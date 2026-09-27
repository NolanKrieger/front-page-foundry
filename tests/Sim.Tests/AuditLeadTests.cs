using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>Lead's merge checks for the 2026-09-23 audit: fixes that span two auditors' files.</summary>
public class AuditLeadTests
{
    static World SmelterBurntDown(out Cell at)
    {
        var w = Rig.Fresh();
        at = new Cell(0, 0);
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        var smelter = (Machine)w.BuildingAt(at)!;
        for (int t = 0; t < 300 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        return w;
    }

    [Fact]
    public void DemolishAndUndoPutTheFireBackAsItWasNotAFreshBag()
    {
        var w = SmelterBurntDown(out var at);
        var before = FireState.Of(w.BuildingAt(at)!)!.Value;
        Assert.True(before.Coal < Firebox.StarterCoal);
        long cash = w.CashCents;
        w.Apply(new Remove(at));
        Assert.True(w.Undo());
        Assert.Equal(before, FireState.Of(w.BuildingAt(at)!));
        Assert.Equal(cash, w.CashCents);
        Assert.True(w.Redo());   // demolished again
        Assert.True(w.Undo());   // and back, still with the same fire
        Assert.Equal(before, FireState.Of(w.BuildingAt(at)!));
    }

    [Fact]
    public void UndoAndRedoOfAPurchaseKeepTheFireItHad()
    {
        var w = SmelterBurntDown(out var at);
        var before = FireState.Of(w.BuildingAt(at)!)!.Value;
        // Undo the depot, then the smelter purchase itself; redo brings the smelter back with the fire it had.
        Assert.True(w.Undo());
        Assert.True(w.Undo());
        Assert.Null(w.BuildingAt(at));
        Assert.True(w.Redo());
        Assert.Equal(before, FireState.Of(w.BuildingAt(at)!));
    }

    [Fact]
    public void AnUndoRecordWithAFireSurvivesTheSaveLog()
    {
        var w = SmelterBurntDown(out var at);
        w.Apply(new Remove(at));
        Assert.True(w.Undo());
        var file = new MemoryStream();
        SaveGame.Write(w, file, "Fire Works");
        file.Position = 0;
        var back = SaveGame.Read(file);
        Assert.Equal(w.StateHash(), back.StateHash());
        Assert.Equal(w.Log.ToList(), back.Log.ToList());
        Assert.Contains(back.Log, e => e.Command is Place { Fire: not null });
    }

    [Fact]
    public void ADamsLakeSupersedesThePlansItDrowns()
    {
        var w = new World(1, Rig.Rich, mode: MapMode.RiverOnly);
        int x = 150;
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
        Cell? origin = null;
        for (int dx = 0; dx < 40 && origin == null; dx++)
            for (int oy = centre - 3; oy <= centre + 1; oy++)
                if (w.CanPlace(BuildingType.HydroDam, new Cell(x + dx, oy), Dir.North) == PlaceResult.Ok)
                {
                    origin = new Cell(x + dx, oy);
                    break;
                }
        Assert.NotNull(origin);
        var valley = w.FloodOf(origin!.Value, Dir.North).First(c => w.CanPlace(BuildingType.Smelter, c, Dir.East, asPlan: true) == PlaceResult.Ok);
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetPlanning(true)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.Smelter, valley, Dir.East)));
        Assert.True(w.Planned(valley));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Place(BuildingType.HydroDam, origin.Value, Dir.North)));
        Assert.True(w.Flooded(valley));
        Assert.False(w.Planned(valley));
        Assert.DoesNotContain(w.Plans, p => p.Type == BuildingType.Smelter);
    }

    /// <summary>
    /// Found by the merged self-test: a BANK RATE RAISED event starting on the very tick of a weekly payment raised
    /// that payment past the two-week reserve, and the company folded with no warning at all. The payment is now
    /// charged at the rate posted when the week began; a rise counts from the next week, a week after it is known.
    /// </summary>
    [Fact]
    public void ARateRiseOnPaymentDayCannotFoldACompanyUnwarned()
    {
        var bankRate = MarketEvent.All.Single(e => e.Id == "bank_rate");
        for (int weekOfRise = 1; weekOfRise <= 3; weekOfRise++)
        {
            var w = new World(1, null, Difficulty.SteadyTrade);
            w.Run(6.5 * 120);
            AuditEconomyTests.SpendEverything(w);
            Assert.True(w.DebtCents > 500_000, $"the credit line was used ({w.DebtCents})");
            while (w.TickCount < weekOfRise * (long)World.TicksPerWeek - 1)
                w.Tick();
            w.StartEvent(bankRate);        // the rate goes up on the tick before the payment
            for (int t = 0; t < 6 * World.TicksPerWeek && !w.Defaulted; t++)
                w.Tick();
            var fold = w.Notices.FirstOrDefault(n => n.Key == "DEFAULT");
            if (fold != null)
                Assert.Contains(w.Notices, n => n.Key == "BANKERS_WARNING" && n.Tick <= fold.Tick - World.TicksPerWeek);
        }
    }
}

