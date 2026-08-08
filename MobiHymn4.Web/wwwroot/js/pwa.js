// PWA / install helpers (standalone detection + install prompt + access gate).
window.mobihymnPwa = (function () {
  let deferredPrompt = null;
  const availabilityListeners = [];
  let gateWatchTimer = null;
  let appInstalled = false;
  let installedCheckPromise = null;

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
      notifyAvailability();
    });
    window.addEventListener("appinstalled", () => {
      deferredPrompt = null;
      markInstalled();
      notifyAvailability();
      applyAccessGate();
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

  function readInstalledFlag() {
    try {
      return localStorage.getItem("mh-pwa-installed") === "1";
    } catch {
      return false;
    }
  }

  // Home Screen / standalone launch: remember so Safari can show "Open app".
  if (isStandalone())
    markInstalled();
  else if (readInstalledFlag())
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
    // Local development: allow normal browser tabs without install/desktop gates.
    if (isLocalDevHost())
      return "none";
    if (isStandalone())
      return "none";
    if (isMobile())
      return "install";
    return "desktop";
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
    const installed = !!appInstalled || readInstalledFlag();
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
      let installed = isStandalone() || readInstalledFlag();

      try {
        if (navigator.getInstalledRelatedApps) {
          const apps = await navigator.getInstalledRelatedApps();
          if (Array.isArray(apps) && apps.length > 0)
            installed = true;
        }
      } catch { /* ignore */ }

      if (installed)
        markInstalled();
      else
        appInstalled = false;

      notifyAvailability();
      return installed;
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

    // Android: primary CTA only — hide the numbered install instructions.
    if (steps) steps.hidden = true;
    if (stepsIos) {
      stepsIos.hidden = true;
      stepsIos.setAttribute("hidden", "");
      stepsIos.style.display = "none";
    }
    if (stepsOther) {
      stepsOther.hidden = true;
      stepsOther.setAttribute("hidden", "");
      stepsOther.style.display = "none";
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
      gateWatchTimer = setInterval(() => {
        if (getGateMode() === "none")
          applyAccessGate();
        else
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
    applyAccessGate
  };
})();
