using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Orijinal Gwent'e benzer kart: tam boy çizim, sol üstte güç rozeti (kahramanda turuncu), sol altta yetenek
/// simgeleri, sağ altta sıra türü. Çizim yoksa fraksiyon renkli yer tutucuya düşer.
/// </summary>
public sealed class GwintCard : Border
{
    public const double Aspect = 444.0 / 308.0; // kart resminin en-boy oranı

    public CardInstance? Card { get; }
    public CardDef Def { get; }

    public static string? GlyphFor(string ability) => ability switch
    {
        "spy" => "ab_spy",
        "medic" => "ab_medic",
        "muster" => "ab_muster",
        "bond" => "ab_bond",
        "morale" => "ab_morale",
        "horn" => "ab_horn",
        "scorch" or "scorch_c" or "scorch_r" or "scorch_s" => "ab_scorch",
        "avenger" => "ab_avenger",
        "berserker" => "ab_berserker",
        "mardroeme" => "ab_mardroeme",
        "frost" => "ab_frost",
        "fog" => "ab_fog",
        "rain" => "ab_rain",
        "storm" => "ab_storm",
        "clear" => "ab_clear",
        "decoy" => "ab_decoy",
        _ => null
    };

    public GwintCard(CardDef def, double width, int? strength = null, CardInstance? card = null,
        bool selected = false, bool highlight = false, bool dimmed = false, double? height = null)
    {
        Def = def;
        Card = card;
        var w = width;
        var h = height ?? width * 1.34;
        Width = w;
        Height = h;
        Cursor = System.Windows.Input.Cursors.Hand;
        Opacity = dimmed ? 0.5 : 1;
        SnapsToDevicePixels = true;
        CornerRadius = new CornerRadius(Math.Max(2, w * 0.04));
        BorderThickness = new Thickness(selected || highlight ? Math.Max(2.5, w * 0.03) : 1);
        BorderBrush = selected ? Theme.Select : highlight ? Theme.Gold : Theme.B("#241a10");
        ClipToBounds = true;
        ToolTip = CardControl.BuildToolTip(def, strength);
        if (selected || highlight)
            Effect = new DropShadowEffect { Color = Color.FromRgb(255, 220, 120), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 };

        var grid = new Grid();
        var art = Assets.Card(def.Id);
        if (art != null)
        {
            var img = new Image { Source = art, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            // Köşeler yuvarlak kırpılır: dağıtım paketindeki JPEG kart resimlerinde saydam köşe yoktur
            var radius = Math.Max(2, w * 0.04);
            img.SizeChanged += (_, e) => img.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
            grid.Children.Add(img);
        }
        else
        {
            grid.Background = new LinearGradientBrush(((SolidColorBrush)Theme.FactionColor(def.Faction)).Color,
                Color.Multiply(((SolidColorBrush)Theme.FactionColor(def.Faction)).Color, 0.5f), 90);
            var t = new TextBlock
            {
                Text = def.Name,
                Foreground = Theme.Text,
                FontSize = Math.Max(8, w * 0.1),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4)
            };
            grid.Children.Add(t);
        }

        // Güç rozeti
        var b = Math.Max(20, w * 0.34);
        if (def.IsUnit)
        {
            var shown = strength ?? def.Strength;
            // Kahraman: orijinaldeki süslü güneş köşesi (güç sayısı güneşin ortasında)
            var sun = def.Hero ? Assets.Ui("corner_sun") : null;
            if (sun != null)
            {
                var c = w * 0.54;
                var corner = new Canvas { Width = c, Height = c, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                var cim = new Image { Source = sun, Width = c, Height = c, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(cim, BitmapScalingMode.HighQuality);
                corner.Children.Add(cim);
                var txt = shown.ToString();
                var holder = new Grid { Width = c * 0.44, Height = c * 0.44 };
                holder.Children.Add(Number(txt, c * 0.6, Brushes.White, true));
                Canvas.SetLeft(holder, c * 0.34 - c * 0.22);
                Canvas.SetTop(holder, c * 0.34 - c * 0.22);
                corner.Children.Add(holder);
                grid.Children.Add(corner);
            }
            else
            {
            var badge = Badge(def.Hero ? "badge_hero" : "badge", b);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.Margin = new Thickness(w * 0.02, w * 0.02, 0, 0);
            Brush fg = def.Hero ? Brushes.White
                : shown > def.Strength ? Theme.B("#1d7a1d") : shown < def.Strength ? Theme.B("#b02a1a") : Theme.B("#2a1c0e");
            badge.Children.Add(Number(shown.ToString(), b, fg, def.Hero));
            grid.Children.Add(badge);
            }
        }

        // Kaer Morhen rünü: sağ üst köşede nişan
        if (card?.Rune is { } rune)
        {
            var rb = KaerMorhen.ForgeView.RuneBadge(rune, Math.Max(18, w * 0.3));
            rb.HorizontalAlignment = HorizontalAlignment.Right;
            rb.VerticalAlignment = VerticalAlignment.Top;
            rb.Margin = new Thickness(0, w * 0.03, w * 0.03, 0);
            grid.Children.Add(rb);
        }

        // Alt şerit: yetenekler (sol) ve sıra türü (sağ)
        var s = Math.Max(20, w * 0.31);
        var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(w * 0.02, 0, w * 0.02, w * 0.02) };
        var abilities = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var seen = new HashSet<string>();
        foreach (var ab in def.Abilities)
        {
            var g = GlyphFor(ab);
            if (g == null || !seen.Add(g)) continue;
            abilities.Children.Add(Glyph(g, s, "badge"));
        }
        bottom.Children.Add(abilities);

        if (def.IsUnit)
        {
            var rows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (def.IsAgile)
            {
                rows.Children.Add(Glyph("row_close", s * 0.85, "badge_hero"));
                rows.Children.Add(Glyph("row_ranged", s * 0.85, "badge_hero"));
            }
            else
            {
                var rg = def.Row switch { "close" => "row_close", "ranged" => "row_ranged", _ => "row_siege" };
                rows.Children.Add(Glyph(rg, s, "badge_hero"));
            }
            bottom.Children.Add(rows);
        }
        grid.Children.Add(bottom);

        Child = grid;
    }

    private static Grid Badge(string name, double size)
    {
        var g = new Grid { Width = size, Height = size };
        var src = Assets.Ui(name);
        if (src != null)
        {
            var im = new Image { Source = src, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            g.Children.Add(im);
        }
        else
        {
            g.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Fill = name == "badge_hero" ? Theme.Gold : Theme.B("#e8dcc4"),
                Stroke = Theme.B("#6a5420"),
                StrokeThickness = 1.5
            });
        }
        return g;
    }

    private static TextBlock Number(string text, double badge, Brush fg, bool bold) => new()
    {
        Text = text,
        FontFamily = Theme.Serif,
        FontWeight = FontWeights.Bold,
        FontSize = badge * (text.Length > 1 ? 0.5 : 0.6),
        Foreground = fg,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    internal static Grid Glyph(string glyph, double size, string badgeName)
    {
        var g = Badge(badgeName, size);
        g.Margin = new Thickness(0, 0, size * 0.04, 0);
        var src = Assets.Ui(glyph);
        if (src != null)
        {
            var im = new Image { Source = src, Stretch = Stretch.Uniform, Width = size * 0.72, Height = size * 0.72 };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            g.Children.Add(im);
        }
        return g;
    }
}
