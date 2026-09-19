import { useCallback, useEffect, useRef, useState } from 'react'
import { useApplicationContext, usePlatformLog, createPlatformLogOperation } from '@hammer2fall/identity-platform-react'
import { workThemes, ruleTitles, themeForRule } from './salesNavigation'
import { ReportsPage } from './ReportsPage'
import { safeCrmUrl } from './ReportEvidence'

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
  const log = usePlatformLog()
  const { activeTenantId, authorizedFetch, user } = useApplicationContext()
  const [response, setResponse] = useState<WorklistResponse | null>(null)
  const [selectedRule, setSelectedRule] = useState<string | null>(null)
  const [theme, setTheme] = useState('all')
  const [page, setPage] = useState(0)
  const versionRef = useRef(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadWorklist = useCallback(async (refresh = false) => {
    if (!user || !activeTenantId) return
    const version = ++versionRef.current
    setLoading(true)
    setError(null)
    const operation = createPlatformLogOperation(log, authorizedFetch)
    try {
      const apiResponse = await operation.fetch(`/api/worklist?refresh=${refresh ? 'true' : 'false'}`)
      if (!apiResponse.ok) throw new Error(`Arbeitsliste antwortete mit HTTP ${apiResponse.status}.`)
      const payload = await apiResponse.json() as WorklistResponse
      if (version === versionRef.current) setResponse(payload)
    } catch (reason) {
      if (version !== versionRef.current) return
      operation.log('Error', 'WorklistWidget operation failed', { category: 'Sales.WorklistWidget', tenantId: activeTenantId })
      setResponse(null)
      setError(reason instanceof Error ? reason.message : 'Die Arbeitsliste ist nicht erreichbar.')
    } finally {
      if (version === versionRef.current) setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, user, log])

  useEffect(() => {
    void loadWorklist(true)
    const refreshTimer = window.setInterval(() => void loadWorklist(false), 60_000)
    return () => { window.clearInterval(refreshTimer); versionRef.current++ }
  }, [loadWorklist])

  useEffect(() => {
    if (selectedRule && response && !response.rules.some(rule => rule.code === selectedRule))
      setSelectedRule(null)
  }, [response, selectedRule])

  const snoozeItem = async (item: WorklistItem) => {
    setError(null)
    const operation = createPlatformLogOperation(log, authorizedFetch)
    try {
      const apiResponse = await operation.fetch(`/api/worklist/${item.id}/snooze`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tomorrow: true }),
      })
      if (!apiResponse.ok) throw new Error(`Vorgang konnte nicht aktualisiert werden (HTTP ${apiResponse.status}).`)
      const updated = await apiResponse.json() as WorklistItem
      setResponse(current => current ? { ...current, items: current.items.filter(candidate => candidate.id !== updated.id) } : current)
    } catch (reason) {
      operation.log('Error', 'WorklistWidget operation failed', { category: 'Sales.WorklistWidget', tenantId: activeTenantId })
      setError(reason instanceof Error ? reason.message : `Der Vorgang „${item.title}“ konnte nicht aktualisiert werden.`)
    }
  }

  const formatDate = (value: string | null) => value
    ? new Date(value).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
    : 'kein Termin'

  const visibleItems = response
    ? response.items.filter(item => selectedRule ? item.sourceRuleCode === selectedRule : theme === 'all' || themeForRule(item.sourceRuleCode) === theme)
    : []
  const currentPage = Math.min(page, Math.max(0, Math.ceil(visibleItems.length / 25) - 1))
  const meetingView = theme === 'meetings' && !selectedRule
  const select = (nextTheme: string, rule: string | null = null) => { setTheme(nextTheme); setSelectedRule(rule); setPage(0) }

  return (
    <section className="sales-card worklist-card webpart-card">
      <div className="card-heading">
        <div>
          <p className="sales-eyebrow">{response?.teamView ? 'TEAM-ARBEITSLISTE' : 'MEINE ARBEITSLISTE'}</p>
          <h2>{meetingView ? 'Meeting Report' : `${visibleItems.length} offene Vorgänge`}</h2>
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
          <nav className="worklist-rule-nav sales-section-nav" aria-label="Arbeit nach Themen">
            <p className="worklist-rule-nav-title">Arbeit</p>
            {workThemes.map(group => <div key={group.key}>
              <button className={`worklist-rule-button${theme === group.key && !selectedRule ? ' is-active' : ''}`} aria-current={theme === group.key && !selectedRule ? 'page' : undefined} onClick={() => select(group.key)}><span>{group.title}</span>{group.key !== 'meetings' && <strong>{response.items.filter(item => themeForRule(item.sourceRuleCode) === group.key).length}</strong>}</button>
              {theme === group.key && response.rules.filter(rule => themeForRule(rule.code) === group.key).map(rule => <button key={rule.code} className={`worklist-rule-button worklist-subtopic${selectedRule === rule.code ? ' is-active' : ''}`} aria-current={selectedRule === rule.code ? 'page' : undefined} onClick={() => select(group.key, rule.code)} title={`${rule.code}: ${rule.description ?? rule.name}`}><span>{ruleTitles[rule.code] ?? rule.name}</span><strong>{response.items.filter(item => item.sourceRuleCode === rule.code).length}</strong></button>)}
            </div>)}
            <button className={`worklist-rule-button${theme === 'all' ? ' is-active' : ''}`} type="button" onClick={() => select('all')}>
              <span>Alle Vorgänge</span><strong>{response.items.length}</strong>
            </button>
            {response.items.some(item => themeForRule(item.sourceRuleCode) === 'other') && <button className={theme === 'other' ? 'is-active' : ''} onClick={() => select('other')}>Weitere Vorgänge</button>}
          </nav>
          <div className="worklist-results">
            {meetingView ? <ReportsPage meetingOnly /> : <>
            <h3>{selectedRule ? ruleTitles[selectedRule] ?? response.rules.find(r => r.code === selectedRule)?.name : workThemes.find(g => g.key === theme)?.title ?? (theme === 'all' ? 'Alle Vorgänge' : 'Weitere Vorgänge')}</h3>
            {visibleItems.length === 0 && <div className="worklist-empty"><strong>Keine offenen Vorgänge in dieser Auswahl</strong><span>Nach der nächsten CRM-Synchronisation wird die Liste erneut bewertet.</span></div>}
            {visibleItems.length > 0 && <div className={`worklist-list ${compact ? 'worklist-list-compact' : ''}`}>
              {visibleItems.slice(compact ? 0 : currentPage * 25, compact ? 6 : (currentPage + 1) * 25).map(item => (
            <article className="worklist-item" key={item.id}>
              <div className="worklist-item-main">
                <div className="worklist-item-topline">
                  <span className={`priority-badge priority-${item.priorityBand}`}>{{ critical: 'Dringend', high: 'Hoch', medium: 'Normal', low: 'Niedrig' }[item.priorityBand]}</span>
                  <span className="worklist-type">{item.workItemTypeName}</span>
                </div>
                <h3>{item.title}</h3>
                <p>{item.reason}</p>
                <div className="worklist-meta">
                  <span>Fällig: <strong>{formatDate(item.dueAt)}</strong></span>
                  {item.ownerName && <span>Zuständig: <strong>{item.ownerName}</strong></span>}
                </div>
              </div>
              <div className="worklist-actions">
                {safeCrmUrl(item.crmTaskUrl) && <a className="secondary-button worklist-open-link" href={safeCrmUrl(item.crmTaskUrl)!} target="_blank" rel="noopener noreferrer">Aufgabe im CRM öffnen ↗</a>}
                {safeCrmUrl(item.externalUrl) && <a className="secondary-button worklist-open-link" href={safeCrmUrl(item.externalUrl)!} target="_blank" rel="noopener noreferrer">Ziel im CRM öffnen ↗</a>}
                <button className="secondary-button" type="button" onClick={() => void snoozeItem(item)}>Für morgen planen</button>
              </div>
            </article>
              ))}
            </div>}
            {compact && response && visibleItems.length > 6 && <p className="webpart-footnote">Weitere {visibleItems.length - 6} Vorgänge in der vollständigen Arbeitsliste.</p>}
            {!compact && visibleItems.length > 25 && <div className="button-row"><button className="secondary-button" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>Zurück</button><span>Seite {currentPage + 1} von {Math.ceil(visibleItems.length / 25)}</span><button className="secondary-button" disabled={(currentPage + 1) * 25 >= visibleItems.length} onClick={() => setPage(currentPage + 1)}>Weiter</button></div>}
            </>}
          </div>
        </div>
      )}
    </section>
  )
}
