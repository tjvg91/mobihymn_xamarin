// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
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
    // Activate this worker immediately so deploys aren't stuck behind an old cache.
    self.skipWaiting();

    const cache = await caches.open(cacheName);
    const assets = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));

    // Cache one-by-one so a single integrity/network failure cannot abort the whole install
    // (cache.addAll fails atomically and has caused hung launches on iOS PWAs).
    for (const asset of assets) {
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

async function onActivate(event) {
    console.info('Service worker: Activate');

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    await self.clients.claim();
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
