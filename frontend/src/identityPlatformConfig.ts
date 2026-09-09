import { deploymentEnvironment } from './deploymentEnvironment'
import {
  resolveTenantApplicationPath,
  type IdentityPlatformApplicationOptions,
} from '@hammer2fall/identity-platform-react'

export const applicationKey = 'sales-plattform'

const configuredRootUrl = deploymentEnvironment.VITE_API_BASE_URL
  || window.location.origin

export const tenantApplicationPath = resolveTenantApplicationPath(
  applicationKey,
  configuredRootUrl,
)

export const identityPlatformConfig: IdentityPlatformApplicationOptions = {
  applicationKey,
  applicationRootUrl: tenantApplicationPath.applicationRootUrl,
  applicationBaseUrl: tenantApplicationPath.applicationBaseUrl,
  platformApiBaseUrl: deploymentEnvironment.VITE_PLATFORM_API_BASE_URL,
  syncTenantToUrl: false,
}
