namespace FrontPageFoundry.Sim;

/// <summary>What runs on which network (GDD §4): trucks on roads, trains on rail, barges on the river.</summary>
public enum VehicleKind : byte { Truck, Train, Barge }

public enum VehicleState : byte
{
    /// <summary>Owned, no route: sits at a terminal until it is given two ends.</summary>
    Parked,
    /// <summary>At a terminal: unloading, loading, or waiting for something worth carrying.</summary>
    Loading,
    Moving,
    /// <summary>Stopped: nowhere to unload, no way through, or the block ahead is taken.</summary>
    Waiting,
}

public enum WaitReason : byte { None, Idle, NoRoute, Full, Signal }

/// <summary>A road or rail tile. Undirected: vehicles run either way. A rail tile may carry a block signal.</summary>
public sealed class Track : Building
{
    public Track(int id, BuildingType type, Cell origin, Dir facing) : base(id, type, origin, facing) { }

    public bool IsRoad => Type == BuildingType.Road;
    public bool IsRail => Type is BuildingType.Rail or BuildingType.RailSignal;
    /// <summary>A block boundary: a train passes only when the block beyond is free (GDD §4).</summary>
    public bool Signal => Type == BuildingType.RailSignal;

    /// <summary>Signals go on and off an existing rail tile without rebuilding it.</summary>
    internal void SetSignal(bool on) => Type = on ? BuildingType.RailSignal : BuildingType.Rail;
}

/// <summary>
/// Where vehicles call: truck depot, rail station or barge landing. Belts deliver into any edge; what
/// arrives waits in the yard for a vehicle, and what a vehicle brings leaves by the port belt (GDD §4).
/// </summary>
public sealed partial class Terminal : Building
{
    readonly List<Item> inbound = new(), outbound = new();

    public Terminal(int id, BuildingType type, Cell origin, Dir facing) : base(id, type, origin, facing) { }

    public VehicleKind Kind => KindOf(Type);

    public static VehicleKind KindOf(BuildingType type) => type switch
    {
        BuildingType.TruckDepot or BuildingType.MotorTruck or BuildingType.Road => VehicleKind.Truck,
        BuildingType.RailStation or BuildingType.Locomotive or BuildingType.Rail or BuildingType.RailSignal => VehicleKind.Train,
        _ => VehicleKind.Barge,
    };

    /// <summary>Goods the yard holds each way before belts back up or vehicles wait.</summary>
    public int Cap => Kind switch { VehicleKind.Truck => 48, VehicleKind.Train => 240, _ => 320 };
    /// <summary>Delivered by belt, waiting for a vehicle.</summary>
    public IReadOnlyList<Item> Inbound => inbound;
    /// <summary>Brought by vehicle, leaving by the port belt.</summary>
    public IReadOnlyList<Item> Outbound => outbound;
    public long Received { get; private set; }
    public long Shipped { get; private set; }
    public Cell PortCell => OutputCells(1)[0];

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        // A motor truck of your own make, driven into a depot, joins the fleet (GDD §4).
        if (item == Item.MotorTruck && Kind == VehicleKind.Truck)
        {
            world.Haulage.Enlist(BuildingType.MotorTruck, this);
            return true;
        }
        if (inbound.Count >= Cap)
            return false;
        inbound.Add(item);
        Received++;
        return true;
    }

    internal override void Update(World world)
    {
        if (outbound.Count > 0 && world.Offer(outbound[0], PortCell, Facing, BeltTiers.Spacing / 2))
        {
            outbound.RemoveAt(0);
            Shipped++;
        }
    }

    internal Item? TakeInbound()
    {
        if (inbound.Count == 0)
            return null;
        var item = inbound[0];
        inbound.RemoveAt(0);
        return item;
    }

    internal bool TryPutOutbound(Item item)
    {
        if (outbound.Count >= Cap)
            return false;
        outbound.Add(item);
        return true;
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(Received);
        h.Mix(Shipped);
        foreach (var i in inbound)
            h.Mix((long)i);
        h.Mix(-1);
        foreach (var i in outbound)
            h.Mix((long)i);
    }
}

/// <summary>Placeholder figures for the M14 balance pass: how much each vehicle carries and how fast it goes.</summary>
public static class Vehicles
{
    /// <summary>Belt sub-units per tick: trucks 3 tiles/s, trains 5, barges 1.25.</summary>
    public static int SpeedSu(VehicleKind kind) => kind switch { VehicleKind.Truck => 12, VehicleKind.Train => 20, _ => 5 };
    /// <summary>Trains run at 60% on hills (GDD §4).</summary>
    public const int HillPermille = 600;
    public static int Capacity(VehicleKind kind) => kind switch { VehicleKind.Truck => 24, VehicleKind.Train => 120, _ => 160 };
    /// <summary>Cells a vehicle's body covers along its path: a train is a locomotive and three cars.</summary>
    public static int Length(VehicleKind kind) => kind switch { VehicleKind.Train => 4, VehicleKind.Barge => 2, _ => 1 };
    /// <summary>Ticks spent at a terminal after loading before setting off.</summary>
    public const int DwellTicks = 60;
    /// <summary>A part load leaves after this long rather than waiting for a full one.</summary>
    public const int PartLoadTicks = 5 * World.TicksPerSecond;
    public const int RetryTicks = World.TicksPerSecond;
}

/// <summary>
/// A truck, train or barge. Runs A↔B for ever: at each end it unloads into the yard, loads what the
/// yard holds for the other end, and sets off when full, or after a short wait with a part load, or
/// when the far end has something waiting. Positions are integers along a cell path.
/// </summary>
public sealed partial class Vehicle
{
    readonly List<Item> cargo = new();
    internal List<Cell>? Path;
    int index, posSu, dwell, retry, pathVersion = -1;
    bool toB;
    /// <summary>The cargo aboard is for the terminal it is at (it has just arrived), not just loaded there.</summary>
    bool unloading;
    /// <summary>Between leaving one end and reaching the other (moving, or stopped on the way).</summary>
    public bool Underway { get; private set; }
    /// <summary>Trains: the rail blocks this train holds (its body, plus the one it is entering).</summary>
    internal readonly HashSet<int> Held = new();
    /// <summary>Trains: the block of every cell of <see cref="Path"/>, cut at <see cref="TrailVersion"/> (a cache).</summary>
    internal int[]? Trail;
    internal List<Cell>? TrailPath;
    internal int TrailVersion = -1;
    /// <summary>Network version at which a search from where it stands found no way on (a cache: not repeated until the network changes).</summary>
    int strandedVersion = -1;

    internal Vehicle(int id, BuildingType type, Cell at)
    {
        Id = id;
        Type = type;
        Kind = Terminal.KindOf(type);
        At = at;
        Capacity = Vehicles.Capacity(Kind);
    }

    public int Id { get; }
    public BuildingType Type { get; }
    public VehicleKind Kind { get; }
    public int Capacity { get; }
    /// <summary>The two terminals (origins) it runs between; null while parked.</summary>
    public Cell? A { get; internal set; }
    public Cell? B { get; internal set; }
    /// <summary>The terminal it is at or last left.</summary>
    public Cell At { get; internal set; }
    public Cell Destination => toB ? B ?? At : A ?? At;
    public VehicleState State { get; private set; } = VehicleState.Parked;
    public WaitReason Reason { get; private set; }
    public IReadOnlyList<Item> Cargo => cargo;
    public long Delivered { get; private set; }

    /// <summary>The cell the head is on, the next cell it is moving into, and how far between them in sub-units.</summary>
    public (Cell From, Cell To, int PosSu) Head
    {
        get
        {
            if (Path == null || Path.Count == 0)
                return (At, At, 0);
            if (!Underway)
            {
                var end = toB ? Path[0] : Path[^1];
                return (end, end, 0);
            }
            int i = Math.Min(index, Path.Count - 1);
            // The next cell in the direction of travel: toward A that is the one before on the stored A→B path
            // (it used to be the one after, so a vehicle heading home was drawn sliding backwards).
            return (Path[i], Path[Math.Clamp(i + (toB ? 1 : -1), 0, Path.Count - 1)], posSu);
        }
    }

    /// <summary>Cells the body covers, head first.</summary>
    public IEnumerable<Cell> BodyCells
    {
        get
        {
            if (Path == null || !Underway)
            {
                yield return Head.From;
                yield break;
            }
            int step = toB ? 1 : -1;
            for (int k = 0; k < Vehicles.Length(Kind); k++)
            {
                int i = index - step * k;
                if (i < 0 || i >= Path.Count)
                    yield break;
                yield return Path[i];
            }
        }
    }

    /// <summary>
    /// The cell a train is part-way into, whose block it took before moving off (null between cells). Moving
    /// takes the next cell's block first, and a step always ends exactly on a cell, so this is exactly "holds
    /// the block ahead" and a recut or a load can rebuild the hold from it.
    /// </summary>
    internal Cell? Entering
    {
        get
        {
            if (!Underway || Path == null || posSu <= 0)
                return null;
            int next = index + (toB ? 1 : -1);
            return next >= 0 && next < Path.Count ? Path[next] : null;
        }
    }

    /// <summary>Which way along the stored path it is going: the path is always kept A→B.</summary>
    internal bool TowardB => toB;
    internal int Index => index;

    internal void SetRoute(World world, Cell? a, Cell? b)
    {
        A = a;
        B = b;
        Path = null;
        pathVersion = -1;
        Underway = false;
        world.Haulage.Release(this);
        if (a == null || b == null)
        {
            State = VehicleState.Parked;
            Reason = WaitReason.None;
            return;
        }
        if (At != a && At != b)
            At = a.Value;
        toB = At == a;
        unloading = cargo.Count > 0;
        State = VehicleState.Loading;
        dwell = 0;
        retry = 0;
    }

    internal void Update(World world)
    {
        if (State == VehicleState.Parked)
            return;
        if (Underway)
            Move(world);
        else
            Call(world);
    }

    /// <summary>At a terminal (or waiting to leave one).</summary>
    void Call(World world)
    {
        var here = world.Haulage.TerminalOf(Kind, At);
        if (here == null)
        {
            Wait(WaitReason.NoRoute);
            return;
        }
        if (unloading)
        {
            while (cargo.Count > 0 && here.TryPutOutbound(cargo[0]))
            {
                cargo.RemoveAt(0);
                Delivered++;
            }
            if (cargo.Count > 0)
            {
                Wait(WaitReason.Full);
                return;
            }
            unloading = false;
        }
        while (cargo.Count < Capacity && here.TakeInbound() is { } item)
            cargo.Add(item);

        var far = world.Haulage.TerminalOf(Kind, Destination);
        if (far == null || Destination == At)
        {
            Wait(WaitReason.NoRoute);
            return;
        }
        dwell++;
        bool go = cargo.Count >= Capacity
                  || cargo.Count > 0 && dwell >= Vehicles.PartLoadTicks
                  || far.Inbound.Count > 0 && dwell >= Vehicles.DwellTicks;
        if (!go)
        {
            Wait(WaitReason.Idle);
            return;
        }
        if (--retry > 0)
            return;
        retry = Vehicles.RetryTicks;
        if (!EnsurePath(world))
        {
            Wait(WaitReason.NoRoute);
            return;
        }
        // Set off from this end: the path runs A→B, so leaving B walks it backwards.
        index = toB ? 0 : Path!.Count - 1;
        posSu = 0;
        if (Kind == VehicleKind.Train && !world.Haulage.TryEnter(this, index))
        {
            Wait(WaitReason.Signal);
            return;
        }
        State = VehicleState.Moving;
        Reason = WaitReason.None;
        Underway = true;
    }

    void Wait(WaitReason why)
    {
        State = why == WaitReason.Idle ? VehicleState.Loading : VehicleState.Waiting;
        Reason = why;
    }

    bool EnsurePath(World world)
    {
        int version = world.Haulage.Version(Kind);
        // Same network, same answer: a route that failed is not searched again every second (a barge cut off
        // by a dam would flood the endless river to the search limit each time).
        if (pathVersion == version)
            return Path != null;
        var a = world.Haulage.TerminalOf(Kind, A!.Value);
        var b = world.Haulage.TerminalOf(Kind, B!.Value);
        if (a == null || b == null)
            return false;
        Path = world.Haulage.FindPath(Kind, a, b);
        pathVersion = version;
        return Path != null;
    }

    void Move(World world)
    {
        var path = Path!;
        int step = toB ? 1 : -1;
        int last = toB ? path.Count - 1 : 0;
        if (index == last)
        {
            Arrive(world);
            return;
        }
        var next = path[index + step];
        if (!world.Haulage.Passable(Kind, next))
        {
            // The way ahead has gone: once a second, try a fresh path from here; else stand until it comes back.
            if (--retry > 0)
                return;
            retry = Vehicles.RetryTicks;
            int version = world.Haulage.Version(Kind);
            var far = world.Haulage.TerminalOf(Kind, Destination);
            var fresh = far == null || strandedVersion == version ? null : world.Haulage.FindPath(Kind, path[index], far);
            if (fresh == null)
            {
                strandedVersion = far == null ? -1 : version;
                State = VehicleState.Waiting;
                Reason = WaitReason.NoRoute;
                return;
            }
            // Splice the detour onto the way already travelled, so the stored path still runs A→B end to end
            // and the body keeps its cells. It is a detour, not the route: the next departure plans afresh
            // (a path from here alone made the next trip "arrive" at the far end from the middle of the road).
            if (toB)
            {
                var spliced = path.GetRange(0, index);
                spliced.AddRange(fresh);
                Path = spliced;
            }
            else
            {
                fresh.Reverse();
                int here = fresh.Count - 1;
                fresh.AddRange(path.GetRange(index + 1, path.Count - index - 1));
                Path = fresh;
                index = here;
            }
            pathVersion = -1;
            posSu = 0;
            if (Kind == VehicleKind.Train)
                world.Haulage.ReleaseBehind(this);
            return;
        }
        if (Kind == VehicleKind.Train && !world.Haulage.TryEnter(this, index + step))
        {
            State = VehicleState.Waiting;
            Reason = WaitReason.Signal;
            return;
        }
        State = VehicleState.Moving;
        Reason = WaitReason.None;
        int speed = Vehicles.SpeedSu(Kind);
        if (Kind == VehicleKind.Train && world.TileAt(next).Hill)
            speed = speed * Vehicles.HillPermille / 1000;
        posSu += speed;
        if (posSu >= BeltTiers.TileLength)
        {
            posSu -= BeltTiers.TileLength;
            index += step;
            if (Kind == VehicleKind.Train)
                world.Haulage.ReleaseBehind(this);
            if (index == last)
                Arrive(world);
        }
    }

    void Arrive(World world)
    {
        At = Destination;
        toB = !toB;
        Underway = false;
        unloading = true;
        State = VehicleState.Loading;
        Reason = WaitReason.None;
        dwell = 0;
        retry = 0;
        posSu = 0;
        if (Kind == VehicleKind.Train)
            world.Haulage.Release(this);
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(Id);
        h.Mix((long)Type);
        h.Mix(A.HasValue ? A.Value.X : int.MinValue);
        h.Mix(A.HasValue ? A.Value.Y : int.MinValue);
        h.Mix(B.HasValue ? B.Value.X : int.MinValue);
        h.Mix(B.HasValue ? B.Value.Y : int.MinValue);
        h.Mix(At.X);
        h.Mix(At.Y);
        h.Mix((long)State);
        h.Mix(Underway ? 1 : 0);
        h.Mix(unloading ? 1 : 0);
        h.Mix(index);
        h.Mix(posSu);
        h.Mix(toB ? 1 : 0);
        h.Mix(dwell);
        h.Mix(Delivered);
        foreach (var c in cargo)
            h.Mix((long)c);
    }
}

/// <summary>
/// The road, rail and river networks, the fleet, and the rail blocks (GDD §4). Paths are searched
/// breadth-first in a fixed neighbour order so the same layout always gives the same route.
/// </summary>
public sealed partial class Haulage
{
    public const int SearchLimit = 60_000;
    static readonly Dir[] Order = { Dir.North, Dir.East, Dir.South, Dir.West };
    /// <summary>Diagonal steps for barges, in a fixed order: north-east, south-east, south-west, north-west.</summary>
    static readonly Cell[] Corners = { new(1, -1), new(1, 1), new(-1, 1), new(-1, -1) };

    readonly World world;
    readonly List<Vehicle> vehicles = new();
    readonly Dictionary<int, Vehicle> byId = new();
    readonly int[] versions = new int[3];
    readonly Dictionary<Cell, int> blocks = new();
    readonly Dictionary<int, Vehicle> holders = new();
    int blocksVersion = -1, nextId = 1;

    internal Haulage(World world) => this.world = world;

    public IReadOnlyList<Vehicle> Fleet => vehicles;
    public Vehicle? VehicleById(int id) => byId.GetValueOrDefault(id);
    public IEnumerable<Vehicle> Calling(Terminal t) => vehicles.Where(v => v.A == t.Origin || v.B == t.Origin || v.State == VehicleState.Parked && v.At == t.Origin);

    /// <summary>Bumps when a network changes, so vehicles re-plan and the rail blocks are recut.</summary>
    public int Version(VehicleKind kind) => versions[(int)kind];
    internal void Changed(VehicleKind kind) => versions[(int)kind]++;
    internal void Changed(BuildingType type)
    {
        if (type is BuildingType.Road or BuildingType.TruckDepot)
            Changed(VehicleKind.Truck);
        else if (type is BuildingType.Rail or BuildingType.RailSignal or BuildingType.RailStation)
            Changed(VehicleKind.Train);
        else if (type is BuildingType.BargeLanding or BuildingType.HydroDam or BuildingType.Waterwheel or BuildingType.WaterPump)
            Changed(VehicleKind.Barge);
    }

    public Terminal? TerminalAt(Cell origin) => world.BuildingAt(origin) is Terminal t && t.Origin == origin ? t : null;

    /// <summary>The terminal at an origin if it serves this kind of vehicle (a depot rebuilt as a landing is not a truck's).</summary>
    public Terminal? TerminalOf(VehicleKind kind, Cell origin) => TerminalAt(origin) is { } t && t.Kind == kind ? t : null;

    public bool Passable(VehicleKind kind, Cell c) => kind switch
    {
        VehicleKind.Truck => world.BuildingAt(c) is Track { IsRoad: true },
        VehicleKind.Train => world.BuildingAt(c) is Track { IsRail: true },
        _ => world.TerrainAt(c) == Terrain.River && world.BuildingAt(c) == null,
    };

    /// <summary>Network cells touching a terminal's footprint, where its vehicles stand.</summary>
    public List<Cell> Gates(Terminal t)
    {
        var gates = new SortedSet<Cell>();
        foreach (var c in t.Cells)
            foreach (var d in Order)
            {
                var n = c + d.Offset();
                if (world.BuildingAt(n) != t && Passable(t.Kind, n))
                    gates.Add(n);
            }
        return gates.ToList();
    }

    public List<Cell>? FindPath(VehicleKind kind, Terminal from, Terminal to) => Search(kind, Gates(from), Gates(to));

    internal List<Cell>? FindPath(VehicleKind kind, Cell from, Terminal to) => Search(kind, new List<Cell> { from }, Gates(to));

    List<Cell>? Search(VehicleKind kind, List<Cell> sources, List<Cell> targets)
    {
        if (sources.Count == 0 || targets.Count == 0)
            return null;
        var goal = new HashSet<Cell>(targets);
        var prev = new Dictionary<Cell, Cell>();
        var queue = new Queue<Cell>();
        foreach (var s in sources)
        {
            if (!prev.TryAdd(s, s))
                continue;
            queue.Enqueue(s);
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (goal.Contains(c))
            {
                var path = new List<Cell>();
                for (var p = c; ; p = prev[p])
                {
                    path.Add(p);
                    if (prev[p] == p)
                        break;
                }
                path.Reverse();
                return path;
            }
            if (prev.Count > SearchLimit)
                return null;
            foreach (var d in Order)
            {
                var n = c + d.Offset();
                if (prev.ContainsKey(n) || !Passable(kind, n))
                    continue;
                prev[n] = c;
                queue.Enqueue(n);
            }
            if (kind != VehicleKind.Barge)
                continue;
            // A meandering river shifts a whole row between two columns in places, so its cells touch only
            // at a corner (every 30-40 tiles on the main river): a barge slips through such a corner. Only
            // where neither side cell is open water, so a stretch it could already follow keeps its path.
            foreach (var d in Corners)
            {
                var n = c + d;
                if (prev.ContainsKey(n) || !Passable(kind, n)
                    || Passable(kind, new Cell(n.X, c.Y)) || Passable(kind, new Cell(c.X, n.Y)))
                    continue;
                prev[n] = c;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    // ---- Fleet ---------------------------------------------------------------------------------

    internal Vehicle Buy(BuildingType type, Cell at, int? id = null)
    {
        if (id is { } wanted && !byId.ContainsKey(wanted))
            nextId = Math.Max(nextId, wanted + 1);
        else
            wanted = nextId++;
        var v = new Vehicle(wanted, type, at);
        vehicles.Add(v);
        byId[v.Id] = v;
        return v;
    }

    /// <summary>A built motor truck delivered to a depot: a vehicle of the fleet, parked there, at cost.</summary>
    internal void Enlist(BuildingType type, Terminal at)
    {
        Buy(type, at.Origin);
        world.Enlisted(type);
    }

    internal void Scrap(Vehicle v)
    {
        // Cargo at a terminal goes into its yard; cargo on the road is lost.
        if (!v.Underway && TerminalAt(v.At) is { } t)
            foreach (var item in v.Cargo)
                t.TryPutOutbound(item);
        Release(v);
        vehicles.Remove(v);
        byId.Remove(v.Id);
    }

    /// <summary>The parked vehicle of a type at either terminal, if any: the two-click route uses it before buying.</summary>
    public Vehicle? ParkedAt(BuildingType type, Cell a, Cell b) =>
        vehicles.FirstOrDefault(v => v.Type == type && v.State == VehicleState.Parked && (v.At == a || v.At == b));

    internal void Tick()
    {
        foreach (var v in vehicles)
            v.Update(world);
    }

    // ---- Rail blocks -----------------------------------------------------------------------------

    /// <summary>Block id of a rail cell: connected rail cut at signals. Signal cells belong to no block (−1).</summary>
    public int BlockOf(Cell c)
    {
        Recut();
        return blocks.GetValueOrDefault(c, -1);
    }

    public Vehicle? HolderOf(int block) => holders.GetValueOrDefault(block);

    void Recut()
    {
        int version = versions[(int)VehicleKind.Train];
        if (blocksVersion == version)
            return;
        blocksVersion = version;
        blocks.Clear();
        holders.Clear();
        int next = 0;
        // Flood from every rail tile in id order (creation order), cutting at signals.
        foreach (var b in world.Buildings)
        {
            if (b is not Track { IsRail: true, Signal: false } t || blocks.ContainsKey(t.Origin))
                continue;
            int id = next++;
            var stack = new Stack<Cell>();
            stack.Push(t.Origin);
            blocks[t.Origin] = id;
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var d in Order)
                {
                    var n = c + d.Offset();
                    if (blocks.ContainsKey(n) || world.BuildingAt(n) is not Track { IsRail: true, Signal: false })
                        continue;
                    blocks[n] = id;
                    stack.Push(n);
                }
            }
        }
        // Trains under way keep the blocks their bodies stand in, and the one they are part-way into (dropping
        // it let a second train into a block the first was already entering).
        foreach (var v in vehicles)
        {
            v.Held.Clear();
            if (v.Kind != VehicleKind.Train || !v.Underway || v.Path == null)
                continue;
            foreach (var c in v.BodyCells)
                Hold(v, c);
            if (v.Entering is { } ahead)
                Hold(v, ahead);
        }
    }

    void Hold(Vehicle v, Cell c)
    {
        int block = blocks.GetValueOrDefault(c, -1);
        if (block < 0)
            return;
        holders[block] = v;
        v.Held.Add(block);
    }

    /// <summary>
    /// Lets a train into the cell at <paramref name="at"/> on its path (setting off, or crossing into the next
    /// cell). Entering a block takes it, as before, but only while no other train holds that block or any block
    /// further along its way. Trains run A↔B, so two trains on one track always meet head-on: with the block
    /// ahead as the only test, each ended up holding the block the other wanted and both waited for ever at a
    /// signal. A train in a station holds nothing, so a train kept out waits there, or at its signal, until
    /// the way clears; one train to a block still holds.
    /// </summary>
    internal bool TryEnter(Vehicle v, int at)
    {
        Recut();
        var path = v.Path!;
        var trail = Trail(v);
        int block = trail[at];
        if (v.Underway && (block < 0 || holders.TryGetValue(block, out var mine) && mine == v))
            return true;     // a signal cell, or a block it already has
        int step = v.TowardB ? 1 : -1, end = v.TowardB ? path.Count : -1, seen = -1;
        for (int i = at; i != end; i += step)
        {
            int k = trail[i];
            if (k < 0 || k == seen)
                continue;
            seen = k;
            if (holders.TryGetValue(k, out var other) && other != v)
                return false;
        }
        if (block >= 0)
        {
            holders[block] = v;
            v.Held.Add(block);
        }
        return true;
    }

    /// <summary>The block of every cell of a train's path, recut only when the path or the blocks change.</summary>
    int[] Trail(Vehicle v)
    {
        if (v.Trail == null || v.TrailPath != v.Path || v.TrailVersion != blocksVersion)
        {
            var path = v.Path!;
            var trail = new int[path.Count];
            for (int i = 0; i < path.Count; i++)
                trail[i] = blocks.GetValueOrDefault(path[i], -1);
            v.Trail = trail;
            v.TrailPath = path;
            v.TrailVersion = blocksVersion;
        }
        return v.Trail;
    }

    /// <summary>After a step: blocks no part of the body stands in are given up.</summary>
    internal void ReleaseBehind(Vehicle v)
    {
        Recut();
        var standing = new HashSet<int>();
        foreach (var c in v.BodyCells)
            standing.Add(blocks.GetValueOrDefault(c, -1));
        foreach (int block in v.Held.ToList())
            if (!standing.Contains(block))
            {
                v.Held.Remove(block);
                if (holders.TryGetValue(block, out var h) && h == v)
                    holders.Remove(block);
            }
    }

    internal void Release(Vehicle v)
    {
        foreach (int block in v.Held)
            if (holders.TryGetValue(block, out var h) && h == v)
                holders.Remove(block);
        v.Held.Clear();
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(nextId);
        foreach (var v in vehicles)
            v.Hash(ref h);
    }
}
