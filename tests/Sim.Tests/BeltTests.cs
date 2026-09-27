using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class BeltTests
{
    [Theory]
    [InlineData(BuildingType.BeltCanvas, 2.0)]
    [InlineData(BuildingType.BeltRubber, 4.0)]
    [InlineData(BuildingType.BeltSteel, 8.0)]
    public void EachTierCarriesItsRatedGoodsPerSecond(BuildingType type, double perSecond)
    {
        var w = Rig.Fresh();
        w.BeltRow(type, 0, 19, 0);
        w.PlaceOk(BuildingType.FreightDepot, 20, -1);
        var depot = (Depot)w.BuildingAt(new Cell(20, 0))!;
        // Feed the head as fast as it will take goods; measure once the run is full.
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        long before = depot.ItemsSold;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        double measured = (depot.ItemsSold - before) / 20.0;
        Assert.InRange(measured, perSecond * 0.98, perSecond * 1.02);
        Assert.Equal(perSecond, BeltTiers.GoodsPerSecond(Catalog.TierOf(type)));
    }

    [Fact]
    public void ConnectedBeltsMergeIntoOneLineAndTiersSplitIt()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 4, 0);
        Assert.Single(w.Lines);
        Assert.Equal(5, w.Lines[0].Tiles.Count);
        Assert.Equal(5 * BeltTiers.TileLength, w.Lines[0].Length);
        for (int x = 0; x <= 4; x++)
            Assert.Equal(x * BeltTiers.TileLength, w.BeltAt(x, 0).LineStart);

        w.PlaceOk(BuildingType.BeltSteel, 5, 0);
        w.PlaceOk(BuildingType.BeltSteel, 6, 0);
        Assert.Equal(2, w.Lines.Count);
        Assert.NotSame(w.BeltAt(4, 0).Line, w.BeltAt(5, 0).Line);
    }

    [Fact]
    public void GoodsCrossTierBoundariesAndCurves()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 3, 0);
        w.BeltRow(BuildingType.BeltRubber, 4, 6, 0);
        w.PlaceOk(BuildingType.BeltRubber, 7, 0, Dir.South);
        w.PlaceOk(BuildingType.BeltRubber, 7, 1, Dir.South);
        w.PlaceOk(BuildingType.FreightDepot, 7, 2, Dir.South);
        Assert.Equal(Dir.East, w.BeltAt(7, 0).CurveIn);
        Assert.Equal(2, w.Lines.Count);
        var depot = (Depot)w.BuildingAt(new Cell(7, 2))!;
        int dropped = 0;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            if (w.Drop(0, 0))
                dropped++;
            w.Tick();
        }
        Assert.InRange(depot.ItemsSold, 20, 40);
        // Every good put on is either sold or still on the belts: none lost at the tier boundary or the curve.
        Assert.Equal(dropped, depot.ItemsSold + w.Lines.Sum(l => l.Count));
    }

    [Fact]
    public void BlockedLineCompressesToTwoGoodsPerTileAndSleeps()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltSteel, 0, 4, 0);
        for (int t = 0; t < 10 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        var line = w.Lines[0];
        Assert.Equal(10, line.Count);
        Assert.True(line.Stalled);
        var goods = line.Goods.ToList();
        Assert.Equal(line.End, goods[0].Pos);
        Assert.Equal(BeltTiers.Spacing / 2, goods[^1].Pos);
        for (int i = 1; i < goods.Count; i++)
            Assert.Equal(BeltTiers.Spacing, goods[i - 1].Pos - goods[i].Pos);
        Assert.All(goods, g => Assert.False(g.Moving));

        w.Run(2);
        Assert.Equal(goods, line.Goods.ToList());
        Assert.True(line.Stalled);
        Assert.Equal(10, line.Count);
    }

    [Fact]
    public void FrontGapStopsAtTheEndAndReleasesWhenTheEndOpens()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 2, 0);
        w.Drop(0, 0);
        w.Run(5);
        var line = w.Lines[0];
        Assert.Equal(line.End, line.Goods.First().Pos);

        w.PlaceOk(BuildingType.FreightDepot, 3, -1);
        w.Run(1);
        Assert.Equal(0, line.Count);
        Assert.Equal(1, ((Depot)w.BuildingAt(new Cell(3, 0))!).ItemsSold);
    }

    [Fact]
    public void SideLoadEntersMidTileHeadOnIsRejected()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 4, 0);
        w.Tick();
        var target = w.BeltAt(2, 0);
        Assert.True(target.TryAccept(w, Item.IronOre, Dir.South, 0, target.Origin));
        Assert.Equal(target.LineStart + BeltTiers.TileLength / 2, w.Lines[0].Goods.First().Pos);
        Assert.False(target.TryAccept(w, Item.IronOre, Dir.West, 0, target.Origin));
        // Too close to the good already at mid-tile.
        Assert.False(target.TryAccept(w, Item.IronOre, Dir.North, 0, target.Origin));
    }

    [Fact]
    public void SideLoadingBeltFeedsTheMainLine()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 5, 0);
        w.PlaceOk(BuildingType.BeltCanvas, 3, -2, Dir.South);
        w.PlaceOk(BuildingType.BeltCanvas, 3, -1, Dir.South);
        Assert.Equal(2, w.Lines.Count);
        Assert.Null(w.BeltAt(3, 0).CurveIn);
        w.PlaceOk(BuildingType.FreightDepot, 6, -1);
        var depot = (Depot)w.BuildingAt(new Cell(6, 0))!;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(3, -2);
            w.Tick();
        }
        // Five tiles of transit at one tile per second, then two per second.
        Assert.InRange(depot.ItemsSold, 26, 36);
    }

    [Fact]
    public void BeltFedOnlyFromTheSideIsACurveAndStraightensWhenFedFromBehind()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.BeltCanvas, 0, 0);
        w.PlaceOk(BuildingType.BeltCanvas, 1, 0, Dir.South);
        var corner = w.BeltAt(1, 0);
        Assert.Equal(Dir.East, corner.CurveIn);
        Assert.Single(w.Lines);

        w.PlaceOk(BuildingType.BeltCanvas, 1, -1, Dir.South);
        Assert.Null(corner.CurveIn);
        Assert.Equal(2, w.Lines.Count);
        Assert.Same(w.BeltAt(1, -1).Line, corner.Line);
        Assert.NotSame(w.BeltAt(0, 0).Line, corner.Line);
    }

    [Fact]
    public void RemovingAMiddleBeltSplitsTheLineAndKeepsGoodsInPlace()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 5, 0);
        w.Tick();
        for (int x = 0; x <= 5; x++)
            w.Drop(x, 0, 100);
        var before = w.Lines[0].Goods.Select(g => g.Pos).ToList();
        Assert.Equal(6, before.Count);

        w.Apply(new Remove(new Cell(2, 0)));
        Assert.Equal(2, w.Lines.Count);
        var left = w.BeltAt(0, 0).Line!;
        var right = w.BeltAt(3, 0).Line!;
        Assert.Equal(2, left.Count);
        Assert.Equal(3, right.Count);
        Assert.Equal(new[] { 340, 100 }, left.Goods.Select(g => g.Pos));
        Assert.Equal(new[] { 580, 340, 100 }, right.Goods.Select(g => g.Pos));

        // Putting it back joins them again with everything still there.
        w.PlaceOk(BuildingType.BeltCanvas, 2, 0);
        Assert.Single(w.Lines);
        Assert.Equal(5, w.Lines[0].Count);
    }

    [Fact]
    public void RotatingABeltRewiresLines()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 3, 0);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Rotate(new Cell(2, 0), Dir.North)));
        Assert.Equal(Dir.North, w.BeltAt(2, 0).Facing);
        Assert.Equal(Dir.East, w.BeltAt(2, 0).CurveIn);
        // 0,1,2 chain through the curve; 3 is alone.
        Assert.Equal(2, w.Lines.Count);
        Assert.Equal(3, w.BeltAt(0, 0).Line!.Tiles.Count);
        Assert.Single(w.BeltAt(3, 0).Line!.Tiles);
    }

    [Fact]
    public void ALoopCirculatesForever()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltRubber, 0, 3, 0, Dir.East);
        w.PlaceOk(BuildingType.BeltRubber, 4, 0, Dir.South);
        w.PlaceOk(BuildingType.BeltRubber, 4, 1, Dir.South);
        w.PlaceOk(BuildingType.BeltRubber, 4, 2, Dir.West);
        for (int x = 3; x >= 1; x--)
            w.PlaceOk(BuildingType.BeltRubber, x, 2, Dir.West);
        w.PlaceOk(BuildingType.BeltRubber, 0, 2, Dir.North);
        w.PlaceOk(BuildingType.BeltRubber, 0, 1, Dir.North);
        Assert.Single(w.Lines);
        Assert.Equal(12, w.Lines[0].Tiles.Count);

        var loop = w.Lines[0];
        for (int i = 0; i < 6; i++)
            Assert.True(loop.TryInsert(w, 2 * i * BeltTiers.TileLength + 30 + 3 * i, Item.IronOre));
        int count = loop.Count;
        Assert.Equal(6, count);
        for (int s = 0; s < 4; s++)
        {
            w.Run(15);
            Assert.Equal(count, w.Lines[0].Count);
            Assert.All(w.Lines[0].Goods, g => Assert.True(g.Moving));
        }

        // Fill it: two goods per tile at most, and a full ring keeps turning.
        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        int full = w.Lines[0].Count;
        Assert.InRange(full, 23, 24);
        w.Run(5);
        Assert.Equal(full, w.Lines[0].Count);
        Assert.All(w.Lines[0].Goods, g => Assert.True(g.Moving));
    }

    [Fact]
    public void MineFeedsTheBackOfALineAndTheSideOfAnother()
    {
        var w = Rig.Fresh();
        // Mine at (5,-5) facing east outputs into (7,-4).
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 9, -4);
        w.Run(5);
        Assert.True(w.Lines[0].Count >= 3);

        var w2 = Rig.Fresh();
        w2.PlaceOk(BuildingType.MineHead, 5, -5);
        w2.BeltRow(BuildingType.BeltCanvas, 7, 7, -6, Dir.South);
        w2.PlaceOk(BuildingType.BeltCanvas, 7, -5, Dir.South);
        w2.PlaceOk(BuildingType.BeltCanvas, 7, -4, Dir.South);
        w2.PlaceOk(BuildingType.BeltCanvas, 7, -3, Dir.South);
        w2.Run(5);
        Assert.True(w2.Lines[0].Count >= 3);
    }
}
