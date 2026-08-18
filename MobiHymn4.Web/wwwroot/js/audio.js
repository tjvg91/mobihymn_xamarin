window.mobihymnAudio = (function () {
  let mode = "none"; // none | mp3 | midi
  let audioEl = null;
  let midiPlayer = null;
  let midiInstrument = null;
  /** @type {Record<string, any>} */
  let midiInstrumentsByName = {};
  /** @type {Map<string, any>} */
  let activeMidiNotes = new Map();
  /** @type {Map<string, { n: number, vel: number }>} */
  let midiNoteMeta = new Map();
  const WAVE_BINS = 192;
  /** @type {Record<number, { bins: Float32Array, i: number }>} */
  let midiChannelWaves = {};
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
  /** @type {Record<number, string>} */
  let midiChannelNames = {};
  /** @type {Record<number, number>} */
  let midiChannelVolumes = {};
  /** @type {Record<number, { active: number, peak: number, at: number }>} */
  let midiChannelActivity = {};
  let midiTranspose = 0; // semitones
  let midiTempoScale = 1;
  let midiFileTempo = 120;
  let midiKeyRoot = "C"; // pitch class from Key Signature (or default)
  let midiKeyMode = "Major"; // Major | Minor
  let midiKeyDetected = false;
  /** Whether playback restarts at end (MIDI + MP3). Survives track changes. */
  let playbackLoop = false;
  /** Instrument/preset id — survives hymn changes; samples reload per AudioContext. */
  let midiInstrumentId = "acoustic_grand_piano";
  let midiInstrumentLoading = false;
  /** In-flight sample download, so resolve() need not wait on it but play() can. */
  let midiInstrumentPromise = null;

  // A one-item instruments array applies one patch to every channel. Four-item
  // arrays map in sorted channel order to Soprano, Alto, Tenor, Bass.
  const MIDI_INSTRUMENTS = [
    { id: "acoustic_grand_piano", label: "Piano", instruments: ["acoustic_grand_piano"] },
    { id: "church_organ", label: "Church Organ", instruments: ["church_organ"] },
    { id: "drawbar_organ", label: "Drawbar Organ", instruments: ["drawbar_organ"] },
    { id: "reed_organ", label: "Reed Organ", instruments: ["reed_organ"] },
    { id: "harpsichord", label: "Harpsichord", instruments: ["harpsichord"] },
    { id: "string_ensemble_1", label: "String Ensemble", instruments: ["string_ensemble_1"] },
    { id: "choir_aahs", label: "Choir Aahs", instruments: ["choir_aahs"] },
    { id: "voice_oohs", label: "Choir Oohs", instruments: ["voice_oohs"] },
    {
      id: "strings_satb",
      label: "SATB Strings",
      instruments: ["violin", "viola", "cello", "contrabass"]
    },
    {
      id: "sax_satb",
      label: "SATB Sax Choir",
      instruments: ["soprano_sax", "alto_sax", "tenor_sax", "baritone_sax"]
    },
    {
      id: "sax_at",
      label: "Alto / Tenor Sax",
      instruments: ["alto_sax", "alto_sax", "tenor_sax", "tenor_sax"]
    },
    {
      id: "organ_strings",
      label: "Strings + Organ",
      instruments: ["violin", "viola", "church_organ", "church_organ"]
    },
    {
      id: "woodwind_satb",
      label: "Mixed Woodwinds",
      instruments: ["flute", "clarinet", "english_horn", "bassoon"]
    }
  ];

  function normalizeInstrumentId(id) {
    const name = String(id || "");
    return MIDI_INSTRUMENTS.some((i) => i.id === name) ? name : "acoustic_grand_piano";
  }

  function currentInstrumentPreset() {
    return MIDI_INSTRUMENTS.find((i) => i.id === midiInstrumentId) || MIDI_INSTRUMENTS[0];
  }

  // Hymn MIDIs release each chord a hair before the next one starts. Sustained
  // patches need a release long enough to bridge that gap, or the line breathes
  // between every chord; percussive ones want a short one so notes stay crisp.
  const RELEASE_BY_INSTRUMENT = {
    choir_aahs: 0.5,
    voice_oohs: 0.5,
    string_ensemble_1: 0.45,
    string_ensemble_2: 0.45,
    violin: 0.32,
    viola: 0.32,
    cello: 0.32,
    contrabass: 0.32,
    church_organ: 0.2,
    reed_organ: 0.2,
    drawbar_organ: 0.14,
    rock_organ: 0.14,
    percussive_organ: 0.12,
    soprano_sax: 0.24,
    alto_sax: 0.24,
    tenor_sax: 0.24,
    baritone_sax: 0.24,
    flute: 0.26,
    clarinet: 0.24,
    english_horn: 0.26,
    bassoon: 0.26,
    acoustic_grand_piano: 0.3,
    harpsichord: 0.14
  };
  const DEFAULT_RELEASE = 0.25;

  function voiceForChannel(channel) {
    const preset = currentInstrumentPreset();
    const role = Math.max(0, midiChannels.indexOf(channel));
    const name = preset.instruments.length === 1
      ? preset.instruments[0]
      : preset.instruments[Math.min(role, preset.instruments.length - 1)];
    return {
      name: name,
      instrument: midiInstrumentsByName[name] || midiInstrument,
      release: RELEASE_BY_INSTRUMENT[name] ?? DEFAULT_RELEASE
    };
  }

  function midiNoteKey(channel, event) {
    const pitch = typeof event?.noteNumber === "number"
      ? event.noteNumber
      : String(event?.noteName || "");
    return channel + ":" + pitch;
  }

  function stopMidiNote(channel, event) {
    const key = midiNoteKey(channel, event);
    const node = activeMidiNotes.get(key);
    midiNoteMeta.delete(key);
    if (!node) return;
    activeMidiNotes.delete(key);
    try { node.stop(midiAudioCtx?.currentTime); } catch { /* already stopped */ }
  }

  function stopActiveMidiNotes(channel) {
    for (const [key, node] of activeMidiNotes) {
      if (channel != null && !key.startsWith(channel + ":")) continue;
      activeMidiNotes.delete(key);
      midiNoteMeta.delete(key);
      try { node.stop(midiAudioCtx?.currentTime); } catch { /* already stopped */ }
    }
  }

  function sampleChannelWaves() {
    const out = {};
    for (const ch of midiChannels) {
      const bins = midiChannelWaves[ch];
      out[ch] = bins ? Array.from(bins) : [];
    }
    return out;
  }

  function eventAbsTick(ev, running) {
    if (typeof ev?.tick === "number" && ev.tick > 0) return ev.tick;
    return running;
  }

  function bakeChannelWaves() {
    const next = {};
    for (const ch of midiChannels)
      next[ch] = new Float32Array(WAVE_BINS);
    midiChannelWaves = next;

    const tracks = midiPlayer?.getEvents?.() || [];
    const notes = [];
    let maxTick = 0;

    try {
      for (const track of tracks) {
        if (!Array.isArray(track)) continue;
        let running = 0;
        const open = {};
        for (const ev of track) {
          running += Number(ev?.delta) || 0;
          const tick = eventAbsTick(ev, running);
          maxTick = Math.max(maxTick, tick, running);
          if (typeof ev?.channel !== "number") continue;
          const ch = ev.channel;
          const pitch = typeof ev.noteNumber === "number" ? ev.noteNumber : ev.noteName;
          if (pitch == null) continue;
          const key = ch + ":" + pitch;
          const isOff = ev.name === "Note off"
            || (ev.name === "Note on" && !(ev.velocity > 0));
          const isOn = ev.name === "Note on" && ev.velocity > 0;
          if (isOn) {
            open[key] = { tick, vel: ev.velocity || 80, n: typeof pitch === "number" ? pitch : 60 };
          } else if (isOff && open[key]) {
            notes.push({
              ch,
              start: open[key].tick,
              end: Math.max(open[key].tick + 1, tick),
              vel: open[key].vel,
              n: open[key].n
            });
            delete open[key];
          }
        }
        for (const key of Object.keys(open)) {
          notes.push({
            ch: Number(key.split(":")[0]),
            start: open[key].tick,
            end: Math.max(open[key].tick + 1, running, maxTick),
            vel: open[key].vel,
            n: open[key].n
          });
        }
      }
    } catch { /* keep empty waves */ }

    const totalTicks = Math.max(
      1,
      maxTick,
      midiPlayer?.getTotalTicks?.() || 0,
      midiPlayer?.totalTicks || 0
    );

    for (const note of notes) {
      const bins = midiChannelWaves[note.ch];
      if (!bins) continue;
      const start = Math.max(0, Math.min(totalTicks, note.start));
      const end = Math.max(start + 1, Math.min(totalTicks, note.end));
      const a = Math.max(0, Math.floor((start / totalTicks) * WAVE_BINS));
      const b = Math.min(WAVE_BINS - 1, Math.max(a, Math.ceil((end / totalTicks) * WAVE_BINS) - 1));
      const amp = Math.max(0.2, Math.min(1, (note.vel || 80) / 100));
      for (let i = a; i <= b; i++) {
        const phase = (note.n || 60) * 0.37 + i * 0.51;
        const grain = 0.72 + 0.28 * Math.abs(Math.sin(phase));
        bins[i] = Math.min(1, bins[i] + amp * grain);
      }
    }

    for (const ch of midiChannels) {
      const bins = midiChannelWaves[ch];
      let max = 0;
      for (let i = 0; i < bins.length; i++) max = Math.max(max, bins[i]);
      if (max <= 0) continue;
      for (let i = 0; i < bins.length; i++) {
        if (bins[i] <= 0) continue;
        bins[i] = 0.22 + 0.78 * (bins[i] / max);
      }
    }
  }

  async function loadMidiInstrument(id) {
    if (!midiAudioCtx || !window.Soundfont)
      throw new Error("MIDI audio not ready");
    const presetId = normalizeInstrumentId(id);
    const preset = MIDI_INSTRUMENTS.find((i) => i.id === presetId) || MIDI_INSTRUMENTS[0];
    midiInstrumentLoading = true;
    try {
      const names = Array.from(new Set(preset.instruments));
      const loaded = await Promise.all(
        names.map(async (name) => [name, await window.Soundfont.instrument(midiAudioCtx, name)])
      );
      const next = Object.fromEntries(loaded);
      // Swap only after every voice is ready, so playback never becomes a
      // partially loaded mixture.
      stopActiveMidiNotes();
      midiInstrumentsByName = next;
      midiInstrument = next[preset.instruments[0]];
      midiInstrumentId = presetId;
    } finally {
      midiInstrumentLoading = false;
    }
  }

  // Samples are several hundred KB, so the caller decides whether to wait: the
  // transport can render immediately while this streams, and play() joins it.
  function beginInstrumentLoad(id) {
    midiInstrumentPromise = loadMidiInstrument(id).catch((e) => {
      console.warn("Instrument load failed", e);
    });
    return midiInstrumentPromise;
  }

  async function awaitInstrument() {
    if (!midiInstrumentPromise) return;
    try { await midiInstrumentPromise; } catch { /* already logged */ }
  }

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
    midiChannelNames = {};
    midiChannelVolumes = {};
    midiChannelActivity = {};
    midiChannelWaves = {};
    midiNoteMeta = new Map();
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
    // Major: "C" / "Eb". Minor: "Am" / "Ebm".
    return midiKeyMode === "Minor" ? root + "m" : root;
  }

  /**
   * Change playback BPM without jumping the playhead.
   * midi-player-js maps wall-clock → ticks using tempo; changing tempo mid-play
   * without resetting the clock makes getCurrentTick leap forward/back.
   *
   * Do not call skipToTick from a MIDI Set Tempo event — that rewinds the
   * event cursor and note-on/note-off fire as fragments.
   */
  function applyTempo(opts) {
    if (!midiPlayer || typeof midiPlayer.setTempo !== "function") return;
    const bpm = Math.max(20, Math.min(400, midiFileTempo * midiTempoScale));
    const seek = !opts || opts.seek !== false;
    const wasPlaying = !midiPaused && (typeof midiPlayer.isPlaying !== "function" || midiPlayer.isPlaying());

    let tick = 0;
    try {
      tick = typeof midiPlayer.getCurrentTick === "function"
        ? midiPlayer.getCurrentTick()
        : (midiPlayer.tick || 0);
    } catch { tick = midiPlayer.tick || 0; }

    try {
      if (seek && wasPlaying) {
        try { midiPlayer.pause(); } catch { /* ignore */ }
      }
      midiPlayer.setTempo(bpm);
      if (seek && typeof midiPlayer.skipToTick === "function")
        midiPlayer.skipToTick(Math.max(0, tick));
      else if (seek) {
        midiPlayer.startTick = Math.max(0, tick);
        midiPlayer.tick = Math.max(0, tick);
      }
      syncMidiTimesFromTicks();
      if (seek && wasPlaying) {
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

  const SATB_NAMES = {
    0: "Soprano",
    1: "Alto",
    2: "Tenor",
    3: "Bass",
    4: "Soprano Alt",
    5: "Bass Alt"
  };
  const GENERIC_TRACK_NAME = /^(untitled|piano|pianor|pianol|treble\s*clef|bass\s*clef|other\s+\d+)$/i;

  function cleanTrackName(raw) {
    let s = String(raw ?? "").trim();
    s = s.replace(/^piano\s*,\s*/i, "").trim();
    if (GENERIC_TRACK_NAME.test(s)) return "";
    return s;
  }

  function collectChannelsAndKey() {
    const found = new Set();
    const names = {};
    try {
      const tracks = midiPlayer?.getEvents?.() || [];
      for (const track of tracks) {
        if (!Array.isArray(track)) continue;
        let trackName = "";
        let trackCh = null;
        for (const ev of track) {
          if (ev?.name === "Key Signature")
            applyKeySignatureEvent(ev);
          if (ev?.name === "Sequence/Track Name" || ev?.name === "Instrument Name") {
            const s = cleanTrackName(ev.string ?? ev.data ?? "");
            if (s) trackName = s;
          }
          if (typeof ev?.channel === "number" && (ev.name === "Note on" || ev.name === "Note off")) {
            found.add(ev.channel);
            if (trackCh == null) trackCh = ev.channel;
          }
        }
        if (trackCh != null && trackName)
          names[trackCh] = trackName;
      }
    } catch { /* ignore */ }
    midiChannels = Array.from(found).sort((a, b) => a - b);
    if (midiChannels.includes(2)) {
      for (const ch of midiChannels) {
        if (!names[ch] && SATB_NAMES[ch]) names[ch] = SATB_NAMES[ch];
      }
    }
    midiChannelNames = names;
    bakeChannelWaves();
  }

  function handleMidiEvent(event) {
    if (!event) return;

    if (event.name === "Set Tempo") {
      const bpm = event.tempo || event.data;
      if (bpm) {
        midiFileTempo = bpm;
        applyTempo({ seek: false });
      }
      return;
    }

    if (event.name === "Key Signature") {
      applyKeySignatureEvent(event);
      return;
    }

    const ch = typeof event.channel === "number" ? event.channel : 0;
    const isNoteOff = event.name === "Note off"
      || (event.name === "Note on" && !(event.velocity > 0));
    if (isNoteOff) {
      const offState = midiChannelActivity[ch] || (midiChannelActivity[ch] = { active: 0, peak: 0, at: 0 });
      offState.active = Math.max(0, (offState.active || 0) - 1);
      offState.at = (typeof performance !== "undefined" && performance.now)
        ? performance.now()
        : Date.now();
      stopMidiNote(ch, event);
      return;
    }
    if (!midiInstrument || event.name !== "Note on" || !(event.velocity > 0)) return;
    if (mutedChannels.has(ch)) return;

    const baseNum = typeof event.noteNumber === "number"
      ? event.noteNumber
      : null;
    const note = baseNum != null
      ? midiNoteName(baseNum + midiTranspose)
      : event.noteName;
    if (!note) return;

    try {
      const channelVolume = midiChannelVolumes[ch] ?? 1;
      const activity = midiChannelActivity[ch] || (midiChannelActivity[ch] = { active: 0, peak: 0, at: 0 });
      activity.active = (activity.active || 0) + 1;
      activity.peak = Math.max(activity.peak || 0, (event.velocity || 0) / 127);
      activity.at = (typeof performance !== "undefined" && performance.now)
        ? performance.now()
        : Date.now();
      const voice = voiceForChannel(ch);
      if (!voice.instrument) return;
      // A repeated note-on replaces the prior voice. Tracking the returned
      // node lets MIDI Note Off end sustained organ/string/choir samples.
      stopMidiNote(ch, event);
      const node = voice.instrument.play(note, midiAudioCtx.currentTime, {
        gain: (event.velocity / 127) * channelVolume,
        release: voice.release
      });
      const key = midiNoteKey(ch, event);
      if (node) activeMidiNotes.set(key, node);
      if (baseNum != null) {
        midiNoteMeta.set(key, {
          n: baseNum + midiTranspose,
          vel: Math.max(0.08, (event.velocity || 0) / 127)
        });
      }
    } catch { /* ignore note errors */ }
  }

  async function tearDownMidi() {
    stopMidiClock();
    stopActiveMidiNotes();
    try { midiPlayer?.stop(); } catch { /* ignore */ }
    midiPlayer = null;
    midiInstrument = null;
    midiInstrumentPromise = null;
    midiInstrumentsByName = {};
    activeMidiNotes = new Map();
    midiNoteMeta = new Map();
    midiChannelWaves = {};
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
    const now = (typeof performance !== "undefined" && performance.now)
      ? performance.now()
      : Date.now();
    const channelActivity = {};
    for (const ch of midiChannels) {
      const state = midiChannelActivity[ch];
      if (!state) {
        channelActivity[ch] = 0;
        continue;
      }
      const elapsed = Math.max(0, now - (state.at || 0));
      const decay = Math.max(0, 1 - elapsed / 900);
      const sustain = state.active > 0 ? Math.min(1, 0.2 + state.active * 0.18) : 0;
      channelActivity[ch] = Math.max(sustain, (state.peak || 0) * decay);
    }
    return {
      mode: mode,
      channels: midiChannels.slice(),
      channelNames: { ...midiChannelNames },
      channelVolumes: { ...midiChannelVolumes },
      channelActivity,
      muted: Array.from(mutedChannels),
      transpose: midiTranspose,
      tempoScale: midiTempoScale,
      tempoBpm: Math.round(midiFileTempo * midiTempoScale),
      keyName: currentKeyName(),
      instrument: midiInstrumentId,
      instruments: MIDI_INSTRUMENTS.map((i) => ({ id: i.id, label: i.label })),
      instrumentLoading: midiInstrumentLoading
    };
  }

  // The MIDI libraries come from a CDN, so fetch them while the app is still
  // booting rather than on the first hymn's resolve.
  (function warmMidiLibs() {
    var start = function () { ensureMidiLibs().catch(function () { /* retried on demand */ }); };
    if (window.requestIdleCallback) window.requestIdleCallback(start, { timeout: 3000 });
    else setTimeout(start, 1200);
  })();

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
      beginInstrumentLoad(midiInstrumentId);

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
        midiCurrent = 0;
        stopMidiClock();
        stopActiveMidiNotes();
        if (playbackLoop) {
          try {
            if (typeof midiPlayer.skipToTick === "function")
              midiPlayer.skipToTick(0);
            else if (typeof midiPlayer.stop === "function")
              midiPlayer.stop();
          } catch { /* ignore */ }
          midiPaused = false;
          try {
            midiPlayer.play();
            tickMidiClock();
          } catch {
            midiPaused = true;
          }
          return;
        }
        midiPaused = true;
        try { midiPlayer.stop(); } catch { /* ignore */ }
      });

      // Keep playhead in sync even if rAF is throttled (background tab / heavy Blazor renders).
      midiPlayer.on("playing", (payload) => {
        try {
          const tick = payload && typeof payload.tick === "number"
            ? payload.tick
            : (typeof midiPlayer.getCurrentTick === "function"
              ? midiPlayer.getCurrentTick()
              : (midiPlayer.tick || 0));
          const totalTicks = midiPlayer.totalTicks || 0;
          if (typeof midiPlayer.getSongTime === "function") {
            const total = midiPlayer.getSongTime();
            if (isFinite(total) && total > 0)
              midiDuration = total;
          }
          if (totalTicks > 0 && midiDuration > 0)
            midiCurrent = Math.max(0, Math.min(midiDuration, (tick / totalTicks) * midiDuration));
        } catch { /* ignore */ }
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
      el.loop = playbackLoop;
      let abs = url;
      try { abs = new URL(url, window.location.href).href; } catch { /* keep raw */ }
      // Avoid reload on every Blazor render — el.src is always absolute.
      if (el.src === abs) return;
      el.src = url;
      el.load();
    },

    setLoop(enabled) {
      playbackLoop = !!enabled;
      if (audioEl) audioEl.loop = playbackLoop;
      return playbackLoop;
    },

    getLoop() {
      return playbackLoop;
    },

    async play(el) {
      if (mode === "midi") {
        if (!midiPlayer) return false;
        await awaitInstrument();
        if (!midiInstrument) return false;
        if (midiAudioCtx?.state === "suspended")
          await midiAudioCtx.resume();
        applyTempo({ seek: false });
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
        stopActiveMidiNotes();
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
          stopActiveMidiNotes();
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
        // Always refresh from the player clock — UI polls this for the seek thumb.
        syncMidiTimesFromTicks();
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

    sampleChannelWaves,

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

    async setInstrument(id) {
      if (mode !== "midi" || !midiAudioCtx)
        return midiControlState();
      const name = normalizeInstrumentId(id);
      if (name === midiInstrumentId && midiInstrument && !midiInstrumentLoading)
        return midiControlState();
      try {
        if (midiAudioCtx.state === "suspended")
          await midiAudioCtx.resume();
        await beginInstrumentLoad(name);
      } catch (e) {
        console.warn("Instrument load failed", e);
      }
      return midiControlState();
    },

    /** Resolves once the pending soundfont is ready, for UI that shows progress. */
    async waitForInstrument() {
      await awaitInstrument();
      return midiControlState();
    },

    setChannelMuted(channel, muted) {
      if (mode !== "midi") return midiControlState();
      const ch = Number(channel);
      if (!isFinite(ch)) return midiControlState();
      if (muted) {
        mutedChannels.add(ch);
        stopActiveMidiNotes(ch);
      }
      else mutedChannels.delete(ch);
      return midiControlState();
    },

    setChannelVolume(channel, volume) {
      if (mode !== "midi") return midiControlState();
      const ch = Number(channel);
      const value = Number(volume);
      if (!isFinite(ch) || !isFinite(value)) return midiControlState();
      midiChannelVolumes[ch] = Math.max(0, Math.min(1, value));
      return midiControlState();
    },

    toggleChannelMute(channel) {
      if (mode !== "midi") return midiControlState();
      const ch = Number(channel);
      if (!isFinite(ch)) return midiControlState();
      if (mutedChannels.has(ch)) mutedChannels.delete(ch);
      else {
        mutedChannels.add(ch);
        stopActiveMidiNotes(ch);
      }
      return midiControlState();
    },

    muteAllChannels() {
      if (mode !== "midi") return midiControlState();
      for (const ch of midiChannels) mutedChannels.add(ch);
      stopActiveMidiNotes();
      return midiControlState();
    },

    unmuteAllChannels() {
      if (mode !== "midi") return midiControlState();
      mutedChannels.clear();
      return midiControlState();
    }
  };
})();
