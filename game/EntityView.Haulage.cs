using System.Collections.Generic;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>Roads, rail, signals, terminals and the fleet (M9), in the same engraved ink as everything else.</summary>
public partial class EntityView
{
    /// <summary>The first terminal clicked while a vehicle tool is in hand; ringed in pencil until the second click.</summary>
    public Cell? RouteStart { get; set; }

    static bool RoadJoins(Building? b) => b is Track { IsRoad: true } or Terminal { Kind: VehicleKind.Truck };
    static bool RailJoins(Building? b) => b is Track { IsRail: true } or Terminal { Kind: VehicleKind.Train };

    /// <summary>World position of a vehicle's head, between the two cells it is moving between.</summary>
    public static Vector2 VehiclePos(Vehicle v)
    {
        var (from, to, pos) = v.Head;
        return Center(from).Lerp(Center(to), pos / (float)BeltTiers.TileLength);
    }

    /// <summary>A point <paramref name="backSu"/> sub-units behind the head along the path (a train's cars).</summary>
    static Vector2 PathPoint(Vehicle v, int backSu)
    {
        var path = v.Path;
        if (path == null || path.Count < 2 || !v.Underway)
            return VehiclePos(v);
        int step = v.TowardB ? 1 : -1;
        int i = v.Index;
        int s = v.Head.PosSu;
        while (backSu > s)
        {
            int prev = i - step;
            if (prev < 0 || prev >= path.Count)
                return Center(path[i]);
            backSu -= s;
            i = prev;
            s = BeltTiers.TileLength;
        }
        int next = i + step;
        if (next < 0 || next >= path.Count)
            return Center(path[i]);
        return Center(path[i]).Lerp(Center(path[next]), (s - backSu) / (float)BeltTiers.TileLength);
    }

    /// <summary>Direction of travel at the head, as a unit vector; east when standing still.</summary>
    static Vector2 Heading(Vehicle v)
    {
        var (from, to, _) = v.Head;
        if (from == to)
        {
            if (v.Path is { Count: > 1 } p)
            {
                var a = v.TowardB ? p[0] : p[^1];
                var b = v.TowardB ? p[1] : p[^2];
                return (Center(b) - Center(a)).Normalized();
            }
            return Vector2.Right;
        }
        return (Center(to) - Center(from)).Normalized();
    }

    // ---- Track --------------------------------------------------------------------------------

    /// <summary>A dirt road: two soft edge lines toward every joined neighbour, with wheel ruts down the middle.</summary>
    void DrawRoad(Track road)
    {
        float t = Ink.Tile;
        var c = Center(road.Origin);
        int joints = 0;
        for (int d = 0; d < 4; d++)
        {
            var dir = (Dir)d;
            if (!RoadJoins(World.BuildingAt(road.Origin + dir.Offset())))
                continue;
            joints++;
            var v = V(dir);
            var p = new Vector2(-v.Y, v.X);
            if (detail <= 0f)
            {
                DrawLine(c, c + v * t * 0.5f, I(Ink.Soft), 3f / zoom);
                continue;
            }
            DrawLine(c + p * t * 0.34f, c + v * t * 0.5f + p * t * 0.34f, I(Ink.Black with { A = 0.55f }), 2f);
            DrawLine(c - p * t * 0.34f, c + v * t * 0.5f - p * t * 0.34f, I(Ink.Black with { A = 0.55f }), 2f);
            if (detail > 0f)
                for (int k = 0; k < 2; k++)
                {
                    float off = (k == 0 ? -1 : 1) * t * 0.12f;
                    DrawDashedLine(c + p * off + v * t * 0.1f, c + v * t * 0.5f + p * off, I(Ink.Faint with { A = 0.9f * Ink.Faint.A }), 1f, 4f);
                }
        }
        if (joints == 0)
        {
            var r = new Rect2(c - new Vector2(t * 0.34f, t * 0.34f), new Vector2(t * 0.68f, t * 0.68f));
            DrawRect(r, I(Ink.Soft), false, 1.5f);
        }
    }

    /// <summary>Two rails on sleepers toward every joined neighbour; a signal is a post with an arm, red while its block is held.</summary>
    void DrawRail(Track rail)
    {
        float t = Ink.Tile;
        var c = Center(rail.Origin);
        int joints = 0;
        for (int d = 0; d < 4; d++)
        {
            var dir = (Dir)d;
            if (!RailJoins(World.BuildingAt(rail.Origin + dir.Offset())))
                continue;
            joints++;
            var v = V(dir);
            var p = new Vector2(-v.Y, v.X);
            if (detail <= 0f)
            {
                DrawLine(c, c + v * t * 0.5f, I(Ink.Black), 2.5f / zoom);
                continue;
            }
            DrawLine(c + p * t * 0.16f, c + v * t * 0.5f + p * t * 0.16f, I(Ink.Black), 1.5f);
            DrawLine(c - p * t * 0.16f, c + v * t * 0.5f - p * t * 0.16f, I(Ink.Black), 1.5f);
            if (detail > 0f)
                for (int k = 0; k < 2; k++)
                {
                    var s = c + v * t * (0.15f + 0.25f * k);
                    DrawLine(s + p * t * 0.24f, s - p * t * 0.24f, I(Ink.Soft), 1.5f);
                }
        }
        if (joints == 0)
        {
            DrawLine(c - new Vector2(t * 0.4f, t * 0.16f), c + new Vector2(t * 0.4f, -t * 0.16f), I(Ink.Black), 1.5f);
            DrawLine(c - new Vector2(t * 0.4f, -t * 0.16f), c + new Vector2(t * 0.4f, t * 0.16f), I(Ink.Black), 1.5f);
        }
        if (!rail.Signal)
            return;
        bool held = false;
        for (int d = 0; d < 4 && !held; d++)
        {
            var n = rail.Origin + ((Dir)d).Offset();
            int block = World.Haulage.BlockOf(n);
            held = block >= 0 && World.Haulage.HolderOf(block) != null;
        }
        var foot = c + new Vector2(t * 0.3f, t * 0.28f);
        var top = foot - new Vector2(0, t * 0.7f);
        DrawLine(foot, top, I(Ink.Black), 2f);
        var arm = held ? top + new Vector2(-t * 0.28f, 0) : top + new Vector2(-t * 0.2f, -t * 0.2f);
        DrawLine(top, arm, I(held ? Ink.Red : Ink.Black), 3f);
        DrawCircle(top, t * 0.05f, I(Ink.Black));
    }

    // ---- Terminals ----------------------------------------------------------------------------

    /// <summary>A depot, station or landing: its block, a platform along the front, and the goods waiting in its yard.</summary>
    void DrawTerminal(Terminal term)
    {
        float t = Ink.Tile;
        bool busy = World.Haulage.Calling(term).Any(v => v.State != VehicleState.Parked);
        DrawBlock(term, busy ? MachineState.Working : MachineState.Starved, 1);
        if (detail <= 0f)
            return;
        // Waiting goods as small lumps along the near edge: inbound on the left, outbound on the right.
        var fp = Footprint(term);
        float y = fp.End.Y - t * 0.12f;
        int inbound = Mathf.Min(term.Inbound.Count, 6), outbound = Mathf.Min(term.Outbound.Count, 6);
        for (int k = 0; k < inbound; k++)
            DrawCircle(new Vector2(fp.Position.X + t * 0.45f + k * t * 0.13f, y), t * 0.05f, I(Ink.Black));
        for (int k = 0; k < outbound; k++)
            DrawCircle(new Vector2(fp.End.X - t * 0.45f - k * t * 0.13f, y), t * 0.05f, I(Ink.Soft));
    }

    // ---- The fleet ----------------------------------------------------------------------------

    void DrawVehicle(Vehicle v)
    {
        var pos = VehiclePos(v);
        var d = Heading(v);
        float angle = d.Angle();
        // Drawn a little larger than life as the view pulls back, so a truck stays a truck at any zoom.
        float scale = Mathf.Clamp(0.7f / zoom, 1f, 1.6f);
        float t = Ink.Tile * scale;
        float fill = v.Capacity == 0 ? 0 : Mathf.Clamp(v.Cargo.Count / (float)v.Capacity, 0f, 1f);
        var stopped = v.State == VehicleState.Waiting;
        switch (v.Kind)
        {
            case VehicleKind.Truck:
                DrawSetTransform(pos - new Vector2(0, t * 0.16f), angle, Vector2.One);
                // Bed with its load, cab in front, two wheels.
                var bed = new Rect2(-t * 0.36f, -t * 0.18f, t * 0.46f, t * 0.36f);
                DrawRect(bed, I(Ink.Paper));
                if (fill > 0)
                    DrawRect(new Rect2(bed.Position.X + 2, bed.Position.Y + 2, (bed.Size.X - 4) * fill, bed.Size.Y - 4), I(Ink.Black));
                DrawRect(bed, I(stopped ? Ink.Red : Ink.Black), false, 1.5f);
                var cab = new Rect2(t * 0.1f, -t * 0.14f, t * 0.24f, t * 0.28f);
                DrawRect(cab, I(Ink.Paper));
                DrawRect(cab, I(Ink.Black), false, 1.5f);
                DrawCircle(new Vector2(-t * 0.22f, t * 0.2f), t * 0.06f, I(Ink.Black));
                DrawCircle(new Vector2(t * 0.2f, t * 0.2f), t * 0.06f, I(Ink.Black));
                break;

            case VehicleKind.Train:
                // Cars first, from the back, so the locomotive prints over them.
                for (int k = 3; k >= 1; k--)
                {
                    int back = (int)(k * BeltTiers.TileLength * scale);
                    var at = PathPoint(v, back);
                    var ahead = PathPoint(v, back - BeltTiers.TileLength / 2);
                    float a = ahead == at ? angle : (ahead - at).Angle();
                    DrawSetTransform(at - new Vector2(0, t * 0.14f), a, Vector2.One);
                    var car = new Rect2(-t * 0.42f, -t * 0.16f, t * 0.84f, t * 0.32f);
                    DrawRect(car, I(Ink.Paper));
                    if (fill > 0)
                        DrawRect(new Rect2(car.Position.X + 2, car.Position.Y + 2, (car.Size.X - 4) * fill, car.Size.Y - 4), I(Ink.Soft));
                    DrawRect(car, I(Ink.Black), false, 1.5f);
                    DrawCircle(new Vector2(-t * 0.28f, t * 0.18f), t * 0.05f, I(Ink.Black));
                    DrawCircle(new Vector2(t * 0.28f, t * 0.18f), t * 0.05f, I(Ink.Black));
                }
                DrawSetTransform(pos - new Vector2(0, t * 0.16f), angle, Vector2.One);
                var loco = new Rect2(-t * 0.42f, -t * 0.18f, t * 0.84f, t * 0.36f);
                DrawRect(loco, I(Ink.Paper));
                DrawRect(loco, I(stopped ? Ink.Red : Ink.Black), false, 2f);
                DrawRect(new Rect2(-t * 0.4f, -t * 0.3f, t * 0.3f, t * 0.14f), I(Ink.Black)); // cab roof
                DrawRect(new Rect2(t * 0.18f, -t * 0.34f, t * 0.1f, t * 0.18f), I(Ink.Black)); // chimney
                for (int k = 0; k < 3; k++)
                    DrawCircle(new Vector2(-t * 0.25f + k * t * 0.25f, t * 0.2f), t * 0.07f, I(Ink.Black));
                if (v.State == VehicleState.Moving)
                {
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    // Smoke drifts up and back from the stack.
                    float phase = (float)(World.TickCount % 90) / 90f;
                    var stack = pos - new Vector2(0, t * 0.16f) + d * t * 0.23f + new Vector2(0, -t * 0.34f);
                    for (int k = 0; k < 3; k++)
                    {
                        float f = (phase + k / 3f) % 1f;
                        var puff = stack - d * t * 0.5f * f + new Vector2(0, -t * 0.55f * f);
                        DrawArc(puff, t * (0.06f + 0.1f * f), 0, Mathf.Tau, 10, I(Ink.Soft with { A = Ink.Soft.A * (1 - f) }), 1.2f);
                    }
                }
                break;

            default:
                DrawSetTransform(pos, angle, Vector2.One);
                // A flat hull, pointed at the bow, with a cargo hatch.
                var hull = new[]
                {
                    new Vector2(t * 0.8f, 0), new Vector2(t * 0.55f, -t * 0.28f), new Vector2(-t * 0.7f, -t * 0.28f),
                    new Vector2(-t * 0.8f, 0), new Vector2(-t * 0.7f, t * 0.28f), new Vector2(t * 0.55f, t * 0.28f),
                };
                DrawColoredPolygon(hull, I(Ink.Paper));
                DrawPolyline(hull.Append(hull[0]).ToArray(), I(stopped ? Ink.Red : Ink.Black), 1.5f);
                var hatch = new Rect2(-t * 0.5f, -t * 0.16f, t * 0.9f, t * 0.32f);
                if (fill > 0)
                    DrawRect(new Rect2(hatch.Position, new Vector2(hatch.Size.X * fill, hatch.Size.Y)), I(Ink.Black));
                DrawRect(hatch, I(Ink.Black), false, 1f);
                break;
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>With a vehicle tool in hand: the first terminal chosen is ringed, and a pencil line runs to the cursor.</summary>
    void DrawRouteSketch()
    {
        if (RouteStart is not { } start || World.BuildingAt(start) is not Terminal term)
            return;
        var fp = Footprint(term).Grow(6);
        var c = fp.GetCenter();
        var pts = new Vector2[33];
        for (int i = 0; i <= 32; i++)
        {
            float a = Mathf.Tau * i / 32;
            float wobble = 1f + 0.04f * Mathf.Sin(a * 4.3f);
            pts[i] = c + new Vector2(Mathf.Cos(a) * fp.Size.X / 2 * wobble, Mathf.Sin(a) * fp.Size.Y / 2 * wobble);
        }
        DrawPolyline(pts, Ink.Red with { A = 0.85f }, 2.5f / zoom);
        if (HoverCell is { } h)
            DrawDashedLine(c, Center(h), Ink.Pencil, 2f / zoom, 10f / zoom);
    }
}
