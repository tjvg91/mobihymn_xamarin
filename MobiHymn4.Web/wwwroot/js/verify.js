// Visibility hook for /verify — resume after opening the email link.
window.mobihymnVerify = (function () {
  let ref = null;
  let hooked = false;

  function onVis() {
    if (document.visibilityState === "visible" && ref) {
      try { ref.invokeMethodAsync("OnBecameVisible"); } catch { /* disposed */ }
    }
  }

  function onFocus() {
    if (ref) {
      try { ref.invokeMethodAsync("OnBecameVisible"); } catch { /* disposed */ }
    }
  }

  return {
    onVisible(dotNetRef) {
      ref = dotNetRef;
      if (hooked) return;
      document.addEventListener("visibilitychange", onVis);
      window.addEventListener("focus", onFocus);
      hooked = true;
    },
    offVisible() {
      if (hooked) {
        document.removeEventListener("visibilitychange", onVis);
        window.removeEventListener("focus", onFocus);
        hooked = false;
      }
      ref = null;
    }
  };
})();
