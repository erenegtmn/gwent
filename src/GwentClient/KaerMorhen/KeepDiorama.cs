using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using KaerMorhen.Core;

namespace GwentClient.KaerMorhen;

/// <summary>
/// Kaer Morhen maketi: savaş masasında, bölge haritası zemininde, oyunun gerçek dokularıyla (kumtaşı sur, kaldırım, arduvaz çatı;
/// tools/w3_keep_textures.py) basit 3D parçalardan kale. Binalar seviyeye göre değişir: harabe (alçak, çatısız, moloz) → onarılmış
/// (çatı) → yükseldikçe daha yüksek, daha çok yanan pencere. Sinematik: kamera yavaşça salınır; binanın üstüne gelince parlar,
/// tıklayınca kamera yaklaşır (<see cref="BuildingClicked"/>). Kuşatmada ışık soğur, gök kızarır. Doku yoksa düz renklerle çizilir.
/// </summary>
public sealed class KeepDiorama : Grid
{
    private readonly KeepSave _s;
    private readonly Model3DGroup _scene = new();
    private readonly Model3DGroup _world = new();
    private readonly Viewport3D _view = new();
    private readonly PerspectiveCamera _camera;
    private readonly Canvas _labels = new() { IsHitTestVisible = false };
    private readonly List<(string Text, Point3D At, Brush Color, string? Id)> _tags = new();
    private readonly Dictionary<GeometryModel3D, string> _owner = new();
    private readonly Dictionary<string, List<GeometryModel3D>> _parts = new();
    private readonly AxisAngleRotation3D _orbit = new(new Vector3D(0, 1, 0), 0);
    private readonly ModelVisual3D _visual;
    private string? _hover;
    private bool _labelsLive;
    private const double G = 1.0;   // tepenin üstü

    // Kale ekranında kale, soldaki bina listesi ile sağdaki pano arasında kalsın diye geriden bakılır
    private static readonly Point3D HomePos = new(0, 25, 33.5);
    private static readonly Vector3D HomeDir = new(0, -24.4, -35);

    /// <summary>Bir binaya tıklandı (kimliği).</summary>
    public event Action<string>? BuildingClicked;

    public KeepDiorama(KeepSave s, bool animate = true)
    {
        _s = s;
        ClipToBounds = true;
        var siege = Keep.ContractsLocked(s);
        Background = new LinearGradientBrush(siege ? Color.FromRgb(0x2a, 0x0c, 0x0c) : Color.FromRgb(0x14, 0x18, 0x20), Color.FromRgb(0x05, 0x05, 0x06), 90);
        if (Assets.Ui("pano_kaer_morhen") is { } pano)
            Children.Add(new Image { Source = pano, Stretch = Stretch.UniformToFill, Opacity = siege ? 0.45 : 0.8, VerticalAlignment = VerticalAlignment.Top });

        Build();
        _world.Transform = new RotateTransform3D(_orbit);
        _camera = new PerspectiveCamera(HomePos, HomeDir, new Vector3D(0, 1, 0), 50);
        _view.Camera = _camera;
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(siege ? Color.FromRgb(0x40, 0x40, 0x48) : Color.FromRgb(0x50, 0x4c, 0x50)));
        lights.Children.Add(new DirectionalLight(siege ? Color.FromRgb(0xc4, 0xc8, 0xd8) : Color.FromRgb(0xff, 0xe2, 0xc0), new Vector3D(-0.6, -1, -0.5)));
        lights.Children.Add(new DirectionalLight(siege ? Color.FromRgb(0x58, 0x1c, 0x14) : Color.FromRgb(0x40, 0x50, 0x70), new Vector3D(0.7, -0.4, 0.6)));
        _scene.Children.Add(lights);
        _scene.Children.Add(_world);
        _visual = new ModelVisual3D { Content = _scene };
        _view.Children.Add(_visual);
        Children.Add(_view);
        Children.Add(_labels);
        // Alt kenar sisi (sinema)
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0, 0, 0), 0.6), new(Color.FromArgb(0xc8, 0x04, 0x04, 0x05), 1) }, 90)
        });

        _view.MouseMove += OnMove;
        _view.MouseLeave += (_, _) => SetHover(null);
        _view.MouseLeftButtonUp += (_, e) => { if (Pick(e.GetPosition(_view)) is { } id) BuildingClicked?.Invoke(id); };
        SizeChanged += (_, _) => PlaceLabels();
        Loaded += (_, _) =>
        {
            PlaceLabels();
            if (!animate || !Ornate.Animate) return;
            // Kamera yavaşça salınır (maketin etrafında küçük bir yay); etiketler her karede izdüşüme taşınır
            _orbit.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(-9, 9, TimeSpan.FromSeconds(28))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _labelsLive = true;
            CompositionTarget.Rendering += OnFrame;
        };
        Unloaded += (_, _) => { if (_labelsLive) CompositionTarget.Rendering -= OnFrame; _labelsLive = false; };
    }

    private void OnFrame(object? sender, EventArgs e) => PlaceLabels();

    /// <summary>Kamerayı bir binaya yaklaştırır (null: genel görünüm). Salınım durur, geri dönünce sürer.</summary>
    public void Focus(string? building)
    {
        var dur = TimeSpan.FromMilliseconds(Ornate.Animate ? 750 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        Point3D pos;
        Vector3D dir;
        if (building != null && _parts.TryGetValue(building, out var models) && models.Count > 0)
        {
            var b = models.Select(m => m.Bounds).Aggregate(Rect3D.Union);
            var c = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            c = _world.Transform.Transform(c);
            pos = new Point3D(c.X + 2.5, c.Y + 11, c.Z + 15.5);
            dir = c - pos;
        }
        else { pos = HomePos; dir = HomeDir; }
        _camera.BeginAnimation(ProjectionCamera.PositionProperty, new Point3DAnimation(pos, dur) { EasingFunction = ease });
        _camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, new Vector3DAnimation(dir, dur) { EasingFunction = ease });
        if (!Ornate.Animate) { _camera.Position = pos; _camera.LookDirection = dir; }
        SetHover(building);
    }

    // ---------- Fare ----------

    private string? Pick(Point p)
    {
        string? found = null;
        VisualTreeHelper.HitTest(_view, null, r =>
        {
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D gm && _owner.TryGetValue(gm, out var id)) { found = id; return HitTestResultBehavior.Stop; }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        return found;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var id = Pick(e.GetPosition(_view));
        SetHover(id);
        _view.Cursor = id != null ? Cursors.Hand : Cursors.Arrow;
    }

    private void SetHover(string? id)
    {
        if (id == _hover) return;
        if (_hover != null && _parts.TryGetValue(_hover, out var old)) foreach (var m in old) m.Material = (Material)m.GetValue(BaseMaterialProperty);
        _hover = id;
        if (id != null && _parts.TryGetValue(id, out var now))
            foreach (var m in now) m.Material = new MaterialGroup { Children = { (Material)m.GetValue(BaseMaterialProperty), new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x50, 0xe8, 0xc9, 0x70))) } };
        PlaceLabels();
    }

    private static readonly DependencyProperty BaseMaterialProperty = DependencyProperty.RegisterAttached("BaseMaterial", typeof(Material), typeof(KeepDiorama));

    // ---------- Malzemeler ----------

    private static readonly Dictionary<string, Material> Cache = new();

    /// <summary>Doku (dünya biriminde <paramref name="tile"/> boyunda tekrar eder); yoksa düz renk. <paramref name="tint"/> karartır/renklendirir.</summary>
    private static Material Tex(string name, string fallback, Color? tint = null)
    {
        var key = name + tint;
        if (Cache.TryGetValue(key, out var m)) return m;
        var g = new MaterialGroup();
        if (Assets.Ui("km/" + name) is { } src)
        {
            var brush = new ImageBrush(src) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            brush.Freeze();
            g.Children.Add(new DiffuseMaterial(brush) { Color = tint ?? Colors.White });
        }
        else g.Children.Add(new DiffuseMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback))) { Color = tint ?? Colors.White });
        g.Freeze();
        return Cache[key] = g;
    }

    private static Material Wall => Tex("wall", "#a39a8c");
    private static Material WallTop => Tex("wall_top", "#a39a8c");
    private static Material OldStone => Tex("old_stone", "#9a948a");
    private static Material Ruin => Tex("wall", "#5c564e", Color.FromRgb(0x80, 0x78, 0x70));
    private static Material Roof => Tex("roof", "#4a4a52");
    private static Material RoofMoss => Tex("roof_moss", "#3a4a3e");
    private static Material Wood => Tex("wood", "#6b4a2f");
    private static Material Rock => Tex("rock", "#6f6a62");
    private static Material Snow => Tex("snow", "#e8ecf0");
    private static Material Cobble => Tex("cobble", "#7d756a");
    private static Material Door => Tex("door", "#4a3420");

    private static Material Solid(string hex) => new DiffuseMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    private static Material Glow(string hex) => new MaterialGroup
    {
        Children = { new DiffuseMaterial(Brushes.Black), new EmissiveMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))) }
    };

    // ---------- Sahne ----------

    private string? _current;   // eklenen parçalar bu binaya ait (tıklama/parlama)

    private void Build()
    {
        // Savaş masası ve harita
        Box(-16, -1.2, -11, 32, 1.2, 22, Wood, 4);
        if (Assets.Ui("map_kaer_morhen") is { } map)
        {
            var brush = new ImageBrush(map) { Viewbox = new Rect(0.30, 0.22, 0.40, 0.36), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            Quad(new Point3D(-15, 0.01, -10), new Point3D(15, 0.01, -10), new Point3D(15, 0.01, 10), new Point3D(-15, 0.01, 10), new DiffuseMaterial(brush), 1);
        }

        // Kayalık tepe, avlu ve dağlar
        Box(-9.2, 0, -6.8, 17.6, G, 13.2, Rock, 5);
        Quad(new Point3D(-8.5, G + 0.01, -6), new Point3D(7.5, G + 0.01, -6), new Point3D(7.5, G + 0.01, 5.5), new Point3D(-8.5, G + 0.01, 5.5), Cobble, 3);
        var rnd = new Random(7);
        foreach (var (x, z, r, h) in new[] { (-13.0, -8.8, 4.2, 6.8), (-7.0, -9.8, 4.8, 8.8), (-0.5, -10.2, 4.0, 6.6), (6.5, -9.6, 5.2, 9.6), (13.0, -8.4, 4.0, 6.2) })
            Mountain(x, z, r, h, rnd);

        // Surlar, kapı ve köşe kuleleri
        Rampart(-8.5, -6, 7.5, -6);
        Rampart(-8.5, -6, -8.5, 5.5);
        Rampart(7.5, -6, 7.5, 5.5);
        Rampart(-8.5, 5.5, -1.4, 5.5);
        Rampart(1.4, 5.5, 7.5, 5.5);
        Box(-1.4, G, 5.25, 2.8, 2.1, 0.5, Wall, 3.5);
        Quad(new Point3D(-0.9, G, 5.76), new Point3D(0.9, G, 5.76), new Point3D(0.9, G + 1.8, 5.76), new Point3D(-0.9, G + 1.8, 5.76), Door, 0, uvFull: true);
        foreach (var (x, z) in new[] { (-8.5, -6.0), (7.5, -6.0), (-8.5, 5.5), (7.5, 5.5), (-1.7, 5.5), (1.7, 5.5) })
        {
            Cylinder(x, z, G, 0.8, 2.8, Wall, 10, 3.5);
            Cone(x, z, G + 2.8, 0.95, 1.1, Roof, 10, 2);
        }

        // Büyük kule (Kaer Morhen'in simgesi)
        Cylinder(-6, -4, G, 1.3, 7.8, OldStone, 14, 5);
        Cone(-6, -4, G + 7.8, 1.6, 2.6, RoofMoss, 14, 2);
        Tag("Kaer Morhen", new Point3D(-6, G + 11, -4), Theme.B("#e8c970"), null);

        Building(Keep.WarRoom, -2.8, -3.6, 4.2, 3.0, 3.2);
        Building(Keep.AlliesHall, 3.4, -3.8, 3.4, 2.6, 2.4);
        Building(Keep.Forge, -5.6, 1.4, 2.4, 2.2, 1.6, chimney: true, glow: "#ff8a30");
        Building(Keep.Barracks, -1.2, 2.9, 4.4, 1.6, 1.5);
        Building(Keep.Workshop, 4.6, 0.8, 2.6, 2.2, 1.6, crane: true);
        Tower(Keep.Alchemy, 1.6, -0.2);

        // Kuşatma: Wild Hunt'ın kızıl ışığı surların önünde
        if (Keep.ContractsLocked(_s))
            _world.Children.Add(new PointLight(Color.FromRgb(0x90, 0x2a, 0x18), new Point3D(0, G + 2, 9)) { Range = 9, LinearAttenuation = 0.15 });
    }

    /// <summary>Seviyeye göre bina: 0 = harabe; arttıkça yükselir, çatılanır, pencereler yanar.</summary>
    private void Building(string id, double x, double z, double w, double d, double h, bool chimney = false, bool crane = false, string glow = "#ffb050")
    {
        _current = id;
        var lvl = _s.Level(id);
        var b = Keep.Get(id);
        if (lvl == 0)
        {
            Box(x - w / 2, G, z - d / 2, w, h * 0.45, d, Ruin, 3.5);
            Box(x - w / 2 + 0.3, G + h * 0.45, z - d / 2, w * 0.35, h * 0.3, 0.3, Ruin, 3.5);   // yıkık duvar parçası
            for (var i = 0; i < 4; i++) Box(x - w / 2 - 0.4 + i * w / 3.2, G, z + d / 2 + 0.15 + (i % 2) * 0.35, 0.45, 0.3, 0.4, Ruin, 1);
            Tag(b.Name + " · harabe", new Point3D(x, G + h * 0.75 + 0.6, z), Theme.B("#d8cfbc"), id);
            _current = null;
            return;
        }
        var hh = h * (0.75 + 0.15 * lvl);
        Box(x - w / 2, G, z - d / 2, w, hh, d, Wall, 3.5);
        Gable(x, z, w + 0.3, d + 0.3, G + hh, 1.2 + 0.2 * lvl, Roof);
        for (var i = 0; i < 1 + lvl; i++)
        {
            var wx = x - w / 2 + (i + 0.6) * w / (1.5 + lvl);
            Box(wx, G + hh * 0.45, z + d / 2 + 0.01, 0.28, 0.5, 0.04, Glow(glow), 0);
        }
        if (chimney)
        {
            Box(x + w / 2 - 0.6, G + hh, z - 0.3, 0.45, 1.6 + 0.3 * lvl, 0.45, OldStone, 1);
            _world.Children.Add(new PointLight(Color.FromRgb(0xff, 0x80, 0x30), new Point3D(x, G + hh + 1, z + d)) { Range = 5 + lvl });
        }
        if (crane) { Box(x + w / 2, G, z, 0.2, 3.2 + 0.4 * lvl, 0.2, Wood, 1); Box(x + w / 2 - 1.4, G + 3.0 + 0.4 * lvl, z - 0.1, 1.8, 0.16, 0.16, Wood, 1); }
        if (id == Keep.AlliesHall) for (var i = 0; i < lvl; i++) Banner(x - w / 2 + 0.5 + i * 1.1, z + d / 2 + 0.05, G + hh - 0.2);
        Tag($"{b.Name} {new string('●', lvl)}{new string('○', b.MaxLevel - lvl)}", new Point3D(x, G + hh + 2.2 + 0.2 * lvl, z), Theme.B("#e8c970"), id);
        _current = null;
    }

    private void Tower(string id, double x, double z)
    {
        _current = id;
        var lvl = _s.Level(id);
        var b = Keep.Get(id);
        if (lvl == 0)
        {
            Cylinder(x, z, G, 0.9, 1.3, Ruin, 10, 2);
            Box(x - 0.9, G, z + 1.0, 0.5, 0.3, 0.4, Ruin, 1);
            Tag(b.Name + " · harabe", new Point3D(x, G + 2.2, z), Theme.B("#d8cfbc"), id);
            _current = null;
            return;
        }
        var h = 2.4 + 0.9 * lvl;
        Cylinder(x, z, G, 0.9, h, OldStone, 12, 2);
        Cone(x, z, G + h, 1.15, 1.6, RoofMoss, 12, 2);
        for (var i = 0; i < lvl; i++) Box(x - 0.14, G + 0.9 + i * 0.9, z + 0.9, 0.28, 0.45, 0.04, Glow("#7ae070"), 0);
        Tag($"{b.Name} {new string('●', lvl)}{new string('○', b.MaxLevel - lvl)}", new Point3D(x, G + h + 2.2, z), Theme.B("#e8c970"), id);
        _current = null;
    }

    private void Rampart(double x1, double z1, double x2, double z2)
    {
        const double h = 1.6, t = 0.55;
        var horizontal = Math.Abs(z1 - z2) < 0.01;
        var len = horizontal ? Math.Abs(x2 - x1) : Math.Abs(z2 - z1);
        var (x0, z0) = (Math.Min(x1, x2), Math.Min(z1, z2));
        if (horizontal) Box(x0, G, z0 - t / 2, len, h, t, Wall, 3.5); else Box(x0 - t / 2, G, z0, t, h, len, Wall, 3.5);
        for (double p = 0.2; p < len - 0.2; p += 0.7)
            if (horizontal) Box(x0 + p, G + h, z0 - t / 2, 0.35, 0.35, t, WallTop, 1); else Box(x0 - t / 2, G + h, z0 + p, t, 0.35, 0.35, WallTop, 1);
    }

    private void Banner(double x, double z, double top)
    {
        Box(x, top - 1.4, z, 0.6, 1.4, 0.03, Solid("#8a1e1e"), 0);
        Box(x + 0.22, top - 0.95, z + 0.035, 0.16, 0.5, 0.01, Solid("#e8c970"), 0);
    }

    /// <summary>Pürüzlü dağ: halka halka daralan, köşeleri rastgele oynayan kaya kütlesi; üst halkalar karlı.</summary>
    private void Mountain(double cx, double cz, double r, double h, Random rnd)
    {
        const int n = 16;
        double[] levels = { 0, 0.3, 0.55, 0.78, 0.92 };
        var rings = new List<Point3D[]>();
        foreach (var t in levels)
        {
            var ring = new Point3D[n];
            var shrink = Math.Pow(1 - t, 0.85);
            for (var i = 0; i < n; i++)
            {
                var a = 2 * Math.PI * (i + (rnd.NextDouble() - 0.5) * 0.5) / n;
                var rr = r * shrink * (0.72 + rnd.NextDouble() * 0.5);
                ring[i] = new Point3D(cx + rr * Math.Cos(a), h * t * (0.92 + rnd.NextDouble() * 0.16), cz + rr * Math.Sin(a) * 0.8);
            }
            rings.Add(ring);
        }
        var apex = new Point3D(cx + (rnd.NextDouble() - 0.5) * r * 0.3, h, cz + (rnd.NextDouble() - 0.5) * r * 0.2);
        var rock = new MeshGeometry3D();
        var snow = new MeshGeometry3D();
        for (var k = 0; k < rings.Count - 1; k++)
        {
            var target = k >= 2 ? snow : rock;
            for (var i = 0; i < n; i++)
            {
                var (b0, b1, t0, t1) = (rings[k][i], rings[k][(i + 1) % n], rings[k + 1][i], rings[k + 1][(i + 1) % n]);
                AddTri(target, b0, t0, b1, 7); AddTri(target, b1, t0, t1, 7);
            }
        }
        var top = rings[^1];
        for (var i = 0; i < n; i++) AddTri(snow, top[i], apex, top[(i + 1) % n], 5);
        Add(rock, Rock);
        Add(snow, Snow);
    }

    private static Point3D Lerp(Point3D a, Point3D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private void Tag(string text, Point3D at, Brush color, string? id) => _tags.Add((text, at, color, id));

    /// <summary>Etiketler 3D noktanın ekrandaki izdüşümüne yerleşir (salınırken her karede).</summary>
    private void PlaceLabels()
    {
        _labels.Children.Clear();
        if (_visual.TransformToAncestor(_view) is not { } t) return;
        foreach (var (text, at, color, id) in _tags)
        {
            if (!t.TryTransform(_world.Transform.Transform(at), out var p)) continue;
            var hot = id != null && id == _hover;
            var tb = new TextBlock
            {
                Text = text, Foreground = hot ? Brushes.White : color, FontFamily = Theme.Serif, FontSize = hot ? 21 : 17, FontWeight = FontWeights.SemiBold,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 7, Opacity = 1 }
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, p.X - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, p.Y - tb.DesiredSize.Height);
            _labels.Children.Add(tb);
        }
    }

    // ---------- Geometri (dokular dünya biriminde tekrar eder: uv = konum / tile) ----------

    private void Add(MeshGeometry3D mesh, Material m)
    {
        var gm = new GeometryModel3D(mesh, m) { BackMaterial = m };
        gm.SetValue(BaseMaterialProperty, m);
        if (_current != null)
        {
            _owner[gm] = _current;
            if (!_parts.TryGetValue(_current, out var list)) _parts[_current] = list = new List<GeometryModel3D>();
            list.Add(gm);
        }
        _world.Children.Add(gm);
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3, double tile)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(p0); mesh.Positions.Add(p1); mesh.Positions.Add(p2); mesh.Positions.Add(p3);
        if (tile > 0)
        {
            var u = (p1 - p0).Length / tile;
            var v = (p3 - p0).Length / tile;
            mesh.TextureCoordinates.Add(new Point(0, v)); mesh.TextureCoordinates.Add(new Point(u, v));
            mesh.TextureCoordinates.Add(new Point(u, 0)); mesh.TextureCoordinates.Add(new Point(0, 0));
        }
        else
        {
            mesh.TextureCoordinates.Add(new Point(0, 1)); mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0)); mesh.TextureCoordinates.Add(new Point(0, 0));
        }
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
    }

    private static void AddTri(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, double tile)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c);
        mesh.TextureCoordinates.Add(new Point((a.X + a.Z) / tile, -a.Y / tile));
        mesh.TextureCoordinates.Add(new Point((b.X + b.Z) / tile, -b.Y / tile));
        mesh.TextureCoordinates.Add(new Point((c.X + c.Z) / tile, -c.Y / tile));
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
    }

    private void Quad(Point3D a, Point3D b, Point3D c, Point3D d, Material m, double tile, bool uvFull = false)
    {
        var mesh = new MeshGeometry3D();
        if (uvFull) AddQuad(mesh, a, b, c, d, 0);
        else if (tile <= 1 && m is DiffuseMaterial)
        {
            mesh.Positions = new Point3DCollection { a, b, c, d };
            mesh.TriangleIndices = new Int32Collection { 0, 2, 1, 0, 3, 2 };
            mesh.TextureCoordinates = new PointCollection { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        }
        else AddQuad(mesh, a, b, c, d, tile);
        Add(mesh, m);
    }

    private void Box(double x, double y, double z, double w, double h, double d, Material m, double tile)
    {
        var mesh = new MeshGeometry3D();
        var (x2, y2, z2) = (x + w, y + h, z + d);
        AddQuad(mesh, new(x, y, z2), new(x2, y, z2), new(x2, y2, z2), new(x, y2, z2), tile);   // ön
        AddQuad(mesh, new(x2, y, z), new(x, y, z), new(x, y2, z), new(x2, y2, z), tile);       // arka
        AddQuad(mesh, new(x, y, z), new(x, y, z2), new(x, y2, z2), new(x, y2, z), tile);       // sol
        AddQuad(mesh, new(x2, y, z2), new(x2, y, z), new(x2, y2, z), new(x2, y2, z2), tile);   // sağ
        AddQuad(mesh, new(x, y2, z2), new(x2, y2, z2), new(x2, y2, z), new(x, y2, z), tile);   // üst
        AddQuad(mesh, new(x, y, z), new(x2, y, z), new(x2, y, z2), new(x, y, z2), tile);       // alt
        Add(mesh, m);
    }

    /// <summary>Beşik çatı: iki eğik yüz (dokulu) ve iki üçgen alın.</summary>
    private void Gable(double cx, double cz, double w, double d, double y, double h, Material m)
    {
        var (x1, x2, z1, z2) = (cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2);
        var mesh = new MeshGeometry3D();
        AddQuad(mesh, new(x1, y, z2), new(x2, y, z2), new(x2, y + h, cz), new(x1, y + h, cz), 2.5);
        AddQuad(mesh, new(x2, y, z1), new(x1, y, z1), new(x1, y + h, cz), new(x2, y + h, cz), 2.5);
        Add(mesh, m);
        var gab = new MeshGeometry3D();
        AddTri(gab, new(x1, y, z1), new(x1, y, z2), new(x1, y + h, cz), 2);
        AddTri(gab, new(x2, y, z2), new(x2, y, z1), new(x2, y + h, cz), 2);
        Add(gab, Wall);
    }

    private void Cylinder(double cx, double cz, double y, double r, double h, Material m, int n, double tile)
    {
        var mesh = new MeshGeometry3D();
        var circ = 2 * Math.PI * r;
        for (var i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var i0 = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a0), y, cz + r * Math.Sin(a0)));
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a1), y, cz + r * Math.Sin(a1)));
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a1), y + h, cz + r * Math.Sin(a1)));
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a0), y + h, cz + r * Math.Sin(a0)));
            double u0 = circ * i / n / tile, u1 = circ * (i + 1) / n / tile, v = h / tile;
            mesh.TextureCoordinates.Add(new Point(u0, v)); mesh.TextureCoordinates.Add(new Point(u1, v));
            mesh.TextureCoordinates.Add(new Point(u1, 0)); mesh.TextureCoordinates.Add(new Point(u0, 0));
            foreach (var j in new[] { 0, 3, 2, 0, 2, 1 }) mesh.TriangleIndices.Add(i0 + j);
            AddTri(mesh, new Point3D(cx, y + h, cz), mesh.Positions[i0 + 3], mesh.Positions[i0 + 2], tile);
        }
        Add(mesh, m);
    }

    private void Cone(double cx, double cz, double y, double r, double h, Material m, int n, double tile)
    {
        var mesh = new MeshGeometry3D();
        var apex = new Point3D(cx, y + h, cz);
        for (var i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            AddTri(mesh, new Point3D(cx + r * Math.Cos(a0), y, cz + r * Math.Sin(a0)), apex, new Point3D(cx + r * Math.Cos(a1), y, cz + r * Math.Sin(a1)), tile);
        }
        Add(mesh, m);
    }
}
