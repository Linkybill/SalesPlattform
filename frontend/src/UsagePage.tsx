import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

type UsageBreakdown = {
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

type UsageProvider = {
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
  breakdown: UsageBreakdown[]
}

type UsageScope = {
  runId: string | null
  jobName: string | null
  origin: string
  requestedBy: string | null
  correlationId: string | null
  runMode: string | null
  runStatus: string | null
  currentModule: string | null
  firstObservedAt: string
  lastObservedAt: string
  requests: number
  successfulRequests: number
  failedRequests: number
  retryableRequests: number
  unitsByName: Record<string, number>
}

type UsageReport = {
  fromUtc: string
  toUtc: string
  requests: number
  successfulRequests: number
  failedRequests: number
  retryableRequests: number
  unitsByName: Record<string, number>
  providers: UsageProvider[]
  scopes: UsageScope[]
}

type UsageCall = {
  id: string
  runId: string | null
  origin: string
  requestedBy: string | null
  correlationId: string | null
  providerKey: string
  connectionKey: string
  httpMethod: string
  endpoint: string
  operation: string
  category: string
  statusCode: number | null
  succeeded: boolean
  retryable: boolean
  estimatedUnits: number
  usageUnit: string
  providerUnitsRemaining: number | null
  occurredAt: string
  durationMilliseconds: number
}

type UsageCallPage = {
  total: number
  offset: number
  limit: number
  calls: UsageCall[]
}

type ApiErrorPayload = { message?: string; detail?: string; title?: string }

function formatUnits(units: Record<string, number>): string {
  return Object.entries(units)
    .map(([unit, value]) => `${value.toLocaleString('de-DE')} ${unit}`)
    .join(' · ') || '0'
}

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleString('de-DE') : '–'
}

function originLabel(origin: string): string {
  if (origin === 'job') return 'Job'
  if (origin === 'user-interface') return 'Benutzeroberfläche'
  if (origin === 'system') return 'System'
  return origin
}

function scopeKey(scope: UsageScope): string {
  return [scope.runId ?? 'none', scope.origin, scope.requestedBy ?? '', scope.correlationId ?? ''].join(':')
}

function scopeTitle(scope: UsageScope): string {
  if (scope.jobName) return scope.jobName
  if (scope.runId) return 'Unbenannter Lauf'
  return `${originLabel(scope.origin)} ohne Lauf`
}

async function readJson<T>(response: Response): Promise<T | null> {
  const body = await response.text()
  if (!body.trim()) return null
  try { return JSON.parse(body) as T } catch { return null }
}

export function UsagePage() {
  const { activeTenant, activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [hours, setHours] = useState(24)
  const [usage, setUsage] = useState<UsageReport | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [expandedScope, setExpandedScope] = useState<string | null>(null)
  const [callsByScope, setCallsByScope] = useState<Record<string, UsageCallPage>>({})
  const [callsLoading, setCallsLoading] = useState<string | null>(null)

  const canManageUsage = activeTenant?.isTenantAdmin === true

  const load = useCallback(async () => {
    if (!user || !activeTenantId || !canManageUsage) return
    setLoading(true)
    setError(null)
    try {
      const response = await authorizedFetch(`/api/integrations/usage?hours=${hours}`)
      const payload = await readJson<UsageReport & ApiErrorPayload>(response)
      if (!response.ok || !payload) {
        throw new Error(payload?.message ?? payload?.detail ?? payload?.title ?? `Usage antwortete mit HTTP ${response.status}.`)
      }
      setUsage(payload)
      setCallsByScope({})
      setExpandedScope(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Usage-Daten sind nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, canManageUsage, hours, user])

  useEffect(() => { void load() }, [load])

  const loadCalls = async (scope: UsageScope, append = false) => {
    const key = scopeKey(scope)
    const previous = callsByScope[key]
    const query = new URLSearchParams({ hours: String(hours), limit: '100' })
    if (scope.runId) query.set('runId', scope.runId)
    else {
      query.set('origin', scope.origin)
      if (scope.requestedBy) query.set('requestedBy', scope.requestedBy)
      if (scope.correlationId) query.set('correlationId', scope.correlationId)
    }
    query.set('offset', append ? String(previous?.calls.length ?? 0) : '0')

    setCallsLoading(key)
    try {
      const response = await authorizedFetch(`/api/integrations/usage/calls?${query.toString()}`)
      const payload = await readJson<UsageCallPage & ApiErrorPayload>(response)
      if (!response.ok || !payload) {
        throw new Error(payload?.message ?? payload?.detail ?? payload?.title ?? `Calls antworteten mit HTTP ${response.status}.`)
      }
      setCallsByScope(current => ({
        ...current,
        [key]: append && previous ? { ...payload, calls: [...previous.calls, ...payload.calls] } : payload,
      }))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die API-Calls sind nicht erreichbar.')
    } finally {
      setCallsLoading(null)
    }
  }

  const toggleScope = async (scope: UsageScope) => {
    const key = scopeKey(scope)
    if (expandedScope === key) {
      setExpandedScope(null)
      return
    }
    setExpandedScope(key)
    if (!callsByScope[key]) await loadCalls(scope)
  }

  if (!canManageUsage) {
    return (
      <main className="sales-page">
        <section className="sales-card">
          <p className="sales-eyebrow">INTEGRATION · USAGE</p>
          <h1>Usage nicht freigegeben</h1>
          <p className="sales-card-copy">Diese Seite ist ausschließlich für Tenant-Administratoren verfügbar.</p>
        </section>
      </main>
    )
  }

  return (
    <main className="sales-page usage-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · INTEGRATION USAGE</p>
          <h1>API-Verbrauch</h1>
          <p className="sales-lead">Alle CRM-Aufrufe und Verbrauchseinheiten auf Tenant-Ebene. Läufe, interaktive Aufrufe und die einzelnen Requests bleiben getrennt nachvollziehbar.</p>
        </div>
        <div className="report-toolbar">
          <label>Zeitraum
            <select value={hours} onChange={event => setHours(Number(event.target.value))}>
              <option value="24">Letzte 24 Stunden</option>
              <option value="72">Letzte 3 Tage</option>
              <option value="168">Letzte 7 Tage</option>
            </select>
          </label>
          <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading}>
            {loading ? 'Wird geladen …' : 'Usage aktualisieren'}
          </button>
        </div>
      </section>

      {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}
      {!usage && loading && <section className="sales-card report-loading">Usage-Daten werden geladen …</section>}
      {usage && (
        <>
          <section className="sales-card integration-card">
            <div className="card-heading">
              <div>
                <p className="sales-eyebrow">VERBRAUCH · LOKAL ERFASST</p>
                <h2>Übersicht</h2>
              </div>
              <span className="usage-period">{formatDate(usage.fromUtc)} – {formatDate(usage.toUtc)}</span>
            </div>
            <div className="usage-summary-grid usage-summary-grid-four">
              <div className="report-kpi"><span>API-Calls</span><strong>{usage.requests.toLocaleString('de-DE')}</strong><small>{usage.successfulRequests.toLocaleString('de-DE')} erfolgreich</small></div>
              <div className="report-kpi"><span>Verbrauch</span><strong>{formatUnits(usage.unitsByName)}</strong><small>providerabhängige Einheiten</small></div>
              <div className="report-kpi"><span>Fehlgeschlagen</span><strong>{usage.failedRequests.toLocaleString('de-DE')}</strong><small>{usage.retryableRequests.toLocaleString('de-DE')} retryfähig</small></div>
              <div className="report-kpi"><span>Kontexte</span><strong>{usage.scopes.length.toLocaleString('de-DE')}</strong><small>Läufe und UI-/Systemkontexte</small></div>
            </div>
            <p className="usage-explanation">Die Abfrage dieser Seite liest ausschließlich unsere lokale Usage-Tabelle. Sie erzeugt keinen zusätzlichen CRM-Call.</p>
          </section>

          <section className="sales-card integration-card">
            <div className="card-heading">
              <div>
                <p className="sales-eyebrow">NACH PROVIDER</p>
                <h2>Provider-Verbrauch</h2>
              </div>
            </div>
            {usage.providers.map(provider => (
              <div className="usage-provider" key={`${provider.providerKey}:${provider.connectionKey}`}>
                <div className="usage-provider-heading">
                  <strong>{provider.providerKey} · {provider.connectionKey}</strong>
                  <span>{provider.requests.toLocaleString('de-DE')} Calls · Verbrauch: {formatUnits(provider.unitsByName)}</span>
                </div>
                {provider.latestProviderUnitsRemaining !== null && <p className="usage-provider-remaining">Provider-Restwert: <strong>{provider.latestProviderUnitsRemaining.toLocaleString('de-DE')}</strong>{provider.latestProviderObservationAt && ` (${formatDate(provider.latestProviderObservationAt)})`}</p>}
                <div className="usage-breakdown-list">
                  {provider.breakdown.slice(0, 8).map(item => <div className="usage-breakdown-row" key={`${item.httpMethod}:${item.endpoint}`}><span><code>{item.httpMethod}</code> {item.endpoint}<small>{item.category}</small></span><strong>{item.requests.toLocaleString('de-DE')} · {item.estimatedUnits.toLocaleString('de-DE')} {item.usageUnit}</strong></div>)}
                </div>
              </div>
            ))}
          </section>

          <section className="sales-card integration-card">
            <div className="card-heading">
              <div>
                <p className="sales-eyebrow">NACH AUSFÜHRUNGSKONTEXT</p>
                <h2>Läufe und Aufrufe</h2>
              </div>
              <span className="usage-period">Aufklappen lädt die Einzel-Calls</span>
            </div>
            <div className="usage-scope-list">
              {usage.scopes.map(scope => {
                const key = scopeKey(scope)
                const calls = callsByScope[key]
                const isOpen = expandedScope === key
                return (
                  <article className="usage-scope" key={key}>
                    <button className="usage-scope-header" type="button" onClick={() => void toggleScope(scope)}>
                      <span className="usage-scope-title"><strong>{scopeTitle(scope)}</strong><small>{scope.runId ? `Lauf-ID: ${scope.runId}` : originLabel(scope.origin)}{scope.runMode ? ` · ${scope.runMode}` : ''}{scope.runStatus ? ` · ${scope.runStatus}` : ''}</small></span>
                      <span className="usage-scope-stats"><strong>{formatUnits(scope.unitsByName)} verbraucht</strong><small>{scope.requests.toLocaleString('de-DE')} Calls · {scope.failedRequests.toLocaleString('de-DE')} Fehler</small></span>
                      <span className="usage-scope-toggle">{isOpen ? 'Schließen ▲' : 'Calls anzeigen ▼'}</span>
                    </button>
                    <div className="usage-scope-meta"><span>{formatDate(scope.firstObservedAt)} – {formatDate(scope.lastObservedAt)}</span>{scope.currentModule && <span>Aktuell: <strong>{scope.currentModule}</strong></span>}{scope.requestedBy && <span>Auslöser: <code>{scope.requestedBy}</code></span>}{scope.correlationId && !scope.runId && <span>Korrelation: <code>{scope.correlationId}</code></span>}</div>
                    {isOpen && <div className="usage-call-list">
                      {callsLoading === key && !calls && <div className="usage-call-empty">Calls werden geladen …</div>}
                      {calls?.calls.map(call => <div className="usage-call" key={call.id}><span className="usage-call-time">{formatDate(call.occurredAt)}</span><span className="usage-call-main"><strong><code>{call.httpMethod}</code> {call.endpoint}</strong><small>{call.operation} · {call.category} · {call.durationMilliseconds} ms</small></span><span className={call.succeeded ? 'usage-call-status is-success' : 'usage-call-status is-error'}>{call.statusCode ?? '–'} · {call.estimatedUnits} {call.usageUnit} verbraucht{call.providerUnitsRemaining !== null ? ` · ${call.providerUnitsRemaining} ${call.usageUnit} verbleibend` : ''}{call.retryable ? ' · retryfähig' : ''}</span></div>)}
                      {calls?.calls.length === 0 && <div className="usage-call-empty">Keine Calls in diesem Kontext.</div>}
                      {calls && calls.calls.length < calls.total && <button className="secondary-button usage-more-button" type="button" onClick={() => void loadCalls(scope, true)} disabled={callsLoading === key}>{callsLoading === key ? 'Wird geladen …' : `Weitere Calls laden (${calls.calls.length} von ${calls.total})`}</button>}
                    </div>}
                  </article>
                )
              })}
              {usage.scopes.length === 0 && <div className="worklist-empty"><strong>Noch keine CRM-Calls erfasst</strong><span>Nach dem nächsten CRM-Aufruf wird der Ausführungskontext hier angezeigt.</span></div>}
            </div>
          </section>
        </>
      )}
    </main>
  )
}
