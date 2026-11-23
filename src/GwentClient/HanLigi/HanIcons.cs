using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi'nin kendi yetenek simgeleri: oyun dosyalarına bağlı değil, kodda çizilmiş vektör şekiller (24×24 ızgara). Kartta
/// satır simgesiyle aynı altın madalyonun içinde koyu siluet olarak durur; masadaki küçük kartta (~15 px) da okunsun diye kalın
/// ve az ayrıntılı çizilir. Görsel denetim: <c>--shot-han</c> → <c>han_ikonlar.png</c>.
/// </summary>
public static class HanIcons
{
    // ---------- şekil yardımcıları ----------
    private static Geometry P(string d) => Geometry.Parse(d);
    private static Geometry C(double cx, double cy, double r) => new EllipseGeometry(new Point(cx, cy), r, r);

    private static Geometry E(double cx, double cy, double rx, double ry, double rot = 0)
    {
        var e = new EllipseGeometry(new Point(cx, cy), rx, ry);
        if (rot != 0) e.Transform = new RotateTransform(rot, cx, cy);
        return e;
    }

    private static Geometry R(double x, double y, double w, double h, double r = 0) => new RectangleGeometry(new Rect(x, y, w, h), r, r);

    /// <summary>Çizgiyi kalın dolu şekle çevirir (yuvarlak uçlu).</summary>
    private static Geometry S(string d, double thick) => P(d).GetWidenedPathGeometry(new Pen(Brushes.Black, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    private static Geometry T(Geometry g, double rot, double cx = 12, double cy = 12, double dx = 0, double dy = 0)
        => new GeometryGroup { Children = { g }, Transform = new TransformGroup { Children = { new RotateTransform(rot, cx, cy), new TranslateTransform(dx, dy) } } };

    private static Geometry U(params Geometry[] gs)
    {
        Geometry acc = gs[0];
        for (var i = 1; i < gs.Length; i++) acc = Geometry.Combine(acc, gs[i], GeometryCombineMode.Union, null);
        return acc;
    }

    /// <summary><paramref name="a"/>'dan <paramref name="cut"/> çıkarılır.</summary>
    private static Geometry X(Geometry a, params Geometry[] cut) => Geometry.Combine(a, cut.Length == 1 ? cut[0] : U(cut), GeometryCombineMode.Exclude, null);

    private static Geometry I(Geometry a, Geometry b) => Geometry.Combine(a, b, GeometryCombineMode.Intersect, null);

    // ---------- simgeler ----------
    private static readonly Lazy<Dictionary<string, Geometry>> Set = new(Build);

    private static Dictionary<string, Geometry> Build()
    {
        var m = new Dictionary<string, Geometry>();

        // Bağ: iki halkalı zincir
        {
            var l1 = X(R(1.5, 8.2, 12.5, 7.6, 3.8), R(4.3, 10.8, 6.9, 2.4, 1.2));
            var l2 = X(R(10, 8.2, 12.5, 7.6, 3.8), R(12.8, 10.8, 6.9, 2.4, 1.2));
            m["Bağ"] = T(U(l1, l2), -45);
        }
        // Büyüme: topraktan çıkan filiz
        m["Büyüme"] = U(
            P("M4,21.8 Q12,16.6 20,21.8 Z"),
            R(11, 9.5, 2, 11, 1),
            P("M11.6,14.2 C7.8,14.6 4,12.6 3,7.6 C7.4,7 11,9.2 11.6,14.2 Z"),
            P("M12.4,11 C12.8,6.2 16.6,3 21.4,3.2 C21,8 17.2,11.2 12.4,11 Z"));
        // Moral: sancak
        m["Moral"] = U(R(4.4, 2.8, 2.2, 19.2, 1.1), C(5.5, 2.6, 1.7), P("M6.6,4 L21,4 L17.4,8.7 L21,13.4 L6.6,13.4 Z"));
        // Casus: göz
        m["Casus"] = U(X(P("M1.2,12 C5,5.2 19,5.2 22.8,12 C19,18.8 5,18.8 1.2,12 Z"), C(12, 12, 4.8)), X(C(12, 12, 2.9), C(13.1, 10.9, 0.9)));
        // Ozan: lavta
        m["Ozan"] = U(
            X(E(8.6, 15.4, 6.6, 5.0, -45), C(9.4, 14.6, 1.6)),
            P("M10.4,11.4 L18.2,3.6 L19.9,5.3 L12.1,13.1 Z"),
            P("M17.6,3.0 L19.6,1.0 L23.0,2.8 L20.6,6.2 Z"));
        // Hazine: kilitli sandık
        m["Hazine"] = X(U(
                P("M3,10.4 L3,8.4 C3,5.2 6.2,3.4 12,3.4 C17.8,3.4 21,5.2 21,8.4 L21,10.4 Z"),
                R(3, 11.6, 18, 9.8, 1.2),
                R(9.8, 8.4, 4.4, 6.2, 0.9)),
            U(R(3, 10.4, 6.8, 1.2), R(14.2, 10.4, 6.8, 1.2), C(12, 10.8, 1.0), R(11.5, 10.8, 1.0, 2.3)));
        // Pusu: kapüşonlu, yüzü gölgede, gözleri parlar
        m["Pusu"] = U(
            X(P("M12,1.2 C7.6,3.2 4.2,7.4 4.2,12.8 L2.6,22.4 L21.4,22.4 L19.8,12.8 C19.8,7.4 16.4,3.2 12,1.2 Z"), E(12, 13.8, 5.2, 6.4)),
            X(E(12, 15, 3.7, 4.4), E(10.1, 14.1, 1.6, 0.8, 12), E(13.9, 14.1, 1.6, 0.8, -12)));
        // İntikam: sıkılmış yumruk
        m["İntikam"] = X(U(
                R(5.0, 5.6, 3.7, 7.4, 1.8), R(8.7, 4.6, 3.7, 8.4, 1.8), R(12.4, 4.9, 3.7, 8.1, 1.8), R(16.1, 6.0, 3.3, 7.0, 1.6),
                R(5.0, 9.4, 14.4, 8.6, 2.6),
                R(7.6, 17, 9.2, 5.6, 1.2)),
            S("M8.7,6.2 L8.7,10.2", 0.75), S("M12.4,5.6 L12.4,10.2", 0.75), S("M16.1,6.6 L16.1,10.2", 0.75),
            S("M4.2,12.6 L11.6,12.6 C13,12.6 13.6,14.4 12.2,15.2", 0.9), S("M6.8,17.6 L17.6,17.6", 0.8));
        // Coşku: defne çelengi (her düğümde iki yaprak, uca doğru)
        {
            var parts = new List<Geometry>();
            foreach (var side in new[] { -1, 1 })
            {
                Point At(double deg, double r) { var a = deg * Math.PI / 180; return new Point(12 + side * -Math.Cos(a) * r, 12.6 + Math.Sin(a) * r); }
                var s0 = At(98, 8.4); var s1 = At(250, 8.4); var sm = At(174, 9.0);
                parts.Add(S($"M{F(s0.X)},{F(s0.Y)} Q{F(sm.X)},{F(sm.Y)} {F(s1.X)},{F(s1.Y)}", 1.5));
                for (var i = 0; i < 5; i++)
                {
                    var deg = 122 + i * 25;
                    var a1 = At(deg, 8.4); var a2 = At(deg + 1, 8.4);
                    var tangent = Math.Atan2(a2.Y - a1.Y, a2.X - a1.X) * 180 / Math.PI;
                    foreach (var o in new[] { 1.0, -1.0 })
                    {
                        var p = At(deg + 4, 8.4 + o * 1.9);
                        parts.Add(E(p.X, p.Y, 2.4, 1.05, tangent + side * o * -32));
                    }
                }
                var tip = At(255, 8.4);
                parts.Add(E(tip.X, tip.Y, 2.2, 1.05, side * 25 + 90));
            }
            parts.Add(S("M9.6,20.8 L14.4,20.8", 1.6));
            m["Coşku"] = U(parts.ToArray());
        }
        // Çağrı: arkada bir eş (iki figür)
        {
            var back = X(U(C(16.2, 6.6, 3.1), P("M10,17.6 C10,13 12.8,11 16.2,11 C19.6,11 22.4,13 22.4,17.6 Z")),
                C(8.4, 9.4, 4.7), P("M0.4,23.5 C0.4,15.2 4,12.6 8.4,12.6 C12.8,12.6 16.6,15.2 16.6,23.5 Z"));
            m["Çağrı"] = U(back, C(8.4, 9.4, 3.5), P("M1.6,22 C1.6,16.4 4.6,13.9 8.4,13.9 C12.2,13.9 15.2,16.4 15.2,22 Z"));
        }
        // Şifacı: kalın haç
        m["Şifacı"] = U(R(9, 2.6, 6, 18.8, 1.4), R(2.6, 9, 18.8, 6, 1.4));
        // Yankı: hayalet
        m["Yankı"] = X(P("M12,2.2 C7,2.2 4.3,6.2 4.3,11 L4.3,21.6 L6.9,19.4 L9.5,21.6 L12,19.4 L14.5,21.6 L17.1,19.4 L19.7,21.6 L19.7,11 C19.7,6.2 17,2.2 12,2.2 Z"),
            E(9.3, 10.2, 1.4, 2.0), E(14.7, 10.2, 1.4, 2.0), E(12, 15.2, 1.2, 1.6));
        // Yansıma: el aynası (camda parıltı)
        m["Yansıma"] = U(
            X(E(12, 9, 6.6, 7.6), E(12, 9, 5.0, 6.0)),
            X(E(12, 9, 3.9, 4.9), S("M9.2,9.2 L12.8,5.0", 1.2), S("M10.6,11.8 L14.6,7.2", 0.8)),
            R(11.2, 16.2, 1.6, 1.4), R(10.8, 17.2, 2.4, 4.6, 1), C(12, 22, 1.5));
        // Çevik: iki yöne ok
        m["Çevik"] = U(R(2.6, 6.6, 14, 2.6, 1.3), P("M15,3.2 L21.6,7.9 L15,12.6 Z"), R(7.4, 15.4, 14, 2.6, 1.3), P("M9,11.4 L2.4,16.7 L9,22 Z"));

        // ---- özel kartlar ----
        // Ayaz: kar tanesi
        {
            var arm = U(R(11, 2.2, 2, 10, 1), S("M12,6.6 L9.3,3.9", 1.7), S("M12,6.6 L14.7,3.9", 1.7));
            var parts = new List<Geometry> { C(12, 12, 2.4) };
            for (var k = 0; k < 6; k++) parts.Add(T(arm, k * 60));
            m["Ayaz"] = U(parts.ToArray());
        }
        // Sis: üç dalgalı şerit
        m["Sis"] = U(
            S("M2.6,6.8 C4.6,4.9 6.6,4.9 8.6,6.8 S12.6,8.7 14.6,6.8 S18.6,4.9 21.2,6.8", 2.2),
            S("M2.8,12.2 C4.8,10.3 6.8,10.3 8.8,12.2 S12.8,14.1 14.8,12.2 S18.8,10.3 21.4,12.2", 2.2),
            S("M2.6,17.6 C4.6,15.7 6.6,15.7 8.6,17.6 S12.6,19.5 14.6,17.6 S18.6,15.7 21.2,17.6", 2.2));
        // Yağmur: bulut ve damlalar
        m["Yağmur"] = U(C(7.8, 10, 3.6), C(12.6, 7.6, 4.6), C(17.2, 10.4, 3.4), R(4.2, 9.6, 16.4, 4.6, 2.3),
            S("M7.6,17 L6.2,20.8", 1.9), S("M12.4,17 L11,21.6", 1.9), S("M17.2,17 L15.8,20.8", 1.9));
        // Kavurma: alev (içte açık dil)
        m["Kavurma"] = X(P("M12,1.2 C13.2,6 18.8,8.2 18.8,14.4 C18.8,18.8 15.6,22.4 12,22.4 C8.4,22.4 5.2,18.8 5.2,14.6 C5.2,11 7.4,9.2 8.4,7.4 C9,10 10,11 11,11.6 C10.4,8 11,4.4 12,1.2 Z"),
            P("M12,12.2 C13,14.2 15.2,15.4 15.2,17.8 C15.2,19.8 13.7,20.9 12,20.9 C10.3,20.9 8.8,19.8 8.8,17.9 C8.8,16.2 9.8,15.2 10.5,14.1 C10.9,15.2 11.4,15.6 12,15.8 C11.8,14.6 11.7,13.4 12,12.2 Z"));
        // Boynuz: savaş borusu (ağızlıktan geniş çana doğru açılan kavisli koni, iki bilezik)
        m["Boynuz"] = Horn();
        // Kalkan: kenarlı arma kalkanı, içinde haç
        {
            var outer = P("M12,1.6 L20.8,4.4 L20.8,11 C20.8,16.2 17.2,19.8 12,22.4 C6.8,19.8 3.2,16.2 3.2,11 L3.2,4.4 Z");
            var inner = P("M12,3.8 L18.7,5.9 L18.7,11 C18.7,15.1 15.8,18.1 12,20.1 C8.2,18.1 5.3,15.1 5.3,11 L5.3,5.9 Z");
            m["Kalkan"] = U(X(outer, inner), I(U(R(10.8, 3, 2.4, 18), R(4, 9.4, 16, 2.4)), inner));
        }
        foreach (var g in m.Values) g.Freeze();
        return m;
    }

    /// <summary>
    /// Boru: ağızlık (sağ alt) → çan (sol üst) kavisli orta çizgi boyunca genişleyen koni; çanın ağzı elips, iki yerde ince
    /// bilezik aralığı.
    /// </summary>
    private static Geometry Horn()
    {
        Point M = new(21, 19.4), Ctl = new(8.2, 20.2), B = new(5.6, 6.2);
        Point At(double t) => new((1 - t) * (1 - t) * M.X + 2 * (1 - t) * t * Ctl.X + t * t * B.X, (1 - t) * (1 - t) * M.Y + 2 * (1 - t) * t * Ctl.Y + t * t * B.Y);
        Vector Tan(double t) { var v = new Vector(2 * (1 - t) * (Ctl.X - M.X) + 2 * t * (B.X - Ctl.X), 2 * (1 - t) * (Ctl.Y - M.Y) + 2 * t * (B.Y - Ctl.Y)); v.Normalize(); return v; }
        double W(double t) => 0.75 + 3.4 * Math.Pow(t, 1.6);
        const int n = 28;
        var left = new List<Point>();
        var right = new List<Point>();
        for (var i = 0; i <= n; i++)
        {
            var t = (double)i / n;
            var c = At(t);
            var tg = Tan(t);
            var nn = new Vector(-tg.Y, tg.X) * W(t);
            left.Add(c + nn);
            right.Add(c - nn);
        }
        right.Reverse();
        var pts = left.Concat(right).ToList();
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        var bt = Tan(1);
        var bellAngle = Math.Atan2(bt.Y, bt.X) * 180 / Math.PI + 90;
        var bell = E(B.X + bt.X * 0.4, B.Y + bt.Y * 0.4, W(1) + 0.6, 1.5, bellAngle);
        Geometry Band(double t)
        {
            var c = At(t);
            var tg = Tan(t);
            var nn = new Vector(-tg.Y, tg.X) * (W(t) + 1);
            var a = c + nn; var b = c - nn;
            return S($"M{F(a.X)},{F(a.Y)} L{F(b.X)},{F(b.Y)}", 0.8);
        }
        return X(U(g, bell, C(M.X, M.Y, 1.1)), Band(0.42), Band(0.72));
    }

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Galeri (görsel denetim) için bütün simgeler, adlarıyla.</summary>
    public static IReadOnlyDictionary<string, Geometry> All => Set.Value;

    public static Geometry? For(Ability a) => a switch
    {
        Ability.Bond => Set.Value["Bağ"], Ability.Growth => Set.Value["Büyüme"], Ability.Morale => Set.Value["Moral"], Ability.Spy => Set.Value["Casus"],
        Ability.Bard => Set.Value["Ozan"], Ability.Treasure => Set.Value["Hazine"], Ability.Ambush => Set.Value["Pusu"], Ability.Revenge => Set.Value["İntikam"],
        Ability.Zeal => Set.Value["Coşku"], Ability.Summon => Set.Value["Çağrı"], Ability.Medic => Set.Value["Şifacı"], Ability.DeathEcho => Set.Value["Yankı"],
        Ability.Mirror => Set.Value["Yansıma"], _ => null
    };

    /// <summary>Kartın simgesi: yetenek, özel kart etkisi ya da Çevik; kahramanda yok.</summary>
    public static Geometry? For(CardDef d)
    {
        if (d.IsHero) return null;
        if (!d.IsUnit)
            return d.Special switch
            {
                Special.Frost => Set.Value["Ayaz"], Special.Fog => Set.Value["Sis"], Special.Rain => Set.Value["Yağmur"], Special.Scorch => Set.Value["Kavurma"],
                Special.Horn => Set.Value["Boynuz"], Special.Shield => Set.Value["Kalkan"], _ => null
            };
        return For(d.Ability) ?? (d.Agile ? Set.Value["Çevik"] : null);
    }

    private static readonly Brush Gold = Freeze(new RadialGradientBrush(Color.FromRgb(0xf6, 0xdc, 0x96), Color.FromRgb(0xb0, 0x82, 0x34)) { GradientOrigin = new Point(0.4, 0.3) });
    private static readonly Brush Rim = Freeze(new SolidColorBrush(Color.FromRgb(0x3a, 0x2a, 0x12)));
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x1c, 0x12, 0x08)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Altın madalyon içinde koyu simge (kartın satır simgesiyle aynı biçim).</summary>
    public static FrameworkElement Medal(Geometry g, double size, bool shadow = true)
    {
        var grid = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        grid.Children.Add(new Ellipse
        {
            Fill = Gold, Stroke = Rim, StrokeThickness = Math.Max(1, size * 0.06),
            Effect = shadow ? new DropShadowEffect { Color = Colors.Black, BlurRadius = Math.Max(3, size * 0.2), ShadowDepth = 1, Opacity = 0.8 } : null
        });
        var glyph = new Canvas { Width = 24, Height = 24 };
        glyph.Children.Add(new Path { Data = g, Fill = Ink });
        grid.Children.Add(new Viewbox { Width = size * 0.72, Height = size * 0.72, Child = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    public static FrameworkElement? Medal(CardDef d, double size) => For(d) is { } g ? Medal(g, size) : null;
}
