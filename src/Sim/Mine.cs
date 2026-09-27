namespace FrontPageFoundry.Sim;

/// <summary>Raises ore from the seams under its footprint and pushes it out of its port.</summary>
public sealed partial class Mine : Building, IPowered
{
    /// <summary>Ticks per ore with a full footprint of fresh seams: one ore per second.</summary>
    public const int BaseTicksPerOre = 60;
    /// <summary>Patch richness W_d (GDD §9): how much ore until the seam's bonus fades.</summary>
    public const double Richness = 20_000;
    /// <summary>A worked-out seam still yields this fraction; it never runs dry.</summary>
    public const double FloorYield = 0.25;
    public const int BufferCap = 10;
    const long Unit = 1_000_000;

    long progress;

    public Mine(int id, Cell origin, Dir facing, int oreTiles, Item yields, Patch? patch) : base(id, BuildingType.MineHead, origin, facing)
    {
        OreTiles = oreTiles;
        Yields = yields;
        Patch = patch;
        Firebox = new Firebox(Def.DrawKw);
    }

    public int OreTiles { get; }
    /// <summary>The raw good under the footprint (the commonest seam, if mixed).</summary>
    public Item Yields { get; }
    /// <summary>The seam this head works; every head on it shares its depletion (GDD §9).</summary>
    public Patch? Patch { get; }
    public Firebox Firebox { get; }
    Firebox? IPowered.Firebox => Firebox;
    public int DrawKw => Def.DrawKw;
    public PowerNeed Need => PowerNeed.Any;
    public bool WantsPower => Buffered < BufferCap;
    public PowerSource Source { get; set; }
    public long Extracted { get; private set; }
    public int Buffered { get; private set; }
    public bool Lit => Firebox.Lit || Source is PowerSource.Shaft or PowerSource.Steam or PowerSource.Electric;

    /// <summary>The cell just outside the front-right of the mine, where ore comes out.</summary>
    public Cell OutputCell => OutputCells(1)[0];

    /// <summary>Output multiplier after <paramref name="extracted"/> ore from a patch of richness W_d: 0.25 + 0.75·e^(−W/W_d).</summary>
    public static double YieldFactor(long extracted, double richness = Richness) =>
        FloorYield + (1 - FloorYield) * Math.Exp(-extracted / richness);

    /// <summary>Current output as a fraction of a fresh, fully seated mine.</summary>
    public double OutputRate => OreTiles / 4.0 * yield;

    double yield = 1;
    /// <summary>The seam's W when <see cref="yield"/> was worked out. The yield is a pure function of W, recomputed
    /// whenever W moves, so it is neither saved nor hashed and a loaded company goes on exactly as it would have.</summary>
    long yieldAt = -1;

    internal void RefreshYield(World world)
    {
        long w = Patch == null ? Extracted : world.PatchExtracted(Patch.Id);
        if (w == yieldAt)
            return;
        yieldAt = w;
        yield = Patch == null ? YieldFactor(w, world.Richness(Richness)) : world.PatchYield(Patch);
    }

    /// <summary>Coal fed to a mine head goes to its firebox; a coal mine can feed itself from its own belt.</summary>
    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into) =>
        item == Item.Coal && Firebox.TryAddCoal();

    internal override void Update(World world)
    {
        int power = Buffered < BufferCap ? world.Power.Permille(this) : 0;
        if (power > 0)
        {
            RefreshYield(world);
            progress += (long)(Unit * OutputRate) * power / 1000;
            if (progress >= BaseTicksPerOre * Unit)
            {
                progress -= BaseTicksPerOre * Unit;
                Buffered++;
                Extracted++;
                if (Patch != null)
                    world.Extract(Patch.Id);
            }
        }

        if (Buffered > 0 && world.Offer(Yields, OutputCell, Facing, BeltTiers.Spacing / 2))
            Buffered--;
    }

    internal long Progress => progress;

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(progress);
        h.Mix(Buffered);
        h.Mix(Extracted);
        Firebox.Hash(ref h);
    }
}
