using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Oyun ekranı: Witcher 3'teki Gwent masasının düzenini (1920×1080) izler. Tahta arka planı, kartlar, rozetler ve
/// simgeler oyundan çıkarılmış görsellerdir (yoksa yer tutucuya düşer). Durum her değişiklikte baştan çizilir;
/// kural mantığı motordadır, burada yalnız seçim, tıklama ve görünüm vardır.
/// </summary>
public sealed class GwintView : Viewbox
{
    private const double W = 1920, H = 1080;
    // Koltuk: yerelde ben hep 0; online'da host 0, konuk 1. Ekranda "ben" her zaman alttadır.
    private int Me => _s.MySeat;
    private int Opp => _s.OppSeat;

    /// <summary>Oyuncu numarasının ekrandaki tarafı: 0 = ben (alt), 1 = rakip (üst).</summary>
    private int Side(int player) => player == Me ? 0 : 1;

    // Satır yerleşimi (orijinal ekran görüntüsünden): üstten alta rakip Kuşatma, Menzilli, Yakın; sonra Yakın, Menzilli, Kuşatma
    private static readonly double[] RowTop = { 14, 148, 283, 432, 566, 703 };
    private const double RowH = 124, CardW = 90, CardH = 121, CardsX = 715, CardsRight = 1512, SlotX = 572;

    private readonly GameSession _s;
    private readonly Action _toMenu;
    private readonly Action<MatchConfig> _rematch;
    private readonly bool _animate;
    private readonly bool _effects;
    private Game G => _s.Game;

    private readonly Grid _root = new() { Width = W, Height = H, ClipToBounds = true };
    private readonly Canvas _board = new() { Width = W, Height = H };
    private readonly Grid _bannerHost = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly Grid _overlay = new() { Width = W, Height = H, Visibility = Visibility.Collapsed };

    private CardInstance? _selected;
    private bool _aiRunning;
    private string _hint = "";
    private bool _showLog;
    private CardDef? _previewDef;
    private int? _previewStrength;

    private HashSet<int> _prevBoard = new();
    private Dictionary<string, int> _prevTotals = new();
    private Dictionary<string, int> _curTotals = new();
    private bool _firstDraw = true;
    private int _pauseAfterMs;
    private Window? _keyWindow;
    private bool _opening;        // maç başı: yazı-tura + kart dağıtımı sürüyor (kart değiştirme penceresi bundan sonra gelir)
    private bool _dealStarted;
    private Canvas? _coinLayer;
    private int _dealDelay;
    private bool _gameOverShown;
    private string? _confirm;                 // online: "resign" | "leave" onay penceresi
    private readonly Border _onlineBar = new()
    {
        Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(22, 10, 22, 10), CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Color.FromArgb(0xee, 0x14, 0x0e, 0x08)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0xa2, 0x4a)), BorderThickness = new Thickness(1.5)
    };
    private DateTime? _oppGoneSince;
    private System.Windows.Threading.DispatcherTimer? _barTimer;
    private TextBlock? _barText;
    private Button? _claimBtn;
    private static readonly TimeSpan ClaimAfter = TimeSpan.FromMinutes(2);
    private SettingsView? _settingsView;   // açıkken aynı örnek korunur (kaydırıcı sürüklenirken yeniden çizim bozmasın)
    private bool _settingsOpen;
    private bool _cinema;                              // global etkili bir kart ekran ortasında gösteriliyor
    private IReadOnlyList<GameEvent>? _forcedEvs;      // sinematikten sonra işlenecek olaylar
    private DateTime _animUntil;

    // Animasyon durumu: önceki çizimdeki konumlar (kartlar nereden gelip nereye gidiyor)
    private sealed record Snap(CardDef Def, Point Pos, int Side);
    private readonly Canvas _fx = new() { Width = W, Height = H, IsHitTestVisible = false };
    private Dictionary<int, Point> _prevHandPos = new(), _curHandPos = new();
    private Dictionary<int, Snap> _prevBoardInfo = new(), _curBoardInfo = new();
    private Dictionary<int, int> _prevGraveInfo = new();
    private readonly int[] _prevGems = { 2, 2 };
    private readonly bool[] _prevWeather = new bool[3];
    private readonly FrameworkElement?[] _wxEl = new FrameworkElement?[6]; // sıra başına canlı hava örtüsü (yeniden çizimde korunur)
    private readonly string?[] _wxType = new string?[6];
    private readonly GwintCard?[] _leaderEl = new GwintCard?[2];
    private IReadOnlyList<GameEvent> _evs = Array.Empty<GameEvent>();
    private static readonly Point[] DeckPos = { new(1735, 830), new(1735, 70) }; // [Me, Opp]
    private static readonly Point[] GravePos = { new(1545, 828), new(1545, 70) };

    // Pencereler: lider yakınlaştırma ve ıskarta görüntüleyici
    private (int Player, CardDef Def)? _zoom;
    private int? _graveView;

    private readonly HashSet<int> _mulliganSel = new();
    private PendingChoice? _lastPending;
    private readonly List<int> _choiceSel = new();
    private RowKind? _choiceRow;
    private ScrollViewer? _logScroll;

    public GwintView(GameSession session, Action toMenu, Action<MatchConfig> rematch, bool animate = true, bool? effects = null)
    {
        _s = session;
        _toMenu = toMenu;
        _rematch = rematch;
        _animate = animate;
        _effects = effects ?? animate;
        Stretch = Stretch.Uniform;
        Focusable = true;

        // Arka plan: oyundaki masa (yoksa kahverengi düz zemin + çizilmiş paneller)
        var bg = Assets.Ui("board");
        if (bg != null)
            _root.Children.Add(new Image { Source = bg, Stretch = Stretch.Fill, Width = W, Height = H });
        else
            _root.Children.Add(FallbackBoard());
        _root.Children.Add(_board);
        // Kenarlara doğru koyulaşan hafif vinyet: oyun alanı da diğer ekranlar gibi karanlık Witcher havasında
        var vignette = new Border
        {
            Width = W, Height = H, IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.78, RadiusY = 0.85,
                GradientStops = { new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0x99, 0, 0, 0), 1) }
            }
        };
        Panel.SetZIndex(vignette, 40);
        _root.Children.Add(vignette);
        Panel.SetZIndex(_fx, 50);
        _root.Children.Add(_fx);
        Panel.SetZIndex(_bannerHost, 90);
        _root.Children.Add(_bannerHost);
        Panel.SetZIndex(_onlineBar, 110);
        _root.Children.Add(_onlineBar);
        Panel.SetZIndex(_overlay, 100);
        _root.Children.Add(_overlay);
        Child = _root;

        // Tuş olayları odaktaki öğeye gider; pencere düzeyinde dinle ki Boşluk/Esc/Enter her zaman çalışsın.
        Loaded += (_, _) => { _keyWindow = Window.GetWindow(this); if (_keyWindow != null) _keyWindow.PreviewKeyDown += OnKey; };
        Unloaded += (_, _) => { if (_keyWindow != null) _keyWindow.PreviewKeyDown -= OnKey; _keyWindow = null; };

        // Online: sunucudan eylem gelince (herhangi bir iş parçacığında olabilir) arayüz iş parçacığında işle
        Action onRemote = () =>
        {
            if (Dispatcher.CheckAccess()) KickAi();
            else Dispatcher.BeginInvoke(new Action(KickAi));
        };
        _s.RemoteArrived += onRemote;
        Unloaded += (_, _) => _s.RemoteArrived -= onRemote;

        _s.AiAutoMulligan();
        _opening = _animate && _effects && G.Phase == Phase.Mulligan;
        if (_opening)
        {
            // Scoia'tael'de başlangıcı seçen taraf var: yazı-tura yapılmaz
            _dealDelay = G.FirstIsChosen ? 500 : 3300;   // Scoia'tael ya da Kışla
            PreviewMouseLeftButtonDown += (_, e) => { if (_opening) { e.Handled = true; FinishOpening(); } };
        }
        Refresh();
        if (_opening) StartOpening();
        KickAi();
    }

    // ---------- Yardımcılar ----------

    private bool MyTurn => G.Phase == Phase.Playing && G.Pending == null && G.Current == Me
                           && !G.Players[Me].Passed && !_aiRunning && !_s.IsOver && !_cinema && !_s.AwaitingEcho && !_s.IsSpectator;

    private CardInstance? FindCard(int uid)
    {
        if (G.Pending?.LimboCard is { } l && l.Uid == uid) return l;
        foreach (var p in G.Players)
            foreach (var c in p.Hand.Concat(p.Deck).Concat(p.Grave).Concat(p.Rows.SelectMany(r => r.Cards)))
                if (c.Uid == uid) return c;
        return null;
    }

    private static T Put<T>(Canvas c, T el, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        c.Children.Add(el);
        return el;
    }

    private static Image Pic(string name, double w, double h, double opacity = 1, Stretch st = Stretch.Uniform)
    {
        var im = new Image { Source = Assets.Ui(name), Width = w, Height = h, Stretch = st, Opacity = opacity, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
        return im;
    }

    private static TextBlock Txt(string text, double size, Brush? fg = null, bool bold = false, TextAlignment align = TextAlignment.Left, double? width = null, bool italic = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Theme.Serif,
            FontSize = size,
            Foreground = fg ?? Theme.Text,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = align,
            IsHitTestVisible = false
        };
        if (width != null) { t.Width = width.Value; t.TextWrapping = TextWrapping.Wrap; }
        return t;
    }

    private static TextBlock Outlined(string text, double size, Brush fg, double width)
    {
        var t = Txt(text, size, fg, true, TextAlignment.Center, width);
        t.TextWrapping = TextWrapping.NoWrap;
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.95 };
        return t;
    }

    private GwintCard MakeCard(CardInstance c, double width, bool onBoard, bool selected = false, bool highlight = false,
        bool dimmed = false, double? height = null)
    {
        var current = c.Def.IsUnit ? (onBoard ? G.CardScore(c) : c.Def.Strength + (c.Rune == RuneIds.Svarog && !c.Def.Hero ? 1 : 0)) : (int?)null;
        var gc = new GwintCard(c.Def, width, current, c, selected, highlight, dimmed, height);
        gc.MouseEnter += (_, _) => SetPreview(c.Def, current);
        return gc;
    }

    private void SetPreview(CardDef d, int? strength)
    {
        if (_previewDef == d && _previewStrength == strength) return;
        _previewDef = d;
        _previewStrength = strength;
        if (!_showLog) Refresh(rebuildOnlyPreview: true);
    }

    // ---------- Online: çıkış/teslim onayı, rakip durumu ----------

    private void OnMenuClicked()
    {
        if ((_s.IsRemote || _s.LeaveWarning != null) && !_s.IsOver && !_s.IsSpectator) { _confirm = "leave"; Refresh(); }
        else _toMenu();
    }

    private UIElement ConfirmPanel()
    {
        var resign = _confirm == "resign";
        var sp = new StackPanel { MinWidth = 640 };
        sp.Children.Add(Title(resign ? "Teslim olmak istiyor musun?" : "Maçtan ayrılmak istiyor musun?"));
        var t = Txt(resign ? "Maçı kaybedersin; rakip galip sayılır." : _s.LeaveWarning ?? "Maç sürerken ayrılmak teslim olmak sayılır; rakip galip sayılır.", 22, Theme.TextDim, false, TextAlignment.Center, 640);
        t.Margin = new Thickness(0, 12, 0, 26);
        sp.Children.Add(t);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var yes = Ui.Button(resign ? "Evet, teslim ol" : "Evet, ayrıl", () =>
        {
            var kind = _confirm;
            _confirm = null;
            if (kind == "resign") { _s.Link!.Resign(); Refresh(); }
            else _toMenu();
        }, primary: true);
        var no = Ui.Button("Vazgeç", () => { _confirm = null; Refresh(); });
        yes.FontSize = 22; no.FontSize = 22;
        yes.Padding = new Thickness(36, 10, 36, 10); no.Padding = new Thickness(36, 10, 36, 10);
        no.Margin = new Thickness(16, 0, 0, 0);
        yes.Tag = "confirm-yes";
        row.Children.Add(yes);
        row.Children.Add(no);
        sp.Children.Add(row);
        return sp;
    }

    /// <summary>Sunucudan gelen bağlantı/rakip bildirimlerini gösterir (opponent_disconnected, reconnecting, rematch_requested ...).</summary>
    public void OnlineNotice(string code, string text)
    {
        switch (code)
        {
            case "opponent_disconnected":
                _oppGoneSince = DateTime.UtcNow;
                ShowBar(text + " Dönmesi bekleniyor…", claim: true);
                break;
            case "opponent_returned":
            case "reconnected":
                _oppGoneSince = null;
                HideBar();
                _s.Log.Add(text);
                break;
            case "reconnecting":
                ShowBar(text, claim: false);
                break;
            case "connection_lost":
            case "room_closed":
                ShowBar(text + " Menüye dönebilirsin.", claim: false, menu: true);
                break;
            case "rematch_requested":
                _hint = text + " Sen de “Rövanş iste”ye bas.";
                _s.Log.Add(text);
                Refresh();
                break;
            default:
                _s.Log.Add(text);
                break;
        }
    }

    public GameSession Session => _s;

    /// <summary>Host: yarım kalan maçta konuğa verilecek davet kodu (rakip dönmeyince çubukta "Davet kodunu kopyala" çıkar).</summary>
    public string? InviteText { get; set; }

    private void ShowBar(string text, bool claim, bool menu = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _barText = Txt(text, 20, Theme.B("#f0d890"), true);
        _barText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_barText);
        _claimBtn = null;
        if (claim)
        {
            _claimBtn = Ui.Button("Galibiyeti al", () => _s.Link?.ClaimWin());
            _claimBtn.IsEnabled = false;
            _claimBtn.Margin = new Thickness(18, 0, 0, 0);
            _claimBtn.FontSize = 16;
            _claimBtn.Tag = "claim-win";
            row.Children.Add(_claimBtn);
        }
        if (menu)
        {
            var b = Ui.Button("Menüye dön", _toMenu, primary: true);
            b.Margin = new Thickness(18, 0, 0, 0);
            b.FontSize = 16;
            row.Children.Add(b);
        }
        if (claim && InviteText != null)
        {
            var copy = Ui.Button("Davet kodunu kopyala", () => { try { Clipboard.SetText(InviteText); } catch { /* pano kilitli olabilir */ } });
            copy.Margin = new Thickness(12, 0, 0, 0);
            copy.FontSize = 16;
            copy.Tag = "copy-invite";
            row.Children.Add(copy);
        }
        _onlineBar.Child = row;
        _onlineBar.Visibility = Visibility.Visible;
        _barTimer?.Stop();
        if (claim)
        {
            _barTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _barTimer.Tick += (_, _) => UpdateBar();
            _barTimer.Start();
            UpdateBar();
        }
    }

    private void UpdateBar()
    {
        if (_oppGoneSince == null || _claimBtn == null) { _barTimer?.Stop(); return; }
        var left = ClaimAfter - (DateTime.UtcNow - _oppGoneSince.Value);
        _claimBtn.IsEnabled = left <= TimeSpan.Zero;
        if (_barText != null && left > TimeSpan.Zero)
            _barText.Text = $"Rakibin bağlantısı koptu. Dönmesi bekleniyor… ({(int)left.TotalMinutes}:{left.Seconds:00} sonra galibiyeti alabilirsin)";
        else if (_barText != null)
            _barText.Text = "Rakip hâlâ dönmedi. İstersen galibiyeti alabilirsin.";
    }

    private void HideBar()
    {
        _barTimer?.Stop();
        _onlineBar.Visibility = Visibility.Collapsed;
    }

    private void OpenSettings()
    {
        _settingsOpen = true;
        _settingsView = new SettingsView(CloseSettings, embedded: true);
        Refresh();
    }

    private void CloseSettings()
    {
        _settingsOpen = false;
        _settingsView = null;
        Settings.Save();
        Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _confirm != null) { _confirm = null; Refresh(); e.Handled = true; return; }
        if (e.Key == Key.Escape && _settingsOpen) { CloseSettings(); e.Handled = true; return; }
        if (e.Key == Key.Escape && (_zoom != null || _graveView != null)) { _zoom = null; _graveView = null; Refresh(); e.Handled = true; }
        else if (e.Key == Key.Escape && _selected != null) { _selected = null; _hint = ""; Refresh(); e.Handled = true; }
        else if (e.Key == Key.Escape && !_cinema && !_opening && !_s.IsOver) { OpenSettings(); e.Handled = true; }
        else if (e.Key == Key.Space && _zoom == null && _graveView == null && G.Pending == null && G.Phase == Phase.Playing) { e.Handled = true; if (MyTurn) OnPass(); }
        else if (e.Key == Key.Enter && _selected != null && MyTurn && NeedsNoTarget(_selected.Def)) { e.Handled = true; DoPlay(_selected); }
    }

    // ---------- Çizim ----------

    private Canvas? _previewLayer;

    public void Refresh(bool rebuildOnlyPreview = false)
    {
        if (rebuildOnlyPreview)
        {
            if (_previewLayer != null) { _previewLayer.Children.Clear(); BuildRightPanel(_previewLayer); }
            return;
        }

        if (_cinema) return; // sinematik bitince son durum çizilir

        if (_selected != null && !G.Players[Me].Hand.Contains(_selected)) _selected = null;
        if (!MyTurn) _selected = null;

        var forced = _forcedEvs != null;
        var evs = _forcedEvs ?? _s.TakeEvents();
        _forcedEvs = null;
        if (!forced && _animate && _effects && !_firstDraw && FindShowcase(evs) is { } show)
        {
            StartCinema(show.Player, show.Def, show.Caption, evs);
            return;
        }
        _evs = evs;
        _board.Children.Clear();
        _curTotals = new Dictionary<string, int>();
        _curHandPos = new Dictionary<int, Point>();
        _curBoardInfo = new Dictionary<int, Snap>();

        for (var i = 0; i < 6; i++) BuildRow(i);
        BuildSide(Opp);
        BuildSide(Me);
        BuildLeaderSlots();
        BuildPiles();
        BuildWeatherBox();
        BuildHand();
        _previewLayer = new Canvas { Width = 360, Height = 760 };
        Put(_board, _previewLayer, 1540, 232);
        BuildRightPanel(_previewLayer);
        BuildControls();
        BuildOverlay();

        if (_effects && !_firstDraw) { SpawnGhosts(evs); HandleEvents(evs); }
        if (_animate && _effects && evs.Count > 0) _animUntil = DateTime.UtcNow.AddMilliseconds(evs.Any(e => e.Kind == "kill") ? 1800 : 700);
        _prevBoard = G.Players.SelectMany(p => p.Rows.SelectMany(r => r.Cards)).Select(c => c.Uid).ToHashSet();
        _prevTotals = _curTotals;
        _prevHandPos = _curHandPos;
        _prevBoardInfo = _curBoardInfo;
        _prevGraveInfo = new Dictionary<int, int>();
        for (var pl = 0; pl < 2; pl++)
        {
            foreach (var gc in G.Players[pl].Grave) _prevGraveInfo[gc.Uid] = pl;
            _prevGems[pl] = G.Players[pl].Gems;
        }
        for (var r = 0; r < 3; r++) _prevWeather[r] = G.WeatherActive((RowKind)r);
        _firstDraw = false;
    }

    private UIElement FallbackBoard()
    {
        var c = new Canvas { Width = W, Height = H, Background = new LinearGradientBrush(Color.FromRgb(0x3a, 0x26, 0x16), Color.FromRgb(0x1a, 0x10, 0x0a), 0) };
        for (var i = 0; i < 6; i++)
        {
            Put(c, new Rectangle { Width = 130, Height = RowH, Fill = Theme.B("#33000000"), RadiusX = 4, RadiusY = 4 }, SlotX, RowTop[i]);
            Put(c, new Rectangle { Width = CardsRight - 707, Height = RowH, Fill = Theme.B("#22000000"), RadiusX = 4, RadiusY = 4 }, 707, RowTop[i]);
        }
        foreach (var (x, y) in new[] { (133.0, 75.0), (133.0, 828.0), (1545.0, 70.0), (1545.0, 828.0) })
            Put(c, new Rectangle { Width = 112, Height = 146, Fill = Theme.B("#33000000"), RadiusX = 4, RadiusY = 4 }, x, y);
        Put(c, new Rectangle { Width = 302, Height = 170, Fill = Theme.B("#33000000"), RadiusX = 4, RadiusY = 4 }, 130, 433);
        Put(c, new Rectangle { Width = 945, Height = 134, Fill = Theme.B("#22000000"), RadiusX = 6, RadiusY = 6 }, 574, 838);
        return c;
    }

    // ---------- Sıralar ----------

    private bool RowHighlighted(int side, RowKind rk)
    {
        if (_selected == null || !MyTurn) return false;
        var d = _selected.Def;
        if (d.IsUnit)
        {
            if (d.IsAgile) return side == Me && rk is RowKind.Close or RowKind.Ranged && !G.RowFull(Me, rk);
            var target = d.Has("spy") ? Opp : Me;
            return side == target && rk == d.FixedRow && !G.RowFull(target, rk);
        }
        if (d.Id is Ids.Horn or Ids.Mardroeme) return side == Me && G.Players[Me].Row(rk).Special == null;
        return false;
    }

    private void BuildRow(int index)
    {
        var side = index < 3 ? Opp : Me;
        var rk = index switch { 0 => RowKind.Siege, 1 => RowKind.Ranged, 2 => RowKind.Close, 3 => RowKind.Close, 4 => RowKind.Ranged, _ => RowKind.Siege };
        var row = G.Players[side].Row(rk);
        var top = RowTop[index];
        var highlighted = RowHighlighted(side, rk);
        BuildTerrain(side, rk, top);

        // Hava: canlı örtü, çizimler arasında aynı örnek kalır (animasyon sıfırlanmaz)
        if (G.WeatherActive(rk))
        {
            var storm = rk != RowKind.Close && G.WeatherCards.Any(w => w.Def.Has("storm"));
            var type = storm ? "storm" : rk switch { RowKind.Close => "frost", RowKind.Ranged => "fog", _ => "rain" };
            if (_wxEl[index] == null || _wxType[index] != type)
            {
                var intro = _effects && !_firstDraw && (!_prevWeather[(int)rk] || _wxType[index] != type);
                _wxEl[index] = WeatherFx.Make(type, CardsRight - 707, RowH, intro);
                _wxType[index] = type;
            }
            Put(_board, _wxEl[index]!, 707, top);
            _wxEl[index]!.Opacity = G.CommanderShields(side, rk) ? 0.3 : 1;   // komutan bu satırı havadan koruyor
        }
        else if (_wxEl[index] is { } old)
        {
            _wxEl[index] = null;
            _wxType[index] = null;
            if (_effects && !_firstDraw)
            {
                Put(_fx, old, 707, top);
                Fx.FadeOut(old, 800, 0, () => _fx.Children.Remove(old));
            }
        }

        // Sıra puanı: oyundaki gibi yuvarlak rozet (rakipte mavi tonlu)
        var total = G.RowTotal(side, rk);
        var circle = new Grid { Width = 54, Height = 54, IsHitTestVisible = false };
        var cimg = Pic("badge_big", 54, 54);
        if (cimg.Source != null) circle.Children.Add(cimg);
        else circle.Children.Add(new Ellipse { Fill = Theme.B("#e8dcc4"), Stroke = Theme.B("#8a6a30"), StrokeThickness = 2 });
        if (side == Opp) circle.Children.Add(new Ellipse { Fill = Theme.B("#5a9ad8"), Opacity = 0.42, Margin = new Thickness(4) });
        circle.Children.Add(Txt(total.ToString(), total > 99 ? 20 : 26, Theme.B("#2a1c0e"), true, TextAlignment.Center));
        ((TextBlock)circle.Children[^1]).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)circle.Children[^1]).VerticalAlignment = VerticalAlignment.Center;
        PulseIfChanged(circle, $"r{side}:{(int)rk}", total);
        Put(_board, circle, 508, top + (RowH - 54) / 2);

        // Boynuz / Mardroeme yuvası
        if (row.Special != null)
        {
            var sc = MakeCard(row.Special, CardW, false);
            Put(_board, sc, SlotX + (130 - CardW) / 2, top + 1.5);
        }

        // Kartlar
        var n = row.Cards.Count;
        var step = n <= 1 ? 0 : Math.Min(CardW + 3, (CardsRight - CardsX - CardW) / (double)(n - 1));
        for (var i = 0; i < n; i++)
        {
            var c = row.Cards[i];
            var isDecoyTarget = _selected?.Def.Id == Ids.Decoy && side == Me && MyTurn && c.Def.IsUnit && !c.Def.Hero;
            var gc = MakeCard(c, CardW, true, highlight: isDecoyTarget, height: CardH);
            if (isDecoyTarget) gc.Tag = "decoy-target";
            var pos = new Point(CardsX + i * step, top + 1.5);
            _curBoardInfo[c.Uid] = new Snap(c.Def, pos, side);
            var arriving = _effects && !_firstDraw && !_prevBoard.Contains(c.Uid);
            var shadow = CardShadow(CardW, CardH);
            if (arriving) shadow.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { BeginTime = TimeSpan.FromMilliseconds(500) });
            Put(_board, shadow, pos.X + 3, pos.Y + 6);
            if (arriving) AnimateArrival(gc, c, side, pos);
            else if (_effects && !_firstDraw && _prevBoardInfo.TryGetValue(c.Uid, out var was) && (Math.Abs(was.Pos.X - pos.X) > 0.5 || Math.Abs(was.Pos.Y - pos.Y) > 0.5))
            {
                // Sıra yeniden dizildi (kart yandı / geri alındı): kart eski yerinde kalır, yanma bitince boşluğu kapatmak için kayar.
                // Böylece ateş, yerine kayan başka bir kartın üstünde yanıyormuş gibi görünmez.
                var hold = _evs.Any(e => e.Kind == "kill") ? 1050 : 0;
                SlideFrom(gc, was.Pos.X - pos.X, was.Pos.Y - pos.Y, hold);
                SlideFrom(shadow, was.Pos.X - pos.X, was.Pos.Y - pos.Y, hold);
            }
            var captured = c;
            gc.MouseLeftButtonUp += (_, e) => { if (isDecoyTarget) { e.Handled = true; if (_selected is { } sel && MyTurn) DoPlay(sel, null, captured.Uid); } };
            Put(_board, gc, pos.X, pos.Y);
        }

        // Hedef vurgusu
        if (highlighted)
        {
            var hl = new Border
            {
                Width = CardsRight - SlotX + 6,
                Height = RowH,
                BorderBrush = Theme.Gold,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(4),
                Background = Theme.B("#2affd36a"),
                Cursor = Cursors.Hand,
                Tag = "row-hl",
                Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 }
            };
            var s2 = side; var r2 = rk;
            hl.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnRowClicked(s2, r2); };
            Panel.SetZIndex(hl, 5);
            Put(_board, hl, SlotX - 3, top);
        }
    }

    // ---------- Kaer Morhen: arazi ve komutan ----------

    private static string TerrainGlyph(Terrain t) => t switch
    {
        Terrain.Hill => "▲", Terrain.Bridge => "≡", Terrain.Forest => "♣", Terrain.Swamp => "≈", Terrain.Ruins => "⌂", _ => ""
    };

    private static string TerrainColor(Terrain t) => t switch
    {
        Terrain.Hill => "#d0a858", Terrain.Bridge => "#a8a8b4", Terrain.Forest => "#5cae4c", Terrain.Swamp => "#6f9488", Terrain.Ruins => "#a890c0", _ => "#c9c0aa"
    };

    /// <summary>Komutan nişanı satır puanı rozetinin sağ alt köşesine iliştirilir (oyuncu toplamının defnesiyle çakışmaz).</summary>
    private const double CommanderX = 540, CommanderDy = 74;

    /// <summary>Satırın arazisi (renk tonu + rozet) ve komutan figürü / taşıma hedefi.</summary>
    private void BuildTerrain(int side, RowKind rk, double top)
    {
        var t = G.TerrainAt(side, rk);
        if (t != Terrain.Plain)
        {
            var col = (Color)ColorConverter.ConvertFromString(TerrainColor(t));
            Put(_board, new Rectangle
            {
                Width = CardsRight - 707, Height = RowH, IsHitTestVisible = false, RadiusX = 4, RadiusY = 4,
                Fill = new LinearGradientBrush(Color.FromArgb(0x55, col.R, col.G, col.B), Color.FromArgb(0x10, col.R, col.G, col.B), 0)
            }, 707, top);
            Put(_board, new Rectangle { Width = 4, Height = RowH - 8, Fill = new SolidColorBrush(col), Opacity = 0.8, IsHitTestVisible = false }, 707, top + 4);
            var full = t == Terrain.Bridge ? $" {G.Players[side].Row(rk).Cards.Count(c => c.Def.IsUnit)}/{Game.BridgeCapacity}" : "";
            var chip = new Border
            {
                Width = 126, Height = 24, CornerRadius = new CornerRadius(3), Background = Theme.B("#d8140e08"), BorderBrush = new SolidColorBrush(col),
                BorderThickness = new Thickness(1), Tag = "terrain",
                ToolTip = $"{GwentAI.Battlefields.Name(t)}: {GwentAI.Battlefields.Rule(t)}",
                Child = Txt($"{TerrainGlyph(t)} {GwentAI.Battlefields.Name(t)}{full}", 15, new SolidColorBrush(col), true, TextAlignment.Center)
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            Panel.SetZIndex(chip, 4);
            Put(_board, chip, SlotX + 2, top + RowH - 26);
        }

        if (!G.Options.Commanders) return;
        var ps = G.Players[side];
        if (ps.Commander == rk)
        {
            var shielding = G.WeatherActive(rk);
            var token = new Grid
            {
                Width = 32, Height = 32, Tag = "commander", Cursor = Cursors.Arrow,
                ToolTip = "Komutan: bu satırı havadan korur." + (G.HasDiscipline(side) ? $" Disiplin ({Texts.FactionShort(ps.Faction)}): bu satırdaki birimler +{Game.CommanderDiscipline}." : "")
            };
            token.Children.Add(new Ellipse
            {
                Fill = Theme.B(side == Me ? "#2a1c0e" : "#16202e"), Stroke = Theme.B(side == Me ? "#e8c970" : "#7ab0e8"), StrokeThickness = 2.5,
                Effect = shielding ? new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 } : null
            });
            var glyph = Txt("♜", 18, Theme.B(side == Me ? "#e8c970" : "#9ac4f0"), true, TextAlignment.Center);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            token.Children.Add(glyph);
            Panel.SetZIndex(token, 6);
            Put(_board, token, CommanderX, top + CommanderDy);
        }
        else if (side == Me && MyTurn && _selected == null && !ps.CommanderMoved)
        {
            // Taşıma hedefi: soluk figür, üzerine gelince belirginleşir
            var target = new Grid
            {
                Width = 32, Height = 32, Tag = "commander-move", Cursor = Cursors.Hand, Opacity = 0.35, Background = Brushes.Transparent,
                ToolTip = $"Komutanı {Texts.Row(rk)} satırına taşı (bu hamlen sayılır; raundda bir kez)."
            };
            target.Children.Add(new Ellipse { Stroke = Theme.B("#e8c970"), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } });
            var g2 = Txt("♜", 17, Theme.B("#e8c970"), false, TextAlignment.Center);
            g2.HorizontalAlignment = HorizontalAlignment.Center;
            g2.VerticalAlignment = VerticalAlignment.Center;
            target.Children.Add(g2);
            target.MouseEnter += (_, _) => target.Opacity = 1;
            target.MouseLeave += (_, _) => target.Opacity = 0.35;
            var row = rk;
            target.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnMoveCommander(row); };
            Panel.SetZIndex(target, 6);
            Put(_board, target, CommanderX, top + CommanderDy);
        }
    }

    private void OnMoveCommander(RowKind row)
    {
        if (!MyTurn) return;
        var r = _s.Apply(new MoveCommander(Me, row));
        if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
        AfterHumanAction();
    }

    // ---------- Oyuncu blokları ----------

    private void BuildSide(int p)
    {
        var ps = G.Players[p];
        var baseY = p == Opp ? 280.0 : 665.0;

        var band = Pic("name_band", 450, 134, 0.9, Stretch.Fill);
        if (band.Source != null) Put(_board, band, 0, baseY);
        else Put(_board, new Rectangle { Width = 450, Height = 134, Fill = Theme.B("#aa000000"), IsHitTestVisible = false }, 0, baseY);

        // Fraksiyon kalkanı
        var shield = Pic("shield_" + ps.Faction, 58, 58);
        if (shield.Source != null) { Panel.SetZIndex(shield, 10); Put(_board, shield, 108, baseY + 4); }

        // Avatar (profil portresi; rakip yapay zekâysa lider yüzü)
        var turn = G.Phase == Phase.Playing && G.Pending == null && G.Current == p && !ps.Passed;
        var avatar = Portraits.Medallion(p == Me ? _s.MyAvatar : _s.OppAvatar, 106, glow: turn);
        Put(_board, avatar, 125, baseY + 10);

        var shown = p == Me ? _s.MyName : _s.OppName;   // uzun adlar puan defnesine taşmasın: küçültülür
        Put(_board, Txt(shown, shown.Length > 11 ? 19 : shown.Length > 8 ? 23 : 26, Theme.B("#e8c970"), true, TextAlignment.Left, 145), 252, baseY + (shown.Length > 11 ? 10 : 6));
        Put(_board, Txt(Texts.FactionShort(ps.Faction), 18, Theme.B("#c9c0aa")), 252, baseY + 40);

        // El sayısı + mücevherler
        var cardsIcon = Pic("cards_icon", 24, 32);
        if (cardsIcon.Source != null) Put(_board, cardsIcon, 252, baseY + 78);
        Put(_board, Txt(ps.Hand.Count.ToString(), 24, Theme.B("#e8dcc4"), true), 282, baseY + 78);
        for (var i = 0; i < 2; i++)
        {
            var alive = i < ps.Gems;
            var gem = Pic("gem", 36, 36, alive ? 1 : 0.22);
            if (gem.Source != null) Put(_board, gem, 318 + i * 42, baseY + 76);
            else Put(_board, new Ellipse { Width = 30, Height = 30, Fill = alive ? Theme.B("#d94a4a") : Theme.B("#3a2e20"), Stroke = Theme.B("#ffb0a0"), StrokeThickness = 1.5 }, 321 + i * 42, baseY + 79);
        }

        // Toplam (öndeki oyuncuda defne). Orijinalden küçük tutuldu: sıra rozetine taşmasın.
        var total = G.PlayerTotal(p);
        var other = G.PlayerTotal(1 - p);
        var holder = new Grid { Width = 100, Height = 80, IsHitTestVisible = false };
        if (total > other)
        {
            var laurel = Pic("laurel", 100, 78);
            if (laurel.Source != null) holder.Children.Add(laurel);
        }
        var num = Outlined(total.ToString(), total > 99 ? 28 : 36, Brushes.White, 100);
        num.VerticalAlignment = VerticalAlignment.Center;
        PulseIfChanged(num, "p" + p, total);
        holder.Children.Add(num);
        Put(_board, holder, 396, baseY + 14);

        if (ps.Passed) Put(_board, Txt("Pas geçti", 22, Theme.B("#e8c970"), true, TextAlignment.Center, 120, italic: true), 386, baseY + 98);
    }

    // ---------- Liderler, yığınlar, hava ----------

    private void BuildLeaderSlots()
    {
        foreach (var p in new[] { Opp, Me })
        {
            var ps = G.Players[p];
            var y = p == Opp ? 75.0 : 828.0;
            var passive = IsPassiveLeader(ps.Leader);
            var usable = p == Me ? G.LeaderAvailable(p) && MyTurn : !ps.LeaderUsed && !G.LeaderCancelled(p) && !passive;
            var used = ps.LeaderUsed || G.LeaderCancelled(p);
            var card = new GwintCard(ps.Leader, 112, null, null, highlight: p == Me && usable && G.LeaderBlockedReason(Me) == null, dimmed: used && !passive, height: 146);
            card.MouseEnter += (_, _) => SetPreview(ps.Leader, null);
            var owner = p;
            card.MouseLeftButtonUp += (_, e) => { e.Handled = true; _zoom = (owner, ps.Leader); Refresh(); };
            if (p == Me) card.Tag = "leader-button";
            _leaderEl[p] = card;
            Put(_board, card, 133, y);
            var state = passive ? (G.LeaderCancelled(p) ? "iptal" : "otomatik") : ps.LeaderUsed ? "kullanıldı" : G.LeaderCancelled(p) ? "iptal" : p == Me && G.LeaderBlockedReason(Me) == null && MyTurn ? "hazır" : "";
            if (state != "")
                Put(_board, Outlined(state, 15, state == "hazır" ? Theme.B("#ffe08a") : Theme.B("#d0c5ae"), 112), 133, y + 120);
        }
    }

    private static bool IsPassiveLeader(CardDef l) => l.Id is "emhyr_var_emreis_the_white_flame" or "emhyr_var_emreis_invader_of_the_north"
        or "eredin_breacc_glas_the_treacherous" or "francesca_findabair_daisy_of_the_valley" or "king_bran";

    private void BuildPiles()
    {
        foreach (var p in new[] { Opp, Me })
        {
            var ps = G.Players[p];
            var y = p == Opp ? 70.0 : 828.0;
            // Mezarlık: en üstteki kart
            if (ps.Grave.Count > 0)
            {
                var top = ps.Grave[^1];
                var gc = new GwintCard(top.Def, 112, null, top, height: 146);
                gc.MouseEnter += (_, _) => SetPreview(top.Def, null);
                var gp = p;
                gc.Tag = "grave-pile";
                gc.MouseLeftButtonUp += (_, e) => { e.Handled = true; _graveView = gp; Refresh(); };
                Put(_board, gc, 1545, y);
                Put(_board, Outlined(ps.Grave.Count.ToString(), 22, Brushes.White, 40), 1621, y + 112);
            }
            // Deste
            var deck = Pic("deck_" + ps.Faction, 87, 160);
            if (ps.Deck.Count > 0)
            {
                if (deck.Source != null) Put(_board, deck, 1728, p == Opp ? 62.0 : 820.0);
                else Put(_board, new Rectangle { Width = 80, Height = 150, Fill = Theme.FactionColor(ps.Faction), RadiusX = 4, RadiusY = 4, Opacity = 0.8 }, 1728, p == Opp ? 62.0 : 820.0);
                Put(_board, Outlined(ps.Deck.Count.ToString(), 26, Brushes.White, 87), 1728, p == Opp ? 168.0 : 926.0);
            }
        }
    }

    private void BuildWeatherBox()
    {
        var weatherTarget = _selected?.Def.IsWeather == true && MyTurn;
        var n = G.WeatherCards.Count;
        for (var i = 0; i < n; i++)
        {
            var w = G.WeatherCards[i];
            var gc = MakeCard(w, 92, false, height: 123);
            var step = n <= 1 ? 0 : Math.Min(96, (302 - 92 - 8) / (double)(n - 1));
            Put(_board, gc, 139 + (n == 1 ? (302 - 92) / 2.0 - 9 : i * step), 457);
        }
        if (n == 0) Put(_board, Txt("Hava açık", 18, Theme.B("#7a6a52"), false, TextAlignment.Center, 302, italic: true), 130, 504);
        if (weatherTarget)
        {
            var hl = new Border
            {
                Width = 302, Height = 170, BorderBrush = Theme.Gold, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4),
                Background = Theme.B("#2affd36a"), Cursor = Cursors.Hand, Tag = "weather-hl",
                Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 }
            };
            hl.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_selected is { } sel && MyTurn) DoPlay(sel); };
            Panel.SetZIndex(hl, 5);
            Put(_board, hl, 130, 433);
        }
    }

    // ---------- El ----------

    private void BuildHand()
    {
        var frame = Pic("hand_frame", 946, 134, 0.95, Stretch.Fill);
        if (frame.Source != null) Put(_board, frame, 574, 838);
        if (_s.IsSpectator && !_s.ShowHands)
        {
            Put(_board, Txt("Eller gizli (turnuva kuralı): yalnız masa görünüyor", 20, Theme.B("#8a7a60"), false, TextAlignment.Center, 940, italic: true), 574, 890);
            return;
        }
        var hand = G.Players[Me].Hand;
        var n = hand.Count;
        const double cw = 92, ch = 123, left = 590, right = 1503;
        var step = n <= 1 ? 0 : Math.Min(cw + 4, (right - left - cw) / (n - 1));
        var startX = n <= 1 ? left + (right - left - cw) / 2 : left + Math.Max(0, (right - left - cw - step * (n - 1)) / 2);
        for (var i = 0; i < n; i++)
        {
            var c = hand[i];
            var sel = _selected == c;
            var gc = MakeCard(c, cw, false, selected: sel, dimmed: !MyTurn, height: ch);
            gc.Tag = "hand";
            if (_effects && MyTurn && !sel) Fx.HoverLift(gc);
            var captured = c;
            gc.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!MyTurn) return;
                _selected = _selected == captured ? null : captured;
                _hint = _selected == null ? "" : HintFor(_selected);
                Refresh();
            };
            var hpos = new Point(startX + i * step, 842);
            _curHandPos[c.Uid] = hpos;
            if (_effects && _opening && !_dealStarted)
            {
                var delay = _dealDelay + i * 110;
                Fx.FlyIn(gc, DeckPos[0], hpos, 480, delayMs: delay);
                Fx.After(delay, () => { if (IsLoaded && _opening) Sfx.Play("draw_card"); });
            }
            else if (_effects && !_firstDraw && !_prevHandPos.ContainsKey(c.Uid))
            {
                // Elde yeni kart: desteden (ya da Yem ile sahadan) uçarak gelir
                var from = _prevBoardInfo.TryGetValue(c.Uid, out var bs) ? bs.Pos : DeckPos[0];
                Fx.FlyIn(gc, from, hpos, 480, delayMs: i * 55 % 330);
            }
            Put(_board, CardShadow(cw, ch), hpos.X + 3, hpos.Y + 6 - (sel ? 14 : 0));
            Put(_board, gc, hpos.X, hpos.Y - (sel ? 14 : 0));
        }
        if (n == 0) Put(_board, Txt("Elinde kart kalmadı", 20, Theme.B("#8a7a60"), false, TextAlignment.Center, 940, italic: true), 574, 890);
    }

    private static string HintFor(CardInstance c)
    {
        var d = c.Def;
        if (d.IsUnit)
        {
            if (d.IsAgile) return "Yakın dövüş ya da Menzilli sırasını seç.";
            if (d.Has("spy")) return "Casus rakibin sahasına konur ve 2 kart çekersin. Vurgulu sıraya tıkla ya da “Oyna”.";
            return "Vurgulu sıraya tıkla ya da “Oyna”.";
        }
        if (d.Id is Ids.Horn) return "Güçlendireceğin sırayı seç.";
        if (d.Id is Ids.Mardroeme) return "Mardroeme'yi koyacağın sırayı seç.";
        if (d.Id == Ids.Decoy) return "Geri alacağın birimi kendi sahandan seç.";
        return "“Oyna”ya bas.";
    }

    private static bool NeedsNoTarget(CardDef d) => d.IsUnit ? !d.IsAgile : d.IsWeather || d.Id == Ids.Scorch;

    // ---------- Sağ panel: önizleme / günlük ----------

    private void BuildRightPanel(Canvas layer)
    {
        // Günlük/önizleme düğmesi
        var tab = new Border
        {
            Width = 120, Height = 30, CornerRadius = new CornerRadius(4), Background = Theme.B("#aa150e08"), BorderBrush = Theme.B("#6a5420"),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Tag = "toggle-log",
            Child = Txt(_showLog ? "Önizleme" : "Günlük", 15, Theme.B("#e8c970"), true, TextAlignment.Center)
        };
        ((TextBlock)tab.Child).VerticalAlignment = VerticalAlignment.Center;
        tab.MouseLeftButtonUp += (_, _) => { _showLog = !_showLog; Refresh(); };
        Put(layer, tab, 190, 0);
        // Etkin lanetler (Gaunter O'Dimm): elin altındaki şeritte, üzerine gelince açıklamaları
        if (_s.Config.Curses is { Count: > 0 } curses)
        {
            var cs = curses.Select(GwentAI.Curses.Get).ToList();
            var label = Txt("☠ Lanet: " + string.Join(" · ", cs.Select(c => c.Name)), 17, Theme.B("#d8b8f0"), true, TextAlignment.Left, 680);
            label.IsHitTestVisible = true;
            label.Tag = "curse-label";
            label.ToolTip = string.Join(Environment.NewLine, cs.Select(c => c.Name + ": " + GwentAI.Curses.TextFor(c.Id, _s.OnlineSetup != null)));
            Put(_board, label, 590, 1030);
        }
        // Savaş alanı (Kaer Morhen): adı ve üzerine gelince raund programı
        if (G.Options.Terrain is { } map)
        {
            var lines = new List<string> { map.Name };
            for (var r = 1; r <= Math.Max(1, map.Rounds.Count); r++)
            {
                var parts = Enum.GetValues<RowKind>().Select(rk => $"{Texts.Row(rk)} {GwentAI.Battlefields.Name(map.At(Me, rk, r))}");
                lines.Add($"Raund {r}{(r == map.Rounds.Count && r < 3 ? "+" : "")}: " + string.Join(" · ", parts));
            }
            if (G.Options.Commanders) lines.Add("Komutan (♜) bulunduğu satırı havadan korur; raundda bir kez taşınabilir, bu bir hamle sayılır.");
            var bl = Txt("⚑ " + map.Name + (map.Rounds.Count > 1 ? $" · raund {G.Round}" : ""), 17, Theme.B("#d8c890"), true, TextAlignment.Left, 600);
            bl.IsHitTestVisible = true;
            bl.Tag = "battlefield-label";
            bl.ToolTip = string.Join(Environment.NewLine, lines);
            Put(_board, bl, 590, 1000);
        }

        if (_showLog)
        {
            var logSp = new StackPanel();
            foreach (var line in _s.Log.TakeLast(60))
            {
                var isHeader = line.StartsWith("—") || line.StartsWith("Raund bitti") || line.StartsWith("Oyun");
                var t = Txt(line, 15, isHeader ? Theme.B("#e8c970") : line.StartsWith("Rakip") ? Theme.B("#e0b0a4") : Theme.B("#e8dcc4"), isHeader, TextAlignment.Left, 318);
                t.IsHitTestVisible = true;
                t.Margin = new Thickness(0, 1, 0, 3);
                logSp.Children.Add(t);
            }
            _logScroll = new ScrollViewer { Content = logSp, Width = 330, Height = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var box = new Border { Background = Theme.B("#cc120c07"), BorderBrush = Theme.B("#4a3a22"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Child = _logScroll };
            Put(layer, box, 0, 38);
            _logScroll.ScrollToEnd();
            return;
        }

        var d = _previewDef;
        if (d == null)
        {
            Put(layer, Txt("Bir kartın üzerine gel", 18, Theme.B("#8a7a60"), false, TextAlignment.Center, 330, italic: true), 0, 200);
            return;
        }
        var card = new GwintCard(d, 270, _previewStrength, null, height: 270 * GwintCard.Aspect) { Cursor = Cursors.Arrow, ToolTip = null };
        Put(layer, card, 30, 42);
        var y = 42 + 270 * GwintCard.Aspect + 12;
        // İsim iki satıra inebilir: isim ve açıklama aynı akışta (üst üste binmez)
        var flow = new StackPanel { Width = 330 };
        flow.Children.Add(Txt(d.Name, 24, Theme.B("#f0d890"), true, TextAlignment.Center, 330));
        var info = new List<string> { Texts.Faction(d.Faction) + (d.Hero ? " · Kahraman" : "") };
        if (d.IsUnit) info.Add($"Güç {d.Strength} · {Texts.RowOf(d)}");
        foreach (var a in d.Abilities) info.Add(Texts.AbilityText(a));
        if (d.IsLeader) info.Add(Texts.LeaderText(d));
        if (d.Hero) info.Add("Kahraman: hava, Yak ve Yem gibi etkilerden etkilenmez.");
        var tb = Txt(string.Join("\n", info), 15, Theme.B("#d8cdb4"), false, TextAlignment.Center, 330);
        tb.Margin = new Thickness(0, 6, 0, 0);
        flow.Children.Add(tb);
        Put(layer, flow, 0, y);
    }

    // ---------- Düğmeler ve durum ----------

    private void BuildControls()
    {
        if (_s.IsSpectator) { BuildSpectatorControls(); return; }
        // Pas düğmesi (oyundaki [BOŞLUK] Pas etiketi)
        var passEnabled = MyTurn;
        var label = new Border
        {
            Width = 300, Height = 46, CornerRadius = new CornerRadius(4), Cursor = passEnabled ? Cursors.Hand : Cursors.Arrow,
            Background = Theme.B("#e6120c07"), BorderBrush = passEnabled ? Theme.B("#c9a24b") : Theme.B("#3a2e1e"), BorderThickness = new Thickness(1.5),
            Opacity = passEnabled ? 1 : 0.55, Tag = "pass-button"
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Txt("[BOŞLUK]", 16, Theme.B("#e8c970"), true));
        sp.Children.Add(Txt("  Pas geç", 20, Theme.B("#f0e6d0")));
        label.Child = sp;
        label.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnPass(); };
        Put(_board, label, 125, 612);

        // Durum / ipucu
        Put(_board, Txt(StatusText(), 17, Theme.B("#e8dcc4"), false, TextAlignment.Left, 320), 125, 238);
        if (!string.IsNullOrEmpty(_hint)) Put(_board, Txt(_hint, 14, Theme.B("#b8ad94"), false, TextAlignment.Left, 320, true), 125, 262);

        // Alt şerit: Oyna düğmesi, ses ve çıkış
        if (_selected != null && MyTurn && NeedsNoTarget(_selected.Def))
        {
            var play = Ui.Button($"Oyna: {_selected.Def.Name}", () => { if (_selected is { } sel && MyTurn) DoPlay(sel); }, primary: true);
            play.FontSize = 20;
            play.Padding = new Thickness(26, 8, 26, 8);
            Put(_board, play, 680, 984);
        }
        var cfgBtn = Ui.Button("Ayarlar", OpenSettings);
        cfgBtn.FontSize = 13; cfgBtn.Padding = new Thickness(10, 3, 10, 3);
        cfgBtn.Tag = "settings-button";
        Put(_board, cfgBtn, 1290, 1030);
        if (_s.IsRemote && !_s.IsOver)
        {
            var resign = Ui.Button("Teslim ol", () => { _confirm = "resign"; Refresh(); });
            resign.FontSize = 13; resign.Padding = new Thickness(10, 3, 10, 3);
            resign.Tag = "resign-button";
            Put(_board, resign, 1190, 1030);
        }
        var quit = Ui.Button("Menüye dön", OnMenuClicked);
        quit.FontSize = 13; quit.Padding = new Thickness(10, 3, 10, 3);
        Put(_board, quit, 1380, 1030);
    }

    /// <summary>İzleyici arayüzü: bakış değiştirme, durum ve çıkış. Hamle düğmeleri yoktur.</summary>
    private void BuildSpectatorControls()
    {
        var flip = Ui.Button($"Bakış: {_s.MyName}  ⇄", () => { _s.FlipView(); _selected = null; Refresh(); });
        flip.FontSize = 16;
        flip.Tag = "spectate-flip";
        flip.Width = 300;
        Put(_board, flip, 125, 612);
        Put(_board, Txt(StatusText(), 17, Theme.B("#e8dcc4"), false, TextAlignment.Left, 320), 125, 238);
        var quit = Ui.Button("İzlemeyi bırak", () => _toMenu(), primary: true);
        quit.FontSize = 15;
        quit.Padding = new Thickness(14, 4, 14, 4);
        quit.Tag = "spectate-quit";
        Put(_board, quit, 1330, 1026);
    }

    private string StatusText()
    {
        if (_s.IsSpectator)
        {
            if (_s.IsOver) return "👁 Maç bitti.";
            if (G.Phase == Phase.Mulligan) return "👁 Kart değiştirme sürüyor…";
            var who = G.Phase == Phase.Playing ? (G.Current == Me ? _s.MyName : _s.OppName) : "";
            return who == "" ? $"👁 İzliyorsun · Raund {G.Round}" : $"👁 İzliyorsun · Raund {G.Round} · sıra: {who}";
        }
        if (_s.IsOver) return "Oyun bitti.";
        if (G.Phase == Phase.Mulligan) return G.PlayersToAct().Contains(Me) ? "Kartlarını değiştir." : "Rakibin kart değiştirmesi bekleniyor…";
        if (G.Pending is { } p) return p.Player == Me ? "Bir seçim yapman gerekiyor." : "Rakip seçim yapıyor…";
        if (G.Players[Me].Passed) return "Pas geçtin. Rakip oynuyor…";
        if (_aiRunning || G.Current == Opp) return "Rakip düşünüyor…";
        return $"Raund {G.Round} · senin sıran";
    }

    // ---------- Eylemler ----------

    private void OnRowClicked(int side, RowKind rk)
    {
        if (_selected == null || !MyTurn) return;
        var d = _selected.Def;
        if (d.IsUnit && d.IsAgile) DoPlay(_selected, rk);
        else if (d.IsUnit) DoPlay(_selected);
        else if (d.Id is Ids.Horn or Ids.Mardroeme) DoPlay(_selected, rk);
    }

    private void DoPlay(CardInstance c, RowKind? row = null, int? target = null)
    {
        var r = _s.Apply(new PlayCard(Me, c.Uid, row, target));
        if (!r.Ok) { _hint = r.Error ?? "Bu hamle yapılamaz."; Refresh(); return; }
        AfterHumanAction();
    }

    private void OnPass()
    {
        if (!MyTurn) return;
        var r = _s.Apply(new Pass(Me));
        if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
        AfterHumanAction();
    }

    private void OnLeader()
    {
        if (!MyTurn) return;
        var reason = G.LeaderBlockedReason(Me);
        if (reason != null) { _hint = reason; Refresh(); return; }
        var r = _s.Apply(new UseLeader(Me));
        if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
        AfterHumanAction();
    }

    private void AfterHumanAction()
    {
        _selected = null;
        _hint = "";
        Refresh();
        KickAi();
    }

    // ---------- Maç başı: yazı-tura ve kart dağıtımı ----------

    private void StartOpening()
    {
        _dealStarted = true;
        var coin = _dealDelay > 1000;
        if (coin) PlayCoin(G.CoinFirst);
        Fx.After(_dealDelay + G.Players[Me].Hand.Count * 110 + 700, FinishOpening);
    }

    private void FinishOpening()
    {
        if (!_opening) return;
        _opening = false;
        if (_coinLayer != null) { _fx.Children.Remove(_coinLayer); _coinLayer = null; }
        if (IsLoaded) Refresh();
    }

    /// <summary>Oyunun kendi madalyonu havaya atılır, döne döne düşer; kale yüzü sen, meşale yüzü rakip başlar.</summary>
    private void PlayCoin(int first)
    {
        var layer = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        Panel.SetZIndex(layer, 60);
        _fx.Children.Add(layer);
        _coinLayer = layer;
        var dim = new Rectangle { Width = W, Height = H, Fill = Theme.B("#99000000"), Opacity = 0 };
        layer.Children.Add(dim);
        dim.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        var faceA = Assets.Ui("coin_a");   // kale: sen
        var faceB = Assets.Ui("coin_b");   // meşale: rakip
        const double size = 230;
        var coin = new System.Windows.Controls.Image { Width = size, Height = size, Stretch = Stretch.Uniform, Source = faceA, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(coin, BitmapScalingMode.HighQuality);
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        coin.RenderTransform = new TransformGroup { Children = { scale, move } };
        const double cx = 1110 - size / 2, cy = 520 - size / 2;
        Put(layer, coin, cx, cy);

        var label = Outlined(first == Me ? "Sen başlıyorsun" : "Rakip başlıyor", 40, Theme.B("#f0d890"), 900);
        label.TextAlignment = TextAlignment.Center;
        label.Opacity = 0;
        Put(layer, label, 1110 - 450, cy + size + 40);

        Sfx.Play("coin_toss");
        var halfTurns = first == Me ? 8 : 7;     // çift: kale (sen), tek: meşale (rakip)
        const double total = 1900;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EventHandler? onFrame = null;
        onFrame = (_, _) =>
        {
            var t = Math.Min(1, sw.ElapsedMilliseconds / total);
            var eased = 1 - Math.Pow(1 - t, 2.2);
            var theta = Math.PI * halfTurns * eased;
            var turn = (int)Math.Floor(theta / Math.PI);
            coin.Source = turn % 2 == 0 ? faceA : faceB;
            scale.ScaleY = Math.Max(0.04, Math.Abs(Math.Cos(theta)));
            var arc = Math.Sin(Math.PI * t);
            move.Y = -arc * 240;
            var pop = 1 + 0.35 * arc;
            scale.ScaleX = pop;
            scale.ScaleY *= pop;
            if (t >= 1)
            {
                CompositionTarget.Rendering -= onFrame;
                coin.Source = first == Me ? faceA : faceB;
                scale.ScaleX = scale.ScaleY = 1;
                move.Y = 0;
                label.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                Fx.PlayFrames(_fx, "burst", new Point(1110, 520), 300, 300, 36);
            }
        };
        CompositionTarget.Rendering += onFrame;

        Fx.After(3000, () =>
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) => _fx.Children.Remove(layer);
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    // ---------- Global etkili kartların sinematiği ----------

    /// <summary>Boynuz, Yak, Mardroeme, hava kartları ve lider yeteneğinin çağırdığı kartlar (Yem hariç) herkese gösterilir.</summary>
    private (int Player, CardDef Def, string Caption)? FindShowcase(IReadOnlyList<GameEvent> evs)
    {
        foreach (var e in evs)
        {
            if (e.Kind == "play")
            {
                var d = G.Catalog.Get(e.Detail);
                if (!d.IsUnit && d.Id != Ids.Decoy) return (e.Player, d, $"{Texts.Who(e.Player, Me)} oynadı");
            }
            else if (e.Kind == "leader_card")
            {
                var leader = evs.FirstOrDefault(x => x.Kind == "leader" && x.Player == e.Player);
                var name = leader != null ? G.Catalog.Get(leader.Detail).Name : "lider";
                return (e.Player, G.Catalog.Get(e.Detail), $"{Texts.Who(e.Player, Me)}: lider yeteneği — {name}");
            }
        }
        return null;
    }

    /// <summary>Kart ekran ortasına kayarak gelir, iki oyuncuya da görünür, sonra gider; etkisi o zaman işlenir.</summary>
    private void StartCinema(int player, CardDef def, string caption, IReadOnlyList<GameEvent> evs)
    {
        _cinema = true;
        // Sinematik süresince yeniden çizim yapılmaz: açık kalan panel (ör. lider "Kullan" ekranı) masayı ve sinematiği örtmesin
        _zoom = null;
        _graveView = null;
        _overlay.Visibility = Visibility.Collapsed;
        _forcedEvs = evs;
        _pauseAfterMs = Math.Max(_pauseAfterMs, 400);

        var layer = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        Panel.SetZIndex(layer, 60);
        _fx.Children.Add(layer);

        var dim = new Rectangle { Width = W, Height = H, Fill = Theme.B("#aa000000"), Opacity = 0 };
        layer.Children.Add(dim);
        dim.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));

        const double w = 300;
        var h = w * GwintCard.Aspect;
        var pos = new Point(1110 - w / 2, 500 - h / 2);
        var card = new GwintCard(def, w, null, null, height: h) { IsHitTestVisible = false };
        var halo = new Ellipse
        {
            Width = w * 2.1, Height = h * 1.7, IsHitTestVisible = false, Opacity = 0,
            Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(0x70, 255, 205, 100), 0), new GradientStop(Color.FromArgb(0, 255, 205, 100), 1) } }
        };
        Put(layer, halo, 1110 - w * 1.05, 500 - h * 0.85);
        halo.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(250) });
        Put(layer, card, pos.X, pos.Y);
        Fx.FlyIn(card, player == Me ? new Point(900, 880) : new Point(1000, -300), pos, 560);

        var cap = Outlined(caption, 30, Theme.B("#f0d890"), 900);
        cap.TextAlignment = TextAlignment.Center;
        Put(layer, cap, 1110 - 450, pos.Y + h + 18);
        var sub = Txt(def.Name, 22, Theme.B("#e8dcc4"), false, TextAlignment.Center, 900, true);
        Put(layer, sub, 1110 - 450, pos.Y + h + 62);
        cap.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(300) });
        sub.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(300) });

        Fx.After(1650, () =>
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
            fade.Completed += (_, _) =>
            {
                _fx.Children.Remove(layer);
                _cinema = false;
                if (IsLoaded) Refresh();
            };
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    /// <summary>Olay getirmeyen yeniden çizim, süren kart animasyonunu silmesin diye bitişine ertelenir.</summary>
    private void SoftRefresh()
    {
        var left = (_animUntil - DateTime.UtcNow).TotalMilliseconds;
        if (!_animate || left <= 0) Refresh();
        else Fx.After((int)left + 20, () => { if (IsLoaded) Refresh(); });
    }

    private async void KickAi()
    {
        if (_aiRunning) return;
        if (!_s.OpponentBusy) return;
        _aiRunning = true;
        try
        {
            SoftRefresh();
            while (!_s.IsOver && _s.OpponentBusy)
            {
                // Kendi hamlemin sunucu yankısı beklemeden işlenir; rakip hamleleri (yapay zekâ ya da uzaktaki oyuncu) kısa bir soluklanmayla
                var mine = _s.IsRemote && !_s.IsSpectator && _s.PeekRemotePlayer() == Me;
                if (_animate)
                {
                    if (!mine) await Task.Delay(_s.IsRemote ? (G.Phase == Phase.Playing ? 450 : 150) : (G.Phase == Phase.Playing ? 850 : 350));
                    while (_cinema && IsLoaded) await Task.Delay(100);
                    // Sinematik bitince çizilen efektler (yanma, hayaletler) bitmeden rakip yeni kart atmasın
                    var left = (_animUntil - DateTime.UtcNow).TotalMilliseconds;
                    if (!mine && left > 0) await Task.Delay((int)left + 150);
                    if (!IsLoaded) return;
                }
                if (_s.IsRemote) _s.ApplyNextRemote(); else _s.AiStep();
                Refresh();
                if (_animate && _pauseAfterMs > 0) { await Task.Delay(_pauseAfterMs); _pauseAfterMs = 0; }
            }
        }
        catch (Exception ex)
        {
            _hint = "Rakip hata verdi: " + ex.Message;
            _s.Log.Add("⚠ " + _hint);
        }
        finally
        {
            _aiRunning = false;
            SoftRefresh();
        }
    }

    // ---------- Ses, banner, animasyon ----------

    private void PulseIfChanged(FrameworkElement el, string key, int value)
    {
        _curTotals[key] = value;
        if (_effects && !_firstDraw && _prevTotals.TryGetValue(key, out var old) && old != value) Fx.Pulse(el);
    }

    private static string RowSound(CardDef d) => d.Has("spy") ? "spy" : d.Hero ? "hero" : d.Row switch
    {
        "ranged" => "long_range",
        "siege" => "siege_weapon",
        _ => "close_combat"
    };

    // ---------- Animasyonlar: varış, hayalet kartlar, efektler ----------

    /// <summary>Kartın altına düşen yumuşak gölge: kart masadan yüksekte duruyormuş gibi görünür.</summary>
    private static Grid CardShadow(double w, double h)
    {
        var g = new Grid { Width = w, Height = h, IsHitTestVisible = false };
        foreach (var (grow, alpha) in new[] { (7.0, 0x16), (4.0, 0x22), (1.5, 0x34) })
            g.Children.Add(new Rectangle
            {
                Width = w + grow * 2, Height = h + grow * 2, RadiusX = 4 + grow, RadiusY = 4 + grow, IsHitTestVisible = false,
                Fill = new SolidColorBrush(Color.FromArgb((byte)alpha, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        return g;
    }

    private static Point Center(Point topLeft) => new(topLeft.X + CardW / 2, topLeft.Y + CardH / 2);

    /// <summary>Öğe (dx, dy) kadar geride başlar, <paramref name="holdMs"/> bekler, sonra yerine kayar.</summary>
    private static void SlideFrom(FrameworkElement el, double dx, double dy, int holdMs)
    {
        if (el.RenderTransform is not null and not MatrixTransform) return;   // başka bir dönüşüm (ör. üzerine gelme) varsa dokunma
        var tt = new TranslateTransform(dx, dy);
        el.RenderTransform = tt;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
        var begin = TimeSpan.FromMilliseconds(holdMs);
        tt.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });
    }

    /// <summary>Sahaya yeni inen kart: elden/desteden/mezarlıktan uçar, inince efekt oynar.</summary>
    private void AnimateArrival(GwintCard gc, CardInstance card, int side, Point pos)
    {
        Point? from = null;
        if (_prevHandPos.TryGetValue(card.Uid, out var hp)) from = hp;
        else if (_prevGraveInfo.TryGetValue(card.Uid, out var gs)) from = GravePos[Side(gs)];
        else if (!card.Def.Token)
            from = side == Opp || _evs.Any(e => e.Kind == "play" && e.Player == Opp) ? new Point(CardsX + 200, -150) : DeckPos[0];

        if (from == null) { Fx.CardIn(gc, side == Me ? 90 : -90); Landing(card.Def, Center(pos), 0); return; }
        Fx.FlyIn(gc, from.Value, pos, 520);
        Landing(card.Def, Center(pos), 440);
        // Çarpma: ağır kartlarda (kahraman / güçlü) tahta biraz daha sarsılır
        var heavy = card.Def.Hero || card.Def.Strength >= 8;
        Fx.After(520, () => { if (IsLoaded) Fx.Shake(_board, heavy ? 3.5 : 1.6, heavy ? 230 : 150); });
    }

    /// <summary>Kartın iniş efekti: kahraman → yıldırım, Medic → duman, toplanma/jeton → bulut, diğerleri → toz patlaması.</summary>
    private void Landing(CardDef d, Point c, int delay)
    {
        if (d.Hero) Fx.PlayFrames(_fx, "lightning", c, 230, 205, 34, delay);
        else if (d.Has("medic")) Fx.PlayFrames(_fx, "smoke", new Point(c.X, c.Y - 14), 190, 142, 26, delay);
        else if (d.Has("muster") || d.Token) Fx.PlayFrames(_fx, "cloud", c, 170, 170, 30, delay);
        else Fx.PlayFrames(_fx, "burst", c, 190, 190, 36, delay);
    }

    /// <summary>Sahadan kaybolan kartlar için hayalet çizer: yanar, mezarlığa uçar, ele döner ya da solar. Kırılan mücevher de burada.</summary>
    private void SpawnGhosts(IReadOnlyList<GameEvent> evs)
    {
        var roundEnd = evs.Any(e => e.Kind == "round_end");
        var killed = evs.Where(e => e.Kind == "kill").Select(e => e.CardUid).ToHashSet();
        var decoyed = evs.Where(e => e.Kind == "decoy").Select(e => e.CardUid).ToHashSet();
        var k = 0;
        foreach (var (uid, snap) in _prevBoardInfo)
        {
            if (_curBoardInfo.ContainsKey(uid)) continue;
            var ghost = new GwintCard(snap.Def, CardW, null, null, height: CardH) { IsHitTestVisible = false };
            Put(_fx, ghost, snap.Pos.X, snap.Pos.Y);
            Action remove = () => _fx.Children.Remove(ghost);
            if (killed.Contains(uid))
            {
                Fx.PlayFrames(_fx, "fire", new Point(snap.Pos.X + CardW / 2, snap.Pos.Y + CardH / 2 - 8), 150, 190, 30);
                var soot = new Rectangle { Width = CardW, Height = CardH, Fill = Theme.B("#000000"), Opacity = 0, IsHitTestVisible = false };
                Put(_fx, soot, snap.Pos.X, snap.Pos.Y);
                soot.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 0.55, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(250) });
                Fx.FadeOut(soot, 700, 1000, () => _fx.Children.Remove(soot));
                Fx.FadeOut(ghost, 700, 1000, remove);
            }
            else if (decoyed.Contains(uid))
            {
                var target = snap.Side != Me ? new Point(CardsX + 200, -150)
                    : _curHandPos.TryGetValue(uid, out var hp) ? hp : new Point(950, 842);
                Fx.FlyAway(ghost, target, 560, 0, remove);
            }
            else if (roundEnd)
            {
                Fx.FlyAway(ghost, new Point(GravePos[Side(snap.Side)].X + 10, GravePos[Side(snap.Side)].Y + 10), 650, Math.Min(500, k * 35), remove);
            }
            else Fx.FadeOut(ghost, 400, 0, remove);
            k++;
        }

        if (killed.Count > 0) Fx.Shake(_board, 6, 360);

        // Kaybedilen mücevher: kırılır
        for (var p = 0; p < 2; p++)
        {
            if (G.Players[p].Gems >= _prevGems[p]) continue;
            var baseY = p == Opp ? 280.0 : 665.0;
            var gem = Pic("gem", 36, 36);
            if (gem.Source == null) continue;
            var gx = 318 + (G.Players[p].Gems) * 42;
            Put(_fx, gem, gx, baseY + 76);
            Fx.Shatter(gem, () => _fx.Children.Remove(gem));
            Fx.PlayFrames(_fx, "burst", new Point(gx + 18, baseY + 94), 110, 110, 36, 0);
        }
    }

    private UIElement ZoomPanel(int player, CardDef def)
    {
        var ps = G.Players[player];
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var card = new GwintCard(def, 300, null, null, height: 300 * GwintCard.Aspect) { Cursor = Cursors.Arrow, ToolTip = null };
        sp.Children.Add(card);

        var right = new StackPanel { Width = 520, Margin = new Thickness(36, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Txt(def.Name, 36, Theme.B("#f0d890"), true, TextAlignment.Left, 520));
        right.Children.Add(Txt(Texts.Faction(def.Faction) + (player == Me ? " · senin liderin" : " · rakibin lideri"), 20, Theme.TextDim, false, TextAlignment.Left, 520));
        var desc = Txt(Texts.LeaderText(def), 24, Theme.Text, false, TextAlignment.Left, 520);
        desc.Margin = new Thickness(0, 22, 0, 18);
        right.Children.Add(desc);

        var passive = IsPassiveLeader(def);
        string status;
        if (passive) status = "Bu yetenek otomatiktir; kullanılmaz, oyun boyunca etkindir.";
        else if (ps.LeaderUsed) status = "Bu lider yeteneği bu oyunda zaten kullanıldı.";
        else if (G.LeaderCancelled(player)) status = "Rakibin lideri bu yeteneği iptal etti.";
        else if (player != Me) status = "Rakibin lider yeteneği henüz kullanılmadı.";
        else status = G.LeaderBlockedReason(Me) ?? (MyTurn ? "Oyun başına bir kez kullanılır ve bir turunu harcar." : "Şu an sıra sende değil.");
        right.Children.Add(Txt(status, 19, Theme.B("#c9b88a"), false, TextAlignment.Left, 520, true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        var canUse = player == Me && MyTurn && G.LeaderBlockedReason(Me) == null;
        if (player == Me)
        {
            var use = Ui.Button("Kullan", () => { _zoom = null; OnLeader(); }, primary: true, enabled: canUse);
            use.FontSize = 22; use.Padding = new Thickness(36, 10, 36, 10);
            buttons.Children.Add(use);
        }
        var cancel = Ui.Button(player == Me ? "Vazgeç" : "Kapat", () => { _zoom = null; Refresh(); }, primary: player != Me);
        cancel.FontSize = 22; cancel.Padding = new Thickness(36, 10, 36, 10);
        cancel.Margin = new Thickness(player == Me ? 14 : 0, 0, 0, 0);
        buttons.Children.Add(cancel);
        right.Children.Add(buttons);
        sp.Children.Add(right);
        return sp;
    }

    private UIElement GravePanel(int side)
    {
        var ps = G.Players[side];
        var sp = new StackPanel { MinWidth = 900 };
        sp.Children.Add(Title($"{(side == Me ? "Senin" : "Rakibin")} mezarlığı — {ps.Grave.Count} kart"));
        sp.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 18));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in ps.Grave)
        {
            var gc = MakeCard(c, 150, false, height: 201);
            gc.Margin = new Thickness(6);
            gc.Tag = "grave-card";
            row.Children.Add(gc);
        }
        if (ps.Grave.Count == 0) row.Children.Add(Txt("Mezarlık boş.", 24, Theme.TextDim, false, TextAlignment.Center, 900, true));
        var scroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 1350,
            Padding = new Thickness(0, 0, 0, 14)
        };
        sp.Children.Add(scroll);
        sp.Children.Add(Txt("Kaydırmak için fare tekerleğini ya da kaydırma çubuğunu kullan. Bir kartın üzerine gelince ayrıntısı görünür.", 16, Theme.TextDim, false, TextAlignment.Center, 1200, true));

        var close = Ui.Button("Kapat", () => { _graveView = null; Refresh(); }, primary: true);
        close.FontSize = 22; close.Padding = new Thickness(40, 10, 40, 10);
        close.Margin = new Thickness(0, 20, 0, 0);
        close.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(close);
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true; };
        return sp;
    }

    private void HandleEvents(IReadOnlyList<GameEvent> evs)
    {
        if (evs.Count == 0) return;
        var sounds = new List<string>();     // hemen çalan sesler
        var landing = new List<string>();    // kart sahaya inince çalan sesler (uçuş bitince)
        string? title = null, sub = null, icon = null;
        Brush color = Theme.Gold;
        var hold = 800;

        foreach (var e in evs)
        {
            switch (e.Kind)
            {
                case "play":
                {
                    var d = G.Catalog.Get(e.Detail);
                    (d.IsUnit || d.Id == Ids.Decoy ? landing : sounds).Add(d.IsWeather ? (d.Has("frost") ? "frost" : d.Has("fog") ? "fog" : d.Has("rain") ? "rain" : d.Has("storm") ? "ske_tidal_wave" : "clear_weather")
                        : d.Id == Ids.Horn ? "horn" : d.Id == Ids.Scorch ? "scorch" : d.Id == Ids.Decoy ? "dummy"
                        : d.Id == Ids.Mardroeme ? "ske_mushroom" : RowSound(d));
                    break;
                }
                case "place":
                    if (!evs.Any(x => x.Kind == "play" && x.CardUid == e.CardUid))
                    {
                        var d = G.Catalog.Get(G.Players.SelectMany(p => p.Rows.SelectMany(r => r.Cards)).FirstOrDefault(c => c.Uid == e.CardUid)?.Def.Id ?? Ids.Decoy);
                        landing.Add(d.IsUnit ? RowSound(d) : "dummy");
                    }
                    break;
                case "muster": landing.Add("summon_clones"); break;
                case "draw": sounds.Add(e.Detail == "2" ? "draw_2" : "draw_card"); break;
                case "kill": sounds.Add("scorch"); break;
                case "weather": break;
                case "transform": sounds.Add("ske_berserker"); break;
                case "spawn": landing.Add("cow_death"); break;
                case "decoy": landing.Add("dummy"); break;
                case "skellige_revive": landing.Add("resurrect"); break;
                case "curse_weather":
                    if (title == null || G.Round > 1) { title = "Bitmeyen Kış"; sub = G.Catalog.Get(e.Detail).Name + " raundu açtı"; color = Theme.B("#b8c8f0"); hold = 1100; }
                    break;
                case "curse_discard":
                    title = "Kan Bedeli";
                    sub = (e.Player == Me ? "Raundu kazandın ve kaybettin: " : "Rakip raundu kazandı ve kaybetti: ") + G.Catalog.Get(e.Detail).Name;
                    color = Theme.B("#e07060");
                    hold = 1500;
                    break;
                case "commander":
                    sounds.Add("dummy");
                    if (title == null) { title = e.Player == Me ? "Komutanını taşıdın" : "Rakip komutanını taşıdı"; sub = Texts.Row(Enum.Parse<RowKind>(e.Detail)); color = Theme.Gold; hold = 900; }
                    break;
                case "bridge_fall":
                    title = "Köprü dolu";
                    sub = G.Catalog.Get(e.Detail).Name + " köprüden düştü";
                    color = Theme.Bad;
                    hold = 1200;
                    break;
                case "terrain":
                    if (G.Round > 1 && G.Options.Terrain is { } tm)
                    {
                        var ch = Enum.GetValues<RowKind>().Where(rk => tm.At(Me, rk, G.Round) != tm.At(Me, rk, G.Round - 1))
                            .Select(rk => $"{Texts.Row(rk)}: {GwentAI.Battlefields.Name(tm.At(Me, rk, G.Round))}").ToList();
                        if (ch.Count > 0) sub = (sub == null ? "" : sub + Environment.NewLine) + "Arazi değişti — " + string.Join(", ", ch);
                    }
                    break;
                case "coin_toss":
                case "first_chosen": sounds.Add("coin_toss"); break;
                case "pass":
                    sounds.Add("lock_in");
                    if (title == null) { title = e.Player == Me ? "Pas geçtin" : "Rakip pas geçti"; color = Theme.TextDim; hold = 700; icon = "msg_round_pass"; }
                    break;
                case "leader":
                    sounds.Add("leader_change");
                    if (_leaderEl[e.Player] is { } le)
                    {
                        Fx.Pulse(le);
                        var lc = new Point(133 + 56, (e.Player == Opp ? 75.0 : 828.0) + 73);
                        Fx.PlayFrames(_fx, "lightning", lc, 260, 232, 34);
                    }
                    title = $"{Texts.Who(e.Player, Me)}: lider yeteneği";
                    sub = _s.Game.Catalog.Get(e.Detail).Name;
                    color = Theme.Gold;
                    hold = 1000;
                    break;
                case "round_end":
                {
                    var parts = e.Detail.Split(':');
                    if (e.Player == Me) { title = "Raundu kazandın!"; color = Theme.Good; sounds.Add("battle_won"); icon = "msg_round_victory"; }
                    else if (e.Player == Opp) { title = "Raundu rakip kazandı"; color = Theme.Bad; sounds.Add("battle_lost"); icon = "msg_round_defeat"; }
                    else { title = "Raund berabere"; color = Theme.Gold; sounds.Add("battle_draw"); icon = "msg_round_draw"; }
                    sub = $"Sen {parts[Me]}  –  Rakip {parts[Opp]}";
                    hold = 1500;
                    _pauseAfterMs = 2000;
                    sounds.Add("gem_destruction");
                    break;
                }
                case "round_start":
                    if (G.Round > 1)
                    {
                        var starter = $"Raund {G.Round}: {(e.Player == Me ? "sen başlıyorsun" : "rakip başlıyor")}";
                        if (title == null) { title = starter; color = Theme.Gold; hold = 900; icon = "msg_round_start"; }
                        else sub = (sub == null ? "" : sub + "\n") + starter;
                    }
                    break;
                case "game_end":
                    sounds.Add(e.Player == Me ? "clash_victory" : e.Player == Opp ? "clash_defeat" : "battle_draw");
                    break;
            }
        }
        // Sıra bildirimi
        if (title == null && G.Phase == Phase.Playing && G.Pending == null && evs.Any(x => x.Kind is "play" or "pass" or "leader") && G.Current == Me && !G.Players[Me].Passed)
            sounds.Add("your_turn");

        foreach (var snd in sounds.Distinct().Where(x => x != "your_turn" || landing.Count == 0)) Sfx.Play(snd);
        if (landing.Count > 0)
        {
            // Kartlar 520 ms uçarak iner (Fx.FlyIn); çarpma sesi o ana denk gelir. Sıra bildirimi inişten hemen sonra.
            var flies = _animate && _effects && !_firstDraw;
            var delay = flies ? 520 : 0;
            var yourTurn = sounds.Contains("your_turn");
            void PlayLanding()
            {
                if (!IsLoaded) return;
                foreach (var snd in landing.Distinct()) Sfx.Play(snd);
                if (yourTurn) Fx.After(flies ? 380 : 0, () => { if (IsLoaded) Sfx.Play("your_turn"); });
            }
            if (delay == 0) PlayLanding(); else Fx.After(delay, PlayLanding);
        }
        if (title != null && !_s.IsOver) ShowBanner(title, sub, color, hold, icon);
    }

    private void ShowBanner(string title, string? sub, Brush color, int holdMs, string? icon)
    {
        _bannerHost.Children.Clear();
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var img = icon != null ? Pic(icon, 190, 150) : null;
        if (img?.Source != null) sp.Children.Add(img);
        sp.Children.Add(Txt(title, 56, color, true, TextAlignment.Center));
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(Txt(sub, 24, Theme.Text, false, TextAlignment.Center));
        var b = new Border
        {
            Background = Theme.B("#e0120d09"),
            BorderBrush = color,
            BorderThickness = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(60, 14, 60, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(450, 0, 0, 0),
            Opacity = 0,
            Child = sp
        };
        _bannerHost.Children.Add(b);
        Fx.Banner(b, holdMs, () => _bannerHost.Children.Remove(b));
    }

    // ---------- Pencereler ----------

    private void BuildOverlay()
    {
        _overlay.Children.Clear();
        UIElement? content = null;
        if (_opening) { _overlay.Visibility = Visibility.Collapsed; return; }

        if (_settingsOpen && !_s.IsOver && _settingsView != null)
        {
            _overlay.Background = Theme.B("#d00b0805");
            _overlay.Children.Add(_settingsView);
            _overlay.Visibility = Visibility.Visible;
            return;
        }
        if (_s.IsOver) { RecordStats(); content = GameOverPanel(); }
        else if (_confirm != null) content = ConfirmPanel();
        else if (G.Phase == Phase.Mulligan && G.PlayersToAct().Contains(Me) && !_s.IsSpectator) content = MulliganPanel();
        else if (G.Phase == Phase.ChooseFirst && G.Current == Me && !_s.IsSpectator) content = ChooseFirstPanel();
        else if (G.Pending is { } pc && pc.Player == Me && !_aiRunning && !_s.IsSpectator) content = ChoicePanel(pc);
        else if (_zoom is { } z) { _lastPending = null; content = ZoomPanel(z.Player, z.Def); }
        else if (_graveView is { } gv) { _lastPending = null; content = GravePanel(gv); }
        else _lastPending = null;

        if (content == null) { _overlay.Visibility = Visibility.Collapsed; return; }
        if (_s.IsOver)
        {
            // Oyun sonu: kutu yok; kazanınca altın, kaybedince kızıl bir ışıkla tam ekran sahne
            var win = _s.WinnerSeat;
            var c0 = win == Me ? Color.FromArgb(0xf0, 0x24, 0x1a, 0x08) : win == null ? Color.FromArgb(0xf0, 0x1c, 0x1a, 0x16) : Color.FromArgb(0xf0, 0x22, 0x0a, 0x07);
            _overlay.Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4), RadiusX = 0.8, RadiusY = 0.9,
                GradientStops = { new GradientStop(c0, 0), new GradientStop(Color.FromArgb(0xfa, 0x04, 0x03, 0x02), 1) }
            };
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            _overlay.Children.Add(content);
            _overlay.Visibility = Visibility.Visible;
            if (_effects && _animate && !_gameOverShown)
            {
                _gameOverShown = true;
                _overlay.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
                if (win == Me)
                    for (var i = 0; i < 7; i++)
                    {
                        var rnd = Random.Shared;
                        Fx.PlayFrames(_fx, "burst", new Point(250 + rnd.NextDouble() * 1400, 150 + rnd.NextDouble() * 600), 260, 260, 34, 300 + i * 260, 0.55);
                    }
            }
            return;
        }
        _overlay.Background = Theme.B("#d00b0805");
        var framed = Ornate.Panel(new ContentControl { Content = content, MaxWidth = 1500 }, new Thickness(54, 40, 54, 44), 22, 62);
        framed.HorizontalAlignment = HorizontalAlignment.Center;
        framed.VerticalAlignment = VerticalAlignment.Center;
        _overlay.Children.Add(framed);
        _overlay.Visibility = Visibility.Visible;
    }

    private TextBlock Title(string s) => Txt(s, 40, Theme.B("#e8c970"), true, TextAlignment.Center);

    private UIElement MulliganPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(Title("Kart değiştir"));
        var sub = Txt($"İstediğin en çok {G.MulligansFor(Me)} kartı seç; destene karışıp yerine yeni kart çekersin.", 20, Theme.TextDim, false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 6, 0, 22);
        sp.Children.Add(sub);

        var full = _mulliganSel.Count >= G.MulligansFor(Me);
        var count = Txt($"Seçilen: {_mulliganSel.Count} / {G.MulligansFor(Me)}" + (full ? "   ·   değiştirmek istemediğin karta yeniden tıkla" : ""), 20,
            _mulliganSel.Count > 0 ? Theme.B("#f0b070") : Theme.TextDim, true, TextAlignment.Center);
        count.Margin = new Thickness(0, 0, 0, 10);
        sp.Children.Add(count);

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1350 };
        foreach (var c in G.Players[Me].Hand)
        {
            var selected = _mulliganSel.Contains(c.Uid);
            var gc = MakeCard(c, 130, false, selected: selected, height: 174);
            gc.Tag = "mulligan";
            var uid = c.Uid;
            gc.MouseLeftButtonUp += (_, _) =>
            {
                if (!_mulliganSel.Remove(uid) && _mulliganSel.Count < G.MulligansFor(Me)) _mulliganSel.Add(uid);
                Refresh();
            };
            // Seçilen kart belirgin: yukarı kalkar, kızıl bir örtü ve "DEĞİŞECEK" etiketi; sınır dolunca seçilmeyenler hafifçe söner
            var tile = new Grid { Margin = new Thickness(6, 22, 6, 6) };
            tile.Children.Add(gc);
            if (selected)
            {
                tile.RenderTransform = new TranslateTransform(0, -18);
                tile.Children.Add(new Border
                {
                    IsHitTestVisible = false, CornerRadius = new CornerRadius(5), BorderBrush = Theme.B("#ff7a50"), BorderThickness = new Thickness(3),
                    Background = new LinearGradientBrush(Color.FromArgb(0x30, 0xb0, 0x30, 0x20), Color.FromArgb(0x90, 0x70, 0x10, 0x08), 90)
                });
                var mark = Txt("↻", 54, Theme.B("#ffe0c8"), true, TextAlignment.Center);
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 1 };
                tile.Children.Add(mark);
                tile.Children.Add(new Border
                {
                    IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -14),
                    Background = Theme.B("#e0a03a20"), BorderBrush = Theme.B("#ffb080"), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 2, 10, 3),
                    Child = Txt("DEĞİŞECEK", 15, Brushes.White, true, TextAlignment.Center)
                });
                tile.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 120, 60), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.85 };
            }
            else if (full) tile.Opacity = 0.55;
            wrap.Children.Add(tile);
        }
        sp.Children.Add(wrap);

        var swap = Ui.Button(_mulliganSel.Count == 0 ? "Değiştirmeden başla" : $"{_mulliganSel.Count} kartı değiştir", () =>
        {
            var r = _s.Apply(new Mulligan(Me, _mulliganSel.ToList()));
            _mulliganSel.Clear();
            if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
            AfterHumanAction();
        }, primary: true);
        swap.FontSize = 20;
        swap.Margin = new Thickness(0, 24, 0, 0);
        swap.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(swap);
        return sp;
    }

    private UIElement ChooseFirstPanel()
    {
        var sp = new StackPanel { MinWidth = 600 };
        sp.Children.Add(Title(G.Players[Me].Faction == "scoiatael" ? "Scoia'tael yeteneği" : "Kışla: kim başlasın?"));
        var sub = Txt("İlk turu kimin oynayacağını sen seçersin.", 22, Theme.TextDim, false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 6, 0, 22);
        sp.Children.Add(sub);
        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var me = Ui.Button("Ben başlayayım", () => { _s.Apply(new ChooseFirst(Me, Me)); AfterHumanAction(); }, primary: true);
        var opp = Ui.Button("Rakip başlasın", () => { _s.Apply(new ChooseFirst(Me, Opp)); AfterHumanAction(); });
        me.FontSize = 20; opp.FontSize = 20;
        opp.Margin = new Thickness(14, 0, 0, 0);
        b.Children.Add(me);
        b.Children.Add(opp);
        sp.Children.Add(b);
        return sp;
    }

    private UIElement ChoicePanel(PendingChoice pc)
    {
        if (!ReferenceEquals(_lastPending, pc))
        {
            _lastPending = pc;
            _choiceSel.Clear();
            _choiceRow = null;
            if (pc.Kind == ChoiceKind.AgileRow) _choiceSel.Add(pc.Candidates[0]);
        }

        var sp = new StackPanel { MinWidth = 700 };
        sp.Children.Add(Title(pc.Prompt));
        sp.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 20));

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1350 };
        foreach (var uid in pc.Candidates)
        {
            var c = FindCard(uid);
            if (c == null) continue;
            var selected = _choiceSel.Contains(uid);
            var gc = MakeCard(c, 130, false, selected: selected, height: 174);
            gc.Margin = new Thickness(6);
            gc.Tag = "choice";
            var u = uid;
            if (pc.Kind != ChoiceKind.AgileRow)
                gc.MouseLeftButtonUp += (_, _) =>
                {
                    if (!_choiceSel.Remove(u))
                    {
                        if (pc.Max == 1) _choiceSel.Clear();
                        if (_choiceSel.Count < pc.Max) _choiceSel.Add(u);
                    }
                    _choiceRow = null;
                    Refresh();
                };
            wrap.Children.Add(gc);
        }
        sp.Children.Add(wrap);

        var pickedAgile = _choiceSel.Count == 1 && FindCard(_choiceSel[0])?.Def.IsAgile == true;
        var needRow = pc.Kind == ChoiceKind.AgileRow || (pc.Kind == ChoiceKind.Medic && pickedAgile);
        if (needRow)
        {
            var q = Txt("Hangi sıraya konsun?", 20, Theme.TextDim, false, TextAlignment.Center);
            q.Margin = new Thickness(0, 18, 0, 6);
            sp.Children.Add(q);
            var rows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var rk in new[] { RowKind.Close, RowKind.Ranged })
            {
                var r = rk;
                var tb = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"),
                    Content = Ui.Text(Texts.Row(rk), 18, Theme.Text, true, TextAlignment.Center),
                    IsChecked = _choiceRow == rk,
                    Margin = new Thickness(5),
                    MinWidth = 200
                };
                tb.Click += (_, _) => { _choiceRow = r; Refresh(); };
                rows.Children.Add(tb);
            }
            sp.Children.Add(rows);
        }

        var ready = _choiceSel.Count >= pc.Min && _choiceSel.Count <= pc.Max && (!needRow || _choiceRow != null);
        var confirm = Ui.Button(pc.Max > 1 ? $"Onayla ({_choiceSel.Count}/{pc.Max})" : "Onayla", () =>
        {
            var r = _s.Apply(new Choose(Me, _choiceSel.ToList(), _choiceRow));
            if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
            _lastPending = null;
            AfterHumanAction();
        }, primary: true, enabled: ready);
        confirm.FontSize = 20;
        confirm.Margin = new Thickness(0, 24, 0, 0);
        confirm.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(confirm);
        return sp;
    }

    private bool _statsRecorded;

    private void RecordStats()
    {
        if (_statsRecorded) return;
        _statsRecorded = true;
        var win = _s.WinnerSeat;
        var online = _s.OnlineSetup;
        var opp = online != null ? (_s.MySeat == 0 ? online.Nick1 : online.Nick0)
            : _s.OppNameOverride != null ? (_s.ModeLabel ?? "Collect 'Em All") + ": " + _s.OppNameOverride
            : "Yapay zekâ (" + _s.Config.Difficulty switch { GwentAI.Difficulty.Easy => "Kolay", GwentAI.Difficulty.Hard => "Zor", _ => "Normal" } + ")";
        Stats.Record(new MatchRecord(DateTime.Now, online != null, opp, _s.Config.MyFaction, _s.Config.OppFaction, win == null ? 0 : win == Me ? 1 : -1));
        if (_s.Settle != null && !_s.IsSpectator) _careerText = _s.Settle(win == null ? null : win == _s.MySeat);
    }

    private string? _careerText;

    /// <summary>İzleyici için maç sonu: kazanan, raund skorları ve bracket'e dönüş.</summary>
    private UIElement SpectatorOverPanel()
    {
        var sp = new StackPanel { MinWidth = 800, HorizontalAlignment = HorizontalAlignment.Center };
        var winner = _s.WinnerSeat;
        var title = Txt(winner == null ? "Maç berabere bitti" : $"Kazanan: {(winner == _s.MySeat ? _s.MyName : _s.OppName)}", 52, Theme.B("#f0d890"), true, TextAlignment.Center);
        sp.Children.Add(title);
        sp.Children.Add(Ornate.Rule(560, 14));
        foreach (var r in G.History)
            sp.Children.Add(Txt($"Raund {r.Round}:  {(_s.MySeat == 0 ? r.Score0 : r.Score1)} – {(_s.MySeat == 0 ? r.Score1 : r.Score0)}", 26, Theme.B("#e8dcc4"), false, TextAlignment.Center));
        var auto = Txt("Birkaç saniye içinde kendiliğinden bracket'e dönülür.", 18, Theme.TextDim, false, TextAlignment.Center);
        auto.Margin = new Thickness(0, 20, 0, 0);
        sp.Children.Add(auto);
        var back = Ui.Button("Bracket'e dön", () => _toMenu(), primary: true);
        back.FontSize = 24;
        back.Padding = new Thickness(50, 12, 50, 12);
        back.Margin = new Thickness(0, 14, 0, 0);
        back.HorizontalAlignment = HorizontalAlignment.Center;
        back.Tag = "spectate-quit";
        sp.Children.Add(back);
        return sp;
    }

    private UIElement GameOverPanel()
    {
        if (_s.IsSpectator) return SpectatorOverPanel();
        var sp = new StackPanel { MinWidth = 900, HorizontalAlignment = HorizontalAlignment.Center };
        var winner = _s.WinnerSeat;
        // Sönük, antik bir palet: kazanınca eski altın, kaybedince paslı kızıl, beraberlikte gri
        var (title, color, frame, glow, g1, g2) = winner == Me
            ? ("Kazandın!", Theme.B("#d8bd78"), "msg_battle_victory", Color.FromRgb(150, 110, 40), Theme.B("#e0c47e"), Theme.B("#7a5c24"))
            : winner == null
                ? ("Berabere", Theme.B("#b8ad94"), "msg_battle_draw", Color.FromRgb(110, 105, 92), Theme.B("#c4baa4"), Theme.B("#625c4e"))
                : ("Kaybettin", Theme.B("#a8503f"), "msg_battle_defeat", Color.FromRgb(110, 30, 22), Theme.B("#b5584a"), Theme.B("#4a1812"));

        var src = Assets.Ui(frame);
        if (src != null)
        {
            // Orijinal simge yalnız şekil olarak kullanılır; rengi paletimizdeki gradyan verir (mavi/neon ton yok)
            var icon = new Rectangle
            {
                Width = 520, Height = 232, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(((SolidColorBrush)g1).Color, ((SolidColorBrush)g2).Color, 90),
                OpacityMask = new ImageBrush(src) { Stretch = Stretch.Uniform },
                Effect = new DropShadowEffect { Color = glow, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.55 }
            };
            sp.Children.Add(icon);
        }
        var t = Txt(title, 104, color, true, TextAlignment.Center);
        t.Effect = new DropShadowEffect { Color = glow, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 };
        sp.Children.Add(t);

        var myRounds = G.History.Count(r => r.Winner == Me);
        var oppRounds = G.History.Count(r => r.Winner == Opp);
        var sub = Txt($"Raund skoru   {myRounds} – {oppRounds}", 30, Theme.B("#e8dcc4"), false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 2, 0, 4);
        sp.Children.Add(sub);
        sp.Children.Add(Ornate.Rule(560, 14));

        // Raund tablosu
        var table = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var w in new[] { 150.0, 170, 170, 270 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        void Cell(string text, int row, int col, Brush fg, double size, bool bold)
        {
            var c = Txt(text, size, fg, bold, TextAlignment.Center);
            c.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            table.Children.Add(c);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (h, col) in new[] { ("Raund", 0), ("Sen", 1), ("Rakip", 2), ("Sonuç", 3) }) Cell(h, 0, col, Theme.TextDim, 20, true);
        var rowIx = 1;
        foreach (var r in G.History)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var mine = r.Winner == Me;
            var theirs = r.Winner == Opp;
            Cell(r.Round.ToString(), rowIx, 0, Theme.Text, 28, false);
            Cell((Me == 0 ? r.Score0 : r.Score1).ToString(), rowIx, 1, mine ? Theme.B("#d8bd78") : Theme.TextDim, 32, mine);
            Cell((Me == 0 ? r.Score1 : r.Score0).ToString(), rowIx, 2, theirs ? Theme.B("#d8bd78") : Theme.TextDim, 32, theirs);
            Cell(r.Winner == null ? "Berabere" : mine ? "Sen kazandın" : "Rakip kazandı", rowIx, 3, r.Winner == null ? Theme.TextDim : mine ? Theme.B("#c9b06c") : Theme.B("#9c5446"), 24, true);
            rowIx++;
        }
        sp.Children.Add(table);
        if (_careerText != null)
        {
            var reward = Txt(_careerText, 26, Theme.B("#f0d890"), true, TextAlignment.Center, 1100);
            reward.Margin = new Thickness(0, 14, 0, 0);
            reward.Tag = "career-reward";
            sp.Children.Add(reward);
        }

        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, 0) };
        var again = Ui.Button(_s.IsRemote ? "Rövanş iste" : "Yeniden oyna", () =>
        {
            if (_s.IsRemote) { _s.Link!.RequestRematch(); _hint = "Rövanş isteği gönderildi; rakibin kabul etmesi bekleniyor."; Refresh(); }
            else _rematch(_s.Config with { Seed = Random.Shared.Next(1, int.MaxValue) });
        }, primary: true);
        var menu = Ui.Button(_s.ExitLabel ?? (_s.TournamentMode ? "Bracket'e dön" : "Menü"), _toMenu);
        again.FontSize = 26; menu.FontSize = 26;
        again.Padding = new Thickness(50, 14, 50, 14); menu.Padding = new Thickness(50, 14, 50, 14);
        menu.Margin = new Thickness(18, 0, 0, 0);
        if (!_s.TournamentMode || winner == null) b.Children.Add(again);   // turnuvada rövanş yalnız berabere kalınca gerekir
        b.Children.Add(menu);
        sp.Children.Add(b);
        if (_s.IsRemote && !string.IsNullOrEmpty(_hint))
        {
            var h = Txt(_hint, 20, Theme.B("#c9b88a"), false, TextAlignment.Center, 900, true);
            h.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(h);
        }
        return sp;
    }
}
