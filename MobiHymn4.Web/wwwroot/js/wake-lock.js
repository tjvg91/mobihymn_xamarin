// Screen Wake Lock — mirrors MAUI DeviceDisplay.KeepScreenOn / KeepAwake.
(function () {
  'use strict';

  let desired = false;
  let lock = null;
  let listening = false;

  async function release() {
    if (!lock) return;
    try {
      await lock.release();
    } catch { /* ignore */ }
    lock = null;
  }

  async function acquire() {
    if (!desired) {
      await release();
      return;
    }
    if (!('wakeLock' in navigator) || typeof navigator.wakeLock.request !== 'function')
      return;
    if (document.visibilityState !== 'visible')
      return;
    if (lock) return;

    try {
      lock = await navigator.wakeLock.request('screen');
      lock.addEventListener('release', () => {
        lock = null;
        // Re-acquire if we still want it (browser released on visibility hide, etc.).
        if (desired && document.visibilityState === 'visible')
          void acquire();
      });
    } catch (err) {
      // NotAllowedError is common without a recent user gesture / secure context.
      console.debug('wakeLock request failed:', err && err.name ? err.name : err);
      lock = null;
    }
  }

  function onVisibility() {
    if (document.visibilityState === 'visible')
      void acquire();
    else
      lock = null; // browser already released; clear our handle
  }

  function onUserGesture() {
    if (desired && !lock)
      void acquire();
  }

  function ensureListeners() {
    if (listening) return;
    listening = true;
    document.addEventListener('visibilitychange', onVisibility);
    // Browsers often require a recent gesture; retry on interaction.
    document.addEventListener('pointerdown', onUserGesture, { passive: true });
    document.addEventListener('keydown', onUserGesture);
  }

  window.mobihymnWakeLock = {
    /**
     * @param {boolean} enabled
     * @returns {Promise<boolean>} true when a lock is held (or desired=false after release)
     */
    setKeepAwake: async function (enabled) {
      desired = !!enabled;
      ensureListeners();
      if (!desired) {
        await release();
        return true;
      }
      await acquire();
      return !!lock;
    },
    isSupported: function () {
      return !!(navigator.wakeLock && typeof navigator.wakeLock.request === 'function');
    },
    isActive: function () {
      return !!lock;
    }
  };
})();
