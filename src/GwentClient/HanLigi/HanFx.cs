using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using HanLigi;
using Ramp = GwentClient.HanLigi.FxEngine.Ramp;
using Rgb = GwentClient.HanLigi.FxEngine.Rgb;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi efekt tarifleri. Hepsi <see cref="FxEngine"/> üstünde: fizikli parçacıklar (alev, kıvılcım, toz, duman) ve
/// her karede çizilen biçimli ışıklar (şok dalgası halkası, Quen kubbesi, Yrden mührü, şimşek). Masa zemini perspektifli
/// olduğundan zemindeki halkalar yatay elips (dikey/yatay ≈ 0.3) çizilir. Bütün koordinatlar ekran (1920×1080).
/// </summary>
public static class HanFx
{
    private const float Floor = 0.3f;   // zemin elipsinin basıklığı (perspektif)
    private static readonly Rgb White = new(1, 1, 1);
    private static readonly Rgb GoldC = new(1f, 0.78f, 0.3f);
    private static readonly Rgb AardC = new(0.45f, 0.72f, 1f);
    private static readonly Rgb VioletC = new(0.72f, 0.36f, 1f);
    private static readonly Rgb FireC = new(1f, 0.45f, 0.08f);
    private static readonly Rgb IceC = new(0.6f, 0.88f, 1f);

    private static float Ease(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

    /// <summary>Açılış–tutma–sönüş zarfı.</summary>
    private static float Env(float t, float rise = 0.12f, float hold = 0.7f) => t < rise ? t / rise : t < hold ? 1 : Math.Max(0, 1 - (t - hold) / (1 - hold));

    // ---------- temel parçalar ----------

    /// <summary>Patlama: göz alan çakma, kabaran ateş topu, beyaz-sıcak çekirdek, yerçekimli kıvılcımlar, zemine vuran ışık ve duman.</summary>
    public static void Explosion(FxEngine e, Point at, Ramp ramp, double power = 1, Ramp? sparks = null)
    {
        var p = (float)power;
        e.Light(at, 150 * p, Ramp.White, 1.5f * p, 0.16f, 0.75f);
        e.Light(new Point(at.X, at.Y + 30), 760 * p, ramp, 0.6f * p, 1.2f);
        // ateş topu: hızla açılır, sürüklenmeyle yavaşlar, ısıyla yükselir
        for (var i = 0; i < (int)(110 * p); i++)
            e.Spark(at, e.R(0, 360), e.R(80, 520) * p, e.R(0.45f, 1.0f), e.R(24, 40) * p, e.R(70, 140) * p, ramp, e.R(0.4f, 0.75f), -170, 3.2f, false, 500);
        for (var i = 0; i < (int)(30 * p); i++)
            e.Spark(at, e.R(0, 360), e.R(30, 220) * p, e.R(0.2f, 0.4f), e.R(30, 50) * p, e.R(50, 90) * p, Ramp.HotCore, e.R(0.8f, 1.2f), -60, 3);
        for (var i = 0; i < (int)(80 * p); i++)
            e.Spark(at, e.R(190, 350), e.R(380, 1050) * p, e.R(0.5f, 1.3f), e.R(3, 6), 2, sparks ?? Ramp.Ember, e.R(0.9f, 1.5f), 1100, 0.9f, true);
        // duman: ateş sönerken ortaya çıkar
        for (var i = 0; i < (int)(28 * p); i++)
            e.Puff(new Point(at.X + e.R(-30, 30), at.Y + e.R(-20, 10)), e.R(200, 340), e.R(30, 170) * p, e.R(1.3f, 2.4f), e.R(50, 80) * p, e.R(180, 280) * p, e.R(0.2f, 0.32f), -70, 90);
    }

    /// <summary>Bir noktada bir süre yanan alev (yanan kart, Kavurma): dalgalanan dillerle, beyaz-sıcak dip, kor ve duman.</summary>
    public static void Burn(FxEngine e, Point at, double width, double seconds = 1.2, double intensity = 1)
    {
        var w = (float)width;
        var k = (float)intensity;
        e.Light(new Point(at.X, at.Y + 10), w * 3.2f, Ramp.Fire, 0.35f * k, (float)seconds + 0.4f);
        e.Add(new FxEngine.Emitter
        {
            Pos = _ => at, Duration = seconds, Rate = 220 * intensity,
            Spawn = (en, p, t) =>
            {
                var fall = (float)Math.Max(0.25, 1 - t / seconds);   // sönerken diller kısalır
                var q = new Point(p.X + en.R(-w * 0.45f, w * 0.45f), p.Y + en.R(-w * 0.1f, w * 0.5f));
                en.Spark(q, en.R(-100, -80), en.R(90, 240) * fall, en.R(0.35f, 0.75f), en.R(22, 36), en.R(6, 14), Ramp.Fire, en.R(0.45f, 0.8f) * k, -260, 1.4f, false, 700);
                if (en.Rng.Next(3) == 0) en.Spark(q, en.R(-95, -85), en.R(40, 90), en.R(0.18f, 0.3f), en.R(18, 26), 8, Ramp.HotCore, 0.7f * k, -100, 1);
                if (en.Rng.Next(7) == 0) en.Spark(q, en.R(-120, -60), en.R(120, 320), en.R(0.6f, 1.3f), 3, 2, Ramp.Ember, 1.3f, -60, 0.6f, true, 900);
                if (en.Rng.Next(5) == 0) en.Puff(new Point(q.X, q.Y - 50), en.R(-100, -80), en.R(40, 110), en.R(1.0f, 1.8f), 40, en.R(120, 200), 0.16f * k, -60, 120);
            }
        });
    }

    /// <summary>Yukarı süzülen ışık (iksir aurası, iyileşme): bir satırın üstünde; zeminde ışık havuzu.</summary>
    public static void Aura(FxEngine e, Point center, double width, Ramp ramp, double seconds = 1.1)
    {
        var w = (float)width;
        e.Light(new Point(center.X, center.Y + 20), w * 1.3f, ramp, 0.45f, (float)seconds + 0.4f);
        e.Add(new FxEngine.Emitter
        {
            Pos = _ => center, Duration = seconds, Rate = 220,
            Spawn = (en, p, _) =>
            {
                var q = new Point(p.X + en.R(-w / 2, w / 2), p.Y + en.R(-10, 40));
                en.Spark(q, en.R(-95, -85), en.R(60, 200), en.R(0.6f, 1.3f), en.R(10, 18), en.R(2, 5), ramp, en.R(0.6f, 1.0f), -80, 0.8f, en.Rng.Next(3) == 0, 120);
            }
        });
        e.Add(new FxEngine.Painter
        {
            Duration = seconds + 0.3,
            Draw = (en, t, _) => en.Ellipse(new Point(center.X, center.Y + 30), w * 0.5 * (0.8 + 0.2 * Ease(t)), w * 0.5 * Floor, 10, ramp.At(0.3f), 0.35f * Env(t, 0.15f, 0.6f))
        });
    }

    /// <summary>Satır zaferi: satır boyunca fışkıran kıvılcımlar ve zemin ışığı.</summary>
    public static void Fountain(FxEngine e, Point center, double width, Ramp ramp)
    {
        var w = (float)width;
        e.Light(center, w * 1.1f, ramp, 0.6f, 1.0f);
        for (var i = 0; i < 180; i++)
        {
            var q = new Point(center.X + e.R(-w / 2, w / 2), center.Y + e.R(-6, 6));
            e.Spark(q, e.R(-115, -65), e.R(260, 720), e.R(0.6f, 1.4f), e.R(3, 6), 2, ramp, e.R(0.9f, 1.5f), 900, 0.5f, true);
        }
        for (var i = 0; i < 34; i++)
            e.Spark(new Point(center.X + e.R(-w / 2, w / 2), center.Y), e.R(-100, -80), e.R(20, 80), e.R(0.4f, 0.8f), e.R(40, 70), e.R(90, 150), ramp, 0.4f, -40);
    }

    /// <summary>Zeminde genişleyen şok halkası (perspektifli elips); ön yarısı parlak.</summary>
    public static void Ring(FxEngine e, Point center, double radius, Rgb c, double seconds, float energy = 1, float thick = 22, double delay = 0)
        => e.Add(new FxEngine.Painter
        {
            Duration = seconds, Delay = delay,
            Draw = (en, t, _) =>
            {
                var r = radius * Ease(t);
                var fade = (1 - t) * (1 - t) * energy;
                var th = thick * (1 - t * 0.6f);
                en.Ellipse(center, r, r * Floor, th, c, fade * 0.5f, 0, 360, d => 0.55f + 0.45f * (float)Math.Sin(d * Math.PI / 180));
                en.Ellipse(center, r * 0.97, r * 0.97 * Floor, th * 0.35f, Rgb.Lerp(c, White, 0.6f), fade * 0.7f, 0, 360, d => 0.4f + 0.6f * (float)Math.Sin(d * Math.PI / 180));
            }
        });

    // ---------- işaret: havada çiziliş ----------

    /// <summary>
    /// İşaret sembolü havada çizilir: ışıklı bir kalem sembolün hatlarını izler (motor ışığı + WPF'de keskin beyaz çekirdek),
    /// ucundan kıvılcım saçar; tamamlanınca sembol parlayıp söner.
    /// </summary>
    public static void DrawGlyph(Canvas layer, FxEngine e, Sign s, Point center, double size, int drawMs = 380)
    {
        var geo = HanArt.SignGeometry(s).GetFlattenedPathGeometry();
        Point Map(Point p) => new(center.X + (p.X - 50) / 100 * size, center.Y + (p.Y - 50) / 100 * size);
        // parçalar: figürler arası atlama çizilmez
        var segs = new List<(Point A, Point B, double L0, double L1)>();
        double total = 0;
        foreach (var fig in geo.Figures)
        {
            var prev = Map(fig.StartPoint);
            var pts = new List<Point>();
            foreach (var seg in fig.Segments)
                if (seg is PolyLineSegment pl) pts.AddRange(pl.Points.Select(Map));
                else if (seg is LineSegment ls) pts.Add(Map(ls.Point));
            if (fig.IsClosed) pts.Add(Map(fig.StartPoint));
            foreach (var q in pts)
            {
                var l = (q - prev).Length;
                if (l > 0.01) { segs.Add((prev, q, total, total + l)); total += l; }
                prev = q;
            }
        }
        total = Math.Max(1, total);
        Point At(double f)
        {
            var d = f * total;
            foreach (var sg in segs)
                if (sg.L1 >= d) return sg.A + (sg.B - sg.A) * ((d - sg.L0) / Math.Max(1e-6, sg.L1 - sg.L0));
            return segs.Count > 0 ? segs[^1].B : center;
        }
        var ramp = RampFor(s);
        var col = ramp.At(0.45f);
        var thick = (float)Math.Max(4, size * 0.05);
        var drawS = drawMs / 1000.0;
        // keskin beyaz çekirdek (WPF, tam çözünürlük)
        var path = new Path
        {
            Data = HanArt.SignGeometry(s), Stretch = Stretch.Fill, Stroke = new SolidColorBrush(Color.FromRgb(0xff, 0xfb, 0xf0)),
            StrokeThickness = thick * 0.45, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false
        };
        var bounds = HanArt.SignGeometry(s).Bounds;
        path.Width = bounds.Width / 100 * size;
        path.Height = bounds.Height / 100 * size;
        Canvas.SetLeft(path, center.X + (bounds.X - 50) / 100 * size);
        Canvas.SetTop(path, center.Y + (bounds.Y - 50) / 100 * size);
        var dashUnits = total / (thick * 0.45) + 2;
        path.StrokeDashArray = new DoubleCollection { dashUnits, dashUnits };
        path.StrokeDashOffset = dashUnits;
        layer.Children.Add(path);
        path.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(dashUnits, 0, TimeSpan.FromMilliseconds(drawMs)));
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(drawMs + 600) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(drawMs + 100))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        path.BeginAnimation(UIElement.OpacityProperty, fade);
        Fx.After(drawMs + 650, () => layer.Children.Remove(path));
        // ışıklı iz: çizilen kısım parlar; bitince yanıp söner
        e.Light(center, (float)size * 2.2f, ramp, 0.3f, (float)drawS + 0.8f, 0);
        e.Add(new FxEngine.Painter
        {
            Duration = drawS + 0.6,
            Draw = (en, _, el) =>
            {
                var f = Math.Min(1, el / drawS);
                var after = Math.Max(0, el - drawS);
                var k = after <= 0 ? 1f : after < 0.08 ? 1 + (float)after / 0.08f * 0.8f : Math.Max(0, 1.8f * (1 - (float)(after - 0.08) / 0.52f));
                var d = f * total;
                foreach (var sg in segs)
                {
                    if (sg.L0 >= d) break;
                    var b = sg.L1 <= d ? sg.B : sg.A + (sg.B - sg.A) * ((d - sg.L0) / (sg.L1 - sg.L0));
                    en.Line(sg.A, b, thick * 4.5f, col, 0.16f * k);
                    en.Line(sg.A, b, thick * 1.6f, Rgb.Lerp(col, White, 0.5f), 0.3f * k);
                }
                if (f < 1) en.Glow(At(f), thick * 7, Rgb.Lerp(col, White, 0.7f), 1.4f);
            }
        });
        // kalem ucu: kıvılcım
        e.Add(new FxEngine.Emitter
        {
            Pos = At, Duration = drawS, Rate = 320,
            Spawn = (en, p, _) =>
            {
                en.Spark(p, en.R(0, 360), en.R(20, 120), en.R(0.25f, 0.55f), en.R(8, 14), 3, ramp, 0.9f, 60, 2);
                if (en.Rng.Next(2) == 0) en.Spark(p, en.R(0, 360), en.R(180, 460), en.R(0.3f, 0.6f), 3, 2, ramp, 1.3f, 700, 1, true);
            }
        });
        Fx.After(drawMs, () =>
        {
            e.Light(center, (float)size * 1.4f, ramp, 1.1f, 0.35f, 0);
            for (var i = 0; i < 70; i++) e.Spark(At(e.Rng.NextDouble()), e.R(0, 360), e.R(60, 320), e.R(0.3f, 0.8f), 3, 2, ramp, 1.2f, 300, 1.5f, true);
        });
    }

    public static Ramp RampFor(Sign s) => s switch { Sign.Aard => Ramp.Aard, Sign.Igni => Ramp.Fire, Sign.Quen => Ramp.Quen, _ => Ramp.Yrden };

    // ---------- işaretlerin etkisi ----------

    /// <summary>Igni: elden hedefe kükreyen alev seli (beyaz-sıcak çekirdek, dalgalanan diller, kor, duman), hedefte patlama ve süren yanma.</summary>
    public static void Igni(FxEngine e, Point from, Point to, double cardW, bool landed, int streamMs = 520)
    {
        var dir = to - from;
        var ang = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI;
        var dist = (float)dir.Length;
        var streamS = streamMs / 1000.0;
        e.Light(from, 380, Ramp.Fire, 0.7f, (float)streamS + 0.3f);
        e.Add(new FxEngine.Painter
        {
            Duration = streamS + 0.15,
            Draw = (en, t, _) =>
            {
                // alev yolunu aydınlatan sıcak hâle
                var reach = Math.Min(1, t * 2.2);
                var n = 10;
                for (var i = 0; i <= n; i++)
                {
                    var f = reach * i / n;
                    en.Glow(from + dir * f, 120 + 140 * (float)f, FireC, 0.12f * (1 - t * 0.6f));
                }
            }
        });
        e.Add(new FxEngine.Emitter
        {
            Pos = _ => from, Duration = streamS, Rate = 900,
            Spawn = (en, p, t) =>
            {
                var speed = dist / 0.4f * en.R(0.85f, 1.12f);
                var r = en.Rng.Next(10);
                if (r < 3) en.Spark(p, ang + en.R(-4, 4), speed * 1.05f, en.R(0.3f, 0.42f), en.R(10, 16), en.R(30, 46), Ramp.HotCore, en.R(0.5f, 0.75f), -60, 0.4f, false, 250);
                else if (r < 9) en.Spark(p, ang + en.R(-11, 11), speed, en.R(0.42f, 0.58f), en.R(16, 26), en.R(90, 150), Ramp.Fire, en.R(0.28f, 0.48f), -320, 0.5f, false, 1400);
                else en.Spark(p, ang + en.R(-16, 16), speed * 1.15f, en.R(0.4f, 0.7f), 3, 2, Ramp.Ember, 1.4f, 300, 0.4f, true, 600);
                if (en.Rng.Next(9) == 0) en.Puff(p + dir * 0.55, ang + en.R(-20, 20), speed * 0.35f, en.R(0.9f, 1.5f), 40, en.R(130, 200), 0.18f, -90, 140);
            }
        });
        Fx.After(380, () =>
        {
            Explosion(e, to, Ramp.Fire, landed ? 1.15 : 0.65);
            Ring(e, new Point(to.X, to.Y + 30), cardW * 1.6, FireC, 0.5, 0.8f, 18);
            if (landed) Burn(e, to, cardW, 1.5, 1.0);
        });
    }

    /// <summary>Aard: elden fırlayan telekinetik dalga (soluk mavi hava kütlesi + çizgi akımı); satırda şok halkası, savrulan toz ve kıymık.</summary>
    public static void Aard(FxEngine e, Point from, Point rowCenter, double rowWidth)
    {
        var dir = rowCenter - from;
        var ang = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI;
        var dist = (float)dir.Length;
        e.Light(from, 200, Ramp.Aard, 1.2f, 0.25f, 0.7f);
        Ring(e, from, 160, AardC, 0.35, 1.2f, 14);
        // dalga gövdesi: büyük soluk hava kütleleri + hızlı çizgiler
        for (var i = 0; i < 70; i++)
            e.Spark(from, ang + e.R(-14, 14), dist / 0.3f * e.R(0.8f, 1.1f), e.R(0.3f, 0.42f), e.R(30, 50), e.R(150, 260), Ramp.Aard, e.R(0.12f, 0.2f), 0, 1.6f);
        for (var i = 0; i < 240; i++)
            e.Spark(from, ang + e.R(-18, 18), e.R(1100, 1900), e.R(0.25f, 0.42f), e.R(4, 8), 3, Ramp.Aard, e.R(0.5f, 0.9f), 0, 2.4f, true);
        Fx.After(240, () =>
        {
            var rx = (float)rowWidth * 0.62f;
            e.Light(rowCenter, rx * 1.6f, Ramp.Aard, 0.7f, 0.6f);
            e.Light(rowCenter, 260, Ramp.White, 1.1f, 0.14f);
            Ring(e, rowCenter, rx, AardC, 0.7, 1.4f, 30);
            Ring(e, rowCenter, rx * 0.75, AardC, 0.8, 0.8f, 16, 0.08);
            // halkayla birlikte açılan çizgiler (tutarlı hız: halka biçimi korunur)
            for (var i = 0; i < 260; i++)
            {
                var a = e.R(0, 360) * Math.PI / 180;
                var v = e.R(0.9f, 1.05f) * rx * 2.6f;
                e.Add(new FxEngine.Particle
                {
                    X = (float)rowCenter.X, Y = (float)rowCenter.Y, VX = (float)Math.Cos(a) * v, VY = (float)Math.Sin(a) * v * Floor,
                    Drag = 3.6f, Life = e.R(0.4f, 0.7f), S0 = e.R(4, 7), S1 = 2, Energy = e.R(0.6f, 1.0f), Ramp = Ramp.Aard, Streak = true
                });
            }
            // toz: zeminden savrulan bulut (karartır) ve ışık alan zerreler
            for (var i = 0; i < 46; i++)
            {
                var a = e.R(0, 360) * Math.PI / 180;
                var q = new Point(rowCenter.X + Math.Cos(a) * rx * e.R(0.1f, 0.6f), rowCenter.Y + Math.Sin(a) * rx * Floor * 0.6);
                e.Add(new FxEngine.Particle
                {
                    X = (float)q.X, Y = (float)q.Y, VX = (float)Math.Cos(a) * e.R(250, 600), VY = (float)Math.Sin(a) * e.R(60, 180) - 40, AY = -30, Drag = 2.2f,
                    Life = e.R(1.0f, 1.8f), S0 = e.R(50, 80), S1 = e.R(160, 260), Energy = e.R(0.22f, 0.34f), Ramp = Ramp.Smoke, Smoke = true, Turb = 80
                });
            }
            for (var i = 0; i < 90; i++)
            {
                var q = new Point(rowCenter.X + e.R(-rx, rx) * 0.7, rowCenter.Y + e.R(-15, 25));
                e.Spark(q, e.R(-165, -15), e.R(120, 420), e.R(0.6f, 1.2f), e.R(10, 20), e.R(30, 50), Ramp.Dust, 0.5f, 380, 1.4f);
            }
            for (var i = 0; i < 50; i++)
                e.Spark(new Point(rowCenter.X + e.R(-rx, rx) * 0.6, rowCenter.Y), e.R(-160, -20), e.R(300, 800), e.R(0.6f, 1.1f), 3, 2, Ramp.Dust, 1.4f, 1300, 0.6f, true);
        });
    }

    /// <summary>
    /// Quen: satırın üstünde altın enerji kubbesi — parlak kenar (silüet), dönen enlem/boylam ağı, zeminde ışık halkası,
    /// yüzeyde dolaşan kıvılcım. Engellediğinde darbe noktasından çatlaklar yayılır, kubbe titrer, kıvılcım saçar.
    /// </summary>
    public static void Quen(Canvas layer, FxEngine e, Point center, double width, bool blocked)
    {
        var rx = width * 0.5;
        var ry = rx * Floor;
        var hh = width * 0.3;
        var life = blocked ? 1.6 : 1.35;
        var hitAt = 0.42f;
        var hit = new Point(center.X + rx * 0.18, center.Y - hh * 0.82);
        e.Light(center, (float)rx * 2.4f, Ramp.Quen, 0.4f, (float)life);
        e.Add(new FxEngine.Painter
        {
            Duration = life,
            Draw = (en, t, el) =>
            {
                var env = Env(t, 0.14f, 0.72f);
                var grow = 0.55f + 0.45f * Ease(Math.Min(1, t / 0.18f));
                var flick = 1f;
                if (blocked && t > hitAt && t < hitAt + 0.16f) flick = 0.35f + 1.3f * (float)Math.Abs(Math.Sin(el * 70));
                var k = env * flick;
                var Rx = rx * grow;
                var Ry = ry * grow;
                var Hh = hh * grow;
                Point P(double u, double v) => new(center.X + Math.Cos(u) * Rx * Math.Cos(v), center.Y + Math.Sin(u) * Ry * Math.Cos(v) - Math.Sin(v) * Hh);
                // silüet: kubbenin dış kenarı (yukarı yarım elips) ve taban halkası
                en.Ellipse(center, Rx, Hh, 26, GoldC, 0.22f * k, 180, 360);
                en.Ellipse(center, Rx, Hh, 7, Rgb.Lerp(GoldC, White, 0.55f), 0.55f * k, 180, 360);
                en.Ellipse(center, Rx, Ry, 22, GoldC, 0.3f * k, 0, 360, d => 0.5f + 0.5f * (float)Math.Sin(d * Math.PI / 180));
                en.Ellipse(center, Rx, Ry, 6, Rgb.Lerp(GoldC, White, 0.5f), 0.5f * k);
                // ağ: dönen boylamlar ve enlemler; ön yüz parlak, arka yüz sönük
                var spin = el * 0.5;
                for (var m = 0; m < 14; m++)
                {
                    var u = m * Math.PI / 7 + spin;
                    var front = (float)Math.Sin(u);
                    var vis = front > 0 ? 0.55f + 0.45f * front : 0.22f;
                    var shimmer = 0.75f + 0.25f * (float)Math.Sin(el * 9 + m * 1.7);
                    Point last = P(u, 0);
                    for (var j = 1; j <= 8; j++)
                    {
                        var q = P(u, j * Math.PI / 2 / 8);
                        en.Line(last, q, 4, GoldC, 0.11f * k * vis * shimmer);
                        last = q;
                    }
                }
                foreach (var v in new[] { 0.42, 0.85, 1.2 })
                {
                    var c = new Point(center.X, center.Y - Math.Sin(v) * Hh);
                    en.Ellipse(c, Rx * Math.Cos(v), Ry * Math.Cos(v), 4, GoldC, 0.1f * k, 0, 360, d => 0.3f + 0.7f * Math.Max(0, (float)Math.Sin(d * Math.PI / 180)));
                }
                // ağ düğümlerinde parıltı
                for (var m = 0; m < 14; m += 2)
                {
                    var u = m * Math.PI / 7 + spin;
                    if (Math.Sin(u) < 0) continue;
                    en.Glow(P(u, 0.85), 10, Rgb.Lerp(GoldC, White, 0.6f), 0.35f * k * (0.6f + 0.4f * (float)Math.Sin(el * 12 + m)));
                }
                // darbe: çatlaklar ve dalgalanma
                if (blocked && t > hitAt && t < hitAt + 0.35f)
                {
                    var h = (t - hitAt) / 0.35f;
                    var ck = (1 - h) * 1.2f;
                    var rng = new Random(7);
                    for (var c = 0; c < 6; c++)
                    {
                        var a = rng.NextDouble() * Math.PI * 2;
                        var len = (40 + rng.NextDouble() * 90) * Math.Min(1, h * 4);
                        var p0 = hit;
                        for (var s = 0; s < 4; s++)
                        {
                            a += (rng.NextDouble() - 0.5) * 0.9;
                            var p1 = p0 + new Vector(Math.Cos(a), Math.Sin(a) * 0.6) * len / 4;
                            en.Line(p0, p1, 4, White, 0.5f * ck);
                            en.Line(p0, p1, 14, GoldC, 0.15f * ck);
                            p0 = p1;
                        }
                    }
                    en.Ellipse(hit, 30 + 160 * Ease(h), (30 + 160 * Ease(h)) * 0.55, 10, GoldC, 0.4f * (1 - h));
                }
            }
        });
        // yüzeyde dolaşan kıvılcımlar
        e.Add(new FxEngine.Emitter
        {
            Pos = _ => center, Duration = life * 0.8, Rate = 160,
            Spawn = (en, p, _) =>
            {
                var u = en.R(0, 360) * Math.PI / 180;
                var v = en.R(0, 85) * Math.PI / 180;
                var q = new Point(p.X + Math.Cos(u) * rx * Math.Cos(v), p.Y + Math.Sin(u) * ry * Math.Cos(v) - Math.Sin(v) * hh);
                en.Spark(q, en.R(0, 360), en.R(10, 60), en.R(0.3f, 0.6f), en.R(6, 10), 2, Ramp.Quen, 0.9f, -40, 1);
            }
        });
        if (blocked)
            Fx.After((int)(life * hitAt * 1000), () =>
            {
                e.Light(hit, 120, Ramp.White, 1.6f, 0.15f, 0.7f);
                e.Light(hit, 420, Ramp.Gold, 0.7f, 0.5f, 0.6f);
                for (var i = 0; i < 140; i++) e.Spark(hit, e.R(160, 380), e.R(300, 1000), e.R(0.4f, 1.0f), e.R(3, 5), 2, Ramp.Quen, 1.4f, 1000, 1, true);
            });
    }

    /// <summary>
    /// Yrden: hedef satırın zemininde dönen mor mühür — çift halka, üç rün, halkadan yükselen ışık perdesi, çıtırdayan şimşekler.
    /// Yakaladığında şimşekler sıklaşır ve mühür son bir kez çakar.
    /// </summary>
    public static void Yrden(Canvas layer, FxEngine e, Point center, double width, bool caught)
    {
        var rx = width * 0.4;
        var ry = rx * Floor;
        var life = 1.6;
        e.Light(center, (float)rx * 2.6f, Ramp.Yrden, 0.5f, (float)life);
        e.Add(new FxEngine.Painter
        {
            Duration = life,
            Draw = (en, t, el) =>
            {
                var env = Env(t, 0.12f, 0.75f);
                var sc = 1 + 0.6 * (1 - Ease(Math.Min(1, t / 0.25f)));   // dışarıdan içe kapanır
                var Rx = rx * sc;
                var Ry = ry * sc;
                var lite = Rgb.Lerp(VioletC, White, 0.55f);
                en.Ellipse(center, Rx, Ry, 24, VioletC, 0.28f * env, 0, 360, d => 0.55f + 0.45f * (float)Math.Sin(d * Math.PI / 180));
                en.Ellipse(center, Rx, Ry, 6, lite, 0.6f * env);
                // iç halka: dönen kesik çizgi
                var rot = el * 70;
                en.Ellipse(center, Rx * 0.8, Ry * 0.8, 5, lite, 0.5f * env, 0, 360, d => Math.Sin((d + rot) * Math.PI / 180 * 12) > 0.2 ? 1 : 0);
                // rünler: halka üstünde üç Yrden sembolü (perspektifle basık üçgen + çizgi)
                for (var i = 0; i < 3; i++)
                {
                    var a = (i * 120 - rot * 0.6) * Math.PI / 180;
                    var c = new Point(center.X + Math.Cos(a) * Rx * 0.9, center.Y + Math.Sin(a) * Ry * 0.9);
                    var s = 26.0;
                    var p0 = new Point(c.X, c.Y - s * 0.5);
                    var p1 = new Point(c.X - s * 0.55, c.Y + s * 0.25);
                    var p2 = new Point(c.X + s * 0.55, c.Y + s * 0.25);
                    var front = 0.6f + 0.4f * (float)Math.Sin(a);
                    en.Line(p0, p1, 4, White, 0.5f * env * front);
                    en.Line(p1, p2, 4, White, 0.5f * env * front);
                    en.Line(p2, p0, 4, White, 0.5f * env * front);
                    en.Line(new Point(c.X, c.Y - s * 0.9), new Point(c.X, c.Y + s * 0.4), 3, lite, 0.45f * env * front);
                    en.Glow(c, 60, VioletC, 0.25f * env);
                }
                // ışık perdesi: halkadan yükselen dikey ışık şeritleri
                for (var i = 0; i < 36; i++)
                {
                    var a = (i * 10 + rot * 0.3) * Math.PI / 180;
                    var b = new Point(center.X + Math.Cos(a) * Rx, center.Y + Math.Sin(a) * Ry);
                    var hgt = 50 + 30 * Math.Sin(el * 5 + i * 1.3);
                    var front = Math.Sin(a) > 0 ? 1f : 0.45f;
                    en.Line(b, new Point(b.X, b.Y - hgt), 8, VioletC, 0.05f * env * front);
                }
                // şimşek: kısa aralıklarla halkadan halkaya ya da içe
                var slot = (int)(el / 0.07);
                var rng = new Random(slot * 31 + 5);
                var bolts = caught ? 2 : 1;
                if (rng.NextDouble() < (caught ? 0.85 : 0.55) && env > 0.3)
                    for (var b = 0; b < bolts; b++)
                    {
                        var a0 = rng.NextDouble() * Math.PI * 2;
                        var a1 = a0 + Math.PI * (0.5 + rng.NextDouble());
                        var p0 = new Point(center.X + Math.Cos(a0) * Rx, center.Y + Math.Sin(a0) * Ry);
                        var p1 = rng.Next(2) == 0
                            ? new Point(center.X + Math.Cos(a1) * Rx * 0.8, center.Y + Math.Sin(a1) * Ry * 0.8)
                            : new Point(center.X + rng.Next(-40, 40), center.Y - rng.Next(30, 90));
                        en.Bolt(p0, p1, 3, VioletC, 0.9f * env, 0.25, 5);
                    }
                if (caught && t > 0.62f && t < 0.72f) en.Glow(center, (float)Rx * 2.2f, lite, 0.5f * (1 - (t - 0.62f) / 0.1f));
            }
        });
        e.Add(new FxEngine.Emitter
        {
            Pos = _ => center, Duration = life * 0.8, Rate = 200,
            Spawn = (en, p, _) =>
            {
                var a = en.R(0, 360) * Math.PI / 180;
                var q = new Point(p.X + Math.Cos(a) * rx, p.Y + Math.Sin(a) * ry);
                en.Spark(q, en.R(-95, -85), en.R(60, 220), en.R(0.4f, 0.9f), en.R(4, 8), 2, Ramp.Yrden, 1f, -60, 0.8f, en.Rng.Next(2) == 0);
            }
        });
        if (caught)
            Fx.After((int)(life * 620), () =>
            {
                e.Light(center, 360, Ramp.White, 1.1f, 0.18f);
                for (var i = 0; i < 90; i++) e.Spark(center, e.R(190, 350), e.R(200, 700), e.R(0.4f, 0.9f), 3, 2, Ramp.Yrden, 1.3f, 800, 1, true);
            });
    }

    // ---------- simya ----------

    /// <summary>Fırlatılan bomba: parlayan şişe kavis çizer, ardında kıvılcım ve duman izi; düşünce <paramref name="land"/> çalışır.</summary>
    public static void Throw(FxEngine e, Point from, Point to, Ramp trail, int ms, Action land)
    {
        var dur = ms / 1000.0;
        Point At(double t)
        {
            var p = from + (to - from) * t;
            return new Point(p.X, p.Y - Math.Sin(t * Math.PI) * 200);
        }
        var head = trail.At(0.2f);
        e.Add(new FxEngine.Painter
        {
            Duration = dur,
            Draw = (en, t, _) =>
            {
                var p = At(t);
                en.Glow(p, 70, head, 0.5f);
                en.Glow(p, 18, White, 1.4f);
            }
        });
        e.Add(new FxEngine.Emitter
        {
            Pos = At, Duration = dur, Rate = 300,
            Spawn = (en, p, _) =>
            {
                en.Spark(p, en.R(0, 360), en.R(10, 60), en.R(0.25f, 0.5f), en.R(14, 22), 3, trail, 0.8f, -30, 1, false, 200);
                if (en.Rng.Next(2) == 0) en.Spark(p, en.R(0, 360), en.R(100, 320), en.R(0.2f, 0.45f), 3, 2, trail, 1.2f, 700, 1, true);
                if (en.Rng.Next(6) == 0) en.Puff(p, en.R(0, 360), en.R(10, 40), en.R(0.6f, 1.0f), 20, 70, 0.14f, -30, 60);
            }
        });
        Fx.After(ms, land);
    }

    /// <summary>Buz bombası: beyaz çakma, saçılan buz kıymıkları, zemine yayılan kırağı halkası ve soğuk sis.</summary>
    public static void Frost(FxEngine e, Point at)
    {
        e.Light(at, 170, Ramp.White, 1.6f, 0.18f, 0.75f);
        e.Light(new Point(at.X, at.Y + 20), 560, Ramp.Ice, 0.6f, 1.6f);
        Ring(e, new Point(at.X, at.Y + 20), 300, IceC, 0.6, 1.3f, 26);
        for (var i = 0; i < 130; i++) e.Spark(at, e.R(0, 360), e.R(250, 900), e.R(0.35f, 0.8f), e.R(3, 6), 2, Ramp.Ice, 1.3f, 500, 2.6f, true);
        for (var i = 0; i < 50; i++) e.Spark(at, e.R(0, 360), e.R(40, 220), e.R(0.9f, 1.7f), e.R(40, 60), e.R(140, 220), Ramp.Ice, 0.16f, -10, 1.6f, false, 60);
        // kırağı: zeminde uzun süre kalan, yavaş sönen kristal halka
        e.Add(new FxEngine.Painter
        {
            Duration = 1.6, Delay = 0.1,
            Draw = (en, t, _) =>
            {
                var k = (1 - t) * (1 - t);
                var rng = new Random(11);
                for (var i = 0; i < 18; i++)
                {
                    var a = i * Math.PI * 2 / 18 + rng.NextDouble() * 0.3;
                    var len = (90 + rng.NextDouble() * 110) * Ease(Math.Min(1, t * 4));
                    var p1 = new Point(at.X + Math.Cos(a) * len, at.Y + 20 + Math.Sin(a) * len * Floor);
                    en.Line(new Point(at.X, at.Y + 20), p1, 4, IceC, 0.3f * k);
                }
                en.Glow(new Point(at.X, at.Y + 20), 220, IceC, 0.25f * k);
            }
        });
    }

    /// <summary>Ateş topu: beyaz-sıcak çekirdek, dalgalanan alev izi ve duman; hedefte patlar.</summary>
    public static void Fireball(FxEngine e, Point from, Point to, int ms, Action land)
    {
        var dur = ms / 1000.0;
        Point At(double t)
        {
            var ease = t * t;
            var p = from + (to - from) * ease;
            return new Point(p.X, p.Y - Math.Sin(t * Math.PI) * 140);
        }
        e.Add(new FxEngine.Painter
        {
            Duration = dur,
            Draw = (en, t, _) =>
            {
                var p = At(t);
                en.Glow(p, 150, FireC, 0.35f);
                en.Glow(p, 46, new Rgb(1, 0.85f, 0.5f), 1.2f);
                en.Glow(p, 18, White, 1.5f);
            }
        });
        e.Add(new FxEngine.Emitter
        {
            Pos = At, Duration = dur, Rate = 650,
            Spawn = (en, p, _) =>
            {
                en.Spark(p, en.R(0, 360), en.R(10, 70), en.R(0.3f, 0.55f), en.R(30, 44), en.R(8, 16), Ramp.Fire, 0.55f, -120, 1, false, 500);
                if (en.Rng.Next(3) == 0) en.Spark(p, en.R(0, 360), en.R(80, 300), en.R(0.3f, 0.7f), 3, 2, Ramp.Ember, 1.3f, 500, 1, true);
                if (en.Rng.Next(7) == 0) en.Puff(p, en.R(0, 360), en.R(10, 40), en.R(0.7f, 1.2f), 26, 90, 0.16f, -40, 70);
            }
        });
        Fx.After(ms, () => { Explosion(e, to, Ramp.Fire, 0.8); land(); });
    }
}
