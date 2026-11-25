#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sales-deploy-contract-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'deploy') -Force
@'
param($Phase, $NoCache, $AppKey, $AppRoot, $ReceiptDirectory, $WorkingTree)
if ($env:SALES_TEST_PUBLISH_FAIL -eq '1') { throw 'publication rejected' }
[pscustomobject]@{ Route='publication'; WorkingTree=[bool]$WorkingTree; Phase=$Phase; NoCache=[bool]$NoCache; AppKey=$AppKey; AppRoot=$AppRoot; Receipt=$ReceiptDirectory }
'@ | Set-Content (Join-Path $fixture 'deploy/publish-app.ps1')
@'
param($Target, $AppKey, $AppRoot, [switch]$Preview)
[pscustomobject]@{ Route='local'; Target=$Target; AppKey=$AppKey }
'@ | Set-Content (Join-Path $fixture 'deploy/invoke-app-deployment.ps1')
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$entry = Join-Path $root 'deploy-all.ps1'
$previous = $env:SALES_TEST_PUBLISH_FAIL
try {
    $result = & $entry -PlatformRepositoryRoot $fixture
    Assert ($result.Route -eq 'publication' -and $result.WorkingTree -and $result.Phase -eq 'Apply' -and $result.AppKey -eq 'sales-plattform' -and $result.AppRoot -eq $root) 'Default deploy must publish this app.'
    $result = & $entry -Target ax42-1 -PlatformTarget ax42-1 -Environment dev -NoCache -ReceiptDirectory 'receipt-test' -PlatformRepositoryRoot $fixture
    Assert ($result.Route -eq 'publication' -and $result.NoCache -and $result.Receipt -eq 'receipt-test') 'Existing online invocation must publish with its build options.'
    $result = & $entry -Preview -PlatformRepositoryRoot $fixture
    Assert ($result.Phase -eq 'Preview') 'Preview must not publish.'
    $result = & (Join-Path $root 'deploy-remote.ps1') -Preview -NoCache -ReceiptDirectory 'remote-receipt' -PlatformRepositoryRoot $fixture
    Assert ($result.Route -eq 'publication' -and $result.Phase -eq 'Preview' -and $result.NoCache -and $result.Receipt -eq 'remote-receipt') 'Remote alias must use publication and forward options.'
    $result = & $entry -Target local -Preview -PlatformRepositoryRoot $fixture
    Assert ($result.Route -eq 'local') 'Local development must retain its local path.'
    foreach ($invalid in @(@{Target='another-server'}, @{SshConfig='custom'}, @{Tag='old'}, @{Installation='tenant'}, @{Environment='prod'})) {
        $failed = $false
        try { & $entry @invalid -PlatformRepositoryRoot $fixture | Out-Null } catch { $failed = $true }
        Assert $failed 'Unsupported scope must not silently publish globally.'
    }
    $env:SALES_TEST_PUBLISH_FAIL = '1'
    $failed = $false
    try { & $entry -PlatformRepositoryRoot $fixture | Out-Null } catch { $failed = $_.Exception.Message -eq 'publication rejected' }
    Assert $failed 'Publication failure must propagate without bootstrap fallback.'
    $failed = $false
    try { & (Join-Path $root 'deploy-remote.ps1') -PlatformRepositoryRoot $fixture | Out-Null } catch { $failed = $_.Exception.Message -eq 'publication rejected' }
    Assert $failed 'Remote alias must propagate publication failure.'
    Write-Host 'Deploy publication routing checks passed.'
} finally {
    $env:SALES_TEST_PUBLISH_FAIL = $previous
    # Exact files created by this test only; no recursive deletion.
    Remove-Item -LiteralPath (Join-Path $fixture 'deploy/publish-app.ps1'), (Join-Path $fixture 'deploy/invoke-app-deployment.ps1') -Force
    Remove-Item -LiteralPath (Join-Path $fixture 'deploy'), $fixture
}
