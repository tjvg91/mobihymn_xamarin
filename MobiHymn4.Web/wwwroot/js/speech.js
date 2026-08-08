// Browser speech recognition helper (Web Speech API).
window.mobihymnSpeech = (function () {
  let active = null;

  function isSupported() {
    return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
  }

  function stop() {
    try {
      if (active) active.stop();
    } catch { /* ignore */ }
    active = null;
  }

  /** @returns {Promise<string>} transcript or empty string */
  function listenOnce(lang) {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) return Promise.resolve("");

    stop();
    return new Promise((resolve) => {
      const r = new SR();
      active = r;
      let done = false;
      const finish = (v) => {
        if (done) return;
        done = true;
        if (active === r) active = null;
        resolve(v || "");
      };
      r.lang = lang || "en-US";
      r.interimResults = false;
      r.maxAlternatives = 1;
      r.onresult = (e) => {
        try {
          finish(e.results[0][0].transcript || "");
        } catch {
          finish("");
        }
      };
      r.onerror = () => finish("");
      r.onend = () => finish("");
      try {
        r.start();
      } catch {
        finish("");
      }
    });
  }

  return { isSupported, listenOnce, stop };
})();
