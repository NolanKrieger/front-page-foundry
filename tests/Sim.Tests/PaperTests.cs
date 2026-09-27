using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class PaperTests
{
    [Fact]
    public void TheFirstEditionPrintsAtOnceAndCirclesTheMineHead()
    {
        var w = Rig.Fresh();
        Assert.Empty(w.Paper.Editions);
        w.Tick();
        var ed = Assert.Single(w.Paper.Editions);
        Assert.Equal("TUT1", ed.Key);
        Assert.Equal(1, ed.Number);
        Assert.Equal(BuildingType.MineHead, ed.CircledAd);
        Assert.Same(ed, w.Paper.JustPrinted);
        w.Tick();
        Assert.Null(w.Paper.JustPrinted);
    }

    [Fact]
    public void TheTutorialFollowsThePlayersFirstMoves()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        Assert.Equal("TUT2", w.Paper.Latest!.Key);
        Assert.Equal(BuildingType.Smelter, w.Paper.Latest.CircledAd);
        Assert.Equal(2, w.Paper.TutorialStep);

        // Ore to a smelter: the first ingot brings edition 3 (the depot).
        w.BeltRow(BuildingType.BeltCanvas, 7, 8, -4);
        w.PlaceOk(BuildingType.Smelter, 9, -5, Dir.East);
        w.Run(10);
        Assert.Equal("TUT3", w.Paper.Latest!.Key);
        Assert.Equal(BuildingType.FreightDepot, w.Paper.Latest.CircledAd);

        // Sell ingots until the market sags, and the paper frets about too much iron.
        w.PlaceOk(BuildingType.FreightDepot, 11, -5, Dir.East);
        for (int i = 0; i < 400; i++)
            w.Sell(Item.IronIngot);
        w.Run(6);
        Assert.Equal("TUT4", w.Paper.Latest!.Key);
        Assert.Equal(BuildingType.Press, w.Paper.Latest.CircledAd);
        Assert.Contains("FIRST_SALE", w.Paper.Latest.Inside);

        // The first plate brings the banker; a day later, the telegram desk opens with an order.
        w.Made(Item.IronPlate, 1);
        Assert.Equal("TUT5", w.Paper.Latest!.Key);
        w.Run(2 * 120);
        Assert.Equal("TUT6", w.Paper.Latest!.Key);
        Assert.Equal(6, w.Paper.TutorialStep);
        var order = Assert.Single(w.Paper.Telegrams);
        Assert.Equal(Item.IronPlate, order.Item);
        Assert.Equal(TelegramState.Open, order.State);
        Assert.Equal(20, order.Quantity);
    }

    [Fact]
    public void OneFrontPageADayAndTheRestWaitsForTomorrow()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.StartEvent(MarketEvent.All[0]);
        w.Tick();
        w.StartEvent(MarketEvent.All[1]);
        w.Tick();
        // The first event took today's front page; the second waits.
        Assert.Equal("EVENT_RUBBER_SHORTAGE", w.Paper.Latest!.Key);
        Assert.Equal(2, w.Paper.Editions.Count);
        w.Run(120);
        Assert.Equal("EVENT_MOTOR_CRAZE", w.Paper.Latest!.Key);
        Assert.Equal(3, w.Paper.Editions.Count);
    }

    [Fact]
    public void TelegramsFillBySellingAndMissWhenDue()
    {
        var w = Rig.Fresh();
        w.Tick();
        // Fast-forward the tutorial by making its goods directly.
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.Made(Item.IronIngot, 1);
        for (int i = 0; i < 400; i++)
            w.Sell(Item.IronIngot);
        w.Run(6);
        w.Made(Item.IronPlate, 1);
        w.Run(2 * 120);
        var order = Assert.Single(w.Paper.Telegrams);
        for (int i = 0; i < order.Quantity; i++)
            w.Sell(Item.IronPlate);
        Assert.Equal(TelegramState.Filled, order.State);
        Assert.Contains(w.Paper.Editions, e => e.Key == "TELEGRAM_FILLED");

        // Later orders arrive on their own; one left unfilled is missed, with a headline.
        w.Run(20 * 120);
        var missed = w.Paper.Telegrams.FirstOrDefault(t => t.State == TelegramState.Missed);
        Assert.NotNull(missed);
        Assert.Contains(w.Paper.Editions, e => e.Key == "TELEGRAM_MISSED");
        Assert.True(w.Paper.Telegrams.Count >= 3);
    }

    [Fact]
    public void MilestonesAndTheFoldPrint()
    {
        var w = Rig.Fresh();
        w.Tick();
        w.Made(Item.SteelIngot, 1);
        w.Made(Item.SteelIngot, 1);
        Assert.Single(w.Paper.Editions, e => e.Key == "FIRST_STEEL");
        w.Run(120);
        w.Made(Item.Automobile, 1);
        Assert.Contains(w.Paper.Editions, e => e.Key == "FIRST_CAR");

        var broke = new World(1, 0, Difficulty.SteadyTrade with { BaseCreditCents = 100_000 });
        broke.Pay(100_000);
        broke.Run(7 * 120);
        Assert.True(broke.Ended);
        Assert.Equal("FOLDS", broke.Paper.Latest!.Key);
    }

    [Fact]
    public void ThePaperIsPartOfTheDeterministicState()
    {
        static World Play()
        {
            var w = Rig.Fresh(seed: 77);
            w.Tick();
            w.PlaceOk(BuildingType.MineHead, 5, -5);
            w.Made(Item.IronIngot, 1);
            for (int i = 0; i < 400; i++)
                w.Sell(Item.IronIngot);
            w.Run(6);
            w.Made(Item.IronPlate, 1);
            w.Run(12 * 120);
            return w;
        }
        Assert.Equal(Play().StateHash(), Play().StateHash());
        Assert.NotEmpty(Play().Market.History(Item.IronIngot));
        Assert.Equal(7, Play().Market.History(Item.IronOre).Count);
    }
}
