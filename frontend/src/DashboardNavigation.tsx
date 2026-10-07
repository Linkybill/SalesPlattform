import { useEffect, useRef, useSyncExternalStore } from 'react'
import type { MouseEvent } from 'react'
import { dashboardTabs, reportSections, workThemes } from './salesNavigation'
import { salesRoutes } from './salesRoutes'

type DashboardArea = 'worklist' | 'reports'

function subscribe(listener: () => void) {
  window.addEventListener('popstate', listener)
  return () => window.removeEventListener('popstate', listener)
}

export function useDashboardSection(area: DashboardArea) {
  const search = useSyncExternalStore(subscribe, () => window.location.search)
  const key = new URLSearchParams(search).get('section')
  const choices = area === 'worklist' ? [...workThemes, { key: 'all' }, { key: 'other' }] : reportSections
  return choices.some(choice => choice.key === key) ? key! : area === 'worklist' ? 'all' : 'cockpit'
}

export function dashboardSectionUrl(area: DashboardArea, key: string) {
  const route = salesRoutes.find(route => route.id === (area === 'worklist' ? 'worklist' : 'dashboard'))!
  const url = new URL(route.route, window.location.origin)
  url.searchParams.set('section', key)
  return url.pathname + url.search
}

export function navigateDashboardSection(area: DashboardArea, key: string) {
  const url = dashboardSectionUrl(area, key)
  if (url === window.location.pathname + window.location.search) return
  window.history.pushState(window.history.state, '', url)
  window.dispatchEvent(new PopStateEvent('popstate'))
}

function followSection(event: MouseEvent<HTMLAnchorElement>, area: DashboardArea, key: string) {
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
  event.preventDefault()
  navigateDashboardSection(area, key)
}

export function DashboardNavigation({ area, activeKey }: { area: DashboardArea; activeKey: string }) {
  const nav = useRef<HTMLElement>(null)
  useEffect(() => {
    const active = nav.current?.querySelector<HTMLElement>('[aria-current="page"]')
    if (active && nav.current) {
      const tab = active.getBoundingClientRect()
      const container = nav.current.getBoundingClientRect()
      if (tab.left < container.left) nav.current.scrollLeft -= container.left - tab.left
      else if (tab.right > container.right) nav.current.scrollLeft += tab.right - container.right
    }
  }, [area, activeKey])

  return <header className="dashboard-header">
    <div className="dashboard-heading"><h1>Vertriebsdashboard</h1><span className="dashboard-area">{area === 'worklist' ? 'Arbeit' : 'Steuerung'}</span></div>
    <nav className="dashboard-tabs-nav" aria-label="Vertriebsdashboard" ref={nav}>
      {dashboardTabs.map(tab => <a key={tab.key} href={dashboardSectionUrl(tab.area, tab.key)}
        className={`dashboard-tab${tab.area === area && tab.key === activeKey ? ' is-active' : ''}${tab.key === 'month' ? ' dashboard-tab-steering' : ''}`}
        aria-current={tab.area === area && tab.key === activeKey ? 'page' : undefined}
        onClick={event => followSection(event, tab.area, tab.key)}>{tab.title}</a>)}
    </nav>
  </header>
}
