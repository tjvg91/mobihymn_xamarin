// Pinch-to-resize reader font — mirrors MAUI PinchGestureRecognizer on ReadPage.
// iOS + Android: non-passive preventDefault on 2-finger move; also Pointer Events.
(function () {
  'use strict';

  var MIN = 15;
  var MAX = 40;

  function distance(a, b) {
    var dx = a.clientX - b.clientX;
    var dy = a.clientY - b.clientY;
    return Math.sqrt(dx * dx + dy * dy) || 1;
  }

  function clamp(n) {
    return Math.min(MAX, Math.max(MIN, Math.round(n * 10) / 10));
  }

  function pointFromTouch(t) {
    return { clientX: t.clientX, clientY: t.clientY };
  }

  function detach(el) {
    if (!el || !el._mhPinch) return;
    var h = el._mhPinch;
    el.removeEventListener('touchstart', h.onTouchStart, h.optsTouchStart);
    el.removeEventListener('touchmove', h.onTouchMove, h.optsTouchMove);
    el.removeEventListener('touchend', h.onTouchEnd, h.optsTouchEnd);
    el.removeEventListener('touchcancel', h.onTouchEnd, h.optsTouchEnd);
    el.removeEventListener('pointerdown', h.onPointerDown, h.optsPointer);
    el.removeEventListener('pointermove', h.onPointerMove, h.optsPointerMove);
    el.removeEventListener('pointerup', h.onPointerUp, h.optsPointer);
    el.removeEventListener('pointercancel', h.onPointerUp, h.optsPointer);
    el.style.touchAction = '';
    el._mhPinch = null;
  }

  function attach(el, dotnetRef, initialSize) {
    if (!el || !dotnetRef) return;

    if (el._mhPinch) {
      el._mhPinch.dotnetRef = dotnetRef;
      if (Number(initialSize) > 0 && !el._mhPinch.isPinching())
        el._mhPinch.setKnownSize(Number(initialSize));
      return;
    }

    var startDist = 0;
    var startSize = Number(initialSize) > 0 ? Number(initialSize) : 20;
    var lastSize = startSize;
    var pinching = false;
    var knownSize = startSize;
    var raf = 0;
    var pending = null;
    var ref = dotnetRef;
    var pointers = new Map();
    var usingPointers = false;

    function applyDom(size) {
      el.style.fontSize = size + 'px';
      knownSize = size;
    }

    function flush() {
      raf = 0;
      if (pending == null) return;
      var size = pending;
      pending = null;
      try { ref.invokeMethodAsync('OnReadPinchFontSize', size); }
      catch (err) { console.debug('read-pinch live sync failed', err); }
    }

    function queue(size) {
      pending = size;
      if (!raf) raf = requestAnimationFrame(flush);
    }

    function beginPinch(p0, p1) {
      pinching = true;
      startDist = distance(p0, p1);
      startSize = knownSize;
      lastSize = startSize;
      el.dataset.mhPinching = '1';
      el.style.touchAction = 'none';
    }

    function updatePinch(p0, p1) {
      if (!startDist) startDist = 1;
      lastSize = clamp(startSize * (distance(p0, p1) / startDist));
      applyDom(lastSize);
      queue(lastSize);
    }

    function endPinch() {
      if (!pinching) return;
      pinching = false;
      usingPointers = false;
      pointers.clear();
      delete el.dataset.mhPinching;
      el.style.touchAction = 'pan-y';
      if (raf) { cancelAnimationFrame(raf); raf = 0; }
      pending = null;
      applyDom(lastSize);
      try { ref.invokeMethodAsync('OnReadPinchFontSizeEnd', lastSize); }
      catch (err) { console.debug('read-pinch end sync failed', err); }
    }

    function onTouchStart(e) {
      if (usingPointers) return;
      if (e.touches.length >= 2) {
        if (e.cancelable) e.preventDefault();
        beginPinch(pointFromTouch(e.touches[0]), pointFromTouch(e.touches[1]));
      }
    }

    function onTouchMove(e) {
      if (usingPointers) return;
      if (e.touches.length < 2) return;
      if (e.cancelable) e.preventDefault();
      var p0 = pointFromTouch(e.touches[0]);
      var p1 = pointFromTouch(e.touches[1]);
      if (!pinching) beginPinch(p0, p1);
      updatePinch(p0, p1);
    }

    function onTouchEnd(e) {
      if (usingPointers) return;
      if (!pinching) return;
      if (e.touches.length >= 2) return;
      endPinch();
    }

    function pointerList() {
      return Array.from(pointers.values());
    }

    function onPointerDown(e) {
      if (e.pointerType === 'mouse') return;
      pointers.set(e.pointerId, e);
      try { el.setPointerCapture(e.pointerId); } catch { /* ignore */ }
      if (pointers.size >= 2) {
        usingPointers = true;
        if (e.cancelable) e.preventDefault();
        var pts = pointerList();
        beginPinch(pts[0], pts[1]);
      }
    }

    function onPointerMove(e) {
      if (!pointers.has(e.pointerId)) return;
      pointers.set(e.pointerId, e);
      if (pointers.size < 2) return;
      if (e.cancelable) e.preventDefault();
      usingPointers = true;
      var pts = pointerList();
      if (!pinching) beginPinch(pts[0], pts[1]);
      updatePinch(pts[0], pts[1]);
    }

    function onPointerUp(e) {
      if (!pointers.has(e.pointerId)) return;
      pointers.delete(e.pointerId);
      try { el.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
      if (pinching && pointers.size < 2)
        endPinch();
    }

    var optsTouchStart = { passive: false, capture: true };
    var optsTouchMove = { passive: false, capture: true };
    var optsTouchEnd = { passive: true, capture: true };
    var optsPointer = { capture: true };
    var optsPointerMove = { passive: false, capture: true };

    var handlers = {
      onTouchStart: onTouchStart,
      onTouchMove: onTouchMove,
      onTouchEnd: onTouchEnd,
      onPointerDown: onPointerDown,
      onPointerMove: onPointerMove,
      onPointerUp: onPointerUp,
      optsTouchStart: optsTouchStart,
      optsTouchMove: optsTouchMove,
      optsTouchEnd: optsTouchEnd,
      optsPointer: optsPointer,
      optsPointerMove: optsPointerMove,
      get dotnetRef() { return ref; },
      set dotnetRef(v) { ref = v; },
      isPinching: function () { return pinching; },
      setKnownSize: function (n) {
        if (Number(n) > 0) knownSize = Number(n);
      }
    };

    el.style.touchAction = 'pan-y';
    el.addEventListener('touchstart', onTouchStart, optsTouchStart);
    el.addEventListener('touchmove', onTouchMove, optsTouchMove);
    el.addEventListener('touchend', onTouchEnd, optsTouchEnd);
    el.addEventListener('touchcancel', onTouchEnd, optsTouchEnd);
    el.addEventListener('pointerdown', onPointerDown, optsPointer);
    el.addEventListener('pointermove', onPointerMove, optsPointerMove);
    el.addEventListener('pointerup', onPointerUp, optsPointer);
    el.addEventListener('pointercancel', onPointerUp, optsPointer);
    el._mhPinch = handlers;

    if (Number(initialSize) > 0)
      knownSize = Number(initialSize);
  }

  window.mobihymnReadPinch = {
    attach: attach,
    detach: detach,
    setSize: function (el, size) {
      if (!el) return;
      var n = clamp(Number(size) || 20);
      el.style.fontSize = n + 'px';
      if (el._mhPinch && el._mhPinch.setKnownSize)
        el._mhPinch.setKnownSize(n);
    }
  };
})();
