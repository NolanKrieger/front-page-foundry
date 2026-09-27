using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class PowerTests
{
    /// <summary>A north-bank cell at or east of column x with a clear 12×12 of ground behind it.</summary>
    static Cell NorthBank(World w, int x) => w.FlatBankNear(x, 12, 12);

    static World River(int seed = 1) => new(seed, Rig.Rich, mode: MapMode.RiverOnly);

    /// <summary>A smelter facing east with a depot on its port, so it never blocks.</summary>
    static Machine Smelter(World w, int x, int y)
    {
        w.PlaceOk(BuildingType.Smelter, x, y);
        w.PlaceOk(BuildingType.FreightDepot, x + 2, y);
        return (Machine)w.BuildingAt(new Cell(x, y))!;
    }

    static void Feed(World w, Machine m, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            m.TryAccept(w, Item.IronOre, Dir.East, 60, m.Origin);
            w.Tick();
        }
    }

    [Fact]
    public void WaterwheelNeedsABankAndDrivesWhatTouchesItAndOneBeyond()
    {
        var w = River();
        Assert.Equal(PlaceResult.NeedsBank, w.Place(BuildingType.Waterwheel, 0, 0, Dir.North));
        var bank = NorthBank(w, 60);
        w.PlaceOk(BuildingType.Waterwheel, bank.X, bank.Y - 1, Dir.North);      // 1×2 standing on the bank
        var a = Smelter(w, bank.X + 1, bank.Y - 2);                             // touches the wheel
        var b = Smelter(w, bank.X + 1, bank.Y - 4);                             // touches a: one beyond
        var c = Smelter(w, bank.X + 1, bank.Y - 6);                             // touches b only: on its own coal
        w.Tick();
        Assert.Equal(2, w.Power.WheelsOf(a).Count + w.Power.WheelsOf(b).Count);
        Assert.Empty(w.Power.WheelsOf(c));
        int coalA = a.Firebox!.Coal, coalC = c.Firebox!.Coal;
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
        {
            foreach (var m in new[] { a, b, c })
                m.TryAccept(w, Item.IronOre, Dir.East, 60, m.Origin);
            w.Tick();
        }
        Assert.Equal(PowerSource.Shaft, a.Source);
        Assert.Equal(PowerSource.Shaft, b.Source);
        Assert.Equal(PowerSource.Firebox, c.Source);
        Assert.Equal(coalA, a.Firebox.Coal);
        Assert.True(c.Firebox.Coal < coalC, "the far smelter burns its own coal");
        Assert.True(a.Runs >= 28 && b.Runs >= 28, $"a {a.Runs} b {b.Runs} runs on river power");
    }

    [Fact]
    public void ShortageSlowsEveryLoadOnTheWheel()
    {
        var w = River();
        var bank = NorthBank(w, 80);
        w.PlaceOk(BuildingType.Waterwheel, bank.X, bank.Y - 1, Dir.North);
        // 30 kW wheel; an open hearth draws 30 and a smelter 8: together they get 30/38.
        w.PlaceOk(BuildingType.OpenHearthFurnace, bank.X + 1, bank.Y - 3);      // touches the wheel; port (x+4, y-2)
        var hearth = (Machine)w.BuildingAt(new Cell(bank.X + 1, bank.Y - 3))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X + 4, bank.Y - 3);
        w.PlaceOk(BuildingType.Smelter, bank.X - 2, bank.Y - 2, Dir.West);       // touches the wheel; port (x-3, y-2)
        var smelter = (Machine)w.BuildingAt(new Cell(bank.X - 2, bank.Y - 2))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X - 4, bank.Y - 3);
        for (int t = 0; t < 80 * World.TicksPerSecond; t++)
        {
            hearth.TryAccept(w, Item.IronIngot, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Coke, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Limestone, Dir.East, 60, hearth.Origin);
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        // Full speed would be 10 steel and 40 ingots; at 79% expect about 8 and 31.
        Assert.InRange(hearth.Runs, 6, 9);
        Assert.InRange(smelter.Runs, 26, 34);
        Assert.Equal(PowerSource.Shaft, hearth.Source);
    }

    [Fact]
    public void SteamMainsNeedWaterAndBurnHalfTheCoal()
    {
        var w = River();
        var bank = NorthBank(w, 100);
        // Pump on the bank, a pipe run north, a boiler and a smelter on the mains.
        w.PlaceOk(BuildingType.WaterPump, bank.X, bank.Y, Dir.North);
        for (int y = bank.Y - 1; y >= bank.Y - 6; y--)
            w.PlaceOk(BuildingType.SteamPipe, bank.X, y);
        w.PlaceOk(BuildingType.Boiler, bank.X + 1, bank.Y - 5);                 // beside the pipe at (x, y-4)
        w.PlaceOk(BuildingType.Smelter, bank.X - 2, bank.Y - 7, Dir.West);       // beside the pipe at (x, y-6); port (x-3, y-7)
        var smelter = (Machine)w.BuildingAt(new Cell(bank.X - 2, bank.Y - 7))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X - 4, bank.Y - 8);
        var boiler = (Boiler)w.BuildingAt(new Cell(bank.X + 1, bank.Y - 5))!;
        w.Tick();
        var net = Assert.Single(w.Power.SteamNets);
        Assert.Single(net.Boilers);
        Assert.Single(net.Pumps);
        Assert.Contains(smelter, net.Machines);
        Assert.Equal(1000, net.WaterMilli);

        int fireboxCoal = smelter.Firebox!.Coal;
        int boilerCoal = boiler.Coal;
        Feed(w, smelter, 300 * World.TicksPerSecond);
        Assert.Equal(PowerSource.Steam, smelter.Source);
        Assert.Equal(fireboxCoal, smelter.Firebox.Coal);
        // 8 kW for 300 s = 2400 kW·s: 20 lumps in a firebox, 10 in a boiler.
        Assert.InRange(boilerCoal - boiler.Coal, 9, 11);
        Assert.True(smelter.Runs >= 140, $"{smelter.Runs} runs");

        // No pump: no steam. The smelter falls back to its own fire.
        w.Apply(new Remove(bank));
        w.Tick();
        Assert.Equal(0, w.Power.SteamNets[0].WaterMilli);
        Feed(w, smelter, 10 * World.TicksPerSecond);
        Assert.Equal(PowerSource.Firebox, smelter.Source);
    }

    [Fact]
    public void ElectricityRunsElectricShopsFasterAndBeatsSteamOnCoal()
    {
        var w = River();
        var bank = w.FlatBankNear(120, 34, 14);
        w.PlaceOk(BuildingType.WaterPump, bank.X, bank.Y, Dir.North);
        w.PlaceOk(BuildingType.SteamPipe, bank.X, bank.Y - 1);
        w.PlaceOk(BuildingType.SteamPipe, bank.X + 1, bank.Y - 1);
        w.PlaceOk(BuildingType.Boiler, bank.X + 1, bank.Y - 3);                 // (x+1..x+2, y-3..y-2), above the pipe at (x+1, y-1)
        w.PlaceOk(BuildingType.PowerStation, bank.X + 3, bank.Y - 4);           // (x+3..x+5, y-4..y-2), beside the boiler
        w.PlaceOk(BuildingType.PowerPole, bank.X + 6, bank.Y - 6);
        w.PlaceOk(BuildingType.PowerPole, bank.X + 11, bank.Y - 6);
        w.PlaceOk(BuildingType.ElectricalShop, bank.X + 12, bank.Y - 9);       // within 6 of the second pole; port (x+14, y-8)
        var shop = (Machine)w.BuildingAt(new Cell(bank.X + 12, bank.Y - 9))!;
        w.PlaceOk(BuildingType.FreightDepot, bank.X + 14, bank.Y - 9);
        var smelter = Smelter(w, bank.X + 7, bank.Y - 9);                        // within 6 of the first pole: prefers electricity
        var boiler = (Boiler)w.BuildingAt(new Cell(bank.X + 1, bank.Y - 3))!;
        w.Tick();
        var grid = Assert.Single(w.Power.ElecNets);
        Assert.Equal(2, grid.Poles.Count);
        Assert.Single(grid.Stations);
        Assert.Contains(shop, grid.Machines);
        Assert.Contains(smelter, grid.Machines);

        int boilerCoal = boiler.Coal;
        for (int t = 0; t < 120 * World.TicksPerSecond; t++)
        {
            shop.TryAccept(w, Item.InsulatedWire, Dir.East, 60, shop.Origin);
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        Assert.Equal(PowerSource.Electric, shop.Source);
        Assert.Equal(PowerSource.Electric, smelter.Source);
        // A quarter faster: 5 s harnesses become 4 s, 2 s ingots 1.6 s.
        Assert.InRange(shop.Runs, 28, 31);
        Assert.InRange(smelter.Runs, 72, 76);
        // 18 kW for 120 s = 2160 kW·s of electricity = 1440 kW·s of steam = 6 lumps; a firebox would burn 18.
        Assert.InRange(boilerCoal - boiler.Coal, 5, 8);
        Assert.Equal(Firebox.StarterCoal, smelter.Firebox!.Coal);
    }

    [Fact]
    public void TheDamStandsAcrossTheRiverFloodsTheValleyAndFeedsTheGrid()
    {
        var w = River();
        int x = 150;
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
        Assert.Equal(PlaceResult.NeedsRiver, w.Place(BuildingType.HydroDam, x, centre - 20));
        // Find a 3×3 placement covering the river fully in all three columns.
        Cell? origin = null;
        for (int dx = 0; dx < 40 && origin == null; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x + dx, oy), Dir.North) == PlaceResult.Ok)
                {
                    origin = new Cell(x + dx, oy);
                    break;
                }
        Assert.NotNull(origin);
        var flood = w.FloodOf(origin!.Value, Dir.North).ToList();
        Assert.True(flood.Count > 100, $"flood covers {flood.Count}");
        // Something built in the valley blocks the dam until it is cleared.
        var inValley = flood.First(c => w.CanPlace(BuildingType.BeltCanvas, c, Dir.East) == PlaceResult.Ok);
        w.PlaceOk(BuildingType.BeltCanvas, inValley.X, inValley.Y);
        Assert.Equal(PlaceResult.FloodBlocked, w.CanPlace(BuildingType.HydroDam, origin.Value, Dir.North));
        w.Apply(new Remove(inValley));
        w.PlaceOk(BuildingType.HydroDam, origin.Value.X, origin.Value.Y);
        Assert.Equal(Terrain.River, w.TerrainAt(inValley));
        Assert.True(w.Flooded(inValley));

        // Poles from the dam to a reduction works: 60 kW of the 400 on offer.
        w.PlaceOk(BuildingType.PowerPole, origin.Value.X + 3, origin.Value.Y - 5);
        w.PlaceOk(BuildingType.PowerPole, origin.Value.X + 3, origin.Value.Y - 10);
        w.PlaceOk(BuildingType.ReductionWorks, origin.Value.X + 4, origin.Value.Y - 14);   // port (x+7, y-13)
        var works = (Machine)w.BuildingAt(new Cell(origin.Value.X + 4, origin.Value.Y - 14))!;
        w.PlaceOk(BuildingType.FreightDepot, origin.Value.X + 7, origin.Value.Y - 14);
        for (int t = 0; t < 40 * World.TicksPerSecond; t++)
        {
            works.TryAccept(w, Item.Bauxite, Dir.East, 60, works.Origin);
            works.TryAccept(w, Item.Coke, Dir.East, 60, works.Origin);
            w.Tick();
        }
        Assert.Equal(PowerSource.Electric, works.Source);
        Assert.InRange(works.Runs, 4, 5);
        Assert.Equal(400, w.Power.ElecNets[0].SupplyKw);
        Assert.Contains(w.Paper.Editions, e => e.Key == "DAM");

        // Removing the dam drains the lake.
        w.Apply(new Remove(origin.Value));
        Assert.False(w.Flooded(inValley));
        Assert.NotEqual(Terrain.River, w.TerrainAt(inValley));
    }

    [Fact]
    public void DroughtHalvesRiverPowerAndNetworksAreDeterministic()
    {
        static World Play()
        {
            var w = River(9);
            var bank = NorthBank(w, 70);
            w.PlaceOk(BuildingType.Waterwheel, bank.X, bank.Y - 1, Dir.North);
            var a = Smelter(w, bank.X + 1, bank.Y - 2);
            for (int t = 0; t < 20 * World.TicksPerSecond; t++)
            {
                a.TryAccept(w, Item.IronOre, Dir.East, 60, a.Origin);
                w.Tick();
            }
            return w;
        }
        var w = Play();
        var wheel = w.Buildings.OfType<Waterwheel>().First();
        Assert.Equal(30, wheel.SuppliedKw);
        w.StartEvent(MarketEvent.All.First(e => e.Id == "drought"));
        w.Tick();
        Assert.Equal(15, wheel.SuppliedKw);
        Assert.Equal(Play().StateHash(), Play().StateHash());
    }
}
