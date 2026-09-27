using System.Collections.Generic;
using Godot;

namespace FrontPageFoundry;

/// <summary>The printer's palette and the engraving strokes everything is drawn with.</summary>
public static class Ink
{
    public static readonly Color Paper = new(0.937f, 0.906f, 0.839f);
    public static readonly Color Black = new(0.118f, 0.102f, 0.086f);
    public static readonly Color Soft = new(0.118f, 0.102f, 0.086f, 0.55f);
    public static readonly Color Faint = new(0.118f, 0.102f, 0.086f, 0.10f);
    public static readonly Color Pencil = new(0.33f, 0.31f, 0.29f, 0.8f);
    /// <summary>The one spot colour, used only for trouble.</summary>
    public static readonly Color Red = new(0.66f, 0.2f, 0.16f);

    /// <summary>Pixels per tile at 1× zoom (GDD §11).</summary>
    public const int Tile = 64;

    /// <summary>Diagonal engraving hatch clipped to a rectangle.</summary>
    public static void Hatch(CanvasItem c, Rect2 r, float spacing, Color color, float width = 1f, bool rising = true)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        var pts = new List<Vector2>();
        if (rising)
        {
            // Lines x + y = k.
            for (float k = x0 + y0 + spacing; k < x1 + y1; k += spacing)
            {
                var a = new Vector2(Mathf.Max(x0, k - y1), 0);
                a.Y = k - a.X;
                var b = new Vector2(Mathf.Min(x1, k - y0), 0);
                b.Y = k - b.X;
                pts.Add(a);
                pts.Add(b);
            }
        }
        else
        {
            // Lines x - y = k.
            for (float k = x0 - y1 + spacing; k < x1 - y0; k += spacing)
            {
                var a = new Vector2(Mathf.Max(x0, k + y0), 0);
                a.Y = a.X - k;
                var b = new Vector2(Mathf.Min(x1, k + y1), 0);
                b.Y = b.X - k;
                pts.Add(a);
                pts.Add(b);
            }
        }
        // One draw call for the whole hatch.
        if (pts.Count >= 2)
            c.DrawMultiline(pts.ToArray(), color, width);
    }

    /// <summary>Vertical shading strokes, for the shadowed front face of a building.</summary>
    public static void Strokes(CanvasItem c, Rect2 r, float spacing, Color color, float width = 1f)
    {
        for (float x = r.Position.X + spacing * 0.5f; x < r.End.X; x += spacing)
            c.DrawLine(new Vector2(x, r.Position.Y), new Vector2(x, r.End.Y), color, width);
    }

    /// <summary>Stable pseudo-random number in [0,1) for decorative jitter.</summary>
    public static float Jitter(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
        h = (h ^ (h >> 15)) * 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
