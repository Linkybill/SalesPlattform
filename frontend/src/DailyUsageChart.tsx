import { useEffect, useState } from 'react'

type Day = {
  date: string; requests: number; successfulRequests: number; failedRequests: number
  estimatedUnits: number; isPartial: boolean
}
type Series = { providerKey: string; connectionKey: string; usageUnit: string; days: Day[] }
export type DailyUsageReport = { fromUtc: string; toUtc: string; timeZone: string; series: Series[] }
const seriesKey = (series: Series) => JSON.stringify([series.providerKey, series.connectionKey, series.usageUnit])
const dateLabel = (date: string) => date.split('-').reverse().join('.')
const count = (value: number) => value.toLocaleString('de-DE')

export function DailyUsageChart({ authorizedFetch, onError }: {
  authorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  onError: () => void
}) {
  const [days, setDays] = useState(30)
  const [refresh, setRefresh] = useState(0)
  const [data, setData] = useState<DailyUsageReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selection, setSelection] = useState('')
  const [metric, setMetric] = useState<'estimatedUnits' | 'requests' | 'failedRequests'>('estimatedUnits')
  const [selectedDate, setSelectedDate] = useState<string | null>(null)
  useEffect(() => {
    let disposed = false
    const controller = new AbortController()
    setData(null)
    setError(null)
    setLoading(true)
    setSelectedDate(null)
    void (async () => {
      try {
        const response = await authorizedFetch(`/api/integrations/usage/daily?days=${days}`, { signal: controller.signal })
        if (!response.ok) throw new Error(response.status === 403 ? 'Tagesverbrauch nur für Tenant-Administratoren verfügbar.'
          : `Tagesverbrauch konnte nicht geladen werden (HTTP ${response.status}).`)
        const result = await response.json() as DailyUsageReport
        if (!disposed) setData(result)
      } catch (reason) {
        if (!disposed) {
          onError()
          setError(reason instanceof Error ? reason.message : 'Tagesverbrauch nicht erreichbar.')
        }
      } finally {
        if (!disposed) setLoading(false)
      }
    })()
    return () => { disposed = true; controller.abort() }
  }, [authorizedFetch, onError, days, refresh])

  const series = data?.series.find(x => seriesKey(x) === selection) ?? data?.series[0]
  const unit = metric === 'estimatedUnits' ? series?.usageUnit ?? 'Einheiten' : metric === 'requests' ? 'Requests' : 'Fehler'
  const values = series?.days ?? []
  const max = Math.max(1, ...values.map(x => x[metric]))
  const selectedDay = values.find(x => x.date === selectedDate)

  return <section className="sales-card integration-card daily-usage" aria-labelledby="daily-usage-title" aria-busy={loading}>
    <div className="card-heading">
      <div><p className="sales-eyebrow">VERBRAUCH PRO KALENDERTAG</p><h2 id="daily-usage-title">Tagesverlauf</h2></div>
      <div className="report-toolbar">
        <label>Tageszeitraum<select aria-label="Tageszeitraum" value={days} onChange={e => setDays(Number(e.target.value))}>
          <option value="7">7 Tage</option><option value="30">30 Tage</option><option value="90">90 Tage</option>
        </select></label>
        <button type="button" className="secondary-button" disabled={loading} onClick={() => setRefresh(x => x + 1)}>Tagesverlauf aktualisieren</button>
      </div>
    </div>
    <p>Quelle: gespeicherte CRM-HTTP-Versuche dieses Mandanten. Verbrauch = Summe der geschätzten Einheiten;
      bei Zoho API-Credits, keine KI-Tokens. Jeder Wiederholungsversuch wird separat gezählt.</p>
    <p>Der Tageszeitraum gilt nur für dieses Diagramm, unabhängig vom Stundenfilter der übrigen Übersicht.
      Tagesgrenzen: 00:00–24:00 UTC, unabhängig von der Browserzeitzone. Heute ist noch unvollständig.
      0 bedeutet keine aufgezeichneten Aufrufe; fehlende Messungen lassen sich daraus nicht ausschließen.</p>
    {loading && <p role="status">Tagesverbrauch wird geladen …</p>}
    {error && <div className="message error-message" role="alert">{error}</div>}
    {data && !series && <p role="status">Keine Verbrauchsdaten im gewählten Tageszeitraum erfasst.</p>}
    {series && data && <>
      <div className="report-toolbar">
        <label>Provider / Verbindung / Einheit<select aria-label="Verbrauchsreihe" value={seriesKey(series)} onChange={e => { setSelection(e.target.value); setSelectedDate(null) }}>
          {data.series.map(x => <option key={seriesKey(x)} value={seriesKey(x)}>{x.providerKey} · {x.connectionKey} · {x.usageUnit}</option>)}
        </select></label>
        <label>Darstellung<select aria-label="Verbrauchskennzahl" value={metric} onChange={e => setMetric(e.target.value as typeof metric)}>
          <option value="estimatedUnits">Verbrauchseinheiten (geschätzt)</option><option value="requests">Requests</option><option value="failedRequests">Fehlgeschlagene Requests</option>
        </select></label>
      </div>
      <p><strong>{count(values.reduce((sum, x) => sum + x[metric], 0))} {unit}</strong> im Tageszeitraum ·
        Stand: {new Date(data.toUtc).toLocaleString('de-DE', { timeZone: 'UTC' })} UTC</p>
      <figure className="daily-usage-figure">
        <figcaption>{unit} je Tag · Skala 0–{count(Math.max(0, ...values.map(x => x[metric])))} · Balken anklicken für Tageswerte</figcaption>
        <div className="daily-usage-scroll" tabIndex={0} role="region" aria-label="Verbrauchsdiagramm, horizontal scrollbar">
          <div className="daily-usage-bars" style={{ gridTemplateColumns: `repeat(${values.length}, minmax(44px, 1fr))` }}>
            {values.map(day => <button type="button" key={day.date} className="daily-usage-day"
              aria-label={`${dateLabel(day.date)}: ${count(day[metric])} ${unit}${day.isPartial ? ', unvollständig' : ''}`}
              aria-pressed={selectedDate === day.date} onClick={() => setSelectedDate(day.date)}>
              <span className="daily-usage-value">{count(day[metric])}</span>
              <span className="daily-usage-track" aria-hidden="true"><span style={{ height: `${day[metric] / max * 100}%` }} /></span>
              <span>{dateLabel(day.date).slice(0, 5)}{day.isPartial ? '*' : ''}</span>
            </button>)}
          </div>
        </div>
      </figure>
      {selectedDay && <div className="daily-usage-detail" role="status">
        <strong>{dateLabel(selectedDay.date)} (UTC){selectedDay.isPartial ? ' · unvollständig' : ''}</strong>
        <p>{count(selectedDay.estimatedUnits)} {series.usageUnit} geschätzt · {count(selectedDay.requests)} Requests,
          davon {count(selectedDay.successfulRequests)} erfolgreich und {count(selectedDay.failedRequests)} fehlgeschlagen.</p>
      </div>}
      <details><summary>Tageswerte als Tabelle</summary>
        <div className="table-wrap" tabIndex={0} role="region" aria-label="Tagesverbrauchstabelle">
          <table><thead><tr><th>Datum (UTC)</th><th>{series.usageUnit} (geschätzt)</th><th>Requests</th><th>Erfolgreich</th><th>Fehlgeschlagen</th></tr></thead>
            <tbody>{values.map(day => <tr key={day.date}><td>{dateLabel(day.date)}{day.isPartial ? ' · unvollständig' : ''}</td>
              <td>{count(day.estimatedUnits)}</td><td>{count(day.requests)}</td><td>{count(day.successfulRequests)}</td><td>{count(day.failedRequests)}</td></tr>)}</tbody>
          </table>
        </div>
      </details>
    </>}
  </section>
}
