#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('local')][string]$Target = 'local',
    [switch]$Preview,
    [string]$Tag = '',
    [string]$Environment = 'dev',
    [string]$ClusterNamePrefix = '',
    [string]$Namespace = 'identity-platform',
    [string]$KubeContext = '',
    [string]$PlatformRepositoryRoot = '',
    [switch]$NoCache
)

$ErrorActionPreference = 'Stop'
if (-not $env:IDENTITY_PLATFORM_DEPLOYMENT_PLAN) {
    if ($KubeContext -or $PSBoundParameters.ContainsKey('Namespace')) { throw 'Standalone: Target/Environment/ClusterNamePrefix statt separatem Namespace/Kontext verwenden.' }
    & (Join-Path $PSScriptRoot 'deploy-all.ps1') -Target $Target -PlatformTarget $Target -Environment $Environment -ClusterNamePrefix $ClusterNamePrefix `
        -Tag $Tag -Preview:$Preview -NoCache:$NoCache -PlatformRepositoryRoot $PlatformRepositoryRoot
    return
}
$previousLocation = Get-Location
$previousPath = $env:PATH
$previousKubeConfig = $env:KUBECONFIG
$lifecycleLock = $null
try {
Set-Location -LiteralPath $PSScriptRoot
if ($env:PATHEXT -notmatch '(?i)(^|;)\.EXE(;|$)') { $env:PATHEXT = "$env:PATHEXT;.EXE" }
$platformRoot = if ($PlatformRepositoryRoot) { (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path }
. (Join-Path $platformRoot 'deploy\deployment-plan.ps1')
. (Join-Path $platformRoot 'deploy\app-profile.ps1')
if ($Preview) {
    Resolve-DeploymentPlan @{ Root=$platformRoot; Target=$Target; Environment=$Environment;
        ClusterNamePrefix=$ClusterNamePrefix; AppPaths=@{ 'sales-plattform'=$PSScriptRoot } } |
        ConvertTo-Json -Depth 30
    return
}
if (-not (Get-DeploymentPlan)) {
    $lockPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'IdentityPlatform\Deployments\local.lock'
    if (-not (Test-Path (Split-Path $lockPath))) { throw 'Zuerst deploy-all -Target local -Environment dev verwenden.' }
    try { $lifecycleLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Ein lokaler Lifecycle-Lauf ist bereits aktiv; kein paralleler Sales-Rebuild.' }
}
$plan = Resolve-AppRebuildPlan -PlatformRoot $platformRoot -AppKey 'sales-plattform' -AppRoot $PSScriptRoot -Environment $Environment -ClusterNamePrefix $ClusterNamePrefix
if (($PSBoundParameters.ContainsKey('Namespace') -and $Namespace -ne $plan.Names.Namespace) -or
    ($KubeContext -and $KubeContext -ne $plan.KubeContext)) { throw 'App und Plattform muessen dieselbe Installation verwenden.' }
$Namespace = $plan.Names.Namespace
$KubeContext = $plan.KubeContext

$defaultKubeConfig = Join-Path $env:USERPROFILE '.kube\config'
if ($env:KUBECONFIG -and $env:KUBECONFIG -ne $defaultKubeConfig) {
    throw 'Abweichendes KUBECONFIG zuerst bewusst entfernen; keine Zielkonfiguration wird ueberschrieben.'
}
if (Test-Path -LiteralPath $defaultKubeConfig) {
    $env:KUBECONFIG = $defaultKubeConfig
}
. (Join-Path $platformRoot 'deploy\kubernetes\operator-kubectl.ps1')
Initialize-PlatformKubectl
Assert-AppRebuildPlatformReady -Plan $plan

function Invoke-Captured {
    $Command = [string]$args[0]
    $Arguments = @($args | Select-Object -Skip 1)

    $stdoutPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()
    try {
        $process = Start-Process -FilePath $Command `
            -ArgumentList $Arguments `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -NoNewWindow `
            -Wait -PassThru
        $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
        if ($null -eq $stdout) { $stdout = '' }
        if ($null -eq $stderr) { $stderr = '' }
        $output = (([string]$stdout) + ([string]$stderr)).Trim()
        if ($process.ExitCode -ne 0) {
            throw "Befehl fehlgeschlagen: $Command $($Arguments -join ' ')`n$output"
        }
        return $output
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Checked {
    $output = Invoke-Captured @args
    if (-not [string]::IsNullOrWhiteSpace($output)) {
        Write-Host $output
    }
}

$dockerCommand = if (Get-Command docker.exe -ErrorAction SilentlyContinue) { 'docker.exe' } else { 'docker' }
$kubectlCommand = if (Get-Command kubectl.exe -ErrorAction SilentlyContinue) { 'kubectl.exe' } else { 'kubectl' }
$k3dCommand = if (Get-Command k3d.exe -ErrorAction SilentlyContinue) { 'k3d.exe' } else { 'k3d' }

foreach ($command in @($dockerCommand, $kubectlCommand, $k3dCommand)) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command wurde nicht gefunden."
    }
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_PACKAGES_TOKEN)) {
    throw 'GITHUB_PACKAGES_TOKEN fehlt; IdentityPlatform.Shared kann nicht aus GitHub Packages wiederhergestellt werden.'
}

if (-not [string]::IsNullOrWhiteSpace($KubeContext)) {
    Invoke-Checked $kubectlCommand config use-context $KubeContext
}

$currentContext = if (-not [string]::IsNullOrWhiteSpace($KubeContext)) {
    $KubeContext.Trim()
} else {
    (Invoke-Captured $kubectlCommand config current-context).Trim()
}
if ([string]::IsNullOrWhiteSpace($currentContext)) {
    throw 'Kein aktiver Kubernetes-Kontext ist gesetzt.'
}

if ($currentContext -notmatch '^k3d-(.+)$') {
    throw "Der Kontext '$currentContext' ist kein lokaler K3d-Kontext. Für einen externen Cluster muss zuerst eine Registry-Konfiguration ergänzt werden."
}

$k3dCluster = $Matches[1]
Invoke-Checked $kubectlCommand cluster-info

# The controller reconciles the registered manifest, not the CLI image tag.
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backend/manifest.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = [string]$manifest.imageTag }
if ([string]::IsNullOrWhiteSpace($Tag) -or $manifest.imageTag -ne $Tag -or
    @($manifest.components | Where-Object imageTag -ne $Tag).Count -gt 0) {
    throw 'Build-Tag und alle imageTag-Werte im Manifest muessen uebereinstimmen. Manifest zuerst aktualisieren.'
}
$backendImage = "$($manifest.components | Where-Object key -eq 'backend' | Select-Object -ExpandProperty imageRepository):$Tag"
$frontendImage = "$($manifest.components | Where-Object key -eq 'frontend' | Select-Object -ExpandProperty imageRepository):$Tag"
$applicationKey = 'sales-plattform'
$platformRoot = if ([string]::IsNullOrWhiteSpace($PlatformRepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path
} else {
    (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path
}
. (Join-Path $platformRoot 'deploy\deployment-plan.ps1')
. (Join-Path $platformRoot 'deploy\app-profile.ps1')
$plan = Resolve-AppRebuildPlan -PlatformRoot $platformRoot -AppKey 'sales-plattform' -AppRoot $PSScriptRoot -Environment $Environment -ClusterNamePrefix $ClusterNamePrefix
if (-not $PSBoundParameters.ContainsKey('Namespace')) { $Namespace = $plan.Names.Namespace }
$platformName = $plan.Names.Platform
if ($plan.Names.Namespace -ne $Namespace -or $plan.KubeContext -ne $currentContext) { throw 'App und Plattform muessen dieselbe Installation verwenden.' }
$appProfile = New-AppDeploymentProfile -Plan $plan -Manifest $manifest
$dockerContext = (Resolve-Path $PSScriptRoot).Path
$bootstrapPath = Join-Path $PSScriptRoot 'kubernetes\bootstrap.yaml'
$zohoWebhookUrl = if ([string]::IsNullOrWhiteSpace($env:ZOHO_WEBHOOK_URL)) {
    ''
} else { $env:ZOHO_WEBHOOK_URL }

function Assert-PublicHttpsUrl {
    param([string]$Name, [string]$Value, [switch]$RootOrigin)
    $parsedUrl = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsedUrl) -or
        $parsedUrl.Scheme -ne 'https' -or -not $parsedUrl.IsWellFormedOriginalString() -or
        -not [string]::IsNullOrEmpty($parsedUrl.UserInfo)) {
        throw "$Name muss eine absolute HTTPS-URL ohne Zugangsdaten sein. Alte HTTP-Umgebungsvariablen aktualisieren oder entfernen."
    }
    if ($RootOrigin -and ($parsedUrl.AbsolutePath -ne '/' -or $parsedUrl.Query -or $parsedUrl.Fragment)) {
        throw "$Name muss eine HTTPS-Origin mit App-Wurzel / ohne Query oder Fragment sein."
    }
}

# Build, runtime configuration, manifest and HTTPS checks consume the same plan.
$applicationBaseUrl = $plan.Urls.'sales-plattform'.Frontend.TrimEnd('/')
$publicPlatformApiUrl = $plan.Urls.'identity-platform'.PlatformApi
$tenantPortalUrl = $plan.Urls.'identity-platform'.TenantPortal
$zohoRedirectUri = $plan.BackendUrls.'sales-plattform'.Zoho__RedirectUri
$zohoFrontendCallbackUrl = $plan.BackendUrls.'sales-plattform'.Zoho__FrontendCallbackUrl
Assert-PublicHttpsUrl -Name 'VITE_APPLICATION_BASE_URL' -Value $applicationBaseUrl -RootOrigin
Assert-PublicHttpsUrl -Name 'VITE_PLATFORM_API_BASE_URL' -Value $publicPlatformApiUrl
Assert-PublicHttpsUrl -Name 'VITE_TENANT_PORTAL_URL' -Value $tenantPortalUrl
Assert-PublicHttpsUrl -Name 'ZOHO_REDIRECT_URI' -Value $zohoRedirectUri
Assert-PublicHttpsUrl -Name 'ZOHO_FRONTEND_CALLBACK_URL' -Value $zohoFrontendCallbackUrl

# OAuth defaults remain app-owned.
$zohoSettings = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backend\appsettings.json') -Raw | ConvertFrom-Json).Zoho
$defaultZohoScopes = $zohoSettings.Scopes -split ','
$requiredZohoScopes = $defaultZohoScopes | Where-Object { $_ -match '\.(CREATE|UPDATE|DELETE)$' }
$configuredZohoScopes = if ([string]::IsNullOrWhiteSpace($env:ZOHO_SCOPES)) {
    $defaultZohoScopes
} else {
    $env:ZOHO_SCOPES -split ','
}
$zohoScopes = @($configuredZohoScopes + $requiredZohoScopes |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique) -join ','

function Get-SalesDeployments {
    $output = Invoke-Captured $kubectlCommand get deployments -n $Namespace `
        -l "identity-platform.io/app-key=$applicationKey" `
        -o 'custom-columns=NAMESPACE:.metadata.namespace,NAME:.metadata.name,COMPONENT:.metadata.labels.identity-platform\.io/component-key' `
        --no-headers
    $rows = @($output -split "`r?`n")

    foreach ($row in $rows) {
        $parts = $row.ToString().Trim() -split '\s+'
        if ($parts.Count -ge 3) {
            [PSCustomObject]@{
                Namespace = $parts[0]
                Name      = $parts[1]
                Component = $parts[2]
            }
        }
    }
}

$existingDeployments = @(Get-SalesDeployments)
if ($existingDeployments.Count -eq 0) {
    if ($plan) { Initialize-AppBootstrap -Plan $plan -Path $bootstrapPath }
    else { Invoke-Checked $kubectlCommand apply -f $bootstrapPath }
    $deployments = @(Get-SalesDeployments)
} else {
    $deployments = $existingDeployments
}

if ($deployments.Count -eq 0) {
    throw 'Keine SalesPlattform-Deployments konnten gefunden oder angelegt werden.'
}

$commonBuildArguments = @(
    'build',
    '--progress=plain'
)
if ($NoCache) { $commonBuildArguments += '--no-cache' }

Write-Host "Baue Backend: $backendImage ..." -ForegroundColor Cyan
$backendBuildArguments = $commonBuildArguments + @(
    '--secret', 'id=github_packages_token,env=GITHUB_PACKAGES_TOKEN',
    '--tag', $backendImage,
    '--file', 'backend/Dockerfile',
    '.'
)
Push-Location $dockerContext
try {
    Invoke-Checked $dockerCommand @backendBuildArguments
} finally {
    Pop-Location
}

Write-Host "Baue Frontend: $frontendImage ..." -ForegroundColor Cyan
$frontendBuildArguments = $commonBuildArguments + @(
    '--secret', 'id=github_packages_token,env=GITHUB_PACKAGES_TOKEN',
    '--tag', $frontendImage,
    '--file', 'frontend/Dockerfile',
    '--build-arg', "VITE_API_BASE_URL=$applicationBaseUrl",
    '--build-arg', "VITE_PLATFORM_API_BASE_URL=$publicPlatformApiUrl",
    '--build-arg', "VITE_TENANT_PORTAL_URL=$tenantPortalUrl",
    '.'
)
Push-Location $dockerContext
try {
    Invoke-Checked $dockerCommand @frontendBuildArguments
} finally {
    Pop-Location
}

Write-Host "Importiere Images in K3d-Cluster '$k3dCluster' ..." -ForegroundColor Cyan
$importLock = New-Object System.Threading.Mutex($false, "Local\IdentityPlatform.K3dImport.$k3dCluster")
$importLockHeld = $false
try {
    $importLockHeld = $importLock.WaitOne([TimeSpan]::FromMinutes(10))
    if (-not $importLockHeld) { throw 'Zeitlimit beim Warten auf einen anderen K3d-Image-Import.' }
    Invoke-Checked $k3dCommand image import --cluster $k3dCluster $backendImage $frontendImage
} finally {
    if ($importLockHeld) { $importLock.ReleaseMutex() }
    $importLock.Dispose()
}

# One image/configuration patch; existing replica counts belong to controller/HPA.
# Optional Sales-only settings join that same patch, never a second rollout.
$appProfile.patches.backend.spec.template.spec.containers[0].env += @(
    @{ name = 'SalesNotifications__Mail__Host'; value = "$platformName-mailpit"; valueFrom = $null },
    @{ name = 'SalesNotifications__Mail__Port'; value = '1025'; valueFrom = $null },
    @{ name = 'Zoho__WebhookUrl'; value = $zohoWebhookUrl; valueFrom = $null },
    @{ name = 'Zoho__Scopes'; value = $zohoScopes; valueFrom = $null }
)
Install-AppDeploymentProfile -DeploymentProfile $appProfile -Namespace $Namespace -Deployments $deployments -Images @{ backend = $backendImage; frontend = $frontendImage } -Restart
Start-AppBootstrap -Namespace $Namespace -Deployments $deployments

foreach ($deployment in $deployments) {
    Invoke-Checked $kubectlCommand -n $deployment.Namespace rollout status deployment/$($deployment.Name) --timeout=180s
}

foreach ($deployment in $deployments | Where-Object Component -eq 'backend') {
    $deploymentJson = (Invoke-Captured $kubectlCommand get deployment `
        -n $deployment.Namespace $deployment.Name -o json) | ConvertFrom-Json
    $selector = @($deploymentJson.spec.selector.matchLabels.PSObject.Properties |
        ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ','
    $podOutput = Invoke-Captured $kubectlCommand get pods -n $deployment.Namespace `
        -l $selector `
        -o 'custom-columns=NAME:.metadata.name,READY:.status.containerStatuses[0].ready,PHASE:.status.phase' `
        --sort-by=.metadata.creationTimestamp --no-headers
    $podRows = @($podOutput -split "`r?`n")
    $podName = $podRows |
        ForEach-Object {
            $columns = $_ -split '\s+'
            if ($columns.Count -ge 3 -and $columns[1] -eq 'true' -and $columns[2] -eq 'Running') { $columns[0] }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($podName)) {
        throw "Kein laufender Backend-Pod für '$($deployment.Namespace)/$($deployment.Name)' gefunden."
    }
    $logs = Invoke-Captured $kubectlCommand logs -n $deployment.Namespace pod/$podName --tail=250
    if ($logs -notmatch 'Application manifest registered' -or
        $logs -notmatch 'Application started') {
        Write-Host $logs
        throw "Die SalesPlattform-Instanz '$($deployment.Namespace)/$($deployment.Name)' hat sich nicht erfolgreich registriert."
    }
}

if ($plan) { Test-AppDeploymentHttps -Plan $plan -AppKey $applicationKey }
Write-Host 'Rebuild aller SalesPlattform-Instanzen erfolgreich abgeschlossen.' -ForegroundColor Green
Write-Host 'Die endgültigen Replica-Zahlen werden nach der Registrierung durch den DeploymentController übernommen.' -ForegroundColor DarkGray
Invoke-Checked $kubectlCommand get deployments -n $Namespace -l "identity-platform.io/app-key=$applicationKey" -o wide
} finally {
    $env:PATH = $previousPath
    $env:KUBECONFIG = $previousKubeConfig
    Set-Location -LiteralPath $previousLocation.Path
    if ($lifecycleLock) { $lifecycleLock.Dispose() }
}
