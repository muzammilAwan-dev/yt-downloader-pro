// Offline tests for findHash in tools/check-engine-checksums.js. The samples mirror host/YTDLPHost.Tests/Sha256SumsTests.cs.
// Run: node --test tools/test-engine-checksums.js
const { test } = require('node:test');
const assert = require('node:assert');
const { findHash } = require('./check-engine-checksums.js');

const A = 'a'.repeat(64), B = 'b'.repeat(64), C = 'c'.repeat(64);

test('a gnu style list is searched by file name', () => {
  const text = `${A}  yt-dlp\n${B}  yt-dlp.exe\n${C}  yt-dlp_macos\n`;
  assert.strictEqual(findHash(text, 'yt-dlp.exe'), B);
  assert.strictEqual(findHash(text, 'yt-dlp'), A);
  assert.strictEqual(findHash(text, 'yt-dlp_macos'), C);
});

test('windows line endings, a binary marker and a path prefix are fine', () => {
  const text = `${A} *./ffmpeg-master-latest-win64-gpl.zip\r\n${B}  ffmpeg-master-latest-linux64-gpl.tar.xz\r\n`;
  assert.strictEqual(findHash(text, 'ffmpeg-master-latest-win64-gpl.zip'), A);
  assert.strictEqual(findHash(text, 'ffmpeg-master-latest-linux64-gpl.tar.xz'), B);
});

test('a hash in capitals comes back in lower case', () => {
  assert.strictEqual(findHash(`${A.toUpperCase()}  tool.exe\n`, 'tool.exe'), A);
});

test('another file with a similar name is not a match', () => {
  for (const listed of ['my-yt-dlp.exe', 'yt-dlp.exe.sig', 'dlp.exe']) {
    assert.strictEqual(findHash(`${A}  ${listed}\n`, 'yt-dlp.exe'), null, listed);
  }
});

test('nothing listed, or a hash of the wrong length, is no hash', () => {
  assert.strictEqual(findHash(`${A}  other.zip\n`, 'tool.zip'), null);
  assert.strictEqual(findHash('', 'tool.zip'), null);
  assert.strictEqual(findHash('no hashes in here\n', 'tool.zip'), null);
  assert.strictEqual(findHash(`${A}f  tool.zip\n`, 'tool.zip'), null);
  assert.strictEqual(findHash(`${A.slice(1)}  tool.zip\n`, 'tool.zip'), null);
});

test('a powershell style table is read by the file name at the end of the line', () => {
  const text = 'Algorithm       Hash                                                                   Path\r\n' +
               '---------       ----                                                                   ----\r\n' +
               `SHA256          ${A.toUpperCase()}       D:\\a\\deno\\target\\release\\deno-x86_64-pc-windows-msvc.zip\r\n`;
  assert.strictEqual(findHash(text, 'deno-x86_64-pc-windows-msvc.zip'), A);
});

test('a bare hash is only accepted when asked for and when there is exactly one', () => {
  assert.strictEqual(findHash(A + '\n', 'tool.zip'), null);
  assert.strictEqual(findHash(A + '\n', 'tool.zip', true), A);
  assert.strictEqual(findHash(`${A}\n${A}\n`, 'tool.zip', true), A);
  assert.strictEqual(findHash(`${A}\n${B}\n`, 'tool.zip', true), null);
});

test('a named entry wins over the bare rule', () => {
  assert.strictEqual(findHash(`${A}  other.zip\n${B}  tool.zip\n`, 'tool.zip', true), B);
});
