using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using HanLigi;
using HT = HanLigi.Texts;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi oyun ekranı (YENI_MOD.md). Ortada perspektifli masa (<see cref="HanScene"/>; üstte rakibin, altta senin satırların),
/// altta tek bir şerit (ekonomi · depo · tezgâh · Hazır; savaşta işaret çubuğu), sağda oyuncular, solda sinerjiler, üstte ince
/// yönlendirme şeridi.
/// <para>Performans: ekran katmanlara ayrılır ve durağan katmanlar GPU'da önbelleğe alınır (<see cref="BitmapCache"/>); her
/// karede değişen şeyler (süre çubuğu, parçacıklar, uçan yazılar) ayrı, küçük ve önbelleksiz katmanlardadır. Böylece efekt ya da
/// sayaç oynarken ekranın geri kalanı yeniden çizilmez.</para>
/// Etkileşimli öğeler <c>Tag</c> taşır (han-*).
/// </summary>
public sealed partial class HanView : Grid
{
    private enum Screen { School, Prep, Activation, Battle, Result, Final }

    public HanSession Session { get; }
    private readonly Action _back;
    private readonly Action _again;
    private readonly bool _live;
    private readonly HanScene _scene;
    private readonly Grid _stage = new() { Width = 1920, Height = 1080, ClipToBounds = true, Background = Brushes.Black };
    /// <summary>Masadaki satır vurguları, sis, bırakma alanları (perspektifli yamuklar).</summary>
    private readonly Canvas _rows = new() { Width = 1920, Height = 1080 };
    /// <summary>Masada sürekli oynayan şeyler (hava): önbelleksiz, yalnız kendi bölgesi yeniden çizilir.</summary>
    private readonly Canvas _anim = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    /// <summary>Masadaki kartlar ve rozetler.</summary>
    private readonly Canvas _units = new() { Width = 1920, Height = 1080 };
    /// <summary>Arayüz (HUD).</summary>
    private readonly Canvas _c = new() { Width = 1920, Height = 1080 };
    /// <summary>Yuvarlanarak değişen sayılar (altın, can, satır gücü): önbelleksiz, yalnız sayı yazıları.</summary>
    private readonly Canvas _counters = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    private readonly Canvas _fx = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    private readonly Grid _banner = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    private readonly Canvas _hover = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    /// <summary>Havada süzülen tezgâh (Auto Chess gibi): önbelleksiz; kartlar tek tek önbellekte, yalnız konumları oynar.</summary>
    private readonly Canvas _shop = new() { Width = 1920, Height = 1080 };
    /// <summary>Süre çubuğu ve sayaç: tek başına, önbelleksiz (her karede akıcı akar, başka hiçbir şeyi yeniden çizdirmez).</summary>
    private readonly Canvas _clockLayer = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    /// <summary>Bildirimler (öteki masaların sonuçları): ekran yeniden çizilince silinmez.</summary>
    private readonly Canvas _toasts = new() { Width = 1920, Height = 1080, IsHitTestVisible = false };
    private readonly FxEngine _engine = new();
    internal FxEngine Engine => _engine;
    private readonly Dictionary<int, FrameworkElement> _unitEls = new();
    /// <summary>Masadaki kartların ekrandaki genişliği (efektler kart ölçeğinde).</summary>
    private readonly Dictionary<int, double> _cardW = new();
    private readonly List<BitmapCache> _caches = new();
    private UIElement[] _cachedLayers = Array.Empty<UIElement>();
    private Screen _screen = Screen.School;
    private int? _sel;
    internal int? Selected => _sel;
    /// <summary>Sınama teşhisi: ekran ve bekleme durumları.</summary>
    /// <summary>Sınama: bir çözüm (pencere, savaş sonu) oynuyor.</summary>
    internal bool Busy => _busy;
    internal string DebugState => $"{_screen} waitReady={_waitReady} waitAct={_waitAct} waitWindow={_waitWindow} waitRound={_waitRound} busy={_busy} shown={_resultShown} battle={(Session.MyBattle is { } b ? $"w{b.Window} done={b.Done} sub={b.Submitted(Session.MySide)} out={b.Outcomes.Count}" : "-")}";
    private bool _help;
    private Activation _act = new();
    private readonly List<Sign> _signs = new();
    private Potion? _potion;
    private readonly List<int> _ambush = new();
    private DispatcherTimer? _timer;
    private DateTime _deadline;
    private int _lastShown = -1;
    private TextBlock? _clock;
    private Action? _onExpire;
    /// <summary>Animasyon sürerken girdi kapalı; her yeni animasyon dizisi kuşağı artırır (eski zamanlayıcılar kendini iptal eder).</summary>
    private bool _busy;
    private int _gen;
    // online bekleme durumları: sunucudan yanıt gelince bir sonraki adıma geçilir
    private bool _waitReady, _waitAct, _waitRound;
    private int? _waitWindow;
    /// <summary>Kendi savaşımın sonu ekranda gösterildi mi. Motor canı savaş biter bitmez düşürür; ekran, sonuç sinematiği
    /// oynayana kadar eski canı gösterir (sürpriz bozulmasın).</summary>
    private bool _resultShown = true;
    /// <summary>Bildirimi yapılmış savaş kaydı sayısı (öteki masaların sonuçları).</summary>
    private int _histSeen = -1;
    private readonly Dictionary<int, int> _hpShownLast = new();
    private (int Mine, int Theirs) _scoreShown = (-1, -1);

    private Match M => Session.M;
    private PlayerState Me => Session.Player;

    // Tahta ölçüleri (HanScene tahtası, 1600×1000): Gwent masasının satır bölgesi
    private const double RowX = 463, RowW = 955, RowH = 137, ColX = 303, ColW = 150;
    private static readonly double[] OppTop = { 345, 185, 32 };
    private static readonly double[] MyTop = { 515, 678, 838 };
    private static double RowY(bool mine, Row r) => (mine ? MyTop : OppTop)[(int)r];
    private static Rect RowRect(bool mine, Row r) => new(RowX, RowY(mine, r), RowW, RowH);
    private static Point RowCenter(bool mine, Row r) => new(RowX + RowW / 2, RowY(mine, r) + RowH / 2);

    // Ekran düzeni
    private const double BarY = 880, BarH = 192;            // alt şerit
    private const double ListX = 1714, ListW = 194;         // sağ: oyuncular
    private const double SideX = 12, SideW = 236;           // sol: sinerjiler / taraflar
    private const double TopX = 550, TopW = 820;            // üst yönlendirme şeridi
    // Kart: tasarım boyutu (masada ölçeklenir) ve masaya yaslanma (dikey basıklık)
    private const double DesignW = 140, DesignH = DesignW * HanCardView.CardAspect, Lean = 1.0;   // kart basılmaz: gerçek oranında durur

    private static readonly ControlTemplate BareTemplate = (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>");

    public HanView(HanSession session, Action back, Action again, bool animate = true)
    {
        Session = session;
        _back = back;
        _again = again;
        _live = animate;
        Background = Brushes.Black;
        _scene = new HanScene(animate);
        foreach (var layer in new UIElement[] { _scene, _tavernHost, _rows, _anim, _units, _c, _counters, _shop, _fx, _engine, _clockLayer, _toasts, _banner, _hover }) _stage.Children.Add(layer);
        // durağan katmanlar GPU dokusu olarak önbelleğe alınır (ölçek pencere boyuna göre ayarlanır: yazılar net kalır)
        _cachedLayers = new UIElement[] { _scene, _rows, _units, _c };
        foreach (var layer in _cachedLayers)
        {
            var cache = new BitmapCache(1) { SnapsToDevicePixels = true };
            _caches.Add(cache);
            layer.CacheMode = cache;
        }
        SizeChanged += (_, _) => UpdateCacheScale();
        Loaded += (_, _) => UpdateCacheScale();
        // açıklamalar beklemeden çıksın; kartlarda ayrıca anlık inceleme penceresi var
        ToolTipService.SetInitialShowDelay(this, 60);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 60000);
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _stage });
        Unloaded += (_, _) => { StopTimer(); EndRoll(); _gen++; StopDragGhost(); CompositionTarget.Rendering -= Tick; _ticking = false; DropTavern(); };
        _stage.MouseMove += OnDragMove;
        // başka bir kartımın üstüne bırakılan kart onunla yer değiştirir (bırakma alanlarından önce: önizleme olayı tepeden iner)
        _stage.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging || _pressUid is not { } uid || OwnCardAt(e.GetPosition(_stage), uid) is not { } target) return;
            e.Handled = true;
            DropOnCard(uid, target);
        };
        _stage.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => EndDrag()), true);
        // boş bir yere tıklamak seçimi bırakır (kartlar ve düğmeler olayı kendileri tüketir)
        _stage.MouseLeftButtonUp += (_, e) => { if (!e.Handled) Deselect(); };
        _stage.MouseRightButtonUp += (_, _) => Deselect();
        _stage.MouseLeave += (_, _) => EndDrag();
        // kısayollar: Boşluk tezgâh, R yenile, F XP
        Focusable = true;
        FocusVisualStyle = null;
        Loaded += (_, _) => Focus();
        _stage.MouseDown += (_, _) => Focus();
        PreviewKeyDown += OnKey;
        Session.Changed += OnSessionChanged;
        Unloaded += (_, _) => Session.Changed -= OnSessionChanged;
        _histSeen = M.History.Count;
        _screen = M.Phase switch { Phase.School => Screen.School, Phase.Finished => Screen.Final, _ => Screen.Prep };
        if (_screen == Screen.Prep) EnterPrep(); else Render();
    }

    /// <summary>
    /// Oturumda bir eylem uygulandı (yerelde hemen, online'da sunucudan gelince). Beklenen adım geldiyse ona geçilir; yoksa ekran
    /// yeniden çizilir (öteki oyuncuların hamleleri oyuncu listesini ve havuzu değiştirir).
    /// </summary>
    /// <summary>Kendi eylemim uygulanıyor (<c>Do</c>): ekranı o kurar, bildirim ayrıca çizmez.</summary>
    private bool _acting;

    private void OnSessionChanged()
    {
        Announce();
        switch (_screen)
        {
            case Screen.School:
                if (Me.School != null && M.Phase != Phase.School) EnterPrep(); else Render();
                break;
            case Screen.Prep:
                if (M.Phase == Phase.Battle && Session.MyBattle != null) EnterActivation();
                else if (!_dragging && _pressUid == null && !_acting) Render();
                break;
            case Screen.Activation:
                if (Session.MyBattle is { Started: true }) BeginBattle();   // iki taraf da seçti (ya da süre doldu, sunucu benim yerime seçti)
                else if (Session.MyBattle != null) Render();   // öteki masalar ilerledi (canlar, masa listesi)
                break;
            case Screen.Battle:
                if (_waitWindow is { } i && B.Outcomes.Count > i) ResolveWindow(B.Outcomes[i]);
                else if (_waitRound && Session.RoundClosed) ShowResult();
                else if (!_busy && Session.MyBattle != null) Render();
                break;
            case Screen.Final:
                Render();
                break;
        }
    }

    /// <summary>Beklenen şeyi anlatan yazı (online; yoksa null).</summary>
    private string? WaitingText() =>
        _screen == Screen.School && Me.School != null ? "Öteki oyuncular okulunu seçiyor…"
        : _waitReady ? (Session.Online ? "Süre doldu, masan kilitlendi. Öteki oyuncular bekleniyor…" : "Hazırsın. Öteki oyuncular hazırlanıyor…")
        : _waitAct ? "Rakibin hedeflerini seçiyor…"
        : _waitWindow != null ? "Rakibin seçimini yapıyor…"
        : _waitRound ? (M.Battles.All(b => b.Done) ? $"{MyVerdict()} · diğer oyuncular savaşlarının sonunu izliyor…" : $"{MyVerdict()} · diğer oyuncular bekleniyor ({M.Battles.Count(b => b.Done)}/{M.Battles.Count} masa bitti)")
        : null;

    /// <summary>"Kazandın 2–1" / "Kaybettin 1–2" / "Berabere 1–1" (kendi savaşım).</summary>
    private string MyVerdict()
    {
        if (Session.MyBattle is not { } b) return "Savaş bitti";
        var (m, t) = (b.Sides[Session.MySide].Points, b.Sides[1 - Session.MySide].Points);
        return (b.Winner < 0 ? "Berabere" : b.Winner == Session.MySide ? "Kazandın" : "Kaybettin") + $" {m}–{t}";
    }

    /// <summary>Kendi savaşımın sonucu henüz gösterilmediyse oradan gelen can kaybı (ekranda henüz düşmemiş olan).</summary>
    private int HiddenDamage(PlayerState p)
    {
        if (_resultShown || _screen is not (Screen.Activation or Screen.Battle) || Session.MyBattle is not { Settled: true } b || b.Winner < 0) return 0;
        var loser = 1 - b.Winner;
        if (loser == 1 && (b.Pve || b.Ghost)) return 0;
        return b.Sides[loser].PlayerId == p.Id ? b.Damage : 0;
    }

    private int HpShown(PlayerState p) => p.Hp + HiddenDamage(p);
    private bool AliveShown(PlayerState p) => p.Alive || HiddenDamage(p) > 0;

    /// <summary>
    /// Öteki masaların sonuçları biter bitmez bildirilir (üstte sağda kayan yazılar): kim kimi hangi skorla yendi, kim kaç can
    /// kaybetti, kim elendi. Kendi savaşım bildirilmez (onu sinematik gösterir).
    /// </summary>
    private void Announce()
    {
        var fresh = M.History.Skip(_histSeen).ToList();
        _histSeen = M.History.Count;
        if (!_live) return;
        var k = 0;
        foreach (var h in fresh)
        {
            if (h.PlayerA == Session.Me || (h.PlayerB == Session.Me && !h.Ghost)) continue;
            var (a, bn) = (h.NameA, h.Pve ? "canavarlar" : h.NameB);
            string text;
            Color c;
            if (h.Winner < 0) { text = $"{a} – {bn}  berabere {h.PointsA}–{h.PointsB}"; c = HanUi.Parchment; }
            else
            {
                var (w, l, wp, lp) = h.Winner == 0 ? (a, bn, h.PointsA, h.PointsB) : (bn, a, h.PointsB, h.PointsA);
                text = h.Pve ? (h.Winner == 0 ? $"{a} canavarları {wp}–{lp} yendi" : $"{a} canavarlara {wp}–{lp} yenildi")
                    : $"{w}, {l} karşısında {wp}–{lp} kazandı";
                if (h.Damage > 0 && !(h.Ghost && h.Winner == 0)) text += $"  ·  {(h.Pve ? a : l)} −{h.Damage} can";
                c = HanUi.Gold;
            }
            Toast(text, c, 700 * k++);
            foreach (var pid in new[] { h.PlayerA, h.PlayerB })
                if (pid >= 0 && pid < M.Players.Count && M.Players[pid] is { Alive: false } dead && dead.EliminatedRound == M.Round && !(h.Ghost && pid == h.PlayerB))
                    Toast($"{dead.Name} elendi  ·  {dead.Place}. sıra", HanUi.Bad, 700 * k++);
        }
    }

    private int _toastSlot;

    /// <summary>Üst sağda beliren, kendiliğinden kaybolan bildirim satırı.</summary>
    private void Toast(string text, Color color, int delay)
    {
        Fx.After(delay, () =>
        {
            var slot = _toastSlot++ % 4;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 6, 16, 7) };
            sp.Children.Add(new Rectangle { Width = 3, Height = 18, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(HanUi.T(text, 15, HanUi.Br(HanUi.Parchment), FontWeights.SemiBold));
            sp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var f = HanUi.Frame(sp.DesiredSize.Width + 2, sp.DesiredSize.Height, sp, 0xe8, color, 8);
            f.IsHitTestVisible = false;
            Canvas.SetLeft(f, ListX - 14 - f.Width);
            Canvas.SetTop(f, 66 + slot * 44);
            _toasts.Children.Add(f);
            var tt = new TranslateTransform(24, 0);
            f.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            var life = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(4600) };
            life.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            life.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.07)));
            life.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.85)));
            life.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            life.Completed += (_, _) => _toasts.Children.Remove(f);
            f.BeginAnimation(OpacityProperty, life);
            Sfx.Play("han_click");
        });
    }

    /// <summary>Bekleme göstergesi: ekranın ortasında yarı saydam pill (tıklamayı engellemez).</summary>
    private void DrawWaiting()
    {
        if (WaitingText() is not { } t) return;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 10, 24, 12) };
        var dot = new Ellipse { Width = 12, Height = 12, Fill = HanUi.Br(HanUi.Gold), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        if (_live) dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        sp.Children.Add(dot);
        sp.Children.Add(HanUi.T(t, 20, HanUi.Br(HanUi.Parchment), FontWeights.SemiBold));
        sp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var f = HanUi.Frame(sp.DesiredSize.Width + 4, sp.DesiredSize.Height + 2, sp, 0xf0, HanUi.Gold, 10);
        f.IsHitTestVisible = false;
        Canvas.SetLeft(f, 960 - f.Width / 2);
        Canvas.SetTop(f, 470);
        _clockLayer.Children.Add(f);
        _waitEl = f;
    }

    private FrameworkElement? _waitEl;

    /// <summary>Önbellek dokusu pencerenin gerçek ölçeğinde çizilir (büyük ekranda bulanık, küçükte boşa büyük olmasın).</summary>
    private void UpdateCacheScale()
    {
        if (ActualWidth < 10) return;
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var s = Math.Min(ActualWidth / 1920, ActualHeight / 1080) * dpi;
        s = Math.Clamp(Math.Ceiling(s * 8) / 8, 0.5, 2);
        foreach (var c in _caches) if (Math.Abs(c.RenderAtScale - s) > 0.01) c.RenderAtScale = s;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Esc: açık pencereyi kapatır; yoksa çıkışı sorar
            e.Handled = true;
            if (_exitAsk) { _exitAsk = false; Render(); }
            else if (_help) { _help = false; Render(); }
            else AskExit();
            return;
        }
        if (_exitAsk) return;
        if (_screen == Screen.Result && !_help && e.Key is Key.Enter or Key.Space) { e.Handled = true; EnterPrep(); return; }
        // savaşta: Boşluk tezgâh (alt şerit depo satırına döner), L kilit
        if (_screen is Screen.Activation or Screen.Battle && !_help && !_busy && e.Key is Key.Space or Key.L)
        {
            e.Handled = true;
            if (e.Key == Key.Space) ToggleBattleShop(); else if (_battleShop) ToggleLock();
            return;
        }
        if (_screen != Screen.Prep || _busy || _help) return;
        if (Me.PotionOptions.Length > 0 || Me.RewardOptions.Length > 0) return;
        switch (e.Key)
        {
            case Key.Space: ToggleShop(); e.Handled = true; break;
            case Key.R: Do(() => Session.Act(HanActKind.Reroll), "han_reroll"); _shopOpen = true; Render(); e.Handled = true; break;
            case Key.F: Do(() => Session.Act(HanActKind.Xp), "han_xp"); e.Handled = true; break;
            case Key.L: ToggleLock(); e.Handled = true; break;
        }
    }

    /// <summary>Seçili kartı bırakır (boş yere tıklama, sağ tık).</summary>
    /// <summary>Sınama: hazırlık süresi doldu (canlı olmayan kipte sayaç çalışmaz).</summary>
    internal bool PrepTimeUp()
    {
        if (_screen != Screen.Prep || _waitReady || M.Phase != Phase.Prep || Me.Ready || !Me.Alive) return false;
        OnReady();
        return true;
    }

    internal void Deselect()
    {
        if (_sel == null || _dragging || _screen != Screen.Prep) return;
        _sel = null;
        Render();
    }

    // ---------- yardımcılar ----------

    private T Put<T>(T el, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        _c.Children.Add(el);
        return el;
    }

    /// <summary>Arayüz paneli (tasarım sistemi çerçevesi): pahlı, ince altın kenar, üstte parlayan çizgi.</summary>
    private static Grid Panel(double w, double h, UIElement? child = null, byte alpha = 0xe0, Color? accent = null, double cut = 10) => HanUi.Frame(w, h, child, alpha, accent, cut);

    /// <summary>Panel başlığı: küçük, aralıklı büyük harf.</summary>
    private static TextBlock Caption(string text) => HanUi.Label(text);

    /// <summary>Metin düğmesi (tasarım sistemi).</summary>
    private static HanButton Btn(string text, string tag, Action click, bool primary = false, bool enabled = true, double? width = null, string? tip = null, double height = 46, string? sub = null)
    {
        var w = width ?? Math.Max(120, 40 + text.Length * 12);
        return new HanButton(text, click, primary ? HanButtonKind.Primary : HanButtonKind.Secondary, w, height, sub, null, enabled, tip) { Tag = tag };
    }

    /// <summary>HUD eylem düğmesi: başlık + alt yazı.</summary>
    private static HanButton Act(string title, string? sub, string tag, Action click, HanButtonKind kind, double w, double h, bool enabled = true, string? tip = null, FrameworkElement? icon = null)
        => new(title, click, kind, w, h, sub, icon, enabled, tip) { Tag = tag };

    /// <summary>Kendi görseli olan düğme (madalyon, ikon, bırakma alanı): çerçevesiz, içerik neyse o.</summary>
    private static Button Bare(FrameworkElement content, string tag, Action click, bool enabled = true, string? tip = null)
    {
        var b = new Button { Template = BareTemplate, Content = content, Tag = tag, IsEnabled = enabled, Cursor = Cursors.Hand, Focusable = false };
        b.Click += (_, _) => click();
        if (tip != null)
        {
            b.ToolTip = HanUi.Tip(tip);
            ToolTipService.SetShowOnDisabled(b, true);
        }
        return b;
    }

    private HanCardView CardEl(CardDef d, int star, double w, string? tag, Action? click, decimal? value = null, bool selected = false, string? footer = null, bool dim = false, int? price = null, bool named = false, double aspect = HanCardView.CardAspect, bool plain = false)
    {
        var v = new HanCardView(d, star, w, value, selected, footer, dim, price, aspect: aspect, named: named, plain: plain) { Tag = tag, ToolTip = null };
        if (click != null) v.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        else v.Cursor = Cursors.Arrow;
        v.MouseEnter += (_, _) => Inspect(v, d, star, value);
        v.MouseLeave += (_, _) => _hover.Children.Clear();
        return v;
    }

    private const double CardInfoW = 360;

    /// <summary>Metni yüzde ve sayılar vurgulanmış satıra çevirir ("+%20" bölünmez, sayılar altın ve kalın).</summary>
    private static TextBlock Rich(string text, double size, Brush? fg = null, Brush? hi = null)
    {
        var t = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(size), Foreground = fg ?? HanUi.Br(HanUi.Parchment), TextWrapping = TextWrapping.Wrap, LineHeight = HanUi.S(size) * 1.32 };
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"([+−-]?%\d+|[+−-]?\d+(?:[.,]\d+)?)");
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            var num = System.Text.RegularExpressions.Regex.IsMatch(part, @"^[+−-]?%?\d");
            // satır sonu "%" ile sayının arasına düşmesin: kelime birleştirici
            var txt = num ? part.Replace("%", "%\u2060").Replace("+", "+\u2060").Replace("−", "−\u2060") : part;
            var run = new System.Windows.Documents.Run(txt);
            if (num) { run.FontWeight = FontWeights.Bold; run.Foreground = hi ?? HanUi.GoldText; }   // sayı olmayan kısım TextBlock rengini alır
            t.Inlines.Add(run);
        }
        return t;
    }

    /// <summary>
    /// Kart bilgi paneli (Witcher 3'ün eşya ipucu düzeni, oyunun kendi yazısı PF Din ile): üstte tür etiketi ve ad, süslü ayraç,
    /// güç / satır / kademe şeridi, yetenek (ikonu, adı, açıklaması; sayılar vurgulu), kahramanın gücü ve bağışıklığı, sınıflar ve
    /// eşikleri, altta satış değeri.
    /// </summary>
    private StackPanel CardInfo(CardDef d, int star, decimal? value)
    {
        var tier = HanCardView.TierColor(d);
        var w = CardInfoW - 40;
        var info = new StackPanel { Width = w, Margin = new Thickness(20, 16, 20, 16) };
        // tür etiketi
        var kicker = new StackPanel { Orientation = Orientation.Horizontal };
        kicker.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(tier), Margin = new Thickness(0, 0, 8, 1), VerticalAlignment = VerticalAlignment.Center });
        kicker.Children.Add(HanUi.Label(d.IsHero ? "Kahraman" : d.IsUnit ? $"Birim  ·  Kademe {d.Tier}" : $"Özel kart  ·  Kademe {d.Tier}", 11, HanUi.Br(tier)));
        info.Children.Add(kicker);
        // ad + yıldız
        var title = HanUi.Title(d.Name, d.Name.Length > 18 ? 24 : 27, HanUi.GoldText);
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 3, 0, 0);
        title.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
        info.Children.Add(title);
        if (star > 1 && !d.IsHero) info.Children.Add(HanUi.T(new string('★', star) + (star == 3 ? "  en üst yıldız" : "  yıldız"), 14, HanUi.Br(Color.FromRgb(0xff, 0xd7, 0x6a)), FontWeights.Bold));
        var rule = HanUi.Rule(w, tier);
        rule.Margin = new Thickness(0, 9, 0, 10);
        info.Children.Add(rule);

        // güç · satır · kademe şeridi
        if (d.IsUnit)
        {
            var strip = new Grid();
            foreach (var stars in new[] { 0.7, 1.35, 1.0 }) strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stars, GridUnitType.Star) });
            FrameworkElement Cell(string label, string val, FrameworkElement? icon, Brush? vfg = null)
            {
                var c = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                c.Children.Add(HanUi.Label(label, 10));
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                if (icon != null) { icon.Margin = new Thickness(0, 0, 6, 0); icon.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(icon); }
                var v = HanUi.Number(val, val.Length > 3 ? 18 : 21, vfg ?? HanUi.Br(HanUi.Parchment));
                v.VerticalAlignment = VerticalAlignment.Center;
                v.TextTrimming = TextTrimming.CharacterEllipsis;
                line.Children.Add(v);
                c.Children.Add(line);
                return c;
            }
            var basePow = Tuning.Power(d, star);
            var shown = value ?? basePow;
            var pow = Cell("Güç", Tuning.Fmt(shown), null, shown > basePow ? HanUi.Br(HanUi.Good) : shown < basePow ? HanUi.Br(HanUi.Bad) : HanUi.GoldText);
            var rowIcon = d.Agile ? null : Assets.Ui(d.Row switch { Row.Close => "row_close", Row.Ranged => "row_ranged", _ => "row_siege" }) is { } ri ? new Image { Source = ri, Width = 22, Height = 22 } : null;
            var row = Cell("Satır", d.Agile ? "Çevik" : HT.Name(d.Row), rowIcon);
            var third = d.IsHero ? Cell("Durum", "Bağışık", null, HanUi.GoldText) : Cell("Satış", $"{Tuning.SellValue(d, star)} altın", null);
            Grid.SetColumn(row, 1); Grid.SetColumn(third, 2);
            strip.Children.Add(pow); strip.Children.Add(row); strip.Children.Add(third);
            info.Children.Add(strip);
            if (shown != basePow) info.Children.Add(HanUi.Small($"Kartın kendi gücü {basePow}; masadaki etkilerle {Tuning.Fmt(shown)}.", 12.5));
        }

        // yetenek / kahraman gücü / özel kart etkisi
        void Section(string head, string body, Geometry? icon, Brush headFg)
        {
            var box = new Border
            {
                Background = Theme.B("#30000000"), BorderBrush = HanUi.Br(HanUi.GoldDeep, 0x70), BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 2), Margin = new Thickness(0, 12, 0, 0)
            };
            var sp = new StackPanel();
            var hl = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null)
            {
                var md = HanIcons.Medal(icon, 28);
                md.Margin = new Thickness(0, 0, 9, 0);
                md.VerticalAlignment = VerticalAlignment.Center;
                hl.Children.Add(md);
            }
            var ht = HanUi.T(HanUi.Tracked(HanUi.Upper(head), 1), 14, headFg, FontWeights.Bold);
            ht.VerticalAlignment = VerticalAlignment.Center;
            hl.Children.Add(ht);
            sp.Children.Add(hl);
            var bt = Rich(body, 15.5);
            bt.Margin = new Thickness(0, 5, 0, 0);
            sp.Children.Add(bt);
            box.Child = sp;
            info.Children.Add(box);
        }
        if (d.IsHero)
        {
            {
                var htag = d.Tags[0];
                var hth = _screen == Screen.Battle && Session.MyBattle != null ? B.Sides[Mine].Th(htag) : Me.Th(htag);
                var tiers = HT.HeroTiers(d.Hero);
                var bp = Tuning.Breakpoints(htag);
                var tierNow = hth >= 3 ? 2 : hth >= 2 ? 1 : 0;
                var body = HT.Describe(d.Hero)
                    + $"\n{(tierNow == 1 ? "▶ " : "")}II · {HT.Name(htag)} {bp[1]} kart: {tiers[0]}"
                    + $"\n{(tierNow == 2 ? "▶ " : "")}III · {HT.Name(htag)} {bp[2]} kart: {tiers[1]}";
                Section(d.Name.Split(' ').Last() + "'in gücü", body, null, HanUi.GoldText);
            }
            if (d.Ability == Ability.Ambush) Section("Pusu", HT.Describe(Ability.Ambush), HanIcons.For(Ability.Ambush), HanUi.Br(Color.FromRgb(0xff, 0xe2, 0xa0)));
            var imm = HanUi.T("Kahraman: hava, Kavurma ve karta hedefli etkiler işlemez.", 13, HanUi.Br(HanUi.Muted), FontWeights.Normal);
            imm.FontStyle = FontStyles.Italic;
            imm.TextWrapping = TextWrapping.Wrap;
            imm.Margin = new Thickness(0, 8, 0, 0);
            info.Children.Add(imm);
        }
        else if (!d.IsUnit) Section(HanCardView.Keyword(d), HT.Describe(d.Special), HanIcons.For(d), HanUi.Br(Color.FromRgb(0x9f, 0xd8, 0xff)));
        else if (d.Ability != Ability.None) Section(HanCardView.Keyword(d), HT.Describe(d.Ability), HanIcons.For(d), HanUi.Br(Color.FromRgb(0xff, 0xe2, 0xa0)));
        else if (d.Agile) Section("Çevik", "Yakın ya da Menzilli satıra konabilir.", HanIcons.For(d), HanUi.Br(Color.FromRgb(0xff, 0xe2, 0xa0)));

        // bu an geçerli yüzdeler (İntikam, Coşku, Eredin, Haydut)
        {
            var inBattle = _screen == Screen.Battle && Session.MyBattle != null;
            var bs = inBattle ? B.Sides[Mine] : null;
            var live = HT.LiveNotes(d, inBattle ? bs!.Prev : null, bs?.EredinRow, inBattle ? B.Sides[1 - Mine].EredinRow : null,
                inBattle ? bs!.Th(global::HanLigi.Tag.Bandit) : Me.Th(global::HanLigi.Tag.Bandit),
                inBattle ? bs!.EredinPct : 0.30, inBattle ? B.Sides[1 - Mine].EredinPct : 0.30);
            if (live.Count > 0) Section("Şu an", string.Join("\n", live), null, HanUi.Br(Color.FromRgb(0x9f, 0xe8, 0xa8)));
        }

        // sınıflar ve eşikleri (sahadaki sayınla)
        if (d.IsUnit && d.Tags.Length > 0)
        {
            var counts = Me.TagCounts();
            foreach (var tag in d.Tags)
            {
                var c = HanCardView.TagColor(tag);
                var bp = Tuning.Breakpoints(tag);
                var th = HT.Thresholds(tag);
                var n = counts.GetValueOrDefault(tag);
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 3) };
                head.Children.Add(HanUi.Chip(HT.Name(tag), c, 12, true));
                var cnt = HanUi.Small($"   sahanda {n}", 12.5);
                cnt.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(cnt);
                info.Children.Add(head);
                for (var i = 0; i < 3; i++)
                {
                    var on = n >= bp[i];
                    var line = new Grid { Margin = new Thickness(2, 2, 0, 0) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                    line.ColumnDefinitions.Add(new ColumnDefinition());
                    var num = HanUi.Number(bp[i].ToString(), 15, on ? new SolidColorBrush(c) : HanUi.Br(HanUi.Muted, 0x90));
                    var txt = Rich(th[i], 13.5, on ? HanUi.Br(HanUi.Parchment) : HanUi.Br(HanUi.Muted, 0xb0), on ? HanUi.GoldText : HanUi.Br(HanUi.Muted));
                    Grid.SetColumn(txt, 1);
                    line.Children.Add(num);
                    line.Children.Add(txt);
                    info.Children.Add(line);
                }
            }
        }
        return info;
    }

    /// <summary>Anlık kart inceleme: büyük kart ve açıklaması, kartın yanında (ekrana sığacak tarafta).</summary>
    private void Inspect(FrameworkElement anchor, CardDef d, int star, decimal? value)
    {
        _hover.Children.Clear();
        if (_dragging) return;
        Rect r;
        try { r = anchor.TransformToAncestor(_stage).TransformBounds(new Rect(anchor.RenderSize)); }
        catch { return; }
        var g = new StackPanel { Orientation = Orientation.Horizontal };
        var info = CardInfo(d, star, value);
        info.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // yalnız masadaki kart hafifçe kalkar (depo, pusu, ödül/seçim penceresi ve alt şerit kartları kıpırdamaz; tezgâh kartları kendileri yükselir)
        if (_units.IsAncestorOf(anchor)) HoverLift(anchor, r);
        g.Children.Add(HanUi.Frame(CardInfoW, info.DesiredSize.Height + 4, info, 0xf8, HanCardView.TierColor(d), 12));
        const double gw = CardInfoW;
        g.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var gh = g.DesiredSize.Height;
        var x = r.Right + 14 + gw < 1906 ? r.Right + 14 : r.Left - 14 - gw;
        var y = Math.Clamp(r.Top + r.Height / 2 - gh / 2, 64, 1080 - gh - 8);
        if (r.Top > BarY - 10) y = BarY - gh - 8;   // alt şerit: üstünde aç
        Canvas.SetLeft(g, Math.Clamp(x, 8, 1920 - gw - 8));
        Canvas.SetTop(g, y);
        _hover.Children.Add(g);
    }

    private static TextBlock Shadowed(TextBlock t, double blur = 6)
    {
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 1, Opacity = 1 };
        return t;
    }

    private static TextBlock Txt(string s, double size, Brush? fg = null, bool bold = false, TextAlignment align = TextAlignment.Left, bool wrap = false) => Shadowed(HanUi.T(s, size, fg, bold ? FontWeights.Bold : FontWeights.Normal, align, wrap));

    // ---------- süre ----------

    /// <summary>
    /// Süre: üst şeridin altında ince bir çubuk akar (WPF animasyonu: GPU'da, kare hızında) ve sağında saniye yazar. Sayaç
    /// yalnız saniye değişince güncellenir; son beş saniyede tıkırdar.
    /// </summary>
    private void StartTimer(int seconds, Action onExpire)
    {
        StopTimer();
        if (!_live || !Session.Timed) return;
        _deadline = DateTime.UtcNow.AddSeconds(seconds);
        _onExpire = onExpire;
        _lastShown = -1;
        var track = new Border { Width = TopW - 40, Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.B("#88000000") };
        Canvas.SetLeft(track, TopX + 20);
        Canvas.SetTop(track, 52);
        _clockLayer.Children.Add(track);
        var fill = new Border { Width = TopW - 40, Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.B("#e0b85a") };
        var sc = new ScaleTransform(1, 1);
        fill.RenderTransform = sc;
        Canvas.SetLeft(fill, TopX + 20);
        Canvas.SetTop(fill, 52);
        _clockLayer.Children.Add(fill);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(seconds)));
        _clock = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(24), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffe9a3"), Width = 44, TextAlignment = TextAlignment.Right };
        Canvas.SetLeft(_clock, TopX + TopW - 56);
        Canvas.SetTop(_clock, 14);
        _clockLayer.Children.Add(_clock);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            var left = (_deadline - DateTime.UtcNow).TotalSeconds;
            var shown = (int)Math.Max(0, Math.Ceiling(left));
            if (shown != _lastShown && _clock != null)
            {
                _lastShown = shown;
                _clock.Text = shown.ToString();
                if (shown <= 5)
                {
                    _clock.Foreground = Theme.Bad;
                    fill.Background = Theme.Bad;
                    if (shown > 0) Sfx.Play("han_tick");
                }
            }
            if (left > 0) return;
            var f = _onExpire;
            StopTimer();
            f?.Invoke();
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
        _onExpire = null;
        _clock = null;
        _clockLayer.Children.Clear();
    }

    /// <summary>Animasyon adımı: kuşak değişmediyse çalışır.</summary>
    private void After(int ms, int gen, Action a) => Fx.After(ms, () => { if (gen == _gen) a(); });

    private static string SchoolName(School? s) => s is { } x ? HT.Name(x) : "—";

    /// <summary>Oyuncu çerçevesinin köşesine konan küçük okul simgesi; üstüne gelince okulun açıklaması çıkar.</summary>
    private static FrameworkElement SchoolPip(School? s, int mageLevel, double size, bool mine, HorizontalAlignment ha, VerticalAlignment va, Thickness margin)
    {
        var g = new Grid { Width = size, Height = size, Background = Brushes.Transparent, HorizontalAlignment = ha, VerticalAlignment = va, Margin = margin };
        g.Children.Add(HanArt.SchoolMedallion(s, size, mine));
        if (s is { } school) g.ToolTip = HT.Name(school) + " okulu\n" + HT.DescribeLive(school, mageLevel);
        return g;
    }

    /// <summary>Seçilen okulun rozeti: amblem, ad ve kısa özet; üstüne gelince tam açıklama ve güncel sayılar.</summary>
    private static FrameworkElement SchoolBadge(School? s, int mageLevel, bool mine)
    {
        var g = new Grid { Width = SideW, Height = 64, Background = Brushes.Transparent };
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(32, 8, 8, 32), BorderThickness = new Thickness(1.5), BorderBrush = mine ? Theme.B("#c9a24b") : Theme.B("#a05a48"),
            Background = new LinearGradientBrush(Color.FromArgb(0xe0, 0x2c, 0x20, 0x12), Color.FromArgb(0xc0, 0x0c, 0x07, 0x04), 0)
        });
        var med = HanArt.SchoolMedallion(s, 52, mine);
        med.HorizontalAlignment = HorizontalAlignment.Left;
        med.Margin = new Thickness(6, 0, 0, 0);
        g.Children.Add(med);
        var sp = new StackPanel { Margin = new Thickness(66, 6, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "OKULUN", FontFamily = HanUi.Font, FontSize = HanUi.S(10), FontWeight = FontWeights.Bold, Foreground = Theme.B("#a8905a") });
        sp.Children.Add(new TextBlock { Text = SchoolName(s), FontFamily = HanUi.Font, FontSize = HanUi.S(19), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffe9a3") });
        g.Children.Add(sp);
        if (s is { } school) g.ToolTip = HT.Name(school) + " okulu\n" + HT.DescribeLive(school, mageLevel);
        return g;
    }

    private string? Face(int pid) => pid == Session.Me ? Settings.Portrait : Session.FaceOf(pid);

    // ---------- üst şerit, oyuncu listesi ----------

    /// <summary>Üst orta: ince şerit. Solda evre çipi, ortada tek cümlelik yönlendirme, sağda süre.</summary>
    private void TopBar(string phase, string hint, Brush? hintColor = null)
    {
        var g = new Grid { Width = TopW, Height = 50 };
        g.Children.Add(Panel(TopW, 50, null, 0xdc, null, 12));
        var chip = HanUi.Chip(phase, HanUi.Gold, 12, true);
        chip.Margin = new Thickness(14, 0, 0, 0);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(chip);
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var left = 14 + chip.DesiredSize.Width + 12;
        var h = HanUi.T(hint, hint.Length > 72 ? 16 : 18, hintColor ?? HanUi.Br(HanUi.Parchment), FontWeights.SemiBold, TextAlignment.Center);
        // uzun cümle şeride sığana kadar küçülür (kesilip "depoy…" diye bitiyordu)
        HanUi.Fit(h, TopW - left - 64 - 4, 13);
        h.MaxWidth = double.PositiveInfinity;
        h.VerticalAlignment = VerticalAlignment.Center;
        h.Margin = new Thickness(left, 0, 64, 2);
        g.Children.Add(h);
        Put(g, TopX, 6);
        TopButtons();
    }

    private void TopButtons()
    {
        Put(Act("?", null, "han-help", () => { _help = true; Render(); }, HanButtonKind.Ghost, 38, 34, tip: "Nasıl oynanır"), 12, 14);
        Put(Act("✕", null, "han-exit", AskExit, HanButtonKind.Ghost, 38, 34, tip: "Maçtan çık (Esc)"), 56, 14);
    }

    private bool _exitAsk;

    /// <summary>Maç sürerken çıkış önce sorulur (maç kaybolur); okul seçiminde ya da maç bitince doğrudan çıkılır.</summary>
    private void AskExit()
    {
        if (_screen is Screen.School or Screen.Final) { Exit(); return; }
        _exitAsk = true;
        _help = false;
        Render();
    }

    private void Exit() { StopTimer(); _gen++; _exitAsk = false; _back(); }

    private void DrawExitAsk()
    {
        Put(new Border { Width = 1920, Height = 1080, Background = Theme.B("#c8000000") }, 0, 0);
        var sp = new StackPanel { Margin = new Thickness(56, 40, 56, 40) };
        sp.Children.Add(HanUi.Label("Han Ligi", 13));
        sp.Children.Add(HanUi.Display("Maçtan çıkılsın mı?", 40, TextAlignment.Left));
        var rule = HanUi.Rule(620);
        rule.Margin = new Thickness(0, 10, 0, 16);
        sp.Children.Add(rule);
        sp.Children.Add(HanUi.Body("Çıkarsan bu maçtaki yerini kaybedersin.", 17));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        var stay = Btn("Masada kal", "han-exit-cancel", () => { _exitAsk = false; Render(); }, true, width: 230, height: 52);
        stay.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(stay);
        row.Children.Add(Btn("Maçtan çık", "han-exit-confirm", Exit, width: 210, height: 52));
        sp.Children.Add(row);
        Put(Panel(740, 330, sp, 0xf0, null, 16), 590, 360);
    }

    /// <summary>Sağ kenar: oyuncular (portre, can, seviye), cana göre sıralı. Rakip kırmızı, sen altın çerçeveli.</summary>
    private void PlayerList()
    {
        var opp = Me.Alive && M.Phase is Phase.Prep or Phase.Battle && !M.IsPve ? Me.OppId : -1;
        var y = 66.0;
        foreach (var p in M.Players.OrderByDescending(AliveShown).ThenByDescending(HpShown).ThenBy(p => AliveShown(p) ? 0 : p.Place))
        {
            var me = p.Id == Session.Me;
            var foe = p.Id == opp;
            var alive = AliveShown(p);
            var hpNow = HpShown(p);
            var g = new Grid { Width = ListW, Height = 44, Opacity = alive ? 1 : 0.35 };
            g.Children.Add(new Path
            {
                Data = HanUi.ChamferGeometry(ListW, 44, 7, 0.5), StrokeThickness = me || foe ? 1.4 : 1,
                Stroke = me ? HanUi.GoldEdge : foe ? HanUi.Br(HanUi.Bad, 0xd0) : HanUi.Br(HanUi.GoldDeep, 0x80),
                Fill = new LinearGradientBrush(Color.FromArgb(0xd8, me ? (byte)0x2a : (byte)0x18, me ? (byte)0x1e : (byte)0x11, 0x0a), Color.FromArgb(0xc8, 0x0c, 0x08, 0x05), 0)
            });
            var port = Portraits.Medallion(Face(p.Id), 40);
            port.HorizontalAlignment = HorizontalAlignment.Left;
            port.VerticalAlignment = VerticalAlignment.Center;
            port.Margin = new Thickness(2, 0, 0, 0);
            g.Children.Add(port);
            if (p.School != null) g.Children.Add(SchoolPip(p.School, 0, 19, me, HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(30, 0, 0, 0)));
            var name = new TextBlock
            {
                Text = (foe ? "⚔ " : "") + p.Name, FontFamily = HanUi.Font, FontSize = HanUi.S(13), FontWeight = me || foe ? FontWeights.Bold : FontWeights.SemiBold, Width = 100, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = me ? Theme.B("#ffe9a3") : foe ? Theme.B("#f0a090") : Theme.Text, Margin = new Thickness(48, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
            };
            g.Children.Add(name);
            var sub = new TextBlock { Text = alive ? $"Sv {p.Level}" + (p.Streak <= -2 ? $" · {-p.Streak} yenilgi" : p.Streak >= 3 ? $" · {p.Streak} galibiyet" : "") : $"{p.Place}.", FontFamily = HanUi.Font, FontSize = HanUi.S(11), Foreground = Theme.TextDim, Margin = new Thickness(48, 23, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(sub);
            var frac = Math.Clamp(hpNow, 0, 100) / 100.0;
            var hpc = hpNow > 50 ? Color.FromRgb(0x8f, 0xd0, 0x62) : hpNow > 25 ? Color.FromRgb(0xe8, 0xc0, 0x50) : Color.FromRgb(0xe0, 0x5a, 0x48);
            g.Children.Add(new Border { Width = 132 * frac, Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(48, 0, 0, 4), Background = new SolidColorBrush(hpc) });
            g.ToolTip = $"{p.Name} · Seviye {p.Level} · {SchoolName(p.School)} okulu · {p.Wins} galibiyet, {p.Losses} yenilgi";
            Put(g, ListX, y);
            // can düşünce sayı yuvarlanarak iner (sayarken sayaç katmanında, dururken satırın içinde)
            var hp = HanUi.Number(alive ? hpNow.ToString() : "✝", 21, new LinearGradientBrush(Color.Add(hpc, Color.FromRgb(0x50, 0x50, 0x50)), hpc, 90));
            hp.Tag = "hp:" + p.Id;
            if (alive && Count("hp:" + p.Id, hp, hpNow, Whole, 700))
            {
                hp.Width = ListW - 10;
                hp.TextAlignment = TextAlignment.Right;
                hp.Opacity = g.Opacity;
                hp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(hp, ListX);
                Canvas.SetTop(hp, y + 22 - hp.DesiredSize.Height / 2 - 1);
                _counters.Children.Add(hp);
            }
            else
            {
                if (!alive) _shownNum.Remove("hp:" + p.Id);
                hp.HorizontalAlignment = HorizontalAlignment.Right;
                hp.VerticalAlignment = VerticalAlignment.Center;
                hp.Margin = new Thickness(0, 0, 10, 2);
                g.Children.Add(hp);
            }
            _hpAt[p.Id] = new Point(ListX + ListW - 24, y + 22);
            // can o an düştüyse (öteki masada savaş bitti): sayı parlar, yanında kayıp yazar, can çubuğunda kaybedilen parça
            // önce açık renkte kalıp erir
            if (_hpShownLast.TryGetValue(p.Id, out var before) && hpNow < before && _live)
            {
                Pop(hp);
                HanArt.FloatText(_fx, "−" + (before - hpNow), new Point(ListX - 26, y + 22), Color.FromRgb(0xff, 0x5a, 0x48), 24, 1500);
                var lost = new Border { Width = 132 * (Math.Clamp(before, 0, 100) / 100.0 - frac), Height = 2, Background = HanUi.Br(Color.FromRgb(0xff, 0xe0, 0xc0)), RenderTransformOrigin = new Point(0, 0.5) };
                Canvas.SetLeft(lost, ListX + 48 + 132 * frac);
                Canvas.SetTop(lost, y + 44 - 4 - 2);
                _counters.Children.Add(lost);
                var melt = new ScaleTransform(1, 1);
                lost.RenderTransform = melt;
                melt.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(650)) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            }
            _hpShownLast[p.Id] = hpNow;
            y += 48;
        }
    }

    private readonly Dictionary<int, Point> _hpAt = new();

    // ---------- masa: yamuklar, kartlar ----------

    private static PathGeometry TrapGeometry(Rect board, double inset = 0)
    {
        var r = new Rect(board.X + inset, board.Y + inset, Math.Max(1, board.Width - 2 * inset), Math.Max(1, board.Height - 2 * inset));
        var p = HanScene.Trapezoid(r);
        var fig = new PathFigure { StartPoint = p[0], IsClosed = true, IsFilled = true };
        fig.Segments.Add(new PolyLineSegment(new[] { p[1], p[2], p[3] }, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    /// <summary>Tahtadaki bir dikdörtgeni masanın perspektifinde boyar (satır vurgusu, sis, kazanan satır).</summary>
    private Path Trap(Rect board, Brush? fill, Brush? stroke = null, double thick = 0, double inset = 3, Effect? fx = null)
    {
        var p = new Path { Data = TrapGeometry(board, inset), Fill = fill, Stroke = stroke, StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Effect = fx };
        _rows.Children.Add(p);
        return p;
    }

    /// <summary>Satırın ekrandaki bandı: üst ve alt kenarın y'si, ortadaki sol/sağ x.</summary>
    private static (double Top, double Bottom, double Left, double Right) Band(bool mine, Row r, double rightPad = 70)
    {
        var y = RowY(mine, r);
        var cy = y + RowH / 2;
        return (HanScene.ToScreen(new Point(RowX + RowW / 2, y + 5)).Y, HanScene.ToScreen(new Point(RowX + RowW / 2, y + RowH - 5)).Y,
            HanScene.ToScreen(new Point(RowX + 10, cy)).X, HanScene.ToScreen(new Point(RowX + RowW - rightPad, cy)).X);
    }

    /// <summary>
    /// Bir satırın kartlarını masaya dizer: her kart satır bandına sığacak boyda, hafifçe masaya yaslanmış (dikey basık) ve
    /// ayağında gölgeyle durur; kartlar arkadaki satırı örtmez. Kart merkezleri (ekran) <paramref name="pos"/> içine yazılır.
    /// </summary>
    private void PlaceRow(IReadOnlyList<(int Uid, FrameworkElement El)> cards, bool mine, Row r, Dictionary<int, Point>? pos = null)
    {
        if (cards.Count == 0) return;
        var (top, bottom, left, right) = Band(mine, r);
        var k = (bottom - top) * 0.98 / (DesignH * Lean);
        var sw = DesignW * k;
        var avail = right - left;
        var step = cards.Count == 1 ? 0 : Math.Min(sw + 8 * k, (avail - sw) / (cards.Count - 1));
        var total = step * (cards.Count - 1) + sw;
        var x0 = left + (avail - total) / 2 + sw / 2;
        for (var i = 0; i < cards.Count; i++)
        {
            var p = PlaceCard(cards[i].El, new Point(x0 + i * step, bottom), k, i);
            _unitEls[cards[i].Uid] = cards[i].El;
            _cardW[cards[i].Uid] = sw;
            if (pos != null) pos[cards[i].Uid] = p;
        }
    }

    /// <summary>Bir kartı ayağı <paramref name="foot"/> noktasında olacak biçimde koyar (ölçek <paramref name="k"/>). Ekrandaki merkezi döner.</summary>
    private Point PlaceCard(FrameworkElement el, Point foot, double k, int z = 0, Canvas? layer = null)
    {
        layer ??= _units;
        var sw = DesignW * k;
        var shadow = new Ellipse { Width = sw * 1.1, Height = sw * 0.16, IsHitTestVisible = false, Fill = new RadialGradientBrush(Color.FromArgb(0xa0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) };
        Canvas.SetLeft(shadow, foot.X - sw * 0.55);
        Canvas.SetTop(shadow, foot.Y - sw * 0.07);
        layer.Children.Add(shadow);
        var holder = new Grid { Width = DesignW, Height = DesignH, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(k, k * Lean) };
        if (el.Width != DesignW) { var f = DesignW / el.Width; el.LayoutTransform = new ScaleTransform(f, f); }
        holder.Children.Add(el);
        Canvas.SetLeft(holder, foot.X - DesignW / 2);
        Canvas.SetTop(holder, foot.Y - DesignH);
        System.Windows.Controls.Panel.SetZIndex(holder, (int)foot.Y * 10 + z);
        layer.Children.Add(holder);
        return new Point(foot.X, foot.Y - DesignH * k * Lean / 2);
    }

    /// <summary>Ekran katmanında, tahtadaki bir noktanın üstünde ortalanmış öğe (rozet, yazı).</summary>
    private T PutU<T>(T el, Point board, double dy = 0, Canvas? layer = null) where T : FrameworkElement
    {
        var p = HanScene.ToScreen(board);
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = double.IsNaN(el.Width) ? el.DesiredSize.Width : el.Width;
        var h = double.IsNaN(el.Height) ? el.DesiredSize.Height : el.Height;
        Canvas.SetLeft(el, p.X - w / 2);
        Canvas.SetTop(el, p.Y - h / 2 + dy);
        layer ??= _units;
        if (layer == _units) System.Windows.Controls.Panel.SetZIndex(el, 100000);
        layer.Children.Add(el);
        return el;
    }

    /// <summary>Satırın ekrandaki merkezi ve genişliği.</summary>
    private static (Point Center, double Width) RowS(bool mine, Row r)
    {
        var c = RowCenter(mine, r);
        var a = HanScene.ToScreen(new Point(RowX, c.Y));
        var b = HanScene.ToScreen(new Point(RowX + RowW, c.Y));
        return (HanScene.ToScreen(c), b.X - a.X);
    }

    /// <summary>Satırın gücünü gösteren rozet: metal halkalı koyu disk, altın sayı; kazanınca parlar.</summary>
    private static FrameworkElement ScoreBadge(string text, bool mine, bool strong = false, double size = 54)
    {
        var g = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        var ring = strong ? HanUi.GoldEdge : mine ? (Brush)new LinearGradientBrush(Color.FromRgb(0xd8, 0xb4, 0x62), Color.FromRgb(0x5e, 0x45, 0x1c), 90) : new LinearGradientBrush(Color.FromRgb(0xe0, 0x84, 0x70), Color.FromRgb(0x5a, 0x22, 0x1a), 90);
        g.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromRgb(0x30, 0x22, 0x14), Color.FromRgb(0x08, 0x06, 0x04)) { GradientOrigin = new Point(0.5, 0.3) }, StrokeThickness = strong ? 3 : 2, Stroke = ring,
            Effect = strong ? new DropShadowEffect { Color = Color.FromRgb(0xff, 0xd8, 0x70), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.95 } : new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8 }
        });
        g.Children.Add(new Ellipse { Margin = new Thickness(5), Stroke = HanUi.Br(HanUi.Gold, 0x30), StrokeThickness = 1 });
        var t = HanUi.Number(text, size * (text.Length > 2 ? 0.34 : 0.44), strong ? HanUi.GoldText : HanUi.Br(HanUi.Parchment));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        return g;
    }

    /// <summary>Kart iner: kısa bir süzülüş ve belirme (ölçek sıçraması yok: masa kıpırdamaz).</summary>
    private void DropIn(FrameworkElement el, int delayMs = 0, double fromY = -28)
    {
        if (!_live) return;
        var move = new TranslateTransform(0, fromY);
        el.RenderTransform = move;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var begin = TimeSpan.FromMilliseconds(delayMs);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(240)) { BeginTime = begin, EasingFunction = ease });
        var to = el.Opacity;   // soluk (alınamaz) kart soluk kalır: animasyon tam görünürlüğe çekmesin
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(180)) { BeginTime = begin });
    }

    // ---------- sürükle-bırak ----------

    private int? _pressUid;
    private bool _pressWasSelected;
    private Point _pressPoint;
    private bool _dragging;
    private FrameworkElement? _ghost;

    /// <summary>Kendi kartım: basınca seçilir (bırakılabilecek yerler parlar); sürüklenirse hayaleti imleci izler.</summary>
    private HanCardView Own(OwnedCard c, double w, decimal? value)
    {
        var uid = c.Uid;
        var el = CardEl(c.Def, c.Star, w, "han-card:" + c.Uid, () =>
        {
            if (_dragging) return;
            // fareyle: basışta seçildi, bırakışta yalnız "zaten seçiliydi" ise kapanır. Sınamada yalnız bırakış gelir: aç/kapa.
            if (_pressUid == uid) { _pressUid = null; if (_pressWasSelected) _sel = null; Render(); return; }
            _sel = _sel == uid ? null : uid;
            Sfx.Play("han_click");
            Render();
        }, value, _sel == c.Uid);
        _ownVals[uid] = value;
        el.MouseLeftButtonDown += (_, e) =>
        {
            if (_busy || _screen != Screen.Prep) return;
            e.Handled = true;
            _pressUid = uid;
            _pressWasSelected = _sel == uid;
            _pressPoint = e.GetPosition(_stage);
            if (_sel != uid) { _sel = uid; Sfx.Play("han_click"); Render(); }
        };
        _ownEls[uid] = el;
        return el;
    }

    /// <summary>Bu çizimdeki kendi kartlarım (sürükle-bırak takasında hedef bulmak için).</summary>
    private readonly Dictionary<int, HanCardView> _ownEls = new();
    private FrameworkElement? _swapMark;

    /// <summary>Kartın sahnedeki kapladığı alan (perspektifli yerleşim dahil); görünmüyorsa null.</summary>
    private Rect? StageBounds(FrameworkElement el)
    {
        if (el.Visibility != Visibility.Visible || el.ActualWidth <= 0 || !_stage.IsAncestorOf(el)) return null;
        try { return el.TransformToAncestor(_stage).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Noktanın altındaki kendi kartım (sürüklenen hariç); birden çoksa merkezi en yakın olan.</summary>
    private int? OwnCardAt(Point p, int except)
    {
        int? best = null;
        var bestD = double.MaxValue;
        foreach (var (uid, el) in _ownEls)
        {
            if (uid == except || StageBounds(el) is not { } r || !r.Contains(p)) continue;
            var d = (new Point(r.X + r.Width / 2, r.Y + r.Height / 2) - p).LengthSquared;
            if (d < bestD) { bestD = d; best = uid; }
        }
        return best;
    }

    /// <summary>Sürüklenen kart başka bir kartımın üstüne bırakıldı: yer değiştirirler (olamıyorsa ret sesi).</summary>
    private void DropOnCard(int uid, int target)
    {
        // önce takas (sürükleme sürerken: kart, hayaletin bırakıldığı yerden yerine uçar), sonra sürükleme biter
        Do(() => Session.Act(HanActKind.SwapCards, uid, target), "han_place");
        EndDrag();
    }

    /// <summary>Sınama: kartı gerçek sürükle-bırak yolundan başka bir kartın merkezine bırakır (isabet denetimi dahil).</summary>
    internal bool TestDragOnto(int uid, int target)
    {
        if (_screen != Screen.Prep || !_ownEls.TryGetValue(target, out var el) || StageBounds(el) is not { } r) return false;
        _pressUid = uid;
        _dragging = true;
        if (OwnCardAt(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), uid) is not { } hit) { EndDrag(); return false; }
        DropOnCard(uid, hit);
        return true;
    }

    /// <summary>Sürüklerken altındaki kart: takas edilebilirse çevresinde "Yer değiştir" işareti.</summary>
    private void MarkSwapTarget(Point p, int uid)
    {
        if (_swapMark != null) { _fx.Children.Remove(_swapMark); _swapMark = null; }
        if (OwnCardAt(p, uid) is not { } t || !M.CanSwapCards(Session.Me, uid, t) || StageBounds(_ownEls[t]) is not { } r) return;
        var g = new Grid { Width = r.Width + 12, Height = r.Height + 12, IsHitTestVisible = false };
        g.Children.Add(new Border { BorderBrush = HanUi.GoldText, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(8), Background = HanUi.Br(HanUi.Gold, 0x26), Effect = new DropShadowEffect { Color = HanUi.Gold, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 } });
        var label = HanUi.Shadow(HanUi.Label("Yer değiştir", 12, HanUi.GoldText));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Top;
        label.Margin = new Thickness(0, -20, 0, 0);
        g.Children.Add(label);
        Canvas.SetLeft(g, r.X - 6);
        Canvas.SetTop(g, r.Y - 6);
        _swapMark = g;
        _fx.Children.Insert(0, g);
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_pressUid is not { } uid || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_stage);
        if (!_dragging)
        {
            if ((p - _pressPoint).Length < 9) return;
            if (Me.Cards.FirstOrDefault(c => c.Uid == uid) is not { } card) return;
            _dragging = true;
            _units.IsHitTestVisible = false;   // sürüklerken masadaki kartlar satır bırakma alanlarını örtmesin
            // depo/pusu bırakma alanları, o alandaki kartların üstüne çıkar (yalnız sürüklerken: seçiliyken kartlara tıklanabilsin)
            foreach (var z in _screenZones) System.Windows.Controls.Panel.SetZIndex(z, 1000);
            _hover.Children.Clear();
            StartDragGhost(card, p);
        }
        if (_ghost == null) return;
        _dragCursor = p;
        if (!_live) DragSnap();
        MarkSwapTarget(p, uid);
    }

    private void EndDrag()
    {
        // boşa bırakılan kart, hayaletin son yerinden yerine döner (Motion)
        if (_dragging && _pressUid is { } du && DragRect() is { } dr) _dropFrom = (du, dr);
        StopDragGhost();
        if (_ghost != null) _fx.Children.Remove(_ghost);
        _ghost = null;
        if (_swapMark != null) _fx.Children.Remove(_swapMark);
        _swapMark = null;
        var was = _dragging || _pressUid != null;
        _pressUid = null;
        _dragging = false;
        _units.IsHitTestVisible = true;
        if (was && _screen == Screen.Prep) Render();
    }

    /// <summary>Bırakma alanı: tıklanınca ya da üstüne bırakılınca <paramref name="act"/> çalışır. Masadaysa perspektifli yamuk.</summary>
    private Button DropZone(Geometry shape, Rect bounds, string tag, string label, Action act, bool danger = false, bool enabled = true, string? tip = null)
    {
        var c = danger ? Color.FromRgb(0xe0, 0x5a, 0x48) : Color.FromRgb(0xff, 0xe0, 0x8a);
        var face = new Grid { Width = bounds.Width, Height = bounds.Height, Opacity = enabled ? 1 : 0.4 };
        var local = shape.Clone();
        local.Transform = new TranslateTransform(-bounds.X, -bounds.Y);
        face.Children.Add(new Path
        {
            Data = local, Fill = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)), Stroke = new SolidColorBrush(c), StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 5, 3 },
            Effect = new DropShadowEffect { Color = c, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 }
        });
        var t = new TextBlock { Text = label, FontFamily = HanUi.Font, FontSize = HanUi.S(Math.Clamp(bounds.Height * 0.2, 13, 22)), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
        face.Children.Add(Shadowed(t, 4));
        var b = Bare(face, tag, act, enabled, tip);
        b.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging || !b.IsEnabled) return;
            e.Handled = true;
            EndDrag();
            act();
        };
        Canvas.SetLeft(b, bounds.X);
        Canvas.SetTop(b, bounds.Y);
        return b;
    }

    /// <summary>Masadaki bir dikdörtgen bölge için bırakma alanı (satırlar katmanına eklenir).</summary>
    private Button BoardZone(Rect board, string tag, string label, Action act, bool enabled = true, string? tip = null)
    {
        var g = TrapGeometry(board, 4);
        var b = DropZone(g, g.Bounds, tag, label, act, false, enabled, tip);
        System.Windows.Controls.Panel.SetZIndex(b, 1);
        _rows.Children.Add(b);
        return b;
    }

    private readonly List<Button> _screenZones = new();

    /// <summary>Ekrandaki dikdörtgen bölge için bırakma alanı (HUD katmanına eklenir).</summary>
    private Button ScreenZone(Rect r, string tag, string label, Action act, bool danger = false, bool enabled = true)
    {
        var b = DropZone(new RectangleGeometry(r, 8, 8), r, tag, label, act, danger, enabled);
        _c.Children.Add(b);
        _screenZones.Add(b);
        return b;
    }

    // ---------- çizim ----------

    /// <summary>Ölçüm: ekranı baştan kurmanın en uzun süresi (ms).</summary>
    internal static double RenderMaxMs;
    /// <summary>Ölçüm: son çizimin süresi (ms; tıklama gecikmesi ölçümü).</summary>
    internal static double RenderLastMs;

    private void Render()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { RenderCore(); }
        finally { RenderLastMs = sw.Elapsed.TotalMilliseconds; RenderMaxMs = Math.Max(RenderMaxMs, RenderLastMs); }
    }

    private void RenderCore()
    {
        // kartların çizimden önceki yerleri: yeri değişen kart eski yerinden süzülür (HanView.Motion.cs)
        var motion = BeginMotion();
        _arrival = motion?.From != null;
        // yarıda kalan sayaçlar: bu çizimde yeniden kurulmayanlar son değerlerine oturur
        var unfinished = _tickers.ToList();
        _tickers.Clear();
        // Önbellekli katmanlar baştan kurulurken önbellek bırakılır: süren animasyonlu kartlar kaldırılınca eski dokudan
        // kart kırıntıları kalıyordu (kart birleşince satırın uçlarında hayalet kenarlar). Sonda yeniden bağlanır.
        for (var i = 0; i < _cachedLayers.Length; i++) _cachedLayers[i].CacheMode = null;
        try { RenderBody(); }
        finally { for (var i = 0; i < _cachedLayers.Length; i++) _cachedLayers[i].CacheMode = _caches[i]; }
        foreach (var u in unfinished) if (_tickers.All(t => t.Key != u.Key)) _shownNum[u.Key] = u.To;
        // sayaç katmanı önbelleksizdir ama öğeleri tek tek önbellekte: efekt oynarken (bütün ekran her karede yeniden
        // birleşirken) gölgeli sayılar her karede yeniden çizilmesin; yalnız sayısı değişen kendi dokusunu yeniler
        foreach (UIElement el in _counters.Children) el.CacheMode ??= new BitmapCache(CacheScale) { SnapsToDevicePixels = true };
        EndMotion(motion);
    }

    /// <summary>Bu çizimde tezgâhtan (ya da ödülden) uçarak gelen bir kart var: yeni kart düşerek belirmez.</summary>
    private bool _arrival;

    private void RenderBody()
    {
        _screenZones.Clear();
        _counters.Children.Clear();
        _ownVals.Clear();
        _rows.Children.Clear();
        _anim.Children.Clear();
        _shop.Children.Clear();
        _units.Children.Clear();
        _c.Children.Clear();
        _unitEls.Clear();
        _ownEls.Clear();
        _cardW.Clear();
        _hover.Children.Clear();
        _scene.Visibility = Visibility.Visible;   // masa her ekranda arkada kalır; ara ekranlar onun üstünde saydam durur
        if (_screen == Screen.School) ShowTavern();
        switch (_screen)
        {
            case Screen.School: DrawSchool(); break;
            case Screen.Prep: DrawPrep(); break;
            case Screen.Activation:
            case Screen.Battle: DrawArena(); break;
            case Screen.Result: DrawResult(); break;
            default: DrawFinal(); break;
        }
        if (_help) DrawHelp();
        if (_exitAsk) DrawExitAsk();
        if (_waitEl != null) { _clockLayer.Children.Remove(_waitEl); _waitEl = null; }
        DrawWaiting();
        foreach (var layer in new DependencyObject[] { _rows, _units, _c, _shop }) FastTips(layer);
    }

    /// <summary>
    /// İpuçları beklemeden açılsın: WPF'te ipucu gecikmesi öğe başınadır (kalıtılmaz), bu yüzden ipucu taşıyan her öğeye tek tek
    /// 0 ms verilir (işaret madalyonları, simya, sinerjiler, düğmeler).
    /// </summary>
    private static void FastTips(DependencyObject root)
    {
        if (root is FrameworkElement fe && fe.ToolTip != null)
        {
            ToolTipService.SetInitialShowDelay(fe, 0);
            ToolTipService.SetBetweenShowDelay(fe, 0);
            ToolTipService.SetShowDuration(fe, 60000);
            if (fe.ToolTip is string text) fe.ToolTip = HanUi.Tip(text);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d) FastTips(d);
    }

    private void Backdrop(double dim)
    {
        Put(new Rectangle { Width = 1920, Height = 1080, Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * dim), 6, 4, 2)), IsHitTestVisible = false }, 0, 0);
    }

    // ---------- okul ----------

    /// <summary>Tam ekran sayfa başlığı: küçük üst etiket, büyük gösteri başlığı, alt cümle.</summary>
    private void PageHeader(string kicker, string title, string? sub, double y)
    {
        var sp = new StackPanel { Width = 1920 };
        var k = HanUi.Label(kicker, 13);
        k.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(k);
        var t = HanUi.Display(title, 56);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 4, 0, 6);
        sp.Children.Add(t);
        var r = HanUi.Rule(420);
        r.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(r);
        if (sub != null)
        {
            var b = HanUi.Body(sub, 17, HanUi.Br(HanUi.Muted), true, TextAlignment.Center);
            b.MaxWidth = 900;
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(b);
        }
        Put(sp, 0, y);
    }

    // ---------- han: okul seçiminde canlı oda, seçince kamera masaya iner ----------

    /// <summary>Canlı han görüntüsü (okul seçimi); yalnız canlı kipte. Masa katmanının hemen üstünde, arayüzün altında durur.</summary>
    private readonly Grid _tavernHost = new() { Width = 1920, Height = 1080 };
    private HanTavernView? _tavern;

    private void ShowTavern()
    {
        if (!_live || _tavern != null) return;
        _tavern = new HanTavernView(HanScene.BoardTexture());
        _tavernHost.Children.Add(_tavern);
    }

    /// <summary>
    /// Okul seçildi, hazırlık başlıyor: han görüntüsü her şeyin üstüne çıkar, kamera oyundaki masa görüşüne iner (son kare
    /// arkadaki masayla aynı), sonra görüntü sönerek altında hazır bekleyen hazırlık ekranını açar.
    /// </summary>
    private void FlyToTable()
    {
        if (_tavern is not { } tv) return;
        System.Windows.Controls.Panel.SetZIndex(_tavernHost, 1000);
        Sfx.Play("han_whoosh");
        tv.FlyTo(HanScene.GameCamera(), TavernFlyMs, () =>
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420));
            fade.Completed += (_, _) => DropTavern();
            _tavernHost.BeginAnimation(OpacityProperty, fade);
        });
    }

    private const double TavernFlyMs = 2600;

    private void DropTavern()
    {
        if (_tavern == null) return;
        _tavernHost.BeginAnimation(OpacityProperty, null);
        _tavernHost.Opacity = 1;
        _tavernHost.Children.Clear();
        System.Windows.Controls.Panel.SetZIndex(_tavernHost, 0);
        _tavern = null;
    }

    private void DrawSchool()
    {
        Backdrop(_tavern != null ? 0.18 : 0.45);
        TopButtons();
        PageHeader("Han Ligi", "Okulunu seç", "Sekiz oyuncu, yüz can; son ayakta kalan kazanır. Okulun, maç boyunca taşıyacağın pasif yetenektir.", 70);
        const double cw = 380, ch = 540, gap = 40;
        var x = 960 - (Me.SchoolOptions.Length * (cw + gap) - gap) / 2.0;
        var i = 0;
        foreach (var s in Me.SchoolOptions)
        {
            var sp = new StackPanel { Margin = new Thickness(28, 30, 28, 24) };
            var med = HanArt.SchoolMedallion(s, 168, true);
            med.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(med);
            var k = HanUi.Label("Okul", 12);
            k.HorizontalAlignment = HorizontalAlignment.Center;
            k.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(k);
            var name = HanUi.Display(HT.Name(s), 34);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(name);
            var rule = HanUi.Rule(220);
            rule.HorizontalAlignment = HorizontalAlignment.Center;
            rule.Margin = new Thickness(0, 8, 0, 12);
            sp.Children.Add(rule);
            var d = HanUi.Body(HT.Describe(s), 16.5, null, true, TextAlignment.Center);
            d.Height = 112;
            sp.Children.Add(d);
            var school = s;
            var b = Btn("Seç", "han-school:" + s, () => { Sfx.Play("han_levelup"); Session.ChooseSchool(school); if (Session.Online) Render(); }, Me.School == null, width: 230, height: 54);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(b);
            var panel = Panel(cw, ch, sp, 0xe8, null, 16);
            Put(panel, x, 290);
            if (_live) DropIn(panel, 90 + 120 * i, 30);
            x += cw + gap;
            i++;
        }
        if (Session.Online) return;
        var timed = Btn(Session.Timed ? "Süre sınırı: açık" : "Süre sınırı: kapalı", "han-timed", () => { Session.Timed = !Session.Timed; Render(); }, false, true, 280, $"Açıkken hazırlık {HanTimes.Prep} sn, hedef seçimi {HanTimes.Activation} sn, her çarpışma {HanTimes.Window} sn. İlk maçında kapalı bırak: acele etmeden öğrenirsin.", 42);
        Put(timed, 960 - 140, 870);
    }

    // ---------- yardım ----------

    private void DrawHelp()
    {
        Put(new Border { Width = 1920, Height = 1080, Background = Theme.B("#dc000000") }, 0, 0);
        var root = new StackPanel { Margin = new Thickness(50, 34, 50, 30) };
        var k = HanUi.Label("Han Ligi", 13);
        root.Children.Add(k);
        root.Children.Add(HanUi.Display("Nasıl oynanır", 44, TextAlignment.Left));
        var rule = HanUi.Rule(1260);
        rule.Margin = new Thickness(0, 10, 0, 18);
        root.Children.Add(rule);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cols = new[] { new StackPanel(), new StackPanel() };
        Grid.SetColumn(cols[1], 2);
        grid.Children.Add(cols[0]);
        grid.Children.Add(cols[1]);
        var n = 0;
        void P(string title, string text)
        {
            var col = cols[n < 4 ? 0 : 1];
            n++;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, col.Children.Count == 0 ? 0 : 18, 0, 6) };
            var badge = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            badge.Children.Add(new Path { Data = HanUi.ChamferGeometry(30, 30, 7), Fill = HanUi.Br(HanUi.Gold, 0x30), Stroke = HanUi.GoldEdge, StrokeThickness = 1 });
            var num = HanUi.Number(n.ToString(), 16);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            badge.Children.Add(num);
            head.Children.Add(badge);
            var t = HanUi.Title(title, 21, HanUi.GoldText);
            t.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(t);
            col.Children.Add(head);
            col.Children.Add(HanUi.Body(text, 15.5));
        }
        P("Kart al, masaya diz", "Alttaki tezgâhtan kart al. Kartı depodan masadaki satırına sürükle ya da karta tıkla, sonra parlayan yere tıkla; boş yere tıklamak seçimi bırakır. Aynı karttan üç tane birleşip yıldız atlar. Satmak için seçili kartla SAT'a bas ya da kartı tezgâha bırak.");
        P("Kartı okumak", "Sol üstte güç. Alttaki plakada yeteneğin adı (Casus, Bağ, Şifacı...) ve sınıfları (Asker, Canavar...) yazar; sınıflar soldaki sinerjileri açar. Karta gelince tam açıklaması anında açılır.");
        P("Altın", "Her round +4 altın. Elinde tuttuğun her 10 altın +1 faiz (en çok +2). Seri: art arda kaybedersen 2'de +1, 3'te +2, 4'te +3, 5'te +4 altın; art arda kazanırsan 3'te +1, 5'te +2. Erken round'larda hasar hafiftir, geçte ağırlaşır.");
        P("Savaş: üç satır", "Rakibin satırları sis altındadır. Satırlar sırayla açılır; her satırda toplamı büyük olan kazanır. Üç satırdan çoğunu alan savaşı kazanır, kaybeden can kaybeder.");
        P("İşaretler", "Her satır açılmadan önce bir işaret atabilirsin; rakip de aynı anda seçer. Aard: rakip satır −%10. Igni (2 Odak): rakibin en güçlü kartı −%50. Quen: rakibin Igni'sini engeller, engellerse Odağın geri gelir. Yrden (2 Odak): rakibin Aard'ını keser ve o satırda pusudan inmesine izin vermez (kart pusuda kalır, sonra inebilir; son satırda hiç inmez). Pusu hiçbir satırda kendiliğinden inmez, son satırda da seçmen gerekir; 6 Haydut'ta pusu Yrden'den etkilenmez. Döngü: Aard Quen'i, Igni Yrden'i, Quen Igni'yi, Yrden Aard'ı yener. Savaşa 2 Odakla başlarsın, yani her satırda işaret atamazsın; hangisini boş geçeceğin kararın. Kaybettiğin satır +1 Odak verir.");
        P("Puan nasıl hesaplanır", "İki adım. Kart: güç → hava (1'e iner) → Bağ, Büyüme, Moral, sınıf → kart yüzdeleri (Igni, bombalar, İntikam...); kartın üstündeki sayı budur. Satır: kart toplamı × (1 + satır yüzdeleri). Boynuz, Aard, Gök Gürültüsü, Yennefer, Cüce toplanır; sırası fark etmez (Boynuz +%50 ve Aard −%10 = +%40). Satır puanının altında döküm yazar.");
        P("Kazanmak", "Sekiz oyuncu 100 canla başlar; son ayakta kalan kazanır. 1., 2. ve her 5. round canavarlara karşıdır.");
        root.Children.Add(grid);
        var close = Btn("Anladım", "han-help-close", () => { _help = false; Render(); }, true, width: 220, height: 52);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 26, 0, 0);
        root.Children.Add(close);
        Put(Panel(1360, 800, root, 0xf0, null, 18), 280, 140);
    }
}
