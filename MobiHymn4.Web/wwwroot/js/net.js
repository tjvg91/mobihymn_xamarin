// Online / offline helpers for UI that requires the network (e.g. Selah).
window.mobihymnNet = (function () {
  function isOnline() {
    try {
      return typeof navigator === "undefined" || navigator.onLine !== false;
    } catch {
      return true;
    }
  }

  function subscribe(dotNetRef) {
    const fire = () => {
      try {
        dotNetRef.invokeMethodAsync("OnConnectivityChanged", isOnline());
      } catch { /* ignore */ }
    };
    const onOnline = () => fire();
    const onOffline = () => fire();
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);
    fire();
    return {
      dispose: function () {
        window.removeEventListener("online", onOnline);
        window.removeEventListener("offline", onOffline);
      }
    };
  }

  return { isOnline, subscribe };
})();
