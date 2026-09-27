using System.Reflection;
using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>Regressions for the economy audit (money, market, paper, town, map): each test failed before its fix.</summary>
public class AuditEconomyTests
{
    static World SaveAndLoad(World w)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "audit");
        ms.Position = 0;
        return SaveGame.Read(ms);
    }

    // ---- Market -------------------------------------------------------------------------------

    [Fact]
    public void TrendsRevertTowardOneAtTheDesignedRate()
    {
        // ln(trend) is an OU walk: a step of ±σ an hour and a pull of 1/336 an hour, so it settles near
        // σ_step·√168 (33 / 63 / 93 milli-nats by preset). Integer truncation used to drop the pull entirely
        // inside ±336, and every preset drifted out to ~200 (a third of all goods beyond ×0.8 or ×1.25).
        foreach (var (d, limit) in new[] { (Difficulty.BoomTimes, 55), (Difficulty.SteadyTrade, 105), (Difficulty.HardTimes, 155) })
        {
            var m = new Market(1, d);
            for (long t = Market.TicksPerHour; t <= 365L * World.TicksPerDay; t += Market.TicksPerHour)
                m.Tick(t);
            double rms = Math.Sqrt(Items.All.Average(i => Math.Pow(1000 * Math.Log(m.TrendMilli(i) / 1000.0), 2)));
            Assert.True(rms < limit, $"{d.Id}: trends spread {rms:F0} milli-nats after a year (limit {limit})");
            Assert.True(rms > limit / 5, $"{d.Id}: trends still move ({rms:F0})");
        }
    }

    [Fact]
    public void NoEventMixLetsTheDockUndercutTheDepot()
    {
        // Buy-side-only supply events (copper strike, oil gusher, cotton tumble) used to put the dock's price under
        // the depot's, so a dock belted into a depot printed money for the event's days.
        foreach (var a in MarketEvent.All)
            foreach (var b in MarketEvent.All.Prepend(null))
            {
                if (b == a)
                    continue;
                var m = new Market(1, Difficulty.SteadyTrade with { TrendSigmaMilli = 0 });
                m.Force(a, 0);
                if (b != null)
                    m.Force(b, 0);
                foreach (var item in Items.All.Where(i => Items.Of(i).Tier == Tier.Raw))
                    Assert.True(m.BuyPriceCents(item) >= m.PriceCents(item), $"{a.Id}+{b?.Id}: {item} buys at {m.BuyPriceCents(item)} and sells at {m.PriceCents(item)}");
            }
    }

    [Fact]
    public void ADockBeltedIntoADepotNeverMakesMoney()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.ReceivingDock, 30, -30);
        w.BeltRow(BuildingType.BeltCanvas, 32, 34, -29);
        w.PlaceOk(BuildingType.FreightDepot, 35, -30);
        Assert.Equal(PlaceResult.Ok, w.Apply(new SetOrder(new Cell(30, -30), Item.CopperOre)));
        w.StartEvent(MarketEvent.All.First(e => e.Id == "copper_strike"));
        long cash = w.CashCents;
        w.Run(120);
        var depot = (Depot)w.BuildingAt(new Cell(35, -30))!;
        Assert.True(depot.ItemsSold > 100, $"the loop ran ({depot.ItemsSold} sold)");
        Assert.True(w.CashCents <= cash, $"a dock-to-depot loop made {(w.CashCents - cash) / 100.0:F2} dollars in a day");
    }

    [Fact]
    public void DemandGrowthNeverOverflowsTheMarket()
    {
        // K grows 3% a month for ever; after ~55 in-game years the long arithmetic used to overflow.
        var m = new Market(1, Difficulty.SteadyTrade with { TrendSigmaMilli = 0 });
        for (long t = Market.TicksPerHour; t <= 60L * 360 * World.TicksPerDay; t += Market.TicksPerHour)
            m.Tick(t);
        foreach (var item in Items.All)
        {
            Assert.InRange(m.PriceCents(item), Items.BasePriceCents(item) / 2, Items.BasePriceCents(item) * 2);
            Assert.InRange(m.BuyPriceCents(item), Items.BasePriceCents(item) / 2, Items.BasePriceCents(item) * 2);
            Assert.True(m.Depth(item) > 0);
        }
    }

    // ---- Credit, interest, default --------------------------------------------------------------

    /// <summary>Buys smelters that are fed nothing, then belts, until the bank says no.</summary>
    internal static void SpendEverything(World w)
    {
        int n = 0;
        while (w.Place(BuildingType.Smelter, -60 + 3 * (n % 30), -60 - 3 * (n / 30)) == PlaceResult.Ok)
            n++;
        for (int x = -100; x < 100 && w.Place(BuildingType.BeltCanvas, x, -120) == PlaceResult.Ok; x++) { }
    }

    [Fact]
    public void SpendingLateInTheWeekStillLeavesAWeeksWarningBeforeAnyDefault()
    {
        var w = new World(1, null, Difficulty.SteadyTrade);
        w.Run(6.5 * 120);
        SpendEverything(w);
        Assert.True(w.DebtCents > 500_000, $"the credit line was used ({w.DebtCents})");
        w.Run(4 * 7 * 120);
        var fold = w.Notices.FirstOrDefault(n => n.Key == "DEFAULT");
        Assert.NotNull(fold);
        Assert.Contains(w.Notices, n => n.Key == "BANKERS_WARNING" && n.Tick <= fold!.Tick - World.TicksPerWeek);
        Assert.Contains(w.Paper.Editions, e => e.Key == "BANKERS_WARNING");
    }

    [Fact]
    public void TheWarningIsPrintedAsSoonAsItIsTrueNotOnlyAtTheWeeksEnd()
    {
        var w = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        w.Run(2 * 120);
        Assert.False(w.BankersWarning);
        w.Pay(99_950);   // mid-week the balance swells; next week's $9.99 is beyond the 50 cents left
        w.Run(10);
        Assert.True(w.BankersWarning);
        Assert.Contains(w.Notices, n => n.Key == "BANKERS_WARNING" && n.Tick < World.TicksPerWeek);
        w.Run(120);
        Assert.Single(w.Notices, n => n.Key == "BANKERS_WARNING");
    }

    [Fact]
    public void TheSharePriceIsNeverNegative()
    {
        // A founder who buys a works and earns nothing yet: the week's "loss" is the quarter every purchase
        // gives up on resale, and four times it used to drive the share below zero.
        var w = new World(1, null, Difficulty.SteadyTrade);
        SpendEverything(w);
        w.Run(7 * 120);
        Assert.True(w.WeeklyProfits[0] < 0);
        Assert.True(w.SharePriceCents >= 0, $"share {w.SharePriceCents}");
    }

    [Fact]
    public void NothingChangesAfterTheCompanyFolds()
    {
        var w = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        w.PlaceOk(BuildingType.BeltCanvas, 40, -40);
        w.Pay(99_300);
        w.Run(7 * 120);
        Assert.True(w.Ended);
        ulong h = w.StateHash();
        long cash = w.CashCents;
        w.Undo();
        w.Redo();
        Assert.Equal(cash, w.CashCents);
        Assert.Equal(h, w.StateHash());
        Assert.NotNull(w.BuildingAt(new Cell(40, -40)));
    }

    [Fact]
    public void UndoNeverRefundsABuildingThatIsNoLongerThere()
    {
        // Place a dear furnace, demolish it, let a plan put a belt on its square, then undo back past the belt:
        // the old "remove what I placed here" step used to take the belt away and refund the furnace in full.
        var w = Rig.Fresh();
        long cash = w.CashCents;
        w.PlaceOk(BuildingType.OpenHearthFurnace, 40, -40);
        Assert.Equal(PlaceResult.Ok, w.Apply(new Remove(new Cell(40, -40))));
        w.Apply(new SetPlanning(true));
        Assert.Equal(PlaceResult.Ok, w.Apply(new Draft(BuildingType.BeltCanvas, new Cell(40, -40), Dir.East)));
        w.Apply(new SetPlanning(false));
        w.Tick();
        Assert.IsType<Belt>(w.BuildingAt(new Cell(40, -40)));
        for (int i = 0; i < 4; i++)
            w.Undo();
        Assert.True(w.CashCents <= cash, $"undo paid out {(w.CashCents - cash) / 100.0:F2} dollars more than was ever spent");
    }

    // ---- The paper ----------------------------------------------------------------------------

    [Fact]
    public void EveryEventAndEveryWarningMakesThePaperNotJustTheFirst()
    {
        var w = Rig.Fresh();
        w.Tick();
        var drought = MarketEvent.All.First(e => e.Id == "drought");
        w.StartEvent(drought);
        w.Run(6 * 120);
        Assert.DoesNotContain(w.Market.Events, e => e.Event == drought);
        w.StartEvent(drought);
        w.Run(3 * 120);
        Assert.True(w.Paper.Editions.Count(e => e.Key == "EVENT_DROUGHT") >= 2, "the second drought made no headline");
    }

    [Fact]
    public void ThePapersHashIsTheSameInEveryProcess()
    {
        // string.GetHashCode is seeded per process, so the state hash of any world with an edition differed run to run.
        var w = Rig.Fresh(seed: 5);
        w.Tick();
        var h = StateHasher.Start();
        w.Paper.Hash(ref h);
        Assert.Equal(PaperHashAfterTheFirstEdition, h.Value);
    }

    internal const ulong PaperHashAfterTheFirstEdition = 1605636224476433319;

    static World TelegramDesk(int seed)
    {
        var w = Rig.Fresh(seed);
        w.Tick();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.Made(Item.IronIngot, 1);
        for (int i = 0; i < 400; i++)
            w.Sell(Item.IronIngot);
        w.Run(6);
        // Parts made in the reverse of their table order, so the order they were made differs from the order a save keeps.
        foreach (var item in new[] { Item.Gears, Item.Bolts, Item.CopperWire, Item.SteelRod, Item.SteelPlate })
            w.Made(item, 1);
        w.Made(Item.IronPlate, 1);
        w.Run(2 * 120);
        Assert.Equal(6, w.Paper.TutorialStep);
        return w;
    }

    [Fact]
    public void TelegramsAskForTheSameGoodsAfterASaveAndLoad()
    {
        var w = TelegramDesk(9);
        var loaded = SaveAndLoad(w);
        Assert.Equal(w.StateHash(), loaded.StateHash());
        w.Run(40 * 120);
        loaded.Run(40 * 120);
        Assert.True(w.Paper.Telegrams.Count > 8);
        Assert.Equal(w.Paper.Telegrams.Select(t => t.Item), loaded.Paper.Telegrams.Select(t => t.Item));
        Assert.Equal(w.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void OneSaleCountsTowardOneTelegramOnly()
    {
        var w = TelegramDesk(9);
        var wire = typeof(Paper).GetMethod("Wire", BindingFlags.NonPublic | BindingFlags.Instance)!;
        wire.Invoke(w.Paper, new object[] { w, Item.IronPlate, 20, 4 });
        var open = w.Paper.Telegrams.Where(t => t.Item == Item.IronPlate && t.State == TelegramState.Open).ToList();
        Assert.Equal(2, open.Count);
        for (int i = 0; i < 20; i++)
            w.Sell(Item.IronPlate);
        Assert.Equal(TelegramState.Filled, open[0].State);
        Assert.Equal(TelegramState.Open, open[1].State);
        Assert.Equal(0, open[1].Delivered);
    }

    [Fact]
    public void TheTutorialFinishesForAPlayerWhoNeverBuysAPress()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.Made(Item.IronIngot, 1);
        for (int i = 0; i < 400; i++)
            w.Sell(Item.IronIngot);
        w.Run(6);
        Assert.Equal(4, w.Paper.TutorialStep);
        w.Run(8 * 120);
        Assert.Equal(6, w.Paper.TutorialStep);
        Assert.NotEmpty(w.Paper.Telegrams);
    }

    [Fact]
    public void TheTutorialFinishesForAPlayerWhoPressesEveryIngot()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.Made(Item.IronIngot, 1);
        Assert.Equal(3, w.Paper.TutorialStep);
        // Every ingot goes into a press; only plates are sold, so iron itself never gluts.
        for (int i = 0; i < 50; i++)
        {
            w.Made(Item.IronPlate, 1);
            w.Sell(Item.IronPlate);
        }
        w.Run(3 * 120);
        Assert.Equal(6, w.Paper.TutorialStep);
    }

    [Fact]
    public void TheFirstEditionsQuoteThePresetsOwnMoney()
    {
        foreach (var d in Difficulty.All)
        {
            var w = new World(1, null, d);
            w.Tick();
            Assert.Equal(new[] { d.StartingCashCents.ToString() }, w.Paper.Editions[0].Args);
            w.PlaceOk(BuildingType.MineHead, 5, -5);
            w.Made(Item.IronIngot, 1);
            for (int i = 0; i < 400; i++)
                w.Sell(Item.IronIngot);
            w.Run(6);
            w.Made(Item.IronPlate, 1);
            var tut5 = w.Paper.Editions.Single(e => e.Key == "TUT5");
            Assert.Equal(new[] { d.BaseCreditCents.ToString() }, tut5.Args);
        }
    }

    [Fact]
    public void ThePaperSaysWhenTheFiresGoOut()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        for (int t = 0; t < 470 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        Assert.Equal(MachineState.Unpowered, smelter.State);
        w.Run(2 * 120);
        var ed = Assert.Single(w.Paper.Editions, e => e.Key == "FIRES_OUT");
        Assert.Equal(BuildingType.MineHead, ed.CircledAd);
    }

    // ---- Map and town -------------------------------------------------------------------------

    [Fact]
    public void HarderPresetsHaveThinnerSeamsAndASaveKeepsThem()
    {
        foreach (var d in Difficulty.All)
        {
            var w = new World(1, null, d);
            long expected = MapGen.StarterRichness * d.RichnessPercent / 100;
            Assert.Equal(expected, w.TileAt(MapGen.StarterPatch).Patch!.Richness);
            Assert.Equal(expected, SaveAndLoad(w).TileAt(MapGen.StarterPatch).Patch!.Richness);
        }
        // Dealt patches too, against the same map at the standard richness.
        var plain = new MapGen(1);
        var hard = new World(1, null, Difficulty.HardTimes);
        var far = Enumerable.Range(3, 20).SelectMany(cx => plain.PatchesIn(cx, 3)).First();
        Assert.Equal(far.Richness * 70 / 100, hard.Map.PatchesIn((int)Math.Floor(far.Centre.X / 32.0), 3).First(p => p.Id == far.Id).Richness);
    }

    [Fact]
    public void TheTownKeepsGrowingForYears()
    {
        var w = new World(1, Rig.Rich);
        int founded = w.Town.Blocks;
        w.Run(90 * 120);
        Assert.True(w.Town.Blocks >= founded + 25, $"the town grew from {founded} to {w.Town.Blocks} blocks in 90 days");
        Assert.All(w.Town.Tiles, t => Assert.False(w.Map.TileAt(t).IsSeam || w.Map.TerrainAt(t) == Terrain.River));
    }

    [Fact]
    public void TheTownGrowsTheSameAfterASaveAndLoad()
    {
        var w = new World(3, Rig.Rich);
        w.Run(12 * 120);
        var loaded = SaveAndLoad(w);
        Assert.Equal(w.StateHash(), loaded.StateHash());
        w.Run(40 * 120);
        loaded.Run(40 * 120);
        Assert.Equal(w.Town.Tiles.OrderBy(c => c), loaded.Town.Tiles.OrderBy(c => c));
        Assert.Equal(w.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void TheTownNeverBuildsInTheDamsLake()
    {
        var w = Rig.Fresh();
        Cell? site = null;
        var facing = Dir.North;
        for (int x = 40; x < 400 && site == null; x++)
        {
            int c = (int)Math.Floor(w.Map.RiverCentre(0, x));
            foreach (var f in new[] { Dir.North, Dir.East })
                for (int y = c - 3; y <= c + 1 && site == null; y++)
                    if (w.CanPlace(BuildingType.HydroDam, new Cell(x, y), f) == PlaceResult.Ok)
                    {
                        site = new Cell(x, y);
                        facing = f;
                    }
        }
        Assert.NotNull(site);
        w.PlaceOk(BuildingType.HydroDam, site!.Value.X, site.Value.Y, facing);
        var tryAdd = typeof(Town).GetMethod("TryAddBlock", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int blocks = w.Town.Blocks;
        foreach (var c in w.FloodOf(site.Value, facing))
            Assert.False((bool)tryAdd.Invoke(w.Town, new object[] { w, c })!, $"a block went up in the lake at {c}");
        Assert.Equal(blocks, w.Town.Blocks);
    }
}
