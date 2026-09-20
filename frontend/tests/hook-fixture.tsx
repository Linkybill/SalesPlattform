import { WebhookOverview, type HookOverviewData } from '../src/WebhookOverview'

declare global { interface Window { hookFixture: { fail: boolean; requests: string[]; errors: number; checkMode: string; checkedModules: string[]; jobRequests: string[]; jobMode: string } } }
window.hookFixture = { fail: false, requests: [], errors: 0, checkMode: 'verified', checkedModules: [], jobRequests: [], jobMode: 'accepted' }
const time = '2026-09-19T10:00:00Z'
const events = Array.from({ length: 27 }, (_, index) => ({
  id: `synthetic-event-${index}`, module: index === 26 ? 'Deals' : 'Leads', operation: 'edit',
  status: index === 0 ? 'failed' : 'queued', attemptCount: index === 0 ? 5 : 0,
  receivedAt: time, processedAt: null,
  error: index === 0 ? 'INVALID_DATA: Zoho hat übermittelte Daten abgelehnt.' : null,
}))
async function fetchHooks(url: string, init?: RequestInit): Promise<Response> {
  window.hookFixture.requests.push(url)
  if (url.endsWith('/hooks/check')) {
    if (init?.method !== 'POST') throw new Error('Live check must be explicit POST')
    const module = (JSON.parse(String(init.body)) as { module: string }).module
    window.hookFixture.checkedModules.push(module)
    if (window.hookFixture.checkMode === 'forbidden') return new Response('private error must not appear', { status: 403 })
    return Response.json({ checkedAt: time, module, status: window.hookFixture.checkMode,
      message: window.hookFixture.checkMode === 'scope-missing'
        ? 'ZohoCRM.notifications.READ fehlt. Erneut Zoho verbinden.'
        : 'Registrierung direkt bei Zoho bestätigt. Kein Nachweis einer erfolgreichen Callback-Zustellung.',
      expectedUrl: 'https://sales.example.test/api/integrations/zoho/webhook?tenant_id=12345678-1234-4123-8123-123456789abc',
      registeredUrl: 'https://sales.example.test/api/integrations/zoho/webhook?tenant_id=12345678-1234-4123-8123-123456789abc',
      expiresAt: time, events: [`${module}.create`, `${module}.edit`, `${module}.delete`],
      filters: { status: window.hookFixture.checkMode === 'filters-present' ? 'present' : 'none',
        message: window.hookFixture.checkMode === 'filters-present' ? 'Feldfilter gesetzt.' : 'Keine Feldfilter bei Zoho gesetzt.',
        conditions: window.hookFixture.checkMode === 'filters-present' ? [`${module}: (Call_Duration UND Subject)`] : [] },
      tokenCheck: { status: window.hookFixture.checkMode === 'token-not-verified' ? 'mismatch' : 'match',
        message: window.hookFixture.checkMode === 'token-not-verified' ? 'Verification-Token weicht ab.' : 'Verification-Token stimmt überein.' },
      localChannelCheck: { status: window.hookFixture.checkMode === 'local-not-ready' ? 'expired' : 'ready',
        message: window.hookFixture.checkMode === 'local-not-ready' ? 'Lokaler Channel ist abgelaufen.' : 'Lokaler Channel stimmt überein, ist aktiv und gültig.' },
      localExpiresAt: time, notifyOnRelatedAction: false,
    })
  }
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
async function startHookJob(url: string, init?: RequestInit): Promise<Response> {
  if (init?.method !== 'POST' || init.body !== '{}') throw new Error('Expected explicit platform job POST')
  window.hookFixture.jobRequests.push(url)
  if (window.hookFixture.jobMode === 'lost') throw new Error('private provider failure must not appear')
  if (window.hookFixture.jobMode === 'conflict') return new Response('private error', { status: 409 })
  if (window.hookFixture.jobMode === 'forbidden') return new Response('private error', { status: 403 })
  return Response.json({ id: '33333333-3333-4333-8333-333333333333', status: 'queued' }, { status: 202 })
}
export function HookFixture() {
  return <WebhookOverview authorizedFetch={fetchHooks} platformAuthorizedFetch={startHookJob} applicationKey="sales-plattform"
    tenantId="12345678-1234-4123-8123-123456789abc" jobsUrl="/jobs" onError={onError} />
}
