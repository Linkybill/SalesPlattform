$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rebuildPath = Join-Path $root 'rebuild-all.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($rebuildPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Rebuild-Skript ist syntaktisch ungueltig.' }

# Load only the pure validator, never execute the deployment script.
$validator = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-PublicHttpsUrl'
}, $true)
if ($null -eq $validator) { throw 'HTTPS-Vorabpruefung fehlt.' }
. ([scriptblock]::Create($validator.Extent.Text))
foreach ($url in @('https://127.0.0.1:3003', 'https://176.9.57.203:3003', 'https://example.org/app')) {
    Assert-PublicHttpsUrl -Name 'test' -Value $url
}
foreach ($url in @('http://127.0.0.1:3003', '//127.0.0.1:3003', '/relative', '', 'https://user:example@127.0.0.1:3003', 'https://localhost/a b')) {
    $rejected = $false
    try { Assert-PublicHttpsUrl -Name 'test' -Value $url } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsichere oder ungueltige URL wurde akzeptiert.' }
}

foreach ($origin in @('https://127.0.0.1:3003', 'https://176.9.57.203:3003')) {
    Assert-PublicHttpsUrl -Name 'app' -Value $origin -RootOrigin
    foreach ($suffix in @('/apps/sales-plattform', '/12345678-1234-4123-8123-123456789abc', '/?tenant=x', '/#auth')) {
        $rejected = $false
        try { Assert-PublicHttpsUrl -Name 'app' -Value "$origin$suffix" -RootOrigin } catch { $rejected = $true }
        if (-not $rejected) { throw 'App-Einstieg muss eine tenantneutrale Root-Origin sein.' }
    }
}

# Evaluate only the URL assignments against two synthetic plans, never the lifecycle.
$urlNames = @('applicationBaseUrl', 'publicPlatformApiUrl', 'tenantPortalUrl', 'zohoRedirectUri', 'zohoFrontendCallbackUrl')
$assignments = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
    $node.Left.VariablePath.UserPath -in $urlNames
}, $true))
if ($assignments.Count -ne $urlNames.Count) { throw 'Oeffentliche URLs muessen eindeutig aus dem Profil stammen.' }
foreach ($publicHost in @('127.0.0.1', '176.9.57.203')) {
    $origin = "https://${publicHost}:3003"
    $plan = @{ Urls = @{
        'sales-plattform' = @{ Frontend = "$origin/" }
        'identity-platform' = @{
            PlatformApi = "https://${publicHost}:9443/profile-api"
            TenantPortal = "https://${publicHost}:3001"
        }
    }; BackendUrls = @{ 'sales-plattform' = @{
        Zoho__RedirectUri = "$origin/api/integrations/zoho/oauth/callback"
        Zoho__FrontendCallbackUrl = "$origin/import"
    } } }
    foreach ($assignment in $assignments) { . ([scriptblock]::Create($assignment.Extent.Text)) }
    if ($applicationBaseUrl -ne $origin -or
        $publicPlatformApiUrl -ne $plan.Urls.'identity-platform'.PlatformApi -or
        $tenantPortalUrl -ne $plan.Urls.'identity-platform'.TenantPortal) {
        throw 'Build-URLs weichen vom Profil ab oder enthalten einen abschliessenden Slash.'
    }
    if ($zohoRedirectUri -ne "$origin/api/integrations/zoho/oauth/callback" -or
        $zohoFrontendCallbackUrl -ne "$origin/import") { throw 'Zoho-Callbacks muessen genau einen Root-Slash verwenden.' }
}

$deployment = Get-Content -LiteralPath (Join-Path $root 'appsettings.Deployment.json') -Raw | ConvertFrom-Json
foreach ($target in @('local', 'ax42-1')) {
    $frontend = $deployment.Targets.$target.Environments.dev.Urls.Frontend
    if ($frontend.Port -ne 3003 -or $frontend.Path -or $frontend.BaseUrlRef) {
        throw "Falsche App-Root-/Port-Zuordnung fuer $target/dev."
    }
}

$rebuild = Get-Content -LiteralPath $rebuildPath -Raw
if ($rebuild.IndexOf('image import --cluster') -gt $rebuild.IndexOf('Install-AppDeploymentProfile -DeploymentProfile')) {
    throw 'Build und Image-Import muessen vor dem Stoppen laufender Apps fertig sein.'
}
if ($rebuild -notmatch 'IdentityPlatform\.K3dImport' -or
    $rebuild -notmatch '\$manifest\.imageTag -ne \$Tag') {
    throw 'Manifest-Tag-Pruefung oder serialisierter K3d-Import fehlt.'
}
$guards = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Assert-PublicHttpsUrl'
}, $true))
if ($guards.Count -ne 5) { throw 'Nicht alle oeffentlichen Build-URLs werden geprueft.' }
foreach ($guard in $guards) {
    if ($guard.Extent.StartOffset -gt $rebuild.IndexOf('Install-AppDeploymentProfile -DeploymentProfile')) { throw 'HTTPS-Pruefung muss vor dem Stoppen der App laufen.' }
}

foreach ($file in @('frontend/Dockerfile', 'frontend/.env.example', 'frontend/src/main.tsx', 'rebuild-all.ps1', 'kubernetes/bootstrap.yaml', 'backend/manifest.json', 'backend/appsettings.json')) {
    $content = Get-Content -LiteralPath (Join-Path $root $file) -Raw
    if ($content -match 'http://(localhost|127\.0\.0\.1|176\.9\.57\.203):(3000|3001|3002|3003|3101|8080|8081)\b' -or
        $content -match '/apps/sales-plattform') { throw "Alte oeffentliche Adresse in $file." }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'backend/manifest.json') -Raw | ConvertFrom-Json
$origin = 'https://127.0.0.1:3003'
if (@($manifest.entryPoints | Where-Object type -eq 'web').Count -ne 1 -or
    $manifest.entryPoints[0].url -ne "$origin/" -or
    ($manifest.client.redirectUris -join ',') -ne "$origin/auth/callback,$origin/auth/silent-callback" -or
    ($manifest.client.postLogoutRedirectUris -join ',') -ne "$origin/auth/logout-callback" -or
    ($manifest.client.webOrigins -join ',') -ne $origin) { throw 'Manifest-Root, Callbacks oder Web-Origin sind falsch.' }
foreach ($url in @($manifest.entryPoints.url) + @($manifest.client.redirectUris) + @($manifest.client.postLogoutRedirectUris) + @($manifest.client.webOrigins)) {
    Assert-PublicHttpsUrl -Name 'manifest' -Value $url
}
$settings = Get-Content -LiteralPath (Join-Path $root 'backend/appsettings.json') -Raw | ConvertFrom-Json
if ($settings.Authentication.Issuer -ne 'https://localhost:8080/realms/identity-platform' -or
    -not $settings.Authentication.RequireHttpsMetadata) { throw 'Oeffentliche Backend-Authentifizierung ist nicht HTTPS.' }
if ($rebuild -notmatch 'New-AppDeploymentProfile' -or $rebuild -notmatch 'Install-AppDeploymentProfile' -or
    $rebuild -match '--replicas=0|--replicas=1|rollout restart|set env') {
    throw 'App runtime must use the shared profile without resetting replicas or parallel env setters.'
}
foreach ($file in @('rebuild-all.ps1', 'start-all.ps1')) {
    $path = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $null = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "PowerShell-Syntaxfehler in $file." }
}
Write-Host 'sales-plattform: HTTPS-Konfiguration, URL-Validierung und Lifecycle-Pruefungen erfolgreich.'
