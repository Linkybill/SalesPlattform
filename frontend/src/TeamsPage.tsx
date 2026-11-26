import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext, usePlatformLog } from '@hammer2fall/identity-platform-react'
import './teams.css'

type TeamUser = { id: string; email: string }
type Team = { id: string; name: string; revision: string; members: TeamUser[] }
type Draft = { id?: string; revision?: string; name: string; members: TeamUser[] }

export function TeamsPage() {
  const { authorizedFetch, activeTenant, activeTenantId, user } = useApplicationContext()
  const canManage = activeTenant?.isTenantAdmin === true
  const log = usePlatformLog()
  const [teams, setTeams] = useState<Team[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState('')
  const [filter, setFilter] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [users, setUsers] = useState<TeamUser[] | null>(null)
  const [usersLoading, setUsersLoading] = useState(false)
  const [userFilter, setUserFilter] = useState('')
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const load = useCallback(async (signal: AbortSignal) => {
    setLoading(true)
    setError(null)
    try {
      const result = await authorizedFetch('/api/teams', { signal })
      if (!result.ok) throw new Error('Die Teams konnten nicht geladen werden.')
      const data = await result.json() as Team[]
      if (!signal.aborted) setTeams(data)
    } catch {
      if (!signal.aborted) {
        setError('Die Teams konnten nicht geladen werden. Bitte erneut versuchen.')
        log('Error', 'Team list request failed', { category: 'Sales.Teams' })
      }
    } finally { if (!signal.aborted) setLoading(false) }
  }, [authorizedFetch, log])

  useEffect(() => {
    if (!activeTenantId || !user) { setLoading(false); return }
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [activeTenantId, user, load])

  async function loadUsers() {
    setUsersLoading(true)
    setFormError(null)
    try {
      const result = await authorizedFetch('/api/teams/users')
      if (!result.ok) throw new Error()
      setUsers(await result.json() as TeamUser[])
    } catch {
      setFormError('Die Sales-Benutzer konnten nicht geladen werden. Bitte erneut versuchen.')
      log('Error', 'Team user directory request failed', { category: 'Sales.Teams' })
    } finally { setUsersLoading(false) }
  }

  function edit(team?: Team) {
    setDraft(team ? { id: team.id, revision: team.revision, name: team.name, members: [...team.members] } : { name: '', members: [] })
    setUsers(null)
    setUserFilter('')
    setNotice('')
    void loadUsers()
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!draft || saving || !users) return
    setSaving(true)
    setFormError(null)
    try {
      const result = await authorizedFetch(draft.id ? `/api/teams/${draft.id}` : '/api/teams', {
        method: draft.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: draft.name.trim(), memberIds: draft.members.map(m => m.id), revision: draft.revision ?? null }),
      })
      if (!result.ok) {
        const payload = await result.json().catch(() => null) as { message?: string } | null
        throw new Error(payload?.message ?? 'Das Team konnte nicht gespeichert werden.')
      }
      const saved = await result.json() as Team
      setTeams(current => [...current.filter(t => t.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name, 'de')))
      setDraft(null)
      setNotice(`Team „${saved.name}“ gespeichert.`)
    } catch (reason) {
      setFormError(reason instanceof Error ? reason.message : 'Das Team konnte nicht gespeichert werden.')
      log('Error', 'Team save request failed', { category: 'Sales.Teams' })
    } finally { setSaving(false) }
  }

  if (!activeTenantId || !user) return <main className="sales-page"><p>Bitte anmelden und einen Mandanten auswählen.</p></main>
  const visible = teams.filter(t => `${t.name} ${t.members.map(m => m.email).join(' ')}`.toLocaleLowerCase('de').includes(filter.toLocaleLowerCase('de')))
  const unavailable = draft?.members.filter(m => users && !users.some(u => u.id === m.id)) ?? []

  return <main className="sales-page teams-page">
    <section className="sales-panel">
      <div className="teams-heading">
        <div><p className="sales-eyebrow">VERTRIEBSORGANISATION</p><h1>Vertriebsteams</h1>
          <p>Teams erfassen und bestehende Sales-Benutzer als Mitglieder zuordnen.</p></div>
        {canManage && !draft && <button type="button" className="primary-button" onClick={() => edit()}>Team anlegen</button>}
      </div>
      {notice && <p role="status">{notice}</p>}
      {!canManage && <p>Teams werden von der Mandantenadministration gepflegt.</p>}
    </section>

    {draft && <section className="sales-panel" aria-labelledby="team-editor-title">
      <h2 id="team-editor-title">{draft.id ? 'Team bearbeiten' : 'Neues Team'}</h2>
      <form onSubmit={event => void save(event)}>
        <fieldset disabled={saving} className="team-fields">
          <label htmlFor="team-name">Teamname</label>
          <input id="team-name" autoFocus required maxLength={200} value={draft.name}
            onChange={e => setDraft({ ...draft, name: e.target.value })} placeholder="Zum Beispiel Vertrieb Nord" />
          <fieldset className="team-member-picker">
            <legend>Mitglieder ({draft.members.length} ausgewählt)</legend>
            <p>Wähle mindestens eine Person mit aktivem Zugriff auf Sales aus.</p>
            {usersLoading ? <p role="status">Sales-Benutzer werden geladen …</p> : users === null
              ? <button type="button" className="secondary-button" onClick={() => void loadUsers()}>Benutzer erneut laden</button>
              : <>
                <label htmlFor="team-user-search">Person suchen</label>
                <input id="team-user-search" type="search" value={userFilter} onChange={e => setUserFilter(e.target.value)} placeholder="E-Mail-Adresse" />
                {users.length === 0 && <p>Es sind keine aktiven Sales-Benutzer verfügbar. Bitte zunächst den Sales-Zugriff in der Tenantverwaltung einrichten.</p>}
                <div className="team-user-options">
                  {users.filter(u => u.email.toLowerCase().includes(userFilter.toLowerCase())).map(u => <label key={u.id} className="team-user-option">
                    <input type="checkbox" checked={draft.members.some(m => m.id === u.id)}
                      onChange={e => setDraft({ ...draft, members: e.target.checked ? [...draft.members, u] : draft.members.filter(m => m.id !== u.id) })} />
                    <span>{u.email}</span>
                  </label>)}
                  {users.length > 0 && !users.some(u => u.email.toLowerCase().includes(userFilter.toLowerCase())) && <p>Keine Person zur Suche gefunden.</p>}
                </div>
                {unavailable.map(m => <div key={m.id} className="team-unavailable"><span>{m.email} – kein aktiver Sales-Zugriff mehr</span>
                  <button type="button" className="secondary-button" onClick={() => setDraft({ ...draft, members: draft.members.filter(p => p.id !== m.id) })}>Aus Team entfernen</button></div>)}
              </>}
          </fieldset>
        </fieldset>
        {formError && <p role="alert" className="teams-error">{formError}</p>}
        <div className="teams-actions">
          <button type="submit" className="primary-button" disabled={saving || usersLoading || !users || !draft.name.trim() || !draft.members.length || unavailable.length > 0}>{saving ? 'Wird gespeichert …' : 'Team speichern'}</button>
          <button type="button" className="secondary-button" disabled={saving || usersLoading} onClick={() => setDraft(null)}>Abbrechen</button>
        </div>
      </form>
    </section>}

    <section className="sales-panel" aria-labelledby="team-list-title">
      <div className="teams-heading"><h2 id="team-list-title">Teamübersicht</h2>
        {!draft && <button type="button" className="secondary-button" disabled={loading} onClick={() => void load(new AbortController().signal)}>Neu laden</button>}</div>
      <label htmlFor="team-search">Teams oder Mitglieder suchen</label>
      <input id="team-search" type="search" value={filter} onChange={e => setFilter(e.target.value)} />
      {loading ? <p role="status">Teams werden geladen …</p> : error ? <p role="alert" className="teams-error">{error}</p>
        : teams.length === 0 ? <p>Noch keine Vertriebsteams erfasst.{canManage ? ' Lege das erste Team an.' : ''}</p>
        : visible.length === 0 ? <p>Keine Teams zur Suche gefunden.</p>
        : <div className="teams-table-scroll"><table className="teams-table"><thead><tr><th>Teamname</th><th>Mitglieder</th>{canManage && <th><span className="teams-action-label">Aktionen</span></th>}</tr></thead>
          <tbody>{visible.map(t => <tr key={t.id}><th scope="row">{t.name}</th><td><span>{t.members.length} {t.members.length === 1 ? 'Person' : 'Personen'}</span>
            <ul>{t.members.map(m => <li key={m.id}>{m.email}</li>)}</ul></td>
            {canManage && <td><button type="button" className="secondary-button" disabled={draft !== null} aria-label={`Team ${t.name} bearbeiten`} onClick={() => edit(t)}>Bearbeiten</button></td>}</tr>)}</tbody>
        </table></div>}
    </section>
  </main>
}
