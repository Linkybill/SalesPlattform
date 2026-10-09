import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import * as L from 'leaflet'
import type { FeatureCollection } from 'geojson'
import 'leaflet/dist/leaflet.css'
import { useApplicationContext, usePlatformLog, createPlatformLogOperation } from '@hammer2fall/identity-platform-react'
import { SupplementaryReports } from './SpecifiedReports'
import { useEvidence } from './ReportEvidence'
import { AnnualTargetsEditor } from './AnnualTargetsEditor'
import { DashboardContentEditor, type LayoutNode, type ReportDefinition } from './DashboardContentEditor'
import { dashboardTabs, reportSections, filterReportLayout } from './salesNavigation'
import { DashboardNavigation, navigateDashboardSection, useDashboardSection } from './DashboardNavigation'
import { ReportEvidenceProvider, MetricTile, MetricLink, EvidenceChart, EvidenceTable, safeCrmUrl, type ReportEvidence } from './ReportEvidence'

type Breakdown = { label: string; count: number; amount: number | null }
type Cockpit = {
  periodName: string; currency: string; wonRevenue: number; annualTarget: number; targetAttainmentPercent: number; winRatePercent: number; pipelineAmount: number; pipelineCoveragePercent: number; averageSalesCycleDays: number | null; arr: number; newRevenue: number; existingRevenue: number; staleDealCount: number; expiringContractCount: number; funnel: { name: string; dealCount: number; amount: number; conversionPercent: number | null }[]; actionPoints: { id: string; title: string; reason: string | null; ruleCode: string | null; priorityScore: number; dueAt: string | null }[]
}
type Team = { periodName: string; timeSharePercent: number; members: { ownerId: string; name: string; wonRevenue: number; target: number; attainmentPercent: number; pace: number; openDealCount: number; pipelineAmount: number; appointmentCount: number; callCount: number; conversationCount: number; appointmentTypes: Breakdown[] }[]; appointmentTypes: Breakdown[] }
type Meetings = { periodName: string; newAppointments: number; currentWeekAppointments: number; plannedAppointments: number; completedAppointments: number; cancelledAppointments: number; rescheduledAppointments: number; noShowAppointments: number; completionRatePercent: number; noShowRatePercent: number; rescheduleRatePercent: number; byType: Breakdown[]; byStatus: Breakdown[] }
type Analysis = { periodName: string; byProduct: Breakdown[]; byIndustry: Breakdown[]; byRegion: Breakdown[]; lossReasons: Breakdown[]; stageDwell: { stage: string; dealCount: number; averageDays: number }[]; crossSelling: { customerId: string; customerName: string; categories: string[]; categoryCount: number }[] }
type Customers = { postalAreas?: FeatureCollection | null; periodName: string; customers: { currency?: string | null; industry?: string | null; status?: string | null; products?: string[]; id: string; name: string; ownerName: string | null; countryCode: string | null; postalCode: string | null; city: string | null; regionCode: string | null; addressLine1: string | null; houseNumber: string | null; latitude: number | null; longitude: number | null; lifetimeRevenue: number | null; lastContactAt: string | null; openDealCount: number; needsReview: boolean; externalUrl: string | null }[]; unmappedCount: number; regions: Breakdown[] }
type Goals = { periodName: string; timeSharePercent: number; members: { ownerId: string; name: string; target: number; achieved: number; attainmentPercent: number; timeSharePercent: number; pace: number; status: string }[] }
type Cleanup = { duplicates: { id: string; customerA: string; customerB: string; score: number; confidence: string; status: string; matchDetailsJson: string | null }[]; qualityFindings: Breakdown[]; openFindingCount: number }
type Service = { periodName: string; totalCases: number; openCases: number; overdueCases: number; urgentCases: number; byStatus: Breakdown[]; byPriority: Breakdown[]; urgentItems: { id: string; subject: string; status: string; priority: string; openedAt: string | null; dueAt: string | null; customerName: string | null; externalUrl: string | null }[] }
type Commercial = { periodName: string; offerCount: number; openOfferCount: number; offerAmount: number; overdueOfferCount: number; orderCount: number; openOrderCount: number; orderAmount: number; overdueOrderCount: number; invoiceCount: number; openInvoiceCount: number; openInvoiceAmount: number; overdueInvoiceCount: number; statusBreakdown: Breakdown[] }
type LayoutResponse = { nodes: LayoutNode[]; availableReports: ReportDefinition[]; isDefault: boolean; canEdit: boolean }
type Dashboard = { canManageAnnualTargets?: boolean; sourceSync?: { mode: string; status: string; finishedAt: string; failedRecords: number } | null; evidence: ReportEvidence; generatedAt: string; timeframe: string; periodName: string; layout: LayoutResponse; cockpit: Cockpit | null; team: Team | null; meetings: Meetings | null; analysis: Analysis | null; customers: Customers | null; goals: Goals; cleanup: Cleanup | null; service: Service; commercial: Commercial }


export function ReportsPage({ forceEdit = false, meetingOnly = false, workReport }: { forceEdit?: boolean; meetingOnly?: boolean; workReport?: 'dormant' | 'followups' | 'renewals' }) {
  const embedded = meetingOnly || !!workReport
  const log = usePlatformLog()
  const { activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const section = useDashboardSection('reports')
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [chosenTimeframe, setTimeframe] = useState(embedded ? 'month' : reportSections.find(s => s.key === section)?.timeframe ?? 'year')
  const timeframe = (!embedded && reportSections.find(s => s.key === section)?.timeframe) || chosenTimeframe
  const requestVersion = useRef(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(forceEdit)
  const [editingTargets, setEditingTargets] = useState(false)
  const [draftNodes, setDraftNodes] = useState<LayoutNode[]>([])
  const [savingLayout, setSavingLayout] = useState(false)
  const [layoutMessage, setLayoutMessage] = useState<string | null>(null)

  useEffect(() => {
    const choice = reportSections.find(s => s.key === section)
    if (!embedded && choice?.timeframe) setTimeframe(choice.timeframe)
  }, [embedded, section])

  const load = useCallback(async () => {
    if (!user || !activeTenantId) return
    const version = ++requestVersion.current
    setLoading(true)
    setError(null)
    const operation = createPlatformLogOperation(log, authorizedFetch)
    try {
      const response = await operation.fetch(`/api/reports/dashboard?timeframe=${timeframe}`)
      if (!response.ok) throw new Error(`Reports antworteten mit HTTP ${response.status}.`)
      const payload = await response.json() as Dashboard
      if (version !== requestVersion.current) return
      setDashboard(payload)
      setDraftNodes(payload.layout.nodes)
    } catch (reason) {
      if (version !== requestVersion.current) return
      operation.log('Error', 'ReportsPage operation failed', { category: 'Sales.ReportsPage', tenantId: activeTenantId })
      setError(reason instanceof Error ? reason.message : 'Die Reports sind nicht erreichbar.')
    } finally {
      if (version === requestVersion.current) setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, timeframe, user, log])

  useEffect(() => { setDashboard(null); void load(); return () => { requestVersion.current++ } }, [load])

  const available = reportSections.filter(s => s.reports.some(key => dashboard?.layout.availableReports.some(r => r.key === key && r.allowed)))
  const selected = available.find(s => s.key === section) ?? available[0]
  const selectSection = (key: string) => {
    navigateDashboardSection('reports', key)
    const choice = reportSections.find(s => s.key === key)
    if (choice?.timeframe) setTimeframe(choice.timeframe)
  }
  const canShowAnalysis = !embedded && dashboard?.analysis && filterReportLayout(dashboard.layout.nodes, ['analysis']).length > 0
  const Container = embedded ? 'div' : 'main'

  const saveLayout = async () => {
    if (!dashboard) return
    setSavingLayout(true)
    setError(null)
    setLayoutMessage(null)
    const operation = createPlatformLogOperation(log, authorizedFetch)
    try {
      const response = await operation.fetch('/api/reports/layout', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ nodes: draftNodes }),
      })
      const responseText = await response.text()
      let payload: (LayoutResponse & { message?: string }) | null = null
      if (responseText) {
        try { payload = JSON.parse(responseText) as LayoutResponse & { message?: string } } catch { /* use the status below */ }
      }
      if (!response.ok) throw new Error(payload?.message ?? `Reportseite konnte nicht gespeichert werden (HTTP ${response.status}).`)
      if (!payload?.nodes) throw new Error('Die gespeicherte Reportseite wurde vom Backend nicht bestätigt.')
      setDashboard(current => current ? { ...current, layout: payload } : current)
      setDraftNodes(payload.nodes)
      setEditing(false)
      setLayoutMessage('Die Reportseite wurde für diesen Mandanten gespeichert.')
    } catch (reason) {
      operation.log('Error', 'ReportsPage operation failed', { category: 'Sales.ReportsPage', tenantId: activeTenantId })
      setError(reason instanceof Error ? reason.message : 'Die Reportseite konnte nicht gespeichert werden.')
    } finally {
      setSavingLayout(false)
    }
  }

  return (
    <Container className={embedded ? 'meeting-report-embedded' : 'sales-page reports-page sales-dashboard'}>
      {!embedded && <DashboardNavigation area="reports" activeKey={selected?.key ?? section} />}
      {!embedded && <nav className="dashboard-secondary-nav" aria-label="Weitere Auswertungen"><span>Weitere Auswertungen</span>{available.filter(s => !dashboardTabs.some(tab => tab.area === 'reports' && tab.key === s.key)).map(s => <button type="button" key={s.key} className={s.key === selected?.key ? 'is-active' : ''} aria-current={s.key === selected?.key ? 'page' : undefined} onClick={() => selectSection(s.key)}>{s.title}</button>)}</nav>}
      <section className="dashboard-section-heading">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · {meetingOnly ? 'ARBEIT' : 'STEUERUNG'}</p>
          <h2>{meetingOnly ? 'Meeting Report' : selected?.title ?? 'Steuerung'}</h2>
          <p className="sales-lead">{meetingOnly ? 'Welche Termine wurden vereinbart, stehen an oder haben nicht stattgefunden?' : 'Klicke auf eine Kennzahl, ein Kuchenstück oder einen Diagrammbalken für die zugrunde liegenden Daten.'}</p>
        </div>
        <div className="report-toolbar">
          <label>Zeitraum
            <select value={timeframe} disabled={!embedded && !!selected?.timeframe} onChange={event => setTimeframe(event.target.value)}>
              <option value="month">Monat</option>
              <option value="year">Geschäftsjahr</option>
              <option value="lifetime">Lifetime</option>
            </select>
          </label>
          <div className="report-toolbar-actions"><button className="secondary-button" type="button" onClick={() => void load()} disabled={loading || savingLayout}>{loading ? 'Wird geladen …' : 'Reports aktualisieren'}</button>{!embedded && dashboard?.layout.canEdit && <button className={editing ? 'primary-button' : 'secondary-button'} type="button" onClick={() => { setEditing(current => !current); setLayoutMessage(null) }} disabled={savingLayout}>{editing ? 'Bearbeitung schließen' : 'Layout bearbeiten'}</button>}</div>
        </div>
      </section>
      {canShowAnalysis && !selected?.reports.includes('analysis') && <p><button className="secondary-button" type="button" onClick={() => selectSection('analysis')}>Diagramme anzeigen</button></p>}
      {!embedded && dashboard?.canManageAnnualTargets && <p><button type="button" className="secondary-button" onClick={() => setEditingTargets(true)}>Jahresziel festlegen</button></p>}
      {!embedded && dashboard?.canManageAnnualTargets && editingTargets && <AnnualTargetsEditor key={`${activeTenantId}`} onClose={() => setEditingTargets(false)} onSaved={async () => {
        setEditingTargets(false)
        await load()
        setLayoutMessage('Jahresziele wurden in der Salesplattform gespeichert.')
      }} />}
      {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}
      {!dashboard && loading && <section className="sales-card report-loading">Reports werden aus der Tenant-Datenbank geladen …</section>}
      {dashboard && (
        <ReportEvidenceProvider evidence={dashboard.evidence} generatedAt={dashboard.generatedAt}>
          <div className="report-status">Berechnet am {new Date(dashboard.generatedAt).toLocaleString('de-DE')} · {dashboard.periodName} · Aus zuletzt synchronisierten CRM-Daten, kein Live-CRM. Bestands- und Jahreskennzahlen weisen ihren eigenen Zeitraum aus.</div>
          <p className="report-status">{dashboard.sourceSync ? `Letzter beendeter Import: ${new Date(dashboard.sourceSync.finishedAt).toLocaleString('de-DE')} · ${dashboard.sourceSync.mode} · Status: ${dashboard.sourceSync.status} · ${dashboard.sourceSync.failedRecords} fehlgeschlagene Datensätze. Ein Teilimport bestätigt nicht die Aktualität aller Entitäten.` : 'Noch kein beendeter CRM-Import dokumentiert; die Vollständigkeit der Daten ist nicht bestätigt.'}</p>
          {layoutMessage && <div className="message success-message">{layoutMessage}</div>}
          {editing && dashboard.layout.canEdit
            ? <><DashboardContentEditor nodes={draftNodes} reports={dashboard.layout.availableReports} onChange={setDraftNodes} /><div className="content-editor-footer"><button className="secondary-button" type="button" onClick={() => { setDraftNodes(dashboard.layout.nodes); setEditing(false) }} disabled={savingLayout}>Änderungen verwerfen</button><button className="primary-button" type="button" onClick={() => void saveLayout()} disabled={savingLayout}>{savingLayout ? 'Wird gespeichert …' : 'Reportseite speichern'}</button></div></>
            : workReport ? (dashboard.analysis && filterReportLayout(dashboard.layout.nodes, ['contact-reports']).length > 0 ? <SupplementaryReports area={workReport} /> : <p>Kontaktreports sind im Layout ausgeblendet oder nicht freigegeben.</p>)
            : meetingOnly ? <LayoutRenderer nodes={filterReportLayout(dashboard.layout.nodes, ['meetings'])} dashboard={dashboard} />
            : <section className="sales-section-content"><LayoutRenderer nodes={filterReportLayout(dashboard.layout.nodes, selected?.reports ?? [])} dashboard={dashboard} /><p className="webpart-footnote">Die sichtbaren Report-Komponenten folgen dem gespeicherten Mandantenlayout. Ausgeblendete Reports können unter „Layout bearbeiten“ wieder eingeblendet werden.</p></section>}
        </ReportEvidenceProvider>
      )}
    </Container>
  )
}

function LayoutRenderer({ nodes, dashboard }: { nodes: LayoutNode[]; dashboard: Dashboard }) {
  if (!nodes.length) return <p className="message info-message">Dieser Report ist im Mandantenlayout ausgeblendet oder für deine Rolle nicht freigegeben.</p>
  return <div className="dashboard-page-layout">{nodes.filter(node => node.visible && node.allowed).map(node => <div className="dashboard-layout-item" style={{ gridColumn: `span ${layoutSpan(node.columns)}` }} key={node.id}><LayoutNodeView node={node} dashboard={dashboard} /></div>)}</div>
}

function LayoutNodeView({ node, dashboard }: { node: LayoutNode; dashboard: Dashboard }) {
  if (!node.visible || !node.allowed) return null
  if (node.type === 'heading') return <section className="dashboard-heading-block"><h2>{node.title}</h2></section>
  if (node.type === 'text') return <p className="dashboard-text-block">{node.text}</p>
  if (node.type === 'grid') {
    const gridColumns = layoutSpan(node.gridColumns ?? 12)
    return <div className="dashboard-grid-node" style={{ gridTemplateColumns: `repeat(${gridColumns}, minmax(0, 1fr))` }}>{node.children.filter(child => child.visible && child.allowed).map(child => <div className="dashboard-grid-item" style={{ gridColumn: `span ${gridSpan(child.columns, gridColumns)}` }} key={child.id}><LayoutNodeView node={child} dashboard={dashboard} /></div>)}</div>
  }
  if (node.type === 'accordion') return <section className="dashboard-accordion"><details open><summary>{node.title || 'Abschnitt'}</summary><div className="dashboard-accordion-content"><LayoutChildren nodes={node.children} dashboard={dashboard} /></div></details></section>
  if (node.type === 'tabs') return <DashboardTabs node={node} dashboard={dashboard} />
  if (node.type !== 'report' || !node.reportKey) return null
  const definition = dashboard.layout.availableReports.find(report => report.key === node.reportKey)
  return <section className="webpart-slot"><div className="webpart-label"><span>{node.title ?? definition?.title ?? node.reportKey}</span><small>{definition?.description}</small></div>{renderWebpart(node.reportKey, dashboard)}</section>
}

function LayoutChildren({ nodes, dashboard }: { nodes: LayoutNode[]; dashboard: Dashboard }) {
  return <div className="dashboard-child-layout">{nodes.filter(node => node.visible && node.allowed).map(node => <div className="dashboard-layout-item" style={{ gridColumn: `span ${layoutSpan(node.columns)}` }} key={node.id}><LayoutNodeView node={node} dashboard={dashboard} /></div>)}</div>
}

function DashboardTabs({ node, dashboard }: { node: LayoutNode; dashboard: Dashboard }) {
  const [active, setActive] = useState(0)
  const index = Math.min(active, Math.max(0, node.children.length - 1))
  return <section className="dashboard-tabs"><div className="dashboard-tab-buttons">{node.children.map((child, childIndex) => <button className={childIndex === index ? 'is-active' : ''} type="button" key={child.id} onClick={() => setActive(childIndex)}>{child.title || `Tab ${childIndex + 1}`}</button>)}</div>{node.children[index] && <div className="dashboard-tab-content"><LayoutChildren nodes={[node.children[index]]} dashboard={dashboard} /></div>}</section>
}

function renderWebpart(key: string, dashboard: Dashboard) {
  switch (key) {
    case 'cockpit': return dashboard.cockpit ? <CockpitWebpart report={dashboard.cockpit} /> : null
    case 'team': return dashboard.team ? <TeamWebpart report={dashboard.team} /> : null
    case 'meetings': return dashboard.meetings ? <MeetingsWebpart report={dashboard.meetings} /> : null
    case 'analysis': return dashboard.analysis ? <AnalysisWebpart report={dashboard.analysis} /> : null
    case 'customers': return dashboard.customers ? <CustomersWebpart report={dashboard.customers} /> : null
    case 'goals': return <GoalsWebpart report={dashboard.goals} />
    case 'cleanup': return dashboard.cleanup ? <CleanupWebpart report={dashboard.cleanup} /> : null
    case 'service': return <ServiceWebpart report={dashboard.service} />
    case 'contact-reports': return dashboard.analysis ? <WebpartCard><h2>Kontakt- und Wiedervorlagereports</h2><SupplementaryReports area="dormant" /><SupplementaryReports area="followups" /><SupplementaryReports area="renewals" /></WebpartCard> : null
    case 'commercial': return <CommercialWebpart report={dashboard.commercial} />
    default: return null
  }
}

function layoutSpan(value: number | null | undefined) {
  return Math.max(1, Math.min(12, value ?? 12))
}

function gridSpan(value: number | null | undefined, gridColumns: number) {
  return Math.max(1, Math.min(gridColumns, Math.ceil(layoutSpan(value) * gridColumns / 12)))
}

function WebpartCard({ children }: { children: ReactNode }) {
  return <div className="sales-card webpart-card">{children}</div>
}

function CockpitWebpart({ report }: { report: Cockpit }) {
  return <WebpartCard>
    <div className="card-heading"><div><p className="sales-eyebrow">COCKPIT · {report.periodName.toUpperCase()}</p><h2>Vertrieb auf einen Blick</h2></div></div>
    <div className="report-kpi-grid">
      {['won', 'won-count', 'annual-target', 'attainment', 'win-rate', 'pipeline', 'coverage', 'cycle', 'recurring', 'stale', 'expiring'].map(key => <MetricTile key={key} metricKey={key} />)}
    </div>
    <h3>Offene Pipeline nach Stufe</h3><EvidenceChart prefix="funnel:" />
  <SupplementaryReports area="cockpit" /></WebpartCard>
}

function TeamWebpart({ report }: { report: Team }) {
  const labels = Object.fromEntries(report.members.flatMap(member => ['calls', 'appointments'].map(key => [`owner:${member.ownerId}:${key}`, member.name])))
  return <WebpartCard>
    <h2>Vertriebsteam · {report.periodName}</h2>
    <p className="muted">Umsatz und Aktivitäten: gewählter Zeitraum. Ziele, Zielerreichung und Pace: aktuelles Geschäftsjahr. Pipeline: aktueller Bestand. Jede Zahl öffnet ihren Nachweis.</p>
    <div className="report-columns">
      <div><h3>Telefonate je Mitarbeiter</h3><EvidenceChart prefix="owner:" suffix=":calls" labels={labels} /></div>
      <div><h3>Termine je Mitarbeiter</h3><EvidenceChart prefix="owner:" suffix=":appointments" labels={labels} /></div>
    </div>
    <div className="table-wrap"><table>
      <thead><tr><th>Mitarbeiter</th><th>Umsatz</th><th>Jahresziel</th><th>Erreichung GJ</th><th>Pace GJ</th><th>Pipeline</th><th>Termine</th><th>Anrufe / Gespräche</th></tr></thead>
      <tbody>{report.members.map(member => <tr key={member.ownerId}>
        <td><strong>{member.name}</strong></td>
        {['won', 'target', 'attainment', 'pace', 'pipeline', 'appointments'].map(key => <td key={key}><MetricLink metricKey={`owner:${member.ownerId}:${key}`} /></td>)}
        <td><MetricLink metricKey={`owner:${member.ownerId}:calls`} /> / <MetricLink metricKey={`owner:${member.ownerId}:conversations`} /></td>
      </tr>)}</tbody>
    </table></div>
  <SupplementaryReports area="team" /></WebpartCard>
}

function MeetingsWebpart({ report }: { report: Meetings }) {
  const [list, setList] = useState('meetings:preparation')
  return <WebpartCard>
    <h2>Termine · {report.periodName}</h2>
    <div className="report-kpi-grid">{['new', 'week', 'planned', 'status-completed', 'status-cancelled', 'status-rescheduled', 'completion', 'no-show', 'reschedule'].map(key => <MetricTile key={key} metricKey={`meetings:${key}`} />)}</div>
    <div className="report-columns"><div><h3>Nach Status</h3><EvidenceChart prefix="meeting-status:" /></div><div><h3>Nach Terminart</h3><EvidenceChart prefix="meeting-type:" /></div></div>
    <h3>Terminlisten und Vorbereitung</h3>
    <label>Liste auswählen<select value={list} onChange={e => setList(e.target.value)}>
      <option value="meetings:preparation">Terminvorbereitung · nächste Tage</option>
      <option value="meetings:week">Termine dieser Woche</option>
      <option value="meetings:week-cancelled">Abgesagt · diese Woche</option>
      <option value="meetings:week-rescheduled">Verschoben · diese Woche</option>
      <option value="meetings:week-no-show">Nicht stattgefunden · diese Woche</option>
      <option value="meetings:new">Neu angelegt im Zeitraum</option>
      <option value="meetings:planned">Alle Termine im Zeitraum</option>
      <option value="meetings:status-completed">Durchgeführt im Zeitraum</option>
      <option value="meetings:status-cancelled">Abgesagt im Zeitraum</option>
      <option value="meetings:status-rescheduled">Verschoben im Zeitraum</option>
      <option value="meetings:missed">Nicht stattgefunden / verschoben im Zeitraum</option>
      <option value="meetings:unclassified">Ohne eindeutige Erst-/Folgetermin-Zuordnung</option>
    </select></label>
    <MetricTile metricKey={list} />
    <EvidenceTable key={list} metricKey={list} />
  <SupplementaryReports area="meetings" /></WebpartCard>
}

function AnalysisWebpart({ report }: { report: Analysis }) {
  const { evidence } = useEvidence()
  const [allGroups, setAllGroups] = useState(true)
  const chartPrefix = (prefix: string) => allGroups && ['product:', 'industry:'].includes(prefix) && Object.keys(evidence.metrics).some(k => k.startsWith('full-' + prefix)) ? 'full-' + prefix : prefix
  return <WebpartCard>
    <h2>Umsatz, Prozess und Chancen · {report.periodName}</h2>
    <label className="report-percent-toggle"><input type="checkbox" checked={allGroups} onChange={e => setAllGroups(e.target.checked)} />Alle Produkt- und Branchengruppen anzeigen</label>
    <div className="report-columns report-columns-three">
      {[
        ['Umsatz nach Branche', 'industry:', null],
        ['Umsatz nach Produkt', 'product:', null],
        ['Top-Produkte nach Anzahl', 'product-count:', 'analysis:products-count'],
        ['Erstgespräche nach Branche', 'meeting-first-industry:', 'analysis:first-meetings'],
        ['Folgetermine nach Branche', 'meeting-follow-up-industry:', 'analysis:follow-up-meetings'],
        ['Offene Angebots-Deals nach Branche · aktueller Bestand', 'offer-deal-industry:', 'analysis:offer-deals'],
        ['Offene Angebotsbelege nach Branche · im Zeitraum', 'offer-document-industry:', 'analysis:offer-documents'],
        ['Regionen', 'region:', null],
        ['Verlustgründe', 'loss:', null],
        ['Verweildauer je Stufe', 'dwell:', null],
        ['Produktkategorien pro Kunde', 'cross:', null],
      ].map(([title, prefix, total]) => <div key={prefix!}><h3>{title}</h3>{total && <MetricTile metricKey={total} />}<EvidenceChart prefix={chartPrefix(prefix!)} title={title!} variant={['product:', 'industry:', 'meeting-first-industry:', 'meeting-follow-up-industry:', 'offer-deal-industry:', 'offer-document-industry:'].includes(prefix!) ? 'pie' : 'bar'} /></div>)}
    </div>
    <details><summary>Terminarten prüfen</summary><p>Erstgespräche und Folgetermine verwenden die Zuordnung in den Tenant-AppSettings. Andere oder mehrdeutig zugeordnete Terminarten stehen hier zur Prüfung.</p><MetricTile metricKey="meetings:unclassified" /><EvidenceTable metricKey="meetings:unclassified" /></details>
  <SupplementaryReports area="analysis" />{report.periodName === "Lifetime" && <SupplementaryReports area="lifetime" />}</WebpartCard>
}

function CustomersWebpart({ report }: { report: Customers }) {
  const [mapMode, setMapMode] = useState<'points' | 'revenue' | 'count'>('points')
  const [page, setPage] = useState(0)
  const { generatedAt } = useEvidence()
  const [filters, setFilters] = useState({ owner: '', industry: '', product: '', status: '', contact: '', revenue: '' })
  const updateFilter = (key: keyof typeof filters, value: string) => { setFilters(old => ({ ...old, [key]: value })); setPage(0) }
  const filtered = report.customers.filter(c =>
    (!filters.owner || c.ownerName === filters.owner) &&
    (!filters.industry || c.industry === filters.industry) &&
    (!filters.product || c.products?.includes(filters.product)) &&
    (!filters.status || c.status === filters.status) &&
    (!filters.revenue || c.lifetimeRevenue !== null && c.lifetimeRevenue >= Number(filters.revenue)) &&
    (!filters.contact || (filters.contact === 'missing' ? !c.lastContactAt : !!c.lastContactAt && Date.parse(generatedAt) - Date.parse(c.lastContactAt) >= Number(filters.contact) * 86400000)))
  const currentPage = Math.min(page, Math.max(0, Math.ceil(filtered.length / 25) - 1))
  const points = useMemo(() => filtered.map(customer => toCustomerMapPoint(customer)).filter((point): point is CustomerMapPoint => point !== null), [filtered])
  const exactPoints = points.filter(point => !point.isFallback).length
  const fallbackPoints = points.length - exactPoints

  return <WebpartCard>
    <div className="card-heading"><div><p className="sales-eyebrow">KUNDENSTAMM · KARTE</p><h2>Kunden und Gebiete</h2></div><span className="worklist-refresh">{report.customers.length} Kunden · {report.unmappedCount} ohne exakte Koordinaten</span></div>
    <div className="report-customer-filters" role="group" aria-label="Kunden filtern">
      {(['owner', 'industry', 'product', 'status'] as const).map((key, index) => {
        const options = [...new Set(report.customers.flatMap(c => key === 'product' ? c.products ?? [] : [key === 'owner' ? c.ownerName : c[key]]).filter((v): v is string => !!v))].sort()
        return <label key={key}>{['Betreuer', 'Branche', 'Produkt', 'Kundenstatus'][index]}<select value={filters[key]} onChange={e => updateFilter(key, e.target.value)}><option value="">Alle</option>{options.map(v => <option key={v}>{v}</option>)}</select></label>
      })}
      <label>Letzter Kontakt<select value={filters.contact} onChange={e => updateFilter('contact', e.target.value)}><option value="">Alle</option><option value="30">Vor mindestens 30 Tagen</option><option value="90">Vor mindestens 90 Tagen</option><option value="180">Vor mindestens 180 Tagen</option><option value="missing">Nicht dokumentiert</option></select></label>
      <label>Umsatz ab<input type="number" min="0" value={filters.revenue} onChange={e => updateFilter('revenue', e.target.value)} /></label>
    </div>
    <p className="muted">{filtered.length} von {report.customers.length} Kunden. Filter gelten für Kundenkarte und Kundentabelle; die Gebietsreports darunter zeigen den gesamten Bestand.</p>
    <label>Kartendarstellung<select value={mapMode} onChange={e => setMapMode(e.target.value as typeof mapMode)}><option value="points">Kundenpunkte</option><option value="revenue" disabled={!report.postalAreas}>PLZ-Flächen nach Umsatz</option><option value="count" disabled={!report.postalAreas}>PLZ-Flächen nach Kundenanzahl</option></select></label>
    {!report.postalAreas && <p className="muted">Für die Flächenansicht fehlen hinterlegte PLZ-Gebietsgrenzen. Kundenpunkte und Ranglisten sind verfügbar.</p>}
    {mapMode !== 'points' && <p className="muted">Dunklere Flächen bedeuten höhere Werte innerhalb der gefilterten Kunden. Graue Flächen haben keine vergleichbaren Umsatzwerte. Klick zeigt den Wert; Gebiete ohne hinterlegte Grenze erscheinen weiterhin in der Tabelle.</p>}
    <div className="customer-map">
      <div className="customer-map-grid">
        <CustomerLeafletMap points={points} customers={filtered} postalAreas={report.postalAreas} mode={mapMode} />
        {points.length === 0 && <div className="customer-map-empty">Für die Kunden sind noch keine verwertbaren Standortdaten vorhanden.</div>}
      </div>
      <div className="customer-map-copy"><strong>Deutschland als Startausschnitt</strong><span>Die Karte ist interaktiv. Mit den Zoom-Schaltflächen oder dem Mausrad kann der Ausschnitt verändert werden; „Deutschland“ setzt den Startausschnitt zurück und „Alle Standorte“ passt ihn an alle vorhandenen Kunden an. Exakte Koordinaten werden bevorzugt, ansonsten werden die verfügbaren Standortdaten als Näherung dargestellt. Dichte Standorte werden beim Herauszoomen gebündelt. Bei einheitlicher Währung zeigt die Punktgröße den Umsatz; bei verschiedenen Währungen bleiben die Punkte gleich groß.</span><small>{points.length} Kartenpositionen · {exactPoints} exakte Standorte · {fallbackPoints} Standort-Näherungen · {filtered.length - points.length} ohne Kartenposition</small></div>
    </div>
    <div className="table-wrap"><table><thead><tr><th>Kunde</th><th>Betreuer</th><th>Standort</th><th>Gewonnener Umsatz (Lifetime)</th><th>Offene Deals</th><th></th></tr></thead><tbody>{filtered.slice(currentPage * 25, (currentPage + 1) * 25).map(customer => <tr key={customer.id}><td><strong>{customer.name}</strong>{customer.needsReview && <small className="table-note">Prüfen</small>}</td><td>{customer.ownerName ?? '–'}</td><td>{formatCustomerLocation(customer)}</td><td><MetricLink metricKey={`customer:${customer.id}:revenue`} /></td><td><MetricLink metricKey={`customer:${customer.id}:open`} /></td><td>{safeCrmUrl(customer.externalUrl) && <a href={safeCrmUrl(customer.externalUrl)!} target="_blank" rel="noopener noreferrer">CRM ↗</a>}</td></tr>)}</tbody></table></div>
    <div className="button-row"><button className="secondary-button" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>Zurück</button><span>Seite {currentPage + 1} von {Math.max(1, Math.ceil(filtered.length / 25))} · {report.customers.length} Kunden</span><button className="secondary-button" disabled={(currentPage + 1) * 25 >= filtered.length} onClick={() => setPage(currentPage + 1)}>Weiter</button></div>
  <SupplementaryReports area="customers" /></WebpartCard>
}

type CustomerMapPoint = {
  customer: Customers['customers'][number]
  latitude: number
  longitude: number
  isFallback: boolean
  locationBasis: 'exact' | 'city' | 'postal' | 'country'
}

const GERMANY_MAP_CENTER: [number, number] = [51.1657, 10.4515]
const GERMANY_MAP_ZOOM = 6

function CustomerLeafletMap({ points, customers, postalAreas, mode }: { points: CustomerMapPoint[]; customers: Customers['customers']; postalAreas?: FeatureCollection | null; mode: 'points' | 'revenue' | 'count' }) {
  const [mapError, setMapError] = useState<string | null>(null)
  const mapElementRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<L.Map | null>(null)
  const markerLayerRef = useRef<L.LayerGroup | null>(null)

  useEffect(() => {
    if (!mapElementRef.current || mapRef.current) return

    const map = L.map(mapElementRef.current, { zoomControl: true, scrollWheelZoom: true })
      .setView(GERMANY_MAP_CENTER, GERMANY_MAP_ZOOM)
    const markerLayer = L.layerGroup().addTo(map)
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap contributors</a>',
    }).addTo(map)

    mapRef.current = map
    markerLayerRef.current = markerLayer
    const resizeObserver = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => { if (mapRef.current === map) map.invalidateSize() })
    resizeObserver?.observe(mapElementRef.current)
    const resizeTimer = window.setTimeout(() => { if (mapRef.current === map) map.invalidateSize() }, 0)

    return () => {
      window.clearTimeout(resizeTimer)
      resizeObserver?.disconnect()
      markerLayerRef.current = null
      mapRef.current = null
      map.remove()
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    const markerLayer = markerLayerRef.current
    if (!map || !markerLayer) return

    if (mode !== 'points') {
      if (!postalAreas) return
      try {
        const areas = postalAreas.features.filter(feature => {
          const properties = feature.properties
          return ['Polygon','MultiPolygon'].includes(feature.geometry?.type) &&
            typeof properties?.countryCode === 'string' && typeof properties?.postalPrefix === 'string' && properties.postalPrefix.trim().length > 0
        }).map(feature => {
          const country = feature.properties!.countryCode.trim().toUpperCase()
          const prefix = feature.properties!.postalPrefix.trim()
          const matching = customers.filter(c => c.countryCode?.trim().toUpperCase() === country && c.postalCode?.trim().startsWith(prefix))
          const comparable = matching.every(c => c.lifetimeRevenue !== null) && new Set(matching.map(c => c.currency ?? 'EUR')).size <= 1
          const value = mode === 'count' ? matching.length : comparable ? matching.reduce((sum,c) => sum + (c.lifetimeRevenue ?? 0),0) : null
          return {feature, country, prefix, matching, value, currency: matching[0]?.currency ?? 'EUR'}
        })
        const sameCurrency = mode === 'count' || new Set(areas.filter(a => a.matching.length).map(a => a.currency)).size <= 1
        const maximum = Math.max(1,...areas.map(a => a.value ?? 0))
        markerLayer.clearLayers()
        for (const area of areas) {
          const known = area.value !== null && sameCurrency
          const popup = document.createElement('div')
          popup.textContent = area.country + ' · PLZ ' + area.prefix + ' · ' + (known ? area.value!.toLocaleString('de-DE') + (mode === 'count' ? ' Kunden' : ' ' + area.currency) : 'Keine vergleichbare Umsatzsumme') + ' · ' + area.matching.length + ' Kunden'
          L.geoJSON(area.feature, {style: {color:'#475569',weight:1,fillColor:known?'#0369a1':'#9ca3af',fillOpacity: known ? .15 + .7 * Math.max(0,area.value!) / maximum : .25}})
            .bindPopup(popup).addTo(markerLayer)
        }
        if (!areas.length) setMapError('Keine gültigen PLZ-Flächen mit Land und PLZ-Präfix hinterlegt.')
        else setMapError(null)
      } catch {
        markerLayer.clearLayers()
        setMapError('Die hinterlegten PLZ-Grenzen konnten nicht dargestellt werden.')
      }
      return
    }
    setMapError(null)
    const comparable = new Set(points.filter(p => p.customer.lifetimeRevenue !== null).map(p => p.customer.currency ?? 'EUR')).size <= 1
    const maxRevenue = Math.max(1, ...points.map(p => p.customer.lifetimeRevenue ?? 0))
    const draw = () => {
      markerLayer.clearLayers()
      const groups = new Map<string, CustomerMapPoint[]>()
      for (const point of points) {
        const pixel = map.project([point.latitude, point.longitude], map.getZoom())
        const key = map.getZoom() >= 16 ? point.customer.id : Math.floor(pixel.x / 44) + ':' + Math.floor(pixel.y / 44)
        groups.set(key, [...(groups.get(key) ?? []), point])
      }
      for (const group of groups.values()) {
        if (group.length > 1) {
          const bounds = L.latLngBounds(group.map(p => [p.latitude, p.longitude] as [number, number]))
          L.marker(bounds.getCenter(), { icon: L.divIcon({ className: 'customer-cluster', html: '<span>' + group.length + '</span>', iconSize: [36, 36] }) })
            .bindTooltip(group.length + ' Kunden · zum Vergrößern anklicken')
            .on('click', () => map.fitBounds(bounds, { padding: [35, 35], maxZoom: 16 })).addTo(markerLayer)
          continue
        }
        const point = group[0]
        L.circleMarker([point.latitude, point.longitude], {
          radius: comparable ? 5 + 11 * Math.sqrt(Math.max(0, point.customer.lifetimeRevenue ?? 0) / maxRevenue) : 7,
          color: point.isFallback ? '#fff0bd' : '#d7fbff', weight: 2,
          fillColor: point.isFallback ? '#b45309' : '#0369a1', fillOpacity: 0.9,
        }).bindPopup(createCustomerPopup(point)).addTo(markerLayer)
      }
    }
    draw()
    map.on('zoomend', draw)
    map.invalidateSize()
    return () => { map.off('zoomend', draw) }
  }, [points, customers, postalAreas, mode])

  const showGermany = () => mapRef.current?.setView(GERMANY_MAP_CENTER, GERMANY_MAP_ZOOM)
  const showAllLocations = () => {
    const map = mapRef.current
    if (!map || points.length === 0) return showGermany()
    const bounds = L.latLngBounds(points.map(point => [point.latitude, point.longitude] as [number, number]))
    map.fitBounds(bounds, { padding: [24, 24], maxZoom: 12 })
  }

  return <>
    {mapError && <p role="status">{mapError}</p>}
    <div className="customer-map-controls" role="group" aria-label="Kartenausschnitt ändern">
      <button type="button" onClick={showGermany}>Deutschland</button>
      <button type="button" onClick={showAllLocations}>Alle Standorte</button>
    </div>
    <div className="customer-map-leaflet" ref={mapElementRef} role="application" aria-label="Interaktive Kundenkarte" />
  </>
}

function createCustomerPopup(point: CustomerMapPoint) {
  const root = document.createElement('div')
  const title = document.createElement('strong')
  title.textContent = point.customer.name
  root.append(title)

  const location = document.createElement('div')
  location.textContent = formatCustomerLocation(point.customer)
  root.append(location)
  const values = document.createElement('div')
  values.textContent = `Betreuer: ${point.customer.ownerName ?? '–'} · Umsatz: ${point.customer.lifetimeRevenue === null ? 'Nicht berechenbar' : point.customer.lifetimeRevenue.toLocaleString('de-DE') + ' ' + (point.customer.currency ?? 'EUR')} · Letzter Kontakt: ${point.customer.lastContactAt ? new Date(point.customer.lastContactAt).toLocaleDateString('de-DE') : 'Nicht dokumentiert'} · Offene Deals: ${point.customer.openDealCount}`
  root.append(values)

  const details = document.createElement('small')
  details.textContent = `${point.locationBasis === 'exact' ? 'Exakter Standort' : `Standort-Näherung (${point.locationBasis})`} · Umsatz und Deal-Nachweise in der Tabelle unter der Karte`
  root.append(details)

  if (safeCrmUrl(point.customer.externalUrl)) {
    const link = document.createElement('a')
    link.href = safeCrmUrl(point.customer.externalUrl)!
    link.target = '_blank'
    link.rel = 'noopener noreferrer'
    link.textContent = 'CRM öffnen ↗'
    root.append(link)
  }
  return root
}

const countryCentres: Record<string, { latitude: number; longitude: number }> = {
  AT: { latitude: 47.6, longitude: 14.1 },
  AU: { latitude: -25.3, longitude: 133.8 },
  BE: { latitude: 50.8, longitude: 4.5 },
  CA: { latitude: 56.1, longitude: -106.3 },
  CH: { latitude: 46.8, longitude: 8.2 },
  DE: { latitude: 51.2, longitude: 10.4 },
  ES: { latitude: 40.4, longitude: -3.7 },
  FR: { latitude: 46.2, longitude: 2.2 },
  GB: { latitude: 55.4, longitude: -3.4 },
  IT: { latitude: 41.9, longitude: 12.6 },
  NL: { latitude: 52.1, longitude: 5.3 },
  PL: { latitude: 52.1, longitude: 19.1 },
  US: { latitude: 37.1, longitude: -95.7 },
}

const cityCentres: Record<string, { latitude: number; longitude: number }> = {
  amsterdam: { latitude: 52.4, longitude: 4.9 },
  basel: { latitude: 47.6, longitude: 7.6 },
  berlin: { latitude: 52.5, longitude: 13.4 },
  bonn: { latitude: 50.7, longitude: 7.1 },
  bremen: { latitude: 53.1, longitude: 8.8 },
  dresden: { latitude: 51.1, longitude: 13.7 },
  dusseldorf: { latitude: 51.2, longitude: 6.8 },
  dortmund: { latitude: 51.5, longitude: 7.5 },
  essen: { latitude: 51.5, longitude: 7.0 },
  frankfurt: { latitude: 50.1, longitude: 8.7 },
  frankfurtammain: { latitude: 50.1, longitude: 8.7 },
  hamburg: { latitude: 53.6, longitude: 10.0 },
  hannover: { latitude: 52.4, longitude: 9.7 },
  koln: { latitude: 50.9, longitude: 6.96 },
  koeln: { latitude: 50.9, longitude: 6.96 },
  leipzig: { latitude: 51.3, longitude: 12.4 },
  london: { latitude: 51.5, longitude: -0.1 },
  madrid: { latitude: 40.4, longitude: -3.7 },
  mailand: { latitude: 45.5, longitude: 9.2 },
  munchen: { latitude: 48.1, longitude: 11.6 },
  muenchen: { latitude: 48.1, longitude: 11.6 },
  nurnberg: { latitude: 49.5, longitude: 11.1 },
  paris: { latitude: 48.9, longitude: 2.3 },
  salzburg: { latitude: 47.8, longitude: 13.0 },
  stuttgart: { latitude: 48.8, longitude: 9.2 },
  wien: { latitude: 48.2, longitude: 16.4 },
  zurich: { latitude: 47.4, longitude: 8.5 },
}

const germanPostalCentres: Record<string, { latitude: number; longitude: number }> = {
  '0': { latitude: 51.1, longitude: 12.4 },
  '1': { latitude: 52.5, longitude: 13.4 },
  '2': { latitude: 53.6, longitude: 10.0 },
  '3': { latitude: 52.4, longitude: 9.7 },
  '4': { latitude: 51.2, longitude: 6.8 },
  '5': { latitude: 50.9, longitude: 6.9 },
  '6': { latitude: 50.1, longitude: 8.7 },
  '7': { latitude: 48.8, longitude: 9.2 },
  '8': { latitude: 48.1, longitude: 11.6 },
  '9': { latitude: 49.5, longitude: 11.1 },
}

function toCustomerMapPoint(customer: Customers['customers'][number]): CustomerMapPoint | null {
  if (customer.latitude !== null && customer.longitude !== null) {
    return { customer, latitude: customer.latitude, longitude: customer.longitude, isFallback: false, locationBasis: 'exact' }
  }
  const city = customer.city ? cityCentres[normalizeLocationKey(customer.city)] : undefined
  if (city) return { customer, ...city, isFallback: true, locationBasis: 'city' }
  const country = normalizeCountryCode(customer.countryCode)
  const postalValue = customer.postalCode?.trim() ?? ''
  const postal = (country === 'DE' || /^\d{5}$/.test(postalValue)) ? germanPostalCentres[postalValue.charAt(0)] : undefined
  if (postal) return { customer, ...postal, isFallback: true, locationBasis: 'postal' }
  const centre = country ? countryCentres[country] : undefined
  return centre ? { customer, ...centre, isFallback: true, locationBasis: 'country' } : null
}

function normalizeLocationKey(value: string) {
  return value.trim().toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[^a-z0-9]+/g, ' ').trim().replace(/ /g, '')
}

function normalizeCountryCode(value: string | null) {
  const normalized = value?.trim().toUpperCase()
  if (!normalized) return null
  return ({ DEUTSCHLAND: 'DE', GERMANY: 'DE', ÖSTERREICH: 'AT', AUSTRIA: 'AT', SCHWEIZ: 'CH', SWITZERLAND: 'CH' } as Record<string, string>)[normalized] ?? normalized
}

function formatCustomerLocation(customer: Customers['customers'][number]) {
  const street = [customer.addressLine1, customer.houseNumber].filter(Boolean).join(' ')
  const city = [customer.postalCode, customer.city].filter(Boolean).join(' ')
  return [street, city, customer.regionCode, customer.countryCode].filter(Boolean).join(', ') || 'Standort unbekannt'
}

function GoalsWebpart({ report }: { report: Goals }) {
  return <WebpartCard><h2>Teamziele · aktuelles Geschäftsjahr</h2><p className="muted">Pace ist die Zielerreichung abzüglich des verstrichenen Jahresanteils in Prozentpunkten. Ohne belastbares Jahresziel gibt es keine Zielerreichung oder Pace.</p><div className="table-wrap"><table><thead><tr><th>Mitarbeiter</th><th>Jahresziel</th><th>Umsatz Geschäftsjahr</th><th>Erreichung</th><th>Pace</th></tr></thead><tbody>{report.members.map(member => <tr key={member.ownerId}><td><strong>{member.name}</strong></td>{['target', 'achieved', 'attainment', 'pace'].map(key => <td key={key}><MetricLink metricKey={`owner:${member.ownerId}:${key}`} /></td>)}</tr>)}</tbody></table></div><SupplementaryReports area="goals" /></WebpartCard>
}

function CleanupWebpart({ report }: { report: Cleanup }) {
  return <WebpartCard><h2>Datenqualität</h2><p className="sales-card-copy">Dubletten werden nur vorgeschlagen. Zusammenführen bleibt eine manuelle, protokollierte Entscheidung.</p><MetricTile metricKey="quality-total" /><div className="report-columns"><div><h3>Mögliche Dubletten</h3><ul className="report-list">{report.duplicates.map(item => <li key={item.id}><strong>{item.customerA} ↔ {item.customerB}</strong><span>{item.confidence} · {item.status}</span></li>)}</ul></div><div><h3>Offene Prüfungen nach Schwere</h3><EvidenceChart prefix="quality:" /></div></div></WebpartCard>
}

function ServiceWebpart({ report }: { report: Service }) {
  return <WebpartCard><h2>Servicefälle · {report.periodName}</h2><div className="report-kpi-grid">{['total', 'open', 'overdue', 'urgent'].map(key => <MetricTile key={key} metricKey={`service:${key}`} />)}</div><div className="report-columns"><div><h3>Status</h3><EvidenceChart prefix="service-status:" /></div><div><h3>Priorität</h3><EvidenceChart prefix="service-priority:" /></div></div><h3>Dringende Fälle</h3><EvidenceTable metricKey="service:urgent" /></WebpartCard>
}

function CommercialWebpart({ report }: { report: Commercial }) {
  return <WebpartCard><h2>Angebot bis Zahlung · {report.periodName}</h2>
    <div className="report-kpi-grid">{['offers', 'orders', 'invoices', 'outstanding'].map(key => <MetricTile key={key} metricKey={`commercial:${key}`} />)}</div>
    <div className="table-wrap"><table><thead><tr><th>Belegart</th><th>Offen</th><th>Überfällig</th><th>Betrag</th></tr></thead><tbody>
      {[['offers', 'Angebote', 'offers-amount'], ['orders', 'Aufträge', 'orders-amount'], ['invoices', 'Rechnungen', 'outstanding']].map(([key, label, amount]) => <tr key={key}><td>{label}</td><td><MetricLink metricKey={`commercial:${key}-open`} /></td><td><MetricLink metricKey={`commercial:${key}-overdue`} /></td><td><MetricLink metricKey={`commercial:${amount}`} /><small className="table-note">{key === 'invoices' ? 'Offener Rechnungsbetrag' : 'Gesamtvolumen, alle Status'}</small></td></tr>)}
    </tbody></table></div><h3>Status</h3><EvidenceChart prefix="commercial-status:" />
  </WebpartCard>
}
