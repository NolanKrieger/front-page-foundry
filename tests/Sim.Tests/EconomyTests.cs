using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class MarketTests
{
    static Market Fresh(int seed = 1) => new(seed);
    /// <summary>A market whose trends stand still, for exact price checks.</summary>
    static Market Still(int seed = 1) => new(seed, Difficulty.SteadyTrade with { TrendSigmaMilli = 0 });

    static void Hours(Market m, int hours)
    {
        for (int t = 1; t <= hours * Market.TicksPerHour; t++)
            m.Tick(t);
    }

    [Fact]
    public void SellingHalfADepthHalvesThePriceAndItRecovers()
    {
        var m = Still();
        long fresh = m.PriceCents(Item.IronOre);
        Assert.Equal(200, fresh);
        for (int i = 0; i < (int)Items.MarketDepth(Item.IronOre); i++)
            m.Sell(Item.IronOre);
        // Counted at once, before the hour turns.
        Assert.Equal(fresh / 2, m.PriceCents(Item.IronOre));
        Assert.InRange(m.Discount(Item.IronOre), 0.49, 0.51);
        Hours(m, 5 * 48);
        Assert.InRange(m.PriceCents(Item.IronOre), fresh * 0.985, fresh);
    }

    [Fact]
    public void BuyingPushesTheBuyPriceUp()
    {
        var m = Fresh();
        Assert.Equal(200, m.BuyPriceCents(Item.IronOre));
        for (int i = 0; i < 5000; i++)
            m.Buy(Item.IronOre);
        Assert.Equal(400, m.BuyPriceCents(Item.IronOre));
        Assert.Equal(200, m.PriceCents(Item.IronOre));
    }

    [Fact]
    public void TrendsStayInBoundsAndFollowTheSeed()
    {
        var a = Fresh(7);
        var b = Fresh(7);
        var c = Fresh(8);
        Hours(a, 24 * 60);
        Hours(b, 24 * 60);
        Hours(c, 24 * 60);
        bool moved = false;
        foreach (var item in Items.All)
        {
            Assert.Equal(a.TrendMilli(item), b.TrendMilli(item));
            Assert.InRange(a.TrendMilli(item), 600, 1600);
            moved |= a.TrendMilli(item) != 1000;
        }
        Assert.True(moved, "trends drift");
        Assert.True(Items.All.Any(i => a.TrendMilli(i) != c.TrendMilli(i)), "seeds differ");
    }

    [Fact]
    public void EventsMultiplyPricesForTheirDaysThenExpire()
    {
        var m = Still();
        var glut = MarketEvent.All.First(e => e.Id == "steel_glut");
        m.Force(glut, 0);
        Assert.Equal(1260, m.PriceCents(Item.SteelIngot));
        Assert.Equal(1800, m.BuyPriceCents(Item.SteelIngot));
        Assert.Single(m.Events);
        Hours(m, 24 * 5);
        Assert.Empty(m.Events);
        Assert.Equal(1800, m.PriceCents(Item.SteelIngot));

        var rate = MarketEvent.All.First(e => e.Id == "bank_rate");
        m.Force(rate, 0);
        Assert.Equal(5, m.InterestPointsPermille);
        var mail = MarketEvent.All.First(e => e.Id == "air_mail");
        m.Force(mail, 0);
        Assert.Equal(12, m.Depth(Item.Aeroplane));
    }

    [Fact]
    public void DepthGrowsThreePercentAMonthAndEventsRollFromTheSeed()
    {
        var m = Fresh(3);
        Assert.Equal(5000, m.Depth(Item.IronOre));
        Hours(m, 24 * 30);
        Assert.Equal(5150, m.Depth(Item.IronOre));

        var a = Fresh(5);
        var b = Fresh(5);
        int startedA = 0, startedB = 0;
        for (int t = 1; t <= 24 * 200 * Market.TicksPerHour; t++)
        {
            a.Tick(t);
            b.Tick(t);
            startedA += a.Started.Count;
            startedB += b.Started.Count;
        }
        Assert.Equal(startedA, startedB);
        Assert.InRange(startedA, 8, 32);
    }
}

public class EconomyTests
{
    static readonly Difficulty NoCredit = Difficulty.SteadyTrade with { BaseCreditCents = 0 };

    [Fact]
    public void DockBuysOntoItsBeltAtTheBuyPriceAndStopsWhenBroke()
    {
        var w = new World(1, 200_000, NoCredit);
        w.PlaceOk(BuildingType.ReceivingDock, 0, 0, Dir.East);
        var dock = (Dock)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(new Cell(2, 1), dock.PortCell);
        w.BeltRow(BuildingType.BeltSteel, 2, 4, 1);
        long cash = w.CashCents;
        Assert.Equal(PlaceResult.Blocked, w.Apply(new SetOrder(new Cell(0, 0), Item.SteelPlate)));
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetOrder(new Cell(0, 0), Item.Coal)));
        w.Run(3);
        var line = w.BeltAt(2, 1).Line!;
        Assert.Equal(6, line.Count);
        Assert.All(line.Goods, g => Assert.Equal(Item.Coal, g.Item));
        Assert.Equal(6, dock.UnitsBought);
        Assert.Equal(cash - 6 * 150, w.CashCents);
        Assert.Equal(6 * 150, w.SpentCents);

        // Sell the coal on, and it keeps buying; then run out of money.
        w.PlaceOk(BuildingType.FreightDepot, 5, 0);
        w.Run(60);
        Assert.True(dock.UnitsBought > 100);
        // Coal costs more than it fetches once bought in bulk, so the till empties.
        var poor = new World(1, 900, NoCredit);
        poor.PlaceOk(BuildingType.BeltCanvas, 40, -40);
        Assert.Equal(PlaceResult.TooExpensive, poor.Place(BuildingType.BeltCanvas, 41, -40));
        Assert.Equal(200, poor.CashCents);
    }

    [Fact]
    public void PurchasesDrawOnCreditUpToTheLimit()
    {
        var w = new World(1, 1_000);
        long limit0 = w.CreditLimitCents;
        Assert.Equal(1_000_000, limit0);
        Assert.Equal(PlaceResult.Ok, w.Place(BuildingType.MineHead, 5, -5));
        Assert.Equal(0, w.CashCents);
        // $400 plus four tiles of rural land at $2.
        Assert.Equal(39_800, w.DebtCents);
        // The mine head is worth 75% of $400 on resale; half of that raises the limit.
        Assert.Equal(1_000_000 + 30_000 / 2, w.CreditLimitCents);
        Assert.Equal(w.CreditLimitCents - 39_800, w.CreditAvailableCents);

        int placed = 0;
        while (w.Place(BuildingType.Smelter, 20 + 3 * (placed % 20), 20 + 3 * (placed / 20)) == PlaceResult.Ok)
            placed++;
        Assert.True(placed > 5);
        Assert.True(w.DebtCents <= w.CreditLimitCents);
        Assert.Equal(PlaceResult.TooExpensive, w.Place(BuildingType.OpenHearthFurnace, 80, 80));
    }

    [Fact]
    public void WeeklyInterestIsChargedAndCashRepaysDaily()
    {
        var w = new World(1, 0);
        w.Pay(100_000);
        Assert.Equal(100_000, w.DebtCents);
        Assert.Equal(1_000, w.NextInterestCents);
        w.Run(7 * 120);
        Assert.Equal(1_000, w.InterestPaidCents);
        Assert.Equal(101_000, w.DebtCents);
        Assert.Single(w.WeeklyProfits);
        // Borrowing $1,000 for nothing and paying $10 on it is a $1,010 worse week.
        Assert.Equal(-101_000, w.WeeklyProfits[0]);

        // Income repays the balance at the day's end.
        var earner = new World(1, 0);
        earner.Pay(50_000);
        earner.PlaceOk(BuildingType.MineHead, 5, -5);
        earner.PlaceOk(BuildingType.FreightDepot, 7, -5);
        // $400 mine + $600 depot + $16 of land on credit: $1,516 owed, then a day of ore sales comes off it at midnight.
        Assert.Equal(151_600, earner.DebtCents);
        earner.Run(120);
        Assert.True(earner.CashCents == 0 && earner.DebtCents < 137_000, $"cash {earner.CashCents} debt {earner.DebtCents}");
    }

    [Fact]
    public void OneMissedPaymentEndsTheCompany()
    {
        var w = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        w.Pay(100_000);
        Assert.Equal(0, w.CreditAvailableCents);
        w.Run(7 * 120);
        Assert.True(w.Defaulted);
        Assert.True(w.Ended);
        Assert.Contains(w.Notices, n => n.Key == "DEFAULT");
        Assert.Contains(w.Notices, n => n.Key == "FOLDS");
        ulong h = w.StateHash();
        long tick = w.TickCount;
        w.Run(10);
        Assert.Equal(tick, w.TickCount);
        Assert.Equal(h, w.StateHash());
        Assert.Equal(PlaceResult.Ended, w.Place(BuildingType.BeltCanvas, 0, 0));
    }

    [Fact]
    public void TheBankerWarnsAWeekAhead()
    {
        var w = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        w.Pay(99_000);
        // This week's interest ($9.90) fits in the $10 of credit left; next week's will not.
        w.Run(7 * 120);
        Assert.False(w.Defaulted);
        Assert.True(w.BankersWarning);
        Assert.Contains(w.Notices, n => n.Key == "BANKERS_WARNING");
        w.Run(7 * 120);
        Assert.True(w.Defaulted);
    }

    [Fact]
    public void SharePriceFollowsNetWorthAndProfit()
    {
        var w = new World(1, 500_000);
        Assert.Equal(50, w.SharePriceCents);
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        // $408 spent (land included), $300 resale: net worth $4,892 → 48 cents a share.
        Assert.Equal(48, w.SharePriceCents);
        w.PlaceOk(BuildingType.FreightDepot, 7, -5);
        w.Run(7 * 120);
        Assert.Single(w.WeeklyProfits);
        Assert.True(w.WeeklyProfits[0] > 0, "a week of ore sales is profit");
        Assert.True(w.SharePriceCents > 49);
    }

    [Fact]
    public void EconomyReplaysDeterministically()
    {
        static World Play(int seed)
        {
            var w = new World(seed, 200_000);
            w.PlaceOk(BuildingType.MineHead, 5, -5);
            w.BeltRow(BuildingType.BeltCanvas, 7, 9, -4);
            w.PlaceOk(BuildingType.FreightDepot, 10, -5);
            w.PlaceOk(BuildingType.ReceivingDock, -20, -12);
            w.BeltRow(BuildingType.BeltCanvas, -18, -15, -11);
            w.PlaceOk(BuildingType.FreightDepot, -14, -12);
            w.Apply(new SetOrder(new Cell(-20, -12), Item.IronOre));
            w.Run(3 * 120 + 7);
            return w;
        }
        var a = Play(42);
        var b = Play(42);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(a.SpentCents > 0 && a.RevenueCents > 0);
        Assert.NotEqual(a.StateHash(), Play(43).StateHash());
    }
}
