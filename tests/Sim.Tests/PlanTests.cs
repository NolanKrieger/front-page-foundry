using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>M10: planning ghosts that build themselves, and blueprints that stamp at scale.</summary>
public class PlanTests
{
    [Fact]
    public void APlanCostsNothingHoldsItsGroundAndIsBuiltOnceThePencilIsDown()
    {
        var w = Rig.Fresh();
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetPlanning(true)));
        Assert.True(w.Planning);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.Smelter, new Cell(-6, 2), Dir.East)));
        Assert.Equal(cash, w.CashCents);
        Assert.True(w.Planned(new Cell(-5, 3)));
        Assert.Null(w.BuildingAt(new Cell(-6, 2)));
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Draft(BuildingType.Press, new Cell(-5, 3), Dir.East)));
        Assert.Equal(PlaceResult.NeedsOre, w.Apply(new Draft(BuildingType.MineHead, new Cell(-20, -20), Dir.East)));
        w.Run(5);
        Assert.Single(w.Plans);

        w.Apply(new SetPlanning(false));
        w.Tick();
        Assert.Empty(w.Plans);
        var smelter = Assert.IsType<Machine>(w.BuildingAt(new Cell(-6, 2)));
        Assert.Equal(BuildingType.Smelter, smelter.Type);
        Assert.False(w.Planned(new Cell(-6, 2)));
        Assert.True(w.CashCents < cash, "the build was paid for");
    }

    [Fact]
    public void PlansBuildFromCashOnlyNeverFromCredit()
    {
        var w = new World(1, 150_000);
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 12, -4);
        w.PlaceOk(BuildingType.FreightDepot, 13, -5);
        long price = w.PurchaseCents(BuildingType.Smelter, new Cell(-6, 2), Dir.East);
        Assert.True(w.CashCents < price && w.CanAfford(price), "cash is short but the credit line could cover it");
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.Smelter, new Cell(-6, 2), Dir.East)));
        w.Run(10);
        Assert.Single(w.Plans);
        Assert.Equal(0, w.DebtCents);
        // Ore sells, cash comes in, and the plan goes up without a cent of credit.
        w.Run(600);
        Assert.Empty(w.Plans);
        Assert.IsType<Machine>(w.BuildingAt(new Cell(-6, 2)));
        Assert.Equal(0, w.DebtCents);
    }

    [Fact]
    public void PlansGoUpOnePerTickInTheOrderTheyWereDrawn()
    {
        var w = Rig.Fresh();
        w.Apply(new SetPlanning(true));
        for (int x = 0; x < 3; x++)
            w.Apply(new Draft(BuildingType.BeltCanvas, new Cell(-20 + x, 10), Dir.East));
        w.Apply(new SetPlanning(false));
        w.Tick();
        Assert.NotNull(w.BuildingAt(new Cell(-20, 10)));
        Assert.Null(w.BuildingAt(new Cell(-19, 10)));
        w.Tick();
        Assert.NotNull(w.BuildingAt(new Cell(-19, 10)));
        w.Tick();
        Assert.NotNull(w.BuildingAt(new Cell(-18, 10)));
        Assert.Empty(w.Plans);
        Assert.Equal(new[] { -20, -19, -18 }, w.Buildings.OfType<Belt>().Select(b => b.Origin.X).ToArray());
    }

    [Fact]
    public void ADraftedSorterAndDockKeepTheirSettings()
    {
        var w = Rig.Fresh();
        w.Apply(new Draft(BuildingType.SortingSplitter, new Cell(-20, 10), Dir.East, 0, Item.Coal));
        w.Apply(new Draft(BuildingType.ReceivingDock, new Cell(-20, 14), Dir.East, 0, Item.Sand));
        w.Run(1);
        Assert.Equal(Item.Coal, Assert.IsType<Splitter>(w.BuildingAt(new Cell(-20, 10))).Filter);
        Assert.Equal(Item.Sand, Assert.IsType<Dock>(w.BuildingAt(new Cell(-20, 14))).Order);
    }

    [Fact]
    public void UndoCoversDraftsAndABuildOverAPlanRubsItOut()
    {
        var w = Rig.Fresh();
        w.Apply(new SetPlanning(true));
        w.Apply(new Draft(BuildingType.Press, new Cell(-20, 10), Dir.East));
        Assert.True(w.Undo());
        Assert.Empty(w.Plans);
        Assert.True(w.Redo());
        Assert.Single(w.Plans);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Undraft(new Cell(-19, 11))));
        Assert.Empty(w.Plans);
        Assert.True(w.Undo());
        Assert.Single(w.Plans);
        // A real belt through the plan's footprint supersedes it.
        w.PlaceOk(BuildingType.BeltCanvas, -19, 10);
        Assert.Empty(w.Plans);
        Assert.False(w.Planned(new Cell(-20, 10)));
    }

    [Fact]
    public void TheTownGrowsAroundPlansButNotOverThem()
    {
        static (World, int) Grown(bool fenced)
        {
            var w = new World(7, Rig.Rich);
            int minX = w.Town.Tiles.Min(t => t.X) - 8, maxX = w.Town.Tiles.Max(t => t.X) + 8;
            int minY = w.Town.Tiles.Min(t => t.Y) - 8, maxY = w.Town.Tiles.Max(t => t.Y) + 8;
            if (fenced)
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                        w.Apply(new Draft(BuildingType.BeltCanvas, new Cell(x, y), Dir.East));
            w.Apply(new SetPlanning(true));
            int before = w.Town.Blocks;
            w.Run(12 * 120);
            return (w, w.Town.Blocks - before);
        }
        var (_, free) = Grown(false);
        var (fencedWorld, fenced) = Grown(true);
        Assert.True(free > 0, "the free town grew");
        Assert.Equal(0, fenced);
        Assert.True(fencedWorld.Plans.Count > 100);
    }

    static World Layout()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltCanvas, 0, 2, 0);
        w.PlaceOk(BuildingType.Splitter, 3, 0);
        w.PlaceOk(BuildingType.SortingSplitter, 5, 0);
        w.Apply(new SetFilter(new Cell(5, 0), Item.IronOre));
        w.PlaceOk(BuildingType.ReceivingDock, 0, 3);
        w.Apply(new SetOrder(new Cell(0, 3), Item.Coal));
        w.PlaceOk(BuildingType.Trestle, 3, 4, Dir.East, 2);
        w.PlaceOk(BuildingType.Smelter, 7, 2, Dir.South);
        return w;
    }

    [Fact]
    public void ABlueprintCopiesWhatStandsWithItsSettings()
    {
        var w = Layout();
        var pieces = Blueprint.Capture(w, new Cell(0, 0), new Cell(9, 6));
        // Three belts, two splitters, a dock, one bridge (entry only), a smelter.
        Assert.Equal(8, pieces.Count);
        Assert.Contains(pieces, p => p.Type == BuildingType.SortingSplitter && p.Setting == Item.IronOre);
        Assert.Contains(pieces, p => p.Type == BuildingType.ReceivingDock && p.Setting == Item.Coal);
        Assert.Contains(pieces, p => p.Type == BuildingType.Trestle && p.Span == 2 && p.Offset == new Cell(3, 4));
        Assert.DoesNotContain(pieces, p => p.Type == BuildingType.Trestle && p.Offset == new Cell(6, 4));
        Assert.Equal((9, 5), Blueprint.Size(pieces));
        // A rectangle that misses the smelter's origin leaves it out.
        Assert.Equal(7, Blueprint.Capture(w, new Cell(0, 0), new Cell(6, 6)).Count);
    }

    [Fact]
    public void AQuarterTurnKeepsEveryPieceInPlaceAndFourTurnsComeBackRound()
    {
        var w = Layout();
        var pieces = Blueprint.Capture(w, new Cell(0, 0), new Cell(9, 6));
        var turned = Blueprint.Rotate(pieces);
        Assert.Equal((5, 9), Blueprint.Size(turned));
        Assert.All(turned.Where(p => Catalog.Of(p.Type).IsBelt), p => Assert.Equal(Dir.South, p.Facing));
        // The east-facing splitter (1 wide, 2 tall) becomes south-facing, 2 wide and 1 tall, in the turned spot.
        var splitter = turned.Single(p => p.Type == BuildingType.Splitter);
        Assert.Equal(Dir.South, splitter.Facing);
        Assert.Equal((2, 1), Catalog.Footprint(BuildingType.Splitter, splitter.Facing));
        var cells = new HashSet<Cell>();
        foreach (var p in turned)
            foreach (var c in Blueprint.Cells(p))
                Assert.True(cells.Add(c), $"{p.Type} overlaps at {c}");
        var round = pieces;
        for (int k = 0; k < 4; k++)
            round = Blueprint.Rotate(round);
        Assert.Equal(pieces, round);
    }

    [Fact]
    public void AStampDraftsEveryPieceAsOneUndoStepAndTheyBuild()
    {
        var w = Layout();
        var pieces = Blueprint.Rotate(Blueprint.Capture(w, new Cell(0, 0), new Cell(9, 6)));
        long cost = Blueprint.CostCents(w, pieces, new Cell(30, 30));
        Assert.True(cost > 0);
        int before = w.Buildings.Count;
        Assert.Equal(pieces.Count, Blueprint.Stamp(w, pieces, new Cell(30, 30)));
        Assert.Equal(pieces.Count, w.Plans.Count);
        Assert.True(w.Undo());
        Assert.Empty(w.Plans);
        Assert.True(w.Redo());
        Assert.Equal(pieces.Count, w.Plans.Count);
        w.Run(pieces.Count / (double)World.TicksPerSecond + 0.1);
        Assert.Empty(w.Plans);
        Assert.Equal(before + pieces.Count + 1, w.Buildings.Count); // the bridge is two buildings
        var dock = w.Buildings.OfType<Dock>().Single(d => d.Origin.X >= 30);
        Assert.Equal(Item.Coal, dock.Order);
        // Stamping where something stands drafts only what fits.
        Assert.True(Blueprint.Stamp(w, pieces, new Cell(30, 30)) < pieces.Count);
    }

    [Fact]
    public void PlansAndStampsReplayDeterministically()
    {
        static World Play()
        {
            var w = Layout();
            var pieces = Blueprint.Capture(w, new Cell(0, 0), new Cell(9, 6));
            w.Apply(new SetPlanning(true));
            Blueprint.Stamp(w, pieces, new Cell(20, 20));
            w.Run(2);
            w.Apply(new SetPlanning(false));
            w.Run(3);
            w.Undo();
            w.Run(1);
            return w;
        }
        Assert.Equal(Play().StateHash(), Play().StateHash());
    }

    [Fact]
    public void AStarvedMachineSaysWhatItWantsAndLinesCountWhatPasses()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.OpenHearthFurnace, 0, 0);
        var furnace = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(Item.IronIngot, furnace.Wants());
        furnace.TryAccept(w, Item.Coke, Dir.East, 60, furnace.Origin);
        Assert.Equal(Item.IronIngot, furnace.Wants());
        furnace.TryAccept(w, Item.IronIngot, Dir.East, 60, furnace.Origin);
        furnace.TryAccept(w, Item.IronIngot, Dir.East, 60, furnace.Origin);
        Assert.Equal(Item.Limestone, furnace.Wants());

        var line = Rig.StarterLine(out _);
        line.Run(20);
        var belt = line.BeltAt(7, -4).Line!;
        Assert.InRange(belt.Passed, 12, 15);
    }
}
