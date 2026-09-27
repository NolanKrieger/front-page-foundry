using System.Collections.Generic;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// The soundscape (GDD §12, M12): three original rags on a gramophone that gain a banjo and a cornet as
/// the works grows, the press and the telegraph and the typewriter for the paper's moments, and the
/// machines, belts, steam and the wheel heard where the camera is, thinned out as the view pulls back.
/// Every file comes from <c>tools/audio/build.py</c>; nothing here is licensed from anyone.
/// </summary>
public partial class Audio : Node
{
    const string Dir = "res://assets/audio/";
    static readonly string[] Rags = { "rag_courier", "rag_carvell", "rag_foundry" };
    static readonly string[] Stems = { "piano", "banjo", "cornet" };
    static readonly string[] OneShots = { "press", "telegraph", "typewriter", "bell", "pencil", "thud", "rustle", "truck", "whistle", "horn" };
    static readonly string[] Loops = { "belt", "machine", "steam", "wheel" };
    /// <summary>Buildings that bring in the banjo, then the cornet.</summary>
    public const int BanjoAt = 40, CornetAt = 150;
    const float NearZoom = 0.6f, FarZoom = 0.35f;
    const int MaxLoops = 8;

    readonly Dictionary<string, AudioStream> bank = new();
    readonly AudioStreamPlayer[] stems = new AudioStreamPlayer[3];
    readonly List<AudioStreamPlayer> shots = new();
    readonly Dictionary<int, AudioStreamPlayer2D> loops = new();
    AudioStreamPlayer belt = null!;
    World world = null!;
    Camera2D camera = null!;
    int rag;
    double gap;
    int musicBus, sfxBus;

    public int Loaded => bank.Count;
    /// <summary>1 = piano alone, 2 = with banjo, 3 = the full combo.</summary>
    public int Layers { get; private set; } = 1;
    public string? LastPlayed { get; private set; }
    public int Played { get; private set; }
    public bool MusicPlaying => stems[0] != null && stems[0].Playing;
    public string CurrentRag => Rags[rag];
    public int LoopsPlaying => loops.Count;

    public void Init(World world, Camera2D camera, bool mute)
    {
        this.world = world;
        this.camera = camera;
        musicBus = Bus("Music", -9f);
        sfxBus = Bus("SFX", -3f);
        if (mute)
            AudioServer.SetBusMute(0, true);

        foreach (var r in Rags)
            foreach (var s in Stems)
                Load($"{r}_{s}", loop: false);
        foreach (var s in OneShots)
            Load("sfx_" + s, loop: false);
        foreach (var s in Loops)
            Load("sfx_" + s, loop: true);

        for (int i = 0; i < stems.Length; i++)
        {
            stems[i] = new AudioStreamPlayer { Bus = "Music", VolumeDb = i == 0 ? 0f : -60f };
            AddChild(stems[i]);
        }
        belt = new AudioStreamPlayer { Bus = "SFX", VolumeDb = -60f };
        if (bank.TryGetValue("sfx_belt", out var rattle))
            belt.Stream = rattle;
        AddChild(belt);
        StartRag(0);
    }

    /// <summary>The named bus, made once: the audio server outlives a scene reload (a new company, a load), so it is reused, not added again.</summary>
    static int Bus(string name, float db)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index >= 0)
            return index;
        index = AudioServer.BusCount;
        AudioServer.AddBus();
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusVolumeDb(index, db);
        return index;
    }

    void Load(string name, bool loop)
    {
        string path = Dir + name + ".ogg";
        if (!ResourceLoader.Exists(path))
            return;
        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = loop;
        bank[name] = stream;
    }

    void StartRag(int index)
    {
        rag = index;
        for (int i = 0; i < stems.Length; i++)
        {
            if (!bank.TryGetValue($"{Rags[rag]}_{Stems[i]}", out var stream))
                continue;
            stems[i].Stream = stream;
            stems[i].Play();
        }
    }

    /// <summary>Plays a one-shot by name (press, telegraph, typewriter, bell, pencil, thud, rustle, truck, whistle, horn).</summary>
    public void Play(string name)
    {
        LastPlayed = name;
        Played++;
        if (!bank.TryGetValue("sfx_" + name, out var stream))
            return;
        var player = shots.FirstOrDefault(p => !p.Playing);
        if (player == null)
        {
            if (shots.Count >= 12)
                return;
            player = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(player);
            shots.Add(player);
        }
        player.Stream = stream;
        player.Play();
    }

    public override void _Process(double delta)
    {
        if (world == null)
            return;
        UpdateMusic(delta);
        UpdateLoops();
    }

    /// <summary>Next record after a short pause; the combo fills out with the works.</summary>
    void UpdateMusic(double delta)
    {
        if (!stems[0].Playing)
        {
            gap += delta;
            if (gap > 6)
            {
                gap = 0;
                StartRag((rag + 1) % Rags.Length);
            }
        }
        int count = world.Buildings.Count;
        Layers = count >= CornetAt ? 3 : count >= BanjoAt ? 2 : 1;
        for (int i = 0; i < stems.Length; i++)
        {
            float target = i < Layers ? 0f : -60f;
            stems[i].VolumeDb = Mathf.MoveToward(stems[i].VolumeDb, target, (float)delta * 20f);
        }
    }

    /// <summary>How loud the works is at this zoom: silent pulled right out, full when close.</summary>
    float Nearness() => Mathf.Clamp((camera.Zoom.X - FarZoom) / (NearZoom - FarZoom), 0f, 1f);

    static float Db(float gain) => gain <= 0.001f ? -60f : Mathf.LinearToDb(gain);

    /// <summary>Up to eight working machines nearest the camera hum; the belts in view rattle; a lit boiler hisses; a wheel creaks.</summary>
    void UpdateLoops()
    {
        float near = Nearness();
        var centre = camera.Position;
        var wanted = new List<(Building b, string sound, float dist)>();
        if (near > 0)
            foreach (var b in world.Buildings)
            {
                string? sound = b switch
                {
                    Machine { State: MachineState.Working } => "machine",
                    Mine { Buffered: < Mine.BufferCap } m when m.Lit => "machine",
                    Boiler { Lit: true } => "steam",
                    Waterwheel { DemandKw: > 0 } => "wheel",
                    _ => null,
                };
                if (sound == null)
                    continue;
                float dist = EntityView.Center(b.Origin).DistanceTo(centre);
                if (dist < 16 * Ink.Tile)
                    wanted.Add((b, sound, dist));
            }
        wanted.Sort((x, y) => x.dist.CompareTo(y.dist));
        var keep = new HashSet<int>();
        foreach (var (b, sound, _) in wanted.Take(MaxLoops))
        {
            keep.Add(b.Id);
            if (!loops.TryGetValue(b.Id, out var player))
            {
                if (!bank.TryGetValue("sfx_" + sound, out var stream))
                    continue;
                player = new AudioStreamPlayer2D { Bus = "SFX", Stream = stream, MaxDistance = 14 * Ink.Tile, Attenuation = 1.4f, Position = EntityView.Center(b.Origin) };
                AddChild(player);
                player.Play((float)GD.RandRange(0, 1.5));
                loops[b.Id] = player;
            }
            player.VolumeDb = Db(near * (sound == "machine" ? 0.5f : 0.4f));
        }
        foreach (var id in loops.Keys.ToList())
            if (!keep.Contains(id))
            {
                loops[id].QueueFree();
                loops.Remove(id);
            }
        int moving = 0;
        foreach (var line in world.Lines)
            if (line.Count > 0 && !line.Stalled && LineNear(line, centre))
                moving++;
        float rattle = near * Mathf.Min(1f, moving / 6f) * 0.35f;
        if (rattle > 0.001f && !belt.Playing && belt.Stream != null)
            belt.Play();
        belt.VolumeDb = Mathf.MoveToward(belt.VolumeDb, Db(rattle), 60f * (float)GetProcessDeltaTime());
        if (belt.VolumeDb <= -59f && belt.Playing)
            belt.Stop();
    }

    static bool LineNear(TransportLine line, Vector2 centre)
    {
        float x = Mathf.Clamp(centre.X, line.Min.X * Ink.Tile, (line.Max.X + 1) * Ink.Tile);
        float y = Mathf.Clamp(centre.Y, line.Min.Y * Ink.Tile, (line.Max.Y + 1) * Ink.Tile);
        return new Vector2(x, y).DistanceTo(centre) < 16 * Ink.Tile;
    }
}
