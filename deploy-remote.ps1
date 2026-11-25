#Requires -Version 7.4
# Compatibility entry: online deployments use the platform publication lifecycle.
[CmdletBinding()]
param(
    [switch]$Preview, [switch]$NoCache,
    [string]$ReceiptDirectory = '', [string]$PlatformRepositoryRoot = ''
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'deploy-all.ps1') -Target ax42-1 -PlatformTarget ax42-1 -Environment dev @PSBoundParameters
if (-not $?) { throw 'Sales-Publish fehlgeschlagen.' }
