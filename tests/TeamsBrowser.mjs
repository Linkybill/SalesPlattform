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
const profile = await mkdtemp(path.join(tmpdir(), 'sales-teams-test-'))
const server = await createServer({ root: frontend, configFile: path.join(frontend, 'vite.config.ts'), resolve: { alias: [
  { find: /^@hammer2fall\/identity-platform-react$/, replacement: path.join(frontend, 'tests/teams-mock.ts') },
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
  const base=`http://127.0.0.1:${server.httpServer.address().port}/tests/teams-smoke.html`
  await send('Page.navigate',{url:base})
  await eventually(()=>evaluate(`document.body.textContent.includes('Noch keine Vertriebsteams')`),'Empty list missing')
  await click('Team anlegen')
  await eventually(()=>evaluate(`document.querySelectorAll('input[type=checkbox]').length===2`),'Directory missing')
  assert.equal(await evaluate(`document.querySelector('button[type=submit]').disabled`),true)
  await input('team-name','Vertrieb Nord')
  await evaluate(`document.querySelectorAll('input[type=checkbox]')[0].click()`)
  await evaluate(`document.querySelectorAll('input[type=checkbox]')[1].click()`)
  await click('Team speichern')
  await eventually(()=>evaluate(`document.querySelector('tbody')?.textContent.includes('2 Personen')`),'Created team not listed')
  await click('Bearbeiten')
  await eventually(()=>evaluate(`document.querySelectorAll('input[type=checkbox]').length===2`),'Edit directory missing')
  await input('team-name','Vertrieb Süd')
  await evaluate(`document.querySelectorAll('input[type=checkbox]')[0].click()`)
  await click('Team speichern')
  await eventually(()=>evaluate(`document.querySelector('tbody')?.textContent.includes('Vertrieb Süd')`),'Edit not saved')
  assert.equal(await evaluate(`document.querySelector('tbody').textContent.includes('anna@example.test')`),false)
  await input('team-search','no match')
  await eventually(()=>evaluate(`document.body.textContent.includes('Keine Teams zur Suche gefunden')`),'Search failed')
  await send('Page.navigate',{url:base+'?readonly'})
  await eventually(()=>evaluate(`document.body.textContent.includes('Mandantenadministration gepflegt')`),'Read-only view missing')
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].some(b=>b.textContent==='Team anlegen')`),false)
  assert.deepEqual(errors,[])
  console.log('Teams browser: create, list, edit, member removal, search and read-only checks passed.')
} finally {
  socket?.close(); if(chrome&&!chrome.killed) chrome.kill()
  await server.close()
  for(const p of pending.values()) clearTimeout(p.timer)
  await sleep(300)
  // Only the exact directory created by this test is removed.
  await rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:200})
}
