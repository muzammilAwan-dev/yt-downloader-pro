/**
 * @fileoverview sites.js
 * Single source of truth for every site this extension can act on: which
 * yt-dlp features make sense there, what cookies are needed, and where in
 * the page a download control should be anchored.
 *
 * Loaded as a plain (non-module) script in three contexts: content scripts,
 * the background service worker (via importScripts), and the popup - so it
 * has to stay dependency-free and framework-free.
 *
 * Adding a new site = adding one entry here. Nothing else should need a
 * site-specific "if" statement after this refactor.
 */

const YTDLP_SITES = {

  youtube: {
    id: 'youtube',
    label: 'YouTube',
    builtIn: true, // ships enabled by default in the static manifest - no opt-in needed
    matches: ['*://*.youtube.com/*', '*://youtu.be/*'],
    cookieDomain: '.youtube.com',
    cookieAllowlist: [
      '__Secure-1PSID', '__Secure-3PSID', '__Secure-1PSIDTS',
      'LOGIN_INFO', 'VISITOR_INFO1_LIVE', 'VISITOR_PRIVACY_METADATA',
      'SOCS', 'YSC', 'PREF'
    ],
    features: {
      quality2160: true, quality1440: true, audio: true, subtitles: true,
      thumbnail: true, metadata: true, sponsorBlock: true, compatMode: true,
      crop: true, playlist: true, channel: true
    },
    isVideoPage: (url) => /\/(watch\?v=|shorts\/|live\/)/.test(url),
    isChannelPage: (url) => {
      if (/\/(watch\?v=|shorts\/)/.test(url)) return false;
      return /\/(channel\/UC[\w-]{10,}|c\/[^/?#]+|@[\w.-]+|user\/[^/?#]+)(\/(videos|streams|shorts))?\/?(\?.*)?$/.test(url);
    },
    videoAnchorSelectors: ['#movie_player'],
    channelAnchorSelectors: [
      '#channel-header-container', '#channel-header', 'ytd-c4-tabbed-header-renderer',
      'yt-page-header-view-model', '#page-header', '#inner-header-container',
      'ytd-page-header-renderer', 'ytd-browse #header'
    ],
    channelUrlForCommand: (url) => url, // yt-dlp already treats the channel URL itself as a flat playlist
    anchorMode: 'append', // proven working: #movie_player is a real positioned container, safe to append into directly
  },

  twitter: {
    id: 'twitter',
    userNote: "Page layout changes often, so this may need occasional updates.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    // Timelines show many videos/photos per page and the page URL only names the *opened* tweet, so use the
    // same "button follows the media in view" logic as Instagram/Facebook, resolving each tweet's own link.
    feedStyle: true,
    imagePosts: true,
    imageSrcRegex: /pbs\.twimg\.com\/media\//,
    imageUrlTransform: (src) => { try { const u = new URL(src); if (u.searchParams.has('name')) u.searchParams.set('name', 'orig'); return u.href; } catch { return src; } },
    postLinkRegex: /\/status\/\d+/,
    resolvePostUrl: (el) => { const t = el.closest('article')?.querySelector('a[href*="/status/"] time'); const a = t && t.closest('a'); return a ? a.href : null; },
    label: 'Twitter / X',
    matches: ['*://twitter.com/*', '*://x.com/*'],
    cookieDomain: '.x.com',
    cookieAllowlist: ['auth_token', 'ct0'],
    features: {
      quality2160: false, quality1440: false, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: false
    },
    isVideoPage: (url) => /\/status\/\d+/.test(url),
    isChannelPage: () => false, // profile timelines are login-heavy and unreliable to bulk-pull - deliberately off
    videoAnchorSelectors: ['article[data-testid="tweet"] video', 'article video', 'video'],
    preferAncestor: 'article', // VERIFIED via dom-probe: X wraps every tweet in <article> - anchor there, not on the <video> itself
    channelAnchorSelectors: [],
    note: 'X/Twitter changes its DOM structure often; selectors here may need periodic updates.',
  },

  instagram: {
    id: 'instagram',
    userNote: "Works best when you're logged in.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'Instagram',
    matches: ['*://www.instagram.com/*', '*://instagram.com/*'],
    cookieDomain: '.instagram.com',
    cookieAllowlist: ['sessionid', 'csrftoken', 'ds_user_id'],
    features: {
      quality2160: false, quality1440: false, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: false
    },
    isVideoPage: (url) => /\/(reels?|p|tv)\/[\w-]{5,}/.test(url), // 'reels?' - the full-page reel viewer is /reels/<id>/ (plural), which the old pattern missed
    // Used by feed-style sites to find the permalink of the video in view. Must only match a REAL post link
    // (a nav link like /reels/ or a hashtag link must not match).
    postLinkRegex: /\/(?:p|reels?|tv)\/[\w-]{5,}/,
    imagePosts: true, // photo posts: the floating button saves the image in view directly (no yt-dlp involved)
    imageSrcRegex: /cdninstagram\.com|fbcdn\.net/,
    isChannelPage: () => false, // profile grids mix posts/reels/tagged - not a clean bulk-download unit yet
    videoAnchorSelectors: ['main video', 'article video', 'video'],
    preferAncestor: 'main', // VERIFIED via dom-probe: only stable landmark found; everything else is hashed atomic-CSS classes
    feedStyle: true, // VERIFIED via dom-probe: Reels pages prefetch 6-8 <video> elements at once - must pick the one actually in view, not the first in DOM order
    channelAnchorSelectors: [],
    note: 'Most content requires an active login session (sessionid) even to view. No stable CSS selectors exist here today - relies on the geometry-based fallback.',
  },

  tiktok: {
    id: 'tiktok',
    userNote: "Page layout changes often, so this may occasionally break.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'TikTok',
    matches: ['*://www.tiktok.com/*', '*://tiktok.com/*'],
    cookieDomain: '.tiktok.com',
    cookieAllowlist: ['sessionid', 'sid_tt', 'tt_csrf_token'],
    features: {
      quality2160: false, quality1440: false, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: true
    },
    isVideoPage: (url) => /\/@[\w.-]*\/video\/\d+/.test(url),
    postLinkRegex: /\/@[\w.-]*\/video\/\d+/,
    // FIX: the logged-out feed renders NO /@user/video/<id> link inside the post (logs showed only /@user, /tag/*, /music/*
    // anchors), so the generic "find the permalink" strategy can never work there. Read the video id from the player instead.
    resolvePostUrl: (el) => tiktokResolvePostUrl(el),
    hookDiag: () => tiktokLastDiag, // read by content.js so the debug log says which sources voted (or stayed silent)
    unresolvedHint: "Couldn't read this TikTok's video ID. Open the video on its own page (tap the creator's name, then the video) and try again.", // clicking a feed video only pauses it
    isChannelPage: (url) => /\/@[\w.-]+\/?(\?.*)?$/.test(url),
    videoAnchorSelectors: ['[data-e2e="feed-video"]', 'section[data-e2e="feed-video"]', 'video'],
    feedStyle: true, // VERIFIED via dom-probe: home feed prefetches multiple <video> elements simultaneously, same issue as Instagram Reels
    channelAnchorSelectors: ['[data-e2e="user-page"]', '[data-e2e="user-title"]'],
    channelUrlForCommand: (url) => url, // yt-dlp's TikTok user extractor pulls the whole profile
    note: 'TikTok changes markup very frequently - highest ongoing-maintenance risk of this list.',
  },

  facebook: {
    id: 'facebook',
    userNote: "Works best when you're logged in. Facebook can be unreliable.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'Facebook',
    matches: ['*://www.facebook.com/*', '*://facebook.com/*', '*://fb.watch/*'],
    cookieDomain: '.facebook.com',
    cookieAllowlist: ['c_user', 'xs'],
    features: {
      quality2160: false, quality1440: false, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: false
    },
    isVideoPage: (url) => /\/(?:watch\/?\?(?:.*&)?v=\d+|reel\/\d+|videos\/(?:[^/?#]+\/)?\d+|share\/[vr]\/[\w-]+|[^/?#]+\/posts\/[\w-]+|groups\/[^/?#]+\/(?:permalink|posts)\/\d+)/.test(url) || /fb\.watch\//.test(url),
    // reel/<digits> on purpose: /reel/hashtag/?q=... is a hashtag page, not a video. Posts are best-effort (yt-dlp may reject pfbid permalinks).
    postLinkRegex: /\/(?:reel\/\d+|videos\/(?:[^/?#]+\/)?\d+|share\/[vr]\/[\w-]+|[^/?#]+\/posts\/[\w-]+|groups\/[^/?#]+\/(?:permalink|posts)\/\d+|watch\/?\?(?:[^#]*&)?v=\d+)|fb\.watch\/\w+/,
    imagePosts: true, // photo posts: the floating button saves the image in view directly (no yt-dlp involved)
    imageSrcRegex: /fbcdn\.net|fbsbx\.com/,
    isChannelPage: () => false,
    videoAnchorSelectors: ['[data-pagelet="WatchPermalinkVideo"] video', 'video'],
    feedStyle: true, // VERIFIED via dom-probe: Reels pages also prefetch multiple <video> elements at once
    channelAnchorSelectors: [],
    note: 'Facebook\'s DOM is obfuscated and changes often; dom-probe found no stable selector of any kind here - relies entirely on the geometry-based fallback. yt-dlp\'s own Facebook extractor is historically one of its more fragile ones too.',
  },

  reddit: {
    id: 'reddit',
    userNote: "Most videos are public, so no login is needed.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    // Reddit's player is a custom element with its <video> inside a shadow root, which document.querySelectorAll('video') can't see.
    feedStyle: true,
    hostVideoSelector: 'shreddit-player-2, shreddit-player',
    resolvePostUrl: (el) => { const p = el.closest('shreddit-post'); const l = p && (p.getAttribute('permalink') || p.getAttribute('content-href')); return l ? new URL(l, location.origin).href : null; },
    label: 'Reddit',
    matches: ['*://www.reddit.com/*', '*://reddit.com/*', '*://old.reddit.com/*'],
    cookieDomain: '.reddit.com',
    cookieAllowlist: ['reddit_session', 'token_v2'],
    features: {
      quality2160: true, quality1440: true, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: false
    },
    isVideoPage: (url) => /\/r\/[\w-]+\/comments\/[\w-]+/.test(url),
    isChannelPage: () => false,
    videoAnchorSelectors: ['shreddit-player', 'video'],
    channelAnchorSelectors: [],
    note: 'Most v.redd.it posts are public; low cookie dependency compared to the others here. NOT YET VERIFIED: dom-probe was only run on the homepage, not an actual /r/.../comments/... video post - run it there too when possible.',
  },

  twitch: {
    id: 'twitch',
    userNote: "VODs and clips.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'Twitch',
    matches: ['*://www.twitch.tv/*', '*://twitch.tv/*', '*://clips.twitch.tv/*'],
    cookieDomain: '.twitch.tv',
    cookieAllowlist: ['auth-token'],
    features: {
      quality2160: false, quality1440: true, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: true
    },
    isVideoPage: (url) => /\/videos\/\d+/.test(url) || /clips\.twitch\.tv\//.test(url) || /\/[\w]+\/clip\//.test(url),
    isChannelPage: (url) => /^\/[\w]+\/videos(\/all)?\/?(\?.*)?$/.test(new URL(url).pathname + (new URL(url).search || '')),
    videoAnchorSelectors: ['[data-test-selector="video-player__video-container"]', 'video'],
    channelAnchorSelectors: ['[data-a-target="videos-tab"]'],
    channelUrlForCommand: (url) => url,
    note: 'Twitch also supports downloading chat replay (--write-comments) - not wired into the UI yet, listed as a future site-specific extra.',
  },

  vimeo: {
    id: 'vimeo',
    label: 'Vimeo',
    matches: ['*://vimeo.com/*', '*://player.vimeo.com/*'],
    cookieDomain: '.vimeo.com',
    cookieAllowlist: ['vuid'],
    features: {
      quality2160: true, quality1440: true, audio: true, subtitles: true,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: true
    },
    isVideoPage: (url) => /^\/(?:video\/)?\d+/.test(new URL(url).pathname),
    isChannelPage: (url) => /^\/[\w-]+\/videos\/?$/.test(new URL(url).pathname),
    videoAnchorSelectors: ['[data-testid="vh-player-container"]', '.vp-video-wrapper video', 'video'],
    channelAnchorSelectors: [],
    channelUrlForCommand: (url) => url,
  },

  soundcloud: {
    id: 'soundcloud',
    userNote: "Audio only.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'SoundCloud',
    matches: ['*://soundcloud.com/*'],
    cookieDomain: '.soundcloud.com',
    cookieAllowlist: ['oauth_token'],
    audioOnly: true, // no video quality concept at all - UI should skip straight to audio format
    features: {
      quality2160: false, quality1440: false, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: false,
      crop: true, playlist: false, channel: true
    },
    isVideoPage: (url) => /^\/[\w-]+\/[\w-]+\/?$/.test(new URL(url).pathname),
    isChannelPage: (url) => /^\/[\w-]+\/?$/.test(new URL(url).pathname),
    videoAnchorSelectors: ['.sc-button-share', '.soundActions', '.playControls'],
    anchorMode: 'after', // the verified anchor is a <button> itself - append INTO it would be invalid, insert as a sibling instead
    channelAnchorSelectors: [],
    channelUrlForCommand: (url) => url,
  },
};

/**
 * TikTok feed: work out the permalink of the video in view WITHOUT relying on a link in the DOM.
 *
 * Design goal: survive TikTok's front-end churn without code updates. So nothing here depends on one selector.
 * Instead several INDEPENDENT sources each nominate a video id, every nomination is sanity-checked, and the
 * sources vote:
 *   anchor        a real /@user/video/<id> link inside the post                         (weight 10)
 *   xgwrapper     the player wrapper's own id, <div id="xgwrapper-<n>-<videoId>">         (weight 4)
 *   attr:<name>   any attribute whose NAME says id (data-more-menu-item-id, data-video-id...) (weight 4)
 *   react-fiber   the item object in React's props, found by SHAPE not by prop name       (weight 4, +3 if its
 *                 video.duration matches this <video>'s duration - proves it is THIS video)
 *   attr-any      any other long number in an attribute                                    (weight 1)
 * Sanity checks: a TikTok id is a snowflake - its top 32 bits are a unix timestamp, so a number that does not
 * decode to a plausible date is rejected; ids that appear in /music/, /tag/ or /@user links are rejected (the
 * music id on the page is a different number from the video id).
 * Everything considered goes into tiktokLastDiag, which content.js writes to the debug log - so if TikTok
 * changes and this ever stops working, the log says WHICH source went quiet instead of just "none".
 * yt-dlp accepts https://www.tiktok.com/@/video/<id>, so a missing author handle is not fatal.
 */
let tiktokLastDiag = null;

function tiktokIsPlausibleId(id) {
  if (!/^\d{17,20}$/.test(id)) return false;
  try {
    const ts = Number(BigInt(id) >> 32n); // snowflake: high 32 bits = creation time (unix seconds)
    return ts >= 1472688000 && ts <= Date.now() / 1000 + 2 * 86400; // after Sep 2016, not in the future
  } catch { return false; }
}

function tiktokResolvePostUrl(videoEl) {
  const diag = { sources: {}, rejected: [], pick: null, total: 0 };
  tiktokLastDiag = diag;

  // On a real /@user/video/<id> page the URL itself is authoritative - let content.js's generic page-url strategy win.
  if (YTDLP_SITES.tiktok.isVideoPage(location.href)) { diag.pick = 'page-url'; return null; }

  // The post that owns this video: TikTok's own test hooks first, then climb until a second <video> shows up.
  // OUTERMOST container wins: <section data-e2e="feed-video"> sits INSIDE the <article>, and the author/music links and the
  // "more" button (which carries the id) are siblings of that section, not children - the inner one would hide them.
  let post = videoEl.closest('article') || videoEl.closest('[data-e2e="recommend-list-item-container"]') || videoEl.closest('[data-e2e="feed-video"]');
  if (!post) {
    let node = videoEl; post = videoEl.parentElement || videoEl;
    for (let i = 0; i < 30 && node.parentElement; i++) { node = node.parentElement; if (node.querySelectorAll('video').length > 1) break; post = node; }
  }
  const nodes = [post, ...Array.from(post.querySelectorAll('*')).slice(0, 900)];

  // ids that are NOT the video's: whatever shows up in music / tag / user / non-video links.
  const blocked = new Set();
  const NUM = /(?<![\d.])\d{17,20}(?![\d.])/g;
  post.querySelectorAll('a[href]').forEach((a) => {
    const h = a.getAttribute('href') || '';
    if (/\/video\/\d/.test(h)) return;
    (h.match(NUM) || []).forEach((n) => blocked.add(n));
  });

  const votes = new Map(); // id -> { total, sources: [] }
  const vote = (id, source, weight) => {
    if (!tiktokIsPlausibleId(id)) { if (diag.rejected.length < 6) diag.rejected.push({ id: String(id).slice(0, 24), source, why: 'not-a-snowflake' }); return; }
    if (blocked.has(id)) { if (diag.rejected.length < 6) diag.rejected.push({ id, source, why: 'seen-in-music/tag/user-link' }); return; }
    const v = votes.get(id) || { total: 0, sources: [] };
    v.total += weight; v.sources.push(source);
    votes.set(id, v);
    diag.sources[source] = (diag.sources[source] || 0) + 1;
  };

  // 1. a real permalink anchor
  let anchorAuthor = '';
  for (const a of post.querySelectorAll('a[href*="/video/"]')) {
    const m = (a.getAttribute('href') || '').match(/\/@([\w.-]*)\/video\/(\d{17,20})/);
    if (m) { vote(m[2], 'anchor', 10); anchorAuthor = m[1]; break; }
  }

  // 2. player wrapper id (xgplayer) - on the video's own ancestors, any ancestor id carrying an id-like number counts
  let wrapperHit = false;
  for (let p = videoEl, i = 0; p && p !== post.parentElement && i < 14; p = p.parentElement, i++) {
    if (!p.id) continue;
    const m = p.id.match(/^xgwrapper-\d+-(\d{17,20})$/);
    if (m) { vote(m[1], 'xgwrapper', 4); wrapperHit = true; break; }
  }
  if (!wrapperHit) {
    for (let p = videoEl, i = 0; p && p !== post.parentElement && i < 14; p = p.parentElement, i++) {
      const m = p.id && p.id.match(NUM);
      if (m) { vote(m[0], 'ancestor-id', 3); break; }
    }
  }

  // 3. attributes anywhere in the post (names that say "id" are trusted, anything else is a weak hint)
  const ID_NAME = /(video|item|aweme|vid|post|media)[-_]?id/i;
  const ownAncestors = new Set(); // their `id` attribute was already judged by the wrapper pass above - don't count the same signal twice
  for (let p = videoEl; p; p = p.parentElement) ownAncestors.add(p);
  for (const n of nodes) {
    for (const attr of n.attributes || []) {
      if (/^(href|src|srcset|style|class|d|points|viewbox|transform)$/i.test(attr.name)) continue;
      if (attr.name === 'id' && ownAncestors.has(n)) continue;
      if (attr.name.startsWith('data-ytdlp-')) continue; // our own helper's output from an earlier call - never feed it back in as evidence (TikTok recycles elements, it may be stale)
      if (attr.value.length > 200) continue;
      const found = attr.value.match(NUM);
      if (!found) continue;
      const trusted = ID_NAME.test(attr.name);
      found.forEach((id) => vote(id, trusted ? 'attr:' + attr.name.replace(/^data-/, '') : 'attr-any', trusted ? 4 : 1));
    }
  }

  // 4. React props, read by tiktok-main.js in the MAIN world (needs the page's own JS objects; the isolated world can't see them)
  let fiberItems = [];
  try {
    videoEl.dispatchEvent(new CustomEvent('ytdlp-resolve-tiktok'));
    const raw = videoEl.getAttribute('data-ytdlp-items');
    diag.mainWorld = raw ? 'items' : (videoEl.getAttribute('data-ytdlp-miss') || 'no-response'); // 'no-response' = helper script not running
    if (raw) fiberItems = JSON.parse(raw);
  } catch (e) { diag.mainWorld = 'error:' + String(e && e.message || e).slice(0, 80); }
  const vdur = isFinite(videoEl.duration) ? videoEl.duration : null;
  const authorOf = new Map();
  for (const it of fiberItems) {
    const known = vdur != null && it.dur != null;
    if (known && Math.abs(it.dur - vdur) > 1.5) { // same-looking item but a different length: it describes ANOTHER video (a neighbour in the list) - disproved, no vote
      if (diag.rejected.length < 6) diag.rejected.push({ id: String(it.id), source: 'react-fiber', why: `duration ${it.dur}s != video ${Math.round(vdur)}s` });
      continue;
    }
    vote(String(it.id), 'react-fiber', 4);
    if (known) vote(String(it.id), 'react-fiber+duration', 3);
    if (it.author) authorOf.set(String(it.id), it.author);
  }

  // decide
  const ranked = [...votes.entries()].sort((a, b) => b[1].total - a[1].total);
  diag.candidates = ranked.slice(0, 4).map(([id, v]) => ({ id, total: v.total, sources: v.sources.join(',') }));
  if (!ranked.length) return null;
  const [id, best] = ranked[0];
  const runnerUp = ranked[1];
  if (runnerUp && runnerUp[1].total * 2 > best.total) diag.conflict = true; // two ids with comparable support - logged, best one still wins
  if (best.total < 3 && ranked.length > 1) return null;  // only weak hints and they disagree: refuse rather than download the wrong video
  diag.pick = id; diag.total = best.total;

  const handle = () => {
    if (anchorAuthor) return anchorAuthor;
    if (authorOf.get(id)) return authorOf.get(id);
    const pick = (sel) => { const e = post.querySelector(sel); const m = e && (e.getAttribute('href') || '').match(/\/@([\w.-]+)\/?(?:[?#].*)?$/); return m ? m[1] : ''; };
    return pick('a[data-e2e="video-author-avatar"]') || pick('a[data-e2e="browser-username"]') || pick('a[href^="/@"], a[href*="tiktok.com/@"]') || '';
  };
  return `https://www.tiktok.com/@${handle()}/video/${id}`;
}

/** Returns the site config whose `matches` cover this URL, or null. */
function getSiteForUrl(url) {
  if (!url) return null;
  for (const site of Object.values(YTDLP_SITES)) {
    for (const pattern of site.matches) {
      if (matchPattern(pattern, url)) return site;
    }
  }
  return null;
}

/** Minimal Chrome match-pattern ("*://*.example.com/*") tester - no deps. */
function matchPattern(pattern, url) {
  try {
    const re = new RegExp(
      '^' + pattern
        .replace(/[.+^${}()|[\]\\]/g, '\\$&') // escape regex specials
        .replace(/\*/g, '.*') + '$'
    );
    return re.test(url);
  } catch {
    return false;
  }
}

function getAllSites() {
  return Object.values(YTDLP_SITES);
}

// Expose to both window (content script / popup) and self (service worker) scopes.
if (typeof window !== 'undefined') {
  window.YTDLP_SITES = YTDLP_SITES;
  window.getSiteForUrl = getSiteForUrl;
  window.getAllSites = getAllSites;
}
if (typeof self !== 'undefined') {
  self.YTDLP_SITES = YTDLP_SITES;
  self.getSiteForUrl = getSiteForUrl;
  self.getAllSites = getAllSites;
}