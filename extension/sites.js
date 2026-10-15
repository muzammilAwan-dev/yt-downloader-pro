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
    // yt-dlp only treats a session as logged in (and then uses the logged-in client list + SAPISIDHASH auth header)
    // when it finds SAPISID / __Secure-3PAPISID. The old minimal allowlist left those out, so yt-dlp saw a guest
    // session and the bot check was not bypassed. Google rotates and renames its auth cookies, so send everything
    // except analytics noise / oversized values (see ytdlpSelectCookies) instead of chasing names.
    cookieAllowAll: true,
    cookieAllowlist: [   // only used if cookieAllowAll is turned off; must keep the *APISID cookies
      'SID', 'HSID', 'SSID', 'APISID', 'SAPISID',
      '__Secure-1PAPISID', '__Secure-3PAPISID',
      '__Secure-1PSID', '__Secure-3PSID', '__Secure-1PSIDTS', '__Secure-3PSIDTS',
      '__Secure-1PSIDCC', '__Secure-3PSIDCC', 'SIDCC',
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
    channelAnchorSelectors: ['[data-e2e="user-page"] [data-e2e="user-bio"]', '[data-e2e="user-page"] [data-e2e="user-subtitle"]', '[data-e2e="user-title"]', '[data-e2e="user-page"]'], // only used if channelFloating is turned off
    // FIX: a button inserted next to TikTok's profile header depends on a layout that changes constantly, and a button in the wrong place or
    // clipped away looks exactly like "the feature doesn't exist". Float it in a fixed corner instead - independent of the page's markup.
    channelFloating: true,
    // yt-dlp wants the bare profile URL: no ?lang=, ?is_from_webapp= or tab parameters.
    channelUrlForCommand: (url) => { try { const u = new URL(url); const m = u.pathname.match(/^\/(@[\w.-]+)/); return m ? `${u.origin}/${m[1]}` : url; } catch { return url; } },
    // Direct links to this profile's videos that are loaded in the page right now. Handed to yt-dlp NEXT TO the profile URL, so if
    // yt-dlp's profile extractor fails (it needs the secUid + browser impersonation), the videos we can already see still download.
    // Videos by other accounts (suggestions, Reposts tab) are filtered out by handle.
    collectChannelItemUrls: () => {
      const handle = (location.pathname.match(/^\/@([\w.-]+)/) || [])[1];
      if (!handle) return [];
      const seen = new Set(), out = [];
      document.querySelectorAll('a[href*="/video/"]').forEach((a) => {
        const m = (a.getAttribute('href') || '').match(/\/@([\w.-]+)\/video\/(\d{17,20})/);
        if (m && m[1].toLowerCase() === handle.toLowerCase() && !seen.has(m[2])) { seen.add(m[2]); out.push(`https://www.tiktok.com/@${m[1]}/video/${m[2]}`); }
      });
      return out;
    },
    // TikTok titles are just the caption: many videos share one ("#fyp"), and yt-dlp silently SKIPS a file whose name already exists.
    // Put the video id in the file name so every video gets its own file.
    uniqueFilenames: true,
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
    // Image / gallery posts: same "button follows the media in view" logic as Instagram/X, saving the image directly (no yt-dlp).
    // Only Reddit's own image hosts count - external-preview.redd.it is a link/video thumbnail, not the post's media.
    imagePosts: true,
    imageSrcRegex: /^https?:\/\/(?:preview|i)\.redd\.it\//,
    // The feed serves a resized, signed copy: preview.redd.it/<caption-slug>-v0-<id>.jpg?width=640&auto=webp&s=...
    // The original lives at i.redd.it/<id>.jpg - the caption slug is NOT part of that address (keeping it gives a 404 page).
    // Reddit also keeps that exact address in a hidden lightbox <img> next to the visible one, so prefer reading it from there
    // and only fall back to rebuilding it from the preview name.
    imageOriginalSrc: (img) => {
      const box = img.closest('figure, shreddit-aspect-ratio, gallery-carousel, li') || img.parentElement;
      const o = box && box.querySelector('img[src^="https://i.redd.it/"]');
      return o ? o.src : null;
    },
    imageUrlTransform: (src) => {
      try {
        const u = new URL(src);
        if (u.hostname !== 'preview.redd.it') return u.origin + u.pathname;
        const m = /-v0-([A-Za-z0-9]+)(\.\w+)$/.exec(u.pathname);
        return m ? `https://i.redd.it/${m[1]}${m[2]}` : `https://i.redd.it${u.pathname}`;
      } catch { return src; }
    },
    // Image posts sit in <shreddit-post>, galleries in <gallery-carousel>; either may keep its <img> inside an (open) shadow root.
    imageShadowHosts: 'shreddit-post, gallery-carousel, shreddit-media-lightbox-listener, faceplate-img',
    // Reddit paints a blurred, enlarged copy of the image behind it - never the thing the user wants to save.
    imageReject: (img) => { try { return img.getAttribute('aria-hidden') === 'true' || /blur/i.test(getComputedStyle(img).filter) || !!img.closest('[class*="background-image"], [class*="blur"]'); } catch { return false; } },
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
    userNote: "Live streams, VODs and clips.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
    label: 'Twitch',
    matches: ['*://www.twitch.tv/*', '*://twitch.tv/*', '*://clips.twitch.tv/*'],
    cookieDomain: '.twitch.tv',
    cookieAllowlist: ['auth-token'],
    features: {
      quality2160: false, quality1440: true, audio: true, subtitles: false,
      thumbnail: true, metadata: true, sponsorBlock: false, compatMode: true,
      crop: true, playlist: false, channel: true
    },
    // VERIFIED via probe (v4, home + live channel): the <video> (aria-label "Twitch video player") sits inside div[data-a-target=video-player];
    // it is a plain light-DOM element with no src (MSE), so the button floats over whatever player is in view - the same
    // "button follows the media in view" logic as the feed sites. That covers the live channel page, the home-page carousel and the
    // mini player that keeps playing while you browse. The link of what is playing comes from twitchResolvePostUrl (below).
    feedStyle: true,
    resolvePostUrl: (el) => twitchResolvePostUrl(el),
    hookDiag: () => twitchLastDiag,
    unresolvedHint: "Couldn't tell which channel this player belongs to. Open the channel (or the video / clip) on its own page and try again.",
    // VODs and clips: the page URL is the item. Live channels are resolved by the hook (path -> channel login).
    isVideoPage: (url) => /\/videos\/\d+/.test(url) || /clips\.twitch\.tv\//.test(url) || /\/[\w]+\/clip\//.test(url),
    // /<channel>/videos and /<channel>/clips are the bulk-download pages (yt-dlp handles both as playlists)
    isChannelPage: (url) => { try { return /^\/[\w]+\/(?:videos|clips)(?:\/all)?\/?$/.test(new URL(url).pathname); } catch { return false; } },
    videoAnchorSelectors: ['[data-a-target="video-player"]', '[data-test-selector="video-player__video-container"]', 'video'],
    // Twitch's tab row isn't verified (probe was taken on the live page, which has no Videos tab), so the bulk button floats - bottom-LEFT so
    // it can't sit on top of the chat box that lives at the bottom-right.
    cropKeyframeCuts: false, // HLS: cutting with --force-keyframes-at-cuts re-encodes (0.1x speed, MPEG-2 output) - stream-copy instead
    channelFloating: true,
    channelFloatCorner: 'left',
    channelAnchorSelectors: ['[data-a-target="videos-tab"]'], // only used if channelFloating is turned off
    channelUrlForCommand: (url) => { try { const u = new URL(url); return u.origin + u.pathname.replace(/\/$/, ''); } catch { return url; } }, // drop ?filter=&sort= tracking-ish query; yt-dlp lists everything
    note: 'Live recording is open-ended: yt-dlp records until the stream ends or the task is stopped in the app. Chat replay (--write-comments) is not wired into the UI yet.',
  },

  vimeo: {
    id: 'vimeo',
    label: 'Vimeo',
    matches: ['*://vimeo.com/*', '*://player.vimeo.com/*'],
    cookieDomain: '.vimeo.com',
    // VERIFIED (user's host log, Oct 2026): yt-dlp's Vimeo extractor now fails anonymously with "The web client only works when logged-in".
    // It needs the logged-in browser session. Which cookie carries the login isn't documented, so rather than hard-code one name
    // (brittle) we forward every Vimeo cookie except analytics/consent ones, size-capped (see ytdlpSelectCookies below).
    cookieAllowAll: true,
    alwaysCookies: true, // send them on every Vimeo download - the "Bypass Age & Bot Lock" toggle is not needed (or even sensible) here
    cookieAllowlist: ['vuid'],   // only used if cookieAllowAll is turned off
    userNote: 'Needs you to be logged in to vimeo.com in this browser (Vimeo now blocks anonymous downloads).',
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
  const VIDLINK = 'a[href*="/video/"]';
  let post = videoEl.closest('article') || videoEl.closest('[data-e2e="recommend-list-item-container"]') ||
             videoEl.closest('[data-e2e="user-post-item"]') || videoEl.closest('[data-e2e="feed-video"]');
  if (!post) {
    // Unknown layout (grids, search results, ...). Climb only while it is still ONE item: stop before an ancestor that holds
    // another <video> or links to other videos - otherwise we end up at <html> and "the post" is the whole page.
    let node = videoEl; post = videoEl.parentElement || videoEl;
    for (let i = 0; i < 12 && node.parentElement; i++) {
      node = node.parentElement;
      if (node.querySelectorAll('video').length > 1) break;
      const seen = new Set();
      node.querySelectorAll(VIDLINK).forEach((a) => { const m = (a.getAttribute('href') || '').match(/\/video\/(\d{17,20})/); if (m) seen.add(m[1]); });
      if (seen.size > 1) break;
      post = node;
    }
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

  // 1. a real permalink. Best case: the player sits INSIDE its own <a href=".../video/<id>"> (profile grids, search results, related videos).
  //    Otherwise links inside the post count only if they all point at ONE video - a grid or list holds many, and "the first one" is a guess.
  let anchorAuthor = '';
  const linkOf = (a) => (a.getAttribute('href') || '').match(/\/@([\w.-]*)\/video\/(\d{17,20})/);
  const ownLink = videoEl.closest(VIDLINK);
  const own = ownLink && linkOf(ownLink);
  if (own) { vote(own[2], 'own-link', 10); anchorAuthor = own[1]; }
  else {
    const found = new Map();
    post.querySelectorAll(VIDLINK).forEach((a) => { const m = linkOf(a); if (m) found.set(m[2], m[1]); });
    if (found.size === 1) { const [id, au] = [...found][0]; vote(id, 'anchor', 10); anchorAuthor = au; }
    else found.forEach((au, id) => vote(id, 'anchor-multi', 1)); // many candidates: weak hints only, the player's own id decides
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

// ---------------------------------------------------------------------------------------------------------------------
// Twitch: which channel / video is the player in view playing?
// Pages: /<channel> (live), /<channel>/videos|about|schedule (live player stays on top), /videos/<id> (VOD), /<channel>/clip/<slug>,
// home page (a featured stream autoplays in a carousel), and a mini player that keeps playing while you browse elsewhere.
// ---------------------------------------------------------------------------------------------------------------------
let twitchLastDiag = null;

// Top-level paths that are Twitch pages, not channels (a channel login is 3-25 chars of a-z 0-9 _).
const TWITCH_RESERVED = new Set(['directory', 'videos', 'video', 'settings', 'downloads', 'p', 'popout', 'embed', 'subscriptions', 'inventory', 'drops',
  'wallet', 'friends', 'turbo', 'jobs', 'store', 'prime', 'search', 'u', 'moderator', 'dashboard', 'broadcast', 'team', 'event', 'events', 'schedule',
  'following', 'privacy', 'legal', 'login', 'signup', 'payments', 'subs', 'bits', 'gift', 'help', 'about', 'partner', 'creatorcamp', 'turbo', 'ads', 'clips', 'all']);

function twitchLoginFromHref(href) {
  try {
    const u = new URL(href, 'https://www.twitch.tv');
    if (!/(^|\.)twitch\.tv$/.test(u.hostname)) return null;
    const m = /^\/([A-Za-z0-9_]{3,25})(?:\/(?:home|about|schedule|videos|clips|chat))?\/?$/.exec(u.pathname);
    return m && !TWITCH_RESERVED.has(m[1].toLowerCase()) ? m[1] : null;
  } catch { return null; }
}

function twitchResolvePostUrl(el) {
  const diag = { tried: [], pick: null };
  twitchLastDiag = diag;
  const done = (login, via) => { diag.pick = login ? { login, via } : null; return login ? `https://www.twitch.tv/${login}` : null; };

  // 1. a VOD / clip page: the page URL is the item - return nothing so the generic page-URL rule applies
  if (YTDLP_SITES.twitch.isVideoPage(location.href)) { diag.tried.push('item-page'); return null; }

  // 2. a channel page: the player on it is that channel's live stream
  const fromPath = twitchLoginFromHref(location.href);
  diag.tried.push('path:' + (fromPath || '-'));
  if (fromPath) return done(fromPath, 'path');

  // 3. a card / carousel item around the player (home page featured stream): the first channel link inside it
  const card = el.closest('[data-a-target="featured-item"], [data-a-target^="card-"], article');
  if (card) {
    for (const a of card.querySelectorAll('a[href]')) {
      const login = twitchLoginFromHref(a.getAttribute('href'));
      if (login) { diag.tried.push('card-link'); return done(login, 'card-link'); }
    }
    diag.tried.push('card-link:none');
  }

  // 4. the page's own data: Twitch's player components carry the channel login as a prop (found by name, not by position)
  try {
    let fiberKey = null, start = null;
    for (let p = el, i = 0; p && i < 30 && !start; p = p.parentElement, i++) {
      fiberKey = Object.keys(p).find((k) => k.startsWith('__reactFiber$'));
      if (fiberKey) start = p[fiberKey];
    }
    let hops = 0;
    for (let f = start; f && hops < 90; f = f.return, hops++) {
      const pr = f.memoizedProps;
      if (!pr || typeof pr !== 'object') continue;
      for (const v of [pr.channelLogin, pr.channel && pr.channel.login, pr.channelName]) {
        if (typeof v === 'string' && /^[A-Za-z0-9_]{3,25}$/.test(v) && !TWITCH_RESERVED.has(v.toLowerCase())) { diag.tried.push('react@' + hops); return done(v, 'react'); }
      }
    }
    diag.tried.push(start ? 'react:none' : 'react:no-fiber');
  } catch (e) { diag.tried.push('react:error'); }
  return done(null);
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

// Cookies that are pure analytics / consent banners: never needed by yt-dlp, and they bloat the ytdlp:// link.
const COOKIE_NOISE_RE = /^(_ga|_gid|_gat|_gcl_|_fbp|_fbc|_hj|_dd_|_scid|_uet|_clck|_clsk|_pin_|_tt_|__stripe|ajs_|amp_|mp_|optanon)/i;
const COOKIE_MAX_VALUE = 1500;   // a single cookie bigger than this is tracking/state, not a session token
const COOKIE_MAX_TOTAL = 7000;   // raw chars of name+value kept in total; keeps the whole link far below the ~32k Windows command-line limit

/**
 * Picks which of the browser's cookies for a site go to yt-dlp.
 * - default: only the names in site.cookieAllowlist (minimal exposure; used by every site that has a known login cookie)
 * - site.cookieAllowAll: everything except analytics/consent noise and oversized values; if still over budget the largest go first
 *   (session tokens are small, so they are the last to be dropped)
 */
function ytdlpSelectCookies(site, cookies) {
  const list = Array.isArray(cookies) ? cookies : [];
  if (!site || !site.cookieAllowAll) {
    const allow = (site && site.cookieAllowlist) || [];
    return list.filter(c => allow.includes(c.name));
  }
  const kept = list
    .filter(c => c && c.name && !COOKIE_NOISE_RE.test(c.name) && String(c.value || '').length <= COOKIE_MAX_VALUE)
    .sort((a, b) => (a.name.length + String(a.value).length) - (b.name.length + String(b.value).length)); // smallest first
  const out = [];
  let used = 0;
  for (const c of kept) {
    const size = c.name.length + String(c.value).length;
    if (used + size > COOKIE_MAX_TOTAL) continue;
    used += size;
    out.push(c);
  }
  return out;
}

// Expose to both window (content script / popup) and self (service worker) scopes.
if (typeof window !== 'undefined') {
  window.YTDLP_SITES = YTDLP_SITES;
  window.getSiteForUrl = getSiteForUrl;
  window.getAllSites = getAllSites;
  window.ytdlpSelectCookies = ytdlpSelectCookies;
}
if (typeof self !== 'undefined') {
  self.YTDLP_SITES = YTDLP_SITES;
  self.getSiteForUrl = getSiteForUrl;
  self.getAllSites = getAllSites;
  self.ytdlpSelectCookies = ytdlpSelectCookies;
}