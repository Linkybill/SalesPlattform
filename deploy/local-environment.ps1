# App-specific local defaults only. The shared deployment owns build/rollout.
param($Plan, [string]$AppRoot)
$settings = (Get-Content -LiteralPath (Join-Path $AppRoot 'backend/appsettings.json') -Raw | ConvertFrom-Json).Zoho
$defaults = $settings.Scopes -split ','
$configured = if ([string]::IsNullOrWhiteSpace($env:ZOHO_SCOPES)) { $defaults } else { $env:ZOHO_SCOPES -split ',' }
$scopes = @($configured + @($defaults | Where-Object { $_ -match '\.(CREATE|UPDATE|DELETE)$' }) |
    ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique) -join ','
@{ name = 'SalesNotifications__Mail__Host'; value = "$($Plan.Names.Platform)-mailpit"; valueFrom = $null }
@{ name = 'SalesNotifications__Mail__Port'; value = '1025'; valueFrom = $null }
@{ name = 'Zoho__WebhookUrl'; value = [string]$env:ZOHO_WEBHOOK_URL; valueFrom = $null }
@{ name = 'Zoho__Scopes'; value = $scopes; valueFrom = $null }
