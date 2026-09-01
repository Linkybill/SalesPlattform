import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

type ZohoStatus = {
  connected: boolean
  connectedAt: string | null
  lastTokenRefreshAt: string | null
  lastSyncAt: string | null
  apiDomain: string | null
}

type ApiErrorPayload = {
  message?: string
  detail?: string
  title?: string
}

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
  const canManageImport = activeTenant?.isTenantAdmin === true

  const loadZohoStatus = useCallback(async () => {
    if (!user || !activeTenantId || !canManageImport) return

    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/status')
      const payload = await readJson<ZohoStatus & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok) {
        throw new Error(getApiErrorMessage(payload, 'Zoho-Status antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      if (!payload) throw new Error('Zoho-Status lieferte keine gültige Antwort.')
      setZohoStatus(payload)
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Status ist nicht erreichbar.')
    }
  }, [activeTenantId, authorizedFetch, canManageImport, user])

  const connectZoho = useCallback(async () => {
    setZohoLoading(true)
    setZohoError(null)
    setZohoMessage(null)
    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/oauth/start')
      const payload = await readJson<{ authorizationUrl?: string } & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok || !payload?.authorizationUrl) {
        throw new Error(getApiErrorMessage(payload, 'Zoho-Start antwortete mit HTTP ' + apiResponse.status + '.'))
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
      const payload = await readJson<{ availableModules?: string[] } & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok) {
        throw new Error(getApiErrorMessage(payload, 'Zoho-Test antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      setZohoMessage('Verbindung aktiv. ' + (payload?.availableModules?.length ?? 0) + ' Zoho-Module gefunden.')
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
      const payload = await readJson<{
        recordsRead?: number
        recordsWritten?: number
        recordsFailed?: number
        message?: string
      } & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok) {
        throw new Error(getApiErrorMessage(payload, 'Zoho-Sync antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      setZohoMessage(
        'Import abgeschlossen: ' + (payload?.recordsWritten ?? 0) + ' geschrieben, '
        + (payload?.recordsFailed ?? 0) + ' Fehler bei ' + (payload?.recordsRead ?? 0) + ' Datensätzen.',
      )
      await loadZohoStatus()
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Import ist fehlgeschlagen.')
    } finally {
      setZohoLoading(false)
    }
  }, [authorizedFetch, loadZohoStatus])

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
        const payload = await readJson<{ message?: string; apiDomain?: string } & ApiErrorPayload>(apiResponse)
        if (!apiResponse.ok) {
          throw new Error(getApiErrorMessage(payload, 'Zoho-OAuth antwortete mit HTTP ' + apiResponse.status + '.'))
        }
        setZohoMessage('Zoho ist verbunden (' + (payload?.apiDomain ?? 'API-Domain erkannt') + ').')
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
  }, [activeTenantId, authorizedFetch, canManageImport, loadZohoStatus, user])

  if (!canManageImport) {
    return (
      <main className="sales-page">
        <section className="sales-card">
          <p className="sales-eyebrow">IMPORT</p>
          <h1>Import nicht freigegeben</h1>
          <p className="sales-card-copy">
            Diese Seite ist ausschließlich für Tenant-Administratoren verfügbar.
          </p>
        </section>
      </main>
    )
  }

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">DATENIMPORT · CRM-ADAPTER</p>
          <h1>CRM-Daten importieren</h1>
          <p className="sales-lead">
            Verwalten Sie hier die CRM-Verbindung und starten Sie den Import in
            die tenant-eigene Sales-Datenbank.
          </p>
        </div>
        <span className="status-badge">Nur Tenant-Admins</span>
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

        {(zohoError || platformError) && <div className="message error-message">{zohoError ?? platformError}</div>}
        {zohoMessage && <div className="message success-message">{zohoMessage}</div>}
      </section>
    </main>
  )
}
