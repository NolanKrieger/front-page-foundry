# Audit — production (machines, recipes, content tables, mines/harvest, dock/depot, power)

*Evidence paths in this report (`scratch/…`, `~/.cache/fpf-audit/…`) pointed into the auditor's private copy and were not kept; the regression tests named here are in `tests/Sim.Tests/Audit*Tests.cs` and `game/Main.SelfTest.cs`. Merge notes: `docs/AUDIT-2026-09-23.md`.*

Auditor: production. Copy: `~/.cache/fpf-audit/production/`. Regression tests: `tests/Sim.Tests/AuditProductionTests.cs` (13).
Patch: `~/.cache/fpf-audit/production.patch`. Files touched: `src/Sim/Power.cs`, `Machine.cs`, `Mine.cs`, `Harvest.cs`, `Dock.cs`,
two small hooks in `World.cs` (`Richness`/`PatchYield` beside `PatchExtracted`; dam drain in `Drop`).

**Counts:** fixed 12 (major 5, minor 7) + 1 undo hook for the lead to wire; proposals 7; out-of-scope reports 5.

**Checked and correct (no change):** all 76 recipes against GDD §6 line by line (inputs, counts, outputs, machine, T6 prestige
parts); subset rule (existing test); every footprint and Any/Elec flag against §5 (except Q4); all 90 goods obtainable
(8 raws minable, timber from camps, crude from jacks, raw rubber and cotton dock-only, 78 made goods on sold machines);
the dock refuses non-raws and charges exactly the price it checked; the depot pays the market sell price and reports
every sale to the paper (telegrams); round-robin recipe choice, input/output caps, goods on any edge, ports turning,
refinery's three ports, milli-tick progress, +25% electric, states; firebox 120 kW·s a lump, starter bag 30 over cap 8,
coke oven coal to the recipe first, mines feeding themselves; boilers burn in proportion to steam drawn; one pump waters
two boilers; poles radius 6; station intake cap 200 kW ×1.5; dam placement, flood and drought ×0.5; rebuild on every
relevant change (placing/removing/rotating machines and power pieces, dam add/remove, load).

## Fixed findings

| ID | Sev | File:line | What was wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| P1 | major | src/Sim/Power.cs:231-268 (Rebuild), :340-352 (Evaluate) | A power station within reach of two pole networks is put in both; the later network overwrites the station's intake. A stray pole beside the station (out of reach of the grid's poles) makes the idle network ask for 1 kW, so the real grid gets ~1 kW and browns out. | `AStrayPoleBesideTheStationDoesNotBrownOutTheGrid`: electrical shop 3 runs in 120 s instead of ~30 | `Rebuild` links every pole that reaches a station or dam into one grid (a station/dam belongs to exactly one grid); poles scan only the cells in their radius | fixed |
| P2 | major | src/Sim/Power.cs:261 | A dam within reach of two separate pole networks gives its 400 kW to each (free power, ×N with N pole lines). | `ADamReachedByTwoPoleLinesIsCountedOnce`: total supply 800 for one dam | same change as P1 | fixed |
| P3 | major | src/Sim/Power.cs:307-327, :375-382 | Fallback (GDD §16): machines on a grid/main that delivers nothing are still counted as that grid's load and left out of the next source's demand, so the wheel/steam main they fall back to is oversubscribed without knowing it (free power, boilers under-burn). | `MachinesFallingBackFromADryGridLoadTheWheel`: wheel demand 0 instead of 38 kW | `Evaluate` settles steam supply first, marks each grid `Live` (a dam, or a station on a main with steam); only live grids/mains take load, the wheel counts every load not on a live grid or main | fixed |
| P4 | minor | src/Sim/Mine.cs:83-88 | `Mine.Hash` leaves out its firebox (camps and jacks include theirs), so determinism/replay/save checks cannot see mine coal diverge. | `TheStateHashCoversAMineHeadsFirebox` | `Mine.Hash` mixes the firebox | fixed |
| P5 | minor | src/Sim/Mine.cs:50-65, Harvest.cs:171,201 | Cached `yield` (a double) is neither hashed nor saved; it is refreshed every 64 ticks, and on load it is recomputed from the current W, so a company saved between refreshes goes on differently from the original (breaks the M13 exact round-trip). Pump jacks never refresh on load. | `AMineSavedBetweenYieldRefreshesGoesOnIdentically` (hash differs 2 ticks after load) | yield is recomputed whenever the patch's W moves (`yieldAt`), a pure function of saved state; no save change | fixed |
| P6 | minor | src/Sim/Mine.cs:53, Harvest.cs:201 (Difficulty.RichnessPercent unused) | GDD §13 patch richness W_d ×1.5 / ×1 / ×0.7 by preset is never applied, though the founding page promises "seams rich" / "seams are thin". | `DifficultyScalesHowFastASeamSlows` | `World.Richness`/`World.PatchYield` apply `RichnessPercent`; mines and jacks use them. Game-layer readouts (Main.cs:1053/1098/1103, EntityView.Plans.cs:160) still call `Mine.YieldFactor(..., patch.Richness)` and should call `world.PatchYield(patch)` (gamelayer) | fixed (sim) |
| P7 | minor | src/Sim/Harvest.cs:121-130, World.cs:485 | `TreesAround` counts forest under the camp's own footprint, which the placement fells: a camp can be sold on trees it clears and then never works. | `ALoggingCampNeedsTreesBeyondTheGroundItClears` (camp at (197,287), 3 trees, all under it) | `TreesAround` skips the footprint cells | fixed |
| P8 | minor | src/Sim/World.cs:846-851 (Drop) | Two dams whose lakes overlap (reachable: probe found 3,337 placeable pairs over 3 seeds): removing one drains the other's lake too. | `RemovingOneDamLeavesTheOtherDamsLake` | `Drop` re-floods the lakes of the dams still standing | fixed |
| P9 | major (perf) | src/Sim/Power.cs:231-268 (Rebuild, electric part) | Grid rebuild was O(grids × all buildings × poles) with an iterator and closure per (building, pole): 318 ms per rebuild (Release) for 6,000 buildings / 1,000 poles / 40 grids, and it runs after every placement of a machine or power piece (plans build one a tick), so a big electrified works stutters on every build. | `RebuildingTheGridsOfAnElectrifiedWorksIsCheap`: 318 ms → 4.3 ms per rebuild (Release), 611 → ~5 ms (Debug) | poles bucketed 8×8; only poles/stations/dams/powered machines search nearby buckets (belts cost nothing); same reach rule (nearest footprint cell within radius 6) | fixed |
| P10 | minor | src/Sim/Power.cs (Rebuild: wheels, pumps) | A waterwheel or pump may be set on a dam lake's shore (flooded tiles count as river); when the dam goes and the lake drains it keeps giving 30 kW / water on dry ground. | `AWheelOnADrainedLakeStopsTurning` (30 kW after draining) | wheels and pumps count only while a footprint cell touches water; rebuild already runs on dam add/remove | fixed |
| P11 | major | src/Sim/Dock.cs:130-142 | A dock buys "while affordable" = cash + the whole credit line. Left running on a losing trade it borrows to within one purchase of the limit, so the next weekly interest cannot be paid and the company folds — with no Banker's Warning (that check runs only at week ends and cannot see a dock drain $2k inside a week). | scratch run: Steady Trade, coal dock → depot: debt $10,534 of $10,536, DEFAULT at day 21, no BANKERS_WARNING notice. Test `ADockBuyingOnCreditLeavesTheBankersWarningAWeekAhead` (fails on RC at day 16) | the dock keeps next week's interest on its new balance in hand (cash short-circuit first, so the resale-value loop is skipped while cash covers it); now the warning prints at day 21 and a missed payment can come no sooner than a week later | fixed |
| P12 | minor | src/Sim/Machine.cs:172-192 (`Wants`) | A starved machine reports the first input of the *next* job in its round-robin: a smelter fed iron ore tells the flow overlay it wants COPPER ORE; a lathe fed wire wants iron casting. | `AStarvedMachineWantsWhatItWasMaking` (RC: Expected IronOre, Actual CopperOre) | look from the job it last ran (derived from saved `nextRecipe`/`Runs`, so it survives a load) | fixed |
| P13 | minor | src/Sim/World.cs ExecuteRemove/ExecutePlace + Machine.cs `Firebox` ctor, Power.cs `Boiler` ctor | Demolish + undo (or undo + redo of a purchase) is money-neutral but re-creates the building with a fresh 30-lump starter bag: free coal, repeatable (smelter at 3 lumps → 30 after remove + undo, cash unchanged). Also reported by logistics. | scratch run (coal 3 → 30, cash change 0); `AFireCanBePutBackAsItWas` | hook added in my files: `FireState` (record struct: Coal, Burn) with `FireState.Of(Building)` and `FireState.Restore(Building, FireState)`, covering fireboxes (machines, mine heads, camps, jacks) and boilers. **Lead to wire:** in `ExecuteRemove` capture `var fire = FireState.Of(b)` before `Drop(b)` and carry it on the inverse `Place` (e.g. a new optional `FireState? Fire` on the logistics `Place(..., Setting, Id)` record, saved with the command log); in `ExecutePlace` after `Add(b)` call `if (cmd.Fire is { } f) FireState.Restore(b, f);`. A fresh purchase (no record) still gets the starter bag. | hook added; wiring = lead |

## Proposals (not changed: design decisions or other owners' call)

| ID | Sev | File:line | What | Evidence | Proposal |
|---|---|---|---|---|---|
| Q1 | major | Machine.cs:109-124 (`TryAccept`), :146-151 (burn only while working) | **Coal on an ore belt soft-locks the machine for good.** A full firebox refuses the lump at the head of the belt; the ore behind it cannot enter; a starved machine never burns, so the fire never makes room. A new machine's 30-lump starter bag is over the cap of 8, so even one coal in a hundred jams it within seconds; with a 1:1 merge (splitter, side-load) any fast machine (smelter, press, lathe…) jams once its fire is full. The overlay then says the machine *wants* ore that sits on the belt behind the coal. The tutorial (ed. 2) says "a belt of coal into any building keeps it lit", which invites merging. | `scratch/ScratchProofs.cs.txt` `MixedBelt`: fresh smelter, belt carrying ore, ore, coal…: 2 runs in 300 s, Starved, fire 29, belt stalled with coal at its head | This is the genre's belt-ends-at-machine rule (a recipe-ratio jam is the same trap, §16 M7) and every bounded coal store only postpones it, so I did not change coal rules. Options for Nolan: (a) communicate it: the clipping/overlay prints "won't take coal: fire full" when the head good is refused (game layer; the sim could expose the last refused good per machine); (b) tutorial ed. 2: "give each building its own coal belt; coal mixed into ore jams"; (c) design change: a starved machine takes coal regardless (unbounded hoard) — fixes the jam, costs coal back-pressure. I recommend (a)+(b). |
| Q2 | minor | Machine.cs:109-124 | Any good a machine cannot use (or does not need because its buffer is full) stops the belt until taken; nothing tells the player which good is at the door. Intended FIFO behaviour, uncommunicated. | reading + Q1 run | same as Q1 (a). |
| Q3 | polish | World.cs:866-883 (`MakeMine`) | A head over mixed seams yields the commonest seam but counts every ore tile toward its rate, and the clipping says "from 4 of 4 seams" when two are copper under an iron head. | reading | count only the chosen seam's tiles (a small output cut on mixed placements) or reword the clipping. |
| Q4 | polish | Catalog.cs:70 | Water Pump is `PowerNeed.None`; GDD §5 lists it as Any. Pumps needing no fuel reads well; record it in §16. | reading | keep; note in §16. |
| Q5 | polish | Catalog.cs:121 | GDD §5 "Hydro Dam 60,000 + 500 cement delivered (proposal)": the cement delivery is not implemented; §16 is silent. | reading | decide: strike it from §5, or require cement at the dam site. |
| Q6 | polish | Power.cs (`Evaluate`, station intake `+1`) | An idle station still asks its main for 1 kW of steam (rounding up), so a lone lit boiler burns a lump every ~4 min for nothing (with 2+ boilers the integer share burns 0). | reading | use a true ceiling `(perStation*1000 + 1499)/1500` and keep grids "live" by the P3 flag. Negligible; left alone. |
| Q7 | polish | Power.cs (`Rebuild`) | A machine within reach of two separate grids takes the first by pole order, even when that grid cannot deliver and the other can. | reading | pick the first live grid at evaluation time if it matters. |

## Out-of-scope findings (reported to the owner, not changed)

| ID | Sev | Owner / file | What | Evidence | Suggested fix |
|---|---|---|---|---|---|
| X1 | major (exploit) | economy — Market.cs `PriceCents`/`BuyPriceCents`, events | Buy-side events (copper strike ×0.6, oil gusher ×0.5, cotton tumble ×0.7) leave the sell price alone, so a dock straight into a depot is a money machine while they run. | `scratch/ScratchProofs.cs.txt` `Arbitrage`: copper strike, canvas belt dock → depot: **+$1,599.36 in 7 days** (1,681 units), from a $5,000 start | never let the depot pay more for a raw than the dock currently charges (sell = min(sell, buy) for raws), or apply buy-side supply events to the sell side too |
| X2 | minor | World.cs `CanPlace` (dam) / plans | A dam floods tiles that hold plans; those plans can never build and stay as ghosts forever (the dam checks buildings and town, not plans). | reading | the dam supersedes plans on its lake (as a real build over a plan does), or refuses like buildings |
| X3 | minor | haulage — barge landings | Same cause as P10: a landing on a dam lake's shore keeps working after the lake drains. | reading + P10 test | require the bank when routing/serving, as wheels and pumps now do |
| X4 | minor (perf) | World.cs `CanAfford` → `CreditAvailableCents` → `ResaleValueCents` | Every call walks every owned escalating copy (a sqrt each): docks every tick (now only when cash does not cover the buy), plans up to 64×/tick, the UI per frame. | reading; PerfTests' 1.2 ms/tick full works includes it | cache resale value, invalidated where `owned[]` changes |
| X5 | minor | gamelayer — Main.cs:1053/1098/1103, EntityView.Plans.cs:160 | The seam/jack % readouts call `Mine.YieldFactor(W, patch.Richness)` and so ignore the preset richness now applied in the sim (P6). | reading | call `world.PatchYield(patch)` |

## Save compatibility

No change to the save format, its writer or its reader: RC `FPF1` saves load as before. On load, Boom Times / Hard Times
companies now get the preset's seam richness (P6), so their patch % moves (Steady Trade is unchanged), and mine/jack yields
are exact from the first tick (P5). `Mine.Hash` now includes the firebox (P4), so state-hash values differ from RC builds;
nothing stores hashes across builds. If the lead wires P13 by adding a `FireState?` to the `Place` command, the saved
command log gains that field (logistics' Commands/Save format; RC logs would read it as absent).


## Tests run

- `dotnet test tests/Sim.Tests`: **140/140** passed (127 existing + 13 new in `AuditProductionTests`). Each new test was run
  against the RC code first and failed (P1 3 runs vs 28–31; P2 800 kW vs 400; P3 wheel demand 0 vs 38; P4 equal hashes;
  P5 hash differs 2 ticks after load; P6 Boom 0.705 vs 0.787; P7 camp at (197,287) sold on 3 trees it clears; P8 lake
  drained; P9 318 ms/rebuild; P10 30 kW after draining; P11 "day 16: the dock spent next week's interest"; P12 Expected
  IronOre, Actual CopperOre).
- Perf (Release): grid rebuild 318.4 ms → 4.3 ms (6,000 buildings, 1,000 poles, 40 grids); plain tick 0.15 ms.
- `dotnet build FrontPageFoundry.csproj`: clean (0 errors).
- Self-test through `vrun.sh ... -- --selftest` on the final code: **SELFTEST PASS, 132/132** checks (`scratch/selftest2.log`).
- Evidence scripts for proposals/out-of-scope items: `scratch/ScratchProofs.cs.txt` (copy into `tests/Sim.Tests/` as `.cs` to rerun; not in the patch).
