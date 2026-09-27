using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>
/// M14: the money curve to the aeroplane, as a model over the real tables. A company grows in stages;
/// each stage's works is bought from the previous stage's income (the market glut makes a flooded good
/// worth less), and every piece takes the player time to lay. The estimate is printed and bounded so
/// tuning is a deliberate act; the real curve is Nolan's playtests (GDD §15 M14).
/// </summary>
public class BalanceTests
{
    readonly ITestOutputHelper output;
    public BalanceTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Real seconds per in-game hour, and how many hours a unit of glut lingers (the market's τ = 2 days).</summary>
    const double SecondsPerHour = 120.0 / 24, GlutHours = 1 / (1 - 1003.0 / 1024);
    /// <summary>Pieces a player lays a minute, thinking time included (assumption).</summary>
    public const double PiecesPerMinute = 2.5;
    public const long BeltCents = 900, LandCents = 200;

    record Stage(string Name, (BuildingType Type, int Count)[] Machines, int Belts, (Item Item, double PerSecond)[] Sells);

    static readonly Stage[] Stages =
    {
        new("Iron ore", new[] { (BuildingType.MineHead, 4), (BuildingType.FreightDepot, 2) }, 100, new[] { (Item.IronOre, 4.0) }),
        new("Ingots", new[] { (BuildingType.Smelter, 4) }, 100, new[] { (Item.IronIngot, 4.0) }),
        new("Steel", new[] { (BuildingType.MineHead, 3), (BuildingType.CokeOven, 2), (BuildingType.OpenHearthFurnace, 2), (BuildingType.Press, 2), (BuildingType.RollingMill, 1), (BuildingType.DrawingMill, 2), (BuildingType.FreightDepot, 2) }, 375,
            new[] { (Item.IronIngot, 2.0), (Item.SteelPlate, 0.4), (Item.SteelBeam, 0.1), (Item.SteelRod, 0.6), (Item.SteelWire, 0.6), (Item.IronPlate, 0.5) }),
        new("Diversify", new[] { (BuildingType.MineHead, 4), (BuildingType.LoggingCamp, 1), (BuildingType.Sawmill, 1), (BuildingType.Kiln, 1), (BuildingType.Glassworks, 2), (BuildingType.ReceivingDock, 2), (BuildingType.TextileMill, 1), (BuildingType.Vulcanizer, 1), (BuildingType.PumpJack, 1), (BuildingType.Refinery, 1), (BuildingType.ChemicalWorks, 1), (BuildingType.FreightDepot, 3) }, 550,
            new[] { (Item.CopperIngot, 1.0), (Item.Glass, 0.6), (Item.Lumber, 1.0), (Item.Porcelain, 0.4), (Item.Cloth, 0.4), (Item.Rubber, 0.5), (Item.Gasoline, 0.5), (Item.Varnish, 0.4), (Item.SteelPlate, 0.4), (Item.SteelRod, 0.6), (Item.SteelWire, 0.6) }),
        new("Parts", new[] { (BuildingType.Lathe, 3), (BuildingType.Coiler, 1), (BuildingType.PrecisionShop, 2), (BuildingType.Foundry, 2), (BuildingType.TubeMill, 2), (BuildingType.Joinery, 2), (BuildingType.WireCoater, 1), (BuildingType.RubberWorks, 1), (BuildingType.TireWorks, 1), (BuildingType.DopingShed, 1), (BuildingType.FreightDepot, 2) }, 650,
            new[] { (Item.Bolts, 2.0), (Item.Gears, 0.6), (Item.Bearings, 0.4), (Item.Springs, 0.3), (Item.IronCasting, 0.2), (Item.Tire, 0.15), (Item.Plywood, 0.25), (Item.InsulatedWire, 0.5), (Item.SteelTube, 0.3), (Item.Glass, 0.6), (Item.Lumber, 1.0) }),
        new("Power", new[] { (BuildingType.Waterwheel, 2), (BuildingType.WaterPump, 1), (BuildingType.Boiler, 2), (BuildingType.PowerStation, 1), (BuildingType.PowerPole, 30), (BuildingType.SteamPipe, 20), (BuildingType.MineHead, 1), (BuildingType.ReductionWorks, 1) }, 200,
            new[] { (Item.AluminumIngot, 0.1), (Item.Bolts, 2.0), (Item.Gears, 0.6), (Item.Bearings, 0.4), (Item.Springs, 0.3), (Item.Tire, 0.15), (Item.InsulatedWire, 0.5), (Item.SteelTube, 0.3) }),
        new("Components", new[] { (BuildingType.BoringMill, 1), (BuildingType.DropForge, 1), (BuildingType.ElectricalShop, 2), (BuildingType.SheetMetalShop, 2), (BuildingType.PrecisionShop, 1), (BuildingType.Wheelwright, 1), (BuildingType.Upholstery, 1), (BuildingType.FrameShop, 2), (BuildingType.DrivetrainShop, 1), (BuildingType.FreightDepot, 2) }, 800,
            new[] { (Item.SparkPlug, 0.33), (Item.Magneto, 0.1), (Item.Headlamp, 0.15), (Item.Radiator, 0.1), (Item.Wheel, 0.15), (Item.Seat, 0.15), (Item.Axle, 0.2), (Item.Transmission, 0.1), (Item.Carburetor, 0.15), (Item.Bolts, 1.0), (Item.Gears, 0.3) }),
        new("Assemblies", new[] { (BuildingType.EngineWorks, 1), (BuildingType.ChassisShop, 1), (BuildingType.BodyShop, 1) }, 500,
            new[] { (Item.CarEngine, 0.03), (Item.RollingChassis, 0.03), (Item.CarBody, 0.03), (Item.SparkPlug, 0.2), (Item.Wheel, 0.1), (Item.Transmission, 0.05) }),
        new("Automobile", new[] { (BuildingType.FinalAssemblyLine, 1) }, 200, new[] { (Item.Automobile, 0.016), (Item.CarEngine, 0.01) }),
        new("Aeroplane", new[] { (BuildingType.AirframeWorks, 1), (BuildingType.AircraftHangar, 1), (BuildingType.Joinery, 1), (BuildingType.DopingShed, 1), (BuildingType.ChemicalWorks, 1), (BuildingType.Foundry, 1) }, 650, new[] { (Item.Aeroplane, 0.006), (Item.Automobile, 0.016) }),
    };

    /// <summary>What a good fetches when it is sold steadily at this rate: base × K / (K + steady glut).</summary>
    static double SteadyPriceCents(Item item, double perSecond)
    {
        double perHour = perSecond * SecondsPerHour;
        double glut = perHour * GlutHours;
        double k = Items.MarketDepth(item);
        return Items.BasePriceCents(item) * k / (k + glut);
    }

    static double IncomePerSecond(Stage s) => s.Sells.Sum(x => x.PerSecond * SteadyPriceCents(x.Item, x.PerSecond));

    [Fact]
    public void TheRoadToTheAeroplaneIsMeasuredInTensOfHours()
    {
        var owned = new int[Catalog.All.Length];
        double seconds = 0, income = 0;
        long capitalTotal = 0;
        int piecesTotal = 0, gated = 0;
        output.WriteLine($"stage            capital      income/s   money h   build h   stage h   total h");
        foreach (var s in Stages)
        {
            long capital = 0;
            int pieces = s.Belts;
            foreach (var (type, count) in s.Machines)
                for (int k = 0; k < count; k++)
                {
                    capital += World.CostForCopy(type, owned[(int)type]++) + LandCents * Catalog.Of(type).Width * Catalog.Of(type).Height;
                    pieces++;
                }
            capital += s.Belts * (BeltCents + LandCents);
            double moneyHours = income <= 0 ? (capital > Difficulty.SteadyTrade.StartingCashCents ? double.PositiveInfinity : 0) : capital / income / 3600;
            double buildHours = pieces / PiecesPerMinute / 60;
            double stageHours = Math.Max(moneyHours, buildHours);
            if (moneyHours >= 0.7 * buildHours)
                gated++;
            seconds += stageHours * 3600;
            capitalTotal += capital;
            piecesTotal += pieces;
            income = IncomePerSecond(s);
            output.WriteLine($"{s.Name,-14} {Hud(capital),12} {income,10:F1} {moneyHours,9:F2} {buildHours,9:F2} {stageHours,9:F2} {seconds / 3600,9:F1}");
        }
        double hours = seconds / 3600;
        output.WriteLine($"total: {hours:F1} h to the first aeroplane; {Hud(capitalTotal)} of works; {piecesTotal} pieces at {PiecesPerMinute}/min; money gates {gated} stages");
        // The GDD wants aeroplanes around the 50-hour mark for playtesters. The model puts a brisk player who never
        // idles near 30 h and a first-time player laying half as fast near 50; it must stay in that country, and the
        // works must be dear enough that money is felt (at least two stages where it is most of the wait).
        Assert.InRange(hours, 22, 60);
        Assert.True(gated >= 2, $"money gates only {gated} stages");
    }

    static string Hud(long cents) => "$" + (cents / 100.0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void FloodingOneGoodHalvesItsPriceAtTheDepthAndDiversifyingBeatsIt()
    {
        // Ten iron mines into one depot: the glut settles near K and the price near half.
        double flooded = SteadyPriceCents(Item.IronOre, 10) / Items.BasePriceCents(Item.IronOre);
        Assert.InRange(flooded, 0.6, 0.72);
        // The same ten mines' worth of ore split into two goods earns more than one.
        double one = 10 * SteadyPriceCents(Item.IronOre, 10);
        double two = 5 * SteadyPriceCents(Item.IronOre, 5) + 5 * SteadyPriceCents(Item.CopperOre, 5) * Items.BasePriceCents(Item.IronOre) / Items.BasePriceCents(Item.CopperOre);
        Assert.True(two > one * 1.1, $"one {one:F0} two {two:F0}");
        // A flagship is scarce: the sixth aeroplane a day already costs a third of the price.
        double planeRate = 6.0 / 120;
        Assert.InRange(SteadyPriceCents(Item.Aeroplane, planeRate) / Items.BasePriceCents(Item.Aeroplane), 0.3, 0.6);
    }
}
