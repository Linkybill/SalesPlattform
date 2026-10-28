#Requires -Version 7.2
[CmdletBinding()]
param([switch]$Preview, [switch]$NoCache)

$ErrorActionPreference = 'Stop'
$platformRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path
$platformHetznerRoot = Join-Path $platformRoot 'deploy\hetzner'
$platformScript = Join-Path $platformHetznerRoot 'deploy-application.ps1'
$sshConfig = Join-Path $env:LOCALAPPDATA 'IdentityPlatform\deploy-admin.sshconfig'
$previousPlan = $env:IDENTITY_PLATFORM_DEPLOYMENT_PLAN

try {
    if (-not (Test-Path -LiteralPath $sshConfig -PathType Leaf)) { throw "SSH-Konfiguration fehlt: $sshConfig" }
    $planJson = (& (Join-Path $PSScriptRoot 'deploy-all.ps1') -Target ax42-1 -PlatformTarget ax42-1 -Environment dev -Preview | Out-String).Trim()
    $plan = $planJson | ConvertFrom-Json
    if ($plan.Target -cne 'ax42-1' -or $plan.PlatformTarget -cne 'ax42-1' -or
        $plan.RuntimeTarget -cne 'hetzner-test' -or $plan.Connection.SshAlias -cne 'identity-hetzner-admin' -or
        $plan.Connection.PublicHost -cne '176.9.57.203') { throw 'Deploymentplan weicht vom erwarteten Ziel ax42-1 ab.' }
    if ($Preview) {
        Write-Host "Direktes Deployment: SalesPlattform -> $($plan.Target)/dev ($($plan.Connection.PublicHost))" -ForegroundColor Cyan
        Write-Host "SSH: $sshConfig"
        Write-Host 'Ein sudo-Passwort wird beim echten Start einmal abgefragt.'
        return
    }
    $containerOs = [string](& docker.exe info --format '{{.OSType}}')
    if ($LASTEXITCODE -ne 0 -or $containerOs.Trim() -cne 'linux') { throw 'Docker Desktop muss mit Linux-Containern laufen.' }
    & ssh.exe -F $sshConfig -o BatchMode=yes -o StrictHostKeyChecking=yes identity-hetzner-admin true
    if ($LASTEXITCODE -ne 0) { throw 'SSH-Verbindung zu ax42-1 fehlgeschlagen.' }

    $source = Get-Content -LiteralPath $platformScript -Raw
    $anchor = ". (Join-Path `$PSScriptRoot 'operator-functions.ps1')"
    if ($source.IndexOf($anchor, [StringComparison]::Ordinal) -lt 0) { throw 'Plattformskript hat sich geaendert; sudo-Adapter kann nicht eingesetzt werden.' }
    $source = $source.Replace($anchor, "$anchor`n. (Join-Path `$script:SalesAppScriptRoot 'deploy-sudo-adapter.ps1')")
    $source = $source.Replace('$PSScriptRoot', '$script:SalesPlatformHetznerRoot')
    $script:SalesPlatformHetznerRoot = $platformHetznerRoot
    $script:SalesAppScriptRoot = $PSScriptRoot
    $env:IDENTITY_PLATFORM_DEPLOYMENT_PLAN = $plan | ConvertTo-Json -Depth 30 -Compress
    $global:SalesDeploySudoPassword = Read-Host 'sudo-Passwort fuer platformadmin (einmalig)' -AsSecureString
    if (-not $global:SalesDeploySudoPassword -or $global:SalesDeploySudoPassword.Length -eq 0) { throw 'Leeres sudo-Passwort.' }
    $runner = [ScriptBlock]::Create($source)
    & $runner -SshConfig $sshConfig -SshAlias $plan.Connection.SshAlias -ExpectedHost $plan.Connection.PublicHost `
        -AppKey sales-plattform -AppRoot $PSScriptRoot -NoCache:$NoCache
    $publicUrl = 'https://176.9.57.203:3003/'
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(4)
    $lastStatus = 'Keine Antwort'
    do {
        try {
            $page = Invoke-WebRequest -Uri $publicUrl -TimeoutSec 20 -SkipHttpErrorCheck
            $lastStatus = "HTTPS $($page.StatusCode)"
            if ($page.StatusCode -eq 200) {
                $asset = [regex]::Match($page.Content, 'assets/index-[A-Za-z0-9_-]+\.js').Value
                if (-not $asset) { throw 'Frontend-Bundle fehlt in der Startseite.' }
                $bundle = Invoke-WebRequest -Uri ($publicUrl + $asset) -TimeoutSec 30 -SkipHttpErrorCheck
                $config = Invoke-WebRequest -Uri ($publicUrl + 'assets/deployment-config.js') -TimeoutSec 20 -SkipHttpErrorCheck
                if ($bundle.StatusCode -eq 200 -and $config.StatusCode -eq 200) {
                    Write-Host "Direktes Deployment und oeffentlicher Einstieg bereit: $publicUrl ($asset)" -ForegroundColor Green
                    return
                }
                $lastStatus = "Bundle HTTPS $($bundle.StatusCode), Konfiguration HTTPS $($config.StatusCode)"
            }
        } catch { $lastStatus = $_.Exception.Message }
        if ([DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Seconds 10 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Kubernetes-Rollout abgeschlossen, aber oeffentlicher Einstieg nicht bereit: $lastStatus ($publicUrl)"
} finally {
    $global:SalesDeploySudoPassword = $null
    $env:IDENTITY_PLATFORM_DEPLOYMENT_PLAN = $previousPlan
}
