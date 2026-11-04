import { useEffect, useRef, useState } from 'react'
import { deploymentEnvironment } from './deploymentEnvironment'
import { requestZohoHookRebuild } from './ZohoHookRebuildRequest'

export function ZohoHookRebuild({ authorizedFetch, platformAuthorizedFetch, applicationKey, tenantId, jobsUrl, disabled, handoffOnly = false }: {
  authorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  platformAuthorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  applicationKey: string; tenantId: string; jobsUrl: string; disabled: boolean; handoffOnly?: boolean
}) {
  const [starting, setStarting] = useState(false)
  const [runId, setRunId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const active = useRef<AbortController | null>(null)
  // Parent remounts on tenant/user change. A late response cannot update a new tenant.
  useEffect(() => () => { active.current?.abort(); active.current = null }, [])
  async function rebuild() {
    if (active.current || disabled) return
    if (!window.confirm(handoffOnly
      ? 'Die vorhandenen Zoho-Channels dieser Installationsübergabe erneut prüfen und gegebenenfalls ihre Callback-Adresse aktualisieren? Channels und Tokens bleiben erhalten.'
      : 'Alle verfügbaren Zoho-Modul-Hooks dieses Mandanten mit neuen Channel-IDs und Tokens neu registrieren? Dies verbraucht Zoho-API-Anfragen. Der Job verarbeitet anschließend auch wartende Ereignisse.')) return
    const controller = new AbortController()
    active.current = controller
    setStarting(true); setRunId(null); setError(null)
    try {
      // Same tenant-admin authorization, durable queue and concurrency group as /jobs.
      // Never retry a POST automatically: a lost reply may still mean the job was accepted.
      const response = await requestZohoHookRebuild({ authorizedFetch, platformAuthorizedFetch, applicationKey, tenantId,
        installationId: deploymentEnvironment.VITE_INSTALLATION_ID, signal: controller.signal, handoffOnly })
      if (active.current !== controller) return
      if (!response.ok) {
        setError(response.status === 403 ? 'Nur Mandanten-Administratoren dürfen Hooks neu registrieren.'
          : response.status === 409 ? 'Der Job kann aktuell nicht gestartet werden. Einen laufenden oder blockierenden Job unter Jobs prüfen.'
            : 'Jobstart nicht bestätigt. Zuerst unter Jobs prüfen, ob ein Lauf angelegt wurde; nicht ungeprüft erneut starten.')
        return
      }
      const run = await response.json() as { id?: string }
      if (typeof run.id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(run.id))
        throw new Error('Invalid job response')
      if (active.current === controller) setRunId(run.id)
    } catch {
      if (active.current === controller && !controller.signal.aborted)
        setError('Jobstart nicht bestätigt. Zuerst unter Jobs prüfen, ob ein Lauf angelegt wurde; nicht ungeprüft erneut starten.')
    } finally {
      if (active.current === controller) { setStarting(false); active.current = null }
    }
  }
  return <section aria-label={handoffOnly ? 'Callback-Übergabe fortsetzen' : 'Hooks neu registrieren'} aria-busy={starting}>
    <button type="button" className="secondary-button" disabled={disabled || starting} onClick={() => void rebuild()}>
      {starting ? 'Job wird gestartet …' : handoffOnly ? 'Übergabe fortsetzen' : 'Hooks aktualisieren'}
    </button>
    {handoffOnly ? <p>Prüft dieselben Channels erneut. Ein verlorener Antwortaufruf erzeugt keine neuen Channels oder Tokens.</p>
      : <p>Registriert alle verfügbaren Modul-Hooks dieses Mandanten neu, unabhängig von den Listenfiltern und der Restlaufzeit.
        Zuerst die alten Channels deaktivieren, anschließend neue registrieren und lokal speichern. Eine offene Installationsübergabe hat Vorrang.</p>}
    {error && <p role="alert" className="message error-message">{error} <a href={jobsUrl}>Jobs öffnen</a>.</p>}
    {runId && <p role="status" className="message">{handoffOnly ? 'Übergabeprüfung' : 'Neuaufbau'} als Job angenommen – noch kein Abschlussnachweis.
      Lauf-ID: <code className="hook-url">{runId}</code>. <a href={jobsUrl}>Fortschritt und Fehler im Jobprotokoll öffnen</a>.
      Nach Abschluss „Übersicht aktualisieren“ und „Registrierung bei Zoho prüfen“ verwenden.</p>}
  </section>
}
