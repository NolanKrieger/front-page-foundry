namespace FrontPageFoundry.Sim;

/// <summary>
/// Carvell Falls (GDD §9): blocks of houses on both banks near the works. It adds blocks every few
/// days, faster when the company prospers, only on free ground, and never over anything built.
/// </summary>
public sealed partial class Town
{
    public const int Block = 3;
    public const int GrowEveryDays = 3;
    public static readonly Cell Centre = new(0, MapGen.MainRiverRow);

    readonly HashSet<Cell> tiles = new();
    readonly Pcg32 rng;
    long nextGrowthDay = GrowEveryDays;

    public Town(int seed) => rng = new Pcg32((ulong)seed, 53);

    public IReadOnlySet<Cell> Tiles => tiles;
    /// <summary>Bumps whenever the town changes, so the renderer knows to redraw.</summary>
    public int Version { get; private set; }
    public int Blocks { get; private set; }

    /// <summary>The first blocks: a close cluster on both banks, a street's width apart.</summary>
    internal void Found(World w)
    {
        for (int i = 0; i < 120 && Blocks < 16; i++)
        {
            int x = Centre.X - 11 + rng.NextInt(22);
            int y = Centre.Y - 10 + rng.NextInt(20);
            TryAddBlock(w, new Cell(x, y));
        }
    }

    /// <summary>
    /// Called daily: every few days, one block per $500 of trailing profit, at least one. Each goes on one of the
    /// three free spots a street's width beside the town that lie nearest the centre. Spots that can never take a
    /// block (the river runs through the centre, and the town's own blocks fill in) are passed over rather than
    /// counted, so the town keeps growing for as long as there is ground; and the spots are ordered by distance
    /// then position, never by the order the tiles happen to be stored in, so a loaded company grows the same way.
    /// </summary>
    internal void Grow(World w)
    {
        if (w.Day < nextGrowthDay)
            return;
        nextGrowthDay = w.Day + GrowEveryDays;
        int n = (int)Math.Clamp(1 + w.TrailingProfitCents / 50_000, 1, 4);
        var seen = new HashSet<Cell>();
        var spots = new List<Cell>();
        foreach (var t in tiles)
            for (int d = 0; d < 4; d++)
            {
                var c = t + ((Dir)d).Offset() * (Block + 1);
                if (!tiles.Contains(c) && seen.Add(c))
                    spots.Add(c);
            }
        spots.Sort((a, b) =>
        {
            int near = a.Manhattan(Centre).CompareTo(b.Manhattan(Centre));
            return near != 0 ? near : a.CompareTo(b);
        });
        var fits = new List<Cell>(3);
        for (int k = 0; k < n; k++)
        {
            fits.Clear();
            foreach (var c in spots)
            {
                if (fits.Count == 3)
                    break;
                if (CanAddBlock(w, c))
                    fits.Add(c);
            }
            if (fits.Count == 0)
                break;
            TryAddBlock(w, fits[rng.NextInt(fits.Count)]);
        }
    }

    /// <summary>A 3×3 block fits on free, dry ground: no town, building, plan, river, dam lake or seam under it.</summary>
    bool CanAddBlock(World w, Cell origin)
    {
        for (int dy = 0; dy < Block; dy++)
            for (int dx = 0; dx < Block; dx++)
            {
                var c = new Cell(origin.X + dx, origin.Y + dy);
                if (tiles.Contains(c) || w.BuildingAt(c) != null || w.Planned(c) || w.Flooded(c))
                    return false;
                var tile = w.Map.TileAt(c);
                if (tile.Kind == Terrain.River || tile.IsSeam)
                    return false;
            }
        return true;
    }

    bool TryAddBlock(World w, Cell origin)
    {
        if (!CanAddBlock(w, origin))
            return false;
        for (int dy = 0; dy < Block; dy++)
            for (int dx = 0; dx < Block; dx++)
                tiles.Add(new Cell(origin.X + dx, origin.Y + dy));
        Blocks++;
        Version++;
        return true;
    }

    /// <summary>Share of tiles within 8 that are town: the land-price driver.</summary>
    public double Density(Cell c)
    {
        int count = 0;
        for (int dy = -8; dy <= 8; dy++)
            for (int dx = -8; dx <= 8; dx++)
                if (tiles.Contains(new Cell(c.X + dx, c.Y + dy)))
                    count++;
        return count / 289.0;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Mix(Blocks);
        foreach (var c in tiles.OrderBy(c => c))
        {
            h.Mix(c.X);
            h.Mix(c.Y);
        }
        h.Mix((long)rng.State);
        h.Mix(nextGrowthDay);
    }
}
