#!/usr/bin/env bash
# Direct Roslyn invocation avoids restore/network and works in restricted Linux containers.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$ROOT/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
DOTNET="$(command -v "$DOTNET")"
SDK_ROOT="$(dirname "$(readlink -f "$DOTNET")")"
SDK_VERSION="$(ls "$SDK_ROOT/sdk" | sort -V | tail -1)"
REF_VERSION="$(ls "$SDK_ROOT/packs/Microsoft.NETCore.App.Ref" | sort -V | tail -1)"
FRAMEWORK="$(ls "$SDK_ROOT/packs/Microsoft.NETCore.App.Ref/$REF_VERSION/ref" | sort -V | tail -1)"
RUNTIME_VERSION="$(ls "$SDK_ROOT/shared/Microsoft.NETCore.App" | sort -V | tail -1)"
BUILD="${STORAGE_TEST_BUILD:-$ROOT/.build}"
mkdir -p "$BUILD"
python3 - "$REPO" "$ROOT" "$BUILD" "$SDK_ROOT" "$REF_VERSION" "$FRAMEWORK" "$RUNTIME_VERSION" <<'PY'
import json,sys
from pathlib import Path
repo,root,build,sdk=map(Path,sys.argv[1:5]); ref,framework,runtime=sys.argv[5:]
args=['-nologo','-target:exe','-nullable:enable','-warnaserror','-out:'+str(build/'StorageRegression.dll')]
args+=['-r:'+str(p) for p in sorted((sdk/'packs/Microsoft.NETCore.App.Ref'/ref/'ref'/framework).glob('*.dll'))]
args += [str(p) for p in sorted(root.glob('*.cs'))]
args += [str(repo/'src/ShortcutList/Services'/p) for p in ['ShortcutStore.cs','SampleBookmarks.cs','StorageFile.cs','StorageIntegrity.cs','StorageSnapshotDiff.cs','ShortcutDetector.cs','ShortcutRunner.cs']]
args += [str(repo/'src/ShortcutList/Models'/p) for p in ['AppData.cs','AppSettings.cs','ShortcutItem.cs','WorkspaceItem.cs','CommandItem.cs','OperationLogItem.cs','ShortcutType.cs','BookmarkSource.cs']]
(build/'compile.rsp').write_text('\n'.join('"'+arg+'"' for arg in args)+'\n')
(build/'StorageRegression.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':framework,'framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
PY
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1
"$DOTNET" exec "$SDK_ROOT/sdk/$SDK_VERSION/Roslyn/bincore/csc.dll" @"$BUILD/compile.rsp"
"$DOTNET" "$BUILD/StorageRegression.dll"
