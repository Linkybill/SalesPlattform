import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { tenantApplicationPath } from './identityPlatformConfig'
import { usageRoute } from './salesRoutes'

type ZohoStatus = {
  connected: boolean
  connectedAt: string | null
  lastTokenRefreshAt: string | null
  lastSyncAt: string | null
  apiDomain: string | null
}

type CrmApiUsageBreakdown = {
  category: string
  operation: string
  httpMethod: string
  endpoint: string
  usageUnit: string
  requests: number
  successfulRequests: number
  failedRequests: number
  estimatedUnits: number
}

type CrmApiUsageProvider = {
  providerKey: string
  connectionKey: string
  requests: number
  successfulRequests: number
  failedRequests: number
  retryableRequests: number
  estimatedUnits: number
  unitsByName: Record<string, number>
  latestProviderUnitsRemaining: number | null
  latestProviderUnitsLimit: number | null
  latestProviderUnitName: string | null
  latestProviderObservationAt: string | null
  breakdown: CrmApiUsageBreakdown[]
}

type CrmApiUsageReport = {
  fromUtc: string
  toUtc: string
  requests: number
  successfulRequests: number
  failedRequests: number
  retryableRequests: number
  unitsByName: Record<string, number>
  providers: CrmApiUsageProvider[]
}

type ApiErrorPayload = {
  message?: string
  detail?: string
  title?: string
}

const applicationRouteBase = tenantApplicationPath.routerBasePath.replace(/\/+$/, '') || '/'
const jobsRoute = `${applicationRouteBase === '/' ? '' : applicationRouteBase}/jobs`

async function readJson<T>(response: Response): Promise<T | null> {
  const body = await response.text()
  if (!body.trim()) return null
  try {
    return JSON.parse(body) as T
  } catch {
    return null
  }
}

function getApiErrorMessage(payload: ApiErrorPayload | null, fallback: string): string {
  return payload?.message ?? payload?.detail ?? payload?.title ?? fallback
}

function formatUnits(units: Record<string, number>): string {
  return Object.entries(units)
    .map(([unit, value]) => `${value.toLocaleString('de-DE')} ${unit}`)
    .join(' · ') || '0'
}

export function ImportPage() {
  const {
    activeTenant,
    activeTenantId,
    authorizedFetch,
    error: platformError,
    user,
  } = useApplicationContext()
  const [zohoStatus, setZohoStatus] = useState<ZohoStatus | null>(null)
  const [zohoLoading, setZohoLoading] = useState(false)
  const [zohoMessage, setZohoMessage] = useState<string | null>(null)
  const [zohoError, setZohoError] = useState<string | null>(null)
  const [usage, setUsage] = useState<CrmApiUsageReport | null>(null)
  const [usageLoading, setUsageLoading] = useState(false)
  const [usageError, setUsageError] = useState<string | null>(null)
  const canManageImport = activeTenant?.isTenantAdmin === true

  const loadZohoStatus = useCallback(async () => {
    if (!user || !activeTenantId || !canManageImport) return
    try {
      const response = await authorizedFetch('/api/integrations/zoho/status')
      const payload = await readJson<ZohoStatus & ApiErrorPayload>(response)
      if (!response.ok || !payload) {
        throw new Error(getApiErrorMessage(payload, `Zoho-Status antwortete mit HTTP ${response.status}.`))
      }
      setZohoStatus(payload)
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Status ist nicht erreichbar.')
    }
  }, [activeTenantId, authorizedFetch, canManageImport, user])

  const loadUsage = useCallback(async () => {
    if (!user || !activeTenantId || !canManageImport) return
    setUsageLoading(true)
    try {
      const response = await authorizedFetch('/api/integrations/usage?hours=24')
      const payload = await readJson<CrmApiUsageReport & ApiErrorPayload>(response)
      if (!response.ok || !payload) {
        throw new Error(getApiErrorMessage(payload, `API-Verbrauch antwortete mit HTTP ${response.status}.`))
      }
      setUsage(payload)
      setUsageError(null)
    } catch (reason) {
      setUsageError(reason instanceof Error ? reason.message : 'Der API-Verbrauch ist nicht erreichbar.')
    } finally {
      setUsageLoading(false)
    }
  }, [activeTenantId, authorizedFetch, canManageImport, user])

  const connectZoho = useCallback(async () => {
    setZohoLoading(true)
    setZohoError(null)
    setZohoMessage(null)
    try {
      const response = await authorizedFetch('/api/integrations/zoho/oauth/start')
      const payload = await readJson<{ authorizationUrl?: string } & ApiErrorPayload>(response)
      if (!response.ok || !payload?.authorizationUrl) {
        throw new Error(getApiErrorMessage(payload, `Zoho-Start antwortete mit HTTP ${response.status}.`))
      }
      window.location.assign(payload.authorizationUrl)
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Die Zoho-Verbindung konnte nicht gestartet werden.')
      setZohoLoading(false)
    }
  }, [authorizedFetch])

  const testZoho = useCallback(async () => {
    setZohoLoading(true)
    setZohoError(null)
    setZohoMessage(null)
    try {
      const response = await authorizedFetch('/api/integrations/zoho/test-connection')
      const payload = await readJson<{ availableModules?: string[] } & ApiErrorPayload>(response)
      if (!response.ok) {
        throw new Error(getApiErrorMessage(payload, `Zoho-Test antwortete mit HTTP ${response.status}.`))
      }
      setZohoMessage(`Verbindung aktiv. ${payload?.availableModules?.length ?? 0} Zoho-Module gefunden.`)
      await loadZohoStatus()
      await loadUsage()
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Verbindungstest ist fehlgeschlagen.')
    } finally {
      setZohoLoading(false)
    }
  }, [authorizedFetch, loadUsage, loadZohoStatus])

  useEffect(() => {
    if (!user || !activeTenantId || !canManageImport) return
    const query = new URLSearchParams(window.location.search)
    const code = query.get('zoho_code')
    const state = query.get('zoho_state')
    const oauthError = query.get('zoho_error')
    const oauthErrorDescription = query.get('zoho_error_description')

    if (oauthError) {
      setZohoError(oauthErrorDescription ?? oauthError)
      window.history.replaceState({}, document.title, window.location.pathname)
      void loadZohoStatus()
      void loadUsage()
      return
    }
    if (!code || !state) {
      void loadZohoStatus()
      void loadUsage()
      return
    }

    setZohoLoading(true)
    setZohoError(null)
    void (async () => {
      try {
        const response = await authorizedFetch('/api/integrations/zoho/oauth/complete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code, state }),
        })
        const payload = await readJson<{ message?: string; apiDomain?: string } & ApiErrorPayload>(response)
        if (!response.ok) {
          throw new Error(getApiErrorMessage(payload, `Zoho-OAuth antwortete mit HTTP ${response.status}.`))
        }
        setZohoMessage(`Zoho ist verbunden (${payload?.apiDomain ?? 'API-Domain erkannt'}).`)
        await loadZohoStatus()
        await loadUsage()
      } catch (reason) {
        setZohoError(reason instanceof Error ? reason.message : 'Die Zoho-Verbindung konnte nicht abgeschlossen werden.')
      } finally {
        window.history.replaceState({}, document.title, window.location.pathname)
        setZohoLoading(false)
      }
    })()
  }, [activeTenantId, authorizedFetch, canManageImport, loadUsage, loadZohoStatus, user])

  if (!canManageImport) {
    return (
      <main className="sales-page">
        <section className="sales-card">
          <p className="sales-eyebrow">IMPORT</p>
          <h1>Import nicht freigegeben</h1>
          <p className="sales-card-copy">Diese Seite ist ausschließlich für Tenant-Administratoren verfügbar.</p>
        </section>
      </main>
    )
  }

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">CRM-VERBINDUNG · PROVIDER-ADAPTER</p>
          <h1>CRM-Integration</h1>
          <p className="sales-lead">
            Verwalten Sie hier die Verbindung zum ausgewählten CRM. Vollimport,
            Incremental-Crawl, Zeitpläne und Laufhistorie werden zentral als Plattformjobs verwaltet.
          </p>
        </div>
        <span className="status-badge">Nur Tenant-Admins</span>
      </section>

      <section className="sales-card integration-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">AKTUELLER CRM-ADAPTER</p>
            <h2>Zoho CRM</h2>
          </div>
          <code>{zohoStatus?.connected ? 'VERBUNDEN' : 'NOCH NICHT VERBUNDEN'}</code>
        </div>

        <p>
          OAuth, Tokens und Providerzugriffe bleiben vollständig im Zoho-Adapter des Backends.
          Die Synchronisationsjobs arbeiten ausschließlich gegen die neutrale CRM-Schnittstelle und
          die kanonischen Sales-Repositories.
        </p>

        <div className="button-row">
          {zohoStatus?.connected ? (
            <>
              <button className="primary-button" type="button" onClick={() => window.location.assign(jobsRoute)}>
                Jobs und Zeitpläne öffnen
              </button>
              <button className="secondary-button" type="button" onClick={() => void testZoho()} disabled={zohoLoading}>
                Verbindung testen
              </button>
              <button className="secondary-button" type="button" onClick={() => void connectZoho()} disabled={zohoLoading}>
                Verbindung erneuern
              </button>
            </>
          ) : (
            <button className="primary-button" type="button" onClick={() => void connectZoho()} disabled={zohoLoading}>
              {zohoLoading ? 'Weiterleitung …' : 'Zoho verbinden'}
            </button>
          )}
        </div>

        {zohoStatus?.connected && (
          <div className="integration-meta">
            <span>API: <code>{zohoStatus.apiDomain}</code></span>
            <span>
              Letzte erfolgreiche Synchronisation:{' '}
              <code>{zohoStatus.lastSyncAt ? new Date(zohoStatus.lastSyncAt).toLocaleString('de-DE') : 'noch keine'}</code>
            </span>
          </div>
        )}

        {(zohoError || platformError) && <div className="message error-message">{zohoError ?? platformError}</div>}
        {zohoMessage && <div className="message success-message">{zohoMessage}</div>}
      </section>

      <section className="sales-card integration-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">CRM-API-VERBRAUCH · LETZTE 24 STUNDEN</p>
            <h2>Verbrauch der Integration</h2>
          </div>
          <div className="button-row">
            <button className="secondary-button" type="button" onClick={() => void loadUsage()} disabled={usageLoading}>
              {usageLoading ? 'Wird geladen …' : 'Verbrauch aktualisieren'}
            </button>
            <button className="secondary-button" type="button" onClick={() => window.location.assign(usageRoute)}>
              Vollständige Usage öffnen
            </button>
          </div>
        </div>
        <p>
          Hier werden alle tatsächlichen CRM-HTTP-Versuche dieser SalesPlattform erfasst.
          Einheiten sind die providerabhängige Bewertung; bei Zoho wird zusätzlich der vom CRM gelieferte Restwert gespeichert.
        </p>
        {usageError && <div className="message error-message">{usageError}</div>}
        {usage && (
          <>
            <div className="usage-summary-grid">
              <div className="report-kpi"><span>Requests</span><strong>{usage.requests.toLocaleString('de-DE')}</strong><small>{usage.successfulRequests.toLocaleString('de-DE')} erfolgreich</small></div>
              <div className="report-kpi"><span>Verbrauchseinheiten</span><strong>{formatUnits(usage.unitsByName)}</strong><small>je Provider unterschiedlich</small></div>
              <div className="report-kpi"><span>Fehlgeschlagen</span><strong>{usage.failedRequests.toLocaleString('de-DE')}</strong><small>{usage.retryableRequests.toLocaleString('de-DE')} retryfähig</small></div>
            </div>
            {usage.providers.map(provider => (
              <div className="usage-provider" key={`${provider.providerKey}:${provider.connectionKey}`}>
                <div className="usage-provider-heading">
                  <strong>{provider.providerKey} · {provider.connectionKey}</strong>
                  <span>{provider.requests.toLocaleString('de-DE')} Requests · {formatUnits(provider.unitsByName)}</span>
                </div>
                {provider.latestProviderUnitsRemaining !== null && (
                  <p className="usage-provider-remaining">
                    Vom Provider gemeldetes Restguthaben: <strong>{provider.latestProviderUnitsRemaining.toLocaleString('de-DE')}</strong>
                    {provider.latestProviderObservationAt && ` (${new Date(provider.latestProviderObservationAt).toLocaleString('de-DE')})`}
                  </p>
                )}
                {provider.latestProviderUnitsRemaining === null && provider.providerKey === 'zoho' && (
                  <p className="usage-provider-remaining">
                    Zoho liefert das Restguthaben erst ab 50&nbsp;% Verbrauch. Bis dahin zeigt diese Seite die exakt gezählten Requests und die Zoho-Kostenschätzung.
                  </p>
                )}
                <div className="usage-breakdown-list">
                  {provider.breakdown.slice(0, 8).map(item => (
                    <div className="usage-breakdown-row" key={`${item.httpMethod}:${item.endpoint}`}>
                      <span><code>{item.httpMethod}</code> {item.endpoint}<small>{item.category}</small></span>
                      <strong>{item.requests.toLocaleString('de-DE')} · {item.estimatedUnits.toLocaleString('de-DE')} {item.usageUnit}</strong>
                    </div>
                  ))}
                </div>
              </div>
            ))}
            {usage.providers.length === 0 && <div className="worklist-empty"><strong>Noch keine CRM-API-Aufrufe erfasst</strong><span>Nach dem nächsten CRM-Aufruf wird die Aufschlüsselung hier angezeigt.</span></div>}
          </>
        )}
      </section>
    </main>
  )
}
