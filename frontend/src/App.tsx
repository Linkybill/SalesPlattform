import { useEffect, useState } from 'react'
import { DashboardPage } from './DashboardPage'
import { DashboardLayoutPage } from './DashboardLayoutPage'
import { ImportPage } from './ImportPage'
import { ReportsPage } from './ReportsPage'
import { UsagePage } from './UsagePage'
import { resolveSalesRoute } from './salesRoutes'

function App() {
  const [pathname, setPathname] = useState(() => window.location.pathname)

  useEffect(() => {
    const handleLocationChange = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', handleLocationChange)
    return () => window.removeEventListener('popstate', handleLocationChange)
  }, [])

  const route = resolveSalesRoute(pathname)
  if (route === 'dashboard-layout') return <DashboardLayoutPage />
  if (route === 'import') return <ImportPage />
  if (route === 'usage') return <UsagePage />
  if (route === 'worklist') return <DashboardPage />
  return <ReportsPage />
}

export default App
