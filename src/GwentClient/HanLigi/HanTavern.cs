using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace GwentClient.HanLigi;

/// <summary>
/// Han odası: masanın çevresindeki 3B han içi (duvarlar, ocak, tezgâh, fıçılar, öteki masalar ve oturanlar, duvar mumları,
/// pencereler). Masa yerel koordinatlarında kurulur: x sağ, y uzak (tahtanın üstü), z yukarı (masanın yüzünden); birim 10 cm,
/// masanın yüzü z = 0, tahta x ∈ [-8, 8], y ∈ [-5, 5]. Dokular oyunun kendi dokuları (<c>tools/w3_han_scene.py</c>), yoksa düz renk.
/// </summary>
public static class HanTavern
{
    public const double Floor = -7.6, Ceil = 27, BackY = 75, SideX = 70, FrontY = -48;

    // ---------- örgü kurucu ----------
    private sealed class Mesh
    {
        public readonly Point3DCollection P = new();
        public readonly Vector3DCollection N = new();
        public readonly PointCollection T = new();
        public readonly Int32Collection I = new();

        /// <summary>
        /// Dörtgen yüz: köşe <paramref name="o"/>, kenarlar <paramref name="u"/> ve <paramref name="v"/> (u×v yüzün baktığı yön),
        /// nu×nv parçaya bölünür (köşe ışığı yumuşak düşsün); doku her <paramref name="tile"/> birimde bir tekrarlanır.
        /// </summary>
        public Mesh Quad(Point3D o, Vector3D u, Vector3D v, double tile, double step = 4, double tu0 = 0, double tv0 = 0)
        {
            var nu = Math.Max(1, (int)Math.Ceiling(u.Length / step));
            var nv = Math.Max(1, (int)Math.Ceiling(v.Length / step));
            var n = Vector3D.CrossProduct(u, v);
            n.Normalize();
            var b = P.Count;
            for (var j = 0; j <= nv; j++)
                for (var i = 0; i <= nu; i++)
                {
                    double a = (double)i / nu, c = (double)j / nv;
                    P.Add(o + u * a + v * c);
                    N.Add(n);
                    T.Add(new Point(tu0 + u.Length * a / tile, tv0 + v.Length * (1 - c) / tile));
                }
            for (var j = 0; j < nv; j++)
                for (var i = 0; i < nu; i++)
                {
                    var k = b + j * (nu + 1) + i;
                    I.Add(k); I.Add(k + 1); I.Add(k + nu + 2);
                    I.Add(k); I.Add(k + nu + 2); I.Add(k + nu + 1);
                }
            return this;
        }

        /// <summary>Kutu (dış yüzler), min/max köşeleri; <paramref name="skipBottom"/> ise alt yüz yok.</summary>
        public Mesh Box(Point3D a, Point3D b, double tile, double step = 4, bool skipBottom = true)
        {
            double x0 = a.X, y0 = a.Y, z0 = a.Z, x1 = b.X, y1 = b.Y, z1 = b.Z;
            Quad(new(x0, y0, z1), new(x1 - x0, 0, 0), new(0, y1 - y0, 0), tile, step);                 // üst
            Quad(new(x0, y0, z0), new(x1 - x0, 0, 0), new(0, 0, z1 - z0), tile, step);                 // ön (y0)
            Quad(new(x1, y1, z0), new(x0 - x1, 0, 0), new(0, 0, z1 - z0), tile, step);                 // arka (y1)
            Quad(new(x0, y1, z0), new(0, y0 - y1, 0), new(0, 0, z1 - z0), tile, step);                 // sol (x0)
            Quad(new(x1, y0, z0), new(0, y1 - y0, 0), new(0, 0, z1 - z0), tile, step);                 // sağ (x1)
            if (!skipBottom) Quad(new(x0, y1, z0), new(x1 - x0, 0, 0), new(0, y0 - y1, 0), tile, step);
            return this;
        }

        /// <summary>Dik silindir (yan yüz; isterse kapak): doku çevresinde bir kez döner, boyda <paramref name="vTile"/>.</summary>
        public Mesh Cylinder(double cx, double cy, double z0, double z1, double r, int seg = 18, double vTile = 1, bool cap = true, Func<double, double>? bulge = null)
        {
            const int rings = 6;
            var b = P.Count;
            for (var j = 0; j <= rings; j++)
            {
                var t = (double)j / rings;
                var z = z0 + (z1 - z0) * t;
                var rr = r * (bulge?.Invoke(t) ?? 1);
                for (var i = 0; i <= seg; i++)
                {
                    var a = 2 * Math.PI * i / seg;
                    P.Add(new Point3D(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr, z));
                    N.Add(new Vector3D(Math.Cos(a), Math.Sin(a), 0));
                    T.Add(new Point((double)i / seg, (1 - t) * vTile));
                }
            }
            for (var j = 0; j < rings; j++)
                for (var i = 0; i < seg; i++)
                {
                    var k = b + j * (seg + 1) + i;
                    I.Add(k); I.Add(k + 1); I.Add(k + seg + 2);
                    I.Add(k); I.Add(k + seg + 2); I.Add(k + seg + 1);
                }
            if (cap)
            {
                var c = P.Count;
                var rTop = r * (bulge?.Invoke(1) ?? 1);
                P.Add(new Point3D(cx, cy, z1)); N.Add(new Vector3D(0, 0, 1)); T.Add(new Point(0.5, 0.5));
                for (var i = 0; i <= seg; i++)
                {
                    var a = 2 * Math.PI * i / seg;
                    P.Add(new Point3D(cx + Math.Cos(a) * rTop, cy + Math.Sin(a) * rTop, z1)); N.Add(new Vector3D(0, 0, 1)); T.Add(new Point(0.5 + Math.Cos(a) * 0.5, 0.5 + Math.Sin(a) * 0.5));
                }
                for (var i = 0; i < seg; i++) { I.Add(c); I.Add(c + 1 + i); I.Add(c + 2 + i); }
            }
            return this;
        }

        public MeshGeometry3D Build()
        {
            var m = new MeshGeometry3D { Positions = P, Normals = N, TextureCoordinates = T, TriangleIndices = I };
            m.Freeze();
            return m;
        }
    }

    // ---------- malzemeler ----------
    private static Material Tex(string name, Color fallback, Color? tint = null)
    {
        Brush b = HanArt.Src(name) is { } src
            ? new ImageBrush(src) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill }
            : new SolidColorBrush(fallback);
        var m = new DiffuseMaterial(b) { Color = tint ?? Colors.White };
        m.Freeze();
        return m;
    }

    private static Material Solid(Color c) { var m = new DiffuseMaterial(new SolidColorBrush(c)); m.Freeze(); return m; }

    private static Material Glow(Brush b)
    {
        var g = new MaterialGroup { Children = { new DiffuseMaterial(Brushes.Black), new EmissiveMaterial(b) } };
        g.Freeze();
        return g;
    }

    private static GeometryModel3D Model(Mesh m, Material mat) => new(m.Build(), mat) { BackMaterial = mat };

    // ---------- oda ----------
    private static Model3DGroup? _room, _table, _lights, _props;
    private static bool _cachedAssets;

    private static T Cached<T>(ref T? slot, Func<T> build) where T : class
    {
        if (_cachedAssets != Assets.Available) { _room = _table = _lights = _props = null; _cachedAssets = Assets.Available; }
        return slot ??= build();
    }

    /// <summary>Han odası (masa yerel koordinatlarında). Donmuş ve önbellekte: oyun sahnesi ve açılış görüntüsü paylaşır.</summary>
    public static Model3DGroup Room() => Cached(ref _room, BuildRoom);

    public static Model3DGroup Table() => Cached(ref _table, BuildTable);

    public static Model3DGroup Lights() => Cached(ref _lights, BuildLights);

    /// <summary>Masadaki nesneler: yalnız açılış görüntüsünde (oyundaki yakın görüşte basit şekiller kaba duruyor).</summary>
    public static Model3DGroup Props() => Cached(ref _props, BuildProps);

    private static Model3DGroup BuildRoom()
    {
        var g = new Model3DGroup();
        var stone = Tex("room_stone", Color.FromRgb(0x5a, 0x52, 0x48));
        var timber = Tex("room_timber", Color.FromRgb(0x7a, 0x6a, 0x58));
        var floor = Tex("room_floor", Color.FromRgb(0x3a, 0x2c, 0x20));
        var ceil = Tex("room_ceiling", Color.FromRgb(0x3a, 0x2c, 0x20));
        var pole = Tex("room_pole", Color.FromRgb(0x5a, 0x44, 0x2c));
        var planks = Tex("room_planks", Color.FromRgb(0x4a, 0x34, 0x22));
        var tableWood = Tex("room_table", Color.FromRgb(0x4a, 0x34, 0x22));
        var barrel = Tex("room_barrel", Color.FromRgb(0x5a, 0x42, 0x2a));
        const double band = 4;   // duvarın taş alt kuşağı (masanın biraz üstüne dek)

        // zemin ve tavan
        g.Children.Add(Model(new Mesh().Quad(new(-SideX, FrontY, Floor), new(2 * SideX, 0, 0), new(0, BackY - FrontY, 0), 26, 5), floor));
        g.Children.Add(Model(new Mesh().Quad(new(-SideX, BackY, Ceil), new(2 * SideX, 0, 0), new(0, FrontY - BackY, 0), 34, 8), ceil));
        // arka duvar: altta taş, üstte ahşap çatkılı sıva
        g.Children.Add(Model(new Mesh().Quad(new(-SideX, BackY, Floor), new(2 * SideX, 0, 0), new(0, 0, band - Floor), 18, 4), stone));
        g.Children.Add(Model(new Mesh().Quad(new(-SideX, BackY, band), new(2 * SideX, 0, 0), new(0, 0, Ceil - band), 23, 4), timber));
        // yan duvarlar
        foreach (var s in new[] { -1, 1 })
        {
            var x = s * SideX;
            // iç yüz odaya baksın: solda +x, sağda -x
            var o1 = new Point3D(x, s < 0 ? FrontY : BackY, Floor);
            var along = new Vector3D(0, s < 0 ? BackY - FrontY : FrontY - BackY, 0);
            g.Children.Add(Model(new Mesh().Quad(o1, along, new(0, 0, band - Floor), 18, 4), stone));
            g.Children.Add(Model(new Mesh().Quad(o1 + new Vector3D(0, 0, band - Floor), along, new(0, 0, Ceil - band), 23, 4), timber));
        }
        // tavan kirişleri ve direkler
        var beams = new Mesh();
        foreach (var y in new[] { -20.0, 12, 44, 72 }) beams.Box(new(-SideX, y - 1.4, Ceil - 3), new(SideX, y + 1.4, Ceil), 14);
        foreach (var x in new[] { -34.0, 34 })
            foreach (var y in new[] { -20.0, 44 })
                beams.Box(new(x - 1.4, y - 1.4, Floor), new(x + 1.4, y + 1.4, Ceil - 3), 30);
        g.Children.Add(Model(beams, pole));

        // ocak: arka duvarda, solda; taş gövde, ağız, raf, baca
        g.Children.Add(Model(new Mesh().Box(new(-33, 67, Floor), new(-7, BackY, 10), 12).Box(new(-27, 70, 11.5), new(-13, BackY, Ceil), 12), stone));
        g.Children.Add(Model(new Mesh().Box(new(-35, 65.6, 10), new(-5, BackY, 11.5), 8), planks));
        g.Children.Add(Model(new Mesh().Quad(new(-26, 66.95, Floor), new(14, 0, 0), new(0, 0, 3 - Floor), 30), Solid(Color.FromRgb(0x08, 0x05, 0x03))));
        var fire = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.95), Center = new Point(0.5, 0.95), RadiusX = 0.6, RadiusY = 0.95 };
        fire.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0xf0, 0xb0), 0));
        fire.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0x9a, 0x30), 0.35));
        fire.GradientStops.Add(new GradientStop(Color.FromRgb(0x90, 0x2a, 0x08), 0.7));
        fire.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 0), 1));
        g.Children.Add(Model(new Mesh().Quad(new(-24.5, 66.9, Floor + 0.2), new(11, 0, 0), new(0, 0, 8.5), 1000), Glow(fire)));

        // tezgâh (sağ arkada) ve arkasındaki raflar
        g.Children.Add(Model(new Mesh().Box(new(28, 50, Floor), new(62, 58, 3.6), 10), planks));
        g.Children.Add(Model(new Mesh().Box(new(27, 49, 3.6), new(63, 59, 4.6), 12), tableWood));
        var shelves = new Mesh();
        foreach (var z in new[] { 9.0, 15 }) shelves.Box(new(30, 72, z), new(60, BackY, z + 0.8), 12);
        g.Children.Add(Model(shelves, planks));
        var bottles = new Mesh();
        var rnd = new Random(11);
        foreach (var z in new[] { 9.8, 15.8 })
            for (var x = 32.0; x < 59; x += rnd.NextDouble() * 2.6 + 1.6)
                bottles.Cylinder(x, 73.4, z, z + 3.2 + rnd.NextDouble() * 1.6, 0.7, 8, 1, true, t => t > 0.62 ? 0.45 : 1);
        g.Children.Add(Model(bottles, Solid(Color.FromRgb(0x2a, 0x3a, 0x26))));

        // fıçılar (sağ duvar dibinde)
        var barrels = new Mesh();
        foreach (var (x, y, z) in new[] { (63.0, 34.0, Floor), (63.0, 25.6, Floor), (56.0, 38.0, Floor), (63.0, 29.8, Floor + 9) })
            barrels.Cylinder(x, y, z, z + 9, 3.6, 18, 1, true, t => 1 + 0.12 * Math.Sin(t * Math.PI));
        g.Children.Add(Model(barrels, barrel));

        // öteki masalar, sıralar ve oturanlar
        var tables = new Mesh();
        foreach (var (x, y) in new[] { (-46.0, 30.0), (40.0, 14.0), (-40.0, -12.0) })
        {
            tables.Box(new(x - 8, y - 4.5, -1.2), new(x + 8, y + 4.5, -0.2), 12);
            tables.Box(new(x - 6.6, y - 3.4, Floor), new(x - 5.4, y + 3.4, -1.2), 12).Box(new(x + 5.4, y - 3.4, Floor), new(x + 6.6, y + 3.4, -1.2), 12);
            foreach (var s in new[] { -1, 1 }) tables.Box(new(x - 7.5, y + s * 7.4 - 1.4, Floor), new(x + 7.5, y + s * 7.4 + 1.4, -3.4), 12);
        }
        g.Children.Add(Model(tables, tableWood));
        var people = new Model3DGroup();
        foreach (var (x, y, face, pose) in new[] { (-49.0, 37.6, 0.0, 0), (-42.0, 37.6, 0.0, 1), (-45.0, 22.4, 180.0, 2), (37.0, 21.4, 0.0, 1), (44.0, 6.6, 180.0, 0), (54.0, 46.0, 200.0, 2), (-17.0, 60.0, 160.0, 3) })
            people.Children.Add(Patron(x, y, face, pose));

        // pencereler (sol duvar: gece mavisi) ve duvar mumları
        var night = new LinearGradientBrush(Color.FromRgb(0x2a, 0x3c, 0x5a), Color.FromRgb(0x0c, 0x14, 0x24), 90);
        foreach (var y in new[] { -2.0, 26 })
        {
            g.Children.Add(Model(new Mesh().Quad(new(-SideX + 0.1, y, 6), new(0, 8, 0), new(0, 0, 11), 100), Glow(night)));
            var frame = new Mesh().Box(new(-SideX, y + 3.7, 6), new(-SideX + 0.5, y + 4.3, 17), 10).Box(new(-SideX, y, 11.2), new(-SideX + 0.5, y + 8, 11.8), 10);
            g.Children.Add(Model(frame, pole));
        }
        var flame = new RadialGradientBrush(Color.FromRgb(0xff, 0xe6, 0xa0), Color.FromRgb(0xff, 0x90, 0x30));
        var sconces = Sconces;
        var flames = new Mesh();
        foreach (var p in sconces) flames.Box(p - new Vector3D(0.45, 0.45, 0), p + new Vector3D(0.45, 0.45, 1.4), 10);
        g.Children.Add(Model(flames, Glow(flame)));
        var lamps = new Mesh();
        foreach (var p in sconces) lamps.Box(p - new Vector3D(0.7, 0.7, 2.4), p + new Vector3D(0.7, 0.7, 0), 6);
        g.Children.Add(Model(lamps, pole));

        // oturanlar en son: saydam kenarları arkalarındakini örtmesin
        g.Children.Add(people);

        g.Freeze();
        return g;
    }

    /// <summary>Duvar mumlarının yerleri (ışık ve ileride titreşen alev için).</summary>
    public static readonly Point3D[] Sconces = { new(-2, BackY - 0.4, 12), new(18, BackY - 0.4, 12), new(-SideX + 0.4, 14, 13), new(SideX - 0.4, 8, 13), new(SideX - 0.4, -26, 13), new(-SideX + 0.4, -30, 13) };

    /// <summary>Ocağın ağzı (ateşin ortası).</summary>
    public static readonly Point3D Hearth = new(-19, 66.5, -3.5);

    /// <summary>
    /// Işıklar: çok loş sıcak ortam; ocak en güçlüsü (yakında çok parlak, çabuk söner); duvar mumları küçük havuzlar; masamızın
    /// üstündeki asma lamba masayı odakta tutar.
    /// </summary>
    private static Model3DGroup BuildLights()
    {
        var g = new Model3DGroup();
        g.Children.Add(new AmbientLight(Color.FromRgb(0x1a, 0x13, 0x0e)));
        g.Children.Add(new PointLight(Color.FromRgb(0xff, 0x84, 0x34), new Point3D(-19, 58, -1.5)) { Range = 130, ConstantAttenuation = 0.16, LinearAttenuation = 0.022, QuadraticAttenuation = 0.0011 });
        g.Children.Add(new PointLight(Color.FromRgb(0xb0, 0x48, 0x14), new Point3D(-19, 48, -6)) { Range = 60, ConstantAttenuation = 0.5, LinearAttenuation = 0.03, QuadraticAttenuation = 0.002 });
        foreach (var p in Sconces)
            g.Children.Add(new PointLight(Color.FromRgb(0xff, 0xa4, 0x52), p + new Vector3D(p.X < -60 ? 3 : p.X > 60 ? -3 : 0, p.Y > 70 ? -3 : 0, 0)) { Range = 40, ConstantAttenuation = 0.55, LinearAttenuation = 0.05, QuadraticAttenuation = 0.006 });
        g.Children.Add(new PointLight(Color.FromRgb(0xd0, 0x90, 0x50), new Point3D(45, 50, 13)) { Range = 40, ConstantAttenuation = 0.4, LinearAttenuation = 0.04, QuadraticAttenuation = 0.004 });
        g.Children.Add(new PointLight(Color.FromRgb(0xc8, 0x84, 0x48), new Point3D(38, 12, 10)) { Range = 36, ConstantAttenuation = 0.5, LinearAttenuation = 0.05, QuadraticAttenuation = 0.005 });
        g.Children.Add(new PointLight(Color.FromRgb(0xc8, 0x84, 0x48), new Point3D(-44, 26, 10)) { Range = 36, ConstantAttenuation = 0.5, LinearAttenuation = 0.05, QuadraticAttenuation = 0.005 });
        // masamızın üstündeki asma lamba: masa sahnenin odağı
        g.Children.Add(new PointLight(Color.FromRgb(0xff, 0xcc, 0x8a), new Point3D(0, 0, 15)) { Range = 60, ConstantAttenuation = 0.08, LinearAttenuation = 0.018, QuadraticAttenuation = 0.0036 });
        g.Freeze();
        return g;
    }

    /// <summary>Bizim masa: kalın tahta, kenarları ve ayakları (tahta dokusu ayrı çizilir, z = 0 yüzüne).</summary>
    private static Model3DGroup BuildTable()
    {
        var g = new Model3DGroup();
        var wood = Tex("room_table", Color.FromRgb(0x4a, 0x34, 0x22), tint: Color.FromRgb(0xff, 0xd6, 0xa8));
        g.Children.Add(Model(new Mesh().Box(new(-9.8, -6.6, -0.9), new(9.8, 6.6, -0.02), 6, 2), wood));
        var legs = new Mesh();
        foreach (var x in new[] { -8.6, 8.6 })
            foreach (var y in new[] { -5.4, 5.4 })
                legs.Box(new(x - 0.7, y - 0.7, Floor), new(x + 0.7, y + 0.7, -0.9), 8, 3);
        g.Children.Add(Model(legs, wood));
        g.Freeze();
        return g;
    }

    /// <summary>Masamızdaki mumun alevi (ışıltı için).</summary>
    public static readonly Point3D CandleFlame = new(8.5, -1.0, 2.12);

    /// <summary>
    /// Masanın sağ kenarındaki nesneler (tahtanın dışında): pirinç tabakta mum, uzak köşede bira kupası, önde altın yığınları
    /// ve zarlar. Oyundaki görüşte sağdaki boş şeride (oyuncu listesinin altı) düşerler; kupa listenin arkasında kalır.
    /// Kodla kurulur (silindir, kutu).
    /// </summary>
    private static Model3DGroup BuildProps()
    {
        var g = new Model3DGroup();
        var brass = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x9a, 0x74, 0x30))), new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0xa0)), 24) } };
        var gold = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xc8, 0x98, 0x38))), new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xff, 0xf0, 0xb0)), 30) } };
        var wax = Solid(Color.FromRgb(0xe8, 0xdc, 0xc0));
        var wood = Tex("room_planks", Color.FromRgb(0x5a, 0x40, 0x28), tint: Color.FromRgb(0xff, 0xd8, 0xb0));
        // mum: tabak, kulp, mum, fitil
        var (cx, cy) = (CandleFlame.X, CandleFlame.Y);
        g.Children.Add(Model(new Mesh().Cylinder(cx, cy, 0, 0.13, 0.72, 20).Cylinder(cx, cy, 0.13, 0.3, 0.26, 14), brass));
        g.Children.Add(Model(new Mesh().Box(new(cx + 0.62, cy - 0.08, 0.05), new(cx + 1.0, cy + 0.08, 0.13), 4), brass));
        g.Children.Add(Model(new Mesh().Cylinder(cx, cy, 0.3, 1.85, 0.22, 14, 1, true, t => t > 0.9 ? 0.92 : 1), wax));
        g.Children.Add(Model(new Mesh().Box(new(cx - 0.02, cy - 0.02, 1.85), new(cx + 0.02, cy + 0.02, 1.98), 1), Solid(Color.FromRgb(0x18, 0x10, 0x08))));
        var flame = new RadialGradientBrush(Color.FromRgb(0xff, 0xf4, 0xc8), Color.FromRgb(0xff, 0x9c, 0x38));
        g.Children.Add(Model(new Mesh().Cylinder(cx, cy, 1.95, 2.35, 0.08, 8, 1, true, t => 1 - t * 0.85), Glow(flame)));
        // bira kupası: ahşap gövde, metal halkalar, kulp, köpük
        var (mx, my) = (8.0, 3.9);
        g.Children.Add(Model(new Mesh().Cylinder(mx, my, 0, 1.7, 0.72, 18, 1, false, t => 1 - 0.08 * t), wood));
        g.Children.Add(Model(new Mesh().Cylinder(mx, my, 0.2, 0.38, 0.75, 18, 1, false).Cylinder(mx, my, 1.3, 1.48, 0.7, 18, 1, false), brass));
        g.Children.Add(Model(new Mesh().Box(new(mx + 0.6, my - 0.12, 0.4), new(mx + 1.15, my + 0.12, 0.56), 4).Box(new(mx + 0.98, my - 0.12, 0.4), new(mx + 1.15, my + 0.12, 1.5), 4).Box(new(mx + 0.6, my - 0.12, 1.34), new(mx + 1.15, my + 0.12, 1.5), 4), wood));
        g.Children.Add(Model(new Mesh().Cylinder(mx, my, 1.6, 1.82, 0.63, 18), Solid(Color.FromRgb(0xf0, 0xe4, 0xc8))));
        // zarlar (kemik rengi, üst yüzde noktalar)
        var die = new MaterialGroup { Children = { new DiffuseMaterial(new ImageBrush(DieFace()) { Stretch = Stretch.Fill }), new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x60, 0x58, 0x48)), 18) } };
        foreach (var (x, y, rot) in new[] { (8.7, -3.5, 18.0), (9.25, -4.05, -24.0) })
        {
            var m = Model(new Mesh().Box(new(-0.21, -0.21, 0), new(0.21, 0.21, 0.42), 0.42, 1), die);
            m.Transform = new Transform3DGroup { Children = { new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rot)), new TranslateTransform3D(x, y, 0) } };
            g.Children.Add(m);
        }
        // altın yığınları (sağ önde)
        var coins = new Mesh();
        foreach (var (x, y, n) in new[] { (7.5, -2.7, 6), (8.15, -2.45, 4), (7.75, -3.3, 2) })
            for (var i = 0; i < n; i++)
                coins.Cylinder(x + (i % 2) * 0.03, y + (i % 3) * 0.02, i * 0.085, i * 0.085 + 0.075, 0.28, 16);
        g.Children.Add(Model(coins, gold));
        g.Freeze();
        return g;
    }

    private static BitmapSource? _die;

    /// <summary>Zar yüzü: kemik rengi, beş nokta.</summary>
    private static BitmapSource DieFace()
    {
        if (_die != null) return _die;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xe8, 0xdc, 0xc4)), null, new Rect(0, 0, 64, 64));
            var pip = new SolidColorBrush(Color.FromRgb(0x2a, 0x1a, 0x10));
            foreach (var (x, y) in new[] { (16, 16), (48, 16), (32, 32), (16, 48), (48, 48) }) dc.DrawEllipse(pip, null, new Point(x, y), 6, 6);
        }
        var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return _die = rtb;
    }

    /// <summary>Oturan bir müşterinin silueti: koyu düz kesik (bulanık arka planda insan gibi okunur).</summary>
    private static Model3D Patron(double x, double y, double faceDeg, int pose)
    {
        var img = Silhouette(pose);
        var w = 9.0;
        var h = w * img.PixelHeight / img.PixelWidth;
        var a = faceDeg * Math.PI / 180;
        var u = new Vector3D(Math.Cos(a) * w, Math.Sin(a) * w, 0);
        var o = new Point3D(x, y, Floor + 0.2) - u / 2;
        var m = new Mesh().Quad(o, u, new Vector3D(0, 0, h), 1000, 100);
        // doku koordinatları tek parça (0..1)
        var mesh = new MeshGeometry3D { Positions = m.P, Normals = m.N, TriangleIndices = m.I, TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(0, 0), new(1, 0) } };
        mesh.Freeze();
        var mat = new DiffuseMaterial(new ImageBrush(img));
        mat.Freeze();
        return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
    }

    private static readonly Dictionary<int, BitmapSource> Silhouettes = new();

    private static BitmapSource Silhouette(int pose)
    {
        if (Silhouettes.TryGetValue(pose, out var s)) return s;
        const int W = 180, H = 260;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var ink = new SolidColorBrush(Color.FromRgb(0x2c, 0x21, 0x19));
            var lean = pose switch { 1 => 10, 2 => -8, _ => 0 };
            dc.DrawEllipse(ink, null, new Point(90 + lean, 52), 20, 24);                                              // baş
            if (pose == 3) dc.DrawGeometry(ink, null, Geometry.Parse("M62,66 C60,18 120,18 118,66 L124,96 L56,96 Z"));   // kapüşon (ayakta, ocak başında)
            dc.DrawGeometry(ink, null, Geometry.Parse($"M{50 + lean},88 C{58 + lean},74 {122 + lean},74 {130 + lean},88 L140,190 L40,190 Z"));   // omuzlar, gövde
            dc.DrawGeometry(ink, null, Geometry.Parse(pose == 3 ? "M44,190 L136,190 L132,258 L48,258 Z" : "M36,186 L150,186 L150,214 L36,214 Z")); // bacaklar / kucak
            if (pose == 1) dc.DrawGeometry(ink, null, Geometry.Parse("M128,96 L172,70 L178,80 L136,112 Z"));            // kalkık kol (kupa)
            if (pose == 2) dc.DrawGeometry(ink, null, Geometry.Parse("M52,100 L20,150 L32,156 L62,116 Z"));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return Silhouettes[pose] = rtb;
    }

    // ---------- görüntü ----------
    /// <summary>Masa yerel koordinatlarında kamera.</summary>
    public static PerspectiveCamera Camera(Point3D pos, Point3D target, double fov)
    {
        var look = target - pos;
        var up = Vector3D.CrossProduct(Vector3D.CrossProduct(look, new Vector3D(0, 0, 1)), look);
        return new PerspectiveCamera(pos, look, up, fov);
    }

    /// <summary>Oyundaki masa kamerası (HanScene ile aynı görüş).</summary>
    public static PerspectiveCamera GameCamera(double tilt, double lift, double camZ, double fov, double shiftX)
    {
        var a = -tilt * Math.PI / 180;   // tahtanın dünyadaki eğiminin tersi
        var px = -shiftX;
        var py = -lift * Math.Cos(a) - camZ * Math.Sin(a);
        var pz = -lift * Math.Sin(a) + camZ * Math.Cos(a);
        var look = new Vector3D(0, Math.Sin(a), -Math.Cos(a));
        var up = new Vector3D(0, Math.Cos(a), Math.Sin(a));
        return new PerspectiveCamera(new Point3D(px, py, pz), look, up, fov);
    }

    /// <summary>Dünya noktasının ekrandaki yeri (yatay görüş açılı perspektif kamera); kameranın arkasındaysa null.</summary>
    public static Point? Project(PerspectiveCamera cam, Point3D p, double w, double h)
    {
        var f = cam.LookDirection; f.Normalize();
        var r = Vector3D.CrossProduct(f, cam.UpDirection); r.Normalize();
        var u = Vector3D.CrossProduct(r, f);
        var d = p - cam.Position;
        var z = Vector3D.DotProduct(d, f);
        if (z <= 0.01) return null;
        var k = 1 / Math.Tan(cam.FieldOfView * Math.PI / 360);
        return new Point(w / 2 + Vector3D.DotProduct(d, r) / z * k * w / 2, h / 2 - Vector3D.DotProduct(d, u) / z * k * w / 2);
    }

    /// <summary>
    /// Gwent tahtası masanın yüzünde (z = 0, x ∈ [-8, 8], y ∈ [-5, 5]). Kendinden ışıklı: odanın ışıkları tahtanın rengini
    /// değiştirmez (oyundaki görüntüsü aynen kalır).
    /// </summary>
    public static GeometryModel3D Board(ImageSource board)
    {
        // dokunun yalnız dolu kısmı (çerçeveli tahta; çevresi saydam): kendinden ışıklı malzemede saydam yerler siyah çıkardı
        double u0 = HanScene.BoardTexRect.Left / HanScene.BoardW, u1 = HanScene.BoardTexRect.Right / HanScene.BoardW;
        double v0 = HanScene.BoardTexRect.Top / HanScene.BoardH, v1 = HanScene.BoardTexRect.Bottom / HanScene.BoardH;
        double x0 = -8 + 16 * u0, x1 = -8 + 16 * u1, y0 = 5 - 10 * v1, y1 = 5 - 10 * v0;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { new(x0, y0, 0.005), new(x1, y0, 0.005), new(x1, y1, 0.005), new(x0, y1, 0.005) },
            Normals = new Vector3DCollection { new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1) },
            TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        var brush = new ImageBrush(board) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = HanScene.BoardTexRect };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        return new GeometryModel3D(mesh, Glow(brush));
    }

    private static Model3DGroup TableWithBoard(ImageSource? board)
    {
        var g = new Model3DGroup { Children = { Table(), Props() } };
        if (board != null) g.Children.Add(Board(board));
        return g;
    }

    private static BitmapSource Snap(FrameworkElement el, int w, int h)
    {
        el.Measure(new Size(w, h));
        el.Arrange(new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        rtb.Freeze();
        return rtb;
    }

    private static Viewport3D View(Camera cam, int w, int h, params Model3D[] models)
    {
        var vp = new Viewport3D { Width = w, Height = h, Camera = cam, ClipToBounds = true };
        var g = new Model3DGroup();
        foreach (var m in models) g.Children.Add(m);
        g.Children.Add(Lights());
        vp.Children.Add(new ModelVisual3D { Content = g });
        return vp;
    }

    /// <summary>
    /// Sahneyi (oda + masa + tahta) verilen kamerayla bir bitmap'e çizer. <paramref name="roomBlur"/> > 0 ise oda ayrı çizilip
    /// bulanıklaşır (alan derinliği: masa net, han odak dışı). Üstüne ocağın ışıltısı ve kenar karartması.
    /// </summary>
    public static BitmapSource Render(PerspectiveCamera cam, int w, int h, ImageSource? board, double roomBlur = 0)
    {
        var root = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(4, 3, 2)) };
        if (roomBlur > 0)
        {
            var room = Snap(new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(4, 3, 2)), Children = { View(cam, w, h, Room()) } }, w, h);
            root.Children.Add(new Image { Source = room, Width = w, Height = h, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = roomBlur, KernelType = System.Windows.Media.Effects.KernelType.Gaussian } });
            root.Children.Add(View(cam, w, h, TableWithBoard(board)));
        }
        else root.Children.Add(View(cam, w, h, Room(), TableWithBoard(board)));
        // ocağın ışıltısı (eklemeli değil, yarı saydam sıcak hale) ve kenar karartması
        if (Project(cam, Hearth, w, h) is { } hp)
        {
            var glow = new System.Windows.Shapes.Ellipse { Width = w * 0.36, Height = w * 0.3, IsHitTestVisible = false, Fill = new RadialGradientBrush(Color.FromArgb(0x58, 0xff, 0x8c, 0x30), Color.FromArgb(0, 0xff, 0x70, 0x20)) };
            glow.Margin = new Thickness(hp.X - w * 0.18, hp.Y - w * 0.15, 0, 0);
            glow.HorizontalAlignment = HorizontalAlignment.Left;
            glow.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(glow);
        }
        root.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xc8, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.8, GradientOrigin = new Point(0.5, 0.55), Center = new Point(0.5, 0.55) } });
        return Snap(root, w, h);
    }
}
