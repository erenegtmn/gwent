using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HanLigi;
using HT = HanLigi.Texts;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi kartı. Üstte güç rozeti (kahramanda altın) ve yıldızlar; altta okunur bir bilgi plakası: <b>yetenek adı</b> (simgesiyle)
/// ve <b>sınıfları</b> (sinerji renginde). Adlı kartta (tezgâh, inceleme) ad ve yeteneğin kısa özeti de yazar. Küçük yazılar
/// ince sans yazı tipiyle çizilir: masada küçülse de okunur. Tam açıklama karta gelince açılan inceleme penceresindedir.
/// </summary>
public sealed class HanCardView : Grid
{
    public static FontFamily Sans => HanUi.Font;

    /// <summary>Kartın yüksekliği / genişliği (masa, depo, pusu): gerçek kart oranına yakın, basık değil.</summary>
    public const double CardAspect = 1.32;

    public static Color TierColor(CardDef d) => d.IsHero ? Color.FromRgb(0xf2, 0xc8, 0x5a) : d.Tier switch
    {
        1 => Color.FromRgb(0x9a, 0x94, 0x86), 2 => Color.FromRgb(0x5f, 0xb0, 0x5a), 3 => Color.FromRgb(0x52, 0x94, 0xdc), 4 => Color.FromRgb(0xa8, 0x6a, 0xdc), _ => Color.FromRgb(0xe8, 0x8a, 0x2c)
    };

    public static string Describe(CardDef d, int star)
    {
        var lines = new List<string> { d.Name + (d.IsHero ? "  (Kahraman)" : star > 1 ? "  " + new string('★', star) : "") };
        if (d.IsUnit)
        {
            var row = d.Agile ? "Çevik (Yakın / Menzilli)" : HT.Name(d.Row);
            lines.Add($"Güç {Tuning.Power(d, star)} · {row}" + (d.IsHero ? "" : $" · Tier {d.Tier}"));
            if (d.Tags.Length > 0) lines.Add("Sınıf: " + string.Join(", ", d.Tags.Select(HT.Name)));
            if (d.IsHero) { lines.Add(HT.Describe(d.Hero)); lines.Add("Hava, Kavurma ve kart hedefli etkilere bağışık."); }
            if (d.Ability != Ability.None && !(d.IsHero && d.Ability == Ability.Ambush)) lines.Add(HT.Describe(d.Ability));
        }
        else
        {
            lines.Add($"Özel kart · Tier {d.Tier}");
            lines.Add(HT.Describe(d.Special));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Kartın üstündeki tek kelimelik yetenek adı (yoksa boş).</summary>
    public static string Keyword(CardDef d)
    {
        if (d.IsHero) return "Kahraman";
        if (!d.IsUnit)
            return d.Special switch { Special.Frost => "Ayaz", Special.Fog => "Sis", Special.Rain => "Yağmur", Special.Scorch => "Kavurma", Special.Horn => "Boynuz", Special.Shield => "Kalkan", _ => "Özel" };
        var a = d.Ability switch
        {
            Ability.Bond => "Bağ", Ability.Growth => "Büyüme", Ability.Morale => "Moral", Ability.Spy => "Casus", Ability.Bard => "Ozan", Ability.Treasure => "Hazine",
            Ability.Ambush => "Pusu", Ability.Revenge => "İntikam", Ability.Zeal => "Coşku", Ability.Summon => "Çağrı", Ability.Medic => "Şifacı", Ability.DeathEcho => "Yankı", Ability.Mirror => "Yansıma", _ => ""
        };
        return a.Length == 0 && d.Agile ? "Çevik" : a;
    }

    /// <summary>Yeteneğin oyuncu diliyle birkaç kelimelik özeti (adlı kartlarda).</summary>
    public static string Gist(CardDef d)
    {
        if (d.IsHero) return "Etkilere bağışık";
        if (!d.IsUnit)
            return d.Special switch
            {
                Special.Frost => "Yakın satırlar 1 olur", Special.Fog => "Menzilli satırlar 1 olur", Special.Rain => "Kuşatma satırları 1 olur", Special.Scorch => "En güçlü birimi yakar",
                Special.Horn => "Bir satıra +%50", Special.Shield => "Satırı havadan korur", _ => ""
            };
        return d.Ability switch
        {
            Ability.Bond => "Aynı sınıftan dost başına +1", Ability.Growth => "Satırdaki her dosta +1", Ability.Morale => "Bir satıra +★ güç", Ability.Spy => "Rakibin kartlarını gösterir",
            Ability.Bard => "Savaşa +1 Odakla başla", Ability.Treasure => "Her round +1 altın", Ability.Ambush => "Savaşta istediğin satıra iner", Ability.Revenge => "Önceki satır kaybedildiyse +%50",
            Ability.Zeal => "Önceki satır kazanıldıysa +%30", Ability.Summon => "Depodaki eşini sahaya çeker (2★: 1, 3★: 2)", Ability.Medic => "En güçlünü etkilerden korur", Ability.DeathEcho => "Ölünce hayalet bırakır",
            Ability.Mirror => "En güçlü dostun yarısı kadar", _ => d.Agile ? "Yakın ya da Menzilli" : ""
        };
    }

    public static Color TagColor(global::HanLigi.Tag t) => t switch
    {
        global::HanLigi.Tag.Monster => Color.FromRgb(0xe0, 0x6a, 0x58), global::HanLigi.Tag.Soldier => Color.FromRgb(0x7a, 0xaa, 0xe8), global::HanLigi.Tag.Mage => Color.FromRgb(0xc0, 0x8a, 0xff),
        global::HanLigi.Tag.Bandit => Color.FromRgb(0x7a, 0xd0, 0x6a), global::HanLigi.Tag.Noble => Color.FromRgb(0xf0, 0xc8, 0x5a), _ => Color.FromRgb(0xd8, 0xa2, 0x6a)
    };

    /// <summary>Yeteneğin Gwent simgesi (ab_*.png; görselin sol yarısı).</summary>
    internal static ImageSource? AbilityIcon(CardDef d)
    {
        var name = d.Ability switch
        {
            Ability.Bond => "ab_bond", Ability.Morale => "ab_morale", Ability.Spy => "ab_spy", Ability.Medic => "ab_medic", Ability.Summon => "ab_muster",
            Ability.Bard => "ab_horn", Ability.DeathEcho => "ab_avenger", Ability.Mirror => "ab_berserker", Ability.Growth => "ab_mardroeme", Ability.Ambush => "ab_decoy",
            Ability.Revenge => "ab_scorch", Ability.Zeal => "ab_storm", _ => d.Agile ? "ab_agile" : null
        };
        if (d.Special != Special.None) name = d.Special switch { Special.Frost => "ab_frost", Special.Fog => "ab_fog", Special.Rain => "ab_rain", Special.Scorch => "ab_scorch", Special.Horn => "ab_horn", _ => "ab_decoy" };
        if (d.IsHero) name = null;
        if (name == null || Assets.Ui(name) is not BitmapSource src) return null;
        lock (IconCache)
        {
            if (IconCache.TryGetValue(name, out var c)) return c;
            ImageSource s;
            try { s = src.PixelWidth >= src.PixelHeight * 2 ? new CroppedBitmap(src, new Int32Rect(0, 0, src.PixelWidth / 2, src.PixelHeight)) : src; }
            catch { s = src; }
            s.Freeze();
            return IconCache[name] = s;
        }
    }

    private static readonly Dictionary<string, ImageSource> IconCache = new();

    /// <param name="aspect">Yükseklik / genişlik. Masada ve depoda 1.2 (kısa kart), incelemede 1.34.</param>
    /// <param name="named">Ad ve yetenek özeti de yazılsın (tezgâh, inceleme, seçim).</param>
    public HanCardView(CardDef d, int star, double width, int? value = null, bool selected = false, string? footer = null, bool dim = false, int? price = null, bool faceDown = false, double aspect = CardAspect, bool named = false, bool plain = false)
    {
        var w = width;
        var h = Math.Round(width * aspect);
        Width = w;
        Height = h;
        Cursor = System.Windows.Input.Cursors.Hand;
        Opacity = dim ? 0.45 : 1;
        Background = Brushes.Transparent;
        var radius = Math.Max(3, w * 0.06);
        var tier = TierColor(d);

        // gövde: gölge + çizim
        var body = new Border
        {
            CornerRadius = new CornerRadius(radius), Background = Theme.B("#1b140d"),
            Effect = new DropShadowEffect { Color = selected ? Color.FromRgb(255, 224, 130) : Colors.Black, BlurRadius = selected ? 26 : 8, ShadowDepth = selected ? 0 : 3, Opacity = selected ? 1 : 0.8, Direction = 270 }
        };
        var art = new Grid { ClipToBounds = true };
        art.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
        var img = faceDown ? HanArt.Src("cardback") : Assets.Card(d.Art);
        if (img != null)
        {
            var im = new Image { Source = img, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            art.Children.Add(im);
        }
        else
        {
            art.Background = faceDown
                ? new LinearGradientBrush(Color.FromRgb(0x3a, 0x22, 0x18), Color.FromRgb(0x14, 0x0b, 0x07), 90)
                : new LinearGradientBrush(Color.Multiply(tier, 0.6f), Color.Multiply(tier, 0.18f), 90);
            if (!faceDown && !named)
                art.Children.Add(new TextBlock { Text = d.Name, FontFamily = HanUi.Font, FontWeight = FontWeights.SemiBold, FontSize = Math.Max(9, w * 0.105), Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(w * 0.06, 0, w * 0.06, h * 0.3) });
        }
        body.Child = art;
        Children.Add(body);
        if (!faceDown && !plain) Children.Add(Plate(d, star, w, h, radius, tier, footer, named));
        // çerçeve: tier renginde metal
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius), IsHitTestVisible = false, BorderThickness = new Thickness(selected ? Math.Max(3, w * 0.035) : Math.Max(1.6, w * 0.022)),
            BorderBrush = selected ? Theme.Select : faceDown ? Theme.B("#6a4a26") : new LinearGradientBrush(Color.Add(tier, Color.FromRgb(0x38, 0x38, 0x30)), Color.Multiply(tier, 0.55f), 90)
        });
        if (faceDown) return;

        // kahraman: orijinal Gwent'in süslü güneş köşesi (güç sayısı güneşin ortasında); görsel yoksa altın rozet
        if (d.IsHero && Assets.Ui("corner_sun") is { } sun)
        {
            var shownH = value ?? Tuning.Power(d, star);
            var c = w * 0.54;
            var corner = new Canvas { Width = c, Height = c, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            var cim = new Image { Source = sun, Width = c, Height = c, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(cim, BitmapScalingMode.HighQuality);
            corner.Children.Add(cim);
            var holder = new Grid { Width = c * 0.44, Height = c * 0.44 };
            holder.Children.Add(new TextBlock
            {
                Text = shownH.ToString(), FontFamily = HanUi.Font, FontWeight = FontWeights.Bold, FontSize = c * (shownH >= 10 ? 0.27 : 0.32),
                Foreground = shownH > Tuning.Power(d, star) ? Theme.B("#b6ff9a") : shownH < Tuning.Power(d, star) ? Theme.B("#ff9a86") : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 }
            });
            Canvas.SetLeft(holder, c * 0.34 - c * 0.22);
            Canvas.SetTop(holder, c * 0.34 - c * 0.22);
            corner.Children.Add(holder);
            Children.Add(corner);
        }
        // güç rozeti
        else if (d.IsUnit)
        {
            var shown = value ?? Tuning.Power(d, star);
            var basePow = Tuning.Power(d, star);
            var size = Math.Round(Math.Max(20, w * 0.33));
            var badge = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(w * 0.035, w * 0.035, 0, 0), IsHitTestVisible = false };
            badge.Children.Add(new Ellipse
            {
                Fill = d.IsHero ? new RadialGradientBrush(Color.FromRgb(0xff, 0xe0, 0x88), Color.FromRgb(0xb0, 0x7a, 0x1c)) : new RadialGradientBrush(Color.FromRgb(0x3a, 0x2c, 0x1c), Color.FromRgb(0x0a, 0x07, 0x04)),
                Stroke = d.IsHero ? Theme.B("#fff2c0") : new LinearGradientBrush(Color.FromRgb(0xf0, 0xd0, 0x80), Color.FromRgb(0x7a, 0x5a, 0x24), 90), StrokeThickness = Math.Max(1.5, size * 0.08)
            });
            badge.Children.Add(new TextBlock
            {
                Text = shown.ToString(), FontFamily = HanUi.Font, FontWeight = FontWeights.Bold, FontSize = size * (shown >= 100 ? 0.42 : shown >= 10 ? 0.54 : 0.62),
                Foreground = shown > basePow ? Theme.B("#8ff07a") : shown < basePow ? Theme.B("#ff7a62") : d.IsHero ? Theme.B("#1a1209") : Theme.B("#fff4dc"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -size * 0.04, 0, 0)
            });
            Children.Add(badge);
        }
        // satır: sağ altta oyunun satır simgesi (çevikte iki simge), Gwent kartlarındaki gibi
        if (d.IsUnit)
        {
            var rows = d.Agile ? new[] { "row_close", "row_ranged" } : new[] { d.Row switch { Row.Close => "row_close", Row.Ranged => "row_ranged", _ => "row_siege" } };
            var rs = Math.Max(15, w * 0.2);
            var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, w * 0.035, w * 0.035), IsHitTestVisible = false };
            foreach (var r in rows)
            {
                var cell = new Grid { Width = rs, Height = rs, Margin = new Thickness(w * 0.012, 0, 0, 0) };
                cell.Children.Add(new Ellipse { Fill = new RadialGradientBrush(Color.FromRgb(0xf6, 0xdc, 0x96), Color.FromRgb(0xb0, 0x82, 0x34)) { GradientOrigin = new Point(0.4, 0.3) }, Stroke = Theme.B("#3a2a12"), StrokeThickness = Math.Max(1, rs * 0.06), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 } });
                if (Assets.Ui(r) is { } ic)
                {
                    var im = new Image { Source = ic, Width = rs * 0.74, Height = rs * 0.74 };
                    RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                    cell.Children.Add(im);
                }
                else cell.Children.Add(new TextBlock { Text = r == "row_close" ? "Y" : r == "row_ranged" ? "M" : "K", FontFamily = HanUi.Font, FontWeight = FontWeights.Bold, FontSize = rs * 0.55, Foreground = Theme.B("#ffe2a0"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                cell.ToolTip = r == "row_close" ? "Yakın" : r == "row_ranged" ? "Menzilli" : "Kuşatma";
                strip.Children.Add(cell);
            }
            Children.Add(strip);
        }
        // yıldızlar
        if (star > 1)
            Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, w * 0.035, w * 0.035, 0), Padding = new Thickness(w * 0.025, 0, w * 0.025, w * 0.005),
                CornerRadius = new CornerRadius(w * 0.06), Background = Theme.B("#d0120c07"), IsHitTestVisible = false,
                Child = new TextBlock { Text = new string('★', star), FontSize = w * 0.14, Foreground = Theme.B("#ffd76a") }
            });
        // fiyat (tezgâh)
        if (price is { } p)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            chip.Children.Add(HanArt.Icon("icon_crowns", w * 0.15, "◉"));
            chip.Children.Add(new TextBlock { Text = " " + p, FontFamily = HanUi.Font, FontSize = HanUi.S(w * 0.14), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffd76a"), VerticalAlignment = VerticalAlignment.Center });
            Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, star > 1 ? w * 0.22 : w * 0.04, w * 0.04, 0), Padding = new Thickness(w * 0.04, 0, w * 0.05, 0),
                Background = Theme.B("#e6120c07"), BorderBrush = Theme.B("#c9a24b"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(w * 0.1), Child = chip, IsHitTestVisible = false
            });
        }
    }

    /// <summary>Alt bilgi plakası: (ad) · yetenek simgesi + adı · sınıflar · (özet).</summary>
    private static FrameworkElement Plate(CardDef d, int star, double w, double h, double radius, Color tier, string? footer, bool named)
    {
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(w * 0.05, 0, d.IsUnit ? (d.Agile ? w * 0.4 : w * 0.23) : w * 0.04, w * 0.04), IsHitTestVisible = false };   // sağ alt: satır simgesi
        var lines = 0;
        if (named)
        {
            sp.Children.Add(new TextBlock { Text = d.Name, FontFamily = HanUi.Font, FontSize = HanUi.S(w * 0.112), FontWeight = FontWeights.Bold, Foreground = HanUi.GoldText, TextTrimming = TextTrimming.CharacterEllipsis });
            lines++;
        }
        var key = footer ?? Keyword(d);
        if (key.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (footer == null && AbilityIcon(d) is { } ic)
                row.Children.Add(new Image { Source = ic, Width = w * 0.15, Height = w * 0.15, Margin = new Thickness(0, 0, w * 0.025, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock
            {
                Text = HanUi.Tracked(HanUi.Upper(key)), FontFamily = HanUi.Font, FontSize = HanUi.S(w * 0.108), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = footer != null ? Theme.B("#ffd76a") : d.IsHero ? Theme.B("#ffd76a") : !d.IsUnit ? Theme.B("#9fd8ff") : Theme.B("#ffe2a0"), TextTrimming = TextTrimming.CharacterEllipsis
            });
            sp.Children.Add(row);
            lines++;
        }
        if (d.IsUnit && d.Tags.Length > 0)
        {
            var tags = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(w * 0.092), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            for (var i = 0; i < d.Tags.Length; i++)
            {
                if (i > 0) tags.Inlines.Add(new System.Windows.Documents.Run(" · ") { Foreground = Theme.B("#88e8dcc4") });
                tags.Inlines.Add(new System.Windows.Documents.Run(HT.Name(d.Tags[i])) { Foreground = new SolidColorBrush(TagColor(d.Tags[i])) });
            }
            sp.Children.Add(tags);
            lines++;
        }
        if (named && w >= 160 && Gist(d) is { Length: > 0 } gist)
        {
            sp.Children.Add(new TextBlock { Text = gist, FontFamily = HanUi.Font, FontSize = HanUi.S(w * 0.08), Foreground = Theme.B("#d8ccb4"), TextWrapping = TextWrapping.Wrap, MaxHeight = w * 0.2 });
            lines++;
        }
        var g = new Grid { IsHitTestVisible = false };
        // plakanın koyu zemini: yazı sayısına göre yükselir
        var plateH = Math.Min(h * 0.62, w * (0.07 + 0.13 * lines) + (named ? w * 0.06 : 0));
        g.Children.Add(new Border
        {
            Height = plateH, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, radius, radius),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x00, 0x0a, 0x07, 0x04), 0), new(Color.FromArgb(0xd8, 0x0a, 0x07, 0x04), 0.28), new(Color.FromArgb(0xf2, 0x0a, 0x07, 0x04), 1) }, 90)
        });
        if (lines > 0) g.Children.Add(sp);
        return g;
    }
}
