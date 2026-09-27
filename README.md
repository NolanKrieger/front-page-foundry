# Front Page Foundry

A 1920s factory game drawn as the town newspaper. Godot 4.7 (.NET / C#). Design: [`docs/GDD.md`](docs/GDD.md).

## Run it

- **Play:** app menu → *Front Page Foundry*, or `godot --path ~/front-page-foundry`
- **Edit in Godot:** app menu → *Godot Engine*, then open this folder; or `godot -e --path ~/front-page-foundry`
- **Demo factory:** `godot --path ~/front-page-foundry -- --demo`

Controls: `Tab` opens the Courier (front page, classifieds, market, telegrams, archive; Tab/Esc closes) · `Shift+Tab` turns the quick strip's column · `1`–`9` pick an advertisement in it (or click one) · `R` / `Shift+R` turn the held piece, or the hovered building ·
click to build, drag to lay belt, pipe, road or rail · trestle: click the entry, then the far end · vehicles (Haulage column): click one depot, station or landing, then the other, and a truck, locomotive or barge runs between them (a vehicle parked there is used before one is bought) · rail signal: click it onto rail · `Q` copies the hovered building or vehicle, or a good on a belt
(then click a sorting splitter to set its filter, or a receiving dock to order it) · right-click drops what you hold, or demolishes (drag to clear a path), or scraps the vehicle under the cursor ·
the Exposition Yard (Extraction column) takes any good at market price and counts it toward the open commission; hover it for the bill · `P` planning mode (every tool pencils in a free plan; plans build themselves, oldest first, from cash on hand, once the pencil is down) · `B` blueprint mode (drag over a layout to copy it, `R` turns, `1`–`9` saves, `B` then a key loads, click stamps it as plans) · `F` flow overlay ·
`Ctrl+Z` / `Ctrl+Y` undo and redo · `Esc` empties the hand, or with an empty hand opens the **front office** (the only pause: continue, found a new company, the companies on the books, settings, close up for the day) · WASD / arrows / middle-drag pan · wheel zoom.

Companies save themselves every in-game day and when you close up, one file per company under `user://companies/<name>/` (`~/.local/share/godot/app_userdata/Front Page Foundry/companies/` here) with two rolling backups that only ever serve to recover a damaged file. Settings live in `user://settings.json`. `--office` opens the front office at start; `--office=new` on the founding page.

## Test it

```bash
dotnet test tests/Sim.Tests                # simulation rules (headless), incl. replay determinism and perf budgets
godot --path . -- --selftest               # real input path, 185 checks: keys, drags, ads, trestle, pipette, undo, zoom, power, haulage, plans, blueprints, overlay, front office, fold (goes full screen; sandboxed user://selftest/)
godot --path . -- --playtest=/tmp/pt       # a scripted new player from the first run to the fold, 20 screenshots into /tmp/pt (sandboxed user://playtest/)
godot --path . --fullscreen -- --demo --overlay --screenshot=/tmp/flow.png --frames=200   # the flow overlay over the demo
godot --path . --fullscreen -- --demo --zoom=1.0 --screenshot=/tmp/shot.png --frames=120   # 1080p render check
godot --path . --fullscreen -- --demo --fastforward=1500 --paper=market --screenshot=/tmp/paper.png   # a paper page after 12 days
godot --path . -- --bench --frames=600     # 40 loops ≈ 10k moving goods; prints measured UPS, ms/tick, FPS (--bench=200 for 50k)
godot --path . --fullscreen -- --panbench --zoom=0.4 --frames=600   # pans over fresh ground at WASD speed; prints FPS and chunks (--panspeed=0 to stand still, --at=x,y to start somewhere)
```

Run `godot` and `dotnet` outside the sandbox; screenshots and the self-test need the real display. `--mute` silences a run (the self-test and screenshots are always muted). Rebuild the soundscape with `uv run --with numpy python3 tools/audio/build.py` then `godot --headless --path . --import`.

## Ship it

`bash tools/release.sh` runs the sim tests, the game build, the self-test, exports Linux and Windows release builds from `export_presets.cfg` (Godot 4.7.2 mono export templates are installed under `~/.local/share/godot/export_templates/`), runs the self-test through the exported Linux binary, and leaves a Linux `.tar.gz`, a Windows `.zip` and `SHA256SUMS` in `build/`. Version: `VERSION` and `project.godot` (`config/version`). The string table is exported as a raw file (`assets/text/en.csv.import` says `importer="keep"`; the default CSV importer would turn it into a Translation and drop the file). Store page, AI disclosure, cloud and achievement wiring: `docs/STEAM.md`; trailer plan: `docs/TRAILER.md`.

## Layout

| Path | What |
|---|---|
| `src/Sim/` | The whole game state and rules in plain C#. No Godot types, so it is deterministic and testable. `World` (grid, commands, undo, ticking), `Belts` (tiles + transport lines), `Splitter`, `Machine` (recipes, ports, fireboxes), `Mine`, `Depot`, `Power` (shaft/steam/electric networks), `Haulage` (roads, rail, river; terminals, vehicles, block signals), `Catalog`/`Items`/`Recipes` (content tables), `Commands`. |
| `tests/Sim.Tests/` | xUnit tests for the sim. |
| `game/` | Godot layer: `Main` (input → sim commands, bench), `EntityView` (belts, goods, buildings), `TerrainLayer` (chunked ground), `Hud` (the newspaper), `Ink` (palette and engraving strokes), `Text` (string table). |
| `assets/text/en.csv` | Every player-facing string. |
| `assets/` | Fonts (all SIL OFL, licences beside them), the paper shader, and `audio/` (all generated by `tools/audio/build.py`; see its README). |
| `docs/GDD.md` | Game design document, roadmap M0–M15 and decisions log. |

The sim runs at 60 ticks/s in `_PhysicsProcess`. Rendering reads sim state and never changes it.

## Toolchain

Godot 4.7.2 .NET at `~/.local/opt/godot-4.7.2-mono` (launcher `~/.local/bin/godot` sets `DOTNET_ROOT`), .NET 8 SDK at `~/.dotnet`.
