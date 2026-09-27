using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>
/// Economy audit: does the opening the tutorial teaches make money on every preset, with the real prices?
/// The works is laid with real commands on the real starter map: a mine head on the starter iron, five canvas
/// belts to a smelter, four to a depot; a coal mine on the starter coal feeding itself through a splitter and
/// sending the rest by splitters to the iron mine, the smelter and (after edition 4) the press, which takes half
/// the ingots and sends its plates back to the depot. The ledger is printed week by week.
/// </summary>
public class AuditViabilityTests
{
    readonly ITestOutputHelper output;
    public AuditViabilityTests(ITestOutputHelper output) => this.output = output;

    static void Put(World w, BuildingType t, int x, int y, Dir f = Dir.East) =>
        Assert.True(w.Apply(new Place(t, new Cell(x, y), f)) == PlaceResult.Ok, $"{t} at ({x},{y})");

    static void Line(World w, int x0, int y0, int x1, int y1, Dir f)
    {
        int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
        for (int x = x0, y = y0; ; x += dx, y += dy)
        {
            Put(w, BuildingType.BeltCanvas, x, y, f);
            if (x == x1 && y == y1)
                break;
        }
    }

    static string D(long cents) => (cents / 100.0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <param name="coal">Lay the coal mine and its splitters (without it the starter bags burn out in days).</param>
    World Play(Difficulty d, bool coal, int weeks, out long firstWorth)
    {
        var w = new World(1, null, d);
        w.Tick();
        Put(w, BuildingType.MineHead, 5, -5);
        Line(w, 7, -4, 11, -4, Dir.East);
        Put(w, BuildingType.Smelter, 12, -5);
        Line(w, 14, -4, 17, -4, Dir.East);
        Put(w, BuildingType.FreightDepot, 18, -5);
        if (coal)
        {
            Put(w, BuildingType.MineHead, -10, 3);
            Put(w, BuildingType.BeltCanvas, -8, 4);
            Put(w, BuildingType.Splitter, -7, 4);
            Put(w, BuildingType.BeltCanvas, -6, 4, Dir.North);
            Line(w, -6, 3, -8, 3, Dir.West);
            Line(w, -6, 5, 3, 5, Dir.East);
            Put(w, BuildingType.Splitter, 4, 5);
            Line(w, 5, 5, 5, -3, Dir.North);
            Line(w, 5, 6, 11, 6, Dir.East);
            Line(w, 12, 6, 12, 2, Dir.North);
            Put(w, BuildingType.Splitter, 12, 1, Dir.North);
            Line(w, 12, 0, 12, -3, Dir.North);
            Line(w, 13, 0, 16, 0, Dir.East);
            Line(w, 17, 0, 17, -1, Dir.North);
        }
        firstWorth = w.NetWorthCents;
        output.WriteLine($"{d.Id}{(coal ? "" : ", no coal")}: opening spent {D(d.StartingCashCents - w.CashCents + w.DebtCents)}, cash {D(w.CashCents)}, debt {D(w.DebtCents)}, limit {D(w.CreditLimitCents)}");
        bool press = false;
        for (int week = 1; week <= weeks && !w.Ended; week++)
        {
            for (int t = 0; t < World.TicksPerWeek && !w.Ended; t++)
            {
                w.Tick();
                Assert.False(w.BankersWarning, $"{d.Id}: warned on day {w.Day}");
                if (coal && !press && w.Paper.TutorialStep >= 4)
                {
                    // Edition 4: split the ingot line and feed some to a press; its plates go back to the depot.
                    w.Apply(new Remove(new Cell(15, -4)));
                    w.Apply(new Remove(new Cell(16, -4)));
                    Put(w, BuildingType.Splitter, 15, -4);
                    Put(w, BuildingType.BeltCanvas, 16, -4);
                    Put(w, BuildingType.BeltCanvas, 16, -3);
                    Put(w, BuildingType.Press, 17, -3);
                    Put(w, BuildingType.BeltCanvas, 19, -2, Dir.North);
                    Put(w, BuildingType.BeltCanvas, 19, -3, Dir.North);
                    press = true;
                    output.WriteLine($"  press bought on day {w.Day}: cash {D(w.CashCents)}, debt {D(w.DebtCents)}");
                }
            }
            output.WriteLine($"  week {week}: cash {D(w.CashCents),7} debt {D(w.DebtCents),6} limit {D(w.CreditLimitCents),6} interest paid {D(w.InterestPaidCents),4} " +
                $"revenue {D(w.RevenueCents),7} net worth {D(w.NetWorthCents),7} week's profit {D(w.WeeklyProfits.LastOrDefault()),6} share ${w.SharePriceCents / 100.0:F2} ed. {w.Paper.TutorialStep}");
        }
        return w;
    }

    [Fact]
    public void TheTaughtOpeningPaysOnEveryPreset()
    {
        foreach (var d in Difficulty.All)
        {
            var w = Play(d, coal: true, weeks: 6, out long start);
            Assert.False(w.Ended);
            Assert.Equal(6, w.Paper.TutorialStep);
            Assert.True(w.NetWorthCents > start + 500_000, $"{d.Id}: six weeks earned only {D(w.NetWorthCents - start)}");
            Assert.All(w.WeeklyProfits.Skip(1), p => Assert.True(p > 100_000, $"{d.Id}: a week earned {D(p)}"));
            Assert.Equal(0, w.DebtCents);
            Assert.True(w.SharePriceCents > 0);
        }
    }

    [Fact]
    public void AFounderWhoBuildsALittleAndWaitsIsNeverFolded()
    {
        foreach (var d in Difficulty.All)
        {
            // Mine, belts, smelter and depot on the starter bags of coal alone: the fires go out in days.
            var w = Play(d, coal: false, weeks: 6, out _);
            Assert.False(w.Ended);
            Assert.Contains(w.Paper.Editions, e => e.Key == "FIRES_OUT");
        }
    }
}
