# MobiHymn Web (Blazor WASM PWA)

Browser client for MobiHymn. Lyrics load **on demand from the hymn API** through the host reverse proxy. The MAUI app continues to use the downloaded `lyrics.mb` catalog.

## Projects

| Project | Role |
|---|---|
| `MobiHymn4.Shared` | Models, path constants, `IHymnLyricsSource`, agent HTTP client |
| `MobiHymn4.Web` | Blazor WebAssembly PWA (UI + Firebase JS interop) |
| `MobiHymn4.Web.Host` | Serves WASM assets + proxies `/api/hymn/*` → `http://157.230.9.81/hymn/...` |

## Run

```bash
dotnet run --project MobiHymn4.Web.Host
```

Open the HTTPS URL from launchSettings.

### Deep links (MAUI parity)

| Link | Behavior |
|------|----------|
| `/read/{number}` | Open hymn in the reader |
| `/hymn/{number}` | Same as MAUI `mobihymn://hymn/{number}` — redirects to `/read/{number}` |
| `/auth/continue` | After Firebase email verification / password reset Continue — refreshes auth and routes to login, verify, profile, or account |
| `/login?mode=signup` | Open login in sign-up mode |
| `/groups?code={invite}` | Prefill / open join with invite code (web invite share) |

Verification and password-reset emails use the current origin’s `/auth/continue` as the Firebase continue URL (add that host under Authentication → Authorized domains).

## Firebase

1. Firebase Console → project `mobihymn` → add a **Web** app.
2. Paste `apiKey` and `appId` into `MobiHymn4.Web/wwwroot/js/firebase-config.js`.
3. Add your host domain under Authentication → Settings → Authorized domains.

Without Firebase config, reader / search / agent still work; auth and groups stay disabled.

### Catalog updates (installed PWA only)

Update badges run **only after** you install MobiHymn as a PWA and download the hymn library (Settings → Download library). Browser tabs keep on-demand API loading with no update checks. After download, startup and Settings → Resync compare `catalogHash` and show menu/Settings badges when the server catalog changed.

### App version updates (installed PWA only)

On startup the installed PWA reads Firebase Realtime DB `LatestRelease/Web` (`Version`, `DownloadUrl`, `Mandatory`) and compares to `ReleaseHistory.CurrentVersion`. Optional updates can be dismissed; mandatory updates keep prompting (same as MAUI Android/iOS).
