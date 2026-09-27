using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

public partial class Main
{
    /// <summary>
    /// <c>--playtest=&lt;dir&gt;</c>: plays the opening as a brand-new player would, through the real input path, and saves a
    /// screenshot at every step into the folder. It keeps its companies and settings in a sandbox
    /// (<c>user://playtest/</c>, emptied first), so it never touches the player's own. The founding page reloads the
    /// scene; the stage survives in a static and the new scene carries on.
    /// </summary>
    static string? playtest;
    static int playtestStage;

    void BeginPlaytest(string[] args)
    {
        if (playtest == null)
        {
            var arg = args.First(a => a.StartsWith("--playtest"));
            playtest = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : ProjectSettings.GlobalizePath("user://playtest-shots");
            Companies.UserRoot = "user://playtest/";
            string sandbox = ProjectSettings.GlobalizePath(Companies.UserRoot);
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, true);
            Directory.CreateDirectory(playtest);
        }
        CallDeferred(MethodName.RunPlaytest);
    }

    async void RunPlaytest()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        async System.Threading.Tasks.Task Frames(int n)
        {
            for (int i = 0; i < n; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async System.Threading.Tasks.Task Shot(string name)
        {
            await Frames(3);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(playtest!, name + ".png"));
            GD.Print($"playtest: {name}");
        }
        void Push(InputEvent e) => GetViewport().PushInput(e);
        void Press(Key k, bool ctrl = false, bool shift = false)
        {
            Push(new InputEventKey { Keycode = k, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift });
            Push(new InputEventKey { Keycode = k, Pressed = false, CtrlPressed = ctrl, ShiftPressed = shift });
        }
        void Type(string text)
        {
            foreach (char c in text)
            {
                Push(new InputEventKey { Keycode = Key.None, Unicode = c, Pressed = true });
                Push(new InputEventKey { Keycode = Key.None, Unicode = c, Pressed = false });
            }
        }
        void Click(MouseButton b, Vector2 at, bool pressed) =>
            Push(new InputEventMouseButton { ButtonIndex = b, Position = at, GlobalPosition = at, Pressed = pressed });
        void Tap(Vector2 at)
        {
            Click(MouseButton.Left, at, true);
            Click(MouseButton.Left, at, false);
        }
        void Move(Vector2 at) => Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Vector2 ScreenOf(Cell c) => GetCanvasTransform() * (new Vector2(c.X + 0.5f, c.Y + 0.5f) * Ink.Tile);
        void Drag(Cell from, Cell to)
        {
            Click(MouseButton.Left, ScreenOf(from), true);
            Move(ScreenOf(to));
            Click(MouseButton.Left, ScreenOf(to), false);
        }
        Control? Find(Node root, System.Func<Control, bool> match)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is Control c && c.IsVisibleInTree() && match(c))
                    return c;
                if (Find(child, match) is { } deeper)
                    return deeper;
            }
            return null;
        }
        void ClickButton(Node root, string text)
        {
            if (Find(root, c => c is Button b && b.Text == text) is { } b)
                Tap(b.GetGlobalRect().GetCenter());
            else
                GD.PushError($"playtest: no button '{text}'");
        }
        void Tick(int seconds)
        {
            for (int i = 0; i < seconds * World.TicksPerSecond; i++)
                world.Tick();
        }

        await Frames(5);
        if (playtestStage == 0)
        {
            // A first run: no company on the books, so the founding page is open.
            await Shot("p01-first-run");
            var field = (LineEdit)Find(menu, c => c is LineEdit)!;
            Tap(field.GetGlobalRect().GetCenter());
            Press(Key.A, ctrl: true);
            Press(Key.Backspace);
            ClickButton(menu, Text.Get("MENU_START"));
            await Shot("p02-empty-name-refused");
            Tap(field.GetGlobalRect().GetCenter());
            Type("Kestrel Iron & Steel");
            ClickButton(menu, Text.Get("DIFF_hard_times"));
            await Shot("p03-name-typed");
            playtestStage = 1;
            ClickButton(menu, Text.Get("MENU_START"));   // reloads the scene; the next Main carries on
            return;
        }

        // Stage 1: the new company, following the first editions through the paper.
        camera.Zoom = Vector2.One * 0.8f;
        camera.Position = new Vector2(12, -3) * Ink.Tile;
        camera.ForceUpdateScroll();
        await Shot("p04-new-company");
        Press(Key.Tab);
        await Shot("p05-paper-edition-1");
        Tap(Find(paper, c => c is Label l && l.Text == Text.Get("PAGE_CLASSIFIEDS"))!.GetGlobalRect().GetCenter());
        await Shot("p06-classifieds-ringed");
        Tap(paper.Ads.First(a => a.Type == BuildingType.MineHead).GetGlobalRect().GetCenter());
        Move(ScreenOf(new Cell(6, -4)));
        await Shot("p07-mine-in-hand");
        Tap(ScreenOf(new Cell(6, -4)));
        Press(Key.Escape);
        await Shot("p08-mine-down-edition-2");
        Press(Key.Tab);
        await Shot("p09-paper-edition-2");
        Press(Key.Escape);
        // Belt from the chute east, a smelter, a short belt and a depot (the second and third editions' advice).
        hud.ShowColumn(AdColumn.Transport);
        Press(Key.Key1);
        Drag(new Cell(8, -3), new Cell(12, -3));
        hud.ShowColumn(AdColumn.MillsAndFurnaces);
        Press(Key.Key1);
        Tap(ScreenOf(new Cell(13, -4)));
        hud.ShowColumn(AdColumn.Transport);
        Press(Key.Key1);
        Drag(new Cell(15, -3), new Cell(16, -3));
        hud.ShowColumn(AdColumn.Extraction);
        Press(Key.Key1 + Catalog.InColumn(AdColumn.Extraction).ToList().IndexOf(BuildingType.FreightDepot));
        Tap(ScreenOf(new Cell(17, -4)));
        Press(Key.Escape);
        Move(ScreenOf(new Cell(17, -4)));
        Tick(40);
        await Shot("p10-first-line-depot-clipping");
        Press(Key.F);
        await Shot("p11-flow-overlay");
        Press(Key.F);
        Press(Key.Tab);
        await Shot("p12-paper-edition-3");
        Tap(Find(paper, c => c is Label l && l.Text == Text.Get("PAGE_MARKET"))!.GetGlobalRect().GetCenter());
        await Shot("p13-market");
        Press(Key.Escape);
        // Days later, with the starter coal burnt: the overlay must say the fires are out.
        Tick(900);
        Press(Key.F);
        Move(ScreenOf(new Cell(6, -4)));
        await Shot("p14-fire-out-overlay");
        Press(Key.F);
        // The front office: pause, the companies on the books (a long list, as after many foldings), the settings.
        companies.Save(world);
        for (int n = 2; n <= 16; n++)
        {
            string copy = companies.Dir + $"-{n}";
            Directory.CreateDirectory(copy);
            File.Copy(Path.Combine(companies.Dir!, SaveStore.FileName), Path.Combine(copy, SaveStore.FileName), true);
        }
        Press(Key.Escape);
        await Shot("p15-office");
        ClickButton(menu, Text.Get("MENU_COMPANIES"));
        await Shot("p16-office-companies");
        ClickButton(menu, Text.Get("MENU_BACK"));
        ClickButton(menu, Text.Get("MENU_NEW"));
        await Shot("p17-office-new-name-offered");
        ClickButton(menu, Text.Get("MENU_BACK"));
        Press(Key.Escape);
        // The fold: spend everything and earn nothing, then run on until the bank calls in the line.
        foreach (var type in Catalog.All.Where(t => Catalog.Of(t).Available && !Catalog.Of(t).IsVehicle).OrderByDescending(t => Catalog.Of(t).BaseCostCents))
            for (int n = 0, y = -200 - (int)type * 6; n < 400; n++)
                if (world.Apply(new Place(type, new Cell(200 + n * 4, y), Dir.East)) == PlaceResult.TooExpensive)
                    break;
        // Purchases keep two weeks' interest in hand, so the drawn line folds the company some weeks on, warned.
        for (long i = 0; i < 10L * World.TicksPerWeek && !world.Ended; i++)
            world.Tick();
        await Shot("p18-folded");
        Press(Key.Tab);
        await Shot("p19-folded-paper");
        Press(Key.Escape);
        Press(Key.Escape);
        await Shot("p20-folded-office");
        GD.Print($"PLAYTEST DONE ({playtest})");
        GetTree().Quit();
    }
}
