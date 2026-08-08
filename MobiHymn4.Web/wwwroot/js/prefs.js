window.mobihymnPrefs = {
  get(key) { return localStorage.getItem(key); },
  set(key, value) { localStorage.setItem(key, value ?? ""); },
  remove(key) { localStorage.removeItem(key); },
  clearPrefix(prefix) {
    if (!prefix) return;
    const toRemove = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(prefix)) toRemove.push(k);
    }
    toRemove.forEach(k => localStorage.removeItem(k));
  },
  openDatePicker(el) {
    if (!el) return;
    try {
      if (typeof el.showPicker === "function") {
        el.showPicker();
        return;
      }
    } catch { /* fall through */ }
    try { el.focus(); } catch { /* ignore */ }
    try { el.click(); } catch { /* ignore */ }
  }
};
