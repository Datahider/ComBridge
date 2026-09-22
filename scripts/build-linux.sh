#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_root"

dotnet test ComBridge.slnx -c Release
dotnet publish src/ComBridge/ComBridge.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o dist/win-x64

test -f dist/win-x64/ComBridge.exe
zip -9 -j dist/ComBridge-win-x64.zip dist/win-x64/ComBridge.exe
test -f dist/ComBridge-win-x64.zip
