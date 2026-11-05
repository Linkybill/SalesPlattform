#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
function Assert([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
$source = Get-Content (Join-Path $root '.github/workflows/release.yml') -Raw
Assert ($source.Contains("'refs/heads/main'") -and $source.Contains("'workflow_dispatch'") -and $source.Contains("-cne 'publish'")) 'Only an explicit main publication is supported.'
Assert ($source.Contains('APP_RELEASE_APP_KEY: sales-plattform')) 'Wrong publication app.'
Assert ($source.Contains('APP_RELEASE_IMAGE_REPOSITORY: ghcr.io/hammer2fall/identityplattform-sales-plattform')) 'Publish only to the central application registry prefix.'
Assert ($source.Contains('uses: ./.github/workflows/ci.yml')) 'App CI must precede publication.'
Assert ($source.Contains('app-ci-release.mjs') -and $source.Contains('app-release-publication.mjs') -and $source.Contains('notify-app-release.ps1')) 'Use reviewed central build, candidate and notification tooling.'
Assert ($source -notmatch 'APP_CI_|deploy-application.ps1|setup-helm|setup-kubectl|access_only|publish_only|send-release|deploy-app-remote|DEPLOY_SSH_KEY|sudo |StrictHostKeyChecking=no|insecure-skip-tls') 'Publication must not have a direct cluster deployment path or credentials.'
Assert ($source.Contains('environment: application-release') -and $source.Contains('id-token: write')) 'Protect the release publisher environment and enable GitHub OIDC.'
Assert ($source.Contains('vars.CENTRAL_REGISTRY_USERNAME') -and $source.Contains('secrets.CENTRAL_REGISTRY_TOKEN')) 'Central registry publishing needs explicit scoped credentials.'
Assert ($source.Contains('vars.APPLICATION_RELEASE_PLATFORM_URL')) 'Platform publication must use the configured HTTPS origin.'
$images = ($source -split '(?m)^  images:')[1] -split '(?m)^  notify:' | Select-Object -First 1
$notify = ($source -split '(?m)^  notify:')[1]
Assert ($images.Contains('environment: application-release') -and -not $images.Contains('id-token: write')) 'Image building cannot acquire a platform publication identity.'
Assert ($notify.Contains('environment: application-release') -and $notify.Contains('needs: [validate, ci, images]')) 'Only successfully published images may be notified.'
Assert ($notify -notmatch 'CENTRAL_REGISTRY_TOKEN|docker/|packages: write') 'Notification must not receive registry publishing credentials.'
foreach ($use in [regex]::Matches($source,'uses: ([^\s#]+)')) {
    Assert ($use.Groups[1].Value -match '^\./\.github/workflows/ci.yml$|^[a-zA-Z0-9_/-]+@[a-f0-9]{40}$') 'Pin external actions by commit.'
}
Assert ($source.Contains('steps.backend.outputs.digest') -and $source.Contains('steps.frontend.outputs.digest')) 'Publish both exact image digests.'
Assert ($source.Contains('--export-installation-job-catalog') -and $source.Contains('--network none')) 'Export the compiled job catalog from the digest-pinned backend without network access.'
Assert ($source.Contains('org.opencontainers.image.source=https://github.com/${{ github.repository }}')) 'Preserve actual source-repository provenance.'
Assert ($source.Contains('cancel-in-progress: false')) 'Do not interrupt a running publication.'
Write-Host 'sales-plattform: main publication, central image digests, protected OIDC notification and absence of cluster access passed.'
