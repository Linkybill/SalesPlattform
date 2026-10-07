import { WorklistWidget } from './WorklistWidget'
import { DashboardNavigation, navigateDashboardSection, useDashboardSection } from './DashboardNavigation'

export function DashboardPage() {
  const theme = useDashboardSection('worklist')
  return (
    <main className="sales-page reports-page sales-dashboard">
      <DashboardNavigation area="worklist" activeKey={theme} />
      <WorklistWidget selectedTheme={theme} onSelectTheme={key => navigateDashboardSection('worklist', key)} />
    </main>
  )
}
