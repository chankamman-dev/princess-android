#!/usr/bin/env bash
# Compiles the port's own runtime code (tools/princessandroid) into PrincessAndroid.dll.
# References only the class library and the Android player's UnityEngine modules, so it can be built
# before staging.
set -euo pipefail

WORK=/e/android-recompile-work/princess-android-work
HERE="$(cd "$(dirname "$0")" && pwd)"
D=/e/android-recompile-work/princess-android-work/unity/Editor/Data
BCL=${BCL:-$D/MonoBleedingEdge/lib/mono/unityaot-linux}
AM=$D/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Managed
CSC=$(ls -d '/c/Program Files/dotnet/sdk/'8.*/Roslyn/bincore/csc.dll | tail -1)
OUT=$WORK/extra
mkdir -p "$OUT"
RSP="$OUT/princessandroid.rsp"
{
    echo '-nologo'
    echo '-noconfig'
    echo '-nostdlib+'
    echo '-target:library'
    echo '-optimize+'
    echo '-debug-'
    echo '-langversion:8.0'
    echo "-out:$(cygpath -w "$OUT/PrincessAndroid.dll")"
    for f in mscorlib.dll System.dll System.Core.dll Facades/netstandard.dll; do echo "-r:$(cygpath -w "$BCL/$f")"; done
    for f in UnityEngine.CoreModule.dll UnityEngine.AndroidJNIModule.dll UnityEngine.InputLegacyModule.dll; do echo "-r:$(cygpath -w "$AM/$f")"; done
    find "$HERE/princessandroid" -name '*.cs' -exec cygpath -w {} \; | sed 's/^/"/; s/$/"/'
} > "$RSP"
if dotnet "$(cygpath -w "$CSC")" "@$(cygpath -w "$RSP")" > "$OUT/csc.log" 2>&1; then :; else grep -E "error" "$OUT/csc.log" | cut -c1-200 | sort -u | head -20; exit 1; fi
ls -la "$OUT/PrincessAndroid.dll"
