using System;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>Period type for the paper, loaded once.</summary>
public static class Type
{
    static FontFile? blackletter, fell, serif, serifItalic, serifBold;
    public static FontFile Blackletter => blackletter ??= GD.Load<FontFile>("res://assets/fonts/UnifrakturMaguntia-Book.ttf");
    public static FontFile Fell => fell ??= GD.Load<FontFile>("res://assets/fonts/IMFellEnglish-Roman.ttf");
    public static FontFile Serif => serif ??= GD.Load<FontFile>("res://assets/fonts/OldStandard-Regular.ttf");
    public static FontFile SerifItalic => serifItalic ??= GD.Load<FontFile>("res://assets/fonts/OldStandard-Italic.ttf");
    public static FontFile SerifBold => serifBold ??= GD.Load<FontFile>("res://assets/fonts/OldStandard-Bold.ttf");

    public static Label Text(string text, Font font, int size, HorizontalAlignment align = HorizontalAlignment.Left, Color? color = null) =>
        new()
        {
            Text = text,
            LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = color ?? Ink.Black },
            HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

    public static ColorRect Rule(float height) =>
        new() { Color = Ink.Black, CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };
}

/// <summary>
/// One classified advertisement: title, pitch, current price, key. The same card serves the deck's
/// quick strip and the paper's classifieds page; the tutorial rings one in red pencil.
/// </summary>
public partial class AdCard : PanelContainer
{
    static StyleBoxFlat? plain, selected;

    readonly Label[] labels;
    readonly Label price;
    readonly Ring ring;
    bool on, circled;

    public BuildingType Type { get; }
    public bool IsCircled => circled;

    public AdCard(BuildingType type, Action<BuildingType> onClick, int width = 176)
    {
        Type = type;
        var def = Catalog.Of(type);
        plain ??= MakeStyle(false);
        selected ??= MakeStyle(true);
        CustomMinimumSize = new Vector2(width, 0);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeStyleboxOverride("panel", plain);

        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 0);
        var t = FrontPageFoundry.Type.Text(Text.Get($"AD_{def.Id}_TITLE"), FrontPageFoundry.Type.Fell, 18, HorizontalAlignment.Center);
        var b = FrontPageFoundry.Type.Text(Text.Get($"AD_{def.Id}_BODY"), FrontPageFoundry.Type.SerifItalic, 12, HorizontalAlignment.Center);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        b.CustomMinimumSize = new Vector2(width - 26, 0);
        price = FrontPageFoundry.Type.Text("", FrontPageFoundry.Type.SerifBold, 13, HorizontalAlignment.Center);
        int keyIndex = System.Linq.Enumerable.ToList(Catalog.InColumn(def.Column)).IndexOf(type);
        var k = FrontPageFoundry.Type.Text(keyIndex < 9 ? Text.Get("AD_KEY", keyIndex + 1) : "", FrontPageFoundry.Type.Serif, 10, HorizontalAlignment.Center);
        labels = new[] { t, b, price, k };
        foreach (var l in labels)
            v.AddChild(l);
        AddChild(v);

        ring = new Ring { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        ring.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(ring);

        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                onClick(type);
        };
    }

    static StyleBoxFlat MakeStyle(bool selected)
    {
        var s = new StyleBoxFlat
        {
            BgColor = selected ? Ink.Black : Ink.Paper,
            BorderColor = Ink.Black,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        s.SetBorderWidthAll(selected ? 4 : 2);
        return s;
    }

    /// <summary>Updates price, affordability, selection and the pencil ring.</summary>
    public void Refresh(World world, bool selectedNow, bool circledNow)
    {
        var def = Catalog.Of(Type);
        long cost = world.CostCents(Type);
        price.Text = Text.Get(def.IsBelt ? "PRICE_YARD" : def.IsBridge ? "PRICE_PAIR" : "PRICE_EACH", Hud.Money(cost));
        if (on != selectedNow)
        {
            on = selectedNow;
            AddThemeStyleboxOverride("panel", on ? selected! : plain!);
            foreach (var l in labels)
                l.LabelSettings.FontColor = on ? Ink.Paper : Ink.Black;
        }
        if (!on)
            price.LabelSettings.FontColor = world.CanAfford(cost) ? Ink.Black : Ink.Red;
        if (circled != circledNow)
        {
            circled = circledNow;
            ring.Visible = circled;
        }
    }

    /// <summary>A loose red-pencil ellipse round the card.</summary>
    partial class Ring : Control
    {
        public override void _Draw()
        {
            var r = GetRect();
            var c = new Vector2(r.Size.X / 2, r.Size.Y / 2);
            var pts = new Vector2[41];
            for (int i = 0; i <= 40; i++)
            {
                float a = Mathf.Tau * i / 40 - 0.4f;
                float wobble = 1f + 0.03f * Mathf.Sin(a * 3.7f);
                pts[i] = c + new Vector2(Mathf.Cos(a) * (r.Size.X / 2 + 6) * wobble, Mathf.Sin(a) * (r.Size.Y / 2 + 4) * wobble);
            }
            DrawPolyline(pts, Ink.Red with { A = 0.85f }, 3f, true);
        }
    }
}
