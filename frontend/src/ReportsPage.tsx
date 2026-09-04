import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import * as L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { DashboardContentEditor, type LayoutNode, type ReportDefinition } from './DashboardContentEditor'

type Breakdown = { label: string; count: number; amount: number | null }
type Cockpit = {
  periodName: string; currency: string; wonRevenue: number; annualTarget: number; targetAttainmentPercent: number; winRatePercent: number; pipelineAmount: number; pipelineCoveragePercent: number; averageSalesCycleDays: number | null; arr: number; newRevenue: number; existingRevenue: number; staleDealCount: number; expiringContractCount: number; funnel: { name: string; dealCount: number; amount: number; conversionPercent: number | null }[]; actionPoints: { id: string; title: string; reason: string | null; ruleCode: string | null; priorityScore: number; dueAt: string | null }[]
}
type Team = { periodName: string; timeSharePercent: number; members: { ownerId: string; name: string; wonRevenue: number; target: number; attainmentPercent: number; pace: number; openDealCount: number; pipelineAmount: number; appointmentCount: number; callCount: number; conversationCount: number; appointmentTypes: Breakdown[] }[]; appointmentTypes: Breakdown[] }
type Meetings = { periodName: string; newAppointments: number; currentWeekAppointments: number; plannedAppointments: number; completedAppointments: number; cancelledAppointments: number; rescheduledAppointments: number; noShowAppointments: number; completionRatePercent: number; noShowRatePercent: number; rescheduleRatePercent: number; byType: Breakdown[]; byStatus: Breakdown[] }
type Analysis = { periodName: string; byProduct: Breakdown[]; byIndustry: Breakdown[]; byRegion: Breakdown[]; lossReasons: Breakdown[]; stageDwell: { stage: string; dealCount: number; averageDays: number }[]; crossSelling: { customerId: string; customerName: string; categories: string[]; categoryCount: number }[] }
type Customers = { periodName: string; customers: { id: string; name: string; ownerName: string | null; countryCode: string | null; postalCode: string | null; city: string | null; regionCode: string | null; addressLine1: string | null; houseNumber: string | null; latitude: number | null; longitude: number | null; lifetimeRevenue: number; lastContactAt: string | null; openDealCount: number; needsReview: boolean; externalUrl: string | null }[]; unmappedCount: number; regions: Breakdown[] }
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
          <p className="sales-lead">Jeder Report ist eine eigene Komponente. Das Standardlayout zeigt die gesamte fachliche Kette; ein Tenant-Administrator kann diese Seite direkt mit Grids, Tabs, Akkordeons, Überschriften und Texten gestalten. Die Arbeitsliste bleibt eine separate Seite.</p>
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
  const points = useMemo(() => report.customers.map(customer => toCustomerMapPoint(customer)).filter((point): point is CustomerMapPoint => point !== null), [report.customers])
  const exactPoints = points.filter(point => !point.isFallback).length
  const fallbackPoints = points.length - exactPoints

  return <WebpartCard>
    <div className="card-heading"><div><p className="sales-eyebrow">KUNDENSTAMM · KARTE</p><h2>Kunden und Gebiete</h2></div><span className="worklist-refresh">{report.customers.length} Kunden · {report.unmappedCount} ohne exakte Koordinaten</span></div>
    <div className="customer-map">
      <div className="customer-map-grid">
        <CustomerLeafletMap points={points} />
        {points.length === 0 && <div className="customer-map-empty">Für die Kunden sind noch keine verwertbaren Standortdaten vorhanden.</div>}
      </div>
      <div className="customer-map-copy"><strong>Deutschland als Startausschnitt</strong><span>Die Karte ist interaktiv. Mit den Zoom-Schaltflächen oder dem Mausrad kann der Ausschnitt verändert werden; „Deutschland“ setzt den Startausschnitt zurück und „Alle Standorte“ passt ihn an alle vorhandenen Kunden an. Exakte Koordinaten werden bevorzugt, ansonsten werden die verfügbaren Standortdaten als Näherung dargestellt.</span><small>{points.length} Kartenpositionen · {exactPoints} exakte Standorte · {fallbackPoints} Standort-Näherungen · {report.customers.length - points.length} ohne Kartenposition</small></div>
    </div>
    <div className="table-wrap"><table><thead><tr><th>Kunde</th><th>Betreuer</th><th>Standort</th><th>Umsatz</th><th>Offene Deals</th><th></th></tr></thead><tbody>{report.customers.slice(0, 25).map(customer => <tr key={customer.id}><td><strong>{customer.name}</strong>{customer.needsReview && <small className="table-note">Prüfen</small>}</td><td>{customer.ownerName ?? '–'}</td><td>{formatCustomerLocation(customer)}</td><td>{money(customer.lifetimeRevenue)}</td><td>{customer.openDealCount}</td><td>{customer.externalUrl && <a href={customer.externalUrl} target="_blank" rel="noopener noreferrer">CRM ↗</a>}</td></tr>)}</tbody></table></div>
  </WebpartCard>
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

function CustomerLeafletMap({ points }: { points: CustomerMapPoint[] }) {
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
    const resizeObserver = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => map.invalidateSize())
    resizeObserver?.observe(mapElementRef.current)
    window.setTimeout(() => map.invalidateSize(), 0)

    return () => {
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

    markerLayer.clearLayers()
    for (const point of points) {
      L.circleMarker([point.latitude, point.longitude], {
        radius: point.isFallback ? 6 : 7,
        color: point.isFallback ? '#fff0bd' : '#d7fbff',
        weight: 2,
        fillColor: point.isFallback ? '#f5c96b' : '#71e4ef',
        fillOpacity: 0.9,
      })
        .bindPopup(createCustomerPopup(point))
        .addTo(markerLayer)
    }
    map.invalidateSize()
  }, [points])

  const showGermany = () => mapRef.current?.setView(GERMANY_MAP_CENTER, GERMANY_MAP_ZOOM)
  const showAllLocations = () => {
    const map = mapRef.current
    if (!map || points.length === 0) return showGermany()
    const bounds = L.latLngBounds(points.map(point => [point.latitude, point.longitude] as [number, number]))
    map.fitBounds(bounds, { padding: [24, 24], maxZoom: 12 })
  }

  return <>
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

  const details = document.createElement('small')
  details.textContent = `${point.locationBasis === 'exact' ? 'Exakter Standort' : `Standort-Näherung (${point.locationBasis})`} · ${money(point.customer.lifetimeRevenue)}`
  root.append(details)

  if (point.customer.externalUrl) {
    const link = document.createElement('a')
    link.href = point.customer.externalUrl
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
