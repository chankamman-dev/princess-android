#!/usr/bin/env bash
# Port code -> staged assemblies -> libil2cpp.so -> APK.
#   ABI=x86_64   emulator build that runs natively (fast); output in $W/apk-x64
#   EMU=1        arm64 build for the emulator's translator (SEVL/WFE patched out); output in $W/apk-emu
#   INSTALL=<serial>  install the result afterwards
set -euo pipefail

T="$(cd "$(dirname "$0")" && pwd)"
W=/e/android-recompile-work/princess-android-work
D=$W/unity/Editor/Data
[ -f "$T/local.env" ] && . "$T/local.env"   # machine paths, see local.env.example
export NDK="${NDK:?set NDK in tools/local.env}"
PTOOL=${PTOOL:-/e/android-recompile-work/princess-android-work/ptool-bin3/ptool.exe}

(cd "$T/ptool" && dotnet build -c Release -o "$(dirname "$PTOOL")" 2>&1 | grep -E " error |个错误")
bash "$T/build-princessandroid.sh" | tail -1
"$PTOOL" stage 'H:\SteamLibrary\steamapps\common\Magical Princess\MagicalPrincess_Data\Managed' \
    "$(cygpath -w "$D/MonoBleedingEdge/lib/mono/unityaot-linux")" \
    "$(cygpath -w "$D/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Managed")" \
    'E:\android-recompile-work\princess-android-work\stage' 'E:\android-recompile-work\princess-android-work\extra'
bash "$T/build-il2cpp.sh"
if [ "${ABI:-}" = x86_64 ]; then export OUT=$W/apk-x64; elif [ "${EMU:-}" = 1 ]; then export OUT=$W/apk-emu; else export OUT=$W/apk; fi
bash "$T/build-apk.sh" 2>&1 | grep -vE 'Expected stack map|^Warning in|^ע|^注' | tail -2
if [ -n "${INSTALL:-}" ]; then
    MSYS_NO_PATHCONV=1 /g/AndroidLab/sdk/platform-tools/adb.exe -s "$INSTALL" install -r -g "$(cygpath -w "$OUT/princess-android.apk")" | tail -1
fi
