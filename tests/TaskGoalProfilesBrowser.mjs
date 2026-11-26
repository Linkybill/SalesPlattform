import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'
const frontend = fileURLToPath(new URL('../frontend/', import.meta.url))
const require = createRequire(path.join(frontend, 'package.json'))
const { createServer } = await import(pathToFileURL(require.resolve('vite')))
const profile = await mkdtemp(path.join(tmpdir(), 'sales-goals-test-'))
const server = await createServer({ root: frontend, configFile: path.join(frontend, 'vite.config.ts'), resolve: { alias: [
  { find: /^@hammer2fall\/identity-platform-react$/, replacement: path.join(frontend, 'tests/goals-mock.ts') },
] }, server: { host: '127.0.0.1', port: 0 } })
let chrome, socket, sequence = 0
const pending = new Map(), errors = []
const sleep = ms => new Promise(r => setTimeout(r, ms))
async function eventually(check, message) {
  for (let i=0; i<100; i++) { if (await check()) return; await sleep(100) }
  throw new Error(message)
}
try {
  await server.listen()
  chrome = spawn(process.env.REPORT_TEST_BROWSER ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    ['--headless=new','--no-first-run','--no-default-browser-check','--disable-extensions','--remote-debugging-port=0',`--user-data-dir=${profile}`,'about:blank'], { stdio: 'ignore' })
  chrome.on('error', e => errors.push(e.message))
  let port
  await eventually(async () => { try { port=(await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]; return true } catch { return false } },'Chrome did not start')
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()
  socket = new WebSocket(targets.find(t => t.type==='page').webSocketDebuggerUrl)
  await new Promise((resolve,reject) => { socket.onopen=resolve; socket.onerror=reject })
  socket.onmessage = e => {
    const m=JSON.parse(e.data)
    if (pending.has(m.id)) { const p=pending.get(m.id); pending.delete(m.id); clearTimeout(p.timer); m.error?p.reject(new Error(m.error.message)):p.resolve(m.result) }
    if(m.method==='Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text)
  }
  const send=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++sequence, timer=setTimeout(()=>{pending.delete(id);reject(new Error(method+' timed out'))},15000)
    pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}))
  })
  const evaluate=async expression=>{
    const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true})
    if(result.exceptionDetails) throw new Error(result.exceptionDetails.text)
    return result.result.value
  }
  const click=async text=>eventually(()=>evaluate(`(()=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===${JSON.stringify(text)}&&!b.disabled);if(!b)return false;b.click();return true})()`),'Button unavailable: '+text)
  const input=async (id,value)=>evaluate(`(()=>{const el=document.getElementById(${JSON.stringify(id)});Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event('input',{bubbles:true}))})()`)
  await send('Runtime.enable'); await send('Page.enable')
  const base=`http://127.0.0.1:${server.httpServer.address().port}/tests/goals-smoke.html`
  await send('Page.navigate',{url:base})
  await eventually(()=>evaluate(`document.body.textContent.includes('Noch keine Aufgabenzielprofile')`),'Empty list missing')
  await click('Profil anlegen')
  await input('goal-name','Junior')
  const setGoal=async(index,value)=>evaluate(`(()=>{const el=document.querySelectorAll('.goal-editor-table tbody input')[${index}];Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event('input',{bubbles:true}))})()`)
  assert.equal(await evaluate(`document.querySelectorAll('form input[type=number]').length`),4)
  await evaluate(`document.querySelector('form details summary').click()`)
  await evaluate(`document.querySelector('form details input[type=checkbox]').click()`)
  assert.equal(await evaluate(`document.querySelector('button[type=submit]').disabled`),true)
  await setGoal(0,'20')
  await setGoal(3,'5')
  await click('Profil speichern')
  await eventually(()=>evaluate(`document.querySelector('tbody')?.textContent.includes('Junior')`),'Created profile not listed')
  assert.match(await evaluate(`document.querySelector('tbody').textContent`), /Baugewerbe/)
  await click('Bearbeiten')
  assert.equal(await evaluate(`document.querySelectorAll('.goal-editor-table tbody input')[0].value`),'20')
  assert.equal(await evaluate(`document.querySelectorAll('.goal-editor-table tbody input')[3].value`),'5')
  await input('goal-name','Senior')
  await setGoal(0,'-1')
  assert.equal(await evaluate(`document.querySelector('form').checkValidity()`),false)
  await setGoal(0,'25')
  await click('Profil speichern')
  await eventually(()=>evaluate(`document.querySelector('tbody')?.textContent.includes('Senior')`),'Edited profile missing')
  await click('Zeitraumziel anlegen')
  await evaluate(`(()=>{const select=document.getElementById('plan-profile');select.value=select.options[1].value;select.dispatchEvent(new Event('change',{bubbles:true}))})()`)
  await click('Vorlage übernehmen')
  assert.equal(await evaluate(`document.getElementById('plan-goal-cold-call').value`),'25')
  assert.match(await evaluate(`document.querySelector('.goal-plan-fields details summary').textContent`),/Baugewerbe/)
  await input('plan-name','Tagesaktion')
  await click('Ziel speichern')
  await eventually(()=>evaluate(`[...document.querySelectorAll('tbody')].some(table=>table.textContent.includes('Tagesaktion')&&table.textContent.includes('Aktiv'))`),'Daily plan missing from history')
  await click('Zeitraum bearbeiten')
  assert.equal(await evaluate(`document.getElementById('plan-goal-cold-call').value`),'25')
  await input('plan-goal-cold-call','12')
  await click('Ziel speichern')
  await eventually(()=>evaluate(`[...document.querySelectorAll('tbody tr')].some(row=>row.textContent.includes('Tagesaktion')&&row.textContent.includes('12 / Tag'))`),'Period update missing')
  await evaluate(`window.confirm = () => true`)
  await click('Deaktivieren')
  await eventually(()=>evaluate(`[...document.querySelectorAll('tbody tr')].some(row=>row.textContent.includes('Tagesaktion')&&row.textContent.includes('Deaktiviert'))`),'Disabled period missing from history')
  await click('Ziele bearbeiten')
  await input('plan-goal-cold-call','7')
  await click('Ziel speichern')
  await eventually(()=>evaluate(`[...document.querySelectorAll('tbody')].some(table=>table.textContent.includes('employee@example.test')&&table.textContent.includes('7 / Tag'))`),'Personal target missing')
  await input('goal-search','no match')
  await eventually(()=>evaluate(`document.body.textContent.includes('Keine Profile zur Suche')`),'Search failed')
  await send('Page.navigate',{url:base+'?readonly'})
  await eventually(()=>evaluate(`document.body.textContent.includes('Mandantenadministration gepflegt')`),'Read-only view missing')
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].some(b=>b.textContent==='Profil anlegen')`),false)
  await send('Page.navigate',{url:base+'?load-error'})
  await eventually(()=>evaluate(`document.querySelector('[role=alert]')?.textContent.includes('nicht geladen')`),'Load error missing')
  await send('Page.navigate',{url:base+'?conflict'})
  await click('Profil anlegen')
  await input('goal-name','Conflict')
  await setGoal(0,'10')
  await click('Profil speichern')
  await eventually(()=>evaluate(`document.querySelector('[role=alert]')?.textContent.includes('inzwischen geändert')`),'Conflict feedback missing')
  assert.equal(await evaluate(`document.getElementById('goal-name').value`),'Conflict','Conflict must preserve draft')
  await send('Page.navigate',{url:base+'?mine'})
  await eventually(()=>evaluate(`document.querySelector('tbody')?.textContent.includes('Kaltakquise-Anruf')`),'Assigned task missing')
  assert.match(await evaluate(`document.querySelector('tbody').textContent`), /Testlead.*Baugewerbe.*26\.11\.2026/)
  assert.match(await evaluate(`document.querySelector('tbody').textContent`), /Erledigt.*CRM-Aufgabe öffnen/)
  assert.equal(await evaluate(`document.querySelector('tbody a').href`), 'https://crm.example.test/tasks/2')
  assert.deepEqual(errors,[])
  console.log('Goal profiles browser: counts, industry filters, personal and period goals, history and My Tasks passed.')
} finally {
  socket?.close(); if(chrome&&!chrome.killed) chrome.kill()
  await server.close()
  for(const p of pending.values()) clearTimeout(p.timer)
  await sleep(300)
  // Only the exact directory created by this test is removed.
  await rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:200})
}
