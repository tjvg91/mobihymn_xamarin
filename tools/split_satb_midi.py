"""Split hymn MIDIs into SATB plus octave-doubling alts.

Typical MIDISCAN / piano-hymn layout in midi/:
  ch 0 Treble     — Soprano + Alto (+ soprano octave doubling)
  ch 1 Bass inner — Tenor + bass octave doubling
  ch 3 Bass       — Bass
  ch 2 unused

Two-staff SA/TB files (no ch 3), e.g. h731:
  ch 0 SA         — Soprano + Alto
  ch 1 TB         — Tenor + Bass
  A single note on a staff is treated as a unison (copied to both voices).

Output channels:
  0 Soprano      highest treble note
  1 Alto         treble note that is not soprano or soprano-octave
  2 Tenor        highest bass-inner / TB note
  3 Bass         original bass, or the lower TB note when there is no bass track
  4 Soprano Alt  treble octave doubling of soprano
  5 Bass Alt     bass-inner octave doubling of bass (3-staff files)

If Alto and Tenor are the same pitch (unison), the note is written on both
channels. A missing Alto or Tenor is filled from the other when the rest of
the chord is sounding.
"""

from __future__ import annotations

import argparse
import sys
from collections import defaultdict
from pathlib import Path

import mido

CH_S, CH_A, CH_T, CH_B, CH_SALT, CH_BALT = 0, 1, 2, 3, 4, 5
SATB_NAMES = {
    CH_S: "Soprano",
    CH_A: "Alto",
    CH_T: "Tenor",
    CH_B: "Bass",
    CH_SALT: "Soprano Alt",
    CH_BALT: "Bass Alt",
}
DEST_CHANNELS = (CH_S, CH_A, CH_T, CH_B, CH_SALT, CH_BALT)

Note = tuple[int, int, int, int]  # start, end, pitch, velocity
MIN_CHORD_OVERLAP = 12  # ignore 1-tick MIDISCAN staggers / tiny legato


KEEP_CC = {7, 10, 11}  # volume, pan, expression


def extract_notes(track: mido.MidiTrack):
    """Return (notes, extras, channel)."""
    abs_time = 0
    pending: dict[int, list[tuple[int, int]]] = defaultdict(list)
    notes: list[Note] = []
    extras: list[tuple[int, mido.Message]] = []
    channel = None

    for msg in track:
        abs_time += msg.time
        if msg.type in ("track_name", "end_of_track", "instrument_name"):
            continue
        if msg.type == "note_on" and msg.velocity > 0:
            channel = msg.channel if channel is None else channel
            pending[msg.note].append((abs_time, msg.velocity))
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            channel = msg.channel if channel is None else channel
            stack = pending[msg.note]
            if stack:
                start, vel = stack.pop(0)
                if abs_time > start:
                    notes.append((start, abs_time, msg.note, vel))
        elif msg.type == "program_change":
            extras.append((abs_time, msg.copy()))
        elif msg.type == "control_change" and msg.control in KEEP_CC:
            extras.append((abs_time, msg.copy()))

    return notes, extras, channel


def overlap_ticks(a: Note, b: Note) -> int:
    return max(0, min(a[1], b[1]) - max(a[0], b[0]))


def overlapping_pitches(note: Note, peers: list[Note]) -> list[int]:
    pitches = {note[2]}
    for other in peers:
        if other is note or overlap_ticks(note, other) >= MIN_CHORD_OVERLAP:
            pitches.add(other[2])
    return sorted(pitches, reverse=True)


def any_overlap(note: Note, peers: list[Note]) -> bool:
    return any(overlap_ticks(note, other) >= MIN_CHORD_OVERLAP for other in peers)


def dest_treble(note: Note, treble: list[Note]) -> int:
    pitches = overlapping_pitches(note, treble)
    highest = pitches[0]
    pitch = note[2]
    if pitch == highest:
        return CH_S
    if (highest - pitch) in (12, 24):
        return CH_SALT
    return CH_A


def dest_inner(note: Note, inner: list[Note], two_staff: bool) -> int:
    pitches = overlapping_pitches(note, inner)
    pitch = note[2]
    highest = pitches[0]
    if pitch == highest:
        return CH_T
    if not two_staff:
        return CH_BALT
    lowers = [p for p in pitches if p != highest]
    bass_written = max(lowers)
    if pitch == bass_written:
        return CH_B
    if (bass_written - pitch) in (12, 24) or (highest - pitch) in (12, 24):
        return CH_BALT
    return CH_B


def keep_highest_on_overlaps(notes: list[Note]) -> list[Note]:
    kept: list[Note] = []
    for note in sorted(notes, key=lambda n: (-n[2], n[0])):
        if any(overlap_ticks(note, other) >= MIN_CHORD_OVERLAP for other in kept):
            continue
        kept.append(note)
    return kept


def fill_at_unison(
    altos: list[Note],
    tenors: list[Note],
    sopranos: list[Note],
    basses: list[Note],
) -> tuple[list[Note], list[Note]]:
    """If A or T is missing while the chord is sounding, copy the shared unison both ways."""
    extra_a: list[Note] = []
    extra_t: list[Note] = []
    for tenor in tenors:
        if any_overlap(tenor, altos):
            continue
        if any_overlap(tenor, sopranos):
            extra_a.append(tenor)
    for alto in altos:
        if any_overlap(alto, tenors):
            continue
        if any_overlap(alto, basses):
            extra_t.append(alto)
    return altos + extra_a, tenors + extra_t


def fill_sa_tb_unison(
    sopranos: list[Note],
    altos: list[Note],
    tenors: list[Note],
    basses: list[Note],
) -> tuple[list[Note], list[Note]]:
    """On 2-staff SA/TB files, a single staff note is a unison of that pair."""
    extra_a: list[Note] = []
    extra_b: list[Note] = []
    tb = tenors + basses
    sa = sopranos + altos
    for soprano in sopranos:
        if any_overlap(soprano, altos):
            continue
        if any_overlap(soprano, tb):
            extra_a.append(soprano)
    for tenor in tenors:
        if any_overlap(tenor, basses):
            continue
        if any_overlap(tenor, sa):
            extra_b.append(tenor)
    return altos + extra_a, basses + extra_b


def assign_segments(notes_by_ch: dict[int, list[Note]]) -> dict[int, list[Note]]:
    treble = notes_by_ch.get(0, [])
    inner = notes_by_ch.get(1, [])
    bass = notes_by_ch.get(3, [])
    two_staff = not bass
    out: dict[int, list[Note]] = defaultdict(list)

    for note in treble:
        out[dest_treble(note, treble)].append(note)

    for note in inner:
        out[dest_inner(note, inner, two_staff)].append(note)

    out[CH_B].extend(bass)

    for ch, notes in notes_by_ch.items():
        if ch in (0, 1, 3):
            continue
        out[ch].extend(notes)

    for dest in (CH_S, CH_A, CH_T, CH_SALT, CH_BALT):
        out[dest] = keep_highest_on_overlaps(out[dest])
    if two_staff:
        out[CH_B] = keep_highest_on_overlaps(out[CH_B])

    if two_staff:
        out[CH_A], out[CH_B] = fill_sa_tb_unison(
            out[CH_S], out[CH_A], out[CH_T], out[CH_B]
        )
        out[CH_A] = keep_highest_on_overlaps(out[CH_A])
        out[CH_B] = keep_highest_on_overlaps(out[CH_B])
    else:
        out[CH_A], out[CH_T] = fill_at_unison(
            out[CH_A], out[CH_T], out[CH_S], out[CH_B] + out[CH_BALT]
        )
        out[CH_A] = keep_highest_on_overlaps(out[CH_A])
        out[CH_T] = keep_highest_on_overlaps(out[CH_T])
    return out


def retarget_cc(extras: list[tuple[int, mido.Message]], dest_ch: int) -> list[tuple[int, mido.Message]]:
    out = []
    for t, msg in extras:
        if msg.is_meta:
            out.append((t, msg))
            continue
        copied = msg.copy()
        if hasattr(copied, "channel"):
            copied.channel = dest_ch
        out.append((t, copied))
    return out


def build_track(
    name: str,
    channel: int,
    notes: list[Note],
    extras: list[tuple[int, mido.Message]],
) -> mido.MidiTrack:
    events: list[tuple[int, int, mido.Message]] = []
    events.append((0, 0, mido.MetaMessage("track_name", name=name, time=0)))
    events.append((0, 0, mido.MetaMessage("instrument_name", name=name, time=0)))
    for t, msg in extras:
        events.append((t, 1, msg))
    for start, end, pitch, vel in notes:
        events.append((start, 2, mido.Message("note_on", channel=channel, note=pitch, velocity=vel, time=0)))
        events.append((end, 0, mido.Message("note_on", channel=channel, note=pitch, velocity=0, time=0)))
    events.sort(key=lambda e: (e[0], e[1], getattr(e[2], "note", 0)))

    track = mido.MidiTrack()
    last = 0
    for t, _order, msg in events:
        delta = t - last
        track.append(msg.copy(time=delta))
        last = t
    return track


def meta_only_track(track: mido.MidiTrack) -> mido.MidiTrack:
    """Copy tempo/time/key from a track, dropping notes, names, and lyrics."""
    keep = {"time_signature", "key_signature", "set_tempo"}
    meta = mido.MidiTrack()
    abs_events: list[tuple[int, mido.Message]] = []
    t = 0
    for msg in track:
        t += msg.time
        if not (msg.is_meta and msg.type in keep):
            continue
        abs_events.append((t, msg.copy(time=0)))
    last = 0
    for t, msg in abs_events:
        meta.append(msg.copy(time=t - last))
        last = t
    return meta


def apply_lead_in(
    lead: int,
    notes_by_ch: dict[int, list[Note]],
    extras_by_ch: dict[int, list[tuple[int, mido.Message]]],
    assigned: dict[int, list[Note]],
    meta_track: mido.MidiTrack,
) -> mido.MidiTrack:
    """Shift sounding events later so the first chord is not on tick 0.

    midi-player-js can drop or chop notes that start at tick 0, especially when
    a track has many setup events before the first note-on.
    """
    if lead <= 0:
        return meta_track

    def shift_notes(notes: list[Note]) -> list[Note]:
        return [(s + lead, e + lead, p, v) for s, e, p, v in notes]

    def shift_extras(extras: list[tuple[int, mido.Message]]):
        setup = {"control_change", "program_change", "midi_port"}
        out = []
        for t, msg in extras:
            keep = t == 0 and (msg.is_meta or msg.type in setup)
            out.append((t if keep else t + lead, msg))
        return out

    for ch in list(notes_by_ch):
        notes_by_ch[ch] = shift_notes(notes_by_ch[ch])
    for ch in list(assigned):
        assigned[ch] = shift_notes(assigned[ch])
    for ch in list(extras_by_ch):
        extras_by_ch[ch] = shift_extras(extras_by_ch[ch])

    shifted_meta = mido.MidiTrack()
    abs_events: list[tuple[int, mido.Message]] = []
    t = 0
    setup_meta = {"time_signature", "key_signature", "set_tempo"}
    for msg in meta_track:
        t += msg.time
        keep = t == 0 and msg.is_meta and msg.type in setup_meta
        abs_events.append((t if keep else t + lead, msg.copy(time=0)))
    last = 0
    for abs_t, msg in abs_events:
        shifted_meta.append(msg.copy(time=abs_t - last))
        last = abs_t
    return shifted_meta


def split_file(src: Path, dest: Path) -> dict:
    mid = mido.MidiFile(src)
    meta_track = meta_only_track(mid.tracks[0]) if mid.tracks else mido.MidiTrack()

    notes_by_ch: dict[int, list[Note]] = defaultdict(list)
    extras_by_ch: dict[int, list[tuple[int, mido.Message]]] = {}

    for track in mid.tracks:
        notes, extras, ch = extract_notes(track)
        if ch is None or not notes:
            continue
        notes_by_ch[ch].extend(notes)
        if ch not in extras_by_ch:
            extras_by_ch[ch] = extras

    assigned = assign_segments(notes_by_ch)

    first_note = min((n[0] for notes in notes_by_ch.values() for n in notes), default=1)
    if first_note == 0:
        meta_track = apply_lead_in(
            mid.ticks_per_beat, notes_by_ch, extras_by_ch, assigned, meta_track
        )

    out = mido.MidiFile(type=1, ticks_per_beat=mid.ticks_per_beat)
    out.tracks.append(meta_track)

    cc_source = {
        CH_S: extras_by_ch.get(0, []),
        CH_A: extras_by_ch.get(0, []),
        CH_T: extras_by_ch.get(1, []),
        CH_B: extras_by_ch.get(3, []) or extras_by_ch.get(1, []),
        CH_SALT: extras_by_ch.get(0, []),
        CH_BALT: extras_by_ch.get(1, []),
    }

    stats = {"src": src.name, "in": {}, "out": {}}
    for ch, notes in sorted(notes_by_ch.items()):
        stats["in"][ch] = len(notes)

    for dest_ch in DEST_CHANNELS:
        notes = assigned.get(dest_ch, [])
        stats["out"][dest_ch] = len(notes)
        extras = retarget_cc(cc_source.get(dest_ch, []), dest_ch)
        out.tracks.append(build_track(SATB_NAMES[dest_ch], dest_ch, notes, extras))

    extra_chs = sorted(ch for ch in notes_by_ch if ch not in (0, 1, 3))
    next_ch = 6
    for ch in extra_chs:
        dest_ch = ch if ch not in DEST_CHANNELS else next_ch
        if dest_ch == next_ch:
            next_ch += 1
        notes = notes_by_ch[ch]
        extras = extras_by_ch.get(ch, [])
        out.tracks.append(build_track(f"Other {ch}", dest_ch, notes, extras))

    dest.parent.mkdir(parents=True, exist_ok=True)
    out.save(dest)
    return stats


def max_polyphony(path: Path) -> dict[int, int]:
    mid = mido.MidiFile(path)
    sounding: dict[int, set[int]] = defaultdict(set)
    maxp: dict[int, int] = defaultdict(int)
    for msg in mido.merge_tracks(mid.tracks):
        if msg.type == "note_on" and msg.velocity > 0:
            sounding[msg.channel].add(msg.note)
            maxp[msg.channel] = max(maxp[msg.channel], len(sounding[msg.channel]))
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            sounding[msg.channel].discard(msg.note)
    return dict(maxp)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", default="midi", help="MIDI file or folder (default: midi)")
    parser.add_argument("-o", "--output", default="midi-satb", help="Output file or folder (default: midi-satb)")
    parser.add_argument("--limit", type=int, default=0, help="Process at most N files (0 = all)")
    args = parser.parse_args(argv)

    root = Path(__file__).resolve().parents[1]
    src = Path(args.input)
    if not src.is_absolute():
        src = root / src
    dest_arg = Path(args.output)
    if not dest_arg.is_absolute():
        dest_arg = root / dest_arg

    files = sorted(src.glob("*.mid")) if src.is_dir() else [src]
    if args.limit:
        files = files[: args.limit]
    if not files:
        print(f"No MIDI files in {src}", file=sys.stderr)
        return 1

    ok = 0
    for f in files:
        out_path = dest_arg / f.name if src.is_dir() or dest_arg.suffix.lower() != ".mid" else dest_arg
        try:
            stats = split_file(f, out_path)
            poly = max_polyphony(out_path)
            print(f"{f.name}: in {stats['in']} -> out {stats['out']}  maxpoly {poly}")
            ok += 1
        except Exception as exc:  # noqa: BLE001
            print(f"{f.name}: FAILED {exc}", file=sys.stderr)

    print(f"Wrote {ok}/{len(files)} files to {dest_arg}")
    return 0 if ok == len(files) else 1


if __name__ == "__main__":
    raise SystemExit(main())
