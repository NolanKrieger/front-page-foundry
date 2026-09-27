namespace FrontPageFoundry.Sim;

/// <summary>
/// A random fictional happening that moves prices for a while (GDD §8, §10). The paper prints
/// <see cref="HeadlineKey"/> when it starts. Interest and output effects are read by their systems.
/// </summary>
public sealed record MarketEvent(string Id, Item[] Items, int SellMilli, int BuyMilli, int DurationDays,
    int DepthMilli = 1000, int InterestPointsPermille = 0, int WaterOutputMilli = 1000)
{
    public string HeadlineKey => "EVENT_" + Id.ToUpperInvariant();

    public static readonly MarketEvent[] All =
    {
        new("rubber_shortage", new[] { Item.RawRubber }, 1000, 1800, 6),
        new("motor_craze", new[] { Item.Automobile }, 1400, 1000, 8),
        new("steel_glut", new[] { Item.SteelIngot, Item.SteelPlate, Item.SteelBeam }, 700, 1000, 5),
        // A supply glut is cheap at the docks and at the depots alike: a dock can never undersell a depot.
        new("copper_strike", new[] { Item.CopperOre }, 600, 600, 7),
        new("air_mail", new[] { Item.Aeroplane }, 1500, 1000, 10, DepthMilli: 2000),
        new("bank_rate", Array.Empty<Item>(), 1000, 1000, 14, InterestPointsPermille: 5),
        new("drought", Array.Empty<Item>(), 1000, 1000, 5, WaterOutputMilli: 500),
        new("oil_gusher", new[] { Item.CrudeOil }, 500, 500, 6),
        new("glass_demand", new[] { Item.Glass, Item.PlateGlass }, 1300, 1000, 7),
        new("cotton_tumble", new[] { Item.Cotton }, 700, 700, 4),
    };
}

/// <summary>An event in progress.</summary>
public sealed record ActiveEvent(MarketEvent Event, long StartTick, long EndTick);

/// <summary>
/// The market (GDD §8), in integer fixed point. Per good: sell = base × trend × event × K/(K+Q),
/// buy = base × trend × event × (K+B)/K, where Q and B are your own recent sales and purchases,
/// decaying with τ = 2 days. Trend is a slow mean-reverting random walk in log space, bounded
/// 0.6–1.6. Everything is stepped once per in-game hour from seeded PCG streams; sales within the
/// hour count at once so a dump cannot dodge the glut.
/// </summary>
public sealed partial class Market
{
    public const int TicksPerHour = World.TicksPerDay / 24;
    public const int HoursPerDay = 24;
    /// <summary>τ = 2 in-game days, for tests that wait it out.</summary>
    public const long RecoveryTicks = 2L * World.TicksPerDay;
    /// <summary>e^(−1/48) per hour as a fraction of 1024: two days to fall to 1/e.</summary>
    const int DecayNum = 1003, DecayDen = 1024;
    /// <summary>Trend mean reversion: 1/336 per hour (two weeks).</summary>
    const int ReversionHours = 336;
    const int TrendMinMilli = -511, TrendMaxMilli = 470;   // ln 0.6 .. ln 1.6
    const int Scale = 1000;
    /// <summary>
    /// Depth stops growing at a millionfold (after ~39 in-game years): beyond ~55 years of 3% a month the price
    /// arithmetic would overflow a long. No market that deep feels a glut anyway.
    /// </summary>
    const long MaxDemandGrowthMilli = 1_000_000_000;

    static readonly int[] ExpMilli = BuildExp();

    readonly int n = Items.All.Length;
    readonly long[] glutMilli, buyMilli;      // Q, B × 1000
    readonly int[] pendingSold, pendingBought;
    readonly int[] trendLogMilli;             // ln(trend) × 1000
    readonly Pcg32 trendRng, eventRng;
    readonly List<ActiveEvent> events = new();
    readonly Difficulty difficulty;
    long demandGrowthMilli = Scale;
    long hoursStepped;
    /// <summary>Seven days of closing sell prices per good, oldest first (the market page's sparkline).</summary>
    readonly long[][] history;

    public Market(int seed, Difficulty? difficulty = null)
    {
        this.difficulty = difficulty ?? Difficulty.SteadyTrade;
        glutMilli = new long[n];
        buyMilli = new long[n];
        pendingSold = new int[n];
        pendingBought = new int[n];
        trendLogMilli = new int[n];
        trendRng = new Pcg32((ulong)seed, 11);
        eventRng = new Pcg32((ulong)seed, 23);
        history = new long[n][];
        for (int i = 0; i < n; i++)
            history[i] = Array.Empty<long>();
    }

    public IReadOnlyList<long> History(Item item) => history[(int)item];

    public IReadOnlyList<ActiveEvent> Events => events;
    /// <summary>Events that started this tick, for the paper.</summary>
    public List<ActiveEvent> Started { get; } = new();

    static int[] BuildExp()
    {
        var table = new int[TrendMaxMilli - TrendMinMilli + 1];
        for (int i = 0; i < table.Length; i++)
            table[i] = (int)Math.Round(Math.Exp((TrendMinMilli + i) / 1000.0) * Scale);
        return table;
    }

    /// <summary>Recent units sold (your own glut), decaying.</summary>
    public double Glut(Item item) => (glutMilli[(int)item] + pendingSold[(int)item] * (long)Scale) / (double)Scale;
    public double Bought(Item item) => (buyMilli[(int)item] + pendingBought[(int)item] * (long)Scale) / (double)Scale;
    /// <summary>Trend multiplier as a fraction of 1000.</summary>
    public int TrendMilli(Item item) => ExpMilli[trendLogMilli[(int)item] - TrendMinMilli];
    /// <summary>Market depth K for a good today (grows 3% a month).</summary>
    public long Depth(Item item) => (long)Items.MarketDepth(item) * demandGrowthMilli * EventDepthMilli(item) / (Scale * Scale);
    /// <summary>Fraction of the base price currently lost to the glut.</summary>
    public double Discount(Item item)
    {
        long k = Depth(item) * Scale, q = glutMilli[(int)item] + pendingSold[(int)item] * (long)Scale;
        return 1 - k / (double)(k + q);
    }

    int EventSellMilli(Item item)
    {
        long m = Scale;
        foreach (var e in events)
            if (Array.IndexOf(e.Event.Items, item) >= 0)
                m = m * e.Event.SellMilli / Scale;
        return (int)m;
    }

    int EventBuyMilli(Item item)
    {
        long m = Scale;
        foreach (var e in events)
            if (Array.IndexOf(e.Event.Items, item) >= 0)
                m = m * e.Event.BuyMilli / Scale;
        return (int)m;
    }

    int EventDepthMilli(Item item)
    {
        long m = Scale;
        foreach (var e in events)
            if (Array.IndexOf(e.Event.Items, item) >= 0)
                m = m * e.Event.DepthMilli / Scale;
        return (int)m;
    }

    /// <summary>Extra weekly interest from events, in permille points.</summary>
    public int InterestPointsPermille => events.Sum(e => e.Event.InterestPointsPermille);
    /// <summary>Waterwheel and dam output multiplier from events, per mille.</summary>
    public int WaterOutputMilli => events.Aggregate((long)Scale, (m, e) => m * e.Event.WaterOutputMilli / Scale) is var m ? (int)m : Scale;

    long Base(Item item) => Items.BasePriceCents(item) * TrendMilli(item) / Scale;

    /// <summary>What the depot pays per unit right now.</summary>
    public long PriceCents(Item item)
    {
        long k = Depth(item) * Scale;
        long q = glutMilli[(int)item] + pendingSold[(int)item] * (long)Scale;
        return Math.Max(1, Base(item) * EventSellMilli(item) / Scale * k / (k + q));
    }

    /// <summary>What the receiving dock charges per unit right now; never less than the depot pays, so no loop prints money.</summary>
    public long BuyPriceCents(Item item)
    {
        long k = Depth(item) * Scale;
        long b = buyMilli[(int)item] + pendingBought[(int)item] * (long)Scale;
        return Math.Max(PriceCents(item), Base(item) * EventBuyMilli(item) / Scale * (k + b) / k);
    }

    /// <summary>Sells one unit and returns the price it fetched.</summary>
    internal long Sell(Item item)
    {
        long price = PriceCents(item);
        pendingSold[(int)item]++;
        return price;
    }

    /// <summary>Buys one unit and returns what it cost.</summary>
    internal long Buy(Item item)
    {
        long price = BuyPriceCents(item);
        pendingBought[(int)item]++;
        return price;
    }

    internal void Tick(long tick)
    {
        Started.Clear();
        if (tick % TicksPerHour != 0)
            return;
        hoursStepped++;
        for (int i = 0; i < n; i++)
        {
            glutMilli[i] = (glutMilli[i] + pendingSold[i] * (long)Scale) * DecayNum / DecayDen;
            buyMilli[i] = (buyMilli[i] + pendingBought[i] * (long)Scale) * DecayNum / DecayDen;
            pendingSold[i] = 0;
            pendingBought[i] = 0;
            int l = trendLogMilli[i];
            l += -Reversion(l) + trendRng.NextSigned(difficulty.TrendSigmaMilli);
            trendLogMilli[i] = Math.Clamp(l, TrendMinMilli, TrendMaxMilli);
        }
        if (hoursStepped % (HoursPerDay * 30) == 0)
            demandGrowthMilli = Math.Min(MaxDemandGrowthMilli, demandGrowthMilli * 1030 / Scale);
        events.RemoveAll(e => e.EndTick <= tick);
        if (hoursStepped % HoursPerDay == 0)
        {
            for (int i = 0; i < n; i++)
            {
                var h = history[i];
                var next = new long[Math.Min(7, h.Length + 1)];
                Array.Copy(h, Math.Max(0, h.Length - 6), next, 0, next.Length - 1);
                next[^1] = PriceCents(Items.All[i]);
                history[i] = next;
            }
            RollEvent(tick);
        }
    }

    /// <summary>
    /// This hour's pull back toward trend 1.0: l/336 milli-nats. Integer division alone would round every pull
    /// inside ±336 down to nothing, so prices would wander freely between ×0.7 and ×1.4; the remainder is instead
    /// rounded up at random in proportion, so the pull is right on average at every trend.
    /// </summary>
    int Reversion(int l)
    {
        int pull = l / ReversionHours, rest = l % ReversionHours;
        if (rest != 0 && trendRng.NextInt(ReversionHours) < Math.Abs(rest))
            pull += Math.Sign(rest);
        return pull;
    }

    /// <summary>Once a day: maybe one new fictional event, never one already running.</summary>
    void RollEvent(long tick)
    {
        if (!eventRng.Chance(difficulty.EventChancePercentPerDay, 100))
            return;
        var e = MarketEvent.All[eventRng.NextInt(MarketEvent.All.Length)];
        if (events.Any(a => a.Event == e))
            return;
        var active = new ActiveEvent(e, tick, tick + (long)e.DurationDays * World.TicksPerDay);
        events.Add(active);
        Started.Add(active);
    }

    /// <summary>Starts an event now (for tests and the tutorial).</summary>
    internal void Force(MarketEvent e, long tick)
    {
        var active = new ActiveEvent(e, tick, tick + (long)e.DurationDays * World.TicksPerDay);
        events.Add(active);
        Started.Add(active);
    }

    internal void Hash(ref StateHasher h)
    {
        for (int i = 0; i < n; i++)
        {
            h.Mix(glutMilli[i]);
            h.Mix(buyMilli[i]);
            h.Mix(pendingSold[i]);
            h.Mix(pendingBought[i]);
            h.Mix(trendLogMilli[i]);
        }
        h.Mix(demandGrowthMilli);
        h.Mix(hoursStepped);
        h.Mix((long)trendRng.State);
        h.Mix((long)eventRng.State);
        foreach (var e in events)
        {
            h.Mix(Array.IndexOf(MarketEvent.All, e.Event));
            h.Mix(e.EndTick);
        }
    }
}
