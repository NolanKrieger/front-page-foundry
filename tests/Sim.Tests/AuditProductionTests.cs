using System.Diagnostics;
using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>Audit (production): machines, fireboxes, mines and harvesters, dock and depot, power networks.</summary>
public class AuditProductionTests
{
    readonly ITestOutputHelper output;
    public AuditProductionTests(ITestOutputHelper output) => this.output = output;

    static World River(int seed = 1) => new(seed, Rig.Rich, mode: MapMode.RiverOnly);

    static Cell DamSite(World w, int x0)
    {
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x0));
        for (int dx = 0; dx < 80; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x0 + dx, oy), Dir.North) == PlaceResult.Ok)
                    return new Cell(x0 + dx, oy);
        throw new InvalidOperationException("no dam site");
    }

    static World RoundTrip(World w)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Audit Works");
        ms.Position = 0;
        return SaveGame.Read(ms);
    }

    // ---- Power: stations and dams reached by two pole networks ------------------------------------

    /// <summary>
    /// A pole set down on the far side of a power station, out of reach of the grid's own poles, must not
    /// cut the grid off: the station feeds one network, and the stray pole joins it through the station.
    /// </summary>
    [Fact]
    public void AStrayPoleBesideTheStationDoesNotBrownOutTheGrid()
    {
        var w = River();
        var bank = w.FlatBankNear(120, 34, 14);
        w.PlaceOk(BuildingType.WaterPump, bank.X, bank.Y, Dir.North);
        w.PlaceOk(BuildingType.SteamPipe, bank.X, bank.Y - 1);
        w.PlaceOk(BuildingType.SteamPipe, bank.X + 1, bank.Y - 1);
        w.PlaceOk(BuildingType.Boiler, bank.X + 1, bank.Y - 3);
        w.PlaceOk(BuildingType.PowerStation, bank.X + 3, bank.Y - 4);
        w.PlaceOk(BuildingType.PowerPole, bank.X + 6, bank.Y - 6);
        w.PlaceOk(BuildingType.PowerPole, bank.X + 11, bank.Y - 6);
        w.PlaceOk(BuildingType.ElectricalShop, bank.X + 12, bank.Y - 9);
        var shop = (Machine)w.BuildingAt(new Cell(bank.X + 12, bank.Y - 9))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X + 14, bank.Y - 9);
        // Within six of the station's west cells, more than six from either grid pole.
        w.PlaceOk(BuildingType.PowerPole, bank.X - 1, bank.Y - 5);
        for (int t = 0; t < 120 * World.TicksPerSecond; t++)
        {
            shop.TryAccept(w, Item.InsulatedWire, Dir.East, 60, shop.Origin);
            w.Tick();
        }
        Assert.Equal(PowerSource.Electric, shop.Source);
        // 5 s harnesses at +25%: 4 s each, so about 30 in two minutes (the same as without the stray pole).
        Assert.InRange(shop.Runs, 28, 31);
        var station = w.Buildings.OfType<PowerStation>().Single();
        Assert.Single(w.Power.ElecNets, n => n.Stations.Contains(station));
    }

    /// <summary>A dam reached by two pole lines gives its 400 kW once, not once per line.</summary>
    [Fact]
    public void ADamReachedByTwoPoleLinesIsCountedOnce()
    {
        var w = River();
        var dam = DamSite(w, 150);
        w.PlaceOk(BuildingType.HydroDam, dam.X, dam.Y);
        // One pole north of the dam, one south: each within six of the dam, twelve apart.
        w.PlaceOk(BuildingType.PowerPole, dam.X + 1, dam.Y - 5);
        w.PlaceOk(BuildingType.PowerPole, dam.X + 1, dam.Y + 7);
        w.PlaceOk(BuildingType.ReductionWorks, dam.X + 2, dam.Y - 9);
        w.PlaceOk(BuildingType.ReductionWorks, dam.X + 2, dam.Y + 9);
        w.Tick();
        Assert.Equal(400, w.Power.ElecNets.Sum(n => n.SupplyKw));
        Assert.Equal(1, w.Power.ElecNets.Count(n => n.Dams.Count > 0));
    }

    /// <summary>
    /// Networks are rebuilt whenever a machine or power piece is placed (plans build one a tick), so a rebuild in an
    /// electrified works must stay cheap: 40 belt loops (4,960 belts), 1,000 poles in 40 grids, 40 smelters.
    /// </summary>
    [Fact]
    public void RebuildingTheGridsOfAnElectrifiedWorksIsCheap()
    {
        var w = PerfTests.LoopField(loops: 40, side: 32, BuildingType.BeltCanvas, out _);
        int cols = (int)Math.Ceiling(Math.Sqrt(40));
        for (int n = 0; n < 40; n++)
        {
            int ox = (n % cols) * 34, oy = (n / cols) * 34;
            for (int py = 3; py < 30; py += 6)
                for (int px = 3; px < 30; px += 6)
                    w.PlaceOk(BuildingType.PowerPole, ox + px, oy + py);
            w.PlaceOk(BuildingType.Smelter, ox + 4, oy + 4);
        }
        w.Tick();
        Assert.Equal(40, w.Power.ElecNets.Count);
        Assert.All(w.Power.ElecNets, n => Assert.Single(n.Machines));
        var tick = Stopwatch.StartNew();
        for (int k = 0; k < 5; k++)
            w.Tick();
        double tickMs = tick.Elapsed.TotalMilliseconds / 5;
        var sw = Stopwatch.StartNew();
        const int rebuilds = 5;
        for (int k = 0; k < rebuilds; k++)
        {
            w.Power.Invalidate();
            w.Power.Evaluate();
        }
        double ms = sw.Elapsed.TotalMilliseconds / rebuilds;
        output.WriteLine($"{w.Buildings.Count} buildings, {w.Buildings.OfType<Pole>().Count()} poles: {ms:F1} ms per rebuild; a plain tick {tickMs:F2} ms");
        Assert.True(ms < 60, $"{ms:F1} ms per rebuild");
    }

    // ---- Power: falling back from a network that delivers nothing ----------------------------------

    /// <summary>
    /// GDD §16 fallback: a network that delivers nothing does not count and the machine takes the next source
    /// down. The next source must then carry its load: a waterwheel's two loads that also sit on a dry grid
    /// still share the wheel's 30 kW (38 kW asked, 79%), rather than both running free.
    /// </summary>
    [Fact]
    public void MachinesFallingBackFromADryGridLoadTheWheel()
    {
        var w = River();
        var bank = w.FlatBankNear(80, 12, 12);
        w.PlaceOk(BuildingType.Waterwheel, bank.X, bank.Y - 1, Dir.North);
        w.PlaceOk(BuildingType.OpenHearthFurnace, bank.X + 1, bank.Y - 3);
        var hearth = (Machine)w.BuildingAt(new Cell(bank.X + 1, bank.Y - 3))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X + 4, bank.Y - 3);
        w.PlaceOk(BuildingType.Smelter, bank.X - 2, bank.Y - 2, Dir.West);
        var smelter = (Machine)w.BuildingAt(new Cell(bank.X - 2, bank.Y - 2))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X - 4, bank.Y - 3);
        // A pole over both, and a power station on it with no steam at all.
        w.PlaceOk(BuildingType.PowerPole, bank.X, bank.Y - 5);
        w.PlaceOk(BuildingType.PowerStation, bank.X - 2, bank.Y - 10);
        var wheel = w.Buildings.OfType<Waterwheel>().Single();
        int asked = 0;
        for (int t = 0; t < 80 * World.TicksPerSecond; t++)
        {
            hearth.TryAccept(w, Item.IronIngot, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Coke, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Limestone, Dir.East, 60, hearth.Origin);
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
            asked = Math.Max(asked, wheel.DemandKw);
        }
        Assert.Equal(PowerSource.Shaft, hearth.Source);
        Assert.Equal(38, asked);
        // Same as the wheel alone (PowerTests.ShortageSlowsEveryLoadOnTheWheel): about 8 steel and 31 ingots.
        Assert.InRange(hearth.Runs, 6, 9);
        Assert.InRange(smelter.Runs, 26, 34);
    }

    /// <summary>A waterwheel set on the shore of a dam's lake stops when the dam goes and the lake drains.</summary>
    [Fact]
    public void AWheelOnADrainedLakeStopsTurning()
    {
        var w = River();
        var dam = DamSite(w, 150);
        w.PlaceOk(BuildingType.HydroDam, dam.X, dam.Y);
        bool NearOldRiver(Cell c) => Enumerable.Range(0, 4).Any(d => w.Map.TerrainAt(c + ((Dir)d).Offset()) == Terrain.River);
        Cell? spot = null;
        foreach (var c in w.FloodOf(dam, Dir.North))
            foreach (var d in new[] { Dir.North, Dir.South })
            {
                var at = c + d.Offset() * (d == Dir.North ? 2 : 1);
                if (spot == null && w.CanPlace(BuildingType.Waterwheel, at, Dir.North) == PlaceResult.Ok
                    && !NearOldRiver(at) && !NearOldRiver(at + new Cell(0, 1)))
                    spot = at;
            }
        Assert.NotNull(spot);
        w.PlaceOk(BuildingType.Waterwheel, spot!.Value.X, spot.Value.Y, Dir.North);
        var wheel = (Waterwheel)w.BuildingAt(spot.Value)!;
        w.Tick();
        Assert.Equal(Waterwheel.Kw, wheel.SuppliedKw);
        w.Apply(new Remove(dam));
        w.Tick();
        Assert.Equal(0, wheel.SuppliedKw);
    }

    // ---- Machines ---------------------------------------------------------------------------------

    /// <summary>The flow overlay's "wants" names what the machine has been fed, not the next job in its rotation.</summary>
    [Fact]
    public void AStarvedMachineWantsWhatItWasMaking()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(Item.IronOre, smelter.Wants());   // new: its first job
        for (int i = 0; i < 3; i++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Run(3);
        }
        Assert.Equal(MachineState.Starved, smelter.State);
        Assert.Equal(Item.IronOre, smelter.Wants());

        w.PlaceOk(BuildingType.Lathe, 10, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 11, -1);
        var lathe = (Machine)w.BuildingAt(new Cell(10, 0))!;
        lathe.TryAccept(w, Item.SteelWire, Dir.East, 60, lathe.Origin);
        w.Run(5);
        Assert.Equal(Item.SteelWire, lathe.Wants());
        // Round trip: the answer comes from saved state.
        Assert.Equal(Item.SteelWire, ((Machine)RoundTrip(w).BuildingAt(new Cell(10, 0))!).Wants());
    }

    /// <summary>
    /// The undo hook: a fire captured before demolition and restored on the re-placed building is exactly the old
    /// fire (RC: demolish + undo hands back a fresh 30-lump starter bag — free coal, money-neutral, repeatable).
    /// </summary>
    [Fact]
    public void AFireCanBePutBackAsItWas()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        for (int t = 0; t < 400 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        var fire = FireState.Of(smelter)!.Value;
        Assert.True(fire.Coal < Firebox.StarterCoal && fire.Burn > 0);
        w.Apply(new Remove(new Cell(0, 0)));
        Assert.True(w.Undo());
        var again = w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(fire, FireState.Of(again));   // wired into undo (the RC handed back a fresh 30-lump bag)
        FireState.Restore(again, new FireState(Firebox.StarterCoal, 0));
        Assert.Equal(Firebox.StarterCoal, FireState.Of(again)!.Value.Coal);
        FireState.Restore(again, fire);
        Assert.Equal(fire, FireState.Of(again));

        var bank = new World(1, Rig.Rich, mode: MapMode.RiverOnly).FlatBankNear(100, 12, 12);
        var r = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        r.PlaceOk(BuildingType.Boiler, bank.X, bank.Y - 4);
        var boiler = (Boiler)r.BuildingAt(new Cell(bank.X, bank.Y - 4))!;
        boiler.Burn(120);
        var boilerFire = FireState.Of(boiler)!.Value;
        FireState.Restore(boiler, new FireState(3, 0));
        Assert.Equal(3, boiler.Coal);
        FireState.Restore(boiler, boilerFire);
        Assert.Equal(boilerFire, FireState.Of(boiler));
        Assert.Null(FireState.Of(w.BuildingAt(new Cell(2, 0))!));   // a depot has no fire
    }

    // ---- Dock -------------------------------------------------------------------------------------

    /// <summary>
    /// A dock left buying at a loss draws the credit line down, but keeps next week's interest in hand: the
    /// Banker's Warning prints a week before any missed payment (it used to spend to the last dollar and fold
    /// the company at the next weekly payment with no warning).
    /// </summary>
    [Fact]
    public void ADockBuyingOnCreditLeavesTheBankersWarningAWeekAhead()
    {
        var w = new World(1, difficulty: Difficulty.SteadyTrade, mode: MapMode.Flat);
        w.PlaceOk(BuildingType.ReceivingDock, 0, 0, Dir.East);
        w.Apply(new SetOrder(new Cell(0, 0), Item.Coal));
        var dock = (Dock)w.BuildingAt(new Cell(0, 0))!;
        w.PlaceOk(BuildingType.BeltSteel, dock.PortCell.X, dock.PortCell.Y);
        w.PlaceOk(BuildingType.FreightDepot, dock.PortCell.X + 1, dock.PortCell.Y - 1);
        for (int t = 0; t < 30 * World.TicksPerDay && !w.Defaulted; t++)
        {
            w.Tick();
            Assert.True(w.CashCents + w.CreditAvailableCents >= w.NextInterestCents || w.BankersWarning,
                $"day {w.Day}: the dock spent next week's interest");
        }
        Assert.True(dock.UnitsBought > 10_000, "the dock bought on credit");
        // Merged with the economy audit's two-week reserve on every purchase: the dock now stops before the
        // warning is even needed. Whatever happens, a fold never comes without the warning a week before it.
        var warning = w.Notices.FirstOrDefault(n => n.Key == "BANKERS_WARNING");
        var fold = w.Notices.FirstOrDefault(n => n.Key == "DEFAULT");
        if (fold != null)
            Assert.True(warning != null && fold.Tick - warning.Tick >= World.TicksPerWeek, "the warning came a week ahead");
    }

    // ---- Mines: hash, yield cache, difficulty richness ---------------------------------------------

    [Fact]
    public void TheStateHashCoversAMineHeadsFirebox()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        var mine = (Mine)w.BuildingAt(new Cell(5, -5))!;
        ulong before = w.StateHash();
        Assert.True(mine.Firebox.Burn());
        Assert.NotEqual(before, w.StateHash());
    }

    /// <summary>A save taken between two yield refreshes loads into a world that goes on exactly as the original.</summary>
    [Fact]
    public void AMineSavedBetweenYieldRefreshesGoesOnIdentically()
    {
        var w = Rig.StarterLine(out _);
        var mine = (Mine)w.BuildingAt(new Cell(5, -5))!;
        long seen = mine.Extracted;
        for (int i = 0; i < 20_000; i++)
        {
            w.Tick();
            bool raised = mine.Extracted != seen;
            seen = mine.Extracted;
            if (raised && mine.Extracted >= 3 && (w.TickCount & 63) != 63 && (w.TickCount & 63) != 0)
                break;
        }
        var back = RoundTrip(w);
        Assert.Equal(w.StateHash(), back.StateHash());
        w.Run(2);
        back.Run(2);
        Assert.Equal(w.StateHash(), back.StateHash());
        w.Run(120);
        back.Run(120);
        Assert.Equal(w.StateHash(), back.StateHash());
    }

    /// <summary>GDD §13: patch richness W_d is 1.5× on Boom Times and 0.7× on Hard Times, and the presets say so.</summary>
    [Fact]
    public void DifficultyScalesHowFastASeamSlows()
    {
        double RateAfter(Difficulty d)
        {
            var w = new World(1, Rig.Rich, d, MapMode.Flat);
            w.PlaceOk(BuildingType.MineHead, 5, -5);
            var mine = (Mine)w.BuildingAt(new Cell(5, -5))!;
            for (int k = 0; k < 10_000; k++)
                w.Extract(mine.Patch!.Id);
            mine.RefreshYield(w);
            return mine.OutputRate;
        }
        double boom = RateAfter(Difficulty.BoomTimes), steady = RateAfter(Difficulty.SteadyTrade), hard = RateAfter(Difficulty.HardTimes);
        Assert.Equal(Mine.YieldFactor(10_000, MapGen.StarterRichness), steady, 9);
        Assert.Equal(Mine.YieldFactor(10_000, MapGen.StarterRichness * 1.5), boom, 9);
        Assert.Equal(Mine.YieldFactor(10_000, MapGen.StarterRichness * 0.7), hard, 9);
    }

    // ---- Logging camp -----------------------------------------------------------------------------

    /// <summary>The camp fells its own footprint to stand on it, so trees under it do not count toward "needs forest".</summary>
    [Fact]
    public void ALoggingCampNeedsTreesBeyondTheGroundItClears()
    {
        var w = Rig.Fresh();
        for (int r = 20; r < 400; r++)
            for (int x = -r; x <= r; x++)
                foreach (int y in new[] { -r, r })
                {
                    var at = new Cell(x, y);
                    if (w.CanPlace(BuildingType.LoggingCamp, at, Dir.East) != PlaceResult.Ok)
                        continue;
                    int under = World.FootprintCells(BuildingType.LoggingCamp, at, Dir.East).Count(c => w.TerrainAt(c) == Terrain.Forest);
                    int all = LoggingCamp.TreesAround(w, at, Dir.East);
                    if (under == 0 || all != under)
                        continue;
                    // Every tree in reach is under the camp itself: once it stands there are none.
                    Assert.Fail($"a camp at {at} can be bought with {under} trees, all of which it clears");
                }
        // And a real placement counts only the trees left standing.
        var spot = FindCampSite(w);
        int before = LoggingCamp.TreesAround(w, spot, Dir.East);
        w.PlaceOk(BuildingType.LoggingCamp, spot.X, spot.Y);
        var camp = (LoggingCamp)w.BuildingAt(spot)!;
        Assert.Equal(camp.Trees, before);
    }

    static Cell FindCampSite(World w)
    {
        for (int r = 20; r < 200; r++)
            for (int x = -r; x <= r; x += 2)
                foreach (int y in new[] { -r, r })
                {
                    var at = new Cell(x + 1, y + 1);
                    if (w.TerrainAt(new Cell(x, y)) == Terrain.Forest && w.CanPlace(BuildingType.LoggingCamp, at, Dir.East) == PlaceResult.Ok
                        && World.FootprintCells(BuildingType.LoggingCamp, at, Dir.East).Any(c => w.TerrainAt(c) == Terrain.Forest))
                        return at;
                }
        throw new InvalidOperationException("no forest");
    }

    // ---- Dams -------------------------------------------------------------------------------------

    /// <summary>Two dams whose lakes overlap: taking one away leaves the other's lake full.</summary>
    [Fact]
    public void RemovingOneDamLeavesTheOtherDamsLake()
    {
        var w = River();
        // Dam sites along the main river; find an upper dam with a lower one close enough downstream that their lakes overlap.
        var sites = new List<Cell>();
        for (int x = -400; x < 400; x++)
        {
            int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
            for (int oy = centre - 4; oy <= centre + 2; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x, oy), Dir.North) == PlaceResult.Ok)
                    sites.Add(new Cell(x, oy));
        }
        Cell? upperAt = null, lowerAt = null;
        foreach (var a in sites)
        {
            w.PlaceOk(BuildingType.HydroDam, a.X, a.Y);
            var lakeA = w.FloodOf(a, Dir.North).ToHashSet();
            foreach (var b in sites.Where(s => s.X >= a.X + 3 && s.X < a.X + Dam.FloodLength))
                if (w.CanPlace(BuildingType.HydroDam, b, Dir.North) == PlaceResult.Ok && w.FloodOf(b, Dir.North).Any(lakeA.Contains))
                {
                    upperAt = a;
                    lowerAt = b;
                    break;
                }
            if (lowerAt != null)
                break;
            Assert.True(w.Undo());
        }
        Assert.NotNull(lowerAt);
        var upper = upperAt!.Value;
        var lower = lowerAt!.Value;
        w.PlaceOk(BuildingType.HydroDam, lower.X, lower.Y);
        var lowerLake = w.FloodOf(lower, Dir.North).ToHashSet();
        Assert.NotEmpty(w.FloodOf(upper, Dir.North).Where(lowerLake.Contains));
        w.Apply(new Remove(upper));
        Assert.All(lowerLake, c => Assert.True(w.Flooded(c), $"{c} drained though the lower dam still holds it"));
    }
}
