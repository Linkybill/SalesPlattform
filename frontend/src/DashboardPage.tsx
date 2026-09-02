import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

type WorklistItem = {
  id: string
  workItemType: string
  workItemTypeName: string
  status: string
  title: string
  reason: string | null
  ownerName: string | null
  dueAt: string | null
  priorityScore: number
  priorityBand: 'critical' | 'high' | 'medium' | 'low'
  sourceRuleCode: string | null
  snoozedUntil: string | null
  requiresApproval: boolean
}

type WorklistResponse = {
  generatedAt: string
  lastRefreshAt: string | null
  ownerMatched: boolean
  teamView: boolean
  items: WorklistItem[]
}

export function DashboardPage() {
  const { activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [response, setResponse] = useState<WorklistResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadWorklist = useCallback(async (refresh = false) => {
    if (!user || !activeTenantId) return

    setLoading(true)
    setError(null)
    try {
      const apiResponse = await authorizedFetch(`/api/worklist?refresh=${refresh ? 'true' : 'false'}`)
      if (!apiResponse.ok) {
        throw new Error(`Arbeitsliste antwortete mit HTTP ${apiResponse.status}.`)
      }

      setResponse(await apiResponse.json() as WorklistResponse)
    } catch (reason) {
      setResponse(null)
      setError(reason instanceof Error ? reason.message : 'Die Arbeitsliste ist nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, user])

  useEffect(() => {
    void loadWorklist(true)
  }, [loadWorklist])

  const updateItem = async (item: WorklistItem, path: string, init?: RequestInit) => {
    setError(null)
    try {
      const apiResponse = await authorizedFetch(path, { method: 'POST', ...init })
      if (!apiResponse.ok) throw new Error(`Vorgang konnte nicht aktualisiert werden (HTTP ${apiResponse.status}).`)
      const updated = await apiResponse.json() as WorklistItem
      setResponse(current => current
        ? { ...current, items: current.items.filter(candidate => candidate.id !== updated.id) }
        : current)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : `Der Vorgang „${item.title}“ konnte nicht aktualisiert werden.`)
    }
  }

  const completeItem = async (item: WorklistItem) => {
    await updateItem(item, `/api/worklist/${item.id}/complete`)
  }

  const snoozeItem = async (item: WorklistItem) => {
    const until = new Date(Date.now() + 24 * 60 * 60 * 1000)
    await updateItem(item, `/api/worklist/${item.id}/snooze`, {
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ until: until.toISOString() }),
    })
  }

  const formatDate = (value: string | null) => value
    ? new Date(value).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
    : 'kein Termin'

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · {response?.teamView ? 'TEAM-ARBEITSLISTE' : 'MEINE ARBEITSLISTE'}</p>
          <h1>{response?.teamView ? 'Team-Arbeitsliste' : 'Arbeitsliste'}</h1>
          <p className="sales-lead">
            {response?.teamView
              ? 'Alle offenen Vorgänge des Tenants werden priorisiert und als nächste Team-Arbeitsschritte angezeigt.'
              : 'Die wichtigsten offenen Vorgänge werden aus den CRM-Daten priorisiert und direkt als nächste Arbeitsschritte angezeigt.'}
          </p>
        </div>
        <button className="secondary-button" type="button" onClick={() => void loadWorklist(true)} disabled={loading}>
          {loading ? 'Wird aktualisiert …' : 'Arbeitsliste aktualisieren'}
        </button>
      </section>

      {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}

      <section className="sales-card worklist-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">PRIORISIERT NACH FACHLICHER DRINGLICHKEIT</p>
            <h2>{response?.items.length ?? 0} offene Vorgänge</h2>
          </div>
          <span className="worklist-refresh">
            {response?.lastRefreshAt ? `Stand ${formatDate(response.lastRefreshAt)}` : 'Noch nicht bewertet'}
          </span>
        </div>

        {response && !response.teamView && !response.ownerMatched && (
          <div className="message info-message">
            Für deinen Plattform-Benutzer ist noch kein CRM-Besitzer hinterlegt. Es werden deshalb nur nicht zugeordnete Vorgänge angezeigt.
          </div>
        )}

        {!response && loading && <p className="worklist-empty">Arbeitsliste wird aus den CRM-Daten aufgebaut …</p>}
        {response?.items.length === 0 && (
          <div className="worklist-empty">
            <strong>Keine offenen Vorgänge</strong>
            <span>Nach der nächsten CRM-Synchronisation wird die Liste erneut bewertet.</span>
          </div>
        )}

        {response && response.items.length > 0 && (
          <div className="worklist-list">
            {response.items.map(item => (
              <article className="worklist-item" key={item.id}>
                <div className="worklist-item-main">
                  <div className="worklist-item-topline">
                    <span className={`priority-badge priority-${item.priorityBand}`}>{item.priorityBand}</span>
                    <span className="worklist-type">{item.workItemTypeName}</span>
                    <span className="worklist-score">{item.priorityScore.toFixed(0)} Punkte</span>
                  </div>
                  <h3>{item.title}</h3>
                  <p>{item.reason}</p>
                  <div className="worklist-meta">
                    <span>Fällig: <strong>{formatDate(item.dueAt)}</strong></span>
                    {item.ownerName && <span>Zuständig: <strong>{item.ownerName}</strong></span>}
                    {item.sourceRuleCode && <code>{item.sourceRuleCode}</code>}
                  </div>
                </div>
                <div className="worklist-actions">
                  <button className="primary-button" type="button" onClick={() => void completeItem(item)}>
                    {item.requiresApproval ? 'Als geprüft schließen' : 'Erledigt'}
                  </button>
                  <button className="secondary-button" type="button" onClick={() => void snoozeItem(item)}>
                    Morgen
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </main>
  )
}
