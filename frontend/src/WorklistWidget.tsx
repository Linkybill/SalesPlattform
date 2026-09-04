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
  crmTaskUrl: string | null
  snoozedUntil: string | null
  requiresApproval: boolean
  availableFrom: string | null
}

type WorklistResponse = {
  generatedAt: string
  lastRefreshAt: string | null
  ownerMatched: boolean
  teamView: boolean
  rules: WorklistRule[]
  items: WorklistItem[]
}

type WorklistRule = {
  code: string
  name: string
  description: string | null
  itemCount: number
}

export function WorklistWidget({ compact = false }: { compact?: boolean }) {
  const { activeTenantId, authorizedFetch, user } = useApplicationContext()
  const [response, setResponse] = useState<WorklistResponse | null>(null)
  const [selectedRule, setSelectedRule] = useState<string | null>(null)
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

  useEffect(() => {
    if (selectedRule && response && !response.rules.some(rule => rule.code === selectedRule))
      setSelectedRule(null)
  }, [response, selectedRule])

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

  const visibleItems = response
    ? response.items.filter(item => !selectedRule || item.sourceRuleCode === selectedRule)
    : []

  return (
    <section className="sales-card worklist-card webpart-card">
      <div className="card-heading">
        <div>
          <p className="sales-eyebrow">{response?.teamView ? 'TEAM-ARBEITSLISTE' : 'MEINE ARBEITSLISTE'}</p>
          <h2>{visibleItems.length} offene Vorgänge</h2>
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
      {response && (
        <div className="worklist-browser">
          <nav className="worklist-rule-nav" aria-label="Arbeitsliste nach Regel filtern">
            <p className="worklist-rule-nav-title">Aufgaben nach Regel</p>
            <button className={`worklist-rule-button${selectedRule === null ? ' is-active' : ''}`} type="button" onClick={() => setSelectedRule(null)}>
              <span>Alle</span><strong>{response.items.length}</strong>
            </button>
            {response.rules.map(rule => (
              <button className={`worklist-rule-button${selectedRule === rule.code ? ' is-active' : ''}`} type="button" key={rule.code} onClick={() => setSelectedRule(rule.code)} title={rule.description ?? undefined}>
                <span><small>{rule.code}</small>{rule.name}</span><strong>{rule.itemCount}</strong>
              </button>
            ))}
          </nav>
          <div className="worklist-results">
            {visibleItems.length === 0 && <div className="worklist-empty"><strong>{selectedRule ? 'Keine passenden Vorgänge' : 'Keine offenen Vorgänge'}</strong><span>{selectedRule ? 'Für diese Regel gibt es aktuell keine offenen Vorgänge.' : 'Nach der nächsten CRM-Synchronisation wird die Liste erneut bewertet.'}</span></div>}
            {visibleItems.length > 0 && <div className={`worklist-list ${compact ? 'worklist-list-compact' : ''}`}>
              {visibleItems.slice(0, compact ? 6 : 100).map(item => (
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
                {item.crmTaskUrl && <a className="secondary-button worklist-open-link" href={item.crmTaskUrl} target="_blank" rel="noopener noreferrer">Aufgabe im CRM öffnen ↗</a>}
                {item.externalUrl && <a className="secondary-button worklist-open-link" href={item.externalUrl} target="_blank" rel="noopener noreferrer">Ziel im CRM öffnen ↗</a>}
                <button className="secondary-button" type="button" onClick={() => void snoozeItem(item)}>Für morgen planen</button>
              </div>
            </article>
              ))}
            </div>}
            {compact && response && visibleItems.length > 6 && <p className="webpart-footnote">Weitere {visibleItems.length - 6} Vorgänge in der vollständigen Arbeitsliste.</p>}
          </div>
        </div>
      )}
    </section>
  )
}
