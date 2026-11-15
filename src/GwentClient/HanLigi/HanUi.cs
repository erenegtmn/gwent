using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi tasarım sistemi: tek yazı ailesi, yazı ölçeği, renk belirteçleri, köşesi kırık (pah) düğmeler ve çerçeveler.
/// <para><b>Yazı:</b> Witcher 3'ün kendi arayüz yazısı PF Din Text Cond (dar, keskin; <c>assets/ui/fonts</c>), yoksa Windows'un
/// Bahnschrift'i (aynı aileden dar grotesk). Başlık ve etiketler BÜYÜK HARF ve harf aralıklı; sayılar kalın ve altın degrade.</para>
/// <para><b>Düğme:</b> pahlı gövde, metalik altın kenar, üstte cam parıltısı; üstüne gelince ışır, basınca çöker, kapalıyken
/// söner. Dört tür: Birincil (eylem çağrısı), İkincil, Tehlike (sat), Hayalet (ikon).</para>
/// </summary>
public static class HanUi
{
    // ---------- renk belirteçleri ----------
    public static readonly Color Gold = Color.FromRgb(0xe8, 0xc3, 0x6a);
    public static readonly Color GoldHi = Color.FromRgb(0xff, 0xf0, 0xc4);
    public static readonly Color GoldDeep = Color.FromRgb(0x8a, 0x62, 0x22);
    public static readonly Color Ink = Color.FromRgb(0x0b, 0x08, 0x06);
    public static readonly Color Parchment = Color.FromRgb(0xf0, 0xe6, 0xd0);
    public static readonly Color Muted = Color.FromRgb(0x9c, 0x8e, 0x74);
    public static readonly Color Ember = Color.FromRgb(0xff, 0x8a, 0x3d);
    public static readonly Color Good = Color.FromRgb(0x95, 0xd8, 0x6c);
    public static readonly Color Bad = Color.FromRgb(0xe8, 0x62, 0x50);
    public static readonly Color Mana = Color.FromRgb(0x82, 0xcc, 0xff);

    public static SolidColorBrush Br(Color c, byte a = 0xff) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }

    /// <summary>Altın metal degrade (sayılar, birincil yazı).</summary>
    public static readonly Brush GoldText = Frozen(new LinearGradientBrush(new GradientStopCollection { new(GoldHi, 0), new(Gold, 0.55), new(Color.FromRgb(0xb8, 0x86, 0x30), 1) }, 90));
    public static readonly Brush GoldEdge = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb(0xff, 0xe8, 0xa8), 0), new(Color.FromRgb(0xb8, 0x86, 0x2e), 0.5), new(Color.FromRgb(0x6a, 0x4a, 0x16), 1) }, 90));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    // ---------- yazı ----------
    private static FontFamily? _font;
    private static bool _game;

    /// <summary>Tek yazı ailesi: oyunun PF Din Text Cond'u, yoksa Bahnschrift.</summary>
    public static FontFamily Font
    {
        get
        {
            if (_font != null) return _font;
            _game = Theme.GameFont || Theme.Serif.Source.Contains("Din");
            _font = _game ? Theme.Serif : new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");
            return _font;
        }
    }

    /// <summary>Dar oyun yazısı aynı boyda küçük görünür: boyutlar bununla çarpılır.</summary>
    private static double K => Font != null && _game ? 1.14 : 1.0;

    /// <summary>Yazı boyutu: ailenin darlığına göre ölçeklenmiş.</summary>
    public static double S(double size) => size * K;

    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Harf aralığı: WPF'te izleme yok, harflerin arasına ince boşluk konur.</summary>
    public static string Tracked(string s, int hair = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var gap = new string(' ', hair);
        return string.Join(gap, s.ToCharArray().Select(c => c.ToString()));
    }

    public static string Upper(string s) => s.ToUpper(Tr);

    /// <summary>Temel yazı.</summary>
    public static TextBlock T(string text, double size, Brush? fg = null, FontWeight? weight = null, TextAlignment align = TextAlignment.Left, bool wrap = false) => new()
    {
        Text = text, FontFamily = Font, FontSize = size * K, Foreground = fg ?? Br(Parchment), FontWeight = weight ?? FontWeights.Normal, TextAlignment = align,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, IsHitTestVisible = false
    };

    /// <summary>Gösteri başlığı: büyük, kalın, altın degrade, aralıklı büyük harf, gölgeli.</summary>
    public static TextBlock Display(string text, double size = 52, TextAlignment align = TextAlignment.Center)
    {
        var t = T(Tracked(Upper(text), size >= 40 ? 2 : 1), size, GoldText, FontWeights.Bold, align);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = size * 0.35, ShadowDepth = 2, Opacity = 0.95 };
        return t;
    }

    /// <summary>Bölüm başlığı (panel başı): kalın, açık.</summary>
    public static TextBlock Title(string text, double size = 24, Brush? fg = null, TextAlignment align = TextAlignment.Left)
        => T(text, size, fg ?? Br(Parchment), FontWeights.Bold, align);

    /// <summary>Etiket: küçük, aralıklı büyük harf, soluk altın.</summary>
    public static TextBlock Label(string text, double size = 11.5, Brush? fg = null)
        => T(Tracked(Upper(text), 2), size, fg ?? Br(Color.FromRgb(0xb8, 0x9a, 0x5e)), FontWeights.Bold);

    public static TextBlock Body(string text, double size = 15, Brush? fg = null, bool wrap = true, TextAlignment align = TextAlignment.Left)
        => T(text, size, fg ?? Br(Parchment, 0xe8), FontWeights.Normal, align, wrap);

    public static TextBlock Small(string text, double size = 12, Brush? fg = null) => T(text, size, fg ?? Br(Muted), FontWeights.Normal);

    /// <summary>Sayı: kalın, altın degrade (altın, can, güç).</summary>
    public static TextBlock Number(string text, double size, Brush? fg = null)
    {
        var t = T(text, size, fg ?? GoldText, FontWeights.Bold);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.9 };
        return t;
    }

    /// <summary>Masa üstündeki yazılar için gölge (zemin ne olursa olsun okunur).</summary>
    public static T Shadow<T>(T el, double blur = 6) where T : UIElement
    {
        el.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 1, Opacity = 1 };
        return el;
    }

    // ---------- biçimler ----------

    /// <summary>Pahlı dikdörtgen: dört köşesi <paramref name="cut"/> kadar kırık; <paramref name="inset"/> kadar içeriden.</summary>
    public static Geometry ChamferGeometry(double w, double h, double cut, double inset = 0)
    {
        var c = Math.Min(cut, Math.Min(w, h) / 2 - inset);
        double l = inset, t = inset, r = w - inset, b = h - inset;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(l + c, t), true, true);
            ctx.PolyLineTo(new[] { new Point(r - c, t), new Point(r, t + c), new Point(r, b - c), new Point(r - c, b), new Point(l + c, b), new Point(l, b - c), new Point(l, t + c) }, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Çerçeve (panel): pahlı koyu gövde, ince altın kenar, üst kenarda ortası parlayan çizgi ve köşe çentikleri.
    /// <paramref name="accent"/> verilirse kenar o renkte (rakip kırmızı, seçili altın).
    /// </summary>
    public static Grid Frame(double w, double h, UIElement? child = null, byte alpha = 0xe6, Color? accent = null, double cut = 10, bool glowTop = true)
    {
        var g = new Grid { Width = w, Height = h };
        var edge = accent is { } a
            ? (Brush)new LinearGradientBrush(Color.FromArgb(0xe0, a.R, a.G, a.B), Color.FromArgb(0x70, a.R, a.G, a.B), 90)
            : new LinearGradientBrush(Color.FromArgb(0xb0, 0xd8, 0xb4, 0x62), Color.FromArgb(0x48, 0x8a, 0x62, 0x22), 90);
        g.Children.Add(new Path
        {
            Data = ChamferGeometry(w, h, cut, 0.5), StrokeThickness = 1, Stroke = edge, IsHitTestVisible = child != null,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(alpha, 0x1e, 0x16, 0x0e), 0), new(Color.FromArgb(alpha, 0x12, 0x0c, 0x08), 0.35), new(Color.FromArgb(alpha, 0x0a, 0x07, 0x05), 1)
            }, 90)
        });
        if (glowTop)
        {
            var c = accent ?? Gold;
            g.Children.Add(new Rectangle
            {
                Height = 1.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(cut + 6, 1, cut + 6, 0), IsHitTestVisible = false,
                Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, c.R, c.G, c.B), 0), new(Color.FromArgb(0xe0, c.R, c.G, c.B), 0.5), new(Color.FromArgb(0, c.R, c.G, c.B), 1) }, 0)
            });
        }
        // köşe çentikleri (ince elmas)
        foreach (var (ha, va) in new[] { (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Bottom) })
            g.Children.Add(new Rectangle
            {
                Width = 5, Height = 5, HorizontalAlignment = ha, VerticalAlignment = va, Margin = new Thickness(cut * 0.3 - 2.5 + 0.5), IsHitTestVisible = false,
                Fill = Br(accent ?? Gold, 0xd0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45)
            });
        if (child != null) g.Children.Add(child);
        return g;
    }

    /// <summary>Yatay ayraç: ortası altın, uçları sönen ince çizgi, ortada küçük elmas.</summary>
    public static FrameworkElement Rule(double w, Color? c = null)
    {
        var col = c ?? Gold;
        var g = new Grid { Width = w, Height = 9, IsHitTestVisible = false };
        g.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Center, Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, col.R, col.G, col.B), 0), new(Color.FromArgb(0xc0, col.R, col.G, col.B), 0.5), new(Color.FromArgb(0, col.R, col.G, col.B), 1) }, 0) });
        g.Children.Add(new Rectangle { Width = 6, Height = 6, Fill = Br(col), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) });
        return g;
    }

    /// <summary>Küçük çip (rozet yazı): pahlı, ince kenarlı.</summary>
    public static Border Chip(string text, Color c, double size = 12, bool filled = false) => new()
    {
        Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(3), IsHitTestVisible = false,
        Background = filled ? new SolidColorBrush(Color.FromArgb(0x50, c.R, c.G, c.B)) : Br(Ink, 0xd0), BorderBrush = Br(c, 0xb0), BorderThickness = new Thickness(1),
        Child = T(Tracked(Upper(text)), size, Br(Color.Add(c, Color.FromRgb(0x20, 0x20, 0x20))), FontWeights.Bold)
    };

    /// <summary>İpucu kutusu: koyu, altın kenarlı, oyunun yazısıyla.</summary>
    public static ToolTip Tip(string text) => new()
    {
        Content = new TextBlock { Text = text, FontFamily = Font, FontSize = 15 * K, MaxWidth = 340, TextWrapping = TextWrapping.Wrap, Foreground = Br(Parchment) },
        Background = Br(Color.FromRgb(0x12, 0x0d, 0x08), 0xf6), BorderBrush = Br(Gold, 0x90), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 12, 10)
    };
}

/// <summary>Düğme türü.</summary>
public enum HanButtonKind { Primary, Secondary, Danger, Ghost }

/// <summary>
/// Han Ligi düğmesi (tasarım sistemi). Pahlı gövde, metalik kenar, cam parıltısı; üstüne gelince kenar ışır ve hafif yükselir,
/// basınca çöker, kapalıyken söner. Başlık BÜYÜK HARF ve aralıklı; isteğe bağlı alt yazı ve ikon.
/// </summary>
public sealed class HanButton : Button
{
    private static readonly ControlTemplate Plain = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>");

    private readonly Path _glow;
    private readonly Grid _body;
    private readonly TranslateTransform _lift = new();

    public HanButton(string title, Action click, HanButtonKind kind = HanButtonKind.Secondary, double w = 160, double h = 48, string? sub = null, FrameworkElement? icon = null, bool enabled = true, string? tip = null, double? titleSize = null)
    {
        Template = Plain;
        Focusable = false;
        Cursor = Cursors.Hand;
        IsEnabled = enabled;
        Width = w;
        Height = h;
        Click += (_, _) => click();
        if (tip != null) { ToolTip = HanUi.Tip(tip); ToolTipService.SetShowOnDisabled(this, true); }
        var (edge, fillTop, fillBot, text) = kind switch
        {
            HanButtonKind.Primary => (HanUi.GoldEdge, Color.FromRgb(0x5a, 0x3e, 0x12), Color.FromRgb(0x22, 0x16, 0x06), HanUi.GoldText),
            HanButtonKind.Danger => ((Brush)new LinearGradientBrush(Color.FromRgb(0xff, 0x9a, 0x84), Color.FromRgb(0x8a, 0x2a, 0x1c), 90), Color.FromRgb(0x46, 0x16, 0x10), Color.FromRgb(0x1c, 0x08, 0x06), (Brush)new LinearGradientBrush(Color.FromRgb(0xff, 0xd4, 0xc8), Color.FromRgb(0xf0, 0x7a, 0x62), 90)),
            HanButtonKind.Ghost => ((Brush)HanUi.Br(HanUi.Gold, 0x80), Color.FromRgb(0x1a, 0x12, 0x0a), Color.FromRgb(0x0c, 0x08, 0x05), (Brush)HanUi.Br(HanUi.Parchment)),
            _ => ((Brush)new LinearGradientBrush(Color.FromRgb(0xd8, 0xb4, 0x62), Color.FromRgb(0x5e, 0x45, 0x1c), 90), Color.FromRgb(0x2c, 0x20, 0x12), Color.FromRgb(0x12, 0x0c, 0x07), (Brush)HanUi.Br(HanUi.Parchment))
        };
        var cut = Math.Min(kind == HanButtonKind.Primary ? 12 : 8, h * 0.28);
        _body = new Grid { Width = w, Height = h, RenderTransform = _lift, Background = Brushes.Transparent };
        // ışıma (üstüne gelince)
        _glow = new Path
        {
            Data = HanUi.ChamferGeometry(w, h, cut, 1), Stroke = kind == HanButtonKind.Danger ? HanUi.Br(HanUi.Bad) : HanUi.Br(HanUi.GoldHi), StrokeThickness = 2, Opacity = kind == HanButtonKind.Primary ? 0.35 : 0,
            Effect = new BlurEffect { Radius = 10 }, IsHitTestVisible = false
        };
        _body.Children.Add(_glow);
        _body.Children.Add(new Path
        {
            Data = HanUi.ChamferGeometry(w, h, cut, 0.75), Stroke = edge, StrokeThickness = kind == HanButtonKind.Primary ? 1.6 : 1.1,
            Fill = new LinearGradientBrush(fillTop, fillBot, 90)
        });
        // cam parıltısı: üst yarı
        _body.Children.Add(new Path
        {
            Data = HanUi.ChamferGeometry(w, h * 0.5, Math.Max(2, cut - 2), 3), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top,
            Fill = new LinearGradientBrush(Color.FromArgb(kind == HanButtonKind.Primary ? (byte)0x38 : (byte)0x1c, 0xff, 0xf0, 0xd0), Color.FromArgb(0, 0xff, 0xf0, 0xd0), 90)
        });
        if (kind == HanButtonKind.Primary)
            foreach (var ha in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
                _body.Children.Add(new Rectangle
                {
                    Width = 6, Height = 6, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ha == HorizontalAlignment.Left ? -3 : 0, 0, ha == HorizontalAlignment.Right ? -3 : 0, 0),
                    Fill = HanUi.Br(HanUi.GoldHi), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45), IsHitTestVisible = false
                });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon != null) { icon.Margin = new Thickness(0, 0, 8, 0); icon.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(icon); }
        var size = titleSize ?? (kind == HanButtonKind.Primary ? Math.Min(26, h * 0.34) : Math.Min(18, h * 0.36));
        var t = HanUi.T(HanUi.Tracked(HanUi.Upper(title), kind == HanButtonKind.Primary ? 2 : 1), size, text, FontWeights.Bold);
        t.VerticalAlignment = VerticalAlignment.Center;
        if (kind == HanButtonKind.Primary) t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
        line.Children.Add(t);
        // başlık düğmeye sığmazsa küçülür (hiçbir zaman taşmaz)
        stack.Children.Add(new Viewbox { Child = line, StretchDirection = StretchDirection.DownOnly, MaxWidth = w - 22, HorizontalAlignment = HorizontalAlignment.Center });
        if (sub is { Length: > 0 })
        {
            var s = HanUi.T(sub, Math.Min(12.5, h * 0.2), HanUi.Br(kind == HanButtonKind.Danger ? Color.FromRgb(0xf0, 0xb0, 0xa0) : HanUi.Muted), FontWeights.SemiBold, TextAlignment.Center);
            s.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(s);
        }
        _body.Children.Add(stack);
        Content = _body;
        Opacity = enabled ? 1 : 0.38;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.38;
        var idle = kind == HanButtonKind.Primary ? 0.35 : 0.0;
        MouseEnter += (_, _) =>
        {
            if (!IsEnabled) return;
            _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, TimeSpan.FromMilliseconds(120)));
            Sfx.Play("han_hover");
        };
        MouseLeave += (_, _) =>
        {
            _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(idle, TimeSpan.FromMilliseconds(180)));
            _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
        };
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(IsPressed ? 1.5 : IsMouseOver ? -2 : 0, TimeSpan.FromMilliseconds(70)));
    }
}
