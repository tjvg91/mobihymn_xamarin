/* eslint-disable no-undef */
// Dedicated FCM service worker (narrow scope). Kept separate from Blazor's
// offline service-worker.js so getToken() is not blocked by Blazor precache.
// Use root-absolute importScripts — relative paths break depending on scope.
try {
  importScripts('/js/firebase-config.js');
  importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js');
  importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js');
} catch (e) {
  console.warn('[BoardOpen][fcm-sw] importScripts failed', e);
}

self.addEventListener('install', (event) => {
  self.skipWaiting();
  event.waitUntil(Promise.resolve());
});
self.addEventListener('activate', (event) => {
  event.waitUntil(Promise.all([
    self.clients.claim(),
    restoreAuthUid()
  ]));
});

async function restoreAuthUid() {
  try {
    const cache = await caches.open('mh-auth-v1');
    const res = await cache.match('/__uid');
    if (res) self.__mhAuthUid = (await res.text()) || '';
  } catch { /* ignore */ }
}

async function setAuthUid(uid) {
  self.__mhAuthUid = uid ? String(uid) : '';
  try {
    const cache = await caches.open('mh-auth-v1');
    if (self.__mhAuthUid)
      await cache.put('/__uid', new Response(self.__mhAuthUid, { headers: { 'Content-Type': 'text/plain' } }));
    else
      await cache.delete('/__uid');
  } catch { /* ignore */ }
}

async function currentAuthUid() {
  const mem = String(self.__mhAuthUid || '');
  if (mem) return mem;
  await restoreAuthUid();
  return String(self.__mhAuthUid || '');
}

function parseNotifData(notification) {
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

function boardHref(origin, groupId, listId, boardPath) {
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

/** openWindow(https) opens Chrome, not the Play Store TWA — bridge via launch-app.html. */
function openBoardClient(target, msg) {
  return (async () => {
    const all = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    console.log('[BoardOpen][fcm-sw] matchAll found', all.length, 'window client(s)');
    let focused = null;
    for (const client of all) {
      if (!client.url.startsWith(self.location.origin)) continue;
      // Skip the intent-bridge tab if it is still around.
      if (client.url.indexOf('/launch-app.html') >= 0) continue;
      try { client.postMessage(msg); } catch { /* ignore */ }
      try {
        if ('focus' in client) {
          await client.focus();
          focused = client;
        }
      } catch { /* ignore */ }
    }
    if (focused) {
      try {
        if (typeof focused.navigate === 'function')
          await focused.navigate(target);
      } catch { /* ignore */ }
      return;
    }
    if (!clients.openWindow) return;
    try {
      const u = new URL(target, self.location.origin);
      const to = u.pathname + u.search + u.hash;
      const launch = self.location.origin + '/launch-app.html?to=' + encodeURIComponent(to);
      console.log('[BoardOpen][fcm-sw] opening via TWA bridge', launch);
      return clients.openWindow(launch);
    } catch (e) {
      console.warn('[BoardOpen][fcm-sw] bridge failed, raw openWindow', e);
      return clients.openWindow(target);
    }
  })();
}

function showFromPayload(payload) {
  try {
    const raw = payload && typeof payload === 'object' ? payload : {};
    const d = (raw.data && typeof raw.data === 'object') ? raw.data : raw;
    const n = (raw.notification && typeof raw.notification === 'object') ? raw.notification : {};
    const updatedBy = String(d.updatedBy || '');
    return currentAuthUid().then((me) => {
      // Same Firebase account — suppress tray for own edits.
      if (me && updatedBy && me === updatedBy) {
        console.log('[BoardOpen][fcm-sw] skip self-update notification', { updatedBy });
        return;
      }
      const title = String(n.title || d.title || 'MobiHymn');
      const body = String(n.body || d.body || '');
      const groupId = String(d.groupId || '');
      const listId = String(d.listId || d.date || '');
      const boardPath = String(d.boardPath || '');
      const tag = listId ? ('board-' + listId) : (groupId ? ('board-' + groupId) : 'board');
      console.log('[BoardOpen][fcm-sw] showNotification', { title, body, groupId, listId });
      return self.registration.showNotification(title, {
        body,
        icon: '/icon-192.png',
        badge: '/icon-192.png',
        tag,
        renotify: true,
        data: Object.assign({}, d, { groupId, listId, boardPath, title, body })
      });
    });
  } catch (e) {
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
      event.waitUntil(setAuthUid(data.uid ? String(data.uid) : ''));
  } catch { /* ignore */ }
});

self.addEventListener('notificationclick', (event) => {
  event.stopImmediatePropagation();
  event.notification.close();
  const { groupId, listId, boardPath } = parseNotifData(event.notification);
  const target = boardHref(self.location.origin, groupId, listId, boardPath);
  const msg = {
    type: 'boardNotificationClick',
    groupId: groupId || '',
    listId: listId || '',
    boardPath: boardPath || '',
    url: target
  };
  console.log('[BoardOpen][fcm-sw] notificationclick', msg);
  event.waitUntil(openBoardClient(target, msg));
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
  console.log('[BoardOpen][fcm-sw] push', payload);
  event.waitUntil(showFromPayload(payload));
});

try {
  const cfg = self.mobihymnFirebaseConfig || {};
  if (cfg.apiKey && cfg.appId && typeof firebase !== 'undefined') {
    firebase.initializeApp(cfg);
    firebase.messaging();
  }
} catch (e) {
  console.warn('[BoardOpen][fcm-sw] messaging init failed', e);
}
