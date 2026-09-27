namespace FrontPageFoundry.Sim;

/// <summary>Where a machine's power came from this tick.</summary>
public enum PowerSource : byte { None, Firebox, Shaft, Steam, Electric }

/// <summary>Anything that draws power: machines and mine heads.</summary>
public interface IPowered
{
    int Id { get; }
    int DrawKw { get; }
    PowerNeed Need { get; }
    Firebox? Firebox { get; }
    /// <summary>True when it has a run in hand and somewhere to put the result: it will draw this tick.</summary>
    bool WantsPower { get; }
    IEnumerable<Cell> Cells { get; }
    PowerSource Source { get; set; }
}

/// <summary>Waterwheel on the bank: shaft power to whatever touches it, and one machine beyond (GDD §7).</summary>
public sealed class Waterwheel : Building
{
    public const int Kw = 30;
    public Waterwheel(int id, Cell origin, Dir facing) : base(id, BuildingType.Waterwheel, origin, facing) { }
    public int SuppliedKw { get; internal set; }
    public int DemandKw { get; internal set; }
}

/// <summary>Pumps river water into the pipe it touches: enough for two boilers.</summary>
public sealed class WaterPump : Building
{
    public const int WaterUnits = 20;
    public WaterPump(int id, Cell origin, Dir facing) : base(id, BuildingType.WaterPump, origin, facing) { }
}

/// <summary>Water and coal make steam for the mains. Twice the work of a firebox from each lump.</summary>
public sealed partial class Boiler : Building
{
    public const int Kw = 120;
    public const int WaterUnits = 10;
    public const int CoalCap = 8;
    /// <summary>Kilowatt-seconds of steam from one lump: twice a firebox.</summary>
    public const int KwsPerCoal = 2 * Firebox.KwsPerCoal;

    long burnMilliKws;

    public Boiler(int id, Cell origin, Dir facing) : base(id, BuildingType.Boiler, origin, facing) => Coal = Firebox.StarterCoal;
    public int Coal { get; private set; }
    public bool Lit => Coal > 0;
    public int DeliveredKw { get; internal set; }

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        if (item != Item.Coal || Coal >= CoalCap)
            return false;
        Coal++;
        return true;
    }

    /// <summary>Burns coal in proportion to the steam actually drawn.</summary>
    internal void Burn(int deliveredKw)
    {
        DeliveredKw = deliveredKw;
        if (deliveredKw <= 0 || Coal == 0)
            return;
        burnMilliKws += deliveredKw * 1000L / World.TicksPerSecond;
        long perCoal = KwsPerCoal * 1000L;
        while (burnMilliKws >= perCoal && Coal > 0)
        {
            burnMilliKws -= perCoal;
            Coal--;
        }
    }

    /// <summary>The fire as it stands, for undo records (see <see cref="FireState"/>).</summary>
    public FireState Fire => new(Coal, burnMilliKws);

    internal void Restore(FireState fire)
    {
        Coal = fire.Coal;
        burnMilliKws = fire.Burn;
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(Coal);
        h.Mix(burnMilliKws);
    }
}

/// <summary>Carries steam and water between boilers, pumps, stations and machines.</summary>
public sealed class Pipe : Building
{
    public Pipe(int id, Cell origin, Dir facing) : base(id, BuildingType.SteamPipe, origin, facing) { }
}

/// <summary>Steam in, electricity out along the poles: three times a firebox's work per lump.</summary>
public sealed class PowerStation : Building
{
    /// <summary>Steam it can take.</summary>
    public const int IntakeKw = 200;
    /// <summary>Electricity per unit of steam.</summary>
    public const int GainMilli = 1500;
    public PowerStation(int id, Cell origin, Dir facing) : base(id, BuildingType.PowerStation, origin, facing) { }
    public int IntakeDemandKw { get; internal set; }
    public int OutputKw { get; internal set; }
}

/// <summary>Links poles and machines within six tiles.</summary>
public sealed class Pole : Building
{
    public const int Radius = 6;
    public Pole(int id, Cell origin, Dir facing) : base(id, BuildingType.PowerPole, origin, facing) { }
}

/// <summary>Across a river three wide or less: a great deal of power, and a lake behind it.</summary>
public sealed class Dam : Building
{
    public const int Kw = 400;
    public const int FloodLength = 30;
    public Dam(int id, Cell origin, Dir facing) : base(id, BuildingType.HydroDam, origin, facing) { }
    public int OutputKw { get; internal set; }
}

/// <summary>A pipe network: pumps, boilers, stations and the machines they reach.</summary>
public sealed class SteamNet
{
    public readonly List<Boiler> Boilers = new();
    public readonly List<WaterPump> Pumps = new();
    public readonly List<PowerStation> Stations = new();
    public readonly List<IPowered> Machines = new();
    public int SupplyKw { get; internal set; }
    public int DemandKw { get; internal set; }
    public int WaterMilli { get; internal set; }
    public int SatisfactionMilli { get; internal set; }
    /// <summary>Boilers with coal this tick: they share the steam drawn.</summary>
    internal int LitBoilers;
}

/// <summary>A pole network: stations, dams and the machines within reach.</summary>
public sealed class ElecNet
{
    public readonly List<Pole> Poles = new();
    public readonly List<PowerStation> Stations = new();
    public readonly List<Dam> Dams = new();
    public readonly List<IPowered> Machines = new();
    public int SupplyKw { get; internal set; }
    public int DemandKw { get; internal set; }
    public int SatisfactionMilli { get; internal set; }
    /// <summary>A dam, or a station whose main has steam: the grid can deliver this tick (GDD §16 fallback).</summary>
    public bool Live { get; internal set; }
}

/// <summary>
/// The power systems (GDD §7, §14): networks are graph components rebuilt only when the layout
/// changes; each tick every network gets satisfaction = supply ÷ demand and its machines run at that
/// fraction. A machine uses the best source it can reach: electric (a quarter faster) over steam over
/// shaft over its own firebox. Chemical inputs are not power.
/// </summary>
public sealed class PowerGrid
{
    readonly World world;
    readonly List<SteamNet> steamNets = new();
    readonly List<ElecNet> elecNets = new();
    readonly Dictionary<int, SteamNet> steamOf = new();
    readonly Dictionary<int, ElecNet> elecOf = new();
    readonly Dictionary<int, List<Waterwheel>> wheelsOf = new();
    readonly Dictionary<int, List<IPowered>> wheelLoads = new();
    readonly HashSet<int> dryWheels = new();
    bool dirty = true;

    public PowerGrid(World world) => this.world = world;

    public IReadOnlyList<SteamNet> SteamNets => steamNets;
    public IReadOnlyList<ElecNet> ElecNets => elecNets;
    public SteamNet? SteamNetOf(Building b) => steamOf.GetValueOrDefault(b.Id);
    public ElecNet? ElecNetOf(Building b) => elecOf.GetValueOrDefault(b.Id);
    public IReadOnlyList<Waterwheel> WheelsOf(IPowered m) => wheelsOf.GetValueOrDefault(m.Id) ?? (IReadOnlyList<Waterwheel>)Array.Empty<Waterwheel>();

    internal void Invalidate() => dirty = true;

    public static bool IsPowerPiece(BuildingType t) => t is BuildingType.Waterwheel or BuildingType.WaterPump or BuildingType.Boiler
        or BuildingType.SteamPipe or BuildingType.PowerStation or BuildingType.PowerPole or BuildingType.HydroDam;

    // ---- Topology ----------------------------------------------------------------------------

    void Rebuild()
    {
        dirty = false;
        steamNets.Clear();
        elecNets.Clear();
        steamOf.Clear();
        elecOf.Clear();
        wheelsOf.Clear();
        wheelLoads.Clear();
        dryWheels.Clear();

        var powered = world.Buildings.OfType<IPowered>().ToList();
        var byCell = new Dictionary<Cell, IPowered>();
        foreach (var m in powered)
            foreach (var c in m.Cells)
                byCell[c] = m;

        // Steam: pipes, boilers, pumps and stations conduct; machines hang off them.
        var seen = new HashSet<int>();
        foreach (var b in world.Buildings)
        {
            if (b is not (Pipe or Boiler or WaterPump or PowerStation) || !seen.Add(b.Id))
                continue;
            var net = new SteamNet();
            var stack = new Stack<Building>();
            stack.Push(b);
            var reached = new HashSet<int>();
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                switch (cur)
                {
                    case Boiler boiler: net.Boilers.Add(boiler); break;
                    case WaterPump pump when Wet(pump): net.Pumps.Add(pump); break;
                    case PowerStation st: net.Stations.Add(st); steamOf[st.Id] = net; break;
                }
                foreach (var c in cur.Cells)
                    for (int d = 0; d < 4; d++)
                    {
                        var n = c + ((Dir)d).Offset();
                        var nb = world.BuildingAt(n);
                        if (nb == null || nb == cur)
                            continue;
                        if (nb is Pipe or Boiler or WaterPump or PowerStation)
                        {
                            if (seen.Add(nb.Id))
                                stack.Push(nb);
                        }
                        else if (nb is IPowered m && m.Need != PowerNeed.None && reached.Add(nb.Id))
                        {
                            net.Machines.Add(m);
                            steamOf.TryAdd(nb.Id, net);
                        }
                    }
            }
            if (net.Boilers.Count + net.Pumps.Count + net.Stations.Count > 0)
                steamNets.Add(net);
        }

        // Electric: poles link within the radius. A station or dam within reach of several poles links them
        // too, so it feeds one grid however many pole lines leave it (and is never counted twice); machines
        // hang off the first grid that reaches them. Poles are bucketed by area, and only poles, stations,
        // dams and powered machines look for the poles near them, so belts cost nothing here.
        var poles = world.Buildings.OfType<Pole>().ToList();
        var buckets = new Dictionary<(int, int), List<Pole>>();
        var reach = new Dictionary<int, List<Building>>();
        foreach (var p in poles)
        {
            var key = (p.Origin.X >> BucketShift, p.Origin.Y >> BucketShift);
            (buckets.TryGetValue(key, out var list) ? list : buckets[key] = new()).Add(p);
            reach[p.Id] = new();
        }
        var polesAt = new Dictionary<int, List<Pole>>();
        if (poles.Count > 0)
            foreach (var b in world.Buildings)
            {
                if (b is not (Pole or PowerStation or Dam) && b is not IPowered { Need: not PowerNeed.None })
                    continue;
                var (w, h) = Catalog.Footprint(b.Type, b.Facing);
                int x0 = b.Origin.X, y0 = b.Origin.Y, x1 = x0 + w - 1, y1 = y0 + h - 1;
                for (int by = (y0 - Pole.Radius) >> BucketShift; by <= (y1 + Pole.Radius) >> BucketShift; by++)
                    for (int bx = (x0 - Pole.Radius) >> BucketShift; bx <= (x1 + Pole.Radius) >> BucketShift; bx++)
                    {
                        if (!buckets.TryGetValue((bx, by), out var near))
                            continue;
                        foreach (var p in near)
                        {
                            // The footprint cell nearest the pole decides whether it is within reach.
                            int dx = Math.Max(0, Math.Max(x0 - p.Origin.X, p.Origin.X - x1));
                            int dy = Math.Max(0, Math.Max(y0 - p.Origin.Y, p.Origin.Y - y1));
                            if (p == b || dx * dx + dy * dy > Pole.Radius * Pole.Radius)
                                continue;
                            reach[p.Id].Add(b);
                            if (b is PowerStation or Dam)
                                (polesAt.TryGetValue(b.Id, out var at) ? at : polesAt[b.Id] = new()).Add(p);
                        }
                    }
            }
        var poleSeen = new HashSet<int>();
        var sourceSeen = new HashSet<int>();
        foreach (var start in poles)
        {
            if (!poleSeen.Add(start.Id))
                continue;
            var net = new ElecNet();
            var stack = new Stack<Pole>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                net.Poles.Add(p);
                foreach (var b in reach[p.Id])
                {
                    if (b is Pole q)
                    {
                        if (poleSeen.Add(q.Id))
                            stack.Push(q);
                        continue;
                    }
                    if (b is not (PowerStation or Dam) || !sourceSeen.Add(b.Id))
                        continue;
                    if (b is PowerStation st)
                        net.Stations.Add(st);
                    else
                        net.Dams.Add((Dam)b);
                    elecOf[b.Id] = net;
                    foreach (var q2 in polesAt[b.Id])
                        if (poleSeen.Add(q2.Id))
                            stack.Push(q2);
                }
            }
            foreach (var p in net.Poles)
                foreach (var b in reach[p.Id])
                    if (b is IPowered m && m.Need != PowerNeed.None && elecOf.TryAdd(b.Id, net))
                        net.Machines.Add(m);
            net.Stations.Sort((a, b) => a.Id.CompareTo(b.Id));
            net.Dams.Sort((a, b) => a.Id.CompareTo(b.Id));
            net.Machines.Sort((a, b) => a.Id.CompareTo(b.Id));
            elecNets.Add(net);
        }

        // Shaft: a wheel drives what touches it, and one machine beyond each of those.
        foreach (var wheel in world.Buildings.OfType<Waterwheel>())
        {
            var loads = new List<IPowered>();
            if (!Wet(wheel))
            {
                // Its lake was drained: it stands on dry ground and drives nothing.
                dryWheels.Add(wheel.Id);
                wheelLoads[wheel.Id] = loads;
                continue;
            }
            var first = new HashSet<int>();
            foreach (var c in wheel.Cells)
                for (int d = 0; d < 4; d++)
                    if (byCell.TryGetValue(c + ((Dir)d).Offset(), out var m) && m.Need == PowerNeed.Any && first.Add(m.Id))
                        loads.Add(m);
            var chain = new List<IPowered>();
            foreach (var m in loads)
                foreach (var c in m.Cells)
                    for (int d = 0; d < 4; d++)
                        if (byCell.TryGetValue(c + ((Dir)d).Offset(), out var n) && n.Need == PowerNeed.Any && !first.Contains(n.Id) && first.Add(n.Id))
                            chain.Add(n);
            loads.AddRange(chain);
            wheelLoads[wheel.Id] = loads;
            foreach (var m in loads)
                (wheelsOf.TryGetValue(m.Id, out var list) ? list : wheelsOf[m.Id] = new()).Add(wheel);
        }
    }

    /// <summary>A wheel or pump works only while its footprint touches water: a drained lake leaves it dry.</summary>
    bool Wet(Building b)
    {
        foreach (var c in b.Cells)
            for (int d = 0; d < 4; d++)
                if (world.TerrainAt(c + ((Dir)d).Offset()) == Terrain.River)
                    return true;
        return false;
    }

    /// <summary>Poles are bucketed in 8×8 squares when grids are rebuilt.</summary>
    const int BucketShift = 3;

    // ---- Each tick -----------------------------------------------------------------------------

    /// <summary>Settles every network for this tick and stamps each consumer's source.</summary>
    internal void Evaluate()
    {
        if (dirty)
            Rebuild();
        int water = world.Market.WaterOutputMilli;

        // Steam on offer depends only on lit boilers and their water, so it is known before any demand.
        foreach (var net in steamNets)
        {
            int need = net.Boilers.Count * Boiler.WaterUnits;
            int have = net.Pumps.Count * WaterPump.WaterUnits;
            net.WaterMilli = need == 0 ? 1000 : Math.Min(1000, have * 1000 / need);
            int lit = 0;
            foreach (var b in net.Boilers)
                if (b.Lit)
                    lit++;
            net.LitBoilers = lit;
            net.SupplyKw = lit * Boiler.Kw * net.WaterMilli / 1000;
        }
        // A grid delivers if it has a dam or a station on a main with steam. One that cannot does not
        // count (GDD §16): its machines fall to the next source, which must then carry their load.
        foreach (var net in elecNets)
        {
            bool live = net.Dams.Count > 0;
            foreach (var st in net.Stations)
                live |= steamOf.GetValueOrDefault(st.Id) is { SupplyKw: > 0 };
            net.Live = live;
        }

        // Who takes what: electric first, then steam, then shaft, else the firebox.
        foreach (var net in elecNets)
        {
            net.DemandKw = 0;
            if (!net.Live)
                continue;
            foreach (var m in net.Machines)
            {
                m.Source = PowerSource.Electric;
                if (m.WantsPower)
                    net.DemandKw += m.DrawKw;
            }
        }
        foreach (var net in steamNets)
        {
            net.DemandKw = 0;
            if (net.SupplyKw == 0)
                continue;
            foreach (var m in net.Machines)
                if (steamOf[m.Id] == net && m.Need == PowerNeed.Any && !OnLiveGrid(m))
                {
                    m.Source = PowerSource.Steam;
                    if (m.WantsPower)
                        net.DemandKw += m.DrawKw;
                }
        }

        // Stations ask their mains for what their grid wants, capped.
        foreach (var net in elecNets)
        {
            int dams = net.Dams.Count * (Dam.Kw * water / 1000);
            int fromStations = Math.Max(0, net.DemandKw - dams);
            int perStation = net.Stations.Count == 0 ? 0 : (fromStations + net.Stations.Count - 1) / net.Stations.Count;
            foreach (var st in net.Stations)
            {
                st.IntakeDemandKw = Math.Min(PowerStation.IntakeKw, perStation * 1000 / PowerStation.GainMilli + 1);
                var steam = steamOf.GetValueOrDefault(st.Id);
                if (steam != null)
                    steam.DemandKw += st.IntakeDemandKw;
            }
        }

        foreach (var net in steamNets)
        {
            net.SatisfactionMilli = net.DemandKw == 0 ? 1000 : Math.Min(1000, net.SupplyKw * 1000 / net.DemandKw);
            int delivered = Math.Min(net.SupplyKw, net.DemandKw);
            int lit = net.LitBoilers;
            foreach (var b in net.Boilers)
                b.Burn(lit == 0 || !b.Lit ? 0 : delivered / lit);
        }

        foreach (var net in elecNets)
        {
            net.SupplyKw = 0;
            foreach (var dam in net.Dams)
                net.SupplyKw += dam.OutputKw = Dam.Kw * water / 1000;
            foreach (var st in net.Stations)
            {
                var steam = steamOf.GetValueOrDefault(st.Id);
                int got = steam == null ? 0 : st.IntakeDemandKw * steam.SatisfactionMilli / 1000;
                st.OutputKw = got * PowerStation.GainMilli / 1000;
                net.SupplyKw += st.OutputKw;
            }
            net.SatisfactionMilli = net.DemandKw == 0 ? 1000 : Math.Min(1000, net.SupplyKw * 1000 / net.DemandKw);
        }

        foreach (var (wheelId, loads) in wheelLoads)
        {
            var wheel = (Waterwheel)world.BuildingById(wheelId)!;
            wheel.SuppliedKw = dryWheels.Contains(wheelId) ? 0 : Waterwheel.Kw * water / 1000;
            wheel.DemandKw = 0;
            foreach (var m in loads)
                if (m.WantsPower && !OnLiveGrid(m) && !OnLiveMain(m))
                    wheel.DemandKw += m.DrawKw;
        }
    }

    bool OnLiveGrid(IPowered m) => elecOf.TryGetValue(m.Id, out var net) && net.Live;
    bool OnLiveMain(IPowered m) => m.Need == PowerNeed.Any && steamOf.TryGetValue(m.Id, out var net) && net.SupplyKw > 0;

    /// <summary>Power available to a consumer this tick, in thousandths of full speed, and where it came from.</summary>
    internal int Permille(IPowered m)
    {
        if (m.Need == PowerNeed.None)
            return 1000;
        // A network counts only while it actually delivers; otherwise the next source down takes over.
        if (elecOf.TryGetValue(m.Id, out var elec) && elec.SupplyKw > 0)
        {
            m.Source = PowerSource.Electric;
            return elec.SatisfactionMilli * 1250 / 1000;
        }
        if (m.Need == PowerNeed.Elec)
        {
            m.Source = PowerSource.None;
            return 0;
        }
        if (steamOf.TryGetValue(m.Id, out var steam) && steam.SupplyKw > 0)
        {
            m.Source = PowerSource.Steam;
            return steam.SatisfactionMilli;
        }
        if (wheelsOf.TryGetValue(m.Id, out var wheels))
        {
            // The best wheel's share: supply over what its loads want.
            int best = 0;
            foreach (var w in wheels)
                best = Math.Max(best, w.DemandKw == 0 ? 1000 : Math.Min(1000, w.SuppliedKw * 1000 / w.DemandKw));
            if (best > 0)
            {
                m.Source = PowerSource.Shaft;
                return best;
            }
        }
        if (m.Firebox != null && m.Firebox.Burn())
        {
            m.Source = PowerSource.Firebox;
            return 1000;
        }
        m.Source = PowerSource.None;
        return 0;
    }

    internal void Hash(ref StateHasher h)
    {
        foreach (var net in steamNets)
        {
            h.Mix(net.SupplyKw);
            h.Mix(net.DemandKw);
        }
        foreach (var net in elecNets)
        {
            h.Mix(net.SupplyKw);
            h.Mix(net.DemandKw);
        }
    }
}
