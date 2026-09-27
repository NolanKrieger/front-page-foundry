using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class BridgeTests
{
    [Fact]
    public void PlacesAPairAndRemovingEitherEndRemovesBoth()
    {
        var w = Rig.Fresh();
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.BadSpan, w.Place(BuildingType.Trestle, 0, 0, Dir.East, 0));
        Assert.Equal(PlaceResult.BadSpan, w.Place(BuildingType.Trestle, 0, 0, Dir.East, 5));
        w.PlaceOk(BuildingType.Trestle, 0, 0, Dir.East, 3);
        Assert.Equal(cash - Catalog.Of(BuildingType.Trestle).BaseCostCents - 2 * World.RuralLandCents, w.CashCents);
        var entry = w.BeltAt(0, 0);
        var exit = w.BeltAt(4, 0);
        Assert.Equal(BeltRole.BridgeEntry, entry.Role);
        Assert.Equal(BeltRole.BridgeExit, exit.Role);
        Assert.Same(exit, entry.Partner);
        for (int x = 1; x <= 3; x++)
            Assert.Null(w.BuildingAt(new Cell(x, 0)));
        Assert.Single(w.Lines);
        Assert.Equal(5 * BeltTiers.TileLength, w.Lines[0].Length);

        w.Apply(new Remove(new Cell(4, 0)));
        Assert.Null(w.BuildingAt(new Cell(0, 0)));
        Assert.Null(w.BuildingAt(new Cell(4, 0)));
        Assert.Empty(w.Lines);
    }

    [Fact]
    public void GoodsCrossTheDeckOverABeltBelow()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltRubber, 0, 2, 0);
        w.PlaceOk(BuildingType.Trestle, 3, 0, Dir.East, 2);
        w.BeltRow(BuildingType.BeltRubber, 7, 9, 0);
        w.PlaceOk(BuildingType.FreightDepot, 10, -1);
        // A crossing line underneath.
        w.PlaceOk(BuildingType.BeltCanvas, 4, -1, Dir.South);
        w.PlaceOk(BuildingType.BeltCanvas, 4, 0, Dir.South);
        w.PlaceOk(BuildingType.BeltCanvas, 4, 1, Dir.South);
        Assert.Equal(BeltTier.Rubber, w.BeltAt(3, 0).Tier);
        Assert.Equal(BeltTier.Rubber, w.BeltAt(6, 0).Tier);
        // The whole rubber run, deck included, is one line.
        Assert.Same(w.BeltAt(0, 0).Line, w.BeltAt(9, 0).Line);
        var depot = (Depot)w.BuildingAt(new Cell(10, 0))!;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Drop(4, -1);
            w.Tick();
        }
        // Ten tiles of transit at two tiles per second, then four per second.
        Assert.InRange(depot.ItemsSold, 4 * 15 * 0.95, 4 * 15 * 1.05);
        // Nothing leaked from the canvas line into the rubber one.
        Assert.Equal(3, w.BeltAt(4, 0).Line!.Tiles.Count);
    }

    [Fact]
    public void ExitOnlyTakesGoodsFromTheDeck()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Trestle, 0, 0, Dir.East, 1);
        var exit = w.BeltAt(2, 0);
        Assert.False(exit.TryAccept(w, Item.IronOre, Dir.East, 0, exit.Origin));
        Assert.True(exit.TryAccept(w, Item.IronOre, Dir.South, 0, exit.Origin));
        Assert.False(exit.TryAccept(w, Item.IronOre, Dir.West, 0, exit.Origin));
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(new Cell(0, 0), Dir.North)));
    }

    [Fact]
    public void BridgeTakesTheTierOfItsFeed()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Trestle, 3, 0, Dir.East, 2);
        Assert.Equal(BeltTier.Canvas, w.BeltAt(3, 0).Tier);
        w.PlaceOk(BuildingType.BeltSteel, 2, 0);
        Assert.Equal(BeltTier.Steel, w.BeltAt(3, 0).Tier);
        Assert.Equal(BeltTier.Steel, w.BeltAt(6, 0).Tier);
        Assert.Same(w.BeltAt(2, 0).Line, w.BeltAt(6, 0).Line);
        w.Apply(new Remove(new Cell(2, 0)));
        Assert.Equal(BeltTier.Canvas, w.BeltAt(6, 0).Tier);
    }
}
