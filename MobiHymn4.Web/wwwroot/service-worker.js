// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

// --- Firebase Cloud Messaging (dev parity with service-worker.published.js) ----
try {
    self.importScripts('./js/firebase-config.js');
    self.importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js');
    self.importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js');

    function fcmShowBoardNotification(payload) {
        const raw = payload && typeof payload === 'object' ? payload : {};
        const d = (raw.data && typeof raw.data === 'object') ? raw.data : raw;
        const n = (raw.notification && typeof raw.notification === 'object') ? raw.notification : {};
        const updatedBy = (d.updatedBy || '').toString();
        const me = (self.__mhAuthUid || '').toString();
        if (me && updatedBy && me === updatedBy) {
            console.log('[BoardOpen][sw-dev] skip self-update notification', { updatedBy });
            return Promise.resolve();
        }
        const title = (n.title || d.title || 'MobiHymn').toString();
        const body = (n.body || d.body || '').toString();
        const groupId = (d.groupId || '').toString();
        const listId = (d.listId || d.date || '').toString();
        const boardPath = (d.boardPath || '').toString();
        const tag = listId ? ('board-' + listId) : (groupId ? ('board-' + groupId) : 'board');
        console.log('[BoardOpen][sw-dev] showNotification', { title, body, groupId, listId });
        return self.registration.showNotification(title, {
            body, icon: '/icon-192.png', badge: '/icon-192.png', tag, renotify: true,
            data: Object.assign({}, d, { groupId, listId, boardPath, title, body })
        });
    }

    async function fcmSetAuthUid(uid) {
        self.__mhAuthUid = uid ? String(uid) : '';
        try {
            const cache = await caches.open('mh-auth-v1');
            if (self.__mhAuthUid)
                await cache.put('/__uid', new Response(self.__mhAuthUid, { headers: { 'Content-Type': 'text/plain' } }));
            else
                await cache.delete('/__uid');
        } catch { /* ignore */ }
    }

    async function fcmRestoreAuthUid() {
        try {
            const cache = await caches.open('mh-auth-v1');
            const res = await cache.match('/__uid');
            if (res) self.__mhAuthUid = (await res.text()) || '';
        } catch { /* ignore */ }
    }

    self.addEventListener('activate', (event) => {
        event.waitUntil(fcmRestoreAuthUid());
    });

    self.addEventListener('message', (event) => {
        try {
            const data = event && event.data;
            if (data && data.type === 'mh-auth')
                event.waitUntil(fcmSetAuthUid(data.uid ? String(data.uid) : ''));
        } catch { /* ignore */ }
    });

    self.addEventListener('notificationclick', (event) => {
        event.stopImmediatePropagation();
        event.notification.close();
        const raw = (event.notification && event.notification.data) || {};
        const nested = raw.FCM_MSG && (raw.FCM_MSG.data || raw.FCM_MSG.notification)
            ? (raw.FCM_MSG.data || {})
            : {};
        const data = Object.assign({}, nested, raw);
        const groupId = (data.groupId || '').toString();
        const listId = (data.listId || data.date || '').toString();
        const boardPath = (data.boardPath || '').toString();
        const target = boardPath
            ? (self.location.origin + (boardPath.startsWith('/') ? boardPath : '/' + boardPath))
            : (groupId
                ? self.location.origin + '/read?groupId=' + encodeURIComponent(groupId) + (listId ? '&listId=' + encodeURIComponent(listId) : '')
                : self.location.origin + '/');
        const msg = { type: 'boardNotificationClick', groupId, listId, boardPath, url: target };
        console.log('[BoardOpen][sw-dev] notificationclick', msg);
        event.waitUntil((async () => {
            const all = await clients.matchAll({ type: 'window', includeUncontrolled: true });
            let focused = null;
            for (const client of all) {
                if (!client.url.startsWith(self.location.origin)) continue;
                try { client.postMessage(msg); } catch { /* ignore */ }
                try { if ('focus' in client) { await client.focus(); focused = client; } } catch { /* ignore */ }
            }
            if (!focused && clients.openWindow) return clients.openWindow(target);
        })());
    });

    self.addEventListener('push', (event) => {
        let payload = {};
        try {
            payload = event.data ? event.data.json() : {};
        } catch (e) {
            try {
                const text = event.data && event.data.text();
                payload = text ? { data: { body: text } } : {};
            } catch { /* ignore */ }
        }
        console.log('[BoardOpen][sw-dev] push event', payload);
        event.waitUntil(fcmShowBoardNotification(payload));
    });

    const cfg = self.mobihymnFirebaseConfig || {};
    if (cfg.apiKey && cfg.appId) {
        firebase.initializeApp(cfg);
        firebase.messaging();
    }
} catch (e) {
    console.warn('[BoardOpen][sw-dev] FCM setup failed', e);
}
