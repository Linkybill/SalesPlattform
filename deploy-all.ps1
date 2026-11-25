#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$Target = '', [string]$PlatformTarget = '', [string]$Environment = 'dev',
    [string]$Installation = '', [string]$ClusterNamePrefix = '', [string]$Tag = '',
    [switch]$Preview, [switch]$NoCache, [string]$SshConfig = '', [string]$PlatformRepositoryRoot = '',
    [string]$ReceiptDirectory = ''
)
$ErrorActionPreference = 'Stop'
$platformRoot = if ($PlatformRepositoryRoot) { (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path }
# Online releases must enter the platform publication lifecycle. Updating only
# bootstrap deployments leaves the managed instances on their previous release.
if ($Target -ne 'local') {
    if (($Target -and $Target -ne 'ax42-1') -or
        ($PlatformTarget -and $PlatformTarget -ne 'ax42-1') -or $Environment -ne 'dev' -or
        $Installation -or $ClusterNamePrefix -or $Tag -or $SshConfig) {
        throw 'Online-Publish wird von der Plattform platziert. Installation, ClusterNamePrefix, Tag, SshConfig und abweichende Zielangaben werden nicht unterstuetzt. Fuer lokale Tests -Target local verwenden.'
    }
    Write-Host 'SalesPlattform online: lokal bauen, Images veroeffentlichen und neue Version an die Plattform melden. Keine GitHub Actions; kein direkter SSH-Basis-Rollout.'
    $publication = @{ Phase = $(if ($Preview) { 'Preview' } else { 'Apply' }); NoCache = $NoCache;
        AppKey = 'sales-plattform'; AppRoot = $PSScriptRoot; WorkingTree = $true }
    if ($ReceiptDirectory) { $publication.ReceiptDirectory = $ReceiptDirectory }
    & (Join-Path $platformRoot 'deploy\publish-app.ps1') @publication
    if (-not $?) { throw 'SalesPlattform-Publish fehlgeschlagen.' }
    if (-not $Preview) {
        Write-Host 'Publish beendet. Instanz-Rollout und oeffentliche Version sind separat zu verifizieren; dies ist noch keine Live-Abnahme.'
    }
    return
}
if ($ReceiptDirectory) { throw '-ReceiptDirectory ist nur fuer Online-Publish vorgesehen.' }
$parameters = @{} + $PSBoundParameters
$parameters.Remove('PlatformRepositoryRoot')
$parameters.Remove('ReceiptDirectory')
& (Join-Path $platformRoot 'deploy\invoke-app-deployment.ps1') @parameters -AppKey 'sales-plattform' -AppRoot $PSScriptRoot
