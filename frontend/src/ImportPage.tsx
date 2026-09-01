import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'

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

type ZohoSyncModule = {
  module: string
  status: string
  recordsRead: number
  recordsWritten: number
  recordsFailed: number
  startedAt: string | null
  finishedAt: string | null
  error: string | null
}

type ZohoSyncJob = {
  runId: string
  status: string
  modules: string[]
  currentModule: string | null
  recordsRead: number
  recordsWritten: number
  recordsFailed: number
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  error: string | null
  items: ZohoSyncModule[]
}

const syncJobStorageKey = (tenantId: string) => `sales-zoho-sync-job:${tenantId}`

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
  const [zohoJob, setZohoJob] = useState<ZohoSyncJob | null>(null)
  const canManageImport = activeTenant?.isTenantAdmin === true
  const zohoImportActive = zohoJob?.status === 'queued' || zohoJob?.status === 'running'

  const applyZohoJob = useCallback((job: ZohoSyncJob) => {
    setZohoJob(job)
    const active = job.status === 'queued' || job.status === 'running'
    setZohoLoading(active)
    if (!active && activeTenantId) sessionStorage.removeItem(syncJobStorageKey(activeTenantId))
    if (job.status === 'succeeded') {
      setZohoMessage(`Import abgeschlossen: ${job.recordsWritten} geschrieben, ${job.recordsFailed} Fehler bei ${job.recordsRead} Datensätzen.`)
    } else if (job.status === 'completed_with_errors') {
      setZohoError(job.error ?? `Import mit ${job.recordsFailed} Fehlern abgeschlossen.`)
    } else if (job.status === 'failed') {
      setZohoError(job.error ?? 'Der Zoho-Import ist fehlgeschlagen.')
    }
  }, [activeTenantId])

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

  const loadActiveZohoJob = useCallback(async (): Promise<boolean> => {
    if (!user || !activeTenantId || !canManageImport) return false

    try {
      const apiResponse = await authorizedFetch('/api/integrations/zoho/sync/active')
      if (apiResponse.status === 204) return false
      const payload = await readJson<ZohoSyncJob & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok || !payload) return false
      sessionStorage.setItem(syncJobStorageKey(activeTenantId), payload.runId)
      applyZohoJob(payload)
      return true
    } catch {
      return false
    }
  }, [activeTenantId, applyZohoJob, authorizedFetch, canManageImport, user])

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
        runId?: string
        status?: string
        message?: string
      } & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok) {
        if (apiResponse.status === 409 && await loadActiveZohoJob()) {
          setZohoMessage('Es läuft bereits ein Import. Der vorhandene Auftrag wird jetzt überwacht.')
          return
        }
        throw new Error(getApiErrorMessage(payload, 'Zoho-Sync antwortete mit HTTP ' + apiResponse.status + '.'))
      }
      if (!payload?.runId || !activeTenantId) throw new Error('Der Zoho-Import lieferte keine gültige RunId.')
      sessionStorage.setItem(syncJobStorageKey(activeTenantId), payload.runId)
      setZohoJob({
        runId: payload.runId,
        status: payload.status ?? 'queued',
        modules: ['Accounts', 'Deals', 'Leads'],
        currentModule: null,
        recordsRead: 0,
        recordsWritten: 0,
        recordsFailed: 0,
        queuedAt: new Date().toISOString(),
        startedAt: null,
        finishedAt: null,
        error: null,
        items: [],
      })
      setZohoMessage('Import wurde gestartet und läuft im Hintergrund.')
    } catch (reason) {
      setZohoError(reason instanceof Error ? reason.message : 'Der Zoho-Import ist fehlgeschlagen.')
    } finally {
      setZohoLoading(false)
    }
  }, [activeTenantId, authorizedFetch, loadActiveZohoJob])

  const loadZohoJob = useCallback(async () => {
    if (!user || !activeTenantId || !canManageImport) return
    const runId = sessionStorage.getItem(syncJobStorageKey(activeTenantId))
    if (!runId) {
      await loadActiveZohoJob()
      return
    }

    try {
      const apiResponse = await authorizedFetch(`/api/integrations/zoho/sync/${runId}`)
      if (apiResponse.status === 404) {
        sessionStorage.removeItem(syncJobStorageKey(activeTenantId))
        return
      }
      const payload = await readJson<ZohoSyncJob & ApiErrorPayload>(apiResponse)
      if (!apiResponse.ok || !payload) return
      applyZohoJob(payload)
    } catch {
      // The SignalR connection will report live connectivity errors; a failed
      // status refresh must not hide the rest of the import page.
    }

    await loadActiveZohoJob()
  }, [activeTenantId, applyZohoJob, authorizedFetch, canManageImport, loadActiveZohoJob, user])

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
      void loadZohoJob()
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
  }, [activeTenantId, authorizedFetch, canManageImport, loadZohoJob, loadZohoStatus, user])

  useEffect(() => {
    if (!zohoJob?.runId || !activeTenantId || !user?.accessToken) return

    const connection = new HubConnectionBuilder()
      .withUrl(`${window.location.origin}/apps/sales-plattform/${activeTenantId}/api/hubs/zoho-sync`, {
        accessTokenFactory: () => user.accessToken ?? '',
        transport: HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('jobUpdated', (snapshot: ZohoSyncJob) => applyZohoJob(snapshot))
    void connection.start()
      .then(() => connection.invoke<ZohoSyncJob>('Watch', zohoJob.runId))
      .then(snapshot => applyZohoJob(snapshot))
      .catch(reason => setZohoError(`Live-Verbindung zum Import fehlgeschlagen: ${reason instanceof Error ? reason.message : String(reason)}`))

    return () => { void connection.stop() }
  }, [activeTenantId, applyZohoJob, user?.accessToken, zohoJob?.runId])

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
            <button className="primary-button" type="button" onClick={() => void syncZoho()} disabled={zohoLoading || zohoImportActive}>
              {zohoLoading || zohoImportActive ? 'Import läuft …' : 'Accounts, Deals und Leads importieren'}
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

        {zohoJob && (
          <div className="integration-progress">
            <div className="integration-progress-heading">
              <strong>Importauftrag</strong>
              <code>{zohoJob.status === 'queued'
                ? 'WARTESCHLANGE'
                : zohoJob.status === 'running'
                  ? 'LÄUFT'
                  : zohoJob.status.toUpperCase()}</code>
            </div>
            <div className="integration-progress-summary">
              <span>Aktuelles Modul: <code>{zohoJob.currentModule ?? 'wird vorbereitet'}</code></span>
              <span>Fortschritt: <code>{zohoJob.recordsWritten} geschrieben / {zohoJob.recordsRead} gelesen / {zohoJob.recordsFailed} Fehler</code></span>
            </div>
            <div className="integration-progress-modules" aria-live="polite">
              {zohoJob.modules.map(module => {
                const item = zohoJob.items.find(candidate => candidate.module === module)
                return (
                  <div className="integration-progress-module" key={module}>
                    <span>{module}</span>
                    <code>{item?.status === 'succeeded'
                      ? `${item.recordsWritten} geschrieben`
                      : item?.status === 'running'
                        ? `${item.recordsWritten} / ${item.recordsRead}`
                        : item?.status === 'failed'
                          ? 'FEHLER'
                          : 'WARTEND'}</code>
                  </div>
                )
              })}
            </div>
          </div>
        )}

        {(zohoError || platformError) && <div className="message error-message">{zohoError ?? platformError}</div>}
        {zohoMessage && <div className="message success-message">{zohoMessage}</div>}
      </section>
    </main>
  )
}
