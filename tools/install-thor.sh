#!/usr/bin/env bash
# Installs the device build (arm64, no emulator patches) on the AYN Thor and streams the bundles into
# the app's files dir. Needs about 7 GB free on the Thor's internal storage. Reinstalling keeps the
# bundles and saves (install -r); only a first install or an uninstall needs the 6.4 GB push again.
# Ask the user before running this.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
[ -f "$HERE/local.env" ] && . "$HERE/local.env"   # machine paths, see local.env.example
SERIAL=${SERIAL:-${ANDROID_SERIAL:?set ANDROID_SERIAL in tools/local.env}}
ADB=${ADB:?set ADB in tools/local.env}
APK='E:\android-recompile-work\princess-android-work\apk\princess-android.apk'
"$ADB" -s "$SERIAL" shell df -h /data | tail -1
MSYS_NO_PATHCONV=1 "$ADB" -s "$SERIAL" install -r -g "$APK" | tail -1
ADB="$ADB" bash "$HERE/push-bundles.sh" "$SERIAL" 400
ADB="$ADB" bash "$HERE/push-bundles.sh" "$SERIAL" 400 | head -1   # second pass: should say "missing 0"
echo "launch: $ADB -s $SERIAL shell am start -n dev.kmprincess.magical/dev.kmprincess.cloud.LauncherActivity"
