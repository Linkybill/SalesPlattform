import { useCallback, useEffect, useRef, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { IndustrySelection } from './IndustrySelection'
import { SalesTaskPlansPanel } from './SalesTaskPlansPanel'
import './teams.css'
import './taskGoalProfiles.css'

export type TaskType = { key: string; name: string }
export type Goal = { taskType: string; dailyTarget: number }
export type Profile = { id: string; name: string; revision: string; items: Goal[]; industries: string[] }
type Draft = Omit<Profile, 'id' | 'revision'> & { id?: string; revision?: string }

export function TaskGoalProfilesPage() {
  const { authorizedFetch, activeTenant, activeTenantId, user } = useApplicationContext()
  const canManage = activeTenant?.isTenantAdmin === true
  const [profiles, setProfiles] = useState<Profile[]>([])
  const [types, setTypes] = useState<TaskType[]>([])
  const [industries, setIndustries] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [filter, setFilter] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState('')
  const request = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    request.current?.abort()
    const controller = new AbortController()
    request.current = controller
    setLoading(true)
    setError('')
    try {
      const responses = await Promise.all([
        authorizedFetch('/api/task-goal-profiles', { signal: controller.signal }),
        authorizedFetch('/api/task-goal-profiles/types', { signal: controller.signal }),
        authorizedFetch('/api/task-goal-profiles/industries', { signal: controller.signal }),
      ])
      if (responses.some(r => !r.ok)) throw new Error()
      const [list, catalog, industryOptions] = await Promise.all(responses.map(r => r.json())) as [Profile[], TaskType[], string[]]
      if (!controller.signal.aborted) { setProfiles(list); setTypes(catalog); setIndustries(industryOptions) }
    } catch {
      if (!controller.signal.aborted) setError('Die Aufgabenzielprofile konnten nicht geladen werden. Bitte erneut versuchen.')
    } finally { if (!controller.signal.aborted) setLoading(false) }
  }, [authorizedFetch])

  useEffect(() => {
    if (activeTenantId && user) void load()
    return () => request.current?.abort()
  }, [activeTenantId, user, load])

  function edit(profile?: Profile) {
    setNotice('')
    setFormError('')
    setDraft(profile ? { id: profile.id, revision: profile.revision, name: profile.name, industries: profile.industries, items: profile.items.map(({ taskType, dailyTarget }) => ({ taskType, dailyTarget })) } : {
      name: '', industries: [], items: types.map(t => ({ taskType: t.key, dailyTarget: 0 })),
    })
  }

  function changeGoal(taskType: string, dailyTarget: number) {
    setDraft(current => current && ({ ...current, items: current.items.map(item => item.taskType === taskType ? { ...item, dailyTarget } : item) }))
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!draft || saving || !canManage) return
    setSaving(true)
    setFormError('')
    try {
      const response = await authorizedFetch(draft.id ? `/api/task-goal-profiles/${draft.id}` : '/api/task-goal-profiles', {
        method: draft.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...draft, revision: draft.revision ?? null }),
      })
      if (!response.ok) {
        const payload = await response.json().catch(() => null) as { message?: string } | null
        throw new Error(payload?.message ?? 'Das Profil konnte nicht gespeichert werden.')
      }
      const saved = await response.json() as Profile
      setProfiles(current => [...current.filter(p => p.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name, 'de')))
      setDraft(null)
      setNotice(`Profil „${saved.name}“ gespeichert.`)
    } catch (reason) { setFormError(reason instanceof Error ? reason.message : 'Das Profil konnte nicht gespeichert werden.') }
    finally { setSaving(false) }
  }

  if (!activeTenantId || !user) return <main className="sales-page"><p>Bitte anmelden und einen Mandanten auswählen.</p></main>
  const visible = profiles.filter(p => p.name.toLocaleLowerCase('de').includes(filter.toLocaleLowerCase('de')))
  const typeName = (key: string) => types.find(t => t.key === key)?.name ?? key

  return <main className="sales-page teams-page goal-profiles-page">
    <section className="sales-panel">
      <div className="teams-heading"><div><p className="sales-eyebrow">VERTRIEBSORGANISATION</p><h1>Aufgabenzielprofile</h1>
        <p>Vorlagen für Tagesziele, zum Beispiel Junior oder Senior.</p></div>
        {canManage && !draft && <button type="button" className="primary-button" disabled={loading || !!error || !types.length} onClick={() => edit()}>Profil anlegen</button>}
      </div>
      {!canManage && <p>Profile werden von der Mandantenadministration gepflegt.</p>}
      {notice && <p role="status">{notice}</p>}
    </section>

    {draft && canManage && <section className="sales-panel" aria-labelledby="goal-editor-title">
      <h2 id="goal-editor-title">{draft.id ? 'Profil bearbeiten' : 'Neues Profil'}</h2>
      <form onSubmit={event => void save(event)}>
        <fieldset className="team-fields" disabled={saving}>
          <label htmlFor="goal-name">Profilname</label>
          <input id="goal-name" autoFocus required maxLength={200} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} placeholder="Zum Beispiel Junior" />
          <IndustrySelection options={industries} selected={draft.industries} onChange={value => setDraft({ ...draft, industries: value })} />
          <p>Gib für jeden Aufgabentyp die gewünschte Anzahl pro Tag ein. Mit 0 setzt du für diesen Typ kein Ziel.</p>
          <div className="teams-table-scroll"><table className="teams-table goal-editor-table">
            <thead><tr><th>Aufgabentyp</th><th>Anzahl pro Tag</th></tr></thead>
            <tbody>{draft.items.map(item => <tr key={item.taskType}>
              <th scope="row">{typeName(item.taskType)}</th>
              <td><input type="number" required step={1} min={0} max={10000}
                aria-label={`${typeName(item.taskType)}: Anzahl pro Tag`}
                value={Number.isNaN(item.dailyTarget) ? '' : item.dailyTarget}
                onChange={e => changeGoal(item.taskType, e.target.valueAsNumber)} /></td>
            </tr>)}</tbody></table></div>
        </fieldset>
        {formError && <p role="alert" className="teams-error">{formError}</p>}
        <div className="teams-actions">
          <button type="submit" className="primary-button" disabled={saving || !draft.name.trim() || !draft.items.some(i => i.dailyTarget > 0)}>{saving ? 'Wird gespeichert …' : 'Profil speichern'}</button>
          <button type="button" className="secondary-button" disabled={saving} onClick={() => setDraft(null)}>Abbrechen</button>
        </div>
      </form>
    </section>}

    <section className="sales-panel" aria-labelledby="goal-list-title">
      <div className="teams-heading"><h2 id="goal-list-title">Profilübersicht</h2>
        <button type="button" className="secondary-button" disabled={loading || !!draft} onClick={() => void load()}>Neu laden</button></div>
      <label htmlFor="goal-search">Profile suchen</label>
      <input id="goal-search" type="search" value={filter} onChange={e => setFilter(e.target.value)} />
      {loading ? <p role="status">Profile werden geladen …</p> : error ? <p role="alert" className="teams-error">{error}</p>
        : profiles.length === 0 ? <p>Noch keine Aufgabenzielprofile erfasst.</p> : visible.length === 0 ? <p>Keine Profile zur Suche gefunden.</p>
        : <div className="teams-table-scroll"><table className="teams-table"><thead><tr><th>Profil</th><th>Tagesziele</th><th>Branchen</th>{canManage && <th>Aktionen</th>}</tr></thead>
          <tbody>{visible.map(p => <tr key={p.id}><th scope="row">{p.name}</th>
            <td><ul>{p.items.map(i => <li key={i.taskType}>{typeName(i.taskType)}: <strong>{i.dailyTarget}</strong> / Tag</li>)}</ul></td>
            <td>{p.industries.length ? p.industries.join(', ') : 'Alle'}</td>
            {canManage && <td><button type="button" className="secondary-button" disabled={!!draft} aria-label={`Profil ${p.name} bearbeiten`} onClick={() => edit(p)}>Bearbeiten</button></td>}
          </tr>)}</tbody></table></div>}
    </section>
    {canManage && !loading && !error && types.length > 0 &&
      <SalesTaskPlansPanel profiles={profiles} types={types} industries={industries} />}
  </main>
}
