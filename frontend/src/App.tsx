import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

type HelloWorldResponse = {
  tenantId: string
  message: string
  database: {
    connected: boolean
    storedRecords: number
    strategy: string
  }
}

type ZohoStatus = {
  connected: boolean
  connectedAt: string | null
  lastTokenRefreshAt: string | null
  lastSyncAt: string | null
  apiDomain: string | null
}

function App() {
  const { activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [response, setResponse] = useState<HelloWorldResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [zohoStatus, setZohoStatus] = useState<ZohoStatus | null>(null)
  const [zohoLoading, setZohoLoading] = useState(false)
  const [zohoMessage, setZohoMessage] = useState<string | null>(null)
  const [zohoError, setZohoError] = useState<string | null>(null)

  const loadHelloWorld = useCallback(async () => {
    if (!user || !activeTenantId) return

    setLoading(true)
    setError(null)
    try {
      const apiResponse = await authorizedFetch('/api/hello-world')
      if (!apiResponse.ok) {
        throw new Error(`HelloWorld-Endpunkt antwortete mit HTTP ${apiResponse.status}.`)
      }

      setResponse(await apiResponse.json() as HelloWorldResponse)
    } catch (reason) {
      setResponse(null)
      setError(reason instanceof Error ? reason.message : 'Der HelloWorld-Endpunkt ist nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, user])

  const loadZohoStatus = useCallback(async () => {
    if (!user || !activeTenantId) return

    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/status')
      if (!apiResponse.ok) {
        throw new Error('Zoho-Status antwortete mit HTTP ' + apiResponse.status + '.')
      }
      setZohoStatus(await apiResponse.json() as ZohoStatus)
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Status ist nicht erreichbar.')
    }
  }, [activeTenantId, authorizedFetch, user])

  const connectZoho = useCallback(async () => {
    setZohoLoading(true)
    setZohoError(null)
    setZohoMessage(null)
    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/oauth/start')
      const payload = await apiResponse.json() as { authorizationUrl?: string; message?: string }
      if (!apiResponse.ok || !payload.authorizationUrl) {
        throw new Error(payload.message ?? ('Zoho-Start antwortete mit HTTP ' + apiResponse.status + '.'))
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
      const apiResponse = await authorizedFetch('/api/integrations/zoho/test-connection')
      const payload = await apiResponse.json() as { availableModules?: string[]; message?: string }
      if (!apiResponse.ok) {
        throw new Error(payload.message ?? ('Zoho-Test antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      setZohoMessage('Verbindung aktiv. ' + (payload.availableModules?.length ?? 0) + ' Zoho-Module gefunden.')
      await loadZohoStatus()
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Verbindungstest ist fehlgeschlagen.')
    } finally {
      setZohoLoading(false)
    }
  }, [authorizedFetch, loadZohoStatus])

  const syncZoho = useCallback(async () => {
    setZohoLoading(true)
    setZohoError(null)
    setZohoMessage(null)
    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/sync', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ modules: ['Accounts', 'Deals', 'Leads'] }),
      })
      const payload = await apiResponse.json() as {
        recordsRead?: number
        recordsWritten?: number
        recordsFailed?: number
        message?: string
      }
      if (!apiResponse.ok) {
        throw new Error(payload.message ?? ('Zoho-Sync antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      setZohoMessage(
        'Import abgeschlossen: ' + (payload.recordsWritten ?? 0) + ' geschrieben, '
        + (payload.recordsFailed ?? 0) + ' Fehler bei ' + (payload.recordsRead ?? 0) + ' Datensätzen.',
      )
      await loadZohoStatus()
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Import ist fehlgeschlagen.')
    } finally {
      setZohoLoading(false)
    }
  }, [authorizedFetch, loadZohoStatus])

  useEffect(() => {
    void loadHelloWorld()
  }, [loadHelloWorld])

  useEffect(() => {
    if (!user || !activeTenantId) return

    const query = new URLSearchParams(window.location.search)
    const code = query.get('zoho_code')
    const state = query.get('zoho_state')
    const oauthError = query.get('zoho_error')
    const oauthErrorDescription = query.get('zoho_error_description')

    if (oauthError) {
      setZohoError(oauthErrorDescription ?? oauthError)
      window.history.replaceState({}, document.title, window.location.pathname)
      void loadZohoStatus()
      return
    }

    if (!code || !state) {
      void loadZohoStatus()
      return
    }

    setZohoLoading(true)
    setZohoError(null)
    void (async () => {
      try {
        const apiResponse = await authorizedFetch('/api/integrations/zoho/oauth/complete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code, state }),
        })
        const payload = await apiResponse.json() as { message?: string; apiDomain?: string }
        if (!apiResponse.ok) {
          throw new Error(payload.message ?? ('Zoho-OAuth antwortete mit HTTP ' + apiResponse.status + '.'))
        }
        setZohoMessage('Zoho ist verbunden (' + (payload.apiDomain ?? 'API-Domain erkannt') + ').')
        await loadZohoStatus()
      } catch (reason) {
        setZohoError(
          oauthErrorDescription
          ?? oauthError
          ?? (reason instanceof Error ? reason.message : 'Die Zoho-Verbindung konnte nicht abgeschlossen werden.'),
        )
      } finally {
        window.history.replaceState({}, document.title, window.location.pathname)
        setZohoLoading(false)
      }
    })()
  }, [activeTenantId, authorizedFetch, loadZohoStatus, user])

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · STARTGERÜST</p>
          <h1>Willkommen in der SalesPlattform</h1>
          <p className="sales-lead">
            Das Grundgerüst steht: React im Frontend, ein geschützter Backend-Endpunkt
            und eine tenant-isolierte Datenbank.
          </p>
        </div>
        <span className="status-badge">Bereit für den nächsten Schritt</span>
      </section>

      <section className="sales-card integration-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">CRM-ADAPTER</p>
            <h2>Zoho CRM</h2>
          </div>
          <code>{zohoStatus?.connected ? 'VERBUNDEN' : 'NOCH NICHT VERBUNDEN'}</code>
        </div>

        <p>
          Zoho wird ausschließlich über das Backend angebunden. Die Verbindung
          wird pro Tenant gespeichert; Tokens gelangen nicht ins Frontend.
        </p>

        <div className="button-row">
          {zohoStatus?.connected ? (
            <button className="primary-button" type="button" onClick={() => void syncZoho()} disabled={zohoLoading}>
              {zohoLoading ? 'Import läuft …' : 'Accounts, Deals und Leads importieren'}
            </button>
          ) : (
            <button className="primary-button" type="button" onClick={() => void connectZoho()} disabled={zohoLoading}>
              {zohoLoading ? 'Weiterleitung …' : 'Zoho verbinden'}
            </button>
          )}
          {zohoStatus?.connected && (
            <button className="secondary-button" type="button" onClick={() => void testZoho()} disabled={zohoLoading}>
              Verbindung testen
            </button>
          )}
        </div>

        {zohoStatus?.connected && (
          <div className="integration-meta">
            <span>API: <code>{zohoStatus.apiDomain}</code></span>
            <span>
              Letzter Import:{' '}
              <code>{zohoStatus.lastSyncAt ? new Date(zohoStatus.lastSyncAt).toLocaleString('de-DE') : 'noch keiner'}</code>
            </span>
          </div>
        )}

        {zohoError && <div className="message error-message">{zohoError}</div>}
        {zohoMessage && <div className="message success-message">{zohoMessage}</div>}
      </section>

      <section className="sales-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">BACKEND-CHECK</p>
            <h2>HelloWorld-Endpunkt</h2>
          </div>
          <code>GET /api/hello-world</code>
        </div>

        <p>
          Der Aufruf läuft über den zentralen Identity-Platform-Kontext und verwendet
          den aktuell ausgewählten Tenant.
        </p>

        <button className="primary-button" type="button" onClick={() => void loadHelloWorld()} disabled={loading}>
          {loading ? 'Wird geladen …' : 'Endpunkt aufrufen'}
        </button>

        {(error || platformError) && (
          <div className="message error-message">{error ?? platformError}</div>
        )}

        {response && (
          <div className="message success-message">
            <strong>{response.message}</strong>
            <span>Tenant: <code>{response.tenantId}</code></span>
            <span>
              Datenbank: <code>{response.database.strategy}</code> ·
              {' '}{response.database.storedRecords} gespeicherte Datensätze
            </span>
          </div>
        )}
      </section>
    </main>
  )
}

export default App
