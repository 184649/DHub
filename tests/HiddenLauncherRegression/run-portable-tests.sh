#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SDK="${1:?Pass existing .NET 8 SDK root}"
OUT="${2:-$(mktemp -d)}"
mkdir -p "$OUT"
python3 - "$ROOT" "$SDK" "$OUT" <<'PY'
from pathlib import Path
import sys,json,xml.etree.ElementTree as ET
root,sdk,out=map(Path,sys.argv[1:]);out=out.resolve()
compiler=sorted((sdk/'sdk').glob('8.*/Roslyn/bincore/csc.dll'))[-1]
refs=sorted((sdk/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
sources=list(root.glob('*.cs'))+[(root/n.attrib['Include']).resolve() for n in ET.parse(root/'HiddenLauncherRegression.csproj').findall('.//Compile')]
args=['-nologo','-target:exe','-langversion:12','-nullable:enable','-warnaserror+','-out:'+str(out/'HiddenLauncherRegression.dll')]+['-r:'+str(p) for p in refs.glob('*.dll')]+[str(p) for p in sources]
(out/'compile.rsp').write_text('\n'.join('"'+a+'"' for a in args))
(out/'compiler.txt').write_text(str(compiler))
(out/'HiddenLauncherRegression.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
PY
"$SDK/dotnet" exec "$(cat "$OUT/compiler.txt")" @"$OUT/compile.rsp"
"$SDK/dotnet" "$OUT/HiddenLauncherRegression.dll" --logic-only
