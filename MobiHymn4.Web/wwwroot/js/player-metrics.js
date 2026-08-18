// Publishes the read-page player's live height as --read-player-h so floating
// chrome (the FABs) can clear it without hardcoded offsets that break whenever
// the MIDI panel's contents change.
//
// The variable is written onto the FABs themselves rather than the shell: the
// panel's open/close animation resizes the player every frame, and an inherited
// custom property on the shell would invalidate the whole hymn text alongside it.
(function () {
    var observer = null;
    var watched = null;
    var frame = 0;
    var lastHeight = -1;

    function targets() {
        return document.querySelectorAll(".read-fab");
    }

    function measure() {
        frame = 0;
        var player = document.querySelector(".read-player");
        var fabs = targets();
        if (!fabs.length) return;
        if (!player) {
            lastHeight = -1;
            for (var i = 0; i < fabs.length; i++) fabs[i].style.removeProperty("--read-player-h");
            return;
        }
        var height = Math.round(player.getBoundingClientRect().height);
        if (height <= 0 || height === lastHeight) return;
        lastHeight = height;
        for (var j = 0; j < fabs.length; j++) {
            fabs[j].style.setProperty("--read-player-h", height + "px");
        }
    }

    function apply() {
        if (frame) return;
        frame = requestAnimationFrame(measure);
    }

    // Portal utility: moves a DOM node to <body> so it escapes any
    // overflow:hidden / contain ancestor.  Blazor keeps rendering into
    // the original placeholder; after each render we re-hoist the node.
    window.mobihymnPortal = {
        _placeholder: null,
        hoist: function (id) {
            var el = document.getElementById(id);
            if (!el) return;
            if (el.parentElement === document.body) return;
            // Leave an invisible placeholder so Blazor's DOM diffing still
            // finds the element's original location in the tree.
            if (!this._placeholder) {
                this._placeholder = document.createComment("midi-overlay-portal");
            }
            el.parentElement && el.parentElement.insertBefore(this._placeholder, el);
            document.body.appendChild(el);
        },
        restore: function (id) {
            var el = document.getElementById(id);
            var ph = this._placeholder;
            if (!el || !ph || !ph.parentElement) return;
            ph.parentElement.insertBefore(el, ph);
            ph.parentElement.removeChild(ph);
            this._placeholder = null;
        }
    };

    window.mobihymnPlayerMetrics = {
        sync: function () {
            var player = document.querySelector(".read-player");
            if (player !== watched) {
                if (observer) observer.disconnect();
                observer = null;
                watched = player;
                lastHeight = -1;
                if (player && window.ResizeObserver) {
                    observer = new ResizeObserver(apply);
                    observer.observe(player);
                }
            }
            apply();
        },
        stop: function () {
            if (observer) observer.disconnect();
            observer = null;
            watched = null;
            lastHeight = -1;
            if (frame) {
                cancelAnimationFrame(frame);
                frame = 0;
            }
        }
    };
})();
