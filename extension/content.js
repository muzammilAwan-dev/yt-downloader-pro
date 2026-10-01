/**
 * @fileoverview DOM Injection Controller
 * Site-agnostic: reads everything about "where am I / what can I do here"
 * from sites.js's SITE config, so adding a new site never touches this file.
 * @version 6.2.0
 */

(function() {
  'use strict';

  const CONFIG = {
    CONTAINER_ID: 'yt-dlp-container',
    CHANNEL_CONTAINER_ID: 'yt-dlp-channel-container',
  };

  // Bail out entirely if this page isn't one of our configured sites.
  // Every site here is declared statically in the manifest (like YouTube
  // always was) so this always has a match on a supported site's pages.
  const SITE = (typeof getSiteForUrl === 'function') ? getSiteForUrl(window.location.href) : null;
  if (!SITE) return;

  // Every configured site is active automatically, same as YouTube always
  // was - this only stops it if the user explicitly turned this one site
  // off in the popup's Settings panel.
  chrome.storage.sync.get('disabledSites', (res) => {
    if ((res.disabledSites || []).includes(SITE.id)) return;
    initialize();
  });

  function initialize() {
    enforceButtonPresence();

    // FIX: replaced a setInterval(..., 1000) poll with a MutationObserver.
    // Polling every second made sense for one SPA; doing that on every one
    // of several more SPA sites at once is wasteful and visibly laggy.
    // Debounced so a burst of DOM mutations (typical on React/SPA sites)
    // only triggers one recheck, not dozens.
    let debounceHandle = null;
    const observer = new MutationObserver(() => {
      clearTimeout(debounceHandle);
      debounceHandle = setTimeout(enforceButtonPresence, 150);
    });
    observer.observe(document.body, { childList: true, subtree: true });

    // SPA route changes often don't fire any DOM mutation near the anchor
    // point (e.g. YouTube's own navigation events) - also recheck on those.
    window.addEventListener('yt-navigate-finish', enforceButtonPresence);
    window.addEventListener('popstate', enforceButtonPresence);

    // FIX: two real cases the MutationObserver above doesn't reliably catch:
    //  1. Opening a reel from a feed as a modal/lightbox (Instagram) - the
    //     video element can exist in the DOM before it's actually sized/
    //     ready, so the mutation-triggered check can run too early and see
    //     a 0-area video, then nothing re-checks once it's actually ready.
    //  2. Swiping to the next reel in an already-open reels view (Facebook,
    //     Instagram) - the NEXT video often just starts playing in place
    //     without any new DOM node being inserted at all, so there's no
    //     mutation to observe in the first place.
    // 'loadedmetadata'/'play' on a <video> don't bubble, but a capture-phase
    // listener on document still sees every one on its way down to the
    // target, regardless of bubbling - this catches both cases precisely,
    // without resorting to blind polling.
    let mediaEventHandle = null;
    const onMediaEvent = () => {
      clearTimeout(mediaEventHandle);
      mediaEventHandle = setTimeout(enforceButtonPresence, 120);
    };
    document.addEventListener('loadedmetadata', onMediaEvent, true);
    document.addEventListener('play', onMediaEvent, true);

    // FIX: scrolling a feed into view over an already-rendered video (no
    // DOM mutation, nothing loads) also needs a full recheck, not just a
    // reposition of whatever button already exists.
    let scrollHandle = null;
    window.addEventListener('scroll', () => {
      clearTimeout(scrollHandle);
      scrollHandle = setTimeout(enforceButtonPresence, 200);
    }, { passive: true, capture: true });
  }

  function findFirstMatch(selectors) {
    for (const sel of selectors) {
      try {
        const el = document.querySelector(sel);
        if (el) return el;
      } catch { /* invalid selector for this DOM, skip */ }
    }
    return null;
  }

  /**
   * How much of `rect` actually overlaps the current viewport, as a 0-1
   * fraction of the element's own area. Used to pick the video that's
   * actually on screen out of several loaded at once (feed-style sites).
   */
  function visibleAreaRatio(rect) {
    const vw = window.innerWidth, vh = window.innerHeight;
    const visibleW = Math.max(0, Math.min(rect.right, vw) - Math.max(rect.left, 0));
    const visibleH = Math.max(0, Math.min(rect.bottom, vh) - Math.max(rect.top, 0));
    const ownArea = rect.width * rect.height;
    if (ownArea <= 0) return 0;
    return (visibleW * visibleH) / ownArea;
  }

  /**
   * Structural fallback for when no curated selector matches (a layout
   * change, or a site with no stable selectors at all - see sites.js notes
   * for Instagram/Facebook). Every video-hosting site must emit a real
   * <video> element to play anything, so find the one most visible right
   * now (not just the first in DOM order - feed-style sites like Instagram
   * Reels/TikTok keep several loaded simultaneously) and walk up a few
   * ancestors to something roomier than the bare tag.
   */
  function findBestVisibleVideo() {
    const videos = Array.from(document.querySelectorAll('video'));
    let best = null, bestRatio = 0;
    for (const v of videos) {
      const ratio = visibleAreaRatio(v.getBoundingClientRect());
      if (ratio > bestRatio) { bestRatio = ratio; best = v; }
    }
    return bestRatio > 0.4 ? best : null; // require it to be substantially on-screen
  }

  /**
   * FIX: several sites (Instagram home feed, Facebook home feed, Reddit
   * home feed) embed a fully playable video directly in the feed with the
   * URL never changing - SITE.isVideoPage(url) alone can never catch that,
   * since there's no dedicated "video page" URL to match. This checks
   * "is there actually a substantially-visible video right now" instead,
   * as a second, URL-independent signal.
   *
   * findBestVisibleVideo() only sees real <video> tags. Some sites (Reddit's
   * shreddit-player) wrap the real <video> in a custom element, likely
   * behind shadow DOM, invisible to a plain 'video' query - so this also
   * checks the site's own curated selectors directly and accepts a match
   * if THAT element is substantially on screen, even with no <video> tag
   * visible to us at all.
   */
  function isVideoContentVisible() {
    if (findBestVisibleVideo()) return true;
    for (const sel of SITE.videoAnchorSelectors) {
      try {
        const el = document.querySelector(sel);
        if (el && visibleAreaRatio(el.getBoundingClientRect()) > 0.4) return true;
      } catch { /* invalid selector for this DOM, skip */ }
    }
    return false;
  }

  // ===========================================================================
  // FEED WIDGET (instagram / facebook / tiktok - any site marked feedStyle)
  //
  // Per-video inline buttons fundamentally don't work on these: content is
  // virtualized/prefetched off-screen, swiping to the next item often swaps
  // the video in place with no DOM mutation, and reel modals can render
  // before they're sized. Chasing each of those individually is a losing
  // game - IDM and similar tools don't try to anchor a button to any
  // specific video's spot in the page; they scan for every video that
  // exists and list them in their OWN fixed UI, decoupled from the host
  // page's layout entirely. Doing the same thing here.
  // ===========================================================================

  const feedSeen = new Map(); // dedupe key -> { el, url, label, row }
  const permalinkCache = new WeakMap(); // video element -> resolved permalink (or null), computed once per element

  /**
   * The current page URL is just the feed - not any specific post. Find
   * that post's own permalink by looking for a nearby <a> whose href looks
   * like a post/reel/video URL, since that's the actual yt-dlp target.
   */
  function findPostPermalink(videoEl) {
    if (permalinkCache.has(videoEl)) return permalinkCache.get(videoEl);
    const linkSelector = 'a[href*="/p/"], a[href*="/reel/"], a[href*="/tv/"], a[href*="/videos/"], a[href*="/watch"]';
    let found = null;
    let node = videoEl;
    for (let i = 0; i < 8 && node; i++) {
      if (node.matches && node.matches(linkSelector)) { found = node.href; break; }
      const link = node.querySelector ? node.querySelector(linkSelector) : null;
      if (link && link.href) { found = link.href; break; }
      node = node.parentElement;
    }
    permalinkCache.set(videoEl, found);
    return found;
  }

  function dedupeKeyFor(videoEl, permalink) {
    return permalink || videoEl.currentSrc || videoEl.src || null;
  }

  /**
   * Scans the WHOLE page (not just what's in the viewport) for every video,
   * resolving each to a real downloadable URL. A video with no discoverable
   * permalink is only usable if the current page URL IS itself a dedicated
   * video page (e.g. a reel opened full-page, not a feed) - otherwise we
   * can't build a correct command for it, so it's skipped rather than
   * offering a download that would silently grab the wrong thing.
   */
  function scanAllVideos() {
    const results = [];
    const videos = Array.from(document.querySelectorAll('video'));
    const onDedicatedVideoPage = SITE.isVideoPage(window.location.href);
    videos.forEach((el, idx) => {
      const rect = el.getBoundingClientRect();
      if (rect.width < 80 || rect.height < 80) return; // skip tiny/hidden decorative videos
      const permalink = findPostPermalink(el);
      const url = permalink || (onDedicatedVideoPage ? window.location.href : null);
      if (!url) return;
      const key = dedupeKeyFor(el, permalink) || `idx:${idx}`;
      results.push({ el, url, key, label: `Post ${results.length + 1}` });
    });
    return results;
  }

  function getFeedWidget() {
    let widget = document.getElementById('yt-dlp-feed-widget');
    if (widget) return widget;

    widget = document.createElement('div');
    widget.id = 'yt-dlp-feed-widget';
    widget.innerHTML = `
      <button class="yt-dlp-feed-toggle" aria-label="Detected videos">
        <svg viewBox="0 0 24 24" style="width:18px;height:18px;fill:currentColor;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>
        <span class="yt-dlp-feed-badge">0</span>
      </button>
      <div class="yt-dlp-feed-panel">
        <div class="yt-dlp-feed-header">Videos found on this page</div>
        <div class="yt-dlp-feed-list"></div>
      </div>
    `;
    document.body.appendChild(widget);

    const toggle = widget.querySelector('.yt-dlp-feed-toggle');
    const panel = widget.querySelector('.yt-dlp-feed-panel');
    toggle.addEventListener('click', (e) => {
      e.stopPropagation();
      panel.classList.toggle('show');
    });
    document.addEventListener('click', () => panel.classList.remove('show'));
    panel.addEventListener('click', (e) => e.stopPropagation());

    return widget;
  }

  function updateFeedWidget() {
    const found = scanAllVideos();
    const widget = found.length > 0 ? getFeedWidget() : document.getElementById('yt-dlp-feed-widget');
    if (!widget) return; // nothing found yet, nothing to tear down either

    if (found.length === 0) {
      widget.remove();
      feedSeen.clear();
      return;
    }

    const list = widget.querySelector('.yt-dlp-feed-list');
    const badge = widget.querySelector('.yt-dlp-feed-badge');
    const currentKeys = new Set(found.map(f => f.key));

    // Drop rows for videos no longer on the page (scrolled out and unmounted, etc.)
    for (const [key, entry] of feedSeen) {
      if (!currentKeys.has(key)) {
        entry.row.remove();
        feedSeen.delete(key);
      }
    }

    found.forEach(item => {
      if (feedSeen.has(item.key)) return; // already listed
      const row = document.createElement('div');
      row.className = 'yt-dlp-feed-row';

      const label = document.createElement('span');
      label.className = 'yt-dlp-feed-row-label';
      label.textContent = item.label;

      const btn = document.createElement('button');
      btn.className = 'yt-dlp-feed-row-download';
      btn.textContent = 'Download';
      btn.addEventListener('click', () => handleFeedItemDownload(item.url, btn));

      row.appendChild(label);
      row.appendChild(btn);
      list.appendChild(row);
      feedSeen.set(item.key, { el: item.el, url: item.url, label: item.label, row });
    });

    badge.textContent = String(feedSeen.size);
  }

  async function handleFeedItemDownload(url, btn) {
    const original = btn.textContent;
    btn.textContent = '...';
    btn.disabled = true;
    try {
      const prefs = await chrome.storage.sync.get(['resolution']);
      await launchDownload(prefs.resolution || '1080', false, false, false, '', false, '', '', url);
      btn.textContent = 'Started';
      setTimeout(() => { btn.textContent = original; btn.disabled = false; }, 2000);
      showToast('Download started! Check the application.');
    } catch (error) {
      btn.textContent = original;
      btn.disabled = false;
      showToast(`Error: ${error.message}`, 'error');
    }
  }

  function walkUpForContainer(el, preferAncestorTag) {
    if (preferAncestorTag) {
      const landmark = el.closest(preferAncestorTag);
      if (landmark) return landmark;
    }
    let node = el, depth = 0;
    while (node.parentElement && depth < 4) {
      const r = node.parentElement.getBoundingClientRect();
      if (r.width >= 200 && r.height >= 100) return node.parentElement;
      node = node.parentElement;
      depth++;
    }
    return node;
  }

  /**
   * Three-tier resolution: (1) a curated selector from sites.js - cheapest
   * and most precise when it holds up: (2) no selector matched, but a
   * visible <video> exists - fall back to structure/geometry instead of a
   * guess; (3) nothing at all - caller positions a fixed corner button.
   * Returns { anchor, tier } where tier is 'selector' | 'geometry' | 'body'.
   */
  function resolveAnchor(selectors, preferAncestorTag) {
    const bySelector = findFirstMatch(selectors);
    if (bySelector) {
      const el = preferAncestorTag ? (bySelector.closest(preferAncestorTag) || bySelector) : bySelector;
      return { anchor: el, tier: 'selector' };
    }
    const video = findBestVisibleVideo();
    if (video) {
      return { anchor: walkUpForContainer(video, preferAncestorTag), tier: 'geometry' };
    }
    return { anchor: document.body, tier: 'body' };
  }

  // Containers positioned via the geometry tier get repositioned here on
  // scroll/resize, reading each anchor's LIVE rect (not a frozen snapshot)
  // so they track reasonably well as the page scrolls.
  const geometryTracked = new Map(); // container -> anchorElement

  function repositionGeometryTracked() {
    geometryTracked.forEach((anchorEl, container) => {
      if (!document.body.contains(anchorEl) || !document.body.contains(container)) {
        geometryTracked.delete(container);
        return;
      }
      const r = anchorEl.getBoundingClientRect();
      container.style.position = 'fixed';
      container.style.top = Math.max(8, r.top + 8) + 'px';
      container.style.left = Math.max(8, Math.min(window.innerWidth - 170, r.right - 150)) + 'px';
      container.style.right = 'auto';
      container.style.bottom = 'auto';
    });
  }
  window.addEventListener('scroll', () => requestAnimationFrame(repositionGeometryTracked), { passive: true, capture: true });
  window.addEventListener('resize', () => requestAnimationFrame(repositionGeometryTracked), { passive: true });

  function placeContainer(container, anchor, tier, mode) {
    if (tier === 'body') {
      // No real anchor at all - blind fixed-corner placement (CSS classes
      // 'shorts-mode' / 'channel-fallback' handle this, applied by caller).
      document.body.appendChild(container);
      return;
    }
    if (tier === 'geometry') {
      document.body.appendChild(container);
      geometryTracked.set(container, anchor);
      repositionGeometryTracked();
      return;
    }
    // tier === 'selector'
    if (mode === 'append') {
      anchor.appendChild(container);
    } else {
      // Default for every non-YouTube site: insertAdjacentElement instead
      // of appendChild. Most of these sites are React-rendered: appending
      // a raw DOM node as a CHILD of a React-managed element risks React
      // wiping it out on the container's next re-render, since React has
      // no idea our node exists. Inserting as a SIBLING right after the
      // anchor avoids that entirely.
      anchor.insertAdjacentElement('afterend', container);
    }
  }

  function isStillCorrectlyPlaced(existingEl, anchor, tier) {
    if (!existingEl || !document.body.contains(existingEl)) return false;
    if (tier === 'body') return true; // blind corner fallback, no specific anchor to track
    if (tier === 'geometry') return existingEl._ytdlpAnchor === anchor;
    return existingEl.previousElementSibling === anchor || existingEl.parentElement === anchor;
  }

  function enforceButtonPresence() {
    const url = window.location.href;

    // feedStyle sites (Instagram, Facebook, TikTok) use the persistent
    // widget instead - see the FEED WIDGET section above for why.
    if (SITE.feedStyle) {
      updateFeedWidget();
    } else {
      const existingButton = document.getElementById(CONFIG.CONTAINER_ID);
      const isVideo = SITE.isVideoPage(url) || isVideoContentVisible();

      if (!isVideo) {
        if (existingButton) { geometryTracked.delete(existingButton); existingButton.remove(); }
      } else {
        const { anchor, tier } = resolveAnchor(SITE.videoAnchorSelectors, SITE.preferAncestor);
        if (existingButton && !isStillCorrectlyPlaced(existingButton, anchor, tier)) {
          geometryTracked.delete(existingButton);
          existingButton.remove();
        }
        if (!document.getElementById(CONFIG.CONTAINER_ID)) injectDownloadButton(anchor, tier);
      }
    }

    // --- Channel/profile "download everything" button (unaffected by feedStyle - separate page/URL entirely) ---
    const existingChannelButton = document.getElementById(CONFIG.CHANNEL_CONTAINER_ID);
    const isChannel = SITE.features.channel && SITE.isChannelPage(url) && !SITE.isVideoPage(url);
    if (!isChannel) {
      if (existingChannelButton) { geometryTracked.delete(existingChannelButton); existingChannelButton.remove(); }
    } else {
      const { anchor, tier } = resolveAnchor(SITE.channelAnchorSelectors, SITE.preferAncestor);
      if (existingChannelButton && !isStillCorrectlyPlaced(existingChannelButton, anchor, tier)) {
        geometryTracked.delete(existingChannelButton);
        existingChannelButton.remove();
      }
      if (!document.getElementById(CONFIG.CHANNEL_CONTAINER_ID)) injectChannelButton(anchor, tier);
    }
  }

  function injectDownloadButton(anchor, tier) {
    const container = document.createElement('div');
    container.id = CONFIG.CONTAINER_ID;
    container._ytdlpAnchor = anchor; // see isStillCorrectlyPlaced()

    // Blind fixed-corner placement only when there's truly no real anchor
    // (tier 'body'); the 'geometry' tier gets precisely positioned near the
    // actual video instead, via placeContainer()/repositionGeometryTracked().
    if (tier === 'body') container.classList.add('shorts-mode');

    const button = document.createElement('button');
    button.className = 'yt-dlp-btn';
    button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true" style="width:18px;height:18px;fill:currentColor;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg><span>Download</span>`;

    const dropdown = createDropdown();

    button.addEventListener('click', (e) => {
      e.stopPropagation();
      const isExpanded = dropdown.classList.contains('show');
      document.querySelectorAll('.yt-dlp-dropdown').forEach(d => d.classList.remove('show'));
      if (!isExpanded) dropdown.classList.add('show');
    });

    container.appendChild(button);
    container.appendChild(dropdown);
    placeContainer(container, anchor, tier, SITE.anchorMode || 'after');
  }

  function injectChannelButton(anchor, tier) {
    const container = document.createElement('div');
    container.id = CONFIG.CHANNEL_CONTAINER_ID; // FIX: this id was accidentally dropped in an earlier edit, causing duplicate channel buttons on every recheck
    container._ytdlpAnchor = anchor; // see isStillCorrectlyPlaced()
    if (tier === 'body') container.classList.add('channel-fallback');

    const button = document.createElement('button');
    button.className = 'yt-dlp-btn yt-dlp-channel-btn';
    button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true" style="width:18px;height:18px;fill:currentColor;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg><span>Download all uploads</span>`;

    const dropdown = createChannelDropdown();

    button.addEventListener('click', (e) => {
      e.stopPropagation();
      const isExpanded = dropdown.classList.contains('show');
      document.querySelectorAll('.yt-dlp-dropdown').forEach(d => d.classList.remove('show'));
      if (!isExpanded) dropdown.classList.add('show');
    });

    container.appendChild(button);
    container.appendChild(dropdown);
    placeContainer(container, anchor, tier, SITE.anchorMode || 'after');
  }

  function createDropdown() {
    const dropdown = document.createElement('div');
    dropdown.className = 'yt-dlp-dropdown';
    dropdown.addEventListener('click', e => e.stopPropagation());

    const fragment = document.createDocumentFragment();

    const customBtn = document.createElement('button');
    customBtn.className = 'yt-dlp-option custom-cmd';
    customBtn.innerHTML = `<span>⚙️ Run Custom Command</span>`;
    customBtn.addEventListener('click', () => {
        dropdown.classList.remove('show');
        handleQualitySelect('custom');
    });
    fragment.appendChild(customBtn);

    // FEATURE GATING: an audio-only site (e.g. SoundCloud) skips straight to
    // an audio-only option list; a site with quality2160/1440 disabled just
    // omits those two entries instead of offering a resolution yt-dlp can't
    // actually get there.
    const allQualities = [
      { value: 'audio', label: '🎵 Audio Only', always: true },
      { value: '2160', label: '4K (2160p)', flag: 'quality2160' },
      { value: '1440', label: '2K (1440p)', flag: 'quality1440' },
      { value: '1080', label: 'HD (1080p)', default: true, always: true },
      { value: '720', label: 'HD (720p)', always: true },
      { value: '480', label: 'SD (480p)', always: true },
      { value: '360', label: 'SD (360p)', always: true },
    ];

    const qualities = SITE.audioOnly
      ? [{ value: 'audio', label: '🎵 Best Available Audio', default: true }]
      : allQualities.filter(q => q.always || SITE.features[q.flag]);

    qualities.forEach(q => {
      const btn = document.createElement('button');
      btn.className = 'yt-dlp-option';
      btn.innerHTML = `<span class="option-label">${q.label}</span>${q.default ? '<span class="option-badge">Default</span>' : ''}`;

      btn.addEventListener('click', () => {
        dropdown.classList.remove('show');
        handleQualitySelect(q.value);
      });
      fragment.appendChild(btn);
    });

    fragment.appendChild(createSeparator());

    const togglesContainer = document.createElement('div');
    togglesContainer.className = 'yt-dlp-toggles';

    const buildToggle = (id, label, storageKey) => {
        const lbl = document.createElement('label');
        lbl.className = 'yt-dlp-toggle-label';
        lbl.innerHTML = `<input type="checkbox" id="${id}"><span class="toggle-text">${label}</span>`;
        const cb = lbl.querySelector('input');
        chrome.storage.sync.get([storageKey], res => { if (res[storageKey]) cb.checked = true; });
        cb.addEventListener('change', e => chrome.storage.sync.set({ [storageKey]: e.target.checked }));
        return lbl;
    };

    const audioContainer = document.createElement('div');
    audioContainer.className = 'yt-dlp-input-container';
    audioContainer.innerHTML = `<label>Audio Format</label><select id="float-audio-format"><option value="mp3">MP3</option><option value="flac">FLAC</option><option value="wav">WAV</option><option value="m4a">M4A</option></select>`;
    const audioSelect = audioContainer.querySelector('select');
    chrome.storage.sync.get(['audioFormat'], res => { if(res.audioFormat) audioSelect.value = res.audioFormat; });
    audioSelect.addEventListener('change', e => chrome.storage.sync.set({ audioFormat: e.target.value }));
    togglesContainer.appendChild(audioContainer);

    // FEATURE GATING: subtitles are a YouTube-heavy feature (most other
    // sites either have no subtitle tracks yt-dlp can pull, or only
    // auto-captions inconsistently) - hide the toggle where it'd just fail.
    if (SITE.features.subtitles) {
      togglesContainer.appendChild(buildToggle('float-subs', 'Embed Subtitles', 'embedSubs'));
    }

    const cropToggle = buildToggle('float-crop', 'Crop Video section', 'useCrop');
    togglesContainer.appendChild(cropToggle);

    const timeContainer = document.createElement('div');
    timeContainer.className = 'yt-dlp-input-container';
    timeContainer.style.display = 'none';
    timeContainer.innerHTML = `
        <div class="yt-dlp-time-row">
            <input type="text" id="float-start-time" placeholder="Start (MM:SS)">
            <input type="text" id="float-end-time" placeholder="End (MM:SS)">
        </div>
    `;
    const startBox = timeContainer.querySelector('#float-start-time');
    const endBox = timeContainer.querySelector('#float-end-time');

    cropToggle.querySelector('input').addEventListener('change', e => {
        timeContainer.style.display = e.target.checked ? 'block' : 'none';
    });
    togglesContainer.appendChild(timeContainer);

    ['keydown', 'keyup', 'keypress'].forEach(eventType => {
        startBox.addEventListener(eventType, e => e.stopPropagation());
        endBox.addEventListener(eventType, e => e.stopPropagation());
    });

    // FEATURE GATING: "Full Playlist" refers specifically to a YouTube
    // playlist attached to this video - not applicable anywhere else in
    // the registry today. The new per-channel "Download all uploads"
    // button (injectChannelButton) is the multi-site equivalent.
    if (SITE.features.playlist) {
      const plToggle = buildToggle('float-playlist', 'Full Playlist', 'downloadPlaylist');
      togglesContainer.appendChild(plToggle);

      const plInputContainer = document.createElement('div');
      plInputContainer.className = 'yt-dlp-input-container';
      plInputContainer.innerHTML = `<input type="text" id="float-playlist-items" placeholder="e.g. 1-5, 8"><div class="helper-text">Specific videos (optional)</div>`;
      const playlistInputBox = plInputContainer.querySelector('input');

      chrome.storage.sync.get(['downloadPlaylist', 'playlistItems'], res => {
          if (!res.downloadPlaylist) plInputContainer.style.display = 'none';
          if (res.playlistItems) playlistInputBox.value = res.playlistItems;
      });

      plToggle.querySelector('input').addEventListener('change', e => {
          plInputContainer.style.display = e.target.checked ? 'block' : 'none';
      });

      ['keydown', 'keyup', 'keypress'].forEach(eventType => playlistInputBox.addEventListener(eventType, e => e.stopPropagation()));

      let saveTimeout;
      playlistInputBox.addEventListener('input', e => {
          clearTimeout(saveTimeout);
          saveTimeout = setTimeout(() => chrome.storage.sync.set({ playlistItems: e.target.value.trim() }), 500);
      });
      togglesContainer.appendChild(plInputContainer);
    }

    togglesContainer.appendChild(buildToggle('float-cookies', 'Bypass Age & Bot Lock', 'useCookies'));

    fragment.appendChild(togglesContainer);
    dropdown.appendChild(fragment);

    document.addEventListener('click', () => dropdown.classList.remove('show'));
    return dropdown;
  }

  function createChannelDropdown() {
    const dropdown = document.createElement('div');
    dropdown.className = 'yt-dlp-dropdown';
    dropdown.addEventListener('click', e => e.stopPropagation());

    const fragment = document.createDocumentFragment();

    const warning = document.createElement('div');
    warning.className = 'yt-dlp-input-container yt-dlp-channel-warning';
    warning.innerHTML = `<div class="helper-text" style="text-align:left;">This queues <b>every upload</b> on this ${SITE.label} page. It paces requests automatically to avoid rate limits, skips videos already downloaded on repeat runs, and won't stop on individual broken/private videos.</div>`;
    fragment.appendChild(warning);

    const qualities = SITE.audioOnly
      ? [{ value: 'audio', label: '🎵 Best Available Audio', default: true }]
      : [
          { value: '1080', label: 'HD (1080p)', default: true },
          { value: '720', label: 'HD (720p)' },
          { value: 'audio', label: '🎵 Audio Only' },
        ];

    qualities.forEach(q => {
      const btn = document.createElement('button');
      btn.className = 'yt-dlp-option';
      btn.innerHTML = `<span class="option-label">${q.label}</span>${q.default ? '<span class="option-badge">Default</span>' : ''}`;
      btn.addEventListener('click', () => {
        dropdown.classList.remove('show');
        handleChannelDownload(q.value);
      });
      fragment.appendChild(btn);
    });

    dropdown.appendChild(fragment);
    document.addEventListener('click', () => dropdown.classList.remove('show'));
    return dropdown;
  }

  function createSeparator() {
      const sep = document.createElement('div');
      sep.className = 'yt-dlp-separator';
      return sep;
  }

  function sanitizePlaylistItems(rawInput) {
      return rawInput.toLowerCase().replace(/\s+(to|through)\s+/g, '-').replace(/[^0-9,\-]/g, '').replace(/,+/g, ',').replace(/(^,)|(,$)/g, '');
  }

  async function handleQualitySelect(resolution) {
    const wantsSubs = document.getElementById('float-subs')?.checked || false;
    const wantsPlaylist = document.getElementById('float-playlist')?.checked || false;
    const wantsCookies = document.getElementById('float-cookies')?.checked || false;
    const wantsItems = document.getElementById('float-playlist-items')?.value.trim() || '';

    const isCropped = document.getElementById('float-crop')?.checked || false;
    const sTime = document.getElementById('float-start-time')?.value.trim() || '';
    const eTime = document.getElementById('float-end-time')?.value.trim() || '';

    try {
      await launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, wantsItems, isCropped, sTime, eTime);

      // AUTO-UNCHECK FIX: Visually clear situational checkboxes
      const cookiesToggle = document.getElementById('float-cookies');
      const playlistToggle = document.getElementById('float-playlist');
      const cropToggle = document.getElementById('float-crop');
      const startInput = document.getElementById('float-start-time');
      const endInput = document.getElementById('float-end-time');

      if (cookiesToggle) cookiesToggle.checked = false;

      if (playlistToggle) {
          playlistToggle.checked = false;
          const plInputContainer = document.getElementById('float-playlist-items')?.parentElement;
          if (plInputContainer) plInputContainer.style.display = 'none';
      }

      if (cropToggle) {
          cropToggle.checked = false;
          const timeContainer = document.querySelector('.yt-dlp-time-row')?.parentElement;
          if (timeContainer) timeContainer.style.display = 'none';
      }

      if (startInput) startInput.value = '';
      if (endInput) endInput.value = '';

      chrome.storage.sync.set({
          useCookies: false,
          downloadPlaylist: false,
          useCrop: false
      });

      showToast('Download started! Check the application.');
    } catch (error) {
      showToast(`Error: ${error.message}`, 'error');
    }
  }

  async function launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, playlistItems, isCropped, sTime, eTime, urlOverride) {
    const videoUrl = (urlOverride || window.location.href).split('&')[0];
    const prefs = await chrome.storage.sync.get(['savePath', 'concurrentDownloads', 'customCommand', 'audioFormat', 'flagMetadata', 'flagThumbnail', 'flagSponsor', 'compatMode']);

    if (resolution === 'custom') {
        const rawCustom = prefs.customCommand ? prefs.customCommand.trim() : '';
        let finalCustom = rawCustom ? rawCustom : 'yt-dlp';
        if (!finalCustom.includes(videoUrl)) finalCustom += ` "${videoUrl}"`;
        await executeFinalCommand(finalCustom, wantsCookies);
        return;
    }

    let saveDir = prefs.savePath ? prefs.savePath.trim().replace(/[/\\]$/, '') + '\\' : '~/Downloads/YTDownloaderPro/';
    saveDir = saveDir.replace(/\\/g, '\\\\');
    const speed = prefs.concurrentDownloads || '4';
    const audioFmt = prefs.audioFormat || 'mp3';

    let fmt;
    if (resolution === 'audio') {
        let quality = audioFmt === 'flac' || audioFmt === 'wav' ? '' : '--audio-quality 0';
        fmt = { f: 'ba', opt: `--extract-audio --audio-format ${audioFmt} ${quality}`.trim(), ext: audioFmt };
    } else if (resolution === '1440' || resolution === '2160') {
        fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mkv', ext: 'mkv' };
    } else {
        fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
    }

    const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;

    let cropperCmd = '';
    // FEATURE GATING: crop relies on --download-sections, which needs a
    // seekable, on-demand format - fine for VOD-style content across every
    // site in the registry, so no per-site gate needed beyond the toggle
    // itself already being hidden where it wouldn't apply.
    if (isCropped && (sTime || eTime)) {
        const startSec = sTime ? sTime : '00:00:00';
        const endSec = eTime ? eTime : 'inf';
        cropperCmd = `--download-sections "*${startSec}-${endSec}" --force-keyframes-at-cuts `;
    }

    const subCmd = (wantsSubs && resolution !== 'audio' && SITE.features.subtitles)
        ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" --sleep-subtitles 5 '
        : '';

    let plCmd = '--no-playlist ';
    if (wantsPlaylist && SITE.features.playlist) {
        plCmd = '--yes-playlist ';
        if (playlistItems) {
            const clean = sanitizePlaylistItems(playlistItems);
            if (clean) plCmd += `--playlist-items "${clean}" `;
        }
    }

    const metaFlag = (prefs.flagMetadata === true && SITE.features.metadata) ? '--embed-metadata' : '';

    const thumbFlag = (prefs.flagThumbnail !== false && SITE.features.thumbnail && fmt.ext !== 'wav')
        ? '--embed-thumbnail'
        : '';

    const sponsorFlag = (prefs.flagSponsor === true && SITE.features.sponsorBlock) ? '--sponsorblock-remove all' : '';

    const compatFlag = (prefs.compatMode === true && SITE.features.compatMode) ? '-S "vcodec:h264,res,acodec:m4a"' : '';

    const outTemplate = wantsPlaylist && SITE.features.playlist
      ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${fmt.ext}"`
      : `-o "${saveDir}%(title)s_${resLabel}.${fmt.ext}"`;

    const command = ['yt-dlp', '-f', `"${fmt.f}"`, compatFlag, fmt.opt, `-N ${speed}`, cropperCmd, subCmd, plCmd, sponsorFlag, '--restrict-filenames', metaFlag, thumbFlag, '--no-warnings', '--progress', outTemplate, `"${videoUrl}"`].filter(Boolean).join(' ');

    await executeFinalCommand(command, wantsCookies);
  }

  // --- Channel / profile bulk download ---

  function slugForArchive(url) {
    return url.replace(/^https?:\/\//, '').replace(/[^\w.-]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 80) || 'channel';
  }

  async function handleChannelDownload(resolution) {
    const confirmed = window.confirm(
      `Download every upload from this ${SITE.label} page?\n\n` +
      `This can take a long time and use significant disk space and bandwidth. ` +
      `Requests are paced automatically and already-downloaded videos are skipped on repeat runs.`
    );
    if (!confirmed) return;

    try {
      const prefs = await chrome.storage.sync.get(['savePath', 'concurrentDownloads', 'audioFormat', 'flagMetadata', 'flagThumbnail', 'compatMode']);

      let saveDir = prefs.savePath ? prefs.savePath.trim().replace(/[/\\]$/, '') + '\\' : '~/Downloads/YTDownloaderPro/';
      saveDir = saveDir.replace(/\\/g, '\\\\');
      const speed = prefs.concurrentDownloads || '4';
      const audioFmt = prefs.audioFormat || 'mp3';

      let fmt;
      if (resolution === 'audio') {
        fmt = { f: 'ba', opt: `--extract-audio --audio-format ${audioFmt}`, ext: audioFmt };
      } else {
        fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
      }
      const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;

      const channelUrl = SITE.channelUrlForCommand
        ? SITE.channelUrlForCommand(window.location.href)
        : window.location.href;

      const archiveFile = `${saveDir}${slugForArchive(channelUrl)}.archive.txt`;
      const metaFlag = (prefs.flagMetadata === true) ? '--embed-metadata' : '';
      const thumbFlag = (prefs.flagThumbnail !== false && fmt.ext !== 'wav') ? '--embed-thumbnail' : '';
      const compatFlag = (prefs.compatMode === true && SITE.features.compatMode) ? '-S "vcodec:h264,res,acodec:m4a"' : '';

      // SAFETY RAILS for bulk/channel jobs (see Stage 1 research):
      //  --yes-playlist + --ignore-errors: one broken/private video doesn't kill the batch
      //  --download-archive: re-runs only fetch new uploads, not everything again
      //  --sleep-requests/--sleep-interval/--max-sleep-interval: paces requests to
      //    avoid tripping bot-detection/rate limits on a bulk pull
      const command = [
        'yt-dlp', '--yes-playlist', '--ignore-errors',
        '-f', `"${fmt.f}"`, compatFlag, fmt.opt, `-N ${speed}`,
        '--sleep-requests 1', '--sleep-interval 5', '--max-sleep-interval 15',
        `--download-archive "${archiveFile}"`,
        '--restrict-filenames', metaFlag, thumbFlag, '--no-warnings', '--progress',
        `-o "${saveDir}%(channel,uploader)s/%(title)s_${resLabel}.${fmt.ext}"`,
        `"${channelUrl}"`,
      ].filter(Boolean).join(' ');

      await executeFinalCommand(command, false);
      showToast('Channel download started! Check the application.');
    } catch (error) {
      showToast(`Error: ${error.message}`, 'error');
    }
  }

  async function executeFinalCommand(command, wantsCookies) {
      let encoded = btoa(unescape(encodeURIComponent(command)));
      if (wantsCookies) {
          const cookies = await new Promise((resolve, reject) => {
              chrome.runtime.sendMessage({ action: "get_cookies", site: SITE.id }, (response) => {
                  if (chrome.runtime.lastError) {
                      return reject(new Error("Cookie mapping failed. Please refresh."));
                  }
                  resolve(response);
              });
          });
          if (cookies) encoded += `||${cookies}`;
      }

      const protocolUrl = `ytdlp://${encodeURIComponent(encoded)}`;

      const iframe = document.createElement('iframe');
      iframe.src = protocolUrl;
      iframe.style.display = 'none';
      document.body.appendChild(iframe);

      setTimeout(() => iframe.remove(), 4000);
  }

  function showToast(msg, type = 'success') {
    const existing = document.querySelector('.yt-dlp-toast');
    if (existing) existing.remove();
    const t = document.createElement('div');
    t.className = `yt-dlp-toast ${type}`; t.textContent = msg;
    document.body.appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => { t.classList.remove('show'); setTimeout(() => t.remove(), 300); }, 3000);
  }

})();