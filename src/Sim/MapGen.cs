namespace FrontPageFoundry.Sim;

/// <summary>What a tile is. Ore kinds double as the raw good's seam.</summary>
public enum Terrain : byte { Ground, IronOre, CopperOre, Coal, Limestone, Sand, Clay, Sulfur, Bauxite, Forest, River, Town, OilSeep }

/// <summary>One ore patch: where, how big, what, and how rich (GDD §9).</summary>
public sealed record Patch(long Id, Cell Centre, int Radius, Terrain Kind, long Richness);

/// <summary>Everything the map says about a cell before the player touches it.</summary>
public readonly record struct Tile(Terrain Kind, bool Hill, Patch? Patch)
{
    public bool IsSeam => Kind >= Terrain.IronOre && Kind <= Terrain.Bauxite || Kind == Terrain.OilSeep;
}

/// <summary>
/// Deterministic infinite terrain from a seed (GDD §9): same seed, same map, any cell, any order.
/// Rivers run west to east in meandering corridors every <see cref="RiverSpacing"/> rows; elevation
/// noise with valleys along them makes hills; moisture noise makes forests; ore patches are dealt per
/// 32×32 chunk and get richer with distance from the works. The town is the world's (it grows).
/// </summary>
/// <summary>How much of the map to generate: everything, only the river, or bare ground (tests and benches).</summary>
public enum MapMode : byte { Full, RiverOnly, Flat }

public sealed class MapGen
{
    public const int ChunkSize = 32;
    public const int RiverSpacing = 320;
    /// <summary>The main river's centre row at the works.</summary>
    public const int MainRiverRow = 26;
    /// <summary>Richness of the starter iron seam, W_d (GDD §9).</summary>
    public const long StarterRichness = 20_000;

    /// <summary>Centre of the ore patch every new company starts beside.</summary>
    public static readonly Cell StarterPatch = new(6, -4);
    public static readonly Cell StarterCoal = new(-9, 4);
    public static readonly Cell StarterLimestone = new(18, 7);

    static readonly (Terrain Kind, int Weight)[] Kinds =
    {
        (Terrain.IronOre, 22), (Terrain.Coal, 20), (Terrain.CopperOre, 12), (Terrain.Limestone, 12),
        (Terrain.Sand, 9), (Terrain.Clay, 9), (Terrain.Sulfur, 8), (Terrain.Bauxite, 8), (Terrain.OilSeep, 6),
    };

    readonly int seed;
    readonly MapMode mode;
    /// <summary>The preset's patch richness W_d, in percent (GDD §13: Boom 150, Steady 100, Hard 70).</summary>
    readonly int richnessPercent;
    bool flat => mode != MapMode.Full;
    readonly Simplex elevation, moisture, meander, shape;
    readonly Dictionary<(int, int), Patch[]> patchCache = new();
    readonly Dictionary<(int, int), Tile[]> tileCache = new();
    readonly Patch[] starters;

    public MapGen(int seed, MapMode mode = MapMode.Full, int richnessPercent = 100)
    {
        this.seed = seed;
        this.mode = mode;
        this.richnessPercent = richnessPercent;
        elevation = new Simplex(seed * 7 + 1);
        moisture = new Simplex(seed * 7 + 2);
        meander = new Simplex(seed * 7 + 3);
        shape = new Simplex(seed * 7 + 4);
        starters = new[]
        {
            new Patch(-1, StarterPatch, 3, Terrain.IronOre, StarterRichness * richnessPercent / 100),
            new Patch(-2, StarterCoal, 3, Terrain.Coal, StarterRichness * richnessPercent / 100),
            new Patch(-3, StarterLimestone, 2, Terrain.Limestone, StarterRichness * richnessPercent / 100),
        };
    }

    public MapMode Mode => mode;

    /// <summary>The tile, from a per-chunk cache so panning and rebuilding never recompute the noise.</summary>
    public Tile TileAt(Cell c)
    {
        int cx = FloorDiv(c.X, ChunkSize), cy = FloorDiv(c.Y, ChunkSize);
        if (!tileCache.TryGetValue((cx, cy), out var tiles))
        {
            if (tileCache.Count > 4096)
                tileCache.Clear();
            tiles = new Tile[ChunkSize * ChunkSize];
            for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                    tiles[y * ChunkSize + x] = Generate(new Cell(cx * ChunkSize + x, cy * ChunkSize + y));
            tileCache[(cx, cy)] = tiles;
        }
        return tiles[(c.Y - cy * ChunkSize) * ChunkSize + (c.X - cx * ChunkSize)];
    }

    Tile Generate(Cell c)
    {
        if (RiverAt(c))
            return new Tile(Terrain.River, false, null);
        var patch = PatchAt(c);
        if (flat)
            return new Tile(patch?.Kind ?? Terrain.Ground, false, patch);
        bool hill = Elevation(c) > 0.34;
        if (patch != null)
            return new Tile(patch.Kind, hill, patch);
        if (Math.Abs(c.X) >= 20 || Math.Abs(c.Y) >= 14)
        {
            double m = moisture.At(c.X / 110.0 + 7.1, c.Y / 110.0 - 3.3) + 0.3 * moisture.At(c.X / 27.0, c.Y / 27.0);
            if (m > 0.42)
                return new Tile(Terrain.Forest, hill, null);
        }
        return new Tile(Terrain.Ground, hill, null);
    }

    public Terrain TerrainAt(Cell c) => TileAt(c).Kind;

    // ---- Rivers -------------------------------------------------------------------------------

    /// <summary>The corridor index whose river runs nearest this row.</summary>
    static int Corridor(int y) => (int)Math.Round((y - MainRiverRow) / (double)RiverSpacing);

    /// <summary>Centre row of a corridor's river at column x (meanders, so it varies).</summary>
    public double RiverCentre(int corridor, int x) =>
        corridor * RiverSpacing + MainRiverRow + 10 * meander.At(x / 90.0, corridor * 3.1) + 4 * meander.At(x / 25.0 + 50, corridor * 7.7);

    /// <summary>Half-width of the river at column x: it swells and narrows along its length; 0.5–1.45 covers one to three rows.</summary>
    public double RiverHalfWidth(int corridor, int x) => 0.5 + 0.95 * (0.5 + 0.5 * shape.At(x / 60.0, corridor * 5.3 + 9));

    public bool RiverAt(Cell c)
    {
        if (mode == MapMode.Flat)
            return false;
        int k = Corridor(c.Y);
        double centre = RiverCentre(k, c.X);
        return Math.Abs(c.Y + 0.5 - centre) <= RiverHalfWidth(k, c.X);
    }

    /// <summary>Signed distance in rows from the nearest river centre (negative = north bank side).</summary>
    public double RiverOffset(Cell c)
    {
        int k = Corridor(c.Y);
        return c.Y + 0.5 - RiverCentre(k, c.X);
    }

    // ---- Elevation ---------------------------------------------------------------------------

    /// <summary>Roughly −1..1, with a valley scooped out along each river so rivers sit low.</summary>
    public double Elevation(Cell c)
    {
        double e = elevation.Fbm2(c.X / 140.0, c.Y / 140.0);
        double d = RiverOffset(c);
        return e - 0.6 * Math.Exp(-(d * d) / (2 * 26.0 * 26.0));
    }

    // ---- Ore patches ---------------------------------------------------------------------------

    /// <summary>The patch this cell belongs to; where patches overlap, the one whose centre is nearest for its size.</summary>
    public Patch? PatchAt(Cell c)
    {
        foreach (var p in starters)
            if (Inside(p, c))
                return p;
        if (flat || (Math.Abs(c.X) < 24 && Math.Abs(c.Y) < 16))
            return null;
        int cx = FloorDiv(c.X, ChunkSize), cy = FloorDiv(c.Y, ChunkSize);
        Patch? best = null;
        double bestScore = double.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                foreach (var p in PatchesIn(cx + dx, cy + dy))
                {
                    if (!Inside(p, c))
                        continue;
                    int ox = c.X - p.Centre.X, oy = c.Y - p.Centre.Y;
                    double score = (ox * ox + oy * oy) / (double)(p.Radius * p.Radius);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }
        return best;
    }

    bool Inside(Patch p, Cell c)
    {
        int dx = c.X - p.Centre.X, dy = c.Y - p.Centre.Y;
        double r = p.Radius * (1 + 0.35 * shape.At(c.X / 4.0 + p.Id * 0.13, c.Y / 4.0 - p.Id * 0.29));
        return dx * dx + dy * dy <= r * r + 0.5;
    }

    /// <summary>The patches dealt to a chunk, cached: none, one or two, never on a river or near the works.</summary>
    public Patch[] PatchesIn(int cx, int cy)
    {
        if (patchCache.TryGetValue((cx, cy), out var cached))
            return cached;
        var rng = new Pcg32((ulong)(seed * 1_000_003L + cx * 73_856_093L + cy * 19_349_663L), 41);
        int roll = rng.NextInt(100);
        int count = roll < 45 ? 0 : roll < 85 ? 1 : 2;
        var list = new List<Patch>(count);
        for (int i = 0; i < count; i++)
        {
            var centre = new Cell(cx * ChunkSize + 4 + rng.NextInt(ChunkSize - 8), cy * ChunkSize + 4 + rng.NextInt(ChunkSize - 8));
            int radius = 3 + rng.NextInt(3);
            if (Math.Abs(centre.X) < 32 && Math.Abs(centre.Y) < 24)
                continue;
            if (RiverAt(centre))
                continue;
            int pick = rng.NextInt(106);
            var kind = Terrain.IronOre;
            int acc = 0;
            foreach (var (k, w) in Kinds)
            {
                acc += w;
                if (pick < acc)
                {
                    kind = k;
                    break;
                }
            }
            double distance = Math.Sqrt((double)centre.X * centre.X + (double)centre.Y * centre.Y);
            long richness = (long)(StarterRichness * (1 + distance / 2000.0)) * richnessPercent / 100;
            long id = ((long)cx << 34) ^ ((long)(cy & 0xFFFF) << 18) ^ ((long)i << 8) ^ 0x1000;
            list.Add(new Patch(id, centre, radius, kind, richness));
        }
        cached = list.ToArray();
        patchCache[(cx, cy)] = cached;
        return cached;
    }

    static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
