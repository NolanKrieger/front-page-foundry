# Style bible — the engraved plates

Status: **APPROVED by Nolan 2026-09-23 ("for now")**. Generate the remaining plates with the locked templates below. He compared 28 simpler alternatives first and kept this look; that sheet, `scratch/style-lab/style-lab-all.png` (prompts in `scratch/style-lab/styles.tsv`), is the menu if he wants to change the style later. Keep the pipeline able to regenerate everything.

## The look

A 1920s newspaper steel engraving: pure black ink line work (crosshatch, hatching, stipple) on cream newsprint. Buildings are drawn in a *board-game-piece oblique*: the footprint is seen from above (roof foreshortened), the front wall straight on beneath it, side walls hidden, parallel lines kept parallel. Goods are bold woodcut icons that read at postage-stamp size. Terrain is sparse stipple and hatching straight onto the paper.

Contact sheet of the candidates: `scratch/art-probe/style-bible.png` (buildings, goods strip, ore tile).

## Generation (Codex `image_gen`, non-interactive)

```bash
codex exec --skip-git-repo-check -C scratch/art-probe "Use your built-in image generation tool (image_gen) to generate exactly one image, then copy the PNG to <abs path> (do not resize). Reply with only the saved path. Prompt: <PROMPT>"
```

### Building prompt template (locked)

> 1920s newspaper steel engraving of **<SUBJECT, one sentence: what it is, its silhouette features>**. Pure black ink lines only: crosshatch, hatching and stipple, no grey or coloured fill, white paper areas left empty; the only colour is the flat solid pure green background #00FF00 filling the entire background. Military oblique projection like a board-game piece: the square footprint is seen from directly above as a true square, the front wall is seen straight-on below the roof, side walls barely visible, parallel lines stay parallel, no vanishing points, no perspective, no ground, no cast shadow, no text, centred, filling the frame.

Do **not** say "architectural plan" or "cabinet oblique": that produced a 3/4 view with a side wall (mine-v2, smelter-v2) and a split plan-plus-elevation drawing (depot-v2).

### Goods icon template (locked)

> a single **<GOOD>** as a bold 1920s newspaper woodcut icon: **<shape notes>**, thick black outlines, a little hatching for shadow, designed to read clearly at postage-stamp size. Pure black ink only, no grey or coloured fill, white paper left empty inside the outline; the only colour is the flat solid pure green background #00FF00 filling the entire background. Seen straight on, centred, large, no text, no cast shadow.

### Terrain tile template (locked)

Ink on **white** (the green key failed for a texture: the model returned a black ground). Key by darkness instead (`--white`).

> a square seamless repeating pattern tile for a 1920s newspaper engraving of **<GROUND>**, seen from directly above: **<features>** drawn with black ink stipple, short hatching and small crosshatched shadows on pure white paper. Sparse and airy, most of the tile is blank white. Black ink only, no grey wash, no colour, no border, no text, no perspective, no shadow, the drawing fills the square evenly edge to edge so it tiles without a visible seam.

## Processing → sprites

`tools/art/process.py` (Pillow via `uv run --with pillow`): key out the green with Codex's `remove_chroma_key.py`, map pixels onto the ink↔paper axis (white walls become opaque paper so a building hides what stands behind it), scale so the drawing's width equals its footprint at **128 px per tile** (2× the 64 px design size; drawn at half size, mipmapped), trim. Goods → 96 px box. Terrain → 128 px square, ink-with-alpha. Then `tools/art/import.sh` imports into Godot with mipmaps. `tools/art/bible.sh` rebuilds all nine.

Engine: `game/Art.cs` loads a sprite if it exists, else the procedural ink drawing is used, so the game never depends on a plate being cut.

## Review rules

- Reject: side walls visible, perspective convergence, ground/shadow, text, grey fills that survive keying, height above the footprint over ~1.5 tiles for a 2×2.
- Keep a rejection note per batch in `docs/AI-ASSETS.md` so bad patterns aren't repeated.
