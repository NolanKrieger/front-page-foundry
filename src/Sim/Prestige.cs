namespace FrontPageFoundry.Sim;

/// <summary>One line of a commission's bill: how many of a good, and how many have come in.</summary>
public sealed class BillLine
{
    public BillLine(Item item, long needed)
    {
        Item = item;
        Needed = needed;
    }

    public Item Item { get; }
    public long Needed { get; }
    public long Delivered { get; internal set; }
    public bool Done => Delivered >= Needed;
}

/// <summary>
/// A prestige commission (GDD §6): a bill of goods to deliver to the Exposition Yard. The first six
/// are the fixed ones; from the seventh on they repeat with quantities × 1.5 a round and new names.
/// </summary>
public sealed class Commission
{
    internal Commission(int number, int template, string nameKey, string nameArg, IReadOnlyList<BillLine> bill)
    {
        Number = number;
        Template = template;
        NameKey = nameKey;
        NameArg = nameArg;
        Bill = bill;
        long value = 0;
        foreach (var line in bill)
            value += line.Needed * Items.BasePriceCents(line.Item);
        GoodwillCents = value * Commissions.GoodwillPercent / 100;
    }

    public int Number { get; }
    /// <summary>0–5: which of the six templates.</summary>
    public int Template { get; }
    /// <summary>Text key of the name; the first six have their own, later rounds reuse a template name.</summary>
    public string NameKey { get; }
    /// <summary>Text key of the name's argument (an ordinal, or an airship's name), or empty.</summary>
    public string NameArg { get; }
    public IReadOnlyList<BillLine> Bill { get; }
    /// <summary>What it adds to the share price for ever: a share of the bill at base prices (M14 placeholder).</summary>
    public long GoodwillCents { get; }
    public bool Done => Bill.All(l => l.Done);
    public int LinesDone => Bill.Count(l => l.Done);
}

public static class Commissions
{
    public const int GoodwillPercent = 25;
    public const int TemplateCount = 6;

    static readonly (Item Item, long Count)[][] Templates =
    {
        new[] { (Item.Automobile, 500L), (Item.MotorTruck, 200L) },
        new[] { (Item.SteelBeam, 4_000L), (Item.Rivets, 30_000L), (Item.Cement, 3_000L), (Item.PlateGlass, 2_500L) },
        new[] { (Item.Aeroplane, 100L) },
        new[] { (Item.SteelWire, 20_000L), (Item.SteelBeam, 3_000L), (Item.Cement, 8_000L) },
        new[] { (Item.AluminumGirder, 600L), (Item.GasCell, 80L), (Item.RadialAeroEngine, 6L), (Item.Gondola, 2L), (Item.DopedFabric, 400L) },
        new[] { (Item.StressedSkinPanel, 1_200L), (Item.RadialAeroEngine, 16L), (Item.Undercarriage, 8L), (Item.InstrumentPanel, 12L), (Item.Seat, 80L) },
    };

    /// <summary>The n-th commission (from 1). Round r = (n−1)/6 multiplies the template's quantities by 1.5^r.</summary>
    public static Commission Nth(int n)
    {
        int template = (n - 1) % TemplateCount;
        int round = (n - 1) / TemplateCount;
        // 1.5^r in integer arithmetic: multiply by 3, divide by 2, r times, rounding up.
        var bill = new List<BillLine>();
        foreach (var (item, count) in Templates[template])
        {
            long q = count;
            for (int r = 0; r < round; r++)
                q = (q * 3 + 1) / 2;
            bill.Add(new BillLine(item, q));
        }
        string nameKey = round == 0 ? $"COMMISSION_{n}" : $"COMMISSION_AGAIN_{template + 1}";
        string nameArg = round == 0 ? "" : template == 4 ? $"SHIP_{round + 1}" : $"ORDINAL_{round + 1}";
        return new Commission(n, template, nameKey, nameArg, bill);
    }
}

/// <summary>The company's standing with the Exposition: the commission open now and the ones finished (GDD §6).</summary>
public sealed partial class Prestige
{
    public Commission? Current { get; private set; }
    public int Completed { get; private set; }
    /// <summary>Goods of every kind the yard has taken and sold.</summary>
    public long Delivered { get; private set; }

    /// <summary>The first yard opens the first commission; later ones open as each is finished.</summary>
    internal void Open(World w)
    {
        if (Current != null)
            return;
        Current = Commissions.Nth(Completed + 1);
        w.Paper.OnCommission(w, Current, done: false);
    }

    /// <summary>A good delivered to the yard: counted against the bill if the bill wants it.</summary>
    internal void Deliver(World w, Item item)
    {
        Delivered++;
        if (Current is not { } c)
            return;
        foreach (var line in c.Bill)
        {
            if (line.Item != item || line.Done)
                continue;
            line.Delivered++;
            if (c.Done)
            {
                Completed++;
                w.GoodwillCents += c.GoodwillCents;
                w.Paper.OnCommission(w, c, done: true);
                Current = null;
                Open(w);
            }
            return;
        }
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(Completed);
        h.Mix(Delivered);
        h.Mix(Current?.Number ?? 0);
        if (Current != null)
            foreach (var line in Current.Bill)
                h.Mix(line.Delivered);
    }
}

/// <summary>The Exposition Yard (3×3): takes any good, pays the market price like a depot, and counts it toward the commission.</summary>
public sealed partial class Yard : Building
{
    public Yard(int id, Cell origin, Dir facing) : base(id, BuildingType.ExpositionYard, origin, facing) { }

    public long Taken { get; private set; }

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        world.Sell(item);
        world.Prestige.Deliver(world, item);
        Taken++;
        return true;
    }

    internal override void Hash(ref StateHasher h) => h.Mix(Taken);
}
