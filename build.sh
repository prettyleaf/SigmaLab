#!/usr/bin/env bash
# Сборка ВидеоМетра: тесты ядра → VideoMeter.exe → портативный архив → установщик (Inno Setup).
# Требуется .NET SDK 8+ (собирает под .NET Framework 3.5 через Microsoft.NETFramework.ReferenceAssemblies).
# Установщик собирается ISCC.exe: на Windows — из PATH/Program Files, на Linux — через wine (WINEPREFIX с Inno Setup 6).
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(pwd)"
# первый найденный dotnet, у которого установлен SDK
for c in "${DOTNET:-}" "$(command -v dotnet || true)" "$HOME/.dotnet/dotnet"; do
  if [ -n "$c" ] && [ -x "$c" ] && [ -n "$("$c" --list-sdks 2>/dev/null)" ]; then DOTNET="$c"; break; fi
done
[ -n "${DOTNET:-}" ] || { echo "Не найден .NET SDK (8 или новее)"; exit 1; }
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/VideoMeter.csproj)"
DIST="$ROOT/../dist"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== Тесты ядра"
"$DOTNET" run --project tests/CoreTests.csproj -c Release

echo "== Сборка VideoMeter.exe $VERSION"
"$DOTNET" build src/VideoMeter.csproj -c Release -nologo -v q
BIN="$ROOT/src/bin/Release/net35"

echo "== Портативная версия"
PORT="$DIST/VideoMeter-$VERSION-portable"
rm -rf "$PORT" && mkdir -p "$PORT"
cp "$BIN/VideoMeter.exe" "$BIN/VideoMeter.exe.config" "$PORT/"
cp "$ROOT/installer/ПРОЧТИ.txt" "$PORT/"
cp "$ROOT/src/Resources/license.txt" "$PORT/ЛИЦЕНЗИЯ.txt"
(cd "$DIST" && rm -f "VideoMeter-$VERSION-portable.zip" && python3 -c "
import zipfile, os, sys
name = 'VideoMeter-$VERSION-portable'
with zipfile.ZipFile(name + '.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for f in sorted(os.listdir(name)):
        z.write(os.path.join(name, f), os.path.join(name, f))
")

echo "== Установщик"
if command -v ISCC.exe >/dev/null 2>&1; then
  ISCC.exe "/DAppVersion=$VERSION" installer/VideoMeter.iss
else
  export WINEPREFIX="${WINEPREFIX:-$HOME/.cache/sl-wine}" WINEDEBUG=-all
  ISCC="$WINEPREFIX/drive_c/InnoSetup/ISCC.exe"
  [ -f "$ISCC" ] || { echo "Не найден Inno Setup ($ISCC) — установщик не собран"; exit 0; }
  (cd installer && wine "$ISCC" /Q "/DAppVersion=$VERSION" VideoMeter.iss)
fi
ls -la "$DIST"
