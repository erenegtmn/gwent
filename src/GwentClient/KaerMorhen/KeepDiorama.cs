using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using KaerMorhen.Core;

namespace GwentClient.KaerMorhen;

/// <summary>
/// Kaer Morhen maketi (DENEME): savaş masasının üstünde, bölge haritası zemininde basit 3D parçalardan kale.
/// Binalar seviyesine göre değişir: harabe (alçak, çatısız, moloz), onarılmış (çatı), yükseldikçe daha yüksek ve daha çok ışıklı pencere.
/// WPF Viewport3D: gölge ve son işlem yok; maket üslubu (TASARIM.md §5.3).
/// </summary>
public sealed class KeepDiorama : Grid
{
    private readonly KeepSave _s;
    private readonly Model3DGroup _scene = new();
    private readonly Viewport3D _view = new();
    private readonly Canvas _labels = new() { IsHitTestVisible = false };
    private readonly List<(string Text, Point3D At, Brush Color)> _tags = new();

    private static readonly Material Stone = Mat("#a39a8c", 18);
    private static readonly Material StoneDark = Mat("#7d756a", 8);
    private static readonly Material Rubble = Mat("#5c564e", 0);
    private static readonly Material Roof = Mat("#5b3a2c", 12);
    private static readonly Material RoofBlue = Mat("#3a4a5e", 12);
    private static readonly Material Wood = Mat("#6b4a2f", 0);
    private static readonly Material Rock = Mat("#6f6a62", 0);
    private static readonly Material Snow = Mat("#e8ecf0", 30);

    public KeepDiorama(KeepSave s)
    {
        _s = s;
        ClipToBounds = true;
        Background = new LinearGradientBrush(Color.FromRgb(0x10, 0x14, 0x1a), Color.FromRgb(0x05, 0x05, 0x06), 90);
        if (Assets.Ui("pano_kaer_morhen") is { } pano)
            Children.Add(new Image { Source = pano, Stretch = Stretch.UniformToFill, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Top });

        Build();
        _view.Camera = new PerspectiveCamera(new Point3D(0, 21, 27), new Vector3D(0, -20, -28.5), new Vector3D(0, 1, 0), 50);
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x48, 0x48, 0x50)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xff, 0xe2, 0xc0), new Vector3D(-0.6, -1, -0.5)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x40, 0x50, 0x70), new Vector3D(0.7, -0.4, 0.6)));
        _scene.Children.Insert(0, lights);
        var visual = new ModelVisual3D { Content = _scene };
        _view.Children.Add(visual);
        Children.Add(_view);
        Children.Add(_labels);
        // Alt kenar sisi
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0, 0, 0), 0.55), new(Color.FromArgb(0xd0, 0x04, 0x04, 0x05), 1) }, 90)
        });
        SizeChanged += (_, _) => PlaceLabels(visual);
        Loaded += (_, _) => PlaceLabels(visual);
    }

    private static Material Mat(string hex, double spec)
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))));
        if (spec > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x30, 0xff, 0xff, 0xff)), spec));
        return g;
    }

    private static Material Glow(string hex) => new MaterialGroup
    {
        Children = { new DiffuseMaterial(Brushes.Black), new EmissiveMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))) }
    };

    // ---------- Sahne ----------

    private void Build()
    {
        // Savaş masası ve harita zemini
        Box(-16, -1.2, -11, 32, 1.2, 22, Wood);
        if (Assets.Ui("map_kaer_morhen") is { } map)
        {
            var brush = new ImageBrush(map) { Viewbox = new Rect(0.30, 0.22, 0.40, 0.36), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            Quad(new Point3D(-15, 0.01, -10), new Point3D(15, 0.01, -10), new Point3D(15, 0.01, 10), new Point3D(-15, 0.01, 10), new DiffuseMaterial(brush));
        }

        // Kayalık tepe ve dağlar (arka)
        Box(-9, 0, -6.5, 17, 1.0, 12.5, Rock);
        foreach (var (x, z, r, h) in new[] { (-13.0, -8.5, 3.2, 7.5), (-7.5, -9.5, 3.8, 9.5), (-1.0, -9.8, 3.0, 7.0), (6.0, -9.2, 4.2, 10.5), (12.5, -8.0, 3.0, 6.5) })
        {
            Cone(x, z, 0, r, h, Rock, 7);
            Cone(x, z, h * 0.62, r * 0.38, h * 0.38, Snow, 7);
        }

        const double g = 1.0;   // tepenin üstü
        // Surlar ve mazgallar (dış çember), ön kapı
        Wall(-8.5, -6, 7.5, -6);
        Wall(-8.5, -6, -8.5, 5.5);
        Wall(7.5, -6, 7.5, 5.5);
        Wall(-8.5, 5.5, -1.4, 5.5);
        Wall(1.4, 5.5, 7.5, 5.5);
        foreach (var (x, z) in new[] { (-8.5, -6.0), (7.5, -6.0), (-8.5, 5.5), (7.5, 5.5), (-1.6, 5.5), (1.6, 5.5) })
        {
            Cylinder(x, z, g, 0.75, 2.6, Stone, 10);
            Cone(x, z, g + 2.6, 0.9, 1.0, RoofBlue, 10);
        }

        // Büyük kule (Kaer Morhen'in simgesi)
        Cylinder(-6, -4, g, 1.25, 7.5, Stone, 14);
        Cone(-6, -4, g + 7.5, 1.5, 2.4, RoofBlue, 14);
        Tag("Kaer Morhen", new Point3D(-6, g + 10.6, -4), Theme.B("#e8c970"));

        // Binalar
        Building(Keep.WarRoom, -2.8, -3.6, 4.2, 3.0, 3.2, Roof);
        Building(Keep.AlliesHall, 3.4, -3.8, 3.4, 2.6, 2.4, Roof);
        Building(Keep.Forge, -5.6, 1.4, 2.4, 2.2, 1.6, Roof, chimney: true, glow: "#ff8a30");
        Building(Keep.Barracks, -1.2, 2.9, 4.4, 1.6, 1.5, Roof);
        Building(Keep.Workshop, 4.6, 0.8, 2.6, 2.2, 1.6, Roof, crane: true);
        Tower(Keep.Alchemy, 1.6, -0.2);
    }

    /// <summary>Seviyeye göre bina: 0 = harabe (alçak, çatısız, moloz); arttıkça yükselir, çatılanır, pencereler yanar.</summary>
    private void Building(string id, double x, double z, double w, double d, double h, Material roof, bool chimney = false, bool crane = false, string glow = "#ffb050")
    {
        const double g = 1.0;
        var lvl = _s.Level(id);
        var b = Keep.Get(id);
        if (lvl == 0)
        {
            Box(x - w / 2, g, z - d / 2, w, h * 0.45, d, StoneDark);
            Box(x - w / 2 + 0.3, g + h * 0.45, z - d / 2, w * 0.35, h * 0.3, 0.3, StoneDark);   // yıkık duvar parçası
            for (var i = 0; i < 4; i++) Box(x - w / 2 - 0.4 + i * w / 3.2, g, z + d / 2 + 0.15 + (i % 2) * 0.35, 0.45, 0.3, 0.4, Rubble);
            Tag(b.Name + " · harabe", new Point3D(x, g + h * 0.75 + 0.6, z), Theme.TextDim);
            return;
        }
        var hh = h * (0.75 + 0.15 * lvl);
        Box(x - w / 2, g, z - d / 2, w, hh, d, Stone);
        Gable(x, z, w + 0.3, d + 0.3, g + hh, 1.2 + 0.2 * lvl, roof);
        // Pencereler: seviye başına bir sıra yanan pencere (ön yüz)
        for (var i = 0; i < 1 + lvl; i++)
        {
            var wx = x - w / 2 + (i + 0.6) * w / (1.5 + lvl);
            Box(wx, g + hh * 0.45, z + d / 2 + 0.01, 0.28, 0.5, 0.04, Glow(glow));
        }
        if (chimney) { Box(x + w / 2 - 0.6, g + hh, z - 0.3, 0.45, 1.6 + 0.3 * lvl, 0.45, StoneDark); _scene.Children.Add(new PointLight(Color.FromRgb(0xff, 0x80, 0x30), new Point3D(x, g + hh + 1, z + d)) { Range = 5 + lvl }); }
        if (crane) { Box(x + w / 2, g, z, 0.2, 3.2 + 0.4 * lvl, 0.2, Wood); Box(x + w / 2 - 1.4, g + 3.0 + 0.4 * lvl, z - 0.1, 1.8, 0.16, 0.16, Wood); }
        if (id == Keep.AlliesHall) for (var i = 0; i < lvl; i++) Banner(x - w / 2 + 0.5 + i * 1.1, z + d / 2 + 0.05, g + hh - 0.2);
        Tag($"{b.Name} {new string('●', lvl)}{new string('○', b.MaxLevel - lvl)}", new Point3D(x, g + hh + 2.2 + 0.2 * lvl, z), Theme.B("#e8c970"));
    }

    private void Tower(string id, double x, double z)
    {
        const double g = 1.0;
        var lvl = _s.Level(id);
        var b = Keep.Get(id);
        if (lvl == 0)
        {
            Cylinder(x, z, g, 0.9, 1.3, StoneDark, 10);
            Box(x - 0.9, g, z + 1.0, 0.5, 0.3, 0.4, Rubble);
            Tag(b.Name + " · harabe", new Point3D(x, g + 2.2, z), Theme.TextDim);
            return;
        }
        var h = 2.4 + 0.9 * lvl;
        Cylinder(x, z, g, 0.9, h, Stone, 12);
        Cone(x, z, g + h, 1.15, 1.6, RoofBlue, 12);
        for (var i = 0; i < lvl; i++) Box(x - 0.14, g + 0.9 + i * 0.9, z + 0.9, 0.28, 0.45, 0.04, Glow("#7ae070"));
        Tag($"{b.Name} {new string('●', lvl)}{new string('○', b.MaxLevel - lvl)}", new Point3D(x, g + h + 2.2, z), Theme.B("#e8c970"));
    }

    private void Wall(double x1, double z1, double x2, double z2)
    {
        const double g = 1.0, h = 1.5, t = 0.5;
        var horizontal = Math.Abs(z1 - z2) < 0.01;
        var len = horizontal ? Math.Abs(x2 - x1) : Math.Abs(z2 - z1);
        var (x0, z0) = (Math.Min(x1, x2), Math.Min(z1, z2));
        if (horizontal) Box(x0, g, z0 - t / 2, len, h, t, Stone); else Box(x0 - t / 2, g, z0, t, h, len, Stone);
        for (double p = 0.2; p < len - 0.2; p += 0.7)
            if (horizontal) Box(x0 + p, g + h, z0 - t / 2, 0.35, 0.35, t, Stone); else Box(x0 - t / 2, g + h, z0 + p, t, 0.35, 0.35, Stone);
    }

    private void Banner(double x, double z, double top)
    {
        Box(x, top - 1.4, z, 0.6, 1.4, 0.03, Mat("#8a1e1e", 0));
        Box(x + 0.22, top - 0.95, z + 0.035, 0.16, 0.5, 0.01, Mat("#e8c970", 0));
    }

    private void Tag(string text, Point3D at, Brush color) => _tags.Add((text, at, color));

    /// <summary>Etiketler 3D noktanın ekrandaki izdüşümüne yerleşir.</summary>
    private void PlaceLabels(Visual3D visual)
    {
        _labels.Children.Clear();
        if (visual.TransformToAncestor(_view) is not { } t) return;
        foreach (var (text, at, color) in _tags)
        {
            if (!t.TryTransform(at, out var p)) continue;
            var tb = new TextBlock
            {
                Text = text, Foreground = color, FontFamily = Theme.Serif, FontSize = 17, FontWeight = FontWeights.SemiBold,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 6, Opacity = 1 }
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, p.X - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, p.Y - tb.DesiredSize.Height);
            _labels.Children.Add(tb);
        }
    }

    // ---------- Basit geometri ----------

    private void Quad(Point3D a, Point3D b, Point3D c, Point3D d, Material m)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = { a, b, c, d },
            TriangleIndices = { 0, 2, 1, 0, 3, 2 },
            TextureCoordinates = { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) }
        };
        _scene.Children.Add(new GeometryModel3D(mesh, m));
    }

    private void Box(double x, double y, double z, double w, double h, double d, Material m)
    {
        var mesh = new MeshGeometry3D();
        void Face(Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            var i = mesh.Positions.Count;
            mesh.Positions.Add(p0); mesh.Positions.Add(p1); mesh.Positions.Add(p2); mesh.Positions.Add(p3);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
        }
        var (x2, y2, z2) = (x + w, y + h, z + d);
        Face(new(x, y, z2), new(x2, y, z2), new(x2, y2, z2), new(x, y2, z2));   // ön
        Face(new(x2, y, z), new(x, y, z), new(x, y2, z), new(x2, y2, z));       // arka
        Face(new(x, y, z), new(x, y, z2), new(x, y2, z2), new(x, y2, z));       // sol
        Face(new(x2, y, z2), new(x2, y, z), new(x2, y2, z), new(x2, y2, z2));   // sağ
        Face(new(x, y2, z2), new(x2, y2, z2), new(x2, y2, z), new(x, y2, z));   // üst
        Face(new(x, y, z), new(x2, y, z), new(x2, y, z2), new(x, y, z2));       // alt
        _scene.Children.Add(new GeometryModel3D(mesh, m));
    }

    /// <summary>Beşik çatı: iki eğik yüz ve iki üçgen alın.</summary>
    private void Gable(double cx, double cz, double w, double d, double y, double h, Material m)
    {
        var (x1, x2, z1, z2) = (cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2);
        var mesh = new MeshGeometry3D();
        var p = new[] { new Point3D(x1, y, z1), new Point3D(x2, y, z1), new Point3D(x2, y, z2), new Point3D(x1, y, z2), new Point3D(x1, y + h, cz), new Point3D(x2, y + h, cz) };
        foreach (var q in p) mesh.Positions.Add(q);
        foreach (var i in new[] { 3, 2, 5, 3, 5, 4, 1, 0, 4, 1, 4, 5, 0, 3, 4, 2, 1, 5 }) mesh.TriangleIndices.Add(i);
        _scene.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }

    private void Cylinder(double cx, double cz, double y, double r, double h, Material m, int n)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var b0 = new Point3D(cx + r * Math.Cos(a0), y, cz + r * Math.Sin(a0));
            var b1 = new Point3D(cx + r * Math.Cos(a1), y, cz + r * Math.Sin(a1));
            var t0 = new Point3D(b0.X, y + h, b0.Z);
            var t1 = new Point3D(b1.X, y + h, b1.Z);
            var k = mesh.Positions.Count;
            mesh.Positions.Add(b0); mesh.Positions.Add(b1); mesh.Positions.Add(t1); mesh.Positions.Add(t0);
            foreach (var j in new[] { 0, 3, 2, 0, 2, 1 }) mesh.TriangleIndices.Add(k + j);
            var c = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(cx, y + h, cz)); mesh.Positions.Add(t0); mesh.Positions.Add(t1);
            foreach (var j in new[] { 0, 2, 1 }) mesh.TriangleIndices.Add(c + j);
        }
        _scene.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }

    private void Cone(double cx, double cz, double y, double r, double h, Material m, int n)
    {
        var mesh = new MeshGeometry3D();
        var apex = new Point3D(cx, y + h, cz);
        for (var i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            var k = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a0), y, cz + r * Math.Sin(a0)));
            mesh.Positions.Add(new Point3D(cx + r * Math.Cos(a1), y, cz + r * Math.Sin(a1)));
            mesh.Positions.Add(apex);
            foreach (var j in new[] { 0, 2, 1 }) mesh.TriangleIndices.Add(k + j);
        }
        _scene.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }
}
