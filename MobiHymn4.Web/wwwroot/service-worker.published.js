// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');

// --- Firebase Cloud Messaging -------------------------------------------------
// IMPORTANT: this MUST live in the same SW registration/scope as the offline-cache
// logic below, not a separate registered script. Two different scripts registered
// at the same scope ("/") fight over ONE ServiceWorkerRegistration slot.
self.importScripts('./js/firebase-config.js');
self.importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js');
self.importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js');

function fcmParseNotifData(notification) {
    const raw = (notification && notification.data) || {};
    const nested = raw.FCM_MSG && (raw.FCM_MSG.data || raw.FCM_MSG.notification)
        ? (raw.FCM_MSG.data || {})
        : {};
    const data = Object.assign({}, nested, raw);
    let groupId = (data.groupId || '').toString();
    let listId = (data.listId || data.date || '').toString();
    let boardPath = (data.boardPath || '').toString();
    if (!listId && notification && typeof notification.tag === 'string') {
        const m = /^board-(.+)$/.exec(notification.tag);
        if (m) listId = m[1];
    }
    return { groupId, listId, boardPath };
}

function fcmBoardHref(origin, groupId, listId, boardPath) {
    if (boardPath) {
        if (/^https?:\/\//i.test(boardPath)) return boardPath;
        return origin + (boardPath.startsWith('/') ? boardPath : '/' + boardPath);
    }
    if (groupId && listId)
        return origin + '/read?groupId=' + encodeURIComponent(groupId) + '&listId=' + encodeURIComponent(listId);
    if (groupId)
        return origin + '/read?groupId=' + encodeURIComponent(groupId);
    return origin + '/';
}

/**
 * Register click FIRST and stopImmediatePropagation so Firebase's default
 * notificationclick (installed by firebase.messaging()) cannot hijack taps.
 * openWindow(https) opens Chrome — not the Play Store TWA — so cold starts
 * go through /launch-app.html which fires an Android intent into the TWA.
 */
self.addEventListener('notificationclick', (event) => {
    event.stopImmediatePropagation();
    event.notification.close();
    const { groupId, listId, boardPath } = fcmParseNotifData(event.notification);
    const target = fcmBoardHref(self.location.origin, groupId, listId, boardPath);
    const msg = {
        type: 'boardNotificationClick',
        groupId: groupId || '',
        listId: listId || '',
        boardPath: boardPath || '',
        url: target
    };
    console.log('[BoardOpen][sw] notificationclick', msg);

    event.waitUntil((async () => {
        const all = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        console.log('[BoardOpen][sw] matchAll found', all.length, 'window client(s)');
        let focused = null;
        for (const client of all) {
            if (!client.url.startsWith(self.location.origin)) continue;
            if (client.url.indexOf('/launch-app.html') >= 0) continue;
            try {
                client.postMessage(msg);
                console.log('[BoardOpen][sw] postMessage sent to client', client.url);
            } catch (e) { console.warn('[BoardOpen][sw] postMessage failed', e); }
            try {
                if ('focus' in client) {
                    await client.focus();
                    focused = client;
                }
            } catch (e) { console.warn('[BoardOpen][sw] focus failed', e); }
        }

        if (focused) {
            try {
                if (typeof focused.navigate === 'function')
                    await focused.navigate(target);
            } catch { /* iOS often rejects navigate; postMessage already sent */ }
            return;
        }

        if (!clients.openWindow) return;
        try {
            const u = new URL(target, self.location.origin);
            const to = u.pathname + u.search + u.hash;
            const launch = self.location.origin + '/launch-app.html?to=' + encodeURIComponent(to);
            console.log('[BoardOpen][sw] opening via TWA bridge', launch);
            return clients.openWindow(launch);
        } catch (e) {
            console.warn('[BoardOpen][sw] bridge failed, raw openWindow', e);
            return clients.openWindow(target);
        }
    })());
});

/**
 * Always paint a user-visible notification for data-only pushes. When the payload
 * already includes `notification`, Chrome/FCM may auto-display — we still show with
 * the same tag so the tray never falls back to "updated in the background".
 */
function fcmShowBoardNotification(payload) {
    try {
        const raw = payload && typeof payload === 'object' ? payload : {};
        const d = (raw.data && typeof raw.data === 'object') ? raw.data : raw;
        const n = (raw.notification && typeof raw.notification === 'object') ? raw.notification : {};
        const updatedBy = String(d.updatedBy || '');
        const me = String(self.__mhAuthUid || '');
        if (me && updatedBy && me === updatedBy) {
            console.log('[BoardOpen][sw] skip self-update notification', { updatedBy });
            return Promise.resolve();
        }
        const title = String(n.title || d.title || 'MobiHymn');
        const body = String(n.body || d.body || '');
        const groupId = String(d.groupId || '');
        const listId = String(d.listId || d.date || '');
        const boardPath = String(d.boardPath || '');
        const tag = listId ? ('board-' + listId) : (groupId ? ('board-' + groupId) : 'board');
        console.log('[BoardOpen][sw] showNotification', { title, body, groupId, listId });
        return self.registration.showNotification(title, {
            body,
            icon: '/icon-192.png',
            badge: '/icon-192.png',
            tag,
            renotify: true,
            data: Object.assign({}, d, { groupId, listId, boardPath, title, body })
        });
    } catch (e) {
        console.warn('[BoardOpen][sw] showNotification failed, using fallback', e);
        return self.registration.showNotification('MobiHymn', {
            body: 'Board updated',
            icon: '/icon-192.png',
            tag: 'board'
        });
    }
}

self.addEventListener('message', (event) => {
    try {
        const data = event && event.data;
        if (data && data.type === 'mh-auth')
            self.__mhAuthUid = data.uid ? String(data.uid) : '';
    } catch { /* ignore */ }
});

self.addEventListener('push', (event) => {
    let payload = {};
    try {
        payload = event.data ? event.data.json() : {};
    } catch (e) {
        try {
            const text = event.data && event.data.text();
            payload = text ? { data: { body: String(text).slice(0, 120) } } : {};
        } catch { /* ignore */ }
    }
    console.log('[BoardOpen][sw] push', payload);
    event.waitUntil(fcmShowBoardNotification(payload));
});

(function initFcm() {
    const cfg = self.mobihymnFirebaseConfig || {};
    if (!cfg.apiKey || !cfg.appId) return;
    try {
        firebase.initializeApp(cfg);
        // Init AFTER our notificationclick listener so we win the handler race.
        firebase.messaging();
    } catch (e) {
        console.warn('[BoardOpen][sw] messaging init failed', e);
    }
})();
// --- end Firebase Cloud Messaging ---------------------------------------------

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING')
        self.skipWaiting();
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.svg$/, /\.blat$/, /\.dat$/, /\.ttf$/, /\.otf$/, /\.woff2$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');
    // Activate ASAP. Precaching the full Blazor payload during install can take
    // minutes on mobile after clear-data and leaves the worker stuck in
    // "installing" — FCM getToken / serviceWorker.ready hang and Account
    // "Register" stays on Registering forever.
    self.skipWaiting();

    const cache = await caches.open(cacheName);
    const critical = self.assetsManifest.assets.filter(asset => {
        const u = asset.url || '';
        return u === 'index.html'
            || /(^|\/)index\.html$/i.test(u)
            || /\.(css|webmanifest)$/i.test(u)
            || u.startsWith('js/')
            || u.startsWith('css/')
            || /^icon-/i.test(u)
            || /splash/i.test(u)
            || u === 'manifest.webmanifest';
    });

    for (const asset of critical) {
        try {
            const request = new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' });
            const response = await fetch(request);
            if (response && response.ok)
                await cache.put(request, response);
        } catch (err) {
            console.warn('Service worker: skip asset', asset.url, err);
        }
    }
}

async function precacheRemaining() {
    try {
        const cache = await caches.open(cacheName);
        const assets = self.assetsManifest.assets
            .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
            .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));
        for (const asset of assets) {
            try {
                const request = new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' });
                if (await cache.match(request)) continue;
                const response = await fetch(request);
                if (response && response.ok)
                    await cache.put(request, response);
            } catch { /* ignore individual failures */ }
        }
        console.info('Service worker: background precache done');
    } catch (e) {
        console.warn('Service worker: background precache failed', e);
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    await self.clients.claim();
    // Do not event.waitUntil(precache) — that would still block activation.
    precacheRemaining();
}

async function networkFirst(request, timeoutMs) {
    const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    const timer = controller
        ? setTimeout(() => controller.abort(), timeoutMs)
        : null;
    try {
        return await fetch(request, controller ? { signal: controller.signal } : undefined);
    } finally {
        if (timer) clearTimeout(timer);
    }
}

function isNavigation(request, url) {
    return request.mode === 'navigate'
        || url.pathname === '/'
        || url.pathname.endsWith('.html');
}

/** Shell assets that change every deploy — never prefer a stale cache hit. */
function isShellAsset(url) {
    if (url.pathname.startsWith('/_framework/'))
        return false;
    return /\.(css|js|webmanifest|json)$/i.test(url.pathname)
        || url.pathname.startsWith('/js/')
        || url.pathname.startsWith('/css/')
        || url.pathname.startsWith('/fonts/');
}

/** Content-hashed Blazor outputs — safe to cache-first. */
function isImmutableFramework(url) {
    return url.pathname.startsWith('/_framework/');
}

async function matchCache(request, url) {
    try {
        const cache = await caches.open(cacheName);
        let hit = await cache.match(request);
        // Only fall back to pathname (ignore ?v=) when offline — otherwise deploys look stuck.
        if (!hit && url.search)
            hit = await cache.match(url.pathname);
        return hit;
    } catch {
        return null;
    }
}

async function putCache(requestOrPath, response) {
    try {
        const cache = await caches.open(cacheName);
        await cache.put(requestOrPath, response);
    } catch { /* ignore */ }
}

async function onFetch(event) {
    const url = new URL(event.request.url);

    // Never intercept Firebase Auth handlers or our email-action page.
    if (url.pathname.startsWith('/__/auth') || url.pathname.startsWith('/auth/action')) {
        return fetch(event.request);
    }

    // Never intercept service-worker scripts (FCM SW install / importScripts).
    if (url.pathname === '/firebase-messaging-sw.js'
        || url.pathname === '/service-worker.js'
        || url.pathname === '/service-worker-assets.js'
        || url.pathname === '/service-worker.published.js'
        || url.pathname === '/js/firebase-config.js'
        || url.pathname === '/launch-app.html') {
        return fetch(event.request);
    }

    // Always network for hymn API proxy (lyrics/agent/audio) — never serve stale API data.
    if (url.pathname.startsWith('/api/hymn') || url.pathname.startsWith('/api/midi')) {
        try {
            return await fetch(event.request);
        } catch {
            return new Response('Offline', { status: 503, statusText: 'Offline' });
        }
    }

    // Cross-origin (Firebase CDNs, fonts, etc.) — do not intercept.
    if (url.origin !== self.location.origin)
        return fetch(event.request);

    if (event.request.method !== 'GET')
        return fetch(event.request);

    // HTML + CSS/JS/fonts/manifest: network-first so a reopen picks up deploys.
    if (isNavigation(event.request, url) || isShellAsset(url)) {
        try {
            const fresh = await networkFirst(event.request, 8000);
            if (fresh && fresh.ok) {
                const storeAs = isNavigation(event.request, url) ? 'index.html' : url.pathname;
                await putCache(storeAs, fresh.clone());
                return fresh;
            }
        } catch { /* fall through */ }

        const cached = await matchCache(event.request, url);
        if (cached) return cached;

        if (isNavigation(event.request, url)) {
            try {
                const cache = await caches.open(cacheName);
                const fallback = await cache.match('index.html');
                if (fallback) return fallback;
            } catch { /* ignore */ }
            return new Response('Offline', { status: 503, statusText: 'Offline' });
        }

        return fetch(event.request);
    }

    // _framework hashed bundles: cache-first
    if (isImmutableFramework(url)) {
        const cached = await matchCache(event.request, url);
        if (cached) return cached;
        try {
            const fresh = await fetch(event.request);
            if (fresh && fresh.ok)
                await putCache(event.request, fresh.clone());
            return fresh;
        } catch (err) {
            if (cached) return cached;
            throw err;
        }
    }

    // Images / other: cache-first, refresh in background when possible
    const cached = await matchCache(event.request, url);
    if (cached) {
        event.waitUntil((async () => {
            try {
                const fresh = await fetch(event.request);
                if (fresh && fresh.ok)
                    await putCache(url.pathname, fresh.clone());
            } catch { /* ignore */ }
        })());
        return cached;
    }

    try {
        const fresh = await fetch(event.request);
        if (fresh && fresh.ok)
            await putCache(url.pathname, fresh.clone());
        return fresh;
    } catch (err) {
        throw err;
    }
}
