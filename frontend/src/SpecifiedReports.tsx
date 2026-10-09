import { useState } from 'react'
import { EvidenceTable, MetricLink, MetricTile, metricValue, useEvidence, type ReportMetric } from './ReportEvidence'

export type ReportPanelRow = { label: string; metricKeys: string[] }
export type ReportPanel = { key: string; area: string; title: string; kind: string; description: string; columns: string[]; rows: ReportPanelRow[] }
const colors = ['#2563eb', '#059669', '#d97706', '#db2777', '#7c3aed', '#0891b2', '#dc2626', '#4d7c0f', '#9333ea', '#64748b']

export function SupplementaryReports({ area }: { area: string }) {
  const { evidence } = useEvidence()
  const panels = (evidence.panels ?? []).filter(p => p.area === area)
  if (!panels.length) return null
  return <div className="specified-reports" data-report-area={area}>
    {panels.length > 3 && <nav className="report-index" aria-label="Auswertungen in diesem Bereich">{panels.map(p => <a key={p.key} href={'#report-' + p.key}>{p.title}</a>)}</nav>}
    {panels.map(panel => <ReportPanelView key={panel.key} panel={panel} />)}
  </div>
}

function ReportPanelView({ panel }: { panel: ReportPanel }) {
  const [percent, setPercent] = useState(false)
  const { evidence } = useEvidence()
  const keys = panel.rows.flatMap(r => r.metricKeys)
  const metrics = keys.map(k => evidence.metrics[k]).filter(Boolean)
  const currencies = new Set(metrics.filter(m => m.unit.includes('money') && m.value !== null).map(m => m.currency))
  const canPlot = currencies.size <= 1 && metrics.every(m => m.value === null || Number.isFinite(m.value))
  const allowPercent = ['area', 'stacked'].includes(panel.kind) && canPlot && metrics.every(m => m.value !== null && m.value >= 0)
  const graph = ['line', 'area', 'stacked', 'bar', 'pareto'].includes(panel.kind)
  return <section id={'report-' + panel.key} className={'specified-report specified-report-' + panel.kind}>
    <div className="card-heading"><h3>{panel.title}</h3>{allowPercent && <label className="report-percent-toggle"><input type="checkbox" checked={percent} onChange={e => setPercent(e.target.checked)} />Anteile in %</label>}</div>
    {panel.description && <p className="muted">{panel.description}</p>}
    {panel.rows.length === 0 ? <p className="muted">Keine passenden Daten vorhanden.</p>
      : panel.kind === 'metrics' ? <div className="report-kpi-grid">{keys.map(key => <MetricTile key={key} metricKey={key} />)}</div>
      : panel.kind === 'records' ? <>{keys.map(key => <div key={key}><MetricTile metricKey={key} /><EvidenceTable metricKey={key} /></div>)}</>
      : <>
        {panel.kind === 'progress' && <GoalProgress panel={panel} />}
        {graph && (canPlot ? <PanelPlot panel={panel} percent={allowPercent && percent} /> : <p className="message info-message">Unterschiedliche Währungen: keine gemeinsame Skala ohne Umrechnung.</p>)}
        <PanelTable panel={panel} />
      </>}
  </section>
}

function GoalProgress({ panel }: { panel: ReportPanel }) {
  const { evidence } = useEvidence()
  const max = Math.max(100, ...panel.rows.map(r => evidence.metrics[r.metricKeys[0]]?.value ?? 0))
  return <div className="report-goal-progress">{panel.rows.map(row => {
    const achievement = evidence.metrics[row.metricKeys[0]]?.value
    const time = evidence.metrics[row.metricKeys[1]]?.value
    return <div className="report-stacked-row" key={row.label}><span>{row.label}</span>
      <div className="report-progress-track" aria-label={row.label + ' · Zielverlauf'}>
        {achievement !== null && achievement !== undefined && <div className="report-progress-fill" style={{ width: Math.max(0, achievement) / max * 100 + '%' }} />}
        {time !== null && time !== undefined && <span className="report-time-marker" style={{ left: time / max * 100 + '%' }} title={'Zeitanteil: ' + time.toLocaleString('de-DE') + ' %'} />}
      </div><MetricLink metricKey={row.metricKeys[0]} /></div>
  })}</div>
}

function PanelTable({ panel }: { panel: ReportPanel }) {
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(0)
  const rows = panel.rows.filter(r => r.label.toLocaleLowerCase('de-DE').includes(query.toLocaleLowerCase('de-DE')))
  const pages = Math.max(1, Math.ceil(rows.length / 25))
  const current = Math.min(page, pages - 1)
  return <div className="panel-table">
    {panel.rows.length > 10 && <label>Zeilen durchsuchen<input type="search" value={query} onChange={e => { setQuery(e.target.value); setPage(0) }} /></label>}
    <div className="table-wrap"><table><thead><tr><th>{panel.area === 'lifetime' ? 'Jahr' : 'Auswertung'}</th>{panel.columns.map((c, i) => <th key={i}>{c}</th>)}</tr></thead>
      <tbody>{rows.slice(current * 25, (current + 1) * 25).map((row, i) => <tr key={i}><th scope="row">{row.label}</th>{row.metricKeys.map(key => <td key={key}><MetricLink metricKey={key} /></td>)}</tr>)}</tbody>
    </table></div>
    {pages > 1 && <div className="button-row"><button className="secondary-button" disabled={current === 0} onClick={() => setPage(current - 1)}>Zurück</button><span>Seite {current + 1} von {pages} · {rows.length} Zeilen</span><button className="secondary-button" disabled={current + 1 === pages} onClick={() => setPage(current + 1)}>Weiter</button></div>}
  </div>
}

function PanelPlot({ panel, percent }: { panel: ReportPanel; percent: boolean }) {
  const { evidence, open } = useEvidence()
  const data = panel.rows.map(row => row.metricKeys.map(k => evidence.metrics[k]))
  const values = data.map(row => {
    const sum = row.reduce((sum, m) => sum + (m?.value ?? 0), 0)
    return row.map(m => m?.value == null ? null : percent ? sum > 0 ? m.value / sum * 100 : 0 : m.value)
  })
  const hasValues = values.some(row => row.some(v => v !== null))
  if (!hasValues) return <p className="muted">Für das Diagramm fehlen berechenbare Werte. Die Begründungen stehen in den Kennzahldetails.</p>
  const label = (metric: ReportMetric | undefined) => metric ? metric.label + ': ' + metricValue(metric) : 'Keine Daten'
  const activate = (key: string) => ({ role: 'button' as const, tabIndex: 0, onClick: () => open(key),
    onKeyDown: (e: React.KeyboardEvent<SVGElement>) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(key) } } })
  const legend = <div className="report-chart-legend">{panel.columns.map((c, i) => <span key={i}><i style={{ background: colors[i % colors.length] }} />{c}</span>)}</div>
  if (panel.kind === 'stacked' || panel.kind === 'bar') {
    if (values.some(row => row.some(v => v !== null && v < 0))) return <p>Negative Werte sind in der Tabelle ausgewiesen; eine gemeinsame gestapelte Balkenskala ist dafür nicht verfügbar.</p>
    const max = Math.max(1, ...values.map(row => row.reduce<number>((sum, v) => sum + Math.max(0, v ?? 0), 0)))
    return <>{legend}<div className="report-stacked-bars">{panel.rows.map((row, i) => <div className="report-stacked-row" key={i}>
      <span>{row.label}</span><div className="report-stacked-track">{row.metricKeys.map((key, j) => <button key={key} type="button" onClick={() => open(key)} title={label(data[i][j])}
        aria-label={row.label + ' · ' + label(data[i][j])}
        style={{ width: Math.max(0, values[i][j] ?? 0) / max * 100 + '%', background: colors[j % colors.length] }} />)}</div>
    </div>)}</div></>
  }
  const stacked = panel.kind === 'area'
  const pareto = panel.kind === 'pareto'
  if (stacked && values.some(row => row.some(v => v !== null && v < 0))) return <p>Negative Werte können nicht als gestapelte Fläche dargestellt werden.</p>
  const rawMax = stacked ? Math.max(1, ...values.map(row => row.reduce<number>((sum, v) => sum + (v ?? 0), 0))) :
    Math.max(1, ...values.flatMap(row => pareto ? [row[0] ?? 0] : row.map(v => v ?? 0)))
  const min = stacked || pareto ? 0 : Math.min(0, ...values.flatMap(row => row.map(v => v ?? 0)))
  const x = (i: number) => 65 + i * 730 / Math.max(1, panel.rows.length - 1)
  const y = (value: number, series = 0) => 245 - ((pareto && series === 1 ? value / 100 : (value - min) / (rawMax - min)) * 210)
  const top = (row: number, series: number) => values[row].slice(0, series + 1).reduce<number>((sum, v) => sum + (v ?? 0), 0)
  return <>{legend}<svg className="report-series-svg" viewBox="0 0 860 295" role="img" aria-label={panel.title + (percent ? ' · Prozent' : '')}>
    <line x1="65" y1="245" x2="795" y2="245" stroke="currentColor" />
    {[0, .5, 1].map(t => <g key={t}><line x1="65" y1={245 - t * 210} x2="795" y2={245 - t * 210} className="chart-grid-line" />
      <text x="58" y={249 - t * 210} textAnchor="end">{(min + (rawMax - min) * t).toLocaleString('de-DE', { maximumFractionDigits: 1 })}{percent ? '%' : ''}</text></g>)}
    {stacked && panel.columns.map((_, j) => {
      if (values.some(row => row[j] === null)) return null
      const points = values.map((_, i) => `${x(i)},${y(top(i, j))}`).concat(values.map((_, i) => `${x(i)},${y(j > 0 ? top(i, j - 1) : 0)}`).reverse()).join(' ')
      return <polygon key={j} points={points} fill={colors[j % colors.length]} opacity=".65" />
    })}
    {panel.columns.map((_, j) => <g key={j}>
      {pareto && j === 0 ? values.map((row, i) => row[0] === null ? null : <rect key={i} x={x(i) - 12} y={y(row[0])} width="24" height={245 - y(row[0])} fill={colors[0]} {...activate(panel.rows[i].metricKeys[0])}><title>{panel.rows[i].label + ' · ' + label(data[i][0])}</title></rect>)
        : <path d={values.map((row, i) => row[j] === null ? '' : `${i === 0 || values[i - 1][j] === null ? 'M' : 'L'} ${x(i)} ${y(stacked ? top(i, j) : row[j] ?? 0, j)}`).join(' ')} fill="none" stroke={colors[j % colors.length]} strokeWidth="2" />}
      {values.map((row, i) => row[j] === null || !panel.rows[i].metricKeys[j] ? null : <circle key={i} cx={x(i)} cy={y(stacked ? top(i, j) : row[j] ?? 0, j)} r="5" fill={colors[j % colors.length]}
        aria-label={panel.rows[i].label + ' · ' + label(data[i][j])} {...activate(panel.rows[i].metricKeys[j])}><title>{panel.rows[i].label + ' · ' + label(data[i][j])}</title></circle>)}
    </g>)}
    {panel.rows.map((row, i) => <text key={i} x={x(i)} y="270" textAnchor="middle">{row.label.length > 16 ? row.label.slice(0, 14) + '…' : row.label}</text>)}
    {pareto && <text x="805" y="35">100 %</text>}
  </svg></>
}
