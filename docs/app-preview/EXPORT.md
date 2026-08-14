# Export — 9:16 App Store / Play preview

## Locked exports

| File | Duration | Source |
|------|----------|--------|
| `MobiHymn-preview-30s-9x16.mp4` | 30s | Primary store preview |
| `MobiHymn-preview-15s-9x16.mp4` | 15s | Teaser / thumbnail loop |

**Spec:** H.264, AAC (or silent), **1080×1920** (9:16), 30 fps, no letterboxing.

---

## Path A — Animatic now (no device footage yet)

1. Open `preview/index.html` in Chrome (full screen or focus the phone frame).
2. Click **Play 30s** (or **Play 15s**).
3. Screen-record **only the phone frame** (OBS / Win+Alt+R / QuickTime).
4. Import recording + burn in or replace captions from `captions/30s.srt` / `15s.srt`.
5. Optional: duck soft instrumental under; end on `preview/end-card.png`.
6. Export 1080×1920 mp4 with the filenames above into this folder when ready.

The HTML storyboard already matches the locked beat sheet and caption timings.

---

## Path B — Final cut from device footage

1. Capture passes in `SHOT_LIST.md`.
2. Edit to the timing table in `README.md`.
3. Drop SRT captions; sync VO from `VO_SCRIPT.md` if used.
4. End on `preview/end-card.png`.
5. Export both 30s and 15s (15s is a trim of primary footage).

---

## Checklist before App Store / Play upload

- [ ] Captions readable on yellow / charcoal
- [ ] No login, download, or install-gate screens
- [ ] Status bar clean
- [ ] Audio ducked / resolved cleanly
- [ ] 9:16, ≥1080 wide, ≤30s for primary preview slot rules on each store
