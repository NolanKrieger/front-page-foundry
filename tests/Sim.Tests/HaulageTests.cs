using System.Diagnostics;
using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>M9: roads and trucks, rail with block signals, barges. The exit criterion is a remote patch feeding the works.</summary>
public class HaulageTests
{
    readonly ITestOutputHelper output;
    public HaulageTests(ITestOutputHelper output) => this.output = output;

    static Terminal TerminalAt(World w, int x, int y) => (Terminal)w.BuildingAt(new Cell(x, y))!;

    /// <summary>Mine on the starter iron → belt → depot A at (10,-5); road along y=-3 to depot B at (61,-5) → belt → freight depot.</summary>
    static World TruckLine(out Terminal a, out Terminal b, out Depot sale, int roadEnd = 61)
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 9, -4);
        w.PlaceOk(BuildingType.TruckDepot, 10, -5);
        for (int x = 10; x <= roadEnd; x++)
            w.PlaceOk(BuildingType.Road, x, -3);
        w.PlaceOk(BuildingType.TruckDepot, roadEnd, -5);
        w.PlaceOk(BuildingType.BeltCanvas, roadEnd + 2, -4);
        w.PlaceOk(BuildingType.FreightDepot, roadEnd + 3, -5);
        a = TerminalAt(w, 10, -5);
        b = TerminalAt(w, roadEnd, -5);
        sale = (Depot)w.BuildingAt(new Cell(roadEnd + 3, -5))!;
        return w;
    }

    [Fact]
    public void TrucksCarryOreFromARemotePatchToTheWorks()
    {
        var w = TruckLine(out var a, out var b, out var sale);
        Assert.Equal(new Cell(12, -4), a.PortCell);
        Assert.Equal(PlaceResult.NeedsTerminal, w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(5, -5))));
        Assert.Equal(PlaceResult.Blocked, w.Place(BuildingType.MotorTruck, 30, 30));
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5))));
        Assert.Equal(Catalog.Of(BuildingType.MotorTruck).BaseCostCents, cash - w.CashCents);
        var truck = Assert.Single(w.Haulage.Fleet);
        Assert.Equal(VehicleState.Loading, truck.State);
        Assert.Equal(1, w.Owned(BuildingType.MotorTruck));

        w.Run(15);
        Assert.True(a.Received > 0, "the belt fills the depot's yard");
        Assert.True(truck.Underway || truck.Cargo.Count > 0, "the truck has loaded");
        w.Run(105);
        Assert.True(truck.Delivered >= 24, $"delivered {truck.Delivered}");
        Assert.True(b.Shipped > 0, "the far depot put ore on its belt");
        Assert.True(sale.ItemsSold >= 20, $"sold {sale.ItemsSold}");
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_DEPOT");
        // A round trip of 51 tiles each way at 3 tiles/s is 34 s plus dwell; several trips fit in two minutes.
        output.WriteLine($"truck delivered {truck.Delivered}, depot sold {sale.ItemsSold} in 120 s");
    }

    [Fact]
    public void ABrokenRoadStopsTheTruckUntilItIsMended()
    {
        var w = TruckLine(out _, out _, out var sale);
        w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5)));
        var truck = w.Haulage.Fleet[0];
        while (!truck.Underway)
            w.Tick();
        w.Run(2);
        Assert.True(truck.Head.From.X < 30, "the truck is on the road, short of the gap");
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(40, -3))));
        w.Run(15);
        Assert.Equal(VehicleState.Waiting, truck.State);
        Assert.Equal(WaitReason.NoRoute, truck.Reason);
        Assert.True(truck.Head.From.X < 40, "it stopped short of the gap");
        Assert.True(w.Undo());
        w.Run(60);
        Assert.True(truck.Delivered > 0 && sale.ItemsSold > 0, "mended road, deliveries resume");
    }

    [Fact]
    public void AHomeMadeMotorTruckJoinsTheFleetAtNoCharge()
    {
        var w = TruckLine(out var a, out _, out _);
        long cash = w.CashCents;
        Assert.True(a.TryAccept(w, Item.MotorTruck, Dir.East, 60, a.Origin));
        var truck = Assert.Single(w.Haulage.Fleet);
        Assert.Equal(VehicleState.Parked, truck.State);
        Assert.Equal(a.Origin, truck.At);
        Assert.Equal(cash, w.CashCents);
        Assert.Equal(1, w.Owned(BuildingType.MotorTruck));
        Assert.Same(truck, w.Haulage.ParkedAt(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Route(truck.Id, new Cell(10, -5), new Cell(61, -5))));
        w.Run(60);
        Assert.True(truck.Delivered > 0);
        // Parking it again keeps it where it stands.
        Assert.Equal(PlaceResult.Ok, w.Apply(new Route(truck.Id, null, null)));
        Assert.Equal(VehicleState.Parked, truck.State);
        Assert.True(w.Undo());
        Assert.Equal(VehicleState.Loading, truck.State);
    }

    [Fact]
    public void ScrapRefundsThreeQuartersAndUndoIsExact()
    {
        var w = TruckLine(out _, out _, out _);
        long cash = w.CashCents;
        w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5)));
        long price = cash - w.CashCents;
        var truck = w.Haulage.Fleet[0];
        Assert.Equal(PlaceResult.Ok, w.Apply(new Scrap(truck.Id)));
        Assert.Empty(w.Haulage.Fleet);
        Assert.Equal(0, w.Owned(BuildingType.MotorTruck));
        Assert.Equal(cash - price + (long)Math.Round(price * World.RefundFraction), w.CashCents);
        Assert.True(w.Undo());
        var back = Assert.Single(w.Haulage.Fleet);
        Assert.Equal(new Cell(10, -5), back.A);
        Assert.Equal(new Cell(61, -5), back.B);
        Assert.Equal(cash - price, w.CashCents);
        Assert.True(w.Undo());
        Assert.Empty(w.Haulage.Fleet);
        Assert.Equal(cash, w.CashCents);
    }

    [Fact]
    public void ASignalGoesOnExistingRailForTheDifferenceAndComesOffAgain()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Rail, 20, 10);
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.Ok, w.CanPlace(BuildingType.RailSignal, new Cell(20, 10), Dir.East));
        w.PlaceOk(BuildingType.RailSignal, 20, 10);
        var track = Assert.IsType<Track>(w.BuildingAt(new Cell(20, 10)));
        Assert.True(track.Signal && track.IsRail);
        Assert.Equal(w.SignalCents, cash - w.CashCents);
        Assert.Equal((0, 1), (w.Owned(BuildingType.Rail), w.Owned(BuildingType.RailSignal)));
        Assert.True(w.Undo());
        Assert.False(track.Signal);
        Assert.Equal(cash, w.CashCents);
        Assert.Equal((1, 0), (w.Owned(BuildingType.Rail), w.Owned(BuildingType.RailSignal)));
        // On bare ground the signal tool lays signalled rail, and removing it refunds the pair.
        w.PlaceOk(BuildingType.RailSignal, 21, 10);
        Assert.True(((Track)w.BuildingAt(new Cell(21, 10))!).Signal);
        cash = w.CashCents;
        w.Apply(new Remove(new Cell(21, 10)));
        Assert.Equal((long)Math.Round(Catalog.Of(BuildingType.RailSignal).BaseCostCents * World.RefundFraction), w.CashCents - cash);
    }

    /// <summary>Two stations joined by one track, y = 20, stations at x = 10 and x = 50 (3×3, gates on the track row below them).</summary>
    static World RailLine(out Terminal a, out Terminal b, int signalEvery = 0)
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        w.PlaceOk(BuildingType.RailStation, 10, 17);
        w.PlaceOk(BuildingType.RailStation, 50, 17);
        for (int x = 10; x <= 52; x++)
            w.PlaceOk(signalEvery > 0 && x % signalEvery == 0 ? BuildingType.RailSignal : BuildingType.Rail, x, 20);
        a = TerminalAt(w, 10, 17);
        b = TerminalAt(w, 50, 17);
        return w;
    }

    [Fact]
    public void OneTrackIsOneBlockSoTwoTrainsNeverMeet()
    {
        var w = RailLine(out var a, out var b);
        for (int k = 0; k < 100; k++)
        {
            Assert.True(a.TryAccept(w, Item.IronOre, Dir.East, 60, a.Origin));
            Assert.True(b.TryAccept(w, Item.Coal, Dir.East, 60, b.Origin));
        }
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, a.Origin, b.Origin)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, b.Origin, a.Origin)));
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_RAIL");
        var first = w.Haulage.Fleet[0];
        var second = w.Haulage.Fleet[1];
        Assert.Equal(0, w.Haulage.BlockOf(new Cell(30, 20)));
        Assert.Equal(w.Haulage.BlockOf(new Cell(10, 20)), w.Haulage.BlockOf(new Cell(52, 20)));

        bool someoneWaited = false, bothRan = false;
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
        {
            w.Tick();
            Assert.False(first.Underway && second.Underway, "one block, one train under way");
            if (first.Reason == WaitReason.Signal || second.Reason == WaitReason.Signal)
                someoneWaited = true;
            if (first.Underway)
                Assert.Same(first, w.Haulage.HolderOf(0));
            if (second.Underway)
                Assert.Same(second, w.Haulage.HolderOf(0));
            bothRan |= first.Delivered > 0 && second.Delivered > 0;
        }
        Assert.True(someoneWaited, "a train waited for the block");
        Assert.True(bothRan, $"both trains ran: delivered {first.Delivered}+{second.Delivered}");
        Assert.True(first.Delivered + second.Delivered >= 200, $"delivered {first.Delivered}+{second.Delivered}");
        Assert.True(b.Outbound.Count > 0 || b.Shipped > 0);
    }

    [Fact]
    public void SignalsCutTheTrackIntoBlocks()
    {
        var w = RailLine(out var a, out var b, signalEvery: 20);
        Assert.Equal(-1, w.Haulage.BlockOf(new Cell(20, 20)));
        Assert.NotEqual(w.Haulage.BlockOf(new Cell(15, 20)), w.Haulage.BlockOf(new Cell(25, 20)));
        Assert.Equal(w.Haulage.BlockOf(new Cell(21, 20)), w.Haulage.BlockOf(new Cell(39, 20)));
        Assert.Equal(3, new[] { 15, 25, 45 }.Select(x => w.Haulage.BlockOf(new Cell(x, 20))).Distinct().Count());
        // A train under way holds only the blocks its body stands in.
        for (int k = 0; k < 50; k++)
            a.TryAccept(w, Item.IronOre, Dir.East, 60, a.Origin);
        w.Apply(new Assign(BuildingType.Locomotive, a.Origin, b.Origin));
        var train = w.Haulage.Fleet[0];
        w.Run(6);
        Assert.True(train.Underway, $"{train.State}/{train.Reason} path={train.Path?.Count} cargo={train.Cargo.Count}");
        Assert.Single(train.Held);
        w.Run(20);
        Assert.Equal(VehicleState.Loading, train.State);
        Assert.Empty(train.Held);
        Assert.Null(w.Haulage.HolderOf(0));
    }

    [Fact]
    public void RailCostsThreeTimesOnHillsAndTrainsSlowToSixtyPercent()
    {
        var w = new World(3, Rig.Rich);
        // Find a run of twelve hill tiles and a run of twelve flat tiles, each clear to build on.
        Cell? hill = null, flat = null;
        for (int y = -60; y <= 60 && (hill == null || flat == null); y += 2)
            for (int x = -120; x <= 120; x += 2)
            {
                bool allHill = true, allFlat = true;
                for (int k = 0; k < 12; k++)
                {
                    var c = new Cell(x + k, y);
                    var tile = w.TileAt(c);
                    bool ok = tile.Kind == Terrain.Ground && w.CanPlace(BuildingType.Rail, c, Dir.East) == PlaceResult.Ok;
                    allHill &= ok && tile.Hill;
                    allFlat &= ok && !tile.Hill;
                }
                if (allHill && hill == null && w.CanPlace(BuildingType.RailStation, new Cell(x, y - 3), Dir.East) == PlaceResult.Ok && w.CanPlace(BuildingType.RailStation, new Cell(x + 9, y - 3), Dir.East) == PlaceResult.Ok)
                    hill = new Cell(x, y);
                if (allFlat && flat == null && w.CanPlace(BuildingType.RailStation, new Cell(x, y - 3), Dir.East) == PlaceResult.Ok && w.CanPlace(BuildingType.RailStation, new Cell(x + 9, y - 3), Dir.East) == PlaceResult.Ok)
                    flat = new Cell(x, y);
            }
        Assert.True(hill != null && flat != null, $"hill {hill} flat {flat}");

        long flatRail = w.PurchaseCents(BuildingType.Rail, flat!.Value, Dir.East) - w.LandCostCents(BuildingType.Rail, flat.Value, Dir.East);
        long hillRail = w.PurchaseCents(BuildingType.Rail, hill!.Value, Dir.East) - w.LandCostCents(BuildingType.Rail, hill.Value, Dir.East);
        Assert.Equal(3 * flatRail, hillRail);

        int Trip(Cell run)
        {
            w.PlaceOk(BuildingType.RailStation, run.X, run.Y - 3);
            w.PlaceOk(BuildingType.RailStation, run.X + 9, run.Y - 3);
            for (int k = 0; k < 12; k++)
                w.PlaceOk(BuildingType.Rail, run.X + k, run.Y);
            var from = TerminalAt(w, run.X, run.Y - 3);
            var to = TerminalAt(w, run.X + 9, run.Y - 3);
            for (int k = 0; k < 120; k++)
                from.TryAccept(w, Item.IronOre, Dir.East, 60, from.Origin);
            w.Apply(new Assign(BuildingType.Locomotive, from.Origin, to.Origin));
            var train = w.Haulage.Fleet[^1];
            int ticks = 0;
            while (!train.Underway)
            {
                w.Tick();
                ticks++;
            }
            int start = ticks;
            while (train.Underway)
            {
                w.Tick();
                ticks++;
            }
            Assert.Equal(to.Origin, train.At);
            return ticks - start;
        }
        int flatTicks = Trip(flat.Value);
        int hillTicks = Trip(hill.Value);
        output.WriteLine($"flat {flatTicks} ticks, hill {hillTicks} ticks");
        Assert.InRange(hillTicks, flatTicks * 160 / 100, flatTicks * 175 / 100);
    }

    [Fact]
    public void BargesShipBetweenLandingsAlongTheRiver()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        var up = w.FlatBankNear(20, 4, 4);
        var down = w.FlatBankNear(70, 6, 4);
        Assert.Equal(PlaceResult.NeedsBank, w.Place(BuildingType.BargeLanding, up.X, up.Y - 8));
        w.PlaceOk(BuildingType.BargeLanding, up.X - 1, up.Y - 1);
        w.PlaceOk(BuildingType.BargeLanding, down.X - 1, down.Y - 1);
        var a = TerminalAt(w, up.X - 1, up.Y - 1);
        var b = TerminalAt(w, down.X - 1, down.Y - 1);
        Assert.Equal(VehicleKind.Barge, a.Kind);
        Assert.NotEmpty(w.Haulage.Gates(a));
        // The far landing's port belt runs into a freight depot.
        var port = b.PortCell;
        w.PlaceOk(BuildingType.BeltCanvas, port.X, port.Y, Dir.North);
        w.PlaceOk(BuildingType.FreightDepot, port.X, port.Y - 2, Dir.North);
        var sale = (Depot)w.BuildingAt(new Cell(port.X, port.Y - 2))!;
        for (int k = 0; k < 100; k++)
            Assert.True(a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, a.Origin, b.Origin)));
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_BARGE");
        var barge = w.Haulage.Fleet[0];
        w.Run(90);
        Assert.True(barge.Delivered >= 100, $"delivered {barge.Delivered}; {barge.State}/{barge.Reason} underway={barge.Underway} path={barge.Path?.Count} head={barge.Head} cargo={barge.Cargo.Count} a.in={a.Inbound.Count} b.out={b.Outbound.Count} up={up} down={down}");
        Assert.True(sale.ItemsSold > 0, $"sold {sale.ItemsSold}; b.out={b.Outbound.Count} shipped={b.Shipped} port={port} portTerrain={w.TerrainAt(port)} beltLine={w.BeltAt(port.X, port.Y).Line?.Count}");
        Assert.All(barge.Path!, c => Assert.Equal(Terrain.River, w.TerrainAt(c)));
        output.WriteLine($"barge path {barge.Path!.Count} cells, delivered {barge.Delivered} in 90 s");
    }

    [Fact]
    public void HaulageReplaysDeterministically()
    {
        static World Play()
        {
            var w = TruckLine(out _, out _, out _);
            w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5)));
            w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(61, -5)));
            w.Run(45);
            w.Apply(new Remove(new Cell(30, -3)));
            w.Run(5);
            w.Undo();
            w.Run(30);
            return w;
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(a.Haulage.Fleet.Sum(v => v.Delivered) > 0);
    }

    [Fact]
    public void AHundredTrucksTickCheaply()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        // A road ring of 200 tiles with four depots on it; 100 trucks run between opposite pairs.
        for (int x = 0; x < 50; x++)
        {
            w.PlaceOk(BuildingType.Road, x, 0);
            w.PlaceOk(BuildingType.Road, x, 50);
        }
        for (int y = 1; y < 50; y++)
        {
            w.PlaceOk(BuildingType.Road, 0, y);
            w.PlaceOk(BuildingType.Road, 49, y);
        }
        w.PlaceOk(BuildingType.TruckDepot, 20, -2);
        w.PlaceOk(BuildingType.TruckDepot, 20, 51);
        var a = TerminalAt(w, 20, -2);
        var b = TerminalAt(w, 20, 51);
        for (int k = 0; k < 100; k++)
            Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.MotorTruck, a.Origin, b.Origin)));
        for (int k = 0; k < 48; k++)
        {
            a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin);
            b.TryAccept(w, Item.Coal, Dir.East, 60, b.Origin);
        }
        w.Run(2);
        var sw = Stopwatch.StartNew();
        const int ticks = 600;
        for (int i = 0; i < ticks; i++)
        {
            if (i % 30 == 0)
                for (int k = 0; k < 24; k++)
                {
                    a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin);
                    b.TryAccept(w, Item.Coal, Dir.East, 60, b.Origin);
                }
            w.Tick();
        }
        sw.Stop();
        double perTick = sw.Elapsed.TotalMilliseconds / ticks;
        output.WriteLine($"100 trucks: {perTick:F4} ms/tick");
        Assert.True(w.Haulage.Fleet.Count(v => v.Underway) > 10, "trucks are moving");
        Assert.True(perTick < 1.0, $"{perTick:F3} ms per tick");
    }
}
