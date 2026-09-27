using System.Globalization;

namespace FrontPageFoundry.Sim;

/// <summary>
/// One printing of <i>The Carvell Falls Courier</i> (GDD §10). <see cref="Key"/> names the headline,
/// deck and body in the string table (HEADLINE_/DECK_/BODY_ + key); <see cref="Inside"/> are the
/// lesser items printed in the inside columns; <see cref="CircledAd"/> is the advertisement the
/// paper rings in red pencil (the tutorial's next move).
/// </summary>
public sealed record Edition(int Number, long Tick, string Key, string[] Args, string[] Inside, BuildingType? CircledAd);

public enum TelegramState : byte { Open, Filled, Missed }

/// <summary>An order from a fictional firm: sell this many of the good through any depot before it is due.</summary>
public sealed class Telegram
{
    public Telegram(int id, Item item, int quantity, long issuedTick, long dueTick, string sender, long soldAtIssue)
    {
        Id = id;
        Item = item;
        Quantity = quantity;
        IssuedTick = issuedTick;
        DueTick = dueTick;
        Sender = sender;
        SoldAtIssue = soldAtIssue;
    }

    public int Id { get; }
    public Item Item { get; }
    public int Quantity { get; }
    public long IssuedTick { get; }
    public long DueTick { get; }
    /// <summary>String-table key of the firm (TELEGRAM_FROM_ + sender).</summary>
    public string Sender { get; }
    public long SoldAtIssue { get; }
    public TelegramState State { get; internal set; }
    public long Delivered { get; internal set; }
}

/// <summary>
/// The newspaper's editor: watches what happens, prints editions, keeps the archive, runs the
/// tutorial's first six editions and the telegram desk. Deterministic and hashed like everything else.
/// </summary>
public sealed partial class Paper
{
    public const int TutorialEditions = 6;
    /// <summary>Days edition 4 waits for an iron plate before the banker's edition prints anyway.</summary>
    public const int TutorialPatienceDays = 3;
    /// <summary>Firms that wire orders.</summary>
    public static readonly string[] Senders = { "GARVEY", "PENROSE", "ALBANY_WORKS", "CUTTER_MOTOR", "HALLORAN", "STATE_ROAD" };

    readonly List<Edition> editions = new();
    readonly List<Telegram> telegrams = new();
    /// <summary>News waiting to print: front-page candidates take tomorrow's front page; lesser items ride inside the next edition.</summary>
    readonly List<(string Key, string[] Args, bool Front)> pending = new();
    readonly HashSet<string> printed = new();
    readonly HashSet<Item> made = new();
    readonly long[] sold = new long[Items.All.Length];
    readonly Pcg32 rng;
    long lastFrontDay = -1;
    long nextTelegramDay = long.MaxValue;
    int nextTelegramId = 1;

    public Paper(int seed) => rng = new Pcg32((ulong)seed, 37);

    public IReadOnlyList<Edition> Editions => editions;
    public Edition? Latest => editions.Count > 0 ? editions[^1] : null;
    public IReadOnlyList<Telegram> Telegrams => telegrams;
    /// <summary>How many tutorial editions have printed (0–6).</summary>
    public int TutorialStep { get; private set; }
    public bool Printed(string key) => printed.Contains(key);
    public long Sold(Item item) => sold[(int)item];
    public bool Made(Item item) => made.Contains(item);
    /// <summary>Set when an edition prints this tick, for the deck to react (press thunk, EXTRA flag).</summary>
    public Edition? JustPrinted { get; private set; }

    // ---- What the world reports ------------------------------------------------------------

    internal void OnBuilt(World w, BuildingType type)
    {
        CheckTutorial(w);
        switch (type)
        {
            case BuildingType.Waterwheel: Front(w, "FIRST_WATERWHEEL"); break;
            case BuildingType.Boiler: Front(w, "FIRST_BOILER"); break;
            case BuildingType.PowerStation: Front(w, "ELECTRIFICATION"); break;
            case BuildingType.HydroDam: Front(w, "DAM"); break;
            case BuildingType.TruckDepot: Front(w, "FIRST_DEPOT"); break;
            case BuildingType.RailStation: Front(w, "FIRST_RAIL"); break;
            case BuildingType.BargeLanding: Front(w, "FIRST_BARGE"); break;
        }
    }

    internal void OnMade(World w, Item item, int count)
    {
        bool first = made.Add(item);
        CheckTutorial(w);
        switch (item)
        {
            case Item.SteelIngot when first: Front(w, "FIRST_STEEL"); break;
            case Item.Automobile when first: Front(w, "FIRST_CAR"); break;
            case Item.MotorTruck when first: Front(w, "FIRST_TRUCK"); break;
            case Item.Aeroplane when first: Front(w, "FIRST_PLANE"); break;
        }
        if (item == Item.Automobile && Count(Item.Automobile, count) == 1000)
            Front(w, "CARS_1000");
    }

    readonly Dictionary<Item, long> madeCount = new();

    long Count(Item item, int add)
    {
        madeCount[item] = madeCount.GetValueOrDefault(item) + add;
        return madeCount[item];
    }

    internal void OnSold(World w, Item item)
    {
        sold[(int)item]++;
        if (TutorialStep == 3 && (item == Item.IronIngot || item == Item.IronOre) && !printed.Contains("FIRST_SALE"))
            Inside(w, "FIRST_SALE");
        // A sale fills one order, the oldest open one for the good: two orders for the same good need twice the goods.
        foreach (var t in telegrams)
        {
            if (t.State != TelegramState.Open || t.Item != item)
                continue;
            t.Delivered++;
            if (t.Delivered >= t.Quantity)
            {
                t.State = TelegramState.Filled;
                Front(w, "TELEGRAM_FILLED", Items.Id(t.Item), t.Sender, t.Quantity.ToString());
            }
            break;
        }
    }

    internal void OnNotice(World w, Notice notice)
    {
        switch (notice.Key)
        {
            // A week's notice is only a week if it is read today: the warning prints at once, like the fold.
            case "BANKERS_WARNING": Extra(w, "BANKERS_WARNING", Hud(notice.Amount)); break;
            case "DEFAULT": break;
            case "FOLDS": Extra(w, "FOLDS", Hud(w.Notices.LastOrDefault(n => n.Key == "DEFAULT")?.Amount ?? 0)); break;
            default: Front(w, notice.Key); break;
        }
    }

    static string Hud(long cents) => cents.ToString();

    /// <summary>A commission opened or filled: a front page naming it (the name is a text key plus its argument).</summary>
    internal void OnCommission(World w, Commission c, bool done) =>
        Front(w, done ? "COMMISSION_DONE" : "COMMISSION_OPEN", c.NameKey, c.NameArg);

    // ---- Time -------------------------------------------------------------------------------

    internal void Tick(World w)
    {
        JustPrinted = null;
        if (w.TickCount == 1)
            Tutorial(w, 1, BuildingType.MineHead, w.Difficulty.StartingCashCents.ToString(CultureInfo.InvariantCulture));
        CheckTutorial(w);
        if (w.TickCount % Market.TicksPerHour == 0)
            Hourly(w);
        if (w.TickCount % World.TicksPerDay == 0)
            Daily(w);
    }

    /// <summary>
    /// The tutorial reads the world's state rather than waiting for events, so a goal met before its
    /// edition (or while the paper looked away) still moves it on. One step per call; at most one a tick.
    /// </summary>
    void CheckTutorial(World w)
    {
        switch (TutorialStep)
        {
            case 1 when w.Owned(BuildingType.MineHead) > 0:
                Tutorial(w, 2, BuildingType.Smelter);
                break;
            case 2 when made.Contains(Item.IronIngot):
                Tutorial(w, 3, BuildingType.FreightDepot);
                break;
            // A player who presses every ingot never gluts iron: the plate itself moves the lesson on.
            case 3 when w.Market.Discount(Item.IronIngot) >= 0.10 || w.Market.Discount(Item.IronOre) >= 0.10
                        || sold[(int)Item.IronOre] + sold[(int)Item.IronIngot] >= 300 || made.Contains(Item.IronPlate):
                Tutorial(w, 4, BuildingType.Press);
                break;
            // Without a press the banker (credit, interest, default) and the telegram desk still come, a few days on.
            case 4 when made.Contains(Item.IronPlate) || w.Day >= DayOf("TUT4") + TutorialPatienceDays:
                Tutorial(w, 5, null, w.Difficulty.BaseCreditCents.ToString(CultureInfo.InvariantCulture));
                break;
            case 5 when w.Day >= tutorialDay5 + 1:
                Tutorial(w, 6, BuildingType.Splitter);
                Wire(w, Item.IronPlate, 20, 4);
                nextTelegramDay = w.Day + 2 + rng.NextInt(3);
                break;
        }
    }

    /// <summary>The day an edition first printed (the tutorial's own clock), or the start.</summary>
    long DayOf(string key) => (editions.Find(e => e.Key == key)?.Tick ?? 0) / World.TicksPerDay;

    /// <summary>A machine or mine head standing idle because its firebox is cold and nothing else powers it.</summary>
    static bool FiresOut(World w)
    {
        foreach (var b in w.Buildings)
            if (b is Machine { Firebox.Lit: false, State: MachineState.Unpowered } || b is Mine { Lit: false, WantsPower: true })
                return true;
        return false;
    }

    void Hourly(World w)
    {
        // The first time the starter coal burns out, the paper says why the works has stopped (once per company).
        if (!printed.Contains("FIRES_OUT") && FiresOut(w))
            Print(w, "FIRES_OUT", Array.Empty<string>(), BuildingType.MineHead, front: true, extra: true);
        foreach (var t in telegrams)
            if (t.State == TelegramState.Open && w.TickCount >= t.DueTick)
            {
                t.State = TelegramState.Missed;
                Front(w, "TELEGRAM_MISSED", Items.Id(t.Item), t.Sender, t.Quantity.ToString());
            }
    }

    void Daily(World w)
    {
        long day = w.Day;
        if (TutorialStep >= 6 && day >= nextTelegramDay)
        {
            if (telegrams.Count(t => t.State == TelegramState.Open) < 2 && made.Count > 0)
            {
                // Order something the works has made, leaning toward the finer goods. Ties go by the table's order,
                // never by the order goods were first made (a save keeps the set, not that order).
                var candidates = made.OrderByDescending(i => (int)Items.Of(i).Tier).ThenBy(i => (int)i).Take(4).ToArray();
                var item = candidates[rng.NextInt(candidates.Length)];
                int tier = (int)Items.Of(item).Tier;
                int quantity = tier switch { 0 => 100, 1 => 60, 2 => 40, 3 => 20, 4 => 6, _ => 3 } + rng.NextInt(5) * (tier < 3 ? 10 : 1);
                Wire(w, item, quantity, 3 + rng.NextInt(4));
            }
            nextTelegramDay = day + 2 + rng.NextInt(3);
        }
        // Yesterday's front-page news comes out with today's paper, the inside items along with it.
        int i = pending.FindIndex(p => p.Front);
        if (i >= 0 && lastFrontDay < day)
        {
            var (key, args, _) = pending[i];
            pending.RemoveAt(i);
            Print(w, key, args, null, front: true);
        }
    }

    long tutorialDay5;

    void Wire(World w, Item item, int quantity, int days)
    {
        var sender = Senders[rng.NextInt(Senders.Length)];
        var t = new Telegram(nextTelegramId++, item, quantity, w.TickCount, w.TickCount + (long)days * World.TicksPerDay, sender, sold[(int)item]);
        telegrams.Add(t);
        if (printed.Contains("TUT6") && editions.Count > 0 && editions[^1].Key == "TUT6" && editions[^1].Tick == w.TickCount)
            return;   // the tutorial edition announces the first order itself
        Front(w, "TELEGRAM_NEW", Items.Id(item), sender, quantity.ToString());
    }

    // ---- Printing ---------------------------------------------------------------------------

    /// <param name="args">Figures the copy quotes: edition 1 the founding cash, edition 5 the line of credit, in cents.</param>
    void Tutorial(World w, int step, BuildingType? circled, params string[] args)
    {
        TutorialStep = step;
        if (step == 5)
            tutorialDay5 = w.Day;
        Print(w, "TUT" + step, args, circled, front: true, extra: true);
    }

    /// <summary>Front-page news: today's paper if none has printed, else tomorrow's.</summary>
    void Front(World w, string key, params string[] args)
    {
        // Telegrams, commissions and market events recur; everything else prints once.
        if (!key.StartsWith("TELEGRAM") && !key.StartsWith("COMMISSION") && !key.StartsWith("EVENT_") && !printed.Add(key))
            return;
        if (lastFrontDay == w.Day)
            pending.Add((key, args, true));
        else
            Print(w, key, args, null, front: true);
    }

    /// <summary>Always prints now: the tutorial and the last edition.</summary>
    void Extra(World w, string key, params string[] args) => Print(w, key, args, null, front: true, extra: true);

    /// <summary>Lesser news: an inside column of the next edition.</summary>
    void Inside(World w, string key, params string[] args)
    {
        if (!printed.Add(key))
            return;
        pending.Add((key, args, false));
    }

    void Print(World w, string key, string[] args, BuildingType? circled, bool front, bool extra = false)
    {
        printed.Add(key);
        var inside = pending.Where(p => !p.Front).Select(p => p.Key).ToArray();
        pending.RemoveAll(p => !p.Front);
        var edition = new Edition(editions.Count + 1, w.TickCount, key, args, inside, circled);
        editions.Add(edition);
        JustPrinted = edition;
        if (!extra)
            lastFrontDay = w.Day;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(editions.Count);
        foreach (var e in editions)
        {
            h.Mix(e.Tick);
            h.Mix(StableHash(e.Key));
        }
        // What decides the future but is not in the editions: once-only headlines, goods made, the tutorial's clock.
        h.Mix(printed.Count);
        foreach (var i in made.OrderBy(i => (int)i))
            h.Mix((long)i);
        foreach (var (item, count) in madeCount.OrderBy(kv => (int)kv.Key))
        {
            h.Mix((long)item);
            h.Mix(count);
        }
        h.Mix(tutorialDay5);
        h.Mix(nextTelegramId);
        h.Mix(TutorialStep);
        h.Mix(lastFrontDay);
        h.Mix(nextTelegramDay);
        h.Mix(pending.Count);
        foreach (var t in telegrams)
        {
            h.Mix(t.Id);
            h.Mix((long)t.Item);
            h.Mix(t.Quantity);
            h.Mix(t.DueTick);
            h.Mix((long)t.State);
            h.Mix(t.Delivered);
        }
        for (int i = 0; i < sold.Length; i++)
            h.Mix(sold[i]);
        h.Mix((long)rng.State);
    }

    /// <summary>FNV-1a over the characters: the same in every process (string.GetHashCode is seeded per process).</summary>
    static long StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return (long)h;
    }
}
