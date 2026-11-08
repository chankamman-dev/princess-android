#!/usr/bin/env bash
# Writes the port description the APK carries (assets/port/port.txt) and collects the PC-converted
# shader bundles (assets/port/shaders). See cloud/.../GameData.java for how the device uses them.
#   port.txt: app, depot, manifest (the depot manifest this build was made from), build, prefix,
#             bundles/bundlebytes (counts of the Windows originals),
#             "base <sha1> <depot path>" for every file outside the bundle folder (code and core data
#             baked into the APK: a Steam version that changes any of them needs a new APK),
#             "shader <sha1 of the Windows original> <bundle>" for every bundle shipped converted,
#             "shaderset <sha1>" naming the set of converted bundles (devices re-copy them when it changes).
# usage: make-port.sh <out dir>   (default E:\android-recompile-work\princess-android-work\port)
set -euo pipefail
OUT=${1:-/e/android-recompile-work/princess-android-work/port}
GAME="/h/SteamLibrary/steamapps/common/Magical Princess"
ACF=/h/SteamLibrary/steamapps/appmanifest_3562120.acf
CONVERTED=/e/android-recompile-work/princess-android-work/aa
REPORT=/e/android-recompile-work/princess-android-work/shader-report-bundles.txt
PREFIX='MagicalPrincess_Data\StreamingAssets\aa\StandaloneWindows64\'

manifest=$(awk '/"3562121"/{f=1} f && /"manifest"/{gsub(/"/,"",$2); print $2; exit}' "$ACF")
build=$(awk '/"buildid"/{gsub(/"/,"",$2); print $2; exit}' "$ACF")
[ -n "$manifest" ] && [ -n "$build" ] || { echo "no manifest/build in $ACF" >&2; exit 1; }

rm -rf "$OUT"; mkdir -p "$OUT/shaders"
T="$OUT/port.txt"
{
    echo "app 3562120"
    echo "depot 3562121"
    echo "manifest $manifest"
    echo "build $build"
    echo "prefix $PREFIX"
} > "$T"

cd "$GAME"
BD="MagicalPrincess_Data/StreamingAssets/aa/StandaloneWindows64"
echo "bundles $(find "$BD" -maxdepth 1 -name '*.bundle' | wc -l)" >> "$T"
echo "bundlebytes $(find "$BD" -maxdepth 1 -name '*.bundle' -printf '%s\n' | awk '{s+=$1} END {print s}')" >> "$T"

# Every non-empty depot file outside the bundle folder (Windows-only files included: harmless, they
# only make the comparison stricter).
find . -type f -size +0 ! -path "./$BD/*" -printf '%P\n' | sort | while IFS= read -r f; do
    h=$(sha1sum "$f" | cut -c1-40)
    echo "base $h ${f//\//\\}"
done >> "$T"

cut -d: -f1 "$REPORT" | sort -u | while read -r b; do
    [ -f "$CONVERTED/$b" ] || { echo "missing converted $b" >&2; exit 1; }
    echo "shader $(sha1sum "$BD/$b" | cut -c1-40) $b"
    cp "$CONVERTED/$b" "$OUT/shaders/$b"
done >> "$T"
echo "shaderset $(cd "$OUT/shaders" && sha1sum *.bundle | sha1sum | cut -c1-40)" >> "$T"

echo "port.txt: manifest $manifest build $build, $(grep -c '^base ' "$T") base files, $(grep -c '^shader ' "$T") shader bundles ($(du -sh "$OUT/shaders" | cut -f1))"
