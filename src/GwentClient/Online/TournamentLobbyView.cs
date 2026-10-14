using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

    public TournamentLobbyView(OnlineController controller, Func<Task> leave)
    {
        _c = controller;
        _leave = leave;
        Children.Add(Ornate.Stage(_host));
        _picker = new LoadoutPicker(() => St?.NextGen == true);
        _picker.Changed += SendLoadout;
        _c.Link.TournamentChanged += OnState;
        Unloaded += (_, _) => _c.Link.TournamentChanged -= OnState;
        Rebuild();
        SendLoadout();   // varsayılan seçimle hemen hazır olunabilsin
    }

    private TournamentState? St => _c.Link.TState;
    private void OnState() { EnsureFee(); Rebuild(); }

    /// <summary>Katılım ücreti bir kez düşer; bakiye yetmiyorsa turnuvadan ayrılınır.</summary>
    private void EnsureFee()
    {
        if (_feeChecked || St is not { } st) return;
        _feeChecked = true;
        if (!TournamentEconomy.TryPayEntry(st.Code, st.EntryFee))
        {
            _error = $"Katılım ücreti {st.EntryFee} taç; bakiyen {Settings.Crowns}. Turnuvadan ayrılıyorsun.";
            Dispatcher.BeginInvoke(new Action(LeaveClicked));
        }
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
        var sub = st == null ? "" : $"{st.MaxPlayers} kişilik tek eleme · " + (fee > 0 ? $"katılım {fee} taç · ödül havuzu {fee * st.Players.Count} taç" : "ücretsiz");
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
        (_picker.Parent as Panel)?.Children.Remove(_picker);
        _picker.Build();
        root.Children.Add(_picker);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
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
