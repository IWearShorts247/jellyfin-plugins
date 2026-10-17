# Showtime

A CinemaVision-style pre-show for Jellyfin 12.x. Before a movie, cinema-mode clients play a sequence you design:
trivia reel → trailers → bumpers → rating card → audio-format bumper → feature, with HTTP light cues (e.g. Home Assistant) along the way.

## How it works

- **Intro provider.** Jellyfin asks plugins for "intros" when a client presses play; Showtime builds the list per movie and per viewer.
  Clients must have *Settings → Playback → Cinema mode* on (default). Tested path: Jellyfin Web / Jellyfin Media Player.
- **Content library.** Every clip must be a library item, so bumpers live in a dedicated library (type *Home Videos*), laid out like CinemaVision's content folder:

```
Showtime/
  Video Bumpers/<Countdown|Feature Intro|Trailers Intro|Trivia Intro|…>/*.mp4
  Ratings Bumpers/MPAA/PG-13.mp4   (also BBFC/, FSK/, DEJUS/ — file name = rating)
  Audio Format Bumpers/<Dolby Atmos|Dolby TrueHD|Dolby Digital Plus|Dolby Digital|DTS-X|DTS-HD Master Audio|DTS|Auro-3D|Other>/*.mp4
  Trailers/*.mp4                   (optional; otherwise other movies' local trailers are used)
  Trivia/…                         (slide images: Name_q.jpg, Name_c1.jpg, Name_a.jpg, or standalone images)
  Music/…                          (trivia background music)
  Trivia Reels/                    (written by the plugin)
```

  Old CinemaVision content packs drop straight in. Image bumpers aren't converted yet — videos only (trivia slides are fine as images).
- **Trivia reels.** Scheduled task *Showtime → Render trivia reels* (daily 04:00) renders N reels of M minutes with the server's ffmpeg:
  slide packs plus cards generated from your library (taglines, directors, release years, co-stars), over shuffled music.
- **Picking.** Bumpers/reels rotate by least-recently-seen per user. Trailers prefer unwatched movies and can be capped at the feature's rating.
  Audio bumpers fall back (Atmos → TrueHD → DD+ → DD → Other). Sequences can be limited by genre and user; specific beats catch-all.
- **Actions.** Named HTTP requests fired when a step starts, when the feature starts, and when it stops.
  Placeholders `{feature} {rating} {user} {step} {device}`. For Home Assistant use a webhook-triggered automation.
- **Editor.** Dashboard → Plugins → Showtime: drag-and-drop sequence editor, content stats, actions with a Test button, and a per-movie Preview.

## Build / install

Install from the repository (see the [top-level README](../../README.md)), or build with `../../build.sh Showtime`
and unzip `dist/Showtime_<ver>.zip` into `<jellyfin data>/plugins/Showtime_<ver>/` (e.g. `/var/lib/jellyfin/plugins/` or
`/config/data/plugins/` in Docker), then restart Jellyfin.

Targets Jellyfin 12.x (.NET 10, built against 12.2.0). The last 10.11 build is git tag `v0.1.0-jf10.11`.
Note Jellyfin 12 only accepts `Authorization: MediaBrowser Token="…"` — the old `X-Emby-Token` header and `?api_key=` are gone.

## Limits

- Native apps (Android TV, Roku, Swiftfin) vary in intro support; Web/JMP are the reliable clients.
- Jellyfin only asks for intros on a fresh start, not on resume — which is what you want.
- No post-roll (Jellyfin has no outro hook); use the feature-end action for lights.
