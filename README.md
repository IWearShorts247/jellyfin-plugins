# Jellyfin plugins

A Jellyfin plugin repository. Add it once and install or update any of the
plugins below from **Dashboard → Plugins → Catalog**.

## Add the repository

1. Open **Dashboard → Plugins → Repositories** and click **+**.
2. Name it anything, and set the URL to:

   ```
   https://raw.githubusercontent.com/IWearShorts247/jellyfin-plugins/main/manifest.json
   ```

3. Save, then open **Catalog**. The plugins appear under *General*.

Jellyfin only lists versions whose `targetAbi` is compatible with your server.

## Plugins

| Plugin | Jellyfin | What it does |
| --- | --- | --- |
| [Does the Dog Die](plugins/DoesTheDogDie) | 12.x | Content warnings from DoesTheDogDie.com when playback starts, with per-user and per-library toggles. |
| [Path Tagger](plugins/PathTagger) | 12.x | Tags items by folder path so shared libraries can be filtered per user with blocked tags. |
| [Pause Tracker](plugins/PauseTracker) | 12.x | Counts pauses and accumulated paused time, shown in the video OSD. |
| [Progress Eraser](plugins/ProgressEraser) | 12.x | Eraser button on media cards that resets watch progress and removes the item from Continue Watching. |
| [Showtime](plugins/Showtime) | 12.x | CinemaVision-style pre-show: trivia reels, trailers, bumpers, rating and audio-format cards, light cues. |

## Layout

```
plugins/<Name>/
  Jellyfin.Plugin.<Name>/   the project
  meta.json                 plugin metadata (version and timestamp are filled at build time)
  README.md                 plugin docs
build.sh                    builds zips into dist/ and updates manifest.json
tools/manifest.py           helper used by build.sh
manifest.json               the repository index Jellyfin reads
```

## Building and releasing

Requires the .NET 10 SDK. The version of a
plugin is its csproj `<AssemblyVersion>`.

```bash
./build.sh                    # all plugins → dist/<Name>_<version>.zip, manifest.json updated
./build.sh Showtime           # just one
./build.sh --release Showtime # also upload the zip to a GitHub release and push manifest.json
```

To ship a new version: bump `<AssemblyVersion>` and `<FileVersion>` in the
csproj, update `changelog` in the plugin's `meta.json`, commit, then run
`./build.sh --release <Name>`. Releases are tagged `<Name>-v<version>` and the
manifest's `sourceUrl` points at the release asset, so old versions stay
installable.

Manual install without the repository: unzip `dist/<Name>_<version>.zip` into
`<jellyfin data>/plugins/<Name>_<version>/` and restart Jellyfin.
