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
test('live verification is authenticated, tenant-admin guarded and does not accept a channel or URL', () => {
  const source = readFileSync(new URL('../backend/Integrations/Zoho/ZohoEndpointExtensions.cs', import.meta.url), 'utf8');
  const check = source.split('protectedGroup.MapPost("/hooks/check"')[1].split('protectedGroup.MapGet("/oauth/start"')[0];
  assert.match(source, /RequireAuthorization\("sales-user"\)/);
  assert.match(check, /IsCurrentTenantAdminAsync/);
  assert.ok(check.indexOf('IsCurrentTenantAdminAsync') < check.indexOf('verification.CheckAsync'));
  assert.match(check, /CacheControl = "no-store"/);
  assert.match(source, /record ZohoHookCheckRequest\(string Module\)/);
});
test('live verification never writes subscriptions and only reads scoped channel IDs', () => {
  const source = readFileSync(new URL('../backend/Integrations/Zoho/ZohoHookVerificationService.cs', import.meta.url), 'utf8');
  assert.match(source, /OpenReadOnlyAsync/);
  assert.match(source, /ProviderKey == "zoho" && x.ConnectionKey == "default" && x.Module == module/);
  assert.match(source, /Select\(x => x.ChannelId\)/);
  assert.doesNotMatch(source, /SaveChanges|RegisterNotificationsAsync|DisableNotificationsAsync/);
  assert.match(source, /VerificationTokenHash = x.VerificationTokenHash/);
  assert.match(source, /CryptographicOperations.FixedTimeEquals/);
  const adapter = readFileSync(new URL('../backend/Integrations/Zoho/ZohoCrmAdapter.NotificationDiagnostics.cs', import.meta.url), 'utf8');
  assert.match(adapter, /SendAsync\(HttpMethod.Get/);
  assert.match(adapter, /channel_id=/);
  assert.doesNotMatch(adapter, /HttpMethod.Post|HttpMethod.Delete/);
  assert.match(adapter, /\[property: JsonIgnore\] string\? TokenHash/);
});
test('live verification is explicit, abortable and independent of overview refresh', () => {
  const source = readFileSync(new URL('../frontend/src/ZohoHookCheck.tsx', import.meta.url), 'utf8');
  assert.match(source, /onClick=\{\(\) => void check\(\)\}/);
  assert.match(source, /method: 'POST'/);
  assert.match(source, /signal: controller.signal/);
  assert.match(source, /active.current === controller/);
  assert.match(source, /notifications.READ/);
});
test('hook rebuild uses the central manual job queue with tenant scope and confirmation', () => {
  const source = readFileSync(new URL('../frontend/src/ZohoHookRebuild.tsx', import.meta.url), 'utf8');
  assert.match(source, /window.confirm/);
  assert.match(source, /platformAuthorizedFetch/);
  assert.match(source, /crm-subscription-maintenance\/runs\?tenantId=/);
  assert.match(source, /method: 'POST'/);
  assert.match(source, /active.current === controller/);
  assert.match(source, /response.status === 409/);
  const worker = readFileSync(new URL('../backend/Integrations/Zoho/ZohoSubscriptionAdapter.cs', import.meta.url), 'utf8');
  assert.match(worker, /IsManualRebuild\(context.Trigger\)/);
  assert.match(worker, /!forceRebuild && CanKeepSubscription/);
  assert.match(worker, /await using var moduleSession = await dbFactory.OpenAsync/);
  assert.doesNotMatch(worker, /subscription.Status = "failed"/);
  assert.doesNotMatch(worker.split('var forceRebuild')[1].split('var eventResult')[0], /exception.Message/);
});
