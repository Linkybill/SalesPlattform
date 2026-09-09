#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('local', 'ax42-1')][string]$Target,
    [string]$Environment = 'dev', [string]$ClusterNamePrefix = '', [string]$Tag = '',
    [switch]$Preview, [switch]$NoCache, [string]$SshConfig = '', [string]$PlatformRepositoryRoot = ''
)
$ErrorActionPreference = 'Stop'
$platformRoot = if ($PlatformRepositoryRoot) { (Resolve-Path -LiteralPath $PlatformRepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..\..\IdentityPlattform')).Path }
$parameters = @{} + $PSBoundParameters
$parameters.Remove('PlatformRepositoryRoot')
& (Join-Path $platformRoot 'deploy\invoke-app-deployment.ps1') @parameters -AppKey 'sales-plattform' -AppRoot $PSScriptRoot
