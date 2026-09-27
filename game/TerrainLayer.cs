using System.Collections.Generic;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// Draws the ground in 16×16 chunks. Godot caches each chunk's strokes until it is freed,
/// so an infinite map costs only what is on screen. Chunks redraw when the ground changes
/// (the town grows, wood is cleared).
/// </summary>
public partial class TerrainLayer : Node2D
{
    const int ChunkCells = 16;
    const int ChunkPx = ChunkCells * Ink.Tile;
    const int ChunksPerFrame = 4;

    readonly Dictionary<Vector2I, TerrainChunk> chunks = new();
    World world = null!;
    int version;

    public void Init(World world) => this.world = world;

    public override void _Ready() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Process(double delta)
    {
        if (world.TerrainVersion != version)
        {
            version = world.TerrainVersion;
            foreach (var chunk in chunks.Values)
                chunk.QueueRedraw();
        }

        var view = VisibleWorldRect();
        var min = new Vector2I(Mathf.FloorToInt(view.Position.X / ChunkPx) - 1, Mathf.FloorToInt(view.Position.Y / ChunkPx) - 1);
        var max = new Vector2I(Mathf.FloorToInt(view.End.X / ChunkPx) + 1, Mathf.FloorToInt(view.End.Y / ChunkPx) + 1);

        // New chunks nearest the centre first, a few per frame, so a fast pan never stalls a frame.
        var centre = new Vector2((min.X + max.X) / 2f, (min.Y + max.Y) / 2f);
        List<Vector2I>? wanted = null;
        for (int cy = min.Y; cy <= max.Y; cy++)
            for (int cx = min.X; cx <= max.X; cx++)
            {
                var key = new Vector2I(cx, cy);
                if (!chunks.ContainsKey(key))
                    (wanted ??= new()).Add(key);
            }
        if (wanted != null)
        {
            wanted.Sort((a, b) => ((Vector2)a).DistanceSquaredTo(centre).CompareTo(((Vector2)b).DistanceSquaredTo(centre)));
            int budget = ChunksPerFrame;
            foreach (var key in wanted)
            {
                if (budget-- == 0)
                    break;
                var chunk = new TerrainChunk(world, key, ChunkCells) { Position = new Vector2(key.X, key.Y) * ChunkPx };
                chunks[key] = chunk;
                AddChild(chunk);
                ChunksDrawn++;
            }
        }

        // Free chunks well off screen.
        var stale = new List<Vector2I>();
        foreach (var key in chunks.Keys)
            if (key.X < min.X - 2 || key.X > max.X + 2 || key.Y < min.Y - 2 || key.Y > max.Y + 2)
                stale.Add(key);
        foreach (var key in stale)
        {
            chunks[key].QueueFree();
            chunks.Remove(key);
        }
    }

    public int LoadedChunks => chunks.Count;
    /// <summary>Chunks generated since start, for the pan benchmark.</summary>
    public int ChunksDrawn { get; private set; }

    Rect2 VisibleWorldRect()
    {
        var inv = GetCanvasTransform().AffineInverse();
        var size = GetViewportRect().Size;
        var a = inv * Vector2.Zero;
        var b = inv * size;
        return new Rect2(a, b - a);
    }
}

/// <summary>
/// One chunk of engraved ground: seams, forest, hill hatching, the river, the town. Every stroke of a
/// kind is gathered into one multiline call, so a chunk costs a dozen draw commands however busy it is.
/// </summary>
public partial class TerrainChunk : Node2D
{
    readonly World world;
    readonly Vector2I key;
    readonly int cells;

    // Segment lists, flushed as one DrawMultiline each.
    readonly List<Vector2> hatch = new(), waves = new(), banks = new(), trees = new(), trunks = new(), marks = new(), heavyMarks = new(), townLines = new();
    readonly List<(Vector2 P, float R, Color C)> dots = new();

    public TerrainChunk(World world, Vector2I key, int cells)
    {
        this.world = world;
        this.key = key;
        this.cells = cells;
    }

    public TerrainChunk() : this(null!, default, 0) { }

    public override void _Draw()
    {
        int t = Ink.Tile;
        var ore = Art.Terrain("iron_ore");
        foreach (var list in new[] { hatch, waves, banks, trees, trunks, marks, heavyMarks, townLines })
            list.Clear();
        dots.Clear();

        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                int wx = key.X * cells + x, wy = key.Y * cells + y;
                var cell = new Cell(wx, wy);
                var tile = world.TileAt(cell);
                var r = new Rect2(x * t, y * t, t, t);
                if (tile.Hill && tile.Kind != Terrain.River)
                    HillStrokes(r, cell);
                switch (tile.Kind)
                {
                    case Terrain.River: RiverStrokes(r, cell); break;
                    case Terrain.Forest: ForestStrokes(r, wx, wy); break;
                    case Terrain.Town: TownStrokes(r, cell); break;
                    case Terrain.Ground: break;
                    default: SeamStrokes(r, tile.Kind, wx, wy, ore); break;
                }
            }

        if (hatch.Count > 0) DrawMultiline(hatch.ToArray(), Ink.Faint with { A = 0.28f }, 1f);
        if (waves.Count > 0) DrawMultiline(waves.ToArray(), Ink.Soft with { A = 0.5f }, 1f);
        if (banks.Count > 0) DrawMultiline(banks.ToArray(), Ink.Black, 2f);
        if (trunks.Count > 0) DrawMultiline(trunks.ToArray(), Ink.Black, 1.5f);
        if (trees.Count > 0) DrawMultiline(trees.ToArray(), Ink.Black, 1.2f);
        if (marks.Count > 0) DrawMultiline(marks.ToArray(), Ink.Soft, 1.2f);
        if (heavyMarks.Count > 0) DrawMultiline(heavyMarks.ToArray(), Ink.Black, 3f);
        if (townLines.Count > 0) DrawMultiline(townLines.ToArray(), Ink.Black, 1.5f);
        foreach (var (p, rad, c) in dots)
            DrawCircle(p, rad, c);
    }

    static void Seg(List<Vector2> list, Vector2 a, Vector2 b)
    {
        list.Add(a);
        list.Add(b);
    }

    /// <summary>Ore ground: the iron plate turned a quarter per cell; other seams get their own marks until their plates are cut.</summary>
    void SeamStrokes(Rect2 r, Terrain kind, int wx, int wy, Texture2D? ore)
    {
        int t = Ink.Tile;
        if (ore != null && kind == Terrain.IronOre)
        {
            int quarter = (int)(Ink.Jitter(wx, wy, 7) * 4);
            DrawSetTransform(r.GetCenter(), quarter * Mathf.Pi / 2, Vector2.One);
            DrawTextureRect(ore, new Rect2(-t / 2f, -t / 2f, t, t), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        int n = kind switch { Terrain.Sand => 10, Terrain.Coal => 5, Terrain.Limestone => 3, Terrain.OilSeep => 2, _ => 6 };
        for (int s = 0; s < n; s++)
        {
            var p = r.Position + new Vector2(Ink.Jitter(wx, wy, s) * (t - 12) + 6, Ink.Jitter(wx, wy, s + 17) * (t - 12) + 6);
            float rad = 1.5f + Ink.Jitter(wx, wy, s + 41) * 2.5f;
            switch (kind)
            {
                case Terrain.Coal:
                    Seg(heavyMarks, p - new Vector2(rad + 1, 0), p + new Vector2(rad + 1, 0));
                    break;
                case Terrain.Limestone:
                    var a = p - new Vector2(rad * 2, rad);
                    var b = p + new Vector2(rad * 2, rad);
                    Seg(marks, a, new Vector2(b.X, a.Y)); Seg(marks, new Vector2(b.X, a.Y), b); Seg(marks, b, new Vector2(a.X, b.Y)); Seg(marks, new Vector2(a.X, b.Y), a);
                    break;
                case Terrain.Sand:
                    Seg(marks, p, p + new Vector2(1.5f, 0));
                    break;
                case Terrain.Clay:
                    Seg(marks, p - new Vector2(rad * 2, 0), p + new Vector2(rad * 2, 0));
                    Seg(marks, p - new Vector2(rad * 2, 3), p + new Vector2(rad * 2, 3));
                    break;
                case Terrain.CopperOre:
                    dots.Add((p, rad, Ink.Soft));
                    Seg(marks, p + new Vector2(-rad - 2, -rad - 2), p + new Vector2(rad + 2, -rad - 2));
                    Seg(marks, p + new Vector2(rad + 2, -rad - 2), p + new Vector2(rad + 2, rad + 2));
                    Seg(marks, p + new Vector2(rad + 2, rad + 2), p + new Vector2(-rad - 2, rad + 2));
                    Seg(marks, p + new Vector2(-rad - 2, rad + 2), p + new Vector2(-rad - 2, -rad - 2));
                    break;
                case Terrain.OilSeep:
                    // A dark pool with a rainbow ring: a blot and a loose circle.
                    dots.Add((p, rad * 1.4f, Ink.Black));
                    Seg(marks, p + new Vector2(-rad * 2.2f, 0), p + new Vector2(-rad * 1.4f, -rad * 1.2f));
                    Seg(marks, p + new Vector2(rad * 1.4f, rad * 1.2f), p + new Vector2(rad * 2.2f, 0));
                    break;
                case Terrain.Sulfur:
                    Seg(marks, p + new Vector2(0, -rad * 1.5f), p + new Vector2(rad, rad));
                    Seg(marks, p + new Vector2(rad, rad), p + new Vector2(-rad, rad));
                    Seg(marks, p + new Vector2(-rad, rad), p + new Vector2(0, -rad * 1.5f));
                    break;
                default:
                    Seg(marks, p + new Vector2(0, -rad), p + new Vector2(rad, 0));
                    Seg(marks, p + new Vector2(rad, 0), p + new Vector2(0, rad));
                    Seg(marks, p + new Vector2(0, rad), p + new Vector2(-rad, 0));
                    Seg(marks, p + new Vector2(-rad, 0), p + new Vector2(0, -rad));
                    break;
            }
        }
    }

    /// <summary>Contour hatching: light diagonal strokes, a little denser the higher the ground.</summary>
    void HillStrokes(Rect2 r, Cell cell)
    {
        double e = world.Map.Elevation(cell);
        float spacing = e > 0.55 ? 12f : e > 0.44 ? 16f : 22f;
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        for (float k = x0 + y0 + spacing; k < x1 + y1; k += spacing)
        {
            var a = new Vector2(Mathf.Max(x0, k - y1), 0);
            a.Y = k - a.X;
            var b = new Vector2(Mathf.Min(x1, k - y0), 0);
            b.Y = k - b.X;
            Seg(hatch, a, b);
        }
    }

    /// <summary>Two little engraved trees per tile, jittered so a wood never looks planted.</summary>
    void ForestStrokes(Rect2 r, int wx, int wy)
    {
        int t = Ink.Tile;
        for (int s = 0; s < 2; s++)
        {
            var foot = r.Position + new Vector2(Ink.Jitter(wx, wy, s + 3) * (t - 16) + 8, Ink.Jitter(wx, wy, s + 23) * (t - 22) + 20);
            float h = t * (0.28f + 0.14f * Ink.Jitter(wx, wy, s + 51));
            var top = foot - new Vector2(0, h);
            var left = foot + new Vector2(-h * 0.45f, -h * 0.25f);
            var right = foot + new Vector2(h * 0.45f, -h * 0.25f);
            Seg(trunks, foot, foot - new Vector2(0, h * 0.3f));
            Seg(trees, top, right);
            Seg(trees, right, left);
            Seg(trees, left, top);
            Seg(marks, top + new Vector2(0, h * 0.2f), foot + new Vector2(h * 0.2f, -h * 0.28f));
        }
    }

    /// <summary>Engraved water: wavering lines along the flow, a heavier stroke along each bank.</summary>
    void RiverStrokes(Rect2 r, Cell cell)
    {
        int t = Ink.Tile;
        DrawRect(r, Ink.Faint with { A = 0.18f });
        for (int k = 0; k < 4; k++)
        {
            float y = r.Position.Y + t * (0.15f + 0.23f * k) + 2 * Ink.Jitter(cell.X, cell.Y, k);
            Vector2 prev = default;
            for (int i = 0; i < 5; i++)
            {
                var p = new Vector2(r.Position.X + i * t / 4f, y + 1.8f * Mathf.Sin((cell.X * 4 + i) * 1.3f + k));
                if (i > 0)
                    Seg(waves, prev, p);
                prev = p;
            }
        }
        if (world.TerrainAt(cell + Dir.North.Offset()) != Terrain.River)
            Seg(banks, r.Position + new Vector2(0, 1), r.Position + new Vector2(t, 1));
        if (world.TerrainAt(cell + Dir.South.Offset()) != Terrain.River)
            Seg(banks, r.Position + new Vector2(0, t - 1), r.Position + new Vector2(t, t - 1));
    }

    /// <summary>A town block: a roofline of little houses with windows, streets left blank between blocks.</summary>
    void TownStrokes(Rect2 r, Cell cell)
    {
        var body = r.Grow(-3);
        DrawRect(body, Ink.Paper);
        float roof = body.Position.Y + body.Size.Y * 0.4f;
        for (float k = body.Position.X + body.Position.Y + 4; k < body.End.X + roof; k += 4)
        {
            var a = new Vector2(Mathf.Max(body.Position.X, k - roof), 0);
            a.Y = k - a.X;
            var b = new Vector2(Mathf.Min(body.End.X, k - body.Position.Y), 0);
            b.Y = k - b.X;
            if (a.Y >= body.Position.Y && b.Y <= roof)
                Seg(hatch, a, b);
        }
        Seg(townLines, body.Position, new Vector2(body.End.X, body.Position.Y));
        Seg(townLines, new Vector2(body.End.X, body.Position.Y), body.End);
        Seg(townLines, body.End, new Vector2(body.Position.X, body.End.Y));
        Seg(townLines, new Vector2(body.Position.X, body.End.Y), body.Position);
        Seg(townLines, new Vector2(body.Position.X, roof), new Vector2(body.End.X, roof));
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(body.Position.X + 8 + i * (body.Size.X - 12) / 3f, body.Position.Y + body.Size.Y * 0.55f, 7, 10), Ink.Black);
        if (((cell.X * 7 + cell.Y * 3) & 3) == 0)
            DrawRect(new Rect2(body.End.X - 14, body.Position.Y - 6, 5, 9), Ink.Black);
    }
}
