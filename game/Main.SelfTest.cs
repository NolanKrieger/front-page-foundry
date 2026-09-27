using System.Collections.Generic;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

public partial class Main
{
    /// <summary>
    /// Drives the real input path (viewport → GUI → _UnhandledInput) with synthetic events
    /// and checks the world afterwards. Run with `godot --path . -- --selftest`.
    /// </summary>
    async void RunSelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok)
                failures.Add(what);
            GD.Print((ok ? "ok   " : "FAIL ") + what + (ok ? "" : $"   [tool={tool} column={hud.Column} filter={heldFilter} entry={bridgeEntry}]"));
        }

        // Companies and settings go to a sandbox, so the self-test (run by release.sh on the player's own PC) never
        // lists, overwrites or deletes a real company.
        Companies.UserRoot = "user://selftest/";
        string sandbox = ProjectSettings.GlobalizePath(Companies.UserRoot);
        if (System.IO.Directory.Exists(sandbox))
            System.IO.Directory.Delete(sandbox, true);

        // Full screen, so the test cells never land under the classifieds (KWin tiles a plain window to half height).
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        camera.Zoom = Vector2.One * 0.5f;
        camera.ForceUpdateScroll();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"selftest window {DisplayServer.WindowGetSize()}");

        Vector2 ScreenOf(Cell c) => GetCanvasTransform() * (new Vector2(c.X + 0.5f, c.Y + 0.5f) * Ink.Tile);
        void Push(InputEvent e) => GetViewport().PushInput(e);
        void Press(Key k, bool ctrl = false, bool shift = false)
        {
            Push(new InputEventKey { Keycode = k, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift });
            Push(new InputEventKey { Keycode = k, Pressed = false, CtrlPressed = ctrl, ShiftPressed = shift });
        }
        void Click(MouseButton b, Vector2 at, bool pressed) =>
            Push(new InputEventMouseButton { ButtonIndex = b, Position = at, GlobalPosition = at, Pressed = pressed });
        void Tap(MouseButton b, Vector2 at)
        {
            Click(b, at, true);
            Click(b, at, false);
        }
        void Move(Vector2 at) => Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Belt? BeltAt(int x, int y) => world.BuildingAt(new Cell(x, y)) as Belt;

        // Every ad, name and item has a printed line.
        bool strings = true;
        foreach (var def in Catalog.Defs)
            strings &= Text.Has($"AD_{def.Id}_TITLE") && Text.Has($"AD_{def.Id}_BODY") && Text.Has("NAME_" + def.Id);
        foreach (var item in Items.Defs)
            strings &= Text.Has("ITEM_" + item.Id);
        Check(strings, "every ad, building and good has a line in the string table");
        {
            // Every row of the string table reads as exactly key + text (an unquoted comma, or two rows run together, would not).
            using var csv = FileAccess.Open("res://assets/text/en.csv", FileAccess.ModeFlags.Read);
            var bad = new List<string>();
            while (csv != null && !csv.EofReached())
            {
                var row = csv.GetCsvLine();
                if (row.Length != 2 && !(row.Length == 1 && row[0].Length == 0))
                    bad.Add(row[0]);
            }
            Check(csv != null && bad.Count == 0, $"every row of the string table is one key and one text ({string.Join(", ", bad)})");
            Check(Text.Has("CURSOR_NEEDS_BANK") && !Text.Get("CLIP_HILL").Contains("CURSOR_") && Text.Get("HINT").Contains("wheel zooms"), "the hint, the hill clipping and the bank note print whole");
            foreach (var key in new[] { "STATE_NoElec", "OVERLAY_FIRE_OUT", "MACHINE_FIREBOX", "CLIP_DEPOT_SELLING", "CLIP_DEPOT_IDLE", "ARCHIVE_ROW", "NOTICE_FOLDS_HINT",
                         "MENU_FIRST_RUN", "MENU_COMPANY_FOLDED", "MENU_UNREADABLE", "MENU_NAME_EMPTY", "MENU_NAME_TAKEN" })
                Check(Text.Has(key), $"the string table has {key}");
        }

        // One drag lays a run east, then turns south.
        Press(Key.Key1);
        Check(tool == BuildingType.BeltCanvas, "key 1 picks canvas belts");
        long cash = world.CashCents;
        Click(MouseButton.Left, ScreenOf(new Cell(11, -5)), true);
        Move(ScreenOf(new Cell(15, -5)));
        Move(ScreenOf(new Cell(15, -3)));
        Click(MouseButton.Left, ScreenOf(new Cell(15, -3)), false);

        bool eastRun = true;
        for (int x = 11; x <= 14; x++)
            eastRun &= BeltAt(x, -5)?.Facing == Dir.East;
        Check(eastRun, "drag lays four east-facing belts");
        Check(BeltAt(15, -5) is { Facing: Dir.South, CurveIn: Dir.East }, "the turn becomes a curve");
        Check(BeltAt(15, -4)?.Facing == Dir.South && BeltAt(15, -3)?.Facing == Dir.South, "run continues south");
        Check(cash - world.CashCents == 7 * (Catalog.Of(BuildingType.BeltCanvas).BaseCostCents + World.RuralLandCents), "seven belts charged at $5 plus $2 of land each");
        Check(AudioServer.IsBusMute(0) && audio.Loaded == 23 && audio.MusicPlaying && audio.CurrentRag == "rag_courier", "the whole soundscape is loaded, the first rag is on, and the test runs muted");
        Check(BeltAt(11, -5)?.Line == BeltAt(15, -3)?.Line, "the whole run is one transport line");

        // Ctrl+Z takes the whole drag back; Ctrl+Y lays it again.
        Press(Key.Z, ctrl: true);
        Check(BeltAt(13, -5) == null && world.CashCents == cash, "Ctrl+Z undoes the whole drag and refunds it");
        Press(Key.Y, ctrl: true);
        Check(BeltAt(13, -5) != null && cash - world.CashCents == 7 * 700, "Ctrl+Y lays it again");

        // Right-click with a tool in hand just puts it down; without one it removes, for 75% back.
        Tap(MouseButton.Right, ScreenOf(new Cell(13, -5)));
        Check(tool == null && BeltAt(13, -5) != null, "right-click with a tool in hand only drops the tool");
        cash = world.CashCents;
        Tap(MouseButton.Right, ScreenOf(new Cell(13, -5)));
        Check(BeltAt(13, -5) == null, "right-click removes a belt");
        Check(world.CashCents - cash == 375, "removal refunds three quarters");

        // R over a belt with nothing in hand turns it.
        Move(ScreenOf(new Cell(12, -5)));
        Press(Key.R);
        Check(BeltAt(12, -5)?.Facing == Dir.South, "R turns the hovered belt clockwise");
        Press(Key.R, shift: true);
        Check(BeltAt(12, -5)?.Facing == Dir.East, "Shift+R turns it back");

        // The paper: Tab opens it on the first tutorial edition; its classifieds pick tools; Tab closes it.
        Check(world.Paper.Latest?.Key == "TUT1", "the first edition has printed");
        Press(Key.Tab);
        Check(paper.IsOpen && paper.Current == Page.Front, "Tab opens the Courier on the front page");
        Check(audio.LastPlayed == "rustle", "opening the paper rustles it");
        Check(paper.Shown?.Key == "TUT1", "the front page shows the first edition");
        paper.Show(Page.Classifieds);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var mineCard = paper.Ads.First(a => a.Type == BuildingType.MineHead);
        Check(mineCard.IsCircled, "the mine-head advertisement is ringed in red pencil");
        Press(Key.Key1);
        Check(tool == null, "keys do nothing while the paper is open");
        Tap(MouseButton.Left, mineCard.GetGlobalRect().GetCenter());
        Check(tool == BuildingType.MineHead && !paper.IsOpen, "clicking an ad in the paper picks it and closes the paper");
        Press(Key.Escape);
        Press(Key.Tab);
        paper.Show(Page.Market);
        Press(Key.Escape);
        Check(!paper.IsOpen, "Esc closes the paper");

        // Shift+Tab turns the quick strip to the next column; the mine-head ad is in Extraction.
        Check(hud.Column == AdColumn.Transport, "the strip opens on the transport column");
        Press(Key.Tab, shift: true);
        Check(hud.Column == AdColumn.Extraction, "Shift+Tab turns to the extraction column");
        Press(Key.Key1);
        Check(tool == BuildingType.MineHead, "key 1 in that column picks mine heads");
        Press(Key.Escape);
        // The classifieds re-set themselves on the next frame; wait for it before aiming at an ad.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Clicking an advertisement picks that building; R turns the one in hand.
        var ad = hud.AdRect(BuildingType.MineHead).GetCenter();
        Tap(MouseButton.Left, ad);
        Check(tool == BuildingType.MineHead, "clicking the mine-heads ad picks mine heads");
        var before = facing;
        Press(Key.R);
        Check(facing == before.Clockwise(), "R turns the building in hand clockwise");
        facing = Dir.East;

        Tap(MouseButton.Left, ScreenOf(new Cell(6, -4)));
        Check(world.BuildingAt(new Cell(6, -4)) is Mine { OreTiles: > 0, Facing: Dir.East }, "a mine head goes down on the ore patch");
        Check(world.Paper.Latest?.Key == "TUT2" && world.Paper.Latest.CircledAd == BuildingType.Smelter, "the second edition prints and rings the smelter");
        Press(Key.Escape);
        Press(Key.Tab);
        Check(paper.IsOpen && paper.Current == Page.Front && paper.Shown?.Key == "TUT2", "Tab after a new edition opens on the front page with it, not on the page last read");
        paper.Show(Page.Market);
        Press(Key.Tab);
        Press(Key.Tab);
        Check(paper.IsOpen && paper.Current == Page.Market, "with nothing new printed, the paper opens where it was left");
        Press(Key.Tab);
        hud.ShowColumn(AdColumn.Extraction);
        Press(Key.Key1);
        Tap(MouseButton.Left, ScreenOf(new Cell(-12, 8)));
        Check(world.BuildingAt(new Cell(-12, 8)) == null, "no mine head on bare ground");
        Press(Key.Escape);
        Check(tool == null, "Escape puts the pencil down");

        // Q copies the hovered building and its facing.
        Move(ScreenOf(new Cell(12, -5)));
        Press(Key.Q);
        Check(tool == BuildingType.BeltCanvas && facing == Dir.East, "Q over a belt picks up canvas belts facing its way");

        // A machine goes down like anything else and prints a clipping about itself.
        hud.ShowColumn(AdColumn.MillsAndFurnaces);
        Press(Key.Key1);
        Check(tool == BuildingType.Smelter, "key 1 in mills & furnaces picks the smelter");
        Tap(MouseButton.Left, ScreenOf(new Cell(-6, 2)));
        var smelter = world.BuildingAt(new Cell(-6, 2)) as Machine;
        Check(smelter is { State: MachineState.Starved } && smelter.Firebox?.Coal == Firebox.StarterCoal, "a smelter goes down starved, with its starter coal");
        Check(audio.LastPlayed == "thud", "a building going down thuds");
        Check(Describe(new Cell(-6, 2))?.StartsWith("SMELTER. waiting for goods") == true, "hovering it prints its state");
        Press(Key.Escape);
        hud.ShowColumn(AdColumn.Transport);

        // A splitter is a 1×2 piece; key 4 picks it.
        Press(Key.Key4);
        Check(tool == BuildingType.Splitter, "key 4 picks the splitter");
        Tap(MouseButton.Left, ScreenOf(new Cell(20, 0)));
        var splitter = world.BuildingAt(new Cell(20, 0)) as Splitter;
        Check(splitter != null && splitter.Cells.Count() == 2 && world.BuildingAt(new Cell(20, 1)) == splitter, "a splitter covers two cells across the flow");

        // A trestle takes two clicks: the entry, then the far end.
        Press(Key.Key6);
        Check(tool == BuildingType.Trestle, "key 6 picks the trestle");
        cash = world.CashCents;
        Tap(MouseButton.Left, ScreenOf(new Cell(22, 4)));
        Check(bridgeEntry == new Cell(22, 4) && world.BuildingAt(new Cell(22, 4)) == null, "first click marks the entry, nothing built yet");
        Tap(MouseButton.Left, ScreenOf(new Cell(25, 4)));
        Check(BeltAt(22, 4) is { Role: BeltRole.BridgeEntry, Span: 2 } && BeltAt(25, 4) is { Role: BeltRole.BridgeExit },
            "second click builds the pair with the deck spanning two cells");
        Check(cash - world.CashCents == Catalog.Of(BuildingType.Trestle).BaseCostCents + 2 * World.RuralLandCents, "a trestle costs one price for the pair, plus land at both ends");
        Press(Key.Z, ctrl: true);
        Check(BeltAt(22, 4) == null && BeltAt(25, 4) == null && world.CashCents == cash, "undo removes both ends and refunds");
        Press(Key.Escape);

        // Goods on a belt: Q over them picks the good; clicking a sorting splitter sets its filter.
        // The mine at (6,-4) facing east outputs into (8,-3); lay a belt there and let it run.
        Press(Key.Key1);
        Tap(MouseButton.Left, ScreenOf(new Cell(8, -3)));
        Press(Key.Escape);
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
            world.Tick();
        Check(BeltAt(8, -3)?.Line?.Count > 0, "the mine fills the belt at its port");
        Move(ScreenOf(new Cell(8, -3)));
        Press(Key.Q);
        Check(heldFilter == Item.IronOre && tool == null, "Q over goods picks up iron ore for sorting");
        Press(Key.Key5);
        Check(tool == BuildingType.SortingSplitter && heldFilter == null, "key 5 picks the sorting splitter");
        Tap(MouseButton.Left, ScreenOf(new Cell(20, 4)));
        var sorter = world.BuildingAt(new Cell(20, 4)) as Splitter;
        Check(sorter is { Sorting: true, Filter: null }, "a sorting splitter starts with no filter");
        Move(ScreenOf(new Cell(8, -3)));
        Press(Key.Q);
        Tap(MouseButton.Left, ScreenOf(new Cell(20, 4)));
        Check(sorter?.Filter == Item.IronOre, "clicking the sorter with a good in hand sets its filter");
        Press(Key.Z, ctrl: true);
        Check(sorter?.Filter == null, "undo clears the filter again");

        // A receiving dock takes an order the same way.
        hud.ShowColumn(AdColumn.Extraction);
        Press(Key.Key1 + Catalog.InColumn(AdColumn.Extraction).ToList().IndexOf(BuildingType.ReceivingDock));
        Check(tool == BuildingType.ReceivingDock, "the receiving dock's key in extraction picks it");
        Tap(MouseButton.Left, ScreenOf(new Cell(-12, 2)));
        var dock = world.BuildingAt(new Cell(-12, 2)) as Dock;
        Check(dock is { Order: null }, "a dock goes down with nothing on order");
        Move(ScreenOf(new Cell(8, -3)));
        Press(Key.Q);
        Tap(MouseButton.Left, ScreenOf(new Cell(-12, 2)));
        Check(dock?.Order == Item.IronOre, "clicking the dock with a good in hand orders it");
        Check(Describe(new Cell(-12, 2))?.Contains("Buying iron ore") == true, "the dock's clipping names the order");
        hud.ShowColumn(AdColumn.Transport);
        Check(hud.MoneyLine.Contains("CREDIT") && hud.MoneyLine.Contains("SHARE"), "the masthead prints cash, credit and the share price");

        // A building standing in front of the hovered cell prints as a wash; on its own footprint it does not.
        var mine = (Mine)world.BuildingAt(new Cell(6, -4))!;
        entities.HoverCell = new Cell(6, -6);
        Check(entities.Occludes(mine), "hovering the cell behind a mine head marks it for the occlusion wash");
        entities.HoverCell = new Cell(6, -4);
        Check(!entities.Occludes(mine), "hovering the mine head itself does not");
        entities.HoverCell = new Cell(6, -9);
        Check(!entities.Occludes(mine), "a cell above its plate does not");
        Check(Art.Building(mine.Def) != null && Art.Good(Item.IronOre) != null, "the mine head plate and the iron ore icon load");

        // The ground: river and town refuse building; a forest tile prints its clearing price; the town exists.
        Check(world.Town.Blocks >= 12, "Carvell Falls has its first blocks");
        var townTile = world.Town.Tiles.First();
        Check(Describe(townTile)?.StartsWith("CARVELL FALLS") == true, "hovering the town prints its clipping");
        int rx = 40, ry = (int)System.Math.Floor(world.Map.RiverCentre(0, rx));
        Check(world.TerrainAt(new Cell(rx, ry)) == Terrain.River && Describe(new Cell(rx, ry))?.StartsWith("THE CARVELL RIVER") == true, "the river runs south of the works and says so");
        hud.ShowColumn(AdColumn.MillsAndFurnaces);
        Press(Key.Key1);
        Tap(MouseButton.Left, ScreenOf(new Cell(rx, ry)));
        Check(world.BuildingAt(new Cell(rx, ry)) == null, "no smelter on the water");
        Press(Key.Escape);
        hud.ShowColumn(AdColumn.Transport);

        // Power: a waterwheel wants the bank, a pipe run joins a pump, a pole says it is loose.
        int by = ry;
        while (world.TerrainAt(new Cell(rx, by)) == Terrain.River)
            by--;
        hud.ShowColumn(AdColumn.Power);
        var powerKeys = Catalog.InColumn(AdColumn.Power).ToList();
        Press(Key.Key1 + powerKeys.IndexOf(BuildingType.Waterwheel));
        Check(tool == BuildingType.Waterwheel, "the waterwheel's key in the power column picks it");
        facing = Dir.North;
        Tap(MouseButton.Left, ScreenOf(new Cell(rx, by - 6)));
        Check(world.BuildingAt(new Cell(rx, by - 6)) == null, "no waterwheel away from the water");
        // A 1×2 wheel centred on the cursor: aim one cell up so its foot stands on the bank, not in the water.
        Tap(MouseButton.Left, ScreenOf(new Cell(rx, by - 1)));
        Check(world.BuildingAt(new Cell(rx, by)) is Waterwheel, "a waterwheel goes down on the bank");
        Check(Describe(new Cell(rx, by))?.StartsWith("WATERWHEEL") == true, "the wheel prints its clipping");
        Press(Key.Key1 + powerKeys.IndexOf(BuildingType.PowerPole));
        Check(tool == BuildingType.PowerPole, "the pole's key in the power column picks it");
        Tap(MouseButton.Left, ScreenOf(new Cell(rx + 4, by - 4)));
        Check(world.BuildingAt(new Cell(rx + 4, by - 4)) is Pole && Describe(new Cell(rx + 4, by - 4))?.StartsWith("POWER POLE. No station") == true, "a lone pole says its grid has no supply");
        Press(Key.Escape);
        facing = Dir.East;
        hud.ShowColumn(AdColumn.Transport);

        // A logging camp wants wood within reach; a pump jack wants a seep.
        hud.ShowColumn(AdColumn.Extraction);
        var extractKeys = Catalog.InColumn(AdColumn.Extraction).ToList();
        Press(Key.Key1 + extractKeys.IndexOf(BuildingType.LoggingCamp));
        Check(tool == BuildingType.LoggingCamp, "the logging camp's key picks it");
        Tap(MouseButton.Left, ScreenOf(new Cell(-14, -10)));
        Check(world.BuildingAt(new Cell(-14, -10)) == null, "no camp on bare ground");
        Cell? wood = null;
        for (int r = 20; r < 120 && wood == null; r++)
            for (int x = -r; x <= r && wood == null; x += 2)
                foreach (int y in new[] { -r, r })
                    if (world.TerrainAt(new Cell(x, y)) == Terrain.Forest && world.CanPlace(BuildingType.LoggingCamp, new Cell(x + 1, y + 1), Dir.East) == PlaceResult.Ok)
                    {
                        wood = new Cell(x + 1, y + 1);
                        break;
                    }
        Check(wood != null, "there is wood to be found");
        if (wood is { } w0)
        {
            world.Apply(new Place(BuildingType.LoggingCamp, w0, Dir.East));
            Check(world.BuildingAt(w0) is LoggingCamp { Trees: > 0 } && Describe(w0)?.StartsWith("LOGGING CAMP") == true, "a camp by the wood counts its trees and says so");
        }
        Press(Key.Key1 + extractKeys.IndexOf(BuildingType.PumpJack));
        Check(tool == BuildingType.PumpJack, "the pump jack's key picks it");
        Tap(MouseButton.Left, ScreenOf(new Cell(-14, -10)));
        Check(world.BuildingAt(new Cell(-14, -10)) == null, "no pump jack off a seep");
        Press(Key.Escape);
        hud.ShowColumn(AdColumn.Transport);

        // Haulage: a road lays by dragging, two clicks put a truck between depots, a signal goes on rail, a landing wants the bank.
        hud.ShowColumn(AdColumn.Haulage);
        var haulKeys = Catalog.InColumn(AdColumn.Haulage).ToList();
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.Road));
        Check(tool == BuildingType.Road, "the road's key in haulage picks it");
        Click(MouseButton.Left, ScreenOf(new Cell(-20, 6)), true);
        Move(ScreenOf(new Cell(-12, 6)));
        Click(MouseButton.Left, ScreenOf(new Cell(-12, 6)), false);
        Check(Enumerable.Range(-20, 9).All(x => world.BuildingAt(new Cell(x, 6)) is Track { IsRoad: true }), "a drag lays nine road tiles");
        Check(Describe(new Cell(-16, 6))?.StartsWith("ROAD") == true, "hovering a road prints its clipping");
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.TruckDepot));
        Tap(MouseButton.Left, ScreenOf(new Cell(-20, 4)));
        Tap(MouseButton.Left, ScreenOf(new Cell(-13, 4)));
        Check(world.BuildingAt(new Cell(-20, 4)) is Terminal { Kind: VehicleKind.Truck } && world.BuildingAt(new Cell(-13, 4)) is Terminal, "two truck depots go down beside the road");
        Check(Describe(new Cell(-20, 4))?.StartsWith("TRUCK DEPOT") == true, "a depot prints its clipping");
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.MotorTruck));
        Check(tool == BuildingType.MotorTruck && Note(null)?.StartsWith("click the first truck depot") == true, "the truck's key picks it and the cursor asks for a depot");
        // Money by now is partly credit, so count cash less debt.
        long net = world.CashCents - world.DebtCents;
        Tap(MouseButton.Left, ScreenOf(new Cell(-20, 5)));
        Check(routeStart == new Cell(-20, 4) && world.Haulage.Fleet.Count == 0, "the first click marks the first depot and buys nothing");
        Tap(MouseButton.Left, ScreenOf(new Cell(-12, 5)));
        var truck = world.Haulage.Fleet.FirstOrDefault();
        Check(truck is { A: { } ta, B: { } tb } && ta == new Cell(-20, 4) && tb == new Cell(-13, 4) && routeStart == null, "the second click buys a truck to run between them");
        Check(net - (world.CashCents - world.DebtCents) == Catalog.Of(BuildingType.MotorTruck).BaseCostCents, "the truck costs its advertised price");
        Press(Key.Escape);
        if (truck != null)
        {
            var truckAt = GetCanvasTransform() * EntityView.VehiclePos(truck);
            Check(DescribeVehicle(truck).StartsWith("MOTOR TRUCK"), "the truck prints its clipping");
            Tap(MouseButton.Right, truckAt);
            Check(world.Haulage.Fleet.Count == 0, "right-click on a vehicle scraps it");
            Press(Key.Z, ctrl: true);
            Check(world.Haulage.Fleet.Count == 1, "undo brings the vehicle back");
        }
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.Rail));
        Tap(MouseButton.Left, ScreenOf(new Cell(-20, 9)));
        Check(world.BuildingAt(new Cell(-20, 9)) is Track { IsRail: true, Signal: false }, "rail goes down");
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.RailSignal));
        Check(tool == BuildingType.RailSignal, "the signal's key picks it");
        net = world.CashCents - world.DebtCents;
        Tap(MouseButton.Left, ScreenOf(new Cell(-20, 9)));
        Check(world.BuildingAt(new Cell(-20, 9)) is Track { Signal: true } && net - (world.CashCents - world.DebtCents) == world.SignalCents, "the signal tool marks the rail for the difference");
        Check(Describe(new Cell(-20, 9))?.StartsWith("RAIL SIGNAL") == true, "a signal prints its clipping");
        Press(Key.Key1 + haulKeys.IndexOf(BuildingType.BargeLanding));
        Tap(MouseButton.Left, ScreenOf(new Cell(-20, 12)));
        Check(world.BuildingAt(new Cell(-20, 12)) == null, "no barge landing away from the water");
        Press(Key.Escape);
        hud.ShowColumn(AdColumn.Transport);

        // Planning: P lifts the pencil, plans cost nothing, and they build once it is down.
        Press(Key.P);
        Check(world.Planning && audio.LastPlayed == "bell", "P lifts the pencil: planning mode, with the carriage bell");
        hud.ShowColumn(AdColumn.MillsAndFurnaces);
        Press(Key.Key1);
        cash = world.CashCents;
        Tap(MouseButton.Left, ScreenOf(new Cell(-30, -6)));
        Check(world.PlanAt(new Cell(-30, -6)) is { Type: BuildingType.Smelter } && world.CashCents == cash && world.BuildingAt(new Cell(-30, -6)) == null, "a smelter is pencilled in for nothing");
        Check(audio.LastPlayed == "typewriter", "a plan is typed, not thudded");
        Check(Describe(new Cell(-30, -6))?.StartsWith("PLANNED SMELTER") == true, "hovering a plan prints its clipping");
        Check(Note(GhostAt(ScreenOf(new Cell(-30, -12))))?.Contains("pencilled") == true, "the cursor says the next one is a plan too");
        Press(Key.Escape);
        Tap(MouseButton.Right, ScreenOf(new Cell(-30, -6)));
        Check(world.PlanAt(new Cell(-30, -6)) == null, "right-click rubs a plan out");
        Press(Key.Z, ctrl: true);
        Check(world.PlanAt(new Cell(-30, -6)) != null, "undo pencils it back in");
        Press(Key.P);
        Check(!world.Planning, "P again puts the pencil down");
        // Plans build from cash on hand only, never from credit; the till is near empty by now, so sell something first.
        long smelterPrice = world.PurchaseCents(BuildingType.Smelter, new Cell(-30, -6), Dir.East);
        world.Tick();
        Check(world.Plans.Count == 1 || world.CashCents >= smelterPrice, "a plan waits while cash is short even though credit would cover it");
        while (world.CashCents < smelterPrice)
            world.Sell(Item.Aeroplane);
        cash = world.CashCents;
        for (int i = 0; i < 3; i++)
            world.Tick();
        Check(world.BuildingAt(new Cell(-30, -6)) is Machine { Type: BuildingType.Smelter } && world.Plans.Count == 0 && world.CashCents < cash, "the plan is built and paid for once the pencil is down and the till allows");
        hud.ShowColumn(AdColumn.Transport);

        // Blueprints: B, drag over the belt run laid at the start, save it under 7, turn it, stamp it as plans.
        Press(Key.B);
        Check(blueprintMode && tool == null, "B opens blueprint mode with an empty hand");
        Click(MouseButton.Left, ScreenOf(new Cell(10, -6)), true);
        Move(ScreenOf(new Cell(16, -2)));
        Click(MouseButton.Left, ScreenOf(new Cell(16, -2)), false);
        Check(held is { Count: >= 5 }, "dragging over the belt run copies it");
        Press(Key.Key7);
        Check(slots[6] == held, "7 saves the blueprint under that number");
        int heldWidth = held == null ? 0 : Blueprint.Size(held).Width;
        Press(Key.R);
        Check(held != null && Blueprint.Size(held).Height == heldWidth, "R turns the blueprint a quarter");
        Press(Key.Escape);
        Check(held == null && !blueprintMode, "Esc drops it and leaves blueprint mode");
        Press(Key.B);
        Press(Key.Key7);
        Check(held != null && slots[6] != null && held.Count == slots[6]!.Count, "B then 7 takes the saved blueprint in hand");
        int plansBefore = world.Plans.Count;
        Tap(MouseButton.Left, ScreenOf(new Cell(-40, -20)));
        Check(world.Plans.Count >= plansBefore + 5, "clicking stamps it as plans");
        Press(Key.Z, ctrl: true);
        Check(world.Plans.Count == plansBefore, "undo takes the whole stamp back");
        Press(Key.Escape);

        // The flow overlay.
        Press(Key.F);
        Check(entities.Overlay && ModeLine()?.Contains("FLOW") == true, "F shows the flow overlay and says so");
        Press(Key.F);
        Check(!entities.Overlay, "F hides it again");

        // Prestige: the Exposition Yard opens the first commission and its clipping lists the bill.
        hud.ShowColumn(AdColumn.Extraction);
        Check(world.Prestige.Current == null, "no commission before a yard stands");
        Press(Key.Key1 + Catalog.InColumn(AdColumn.Extraction).ToList().IndexOf(BuildingType.ExpositionYard));
        Check(tool == BuildingType.ExpositionYard, "the yard's key in extraction picks it");
        long yardPrice = world.PurchaseCents(BuildingType.ExpositionYard, new Cell(-41, 6), Dir.East);
        while (world.CashCents < yardPrice)
            world.Sell(Item.Aeroplane);
        Tap(MouseButton.Left, ScreenOf(new Cell(-40, 7)));
        var yard = world.Buildings.OfType<Yard>().FirstOrDefault();
        Check(yard != null && world.Prestige.Current?.Number == 1, "a yard goes down and the first commission opens");
        if (yard != null)
            Check(Describe(yard.Origin)?.StartsWith("EXPOSITION YARD") == true && Describe(yard.Origin)!.Contains("Coast-to-Coast Motorcade"), "the yard's clipping names the commission and its bill");
        Press(Key.Escape);
        hud.ShowColumn(AdColumn.Transport);

        Check(audio.Played >= 12, $"the session's actions made sounds ({audio.Played})");

        // The front office: Esc with an empty hand opens it and stops the clock; Esc closes it.
        Press(Key.Escape);
        Check(HandEmpty && menu.IsOpen && menu.Page == MenuPage.Front, "Esc with nothing in hand opens the front office");
        long paused = world.TickCount;
        for (int i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(world.TickCount == paused, "the world stands still while the office is open");
        Press(Key.Key1);
        Check(tool == null, "keys do nothing while the office is open");
        menu.Show(MenuPage.NewCompany);
        Check(menu.ChosenName == Companies.DefaultName && menu.ChosenDifficulty == Difficulty.SteadyTrade, "a new company starts with the default name on Steady Trade");
        menu.Show(MenuPage.Companies);
        menu.Show(MenuPage.Settings);
        float musicBefore = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music"));
        menu.SetMusicDb(-20);
        Check(Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")), -20f), "the music slider moves the music bus");
        menu.SetMusicDb(musicBefore);
        int saveRequests = settings.SaveRequests;
        Press(Key.Escape);
        Check(!menu.IsOpen, "Esc closes the office");
        Check(settings.SaveRequests > saveRequests && !System.IO.File.Exists(Settings.Path), "closing the office keeps the settings (the self-test itself never writes the file)");
        Check(AudioServer.IsBusMute(0), "moving a slider never lifts the self-test's forced mute");
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(world.TickCount > paused, "the clock runs again");

        // Saving: a company folder, an atomic save, a load that matches, a backup after the second save.
        var testCo = new Companies();
        testCo.Adopt("Selftest Works");
        string coDir = testCo.Dir!;
        try
        {
            Check(testCo.Save(world) && System.IO.File.Exists(System.IO.Path.Combine(coDir, SaveStore.FileName)), "the company saves to its folder");
            var (loaded, source) = SaveStore.Load(coDir);
            Check(source == SaveStore.FileName && loaded.StateHash() == world.StateHash(), "the save loads back to the same state");
            world.Tick();
            Check(testCo.Save(world) && System.IO.File.Exists(System.IO.Path.Combine(coDir, SaveStore.Backup1)), "a second save keeps the first as a backup");
            Check(Companies.List().Any(c => c.Header.Company == "Selftest Works"), "the company list finds it");

            // Founding never files a new company under a folder another company keeps.
            var founded = new List<string>();
            menu.NewCompanyRequested -= FoundCompany;
            void Record(string name, Difficulty _) => founded.Add(name);
            menu.NewCompanyRequested += Record;
            menu.Open(OfficeLine(), null, MenuPage.NewCompany);
            foreach (var (name, problem) in new[] { ("", "MENU_NAME_EMPTY"), ("  !!! ", "MENU_NAME_EMPTY"), ("Selftest Works", "MENU_NAME_TAKEN"), ("selftest works!", "MENU_NAME_TAKEN") })
            {
                menu.SetCompanyName(name);
                menu.TryFound();
                Check(founded.Count == 0 && menu.NameProblem == Text.Get(problem), $"the founding page refuses \"{name}\" ({menu.NameProblem})");
            }
            menu.SetCompanyName("  Kestrel Iron & Steel ");
            menu.TryFound();
            Check(founded.Count == 1 && founded[0] == "Kestrel Iron & Steel" && menu.NameProblem == null, "a new name founds the company, trimmed");
            var defaultCo = new Companies();
            defaultCo.Adopt(Companies.DefaultName);
            defaultCo.Save(world);
            menu.SetCompanyName("");
            menu.Show(MenuPage.NewCompany);
            Check(menu.ChosenName == Companies.DefaultName + " 2" && !Companies.Exists(menu.ChosenName), $"with a {Companies.DefaultName} on the books the page offers a free name ({menu.ChosenName})");
            var again = new Companies();
            again.AdoptNew("SELFTEST WORKS");
            Check(again.Dir != coDir && again.Name == "SELFTEST WORKS 2", $"a new company never adopts a taken folder ({again.Name})");
            string longName = new string('x', Companies.MaxName);
            var longCo = new Companies();
            longCo.Adopt(longName);
            longCo.Save(world);
            Check(!Companies.Exists(Companies.FreeName(longName)), "a free name is found even when a long name's folder would truncate onto the taken one");
            menu.NewCompanyRequested -= Record;
            menu.NewCompanyRequested += FoundCompany;
            menu.Close();
            Check(!Played(new World(seed: 1920)), "a works nobody has played is not saved (no phantom company from the founding page)");

            // A company whose books cannot be read at all is reported, not thrown at the player.
            string broken = System.IO.Path.Combine(Companies.Root, "broken-books");
            System.IO.Directory.CreateDirectory(broken);
            System.IO.File.WriteAllText(System.IO.Path.Combine(broken, SaveStore.FileName), "not a ledger");
            string? unreadable = null;
            Check(TryLoad(broken, ref unreadable) == null && unreadable == "broken-books", "an unreadable company fails to load quietly and is named");
            // Achievements: a first pour is pinned as a clipping.
            var ach = new Achievements();
            ach.Attach(coDir);
            world.Made(Item.SteelIngot, 1);
            ach.Poll(world);
            Check(ach.Has("FIRST_POUR") && System.IO.File.Exists(System.IO.Path.Combine(coDir, "achievements.json")), "making steel pins the First Pour clipping and records it");
            var reread = new Achievements();
            reread.Attach(coDir);
            int pinned = 0;
            reread.Unlocked += _ => pinned++;
            reread.Poll(world);
            Check(reread.Has("FIRST_POUR") && pinned == 0 && System.IO.File.ReadAllText(System.IO.Path.Combine(coDir, "achievements.json")).Contains("WeekStartSold"),
                "a company's clippings are pinned once: reopening its books does not pin them again, and the week's sales survive the load");
        }
        finally
        {
            if (System.IO.Directory.Exists(coDir))
                System.IO.Directory.Delete(coDir, true);
        }

        // Wheel zoom keeps the ground under the cursor still.
        if (!HandEmpty)
            Press(Key.Escape);
        var at = ScreenOf(new Cell(9, -3));
        var groundBefore = GetCanvasTransform().AffineInverse() * at;
        Click(MouseButton.WheelUp, at, true);
        camera.ForceUpdateScroll();
        var groundAfter = GetCanvasTransform().AffineInverse() * at;
        Check(Mathf.IsEqualApprox(camera.Zoom.X, 0.5f * 1.15f), "wheel zooms in");
        Check(groundBefore.DistanceTo(groundAfter) < 1f, "zoom holds the point under the cursor");

        // The clipping describes what is under the cursor.
        Move(ScreenOf(new Cell(12, -5)));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Describe(new Cell(12, -5))?.StartsWith("CANVAS BELT") == true, "hovering a belt prints its clipping");

        // Drags end with their button wherever it lands, and never outlive the paper, the office or a change of hand.
        {
            while (!HandEmpty)
                Press(Key.Escape);
            camera.Position = new Vector2(40, -20) * Ink.Tile;
            camera.Zoom = Vector2.One * 0.5f;
            camera.ForceUpdateScroll();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Press(Key.Key1);
            Click(MouseButton.Left, ScreenOf(new Cell(30, -24)), true);
            Move(ScreenOf(new Cell(32, -24)));
            Press(Key.Tab);
            Click(MouseButton.Left, ScreenOf(new Cell(32, -24)), false);
            Press(Key.Tab);
            Move(ScreenOf(new Cell(32, -21)));
            Check(BeltAt(31, -24) != null && BeltAt(32, -22) == null && BeltAt(32, -21) == null, "a drag cut short by the paper lays no belt when the mouse moves afterwards");
            while (!HandEmpty)
                Press(Key.Escape);
            for (int x = 30; x <= 36; x++)
                world.Apply(new Place(BuildingType.BeltCanvas, new Cell(x, -16), Dir.East));
            Click(MouseButton.Right, ScreenOf(new Cell(30, -16)), true);
            Press(Key.Escape);
            bool officeOpened = menu.IsOpen;
            Click(MouseButton.Right, ScreenOf(new Cell(30, -16)), false);
            if (menu.IsOpen)
                Press(Key.Escape);
            Move(ScreenOf(new Cell(34, -16)));
            Check(officeOpened && BeltAt(30, -16) == null && BeltAt(33, -16) != null && BeltAt(34, -16) != null, "a right-drag cut short by the office demolishes nothing when the mouse moves afterwards");
            // A right-drag that starts on bare ground is its own undo step, not part of the last one.
            Click(MouseButton.Right, ScreenOf(new Cell(33, -18)), true);
            Move(ScreenOf(new Cell(33, -16)));
            Click(MouseButton.Right, ScreenOf(new Cell(33, -16)), false);
            Check(BeltAt(33, -16) == null, "a right-drag from bare ground demolishes what it crosses");
            Press(Key.Z, ctrl: true);
            Check(BeltAt(33, -16) != null && BeltAt(30, -16) == null, "undo brings back that drag only, not the demolition before it");
            // The belt tool clicked on a machine, then dragged: the machine stays as it is, and undo takes only the drag.
            camera.Position = new Vector2(-6, 2) * Ink.Tile;
            camera.ForceUpdateScroll();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var smelt = world.BuildingAt(new Cell(-6, 2));
            var smeltFacing = smelt?.Facing;
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(-10, 8), Dir.East));
            Press(Key.Key1);
            Click(MouseButton.Left, ScreenOf(new Cell(-6, 2)), true);
            Move(ScreenOf(new Cell(-9, 2)));
            Click(MouseButton.Left, ScreenOf(new Cell(-9, 2)), false);
            Check(smelt != null && smelt.Facing == smeltFacing, $"dragging a belt off a machine does not turn the machine ({smeltFacing} → {smelt?.Facing})");
            Check(BeltAt(-7, 2) != null && BeltAt(-9, 2) != null, "the drag lays its belt from the machine's side");
            Press(Key.Z, ctrl: true);
            Check(BeltAt(-7, 2) == null && BeltAt(-9, 2) == null && BeltAt(-10, 8) != null, "undo takes that drag back and nothing done before it");
            while (!HandEmpty)
                Press(Key.Escape);
            camera.Position = new Vector2(40, -20) * Ink.Tile;
            camera.ForceUpdateScroll();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // A drag that starts on a belt already facing its way opens its own undo step too.
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(40, -12), Dir.East));
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(50, -12), Dir.East));
            Press(Key.Key1);
            facing = Dir.East;
            Click(MouseButton.Left, ScreenOf(new Cell(40, -12)), true);
            Move(ScreenOf(new Cell(43, -12)));
            Click(MouseButton.Left, ScreenOf(new Cell(43, -12)), false);
            Check(BeltAt(43, -12) != null, "a drag from an existing belt carries the run on");
            Press(Key.Z, ctrl: true);
            Check(BeltAt(41, -12) == null && BeltAt(43, -12) == null && BeltAt(50, -12) != null && BeltAt(40, -12) != null, "undo takes back that drag and not the belt laid before it");
            while (!HandEmpty)
                Press(Key.Escape);
            Press(Key.Key1);
            Click(MouseButton.Left, ScreenOf(new Cell(44, -20)), true);
            Press(Key.B);
            Check(!laying, "B in the middle of a drag ends the drag");
            Move(ScreenOf(new Cell(47, -20)));
            Click(MouseButton.Left, ScreenOf(new Cell(47, -20)), false);
            Check(BeltAt(46, -20) == null, "nothing is laid after the hand changed");
            while (!HandEmpty)
                Press(Key.Escape);
            // Focus lost in the middle of a drag.
            Press(Key.Key1);
            Click(MouseButton.Left, ScreenOf(new Cell(44, -22)), true);
            PropagateNotification((int)NotificationApplicationFocusOut);
            Move(ScreenOf(new Cell(47, -22)));
            Check(BeltAt(46, -22) == null, "a drag ends when the window loses focus");
            Click(MouseButton.Left, ScreenOf(new Cell(47, -22)), false);
            while (!HandEmpty)
                Press(Key.Escape);
        }

        // WASD looks about the works, but not while the office is open (a company's name is typed there).
        {
            menu.Open(OfficeLine(), null);
            var panBefore = camera.Position;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = true });
            for (int i = 0; i < 6; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = false });
            Check(camera.Position == panBefore, "keys typed in the office do not pan the works");
            menu.Close();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = true });
            for (int i = 0; i < 6; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = false });
            Check(camera.Position.X > panBefore.X, "with the office closed the same key looks about");
        }

        // Printed figures and names.
        Check(Hud.Money(-144) == "-$1.44" && Hud.Money(123456) == "$1,234.56", $"a debit prints as -$1.44 ({Hud.Money(-144)})");
        Check(CommissionName("COMMISSION_AGAIN_1", "ORDINAL_21") == "The 21st Coast-to-Coast Motorcade" && CommissionName("COMMISSION_AGAIN_2", "ORDINAL_13") == "The 13th Skyscraper"
              && CommissionName("COMMISSION_AGAIN_5", "SHIP_10") == "Zeppelin Liberty II", $"endless commissions keep English names ({CommissionName("COMMISSION_AGAIN_1", "ORDINAL_21")}, {CommissionName("COMMISSION_AGAIN_5", "SHIP_10")})");

        // The depot says what it is selling; the overlay says when a fire has gone out.
        {
            var mineHead = (Mine)world.BuildingAt(new Cell(6, -4))!;
            Check(entities.OverlayTagFor(mineHead) is { Trouble: false }, "a lit mine head prints its seam's yield in ink");
            while (mineHead.Firebox.Burn())
            {
            }
            mineHead.Source = PowerSource.None;
            Check(!mineHead.Lit && entities.OverlayTagFor(mineHead) is { Trouble: true } t && t.Text == Text.Get("OVERLAY_FIRE_OUT"), "a mine head whose fire is out prints FIRE OUT in red on the overlay");
            // Coal mixed into an ore line: a new machine's firebox is full, the lump is refused and the belt stops behind it.
            world.Apply(new Place(BuildingType.Smelter, new Cell(60, -40), Dir.East));
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(59, -39), Dir.East));
            var intoSmelter = ((Belt)world.BuildingAt(new Cell(59, -39))!).Line!;
            intoSmelter.TryInsert(world, intoSmelter.End, Item.Coal);
            for (int i = 0; i < 10; i++)
                world.Tick();
            var coldSmelter = (Machine)world.BuildingAt(new Cell(60, -40))!;
            Check(EntityView.Refused(world, coldSmelter) == Item.Coal && Describe(new Cell(60, -40))?.Contains("firebox is full") == true
                  && entities.OverlayTagFor(coldSmelter) is { Trouble: true } refusedTag && refusedTag.Text == Text.Get("OVERLAY_REFUSES_COAL"),
                  "coal stopped at a full firebox is named in the machine's clipping and in red on the overlay");
            world.Apply(new Place(BuildingType.FreightDepot, new Cell(60, -30), Dir.East));
            world.Apply(new Place(BuildingType.BeltCanvas, new Cell(59, -29), Dir.East));
            var line = ((Belt)world.BuildingAt(new Cell(59, -29))!).Line!;
            line.TryInsert(world, 0, Item.Coal);
            Check(Describe(new Cell(60, -30))?.Contains(Text.Get("ITEM_coal")) == true && Describe(new Cell(60, -30))?.Contains("Iron ore") == false, $"a depot's clipping names what comes in ({Describe(new Cell(60, -30))})");
        }

        // After the fold: nothing more is built, but the archive and the front office stay open to the player.
        {
            foreach (var type in Catalog.All.Where(t => Catalog.Of(t).Available && !Catalog.Of(t).IsVehicle).OrderByDescending(t => Catalog.Of(t).BaseCostCents))
                for (int n = 0, y = -200 - (int)type * 6; n < 400; n++)
                    if (world.Apply(new Place(type, new Cell(200 + n * 4, y), Dir.East)) == PlaceResult.TooExpensive)
                        break;
            // Purchases keep two weeks' interest in hand, so spending alone cannot fold a company: with nothing
            // coming in, the drawn line folds it some weeks on, after the Banker's Warning.
            for (long i = 0; i < 10L * World.TicksPerWeek && !world.Ended; i++)
                world.Tick();
            var warned = world.Notices.FirstOrDefault(n => n.Key == "BANKERS_WARNING");
            var folded = world.Notices.LastOrDefault(n => n.Key == "DEFAULT");
            Check(world.Ended && warned != null && folded != null && folded.Tick - warned.Tick >= World.TicksPerWeek,
                $"spending everything on credit and earning nothing folds the company, warned a week ahead (debt {Hud.Money(world.DebtCents)}; " +
                string.Join(" ", world.Notices.Select(n => $"{n.Key}@{n.Tick}")) + $"; tick {world.TickCount}; ended {world.Ended})");
            Press(Key.Key1);
            for (int i = 0; i < 2; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(HandEmpty, "after the fold the hand is empty and stays so");
            Press(Key.Escape);
            Check(menu.IsOpen, "after the fold, Esc opens the front office");
            if (menu.IsOpen)
                Press(Key.Escape);
            Press(Key.Tab);
            Check(paper.IsOpen, "after the fold, Tab opens the Courier (the archive remains)");
            if (paper.IsOpen)
                Press(Key.Tab);
        }

        if (System.IO.Directory.Exists(sandbox))
            System.IO.Directory.Delete(sandbox, true);
        Companies.UserRoot = "user://";

        GD.Print(failures.Count == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures.Count})");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }
}
