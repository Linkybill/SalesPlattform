// Native Windows Chrome smoke test; no platform, credentials or CRM requests.
import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'
const frontend = fileURLToPath(new URL('../frontend/', import.meta.url))
const require = createRequire(path.join(frontend, 'package.json'))
const { createServer } = await import(pathToFileURL(require.resolve('vite')))
const chromePath = process.env.REPORT_TEST_BROWSER ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
assert.ok(existsSync(chromePath), 'Chrome required; set REPORT_TEST_BROWSER to a Chromium executable.')
const profile = await mkdtemp(path.join(tmpdir(), 'sales-report-smoke-'))
const server = await createServer({ root: frontend, configFile: path.join(frontend, 'vite.config.ts'), resolve: { alias: [
  { find: /^@hammer2fall\/identity-platform-react$/, replacement: path.join(frontend, 'tests/platform-mock.ts') },
  { find: './identityPlatformConfig', replacement: path.join(frontend, 'tests/navigation-config.ts') },
] }, plugins: [{ name: 'report-fixture-routes', configureServer(server) {
  server.middlewares.use((req, _res, next) => {
    if (/^\/synthetic\/(worklist|reports)(\?|$)/.test(req.url ?? '')) req.url = '/tests/report-smoke.html'
    next()
  })
} }], server: { host: '127.0.0.1', port: 0 } })
let chrome, socket
const pending = new Map()
const errors = []
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))
let sequence = 0
async function eventually(check, message) {
  for (let i = 0; i < 100; i++) { if (await check()) return; await sleep(100) }
  throw new Error(message)
}
try {
  await server.listen()
  chrome = spawn(chromePath, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' })
  chrome.on('error', error => errors.push(error.message))
  let port
  await eventually(async () => { try { port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; return true } catch { return false } }, 'Chrome did not expose its test debugging port.')
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()
  socket = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl)
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject })
  socket.onmessage = event => {
    const message = JSON.parse(event.data)
    if (message.id && pending.has(message.id)) {
      const { resolve, reject, timer } = pending.get(message.id); pending.delete(message.id); clearTimeout(timer)
      message.error ? reject(new Error(message.error.message)) : resolve(message.result)
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text)
  }
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)) }, 15000)
    pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }))
  })
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text)
    return result.result.value
  }
  const click = async text => {
    await eventually(() => evaluate(`(() => { const b = [...document.querySelectorAll('button, a')].find(b => b.textContent.trim() === ${JSON.stringify(text)} && !b.disabled); if (!b) return false; b.click(); return true })()`), `Navigation/action not ready: ${text}`)
  }
  await send('Runtime.enable')
  await send('Page.enable')
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false })
  const address = server.httpServer.address()
  const baseUrl = `http://127.0.0.1:${address.port}`
  await send('Page.navigate', { url: `${baseUrl}/synthetic/worklist` })
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 25`), 'Worklist did not render.')
  assert.deepEqual(await evaluate(`[...document.querySelectorAll('.dashboard-tabs-nav a')].map(a => a.textContent)`), ['Auslaufende Produkte', 'Schlummernde Leads', 'Wiedervorlagen', 'Meeting Report', 'Monatsreport', 'Vertriebsteam', 'Jahresreport', 'Allgemein - Lifetime', 'Kundenstamm'])
  assert.ok(await evaluate(`[...document.querySelectorAll('.dashboard-tabs-nav a')].every(a => a.pathname.startsWith('/synthetic/'))`), 'Tabs must preserve the tenant route.')
  assert.equal(await evaluate(`document.querySelectorAll('h1').length`), 1)
  assert.equal(await evaluate(`document.querySelector('.worklist-score')`), null)
  await click('Weiter')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 5`), 'Worklist pagination failed.')
  await click('Auslaufende Produkte')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 1`), 'Renewal theme did not filter.')
  await click('Wiedervorlagen')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-rule-group summary').length === 5`), 'Followups must group the existing lists by topic.')
  assert.equal(await evaluate(`document.querySelectorAll('.worklist-subtopic').length`), 13)
  await click('Meeting Report')
  await eventually(() => evaluate(`!!document.querySelector('.meeting-report-embedded .evidence-table')`), 'Meeting Report missing under Arbeit.')
  await click('Monatsreport')
  await eventually(() => evaluate(`!!document.querySelector('.metric-open')`), 'Steuerung did not render.')
  assert.equal(await evaluate(`document.querySelector('.report-toolbar select').value`), 'month')
  await evaluate(`history.back()`)
  await eventually(() => evaluate(`!!document.querySelector('.meeting-report-embedded .evidence-table')`), 'Browser back must restore the work tab.')
  await evaluate(`history.forward()`)
  await eventually(() => evaluate(`!!document.querySelector('.metric-open') && document.querySelector('.report-toolbar select').value === 'month'`), 'Browser forward must restore the month report.')
  assert.ok(!await evaluate(`[...document.querySelectorAll('.sales-section-nav button')].some(b => b.textContent === 'Meeting Report')`))
  await evaluate(`document.querySelector('.metric-open').focus(); document.querySelector('.metric-open').click()`)
  await eventually(() => evaluate(`document.querySelector('dialog').open`), 'KPI modal failed to open.')
  assert.equal(await evaluate(`document.querySelectorAll('dialog tbody tr').length`), 25)
  await evaluate(`[...document.querySelectorAll('dialog button')].find(b => b.textContent === 'Weiter').click()`)
  await eventually(() => evaluate(`document.querySelectorAll('dialog tbody tr').length === 2`), 'Modal pagination failed.')
  await evaluate(`(() => { const input = document.querySelector('dialog input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Testdeal 27'); input.dispatchEvent(new Event('input', { bubbles: true })) })()`)
  await eventually(() => evaluate(`document.querySelectorAll('dialog tbody tr').length === 1`), 'Modal search failed.')
  await evaluate(`window.reportDialogClosed = new Promise(resolve => document.querySelector('dialog').addEventListener('close', () => resolve(true), { once: true })); true`)
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await eventually(() => evaluate(`!document.querySelector('dialog').open`), 'Escape did not close modal.')
  await evaluate(`window.reportDialogClosed`)
  assert.ok(await evaluate(`document.activeElement.classList.contains('metric-open')`), 'Modal must restore focus to KPI.')
  await evaluate(`document.querySelector('.chart-detail-button').click()`)
  await eventually(() => evaluate(`document.querySelector('dialog').open`), 'Chart did not open details.')
  await click('Schließen ×')
  await click('Monatsreport')
  await eventually(() => evaluate(`document.querySelector('.report-toolbar select').value === 'month'`), 'Month report did not select month.')
  await click('Jahresreport')
  await eventually(() => evaluate(`document.querySelector('.report-toolbar select').value === 'year'`), 'Year report did not select year.')
  await click('Allgemein - Lifetime')
  await eventually(() => evaluate(`document.querySelector('.report-toolbar select').value === 'lifetime'`), 'Lifetime report did not select lifetime.')
  const previousDocument = await evaluate('performance.timeOrigin')
  await send('Page.reload')
  await eventually(() => evaluate(`performance.timeOrigin !== ${previousDocument} && document.querySelector('.report-toolbar select')?.value === 'lifetime' && !!document.querySelector('.metric-open')`), 'Reload must retain the report and its period.')
  await click('Cockpit')
  for (const label of ['Analyse', 'Ziele & Pace', 'Aufräumen', 'Servicefälle', 'Angebote, Aufträge & Rechnungen']) {
    await click(label)
    await eventually(() => evaluate(`document.querySelector('.dashboard-secondary-nav [aria-current="page"]')?.textContent === ${JSON.stringify(label)}`), 'Additional report must remain reachable: ' + label)
  }
  await click('Jahresreport')
  await click('Jahresziel festlegen')
  await eventually(() => evaluate(`!!document.querySelector('#target-test-owner')`), 'Annual target editor did not load.')
  await evaluate(`(() => { const input = document.querySelector('#target-test-owner'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '50000'); input.dispatchEvent(new Event('input', { bubbles: true })) })()`)
  await click('Jahresziele speichern')
  await eventually(() => evaluate(`!document.querySelector('#annual-target-title') && document.body.textContent.includes('Jahresziele wurden')`), 'Annual target save did not finish.')
  await click('Jahresziel festlegen')
  await eventually(() => evaluate(`document.querySelector('#target-test-owner')?.value === '50000'`), 'Saved annual target was not reloaded.')
  await click('Schließen')
  await click('Jahresziel festlegen')
  await eventually(() => evaluate(`!!document.querySelector('#target-test-owner')`), 'Annual target editor did not reopen.')
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await eventually(() => evaluate(`!document.querySelector('#annual-target-title')`), 'Annual target Escape did not close editor.')
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true })
  await sleep(200)
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Report layout overflows mobile viewport.')
  await click('Schlummernde Leads')
  await eventually(() => evaluate(`!!document.querySelector('.worklist-item')`), 'Worklist did not remount.')
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Worklist layout overflows mobile viewport.')
  await click('Kundenstamm')
  await eventually(() => evaluate(`document.querySelector('.dashboard-tabs-nav [aria-current="page"]')?.textContent === 'Kundenstamm'`), 'Mobile must reach the last tab.')
  assert.ok(await evaluate(`(() => { const nav = document.querySelector('.dashboard-tabs-nav').getBoundingClientRect(); const tab = document.querySelector('.dashboard-tabs-nav [aria-current="page"]').getBoundingClientRect(); return tab.left >= nav.left - 1 && tab.right <= nav.right + 1 })()`), 'Active tab must be visible in the scrollable navigation.')
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Customer report must fit mobile viewport.')
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false })
  await click('Wiedervorlagen')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-rule-group summary').length === 5`), 'Followup view did not return.')
  const screenshotPath = process.env.REPORT_TEST_SCREENSHOT ?? process.argv[2]
  if (screenshotPath) {
    const shot = await send('Page.captureScreenshot', { format: 'png' })
    await writeFile(screenshotPath, Buffer.from(shot.data, 'base64'))
  }
  assert.equal(await evaluate(`document.querySelectorAll('.worklist-item').length`), 3)
  await evaluate(`document.querySelector('.worklist-rule-group summary').click()`)
  assert.ok(await evaluate(`document.querySelector('.worklist-rule-group').open`), 'Work topic must expand to show its rule lists.')
  await evaluate(`[...document.querySelectorAll('.worklist-subtopic')].find(b => b.querySelector('span').textContent === 'Kontakt erneut versuchen').click()`)
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 1`), 'Existing rule filter must still apply within its group.')
  assert.equal(await evaluate(`document.querySelector('a.worklist-open-link').href`), 'https://crm.example/task')
  await click('Für morgen planen')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 0`), 'Existing snooze action must remove the task from the list.')
  await click('Alle in diesem Bereich')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 2`), 'Theme overview must retain the other tasks.')
  assert.deepEqual(errors, [])
  console.log('Browser smoke passed: work themes, report navigation, list/modal pagination, search, KPI/chart drilldown, Escape/focus, month/year and mobile layouts.')
} finally {
  socket?.close()
  for (const request of pending.values()) clearTimeout(request.timer)
  if (chrome) { const exited = new Promise(resolve => chrome.once('exit', resolve)); chrome.kill(); await Promise.race([exited, sleep(2000)]) }
  await server.close()
  // Only the unique browser profile created by this test is removed.
  await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }).catch(() => console.warn('Temporary browser profile retained:', profile))
}
