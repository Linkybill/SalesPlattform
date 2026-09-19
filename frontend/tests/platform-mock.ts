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
  generatedAt: '2026-09-19T12:00:00Z', timeframe: 'year', periodName: 'Geschäftsjahr', evidence: { metrics, records: rows },
  layout: { nodes: reportKeys.map(key => ({ id: key, type: 'report', title: key, reportKey: key, visible: true, allowed: true, columns: 12, children: [] })), availableReports: reportKeys.map(key => ({ key, title: key, allowed: true })), isDefault: true, canEdit: true },
  cockpit: { periodName: 'Testzeitraum' }, team: { periodName: 'Testzeitraum', members: [] }, meetings: { periodName: 'Testzeitraum' }, analysis: { periodName: 'Testzeitraum' }, customers: { customers: [], unmappedCount: 0 }, goals: { members: [] }, cleanup: { duplicates: [] }, service: { periodName: 'Testzeitraum' }, commercial: { periodName: 'Testzeitraum' },
}
const items = Array.from({ length: 30 }, (_, i) => ({ id: String(i), title: `Vorgang ${i + 1}`, reason: 'Testfall', priorityBand: 'high', priorityScore: 88, sourceRuleCode: i === 29 ? 'R-06' : 'R-07', workItemTypeName: 'Reaktivierung', ownerName: 'Test Vertrieb', dueAt: null, crmTaskUrl: null, externalUrl: null }))
const context = {
  activeTenantId: 'synthetic', user: { displayName: 'Synthetic', roles: ['sales-user'] }, error: null,
  authorizedFetch: async (url: string) => new Response(JSON.stringify(url.startsWith('/api/worklist') ? {
    generatedAt: '2026-09-19', teamView: true, ownerMatched: true, items,
    rules: Array.from({ length: 18 }, (_, i) => ({ code: `R-${String(i + 1).padStart(2, '0')}`, name: 'Testregel', itemCount: 0 })),
  } : dashboard), { status: 200, headers: { 'Content-Type': 'application/json' } }),
}
const logger = { log() {} }
export const useApplicationContext = () => context
export const usePlatformLog = () => logger
export const createPlatformLogOperation = () => ({ fetch: context.authorizedFetch, log() {} })
