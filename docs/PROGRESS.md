# Progress

Living status for Front Page Foundry. A new session must be able to resume from this file alone.
Roadmap and exit criteria: `docs/GDD.md` §15. Decisions: GDD §16.

## Status by milestone

| M | Milestone | State |
|---|---|---|
| M0 | Scaffold | **Done** 2026-09-23 (10/10 sim tests, 14/14 selftest) |
| M1 | Logistics core | **Done** 2026-09-23 (43/43 sim tests, 37/37 selftest, 60 UPS with 50k goods) |
| M2 | Style spike | **Done** 2026-09-23 — style bible **approved by Nolan** ("for now"); all 48 building plates and 90 goods icons cut by `tools/art/batch.py` with the locked templates (131 generations, 0 failed, 1 recut) |
| M3 | Machines framework + content | **Done** 2026-09-23 (56/56 sim tests, 46/46 selftest; all 90 goods and every recipe entered) |
| M4 | Economy | **Done** 2026-09-23 (67/67 sim tests, 51/51 selftest) |
| M5 | Newspaper v1 | **Done** 2026-09-23 (73/73 sim tests, 59/59 selftest); Nolan's playtest is the real check |
| M6 | Map generation | **Done** 2026-09-23 (80/80 sim tests; pan at full speed 59.6 FPS) |
| M7 | Power eras | **Done** 2026-09-23 (86/86 sim tests; each era measured cheaper than the last) |
| M8 | T3–T5 content verified | **Done** 2026-09-23 (93/93 sim tests; car in 11 m 39 s, aeroplane in 12 m 41 s on 60 machines) |
| M9 | Long-range logistics | **Done** 2026-09-23 (104/104 sim tests, 92/92 selftest; trucks, trains with block signals, barges) |
| M10 | QoL | **Done** 2026-09-23 (115/115 sim tests, 111/111 selftest; plans, blueprints, flow overlay v2) |
| M11 | Prestige | **Done** 2026-09-23 (119/119 sim tests, 115/115 selftest; yard, commissions 1–6, endless procedural ones) |
| M12 | Audio | **Done** 2026-09-23 (120/120 selftest; 3 original rags × 3 stems, 14 effects, adaptive layers) |
| M13 | Saves & platform | **Done** 2026-09-23 (124/124 sim tests, 132/132 selftest; exact round-trip saves, crash recovery, front office, presets, settings, achievements; Steam wiring waits on an App ID) |
| M14 | Balance & perf | **Done** 2026-09-23 (127/127 sim tests; 42,760 structures + 79k goods + 2,000 working machines + 200 trucks + 3,000 plans at 1.2 ms/tick; the money curve modelled at ~28 h brisk / ~50 h first-timer; playtests are Nolan's) |
| Audit | Bug audit + fixes | **Done** 2026-09-23 — **0.9.0-rc2**: 91 fixes (3 critical data/soft-lock bugs, an undo money exploit, dock→depot arbitrage, folds with no warning, train deadlocks, a town that never grew, a tutorial that stalled), 217/217 sim tests, self-test 185/185 in the editor, the exported Linux build and the Windows build under Proton; `docs/AUDIT-2026-09-23.md` |
| M15 | Ship | **Release candidate 0.9.0-rc1 built** 2026-09-23 (Linux + Windows exports, exported self-test 132/132, store-page draft + AI disclosure, trailer plan); the Steam account, App ID, price, store assets, trailer capture and the git repo are Nolan's |

## M1 — Logistics core (done 2026-09-23)

Exit criterion: 10k goods flowing at 60 UPS; determinism replay test passes. **Met.**

Delivered:
- Transport lines (`src/Sim/Belts.cs`): goods stored as (good, gap) front to back; a moving block costs O(1) per tick; a parked line costs one hand-off try. Three tiers, curves, side-loads, tier boundaries, loops, rebuild-on-edit that keeps goods in place.
- Splitter/merger and sorting splitter (`Splitter.cs`): two internal lanes, fair merging, filter by pipette.
- Trestle bridge: entry/exit pair placed in one command, deck drawn raised over anything beneath.
- Commands (`Commands.cs`, `World.Apply/Undo/Redo`): Place/Remove/Rotate/SetFilter, exact money reversal, grouped drags, command log.
- Catalog/Items tables (`Catalog.cs`, `Items.cs`), string table `assets/text/en.csv` + `game/Text.cs`.
- Godot: keys 1–8, ad clicks, R on held or hovered, Q pipette (building or good), RMB cancel/demolish drag, Ctrl+Z/Y, two-click trestle with span ghost, rendering of tiers/splitters/decks/goods, flow lines in red when jammed.

Evidence (2026-09-23):
- `dotnet test tests/Sim.Tests`: 43/43. Replay test rebuilds a world from the command log through undo/redo and matches the state hash.
- Sim-only perf (PerfTests): 9,920 moving goods 0.04 ms/tick; 496,000 moving goods on 248,000 belts 5.0 ms/tick; 20,000 stalled goods 0.09 ms/tick.
- `godot --path . -- --selftest`: 37/37 PASS.
- `--bench` in engine: 9,920 goods → 60.8 UPS, 59.8 FPS; `--bench=200` (49,600 goods, 24,800 belts) → 60.6 UPS, 57.6 FPS at zoom 0.25 and 0.6.
- Screenshot of `--demo` reviewed (mines, canvas line, splitter, rubber belt over a trestle crossing a steel line, curve, depots, eight ads).

Known follow-ups (not blockers): export must include `assets/text/*.csv` (M13); the demo scene is the visual regression reference; occlusion/y-sort with real sprites belongs to M2.

## M2 — Style spike (engine done 2026-09-23; art gated on approval)

Exit criterion: one screen of factory looks like a newspaper engraving. **Met for the pieces that have plates**: `docs/review/2026-09-23-m2-factory-1080p.png`.

Delivered:
- Codex `image_gen` proven non-interactive: `codex exec --skip-git-repo-check -C <dir> "<ask + prompt>"` (~1 min each, runs in parallel). Prompt templates locked in `docs/STYLE-BIBLE.md` after 5 rejected variants.
- Nine style-bible plates in `assets/art/` (mine head, smelter, freight depot, five goods, ore ground) made by `tools/art/process.py` (chroma key → ink/paper mapping → 128 px per tile → mipmapped import via `tools/art/import.sh`; `tools/art/bible.sh` rebuilds all). Logged in `docs/AI-ASSETS.md`.
- `game/Art.cs`: sprites are optional per id; procedural ink drawing remains the fallback, so play never depends on a plate.
- Press post-process (`assets/shaders/press.gdshader`, layer 5 under the HUD): 45° halftone for mid-tones, ink bleed, accent registration offset; strength 0.55 by default, `--nopress` to compare. Paper grain and vignette stay in `paper.gdshader`.
- Oblique y-sort by footprint bottom; occlusion wash (35%) for a building whose plate stands between the reader and the hovered cell (self-tested); hybrid zoom cross-fade over ±15% around zoom 0.45.
- Ore ground tile turned a quarter per cell (mirroring made a lattice).

Evidence: `dotnet test` 43/43; selftest 41/41 PASS (includes plate loading and occlusion checks); screenshots in `docs/review/` (style bible sheet, 1080p factory frame, press on/off at zoom 0.8).

**Style bible APPROVED by Nolan 2026-09-23 ("for now")**: generate the remaining ~45 building plates and ~85 goods icons with the locked templates in `docs/STYLE-BIBLE.md`. The press look was not separately discussed; keep it as is.

## M3 — Machines framework + content (done 2026-09-23)

Exit criterion: iron → steel → plates/rods/wire chain works. **Met** (`MachineTests.SteelChainMakesPlatesRodsAndWire`: open hearth → splitter → press and two drawing mills → depots).

Delivered:
- Content tables: all 90 goods (`Items.cs`), every building in GDD §4–§5 (`Catalog.cs`, ~56 types; ones from later milestones carry `Available=false` and are not sold), every recipe in GDD §6 with placeholder craft times (`Recipes.cs`). T3+ recipes are entered now; M8 verifies the long chains.
- `Machine.cs`: goods enter on any edge (cap = two runs of the hungriest recipe), the input set picks the recipe round-robin, progress in milli-ticks, states starved/working/blocked/unpowered, finished goods leave by front-edge ports that turn with the building (refinery: three).
- Coal power (era 1): `Firebox` on every "Any" machine and on mine heads. 1 coal = 120 kW·s, cap 8, a starter bag of 30 with every purchase. Electric-only machines stay unpowered until M7.
- Terrain kinds for every seam; fixed starter iron/coal/limestone seams (M6 replaces generation). Mines yield the commonest seam under them.
- Godot: classified columns with Tab and clickable tabs, keys 1–9 within a column, plate-less machines print as labelled ink blocks with a state mark and port chutes, machine clippings (state, run, buffers, fire). String table 329 rows.

Evidence: `dotnet test` 56/56; `--selftest` 46/46 PASS (fullscreen); demo frame `docs/review/2026-09-23-m3-demo.png` (smelter plate working, press and coke oven/open hearth as ink blocks).

## M4 — Economy (done 2026-09-23)

Exit criterion: a company can go bankrupt, or thrive. **Met** (`EconomyTests`: default after one missed payment ends the company and freezes the world; a mine and depot on credit repay their balance; share price rises with a profitable week).

Delivered:
- `Market.cs` rewritten in integer fixed point on seeded PCG streams (`Random.cs`): sell = base × trend × event × K/(K+Q), buy = base × trend × event × (K+B)/K; Q and B decay hourly (τ = 2 days) with in-hour sales counted at once; trends are a bounded (0.6–1.6) mean-reverting log walk stepped hourly; K grows 3% a month; ten fictional events (`MarketEvent.All`) roll daily and carry sell/buy/depth/interest/water-output effects.
- `Difficulty.cs`: Boom Times / Steady Trade / Hard Times presets (cash, base credit, weekly rate, trend σ, event chance, richness).
- `Dock.cs` (Receiving Dock, now on sale): buys the ordered raw at the buy price and pushes it out of its port as fast as the belt takes it; `SetOrder` command, set by pipetting a good and clicking the dock.
- Credit line in `World`: limit = base + half the resale value; purchases draw on it automatically; cash repays the balance daily; interest charged weekly; a Banker's Warning when next week's interest is beyond cash and credit; one missed payment → `Defaulted`, the world stops ticking and refuses commands; `Notices` collects event headlines, warnings and the default for the paper.
- Share price = (net worth + 4 × trailing four-week profit + goodwill) / 10,000.
- HUD: masthead money line (cash · credit used of limit · share), red Banker's Warning line, FOUNDRY FOLDS panel when the company ends; dock clippings.

Evidence: `dotnet test` 67/67; `--selftest` 51/51; `docs/review/2026-09-23-m4-masthead.png`.

Known follow-ups: the market page, telegram orders and event editions are M5; difficulty selection UI is M13.

## M5 — Newspaper v1 (done 2026-09-23)

Exit criterion: a new player learns the game from the paper alone. **Built as designed**; the six tutorial editions teach camera, mine head, belts, coal, smelter, depot, market, saturation, press, credit, default, telegrams, splitters and flow lines in order. Whether a new player actually learns from them is Nolan's playtest to judge.

Delivered:
- `Paper.cs` (sim): editions with headline/deck/body keys and inside columns; one front page per in-game day with the rest queued for tomorrow, tutorial editions and the fold printing at once as extras; milestone editions (first steel/car/plane, 1,000 cars, first waterwheel/boiler/power station/dam); market events, the Banker's Warning and the fold from `Notices`; the telegram desk (orders wired every 2–4 days for goods the works has made, quantity by tier, due in 3–6 days; filled by depot sales, missed at the due hour, each with a headline). Tutorial steps: ed. 2 on the first mine head, 3 on the first iron ingot, 4 when iron sags 15%, 5 on the first iron plate, 6 a day later with the first order (20 iron plate). All hashed.
- `Market.History`: seven daily closing sell prices per good for the sparkline.
- `PaperView.cs` (Godot): Tab opens the Courier as a full-page overlay with FRONT PAGE, CLASSIFIED ADVERTISEMENTS (all six columns; the tutorial's ad ringed in red pencil), MARKET (cash/credit/rate/share/net worth line; every raw plus every good made, sold or bought: sell, buy, seven-day sparkline, trend), TELEGRAM DESK (NEED 20 IRON PLATE BY THURSDAY STOP GARVEY HARDWARE STOP, with delivery status) and ARCHIVE (every edition, click to reread). Tab/Esc close; clicking an ad picks it and closes the paper; world input is blocked while it is open.
- `AdCard.cs` shared by the deck strip and the paper; Shift+Tab now turns the strip's column. A new edition runs as a red EXTRA line under the masthead for ten seconds.
- 456-row string table with all copy (`assets/text/en.csv`).

Evidence: `dotnet test` 73/73 (`PaperTests`: first edition at once, tutorial sequence, one-front-page-a-day, telegrams fill/miss, milestones, fold, determinism); `--selftest` 59/59 (Tab opens/closes, ring on the mine head, ad click picks and closes, keys blocked while open, edition 2 rings the smelter); screenshots `docs/review/2026-09-23-m5-*.png`.

Follow-ups: engravings on the ads (after the style bible is approved); the deck strip could go once the paper is the habit (Nolan's call); flow-overlay key F is M10.

## M6 — Map generation (done 2026-09-23)

Exit criterion: seeded worlds generate endlessly and pan smoothly. **Met**: `--panbench` (WASD speed, zoom 0.4, 600 frames over fresh ground) 59.6 FPS with 233 chunks generated; stationary 57.9 FPS.

Delivered:
- `Noise.cs` (seeded 2D simplex), `MapGen.cs`: rivers as meandering west→east corridors every 320 rows (main river at row 26 by the works, 1–3 tiles wide), elevation with valleys along rivers → hills, moisture → forests, ore patches dealt per 32×32 chunk (0–2, radius 3–5, eight kinds by weight, richness × (1 + distance/2,000)), fixed starter iron/coal/limestone seams, a cleared start area; per-chunk tile cache; `flat` maps for tests and benches.
- `Town.cs`: Carvell Falls founded as 16 blocks on both banks; every 3 days it adds 1 + profit/$500 blocks (max 4) next to itself, never on river, seams, buildings or plans; unbuildable ground.
- Land: $2 a tile × (1 + 4 × town density within 8), machines on hills +25%, forest clearing $20 a tile (the tile stays cleared); every purchase pays land plus structure; the cursor note shows both; rivers and town refuse building (a trestle spans a river).
- Per-patch depletion: every mine head on a seam shares its W; yield 0.25 + 0.75·e^(−W/W_d) with W_d the patch's richness; the seam clipping prints what is left.
- Rendering: terrain chunks gather every stroke of a kind into one multiline call (hatching, waves, banks, trees, seam marks), chunks are created nearest-first with a per-frame budget of four; contour hatching for hills, engraved trees, water lines with bank strokes, town houses.

Evidence: `dotnet test` 80/80 (`MapTests`: river continuity and width, seed determinism in any order, patch richness by distance and placement rules, land prices and clearing, river/trestle rules, town growth never over buildings, shared depletion); `--selftest` PASS; `docs/review/2026-09-23-m6-town-river.png`, `2026-09-23-m6-hills-woods.png`.

Lesson: a python text edit in M3 never matched and silently dropped the mine's `TryAccept` (belt refuelling); found here by a failing test. Every edit script now asserts its match.

## M7 — Power eras (done 2026-09-23)

Exit criterion: each era clearly beats the last on efficiency. **Met and measured** (`PowerTests`): a waterwheel runs what touches it (and one machine beyond) on no coal at all; a boiler on the mains burns half the coal a firebox would for the same work (10 lumps vs 20 in 300 s); a power station's electricity does the same work on a third of the coal and runs machines a quarter faster; a dam gives 400 kW for nothing but the valley it drowns.

Delivered:
- `Power.cs`: `PowerGrid` with steam networks (pipes, boilers, pumps and stations conduct; machines hang off them), electric networks (poles link within 6, stations, dams and machines within 6 hang off), shaft loads per wheel (touching machines plus one beyond). Rebuilt only when the layout changes; each tick demand is summed per network, stations ask their mains for intake (capped, ×1.5 gain), water gates boilers (a pump waters two), satisfaction = supply ÷ demand, boilers burn coal in proportion to steam drawn. Source priority electric > steam > shaft > firebox, and a dry network falls through to the next source.
- `IPowered` on machines and mine heads; the `Machine`/`Mine` update speed scales by the network's satisfaction; electric-only machines run only on electricity; `Source` stamped per tick.
- Buildings on sale: Waterwheel (1×2, needs the bank, 30 kW), Water Pump (bank, 20 units), Boiler (2×2, 120 kW, coal cap 8 with a starter bag), Steam Pipe, Power Station (3×3, 200 kW intake → 300 kW), Power Pole (radius 6), Hydro Dam (3×3, must cover the river's width ≤ 3 in every column; 400 kW; floods the valley 30 columns upstream and 5 rows either side below elevation 0.1; refused while anything stands or the town sits in the flood; the lake drains when it goes). Drought halves wheel and dam output.
- Rendering: pipes with flanged joints, poles with wires to every pole in reach, a turning undershot wheel, a masonry dam with sluice arches, a source glyph on machines (zigzag electric, wave steam, ring shaft); clippings for every piece with supply/demand and satisfaction; cursor notes for bank/river/flood refusals. `World.FlatBankNear` finds a bank with clear ground (tests, demo).
- Demo now has a river-side works (wheel, smelter, pump, pipes, boiler, station, poles).

Evidence: `dotnet test` 86/86; `--selftest` PASS; `docs/review/2026-09-23-m7-power.png`.

Follow-ups: planning-mode flood preview belongs to M10; plates for the power pieces after the style bible; M14 tunes kW figures.

## M8 — T3–T5 content verified (done 2026-09-23)

Exit criterion: automobile and aeroplane produced end to end. **Met** (`FlagshipTests`): 60 real machines on a real map (54 coal-fired works in a grid, a hydro dam and pole line to 6 electric shops), every raw from a bottomless dock, finished goods moved by a scripted yard crew; the first automobile leaves the final assembly line at **699 s (11 m 39 s)** and the first aeroplane the hangar at **761 s (12 m 41 s)**; FIRST_CAR and FIRST_PLANE print; every one of the 60 machines ran.

Delivered:
- The proof itself (`tests/Sim.Tests/FlagshipTests.cs`): the yard crew works from a **bill of materials** (`BillOfMaterials` expands the two flagship recipes through the graph in whole runs; `Allowances` gives each machine type twice what its needed recipes consume of each good). Without it the chain deadlocks: a round-robin crew feeds every press iron for plate and every lathe wire for rivets, and the boring mill never sees a casting. That is the game's own lesson — the sorting splitter and deliberate routing are what make T3+ work — and the M10 flow overlay should make starvation visible.
- Motor Truck: `FIRST_TRUCK` front page (text rows added), proven off the final assembly line on dam power; the fleet behaviour is M9's.
- Receiving-dock imports of raw rubber and cotton proven at the market's buy price (imports cost more than pit coal).
- Audit test: every machine's port count fits its front edge, input caps hold two runs of the hungriest recipe, output caps two runs of the fullest, no zero-time recipe.
- Logging camp, pump jack and oil seeps were delivered under M7's tests (`HarvestTests`).
- Removed the stray "demo bank at" print from the demo build.

Numbers for M14 (from the proof, unlimited raws, no belt transit): 60 machines; critical path is the airframe works making two wings, a fuselage and a tail in series (140 s of craft) after the radial engine's 18 spark plugs; the electric shops are the busiest machines. Belt lengths were not measured (the yard crew stands in for belts, which M1 proved).

Evidence: `dotnet test` 93/93; `dotnet build FrontPageFoundry.csproj` clean (no selftest run: no control changed).

Follow-ups: the bill-of-materials helper could back an M10 "what do I need for X" clipping; M14 retunes craft times against these figures; achievements list in `docs/ACHIEVEMENTS.md` for M13.

## M9 — Long-range logistics (done 2026-09-23)

Exit criterion: remote patches feed the main works. **Met** (`HaulageTests.TrucksCarryOreFromARemotePatchToTheWorks`): a mine on the starter iron → belt → truck depot → 51 tiles of road → truck depot → belt → freight depot sells 50 ore in the first two minutes on one truck; a barge moves 100 goods along 66 river cells between two landings in 90 s; two trains share one track between two stations and never meet.

Delivered (`src/Sim/Haulage.cs`, `Catalog`, `Commands`, `World`):
- **Track**: road and rail tiles, undirected, laid by dragging like belts (pipes drag now too); never ticked. Rail costs 3× on a hill (`World.StructureCents`). **Rail signal** = a flag on a rail tile: the tool over plain rail buys just the signal for the difference (`ClearSignal` is its undo); on bare ground it lays signalled rail. Blocks are connected rail cut at signals; one train holds a block; a train takes the block it is entering and gives up the ones its body has left; a train in a station holds nothing.
- **Terminals**: truck depot 2×2, rail station 3×3, barge landing 2×2 on the bank. Belts deliver into any edge; goods wait in the yard (48 / 240 / 320) for a vehicle, and what a vehicle brings leaves by the port belt. A motor truck good delivered to a depot joins the fleet at no charge (`Enlisted`).
- **Vehicles**: bought from the Haulage column (truck $3,600, locomotive $15,000, barge $2,000, flat) and given two ends by two clicks (`Assign`; a parked vehicle at either end is reused via `Route`; right-click scraps for 75% (`Scrap`); undo restores the same vehicle id). Capacity 24 / 120 / 160, speed 3 / 5 / 1.25 tiles/s, trains 60% on hills. A vehicle unloads on arrival, loads what waits, and leaves when full, after 5 s with a part load, or after 1 s when the far end has something waiting; it re-plans when its network changes and stands (`NoRoute`) if the way is cut, resuming when it is mended. Paths are breadth-first in a fixed neighbour order, from the network cells touching one terminal to those touching the other; barges use river cells with nothing built on them (a dam blocks them, a trestle deck does not).
- Game layer: roads/rail/signals drawn flat under everything (single strokes at far zoom), vehicles drawn between the goods and the standing buildings and grown up to 1.6× as the view pulls back, terminals as blocks with their waiting goods, a pencil ring on the first terminal chosen with a dashed line to the cursor, clippings for road/rail/signal/terminal/vehicle, cursor notes for every step of the two-click route, three new front pages (FIRST_DEPOT / FIRST_RAIL / FIRST_BARGE). Demo gained a coal-by-truck-then-rail chain and a barge.
- Tests: 11 new (`HaulageTests`) — exit criterion, broken road/mend, home-made truck, scrap/undo exactness, signal buy/clear, one-block/two-trains, signals cut blocks, hill cost ×3 and speed (140 vs 84 ticks = 1.67×), barges, determinism replay, 100 trucks at 0.02 ms/tick. Selftest +18 checks (road drag, depots, two-click truck, price, vehicle clipping, right-click scrap + undo, rail, signal for the difference, landing needs the bank).

Evidence: `dotnet test` 104/104; `--selftest` PASS 92/92; `docs/review/2026-09-23-m9-haulage.png` (demo: road with a loaded truck, rail with a signal and stations, viewed).

Follow-ups: plates for the haulage pieces after the style bible; M10 planning ghosts for road/rail runs; M14 tunes capacities, speeds, prices and yard sizes; the truck fleet's Motor Truck cost path is in (a good becomes a vehicle) but there is no fuel or upkeep yet (M14 decision).

## M10 — QoL: planning ghosts + auto-build, blueprints, flow overlay v2 (done 2026-09-23)

Exit criterion: layouts can be stamped at scale. **Met** (`PlanTests`): a captured layout (belts, splitters with filters, a dock with its order, a trestle, a machine) turns a quarter and stamps anywhere as plans in one undo step, and the plans build themselves one a tick in the order drawn; the self-test copies a belt run by dragging, saves it under a key, turns it, stamps it and undoes the stamp.

Delivered (`src/Sim/Plans.cs`, `World`, `Machine.Wants`, `TransportLine.Passed`; `game/EntityView.Plans.cs`, `Main`, `Hud`):
- **Planning mode (P)**: the pencil is up; every tool draws a `Plan` for nothing (`Draft` / `Undraft`, undoable; drags, trestles and the signal tool all plan). Plans hold their ground against the town (`World.Planned`), a planned dam shades the valley it would drown, a real build over a plan supersedes it. **Auto-build**: with the pencil down, one plan a tick is built, oldest first, **from cash on hand only** — a plan never draws on the credit line; a plan waiting for money holds the queue so the layout goes up in the order it was drawn, one that cannot go down yet is passed over. A drafted sorter or dock carries its filter or order into the build.
- **Blueprints (B)**: blueprint mode drags a rectangle; everything (and every plan) whose origin lies inside becomes the blueprint in hand, settings included, bridges by their entry. `R` turns it a quarter (footprints re-originate correctly; four turns come back round), `1`–`9` saves it under that key, `B` then a key takes a saved one in hand, click stamps it as plans centred on the cursor (only pieces that fit), `Esc` drops it. The sketch in hand prints red where a piece cannot go. Nine slots live in the session (M13 saves them with the company).
- **Flow overlay (F)**: printed over everything at any zoom — flow strokes, goods/min past the end of every line (sampled every 2 s), what each starved machine wants (name and icon), FULL / NO POWER in red, patch % under mine heads and pump jacks, yard in/out at terminals, satisfaction % at boilers, stations, dams and wheels. A red mode line under the masthead names whatever is on.
- Demo: a pencilled open hearth, belt and depot; `--overlay` starts with the overlay on.

Evidence: `dotnet test` 115/115 (11 new `PlanTests`: cost nothing / hold ground / build when the pencil is down, cash-only rule, one per tick in order, settings carried, undo, town fenced by plans, capture, rotation invariants, stamp + undo + build, replay determinism, wants/passed); `--selftest` PASS 111/111 (+19 checks); `docs/review/2026-09-23-m10-overlay-plans.png` viewed (overlay tags, pencil plans, mode line).

Follow-ups: M12 typewriter sound for planning; M13 persists blueprint slots; an undo of a real build does not bring back the plan it superseded (documented in §16); M14 may cap plans per company.

## M11 — Prestige: Exposition Yard, commissions 1–6, procedural commissions (done 2026-09-23)

Exit criterion: endless goals after aeroplanes. **Met** (`PrestigeTests`): building the Exposition Yard opens commission 1; filling its bill leaves permanent goodwill, prints a front page and opens commission 2; `Commissions.Nth(n)` gives the six designed bills and then repeats them with quantities × 1.5 a round under new names for ever (the 13th is the third Motorcade at 1,125 cars).

Delivered (`src/Sim/Prestige.cs`, `World.Prestige`, `Paper.OnCommission`, `Yard`):
- **Exposition Yard** (3×3, $25,000, flat, Extraction column) takes any good, pays the market price like a depot, and counts it against the open bill; goods the bill does not want are still paid. Losing the yard keeps the commission and the goodwill.
- **Commissions**: the six from GDD §6 verbatim; the n-th beyond uses template (n−1) mod 6 with quantities × 1.5 per round (integer, rounded up) and a name from the template plus an ordinal ("The Second Skyscraper") or an airship's name ("Zeppelin Liberty"). **Goodwill** = 25% of the bill at base prices, added to the share price for ever (M14 placeholder).
- Front pages `COMMISSION_OPEN` / `COMMISSION_DONE` carry the name (text key + argument, resolved in the game layer, so procedural names localise); commission keys bypass the once-only rule that other headlines have.
- Yard clipping lists the bill line by line; the flow overlay tags the yard with lines done.

Evidence: `dotnet test` 119/119 (4 new `PrestigeTests`); `--selftest` PASS 115/115 (+4 checks: no commission before a yard, the yard's key, the yard opens commission 1, its clipping names the Motorcade).

Follow-ups: a commissions column on the paper's market page (M15 polish); achievements Commission I–VI hang off `Prestige.Completed` (M13); goodwill share and the ×1.5 curve are M14's.

## M12 — Audio: rag renders, SFX set, adaptive layers (done 2026-09-23)

Exit criterion: full soundscape. **Met**: three ragtime pieces play on a gramophone with a banjo and a cornet joining as the works grows; the press, telegraph, typewriter, carriage bell, pencil, thud and paper rustle mark the paper's and the hand's moments; machines hum, belts rattle, boilers hiss and the wheel creaks where the camera is, thinned out as the view pulls back; trucks, whistles and horns answer a new route.

Delivered:
- `tools/audio/build.py` (NumPy + ffmpeg, deterministic): a **ragtime composer** — AABBACCDD form with a four-bar run-in, oom-pah left hand, syncopated right hand from a rhythm library with a recurring motif every four bars, four progression templates with secondary dominants, trio in the subdominant, final chord — rendered by an additive synthesiser (piano with inharmonic partials and hammer noise, banjo pluck, cornet with vibrato) into three stems each, then "pressed to shellac" (250–4500 Hz, wow 0.55 Hz, flutter 6.5 Hz, needle crackle, hiss, soft saturation). Three rags: `rag_courier` (F, 96 bpm, 3:06), `rag_carvell` (B♭, 88, 3:22), `rag_foundry` (C, 104, 2:52). **14 effects** synthesised to the GDD list plus a bell, pencil, thud, rustle, truck, whistle and horn; belt/machine/steam/wheel loop. 25 MB of Ogg Vorbis q5 in `assets/audio/`, `manifest.json`, `README.md` (provenance: all original, nothing licensed).
- `game/Audio.cs`: Music and SFX buses; the three stems start together and the banjo (≥ 40 buildings) and cornet (≥ 150) fade in at 20 dB/s; the next record after a six-second pause; a pool of one-shot players; up to eight positional loops for the nearest working machines/mines, lit boilers and driven wheels within 16 tiles, and one belt rattle scaled by moving lines in view — all scaled by nearness (silent at zoom ≤ 0.35, full at ≥ 0.6). `--mute`; the self-test and review captures always run muted so a test never plays through the desk.
- Hooks: new edition → press (a filled commission rings the bell), new telegram → telegraph, Tab → rustle, build → thud, plan → typewriter, P → bell, stamp → pencil, route assigned → truck / whistle / horn.

Evidence: `--selftest` PASS 120/120 (+6: rustle, thud, bell, typewriter, soundscape loaded + first rag playing + muted, ≥ 12 sounds played); levels measured with ffmpeg — piano mean −20.8 dB / peak −7.0 dB, energy above 4.5 kHz −43 dB (the band limit works), no effect peaks above −1.4 dB; `dotnet test` 119/119 unchanged.

Follow-ups: volume sliders in the M13 settings; M14 may retune layer thresholds and loop gains; hired-pianist renders of PD rags remain an option if Nolan wants real Joplin (money decision, §16).

## M13 — Saves & platform (done 2026-09-23)

Exit criterion: a company survives crashes and syncs between PCs. **Met on the code side** (`SaveTests`, self-test): a busy world (belts with goods, a bridge, sorters, a machine mid-run, a dock, a truck under way, plans, a yard, a market event, three days of paper) saves and loads back to the **same state hash**, ticks on for 600 ticks identically, and takes commands identically; a damaged latest file is refused and the store falls back to the newest backup; the company list reads headers without opening bodies. Syncing between PCs is Steam Cloud on the `user://companies` folder, which is a partner-site setting (M15) — nothing in the code depends on it.

Delivered:
- `src/Sim/Save.cs`: format `FPF1` + version + JSON header (company, difficulty, seed, map mode, tick, day, share, cash, saved-at) + Brotli body + FNV-1a checksum. Every class writes and reads its own state beside its `Hash` (partial classes): world scalars, notices, owned counts, cleared/flooded, patch depletion, plans, the full command log, market (glut/buy/pending/trend/history/RNGs/events), paper (editions, telegrams, pending, printed, made, sold, RNG, tutorial), town, prestige, every building's state (machines' inputs/outputs/recipe/progress/firebox, mines, camps, jacks, depots, docks, yards, terminals' yards, boilers, splitters' lanes, belts' tier/role/span/partner/curve), transport lines by id with their slots verbatim (gap, parked-front bookkeeping), the fleet with paths. Undo history is not saved (a loaded company starts fresh; §16). Power networks are recomputed at the top of every tick and were dropped from the state hash as derived.
- `src/Sim/SaveStore.cs`: one folder per company (slug of the name), atomic write (`save.tmp` → flush to disk → rename), `save.bak1`/`save.bak2` rolled on every save and used only to recover a damaged latest, `Load` says which file it read, `List` reads headers newest first.
- `game/Companies.cs` (autosave every in-game day, on quit and on window close; a scratch world — demo, bench, self-test — never saves), `game/Settings.cs` (`user://settings.json`: gramophone and works volumes, mute), `game/Achievements.cs` (the 18 clippings of `docs/ACHIEVEMENTS.md` checked once a second, recorded per company in `achievements.json`, pinned with a bell and a masthead flash; `IAchievementSink` with a `NoSteam` stand-in until the app has a Steam id), `game/Menu.cs` **the front office** (Esc with an empty hand; the only pause): continue, found a new company (name + Boom Times / Steady Trade / Hard Times with their figures), companies on the books (loads by scene reload), settings (sliders on the buses), close up for the day (saves, quits). First run opens on the founding page; a recovered save says so in red.
- Achievement simplifications (§16): Diversified = ten different goods sold in a week (not "at over 80% of base"); The Dam Held = a dam standing when a drought ends.

Evidence: `dotnet test` 124/124 (5 new `SaveTests`); `--selftest` PASS 132/132 (+12: office opens/pauses/blocks keys/closes, new-company defaults, settings slider moves the bus, save/load hash match, backup rotation, company list, First Pour pinned); `docs/review/2026-09-23-m13-front-office.png` viewed.

Follow-ups: Steamworks binding (Steamworks.NET or Facepunch) needs Nolan's App ID — M15; blueprint slots are still session-only (persist in the company folder at M14/15); a save-version migration hook exists (`ReadState(version)`) with nothing to migrate yet.

## M14 — Balance & perf (done 2026-09-23)

Exit criterion: playtesters reach aeroplanes around the 50-hour mark; perf targets. **Perf met and measured**; **balance tuned against a model** (`BalanceTests`) — real playtests are Nolan's and may retune the same two tables.

Perf (`PerfTests`, `FullWorksPerfTests`, i7-9700K, Debug build under xUnit):
- 496,000 goods moving on 2,000 lines (248,000 belts): **0.21 ms/tick**; 20,000 backed-up goods: 0.016 ms/tick; 9,920 goods on 40 lines: 0.0024 ms/tick.
- The full works: **42,760 structures** (39,680 belts, 2,000 smelters working, 400 idle presses, a 640-tile road ring, two depots), 79,360 goods moving, **200 trucks** shuttling, **3,000 plans** waiting for money, Power evaluated every tick: **1.21 ms/tick** (budget 16.7 ms; target 10). GDD §14's 50,000 structures / 500,000 goods at 60 UPS hold with room to spare.
- One change fell out of it: `World.BuildPlans` scans at most 64 plans a tick from a cursor (a wall of blocked plans used to cost O(plans) every tick); the oldest-first / money-holds-the-queue rules are unchanged.

Balance (the model, over the real `Catalog`/`Items` tables, printed by the test):
- Ten stages from iron ore to the aeroplane; each stage's works is paid from the previous stage's income at the **steady glut price** (base × K / (K + rate × 5 s/h × 48.8 h)), and takes the player time to lay at an assumed **2.5 pieces a minute**, thinking included. Stage time = max(money, building). Result with the tuned tables: **28.5 h to the first aeroplane, $1.96M of works, 4,268 pieces**; money is most of the wait at Steel and Aeroplane. A first-time player laying half as fast lands near 50 h.
- **Tuned**: machine prices (GDD §5 "until M14") — mills & furnaces ×2 (open hearth $12,000, refinery $24,000, glassworks $1,800 …; smelter, coke oven, kiln, sawmill unchanged so the first hour stays gentle), shops ×3 (press $3,000, lathe $2,100, precision shop $12,000, frame shop $30,000, electrical shop $27,000 …), works ×6 (engine works $150,000, chassis/body shop $108,000, airframe works $270,000, final assembly $180,000, aircraft hangar $480,000), reduction works $120,000, power station $40,000, hydro dam $120,000; escalation 4% a copy unchanged. Market depth K: parts 800→500, components 300→200, assemblies 60→40, automobile and motor truck 40→30 (raws 5,000, materials 2,000, aeroplane 6 unchanged). `FloodingOneGoodHalvesItsPriceAtTheDepthAndDiversifyingBeatsIt` pins the glut arithmetic: ten iron mines into one depot settle at 60–72% of base; splitting the same ore across two goods earns over 10% more; the sixth aeroplane a day already fetches 30–60%.
- Not touched (placeholders that the proof and the model do not contradict): craft times, vehicle capacities/speeds/prices, yard sizes, goodwill share, commission ×1.5 rounds, difficulty presets.

Evidence: `dotnet test` 127/127 (3 new: full-works perf, the curve, the glut arithmetic); the self-test is unaffected (it funds its own purchases).

Follow-ups: Nolan's playtests decide whether 28–50 h is right; the two tables are the knobs. If build time proves the real gate, the price multipliers can come back down without touching anything else.

## M15 — Ship (release candidate 2026-09-23; the release itself is Nolan's)

Exit criterion: released on Steam. **Everything the project can do without a Steam account is done**; what remains is listed under "Nolan" in `docs/STEAM.md`.

Delivered:
- **The plates.** With the style bible approved (Nolan, 2026-09-23, via the other session), `tools/art/batch.py` cut every remaining plate and icon with the locked templates: 48 buildings and 90 goods (131 generations, 0 failures, 0 QC flags, one icon recut — `plate_glass` had a glazier in it; the subject line now says "no people"). Contact sheets `docs/review/plates-buildings.png` / `plates-goods.png` reviewed: oblique board-game pieces, no side walls, no ground, consistent line weight; several shops came out as cut-away interiors (fine, and consistent). Every sprite logged in `docs/AI-ASSETS.md`. Regenerate everything with `--force`; subjects in `tools/art/subjects.tsv`. In-game frame: `docs/review/2026-09-23-m15-plates-1080p.png`.
- **Release candidate 0.9.0-rc1**: `export_presets.cfg` (Linux x86_64, Windows x86_64, embedded pck, .NET published), `assets/icon.png`, `VERSION`, `config/version`; `tools/release.sh` runs tests → build → self-test → exports → the self-test through the exported Linux binary → `build/front-page-foundry-0.9.0-rc1-linux-x86_64.tar.gz` + `-windows-x86_64.zip` + `SHA256SUMS`. The string table now exports as a raw file (`importer="keep"`; the CSV importer had silently turned it into a Translation and the exported build had no text).
- **Store page draft and AI disclosure** (`docs/STEAM.md`): description, features, tags, system requirements, the disclosure paragraph, achievements and cloud wiring notes, upload steps, checklist. **Trailer plan** (`docs/TRAILER.md`): nine shots, capture recipe, stills on file. `game/Steam.cs`: the achievement sink to fill in once there is an App ID.

Evidence: `tools/release.sh` 2026-09-23 — sim 127/127, build clean, self-test PASS, both exports, exported self-test PASS 132/132, archives 97 MB (Linux tar.gz) and 110 MB (Windows zip) with checksums. The Windows build has not been run (no Windows box here).

**Nolan's list:** Steam Direct + App ID + tags + price + store assets/capsules (stills in `docs/review/`) + trailer capture (`docs/TRAILER.md`) + content survey; run the Windows build once; `git init` + private repo (never done by the agent); playtests for the M14 curve; the price point (GDD §16 open item 1).

## Audit — 2026-09-23 (0.9.0-rc2)

Five area audits (logistics; production & power; economy, paper, town & map; haulage, plans & saves; game layer & UI),
merged and reconciled by the lead. Summary, the list of decisions for Nolan and what he will notice:
`docs/AUDIT-2026-09-23.md`; the detailed reports with every finding: `docs/audit/`.

Evidence: `dotnet test tests/Sim.Tests` 217/217 (127 before); `--selftest` PASS 185/185 in the editor, through the
exported Linux binary, and through the Windows `.exe` under Proton 11 (the Windows build's first run anywhere);
`--playtest` 20 screenshots from the first run to the fold; Nolan's live Boom Times company (read-only copies at
day 7 and day 90) loads and plays on. Version bumped to 0.9.0-rc2 (`VERSION`, `project.godot`, `export_presets.cfg`).
The release archives in `build/` were **not** rebuilt while Nolan was playing the rc1 build from there: run
`bash tools/release.sh` once the game is closed.

