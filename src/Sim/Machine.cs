namespace FrontPageFoundry.Sim;

public enum MachineState : byte { Starved, Working, Blocked, Unpowered }

/// <summary>
/// Coal power, era 1 (GDD §7): every "Any" machine has a small firebox fed by belt. One coal is
/// worth <see cref="KwsPerCoal"/> kilowatt-seconds, so a 5 kW mine head runs 24 s on a lump.
/// A new machine comes with a starter bag so the first works can light before the first coal mine.
/// </summary>
public sealed partial class Firebox
{
    public const int KwsPerCoal = 120;
    public const int Cap = 8;
    public const int StarterCoal = 30;

    public Firebox(int drawKw)
    {
        DrawKw = Math.Max(1, drawKw);
        Coal = StarterCoal;
    }

    public int DrawKw { get; }
    /// <summary>Lumps waiting to be burned.</summary>
    public int Coal { get; private set; }
    /// <summary>Ticks of running left in the lump now burning.</summary>
    public int BurnTicks { get; private set; }

    public int TicksPerCoal => KwsPerCoal * World.TicksPerSecond / DrawKw;
    public bool AcceptsCoal => Coal < Cap;
    public bool Lit => BurnTicks > 0 || Coal > 0;

    public bool TryAddCoal()
    {
        if (!AcceptsCoal)
            return false;
        Coal++;
        return true;
    }

    /// <summary>Burns one tick's worth while working; returns false when the fire is out.</summary>
    public bool Burn()
    {
        if (BurnTicks == 0)
        {
            if (Coal == 0)
                return false;
            Coal--;
            BurnTicks = TicksPerCoal;
        }
        BurnTicks--;
        return true;
    }

    /// <summary>The fire as it stands, for undo records.</summary>
    public FireState State => new(Coal, BurnTicks);

    internal void Restore(FireState fire)
    {
        Coal = fire.Coal;
        BurnTicks = (int)fire.Burn;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(Coal);
        h.Mix(BurnTicks);
    }
}

/// <summary>
/// A building's fire — a firebox's lumps and burn ticks, or a boiler's lumps and burnt kW·s — so that demolish + undo
/// (and undo + redo of a purchase) puts the fire back as it was instead of handing over a fresh starter bag.
/// Hook for the command layer: capture <see cref="Of"/> before a building is dropped, carry it on the undo record,
/// and call <see cref="Restore"/> right after the reversal re-creates the building.
/// </summary>
public readonly record struct FireState(int Coal, long Burn)
{
    /// <summary>The fire of a building that has one (machine, mine head, camp, jack or boiler), else null.</summary>
    public static FireState? Of(Building b) => b switch
    {
        Boiler boiler => boiler.Fire,
        IPowered { Firebox: { } firebox } => firebox.State,
        _ => null,
    };

    /// <summary>Puts a building's fire back as it was; does nothing for a building without one.</summary>
    public static void Restore(Building b, FireState fire)
    {
        if (b is Boiler boiler)
            boiler.Restore(fire);
        else if (b is IPowered { Firebox: { } firebox })
            firebox.Restore(fire);
    }
}

/// <summary>
/// One-job machine (GDD §5, §14): goods come in on any edge, the input set picks the recipe,
/// progress counts in ticks, finished goods leave by the front port(s). States: starved
/// (nothing to make), working, blocked (nowhere to put the output), unpowered (fire out).
/// </summary>
public sealed partial class Machine : Building, IPowered
{
    readonly int[] input = new int[Items.All.Length];
    readonly List<Item> output = new();
    readonly IReadOnlyList<Recipe> recipes;
    int nextRecipe;
    /// <summary>Progress through the current run, in thousandths of a tick (power shortage slows it).</summary>
    long progressMilli;

    public Machine(int id, BuildingType type, Cell origin, Dir facing) : base(id, type, origin, facing)
    {
        recipes = Recipes.Of(type);
        Firebox = Def.HasFirebox ? new Firebox(Def.DrawKw) : null;
    }

    public Firebox? Firebox { get; }
    public int DrawKw => Def.DrawKw;
    public PowerNeed Need => Def.Power;
    /// <summary>A run in hand: it draws power this tick.</summary>
    public bool WantsPower => Current != null;
    public PowerSource Source { get; set; }
    public Recipe? Current { get; private set; }
    public MachineState State { get; private set; } = MachineState.Starved;
    /// <summary>0..1 through the current run.</summary>
    public double Progress => Current == null ? 0 : progressMilli / (1000.0 * Current.CraftTicks);
    public long Runs { get; private set; }
    public int Stored(Item item) => input[(int)item];
    public IReadOnlyList<Item> Waiting => output;
    public IEnumerable<(Item Item, int Count)> Inputs => Items.All.Where(i => input[(int)i] > 0).Select(i => (i, input[(int)i]));

    /// <summary>Takes the next finished good off the output pile (tests and scripted logistics).</summary>
    internal Item? TakeOutput()
    {
        if (output.Count == 0)
            return null;
        var item = output[0];
        output.RemoveAt(0);
        return item;
    }

    /// <summary>The cell a finished good leaves into, per output port (GDD §3: ports turn with the building).</summary>
    public Cell PortCell(int port) => OutputCells(Recipes.PortCount(Type))[port];

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into)
    {
        bool wanted = false;
        foreach (var r in recipes)
            if (r.Needs(item))
            {
                wanted = true;
                break;
            }
        if (wanted && input[(int)item] < Recipes.InputCap(Type, item))
        {
            input[(int)item]++;
            return true;
        }
        return item == Item.Coal && Firebox != null && Firebox.TryAddCoal();
    }

    internal override void Update(World world)
    {
        // Finished goods leave first, one per tick, each by its recipe's port.
        if (output.Count > 0 && world.Offer(output[0], PortFor(output[0]), Facing, BeltTiers.Spacing / 2))
            output.RemoveAt(0);

        if (Current == null)
        {
            if (output.Count >= Recipes.OutputCap(Type))
            {
                State = MachineState.Blocked;
                return;
            }
            if (!StartNextRun())
            {
                State = MachineState.Starved;
                return;
            }
        }

        int power = PowerPermille(world);
        if (power == 0)
        {
            State = MachineState.Unpowered;
            return;
        }
        State = MachineState.Working;
        progressMilli += power;
        if (progressMilli >= 1000L * Current!.CraftTicks)
        {
            foreach (var (item, count) in Current.Outputs)
            {
                for (int k = 0; k < count; k++)
                    output.Add(item);
                world.Made(item, count);
            }
            Runs++;
            Current = null;
            progressMilli = 0;
        }
    }

    /// <summary>
    /// The good a starved machine is short of, for the flow overlay: the first missing input of a recipe that
    /// has something but not everything, looking from the job it last ran; failing that, that job's first input
    /// (a smelter fed iron ore wants iron ore, not the copper ore that is next in its rotation).
    /// </summary>
    public Item? Wants()
    {
        if (recipes.Count == 0)
            return null;
        // StartNextRun leaves nextRecipe one past the job it started, so the last job is derived from saved state.
        int last = Runs == 0 && Current == null ? 0 : (nextRecipe + recipes.Count - 1) % recipes.Count;
        for (int k = 0; k < recipes.Count; k++)
        {
            var r = recipes[(last + k) % recipes.Count];
            bool any = false;
            Item? missing = null;
            foreach (var (item, count) in r.Inputs)
            {
                if (input[(int)item] > 0)
                    any = true;
                if (input[(int)item] < count && missing == null)
                    missing = item;
            }
            if (any && missing != null)
                return missing;
        }
        var job = recipes[last];
        foreach (var (item, count) in job.Inputs)
            if (input[(int)item] < count)
                return item;
        return job.Inputs[0].Item;
    }

    /// <summary>Takes the next recipe whose inputs are all present, round-robin so a shop fed two jobs alternates.</summary>
    bool StartNextRun()
    {
        for (int k = 0; k < recipes.Count; k++)
        {
            var r = recipes[(nextRecipe + k) % recipes.Count];
            bool ready = true;
            foreach (var (item, count) in r.Inputs)
                if (input[(int)item] < count)
                {
                    ready = false;
                    break;
                }
            if (!ready)
                continue;
            foreach (var (item, count) in r.Inputs)
                input[(int)item] -= count;
            Current = r;
            nextRecipe = (nextRecipe + k + 1) % recipes.Count;
            return true;
        }
        return false;
    }

    /// <summary>Share of full speed this tick, in thousandths, from the best source that reaches the machine (GDD §7).</summary>
    int PowerPermille(World world) => world.Power.Permille(this);

    /// <summary>A good leaves by the port matching its place in its recipe's output list.</summary>
    Cell PortFor(Item item)
    {
        foreach (var r in recipes)
            for (int i = 0; i < r.Outputs.Length; i++)
                if (r.Outputs[i].Item == item)
                    return PortCell(i);
        return PortCell(0);
    }

    internal override void Hash(ref StateHasher h)
    {
        for (int i = 0; i < input.Length; i++)
            if (input[i] != 0)
            {
                h.Mix(i);
                h.Mix(input[i]);
            }
        foreach (var o in output)
            h.Mix((long)o);
        h.Mix(Current == null ? -1 : Array.IndexOf(Recipes.All, Current));
        h.Mix(progressMilli);
        h.Mix(nextRecipe);
        h.Mix(Runs);
        Firebox?.Hash(ref h);
    }
}
