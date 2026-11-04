/**
 * @fileoverview Native Messaging client (Phase 2, step 2.5). Loaded by the background service worker ONLY: content scripts and the popup
 * cannot talk to a native host from their own context, so they send a "native_download" message to background.js, which calls this.
 *
 * One long-lived port (connectNative) is shared by all downloads. While it is open Chrome also keeps the service worker alive, which is what
 * lets the app's progress and final result come back. The host (our small shim) starts the app if needed, so the first reply can take a while.
 *
 * Every request ends in exactly ONE of these results (promise resolves, never rejects):
 *   { ok: true }                          the app accepted and queued it; "done"/"error" are reported later through notify()
 *   { ok: false, unavailable: true, ... } no host registered, the shim/app could not be reached, or it stayed silent -> caller falls back to ytdlp://
 *   { ok: false, rejected: true, ... }    the app judged the request invalid        -> caller falls back to ytdlp:// (one-release migration)
 *   { ok: false, failed: true, ... }      the app could not take it (for example its first-time setup failed) -> show the reason, no fallback
 *
 * Protocol: docs/NATIVE_PROTOCOL.md.
 */
(function (root) {
  'use strict';

  const HOST_NAME = 'com.ytdlppro.host';
  let acceptTimeoutMs = 30000; // the shim may have to start the app first (up to 20 s)

  let port = null;
  let counter = 0;
  const jobs = new Map(); // request id -> { port, tabId, resolve, accepted, timer }

  function nextId() {
    counter += 1;
    return `d${Date.now().toString(36)}_${counter}`; // 1-64 chars from A-Za-z0-9_-, as the protocol requires
  }

  function connect() {
    if (port) return port;
    const p = chrome.runtime.connectNative(HOST_NAME);
    p.onMessage.addListener((message) => onMessage(message));
    p.onDisconnect.addListener(() => onDisconnect(p));
    port = p;
    return p;
  }

  function onDisconnect(p) {
    // Read lastError inside the handler, or Chrome logs "Unchecked runtime.lastError".
    const reason = (chrome.runtime.lastError && chrome.runtime.lastError.message) || 'The connection to the app was closed.';
    if (port === p) port = null;
    for (const [id, job] of Array.from(jobs)) {
      if (job.port !== p) continue;
      clearTimeout(job.timer);
      jobs.delete(id);
      // A download that was already accepted keeps running inside the app; nothing more can be reported about it.
      if (!job.accepted) job.resolve({ ok: false, unavailable: true, reason });
    }
  }

  function settle(id, result) {
    const job = jobs.get(id);
    if (!job) return;
    clearTimeout(job.timer);
    jobs.delete(id);
    job.resolve(result);
  }

  function onMessage(message) {
    if (!message || typeof message !== 'object') return;
    const job = jobs.get(message.id);
    if (!job) return; // not ours, or already settled

    switch (message.type) {
      case 'hello':
        settle(message.id, { ok: true, hostVersion: message.hostVersion, protocol: message.protocol });
        break;
      case 'accepted':
        job.accepted = true;
        clearTimeout(job.timer);
        job.resolve({ ok: true, id: message.id });
        break;
      case 'rejected':
        settle(message.id, { ok: false, rejected: true, reason: message.reason || 'The app refused the request.' });
        break;
      case 'progress':
        // Not shown anywhere yet (the app has its own window); kept for a later progress display.
        job.progress = message;
        break;
      case 'done':
      case 'error':
        if (!job.accepted) {
          settle(message.id, { ok: false, failed: true, reason: message.reason || 'The app could not start the download.' });
        } else {
          jobs.delete(message.id);
          notify(job, message);
        }
        break;
    }
  }

  /** Tells the tab that started the download how it ended (content.js shows a toast for errors); without a tab, errors become a notification. */
  function notify(job, message) {
    const event = { action: 'native_event', event: message.type, reason: message.reason, note: message.note };
    if (job.tabId != null) {
      Promise.resolve(chrome.tabs.sendMessage(job.tabId, event)).catch(() => { /* the tab was closed */ });
    } else if (message.type === 'error' && chrome.notifications) {
      chrome.notifications.create({
        type: 'basic',
        iconUrl: 'icons/icon128.png',
        title: 'YT Downloader Pro',
        message: 'Download failed: ' + (message.reason || 'unknown error'),
      });
    }
  }

  function send(message, tabId) {
    return new Promise((resolve) => {
      const job = { port: null, tabId, resolve, accepted: false, timer: null };
      try {
        const p = connect();
        job.port = p;
        job.timer = setTimeout(() => {
          jobs.delete(message.id);
          resolve({ ok: false, unavailable: true, reason: 'The app did not answer in time.' });
        }, acceptTimeoutMs);
        jobs.set(message.id, job);
        p.postMessage(message);
      } catch (e) {
        clearTimeout(job.timer);
        jobs.delete(message.id);
        port = null;
        resolve({ ok: false, unavailable: true, reason: String((e && e.message) || e) });
      }
    });
  }

  /**
   * @param {{urls: string[], options: object, auth?: {cookies?: string, userAgent?: string}}} request  the "download" fields of the protocol
   * @param {number} [tabId]  the tab to report the final result to
   */
  function sendDownload(request, tabId) {
    const message = { v: 1, id: nextId(), type: 'download', urls: request.urls, options: request.options };
    if (request.auth && (request.auth.cookies || request.auth.userAgent)) message.auth = request.auth;
    return send(message, tabId);
  }

  /** Asks the host who it is: { ok: true, hostVersion, protocol } or { ok: false, unavailable: true, reason }. */
  function hello() {
    return send({ v: 1, id: nextId(), type: 'hello' });
  }

  root.YtdlpNative = {
    sendDownload,
    hello,
    HOST_NAME,
    _setAcceptTimeoutForTests(ms) { acceptTimeoutMs = ms; },
  };
})(typeof self !== 'undefined' ? self : globalThis);
