# Progress Eraser

Two things Jellyfin does not offer on its own:

- **Reset progress.** An eraser button on media cards, next to *Favorite* and
  *Mark Played* in the hover menu, and on the item page. Clicking it clears
  your playback position for that item, which removes it from *Continue
  Watching*. Unlike *Mark Unplayed*, it leaves the played flag and play count
  alone. On a series, season or collection it resets every in-progress episode.
- **Hide from Next Up.** An eye button on Next Up cards, and a show/hide toggle
  on series, season and episode pages. Hidden shows are filtered out of the
  server's Next Up results, so they vanish from every client, not just the web
  UI. Playing an episode of a hidden show puts it back automatically.

## Settings

- **Show eraser on every card**: by default it only appears on cards with a
  progress bar. Turn this on to show it everywhere.
- **Show eraser on item page**: adds it to the button row while the item is
  resumable (the Play button reads *Resume*).
- **Remove hidden shows on the server**: the server-side Next Up filter. Turn
  off to keep the lists per user but stop filtering.
- **Hide/show buttons in the web client**, and **put a show back when played**.
- **Hidden shows**: every user's hidden shows, each with an *Unhide* button.
- **Inject script**: the plugin adds a `<script>` tag to jellyfin-web's
  `index.html` at startup, like Intro Skipper does. If the server cannot write
  to the web folder, the log shows the line to add manually.

Hard-refresh the web client once after installing so the script loads.

## How it works

- `POST /ProgressEraser/{itemId}` (authenticated) zeroes `PlaybackPositionTicks`
  for the calling user. The user comes from the auth token, so nobody can reset
  another user's progress. Returns `{ "ItemsReset": n }`.
- `POST /ProgressEraser/NextUp/Hide/{itemId}` hides the show an episode, season
  or series belongs to; `DELETE /ProgressEraser/NextUp/Hide/{seriesId}` shows
  it again; `GET /ProgressEraser/NextUp/Hidden` lists yours. Admin variants
  under `NextUp/Hidden/All` and `NextUp/Hidden/{userId}/{seriesId}` back the
  settings page.
- The Next Up filter is ASP.NET middleware registered through an
  `IStartupFilter`. It intercepts `GET /Shows/NextUp`, asks the server for a
  few extra rows to backfill, drops items whose `SeriesId` is hidden for the
  requesting user, and passes everything else through untouched. If anything
  goes wrong it falls back to the original response.
- The client script watches the DOM for card hover menus and the item page
  button row. After a reset it removes the card's progress bar; after a hide
  it removes the card. The server also pushes a `UserDataChanged` event that
  jellyfin-web handles on its own.
- Icons are inline SVG, so they do not depend on the icon font version.
