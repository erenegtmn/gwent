using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi görselleri: oyundan çıkarılmış simgeler (<c>assets/ui/han</c>, tools/w3_han_assets.py) ve onlarla kurulan parçalar
/// (işaret madalyonu, ikon, renklendirilmiş efekt dokusu). Dosya yoksa çizimle (yer tutucu) çalışır.
/// </summary>
public static class HanArt
{
    public static ImageSource? Src(string name) => Assets.Ui("han/" + name);

    public static Color SignColor(Sign s) => s switch
    {
        Sign.Aard => Color.FromRgb(0x8f, 0xd3, 0xff),
        Sign.Igni => Color.FromRgb(0xff, 0x8a, 0x2e),
        Sign.Quen => Color.FromRgb(0xff, 0xd8, 0x5a),
        _ => Color.FromRgb(0xb9, 0x7c, 0xff)
    };

    public static string PotionIcon(Potion p) => p switch
    {
        Potion.Thunderbolt => "potion_thunderbolt", Potion.Swallow => "potion_swallow", Potion.BlackBlood => "potion_blackblood",
        Potion.DancingStar => "bomb_dancingstar", _ => "bomb_northernwind"
    };

    public static string SchoolIcon(School s) => "school_" + s switch
    {
        School.Wolf => "wolf", School.Cat => "cat", School.Griffin => "griffin", School.Bear => "bear", School.Viper => "viper", _ => "manticore"
    };

    /// <summary>İkon; dosya yoksa <paramref name="fallback"/> yazısı.</summary>
    public static FrameworkElement Icon(string name, double size, string fallback = "")
    {
        if (Src(name) is { } src)
        {
            var img = new Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        return new TextBlock { Text = fallback, Width = size, FontSize = size * 0.5, Foreground = Theme.Gold, TextAlignment = TextAlignment.Center, FontFamily = HanUi.Font, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
    }

    /// <summary>Beyaz efekt dokusunu istenen renge boyar (doku saydamlık maskesi olur).</summary>
    public static FrameworkElement Tinted(string name, double w, double h, Color color)
    {
        if (Src(name) is not { } src)
            return new Ellipse { Width = w, Height = h, Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(3, w * 0.03), IsHitTestVisible = false };
        return new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(color), OpacityMask = new ImageBrush(src) { Stretch = Stretch.Fill }, IsHitTestVisible = false };
    }

    /// <summary>İşaretin çizgisel sembolü (100×100 kutuda).</summary>
    public static Geometry SignGeometry(Sign s) => Geometry.Parse(s switch
    {
        Sign.Aard => "M50,12 L88,82 L12,82 Z M26,58 L98,58",
        Sign.Igni => "M50,12 L88,82 L12,82 Z",
        Sign.Quen => "M12,18 L88,18 L50,88 Z M28,38 L60,38",
        _ => "M14,14 L86,14 L50,50 Z M14,86 L86,86 L50,50 Z M8,50 L92,50"
    });

    public static Path SignGlyph(Sign s, double size, Color? color = null, double thickness = 0)
    {
        var c = color ?? SignColor(s);
        return new Path
        {
            Data = SignGeometry(s), Stretch = Stretch.Uniform, Width = size, Height = size, Stroke = new SolidColorBrush(c),
            StrokeThickness = thickness > 0 ? thickness : Math.Max(2.5, size * 0.07), StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false, Effect = new DropShadowEffect { Color = c, BlurRadius = size * 0.35, ShadowDepth = 0, Opacity = 0.95 }
        };
    }

    /// <summary>
    /// İşaret madalyonu: koyu disk, metal halka ve renkli sembol; altında aralıklı büyük harfle adı ve Odak bedeli (mavi taşlar).
    /// Seçiliyken halka işaret renginde ışır.
    /// </summary>
    public static Grid SignMedallion(Sign s, double size, bool selected, bool enabled, int cost)
    {
        var c = SignColor(s);
        var g = new Grid { Width = size, Height = size + 30, Opacity = enabled ? 1 : 0.35, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        var disk = new Ellipse
        {
            Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top,
            Fill = new RadialGradientBrush(Color.FromArgb(0xff, 0x2c, 0x22, 0x18), Color.FromArgb(0xff, 0x08, 0x06, 0x04)) { GradientOrigin = new Point(0.5, 0.3) },
            Stroke = selected ? new SolidColorBrush(c) : HanUi.GoldEdge, StrokeThickness = selected ? 3.5 : 2
        };
        if (selected) disk.Effect = new DropShadowEffect { Color = c, BlurRadius = 28, ShadowDepth = 0, Opacity = 1 };
        g.Children.Add(disk);
        g.Children.Add(new Ellipse { Width = size - 12, Height = size - 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0), Stroke = new SolidColorBrush(Color.FromArgb(0x50, c.R, c.G, c.B)), StrokeThickness = 1, IsHitTestVisible = false });
        var glyph = SignGlyph(s, size * 0.54, selected ? Color.Add(c, Color.FromRgb(0x30, 0x30, 0x30)) : Color.Multiply(c, 0.85f));
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, size * 0.22, 0, 0);
        g.Children.Add(glyph);
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        label.Children.Add(HanUi.T(HanUi.Tracked(HanUi.Upper(s.ToString()), 2), 13, selected ? new SolidColorBrush(c) : HanUi.Br(HanUi.Parchment), FontWeights.Bold));
        if (cost == 0) label.Children.Add(HanUi.T("  bedava", 11, HanUi.Br(HanUi.Mana), FontWeights.SemiBold));
        for (var i = 0; i < cost; i++)
            label.Children.Add(new Ellipse { Width = 7, Height = 7, Margin = new Thickness(i == 0 ? 7 : 3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Fill = new RadialGradientBrush(Color.FromRgb(0xd8, 0xf2, 0xff), Color.FromRgb(0x2a, 0x84, 0xc8)) });
        g.Children.Add(label);
        return g;
    }

    /// <summary>Altın çerçeveli yuvarlak ikon düğmesi (iksir, bomba).</summary>
    public static Grid IconToken(string icon, double size, bool selected, string fallback)
    {
        var g = new Grid { Width = size, Height = size, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        var disk = new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0xff, 0x33, 0x27, 0x1a), Color.FromArgb(0xff, 0x0e, 0x0a, 0x06)),
            Stroke = selected ? Theme.Select : HanUi.GoldEdge, StrokeThickness = selected ? 3.5 : 2
        };
        if (selected) disk.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 225, 130), BlurRadius = 26, ShadowDepth = 0, Opacity = 1 };
        g.Children.Add(disk);
        var ic = Icon(icon, size * 0.72, fallback);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        ic.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);
        return g;
    }

    /// <summary>Okul amblemi madalyonu.</summary>
    public static Grid SchoolMedallion(School? s, double size, bool mine)
    {
        var g = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        g.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0xff, 0x30, 0x26, 0x1c), Color.FromArgb(0xff, 0x0c, 0x09, 0x06)),
            Stroke = mine ? Theme.B("#c9a24b") : Theme.B("#9a5a4a"), StrokeThickness = Math.Max(2, size * 0.04),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.9 }
        });
        if (s is { } school)
        {
            var ic = Icon(SchoolIcon(school), size * 0.74, global::HanLigi.Texts.Name(school)[..1]);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
        }
        return g;
    }

    /// <summary>
    /// Oyunun zafer/yenilgi/beraberlik amblemi (tek renkli şekil maskesi): paletteki gradyanla boyanır, altına başlık yazılır.
    /// <paramref name="result"/>: 1 zafer, −1 yenilgi, 0 beraberlik.
    /// </summary>
    public static FrameworkElement ResultEmblem(int result, string title, double width)
    {
        var (frame, g1, g2, glow, fg) = result > 0
            ? ("msg_battle_victory", Color.FromRgb(0xf0, 0xd0, 0x80), Color.FromRgb(0x8a, 0x64, 0x24), Color.FromRgb(0xd0, 0x98, 0x30), Color.FromRgb(0xf4, 0xd8, 0x8a))
            : result < 0
                ? ("msg_battle_defeat", Color.FromRgb(0xc8, 0x62, 0x50), Color.FromRgb(0x5a, 0x1a, 0x12), Color.FromRgb(0xa0, 0x28, 0x1a), Color.FromRgb(0xe0, 0x7a, 0x62))
                : ("msg_battle_draw", Color.FromRgb(0xcc, 0xc2, 0xaa), Color.FromRgb(0x62, 0x5c, 0x4e), Color.FromRgb(0x80, 0x78, 0x68), Color.FromRgb(0xd8, 0xcc, 0xb4));
        var sp = new StackPanel { Width = width, IsHitTestVisible = false };
        if (Assets.Ui(frame) is { } src)
            sp.Children.Add(new Rectangle
            {
                Width = width, Height = width * 0.45, Fill = new LinearGradientBrush(g1, g2, 90), OpacityMask = new ImageBrush(src) { Stretch = Stretch.Uniform },
                Effect = new DropShadowEffect { Color = glow, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 }
            });
        sp.Children.Add(new TextBlock
        {
            Text = title, FontFamily = HanUi.Font, FontSize = HanUi.S(width * 0.17), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(fg), TextAlignment = TextAlignment.Center,
            Effect = new DropShadowEffect { Color = glow, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.8 }
        });
        return sp;
    }

    // ---------- animasyon parçaları ----------

    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Öğeyi merkezinde büyütüp söndürür (parlama, dalga, sembol). Bitince katmandan kaldırır.</summary>
    public static void Burst(Canvas layer, FrameworkElement el, Point center, double fromScale, double toScale, int ms, int delayMs = 0, double peak = 1)
    {
        el.Opacity = 0;
        Canvas.SetLeft(el, center.X - el.Width / 2);
        Canvas.SetTop(el, center.Y - el.Height / 2);
        var sc = new ScaleTransform(fromScale, fromScale);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = sc;
        layer.Children.Add(el);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var d = TimeSpan.FromMilliseconds(ms);
        var grow = new DoubleAnimation(fromScale, toScale, d) { BeginTime = begin, EasingFunction = Out };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = d };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.18)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.55)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => layer.Children.Remove(el);
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Dikdörtgen bir alanı kısa süre parlatır (satır vurgusu, kalkan).</summary>
    public static void Flash(Canvas layer, Rect area, Brush fill, int ms, int delayMs = 0, double peak = 0.75, double radius = 6)
    {
        var r = new Rectangle { Width = area.Width, Height = area.Height, Fill = fill, RadiusX = radius, RadiusY = radius, IsHitTestVisible = false, Opacity = 0 };
        Canvas.SetLeft(r, area.X);
        Canvas.SetTop(r, area.Y);
        layer.Children.Add(r);
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs), Duration = TimeSpan.FromMilliseconds(ms) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.2)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak * 0.8, KeyTime.FromPercent(0.6)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => layer.Children.Remove(r);
        r.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Yükselip sönen yazı (hasar, "ENGELLENDİ", "+1 Odak").</summary>
    public static void FloatText(Canvas layer, string text, Point at, Color color, double size, int ms = 1100, int delayMs = 0)
    {
        var t = new TextBlock
        {
            Text = text, FontFamily = HanUi.Font, FontSize = HanUi.S(size), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), IsHitTestVisible = false, Opacity = 0,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 1 }
        };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, at.X - t.DesiredSize.Width / 2);
        Canvas.SetTop(t, at.Y - t.DesiredSize.Height / 2);
        var move = new TranslateTransform();
        t.RenderTransform = move;
        layer.Children.Add(t);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, -46, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = Out });
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = TimeSpan.FromMilliseconds(ms) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => layer.Children.Remove(t);
        t.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
