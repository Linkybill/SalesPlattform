#Requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Preview','BuildPublish','Notify','Apply')][string]$Phase = 'Preview',
    [string]$ReceiptDirectory = '', [switch]$NoCache, [switch]$NoBrowser,
    [string]$PlatformRepositoryRoot = ''
)
$ErrorActionPreference = 'Stop'
$platformRoot = if ($PlatformRepositoryRoot) { (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path }
$parameters = @{} + $PSBoundParameters
$parameters.Remove('PlatformRepositoryRoot')
& (Join-Path $platformRoot 'deploy\publish-app.ps1') @parameters -AppKey 'sales-plattform' -AppRoot $PSScriptRoot
