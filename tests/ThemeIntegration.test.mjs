import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const json = path => JSON.parse(read(path));
const installed = '../frontend/node_modules/@hammer2fall/identity-platform-react/';

test('Sales uses the published Common theme release, not a local package override', () => {
  const version = json('../frontend/package.json').dependencies['@hammer2fall/identity-platform-react'];
  const lock = json('../frontend/package-lock.json');
  const dependency = lock.packages['node_modules/@hammer2fall/identity-platform-react'];
  assert.match(version, /^\d+\.\d+\.\d+$/, 'Common must use an exact published release version');
  assert.equal(dependency.version, version);
  assert.equal(lock.packages[''].dependencies['@hammer2fall/identity-platform-react'], version);
  assert.equal(json(installed + 'package.json').version, version);
  assert.equal(new URL(dependency.resolved).hostname, 'npm.pkg.github.com');
  assert.match(dependency.integrity, /^sha512-/);
  assert.match(read(installed + 'dist/index.js'), /initializePlatformTheme/);
  assert.match(read(installed + 'dist/UserAuthorisationHeader.js'), /PlatformThemeSelector/);
});

test('all Sales theme tokens are supplied by the installed Common stylesheet', () => {
  const common = read(installed + 'src/styles.css');
  const tokens = new Set([...common.matchAll(/(--ip-[\w-]+):/g)].map(m => m[1]));
  for (const file of ['styles.css', 'reportNavigation.css']) {
    for (const match of read('../frontend/src/' + file).matchAll(/var\((--ip-[\w-]+)/g)) {
      assert.ok(tokens.has(match[1]), `Missing ${match[1]} used by ${file}`);
    }
  }
  assert.match(common, /data-identity-theme="light"/);
  assert.match(common, /--ip-surface: #ffffff/);
});

test('Sales initializes the common preference before rendering and has no second theme store', () => {
  const main = read('../frontend/src/main.tsx');
  assert.ok(main.indexOf('initializePlatformTheme()') < main.indexOf('createRoot(document'));
  assert.doesNotMatch(main, /localStorage|matchMedia/);
});
