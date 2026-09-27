using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// Owns the world and turns input into sim commands. The sim advances once per physics
/// tick (60/s), always in real time.
///
/// Command-line extras (after `--`): `--demo` builds a small working line, `--zoom=0.4` sets
/// the starting zoom, `--screenshot=path.png [--frames=N]` saves the screen after N frames and
/// quits, `--selftest` drives the controls and exits 0 or 1, `--bench[=loops]` fills the map
/// with moving goods and prints measured UPS and FPS after `--frames` frames.
/// </summary>
public partial class Main : Node2D
{
    const float MinZoom = 0.25f, MaxZoom = 2.5f, PanSpeed = 900f;

    World world = null!;
    Camera2D camera = null!;
    EntityView entities = null!;
    TerrainLayer terrain = null!;
    Hud hud = null!;
    PaperView paper = null!;
    Audio audio = null!;
    Menu menu = null!;
    Settings settings = null!;
    readonly Companies companies = new();
    readonly Achievements achievements = new();
    int editionsSeen, telegramsSeen;
    long lastSavedDay = -1, lastPollTick;
    bool forcedMute;

    /// <summary>Set before a scene reload: the company folder to open, or the new company to found.</summary>
    public static string? PendingLoad;
    public static (string Name, Difficulty Difficulty)? PendingNew;

    // What the hand holds.
    BuildingType? tool;
    Dir facing = Dir.East;
    Cell? bridgeEntry;
    Item? heldFilter;
    /// <summary>Vehicle tool: the first terminal clicked, and how the last two-click route went.</summary>
    Cell? routeStart;
    PlaceResult? routeResult;
    /// <summary>Blueprints (GDD §3): the one in hand, the nine saved slots, and the rectangle being dragged out.</summary>
    List<BlueprintPiece>? held;
    readonly List<BlueprintPiece>?[] slots = new List<BlueprintPiece>?[9];
    bool blueprintMode, selecting;
    Cell selectStart, selectEnd;

    bool laying, removing, dragging;
    Cell lastCell;
    /// <summary>The tool a belt/pipe/road/rail drag was started with; the drag ends if the hand changes.</summary>
    BuildingType dragType;
    /// <summary>One drag (or click) is one undo step: false until its first real change opens the step.</summary>
    bool dragStepOpen;
    /// <summary>The red line printed at the head of the front office this session (a recovered or unreadable ledger).</summary>
    string? officeNotice;
    /// <summary>Last mouse position seen in an event (the OS cursor is not consulted, so synthetic input works).</summary>
    Vector2 mouse;

    string? screenshotPath;
    int screenshotFrames = 240, frame;
    bool bench, panBench;
    float panSpeed = 900f;
    readonly Stopwatch tickClock = new(), wallClock = new();
    long benchTicks;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        bool demo = args.Contains("--demo");
        int benchLoops = 0, benchSide = 32;
        foreach (var arg in args)
        {
            if (arg == "--bench")
                benchLoops = 40;
            else if (arg.StartsWith("--bench="))
                benchLoops = int.Parse(arg["--bench=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--benchside="))
                benchSide = int.Parse(arg["--benchside=".Length..], CultureInfo.InvariantCulture);
        }
        bench = benchLoops > 0;
        bool selftest = args.Contains("--selftest");
        bool scratch = demo || bench || selftest;
        if (args.Any(a => a.StartsWith("--playtest")))
            BeginPlaytest(args);
        // A real company: the one asked for by the front office, else the last one played, else a fresh one.
        bool firstRun = false, autoLoaded = false;
        string? unreadable = null;
        World? loaded = null;
        if (scratch)
        {
            loaded = new World(seed: 1920, startingCashCents: demo || bench ? 1_000_000_000 : null, mode: bench ? MapMode.Flat : MapMode.Full);
        }
        else if (PendingLoad is { } loadDir)
        {
            PendingLoad = null;
            loaded = TryLoad(loadDir, ref unreadable);
        }
        else if (PendingNew is { } fresh)
        {
            PendingNew = null;
            companies.AdoptNew(fresh.Name);
            loaded = new World(seed: (int)(Time.GetUnixTimeFromSystem() % int.MaxValue), difficulty: fresh.Difficulty);
        }
        else if (Companies.List() is { Count: > 0 } known)
        {
            loaded = TryLoad(known[0].Dir, ref unreadable);
            autoLoaded = true;
        }
        else
        {
            companies.AdoptNew(Companies.DefaultName);
            loaded = new World(seed: 1920);
            firstRun = true;
        }
        if (loaded == null)
        {
            // The books could not be read: a scratch works stands behind the front office, which lists the other companies.
            companies.Forget();
            loaded = new World(seed: 1920);
        }
        world = loaded;
        if (unreadable != null)
            officeNotice = Text.Get("MENU_UNREADABLE", unreadable);
        else if (companies.RecoveredFrom is { } backup)
            officeNotice = Text.Get("MENU_RECOVERED", backup);
        achievements.Attach(companies.Dir);
        lastSavedDay = world.Day;

        var paperLayer = new CanvasLayer { Layer = -10 };
        var paperRect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        paperRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        paperRect.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/paper.gdshader") };
        paperLayer.AddChild(paperRect);
        AddChild(paperLayer);

        terrain = new TerrainLayer();
        terrain.Init(world);
        AddChild(terrain);

        entities = new EntityView { World = world };
        AddChild(entities);

        // The press: a post-process over the world layers only; the newspaper HUD above stays crisp.
        var pressLayer = new CanvasLayer { Layer = 5 };
        var press = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        press.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        press.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/press.gdshader") };
        pressLayer.AddChild(press);
        AddChild(pressLayer);
        pressLayer.Visible = !args.Contains("--nopress");

        // Centre on the starter patch, nudged so it sits between the masthead and the ads.
        camera = new Camera2D { Position = new Vector2(10.5f, -4f) * Ink.Tile + new Vector2(0, 25) };
        AddChild(camera);
        camera.MakeCurrent();

        hud = new Hud();
        AddChild(hud);
        hud.Init(world);
        hud.AdClicked += Choose;

        // Sound: muted for the self-test and review captures so a test run never plays through the desk.
        forcedMute = args.Contains("--mute") || selftest || playtest != null || args.Any(a => a.StartsWith("--screenshot="));
        audio = new Audio();
        AddChild(audio);
        audio.Init(world, camera, mute: forcedMute);
        settings = Settings.Load();
        // The self-test moves the sliders to prove them; it never writes the player's own settings file.
        settings.Persist = !selftest;
        settings.ForcedMute = forcedMute;
        settings.Apply();
        telegramsSeen = world.Paper.Telegrams.Count;

        // The front office: Esc with an empty hand; the only pause.
        menu = new Menu();
        AddChild(menu);
        menu.Init(settings);
        menu.ContinueRequested += () => menu.Close();
        menu.NewCompanyRequested += FoundCompany;
        menu.LoadRequested += dir =>
        {
            SaveIfPlayed();
            PendingLoad = dir;
            GetTree().ReloadCurrentScene();
        };
        menu.QuitRequested += () =>
        {
            SaveIfPlayed();
            GetTree().Quit();
        };
        companies.Failed += reason => hud.Flash(Text.Get("SAVE_FAILED", reason), Time.GetTicksMsec() / 1000.0);
        achievements.Unlocked += id =>
        {
            hud.Flash(Text.Get("ACH_PINNED", Text.Get("ACH_" + id)), Time.GetTicksMsec() / 1000.0);
            audio.Play("bell");
        };
        if (unreadable != null)
            menu.Open(OfficeLine(), officeNotice, MenuPage.Companies);
        else if (firstRun)
            menu.Open(Text.Get("MENU_FIRST_RUN"), null, MenuPage.NewCompany);
        else if (officeNotice != null || autoLoaded && world.Ended)
            // A recovered ledger says so; a company that folded last time opens on the office, where a new one is founded.
            menu.Open(OfficeLine(), officeNotice);
        else if (args.Contains("--office") || args.Contains("--office=new"))
            menu.Open(OfficeLine(), officeNotice, args.Contains("--office=new") ? MenuPage.NewCompany : MenuPage.Front);

        paper = new PaperView();
        AddChild(paper);
        paper.Init(world);
        paper.AdClicked += type =>
        {
            Choose(type);
            paper.Close();
        };

        foreach (var arg in args)
        {
            if (arg.StartsWith("--screenshot="))
            {
                // Full screen, so a review capture is the same 1920×1080 whatever KWin would tile the window to.
                screenshotPath = arg["--screenshot=".Length..];
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
            else if (arg.StartsWith("--frames="))
                screenshotFrames = int.Parse(arg["--frames=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--zoom="))
                camera.Zoom = Vector2.One * float.Parse(arg["--zoom=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--at="))
            {
                // Camera over a cell, e.g. --at=0,26 for the town.
                var parts = arg["--at=".Length..].Split(',');
                camera.Position = new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)) * Ink.Tile;
            }
            else if (arg == "--panbench")
            {
                // Pans across fresh ground for --frames frames and reports FPS and chunks drawn.
                panBench = true;
                wallClock.Start();
            }
            else if (arg.StartsWith("--panspeed="))
                panSpeed = float.Parse(arg["--panspeed=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--overlay")
                entities.Overlay = true;
        }

        if (demo)
            BuildDemo();
        if (bench)
            BuildBench(benchLoops, benchSide);
        foreach (var arg in args)
            if (arg.StartsWith("--fastforward="))
            {
                // Seconds of play before the first frame, for screenshots of a company with history.
                int seconds = int.Parse(arg["--fastforward=".Length..], CultureInfo.InvariantCulture);
                for (int i = 0; i < seconds * World.TicksPerSecond; i++)
                    world.Tick();
                editionsSeen = world.Paper.Editions.Count;
            }
        foreach (var arg in args)
            if (arg.StartsWith("--paper="))
            {
                paper.Open();
                paper.Show(System.Enum.Parse<Page>(arg["--paper=".Length..], true));
            }
        if (args.Contains("--selftest"))
            CallDeferred(MethodName.RunSelfTest);
    }

    /// <summary>Loads a company folder; a ledger that cannot be read at all is reported, never thrown at the player.</summary>
    World? TryLoad(string dir, ref string? unreadable)
    {
        try
        {
            return companies.Load(dir);
        }
        catch (System.Exception e)
        {
            GD.PushError($"could not read the company in {dir}: {e.Message}");
            unreadable = Companies.NameOf(dir);
            return null;
        }
    }

    /// <summary>The founding page's "Found it": the books are kept, and the scene reloads on the new company.</summary>
    void FoundCompany(string name, Difficulty difficulty)
    {
        SaveIfPlayed();
        PendingNew = (name, difficulty);
        GetTree().ReloadCurrentScene();
    }

    /// <summary>A world anything has happened in: a tick run or a command given.</summary>
    static bool Played(World w) => w.TickCount > 0 || w.Log.Count > 0;

    /// <summary>
    /// Saves the company unless nothing has happened in it yet: a works nobody has played (the first run's founding
    /// page, say) is not worth a folder, and saving it would leave a company on the books the player never founded.
    /// </summary>
    void SaveIfPlayed()
    {
        settings?.Save();
        if (Played(world))
            companies.Save(world);
    }

    /// <summary>The line under the front office's title: whose books these are.</summary>
    string OfficeLine() => companies.Name == null ? Text.Get("MENU_SCRATCH")
        : world.Ended ? Text.Get("MENU_COMPANY_FOLDED", companies.Name)
        : Text.Get("MENU_COMPANY", companies.Name);

    void OpenOffice()
    {
        CancelDrags();
        menu.Open(OfficeLine(), officeNotice);
    }

    void OpenPaper()
    {
        CancelDrags();
        paper.Open();
        audio.Play("rustle");
    }

    /// <summary>
    /// Ends whatever the mouse was dragging. Called when the paper or the office opens over the works (their button
    /// release never reaches the works) and when the window loses focus, so no drag outlives its button.
    /// </summary>
    void CancelDrags()
    {
        laying = removing = dragging = selecting = false;
    }

    /// <summary>Two mines feeding one line through a splitter, over a bridge, into two depots.</summary>
    void BuildDemo()
    {
        world.Apply(new Place(BuildingType.MineHead, new Cell(5, -5), Dir.East));
        world.Apply(new Place(BuildingType.MineHead, new Cell(8, -7), Dir.South));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(8, -5), Dir.South));
        for (int x = 7; x <= 10; x++)
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(x, -4), Dir.East));
        world.Apply(new Place(BuildingType.Splitter, new Cell(11, -4), Dir.East));
        world.Apply(new Place(BuildingType.BeltRubber, new Cell(12, -4), Dir.East));
        world.Apply(new Place(BuildingType.Trestle, new Cell(13, -4), Dir.East, 2));
        world.Apply(new Place(BuildingType.BeltRubber, new Cell(17, -4), Dir.East));
        world.Apply(new Place(BuildingType.FreightDepot, new Cell(18, -5), Dir.East));
        // The bottom lane feeds a smelter; its ingots run south to a press, then a depot.
        for (int x = 12; x <= 13; x++)
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(x, -3), Dir.East));
        world.Apply(new Place(BuildingType.Smelter, new Cell(14, -3), Dir.South));       // fed at (14,-3); port (14,-1)
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(14, -1), Dir.South));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(14, 0), Dir.South));
        world.Apply(new Place(BuildingType.Press, new Cell(14, 1), Dir.South));          // fed at (14,1); port (14,3)
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(14, 3), Dir.South));
        world.Apply(new Place(BuildingType.FreightDepot, new Cell(14, 4), Dir.South));
        // A coke oven and an open hearth stand ready for the steel chain, starved until coal and limestone arrive.
        world.Apply(new Place(BuildingType.CokeOven, new Cell(18, 0), Dir.East));
        world.Apply(new Place(BuildingType.OpenHearthFurnace, new Cell(21, -1), Dir.East));
        // Down on the river: a waterwheel driving a smelter, a pump and boiler on a short main, and poles from a station.
        var bank = world.FlatBankNear(24, 16, 10);
        world.Apply(new Place(BuildingType.Waterwheel, new Cell(bank.X, bank.Y - 1), Dir.North));
        world.Apply(new Place(BuildingType.Smelter, new Cell(bank.X + 1, bank.Y - 2), Dir.East));
        world.Apply(new Place(BuildingType.FreightDepot, new Cell(bank.X + 3, bank.Y - 2), Dir.East));
        world.Apply(new Place(BuildingType.WaterPump, new Cell(bank.X - 2, bank.Y), Dir.North));
        world.Apply(new Place(BuildingType.SteamPipe, new Cell(bank.X - 2, bank.Y - 1), Dir.North));
        world.Apply(new Place(BuildingType.SteamPipe, new Cell(bank.X - 2, bank.Y - 2), Dir.North));
        world.Apply(new Place(BuildingType.SteamPipe, new Cell(bank.X - 3, bank.Y - 2), Dir.West));
        world.Apply(new Place(BuildingType.Boiler, new Cell(bank.X - 5, bank.Y - 3), Dir.North));
        world.Apply(new Place(BuildingType.PowerStation, new Cell(bank.X - 8, bank.Y - 4), Dir.North));
        world.Apply(new Place(BuildingType.PowerPole, new Cell(bank.X - 6, bank.Y - 7), Dir.North));
        world.Apply(new Place(BuildingType.PowerPole, new Cell(bank.X - 1, bank.Y - 8), Dir.North));
        // A crossing line under the bridge.
        for (int y = -6; y <= -2; y++)
            world.Apply(new Place(BuildingType.BeltSteel, new Cell(15, y), Dir.South));
        // Haulage (M9): coal from the west seam by truck to a rail station, by train to a depot; a barge on the river.
        world.Apply(new Place(BuildingType.MineHead, new Cell(-10, 5), Dir.East));           // out (-8,6)
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-8, 6), Dir.East));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-7, 6), Dir.East));
        world.Apply(new Place(BuildingType.TruckDepot, new Cell(-6, 5), Dir.East));          // fed at (-6,6); gates (-6,4) (-5,4)
        for (int x = -30; x <= -5; x++)
            world.Apply(new Place(BuildingType.Road, new Cell(x, 4), Dir.East));
        world.Apply(new Place(BuildingType.TruckDepot, new Cell(-32, 3), Dir.North));        // gate (-30,4); port (-31,2)
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-31, 2), Dir.North));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-31, 1), Dir.North));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-31, 0), Dir.North));
        world.Apply(new Place(BuildingType.RailStation, new Cell(-32, -3), Dir.East));      // fed at (-31,-1); gates y=-4
        for (int x = -32; x <= 1; x++)
            world.Apply(new Place(x == -16 ? BuildingType.RailSignal : BuildingType.Rail, new Cell(x, -4), Dir.East));
        world.Apply(new Place(BuildingType.RailStation, new Cell(-1, -7), Dir.East));       // port (2,-6)
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(2, -6), Dir.East));
        world.Apply(new Place(BuildingType.BeltCanvas, new Cell(3, -6), Dir.East));
        world.Apply(new Place(BuildingType.FreightDepot, new Cell(4, -7), Dir.East));
        world.Apply(new Assign(BuildingType.MotorTruck, new Cell(-6, 5), new Cell(-32, 3)));
        world.Apply(new Assign(BuildingType.Locomotive, new Cell(-32, -3), new Cell(-1, -7)));
        var quay = world.FlatBankNear(bank.X + 12, 4, 4);
        var farQuay = world.FlatBankNear(bank.X + 44, 4, 4);
        world.Apply(new Place(BuildingType.BargeLanding, new Cell(quay.X - 1, quay.Y - 1), Dir.East));
        world.Apply(new Place(BuildingType.BargeLanding, new Cell(farQuay.X - 1, farQuay.Y - 1), Dir.East));
        if (world.BuildingAt(new Cell(quay.X - 1, quay.Y - 1)) is Terminal landing)
            for (int k = 0; k < 80; k++)
                landing.TryAccept(world, Item.Coal, Dir.East, 60, landing.Origin);
        world.Apply(new Assign(BuildingType.Barge, new Cell(quay.X - 1, quay.Y - 1), new Cell(farQuay.X - 1, farQuay.Y - 1)));
        // Pencilled in, not built: an open hearth with its belt to a depot. P puts the pencil down and builds it.
        world.Apply(new SetPlanning(true));
        world.Apply(new Draft(BuildingType.OpenHearthFurnace, new Cell(-2, 8), Dir.East));
        for (int x = 1; x <= 6; x++)
            world.Apply(new Draft(BuildingType.BeltCanvas, new Cell(x, 9), Dir.East));
        world.Apply(new Draft(BuildingType.FreightDepot, new Cell(7, 8), Dir.East));
        // Let the lines fill before the first frame.
        for (int i = 0; i < 25 * World.TicksPerSecond; i++)
            world.Tick();
    }

    /// <summary>
    /// A field of full belt loops so every good moves every tick; 40 loops of side 32 ≈ 10k goods.
    /// The camera sits on the middle loop's top-left corner so belts and goods fill the view.
    /// </summary>
    void BuildBench(int loops, int side)
    {
        int cols = Mathf.CeilToInt(Mathf.Sqrt(loops));
        int goods = 0;
        for (int n = 0; n < loops; n++)
        {
            int ox = (n % cols) * (side + 2), oy = (n / cols) * (side + 2);
            for (int x = 0; x < side - 1; x++)
                world.Apply(new Place(BuildingType.BeltSteel, new Cell(ox + x, oy), Dir.East));
            for (int y = 0; y < side - 1; y++)
                world.Apply(new Place(BuildingType.BeltSteel, new Cell(ox + side - 1, oy + y), Dir.South));
            for (int x = side - 1; x > 0; x--)
                world.Apply(new Place(BuildingType.BeltSteel, new Cell(ox + x, oy + side - 1), Dir.West));
            for (int y = side - 1; y > 0; y--)
                world.Apply(new Place(BuildingType.BeltSteel, new Cell(ox, oy + y), Dir.North));
            var line = ((Belt)world.BuildingAt(new Cell(ox, oy))!).Line!;
            for (int p = line.End; p >= 0; p -= BeltTiers.Spacing)
                if (line.TryInsert(world, p, (Item)(n % Items.All.Length)))
                    goods++;
        }
        int mid = (cols / 2) * (side + 2);
        camera.Position = new Vector2(mid + 1, mid + 1) * Ink.Tile;
        GD.Print($"bench: {loops} loops of side {side}, {world.Buildings.Count} belts, {goods} goods");
        wallClock.Start();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (menu.IsOpen)
            return;
        if (bench)
        {
            tickClock.Start();
            world.Tick();
            tickClock.Stop();
            benchTicks++;
        }
        else
        {
            world.Tick();
        }
        // Autosave every in-game day (compressed and written off the main thread); achievements checked once a second.
        if (world.Day != lastSavedDay)
        {
            lastSavedDay = world.Day;
            companies.SaveInBackground(world);
        }
        if (world.TickCount - lastPollTick >= World.TicksPerSecond)
        {
            lastPollTick = world.TickCount;
            achievements.Poll(world);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && world != null)
            SaveIfPlayed();
        else if (what is (int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)
            CancelDrags();
    }

    public override void _Process(double delta)
    {
        // WASD and the arrows look about the works, not while the paper or the office is open (typing a company's name must not pan).
        if (!menu.IsOpen && !paper.IsOpen)
        {
            var pan = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            pan += new Vector2(
                (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0),
                (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0));
            if (pan != Vector2.Zero)
                camera.Position += pan.LimitLength(1) * PanSpeed * (float)delta / camera.Zoom.X;
        }
        // After the fold nothing more is built: whatever was in hand is put down.
        if (world.Ended && !HandEmpty)
            DropHand();

        var ghost = paper.IsOpen ? null : GhostAt(mouse);
        entities.Ghost = ghost;
        entities.HoverCell = paper.IsOpen ? null : CellAt(mouse);
        entities.RouteStart = tool is { } vt && Catalog.Of(vt).IsVehicle ? routeStart : null;
        entities.Held = held;
        entities.StampOrigin = held == null || paper.IsOpen ? null : StampOriginAt(mouse);
        entities.Selection = selecting ? (selectStart, selectEnd) : null;
        hud.Mode = ModeLine();
        string? hover = paper.IsOpen ? null : VehicleNear(mouse) is { } near ? DescribeVehicle(near) : Describe(CellAt(mouse));
        hud.Refresh(tool, ghost, mouse, hover, paper.IsOpen ? null : Note(ghost));
        paper.Refresh();
        var editions = world.Paper.Editions;
        if (editions.Count > editionsSeen)
        {
            var latest = editions[^1];
            hud.Flash(PaperView.Copy(latest).Headline, Time.GetTicksMsec() / 1000.0);
            editionsSeen = editions.Count;
            audio.Play(latest.Key.StartsWith("COMMISSION_DONE") ? "bell" : "press");
        }
        if (world.Paper.Telegrams.Count > telegramsSeen)
        {
            telegramsSeen = world.Paper.Telegrams.Count;
            audio.Play("telegraph");
        }

        frame++;
        if (panBench)
        {
            camera.Position += new Vector2(panSpeed, panSpeed * 0.4f) * (float)delta / camera.Zoom.X;
            if (frame == screenshotFrames)
            {
                double wall = wallClock.Elapsed.TotalSeconds;
                GD.Print($"panbench: {frame} frames in {wall:F2} s = {frame / wall:F1} FPS; {terrain.ChunksDrawn} chunks drawn, {terrain.LoadedChunks} loaded; camera at {camera.Position / Ink.Tile}");
                if (screenshotPath != null)
                    GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
                GetTree().Quit();
            }
            return;
        }
        if (bench && frame == screenshotFrames)
        {
            double wall = wallClock.Elapsed.TotalSeconds;
            GD.Print($"bench: {benchTicks} ticks in {wall:F2} s = {benchTicks / wall:F1} UPS; " +
                     $"sim {tickClock.Elapsed.TotalMilliseconds / benchTicks:F3} ms/tick; " +
                     $"{frame / wall:F1} FPS ({frame} frames); goods {world.Lines.Sum(l => l.Count)}");
            if (screenshotPath != null)
                GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
            GetTree().Quit();
        }
        else if (!bench && screenshotPath != null && frame == screenshotFrames)
        {
            GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
            GetTree().Quit();
        }
    }

    // ---- Input ---------------------------------------------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouse m)
            mouse = m.Position;
        // A button let go ends its drag wherever the release lands, even over the paper, the office or the classifieds.
        if (e is InputEventMouseButton { Pressed: false } up)
        {
            switch (up.ButtonIndex)
            {
                case MouseButton.Left:
                    laying = false;
                    if (menu.IsOpen || paper.IsOpen)
                        selecting = false;
                    break;
                case MouseButton.Right:
                    removing = false;
                    break;
                case MouseButton.Middle:
                    dragging = false;
                    break;
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // The front office takes everything but Esc while it is open.
        if (menu.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                menu.Close();
            return;
        }
        // With the paper open, only Tab and Esc (close) get through.
        if (paper.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab or Key.Escape })
                paper.Close();
            return;
        }
        // After the fold nothing is built, but the archive can be read (Tab) and the front office opened (Esc) to found
        // a new company or open another; otherwise only looking about is left.
        if (world.Ended)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } k)
            {
                if (k.Keycode == Key.Escape)
                    OpenOffice();
                else if (k.Keycode == Key.Tab && !k.ShiftPressed)
                    OpenPaper();
                return;
            }
            if (e is not (InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.Middle } or InputEventMouseMotion))
                return;
        }
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAt(wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f, wheel.Position);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mid:
                dragging = mid.Pressed;
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                laying = false;
                if (left.Pressed)
                    OnLeftClick(left.Position);
                else if (selecting)
                    EndSelection(CellAt(left.Position));
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                removing = false;
                if (right.Pressed)
                    OnRightClick(right.Position);
                break;

            case InputEventMouseMotion motion:
                if (dragging)
                    camera.Position -= motion.Relative / camera.Zoom.X;
                if (selecting)
                    selectEnd = CellAt(motion.Position);
                if (removing)
                    RemoveAt(CellAt(motion.Position));
                if (laying)
                    LayBeltTo(CellAt(motion.Position));
                break;
        }
    }

    void OnKey(InputEventKey key)
    {
        if (key.CtrlPressed)
        {
            if (key.Keycode == Key.Z && key.ShiftPressed || key.Keycode == Key.Y)
                world.Redo();
            else if (key.Keycode == Key.Z)
                world.Undo();
            return;
        }
        switch (key.Keycode)
        {
            case >= Key.Key1 and <= Key.Key9:
                int i = (int)key.Keycode - (int)Key.Key1;
                if (held != null)
                {
                    slots[i] = held;
                    break;
                }
                if (blueprintMode)
                {
                    held = slots[i];
                    break;
                }
                var onSale = Catalog.InColumn(hud.Column).ToList();
                if (i < onSale.Count)
                    Choose(onSale[i]);
                break;
            case Key.Tab when key.ShiftPressed: hud.NextColumn(); break;
            case Key.Tab: OpenPaper(); break;
            case Key.Q: Pipette(mouse); break;
            case Key.Escape when HandEmpty: OpenOffice(); break;
            case Key.Escape: DropHand(); break;
            case Key.R when held != null: held = Blueprint.Rotate(held); break;
            case Key.R: RotateHeldOrHovered(key.ShiftPressed); break;
            case Key.P: world.Apply(new SetPlanning(!world.Planning)); audio.Play("bell"); break;
            case Key.B:
                blueprintMode = !blueprintMode;
                held = null;
                selecting = false;
                laying = false;
                if (blueprintMode)
                {
                    tool = null;
                    heldFilter = null;
                    bridgeEntry = null;
                    routeStart = null;
                }
                break;
            case Key.F: entities.Overlay = !entities.Overlay; break;
        }
    }

    void Choose(BuildingType type)
    {
        heldFilter = null;
        bridgeEntry = null;
        routeStart = null;
        routeResult = null;
        tool = tool == type ? null : type;
    }

    bool HandEmpty => tool == null && heldFilter == null && bridgeEntry == null && held == null && !blueprintMode && routeStart == null;

    void DropHand()
    {
        tool = null;
        bridgeEntry = null;
        heldFilter = null;
        routeStart = null;
        routeResult = null;
        laying = false;
        held = null;
        selecting = false;
        blueprintMode = false;
    }

    /// <summary>The red line under the hint while a mode is on.</summary>
    string? ModeLine()
    {
        var parts = new List<string>();
        if (world.Planning)
            parts.Add(Text.Get("MODE_PLANNING"));
        if (blueprintMode || held != null)
            parts.Add(Text.Get("MODE_BLUEPRINT"));
        if (entities.Overlay)
            parts.Add(Text.Get("MODE_OVERLAY"));
        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
    }

    /// <summary>Top-left cell that centres the blueprint in hand on a screen point.</summary>
    Cell StampOriginAt(Vector2 screen)
    {
        var (w, h) = Blueprint.Size(held!);
        var m = ToWorld(screen) / Ink.Tile - new Vector2(w, h) * 0.5f + new Vector2(0.5f, 0.5f);
        return new Cell(Mathf.FloorToInt(m.X), Mathf.FloorToInt(m.Y));
    }

    /// <summary>The drag ended: what stands in the rectangle becomes the blueprint in hand.</summary>
    void EndSelection(Cell at)
    {
        selecting = false;
        selectEnd = at;
        var pieces = Blueprint.Capture(world, selectStart, selectEnd);
        held = pieces.Count > 0 ? pieces : null;
    }

    /// <summary>Belts, pipes, roads and rail lay along a drag (GDD §3).</summary>
    static bool DragLaid(BuildingType type) =>
        Catalog.Of(type).IsBelt || type is BuildingType.SteamPipe or BuildingType.Road or BuildingType.Rail;

    /// <summary>A vehicle tool: the first click marks one terminal, the second buys (or reuses a parked vehicle) and routes.</summary>
    void RouteClick(BuildingType type, Cell cell)
    {
        var kind = Terminal.KindOf(type);
        if (world.BuildingAt(cell) is not Terminal t || t.Kind != kind)
            return;
        if (routeStart is not { } start)
        {
            routeStart = t.Origin;
            routeResult = null;
            return;
        }
        if (start == t.Origin)
            return;
        var parked = world.Haulage.ParkedAt(type, start, t.Origin);
        routeResult = parked != null
            ? world.Apply(new Route(parked.Id, start, t.Origin))
            : world.Apply(new Assign(type, start, t.Origin));
        if (routeResult == PlaceResult.Ok)
        {
            routeStart = null;
            audio.Play(kind switch { VehicleKind.Truck => "truck", VehicleKind.Train => "whistle", _ => "horn" });
        }
    }

    /// <summary>The vehicle whose head is under the cursor, if any.</summary>
    Vehicle? VehicleNear(Vector2 screen)
    {
        var p = ToWorld(screen);
        Vehicle? best = null;
        float bestDist = Ink.Tile * 0.7f;
        foreach (var v in world.Haulage.Fleet)
        {
            float d = EntityView.VehiclePos(v).DistanceTo(p);
            if (d < bestDist)
            {
                bestDist = d;
                best = v;
            }
        }
        return best;
    }

    /// <summary>
    /// Applies one step of a click or drag. The first step that changes anything opens a fresh undo step and the
    /// rest of the drag joins it, so a drag never folds into whatever the player did before it.
    /// </summary>
    PlaceResult DragApply(Command command)
    {
        bool changes = command is not Rotate r || world.BuildingAt(r.Cell) is { } b && b.Facing != r.Facing;
        var result = world.Apply(command, joinPrevious: dragStepOpen);
        if (result == PlaceResult.Ok && changes)
            dragStepOpen = true;
        return result;
    }

    void OnLeftClick(Vector2 at)
    {
        var cell = CellAt(at);
        if (held is { } stamp)
        {
            if (Blueprint.Stamp(world, stamp, StampOriginAt(at)) > 0)
                audio.Play("pencil");
            return;
        }
        if (blueprintMode)
        {
            selecting = true;
            selectStart = selectEnd = cell;
            return;
        }
        if (heldFilter is { } filter)
        {
            if (world.BuildingAt(cell) is Splitter { Sorting: true })
                world.Apply(new SetFilter(cell, filter));
            else if (world.BuildingAt(cell) is Dock)
                world.Apply(new SetOrder(cell, filter));
            return;
        }
        if (tool is not { } type)
            return;

        if (Catalog.Of(type).IsVehicle)
        {
            RouteClick(type, cell);
            return;
        }
        if (type == BuildingType.Trestle)
        {
            if (bridgeEntry is not { } entry)
            {
                bridgeEntry = cell;
                return;
            }
            int span = SpanTo(entry, cell);
            Command bridge = world.Planning ? new Draft(type, entry, facing, span) : new Place(type, entry, facing, span);
            dragStepOpen = false;
            if (DragApply(bridge) == PlaceResult.Ok)
            {
                bridgeEntry = null;
                audio.Play(world.Planning ? "typewriter" : "thud");
            }
            return;
        }

        var origin = OriginAt(type, at);
        dragStepOpen = false;
        var result = DragApply(world.Planning ? new Draft(type, origin, facing) : new Place(type, origin, facing));
        if (result == PlaceResult.Ok)
            audio.Play(world.Planning ? "typewriter" : "thud");
        // Clicking a belt with a belt in hand turns it to the hand's facing (a drag from it then carries on the run).
        if (!world.Planning && Catalog.Of(type).IsBelt && result == PlaceResult.Blocked && world.BuildingAt(origin) is Belt)
            DragApply(new Rotate(origin, facing));
        if (DragLaid(type))
        {
            laying = true;
            dragType = type;
            lastCell = origin;
        }
    }

    void OnRightClick(Vector2 at)
    {
        if (tool != null || heldFilter != null || bridgeEntry != null || held != null || blueprintMode)
        {
            DropHand();
            return;
        }
        if (VehicleNear(at) is { } v)
        {
            world.Apply(new Scrap(v.Id));
            return;
        }
        removing = true;
        dragStepOpen = false;
        RemoveAt(CellAt(at));
    }

    /// <summary>Demolishes the building under the cell, or rubs out the plan there; a drag of them is one undo step.</summary>
    void RemoveAt(Cell cell)
    {
        if (world.BuildingAt(cell) == null)
        {
            if (world.PlanAt(cell) != null)
                DragApply(new Undraft(cell));
            return;
        }
        DragApply(new Remove(cell));
    }

    /// <summary>R turns what the hand holds, or else the building under the cursor.</summary>
    void RotateHeldOrHovered(bool counter)
    {
        if (tool != null)
        {
            facing = counter ? facing.CounterClockwise() : facing.Clockwise();
            return;
        }
        var cell = CellAt(mouse);
        if (world.BuildingAt(cell) is { } b)
            world.Apply(new Rotate(cell, counter ? b.Facing.CounterClockwise() : b.Facing.Clockwise()));
    }

    /// <summary>Q copies the good under the cursor (for sorting), or else the building type and its facing.</summary>
    void Pipette(Vector2 at)
    {
        var cell = CellAt(at);
        if (VehicleNear(at) is { } v)
        {
            heldFilter = null;
            bridgeEntry = null;
            routeStart = null;
            tool = v.Type;
            return;
        }
        if (world.BuildingAt(cell) == null && world.PlanAt(cell) is { } plan)
        {
            heldFilter = null;
            bridgeEntry = null;
            routeStart = null;
            tool = plan.Type;
            facing = plan.Facing;
            return;
        }
        switch (world.BuildingAt(cell))
        {
            case Belt belt when GoodNear(belt, at) is { } good:
                tool = null;
                bridgeEntry = null;
                heldFilter = good;
                break;
            case Splitter { Sorting: true, Filter: { } filter }:
                tool = null;
                bridgeEntry = null;
                heldFilter = filter;
                break;
            case { } b:
                heldFilter = null;
                bridgeEntry = null;
                tool = b.Type;
                if (b is not Belt { Role: not BeltRole.Plain })
                    facing = b.Facing;
                break;
        }
    }

    /// <summary>The good on this belt tile closest to the cursor, if any.</summary>
    Item? GoodNear(Belt belt, Vector2 screen)
    {
        if (belt.Line == null)
            return null;
        var d = EntityView.V(belt.PrimaryIn);
        float along = (ToWorld(screen) - EntityView.Center(belt.Origin)).Dot(d) / Ink.Tile * BeltTiers.TileLength + BeltTiers.TileLength / 2f;
        Item? best = null;
        float bestDist = float.MaxValue;
        foreach (var (item, pos, _) in belt.Line.Goods)
        {
            float local = pos - belt.LineStart;
            if (local < -BeltTiers.Spacing / 2f || local >= belt.PathLength)
                continue;
            float dist = Mathf.Abs(local - along);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = item;
            }
        }
        return best;
    }

    /// <summary>
    /// Extends a belt run from the last laid cell toward <paramref name="target"/>, one orthogonal
    /// step at a time, turning the previous belt to point at each new one. The whole drag is one undo step.
    /// </summary>
    void LayBeltTo(Cell target)
    {
        // The hand changed mid-drag (a key, the pipette, blueprint mode): the drag is over.
        if (tool is not { } type || type != dragType)
        {
            laying = false;
            return;
        }
        bool belt = Catalog.Of(type).IsBelt;
        while (lastCell != target)
        {
            int dx = target.X - lastCell.X, dy = target.Y - lastCell.Y;
            var dir = System.Math.Abs(dx) >= System.Math.Abs(dy)
                ? (dx > 0 ? Dir.East : Dir.West)
                : (dy > 0 ? Dir.South : Dir.North);
            var next = lastCell + dir.Offset();
            if (world.Planning)
            {
                // Pencil: each step is a plan facing the way the drag goes; over a like plan it carries on.
                if (DragApply(new Draft(type, next, dir)) != PlaceResult.Ok && world.PlanAt(next)?.Type != type)
                {
                    laying = false;
                    return;
                }
                facing = dir;
                lastCell = next;
                continue;
            }
            // Only a belt is turned to point the run onward; a machine, depot or splitter the drag started on stays as it is.
            if (belt && world.BuildingAt(lastCell) is Belt { Role: BeltRole.Plain })
                DragApply(new Rotate(lastCell, dir));
            if (DragApply(new Place(type, next, dir)) != PlaceResult.Ok)
            {
                // A belt turns to follow the drag; road, rail and pipe just carry on over their own kind.
                bool carried = belt
                    ? world.BuildingAt(next) is Belt && DragApply(new Rotate(next, dir)) == PlaceResult.Ok
                    : world.BuildingAt(next) is { } same && same.Type == type;
                if (!carried)
                {
                    laying = false;
                    return;
                }
            }
            facing = dir;
            lastCell = next;
        }
    }

    /// <summary>Deck length for a bridge from <paramref name="entry"/> toward the cursor's cell, clamped to 1–4.</summary>
    int SpanTo(Cell entry, Cell toward)
    {
        var f = facing.Offset();
        int along = (toward.X - entry.X) * f.X + (toward.Y - entry.Y) * f.Y;
        return Mathf.Clamp(along - 1, 1, World.MaxBridgeSpan);
    }

    Ghost? GhostAt(Vector2 mouse)
    {
        if (tool is not { } type || Catalog.Of(type).IsVehicle)
            return null;
        var cell = CellAt(mouse);
        if (type == BuildingType.Trestle)
        {
            if (bridgeEntry is { } entry)
            {
                int span = SpanTo(entry, cell);
                return new Ghost(type, entry, facing, world.CanPlace(type, entry, facing, span), span);
            }
            return new Ghost(type, cell, facing, world.BuildingAt(cell) == null ? PlaceResult.Ok : PlaceResult.Blocked, 0);
        }
        var origin = OriginAt(type, mouse);
        return new Ghost(type, origin, facing, world.CanPlace(type, origin, facing, asPlan: world.Planning), 0);
    }

    /// <summary>The short line printed by the cursor.</summary>
    string? Note(Ghost? ghost)
    {
        if (held is { } stamp)
            return Text.Get("CURSOR_BLUEPRINT", stamp.Count, Hud.Money(Blueprint.CostCents(world, stamp, StampOriginAt(mouse))));
        if (blueprintMode)
            return Text.Get("CURSOR_BLUEPRINT_SELECT");
        if (heldFilter is { } f)
            return Text.Get("CURSOR_HELD", Text.Get("ITEM_" + Items.Id(f)));
        if (tool is { } vt && Catalog.Of(vt).IsVehicle)
            return VehicleNote(vt);
        if (ghost is not { } g)
            return null;
        if (g.Type == BuildingType.Trestle && bridgeEntry == null && g.Status == PlaceResult.Ok)
            return Text.Get("CURSOR_BRIDGE_END");
        if (g.Type == BuildingType.RailSignal && world.BuildingAt(g.Origin) is Track { Type: BuildingType.Rail })
            return g.Status == PlaceResult.Ok ? Text.Get("CURSOR_SIGNAL", Hud.Money(world.SignalCents)) : CannotAfford(world.SignalCents);
        long land = world.LandCostCents(g.Type, g.Origin, g.Facing, g.Span);
        long structure = world.StructureCents(g.Type, g.Origin);
        string price = land > 0 ? Text.Get("CURSOR_PRICE_LAND", Hud.Money(structure), Hud.Money(land)) : Hud.Money(structure);
        return g.Status switch
        {
            PlaceResult.Ok when world.Planning => Text.Get("CURSOR_PLAN", price),
            PlaceResult.Ok => price,
            PlaceResult.NeedsOre => Text.Get("CURSOR_NO_ORE"),
            PlaceResult.Blocked when BlockedByGround(g) => Text.Get("CURSOR_GROUND"),
            PlaceResult.Blocked => Text.Get("CURSOR_BLOCKED"),
            PlaceResult.BadSpan => Text.Get("CURSOR_BAD_SPAN"),
            PlaceResult.NeedsBank => Text.Get("CURSOR_NEEDS_BANK"),
            PlaceResult.NeedsRiver => Text.Get("CURSOR_NEEDS_RIVER"),
            PlaceResult.FloodBlocked => Text.Get("CURSOR_FLOOD"),
            PlaceResult.NeedsForest => Text.Get("CURSOR_NEEDS_FOREST"),
            PlaceResult.NeedsOil => Text.Get("CURSOR_NEEDS_OIL"),
            _ => CannotAfford(world.PurchaseCents(g.Type, g.Origin, g.Facing, g.Span)),
        };
    }

    /// <summary>"Cannot afford $X", and why when cash and credit would cover it: the bank keeps the coming interest back.</summary>
    string CannotAfford(long cents) =>
        !world.CanAfford(cents) && cents <= world.CashCents + world.CreditAvailableCents
            ? Text.Get("CURSOR_RESERVE", Hud.Money(cents))
            : Text.Get("CURSOR_CANNOT_AFFORD", Hud.Money(cents));

    static BuildingType TerminalOf(VehicleKind kind) => kind switch
    {
        VehicleKind.Truck => BuildingType.TruckDepot,
        VehicleKind.Train => BuildingType.RailStation,
        _ => BuildingType.BargeLanding,
    };

    string? VehicleNote(BuildingType type)
    {
        var kind = Terminal.KindOf(type);
        string terminal = Text.Get("NAME_" + Catalog.Of(TerminalOf(kind)).Id).ToLowerInvariant();
        if (routeResult == PlaceResult.NoRoute)
            return Text.Get("CURSOR_NO_ROUTE", Text.Get("NET_" + kind));
        if (routeResult == PlaceResult.TooExpensive)
            return CannotAfford(world.CostCents(type));
        if (routeStart is not { } start)
            return Text.Get("CURSOR_ROUTE_FIRST", terminal);
        if (world.BuildingAt(CellAt(mouse)) is Terminal t && t.Kind == kind && world.Haulage.ParkedAt(type, start, t.Origin) != null)
            return Text.Get("CURSOR_ROUTE_PARKED", terminal);
        return Text.Get("CURSOR_ROUTE_SECOND", terminal, Hud.Money(world.CostCents(type)));
    }

    /// <summary>True when the ghost fails on river or town rather than on something built.</summary>
    bool BlockedByGround(Ghost g)
    {
        var cells = Catalog.Of(g.Type).IsBridge
            ? new[] { g.Origin, g.Origin + g.Facing.Offset() * (g.Span + 1) }
            : World.FootprintCells(g.Type, g.Origin, g.Facing).ToArray();
        return cells.Any(c => world.TerrainAt(c) is Terrain.River or Terrain.Town);
    }

    /// <summary>Zooms about a screen point so the ground under it stays put.</summary>
    void ZoomAt(float factor, Vector2 screen)
    {
        var before = ToWorld(screen);
        float z = Mathf.Clamp(camera.Zoom.X * factor, MinZoom, MaxZoom);
        camera.Zoom = new Vector2(z, z);
        camera.ForceUpdateScroll();
        camera.Position += before - ToWorld(screen);
    }

    /// <summary>Screen (viewport) pixels to world pixels.</summary>
    Vector2 ToWorld(Vector2 screen) => GetCanvasTransform().AffineInverse() * screen;

    Cell CellAt(Vector2 screen)
    {
        var m = ToWorld(screen) / Ink.Tile;
        return new Cell(Mathf.FloorToInt(m.X), Mathf.FloorToInt(m.Y));
    }

    /// <summary>Top-left cell that centres a building of this type on a screen point.</summary>
    Cell OriginAt(BuildingType type, Vector2 screen)
    {
        var (w, h) = Catalog.Footprint(type, facing);
        var m = ToWorld(screen) / Ink.Tile - new Vector2(w, h) * 0.5f + new Vector2(0.5f, 0.5f);
        return new Cell(Mathf.FloorToInt(m.X), Mathf.FloorToInt(m.Y));
    }

    /// <summary>Text for the clipping in the corner: what the cursor is over.</summary>
    string? Describe(Cell cell)
    {
        if (world.BuildingAt(cell) == null && world.PlanAt(cell) is { } plan)
            return Text.Get("CLIP_PLAN", Text.Get("NAME_" + Catalog.Of(plan.Type).Id).ToUpperInvariant(),
                Hud.Money(world.PurchaseCents(plan.Type, plan.Origin, plan.Facing, plan.Span)));
        switch (world.BuildingAt(cell))
        {
            case Belt { Role: BeltRole.BridgeEntry } entry:
                return Text.Get("CLIP_BRIDGE", entry.Span);
            case Belt { Role: BeltRole.BridgeExit, Partner: { } entry }:
                return Text.Get("CLIP_BRIDGE", entry.Span);
            case Belt b:
                int here = b.Line == null ? 0 : b.Line.Goods.Count(g => g.Pos >= b.LineStart && g.Pos < b.LineStart + b.PathLength);
                return Text.Get("CLIP_BELT", Text.Get("NAME_" + b.Def.Id).ToUpperInvariant(), here,
                    b.CurveIn != null ? Text.Get("CLIP_BELT_CURVE") : "",
                    b.Line is { Stalled: true } ? Text.Get("CLIP_BELT_JAMMED") : Text.Get("CLIP_BELT_MOVING"));
            case Splitter { Sorting: true } s:
                return Text.Get("CLIP_SORTER", s.Filter is { } f ? Text.Get("ITEM_" + Items.Id(f)) : Text.Get("CLIP_SORTER_NONE"));
            case Splitter s:
                return Text.Get("CLIP_SPLITTER", s.Lane(0).Count + s.Lane(1).Count);
            case Mine m:
                return Text.Get("CLIP_MINE", ItemName(m.Yields).ToLowerInvariant(), m.OreTiles, m.Extracted.ToString("N0", CultureInfo.InvariantCulture),
                    m.OutputRate.ToString("P0", CultureInfo.InvariantCulture), Fire(m.Firebox));
            case Depot d:
                return Text.Get("CLIP_DEPOT", d.ItemsSold.ToString("N0", CultureInfo.InvariantCulture), Selling(d));
            case Machine mc:
                return DescribeMachine(mc);
            case LoggingCamp camp:
                return Text.Get("CLIP_CAMP", camp.Trees, camp.Felled.ToString("N0", CultureInfo.InvariantCulture), camp.OutputRate.ToString("P0", CultureInfo.InvariantCulture), Fire(camp.Firebox));
            case PumpJack jack:
                return Text.Get("CLIP_JACK", jack.SeepTiles, jack.Pumped.ToString("N0", CultureInfo.InvariantCulture),
                    (jack.Patch == null ? 1 : world.PatchYield(jack.Patch)).ToString("P0", CultureInfo.InvariantCulture), Fire(jack.Firebox));
            case Waterwheel wheel:
                return Text.Get("CLIP_WHEEL", wheel.SuppliedKw, wheel.DemandKw, Pct(wheel.DemandKw == 0 ? 1000 : System.Math.Min(1000, wheel.SuppliedKw * 1000 / wheel.DemandKw)));
            case WaterPump:
                return Text.Get("CLIP_PUMP", WaterPump.WaterUnits);
            case Boiler boiler:
                var bn = world.Power.SteamNets.FirstOrDefault(n => n.Boilers.Contains(boiler));
                return Text.Get("CLIP_BOILER", Text.Get(boiler.Lit ? "CLIP_BOILER_LIT" : "CLIP_BOILER_OUT"), bn?.SupplyKw ?? 0, bn?.DemandKw ?? 0, boiler.Coal, Pct(bn?.WaterMilli ?? 0));
            case Pipe pipe:
                var pn = world.Power.SteamNets.FirstOrDefault(n => n.Boilers.Count + n.Pumps.Count + n.Stations.Count > 0 && world.Power.SteamNets.Contains(n) && NetHasPipe(n, pipe));
                return pn == null ? Text.Get("CLIP_PIPE_LOOSE") : Text.Get("CLIP_PIPE", pn.Boilers.Count, pn.Pumps.Count, pn.Machines.Count, pn.SupplyKw, pn.DemandKw, Pct(pn.SatisfactionMilli));
            case PowerStation st:
                var en = world.Power.ElecNetOf(st);
                return Text.Get("CLIP_STATION", st.IntakeDemandKw, st.OutputKw, en?.SupplyKw ?? 0, en?.DemandKw ?? 0, Pct(en?.SatisfactionMilli ?? 1000));
            case Pole pole:
                var gn = world.Power.ElecNets.FirstOrDefault(n => n.Poles.Contains(pole));
                return gn == null || gn.Stations.Count + gn.Dams.Count == 0 ? Text.Get("CLIP_POLE_LOOSE") : Text.Get("CLIP_POLE", gn.Poles.Count, gn.SupplyKw, gn.DemandKw, Pct(gn.SatisfactionMilli));
            case Dam dam:
                return Text.Get("CLIP_DAM", dam.OutputKw, world.FloodOf(dam.Origin, dam.Facing).Count());
            case Track { IsRoad: true }:
                return Text.Get("CLIP_ROAD");
            case Track { Signal: true } signal:
                bool held = false;
                for (int d = 0; d < 4 && !held; d++)
                {
                    int block = world.Haulage.BlockOf(signal.Origin + ((Dir)d).Offset());
                    held = block >= 0 && world.Haulage.HolderOf(block) != null;
                }
                return Text.Get("CLIP_SIGNAL", Text.Get(held ? "CLIP_SIGNAL_HELD" : "CLIP_SIGNAL_FREE"));
            case Track:
                return Text.Get("CLIP_RAIL");
            case Terminal term:
                return DescribeTerminal(term);
            case Yard yard:
                return DescribeYard(yard);
            case Dock dock:
                string order = dock.Order is { } o
                    ? Text.Get("CLIP_DOCK_ORDER", ItemName(o).ToLowerInvariant(), Hud.Money(world.Market.BuyPriceCents(o)))
                    : Text.Get("CLIP_DOCK_NONE");
                return Text.Get("CLIP_DOCK", order, dock.UnitsBought.ToString("N0", CultureInfo.InvariantCulture), Hud.Money(dock.SpentCents));
        }
        var tile = world.TileAt(cell);
        if (Items.OfTerrain(tile.Kind) is { } seam)
        {
            double left = tile.Patch != null ? world.PatchYield(tile.Patch) : 1;
            return Text.Get("CLIP_SEAM", ItemName(seam).ToUpperInvariant(), left.ToString("P0", CultureInfo.InvariantCulture));
        }
        return tile.Kind switch
        {
            Terrain.OilSeep => Text.Get("CLIP_OIL", (tile.Patch == null ? 1 : world.PatchYield(tile.Patch)).ToString("P0", CultureInfo.InvariantCulture)),
            Terrain.River => Text.Get("CLIP_RIVER"),
            Terrain.Forest => Text.Get("CLIP_FOREST", Hud.Money(World.ClearingCents)),
            Terrain.Town => Text.Get("CLIP_TOWN"),
            _ when tile.Hill => Text.Get("CLIP_HILL"),
            _ => null,
        };
    }

    static string ItemName(Item item) => Text.Get("ITEM_" + Items.Id(item));

    /// <summary>"Coast-to-Coast Motorcade", "The Second Skyscraper", "Zeppelin Liberty": a commission's name from its keys.</summary>
    public static string CommissionName(string nameKey, string nameArg)
    {
        if (nameArg.Length == 0)
            return Text.Get(nameKey);
        return Text.Get(nameKey, Text.Has(nameArg) ? Text.Get(nameArg) : NameBeyondTable(nameArg));
    }

    /// <summary>
    /// The endless rounds outrun the printed lists: an ordinal past "Twelfth" is written in figures with the right
    /// ending ("13th", "21st", "22nd"), and an airship past the named ones takes a name again with a numeral ("Liberty II").
    /// </summary>
    static string NameBeyondTable(string nameArg)
    {
        int cut = nameArg.LastIndexOf('_');
        if (!int.TryParse(nameArg[(cut + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
            return nameArg;
        if (nameArg.StartsWith("SHIP_"))
        {
            // SHIP_2 … SHIP_9 are named; beyond them the names come round again: II, III, …
            int named = 0;
            while (Text.Has($"SHIP_{named + 2}"))
                named++;
            if (named > 0 && n >= 2)
                return $"{Text.Get($"SHIP_{2 + (n - 2) % named}")} {Roman((n - 2) / named + 1)}";
        }
        int tens = n % 100;
        string suffix = tens is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    static string Roman(int n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (value, glyph) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
            for (; n >= value; n -= value)
                sb.Append(glyph);
        return sb.ToString();
    }

    /// <summary>What the belts running into a depot carry and what each fetches today ("Iron ore fetches $1.96 today.").</summary>
    string Selling(Depot d)
    {
        var goods = new List<Item>();
        foreach (var cell in d.Cells)
            for (int k = 0; k < 4; k++)
            {
                var from = cell + ((Dir)k).Offset();
                if (world.BuildingAt(from) is not Belt { Line: { } line } belt || d.Cells.Contains(from + belt.Facing.Offset()) == false)
                    continue;
                foreach (var (item, _, _) in line.Goods)
                    if (!goods.Contains(item))
                        goods.Add(item);
            }
        if (goods.Count == 0)
            return Text.Get("CLIP_DEPOT_IDLE");
        return string.Join(" ", goods.Take(3).Select(g => Text.Get("CLIP_DEPOT_SELLING", ItemName(g), Hud.Money(world.Market.PriceCents(g)))));
    }

    string DescribeYard(Yard yard)
    {
        var c = world.Prestige.Current;
        string lines = c == null
            ? Text.Get("CLIP_YARD_NONE")
            : string.Join(", ", c.Bill.Select(l => Text.Get("CLIP_YARD_LINE", ItemName(l.Item).ToLowerInvariant(), l.Delivered.ToString("N0", CultureInfo.InvariantCulture), l.Needed.ToString("N0", CultureInfo.InvariantCulture))));
        return Text.Get("CLIP_YARD", yard.Taken.ToString("N0", CultureInfo.InvariantCulture), c?.Number ?? 0, c == null ? "" : CommissionName(c.NameKey, c.NameArg), lines);
    }

    string DescribeTerminal(Terminal term)
    {
        var calls = world.Haulage.Calling(term).ToList();
        int parked = calls.Count(v => v.State == VehicleState.Parked);
        string who = calls.Count == 0 ? Text.Get("CLIP_TERMINAL_NONE") : "";
        if (calls.Count - parked > 0)
            who += Text.Get("CLIP_TERMINAL_CALLS", Fleet(calls.Count - parked, calls[0].Type));
        if (parked > 0)
            who += (who.Length > 0 ? " " : "") + Text.Get("CLIP_TERMINAL_PARKED", Fleet(parked, calls[0].Type));
        return Text.Get("CLIP_TERMINAL", Text.Get("NAME_" + term.Def.Id).ToUpperInvariant(), term.Inbound.Count, term.Outbound.Count,
            term.Received.ToString("N0", CultureInfo.InvariantCulture), term.Shipped.ToString("N0", CultureInfo.InvariantCulture), who);
    }

    static string Fleet(int count, BuildingType type)
    {
        string name = Text.Get("NAME_" + Catalog.Of(type).Id).ToLowerInvariant();
        return count == 1 ? $"1 {name}" : $"{count} {name}s";
    }

    string DescribeVehicle(Vehicle v)
    {
        string state = Text.Get("VSTATE_" + v.State switch
        {
            VehicleState.Loading when v.Reason == WaitReason.Idle => "Idle",
            VehicleState.Waiting => v.Reason.ToString(),
            _ => v.State.ToString(),
        });
        string terminal = Text.Get("NAME_" + Catalog.Of(TerminalOf(v.Kind)).Id).ToLowerInvariant();
        string route = v.A is { } a && v.B is { } b ? Text.Get("CLIP_VEHICLE_ROUTE", a, b, terminal) : Text.Get("CLIP_VEHICLE_NO_ROUTE", terminal);
        return Text.Get("CLIP_VEHICLE", Text.Get("NAME_" + Catalog.Of(v.Type).Id).ToUpperInvariant(), state, v.Cargo.Count, v.Capacity, route);
    }

    static string Pct(int milli) => (milli / 1000.0).ToString("P0", CultureInfo.InvariantCulture);

    /// <summary>Whether a steam net runs through this pipe: it conducts to something on the net.</summary>
    bool NetHasPipe(SteamNet net, Pipe pipe)
    {
        var seen = new HashSet<Cell>();
        var stack = new Stack<Cell>();
        stack.Push(pipe.Origin);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (!seen.Add(c) || seen.Count > 4000)
                continue;
            var b = world.BuildingAt(c);
            if (b is Boiler bo && net.Boilers.Contains(bo) || b is WaterPump pu && net.Pumps.Contains(pu) || b is PowerStation ps && net.Stations.Contains(ps))
                return true;
            if (b is Pipe or Boiler or WaterPump or PowerStation)
                for (int d = 0; d < 4; d++)
                    stack.Push(c + ((Dir)d).Offset());
        }
        return false;
    }

    static string Fire(Firebox? f) => f == null ? Text.Get("CLIP_MACHINE_NOFIRE") : Text.Get("CLIP_FIRE", f.Coal);

    string DescribeMachine(Machine m)
    {
        bool elec = m.Def.Power == PowerNeed.Elec;
        // An electric-only machine that stops has no fire to go out: it wants electricity.
        string state = Text.Get(elec && m.State == MachineState.Unpowered ? "STATE_NoElec" : "STATE_" + m.State);
        string run = m.Current is { } r ? " " + Text.Get("CLIP_MACHINE_RUN", ItemName(r.Outputs[0].Item).ToLowerInvariant(), m.Progress.ToString("P0", CultureInfo.InvariantCulture)) : "";
        var held = m.Inputs.Select(i => $"{i.Count} {ItemName(i.Item).ToLowerInvariant()}").ToList();
        string holds = held.Count == 0 ? Text.Get("CLIP_NOTHING") : string.Join(", ", held);
        string fire = elec ? Text.Get(m.Source == PowerSource.Electric ? "CLIP_MACHINE_NOFIRE" : "CLIP_MACHINE_ELEC")
            : m.Firebox is { } f ? Text.Get("MACHINE_FIREBOX", f.Coal) : "";
        string source = m.Def.Power == PowerNeed.None ? "" : Text.Get("CLIP_MACHINE_SOURCE", Text.Get("SOURCE_" + m.Source));
        string refused = EntityView.Refused(world, m) is { } good
            ? " " + (good == Item.Coal && m.Firebox != null ? Text.Get("CLIP_MACHINE_REFUSES_COAL", Firebox.Cap) : Text.Get("CLIP_MACHINE_REFUSES", ItemName(good).ToLowerInvariant()))
            : "";
        return Text.Get("CLIP_MACHINE", Text.Get("NAME_" + m.Def.Id).ToUpperInvariant(), state, run, holds, m.Waiting.Count, fire) + source + refused;
    }
}
