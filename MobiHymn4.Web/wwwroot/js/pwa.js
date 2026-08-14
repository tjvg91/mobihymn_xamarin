// PWA / install helpers (standalone detection + install prompt + access gate).
window.mobihymnPwa = (function () {
  let deferredPrompt = null;
  const availabilityListeners = [];
  let gateWatchTimer = null;
  let appInstalled = false;
  let installedCheckPromise = null;
  let appForegroundRef = null;

  function notifyAppForeground() {
    try {
      if (document.visibilityState !== "visible" || !appForegroundRef)
        return;
      appForegroundRef.invokeMethodAsync("OnAppForeground").catch(() => {});
    } catch { /* ignore */ }
  }

  function registerAppForeground(dotNetRef) {
    unregisterAppForeground();
    appForegroundRef = dotNetRef;
    document.addEventListener("visibilitychange", notifyAppForeground);
    window.addEventListener("focus", notifyAppForeground);
  }

  function unregisterAppForeground() {
    document.removeEventListener("visibilitychange", notifyAppForeground);
    window.removeEventListener("focus", notifyAppForeground);
    appForegroundRef = null;
  }

  function notifyAvailability() {
    const state = getInstallUiState();
    availabilityListeners.slice().forEach((fn) => {
      try { fn(state); } catch { /* ignore */ }
    });
    syncInstallButton();
  }

  try {
    window.addEventListener("beforeinstallprompt", (e) => {
      e.preventDefault();
      deferredPrompt = e;
      // Chrome only offers install when the app isn't present — drop stale "Open app".
      clearInstalledFlag();
      notifyAvailability();
    });
    window.addEventListener("appinstalled", () => {
      deferredPrompt = null;
      markInstalled();
      notifyAvailability();
      applyAccessGate();
      setTimeout(() => { registerDevice(true); }, 800);
    });
    document.addEventListener("gesturestart", (e) => e.preventDefault(), { passive: false });
    document.addEventListener("gesturechange", (e) => e.preventDefault(), { passive: false });
  } catch { /* ignore */ }

  /** True only for an installed app shell — not a normal browser tab. */
  function isStandalone() {
    try {
      if (typeof navigator !== "undefined" && navigator.standalone === true)
        return true;
      if (window.matchMedia && window.matchMedia("(display-mode: standalone)").matches)
        return true;
      if (window.matchMedia && window.matchMedia("(display-mode: window-controls-overlay)").matches)
        return true;
    } catch { /* ignore */ }
    return false;
  }

  /** Persist install across Safari ↔ Home Screen (same-origin localStorage). */
  function markInstalled() {
    appInstalled = true;
    try { localStorage.setItem("mh-pwa-installed", "1"); } catch { /* ignore */ }
  }

  function clearInstalledFlag() {
    appInstalled = false;
    try { localStorage.removeItem("mh-pwa-installed"); } catch { /* ignore */ }
  }

  function readInstalledFlag() {
    try {
      return localStorage.getItem("mh-pwa-installed") === "1";
    } catch {
      return false;
    }
  }

  function isOurRelatedApp(app) {
    if (!app || !app.platform) return false;
    const platform = String(app.platform).toLowerCase();
    if (platform === "play" || platform === "android_app") {
      const id = app.id ? String(app.id) : "";
      return !id || id === "com.tjapps.mobihymn.twa";
    }
    // Installed PWA / WebAPK for this origin.
    return platform === "webapp";
  }

  // Home Screen / standalone: remember for iOS Safari "Open app" copy.
  // Android must not trust a stale localStorage flag after uninstall — verify via
  // getInstalledRelatedApps / beforeinstallprompt in refreshInstalledState().
  if (isStandalone())
    markInstalled();
  else if (isIos() && readInstalledFlag())
    appInstalled = true;

  function isIos() {
    try {
      const ua = navigator.userAgent || "";
      if (/iPhone|iPad|iPod/i.test(ua))
        return true;
      if (navigator.platform === "MacIntel" && (navigator.maxTouchPoints || 0) > 1)
        return true;
    } catch { /* ignore */ }
    return false;
  }

  /** Firefox has no beforeinstallprompt — Install CTA never appears; show menu steps instead. */
  function isFirefox() {
    try {
      const ua = navigator.userAgent || "";
      return /Firefox\//i.test(ua) || /FxiOS\//i.test(ua);
    } catch {
      return false;
    }
  }

  function isMobile() {
    try {
      if (isIos())
        return true;

      const ua = navigator.userAgent || "";
      // Prefer UA / platform signals over Client Hints — some Android browsers
      // report userAgentData.mobile=false while still being phone/tablet Chrome.
      if (/Android.+Mobile|webOS|BlackBerry|IEMobile|Opera Mini|Windows Phone/i.test(ua))
        return true;
      // Android tablet (often no "Mobile")
      if (/Android/i.test(ua) && !/Windows/i.test(ua))
        return true;

      if (navigator.userAgentData && typeof navigator.userAgentData.mobile === "boolean")
        return navigator.userAgentData.mobile;
    } catch { /* ignore */ }
    return false;
  }

  function isLocalDevHost() {
    try {
      const host = (location.hostname || "").toLowerCase();
      return host === "localhost"
        || host === "127.0.0.1"
        || host === "[::1]"
        || host === "::1"
        || host.endsWith(".localhost");
    } catch { /* ignore */ }
    return false;
  }

  /** @returns {'none'|'desktop'|'install'} */
  function getGateMode() {
    // Installed PWA / TWA / Home Screen — never gate (dashboard is a browser admin tool).
    if (isStandalone())
      return "none";
    // Admin census: desktop/mobile *browser tabs* only (not the installed app).
    if (isDashboardPath())
      return "none";
    // Local development: allow normal browser tabs without install/desktop gates.
    if (isLocalDevHost())
      return "none";
    if (isMobile())
      return "install";
    return "desktop";
  }

  function isDashboardPath() {
    try {
      return /^\/dashboard\/?$/i.test(location.pathname || "");
    } catch {
      return false;
    }
  }

  function canPromptInstall() {
    // iOS Safari has no beforeinstallprompt — never treat as promptable.
    return !isIos() && !!deferredPrompt;
  }

  function isAppInstalled() {
    return !!appInstalled;
  }

  function getInstallUiState() {
    const ios = isIos();
    // Chrome fires beforeinstallprompt again after uninstall — never show "Open app" then.
    let installed = !!appInstalled;
    if (ios)
      installed = installed || readInstalledFlag();
    if (!ios && deferredPrompt)
      installed = false;
    if (installed)
      appInstalled = true;
    const canPrompt = !ios && !!deferredPrompt;
    return {
      isIos: ios,
      installed,
      canPrompt,
      // iOS Safari cannot launch the Home Screen PWA — never show Open/Install CTA.
      showButton: !ios && (canPrompt || installed),
      buttonLabel: installed ? "Open app" : "Install app"
    };
  }

  async function refreshInstalledState() {
    if (installedCheckPromise)
      return installedCheckPromise;

    installedCheckPromise = (async () => {
      if (isStandalone()) {
        markInstalled();
        notifyAvailability();
        return true;
      }

      // Installable again ⇒ not installed (covers Android uninstall while localStorage lags).
      if (deferredPrompt) {
        clearInstalledFlag();
        notifyAvailability();
        return false;
      }

      let relatedInstalled = false;
      try {
        if (navigator.getInstalledRelatedApps) {
          const apps = await navigator.getInstalledRelatedApps();
          relatedInstalled = Array.isArray(apps) && apps.some(isOurRelatedApp);
        }
      } catch { /* ignore */ }

      if (relatedInstalled) {
        markInstalled();
        notifyAvailability();
        return true;
      }

      // Android: related-apps (or lack of them) wins over a stale mh-pwa-installed flag.
      if (!isIos()) {
        clearInstalledFlag();
        notifyAvailability();
        return false;
      }

      // iOS: keep the Home Screen localStorage heuristic.
      const flag = readInstalledFlag();
      appInstalled = flag;
      notifyAvailability();
      return flag;
    })();

    try {
      return await installedCheckPromise;
    } finally {
      installedCheckPromise = null;
    }
  }

  async function promptInstall() {
    if (isIos() || !deferredPrompt)
      return { outcome: "unavailable" };
    const promptEvent = deferredPrompt;
    deferredPrompt = null;
    notifyAvailability();
    try {
      promptEvent.prompt();
      const choice = await promptEvent.userChoice;
      return { outcome: choice && choice.outcome ? choice.outcome : "dismissed" };
    } catch {
      return { outcome: "unavailable" };
    }
  }

  async function openInstalledApp() {
    // iOS Safari cannot hand off to the Home Screen PWA; guide the user.
    if (isIos())
      return { outcome: "manual" };

    try {
      let packageId = null;
      if (navigator.getInstalledRelatedApps) {
        const apps = await navigator.getInstalledRelatedApps();
        const android = (apps || []).find((a) =>
          a && (a.platform === "play" || a.platform === "android_app"));
        if (android && android.id)
          packageId = android.id;
      }
      if (!packageId)
        packageId = "com.tjapps.mobihymn.twa";

      // Prefer launching the related Android / TWA package when possible.
      const path = (location.pathname || "/") + (location.search || "");
      const fallback = encodeURIComponent(location.href);
      location.href =
        "intent://" + location.host + path +
        "#Intent;scheme=https;package=" + encodeURIComponent(packageId) +
        ";S.browser_fallback_url=" + fallback + ";end";
      return { outcome: "launched" };
    } catch {
      return { outcome: "manual" };
    }
  }

  function subscribeInstallAvailability(dotNetRef) {
    const fn = (state) => {
      try {
        // Back-compat: old bool callback still works if only one arg is used in C#.
        dotNetRef.invokeMethodAsync(
          "OnInstallUiChanged",
          !!state.canPrompt,
          !!state.installed,
          !!state.isIos,
          !!state.showButton,
          state.buttonLabel || "Install app");
      } catch {
        try {
          dotNetRef.invokeMethodAsync("OnInstallAvailabilityChanged", !!state.canPrompt);
        } catch { /* ignore */ }
      }
    };
    availabilityListeners.push(fn);
    try { fn(getInstallUiState()); } catch { /* ignore */ }
    refreshInstalledState();
    return {
      dispose: function () {
        const i = availabilityListeners.indexOf(fn);
        if (i >= 0) availabilityListeners.splice(i, 1);
      }
    };
  }

  function syncInstallButton() {
    const btn = document.getElementById("mh-pwa-install-btn");
    const steps = document.getElementById("mh-pwa-install-steps");
    const stepsIos = document.getElementById("mh-pwa-install-steps-ios");
    const stepsOther = document.getElementById("mh-pwa-install-steps-other");
    const title = document.getElementById("mh-pwa-install-title");
    const copy = document.getElementById("mh-pwa-install-copy");
    if (!btn) return;

    const ui = getInstallUiState();

    if (title) title.textContent = ui.installed ? "Open MobiHymn" : "Install MobiHymn";
    if (copy) {
      if (ui.installed) {
        copy.textContent = "MobiHymn is installed. Open the app from your home screen — this browser view isn’t available.";
      } else if (ui.isIos) {
        copy.textContent = "Add MobiHymn to your Home Screen to continue. This browser view isn’t available until the app is installed.";
      } else {
        copy.textContent = "Install the app to continue. The browser reader isn’t available — open MobiHymn from your home screen after installing.";
      }
    }

    if (ui.isIos) {
      // iOS: never show Open/Install — Safari cannot launch the Home Screen PWA.
      btn.hidden = true;
      btn.setAttribute("hidden", "");
      btn.style.display = "none";
      btn.dataset.mode = "";
      if (steps) steps.hidden = ui.installed; // hide how-to once we know it's on Home Screen
      if (stepsIos) {
        if (ui.installed) {
          stepsIos.hidden = true;
          stepsIos.setAttribute("hidden", "");
          stepsIos.style.display = "none";
        } else {
          stepsIos.hidden = false;
          stepsIos.removeAttribute("hidden");
          stepsIos.style.display = "";
        }
      }
      if (stepsOther) {
        stepsOther.hidden = true;
        stepsOther.setAttribute("hidden", "");
        stepsOther.style.display = "none";
      }
      return;
    }

    // Chromium: beforeinstallprompt → show Install/Open CTA, hide numbered steps.
    // Firefox / others: no beforeinstallprompt — hide CTA, show menu install steps.
    const showManualSteps = !ui.showButton && !ui.installed;

    if (stepsIos) {
      stepsIos.hidden = true;
      stepsIos.setAttribute("hidden", "");
      stepsIos.style.display = "none";
    }

    if (showManualSteps) {
      if (steps) {
        steps.hidden = false;
        steps.removeAttribute("hidden");
        steps.style.display = "";
      }
      if (stepsOther) {
        stepsOther.hidden = false;
        stepsOther.removeAttribute("hidden");
        stepsOther.style.display = "";
      }
      if (copy) {
        copy.textContent = isFirefox()
          ? "Firefox doesn’t offer a one-tap install button. Use the menu steps below, then open MobiHymn from your home screen."
          : "Use the menu steps below to install, then open MobiHymn from your home screen.";
      }
    } else {
      if (steps) steps.hidden = true;
      if (stepsOther) {
        stepsOther.hidden = true;
        stepsOther.setAttribute("hidden", "");
        stepsOther.style.display = "none";
      }
    }

    if (ui.showButton) {
      btn.hidden = false;
      btn.removeAttribute("hidden");
      btn.style.display = "block";
      btn.textContent = ui.buttonLabel;
      btn.dataset.mode = ui.installed ? "open" : "install";
    } else {
      btn.hidden = true;
      btn.setAttribute("hidden", "");
      btn.style.display = "none";
      btn.dataset.mode = "";
    }
  }

  const PENDING_DEEP_LINK_KEY = "mh-pending-deep-link";
  const PENDING_DEEP_LINK_TTL_MS = 30 * 60 * 1000;

  /** Remember board deep links when Safari shows the install gate (Home Screen drops the path). */
  function stashPendingDeepLinkIfNeeded() {
    try {
      if (getGateMode() === "none")
        return;
      const pathname = location.pathname || "/";
      const search = location.search || "";
      const isLegacyGroups = /^\/groups\//i.test(pathname);
      const isReadBoard = /^\/read(\/|$)/i.test(pathname)
        && /(?:^|[?&])groupId=/i.test(search);
      if (!isLegacyGroups && !isReadBoard)
        return;
      const path = pathname + search + (location.hash || "");
      localStorage.setItem(PENDING_DEEP_LINK_KEY, JSON.stringify({ path, at: Date.now() }));
    } catch { /* ignore */ }
  }

  /** @returns {string|null} relative path like /read/549?groupId=...&listId=... */
  function consumePendingDeepLink() {
    try {
      const raw = localStorage.getItem(PENDING_DEEP_LINK_KEY);
      if (!raw)
        return null;
      localStorage.removeItem(PENDING_DEEP_LINK_KEY);
      let path = null;
      let at = 0;
      try {
        const parsed = JSON.parse(raw);
        path = parsed && parsed.path ? String(parsed.path) : null;
        at = parsed && parsed.at ? Number(parsed.at) : 0;
      } catch {
        path = String(raw);
        at = Date.now();
      }
      if (!path)
        return null;
      const pathOnly = (path.split("?")[0] || "");
      const search = path.includes("?") ? path.slice(path.indexOf("?")) : "";
      const ok = /^\/groups\//i.test(pathOnly)
        || (/^\/read(\/|$)/i.test(pathOnly) && /(?:^|[?&])groupId=/i.test(search));
      if (!ok)
        return null;
      if (at > 0 && (Date.now() - at) > PENDING_DEEP_LINK_TTL_MS)
        return null;
      return path;
    } catch {
      return null;
    }
  }

  function applyAccessGate() {
    const root = document.getElementById("mh-pwa-gate");
    if (!root) return;

    const mode = getGateMode();
    const desktop = document.getElementById("mh-pwa-gate-desktop");
    const install = document.getElementById("mh-pwa-gate-install");

    if (mode === "none") {
      root.hidden = true;
      root.setAttribute("aria-hidden", "true");
      document.documentElement.classList.remove("mh-gate-active");
      if (gateWatchTimer) {
        clearInterval(gateWatchTimer);
        gateWatchTimer = null;
      }
      return;
    }

    stashPendingDeepLinkIfNeeded();
    root.hidden = false;
    root.setAttribute("aria-hidden", "false");
    document.documentElement.classList.add("mh-gate-active");
    if (desktop) desktop.hidden = mode !== "desktop";
    if (install) install.hidden = mode !== "install";
    syncInstallButton();
    refreshInstalledState();

    // Host label
    const hostEl = document.getElementById("mh-pwa-gate-host");
    if (hostEl) {
      try { hostEl.textContent = location.host || "mobihymn.web.app"; }
      catch { hostEl.textContent = "mobihymn.web.app"; }
    }

    if (!gateWatchTimer) {
      let lastMode = mode;
      gateWatchTimer = setInterval(() => {
        const next = getGateMode();
        if (next !== lastMode) {
          lastMode = next;
          applyAccessGate();
          return;
        }
        if (next !== "none")
          syncInstallButton();
      }, 1200);
    }
  }

  function wireGateUi() {
    const btn = document.getElementById("mh-pwa-install-btn");
    if (btn && !btn.dataset.wired) {
      btn.dataset.wired = "1";
      btn.addEventListener("click", async () => {
        const status = document.getElementById("mh-pwa-install-status");
        const ui = getInstallUiState();
        btn.disabled = true;

        if (ui.installed || btn.dataset.mode === "open") {
          btn.textContent = "Opening…";
          try {
            const result = await openInstalledApp();
            if (result.outcome !== "launched" && status) {
              status.textContent = ui.isIos
                ? "Open MobiHymn from your Home Screen."
                : "Open MobiHymn from your home screen.";
            }
          } catch {
            if (status) {
              status.textContent = ui.isIos
                ? "Open MobiHymn from your Home Screen."
                : "Open MobiHymn from your home screen.";
            }
          } finally {
            btn.disabled = false;
            syncInstallButton();
          }
          return;
        }

        btn.textContent = "Opening install…";
        try {
          const result = await promptInstall();
          if (result.outcome === "accepted") {
            markInstalled();
            if (status) status.textContent = "Installed. Close this tab and open MobiHymn from your home screen.";
          } else if (result.outcome === "unavailable") {
            if (status) status.textContent = "Use your browser menu to Add to Home screen, then open the app from there.";
            syncInstallButton();
          } else {
            if (status) status.textContent = "Install was cancelled. Tap Install app again when you’re ready.";
          }
        } catch {
          if (status) status.textContent = "Use your browser menu to Add to Home screen, then open the app from there.";
          syncInstallButton();
        } finally {
          btn.disabled = false;
          syncInstallButton();
          applyAccessGate();
        }
      });
    }
    applyAccessGate();
  }

  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", wireGateUi);
  else
    wireGateUi();

  /** Update the address bar without a Blazor navigation (avoids remount / query flicker loops). */
  function replacePath(path) {
    try {
      const raw = String(path || "").trim();
      if (!raw)
        return false;
      const url = new URL(raw.startsWith("/") ? raw : "/" + raw, location.origin);
      const next = url.pathname + url.search + (url.hash || "");
      const cur = location.pathname + location.search + (location.hash || "");
      if (next === cur)
        return false;
      history.replaceState(history.state || null, "", next);
      return true;
    } catch {
      return false;
    }
  }

  function getOrCreateDeviceId() {
    try {
      let id = localStorage.getItem("fcm_device_id") || localStorage.getItem("mh-device-id");
      if (id && /^[A-Za-z0-9_-]{8,128}$/.test(id))
        return id;
      id = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2))
        .replace(/-/g, "");
      localStorage.setItem("mh-device-id", id);
      localStorage.setItem("fcm_device_id", id);
      return id;
    } catch {
      return "anon" + String(Date.now());
    }
  }

  function detectPlatform() {
    try {
      const ua = navigator.userAgent || "";
      if (/Android/i.test(ua) && document.referrer && /android-app:\/\/com\.tjapps\.mobihymn\.twa/i.test(document.referrer))
        return "twa";
      if (isStandalone()) {
        if (isIos()) return "ios";
        if (/Android/i.test(ua)) return "twa"; // installed Android WebAPK / TWA often looks standalone
        return "pwa";
      }
      if (isIos()) return "ios";
      if (/Android/i.test(ua)) return "android";
    } catch { /* ignore */ }
    return "web";
  }

  let deviceRegisterPromise = null;

  /** Ping Cloud Function so installs / guests show on /dashboard. */
  async function registerDevice(forceInstalled) {
    if (deviceRegisterPromise)
      return deviceRegisterPromise;
    deviceRegisterPromise = (async () => {
      try {
        if (!window.mobihymnFirebase || typeof window.mobihymnFirebase.callFunction !== "function")
          return null;
        const standalone = isStandalone();
        const installed = forceInstalled === true || standalone || readInstalledFlag();
        const platform = installed
          ? (detectPlatform() === "web" ? "pwa" : detectPlatform())
          : detectPlatform();
        // Only count install census for installed shells (or explicit appinstalled).
        // Still register signed-in browsers so account linking works later.
        const shouldRegister = installed || forceInstalled === true;
        if (!shouldRegister)
          return null;
        return await window.mobihymnFirebase.callFunction("registerDevice", {
          deviceId: getOrCreateDeviceId(),
          platform: installed ? (platform === "web" ? "pwa" : platform) : platform,
          installed: !!installed,
          userAgent: navigator.userAgent || "",
        });
      } catch (e) {
        console.warn("registerDevice failed", e?.message || e);
        return null;
      } finally {
        deviceRegisterPromise = null;
      }
    })();
    return deviceRegisterPromise;
  }

  // Capture deep links before Blazor boots (gate may hide the router).
  stashPendingDeepLinkIfNeeded();

  // Standalone / previously installed: register for dashboard census.
  try {
    if (isStandalone() || readInstalledFlag())
      setTimeout(() => { registerDevice(false); }, 1500);
  } catch { /* ignore */ }

  return {
    isStandalone,
    isMobile,
    isIos,
    isAppInstalled,
    getGateMode,
    getInstallUiState,
    canPromptInstall,
    promptInstall,
    openInstalledApp,
    refreshInstalledState,
    subscribeInstallAvailability,
    applyAccessGate,
    stashPendingDeepLinkIfNeeded,
    consumePendingDeepLink,
    replacePath,
    registerDevice,
    getOrCreateDeviceId,
    registerAppForeground,
    unregisterAppForeground
  };
})();
