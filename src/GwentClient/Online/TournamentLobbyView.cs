using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
using GwentCore;
using GwentNet;

namespace GwentClient;

/// <summary>
/// Turnuva bekleme odası: oyuncular (portre, ad, hazır mührü), benim fraksiyon/deste/liderim, katılım ücreti ve ödül havuzu.
/// Herkes hazır olunca kuran "Turnuvayı başlat" der; başlayınca <c>MainWindow</c> bracket ekranına geçer. Deste turnuva boyunca kilitlidir.
/// </summary>
public sealed class TournamentLobbyView : Grid
{
    private readonly OnlineController _c;
    private readonly Func<Task> _leave;
    private readonly Border _host = new();
    private readonly LoadoutPicker _picker;
    private bool _revealed, _leaving, _feeChecked;
    private bool _meReadyShown;
    private string _error = "";
    private readonly LoadoutPicker _rulePicker;                  // host: sabit deste için seçilen deste
    private TournamentRuleKind _ruleKind = TournamentRuleKind.Free;
    private string _ruleFaction = "northern_realms";
    private bool _specHands = true;
    private bool _rulesSynced;
    private TournamentRules? _lastRules;

    public TournamentLobbyView(OnlineController controller, Func<Task> leave)
    {
        _c = controller;
        _leave = leave;
        Children.Add(Ornate.Stage(_host));
        _picker = new LoadoutPicker(() => St?.NextGen == true);
        _picker.Changed += SendLoadout;
        _rulePicker = new LoadoutPicker(() => St?.NextGen == true);
        _rulePicker.Changed += SendRules;
        _c.Link.TournamentChanged += OnState;
        Unloaded += (_, _) => _c.Link.TournamentChanged -= OnState;
        SyncRules();
        Rebuild();
        SendLoadout();   // varsayılan seçimle hemen hazır olunabilsin
    }

    private TournamentState? St => _c.Link.TState;
    private void OnState()
    {
        EnsureFee();
        SyncRules();
        Rebuild();
    }

    /// <summary>Sunucudaki kurallar değişince yerel seçimi uyarlar; fraksiyon kuralında seçimi silinen yeniden gönderir.</summary>
    private void SyncRules()
    {
        if (St?.Rules is not { } r) return;
        if (!_rulesSynced || !_c.IsHost)
        {
            _ruleKind = r.Kind;
            if (r.Faction != null) _ruleFaction = r.Faction;
            _specHands = r.SpectatorHands;
            _rulesSynced = true;
        }
        var changed = _lastRules == null || _lastRules.Kind != r.Kind || _lastRules.Faction != r.Faction;
        _lastRules = r;
        _picker.LockedFaction = r.Kind == TournamentRuleKind.Faction ? r.Faction : null;
        if (changed && r.Kind == TournamentRuleKind.Faction && Me?.Loadout == null) { _picker.Build(); SendLoadout(); }   // seçimi silinen oyuncu kilitli fraksiyonla yeniden gönderir
    }

    private async void SendRules()
    {
        var rules = _ruleKind switch
        {
            TournamentRuleKind.Faction => new TournamentRules(TournamentRuleKind.Faction, _ruleFaction, null, _specHands),
            TournamentRuleKind.FixedDeck => new TournamentRules(TournamentRuleKind.FixedDeck, null, _rulePicker.ToLoadout(), _specHands),
            _ => new TournamentRules(TournamentRuleKind.Free, null, null, _specHands)
        };
        var r = await _c.Link.SetTournamentRulesAsync(rules);
        _error = r.Ok ? "" : r.Error ?? "Kurallar gönderilemedi.";
        Rebuild();
    }

    /// <summary>Katılım ücreti bir kez düşer; bakiye yetmezse eldeki kadar (turnuvaya girişi engellemez).</summary>
    private void EnsureFee()
    {
        if (_feeChecked || St is not { } st) return;
        _feeChecked = true;
        TournamentEconomy.PayEntry(st.Code, st.EntryFee);
    }

    private async void SendLoadout()
    {
        var r = await _c.Link.SetTournamentLoadoutAsync(_picker.ToLoadout());
        _error = r.Ok ? "" : r.Error ?? "Seçim gönderilemedi.";
        Rebuild();
    }

    private async void ToggleReady(bool ready)
    {
        var r = await _c.Link.TournamentReadyAsync(ready);
        if (!r.Ok) { _error = r.Error ?? ""; Rebuild(); }
    }

    private async void StartClicked()
    {
        var r = await _c.Link.StartTournamentAsync();
        if (!r.Ok) { _error = r.Error ?? ""; Rebuild(); }
    }

    private async void LeaveClicked()
    {
        if (_leaving) return;
        _leaving = true;
        await _leave();
    }

    private static string RuleTitle(TournamentRules? r) => r?.Kind switch
    {
        TournamentRuleKind.Faction => "Tek fraksiyon: " + Texts.FactionShort(r.Faction ?? ""),
        TournamentRuleKind.FixedDeck => "Sabit deste: " + Texts.FactionShort(r.Deck?.Faction ?? ""),
        _ => "Kuralsız"
    };

    private TPlayerInfo? Me => St is { } s && s.YourIndex >= 0 && s.YourIndex < s.Players.Count ? s.Players[s.YourIndex] : null;

    private void Rebuild()
    {
        var st = St;
        var grid = new Grid { Margin = new Thickness(70, 46, 70, 46) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- Üst ---
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fee = st?.EntryFee ?? 0;
        var sub = st == null ? "" : $"{st.MaxPlayers} kişilik tek eleme · " + (fee > 0 ? $"katılım {fee} taç · ödül havuzu {fee * st.Players.Count} taç" : "ücretsiz") + " · " + RuleTitle(st.Rules);
        head.Children.Add(Ornate.Heading("Turnuva", sub, 46));
        var code = LobbyParts.CodePanel(st?.Code, _c, "TURNUVA KODU");
        Grid.SetColumn(code, 1);
        head.Children.Add(code);
        grid.Children.Add(head);

        // --- Orta: ben | oyuncular ---
        var body = new Grid { Margin = new Thickness(0, 26, 0, 22) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        var mine = Ornate.Panel(MyPanel(), new Thickness(40, 30, 34, 30), 20, 58);
        body.Children.Add(mine);
        var roster = Ornate.Panel(Roster(), new Thickness(36, 28, 36, 28), 20, 58);
        Grid.SetColumn(roster, 2);
        body.Children.Add(roster);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // --- Alt ---
        var foot = new StackPanel { Orientation = Orientation.Horizontal };
        var ready = Me?.Ready == true;
        var readyBtn = Ui.Button(ready ? "Hazır değilim" : "Hazırım", () => ToggleReady(!ready), primary: !ready, enabled: Me?.Loadout != null);
        readyBtn.FontSize = 26;
        readyBtn.Padding = new Thickness(64, 12, 64, 12);
        readyBtn.Tag = "tlobby-ready";
        foot.Children.Add(readyBtn);
        if (_c.IsHost)
        {
            var canStart = st != null && st.Players.Count >= 3 && st.Players.All(p => p.Ready);
            var startBtn = Ui.Button("Turnuvayı başlat", StartClicked, primary: canStart, enabled: canStart);
            startBtn.FontSize = 22;
            startBtn.Padding = new Thickness(36, 12, 36, 12);
            startBtn.Margin = new Thickness(16, 0, 0, 0);
            startBtn.Tag = "tlobby-start";
            foot.Children.Add(startBtn);
        }
        var leaveBtn = Ui.Button("Turnuvadan ayrıl", LeaveClicked);
        leaveBtn.FontSize = 20;
        leaveBtn.Padding = new Thickness(30, 12, 30, 12);
        leaveBtn.Margin = new Thickness(16, 0, 0, 0);
        foot.Children.Add(leaveBtn);
        var msg = Ui.Text(_error != "" ? _error : FootMessage(), 18, _error != "" ? Theme.Bad : Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
        msg.VerticalAlignment = VerticalAlignment.Center;
        msg.Margin = new Thickness(26, 0, 0, 0);
        msg.MaxWidth = 620;
        foot.Children.Add(msg);
        Grid.SetRow(foot, 2);
        grid.Children.Add(foot);

        _host.Child = grid;
        if (!_revealed)
        {
            _revealed = true;
            Ornate.Reveal(new UIElement[] { head, mine, roster, foot }, 30, 60, 130, 620);
        }
    }

    private string FootMessage()
    {
        var st = St;
        if (st == null) return "Bağlanılıyor…";
        if (st.Players.Count < 3) return _c.IsHost ? "En az 3 oyuncu gerekir. Turnuva kodunu arkadaşlarına ver." : "Oyuncular bekleniyor…";
        if (_c.IsHost) return st.Players.All(p => p.Ready) ? "Herkes hazır. Turnuvayı başlatabilirsin." : "Herkesin hazır olması bekleniyor…";
        return Me?.Ready == true ? "Kurucunun turnuvayı başlatması bekleniyor…" : "Destemi seç ve hazır ol. Deste turnuva boyunca kilitli kalır.";
    }

    private UIElement MyPanel()
    {
        var ready = Me?.Ready == true;
        var stamp = ready && !_meReadyShown;
        _meReadyShown = ready;
        var root = new StackPanel();
        root.Children.Add(LobbyView.SeatHeader("SEN", Me?.Nick ?? _c.Nick, LobbyView.Seal(ready, stamp), Settings.Portrait));
        var rule = Ornate.Rule(560, 10);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(rule);
        if (_c.IsHost) root.Children.Add(RulesPanel());
        else if (St?.Rules is { Kind: not TournamentRuleKind.Free } rr) root.Children.Add(RuleNote(rr));
        if (St?.Rules is { Kind: TournamentRuleKind.FixedDeck } fixedRules) root.Children.Add(FixedDeckCard(fixedRules));
        else
        {
            (_picker.Parent as Panel)?.Children.Remove(_picker);
            _picker.Build();
            root.Children.Add(_picker);
        }
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
    }

    /// <summary>Host için kurallar bölümü: çeşit, fraksiyon/deste ve izleyici elleri.</summary>
    private UIElement RulesPanel()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(Ornate.Caption("Turnuva çeşidi", 6));
        var row = new WrapPanel();
        foreach (var (kind, title) in new[] { (TournamentRuleKind.Free, "Kuralsız"), (TournamentRuleKind.Faction, "Tek fraksiyon"), (TournamentRuleKind.FixedDeck, "Sabit deste") })
        {
            var k = kind;
            var b = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = Ui.Text(title, 17, Theme.Text, true, TextAlignment.Center), IsChecked = _ruleKind == k,
                Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(18, 9, 18, 9), MinWidth = 140, Tag = "trule:" + k
            };
            b.Click += (_, _) =>
            {
                if (_ruleKind == k) { b.IsChecked = true; return; }
                _ruleKind = k;
                SendRules();
            };
            row.Children.Add(b);
        }
        sp.Children.Add(row);
        var desc = _ruleKind switch
        {
            TournamentRuleKind.Faction => "Herkes aynı fraksiyonu oynar; deste ve lider serbest.",
            TournamentRuleKind.FixedDeck => "Herkes senin seçtiğin aynı desteyle oynar; değiştiremez.",
            _ => "Herkes istediği fraksiyon ve desteyle oynar."
        };
        var d = Ui.Text(desc, 14, Theme.TextDim, false, TextAlignment.Left, true);
        d.Margin = new Thickness(2, 0, 0, 6);
        sp.Children.Add(d);
        if (_ruleKind == TournamentRuleKind.Faction)
        {
            var fr = new WrapPanel();
            foreach (var f in new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" })
            {
                var key = f;
                var b = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), Content = Ui.Text(Texts.FactionShort(f), 15, Theme.Text, true, TextAlignment.Center), IsChecked = _ruleFaction == f,
                    Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 7, 14, 7), Tag = "trule-faction:" + f
                };
                b.Click += (_, _) => { _ruleFaction = key; SendRules(); };
                fr.Children.Add(b);
            }
            sp.Children.Add(fr);
        }
        else if (_ruleKind == TournamentRuleKind.FixedDeck)
        {
            sp.Children.Add(Ui.Text("Herkesin oynayacağı deste:", 15, Theme.B("#e8c970"), true));
            (_rulePicker.Parent as Panel)?.Children.Remove(_rulePicker);
            _rulePicker.Build();
            sp.Children.Add(_rulePicker);
        }
        var hands = Ornate.Option("İzleyiciler elleri görsün", _specHands ? "Açık: izleyen oyuncuların kartlarını da görür" : "Kapalı: izleyen yalnız masayı görür", _specHands);
        hands.Tag = "trule-hands";
        hands.Click += (_, _) => { _specHands = !_specHands; SendRules(); };
        sp.Children.Add(hands);
        var rule = Ornate.Rule(560, 8);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(rule);
        return sp;
    }

    /// <summary>Oyuncu için kural özeti.</summary>
    private static UIElement RuleNote(TournamentRules r)
    {
        var text = r.Kind == TournamentRuleKind.Faction
            ? $"Kural: bu turnuvada yalnız {Texts.Faction(r.Faction ?? "")} oynanır. Deste ve lideri sen seç."
            : $"Kural: bu turnuvada herkes aynı desteyle oynar ({Texts.Faction(r.Deck?.Faction ?? "")}).";
        var t = Ui.Text(text, 15, Theme.B("#e8c970"), false, TextAlignment.Left, true);
        t.Margin = new Thickness(0, 4, 0, 8);
        return t;
    }

    /// <summary>Sabit deste turnuvasında oyuncunun göreceği şey: lider kartı ve "deste kilitli" notu (seçim yok).</summary>
    private static UIElement FixedDeckCard(TournamentRules r)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (r.Deck is { } d && CardCatalog.Default.TryGet(d.LeaderId, out var leader))
            sp.Children.Add(new GwintCard(leader, 150, null, null, height: 150 * GwintCard.Aspect) { IsHitTestVisible = false, Margin = new Thickness(0, 0, 22, 0) });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 420 };
        info.Children.Add(Ui.Text(Ornate.Spaced("SABİT DESTE"), 13, Theme.B("#a8905a"), true));
        info.Children.Add(Ui.Text(Texts.Faction(r.Deck?.Faction ?? ""), 26, Theme.B("#f0d890"), true));
        info.Children.Add(Ui.Text(r.Deck?.CardIds is { } ids ? $"{ids.Count} kart" : "", 16, Theme.TextDim));
        var note = Ui.Text("Bu turnuvada herkes bu desteyle oynar; değiştirilemez. Hazır olman yeterli.", 16, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
        note.Margin = new Thickness(0, 10, 0, 0);
        info.Children.Add(note);
        sp.Children.Add(info);
        return sp;
    }

    /// <summary>Katılanlar ve boş yerler: portre, ad, kurucu işareti, hazır durumu.</summary>
    private UIElement Roster()
    {
        var st = St;
        var sp = new StackPanel();
        var head = Ui.Text(Ornate.Spaced("OYUNCULAR"), 13, Theme.B("#a8905a"), true);
        sp.Children.Add(head);
        sp.Children.Add(Ui.Text($"{st?.Players.Count ?? 0} / {st?.MaxPlayers ?? 0}", 30, Theme.B("#f0d890"), true));
        var rule = Ornate.Rule(440, 10);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(rule);
        var slots = st?.MaxPlayers ?? 4;
        for (var i = 0; i < slots; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4), Height = 62 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (st != null && i < st.Players.Count)
            {
                var p = st.Players[i];
                var medal = Portraits.Medallion(p.Avatar ?? p.Loadout?.Avatar ?? p.Loadout?.LeaderId, 50);
                medal.Margin = new Thickness(0, 0, 12, 0);
                row.Children.Add(medal);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nm = Ui.Text(p.Nick + (i == 0 ? "  ♛" : ""), 22, p.Connected ? Theme.B("#f0d890") : Theme.TextDim, true);
                texts.Children.Add(nm);
                texts.Children.Add(Ui.Text(i == st.YourIndex ? "sen" : p.Connected ? "bağlı" : "bağlantısı koptu", 13, Theme.TextDim));
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);
                var seal = LobbyView.Seal(p.Ready, false, 40);
                ((FrameworkElement)seal).VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(seal, 2);
                row.Children.Add(seal);
            }
            else
            {
                row.Children.Add(new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(25), BorderBrush = Theme.B("#4a3b28"), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 12, 0) });
                var empty = Ui.Text("boş yer", 18, Theme.B("#6a604c"));
                empty.FontStyle = FontStyles.Italic;
                empty.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(empty, 1);
                row.Children.Add(empty);
            }
            sp.Children.Add(row);
        }
        return sp;
    }
}
