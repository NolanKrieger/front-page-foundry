# Trailer — storyboard and capture (M15)

Sixty seconds, no voice-over, the gramophone rag as the bed (`assets/audio/rag_courier_piano.ogg` with the banjo and cornet stems fading in at the midpoint). Every shot is the game as it runs; nothing staged outside it. Title cards are typeset like the paper (IM Fell, cream, black rule).

| # | Seconds | Shot | How to get it |
|---|---|---|---|
| 1 | 0–4 | The masthead alone on cream paper; the press thunks; the first headline slides in. | `--demo --paper=front --zoom=1` still, then cut to the deck |
| 2 | 4–12 | Zoom 1.0 on the starter mine and belt; ore rolls into the depot; the cash counter ticks. | `--demo --at=8,-4 --zoom=1.0`, pan slowly right |
| 3 | 12–20 | Pull back to 0.5: smelters, presses, the coke oven and open hearth; goods become flow lines. | `--demo --at=14,0`, wheel-zoom out over 8 s |
| 4 | 20–28 | The Courier opens on the classifieds; a card is ringed in red pencil; the market page's sparklines. | Tab, click Classifieds, then Market |
| 5 | 28–36 | Haulage: the truck on the road, the train passing the signal, the barge on the river. | `--demo --at=-16,-1 --zoom=0.5`; then `--at` on the quay |
| 6 | 36–44 | Planning: pencil ghosts appear along a drag, P, and they build themselves one a tick; the flow overlay flicks on with F. | `--demo`, P, drag belts, P, F |
| 7 | 44–52 | Power: the waterwheel turning, steam mains, the dam and its lake. | `--demo` river-side works, `--at=<bank>` |
| 8 | 52–58 | The first automobile leaves the line; the FIRST MOTORCAR front page; the Exposition yard. | a company with the chain built (`--fastforward` on a saved company) |
| 9 | 58–60 | Card: "FRONT PAGE FOUNDRY — Read all about it." Steam logo. Wishlist. | title card |

## Capture recipe (this desk)

The game runs full screen at 1920×1080. Record the screen with PipeWire/ffmpeg while it plays:

```bash
godot --path . -- --demo --at=8,-4 --zoom=1.0 &            # or the exported binary
ffmpeg -f x11grab -framerate 60 -video_size 1920x1080 -i :0.0 -t 12 -c:v libx264 -crf 18 -pix_fmt yuv420p scratch/trailer/shot2.mp4
```

Wayland session: use `wf-recorder -g "0,0 1920x1080" -f scratch/trailer/shot2.mp4` or OBS if x11grab shows black. Stills for the store page: `--screenshot=<png> --frames=200` (goes full screen, muted). Assemble with `system/scripts/video-edit.sh join` or Kdenlive; music from `assets/audio/`.

## Stills already on file (1920×1080, `docs/review/`)

`2026-09-23-m2-factory-1080p.png` (the first works), `2026-09-23-m7-power.png` (river-side works), `2026-09-23-m9-haulage.png` (road, rail, signal, stations), `2026-09-23-m10-overlay-plans.png` (flow overlay and pencil plans), `2026-09-23-m13-front-office.png` (the menu). Retake them after the plate batch so every building shows its engraving.
