#!/usr/bin/env bash
# Assembles LincleLINK.app from a `dotnet publish` output directory.
#
# Usage: make-app-bundle.sh <publish-dir> <output-dir>
# Example:
#   dotnet publish src/LincleLINK.App -c Release -r osx-arm64 --self-contained \
#     -o artifacts/publish/osx-arm64 -p:PublishSingleFile=true
#   packaging/macos/make-app-bundle.sh artifacts/publish/osx-arm64 artifacts/bundle
set -euo pipefail

publish_dir=$1
output_dir=$2
script_dir=$(cd "$(dirname "$0")" && pwd)
repo_root=$(cd "$script_dir/../.." && pwd)

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repo_root/Directory.Build.props")
if [[ -z "$version" ]]; then
    echo "error: could not read <Version> from Directory.Build.props" >&2
    exit 1
fi

app="$output_dir/LincleLINK.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$publish_dir/." "$app/Contents/MacOS/"
cp "$script_dir/LL_logo.icns" "$app/Contents/Resources/"
sed "s/APP_VERSION/$version/g" "$script_dir/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/LincleLINK"

# Re-sign the assembled bundle: the executable carries the .NET linker's ad-hoc
# signature for a bare binary, which is malformed as an app-bundle signature
# (no resource seal — Gatekeeper reports the app as "damaged").
codesign --force --deep --sign - "$app"
codesign --verify --strict "$app"

echo "created $app (version $version)"
