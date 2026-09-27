using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FrontPageFoundry.Sim;

/// <summary>What the company list shows without opening the body (GDD §14).</summary>
public sealed record SaveHeader(int Version, string Company, string Difficulty, int Seed, string Mode, long Tick, long Day,
    long ShareCents, long CashCents, string SavedAt);

/// <summary>A world captured for saving: its header and its uncompressed state, immutable and safe to hand to another thread.</summary>
public sealed class SaveSnapshot
{
    internal SaveSnapshot(byte[] header, byte[] state, long tick)
    {
        Header = header;
        State = state;
        Tick = tick;
    }

    internal byte[] Header { get; }
    internal byte[] State { get; }
    /// <summary>The world's tick when it was taken.</summary>
    public long Tick { get; }
}

/// <summary>
/// The save format (GDD §14): a magic, a version, a small JSON header, a Brotli-compressed binary
/// body holding the whole world, and a checksum of the body. Every class writes and reads its own
/// state beside its <c>Hash</c>, so a round trip reproduces the state hash exactly.
/// </summary>
public static class SaveGame
{
    public const int Version = 1;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("FPF1");

    public static void Write(World w, Stream stream, string company) => Write(Capture(w, company), stream);

    /// <summary>
    /// The world's state as bytes, not yet compressed. Taking it is quick and must happen on the thread that
    /// ticks the world; packing and writing it (<see cref="Write(SaveSnapshot, Stream)"/>) may happen on any
    /// thread, so an autosave of a large works never stalls a frame for the compression.
    /// </summary>
    public static SaveSnapshot Capture(World w, string company)
    {
        var header = new SaveHeader(Version, company, w.Difficulty.Id, w.Seed, w.Map.Mode.ToString(), w.TickCount, w.Day,
            w.SharePriceCents, w.CashCents, DateTime.UtcNow.ToString("O"));
        using var raw = new MemoryStream();
        using (var b = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
            w.WriteState(b);
        return new SaveSnapshot(JsonSerializer.SerializeToUtf8Bytes(header), raw.ToArray(), w.TickCount);
    }

    public static void Write(SaveSnapshot snapshot, Stream stream)
    {
        byte[] body;
        using (var packed = new MemoryStream())
        {
            using (var brotli = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                brotli.Write(snapshot.State);
            body = packed.ToArray();
        }
        using var o = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        o.Write(Magic);
        o.Write(Version);
        o.Write(snapshot.Header.Length);
        o.Write(snapshot.Header);
        o.Write(body.Length);
        o.Write(body);
        o.Write(Checksum(body));
        o.Flush();
    }

    public static SaveHeader ReadHeader(Stream stream)
    {
        using var i = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadHeader(i, out _);
    }

    /// <summary>The header is a few hundred bytes; anything claiming more is a damaged length.</summary>
    const int MaxHeaderBytes = 64 * 1024;

    /// <summary>A length read from the file that cannot be right (negative, or past the end of the file).</summary>
    static bool BadLength(Stream s, int len, int max = int.MaxValue) =>
        len < 0 || len > max || s.CanSeek && len > s.Length - s.Position;

    static SaveHeader ReadHeader(BinaryReader i, out int version)
    {
        var magic = i.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("not a Front Page Foundry save");
        version = i.ReadInt32();
        if (version < 1 || version > Version)
            throw new InvalidDataException($"save version {version} is not readable by this build ({Version})");
        int len = i.ReadInt32();
        if (len == 0 || BadLength(i.BaseStream, len, MaxHeaderBytes))
            throw new InvalidDataException("save header is damaged");
        SaveHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<SaveHeader>(i.ReadBytes(len));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("save header is damaged", e);
        }
        if (header?.Company == null || header.Difficulty == null || header.Mode == null || header.SavedAt == null)
            throw new InvalidDataException("bad header");
        return header;
    }

    public static World Read(Stream stream)
    {
        using var i = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var header = ReadHeader(i, out int version);
        int len = i.ReadInt32();
        if (BadLength(stream, len))
            throw new InvalidDataException("save body is damaged");
        var body = i.ReadBytes(len);
        if (body.Length != len || i.ReadUInt64() != Checksum(body))
            throw new InvalidDataException("save body is damaged");
        var difficulty = Difficulty.All.FirstOrDefault(d => d.Id == header.Difficulty) ?? Difficulty.SteadyTrade;
        if (!Enum.TryParse<MapMode>(header.Mode, out var mode) || !Enum.IsDefined(mode))
            throw new InvalidDataException($"unknown map mode {header.Mode}");
        var w = World.Blank(header.Seed, difficulty, mode);
        using var brotli = new BrotliStream(new MemoryStream(body), CompressionMode.Decompress);
        using var b = new BinaryReader(brotli, Encoding.UTF8);
        try
        {
            w.ReadState(b, version);
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            // A body that passed its checksum but will not rebuild (a table index out of range, a short stream):
            // report it as a damaged save so the store tries the backups instead of the game falling over.
            throw new InvalidDataException("save body could not be read", e);
        }
        return w;
    }

    /// <summary>FNV-1a over the compressed body: a damaged file is refused rather than loaded wrong.</summary>
    static ulong Checksum(byte[] data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte x in data)
        {
            h ^= x;
            h *= 1099511628211UL;
        }
        return h;
    }

    // ---- Little helpers shared by every class ------------------------------------------------

    internal static void Write(this BinaryWriter b, Cell c)
    {
        b.Write(c.X);
        b.Write(c.Y);
    }

    internal static Cell ReadCell(this BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());

    internal static void Write(this BinaryWriter b, Cell? c)
    {
        b.Write(c.HasValue);
        if (c.HasValue)
            b.Write(c.Value);
    }

    internal static Cell? ReadCellOrNull(this BinaryReader r) => r.ReadBoolean() ? r.ReadCell() : null;

    internal static void Write(this BinaryWriter b, Item? item) => b.Write(item.HasValue ? (int)item.Value : -1);

    internal static Item? ReadItemOrNull(this BinaryReader r)
    {
        int v = r.ReadInt32();
        return v < 0 ? null : (Item)v;
    }

    internal static void Write(this BinaryWriter b, Command c)
    {
        switch (c)
        {
            case Place p when p.Setting is null && p.Id is null && p.Fire is null: b.Write((byte)1); b.Write((int)p.Type); b.Write(p.Origin); b.Write((byte)p.Facing); b.Write(p.Span); WriteLong(b, p.PriceCents); break;
            case Remove rm when rm.Id is null: b.Write((byte)2); b.Write(rm.Cell); WriteLong(b, rm.RefundCents); break;
            case Rotate ro: b.Write((byte)3); b.Write(ro.Cell); b.Write((byte)ro.Facing); break;
            case SetFilter f: b.Write((byte)4); b.Write(f.Cell); b.Write(f.Filter); break;
            case SetOrder o: b.Write((byte)5); b.Write(o.Cell); b.Write(o.Item); break;
            case Assign a: b.Write((byte)6); b.Write((int)a.Type); b.Write(a.A); b.Write(a.B); WriteLong(b, a.PriceCents); b.Write(a.Id ?? -1); break;
            case Route r: b.Write((byte)7); b.Write(r.Id); b.Write(r.A); b.Write(r.B); break;
            case Scrap s: b.Write((byte)8); b.Write(s.Id); WriteLong(b, s.RefundCents); break;
            case ClearSignal cs: b.Write((byte)9); b.Write(cs.Cell); WriteLong(b, cs.RefundCents); break;
            case Draft d: b.Write((byte)10); b.Write((int)d.Type); b.Write(d.Origin); b.Write((byte)d.Facing); b.Write(d.Span); b.Write(d.Setting); break;
            case Undraft u: b.Write((byte)11); b.Write(u.Cell); break;
            case SetPlanning sp: b.Write((byte)12); b.Write(sp.On); break;
            // Undo records added after 0.9.0-rc1 get their own tags, so an RC save's log reads unchanged.
            case Place p:
                b.Write((byte)13); b.Write((int)p.Type); b.Write(p.Origin); b.Write((byte)p.Facing); b.Write(p.Span); WriteLong(b, p.PriceCents); b.Write(p.Setting); b.Write(p.Id ?? -1);
                b.Write(p.Fire.HasValue);
                if (p.Fire is { } fire)
                {
                    b.Write(fire.Coal);
                    b.Write(fire.Burn);
                }
                break;
            case Remove rm: b.Write((byte)14); b.Write(rm.Cell); WriteLong(b, rm.RefundCents); b.Write(rm.Id!.Value); break;
            default: throw new InvalidDataException($"cannot save command {c}");
        }
    }

    internal static Command ReadCommand(this BinaryReader r)
    {
        switch (r.ReadByte())
        {
            case 1: return new Place((BuildingType)r.ReadInt32(), r.ReadCell(), (Dir)r.ReadByte(), r.ReadInt32(), ReadLong(r));
            case 2: return new Remove(r.ReadCell(), ReadLong(r));
            case 3: return new Rotate(r.ReadCell(), (Dir)r.ReadByte());
            case 4: return new SetFilter(r.ReadCell(), r.ReadItemOrNull());
            case 5: return new SetOrder(r.ReadCell(), r.ReadItemOrNull());
            case 6:
            {
                var type = (BuildingType)r.ReadInt32();
                var a = r.ReadCell();
                var bb = r.ReadCell();
                var price = ReadLong(r);
                int id = r.ReadInt32();
                return new Assign(type, a, bb, price, id < 0 ? null : id);
            }
            case 7: return new Route(r.ReadInt32(), r.ReadCellOrNull(), r.ReadCellOrNull());
            case 8: return new Scrap(r.ReadInt32(), ReadLong(r));
            case 9: return new ClearSignal(r.ReadCell(), ReadLong(r));
            case 10: return new Draft((BuildingType)r.ReadInt32(), r.ReadCell(), (Dir)r.ReadByte(), r.ReadInt32(), r.ReadItemOrNull());
            case 11: return new Undraft(r.ReadCell());
            case 12: return new SetPlanning(r.ReadBoolean());
            case 13:
            {
                var place = new Place((BuildingType)r.ReadInt32(), r.ReadCell(), (Dir)r.ReadByte(), r.ReadInt32(), ReadLong(r), r.ReadItemOrNull());
                int id = r.ReadInt32();
                FireState? fire = r.ReadBoolean() ? new FireState(r.ReadInt32(), r.ReadInt64()) : null;
                return place with { Id = id < 0 ? null : id, Fire = fire };
            }
            case 14: return new Remove(r.ReadCell(), ReadLong(r), r.ReadInt32());
            default: throw new InvalidDataException("unknown command in save");
        }
    }

    static void WriteLong(BinaryWriter b, long? v)
    {
        b.Write(v.HasValue);
        if (v.HasValue)
            b.Write(v.Value);
    }

    static long? ReadLong(BinaryReader r) => r.ReadBoolean() ? r.ReadInt64() : null;

    internal static void WriteItems(this BinaryWriter b, IReadOnlyList<Item> items)
    {
        b.Write(items.Count);
        foreach (var i in items)
            b.Write((byte)i);
    }

    internal static List<Item> ReadItems(this BinaryReader r)
    {
        int n = r.ReadInt32();
        var list = new List<Item>(n);
        for (int k = 0; k < n; k++)
            list.Add((Item)r.ReadByte());
        return list;
    }
}

// ---- Each class saves what it hashes ---------------------------------------------------------

public sealed partial class Pcg32
{
    internal void Write(BinaryWriter b) => b.Write(state);
    internal void Read(BinaryReader r) => state = r.ReadUInt64();
}

public sealed partial class Firebox
{
    internal void Write(BinaryWriter b)
    {
        b.Write(Coal);
        b.Write(BurnTicks);
    }

    internal void Read(BinaryReader r)
    {
        Coal = r.ReadInt32();
        BurnTicks = r.ReadInt32();
    }
}

public sealed partial class TransportLine
{
    /// <summary>The slots exactly as kept (a good's gap may put it just short of the line's start), plus the bookkeeping the hash covers.</summary>
    internal void WriteGoods(BinaryWriter b)
    {
        b.Write(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            b.Write((byte)items[i].Item);
            b.Write(items[i].Gap);
        }
        b.Write(firstMoving);
        b.Write(tailPos);
        b.Write(lastTick);
    }

    internal void ReadGoods(BinaryReader r)
    {
        items.Clear();
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            items.PushBack(new Slot { Item = (Item)r.ReadByte(), Gap = r.ReadInt32() });
        firstMoving = r.ReadInt32();
        tailPos = r.ReadInt32();
        lastTick = r.ReadInt64();
    }
}

public sealed partial class Machine
{
    internal void Write(BinaryWriter b)
    {
        b.Write(input.Length);
        foreach (int v in input)
            b.Write(v);
        b.WriteItems(output);
        b.Write(nextRecipe);
        b.Write(progressMilli);
        b.Write(Current == null ? -1 : Array.IndexOf(Recipes.All, Current));
        b.Write((byte)State);
        b.Write((byte)Source);
        b.Write(Runs);
        b.Write(Firebox != null);
        Firebox?.Write(b);
    }

    internal void Read(BinaryReader r)
    {
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int v = r.ReadInt32();
            if (i < input.Length)
                input[i] = v;
        }
        output.Clear();
        output.AddRange(r.ReadItems());
        nextRecipe = r.ReadInt32();
        progressMilli = r.ReadInt64();
        int recipe = r.ReadInt32();
        Current = recipe < 0 ? null : Recipes.All[recipe];
        State = (MachineState)r.ReadByte();
        Source = (PowerSource)r.ReadByte();
        Runs = r.ReadInt64();
        if (r.ReadBoolean())
        {
            if (Firebox != null)
                Firebox.Read(r);
            else
                new Firebox(1).Read(r);     // saved with a firebox this machine no longer has: skip it
        }
    }
}

public sealed partial class Mine
{
    internal void Write(BinaryWriter b)
    {
        b.Write(progress);
        b.Write(Buffered);
        b.Write(Extracted);
        b.Write((byte)Source);
        Firebox.Write(b);
    }

    internal void Read(BinaryReader r)
    {
        progress = r.ReadInt64();
        Buffered = r.ReadInt32();
        Extracted = r.ReadInt64();
        Source = (PowerSource)r.ReadByte();
        Firebox.Read(r);
    }
}

public sealed partial class LoggingCamp
{
    internal void Write(BinaryWriter b)
    {
        b.Write(progress);
        b.Write(Trees);
        b.Write(Felled);
        b.Write(Buffered);
        b.Write((byte)Source);
        Firebox.Write(b);
    }

    internal void Read(BinaryReader r)
    {
        progress = r.ReadInt64();
        Trees = r.ReadInt32();
        Felled = r.ReadInt64();
        Buffered = r.ReadInt32();
        Source = (PowerSource)r.ReadByte();
        Firebox.Read(r);
    }
}

public sealed partial class PumpJack
{
    internal void Write(BinaryWriter b)
    {
        b.Write(progress);
        b.Write(Pumped);
        b.Write(Buffered);
        b.Write((byte)Source);
        Firebox.Write(b);
    }

    internal void Read(BinaryReader r)
    {
        progress = r.ReadInt64();
        Pumped = r.ReadInt64();
        Buffered = r.ReadInt32();
        Source = (PowerSource)r.ReadByte();
        Firebox.Read(r);
    }
}

public sealed partial class Depot
{
    internal void Write(BinaryWriter b) => b.Write(ItemsSold);
    internal void Read(BinaryReader r) => ItemsSold = r.ReadInt64();
}

public sealed partial class Dock
{
    internal void Write(BinaryWriter b)
    {
        b.Write(Order);
        b.Write(UnitsBought);
        b.Write(SpentCents);
    }

    internal void Read(BinaryReader r)
    {
        Order = r.ReadItemOrNull();
        UnitsBought = r.ReadInt64();
        SpentCents = r.ReadInt64();
    }
}

public sealed partial class Yard
{
    internal void Write(BinaryWriter b) => b.Write(Taken);
    internal void Read(BinaryReader r) => Taken = r.ReadInt64();
}

public sealed partial class Terminal
{
    internal void Write(BinaryWriter b)
    {
        b.WriteItems(inbound);
        b.WriteItems(outbound);
        b.Write(Received);
        b.Write(Shipped);
    }

    internal void Read(BinaryReader r)
    {
        inbound.Clear();
        inbound.AddRange(r.ReadItems());
        outbound.Clear();
        outbound.AddRange(r.ReadItems());
        Received = r.ReadInt64();
        Shipped = r.ReadInt64();
    }
}

public sealed partial class Boiler
{
    internal void Write(BinaryWriter b)
    {
        b.Write(Coal);
        b.Write(burnMilliKws);
    }

    internal void Read(BinaryReader r)
    {
        Coal = r.ReadInt32();
        burnMilliKws = r.ReadInt64();
    }
}

public sealed partial class Splitter
{
    internal void Write(BinaryWriter b)
    {
        b.Write(nextOut);
        b.Write(laneToggle);
        b.Write(Filter);
        foreach (var lane in Lanes)
        {
            b.Write(lane.Id);
            b.Write((byte)lane.Tier);
            lane.WriteGoods(b);
        }
    }

    internal void Read(BinaryReader r)
    {
        nextOut = r.ReadInt32();
        laneToggle = r.ReadInt32();
        Filter = r.ReadItemOrNull();
        foreach (var lane in Lanes)
        {
            lane.Id = r.ReadInt32();
            lane.Tier = (BeltTier)r.ReadByte();
            lane.ReadGoods(r);
        }
    }
}

public sealed partial class Vehicle
{
    internal void Write(BinaryWriter b)
    {
        b.Write(A);
        b.Write(B);
        b.Write(At);
        b.Write((byte)State);
        b.Write((byte)Reason);
        b.Write(Underway);
        b.Write(unloading);
        b.Write(toB);
        b.Write(index);
        b.Write(posSu);
        b.Write(dwell);
        b.Write(retry);
        b.Write(pathVersion);
        b.Write(Delivered);
        b.WriteItems(cargo);
        b.Write(Path?.Count ?? -1);
        if (Path != null)
            foreach (var c in Path)
                b.Write(c);
    }

    internal void Read(BinaryReader r)
    {
        A = r.ReadCellOrNull();
        B = r.ReadCellOrNull();
        At = r.ReadCell();
        State = (VehicleState)r.ReadByte();
        Reason = (WaitReason)r.ReadByte();
        Underway = r.ReadBoolean();
        unloading = r.ReadBoolean();
        toB = r.ReadBoolean();
        index = r.ReadInt32();
        posSu = r.ReadInt32();
        dwell = r.ReadInt32();
        retry = r.ReadInt32();
        pathVersion = r.ReadInt32();
        Delivered = r.ReadInt64();
        cargo.Clear();
        cargo.AddRange(r.ReadItems());
        int n = r.ReadInt32();
        if (n < 0)
        {
            Path = null;
            return;
        }
        Path = new List<Cell>(n);
        for (int k = 0; k < n; k++)
            Path.Add(r.ReadCell());
    }
}

public sealed partial class Haulage
{
    internal void Write(BinaryWriter b)
    {
        b.Write(nextId);
        foreach (int v in versions)
            b.Write(v);
        b.Write(vehicles.Count);
        foreach (var v in vehicles)
        {
            b.Write(v.Id);
            b.Write((int)v.Type);
            v.Write(b);
        }
    }

    internal void Read(BinaryReader r)
    {
        nextId = r.ReadInt32();
        for (int i = 0; i < versions.Length; i++)
            versions[i] = r.ReadInt32();
        int n = r.ReadInt32();
        vehicles.Clear();
        byId.Clear();
        for (int k = 0; k < n; k++)
        {
            int id = r.ReadInt32();
            var type = (BuildingType)r.ReadInt32();
            var v = new Vehicle(id, type, default);
            v.Read(r);
            vehicles.Add(v);
            byId[id] = v;
        }
        // Paths were kept, but the vehicle's path version must be seen as stale against a fresh block cut.
        blocksVersion = -1;
    }
}

public sealed partial class Market
{
    internal void Write(BinaryWriter b)
    {
        b.Write(n);
        for (int i = 0; i < n; i++)
        {
            b.Write(glutMilli[i]);
            b.Write(buyMilli[i]);
            b.Write(pendingSold[i]);
            b.Write(pendingBought[i]);
            b.Write(trendLogMilli[i]);
            b.Write(history[i].Length);
            foreach (long v in history[i])
                b.Write(v);
        }
        b.Write(demandGrowthMilli);
        b.Write(hoursStepped);
        trendRng.Write(b);
        eventRng.Write(b);
        b.Write(events.Count);
        foreach (var e in events)
        {
            b.Write(Array.IndexOf(MarketEvent.All, e.Event));
            b.Write(e.StartTick);
            b.Write(e.EndTick);
        }
    }

    internal void Read(BinaryReader r)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            long glut = r.ReadInt64(), buy = r.ReadInt64();
            int sold = r.ReadInt32(), bought = r.ReadInt32(), trend = r.ReadInt32();
            int hn = r.ReadInt32();
            var h = new long[hn];
            for (int k = 0; k < hn; k++)
                h[k] = r.ReadInt64();
            if (i >= n)
                continue;
            glutMilli[i] = glut;
            buyMilli[i] = buy;
            pendingSold[i] = sold;
            pendingBought[i] = bought;
            trendLogMilli[i] = trend;
            history[i] = h;
        }
        demandGrowthMilli = r.ReadInt64();
        hoursStepped = r.ReadInt64();
        trendRng.Read(r);
        eventRng.Read(r);
        events.Clear();
        int en = r.ReadInt32();
        for (int k = 0; k < en; k++)
        {
            int idx = r.ReadInt32();
            long start = r.ReadInt64(), end = r.ReadInt64();
            if (idx >= 0 && idx < MarketEvent.All.Length)
                events.Add(new ActiveEvent(MarketEvent.All[idx], start, end));
        }
    }
}

public sealed partial class Paper
{
    internal void Write(BinaryWriter b)
    {
        b.Write(editions.Count);
        foreach (var e in editions)
        {
            b.Write(e.Number);
            b.Write(e.Tick);
            b.Write(e.Key);
            b.Write(e.Args.Length);
            foreach (var a in e.Args)
                b.Write(a);
            b.Write(e.Inside.Length);
            foreach (var s in e.Inside)
                b.Write(s);
            b.Write(e.CircledAd.HasValue ? (int)e.CircledAd.Value : -1);
        }
        b.Write(telegrams.Count);
        foreach (var t in telegrams)
        {
            b.Write(t.Id);
            b.Write((byte)t.Item);
            b.Write(t.Quantity);
            b.Write(t.IssuedTick);
            b.Write(t.DueTick);
            b.Write(t.Sender);
            b.Write(t.SoldAtIssue);
            b.Write((byte)t.State);
            b.Write(t.Delivered);
        }
        b.Write(pending.Count);
        foreach (var (key, args, front) in pending)
        {
            b.Write(key);
            b.Write(args.Length);
            foreach (var a in args)
                b.Write(a);
            b.Write(front);
        }
        b.Write(printed.Count);
        foreach (var p in printed.OrderBy(s => s, StringComparer.Ordinal))
            b.Write(p);
        b.WriteItems(made.OrderBy(i => (int)i).ToList());
        b.Write(madeCount.Count);
        foreach (var (item, count) in madeCount.OrderBy(kv => (int)kv.Key))
        {
            b.Write((byte)item);
            b.Write(count);
        }
        b.Write(sold.Length);
        foreach (long v in sold)
            b.Write(v);
        rng.Write(b);
        b.Write(lastFrontDay);
        b.Write(nextTelegramDay);
        b.Write(nextTelegramId);
        b.Write(TutorialStep);
        b.Write(tutorialDay5);
    }

    internal void Read(BinaryReader r)
    {
        editions.Clear();
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            int number = r.ReadInt32();
            long tick = r.ReadInt64();
            string key = r.ReadString();
            var args = new string[r.ReadInt32()];
            for (int a = 0; a < args.Length; a++)
                args[a] = r.ReadString();
            var inside = new string[r.ReadInt32()];
            for (int a = 0; a < inside.Length; a++)
                inside[a] = r.ReadString();
            int circled = r.ReadInt32();
            editions.Add(new Edition(number, tick, key, args, inside, circled < 0 ? null : (BuildingType)circled));
        }
        telegrams.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            var t = new Telegram(r.ReadInt32(), (Item)r.ReadByte(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadString(), r.ReadInt64())
            {
                State = (TelegramState)r.ReadByte(),
                Delivered = r.ReadInt64(),
            };
            telegrams.Add(t);
        }
        pending.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            string key = r.ReadString();
            var args = new string[r.ReadInt32()];
            for (int a = 0; a < args.Length; a++)
                args[a] = r.ReadString();
            pending.Add((key, args, r.ReadBoolean()));
        }
        printed.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            printed.Add(r.ReadString());
        made.Clear();
        foreach (var i in r.ReadItems())
            made.Add(i);
        madeCount.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            madeCount[(Item)r.ReadByte()] = r.ReadInt64();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            long v = r.ReadInt64();
            if (k < sold.Length)
                sold[k] = v;
        }
        rng.Read(r);
        lastFrontDay = r.ReadInt64();
        nextTelegramDay = r.ReadInt64();
        nextTelegramId = r.ReadInt32();
        TutorialStep = r.ReadInt32();
        tutorialDay5 = r.ReadInt64();
        JustPrinted = null;
    }
}

public sealed partial class Town
{
    internal void Write(BinaryWriter b)
    {
        // In the set's own (insertion) order, not sorted: Town.Grow walks the set to find where to build next,
        // so a reordered copy would grow the town differently after a load.
        b.Write(tiles.Count);
        foreach (var c in tiles)
            b.Write(c);
        b.Write(Blocks);
        rng.Write(b);
        b.Write(nextGrowthDay);
    }

    internal void Read(BinaryReader r)
    {
        tiles.Clear();
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            tiles.Add(r.ReadCell());
        Blocks = r.ReadInt32();
        rng.Read(r);
        nextGrowthDay = r.ReadInt64();
        Version++;
    }
}

public sealed partial class Prestige
{
    internal void Write(BinaryWriter b)
    {
        b.Write(Completed);
        b.Write(Delivered);
        b.Write(Current?.Number ?? 0);
        if (Current != null)
        {
            b.Write(Current.Bill.Count);
            foreach (var line in Current.Bill)
                b.Write(line.Delivered);
        }
    }

    internal void Read(BinaryReader r)
    {
        Completed = r.ReadInt32();
        Delivered = r.ReadInt64();
        int number = r.ReadInt32();
        Current = number > 0 ? Commissions.Nth(number) : null;
        if (Current == null)
            return;
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            long delivered = r.ReadInt64();
            if (k < Current.Bill.Count)
                Current.Bill[k].Delivered = delivered;
        }
    }
}

public sealed partial class World
{
    /// <summary>A world with nothing founded, for a save to fill.</summary>
    internal static World Blank(int seed, Difficulty difficulty, MapMode mode) => new(seed, difficulty, mode);

    World(int seed, Difficulty difficulty, MapMode mode)
    {
        Seed = seed;
        Difficulty = difficulty;
        Map = new MapGen(seed, mode, difficulty.RichnessPercent);
        Town = new Town(seed);
        Power = new PowerGrid(this);
        Haulage = new Haulage(this);
        Market = new Market(seed, difficulty);
        Paper = new Paper(seed);
    }

    internal void WriteState(BinaryWriter b)
    {
        b.Write(TickCount);
        b.Write(CashCents);
        b.Write(RevenueCents);
        b.Write(SpentCents);
        b.Write(DebtCents);
        b.Write(InterestPaidCents);
        b.Write(Defaulted);
        b.Write(BankersWarning);
        b.Write(GoodwillCents);
        b.Write(weekStartWorth);
        b.Write(weeklyProfit.Count);
        foreach (long p in weeklyProfit)
            b.Write(p);
        b.Write(Notices.Count);
        foreach (var n in Notices)
        {
            b.Write(n.Tick);
            b.Write(n.Key);
            b.Write(n.Amount);
        }
        b.Write(owned.Length);
        foreach (int o in owned)
            b.Write(o);
        b.Write(cleared.Count);
        foreach (var c in cleared.OrderBy(c => c))
            b.Write(c);
        b.Write(flooded.Count);
        foreach (var c in flooded.OrderBy(c => c))
            b.Write(c);
        b.Write(patchExtracted.Count);
        foreach (var (id, v) in patchExtracted.OrderBy(kv => kv.Key))
        {
            b.Write(id);
            b.Write(v);
        }
        b.Write(Planning);
        b.Write(nextPlanId);
        b.Write(plans.Count);
        foreach (var p in plans)
        {
            b.Write(p.Id);
            b.Write((int)p.Type);
            b.Write(p.Origin);
            b.Write((byte)p.Facing);
            b.Write(p.Span);
            b.Write(p.Setting);
        }
        b.Write(log.Count);
        foreach (var (tick, cmd) in log)
        {
            b.Write(tick);
            b.Write(cmd);
        }
        Market.Write(b);
        Paper.Write(b);
        Town.Write(b);
        Prestige.Write(b);

        b.Write(nextId);
        b.Write(buildings.Count);
        foreach (var bd in buildings)
        {
            b.Write(bd.Id);
            b.Write((int)bd.Type);
            b.Write(bd.Origin);
            b.Write((byte)bd.Facing);
            switch (bd)
            {
                case Belt belt:
                    b.Write((byte)belt.Tier);
                    b.Write((byte)belt.Role);
                    b.Write(belt.Span);
                    b.Write(belt.Partner?.Id ?? 0);
                    b.Write(belt.CurveIn.HasValue ? (int)belt.CurveIn.Value : -1);
                    break;
                case Splitter s: s.Write(b); break;
                case Machine m: m.Write(b); break;
                case Mine m: m.Write(b); break;
                case LoggingCamp c: c.Write(b); break;
                case PumpJack j: j.Write(b); break;
                case Depot d: d.Write(b); break;
                case Dock d: d.Write(b); break;
                case Yard y: y.Write(b); break;
                case Terminal t: t.Write(b); break;
                case Boiler bo: bo.Write(b); break;
            }
        }
        b.Write(nextLineId);
        b.Write(lines.Count);
        foreach (var line in lines)
        {
            b.Write(line.Id);
            b.Write((byte)line.Tier);
            b.Write(line.Tiles.Count);
            foreach (var t in line.Tiles)
                b.Write(t.Id);
            line.WriteGoods(b);
        }
        Haulage.Write(b);
        WriteExtension(b);
    }

    /// <summary>
    /// State saved since 0.9.0-rc1, in a block after the fleet so the format stays FPF1 version 1 both ways:
    /// an rc1 build reads a newer file and stops before the block (it never looks past the fleet), and this
    /// build finds no block in an rc1 file and keeps the defaults. <see cref="ExtensionRevision"/> numbers the
    /// block's layout: later revisions append, and readers branch on it.
    /// </summary>
    const int ExtensionMarker = 0x31585846;   // "FXX1"
    const int ExtensionRevision = 1;

    void WriteExtension(BinaryWriter b)
    {
        b.Write(ExtensionMarker);
        b.Write(ExtensionRevision);
        // Revision 1: where the plan scan resumes. (A mine's or jack's yield needs no saving: it is worked out
        // from the seam's saved depletion whenever that moves, so a loaded company picks it up exactly.)
        b.Write(planCursor);
        b.Write(PostedInterestPermille);
    }

    void ReadExtension(BinaryReader r)
    {
        int marker;
        try
        {
            marker = r.ReadInt32();
        }
        catch (EndOfStreamException)
        {
            return;     // an rc1 save ends with the fleet
        }
        if (marker != ExtensionMarker)
            throw new InvalidDataException("unknown data after the fleet");
        int revision = r.ReadInt32();
        if (revision >= 1)
        {
            planCursor = r.ReadInt32();
            PostedInterestPermille = r.ReadInt32();
        }
    }

    internal void ReadState(BinaryReader r, int version)
    {
        TickCount = r.ReadInt64();
        CashCents = r.ReadInt64();
        RevenueCents = r.ReadInt64();
        SpentCents = r.ReadInt64();
        DebtCents = r.ReadInt64();
        InterestPaidCents = r.ReadInt64();
        Defaulted = r.ReadBoolean();
        BankersWarning = r.ReadBoolean();
        GoodwillCents = r.ReadInt64();
        weekStartWorth = r.ReadInt64();
        weeklyProfit.Clear();
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            weeklyProfit.Add(r.ReadInt64());
        Notices.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            Notices.Add(new Notice(r.ReadInt64(), r.ReadString(), r.ReadInt64()));
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            int v = r.ReadInt32();
            if (k < owned.Length)
                owned[k] = v;
        }
        cleared.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            cleared.Add(r.ReadCell());
        flooded.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            flooded.Add(r.ReadCell());
        patchExtracted.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            patchExtracted[r.ReadInt64()] = r.ReadInt64();
        Planning = r.ReadBoolean();
        nextPlanId = r.ReadInt32();
        plans.Clear();
        planAt.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            var plan = new Plan(r.ReadInt32(), (BuildingType)r.ReadInt32(), r.ReadCell(), (Dir)r.ReadByte(), r.ReadInt32(), r.ReadItemOrNull());
            plans.Add(plan);
            foreach (var c in plan.Cells)
                planAt[c] = plan;
        }
        log.Clear();
        n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            log.Add((r.ReadInt64(), r.ReadCommand()));
        Market.Read(r);
        Paper.Read(r);
        Town.Read(r);
        Prestige.Read(r);

        nextId = r.ReadInt32();
        n = r.ReadInt32();
        var partnerOf = new Dictionary<int, int>();
        for (int k = 0; k < n; k++)
        {
            int id = r.ReadInt32();
            var type = (BuildingType)r.ReadInt32();
            var origin = r.ReadCell();
            var facing = (Dir)r.ReadByte();
            var def = Catalog.Of(type);
            Building bd;
            if (def.IsBelt || def.IsBridge)
            {
                var tier = (BeltTier)r.ReadByte();
                var role = (BeltRole)r.ReadByte();
                int span = r.ReadInt32();
                int partner = r.ReadInt32();
                int curve = r.ReadInt32();
                var belt = new Belt(id, type, origin, facing, role, span) { Tier = tier, CurveIn = curve < 0 ? null : (Dir)curve };
                if (partner > 0)
                    partnerOf[id] = partner;
                bd = belt;
            }
            else
            {
                bd = type switch
                {
                    BuildingType.MineHead => MakeMine(id, origin, facing),
                    BuildingType.FreightDepot => new Depot(id, origin, facing),
                    BuildingType.ReceivingDock => new Dock(id, origin, facing),
                    BuildingType.ExpositionYard => new Yard(id, origin, facing),
                    _ when def.IsTrack => new Track(id, type, origin, facing),
                    _ when def.IsTerminal => new Terminal(id, type, origin, facing),
                    _ when def.IsSplitter => new Splitter(id, type, origin, facing, MakeLane),
                    _ when def.IsMachine => new Machine(id, type, origin, facing),
                    BuildingType.LoggingCamp => new LoggingCamp(id, origin, facing, 0),
                    BuildingType.PumpJack => MakePumpJack(id, origin, facing),
                    BuildingType.Waterwheel => new Waterwheel(id, origin, facing),
                    BuildingType.WaterPump => new WaterPump(id, origin, facing),
                    BuildingType.Boiler => new Boiler(id, origin, facing),
                    BuildingType.SteamPipe => new Pipe(id, origin, facing),
                    BuildingType.PowerStation => new PowerStation(id, origin, facing),
                    BuildingType.PowerPole => new Pole(id, origin, facing),
                    BuildingType.HydroDam => new Dam(id, origin, facing),
                    _ => throw new InvalidDataException($"cannot restore a {type}"),
                };
                switch (bd)
                {
                    case Splitter s: s.Read(r); break;
                    case Machine m: m.Read(r); break;
                    case Mine m: m.Read(r); break;
                    case LoggingCamp c: c.Read(r); break;
                    case PumpJack j: j.Read(r); break;
                    case Depot d: d.Read(r); break;
                    case Dock d: d.Read(r); break;
                    case Yard y: y.Read(r); break;
                    case Terminal t: t.Read(r); break;
                    case Boiler bo: bo.Read(r); break;
                }
            }
            Add(bd);
        }
        foreach (var (id, partner) in partnerOf)
            if (byId[id] is Belt a && byId.GetValueOrDefault(partner) is Belt p)
                a.Partner = p;
        nextLineId = r.ReadInt32();
        n = r.ReadInt32();
        lines.Clear();
        for (int k = 0; k < n; k++)
        {
            var line = new TransportLine(r.ReadInt32(), (BeltTier)r.ReadByte(), TileOutput.Instance);
            int tiles = r.ReadInt32();
            for (int t = 0; t < tiles; t++)
                if (byId[r.ReadInt32()] is Belt belt)
                    line.AddTile(belt);
            line.ReadGoods(r);
            lines.Add(line);
        }
        Haulage.Read(r);
        foreach (var b in buildings)
            if (b is Mine mine)
                mine.RefreshYield(this);
        // An rc1 save has no posted rate: the week in hand is charged at the rate of the market as loaded.
        PostedInterestPermille = WeeklyInterestPermille;
        ReadExtension(r);
        Power.Invalidate();
        TerrainVersion++;
        _ = version;
    }
}
