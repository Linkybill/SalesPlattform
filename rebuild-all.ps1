#Requires -Version 7.2
# Compatibility alias. All build/deployment logic is centrally maintained.
[CmdletBinding()]
param(
    [ValidateSet('local', 'local-k3s')][string]$Target = 'local',
    [string]$Environment = 'dev', [string]$ClusterNamePrefix = '', [string]$Tag = '',
    [switch]$Preview, [switch]$NoCache, [string]$PlatformRepositoryRoot = '',
    [string]$Namespace = '', [string]$KubeContext = ''
)
if ($Namespace -or $KubeContext) { throw 'Use Target/Environment/ClusterNamePrefix; namespace/context come only from the deployment plan.' }
& (Join-Path $PSScriptRoot 'deploy-all.ps1') -Target local -PlatformTarget local -Environment $Environment `
    -ClusterNamePrefix $ClusterNamePrefix -Tag $Tag -Preview:$Preview -NoCache:$NoCache -PlatformRepositoryRoot $PlatformRepositoryRoot
