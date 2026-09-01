import { useCallback, useEffect, useMemo, useState } from 'react'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { tenantApplicationPath } from './identityPlatformConfig'

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

type ApiErrorPayload = {
  message?: string
  detail?: string
  title?: string
}

const applicationRouteBase = tenantApplicationPath.routerBasePath.replace(/\/+$/, '') || '/'
const jobStorageKey = (tenantId: string) => `sales-zoho-sync-job:${tenantId}`

async function readJson<T>(response: Response): Promise<T | null> {
  const body = await response.text()
  if (!body.trim()) return null
  try {
    return JSON.parse(body) as T
  } catch {
    return null
  }
}

function getError(payload: ApiErrorPayload | null, fallback: string): string {
  return payload?.message ?? payload?.detail ?? payload?.title ?? fallback
}

function isActive(job: ZohoSyncJob): boolean {
  return job.status === 'queued' || job.status === 'running'
}

function statusLabel(status: string): string {
  switch (status) {
    case 'queued': return 'WARTESCHLANGE'
    case 'running': return 'LÄUFT'
    case 'succeeded': return 'ERFOLGREICH'
    case 'completed_with_errors': return 'MIT FEHLERN'
    case 'failed': return 'FEHLER'
    default: return status.toUpperCase()
  }
}

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleString('de-DE') : '–'
}

export function JobsPage() {
  const { activeTenant, activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [jobs, setJobs] = useState<ZohoSyncJob[]>([])
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const canManageJobs = activeTenant?.isTenantAdmin === true

  const upsertJob = useCallback((job: ZohoSyncJob) => {
    setJobs(current => [job, ...current.filter(candidate => candidate.runId !== job.runId)]
      .sort((left, right) => Date.parse(right.queuedAt) - Date.parse(left.queuedAt)))
    setSelectedJobId(current => current ?? job.runId)
    if (activeTenantId && isActive(job))
      sessionStorage.setItem(jobStorageKey(activeTenantId), job.runId)
  }, [activeTenantId])

  const loadActiveJob = useCallback(async (): Promise<ZohoSyncJob | null> => {
    if (!user || !activeTenantId || !canManageJobs) return null

    const response = await authorizedFetch('/api/integrations/zoho/sync/active')
    if (response.status === 204) return null
    const payload = await readJson<ZohoSyncJob & ApiErrorPayload>(response)
    if (!response.ok || !payload) {
      throw new Error(getError(payload, 'Aktive Jobs antworteten mit HTTP ' + response.status + '.'))
    }
    upsertJob(payload)
    return payload
  }, [activeTenantId, authorizedFetch, canManageJobs, upsertJob, user])

  const loadJobs = useCallback(async () => {
    if (!user || !activeTenantId || !canManageJobs) return

    setLoading(true)
    setError(null)
    try {
      const response = await authorizedFetch('/api/integrations/zoho/sync/runs?limit=50')
      const payload = await readJson<ZohoSyncJob[] & ApiErrorPayload>(response)
      if (!response.ok || !payload) {
        throw new Error(getError(payload, 'Jobübersicht antwortete mit HTTP ' + response.status + '.'))
      }
      setJobs(payload)

      const storedJobId = sessionStorage.getItem(jobStorageKey(activeTenantId))
      const storedJob = payload.find(job => job.runId === storedJobId)
      const activeJob = payload.find(isActive)
      setSelectedJobId(storedJob?.runId ?? activeJob?.runId ?? payload[0]?.runId ?? null)

      // This also requeues a run that was interrupted by a pod rebuild.
      await loadActiveJob()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Jobübersicht ist nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, canManageJobs, loadActiveJob, user])

  useEffect(() => {
    void loadJobs()
  }, [loadJobs])

  const selectedJob = useMemo(
    () => jobs.find(job => job.runId === selectedJobId) ?? jobs[0] ?? null,
    [jobs, selectedJobId],
  )

  useEffect(() => {
    if (!selectedJob || !activeTenantId || !user?.accessToken || !isActive(selectedJob)) return

    const connection = new HubConnectionBuilder()
      .withUrl(`${window.location.origin}/apps/sales-plattform/${activeTenantId}/api/hubs/zoho-sync`, {
        accessTokenFactory: () => user.accessToken ?? '',
        transport: HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('jobUpdated', (snapshot: ZohoSyncJob) => upsertJob(snapshot))
    void connection.start()
      .then(() => connection.invoke<ZohoSyncJob>('Watch', selectedJob.runId))
      .then(snapshot => upsertJob(snapshot))
      .catch(reason => setError(`Live-Verbindung zum Job fehlgeschlagen: ${reason instanceof Error ? reason.message : String(reason)}`))

    return () => { void connection.stop() }
  }, [activeTenantId, selectedJob, upsertJob, user?.accessToken])

  if (!canManageJobs) {
    return (
      <main className="sales-page">
        <section className="sales-card">
          <p className="sales-eyebrow">JOBS</p>
          <h1>Jobansicht nicht freigegeben</h1>
          <p className="sales-card-copy">Diese Ansicht ist zunächst ausschließlich für Tenant-Administratoren verfügbar.</p>
        </section>
      </main>
    )
  }

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">JOBS · TENANT-AUFTRÄGE</p>
          <h1>Jobübersicht</h1>
          <p className="sales-lead">
            Hier sehen Sie laufende und abgeschlossene Hintergrundaufträge dieses Mandanten.
            Laufende Zoho-Importe werden per SignalR aktualisiert.
          </p>
        </div>
        <span className="status-badge">Nur Tenant-Admins</span>
      </section>

      <section className="sales-card jobs-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">AUFTRÄGE</p>
            <h2>CRM-Importe</h2>
          </div>
          <button className="secondary-button" type="button" onClick={() => void loadJobs()} disabled={loading}>
            {loading ? 'Aktualisiere …' : 'Aktualisieren'}
          </button>
        </div>

        {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}

        {jobs.length === 0 ? (
          <div className="message">Noch keine Importaufträge vorhanden.</div>
        ) : (
          <div className="jobs-layout">
            <div className="jobs-list" aria-label="Importaufträge">
              {jobs.map(job => (
                <button
                  className={`job-list-entry${job.runId === selectedJob?.runId ? ' selected' : ''}`}
                  key={job.runId}
                  type="button"
                  onClick={() => setSelectedJobId(job.runId)}
                >
                  <span>
                    <strong>Zoho CRM</strong>
                    <small>{formatDate(job.queuedAt)}</small>
                  </span>
                  <code>{statusLabel(job.status)}</code>
                </button>
              ))}
            </div>

            {selectedJob && (
              <div className="job-detail">
                <div className="job-detail-heading">
                  <div>
                    <p className="sales-eyebrow">JOBDETAILS</p>
                    <h3>Zoho CRM · {statusLabel(selectedJob.status)}</h3>
                  </div>
                  <code>{selectedJob.runId}</code>
                </div>
                <div className="job-detail-summary">
                  <span>Gestartet: <code>{formatDate(selectedJob.startedAt)}</code></span>
                  <span>Beendet: <code>{formatDate(selectedJob.finishedAt)}</code></span>
                  <span>Datensätze: <code>{selectedJob.recordsWritten} geschrieben / {selectedJob.recordsRead} gelesen / {selectedJob.recordsFailed} Fehler</code></span>
                </div>
                <div className="job-module-list">
                  {selectedJob.modules.map(module => {
                    const item = selectedJob.items.find(candidate => candidate.module === module)
                    return (
                      <div className="job-module" key={module}>
                        <span>{module}</span>
                        <code>{item?.status ? statusLabel(item.status) : 'WARTEND'} · {item?.recordsWritten ?? 0} geschrieben / {item?.recordsRead ?? 0} gelesen</code>
                      </div>
                    )
                  })}
                </div>
                {selectedJob.error && <div className="message error-message">{selectedJob.error}</div>}
              </div>
            )}
          </div>
        )}

        <p className="jobs-navigation">
          <a href={`${applicationRouteBase === '/' ? '' : applicationRouteBase}/import`}>Zum CRM-Import</a>
        </p>
      </section>
    </main>
  )
}
