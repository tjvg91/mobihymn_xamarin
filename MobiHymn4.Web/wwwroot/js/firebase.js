// Fill firebaseConfig from Firebase Console → Project settings → Your apps → Web app.
// Until configured, ensureInit returns false and auth/cloud features stay disabled.
window.mobihymnFirebaseConfig = window.mobihymnFirebaseConfig || {
  apiKey: "",
  authDomain: "mobihymn.firebaseapp.com",
  databaseURL: "https://mobihymn.firebaseio.com",
  projectId: "mobihymn",
  storageBucket: "mobihymn.appspot.com",
  messagingSenderId: "525477034225",
  appId: ""
};

window.mobihymnFirebase = (function () {
  let app = null;
  let auth = null;
  let db = null;
  let messaging = null;
  let messagingSwReg = null;
  let ready = false;

  function midiObjectPath(number) {
    const cfg = window.mobihymnFirebaseConfig || {};
    const template = cfg.midiPathTemplate || "midi/h{n}.mid";
    const n = String(number || "").trim();
    return template
      .replaceAll("{n}", n)
      .replaceAll("{number}", n)
      .replaceAll("{N}", n);
  }

  async function ensureInit() {
    if (ready) return true;
    const cfg = window.mobihymnFirebaseConfig;
    if (!cfg || !cfg.apiKey || !cfg.appId) {
      console.warn("Firebase web config missing — set wwwroot/js/firebase-config.js");
      return false;
    }

    const { initializeApp } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-app.js");
    const {
      getAuth, setPersistence, browserLocalPersistence, onAuthStateChanged,
      signInWithEmailAndPassword, createUserWithEmailAndPassword,
      signOut, sendPasswordResetEmail, sendEmailVerification, updatePassword, reload
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-auth.js");
    const {
      getFirestore, doc, getDoc, setDoc, deleteDoc, collection, query, where, limit, getDocs, onSnapshot
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-firestore.js");
    const {
      getDatabase, ref, get
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-database.js");

    app = initializeApp(cfg);
    auth = getAuth(app);
    // Keep the signed-in account across refreshes / new tabs.
    await setPersistence(auth, browserLocalPersistence);
    db = getFirestore(app);
    const rtdb = cfg.databaseURL ? getDatabase(app) : null;

    window.__fb = { auth, db, rtdb, doc, getDoc, setDoc, deleteDoc, collection, query, where, limit, getDocs, onSnapshot,
      ref, get,
      signInWithEmailAndPassword, createUserWithEmailAndPassword, signOut, sendPasswordResetEmail,
      sendEmailVerification, updatePassword, reload, onAuthStateChanged };
    window.__fbAuthReady = false;
    window.__fbAuthReadyPromise = new Promise((resolve) => {
      const unsub = onAuthStateChanged(auth, (user) => {
        window.__fbAuthReady = true;
        unsub();
        resolve(user);
      });
    });

    ready = true;
    return true;
  }

  async function ensureMessaging() {
    if (!(await ensureInit())) return null;
    if (messaging) return messaging;
    if (typeof window === "undefined" || !("Notification" in window) || !("serviceWorker" in navigator))
      return null;
    try {
      const { getMessaging, isSupported } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging.js");
      if (!(await isSupported())) return null;
      messaging = getMessaging(app);
      return messaging;
    } catch (e) {
      console.warn("Firebase Messaging unavailable", e);
      return null;
    }
  }

  async function ensureMessagingSw() {
    if (messagingSwReg) return messagingSwReg;
    messagingSwReg = await navigator.serviceWorker.register("firebase-messaging-sw.js");
    return messagingSwReg;
  }

  function userDto(u) {
    if (!u) return null;
    return { uid: u.uid, email: u.email || "", emailVerified: !!u.emailVerified };
  }

  async function waitForAuthUser() {
    if (!(await ensureInit())) return null;
    if (!window.__fbAuthReady)
      await window.__fbAuthReadyPromise;
    return userDto(window.__fb.auth.currentUser);
  }

  function serializeFirestoreValue(value) {
    if (value == null) return null;
    // Firestore Timestamp (or anything with toDate)
    if (typeof value.toDate === "function") {
      try { return value.toDate().toISOString(); } catch { /* fall through */ }
    }
    if (typeof value.toJSON === "function" && value.seconds != null) {
      try {
        const j = value.toJSON();
        if (j && typeof j.seconds === "number")
          return new Date(j.seconds * 1000).toISOString();
      } catch { /* fall through */ }
    }
    if (Array.isArray(value)) return value.map(serializeFirestoreValue);
    if (typeof value === "object") {
      // Plain map / document fields
      if (Object.getPrototypeOf(value) === Object.prototype || value.constructor === Object) {
        const out = {};
        for (const k of Object.keys(value)) out[k] = serializeFirestoreValue(value[k]);
        return out;
      }
      // Last resort: Timestamp-like { seconds, nanoseconds }
      if (typeof value.seconds === "number")
        return new Date(value.seconds * 1000).toISOString();
    }
    return value;
  }

  function serializeDoc(snap) {
    if (!snap.exists()) return null;
    const data = serializeFirestoreValue(snap.data()) || {};
    return Object.assign({ id: snap.id, _id: snap.id }, data);
  }

  return {
    ensureInit,
    async getCurrentUser() {
      return await waitForAuthUser();
    },
    async signIn(email, password) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      try {
        await window.__fb.signInWithEmailAndPassword(window.__fb.auth, email, password);
      } catch (e) {
        throw new Error(e?.code || e?.message || "Sign in failed.");
      }
    },
    async signUp(email, password) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      try {
        await window.__fb.createUserWithEmailAndPassword(window.__fb.auth, email, password);
      } catch (e) {
        throw new Error(e?.code || e?.message || "Sign up failed.");
      }
    },
    async signOut() {
      if (!(await ensureInit())) return;
      await window.__fb.signOut(window.__fb.auth);
    },
    authContinueUrl() {
      // Web PWA continue target (MAUI uses https://mobihymn.firebaseapp.com/auth/continue).
      try {
        return new URL("auth/continue", window.location.origin + "/").toString();
      } catch {
        return window.location.origin + "/auth/continue";
      }
    },
    actionCodeSettings() {
      return {
        url: this.authContinueUrl(),
        handleCodeInApp: false
      };
    },
    async sendPasswordReset(email) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      try {
        await window.__fb.sendPasswordResetEmail(
          window.__fb.auth,
          email,
          this.actionCodeSettings()
        );
      } catch (e) {
        throw new Error(e?.code || e?.message || "Password reset failed.");
      }
    },
    async sendEmailVerification() {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const u = window.__fb.auth.currentUser;
      if (u) await window.__fb.sendEmailVerification(u, this.actionCodeSettings());
    },
    async updatePassword(password) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const u = window.__fb.auth.currentUser;
      if (!u) throw new Error("Not signed in.");
      await window.__fb.updatePassword(u, password);
    },
    async reloadUser() {
      if (!(await ensureInit())) return;
      const u = window.__fb.auth.currentUser;
      if (u) await window.__fb.reload(u);
    },
    async getDoc(path) {
      if (!(await ensureInit())) return null;
      // Ensure auth session is restored before Firestore rules evaluate.
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      const ref = window.__fb.doc(window.__fb.db, ...path.split("/").filter(Boolean));
      const snap = await window.__fb.getDoc(ref);
      return serializeDoc(snap);
    },
    /** JSON string (or null) so Blazor can bind without JsonElement? null failures. */
    async getDocAsJson(path) {
      const data = await this.getDoc(path);
      return data == null ? null : JSON.stringify(data);
    },
    async getCurrentUserAsJson() {
      const data = await this.getCurrentUser();
      return data == null ? null : JSON.stringify(data);
    },
    async getLatestReleaseAsJson(platform) {
      const data = await this.getLatestRelease(platform);
      return data == null ? null : JSON.stringify(data);
    },
    async setDoc(path, data) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const ref = window.__fb.doc(window.__fb.db, ...path.split("/").filter(Boolean));
      // Firestore rejects undefined; stringify via JSON to drop undefined.
      const clean = JSON.parse(JSON.stringify(data));
      await window.__fb.setDoc(ref, clean, { merge: true });
    },
    async deleteDoc(path) {
      if (!(await ensureInit())) return;
      const ref = window.__fb.doc(window.__fb.db, ...path.split("/").filter(Boolean));
      await window.__fb.deleteDoc(ref);
    },
    async queryCollection(path, field, op, value) {
      if (!(await ensureInit())) return [];
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      try {
        const col = window.__fb.collection(window.__fb.db, ...path.split("/").filter(Boolean));
        let q = col;
        if (field && op) q = window.__fb.query(col, window.__fb.where(field, op, value));
        const snap = await window.__fb.getDocs(q);
        const rows = [];
        snap.forEach(d => rows.push(serializeDoc(d)));
        return rows;
      } catch (e) {
        const msg = e?.code || e?.message || "Firestore query failed";
        console.warn("queryCollection failed", path, msg);
        throw new Error(msg);
      }
    },
    async queryCollectionGroup(collectionId, docIdEquals) {
      if (!(await ensureInit())) return [];
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      try {
        const {
          collectionGroup, query, where, getDocs, documentId
        } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-firestore.js");
        const col = collectionGroup(window.__fb.db, collectionId);
        const q = docIdEquals
          ? query(col, where(documentId(), "==", docIdEquals))
          : col;
        const snap = await getDocs(q);
        const rows = [];
        snap.forEach(d => {
          const data = serializeDoc(d) || {};
          const parts = d.ref.path.split("/");
          // groups/{groupId}/members/{uid}
          const groupId = parts.length >= 2 && parts[0] === "groups" ? parts[1] : "";
          rows.push(Object.assign(data, { groupId, _path: d.ref.path }));
        });
        return rows;
      } catch (e) {
        const msg = e?.code || e?.message || "Firestore collectionGroup query failed";
        console.warn("queryCollectionGroup failed", collectionId, msg);
        throw new Error(msg);
      }
    },
    async subscribeDoc(path, dotNetRef) {
      if (!(await ensureInit())) return null;
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      const ref = window.__fb.doc(window.__fb.db, ...path.split("/").filter(Boolean));
      const unsub = window.__fb.onSnapshot(ref, (snap) => {
        const data = serializeDoc(snap);
        // Pass JSON string — JS null cannot bind to JsonElement? in .NET.
        dotNetRef.invokeMethodAsync("OnSnapshot", data == null ? null : JSON.stringify(data));
      });
      const id = "sub_" + Math.random().toString(36).slice(2);
      window.__fbSubs = window.__fbSubs || {};
      window.__fbSubs[id] = unsub;
      return id;
    },
    async subscribeQuery(path, field, op, value, max, dotNetRef) {
      if (!(await ensureInit())) return null;
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      const col = window.__fb.collection(window.__fb.db, ...path.split("/").filter(Boolean));
      const parts = [];
      if (field && op) parts.push(window.__fb.where(field, op, value));
      if (typeof max === "number" && max > 0) parts.push(window.__fb.limit(max));
      const q = parts.length ? window.__fb.query(col, ...parts) : col;
      const unsub = window.__fb.onSnapshot(q, (snap) => {
        const rows = [];
        snap.forEach(d => rows.push(serializeDoc(d)));
        dotNetRef.invokeMethodAsync("OnQuerySnapshot", rows);
      }, (err) => {
        console.warn("subscribeQuery failed", err);
        dotNetRef.invokeMethodAsync("OnQuerySnapshot", []);
      });
      const id = "sub_" + Math.random().toString(36).slice(2);
      window.__fbSubs = window.__fbSubs || {};
      window.__fbSubs[id] = unsub;
      return id;
    },
    unsubscribe(id) {
      if (window.__fbSubs && window.__fbSubs[id]) {
        window.__fbSubs[id]();
        delete window.__fbSubs[id];
      }
    },
    async getNotificationPermission() {
      if (!("Notification" in window)) return "unsupported";
      return Notification.permission;
    },
    async requestNotificationPermission() {
      if (!("Notification" in window)) return "unsupported";
      if (Notification.permission === "granted") return "granted";
      if (Notification.permission === "denied") return "denied";
      return await Notification.requestPermission();
    },
    async getFcmToken() {
      const msg = await ensureMessaging();
      if (!msg) return null;
      const cfg = window.mobihymnFirebaseConfig || {};
      if (!cfg.vapidKey) {
        console.warn("FCM vapidKey missing — set wwwroot/js/firebase-config.js");
        return null;
      }
      const permission = await this.requestNotificationPermission();
      if (permission !== "granted") return null;
      try {
        const { getToken } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging.js");
        const swReg = await ensureMessagingSw();
        const token = await getToken(msg, {
          vapidKey: cfg.vapidKey,
          serviceWorkerRegistration: swReg
        });
        return token || null;
      } catch (e) {
        console.warn("getFcmToken failed", e);
        return null;
      }
    },
    async showLocalNotification(title, body, data) {
      if (!("Notification" in window) || Notification.permission !== "granted") return false;
      // Background tray is FCM; only post while the tab is visible (MAUI foreground path).
      if (typeof document !== "undefined" && document.visibilityState !== "visible") return false;
      try {
        const swReg = await ensureMessagingSw();
        const tag = (data && (data.listId || data.groupId)) ? ("board-" + (data.listId || data.groupId)) : "board";
        await swReg.showNotification(title || "MobiHymn", {
          body: body || "",
          icon: "/icon-192.png",
          badge: "/icon-192.png",
          tag,
          data: data || {},
          renotify: true
        });
        return true;
      } catch (e) {
        console.warn("showLocalNotification failed", e);
        return false;
      }
    },
    onBoardNotificationClick(dotNetRef) {
      if (window.__boardNotifClickHandler)
        navigator.serviceWorker.removeEventListener("message", window.__boardNotifClickHandler);
      const handler = (event) => {
        const msg = event.data || {};
        if (msg.type !== "boardNotificationClick") return;
        dotNetRef.invokeMethodAsync(
          "OnBoardNotificationClick",
          msg.groupId || "",
          msg.listId || "");
      };
      window.__boardNotifClickHandler = handler;
      navigator.serviceWorker.addEventListener("message", handler);
      // Cold-start deep link from query string.
      try {
        const url = new URL(window.location.href);
        if (url.searchParams.get("openBoard") === "1") {
          const groupId = url.searchParams.get("groupId") || "";
          const listId = url.searchParams.get("listId") || "";
          url.searchParams.delete("openBoard");
          url.searchParams.delete("groupId");
          url.searchParams.delete("listId");
          window.history.replaceState({}, "", url.pathname + url.search + url.hash);
          if (groupId)
            setTimeout(() => dotNetRef.invokeMethodAsync("OnBoardNotificationClick", groupId, listId), 0);
        }
      } catch { /* ignore */ }
    },
    async getMidiUrl(number) {
      if (!(await ensureInit())) return null;
      const n = String(number || "").trim();
      if (!n) return null;
      const path = midiObjectPath(n);
      try {
        const { getStorage, ref, getDownloadURL } = await import(
          "https://www.gstatic.com/firebasejs/10.14.1/firebase-storage.js"
        );
        return await getDownloadURL(ref(getStorage(app), path));
      } catch (e) {
        console.debug("MIDI URL not available at", path, e?.code || e?.message || e);
        return null;
      }
    },
    /**
     * Download MIDI bytes via the Storage SDK (avoids CORS on the public download URL).
     * @returns {ArrayBuffer|null}
     */
    async getMidiBytes(number) {
      if (!(await ensureInit())) return null;
      const n = String(number || "").trim();
      if (!n) return null;
      const path = midiObjectPath(n);
      try {
        const { getStorage, ref, getBytes } = await import(
          "https://www.gstatic.com/firebasejs/10.14.1/firebase-storage.js"
        );
        const bytes = await getBytes(ref(getStorage(app), path));
        // getBytes returns Uint8Array — hand back a real ArrayBuffer for midi-player-js.
        return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
      } catch (e) {
        console.debug("MIDI bytes not available at", path, e?.code || e?.message || e);
        return null;
      }
    },
    async getLatestRelease(platform) {
      if (!(await ensureInit())) return null;
      if (!window.__fb.rtdb) return null;
      const key = "LatestRelease/" + (platform || "Web");
      try {
        const snap = await window.__fb.get(window.__fb.ref(window.__fb.rtdb, key));
        if (!snap.exists()) return null;
        const data = snap.val() || {};
        return {
          version: data.Version || data.version || "",
          downloadUrl: data.DownloadUrl || data.downloadUrl || "",
          mandatory: !!(data.Mandatory ?? data.mandatory)
        };
      } catch (e) {
        console.warn("LatestRelease read failed", e);
        return null;
      }
    }
  };
})();
