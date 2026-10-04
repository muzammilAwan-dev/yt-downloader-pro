/**
 * @fileoverview Background Service Worker
 * All configured sites are active automatically (declared statically in the
 * manifest, same as YouTube always was) - there is no separate "enable this
 * site" permission step. "disabledSites" in chrome.storage.sync is just a
 * user preference content.js checks before injecting, so Settings can still
 * turn an individual site off without touching browser permissions at all.
 * @version 6.2.1
 */

importScripts('sites.js');

function getCookiesForSite(siteId, sendResponse) {
  const site = self.YTDLP_SITES[siteId] || self.YTDLP_SITES.youtube;

  chrome.cookies.getAll({ domain: site.cookieDomain }, (cookies) => {
    if (!cookies || cookies.length === 0) {
      sendResponse("");
      return;
    }

    const filteredCookies = cookies.filter(c => site.cookieAllowlist.includes(c.name));

    let netscapeFormat = "# Netscape HTTP Cookie File\n";
    netscapeFormat += "# http://curl.haxx.se/rfc/cookie_spec.html\n";
    netscapeFormat += "# This is a generated file!  Do not edit.\n\n";

    filteredCookies.forEach(c => {
      const domain = c.domain;
      const includeSubdomains = domain.startsWith('.') ? 'TRUE' : 'FALSE';
      const path = c.path;
      const secure = c.secure ? 'TRUE' : 'FALSE';
      const expiry = c.expirationDate ? Math.floor(c.expirationDate) : Math.floor(Date.now() / 1000) + (3600 * 24 * 30);

      netscapeFormat += `${domain}\t${includeSubdomains}\t${path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
    });

    const encodedPayload = btoa(unescape(encodeURIComponent(netscapeFormat)));
    sendResponse(encodedPayload);
  });
}

async function listSites() {
  const { disabledSites = [] } = await chrome.storage.sync.get('disabledSites');
  return self.getAllSites().map(site => ({
    id: site.id,
    label: site.label,
    note: site.note || '',
    enabled: !disabledSites.includes(site.id),
  }));
}

async function setSiteEnabled(siteId, enabled) {
  const { disabledSites = [] } = await chrome.storage.sync.get('disabledSites');
  const next = enabled
    ? disabledSites.filter(id => id !== siteId)
    : Array.from(new Set([...disabledSites, siteId]));
  await chrome.storage.sync.set({ disabledSites: next });
  return { ok: true };
}

/**
 * Debug log store. background.js is the single writer (content.js and
 * popup.js only ever send 'log' messages here, never touch storage
 * directly) specifically to avoid read-modify-write races that would lose
 * entries if two tabs logged at nearly the same moment. logWriteQueue
 * chains each write onto the previous one so they're always serialized,
 * even though each individual write is itself async.
 */
const LOG_KEY = 'ytdlp_debug_logs';
const LOG_MAX_ENTRIES = 800;
let logWriteQueue = Promise.resolve();

function appendLog(entry) {
  logWriteQueue = logWriteQueue.then(async () => {
    const stored = await chrome.storage.local.get(LOG_KEY);
    const logs = stored[LOG_KEY] || [];
    logs.push(entry);
    const trimmed = logs.length > LOG_MAX_ENTRIES ? logs.slice(logs.length - LOG_MAX_ENTRIES) : logs;
    await chrome.storage.local.set({ [LOG_KEY]: trimmed });
  }).catch(() => {}); // a failed write should never break the chain for the next one
  return logWriteQueue;
}

function formatLogEntry(e) {
  const time = new Date(e.t).toISOString();
  const where = e.site ? `${e.component}:${e.site}` : e.component;
  const dataStr = e.data ? ` | ${e.data}` : '';
  return `[${time}] [${String(e.level || 'info').toUpperCase()}] [${where}] ${e.message}${dataStr}${e.url ? `  (${e.url})` : ''}`;
}

/**
 * Downloads the accumulated logs as a real file via chrome.downloads -
 * extensions can't write to an arbitrary filesystem path (no "temp folder"
 * access), so handing off to the browser's own download mechanism is the
 * actual native equivalent. Triggered by the popup button or the
 * Ctrl+Shift+Y shortcut below - same function either way.
 */
async function exportLogs() {
  const stored = await chrome.storage.local.get(LOG_KEY);
  const logs = stored[LOG_KEY] || [];
  const text = logs.length > 0
    ? logs.map(formatLogEntry).join('\n')
    : 'No debug logs recorded yet.';

  const dataUrl = 'data:text/plain;charset=utf-8,' + encodeURIComponent(text);
  const filename = `ytdlp-debug-logs-${new Date().toISOString().replace(/[:.]/g, '-')}.txt`;
  await chrome.downloads.download({ url: dataUrl, filename, saveAs: false });
  return { ok: true, count: logs.length };
}

/**
 * Image posts (Instagram/Facebook photos): yt-dlp has no image support there, so
 * hand the image URL straight to the browser's own download manager.
 * Lands in <Downloads>/YT Downloader Pro/ - extensions can't pick another folder.
 */
async function saveImage(url, site) {
  try {
    if (!/^(https?:|data:)/i.test(url || '')) throw new Error('Unsupported image address');
    let ext = 'jpg';
    try {
      const m = new URL(url).pathname.match(/\.(jpe?g|png|webp|gif|avif)$/i);
      if (m) ext = m[1].toLowerCase();
    } catch { /* data: URL etc - keep jpg */ }
    const filename = `YT Downloader Pro/${site || 'image'}_${Date.now()}.${ext}`;
    await chrome.downloads.download({ url, filename, conflictAction: 'uniquify', saveAs: false });
    return { ok: true };
  } catch (e) {
    return { ok: false, error: String((e && e.message) || e) };
  }
}

async function clearLogs() {
  await chrome.storage.local.remove(LOG_KEY);
  return { ok: true };
}

chrome.commands.onCommand.addListener((command) => {
  if (command === 'export-debug-logs') exportLogs();
});

/**
 * Toolbar badge: the zero-page-footprint fallback. Works the same whether
 * the on-page button/widget is set to always/fade/hidden, since content.js
 * reports its count regardless of that preference.
 */
function setBadgeForTab(tabId, count) {
  if (tabId == null) return;
  chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : '' });
  if (count > 0) chrome.action.setBadgeBackgroundColor({ tabId, color: '#3EA6FF' });
}

// Clear the badge on navigation so a stale count doesn't linger from the
// previous page until content.js reports in again.
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === 'loading') chrome.action.setBadgeText({ tabId, text: '' });
});

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === "get_cookies") {
    getCookiesForSite(request.site, sendResponse);
    return true;
  }

  if (request.action === "download_image") {
    saveImage(request.url, request.site).then(sendResponse);
    return true;
  }

  if (request.action === "report_video_count") {
    setBadgeForTab(sender.tab?.id, request.count || 0);
    return; // no response needed
  }

  if (request.action === "log") {
    appendLog({ ...request.entry, tabId: sender.tab?.id });
    return; // fire-and-forget, no response needed
  }

  if (request.action === "export_logs") {
    exportLogs().then(sendResponse);
    return true;
  }

  if (request.action === "clear_logs") {
    clearLogs().then(sendResponse);
    return true;
  }

  if (request.action === "list_sites") {
    listSites().then(sendResponse);
    return true;
  }

  if (request.action === "set_site_enabled") {
    setSiteEnabled(request.site, request.enabled).then(sendResponse);
    return true;
  }
});