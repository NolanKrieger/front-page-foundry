using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

public enum MenuPage { Front, NewCompany, Companies, Settings }

/// <summary>
/// The front office (GDD §13): Esc opens it and it is the only pause there is. Continue, a new
/// company (name and one of three difficulties), the company list, settings, and quitting to the
/// desk. Printed like everything else, on a sheet of the Courier's paper.
/// </summary>
public partial class Menu : CanvasLayer
{
    static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public event Action? ContinueRequested;
    public event Action<string, Difficulty>? NewCompanyRequested;
    public event Action<string>? LoadRequested;
    public event Action? QuitRequested;

    PanelContainer sheet = null!;
    readonly Dictionary<MenuPage, Control> pages = new();
    LineEdit nameEdit = null!;
    Label difficultyBlurb = null!, recovered = null!, companyLine = null!, nameError = null!;
    VBoxContainer companyList = null!;
    ScrollContainer companyScroll = null!;
    HSlider music = null!, sfx = null!;
    CheckBox mute = null!;
    readonly Dictionary<Difficulty, Button> difficultyButtons = new();
    Settings settings = null!;

    public MenuPage Page { get; private set; } = MenuPage.Front;
    public bool IsOpen => Visible;
    public Difficulty ChosenDifficulty { get; private set; } = Difficulty.SteadyTrade;
    public string ChosenName => nameEdit.Text;
    public int CompaniesListed { get; private set; }
    /// <summary>Why the founding page refused the name, or null.</summary>
    public string? NameProblem => nameError.Visible ? nameError.Text : null;

    public void Init(Settings settings)
    {
        this.settings = settings;
        Layer = 30;
        Visible = false;

        sheet = new PanelContainer();
        sheet.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        sheet.GrowHorizontal = Control.GrowDirection.Both;
        sheet.GrowVertical = Control.GrowDirection.Both;
        sheet.CustomMinimumSize = new Vector2(640, 0);
        var style = new StyleBoxFlat
        {
            BgColor = Ink.Paper, BorderColor = Ink.Black,
            ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 22, ContentMarginBottom = 26,
            ShadowColor = Ink.Black with { A = 0.35f }, ShadowSize = 18,
        };
        style.SetBorderWidthAll(4);
        sheet.AddThemeStyleboxOverride("panel", style);
        AddChild(sheet);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        sheet.AddChild(col);
        col.AddChild(Type.Text(Text.Get("MENU_TITLE"), Type.Fell, 44, HorizontalAlignment.Center));
        col.AddChild(Type.Rule(3));
        companyLine = Type.Text("", Type.SerifItalic, 14, HorizontalAlignment.Center);
        companyLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        companyLine.CustomMinimumSize = new Vector2(560, 0);
        col.AddChild(companyLine);
        recovered = Type.Text("", Type.SerifBold, 13, HorizontalAlignment.Center, Ink.Red);
        recovered.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        recovered.CustomMinimumSize = new Vector2(560, 0);
        recovered.Visible = false;
        col.AddChild(recovered);

        pages[MenuPage.Front] = BuildFront();
        pages[MenuPage.NewCompany] = BuildNew();
        pages[MenuPage.Companies] = BuildCompanies();
        pages[MenuPage.Settings] = BuildSettings();
        foreach (var page in pages.Values)
            col.AddChild(page);
        Show(MenuPage.Front);
    }

    Button PaperButton(string text, Action onPressed, int size = 20)
    {
        var b = new Button { Text = text, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontOverride("font", Type.Fell);
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", Ink.Black);
        b.AddThemeColorOverride("font_hover_color", Ink.Red);
        b.AddThemeColorOverride("font_pressed_color", Ink.Red);
        b.AddThemeColorOverride("font_focus_color", Ink.Black);
        var normal = new StyleBoxFlat { BgColor = Ink.Paper, BorderColor = Ink.Black, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 6, ContentMarginBottom = 6 };
        normal.SetBorderWidthAll(2);
        var hover = new StyleBoxFlat { BgColor = Ink.Paper, BorderColor = Ink.Red, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 6, ContentMarginBottom = 6 };
        hover.SetBorderWidthAll(2);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", normal);
        b.Pressed += onPressed;
        return b;
    }

    Control BuildFront()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        v.AddChild(Type.Text(Text.Get("MENU_PAUSED"), Type.SerifItalic, 13, HorizontalAlignment.Center));
        v.AddChild(PaperButton(Text.Get("MENU_CONTINUE"), () => ContinueRequested?.Invoke()));
        v.AddChild(PaperButton(Text.Get("MENU_NEW"), () => Show(MenuPage.NewCompany)));
        v.AddChild(PaperButton(Text.Get("MENU_COMPANIES"), () => Show(MenuPage.Companies)));
        v.AddChild(PaperButton(Text.Get("MENU_SETTINGS"), () => Show(MenuPage.Settings)));
        v.AddChild(PaperButton(Text.Get("MENU_QUIT"), () => QuitRequested?.Invoke()));
        return v;
    }

    Control BuildNew()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        v.AddChild(Type.Text(Text.Get("MENU_NAME_LABEL"), Type.SerifBold, 14, HorizontalAlignment.Center));
        nameEdit = new LineEdit { Text = Companies.DefaultName, MaxLength = Companies.MaxName, Alignment = HorizontalAlignment.Center };
        nameEdit.AddThemeFontOverride("font", Type.Fell);
        nameEdit.AddThemeFontSizeOverride("font_size", 22);
        // Ink on newsprint, ruled like a form, not the engine's dark field (black type on grey could not be read).
        nameEdit.AddThemeColorOverride("font_color", Ink.Black);
        nameEdit.AddThemeColorOverride("font_uneditable_color", Ink.Soft);
        nameEdit.AddThemeColorOverride("font_placeholder_color", Ink.Soft);
        nameEdit.AddThemeColorOverride("caret_color", Ink.Black);
        nameEdit.AddThemeColorOverride("selection_color", Ink.Red with { A = 0.3f });
        nameEdit.AddThemeColorOverride("font_selected_color", Ink.Black);
        nameEdit.AddThemeStyleboxOverride("normal", FieldStyle(Ink.Black));
        nameEdit.AddThemeStyleboxOverride("focus", FieldStyle(Ink.Red));
        nameEdit.AddThemeStyleboxOverride("read_only", FieldStyle(Ink.Soft));
        nameEdit.TextSubmitted += _ => TryFound();
        // The field reports a change at the end of the frame; only a name other than the one refused clears the line.
        nameEdit.TextChanged += text => nameError.Visible &= text.Trim() == checkedName;
        v.AddChild(nameEdit);
        nameError = Type.Text("", Type.SerifBold, 13, HorizontalAlignment.Center, Ink.Red);
        nameError.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        nameError.CustomMinimumSize = new Vector2(520, 0);
        nameError.Visible = false;
        v.AddChild(nameError);
        v.AddChild(Type.Text(Text.Get("MENU_DIFFICULTY"), Type.SerifBold, 14, HorizontalAlignment.Center));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        foreach (var d in Difficulty.All)
        {
            var pick = d;
            var b = PaperButton(Text.Get("DIFF_" + d.Id), () => Choose(pick), 18);
            difficultyButtons[d] = b;
            row.AddChild(b);
        }
        v.AddChild(row);
        difficultyBlurb = Type.Text("", Type.SerifItalic, 13, HorizontalAlignment.Center);
        difficultyBlurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        difficultyBlurb.CustomMinimumSize = new Vector2(520, 0);
        v.AddChild(difficultyBlurb);
        Choose(Difficulty.SteadyTrade);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 12);
        actions.AddChild(PaperButton(Text.Get("MENU_START"), TryFound));
        actions.AddChild(PaperButton(Text.Get("MENU_BACK"), () => Show(MenuPage.Front), 16));
        v.AddChild(actions);
        return v;
    }

    static StyleBoxFlat FieldStyle(Color rule)
    {
        var box = new StyleBoxFlat
        {
            BgColor = Ink.Paper.Darkened(0.04f), BorderColor = rule,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        box.BorderWidthBottom = 2;
        return box;
    }

    /// <summary>
    /// Founds the company if its name will do: it needs letters or figures, and it may not file under a folder that a
    /// company on the books already keeps (founding would roll that company's saves away).
    /// </summary>
    string checkedName = "";

    public void TryFound()
    {
        string name = checkedName = nameEdit.Text.Trim();
        string? problem = !Companies.Valid(name) ? Text.Get("MENU_NAME_EMPTY")
            : Companies.Exists(name) ? Text.Get("MENU_NAME_TAKEN")
            : null;
        nameError.Text = problem ?? "";
        nameError.Visible = problem != null;
        if (problem == null)
        {
            // Let go of the keyboard before the scene reloads under the field.
            nameEdit.ReleaseFocus();
            NewCompanyRequested?.Invoke(name, ChosenDifficulty);
        }
    }

    /// <summary>For the self-test: what is in the name field.</summary>
    public void SetCompanyName(string name) => nameEdit.Text = name;

    void Choose(Difficulty d)
    {
        ChosenDifficulty = d;
        foreach (var (diff, button) in difficultyButtons)
            button.AddThemeColorOverride("font_color", diff == d ? Ink.Red : Ink.Black);
        difficultyBlurb.Text = Text.Get("DIFF_" + d.Id + "_BLURB", Hud.Money(d.StartingCashCents), Hud.Money(d.BaseCreditCents),
            (d.WeeklyInterestPermille / 10.0).ToString("0.#", En) + "%");
    }

    Control BuildCompanies()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        companyList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        companyList.AddThemeConstantOverride("separation", 6);
        // A long list scrolls inside the sheet instead of running off the page.
        companyScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        companyScroll.AddChild(companyList);
        v.AddChild(companyScroll);
        v.AddChild(PaperButton(Text.Get("MENU_BACK"), () => Show(MenuPage.Front), 16));
        return v;
    }

    void RefreshCompanies()
    {
        foreach (var child in companyList.GetChildren())
        {
            companyList.RemoveChild(child);
            child.QueueFree();
        }
        var list = Companies.List();
        CompaniesListed = list.Count;
        if (list.Count == 0)
        {
            companyList.AddChild(Type.Text(Text.Get("MENU_NO_COMPANIES"), Type.SerifItalic, 14, HorizontalAlignment.Center));
            FitCompanyList();
            return;
        }
        foreach (var (dir, header) in list)
        {
            string when = DateTime.TryParse(header.SavedAt, En, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime().ToString("d MMM yyyy HH:mm", En) : header.SavedAt;
            string row = Text.Get("MENU_COMPANY_ROW", header.Company, Hud.DateOf(header.Tick), Hud.Money(header.ShareCents), Text.Get("DIFF_" + header.Difficulty), when);
            var d = dir;
            companyList.AddChild(PaperButton(row, () => LoadRequested?.Invoke(d), 15));
        }
        FitCompanyList();
    }

    /// <summary>The list shows whole up to about half the screen, then scrolls.</summary>
    void FitCompanyList()
    {
        var want = companyList.GetCombinedMinimumSize();
        float room = Mathf.Max(120f, GetViewport().GetVisibleRect().Size.Y * 0.5f);
        companyScroll.CustomMinimumSize = new Vector2(want.X, Mathf.Min(want.Y, room));
    }

    Control BuildSettings()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        music = Slider(v, Text.Get("MENU_MUSIC"), settings.MusicDb, db => { settings.MusicDb = db; settings.Apply(); });
        sfx = Slider(v, Text.Get("MENU_SFX"), settings.SfxDb, db => { settings.SfxDb = db; settings.Apply(); });
        mute = new CheckBox { Text = Text.Get("MENU_MUTE"), ButtonPressed = settings.Mute };
        mute.AddThemeFontOverride("font", Type.Serif);
        mute.AddThemeColorOverride("font_color", Ink.Black);
        mute.Toggled += on => { settings.Mute = on; settings.Apply(); };
        v.AddChild(mute);
        v.AddChild(PaperButton(Text.Get("MENU_BACK"), () => { settings.Save(); Show(MenuPage.Front); }, 16));
        return v;
    }

    static HSlider Slider(VBoxContainer into, string label, float db, Action<float> changed)
    {
        into.AddChild(Type.Text(label, Type.SerifBold, 14));
        var s = new HSlider { MinValue = -40, MaxValue = 6, Step = 1, Value = db, CustomMinimumSize = new Vector2(400, 20) };
        s.ValueChanged += v => changed((float)v);
        into.AddChild(s);
        return s;
    }

    public void SetMusicDb(float db) => music.Value = db;
    public float MusicDb => (float)music.Value;

    public void Show(MenuPage page)
    {
        Page = page;
        foreach (var (p, control) in pages)
            control.Visible = p == page;
        if (page == MenuPage.Companies)
            RefreshCompanies();
        if (page == MenuPage.NewCompany)
        {
            // The name offered is always one no company on the books files under.
            if (!Companies.Valid(nameEdit.Text) || Companies.Exists(nameEdit.Text))
                nameEdit.Text = Companies.FreeName(Companies.DefaultName);
            nameError.Visible = false;
            nameEdit.GrabFocus();
            nameEdit.CaretColumn = nameEdit.Text.Length;
        }
    }

    /// <summary>
    /// Opens on a page (the front page, or the founding page for a first run). <paramref name="line"/> says whose books
    /// these are; <paramref name="notice"/>, printed in red, says what went wrong with them, if anything.
    /// </summary>
    public void Open(string line, string? notice, MenuPage page = MenuPage.Front)
    {
        companyLine.Text = line;
        recovered.Visible = notice != null;
        recovered.Text = notice ?? "";
        Visible = true;
        Show(page);
    }

    /// <summary>Closing the office keeps whatever was set on the settings page.</summary>
    public void Close()
    {
        if (Visible)
            settings.Save();
        Visible = false;
    }
}
