import { useEffect, useState } from 'react'
import { DashboardPage } from './DashboardPage'
import { DashboardLayoutPage } from './DashboardLayoutPage'
import { ImportPage } from './ImportPage'
import { ReportsPage } from './ReportsPage'
import { UsagePage } from './UsagePage'
import { TeamsPage } from './TeamsPage'
import { TaskGoalProfilesPage } from './TaskGoalProfilesPage'
import { MySalesTasksPage } from './MySalesTasksPage'
import { SalesStrategyPage, SalesChatWidget, type ModelStatus } from './SalesStrategyPage'
import { resolveSalesRoute } from './salesRoutes'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

function App() {
  const { activeTenantId, user } = useApplicationContext()
  return <TenantApp key={`${activeTenantId}:${user?.email ?? user?.username ?? user?.displayName ?? 'anonymous'}:${user?.roles?.join(',')}`} />
}

function TenantApp() {
  const { authorizedFetch, activeTenantId } = useApplicationContext()
  const [pathname, setPathname] = useState(() => window.location.pathname)
  const [model, setModel] = useState<ModelStatus | null>(null)

  useEffect(() => {
    const handleLocationChange = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', handleLocationChange)
    return () => window.removeEventListener('popstate', handleLocationChange)
  }, [])

  useEffect(() => {
    if (!activeTenantId) return
    let controller: AbortController | undefined
    function loadStatus() {
      controller?.abort()
      const request = new AbortController()
      controller = request
      void authorizedFetch('/api/strategy/status', { signal: request.signal })
        .then(async response => response.ok ? await response.json() as ModelStatus : null)
        .then(status => { if (!request.signal.aborted) setModel(status) })
        .catch(() => { if (!request.signal.aborted) setModel(null) })
    }
    loadStatus()
    window.addEventListener('focus', loadStatus)
    return () => {
      controller?.abort()
      window.removeEventListener('focus', loadStatus)
    }
  }, [authorizedFetch, activeTenantId])

  const route = resolveSalesRoute(pathname)
  return <>
    {route === 'strategy' && model?.enabled ? <SalesStrategyPage /> : <SalesPage route={route} />}
    {model?.enabled && <SalesChatWidget model={model} initiallyOpen={route === 'strategy'} />}
  </>
}

function SalesPage({ route }: { route: ReturnType<typeof resolveSalesRoute> }) {
  if (route === 'dashboard-layout') return <DashboardLayoutPage />
  if (route === 'import') return <ImportPage />
  if (route === 'usage') return <UsagePage />
  if (route === 'teams') return <TeamsPage />
  if (route === 'task-goal-profiles') return <TaskGoalProfilesPage />
  if (route === 'worklist') return <DashboardPage />
  if (route === 'my-tasks' || route === 'strategy') return <MySalesTasksPage />
  return <ReportsPage />
}

export default App
