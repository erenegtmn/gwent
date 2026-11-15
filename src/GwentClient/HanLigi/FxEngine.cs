using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GwentClient.HanLigi;

/// <summary>
/// Yazılımsal ışık/parçacık motoru. WPF katmanları ışığı üst üste koyamaz (yalnız "üstüne çizer"); bu motor ışığı yarım
/// çözünürlüklü bir tamponda <b>toplar</b> (eklemeli harmanlama), üstüne çeyrek çözünürlükte bulanıklaştırılmış bir
/// <b>parıltı (bloom)</b> ekler, sonra yumuşak doygunlukla (1 − e^−x) renge çevirir. Böylece ateş, büyü ve kıvılcım gerçekten
/// parlar: üst üste binen ışık beyaza yanar, çevresine hâle yayar. Ayrı bir <b>duman</b> tamponu zemini karartır (ışık
/// eklemekle yapılamayan tek şey).
/// İki çizim yolu var: fizikli <see cref="Particle"/>'lar ve her karede doğrudan ışık çizen <see cref="Painter"/>'lar
/// (halka, kubbe, şimşek gibi biçimli ışıklar).
/// Ekran koordinatı 1920×1080; tampon 960×540, görüntü ekranı kaplayacak biçimde büyütülür.
/// </summary>
public sealed class FxEngine : Image
{
    public const int W = 960, H = 540;
    private const int Q = 4, QW = W / Q, QH = H / Q + 1;   // parıltı tamponu (ekranın 1/8'i)
    private const float Scale = 0.5f;   // ekran → tampon
    private const int BloomPad = 7;     // parıltının taştığı çeyrek hücre sayısı
    private const float Exposure = 1.45f, BloomStrength = 1.1f;

    public readonly record struct Rgb(float R, float G, float B)
    {
        public static Rgb operator *(Rgb c, float k) => new(c.R * k, c.G * k, c.B * k);
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    /// <summary>Renk rampası: ömür boyunca üç durak (başlangıç, orta, son).</summary>
    public sealed record Ramp(float R0, float G0, float B0, float R1, float G1, float B1, float R2, float G2, float B2)
    {
        public static readonly Ramp Fire = new(1.0f, 0.86f, 0.55f, 1.0f, 0.38f, 0.05f, 0.32f, 0.03f, 0.0f);
        public static readonly Ramp HotCore = new(1.0f, 1.0f, 0.9f, 1.0f, 0.8f, 0.35f, 1.0f, 0.3f, 0.02f);
        public static readonly Ramp Ember = new(1.0f, 0.8f, 0.35f, 1.0f, 0.38f, 0.04f, 0.5f, 0.06f, 0.0f);
        public static readonly Ramp Aard = new(0.9f, 0.97f, 1.0f, 0.4f, 0.7f, 1.0f, 0.06f, 0.18f, 0.45f);
        public static readonly Ramp Quen = new(1.0f, 0.96f, 0.75f, 1.0f, 0.72f, 0.18f, 0.45f, 0.22f, 0.02f);
        public static readonly Ramp Yrden = new(0.97f, 0.85f, 1.0f, 0.62f, 0.25f, 1.0f, 0.18f, 0.02f, 0.4f);
        public static readonly Ramp Ice = new(1.0f, 1.0f, 1.0f, 0.55f, 0.85f, 1.0f, 0.1f, 0.3f, 0.6f);
        public static readonly Ramp Toxic = new(0.85f, 1.0f, 0.7f, 0.3f, 0.95f, 0.3f, 0.02f, 0.28f, 0.04f);
        public static readonly Ramp Blood = new(1.0f, 0.5f, 0.5f, 0.8f, 0.06f, 0.1f, 0.22f, 0.0f, 0.02f);
        public static readonly Ramp Gold = new(1.0f, 1.0f, 0.85f, 1.0f, 0.78f, 0.28f, 0.5f, 0.26f, 0.02f);
        public static readonly Ramp Dust = new(0.5f, 0.42f, 0.32f, 0.3f, 0.24f, 0.17f, 0.0f, 0.0f, 0.0f);
        public static readonly Ramp White = new(1, 1, 1, 0.85f, 0.9f, 1.0f, 0.15f, 0.15f, 0.2f);
        /// <summary>Duman: renk kullanılmaz, zemini karartır.</summary>
        public static readonly Ramp Smoke = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        public Rgb At(float t)
        {
            if (t < 0.5f) { var u = t * 2; return new(R0 + (R1 - R0) * u, G0 + (G1 - G0) * u, B0 + (B1 - B0) * u); }
            var v = (t - 0.5f) * 2;
            return new(R1 + (R2 - R1) * v, G1 + (G2 - G1) * v, B1 + (B2 - B1) * v);
        }
    }

    public struct Particle
    {
        public float X, Y, VX, VY, AX, AY, Drag, Age, Life, S0, S1, Energy;
        /// <summary>Rastgele savrulma (px/sn²): alev ve duman dalgalanır.</summary>
        public float Turb;
        public Ramp Ramp;
        /// <summary>true: hareket yönünde uzatılmış iz (kıvılcım).</summary>
        public bool Streak;
        /// <summary>true: ışık değil duman (zemini karartır).</summary>
        public bool Smoke;
        /// <summary>0: yuvarlak; &gt;0: yatay elips (dikey/yatay oranı) — zemine düşen ışık havuzu.</summary>
        public float Squash;
    }

    /// <summary>Zamana yayılan yayıcı: <c>Pos(t)</c> noktasında saniyede <c>Rate</c> parçacık doğurur.</summary>
    public sealed class Emitter
    {
        public required Func<double, Point> Pos { get; init; }
        public double Duration { get; init; }
        public double Rate { get; init; }
        public required Action<FxEngine, Point, double> Spawn { get; init; }
        public double Delay { get; init; }
        internal double T, Debt;
    }

    /// <summary>Her karede doğrudan ışık çizen biçim (halka, kubbe, şimşek). <c>Draw(motor, t 0..1, geçen sn)</c>.</summary>
    public sealed class Painter
    {
        public double Duration { get; init; }
        public double Delay { get; init; }
        public required Action<FxEngine, float, float> Draw { get; init; }
        internal double T;
    }

    private readonly WriteableBitmap _wb = new(W, H, 96, 96, PixelFormats.Pbgra32, null);
    private readonly float[] _r = new float[W * H], _g = new float[W * H], _b = new float[W * H], _s = new float[W * H];
    private readonly float[] _qr = new float[QW * QH], _qg = new float[QW * QH], _qb = new float[QW * QH];
    private readonly float[] _tr = new float[QW * QH], _tg = new float[QW * QH], _tb = new float[QW * QH];
    private readonly int[] _px = new int[W * H];
    private readonly List<Particle> _ps = new();
    private readonly List<Emitter> _em = new();
    private readonly List<Painter> _pa = new();
    private readonly Stopwatch _clock = new();
    private readonly byte[] _lut = new byte[4096];
    private static readonly Dictionary<int, float[]> Kernels = new();
    private Int32Rect _prevAcc = Int32Rect.Empty, _prevDisp = Int32Rect.Empty;
    private int _x0, _y0, _x1, _y1;   // bu karede dokunulan bölge
    private bool _hooked;
    private double _last;
    public Random Rng { get; } = new();
    /// <summary>Ekran görüntüsü üretimi: kendi döngüsüne bağlanmaz, zaman <see cref="Advance"/> ile elle ilerletilir.</summary>
    public static bool Manual { get; set; }

    /// <summary>Zamanı elle ilerletir (60 kare/sn adımlarla).</summary>
    public void Advance(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += 1 / 60.0) Step(Math.Min(1 / 60.0, seconds - t));
    }
    public int Count => _ps.Count;

    public FxEngine()
    {
        Source = _wb;
        Width = 1920;
        Height = 1080;
        Stretch = Stretch.Fill;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        for (var i = 0; i < _lut.Length; i++) _lut[i] = (byte)Math.Round(255 * (1 - Math.Exp(-i / 256.0 * Exposure)));
        Unloaded += (_, _) => Unhook();
    }

    // ---------- doğurma ----------

    public void Add(Particle p)
    {
        p.X *= Scale; p.Y *= Scale; p.VX *= Scale; p.VY *= Scale; p.AX *= Scale; p.AY *= Scale; p.S0 *= Scale; p.S1 *= Scale; p.Turb *= Scale;
        _ps.Add(p);
        Hook();
    }

    public void Add(Emitter e)
    {
        _em.Add(e);
        Hook();
    }

    public void Add(Painter p)
    {
        _pa.Add(p);
        Hook();
    }

    public float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    /// <summary>Tek parçacık: konum, hız (px/sn), ömür (sn), boyut (başlangıç → son, px), enerji.</summary>
    public void Spark(Point at, double angleDeg, float speed, float life, float s0, float s1, Ramp ramp, float energy = 1, float gravity = 0, float drag = 0, bool streak = false, float turb = 0)
    {
        var a = angleDeg * Math.PI / 180;
        Add(new Particle
        {
            X = (float)at.X, Y = (float)at.Y, VX = (float)(Math.Cos(a) * speed), VY = (float)(Math.Sin(a) * speed), AY = gravity, Drag = drag,
            Life = life, S0 = s0, S1 = s1, Energy = energy, Ramp = ramp, Streak = streak, Turb = turb, Smoke = ReferenceEquals(ramp, Ramp.Smoke)
        });
    }

    /// <summary>Duman bulutu: zemini karartan, büyüyerek yükselen yumuşak leke.</summary>
    public void Puff(Point at, double angleDeg, float speed, float life, float s0, float s1, float opacity, float rise = -40, float turb = 60)
        => Spark(at, angleDeg, speed, life, s0, s1, Ramp.Smoke, opacity, rise, 1.2f, false, turb);

    /// <summary>Durağan ışık: <paramref name="squash"/> &gt; 0 ise zemine düşen basık elips havuz, 0 ise havada yuvarlak hâle.</summary>
    public void Light(Point at, float size, Ramp ramp, float energy, float life, float squash = 0.36f)
        => Add(new Particle { X = (float)at.X, Y = (float)at.Y, Life = life, S0 = size, S1 = size * 1.08f, Energy = energy, Ramp = ramp, Squash = squash });

    // ---------- doğrudan ışık çizimi (Painter içinden; ekran koordinatı) ----------

    /// <summary>Bir noktaya yumuşak ışık.</summary>
    public void Glow(Point at, float size, Rgb c, float energy) => Stamp((float)at.X * Scale, (float)at.Y * Scale, size * Scale, c.R * energy, c.G * energy, c.B * energy);

    /// <summary>İki nokta arasında ışıklı çizgi (ışık lekeleri sık aralıkla dizilir).</summary>
    public void Line(Point a, Point b, float size, Rgb c, float energy)
    {
        var len = (float)(b - a).Length * Scale;
        var r = Math.Max(1f, size * Scale * 0.5f);
        var n = Math.Clamp((int)(len / (r * 0.6f)) + 1, 1, 400);
        var k = energy / MathF.Max(1, n * r * 0.6f / MathF.Max(1, len)) ;
        for (var i = 0; i <= n; i++)
        {
            var f = i / (float)n;
            Glow(new Point(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f), size, c, k);
        }
    }

    /// <summary>Zemin perspektifinde elips (yatay yarıçap rx, dikey ry); <paramref name="from"/>..<paramref name="to"/> derece arası.</summary>
    public void Ellipse(Point center, double rx, double ry, float size, Rgb c, float energy, double from = 0, double to = 360, Func<double, float>? mod = null)
    {
        var circ = Math.PI * (rx + ry) * Math.Abs(to - from) / 360;
        var n = Math.Clamp((int)(circ * Scale / Math.Max(1, size * Scale * 0.3)), 8, 900);
        for (var i = 0; i <= n; i++)
        {
            var d = from + (to - from) * i / n;
            var a = d * Math.PI / 180;
            var k = mod?.Invoke(d) ?? 1;
            if (k <= 0.01f) continue;
            Glow(new Point(center.X + Math.Cos(a) * rx, center.Y + Math.Sin(a) * ry), size, c, energy * k);
        }
    }

    /// <summary>Kırık çizgi şimşek (orta nokta kaydırma + dallar). Nokta listesi döner, aynı karede çizer.</summary>
    public void Bolt(Point a, Point b, float size, Rgb c, float energy, double jag = 0.22, int depth = 5, bool branches = true)
    {
        var pts = new List<Point> { a, b };
        var amp = (b - a).Length * jag;
        for (var d = 0; d < depth; d++)
        {
            var next = new List<Point>(pts.Count * 2) { pts[0] };
            for (var i = 1; i < pts.Count; i++)
            {
                var p0 = pts[i - 1];
                var p1 = pts[i];
                var m = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                var dir = p1 - p0;
                var nrm = new Vector(-dir.Y, dir.X);
                if (nrm.Length > 0) nrm /= nrm.Length;
                m += nrm * (Rng.NextDouble() * 2 - 1) * amp;
                next.Add(m);
                next.Add(p1);
                if (branches && d < 2 && Rng.Next(4) == 0)
                {
                    var bend = dir;
                    bend = new Vector(bend.X * Math.Cos(0.6) - bend.Y * Math.Sin(0.6) * (Rng.Next(2) * 2 - 1), bend.X * Math.Sin(0.6) * (Rng.Next(2) * 2 - 1) + bend.Y * Math.Cos(0.6));
                    Bolt(m, m + bend * 0.6, size * 0.6f, c, energy * 0.55f, jag, depth - d - 1, false);
                }
            }
            pts = next;
            amp *= 0.55;
        }
        var white = new Rgb(1, 1, 1);
        for (var i = 1; i < pts.Count; i++)
        {
            Line(pts[i - 1], pts[i], size * 3.2f, c, energy * 0.35f);   // dış hâle
            Line(pts[i - 1], pts[i], size, Rgb.Lerp(c, white, 0.75f), energy);   // beyaz çekirdek
        }
    }

    // ---------- döngü ----------

    private void Hook()
    {
        if (_hooked || Manual) return;
        _hooked = true;
        if (!_clock.IsRunning) _clock.Start();
        _last = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    public void Clear()
    {
        _ps.Clear();
        _em.Clear();
        _pa.Clear();
        Step(0);
    }

    /// <summary>Ölçüm: son ölçüm başından beri kare sayısı ve motorda geçen toplam süre (ms).</summary>
    public static int StatFrames;
    public static double StatMs, StatMaxMs;
    private static readonly Stopwatch StatClock = new();

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _last, 0, 0.05);
        _last = now;
        StatClock.Restart();
        Step(dt);
        var ms = StatClock.Elapsed.TotalMilliseconds;
        StatFrames++;
        StatMs += ms;
        if (ms > StatMaxMs) StatMaxMs = ms;
        if (_ps.Count == 0 && _em.Count == 0 && _pa.Count == 0 && _prevDisp.IsEmpty) Unhook();
    }

    /// <summary>Bir zaman adımı: yayıcılar, hareket, ışık tamponu, görüntü.</summary>
    public void Step(double dt)
    {
        for (var i = _em.Count - 1; i >= 0; i--)
        {
            var em = _em[i];
            em.T += dt;
            var t = em.T - em.Delay;
            if (t < 0) continue;
            if (t > em.Duration) { _em.RemoveAt(i); continue; }
            em.Debt += em.Rate * dt;
            while (em.Debt >= 1)
            {
                em.Debt -= 1;
                em.Spawn(this, em.Pos(Math.Clamp(t / Math.Max(1e-6, em.Duration), 0, 1)), t);
            }
        }
        var fdt = (float)dt;
        for (var i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i];
            p.Age += fdt;
            if (p.Age >= p.Life) { _ps[i] = _ps[^1]; _ps.RemoveAt(_ps.Count - 1); continue; }
            var k = 1 - Math.Min(1, p.Drag * fdt);
            var tx = p.Turb > 0 ? ((float)Rng.NextDouble() * 2 - 1) * p.Turb : 0;
            var ty = p.Turb > 0 ? ((float)Rng.NextDouble() * 2 - 1) * p.Turb : 0;
            p.VX = (p.VX + (p.AX + tx) * fdt) * k;
            p.VY = (p.VY + (p.AY + ty) * fdt) * k;
            p.X += p.VX * fdt;
            p.Y += p.VY * fdt;
            _ps[i] = p;
        }
        foreach (var pa in _pa) pa.T += dt;
        Render();
        _pa.RemoveAll(pa => pa.T - pa.Delay > pa.Duration);
    }

    private static float[] Kernel(int r)
    {
        lock (Kernels)
        {
            if (Kernels.TryGetValue(r, out var k)) return k;
            var n = 2 * r + 1;
            k = new float[n * n];
            var rr = (float)(r * r) + 0.5f;
            for (var y = -r; y <= r; y++)
                for (var x = -r; x <= r; x++)
                {
                    var d = (x * x + y * y) / rr;
                    k[(y + r) * n + x + r] = d >= 1 ? 0 : (float)Math.Exp(-d * 3.2) * (1 - d * d);
                }
            return Kernels[r] = k;
        }
    }

    private void Render()
    {
        // önceki karede dokunulan bölge temizlenir
        if (!_prevAcc.IsEmpty)
            for (var y = _prevAcc.Y; y < _prevAcc.Y + _prevAcc.Height; y++)
            {
                var o = y * W + _prevAcc.X;
                Array.Clear(_r, o, _prevAcc.Width);
                Array.Clear(_g, o, _prevAcc.Width);
                Array.Clear(_b, o, _prevAcc.Width);
                Array.Clear(_s, o, _prevAcc.Width);
            }
        _x0 = W; _y0 = H; _x1 = -1; _y1 = -1;
        foreach (var p in _ps)
        {
            var t = p.Age / p.Life;
            var size = p.S0 + (p.S1 - p.S0) * t;
            // giriş ve çıkış yumuşak
            var fade = Math.Min(1, t * 8) * (1 - t * t);
            var e = p.Energy * fade;
            if (e <= 0.002f) continue;
            if (p.Smoke) { StampSmoke(p.X, p.Y, size, e * (1 - t * 0.3f)); continue; }
            var c = p.Ramp.At(t);
            if (p.Squash > 0) { StampEllipse(p.X, p.Y, size * 0.5f, size * 0.5f * p.Squash, c.R * e, c.G * e, c.B * e); continue; }
            if (p.Streak)
            {
                var vlen = MathF.Sqrt(p.VX * p.VX + p.VY * p.VY);
                var len = vlen * 0.05f;
                var r = Math.Max(1f, size * 0.5f);
                var steps = Math.Clamp((int)(len / (r * 0.7f)) + 1, 1, 40);
                var per = 1f / MathF.Sqrt(steps);   // uzun iz toplamda aşırı parlamasın
                for (var s = 0; s < steps; s++)
                {
                    var f = steps == 1 ? 0 : s / (float)(steps - 1);
                    var px = p.X - p.VX * 0.05f * f;
                    var py = p.Y - p.VY * 0.05f * f;
                    var k = e * (1 - f * 0.75f) * (steps == 1 ? 1 : per * 1.6f);
                    Stamp(px, py, size * (1 - f * 0.4f), c.R * k, c.G * k, c.B * k);
                }
            }
            else Stamp(p.X, p.Y, size, c.R * e, c.G * e, c.B * e);
        }
        foreach (var pa in _pa)
        {
            var el = pa.T - pa.Delay;
            if (el < 0) continue;
            pa.Draw(this, (float)Math.Clamp(el / Math.Max(1e-6, pa.Duration), 0, 1), (float)el);
        }
        var cur = _x1 < _x0 ? Int32Rect.Empty : new Int32Rect(_x0, _y0, _x1 - _x0 + 1, _y1 - _y0 + 1);
        var disp = Bloom(cur);
        var reg = Union(_prevDisp, disp);
        if (!reg.IsEmpty) Tonemap(reg);
        _prevAcc = cur;
        _prevDisp = disp;
    }

    /// <summary>Çeyrek çözünürlükte ışığı bulanıklaştırır; görüntülenecek bölgeyi (taşmayla) döndürür.</summary>
    private Int32Rect Bloom(Int32Rect cur)
    {
        Array.Clear(_qr);
        Array.Clear(_qg);
        Array.Clear(_qb);
        if (cur.IsEmpty) return Int32Rect.Empty;
        var qx0 = cur.X / Q;
        var qy0 = cur.Y / Q;
        var qx1 = Math.Min(QW - 1, (cur.X + cur.Width - 1) / Q);
        var qy1 = Math.Min(QH - 1, (cur.Y + cur.Height - 1) / Q);
        const float inv = 1f / (Q * Q);
        for (var qy = qy0; qy <= qy1; qy++)
            for (var qx = qx0; qx <= qx1; qx++)
            {
                float sr = 0, sg = 0, sb = 0;
                for (var dy = 0; dy < Q; dy++)
                {
                    var y = qy * Q + dy;
                    if (y >= H) break;
                    var o = y * W + qx * Q;
                    for (var dx = 0; dx < Q; dx++) { sr += _r[o + dx]; sg += _g[o + dx]; sb += _b[o + dx]; }
                }
                var qi = qy * QW + qx;
                _qr[qi] = sr * inv; _qg[qi] = sg * inv; _qb[qi] = sb * inv;
            }
        var bx0 = Math.Max(0, qx0 - BloomPad);
        var by0 = Math.Max(0, qy0 - BloomPad);
        var bx1 = Math.Min(QW - 1, qx1 + BloomPad);
        var by1 = Math.Min(QH - 1, qy1 + BloomPad);
        // iki geçişli kutu bulanıklığı ≈ Gauss
        System.Threading.Tasks.Parallel.Invoke(
            () => { for (var pass = 0; pass < 2; pass++) { BoxH(_qr, _tr, bx0, by0, bx1, by1, 3); BoxV(_tr, _qr, bx0, by0, bx1, by1, 3); } },
            () => { for (var pass = 0; pass < 2; pass++) { BoxH(_qg, _tg, bx0, by0, bx1, by1, 3); BoxV(_tg, _qg, bx0, by0, bx1, by1, 3); } },
            () => { for (var pass = 0; pass < 2; pass++) { BoxH(_qb, _tb, bx0, by0, bx1, by1, 3); BoxV(_tb, _qb, bx0, by0, bx1, by1, 3); } });
        var x0 = bx0 * Q;
        var y0 = by0 * Q;
        var x1 = Math.Min(W - 1, bx1 * Q + Q - 1);
        var y1 = Math.Min(H - 1, by1 * Q + Q - 1);
        x0 = Math.Min(x0, cur.X); y0 = Math.Min(y0, cur.Y);
        x1 = Math.Max(x1, cur.X + cur.Width - 1); y1 = Math.Max(y1, cur.Y + cur.Height - 1);
        return new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static void BoxH(float[] src, float[] dst, int x0, int y0, int x1, int y1, int r)
    {
        var k = 1f / (2 * r + 1);
        for (var y = y0; y <= y1; y++)
        {
            var o = y * QW;
            float acc = 0;
            for (var x = x0 - r; x <= x0 + r; x++) if (x >= x0 && x <= x1) acc += src[o + x];
            for (var x = x0; x <= x1; x++)
            {
                dst[o + x] = acc * k;
                var add = x + r + 1;
                var sub = x - r;
                if (add <= x1) acc += src[o + add];
                if (sub >= x0) acc -= src[o + sub];
            }
        }
    }

    private static void BoxV(float[] src, float[] dst, int x0, int y0, int x1, int y1, int r)
    {
        var k = 1f / (2 * r + 1);
        for (var x = x0; x <= x1; x++)
        {
            float acc = 0;
            for (var y = y0 - r; y <= y0 + r; y++) if (y >= y0 && y <= y1) acc += src[y * QW + x];
            for (var y = y0; y <= y1; y++)
            {
                dst[y * QW + x] = acc * k;
                var add = y + r + 1;
                var sub = y - r;
                if (add <= y1) acc += src[add * QW + x];
                if (sub >= y0) acc -= src[sub * QW + x];
            }
        }
    }

    // parıltının yatay örnekleme tablosu (her sütun için sol hücre ve ağırlık)
    private static readonly int[] BloomQx = new int[W];
    private static readonly float[] BloomWx = new float[W];

    static FxEngine()
    {
        for (var x = 0; x < W; x++)
        {
            var fx = (x + 0.5f) / Q - 0.5f;
            var qx = Math.Clamp((int)MathF.Floor(fx), 0, QW - 2);
            BloomQx[x] = qx;
            BloomWx[x] = Math.Clamp(fx - qx, 0, 1);
        }
    }

    private void Tonemap(Int32Rect reg)
    {
        void Row(int y)
        {
            var o = y * W;
            var fy = (y + 0.5f) / Q - 0.5f;
            var qy = Math.Clamp((int)MathF.Floor(fy), 0, QH - 2);
            var wy = Math.Clamp(fy - qy, 0, 1);
            var r0 = qy * QW;
            var r1 = r0 + QW;
            var lut = _lut;
            for (var x = reg.X; x < reg.X + reg.Width; x++)
            {
                var i = o + x;
                var qx = BloomQx[x];
                var wx = BloomWx[x];
                var a00 = r0 + qx;
                var a10 = r1 + qx;
                float w00 = (1 - wx) * (1 - wy) * BloomStrength, w01 = wx * (1 - wy) * BloomStrength, w10 = (1 - wx) * wy * BloomStrength, w11 = wx * wy * BloomStrength;
                var lr = _r[i] + _qr[a00] * w00 + _qr[a00 + 1] * w01 + _qr[a10] * w10 + _qr[a10 + 1] * w11;
                var lg = _g[i] + _qg[a00] * w00 + _qg[a00 + 1] * w01 + _qg[a10] * w10 + _qg[a10 + 1] * w11;
                var lb = _b[i] + _qb[a00] * w00 + _qb[a00 + 1] * w01 + _qb[a10] * w10 + _qb[a10 + 1] * w11;
                var sm = _s[i];
                if (lr < 0.002f && lg < 0.002f && lb < 0.002f && sm <= 0) { _px[i] = 0; continue; }
                int r = lut[Math.Min(4095, (int)(lr * 256))];
                int g = lut[Math.Min(4095, (int)(lg * 256))];
                int b = lut[Math.Min(4095, (int)(lb * 256))];
                var a = Math.Max(r, Math.Max(g, b));
                if (sm > 0)
                {
                    var sa = (int)(255 * Math.Min(0.82f, 1 - MathF.Exp(-sm * 1.2f)));
                    if (sa > a) a = sa;
                }
                _px[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }
        if ((long)reg.Width * reg.Height > 40000)
            System.Threading.Tasks.Parallel.For(reg.Y, reg.Y + reg.Height, Row);
        else
            for (var y = reg.Y; y < reg.Y + reg.Height; y++) Row(y);
        _wb.WritePixels(reg, _px, W * 4, reg.X, reg.Y);
    }

    private void Touch(int ax, int ay, int bx, int by)
    {
        if (ax < _x0) _x0 = ax;
        if (ay < _y0) _y0 = ay;
        if (bx > _x1) _x1 = bx;
        if (by > _y1) _y1 = by;
    }

    private void Stamp(float fx, float fy, float size, float cr, float cg, float cb)
    {
        var r = Math.Clamp((int)(size * 0.5f + 0.5f), 1, 110);
        var k = Kernel(r);
        var n = 2 * r + 1;
        var cx = (int)MathF.Round(fx);
        var cy = (int)MathF.Round(fy);
        var ax = Math.Max(0, cx - r);
        var bx = Math.Min(W - 1, cx + r);
        var ay = Math.Max(0, cy - r);
        var by = Math.Min(H - 1, cy + r);
        if (ax > bx || ay > by) return;
        // küçük parçacıklar ölçek kaybını telafi eder (aynı enerji, daha az alan)
        var boost = r <= 1 ? 2.4f : r <= 2 ? 1.7f : 1f;
        cr *= boost; cg *= boost; cb *= boost;
        for (var y = ay; y <= by; y++)
        {
            var ko = (y - cy + r) * n - cx + r;
            var o = y * W;
            for (var x = ax; x <= bx; x++)
            {
                var w = k[ko + x];
                if (w <= 0) continue;
                var i = o + x;
                _r[i] += cr * w;
                _g[i] += cg * w;
                _b[i] += cb * w;
            }
        }
        Touch(ax, ay, bx, by);
    }

    /// <summary>Basık elips ışık (zemin havuzu); yarıçaplar tampon pikselinde.</summary>
    private void StampEllipse(float fx, float fy, float rx, float ry, float cr, float cg, float cb)
    {
        rx = Math.Clamp(rx, 2, 420);
        ry = Math.Clamp(ry, 2, 260);
        var cx = (int)fx;
        var cy = (int)fy;
        var ax = Math.Max(0, (int)(cx - rx));
        var bx = Math.Min(W - 1, (int)(cx + rx));
        var ay = Math.Max(0, (int)(cy - ry));
        var by = Math.Min(H - 1, (int)(cy + ry));
        if (ax > bx || ay > by) return;
        var ix = 1f / (rx * rx);
        var iy = 1f / (ry * ry);
        for (var y = ay; y <= by; y++)
        {
            var dy = (y - cy) * (y - cy) * iy;
            if (dy >= 1) continue;
            var o = y * W;
            for (var x = ax; x <= bx; x++)
            {
                var d = (x - cx) * (x - cx) * ix + dy;
                if (d >= 1) continue;
                var w = MathF.Exp(-d * 3.2f) * (1 - d * d);
                _r[o + x] += cr * w;
                _g[o + x] += cg * w;
                _b[o + x] += cb * w;
            }
        }
        Touch(ax, ay, bx, by);
    }

    private void StampSmoke(float fx, float fy, float size, float e)
    {
        var r = Math.Clamp((int)(size * 0.5f + 0.5f), 1, 110);
        var k = Kernel(r);
        var n = 2 * r + 1;
        var cx = (int)fx;
        var cy = (int)fy;
        var ax = Math.Max(0, cx - r);
        var bx = Math.Min(W - 1, cx + r);
        var ay = Math.Max(0, cy - r);
        var by = Math.Min(H - 1, cy + r);
        if (ax > bx || ay > by) return;
        for (var y = ay; y <= by; y++)
        {
            var ko = (y - cy + r) * n - cx + r;
            var o = y * W;
            for (var x = ax; x <= bx; x++) _s[o + x] += e * k[ko + x];
        }
        Touch(ax, ay, bx, by);
    }

    private static Int32Rect Union(Int32Rect a, Int32Rect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x0 = Math.Min(a.X, b.X);
        var y0 = Math.Min(a.Y, b.Y);
        var x1 = Math.Max(a.X + a.Width, b.X + b.Width);
        var y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }
}
