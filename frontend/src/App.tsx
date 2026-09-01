import { useEffect, useState } from 'react'
import { DashboardPage } from './DashboardPage'
import { ImportPage } from './ImportPage'
import { JobsPage } from './JobsPage'
import { resolveSalesRoute } from './salesRoutes'

function App() {
  const [pathname, setPathname] = useState(() => window.location.pathname)

  useEffect(() => {
    const handleLocationChange = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', handleLocationChange)
    return () => window.removeEventListener('popstate', handleLocationChange)
  }, [])

  const route = resolveSalesRoute(pathname)
  if (route === 'import') return <ImportPage />
  if (route === 'jobs') return <JobsPage />
  return <DashboardPage />
}

export default App
