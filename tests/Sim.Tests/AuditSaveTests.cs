using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>Audit (haulage-saves): a save must reproduce not just the state hash but the future.</summary>
public class AuditSaveTests
{
    readonly ITestOutputHelper output;
    public AuditSaveTests(ITestOutputHelper output) => this.output = output;

    internal static World RoundTrip(World w)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Audit Works");
        ms.Position = 0;
        return SaveGame.Read(ms);
    }

    /// <summary>Ticks both worlds side by side and fails at the first tick their hashes part.</summary>
    internal static void SameFuture(World live, World loaded, int ticks, string what = "")
    {
        Assert.Equal(live.StateHash(), loaded.StateHash());
        for (int t = 1; t <= ticks; t++)
        {
            live.Tick();
            loaded.Tick();
            if (live.StateHash() != loaded.StateHash())
                Assert.Fail($"{what}: the loaded world parted from the live one {t} ticks after the save (tick {live.TickCount})");
        }
    }

    [Fact]
    public void AMineOnAWorkedSeamGoesOnIdenticallyAfterALoad()
    {
        var w = Rig.StarterLine(out _);
        // Save a few ticks after an ore came up, between the mine's yield refreshes (every 64 ticks).
        var mine = (Mine)w.BuildingAt(new Cell(5, -5))!;
        long mined = mine.Extracted;
        while (mine.Extracted < 30)
            w.Tick();
        while ((w.TickCount & 63) != 70 % 64)
            w.Tick();
        output.WriteLine($"saved at tick {w.TickCount}, extracted {mine.Extracted}");
        SameFuture(w, RoundTrip(w), 2_000, "mine");
    }

    /// <summary>A pump jack on the nearest oil seep of a full map, piping crude to a depot.</summary>
    internal static PumpJack Jack(World w)
    {
        for (int r = 30; r < 400; r += 2)
            for (int y = -r; y <= r; y += 2)
                for (int x = -r; x <= r; x += 2)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != r && Math.Max(Math.Abs(x), Math.Abs(y)) != r - 1)
                        continue;
                    var at = new Cell(x, y);
                    if (w.TerrainAt(at) != Terrain.OilSeep || w.CanPlace(BuildingType.PumpJack, at, Dir.East) != PlaceResult.Ok)
                        continue;
                    var jack = (PumpJack)PlaceAndGet(w, BuildingType.PumpJack, at);
                    var port = jack.OutputCell;
                    if (w.CanPlace(BuildingType.FreightDepot, port, Dir.East) == PlaceResult.Ok)
                        w.PlaceOk(BuildingType.FreightDepot, port.X, port.Y - 1);
                    return jack;
                }
        throw new InvalidOperationException("no oil seep");
    }

    static Building PlaceAndGet(World w, BuildingType type, Cell at)
    {
        w.PlaceOk(type, at.X, at.Y);
        return w.BuildingAt(at)!;
    }

    [Fact]
    public void APumpJackGoesOnIdenticallyAfterALoad()
    {
        var w = Rig.Fresh(3);
        var jack = Jack(w);
        while (jack.Pumped < 20)
            w.Tick();
        while ((w.TickCount & 63) != 10)
            w.Tick();
        output.WriteLine($"jack at {jack.Origin}, pumped {jack.Pumped}, tick {w.TickCount}");
        SameFuture(w, RoundTrip(w), 2_000, "pump jack");
    }

    [Fact]
    public void TheTownGrowsTheSameWayAfterALoad()
    {
        var w = Rig.Fresh(5);
        w.Run(4 * 120);         // the town has grown once (day 3)
        var back = RoundTrip(w);
        for (int t = 0; t < 6 * World.TicksPerDay; t++)
        {
            w.Tick();
            back.Tick();
        }
        Assert.True(w.Town.Blocks > 17, $"the town grew: {w.Town.Blocks} blocks");
        Assert.Equal(w.Town.Blocks, back.Town.Blocks);
        Assert.Equal(w.Town.Tiles.OrderBy(c => c), back.Town.Tiles.OrderBy(c => c));
        Assert.Equal(w.StateHash(), back.StateHash());
    }

    /// <summary>Paper.cs is the economy auditor's: its Daily() orders the works' goods by tier with ties in HashSet order. Unskip once that fix lands.</summary>
    [Fact]
    public void TheTelegramDeskWiresTheSameOrdersAfterALoad()
    {
        var w = Rig.Fresh(2);
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.Paper.OnMade(w, Item.IronIngot, 1);
        for (int k = 0; k < 300; k++)
            w.Paper.OnSold(w, Item.IronOre);
        w.Tick();
        w.Paper.OnMade(w, Item.IronPlate, 1);
        w.Run(125);
        Assert.Equal(6, w.Paper.TutorialStep);
        // The works has made several goods of the same tier: which ones head the list decides the order.
        foreach (var item in new[] { Item.CopperIngot, Item.Coke, Item.Glass, Item.SteelIngot, Item.Lumber, Item.CopperSheet, Item.SteelPlate, Item.IronPlate })
            w.Paper.OnMade(w, item, 1);
        var back = RoundTrip(w);
        for (int t = 0; t < 12 * World.TicksPerDay; t++)
        {
            w.Tick();
            back.Tick();
        }
        output.WriteLine(string.Join(", ", w.Paper.Telegrams.Select(t => $"{t.Item}x{t.Quantity}")) + " | " + string.Join(", ", back.Paper.Telegrams.Select(t => $"{t.Item}x{t.Quantity}")));
        Assert.True(w.Paper.Telegrams.Count >= 3);
        Assert.Equal(w.Paper.Telegrams.Select(t => (t.Item, t.Quantity, t.DueTick)), back.Paper.Telegrams.Select(t => (t.Item, t.Quantity, t.DueTick)));
    }

    /// <summary>A dam site on the river map (as in PowerTests) and the cells its lake will cover.</summary>
    internal static (Cell Origin, List<Cell> Flood) DamSite(World w, int x = 150)
    {
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
        for (int dx = 0; dx < 40; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x + dx, oy), Dir.North) == PlaceResult.Ok)
                    return (new Cell(x + dx, oy), w.FloodOf(new Cell(x + dx, oy), Dir.North).ToList());
        throw new InvalidOperationException("no dam site");
    }

    [Fact]
    public void ThePlanQueueResumesWhereItWasAfterALoad()
    {
        // Pumps pencilled on a dam lake's shore lose their bank when the dam goes, so the scan passes over them;
        // a belt the till cannot pay for then holds the queue behind them, and the cursor waits past the pumps.
        // (Merged audit: a dam's lake now supersedes plans under it, so drowned plans no longer make this case.)
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        var (dam, flood) = DamSite(w);
        w.PlaceOk(BuildingType.HydroDam, dam.X, dam.Y, Dir.North);
        var lake = flood.ToHashSet();
        bool touches(Cell c, Func<Cell, bool> wet) => Enumerable.Range(0, 4).Any(d => wet(c + ((Dir)d).Offset()));
        w.Apply(new SetPlanning(true));
        int drafted = 0;
        foreach (var c in lake.SelectMany(l => Enumerable.Range(0, 4).Select(d => l + ((Dir)d).Offset())).Distinct().OrderBy(c => c))
            if (drafted < 5 && !lake.Contains(c) && !touches(c, n => w.Map.TerrainAt(n) == Terrain.River)
                && w.Apply(new Draft(BuildingType.WaterPump, c, Dir.North)) == PlaceResult.Ok)
                drafted++;
        Assert.Equal(5, drafted);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.BeltCanvas, new Cell(dam.X, dam.Y - 30), Dir.East)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(dam)));                 // the lake drains: the pumps are high and dry
        w.Pay(w.CashCents);                                                      // plans build from cash only
        w.Apply(new SetPlanning(false));
        w.Tick();                                                                // the scan passes the pumps and waits at the belt
        Assert.Equal(6, w.Plans.Count);
        var back = RoundTrip(w);
        SameFuture(w, back, 200, "plans");
        Assert.Equal(6, w.Plans.Count);
    }

    /// <summary>
    /// A line along y = 20 from station A (x 10) to B (x 50) with signals at x 20 and 40, crossed at x 30 by a
    /// line from station C (above, gate (30,5)) to D (below, gate (30,35)) that joins the middle block.
    /// </summary>
    internal static World Crossing(out Terminal a, out Terminal b, out Terminal c, out Terminal d)
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        w.PlaceOk(BuildingType.RailStation, 10, 17);
        w.PlaceOk(BuildingType.RailStation, 50, 17);
        for (int x = 10; x <= 52; x++)
            w.PlaceOk(x % 20 == 0 ? BuildingType.RailSignal : BuildingType.Rail, x, 20);
        w.PlaceOk(BuildingType.RailStation, 29, 2);
        w.PlaceOk(BuildingType.RailStation, 29, 36);
        for (int y = 5; y <= 35; y++)
            if (y != 20)
                w.PlaceOk(BuildingType.Rail, 30, y);
        a = (Terminal)w.BuildingAt(new Cell(10, 17))!;
        b = (Terminal)w.BuildingAt(new Cell(50, 17))!;
        c = (Terminal)w.BuildingAt(new Cell(29, 2))!;
        d = (Terminal)w.BuildingAt(new Cell(29, 36))!;
        return w;
    }

    static void Stock(World w, Terminal t, Item item, int n)
    {
        for (int k = 0; k < n; k++)
            t.TryAccept(w, item, Dir.East, 60, t.Origin);
    }

    [Fact]
    public void ATrainCrossingIntoABlockStillHoldsItAfterALoad()
    {
        var w = Crossing(out var a, out var b, out var c, out var d);
        Stock(w, a, Item.IronOre, 120);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, a.Origin, b.Origin)));
        var first = w.Haulage.Fleet[0];
        while (!(first.Underway && first.Head.From == new Cell(20, 20) && first.Head.PosSu > 0))
            w.Tick();
        Stock(w, c, Item.Coal, 120);
        Stock(w, d, Item.Coal, 10);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, c.Origin, d.Origin)));
        SameFuture(w, RoundTrip(w), 2_000, "trains");
    }

    // ---- Everything at once ------------------------------------------------------------------------

    static T At<T>(World w, Cell c) where T : Building => (T)w.BuildingAt(c)!;

    static Machine SmelterWithDepot(World w, int x, int y)
    {
        w.PlaceOk(BuildingType.Smelter, x, y);
        w.PlaceOk(BuildingType.FreightDepot, x + 2, y);
        return At<Machine>(w, new Cell(x, y));
    }

    /// <summary>
    /// A company with every kind of building in every state the brief lists: machines mid-run on every power
    /// source (fireboxes, a waterwheel, steam, a power station and a dam), a mine on a worked seam, a logging
    /// camp, a pump jack, a dock with an order, sorting and plain splitters with goods on their lanes, a bridge,
    /// belts full and jammed, trucks, trains on a signalled line (one holding blocks, one waiting), barges,
    /// plans and blueprint-stamped plans, a running event, a drought, the tutorial through to its first
    /// telegram, an open commission, a grown town, cleared forest and a flooded valley.
    /// </summary>
    internal static World Everything()
    {
        var w = Rig.Fresh(1);
        // Works by the starter seam (as SaveTests.Busy): mine → trucks → depot, sorter, bridge, smelter, dock, yard.
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 9, -4);
        w.PlaceOk(BuildingType.TruckDepot, 10, -5);
        for (int x = 10; x <= 40; x++)
            w.PlaceOk(BuildingType.Road, x, -3);
        w.PlaceOk(BuildingType.TruckDepot, 40, -5);
        w.PlaceOk(BuildingType.BeltCanvas, 42, -4);
        w.PlaceOk(BuildingType.FreightDepot, 43, -5);
        w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(40, -5)));
        w.Apply(new Assign(BuildingType.MotorTruck, new Cell(40, -5), new Cell(10, -5)));
        w.BeltRow(BuildingType.BeltSteel, -30, -26, -20);
        w.PlaceOk(BuildingType.SortingSplitter, -25, -20);
        w.Apply(new SetFilter(new Cell(-25, -20), Item.Coal));
        w.PlaceOk(BuildingType.Trestle, -24, -20, Dir.East, 2);
        w.PlaceOk(BuildingType.BeltSteel, -20, -20);
        w.PlaceOk(BuildingType.Smelter, -19, -21, Dir.East);
        for (int k = 0; k < 6; k++)
            w.Drop(-30 + k % 5, -20, item: k % 2 == 0 ? Item.Coal : Item.IronOre);
        w.PlaceOk(BuildingType.ReceivingDock, -20, -16);
        w.Apply(new SetOrder(new Cell(-20, -16), Item.Sand));
        w.PlaceOk(BuildingType.ExpositionYard, 20, -30);
        var yard = At<Yard>(w, new Cell(20, -30));
        for (int k = 0; k < 3; k++)
            yard.TryAccept(w, Item.Automobile, Dir.East, 60, yard.Origin);
        // A plain splitter whose two outputs run into nothing: lanes and belts full and jammed.
        w.BeltRow(BuildingType.BeltCanvas, -34, -30, -40);
        w.PlaceOk(BuildingType.Splitter, -29, -40);
        w.BeltRow(BuildingType.BeltRubber, -28, -25, -40);
        w.BeltRow(BuildingType.BeltRubber, -28, -25, -39);
        w.PlaceOk(BuildingType.ReceivingDock, -36, -41);     // port (-34,-40): clay until everything backs up
        w.Apply(new SetOrder(new Cell(-36, -41), Item.Clay));
        // A worked seam: the starter iron has given up a good deal already.
        for (int k = 0; k < 30_000; k++)
            w.Extract(-1);
        // A logging camp in the nearest wood.
        bool camp = false;
        for (int r = 20; r < 200 && !camp; r += 3)
            for (int x = -r; x <= r && !camp; x += 3)
                foreach (int y in new[] { -r, r })
                    if (!camp && w.TerrainAt(new Cell(x, y)) == Terrain.Forest && w.CanPlace(BuildingType.LoggingCamp, new Cell(x + 1, y), Dir.East) == PlaceResult.Ok)
                    {
                        w.PlaceOk(BuildingType.LoggingCamp, x + 1, y);
                        camp = true;
                    }
        Assert.True(camp, "found a wood for the logging camp");
        Jack(w);
        // Steam and electricity on the river bank (as PowerTests), and a waterwheel further on.
        var bank = w.FlatBankNear(60, 34, 14);
        w.PlaceOk(BuildingType.WaterPump, bank.X, bank.Y, Dir.North);
        w.PlaceOk(BuildingType.SteamPipe, bank.X, bank.Y - 1);
        w.PlaceOk(BuildingType.SteamPipe, bank.X + 1, bank.Y - 1);
        w.PlaceOk(BuildingType.Boiler, bank.X + 1, bank.Y - 3);
        w.PlaceOk(BuildingType.PowerStation, bank.X + 3, bank.Y - 4);
        w.PlaceOk(BuildingType.PowerPole, bank.X + 6, bank.Y - 6);
        w.PlaceOk(BuildingType.PowerPole, bank.X + 11, bank.Y - 6);
        w.PlaceOk(BuildingType.ElectricalShop, bank.X + 12, bank.Y - 9);
        w.PlaceOk(BuildingType.FreightDepot, bank.X + 14, bank.Y - 9);
        var shop = At<Machine>(w, new Cell(bank.X + 12, bank.Y - 9));
        var poled = SmelterWithDepot(w, bank.X + 7, bank.Y - 9);          // within a pole's reach: electricity
        w.PlaceOk(BuildingType.Smelter, bank.X - 2, bank.Y - 2, Dir.West);    // touches the pipe, out of reach: steam
        w.PlaceOk(BuildingType.FreightDepot, bank.X - 4, bank.Y - 3);
        var steamed = At<Machine>(w, new Cell(bank.X - 2, bank.Y - 2));
        var boiler = At<Boiler>(w, new Cell(bank.X + 1, bank.Y - 3));
        var wheelBank = w.FlatBankNear(bank.X + 40, 12, 12);
        w.PlaceOk(BuildingType.Waterwheel, wheelBank.X, wheelBank.Y - 1, Dir.North);
        var wheeled = SmelterWithDepot(w, wheelBank.X + 1, wheelBank.Y - 2);
        var fired = SmelterWithDepot(w, -40, 10);
        // A dam far downstream with a pole line to a reduction works; the valley behind it floods.
        Cell? dam = null;
        for (int x = wheelBank.X + 60; x < wheelBank.X + 600 && dam == null; x++)
        {
            int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
            for (int oy = centre - 3; oy <= centre + 1 && dam == null; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x, oy), Dir.North) == PlaceResult.Ok
                    && w.CanPlace(BuildingType.PowerPole, new Cell(x + 3, oy - 5), Dir.North) == PlaceResult.Ok
                    && w.CanPlace(BuildingType.ReductionWorks, new Cell(x + 4, oy - 14), Dir.East) == PlaceResult.Ok
                    && w.CanPlace(BuildingType.FreightDepot, new Cell(x + 7, oy - 14), Dir.East) == PlaceResult.Ok)
                    dam = new Cell(x, oy);
        }
        Assert.NotNull(dam);
        w.PlaceOk(BuildingType.HydroDam, dam!.Value.X, dam.Value.Y, Dir.North);
        w.PlaceOk(BuildingType.PowerPole, dam.Value.X + 3, dam.Value.Y - 5);
        w.PlaceOk(BuildingType.PowerPole, dam.Value.X + 3, dam.Value.Y - 10);
        w.PlaceOk(BuildingType.ReductionWorks, dam.Value.X + 4, dam.Value.Y - 14);
        var works = At<Machine>(w, new Cell(dam.Value.X + 4, dam.Value.Y - 14));
        w.PlaceOk(BuildingType.FreightDepot, dam.Value.X + 7, dam.Value.Y - 14);
        // Barges below the dam.
        var quay = w.FlatBankNear(dam.Value.X + 8, 4, 4);
        var farQuay = w.FlatBankNear(quay.X + 50, 6, 4);
        w.PlaceOk(BuildingType.BargeLanding, quay.X - 1, quay.Y - 1);
        w.PlaceOk(BuildingType.BargeLanding, farQuay.X - 1, farQuay.Y - 1);
        var landing = At<Terminal>(w, new Cell(quay.X - 1, quay.Y - 1));
        var farLanding = At<Terminal>(w, new Cell(farQuay.X - 1, farQuay.Y - 1));
        Stock(w, landing, Item.Coal, 200);
        Stock(w, farLanding, Item.Limestone, 50);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Barge, landing.Origin, farLanding.Origin)));
        // A signalled line with two trains that meet head-on, well north of the works.
        w.PlaceOk(BuildingType.RailStation, 10, -203);
        w.PlaceOk(BuildingType.RailStation, 50, -203);
        for (int x = 10; x <= 52; x++)
            w.PlaceOk(x % 20 == 0 ? BuildingType.RailSignal : BuildingType.Rail, x, -200);
        var west = At<Terminal>(w, new Cell(10, -203));
        var east = At<Terminal>(w, new Cell(50, -203));
        w.PlaceOk(BuildingType.FreightDepot, west.PortCell.X, west.PortCell.Y);
        w.PlaceOk(BuildingType.FreightDepot, east.PortCell.X, east.PortCell.Y);
        Stock(w, west, Item.IronOre, 240);
        Stock(w, east, Item.Coal, 240);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, west.Origin, east.Origin)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.Locomotive, east.Origin, west.Origin)));
        // Plans, and a blueprint of the sorter corner stamped as plans; the pencil stays up.
        w.Apply(new SetPlanning(true));
        w.Apply(new Draft(BuildingType.Press, new Cell(-10, -10), Dir.South, 0, null));
        w.Apply(new Draft(BuildingType.SortingSplitter, new Cell(-6, -10), Dir.East, 0, Item.Coal));
        var print = Blueprint.Rotate(Blueprint.Capture(w, new Cell(-30, -21), new Cell(-18, -15)));
        Assert.True(Blueprint.Stamp(w, print, new Cell(-70, -70)) > 5);
        // The tutorial to its first telegram, a running event and a drought.
        w.Paper.OnMade(w, Item.IronIngot, 1);
        for (int k = 0; k < 300; k++)
            w.Paper.OnSold(w, Item.IronOre);
        w.Tick();
        w.Paper.OnMade(w, Item.IronPlate, 1);
        w.StartEvent(MarketEvent.All[2]);
        w.StartEvent(MarketEvent.All.Single(e => e.Id == "drought"));
        // Run past the first town growth, keeping the works fed.
        for (int t = 0; t < 3 * World.TicksPerDay + 7 * World.TicksPerSecond; t++)
        {
            if (t % 30 == 0)
            {
                shop.TryAccept(w, Item.InsulatedWire, Dir.East, 60, shop.Origin);
                steamed.TryAccept(w, Item.IronOre, Dir.East, 60, steamed.Origin);
                poled.TryAccept(w, Item.IronOre, Dir.East, 60, poled.Origin);
                boiler.TryAccept(w, Item.Coal, Dir.East, 60, boiler.Origin);
                wheeled.TryAccept(w, Item.IronOre, Dir.East, 60, wheeled.Origin);
                fired.TryAccept(w, Item.IronOre, Dir.East, 60, fired.Origin);
                fired.TryAccept(w, Item.Coal, Dir.East, 60, fired.Origin);
                works.TryAccept(w, Item.Bauxite, Dir.East, 60, works.Origin);
                works.TryAccept(w, Item.Coke, Dir.East, 60, works.Origin);
            }
            if (t % 1200 == 0)
            {
                Stock(w, west, Item.IronOre, 60);
                Stock(w, east, Item.Coal, 60);
                Stock(w, landing, Item.Coal, 40);
            }
            w.Tick();
        }
        return w;
    }

    static byte[] Body(World w)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Audit Works");
        var bytes = ms.ToArray();
        int header = BitConverter.ToInt32(bytes, 8);
        return bytes[(12 + header)..];
    }

    [Fact]
    public void EverythingSavesAndGoesOnIdenticallyForThreeThousandTicks()
    {
        var w = Everything();
        // What the world holds at the save, so the test proves it covers the brief's list.
        var machines = w.Buildings.OfType<Machine>().ToList();
        output.WriteLine(string.Join("; ", machines.Select(m => $"{m.Type}@{m.Origin} {m.Source} {m.State} run={m.Current?.Id}")));
        Assert.Contains(machines, m => m.Source == PowerSource.Firebox && m.Current != null);
        Assert.Contains(machines, m => m.Source == PowerSource.Steam);
        Assert.Contains(machines, m => m.Source == PowerSource.Shaft);
        Assert.Contains(machines, m => m.Source == PowerSource.Electric && m.Def.Power == PowerNeed.Elec);
        Assert.Contains(w.Power.ElecNets, n => n.Dams.Count > 0 && n.SupplyKw > 0);
        Assert.True(w.Buildings.OfType<Mine>().Single().Patch!.Id == -1 && w.PatchExtracted(-1) > 30_000);
        Assert.Contains(w.Buildings, b => b is LoggingCamp { Felled: > 0 });
        Assert.Contains(w.Buildings, b => b is PumpJack { Pumped: > 0 });
        Assert.Contains(w.Lines, l => l.Stalled);
        Assert.Contains(w.Buildings.OfType<Splitter>(), sp => !sp.Sorting && (sp.Lane(0).Count + sp.Lane(1).Count) > 0);
        Assert.Contains(w.Haulage.Fleet, v => v.Kind == VehicleKind.Truck && v.Delivered > 0);
        Assert.Contains(w.Haulage.Fleet, v => v.Kind == VehicleKind.Barge && v.Delivered > 0);
        var trains = w.Haulage.Fleet.Where(v => v.Kind == VehicleKind.Train).ToList();
        Assert.True(trains.All(v => v.Delivered > 0), "both trains ran");
        Assert.True(w.Plans.Count > 7 && w.Planning);
        Assert.True(w.Market.Events.Count >= 2 && w.Market.WaterOutputMilli < 1000);
        Assert.Equal(6, w.Paper.TutorialStep);
        Assert.Contains(w.Paper.Telegrams, t => t.State == TelegramState.Open);
        Assert.NotNull(w.Prestige.Current);
        Assert.True(w.Town.Blocks > 16, "the town grew");
        Assert.True(w.Flooded(w.FloodOf(w.Buildings.OfType<Dam>().Single().Origin, Dir.North).First()));
        output.WriteLine($"{w.Buildings.Count} buildings, {w.Lines.Count} lines, {w.Haulage.Fleet.Count} vehicles, {w.Plans.Count} plans, town {w.Town.Blocks} blocks, tick {w.TickCount}");

        var back = RoundTrip(w);
        // A loaded world saves to the very same body.
        Assert.Equal(Body(w), Body(back));
        SameFuture(w, back, 3_000, "everything");
        // Same behaviour, not only the same hash.
        Assert.Equal(w.CashCents, back.CashCents);
        Assert.Equal(w.Haulage.Fleet.Select(v => (v.Id, v.Head, v.State, v.Delivered)), back.Haulage.Fleet.Select(v => (v.Id, v.Head, v.State, v.Delivered)));
        Assert.Equal(w.Town.Tiles.OrderBy(c => c), back.Town.Tiles.OrderBy(c => c));
        Assert.Equal(w.Plans, back.Plans);
        // And both take the same commands afterwards: the pencil down, a build, an undo.
        foreach (var x in new[] { w, back })
        {
            x.Apply(new SetPlanning(false));
            x.PlaceOk(BuildingType.BeltCanvas, 0, -60);
            Assert.True(x.Undo());
        }
        SameFuture(w, back, 600, "after commands");
        Assert.True(w.Plans.Count < 8, "the plans went up");
    }

    [Fact]
    public void ACompanyOnCreditSavesThroughItsWeeklyInterestAndItsFold()
    {
        // Hard Times with $100 in hand: belts bought on credit until the line is nearly spent. (Saves name the
        // difficulty by its preset id, so the test uses a preset, as every company does.)
        static World Spent()
        {
            var s = new World(1, 10_000, Difficulty.All.Single(d => d.Id == "hard_times"));
            for (int x = 0; s.Place(BuildingType.BeltCanvas, x % 200 - 100, -60 - x / 200) == PlaceResult.Ok; x++) { }
            return s;
        }
        // Merged audit: purchases keep two weeks' interest in hand, so the fold comes weeks later, warned; find it.
        var probe = Spent();
        while (!probe.Defaulted && probe.TickCount < 8 * World.TicksPerWeek)
            probe.Tick();
        Assert.True(probe.Defaulted, "a drawn line with no income folds in the end");
        long foldTick = probe.TickCount;
        var w = Spent();
        Assert.True(w.DebtCents > 400_000 && w.CashCents == 0, $"debt {w.DebtCents}, cash {w.CashCents}");
        while (w.TickCount < foldTick - 400)
            w.Tick();
        var back = RoundTrip(w);
        Assert.Equal(w.DebtCents, back.DebtCents);
        Assert.Equal(w.CreditAvailableCents, back.CreditAvailableCents);
        SameFuture(w, back, 1_000, "credit");
        // The week's interest was beyond cash and credit: both fold on the same tick.
        Assert.True(w.Defaulted && back.Defaulted);
        Assert.Contains(back.Paper.Editions, e => e.Key == "FOLDS");
        // A folded company loads folded: it no longer ticks or takes commands.
        var folded = RoundTrip(w);
        Assert.True(folded.Defaulted);
        long tick = folded.TickCount;
        folded.Tick();
        Assert.Equal(tick, folded.TickCount);
        Assert.Equal(PlaceResult.Ended, folded.Place(BuildingType.BeltCanvas, 0, 0));
    }

    /// <summary>The file an rc1 build would have written for this world: the same layout without the extension block.</summary>
    static byte[] AsRc1(World w, int yieldEntries)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Audit Works");
        var file = ms.ToArray();
        int headerLen = BitConverter.ToInt32(file, 8);
        int bodyAt = 12 + headerLen + 4;
        int bodyLen = BitConverter.ToInt32(file, bodyAt - 4);
        using var raw = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(new MemoryStream(file, bodyAt, bodyLen), System.IO.Compression.CompressionMode.Decompress))
            brotli.CopyTo(raw);
        var plain = raw.ToArray();
        // Marker, revision, plan cursor, posted interest rate (mine/jack yields are derived from the saved seams, not saved).
        int extension = 4 + 4 + 4 + 4;
        _ = yieldEntries;
        Assert.Equal(0x31585846, BitConverter.ToInt32(plain, plain.Length - extension));
        using var packed = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(plain, 0, plain.Length - extension);
        var body = packed.ToArray();
        ulong h = 14695981039346656037UL;
        foreach (byte x in body)
        {
            h ^= x;
            h *= 1099511628211UL;
        }
        using var o = new MemoryStream();
        o.Write(file, 0, bodyAt - 4);
        o.Write(BitConverter.GetBytes(body.Length));
        o.Write(body);
        o.Write(BitConverter.GetBytes(h));
        return o.ToArray();
    }

    [Fact]
    public void AnRc1SaveWithoutTheExtensionBlockStillLoads()
    {
        var w = Rig.StarterLine(out _);
        w.Run(30);
        var old = SaveGame.Read(new MemoryStream(AsRc1(w, yieldEntries: 1)));
        Assert.Equal(w.TickCount, old.TickCount);
        Assert.Equal(w.CashCents, old.CashCents);
        Assert.Equal(w.Buildings.Count, old.Buildings.Count);
        old.Run(10);
        Assert.True(old.TickCount > w.TickCount);
    }
}
