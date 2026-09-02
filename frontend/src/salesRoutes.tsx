import type { UserAuthorisationRoute } from '@hammer2fall/identity-platform-react'
import { tenantApplicationPath } from './identityPlatformConfig'

const applicationRouteBase = tenantApplicationPath.routerBasePath.replace(/\/+$/, '') || '/'

export const salesRoutes: readonly UserAuthorisationRoute[] = [
  {
    id: 'dashboard',
    route: applicationRouteBase,
    title: 'Dashboard',
    icon: <HomeIcon />,
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
    id: 'owner-mapping',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/settings`,
    title: 'CRM-Zuordnung',
    icon: <SettingsIcon />,
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

export type SalesRouteId = 'dashboard' | 'worklist' | 'import' | 'owner-mapping' | 'dashboard-layout'

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
  const ownerMappingRoute = salesRoutes.find(route => route.id === 'owner-mapping')
  if (ownerMappingRoute) {
    const ownerMappingPath = normalizePathname(new URL(ownerMappingRoute.route, window.location.origin).pathname)
    if (currentPath === ownerMappingPath || currentPath.startsWith(`${ownerMappingPath}/`)) return 'owner-mapping'
  }
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

function HomeIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="m3 10 9-7 9 7" />
      <path d="M5 9.5V21h14V9.5M9 21v-6h6v6" />
    </svg>
  )
}

function ImportIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 3v12m0 0 4-4m-4 4-4-4" />
      <path d="M5 15v5h14v-5" />
    </svg>
  )
}

function SettingsIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 8.2a3.8 3.8 0 1 0 0 7.6 3.8 3.8 0 0 0 0-7.6Z" />
      <path d="m19.4 15 .1.1a1.8 1.8 0 0 1-2.5 2.5l-.1-.1a1.8 1.8 0 0 0-3 .6v.2a1.8 1.8 0 0 1-3.6 0v-.2a1.8 1.8 0 0 0-3-.6l-.1.1a1.8 1.8 0 1 1-2.5-2.5l.1-.1a1.8 1.8 0 0 0-.6-3h-.2a1.8 1.8 0 0 1 0-3.6h.2a1.8 1.8 0 0 0 .6-3l-.1-.1a1.8 1.8 0 1 1 2.5-2.5l.1.1a1.8 1.8 0 0 0 3-.6v-.2a1.8 1.8 0 0 1 3.6 0v.2a1.8 1.8 0 0 0 3 .6l.1-.1a1.8 1.8 0 1 1 2.5 2.5l-.1.1a1.8 1.8 0 0 0 .6 3h.2a1.8 1.8 0 0 1 0 3.6h-.2a1.8 1.8 0 0 0-.6 3Z" />
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
