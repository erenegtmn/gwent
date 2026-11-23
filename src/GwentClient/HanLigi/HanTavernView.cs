using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace GwentClient.HanLigi;

/// <summary>
/// Canlı han görüntüsü (okul seçimi ve açılış): <see cref="HanTavern"/> odası arkada bulanık (alan derinliği), masa ve tahta
/// önde net; iki görünüm aynı kamerayla hareket eder. Ocağın ve duvar mumlarının ışıltısı titrer, ocaktan kıvılcımlar yükselir
/// (2B katman; noktalar her karede 3B yerlerinden izdüşürülür, kamera kayınca doğru yerde kalır). <see cref="FlyTo"/> kamerayı
/// oyundaki masa görüşüne indirir; son kare <see cref="HanScene"/> ile aynıdır.
/// </summary>
public sealed class HanTavernView : Grid
{
    public readonly record struct Pose(Point3D Pos, Vector3D Look, Vector3D Up, double Fov)
    {
        public static Pose Of(PerspectiveCamera c)
        {
            var l = c.LookDirection; l.Normalize();
            var u = c.UpDirection; u.Normalize();
            return new Pose(c.Position, l, u, c.FieldOfView);
        }

        public static Pose Lerp(Pose a, Pose b, double t)
        {
            var l = a.Look + (b.Look - a.Look) * t; l.Normalize();
            var u = a.Up + (b.Up - a.Up) * t; u.Normalize();
            return new Pose(a.Pos + (b.Pos - a.Pos) * t, l, u, a.Fov + (b.Fov - a.Fov) * t);
        }
    }

    /// <summary>Geniş çekim: kapıdan içeri bakan oturur göz yüksekliği; solda ocak, ortada masamız.</summary>
    public static Pose Wide => Pose.Of(HanTavern.Camera(new Point3D(-12, -42, 9), new Point3D(3, 16, -2), 58));

    private const double W = 1920, H = 1080, WideBlur = 5;
    private readonly PerspectiveCamera _roomCam = new(), _tableCam = new(), _propsCam = new();
    private readonly Viewport3D _props;
    private readonly BlurEffect _blur = new() { Radius = WideBlur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private readonly Canvas _fx = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rnd = new(3);

    private sealed class Glow
    {
        public required Point3D At;
        public required FrameworkElement El;
        public required ScaleTransform Scale;
        public required double Size, Base, Phase, Speed;
        public bool Prop;
    }

    private sealed class Ember
    {
        public Point3D At;
        public Vector3D V;
        public double Life, Age;
        public required Ellipse El;
    }

    private readonly List<Glow> _glows = new();
    private readonly List<Ember> _embers = new();
    private Pose _pose = Wide, _from, _to;
    private double _flyStart, _flyMs;
    private Action? _flyDone;
    private bool _flying, _hooked;
    private double _last;

    public HanTavernView(ImageSource board)
    {
        Width = W;
        Height = H;
        Background = new SolidColorBrush(Color.FromRgb(4, 3, 2));
        IsHitTestVisible = true;   // uçarken altındaki ekrana tıklanmasın
        var room = new Viewport3D { Width = W, Height = H, Camera = _roomCam, IsHitTestVisible = false, Effect = _blur };
        room.Children.Add(new ModelVisual3D { Content = new Model3DGroup { Children = { HanTavern.Room(), HanTavern.Lights() } } });
        var table = new Viewport3D { Width = W, Height = H, Camera = _tableCam, IsHitTestVisible = false };
        table.Children.Add(new ModelVisual3D { Content = new Model3DGroup { Children = { HanTavern.Table(), HanTavern.Board(board), HanTavern.Lights() } } });
        // masadaki nesneler ayrı katmanda: iniş yaklaşınca sönerler (oyundaki yakın görüşte yoklar)
        _props = new Viewport3D { Width = W, Height = H, Camera = _propsCam, IsHitTestVisible = false };
        _props.Children.Add(new ModelVisual3D { Content = new Model3DGroup { Children = { HanTavern.Props(), HanTavern.Lights() } } });
        Children.Add(room);
        Children.Add(table);
        Children.Add(_props);
        Children.Add(_fx);
        // kenar karartması
        Children.Add(new Rectangle { IsHitTestVisible = false, Fill = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xb8, 0, 0, 0)) { RadiusX = 0.78, RadiusY = 0.82, Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.55) } });

        // ışıltılar: ocak (büyük, iki katman) ve duvar mumları
        AddGlow(HanTavern.Hearth + new Vector3D(0, -1.5, 0), 900, 0.5, Color.FromRgb(0xff, 0x8a, 0x34));
        AddGlow(HanTavern.Hearth + new Vector3D(0, -1, -1), 380, 0.55, Color.FromRgb(0xff, 0xc2, 0x6a));
        foreach (var s in HanTavern.Sconces) AddGlow(s + new Vector3D(0, 0, 0.8), 200, 0.42, Color.FromRgb(0xff, 0xb0, 0x5a));
        AddGlow(HanTavern.CandleFlame + new Vector3D(0, 0, 0.35), 70, 0.55, Color.FromRgb(0xff, 0xc4, 0x70), prop: true);
        for (var i = 0; i < 26; i++) _embers.Add(NewEmber(true));
        Apply(_pose, 1);
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
    }

    private void Hook(bool on)
    {
        if (on == _hooked) return;
        _hooked = on;
        if (on) CompositionTarget.Rendering += Tick; else CompositionTarget.Rendering -= Tick;
    }

    private void AddGlow(Point3D at, double size, double baseOpacity, Color c, bool prop = false)
    {
        var e = new Ellipse
        {
            Width = size, Height = size, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(new GradientStopCollection { new(Color.FromArgb(0xff, c.R, c.G, c.B), 0), new(Color.FromArgb(0x60, c.R, c.G, c.B), 0.35), new(Color.FromArgb(0, c.R, c.G, c.B), 1) })
        };
        var sc = new ScaleTransform(1, 1, size / 2, size / 2);
        e.RenderTransform = sc;
        _fx.Children.Add(e);
        _glows.Add(new Glow { At = at, El = e, Scale = sc, Size = size, Base = baseOpacity, Phase = _rnd.NextDouble() * 10, Speed = 0.8 + _rnd.NextDouble() * 0.5, Prop = prop });
    }

    private Ember NewEmber(bool scatter)
    {
        var el = new Ellipse { Width = 6, Height = 6, IsHitTestVisible = false, Fill = new RadialGradientBrush(Color.FromRgb(0xff, 0xe0, 0x90), Color.FromArgb(0, 0xff, 0x70, 0x20)) };
        _fx.Children.Add(el);
        var e = new Ember { El = el };
        Respawn(e);
        if (scatter) e.Age = _rnd.NextDouble() * e.Life;
        return e;
    }

    private void Respawn(Ember e)
    {
        e.At = HanTavern.Hearth + new Vector3D((_rnd.NextDouble() - 0.5) * 8, -1 - _rnd.NextDouble() * 1.5, -2 + _rnd.NextDouble() * 2);
        e.V = new Vector3D((_rnd.NextDouble() - 0.5) * 2.2, -0.6 - _rnd.NextDouble() * 1.4, 3.5 + _rnd.NextDouble() * 4);
        e.Life = 1.1 + _rnd.NextDouble() * 1.6;
        e.Age = 0;
    }

    /// <summary>Kamerayı <paramref name="end"/>'e <paramref name="ms"/> içinde götürür (yavaş başlar, yavaşlayarak durur).</summary>
    public void FlyTo(PerspectiveCamera end, double ms, Action? done = null)
    {
        _from = _pose;
        _to = Pose.Of(end);
        _flyStart = _clock.Elapsed.TotalMilliseconds;
        _flyMs = ms;
        _flyDone = done;
        _flying = true;
    }

    private void Apply(Pose p, double blurK)
    {
        _pose = p;
        foreach (var c in new[] { _roomCam, _tableCam, _propsCam })
        {
            c.Position = p.Pos;
            c.LookDirection = p.Look;
            c.UpDirection = p.Up;
            c.FieldOfView = p.Fov;
        }
        _blur.Radius = WideBlur * blurK;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        var dt = Math.Min(0.05, (now - _last) / 1000);
        _last = now;
        if (_flying)
        {
            var t = Math.Clamp((now - _flyStart) / _flyMs, 0, 1);
            var k = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            Apply(Pose.Lerp(_from, _to, k), 1 - k);
            var fade = Math.Clamp((k - 0.4) / 0.3, 0, 1);
            _props.Opacity = 1 - fade * fade * (3 - 2 * fade);
            if (t >= 1)
            {
                _flying = false;
                var d = _flyDone;
                _flyDone = null;
                d?.Invoke();
            }
        }
        var cam = _roomCam;
        var sec = now / 1000;
        foreach (var g in _glows)
        {
            if (HanTavern.Project(cam, g.At, W, H) is not { } p) { g.El.Visibility = Visibility.Hidden; continue; }
            g.El.Visibility = Visibility.Visible;
            var dist = (g.At - cam.Position).Length;
            var s = Math.Clamp(40 / dist, 0.15, 3);
            var f = 0.8 + 0.12 * Math.Sin(sec * 7.1 * g.Speed + g.Phase) + 0.08 * Math.Sin(sec * 12.7 * g.Speed + g.Phase * 1.7) + 0.05 * Math.Sin(sec * 23.3 + g.Phase * 3.1);
            g.Scale.ScaleX = g.Scale.ScaleY = s * (0.94 + 0.06 * f);
            g.El.Opacity = g.Base * f * (g.Prop ? _props.Opacity : 1);
            Canvas.SetLeft(g.El, p.X - g.Size / 2);
            Canvas.SetTop(g.El, p.Y - g.Size / 2);
        }
        foreach (var m in _embers)
        {
            m.Age += dt;
            if (m.Age >= m.Life) Respawn(m);
            m.V += new Vector3D(Math.Sin(sec * 3 + m.Life * 9) * 1.6 * dt, 0, 0);
            m.At += m.V * dt;
            if (HanTavern.Project(cam, m.At, W, H) is not { } p) { m.El.Visibility = Visibility.Hidden; continue; }
            m.El.Visibility = Visibility.Visible;
            var dist = (m.At - cam.Position).Length;
            var size = Math.Clamp(180 / dist, 1.5, 9);
            m.El.Width = m.El.Height = size;
            var a = m.Age / m.Life;
            m.El.Opacity = (a < 0.15 ? a / 0.15 : 1 - (a - 0.15) / 0.85) * (0.7 + 0.3 * Math.Sin(sec * 20 + m.Life * 13));
            Canvas.SetLeft(m.El, p.X - size / 2);
            Canvas.SetTop(m.El, p.Y - size / 2);
        }
    }
}
