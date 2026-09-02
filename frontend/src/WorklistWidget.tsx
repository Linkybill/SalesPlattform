import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

export type WorklistItem = {
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
  externalUrl: string | null
  snoozedUntil: string | null
  requiresApproval: boolean
  availableFrom: string | null
}

type WorklistResponse = {
  generatedAt: string
  lastRefreshAt: string | null
  ownerMatched: boolean
  teamView: boolean
  items: WorklistItem[]
}

export function WorklistWidget({ compact = false }: { compact?: boolean }) {
  const { activeTenantId, authorizedFetch, user } = useApplicationContext()
  const [response, setResponse] = useState<WorklistResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadWorklist = useCallback(async (refresh = false) => {
    if (!user || !activeTenantId) return
    setLoading(true)
    setError(null)
    try {
      const apiResponse = await authorizedFetch(`/api/worklist?refresh=${refresh ? 'true' : 'false'}`)
      if (!apiResponse.ok) throw new Error(`Arbeitsliste antwortete mit HTTP ${apiResponse.status}.`)
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
    const refreshTimer = window.setInterval(() => void loadWorklist(false), 60_000)
    return () => window.clearInterval(refreshTimer)
  }, [loadWorklist])

  const snoozeItem = async (item: WorklistItem) => {
    setError(null)
    try {
      const apiResponse = await authorizedFetch(`/api/worklist/${item.id}/snooze`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tomorrow: true }),
      })
      if (!apiResponse.ok) throw new Error(`Vorgang konnte nicht aktualisiert werden (HTTP ${apiResponse.status}).`)
      const updated = await apiResponse.json() as WorklistItem
      setResponse(current => current ? { ...current, items: current.items.filter(candidate => candidate.id !== updated.id) } : current)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : `Der Vorgang „${item.title}“ konnte nicht aktualisiert werden.`)
    }
  }

  const formatDate = (value: string | null) => value
    ? new Date(value).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
    : 'kein Termin'

  return (
    <section className="sales-card worklist-card webpart-card">
      <div className="card-heading">
        <div>
          <p className="sales-eyebrow">{response?.teamView ? 'TEAM-ARBEITSLISTE' : 'MEINE ARBEITSLISTE'}</p>
          <h2>{response?.items.length ?? 0} offene Vorgänge</h2>
        </div>
        <button className="secondary-button" type="button" onClick={() => void loadWorklist(true)} disabled={loading}>
          {loading ? 'Wird aktualisiert …' : 'Aktualisieren'}
        </button>
      </div>
      {error && <div className="message error-message">{error}</div>}
      {response && !response.teamView && !response.ownerMatched && (
        <div className="message info-message">Für deinen Plattform-Benutzer ist noch kein CRM-Besitzer hinterlegt. Es werden nur nicht zugeordnete Vorgänge angezeigt.</div>
      )}
      {!response && loading && <p className="worklist-empty">Arbeitsliste wird aus den CRM-Daten aufgebaut …</p>}
      {response?.items.length === 0 && <div className="worklist-empty"><strong>Keine offenen Vorgänge</strong><span>Nach der nächsten CRM-Synchronisation wird die Liste erneut bewertet.</span></div>}
      {response && response.items.length > 0 && (
        <div className={`worklist-list ${compact ? 'worklist-list-compact' : ''}`}>
          {response.items.slice(0, compact ? 6 : 100).map(item => (
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
                {item.externalUrl && <a className="secondary-button worklist-open-link" href={item.externalUrl} target="_blank" rel="noopener noreferrer">Im CRM öffnen ↗</a>}
                <button className="secondary-button" type="button" onClick={() => void snoozeItem(item)}>Für morgen planen</button>
              </div>
            </article>
          ))}
        </div>
      )}
      {compact && response && response.items.length > 6 && <p className="webpart-footnote">Weitere {response.items.length - 6} Vorgänge in der vollständigen Arbeitsliste.</p>}
    </section>
  )
}
