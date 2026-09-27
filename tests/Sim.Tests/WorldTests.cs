using FrontPageFoundry.Sim;

namespace Sim.Tests;

public static class Rig
{
    public const long Rich = 1_000_000_000_000;

    public static World Fresh(int seed = 1, long cash = Rich) => new(seed, cash);

    public static PlaceResult Place(this World w, BuildingType type, int x, int y, Dir facing = Dir.East, int span = 0) =>
        w.Apply(new Place(type, new Cell(x, y), facing, span));

    public static void PlaceOk(this World w, BuildingType type, int x, int y, Dir facing = Dir.East, int span = 0) =>
        Assert.Equal(PlaceResult.Ok, w.Place(type, x, y, facing, span));

    /// <summary>A straight run of belts of one tier from (x0,y) to (x1,y) inclusive, facing east.</summary>
    public static void BeltRow(this World w, BuildingType type, int x0, int x1, int y, Dir facing = Dir.East)
    {
        for (int x = x0; x <= x1; x++)
            w.PlaceOk(type, x, y, facing);
    }

    public static Belt BeltAt(this World w, int x, int y) => (Belt)w.BuildingAt(new Cell(x, y))!;

    public static void Run(this World w, double seconds)
    {
        for (int i = 0; i < (int)(seconds * World.TicksPerSecond); i++)
            w.Tick();
    }

    /// <summary>Puts a good on the line under a belt cell, centred at a position along that tile (test-only).</summary>
    public static bool Drop(this World w, int x, int y, int posInTile = BeltTiers.Spacing / 2, Item item = Item.IronOre)
    {
        var b = w.BeltAt(x, y);
        return b.Line!.TryInsert(w, b.LineStart + posInTile, item);
    }

    /// <summary>Starter mine → six canvas belts east → depot.</summary>
    public static World StarterLine(out Depot depot)
    {
        var w = Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 12, -4);
        w.PlaceOk(BuildingType.FreightDepot, 13, -5);
        depot = (Depot)w.BuildingAt(new Cell(13, -4))!;
        return w;
    }
}

public class WorldTests
{
    [Fact]
    public void OreTravelsFromMineToDepotAndSells()
    {
        var w = Rig.StarterLine(out var depot);
        long cash = w.CashCents;
        w.Run(20);
        // One ore per second; six tiles at one tile per second, so about six seconds in transit.
        Assert.InRange(depot.ItemsSold, 12, 15);
        Assert.True(w.CashCents > cash);
        Assert.Equal(w.CashCents - cash, w.RevenueCents);
    }

    [Fact]
    public void MineNeedsOreAndPlacementRespectsSpaceAndMoney()
    {
        var w = Rig.Fresh();
        Assert.Equal(PlaceResult.NeedsOre, w.Place(BuildingType.MineHead, -18, -12));

        w.PlaceOk(BuildingType.BeltCanvas, 0, 0);
        Assert.Equal(PlaceResult.Blocked, w.Place(BuildingType.FreightDepot, -1, -1));

        var poor = new World(1, 100, Difficulty.SteadyTrade with { BaseCreditCents = 0 });
        Assert.Equal(PlaceResult.TooExpensive, poor.Place(BuildingType.BeltCanvas, 0, 0));
        Assert.Equal(100, poor.CashCents);
        Assert.Empty(poor.Log);
    }

    [Fact]
    public void MachineCostEscalatesPerCopyButLogisticsDoesNot()
    {
        var w = Rig.Fresh();
        Assert.Equal(40_000, w.CostCents(BuildingType.MineHead));
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        // 400 × 1.04^1.5
        Assert.Equal(42_424, w.CostCents(BuildingType.MineHead));

        foreach (var type in new[] { BuildingType.BeltCanvas, BuildingType.Splitter, BuildingType.SortingSplitter })
        {
            long before = w.CostCents(type);
            w.PlaceOk(type, 20, 20 + 3 * (int)type);
            Assert.Equal(before, w.CostCents(type));
        }
    }

    [Fact]
    public void DemolishRefundsThreeQuartersOfTheLatestCopy()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        long cash = w.CashCents;
        // Three quarters of the $400 comes back; the land was bought for good.
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(6, -4))));
        Assert.Equal(cash + 30_000, w.CashCents);
        Assert.Equal(40_000, w.CostCents(BuildingType.MineHead));
        Assert.Null(w.BuildingAt(new Cell(5, -5)));
        Assert.Equal(PlaceResult.Nothing, w.Apply(new Remove(new Cell(5, -5))));
    }

    [Fact]
    public void MineOutputSlowsButNeverStops()
    {
        Assert.Equal(1.0, Mine.YieldFactor(0));
        Assert.True(Mine.YieldFactor((long)Mine.Richness) < 0.6);
        Assert.Equal(Mine.FloorYield, Mine.YieldFactor(10_000_000), 6);
    }

    [Fact]
    public void SameSeedAndCommandsGiveIdenticalState()
    {
        var a = Rig.StarterLine(out _);
        var b = Rig.StarterLine(out _);
        a.Run(90);
        b.Run(90);
        Assert.Equal(a.StateHash(), b.StateHash());

        b.Tick();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void TerrainIsTheSameInAnyQueryOrder()
    {
        var a = new MapGen(7);
        var b = new MapGen(7);
        var cells = Enumerable.Range(-200, 400).Select(i => new Cell(i * 7, -i * 3)).ToArray();
        var forward = cells.Select(a.TerrainAt).ToArray();
        var backward = cells.Reverse().Select(b.TerrainAt).Reverse().ToArray();
        Assert.Equal(forward, backward);
        Assert.Contains(Terrain.IronOre, forward);
    }

    [Fact]
    public void CatalogAndItemTablesFollowTheirEnums()
    {
        foreach (var t in Catalog.All)
            Assert.Equal(t, Catalog.Of(t).Type);
        foreach (var i in Items.All)
            Assert.Equal(i, Items.Of(i).Item);
        Assert.Equal((1, 2), Catalog.Footprint(BuildingType.Splitter, Dir.East));
        Assert.Equal((2, 1), Catalog.Footprint(BuildingType.Splitter, Dir.North));
    }
}
