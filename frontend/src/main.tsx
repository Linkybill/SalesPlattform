import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { IdentityPlatformApplication } from '@hammer2fall/identity-platform-react'
import '@hammer2fall/identity-platform-react/styles.css'
import App from './App'
import { identityPlatformConfig } from './identityPlatformConfig'
import { salesRoutes } from './salesRoutes'
import './styles.css'

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
        applicationSubtitle: 'Startgerüst für die künftige Zoho-Anbindung',
        tenantPortalUrl: import.meta.env.VITE_TENANT_PORTAL_URL ?? 'http://localhost:3001',
        showUser: true,
        routes: salesRoutes,
      }}
    >
      <App />
    </IdentityPlatformApplication>
  </StrictMode>,
)
