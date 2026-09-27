using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>M11: the Exposition Yard, commissions 1–6 and the endless procedural ones.</summary>
public class PrestigeTests
{
    [Fact]
    public void TheFirstSixBillsMatchTheDesignAndLaterRoundsGrowByHalf()
    {
        var first = Commissions.Nth(1);
        Assert.Equal("COMMISSION_1", first.NameKey);
        Assert.Equal("", first.NameArg);
        Assert.Equal(new[] { (Item.Automobile, 500L), (Item.MotorTruck, 200L) }, first.Bill.Select(l => (l.Item, l.Needed)).ToArray());
        Assert.Equal(new[] { (Item.SteelBeam, 4_000L), (Item.Rivets, 30_000L), (Item.Cement, 3_000L), (Item.PlateGlass, 2_500L) }, Commissions.Nth(2).Bill.Select(l => (l.Item, l.Needed)).ToArray());
        Assert.Equal((Item.Aeroplane, 100L), Commissions.Nth(3).Bill.Select(l => (l.Item, l.Needed)).Single());
        Assert.Equal(5, Commissions.Nth(5).Bill.Count);
        Assert.Equal(5, Commissions.Nth(6).Bill.Count);
        Assert.True(first.GoodwillCents > 0);

        var seventh = Commissions.Nth(7);
        Assert.Equal(0, seventh.Template);
        Assert.Equal(new[] { (Item.Automobile, 750L), (Item.MotorTruck, 300L) }, seventh.Bill.Select(l => (l.Item, l.Needed)).ToArray());
        Assert.Equal(("COMMISSION_AGAIN_1", "ORDINAL_2"), (seventh.NameKey, seventh.NameArg));
        Assert.Equal(("COMMISSION_AGAIN_5", "SHIP_2"), (Commissions.Nth(11).NameKey, Commissions.Nth(11).NameArg));
        var thirteenth = Commissions.Nth(13);
        Assert.Equal(new[] { (Item.Automobile, 1_125L), (Item.MotorTruck, 450L) }, thirteenth.Bill.Select(l => (l.Item, l.Needed)).ToArray());
        Assert.Equal("ORDINAL_3", thirteenth.NameArg);
        Assert.True(thirteenth.GoodwillCents > seventh.GoodwillCents && seventh.GoodwillCents > first.GoodwillCents);
    }

    static (World, Yard) WithYard()
    {
        var w = Rig.Fresh();
        Assert.Null(w.Prestige.Current);
        w.PlaceOk(BuildingType.ExpositionYard, 20, 20);
        return (w, (Yard)w.BuildingAt(new Cell(20, 20))!);
    }

    [Fact]
    public void TheYardOpensTheFirstCommissionPaysMarketPriceAndCountsOnlyTheBill()
    {
        var (w, yard) = WithYard();
        Assert.Equal(1, w.Prestige.Current!.Number);
        w.Run(130);
        Assert.Contains(w.Paper.Editions, e => e.Key == "COMMISSION_OPEN" && e.Args[0] == "COMMISSION_1");
        long cash = w.CashCents;
        long price = w.Market.PriceCents(Item.Automobile);
        Assert.True(yard.TryAccept(w, Item.Automobile, Dir.East, 60, yard.Origin));
        Assert.Equal(cash + price, w.CashCents);
        Assert.Equal(1, w.Prestige.Current.Bill[0].Delivered);
        Assert.True(yard.TryAccept(w, Item.Coal, Dir.East, 60, yard.Origin));
        Assert.Equal(2, w.Prestige.Delivered);
        Assert.Equal(1, w.Prestige.Current.Bill[0].Delivered);
        Assert.Equal(0, w.Prestige.Current.Bill[1].Delivered);
        Assert.Equal(2, yard.Taken);
    }

    [Fact]
    public void FillingTheBillLeavesGoodwillPrintsAndOpensTheNext()
    {
        var (w, yard) = WithYard();
        w.Run(130);
        long share = w.SharePriceCents;
        long worthBefore = w.NetWorthCents;
        for (int k = 0; k < 500; k++)
            yard.TryAccept(w, Item.Automobile, Dir.East, 60, yard.Origin);
        for (int k = 0; k < 199; k++)
            yard.TryAccept(w, Item.MotorTruck, Dir.East, 60, yard.Origin);
        Assert.Equal(0, w.Prestige.Completed);
        Assert.Equal(1, w.Prestige.Current!.LinesDone);
        yard.TryAccept(w, Item.MotorTruck, Dir.East, 60, yard.Origin);
        Assert.Equal(1, w.Prestige.Completed);
        Assert.Equal(Commissions.Nth(1).GoodwillCents, w.GoodwillCents);
        Assert.Equal(2, w.Prestige.Current!.Number);
        Assert.True(w.SharePriceCents - share > (w.NetWorthCents - worthBefore) / World.SharesOutstanding, "goodwill lifts the share price beyond the sales");
        w.Run(130);
        Assert.Contains(w.Paper.Editions, e => e.Key == "COMMISSION_DONE" && e.Args[0] == "COMMISSION_1");
        w.Run(130);
        Assert.Equal(2, w.Paper.Editions.Count(e => e.Key == "COMMISSION_OPEN"));
        // Goodwill is for ever: it survives losing the yard.
        w.Apply(new Remove(new Cell(20, 20)));
        Assert.Equal(Commissions.Nth(1).GoodwillCents, w.GoodwillCents);
        Assert.Equal(2, w.Prestige.Current!.Number);
    }

    [Fact]
    public void PrestigeReplaysDeterministically()
    {
        static World Play()
        {
            var (w, yard) = WithYard();
            for (int k = 0; k < 40; k++)
                yard.TryAccept(w, k % 3 == 0 ? Item.MotorTruck : Item.Automobile, Dir.East, 60, yard.Origin);
            w.Run(2);
            return w;
        }
        Assert.Equal(Play().StateHash(), Play().StateHash());
    }
}
