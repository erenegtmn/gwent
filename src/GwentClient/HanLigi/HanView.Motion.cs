using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Hazırlıkta hareketin sürekliliği. Ekran her değişiklikte baştan çizilir; kartlar yeni yerlerinde birden belirmek yerine eski
/// yerlerinden süzülür (FLIP): çizimden önce her kartın sahnedeki yeri alınır, çizimden sonra yeri değişen kartın gerçek öğesi
/// gizlenir ve önbellekli bir kopyası uçuş katmanında (<c>_fx</c>, önbelleksiz) eski yerinden yenisine uçar; varınca gerçek kart
/// görünür. Önbellekli katmanlar uçuş boyunca yeniden çizilmez (kopya kendi dokusunda, yalnız konumu ve ölçeği oynar).
/// <list type="bullet">
/// <item>Alınan kart tezgâhtaki yerinden, ödül kartı seçim penceresinden depoya uçar.</item>
/// <item>Birleşen kopyalar kalan karta akıp söner; varınca yıldız parlaması.</item>
/// <item>Satılan kart altın sayacına süzülüp söner, "+n altın".</item>
/// <item>Bırakılan (sürüklenen) kart, hayaletin son yerinden yerine oturur; satırda kayan komşular da süzülür.</item>
/// </list>
/// Uçuş sürerken ekran yeniden çizilirse uçuş, kartın yeni öğesiyle sürer (hedef değiştiyse o andaki yerinden yeni hedefe).
/// </summary>
public sealed partial class HanView
{
    private sealed class Flight
    {
        public required FrameworkElement Ghost;
        public required ScaleTransform Scale;
        public required TranslateTransform Move;
        public required TranslateTransform Arc;
        /// <summary>Varınca görünecek kart (taşınan kartın kendisi ya da birleşmede kalan kart); yoksa 0.</summary>
        public int Target;
        /// <summary>Uçan kartın kendisi (taşıma) ya da birleşip yok olan kopya; yoksa 0.</summary>
        public int Uid;
        public Rect To;
        public double Height;
        public required CardDef Def;
        public int Star;
        public bool Fade;
        public Action? Landed;
        /// <summary>Yolun %72'sinde bir kez (satış: kart sayaca değdi).</summary>
        public Action? Near;
        // ilerleme (kendi kare döngüsünde): geçen süre, toplam süre, başlangıç, kavis yüksekliği, yumuşatma
        public Rect From;
        public double Ms, Elapsed, Lift;
        public bool EaseInOut;
        /// <summary>Kavissiz, doğrudan (tezgâhtan alınan kart); yeni hedefe dönse de öyle kalır.</summary>
        public bool Straight;

        public Rect Current() => new(Move.X, Move.Y + Arc.Y, DesignW * Scale.ScaleX, Height * Scale.ScaleY);
    }

    private readonly List<Flight> _flights = new();
    private int _flightGen;
    /// <summary>Tezgâhtan (ödül penceresinden) gelen kartın uçuşu: kısa ve doğrudan.</summary>
    private const double BuyFlyMs = 305;
    /// <summary>Sonraki çizimde yeni kartın geldiği yer (tezgâh kartı, ödül kartı) ve kartın kendisi.</summary>
    private Rect? _flyFrom;
    private CardDef? _flyDef;
    /// <summary>Sürükleme bitti: kartın bırakıldığı yer (sonraki çizimde oradan yerine süzülür).</summary>
    private (int Uid, Rect Rect)? _dropFrom;
    /// <summary>Bu çizimde gösterilen değer (masadaki güç), uçan kopya da aynısını göstersin.</summary>
    private readonly Dictionary<int, decimal?> _ownVals = new();
    private Dictionary<int, CardDef> _lastDefs = new();
    /// <summary>Bu çizimde yıldızı artan kartlar (birleşme): parlaması uçuşlar varınca.</summary>
    private readonly List<int> _grown = new();

    private double CacheScale => _caches.Count > 0 ? _caches[0].RenderAtScale : 1;

    private sealed record MotionBefore(Dictionary<int, Rect> Rects, Dictionary<int, int> Stars, Dictionary<int, CardDef> Defs, int Gold, Rect? From, CardDef? FromDef);

    /// <summary>Çizimden önce: kartların sahnedeki yerleri (uçanların o anki yeri, sürüklenenin hayaleti).</summary>
    private MotionBefore? BeginMotion()
    {
        var from = _flyFrom;
        var fromDef = _flyDef;
        _flyFrom = null;
        _flyDef = null;
        if (!_live || _screen != Screen.Prep) { StopFlights(); return null; }
        var rects = new Dictionary<int, Rect>();
        foreach (var f in _flights) if (f.Uid != 0 && f.Uid == f.Target) rects[f.Uid] = f.Current();
        foreach (var (uid, el) in _ownEls)
            if (!rects.ContainsKey(uid) && StageBounds(el) is { } r) rects[uid] = r;
        if (_dragging && _pressUid is { } du && DragRect() is { } dr) rects[du] = dr;
        if (_dropFrom is { } df && !_flights.Any(f => f.Uid == df.Uid)) rects[df.Uid] = df.Rect;
        _dropFrom = null;
        return new MotionBefore(rects, _lastStars, _lastDefs, _lastGold, from, fromDef);
    }

    /// <summary>Çizimden sonra: yeri değişen, gelen, birleşen ve satılan kartların uçuşları.</summary>
    private void EndMotion(MotionBefore? before)
    {
        var grown = _grown.ToList();
        _grown.Clear();
        if (before == null || _screen != Screen.Prep)
        {
            foreach (var g in grown) if (_ownEls.TryGetValue(g, out var el)) { Sfx.Play("han_merge"); StarBurst(el); }
            return;
        }
        _stage.UpdateLayout();
        var now = Me.Cards.ToDictionary(c => c.Uid);
        Rect? NewRect(int uid) => _ownEls.TryGetValue(uid, out var el) ? StageBounds(el) : null;

        // süren uçuşlar: kart yeni öğesiyle sürer; hedefi değiştiyse o andaki yerinden yeni hedefe
        var busy = new HashSet<int>();
        foreach (var f in _flights.ToList())
        {
            if (f.Target == 0) continue;   // satış: hedef kart yok, sürer
            if (!now.ContainsKey(f.Target) || NewRect(f.Target) is not { } to) { DropFlight(f); continue; }
            if (Near(to, f.To))
            {
                Hide(f.Target);
                busy.Add(f.Target);
                continue;
            }
            // hedef değişti: o andaki yerinden yeni hedefe (birleşmenin varış işi de taşınır)
            var cur = f.Current();
            DropFlight(f);
            Fly(f.Def, f.Star, f.Uid, f.Target, cur, to, fade: f.Fade, landed: f.Landed, near: f.Near, straight: f.Straight);
            busy.Add(f.Target);
        }

        // birleşme: kalan kart (eski yıldızıyla) yerine, öteki kopyalar (ve alınıp hemen birleşen kart) ona akar
        var used = new HashSet<int>();
        foreach (var g in grown)
        {
            if (!now.TryGetValue(g, out var keep) || NewRect(g) is not { } to) continue;
            var sources = before.Rects.Where(kv => !now.ContainsKey(kv.Key) && !used.Contains(kv.Key) && before.Defs.TryGetValue(kv.Key, out var d) && d == keep.Def).Select(kv => kv.Key).ToList();
            var bought = before.From is { } bf && before.FromDef == keep.Def && !now.Keys.Any(u => !before.Rects.ContainsKey(u) && _ownEls.ContainsKey(u));
            if (sources.Count == 0 && !bought) { Sfx.Play("han_merge"); if (_ownEls.TryGetValue(g, out var gel)) StarBurst(gel); continue; }
            used.UnionWith(sources);
            var oldStar = before.Stars.TryGetValue(g, out var s0) ? s0 : Math.Max(1, keep.Star - 1);
            var keepFrom = before.Rects.TryGetValue(g, out var kr) ? kr : to;
            // alınıp hemen birleşen kart alım hızında gelir; kalan kart da onunla birlikte açılır
            var ms = bought ? BuyFlyMs + 10 : 420;
            Fly(keep.Def, oldStar, g, g, keepFrom, to, ms, landed: () =>
            {
                Sfx.Play("han_merge");
                if (_ownEls.TryGetValue(g, out var el)) StarBurst(el);
            });
            // kopyalar kalan karttan biraz önce varır: kart, son kopya içine akınca açılır
            var k = 0;
            foreach (var u in sources)
                Fly(keep.Def, before.Stars.TryGetValue(u, out var su) ? su : oldStar, u, g, before.Rects[u], to, ms - 70 + 20 * k++, fade: true);
            if (bought) Fly(keep.Def, 1, 0, g, before.From!.Value, to, ms - 10, fade: true, straight: true);
            busy.Add(g);
        }

        // satış: kart altın sayacına süzülüp söner
        var goldUp = Me.Gold - before.Gold;
        if (goldUp > 0)
            foreach (var u in before.Rects.Keys.Where(u => !now.ContainsKey(u) && !used.Contains(u) && before.Defs.ContainsKey(u)).ToList())
            {
                var d = before.Defs[u];
                var star = before.Stars.TryGetValue(u, out var st) ? st : 1;
                var gain = Tuning.SellValue(d, star);
                var coin = new Rect(EcoX + 18, BarY + 8, 26, 26 * HanCardView.CardAspect);
                // kart sönerken (sayaca değince) altın yazısı çıkar ve sayaç o an saymaya başlar
                DelayCount("gold", double.PositiveInfinity);
                Fly(d, star, 0, 0, before.Rects[u], coin, 380, fade: true, near: () =>
                {
                    HanArt.FloatText(_fx, $"+{gain} altın", new Point(EcoX + 70, BarY - 6), Color.FromRgb(0xff, 0xd7, 0x6a), 24, 1100);
                    DelayCount("gold", 0);
                });
            }

        // alınan kart: tezgâhtaki (ya da ödül penceresindeki) yerinden
        if (before.From is { } from)
            foreach (var u in now.Keys.Where(u => !before.Rects.ContainsKey(u) && !busy.Contains(u)))
                if (NewRect(u) is { } to)
                {
                    Fly(now[u].Def, now[u].Star, u, u, from, to, BuyFlyMs, straight: true);
                    busy.Add(u);
                }

        // taşınan kartlar ve satırda kayan komşuları
        foreach (var (u, c) in now)
        {
            if (busy.Contains(u) || !before.Rects.TryGetValue(u, out var a) || NewRect(u) is not { } b || Near(a, b)) continue;
            Fly(c.Def, c.Star, u, u, a, b);
        }
    }

    private static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < 2.5 && Math.Abs(a.Y - b.Y) < 2.5 && Math.Abs(a.Width - b.Width) < 2.5;

    private void Hide(int uid)
    {
        if (_ownEls.TryGetValue(uid, out var el)) el.Opacity = 0;
    }

    /// <summary>
    /// Kartın önbellekli kopyası <paramref name="from"/>'dan <paramref name="to"/>'ya uçar; varınca <paramref name="target"/> kartı görünür.
    /// Uzun yolda hafif bir kavis çizer (<paramref name="straight"/> ise çizmez, hızla çıkıp yavaşlayarak doğrudan gider);
    /// <paramref name="fade"/> ise sonda söner (birleşen kopya, satılan kart).
    /// </summary>
    private void Fly(CardDef d, int star, int uid, int target, Rect from, Rect to, double ms = 0, bool fade = false, Action? landed = null, Action? near = null, bool straight = false)
    {
        var dist = (new Point(to.X + to.Width / 2, to.Y + to.Height / 2) - new Point(from.X + from.Width / 2, from.Y + from.Height / 2)).Length;
        if (ms <= 0) ms = Math.Clamp(170 + dist * 0.32, 200, 440);
        var ghost = new HanCardView(d, star, DesignW, target != 0 && _ownVals.TryGetValue(target, out var v) ? v : null) { IsHitTestVisible = false, Cursor = null };
        var sa = from.Width / DesignW;
        var sb = to.Width / DesignW;
        var scale = new ScaleTransform(sa, sa);
        var move = new TranslateTransform(from.X, from.Y);
        var arc = new TranslateTransform();
        ghost.RenderTransform = new TransformGroup { Children = { scale, move, arc } };
        // kopya bir kez dokuya çizilir; uçarken yalnız konumu ve ölçeği değişir
        ghost.CacheMode = new BitmapCache(Math.Clamp(Math.Max(sa, sb) * CacheScale * 1.1, 0.25, 3)) { SnapsToDevicePixels = false };
        Canvas.SetLeft(ghost, 0);
        Canvas.SetTop(ghost, 0);
        System.Windows.Controls.Panel.SetZIndex(ghost, 50);
        _fx.Children.Add(ghost);
        var f = new Flight
        {
            Ghost = ghost, Scale = scale, Move = move, Arc = arc, Uid = uid, Target = target, To = to, Height = ghost.Height, Def = d, Star = star, Fade = fade, Landed = landed, Near = near,
            From = from, Ms = ms, Straight = straight,
            Lift = !straight && dist > 220 ? -Math.Min(70, dist * 0.11) : 0, EaseInOut = !straight && dist > 260
        };
        _flights.Add(f);
        if (target != 0) Hide(target);
        if (!_flying) { _flying = true; _flyClock.Restart(); _flyLast = 0; CompositionTarget.Rendering += FlightTick; }
    }

    private readonly Stopwatch _flyClock = new();
    private double _flyLast;
    private bool _flying;

    /// <summary>
    /// Uçuşların kare döngüsü. Her kare en çok 20 ms ilerler: tıklama anının ağır karesi (ekranı baştan kurma, önbelleklerin
    /// yeniden çizimi) uçuşun başını yutmaz, kart takılmadan sonra kaldığı yerden sürer. (WPF animasyonları duvar saatine
    /// bağlıydı: ağır karenin ~100 ms'si animasyondan yeniyor, kart ilk karede yerine sıçrıyordu.)
    /// </summary>
    private void FlightTick(object? sender, EventArgs e)
    {
        var now = _flyClock.Elapsed.TotalMilliseconds;
        var dt = Math.Min(20, now - _flyLast);
        _flyLast = now;
        foreach (var f in _flights.ToList())
        {
            if (!_flights.Contains(f)) continue;
            f.Elapsed += dt;
            var p = Math.Clamp(f.Elapsed / f.Ms, 0, 1);
            var k = f.EaseInOut ? (p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2) : 1 - Math.Pow(1 - p, 3);
            var s = (f.From.Width + (f.To.Width - f.From.Width) * k) / DesignW;
            f.Scale.ScaleX = f.Scale.ScaleY = s;
            f.Move.X = f.From.X + (f.To.X - f.From.X) * k;
            f.Move.Y = f.From.Y + (f.To.Y - f.From.Y) * k;
            // uzun yolda kavis: kart havalanıp iner (tezgâhtan depoya, depodan masaya)
            f.Arc.Y = f.Lift * Math.Sin(Math.PI * p);
            if (f.Fade) f.Ghost.Opacity = p < 0.62 ? 1 : Math.Max(0, 1 - (p - 0.62) / 0.38);
            if (p >= 0.72 && f.Near is { } nearAct) { f.Near = null; nearAct(); }
            if (p < 1) continue;
            DropFlight(f);
            // aynı karta uçan başka kopya yoksa kart görünür
            if (f.Target != 0 && !_flights.Any(o => o.Target == f.Target) && _ownEls.TryGetValue(f.Target, out var el)) el.Opacity = 1;
            f.Landed?.Invoke();
        }
        if (_flights.Count == 0) { _flying = false; CompositionTarget.Rendering -= FlightTick; }
    }

    private void DropFlight(Flight f)
    {
        _flights.Remove(f);
        _fx.Children.Remove(f.Ghost);
    }

    /// <summary>Bütün uçuşları bitirir (hazırlıktan çıkış): kopyalar silinir, gizli kartlar görünür, varış işleri yapılmaz.</summary>
    private void StopFlights()
    {
        if (_flights.Count == 0) return;
        _flightGen++;
        foreach (var f in _flights)
        {
            _fx.Children.Remove(f.Ghost);
            if (f.Target != 0 && _ownEls.TryGetValue(f.Target, out var el)) el.Opacity = 1;
        }
        _flights.Clear();
    }

    // ---------- sürükleme: yaylı takip ve eğim ----------

    private const double DragW = 116;
    private ScaleTransform? _dragScale;
    private RotateTransform? _dragTilt;
    private TranslateTransform? _dragMove;
    private Point _dragCenter, _dragGrab;
    private double _dragAngle, _dragS;
    private readonly Stopwatch _dragClock = new();
    private double _dragLast;
    private Point _dragCursor;

    /// <summary>
    /// Sürükleme başlar: kartın kopyası kartın kendi yerinden kalkar, imleci yaylı izler (tuttuğun nokta imlecin altında kalır) ve
    /// gidiş hızına göre yana yatar. Masadaki kart soluklaşır (kaldırıldı).
    /// </summary>
    private void StartDragGhost(OwnedCard card, Point cursor)
    {
        var src = _ownEls.TryGetValue(card.Uid, out var el) ? StageBounds(el) : null;
        var r = src ?? new Rect(cursor.X - DragW / 2, cursor.Y - DragW * 0.66, DragW, DragW * HanCardView.CardAspect);
        var ghost = new HanCardView(card.Def, card.Star, DragW, _ownVals.TryGetValue(card.Uid, out var v) ? v : null) { IsHitTestVisible = false, Cursor = null };
        var h = ghost.Height;
        // tutulan nokta (kartın içinde, oran olarak): kopya bu noktadan imlece asılı durur
        _dragGrab = new Point(Math.Clamp((_pressPoint.X - r.X) / r.Width, 0.1, 0.9), Math.Clamp((_pressPoint.Y - r.Y) / r.Height, 0.1, 0.9));
        _dragS = r.Width / DragW;
        _dragCenter = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        _dragAngle = 0;
        _dragScale = new ScaleTransform(_dragS, _dragS, DragW / 2, h / 2);
        _dragTilt = new RotateTransform(0, DragW / 2, h / 2);
        _dragMove = new TranslateTransform(_dragCenter.X - DragW / 2, _dragCenter.Y - h / 2);
        ghost.RenderTransform = new TransformGroup { Children = { _dragScale, _dragTilt, _dragMove } };
        ghost.CacheMode = new BitmapCache(Math.Clamp(CacheScale * 1.25, 0.5, 3));
        Canvas.SetLeft(ghost, 0);
        Canvas.SetTop(ghost, 0);
        _ghost = ghost;
        _fx.Children.Add(ghost);
        if (el != null) el.Opacity = 0.3;
        _dragCursor = cursor;
        _dragClock.Restart();
        _dragLast = 0;
        if (_live) CompositionTarget.Rendering += DragTick;
        else DragSnap();
    }

    private void DragSnap()
    {
        if (_ghost == null || _dragMove == null || _dragScale == null) return;
        var h = _ghost.Height;
        _dragS = 1;
        _dragCenter = new Point(_dragCursor.X + (0.5 - _dragGrab.X) * DragW, _dragCursor.Y + (0.5 - _dragGrab.Y) * h);
        _dragScale.ScaleX = _dragScale.ScaleY = 1;
        _dragMove.X = _dragCenter.X - DragW / 2;
        _dragMove.Y = _dragCenter.Y - h / 2;
    }

    private void DragTick(object? sender, EventArgs e)
    {
        if (_ghost == null || _dragMove == null || _dragScale == null || _dragTilt == null) { CompositionTarget.Rendering -= DragTick; return; }
        var t = _dragClock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(t - _dragLast, 0.001, 0.05);
        _dragLast = t;
        var h = _ghost.Height;
        // hedef: tutulan nokta imlecin altında; yay (üstel yaklaşım) kare hızından bağımsız
        var target = new Point(_dragCursor.X + (0.5 - _dragGrab.X) * DragW, _dragCursor.Y + (0.5 - _dragGrab.Y) * h);
        var a = 1 - Math.Exp(-dt * 24);
        var prev = _dragCenter;
        _dragCenter = new Point(prev.X + (target.X - prev.X) * a, prev.Y + (target.Y - prev.Y) * a);
        _dragS += (1.04 - _dragS) * (1 - Math.Exp(-dt * 16));
        // eğim: yatay hıza göre (sağa giderken kart sağa yatar), yavaşça doğrulur
        var vx = (_dragCenter.X - prev.X) / dt;
        var want = Math.Clamp(vx * 0.012, -14, 14);
        _dragAngle += (want - _dragAngle) * (1 - Math.Exp(-dt * 14));
        _dragScale.ScaleX = _dragScale.ScaleY = _dragS;
        _dragTilt.Angle = _dragAngle;
        _dragMove.X = _dragCenter.X - DragW / 2;
        _dragMove.Y = _dragCenter.Y - h / 2;
    }

    /// <summary>Sürüklenen kopyanın o anki yeri (bırakınca kart buradan yerine uçar).</summary>
    private Rect? DragRect()
    {
        if (_ghost == null || _dragScale == null) return null;
        var h = _ghost.Height;
        var w = DragW * _dragScale.ScaleX;
        return new Rect(_dragCenter.X - w / 2, _dragCenter.Y - h * _dragScale.ScaleY / 2, w, h * _dragScale.ScaleY);
    }

    private void StopDragGhost()
    {
        CompositionTarget.Rendering -= DragTick;
        _dragScale = null;
        _dragTilt = null;
        _dragMove = null;
    }

    // ---------- üstüne gelme: kart hafifçe kalkar ----------

    /// <summary>
    /// Masadaki kartın üstüne gelince kartın canlı görüntüsü (VisualBrush, bir kez dokuya çizilir) ayağından hafifçe büyür ve altın bir
    /// kenar ışığı alır. Gerçek kart önbellekli katmanda kıpırdamaz; yalnız bu küçük kopya oynar.
    /// </summary>
    private void HoverLift(FrameworkElement anchor, Rect r)
    {
        if (!_live || _dragging || anchor.ActualWidth <= 0 || r.Width < 30) return;
        var radius = Math.Max(3, r.Width * 0.06);
        // tier taşı kartın üstünden taşar: kopya o kadar yukarıdan başlar
        var oh = anchor is HanCardView ? HanCardView.GemOverhang(anchor.ActualWidth) : 0;
        var ohS = oh * r.Height / anchor.ActualHeight;
        var g = new Grid { Width = r.Width, Height = r.Height + ohS, IsHitTestVisible = false };
        g.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new VisualBrush(anchor) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, -oh, anchor.ActualWidth, anchor.ActualHeight + oh) },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.75 }
        });
        g.Children.Add(new Border
        {
            Margin = new Thickness(0, ohS, 0, 0),
            CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(Math.Max(1.4, r.Width * 0.016)), BorderBrush = HanUi.Br(HanUi.GoldHi, 0xd8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = HanUi.Gold, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 }
        });
        var s = new ScaleTransform(1, 1, r.Width / 2, r.Height + ohS);
        var up = new TranslateTransform();
        g.RenderTransform = new TransformGroup { Children = { s, up } };
        g.CacheMode = new BitmapCache(Math.Clamp(CacheScale * 1.12, 0.5, 3));
        Canvas.SetLeft(g, r.X);
        Canvas.SetTop(g, r.Y - ohS);
        _hover.Children.Insert(0, g);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(120);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.07, dur) { EasingFunction = ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.07, dur) { EasingFunction = ease });
        up.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -Math.Min(8, r.Height * 0.04), dur) { EasingFunction = ease });
    }

    // ---------- sayaçlar: altın, can, satır gücü yuvarlanarak değişir ----------

    /// <summary>Gösterilen sayılar (anahtar: "gold", "hp:3", "row:Close"); sayaç yarıdayken çizim olursa oradan sürer.</summary>
    private readonly Dictionary<string, double> _shownNum = new();
    private sealed class Ticker
    {
        public required string Key;
        public required TextBlock Text;
        public double From, To, Ms;
        /// <summary>Geçen süre (eksi: bekleme). Kare başına en çok 20 ms ilerler (ağır kare sayacı atlatmasın).</summary>
        public double Elapsed;
        public required Func<double, string> Fmt;
    }

    private readonly List<Ticker> _tickers = new();
    private readonly Stopwatch _tickClock = Stopwatch.StartNew();
    private double _tickLast;
    private bool _ticking;

    /// <summary>
    /// Sayıyı <paramref name="to"/>'ya yuvarlayarak getirir (önceki gösterilen değerden). Sayıyorsa true: o zaman yazı
    /// <c>_counters</c> katmanına konmalı (her karede değişir; önbellekli katmanda kalırsa bütün katman yeniden çizilir).
    /// Saymıyorsa yazı önbellekli yerinde kalır: sayaç katmanında duran gölgeli yazılar efektler oynarken kare hızını
    /// düşürüyordu (Aard ~110 → ~80 kare/sn).
    /// </summary>
    private bool Count(string key, TextBlock t, double to, Func<double, string> fmt, double ms = 420)
    {
        _tickers.RemoveAll(x => x.Key == key);
        if (!_live || !_shownNum.TryGetValue(key, out var from) || Math.Abs(from - to) < 0.0001)
        {
            _shownNum[key] = to;
            t.Text = fmt(to);
            return false;
        }
        t.Text = fmt(from);
        _tickers.Add(new Ticker { Key = key, Text = t, From = from, To = to, Ms = ms, Fmt = fmt });
        if (!_ticking) { _ticking = true; _tickLast = _tickClock.Elapsed.TotalMilliseconds; CompositionTarget.Rendering += Tick; }
        return true;
    }

    /// <summary>Süren sayacı <paramref name="ms"/> sonra başlatır (sonsuz: başka bir olay başlatana kadar bekler).</summary>
    private void DelayCount(string key, double ms)
    {
        if (_tickers.FirstOrDefault(x => x.Key == key) is not { } x) return;
        x.Elapsed = -ms;
        x.Text.Text = x.Fmt(x.From);
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = _tickClock.Elapsed.TotalMilliseconds;
        var dt = Math.Min(20, now - _tickLast);
        _tickLast = now;
        for (var i = _tickers.Count - 1; i >= 0; i--)
        {
            var x = _tickers[i];
            x.Elapsed += dt;
            var p = Math.Clamp(x.Elapsed / x.Ms, 0, 1);
            var v = x.From + (x.To - x.From) * (1 - Math.Pow(1 - p, 3));
            var text = x.Fmt(p >= 1 ? x.To : v);
            if (x.Text.Text != text) x.Text.Text = text;
            _shownNum[x.Key] = p >= 1 ? x.To : v;
            if (p >= 1) _tickers.RemoveAt(i);
        }
        if (_tickers.Count == 0) { _ticking = false; CompositionTarget.Rendering -= Tick; }
    }

    private static string Whole(double v) => Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Ondalıklı güç: hedef tamsa tam sayılar, değilse bir basamak (Tuning.Fmt biçimi).</summary>
    private static Func<double, string> PowerFmt(decimal to) => to == Math.Round(to)
        ? v => Tuning.Fmt(Math.Round((decimal)v))
        : v => Tuning.Fmt(Math.Round((decimal)v, 1));
}
