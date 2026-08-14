// Pointer-based board reorder (HTML5 DnD is unreliable on touch / mobile).
// Drag arms on pointerdown but only activates after a small move — long-press
// without movement must not highlight drop lines on other rows.
// When requireVertical is true (whole hymn row), horizontal moves stay with swipe.
window.mobihymnBoardDrag = (function () {
  let active = null;
  const ACTIVATE_PX = 10;

  function hitEntry(x, y, excludeId) {
    const el = document.elementFromPoint(x, y);
    if (!el || !el.closest) return { id: null, below: false };
    const row = el.closest("[data-board-entry-id]");
    if (!row) return { id: null, below: false };
    const id = row.getAttribute("data-board-entry-id");
    if (excludeId && id === excludeId) return { id: null, below: false };
    const rect = row.getBoundingClientRect();
    const below = y >= rect.top + rect.height / 2;
    return { id, below };
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
    phantom.style.pointerEvents = "none";
    phantom.style.width = `${rect.width}px`;
    phantom.style.height = `${rect.height}px`;
    // Keep the press point under the finger / cursor.
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
    /**
     * @param {*} dotNetRef
     * @param {string} entryId
     * @param {number} clientX
     * @param {number} clientY
     * @param {{ requireVertical?: boolean }|boolean|null} [options]
     */
    begin(dotNetRef, entryId, clientX, clientY, options) {
      cleanup();
      const opts = options && typeof options === "object"
        ? options
        : { requireVertical: options === true };
      const requireVertical = !!opts.requireVertical;
      const host = document.querySelector(".board-pane-host.is-open");
      const startX = clientX;
      const startY = clientY;
      let activated = false;
      let aborted = false;
      let phantom = null;

      const activate = (x, y) => {
        if (activated || aborted) return;
        activated = true;
        document.body.classList.add("board-dragging");
        if (host) host.classList.add("is-board-dragging");
        const source = sourceElement(entryId);
        phantom = createPhantom(source, x, y);
        if (active) active.phantom = phantom;
        try {
          dotNetRef.invokeMethodAsync("OnBoardPointerDragActivated");
        } catch { /* ignore */ }
        try {
          const hit = hitEntry(x, y, entryId);
          dotNetRef.invokeMethodAsync("OnBoardPointerDragOver", hit.id, hit.below);
        } catch { /* ignore */ }
      };

      const abortForSwipe = () => {
        if (activated || aborted) return;
        aborted = true;
        const ref = active?.dotNetRef;
        cleanup();
        try {
          ref?.invokeMethodAsync("OnBoardPointerDragEnd", null, false);
        } catch { /* disposed */ }
      };

      const onMove = (e) => {
        if (aborted) return;
        const x = e.clientX ?? e.touches?.[0]?.clientX;
        const y = e.clientY ?? e.touches?.[0]?.clientY;
        if (x == null || y == null) return;

        if (!activated) {
          const dx = x - startX;
          const dy = y - startY;
          const dist = Math.hypot(dx, dy);
          if (dist < ACTIVATE_PX) return;

          // Whole-row drag: leave horizontal gestures to swipe-to-delete.
          if (requireVertical && Math.abs(dx) > Math.abs(dy)) {
            abortForSwipe();
            return;
          }

          if (e.cancelable) e.preventDefault();
          activate(x, y);
        }

        if (e.cancelable) e.preventDefault();
        movePhantom(active?.phantom, x, y);
        const hit = hitEntry(x, y, entryId);
        try {
          dotNetRef.invokeMethodAsync("OnBoardPointerDragOver", hit.id, hit.below);
        } catch { /* disposed */ }
      };

      const finish = (e) => {
        if (aborted) return;
        const wasActive = activated;
        const x = e.clientX ?? e.changedTouches?.[0]?.clientX;
        const y = e.clientY ?? e.changedTouches?.[0]?.clientY;
        const hit = wasActive && x != null && y != null
          ? hitEntry(x, y, entryId)
          : { id: null, below: false };
        const ref = active?.dotNetRef;
        cleanup();
        try {
          // Cancel (null target) when the user long-pressed without dragging.
          ref?.invokeMethodAsync(
            "OnBoardPointerDragEnd",
            wasActive ? hit.id : null,
            wasActive ? hit.below : false);
        } catch { /* disposed */ }
      };

      const onCancel = () => {
        if (aborted) return;
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
        phantom: null,
        onMove,
        onUp: finish,
        onCancel
      };

      window.addEventListener("pointermove", onMove, { passive: false });
      window.addEventListener("pointerup", finish, { passive: false });
      window.addEventListener("pointercancel", onCancel, { passive: false });
      window.addEventListener("touchmove", onMove, { passive: false });
    },

    cancel() {
      cleanup();
    }
  };
})();
