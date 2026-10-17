# Does the Dog Die

Shows community-sourced content warnings from
[DoesTheDogDie.com](https://www.doesthedogdie.com) as an overlay when playback
starts. Episodes and seasons use the show's ratings, since the site rates
series as a whole.

## Setup

1. Get a free API key: sign in on DoesTheDogDie.com, open your profile, and copy
   the key shown there.
2. **Dashboard → Plugins → Does the Dog Die**: paste the key and save. Use the
   *Test lookup* box to confirm it works.
3. Hard-refresh the web client once so the injected overlay script loads.

## Settings

- **Overlay duration**, **minimum yes votes**, and **maximum warnings** control
  what is shown and for how long.
- **Users** and **Libraries**: uncheck anyone or any library that should not get
  warnings. New users and libraries are enabled automatically.
- **Inject script**: the plugin adds a `<script>` tag to jellyfin-web's
  `index.html` at startup, like Intro Skipper does. If the server cannot write
  to the web folder, the log shows the line to add manually.

## How it works

- `GET /DoesTheDogDie/Current?deviceId=…` (authenticated) returns warnings for
  whatever that device is playing. Lookups are cached.
- The overlay script polls that endpoint while a video is playing and shows the
  result once per item.
- Per-user and per-library checks happen on the server before any lookup, so
  disabled targets never hit the DoesTheDogDie API.
