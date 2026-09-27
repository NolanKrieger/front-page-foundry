namespace FrontPageFoundry.Sim;

public abstract class Building
{
    protected Building(int id, BuildingType type, Cell origin, Dir facing)
    {
        Id = id;
        Type = type;
        Origin = origin;
        Facing = facing;
    }

    public int Id { get; }
    public BuildingType Type { get; internal set; }
    public BuildingDef Def => Catalog.Of(Type);
    /// <summary>Top-left cell of the footprint as it currently faces.</summary>
    public Cell Origin { get; internal set; }
    public Dir Facing { get; internal set; }

    public int Width => Catalog.Footprint(Type, Facing).Width;
    public int Height => Catalog.Footprint(Type, Facing).Height;

    public virtual IEnumerable<Cell> Cells
    {
        get
        {
            var (w, h) = Catalog.Footprint(Type, Facing);
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    yield return new Cell(Origin.X + dx, Origin.Y + dy);
        }
    }

    /// <summary>
    /// The cells just outside the front edge, left to right as the building faces. A single-port
    /// building uses the one right of centre (<c>count/2</c>); a 2×2 facing east therefore outputs
    /// from its south-east corner, as the mine head always has.
    /// </summary>
    public Cell[] OutputCells(int ports)
    {
        var (w, h) = Catalog.Footprint(Type, Facing);
        int n = Facing.IsHorizontal() ? h : w;
        var step = Facing.Clockwise().Offset();
        Cell start = Facing switch
        {
            Dir.East => new Cell(Origin.X + w, Origin.Y),
            Dir.North => new Cell(Origin.X, Origin.Y - 1),
            Dir.South => new Cell(Origin.X + w - 1, Origin.Y + h),
            _ => new Cell(Origin.X - 1, Origin.Y + h - 1),
        };
        var cells = new Cell[ports];
        if (ports == 1)
        {
            cells[0] = start + step * (n / 2);
            return cells;
        }
        for (int i = 0; i < ports; i++)
            cells[i] = start + step * Math.Min(i, n - 1);
        return cells;
    }

    /// <summary>
    /// Offers a good moving in <paramref name="travel"/> direction into cell <paramref name="into"/> of this building.
    /// <paramref name="entryPos"/> is where its centre lands in belt sub-units past the boundary (negative: still
    /// short of it, as a good arriving from a belt is when it transfers half a spacing early).
    /// </summary>
    internal virtual bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into) => false;

    internal virtual void Update(World world) { }

    /// <summary>Mixes this building's own state into the world hash.</summary>
    internal virtual void Hash(ref StateHasher h) { }
}

/// <summary>FNV-1a over everything that matters; equal hashes mean two runs did not diverge.</summary>
public struct StateHasher
{
    ulong h;

    public static StateHasher Start() => new() { h = 14695981039346656037UL };

    public void Mix(long v)
    {
        for (int i = 0; i < 8; i++)
        {
            h ^= (byte)(v >> (i * 8));
            h *= 1099511628211UL;
        }
    }

    public void Mix(double v) => Mix(BitConverter.DoubleToInt64Bits(v));

    public readonly ulong Value => h;
}
