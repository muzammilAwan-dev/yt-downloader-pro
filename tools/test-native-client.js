// Tests for extension/native.js with a fake chrome object. Run: node --test tools/test-native-client.js
const { test } = require('node:test');
const assert = require('node:assert');
const vm = require('vm'), fs = require('fs'), path = require('path');

const SOURCE = fs.readFileSync(path.join(__dirname, '..', 'extension', 'native.js'), 'utf8');

function setup() {
  const ports = [], tabMessages = [], notifications = [];
  const chrome = {
    runtime: {
      lastError: null,
      connectNative(name) {
        const handlers = { message: null, disconnect: null };
        const p = {
          name, sent: [], connectedTo: name,
          postMessage(m) { if (p.throwOnPost) throw new Error('Attempt to postMessage on disconnected port'); p.sent.push(m); },
          onMessage: { addListener(fn) { handlers.message = fn; } },
          onDisconnect: { addListener(fn) { handlers.disconnect = fn; } },
          reply(m) { handlers.message(m); },
          disconnect(errorMessage) { chrome.runtime.lastError = errorMessage ? { message: errorMessage } : null; handlers.disconnect(); chrome.runtime.lastError = null; },
        };
        ports.push(p);
        return p;
      },
    },
    tabs: { sendMessage(tabId, message) { tabMessages.push({ tabId, message }); return Promise.resolve(); } },
    notifications: { create(options) { notifications.push(options); } },
  };
  const sandbox = { chrome, self: {}, setTimeout, clearTimeout, Date, Map, Promise, console };
  vm.createContext(sandbox);
  vm.runInContext(SOURCE, sandbox);
  sandbox.self.YtdlpNative._setAcceptTimeoutForTests(150); // jobs left open by a test must not keep node waiting 30 s
  return { native: sandbox.self.YtdlpNative, ports, tabMessages, notifications };
}

const REQUEST = { urls: ['https://vimeo.com/1'], options: { quality: '720' } };
const tick = () => new Promise((r) => setTimeout(r, 5));

test('an accepted download resolves ok and sends a well-formed protocol message', async () => {
  const { native, ports } = setup();
  const result = native.sendDownload({ ...REQUEST, auth: { cookies: '# c', userAgent: 'UA' } }, 7);
  await tick();
  const sent = ports[0].sent[0];
  assert.equal(ports[0].name, 'com.ytdlppro.host');
  assert.equal(sent.v, 1); assert.equal(sent.type, 'download');
  assert.match(sent.id, /^[A-Za-z0-9_-]{1,64}$/);
  assert.deepEqual(sent.urls, REQUEST.urls); assert.deepEqual(sent.options, REQUEST.options);
  assert.deepEqual(sent.auth, { cookies: '# c', userAgent: 'UA' });
  ports[0].reply({ v: 1, id: sent.id, type: 'accepted' });
  assert.deepEqual(await result, { ok: true, id: sent.id });
});

test('no auth block is sent when there are no cookies and no user agent', async () => {
  const { native, ports } = setup();
  native.sendDownload({ ...REQUEST, auth: {} });
  await tick();
  assert.equal('auth' in ports[0].sent[0], false);
});

test('two downloads share one port', async () => {
  const { native, ports } = setup();
  native.sendDownload(REQUEST); native.sendDownload(REQUEST);
  await tick();
  assert.equal(ports.length, 1); assert.equal(ports[0].sent.length, 2);
  assert.notEqual(ports[0].sent[0].id, ports[0].sent[1].id);
});

test('a missing host is reported as unavailable with the browser\'s reason', async () => {
  const { native, ports } = setup();
  const result = native.sendDownload(REQUEST);
  await tick();
  ports[0].disconnect('Specified native messaging host not found.');
  const r = await result;
  assert.equal(r.ok, false); assert.equal(r.unavailable, true); assert.match(r.reason, /not found/);
});

test('a host that exits without a reply is unavailable, and the next request reconnects', async () => {
  const { native, ports } = setup();
  const first = native.sendDownload(REQUEST);
  await tick();
  ports[0].disconnect('Native host has exited.');
  assert.equal((await first).unavailable, true);
  native.sendDownload(REQUEST);
  await tick();
  assert.equal(ports.length, 2);
});

test('a rejected request resolves as rejected with the reason', async () => {
  const { native, ports } = setup();
  const result = native.sendDownload(REQUEST);
  await tick();
  ports[0].reply({ v: 1, id: ports[0].sent[0].id, type: 'rejected', code: 'bad_field', reason: 'options.quality is wrong' });
  assert.deepEqual(await result, { ok: false, rejected: true, reason: 'options.quality is wrong' });
});

test('an error before acceptance resolves as failed (no fallback)', async () => {
  const { native, ports } = setup();
  const result = native.sendDownload(REQUEST);
  await tick();
  ports[0].reply({ v: 1, id: ports[0].sent[0].id, type: 'error', reason: 'setup failed' });
  assert.deepEqual(await result, { ok: false, failed: true, reason: 'setup failed' });
});

test('done after acceptance is reported to the tab', async () => {
  const { native, ports, tabMessages } = setup();
  const result = native.sendDownload(REQUEST, 42);
  await tick();
  const id = ports[0].sent[0].id;
  ports[0].reply({ v: 1, id, type: 'accepted' });
  await result;
  ports[0].reply({ v: 1, id, type: 'progress', percent: 50 });
  ports[0].reply({ v: 1, id, type: 'done', note: 'file already existed' });
  assert.equal(tabMessages.length, 1);
  assert.equal(tabMessages[0].tabId, 42);
  assert.equal(tabMessages[0].message.action, 'native_event');
  assert.equal(tabMessages[0].message.event, 'done');
  assert.equal(tabMessages[0].message.note, 'file already existed');
});

test('an error after acceptance is reported to the tab with its reason', async () => {
  const { native, ports, tabMessages } = setup();
  const result = native.sendDownload(REQUEST, 42);
  await tick();
  const id = ports[0].sent[0].id;
  ports[0].reply({ v: 1, id, type: 'accepted' });
  await result;
  ports[0].reply({ v: 1, id, type: 'error', reason: 'Video unavailable' });
  assert.equal(tabMessages[0].message.event, 'error');
  assert.equal(tabMessages[0].message.reason, 'Video unavailable');
});

test('an error without a tab becomes a notification', async () => {
  const { native, ports, notifications } = setup();
  const result = native.sendDownload(REQUEST);
  await tick();
  const id = ports[0].sent[0].id;
  ports[0].reply({ v: 1, id, type: 'accepted' });
  await result;
  ports[0].reply({ v: 1, id, type: 'error', reason: 'boom' });
  assert.equal(notifications.length, 1);
  assert.match(notifications[0].message, /boom/);
});

test('a disconnect after acceptance does not reject anything and the port is replaced', async () => {
  const { native, ports } = setup();
  const result = native.sendDownload(REQUEST);
  await tick();
  ports[0].reply({ v: 1, id: ports[0].sent[0].id, type: 'accepted' });
  assert.equal((await result).ok, true);
  ports[0].disconnect('Native host has exited.');
  native.sendDownload(REQUEST);
  await tick();
  assert.equal(ports.length, 2);
});

test('silence is unavailable after the timeout', async () => {
  const { native } = setup();
  native._setAcceptTimeoutForTests(20);
  const r = await native.sendDownload(REQUEST);
  assert.equal(r.unavailable, true); assert.match(r.reason, /did not answer/);
});

test('a port that throws on postMessage is unavailable', async () => {
  const { native, ports } = setup();
  const first = native.sendDownload(REQUEST);
  await tick();
  ports[0].throwOnPost = true;
  const r = await native.sendDownload(REQUEST);
  assert.equal(r.unavailable, true);
  ports[0].disconnect('x');
  await first;
});

test('messages for unknown ids are ignored', async () => {
  const { native, ports, tabMessages } = setup();
  native.sendDownload(REQUEST);
  await tick();
  ports[0].reply({ v: 1, id: 'nope', type: 'done' });
  ports[0].reply(null);
  ports[0].reply('text');
  assert.equal(tabMessages.length, 0);
});

test('hello returns the host version', async () => {
  const { native, ports } = setup();
  const result = native.hello();
  await tick();
  assert.equal(ports[0].sent[0].type, 'hello');
  ports[0].reply({ v: 1, id: ports[0].sent[0].id, type: 'hello', hostVersion: '6.4.9', protocol: 1 });
  assert.deepEqual(await result, { ok: true, hostVersion: '6.4.9', protocol: 1 });
});
