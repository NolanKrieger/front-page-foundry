using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class CommandTests
{
    [Fact]
    public void UndoAndRedoReverseMoneyExactly()
    {
        var w = Rig.Fresh();
        long start = w.CashCents;
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.PlaceOk(BuildingType.MineHead, 4, -8);
        long afterTwo = w.CashCents;
        Assert.True(w.CanUndo);

        Assert.True(w.Undo());
        Assert.Null(w.BuildingAt(new Cell(4, -8)));
        Assert.Equal(start - 40_800, w.CashCents);
        Assert.True(w.Undo());
        Assert.Equal(start, w.CashCents);
        Assert.False(w.Undo());

        Assert.True(w.Redo());
        Assert.True(w.Redo());
        Assert.Equal(afterTwo, w.CashCents);
        Assert.NotNull(w.BuildingAt(new Cell(4, -8)));
        Assert.False(w.Redo());
        Assert.Equal(2, w.Owned(BuildingType.MineHead));
    }

    [Fact]
    public void UndoingADemolitionChargesBackTheRefund()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        long cash = w.CashCents;
        w.Apply(new Remove(new Cell(5, -5)));
        Assert.Equal(cash + 30_000, w.CashCents);
        Assert.True(w.Undo());
        Assert.Equal(cash, w.CashCents);
        Assert.IsType<Mine>(w.BuildingAt(new Cell(6, -4)));
        Assert.Equal(1, w.Owned(BuildingType.MineHead));
    }

    [Fact]
    public void ADraggedRunUndoesAsOneStepAndNewActionsClearRedo()
    {
        var w = Rig.Fresh();
        long start = w.CashCents;
        for (int x = 0; x < 6; x++)
            w.Apply(new Place(BuildingType.BeltCanvas, new Cell(x, 0), Dir.East), joinPrevious: x > 0);
        Assert.True(w.CashCents < start);
        Assert.True(w.Undo());
        Assert.Equal(start, w.CashCents);
        Assert.Empty(w.Lines);
        Assert.True(w.CanRedo);
        w.PlaceOk(BuildingType.BeltCanvas, 0, 5);
        Assert.False(w.CanRedo);
    }

    [Fact]
    public void RotateAndFilterUndo()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.SortingSplitter, 0, 0, Dir.East);
        w.Apply(new SetFilter(new Cell(0, 0), Item.Coal));
        w.Apply(new Rotate(new Cell(0, 0), Dir.North));
        var s = (Splitter)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(Dir.North, s.Facing);
        w.Undo();
        Assert.Equal(Dir.East, s.Facing);
        Assert.Equal(Item.Coal, s.Filter);
        w.Undo();
        Assert.Null(s.Filter);
        w.Redo();
        Assert.Equal(Item.Coal, s.Filter);
    }

    [Fact]
    public void UndoOfABridgeRemovesBothEnds()
    {
        var w = Rig.Fresh();
        long cash = w.CashCents;
        w.PlaceOk(BuildingType.Trestle, 0, 0, Dir.South, 4);
        w.Undo();
        Assert.Null(w.BuildingAt(new Cell(0, 0)));
        Assert.Null(w.BuildingAt(new Cell(0, 5)));
        Assert.Equal(cash, w.CashCents);
        w.Redo();
        Assert.IsType<Belt>(w.BuildingAt(new Cell(0, 5)));
    }

    /// <summary>A scripted session with edits, undo and redo mid-run; the log replays to the same hash.</summary>
    [Fact]
    public void ReplayingTheLogReproducesTheStateHash()
    {
        var w = Rig.Fresh(seed: 1920);
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 11, -4);
        w.Run(7.5);
        w.PlaceOk(BuildingType.Splitter, 12, -4);
        w.BeltRow(BuildingType.BeltRubber, 13, 15, -4);
        w.BeltRow(BuildingType.BeltRubber, 13, 15, -3);
        w.PlaceOk(BuildingType.Trestle, 16, -4, Dir.East, 2);
        w.PlaceOk(BuildingType.Smelter, 20, -5);
        w.PlaceOk(BuildingType.BeltCanvas, 22, -4);
        w.PlaceOk(BuildingType.FreightDepot, 23, -5);
        w.PlaceOk(BuildingType.FreightDepot, 16, -3);
        w.PlaceOk(BuildingType.MineHead, MapGen.StarterCoal.X - 1, MapGen.StarterCoal.Y - 1, Dir.North);
        w.Run(12.25);
        w.Apply(new Rotate(new Cell(9, -4), Dir.North));
        w.Run(3);
        w.Undo();
        w.Run(2);
        w.Apply(new Remove(new Cell(10, -4)));
        w.Run(1);
        w.Undo();
        w.Redo();
        w.Undo();
        w.Run(20);
        Assert.True(w.RevenueCents > 0);

        var log = w.Log.ToList();
        var replay = Rig.Fresh(seed: 1920);
        int next = 0;
        while (replay.TickCount < w.TickCount)
        {
            while (next < log.Count && log[next].Tick == replay.TickCount)
                Assert.Equal(PlaceResult.Ok, replay.Apply(log[next++].Command));
            replay.Tick();
        }
        while (next < log.Count)
            Assert.Equal(PlaceResult.Ok, replay.Apply(log[next++].Command));
        Assert.Equal(w.StateHash(), replay.StateHash());
        Assert.Equal(w.CashCents, replay.CashCents);
    }
}
