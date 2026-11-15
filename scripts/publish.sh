#!/bin/bash
# ExcelToSqlite 배포 파일 만들기 (.NET 10 SDK 필요)
#   bash scripts/publish.sh             맥 앱   → dist/ExcelToSqlite.app, dist/ExcelToSqlite-mac.zip (이 맥의 칩에 맞게)
#   bash scripts/publish.sh win         윈도    → dist/win/ExcelToSqlite.exe (한 파일, 맥에서도 만들 수 있음)
#   bash scripts/publish.sh cli [RID]   명령줄  → dist/cli-<RID>/xl2sqlite (한 파일, RID를 안 주면 이 컴퓨터용)
#   bash scripts/publish.sh all         맥 앱 + 윈도 exe
# 런타임까지 넣어서(self-contained) 받는 컴퓨터에 .NET이 없어도 열린다.
# 맥 칩을 바꾸려면 MAC_RID=osx-x64 (Intel 맥용).
#
# 서명: 맥은 임시(ad-hoc) 서명만, 윈도는 서명하지 않는다.
#       받은 사람이 처음 열 때 거치는 단계는 README "처음 열 때" 참고.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist"
APP="$ROOT/src/ExcelToSqlite.Desktop/ExcelToSqlite.Desktop.csproj"
CLI="$ROOT/src/ExcelToSqlite.Cli/ExcelToSqlite.Cli.csproj"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
WHAT="${1:-mac}"

host_rid() {
  case "$(uname -s)" in
    Darwin) [ "$(uname -m)" = "x86_64" ] && echo osx-x64 || echo osx-arm64 ;;
    MINGW*|MSYS*|CYGWIN*) echo win-x64 ;;
    *) [ "$(uname -m)" = "aarch64" ] && echo linux-arm64 || echo linux-x64 ;;
  esac
}

pub() {   # pub <csproj> <RID> <폴더> — 단독 실행 파일 여부는 csproj가 RID를 보고 정한다
  rm -rf "${3:?}"
  dotnet publish "$1" -c Release -r "$2" -o "$3" -nologo
}

make_mac() {
  [ "$(uname -s)" = "Darwin" ] || { echo "맥 앱은 맥에서만 만들 수 있습니다 (codesign·ditto)." >&2; exit 1; }
  local rid="${MAC_RID:-$(host_rid)}"
  local app="$OUT/ExcelToSqlite.app" zip="$OUT/ExcelToSqlite-mac.zip" tmp="$OUT/.mac-$rid"

  # .app은 원래 폴더라 한 파일로 묶지 않고 Contents/MacOS에 그대로 둔다 (풀어 쓰는 단계가 없어 빨리 열림)
  pub "$APP" "$rid" "$tmp"
  rm -rf "$app" "$zip"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$tmp/." "$app/Contents/MacOS/"
  rm -rf "$tmp"

  local icon=""
  if [ -f "$ROOT/src/ExcelToSqlite.Desktop/Assets/AppIcon.icns" ]; then
    cp "$ROOT/src/ExcelToSqlite.Desktop/Assets/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"
    icon="<key>CFBundleIconFile</key><string>AppIcon</string>"
  fi
  # 오픈소스 고지 (코드가 아닌 파일은 Resources에, 서명 전에)
  cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$app/Contents/Resources/"

  # CFBundleShortVersionString 은 숫자만 받는다 (1.0.0-beta → 1.0.0). 최소 macOS 12.0 = .NET 10 런타임의 하한.
  # 번들 ID는 조직 주소(inscompile.github.io)를 거꾸로 — 한 번 내면 바꾸지 않는다
  #   (맥이 설정·권한을 이 ID로 기억한다)
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ExcelToSqlite</string>
  <key>CFBundleDisplayName</key><string>ExcelToSqlite</string>
  <key>CFBundleIdentifier</key><string>io.github.inscompile.exceltosqlite</string>
  <key>CFBundleVersion</key><string>${VERSION%%-*}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION%%-*}</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>ExcelToSqlite</string>
  ${icon}
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
</dict>
</plist>
PLIST
  plutil -lint "$app/Contents/Info.plist" >/dev/null

  # 서명 없이는 Apple 칩 맥에서 열리지 않으므로 임시(ad-hoc) 서명을 한다
  codesign --force --deep --sign - "$app" >/dev/null
  ditto -c -k --keepParent "$app" "$zip"
  echo "맥 앱 ($rid, $VERSION): $zip"
}

make_win() {
  # 한 파일 exe: 네이티브 라이브러리(그림·글꼴·SQLite)까지 넣어 두고 처음 열 때 풀어 쓴다
  pub "$APP" win-x64 "$OUT/win"
  echo "윈도 ($VERSION): $OUT/win/ExcelToSqlite.exe"
}

make_cli() {
  local rid="${1:-$(host_rid)}"
  pub "$CLI" "$rid" "$OUT/cli-$rid"
  echo "명령줄 ($rid, $VERSION): $OUT/cli-$rid/"
}

case "$WHAT" in
  mac) make_mac ;;
  win) make_win ;;
  cli) make_cli "${2:-}" ;;
  all) make_mac; make_win ;;
  *) echo "사용법: bash scripts/publish.sh [mac|win|cli [RID]|all]"; exit 2 ;;
esac
