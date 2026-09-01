import type { UserAuthorisationRoute } from '@hammer2fall/identity-platform-react'
import { tenantApplicationPath } from './identityPlatformConfig'

const applicationRouteBase = tenantApplicationPath.routerBasePath.replace(/\/+$/, '') || '/'

export const salesRoutes: readonly UserAuthorisationRoute[] = [
  {
    id: 'dashboard',
    route: applicationRouteBase,
    title: 'Übersicht',
    icon: <HomeIcon />,
    visibleForRoles: ['sales-user'],
  },
  {
    id: 'import',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/import`,
    title: 'Import',
    icon: <ImportIcon />,
    visibleForRoles: ['sales-user'],
    tenantAdminOnly: true,
  },
  {
    id: 'jobs',
    route: `${applicationRouteBase === '/' ? '' : applicationRouteBase}/jobs`,
    title: 'Jobs',
    icon: <JobsIcon />,
    visibleForRoles: ['sales-user'],
    tenantAdminOnly: true,
  },
]

export type SalesRouteId = 'dashboard' | 'import' | 'jobs'

export function resolveSalesRoute(pathname: string): SalesRouteId {
  const importRoute = salesRoutes.find(route => route.id === 'import')
  const jobsRoute = salesRoutes.find(route => route.id === 'jobs')
  if (!importRoute || !jobsRoute) return 'dashboard'

  const currentPath = normalizePathname(pathname)
  const importPath = normalizePathname(new URL(importRoute.route, window.location.origin).pathname)
  const jobsPath = normalizePathname(new URL(jobsRoute.route, window.location.origin).pathname)
  if (currentPath === importPath || currentPath.startsWith(`${importPath}/`)) return 'import'
  if (currentPath === jobsPath || currentPath.startsWith(`${jobsPath}/`)) return 'jobs'
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

function JobsIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8">
      <rect x="4" y="4" width="16" height="16" rx="2" />
      <path d="M8 9h8M8 13h8M8 17h5" />
    </svg>
  )
}
