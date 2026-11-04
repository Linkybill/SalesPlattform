import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import { test } from 'node:test'

const require = createRequire(new URL('../frontend/package.json', import.meta.url))
const ts = require('typescript')
const { outputText } = ts.transpileModule(readFileSync(new URL('../frontend/src/ZohoHookRebuildRequest.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
})
const exports = {}
const runId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const installationId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
runInNewContext(outputText, { exports, crypto: { randomUUID: () => runId }, DOMException })
const { requestZohoHookRebuild } = exports

function setup(options = {}) {
  const calls = []; const controller = new AbortController()
  const send = target => async (path, init) => {
    calls.push({ target, path, ...init, body: JSON.parse(init.body) })
    if (target === 'local' && options.abort) controller.abort()
    return new Response('{}', { status: target === 'local' ? options.status ?? 200 : 202 })
  }
  return { calls, input: { authorizedFetch: send('local'), platformAuthorizedFetch: send('central'), applicationKey: 'sales-plattform',
    tenantId: 'tenant-1', signal: controller.signal, installationId } }
}

test('installation hook rebuild stages locally before sending only an opaque scoped run ID to central scheduling', async () => {
  const { calls, input } = setup()
  assert.equal((await requestZohoHookRebuild(input)).status, 202)
  assert.deepEqual(calls.map(call => call.target), ['local', 'central'])
  assert.equal(calls[0].method, 'PUT'); assert.ok(calls[0].path.endsWith('/' + runId))
  assert.deepEqual(calls[0].body, { parameters: null })
  assert.deepEqual(calls[1].body, { runId })
  assert.match(calls[1].path, new RegExp(`tenantId=tenant-1&installationId=${installationId}$`))
  assert.equal(calls[1].signal, input.signal)
})

test('failed preparation or cancellation never starts central work or retries via the legacy route', async () => {
  for (const status of [403, 409, 503]) {
    const { calls, input } = setup({ status })
    assert.equal((await requestZohoHookRebuild(input)).status, status)
    assert.deepEqual(calls.map(call => call.target), ['local'])
  }
  const aborted = setup({ abort: true })
  await assert.rejects(requestZohoHookRebuild(aborted.input), { name: 'AbortError' })
  assert.equal(aborted.calls.length, 1)
})

test('legacy hook rebuild still schedules once without local installation storage', async () => {
  const { calls, input } = setup()
  input.installationId = undefined
  assert.equal((await requestZohoHookRebuild(input)).status, 202)
  assert.deepEqual(calls.map(call => call.target), ['central'])
  assert.deepEqual(calls[0].body, {})
  assert.ok(!calls[0].path.includes('installationId'))
})

test('handoff continuation is staged only locally and cannot authorize an invented transition centrally', async () => {
  const { calls, input } = setup()
  assert.equal((await requestZohoHookRebuild({ ...input, handoffOnly: true })).status, 202)
  assert.deepEqual(calls[0].body, { parameters: { operation: 'resume-installation-handoff' } })
  assert.deepEqual(calls[1].body, { runId })
  assert.ok(!JSON.stringify(calls[1].body).includes('transition'))
  const legacy = setup()
  await assert.rejects(requestZohoHookRebuild({ ...legacy.input, installationId: undefined, handoffOnly: true }), /requires an installation/)
  assert.equal(legacy.calls.length, 0)
})
