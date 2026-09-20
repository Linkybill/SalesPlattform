import { useEffect, useRef, useState } from 'react'

type Verification = {
  checkedAt: string; module: string; status: string; message: string
  expectedUrl: string | null; registeredUrl: string | null
  expiresAt: string | null; events: string[] | null
  filters?: { status: string; message: string; conditions: string[] } | null
  tokenCheck?: { status: string; message: string } | null
  localChannelCheck?: { status: string; message: string } | null
  localExpiresAt?: string | null; notifyOnRelatedAction?: boolean | null
}

export function ZohoHookCheck({ authorizedFetch, modules }: {
  authorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  modules: string[]
}) {
  const [module, setModule] = useState('Calls')
  const [result, setResult] = useState<Verification | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const active = useRef<AbortController | null>(null)
  // Parent is keyed by tenant/user. No late result may survive unmount or another selection.
  useEffect(() => () => { active.current?.abort(); active.current = null }, [])
  async function check() {
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    setLoading(true); setResult(null); setError(null)
    try {
      const response = await authorizedFetch('/api/integrations/zoho/hooks/check', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ module }), signal: controller.signal,
      })
      if (!response.ok) throw new Error(response.status === 403
        ? 'Nur Mandanten-Administratoren dürfen die Registrierung prüfen.'
        : `Zoho-Prüfung nicht verfügbar (HTTP ${response.status}).`)
      const value = await response.json() as Verification
      if (active.current === controller) setResult(value)
    } catch (reason) {
      if (active.current === controller && !controller.signal.aborted)
        setError(reason instanceof Error ? reason.message : 'Zoho-Prüfung fehlgeschlagen.')
    } finally {
      if (active.current === controller) { setLoading(false); active.current = null }
    }
  }
  return <section aria-label="Registrierung bei Zoho prüfen" aria-busy={loading}>
    <h3>Registrierung bei Zoho prüfen</h3>
    <p>Manuelle Live-Abfrage für ein Modul. Ändert keine Hooks und sendet keinen Test-Callback;
      benötigt ZohoCRM.notifications.READ und verbraucht Zoho-API-Anfragen.</p>
    <div className="button-row">
      <label>Modul prüfen <select value={module} disabled={loading} onChange={event => {
        active.current?.abort(); active.current = null
        setModule(event.target.value); setResult(null); setError(null); setLoading(false)
      }}>{Array.from(new Set(['Calls', ...modules])).map(name => <option key={name}>{name}</option>)}</select></label>
      <button type="button" className="secondary-button" disabled={loading} onClick={() => void check()}>
        {loading ? 'Wird bei Zoho geprüft …' : 'Registrierung bei Zoho prüfen'}
      </button>
    </div>
    {error && <p className="message error-message" role="alert">{error}</p>}
    {result && <div role="status" className={result.status === 'verified' && result.filters?.status === 'none'
      && result.tokenCheck?.status === 'match' && result.localChannelCheck?.status === 'ready' ? 'message' : 'message error-message'}>
      <p><strong>{result.module}</strong> · Geprüft: {new Date(result.checkedAt).toLocaleString('de-DE')}</p>
      <p>{result.message}</p>
      {result.expectedUrl && <p>Erwartete Callback-Adresse: <code className="hook-url">{result.expectedUrl}</code></p>}
      {result.registeredUrl && <p>Bei Zoho gespeichert (sicher gekürzt): <code className="hook-url">{result.registeredUrl}</code></p>}
      {result.expiresAt && <p>Bei Zoho gültig bis: {new Date(result.expiresAt).toLocaleString('de-DE')}</p>}
      {result.events && <p>Erkannte Ereignisse: {result.events.join(', ') || 'Keine erwarteten Ereignisse'}</p>}
      <p><strong>Feldfilter:</strong> {result.filters?.message ?? 'Nicht geprüft – erweiterte Backend-Diagnose erforderlich.'}</p>
      {result.filters && result.filters.conditions.length > 0 && <ul>
        {result.filters.conditions.map((condition, index) => <li key={index}><code className="hook-url">{condition}</code></li>)}
      </ul>}
      <p><strong>Verification-Token:</strong> {result.tokenCheck?.message ?? 'Nicht geprüft.'}</p>
      <p><strong>Lokaler Channel:</strong> {result.localChannelCheck?.message ?? 'Nicht geprüft.'}</p>
      {result.localExpiresAt && <p>Lokal gültig bis: {new Date(result.localExpiresAt).toLocaleString('de-DE')}</p>}
      <p>Benachrichtigungen wegen verknüpfter Datensätze: {result.notifyOnRelatedAction === true ? 'aktiviert'
        : result.notifyOnRelatedAction === false ? 'deaktiviert (direkte Änderungen am gewählten Modul bleiben relevant)'
          : 'nicht ermittelbar'}.</p>
      <p>Diese Prüfung verändert keine Registrierung. „Hooks aktualisieren“ oder ein manueller Start von
        „CRM-Hooks erneuern“ baut die Modul-Hooks neu auf. Nur geplante Wartungsläufe lassen gültige Hooks
        außerhalb der 36-Stunden-Frist unverändert.</p>
    </div>}
  </section>
}
