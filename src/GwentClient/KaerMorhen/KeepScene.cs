using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KaerMorhen.Core;

namespace GwentClient.KaerMorhen;

/// <summary>
/// Kaer Morhen'in sinematik sahnesi: oyunun kendi render'ı (menü panoraması, tools/w3_keep_textures.py → km/scene_main.jpg; oyuncunun
/// çektiği km/shots/main.jpg varsa o) üstünde gerçek binalara iliştirilmiş noktalar. Onarılan binalardan sıcak ışık sızar, harabeler
/// karanlıkta kalır; kamera yavaşça kayar, sis süzülür; binaya tıklayınca görüntü oraya yakınlaşır (km/shots/&lt;bina&gt;.jpg varsa o
/// yakın çekime geçer). Kuşatmada gök kızarır, kar yağar.
/// </summary>
public sealed class KeepScene : Grid
{
    private const double W = 1920, H = 1080;
    private readonly KeepSave _s;
    private readonly Canvas _world = new() { Width = W, Height = H };
    private readonly ScaleTransform _zoom = new(1.06, 1.06, W / 2, H / 2);
    private readonly TranslateTransform _pan = new();
    private readonly TranslateTransform _drift = new();
    private readonly Image _closeUp = new() { Width = W, Height = H, Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
    private readonly Dictionary<string, FrameworkElement> _spots = new();
    private string? _focus;

    /// <summary>Bina noktaları (sahne görüntüsünde oransal konum). Oyuncunun çektiği ana görüntüde konumlar km/shots/spots.txt ile verilebilir.</summary>
    private static readonly Dictionary<string, Point> DefaultSpots = new()
    {
        [Keep.WarRoom] = new(0.385, 0.435),
        [Keep.AlliesHall] = new(0.339, 0.27),
        [Keep.Forge] = new(0.508, 0.555),
        [Keep.Alchemy] = new(0.557, 0.50),
        [Keep.Barracks] = new(0.43, 0.70),
        [Keep.Workshop] = new(0.8125, 0.645),
    };

    /// <summary>Bir binaya tıklandı (kimliği).</summary>
    public event Action<string>? BuildingClicked;

    public KeepScene(KeepSave s)
    {
        _s = s;
        Width = W; Height = H;
        ClipToBounds = true;
        Background = Brushes.Black;
        var siege = Keep.ContractsLocked(s);
        var spots = LoadSpots();

        // Sahne (kayan ve yakınlaşan katman): gerçek görüntü + ışıklar + noktalar
        var main = Assets.Ui("km/shots/main") ?? Assets.Ui("km/scene_main") ?? Assets.Ui("pano_kaer_morhen");
        if (main != null)
        {
            var img = new Image { Source = main, Width = W, Height = H, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _world.Children.Add(img);
        }
        else _world.Children.Add(new Rectangle { Width = W, Height = H, Fill = Theme.B("#1a1814") });
        foreach (var b in Keep.Buildings)
        {
            if (!spots.TryGetValue(b.Id, out var rel)) continue;
            var at = new Point(rel.X * W, rel.Y * H);
            var lvl = s.Level(b.Id);
            if (lvl == 0) Put(_world, Smoke(), at, 200, 160);
            else Put(_world, Glow(lvl, siege), at, 120 + 50 * lvl, 100 + 40 * lvl);
        }
        foreach (var b in Keep.Buildings)
            if (spots.TryGetValue(b.Id, out var rel)) AddSpot(b, new Point(rel.X * W, rel.Y * H));
        _world.RenderTransform = new TransformGroup { Children = { _zoom, _drift, _pan } };
        Children.Add(_world);
        Children.Add(_closeUp);

        // Renk ve hava: soğuk sabah ya da kuşatmanın kızıl karanlığı
        Children.Add(new Rectangle
        {
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(siege ? Color.FromArgb(0x70, 0x40, 0x06, 0x04) : Color.FromArgb(0x26, 0x30, 0x40, 0x60),
                siege ? Color.FromArgb(0x90, 0x08, 0x02, 0x04) : Color.FromArgb(0x10, 0x60, 0x40, 0x10), 90)
        });
        Children.Add(Fog(0.30, 0.22, 70));
        Children.Add(Fog(0.62, 0.18, 95));
        if (siege) Children.Add(Snow());
        Children.Add(new Rectangle
        {
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.48), GradientOrigin = new Point(0.5, 0.48), RadiusX = 0.75, RadiusY = 0.8,
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0xd8, 0, 0, 0), 1) }
            }
        });

        Loaded += (_, _) =>
        {
            if (!Ornate.Animate) return;
            _drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 28, TimeSpan.FromSeconds(38))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(27))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        };
    }

    /// <summary>km/shots/spots.txt: her satır "bina_kimliği x y" (0-1 oranları); oyuncunun kendi ana görüntüsü için.</summary>
    private static Dictionary<string, Point> LoadSpots()
    {
        var spots = new Dictionary<string, Point>(DefaultSpots);
        if (Assets.Ui("km/shots/main") == null || Assets.Dir is not { } dir) return spots;
        var file = System.IO.Path.Combine(dir, "ui", "km", "shots", "spots.txt");
        if (!System.IO.File.Exists(file)) return spots;
        foreach (var line in System.IO.File.ReadAllLines(file))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 3 && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var x)
                && double.TryParse(p[2], System.Globalization.CultureInfo.InvariantCulture, out var y)) spots[p[0]] = new Point(x, y);
        }
        return spots;
    }

    private static void Put(Canvas c, FrameworkElement el, Point center, double w, double h)
    {
        el.Width = w; el.Height = h;
        Canvas.SetLeft(el, center.X - w / 2);
        Canvas.SetTop(el, center.Y - h / 2);
        c.Children.Add(el);
    }

    /// <summary>Onarılmış binanın pencerelerinden sızan ışık: seviyeyle büyür, hafifçe titrer.</summary>
    private static FrameworkElement Glow(int lvl, bool siege)
    {
        var e = new Ellipse
        {
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(0x70 + 0x20 * lvl), 0xff, siege ? (byte)0x90 : (byte)0xb8, 0x50), 0),
                    new GradientStop(Color.FromArgb(0x30, 0xff, 0x90, 0x30), 0.45),
                    new GradientStop(Color.FromArgb(0, 0xff, 0x80, 0x20), 1)
                }
            }
        };
        if (Ornate.Animate)
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(900 + Random.Shared.Next(600)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        return e;
    }

    /// <summary>Harabe: kararmış, dumanlı bölge.</summary>
    private static FrameworkElement Smoke() => new Ellipse
    {
        IsHitTestVisible = false,
        Fill = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(0x90, 0x08, 0x08, 0x0a), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) }
        }
    };

    /// <summary>Binanın üstündeki işaret: halka (nabız atar), ad ve seviye; yükseltilebilirse altın.</summary>
    private void AddSpot(Building b, Point at)
    {
        var lvl = _s.Level(b.Id);
        var ready = Keep.UpgradeBlocked(_s, b.Id) == null;
        var color = ready ? Theme.B("#f0d890") : lvl == 0 ? Theme.B("#b8b0a0") : Theme.B("#e8c970");
        var g = new Grid { Width = 260, Height = 120, Cursor = Cursors.Hand, Tag = "keep-hotspot:" + b.Id, Background = Brushes.Transparent };
        var ring = new Ellipse { Width = 34, Height = 34, Stroke = color, StrokeThickness = 2.5, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        var dot = new Ellipse { Width = 12, Height = 12, Fill = color, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 11, 0, 0) };
        ring.RenderTransformOrigin = new Point(0.5, 0.5);
        ring.RenderTransform = new ScaleTransform(1, 1);
        if (Ornate.Animate)
        {
            var pulse = new DoubleAnimation(1, 1.5, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        var label = new TextBlock
        {
            Text = $"{b.Name}{(ready ? "  ▲" : "")}", FontFamily = Theme.Serif, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var pips = new TextBlock
        {
            Text = lvl == 0 ? "harabe" : string.Concat(Enumerable.Range(0, b.MaxLevel).Select(i => i < lvl ? "●" : "○")),
            FontSize = 15, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center
        };
        // Açık renkli taş duvar üstünde okunsun: koyu, yarı saydam plaka
        var plate = new Border
        {
            Background = Theme.B("#b0100c08"), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 3, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0),
            Child = new StackPanel { Children = { label, pips } }
        };
        g.Children.Add(ring); g.Children.Add(dot); g.Children.Add(plate);
        g.MouseEnter += (_, _) => { label.Foreground = Brushes.White; plate.Background = Theme.B("#e0281c0c"); };
        g.MouseLeave += (_, _) => { label.Foreground = color; plate.Background = Theme.B("#b0100c08"); };
        g.MouseLeftButtonUp += (_, e) => { e.Handled = true; BuildingClicked?.Invoke(b.Id); };
        Canvas.SetLeft(g, at.X - 130);
        Canvas.SetTop(g, at.Y - 17);
        _world.Children.Add(g);
        _spots[b.Id] = g;
    }

    /// <summary>Kayan sis bandı.</summary>
    private static FrameworkElement Fog(double y, double opacity, double seconds)
    {
        var r = new Rectangle
        {
            IsHitTestVisible = false, Width = W * 2, Height = 260, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-W / 2, y * H - 130, 0, 0), Opacity = opacity,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0xd8, 0xdc, 0xe0), 0), new GradientStop(Color.FromArgb(0x90, 0xd8, 0xdc, 0xe0), 0.5), new GradientStop(Color.FromArgb(0, 0xd8, 0xdc, 0xe0), 1) }
            }
        };
        var t = new TranslateTransform();
        r.RenderTransform = t;
        if (Ornate.Animate)
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-240, 240, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        return r;
    }

    /// <summary>Kuşatma: rüzgârda yatay savrulan kar.</summary>
    private static FrameworkElement Snow()
    {
        var c = new Canvas { IsHitTestVisible = false, Width = W, Height = H };
        var rnd = new Random(11);
        for (var i = 0; i < 140; i++)
        {
            var size = 2 + rnd.NextDouble() * 3.5;
            var e = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.45 + rnd.NextDouble() * 0.5 };
            var x = rnd.NextDouble() * W;
            var y0 = rnd.NextDouble() * H;
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y0);
            c.Children.Add(e);
            if (!Ornate.Animate) continue;
            var dur = TimeSpan.FromSeconds(5 + rnd.NextDouble() * 6);
            var t = new TranslateTransform();
            e.RenderTransform = t;
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-y0 - 20, H - y0 + 20, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * dur.TotalSeconds) });
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -260 - rnd.NextDouble() * 200, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * dur.TotalSeconds) });
        }
        return c;
    }

    /// <summary>
    /// Binaya yakınlaşır (null: genel görünüm). Binanın yakın çekimi (km/shots/&lt;kimlik&gt;) varsa ona geçilir; yoksa ana görüntü o noktaya
    /// büyütülüp ekranın ortasına (soldaki panelin yanına) kaydırılır. Diğer noktalar söner.
    /// </summary>
    public void Focus(string? building)
    {
        _focus = building;
        var dur = TimeSpan.FromMilliseconds(Ornate.Animate ? 900 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        void To(DependencyObject target, DependencyProperty p, double v)
        {
            // Animasyonsuz kipte (sınama/ekran görüntüsü) zaman akmaz: değer doğrudan atanır
            var anim = Ornate.Animate ? new DoubleAnimation(v, dur) { EasingFunction = ease } : null;
            if (target is Animatable a) a.BeginAnimation(p, anim);
            else if (target is UIElement u) u.BeginAnimation(p, anim);
            if (anim == null) target.SetValue(p, v);
        }

        var shot = building == null ? null : Assets.Ui("km/shots/" + building);
        if (shot != null) _closeUp.Source = shot;
        To(_closeUp, OpacityProperty, shot != null ? 1 : 0);

        if (building != null && _spots.TryGetValue(building, out var spot) && shot == null)
        {
            var at = new Point(Canvas.GetLeft(spot) + 130, Canvas.GetTop(spot) + 17);
            const double z = 1.45;   // menü panoraması küçük: daha fazlası bulanıklaşır (oyuncunun çekimleri gelince yakın çekime geçilir)
            To(_zoom, ScaleTransform.ScaleXProperty, z);
            To(_zoom, ScaleTransform.ScaleYProperty, z);
            // ölçek merkezi ekranın ortası: noktayı ekranın ortasına (biraz sağa, sol panel yüzünden) taşı
            To(_pan, TranslateTransform.XProperty, (W * 0.55 - (W / 2 + (at.X - W / 2) * z)));
            To(_pan, TranslateTransform.YProperty, (H * 0.5 - (H / 2 + (at.Y - H / 2) * z)));
        }
        else if (building == null)
        {
            To(_zoom, ScaleTransform.ScaleXProperty, 1.06);
            To(_zoom, ScaleTransform.ScaleYProperty, 1.06);
            To(_pan, TranslateTransform.XProperty, 0);
            To(_pan, TranslateTransform.YProperty, 0);
        }
        foreach (var (id, el) in _spots) To(el, OpacityProperty, building == null || id == building ? 1 : 0.15);
    }
}
