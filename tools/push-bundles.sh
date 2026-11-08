#!/usr/bin/env bash
# Streams the retargeted Addressables bundles (6.4 GB) into the app's internal files dir, where
# PrincessAndroid.Bridge.MapId looks first. Needs the debuggable APK installed (run-as).
# One long tar stream through `adb exec-in` dies after ~1.6 GB, so it goes in batches, and files
# already on the device (same name and size) are skipped: rerunning resumes.
# usage: push-bundles.sh <adb serial> [batch size]
set -euo pipefail
SERIAL=${1:?serial}
BATCH=${2:-1500}
ADB=${ADB:-/g/AndroidLab/sdk/platform-tools/adb.exe}
PKG=dev.kmprincess.magical
SRC=${SRC:-/e/android-recompile-work/princess-android-work/aa}
TMP=$(mktemp -d)
sh_as() { "$ADB" -s "$SERIAL" shell "run-as $PKG sh -c '$1'" | tr -d '\r'; }
sh_as 'mkdir -p files/aa'
# One process per side: toybox ls -l on the device, find -printf here (a stat per file is minutes on Windows).
sh_as 'ls -l files/aa' | awk 'NF>=8 && $NF ~ /.bundle$/ {print $NF, $5}' | sort > "$TMP/have"
find "$SRC" -maxdepth 1 -name '*.bundle' -printf '%f %s\n' | sort > "$TMP/want"
comm -23 "$TMP/want" "$TMP/have" | cut -d' ' -f1 > "$TMP/missing"
total=$(wc -l < "$TMP/missing")
echo "missing $total of $(wc -l < "$TMP/want")"
split -l "$BATCH" "$TMP/missing" "$TMP/batch."
t0=$(date +%s)
for b in "$TMP"/batch.*; do
    [ -f "$b" ] || continue
    tar -C "$SRC" -cf - -T "$b" | "$ADB" -s "$SERIAL" exec-in "run-as $PKG sh -c 'tar -xf - -C files/aa'"
    echo "  batch $(basename "$b"): $(sh_as 'ls files/aa | wc -l') on device, $(( $(date +%s) - t0 )) s"
done
rm -rf "$TMP"
