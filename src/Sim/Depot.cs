namespace FrontPageFoundry.Sim;

/// <summary>Sells anything delivered to it at the current market price.</summary>
public sealed partial class Depot : Building
{
    public Depot(int id, Cell origin, Dir facing) : base(id, BuildingType.FreightDepot, origin, facing) { }

    public long ItemsSold { get; private set; }

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        world.Sell(item);
        ItemsSold++;
        return true;
    }

    internal override void Hash(ref StateHasher h) => h.Mix(ItemsSold);
}
