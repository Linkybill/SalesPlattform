import {
  resolveTenantApplicationPath,
  type IdentityPlatformApplicationOptions,
} from '@hammer2fall/identity-platform-react'

export const applicationKey = 'sales-plattform'

const defaultPlatformOrigin = window.location.port === '3100'
  ? `${window.location.protocol}//${window.location.hostname}:3101`
  : window.location.origin
const configuredRootUrl = import.meta.env.VITE_API_BASE_URL
  ?? `${defaultPlatformOrigin}/apps/${applicationKey}`

export const tenantApplicationPath = resolveTenantApplicationPath(
  applicationKey,
  configuredRootUrl,
)

export const identityPlatformConfig: IdentityPlatformApplicationOptions = {
  applicationKey,
  applicationBaseUrl: tenantApplicationPath.applicationBaseUrl,
  platformApiBaseUrl: import.meta.env.VITE_PLATFORM_API_BASE_URL
    ?? `${defaultPlatformOrigin}/platform`,
  syncTenantToUrl: false,
}
