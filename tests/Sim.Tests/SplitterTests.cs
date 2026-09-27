using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class SplitterTests
{
    /// <summary>One feed belt into lane 1 of an east-facing splitter at (5,0)-(5,1); two output rows to depots.</summary>
    static World SplitRig(out Depot top, out Depot bottom)
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltSteel, 0, 4, 0);
        w.PlaceOk(BuildingType.Splitter, 5, 0);
        w.BeltRow(BuildingType.BeltSteel, 6, 9, 0);
        w.BeltRow(BuildingType.BeltSteel, 6, 9, 1);
        w.PlaceOk(BuildingType.FreightDepot, 10, -1);
        w.PlaceOk(BuildingType.FreightDepot, 10, 1);
        top = (Depot)w.BuildingAt(new Cell(10, 0))!;
        bottom = (Depot)w.BuildingAt(new Cell(10, 1))!;
        return w;
    }

    [Fact]
    public void LaneCellsFollowTheFacing()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Splitter, 0, 0, Dir.East);
        var s = (Splitter)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(new Cell(0, 0), s.LaneCell(0));
        Assert.Equal(new Cell(0, 1), s.LaneCell(1));
        Assert.Equal(new Cell(1, 0), s.OutCell(0));

        w.PlaceOk(BuildingType.Splitter, 4, 0, Dir.South);
        s = (Splitter)w.BuildingAt(new Cell(4, 0))!;
        Assert.Equal(new Cell(5, 0), s.LaneCell(0));
        Assert.Equal(new Cell(4, 0), s.LaneCell(1));

        w.PlaceOk(BuildingType.Splitter, 8, 0, Dir.West);
        s = (Splitter)w.BuildingAt(new Cell(8, 0))!;
        Assert.Equal(new Cell(8, 1), s.LaneCell(0));
        Assert.Equal(new Cell(8, 0), s.LaneCell(1));
        Assert.Equal(new Cell(7, 1), s.OutCell(0));
    }

    [Fact]
    public void SplitsOneFeedEvenlyAndKeepsFullThroughput()
    {
        var w = SplitRig(out var top, out var bottom);
        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        Assert.InRange(top.ItemsSold + bottom.ItemsSold, 8 * 27.5 * 0.95, 8 * 27.5 * 1.05);
        Assert.InRange(Math.Abs(top.ItemsSold - bottom.ItemsSold), 0, 2);
    }

    [Fact]
    public void RoutesEverythingToTheOpenSideWhenOneOutputIsBlocked()
    {
        var w = SplitRig(out var top, out var bottom);
        w.Apply(new Remove(new Cell(10, 1)));
        for (int x = 6; x <= 9; x++)
            w.Apply(new Remove(new Cell(x, 1)));
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
        }
        // About 2.5 s of transit, then the full 8/s through one output.
        Assert.InRange(top.ItemsSold, 8 * 17.5 * 0.95, 8 * 17.5 * 1.05);
        Assert.Equal(0, bottom.ItemsSold);
    }

    [Fact]
    public void MergesTwoFeedsFairly()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltSteel, 0, 4, 0);
        w.BeltRow(BuildingType.BeltSteel, 0, 4, 1);
        w.PlaceOk(BuildingType.Splitter, 5, 0);
        w.BeltRow(BuildingType.BeltSteel, 6, 9, 0);
        w.PlaceOk(BuildingType.FreightDepot, 10, -1);
        var depot = (Depot)w.BuildingAt(new Cell(10, 0))!;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0, 0, Item.IronOre);
            w.Drop(0, 1, 0, Item.CopperOre);
            w.Tick();
        }
        // One steel output carries 8/s; both feeds should share it.
        Assert.InRange(depot.ItemsSold, 8 * 17.5 * 0.95, 8 * 17.5 * 1.05);
        Assert.InRange(Math.Abs(w.Market.Glut(Item.IronOre) - w.Market.Glut(Item.CopperOre)), 0, 3);
    }

    [Fact]
    public void SortingSplitterSendsTheFilterLeftAndTheRestRight()
    {
        var w = SplitRig(out var top, out var bottom);
        w.Apply(new Remove(new Cell(5, 0)));
        w.PlaceOk(BuildingType.SortingSplitter, 5, 0);
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetFilter(new Cell(5, 1), Item.CopperOre)));
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0, 0, t % 3 == 0 ? Item.CopperOre : Item.IronOre);
            w.Tick();
        }
        Assert.True(top.ItemsSold > 30);
        Assert.True(bottom.ItemsSold > 60);
        // The top depot only ever saw copper: iron's glut is entirely the bottom depot's.
        var w2 = SplitRig(out var top2, out _);
        w2.Apply(new Remove(new Cell(5, 0)));
        w2.PlaceOk(BuildingType.SortingSplitter, 5, 0);
        w2.Apply(new SetFilter(new Cell(5, 0), Item.CopperOre));
        w2.Apply(new Remove(new Cell(10, 1)));
        for (int t = 0; t < 10 * World.TicksPerSecond; t++)
        {
            w2.Drop(0, 0, 0, Item.IronOre);
            w2.Tick();
        }
        Assert.Equal(0, top2.ItemsSold);
    }

    [Fact]
    public void LanesTakeTheSpeedOfTheirFeedBelt()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Splitter, 5, 0);
        var s = (Splitter)w.BuildingAt(new Cell(5, 0))!;
        Assert.Equal(BeltTier.Canvas, s.Lane(0).Tier);
        w.PlaceOk(BuildingType.BeltSteel, 4, 0);
        Assert.Equal(BeltTier.Steel, s.Lane(0).Tier);
        Assert.Equal(BeltTier.Canvas, s.Lane(1).Tier);
        w.Apply(new Remove(new Cell(4, 0)));
        Assert.Equal(BeltTier.Canvas, s.Lane(0).Tier);
    }

    [Fact]
    public void RotatingASplitterNeedsRoom()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Splitter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.BeltCanvas, 1, 0);
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(new Cell(0, 0), Dir.North)));
        w.Apply(new Remove(new Cell(1, 0)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Rotate(new Cell(0, 0), Dir.North)));
        Assert.Same(w.BuildingAt(new Cell(0, 0)), w.BuildingAt(new Cell(1, 0)));
        Assert.Null(w.BuildingAt(new Cell(0, 1)));
    }
}
