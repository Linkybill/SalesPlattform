#Requires -Version 7.2
# Consumer-only contracts: the platform repository tests the shared engine.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Https([string]$Value) {
    $url = $null
    Assert ([Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$url)) 'Absolute URL required.'
    Assert ($url.Scheme -eq 'https' -and -not $url.UserInfo -and -not $url.Query -and -not $url.Fragment -and $Value -notmatch '[\s\\]') 'Public URL must be HTTPS without credentials/query/fragment.'
}
$build = Get-Content (Join-Path $root 'appsettings.Build.json') -Raw | ConvertFrom-Json
Assert ($build.Version -eq 1 -and $build.ApplicationKey -ceq 'sales-plattform') 'Wrong build definition.'
Assert ($build.Manifest -ceq 'backend/manifest.json' -and $build.Bootstrap -ceq 'kubernetes/bootstrap.yaml') 'Wrong manifest/bootstrap source.'
Assert (@($build.Components.PSObject.Properties.Name | Sort-Object) -join ',' -ceq 'backend,frontend') 'Exactly backend/frontend required.'
foreach ($component in @('backend', 'frontend')) {
    Assert (Test-Path (Join-Path $root $build.Components.$component.Dockerfile)) 'Declared Dockerfile missing.'
    Assert ($build.Components.$component.PackageToken -eq $true) 'Private package BuildKit secret required.'
}
foreach ($entry in @('deploy-all.ps1', 'rebuild-all.ps1', 'start-all.ps1')) {
    $path = Join-Path $root $entry
    if (-not (Test-Path $path)) { continue }
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    Assert ($errors.Count -eq 0) "Invalid entry syntax: $entry"
    $source = Get-Content $path -Raw
    Assert ($source -notmatch 'docker|k3d|kubectl|helm|Application manifest registered|Test-AppDeploymentHttps|verify-app-https|Start-Sleep') 'App entry must delegate all build/rollout mechanics.'
    $delegate = if ($entry -eq 'deploy-all.ps1') { 'invoke-app-deployment.ps1' } else { 'deploy-all.ps1' }
    Assert ($source.Contains($delegate)) 'Shared deployment delegation missing.'
}
$entry = Get-Content (Join-Path $root 'deploy-all.ps1') -Raw
Assert ($entry.Contains("-AppKey 'sales-plattform' -AppRoot") -and $entry.Contains('$PSScriptRoot')) 'Entrypoint must select only its own application.'
$deployment = Get-Content (Join-Path $root 'appsettings.Deployment.json') -Raw | ConvertFrom-Json
foreach ($target in @('local', 'ax42-1', 'ax42-2')) {
    $frontend = $deployment.Targets.$target.Environments.dev.Urls.Frontend
    Assert ($frontend.Port -eq 3003 -and -not $frontend.Path -and -not $frontend.BaseUrlRef) "Incorrect own app origin: $target"
}
foreach ($file in @('frontend/Dockerfile', 'frontend/.env.example', 'frontend/src/main.tsx', 'kubernetes/bootstrap.yaml', 'backend/manifest.json', 'backend/appsettings.json')) {
    $content = Get-Content (Join-Path $root $file) -Raw
    Assert ($content -notmatch 'http://(localhost|127\.0\.0\.1|176\.9\.57\.203):(3000|3001|3002|3003|3101|8080|8081)\b|/apps/sales-plattform') "Obsolete public URL: $file"
}
$manifest = Get-Content (Join-Path $root $build.Manifest) -Raw | ConvertFrom-Json
$origin = 'https://127.0.0.1:3003'
Assert (@($manifest.entryPoints | Where-Object type -eq 'web').Count -eq 1 -and $manifest.entryPoints[0].url -eq "$origin/") 'Manifest entry mismatch.'
Assert (($manifest.client.redirectUris -join ',') -eq "$origin/auth/callback,$origin/auth/silent-callback") 'OIDC callbacks mismatch.'
Assert (($manifest.client.postLogoutRedirectUris -join ',') -eq "$origin/auth/logout-callback") 'Logout callback mismatch.'
Assert (($manifest.client.webOrigins -join ',') -eq $origin) 'OIDC web origin mismatch.'
foreach ($url in @($manifest.entryPoints.url) + @($manifest.client.redirectUris) + @($manifest.client.postLogoutRedirectUris) + @($manifest.client.webOrigins)) { Assert-Https $url }
$settings = Get-Content (Join-Path $root 'backend/appsettings.json') -Raw | ConvertFrom-Json
Assert ($settings.Authentication.Issuer -eq 'https://localhost:8080/realms/identity-platform' -and $settings.Authentication.RequireHttpsMetadata) 'Backend authentication must use HTTPS.'
Write-Host 'sales-plattform: own build definition, common deployment delegation, public HTTPS and OIDC contracts passed.'
