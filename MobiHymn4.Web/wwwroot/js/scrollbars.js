// Show scrollbars only while scrolling (pairs with app.css .is-scrolling rules).
(function () {
  'use strict';

  var HIDE_MS = 900;
  var timers = new WeakMap();

  function scrollingElement(target) {
    if (target === document || target === document.documentElement || target === document.body)
      return document.documentElement;
    if (target instanceof Element)
      return target;
    return null;
  }

  function onScroll(e) {
    var el = scrollingElement(e.target);
    if (!el) return;

    el.classList.add('is-scrolling');
    var prev = timers.get(el);
    if (prev) clearTimeout(prev);
    timers.set(el, setTimeout(function () {
      el.classList.remove('is-scrolling');
      timers.delete(el);
    }, HIDE_MS));
  }

  document.addEventListener('scroll', onScroll, { capture: true, passive: true });
})();
