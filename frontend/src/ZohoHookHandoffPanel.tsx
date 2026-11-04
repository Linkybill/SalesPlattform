import { useEffect, useRef, useState } from 'react'
import { ZohoHookRebuild } from './ZohoHookRebuild'

export type HookHandoff = {
  id: string; transitionId: string; placementGeneration: number; status: string
  message: string; canConfirmRelay: boolean; channels: number; verifiedChannels: number
}

export function ZohoHookHandoffPanel({ handoff, authorizedFetch, platformAuthorizedFetch, applicationKey, tenantId, jobsUrl, onChanged }: {
  handoff: HookHandoff
  authorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  platformAuthorizedFetch: (url: string, init?: RequestInit) => Promise<Response>
  applicationKey: string; tenantId: string; jobsUrl: string; onChanged: () => void
}) {
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const active = useRef<AbortController | null>(null)
  useEffect(() => () => { active.current?.abort(); active.current = null }, [])
  async function confirmRelay() {
    if (!confirmed || active.current || !handoff.canConfirmRelay) return
    const controller = new AbortController()
    active.current = controller; setBusy(true); setError(null)
    try {
      const response = await authorizedFetch('/api/integrations/zoho/hooks/handoff/confirm-relay', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: controller.signal,
        body: JSON.stringify({ handoffId: handoff.id, transitionId: handoff.transitionId, placementGeneration: handoff.placementGeneration }),
      })
      if (active.current !== controller) return
      if (!response.ok) throw new Error('Die Bestätigung wurde nicht übernommen. Aktuelle Übergabe und Mandantenberechtigung prüfen.')
      setConfirmed(false); onChanged()
    } catch (reason) {
      if (active.current === controller && !controller.signal.aborted)
        setError(reason instanceof Error ? reason.message : 'Bestätigung nicht nachgewiesen. Übersicht erneut laden.')
    } finally {
      if (active.current === controller) { active.current = null; setBusy(false) }
    }
  }
  return <section className="message" aria-label="Callback-Übergabe" aria-busy={busy}>
    <h3>{handoff.status === 'completed' ? 'Callback-Übergabe bestätigt' : 'Callback-Übergabe offen'}</h3>
    <p>{handoff.message}</p>
    <p>{handoff.verifiedChannels} von {handoff.channels} bestehenden Modul-Channels bestätigt.</p>
    {handoff.canConfirmRelay && <>
      <label><input type="checkbox" checked={confirmed} disabled={busy} onChange={event => setConfirmed(event.target.checked)} />{' '}
        Ich habe geprüft, dass die eigene Callback-Adresse jetzt zu dieser Installation weiterleitet.</label>
      <button type="button" className="secondary-button" disabled={busy || !confirmed} onClick={() => void confirmRelay()}>Weiterleitung bestätigen</button>
    </>}
    {error && <p className="error-message" role="alert">{error}</p>}
    {handoff.status !== 'completed' && <ZohoHookRebuild authorizedFetch={authorizedFetch} platformAuthorizedFetch={platformAuthorizedFetch}
      applicationKey={applicationKey} tenantId={tenantId} jobsUrl={jobsUrl} disabled={busy || handoff.canConfirmRelay} handoffOnly />}
  </section>
}
