#!/usr/bin/env bash
#
# One command to cut a new POS release and stage it for auto-deploy.
#
#   ./release.sh 3.0.2
#
# It bumps the version in code, builds a self-contained release, packs it with Velopack
# (generating a small delta against the previous build), and copies the server-side feed files
# into downloads/pos/ — which is committed, so `git push` makes Hostinger publish them to
#   https://posapi-v2.chaychaupal.com/downloads/pos/
# and every installed till picks the update up on its next launch.
#
# The full package chain stays in wpf/Releases/ (git-ignored, local) so the next release can build
# a delta against it; only the latest full + delta + index go into the committed feed.
set -euo pipefail

VER="${1:-}"
if [ -z "$VER" ]; then
    echo "Usage: ./release.sh <version>   e.g.  ./release.sh 3.0.2"
    exit 1
fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
FEED="$ROOT/downloads/pos"
export PATH="$PATH:$HOME/.dotnet/tools"

echo "==> Bumping version to $VER"
sed -i -E "s/public const string Version = \"[0-9.]+\";/public const string Version = \"$VER\";/" \
    "$ROOT/wpf/Pos.Core/AppInfo.cs"
sed -i -E "s#<Version>[0-9.]+</Version>#<Version>$VER</Version>#" \
    "$ROOT/wpf/Pos.App/Pos.App.csproj"

cd "$ROOT/wpf"

echo "==> Publishing self-contained build"
rm -rf publish-vpk
dotnet publish Pos.App/Pos.App.csproj -c Release -r win-x64 --self-contained true -o publish-vpk --nologo

echo "==> Packing with Velopack (delta against previous)"
ICON_FILE=$(sed -n 's/.*<ApplicationIcon>\(.*\)<\/ApplicationIcon>.*/\1/p' "$ROOT/wpf/Pos.App/Pos.App.csproj" | tr -d '\r')
if [ -z "$ICON_FILE" ]; then
    ICON_FILE="app_icon_light.ico"
fi

vpk pack --packId ChayChaupalPOS --packVersion "$VER" --packDir publish-vpk \
    --mainExe Pos.App.exe --packTitle "Chay Chaupal POS" --packAuthors "Chay Chaupal" \
    --outputDir Releases --icon "Pos.App/$ICON_FILE"


echo "==> Updating committed feed (downloads/pos/)"
mkdir -p "$FEED"
rm -f "$FEED"/*.nupkg
rm -f "$FEED"/*.exe
cp "Releases/ChayChaupalPOS-$VER-full.nupkg" "$FEED/"
# A delta only exists from the second release onward.
cp "Releases/ChayChaupalPOS-$VER-delta.nupkg" "$FEED/" 2>/dev/null || true
cp "Releases/ChayChaupalPOS-win-Setup.exe" "$FEED/"
cp Releases/RELEASES Releases/releases.win.json Releases/assets.win.json "$FEED/"

echo ""
echo "✓ v$VER ready. Installer: wpf/Releases/ChayChaupalPOS-win-Setup.exe"
echo ""
echo "  Publish the update:"
echo "    git add -A && git commit -m \"release v$VER\" && git push"
echo ""
echo "  Hostinger auto-deploys downloads/pos/ → tills auto-update on next launch."
