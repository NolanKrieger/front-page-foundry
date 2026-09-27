namespace FrontPageFoundry.Sim;

/// <summary>Fells the wood within three tiles: the more trees in reach, the faster the timber (up to one a second at twelve).</summary>
public sealed partial class LoggingCamp : Building, IPowered
{
    public const int Radius = 3;
    public const int FullTrees = 12;
    public const int BufferCap = 10;
    const long Unit = 1_000_000;

    long progress;

    public LoggingCamp(int id, Cell origin, Dir facing, int trees) : base(id, BuildingType.LoggingCamp, origin, facing)
    {
        Trees = trees;
        Firebox = new Firebox(Def.DrawKw);
    }

    public int Trees { get; internal set; }
    public Firebox Firebox { get; }
    Firebox? IPowered.Firebox => Firebox;
    public int DrawKw => Def.DrawKw;
    public PowerNeed Need => PowerNeed.Any;
    public bool WantsPower => Buffered < BufferCap && Trees > 0;
    public PowerSource Source { get; set; }
    public long Felled { get; private set; }
    public int Buffered { get; private set; }
    public double OutputRate => Math.Min(1.0, Trees / (double)FullTrees);
    public Cell OutputCell => OutputCells(1)[0];

    /// <summary>Forest tiles within reach of a camp at this footprint, not counting the ground it stands on (felled to build it).</summary>
    public static int TreesAround(World world, Cell origin, Dir facing)
    {
        int count = 0;
        var (w, h) = Catalog.Footprint(BuildingType.LoggingCamp, facing);
        for (int y = origin.Y - Radius; y < origin.Y + h + Radius; y++)
            for (int x = origin.X - Radius; x < origin.X + w + Radius; x++)
            {
                bool under = x >= origin.X && x < origin.X + w && y >= origin.Y && y < origin.Y + h;
                if (!under && world.TerrainAt(new Cell(x, y)) == Terrain.Forest)
                    count++;
            }
        return count;
    }

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into) =>
        item == Item.Coal && Firebox.TryAddCoal();

    internal override void Update(World world)
    {
        if ((world.TickCount & 255) == 0)
            Trees = TreesAround(world, Origin, Facing);
        int power = WantsPower ? world.Power.Permille(this) : 0;
        if (power > 0)
        {
            progress += (long)(Unit * OutputRate) * power / 1000;
            if (progress >= Mine.BaseTicksPerOre * Unit)
            {
                progress -= Mine.BaseTicksPerOre * Unit;
                Buffered++;
                Felled++;
            }
        }
        if (Buffered > 0 && world.Offer(Item.Timber, OutputCell, Facing, BeltTiers.Spacing / 2))
            Buffered--;
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(progress);
        h.Mix(Buffered);
        h.Mix(Felled);
        h.Mix(Trees);
        Firebox.Hash(ref h);
    }
}

/// <summary>Nods over an oil seep and brings up crude by the barrel; the seep is a patch and slows like any other.</summary>
public sealed partial class PumpJack : Building, IPowered
{
    public const int BufferCap = 10;
    const long Unit = 1_000_000;

    long progress;
    double yield = 1;
    /// <summary>The seep's W when <see cref="yield"/> was worked out (see <see cref="Mine"/>): derived, so not saved.</summary>
    long yieldAt = -1;

    public PumpJack(int id, Cell origin, Dir facing, int seepTiles, Patch? patch) : base(id, BuildingType.PumpJack, origin, facing)
    {
        SeepTiles = seepTiles;
        Patch = patch;
        Firebox = new Firebox(Def.DrawKw);
    }

    public int SeepTiles { get; }
    public Patch? Patch { get; }
    public Firebox Firebox { get; }
    Firebox? IPowered.Firebox => Firebox;
    public int DrawKw => Def.DrawKw;
    public PowerNeed Need => PowerNeed.Any;
    public bool WantsPower => Buffered < BufferCap;
    public PowerSource Source { get; set; }
    public long Pumped { get; private set; }
    public int Buffered { get; private set; }
    public double OutputRate => SeepTiles / 4.0 * yield;
    public Cell OutputCell => OutputCells(1)[0];

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into) =>
        item == Item.Coal && Firebox.TryAddCoal();

    internal override void Update(World world)
    {
        int power = WantsPower ? world.Power.Permille(this) : 0;
        if (power > 0)
        {
            if (Patch != null && world.PatchExtracted(Patch.Id) is var w && w != yieldAt)
            {
                yieldAt = w;
                yield = world.PatchYield(Patch);
            }
            progress += (long)(Unit * OutputRate) * power / 1000;
            if (progress >= Mine.BaseTicksPerOre * Unit)
            {
                progress -= Mine.BaseTicksPerOre * Unit;
                Buffered++;
                Pumped++;
                if (Patch != null)
                    world.Extract(Patch.Id);
            }
        }
        if (Buffered > 0 && world.Offer(Item.CrudeOil, OutputCell, Facing, BeltTiers.Spacing / 2))
            Buffered--;
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(progress);
        h.Mix(Buffered);
        h.Mix(Pumped);
        Firebox.Hash(ref h);
    }
}
