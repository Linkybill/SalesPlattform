import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { resolveTenantApplicationPath } from '../frontend/node_modules/@hammer2fall/identity-platform-react/dist/TenantApplicationPath.js'
import * as tenantPaths from '../frontend/node_modules/@hammer2fall/identity-platform-react/dist/TenantApplicationPath.js'
import { createOidcSession } from '../frontend/node_modules/@hammer2fall/identity-platform-react/dist/OidcSession.js'

const require = createRequire(new URL('../frontend/package.json', import.meta.url))
const ts = require('typescript')

// Exercise the installed OIDC client too: the old bug passed resolver-only
// tests because its independent tenant parser still assumed /apps/<appKey>.
function installedOidcClient(config) {
  const source = readFileSync(new URL('../frontend/node_modules/@hammer2fall/identity-platform-react/dist/ApplicationOidcClient.js', import.meta.url), 'utf8')
  const { outputText } = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020,
  } })
  const exports = {}
  runInNewContext(outputText, { exports, URL, window: globalThis.window,
    require(name) {
      if (name === './TenantApplicationPath') return tenantPaths
      if (name === './OidcSession') return { createOidcSession }
      if (name === 'oidc-client-ts') return require(name)
      throw new Error('Unexpected OIDC dependency: ' + name)
    },
  })
  return exports.createApplicationOidcClient(config)
}

function evaluate(file, globals) {
  const source = readFileSync(new URL('../frontend/src/' + file, import.meta.url), 'utf8')
    .replace('import.meta.env', 'buildEnvironment')
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  })
  const exports = {}
  runInNewContext(outputText, { exports, URL, window: globalThis.window, ...globals })
  return exports
}

function configuration(environment) {
  return evaluate('identityPlatformConfig.ts', {
    require(name) {
      if (name === './deploymentEnvironment') return { deploymentEnvironment: environment }
      if (name === '@hammer2fall/identity-platform-react') return { resolveTenantApplicationPath }
      throw new Error('Unexpected dependency: ' + name)
    },
  })
}

const tenantId = '12345678-1234-4123-8123-123456789abc'
for (const host of ['127.0.0.1', '176.9.57.203']) {
  test(host + ': root origin, tenant path and callbacks use the installed shared helper', () => {
    const origin = 'https://' + host + ':3003'
    for (const pathname of ['/', '/auth/callback', '/' + tenantId, '/' + tenantId + '/projects', '/auth/silent-callback', '/not-a-tenant/projects']) {
      globalThis.window = { location: { origin, pathname } }
      for (const rootUrl of [undefined, '', origin, origin + '/']) {
        const environment = {
          VITE_API_BASE_URL: rootUrl,
          VITE_PLATFORM_API_BASE_URL: 'https://' + host + ':9443/profile-api',
          VITE_TENANT_PORTAL_URL: 'https://' + host + ':3001',
        }
        const { tenantApplicationPath: path, identityPlatformConfig: config } = configuration(environment)
        const hasTenant = pathname === '/' + tenantId || pathname.startsWith('/' + tenantId + '/')
        assert.equal(path.applicationRootUrl, origin)
        assert.equal(path.tenantId, hasTenant ? tenantId : null)
        assert.equal(path.routerBasePath, hasTenant ? '/' + tenantId : '')
        assert.equal(config.applicationBaseUrl, origin + (hasTenant ? '/' + tenantId : ''))
        assert.equal(config.applicationRootUrl, origin)
        assert.equal(config.platformApiBaseUrl, environment.VITE_PLATFORM_API_BASE_URL)
        assert.equal(config.syncTenantToUrl, false)
        const oidc = installedOidcClient(config)
        assert.equal(oidc.applicationRootUrl, origin)
        assert.equal(oidc.applicationBaseUrl, config.applicationBaseUrl)
        assert.equal(oidc.getStoredTenantId(), hasTenant ? tenantId : null)
      }
    }
  })
}

test('runtime profile wins over build URLs; incomplete runtime must not fall back', () => {
  const runtime = {
    VITE_API_BASE_URL: 'https://176.9.57.203:3003',
    VITE_PLATFORM_API_BASE_URL: 'https://176.9.57.203:9443/profile-api',
    VITE_TENANT_PORTAL_URL: 'https://176.9.57.203:3001',
  }
  const buildEnvironment = {
    ...runtime,
    VITE_API_BASE_URL: 'https://127.0.0.1:3003',
    VITE_PLATFORM_API_BASE_URL: 'https://127.0.0.1:9443/profile-api',
  }
  assert.equal(evaluate('deploymentEnvironment.ts', {
    __IDENTITY_PLATFORM_CONFIG__: runtime, buildEnvironment,
  }).deploymentEnvironment, runtime)
  assert.equal(evaluate('deploymentEnvironment.ts', { buildEnvironment }).deploymentEnvironment, buildEnvironment)
  for (const key of ['VITE_PLATFORM_API_BASE_URL', 'VITE_TENANT_PORTAL_URL']) {
    assert.throws(() => evaluate('deploymentEnvironment.ts', {
      __IDENTITY_PLATFORM_CONFIG__: { ...runtime, [key]: '' }, buildEnvironment,
    }), new RegExp(key))
  }
})
