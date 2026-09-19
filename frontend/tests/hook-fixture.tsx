import { WebhookOverview, type HookOverviewData } from '../src/WebhookOverview'

declare global { interface Window { hookFixture: { fail: boolean; requests: string[]; errors: number } } }
window.hookFixture = { fail: false, requests: [], errors: 0 }
const time = '2026-09-19T10:00:00Z'
const events = Array.from({ length: 27 }, (_, index) => ({
  id: `synthetic-event-${index}`, module: index === 26 ? 'Deals' : 'Leads', operation: 'edit',
  status: index === 0 ? 'failed' : 'queued', attemptCount: index === 0 ? 5 : 0,
  receivedAt: time, processedAt: null,
  error: index === 0 ? 'INVALID_DATA: Zoho hat übermittelte Daten abgelehnt.' : null,
}))
async function fetchHooks(url: string): Promise<Response> {
  window.hookFixture.requests.push(url)
  if (window.hookFixture.fail) return new Response('not shown', { status: 500 })
  const query = new URL(url, location.origin).searchParams
  const module = query.get('module'), status = query.get('status')
  const page = Number(query.get('page') ?? 1), pageSize = 25
  const byModule = events.filter(x => !module || x.module === module)
  const filtered = byModule.filter(x => !status || x.status === status)
  const data: HookOverviewData = {
    observedAt: time, callbackBaseUrl: 'https://sales.example.test/api/integrations/zoho/webhook', schemaCached: true,
    callbackUrlSource: 'tenantApp', callbackUrlError: null,
    subscriptions: ['Leads', 'Deals'].map(module => ({ module, status: module === 'Leads' ? 'active' : 'missing',
      expiresAt: module === 'Leads' ? time : null, lastCheckedAt: time, lastRenewedAt: null, error: null })),
    counts: { queued: byModule.filter(x => x.status === 'queued').length, failed: byModule.filter(x => x.status === 'failed').length },
    total: filtered.length, page, pageSize, events: filtered.slice((page - 1) * pageSize, page * pageSize),
  }
  return Response.json(data)
}
const onError = () => { window.hookFixture.errors++ }
export function HookFixture() {
  return <WebhookOverview authorizedFetch={fetchHooks} jobsUrl="/jobs" onError={onError} />
}
