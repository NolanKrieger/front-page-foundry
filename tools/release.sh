#!/usr/bin/env bash
# Release candidate: tests → build → self-test → Linux and Windows exports → archives + checksums.
# Linux ships as tar.gz (keeps the executable bit); Windows as zip (python's zipfile; no zip binary here).
# Run outside any sandbox (the self-test needs the display, the export needs the network for NuGet).
#   bash tools/release.sh            # everything
#   bash tools/release.sh --no-test  # exports only
#   bash tools/release.sh --package  # archive the builds already in build/
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/1000}" WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}" DISPLAY="${DISPLAY:-:0}"
VERSION="$(cat VERSION)"

if [ "${1:-}" = "--package" ]; then
  cd build
  rm -f "front-page-foundry-$VERSION-linux-x86_64.tar.gz" "front-page-foundry-$VERSION-windows-x86_64.zip" SHA256SUMS
  tar -czf "front-page-foundry-$VERSION-linux-x86_64.tar.gz" -C linux .
  ( cd windows && python3 -m zipfile -c "../front-page-foundry-$VERSION-windows-x86_64.zip" . )
  sha256sum "front-page-foundry-$VERSION-linux-x86_64.tar.gz" "front-page-foundry-$VERSION-windows-x86_64.zip" > SHA256SUMS
  cat SHA256SUMS; ls -la front-page-foundry-$VERSION-*
  echo "release candidate $VERSION packaged in build/"
  exit 0
fi

if [ "${1:-}" != "--no-test" ]; then
  echo "== sim tests"; dotnet test tests/Sim.Tests 2>&1 | grep -E "Passed!|Failed!"
  echo "== game build"; dotnet build FrontPageFoundry.csproj 2>&1 | grep -E " error |Build succeeded"
  echo "== self-test"; godot --path . -- --selftest 2>&1 | grep -E "^SELFTEST" || { echo "self-test failed"; exit 1; }
fi

rm -rf build/linux build/windows
mkdir -p build/linux build/windows
echo "== export linux";   godot --headless --path . --export-release "Linux"   build/linux/front-page-foundry.x86_64 >/dev/null 2>&1
echo "== export windows"; godot --headless --path . --export-release "Windows" build/windows/front-page-foundry.exe  >/dev/null 2>&1
test -x build/linux/front-page-foundry.x86_64 && test -f build/windows/front-page-foundry.exe

echo "== exported self-test (linux)"
build/linux/front-page-foundry.x86_64 -- --selftest 2>&1 | grep -E "^SELFTEST" || { echo "exported self-test failed"; exit 1; }

exec bash "$0" --package
