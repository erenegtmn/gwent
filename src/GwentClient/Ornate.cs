using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>
/// Kesik köşeli (pah kırık) çerçeve: deri dolgu, çift altın çizgi ve köşelerde oyunun bronz güneş süsü.
/// Hem büyük paneller hem düğmeler için aynı çizim; web kutusu yerine oyun arayüzü gibi durur.
/// </summary>
public sealed class ChamferFrame : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = Reg<Brush?>(nameof(Fill), null);
    public static readonly DependencyProperty EdgeProperty = Reg<Brush?>(nameof(Edge), null);
    public static readonly DependencyProperty InnerProperty = Reg<Brush?>(nameof(Inner), null);
    public static readonly DependencyProperty CutProperty = Reg(nameof(Cut), 16.0);
    public static readonly DependencyProperty InsetProperty = Reg(nameof(Inset), 6.0);
    public static readonly DependencyProperty SunSizeProperty = Reg(nameof(SunSize), 0.0);   // 0: köşe süsü yok

    private static DependencyProperty Reg<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(ChamferFrame), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Edge { get => (Brush?)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public Brush? Inner { get => (Brush?)GetValue(InnerProperty); set => SetValue(InnerProperty, value); }
    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }
    public double Inset { get => (double)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }
    public double SunSize { get => (double)GetValue(SunSizeProperty); set => SetValue(SunSizeProperty, value); }

    public ChamferFrame() { IsHitTestVisible = false; SnapsToDevicePixels = true; }

    internal static StreamGeometry Chamfer(double x, double y, double w, double h, double c)
    {
        c = Math.Max(0, Math.Min(c, Math.Min(w, h) / 2));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x + c, y), true, true);
            ctx.LineTo(new Point(x + w - c, y), true, false);
            ctx.LineTo(new Point(x + w, y + c), true, false);
            ctx.LineTo(new Point(x + w, y + h - c), true, false);
            ctx.LineTo(new Point(x + w - c, y + h), true, false);
            ctx.LineTo(new Point(x + c, y + h), true, false);
            ctx.LineTo(new Point(x, y + h - c), true, false);
            ctx.LineTo(new Point(x, y + c), true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        dc.DrawGeometry(Fill, Edge == null ? null : new Pen(Edge, 1.6), Chamfer(0.8, 0.8, w - 1.6, h - 1.6, Cut));
        if (Inner != null && Inset > 0 && w > Inset * 2 + 8 && h > Inset * 2 + 8)
            dc.DrawGeometry(null, new Pen(Inner, 0.8), Chamfer(Inset, Inset, w - Inset * 2, h - Inset * 2, Math.Max(2, Cut - Inset * 0.6)));
        if (SunSize > 0 && Assets.Ui("corner_sun") is { } sun && w > SunSize * 2.2 && h > SunSize * 2.2)
        {
            var r = new Rect(-SunSize * 0.12, -SunSize * 0.12, SunSize, SunSize);
            foreach (var m in new[] { new Matrix(1, 0, 0, 1, 0, 0), new Matrix(-1, 0, 0, 1, w, 0), new Matrix(1, 0, 0, -1, 0, h), new Matrix(-1, 0, 0, -1, w, h) })
            {
                dc.PushTransform(new MatrixTransform(m));
                dc.DrawImage(sun, r);
                dc.Pop();
            }
        }
    }
}

/// <summary>Ortak oyun arayüzü parçaları: paneller, ayraçlar, başlıklar, sahneye giriş animasyonları.</summary>
public static class Ornate
{
    /// <summary>Sınamalar ve ekran görüntüleri (--shot/--selftest) animasyonsuz çalışır; gerçek oyunda açık.</summary>
    public static bool Animate { get; set; } = true;

    public static readonly Brush Leather = Grad("#ee1f160d", "#f2120c07");
    public static readonly Brush GoldEdge = Grad("#e0bf6a", "#7a5c24");
    public static readonly Brush GoldThin = Theme.B("#6a5226");

    private static Brush Grad(string a, string b)
    {
        var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 90);
        g.Freeze();
        return g;
    }

    /// <summary>Kesik köşeli, güneş süslü panel. İçerik <paramref name="pad"/> kadar içeriden yerleşir.</summary>
    public static Grid Panel(UIElement child, Thickness? pad = null, double cut = 22, double sun = 70, Brush? fill = null)
    {
        var g = new Grid();
        g.Children.Add(new ChamferFrame { Fill = fill ?? Leather, Edge = GoldEdge, Inner = GoldThin, Cut = cut, Inset = 8, SunSize = sun });
        g.Children.Add(new ContentControl { Content = child, Margin = pad ?? new Thickness(46, 38, 46, 42) });
        g.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 0, Opacity = 0.9 };
        return g;
    }


    /// <summary>Seçenek plakası: solda altın eşkenar dörtgen (açıkken dolu), başlık ve açıklama. <c>IsChecked</c> ile okunur.</summary>
    public static System.Windows.Controls.Primitives.ToggleButton Option(string title, string desc, bool on)
    {
        var diamond = new TextBlock { Text = on ? "◆" : "◇", FontSize = 20, Foreground = Theme.Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), IsHitTestVisible = false };
        var texts = new StackPanel { IsHitTestVisible = false };
        texts.Children.Add(Ui.Text(title, 17, Theme.Text, true));
        texts.Children.Add(Ui.Text(desc, 13, Theme.TextDim));
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        row.Children.Add(diamond);
        row.Children.Add(texts);
        var tb = new System.Windows.Controls.Primitives.ToggleButton
        {
            Style = (Style)Application.Current.FindResource("GToggle"), Content = row, IsChecked = on, Margin = new Thickness(0, 5, 0, 5),
            Padding = new Thickness(18, 10, 18, 10), HorizontalContentAlignment = HorizontalAlignment.Left
        };
        tb.Checked += (_, _) => diamond.Text = "◆";
        tb.Unchecked += (_, _) => diamond.Text = "◇";
        return tb;
    }

    /// <summary>Küçük, aralıklı büyük harfli bölüm etiketi.</summary>
    public static TextBlock Caption(string text, double top = 14)
    {
        var l = Ui.Text(Spaced(text.ToUpper(new System.Globalization.CultureInfo("tr-TR"))), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, top, 0, 6);
        return l;
    }

    /// <summary>Yatay ayraç: iki yanda sönen altın çizgi, ortada eşkenar dörtgen.</summary>
    public static FrameworkElement Rule(double width = 420, double margin = 14)
    {
        var c = new Canvas { Width = width, Height = 14, Margin = new Thickness(0, margin, 0, margin), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        for (var side = 0; side < 2; side++)
        {
            var line = new System.Windows.Shapes.Rectangle
            {
                Width = width / 2 - 16, Height = 1.3, Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(side == 0 ? (byte)0 : (byte)255, 0xc9, 0xa2, 0x4a), 0),
                    new GradientStop(Color.FromArgb(side == 0 ? (byte)255 : (byte)0, 0xc9, 0xa2, 0x4a), 1)
                }, 0)
            };
            Canvas.SetLeft(line, side == 0 ? 0 : width / 2 + 16);
            Canvas.SetTop(line, 6.5);
            c.Children.Add(line);
        }
        var d = new System.Windows.Shapes.Rectangle { Width = 8, Height = 8, Fill = Theme.B("#e0bf6a"), RenderTransform = new RotateTransform(45, 4, 4) };
        Canvas.SetLeft(d, width / 2 - 4);
        Canvas.SetTop(d, 3);
        c.Children.Add(d);
        return c;
    }

    /// <summary>Büyük, aralıklı harfli başlık + altında küçük açıklama; orijinal oyun başlıkları gibi.</summary>
    public static UIElement Heading(string title, string? sub = null, double size = 40, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var sp = new StackPanel { HorizontalAlignment = align };
        var t = new TextBlock
        {
            Text = Spaced(title.ToUpper(new System.Globalization.CultureInfo("tr-TR"))), FontFamily = Theme.Serif, FontSize = size * Theme.FontScale,
            Foreground = new LinearGradientBrush(Color.FromRgb(0xf2, 0xdc, 0x9a), Color.FromRgb(0xb8, 0x92, 0x44), 90), FontWeight = FontWeights.SemiBold,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.95 }
        };
        sp.Children.Add(t);
        if (sub != null) sp.Children.Add(Ui.Text(sub, 15, Theme.TextDim));
        return sp;
    }

    /// <summary>Harf aralığı (WPF'te yok): ince boşluklar ekler.</summary>
    public static string Spaced(string s) => string.Join("  ", s.Select(ch => ch.ToString()));

    /// <summary>Öğeleri sırayla yukarı kayarak belirir (sinematik giriş). Animasyon kapalıysa hiçbir şey yapmaz.</summary>
    public static void Reveal(IEnumerable<UIElement> items, double dy = 26, int startMs = 80, int stepMs = 90, int durMs = 520)
    {
        if (!Animate) return;
        var i = 0;
        foreach (var el in items)
        {
            var shift = new TranslateTransform(0, dy);
            el.RenderTransform = shift;
            el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(startMs + i++ * stepMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durMs)) { BeginTime = begin, EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(durMs)) { BeginTime = begin, EasingFunction = ease });
        }
    }

    public static void Reveal(params UIElement[] items) => Reveal((IEnumerable<UIElement>)items);

    /// <summary>Tüm ekranlara ortak sahne zemini: koyu leke, kenar karartması ve (varsa) panorama. Üstüne <paramref name="content"/> konur.</summary>
    public static Grid Stage(UIElement content, string pano = "pano_kaer_morhen", double dim = 0.55)
    {
        var g = new Grid { Background = Theme.Bg, ClipToBounds = true };
        if (Assets.Ui(pano) is { } src)
            g.Children.Add(new Image { Source = src, Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Opacity = 1 - dim * 0.5 });
        g.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.78, RadiusY = 0.88,
                GradientStops = { new GradientStop(Color.FromArgb((byte)(255 * dim * 0.35), 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xe6, 0, 0, 0), 1) }
            }
        });
        g.Children.Add(Embers(26));
        g.Children.Add(content);
        // Üst-alt sinema şeritleri: oyun ekranı gibi çerçeve
        g.Children.Add(Letterbox(true));
        g.Children.Add(Letterbox(false));
        return g;
    }

    private static UIElement Letterbox(bool top) => new Border
    {
        Height = 34, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, IsHitTestVisible = false,
        Background = new LinearGradientBrush(Color.FromArgb(0xf0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), top ? 90 : 270)
    };

    /// <summary>Hafif, yukarı süzülen kor parçacıkları. Yalnızca ekranda görünürken çalışır; ekran kapanınca kendini kapatır.</summary>
    public static UIElement Embers(int count)
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        if (!Animate) return canvas;
        var rnd = new Random(7);
        var p = new List<(Ellipse2 e, double x, double y, double v, double sway, double ph)>();
        for (var i = 0; i < count; i++)
        {
            var size = 2 + rnd.NextDouble() * 3.5;
            var e = new Ellipse2(size);
            canvas.Children.Add(e.Shape);
            p.Add((e, rnd.NextDouble(), rnd.NextDouble(), 0.012 + rnd.NextDouble() * 0.03, 0.01 + rnd.NextDouble() * 0.03, rnd.NextDouble() * 6.28));
        }
        var last = DateTime.UtcNow;
        void Tick(object? s, EventArgs a)
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min(0.1, (now - last).TotalSeconds);
            last = now;
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 10) return;
            for (var i = 0; i < p.Count; i++)
            {
                var q = p[i];
                q.y -= q.v * dt;
                q.ph += dt * 0.9;
                if (q.y < -0.05) { q.y = 1.05; q.x = Random.Shared.NextDouble(); }
                p[i] = q;
                var x = (q.x + Math.Sin(q.ph) * q.sway) * w;
                var y = q.y * h;
                Canvas.SetLeft(q.e.Shape, x);
                Canvas.SetTop(q.e.Shape, y);
                q.e.Shape.Opacity = Math.Clamp(Math.Sin(Math.Clamp(q.y, 0, 1) * Math.PI), 0, 1) * 0.8 * (0.6 + 0.4 * Math.Sin(q.ph * 3));
            }
        }
        canvas.Loaded += (_, _) => CompositionTarget.Rendering += Tick;
        canvas.Unloaded += (_, _) => CompositionTarget.Rendering -= Tick;
        return canvas;
    }

    private sealed class Ellipse2
    {
        public System.Windows.Shapes.Ellipse Shape { get; }
        public Ellipse2(double size)
        {
            Shape = new System.Windows.Shapes.Ellipse
            {
                Width = size, Height = size, IsHitTestVisible = false,
                Fill = new RadialGradientBrush(Color.FromRgb(0xff, 0xd8, 0x80), Color.FromArgb(0, 0xe0, 0x70, 0x20)),
                Effect = null
            };
        }
    }
}
