import type { UserAuthorisationRoute } from '@hammer2fall/identity-platform-react'
import { tenantApplicationPath } from './identityPlatformConfig'

const applicationRouteBase = tenantApplicationPath.routerBasePath.replace(/\/+$/, '') || '/'
export const usageRoute = `${applicationRouteBase === '/' ? '' : applicationRouteBase}/usage`

export const salesRoutes: readonly UserAuthorisationRoute[] = [
  {
    id: 'dashboard',
    route: applicationRouteBase,
    title: 'Reports',
    icon: <LayoutIcon />,
    visibleForRoles: ['sales-user', 'sales-manager', 'sales-management', 'sales-backoffice'],
  },
  {
    id: 'worklist',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/worklist`,
    title: 'Arbeitsliste',
    icon: <WorklistIcon />,
    visibleForRoles: ['sales-user', 'sales-manager', 'sales-management', 'sales-backoffice'],
  },
  {
    id: 'import',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/import`,
    title: 'Import',
    icon: <ImportIcon />,
    visibleForRoles: ['sales-user', 'sales-manager', 'sales-management', 'sales-backoffice'],
    tenantAdminOnly: true,
  },
  {
    id: 'usage',
    route: usageRoute,
    title: 'Usage',
    icon: <UsageIcon />,
    visibleForRoles: ['sales-user', 'sales-manager', 'sales-management', 'sales-backoffice'],
    tenantAdminOnly: true,
  },
  {
    id: 'dashboard-layout',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/settings/dashboard`,
    title: 'Reportseite gestalten',
    icon: <LayoutIcon />,
    tenantAdminOnly: true,
  },
]

export type SalesRouteId = 'dashboard' | 'worklist' | 'import' | 'usage' | 'dashboard-layout'

export function resolveSalesRoute(pathname: string): SalesRouteId {
  const importRoute = salesRoutes.find(route => route.id === 'import')
  if (!importRoute) return 'dashboard'

  const currentPath = normalizePathname(pathname)
  const dashboardLayoutRoute = salesRoutes.find(route => route.id === 'dashboard-layout')
  if (dashboardLayoutRoute) {
    const dashboardLayoutPath = normalizePathname(new URL(dashboardLayoutRoute.route, window.location.origin).pathname)
    if (currentPath === dashboardLayoutPath || currentPath.startsWith(`${dashboardLayoutPath}/`)) return 'dashboard-layout'
  }
  const importPath = normalizePathname(new URL(importRoute.route, window.location.origin).pathname)
  if (currentPath === importPath || currentPath.startsWith(`${importPath}/`)) return 'import'
  const usagePath = normalizePathname(new URL(usageRoute, window.location.origin).pathname)
  if (currentPath === usagePath || currentPath.startsWith(`${usagePath}/`)) return 'usage'
  const worklistRoute = salesRoutes.find(route => route.id === 'worklist')
  if (worklistRoute) {
    const worklistPath = normalizePathname(new URL(worklistRoute.route, window.location.origin).pathname)
    if (currentPath === worklistPath || currentPath.startsWith(`${worklistPath}/`)) return 'worklist'
  }
  return 'dashboard'
}

function normalizePathname(pathname: string): string {
  return pathname.replace(/\/+$/, '') || '/'
}

function ImportIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 3v12m0 0 4-4m-4 4-4-4" />
      <path d="M5 15v5h14v-5" />
    </svg>
  )
}

function UsageIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M5 19V9m7 10V5m7 14v-7" /><path d="M3 19h18" />
    </svg>
  )
}

function WorklistIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M5 4h14v16H5zM8 8h8M8 12h8M8 16h5" />
    </svg>
  )
}

function LayoutIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><rect x="14" y="14" width="6" height="6" rx="1" />
    </svg>
  )
}
