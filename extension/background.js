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

  if (request.action === "report_video_count") {
    setBadgeForTab(sender.tab?.id, request.count || 0);
    return; // no response needed
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