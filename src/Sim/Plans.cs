namespace FrontPageFoundry.Sim;

/// <summary>
/// A pencil ghost (GDD §3 planning mode): a building drawn on the map for nothing. It holds its ground
/// against the town and is built for real, oldest first, from cash on hand, whenever planning mode is
/// off. A sorting filter or dock order drawn with it is set the moment it is built.
/// </summary>
public sealed record Plan(int Id, BuildingType Type, Cell Origin, Dir Facing, int Span, Item? Setting)
{
    public IEnumerable<Cell> Cells => Catalog.Of(Type).IsBridge
        ? new[] { Origin, Origin + Facing.Offset() * (Span + 1) }
        : World.FootprintCells(Type, Origin, Facing);
}

/// <summary>One building of a blueprint, placed relative to the blueprint's top-left corner.</summary>
public sealed record BlueprintPiece(BuildingType Type, Cell Offset, Dir Facing, int Span, Item? Setting);

/// <summary>
/// Blueprints (GDD §3): copy what stands (and what is planned) inside a rectangle, turn it, and stamp
/// it anywhere as plans. Pure rules, so the same stamp always drafts the same pieces.
/// </summary>
public static class Blueprint
{
    /// <summary>Every building or plan whose origin lies in the rectangle (both corners inclusive), relative to its top-left.</summary>
    public static List<BlueprintPiece> Capture(World w, Cell a, Cell b)
    {
        var min = new Cell(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        var max = new Cell(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var pieces = new List<BlueprintPiece>();
        foreach (var building in w.Buildings)
        {
            if (building is Belt { Role: BeltRole.BridgeExit })
                continue;
            if (!Inside(building.Origin, min, max))
                continue;
            int span = building is Belt { Role: BeltRole.BridgeEntry } entry ? entry.Span : 0;
            Item? setting = building switch
            {
                Splitter { Sorting: true, Filter: { } f } => f,
                Dock { Order: { } o } => o,
                _ => null,
            };
            pieces.Add(new BlueprintPiece(building.Type, building.Origin - min, building.Facing, span, setting));
        }
        foreach (var plan in w.Plans)
            if (Inside(plan.Origin, min, max))
                pieces.Add(new BlueprintPiece(plan.Type, plan.Origin - min, plan.Facing, plan.Span, plan.Setting));
        pieces.Sort((p, q) => p.Offset.CompareTo(q.Offset));
        return Normalize(pieces);
    }

    static bool Inside(Cell c, Cell min, Cell max) => c.X >= min.X && c.X <= max.X && c.Y >= min.Y && c.Y <= max.Y;

    /// <summary>The blueprint turned a quarter clockwise, so what pointed east points south.</summary>
    public static List<BlueprintPiece> Rotate(IReadOnlyList<BlueprintPiece> pieces)
    {
        var turned = new List<BlueprintPiece>(pieces.Count);
        foreach (var p in pieces)
        {
            var facing = p.Facing.Clockwise();
            Cell origin;
            if (Catalog.Of(p.Type).IsBridge)
            {
                origin = Turn(p.Offset);
            }
            else
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                foreach (var c in World.FootprintCells(p.Type, p.Offset, p.Facing))
                {
                    var t = Turn(c);
                    minX = Math.Min(minX, t.X);
                    minY = Math.Min(minY, t.Y);
                }
                origin = new Cell(minX, minY);
            }
            turned.Add(p with { Offset = origin, Facing = facing });
        }
        turned.Sort((p, q) => p.Offset.CompareTo(q.Offset));
        return Normalize(turned);
    }

    /// <summary>Clockwise quarter turn on a grid whose +Y points down: (x, y) → (−y, x).</summary>
    static Cell Turn(Cell c) => new(-c.Y, c.X);

    /// <summary>Shifts the pieces so the smallest offset is (0,0).</summary>
    static List<BlueprintPiece> Normalize(List<BlueprintPiece> pieces)
    {
        if (pieces.Count == 0)
            return pieces;
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (var p in pieces)
            foreach (var c in Cells(p))
            {
                minX = Math.Min(minX, c.X);
                minY = Math.Min(minY, c.Y);
            }
        var shift = new Cell(minX, minY);
        for (int i = 0; i < pieces.Count; i++)
            pieces[i] = pieces[i] with { Offset = pieces[i].Offset - shift };
        return pieces;
    }

    public static IEnumerable<Cell> Cells(BlueprintPiece p) => Catalog.Of(p.Type).IsBridge
        ? new[] { p.Offset, p.Offset + p.Facing.Offset() * (p.Span + 1) }
        : World.FootprintCells(p.Type, p.Offset, p.Facing);

    /// <summary>Width and height of the stamp.</summary>
    public static (int Width, int Height) Size(IReadOnlyList<BlueprintPiece> pieces)
    {
        int w = 0, h = 0;
        foreach (var p in pieces)
            foreach (var c in Cells(p))
            {
                w = Math.Max(w, c.X + 1);
                h = Math.Max(h, c.Y + 1);
            }
        return (w, h);
    }

    /// <summary>What building every piece would cost right now, at today's prices, land included.</summary>
    public static long CostCents(World w, IReadOnlyList<BlueprintPiece> pieces, Cell at)
    {
        long total = 0;
        foreach (var p in pieces)
            total += w.PurchaseCents(p.Type, at + p.Offset, p.Facing, p.Span);
        return total;
    }

    /// <summary>Drafts every piece that fits as one undo step; returns how many went down.</summary>
    public static int Stamp(World w, IReadOnlyList<BlueprintPiece> pieces, Cell at)
    {
        int drafted = 0;
        foreach (var p in pieces)
            if (w.Apply(new Draft(p.Type, at + p.Offset, p.Facing, p.Span, p.Setting), joinPrevious: drafted > 0) == PlaceResult.Ok)
                drafted++;
        return drafted;
    }
}
