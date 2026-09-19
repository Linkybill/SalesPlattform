// Native Windows Chromium; real shared components + Sales report components,
// synthetic data only. THEME_COMMON_SOURCE optionally tests an unpublished build.
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
const frontend = fileURLToPath(new URL('../frontend/', import.meta.url));
const require = createRequire(path.join(frontend, 'package.json'));
const { createServer } = await import(pathToFileURL(require.resolve('vite')));
const common = process.argv[2] ?? process.env.THEME_COMMON_SOURCE;
const alias = common ? [
  { find: '@hammer2fall/identity-platform-react/styles.css', replacement: path.join(common, 'src/styles.css') },
  { find: /^@hammer2fall\/identity-platform-react$/, replacement: path.join(common, 'dist/index.js') },
] : [];
const server = await createServer({ root: frontend, configFile: path.join(frontend, 'vite.config.ts'),
  resolve: { alias, dedupe: ['react', 'react-dom'] }, server: { host: '127.0.0.1', port: 0, fs: { allow: [frontend, ...(common ? [common] : [])] } } });
const chromePath = process.env.REPORT_TEST_BROWSER ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
assert.ok(existsSync(chromePath), 'Native Chrome required.');
const profile = await mkdtemp(path.join(tmpdir(), 'sales-theme-smoke-'));
const pending = new Map(), errors = [];
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let chrome, socket, sequence = 0;
async function eventually(check, message) {
  for (let i = 0; i < 100; i++) { if (await check()) return; await sleep(100); }
  throw new Error(message);
}
try {
  await server.listen();
  chrome = spawn(chromePath, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  chrome.on('error', error => errors.push(error.message));
  let port;
  await eventually(async () => { try { port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; return true; } catch { return false; } }, 'Chrome startup timed out.');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const { resolve, reject, timer } = pending.get(message.id); pending.delete(message.id); clearTimeout(timer);
      message.error ? reject(new Error(message.error.message)) : resolve(message.result);
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 15000);
    pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
    return result.result.value;
  };
  const select = async value => {
    await evaluate(`(() => { const select = document.querySelector('[aria-label="Darstellung"]'); select.value = ${JSON.stringify(value)}; select.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    await eventually(() => evaluate(`document.documentElement.dataset.identityThemePreference === ${JSON.stringify(value)}`), 'Theme selection not applied.');
  };
  const assertTheme = async (theme) => {
    await eventually(() => evaluate(`document.documentElement.dataset.identityTheme === '${theme}'`), `Expected ${theme}`);
    const light = theme === 'light';
    assert.equal(await evaluate(`getComputedStyle(document.documentElement).colorScheme`), theme);
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.sales-card')).backgroundColor`), light ? 'rgb(255, 255, 255)' : 'rgb(16, 29, 48)');
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.identity-platform-header')).color`), light ? 'rgb(22, 38, 61)' : 'rgb(232, 238, 248)');
  };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }] });
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: `http://127.0.0.1:${server.httpServer.address().port}/tests/theme-smoke.html` });
  await eventually(() => evaluate(`!!document.querySelector('.metric-open')`), 'Theme fixture did not render.');
  await assertTheme('dark');
  await select('light'); await assertTheme('light');
  await evaluate(`document.querySelector('.metric-open').click()`);
  await eventually(() => evaluate(`document.querySelector('dialog').open`), 'Report dialog did not open.');
  assert.equal(await evaluate(`getComputedStyle(document.querySelector('dialog')).backgroundColor`), 'rgb(255, 255, 255)');
  assert.equal(await evaluate(`document.querySelectorAll('dialog tbody tr').length`), 1);
  await evaluate(`document.querySelector('dialog button').click()`);
  await send('Page.reload');
  await eventually(() => evaluate(`!!document.querySelector('.metric-open')`), 'Reload failed.');
  await assertTheme('light');
  assert.equal(await evaluate(`document.querySelector('[aria-label="Darstellung"]').value`), 'light');
  await select('dark'); await assertTheme('dark');
  await select('system');
  await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] });
  await assertTheme('light');
  // Operational webhook view: synthetic authenticated API only, no CRM calls.
  await eventually(() => evaluate(`document.querySelector('[aria-label="Hook-Ereignisse"] tbody tr') !== null`), 'Hook overview did not load.');
  assert.equal(await evaluate(`document.querySelectorAll('[aria-label="Hook-Ereignisse"] tbody tr').length`), 25);
  assert.ok(await evaluate(`document.querySelector('.hook-overview').textContent.includes('Mandanten-AppSetting zoho.webhookUrl')`));
  assert.ok(await evaluate(`document.querySelector('.hook-overview').textContent.includes('https://sales.example.test/api/integrations/zoho/webhook')`));
  assert.ok(await evaluate(`document.querySelector('.hook-overview').textContent.includes('Versuchslimit erreicht')`));
  await evaluate(`document.querySelector('[aria-label="Hook-Ereignisse"] summary').click()`);
  assert.ok(await evaluate(`document.querySelector('[aria-label="Hook-Ereignisse"] details').open`));
  assert.ok(await evaluate(`document.querySelector('[aria-label="Hook-Ereignisse"] details').textContent.includes('synthetic-event-0')`));
  await evaluate(`Array.from(document.querySelectorAll('.hook-overview button')).find(x => x.textContent === 'Weiter').click()`);
  await eventually(() => evaluate(`document.querySelectorAll('[aria-label="Hook-Ereignisse"] tbody tr').length === 2`), 'Hook pagination failed.');
  const hookFilter = async (index, value) => {
    await evaluate(`(() => { const select = document.querySelectorAll('.hook-filters select')[${index}]; select.value = ${JSON.stringify(value)}; select.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  };
  await hookFilter(0, 'Deals');
  await eventually(() => evaluate(`document.querySelectorAll('[aria-label="Hook-Ereignisse"] tbody tr').length === 1`), 'Module filter failed.');
  assert.ok(await evaluate(`document.querySelector('.hook-overview').textContent.includes('Seite 1')`), 'Filter did not reset pagination.');
  await hookFilter(1, 'processed');
  await eventually(() => evaluate(`document.querySelector('.hook-overview').textContent.includes('Keine Hook-Ereignisse')`), 'Empty state missing.');
  await evaluate(`window.hookFixture.fail = true; document.querySelector('.hook-overview button').click()`);
  await eventually(() => evaluate(`document.querySelector('.hook-overview [role="alert"]') !== null`), 'Hook API error missing.');
  assert.ok(await evaluate(`document.querySelector('.hook-overview [role="alert"]').textContent.includes('HTTP 500')`));
  assert.equal(await evaluate(`window.hookFixture.errors`), 1);
  await evaluate(`window.hookFixture.fail = false; document.querySelector('.hook-overview button').click()`);
  await eventually(() => evaluate(`document.querySelector('.hook-overview').textContent.includes('Keine Hook-Ereignisse')`), 'Hook refresh recovery failed.');
  await hookFilter(1, '');
  await eventually(() => evaluate(`document.querySelectorAll('[aria-label="Hook-Ereignisse"] tbody tr').length === 1`), 'Hook filter reset failed.');
  await evaluate(`document.querySelector('.hook-overview > details summary').click()`);
  await eventually(() => evaluate(`document.querySelectorAll('.daily-usage-day').length === 30`), 'Daily chart must show 30 calendar days.');
  assert.ok(await evaluate(`document.querySelector('.daily-usage').textContent.includes('9 credits')`));
  await evaluate(`document.querySelector('.daily-usage-day').click()`);
  assert.ok(await evaluate(`document.querySelector('.daily-usage-detail').textContent.includes('3 Requests')`));
  await evaluate(`document.querySelector('.daily-usage summary').click()`);
  assert.equal(await evaluate(`document.querySelectorAll('[aria-label="Tagesverbrauchstabelle"] tbody tr').length`), 30);
  const dailySelect = async (label, value) => evaluate(`(() => { const select = document.querySelector('[aria-label="${label}"]'); select.value = ${JSON.stringify(value)}; select.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  await dailySelect('Verbrauchskennzahl', 'failedRequests');
  await eventually(() => evaluate(`document.querySelector('.daily-usage-day').getAttribute('aria-label').includes('1 Fehler')`), 'Failure metric wrong.');
  await dailySelect('Verbrauchsreihe', JSON.stringify(['another', 'default', 'requests']));
  await dailySelect('Verbrauchskennzahl', 'estimatedUnits');
  await eventually(() => evaluate(`document.querySelector('.daily-usage').textContent.includes('88 requests')`), 'Different units/provider must not be mixed.');
  await dailySelect('Tageszeitraum', '7');
  await eventually(() => evaluate(`document.querySelectorAll('.daily-usage-day').length === 7`), 'Seven-day selection failed.');
  await dailySelect('Tageszeitraum', '90');
  await eventually(() => evaluate(`document.querySelectorAll('.daily-usage-day').length === 90`), 'Ninety-day selection failed.');
  await evaluate(`window.dailyUsageFixture.fail = true; document.querySelector('.daily-usage button').click()`);
  await eventually(() => evaluate(`document.querySelector('.daily-usage [role="alert"]') !== null`), 'Daily API error missing.');
  assert.equal(await evaluate(`document.querySelectorAll('.daily-usage-day').length`), 0, 'Failed refresh must not show stale chart.');
  await evaluate(`window.dailyUsageFixture.fail = false; window.dailyUsageFixture.empty = true; document.querySelector('.daily-usage button').click()`);
  await eventually(() => evaluate(`document.querySelector('.daily-usage').textContent.includes('Keine Verbrauchsdaten')`), 'Daily empty state missing.');
  await evaluate(`window.dailyUsageFixture.empty = false; document.querySelector('.daily-usage button').click()`);
  await eventually(() => evaluate(`document.querySelectorAll('.daily-usage-day').length === 90`), 'Daily recovery failed.');
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await sleep(100);
  assert.ok(await evaluate(`document.documentElement.scrollWidth <= 392`), 'Theme header/report overflows mobile viewport.');
  assert.deepEqual(errors, []);
  console.log('Theme/hook/daily-usage browser passed: themes, persistence, KPI dialog, hooks, daily chart 7/30/90 days, unit isolation, day details/table, errors/recovery and 390px layout.');
} catch (error) {
  if (errors.length) console.error('Synthetic browser errors:', errors);
  throw error;
} finally {
  socket?.close(); for (const request of pending.values()) clearTimeout(request.timer);
  if (chrome) { const exited = new Promise(resolve => chrome.once('exit', resolve)); chrome.kill(); await Promise.race([exited, sleep(2000)]); }
  await server.close();
  await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }).catch(() => console.warn('Unique test profile retained:', profile));
}
