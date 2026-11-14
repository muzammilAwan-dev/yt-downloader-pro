// Step 3.5 helper, run by hand (needs the internet): node tools/check-engine-checksums.js
// Downloads the REAL checksum files the engine download relies on (EngineCatalog.cs) and checks that each asset name has an entry, with the same
// reading rules as Sha256Sums.cs. It touches nothing on this machine. If a project renames a file or changes the format of its checksum file, this is where it shows.
'use strict';

const HASH = /(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])/;

function endsWithName(rest, name) {
  if (!name || !rest.toLowerCase().endsWith(name.toLowerCase())) return false;
  const before = rest.length - name.length - 1;
  return before < 0 || '/ *'.includes(rest[before]);
}

// Same rules as Sha256Sums.FindHash in host/YTDLPHost.Core/Services/Sha256Sums.cs (tools/test-engine-checksums.js and Sha256SumsTests.cs use the same samples).
function findHash(text, name, allowBare = false) {
  const seen = new Set();
  for (const raw of text.split('\n')) {
    const line = raw.trim();
    const m = HASH.exec(line);
    if (!m) continue;
    const hash = m[0].toLowerCase();
    seen.add(hash);
    const rest = (line.slice(0, m.index) + line.slice(m.index + m[0].length)).replace(/^[ \t*]+|[ \t*]+$/g, '').replace(/\\/g, '/');
    if (endsWithName(rest, name)) return hash;
  }
  return allowBare && seen.size === 1 ? [...seen][0] : null;
}

const YTDLP = 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/';
const DENO = 'https://github.com/denoland/deno/releases/latest/download/';
const BTBN = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/';
const DENO_TARGETS = [
  'deno-x86_64-pc-windows-msvc.zip', 'deno-aarch64-pc-windows-msvc.zip', 'deno-x86_64-apple-darwin.zip',
  'deno-aarch64-apple-darwin.zip', 'deno-x86_64-unknown-linux-gnu.zip', 'deno-aarch64-unknown-linux-gnu.zip'
];

const CHECKS = [
  { sums: YTDLP + 'SHA2-256SUMS', bare: false, assets: ['yt-dlp.exe', 'yt-dlp_arm64.exe', 'yt-dlp_macos', 'yt-dlp_linux', 'yt-dlp_linux_aarch64'] },
  { sums: BTBN + 'checksums.sha256', bare: false, assets: ['ffmpeg-master-latest-win64-gpl.zip', 'ffmpeg-master-latest-winarm64-gpl.zip', 'ffmpeg-master-latest-linux64-gpl.tar.xz', 'ffmpeg-master-latest-linuxarm64-gpl.tar.xz'] },
  ...DENO_TARGETS.map(target => ({ sums: DENO + target + '.sha256sum', bare: true, assets: [target] }))
];

async function main() {
  let failures = 0;
  for (const check of CHECKS) {
    let text;
    try {
      const response = await fetch(check.sums);
      if (!response.ok) throw new Error('HTTP ' + response.status);
      text = await response.text();
    } catch (e) {
      console.log(`FAIL  ${check.sums}  (${e.message})`);
      failures++;
      continue;
    }
    for (const asset of check.assets) {
      const hash = findHash(text, asset, check.bare);
      console.log(`${hash ? 'ok   ' : 'FAIL '} ${asset}  ${hash || 'no entry found in ' + check.sums + ' (' + text.length + ' bytes)'}`);
      if (!hash) failures++;
    }
  }
  console.log(failures === 0 ? '\nAll checksum files are in the expected shape.' : `\n${failures} problem(s): fix EngineCatalog.cs or Sha256Sums.cs before shipping.`);
  process.exit(failures === 0 ? 0 : 1);
}

module.exports = { findHash };
if (require.main === module) main();
