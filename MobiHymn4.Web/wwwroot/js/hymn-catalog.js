// Durable hymn catalog for the installed PWA (IndexedDB).
window.mobihymnCatalog = (function () {
  const DB_NAME = "mobihymn-hymn-catalog";
  const DB_VERSION = 1;
  const STORE = "hymns";
  const BATCH = 100;

  function openDb() {
    return new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE))
          db.createObjectStore(STORE, { keyPath: "number" });
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error || new Error("IndexedDB open failed"));
    });
  }

  function txDone(tx) {
    return new Promise((resolve, reject) => {
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error || new Error("IndexedDB transaction failed"));
      tx.onabort = () => reject(tx.error || new Error("IndexedDB transaction aborted"));
    });
  }

  function reqDone(req) {
    return new Promise((resolve, reject) => {
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error || new Error("IndexedDB request failed"));
    });
  }

  /** @param {string} jsonArrayString JSON array of hymn objects */
  async function replaceAllJson(jsonArrayString) {
    let hymns;
    try {
      hymns = JSON.parse(jsonArrayString || "[]");
    } catch {
      throw new Error("Invalid catalog JSON");
    }
    if (!Array.isArray(hymns))
      throw new Error("Catalog must be a JSON array");

    const rows = [];
    for (const h of hymns) {
      if (!h || typeof h !== "object") continue;
      const number = String(h.number || h.Number || "").trim().toLowerCase();
      if (!number) continue;
      rows.push(Object.assign({}, h, { number }));
    }

    const db = await openDb();
    try {
      // Clear in its own transaction first.
      {
        const tx = db.transaction(STORE, "readwrite");
        tx.objectStore(STORE).clear();
        await txDone(tx);
      }

      // Batch puts so large catalogs don't abort a single huge transaction.
      for (let i = 0; i < rows.length; i += BATCH) {
        const chunk = rows.slice(i, i + BATCH);
        const tx = db.transaction(STORE, "readwrite");
        const store = tx.objectStore(STORE);
        for (const row of chunk)
          store.put(row);
        await txDone(tx);
      }

      const tx = db.transaction(STORE, "readonly");
      const n = await reqDone(tx.objectStore(STORE).count());
      await txDone(tx);
      if ((n | 0) <= 0 && rows.length > 0)
        throw new Error("IndexedDB write did not persist hymns");
      return n | 0;
    } finally {
      db.close();
    }
  }

  async function getJson(number) {
    const key = String(number || "").trim().toLowerCase();
    if (!key) return null;
    const db = await openDb();
    try {
      const tx = db.transaction(STORE, "readonly");
      const row = await reqDone(tx.objectStore(STORE).get(key));
      await txDone(tx);
      return row ? JSON.stringify(row) : null;
    } finally {
      db.close();
    }
  }

  async function getAllJson() {
    const db = await openDb();
    try {
      const tx = db.transaction(STORE, "readonly");
      const rows = await reqDone(tx.objectStore(STORE).getAll());
      await txDone(tx);
      return JSON.stringify(rows || []);
    } finally {
      db.close();
    }
  }

  async function count() {
    const db = await openDb();
    try {
      const tx = db.transaction(STORE, "readonly");
      const n = await reqDone(tx.objectStore(STORE).count());
      await txDone(tx);
      return n | 0;
    } finally {
      db.close();
    }
  }

  async function clear() {
    const db = await openDb();
    try {
      const tx = db.transaction(STORE, "readwrite");
      tx.objectStore(STORE).clear();
      await txDone(tx);
    } finally {
      db.close();
    }
  }

  return {
    replaceAllJson,
    getJson,
    getAllJson,
    count,
    clear
  };
})();
