namespace FrontPageFoundry.Sim;

/// <summary>
/// Buys the ordered raw good at the market's buy price and puts it on the belt at its port, as fast
/// as the belt takes it (GDD §5). One of the two settings in the game; set by pipetting a good.
/// </summary>
public sealed partial class Dock : Building
{
    public Dock(int id, Cell origin, Dir facing) : base(id, BuildingType.ReceivingDock, origin, facing) { }

    /// <summary>The raw good on order, or nothing.</summary>
    public Item? Order { get; internal set; }
    public long UnitsBought { get; private set; }
    public long SpentCents { get; private set; }

    public Cell PortCell => OutputCells(1)[0];

    internal override void Update(World world)
    {
        if (Order is not { } item)
            return;
        long price = world.Market.BuyPriceCents(item);
        // CanAfford keeps the next two weekly payments in hand (GDD §8), so a dock left buying on credit
        // stops short of the limit instead of spending the last dollar and folding the company unwarned.
        if (!world.CanAfford(price))
            return;
        if (world.Offer(item, PortCell, Facing, BeltTiers.Spacing / 2))
        {
            world.Buy(item);
            UnitsBought++;
            SpentCents += price;
        }
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(Order.HasValue ? (long)Order.Value : -1);
        h.Mix(UnitsBought);
        h.Mix(SpentCents);
    }
}
