#!/bin/bash
# Builds Jellyfin plugins into installable zips and maintains manifest.json,
# the plugin-repository index Jellyfin reads.
#
#   ./build.sh                     build every plugin under plugins/
#   ./build.sh Showtime PathTagger build only these
#   ./build.sh --release [names]   also publish zips as GitHub release assets
#                                  and commit + push the updated manifest.json
#
# Each plugin lives at plugins/<Name>/Jellyfin.Plugin.<Name>/ with a
# plugins/<Name>/meta.json next to it. The version comes from the csproj's
# <AssemblyVersion>; meta.json's version and timestamp are filled at build time.
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

REPO="IWearShorts247/jellyfin-plugins"
RELEASE=0
NAMES=()
for arg in "$@"; do
    case "$arg" in
        --release) RELEASE=1 ;;
        -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
        *) NAMES+=("$arg") ;;
    esac
done
if [ ${#NAMES[@]} -eq 0 ]; then
    for d in plugins/*/; do NAMES+=("$(basename "$d")"); done
fi

mkdir -p dist
BUILT=()
for NAME in "${NAMES[@]}"; do
    PROJ="plugins/$NAME/Jellyfin.Plugin.$NAME"
    CSPROJ="$PROJ/Jellyfin.Plugin.$NAME.csproj"
    META="plugins/$NAME/meta.json"
    [ -f "$CSPROJ" ] || { echo "No project at $CSPROJ" >&2; exit 1; }
    [ -f "$META" ] || { echo "No meta.json at $META" >&2; exit 1; }

    VERSION=$(grep -oP '(?<=<AssemblyVersion>)[^<]+' "$CSPROJ")
    TFM=$(grep -oP '(?<=<TargetFramework>)[^<]+' "$CSPROJ")
    TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    echo "==> $NAME $VERSION ($TFM)"

    dotnet build "$PROJ" -c Release -v q

    STAGE="dist/stage/$NAME"
    rm -rf "$STAGE" && mkdir -p "$STAGE"
    cp "$PROJ/bin/Release/$TFM/Jellyfin.Plugin.$NAME.dll" "$STAGE/"
    python3 tools/manifest.py stage "$META" "$VERSION" "$TIMESTAMP" > "$STAGE/meta.json"

    ZIP="dist/${NAME}_${VERSION}.zip"
    rm -f "$ZIP" && (cd "$STAGE" && zip -q "../../$(basename "$ZIP")" ./*)
    SUM=$(md5sum "$ZIP" | cut -d' ' -f1)
    URL="https://github.com/$REPO/releases/download/$NAME-v$VERSION/$(basename "$ZIP")"
    python3 tools/manifest.py update manifest.json "$META" "$VERSION" "$TIMESTAMP" "$SUM" "$URL"
    echo "    $ZIP  md5 $SUM"
    BUILT+=("$NAME|$VERSION|$ZIP")
done

[ "$RELEASE" -eq 1 ] || exit 0

echo "==> Releasing"
for entry in "${BUILT[@]}"; do
    IFS='|' read -r NAME VERSION ZIP <<< "$entry"
    TAG="$NAME-v$VERSION"
    NOTES=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["changelog"])' "plugins/$NAME/meta.json")
    if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
        gh release upload "$TAG" "$ZIP" --repo "$REPO" --clobber
    else
        gh release create "$TAG" "$ZIP" --repo "$REPO" --title "$NAME $VERSION" --notes "$NOTES"
    fi
done
if ! git diff --quiet -- manifest.json; then
    git add manifest.json
    git commit -q -m "Update manifest.json" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
    git push -q
fi
echo "Repository manifest: https://raw.githubusercontent.com/$REPO/main/manifest.json"
