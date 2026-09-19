import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { createRequire, Module } from 'node:module'
import path from 'node:path'

const frontend = fileURLToPath(new URL('../frontend/', import.meta.url))
const require = createRequire(path.join(frontend, 'package.json'))
const ts = require('typescript')
const React = require('react')
const { renderToStaticMarkup } = require('react-dom/server')
function load(name, overrides = {}) {
  const filename = path.join(frontend, 'src', name)
  const compiled = ts.transpileModule(readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const module = new Module(filename)
  module.filename = filename
  module.paths = Module._nodeModulePaths(frontend)
  module.require = id => Object.hasOwn(overrides, id) ? overrides[id] : require(id)
  module._compile(compiled, filename)
  return module.exports
}
const navigation = load('salesNavigation.ts')
const evidence = load('ReportEvidence.tsx')

test('four work themes cover all existing rules once; reports stay under Steuerung', () => {
  assert.deepEqual(navigation.workThemes.map(t => t.title), ['Auslaufende Produkte', 'Schlummernde Leads', 'Wiedervorlagen', 'Meeting Report'])
  const rules = navigation.workThemes.flatMap(t => t.rules).sort()
  assert.deepEqual(rules, Array.from({ length: 18 }, (_, i) => `R-${String(i + 1).padStart(2, '0')}`))
  assert.equal(navigation.themeForRule('R-06'), 'renewals')
  assert.equal(navigation.themeForRule(null), 'other')
  assert.equal(navigation.themeForRule('future-rule'), 'other')
  assert.ok(!navigation.reportSections.some(s => s.reports.includes('meetings')))
  assert.equal(navigation.reportSections.find(s => s.key === 'month').timeframe, 'month')
  assert.equal(navigation.reportSections.find(s => s.key === 'year').timeframe, 'year')
  assert.equal(navigation.reportSections.find(s => s.key === 'lifetime').timeframe, 'lifetime')
})

test('top-level routes default to Arbeit, then Steuerung; tenant root and old worklist links work', () => {
  globalThis.window = { location: { origin: 'https://sales.example' } }
  for (const root of ['/', '/tenant-id']) {
    const routes = load('salesRoutes.tsx', { './identityPlatformConfig': { tenantApplicationPath: { routerBasePath: root } } })
    assert.deepEqual(routes.salesRoutes.slice(0, 2).map(r => r.title), ['Arbeit', 'Steuerung'])
    assert.equal(routes.resolveSalesRoute(root), 'worklist')
    const base = root === '/' ? '' : root
    assert.equal(routes.canonicalSalesEntryPath(root), `${base}/worklist`)
    assert.equal(routes.canonicalSalesEntryPath(`${base}/auth/callback`), `${base}/auth/callback`)
    assert.equal(routes.canonicalSalesEntryPath(`${base}/reports`), `${base}/reports`)
    for (const [route, expected] of [['worklist', 'worklist'], ['reports', 'dashboard'], ['settings/dashboard', 'dashboard-layout'], ['import', 'import'], ['usage', 'usage']]) {
      assert.equal(routes.resolveSalesRoute(`${base}/${route}/`), expected)
    }
  }
})

test('report selection preserves layout/text, permissions and original saved tree', () => {
  const node = (id, type, props = {}) => ({ id, type, title: id, text: null, reportKey: null, columns: 6, gridColumns: 12, visible: true, allowed: true, children: [], ...props })
  const tree = [node('heading', 'heading'), node('tabs', 'tabs', { children: [node('cockpit', 'report', { reportKey: 'cockpit' }), node('meetings', 'report', { reportKey: 'meetings' })] }), node('secret', 'grid', { allowed: false, children: [node('hidden', 'report', { reportKey: 'cockpit' })] })]
  const before = JSON.stringify(tree)
  const result = navigation.filterReportLayout(tree, ['cockpit'])
  assert.equal(result.length, 2)
  assert.equal(result[0].type, 'heading')
  assert.deepEqual(result[1].children.map(n => n.id), ['cockpit'])
  assert.equal(JSON.stringify(tree), before)
  assert.deepEqual(navigation.filterReportLayout(tree, ['absent']), [])
  tree[1].visible = false
  assert.deepEqual(navigation.filterReportLayout(tree, ['cockpit']), [])
})

const metric = { key: 'won', label: 'Gewonnener Umsatz', value: 27, unit: 'money', currency: 'EUR', period: 'September 2026', source: 'CRM → synchronisierte Deals', calculation: 'Summe gewonnener Deals', unavailableReason: null, recordKeys: Array.from({ length: 27 }, (_, i) => `deal:${i}`) }
const snapshot = { metrics: { won: metric }, records: Object.fromEntries(metric.recordKeys.map((key, i) => [key, { key, kind: 'deal', name: `Datensatz-${String(i).padStart(2, '0')}`, customer: null, owner: 'Test', status: 'won', date: null, amount: 1, currency: 'EUR', detail: '<script>unsafe</script>', externalUrl: i === 0 ? 'javascript:alert(1)' : 'https://crm.example/record' }])) }
const render = child => renderToStaticMarkup(React.createElement(evidence.ReportEvidenceProvider, { evidence: snapshot, generatedAt: '2026-09-19T12:00:00Z' }, child))
test('KPI uses accessible button and exposes period, source and calculation', () => {
  const html = render(React.createElement(evidence.MetricTile, { metricKey: 'won' }))
  assert.match(html, /<button/)
  assert.match(html, /aria-label="Gewonnener Umsatz: 27 EUR. Datensätze anzeigen"/)
  assert.match(html, /September 2026/)
  assert.match(html, /Quelle &amp; Berechnung/)
  assert.match(html, /Summe gewonnener Deals/)
  assert.match(html, /<dialog[^>]*aria-labelledby="report-detail-title"/)
  assert.equal(evidence.metricValue({ ...metric, value: null }), 'Nicht berechenbar')
  assert.equal(evidence.metricValue({ ...metric, value: 0 }), '0 EUR')
})
test('details paginate all records, escape text and sanitize CRM links', () => {
  const html = render(React.createElement(evidence.EvidenceTable, { metricKey: 'won' }))
  assert.match(html, /27 von 27 Datensätzen · Seite 1 von 2/)
  assert.match(html, /Datensatz-24/)
  assert.doesNotMatch(html, /Datensatz-25|javascript:|<script>/)
  assert.match(html, /&lt;script&gt;unsafe/)
  assert.match(html, /type="search"/)
  assert.match(html, /Weiter/)
  assert.equal(evidence.safeCrmUrl('/relative'), null)
  assert.equal(evidence.safeCrmUrl('data:text/html,test'), null)
})
test('chart bars are keyboard-activatable drilldown buttons and support owner labels', () => {
  const html = render(React.createElement(evidence.EvidenceChart, { prefix: 'wo', suffix: 'n', labels: { won: 'Vertrieb' } }))
  assert.match(html, /<button[^>]*class="chart-detail-button"/)
  assert.match(html, /aria-label="Vertrieb: 27 EUR. Datensätze anzeigen"/)
  assert.match(html, /width:100%/)
})
