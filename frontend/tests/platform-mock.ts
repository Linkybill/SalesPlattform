// Synthetic fixture only. This module is aliased ONLY by tests/ReportBrowser.mjs.
const rows = Object.fromEntries(Array.from({ length: 27 }, (_, i) => [`deal:${i}`, {
  key: `deal:${i}`, kind: 'deal', name: `Testdeal ${i + 1}`, customer: `Testkunde ${i + 1}`, owner: 'Test Vertrieb', status: 'won', date: '2026-09-19T12:00:00Z', amount: 100, currency: 'EUR', detail: 'Synthetische Testdaten', externalUrl: 'https://crm.example/record',
}]))
const keys = Object.keys(rows)
const metrics = Object.fromEntries(['won', 'won-count', 'annual-target', 'attainment', 'win-rate', 'pipeline', 'coverage', 'cycle', 'recurring', 'stale', 'expiring', 'funnel:Teststufe', 'meetings:new', 'meetings:week', 'meetings:planned', 'meetings:missed', 'meetings:completion', 'meetings:no-show', 'meetings:reschedule', 'meeting-status:planned', 'meeting-type:Erstkontakt'].map(key => [key, {
  key, label: key === 'won' ? 'Gewonnener Umsatz' : key, value: key === 'won' ? 2700 : 27, unit: key === 'won' ? 'money' : 'count', currency: 'EUR', period: 'September 2026', source: 'Synthetische CRM-Daten', calculation: 'Summe aus 27 Testdatensätzen', unavailableReason: null, recordKeys: keys,
}]))

// Additional report fixtures deliberately use different record sets so a
// miswired chart/list cannot pass by displaying the same 27 deals everywhere.
for (let i = 0; i < 3; i++) {
  const key = `appointment:extra-${i}`
  rows[key] = { key, kind: 'appointment', name: `Vorbereitung ${i + 1}`, customer: 'Synthetischer Terminkunde',
    owner: 'Test Vertrieb', status: i === 2 ? 'Abgesagt' : 'Geplant', date: `2026-09-${20 + i}T09:00:00Z`,
    amount: 0, currency: 'EUR', detail: 'Typ: Erstgespräch; Branche: Testbranche', externalUrl: 'https://crm.example/meeting' }
}
const appointmentKeys = [0, 1, 2].map(i => `appointment:extra-${i}`)
function additionalMetric(key: string, label: string, recordKeys: string[], period = 'September 2026') {
  metrics[key] = { key, label, value: recordKeys.length, unit: 'count', currency: 'EUR', period,
    source: 'Synthetische CRM-Daten', calculation: 'Anzahl eindeutig zugeordneter synthetischer Datensätze',
    unavailableReason: null, recordKeys }
}
for (const [key, label, rowKeys] of [
  ['product-count:group:Testprodukt', 'Testprodukt', keys.slice(0, 2)],
  ['analysis:products-count', 'Verkaufte Produkte', keys.slice(0, 2)],
  ['meeting-first-industry:group:Testbranche', 'Testbranche', appointmentKeys.slice(0, 2)],
  ['analysis:first-meetings', 'Erstgespräche nach Branche', appointmentKeys.slice(0, 2)],
  ['meeting-follow-up-industry:group:Testbranche', 'Testbranche', appointmentKeys.slice(2)],
  ['analysis:follow-up-meetings', 'Folgetermine nach Branche', appointmentKeys.slice(2)],
  ['offer-deal-industry:group:Testbranche', 'Testbranche', keys.slice(0, 1)],
  ['analysis:offer-deals', 'Offene Angebots-Deals', keys.slice(0, 1)],
  ['offer-document-industry:group:Testbranche', 'Testbranche', keys.slice(2, 5)],
  ['analysis:offer-documents', 'Offene Angebotsbelege', keys.slice(2, 5)],
  ['meetings:unclassified', 'Termine ohne eindeutige Typzuordnung', appointmentKeys.slice(1, 2)],
  ['meetings:week-cancelled', 'Abgesagte Termine dieser Woche', appointmentKeys.slice(2)],
  ['meetings:week-rescheduled', 'Verschobene Termine dieser Woche', []],
  ['meetings:week-no-show', 'Nicht stattgefundene Termine dieser Woche', []],
  ['meetings:status-completed', 'Durchgeführte Termine', []],
  ['meetings:status-cancelled', 'Abgesagte Termine', appointmentKeys.slice(2)],
  ['meetings:status-rescheduled', 'Verschobene Termine', []],
] as [string, string, string[]][]) additionalMetric(key, label, rowKeys)
additionalMetric('meetings:preparation', 'Terminvorbereitung · nächste 5 Tage', appointmentKeys, '19.09.2026–23.09.2026 (UTC), unabhängig vom Reportzeitraum')


for (const [key, label, recordKeys] of [
  ['industry:group:Branche A', 'Branche A', keys.slice(0, 12)],
  ['industry:group:Branche B', 'Branche B', keys.slice(12)],
  ['product:group:Produkt A', 'Produkt A', keys.slice(0, 20)],
  ['product:group:Produkt B', 'Produkt B', keys.slice(20)],
] as [string, string, string[]][]) {
  additionalMetric(key, label, recordKeys)
  metrics[key].unit = 'money'
  metrics[key].value = recordKeys.length * 100
}
additionalMetric('meeting-first-industry:group:Weitere Branche', 'Weitere Branche', appointmentKeys.slice(2))
additionalMetric('analysis:first-meetings', 'Erstgespräche nach Branche', appointmentKeys)


const panels = [
  { key:'dormant:prospects', area:'dormant',title:'Interessenten · älter als 5 Monate',kind:'records',description:'Importierte Kontakte',columns:[],rows:[{label:'',metricKeys:['fixture:dormant']}] },
  { key:'calls:under-five', area:'followups',title:'Erfolglose Anrufversuche · unter 5',kind:'records',description:'Versuche seit Gespräch',columns:[],rows:[{label:'',metricKeys:['fixture:attempts']}] },
  { key:'lifetime-products', area:'lifetime',title:'Produktmix je Jahr',kind:'area',description:'Jahreswerte',columns:['Produkt A','Produkt B'],rows:[
    {label:'2025',metricKeys:['fixture:2025:a','fixture:2025:b']},{label:'2026',metricKeys:['fixture:2026:a','fixture:2026:b']}] },
  { key:'goal-progress', area:'team',title:'Zielerreichung im Jahresverlauf',kind:'progress',description:'Zeitmarke',columns:['Zielerreichung','Zeitanteil'],rows:[{label:'Test Vertrieb',metricKeys:['fixture:attainment','fixture:time']}] },
  { key:'cross-selling-matrix', area:'analysis',title:'Cross-Selling · Kunde × Produktkategorie',kind:'matrix',description:'Kategorien',columns:['Kategorie'],rows:keys.map((key,i)=>({label:'Matrixkunde '+(i+1),metricKeys:['fixture:matrix:'+i]})) },
]
additionalMetric('fixture:dormant','Alter Interessent',keys.slice(0,1))
additionalMetric('fixture:attempts','Erfolglose Versuche',keys.slice(1,3))
for(const year of [2025,2026]) for(const product of ['a','b']) {
  const key='fixture:'+year+':'+product
  additionalMetric(key,'Produkt '+product,product==='a'?keys.slice(0,2):keys.slice(2,3),'Kalenderjahr '+year)
  metrics[key].unit='money';metrics[key].value=product==='a'?200:100
}
additionalMetric('fixture:attainment','Zielerreichung',keys.slice(0,1));metrics['fixture:attainment'].unit='percent';metrics['fixture:attainment'].value=80
additionalMetric('fixture:time','Zeitanteil',[]);metrics['fixture:time'].unit='percent';metrics['fixture:time'].value=70
keys.forEach((key,i)=>additionalMetric('fixture:matrix:'+i,'Kategorie',[key]))

for(let i=0;i<3;i++) {
  additionalMetric('customer:map-'+i+':revenue','Kundenumsatz',[keys[i]])
  metrics['customer:map-'+i+':revenue'].unit='money';metrics['customer:map-'+i+':revenue'].value=100
  additionalMetric('customer:map-'+i+':open','Offene Deals',[keys[i]])
}
const reportKeys = ['cockpit', 'team', 'meetings', 'analysis', 'customers', 'goals', 'cleanup', 'service', 'commercial', 'contact-reports']
const dashboard = {
  canManageAnnualTargets: true,
  generatedAt: '2026-09-19T12:00:00Z', timeframe: 'year', periodName: 'Geschäftsjahr', evidence: { metrics, records: rows, panels },
  layout: { nodes: reportKeys.map(key => ({ id: key, type: 'report', title: key, reportKey: key, visible: true, allowed: true, columns: 12, children: [] })), availableReports: reportKeys.map(key => ({ key, title: key, allowed: true })), isDefault: true, canEdit: true },
  cockpit: { periodName: 'Testzeitraum' }, team: { periodName: 'Testzeitraum', members: [] }, meetings: { periodName: 'Testzeitraum' }, analysis: { periodName: 'Testzeitraum' }, customers: { postalAreas: {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"countryCode":"DE","postalPrefix":"10"},"geometry":{"type":"Polygon","coordinates":[[[13.3,52.4],[13.6,52.4],[13.6,52.6],[13.3,52.6],[13.3,52.4]]]}}]}, customers: Array.from({length:3},(_,i)=>({id:'map-'+i,name:'Kartenkunde '+i,ownerName:i===2?'Betreuer B':'Betreuer A',industry:i===2?'Handel':'Industrie',status:'active',products:[i===2?'B':'A'],countryCode:i===2?'FR':'DE',postalCode:i===2?'75001':'10115',city:i===2?'Paris':'Berlin',regionCode:null,addressLine1:null,houseNumber:null,latitude:i===2?48.86:52.52+i*.0001,longitude:i===2?2.35:13.405,lifetimeRevenue:100,currency:'EUR',lastContactAt:'2026-08-01T12:00:00Z',openDealCount:1,needsReview:false,externalUrl:'https://crm.example/customer'})), unmappedCount: 0 }, goals: { members: [] }, cleanup: { duplicates: [] }, service: { periodName: 'Testzeitraum' }, commercial: { periodName: 'Testzeitraum' },
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
  } : { ...dashboard, analysis: { ...dashboard.analysis, periodName: new URL(url,'http://fixture').searchParams.get('timeframe') === 'lifetime' ? 'Lifetime' : 'Testzeitraum' } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  },
}
const targetPlan = { startsAt: '2026-01-01', endsAt: '2026-12-31', revision: '1', entries: [
  { ownerId: 'test-owner', name: 'Test Vertrieb', amount: null as number | null },
] }
const logger = { log() {} }
export const useApplicationContext = () => context
export const usePlatformLog = () => logger
export const createPlatformLogOperation = () => ({ fetch: context.authorizedFetch, log() {} })
