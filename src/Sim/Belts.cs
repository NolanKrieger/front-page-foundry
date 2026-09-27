namespace FrontPageFoundry.Sim;

public enum BeltTier : byte { Canvas, Rubber, Steel }

public static class BeltTiers
{
    /// <summary>Sub-units along one tile of belt. Positions are integers so the sim is exactly repeatable.</summary>
    public const int TileLength = 240;
    /// <summary>Centre-to-centre distance between goods: two per tile.</summary>
    public const int Spacing = 120;

    /// <summary>Sub-units per tick: 1, 2 and 4 tiles per second, so 2, 4 and 8 goods per second (GDD §4).</summary>
    public static int Speed(BeltTier tier) => tier switch
    {
        BeltTier.Canvas => 4,
        BeltTier.Rubber => 8,
        _ => 16,
    };

    public static double GoodsPerSecond(BeltTier tier) => Speed(tier) * (double)World.TicksPerSecond / Spacing;

    public static double TilesPerSecond(BeltTier tier) => Speed(tier) * (double)World.TicksPerSecond / TileLength;
}

public enum BeltRole : byte { Plain, BridgeEntry, BridgeExit }

/// <summary>
/// One tile of conveyor, or one end of a trestle bridge. Goods do not live on the tile: every tile
/// belongs to exactly one <see cref="TransportLine"/>, which moves them.
/// </summary>
public sealed class Belt : Building
{
    public Belt(int id, BuildingType type, Cell origin, Dir facing, BeltRole role = BeltRole.Plain, int span = 0)
        : base(id, type, origin, facing)
    {
        Tier = Catalog.Of(type).IsBridge ? BeltTier.Canvas : Catalog.TierOf(type);
        Role = role;
        Span = span;
    }

    /// <summary>Speed class. A bridge takes the tier of the belt feeding it (GDD §16).</summary>
    public BeltTier Tier { get; internal set; }
    public BeltRole Role { get; }
    /// <summary>Bridge entry: how many cells the deck crosses before the exit.</summary>
    public int Span { get; }
    /// <summary>The other end of this bridge.</summary>
    public Belt? Partner { get; internal set; }

    /// <summary>Travel direction of goods entering this belt round a corner, or null for a straight belt.</summary>
    public Dir? CurveIn { get; internal set; }
    /// <summary>Travel direction of goods that enter along the line: from behind, or round the curve.</summary>
    public Dir PrimaryIn => CurveIn ?? Facing;

    public TransportLine? Line { get; internal set; }
    public int LineIndex { get; internal set; }
    /// <summary>Position along the line where this tile starts.</summary>
    public int LineStart { get; internal set; }

    /// <summary>Sub-units of travel across this tile (a bridge entry includes its deck).</summary>
    public int PathLength => Role == BeltRole.BridgeEntry ? BeltTiers.TileLength * (Span + 1) : BeltTiers.TileLength;

    /// <summary>The cell goods leave this tile into.</summary>
    public Cell Exit => Role == BeltRole.BridgeEntry ? Origin + Facing.Offset() * (Span + 1) : Origin + Facing.Offset();

    /// <summary>Goods parked here while lines are rebuilt.</summary>
    internal List<(Item Item, int Pos)>? Loose;

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        if (Line == null || travel == Facing.Opposite())
            return false;
        int offset;
        if (travel == PrimaryIn)
        {
            // Only the bridge deck feeds an exit from behind.
            if (Role == BeltRole.BridgeExit)
                return false;
            offset = entryPos;
        }
        else
        {
            offset = BeltTiers.TileLength / 2;
        }
        return Line.TryInsert(world, LineStart + offset, item);
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix((long)Tier);
        h.Mix((long)Role);
        h.Mix(Span);
        h.Mix(CurveIn.HasValue ? (long)CurveIn.Value : -1);
    }
}

/// <summary>Where a line's front good goes when it reaches the end.</summary>
internal interface ILineOutput
{
    /// <param name="entryPos">Where the good's centre lands, measured from the boundary (negative: still short of it).</param>
    bool TryPush(World world, TransportLine line, Item item, int entryPos);
}

/// <summary>The default: into whatever building the last tile faces.</summary>
internal sealed class TileOutput : ILineOutput
{
    public static readonly TileOutput Instance = new();

    public bool TryPush(World world, TransportLine line, Item item, int entryPos)
    {
        var last = line.Tiles[^1];
        return world.Offer(item, last.Exit, last.Facing, entryPos);
    }
}

/// <summary>A good on a line and its slack: free distance to the good ahead (or to the line end for the front good).</summary>
internal struct Slot
{
    public Item Item;
    public int Gap;
}

/// <summary>Ring buffer with O(1) push-back and pop-front and a shortest-side shift for inserts.</summary>
internal sealed class SlotRing
{
    Slot[] buf = new Slot[8];
    int head, count;

    public int Count => count;

    public ref Slot this[int i] => ref buf[(head + i) & (buf.Length - 1)];

    public void PushBack(Slot s)
    {
        Grow();
        buf[(head + count) & (buf.Length - 1)] = s;
        count++;
    }

    public Slot PopFront()
    {
        var s = buf[head];
        head = (head + 1) & (buf.Length - 1);
        count--;
        return s;
    }

    public void Insert(int i, Slot s)
    {
        Grow();
        if (i < count - i)
        {
            head = (head - 1) & (buf.Length - 1);
            for (int k = 0; k < i; k++)
                this[k] = this[k + 1];
        }
        else
        {
            for (int k = count; k > i; k--)
                this[k] = this[k - 1];
        }
        this[i] = s;
        count++;
    }

    public void Clear()
    {
        head = 0;
        count = 0;
    }

    void Grow()
    {
        if (count < buf.Length)
            return;
        var n = new Slot[buf.Length * 2];
        for (int k = 0; k < count; k++)
            n[k] = this[k];
        buf = n;
        head = 0;
    }
}

/// <summary>
/// A maximal chain of belt tiles that goods flow along without any choice (GDD §14). Goods are
/// stored front to back as (good, gap). Each tick only the goods behind the first moving one shift,
/// as a block, so a moving line costs O(1) and a backed-up line costs nothing but its hand-off try.
///
/// Positions are the centres of goods. A good sits in slots half a spacing in from each end, so a
/// jammed line holds exactly two per tile; the front good transfers to the next line when it would
/// pass <see cref="End"/>, landing there half a spacing short of that line's start.
/// </summary>
public sealed partial class TransportLine
{
    readonly List<Belt> tiles = new();
    readonly SlotRing items = new();
    readonly ILineOutput output;
    /// <summary>Goods before this index are stopped, touching, with the front one at the line end.</summary>
    int firstMoving;
    /// <summary>Position of the last good; only meaningful when there are goods.</summary>
    int tailPos;
    long lastTick = -1;

    internal TransportLine(int id, BeltTier tier, ILineOutput output)
    {
        Id = id;
        Tier = tier;
        this.output = output;
    }

    public int Id { get; internal set; }
    public BeltTier Tier { get; internal set; }
    public int Speed => BeltTiers.Speed(Tier);
    /// <summary>Goods that have left the front of this line so far (the flow overlay measures a rate from it).</summary>
    public long Passed { get; private set; }
    public IReadOnlyList<Belt> Tiles => tiles;
    public int Length { get; private set; }
    /// <summary>Where the front good stops or transfers: half a spacing before the last tile ends.</summary>
    public int End => Length - BeltTiers.Spacing / 2;
    public int Count => items.Count;
    public int FirstMoving => firstMoving;
    /// <summary>True when nothing on it can move until something downstream accepts the front good.</summary>
    public bool Stalled => items.Count > 0 && firstMoving >= items.Count;
    /// <summary>True when the front good is waiting at the end for something to take it.</summary>
    public bool FrontParked => items.Count > 0 && firstMoving > 0;

    /// <summary>Cells this line covers, deck included (for culling).</summary>
    public Cell Min { get; private set; } = new(int.MaxValue, int.MaxValue);
    public Cell Max { get; private set; } = new(int.MinValue, int.MinValue);

    internal void AddTile(Belt b)
    {
        b.Line = this;
        b.LineIndex = tiles.Count;
        b.LineStart = Length;
        tiles.Add(b);
        Length += b.PathLength;
        Cover(b.Origin);
        if (b.Role == BeltRole.BridgeEntry)
            Cover(b.Exit);
    }

    void Cover(Cell c)
    {
        Min = new Cell(Math.Min(Min.X, c.X), Math.Min(Min.Y, c.Y));
        Max = new Cell(Math.Max(Max.X, c.X), Math.Max(Max.Y, c.Y));
    }

    /// <summary>Virtual line (a splitter lane) of a fixed length with no tiles.</summary>
    internal void SetLength(int length) => Length = length;

    /// <summary>Goods front to back with their position along the line and whether they are moving.</summary>
    public IEnumerable<(Item Item, int Pos, bool Moving)> Goods
    {
        get
        {
            int pos = End;
            for (int i = 0; i < items.Count; i++)
            {
                pos -= i == 0 ? items[i].Gap : items[i].Gap + BeltTiers.Spacing;
                yield return (items[i].Item, pos, i >= firstMoving);
            }
        }
    }

    /// <summary>The good about to leave, or null.</summary>
    public Item? Front => items.Count > 0 ? items[0].Item : null;

    internal void Tick(World world)
    {
        int n = items.Count;
        if (n == 0)
        {
            lastTick = world.TickCount;
            return;
        }
        int s = Speed;
        int i = firstMoving;
        int movedPrev = 0;
        int tailMoved;

        if (i == 0)
        {
            ref var front = ref items[0];
            if (front.Gap < s)
            {
                // It would pass the end this tick: offer it onward where its centre would land.
                if (output.TryPush(world, this, front.Item, s - front.Gap - BeltTiers.Spacing / 2))
                {
                    HandOff(s);
                    lastTick = world.TickCount;
                    return;
                }
                int move = front.Gap;
                front.Gap = 0;
                movedPrev = move;
                tailMoved = move;
                firstMoving = 1;
                i = 1;
            }
            else
            {
                front.Gap -= s;
                tailPos += s;
                lastTick = world.TickCount;
                return;
            }
        }
        else
        {
            // The front good is parked at the end; keep offering it.
            if (output.TryPush(world, this, items[0].Item, s - BeltTiers.Spacing / 2))
            {
                HandOff(s);
                lastTick = world.TickCount;
                return;
            }
            tailMoved = 0;
        }

        while (i < n)
        {
            ref var it = ref items[i];
            int room = it.Gap + movedPrev;
            if (room >= s)
            {
                it.Gap = room - s;
                tailMoved = s;
                break;
            }
            it.Gap = 0;
            movedPrev = room;
            tailMoved = room;
            firstMoving = i + 1;
            i++;
        }
        tailPos += tailMoved;
        lastTick = world.TickCount;
    }

    /// <summary>The front good left; everything behind it now moves the full speed this tick.</summary>
    void HandOff(int s)
    {
        var gone = items.PopFront();
        Passed++;
        firstMoving = 0;
        if (items.Count == 0)
            return;
        // The new front's distance to the end, less this tick's travel (Spacing ≥ speed keeps it positive).
        items[0].Gap += gone.Gap + BeltTiers.Spacing - s;
        tailPos += s;
    }

    /// <summary>
    /// Puts a good on the line at <paramref name="pos"/>, if there is room. If the line has not ticked yet
    /// this tick the good is placed one step back so this tick's travel lands it exactly there.
    /// </summary>
    internal bool TryInsert(World world, int pos, Item item)
    {
        int p = lastTick < world.TickCount ? pos - Speed : pos;
        int n = items.Count;
        if (p > End)
            return false;
        if (n == 0)
        {
            items.PushBack(new Slot { Item = item, Gap = End - p });
            tailPos = p;
            firstMoving = 0;
            return true;
        }
        int clear = tailPos - BeltTiers.Spacing;
        if (p <= clear)
        {
            items.PushBack(new Slot { Item = item, Gap = clear - p });
            tailPos = p;
            return true;
        }
        if (clear >= p - Speed)
        {
            // The tail clears the spot within a tick's travel: take the good a little early, just
            // behind the start, so a source feeding every tick keeps goods exactly one spacing apart.
            items.PushBack(new Slot { Item = item, Gap = 0 });
            tailPos = clear;
            return true;
        }
        if (p < tailPos + BeltTiers.Spacing)
            return false;

        // Ahead of the tail: walk from the front to find the neighbours.
        int ahead = End;
        int posI = End - items[0].Gap;
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
                posI -= BeltTiers.Spacing + items[i].Gap;
            if (p > posI)
            {
                if (ahead - p < (i == 0 ? 0 : BeltTiers.Spacing) || p - posI < BeltTiers.Spacing)
                    return false;
                items.Insert(i, new Slot { Item = item, Gap = i == 0 ? End - p : ahead - p - BeltTiers.Spacing });
                items[i + 1].Gap = p - posI - BeltTiers.Spacing;
                if (i < firstMoving)
                    firstMoving = i;
                return true;
            }
            ahead = posI;
        }
        return false;
    }

    /// <summary>
    /// The furthest a good may sit short of its line's start. A good handed on from another line lands
    /// half a spacing short (plus up to a tick or two of travel), and a ring's newest good sits there too,
    /// so a rebuild must keep such goods rather than treat them as pushed off the belt.
    /// </summary>
    internal const int LowestPos = -BeltTiers.Spacing;

    /// <summary>Takes every good off the line, parking it on its tile (used while lines are rebuilt).</summary>
    internal void Dissolve()
    {
        int t = tiles.Count - 1;
        foreach (var (item, pos, _) in Goods)
        {
            while (t > 0 && pos < tiles[t].LineStart)
                t--;
            var tile = tiles[t];
            // Negative only on the first tile: a good still short of the line's start keeps its place.
            (tile.Loose ??= new()).Add((item, pos - tile.LineStart));
        }
        items.Clear();
        foreach (var tile in tiles)
        {
            tile.Line = null;
            tile.LineIndex = 0;
            tile.LineStart = 0;
        }
        tiles.Clear();
        Length = 0;
        Min = new Cell(int.MaxValue, int.MaxValue);
        Max = new Cell(int.MinValue, int.MinValue);
    }

    /// <summary>Loads goods by absolute position (any order), pushing any that overlap back and dropping any pushed more than <see cref="LowestPos"/> short of the start.</summary>
    internal void Load(List<(Item Item, int Pos)> goods)
    {
        items.Clear();
        firstMoving = 0;
        if (goods.Count == 0)
            return;
        goods.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        int ahead = End;
        bool first = true;
        foreach (var (item, pos0) in goods)
        {
            int pos = Math.Min(pos0, first ? End : ahead - BeltTiers.Spacing);
            if (pos < LowestPos)
                break;
            items.PushBack(new Slot { Item = item, Gap = first ? End - pos : ahead - pos - BeltTiers.Spacing });
            ahead = pos;
            tailPos = pos;
            first = false;
        }
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(Id);
        h.Mix((long)Tier);
        h.Mix(Length);
        h.Mix(firstMoving);
        h.Mix(items.Count > 0 ? tailPos : 0);
        for (int i = 0; i < items.Count; i++)
        {
            h.Mix((long)items[i].Item);
            h.Mix(items[i].Gap);
        }
    }
}
