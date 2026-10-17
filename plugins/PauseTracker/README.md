# Jellyfin Pause Tracker

A Jellyfin 12.x plugin that tracks how many times playback is paused and the
length of each pause interval, and shows the pause count plus accumulated paused
time for the current media in the video player overlay (next to the time display):

```
0:12:34 / 1:23:45   ⏸ 3 · 4:12
```

## How it works

- **Server side** — a hosted service subscribes to `ISessionManager` playback
  events. Clients report progress immediately on pause/unpause, so edges of the
  `IsPaused` flag give the pause count and interval lengths per session. Stats
  reset when a new item starts and are dropped when playback stops.
- **API** — `GET /PauseTracker/Stats?deviceId=...` (authenticated) returns
  `PauseCount`, `TotalPausedMs`, `IsPaused`, `CurrentPauseMs`, `IntervalsMs`.
- **Overlay** — at startup the plugin injects a small script tag into
  jellyfin-web's `index.html` (like Intro Skipper does). The script polls the
  stats endpoint once a second while the video OSD is open and renders the
  count and accumulated time. Injection can be disabled on the plugin's
  settings page; the manual one-liner is shown there too.

## Build

Install from the repository (see the [top-level README](../../README.md)), or
run `../../build.sh PauseTracker`, which produces `dist/PauseTracker_<ver>.zip`
containing the plugin DLL and `meta.json`.

## Install

1. Unzip `dist/PauseTracker_<ver>.zip` into a `PauseTracker_<ver>` folder in your Jellyfin server's `plugins`
   directory (Windows default: `C:\ProgramData\Jellyfin\Server\plugins`).
2. Restart Jellyfin.
3. Play something, pause a few times, and open the OSD — the counter appears
   next to the time display. (Hard-refresh the browser tab once after install
   so the injected script is picked up.)

## Notes

- Stats are per playback session and in-memory only — nothing is persisted.
- The web client overlay works in jellyfin-web (browser / desktop app). Other
  clients (Android TV, Kodi, etc.) are still *tracked* — they just can't show
  the overlay — and you can query the API for their sessions.
- If the server can't write to the web folder (some Docker images), the log
  shows the exact `<script>` line to add to `index.html` manually.
