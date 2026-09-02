import { ReportsPage } from './ReportsPage'

// Existing bookmarks keep working; the editor itself lives on the report page.
export function DashboardLayoutPage() {
  return <ReportsPage forceEdit />
}
