#!/bin/bash
# Builds dist/showtime_<version>.zip (drop-in plugin folder) and dist/manifest.json (plugin repository).
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
VERSION=$(grep -oP '(?<=<AssemblyVersion>)[^<]+' src/Jellyfin.Plugin.Showtime/Jellyfin.Plugin.Showtime.csproj)
TARGET_ABI=10.11.8.0
dotnet build src/Jellyfin.Plugin.Showtime -c Release --nologo -v q
rm -rf dist/stage && mkdir -p dist/stage
cp src/Jellyfin.Plugin.Showtime/bin/Release/net9.0/Jellyfin.Plugin.Showtime.dll dist/stage/
cat > dist/stage/meta.json <<JSON
{
  "category": "General",
  "changelog": "See README",
  "description": "CinemaVision-style pre-show sequences: trivia, trailers, bumpers, ratings and audio-format cards, and light cues.",
  "guid": "7c3e5b2a-41d6-4f0e-9b8a-5d2c6e1f9a37",
  "name": "Showtime",
  "overview": "A CinemaVision-style pre-show for Jellyfin",
  "owner": "lcadaver",
  "targetAbi": "$TARGET_ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$VERSION"
}
JSON
ZIP="dist/showtime_$VERSION.zip"
rm -f "$ZIP" && (cd dist/stage && zip -q "../$(basename "$ZIP")" *)
SUM=$(md5sum "$ZIP" | cut -d' ' -f1)
cat > dist/manifest.json <<JSON
[{
  "guid": "7c3e5b2a-41d6-4f0e-9b8a-5d2c6e1f9a37",
  "name": "Showtime",
  "overview": "A CinemaVision-style pre-show for Jellyfin",
  "description": "Trivia reels, trailers, bumpers, ratings and audio-format cards, and Home Assistant light cues.",
  "owner": "lcadaver",
  "category": "General",
  "versions": [{
    "version": "$VERSION",
    "changelog": "See README",
    "targetAbi": "$TARGET_ABI",
    "sourceUrl": "${SHOWTIME_ZIP_URL:-REPLACE_WITH_URL_TO}/$(basename "$ZIP")",
    "checksum": "$SUM",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  }]
}]
JSON
echo "Built $ZIP ($SUM)"
