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
    isVideoPage: (url) => /\/(watch\?v=|shorts\/)/.test(url),
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
    isVideoPage: (url) => /\/@[\w.-]+\/video\/\d+/.test(url),
    postLinkRegex: /\/@[\w.-]+\/video\/\d+/,
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
    postLinkRegex: /\/(?:reel\/\d+|videos\/(?:[^/?#]+\/)?\d+|share\/[vr]\/[\w-]+|[^/?#]+\/posts\/[\w-]+|groups\/[^/?#]+\/(?:permalink|posts)\/\d+|watch\/?\?(?:[^#]*&)?v=\d+|story\.php\?(?:[^#]*&)?story_fbid=\d+)|fb\.watch\/\w+/,
    imagePosts: true, // photo posts: the floating button saves the image in view directly (no yt-dlp involved)
    isChannelPage: () => false,
    videoAnchorSelectors: ['[data-pagelet="WatchPermalinkVideo"] video', 'video'],
    feedStyle: true, // VERIFIED via dom-probe: Reels pages also prefetch multiple <video> elements at once
    channelAnchorSelectors: [],
    note: 'Facebook\'s DOM is obfuscated and changes often; dom-probe found no stable selector of any kind here - relies entirely on the geometry-based fallback. yt-dlp\'s own Facebook extractor is historically one of its more fragile ones too.',
  },

  reddit: {
    id: 'reddit',
    userNote: "Most videos are public, so no login is needed.", // shown in the popup's Supported Sites list (plain language; `note` below is developer-facing)
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
    isVideoPage: (url) => /^\/\d+(\?.*)?$/.test(new URL(url).pathname),
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