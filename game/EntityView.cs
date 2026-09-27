using System.Collections.Generic;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>A building the player is about to place, drawn as a pencil sketch.</summary>
public readonly record struct Ghost(BuildingType Type, Cell Origin, Dir Facing, PlaceResult Status, int Span);

/// <summary>
/// Draws belts, goods and buildings every frame in the engraved oblique view: the grid stays
/// square on screen and buildings rise toward the top of the page, sorted front to back.
/// Zoomed out, goods give way to flow lines (the hybrid view).
/// </summary>
public partial class EntityView : Node2D
{
    const float DetailZoom = 0.45f;

    /// <summary>Buildings standing in front of this cell fade to an ink-wash outline (GDD §11).</summary>
    public const float OcclusionFade = 0.35f;

    public World World { get; set; } = null!;
    public Ghost? Ghost { get; set; }
    /// <summary>The cell under the cursor, for the occlusion fade.</summary>
    public Cell? HoverCell { get; set; }

    /// <summary>Camera zoom this frame; strokes divided by it stay the same width on screen.</summary>
    float zoom = 1f;
    /// <summary>0 = flow lines, 1 = goods drawn; cross-fades over ±15% around the switch zoom.</summary>
    float detail = 1f;
    /// <summary>Multiplies every stroke's alpha while a building is drawn; the occlusion fade sets it.</summary>
    float wash = 1f;

    Color I(Color c) => c with { A = c.A * wash };

    public override void _Ready() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        zoom = GetCanvasTransform().Scale.X;
        detail = Mathf.SmoothStep(DetailZoom * 0.85f, DetailZoom * 1.15f, zoom);
        var view = VisibleWorldRect();
        DrawSurveyGrid(view);
        view = view.Grow(Ink.Tile * 3);

        var visible = World.Buildings.Where(b => view.Intersects(Footprint(b))).ToList();
        poles.Clear();
        if (visible.Any(b => b is Pole))
            foreach (var b in World.Buildings)
                if (b is Pole p)
                    poles.Add(p);

        // Pipes and poles lie flat like belts, under everything that stands.
        wash = 1f;
        foreach (var b in visible)
            if (b is Pipe pipe)
                DrawPipe(pipe);
        foreach (var b in visible)
            if (b is Pole pole)
                DrawPole(pole);
        foreach (var b in visible)
            if (b is Track track)
            {
                if (track.IsRoad)
                    DrawRoad(track);
                else
                    DrawRail(track);
            }
        if (detail > 0f)
        {
            wash = detail;
            foreach (var b in visible)
                if (b is Belt belt)
                    DrawBelt(belt, true);
            foreach (var b in visible)
                if (b is Belt { Role: BeltRole.BridgeEntry } entry)
                    DrawDeck(entry, true);
            foreach (var line in World.Lines)
                if (LineVisible(line, view))
                    DrawGoods(line);
            foreach (var b in visible)
                if (b is Splitter s)
                    DrawLaneGoods(s);
        }
        if (detail < 1f)
        {
            wash = 1f - detail;
            foreach (var b in visible)
                if (b is Belt belt)
                    DrawBelt(belt, false);
            foreach (var b in visible)
                if (b is Belt { Role: BeltRole.BridgeEntry } entry)
                    DrawDeck(entry, false);
            foreach (var line in World.Lines)
                if (line.Count > 0 && LineVisible(line, view))
                    DrawFlow(line);
        }

        // The fleet runs on the flat, over the roads and under whatever stands.
        wash = 1f;
        foreach (var v in World.Haulage.Fleet)
            if (view.HasPoint(VehiclePos(v)))
                DrawVehicle(v);

        // Front-to-back: whatever sits lower on the page is drawn last. A building whose body
        // stands between the reader and the hovered cell prints as a faint wash so the cell shows.
        foreach (var b in visible.Where(b => b is not Belt and not Track).OrderBy(b => b.Origin.Y + b.Height).ThenBy(b => b.Origin.X))
        {
            wash = Occludes(b) ? OcclusionFade : 1f;
            switch (b)
            {
                case Mine m: DrawMine(m); DrawStateMark(m, !m.Lit ? MachineState.Unpowered : m.Buffered >= Mine.BufferCap ? MachineState.Blocked : MachineState.Working); break;
                case Depot d: DrawDepot(d); break;
                case Splitter s: DrawSplitter(s); break;
                case Machine mc: DrawMachine(mc); DrawSourceMark(mc); break;
                case Dock dock: DrawBlock(dock, MachineState.Working, 1); if (dock.Order is { } order) DrawGood(order, Center(dock.PortCell) - V(dock.Facing) * Ink.Tile * 0.9f + new Vector2(0, -Ink.Tile * 0.5f)); break;
                case LoggingCamp camp: DrawBlock(camp, camp.Trees == 0 ? MachineState.Starved : Cold(camp.Firebox, camp.Source) ? MachineState.Unpowered : MachineState.Working, 1); break;
                case PumpJack jack: DrawBlock(jack, Cold(jack.Firebox, jack.Source) ? MachineState.Unpowered : MachineState.Working, 1); break;
                case Waterwheel wheel: DrawWheel(wheel); break;
                case WaterPump pump: DrawBlock(pump, MachineState.Working, 0); break;
                case Boiler boiler: DrawBlock(boiler, boiler.Lit ? MachineState.Working : MachineState.Unpowered, 0); break;
                case PowerStation st: DrawBlock(st, st.OutputKw > 0 ? MachineState.Working : MachineState.Starved, 0); break;
                case Dam dam: DrawDam(dam); break;
                case Terminal term: DrawTerminal(term); break;
                case Yard yard: DrawBlock(yard, World.Prestige.Current == null ? MachineState.Starved : MachineState.Working, 0); break;
            }
        }
        wash = 1f;

        DrawPlans(view);
        if (Ghost is { } g)
            DrawGhost(g);
        DrawRouteSketch();
        DrawHeld();
        DrawSelection();
        if (Overlay)
            DrawOverlay(view, visible);
    }

    /// <summary>Visual height above the footprint, in tiles, of a building's drawn body.</summary>
    static float BodyHeight(Building b)
    {
        if (Art.Building(b.Def) is { } tex)
            return SpriteRect(b, tex).Size.Y / Ink.Tile - b.Height;
        return b switch
        {
            Mine => 0.42f + 0.6f,
            Depot => 0.55f,
            Splitter => 0.2f,
            Machine m => BlockHeight(m),
            Dock d => BlockHeight(d),
            Waterwheel => 0.9f,
            Dam => 0.3f,
            Pipe or Pole or Track => 0f,
            _ => BlockHeight(b),
        };
    }

    static float BlockHeight(Building b) => b.Width switch { 1 => 0.45f, 2 => 0.65f, _ => 0.85f };

    /// <summary>Where a building's plate prints: as wide as its footprint, standing on the footprint's bottom edge.</summary>
    static Rect2 SpriteRect(Building b, Texture2D tex)
    {
        float w = b.Width * Ink.Tile;
        float h = tex.GetHeight() * (w / tex.GetWidth());
        return new Rect2(b.Origin.X * Ink.Tile, (b.Origin.Y + b.Height) * Ink.Tile - h, w, h);
    }

    /// <summary>Prints the building's engraved plate, if one has been cut. Returns false to fall back to ink strokes.</summary>
    bool DrawSprite(Building b)
    {
        if (Art.Building(b.Def) is not { } tex)
            return false;
        DrawTextureRect(tex, SpriteRect(b, tex), false, I(Colors.White));
        return true;
    }

    /// <summary>True when the hovered cell lies under this building's raised body but outside its footprint.</summary>
    public bool Occludes(Building b)
    {
        if (HoverCell is not { } cell)
            return false;
        if (cell.X < b.Origin.X || cell.X >= b.Origin.X + b.Width)
            return false;
        if (cell.Y >= b.Origin.Y && cell.Y < b.Origin.Y + b.Height)
            return false;
        float top = b.Origin.Y - BodyHeight(b);
        return cell.Y + 1 > top && cell.Y < b.Origin.Y;
    }

    /// <summary>Faint cell grid, faded out as you zoom away so it never turns to moiré.</summary>
    void DrawSurveyGrid(Rect2 view)
    {
        float alpha = Mathf.Clamp((zoom - 0.35f) / 0.4f, 0f, 1f) * Ink.Faint.A;
        if (alpha <= 0.005f)
            return;
        var color = Ink.Faint with { A = alpha };
        int t = Ink.Tile;
        for (int x = Mathf.FloorToInt(view.Position.X / t); x <= Mathf.CeilToInt(view.End.X / t); x++)
            DrawLine(new Vector2(x * t, view.Position.Y), new Vector2(x * t, view.End.Y), color, 1f / zoom);
        for (int y = Mathf.FloorToInt(view.Position.Y / t); y <= Mathf.CeilToInt(view.End.Y / t); y++)
            DrawLine(new Vector2(view.Position.X, y * t), new Vector2(view.End.X, y * t), color, 1f / zoom);
    }

    static Rect2 Footprint(Building b) =>
        new(b.Origin.X * Ink.Tile, b.Origin.Y * Ink.Tile, b.Width * Ink.Tile, b.Height * Ink.Tile);

    static bool LineVisible(TransportLine line, Rect2 view) =>
        line.Tiles.Count > 0 && view.Intersects(new Rect2(line.Min.X * Ink.Tile, line.Min.Y * Ink.Tile,
            (line.Max.X - line.Min.X + 1) * Ink.Tile, (line.Max.Y - line.Min.Y + 1) * Ink.Tile));

    public static Vector2 Center(Cell c) => new((c.X + 0.5f) * Ink.Tile, (c.Y + 0.5f) * Ink.Tile);
    public static Vector2 V(Dir d) { var o = d.Offset(); return new Vector2(o.X, o.Y); }

    float BeltPhase(BeltTier tier) => (float)(World.TickCount * BeltTiers.Speed(tier) % BeltTiers.TileLength) / BeltTiers.TileLength;

    // ---- Belts -------------------------------------------------------------------------------

    /// <summary>Corner geometry for a curved belt: pivot, start angle and signed sweep.</summary>
    static (Vector2 pivot, float start, float sweep) Arc(Belt belt, Dir curveIn)
    {
        var c = Center(belt.Origin);
        var dIn = V(curveIn);
        var dOut = V(belt.Facing);
        var pivot = c + (-dIn + dOut) * Ink.Tile * 0.5f;
        var entry = c - dIn * Ink.Tile * 0.5f;
        var exit = c + dOut * Ink.Tile * 0.5f;
        float a0 = (entry - pivot).Angle();
        float sweep = Mathf.Wrap((exit - pivot).Angle() - a0, -Mathf.Pi, Mathf.Pi);
        return (pivot, a0, sweep);
    }

    /// <summary>
    /// Where a good whose centre is <paramref name="local"/> sub-units along this tile sits. Values
    /// below zero are just short of the tile (a good arriving from the line behind); values past the
    /// tile length are on a bridge deck.
    /// </summary>
    public static Vector2 PointOnTile(Belt belt, float local)
    {
        float t = local / BeltTiers.TileLength;
        if (belt.CurveIn is { } curveIn)
        {
            var (pivot, start, sweep) = Arc(belt, curveIn);
            if (t < 0)
                return Center(belt.Origin) - V(curveIn) * Ink.Tile * (0.5f - t);
            return pivot + Vector2.FromAngle(start + sweep * Mathf.Min(t, 1f)) * Ink.Tile * 0.5f;
        }
        var d = V(belt.Facing);
        return Center(belt.Origin) + d * Ink.Tile * (t - 0.5f);
    }

    void DrawBelt(Belt belt, bool detailed)
    {
        float t = Ink.Tile;
        float edge = belt.Tier switch { BeltTier.Canvas => 1.5f, BeltTier.Rubber => 2.2f, _ => 2.8f };
        int bars = belt.Tier switch { BeltTier.Canvas => 3, BeltTier.Rubber => 4, _ => 6 };
        if (belt.CurveIn is { } curveIn)
        {
            var (pivot, start, sweep) = Arc(belt, curveIn);
            if (detailed)
            {
                DrawArc(pivot, t * 0.18f, start, start + sweep, 10, I(Ink.Black), edge);
                DrawArc(pivot, t * 0.82f, start, start + sweep, 16, I(Ink.Black), edge);
                for (int k = 0; k < bars; k++)
                {
                    float f = (k + BeltPhase(belt.Tier) * bars) / bars % 1f;
                    var dir = Vector2.FromAngle(start + sweep * f);
                    DrawLine(pivot + dir * t * 0.2f, pivot + dir * t * 0.8f, I(Ink.Soft), 1f);
                }
            }
            else
            {
                DrawArc(pivot, t * 0.5f, start, start + sweep, 12, I(Ink.Soft), 2.5f / zoom);
            }
            return;
        }

        var c = Center(belt.Origin);
        var d = V(belt.Facing);
        var p = new Vector2(-d.Y, d.X);
        var back = c - d * t * 0.5f;
        var front = c + d * t * 0.5f;
        if (!detailed)
        {
            DrawLine(back, front, I(Ink.Soft), 2.5f / zoom);
            return;
        }

        DrawLine(back + p * t * 0.32f, front + p * t * 0.32f, I(Ink.Black), edge);
        DrawLine(back - p * t * 0.32f, front - p * t * 0.32f, I(Ink.Black), edge);
        for (int k = 0; k < bars; k++)
        {
            float f = (k + BeltPhase(belt.Tier) * bars) / bars % 1f;
            var s = back + d * t * f;
            DrawLine(s + p * t * 0.3f, s - p * t * 0.3f, I(Ink.Soft), belt.Tier == BeltTier.Steel ? 1.5f : 1f);
        }
        if (belt.Role == BeltRole.Plain)
        {
            // Small chevron so direction reads at a glance.
            var tip = c + d * t * 0.12f;
            DrawLine(tip, tip - d * t * 0.12f + p * t * 0.12f, I(Ink.Soft), 1.5f);
            DrawLine(tip, tip - d * t * 0.12f - p * t * 0.12f, I(Ink.Soft), 1.5f);
        }
        else
        {
            // A ramp: the deck rises at an entry and drops at an exit.
            var lo = belt.Role == BeltRole.BridgeEntry ? back : front;
            var hi = belt.Role == BeltRole.BridgeEntry ? front : back;
            DrawLine(lo + p * t * 0.36f, hi + p * t * 0.36f - new Vector2(0, t * 0.22f), I(Ink.Black), 2f);
            DrawLine(lo - p * t * 0.36f, hi - p * t * 0.36f - new Vector2(0, t * 0.22f), I(Ink.Black), 2f);
        }
    }

    /// <summary>The trestle deck from an entry to its exit: two rails on X-braced legs, over whatever lies beneath.</summary>
    void DrawDeck(Belt entry, bool detailed)
    {
        float t = Ink.Tile;
        var d = V(entry.Facing);
        var p = new Vector2(-d.Y, d.X);
        var lift = new Vector2(0, -t * 0.22f);
        var from = Center(entry.Origin) + d * t * 0.5f + lift;
        var to = Center(entry.Exit) - d * t * 0.5f + lift;
        if (!detailed)
        {
            DrawLine(from, to, I(Ink.Black), 3f / zoom);
            return;
        }
        DrawLine(from + p * t * 0.36f, to + p * t * 0.36f, I(Ink.Black), 2.5f);
        DrawLine(from - p * t * 0.36f, to - p * t * 0.36f, I(Ink.Black), 2.5f);
        for (int k = 0; k <= entry.Span * 2; k++)
        {
            var s = from.Lerp(to, k / (entry.Span * 2f));
            var foot = s - lift;
            DrawLine(s + p * t * 0.36f, foot + p * t * 0.3f, I(Ink.Black), 1.5f);
            DrawLine(s - p * t * 0.36f, foot - p * t * 0.3f, I(Ink.Black), 1.5f);
            DrawLine(s + p * t * 0.36f, foot - p * t * 0.3f, I(Ink.Soft), 1f);
            DrawLine(s - p * t * 0.36f, foot + p * t * 0.3f, I(Ink.Soft), 1f);
        }
    }

    void DrawGoods(TransportLine line)
    {
        int t = line.Tiles.Count - 1;
        var lift = new Vector2(0, -Ink.Tile * 0.22f);
        foreach (var (item, pos, _) in line.Goods)
        {
            while (t > 0 && pos < line.Tiles[t].LineStart)
                t--;
            var tile = line.Tiles[t];
            float local = pos - tile.LineStart;
            var at = PointOnTile(tile, local);
            if (tile.Role == BeltRole.BridgeEntry && local >= BeltTiers.TileLength)
                at += lift;
            DrawGood(item, at);
        }
    }

    void DrawLaneGoods(Splitter s)
    {
        for (int lane = 0; lane < 2; lane++)
        {
            var cell = s.LaneCell(lane);
            var d = V(s.Facing);
            foreach (var (item, pos, _) in s.Lane(lane).Goods)
                DrawGood(item, Center(cell) + d * Ink.Tile * (pos / (float)BeltTiers.TileLength - 0.5f));
        }
    }

    /// <summary>The good's engraved icon if one exists, else a lump: ore dark with a highlight, coal blacker, copper ringed.</summary>
    void DrawGood(Item item, Vector2 at)
    {
        float t = Ink.Tile;
        if (Art.Good(item) is { } tex)
        {
            float size = t * 0.44f;
            DrawTextureRect(tex, new Rect2(at - new Vector2(size, size) * 0.5f, new Vector2(size, size)), false, I(Colors.White));
            return;
        }
        switch (item)
        {
            case Item.CopperOre:
                DrawCircle(at, t * 0.14f, I(Ink.Black));
                DrawArc(at, t * 0.09f, 0, Mathf.Tau, 12, I(Ink.Paper), 1.2f);
                break;
            case Item.Coal:
                DrawRect(new Rect2(at - new Vector2(t * 0.12f, t * 0.11f), new Vector2(t * 0.24f, t * 0.22f)), I(Ink.Black));
                break;
            default:
                DrawCircle(at, t * 0.14f, I(Ink.Black));
                DrawCircle(at + new Vector2(-t * 0.045f, -t * 0.05f), t * 0.04f, I(Ink.Paper));
                break;
        }
    }

    /// <summary>Zoomed out: one stroke per line, heavier the fuller it is, in red when it is backed up.</summary>
    void DrawFlow(TransportLine line)
    {
        float fill = Mathf.Min(1f, line.Count * (float)BeltTiers.Spacing / line.Length);
        var color = I(line.Stalled ? Ink.Red : Ink.Black);
        float width = (2f + 5f * fill) / zoom;
        var pts = new List<Vector2>(line.Tiles.Count + 1);
        foreach (var tile in line.Tiles)
        {
            pts.Add(Center(tile.Origin));
            if (tile.Role == BeltRole.BridgeEntry)
                pts.Add(Center(tile.Exit));
        }
        if (pts.Count == 1)
        {
            var d = V(line.Tiles[0].Facing) * Ink.Tile * 0.5f;
            DrawLine(pts[0] - d, pts[0] + d, color, width);
            return;
        }
        DrawPolyline(pts.ToArray(), color, width);
    }

    // ---- Buildings ---------------------------------------------------------------------------

    /// <summary>Draws a box of the given height and returns its top face.</summary>
    Rect2 Block(Rect2 footprint, float height)
    {
        var baseRect = footprint.Grow(-Ink.Tile * 0.1f);
        var top = new Rect2(baseRect.Position.X, baseRect.Position.Y - height, baseRect.Size.X, baseRect.Size.Y);
        var front = new Rect2(baseRect.Position.X, baseRect.End.Y - height, baseRect.Size.X, height);

        // Cast shadow to the lower right, like a plate lit from the upper left.
        DrawRect(new Rect2(baseRect.Position + new Vector2(5, 4), baseRect.Size), I(Ink.Faint));
        DrawRect(front, I(Ink.Paper));
        Ink.Strokes(this, front, 3f, I(Ink.Soft));
        DrawRect(front, I(Ink.Black), false, 2f);
        DrawRect(top, I(Ink.Paper));
        DrawRect(top, I(Ink.Black), false, 2f);
        return top;
    }

    void DrawMine(Mine m)
    {
        float t = Ink.Tile;
        if (DrawSprite(m))
        {
            DrawPort(m.OutputCell, m.Facing);
            return;
        }
        var top = Block(Footprint(m), t * 0.42f);
        Ink.Hatch(this, top.Grow(-4), 6f, I(Ink.Faint with { A = 0.3f }));

        // Headframe: an A-frame over the shaft with its winding wheel.
        var mid = top.GetCenter();
        var l = new Vector2(top.Position.X + top.Size.X * 0.25f, top.End.Y - t * 0.2f);
        var r = new Vector2(top.End.X - top.Size.X * 0.25f, l.Y);
        var apex = new Vector2(mid.X, top.Position.Y + t * 0.3f);
        DrawLine(l, apex, I(Ink.Black), 2.5f);
        DrawLine(r, apex, I(Ink.Black), 2.5f);
        DrawLine(l.Lerp(apex, 0.45f), r.Lerp(apex, 0.45f), I(Ink.Black), 2f);
        DrawArc(apex, t * 0.16f, 0, Mathf.Tau, 18, I(Ink.Black), 2f);
        DrawLine(apex, apex + new Vector2(0, t * 0.55f), I(Ink.Soft), 1f);

        DrawPort(m.OutputCell, m.Facing);
    }

    void DrawDepot(Depot d)
    {
        float t = Ink.Tile;
        if (DrawSprite(d))
            return;
        var top = Block(Footprint(d), t * 0.55f);
        Ink.Hatch(this, top.Grow(-4), 5f, I(Ink.Faint with { A = 0.3f }), rising: false);

        // A braced freight crate.
        var crate = new Rect2(top.GetCenter() - new Vector2(t * 0.42f, t * 0.32f), new Vector2(t * 0.84f, t * 0.64f));
        DrawRect(crate, I(Ink.Paper));
        DrawRect(crate, I(Ink.Black), false, 2f);
        DrawLine(crate.Position, crate.End, I(Ink.Black), 1.5f);
        DrawLine(new Vector2(crate.Position.X, crate.End.Y), new Vector2(crate.End.X, crate.Position.Y), I(Ink.Black), 1.5f);
    }

    /// <summary>A low housing across the flow with a groove per lane; a sorting splitter carries its filter good on the left lane.</summary>
    void DrawSplitter(Splitter s)
    {
        float t = Ink.Tile;
        if (DrawSprite(s))
            return;
        var top = Block(Footprint(s), t * 0.2f);
        var d = V(s.Facing);
        var p = new Vector2(-d.Y, d.X);
        for (int lane = 0; lane < 2; lane++)
        {
            var c = Center(s.LaneCell(lane)) + new Vector2(0, -t * 0.2f);
            DrawLine(c - d * t * 0.38f, c + d * t * 0.38f, I(Ink.Soft), 6f);
            var tip = c + d * t * 0.3f;
            DrawLine(tip, tip - d * t * 0.14f + p * t * 0.14f, I(Ink.Black), 2f);
            DrawLine(tip, tip - d * t * 0.14f - p * t * 0.14f, I(Ink.Black), 2f);
        }
        // The dealing arm: a diagonal bar joining the two grooves.
        var a = Center(s.LaneCell(0)) - d * t * 0.25f + new Vector2(0, -t * 0.2f);
        var b = Center(s.LaneCell(1)) + d * t * 0.25f + new Vector2(0, -t * 0.2f);
        DrawLine(a, b, I(Ink.Black), 2f);
        if (s.Sorting)
        {
            var mark = Center(s.LaneCell(0)) - d * t * 0.02f + new Vector2(0, -t * 0.2f);
            DrawCircle(mark, t * 0.2f, I(Ink.Paper));
            DrawArc(mark, t * 0.2f, 0, Mathf.Tau, 16, I(Ink.Black), 1.5f);
            if (s.Filter is { } f)
                DrawGood(f, mark);
            else
                DrawLine(mark - new Vector2(t * 0.1f, 0), mark + new Vector2(t * 0.1f, 0), I(Ink.Red), 2f);
        }
        _ = top;
    }

    /// <summary>
    /// A machine without a plate yet: an ink block with its name set in type, a mark for its
    /// state (red when stopped), and a chute per output port.
    /// </summary>
    void DrawMachine(Machine m) => DrawBlock(m, m.State, Recipes.PortCount(m.Type));

    void DrawBlock(Building m, MachineState state, int ports)
    {
        float t = Ink.Tile;
        if (!DrawSprite(m))
        {
            var top = Block(Footprint(m), t * BlockHeight(m));
            Ink.Hatch(this, top.Grow(-4), 7f, I(Ink.Faint with { A = 0.25f }));
            var font = Fell;
            string name = Text.Get("NAME_" + m.Def.Id).ToUpperInvariant();
            int size = m.Width == 1 ? 9 : 13;
            var lines = m.Width == 1 ? new[] { name.Length > 7 ? name[..6] + "." : name } : name.Split(' ');
            float y = top.GetCenter().Y - (lines.Length - 1) * size * 0.6f;
            foreach (var line in lines)
            {
                float w = font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X;
                DrawString(font, new Vector2(top.GetCenter().X - w / 2, y + size * 0.35f), line, HorizontalAlignment.Left, -1, size, I(Ink.Black));
                y += size * 1.2f;
            }
        }
        DrawStateMark(m, state);
        var cells = m.OutputCells(ports);
        for (int p = 0; p < ports; p++)
            DrawPort(cells[p], m.Facing);
    }

    /// <summary>State mark in the near corner: paper dot working, ink dot starved, red when stopped.</summary>
    void DrawStateMark(Building m, MachineState state)
    {
        float t = Ink.Tile;
        var mark = new Vector2(m.Origin.X * t + t * 0.2f, (m.Origin.Y + m.Height) * t - t * 0.2f);
        var color = state switch
        {
            MachineState.Working => Ink.Paper,
            MachineState.Starved => Ink.Soft,
            _ => Ink.Red,
        };
        DrawCircle(mark, t * 0.07f, I(color));
        DrawArc(mark, t * 0.07f, 0, Mathf.Tau, 10, I(Ink.Black), 1f);
    }

    static FontFile? fell;
    static FontFile Fell => fell ??= GD.Load<FontFile>("res://assets/fonts/IMFellEnglish-Roman.ttf");

    // ---- Power ------------------------------------------------------------------------------

    static bool Conducts(Building? b) => b is Pipe or Boiler or WaterPump or PowerStation;

    /// <summary>Every pole on the map this frame, gathered once so each pole's wires look only at poles.</summary>
    readonly List<Pole> poles = new();

    /// <summary>A steam main: a fat double line joined to whatever conducts beside it, flanged at the joints.</summary>
    void DrawPipe(Pipe pipe)
    {
        float t = Ink.Tile;
        var c = Center(pipe.Origin);
        int joints = 0;
        for (int d = 0; d < 4; d++)
        {
            var dir = (Dir)d;
            if (!Conducts(World.BuildingAt(pipe.Origin + dir.Offset())))
                continue;
            joints++;
            var v = V(dir);
            var p = new Vector2(-v.Y, v.X);
            DrawLine(c + p * t * 0.11f, c + v * t * 0.5f + p * t * 0.11f, I(Ink.Black), 2f);
            DrawLine(c - p * t * 0.11f, c + v * t * 0.5f - p * t * 0.11f, I(Ink.Black), 2f);
            DrawLine(c + v * t * 0.42f + p * t * 0.17f, c + v * t * 0.42f - p * t * 0.17f, I(Ink.Black), 2.5f);
        }
        DrawCircle(c, t * 0.13f, I(Ink.Paper));
        DrawArc(c, t * 0.13f, 0, Mathf.Tau, 12, I(Ink.Black), 2f);
        if (joints == 0)
            DrawLine(c - new Vector2(t * 0.11f, 0), c + new Vector2(t * 0.11f, 0), I(Ink.Red), 2f);
    }

    /// <summary>A post with a crossbar and its wires to every pole within reach.</summary>
    void DrawPole(Pole pole)
    {
        float t = Ink.Tile;
        var foot = Center(pole.Origin) + new Vector2(0, t * 0.3f);
        var top = foot - new Vector2(0, t * 0.9f);
        DrawLine(foot, top, I(Ink.Black), 3f);
        DrawLine(top + new Vector2(-t * 0.22f, t * 0.08f), top + new Vector2(t * 0.22f, t * 0.08f), I(Ink.Black), 2f);
        foreach (var q in poles)
        {
            if (q.Id <= pole.Id)
                continue;
            int dx = q.Origin.X - pole.Origin.X, dy = q.Origin.Y - pole.Origin.Y;
            if (dx * dx + dy * dy > Pole.Radius * Pole.Radius)
                continue;
            var to = Center(q.Origin) + new Vector2(0, t * 0.3f) - new Vector2(0, t * 0.82f);
            var from = top + new Vector2(0, t * 0.08f);
            var mid = (from + to) / 2 + new Vector2(0, t * 0.12f);
            DrawPolyline(new[] { from, mid, to }, I(Ink.Soft), 1.2f);
        }
    }

    /// <summary>An undershot wheel on the bank: rim, spokes and paddles, turning while it drives anything.</summary>
    void DrawWheel(Waterwheel wheel)
    {
        float t = Ink.Tile;
        var fp = Footprint(wheel);
        var c = fp.GetCenter() + new Vector2(0, -t * 0.15f);
        float r = t * 0.55f;
        float phase = wheel.DemandKw > 0 ? (float)(World.TickCount % 240) / 240f * Mathf.Tau : 0f;
        DrawArc(c, r, 0, Mathf.Tau, 24, I(Ink.Black), 2.5f);
        DrawArc(c, r * 0.8f, 0, Mathf.Tau, 24, I(Ink.Black), 1.2f);
        for (int k = 0; k < 8; k++)
        {
            var d = Vector2.FromAngle(phase + k * Mathf.Tau / 8);
            DrawLine(c, c + d * r * 0.8f, I(Ink.Soft), 1.5f);
            DrawLine(c + d * r * 0.8f, c + d * r + new Vector2(-d.Y, d.X) * t * 0.08f, I(Ink.Black), 2f);
        }
        DrawCircle(c, t * 0.08f, I(Ink.Black));
        // The axle bearing on a post.
        DrawLine(c, c + new Vector2(0, r + t * 0.15f), I(Ink.Black), 3f);
    }

    /// <summary>A masonry wall across the water, with the sluice arches.</summary>
    void DrawDam(Dam dam)
    {
        float t = Ink.Tile;
        var fp = Footprint(dam).Grow(-t * 0.08f);
        DrawRect(fp, I(Ink.Paper));
        Ink.Hatch(this, fp, 5f, I(Ink.Soft), 1f, rising: false);
        DrawRect(fp, I(Ink.Black), false, 3f);
        for (int k = 0; k < 3; k++)
        {
            var a = new Vector2(fp.Position.X + fp.Size.X * (0.2f + 0.3f * k), fp.End.Y - t * 0.1f);
            DrawArc(a, t * 0.18f, Mathf.Pi, Mathf.Tau, 10, I(Ink.Black), 2f);
        }
        DrawLine(fp.Position + new Vector2(0, t * 0.3f), new Vector2(fp.End.X, fp.Position.Y + t * 0.3f), I(Ink.Black), 2f);
    }

    /// <summary>A small glyph beside the state mark: a wave for steam, a zigzag for electricity, a ring for the shaft.</summary>
    void DrawSourceMark(Machine m)
    {
        float t = Ink.Tile;
        var at = new Vector2(m.Origin.X * t + t * 0.45f, (m.Origin.Y + m.Height) * t - t * 0.2f);
        switch (m.Source)
        {
            case PowerSource.Electric:
                DrawPolyline(new[] { at + new Vector2(-6, -6), at + new Vector2(2, -1), at + new Vector2(-2, 1), at + new Vector2(6, 6) }, I(Ink.Black), 1.5f);
                break;
            case PowerSource.Steam:
                DrawPolyline(new[] { at + new Vector2(-7, 2), at + new Vector2(-3, -2), at + new Vector2(1, 2), at + new Vector2(5, -2) }, I(Ink.Black), 1.5f);
                break;
            case PowerSource.Shaft:
                DrawArc(at, 5f, 0, Mathf.Tau, 10, I(Ink.Black), 1.5f);
                break;
        }
    }

    /// <summary>A little chute from a building into the cell its goods come out on.</summary>
    void DrawPort(Cell port, Dir facing)
    {
        var d = V(facing);
        var p = new Vector2(-d.Y, d.X);
        var edge = Center(port) - d * Ink.Tile * 0.5f;
        var tip = edge + d * Ink.Tile * 0.22f;
        DrawColoredPolygon(new[] { tip, edge + p * Ink.Tile * 0.16f, edge - p * Ink.Tile * 0.16f }, I(Ink.Black));
    }

    // ---- Ghost -------------------------------------------------------------------------------

    void DrawGhost(Ghost g)
    {
        var color = g.Status == PlaceResult.Ok ? Ink.Pencil : Ink.Red;
        if (g.Type == BuildingType.Trestle)
        {
            SketchCell(g.Origin, 1, 1, color, g.Facing);
            if (g.Span > 0)
            {
                var exit = g.Origin + g.Facing.Offset() * (g.Span + 1);
                SketchCell(exit, 1, 1, color, g.Facing);
                DrawDashedLine(Center(g.Origin), Center(exit), color, 2f, 8f);
            }
            return;
        }
        var (w, h) = Catalog.Footprint(g.Type, g.Facing);
        SketchCell(g.Origin, w, h, color, g.Facing);
    }

    void SketchCell(Cell origin, int w, int h, Color color, Dir facing)
    {
        float t = Ink.Tile;
        var r = new Rect2(origin.X * t, origin.Y * t, w * t, h * t).Grow(-2);
        DrawRect(r, color with { A = 0.08f });
        var corners = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        for (int i = 0; i < 4; i++)
            DrawDashedLine(corners[i], corners[(i + 1) % 4], color, 1.5f, 6f);

        var d = V(facing);
        var p = new Vector2(-d.Y, d.X);
        var c = r.GetCenter();
        float len = Mathf.Min(w, h);
        var tip = c + d * t * 0.3f * len;
        DrawLine(c - d * t * 0.25f * len, tip, color, 2f);
        DrawLine(tip, tip - d * t * 0.15f + p * t * 0.15f, color, 2f);
        DrawLine(tip, tip - d * t * 0.15f - p * t * 0.15f, color, 2f);
    }

    Rect2 VisibleWorldRect()
    {
        var inv = GetCanvasTransform().AffineInverse();
        var a = inv * Vector2.Zero;
        var b = inv * GetViewportRect().Size;
        return new Rect2(a, b - a);
    }
}
