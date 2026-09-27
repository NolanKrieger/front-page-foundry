using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>Regression tests from the logistics audit (belts, splitters, trestles, commands, undo/redo).</summary>
public class AuditLogisticsTests
{
    static int GoodsOnBelts(World w) =>
        w.Lines.Sum(l => l.Count) + w.Buildings.OfType<Splitter>().Sum(s => s.Lane(0).Count + s.Lane(1).Count);

    /// <summary>A twelve-tile rubber ring round (0,0)–(4,2), filled; <paramref name="slowCell"/> is laid in canvas instead.</summary>
    static World Ring((int X, int Y)? slowCell = null)
    {
        var w = Rig.Fresh();
        var ring = new (int X, int Y, Dir D)[]
        {
            (0, 0, Dir.East), (1, 0, Dir.East), (2, 0, Dir.East), (3, 0, Dir.East), (4, 0, Dir.South), (4, 1, Dir.South),
            (4, 2, Dir.West), (3, 2, Dir.West), (2, 2, Dir.West), (1, 2, Dir.West), (0, 2, Dir.North), (0, 1, Dir.North),
        };
        foreach (var (x, y, d) in ring)
            w.PlaceOk((x, y) == slowCell ? BuildingType.BeltCanvas : BuildingType.BeltRubber, x, y, d);
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
        {
            foreach (var (x, y, _) in ring)
            {
                w.Drop(x, y, 60);
                w.Drop(x, y, 180);
            }
            w.Tick();
        }
        return w;
    }

    // ---- L1: undo refunds only the building it put up --------------------------------------------

    [Fact]
    public void UndoOfABuildNeverRefundsAPlanBuiltOnTheSameCell()
    {
        var w = Rig.Fresh();
        long start = w.NetWorthCents;
        var x = new Cell(30, 5);
        w.PlaceOk(BuildingType.AircraftHangar, x.X, x.Y);
        w.Apply(new Remove(x));
        w.Apply(new Draft(BuildingType.BeltCanvas, x, Dir.East));
        w.Tick();                                       // the plan is built for real, with no undo record
        Assert.IsType<Belt>(w.BuildingAt(x));
        while (w.Undo()) { }
        // The belt the plan bought stays; the hangar's price is not paid out for it.
        Assert.IsType<Belt>(w.BuildingAt(x));
        Assert.True(w.NetWorthCents <= start, $"undo printed {w.NetWorthCents - start} cents");
        Assert.Equal(0, w.Owned(BuildingType.AircraftHangar));
        Assert.Equal(1, w.Owned(BuildingType.BeltCanvas));
    }

    [Fact]
    public void UndoChainsThroughADemolitionStillReverseExactly()
    {
        var w = Rig.Fresh();
        long cash = w.CashCents;
        w.PlaceOk(BuildingType.Smelter, 20, 0);
        w.PlaceOk(BuildingType.Trestle, 20, 4, Dir.East, 3);
        int smelterId = w.BuildingAt(new Cell(20, 0))!.Id;
        w.Apply(new Remove(new Cell(21, 1)));
        w.Apply(new Remove(new Cell(24, 4)));           // the trestle, by its exit
        Assert.True(w.Undo());                          // the trestle comes back
        Assert.True(w.Undo());                          // the smelter comes back, under its old number
        Assert.Equal(smelterId, w.BuildingAt(new Cell(20, 0))!.Id);
        Assert.True(w.Undo());                          // ...so undoing the builds still finds them
        Assert.True(w.Undo());
        Assert.Null(w.BuildingAt(new Cell(20, 0)));
        Assert.Null(w.BuildingAt(new Cell(20, 4)));
        Assert.Equal(cash, w.CashCents);
        for (int i = 0; i < 4; i++)
            Assert.True(w.Redo());
        Assert.Null(w.BuildingAt(new Cell(20, 0)));
        Assert.Null(w.BuildingAt(new Cell(20, 4)));
        Assert.True(w.Undo());
        Assert.True(w.Undo());
        // The buildings list stays in id order (the update order).
        Assert.Equal(w.Buildings.Select(b => b.Id).OrderBy(i => i), w.Buildings.Select(b => b.Id));
        Assert.NotNull(w.BuildingAt(new Cell(20, 0)));
        Assert.NotNull(w.BuildingAt(new Cell(24, 4)));
    }

    // ---- L2: rebuilding a line never loses goods short of its start ------------------------------

    [Fact]
    public void RebuildingALineNextToAFlowingTierBoundaryKeepsEveryGood()
    {
        var w = Rig.Fresh();
        w.BeltRow(BuildingType.BeltRubber, 0, 4, 0);
        w.BeltRow(BuildingType.BeltCanvas, 5, 9, 0);
        w.PlaceOk(BuildingType.FreightDepot, 10, -1);
        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
        {
            w.Drop(0, 0);
            w.Tick();
            int before = GoodsOnBelts(w);
            // Laying and lifting a belt beside the canvas line rebuilds it (nothing is removed from it).
            w.PlaceOk(BuildingType.BeltCanvas, 6, 1, Dir.South);
            Assert.Equal(before, GoodsOnBelts(w));
            Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(6, 1))));
            Assert.Equal(before, GoodsOnBelts(w));
        }
    }

    [Fact]
    public void RebuildingAFullRingKeepsEveryGood()
    {
        var w = Ring();
        for (int t = 0; t < 120; t++)
        {
            w.Tick();
            int before = GoodsOnBelts(w);
            w.PlaceOk(BuildingType.BeltCanvas, 2, 1, Dir.South);   // inside the ring, touching nothing
            Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(2, 1))));
            Assert.Equal(before, GoodsOnBelts(w));
        }
    }

    /// <summary>
    /// Guards finding L9 (left as is): a jammed tier boundary takes one good half a spacing short of the next
    /// line's start. That slack is what lets a full ring made of several lines keep turning.
    /// </summary>
    [Fact]
    public void AFullRingOfTwoTiersKeepsTurning()
    {
        var w = Ring(slowCell: (3, 2));
        Assert.Equal(2, w.Lines.Count);
        Assert.Equal(24, GoodsOnBelts(w));
        long passed = w.Lines.Sum(l => l.Passed);
        w.Run(5);
        Assert.Equal(24, GoodsOnBelts(w));
        // The canvas tile passes 2 a second, and each good crosses both lines.
        Assert.InRange(w.Lines.Sum(l => l.Passed) - passed, 18, 22);
    }

    // ---- L3: turning a piece obeys the ground rules ----------------------------------------------

    [Fact]
    public void TurningASplitterRespectsRiverTownAndPlans()
    {
        var w = Rig.Fresh();
        var bank = w.FlatBankNear(40);
        Assert.Equal(Terrain.River, w.TerrainAt(bank + new Cell(0, 1)));
        w.PlaceOk(BuildingType.Splitter, bank.X, bank.Y, Dir.North);      // (x,y),(x+1,y) along the bank
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(bank, Dir.East)));   // would stand in the river
        Assert.Equal(Dir.North, w.BuildingAt(bank)!.Facing);

        // The town.
        var town = w.Town.Tiles.OrderBy(c => c).First(c => w.TerrainAt(c + new Cell(0, -1)) == Terrain.Ground
            && w.TerrainAt(c + new Cell(1, -1)) == Terrain.Ground && w.BuildingAt(c + new Cell(0, -1)) == null
            && w.BuildingAt(c + new Cell(1, -1)) == null);
        var above = town + new Cell(0, -1);
        w.PlaceOk(BuildingType.Splitter, above.X, above.Y, Dir.North);    // (x,y-1),(x+1,y-1)
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(above, Dir.East)));  // (x,y) is town

        // A plan underfoot.
        w.PlaceOk(BuildingType.Splitter, 0, 0, Dir.North);                // (0,0),(1,0)
        w.Apply(new Draft(BuildingType.BeltCanvas, new Cell(0, 1), Dir.East));
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(new Cell(0, 0), Dir.East)));
        w.Apply(new Undraft(new Cell(0, 1)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Rotate(new Cell(0, 0), Dir.East)));
    }

    [Fact]
    public void TurningASplitterIntoStandingWoodIsRefused()
    {
        var w = Rig.Fresh();
        Cell? spot = null;
        for (int x = 40; x < 400 && spot == null; x++)
            for (int y = -60; y < 0 && spot == null; y++)
            {
                var c = new Cell(x, y);
                if (w.TerrainAt(c) == Terrain.Forest && w.TerrainAt(c + new Cell(0, -1)) == Terrain.Ground
                    && w.TerrainAt(c + new Cell(1, -1)) == Terrain.Ground)
                    spot = c + new Cell(0, -1);
            }
        Assert.NotNull(spot);
        var s = spot!.Value;
        w.PlaceOk(BuildingType.Splitter, s.X, s.Y, Dir.North);
        Assert.Equal(PlaceResult.Blocked, w.Apply(new Rotate(s, Dir.East)));
        Assert.Equal(Terrain.Forest, w.TerrainAt(s + new Cell(0, 1)));
    }

    // ---- L4 / L5: undo restores settings and turns ------------------------------------------------

    [Fact]
    public void UndoOfADemolitionPutsBackTheFilterAndTheOrder()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.SortingSplitter, 0, 0, Dir.East);
        w.Apply(new SetFilter(new Cell(0, 0), Item.Coal));
        w.PlaceOk(BuildingType.ReceivingDock, 4, 0, Dir.East);
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetOrder(new Cell(4, 0), Item.Limestone)));
        w.Apply(new Remove(new Cell(0, 1)));
        w.Apply(new Remove(new Cell(5, 1)));
        Assert.True(w.Undo());
        Assert.True(w.Undo());
        Assert.Equal(Item.Coal, ((Splitter)w.BuildingAt(new Cell(0, 0))!).Filter);
        Assert.Equal(Item.Limestone, ((Dock)w.BuildingAt(new Cell(4, 0))!).Order);
        // Redo takes the splitter (the last one brought back) down again; undo restores its filter once more.
        Assert.True(w.Redo());
        Assert.Null(w.BuildingAt(new Cell(0, 0)));
        Assert.True(w.Undo());
        Assert.Equal(Item.Coal, ((Splitter)w.BuildingAt(new Cell(0, 0))!).Filter);
    }

    [Fact]
    public void UndoOfATurnMadeFromASplittersSecondCell()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Splitter, 0, 0, Dir.East);                 // (0,0),(0,1)
        Assert.Equal(PlaceResult.Ok, w.Apply(new Rotate(new Cell(0, 1), Dir.North)));
        var s = w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(Dir.North, s.Facing);
        Assert.True(w.Undo());
        Assert.Equal(Dir.East, s.Facing);
        Assert.True(w.Redo());
        Assert.Equal(Dir.North, s.Facing);
    }

    // ---- L6: a bridge's tier reaches everything its exit feeds -----------------------------------

    [Fact]
    public void ChainedTrestlesAndASplitterAfterAnExitTakeTheFeedTier()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Trestle, 1, 0, Dir.East, 2);     // 1 → 4
        w.PlaceOk(BuildingType.Trestle, 5, 0, Dir.East, 2);     // 5 → 8
        w.PlaceOk(BuildingType.Splitter, 9, 0, Dir.East);
        var s = (Splitter)w.BuildingAt(new Cell(9, 0))!;
        w.PlaceOk(BuildingType.BeltSteel, 0, 0);
        Assert.Equal(BeltTier.Steel, w.BeltAt(5, 0).Tier);
        Assert.Equal(BeltTier.Steel, w.BeltAt(8, 0).Tier);
        Assert.Equal(BeltTier.Steel, s.Lane(0).Tier);
        Assert.Single(w.Lines);                                 // feed, both decks and both exits: one steel line
        w.Apply(new Remove(new Cell(0, 0)));
        Assert.Equal(BeltTier.Canvas, w.BeltAt(8, 0).Tier);
        Assert.Equal(BeltTier.Canvas, s.Lane(0).Tier);

        // A steel run past the exit joins the bridge's line once the bridge turns steel.
        var w2 = Rig.Fresh();
        w2.PlaceOk(BuildingType.Trestle, 1, 0, Dir.East, 2);
        w2.BeltRow(BuildingType.BeltSteel, 5, 7, 0);
        Assert.Equal(2, w2.Lines.Count);
        w2.PlaceOk(BuildingType.BeltSteel, 0, 0);
        Assert.Single(w2.Lines);
    }

    // ---- L7: a folded company refuses undo --------------------------------------------------------

    [Fact]
    public void AFoldedCompanyRefusesUndoAndRedo()
    {
        var w = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        w.PlaceOk(BuildingType.BeltCanvas, 0, 0);               // on credit
        w.Pay(w.CreditAvailableCents);
        w.Run(7 * 120);
        Assert.True(w.Ended);
        long cash = w.CashCents;
        Assert.False(w.CanUndo);
        Assert.False(w.Undo());
        Assert.False(w.Redo());
        Assert.Equal(cash, w.CashCents);
        Assert.NotNull(w.BuildingAt(new Cell(0, 0)));
    }

    // ---- Properties over random sessions ----------------------------------------------------------

    static readonly BuildingType[] Pieces =
    {
        BuildingType.BeltCanvas, BuildingType.BeltRubber, BuildingType.BeltSteel, BuildingType.BeltCanvas, BuildingType.BeltRubber,
        BuildingType.Splitter, BuildingType.SortingSplitter, BuildingType.Trestle, BuildingType.Smelter, BuildingType.Press,
    };

    /// <summary>One random player action on a 16×10 patch by the works (nothing on it earns money).</summary>
    static void RandomAction(World w, Random rng)
    {
        var cell = new Cell(rng.Next(4, 20), rng.Next(-3, 7));
        var dir = (Dir)rng.Next(4);
        int roll = rng.Next(100);
        if (roll < 40)
        {
            var type = Pieces[rng.Next(Pieces.Length)];
            w.Apply(new Place(type, cell, dir, type == BuildingType.Trestle ? rng.Next(1, 5) : 0), joinPrevious: rng.Next(3) == 0);
        }
        else if (roll < 55)
            w.Apply(new Remove(cell), joinPrevious: rng.Next(4) == 0);
        else if (roll < 63)
            w.Apply(new Rotate(cell, dir));
        else if (roll < 66)
            w.Apply(new SetFilter(cell, (Item)rng.Next(4)));
        else if (roll < 72)
            w.Apply(new Draft(Pieces[rng.Next(Pieces.Length)], cell, dir, rng.Next(1, 5)));
        else if (roll < 74)
            w.Apply(new Undraft(cell));
        else if (roll < 76)
            w.Apply(new SetPlanning(rng.Next(2) == 0));
        else if (roll < 90)
            w.Undo();
        else
            w.Redo();
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void NoSessionOfBuildingDemolishingPlanningAndUndoingMakesMoney(int seed)
    {
        var w = Rig.Fresh(seed: 3);
        var rng = new Random(seed);
        long start = w.NetWorthCents;
        for (int step = 0; step < 600; step++)
        {
            RandomAction(w, rng);
            if (rng.Next(4) == 0)
                for (int t = rng.Next(1, 20); t > 0; t--)
                    w.Tick();
            Assert.True(w.NetWorthCents <= start, $"step {step}: net worth rose by {w.NetWorthCents - start} cents");
        }
        while (w.Undo())
            Assert.True(w.NetWorthCents <= start);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void EditsNeverCreateGoodsAndOnlyDemolitionLosesThem(int seed)
    {
        var w = Rig.Fresh(seed: 4);
        var rng = new Random(seed);
        for (int step = 0; step < 500; step++)
        {
            // Goods appear only by hand here, a few a tick on some of the lines.
            for (int t = rng.Next(1, 6); t > 0; t--)
            {
                foreach (var line in w.Lines.Where((_, i) => i % 3 == step % 3).ToList())
                    line.TryInsert(w, rng.Next(0, line.Length), (Item)rng.Next(4));
                w.Tick();
            }
            int before = GoodsOnBelts(w);
            int roll = rng.Next(100);
            var cell = new Cell(rng.Next(4, 20), rng.Next(-3, 7));
            if (roll < 50)
            {
                var type = Pieces[rng.Next(Pieces.Length)];
                w.Apply(new Place(type, cell, (Dir)rng.Next(4), type == BuildingType.Trestle ? rng.Next(1, 5) : 0));
                Assert.Equal(before, GoodsOnBelts(w));
            }
            else if (roll < 70)
            {
                w.Apply(new Rotate(cell, (Dir)rng.Next(4)));
                Assert.Equal(before, GoodsOnBelts(w));
            }
            else
            {
                if (roll < 85)
                    w.Apply(new Remove(cell));
                else if (roll < 95)
                    w.Undo();
                else
                    w.Redo();
                Assert.True(GoodsOnBelts(w) <= before, $"step {step}: goods rose from {before} to {GoodsOnBelts(w)}");
            }
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void ARandomSessionReplaysAndSavesToTheSameHash(int seed)
    {
        var w = Rig.Fresh(seed: 1920);
        w.PlaceOk(BuildingType.MineHead, 5, -5);                // ore at (7,-4) keeps goods moving
        var rng = new Random(seed);
        for (int step = 0; step < 300; step++)
        {
            RandomAction(w, rng);
            for (int t = rng.Next(0, 12); t > 0; t--)
                w.Tick();
        }
        var log = w.Log.ToList();
        Assert.Contains(log, e => e.Command is Remove { Id: not null });
        Assert.Contains(log, e => e.Command is Place { Id: not null });

        var replay = Rig.Fresh(seed: 1920);
        int next = 0;
        while (replay.TickCount < w.TickCount)
        {
            while (next < log.Count && log[next].Tick == replay.TickCount)
                replay.Apply(log[next++].Command);
            replay.Tick();
        }
        while (next < log.Count)
            replay.Apply(log[next++].Command);
        Assert.Equal(w.StateHash(), replay.StateHash());

        var file = new MemoryStream();
        SaveGame.Write(w, file, "audit");
        file.Position = 0;
        var loaded = SaveGame.Read(file);
        Assert.Equal(w.StateHash(), loaded.StateHash());
        Assert.Equal(log, loaded.Log.ToList());
    }
}
