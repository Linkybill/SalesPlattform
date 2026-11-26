import { useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import type { TaskType } from './TaskGoalProfilesPage'
import './teams.css'

type Task = {
  id: string
  taskType: string
  title: string
  industry: string | null
  assignedOn: string
  crmUrl: string | null
  crmTaskUrl: string | null
  crmStatus: string | null
  crmCompletedAt: string | null
}

export function MySalesTasksPage() {
  const { authorizedFetch, activeTenantId, user } = useApplicationContext()
  const [tasks, setTasks] = useState<Task[] | null>(null)
  const [types, setTypes] = useState<TaskType[]>([])
  const [error, setError] = useState(false)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    if (!activeTenantId || !user) return
    const update = () => {
      if (document.visibilityState === 'visible') setRefresh(value => value + 1)
    }
    const interval = window.setInterval(update, 60_000)
    window.addEventListener('focus', update)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('focus', update)
    }
  }, [activeTenantId, user])

  useEffect(() => {
    if (!activeTenantId || !user) return
    const controller = new AbortController()
    setError(false)
    void (async () => {
      try {
        const [tasksResponse, typesResponse] = await Promise.all([
          authorizedFetch('/api/sales-tasks/mine', { signal: controller.signal }),
          authorizedFetch('/api/task-goal-profiles/types', { signal: controller.signal }),
        ])
        if (!tasksResponse.ok || !typesResponse.ok) throw new Error()
        const [loaded, catalog] = await Promise.all([tasksResponse.json(), typesResponse.json()]) as [Task[], TaskType[]]
        if (!controller.signal.aborted) { setTasks(loaded); setTypes(catalog) }
      } catch {
        if (!controller.signal.aborted) setError(true)
      }
    })()
    return () => controller.abort()
  }, [activeTenantId, user, authorizedFetch, refresh])

  return <main className="sales-page teams-page">
    <section className="sales-panel">
      <div className="teams-heading">
        <h1>Meine Aufgaben</h1>
        {activeTenantId && user && <button type="button" className="secondary-button" onClick={() => setRefresh(value => value + 1)}>Aktualisieren</button>}
      </div>
      {!activeTenantId || !user ? <p>Bitte anmelden und einen Mandanten auswählen.</p>
        : error ? <p className="teams-error" role="alert">Deine Aufgaben konnten nicht geladen werden.</p>
        : tasks === null ? <p role="status">Aufgaben werden geladen …</p>
        : tasks.length === 0 ? <p>Keine offenen oder heute in Zoho erledigten Aufgaben.</p>
        : <div className="teams-table-scroll"><table className="teams-table">
          <thead><tr><th>Aufgabe</th><th>Aufgabentyp</th><th>Branche</th><th>Zugewiesen am</th><th>Zoho-Status</th><th>Aktion</th></tr></thead>
          <tbody>{tasks.map(task => <tr key={task.id}>
            <th scope="row">{task.title}</th>
            <td>{types.find(type => type.key === task.taskType)?.name ?? task.taskType}</td>
            <td>{task.industry || '–'}</td>
            <td><time dateTime={task.assignedOn}>{task.assignedOn.split('-').reverse().join('.')}</time></td>
            <td>{task.crmCompletedAt ? 'Erledigt' : task.crmStatus || '–'}</td>
            <td>{task.crmTaskUrl ? <a href={task.crmTaskUrl} target="_blank" rel="noreferrer">CRM-Aufgabe öffnen</a>
              : task.crmUrl ? <a href={task.crmUrl} target="_blank" rel="noreferrer">CRM-Datensatz öffnen</a>
                : '–'}</td>
          </tr>)}</tbody>
        </table></div>}
    </section>
  </main>
}
