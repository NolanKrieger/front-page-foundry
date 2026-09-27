using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace FrontPageFoundry;

/// <summary>Player settings (GDD §13): volumes and mute, kept as JSON under <c>user://</c>.</summary>
public sealed class Settings
{
    public const float MinDb = -40f, MaxDb = 6f;

    public static string Path => ProjectSettings.GlobalizePath(Companies.UserRoot + "settings.json");

    public float MusicDb { get; set; } = -9f;
    public float SfxDb { get; set; } = -3f;
    public bool Mute { get; set; }

    /// <summary>Muted whatever the player chose (self-test, screenshots, --mute); moving a slider never lifts it.</summary>
    [JsonIgnore] public bool ForcedMute { get; set; }
    /// <summary>False for the self-test, which moves the sliders but must not write the player's own file.</summary>
    [JsonIgnore] public bool Persist { get; set; } = true;
    /// <summary>How many times the file was written this session (for the self-test).</summary>
    [JsonIgnore] public int Writes { get; private set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) is { } s)
            {
                // A hand-edited or damaged file never leaves a bus at a volume the sliders cannot show.
                s.MusicDb = Clamp(s.MusicDb, -9f);
                s.SfxDb = Clamp(s.SfxDb, -3f);
                return s;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PushWarning($"settings not read ({e.Message}); using the defaults");
        }
        return new Settings();
    }

    static float Clamp(float db, float fallback) => float.IsFinite(db) ? Math.Clamp(db, MinDb, MaxDb) : fallback;

    /// <summary>How many times something asked for the settings to be kept (for the self-test).</summary>
    [JsonIgnore] public int SaveRequests { get; private set; }

    public void Save()
    {
        SaveRequests++;
        if (!Persist)
            return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            Writes++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"settings not saved: {e.Message}");
        }
    }

    /// <summary>Pushes the volumes onto the audio buses. A forced mute (self-test, screenshots) stays on regardless.</summary>
    public void Apply()
    {
        int music = AudioServer.GetBusIndex("Music"), sfx = AudioServer.GetBusIndex("SFX");
        if (music >= 0)
            AudioServer.SetBusVolumeDb(music, MusicDb);
        if (sfx >= 0)
            AudioServer.SetBusVolumeDb(sfx, SfxDb);
        AudioServer.SetBusMute(0, Mute || ForcedMute);
    }
}
