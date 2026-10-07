// Synthetic fixture only. This module is aliased ONLY by tests/ReportBrowser.mjs.
const rows = Object.fromEntries(Array.from({ length: 27 }, (_, i) => [`deal:${i}`, {
  key: `deal:${i}`, kind: 'deal', name: `Testdeal ${i + 1}`, customer: `Testkunde ${i + 1}`, owner: 'Test Vertrieb', status: 'won', date: '2026-09-19T12:00:00Z', amount: 100, currency: 'EUR', detail: 'Synthetische Testdaten', externalUrl: 'https://crm.example/record',
}]))
const keys = Object.keys(rows)
const metrics = Object.fromEntries(['won', 'won-count', 'annual-target', 'attainment', 'win-rate', 'pipeline', 'coverage', 'cycle', 'recurring', 'stale', 'expiring', 'funnel:Teststufe', 'meetings:new', 'meetings:week', 'meetings:planned', 'meetings:missed', 'meetings:completion', 'meetings:no-show', 'meetings:reschedule', 'meeting-status:planned', 'meeting-type:Erstkontakt'].map(key => [key, {
  key, label: key === 'won' ? 'Gewonnener Umsatz' : key, value: key === 'won' ? 2700 : 27, unit: key === 'won' ? 'money' : 'count', currency: 'EUR', period: 'September 2026', source: 'Synthetische CRM-Daten', calculation: 'Summe aus 27 Testdatensätzen', unavailableReason: null, recordKeys: keys,
}]))
const reportKeys = ['cockpit', 'team', 'meetings', 'analysis', 'customers', 'goals', 'cleanup', 'service', 'commercial']
const dashboard = {
  canManageAnnualTargets: true,
  generatedAt: '2026-09-19T12:00:00Z', timeframe: 'year', periodName: 'Geschäftsjahr', evidence: { metrics, records: rows },
  layout: { nodes: reportKeys.map(key => ({ id: key, type: 'report', title: key, reportKey: key, visible: true, allowed: true, columns: 12, children: [] })), availableReports: reportKeys.map(key => ({ key, title: key, allowed: true })), isDefault: true, canEdit: true },
  cockpit: { periodName: 'Testzeitraum' }, team: { periodName: 'Testzeitraum', members: [] }, meetings: { periodName: 'Testzeitraum' }, analysis: { periodName: 'Testzeitraum' }, customers: { customers: [], unmappedCount: 0 }, goals: { members: [] }, cleanup: { duplicates: [] }, service: { periodName: 'Testzeitraum' }, commercial: { periodName: 'Testzeitraum' },
}
let items = Array.from({ length: 30 }, (_, i) => ({ id: String(i), title: `Vorgang ${i + 1}`, reason: 'Testfall', priorityBand: 'high', priorityScore: 88, sourceRuleCode: ({ 26: 'R-01', 27: 'R-16', 28: 'R-10', 29: 'R-06' } as Record<number, string>)[i] ?? 'R-07', workItemTypeName: 'Nachfassen', ownerName: 'Test Vertrieb', dueAt: null, crmTaskUrl: 'https://crm.example/task', externalUrl: 'https://crm.example/record' }))
const context = {
  activeTenantId: 'synthetic', user: { displayName: 'Synthetic', roles: ['sales-user'] }, error: null,
  authorizedFetch: async (url: string, init?: RequestInit) => {
    const snooze = url.match(/^\/api\/worklist\/([^/]+)\/snooze$/)
    if (snooze && init?.method === 'POST') {
      const item = items.find(item => item.id === snooze[1])
      if (!item || JSON.parse(String(init.body)).tomorrow !== true) return new Response(null, { status: 400 })
      items = items.filter(candidate => candidate.id !== item.id)
      return new Response(JSON.stringify(item), { status: 200 })
    }
    if (url === '/api/reports/annual-targets') {
      if (init?.method === 'PUT') {
        const request = JSON.parse(String(init.body))
        if (request.revision !== targetPlan.revision) return new Response(null, { status: 409 })
        targetPlan.entries = targetPlan.entries.map(entry => ({ ...entry, amount: request.entries.find((e: { ownerId: string }) => e.ownerId === entry.ownerId).amount }))
        targetPlan.revision += '1'
        metrics['annual-target'].value = targetPlan.entries.reduce((sum, e) => sum + (e.amount ?? 0), 0)
        return new Response(null, { status: 204 })
      }
      return new Response(JSON.stringify(targetPlan), { status: 200 })
    }
    return new Response(JSON.stringify(url.startsWith('/api/worklist') ? {
    generatedAt: '2026-09-19', teamView: true, ownerMatched: true, items,
    rules: Array.from({ length: 18 }, (_, i) => ({ code: `R-${String(i + 1).padStart(2, '0')}`, name: 'Testregel', itemCount: 0 })),
  } : dashboard), { status: 200, headers: { 'Content-Type': 'application/json' } })
  },
}
const targetPlan = { startsAt: '2026-01-01', endsAt: '2026-12-31', revision: '1', entries: [
  { ownerId: 'test-owner', name: 'Test Vertrieb', amount: null as number | null },
] }
const logger = { log() {} }
export const useApplicationContext = () => context
export const usePlatformLog = () => logger
export const createPlatformLogOperation = () => ({ fetch: context.authorizedFetch, log() {} })
