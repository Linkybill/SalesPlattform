import { useEffect, useRef, useState } from 'react'
import { createPlatformLogOperation, useApplicationContext, usePlatformLog } from '@hammer2fall/identity-platform-react'

type TargetPlan = { startsAt: string; endsAt: string; revision: string; entries: { ownerId: string; name: string; amount: number | null }[] }
const euro = (value: number) => value.toLocaleString('de-DE', { style: 'currency', currency: 'EUR' })

export function AnnualTargetsEditor({ onClose, onSaved }: { onClose: () => void; onSaved: () => Promise<void> }) {
  const { authorizedFetch, activeTenantId } = useApplicationContext()
  const log = usePlatformLog()
  const dialog = useRef<HTMLDialogElement>(null)
  const [plan, setPlan] = useState<TargetPlan | null>(null)
  const [amounts, setAmounts] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    const opener = document.activeElement as HTMLElement | null
    dialog.current?.showModal()
    return () => { mounted.current = false; dialog.current?.close(); opener?.focus() }
  }, [])
  useEffect(() => {
    let live = true
    const operation = createPlatformLogOperation(log, authorizedFetch)
    void (async () => {
      try {
        const response = await operation.fetch('/api/reports/annual-targets')
        if (!response.ok) throw new Error(response.status === 403 ? 'Du darfst Jahresziele nicht bearbeiten.' : 'Die Zielplanung kann nicht geladen werden. Bitte prüfe die vorhandenen Ziele und öffne die Maske erneut.')
        const next = await response.json() as TargetPlan
        if (!live) return
        setPlan(next)
        setAmounts(Object.fromEntries(next.entries.map(e => [e.ownerId, e.amount === null ? '' : String(e.amount)])))
      } catch (reason) {
        if (live) setError(reason instanceof Error ? reason.message : 'Zielplanung konnte nicht geladen werden.')
      }
    })()
    return () => { live = false }
  }, [activeTenantId, authorizedFetch, log])
  const total = Object.values(amounts).reduce((sum, value) => sum + (Number(value) || 0), 0)

  async function save() {
    if (!plan || savingRef.current) return
    savingRef.current = true
    setSaving(true)
    setError(null)
    const operation = createPlatformLogOperation(log, authorizedFetch)
    try {
      const response = await operation.fetch('/api/reports/annual-targets', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ revision: plan.revision, entries: plan.entries.map(e => ({ ownerId: e.ownerId, amount: amounts[e.ownerId] === '' ? null : Number(amounts[e.ownerId]) })) }),
      })
      if (!response.ok) throw new Error(response.status === 409 ? 'Die Zielplanung wurde geändert. Bitte schließen und erneut öffnen.'
        : response.status === 403 ? 'Du darfst Jahresziele nicht bearbeiten.' : 'Die Jahresziele konnten nicht gespeichert werden. Bitte Eingaben prüfen.')
      if (mounted.current) await onSaved()
    } catch (reason) {
      if (mounted.current) setError(reason instanceof Error ? reason.message : 'Speichern fehlgeschlagen. Bitte die gespeicherten Ziele vor einem erneuten Versuch prüfen.')
    } finally {
      savingRef.current = false
      if (mounted.current) setSaving(false)
    }
  }

  return <dialog ref={dialog} className="report-detail-dialog" aria-labelledby="annual-target-title"
    onCancel={event => { event.preventDefault(); if (!savingRef.current) onClose() }}>
    <form onSubmit={event => { event.preventDefault(); void save() }}>
      <div className="card-heading"><h2 id="annual-target-title">Jahresziel festlegen</h2><button autoFocus type="button" className="secondary-button" disabled={saving} onClick={onClose}>Schließen</button></div>
      <p>Jahresumsatzziele je Mitarbeiter. Die Summe ist das Jahresziel im Cockpit. Speicherung nur in der Salesplattform, ohne Übertragung nach Zoho.</p>
      {error && <p role="alert" className="message error-message">{error}</p>}
      {!plan && !error && <p role="status">Zielplanung wird geladen …</p>}
      {plan && <>
        <p>Geschäftsjahr: {new Date(plan.startsAt + 'T00:00:00').toLocaleDateString('de-DE')} – {new Date(plan.endsAt + 'T00:00:00').toLocaleDateString('de-DE')}</p>
        <p className="muted">Alle Beträge in Euro. Leer = kein Ziel; 0 = ausdrücklich kein geplanter Umsatz.</p>
        {plan.entries.length ? <div className="table-wrap"><table><thead><tr><th>Mitarbeiter</th><th>Jahresziel in EUR</th></tr></thead><tbody>
          {plan.entries.map(entry => <tr key={entry.ownerId}><td><label htmlFor={`target-${entry.ownerId}`}>{entry.name}</label></td><td>
            <input id={`target-${entry.ownerId}`} type="number" min="0" max="9999999999999999" step="0.01" disabled={saving}
              value={amounts[entry.ownerId] ?? ''} onChange={event => setAmounts(current => ({ ...current, [entry.ownerId]: event.target.value }))} />
          </td></tr>)}
        </tbody></table></div> : <p>Noch keine Mitarbeiter vorhanden. Bitte zuerst die CRM-Mitarbeiter importieren.</p>}
        <p><strong>Gesamtjahresziel: {euro(total)}</strong></p>
        <button type="submit" className="primary-button" disabled={saving || !plan.entries.length}>{saving ? 'Wird gespeichert …' : 'Jahresziele speichern'}</button>
      </>}
    </form>
  </dialog>
}
