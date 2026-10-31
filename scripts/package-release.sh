#!/usr/bin/env bash
# Publish a self-contained mdhub build and pack it into one archive.
#
#   bash scripts/package-release.sh --rid win-x64 --version 1.2.3
#   bash scripts/package-release.sh --rid osx-arm64 --version 1.2.3
#   bash scripts/package-release.sh --rid osx-x64 --version 1.2.3
#
# Windows archives contain mdhub-<rid>/Mdhub.exe. macOS archives contain an
# unsigned Mdhub.app bundle. Signing and notarization are intentionally not
# done here.
set -euo pipefail

rid=""
version=""
label=""
output="artifacts"
app_name="Mdhub"
bundle_id="hk.hong56.mdhub"
display_name="mdhub"

usage() {
  echo "usage: package-release.sh --rid win-x64|osx-arm64|osx-x64 --version major.minor.patch [--label text] [--output dir]" >&2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) rid="${2:-}"; shift 2 ;;
    --version) version="${2:-}"; shift 2 ;;
    --label) label="${2:-}"; shift 2 ;;
    --output) output="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

case "$rid" in
  win-x64|osx-arm64|osx-x64) ;;
  *) echo "unsupported rid: ${rid:-<missing>}" >&2; usage; exit 2 ;;
esac

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "version must be major.minor.patch, got: ${version:-<missing>}" >&2
  exit 2
fi

label="${label:-$version}"

if command -v python3 >/dev/null 2>&1; then
  python="python3"
elif command -v python >/dev/null 2>&1; then
  python="python"
else
  echo "python3 is required to pack the archive" >&2
  exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

publish_dir="$stage/publish"
echo "Publishing $rid (version $version, label $label)"
dotnet publish "$root/Mdhub.csproj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:UseAppHost=true \
  -p:Version="$version" \
  -p:InformationalVersion="$label" \
  -p:IncludeNativeLibrariesForSelfExtract=false \
  -o "$publish_dir"

archive="$output/mdhub-$rid.zip"
rm -f "$archive"

if [[ "$rid" == win-* ]]; then
  if [[ ! -f "$publish_dir/Mdhub.exe" || ! -f "$publish_dir/Mdhub.dll" ]]; then
    echo "publish output is missing Mdhub.exe or Mdhub.dll" >&2
    exit 1
  fi
  bundle="$stage/mdhub-$rid"
  mkdir -p "$bundle"
  cp -a "$publish_dir"/. "$bundle"/
  "$python" "$root/scripts/zip_unix.py" "$bundle" "$archive"
else
  app="$stage/Mdhub.app"
  macos="$app/Contents/MacOS"
  mkdir -p "$macos" "$app/Contents/Resources"
  cp -a "$publish_dir"/. "$macos"/
  host="$macos/$app_name"
  if [[ ! -f "$host" || ! -f "$macos/$app_name.dll" ]]; then
    echo "publish output is missing the macOS app host or $app_name.dll" >&2
    exit 1
  fi
  chmod +x "$host"
  cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$display_name</string>
  <key>CFBundleDisplayName</key>
  <string>$display_name</string>
  <key>CFBundleIdentifier</key>
  <string>$bundle_id</string>
  <key>CFBundleVersion</key>
  <string>$version</string>
  <key>CFBundleShortVersionString</key>
  <string>$version</string>
  <key>CFBundleExecutable</key>
  <string>$app_name</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF
  # ditto writes the zip Finder extracts as an app, including the executable bit.
  # zip_unix.py is the same layout for Linux and Windows hosts.
  if command -v ditto >/dev/null 2>&1; then
    ditto -c -k --keepParent "$app" "$archive"
  else
    "$python" "$root/scripts/zip_unix.py" "$app" "$archive"
  fi
fi

verify="$(mktemp -d)"
# ditto and Info-ZIP restore the executable bit. Python's zipfile extractor does not.
extractor="python"
if command -v ditto >/dev/null 2>&1; then
  ditto -x -k "$archive" "$verify"
  extractor="ditto"
elif command -v unzip >/dev/null 2>&1; then
  unzip -q "$archive" -d "$verify"
  extractor="unzip"
else
  "$python" -m zipfile -e "$archive" "$verify"
fi

if [[ "$rid" == win-* ]]; then
  test -f "$verify/mdhub-$rid/Mdhub.exe"
  test -f "$verify/mdhub-$rid/Mdhub.dll"
  if command -v file >/dev/null 2>&1; then
    exe_info="$(file "$verify/mdhub-$rid/Mdhub.exe")"
    case "$exe_info" in
      *PE32+*) ;;
      *) echo "expected a 64-bit Windows executable, got: $exe_info" >&2; exit 1 ;;
    esac
  fi
else
  test -f "$verify/Mdhub.app/Contents/Info.plist"
  test -f "$verify/Mdhub.app/Contents/MacOS/Mdhub"
  test -f "$verify/Mdhub.app/Contents/MacOS/Mdhub.dll"
  if [[ "$extractor" == "python" ]]; then
    host_mode="$("$python" -c 'import sys, zipfile; info = zipfile.ZipFile(sys.argv[1]).getinfo(sys.argv[2]); print((info.external_attr >> 16) & 0o777)' "$archive" "Mdhub.app/Contents/MacOS/Mdhub")"
    if (( (10#$host_mode & 73) == 0 )); then
      echo "app host is not marked executable in the zip (mode $host_mode)" >&2
      exit 1
    fi
  else
    test -x "$verify/Mdhub.app/Contents/MacOS/Mdhub"
  fi
  grep -q "$bundle_id" "$verify/Mdhub.app/Contents/Info.plist"
  grep -q "<string>$version</string>" "$verify/Mdhub.app/Contents/Info.plist"
  dylib=""
  while IFS= read -r candidate; do
    dylib="$candidate"
    break
  done < <(find "$verify/Mdhub.app/Contents/MacOS" -name '*.dylib' -print)
  if [[ -z "$dylib" ]]; then
    echo "macOS bundle is missing native libraries (*.dylib)" >&2
    exit 1
  fi
  if command -v file >/dev/null 2>&1; then
    host_info="$(file "$verify/Mdhub.app/Contents/MacOS/Mdhub")"
    case "$rid" in
      osx-arm64)
        case "$host_info" in
          *arm64*|*ARM64*) ;;
          *) echo "expected an arm64 Mach-O executable, got: $host_info" >&2; exit 1 ;;
        esac
        ;;
      osx-x64)
        case "$host_info" in
          *x86_64*) ;;
          *) echo "expected an x86_64 Mach-O executable, got: $host_info" >&2; exit 1 ;;
        esac
        ;;
    esac
  fi
fi
rm -rf "$verify"

echo "Wrote $archive ($(du -h "$archive" | awk '{print $1}'))"
