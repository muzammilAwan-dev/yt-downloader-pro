/**
 * @fileoverview logger.js
 * Fire-and-forget debug logging client. Never touches console.* - the
 * page's own console is already noisy with other extensions/site scripts
 * (confirmed firsthand: a full console capture during testing turned out
 * to be entirely from an ad-blocker's filter engine, nothing to do with
 * this extension at all - exactly the kind of noise this avoids).
 *
 * Entries go to background.js, which owns the actual ring-buffer store in
 * chrome.storage.local (a single writer avoids races between content.js
 * and popup.js writing the same key independently). Export via the popup's
 * "Download Debug Logs" button or the Ctrl+Shift+Y shortcut.
 */
function ytdlpLog(level, component, message, data) {
  try {
    chrome.runtime.sendMessage({
      action: 'log',
      entry: {
        t: Date.now(),
        level,       // 'info' | 'warn' | 'error'
        component,   // 'content' | 'background' | 'popup'
        message,
        data: data !== undefined ? safeStringify(data) : undefined,
        url: (typeof location !== 'undefined') ? location.href : undefined,
      }
    });
  } catch { /* service worker asleep or context invalidated - drop silently, never fatal */ }
}

function safeStringify(data) {
  try { return JSON.stringify(data); } catch { return String(data); }
}

if (typeof window !== 'undefined') window.ytdlpLog = ytdlpLog;
