/**
 * @fileoverview tiktok-main.js - runs in the page's MAIN world (see manifest.json, "world": "MAIN").
 *
 * Why this exists: TikTok's logged-out feed renders no /@user/video/<id> link anywhere in the post. The
 * video's data does exist in the page's React tree, but React keeps it in expando properties
 * (__reactFiber$...) that the extension's isolated world cannot see. This tiny script can.
 *
 * It never touches the network, never patches fetch/XHR and never changes the page. It only answers a
 * synchronous CustomEvent from content.js ('ytdlp-resolve-tiktok', dispatched on the <video>) by writing ONE
 * attribute on that element:
 *
 *   data-ytdlp-items   JSON [{ id, author, dur }, ...]   every video-shaped object found near this element
 *   data-ytdlp-miss    why nothing was found (for the debug log)
 *
 * Built to survive TikTok's code churn:
 *  - The <video> itself is created by the player library and has NO React data (confirmed from a real probe), so
 *    this starts from the nearest ANCESTOR that does and walks up the fiber tree.
 *  - It finds the item by SHAPE (an object with a plausible TikTok id plus video-ish fields), at any property
 *    name, so renamed props (item -> itemInfo -> data...) keep working.
 *  - It returns candidates, it does not decide: content.js cross-checks them against other sources.
 */
(function () {
  'use strict';
  const EVT = 'ytdlp-resolve-tiktok';
  const MAX_FIBER_DEPTH = 70;
  const MAX_NODES_PER_PROPS = 400;
  const SKIP_KEYS = new Set(['children', '_owner', '_store', '$$typeof', 'ref', 'key', 'return', 'child', 'sibling', 'alternate', 'stateNode']);

  const isSnowflake = (s) => {
    if (!/^\d{17,20}$/.test(s)) return false;
    try { const ts = Number(BigInt(s) >> 32n); return ts >= 1472688000 && ts <= Date.now() / 1000 + 2 * 86400; } catch { return false; }
  };

  /** Looks like a TikTok video item? Needs a plausible id AND at least one field a user/music/comment object lacks. */
  function asItem(o) {
    if (!o || typeof o !== 'object' || Array.isArray(o)) return null;
    const raw = o.id != null ? o.id : (o.aweme_id != null ? o.aweme_id : o.itemId);
    const id = raw != null ? String(raw) : '';
    if (!isSnowflake(id)) return null;
    const hasVideoish = !!(o.video || o.desc !== undefined || o.stats || o.statistics || o.createTime || o.create_time || o.music || o.textExtra);
    if (!hasVideoish) return null;
    const a = o.author;
    const author = a && typeof a === 'object' ? (a.uniqueId || a.unique_id || '') : (typeof a === 'string' ? a : '');
    const dur = o.video && typeof o.video.duration === 'number' ? o.video.duration : (typeof o.duration === 'number' ? o.duration : null);
    return { id, author: /^[\w.-]+$/.test(author) ? author : '', dur };
  }

  /** Breadth-first scan of one props object for item-shaped objects, any key name, bounded depth and size. */
  function scan(root, out) {
    if (!root || typeof root !== 'object') return;
    const queue = [[root, 0]];
    const seen = new WeakSet();
    let budget = MAX_NODES_PER_PROPS;
    while (queue.length && budget-- > 0) {
      const [o, depth] = queue.shift();
      if (!o || typeof o !== 'object' || seen.has(o)) continue;
      seen.add(o);
      if (typeof Node !== 'undefined' && o instanceof Node) continue;
      if (o === window) continue;
      const it = asItem(o);
      if (it) { if (!out.some((x) => x.id === it.id)) out.push(it); continue; }
      if (depth >= 3 || Array.isArray(o)) continue; // arrays are lists of OTHER videos - their index says nothing about this one
      let keys; try { keys = Object.keys(o); } catch { continue; }
      for (const k of keys.slice(0, 40)) {
        if (SKIP_KEYS.has(k)) continue;
        let v; try { v = o[k]; } catch { continue; }
        if (v && typeof v === 'object') queue.push([v, depth + 1]);
      }
    }
  }

  function fiberOf(el) {
    for (const k in el) if (k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$')) return el[k];
    return null;
  }

  function resolve(video) {
    const items = [];
    // up to 3 nearest ancestors that React rendered (the video has none; its article does)
    const starts = [];
    for (let p = video, i = 0; p && i < 30 && starts.length < 3; p = p.parentElement, i++) {
      const f = fiberOf(p);
      if (f) starts.push(f);
    }
    if (!starts.length) return { items, miss: 'no-fiber-on-any-ancestor' };
    for (const start of starts) {
      let f = start;
      for (let i = 0; f && i < MAX_FIBER_DEPTH; i++, f = f.return) {
        if (f.memoizedProps) scan(f.memoizedProps, items);
        if (items.length >= 6) return { items };
      }
    }
    return items.length ? { items } : { items, miss: 'fiber-found-but-no-item-shaped-props' };
  }

  document.addEventListener(EVT, (e) => {
    const el = e.target;
    if (!el || !el.setAttribute) return;
    try {
      el.removeAttribute('data-ytdlp-items'); el.removeAttribute('data-ytdlp-miss');
      const r = resolve(el);
      if (r.items.length) el.setAttribute('data-ytdlp-items', JSON.stringify(r.items.slice(0, 6)));
      else el.setAttribute('data-ytdlp-miss', r.miss || 'unknown');
    } catch (err) {
      el.setAttribute('data-ytdlp-miss', 'error:' + String((err && err.message) || err).slice(0, 60));
    }
  }, true); // capture: the event doesn't bubble, but a capture listener on document still sees it on its way down
})();
