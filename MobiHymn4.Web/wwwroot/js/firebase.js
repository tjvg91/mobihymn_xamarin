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
  let initPromise = null;

  function midiObjectPath(number) {
    const cfg = window.mobihymnFirebaseConfig || {};
    const template = cfg.midiPathTemplate || "midi/h{n}.mid";
    const n = String(number || "").trim();
    return template
      .replaceAll("{n}", n)
      .replaceAll("{number}", n)
      .replaceAll("{N}", n);
  }

  // Guard against concurrent callers (e.g. Program.cs's auth init racing the
  // notification click wiring on a slow cold start): without this, a second
  // caller could re-run initializeApp() before the first finishes, throwing
  // "app/duplicate-app" and silently breaking whichever caller hit the race
  // (notification taps would then never get wired for the whole session).
  function ensureInit() {
    if (ready) return Promise.resolve(true);
    if (!initPromise) {
      initPromise = doEnsureInit().catch((e) => {
        initPromise = null; // allow a later retry instead of failing forever
        throw e;
      });
    }
    return initPromise;
  }

  async function doEnsureInit() {
    const cfg = window.mobihymnFirebaseConfig;
    if (!cfg || !cfg.apiKey || !cfg.appId) {
      console.warn("Firebase web config missing — set wwwroot/js/firebase-config.js");
      return false;
    }

    const { initializeApp } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-app.js");
    const {
      getAuth, setPersistence, browserLocalPersistence, onAuthStateChanged,
      signInWithEmailAndPassword, createUserWithEmailAndPassword,
      signOut, sendPasswordResetEmail, sendEmailVerification, updatePassword, reload,
      applyActionCode, checkActionCode
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-auth.js");
    const {
      getFirestore, doc, getDoc, setDoc, deleteDoc, collection, query, where, limit, getDocs, onSnapshot
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-firestore.js");
    const {
      getDatabase, ref, get
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-database.js");
    const {
      getFunctions, httpsCallable
    } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-functions.js");

    app = initializeApp(cfg);
    auth = getAuth(app);
    // Keep the signed-in account across refreshes / new tabs.
    await setPersistence(auth, browserLocalPersistence);
    db = getFirestore(app);
    const rtdb = cfg.databaseURL ? getDatabase(app) : null;
    const fns = getFunctions(app, "us-central1");

    window.__fb = { auth, db, rtdb, doc, getDoc, setDoc, deleteDoc, collection, query, where, limit, getDocs, onSnapshot,
      ref, get, fns, httpsCallable,
      signInWithEmailAndPassword, createUserWithEmailAndPassword, signOut, sendPasswordResetEmail,
      sendEmailVerification, updatePassword, reload, applyActionCode, checkActionCode, onAuthStateChanged };
    window.__fbAuthReady = false;
    window.__fbAuthReadyPromise = new Promise((resolve) => {
      const unsub = onAuthStateChanged(auth, (user) => {
        window.__fbAuthReady = true;
        unsub();
        resolve(user);
      });
    });

    ready = true;

    // After init finishes (not during): link signed-in installs for /dashboard.
    // Must not call callFunction from inside doEnsureInit — that awaits the same initPromise.
    onAuthStateChanged(auth, (user) => {
      const uid = user && user.uid ? String(user.uid) : "";
      window.__mhAuthUid = uid;
      try {
        if (navigator.serviceWorker && navigator.serviceWorker.controller) {
          navigator.serviceWorker.controller.postMessage({ type: "mh-auth", uid });
        }
        if (navigator.serviceWorker && typeof navigator.serviceWorker.getRegistrations === "function") {
          navigator.serviceWorker.getRegistrations().then((regs) => {
            for (const reg of regs || []) {
              try {
                const w = reg.active || reg.waiting || reg.installing;
                if (w) w.postMessage({ type: "mh-auth", uid });
              } catch { /* ignore */ }
            }
          }).catch(() => {});
        }
      } catch { /* ignore */ }
      if (!user) return;
      try {
        if (window.mobihymnPwa && typeof window.mobihymnPwa.registerDevice === "function")
          setTimeout(() => { window.mobihymnPwa.registerDevice(false); }, 600);
      } catch { /* ignore */ }
    });

    return true;
  }

  function withTimeout(promise, ms, label) {
    return Promise.race([
      promise,
      new Promise((_, reject) =>
        setTimeout(() => reject(new Error((label || "operation") + " timed out after " + ms + "ms")), ms)
      )
    ]);
  }

  async function ensureMessaging() {
    if (!(await ensureInit())) return null;
    if (messaging) return messaging;
    if (typeof window === "undefined" || !("Notification" in window) || !("serviceWorker" in navigator))
      return null;
    try {
      const { getMessaging, isSupported, onMessage } = await import("https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging.js");
      if (!(await isSupported())) return null;
      messaging = getMessaging(app);
      // Foreground: notification payloads do not auto-display — paint a local tray.
      try {
        onMessage(messaging, (payload) => {
          try {
            const d = (payload && payload.data) || {};
            const updatedBy = (d.updatedBy || "").toString();
            const me = (window.__mhAuthUid || "").toString();
            // Same account on another device — do not toast yourself for your own edits.
            if (me && updatedBy && me === updatedBy) return;
            const n = (payload && payload.notification) || {};
            const title = n.title || d.title || "MobiHymn";
            const body = n.body || d.body || "";
            window.mobihymnFirebase.showLocalNotification(title, body, d);
          } catch (e) {
            console.warn("[BoardOpen] onMessage display failed", e);
          }
        });
      } catch (e) {
        console.warn("[BoardOpen] onMessage wire failed", e);
      }
      return messaging;
    } catch (e) {
      console.warn("Firebase Messaging unavailable", e);
      return null;
    }
  }

  async function ensureMessagingSw() {
    if (messagingSwReg && messagingSwReg.active) return messagingSwReg;
    messagingSwReg = null;
    window.__mhFcmSwError = "";

    // Root-absolute paths only (SPA routes must not affect SW URLs).
    const scriptUrl = "/firebase-messaging-sw.js";
    const scope = "/firebase-cloud-messaging-push-scope/";

    const findFcmReg = async () => {
      try {
        const regs = await withTimeout(navigator.serviceWorker.getRegistrations(), 5000, "getRegistrations");
        for (const r of regs) {
          const script =
            (r.active && r.active.scriptURL) ||
            (r.waiting && r.waiting.scriptURL) ||
            (r.installing && r.installing.scriptURL) ||
            "";
          if (script.includes("firebase-messaging-sw")) return r;
        }
      } catch (e) {
        console.warn("[BoardOpen] getRegistrations:", e && e.message);
      }
      try {
        return await navigator.serviceWorker.getRegistration(scope);
      } catch {
        return null;
      }
    };

    const waitActive = async (reg, ms) => {
      if (!reg) return null;
      if (reg.active) return reg;
      await new Promise((resolve) => {
        const t = setTimeout(resolve, ms);
        const done = () => { clearTimeout(t); resolve(); };
        const watch = (worker) => {
          if (!worker) return;
          try { worker.postMessage({ type: "SKIP_WAITING" }); } catch { /* ignore */ }
          worker.addEventListener("statechange", () => {
            if (worker.state === "activated" || (reg && reg.active)) done();
          });
        };
        watch(reg.installing);
        watch(reg.waiting);
        if (!reg.installing && !reg.waiting) done();
      });
      return (await navigator.serviceWorker.getRegistration(reg.scope)) || reg;
    };

    // 1) Dedicated FCM SW (preferred — tiny, activates fast)
    try {
      let reg = await findFcmReg();
      if (!reg) {
        console.log("[BoardOpen] registering", scriptUrl, "scope=", scope);
        reg = await withTimeout(
          navigator.serviceWorker.register(scriptUrl, { scope }),
          12000,
          "fcm-sw-register"
        );
      }
      reg = await waitActive(reg, 6000);
      if (reg && reg.active) {
        messagingSwReg = reg;
        console.log("[BoardOpen] ensureMessagingSw ok (fcm)", reg.active.scriptURL, reg.scope);
        return messagingSwReg;
      }
      window.__mhFcmSwError = "FCM SW registered but not active";
    } catch (e) {
      window.__mhFcmSwError = (e && e.message) || String(e);
      console.warn("[BoardOpen] FCM SW register failed:", window.__mhFcmSwError);
    }

    // 2) Fall back to Blazor offline SW (already registered by index.html).
    // Its install is now shell-only so it should be active; it also has push handlers.
    try {
      let reg =
        (await navigator.serviceWorker.getRegistration("/")) ||
        (await navigator.serviceWorker.getRegistration()) ||
        null;
      if (!reg) {
        reg = await withTimeout(
          navigator.serviceWorker.register("/service-worker.js", { scope: "/" }),
          12000,
          "blazor-sw-register"
        );
      }
      reg = await waitActive(reg, 8000);
      if (reg && reg.active) {
        messagingSwReg = reg;
        console.log("[BoardOpen] ensureMessagingSw ok (blazor fallback)", reg.active.scriptURL);
        return messagingSwReg;
      }
      window.__mhFcmSwError = (window.__mhFcmSwError ? window.__mhFcmSwError + "; " : "")
        + "Blazor SW also not active";
    } catch (e) {
      window.__mhFcmSwError = (window.__mhFcmSwError ? window.__mhFcmSwError + "; " : "")
        + ((e && e.message) || String(e));
      console.warn("[BoardOpen] Blazor SW fallback failed:", e);
    }

    messagingSwReg = null;
    return null;
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

  // Capture notification taps even before Blazor wires the DotNet callback.
  // IMPORTANT: this must run before `return` below — code placed after the
  // IIFE's `return {...}` statement is unreachable and silently never runs.
  // (That's exactly what happened here for a long time: this listener was
  // physically located after `return`, so the postMessage sent by
  // firebase-messaging-sw.js's notificationclick handler was never received
  // by the page — the board pane could never open from an already-running
  // PWA tab, no matter how correct the rest of the pipeline was.)
  try {
    if ("serviceWorker" in navigator) {
      navigator.serviceWorker.addEventListener("message", (event) => {
        const msg = event.data || {};
        if (msg.type !== "boardNotificationClick") return;
        const gid = msg.groupId || "";
        const lid = msg.listId || "";
        console.log("[BoardOpen][page] received postMessage from SW", msg,
          "dispatchFn?", typeof window.__mhDispatchBoardOpen, "dotNetRef?", !!window.__boardNotifDotNet);
        if (typeof window.__mhDispatchBoardOpen === "function") {
          try { window.__mhDispatchBoardOpen(gid, lid); return; } catch (e) { console.warn("[BoardOpen][page] dispatch fn threw", e); }
        }
        if (window.__boardNotifDotNet) {
          try {
            window.__boardNotifDotNet.invokeMethodAsync("OnBoardNotificationClick", gid, lid);
            return;
          } catch (e) { console.warn("[BoardOpen][page] invokeMethodAsync threw", e); }
        }
        console.log("[BoardOpen][page] stashing click to sessionStorage — nothing wired yet");
        try {
          sessionStorage.setItem(
            "mh-board-notif-click",
            JSON.stringify({ groupId: gid, listId: lid, at: Date.now() }));
        } catch { /* ignore */ }
      });
    }
  } catch { /* ignore */ }

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
      // Prefer current origin so PWA users return to the same host after verify.
      // Fallback matches MAUI AuthEmailActionSettings.ContinueUrl.
      try {
        if (window.location?.origin && /^https?:/i.test(window.location.origin))
          return new URL("auth/continue", window.location.origin + "/").toString();
      } catch { /* ignore */ }
      return "https://mobihymn.firebaseapp.com/auth/continue";
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
        // Retry without continue URL if the host isn't authorized yet.
        const code = e?.code || "";
        if (code.includes("unauthorized-continue-uri") || String(e?.message || "").includes("whitelisted")) {
          try {
            await window.__fb.sendPasswordResetEmail(window.__fb.auth, email);
            return;
          } catch (e2) {
            throw new Error(e2?.code || e2?.message || "Password reset failed.");
          }
        }
        throw new Error(e?.code || e?.message || "Password reset failed.");
      }
    },
    async sendEmailVerification() {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const u = window.__fb.auth.currentUser;
      if (!u) throw new Error("Not signed in.");
      try {
        await window.__fb.sendEmailVerification(u, this.actionCodeSettings());
      } catch (e) {
        const code = e?.code || "";
        if (code.includes("unauthorized-continue-uri") || String(e?.message || "").includes("whitelisted")) {
          try {
            await window.__fb.sendEmailVerification(u);
            return;
          } catch (e2) {
            throw new Error(e2?.code || e2?.message || "Could not send verification email.");
          }
        }
        throw new Error(e?.code || e?.message || "Could not send verification email.");
      }
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
      if (!u) return;
      await window.__fb.reload(u);
      // Firestore rules read email_verified from the ID token claim — reload() alone
      // updates currentUser but not the token until we force-refresh it.
      try { await u.getIdToken(true); } catch { /* ignore */ }
    },
    async refreshIdToken(force) {
      if (!(await ensureInit())) return false;
      const u = window.__fb.auth.currentUser;
      if (!u) return false;
      try {
        await u.getIdToken(force !== false);
        return true;
      } catch {
        return false;
      }
    },
    async applyActionCode(oobCode) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const code = String(oobCode || "").trim();
      if (!code) throw new Error("auth/missing-oob-code");
      try {
        await window.__fb.applyActionCode(window.__fb.auth, code);
        const u = window.__fb.auth.currentUser;
        if (u) await window.__fb.reload(u);
      } catch (e) {
        throw new Error(e?.code || e?.message || "Could not apply email link.");
      }
    },
    async checkActionCode(oobCode) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      const code = String(oobCode || "").trim();
      if (!code) throw new Error("auth/missing-oob-code");
      try {
        const info = await window.__fb.checkActionCode(window.__fb.auth, code);
        return {
          operation: info?.operation || "",
          email: info?.data?.email || info?.data?.previousEmail || ""
        };
      } catch (e) {
        throw new Error(e?.code || e?.message || "Could not check email link.");
      }
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
    /**
     * Invoke an HTTPS Callable Cloud Function (e.g. "boardUpdateList"). Throws
     * a plain Error with a user-readable message on failure (permission-denied
     * messages from the function are surfaced verbatim).
     */
    async callFunction(name, data) {
      if (!(await ensureInit())) throw new Error("Firebase is not configured.");
      if (!window.__fbAuthReady) await window.__fbAuthReadyPromise;
      try {
        const clean = JSON.parse(JSON.stringify(data ?? {}));
        const fn = window.__fb.httpsCallable(window.__fb.fns, name);
        const res = await fn(clean);
        return res?.data;
      } catch (e) {
        throw new Error(e?.message || `${name} failed.`);
      }
    },
    async callFunctionAsJson(name, data) {
      const result = await this.callFunction(name, data);
      return JSON.stringify(result ?? null);
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
    async diagnosePush() {
      const out = {
        permission: ("Notification" in window) ? Notification.permission : "unsupported",
        sw: "none",
        controller: "none",
        push: "unknown",
        messagingSupported: false,
        error: ""
      };
      try {
        if (!("serviceWorker" in navigator)) {
          out.error = "no-serviceWorker";
          return out;
        }
        let reg = null;
        try {
          reg = await withTimeout(
            Promise.resolve(navigator.serviceWorker.getRegistration()).then((r) => r || null),
            3000,
            "diagnose-getRegistration"
          );
        } catch (e) {
          out.error = (e && e.message) || "getRegistration failed";
        }
        if (reg) {
          const script =
            (reg.active && reg.active.scriptURL) ||
            (reg.waiting && reg.waiting.scriptURL) ||
            (reg.installing && reg.installing.scriptURL) ||
            "registered";
          out.sw = script;
          if (reg.pushManager) {
            try {
              const sub = await withTimeout(reg.pushManager.getSubscription(), 3000, "getSubscription");
              out.push = sub ? "subscribed" : "no-subscription";
            } catch {
              out.push = "unknown";
            }
          }
        }
        out.controller = navigator.serviceWorker.controller
          ? navigator.serviceWorker.controller.scriptURL
          : "none";
        try {
          const { isSupported } = await withTimeout(
            import("https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging.js"),
            8000,
            "messaging-import"
          );
          out.messagingSupported = !!(await withTimeout(isSupported(), 4000, "isSupported"));
        } catch (e) {
          out.messagingSupported = false;
          if (!out.error) out.error = (e && e.message) || "messaging-import-failed";
        }
      } catch (e) {
        out.error = (e && e.message) || String(e);
      }
      console.log("[BoardOpen] diagnosePush", out);
      // Return a string so Blazor interop never chokes on a raw object shape.
      return JSON.stringify(out);
    },
    async getFcmToken() {
      // Returns JSON: { token, error } so Account can show the real failure reason.
      const fail = (error) => {
        console.warn("[BoardOpen] getFcmToken:", error);
        return JSON.stringify({ token: null, error: String(error || "unknown") });
      };
      try {
        const msg = await withTimeout(ensureMessaging(), 10000, "ensureMessaging");
        if (!msg) return fail("Firebase Messaging unavailable on this device");

        const cfg = window.mobihymnFirebaseConfig || {};
        if (!cfg.vapidKey) return fail("Missing VAPID key in firebase-config.js");

        const permission = await this.requestNotificationPermission();
        console.log("[BoardOpen] getFcmToken: permission=", permission);
        if (permission !== "granted") return fail("Notification permission is " + permission);

        const { getToken } = await withTimeout(
          import("https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging.js"),
          10000,
          "messaging-import"
        );

        const tryOnce = async () => {
          window.__mhFcmSwError = "";
          const swReg = await ensureMessagingSw();
          if (!swReg) {
            throw new Error(
              window.__mhFcmSwError
                ? ("SW register failed: " + window.__mhFcmSwError)
                : "Could not register /firebase-messaging-sw.js"
            );
          }
          if (!swReg.active) throw new Error("Messaging service worker is not active yet");
          return await withTimeout(
            getToken(msg, { vapidKey: cfg.vapidKey, serviceWorkerRegistration: swReg }),
            15000,
            "getToken"
          );
        };

        let token = null;
        let lastErr = "";
        try {
          token = await tryOnce();
        } catch (e) {
          lastErr = (e && e.message) || String(e);
          console.warn("[BoardOpen] getToken first attempt:", lastErr);
        }

        if (!token) {
          // Soft retry only — do NOT unregister the messaging SW / deleteToken
          // aggressively. That kills the browser push subscription and the next
          // FCM send then purges the Firestore token (registration-token-not-registered).
          try {
            messagingSwReg = null;
            token = await tryOnce();
          } catch (e) {
            lastErr = (e && e.message) || String(e);
            console.warn("[BoardOpen] getToken retry:", lastErr);
          }
        }

        try { localStorage.setItem("__mhFcmEpoch", "fcm24"); } catch { /* ignore */ }
        if (!token) return fail(lastErr || "getToken returned empty");
        console.log("[BoardOpen] getFcmToken: ok len=" + token.length);
        return JSON.stringify({ token, error: "" });
      } catch (e) {
        return fail((e && e.message) || String(e));
      }
    },
    async showLocalNotification(title, body, data) {
      if (!("Notification" in window) || Notification.permission !== "granted") return false;
      // Background tray is FCM; only post while the tab is visible (MAUI foreground path).
      if (typeof document !== "undefined" && document.visibilityState !== "visible") return false;
      try {
        const updatedBy = (data && data.updatedBy) ? String(data.updatedBy) : "";
        const me = (window.__mhAuthUid || "").toString();
        if (me && updatedBy && me === updatedBy) return false;
      } catch { /* ignore */ }
      try {
        const swReg = await ensureMessagingSw();
        const groupId = (data && data.groupId) ? String(data.groupId) : "";
        const listId = (data && (data.listId || data.date)) ? String(data.listId || data.date) : "";
        const tag = listId ? ("board-" + listId) : (groupId ? ("board-" + groupId) : "board");
        const payload = Object.assign({}, data || {}, { groupId, listId });
        await swReg.showNotification(title || "MobiHymn", {
          body: body || "",
          icon: "/icon-192.png",
          badge: "/icon-192.png",
          tag,
          data: payload,
          renotify: true
        });
        return true;
      } catch (e) {
        console.warn("showLocalNotification failed", e);
        return false;
      }
    },
    onBoardNotificationClick(dotNetRef) {
      window.__boardNotifDotNet = dotNetRef;
      console.log("[BoardOpen][page] onBoardNotificationClick wired — dotNetRef set");

      // Re-save FCM token when the installed app / tab becomes visible again.
      // Closing Chrome can invalidate the push subscription; without a refresh the
      // next board notify deletes the dead Web token from Firestore.
      if (!window.__mhPushResumeWired) {
        window.__mhPushResumeWired = true;
        let last = 0;
        const kick = () => {
          if (typeof document !== "undefined" && document.visibilityState !== "visible") return;
          const now = Date.now();
          if (now - last < 8000) return;
          last = now;
          const ref = window.__boardNotifDotNet;
          if (!ref) return;
          try {
            ref.invokeMethodAsync("OnPushResume");
          } catch (e) {
            console.warn("[BoardOpen] OnPushResume failed", e);
          }
        };
        document.addEventListener("visibilitychange", kick);
        window.addEventListener("focus", kick);
      }

      function dispatch(groupId, listId) {
        const gid = (groupId || "").toString().trim();
        const lid = (listId || "").toString().trim();
        console.log("[BoardOpen][page] dispatch()", { groupId: gid, listId: lid });
        if (!gid) return;
        if (window.__boardNotifDotNet) {
          window.__boardNotifDotNet.invokeMethodAsync(
            "OnBoardNotificationClick",
            gid,
            lid);
          return;
        }
        console.log("[BoardOpen][page] dispatch(): no dotNetRef — stashing to sessionStorage");
        try {
          sessionStorage.setItem(
            "mh-board-notif-click",
            JSON.stringify({ groupId: gid, listId: lid, at: Date.now() }));
        } catch { /* ignore */ }
      }

      window.__mhDispatchBoardOpen = dispatch;

      // Replay click that arrived before DotNet wired.
      try {
        const raw = sessionStorage.getItem("mh-board-notif-click");
        if (raw) {
          sessionStorage.removeItem("mh-board-notif-click");
          const parsed = JSON.parse(raw);
          console.log("[BoardOpen][page] replaying stashed click", parsed);
          if (parsed && (Date.now() - (parsed.at || 0)) < 5 * 60 * 1000)
            setTimeout(() => dispatch(parsed.groupId || "", parsed.listId || ""), 0);
        }
      } catch { /* ignore */ }

      // Cold-start query is handled by Read.razor (SupplyParameterFromQuery).
      // Do NOT auto-dispatch from ?groupId= here — SyncBoardUrl leaves those params in the
      // address bar and re-wiring would re-open the board and restart cascade animations.
      try {
        const url = new URL(window.location.href);
        if (url.searchParams.get("openBoard") === "1") {
          const groupId = url.searchParams.get("groupId") || "";
          const listId = url.searchParams.get("listId") || "";
          url.searchParams.delete("openBoard");
          window.history.replaceState({}, "", url.pathname + url.search + url.hash);
          if (groupId)
            setTimeout(() => dispatch(groupId, listId), 0);
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
