/* eslint-disable no-undef */
// FCM background handler for board push (separate from Blazor offline SW).
importScripts('./js/firebase-config.js');
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js');

const cfg = self.mobihymnFirebaseConfig || {};
if (cfg.apiKey && cfg.appId) {
  firebase.initializeApp(cfg);
  firebase.messaging();
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const raw = event.notification.data || {};
  // FCM may nest payload under FCM_MSG.
  const data = raw.FCM_MSG && raw.FCM_MSG.data ? raw.FCM_MSG.data : raw;
  const groupId = (data.groupId || '').toString();
  const listId = (data.listId || data.date || '').toString();

  event.waitUntil((async () => {
    const all = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of all) {
      if (client.url.startsWith(self.location.origin) && 'focus' in client) {
        client.postMessage({ type: 'boardNotificationClick', groupId, listId });
        return client.focus();
      }
    }
    if (clients.openWindow) {
      const url = new URL('/', self.location.origin);
      if (groupId) {
        url.searchParams.set('openBoard', '1');
        url.searchParams.set('groupId', groupId);
        if (listId) url.searchParams.set('listId', listId);
      }
      return clients.openWindow(url.href);
    }
  })());
});
