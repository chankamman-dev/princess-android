#!/usr/bin/env bash
# Developer test helper: copies the Windows bundles (as Steam would deliver them) into the app's
# external files dir, where the launcher's hidden "localSource" mode installs from. Resumable, small
# batches (one long tar stream through adb dies part-way).
# usage: push-src.sh <serial> [batch]
set -euo pipefail
SERIAL=${1:?serial}
BATCH=${2:-300}
ADB=${ADB:-/g/AndroidLab/sdk/platform-tools/adb.exe}
SRC="/h/SteamLibrary/steamapps/common/Magical Princess/MagicalPrincess_Data/StreamingAssets/aa/StandaloneWindows64"
DST=/sdcard/Android/data/dev.kmprincess.magical/files/mp-src
TMP=$(mktemp -d)
export MSYS_NO_PATHCONV=1
"$ADB" -s "$SERIAL" shell "mkdir -p $DST"
for pass in 1 2 3; do
    "$ADB" -s "$SERIAL" shell "ls -l $DST" | tr -d '\r' | awk 'NF>=8 && $NF ~ /.bundle$/ {print $NF, $5}' | sort > "$TMP/have"
    (cd "$SRC" && find . -maxdepth 1 -name '*.bundle' -printf '%f %s\n') | sort > "$TMP/want"
    comm -23 "$TMP/want" "$TMP/have" | cut -d' ' -f1 > "$TMP/missing"
    n=$(wc -l < "$TMP/missing")
    echo "pass $pass: missing $n"
    [ "$n" -eq 0 ] && break
    rm -f "$TMP"/batch.*
    split -l "$BATCH" "$TMP/missing" "$TMP/batch."
    for b in "$TMP"/batch.*; do
        tar -C "$SRC" -cf - -T "$b" | "$ADB" -s "$SERIAL" exec-in "sh -c 'tar -xf - -C $DST'" || true
    done
done
rm -rf "$TMP"
