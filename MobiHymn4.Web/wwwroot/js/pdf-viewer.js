// Sheet-music PDF viewer (PDF.js). Android Chrome / TWA can't show PDFs inline,
// so pages are rendered to canvases. PDF.js is imported on first open only.
(function () {
  const LIB = '/lib/pdfjs/';
  const MIN_ZOOM = 1;
  const MAX_ZOOM = 4;
  const MAX_CANVAS_PIXELS = 16 * 1024 * 1024;
  const DOUBLE_TAP_MS = 300;

  let libPromise = null;
  let state = null;

  function loadLib() {
    if (!libPromise) {
      libPromise = import(LIB + 'pdf.min.mjs').then((lib) => {
        lib.GlobalWorkerOptions.workerSrc = LIB + 'pdf.worker.min.mjs';
        return lib;
      });
      libPromise.catch(() => { libPromise = null; });
    }
    return libPromise;
  }

  const clampZoom = (z) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z));

  // ---- Sheet-music source: /api/pdf (Firebase Storage) + optional offline copies ----
  // Offline copies are AES-GCM encrypted with a non-extractable key kept in IndexedDB, so what's
  // stored on the device is unreadable outside this app. Own cache name so service-worker
  // upgrades (which purge offline-cache-*) keep them.
  const SHEET_CACHE = 'mh-sheet-music-v2';
  const LEGACY_CACHES = ['mh-sheet-music-v1'];
  const KEY_DB = 'mh-sheet-music';
  // /api/pdf rejects requests without this header (plain links / address-bar downloads).
  const CLIENT_HEADERS = { 'X-MobiHymn-Client': 'web' };
  const norm = (n) => String(n).trim();
  const pdfUrl = (n) => '/api/pdf?n=' + encodeURIComponent(norm(n));
  const offlineKey = (n) => '/__sheet-music/' + encodeURIComponent(norm(n).toLowerCase());

  if ('caches' in window) {
    for (const name of LEGACY_CACHES) caches.delete(name).catch(() => { /* ignore */ });
  }

  let keyPromise = null;
  function cryptoKey() {
    if (!keyPromise) {
      keyPromise = new Promise((resolve, reject) => {
        const req = indexedDB.open(KEY_DB, 1);
        req.onupgradeneeded = () => req.result.createObjectStore('keys');
        req.onerror = () => reject(req.error);
        req.onsuccess = () => {
          const db = req.result;
          const get = db.transaction('keys').objectStore('keys').get('aes');
          get.onerror = () => reject(get.error);
          get.onsuccess = async () => {
            if (get.result) return resolve(get.result);
            try {
              const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
              const tx = db.transaction('keys', 'readwrite');
              tx.objectStore('keys').put(key, 'aes');
              tx.oncomplete = () => resolve(key);
              tx.onerror = () => reject(tx.error);
            } catch (e) { reject(e); }
          };
        };
      });
      keyPromise.catch(() => { keyPromise = null; });
    }
    return keyPromise;
  }

  async function encrypt(bytes) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const data = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, await cryptoKey(), bytes));
    const out = new Uint8Array(iv.length + data.length);
    out.set(iv);
    out.set(data, iv.length);
    return out;
  }

  async function decrypt(blob) {
    const all = new Uint8Array(blob);
    return crypto.subtle.decrypt({ name: 'AES-GCM', iv: all.subarray(0, 12) }, await cryptoKey(), all.subarray(12));
  }

  async function openCache() {
    return 'caches' in window ? caches.open(SHEET_CACHE) : null;
  }

  async function cachedResponse(n) {
    try {
      const c = await openCache();
      return c ? (await c.match(offlineKey(n))) || null : null;
    } catch { return null; }
  }

  /** Decrypted offline copy, or null. Unreadable entries (e.g. key lost) are dropped. */
  async function readOffline(n) {
    const r = await cachedResponse(n);
    if (!r) return null;
    try {
      return { bytes: await decrypt(await r.arrayBuffer()), etag: r.headers.get('ETag') || '' };
    } catch {
      try { (await openCache()).delete(offlineKey(n)); } catch { /* ignore */ }
      return null;
    }
  }

  async function fetchPdf(n) {
    const r = await fetch(pdfUrl(n), { headers: CLIENT_HEADERS, cache: 'no-store' });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return { bytes: await r.arrayBuffer(), etag: r.headers.get('ETag') || '' };
  }

  async function storeOffline(n, bytes, etag) {
    const c = await openCache();
    if (!c || !(window.crypto && crypto.subtle)) throw new Error('Offline storage unavailable');
    await c.put(offlineKey(n), new Response(await encrypt(bytes), {
      headers: { 'Content-Type': 'application/octet-stream', 'ETag': etag || '', 'X-Saved-At': new Date().toISOString() }
    }));
    try { if (navigator.storage && navigator.storage.persist) await navigator.storage.persist(); } catch { /* ignore */ }
  }

  /** Refresh a saved copy when Storage has a newer generation. */
  async function revalidate(n, cachedEtag) {
    if (!navigator.onLine) return;
    try {
      const head = await fetch(pdfUrl(n), { method: 'HEAD', headers: CLIENT_HEADERS, cache: 'no-store' });
      if (head.status === 404) return;
      const etag = head.headers.get('ETag') || '';
      if (!head.ok || !etag || etag === cachedEtag) return;
      const fresh = await fetchPdf(n);
      if (await cachedResponse(n)) await storeOffline(n, fresh.bytes, fresh.etag);
    } catch { /* keep the saved copy */ }
  }

  const sheet = {
    /** 'yes' | 'no' | 'unknown' (offline / server error — worth asking again later). */
    async availability(n) {
      if (await cachedResponse(n)) return 'yes';
      try {
        const r = await fetch(pdfUrl(n), { method: 'HEAD', headers: CLIENT_HEADERS, cache: 'no-store' });
        if (r.ok) return 'yes';
        return r.status === 404 ? 'no' : 'unknown';
      } catch { return 'unknown'; }
    },
    async isSaved(n) { return !!(await cachedResponse(n)); },
    async save(n) {
      const s = state;
      if (s && s.number === norm(n) && s.bytes) {
        await storeOffline(n, s.bytes, s.etag);
      } else {
        const f = await fetchPdf(n);
        await storeOffline(n, f.bytes, f.etag);
      }
      return true;
    },
    async remove(n) {
      const c = await openCache();
      if (c) await c.delete(offlineKey(n));
      return false;
    },
    /** { count, bytes } of saved PDFs. */
    async savedSummary() {
      const c = await openCache();
      if (!c) return { count: 0, bytes: 0 };
      let count = 0, bytes = 0;
      for (const req of await c.keys()) {
        const r = await c.match(req);
        if (!r) continue;
        count++;
        bytes += (await r.blob()).size;
      }
      return { count, bytes };
    },
    async clearSaved() {
      try { await caches.delete(SHEET_CACHE); } catch { /* ignore */ }
    },
    /** Native share sheet, else copy to clipboard: 'shared' | 'copied' | 'cancelled' | 'failed'. */
    async share(url, title, text) {
      if (navigator.share) {
        try {
          await navigator.share({ title, text, url });
          return 'shared';
        } catch (e) {
          if (e && e.name === 'AbortError') return 'cancelled';
        }
      }
      try {
        await navigator.clipboard.writeText(url);
        return 'copied';
      } catch {
        return 'failed';
      }
    }
  };

  /** Opens hymn n; returns { pages, saved }. Saved copies open offline-first. */
  async function open(host, n, dotnetRef) {
    close();
    const number = norm(n);
    const s = { host, number, ref: dotnetRef || null, zoom: 1, gen: 0, pages: [], doc: null, listeners: [] };
    state = s;

    const [lib, cached] = await Promise.all([loadLib(), readOffline(number)]);
    if (state !== s) return { pages: 0, saved: false };
    let bytes, etag;
    if (cached) {
      ({ bytes, etag } = cached);
      revalidate(number, etag);
    } else {
      ({ bytes, etag } = await fetchPdf(number));
    }
    if (state !== s) return { pages: 0, saved: false };
    // PDF.js transfers its buffer to the worker; keep a copy for "Keep offline".
    s.bytes = bytes.slice(0);
    s.etag = etag;

    s.task = lib.getDocument({
      data: new Uint8Array(bytes),
      standardFontDataUrl: LIB + 'standard_fonts/',
      isEvalSupported: false
    });
    const doc = await s.task.promise;
    if (state !== s) { doc.destroy(); return { pages: 0, saved: false }; }
    s.doc = doc;

    host.replaceChildren();
    s.inner = document.createElement('div');
    s.inner.className = 'pdf-pages';
    host.appendChild(s.inner);

    for (let i = 1; i <= doc.numPages; i++) {
      const page = await doc.getPage(i);
      if (state !== s) return { pages: 0, saved: false };
      const frame = document.createElement('div');
      frame.className = 'pdf-page';
      s.inner.appendChild(frame);
      s.pages.push({ page, frame, canvas: null, task: null });
    }

    wire(s);
    await render(s);
    return { pages: doc.numPages, saved: !!cached };
  }

  function baseWidth(s) {
    const style = getComputedStyle(s.inner);
    const pad = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
    return Math.max(120, s.host.clientWidth - pad);
  }

  function layout(s) {
    const cssW = baseWidth(s) * s.zoom;
    for (const p of s.pages) {
      const vp = p.page.getViewport({ scale: 1 });
      p.cssScale = cssW / vp.width;
      p.frame.style.width = cssW + 'px';
      p.frame.style.height = (vp.height * p.cssScale) + 'px';
    }
  }

  async function render(s) {
    const gen = ++s.gen;
    layout(s);
    const dprWanted = Math.min(window.devicePixelRatio || 1, 3);
    for (const p of s.pages) {
      if (state !== s || gen !== s.gen) return;
      if (p.task) { try { p.task.cancel(); } catch { /* ignore */ } }
      const vp1 = p.page.getViewport({ scale: p.cssScale });
      const dpr = Math.min(dprWanted, Math.sqrt(MAX_CANVAS_PIXELS / (vp1.width * vp1.height)));
      const viewport = p.page.getViewport({ scale: p.cssScale * dpr });
      const canvas = document.createElement('canvas');
      canvas.width = Math.floor(viewport.width);
      canvas.height = Math.floor(viewport.height);
      p.task = p.page.render({ canvasContext: canvas.getContext('2d'), viewport });
      try {
        await p.task.promise;
      } catch (e) {
        if (e && e.name === 'RenderingCancelledException') return;
        throw e;
      } finally {
        p.task = null;
      }
      if (state !== s || gen !== s.gen) return;
      // Swap only once drawn so zooming never flashes a blank page.
      if (p.canvas) p.canvas.replaceWith(canvas); else p.frame.appendChild(canvas);
      p.canvas = canvas;
    }
  }

  /**
   * Zoom so the content point under (ax, ay) ends up under (tx, ty) — host-relative px.
   * Without a target it stays put; the pinch passes the moved finger midpoint to pan as well.
   */
  function zoomTo(s, zoom, ax, ay, tx, ty) {
    zoom = clampZoom(zoom);
    const host = s.host;
    if (ax == null) { ax = host.clientWidth / 2; ay = host.clientHeight / 2; }
    if (tx == null) { tx = ax; ty = ay; }
    const ratio = zoom / s.zoom;
    const cx = host.scrollLeft + ax;
    const cy = host.scrollTop + ay;
    if (Math.abs(zoom - s.zoom) >= 0.01) {
      s.zoom = zoom;
      layout(s);
      render(s).catch(() => { /* ignore */ });
    }
    host.scrollLeft = cx * ratio - tx;
    host.scrollTop = cy * ratio - ty;
    return s.zoom;
  }

  function on(s, target, type, fn, opts) {
    target.addEventListener(type, fn, opts);
    s.listeners.push(() => target.removeEventListener(type, fn, opts));
  }

  function wire(s) {
    const host = s.host;
    let pinch = null;
    let tap = null;
    let lastTap = 0;
    let lastTouchEnd = 0;
    let resizeTimer = 0;
    let gestureZoom = 1;

    const rel = (t) => {
      const r = host.getBoundingClientRect();
      return { x: t.clientX - r.left, y: t.clientY - r.top };
    };
    const toggleZoom = (pt) => zoomTo(s, s.zoom > 1.05 ? 1 : 2.5, pt.x, pt.y);

    // Single tap toggles full screen immediately (class flipped here, Blazor catches up after).
    // A second tap within the double-tap window undoes that toggle and zooms instead.
    const viewerEl = host.closest('.pdf-viewer-host');
    const toggleFullscreen = () => {
      if (state !== s) return;
      const want = !immersive;
      setFullscreen(want);
      if (viewerEl) viewerEl.classList.toggle('is-fullscreen', want);
      if (s.ref) s.ref.invokeMethodAsync('OnFullscreenChanged', want).catch(() => { /* disposed */ });
    };
    let tapToggledAt = 0;
    const singleTap = () => { toggleFullscreen(); tapToggledAt = Date.now(); };
    const doubleTap = (pt) => {
      if (Date.now() - tapToggledAt < DOUBLE_TAP_MS + 100) toggleFullscreen();
      tapToggledAt = 0;
      toggleZoom(pt);
    };

    on(s, host, 'touchstart', (e) => {
      if (e.touches.length === 1) {
        const pt = rel(e.touches[0]);
        tap = { x: pt.x, y: pt.y, t: Date.now() };
      } else {
        tap = null;
      }
      if (e.touches.length === 2) {
        const [a, b] = [rel(e.touches[0]), rel(e.touches[1])];
        pinch = {
          d0: Math.hypot(a.x - b.x, a.y - b.y) || 1,
          mx0: (a.x + b.x) / 2,
          my0: (a.y + b.y) / 2,
          mx: (a.x + b.x) / 2,
          my: (a.y + b.y) / 2,
          k: 1
        };
        s.inner.style.transformOrigin = `${host.scrollLeft + pinch.mx0}px ${host.scrollTop + pinch.my0}px`;
        e.preventDefault();
      }
    }, { passive: false });

    on(s, host, 'touchmove', (e) => {
      if (tap && e.touches.length === 1) {
        const pt = rel(e.touches[0]);
        if (Math.hypot(pt.x - tap.x, pt.y - tap.y) > 10) tap = null;
      }
      if (!pinch || e.touches.length !== 2) return;
      const [a, b] = [rel(e.touches[0]), rel(e.touches[1])];
      const d = Math.hypot(a.x - b.x, a.y - b.y);
      pinch.k = clampZoom(s.zoom * d / pinch.d0) / s.zoom;
      pinch.mx = (a.x + b.x) / 2;
      pinch.my = (a.y + b.y) / 2;
      // Scale around where the fingers started and follow the midpoint, so two-finger pan works mid-pinch.
      s.inner.style.transform = `translate(${pinch.mx - pinch.mx0}px, ${pinch.my - pinch.my0}px) scale(${pinch.k})`;
      e.preventDefault();
    }, { passive: false });

    on(s, host, 'touchend', (e) => {
      lastTouchEnd = Date.now();
      if (pinch && e.touches.length < 2) {
        const p = pinch;
        pinch = null;
        tap = null;
        s.inner.style.transform = '';
        zoomTo(s, s.zoom * p.k, p.mx0, p.my0, p.mx, p.my);
        lastTap = 0;
        return;
      }
      if (e.touches.length !== 0 || e.changedTouches.length !== 1) return;
      const wasTap = tap && Date.now() - tap.t < 350;
      tap = null;
      if (!wasTap) { lastTap = 0; return; }
      const now = Date.now();
      if (now - lastTap < DOUBLE_TAP_MS) {
        doubleTap(rel(e.changedTouches[0]));
        lastTap = 0;
        e.preventDefault();
      } else {
        lastTap = now;
        singleTap();
      }
    }, { passive: false });

    // Mouse: click = full screen, double-click = zoom. Ignore the click browsers synthesize after a touch.
    on(s, host, 'click', (e) => {
      if (Date.now() - lastTouchEnd < 800 || e.detail > 1) return;
      singleTap();
    });
    on(s, host, 'dblclick', (e) => {
      if (Date.now() - lastTouchEnd < 800) return;
      doubleTap(rel(e));
    });

    // Trackpad pinch (Chrome/Edge/Firefox report it as ctrl+wheel) and Ctrl/⌘ + mouse wheel.
    on(s, host, 'wheel', (e) => {
      if (!e.ctrlKey && !e.metaKey) return;
      e.preventDefault();
      const pt = rel(e);
      const delta = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY;
      zoomTo(s, s.zoom * Math.exp(-delta * 0.01), pt.x, pt.y);
    }, { passive: false });

    // Safari trackpad pinch. On iOS these also fire for touch pinches, which the touch handlers own.
    on(s, host, 'gesturestart', (e) => { e.preventDefault(); gestureZoom = s.zoom; });
    on(s, host, 'gesturechange', (e) => {
      e.preventDefault();
      if (pinch) return;
      const pt = rel(e);
      zoomTo(s, gestureZoom * e.scale, pt.x, pt.y);
    });
    on(s, host, 'gestureend', (e) => e.preventDefault());

    // No "Save image as…" / drag-out of rendered pages: sheet music stays inside the app.
    on(s, host, 'contextmenu', (e) => e.preventDefault());
    on(s, host, 'dragstart', (e) => e.preventDefault());

    on(s, window, 'resize', () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => render(s).catch(() => { /* ignore */ }), 150);
    });
  }

  function zoomBy(factor) {
    const s = state;
    if (!s || !s.doc) return 1;
    return zoomTo(s, s.zoom * factor);
  }

  // ---- Full screen: the Blazor component hides the viewer's top bar; this mirrors that state for taps. ----
  let immersive = false;

  function setFullscreen(on) {
    immersive = !!on;
  }

  function close() {
    immersive = false;
    const s = state;
    state = null;
    if (!s) return;
    s.gen++;
    for (const off of s.listeners) off();
    for (const p of s.pages) { if (p.task) { try { p.task.cancel(); } catch { /* ignore */ } } }
    try { if (s.task) s.task.destroy(); } catch { /* ignore */ }
    try { s.host.replaceChildren(); } catch { /* ignore */ }
  }

  window.mhPdfViewer = { open, zoomBy, close, setFullscreen };
  window.mhSheetMusic = sheet;
})();
