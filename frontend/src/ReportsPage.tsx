import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { DashboardContentEditor, type LayoutNode, type ReportDefinition } from './DashboardContentEditor'
import { WorklistWidget } from './WorklistWidget'

type Breakdown = { label: string; count: number; amount: number | null }
type Cockpit = {
  periodName: string; currency: string; wonRevenue: number; annualTarget: number; targetAttainmentPercent: number; winRatePercent: number; pipelineAmount: number; pipelineCoveragePercent: number; averageSalesCycleDays: number | null; arr: number; newRevenue: number; existingRevenue: number; staleDealCount: number; expiringContractCount: number; funnel: { name: string; dealCount: number; amount: number; conversionPercent: number | null }[]; actionPoints: { id: string; title: string; reason: string | null; ruleCode: string | null; priorityScore: number; dueAt: string | null }[]
}
type Team = { periodName: string; timeSharePercent: number; members: { ownerId: string; name: string; wonRevenue: number; target: number; attainmentPercent: number; pace: number; openDealCount: number; pipelineAmount: number; appointmentCount: number; callCount: number; conversationCount: number; appointmentTypes: Breakdown[] }[]; appointmentTypes: Breakdown[] }
type Meetings = { periodName: string; newAppointments: number; currentWeekAppointments: number; plannedAppointments: number; completedAppointments: number; cancelledAppointments: number; rescheduledAppointments: number; noShowAppointments: number; completionRatePercent: number; noShowRatePercent: number; rescheduleRatePercent: number; byType: Breakdown[]; byStatus: Breakdown[] }
type Analysis = { periodName: string; byProduct: Breakdown[]; byIndustry: Breakdown[]; byRegion: Breakdown[]; lossReasons: Breakdown[]; stageDwell: { stage: string; dealCount: number; averageDays: number }[]; crossSelling: { customerId: string; customerName: string; categories: string[]; categoryCount: number }[] }
type Customers = { periodName: string; customers: { id: string; name: string; ownerName: string | null; countryCode: string | null; postalCode: string | null; regionCode: string | null; latitude: number | null; longitude: number | null; lifetimeRevenue: number; lastContactAt: string | null; openDealCount: number; needsReview: boolean; externalUrl: string | null }[]; unmappedCount: number; regions: Breakdown[] }
type Goals = { periodName: string; timeSharePercent: number; members: { ownerId: string; name: string; target: number; achieved: number; attainmentPercent: number; timeSharePercent: number; pace: number; status: string }[] }
type Cleanup = { duplicates: { id: string; customerA: string; customerB: string; score: number; confidence: string; status: string; matchDetailsJson: string | null }[]; qualityFindings: Breakdown[]; openFindingCount: number }
type Service = { periodName: string; totalCases: number; openCases: number; overdueCases: number; urgentCases: number; byStatus: Breakdown[]; byPriority: Breakdown[]; urgentItems: { id: string; subject: string; status: string; priority: string; openedAt: string | null; dueAt: string | null; customerName: string | null; externalUrl: string | null }[] }
type Commercial = { periodName: string; offerCount: number; openOfferCount: number; offerAmount: number; overdueOfferCount: number; orderCount: number; openOrderCount: number; orderAmount: number; overdueOrderCount: number; invoiceCount: number; openInvoiceCount: number; openInvoiceAmount: number; overdueInvoiceCount: number; statusBreakdown: Breakdown[] }
type LayoutResponse = { nodes: LayoutNode[]; availableReports: ReportDefinition[]; isDefault: boolean; canEdit: boolean }
type Dashboard = { generatedAt: string; timeframe: string; periodName: string; layout: LayoutResponse; cockpit: Cockpit | null; team: Team | null; meetings: Meetings | null; analysis: Analysis | null; customers: Customers | null; goals: Goals; cleanup: Cleanup | null; service: Service; commercial: Commercial }

const money = (value: number | null | undefined) => new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 }).format(value ?? 0)
const percent = (value: number | null | undefined) => `${(value ?? 0).toFixed(1)} %`
const date = (value: string | null) => value ? new Date(value).toLocaleDateString('de-DE') : '–'

export function ReportsPage({ forceEdit = false }: { forceEdit?: boolean }) {
  const { activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [timeframe, setTimeframe] = useState('year')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(forceEdit)
  const [draftNodes, setDraftNodes] = useState<LayoutNode[]>([])
  const [savingLayout, setSavingLayout] = useState(false)
  const [layoutMessage, setLayoutMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!user || !activeTenantId) return
    setLoading(true)
    setError(null)
    try {
      const response = await authorizedFetch(`/api/reports/dashboard?timeframe=${timeframe}`)
      if (!response.ok) throw new Error(`Reports antworteten mit HTTP ${response.status}.`)
      const payload = await response.json() as Dashboard
      setDashboard(payload)
      setDraftNodes(payload.layout.nodes)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Reports sind nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, timeframe, user])

  useEffect(() => { void load() }, [load])

  const saveLayout = async () => {
    if (!dashboard) return
    setSavingLayout(true)
    setError(null)
    setLayoutMessage(null)
    try {
      const response = await authorizedFetch('/api/reports/layout', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ nodes: draftNodes }),
      })
      const payload = await response.json() as LayoutResponse & { message?: string }
      if (!response.ok) throw new Error(payload.message ?? `Reportseite konnte nicht gespeichert werden (HTTP ${response.status}).`)
      setDashboard(current => current ? { ...current, layout: payload } : current)
      setDraftNodes(payload.nodes)
      setEditing(false)
      setLayoutMessage('Die Reportseite wurde für diesen Mandanten gespeichert.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Reportseite konnte nicht gespeichert werden.')
    } finally {
      setSavingLayout(false)
    }
  }

  return (
    <main className="sales-page reports-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · REPORT-DASHBOARD</p>
          <h1>Vertriebsübersicht</h1>
          <p className="sales-lead">Jeder Report ist eine eigene Komponente. Das Standardlayout zeigt die gesamte fachliche Kette; ein Tenant-Administrator kann diese Seite direkt mit Grids, Tabs, Akkordeons, Überschriften und Texten gestalten.</p>
        </div>
        <div className="report-toolbar">
          <label>Zeitraum
            <select value={timeframe} onChange={event => setTimeframe(event.target.value)}>
              <option value="month">Monat</option>
              <option value="year">Geschäftsjahr</option>
              <option value="lifetime">Lifetime</option>
            </select>
          </label>
          <div className="report-toolbar-actions"><button className="secondary-button" type="button" onClick={() => void load()} disabled={loading || savingLayout}>{loading ? 'Wird geladen …' : 'Reports aktualisieren'}</button>{dashboard?.layout.canEdit && <button className={editing ? 'primary-button' : 'secondary-button'} type="button" onClick={() => { setEditing(current => !current); setLayoutMessage(null) }} disabled={savingLayout}>{editing ? 'Bearbeitung schließen' : 'Reportseite bearbeiten'}</button>}</div>
        </div>
      </section>
      {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}
      {!dashboard && loading && <section className="sales-card report-loading">Reports werden aus der Tenant-Datenbank geladen …</section>}
      {dashboard && (
        <>
          <div className="report-status">Stand {new Date(dashboard.generatedAt).toLocaleString('de-DE')} · {dashboard.periodName}</div>
          {layoutMessage && <div className="message success-message">{layoutMessage}</div>}
          {editing && dashboard.layout.canEdit
            ? <><DashboardContentEditor nodes={draftNodes} reports={dashboard.layout.availableReports} onChange={setDraftNodes} /><div className="content-editor-footer"><button className="secondary-button" type="button" onClick={() => { setDraftNodes(dashboard.layout.nodes); setEditing(false) }} disabled={savingLayout}>Änderungen verwerfen</button><button className="primary-button" type="button" onClick={() => void saveLayout()} disabled={savingLayout}>{savingLayout ? 'Wird gespeichert …' : 'Reportseite speichern'}</button></div></>
            : <LayoutRenderer nodes={dashboard.layout.nodes} dashboard={dashboard} />}
        </>
      )}
    </main>
  )
}

function LayoutRenderer({ nodes, dashboard }: { nodes: LayoutNode[]; dashboard: Dashboard }) {
  return <div className="dashboard-page-layout">{nodes.filter(node => node.visible).map(node => <LayoutNodeView key={node.id} node={node} dashboard={dashboard} />)}</div>
}

function LayoutNodeView({ node, dashboard }: { node: LayoutNode; dashboard: Dashboard }) {
  if (!node.visible || !node.allowed) return null
  if (node.type === 'heading') return <section className="dashboard-heading-block"><h2>{node.title}</h2></section>
  if (node.type === 'text') return <p className="dashboard-text-block">{node.text}</p>
  if (node.type === 'grid') return <div className="dashboard-grid-node" style={{ gridTemplateColumns: `repeat(${Math.max(1, Math.min(node.columns, 4))}, minmax(0, 1fr))` }}>{node.children.filter(child => child.visible).map(child => <div className="dashboard-grid-item" style={{ gridColumn: `span ${Math.min(child.columns, Math.max(1, node.columns))}` }} key={child.id}><LayoutNodeView node={child} dashboard={dashboard} /></div>)}</div>
  if (node.type === 'accordion') return <section className="dashboard-accordion"><details open><summary>{node.title || 'Abschnitt'}</summary><div className="dashboard-accordion-content">{node.children.map(child => <LayoutNodeView key={child.id} node={child} dashboard={dashboard} />)}</div></details></section>
  if (node.type === 'tabs') return <DashboardTabs node={node} dashboard={dashboard} />
  if (node.type !== 'report' || !node.reportKey) return null
  const definition = dashboard.layout.availableReports.find(report => report.key === node.reportKey)
  return <section className={`webpart-slot webpart-span-${Math.min(node.columns, 2)}`}><div className="webpart-label"><span>{node.title ?? definition?.title ?? node.reportKey}</span><small>{definition?.description}</small></div>{renderWebpart(node.reportKey, dashboard)}</section>
}

function DashboardTabs({ node, dashboard }: { node: LayoutNode; dashboard: Dashboard }) {
  const [active, setActive] = useState(0)
  const index = Math.min(active, Math.max(0, node.children.length - 1))
  return <section className="dashboard-tabs"><div className="dashboard-tab-buttons">{node.children.map((child, childIndex) => <button className={childIndex === index ? 'is-active' : ''} type="button" key={child.id} onClick={() => setActive(childIndex)}>{child.title || `Tab ${childIndex + 1}`}</button>)}</div>{node.children[index] && <div className="dashboard-tab-content"><LayoutNodeView node={node.children[index]} dashboard={dashboard} /></div>}</section>
}

function renderWebpart(key: string, dashboard: Dashboard) {
  switch (key) {
    case 'worklist': return <WorklistWidget compact />
    case 'cockpit': return dashboard.cockpit ? <CockpitWebpart report={dashboard.cockpit} /> : null
    case 'team': return dashboard.team ? <TeamWebpart report={dashboard.team} /> : null
    case 'meetings': return dashboard.meetings ? <MeetingsWebpart report={dashboard.meetings} /> : null
    case 'analysis': return dashboard.analysis ? <AnalysisWebpart report={dashboard.analysis} /> : null
    case 'customers': return dashboard.customers ? <CustomersWebpart report={dashboard.customers} /> : null
    case 'goals': return <GoalsWebpart report={dashboard.goals} />
    case 'cleanup': return dashboard.cleanup ? <CleanupWebpart report={dashboard.cleanup} /> : null
    case 'service': return <ServiceWebpart report={dashboard.service} />
    case 'commercial': return <CommercialWebpart report={dashboard.commercial} />
    default: return null
  }
}

function WebpartCard({ children }: { children: ReactNode }) {
  return <div className="sales-card webpart-card">{children}</div>
}

function Kpi({ label, value, hint, tone = '' }: { label: string; value: string; hint?: string; tone?: string }) {
  return <div className={`report-kpi ${tone}`}><span>{label}</span><strong>{value}</strong>{hint && <small>{hint}</small>}</div>
}

function CockpitWebpart({ report }: { report: Cockpit }) {
  return <WebpartCard>
    <div className="card-heading"><div><p className="sales-eyebrow">COCKPIT · {report.periodName.toUpperCase()}</p><h2>Geschäftsführung auf einen Blick</h2></div><span className="status-badge">LIVE-AUSWERTUNG</span></div>
    <div className="report-kpi-grid">
      <Kpi label="Gewonnener Umsatz" value={money(report.wonRevenue)} hint={`Ziel ${money(report.annualTarget)}`} tone={report.targetAttainmentPercent < 70 ? 'critical' : ''} />
      <Kpi label="Zielerreichung" value={percent(report.targetAttainmentPercent)} />
      <Kpi label="Win Rate" value={percent(report.winRatePercent)} />
      <Kpi label="Pipeline-Deckung" value={percent(report.pipelineCoveragePercent)} hint={money(report.pipelineAmount)} />
      <Kpi label="Sales Cycle" value={report.averageSalesCycleDays === null ? '–' : `${report.averageSalesCycleDays.toFixed(1)} Tage`} />
      <Kpi label="ARR" value={money(report.arr)} />
      <Kpi label="Hängende Deals" value={`${report.staleDealCount}`} tone={report.staleDealCount > 0 ? 'warning' : ''} />
      <Kpi label="Verträge < 90 Tage" value={`${report.expiringContractCount}`} tone={report.expiringContractCount > 0 ? 'warning' : ''} />
    </div>
    <div className="report-columns"><div><h3>Funnel</h3><BreakdownList items={report.funnel.map(item => ({ label: item.name, count: item.dealCount, amount: item.amount }))} /></div><div><h3>Handlungspunkte</h3>{report.actionPoints.length === 0 ? <p className="muted">Keine offenen Handlungspunkte.</p> : <ul className="report-list">{report.actionPoints.map(item => <li key={item.id}><strong>{item.title}</strong><span>{item.reason ?? 'Prüfung erforderlich.'}</span></li>)}</ul>}</div></div>
  </WebpartCard>
}

function TeamWebpart({ report }: { report: Team }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">TEAM-STEUERUNG · {report.periodName.toUpperCase()}</p><h2>Zielerreichung und Aktivität</h2></div><span className="worklist-refresh">Zeitanteil {percent(report.timeSharePercent)}</span></div><div className="table-wrap"><table><thead><tr><th>Mitarbeiter</th><th>Umsatz / Ziel</th><th>Erreichung</th><th>Pace</th><th>Pipeline</th><th>Termine</th><th>Anrufe</th></tr></thead><tbody>{report.members.map(member => <tr key={member.ownerId}><td><strong>{member.name}</strong></td><td>{money(member.wonRevenue)} / {money(member.target)}</td><td>{percent(member.attainmentPercent)}</td><td className={member.pace < -15 ? 'table-critical' : ''}>{member.pace.toFixed(1)} Pkt.</td><td>{member.openDealCount} · {money(member.pipelineAmount)}</td><td>{member.appointmentCount}</td><td>{member.callCount} / {member.conversationCount} Gespr.</td></tr>)}</tbody></table></div></WebpartCard>
}

function MeetingsWebpart({ report }: { report: Meetings }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">MEETING REPORT · {report.periodName.toUpperCase()}</p><h2>Termine als Frühindikator</h2></div></div><div className="report-kpi-grid report-kpi-grid-four"><Kpi label="Neu angelegt" value={`${report.newAppointments}`} /><Kpi label="Diese Woche" value={`${report.currentWeekAppointments}`} /><Kpi label="Durchführungsquote" value={percent(report.completionRatePercent)} /><Kpi label="No-Show-Quote" value={percent(report.noShowRatePercent)} tone={report.noShowRatePercent >= 5 ? 'warning' : ''} /><Kpi label="Verschiebequote" value={percent(report.rescheduleRatePercent)} /></div><div className="report-columns"><div><h3>Nach Status</h3><BreakdownList items={report.byStatus} /></div><div><h3>Nach Terminart</h3><BreakdownList items={report.byType} /></div></div></WebpartCard>
}

function AnalysisWebpart({ report }: { report: Analysis }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">ANALYSE · {report.periodName.toUpperCase()}</p><h2>Umsatz, Prozess und Chancen</h2></div></div><div className="report-columns report-columns-three"><div><h3>Produkte</h3><BreakdownList items={report.byProduct} /></div><div><h3>Branchen</h3><BreakdownList items={report.byIndustry} /></div><div><h3>Regionen</h3><BreakdownList items={report.byRegion} /></div></div><div className="report-columns"><div><h3>Verlustgründe</h3><BreakdownList items={report.lossReasons} /></div><div><h3>Verweildauer je Stufe</h3><ul className="report-list">{report.stageDwell.map(item => <li key={item.stage}><strong>{item.stage}</strong><span>{item.averageDays.toFixed(1)} Tage · {item.dealCount} Deals</span></li>)}</ul></div></div><h3>Cross-Selling · Kunden mit mehreren Kategorien</h3><div className="tag-list">{report.crossSelling.slice(0, 12).map(item => <span className="tag" key={item.customerId}><strong>{item.customerName}</strong> · {item.categories.join(', ')}</span>)}</div></WebpartCard>
}

function CustomersWebpart({ report }: { report: Customers }) {
  const mapped = report.customers.filter(customer => customer.latitude !== null && customer.longitude !== null)
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">KUNDENSTAMM · KARTE</p><h2>Kunden und Gebiete</h2></div><span className="worklist-refresh">{report.customers.length} Kunden · {report.unmappedCount} nicht verortbar</span></div><div className="customer-map"><div className="customer-map-grid">{mapped.slice(0, 80).map(customer => <span className="customer-dot" title={`${customer.name} · ${money(customer.lifetimeRevenue)}`} key={customer.id} style={{ left: `${Math.min(96, Math.max(2, ((customer.longitude ?? 0) + 180) / 360 * 100))}%`, top: `${Math.min(96, Math.max(2, (90 - (customer.latitude ?? 0)) / 180 * 100))}%` }} />)}</div><div className="customer-map-copy"><strong>Weltweite Kundenverteilung</strong><span>Startet im deutschsprachigen Raum; Punktgröße und Detailkarte folgen mit dem Kartendienst.</span><small>{mapped.length} Kunden mit Koordinaten</small></div></div><div className="table-wrap"><table><thead><tr><th>Kunde</th><th>Betreuer</th><th>Land / PLZ</th><th>Umsatz</th><th>Offene Deals</th><th></th></tr></thead><tbody>{report.customers.slice(0, 25).map(customer => <tr key={customer.id}><td><strong>{customer.name}</strong>{customer.needsReview && <small className="table-note">Prüfen</small>}</td><td>{customer.ownerName ?? '–'}</td><td>{customer.countryCode ?? '–'} / {customer.postalCode ?? '–'}</td><td>{money(customer.lifetimeRevenue)}</td><td>{customer.openDealCount}</td><td>{customer.externalUrl && <a href={customer.externalUrl} target="_blank" rel="noopener noreferrer">CRM ↗</a>}</td></tr>)}</tbody></table></div></WebpartCard>
}

function GoalsWebpart({ report }: { report: Goals }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">ZIELE UND PACE · {report.periodName.toUpperCase()}</p><h2>Teamziele</h2></div><span className="worklist-refresh">Zeitanteil {percent(report.timeSharePercent)}</span></div><div className="table-wrap"><table><thead><tr><th>Mitarbeiter</th><th>Ziel</th><th>Erreicht</th><th>Erreichung</th><th>Pace</th><th>Status</th></tr></thead><tbody>{report.members.map(member => <tr key={member.ownerId}><td><strong>{member.name}</strong></td><td>{money(member.target)}</td><td>{money(member.achieved)}</td><td>{percent(member.attainmentPercent)}</td><td>{member.pace.toFixed(1)} Pkt.</td><td><span className={`pace-badge pace-${member.status.toLowerCase().replace(/ /g, '-')}`}>{member.status}</span></td></tr>)}</tbody></table></div></WebpartCard>
}

function CleanupWebpart({ report }: { report: Cleanup }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">AUFRÄUMEN · DATENQUALITÄT</p><h2>{report.openFindingCount} offene Prüfungen</h2></div></div><p className="sales-card-copy">Dubletten werden nur vorgeschlagen. Zusammenführen bleibt eine manuelle, protokollierte Entscheidung.</p><div className="report-columns"><div><h3>Mögliche Dubletten</h3><ul className="report-list">{report.duplicates.slice(0, 8).map(item => <li key={item.id}><strong>{item.customerA} ↔ {item.customerB}</strong><span>{item.confidence} · {item.score.toFixed(0)} Punkte · {item.status}</span></li>)}</ul></div><div><h3>Datenqualität</h3><BreakdownList items={report.qualityFindings} /></div></div></WebpartCard>
}

function ServiceWebpart({ report }: { report: Service }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">SERVICEFÄLLE · {report.periodName.toUpperCase()}</p><h2>Beschwerden und offene Servicefälle</h2></div><span className="worklist-refresh">{report.openCases} offen</span></div><div className="report-kpi-grid report-kpi-grid-four"><Kpi label="Servicefälle" value={`${report.totalCases}`} /><Kpi label="Offen" value={`${report.openCases}`} /><Kpi label="Überfällig" value={`${report.overdueCases}`} tone={report.overdueCases > 0 ? 'warning' : ''} /><Kpi label="Dringend" value={`${report.urgentCases}`} tone={report.urgentCases > 0 ? 'critical' : ''} /></div><div className="report-columns"><div><h3>Status</h3><BreakdownList items={report.byStatus} /></div><div><h3>Priorität</h3><BreakdownList items={report.byPriority} /></div></div><h3>Dringende Fälle</h3>{report.urgentItems.length === 0 ? <p className="muted">Keine dringenden Servicefälle.</p> : <ul className="report-list">{report.urgentItems.map(item => <li key={item.id}><strong>{item.subject}</strong><span>{item.priority} · {item.status} · {item.customerName ?? 'ohne Kundenzuordnung'}{item.dueAt ? ` · fällig ${date(item.dueAt)}` : ''}</span>{item.externalUrl && <a href={item.externalUrl} target="_blank" rel="noopener noreferrer">CRM öffnen ↗</a>}</li>)}</ul>}</WebpartCard>
}

function CommercialWebpart({ report }: { report: Commercial }) {
  return <WebpartCard><div className="card-heading"><div><p className="sales-eyebrow">KOMMERZIELLE KETTE · {report.periodName.toUpperCase()}</p><h2>Angebot bis Zahlung</h2></div><span className="worklist-refresh">{report.openInvoiceCount} offene Rechnungen</span></div><div className="report-kpi-grid report-kpi-grid-four"><Kpi label="Angebote" value={`${report.offerCount}`} hint={`${report.openOfferCount} offen · ${money(report.offerAmount)}`} /><Kpi label="Aufträge" value={`${report.orderCount}`} hint={`${report.openOrderCount} offen · ${money(report.orderAmount)}`} /><Kpi label="Rechnungen" value={`${report.invoiceCount}`} hint={`${report.openInvoiceCount} offen`} /><Kpi label="Offener Betrag" value={money(report.openInvoiceAmount)} tone={report.overdueInvoiceCount > 0 ? 'warning' : ''} /></div><div className="report-columns"><div><h3>Überfällig</h3><ul className="report-list"><li><strong>Angebote</strong><span>{report.overdueOfferCount}</span></li><li><strong>Aufträge</strong><span>{report.overdueOrderCount}</span></li><li><strong>Rechnungen</strong><span>{report.overdueInvoiceCount}</span></li></ul></div><div><h3>Status</h3><BreakdownList items={report.statusBreakdown} /></div></div></WebpartCard>
}

function BreakdownList({ items }: { items: Breakdown[] }) {
  const max = Math.max(...items.map(item => item.amount ?? item.count), 1)
  if (items.length === 0) return <p className="muted">Keine Daten für den Zeitraum.</p>
  return <ul className="breakdown-list">{items.map(item => { const value = item.amount ?? item.count; return <li key={item.label}><div><strong>{item.label}</strong><span>{item.amount === null ? item.count : money(item.amount)}</span></div><i><b style={{ width: `${Math.max(3, value / max * 100)}%` }} /></i></li> })}</ul>
}
