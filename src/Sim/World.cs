
namespace FrontPageFoundry.Sim;

/// <summary>
/// The whole game state. Advance it only through <see cref="Tick"/> and <see cref="Apply"/>,
/// so the same seed and commands always produce the same world.
/// </summary>
public sealed partial class World
{
    public const int TicksPerSecond = 60;
    /// <summary>One in-game day is two real minutes (GDD §8).</summary>
    public const int TicksPerDay = 120 * TicksPerSecond;
    /// <summary>Demolishing refunds this share of the most recent copy's price (GDD §5).</summary>
    public const double RefundFraction = 0.75;
    public const int TicksPerWeek = 7 * TicksPerDay;
    /// <summary>Rural land: $2 a tile, up to five times that in the thick of town (GDD §9).</summary>
    public const long RuralLandCents = 200;
    /// <summary>Felling a forest tile to build on it.</summary>
    public const long ClearingCents = 2_000;
    public const int MaxBridgeSpan = 4;
    public const int UndoDepth = 100;
    public const long SharesOutstanding = 10_000;

    readonly Dictionary<Cell, Building> grid = new();
    /// <summary>Buildings in creation order, which is also update order.</summary>
    readonly List<Building> buildings = new();
    /// <summary>Buildings that do something each tick; belts move through their lines instead.</summary>
    readonly List<Building> machines = new();
    readonly List<TransportLine> lines = new();
    readonly int[] owned = new int[Catalog.All.Length];
    readonly List<(long Tick, Command Command)> log = new();
    readonly List<List<Command>> undo = new(), redo = new();
    readonly HashSet<Cell> cleared = new();
    readonly HashSet<Cell> flooded = new();
    readonly Dictionary<long, long> patchExtracted = new();
    readonly Dictionary<int, Building> byId = new();
    readonly List<Plan> plans = new();
    readonly Dictionary<Cell, Plan> planAt = new();
    int nextId = 1, nextLineId = 1, nextPlanId = 1;

    /// <param name="mode">Full map, river only, or bare ground with the starter seams (tests and benches).</param>
    public World(int seed, long? startingCashCents = null, Difficulty? difficulty = null, MapMode mode = MapMode.Full)
    {
        Seed = seed;
        Difficulty = difficulty ?? Difficulty.SteadyTrade;
        Map = new MapGen(seed, mode, Difficulty.RichnessPercent);
        Town = new Town(seed);
        Power = new PowerGrid(this);
        Haulage = new Haulage(this);
        Market = new Market(seed, Difficulty);
        Paper = new Paper(seed);
        CashCents = startingCashCents ?? Difficulty.StartingCashCents;
        if (mode == MapMode.Full)
            Town.Found(this);
        weekStartWorth = NetWorthCents;
        PostedInterestPermille = WeeklyInterestPermille;
        if (BeltTiers.Spacing < BeltTiers.Speed(BeltTier.Steel))
            throw new InvalidOperationException("Belt spacing must be at least one tick of travel.");
    }

    public int Seed { get; }
    public Difficulty Difficulty { get; }
    public MapGen Map { get; }
    public Market Market { get; }
    /// <summary>The Courier: editions, tutorial, telegrams (GDD §10).</summary>
    public Paper Paper { get; }
    /// <summary>Carvell Falls, which grows (GDD §9).</summary>
    public Town Town { get; }
    /// <summary>Shaft, steam and electric networks (GDD §7).</summary>
    public PowerGrid Power { get; }
    /// <summary>Roads, rail, the river, and the fleet that runs on them (GDD §4).</summary>
    public Haulage Haulage { get; }
    /// <summary>The Exposition's commissions and the goodwill they leave (GDD §6).</summary>
    public Prestige Prestige { get; } = new();
    public Building? BuildingById(int id) => byId.GetValueOrDefault(id);
    /// <summary>Bumps when the ground itself changes (town growth, forest cleared), so terrain redraws.</summary>
    public int TerrainVersion { get; private set; }

    // ---- Land -------------------------------------------------------------------------------

    /// <summary>The map's tile, with the town and any clearing laid over it.</summary>
    public Terrain TerrainAt(Cell c)
    {
        if (Town.Tiles.Contains(c))
            return Terrain.Town;
        if (flooded.Contains(c))
            return Terrain.River;
        if (cleared.Contains(c))
            return Terrain.Ground;
        return Map.TerrainAt(c);
    }

    public Tile TileAt(Cell c)
    {
        var t = Map.TileAt(c);
        if (Town.Tiles.Contains(c))
            return t with { Kind = Terrain.Town, Patch = null };
        if (flooded.Contains(c))
            return t with { Kind = Terrain.River, Hill = false, Patch = null };
        if (cleared.Contains(c) && t.Kind == Terrain.Forest)
            return t with { Kind = Terrain.Ground };
        return t;
    }

    /// <summary>$2 × (1 + 4 × town density within 8 tiles); machines on hills pay a quarter more.</summary>
    public long LandPriceCents(Cell c, BuildingType type)
    {
        long price = (long)Math.Round(RuralLandCents * (1 + 4 * Town.Density(c)));
        if (Catalog.Of(type).Escalates && Map.TileAt(c).Hill)
            price = price * 5 / 4;
        return price;
    }

    /// <summary>Land and clearing for a footprint (or a bridge's two ends).</summary>
    public long LandCostCents(BuildingType type, Cell origin, Dir facing, int span = 0)
    {
        long total = 0;
        foreach (var c in Catalog.Of(type).IsBridge ? new[] { origin, origin + facing.Offset() * (span + 1) } : FootprintCells(type, origin, facing))
        {
            total += LandPriceCents(c, type);
            if (TerrainAt(c) == Terrain.Forest)
                total += ClearingCents;
        }
        return total;
    }

    /// <summary>The structure alone: rail costs three times as much on a hill (GDD §4).</summary>
    public long StructureCents(BuildingType type, Cell origin)
    {
        long cost = CostCents(type);
        if (type is BuildingType.Rail or BuildingType.RailSignal && Map.TileAt(origin).Hill)
            cost *= 3;
        return cost;
    }

    /// <summary>A signal tool over plain rail: the tile is kept and only the signal is bought.</summary>
    Track? SignalUpgrade(BuildingType type, Cell origin) =>
        type == BuildingType.RailSignal && BuildingAt(origin) is Track { Type: BuildingType.Rail } rail ? rail : null;

    public long SignalCents => CostCents(BuildingType.RailSignal) - CostCents(BuildingType.Rail);

    /// <summary>What the next copy costs here, land included.</summary>
    public long PurchaseCents(BuildingType type, Cell origin, Dir facing, int span = 0) =>
        SignalUpgrade(type, origin) != null ? SignalCents : StructureCents(type, origin) + LandCostCents(type, origin, facing, span);

    /// <summary>Pencil ghosts hold their ground against the town (GDD §9).</summary>
    public bool Planned(Cell c) => planAt.ContainsKey(c);
    public Plan? PlanAt(Cell c) => planAt.GetValueOrDefault(c);
    /// <summary>Plans in the order they were drawn, which is the order they are built.</summary>
    public IReadOnlyList<Plan> Plans => plans;
    /// <summary>Planning mode: the pencil is up, plans are drawn and none are built.</summary>
    public bool Planning { get; private set; }

    /// <summary>Ore raised from a patch so far, by every mine on it.</summary>
    public long PatchExtracted(long patchId) => patchExtracted.GetValueOrDefault(patchId);

    internal void Extract(long patchId) => patchExtracted[patchId] = PatchExtracted(patchId) + 1;

    /// <summary>
    /// A richness W_d scaled by the preset (GDD §13: ×1.5 Boom Times, ×0.7 Hard Times), for a mine on no patch.
    /// A patch's own richness already carries the preset (MapGen deals it scaled), so it is never scaled twice.
    /// </summary>
    public double Richness(double richness) => richness * Difficulty.RichnessPercent / 100.0;

    /// <summary>What a patch yields now, as a fraction of fresh: 0.25 + 0.75·e^(−W/W_d), W_d from the patch (preset included).</summary>
    public double PatchYield(Patch patch) => Mine.YieldFactor(PatchExtracted(patch.Id), patch.Richness);

    bool Buildable(Cell c, BuildingType type)
    {
        var kind = TerrainAt(c);
        if (kind == Terrain.Town)
            return false;
        // The dam stands in the river; everything else keeps to the land.
        return kind != Terrain.River || type == BuildingType.HydroDam;
    }

    /// <summary>A footprint cell 4-adjacent to the water: waterwheels and pumps need one.</summary>
    bool OnBank(BuildingType type, Cell origin, Dir facing)
    {
        foreach (var c in FootprintCells(type, origin, facing))
            for (int d = 0; d < 4; d++)
                if (TerrainAt(c + ((Dir)d).Offset()) == Terrain.River)
                    return true;
        return false;
    }

    /// <summary>The dam must cover the river's whole width, three tiles or less, in every column it spans.</summary>
    bool DamFits(Cell origin, Dir facing)
    {
        var (w, h) = Catalog.Footprint(BuildingType.HydroDam, facing);
        bool anyWater = false;
        for (int x = origin.X; x < origin.X + w; x++)
        {
            int wet = 0;
            for (int y = origin.Y - 4; y < origin.Y + h + 4; y++)
            {
                if (Map.TerrainAt(new Cell(x, y)) != Terrain.River)
                    continue;
                wet++;
                if (y < origin.Y || y >= origin.Y + h)
                    return false;
            }
            if (wet > 3)
                return false;
            anyWater |= wet > 0;
        }
        return anyWater;
    }

    /// <summary>Tiles the dam's lake covers: the river valley thirty columns upstream, five rows either side of the water.</summary>
    public IEnumerable<Cell> FloodOf(Cell origin, Dir facing)
    {
        var (w, h) = Catalog.Footprint(BuildingType.HydroDam, facing);
        int corridor = (int)Math.Round((origin.Y + h / 2.0 - MapGen.MainRiverRow) / MapGen.RiverSpacing);
        for (int x = origin.X - Dam.FloodLength; x < origin.X; x++)
        {
            double centre = Map.RiverCentre(corridor, x);
            for (int y = (int)Math.Floor(centre) - 5; y <= (int)Math.Floor(centre) + 5; y++)
            {
                var c = new Cell(x, y);
                if (Map.TerrainAt(c) != Terrain.River && Map.Elevation(c) < 0.1)
                    yield return c;
            }
        }
    }

    public bool Flooded(Cell c) => flooded.Contains(c);

    /// <summary>
    /// A cell on the main river's north bank at or east of <paramref name="fromX"/> with clear, buildable
    /// ground for <paramref name="width"/>×<paramref name="height"/> tiles above it (tests, the demo, hints).
    /// </summary>
    public Cell FlatBankNear(int fromX, int width = 10, int height = 10)
    {
        for (int x = fromX; x < fromX + 400; x++)
        {
            int y = (int)Math.Floor(Map.RiverCentre(0, x));
            while (TerrainAt(new Cell(x, y)) == Terrain.River)
                y--;
            if (TerrainAt(new Cell(x, y + 1)) != Terrain.River)
                continue;
            bool clear = true;
            for (int dx = -width / 2; dx <= width / 2 && clear; dx++)
                for (int dy = -height; dy <= 0; dy++)
                {
                    var c = new Cell(x + dx, y + dy);
                    if (TerrainAt(c) != Terrain.Ground || grid.ContainsKey(c))
                    {
                        clear = false;
                        break;
                    }
                }
            if (clear)
                return new Cell(x, y);
        }
        throw new InvalidOperationException("no flat bank found");
    }
    public long TickCount { get; private set; }
    public long CashCents { get; private set; }
    public long RevenueCents { get; private set; }
    /// <summary>Paid to the receiving docks so far.</summary>
    public long SpentCents { get; private set; }

    // ---- Money (GDD §8) ------------------------------------------------------------------------

    /// <summary>Outstanding balance on the credit line.</summary>
    public long DebtCents { get; private set; }
    public long InterestPaidCents { get; private set; }
    /// <summary>L = base credit + half of what demolishing everything would refund.</summary>
    public long CreditLimitCents => Difficulty.BaseCreditCents + ResaleValueCents / 2;
    public long CreditAvailableCents => Math.Max(0, CreditLimitCents - DebtCents);
    /// <summary>Weekly rate in permille, events included: what the bank asks from the next week it posts.</summary>
    public int WeeklyInterestPermille => Difficulty.WeeklyInterestPermille + Market.InterestPointsPermille;
    /// <summary>
    /// The rate this week's payment is charged at: posted when the week began. A rise (BANK RATE RAISED) counts from
    /// the next week, so it is always known — and warned of — a full week before it is paid.
    /// </summary>
    public int PostedInterestPermille { get; private set; }
    /// <summary>The payment due at the end of this week.</summary>
    public long NextInterestCents => DebtCents * PostedInterestPermille / 1000;
    /// <summary>A week's interest on a balance, at the higher of the posted rate and the rate the bank now asks.</summary>
    long InterestOn(long debtCents) => debtCents * Math.Max(PostedInterestPermille, WeeklyInterestPermille) / 1000;
    /// <summary>Cash plus what is left on the line: what a weekly payment can be met from.</summary>
    long HeadroomCents => CashCents + CreditAvailableCents;
    /// <summary>
    /// The paper's Banker's Warning: next week's interest is beyond cash and credit at this rate. Kept up to date
    /// every in-game hour (see <see cref="CheckBanker"/>), not only at the week's turn.
    /// </summary>
    public bool BankersWarning { get; private set; }
    /// <summary>The company missed a payment and is finished (GDD §8: one missed payment ends it).</summary>
    public bool Defaulted { get; private set; }
    public bool Ended => Defaulted;
    public long NetWorthCents => CashCents + ResaleValueCents - DebtCents;
    /// <summary>Permanent score term from commissions (M11).</summary>
    public long GoodwillCents { get; internal set; }
    /// <summary>share = (net worth + 4 × trailing four-week profit + goodwill) / shares; a share is never worth less than nothing.</summary>
    public long SharePriceCents => Math.Max(0, (NetWorthCents + 4 * TrailingProfitCents + GoodwillCents) / SharesOutstanding);
    public long TrailingProfitCents => weeklyProfit.Sum();
    public IReadOnlyList<long> WeeklyProfits => weeklyProfit;
    /// <summary>Things the paper should print, in order: events, warnings, the default notice.</summary>
    public List<Notice> Notices { get; } = new();
    public long Day => TickCount / TicksPerDay;
    public long Week => TickCount / TicksPerWeek;

    readonly List<long> weeklyProfit = new();
    long weekStartWorth;

    /// <summary>
    /// What demolishing everything would refund right now. Every purchase check asks (docks every tick), so each
    /// type's figure is kept and only worked out again when that type's count changes; it is a pure function of
    /// <see cref="owned"/>, so nothing here is state.
    /// </summary>
    public long ResaleValueCents
    {
        get
        {
            long total = 0;
            for (int t = 0; t < owned.Length; t++)
            {
                if (resaleCounted[t] != owned[t])
                {
                    resaleOfType[t] = ResaleOf((BuildingType)t, owned[t]);
                    resaleCounted[t] = owned[t];
                }
                total += resaleOfType[t];
            }
            return total;
        }
    }

    readonly long[] resaleOfType = new long[Catalog.All.Length];
    readonly int[] resaleCounted = new int[Catalog.All.Length];

    static long ResaleOf(BuildingType type, int count)
    {
        if (!Catalog.Of(type).Escalates)
            return (long)Math.Round(Catalog.Of(type).BaseCostCents * RefundFraction) * count;
        long total = 0;
        for (int k = 0; k < count; k++)
            total += (long)Math.Round(CostForCopy(type, k) * RefundFraction);
        return total;
    }

    /// <summary>
    /// A purchase is paid from cash, then the credit line, and must leave the next two weekly payments covered.
    /// So spending alone can never fold the company; what can (weeks with no income after the line is drawn)
    /// shows as the Banker's Warning a full week before the payment that would be missed.
    /// </summary>
    public bool CanAfford(long cents)
    {
        long debt = DebtCents + Math.Max(0, cents - CashCents);
        long first = InterestOn(debt);
        return cents + first + InterestOn(debt + first) <= HeadroomCents;
    }

    /// <summary>Takes money from cash, then from the credit line (GDD §8: purchases draw on it automatically).</summary>
    internal void Pay(long cents)
    {
        CashCents -= cents;
        if (CashCents < 0)
        {
            DebtCents += -CashCents;
            CashCents = 0;
        }
    }

    internal void Buy(Item item)
    {
        long price = Market.Buy(item);
        Pay(price);
        SpentCents += price;
    }

    void OnNewDay()
    {
        // Repay freely: whatever cash is on hand goes against the balance.
        long repay = Math.Min(CashCents, DebtCents);
        CashCents -= repay;
        DebtCents -= repay;
        int before = Town.Version;
        Town.Grow(this);
        if (Town.Version != before)
            TerrainVersion++;
    }

    void OnNewWeek()
    {
        long interest = NextInterestCents;
        if (interest > 0)
        {
            if (interest <= HeadroomCents)
            {
                Pay(interest);
                InterestPaidCents += interest;
            }
            else
            {
                Defaulted = true;
                Notify(new Notice(TickCount, "DEFAULT", interest));
                Notify(new Notice(TickCount, "FOLDS", 0));
                return;
            }
        }
        weeklyProfit.Add(NetWorthCents - weekStartWorth);
        if (weeklyProfit.Count > 4)
            weeklyProfit.RemoveAt(0);
        weekStartWorth = NetWorthCents;
        // The coming week's rate is posted now; the warning below already reckons with it.
        PostedInterestPermille = WeeklyInterestPermille;
        CheckBanker();
    }

    /// <summary>
    /// Every in-game hour, and just after each payment: the warning is on while next week's interest is beyond cash
    /// and what is left of the line, and off once it is not. The paper prints it when it comes on, once a week at most.
    /// </summary>
    void CheckBanker()
    {
        long due = NextInterestCents;
        bool warn = due > 0 && due > HeadroomCents;
        if (warn && !BankersWarning && !(Notices.FindLast(n => n.Key == "BANKERS_WARNING") is { } last && last.Tick / TicksPerWeek == Week))
            Notify(new Notice(TickCount, "BANKERS_WARNING", due));
        BankersWarning = warn;
    }

    public IReadOnlyList<Building> Buildings => buildings;
    public IReadOnlyList<TransportLine> Lines => lines;
    /// <summary>Every command applied so far, with the tick it landed on.</summary>
    public IReadOnlyList<(long Tick, Command Command)> Log => log;
    public bool CanUndo => !Ended && undo.Count > 0;
    public bool CanRedo => !Ended && redo.Count > 0;

    public Building? BuildingAt(Cell c) => grid.GetValueOrDefault(c);
    public int Owned(BuildingType type) => owned[(int)type];
    public long CostCents(BuildingType type) => CostForCopy(type, owned[(int)type]);

    /// <summary>price(n) = base × (1 + 0.04·n)^1.5 for machines (GDD §5); logistics stays flat.</summary>
    public static long CostForCopy(BuildingType type, int copiesOwned)
    {
        var def = Catalog.Of(type);
        if (!def.Escalates)
            return def.BaseCostCents;
        double m = 1 + 0.04 * copiesOwned;
        return (long)Math.Round(def.BaseCostCents * m * Math.Sqrt(m));
    }

    // ---- Commands ----------------------------------------------------------------------------

    /// <summary>
    /// Runs a command, logs it and records how to undo it. <paramref name="joinPrevious"/> folds it
    /// into the last undo step (a dragged run of belts undoes as one).
    /// </summary>
    public PlaceResult Apply(Command command, bool joinPrevious = false)
    {
        if (Ended)
            return PlaceResult.Ended;
        var (result, inverse) = Execute(command);
        if (result != PlaceResult.Ok)
            return result;
        log.Add((TickCount, command));
        if (inverse != null)
        {
            if (joinPrevious && undo.Count > 0)
                undo[^1].Add(inverse);
            else
                undo.Add(new List<Command> { inverse });
            if (undo.Count > UndoDepth)
                undo.RemoveAt(0);
            redo.Clear();
        }
        return result;
    }

    /// <summary>Undo and redo are commands too: a folded company refuses them (GDD §8).</summary>
    public bool Undo() => !Ended && Step(undo, redo);
    public bool Redo() => !Ended && Step(redo, undo);

    /// <summary>Pops a group from one stack, applies it newest-first, and pushes the reversal on the other.</summary>
    bool Step(List<List<Command>> from, List<List<Command>> to)
    {
        if (from.Count == 0)
            return false;
        var group = from[^1];
        from.RemoveAt(from.Count - 1);
        var reversal = new List<Command>();
        for (int i = group.Count - 1; i >= 0; i--)
        {
            var (result, inverse) = Execute(group[i]);
            if (result != PlaceResult.Ok)
                continue;
            log.Add((TickCount, group[i]));
            if (inverse != null)
                reversal.Add(inverse);
        }
        if (reversal.Count > 0)
            to.Add(reversal);
        return true;
    }

    (PlaceResult, Command?) Execute(Command command) => command switch
    {
        Place p => ExecutePlace(p),
        Remove r => ExecuteRemove(r),
        Rotate r => ExecuteRotate(r),
        SetFilter f => ExecuteSetFilter(f),
        SetOrder o => ExecuteSetOrder(o),
        Assign a => ExecuteAssign(a),
        Route r => ExecuteRoute(r),
        Scrap s => ExecuteScrap(s),
        ClearSignal c => ExecuteClearSignal(c),
        Draft d => ExecuteDraft(d),
        Undraft u => ExecuteUndraft(u),
        SetPlanning p => ExecuteSetPlanning(p),
        _ => throw new ArgumentException($"Unknown command {command}"),
    };

    /// <param name="asPlan">A pencil ghost: no money is needed, but it may not overlap another plan.</param>
    public PlaceResult CanPlace(BuildingType type, Cell origin, Dir facing, int span = 0, bool asPlan = false)
    {
        var def = Catalog.Of(type);
        if (def.IsVehicle)
            return PlaceResult.Blocked;
        if (SignalUpgrade(type, origin) != null)
        {
            // One plan to a cell here too: a second signal pencilled on the same rail was a plan the map no
            // longer showed (rubbing out the first left it queued, unseen and impossible to rub out).
            if (asPlan && planAt.ContainsKey(origin))
                return PlaceResult.Blocked;
            return asPlan || CanAfford(SignalCents) ? PlaceResult.Ok : PlaceResult.TooExpensive;
        }
        if (def.IsBridge)
        {
            if (span < 1 || span > MaxBridgeSpan)
                return PlaceResult.BadSpan;
            var exit = origin + facing.Offset() * (span + 1);
            if (grid.ContainsKey(origin) || grid.ContainsKey(exit) || !Buildable(origin, type) || !Buildable(exit, type))
                return PlaceResult.Blocked;
            if (asPlan && (planAt.ContainsKey(origin) || planAt.ContainsKey(exit)))
                return PlaceResult.Blocked;
        }
        else
        {
            int ore = 0;
            foreach (var c in FootprintCells(type, origin, facing))
            {
                if (grid.ContainsKey(c) || !Buildable(c, type) || asPlan && planAt.ContainsKey(c))
                    return PlaceResult.Blocked;
                if (Items.OfTerrain(TerrainAt(c)) != null)
                    ore++;
            }
            if (type == BuildingType.MineHead && ore == 0)
                return PlaceResult.NeedsOre;
            if (type == BuildingType.LoggingCamp && LoggingCamp.TreesAround(this, origin, facing) == 0)
                return PlaceResult.NeedsForest;
            if (type == BuildingType.PumpJack && !FootprintCells(type, origin, facing).Any(c => TerrainAt(c) == Terrain.OilSeep))
                return PlaceResult.NeedsOil;
            if (type is BuildingType.Waterwheel or BuildingType.WaterPump or BuildingType.BargeLanding && !OnBank(type, origin, facing))
                return PlaceResult.NeedsBank;
            if (type == BuildingType.HydroDam)
            {
                if (!DamFits(origin, facing))
                    return PlaceResult.NeedsRiver;
                foreach (var c in FloodOf(origin, facing))
                    if (grid.ContainsKey(c) || Town.Tiles.Contains(c))
                        return PlaceResult.FloodBlocked;
            }
        }
        return asPlan || CanAfford(PurchaseCents(type, origin, facing, span)) ? PlaceResult.Ok : PlaceResult.TooExpensive;
    }

    public static IEnumerable<Cell> FootprintCells(BuildingType type, Cell origin, Dir facing)
    {
        var (w, h) = Catalog.Footprint(type, facing);
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                yield return new Cell(origin.X + dx, origin.Y + dy);
    }

    (PlaceResult, Command?) ExecutePlace(Place cmd)
    {
        var def = Catalog.Of(cmd.Type);
        var result = CanPlace(cmd.Type, cmd.Origin, cmd.Facing, cmd.Span);
        // An exact reversal price may differ from the catalog price; only the catalog price gates on money.
        if (result == PlaceResult.TooExpensive && cmd.PriceCents is { } fixedPrice && CanAfford(fixedPrice))
            result = PlaceResult.Ok;
        if (result != PlaceResult.Ok)
            return (result, null);

        long price = cmd.PriceCents ?? PurchaseCents(cmd.Type, cmd.Origin, cmd.Facing, cmd.Span);
        foreach (var c in def.IsBridge ? new[] { cmd.Origin, cmd.Origin + cmd.Facing.Offset() * (cmd.Span + 1) } : FootprintCells(cmd.Type, cmd.Origin, cmd.Facing))
            if (planAt.TryGetValue(c, out var under))
                RemovePlan(under);
        if (SignalUpgrade(cmd.Type, cmd.Origin) is { } rail)
        {
            Pay(price);
            rail.SetSignal(true);
            owned[(int)BuildingType.Rail]--;
            owned[(int)BuildingType.RailSignal]++;
            Haulage.Changed(VehicleKind.Train);
            if (owned[(int)cmd.Type] == 1)
                Paper.OnBuilt(this, cmd.Type);
            return (PlaceResult.Ok, new ClearSignal(cmd.Origin, price));
        }
        Pay(price);
        owned[(int)cmd.Type]++;
        // Felling the wood: the tiles stay cleared even if the building goes.
        foreach (var c in def.IsBridge ? new[] { cmd.Origin, cmd.Origin + cmd.Facing.Offset() * (cmd.Span + 1) } : FootprintCells(cmd.Type, cmd.Origin, cmd.Facing))
            if (Map.TerrainAt(c) == Terrain.Forest && cleared.Add(c))
                TerrainVersion++;

        int builtId;
        if (def.IsBridge)
        {
            var entry = new Belt(NewId(cmd.Id), cmd.Type, cmd.Origin, cmd.Facing, BeltRole.BridgeEntry, cmd.Span);
            builtId = entry.Id;
            var exit = new Belt(nextId++, cmd.Type, entry.Exit, cmd.Facing, BeltRole.BridgeExit);
            entry.Partner = exit;
            exit.Partner = entry;
            Add(entry);
            Add(exit);
            OnBeltsChanged(entry.Origin, exit.Origin);
        }
        else
        {
            int id = NewId(cmd.Id);
            Building b = cmd.Type switch
            {
                BuildingType.MineHead => MakeMine(id, cmd.Origin, cmd.Facing),
                BuildingType.FreightDepot => new Depot(id, cmd.Origin, cmd.Facing),
                BuildingType.ReceivingDock => new Dock(id, cmd.Origin, cmd.Facing),
                BuildingType.ExpositionYard => new Yard(id, cmd.Origin, cmd.Facing),
                _ when def.IsTrack => new Track(id, cmd.Type, cmd.Origin, cmd.Facing),
                _ when def.IsTerminal => new Terminal(id, cmd.Type, cmd.Origin, cmd.Facing),
                _ when def.IsSplitter => new Splitter(id, cmd.Type, cmd.Origin, cmd.Facing, MakeLane),
                _ when def.IsBelt => new Belt(id, cmd.Type, cmd.Origin, cmd.Facing),
                _ when def.IsMachine => new Machine(id, cmd.Type, cmd.Origin, cmd.Facing),
                BuildingType.LoggingCamp => new LoggingCamp(id, cmd.Origin, cmd.Facing, LoggingCamp.TreesAround(this, cmd.Origin, cmd.Facing)),
                BuildingType.PumpJack => MakePumpJack(id, cmd.Origin, cmd.Facing),
                BuildingType.Waterwheel => new Waterwheel(id, cmd.Origin, cmd.Facing),
                BuildingType.WaterPump => new WaterPump(id, cmd.Origin, cmd.Facing),
                BuildingType.Boiler => new Boiler(id, cmd.Origin, cmd.Facing),
                BuildingType.SteamPipe => new Pipe(id, cmd.Origin, cmd.Facing),
                BuildingType.PowerStation => new PowerStation(id, cmd.Origin, cmd.Facing),
                BuildingType.PowerPole => new Pole(id, cmd.Origin, cmd.Facing),
                BuildingType.HydroDam => new Dam(id, cmd.Origin, cmd.Facing),
                _ => throw new NotSupportedException($"{cmd.Type} is not buildable yet"),
            };
            Add(b);
            builtId = id;
            if (cmd.Fire is { } fire)
                FireState.Restore(b, fire);
            // A setting carried by the command (undo of a demolition) goes back on with the building.
            if (cmd.Setting is { } setting)
            {
                if (b is Splitter { Sorting: true } sorter)
                    sorter.Filter = setting;
                else if (b is Dock dock && Items.Of(setting).Tier == Tier.Raw)
                    dock.Order = setting;
            }
            if (b is Yard)
                Prestige.Open(this);
            if (b is Dam)
            {
                foreach (var c in FloodOf(cmd.Origin, cmd.Facing))
                {
                    flooded.Add(c);
                    // The lake supersedes pencil plans under it, as any real build over a plan does:
                    // left there they could never go down and would stand as ghosts for good.
                    if (planAt.TryGetValue(c, out var drowned))
                        RemovePlan(drowned);
                }
                TerrainVersion++;
            }
            if (b is Belt)
                OnBeltsChanged(b.Origin);
            else if (b is Splitter s)
                s.RefreshLaneTiers(this);
        }
        if (owned[(int)cmd.Type] == 1)
            Paper.OnBuilt(this, cmd.Type);
        return (PlaceResult.Ok, new Remove(cmd.Origin, price, builtId));
    }

    (PlaceResult, Command?) ExecuteRemove(Remove cmd)
    {
        if (BuildingAt(cmd.Cell) is not { } b)
            return (PlaceResult.Nothing, null);
        // Undo of a build takes down only what that build put up: a plan built on the cell since is
        // another purchase, and refunding it at the undone building's price would print money.
        if (cmd.Id is { } id && b.Id != id && (b as Belt)?.Partner?.Id != id)
            return (PlaceResult.Nothing, null);
        var origin = b.Origin;
        int span = 0, restoreId = b.Id;
        // The fire goes back as it was on undo; a fresh starter bag would be free coal.
        var fire = FireState.Of(b);
        var changed = new List<Cell>();
        if (b is Belt { Partner: { } partner } end)
        {
            var entry = end.Role == BeltRole.BridgeEntry ? end : partner;
            origin = entry.Origin;
            span = entry.Span;
            restoreId = entry.Id;
            Drop(end);
            Drop(partner);
            changed.Add(end.Origin);
            changed.Add(partner.Origin);
        }
        else
        {
            Drop(b);
            changed.AddRange(b.Cells);
        }
        owned[(int)b.Type]--;
        long refund = cmd.RefundCents ?? (long)Math.Round(CostForCopy(b.Type, owned[(int)b.Type]) * RefundFraction);
        CashCents += refund;
        if (b is Belt)
            OnBeltsChanged(changed.ToArray());
        else
            RefreshSplittersNear(changed);
        Item? setting = b switch
        {
            Splitter { Sorting: true } sorter => sorter.Filter,
            Dock dock => dock.Order,
            _ => null,
        };
        return (PlaceResult.Ok, new Place(b.Type, origin, b.Facing, span, refund, setting, restoreId, fire));
    }

    (PlaceResult, Command?) ExecuteRotate(Rotate cmd)
    {
        if (BuildingAt(cmd.Cell) is not { } b)
            return (PlaceResult.Nothing, null);
        if (b is Belt { Role: not BeltRole.Plain })
            return (PlaceResult.Blocked, null);
        if (b is Track)
            return (PlaceResult.Ok, null);
        var old = b.Facing;
        if (old == cmd.Facing)
            return (PlaceResult.Ok, null);
        var stoodOn = new HashSet<Cell>(b.Cells);
        foreach (var c in FootprintCells(b.Type, b.Origin, cmd.Facing))
        {
            if (grid.TryGetValue(c, out var other) && other != b)
                return (PlaceResult.Blocked, null);
            // A 1×2 piece swung onto new ground obeys the ground rules: no river or town, no plan
            // underfoot, and no standing wood (felling it is part of a purchase).
            if (!stoodOn.Contains(c) && (!Buildable(c, b.Type) || TerrainAt(c) == Terrain.Forest || planAt.ContainsKey(c)))
                return (PlaceResult.Blocked, null);
        }
        if (b.Type is BuildingType.Waterwheel or BuildingType.WaterPump or BuildingType.BargeLanding && !OnBank(b.Type, b.Origin, cmd.Facing))
            return (PlaceResult.NeedsBank, null);
        foreach (var c in b.Cells)
            grid.Remove(c);
        b.Facing = cmd.Facing;
        foreach (var c in b.Cells)
            grid[c] = b;
        if (b is Belt)
            OnBeltsChanged(b.Origin);
        else
            RefreshSplittersNear(b.Cells);
        if (b is IPowered || PowerGrid.IsPowerPiece(b.Type))
            Power.Invalidate();
        Haulage.Changed(b.Type);
        // The origin is in the footprint either way round; the clicked cell may not be.
        return (PlaceResult.Ok, new Rotate(b.Origin, old));
    }

    // ---- The fleet (GDD §4) --------------------------------------------------------------------

    (PlaceResult, Command?) ExecuteAssign(Assign cmd)
    {
        var def = Catalog.Of(cmd.Type);
        if (!def.IsVehicle)
            return (PlaceResult.Blocked, null);
        var kind = Terminal.KindOf(cmd.Type);
        var a = Haulage.TerminalAt(cmd.A);
        var b = Haulage.TerminalAt(cmd.B);
        if (a == null || b == null || a.Kind != kind || b.Kind != kind)
            return (PlaceResult.NeedsTerminal, null);
        if (a != b && Haulage.FindPath(kind, a, b) == null)
            return (PlaceResult.NoRoute, null);
        long price = cmd.PriceCents ?? CostCents(cmd.Type);
        if (!CanAfford(price))
            return (PlaceResult.TooExpensive, null);
        Pay(price);
        owned[(int)cmd.Type]++;
        var v = Haulage.Buy(cmd.Type, cmd.A, cmd.Id);
        if (a != b)
            v.SetRoute(this, cmd.A, cmd.B);
        if (owned[(int)cmd.Type] == 1)
            Paper.OnBuilt(this, cmd.Type);
        return (PlaceResult.Ok, new Scrap(v.Id, price));
    }

    (PlaceResult, Command?) ExecuteRoute(Route cmd)
    {
        if (Haulage.VehicleById(cmd.Id) is not { } v)
            return (PlaceResult.Nothing, null);
        var oldA = v.A;
        var oldB = v.B;
        if (cmd.A is { } ca && cmd.B is { } cb)
        {
            var a = Haulage.TerminalAt(ca);
            var b = Haulage.TerminalAt(cb);
            if (a == null || b == null || a.Kind != v.Kind || b.Kind != v.Kind)
                return (PlaceResult.NeedsTerminal, null);
            if (a == b)
            {
                v.At = ca;
                v.SetRoute(this, null, null);
                return (PlaceResult.Ok, new Route(cmd.Id, oldA, oldB));
            }
            if (Haulage.FindPath(v.Kind, a, b) == null)
                return (PlaceResult.NoRoute, null);
            v.SetRoute(this, ca, cb);
        }
        else
        {
            v.SetRoute(this, null, null);
        }
        return (PlaceResult.Ok, new Route(cmd.Id, oldA, oldB));
    }

    (PlaceResult, Command?) ExecuteScrap(Scrap cmd)
    {
        if (Haulage.VehicleById(cmd.Id) is not { } v)
            return (PlaceResult.Nothing, null);
        owned[(int)v.Type]--;
        long refund = cmd.RefundCents ?? (long)Math.Round(CostForCopy(v.Type, owned[(int)v.Type]) * RefundFraction);
        CashCents += refund;
        var a = v.A ?? v.At;
        var b = v.B ?? v.At;
        Haulage.Scrap(v);
        return (PlaceResult.Ok, new Assign(v.Type, a, b, refund, v.Id));
    }

    (PlaceResult, Command?) ExecuteClearSignal(ClearSignal cmd)
    {
        if (BuildingAt(cmd.Cell) is not Track { Signal: true } rail)
            return (PlaceResult.Nothing, null);
        rail.SetSignal(false);
        owned[(int)BuildingType.RailSignal]--;
        owned[(int)BuildingType.Rail]++;
        long refund = cmd.RefundCents ?? (long)Math.Round(SignalCents * RefundFraction);
        CashCents += refund;
        Haulage.Changed(VehicleKind.Train);
        return (PlaceResult.Ok, new Place(BuildingType.RailSignal, cmd.Cell, rail.Facing, 0, refund));
    }

    // ---- Plans (GDD §3 planning mode) -----------------------------------------------------------

    (PlaceResult, Command?) ExecuteDraft(Draft cmd)
    {
        var result = CanPlace(cmd.Type, cmd.Origin, cmd.Facing, cmd.Span, asPlan: true);
        if (result != PlaceResult.Ok)
            return (result, null);
        var plan = new Plan(nextPlanId++, cmd.Type, cmd.Origin, cmd.Facing, cmd.Span, cmd.Setting);
        plans.Add(plan);
        foreach (var c in plan.Cells)
            planAt[c] = plan;
        return (PlaceResult.Ok, new Undraft(cmd.Origin));
    }

    (PlaceResult, Command?) ExecuteUndraft(Undraft cmd)
    {
        if (!planAt.TryGetValue(cmd.Cell, out var plan))
            return (PlaceResult.Nothing, null);
        RemovePlan(plan);
        return (PlaceResult.Ok, new Draft(plan.Type, plan.Origin, plan.Facing, plan.Span, plan.Setting));
    }

    (PlaceResult, Command?) ExecuteSetPlanning(SetPlanning cmd)
    {
        Planning = cmd.On;
        return (PlaceResult.Ok, null);
    }

    void RemovePlan(Plan plan)
    {
        foreach (var c in plan.Cells)
            if (planAt.TryGetValue(c, out var p) && p == plan)
                planAt.Remove(c);
        plans.Remove(plan);
    }

    /// <summary>
    /// With the pencil down, one plan a tick is built for real, oldest first, from cash on hand only:
    /// a plan never draws on the credit line. A plan that cannot go down yet is passed over; one that
    /// only wants money holds the queue so the layout is built in the order it was drawn.
    /// </summary>
    /// <summary>Where the scan for a buildable plan resumes next tick (bounded so a wall of blocked plans is cheap).</summary>
    int planCursor;
    public const int PlansScannedPerTick = 64;

    void BuildPlans()
    {
        if (Planning || plans.Count == 0)
            return;
        if (planCursor >= plans.Count)
            planCursor = 0;
        for (int step = 0; step < PlansScannedPerTick && step < plans.Count; step++)
        {
            int i = (planCursor + step) % plans.Count;
            var plan = plans[i];
            var result = CanPlace(plan.Type, plan.Origin, plan.Facing, plan.Span);
            if (result == PlaceResult.TooExpensive)
            {
                planCursor = i;
                return;
            }
            if (result != PlaceResult.Ok)
                continue;
            if (CashCents < PurchaseCents(plan.Type, plan.Origin, plan.Facing, plan.Span))
            {
                planCursor = i;
                return;
            }
            planCursor = i;
            var (built, _) = ExecutePlace(new Place(plan.Type, plan.Origin, plan.Facing, plan.Span));
            if (built != PlaceResult.Ok)
                continue;
            RemovePlan(plan);
            planCursor = 0;
            if (plan.Setting is { } setting)
            {
                if (BuildingAt(plan.Origin) is Splitter { Sorting: true })
                    ExecuteSetFilter(new SetFilter(plan.Origin, setting));
                else if (BuildingAt(plan.Origin) is Dock)
                    ExecuteSetOrder(new SetOrder(plan.Origin, setting));
            }
            return;
        }
        planCursor = (planCursor + PlansScannedPerTick) % Math.Max(1, plans.Count);
    }

    /// <summary>A vehicle of your own make joined the fleet: it counts as owned, at no charge.</summary>
    internal void Enlisted(BuildingType type)
    {
        owned[(int)type]++;
        if (owned[(int)type] == 1)
            Paper.OnBuilt(this, type);
    }

    (PlaceResult, Command?) ExecuteSetFilter(SetFilter cmd)
    {
        if (BuildingAt(cmd.Cell) is not Splitter { Sorting: true } s)
            return (PlaceResult.Nothing, null);
        var old = s.Filter;
        s.Filter = cmd.Filter;
        return (PlaceResult.Ok, new SetFilter(cmd.Cell, old));
    }

    (PlaceResult, Command?) ExecuteSetOrder(SetOrder cmd)
    {
        if (BuildingAt(cmd.Cell) is not Dock dock)
            return (PlaceResult.Nothing, null);
        if (cmd.Item is { } item && Items.Of(item).Tier != Tier.Raw)
            return (PlaceResult.Blocked, null);
        var old = dock.Order;
        dock.Order = cmd.Item;
        return (PlaceResult.Ok, new SetOrder(cmd.Cell, old));
    }

    /// <summary>A fresh number, or the old one a building brought back by undo asks for (if it is free).</summary>
    int NewId(int? wanted) => wanted is { } id && id > 0 && id < nextId && !byId.ContainsKey(id) ? id : nextId++;

    void Add(Building b)
    {
        foreach (var c in b.Cells)
            grid[c] = b;
        // Kept in id order (creation order, the update order); a building brought back by undo takes its old place.
        InsertById(buildings, b);
        byId[b.Id] = b;
        if (b is not Belt and not Track)
            InsertById(machines, b);
        if (b is IPowered || PowerGrid.IsPowerPiece(b.Type))
            Power.Invalidate();
        Haulage.Changed(b.Type);
    }

    void Drop(Building b)
    {
        foreach (var c in b.Cells)
            grid.Remove(c);
        byId.Remove(b.Id);
        if (b is IPowered || PowerGrid.IsPowerPiece(b.Type))
            Power.Invalidate();
        if (b is Dam dam)
        {
            foreach (var c in FloodOf(dam.Origin, dam.Facing))
                flooded.Remove(c);
            // Another dam's lake may share those tiles: it keeps them under water.
            foreach (var other in buildings)
                if (other is Dam d && d != dam)
                    foreach (var c in FloodOf(d.Origin, d.Facing))
                        flooded.Add(c);
            TerrainVersion++;
        }
        int i = buildings.BinarySearch(b, IdOrder.Instance);
        if (i >= 0)
            buildings.RemoveAt(i);
        Haulage.Changed(b.Type);
        if (b is not Belt and not Track)
        {
            i = machines.BinarySearch(b, IdOrder.Instance);
            if (i >= 0)
                machines.RemoveAt(i);
        }
        if (b is Belt belt && belt.Line is { } line)
        {
            line.Dissolve();
            RemoveLine(line);
            foreach (var t in line.Tiles)
                t.Loose = null;
        }
        if (b is Belt belt2)
            belt2.Loose = null;
    }

    static void InsertById(List<Building> list, Building b)
    {
        if (list.Count == 0 || list[^1].Id < b.Id)
        {
            list.Add(b);
            return;
        }
        int i = list.BinarySearch(b, IdOrder.Instance);
        list.Insert(i < 0 ? ~i : i, b);
    }

    TransportLine MakeLane(ILineOutput output) => new(nextLineId++, BeltTier.Canvas, output);

    sealed class IdOrder : IComparer<Building>
    {
        public static readonly IdOrder Instance = new();
        public int Compare(Building? a, Building? b) => a!.Id.CompareTo(b!.Id);
    }

    PumpJack MakePumpJack(int id, Cell origin, Dir facing)
    {
        int tiles = 0;
        Patch? patch = null;
        foreach (var c in FootprintCells(BuildingType.PumpJack, origin, facing))
        {
            var tile = TileAt(c);
            if (tile.Kind != Terrain.OilSeep)
                continue;
            tiles++;
            patch ??= tile.Patch;
        }
        return new PumpJack(id, origin, facing, tiles, patch);
    }

    /// <summary>A mine head over mixed seams yields the commonest one under it, and works that seam's patch.</summary>
    Mine MakeMine(int id, Cell origin, Dir facing)
    {
        var counts = new Dictionary<Item, int>();
        var patches = new Dictionary<Item, Patch>();
        foreach (var c in FootprintCells(BuildingType.MineHead, origin, facing))
        {
            var tile = TileAt(c);
            if (Items.OfTerrain(tile.Kind) is { } item)
            {
                counts[item] = counts.GetValueOrDefault(item) + 1;
                if (tile.Patch != null)
                    patches.TryAdd(item, tile.Patch);
            }
        }
        var best = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
        return new Mine(id, origin, facing, counts.Values.Sum(), best.Key, patches.GetValueOrDefault(best.Key));
    }

    // ---- Ticking -----------------------------------------------------------------------------

    public void Tick()
    {
        if (Ended)
            return;
        TickCount++;
        Market.Tick(TickCount);
        foreach (var e in Market.Started)
            Notify(new Notice(TickCount, e.Event.HeadlineKey, 0));
        foreach (var line in lines)
            line.Tick(this);
        Power.Evaluate();
        foreach (var b in machines)
            b.Update(this);
        Haulage.Tick();
        BuildPlans();
        if (TickCount % TicksPerDay == 0)
            OnNewDay();
        if (TickCount % TicksPerWeek == 0)
            OnNewWeek();
        if (!Ended && TickCount % Market.TicksPerHour == 0)
            CheckBanker();
        Paper.Tick(this);
    }

    /// <summary>Offers a good moving in <paramref name="travel"/> direction into a cell (see <see cref="Building.TryAccept"/>).</summary>
    internal bool Offer(Item item, Cell into, Dir travel, int entryPos) =>
        grid.TryGetValue(into, out var b) && b.TryAccept(this, item, travel, entryPos, into);

    internal void Sell(Item item)
    {
        long price = Market.Sell(item);
        CashCents += price;
        RevenueCents += price;
        Paper.OnSold(this, item);
    }

    /// <summary>A machine finished a run.</summary>
    internal void Made(Item item, int count) => Paper.OnMade(this, item, count);

    /// <summary>Starts a market event now and tells the paper (tests, tutorial).</summary>
    internal void StartEvent(MarketEvent e)
    {
        Market.Force(e, TickCount);
        Notify(new Notice(TickCount, e.HeadlineKey, 0));
    }

    void Notify(Notice notice)
    {
        Notices.Add(notice);
        Paper.OnNotice(this, notice);
    }

    public ulong StateHash()
    {
        var h = StateHasher.Start();
        h.Mix(TickCount);
        h.Mix(CashCents);
        h.Mix(RevenueCents);
        h.Mix(SpentCents);
        h.Mix(DebtCents);
        h.Mix(InterestPaidCents);
        h.Mix(Defaulted ? 1 : 0);
        h.Mix(BankersWarning ? 1 : 0);
        h.Mix(GoodwillCents);
        h.Mix(Notices.Count);
        h.Mix(weekStartWorth);
        foreach (var p in weeklyProfit)
            h.Mix(p);
        Market.Hash(ref h);
        Paper.Hash(ref h);
        Town.Hash(ref h);
        // Power networks and their figures are recomputed at the top of every tick, so they are not state.
        Haulage.Hash(ref h);
        Prestige.Hash(ref h);
        h.Mix(Planning ? 1 : 0);
        h.Mix(nextPlanId);
        h.Mix(planCursor);
        h.Mix(PostedInterestPermille);
        foreach (var p in plans)
        {
            h.Mix(p.Id);
            h.Mix((long)p.Type);
            h.Mix(p.Origin.X);
            h.Mix(p.Origin.Y);
            h.Mix((long)p.Facing);
            h.Mix(p.Span);
            h.Mix(p.Setting.HasValue ? (long)p.Setting.Value : -1);
        }
        h.Mix(cleared.Count);
        h.Mix(flooded.Count);
        foreach (var (id, w) in patchExtracted.OrderBy(kv => kv.Key))
        {
            h.Mix(id);
            h.Mix(w);
        }
        foreach (var b in buildings)
        {
            h.Mix(b.Id);
            h.Mix((long)b.Type);
            h.Mix((long)b.Facing);
            h.Mix(b.Origin.X);
            h.Mix(b.Origin.Y);
            b.Hash(ref h);
        }
        foreach (var line in lines)
            line.Hash(ref h);
        return h.Value;
    }

    // ---- Belt topology -----------------------------------------------------------------------

    /// <summary>A belt pointing at this cell from the given side (a bridge entry feeds its deck, not its neighbour).</summary>
    bool FeedsFrom(Belt belt, Dir side) =>
        BuildingAt(belt.Origin + side.Offset()) is Belt n && n.Facing == side.Opposite() && n.Role != BeltRole.BridgeEntry;

    void RefreshCurve(Cell c)
    {
        if (BuildingAt(c) is not Belt belt)
            return;
        if (belt.Role != BeltRole.Plain)
        {
            belt.CurveIn = null;
            return;
        }
        var left = belt.Facing.CounterClockwise();
        var right = belt.Facing.Clockwise();
        bool fromLeft = FeedsFrom(belt, left), fromRight = FeedsFrom(belt, right);
        if (FeedsFrom(belt, belt.Facing.Opposite()) || fromLeft == fromRight)
            belt.CurveIn = null;
        else
            belt.CurveIn = (fromLeft ? left : right).Opposite();
    }

    /// <summary>The tile goods flow into from this one along the same line, if any.</summary>
    Belt? ChainNext(Belt a)
    {
        if (a.Role == BeltRole.BridgeEntry)
            return a.Partner;
        if (BuildingAt(a.Origin + a.Facing.Offset()) is not Belt b)
            return null;
        return b.Role != BeltRole.BridgeExit && b.Tier == a.Tier && b.PrimaryIn == a.Facing ? b : null;
    }

    /// <summary>The tile that feeds this one along the same line, if any.</summary>
    Belt? ChainPrev(Belt b)
    {
        if (b.Role == BeltRole.BridgeExit)
            return b.Partner;
        if (BuildingAt(b.Origin - b.PrimaryIn.Offset()) is not Belt a)
            return null;
        return a.Role != BeltRole.BridgeEntry && a.Tier == b.Tier && a.Facing == b.PrimaryIn ? a : null;
    }

    /// <summary>
    /// Belts changed at these cells: recompute curves and bridge tiers nearby, dissolve every line
    /// that could be affected, and chain the loose tiles back into lines with their goods in place.
    /// </summary>
    void OnBeltsChanged(params Cell[] cells)
    {
        var region = new SortedSet<Cell>();
        foreach (var c in cells)
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2 + Math.Abs(dx); dy <= 2 - Math.Abs(dx); dy++)
                    region.Add(new Cell(c.X + dx, c.Y + dy));

        foreach (var c in region)
            RefreshCurve(c);

        // A bridge takes the tier of its feed; a trestle fed by another's exit follows that one in turn,
        // however far down the chain it stands.
        var retiered = new List<Belt>();
        var pending = new Queue<Belt>();
        foreach (var c in region)
            if (BuildingAt(c) is Belt { Role: BeltRole.BridgeEntry } entry)
                pending.Enqueue(entry);
        while (pending.Count > 0)
        {
            var entry = pending.Dequeue();
            var feeder = BuildingAt(entry.Origin - entry.Facing.Offset()) as Belt;
            var tier = feeder != null && feeder.Facing == entry.Facing && feeder.Role != BeltRole.BridgeEntry ? feeder.Tier : BeltTier.Canvas;
            if (entry.Tier == tier && entry.Partner!.Tier == tier)
                continue;
            entry.Tier = tier;
            entry.Partner!.Tier = tier;
            retiered.Add(entry);
            if (BuildingAt(entry.Partner.Exit) is Belt { Role: BeltRole.BridgeEntry } next && next.Facing == entry.Facing)
                pending.Enqueue(next);
        }

        var work = new List<Belt>();
        var seen = new HashSet<Belt>();
        foreach (var c in region)
        {
            if (BuildingAt(c) is not Belt b)
                continue;
            DissolveInto(b, work, seen);
            if (b.Partner is { } partner)
                DissolveInto(partner, work, seen);
        }
        // A retiered bridge re-chains with the belt past its exit, wherever it is.
        foreach (var entry in retiered)
        {
            DissolveInto(entry, work, seen);
            DissolveInto(entry.Partner!, work, seen);
            if (BuildingAt(entry.Partner!.Exit) is Belt after)
                DissolveInto(after, work, seen);
        }
        RebuildLines(work, seen);
        RefreshSplittersNear(region);
        if (retiered.Count > 0)
            RefreshSplittersNear(retiered.Select(e => e.Partner!.Origin));
    }

    void DissolveInto(Belt b, List<Belt> work, HashSet<Belt> seen)
    {
        if (b.Line is { } line)
        {
            var tiles = new List<Belt>(line.Tiles);
            line.Dissolve();
            RemoveLine(line);
            foreach (var t in tiles)
                if (seen.Add(t))
                    work.Add(t);
        }
        else if (seen.Add(b))
        {
            work.Add(b);
        }
    }

    void RebuildLines(List<Belt> work, HashSet<Belt> seen)
    {
        // Row-major order, so the same edit always yields the same line ids.
        work.Sort((a, b) => a.Origin.CompareTo(b.Origin));
        var goods = new List<(Item, int)>();
        for (int w = 0; w < work.Count; w++)
        {
            var start = work[w];
            if (start.Line != null)
                continue;

            // Walk back to the head. A tile still on an intact line joins the rebuild.
            var head = start;
            var visited = new HashSet<Belt> { head };
            while (true)
            {
                var prev = ChainPrev(head);
                if (prev == null)
                    break;
                if (prev.Line != null)
                {
                    DissolveInto(prev, work, seen);
                    continue;
                }
                if (!visited.Add(prev))
                {
                    // A closed loop: start it from its first cell in row-major order.
                    head = visited.MinBy(t => t.Origin)!;
                    break;
                }
                head = prev;
            }

            var line = new TransportLine(nextLineId++, head.Tier, TileOutput.Instance);
            var cur = head;
            while (cur != null && cur.Line == null)
            {
                line.AddTile(cur);
                cur = ChainNext(cur);
            }

            goods.Clear();
            foreach (var t in line.Tiles)
            {
                if (t.Loose == null)
                    continue;
                foreach (var (item, pos) in t.Loose)
                    goods.Add((item, t.LineStart + Math.Min(pos, t.PathLength - 1)));
                t.Loose = null;
            }
            line.Load(goods);
            lines.Add(line);
        }
    }

    void RemoveLine(TransportLine line)
    {
        int lo = 0, hi = lines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int c = lines[mid].Id.CompareTo(line.Id);
            if (c == 0)
            {
                lines.RemoveAt(mid);
                return;
            }
            if (c < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
    }

    void RefreshSplittersNear(IEnumerable<Cell> cells)
    {
        var done = new HashSet<Splitter>();
        foreach (var c in cells)
            for (int d = 0; d < 4; d++)
                if (BuildingAt(c + ((Dir)d).Offset()) is Splitter s && done.Add(s))
                    s.RefreshLaneTiers(this);
    }
}
