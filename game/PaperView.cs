using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

public enum Page { Front, Classifieds, Market, Telegrams, Archive }

/// <summary>
/// The Carvell Falls Courier, opened with Tab: the latest edition, the classifieds (the build
/// menu), the market page, the telegram desk and the archive (GDD §10). Everything is read
/// from the sim; the only thing it changes is the tool in hand.
/// </summary>
public partial class PaperView : CanvasLayer
{
    static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public event Action<BuildingType>? AdClicked;

    World world = null!;
    PanelContainer sheet = null!;
    readonly Dictionary<Page, Control> pages = new();
    readonly Dictionary<Page, Label> tabs = new();
    readonly List<AdCard> ads = new();
    Label edition = null!, headline = null!, deck = null!, bodyLeft = null!, bodyRight = null!, insideHead = null!, inside = null!;
    Label marketSummary = null!, marketEmpty = null!, telegramEmpty = null!, archiveEmpty = null!;
    GridContainer marketGrid = null!;
    VBoxContainer telegramList = null!, archiveList = null!;
    Edition? shown;
    int frame;

    public Page Current { get; private set; } = Page.Front;
    public bool IsOpen => Visible;
    public IReadOnlyList<AdCard> Ads => ads;
    public Edition? Shown => shown;

    public void Init(World world)
    {
        this.world = world;
        Layer = 20;
        Visible = false;

        sheet = new PanelContainer();
        sheet.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        sheet.OffsetLeft = 48;
        sheet.OffsetRight = -48;
        sheet.OffsetTop = 24;
        sheet.OffsetBottom = -24;
        var style = new StyleBoxFlat
        {
            BgColor = Ink.Paper, BorderColor = Ink.Black,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 18, ContentMarginBottom = 18,
            ShadowColor = Ink.Black with { A = 0.35f }, ShadowSize = 18,
        };
        style.SetBorderWidthAll(3);
        sheet.AddThemeStyleboxOverride("panel", style);
        AddChild(sheet);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        sheet.AddChild(col);

        col.AddChild(Type.Text(Text.Get("MASTHEAD"), Type.Blackletter, 54, HorizontalAlignment.Center));
        edition = Type.Text("", Type.Serif, 13, HorizontalAlignment.Center);
        col.AddChild(edition);
        col.AddChild(Type.Rule(3));

        var tabRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabRow.AddThemeConstantOverride("separation", 34);
        col.AddChild(tabRow);
        foreach (var page in Enum.GetValues<Page>())
        {
            var tab = Type.Text(Text.Get("PAGE_" + page.ToString().ToUpperInvariant()), Type.SerifBold, 14, HorizontalAlignment.Center);
            tab.MouseFilter = Control.MouseFilterEnum.Stop;
            tab.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var p = page;
            tab.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    Show(p);
            };
            tabRow.AddChild(tab);
            tabs[page] = tab;
        }
        var hint = Type.Text(Text.Get("PAPER_HINT"), Type.SerifItalic, 12, HorizontalAlignment.Center, Ink.Soft);
        tabRow.AddChild(hint);
        col.AddChild(Type.Rule(1));

        var body = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("margin_top", 10);
        col.AddChild(body);
        pages[Page.Front] = BuildFront();
        pages[Page.Classifieds] = BuildClassifieds();
        pages[Page.Market] = BuildMarket();
        pages[Page.Telegrams] = BuildTelegrams();
        pages[Page.Archive] = BuildArchive();
        foreach (var p in pages.Values)
            body.AddChild(p);
        Show(Page.Front);
    }

    // ---- Pages ------------------------------------------------------------------------------

    Control BuildFront()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 8);
        headline = Type.Text("", Type.Fell, 46, HorizontalAlignment.Center);
        headline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        deck = Type.Text("", Type.SerifItalic, 18, HorizontalAlignment.Center);
        deck.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(headline);
        v.AddChild(deck);
        v.AddChild(Type.Rule(1));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        bodyLeft = Type.Text("", Type.Serif, 16);
        bodyRight = Type.Text("", Type.Serif, 16);
        foreach (var l in new[] { bodyLeft, bodyRight })
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.SizeFlagsStretchRatio = 1;
            columns.AddChild(l);
        }
        v.AddChild(columns);
        insideHead = Type.Text(Text.Get("PAPER_INSIDE"), Type.SerifBold, 13, HorizontalAlignment.Center);
        inside = Type.Text("", Type.Serif, 14, HorizontalAlignment.Center);
        v.AddChild(Type.Rule(1));
        v.AddChild(insideHead);
        v.AddChild(inside);
        scroll.AddChild(v);
        return scroll;
    }

    Control BuildClassifieds()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 14);
        foreach (var column in Catalog.Columns)
        {
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            v.AddThemeConstantOverride("separation", 8);
            v.AddChild(Type.Text(Text.Get("COL_" + column), Type.SerifBold, 13, HorizontalAlignment.Center));
            v.AddChild(Type.Rule(1));
            foreach (var type in Catalog.InColumn(column))
            {
                var card = new AdCard(type, t => AdClicked?.Invoke(t), 200);
                ads.Add(card);
                v.AddChild(card);
            }
            row.AddChild(v);
        }
        scroll.AddChild(row);
        return scroll;
    }

    Control BuildMarket()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        marketSummary = Type.Text("", Type.SerifBold, 14, HorizontalAlignment.Center);
        v.AddChild(marketSummary);
        v.AddChild(Type.Rule(1));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        marketGrid = new GridContainer { Columns = 5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        marketGrid.AddThemeConstantOverride("h_separation", 24);
        marketGrid.AddThemeConstantOverride("v_separation", 3);
        scroll.AddChild(marketGrid);
        v.AddChild(scroll);
        marketEmpty = Type.Text(Text.Get("MARKET_EMPTY"), Type.SerifItalic, 15, HorizontalAlignment.Center, Ink.Soft);
        v.AddChild(marketEmpty);
        return v;
    }

    Control BuildTelegrams()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        telegramList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        telegramList.AddThemeConstantOverride("separation", 10);
        telegramEmpty = Type.Text(Text.Get("TELEGRAM_EMPTY"), Type.SerifItalic, 15, HorizontalAlignment.Center, Ink.Soft);
        telegramList.AddChild(telegramEmpty);
        scroll.AddChild(telegramList);
        return scroll;
    }

    Control BuildArchive()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        archiveList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        archiveList.AddThemeConstantOverride("separation", 4);
        archiveEmpty = Type.Text(Text.Get("ARCHIVE_EMPTY"), Type.SerifItalic, 15, HorizontalAlignment.Center, Ink.Soft);
        archiveList.AddChild(archiveEmpty);
        scroll.AddChild(archiveList);
        return scroll;
    }

    // ---- Showing ----------------------------------------------------------------------------

    /// <summary>The latest edition the reader has had open in front of them.</summary>
    Edition? seen;

    public void Open()
    {
        Visible = true;
        shown = world.Paper.Latest;
        // A new edition since the paper was last opened is on the front page: open there ("EXTRA … Tab opens the paper").
        if (shown != seen)
        {
            seen = shown;
            Show(Page.Front);
        }
        else
            Refresh(true);
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible)
            Close();
        else
            Open();
    }

    public void Show(Page page)
    {
        Current = page;
        foreach (var (p, control) in pages)
            control.Visible = p == page;
        foreach (var (p, tab) in tabs)
        {
            tab.LabelSettings.FontColor = p == page ? Ink.Black : Ink.Soft;
        }
        Refresh(true);
    }

    /// <summary>Shows one edition from the archive on the front page.</summary>
    public void ShowEdition(Edition e)
    {
        shown = e;
        Show(Page.Front);
    }

    public void Refresh(bool force = false)
    {
        if (!Visible)
            return;
        if (!force && ++frame % 30 != 0)
            return;
        // The front page names the edition on it (an older one from the archive, say); the other pages, today's.
        var dated = Current == Page.Front && shown != null ? shown : world.Paper.Latest;
        edition.Text = dated == null ? "" : Text.Get("PAPER_EDITION", dated.Number, Hud.DateOf(dated.Tick));
        switch (Current)
        {
            case Page.Front: RefreshFront(); break;
            case Page.Classifieds: RefreshClassifieds(); break;
            case Page.Market: RefreshMarket(); break;
            case Page.Telegrams: RefreshTelegrams(); break;
            case Page.Archive: RefreshArchive(); break;
        }
    }

    /// <summary>Headline, deck and body for an edition, with its arguments filled in.</summary>
    public static (string Headline, string Deck, string Body) Copy(Edition e)
    {
        object[] args = Args(e);
        string headKey = e.Key.StartsWith("EVENT_") ? e.Key : "HEADLINE_" + e.Key;
        return (Text.Get(headKey, args), Text.Get("DECK_" + e.Key, args), Text.Get("BODY_" + e.Key, args));
    }

    static object[] Args(Edition e)
    {
        if (e.Key.StartsWith("TELEGRAM") && e.Args.Length >= 3)
            return new object[] { Text.Get("ITEM_" + e.Args[0]).ToUpperInvariant(), Text.Get("TELEGRAM_FROM_" + e.Args[1]), e.Args[2] };
        if ((e.Key == "BANKERS_WARNING" || e.Key == "FOLDS") && e.Args.Length >= 1 && long.TryParse(e.Args[0], out long cents))
            return new object[] { Hud.Money(cents) };
        // Editions 1 and 5 quote the preset's founding cash and line of credit; editions printed before they carried
        // the figure (saves from 0.9.0-rc1) read "a few thousand dollars" / "a line of credit".
        if (e.Key is "TUT1" or "TUT5")
            return new object[] { e.Args.Length >= 1 && long.TryParse(e.Args[0], out long sum)
                ? "$" + (sum / 100).ToString("N0", En)
                : Text.Get(e.Key + "_SUM_UNKNOWN") };
        if (e.Key.StartsWith("COMMISSION") && e.Args.Length >= 2)
        {
            string name = Main.CommissionName(e.Args[0], e.Args[1]);
            return new object[] { name, name.ToUpperInvariant() };
        }
        return e.Args.Cast<object>().ToArray();
    }

    void RefreshFront()
    {
        if (shown == null)
        {
            headline.Text = "";
            deck.Text = "";
            bodyLeft.Text = bodyRight.Text = "";
            insideHead.Visible = inside.Visible = false;
            return;
        }
        var (h, d, b) = Copy(shown);
        headline.Text = h;
        deck.Text = d;
        var paragraphs = b.Split("\n\n");
        int half = (paragraphs.Length + 1) / 2;
        bodyLeft.Text = string.Join("\n\n", paragraphs.Take(half));
        bodyRight.Text = string.Join("\n\n", paragraphs.Skip(half));
        bool any = shown.Inside.Length > 0;
        insideHead.Visible = inside.Visible = any;
        if (any)
            inside.Text = string.Join("\n", shown.Inside.Select(k => Text.Get(k.StartsWith("EVENT_") ? k : "HEADLINE_" + k)));
    }

    void RefreshClassifieds()
    {
        var circled = world.Paper.Latest?.CircledAd;
        foreach (var card in ads)
            card.Refresh(world, false, circled == card.Type);
    }

    void RefreshMarket()
    {
        marketSummary.Text = Text.Get("MARKET_SUMMARY", Hud.Money(world.CashCents), Hud.Money(world.DebtCents), Hud.Money(world.CreditLimitCents),
            (world.WeeklyInterestPermille / 10.0).ToString("0.#", En), Hud.Money(world.SharePriceCents), Hud.Money(world.NetWorthCents));
        foreach (var child in marketGrid.GetChildren())
            child.QueueFree();
        var rows = Items.All.Where(i => Items.Of(i).Tier == Tier.Raw || world.Paper.Sold(i) > 0 || world.Paper.Made(i) || world.Market.Bought(i) > 0).ToList();
        marketEmpty.Visible = rows.Count == 0;
        foreach (var head in new[] { "MARKET_HEAD_GOOD", "MARKET_HEAD_SELL", "MARKET_HEAD_BUY", "MARKET_HEAD_WEEK", "MARKET_HEAD_TREND" })
            marketGrid.AddChild(Type.Text(Text.Get(head), Type.SerifBold, 12));
        foreach (var item in rows)
        {
            marketGrid.AddChild(Type.Text(Text.Get("ITEM_" + Items.Id(item)), Type.Serif, 14));
            marketGrid.AddChild(Type.Text(Hud.Money(world.Market.PriceCents(item)), Type.Serif, 14, HorizontalAlignment.Right));
            marketGrid.AddChild(Type.Text(Hud.Money(world.Market.BuyPriceCents(item)), Type.Serif, 14, HorizontalAlignment.Right));
            marketGrid.AddChild(new Sparkline(world.Market.History(item), world.Market.PriceCents(item)));
            int trend = world.Market.TrendMilli(item);
            string arrow = trend > 1030 ? "▲" : trend < 970 ? "▼" : "—";
            var t = Type.Text($"{arrow} {trend / 10}%", Type.Serif, 14, HorizontalAlignment.Right, trend < 970 ? Ink.Red : Ink.Black);
            marketGrid.AddChild(t);
        }
    }

    void RefreshTelegrams()
    {
        foreach (var child in telegramList.GetChildren())
            if (child != telegramEmpty)
                child.QueueFree();
        var wires = world.Paper.Telegrams;
        telegramEmpty.Visible = wires.Count == 0;
        foreach (var t in wires.Reverse())
        {
            var card = new VBoxContainer();
            string item = Text.Get("ITEM_" + Items.Id(t.Item)).ToUpperInvariant();
            string line = Text.Get("TELEGRAM_LINE", t.Quantity, item, Hud.DayName(t.DueTick), Text.Get("TELEGRAM_FROM_" + t.Sender));
            card.AddChild(Type.Text(line, Type.SerifBold, 15));
            string status = t.State switch
            {
                TelegramState.Filled => Text.Get("TELEGRAM_FILLED"),
                TelegramState.Missed => Text.Get("TELEGRAM_MISSED"),
                _ => Text.Get("TELEGRAM_OPEN", t.Delivered, t.Quantity, Hud.DateOf(t.DueTick)),
            };
            card.AddChild(Type.Text(status, Type.SerifItalic, 13, HorizontalAlignment.Left, t.State == TelegramState.Missed ? Ink.Red : Ink.Soft));
            card.AddChild(Type.Rule(1));
            telegramList.AddChild(card);
        }
    }

    void RefreshArchive()
    {
        foreach (var child in archiveList.GetChildren())
            if (child != archiveEmpty)
                child.QueueFree();
        var editions = world.Paper.Editions;
        archiveEmpty.Visible = editions.Count == 0;
        foreach (var e in editions.Reverse())
        {
            var (h, _, _) = Copy(e);
            var row = Type.Text(Text.Get("ARCHIVE_ROW", e.Number, Hud.DateOf(e.Tick), h), Type.Serif, 14);
            row.MouseFilter = Control.MouseFilterEnum.Stop;
            row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var ed = e;
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    ShowEdition(ed);
            };
            archiveList.AddChild(row);
        }
    }

    /// <summary>Seven days of closing prices as a little ink line.</summary>
    partial class Sparkline : Control
    {
        readonly long[] values;
        readonly long now;

        public Sparkline(IReadOnlyList<long> history, long now)
        {
            values = history.ToArray();
            this.now = now;
            CustomMinimumSize = new Vector2(120, 22);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var pts = values.Append(now).ToArray();
            if (pts.Length < 2)
                return;
            long lo = pts.Min(), hi = pts.Max();
            if (hi == lo)
                hi = lo + 1;
            var line = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                line[i] = new Vector2(i * (Size.X - 4) / (pts.Length - 1) + 2, Size.Y - 3 - (pts[i] - lo) * (Size.Y - 6) / (hi - lo));
            DrawPolyline(line, Ink.Black, 1.5f, true);
            DrawCircle(line[^1], 2.2f, Ink.Red);
        }
    }
}
