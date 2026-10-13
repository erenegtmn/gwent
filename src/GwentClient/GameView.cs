using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Oyun ekranı. Durumu her değişiklikte baştan çizer (küçük bir oyun için yeterince hızlı ve hatasız).
/// İş mantığı motorda; burada yalnız seçim, tıklama ve görünüm vardır.
/// </summary>
public sealed class GameView : Grid
{
    private const int Me = GameSession.Me;
    private const int Opp = GameSession.Opp;

    private readonly GameSession _s;
    private readonly Action _toMenu;
    private readonly Action<MatchConfig> _rematch;
    private readonly bool _animate;   // yapay zekâ bekleme süreleri
    private readonly bool _effects;   // animasyon, ses, banner
    private HashSet<int> _prevBoard = new();
    private Dictionary<string, int> _prevTotals = new();
    private Dictionary<string, int> _curTotals = new();
    private bool _firstDraw = true;
    private int _pauseAfterMs;
    private readonly Grid _bannerHost = new() { IsHitTestVisible = false };
    private Game G => _s.Game;

    private CardInstance? _selected;
    private bool _aiRunning;
    private string _hint = "";

    private readonly Border _left = new();
    private readonly Viewbox _centerBox = new() { Stretch = Stretch.Uniform };
    private readonly Border _right = new();
    private readonly Grid _overlay = new() { Visibility = Visibility.Collapsed };
    private readonly Border _previewHost = new();
    private ScrollViewer? _logScroll;

    // Kart değiştirme / seçim penceresi durumu
    private readonly HashSet<int> _mulliganSel = new();
    private PendingChoice? _lastPending;
    private readonly List<int> _choiceSel = new();
    private RowKind? _choiceRow;

    public GameView(GameSession session, Action toMenu, Action<MatchConfig> rematch, bool animate = true, bool? effects = null)
    {
        _s = session;
        _toMenu = toMenu;
        _rematch = rematch;
        _animate = animate;
        _effects = effects ?? animate;
        Background = Theme.Bg;
        Focusable = true;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });
        SetColumn(_left, 0);
        SetColumn(_centerBox, 1);
        SetColumn(_right, 2);
        Children.Add(_left);
        Children.Add(_centerBox);
        Children.Add(_right);

        SetColumn(_bannerHost, 1);
        Panel.SetZIndex(_bannerHost, 90);
        Children.Add(_bannerHost);

        SetColumnSpan(_overlay, 3);
        Panel.SetZIndex(_overlay, 100);
        Children.Add(_overlay);

        KeyDown += (_, e) => { if (e.Key == Key.Escape && _selected != null) { _selected = null; _hint = ""; Refresh(); } };

        _s.AiAutoMulligan();
        Refresh();
        KickAi();
    }

    // ---------- Yardımcılar ----------

    private bool MyTurn => G.Phase == Phase.Playing && G.Pending == null && G.Current == Me
                           && !G.Players[Me].Passed && !_aiRunning && !_s.IsOver;

    private CardInstance? FindCard(int uid)
    {
        if (G.Pending?.LimboCard is { } l && l.Uid == uid) return l;
        foreach (var p in G.Players)
            foreach (var c in p.Hand.Concat(p.Deck).Concat(p.Grave).Concat(p.Rows.SelectMany(r => r.Cards)))
                if (c.Uid == uid) return c;
        return null;
    }

    private CardControl MakeCard(CardInstance c, CardSize size, bool onBoard, bool selected = false, bool highlight = false, bool dimmed = false)
    {
        var current = c.Def.IsUnit ? (onBoard ? G.CardScore(c) : c.Def.Strength) : (int?)null;
        var cc = new CardControl(c.Def, size, current, c, selected, dimmed, highlight);
        cc.MouseEnter += (_, _) => ShowPreview(c.Def, current);
        return cc;
    }

    private void ShowPreview(CardDef d, int? current)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var card = new CardControl(d, CardSize.Preview, current) { Cursor = Cursors.Arrow, ToolTip = null };
        sp.Children.Add(card);
        _previewHost.Child = sp;
    }

    // ---------- Çizim ----------

    public void Refresh()
    {
        // Seçili kart artık elde değilse bırak
        if (_selected != null && !G.Players[Me].Hand.Contains(_selected)) _selected = null;
        if (!MyTurn) _selected = null;

        BuildLeft();
        BuildCenter();
        BuildRight();
        BuildOverlay();
        _logScroll?.ScrollToEnd();

        var evs = _s.TakeEvents();
        if (_effects && !_firstDraw) HandleEvents(evs);
        _prevBoard = G.Players.SelectMany(p => p.Rows.SelectMany(r => r.Cards)).Select(c => c.Uid).ToHashSet();
        _prevTotals = _curTotals;
        _curTotals = new Dictionary<string, int>();
        _firstDraw = false;
    }

    private void BuildLeft()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var opp = PlayerBlock(Opp);
        SetRow(opp, 0);
        grid.Children.Add(opp);

        // Önizleme paneli yenilemeler arasında yaşar: eski ızgaradan ayırmadan yenisine eklenemez.
        if (_previewHost.Parent is Panel oldParent) oldParent.Children.Remove(_previewHost);
        _previewHost.HorizontalAlignment = HorizontalAlignment.Center;
        _previewHost.VerticalAlignment = VerticalAlignment.Center;
        if (_previewHost.Child == null)
            _previewHost.Child = Ui.Text("Bir kartın üzerine gel", 13, Theme.TextDim, false, TextAlignment.Center);
        SetRow(_previewHost, 1);
        grid.Children.Add(_previewHost);

        var me = PlayerBlock(Me);
        SetRow(me, 2);
        grid.Children.Add(me);
        _left.Child = grid;
    }

    private UIElement PlayerBlock(int p)
    {
        var ps = G.Players[p];
        var sp = new StackPanel();

        var head = new Border
        {
            Background = Theme.FactionColor(ps.Faction),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var headSp = new StackPanel();
        headSp.Children.Add(Ui.Text(p == Me ? "SEN" : "RAKİP", 10, Theme.B("#ccffffff"), true));
        headSp.Children.Add(Ui.Text(Texts.Faction(ps.Faction), 14, Theme.Text, true));
        head.Child = headSp;
        sp.Children.Add(head);

        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Lider kartı
        var leaderBox = new StackPanel();
        var leaderUsable = p == Me ? G.LeaderAvailable(p) : !ps.LeaderUsed && !G.LeaderCancelled(p);
        var lc = new CardControl(ps.Leader, CardSize.Board, null, null, false, dimmed: !leaderUsable && !IsPassiveLeader(ps.Leader));
        lc.MouseEnter += (_, _) => ShowPreview(ps.Leader, null);
        leaderBox.Children.Add(lc);
        string leaderState = IsPassiveLeader(ps.Leader)
            ? (G.LeaderCancelled(p) ? "İptal edildi" : "Otomatik")
            : ps.LeaderUsed ? "Kullanıldı" : G.LeaderCancelled(p) ? "İptal edildi" : "Hazır";
        leaderBox.Children.Add(Ui.Text(leaderState, 10, leaderState == "Hazır" ? Theme.Gold : Theme.TextDim, false, TextAlignment.Center));
        row.Children.Add(leaderBox);

        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var total = Ui.Text(G.PlayerTotal(p).ToString(), 38, Theme.Gold, true);
        PulseIfChanged(total, "p" + p, G.PlayerTotal(p));
        info.Children.Add(total);
        info.Children.Add(Gems(ps.Gems));
        info.Children.Add(Ui.Text($"El {ps.Hand.Count} · Deste {ps.Deck.Count}", 11, Theme.TextDim));
        info.Children.Add(Ui.Text($"Mezarlık {ps.Grave.Count}", 11, Theme.TextDim));
        if (ps.Passed) info.Children.Add(Ui.Text("PAS GEÇTİ", 11, Theme.Bad, true));
        else if (G.Phase == Phase.Playing && G.Pending == null && G.Current == p) info.Children.Add(Ui.Text("● SIRA", 11, Theme.Good, true));
        SetColumn(info, 1);
        row.Children.Add(info);
        sp.Children.Add(row);

        if (p == Me)
        {
            var pass = Ui.Button("Pas geç", OnPass, enabled: MyTurn);
            pass.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(pass);
            var leaderBtn = Ui.Button("Lider yeteneği", OnLeader, enabled: MyTurn && G.LeaderBlockedReason(Me) == null);
            leaderBtn.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(leaderBtn);
            if (MyTurn && G.LeaderBlockedReason(Me) is { } why && !IsPassiveLeader(ps.Leader) && !ps.LeaderUsed)
                sp.Children.Add(Ui.Text(why, 10, Theme.TextDim, false, TextAlignment.Left, true));
        }

        return Ui.Card(sp);
    }

    private static bool IsPassiveLeader(CardDef l) => l.Id is "emhyr_var_emreis_the_white_flame" or "emhyr_var_emreis_invader_of_the_north"
        or "eredin_breacc_glas_the_treacherous" or "francesca_findabair_daisy_of_the_valley" or "king_bran";

    private static UIElement Gems(int count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        for (var i = 0; i < 2; i++)
            sp.Children.Add(new Ellipse
            {
                Width = 15,
                Height = 15,
                Margin = new Thickness(0, 0, 5, 0),
                Fill = i < count ? Theme.B("#d94a4a") : Theme.B("#3a2e20"),
                Stroke = i < count ? Theme.B("#ffb0a0") : Theme.B("#5a4a32"),
                StrokeThickness = 1.5
            });
        return sp;
    }

    // ---------- Orta: saha + el ----------

    private void BuildCenter()
    {
        var root = new StackPanel { Width = 1000, Margin = new Thickness(6) };
        root.Background = Brushes.Transparent;
        root.MouseLeftButtonUp += (_, e) => { if (!e.Handled && _selected != null) { _selected = null; _hint = ""; Refresh(); } };

        foreach (var rk in new[] { RowKind.Siege, RowKind.Ranged, RowKind.Close }) root.Children.Add(BoardRow(Opp, rk));
        root.Children.Add(new Border { Height = 6 });
        foreach (var rk in new[] { RowKind.Close, RowKind.Ranged, RowKind.Siege }) root.Children.Add(BoardRow(Me, rk));

        root.Children.Add(HandStrip());
        _centerBox.Child = root;
    }

    private bool RowHighlighted(int side, RowKind rk)
    {
        if (_selected == null || !MyTurn) return false;
        var d = _selected.Def;
        if (d.IsUnit)
        {
            if (d.IsAgile) return side == Me && rk is RowKind.Close or RowKind.Ranged && side == Me;
            var target = d.Has("spy") ? Opp : Me;
            return side == target && rk == d.FixedRow;
        }
        if (d.Id is Ids.Horn or Ids.Mardroeme) return side == Me && G.Players[Me].Row(rk).Special == null;
        return false;
    }

    private UIElement BoardRow(int side, RowKind rk)
    {
        var ps = G.Players[side];
        var row = ps.Row(rk);
        var highlighted = RowHighlighted(side, rk);

        var bg = side == Me ? Theme.RowMine : Theme.RowOpp;
        var grid = new Grid { Height = 106, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var frame = new Border
        {
            Background = bg,
            BorderBrush = highlighted ? Theme.Gold : Theme.PanelEdge,
            BorderThickness = new Thickness(highlighted ? 3 : 1),
            CornerRadius = new CornerRadius(6)
        };
        SetColumnSpan(frame, 3);
        grid.Children.Add(frame);

        // Hava örtüsü
        if (G.WeatherActive(rk))
        {
            var (label, tint) = WeatherStyle(rk);
            var wx = new Border { Background = tint, CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
            SetColumnSpan(wx, 3);
            grid.Children.Add(wx);
            var wl = Ui.Text(label, 30, Theme.B("#33ffffff"), true);
            wl.HorizontalAlignment = HorizontalAlignment.Right;
            wl.VerticalAlignment = VerticalAlignment.Center;
            wl.Margin = new Thickness(0, 0, 18, 0);
            wl.IsHitTestVisible = false;
            SetColumn(wl, 2);
            grid.Children.Add(wl);
        }

        // Sıra puanı
        var score = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        score.Children.Add(Ui.Text(Texts.Row(rk), 10, Theme.TextDim, false, TextAlignment.Center));
        var total = G.RowTotal(side, rk);
        var scoreBadge = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = Theme.B("#18130c"),
            BorderBrush = Theme.GoldDim,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 3, 0, 0),
            Child = Ui.Text(total.ToString(), 18, Theme.Gold, true, TextAlignment.Center)
        };
        PulseIfChanged(scoreBadge, $"r{side}:{(int)rk}", total);
        score.Children.Add(scoreBadge);
        ((TextBlock)((Border)score.Children[1]).Child).VerticalAlignment = VerticalAlignment.Center;
        SetColumn(score, 0);
        grid.Children.Add(score);

        // Boynuz / Mardroeme yuvası
        var slot = new Border
        {
            Margin = new Thickness(4, 6, 4, 6),
            CornerRadius = new CornerRadius(5),
            BorderBrush = highlighted && _selected?.Def.Id is Ids.Horn or Ids.Mardroeme ? Theme.Gold : Theme.PanelEdge,
            BorderThickness = new Thickness(1.5),
            Background = Theme.B("#20ffffff"),
            Width = 72
        };
        if (row.Special != null)
            slot.Child = new Viewbox { Child = MakeCard(row.Special, CardSize.Board, onBoard: false) };
        else
            slot.Child = Ui.Text("Boynuz", 10, Theme.B("#55e8dcc4"), false, TextAlignment.Center);
        if (row.Special == null) ((TextBlock)slot.Child).VerticalAlignment = VerticalAlignment.Center;
        SetColumn(slot, 1);
        grid.Children.Add(slot);

        // Kartlar
        var cards = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        const double unit = 68 + 6;
        const double avail = 1000 - 82 - 86 - 24;
        var n = row.Cards.Count;
        var overlap = n * unit > avail && n > 1 ? (n * unit - avail) / (n - 1) : 0;
        for (var i = 0; i < n; i++)
        {
            var c = row.Cards[i];
            var isDecoyTarget = _selected?.Def.Id == Ids.Decoy && side == Me && MyTurn && c.Def.IsUnit && !c.Def.Hero;
            var cc = MakeCard(c, CardSize.Board, onBoard: true, highlight: isDecoyTarget);
            if (_effects && !_firstDraw && !_prevBoard.Contains(c.Uid)) Fx.CardIn(cc, side == Me ? 90 : -90);
            if (isDecoyTarget) cc.Tag = "decoy-target";
            if (i > 0) cc.Margin = new Thickness(cc.Margin.Left - overlap, cc.Margin.Top, cc.Margin.Right, cc.Margin.Bottom);
            var captured = c;
            cc.MouseLeftButtonUp += (_, e) =>
            {
                if (isDecoyTarget) { e.Handled = true; DoPlay(_selected!, null, captured.Uid); }
            };
            cards.Children.Add(cc);
        }
        SetColumn(cards, 2);
        grid.Children.Add(cards);

        if (highlighted)
        {
            frame.Cursor = Cursors.Hand;
            grid.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnRowClicked(side, rk); };
            grid.Background = Brushes.Transparent;
            grid.Tag = "row-hl";
        }
        return grid;
    }

    private static (string, Brush) WeatherStyle(RowKind rk) => rk switch
    {
        RowKind.Close => ("DON", Theme.B("#553a5f8a")),
        RowKind.Ranged => ("SİS", Theme.B("#55808890")),
        _ => ("YAĞMUR", Theme.B("#55294f78")),
    };

    private UIElement HandStrip()
    {
        var hand = G.Players[Me].Hand;
        var box = new Border
        {
            Height = 158,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.B("#1a1410"),
            CornerRadius = new CornerRadius(8),
            BorderBrush = Theme.PanelEdge,
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        const double unit = 98 + 6;
        const double avail = 960;
        var n = hand.Count;
        var overlap = n * unit > avail && n > 1 ? (n * unit - avail) / (n - 1) : 0;
        for (var i = 0; i < n; i++)
        {
            var c = hand[i];
            var sel = _selected == c;
            var cc = MakeCard(c, CardSize.Hand, onBoard: false, selected: sel, dimmed: !MyTurn);
            cc.Tag = "hand";
            if (_effects && MyTurn) Fx.HoverLift(cc);
            if (i > 0) cc.Margin = new Thickness(cc.Margin.Left - overlap, cc.Margin.Top, cc.Margin.Right, cc.Margin.Bottom);
            var captured = c;
            cc.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!MyTurn) return;
                _selected = _selected == captured ? null : captured;
                _hint = _selected == null ? "" : HintFor(_selected);
                Refresh();
            };
            sp.Children.Add(cc);
        }
        if (n == 0) sp.Children.Add(Ui.Text("Elinde kart kalmadı", 14, Theme.TextDim));
        box.Child = sp;
        return box;
    }

    private static string HintFor(CardInstance c)
    {
        var d = c.Def;
        if (d.IsUnit)
        {
            if (d.IsAgile) return "Yakın dövüş veya Menzilli sırasını seç.";
            if (d.Has("spy")) return "Casus rakibin sahasına konur ve 2 kart çekersin. “Oyna”ya bas ya da vurgulu sırayı seç.";
            return "“Oyna”ya bas ya da vurgulu sırayı seç.";
        }
        if (d.Id is Ids.Horn) return "Güçlendireceğin sırayı seç.";
        if (d.Id is Ids.Mardroeme) return "Mardroeme'yi koyacağın sırayı seç.";
        if (d.Id == Ids.Decoy) return "Geri alacağın birimi kendi sahandan seç.";
        return "“Oyna”ya bas.";
    }

    private static bool NeedsNoTarget(CardDef d) =>
        d.IsUnit ? !d.IsAgile : d.IsWeather || d.Id == Ids.Scorch;

    // ---------- Sağ panel ----------

    private void BuildRight()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Hava alanı
        var wsp = new StackPanel();
        wsp.Children.Add(Ui.Text("HAVA", 11, Theme.TextDim, true));
        var wrap = new WrapPanel { MinHeight = 100 };
        foreach (var w in G.WeatherCards) wrap.Children.Add(MakeCard(w, CardSize.Board, onBoard: false));
        if (G.WeatherCards.Count == 0) wrap.Children.Add(Ui.Text("Açık", 13, Theme.TextDim));
        wsp.Children.Add(wrap);
        var weatherCard = Ui.Card(wsp);
        var weatherTarget = _selected?.Def.IsWeather == true && MyTurn;
        if (weatherTarget)
        {
            weatherCard.BorderBrush = Theme.Gold;
            weatherCard.BorderThickness = new Thickness(3);
            weatherCard.Cursor = Cursors.Hand;
            weatherCard.MouseLeftButtonUp += (_, e) => { e.Handled = true; DoPlay(_selected!); };
            weatherCard.Tag = "weather-hl";
        }
        SetRow(weatherCard, 0);
        grid.Children.Add(weatherCard);

        // Durum ve eylem
        var status = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        status.Children.Add(Ui.Text($"Raund {G.Round}", 16, Theme.Gold, true));
        status.Children.Add(Ui.Text(StatusText(), 13, Theme.Text, false, TextAlignment.Left, true));
        var snd = Ui.Button(Sfx.Enabled ? "Ses: açık" : "Ses: kapalı", () => { Sfx.Enabled = !Sfx.Enabled; Sfx.SaveSettings(); Sfx.Play("click"); Refresh(); });
        snd.Padding = new Thickness(10, 2, 10, 2);
        snd.FontSize = 11;
        snd.HorizontalAlignment = HorizontalAlignment.Left;
        snd.Margin = new Thickness(0, 6, 0, 2);
        status.Children.Add(snd);
        if (!string.IsNullOrEmpty(_hint)) status.Children.Add(Ui.Text(_hint, 12, Theme.TextDim, false, TextAlignment.Left, true));
        if (_selected != null && MyTurn && NeedsNoTarget(_selected.Def))
        {
            var play = Ui.Button($"Oyna: {_selected.Def.Name}", () => DoPlay(_selected!), primary: true);
            play.Margin = new Thickness(0, 8, 0, 0);
            status.Children.Add(play);
        }
        SetRow(status, 1);
        grid.Children.Add(status);

        // Günlük
        var logSp = new StackPanel();
        foreach (var line in _s.Log.TakeLast(80))
        {
            var isHeader = line.StartsWith("—") || line.StartsWith("Raund bitti") || line.StartsWith("Oyun");
            logSp.Children.Add(Ui.Text(line, 11.5, isHeader ? Theme.Gold : line.StartsWith("Rakip") ? Theme.B("#d9a59a") : Theme.Text, isHeader, TextAlignment.Left, true));
            ((TextBlock)logSp.Children[^1]).Margin = new Thickness(0, 1, 0, 1);
        }
        _logScroll = new ScrollViewer { Content = logSp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var logCard = Ui.Card(_logScroll, pad: 6);
        SetRow(logCard, 2);
        grid.Children.Add(logCard);

        _right.Child = grid;
    }

    private string StatusText()
    {
        if (_s.IsOver) return "Oyun bitti.";
        if (G.Phase == Phase.Mulligan) return "Kartlarını değiştir.";
        if (G.Pending is { } p) return p.Player == Me ? "Bir seçim yapman gerekiyor." : "Rakip seçim yapıyor…";
        if (G.Players[Me].Passed) return "Pas geçtin. Rakip oynuyor…";
        if (_aiRunning || G.Current == Opp) return "Rakip düşünüyor…";
        return "Senin sıran.";
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

    private async void KickAi()
    {
        if (_aiRunning) return;
        if (!_s.AiToAct) return;
        _aiRunning = true;
        try
        {
            Refresh();
            while (!_s.IsOver && _s.AiToAct)
            {
                if (_animate)
                {
                    await Task.Delay(G.Phase == Phase.Playing ? 850 : 350);
                    if (!IsLoaded) return;
                }
                _s.AiStep();
                Refresh();
                // Raund bitince banner okunsun
                if (_animate && _pauseAfterMs > 0) { await Task.Delay(_pauseAfterMs); _pauseAfterMs = 0; }
            }
        }
        catch (Exception ex)
        {
            // Yapay zekâ hatası oyunu çökertmesin: kullanıcıya göster, elle devam edilebilsin.
            _hint = "Rakip hata verdi: " + ex.Message;
            _s.Log.Add("⚠ " + _hint);
        }
        finally
        {
            _aiRunning = false;
            Refresh();
        }
    }

    // ---------- Ses, banner ve animasyon ----------

    private void PulseIfChanged(FrameworkElement el, string key, int value)
    {
        _curTotals[key] = value;
        if (_effects && !_firstDraw && _prevTotals.TryGetValue(key, out var old) && old != value) Fx.Pulse(el);
    }

    private void HandleEvents(IReadOnlyList<GameEvent> evs)
    {
        if (evs.Count == 0) return;
        var sounds = new List<string>();
        string? title = null, sub = null;
        Brush color = Theme.Gold;
        var hold = 800;

        foreach (var e in evs)
        {
            switch (e.Kind)
            {
                case "play":
                {
                    var d = G.Catalog.Get(e.Detail);
                    sounds.Add(d.IsWeather ? "weather" : d.Id == Ids.Horn ? "horn" : d.Id == Ids.Scorch ? "scorch" : d.Has("spy") ? "spy" : "place");
                    break;
                }
                case "place":
                    // Elden oynanan kartın "place" olayı zaten "play" sesini aldı
                    if (!evs.Any(x => x.Kind == "play" && x.CardUid == e.CardUid)) sounds.Add("place");
                    break;
                case "muster": sounds.Add("muster"); break;
                case "draw": sounds.Add("draw"); break;
                case "kill": sounds.Add("scorch"); break;
                case "weather": sounds.Add("weather"); break;
                case "transform": sounds.Add("horn"); break;
                case "spawn":
                case "decoy": sounds.Add("place"); break;
                case "coin_toss":
                case "first_chosen": sounds.Add("coin"); break;
                case "pass":
                    sounds.Add("pass");
                    if (title == null) { title = e.Player == Me ? "Pas geçtin" : "Rakip pas geçti"; color = Theme.TextDim; hold = 650; }
                    break;
                case "leader":
                    sounds.Add("leader");
                    title = $"{Texts.Who(e.Player)}: lider yeteneği";
                    sub = _s.Game.Catalog.Get(e.Detail).Name;
                    color = Theme.Gold;
                    hold = 1000;
                    break;
                case "round_end":
                {
                    var parts = e.Detail.Split(':');
                    if (e.Player == Me) { title = "Raundu kazandın!"; color = Theme.Good; sounds.Add("round-win"); }
                    else if (e.Player == Opp) { title = "Raundu rakip kazandı"; color = Theme.Bad; sounds.Add("round-lose"); }
                    else { title = "Raund berabere"; color = Theme.Gold; sounds.Add("pass"); }
                    sub = $"Sen {parts[0]}  –  Rakip {parts[1]}";
                    hold = 1400;
                    _pauseAfterMs = 1900;
                    break;
                }
                case "round_start":
                    if (G.Round > 1)
                    {
                        var starter = $"Raund {G.Round}: {(e.Player == Me ? "sen başlıyorsun" : "rakip başlıyor")}";
                        if (title == null) { title = starter; color = Theme.Gold; hold = 900; }
                        else sub = (sub == null ? "" : sub + "\n") + starter;
                    }
                    break;
                case "game_end":
                    sounds.Add(e.Player == Me ? "win" : e.Player == Opp ? "lose" : "pass");
                    break;
            }
        }

        foreach (var snd in sounds.Distinct()) Sfx.Play(snd);
        if (title != null && !_s.IsOver) ShowBanner(title, sub, color, hold);
    }

    private void ShowBanner(string title, string? sub, Brush color, int holdMs)
    {
        _bannerHost.Children.Clear();
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(Ui.Text(title, 38, color, true, TextAlignment.Center));
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(Ui.Text(sub, 16, Theme.Text, false, TextAlignment.Center));
        var b = new Border
        {
            Background = Theme.B("#e0120d09"),
            BorderBrush = color,
            BorderThickness = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(60, 16, 60, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
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

        if (_s.IsOver) content = GameOverPanel();
        else if (G.Phase == Phase.Mulligan && G.PlayersToAct().Contains(Me)) content = MulliganPanel();
        else if (G.Phase == Phase.ChooseFirst && G.Current == Me) content = ChooseFirstPanel();
        else if (G.Pending is { } pc && pc.Player == Me && !_aiRunning) content = ChoicePanel(pc);
        else _lastPending = null;

        if (content == null)
        {
            _overlay.Visibility = Visibility.Collapsed;
            return;
        }
        _overlay.Background = Theme.B("#cc0b0805");
        _overlay.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Theme.B("#211a13"),
            BorderBrush = Theme.GoldDim,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(26),
            MaxWidth = 1000,
            Child = content
        });
        _overlay.Visibility = Visibility.Visible;
    }

    private UIElement MulliganPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Kart değiştir", 24, Theme.Gold, true, TextAlignment.Center));
        sp.Children.Add(Ui.Text("İstediğin en çok 2 kartı seç; destene karışıp yerine yeni kart çekersin.", 13, Theme.TextDim, false, TextAlignment.Center));
        sp.Children[1].SetValue(MarginProperty, new Thickness(0, 4, 0, 14));

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 900 };
        foreach (var c in G.Players[Me].Hand)
        {
            var selected = _mulliganSel.Contains(c.Uid);
            var cc = MakeCard(c, CardSize.Hand, onBoard: false, selected: selected);
            cc.Tag = "mulligan";
            var uid = c.Uid;
            cc.MouseLeftButtonUp += (_, _) =>
            {
                if (!_mulliganSel.Remove(uid) && _mulliganSel.Count < Game.MaxMulligans) _mulliganSel.Add(uid);
                Refresh();
            };
            wrap.Children.Add(cc);
        }
        sp.Children.Add(wrap);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var swap = Ui.Button(_mulliganSel.Count == 0 ? "Değiştirmeden başla" : $"{_mulliganSel.Count} kartı değiştir", () =>
        {
            var r = _s.Apply(new Mulligan(Me, _mulliganSel.ToList()));
            _mulliganSel.Clear();
            if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
            AfterHumanAction();
        }, primary: true);
        buttons.Children.Add(swap);
        sp.Children.Add(buttons);
        return sp;
    }

    private UIElement ChooseFirstPanel()
    {
        var sp = new StackPanel { MinWidth = 420 };
        sp.Children.Add(Ui.Text("Scoia'tael yeteneği", 24, Theme.Gold, true, TextAlignment.Center));
        sp.Children.Add(Ui.Text("İlk turu kimin oynayacağını sen seçersin.", 14, Theme.TextDim, false, TextAlignment.Center));
        sp.Children[1].SetValue(MarginProperty, new Thickness(0, 4, 0, 16));
        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var me = Ui.Button("Ben başlayayım", () => { _s.Apply(new ChooseFirst(Me, Me)); AfterHumanAction(); }, primary: true);
        var opp = Ui.Button("Rakip başlasın", () => { _s.Apply(new ChooseFirst(Me, Opp)); AfterHumanAction(); });
        opp.Margin = new Thickness(12, 0, 0, 0);
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

        var sp = new StackPanel { MinWidth = 520 };
        sp.Children.Add(Ui.Text(pc.Prompt, 20, Theme.Gold, true, TextAlignment.Center));
        sp.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 14));

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 900 };
        foreach (var uid in pc.Candidates)
        {
            var c = FindCard(uid);
            if (c == null) continue;
            var selected = _choiceSel.Contains(uid);
            var cc = MakeCard(c, CardSize.Hand, onBoard: false, selected: selected);
            cc.Tag = "choice";
            var u = uid;
            if (pc.Kind != ChoiceKind.AgileRow)
                cc.MouseLeftButtonUp += (_, _) =>
                {
                    if (!_choiceSel.Remove(u))
                    {
                        if (pc.Max == 1) _choiceSel.Clear();
                        if (_choiceSel.Count < pc.Max) _choiceSel.Add(u);
                    }
                    _choiceRow = null;
                    Refresh();
                };
            wrap.Children.Add(cc);
        }
        sp.Children.Add(wrap);

        // Çevik birim için sıra
        var pickedAgile = _choiceSel.Count == 1 && FindCard(_choiceSel[0])?.Def.IsAgile == true;
        var needRow = pc.Kind == ChoiceKind.AgileRow || (pc.Kind == ChoiceKind.Medic && pickedAgile);
        if (needRow)
        {
            sp.Children.Add(Ui.Text("Hangi sıraya konsun?", 13, Theme.TextDim, false, TextAlignment.Center));
            sp.Children[^1].SetValue(MarginProperty, new Thickness(0, 12, 0, 4));
            var rows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var rk in new[] { RowKind.Close, RowKind.Ranged })
            {
                var r = rk;
                var tb = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"),
                    Content = Ui.Text(Texts.Row(rk), 14, Theme.Text, true, TextAlignment.Center),
                    IsChecked = _choiceRow == rk,
                    Margin = new Thickness(4),
                    MinWidth = 150
                };
                tb.Click += (_, _) => { _choiceRow = r; Refresh(); };
                rows.Children.Add(tb);
            }
            sp.Children.Add(rows);
        }

        var ready = _choiceSel.Count >= pc.Min && _choiceSel.Count <= pc.Max && (!needRow || _choiceRow != null);
        var label = pc.Max > 1 ? $"Onayla ({_choiceSel.Count}/{pc.Max})" : "Onayla";
        var confirm = Ui.Button(label, () =>
        {
            var r = _s.Apply(new Choose(Me, _choiceSel.ToList(), _choiceRow));
            if (!r.Ok) { _hint = r.Error ?? ""; Refresh(); return; }
            _lastPending = null;
            AfterHumanAction();
        }, primary: true, enabled: ready);
        confirm.Margin = new Thickness(0, 18, 0, 0);
        confirm.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(confirm);
        return sp;
    }

    private UIElement GameOverPanel()
    {
        var sp = new StackPanel { MinWidth = 440 };
        var winner = G.Outcome!.Winner;
        var (title, color) = winner == Me ? ("Kazandın!", Theme.Good) : winner == null ? ("Berabere", Theme.Gold) : ("Kaybettin", Theme.Bad);
        sp.Children.Add(Ui.Text(title, 40, color, true, TextAlignment.Center));
        sp.Children.Add(new Border { Height = 12 });
        foreach (var r in G.History)
        {
            var who = r.Winner == null ? "berabere" : r.Winner == Me ? "sen kazandın" : "rakip kazandı";
            sp.Children.Add(Ui.Text($"Raund {r.Round}:  Sen {r.Score0} – Rakip {r.Score1}  ({who})", 14, Theme.Text, false, TextAlignment.Center));
        }
        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        var again = Ui.Button("Yeniden oyna", () => _rematch(_s.Config with { Seed = Random.Shared.Next(1, int.MaxValue) }), primary: true);
        var menu = Ui.Button("Menü", _toMenu);
        menu.Margin = new Thickness(12, 0, 0, 0);
        b.Children.Add(again);
        b.Children.Add(menu);
        sp.Children.Add(b);
        return sp;
    }
}
