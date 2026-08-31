import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { IdentityPlatformApplication } from '@hammer2fall/identity-platform-react'
import '@hammer2fall/identity-platform-react/styles.css'
import App from './App'
import { identityPlatformConfig } from './identityPlatformConfig'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <IdentityPlatformApplication
      {...identityPlatformConfig}
      header={{
        eyebrow: 'IDENTITY PLATFORM APP',
        applicationName: 'SalesPlattform',
        applicationSubtitle: 'Startgerüst für die künftige Zoho-Anbindung',
        tenantPortalUrl: import.meta.env.VITE_TENANT_PORTAL_URL ?? 'http://localhost:3001',
        showUser: true,
      }}
    >
      <App />
    </IdentityPlatformApplication>
  </StrictMode>,
)
