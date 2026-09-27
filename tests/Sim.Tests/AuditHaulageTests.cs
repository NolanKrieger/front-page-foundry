using System.Diagnostics;
using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>Audit (haulage-saves): vehicles, rail blocks and paths.</summary>
public class AuditHaulageTests
{
    readonly ITestOutputHelper output;
    public AuditHaulageTests(ITestOutputHelper output) => this.output = output;

    static Terminal TerminalAt(World w, int x, int y) => (Terminal)w.BuildingAt(new Cell(x, y))!;

    static bool Touches(Terminal t, Cell c) => t.Cells.Any(f => f.Manhattan(c) == 1);

    /// <summary>
    /// Depots A (0,0) and B (40,0) on a straight road along y = 2, plus a detour: down x = 5 to y = 6,
    /// east to x = 35, back up to the straight road.
    /// </summary>
    static World Loop(out Terminal a, out Terminal b)
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        w.PlaceOk(BuildingType.TruckDepot, 0, 0);
        w.PlaceOk(BuildingType.TruckDepot, 40, 0);
        for (int x = 0; x <= 41; x++)
            w.PlaceOk(BuildingType.Road, x, 2);
        for (int y = 3; y <= 6; y++)
        {
            w.PlaceOk(BuildingType.Road, 5, y);
            w.PlaceOk(BuildingType.Road, 35, y);
        }
        for (int x = 6; x <= 34; x++)
            w.PlaceOk(BuildingType.Road, x, 6);
        a = TerminalAt(w, 0, 0);
        b = TerminalAt(w, 40, 0);
        return w;
    }

    [Fact]
    public void ATruckThatReroutedMidRoadDrivesTheWholeWayBack()
    {
        var w = Loop(out var a, out var b);
        for (int k = 0; k < 24; k++)
            Assert.True(a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.MotorTruck, a.Origin, b.Origin)));
        var truck = w.Haulage.Fleet[0];
        while (!(truck.Underway && truck.Head.From.X >= 10))
            w.Tick();
        // The straight road is cut ahead of it: it finds the detour from where it stands.
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(20, 2))));
        while (truck.Delivered < 24)
            w.Tick();
        Assert.Equal(b.Origin, truck.At);
        // Something waits at A, so it sets off home.
        Assert.True(a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin));
        // Home again: every cell it stands on is road, and it only arrives from a cell beside the depot.
        var last = truck.Head.To;
        int ticks = 0;
        while (truck.At != a.Origin || truck.Underway)
        {
            if (truck.Underway)
                last = truck.Head.To;
            w.Tick();
            ticks++;
            Assert.True(ticks < 120 * World.TicksPerSecond, "the truck came home");
        }
        Assert.True(Touches(a, last), $"the truck reached depot A from {last}, which does not touch it (a teleport home)");
    }

    /// <summary>Two stations joined by one track along y = 20 (stations at x = 10 and 50), signals where x % every == 0.</summary>
    static World RailLine(out Terminal a, out Terminal b, int signalEvery = 0)
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        w.PlaceOk(BuildingType.RailStation, 10, 17);
        w.PlaceOk(BuildingType.RailStation, 50, 17);
        for (int x = 10; x <= 52; x++)
            w.PlaceOk(signalEvery > 0 && x % signalEvery == 0 ? BuildingType.RailSignal : BuildingType.Rail, x, 20);
        a = TerminalAt(w, 10, 17);
        b = TerminalAt(w, 50, 17);
        // Freight depots on both ports keep the yards draining.
        w.PlaceOk(BuildingType.FreightDepot, a.PortCell.X, a.PortCell.Y);
        w.PlaceOk(BuildingType.FreightDepot, b.PortCell.X, b.PortCell.Y);
        return w;
    }

    static void Stock(World w, Terminal t, Item item, int n)
    {
        for (int k = 0; k < n; k++)
            t.TryAccept(w, item, Dir.East, 60, t.Origin);
    }

    [Fact]
    public void ATrainKeepsTheBlockItIsEnteringWhenRailIsLaidElsewhere()
    {
        var w = RailLine(out var a, out var b, signalEvery: 20);
        Stock(w, a, Item.IronOre, 120);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, a.Origin, b.Origin)));
        var train = w.Haulage.Fleet[0];
        // Run until the train is crossing the signal at x = 20 into the next block.
        while (!(train.Underway && train.Head.From == new Cell(20, 20) && train.Head.PosSu > 0))
            w.Tick();
        int entering = w.Haulage.BlockOf(new Cell(21, 20));
        Assert.Same(train, w.Haulage.HolderOf(entering));
        // Rail laid far away recuts the blocks: the train must still hold the block it is entering.
        w.PlaceOk(BuildingType.Rail, 100, 100);
        entering = w.Haulage.BlockOf(new Cell(21, 20));
        Assert.Same(train, w.Haulage.HolderOf(entering));
    }

    [Fact]
    public void TwoTrainsOnOneSignalledTrackKeepRunning()
    {
        var w = RailLine(out var a, out var b, signalEvery: 20);
        Stock(w, a, Item.IronOre, 240);
        Stock(w, b, Item.Coal, 240);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, a.Origin, b.Origin)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, b.Origin, a.Origin)));
        var first = w.Haulage.Fleet[0];
        var second = w.Haulage.Fleet[1];
        for (int t = 0; t < 5 * 60 * World.TicksPerSecond; t++)
        {
            if (t % 600 == 0)
            {
                Stock(w, a, Item.IronOre, 120);
                Stock(w, b, Item.Coal, 120);
            }
            w.Tick();
            // One train to a block: no two trains ever stand in the same block.
            var blocksOfFirst = first.Underway ? first.BodyCells.Select(w.Haulage.BlockOf).Where(k => k >= 0).ToHashSet() : new HashSet<int>();
            if (second.Underway)
                foreach (var c in second.BodyCells)
                    Assert.DoesNotContain(w.Haulage.BlockOf(c), blocksOfFirst);
        }
        output.WriteLine($"first {first.Delivered} ({first.State}/{first.Reason} at {first.Head}), second {second.Delivered} ({second.State}/{second.Reason} at {second.Head})");
        Assert.True(first.Delivered > 0 && second.Delivered > 0, "both trains delivered");
        long before = first.Delivered + second.Delivered;
        w.Run(120);
        Assert.True(first.Delivered + second.Delivered > before, $"the trains are still delivering: {first.State}/{first.Reason} and {second.State}/{second.Reason}");
    }

    [Fact]
    public void AVehicleHeadingHomeMovesIntoTheCellItSaysItIsMovingInto()
    {
        var w = Loop(out var a, out var b);
        for (int k = 0; k < 24; k++)
            a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin);
        w.Apply(new Assign(BuildingType.MotorTruck, a.Origin, b.Origin));
        var truck = w.Haulage.Fleet[0];
        int steps = 0;
        bool wasHome = true;
        var (from, to, _) = truck.Head;
        for (int t = 0; t < 90 * World.TicksPerSecond; t++)
        {
            if (t % 600 == 0)
                for (int k = 0; k < 24; k++)
                {
                    a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin);
                    b.TryAccept(w, Item.Coal, Dir.East, 60, b.Origin);
                }
            bool underway = truck.Underway;
            w.Tick();
            var head = truck.Head;
            if (underway && truck.Underway && head.From != from)
            {
                // Each step lands on the cell the head was moving into: in both directions.
                Assert.Equal(to, head.From);
                steps++;
                if (!truck.TowardB)
                    wasHome = false;
            }
            (from, to) = (head.From, head.To);
            if (truck.Underway)
                Assert.Equal(1, head.From.Manhattan(head.To));
        }
        Assert.False(wasHome, "the truck drove home at least once");
        Assert.True(steps > 100, $"{steps} steps");
    }

    [Fact]
    public void ABargeCutOffByADamDoesNotSearchTheWholeRiverEverySecond()
    {
        // The landings of BargesShipBetweenLandingsAlongTheRiver, with a dam dropped between them.
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        var up = w.FlatBankNear(20, 4, 4);
        var down = w.FlatBankNear(70, 6, 4);
        w.PlaceOk(BuildingType.BargeLanding, up.X - 1, up.Y - 1);
        w.PlaceOk(BuildingType.BargeLanding, down.X - 1, down.Y - 1);
        var a = TerminalAt(w, up.X - 1, up.Y - 1);
        var b = TerminalAt(w, down.X - 1, down.Y - 1);
        Stock(w, a, Item.Coal, 100);
        Stock(w, b, Item.Coal, 100);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, a.Origin, b.Origin)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, b.Origin, a.Origin)));
        var (dam, _) = AuditSaveTests.DamSite(w, 52);
        Assert.InRange(dam.X, up.X + 1, down.X - 4);
        w.PlaceOk(BuildingType.HydroDam, dam.X, dam.Y, Dir.North);
        for (int t = 0; t < 300 * World.TicksPerSecond && !w.Haulage.Fleet.All(v => v.Reason == WaitReason.NoRoute); t++)
            w.Tick();
        Assert.All(w.Haulage.Fleet, v => Assert.Equal(WaitReason.NoRoute, v.Reason));
        var sw = new Stopwatch();
        double worst = 0, total = 0;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            sw.Restart();
            w.Tick();
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            total += sw.Elapsed.TotalMilliseconds;
        }
        sw.Restart();
        Assert.Null(w.Haulage.FindPath(VehicleKind.Barge, a, b));
        double search = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"two stranded barges: worst tick {worst:F2} ms, {total / 1200:F3} ms/tick on average; one uncached search {search:F1} ms");
        // The average, not the worst tick: one GC pause or a busy machine can stretch a single tick, but a
        // stranded barge re-searching the whole river every second (~80 ms a search) costs over 1 ms a tick.
        Assert.True(total / 1200 < 0.3, $"{total / 1200:F3} ms/tick on average (worst {worst:F2} ms)");
    }

    [Fact]
    public void BargesFollowTheRiverWhereItsCellsOnlyTouchAtACorner()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        HashSet<int> Rows(int x) => Enumerable.Range(0, 60).Where(y => w.TerrainAt(new Cell(x, y)) == Terrain.River).ToHashSet();
        // The first column east of x = 30 whose water does not share a row with the column before.
        int gap = Enumerable.Range(31, 2000).First(x => !Rows(x).Overlaps(Rows(x - 1)));
        var up = w.FlatBankNear(gap - 30, 4, 4);
        var down = w.FlatBankNear(gap + 10, 6, 4);
        Assert.True(up.X < gap && down.X > gap, $"landings either side of the corner at x={gap}: {up} {down}");
        w.PlaceOk(BuildingType.BargeLanding, up.X - 1, up.Y - 1);
        w.PlaceOk(BuildingType.BargeLanding, down.X - 1, down.Y - 1);
        var a = TerminalAt(w, up.X - 1, up.Y - 1);
        var b = TerminalAt(w, down.X - 1, down.Y - 1);
        Stock(w, a, Item.Coal, 100);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, a.Origin, b.Origin)));
        var barge = w.Haulage.Fleet[0];
        w.Run(120);
        Assert.True(barge.Delivered >= 100, $"delivered {barge.Delivered}: {barge.State}/{barge.Reason}");
        Assert.All(barge.Path!, c => Assert.Equal(Terrain.River, w.TerrainAt(c)));
        for (int i = 1; i < barge.Path!.Count; i++)
            Assert.Equal(1, Math.Max(Math.Abs(barge.Path[i].X - barge.Path[i - 1].X), Math.Abs(barge.Path[i].Y - barge.Path[i - 1].Y)));
    }

    [Fact]
    public void ASignalCanBePencilledOntoRailOnlyOnce()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Rail, 20, 10);
        w.Apply(new SetPlanning(true));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.RailSignal, new Cell(20, 10), Dir.East)));
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Draft(BuildingType.RailSignal, new Cell(20, 10), Dir.East)));
        Assert.Single(w.Plans);
        // Rubbing it out leaves nothing behind that the map no longer shows.
        Assert.Equal(PlaceResult.Ok, w.Apply(new Undraft(new Cell(20, 10))));
        Assert.Empty(w.Plans);
    }

    [Fact]
    public void ABargeStopsCallingAtALandingWhoseLakeHasDrained()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        var (dam, flood) = AuditSaveTests.DamSite(w, 150);
        w.PlaceOk(BuildingType.HydroDam, dam.X, dam.Y, Dir.North);
        // A landing whose only water is the lake, and one on the river below the dam.
        bool Lake(Cell c) => w.Flooded(c);
        bool River(Cell c) => w.TerrainAt(c) == Terrain.River && !w.Flooded(c);
        Cell? shore = null;
        foreach (var c in flood.OrderBy(c => c))
            for (int dy = -2; dy <= 1 && shore == null; dy++)
                for (int dx = -2; dx <= 1 && shore == null; dx++)
                {
                    var o = new Cell(c.X + dx, c.Y + dy);
                    if (w.CanPlace(BuildingType.BargeLanding, o, Dir.East) != PlaceResult.Ok)
                        continue;
                    var around = World.FootprintCells(BuildingType.BargeLanding, o, Dir.East)
                        .SelectMany(f => new[] { f + new Cell(1, 0), f + new Cell(-1, 0), f + new Cell(0, 1), f + new Cell(0, -1) }).ToList();
                    if (around.Any(Lake) && !around.Any(River))
                        shore = o;
                }
        Assert.NotNull(shore);
        w.PlaceOk(BuildingType.BargeLanding, shore!.Value.X, shore.Value.Y);
        var quay = w.FlatBankNear(dam.X + 8, 4, 4);
        w.PlaceOk(BuildingType.BargeLanding, quay.X - 1, quay.Y - 1);
        var lake = TerminalAt(w, shore.Value.X, shore.Value.Y);
        var river = TerminalAt(w, quay.X - 1, quay.Y - 1);
        Assert.NotEmpty(w.Haulage.Gates(lake));
        output.WriteLine($"lake landing {lake.Origin} gates {w.Haulage.Gates(lake).Count}, river landing {river.Origin}, dam {dam}");
        // A landing upstream on the river that runs into the lake.
        var up = w.FlatBankNear(shore.Value.X - 40, 4, 4);
        w.PlaceOk(BuildingType.BargeLanding, up.X - 1, up.Y - 1);
        var upstream = TerminalAt(w, up.X - 1, up.Y - 1);
        Stock(w, upstream, Item.Coal, 150);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, upstream.Origin, lake.Origin)));
        var barge = w.Haulage.Fleet[0];
        for (int t = 0; t < 240 * World.TicksPerSecond && barge.Delivered == 0; t++)
            w.Tick();
        Assert.True(barge.Delivered > 0, "the barge crossed the lake");
        // The dam goes: the lake drains and the shore landing stands on dry ground.
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(dam)));
        Assert.Empty(w.Haulage.Gates(lake));
        long delivered = barge.Delivered;
        long shipped = lake.Outbound.Count + lake.Shipped;
        for (int t = 0; t < 240 * World.TicksPerSecond; t++)
        {
            if (t % 600 == 0)
                Stock(w, upstream, Item.Coal, 40);
            w.Tick();
            if (barge.Underway)
                Assert.Equal(Terrain.River, w.TerrainAt(barge.Head.From));
        }
        output.WriteLine($"after draining: barge {barge.State}/{barge.Reason} at {barge.Head.From} ({w.TerrainAt(barge.Head.From)}), delivered {delivered}->{barge.Delivered}, lake landing received {lake.Received}");
        Assert.Equal(WaitReason.NoRoute, barge.Reason);
    }
}
