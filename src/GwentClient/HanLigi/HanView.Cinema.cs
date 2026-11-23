using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;
using HTu = HanLigi.Tuning;
using Ramp = GwentClient.HanLigi.FxEngine.Ramp;

namespace GwentClient.HanLigi;

/// <summary>
/// Savaş sinematiği: satır puanlama (kartlar soldan sağa sayılır, değerleri satır rozetine akar, yüzdeler ayrı vuruş olarak
/// iner, iki rozet ortada çarpışır), çözülmüş satırın kalıcı işareti (kazanana flama, kaybedene kırık mühür), hasar (kazananın
/// kartlarından kaybedenin portresine kıvılcımlar; portre çatlar), pusu inişi (kart masaya çakılır, toz halkası) ve rakibin
/// açılmamış satırlarında süzülen sis (satır açılınca iki yana savrulur).
/// <para>Hepsi ekranı baştan kurmadan oynar: geçici öğeler önbelleksiz <c>_fx</c> katmanında (tek tek GPU dokusu), ışık ve
/// parçacıklar motorda. Masa puanlamanın başında ve sonunda birer kez çizilir.</para>
/// </summary>
public sealed partial class HanView
{
    // pencere gösteriminin parçaları (ms): seçimler, satırın açılışı, sonuçtan sonra bekleme. Hepsi (adımlar ve puanlama
    // dahil) HanTimes.WindowShow içinde kalır: online'da sunucu pencere sayacını o süre sonra başlatır.
    private const int OpenMs = 900, RevealMs = 1000, VerdictMs = 1200;
    private static int ShowBudgetMs => HanTimes.WindowShow * 1000 - 250;
    // puanlama: sayı jetonunun uçuşu, yüzde vuruşu, rozetlerin yaklaşması, çarpışmada duraklama, geri dönüş
    private const double TokenMs = 280, ModMs = 380, ApproachMs = 230, StopMs = 90, ReturnMs = 380;

    /// <summary>Puanlamanın ekrandaki geçici öğeleri (rozet kopyaları, sayı jetonları, yüzde çipleri): sonuçta kaldırılır.</summary>
    private readonly List<FrameworkElement> _cine = new();
    /// <summary>Bu pencerede pusudan inecek kartlar: inişleri oynayana kadar masada görünmez.</summary>
    private readonly HashSet<int> _ambushHidden = new();
    /// <summary>Rakibin satırı bu çizimde açıldı: kartları bir kez iner (adımlar ekranı yeniden çizince yeniden inmesin).</summary>
    private bool _revealFresh;
    /// <summary>Satır işaretlerinin (flama / mühür) ekrandaki yerleri: (satır sırası, benim mi).</summary>
    private readonly Dictionary<(int, bool), Point> _markAt = new();

    // ---------- satır puanlama ----------

    private static (int Cards, int Mods) TallySize(WindowOutcome o, Battle b) =>
        (Math.Max(b.Sides[0].In(o.Row).Count(), b.Sides[1].In(o.Row).Count()), Math.Max(o.RowMods.Count(m => m.Side == 0), o.RowMods.Count(m => m.Side == 1)));

    /// <summary>Ardışık iki kartın sayılması arası (ms): kart çoksa sıklaşır, satır ~1 sn'de sayılır.</summary>
    private static double TickGap(int n) => n <= 1 ? 0 : Math.Min(150, 900.0 / n);

    /// <summary>Puanlamanın tam hızdaki süresi (ms): sayım, yüzdeler, çarpışma.</summary>
    private static double TallyMs(WindowOutcome o, Battle b)
    {
        var (n, m) = TallySize(o, b);
        var tally = 150 + (n > 0 ? (n - 1) * TickGap(n) + TokenMs + 140 : 200);
        return tally + m * ModMs + (m > 0 ? 120 : 0) + ApproachMs + StopMs + ReturnMs;
    }

    private static Point BadgeAt(bool mine, Row r) => HanScene.ToScreen(new Point(RowX + RowW - 36, RowY(mine, r) + RowH / 2));

    private static string PctText(double p) => (p < 0 ? "−" : "+") + "%" + Math.Round(Math.Abs(p) * 100);

    /// <summary>Puanlamada satır rozetinin kopyası: sayı her eklemede yenilenir, vuruşta sıçrar, çarpışmada yer değiştirir.</summary>
    private sealed class BadgeFx
    {
        public required Grid Holder;
        public required ScaleTransform Scale;
        public required RotateTransform Turn;
        public required TranslateTransform Move;
        public required bool Mine;
        public required Point At;
        public decimal Value;
        public FrameworkElement? Chip;
    }

    private BadgeFx CineBadge(bool mine, Point at)
    {
        var scale = new ScaleTransform(1, 1);
        var turn = new RotateTransform(0);
        var move = new TranslateTransform();
        var h = new Grid
        {
            Width = 54, Height = 54, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, turn, move } },
            CacheMode = new BitmapCache(Math.Clamp(CacheScale * 1.4, 0.5, 3)) { SnapsToDevicePixels = false }
        };
        Canvas.SetLeft(h, at.X - 27);
        Canvas.SetTop(h, at.Y - 27);
        System.Windows.Controls.Panel.SetZIndex(h, 60);
        _fx.Children.Add(h);
        _cine.Add(h);
        var b = new BadgeFx { Holder = h, Scale = scale, Turn = turn, Move = move, Mine = mine, At = at };
        SetBadge(b, 0, false);
        return b;
    }

    private static void SetBadge(BadgeFx b, decimal v, bool strong)
    {
        b.Value = v;
        b.Holder.Children.Clear();
        b.Holder.Children.Add(ScoreBadge(HTu.Fmt(v), b.Mine, strong));
    }

    private static void Punch(BadgeFx b, double peak = 1.22, int ms = 180)
    {
        var a = new DoubleAnimation(peak, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        b.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        b.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void DropCine(FrameworkElement el)
    {
        _fx.Children.Remove(el);
        _cine.Remove(el);
    }

    /// <summary>Kartın değeri (sayı jetonu) karttan rozete uçar: hızlanarak iner, değince <paramref name="land"/>.</summary>
    private void Token(string text, Point from, Point to, double ms, Brush color, int gen, Action land)
    {
        var t = HanUi.Number(text, 31, color);
        t.IsHitTestVisible = false;
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 1 };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (w, h) = (t.DesiredSize.Width, t.DesiredSize.Height);
        var sc = new ScaleTransform(1.2, 1.2, w / 2, h / 2);
        var move = new TranslateTransform(from.X - w / 2, from.Y - h / 2);
        t.RenderTransform = new TransformGroup { Children = { sc, move } };
        t.CacheMode = new BitmapCache(Math.Clamp(CacheScale * 1.3, 0.5, 3));
        System.Windows.Controls.Panel.SetZIndex(t, 55);
        _fx.Children.Add(t);
        _cine.Add(t);
        var dur = TimeSpan.FromMilliseconds(ms);
        var ein = new CubicEase { EasingMode = EasingMode.EaseIn };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - w / 2, to.X - w / 2, dur) { EasingFunction = ein });
        var ay = new DoubleAnimation(from.Y - h / 2, to.Y - h / 2, dur) { EasingFunction = ein };
        ay.Completed += (_, _) =>
        {
            DropCine(t);
            if (gen == _gen) land();
        };
        move.BeginAnimation(TranslateTransform.YProperty, ay);
        var shrink = new DoubleAnimation(1.2, 0.75, dur) { EasingFunction = ein };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    /// <summary>Satır yüzdesi rozete iner: çip yukarıda büyük belirip rozetin üstüne çakılır; çarpınca <paramref name="hit"/>.</summary>
    private void ModSlam(BadgeFx b, string text, double pct, int gen, Action hit)
    {
        if (b.Chip is { } old)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => DropCine(old);
            old.BeginAnimation(OpacityProperty, fade);
        }
        var chip = HanUi.Chip(HanUi.Upper(text), pct < 0 ? HanUi.Bad : HanUi.Good, 14, true);
        chip.IsHitTestVisible = false;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (w, h) = (chip.DesiredSize.Width, chip.DesiredSize.Height);
        var sc = new ScaleTransform(1.9, 1.9, w / 2, h / 2);
        var move = new TranslateTransform(0, -26);
        chip.RenderTransform = new TransformGroup { Children = { sc, move } };
        chip.CacheMode = new BitmapCache(Math.Clamp(CacheScale * 1.9, 0.5, 4));
        chip.Opacity = 0;
        Canvas.SetLeft(chip, b.At.X - w / 2);
        Canvas.SetTop(chip, b.At.Y - 34 - h < 70 ? b.At.Y + 34 : b.At.Y - 34 - h);
        System.Windows.Controls.Panel.SetZIndex(chip, 62);
        _fx.Children.Add(chip);
        _cine.Add(chip);
        b.Chip = chip;
        var dur = TimeSpan.FromMilliseconds(150);
        var ein = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var down = new DoubleAnimation(1.9, 1, dur) { EasingFunction = ein };
        down.Completed += (_, _) => { if (gen == _gen) hit(); };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.9, 1, dur) { EasingFunction = ein });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-26, 0, dur) { EasingFunction = ein });
        chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
    }

    /// <summary>
    /// Satırın puanlanması (Balatro'daki gibi): kartlar soldan sağa tek tek parlar, değerleri rozete akar ve her tıkta ses inceltir;
    /// satır yüzdeleri (Boynuz, Aard, Yennefer...) ayrı vuruşlar olarak rozete iner; sonra iki rozet ortada çarpışır, kazanan
    /// altın halkayla yerine döner, kaybeden sendeleyerek. Bitince <paramref name="verdict"/> (masa son haliyle çizilir).
    /// <paramref name="cs"/>: hız katsayısı (pencerede müdahale çoksa sinematik kısalır, en çok %35).
    /// </summary>
    private void ScoreRow(WindowOutcome o, Battle b, int gen, double cs, Action verdict)
    {
        var row = o.Row;
        _hold = 3;   // kartlar son değerinde; bu satırın rozetleri sinematikte
        Render();
        foreach (var el in _cine.ToList()) DropCine(el);
        var sides = new[] { Mine, 1 - Mine };
        var badge = sides.ToDictionary(s => s, s => CineBadge(s == Mine, BadgeAt(s == Mine, row)));
        var cards = sides.ToDictionary(s => s, s => b.Sides[s].In(row).Where(u => _pos.ContainsKey(u.Uid)).OrderBy(u => _pos[u.Uid].X).ToList());
        var n = cards.Values.Max(l => l.Count);
        var gap = TickGap(n) * cs;
        var fly = TokenMs * cs;
        const double start = 150;   // ağır çizim karesi geçsin

        // 1) sayım: kartlar soldan sağa, iki taraf birlikte
        for (var k = 0; k < n; k++)
        {
            var kk = k;
            After((int)(start + k * gap), gen, () =>
            {
                foreach (var s in sides)
                {
                    if (kk >= cards[s].Count || CardAt(cards[s][kk].Uid) is not { } c) continue;
                    var v = o.CardValues.GetValueOrDefault(cards[s][kk].Uid);
                    var mine = s == Mine;
                    var ramp = mine ? Ramp.Gold : Ramp.Ember;
                    var bf = badge[s];
                    _engine.Light(c.C, (float)c.W * 1.6f, ramp, 0.55f, 0.32f, 0.7f);
                    Token("+" + HTu.Fmt(v), new Point(c.C.X, c.C.Y - c.H * 0.42), bf.At, fly, mine ? HanUi.GoldText : HanUi.Br(Color.FromRgb(0xff, 0xb0, 0x98)), gen, () =>
                    {
                        SetBadge(bf, bf.Value + v, false);
                        Punch(bf);
                        HanFx.Tick(_engine, bf.At, ramp, 0.8);
                    });
                }
            });
            After((int)(start + k * gap + fly), gen, () => Sfx.PlayPitch("han_tick", Math.Min(kk, 12)));
        }
        var tm = start + (n > 0 ? (n - 1) * gap + fly + 140 * cs : 200);

        // 2) satır yüzdeleri: her biri ayrı vuruş; rozet birikimli değere atlar (sonuncusunda kesin sonuç)
        var mods = sides.ToDictionary(s => s, s => o.RowMods.Where(m => m.Side == s).ToList());
        var mc = mods.Values.Max(l => l.Count);
        var cum = sides.ToDictionary(s => s, _ => 0.0);
        After((int)tm - 10, gen, () => { foreach (var s in sides) if (badge[s].Value != o.RowBase[s]) SetBadge(badge[s], o.RowBase[s], false); });
        for (var j = 0; j < mc; j++)
        {
            var jj = j;
            After((int)(tm + j * ModMs * cs), gen, () =>
            {
                string? sound = null;
                foreach (var s in sides)
                {
                    if (jj >= mods[s].Count) continue;
                    var (_, src, pct) = mods[s][jj];
                    cum[s] += pct;
                    var bf = badge[s];
                    var val = jj == mods[s].Count - 1 ? (s == 0 ? o.ValueA : o.ValueB) : HTu.ApplyPct(o.RowBase[s], cum[s]);
                    ModSlam(bf, $"{src} {PctText(pct)}", pct, gen, () =>
                    {
                        SetBadge(bf, val, false);
                        Punch(bf, 1.32, 240);
                        HanFx.Tick(_engine, bf.At, pct < 0 ? Ramp.Blood : Ramp.Toxic, 1.4);
                    });
                    // kalıcı satır bonusları (Yennefer, Boynuz, Cüce, Geralt): kartların üstünde de ışık
                    if (pct > 0 && src is not "Gök Gürültüsü")
                    {
                        var ramp = src == "Yennefer" ? Ramp.Aard : Ramp.Gold;
                        foreach (var u in cards[s]) if (CardAt(u.Uid) is { } c) HanFx.CardAura(_engine, c.C, c.W, c.H, ramp, 0.9);
                    }
                    sound = src.StartsWith("Boynuz") ? "horn" : sound == "horn" ? sound : pct > 0 ? "han_potion" : sound ?? "han_quen_hit";
                }
                if (sound != null) Sfx.Play(sound);
            });
        }

        // 3) çarpışma: iki rozet ortada buluşur
        var tc = tm + mc * ModMs * cs + (mc > 0 ? 120 * cs : 0);
        var a = badge[Mine].At;
        var z = badge[1 - Mine].At;
        var mid = new Point((a.X + z.X) / 2, (a.Y + z.Y) / 2);
        var w = o.Winner;
        After((int)tc, gen, () =>
        {
            Sfx.Play("han_whoosh");
            foreach (var s in sides)
            {
                var bf = badge[s];
                SetBadge(bf, s == 0 ? o.ValueA : o.ValueB, false);
                if (bf.Chip is { } ch) { var f = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)); f.Completed += (_, _) => DropCine(ch); ch.BeginAnimation(OpacityProperty, f); bf.Chip = null; }
                var dir = bf.At - mid;
                if (dir.Length > 1) dir.Normalize();
                var dest = mid + dir * 25 - bf.At;
                var dur = TimeSpan.FromMilliseconds(ApproachMs * cs);
                var ein = new CubicEase { EasingMode = EasingMode.EaseIn };
                bf.Move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dest.X, dur) { EasingFunction = ein });
                bf.Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dest.Y, dur) { EasingFunction = ein });
            }
        });
        After((int)(tc + ApproachMs * cs), gen, () =>
        {
            HanFx.Clash(_engine, mid, w < 0 ? Ramp.White : w == Mine ? Ramp.Gold : Ramp.Ember);
            Sfx.Play("han_quen_hit");
            foreach (var s in sides) Punch(badge[s], 1.12, 90);
        });
        After((int)(tc + (ApproachMs + StopMs) * cs), gen, () =>
        {
            foreach (var s in sides)
            {
                var bf = badge[s];
                var won = s == w;
                var lost = w >= 0 && !won;
                var dur = TimeSpan.FromMilliseconds((lost ? ReturnMs : 300) * cs);
                IEasingFunction ease = lost ? new CubicEase { EasingMode = EasingMode.EaseOut } : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
                bf.Move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
                bf.Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
                if (won)
                {
                    SetBadge(bf, bf.Value, true);
                    Punch(bf, 1.3, 260);
                }
                else if (lost)
                {
                    // sendeler: bir yana yatıp toparlanır
                    var wob = new DoubleAnimationUsingKeyFrames { Duration = dur };
                    foreach (var (deg, at) in new[] { (0.0, 0.0), (-16.0, 0.25), (9.0, 0.5), (-4.0, 0.75), (0.0, 1.0) })
                        wob.KeyFrames.Add(new LinearDoubleKeyFrame(deg, KeyTime.FromPercent(at)));
                    bf.Turn.BeginAnimation(RotateTransform.AngleProperty, wob);
                }
            }
        });
        After((int)(tc + (ApproachMs + StopMs + ReturnMs) * cs), gen, () =>
        {
            foreach (var el in _cine.ToList()) DropCine(el);
            verdict();
        });
    }

    // ---------- çözülmüş satırın işareti ----------

    /// <summary>
    /// Çözülmüş satırın sıra numarası yerine işaret: kazanana flama (yeşil: senin, kızıl: rakibin), kaybedene kırık mühür.
    /// <paramref name="k"/>: boy katsayısı (arkadaki satırlar küçük).
    /// </summary>
    private static FrameworkElement RowMark(bool won, bool mine, int number, double k)
    {
        var c = new Canvas { Width = 50, Height = 66, IsHitTestVisible = false };
        var box = new Viewbox { Width = 50 * k, Height = 66 * k, Child = c, IsHitTestVisible = false };
        TextBlock Num(Brush fg, double size)
        {
            var t = HanUi.Number(number.ToString(), size, fg);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return t;
        }
        if (won)
        {
            var cloth = mine
                ? new LinearGradientBrush(Color.FromRgb(0x46, 0x86, 0x4a), Color.FromRgb(0x1a, 0x3c, 0x1f), 90)
                : new LinearGradientBrush(Color.FromRgb(0xa4, 0x38, 0x2a), Color.FromRgb(0x4c, 0x14, 0x10), 90);
            c.Children.Add(new Path
            {
                Data = Geometry.Parse("M7,8 L43,8 L43,61 L25,50 L7,61 Z"), Fill = cloth, Stroke = HanUi.Br(Color.FromRgb(0xd8, 0xb4, 0x5e)), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 7, ShadowDepth = 2, Opacity = 0.85 }
            });
            c.Children.Add(new Path { Data = Geometry.Parse("M10.5,11 L39.5,11 L39.5,55 L25,46 L10.5,55 Z"), Stroke = HanUi.Br(HanUi.Gold, 0x80), StrokeThickness = 0.8 });
            // asma çubuğu ve topuzları
            c.Children.Add(new Rectangle { Width = 44, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = new LinearGradientBrush(Color.FromRgb(0xf4, 0xdc, 0x96), Color.FromRgb(0x84, 0x5e, 0x1e), 90), Margin = new Thickness(3, 4, 0, 0) });
            foreach (var x in new[] { 0.0, 43.0 })
            {
                var knob = new Ellipse { Width = 7, Height = 7, Fill = new RadialGradientBrush(Color.FromRgb(0xff, 0xec, 0xb0), Color.FromRgb(0x9a, 0x70, 0x26)) };
                Canvas.SetLeft(knob, x);
                Canvas.SetTop(knob, 3);
                c.Children.Add(knob);
            }
            var t = Num(HanUi.GoldText, 22);
            t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
            Canvas.SetLeft(t, 25 - t.DesiredSize.Width / 2);
            Canvas.SetTop(t, 29 - t.DesiredSize.Height / 2);
            c.Children.Add(t);
            return box;
        }
        // kırık mühür: düzensiz kenarlı mum, ortasından çatlak, kenarından kopmuş bir parça
        var wax = mine
            ? new RadialGradientBrush(Color.FromRgb(0x8c, 0x6c, 0x34), Color.FromRgb(0x3a, 0x29, 0x10)) { GradientOrigin = new Point(0.4, 0.35) }
            : new RadialGradientBrush(Color.FromRgb(0x84, 0x30, 0x26), Color.FromRgb(0x38, 0x10, 0x0c)) { GradientOrigin = new Point(0.4, 0.35) };
        var rng = new Random(number * 7 + (mine ? 1 : 0));
        var pts = Enumerable.Range(0, 18).Select(i =>
        {
            var ang = i * Math.PI * 2 / 18;
            var rad = 20 + rng.NextDouble() * 2.6;
            return new Point(25 + Math.Cos(ang) * rad, 35 + Math.Sin(ang) * rad);
        }).ToList();
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        fig.Segments.Add(new PolyLineSegment(pts.Skip(1), true) { IsSmoothJoin = true });
        c.Children.Add(new Path
        {
            Data = new PathGeometry(new[] { fig }), Fill = wax, Stroke = HanUi.Br(Color.FromArgb(0x70, 0, 0, 0)), StrokeThickness = 1, Opacity = 0.9,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.8 }
        });
        var ring = new Ellipse { Width = 30, Height = 30, Stroke = HanUi.Br(Color.FromArgb(0x60, 0, 0, 0)), StrokeThickness = 1.6 };
        Canvas.SetLeft(ring, 10);
        Canvas.SetTop(ring, 20);
        c.Children.Add(ring);
        var n = Num(HanUi.Br(Color.FromArgb(0xc0, 0xe8, 0xdc, 0xc4)), 19);
        Canvas.SetLeft(n, 25 - n.DesiredSize.Width / 2);
        Canvas.SetTop(n, 35 - n.DesiredSize.Height / 2);
        c.Children.Add(n);
        // kopan parça (masa rengi) ve çatlak (koyu yarık + ince açık kenar)
        c.Children.Add(new Path { Data = Geometry.Parse("M42,22 L47,29 L41,31 Z"), Fill = HanUi.Br(Color.FromRgb(0x24, 0x17, 0x0b)) });
        const string crack = "M11,19 L17,25 L14,31 L19,37 L15,44 L21,52";   // sayının solundan: sayı okunur kalsın
        c.Children.Add(new Path { Data = Geometry.Parse(crack), Stroke = HanUi.Br(Color.FromArgb(0xe8, 0x12, 0x08, 0x03)), StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Miter });
        c.Children.Add(new Path { Data = Geometry.Parse(crack), Stroke = HanUi.Br(Color.FromArgb(0x60, 0xff, 0xe0, 0xb0)), StrokeThickness = 0.8, RenderTransform = new TranslateTransform(1.2, 0) });
        return box;
    }

    /// <summary>Yeni çözülen satırın işaretleri belirir: flamada altın kıvılcım, kırık mühürde toz.</summary>
    private void MarkPop(int index, int winnerSide)
    {
        foreach (var mine in new[] { true, false })
        {
            if (!_markAt.TryGetValue((index, mine), out var p)) continue;
            var won = (winnerSide == Mine) == mine;
            if (won)
            {
                _engine.Light(p, 150, mine ? Ramp.Gold : Ramp.Ember, 0.9f, 0.5f, 0.8f);
                for (var i = 0; i < 26; i++) _engine.Spark(p, _engine.R(200, 340), _engine.R(120, 360), _engine.R(0.3f, 0.7f), 3, 2, mine ? Ramp.Gold : Ramp.Ember, 1.2f, 500, 1, true);
            }
            else
                for (var i = 0; i < 10; i++) _engine.Puff(p, _engine.R(0, 360), _engine.R(20, 80), _engine.R(0.6f, 1.0f), 14, 50, 0.2f, -20, 30);
        }
    }

    // ---------- rakibin sisi ----------

    private static readonly Stopwatch FogClock = Stopwatch.StartNew();

    private static Brush FogBase() => new LinearGradientBrush(Color.FromArgb(0xf0, 0x08, 0x0a, 0x10), Color.FromArgb(0xd0, 0x15, 0x18, 0x22), 90);

    /// <summary>
    /// Rakibin açılmamış satırı: koyu zemin (önbellekte) ve üstünde iki ayrı hızda, ters yönlere süzülen sis (canlı katmanda).
    /// Sis saati ekrandan bağımsız akar: ekran yeniden çizilince kaldığı yerden sürer, sıçramaz.
    /// </summary>
    private void LiveFog(Rect board)
    {
        if (!_live || Assets.Ui("wx_fog") is not { } fog) { BoardFog(board, 1); return; }
        Trap(board, FogBase());
        var g = TrapGeometry(board, 3);
        var c = FogStrips(g, fog, true);
        Canvas.SetLeft(c, g.Bounds.X);
        Canvas.SetTop(c, g.Bounds.Y);
        _anim.Children.Add(c);
    }

    /// <summary>Yamuğa kırpılmış sis şeritleri (yerel koordinatlarda); <paramref name="drift"/> ise kayar.</summary>
    private static Canvas FogStrips(Geometry g, ImageSource fog, bool drift)
    {
        var (w, h) = (g.Bounds.Width, g.Bounds.Height);
        var c = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
        var local = g.Clone();
        local.Transform = new TranslateTransform(-g.Bounds.X, -g.Bounds.Y);
        c.Clip = local;
        void Strip(double op, double seconds, bool reverse, double dy)
        {
            var step = w * 0.7;
            var s = new Canvas { Width = w + step * 3, Height = h, IsHitTestVisible = false };
            for (var i = 0; i < 4; i++)
            {
                var im = new Image { Source = fog, Width = w, Height = h * 1.25, Stretch = Stretch.Fill, Opacity = op, IsHitTestVisible = false };
                Canvas.SetLeft(im, i * step);
                Canvas.SetTop(im, dy);
                s.Children.Add(im);
            }
            var tt = new TranslateTransform(reverse ? -step : 0, 0);
            s.RenderTransform = tt;
            if (drift)
            {
                var a = reverse ? new DoubleAnimation(-step, 0, TimeSpan.FromSeconds(seconds)) : new DoubleAnimation(0, -step, TimeSpan.FromSeconds(seconds));
                a.RepeatBehavior = RepeatBehavior.Forever;
                a.BeginTime = -TimeSpan.FromSeconds(FogClock.Elapsed.TotalSeconds % seconds);
                tt.BeginAnimation(TranslateTransform.XProperty, a);
            }
            c.Children.Add(s);
        }
        Strip(0.5, 44, false, -h * 0.15);
        Strip(0.3, 29, true, -h * 0.05);
        return c;
    }

    /// <summary>
    /// Satır açılırken sis ortadan ikiye ayrılıp yanlara savrulur ve söner; satır boyunca duman dağılır. Her şey satırın yamuğu
    /// içinde kalır (yanlara taşmaz); iki yarının ortadaki kenarı yumuşaktır. İlk anda bütün sis üstte durur ve hızla söner:
    /// yarıların üst üste bindiği ortada açıklık görünmesin.
    /// </summary>
    private void FogPart(Rect board)
    {
        if (!_live) return;
        var g = TrapGeometry(board, 3);
        var bb = g.Bounds;
        var fog = Assets.Ui("wx_fog");
        var local = g.Clone();
        local.Transform = new TranslateTransform(-bb.X, -bb.Y);
        var stage = new Canvas { Width = bb.Width, Height = bb.Height, IsHitTestVisible = false, Clip = local };
        Canvas.SetLeft(stage, bb.X);
        Canvas.SetTop(stage, bb.Y);
        System.Windows.Controls.Panel.SetZIndex(stage, 30);
        Canvas Sheet()
        {
            var c = new Canvas { Width = bb.Width, Height = bb.Height, IsHitTestVisible = false };
            c.Children.Add(new Rectangle { Width = bb.Width, Height = bb.Height, Fill = FogBase() });
            if (fog != null)
            {
                var strips = FogStrips(g, fog, false);
                strips.Clip = null;
                c.Children.Add(strips);
            }
            c.CacheMode = new BitmapCache(Math.Clamp(CacheScale, 0.5, 2));
            return c;
        }
        var dur = TimeSpan.FromMilliseconds(720);
        foreach (var left in new[] { true, false })
        {
            var half = Sheet();
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            mask.GradientStops.Add(new GradientStop(left ? Colors.Black : Colors.Transparent, left ? 0.34 : 0.5));
            mask.GradientStops.Add(new GradientStop(left ? Colors.Transparent : Colors.Black, left ? 0.5 : 0.66));
            half.OpacityMask = mask;
            var tt = new TranslateTransform();
            half.RenderTransform = tt;
            stage.Children.Add(half);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, (left ? -1 : 1) * bb.Width * 0.42, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            half.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }
        var whole = Sheet();
        stage.Children.Add(whole);
        whole.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
        _fx.Children.Add(stage);
        Fx.After((int)dur.TotalMilliseconds + 30, () => _fx.Children.Remove(stage));
        var cy = bb.Y + bb.Height * 0.55;
        for (var i = 0; i < 22; i++)
        {
            var x = _engine.R((float)(-bb.Width / 2), (float)(bb.Width / 2));
            _engine.Puff(new Point(bb.X + bb.Width / 2 + x, cy + _engine.R(-20, 20)), x < 0 ? _engine.R(165, 195) : _engine.R(-15, 15), _engine.R(90, 260), _engine.R(0.8f, 1.4f), 40, 130, 0.2f, -10, 40);
        }
    }

    // ---------- pusu inişi ----------

    /// <summary>Pusudaki kart masaya çakılır: yukarıdan büyük ve hızla düşer, değdiği an kısa bir donma, sonra toz halkası ve sarsıntı.</summary>
    private void AmbushSlam(int uid)
    {
        _ambushHidden.Remove(uid);
        Render();
        if (!_unitEls.TryGetValue(uid, out var el) || CardAt(uid) is not { } c) return;
        var foot = new Point(c.C.X, c.C.Y + c.H / 2);
        var sc = new ScaleTransform(1.5, 1.5);
        var mv = new TranslateTransform(0, -95);
        el.RenderTransformOrigin = new Point(0.5, 1);
        el.RenderTransform = new TransformGroup { Children = { sc, mv } };
        var dur = TimeSpan.FromMilliseconds(120);
        var fall = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.5, 1, dur) { EasingFunction = fall });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.5, 1, dur) { EasingFunction = fall });
        mv.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-95, 0, dur) { EasingFunction = fall });
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(70)));
        Sfx.Play("han_whoosh");
        var gen = _gen;
        After(120 + 70, gen, () =>   // değdi; kısa donmadan sonra toz ve sarsıntı
        {
            HanFx.DustSlam(_engine, foot, c.W);
            Sfx.Play("han_place");
            Hit(uid, 6);
        });
    }

    // ---------- hasar ----------

    /// <summary>
    /// Savaşın hasarı: kazananın masadaki kartlarından kıvılcımlar sırayla kaybedenin portresine (sol kenardaki taraf kartı) uçar,
    /// her biri portreyi sarsar; sonuncusunda portre çatlar, hasar yazısı çıkar ve <paramref name="landed"/> (can düşer).
    /// </summary>
    private void DamageStrike(Battle b, int gen, Action landed)
    {
        var w = b.Winner;
        var side = 1 - w;
        var mine = side == Mine;
        var y = mine ? 742 : 66;   // SideCard'ın yeri
        var face = mine ? Face(Session.Me) : b.Pve ? (M.Round <= 2 ? "arachas" : "face_imlerith") : Face(b.Sides[side].PlayerId);
        var port = Portraits.Medallion(face, 60, mine);
        port.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ph = double.IsNaN(port.Height) ? port.DesiredSize.Height : port.Height;
        var holder = new Grid { Width = 60, Height = ph, IsHitTestVisible = false };
        holder.Children.Add(port);
        Canvas.SetLeft(holder, SideX + 8);
        Canvas.SetTop(holder, y + 8);
        System.Windows.Controls.Panel.SetZIndex(holder, 40);
        _fx.Children.Add(holder);
        var target = new Point(SideX + 8 + 30, y + 8 + ph / 2);
        var from = b.Order.SelectMany(r => b.Sides[w].In(r)).Where(u => _pos.ContainsKey(u.Uid)).Select(u => _pos[u.Uid]).ToList();
        if (from.Count == 0) from.Add(RowS(w == Mine, Row.Ranged).Center);
        var count = Math.Clamp((int)Math.Ceiling(b.Damage / 2.0), 3, 10);
        var ramp = w == Mine ? Ramp.Gold : Ramp.Ember;
        Sfx.Play("han_whoosh");
        var hits = 0;
        for (var i = 0; i < count; i++)
        {
            var src = from[i * from.Count / count];
            var lift = _engine.R(40, 130);
            After(i * 70, gen, () => HanFx.Ember(_engine, src, target, 470, ramp, lift, () =>
            {
                if (gen != _gen) return;
                hits++;
                if (hits < count)
                {
                    Fx.Shake(holder, 3, 200);
                    if (hits % 3 == 1) Sfx.Play("han_sizzle");
                    return;
                }
                // son kıvılcım: portre çatlar
                Fx.Shake(holder, 8, 460);
                HanFx.Explosion(_engine, target, mine ? Ramp.Blood : Ramp.Fire, 0.35);
                var crack = PortraitCrack(60, ph);
                crack.Opacity = 0;
                holder.Children.Add(crack);
                crack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80)));
                HanArt.FloatText(_fx, $"−{b.Damage} CAN", new Point(SideX + SideW / 2, mine ? y - 30 : y + 124 + 34), Color.FromRgb(0xff, 0x6a, 0x55), 32, 1800);   // taraf kartının dışında: adı örtmesin
                Sfx.Play(mine ? "han_damage" : "han_explosion");
                landed();
            }));
        }
    }

    /// <summary>Portrenin üstüne çatlak: vuruş noktasından dağılan kırık çizgiler (koyu yarık + ince açık kenar), daireye kırpılmış.</summary>
    private static FrameworkElement PortraitCrack(double w, double h)
    {
        var c = new Canvas { Width = w, Height = h, IsHitTestVisible = false, Clip = new EllipseGeometry(new Point(w / 2, h / 2), w * 0.44, h * 0.44) };
        var hit = new Point(w * 0.6, h * 0.4);
        var rng = new Random(5);
        var paths = new List<string>();
        foreach (var ang in new[] { -150.0, -60.0, 10.0, 75.0, 140.0 })
        {
            var pts = new List<Point> { hit };
            var p = hit;
            var a = ang * Math.PI / 180;
            for (var s = 0; s < 4; s++)
            {
                a += (rng.NextDouble() - 0.5) * 0.8;
                var len = w * (0.08 + rng.NextDouble() * 0.07);
                p = new Point(p.X + Math.Cos(a) * len, p.Y + Math.Sin(a) * len);
                pts.Add(p);
            }
            paths.Add("M" + string.Join(" L", pts.Select(q => $"{q.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)},{q.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}")));
        }
        var data = Geometry.Parse(string.Join(" ", paths));
        c.Children.Add(new Ellipse { Width = w * 0.3, Height = h * 0.3, Fill = new RadialGradientBrush(Color.FromArgb(0x70, 0x40, 0x00, 0x00), Color.FromArgb(0, 0, 0, 0)), Margin = new Thickness(hit.X - w * 0.15, hit.Y - h * 0.15, 0, 0) });
        c.Children.Add(new Path { Data = data, Stroke = HanUi.Br(Color.FromArgb(0xf0, 0x0e, 0x06, 0x02)), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Miter });
        c.Children.Add(new Path { Data = data, Stroke = HanUi.Br(Color.FromArgb(0x80, 0xff, 0xd8, 0xa8)), StrokeThickness = 0.7, RenderTransform = new TranslateTransform(0.9, 0.6) });
        return c;
    }
}
