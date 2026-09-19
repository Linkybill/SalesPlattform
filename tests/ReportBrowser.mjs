// Native Windows Chrome smoke test; no platform, credentials or CRM requests.
import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
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
const server = await createServer({ root: frontend, configFile: path.join(frontend, 'vite.config.ts'), resolve: { alias: [{ find: /^@hammer2fall\/identity-platform-react$/, replacement: path.join(frontend, 'tests/platform-mock.ts') }] }, server: { host: '127.0.0.1', port: 0 } })
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
    assert.ok(await evaluate(`(() => { const b = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === ${JSON.stringify(text)}); if (!b) return false; b.click(); return true })()`), `Button not found: ${text}`)
  }
  await send('Runtime.enable')
  await send('Page.enable')
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false })
  const address = server.httpServer.address()
  await send('Page.navigate', { url: `http://127.0.0.1:${address.port}/tests/report-smoke.html` })
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 25`), 'Worklist did not render.')
  assert.equal(await evaluate(`document.querySelector('.worklist-score')`), null)
  await click('Weiter')
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 5`), 'Worklist pagination failed.')
  // Theme buttons include their count; use their label span.
  await evaluate(`document.querySelectorAll('.worklist-rule-button')[0].click()`)
  await eventually(() => evaluate(`document.querySelectorAll('.worklist-item').length === 1`), 'Renewal theme did not filter.')
  await click('Meeting Report')
  await eventually(() => evaluate(`!!document.querySelector('.meeting-report-embedded .evidence-table')`), 'Meeting Report missing under Arbeit.')
  await click('Test Steuerung')
  await eventually(() => evaluate(`!!document.querySelector('.metric-open')`), 'Steuerung did not render.')
  assert.ok(!await evaluate(`[...document.querySelectorAll('.sales-section-nav button')].some(b => b.textContent === 'Meeting Report')`))
  await evaluate(`document.querySelector('.metric-open').focus(); document.querySelector('.metric-open').click()`)
  await eventually(() => evaluate(`document.querySelector('dialog').open`), 'KPI modal failed to open.')
  assert.equal(await evaluate(`document.querySelectorAll('dialog tbody tr').length`), 25)
  await evaluate(`[...document.querySelectorAll('dialog button')].find(b => b.textContent === 'Weiter').click()`)
  await eventually(() => evaluate(`document.querySelectorAll('dialog tbody tr').length === 2`), 'Modal pagination failed.')
  await evaluate(`(() => { const input = document.querySelector('dialog input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Testdeal 27'); input.dispatchEvent(new Event('input', { bubbles: true })) })()`)
  await eventually(() => evaluate(`document.querySelectorAll('dialog tbody tr').length === 1`), 'Modal search failed.')
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 })
  await eventually(() => evaluate(`!document.querySelector('dialog').open`), 'Escape did not close modal.')
  assert.ok(await evaluate(`document.activeElement.classList.contains('metric-open')`), 'Modal must restore focus to KPI.')
  await evaluate(`document.querySelector('.chart-detail-button').click()`)
  await eventually(() => evaluate(`document.querySelector('dialog').open`), 'Chart did not open details.')
  await click('Schließen ×')
  await click('Monatsreport')
  await eventually(() => evaluate(`document.querySelector('.report-toolbar select').value === 'month'`), 'Month report did not select month.')
  await click('Jahresreport')
  await eventually(() => evaluate(`document.querySelector('.report-toolbar select').value === 'year'`), 'Year report did not select year.')
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true })
  await sleep(200)
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Report layout overflows mobile viewport.')
  await click('Test Arbeit')
  await eventually(() => evaluate(`!!document.querySelector('.worklist-item')`), 'Worklist did not remount.')
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Worklist layout overflows mobile viewport.')
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
