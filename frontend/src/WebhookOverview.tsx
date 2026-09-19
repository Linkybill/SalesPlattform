import { useEffect, useState } from 'react'

type Subscription = {
  module: string; status: string; expiresAt: string | null
  lastCheckedAt: string | null; lastRenewedAt: string | null; error: string | null
}
type HookEvent = {
  id: string; module: string; operation: string; status: string; attemptCount: number
  receivedAt: string; processedAt: string | null; error: string | null
}
export type HookOverviewData = {
  observedAt: string; callbackBaseUrl: string | null; schemaCached: boolean
  callbackUrlSource: string; callbackUrlError: string | null
  subscriptions: Subscription[]; counts: Record<string, number>
  total: number; page: number; pageSize: number; events: HookEvent[]
}
const labels: Record<string, string> = {
  active: 'Registriert', expired: 'Abgelaufen', missing: 'Nicht registriert',
  unavailable: 'Nicht im Schema verfügbar', 'schema-missing': 'Schema fehlt',
  'unknown-expiry': 'Ablauf unbekannt', failed: 'Fehlgeschlagen',
  queued: 'Wartend', processing: 'Import läuft', processed: 'Importiert',
  create: 'Angelegt', edit: 'Geändert', delete: 'Gelöscht',
}
const label = (value: string) => labels[value] ?? value
const date = (value: string | null) => value ? new Date(value).toLocaleString('de-DE') : '—'

// Mounted with a tenant/user key by ImportPage. Abort + disposed guard also prevent stale filter responses.
export function WebhookOverview({ authorizedFetch, jobsUrl, onError }: {
  authorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  jobsUrl: string
  onError: () => void
}) {
  const [filters, setFilters] = useState({ module: '', status: '', page: 1 })
  const [refresh, setRefresh] = useState(0)
  const [data, setData] = useState<HookOverviewData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    let disposed = false
    setLoading(true)
    setError(null)
    setData(null)
    const query = new URLSearchParams({ ...filters, page: String(filters.page), pageSize: '25' })
    void (async () => {
      try {
        const response = await authorizedFetch(`/api/integrations/zoho/hooks?${query}`, { signal: controller.signal })
        if (!response.ok) throw new Error(response.status === 403
          ? 'Hook-Übersicht nur für Tenant-Administratoren verfügbar.'
          : `Hook-Übersicht konnte nicht geladen werden (HTTP ${response.status}).`)
        const payload = await response.json() as HookOverviewData
        if (!disposed) setData(payload)
      } catch (reason) {
        if (!disposed) {
          onError()
          setError(reason instanceof Error ? reason.message : 'Hook-Übersicht nicht erreichbar.')
        }
      } finally {
        if (!disposed) setLoading(false)
      }
    })()
    return () => { disposed = true; controller.abort() }
  }, [authorizedFetch, filters, refresh, onError])

  return <section className="sales-card integration-card hook-overview" aria-labelledby="hook-title" aria-busy={loading}>
    <div className="card-heading">
      <div><p className="sales-eyebrow">ZOHO · EINGANG UND IMPORT</p><h2 id="hook-title">Hooks und Ereignisse</h2></div>
      <button className="secondary-button" type="button" disabled={loading} onClick={() => setRefresh(x => x + 1)}>Hooks aktualisieren</button>
    </div>
    <p>Zoho meldet Änderungen an diese Callback-URL. Die Registrierung pro Mandant und Modul übernimmt
      der Job „CRM-Hooks erneuern“ automatisch; keine einzelnen Hooks von Hand anlegen.</p>
    <p>Der Callback speichert zunächst nur das Ereignis. Der Job importiert danach die betroffenen Datensätze
      (höchstens 100 Ereignisse pro Lauf). Bei täglichem Zeitplan kann ein Ereignis bis zum nächsten Lauf warten;
      größere Rückstände benötigen mehrere Läufe. <a href={jobsUrl}>Zeitplan und Jobprotokolle öffnen</a>.</p>
    {error && <div className="message error-message" role="alert">{error}</div>}
    {loading && <p role="status">Hook-Übersicht wird geladen …</p>}
    {data && <>
      <p>Wirksame Basis-URL: <code className="hook-url">{data.callbackBaseUrl ?? 'Nicht konfiguriert / ungültig'}</code><br />
        Quelle: {data.callbackUrlSource === 'tenantApp' ? 'Mandanten-AppSetting zoho.webhookUrl' : data.callbackUrlSource === 'deployment' ? 'Deployment-Standard (kein Mandantenwert gesetzt)' : 'Nicht konfiguriert'}.<br />
        Ändern unter Tenant-Portal → SalesPlattform → AppSettings → Zoho Webhook-URL.<br />
        <small>Die Registrierung ergänzt <code>?tenant_id=…</code> automatisch. Konfiguration ist kein Erreichbarkeitsnachweis.</small></p>
      {data.callbackUrlError && <div className="message error-message">{data.callbackUrlError}</div>}
      {!data.schemaCached && <div className="message">Zuerst den Job „Zoho-Schema cachen“ starten. Ohne Schema werden keine Hooks registriert.</div>}
      <details>
        <summary>Modul-Hooks ({data.subscriptions.filter(x => x.status === 'active').length} registriert / {data.subscriptions.length} geprüft)</summary>
        <p>Registriert bedeutet lokal gespeicherte, noch nicht abgelaufene Subscription, kein Live-Test bei Zoho.
          Bei „Nicht registriert“ den Job starten. Bei deaktivierten Hooks die Einstellung <code>crm.changeDetectionMode</code> prüfen.</p>
        <div className="table-wrap" tabIndex={0} role="region" aria-label="Modul-Hooks">
          <table><thead><tr><th>Modul</th><th>Status</th><th>Gültig bis</th><th>Letzte Prüfung</th><th>Letzte Erneuerung</th></tr></thead>
            <tbody>{data.subscriptions.map(x => <tr key={x.module}><td>{x.module}</td><td>{label(x.status)}{x.error && <small className="table-note">{x.error}</small>}</td>
              <td>{date(x.expiresAt)}</td><td>{date(x.lastCheckedAt)}</td><td>{date(x.lastRenewedAt)}</td></tr>)}</tbody></table>
        </div>
      </details>
    </>}
    <div className="hook-filters">
      <label>Modul <select value={filters.module} onChange={e => setFilters(x => ({ ...x, module: e.target.value, page: 1 }))}>
        <option value="">Alle Module</option>
        {/* Preserve the selected option while a filtered request is loading. */}
        {Array.from(new Set([filters.module, ...(data?.subscriptions.map(x => x.module) ?? [])])).filter(Boolean).map(x => <option key={x}>{x}</option>)}
      </select></label>
      <label>Status <select value={filters.status} onChange={e => setFilters(x => ({ ...x, status: e.target.value, page: 1 }))}>
        <option value="">Alle Status</option>{['queued', 'processing', 'processed', 'failed'].map(x => <option key={x} value={x}>{label(x)}</option>)}
      </select></label>
    </div>
    {data && <>
      <p>Gespeicherte Ereignisse {filters.module ? `für ${filters.module}` : 'aller Module'} (gesamte Historie, unabhängig vom Statusfilter):{' '}
        {['queued', 'processing', 'processed', 'failed'].map(x => `${data.counts[x] ?? 0} ${label(x)}`).join(' · ')}</p>
      <div className="table-wrap" tabIndex={0} role="region" aria-label="Hook-Ereignisse">
        <table><thead><tr><th>Eingang</th><th>Modul / Änderung</th><th>Status</th><th>Versuche</th><th>Importiert am</th><th>Protokoll</th></tr></thead>
          <tbody>{data.events.map(x => <tr key={x.id}><td>{date(x.receivedAt)}</td><td>{x.module} · {label(x.operation)}</td><td>{label(x.status)}
            {x.status === 'failed' && <small className="table-note">{x.attemptCount >= 5 ? 'Versuchslimit erreicht – prüfen' : 'Erneuter Versuch im nächsten Joblauf'}</small>}
          </td><td>{x.attemptCount} / 5</td><td>{date(x.processedAt)}</td><td><details><summary>Ereignisdetails</summary>
            <p>Ereignis-ID: <code className="hook-url">{x.id}</code></p><p>Über diese ID im Jobprotokoll zuordnen (für neue Verarbeitungen).</p>
            {x.error && <p className="table-critical">{x.error}</p>}
          </details></td></tr>)}</tbody></table>
      </div>
      {data.events.length === 0 && <p role="status">Keine Hook-Ereignisse für diese Auswahl gespeichert.</p>}
      <div className="button-row">
        <button className="secondary-button" type="button" disabled={filters.page <= 1} onClick={() => setFilters(x => ({ ...x, page: x.page - 1 }))}>Zurück</button>
        <span>Seite {data.page} · {data.total} Ereignisse</span>
        <button className="secondary-button" type="button" disabled={data.page * data.pageSize >= data.total || data.page >= 10000} onClick={() => setFilters(x => ({ ...x, page: x.page + 1 }))}>Weiter</button>
      </div>
      <p><small>Stand: {date(data.observedAt)}. „Importiert“ bestätigt nur die Übernahme der CRM-Daten.
        Regeln, CRM-Aufgaben und Benachrichtigungen separat im Jobprotokoll prüfen.
        Abgewiesene Aufrufe und ignorierte Dubletten stehen nur im technischen Log, nicht als neue Ereignisse hier.</small></p>
    </>}
  </section>
}
