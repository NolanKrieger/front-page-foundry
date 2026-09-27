using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>Planning ghosts, the blueprint in hand, the selection rectangle and the flow overlay (M10).</summary>
public partial class EntityView
{
    /// <summary>F: throughput, wants and power printed over everything (GDD §11: the overlay always draws on top).</summary>
    public bool Overlay { get; set; }
    /// <summary>The blueprint in hand and where its top-left corner would land.</summary>
    public IReadOnlyList<BlueprintPiece>? Held { get; set; }
    public Cell? StampOrigin { get; set; }
    /// <summary>Blueprint mode: the corners of the rectangle being dragged out.</summary>
    public (Cell A, Cell B)? Selection { get; set; }

    readonly Dictionary<int, (long Passed, long Tick, float PerMinute)> rates = new();

    // ---- Plans ---------------------------------------------------------------------------------

    /// <summary>Every plan in view as a pencil sketch with its initials; a planned dam shades the valley it would drown.</summary>
    void DrawPlans(Rect2 view)
    {
        foreach (var plan in World.Plans)
        {
            var (w, h) = Catalog.Footprint(plan.Type, plan.Facing);
            var rect = new Rect2(plan.Origin.X * Ink.Tile, plan.Origin.Y * Ink.Tile, w * Ink.Tile, h * Ink.Tile);
            if (Catalog.Of(plan.Type).IsBridge)
                rect = rect.Merge(new Rect2(Center(plan.Origin + plan.Facing.Offset() * (plan.Span + 1)) - Vector2.One * Ink.Tile / 2, Vector2.One * Ink.Tile));
            if (!view.Intersects(rect))
                continue;
            if (Catalog.Of(plan.Type).IsBridge)
            {
                var exit = plan.Origin + plan.Facing.Offset() * (plan.Span + 1);
                SketchCell(plan.Origin, 1, 1, Ink.Pencil, plan.Facing);
                SketchCell(exit, 1, 1, Ink.Pencil, plan.Facing);
                DrawDashedLine(Center(plan.Origin), Center(exit), Ink.Pencil, 2f, 8f);
                continue;
            }
            SketchCell(plan.Origin, w, h, Ink.Pencil, plan.Facing);
            if (w > 1 || h > 1)
                Initials(plan.Type, rect.GetCenter() + new Vector2(0, -rect.Size.Y * 0.3f));
            if (plan.Type == BuildingType.HydroDam)
                foreach (var c in World.FloodOf(plan.Origin, plan.Facing))
                    DrawRect(new Rect2(c.X * Ink.Tile, c.Y * Ink.Tile, Ink.Tile, Ink.Tile).Grow(-1), Ink.Pencil with { A = 0.12f });
        }
    }

    /// <summary>"OHF" for an open hearth furnace: the first letters of its name, in pencil.</summary>
    void Initials(BuildingType type, Vector2 at)
    {
        string name = Text.Get("NAME_" + Catalog.Of(type).Id);
        string initials = string.Concat(name.Split(' ').Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0])));
        int size = 12;
        float width = Fell.GetStringSize(initials, HorizontalAlignment.Left, -1, size).X;
        DrawString(Fell, at + new Vector2(-width / 2, size * 0.35f), initials, HorizontalAlignment.Left, -1, size, Ink.Pencil);
    }

    /// <summary>The blueprint in hand, each piece in pencil where it would go, red where it cannot.</summary>
    void DrawHeld()
    {
        if (Held is not { } pieces || StampOrigin is not { } at)
            return;
        foreach (var p in pieces)
        {
            var origin = at + p.Offset;
            bool ok = World.CanPlace(p.Type, origin, p.Facing, p.Span, asPlan: true) == PlaceResult.Ok;
            var color = ok ? Ink.Pencil : Ink.Red;
            if (Catalog.Of(p.Type).IsBridge)
            {
                var exit = origin + p.Facing.Offset() * (p.Span + 1);
                SketchCell(origin, 1, 1, color, p.Facing);
                SketchCell(exit, 1, 1, color, p.Facing);
                DrawDashedLine(Center(origin), Center(exit), color, 2f, 8f);
                continue;
            }
            var (w, h) = Catalog.Footprint(p.Type, p.Facing);
            SketchCell(origin, w, h, color, p.Facing);
        }
        var (bw, bh) = Blueprint.Size(pieces);
        var frame = new Rect2(at.X * Ink.Tile, at.Y * Ink.Tile, bw * Ink.Tile, bh * Ink.Tile).Grow(4);
        DrawRect(frame, Ink.Pencil with { A = 0.6f }, false, 1.5f / zoom);
    }

    /// <summary>The rectangle being dragged out in blueprint mode.</summary>
    void DrawSelection()
    {
        if (Selection is not var (a, b))
            return;
        int x0 = Mathf.Min(a.X, b.X), y0 = Mathf.Min(a.Y, b.Y), x1 = Mathf.Max(a.X, b.X), y1 = Mathf.Max(a.Y, b.Y);
        var r = new Rect2(x0 * Ink.Tile, y0 * Ink.Tile, (x1 - x0 + 1) * Ink.Tile, (y1 - y0 + 1) * Ink.Tile);
        DrawRect(r, Ink.Pencil with { A = 0.08f });
        var corners = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        for (int i = 0; i < 4; i++)
            DrawDashedLine(corners[i], corners[(i + 1) % 4], Ink.Pencil, 2f / zoom, 10f / zoom);
    }

    // ---- Flow overlay --------------------------------------------------------------------------

    /// <summary>Goods a minute past the end of a line, sampled every two seconds.</summary>
    float RateOf(TransportLine line)
    {
        if (!rates.TryGetValue(line.Id, out var r))
        {
            rates[line.Id] = (line.Passed, World.TickCount, 0f);
            return 0f;
        }
        long dt = World.TickCount - r.Tick;
        if (dt >= 2 * World.TicksPerSecond)
        {
            float perMinute = (line.Passed - r.Passed) * (60f * World.TicksPerSecond / dt);
            rates[line.Id] = (line.Passed, World.TickCount, perMinute);
            return perMinute;
        }
        return r.PerMinute;
    }

    void DrawOverlay(Rect2 view, List<Building> visible)
    {
        wash = 1f;
        foreach (var line in World.Lines)
        {
            if (!LineVisible(line, view))
                continue;
            if (detail > 0f)
                DrawFlow(line);
            if (line.Tiles.Count < 2 || zoom < 0.4f)
                continue;
            float rate = RateOf(line);
            var mid = line.Tiles[line.Tiles.Count / 2];
            Tag(Center(mid.Origin) + new Vector2(0, -Ink.Tile * 0.55f), Text.Get("OVERLAY_RATE", rate.ToString("F0", CultureInfo.InvariantCulture)), line.Stalled ? Ink.Red : Ink.Black);
        }
        foreach (var b in visible)
        {
            if (OverlayTagFor(b) is not { } tag)
                continue;
            var top = new Vector2(Footprint(b).GetCenter().X, Footprint(b).Position.Y - BodyHeight(b) * Ink.Tile - Ink.Tile * 0.35f);
            Tag(top, tag.Text, tag.Trouble ? Ink.Red : Ink.Black);
            if (tag.Wants is { } wants)
                DrawGood(wants, top + new Vector2(0, -Ink.Tile * 0.5f));
        }
    }

    /// <summary>
    /// The good parked at the head of a belt into this machine that the machine will not take, if any: no recipe of
    /// its uses it, and it is not coal for a firebox with room. Such a belt never moves again (the goods behind the
    /// lump cannot pass), so it is worth saying. A good the machine uses but has no room for is ordinary back-pressure.
    /// </summary>
    public static Item? Refused(World world, Machine m)
    {
        foreach (var cell in m.Cells)
            for (int k = 0; k < 4; k++)
            {
                var from = cell + ((Dir)k).Offset();
                if (world.BuildingAt(from) is not Belt { Line: { FrontParked: true, Front: { } good } line } belt || line.Tiles[^1] != belt)
                    continue;
                if (!m.Cells.Contains(belt.Exit))
                    continue;
                bool used = Recipes.Of(m.Type).Any(r => r.Needs(good));
                bool burns = good == Item.Coal && m.Firebox is { AcceptsCoal: true };
                if (!used && !burns)
                    return good;
            }
        return null;
    }

    /// <summary>A coal-fired building whose fire is out and that no shaft, steam main or wire drives.</summary>
    public static bool Cold(Firebox? firebox, PowerSource source) =>
        source is not (PowerSource.Shaft or PowerSource.Steam or PowerSource.Electric) && firebox is not { Lit: true };

    /// <summary>
    /// What the flow overlay prints over a building: its text, whether it is trouble (red), and the good a starved
    /// machine wants. A coal-fired machine or extractor gone cold prints FIRE OUT; an electric-only one NO POWER.
    /// </summary>
    public (string Text, bool Trouble, Item? Wants)? OverlayTagFor(Building b)
    {
        switch (b)
        {
            case Machine m when Refused(World, m) is { } refused:
                // A belt into it has stopped behind a good it will not take: that, not what it wants, is the trouble.
                return (refused == Item.Coal && m.Firebox != null ? Text.Get("OVERLAY_REFUSES_COAL") : Text.Get("OVERLAY_REFUSES", Text.Get("ITEM_" + Items.Id(refused)).ToLowerInvariant()), true, null);
            case Machine m:
                return m.State switch
                {
                    MachineState.Starved when m.Wants() is { } wants => (Text.Get("OVERLAY_WANTS", Text.Get("ITEM_" + Items.Id(wants)).ToLowerInvariant()), false, wants),
                    MachineState.Blocked => (Text.Get("OVERLAY_FULL"), true, null),
                    MachineState.Unpowered => (Text.Get(m.Def.Power == PowerNeed.Elec ? "OVERLAY_NO_POWER" : "OVERLAY_FIRE_OUT"), true, null),
                    _ => null,
                };
            case Mine mine:
                if (!mine.Lit)
                    return (Text.Get("OVERLAY_FIRE_OUT"), true, null);
                return (mine.OutputRate.ToString("P0", CultureInfo.InvariantCulture), mine.OutputRate < 0.5, null);
            case LoggingCamp camp:
                if (camp.Trees > 0 && Cold(camp.Firebox, camp.Source))
                    return (Text.Get("OVERLAY_FIRE_OUT"), true, null);
                return (camp.OutputRate.ToString("P0", CultureInfo.InvariantCulture), camp.OutputRate < 0.5, null);
            case PumpJack jack:
                if (Cold(jack.Firebox, jack.Source))
                    return (Text.Get("OVERLAY_FIRE_OUT"), true, null);
                double yield = jack.Patch == null ? 1 : World.PatchYield(jack.Patch);
                return (yield.ToString("P0", CultureInfo.InvariantCulture), yield < 0.5, null);
            case Terminal term:
                return (Text.Get("OVERLAY_YARD", term.Inbound.Count, term.Outbound.Count), term.Inbound.Count >= term.Cap, null);
            case Yard when World.Prestige.Current is { } commission:
                return (Text.Get("OVERLAY_COMMISSION", commission.LinesDone, commission.Bill.Count), false, null);
            case Boiler boiler:
                if (!boiler.Lit)
                    return (Text.Get("OVERLAY_FIRE_OUT"), true, null);
                var sn = World.Power.SteamNets.FirstOrDefault(n => n.Boilers.Contains(boiler));
                return (Pct(sn?.SatisfactionMilli ?? 0), sn is { SatisfactionMilli: < 1000 }, null);
            case PowerStation st:
                var en = World.Power.ElecNetOf(st);
                return (Pct(en?.SatisfactionMilli ?? 0), en is { SatisfactionMilli: < 1000 }, null);
            case Dam dam:
                var dn = World.Power.ElecNets.FirstOrDefault(n => n.Dams.Contains(dam));
                return (Pct(dn?.SatisfactionMilli ?? 0), dn is { SatisfactionMilli: < 1000 }, null);
            case Waterwheel wheel:
                int milli = wheel.DemandKw == 0 ? 1000 : (int)Mathf.Min(1000, wheel.SuppliedKw * 1000 / wheel.DemandKw);
                return (Pct(milli), milli < 1000, null);
        }
        return null;
    }

    static string Pct(int milli) => (milli / 1000.0).ToString("P0", CultureInfo.InvariantCulture);

    /// <summary>A small printed label on a scrap of paper, the same size on screen at any zoom.</summary>
    void Tag(Vector2 at, string text, Color ink)
    {
        int size = (int)Mathf.Clamp(12f / zoom, 12f, 48f);
        float width = Fell.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var box = new Rect2(at.X - width / 2 - 4 / zoom, at.Y - size * 0.75f, width + 8 / zoom, size * 1.05f);
        DrawRect(box, Ink.Paper with { A = 0.92f });
        DrawRect(box, ink with { A = 0.6f }, false, 1f / zoom);
        DrawString(Fell, new Vector2(box.Position.X + 4 / zoom, at.Y + size * 0.05f), text, HorizontalAlignment.Left, -1, size, ink);
    }
}
