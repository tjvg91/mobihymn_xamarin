# MobiHymn demo video snippets

Recorded automatically against the local host (`http://localhost:5297`) with a mobile viewport, as if tapping through the app.

| File | Flow |
|------|------|
| `01-open-hymn.webm` | Open hymn #215 and scroll lyrics |
| `02-search.webm` | Search “amazing” and open a result |
| `03-number-entry.webm` | Number-entry screen (grid) |
| `04-numpad-entry.webm` | Enter hymn #215 on the numpad and open |
| `05-voice-entry.webm` | Say a hymn number (voice mode → #77) |
| `06-midi-settings.webm` | MIDI tempo, key, instrument, mute |
| `07-selah-ai.webm` | Ask Selah for Easter hymn suggestions |
| `08-group-board.webm` | Open group board → setlist → hymns |

## Re-record

1. Start the host: `dotnet run --project MobiHymn4.Web.Host --urls http://127.0.0.1:5297`
2. From `tools/demo-record`:
   - Clips 01–03: `node record-demos.mjs`
   - Clips 04–07: `node record-requested-demos.mjs`
   - Clip 08 (needs auth): log in once with `node record-board-demo.mjs --save-auth`, then `node record-board-demo.mjs`
     - Or set `MOBIHYMN_DEMO_EMAIL` / `MOBIHYMN_DEMO_PASSWORD`

Clips are WebM (Playwright). Convert with ffmpeg if you need MP4:

```bash
ffmpeg -i 01-open-hymn.webm -c:v libx264 -pix_fmt yuv420p 01-open-hymn.mp4
```

There is also a polished **animated phone-frame preview** (not live app) at `docs/app-preview/preview/index.html`.
