import { useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ReportsPage } from '../src/ReportsPage'
import { DashboardPage } from '../src/DashboardPage'
import '../src/styles.css'
import '../src/reportNavigation.css'

function Fixture() {
  const [path, setPath] = useState(window.location.pathname)
  useEffect(() => {
    const update = () => setPath(window.location.pathname)
    window.addEventListener('popstate', update)
    return () => window.removeEventListener('popstate', update)
  }, [])
  return path.endsWith('/reports') ? <ReportsPage /> : <DashboardPage />
}
createRoot(document.getElementById('root')!).render(<Fixture />)
