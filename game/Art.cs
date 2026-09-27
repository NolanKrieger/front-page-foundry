using System.Collections.Generic;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// Engraved sprites from <c>assets/art/</c>, made by <c>tools/art/process.py</c> from Codex
/// generations (GDD §11). Everything is optional: a missing sprite means the procedural ink
/// drawing is used, so the game never breaks while the plates are still being cut.
/// Sprites are stored at 128 px per tile (2× the design size) and drawn at half size.
/// </summary>
public static class Art
{
    /// <summary>Stored pixels per tile.</summary>
    public const int PxPerTile = 128;

    static readonly Dictionary<string, Texture2D?> cache = new();

    public static Texture2D? Building(BuildingDef def) => Load($"res://assets/art/buildings/{def.Id}.png");
    public static Texture2D? Good(Item item) => Load($"res://assets/art/goods/{Items.Id(item)}.png");
    public static Texture2D? Terrain(string id) => Load($"res://assets/art/terrain/{id}.png");

    static Texture2D? Load(string path)
    {
        if (cache.TryGetValue(path, out var tex))
            return tex;
        tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        cache[path] = tex;
        return tex;
    }
}
