param(
    [string]$Tag = '0.1.0',
    [string]$Namespace = 'identity-platform',
    [string]$KubeContext = '',
    [string]$PlatformRepositoryRoot = '',
    [switch]$NoCache
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$defaultKubeConfig = Join-Path $env:USERPROFILE '.kube\config'
if (Test-Path -LiteralPath $defaultKubeConfig) {
    $env:KUBECONFIG = $defaultKubeConfig
}

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

$backendImage = "identity-platform/sales-plattform-backend:$Tag"
$frontendImage = "identity-platform/sales-plattform-frontend:$Tag"
$applicationKey = 'sales-plattform'
$platformApiUrl = "http://identity-platform-api.$Namespace.svc.cluster.local:8080"
$platformRoot = if ([string]::IsNullOrWhiteSpace($PlatformRepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path
} else {
    (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path
}
$platformAppSettingsPath = Join-Path $platformRoot 'src\IdentityPlatform.Api\appsettings.json'
if (-not (Test-Path -LiteralPath $platformAppSettingsPath)) {
    throw "Die Plattformkonfiguration wurde nicht gefunden: $platformAppSettingsPath"
}
$platformAppSettings = Get-Content -LiteralPath $platformAppSettingsPath -Raw | ConvertFrom-Json
$databaseClusterName = [string]$platformAppSettings.DatabaseClusters.ClusterName
if ([string]::IsNullOrWhiteSpace($databaseClusterName)) {
    throw 'DatabaseClusters.ClusterName fehlt in der Plattformkonfiguration.'
}
$keycloakServiceHost = "$databaseClusterName-keycloak"
$rabbitMqServiceHost = "$databaseClusterName-rabbitmq"
$dockerContext = (Resolve-Path $PSScriptRoot).Path
$bootstrapPath = Join-Path $PSScriptRoot 'kubernetes\bootstrap.yaml'
$zohoRedirectUri = if ([string]::IsNullOrWhiteSpace($env:ZOHO_REDIRECT_URI)) {
    'http://localhost:3101/apps/sales-plattform/api/integrations/zoho/oauth/callback'
} else { $env:ZOHO_REDIRECT_URI }
$zohoFrontendCallbackUrl = if ([string]::IsNullOrWhiteSpace($env:ZOHO_FRONTEND_CALLBACK_URL)) {
    'http://localhost:3101/apps/sales-plattform/'
} else { $env:ZOHO_FRONTEND_CALLBACK_URL }
$zohoScopes = if ([string]::IsNullOrWhiteSpace($env:ZOHO_SCOPES)) {
    'ZohoCRM.modules.READ,ZohoCRM.settings.modules.READ,ZohoCRM.settings.fields.READ'
} else { $env:ZOHO_SCOPES }

function Get-SalesDeployments {
    $output = Invoke-Captured $kubectlCommand get deployments -A `
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

function Ensure-ApplicationSettingsProtectionSecret {
    param(
        [string]$TargetNamespace
    )

    $secretName = 'sales-plattform-secrets'
    try {
        $existing = Invoke-Captured $kubectlCommand get secret -n $TargetNamespace $secretName -o name
        if (-not [string]::IsNullOrWhiteSpace($existing)) {
            return
        }
    } catch {
        # The app-local key is provisioned below on the first local rebuild.
    }

    $bytes = New-Object byte[] 48
    $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($bytes)
    } finally {
        $randomNumberGenerator.Dispose()
    }
    $protectionKey = [Convert]::ToBase64String($bytes)
    Invoke-Checked $kubectlCommand create secret generic $secretName `
        -n $TargetNamespace `
        "--from-literal=APPLICATION_SETTINGS_PROTECTION_KEY=$protectionKey"
    Write-Host "App-lokalen Application-Settings-Schlüssel in $TargetNamespace/$secretName angelegt." -ForegroundColor DarkGray
}

function Ensure-SecretEnvironment {
    param(
        [string]$TargetNamespace,
        [string]$DeploymentName
    )

    $deploymentJson = (Invoke-Captured $kubectlCommand get deployment `
        -n $TargetNamespace $DeploymentName -o json) | ConvertFrom-Json
    $containers = @($deploymentJson.spec.template.spec.containers)
    $containerIndex = -1
    for ($index = 0; $index -lt $containers.Count; $index++) {
        if ($containers[$index].name -eq 'app') {
            $containerIndex = $index
            break
        }
    }
    if ($containerIndex -lt 0) {
        throw "Kein App-Container in Deployment '$TargetNamespace/$DeploymentName' gefunden."
    }

    $envItems = @($containers[$containerIndex].env)
    $obsoleteZohoNames = @(
        'Zoho__ClientId',
        'Zoho__ClientSecret',
        'Zoho__TokenProtectionKey'
    )
    $removeOperations = @()
    for ($index = $envItems.Count - 1; $index -ge 0; $index--) {
        if ($obsoleteZohoNames -contains $envItems[$index].name) {
            $removeOperations += @{
                op = 'remove'
                path = "/spec/template/spec/containers/$containerIndex/env/$index"
            }
        }
    }
    if ($removeOperations.Count -gt 0) {
        $patchJson = ConvertTo-Json -InputObject @($removeOperations) -Depth 10 -Compress
        $patchPath = [IO.Path]::GetTempFileName()
        try {
            [IO.File]::WriteAllText($patchPath, $patchJson)
            Invoke-Checked $kubectlCommand -n $TargetNamespace patch `
                deployment/$DeploymentName --type=json --patch-file $patchPath
        } finally {
            Remove-Item -LiteralPath $patchPath -Force -ErrorAction SilentlyContinue
        }
        $envItems = @($envItems | Where-Object { $obsoleteZohoNames -notcontains $_.name })
    }

    $secretEntries = @(
        [PSCustomObject]@{
            Name = 'IdentityPlatform__RegistrationSecret'
            SecretName = 'identity-platform-secrets'
            Key = 'APPLICATION_REGISTRATION_SECRET'
            Optional = $false
        }
        [PSCustomObject]@{
            Name = 'IdentityPlatform__Database__RegistrationSecret'
            SecretName = 'identity-platform-secrets'
            Key = 'APPLICATION_REGISTRATION_SECRET'
            Optional = $false
        }
        [PSCustomObject]@{
            Name = 'IdentityPlatform__ApplicationSettings__ProtectionKey'
            SecretName = 'sales-plattform-secrets'
            Key = 'APPLICATION_SETTINGS_PROTECTION_KEY'
            Optional = $false
        }
        [PSCustomObject]@{
            Name = 'RabbitMq__Username'
            SecretName = 'identity-platform-secrets'
            Key = 'RABBITMQ_USER'
            Optional = $false
        }
        [PSCustomObject]@{
            Name = 'RabbitMq__Password'
            SecretName = 'identity-platform-secrets'
            Key = 'RABBITMQ_PASSWORD'
            Optional = $false
        }
        [PSCustomObject]@{
            Name = 'Trust__JwksSecret'
            SecretName = 'identity-platform-secrets'
            Key = 'APPLICATION_REGISTRATION_SECRET'
            Optional = $false
        }
    )

    foreach ($entry in $secretEntries) {
        $envIndex = -1
        for ($index = 0; $index -lt $envItems.Count; $index++) {
            if ($envItems[$index].name -eq $entry.Name) {
                $envIndex = $index
                break
            }
        }

        $secretReference = @{
            name = $entry.SecretName
            key = $entry.Key
            optional = $entry.Optional
        }
        $environmentValue = @{
            name = $entry.Name
            valueFrom = @{ secretKeyRef = $secretReference }
        }
        $operation = @{
            op = if ($envIndex -ge 0) { 'replace' } else { 'add' }
            path = if ($envIndex -ge 0) {
                "/spec/template/spec/containers/$containerIndex/env/$envIndex"
            } else {
                "/spec/template/spec/containers/$containerIndex/env/-"
            }
            value = $environmentValue
        }
        $patchJson = ConvertTo-Json -InputObject @($operation) -Depth 10 -Compress
        $patchPath = [IO.Path]::GetTempFileName()
        try {
            [IO.File]::WriteAllText($patchPath, $patchJson)
            Invoke-Checked $kubectlCommand -n $TargetNamespace patch `
                deployment/$DeploymentName --type=json --patch-file $patchPath
        } finally {
            Remove-Item -LiteralPath $patchPath -Force -ErrorAction SilentlyContinue
        }
    }
}

$existingDeployments = @(Get-SalesDeployments)
if ($existingDeployments.Count -eq 0) {
    Invoke-Checked $kubectlCommand apply -f $bootstrapPath
    $deployments = @(Get-SalesDeployments)
} else {
    $deployments = $existingDeployments
}

if ($deployments.Count -eq 0) {
    throw 'Keine SalesPlattform-Deployments konnten gefunden oder angelegt werden.'
}

foreach ($namespace in @($deployments | Where-Object Component -eq 'backend' | Select-Object -ExpandProperty Namespace -Unique)) {
    Ensure-ApplicationSettingsProtectionSecret -TargetNamespace $namespace
}

Write-Host "Stoppe $($deployments.Count) SalesPlattform-Deployment(s) ..." -ForegroundColor Yellow
foreach ($deployment in $deployments) {
    Invoke-Checked $kubectlCommand -n $deployment.Namespace scale deployment/$($deployment.Name) --replicas=0
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

$applicationBaseUrl = if ([string]::IsNullOrWhiteSpace($env:VITE_APPLICATION_BASE_URL)) {
    'http://localhost:3101/apps/sales-plattform'
} else { $env:VITE_APPLICATION_BASE_URL }
$publicPlatformApiUrl = if ([string]::IsNullOrWhiteSpace($env:VITE_PLATFORM_API_BASE_URL)) {
    'http://localhost:3101/platform'
} else { $env:VITE_PLATFORM_API_BASE_URL }
$tenantPortalUrl = if ([string]::IsNullOrWhiteSpace($env:VITE_TENANT_PORTAL_URL)) {
    'http://localhost:3001'
} else { $env:VITE_TENANT_PORTAL_URL }
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
Invoke-Checked $k3dCommand image import --cluster $k3dCluster $backendImage $frontendImage

foreach ($deployment in $deployments) {
    if ($deployment.Component -eq 'backend') {
        Ensure-SecretEnvironment -TargetNamespace $deployment.Namespace -DeploymentName $deployment.Name
        Invoke-Checked $kubectlCommand -n $deployment.Namespace set image deployment/$($deployment.Name) app=$backendImage
        Invoke-Checked $kubectlCommand -n $deployment.Namespace set env deployment/$($deployment.Name) `
            IdentityPlatform__PlatformApiUrl=$platformApiUrl `
            IdentityPlatform__Database__PlatformApiUrl=$platformApiUrl `
            Trust__JwksUrl="$platformApiUrl/internal/trust/jwks" `
            Authentication__Authority="http://${keycloakServiceHost}:8080/realms/identity-platform" `
            Authentication__BackchannelHost=$keycloakServiceHost `
            RabbitMq__Host=$rabbitMqServiceHost `
            Zoho__AccountsUrl=https://accounts.zoho.eu `
            Zoho__ApiUrl=https://www.zohoapis.eu `
            Zoho__RedirectUri=$zohoRedirectUri `
            Zoho__FrontendCallbackUrl=$zohoFrontendCallbackUrl `
            Zoho__Scopes=$zohoScopes
    } elseif ($deployment.Component -eq 'frontend') {
        Invoke-Checked $kubectlCommand -n $deployment.Namespace set image deployment/$($deployment.Name) app=$frontendImage
    }
}

Write-Host 'Starte alle SalesPlattform-Deployments zunächst als Bootstrap ...' -ForegroundColor Cyan
foreach ($deployment in $deployments) {
    Invoke-Checked $kubectlCommand -n $deployment.Namespace scale deployment/$($deployment.Name) --replicas=1
    Invoke-Checked $kubectlCommand -n $deployment.Namespace rollout restart deployment/$($deployment.Name)
}

foreach ($deployment in $deployments) {
    Invoke-Checked $kubectlCommand -n $deployment.Namespace rollout status deployment/$($deployment.Name) --timeout=180s
}

foreach ($deployment in $deployments | Where-Object Component -eq 'backend') {
    $podOutput = Invoke-Captured $kubectlCommand get pods -n $deployment.Namespace `
        -l "identity-platform.io/app-key=$applicationKey,identity-platform.io/component-key=backend" `
        -o 'custom-columns=NAME:.metadata.name,PHASE:.status.phase' --no-headers
    $podRows = @($podOutput -split "`r?`n")
    $podName = $podRows |
        ForEach-Object {
            $columns = $_ -split '\s+'
            if ($columns.Count -ge 2 -and $columns[1] -eq 'Running') { $columns[0] }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($podName)) {
        throw "Kein laufender Backend-Pod für '$($deployment.Namespace)/$($deployment.Name)' gefunden."
    }
    $logs = Invoke-Captured $kubectlCommand logs -n $deployment.Namespace pod/$podName --tail=250
    if ($logs -notmatch 'Received HTTP response headers.*200' -or
        $logs -notmatch 'Application manifest registered' -or
        $logs -notmatch 'Application started') {
        Write-Host $logs
        throw "Die SalesPlattform-Instanz '$($deployment.Namespace)/$($deployment.Name)' hat sich nicht erfolgreich registriert."
    }
}

Write-Host 'Rebuild aller SalesPlattform-Instanzen erfolgreich abgeschlossen.' -ForegroundColor Green
Write-Host 'Die endgültigen Replica-Zahlen werden nach der Registrierung durch den DeploymentController übernommen.' -ForegroundColor DarkGray
Invoke-Checked $kubectlCommand get deployments -A -l "identity-platform.io/app-key=$applicationKey" -o wide
