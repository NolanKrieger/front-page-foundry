#!/usr/bin/env bash
# Rebuilds the style-bible sprites from the approved generations in scratch/art-probe.
set -euo pipefail
cd "$(dirname "$0")/../.."
export PATH="$HOME/.local/bin:$PATH"
P() { uv run --with pillow python tools/art/process.py "$@"; }
P scratch/art-probe/mine-v1.png      assets/art/buildings/mine_head.png     --tiles 2
P scratch/art-probe/smelter-v1.png   assets/art/buildings/smelter.png       --tiles 2
P scratch/art-probe/depot-v1.png     assets/art/buildings/freight_depot.png --tiles 2
P scratch/art-probe/icon-iron-ore.png    assets/art/goods/iron_ore.png    --kind good
P scratch/art-probe/icon-coal.png        assets/art/goods/coal.png        --kind good
P scratch/art-probe/icon-iron-ingot.png  assets/art/goods/iron_ingot.png  --kind good
P scratch/art-probe/icon-steel-plate.png assets/art/goods/steel_plate.png --kind good
P scratch/art-probe/icon-gear.png        assets/art/goods/gears.png       --kind good
P scratch/art-probe/ore-tile-white-2.png assets/art/terrain/iron_ore.png  --kind terrain --white
