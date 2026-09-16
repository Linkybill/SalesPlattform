#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$source = Get-Content (Join-Path $root '.github/workflows/release.yml') -Raw
Assert ($source.Contains("'refs/heads/main'") -and -not $source.Contains('APP_CI_BRANCH')) 'Deployments are main-only, without a branch override.'
Assert ($source.Contains('APP_CI_APP_KEY: sales-plattform') -and $source.Contains('APP_CI_TARGET: ax42-1')) 'Wrong app/target.'
Assert ($source.Contains('environment: sales-plattform-ax42-1-dev')) 'Separate protected app environment required.'
Assert ($source -match 'access_only:[\s\S]*?default: false') 'Real deployment is the default, not an access test.'
Assert ($source.Contains('uses: ./.github/workflows/ci.yml')) 'Run current app CI before publishing/deploying.'
Assert ($source.Contains('deploy-application.ps1') -and $source.Contains('app-ci-release.mjs')) 'Delegate deployment to reviewed central tooling.'
Assert ($source -notmatch 'send-release|deploy-app-remote|PUBLIC_APPLICATION_URL|DEPLOY_SSH_KEY|sudo |StrictHostKeyChecking=no|insecure-skip-tls') 'No legacy shell deployment or unverified transport.'
$beforeDeploy = ($source -split '(?m)^  deploy:')[0]
Assert ($beforeDeploy -notmatch 'secrets\.APP_CI_') 'Deployment credentials belong only to the protected deploy job.'
foreach ($secret in @('APP_CI_KUBECONFIG', 'APP_CI_SSH_KEY', 'APP_CI_KNOWN_HOSTS')) {
    Assert ($source.Contains("secrets.$secret") -and -not $source.Contains("vars.$secret")) 'Credentials must use secrets, not variables.'
}
foreach ($use in [regex]::Matches($source, 'uses: ([^\s#]+)')) {
    Assert ($use.Groups[1].Value -match '^\./\.github/workflows/ci.yml$|^[a-zA-Z0-9_/-]+@[a-f0-9]{40}$') 'Pin external actions by commit.'
}
Assert ($source.Contains('steps.backend.outputs.digest') -and $source.Contains('steps.frontend.outputs.digest')) 'Deploy both exact image digests.'
Assert ($source.Contains('cancel-in-progress: false')) 'Do not interrupt a running rollout.'
Write-Host 'sales-plattform: manual app-only deployment, target, secret boundaries and immutable action/image contracts passed.'
