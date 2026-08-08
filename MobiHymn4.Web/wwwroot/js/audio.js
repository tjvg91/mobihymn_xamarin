window.mobihymnAudio = (function () {
  let mode = "none"; // none | mp3 | midi
  let audioEl = null;
  let midiPlayer = null;
  let midiInstrument = null;
  let midiAudioCtx = null;
  let midiDuration = 0;
  let midiBaseDuration = 0;
  let midiCurrent = 0;
  let midiPaused = true;
  let midiRaf = 0;
  let midiLibsPromise = null;
  let activeGeneration = 0;

  /** @type {Set<number>} */
  let mutedChannels = new Set();
  /** @type {number[]} */
  let midiChannels = [];
  let midiTranspose = 0; // semitones
  let midiTempoScale = 1;
  let midiFileTempo = 120;
  let midiKeyRoot = "C"; // pitch class from Key Signature (or default)
  let midiKeyMode = "Major"; // Major | Minor
  let midiKeyDetected = false;

  const NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
  const NOTE_NAMES_FLAT = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
  const CIRCLE_OF_FIFTHS = ["C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#", "E#"];
  const CIRCLE_OF_FOURTHS = ["C", "F", "Bb", "Eb", "Ab", "Db", "Gb", "Cb", "Fb", "Bbb", "Ebb", "Abb"];
  const ROOT_TO_PC = {
    C: 0, "B#": 0,
    "C#": 1, Db: 1,
    D: 2,
    "D#": 3, Eb: 3,
    E: 4, Fb: 4,
    F: 5, "E#": 5,
    "F#": 6, Gb: 6,
    G: 7,
    "G#": 8, Ab: 8,
    A: 9,
    "A#": 10, Bb: 10,
    B: 11, Cb: 11
  };

  function loadScript(src) {
    return new Promise((resolve, reject) => {
      if (document.querySelector(`script[src="${src}"]`)) {
        resolve();
        return;
      }
      const s = document.createElement("script");
      s.src = src;
      s.async = true;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error("Failed to load " + src));
      document.head.appendChild(s);
    });
  }

  function ensureMidiLibs() {
    if (midiLibsPromise) return midiLibsPromise;
    midiLibsPromise = (async () => {
      await loadScript("https://cdn.jsdelivr.net/npm/midi-player-js@2.0.16/browser/midiplayer.min.js");
      await loadScript("https://cdn.jsdelivr.net/npm/soundfont-player@0.12.0/dist/soundfont-player.min.js");
      if (!window.MidiPlayer || !window.Soundfont)
        throw new Error("MIDI libraries unavailable");
    })().catch((e) => {
      midiLibsPromise = null;
      throw e;
    });
    return midiLibsPromise;
  }

  function stopMidiClock() {
    if (midiRaf) {
      cancelAnimationFrame(midiRaf);
      midiRaf = 0;
    }
  }

  function syncMidiTimesFromTicks() {
    if (!midiPlayer) return;
    try {
      const totalTicks = midiPlayer.totalTicks || 0;
      const tick = typeof midiPlayer.getCurrentTick === "function"
        ? midiPlayer.getCurrentTick()
        : (midiPlayer.tick || 0);
      // getSongTime() is total length at the current tempo (not playhead position).
      if (typeof midiPlayer.getSongTime === "function") {
        const total = midiPlayer.getSongTime();
        if (isFinite(total) && total > 0)
          midiDuration = total;
      } else if (midiBaseDuration > 0) {
        midiDuration = Math.max(midiBaseDuration / Math.max(midiTempoScale, 0.05), 0.1);
      }
      if (totalTicks > 0 && midiDuration > 0)
        midiCurrent = Math.max(0, Math.min(midiDuration, (tick / totalTicks) * midiDuration));
    } catch { /* ignore */ }
  }

  function tickMidiClock() {
    if (!midiPlayer || midiPaused) return;
    syncMidiTimesFromTicks();
    midiRaf = requestAnimationFrame(tickMidiClock);
  }

  function resetMidiControls() {
    mutedChannels = new Set();
    midiChannels = [];
    midiTranspose = 0;
    midiTempoScale = 1;
    midiFileTempo = 120;
    midiKeyRoot = "C";
    midiKeyMode = "Major";
    midiKeyDetected = false;
    midiBaseDuration = 0;
  }

  function preferFlats(root) {
    return /b|Bb|Eb|Ab|Db|Gb|Cb|Fb/.test(root) || root === "F";
  }

  function parseKeySignature(sig) {
    if (!sig || typeof sig !== "string") return false;
    const m = sig.trim().match(/^([A-G](?:#|b|bb)?)\s*(Major|Minor)$/i);
    if (!m) return false;
    midiKeyRoot = m[1];
    midiKeyMode = m[2].charAt(0).toUpperCase() + m[2].slice(1).toLowerCase();
    midiKeyDetected = true;
    return true;
  }

  /** midi-player-js stores sf as Uint8; flats are 253..255 not -3..-1. */
  function signedByte(v) {
    const n = Number(v);
    if (!isFinite(n)) return 0;
    return n > 127 ? n - 256 : n;
  }

  function applyKeyFromSfMi(sf, mi) {
    let root = "C";
    if (sf > 0)
      root = CIRCLE_OF_FIFTHS[Math.min(sf, CIRCLE_OF_FIFTHS.length - 1)] || "C";
    else if (sf < 0)
      root = CIRCLE_OF_FOURTHS[Math.min(Math.abs(sf), CIRCLE_OF_FOURTHS.length - 1)] || "C";
    midiKeyRoot = root;
    midiKeyMode = Number(mi) === 1 ? "Minor" : "Major";
    midiKeyDetected = true;
    return true;
  }

  function applyKeySignatureEvent(ev) {
    if (!ev) return false;
    // Prefer raw bytes — library string is wrong for flat keys (Uint8 vs signed sf).
    const data = ev.data;
    if (data && typeof data.length === "number" && data.length >= 2)
      return applyKeyFromSfMi(signedByte(data[0]), data[1]);
    return parseKeySignature(ev.keySignature);
  }

  /** Catalog tuneKey like "Eb", "G minor", "F# Major". */
  function applyTuneKeyHint(hint) {
    if (!hint || typeof hint !== "string") return false;
    let s = hint.trim()
      .replace(/♯/g, "#")
      .replace(/♭/g, "b")
      .replace(/\s+/g, " ");
    if (!s) return false;
    const m = s.match(/^([A-Ga-g])\s*([#b]?)\s*(major|maj|minor|min|m)?$/i);
    if (!m) return false;
    const letter = m[1].toUpperCase();
    const acc = m[2] || "";
    const modeRaw = (m[3] || "major").toLowerCase();
    const mode = (modeRaw === "m" || modeRaw === "min" || modeRaw === "minor") ? "Minor" : "Major";
    const root = letter + acc;
    if (!(root in ROOT_TO_PC) && !((letter + (acc === "#" ? "#" : acc === "b" ? "b" : "")) in ROOT_TO_PC)) {
      // accept C, C#, Db, etc.
      if (!(root in ROOT_TO_PC)) return false;
    }
    midiKeyRoot = root in ROOT_TO_PC ? root : letter;
    midiKeyMode = mode;
    midiKeyDetected = true;
    return true;
  }

  function currentKeyName() {
    const pc0 = ROOT_TO_PC[midiKeyRoot];
    const base = typeof pc0 === "number" ? pc0 : 0;
    const pc = ((base + midiTranspose) % 12 + 12) % 12;
    const useFlats = preferFlats(midiKeyRoot) || (midiTranspose < 0 && !/#/.test(midiKeyRoot));
    const root = (useFlats ? NOTE_NAMES_FLAT : NOTE_NAMES)[pc];
    return root + " " + midiKeyMode;
  }

  /**
   * Change playback BPM without jumping the playhead.
   * midi-player-js maps wall-clock → ticks using tempo; changing tempo mid-play
   * without resetting the clock makes getCurrentTick leap forward/back.
   */
  function applyTempo() {
    if (!midiPlayer || typeof midiPlayer.setTempo !== "function") return;
    const bpm = Math.max(20, Math.min(400, midiFileTempo * midiTempoScale));
    const wasPlaying = !midiPaused && (typeof midiPlayer.isPlaying !== "function" || midiPlayer.isPlaying());

    let tick = 0;
    try {
      tick = typeof midiPlayer.getCurrentTick === "function"
        ? midiPlayer.getCurrentTick()
        : (midiPlayer.tick || 0);
    } catch { tick = midiPlayer.tick || 0; }

    try {
      if (wasPlaying) {
        try { midiPlayer.pause(); } catch { /* ignore */ }
      }
      midiPlayer.setTempo(bpm);
      if (typeof midiPlayer.skipToTick === "function")
        midiPlayer.skipToTick(Math.max(0, tick));
      else {
        midiPlayer.startTick = Math.max(0, tick);
        midiPlayer.tick = Math.max(0, tick);
      }
      syncMidiTimesFromTicks();
      if (wasPlaying) {
        midiPaused = false;
        midiPlayer.play();
        stopMidiClock();
        tickMidiClock();
      }
    } catch { /* ignore */ }
  }

  function midiNoteName(noteNumber) {
    const n = Math.round(noteNumber);
    if (n < 0 || n > 127) return null;
    const name = NOTE_NAMES[n % 12];
    const octave = Math.floor(n / 12) - 1;
    return name + octave;
  }

  function collectChannelsAndKey() {
    const found = new Set();
    try {
      const tracks = midiPlayer?.getEvents?.() || [];
      for (const track of tracks) {
        if (!Array.isArray(track)) continue;
        for (const ev of track) {
          if (ev?.name === "Key Signature")
            applyKeySignatureEvent(ev);
          if (typeof ev?.channel === "number" && (ev.name === "Note on" || ev.name === "Note off"))
            found.add(ev.channel);
        }
      }
    } catch { /* ignore */ }
    midiChannels = Array.from(found).sort((a, b) => a - b);
  }

  function handleMidiEvent(event) {
    if (!event) return;

    if (event.name === "Set Tempo") {
      const bpm = event.tempo || event.data;
      if (bpm) {
        midiFileTempo = bpm;
        applyTempo();
      }
      return;
    }

    if (event.name === "Key Signature") {
      applyKeySignatureEvent(event);
      return;
    }

    if (!midiInstrument || event.name !== "Note on" || !(event.velocity > 0)) return;
    const ch = typeof event.channel === "number" ? event.channel : 0;
    if (mutedChannels.has(ch)) return;

    const baseNum = typeof event.noteNumber === "number"
      ? event.noteNumber
      : null;
    const note = baseNum != null
      ? midiNoteName(baseNum + midiTranspose)
      : event.noteName;
    if (!note) return;

    try {
      midiInstrument.play(note, midiAudioCtx.currentTime, {
        gain: event.velocity / 127
      });
    } catch { /* ignore note errors */ }
  }

  async function tearDownMidi() {
    stopMidiClock();
    try { midiPlayer?.stop(); } catch { /* ignore */ }
    midiPlayer = null;
    midiInstrument = null;
    midiDuration = 0;
    midiBaseDuration = 0;
    midiCurrent = 0;
    midiPaused = true;
    resetMidiControls();
    if (midiAudioCtx) {
      try { await midiAudioCtx.close(); } catch { /* ignore */ }
      midiAudioCtx = null;
    }
  }

  async function urlExists(url) {
    if (!url) return false;
    try {
      const head = await fetch(url, { method: "HEAD", cache: "no-store" });
      if (head.ok) return true;
      if (head.status === 405 || head.status === 501) {
        const get = await fetch(url, {
          method: "GET",
          headers: { Range: "bytes=0-0" },
          cache: "no-store"
        });
        return get.ok || get.status === 206;
      }
      return false;
    } catch {
      return false;
    }
  }

  function mp3Candidates(number, mp3UrlBuilder) {
    const n = String(number || "").trim();
    const urls = [];
    const push = (u) => { if (u && !urls.includes(u)) urls.push(u); };
    if (typeof mp3UrlBuilder === "function") {
      push(mp3UrlBuilder(n));
      const base = n.replace(/[stf]+$/i, "");
      if (base && base !== n) push(mp3UrlBuilder(base));
    }
    return urls;
  }

  function midiControlState() {
    return {
      mode: mode,
      channels: midiChannels.slice(),
      muted: Array.from(mutedChannels),
      transpose: midiTranspose,
      tempoScale: midiTempoScale,
      tempoBpm: Math.round(midiFileTempo * midiTempoScale),
      keyName: currentKeyName()
    };
  }

  return {
    urlExists,

    /**
     * Resolve playback source: Firebase MIDI → MP3 → none.
     * @returns {{ mode: 'midi'|'mp3'|'none', url: string|null }}
     */
    async resolve(number, mp3Urls, tuneKeyHint) {
      const gen = ++activeGeneration;
      await this.stop();

      let midiBuffer = null;
      try {
        const n = String(number || "").trim();
        if (n) {
          const res = await fetch("/api/midi?n=" + encodeURIComponent(n), { cache: "no-store" });
          if (res.ok)
            midiBuffer = await res.arrayBuffer();
        }
      } catch { /* fall through */ }

      if (gen !== activeGeneration) return { mode: "none", url: null };

      if (midiBuffer) {
        try {
          await this.loadMidiBuffer(midiBuffer, tuneKeyHint);
          if (gen !== activeGeneration) return { mode: "none", url: null };
          return { mode: "midi", url: null };
        } catch (e) {
          console.warn("MIDI load failed, trying MP3", e);
          await tearDownMidi();
        }
      }

      const list = Array.isArray(mp3Urls) ? mp3Urls : mp3Candidates(number, mp3Urls);
      for (const url of list) {
        if (await urlExists(url)) {
          if (gen !== activeGeneration) return { mode: "none", url: null };
          mode = "mp3";
          return { mode: "mp3", url };
        }
      }

      mode = "none";
      return { mode: "none", url: null };
    },

    async loadMidiBuffer(buffer, tuneKeyHint) {
      await tearDownMidi();
      await ensureMidiLibs();
      resetMidiControls();
      midiAudioCtx = new (window.AudioContext || window.webkitAudioContext)();
      midiInstrument = await window.Soundfont.instrument(midiAudioCtx, "acoustic_grand_piano");

      const ab = buffer instanceof ArrayBuffer
        ? buffer
        : (buffer?.buffer
          ? buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength)
          : null);
      if (!ab || ab.byteLength < 4)
        throw new Error("Empty MIDI buffer");

      midiPlayer = new window.MidiPlayer.Player(handleMidiEvent);

      await new Promise((resolve, reject) => {
        let settled = false;
        const finish = (fn, arg) => {
          if (settled) return;
          settled = true;
          fn(arg);
        };
        midiPlayer.on("fileLoaded", () => {
          try {
            midiFileTempo = midiPlayer.tempo || midiPlayer.defaultTempo || 120;
            const remaining = midiPlayer.getSongTimeRemaining?.() ?? 0;
            const played = midiPlayer.getSongTime?.() ?? 0;
            midiBaseDuration = Math.max(played + remaining, 0.1);
            midiDuration = midiBaseDuration;
            collectChannelsAndKey();
            if (!midiKeyDetected)
              applyTuneKeyHint(tuneKeyHint);
            applyTempo();
          } catch {
            midiDuration = 0.1;
            midiBaseDuration = 0.1;
          }
          finish(resolve);
        });
        try {
          midiPlayer.loadArrayBuffer(ab);
          setTimeout(() => {
            try {
              if ((midiPlayer.totalTicks ?? 0) > 0 || (midiPlayer.getSongTimeRemaining?.() ?? 0) > 0) {
                midiFileTempo = midiPlayer.tempo || midiPlayer.defaultTempo || midiFileTempo;
                const remaining = midiPlayer.getSongTimeRemaining?.() ?? 0;
                const played = midiPlayer.getSongTime?.() ?? 0;
                midiBaseDuration = Math.max(midiBaseDuration, played + remaining, 0.1);
                midiDuration = midiBaseDuration;
                collectChannelsAndKey();
                if (!midiKeyDetected)
                  applyTuneKeyHint(tuneKeyHint);
                applyTempo();
                finish(resolve);
              }
            } catch { /* wait */ }
          }, 50);
          setTimeout(() => finish(reject, new Error("MIDI load timeout")), 8000);
        } catch (e) {
          finish(reject, e);
        }
      });

      midiPlayer.on("endOfFile", () => {
        midiPaused = true;
        midiCurrent = 0;
        stopMidiClock();
        try { midiPlayer.stop(); } catch { /* ignore */ }
      });

      mode = "midi";
      midiPaused = true;
      midiCurrent = 0;
    },

    /** @deprecated CORS-prone — prefer loadMidiBuffer via host proxy */
    async loadMidi(url) {
      const response = await fetch(url, { cache: "no-store", mode: "cors" });
      if (!response.ok) throw new Error("MIDI fetch failed (" + response.status + ")");
      const buffer = await response.arrayBuffer();
      return this.loadMidiBuffer(buffer);
    },

    bindMp3(el, url) {
      audioEl = el;
      mode = "mp3";
      if (!el || !url) return;
      let abs = url;
      try { abs = new URL(url, window.location.href).href; } catch { /* keep raw */ }
      // Avoid reload on every Blazor render — el.src is always absolute.
      if (el.src === abs) return;
      el.src = url;
      el.load();
    },

    async play(el) {
      if (mode === "midi") {
        if (!midiPlayer) return false;
        if (midiAudioCtx?.state === "suspended")
          await midiAudioCtx.resume();
        applyTempo();
        midiPaused = false;
        midiPlayer.play();
        stopMidiClock();
        tickMidiClock();
        return true;
      }
      const target = el || audioEl;
      if (!target) return false;
      audioEl = target;
      try {
        await target.play();
        return !target.paused;
      } catch (e) {
        console.warn("MP3 play failed", e);
        return false;
      }
    },

    pause(el) {
      if (mode === "midi") {
        try { midiPlayer?.pause(); } catch { /* ignore */ }
        midiPaused = true;
        stopMidiClock();
        return;
      }
      const target = el || audioEl;
      if (target) target.pause();
    },

    async stop() {
      if (mode === "midi") {
        await tearDownMidi();
      } else if (audioEl) {
        try {
          audioEl.pause();
          audioEl.removeAttribute("src");
          audioEl.load();
        } catch { /* ignore */ }
      }
      mode = "none";
      audioEl = null;
    },

    seek(el, seconds) {
      if (!isFinite(seconds)) return;
      const t = Math.max(0, seconds);
      if (mode === "midi") {
        if (!midiPlayer) return;
        try {
          const totalTicks = midiPlayer.totalTicks || 0;
          const dur = midiDuration > 0
            ? midiDuration
            : (typeof midiPlayer.getSongTime === "function" ? midiPlayer.getSongTime() : 0);
          if (totalTicks > 0 && dur > 0 && typeof midiPlayer.skipToTick === "function") {
            const tick = Math.round((t / dur) * totalTicks);
            const wasPlaying = !midiPaused && (typeof midiPlayer.isPlaying !== "function" || midiPlayer.isPlaying());
            if (wasPlaying) {
              try { midiPlayer.pause(); } catch { /* ignore */ }
            }
            midiPlayer.skipToTick(Math.max(0, Math.min(totalTicks, tick)));
            midiCurrent = t;
            if (wasPlaying) {
              midiPaused = false;
              midiPlayer.play();
              stopMidiClock();
              tickMidiClock();
            }
          } else if (dur > 0 && typeof midiPlayer.skipToPercent === "function") {
            midiPlayer.skipToPercent((t / dur) * 100);
            midiCurrent = t;
          } else if (typeof midiPlayer.skipToSeconds === "function") {
            midiPlayer.skipToSeconds(t);
            midiCurrent = t;
          }
        } catch { /* ignore */ }
        return;
      }
      const target = el || audioEl;
      if (!target) return;
      target.currentTime = t;
    },

    getState(el) {
      if (mode === "midi") {
        return {
          current: isFinite(midiCurrent) ? midiCurrent : 0,
          duration: isFinite(midiDuration) ? midiDuration : 0,
          paused: !!midiPaused,
          mode: "midi"
        };
      }
      const target = el || audioEl;
      if (!target) return { current: 0, duration: 0, paused: true, mode: mode || "none" };
      const d = target.duration;
      return {
        current: isFinite(target.currentTime) ? target.currentTime : 0,
        duration: isFinite(d) ? d : 0,
        paused: !!target.paused,
        mode: "mp3"
      };
    },

    getMode() { return mode; },

    getMidiControls() {
      return midiControlState();
    },

    setTempoScale(scale) {
      if (mode !== "midi") return midiControlState();
      const s = Number(scale);
      if (!isFinite(s)) return midiControlState();
      midiTempoScale = Math.max(0.5, Math.min(1.5, s));
      applyTempo();
      return midiControlState();
    },

    setTranspose(semitones) {
      if (mode !== "midi") return midiControlState();
      const n = Math.round(Number(semitones));
      if (!isFinite(n)) return midiControlState();
      midiTranspose = Math.max(-12, Math.min(12, n));
      return midiControlState();
    },

    setChannelMuted(channel, muted) {
      if (mode !== "midi") return midiControlState();
      const ch = Number(channel);
      if (!isFinite(ch)) return midiControlState();
      if (muted) mutedChannels.add(ch);
      else mutedChannels.delete(ch);
      return midiControlState();
    },

    toggleChannelMute(channel) {
      if (mode !== "midi") return midiControlState();
      const ch = Number(channel);
      if (!isFinite(ch)) return midiControlState();
      if (mutedChannels.has(ch)) mutedChannels.delete(ch);
      else mutedChannels.add(ch);
      return midiControlState();
    }
  };
})();
