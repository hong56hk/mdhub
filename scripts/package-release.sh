#!/usr/bin/env bash
# Publish a self-contained mdhub build.
#
#   bash scripts/package-release.sh --rid win-x64 --version 1.2.3
#   bash scripts/package-release.sh --rid osx-arm64 --version 1.2.3
#   bash scripts/package-release.sh --rid osx-x64 --version 1.2.3
#
# Windows writes one executable, Mdhub-<version>-win-x64.exe (PublishSingleFile
# and IncludeNativeLibrariesForSelfExtract). macOS writes one disk image per
# architecture, Mdhub-<version>-osx-*.dmg, containing an unsigned Mdhub.app.
# IncludeNativeLibrariesForSelfExtract is left off on macOS: it breaks CoreCLR.
# Signing and notarization are intentionally not done here.
set -euo pipefail

rid=""
version=""
label=""
output="artifacts"
app_name="Mdhub"
bundle_id="hk.hong56.mdhub"
display_name="mdhub"
mount_point=""

die() { echo "$*" >&2; exit 1; }

usage() {
  echo "usage: package-release.sh --rid win-x64|osx-arm64|osx-x64 --version major.minor.patch [--label text] [--output dir]" >&2
}

detach_dmg() {
  if [[ -n "$mount_point" ]]; then
    hdiutil detach "$mount_point" >/dev/null 2>&1 || hdiutil detach -force "$mount_point" >/dev/null 2>&1 || true
    mount_point=""
  fi
}

cleanup() {
  detach_dmg
  if [[ -n "${stage:-}" ]]; then rm -rf "$stage"; fi
}
trap cleanup EXIT

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
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
stage="$(mktemp -d)"
publish_dir="$stage/publish"

publish_args=(
  -c Release
  -r "$rid"
  --self-contained true
  -p:UseAppHost=true
  -p:Version="$version"
  -p:InformationalVersion="$label"
  -p:DebugSymbols=false
  -p:DebugType=none
  -o "$publish_dir"
)
if [[ "$rid" == win-* ]]; then
  # One exe: managed assemblies, the runtime, and native libraries are inside it.
  # Content files are embedded as manifest resources (see Mdhub.csproj), not
  # extracted beside the exe by IncludeAllContentForSelfExtract.
  publish_args+=(
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    -p:IncludeAllContentForSelfExtract=false
    -p:EnableCompressionInSingleFile=false
  )
else
  publish_args+=(
    -p:PublishSingleFile=false
    -p:IncludeNativeLibrariesForSelfExtract=false
  )
fi

echo "Publishing $rid (version $version, label $label)"
dotnet publish "$root/Mdhub.csproj" "${publish_args[@]}"

if [[ "$rid" == win-* ]]; then
  python=""
  if command -v python3 >/dev/null 2>&1; then
    python="python3"
  elif command -v python >/dev/null 2>&1; then
    python="python"
  else
    die "python3 is required to verify the single-file exe"
  fi
  test -f "$publish_dir/Mdhub.exe" || die "publish output is missing Mdhub.exe"
  if [[ -e "$publish_dir/Mdhub.dll" ]]; then
    die "PublishSingleFile left Mdhub.dll beside the exe"
  fi
  loose="$(find "$publish_dir" \( -iname 'coreclr.dll' -o -iname 'libSkiaSharp.dll' -o -iname 'libHarfBuzzSharp.dll' \) -print)"
  if [[ -n "$loose" ]]; then
    die "native libraries were published beside the exe: $loose"
  fi
  asset="$output/Mdhub-$version-$rid.exe"
  rm -f "$asset"
  cp -f "$publish_dir/Mdhub.exe" "$asset"
  "$python" "$root/scripts/verify-single-file.py" "$asset"
else
  command -v hdiutil >/dev/null 2>&1 || die "hdiutil is required to build the macOS dmg (run this rid on macOS)"
  command -v file >/dev/null 2>&1 || die "file(1) is required to verify the macOS app"
  app="$stage/Mdhub.app"
  macos="$app/Contents/MacOS"
  mkdir -p "$macos" "$app/Contents/Resources"
  cp -a "$publish_dir"/. "$macos"/
  find "$macos" -name '*.pdb' -delete
  rm -f "$macos/$app_name.runtimeconfig.dev.json"
  host="$macos/$app_name"
  test -f "$host" || die "publish output is missing the macOS app host"
  test -f "$macos/$app_name.dll" || die "publish output is missing $app_name.dll"
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
  # Build a read-write image, copy the app in, then add the Applications
  # symlink on the volume. hdiutil -srcfolder follows symlinks, so the link
  # cannot be created until the image is mounted.
  command -v ditto >/dev/null 2>&1 || die "ditto is required to copy Mdhub.app into the dmg"
  dmg_root="$stage/dmg-root"
  mkdir -p "$dmg_root"
  mv "$app" "$dmg_root/Mdhub.app"
  app_kb="$(du -sk "$dmg_root/Mdhub.app" | awk '{print $1}')"
  size_mb=$((app_kb / 1024 + 128))
  if [[ "$size_mb" -lt 200 ]]; then size_mb=200; fi
  rw="$stage/mdhub-rw.dmg"
  rm -f "$rw"
  if ! hdiutil create -size "${size_mb}m" -fs APFS -volname "MDHub" -format UDRW -ov "$rw"; then
    hdiutil create -size "${size_mb}m" -fs HFS+ -volname "MDHub" -format UDRW -ov "$rw"
  fi
  rw_attach="$(hdiutil attach -nobrowse -readwrite "$rw")"
  mount_point="$(printf '%s\n' "$rw_attach" | sed -n 's|^.*\(/Volumes/.*\)|\1|p' | tail -n 1)"
  if [[ -z "$mount_point" || ! -d "$mount_point" ]]; then
    printf '%s\n' "$rw_attach" >&2
    die "failed to mount the read-write disk image"
  fi
  ditto "$dmg_root/Mdhub.app" "$mount_point/Mdhub.app"
  ln -s /Applications "$mount_point/Applications"
  # Flush before detach so the convert step sees the app.
  sync || true
  detach_dmg
  asset="$output/Mdhub-$version-$rid.dmg"
  rm -f "$asset" "${asset}.dmg"
  hdiutil convert "$rw" -format UDZO -ov -o "${asset%.dmg}"
  if [[ ! -f "$asset" && -f "${asset}.dmg" ]]; then
    mv "${asset}.dmg" "$asset"
  fi
  test -f "$asset" || die "hdiutil convert did not write $asset"
  sig="$(tail -c 512 "$asset" | head -c 4)"
  test "$sig" = "koly" || die "dmg is missing the UDIF koly trailer"
  attach_out="$(hdiutil attach -nobrowse -readonly "$asset")"
  mount_point="$(printf '%s\n' "$attach_out" | sed -n 's|^.*\(/Volumes/.*\)|\1|p' | tail -n 1)"
  if [[ -z "$mount_point" || ! -d "$mount_point/Mdhub.app" ]]; then
    printf '%s\n' "$attach_out" >&2
    die "failed to mount $asset"
  fi
  test -d "$mount_point/Mdhub.app"
  test -L "$mount_point/Applications"
  test -f "$mount_point/Mdhub.app/Contents/Info.plist"
  test -x "$mount_point/Mdhub.app/Contents/MacOS/Mdhub"
  test -f "$mount_point/Mdhub.app/Contents/MacOS/Mdhub.dll"
  grep -q "$bundle_id" "$mount_point/Mdhub.app/Contents/Info.plist"
  grep -q "<string>$version</string>" "$mount_point/Mdhub.app/Contents/Info.plist"
  dylib="$(find "$mount_point/Mdhub.app/Contents/MacOS" -name '*.dylib' -print | head -n 1)"
  test -n "$dylib" || die "macOS bundle is missing native libraries (*.dylib)"
  host_info="$(file "$mount_point/Mdhub.app/Contents/MacOS/Mdhub")"
  case "$rid" in
    osx-arm64)
      case "$host_info" in
        *arm64*|*ARM64*) ;;
        *) die "expected an arm64 Mach-O executable, got: $host_info" ;;
      esac
      ;;
    osx-x64)
      case "$host_info" in
        *x86_64*) ;;
        *) die "expected an x86_64 Mach-O executable, got: $host_info" ;;
      esac
      ;;
  esac
  detach_dmg
  echo "DMG_OK $asset contains Mdhub.app ($host_info)"
fi

bytes="$(wc -c < "$asset" | tr -d ' ')"
echo "ASSET $asset bytes=$bytes"
echo "Wrote $asset ($bytes bytes)"
