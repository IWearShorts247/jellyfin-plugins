# Progress Eraser

Adds an eraser button to media cards, next to *Favorite* and *Mark Played* in
the hover menu, and to the item page button row. Clicking it clears your
playback position for that item, which removes it from *Continue Watching*.

Unlike *Mark Unplayed*, it leaves the played flag and play count alone, so a
half-finished rewatch can be dropped without losing the item's history. On a
series, season or collection it resets every in-progress episode inside.

## Settings

- **Show on every card**: by default the eraser only appears on cards that show
  a progress bar, which is where it is useful. Turn this on to show it on all
  cards.
- **Show on item page**: adds the eraser to the button row while the item is
  resumable (the Play button reads *Resume*).
- **Inject script**: the plugin adds a `<script>` tag to jellyfin-web's
  `index.html` at startup, like Intro Skipper does. If the server cannot write
  to the web folder, the log shows the line to add manually.

Hard-refresh the web client once after installing so the script loads.

## How it works

- `POST /ProgressEraser/{itemId}` (authenticated) zeroes `PlaybackPositionTicks`
  for the calling user. The user comes from the auth token, so nobody can reset
  another user's progress. Returns `{ "ItemsReset": n }`.
- The client script watches the DOM for card hover menus and the item page
  button row and adds the button. After a reset it removes the card's progress
  bar; the server also pushes a `UserDataChanged` event that jellyfin-web
  handles on its own.
- The eraser icon is inline SVG, so it does not depend on the icon font version.
