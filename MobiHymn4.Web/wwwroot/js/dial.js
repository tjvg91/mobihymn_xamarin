// Relative drag control for the MIDI panel dials and faders.
// Native range inputs jump to wherever you press, which makes a stray tap on a
// round dial slam the value to an extreme. These controls stay put until the
// pointer actually travels, then move step-by-step from where they were.
(function () {
    var DEAD_ZONE = 3;
    var DEFAULT_PX_PER_STEP = 14;

    var netRef = null;
    var drag = null;
    var bound = false;

    function num(value, fallback) {
        var n = Number(value);
        return isFinite(n) ? n : fallback;
    }

    function quantize(value, step) {
        return Math.round(Math.round(value / step) * step * 1000) / 1000;
    }

    function begin(e) {
        if (!netRef || drag) return;
        if (e.pointerType === "mouse" && e.button !== 0) return;
        var el = e.target && e.target.closest ? e.target.closest("[data-drag-dial]") : null;
        if (!el) return;

        var min = num(el.dataset.dialMin, NaN);
        var max = num(el.dataset.dialMax, NaN);
        var value = num(el.dataset.dialValue, NaN);
        var step = num(el.dataset.dialStep, 1);
        if (!isFinite(min) || !isFinite(max) || !isFinite(value) || max <= min || step <= 0) return;

        drag = {
            name: el.dataset.dragDial,
            min: min,
            max: max,
            step: step,
            value: value,
            sent: value,
            px: num(el.dataset.dialPx, DEFAULT_PX_PER_STEP),
            axis: el.dataset.dialAxis === "x"
                ? "x"
                : (el.dataset.dialAxis === "y" ? "y" : "both"),
            x: e.clientX,
            y: e.clientY,
            moved: false
        };
        try { el.setPointerCapture(e.pointerId); } catch (_) { /* capture is best effort */ }
    }

    function travel(e) {
        var dx = e.clientX - drag.x;
        var dy = drag.y - e.clientY;
        if (drag.axis === "y") return dy;
        if (drag.axis === "x") return dx;
        return dx + dy;
    }

    function move(e) {
        if (!drag) return;
        var moved = travel(e);
        if (!drag.moved) {
            if (Math.abs(moved) < DEAD_ZONE) return;
            drag.moved = true;
        }
        if (e.cancelable) e.preventDefault();

        var next = drag.value + Math.round(moved / drag.px) * drag.step;
        next = quantize(Math.min(drag.max, Math.max(drag.min, next)), drag.step);
        if (Math.abs(next - drag.sent) < drag.step / 2) return;
        drag.sent = next;
        try {
            netRef.invokeMethodAsync("OnDialDrag", drag.name, next);
        } catch (_) {
            drag = null;
        }
    }

    function end() {
        drag = null;
    }

    window.mobihymnDial = {
        init: function (ref) {
            netRef = ref;
            if (bound) return;
            bound = true;
            document.addEventListener("pointerdown", begin, true);
            document.addEventListener("pointermove", move, true);
            document.addEventListener("pointerup", end, true);
            document.addEventListener("pointercancel", end, true);
        },
        release: function () {
            netRef = null;
            drag = null;
        }
    };
})();
