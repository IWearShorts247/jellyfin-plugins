# Path Tagger

Automatically tags library items by folder path. Combined with Jellyfin's
per-user **blocked tags**, this lets one shared library serve different users
different content, for example hiding a `Kids/` folder from adult profiles or
vice versa.

## Setup

1. **Dashboard → Plugins → Path Tagger**: add mappings, one per line, as
   `<folder path>|<tag>`. Every item whose file lives under the folder gets the
   tag.
2. Leave **Tag new items** on to tag items as they are added. The scheduled task
   *Tag items by path* sweeps the whole library and can be run on demand.
3. For each user, **Dashboard → Users → Parental control → Block items with
   tags** and add the tags they should not see.

## Tools

`tools/export-watchstate.ps1` and `tools/import-watchstate.ps1` export every
user's watch state (played, play count, resume position, favorite) keyed by
file path and re-apply it later. Useful when merging several libraries into one
shared library before adopting this plugin: export, rebuild the library, scan,
then import. Run import with `-DryRun` first.
