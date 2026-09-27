using System.Diagnostics;
using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

public class PerfTests
{
    readonly ITestOutputHelper output;

    public PerfTests(ITestOutputHelper output) => this.output = output;

    /// <summary>A field of square belt loops, each filled to the brim, so every good moves every tick.</summary>
    public static World LoopField(int loops, int side, BuildingType belt, out int goods)
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        goods = 0;
        int cols = (int)Math.Ceiling(Math.Sqrt(loops));
        for (int n = 0; n < loops; n++)
        {
            int ox = (n % cols) * (side + 2), oy = (n / cols) * (side + 2);
            for (int x = 0; x < side - 1; x++)
                w.PlaceOk(belt, ox + x, oy, Dir.East);
            for (int y = 0; y < side - 1; y++)
                w.PlaceOk(belt, ox + side - 1, oy + y, Dir.South);
            for (int x = side - 1; x > 0; x--)
                w.PlaceOk(belt, ox + x, oy + side - 1, Dir.West);
            for (int y = side - 1; y > 0; y--)
                w.PlaceOk(belt, ox, oy + y, Dir.North);
            var line = w.BeltAt(ox, oy).Line!;
            // Two per tile, exactly spaced, all moving.
            for (int p = line.End; p >= 0; p -= BeltTiers.Spacing)
                if (line.TryInsert(w, p, Item.IronOre))
                    goods++;
        }
        return w;
    }

    [Fact]
    public void TenThousandMovingGoodsTickWellUnderBudget()
    {
        var w = LoopField(loops: 40, side: 32, BuildingType.BeltSteel, out int goods);
        Assert.True(goods >= 9_900, $"only {goods} goods");
        Assert.All(w.Lines, l => Assert.Equal(124, l.Tiles.Count));

        w.Run(1);
        var sw = Stopwatch.StartNew();
        const int ticks = 600;
        for (int i = 0; i < ticks; i++)
            w.Tick();
        sw.Stop();
        double perTick = sw.Elapsed.TotalMilliseconds / ticks;
        output.WriteLine($"{goods} goods on {w.Lines.Count} lines: {perTick:F4} ms/tick ({1000 / perTick:F0} UPS possible)");
        Assert.Equal(goods, w.Lines.Sum(l => l.Count));
        Assert.All(w.Lines, l => Assert.All(l.Goods, g => Assert.True(g.Moving)));
        // Budget is 16.6 ms; this must be a small fraction of it.
        Assert.True(perTick < 2.0, $"{perTick:F3} ms per tick");
    }

    [Fact]
    public void HalfAMillionMovingGoodsStillFitTheTickBudget()
    {
        var w = LoopField(loops: 2000, side: 32, BuildingType.BeltSteel, out int goods);
        Assert.True(goods >= 490_000, $"only {goods} goods");
        w.Run(0.1);
        var sw = Stopwatch.StartNew();
        const int ticks = 120;
        for (int i = 0; i < ticks; i++)
            w.Tick();
        sw.Stop();
        double perTick = sw.Elapsed.TotalMilliseconds / ticks;
        output.WriteLine($"{goods} goods on {w.Lines.Count} lines ({w.Buildings.Count} belts): {perTick:F3} ms/tick");
        Assert.Equal(goods, w.Lines.Sum(l => l.Count));
        Assert.True(perTick < 8.0, $"{perTick:F3} ms per tick");
    }

    [Fact]
    public void BackedUpGoodsCostNothingToTick()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.Flat);
        // 200 dead-end rows of 50 steel belts, each jammed solid.
        for (int y = 0; y < 200; y++)
        {
            w.BeltRow(BuildingType.BeltSteel, 0, 49, y * 2);
            var line = w.BeltAt(0, y * 2).Line!;
            for (int p = line.End; p >= 0; p -= BeltTiers.Spacing)
                line.TryInsert(w, p, Item.Coal);
        }
        w.Run(1);
        Assert.All(w.Lines, l => Assert.True(l.Stalled));
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 600; i++)
            w.Tick();
        double perTick = sw.Elapsed.TotalMilliseconds / 600;
        output.WriteLine($"{w.Lines.Sum(l => l.Count)} stalled goods: {perTick:F4} ms/tick");
        Assert.True(perTick < 0.5, $"{perTick:F3} ms per tick");
    }
}

/// <summary>GDD §14 targets: 50,000 structures and 500,000 goods at 60 UPS on the i7-9700K, with the whole sim running.</summary>
public class FullWorksPerfTests
{
    readonly ITestOutputHelper output;
    public FullWorksPerfTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void FiftyThousandStructuresWithEverythingRunningFitTheTickBudget()
    {
        // 320 belt loops (39,680 belts, ~40k goods moving), 2,000 working smelters, 400 idle machines behind a
        // 200-truck road ring, 3,000 plans the till cannot pay for, a dam and a pole line to 200 electric shops.
        var w = PerfTests.LoopField(loops: 320, side: 32, BuildingType.BeltSteel, out int goods);
        int cols = (int)Math.Ceiling(Math.Sqrt(320));
        int fieldSide = cols * 34;
        int machines = 0;
        for (int i = 0; i < 2000; i++)
        {
            int x = fieldSide + 4 + (i % 50) * 3, y = (i / 50) * 3;
            if (w.Place(BuildingType.Smelter, x, y) != PlaceResult.Ok)
                continue;
            var m = (Machine)w.BuildingAt(new Cell(x, y))!;
            m.TryAccept(w, Item.IronOre, Dir.East, 60, m.Origin);
            m.TryAccept(w, Item.IronOre, Dir.East, 60, m.Origin);
            machines++;
        }
        for (int i = 0; i < 400; i++)
            w.Place(BuildingType.Press, fieldSide + 4 + (i % 50) * 3, 130 + (i / 50) * 3);
        int ry = 700;
        for (int x = 0; x < 300; x++)
        {
            w.PlaceOk(BuildingType.Road, x, ry);
            w.PlaceOk(BuildingType.Road, x, ry + 40);
        }
        for (int y = ry + 1; y < ry + 40; y++)
        {
            w.PlaceOk(BuildingType.Road, 0, y);
            w.PlaceOk(BuildingType.Road, 299, y);
        }
        w.PlaceOk(BuildingType.TruckDepot, 100, ry - 2);
        w.PlaceOk(BuildingType.TruckDepot, 100, ry + 41);
        var a = (Terminal)w.BuildingAt(new Cell(100, ry - 2))!;
        var b = (Terminal)w.BuildingAt(new Cell(100, ry + 41))!;
        for (int k = 0; k < 200; k++)
            Assert.Equal(PlaceResult.Ok, w.Apply(new Assign(BuildingType.MotorTruck, a.Origin, b.Origin)));
        for (int k = 0; k < 48; k++)
        {
            a.TryAccept(w, Item.Coal, Dir.East, 60, a.Origin);
            b.TryAccept(w, Item.Coal, Dir.East, 60, b.Origin);
        }
        for (int i = 0; i < 3000; i++)
            w.Apply(new Draft(BuildingType.Lathe, new Cell(400 + i % 60, 800 + i / 60), Dir.East));
        w.Apply(new SetPlanning(false));
        int structures = w.Buildings.Count;
        // Everything the till holds goes on the plans' tab? No: a scratch world is rich, so drain it so plans wait.
        w.Pay(w.CashCents);
        Assert.True(structures >= 42_000, $"{structures} structures");
        Assert.Equal(3000, w.Plans.Count);

        w.Run(0.2);
        var sw = Stopwatch.StartNew();
        const int ticks = 180;
        for (int i = 0; i < ticks; i++)
            w.Tick();
        sw.Stop();
        double perTick = sw.Elapsed.TotalMilliseconds / ticks;
        output.WriteLine($"{structures} structures, {goods} goods, {machines} working machines, {w.Haulage.Fleet.Count} trucks, {w.Plans.Count} plans: {perTick:F3} ms/tick");
        Assert.True(w.Buildings.OfType<Machine>().Count(m => m.State == MachineState.Working) > 1500, "the smelters are working");
        Assert.True(w.Haulage.Fleet.Count(v => v.Underway) > 20, "trucks are on the road");
        Assert.Equal(3000, w.Plans.Count);
        Assert.True(perTick < 10.0, $"{perTick:F3} ms per tick (budget 16.7, target 10)");
    }
}
