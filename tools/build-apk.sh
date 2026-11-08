#!/usr/bin/env bash
# Assembles dev.kmprincess.magical: il2cpp output + retargeted player data in the APK. The 6.4 GB of
# Addressables bundles are not in the APK; push-bundles.sh streams them into the app's files dir.
set -euo pipefail
HERE_ENV="$(cd "$(dirname "$0")" && pwd)/local.env"
[ -f "$HERE_ENV" ] && . "$HERE_ENV"   # machine paths, see local.env.example

W=/e/android-recompile-work/princess-android-work
AP=$W/unity/Editor/Data/PlaybackEngines/AndroidPlayer
VAR=$AP/Variations/il2cpp/${VARIATION:-Release}
SDK="$(cygpath -u "${ANDROID_SDK_ROOT:?set ANDROID_SDK_ROOT in tools/local.env}")"
BT="$SDK/build-tools/36.0.0"
JAR="$SDK/platforms/android-35/android.jar"
OUT=${OUT:-$W/apk}
ABI=${ABI:-arm64-v8a}
if [ "$ABI" = x86_64 ]; then IL2CPP=${IL2CPP:-$W/il2cpp-x64}; else IL2CPP=${IL2CPP:-$W/il2cpp}; fi
DATA=${DATA:-$W/data}
SRC_DATA='/h/SteamLibrary/steamapps/common/Magical Princess/MagicalPrincess_Data'
HERE="$(cd "$(dirname "$0")" && pwd)"
Z='/c/Program Files/7-Zip/7z.exe'
export PATH="$PATH:/c/Program Files/Eclipse Adoptium/jdk-17.0.20.8-hotspot/bin"

rm -rf "$OUT/res-flat" "$OUT/gen" "$OUT/java" "$OUT/root"
mkdir -p "$OUT/res-flat" "$OUT/gen" "$OUT/java" "$OUT/root/lib/$ABI" "$OUT/root/assets"
cp "$HERE/AndroidManifest.xml" "$OUT/AndroidManifest.xml"

# Resources and manifest. The GameActivity themes need AppCompat, which this Activity build lacks.
rm -rf "$OUT/res"
cp -r "$AP/Apk/res" "$OUT/res"
sed -i 's|parent="Theme.AppCompat.Light.NoActionBar"|parent="android:Theme.Holo.Light.NoActionBar.Fullscreen"|' "$OUT/res/values/styles.xml" "$OUT/res/values-v31/styles.xml"
# Unity's Gradle template merges values/unityLibrary and values/launcher; aapt2 only reads flat dirs.
for sub in unityLibrary launcher; do mv "$OUT/res/values/$sub/strings.xml" "$OUT/res/values/strings_$sub.xml"; rmdir "$OUT/res/values/$sub"; done
# App icon (art/icon, from the game's own 256 px Windows icon via pctest/Icon.java) replaces Unity's,
# and the launcher name follows the system language.
rm -f "$OUT/res/mipmap-mdpi/"*.png
cp -r "$HERE/../art/icon/." "$OUT/res/"
cp -r "$HERE/../art/label/." "$OUT/res/"
"$BT/aapt2.exe" compile --dir "$(cygpath -w "$OUT/res")" -o "$(cygpath -w "$OUT/res-flat/res.zip")"
"$BT/aapt2.exe" link -o "$(cygpath -w "$OUT/base.apk")" -I "$(cygpath -w "$JAR")" \
    --manifest "$(cygpath -w "$OUT/AndroidManifest.xml")" --java "$(cygpath -w "$OUT/gen")" \
    --custom-package com.unity3d.player --min-sdk-version 26 --target-sdk-version 30 --version-code 1 --version-name 1.0 \
    "$(cygpath -w "$OUT/res-flat/res.zip")"

# Immersive full screen (patch-activity.js) on a copy of the activity source.
mkdir -p "$OUT/src"
node "$(cygpath -w "$HERE/patch-activity.js")" "$(cygpath -w "$AP/Source/com/unity3d/player/UnityPlayerActivity.java")" "$(cygpath -w "$OUT/src/UnityPlayerActivity.java")"
# Unity's activity ships as source only.
javac -nowarn -source 8 -target 8 -cp "$(cygpath -w "$VAR/Classes/classes.jar");$(cygpath -w "$JAR")" -d "$OUT/java" \
    "$(cygpath -w "$OUT/src/UnityPlayerActivity.java")" $(find "$OUT/gen" -name '*.java' -exec cygpath -w {} \;) 2>&1 | grep -v '^warning\|^Note\|^注' || true
(cd "$OUT/java" && jar cf ../activity.jar .)
# The Steam launcher (cloud/) and everything it depends on are dexed alongside Unity.
CLOUD="$HERE/../cloud/build"
CLOUDJARS=()
for j in "$CLOUD/libs/princess-cloud.jar" "$CLOUD"/deps/*.jar; do CLOUDJARS+=("$(cygpath -w "$j")"); done
cmd.exe //c "$(cygpath -w "$BT/d8.bat")" --release --min-api 26 --lib "$(cygpath -w "$JAR")" \
    --output "$(cygpath -w "$OUT/root")" "$(cygpath -w "$OUT/activity.jar")" "$(cygpath -w "$VAR/Classes/classes.jar")" "${CLOUDJARS[@]}" \
    2>&1 | grep -vE '^Warning|Missing class|^\s*$' | head -20 || true
ls "$OUT/root"/classes*.dex

# Native libraries.
cp "$VAR/Libs/$ABI/libmain.so" "$VAR/Libs/$ABI/libunity.so" "$OUT/root/lib/$ABI/"
# il2cpp links with debug info (185 MB); the package gets a stripped copy.
STRIP="$(cygpath -u "${NDK:?set NDK in tools/local.env}")/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-strip.exe"
"$STRIP" --strip-debug -o "$(cygpath -w "$OUT/root/lib/$ABI/libil2cpp.so")" "$(cygpath -w "$IL2CPP/out/libil2cpp.so")"
# EMU=1: a build for the emulator's arm64 translator, which lacks SEVL/WFE. Never for a real device.
if [ "${EMU:-}" = 1 ] && [ "$ABI" = arm64-v8a ]; then
    for l in libunity.so libil2cpp.so; do
        node "$(cygpath -w "$HERE/nop-wfe.js")" "$(cygpath -w "$OUT/root/lib/$ABI/$l")" "$(cygpath -w "$OUT/root/lib/$ABI/$l")"
    done
fi

# Player data.
D="$OUT/root/assets/bin/Data"
mkdir -p "$D/Managed/Metadata" "$D/Managed/Resources" "$D/Resources"
cp -r "$DATA/." "$D/"
# The engine's own resources for this platform; the Windows player kept them in Resources/.
cp "$AP/Data/Resources/unity default resources" "$D/unity default resources"
cp "$AP/Data/Resources/unity default resources" "$D/Resources/unity default resources"
# Windows-only graphics threading settings out; il2cpp's GC slicing in.
grep -vE '^(gfx-enable-gfx-jobs|gfx-threading-mode|single-instance)=' "$DATA/boot.config" > "$D/boot.config"
echo 'gc-max-time-slice=3' >> "$D/boot.config"
cp "$IL2CPP/data/Metadata/global-metadata.dat" "$D/Managed/Metadata/"
cp "$IL2CPP/data/Resources/"*.dat "$D/Managed/Resources/" 2>/dev/null || true
[ -d "$IL2CPP/data/etc" ] && cp -r "$IL2CPP/data/etc" "$D/Managed/etc"
# The player only loads what ScriptingAssemblies.json lists.
node "$(cygpath -w "$HERE/add-assembly.js")" "$(cygpath -w "$D/ScriptingAssemblies.json")" PrincessAndroid.dll
GUID=$(sha1sum "$IL2CPP/data/Metadata/global-metadata.dat" | cut -c1-32)
echo "$GUID" > "$D/unity_app_guid"

# Addressables: catalog and settings stay in the package; bundles are external (Bridge.MapId).
mkdir -p "$OUT/root/assets/aa"
cp "$SRC_DATA/StreamingAssets/aa/catalog.bin" "$SRC_DATA/StreamingAssets/aa/catalog.hash" "$SRC_DATA/StreamingAssets/aa/settings.json" "$OUT/root/assets/aa/"
cp -r "$SRC_DATA/StreamingAssets/aa/AddressablesLink" "$OUT/root/assets/aa/"
cp "$SRC_DATA/StreamingAssets/UnityServicesProjectConfiguration.json" "$OUT/root/assets/"
# Port description, shader bundles converted on the PC, launcher art (see make-port.sh, GameData.java).
PORT=${PORT:-/e/android-recompile-work/princess-android-work/port}
[ -f "$PORT/port.txt" ] || { echo "run make-port.sh first" >&2; exit 1; }
mkdir -p "$OUT/root/assets/port"
cp -r "$PORT/." "$OUT/root/assets/port/"
cp "$HERE/../art/hero.jpg" "$OUT/root/assets/port/hero.jpg"

# Everything is stored: the engine maps these out of the zip.
cp "$OUT/base.apk" "$OUT/unaligned.apk"
(cd "$OUT/root" && "$Z" a -tzip -mx=0 -bso0 -bsp0 ../unaligned.apk .)
"$BT/zipalign.exe" -f -p 4 "$(cygpath -w "$OUT/unaligned.apk")" "$(cygpath -w "$OUT/aligned.apk")"

KS="$W/princess-debug.keystore"
[ -f "$KS" ] || keytool -genkeypair -keystore "$(cygpath -w "$KS")" -storepass princessdebug -keypass princessdebug -alias princess \
    -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=princess-android" 2>/dev/null
cmd.exe //c "$(cygpath -w "$BT/apksigner.bat")" sign --ks "$(cygpath -w "$KS")" --ks-pass pass:princessdebug --key-pass pass:princessdebug \
    --v4-signing-enabled false --out "$(cygpath -w "$OUT/princess-android.apk")" "$(cygpath -w "$OUT/aligned.apk")"
ls -la "$OUT/princess-android.apk"
