import { useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { IndustrySelection } from './IndustrySelection'
import type { Goal, Profile, TaskType } from './TaskGoalProfilesPage'

type Employee = { id: string; email: string }
type Plan = {
  id: string; membershipId: string | null; name: string; startsOn: string | null; endsOn: string | null
  deactivatedAt: string | null; revision: string
  configuration: { goals: Goal[]; industries: string[] }
}
type PlanOverview = { employees: Employee[]; plans: Plan[] }
type PlanDraft = { memberId: string; periodId?: string; revision?: string; name: string; start: string; end: string; goals: Goal[]; industries: string[] }

function todayInBerlin() {
  const parts = new Intl.DateTimeFormat('en', { timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date())
  return ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)?.value).join('-')
}

export function SalesTaskPlansPanel({ profiles, types, industries }: { profiles: Profile[]; types: TaskType[]; industries: string[] }) {
  const { authorizedFetch } = useApplicationContext()
  const [overview, setOverview] = useState<PlanOverview | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [reload, setReload] = useState(0)
  const [draft, setDraft] = useState<PlanDraft | null>(null)
  const [chosenProfile, setChosenProfile] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const today = todayInBerlin()

  useEffect(() => {
    const controller = new AbortController()
    setLoadError(false)
    void (async () => {
      try {
        const response = await authorizedFetch('/api/sales-tasks/plans', { signal: controller.signal })
        if (!response.ok) throw new Error()
        const result = await response.json() as PlanOverview
        if (!controller.signal.aborted) setOverview(result)
      } catch {
        if (!controller.signal.aborted) setLoadError(true)
      }
    })()
    return () => controller.abort()
  }, [authorizedFetch, reload])

  function edit(memberId: string, period?: Plan) {
    const current = period ?? (memberId ? overview?.plans.find(p => p.membershipId === memberId) : undefined)
    setDraft({
      periodId: period?.id, revision: period?.revision,
      memberId, name: current?.name ?? (memberId ? 'Persönliche Ziele' : ''),
      start: period?.startsOn ?? today, end: period?.endsOn ?? today, industries: current?.configuration.industries ?? [],
      goals: current?.configuration.goals ?? types.map(type => ({ taskType: type.key, dailyTarget: 0 })),
    })
    setChosenProfile('')
    setError('')
    setNotice('')
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!draft || saving) return
    setSaving(true)
    setError('')
    try {
      const url = draft.memberId ? `/api/sales-tasks/employees/${draft.memberId}`
        : draft.periodId ? `/api/sales-tasks/periods/${draft.periodId}` : '/api/sales-tasks/periods'
      const response = await authorizedFetch(url, {
        method: draft.memberId || draft.periodId ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: draft.name, goals: draft.goals, industries: draft.industries,
          startsOn: draft.memberId ? null : draft.start, endsOn: draft.memberId ? null : draft.end, revision: draft.revision }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { message?: string } | null
        throw new Error(body?.message ?? 'Das Ziel konnte nicht gespeichert werden.')
      }
      const saved = await response.json() as Plan
      setOverview(current => current && ({ ...current, plans: [saved, ...current.plans.filter(plan => plan.id !== saved.id)] }))
      setDraft(null)
      setNotice('Ziel gespeichert.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Das Ziel konnte nicht gespeichert werden.')
    } finally {
      setSaving(false)
    }
  }

  async function deactivate(plan: Plan) {
    if (saving || !window.confirm(`Zeitraumziel „${plan.name}“ deaktivieren? Bereits zugewiesene Aufgaben bleiben bestehen.`)) return
    setSaving(true)
    setError('')
    setNotice('')
    try {
      const response = await authorizedFetch(`/api/sales-tasks/periods/${plan.id}/deactivate`, { method: 'POST' })
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { message?: string } | null
        throw new Error(body?.message ?? 'Das Zeitraumziel konnte nicht deaktiviert werden.')
      }
      const saved = await response.json() as Plan
      setOverview(current => current && ({ ...current, plans: current.plans.map(item => item.id === saved.id ? saved : item) }))
      setNotice('Zeitraumziel deaktiviert. Für neue Zuweisungen gelten wieder die persönlichen Ziele.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Das Zeitraumziel konnte nicht deaktiviert werden.')
    } finally {
      setSaving(false)
    }
  }

  const goalSummary = (plan?: Plan) => plan ? <>
    <ul>{types.map(type => <li key={type.key}>{type.name}: {plan.configuration.goals.find(g => g.taskType === type.key)?.dailyTarget ?? 0} / Tag</li>)}</ul>
    <p>Branchen: {plan.configuration.industries.join(', ') || 'Alle'}</p>
  </> : 'Noch keine Ziele festgelegt'

  if (loadError) return <section className="sales-panel">
    <p role="alert" className="teams-error">Mitarbeiter- und Zeitraumziele konnten nicht geladen werden.</p>
    <button type="button" className="secondary-button" onClick={() => setReload(value => value + 1)}>Ziele neu laden</button>
  </section>
  if (!overview) return <section className="sales-panel"><p role="status">Ziele werden geladen …</p></section>
  const periods = overview.plans.filter(p => p.membershipId === null)
    .sort((a, b) => (b.startsOn ?? '').localeCompare(a.startsOn ?? ''))

  return <>
    <section className="sales-panel">
      <div className="teams-heading">
        <h2>Ziele für einen Zeitraum</h2>
        <button type="button" className="primary-button" disabled={!!draft || saving} onClick={() => edit('')}>Zeitraumziel anlegen</button>
      </div>
      <p>Diese Tagesmengen gelten im gewählten Zeitraum für alle zum Zeitpunkt der Erfassung berechtigten Sales-Benutzer. Für ein einzelnes Tagesziel wähle dasselbe Start- und Enddatum.</p>
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert" className="teams-error">{error}</p>}
      {draft && <form className="goal-plan-fields" onSubmit={event => void save(event)}>
        <h3>{draft.memberId ? `Ziele für ${overview.employees.find(e => e.id === draft.memberId)?.email}`
          : draft.periodId ? 'Zeitraumziel bearbeiten' : 'Neues Zeitraumziel'}</h3>
        <fieldset disabled={saving} className="team-fields">
          <label htmlFor="plan-name">Name</label>
          <input id="plan-name" autoFocus required maxLength={200} value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} />
          {!draft.memberId && <>
            <label htmlFor="plan-start">Startdatum</label>
            <input id="plan-start" type="date" required min={today} readOnly={!!draft.periodId && draft.start < today} value={draft.start} onChange={event => setDraft({ ...draft, start: event.target.value,
              end: draft.end < event.target.value ? event.target.value : draft.end })} />
            <label htmlFor="plan-end">Enddatum (einschließlich)</label>
            <input id="plan-end" type="date" required min={draft.start > today ? draft.start : today} value={draft.end} onChange={event => setDraft({ ...draft, end: event.target.value })} />
          </>}
          {profiles.length > 0 && <>
            <label htmlFor="plan-profile">Profil als Vorlage wählen</label>
            <select id="plan-profile" value={chosenProfile} onChange={event => setChosenProfile(event.target.value)}>
              <option value="">Profil auswählen</option>
              {profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}
            </select>
            <button type="button" className="secondary-button" disabled={!chosenProfile} onClick={() => {
              const profile = profiles.find(item => item.id === chosenProfile)
              if (profile) setDraft({ ...draft, name: profile.name, goals: profile.items, industries: profile.industries })
            }}>Vorlage übernehmen</button>
          </>}
          <IndustrySelection options={industries} selected={draft.industries} onChange={value => setDraft({ ...draft, industries: value })} />
          {types.map(type => {
            const goal = draft.goals.find(item => item.taskType === type.key)
            return <div key={type.key}>
              <label htmlFor={`plan-goal-${type.key}`}>{type.name} pro Tag</label>
              <input id={`plan-goal-${type.key}`} type="number" min={0} max={10000} step={1} required
                value={Number.isNaN(goal?.dailyTarget) ? '' : goal?.dailyTarget ?? 0} onChange={event => setDraft({ ...draft,
                  goals: draft.goals.map(item => item.taskType === type.key ? { ...item, dailyTarget: event.target.valueAsNumber } : item) })} />
            </div>
          })}
        </fieldset>
        <div className="teams-actions">
          <button type="submit" className="primary-button" disabled={saving}>{saving ? 'Wird gespeichert …' : 'Ziel speichern'}</button>
          <button type="button" className="secondary-button" disabled={saving} onClick={() => setDraft(null)}>Abbrechen</button>
        </div>
      </form>}
      <h3>Verlauf der Zeitraumziele</h3>
      {periods.length === 0 ? <p>Noch keine Zeitraumziele erfasst.</p> : <div className="teams-table-scroll">
        <table className="teams-table">
          <thead><tr><th>Ziel</th><th>Zeitraum</th><th>Status</th><th>Vorgaben</th><th>Aktionen</th></tr></thead>
          <tbody>{periods.map(plan => <tr key={plan.id}>
            <th scope="row">{plan.name}</th><td>{plan.startsOn} bis {plan.endsOn}</td>
            <td>{plan.deactivatedAt ? 'Deaktiviert' : (plan.endsOn ?? '') < today ? 'Abgelaufen' : (plan.startsOn ?? '') > today ? 'Geplant' : 'Aktiv'}</td>
            <td>{goalSummary(plan)}</td>
            <td>{!plan.deactivatedAt && (plan.endsOn ?? '') >= today && <div className="team-fields">
              <button type="button" className="secondary-button" disabled={!!draft || saving}
                onClick={() => edit('', plan)}>Zeitraum bearbeiten</button>
              <button type="button" className="secondary-button" disabled={!!draft || saving}
                onClick={() => void deactivate(plan)}>Deaktivieren</button>
            </div>}</td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>
    <section className="sales-panel">
      <h2>Persönliche Mitarbeiterziele</h2>
      {overview.employees.length === 0 ? <p>Keine Sales-Benutzer verfügbar.</p> : <div className="teams-table-scroll">
        <table className="teams-table">
          <thead><tr><th>Mitarbeiter</th><th>Vorgaben</th><th>Aktion</th></tr></thead>
          <tbody>{overview.employees.map(employee => <tr key={employee.id}>
            <th scope="row">{employee.email}</th>
            <td>{goalSummary(overview.plans.find(plan => plan.membershipId === employee.id))}</td>
            <td><button type="button" className="secondary-button" disabled={!!draft || saving} onClick={() => edit(employee.id)}>Ziele bearbeiten</button></td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>
  </>
}
