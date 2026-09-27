#!/usr/bin/env bash
# Imports new art into Godot with mipmaps (needed for minified sprites), in two passes:
# the first creates default .import files, the second re-imports with mipmaps switched on.
set -euo pipefail
cd "$(dirname "$0")/../.."
export DOTNET_ROOT="$HOME/.dotnet"
godot --headless --path . --import >/dev/null 2>&1 || true
changed=0
for f in assets/art/**/*.png.import assets/art/*/*.png.import; do
  [ -f "$f" ] || continue
  if grep -q '^mipmaps/generate=false' "$f"; then
    sed -i 's|^mipmaps/generate=false|mipmaps/generate=true|' "$f"
    changed=1
  fi
done
if [ "$changed" = 1 ]; then
  godot --headless --path . --import >/dev/null 2>&1 || true
fi
if grep -L 'mipmaps/generate=true' assets/art/*/*.png.import 2>/dev/null | grep -q .; then
  echo "WARNING: some art lacks mipmaps"
else
  echo "art imported with mipmaps"
fi
