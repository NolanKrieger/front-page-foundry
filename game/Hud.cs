using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// The newspaper around the factory: the Courier's masthead with the dateline and your
/// cash, classified ads that are the build menu, and a clipping describing whatever is
/// under the cursor. Every line of text comes from the string table.
/// </summary>
public partial class Hud : CanvasLayer
{
    static readonly DateTime FirstEdition = new(1920, 3, 1);
    static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public event Action<BuildingType>? AdClicked;

    readonly Dictionary<BuildingType, AdCard> ads = new();
    readonly Dictionary<AdColumn, Label> tabs = new();
    Label flash = null!;
    double flashUntil;
    /// <summary>The classified column on show; keys 1–9 pick within it.</summary>
    public AdColumn Column { get; private set; } = AdColumn.Transport;
    Label dateline = null!, cash = null!, ore = null!, cursorNote = null!, clipping = null!, notice = null!, mode = null!;
    /// <summary>Planning / blueprint / overlay, printed in red under the hint while one is on.</summary>
    public string? Mode { get; set; }
    PanelContainer masthead = null!, clippingPanel = null!, foldPanel = null!;
    Label foldBody = null!;
    FontFile blackletter = null!, fell = null!, serif = null!, serifItalic = null!, serifBold = null!;
    World world = null!;

    public void Init(World world)
    {
        this.world = world;
        Layer = 10;
        blackletter = GD.Load<FontFile>("res://assets/fonts/UnifrakturMaguntia-Book.ttf");
        fell = GD.Load<FontFile>("res://assets/fonts/IMFellEnglish-Roman.ttf");
        serif = GD.Load<FontFile>("res://assets/fonts/OldStandard-Regular.ttf");
        serifItalic = GD.Load<FontFile>("res://assets/fonts/OldStandard-Italic.ttf");
        serifBold = GD.Load<FontFile>("res://assets/fonts/OldStandard-Bold.ttf");

        BuildMasthead();
        BuildAds();
        BuildNotes();
    }

    LabelSettings Type(Font font, int size, Color? color = null) =>
        new() { Font = font, FontSize = size, FontColor = color ?? Ink.Black };

    Label Text(string text, Font font, int size, HorizontalAlignment align = HorizontalAlignment.Left) =>
        new() { Text = text, LabelSettings = Type(font, size), HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>The money line's text, for tests.</summary>
    public string MoneyLine => cash.Text;

    static ColorRect Rule(float height) =>
        new() { Color = Ink.Black, CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    void BuildMasthead()
    {
        var bar = masthead = new PanelContainer();
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ink.Paper with { A = 0.97f },
            ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 6, ContentMarginBottom = 0,
        });
        AddChild(bar);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        bar.AddChild(col);

        var top = new HBoxContainer();
        col.AddChild(top);
        var ears = new[]
        {
            Text(FrontPageFoundry.Text.Get("EAR_LEFT"), serif, 12),
            Text(FrontPageFoundry.Text.Get("MASTHEAD"), blackletter, 46, HorizontalAlignment.Center),
            Text(FrontPageFoundry.Text.Get("EAR_RIGHT"), serif, 12, HorizontalAlignment.Right),
        };
        foreach (var l in ears)
        {
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.VerticalAlignment = VerticalAlignment.Center;
            top.AddChild(l);
        }
        ears[1].SizeFlagsStretchRatio = 2.4f;

        col.AddChild(Rule(3));
        var line = new HBoxContainer();
        col.AddChild(line);
        dateline = Text("", serif, 14);
        cash = Text("", serifBold, 15, HorizontalAlignment.Center);
        ore = Text("", serif, 14, HorizontalAlignment.Right);
        foreach (var l in new[] { dateline, cash, ore })
        {
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(l);
        }
        col.AddChild(Rule(1));
        var hint = Text(FrontPageFoundry.Text.Get("HINT"), serifItalic, 13, HorizontalAlignment.Center);
        hint.LabelSettings.FontColor = Ink.Soft;
        // The whole list of controls is long: on a narrow screen it wraps rather than pushing the masthead off the page.
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(hint);
        mode = Text("", serifBold, 13, HorizontalAlignment.Center);
        mode.LabelSettings.FontColor = Ink.Red;
        mode.Visible = false;
        col.AddChild(mode);
        notice = Text("", serifBold, 14, HorizontalAlignment.Center);
        notice.LabelSettings.FontColor = Ink.Red;
        notice.Visible = false;
        col.AddChild(notice);
        flash = Text("", serifBold, 15, HorizontalAlignment.Center);
        flash.LabelSettings.FontColor = Ink.Red;
        flash.Visible = false;
        col.AddChild(flash);

        // The last edition: printed over everything when the company folds.
        foldPanel = new PanelContainer { Visible = false };
        foldPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        foldPanel.GrowHorizontal = Control.GrowDirection.Both;
        foldPanel.GrowVertical = Control.GrowDirection.Both;
        var foldStyle = new StyleBoxFlat { BgColor = Ink.Paper, BorderColor = Ink.Black, ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 24, ContentMarginBottom = 28 };
        foldStyle.SetBorderWidthAll(4);
        foldPanel.AddThemeStyleboxOverride("panel", foldStyle);
        var foldCol = new VBoxContainer();
        foldCol.AddChild(Text(FrontPageFoundry.Text.Get("NOTICE_FOLDS_TITLE"), fell, 64, HorizontalAlignment.Center));
        foldCol.AddChild(Rule(2));
        foldBody = Text("", serif, 16, HorizontalAlignment.Center);
        foldBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foldBody.CustomMinimumSize = new Vector2(520, 0);
        foldCol.AddChild(foldBody);
        // What is left to do: read the archive, or found another company from the front office.
        var foldHint = Text(FrontPageFoundry.Text.Get("NOTICE_FOLDS_HINT"), serifItalic, 14, HorizontalAlignment.Center);
        foldHint.LabelSettings.FontColor = Ink.Soft;
        foldHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foldHint.CustomMinimumSize = new Vector2(520, 0);
        foldCol.AddChild(foldHint);
        foldPanel.AddChild(foldCol);
        AddChild(foldPanel);
    }

    void BuildAds()
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        column.GrowVertical = Control.GrowDirection.Begin;
        column.OffsetBottom = -12;
        column.OffsetLeft = 24;
        column.OffsetRight = -24;
        AddChild(column);

        // The heading and the column tabs sit on a slip of newsprint, so the works beneath never prints through the type.
        var slip = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        slip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ink.Paper with { A = 0.97f },
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 2, ContentMarginBottom = 3,
        });
        column.AddChild(slip);
        var slipCol = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        slipCol.AddThemeConstantOverride("separation", 0);
        slip.AddChild(slipCol);
        var heading = Text(FrontPageFoundry.Text.Get("ADS_HEADING"), serifBold, 13, HorizontalAlignment.Center);
        slipCol.AddChild(heading);

        // The column tabs: one line of small caps, the open one underlined in ink.
        var tabRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabRow.AddThemeConstantOverride("separation", 22);
        slipCol.AddChild(tabRow);
        foreach (var col in Catalog.Columns)
        {
            var tab = Text(FrontPageFoundry.Text.Get("COL_" + col), serif, 12, HorizontalAlignment.Center);
            tab.MouseFilter = Control.MouseFilterEnum.Stop;
            tab.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var c = col;
            tab.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    ShowColumn(c);
            };
            tabRow.AddChild(tab);
            tabs[col] = tab;
        }
        var hintTab = Text(FrontPageFoundry.Text.Get("COL_HINT"), serifItalic, 11, HorizontalAlignment.Center);
        hintTab.LabelSettings.FontColor = Ink.Soft;
        tabRow.AddChild(hintTab);

        var row = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = FlowContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("h_separation", 10);
        row.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(row);

        foreach (var type in Catalog.All)
        {
            if (!Catalog.Of(type).Available)
                continue;
            var card = new AdCard(type, t => AdClicked?.Invoke(t));
            row.AddChild(card);
            ads[type] = card;
        }
        ShowColumn(Column);
    }

    /// <summary>Turns the classifieds to one column.</summary>
    public void ShowColumn(AdColumn column)
    {
        Column = column;
        foreach (var (type, card) in ads)
            card.Visible = Catalog.Of(type).Column == column;
        foreach (var (col, tab) in tabs)
        {
            tab.LabelSettings.FontColor = col == column ? Ink.Black : Ink.Soft;
            tab.LabelSettings.Font = col == column ? serifBold : serif;
        }
    }

    public void NextColumn() => ShowColumn((AdColumn)(((int)Column + 1) % Catalog.Columns.Length));

    void BuildNotes()
    {
        cursorNote = Text("", serifBold, 15);
        cursorNote.ZIndex = 1;
        AddChild(cursorNote);

        clippingPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        clippingPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        clippingPanel.OffsetLeft = 18;
        clippingPanel.OffsetTop = 150;
        var style = new StyleBoxFlat
        {
            BgColor = Ink.Paper,
            BorderColor = Ink.Soft,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
            ShadowColor = Ink.Faint, ShadowSize = 6, ShadowOffset = new Vector2(3, 3),
        };
        style.SetBorderWidthAll(1);
        clippingPanel.AddThemeStyleboxOverride("panel", style);
        clipping = Text("", serif, 14);
        clipping.CustomMinimumSize = new Vector2(280, 0);
        clipping.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        clippingPanel.AddChild(clipping);
        AddChild(clippingPanel);
    }

    /// <summary>Screen rectangle of an advertisement, for tests.</summary>
    public Rect2 AdRect(BuildingType type) => ads[type].GetGlobalRect();

    /// <summary>Dollars and cents; a debit prints with its sign before the dollar mark ("-$1.44"), as a ledger would.</summary>
    public static string Money(long cents) => (cents < 0 ? "-$" : "$") + (Math.Abs(cents) / 100m).ToString("N2", En);

    public static DateTime DateAt(long tick) => FirstEdition.AddDays(tick / World.TicksPerDay);
    public static string DateOf(long tick) => DateAt(tick).ToString("dddd, MMMM d, yyyy", En).ToUpperInvariant();
    public static string DayName(long tick) => DateAt(tick).ToString("dddd", En).ToUpperInvariant();

    /// <summary>A new edition: its headline runs under the masthead for a while.</summary>
    public void Flash(string headline, double now)
    {
        flash.Text = FrontPageFoundry.Text.Get("PAPER_FLASH", headline);
        flash.Visible = true;
        flashUntil = now + 10;
    }

    /// <summary>Called every frame by Main.</summary>
    public void Refresh(BuildingType? tool, Ghost? ghost, Vector2 mouse, string? hover, string? note)
    {
        dateline.Text = FrontPageFoundry.Text.Get("DATELINE", DateOf(world.TickCount));
        cash.Text = FrontPageFoundry.Text.Get("MASTHEAD_MONEY", Money(world.CashCents), Money(world.DebtCents), Money(world.CreditLimitCents), Money(world.SharePriceCents));
        mode.Visible = Mode != null;
        if (Mode != null)
            mode.Text = Mode;
        notice.Visible = world.BankersWarning && !world.Ended;
        if (notice.Visible)
            notice.Text = FrontPageFoundry.Text.Get("NOTICE_BANKERS_WARNING", Money(world.NextInterestCents));
        foldPanel.Visible = world.Ended;
        if (world.Ended)
        {
            var missed = world.Notices.LastOrDefault(n => n.Key == "DEFAULT");
            foldBody.Text = FrontPageFoundry.Text.Get("NOTICE_FOLDS_BODY", Money(missed?.Amount ?? 0));
        }

        double off = world.Market.Discount(Item.IronOre);
        string glut = off >= 0.005 ? FrontPageFoundry.Text.Get("MARKET_GLUT", off.ToString("P0", En)) : "";
        ore.Text = FrontPageFoundry.Text.Get("MARKET_LINE", FrontPageFoundry.Text.Get("ITEM_iron_ore").ToUpperInvariant(), Money(world.Market.PriceCents(Item.IronOre))) + glut;

        var circled = world.Paper.Latest?.CircledAd;
        foreach (var (type, card) in ads)
            card.Refresh(world, tool == type, circled == type);
        if (flash.Visible && Godot.Time.GetTicksMsec() / 1000.0 > flashUntil)
            flash.Visible = false;

        cursorNote.Visible = note != null;
        if (note != null)
        {
            cursorNote.Text = note;
            cursorNote.LabelSettings.FontColor = ghost is { Status: not PlaceResult.Ok } ? Ink.Red : Ink.Black;
            // Beside the cursor, but flipped to its other side near the right or bottom edge so it never runs off the page.
            var size = cursorNote.GetMinimumSize();
            var screen = GetViewport().GetVisibleRect().Size;
            var at = mouse + new Vector2(18, 14);
            if (at.X + size.X > screen.X - 8)
                at.X = Math.Max(8, mouse.X - 12 - size.X);
            if (at.Y + size.Y > screen.Y - 8)
                at.Y = Math.Max(8, mouse.Y - 10 - size.Y);
            cursorNote.Position = at;
            cursorNote.Size = size;
        }

        // The clipping hangs just under the masthead, however many lines the masthead carries today.
        clippingPanel.OffsetTop = masthead.Size.Y + 14;
        clippingPanel.Visible = hover != null;
        if (hover != null)
            clipping.Text = hover;
    }
}
