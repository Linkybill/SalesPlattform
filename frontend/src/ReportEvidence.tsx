import { createContext, useContext, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'

export type ReportRow = { key: string; kind: string; name: string; customer: string | null; owner: string | null;
  status: string | null; date: string | null; amount: number | null; currency: string | null; detail: string; externalUrl: string | null }
export type ReportMetric = { key: string; label: string; value: number | null; unit: string; currency: string | null;
  period: string; source: string; calculation: string; unavailableReason: string | null; recordKeys: string[] }
export type ReportEvidence = { metrics: Record<string, ReportMetric>; records: Record<string, ReportRow> }
const EvidenceContext = createContext<{ evidence: ReportEvidence; open: (key: string) => void } | null>(null)
const number = (value: number, digits = 1) => value.toLocaleString('de-DE', { maximumFractionDigits: digits })
export function metricValue(metric: ReportMetric) {
  if (metric.value === null) return 'Nicht berechenbar'
  if (metric.unit === 'money') return metric.currency ? `${number(metric.value, 2)} ${metric.currency}` : number(metric.value, 2)
  return number(metric.value, metric.unit === 'count' ? 0 : 1) + ({ percent: ' %', days: ' Tage', factor: ' ×', points: ' Pkt.' }[metric.unit] ?? '')
}
export const safeCrmUrl = (url: string | null) => {
  try { const parsed = new URL(url ?? ''); return ['https:', 'http:'].includes(parsed.protocol) ? parsed.href : null } catch { return null }
}
function useEvidence() {
  const value = useContext(EvidenceContext)
  if (!value) throw new Error('Report-Nachweis fehlt.')
  return value
}

export function ReportEvidenceProvider({ evidence, generatedAt, children }: { evidence: ReportEvidence; generatedAt: string; children: ReactNode }) {
  const [selected, setSelected] = useState<string | null>(null)
  const dialog = useRef<HTMLDialogElement>(null)
  const opener = useRef<HTMLElement | null>(null)
  const metric = selected ? evidence.metrics[selected] : null
  useEffect(() => { setSelected(null) }, [evidence])
  useEffect(() => {
    if (metric) dialog.current?.showModal()
    else { dialog.current?.close(); opener.current?.focus() }
  }, [metric])
  const open = (key: string) => { opener.current = document.activeElement as HTMLElement; setSelected(key) }
  return <EvidenceContext.Provider value={{ evidence, open }}>
    {children}
    <dialog className="report-detail-dialog" ref={dialog} aria-labelledby="report-detail-title" onCancel={() => setSelected(null)} onClose={() => setSelected(null)} onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); setSelected(null) } }}>
      {metric && <>
        <div className="card-heading"><h2 id="report-detail-title">{metric.label}</h2><button autoFocus className="secondary-button" onClick={() => setSelected(null)} aria-label="Details schließen">Schließen ×</button></div>
        <p className="detail-value">{metricValue(metric)}</p>
        <MetricExplanation metric={metric} />
        <p className="muted">Auswertung vom {new Date(generatedAt).toLocaleString('de-DE')}. Zahlen und Tabelle stammen aus demselben Datenstand, ohne erneute CRM-Abfrage.</p>
        <EvidenceTable key={metric.key} metricKey={metric.key} />
      </>}
    </dialog>
  </EvidenceContext.Provider>
}

function MetricExplanation({ metric }: { metric: ReportMetric }) {
  return <div className="metric-explanation"><p><strong>Zeitraum:</strong> {metric.period}</p><p><strong>Quelle:</strong> {metric.source}</p>
    <p><strong>Berechnung:</strong> {metric.calculation}</p>{metric.unavailableReason && <p className="message info-message">{metric.unavailableReason}</p>}</div>
}
export function MetricTile({ metricKey }: { metricKey: string }) {
  const { evidence, open } = useEvidence()
  const metric = evidence.metrics[metricKey]
  if (!metric) return null
  return <article className="report-kpi explained-kpi">
    <button className="metric-open" type="button" onClick={() => open(metricKey)} aria-label={`${metric.label}: ${metricValue(metric)}. Datensätze anzeigen`}>
      <span>{metric.label}</span><strong>{metricValue(metric)}</strong><small>{metric.period}</small>
      <small>{metric.unavailableReason ?? 'Datensätze anzeigen ↗'}</small>
    </button>
    <details><summary>Quelle & Berechnung</summary><MetricExplanation metric={metric} /></details>
  </article>
}
export function MetricLink({ metricKey }: { metricKey: string }) {
  const { evidence, open } = useEvidence()
  const metric = evidence.metrics[metricKey]
  return metric ? <button className="metric-link" type="button" title={`${metric.label}: ${metric.unavailableReason ?? metric.calculation}`} onClick={() => open(metricKey)}>{metricValue(metric)}</button> : <span>–</span>
}
export function EvidenceChart({ prefix, suffix = '', labels = {} }: { prefix: string; suffix?: string; labels?: Record<string, string> }) {
  const { evidence, open } = useEvidence()
  const metrics = Object.values(evidence.metrics).filter(m => m.key.startsWith(prefix) && m.key.endsWith(suffix))
  const mixedCurrencies = new Set(metrics.filter(m => m.unit === 'money' && m.value !== null).map(m => m.currency)).size > 1
  const max = Math.max(1, ...metrics.map(m => m.value ?? 0))
  return metrics.length ? <>{mixedCurrencies && <p className="muted">Unterschiedliche Währungen – Beträge ohne Umrechnung nicht als Balken vergleichbar.</p>}<ul className="breakdown-list">{metrics.map(metric => <li key={metric.key}>
    <button type="button" className="chart-detail-button" onClick={() => open(metric.key)} aria-label={`${labels[metric.key] ?? metric.label}: ${metricValue(metric)}. Datensätze anzeigen`}>
      <span><strong>{labels[metric.key] ?? metric.label}</strong><span>{metricValue(metric)}</span></span>
      {!mixedCurrencies && <i aria-hidden="true"><b style={{ width: `${Math.max(0, (metric.value ?? 0) / max * 100)}%` }} /></i>}
    </button></li>)}</ul></> : <p className="muted">Keine Daten im ausgewählten Zeitraum.</p>
}

export function EvidenceTable({ metricKey }: { metricKey: string }) {
  const { evidence } = useEvidence()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const rows = (evidence.metrics[metricKey]?.recordKeys ?? []).map(key => evidence.records[key]).filter(Boolean)
  const filtered = rows.filter(row => [row.name, row.customer, row.owner, row.status, row.detail].some(v => v?.toLocaleLowerCase('de-DE').includes(search.toLocaleLowerCase('de-DE'))))
  const pageCount = Math.max(1, Math.ceil(filtered.length / 25))
  const current = Math.min(page, pageCount - 1)
  return <div className="evidence-table">
    <label>Datensätze durchsuchen<input value={search} onChange={e => { setSearch(e.target.value); setPage(0) }} type="search" placeholder="Name, Kunde, Mitarbeiter oder Status" /></label>
    <p className="muted">{filtered.length} von {rows.length} Datensätzen · Seite {current + 1} von {pageCount}</p>
    <div className="table-wrap"><table><thead><tr><th>Datensatz / Kunde</th><th>Zuständig</th><th>Status</th><th>Datum</th><th>Betrag</th><th>Details</th><th>CRM</th></tr></thead>
      <tbody>{filtered.slice(current * 25, (current + 1) * 25).map(row => <tr key={row.key}>
        <td><strong>{row.name}</strong><small className="table-note">{row.customer}</small></td><td>{row.owner ?? '–'}</td><td>{row.status ?? '–'}</td>
        <td>{row.date ? new Date(row.date).toLocaleString('de-DE') : '–'}</td><td>{row.amount === null ? '–' : `${number(row.amount, 2)} ${row.currency ?? '(Währung fehlt)'}`}</td>
        <td>{row.detail || '–'}</td><td>{safeCrmUrl(row.externalUrl) && <a href={safeCrmUrl(row.externalUrl)!} target="_blank" rel="noopener noreferrer">Öffnen ↗</a>}</td>
      </tr>)}</tbody></table></div>
    {filtered.length === 0 && <p>Keine passenden Datensätze.</p>}
    {pageCount > 1 && <div className="button-row"><button className="secondary-button" disabled={current === 0} onClick={() => setPage(current - 1)}>Zurück</button><button className="secondary-button" disabled={current + 1 >= pageCount} onClick={() => setPage(current + 1)}>Weiter</button></div>}
  </div>
}
