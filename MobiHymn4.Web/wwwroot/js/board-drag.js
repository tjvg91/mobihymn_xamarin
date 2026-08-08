// Pointer-based board reorder (HTML5 DnD is unreliable on touch / mobile).
window.mobihymnBoardDrag = (function () {
  let active = null;

  function hitEntry(x, y) {
    const el = document.elementFromPoint(x, y);
    if (!el || !el.closest) return { id: null, below: false };
    const row = el.closest("[data-board-entry-id]");
    if (!row) return { id: null, below: false };
    const rect = row.getBoundingClientRect();
    const below = y >= rect.top + rect.height / 2;
    return { id: row.getAttribute("data-board-entry-id"), below };
  }

  function sourceElement(entryId) {
    return document.querySelector(`[data-board-entry-id="${CSS.escape(entryId)}"]`);
  }

  function createPhantom(source, clientX, clientY) {
    if (!source) return null;
    const rect = source.getBoundingClientRect();
    const phantom = source.cloneNode(true);
    phantom.removeAttribute("data-board-entry-id");
    phantom.classList.add("board-drag-phantom");
    phantom.setAttribute("aria-hidden", "true");
    phantom.style.width = `${rect.width}px`;
    phantom.style.height = `${rect.height}px`;
    // Keep grip under the finger / cursor.
    const offsetX = Math.min(Math.max(clientX - rect.left, 12), rect.width - 12);
    const offsetY = Math.min(Math.max(clientY - rect.top, 12), rect.height - 12);
    phantom.style.transform = `translate(${clientX - offsetX}px, ${clientY - offsetY}px)`;
    document.body.appendChild(phantom);
    return { el: phantom, offsetX, offsetY };
  }

  function movePhantom(phantom, clientX, clientY) {
    if (!phantom?.el) return;
    phantom.el.style.transform =
      `translate(${clientX - phantom.offsetX}px, ${clientY - phantom.offsetY}px)`;
  }

  function cleanup() {
    if (!active) return;
    const { onMove, onUp, onCancel, host, phantom } = active;
    window.removeEventListener("pointermove", onMove);
    window.removeEventListener("pointerup", onUp);
    window.removeEventListener("pointercancel", onCancel);
    window.removeEventListener("touchmove", onMove);
    document.body.classList.remove("board-dragging");
    if (host) host.classList.remove("is-board-dragging");
    if (phantom?.el?.parentNode) phantom.el.parentNode.removeChild(phantom.el);
    active = null;
  }

  return {
    begin(dotNetRef, entryId, clientX, clientY) {
      cleanup();
      const host = document.querySelector(".board-pane-host.is-open");
      document.body.classList.add("board-dragging");
      if (host) host.classList.add("is-board-dragging");

      const source = sourceElement(entryId);
      const phantom = createPhantom(source, clientX, clientY);

      const onMove = (e) => {
        if (e.cancelable) e.preventDefault();
        const x = e.clientX ?? e.touches?.[0]?.clientX;
        const y = e.clientY ?? e.touches?.[0]?.clientY;
        if (x == null || y == null) return;
        movePhantom(active?.phantom, x, y);
        const hit = hitEntry(x, y);
        try {
          dotNetRef.invokeMethodAsync("OnBoardPointerDragOver", hit.id, hit.below);
        } catch { /* disposed */ }
      };

      const finish = (e) => {
        const x = e.clientX ?? e.changedTouches?.[0]?.clientX;
        const y = e.clientY ?? e.changedTouches?.[0]?.clientY;
        const hit = x != null && y != null ? hitEntry(x, y) : { id: null, below: false };
        const ref = active?.dotNetRef;
        cleanup();
        try {
          ref?.invokeMethodAsync("OnBoardPointerDragEnd", hit.id, hit.below);
        } catch { /* disposed */ }
      };

      const onCancel = () => {
        const ref = active?.dotNetRef;
        cleanup();
        try {
          ref?.invokeMethodAsync("OnBoardPointerDragEnd", null, false);
        } catch { /* disposed */ }
      };

      active = {
        dotNetRef,
        entryId,
        host,
        phantom,
        onMove,
        onUp: finish,
        onCancel
      };

      window.addEventListener("pointermove", onMove, { passive: false });
      window.addEventListener("pointerup", finish, { passive: false });
      window.addEventListener("pointercancel", onCancel, { passive: false });
      window.addEventListener("touchmove", onMove, { passive: false });

      try {
        const hit = hitEntry(clientX, clientY);
        dotNetRef.invokeMethodAsync("OnBoardPointerDragOver", hit.id, hit.below);
      } catch { /* ignore */ }
    },

    cancel() {
      cleanup();
    }
  };
})();
