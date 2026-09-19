import { deploymentEnvironment } from './deploymentEnvironment'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { IdentityPlatformApplication, initializePlatformTheme } from '@hammer2fall/identity-platform-react'
import '@hammer2fall/identity-platform-react/styles.css'
import App from './App'
import { identityPlatformConfig } from './identityPlatformConfig'
import { salesRoutes, canonicalSalesEntryPath } from './salesRoutes'
import './styles.css'
import './reportNavigation.css'

initializePlatformTheme()

const entryPath = canonicalSalesEntryPath(window.location.pathname)
if (entryPath !== window.location.pathname) {
  window.history.replaceState(window.history.state, '', entryPath + window.location.search + window.location.hash)
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <IdentityPlatformApplication
      {...identityPlatformConfig}
      jobs={{
        navigationTitle: 'Jobs',
        title: 'Jobübersicht',
        description: 'Konfigurieren und überwachen Sie die Hintergrundaufträge dieses Mandanten.',
      }}
      header={{
        eyebrow: 'IDENTITY PLATFORM APP',
        applicationName: 'SalesPlattform',
        applicationSubtitle: 'Arbeit organisieren · Vertrieb steuern',
        tenantPortalUrl: deploymentEnvironment.VITE_TENANT_PORTAL_URL,
        showUser: true,
        routes: salesRoutes,
      }}
    >
      <App />
    </IdentityPlatformApplication>
  </StrictMode>,
)
