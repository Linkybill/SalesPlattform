import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const read = path => JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8'));
const config = read('../appsettings.Deployment.json');
const manifest = read('../backend/manifest.json');
const path = '/api/integrations/zoho/webhook';

for (const target of ['ax42-1', 'ax42-2']) test(`${target}: webhook uses the public Sales endpoint projection`, () => {
  const profile = config.Targets[target].Environments.dev;
  assert.deepEqual(profile.BackendUrls.Zoho__WebhookUrl, { Endpoint: 'Frontend', Path: path });
  assert.equal(profile.Urls.Frontend.Port, 3003);
  assert.equal(profile.BackendUrls.Zoho__RedirectUri.Endpoint, 'Frontend');
});
test('manifest opts in only the exact backend webhook path', () => {
  assert.deepEqual(manifest.webhooks, [{ path, componentKey: 'backend' }]);
  assert.equal(manifest.components.find(c => c.key === 'backend').type, 'Backend');
});
test('local development does not register a loopback URL with Zoho', () => {
  assert.equal(config.Targets.local.Environments.dev.BackendUrls.Zoho__WebhookUrl, undefined);
  const local = readFileSync(new URL('../deploy/local-environment.ps1', import.meta.url), 'utf8');
  assert.match(local, /ZOHO_WEBHOOK_URL/);
});
test('hook URL is configurable per tenant, never per user or hardcoded to a customer', () => {
  const setting = manifest.settings.find(x => x.key === 'zoho.webhookUrl');
  assert.ok(setting);
  assert.deepEqual(setting.scopes, ['tenantApp']);
  assert.equal(setting.type, 'string');
  assert.equal(setting.defaultValue, '');
  assert.notEqual(setting.secret, true);
  assert.deepEqual(setting.dependsOn, { scope: 'tenantApp', settingKey: 'crm.integration', value: 'zoho' });
});
test('hook worker and overview use the same tenant settings resolver, not process options', () => {
  for (const file of ['ZohoSubscriptionAdapter.cs', 'ZohoWebhookOverviewService.cs']) {
    const source = readFileSync(new URL(`../backend/Integrations/Zoho/${file}`, import.meta.url), 'utf8');
    assert.match(source, /webhookSettings.ResolveCurrentAsync\(cancellationToken\)/);
    assert.doesNotMatch(source, /options(?:\.Value)?\.WebhookUrl/);
  }
});
