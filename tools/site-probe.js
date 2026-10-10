/**
 * YT Downloader Pro - site probe
 * ---------------------------------------------------------------------------
 * Paste this whole file into the DevTools Console (F12 -> Console) on any
 * supported site, with the video you care about PLAYING and roughly CENTRED
 * on screen. It reads the page (read-only), then:
 *   - copies a JSON report to your clipboard (via DevTools' copy())
 *   - downloads the same report as ytdlp-probe-<host>-<time>.json
 *   - keeps it in window.__ytdlpProbe
 *
 * What it collects: the video elements and how they are wrapped, every link
 * near the active video (query strings stripped), every attribute that holds
 * a long number (that is usually where a post/video id hides), the React
 * props chain around the video (key names + id-like values only), which
 * JSON/state blobs the page ships, and the PATHS of recent API/media requests.
 *
 * What it does NOT collect: cookies, localStorage, tokens, query strings,
 * page text, or anything you typed. Open the JSON and read it before you
 * share it - handles/usernames in links do show up.
 *
 * Run it twice per site if you can: once in the FEED (video in view) and once
 * on a video's own permalink page. Comparing the two shows what the feed hides.
 */
(async () => {
  const OPTS = {
    prime: true,           // hover '#'/'?...' links first (Facebook only fills the real permalink on hover)
    maxDescendants: 1500,  // cap on elements scanned for id-like attributes
    maxFiberDepth: 60,
  };

  const t0 = Date.now();
  const short = (s, n = 80) => String(s == null ? '' : s).replace(/\s+/g, ' ').slice(0, n);
  const pathOnly = (u) => { if (/^blob:/i.test(u)) return 'blob:' + hostOnly(String(u).slice(5)); try { const x = new URL(u, location.href); return x.origin + x.pathname; } catch { return short(u, 80); } };
  const hostOnly = (u) => { try { return new URL(u, location.href).host; } catch { return ''; } };
  const R = (n) => Math.round(n);
  const ID_RE = /(?<![\d.])\d{9,20}(?![\d.])/; // whole integers only - '0.0938...' and '133.33...' are not ids

  // ---- DOM helpers ---------------------------------------------------------
  const describe = (el) => {
    if (!el || !el.tagName) return String(el);
    let s = el.tagName.toLowerCase();
    if (el.id) s += '#' + short(el.id, 60);
    const cls = typeof el.className === 'string' ? el.className.trim().split(/\s+/).filter(Boolean) : [];
    if (cls.length) s += '.' + cls.slice(0, 3).map((c) => short(c, 30)).join('.') + (cls.length > 3 ? `(+${cls.length - 3})` : '');
    for (const a of ['role', 'data-e2e', 'data-testid', 'data-pagelet', 'data-scroll-index', 'data-id', 'aria-label']) {
      const v = el.getAttribute && el.getAttribute(a);
      if (v) s += `[${a}=${short(v, 40)}]`;
    }
    return s;
  };
  const attrsOf = (el) => {
    const o = {};
    for (const a of el.attributes || []) {
      if (/^(style|class|srcset)$/.test(a.name)) continue;
      o[a.name] = /^(href|src|poster)$/.test(a.name) ? pathOnly(a.value) : short(a.value, 70);
    }
    return o;
  };
  const rectOf = (el) => { const r = el.getBoundingClientRect(); return { x: R(r.left), y: R(r.top), w: R(r.width), h: R(r.height) }; };
  const visScore = (el) => {
    const r = el.getBoundingClientRect(), vw = innerWidth, vh = innerHeight;
    const vis = Math.max(0, Math.min(r.right, vw) - Math.max(r.left, 0)) * Math.max(0, Math.min(r.bottom, vh) - Math.max(r.top, 0));
    const own = r.width * r.height;
    if (own <= 0 || vis < Math.min(own, vw * vh) * 0.4) return 0;
    const dist = Math.hypot((r.left + r.right) / 2 - vw / 2, (r.top + r.bottom) / 2 - vh / 2) / Math.hypot(vw / 2, vh / 2);
    return vis * (1 - 0.5 * Math.min(dist, 1));
  };
  const ancestors = (el, n = 14) => { const out = []; for (let p = el; p && out.length < n; p = p.parentElement) out.push(describe(p)); return out; };

  // all elements incl. open shadow roots
  const deepAll = (sel) => {
    const out = [], seen = new Set();
    const walk = (root) => {
      if (!root || seen.has(root)) return; seen.add(root);
      try { root.querySelectorAll(sel).forEach((e) => out.push(e)); } catch {}
      let all = []; try { all = root.querySelectorAll('*'); } catch {}
      for (const e of all) if (e.shadowRoot) walk(e.shadowRoot);
    };
    walk(document);
    return out;
  };

  const report = {
    probeVersion: 2,
    when: new Date().toISOString(),
    page: { href: pathOnly(location.href), search: location.search ? '(stripped)' : '', host: location.host, ua: navigator.userAgent, viewport: { w: innerWidth, h: innerHeight } },
  };

  // ---- 1. videos -----------------------------------------------------------
  const videos = deepAll('video');
  report.videoCount = videos.length;
  report.videos = videos.slice(0, 12).map((v) => ({
    rect: rectOf(v), visScore: R(visScore(v)), paused: v.paused, readyState: v.readyState, duration: isFinite(v.duration) ? R(v.duration) : null,
    srcKind: (v.currentSrc || v.src || '').startsWith('blob:') ? 'blob' : (v.currentSrc || v.src ? 'url:' + hostOnly(v.currentSrc || v.src) : 'none'),
    poster: v.poster ? hostOnly(v.poster) : null, inShadowRoot: v.getRootNode() !== document,
    attrs: attrsOf(v), ancestors: ancestors(v),
  }));
  const hostEls = deepAll('shreddit-player, shreddit-player-2, [data-testid*="player" i], [class*="player" i]').slice(0, 6);
  report.playerLikeElements = hostEls.map((e) => ({ el: describe(e), rect: rectOf(e), hasShadow: !!e.shadowRoot }));

  const best = videos.map((v) => [v, visScore(v)]).sort((a, b) => b[1] - a[1])[0];
  const active = best && best[1] > 0 ? best[0] : null;
  report.activeVideo = active ? 'found' : 'NONE IN VIEW - scroll so the video is centred/playing and run again';

  // ---- 2. container, links, id-like attributes -----------------------------
  if (active) {
    let container = active.closest('article, [role="article"]');
    report.containerKind = container ? 'article' : 'climbed';
    if (!container) {
      let node = active, bestN = active.parentElement || active;
      for (let i = 0; i < 30 && node.parentElement; i++) { node = node.parentElement; if (node.querySelectorAll('video').length > 1) break; bestN = node; }
      container = bestN;
    }
    report.container = { el: describe(container), rect: rectOf(container), attrs: attrsOf(container) };

    const readAnchors = () => Array.from(container.querySelectorAll('a[href]')).slice(0, 60).map((a) => ({
      href: a.getAttribute('href') === '#' || a.getAttribute('href') === '' ? a.getAttribute('href') : pathOnly(a.href),
      text: short(a.textContent, 30), de: a.getAttribute('data-e2e') || a.getAttribute('data-testid') || undefined,
    }));
    report.anchorsBeforePrime = readAnchors();
    if (OPTS.prime) {
      let n = 0;
      container.querySelectorAll('a[href]').forEach((a) => {
        const h = a.getAttribute('href') || '';
        if (n < 12 && (h === '#' || h === '' || h.startsWith('?'))) {
          a.dispatchEvent(new MouseEvent('mouseover', { bubbles: true, cancelable: true, view: window }));
          a.dispatchEvent(new FocusEvent('focusin', { bubbles: true })); n++;
        }
      });
      if (n) { await new Promise((r) => setTimeout(r, 300)); report.primedLinks = n; }
    }
    report.anchors = readAnchors();
    report.anchorIdLike = report.anchors.filter((a) => ID_RE.test(a.href)).map((a) => a.href);

    // every attribute on the video's ancestors / the container's descendants that holds a long number
    const scan = [];
    const seen = new Set();
    const push = (el, why) => {
      if (!el || seen.has(el)) return; seen.add(el);
      for (const a of el.attributes || []) {
        if (/^(href|src|poster|srcset|style|class)$/.test(a.name)) continue;
        if (a.value.length < 160 && ID_RE.test(a.value)) scan.push({ where: why, el: describe(el), attr: a.name, value: short(a.value, 70) });
      }
    };
    for (let p = active, i = 0; p && i < 25; p = p.parentElement, i++) push(p, 'ancestor' + i);
    [container, ...Array.from(container.querySelectorAll('*')).slice(0, OPTS.maxDescendants)].forEach((e) => push(e, 'inside-container'));
    report.idLikeAttributes = scan.slice(0, 50);

    // selector candidates: data-e2e / data-testid / data-pagelet values around the video
    const dv = new Set();
    for (let p = active, i = 0; p && i < 25; p = p.parentElement, i++) for (const a of ['data-e2e', 'data-testid', 'data-pagelet', 'data-test-selector', 'data-a-target']) { const v = p.getAttribute && p.getAttribute(a); if (v) dv.add(`${a}=${short(v, 50)}`); }
    container.querySelectorAll('[data-e2e],[data-testid],[data-pagelet]').forEach((e) => { if (dv.size < 60) dv.add(['data-e2e', 'data-testid', 'data-pagelet'].map((a) => e.getAttribute(a) && `${a}=${short(e.getAttribute(a), 50)}`).filter(Boolean)[0]); });
    report.stableSelectorCandidates = Array.from(dv);

    // ---- 3. React / framework internals around the video (MAIN world: console can see these) ----
    const fiberKey = (el, p) => { for (const k in el) if (k.startsWith(p)) return k; return null; };
    const idKeys = /^(id|aweme_id|awemeId|videoId|video_id|itemId|shortcode|code|pk|media_id|mediaId|post_id|postId|story_fbid|permalink|url)$/i;
    const findIds = (obj, depth = 0, path = '', out = [], seenO = new WeakSet()) => {
      if (!obj || typeof obj !== 'object' || depth > 3 || seenO.has(obj) || out.length > 8) return out;
      seenO.add(obj);
      let keys = []; try { keys = Object.keys(obj); } catch {}
      for (const k of keys.slice(0, 40)) {
        let v; try { v = obj[k]; } catch { continue; }
        if (idKeys.test(k) && (typeof v === 'string' || typeof v === 'number') && String(v).length >= 6) out.push({ path: path + k, value: short(typeof v === 'string' && /^https?:/.test(v) ? pathOnly(v) : v, 70) });
        else if (v && typeof v === 'object' && !(v instanceof Node) && !(v instanceof Window)) findIds(v, depth + 1, path + k + '.', out, seenO);
      }
      return out;
    };
    const fiberReport = { keysOnVideo: [], keysOnContainer: [], startedAt: null, ancestorsWithFiber: 0, levels: [], itemShaped: [] };
    for (const k in active) if (/^__(react|vue|svelte|ng)/i.test(k) || k.startsWith('_')) fiberReport.keysOnVideo.push(k.replace(/\$.*/, '$…'));
    for (const k in container) if (/^__(react|vue|svelte|ng)/i.test(k)) fiberReport.keysOnContainer.push(k.replace(/\$.*/, '$…'));
    // Players often create their own <video>, which React never touched - so start from the NEAREST ancestor React did render.
    const fiberStarts = [];
    for (let p = active, i = 0; p && i < 30; p = p.parentElement, i++) {
      const k = fiberKey(p, '__reactFiber$') || fiberKey(p, '__reactInternalInstance$');
      if (k) { fiberReport.ancestorsWithFiber++; if (fiberStarts.length < 3) { fiberStarts.push(p[k]); if (!fiberReport.startedAt) fiberReport.startedAt = describe(p); } }
    }
    // an "item" = an object with a plausible long id plus video-ish fields, found at ANY property name
    const looksLikeItem = (o) => {
      if (!o || typeof o !== 'object' || Array.isArray(o)) return null;
      const raw = o.id != null ? o.id : (o.aweme_id != null ? o.aweme_id : (o.pk != null ? o.pk : o.itemId));
      const id = raw != null ? String(raw) : '';
      if (!/^\d{9,20}$/.test(id)) return null;
      const fields = ['video', 'desc', 'stats', 'author', 'music', 'createTime', 'caption', 'media', 'video_versions', 'owner'].filter((k) => o[k] !== undefined);
      return fields.length ? { id, fields, dur: o.video && typeof o.video.duration === 'number' ? o.video.duration : (typeof o.duration === 'number' ? o.duration : null) } : null;
    };
    const shapeScan = (root, pathPrefix) => {
      const queue = [[root, 0, pathPrefix]], seenO = new WeakSet(); let budget = 400; const hits = [];
      while (queue.length && budget-- > 0) {
        const [o, d, path] = queue.shift();
        if (!o || typeof o !== 'object' || seenO.has(o) || o instanceof Node || o === window) continue;
        seenO.add(o);
        const it = looksLikeItem(o);
        if (it) { hits.push({ path: path || '(props)', ...it }); continue; }
        if (d >= 3 || Array.isArray(o)) continue;
        let keys = []; try { keys = Object.keys(o); } catch {}
        for (const k of keys.slice(0, 40)) { if (/^(children|_owner|_store|ref|key)$/.test(k)) continue; let v; try { v = o[k]; } catch { continue; } if (v && typeof v === 'object') queue.push([v, d + 1, (path ? path + '.' : '') + k]); }
      }
      return hits;
    };
    fiberStarts.forEach((start, si) => {
      let f = start;
      for (let i = 0; f && i < OPTS.maxFiberDepth; i++, f = f.return) {
        const props = f.memoizedProps && typeof f.memoizedProps === 'object' ? f.memoizedProps : null;
        const ids = props ? findIds(props) : [];
        const name = typeof f.type === 'string' ? f.type : (f.type && (f.type.displayName || f.type.name)) || (f.type && f.type.$$typeof ? 'memo/forwardRef' : '?');
        if (props && fiberReport.itemShaped.length < 12) shapeScan(props, '').forEach((h) => fiberReport.itemShaped.push({ start: si, depth: i, component: short(name, 40), ...h }));
        if ((ids.length || i < 4) && fiberReport.levels.length < 40) fiberReport.levels.push({ start: si, depth: i, type: short(name, 40), propKeys: props ? Object.keys(props).slice(0, 14) : [], idLike: ids });
      }
    });
    report.react = fiberReport;
  }

  // ---- 4. page-level data blobs, meta, selectors ---------------------------
  const blobs = [];
  document.querySelectorAll('script[type="application/json"], script[id*="DATA" i], script[id*="STATE" i], script[type="application/ld+json"]').forEach((s) => {
    const txt = s.textContent || '';
    const o = { id: s.id || null, type: s.type || null, bytes: txt.length };
    if (txt.length && txt.length < 6e6) { try { const j = JSON.parse(txt); o.topKeys = Object.keys(j).slice(0, 15); if (j.__DEFAULT_SCOPE__) o.defaultScopeKeys = Object.keys(j.__DEFAULT_SCOPE__).slice(0, 20); if (j['@type']) o.ldType = j['@type']; } catch { o.parse = 'failed'; } }
    if (blobs.length < 12) blobs.push(o);
  });
  report.jsonBlobs = blobs;
  report.globals = ['__UNIVERSAL_DATA_FOR_REHYDRATION__', '__NEXT_DATA__', '__INITIAL_STATE__', 'SIGI_STATE', 'ytInitialData', 'ytInitialPlayerResponse', '__APOLLO_STATE__', '__additionalData', '_sharedData', 'require', '__d', 'ytcfg'].filter((k) => { try { return k in window; } catch { return false; } });
  const meta = (sel, attr = 'content') => { const e = document.querySelector(sel); return e ? pathOnly(e.getAttribute(attr) || '') : null; };
  report.meta = { canonical: meta('link[rel="canonical"]', 'href'), ogUrl: meta('meta[property="og:url"]'), ogType: (document.querySelector('meta[property="og:type"]') || {}).content || null, ogVideo: meta('meta[property="og:video"]') };

  const de = {};
  document.querySelectorAll('[data-e2e],[data-testid],[data-pagelet]').forEach((e) => { const v = e.getAttribute('data-e2e') || e.getAttribute('data-testid') || e.getAttribute('data-pagelet'); de[v] = (de[v] || 0) + 1; });
  report.pageDataAttributeCounts = Object.fromEntries(Object.entries(de).sort((a, b) => b[1] - a[1]).slice(0, 40));
  report.landmarks = { article: document.querySelectorAll('article').length, roleArticle: document.querySelectorAll('[role="article"]').length, roleFeed: document.querySelectorAll('[role="feed"]').length, main: document.querySelectorAll('main').length };

  // ---- 5. recent network: PATHS ONLY (no query strings, no headers, no bodies) ----
  try {
    const interesting = /video|reel|item|feed|graphql|api|playback|media|manifest|m3u8|mpd|clip|post|story/i;
    const seenU = new Set();
    report.recentRequests = performance.getEntriesByType('resource')
      .filter((e) => /fetch|xmlhttprequest|video|other/.test(e.initiatorType) && interesting.test(e.name))
      .map((e) => `${e.initiatorType} ${pathOnly(e.name)}`)
      .filter((u) => !seenU.has(u) && seenU.add(u)).slice(-60);
  } catch { report.recentRequests = []; }

  report.tookMs = Date.now() - t0;

  // ---- output --------------------------------------------------------------
  const json = JSON.stringify(report, null, 2);
  window.__ytdlpProbe = report;
  let copied = false;
  try { if (typeof copy === 'function') { copy(json); copied = true; } } catch {}
  if (!copied) { try { await navigator.clipboard.writeText(json); copied = true; } catch {} }
  try {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    a.download = `ytdlp-probe-${location.hostname}-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
    document.body.appendChild(a); a.click(); a.remove();
  } catch {}
  console.log('%c[ytdlp-probe] done', 'color:#3ea6ff;font-weight:bold', { copiedToClipboard: copied, bytes: json.length, activeVideo: report.activeVideo, anchors: (report.anchors || []).length, idLikeAttributes: (report.idLikeAttributes || []).length, reactLevels: report.react ? report.react.levels.length : 0, itemShapedObjects: report.react ? report.react.itemShaped.length : 0 });
  if (!copied) console.log(json);
  return report;
})();
