#!/usr/bin/env bash
# Cross-platform part of a release (CI on Linux/macOS): tests, self-contained win-x64 publish and the portable zip.
# The Windows installer needs Inno Setup – run build/publish.ps1 on Windows for that.
#   ./build/publish.sh [--framework] [--skip-tests]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
profile="win-x64"
run_tests=1
for arg in "$@"; do
  case "$arg" in
    --framework) profile="win-x64-framework" ;;
    --skip-tests) run_tests=0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n1)"
echo "GroceryPOS $version ($profile)"

if [ "$run_tests" = 1 ]; then
  dotnet test "$root/GroceryPOS.sln" -c Release --nologo
fi
dotnet publish "$root/src/GroceryPOS.App/GroceryPOS.App.csproj" "-p:PublishProfile=$profile" --nologo

if [ "$profile" = "win-x64" ]; then
  staging="$root/artifacts/portable/GroceryPOS-$version"
  rm -rf "$staging" && mkdir -p "$staging"
  cp -R "$root/artifacts/publish/win-x64/." "$staging/"
  printf '%s\r\n' \
    'GroceryPOS bản di động: dữ liệu (cơ sở dữ liệu, log, sao lưu) nằm trong thư mục "data" cạnh file GroceryPOS.exe.' \
    'Xóa file này để dùng thư mục %LocalAppData%\GroceryPOS như bản cài đặt.' > "$staging/portable.txt"
  zip_path="$root/artifacts/GroceryPOS-$version-portable.zip"
  rm -f "$zip_path"
  (cd "$root/artifacts/portable" && zip -qr "$zip_path" "GroceryPOS-$version")
  echo "$zip_path"
fi
