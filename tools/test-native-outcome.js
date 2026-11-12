// Tests for ytdlpNativeOutcome in extension/sites.js (step 2.7). Run: node --test tools/test-native-outcome.js
const { test } = require('node:test');
const assert = require('node:assert');
const vm = require('vm'), fs = require('fs'), path = require('path');

const sandbox = { self: {} };
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'extension', 'sites.js'), 'utf8'), sandbox);
// The function runs in another vm context, so its objects have another Object.prototype and deepStrictEqual would refuse them: copy through JSON.
const outcome = (result, legacyFallback) => JSON.parse(JSON.stringify(sandbox.self.ytdlpNativeOutcome(result, legacyFallback)));

for (const legacy of [true, false]) {
  test(`an accepted download is done, fallback setting ${legacy}`, () => {
    assert.deepStrictEqual(outcome({ ok: true }, legacy), { action: 'done' });
  });

  test(`a failed download shows the app's reason and never falls back, fallback setting ${legacy}`, () => {
    assert.deepStrictEqual(outcome({ ok: false, failed: true, reason: 'Setup failed' }, legacy), { action: 'error', message: 'Setup failed' });
    assert.deepStrictEqual(outcome({ ok: false, failed: true }, legacy), { action: 'error', message: 'The app could not start the download.' });
  });
}

test('with the fallback on, unavailable, rejected and no answer all send the old ytdlp:// link', () => {
  assert.deepStrictEqual(outcome({ ok: false, unavailable: true, reason: 'host not found' }, true), { action: 'fallback' });
  assert.deepStrictEqual(outcome({ ok: false, rejected: true, reason: 'bad field' }, true), { action: 'fallback' });
  assert.deepStrictEqual(outcome(null, true), { action: 'fallback' });
  assert.deepStrictEqual(outcome(undefined, true), { action: 'fallback' });
});

test('with the fallback off, unavailable and no answer say the app is not reachable', () => {
  for (const result of [{ ok: false, unavailable: true, reason: 'host not found' }, null, undefined]) {
    const o = outcome(result, false);
    assert.strictEqual(o.action, 'error');
    assert.match(o.message, /not reachable/);
  }
});

test('with the fallback off, a rejection says so and includes the reason when there is one', () => {
  assert.deepStrictEqual(outcome({ ok: false, rejected: true, reason: 'bad field' }, false), { action: 'error', message: 'YT Downloader Pro refused this download: bad field' });
  assert.deepStrictEqual(outcome({ ok: false, rejected: true }, false), { action: 'error', message: 'YT Downloader Pro refused this download.' });
});
