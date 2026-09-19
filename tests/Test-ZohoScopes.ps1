#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

# Compile the actual options implementation without external packages or CRM access.
$source = Get-Content (Join-Path $root 'backend/Integrations/Zoho/ZohoOptions.cs') -Raw
if (-not ('SalesPlattform.Backend.Integrations.Zoho.ZohoOptions' -as [type])) {
    Add-Type -TypeDefinition ("using System;`nusing System.Linq;`n" + $source)
}
$options = [SalesPlattform.Backend.Integrations.Zoho.ZohoOptions]::new()
$settings = Get-Content (Join-Path $root 'backend/appsettings.json') -Raw | ConvertFrom-Json
Assert ($options.Scopes -ceq $settings.Zoho.Scopes) 'C# and appsettings scope defaults must match.'
$required = @(
    'ZohoCRM.modules.READ', 'ZohoCRM.modules.emails.READ',
    'ZohoCRM.modules.tasks.CREATE', 'ZohoCRM.modules.tasks.UPDATE',
    'ZohoCRM.notifications.CREATE', 'ZohoCRM.notifications.DELETE',
    'ZohoCRM.users.READ', 'ZohoCRM.org.READ',
    'ZohoCRM.settings.modules.READ', 'ZohoCRM.settings.fields.READ',
    'ZohoCRM.settings.layouts.READ', 'ZohoCRM.settings.pipeline.READ',
    'ZohoCRM.settings.related_lists.READ'
)
function Assert-HistoryScopes([string[]]$Scopes) {
    foreach ($scope in $required) {
        Assert (@($Scopes | Where-Object { $_ -ceq $scope }).Count -eq 1) "Expected exactly one $scope."
    }
    Assert ($Scopes -cnotcontains 'ZohoCRM.modules.ALL') 'Do not add full module access.'
    Assert ($Scopes -notcontains 'ZohoCRM.modules.DealHistory.READ') 'Never send the unsupported history grant, including from old overrides.'
    $writeGrants = @('ZohoCRM.modules.tasks.CREATE', 'ZohoCRM.modules.tasks.UPDATE', 'ZohoCRM.notifications.CREATE', 'ZohoCRM.notifications.DELETE')
    Assert (@($Scopes | Where-Object { $_ -match '\.(CREATE|UPDATE|DELETE|ALL)$' -and $_ -cnotin $writeGrants }).Count -eq 0) 'Only existing task/hook writes are permitted.'
}
Assert-HistoryScopes $options.GetScopes()
Assert ($options.GetScopes().Count -eq $required.Count) 'Defaults must contain exactly the documented integration grants.'

# Old/direct environment overrides retain every required grant, not just module reads.
$options.Scopes = ' ZohoCRM.modules.accounts.READ, ,ZohoCRM.modules.accounts.READ '
$effective = $options.GetScopes()
Assert-HistoryScopes $effective
Assert ($effective.Count -eq $required.Count + 1) 'Trim/deduplicate, retain configured rights and supply all integration grants.'
$options.Scopes = 'ZohoCRM.modules.DealHistory.READ,ZohoCRM.modules.deals.READ,ZohoCRM.modules.DealHistory.READ'
Assert-HistoryScopes $options.GetScopes()
Assert ($options.GetScopes().Count -eq $required.Count + 1) 'Remove unsupported history grants rather than forwarding them.'
$options.Scopes = 'ZohoCRM.modules.READ, ZohoCRM.modules.READ, ZohoCRM.settings.roles.READ'
Assert-HistoryScopes $options.GetScopes()
Assert ($options.GetScopes() -ccontains 'ZohoCRM.settings.roles.READ') 'Preserve additional explicitly configured rights.'
$options.Scopes = 'ZohoCRM.modules.accounts.READ, zohocrm.modules.dealhistory.read'
Assert-HistoryScopes $options.GetScopes()
foreach ($empty in @('', ' , , ', 'ZohoCRM.modules.DealHistory.READ')) {
    $options.Scopes = $empty
    Assert ($options.GetScopes().Count -eq 0) 'Empty scope configuration must not silently acquire defaults.'
    $rejected = $false
    try { $options.ValidateForOAuth() } catch { $rejected = $_.Exception.ToString().Contains('Zoho:Scopes') }
    Assert $rejected 'OAuth must reject an empty scope configuration.'
}

$previousScopes = $env:ZOHO_SCOPES
try {
    foreach ($override in @('', 'ZohoCRM.modules.accounts.READ', ' ZohoCRM.modules.DealHistory.READ,ZohoCRM.modules.deals.READ,ZohoCRM.modules.DealHistory.READ ')) {
        $env:ZOHO_SCOPES = $override
        $entries = @(& (Join-Path $root 'deploy/local-environment.ps1') -AppRoot $root -Plan @{ Names = @{ Platform = 'scope-test' } })
        $scopeEntry = @($entries | Where-Object name -CEQ 'Zoho__Scopes')
        Assert ($scopeEntry.Count -eq 1) 'Deployment must supply exactly one scope setting.'
        $deployed = $scopeEntry[0].value -split ','
        Assert-HistoryScopes $deployed
        foreach ($scope in @('ZohoCRM.modules.tasks.CREATE', 'ZohoCRM.modules.tasks.UPDATE', 'ZohoCRM.notifications.CREATE', 'ZohoCRM.notifications.DELETE')) {
            Assert ($deployed -ccontains $scope) 'Preserve existing task/hook grants.'
        }
    }
} finally {
    $env:ZOHO_SCOPES = $previousScopes
}
Write-Host 'Zoho scopes: defaults, runtime/deployment overrides, deduplication, least privilege and empty configuration passed.'
