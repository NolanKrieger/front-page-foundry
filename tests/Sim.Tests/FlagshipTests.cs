using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>
/// The whole recipe graph, end to end, on real machines and real power. Belts are proven elsewhere,
/// so a scripted "yard crew" moves finished goods to whichever machine wants them and a bottomless
/// dock supplies every raw. Proves an automobile and an aeroplane come out, and records how long.
/// </summary>
public class FlagshipTests
{
    readonly ITestOutputHelper output;
    public FlagshipTests(ITestOutputHelper output) => this.output = output;

    static readonly (BuildingType Type, int Copies)[] Works =
    {
        (BuildingType.Smelter, 3), (BuildingType.CokeOven, 2), (BuildingType.OpenHearthFurnace, 2), (BuildingType.Glassworks, 2),
        (BuildingType.Kiln, 1), (BuildingType.Sawmill, 1), (BuildingType.Refinery, 1), (BuildingType.Vulcanizer, 1),
        (BuildingType.TextileMill, 1), (BuildingType.ChemicalWorks, 2), (BuildingType.Press, 3), (BuildingType.RollingMill, 1),
        (BuildingType.DrawingMill, 3), (BuildingType.TubeMill, 2), (BuildingType.Lathe, 3), (BuildingType.Coiler, 1),
        (BuildingType.PrecisionShop, 3), (BuildingType.Foundry, 2), (BuildingType.BoringMill, 1), (BuildingType.DropForge, 1),
        (BuildingType.Joinery, 3), (BuildingType.DopingShed, 2), (BuildingType.WireCoater, 1), (BuildingType.RubberWorks, 1),
        (BuildingType.TireWorks, 1), (BuildingType.SheetMetalShop, 2), (BuildingType.Wheelwright, 1), (BuildingType.Upholstery, 1),
        (BuildingType.FrameShop, 2), (BuildingType.DrivetrainShop, 1), (BuildingType.EngineWorks, 1), (BuildingType.ChassisShop, 1),
        (BuildingType.BodyShop, 1),
    };

    static readonly BuildingType[] Electric =
    {
        BuildingType.ReductionWorks, BuildingType.ElectricalShop, BuildingType.ElectricalShop, BuildingType.AirframeWorks,
        BuildingType.FinalAssemblyLine, BuildingType.AircraftHangar,
    };

    [Fact]
    public void AnAutomobileAndAnAeroplaneComeOutOfTheWholeChain()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        // Coal-fired works in rows well north of the river.
        int col = 0, row = 0;
        foreach (var (type, copies) in Works)
            for (int k = 0; k < copies; k++)
            {
                w.PlaceOk(type, 100 + col * 4, -80 + row * 4);
                if (++col == 10)
                {
                    col = 0;
                    row++;
                }
            }

        // A dam on the river, poles north to a row of electric shops.
        int x0 = 150;
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x0));
        Cell? dam = null;
        for (int dx = 0; dx < 60 && dam == null; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x0 + dx, oy), Dir.North) == PlaceResult.Ok)
                {
                    dam = new Cell(x0 + dx, oy);
                    break;
                }
        Assert.NotNull(dam);
        w.PlaceOk(BuildingType.HydroDam, dam!.Value.X, dam.Value.Y);
        int px = dam.Value.X + 3, py = dam.Value.Y - 4;
        while (py > -40)
        {
            w.PlaceOk(BuildingType.PowerPole, px, py);
            py -= 5;
        }
        for (int j = 0; j < Electric.Length; j++)
        {
            w.PlaceOk(BuildingType.PowerPole, px + 4 * j, -40);
            w.PlaceOk(BuildingType.PowerPole, px + 4 * j, -45);
            w.PlaceOk(Electric[j], px + 1 + 4 * j, -44);
        }
        w.Tick();
        var grid = Assert.Single(w.Power.ElecNets);
        Assert.Single(grid.Dams);
        Assert.Equal(Electric.Length, grid.Machines.Count);

        var machines = w.Buildings.OfType<Machine>().ToList();
        var raws = Items.All.Where(i => Items.Of(i).Tier == Tier.Raw).ToArray();
        // The yard crew works from a bill of materials: a good goes to a machine only for a recipe the
        // flagships need, and only up to twice what that recipe will consume, so no press turns every
        // iron ingot into plate and no lathe turns every wire into rivets (a player routes belts to
        // the same effect).
        var allowance = Allowances(BillOfMaterials(Item.Automobile, Item.Aeroplane), slack: 2);
        var delivered = new Dictionary<(Item, BuildingType), long>();
        bool Deliver(Machine m, Item item)
        {
            var key = (item, m.Type);
            bool wanted = item == Item.Coal || allowance.GetValueOrDefault(key) > delivered.GetValueOrDefault(key);
            if (!wanted || !m.TryAccept(w, item, Dir.East, 60, m.Origin))
                return false;
            delivered[key] = delivered.GetValueOrDefault(key) + 1;
            return true;
        }
        var pool = new Dictionary<Item, long>();
        var cursor = new Dictionary<Item, int>();
        long carAt = -1, planeAt = -1;
        for (int t = 0; t < 60 * 60 * World.TicksPerSecond; t++)
        {
            // The dock: every raw, as much as the bill allows and the machine will hold.
            if (t % 10 == 0)
                foreach (var m in machines)
                    foreach (var raw in raws)
                        while (Deliver(m, raw)) { }
            // The yard crew: finished goods to whoever the bill says wants them, in turn; the rest waits.
            foreach (var m in machines)
                while (m.TakeOutput() is { } item)
                    pool[item] = pool.GetValueOrDefault(item) + 1;
            foreach (var item in pool.Keys.ToList())
            {
                while (pool[item] > 0)
                {
                    int start = cursor.GetValueOrDefault(item);
                    bool placed = false;
                    for (int k = 0; k < machines.Count && !placed; k++)
                    {
                        int i = (start + k) % machines.Count;
                        if (Deliver(machines[i], item))
                        {
                            placed = true;
                            cursor[item] = (i + 1) % machines.Count;
                        }
                    }
                    if (!placed)
                        break;
                    pool[item]--;
                }
            }
            w.Tick();
            if (carAt < 0 && pool.GetValueOrDefault(Item.Automobile) > 0)
            {
                carAt = w.TickCount;
                w.Sell(Item.Automobile);
            }
            if (planeAt < 0 && pool.GetValueOrDefault(Item.Aeroplane) > 0)
            {
                planeAt = w.TickCount;
                w.Sell(Item.Aeroplane);
            }
            if (carAt > 0 && planeAt > 0)
                break;
        }
        output.WriteLine($"{machines.Count} machines; first automobile at {carAt / World.TicksPerSecond} s, first aeroplane at {planeAt / World.TicksPerSecond} s");
        foreach (var m in machines.Where(m => m.Runs == 0))
            output.WriteLine($"  {m.Type}: {m.State} holds=[{string.Join(" ", m.Inputs.Select(i => $"{i.Item}:{i.Count}"))}]");
        output.WriteLine("yard: " + string.Join(", ", pool.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(carAt > 0, "no automobile in an hour");
        Assert.True(planeAt > 0, "no aeroplane in an hour");
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_CAR");
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_PLANE");
        Assert.All(machines, m => Assert.True(m.Runs > 0, $"{m.Type} never ran"));
    }

    /// <summary>Total goods to make one of each wanted item, expanded through the recipe graph (whole runs at every step).</summary>
    internal static Dictionary<Item, long> BillOfMaterials(params Item[] wanted)
    {
        var need = new Dictionary<Item, long>();
        void Add(Item item, long count)
        {
            need[item] = need.GetValueOrDefault(item) + count;
            var r = Recipes.Producing(item);
            if (r == null)
                return;
            int per = Array.Find(r.Outputs, o => o.Item == item).Count;
            long runs = (count + per - 1) / per;
            foreach (var (input, each) in r.Inputs)
                Add(input, runs * each);
        }
        foreach (var item in wanted)
            Add(item, 1);
        return need;
    }

    /// <summary>How many of each good a machine type may be handed: slack × what its needed recipes consume for the bill.</summary>
    internal static Dictionary<(Item, BuildingType), long> Allowances(Dictionary<Item, long> need, int slack)
    {
        var allow = new Dictionary<(Item, BuildingType), long>();
        foreach (var r in Recipes.All)
        {
            long runs = 0;
            foreach (var (item, per) in r.Outputs)
                if (need.TryGetValue(item, out long n))
                    runs = Math.Max(runs, (n + per - 1) / per);
            if (runs == 0)
                continue;
            foreach (var (input, each) in r.Inputs)
            {
                var key = (input, r.Machine);
                allow[key] = allow.GetValueOrDefault(key) + slack * runs * each;
            }
        }
        return allow;
    }

    [Fact]
    public void TheBillOfMaterialsReachesEveryRawTheFlagshipsNeed()
    {
        var need = BillOfMaterials(Item.Automobile, Item.Aeroplane);
        Assert.Equal(1, need[Item.Automobile]);
        Assert.Equal(1, need[Item.Aeroplane]);
        Assert.Equal(22, need[Item.SparkPlug]);
        Assert.Equal(13, need[Item.Piston]);
        Assert.True(need[Item.IronOre] > 100, $"iron ore {need[Item.IronOre]}");
        Assert.True(need.ContainsKey(Item.Bauxite) && need.ContainsKey(Item.CrudeOil) && need.ContainsKey(Item.Cotton));
        Assert.False(need.ContainsKey(Item.Rivets));
        Assert.False(need.ContainsKey(Item.Cement));
        var allow = Allowances(need, slack: 2);
        Assert.Equal(2 * 2, allow[(Item.IronCasting, BuildingType.BoringMill)]);
        Assert.False(allow.ContainsKey((Item.SteelWire, BuildingType.Lathe)));
    }
}

/// <summary>The rest of M8: the truck good, dock imports, and every machine's ports and caps fitting its recipes.</summary>
public class ChainContentTests
{
    [Fact]
    public void AMotorTruckComesOffTheFinalAssemblyLineAndMakesTheFrontPage()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        int x0 = 150;
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x0));
        Cell? dam = null;
        for (int dx = 0; dx < 60 && dam == null; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x0 + dx, oy), Dir.North) == PlaceResult.Ok)
                {
                    dam = new Cell(x0 + dx, oy);
                    break;
                }
        Assert.NotNull(dam);
        w.PlaceOk(BuildingType.HydroDam, dam!.Value.X, dam.Value.Y);
        var pole = new Cell(dam.Value.X + 3, dam.Value.Y - 4);
        w.PlaceOk(BuildingType.PowerPole, pole.X, pole.Y);
        var at = new Cell(pole.X + 1, pole.Y - 4);
        w.PlaceOk(BuildingType.FinalAssemblyLine, at.X, at.Y);
        var line = (Machine)w.BuildingAt(at)!;
        w.Tick();
        Assert.Contains(line, Assert.Single(w.Power.ElecNets).Machines);

        var truck = Recipes.All.Single(r => r.Outputs[0].Item == Item.MotorTruck);
        Assert.Equal(BuildingType.FinalAssemblyLine, truck.Machine);
        foreach (var (item, count) in truck.Inputs)
            for (int k = 0; k < count; k++)
                Assert.True(line.TryAccept(w, item, Dir.East, 60, at));
        w.Run(truck.CraftTicks / World.TicksPerSecond + 1);
        Assert.Equal(1, line.Runs);
        Assert.Equal(Item.MotorTruck, line.TakeOutput());
        Assert.Equal(Tier.Flagships, Items.Of(Item.MotorTruck).Tier);
        w.Run(World.TicksPerDay / World.TicksPerSecond); // one front page a day: the story waits for tomorrow's edition
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_TRUCK");
        Assert.DoesNotContain(w.Paper.Editions, e => e.Key == "FIRST_CAR");
    }

    [Fact]
    public void TheDockImportsRubberAndCottonAtTheBuyPrice()
    {
        var w = new World(1, 500_000);
        w.PlaceOk(BuildingType.ReceivingDock, 0, 0, Dir.East);
        var dock = (Dock)w.BuildingAt(new Cell(0, 0))!;
        w.BeltRow(BuildingType.BeltSteel, 2, 4, 1);
        Assert.True(w.Market.BuyPriceCents(Item.RawRubber) > w.Market.BuyPriceCents(Item.Coal), "imports cost more than pit coal");
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetOrder(new Cell(0, 0), Item.RawRubber)));
        w.Run(3);
        var line = w.BeltAt(2, 1).Line!;
        Assert.Equal(6, dock.UnitsBought);
        Assert.All(line.Goods, g => Assert.Equal(Item.RawRubber, g.Item));
        Assert.True(dock.SpentCents > 0);
        Assert.Equal(cash - dock.SpentCents, w.CashCents);
        Assert.Equal(dock.SpentCents, w.SpentCents);

        Assert.Equal(PlaceResult.Ok, w.Apply(new SetOrder(new Cell(0, 0), Item.Cotton)));
        w.PlaceOk(BuildingType.FreightDepot, 5, 0);
        w.Run(4);
        Assert.Contains(line.Goods, g => g.Item == Item.Cotton);
        Assert.True(dock.UnitsBought > 6);
    }

    [Fact]
    public void EveryMachinesPortsAndCapsFitItsRecipes()
    {
        foreach (var type in Enum.GetValues<BuildingType>())
        {
            var recipes = Recipes.Of(type);
            if (recipes.Count == 0)
                continue;
            var (_, h) = Catalog.Footprint(type, Dir.East);
            Assert.True(Recipes.PortCount(type) <= h, $"{type}: {Recipes.PortCount(type)} ports on a {h}-cell front edge");
            foreach (var r in recipes)
            {
                foreach (var (item, count) in r.Inputs)
                    Assert.True(Recipes.InputCap(type, item) >= 2 * count, $"{type} cap for {item}");
                Assert.True(Recipes.OutputCap(type) >= 2 * r.OutputCount, $"{type} output cap");
                Assert.True(r.CraftTicks > 0, $"{r.Id} takes no time");
            }
        }
    }
}

public class HarvestTests
{
    static Cell FindForest(World w)
    {
        for (int r = 20; r < 200; r++)
            for (int x = -r; x <= r; x += 2)
                foreach (int y in new[] { -r, r })
                    if (w.TerrainAt(new Cell(x, y)) == Terrain.Forest && w.CanPlace(BuildingType.LoggingCamp, new Cell(x + 1, y + 1), Dir.East) == PlaceResult.Ok)
                        return new Cell(x + 1, y + 1);
        throw new InvalidOperationException("no forest");
    }

    [Fact]
    public void ALoggingCampFellsTimberInProportionToTheTreesAround()
    {
        var w = Rig.Fresh();
        Assert.Equal(PlaceResult.NeedsForest, w.Place(BuildingType.LoggingCamp, -14, -10));
        var at = FindForest(w);
        w.PlaceOk(BuildingType.LoggingCamp, at.X, at.Y);
        var camp = (LoggingCamp)w.BuildingAt(at)!;
        w.PlaceOk(BuildingType.FreightDepot, camp.OutputCell.X, camp.OutputCell.Y - 1);
        Assert.True(camp.Trees > 0);
        w.Run(120);
        double expected = 120 * Math.Min(1.0, camp.Trees / (double)LoggingCamp.FullTrees);
        Assert.InRange(camp.Felled, expected * 0.9 - 1, expected + 1);
        Assert.True(w.Market.Glut(Item.Timber) > 0);
    }

    [Fact]
    public void APumpJackNeedsASeepAndRaisesCrude()
    {
        var w = Rig.Fresh(seed: 4);
        Assert.Equal(PlaceResult.NeedsOil, w.Place(BuildingType.PumpJack, -14, -10));
        Patch? seep = null;
        for (int cx = -12; cx <= 12 && seep == null; cx++)
            for (int cy = -12; cy <= 12 && seep == null; cy++)
                seep = w.Map.PatchesIn(cx, cy).FirstOrDefault(p => p.Kind == Terrain.OilSeep);
        Assert.NotNull(seep);
        var c = seep!.Centre;
        Cell? spot = null;
        for (int dy = -1; dy <= 0 && spot == null; dy++)
            for (int dx = -1; dx <= 0; dx++)
                if (w.CanPlace(BuildingType.PumpJack, new Cell(c.X + dx, c.Y + dy), Dir.East) == PlaceResult.Ok)
                {
                    spot = new Cell(c.X + dx, c.Y + dy);
                    break;
                }
        Assert.NotNull(spot);
        w.PlaceOk(BuildingType.PumpJack, spot!.Value.X, spot.Value.Y);
        var jack = (PumpJack)w.BuildingAt(spot.Value)!;
        Assert.True(jack.SeepTiles >= 3);
        Assert.NotNull(jack.Patch);
        w.PlaceOk(BuildingType.FreightDepot, jack.OutputCell.X, jack.OutputCell.Y - 1);
        w.Run(60);
        Assert.True(jack.Pumped >= 40, $"pumped {jack.Pumped}");
        Assert.Equal(jack.Pumped, w.PatchExtracted(jack.Patch!.Id));
    }
}
