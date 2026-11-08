#!/usr/bin/env bash
# Staged IL -> C++ -> libil2cpp.so. Unchanged translation units come out of the cache.
# Everything lives on F: (NVMe); E: is a SATA SSD that stalls under sustained writes.
#   ABI=arm64-v8a (default, the device build) | x86_64 (emulator test build: runs without translation)
set -uo pipefail

ABI=${ABI:-arm64-v8a}
case "$ABI" in
    arm64-v8a) ARCH=ARM64; BL=release_android_aarch64; DIR=il2cpp ;;
    x86_64)    ARCH=x64;   BL=release_android_x86_64;  DIR=il2cpp-x64 ;;
    *) echo "unknown ABI $ABI"; exit 2 ;;
esac
W='E:\android-recompile-work\princess-android-work'
U="$W\\unity\\Editor\\Data"
NDK=${NDK:-"$U\\PlaybackEngines\\AndroidPlayer\\NDK"}
IL2CPP_EXE=${IL2CPP_EXE:-"$U\\il2cpp\\build\\deploy\\il2cpp.exe"}
# il2cpp links baselib from its own tree; the Android module ships it with the player libs.
BLD=/e/android-recompile-work/princess-android-work/unity/Editor/Data/il2cpp/build/baselib/$BL
[ -f "$BLD/baselib.a" ] || { mkdir -p "$BLD"; cp "/e/android-recompile-work/princess-android-work/unity/Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/StaticLibs/$ABI/baselib.a" "$BLD/"; }
mkdir -p /e/android-recompile-work/princess-android-work/$DIR
cd /e/android-recompile-work/princess-android-work/$DIR
export ANDROID_NDK_ROOT="$NDK"
"$IL2CPP_EXE" --convert-to-cpp --compile-cpp --libil2cpp-static \
    --platform=Android --architecture=$ARCH --configuration=Release --dotnetprofile=unityaot-linux \
    --emit-null-checks --enable-array-bounds-check --incremental-g-c-time-slice=3 \
    --directory="$W\\stage" \
    --generatedcppdir="$W\\$DIR\\cpp" \
    --data-folder="$W\\$DIR\\data" \
    --cachedirectory="$W\\$DIR\\cache" \
    --outputpath="$W\\$DIR\\out\\libil2cpp.so" \
    --tool-chain-path="$NDK" \
    ${IL2CPP_EXTRA:-} \
    --jobs=14 > build.log 2>&1
rc=$?
echo "il2cpp ($ABI) exit $rc"
grep -nE ' error |Error:|undefined reference' build.log | cut -c1-220 | head -12
# A failed run leaves the previous libil2cpp.so behind; packaging it with new metadata would crash.
[ $rc -eq 0 ] || exit $rc
ls -la out/libil2cpp.so
