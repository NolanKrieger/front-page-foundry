# Logistics audit — Front Page Foundry 0.9.0-rc1

*Evidence paths in this report (`scratch/…`, `~/.cache/fpf-audit/…`) pointed into the auditor's private copy and were not kept; the regression tests named here are in `tests/Sim.Tests/Audit*Tests.cs` and `game/Main.SelfTest.cs`. Merge notes: `docs/AUDIT-2026-09-23.md`.*

Scope: `src/Sim/Belts.cs`, `Splitter.cs`, `Geometry.cs`, `Commands.cs`, and in `World.cs` the grid, placement,
removal, rotation, drag runs, trestle pairs, undo/redo, command log, escalating price / 75% refund, land on build.

Status legend: **fixed** (regression test in the patch), **proposed** (not changed; reasoning given),
**suspected** (not proven). Probe/regression tests: `tests/Sim.Tests/AuditLogisticsTests.cs`.

## Findings

| ID | Sev | Where | What is wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| L1 | critical | `World.cs` ExecutePlace/ExecuteRemove (undo inverse `Remove(cell, refund)`) | Undo of a build removes *whatever* stands on the cell and refunds the original price. A plan built on that cell since (plans build without undo records) is removed for the old price: place Aircraft Hangar → demolish (+75%) → draft a belt there → tick (plan builds) → Ctrl+Z ×3 = **+$359,993 net worth from nothing**, repeatable. | probe gained 35,999,300 ¢; now `UndoOfABuildNeverRefundsAPlanBuiltOnTheSameCell`, `UndoChainsThroughADemolitionStillReverseExactly`, fuzz `NoSessionOfBuildingDemolishingPlanningAndUndoingMakesMoney` ×4 | The undo record of a build now names the building it put up (`Remove.Id`); `ExecuteRemove` refuses any other building. New save tag 14 for such records (RC logs read unchanged). | fixed |
| L2 | major | `Belts.cs` `TransportLine.Dissolve` + `Load` | Any line rebuild (placing/removing/rotating a belt within 2 tiles) silently deletes the tail good whenever it sits at a negative position (every good handed on from another line or round a ring spends ticks there). `Dissolve` clamps it to 0, `Load` then drops `pos < 0`. | probe lost 54 goods in 1,800 rebuild pairs, 1 from a full ring; now `RebuildingALineNextToAFlowingTierBoundaryKeepsEveryGood`, `RebuildingAFullRingKeepsEveryGood`, fuzz `EditsNeverCreateGoodsAndOnlyDemolitionLosesThem` ×3 | `Dissolve` keeps a good's negative offset on the first tile; `Load` keeps goods down to `LowestPos = −Spacing` (landing spots reach about −92). | fixed |
| L3 | major | `World.cs` ExecuteRotate | Rotating a 1×2 piece (splitter, sorting splitter, waterwheel) only checks other buildings: it can swing into the river, the town, uncleared forest (no felling), a plan, or off the bank. | probe: splitter turned onto (114,23), river; now `TurningASplitterRespectsRiverTownAndPlans`, `TurningASplitterIntoStandingWoodIsRefused` | New cells of a turned piece must be buildable, not forest, not planned; waterwheel/pump/landing must stay on the bank (`NeedsBank`). No money moves on a turn, so undo stays exact. | fixed |
| L4 | minor | `World.cs` ExecuteRemove inverse | Undo of a demolition brings a sorting splitter back without its filter and a receiving dock without its order (the brief: undo of a removal restores the building and its settings). | `UndoOfADemolitionPutsBackTheFilterAndTheOrder` | `Place` gained an optional `Setting`; the demolition's undo record carries the filter/order and `ExecutePlace` puts it back (dock: raws only). New save tag 13 for such records. | fixed |
| L5 | minor | `World.cs` ExecuteRotate inverse | The undo of a rotation is keyed on the clicked cell; rotating a splitter from its second cell moves it off that cell, so Ctrl+Z does nothing. | `UndoOfATurnMadeFromASplittersSecondCell` | The inverse is keyed on `b.Origin`, which is in the footprint either way round. | fixed |
| L6 | minor | `World.cs` OnBeltsChanged (bridge tiers) | A bridge's tier change does not reach (a) a second trestle fed by its exit, nor (b) a splitter lane fed by its exit: both keep the old (canvas) speed and throttle a steel line to 2/s. | `ChainedTrestlesAndASplitterAfterAnExitTakeTheFeedTier` | Tier changes are propagated down a chain of trestles through a queue; each retiered bridge's lines (and the belt past its exit) are rebuilt and splitters beside its exit refreshed. | fixed |
| L7 | polish | `World.cs` Undo/Redo | After the company folds, `Undo`/`Redo` still run and move money (the game blocks the keys, the sim does not). | `AFoldedCompanyRefusesUndoAndRedo` | `Undo`/`Redo`/`CanUndo`/`CanRedo` refuse when `Ended`. | fixed |
| L8 | minor | `World.cs` ExecuteRemove/ExecutePlace + `Firebox` (Machine.cs, Power.cs Boiler) | Demolish + undo (or undo + redo of a build) is money-neutral but hands the building a fresh 30-lump starter bag: free coal, above the belt cap of 8. | probe (not kept): mine at 29 lumps, demolish + undo → 30, cash unchanged | needs a Firebox setter (production's file) | proposed |
| L9 | polish | `Belts.cs` TryInsert | A jammed tier boundary takes one extra good at −60 (half a spacing before its start), drawn on the same spot as the upstream line's parked front (21 goods on 10 tiles). Not changed: that slack is what lets a full ring built of several lines keep turning (§16 "a ring keeps turning when full"); refusing it deadlocks a full two-tier ring. | probe (not kept): canvas 5 tiles → rubber 5 tiles, jammed: 21 goods, two at x=1140; `AFullRingOfTwoTiersKeepsTurning` guards the reason | — | proposed (won't fix) |
| L10 | polish | `tests/Sim.Tests/BeltTests.cs:69,94` | Two asserts were vacuous (`x - x == 0`, `h ^ h == h ^ h`): the "sleeps" and "nothing lost at the boundary" claims were untested. | read | Replaced with goods conservation (dropped = sold + on belts) and unchanged positions/`Stalled` after 2 s. | fixed |

## Save compatibility

- No saved state changed shape. RC saves (`FPF1`, version 1) load unchanged: the only format addition is two new
  command-log tags, **13** (`Place` carrying a setting and/or an old building number) and **14** (`Remove` naming the
  building it undoes). They are written only for undo/redo records made by this build; tags 1–12 are byte-identical.
- A save written by this build whose log holds tags 13/14 cannot be opened by the RC build (it would call the log
  "unknown command"). If the lead bumps `SaveGame.Version` for another reason, these tags fit under it.
- Touches `src/Sim/Save.cs` (WriteCommand/ReadCommand only) because the command records gained fields; flagged for the
  haulage-saves auditor/lead merge.
- Undo-restored buildings now keep their old id (inserted in id order into `buildings`/`machines`); ids and order are
  saved as before.

## Out of scope (for the lead)

- **G1 (major, game/Main.cs, traced):** drags fold into the previous, unrelated undo step. `LayBeltTo` always passes
  `joinPrevious: true`; if the drag's first click created no undo step (it started on an existing belt already
  facing the drag, so `Place` was Blocked and `Rotate` a no-op), every belt of the drag joins the previous action's
  group. `RemoveAt(join: true)` uses `joinNext`, which is never reset to false, so a right-drag that starts on empty
  ground folds its demolitions into the previous group too. Ctrl+Z then reverts both actions at once. Fix in Main.cs:
  start each drag with `joinNext = false`, pass `joinPrevious: joinNext`, and set `joinNext = true` only after an
  `Apply` that created an undo step.
- **G2 (minor, suspected, World.BuildPlans):** `planCursor` is neither saved nor hashed, yet it decides which plan the
  scan reaches first when more than 64 plans wait; a save/load resets it to 0, so the build order after loading can
  differ from an unbroken run. Not proven.
- **G3 = L8:** the free-coal refill needs `Firebox`/`Boiler` coal to be settable (production's files) so the undo record
  of a demolition can carry the fire as it was. Proposal: add `Fuel`/`BurnTicks` to the demolition's `Place` record and
  restore them instead of the 30-lump starter bag; undo/redo of a *build* is covered by "undo refunds everything".

## Tests run

- `dotnet test tests/Sim.Tests`: **147/147** passed (127 baseline + 20 new in `AuditLogisticsTests.cs`; two vacuous
  asserts in `BeltTests.cs` replaced with real checks).
- `dotnet build FrontPageFoundry.csproj`: succeeded, 0 warnings.
- `vrun.sh … --path . -- --selftest`: **SELFTEST PASS**, 132 checks (log `scratch/selftest.log`).
