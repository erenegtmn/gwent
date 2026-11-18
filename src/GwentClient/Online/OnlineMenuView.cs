using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>
/// Çevrimiçi giriş ekranı (tasarım: ARAYUZ.md). Üstte kimlik şeridi, solda "Odaya katıl" (tek kod kutusu), sağda "Oda kur"
/// (önce oyun türü, sonra o türün kısa ayarı, en sonda tek satırlık bağlantı özeti). Bağlanırken tam ekran perde.
/// </summary>
public sealed class OnlineMenuView : Grid
{
    /// <summary>Oda kurarken seçilen oyun türü.</summary>
    private enum Mode { Duel, Three, Tournament, Han }

    private readonly Action _back;
    private readonly Action<OnlineController> _connected;
    private readonly TextBox _nick = new() { MaxLength = 16, Tag = "online-nick" };
    private readonly TextBox _address = new() { Tag = "online-address" };
    private readonly TextBox _code = new() { MaxLength = 5, Tag = "online-code" };
    private readonly ToggleButton _nextGen;
    private readonly ToggleButton _lan;
    private readonly ToggleButton _tunnel;
    private readonly ToggleButton _upnp;
    private readonly TextBox _invite = new() { Tag = "online-invite", AcceptsReturn = false };
    private readonly TextBlock _status = new() { FontFamily = Theme.Serif, FontSize = 20 * Theme.FontScale, TextWrapping = TextWrapping.Wrap, MinHeight = 28, TextAlignment = TextAlignment.Center, Foreground = Theme.B("#e8c970"), MaxWidth = 460 };
    private readonly TextBlock _error = new() { FontFamily = Theme.Serif, FontSize = 18 * Theme.FontScale, TextWrapping = TextWrapping.Wrap, Foreground = Theme.B("#f0b4a4"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _errorBox = new() { Visibility = Visibility.Collapsed, Tag = "online-error", Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(18, 12, 18, 12), Background = Theme.B("#cc3a1612"), BorderBrush = Theme.B("#b8604f"), BorderThickness = new Thickness(1.5) };
    private readonly Grid _busyLayer = new() { Visibility = Visibility.Collapsed, Background = Theme.B("#e8000000"), Tag = "online-busy" };
    private readonly Button _create;
    private readonly Button _join;
    private ToggleButton _size4 = null!, _size8 = null!;
    private readonly List<ToggleButton> _feeButtons = new();
    private readonly Dictionary<Mode, Border> _modeCards = new();
    private readonly TextBlock _modeDesc = new() { FontFamily = Theme.Serif, FontSize = 18 * Theme.FontScale, Foreground = Theme.TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 10) };
    private readonly StackPanel _tourPanel = new() { Tag = "online-tournament-options" };
    private readonly TextBlock _connSummary = new() { FontFamily = Theme.Serif, FontSize = 18 * Theme.FontScale, VerticalAlignment = VerticalAlignment.Center };
    private Mode _mode = Mode.Duel;
    private bool _busy;

    private static readonly Color Teal = HubColors.Online;

    public OnlineMenuView(Action back, Action<OnlineController> connected)
    {
        _back = back;
        _connected = connected;

        foreach (var tb in new[] { _nick, _address, _code, _invite })
        {
            tb.Style = (Style)Application.Current.FindResource("GText");
            tb.FontFamily = Theme.Serif;
            tb.FontSize = 22;
        }
        _code.CharacterCasing = CharacterCasing.Upper;
        _nick.Text = Settings.Nick;
        _address.Text = Settings.LastAddress;
        _nick.Width = 300;
        _code.Width = 150;

        _nextGen = Ornate.Option("Yeni nesil içerik", "Roach, Geralt/Ciri Toplanma", false);
        _tunnel = Ornate.Option("İnternetten bağlanılsın", "Önerilir; arkadaşın hiçbir şey kurmaz", true);
        _lan = Ornate.Option("Yerel ağdakiler bağlanabilsin", "Aynı modemdeki arkadaşlar için", true);
        _upnp = Ornate.Option("Modemde port aç (UPnP)", "Gelişmiş; her modemde çalışmaz", false);
        foreach (var t in new[] { _tunnel, _lan, _upnp }) { t.Checked += (_, _) => SyncConnection(); t.Unchecked += (_, _) => SyncConnection(); }

        // ---- Kimlik şeridi: portre, takma ad, Crown ----
        var identity = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.Children.Add(Portraits.Medallion(Settings.Portrait, 66, glow: true));
        var nickBox = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nickBox.Children.Add(Ui.Text("Takma adın", 15, Theme.TextDim));
        _nick.Margin = new Thickness(0, 3, 0, 0);
        nickBox.Children.Add(_nick);
        Grid.SetColumn(nickBox, 1);
        identity.Children.Add(nickBox);
        var crowns = Ui.Text($"◆ {Settings.Crowns} Crown", 22, Theme.B("#e8c970"), true);
        crowns.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(crowns, 2);
        identity.Children.Add(crowns);
        ((FrameworkElement)identity.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
        nickBox.HorizontalAlignment = HorizontalAlignment.Left;

        // ---- Devam eden maç / turnuva bildirimi (varsa) ----
        var resume = new StackPanel();
        if (ResumeStore.LoadHost() is { } hostSave) resume.Children.Add(ResumeStrip(hostSave));
        if (ResumeStore.LoadHostTournament() is { } tsave) resume.Children.Add(TournamentResumeStrip(tsave));
        if (ResumeStore.LoadGuest() is { } guestSave) resume.Children.Add(GuestNote(guestSave));
        UIElement? resumeBox = null;
        if (resume.Children.Count > 0)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            g.Children.Add(new ChamferFrame { Fill = Theme.B("#e61a120a"), Edge = new SolidColorBrush(Teal), Inner = Ornate.GoldThin, Cut = 12, Inset = 5 });
            resume.Margin = new Thickness(26, 14, 26, 8);
            g.Children.Add(resume);
            resumeBox = g;
        }

        // ---- Sol: odaya katıl ----
        var join = new StackPanel();
        join.Children.Add(Head("Odaya katıl", "Arkadaşının sana verdiği davet kodunu yapıştır."));
        join.Children.Add(Label("Davet kodu"));
        var inviteRow = new Grid();
        inviteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inviteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var inviteHost = new Grid();
        inviteHost.Children.Add(_invite);
        var hint = new TextBlock { Text = "Kodu buraya yapıştır", FontFamily = Theme.Serif, FontSize = 20 * Theme.FontScale, Foreground = Theme.B("#6e6450"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        inviteHost.Children.Add(hint);
        _invite.TextChanged += (_, _) => hint.Visibility = _invite.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _invite.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; JoinClicked(); } };
        inviteRow.Children.Add(inviteHost);
        var paste = Ui.Button("Yapıştır", () =>
        {
            try { if (Clipboard.ContainsText()) _invite.Text = Clipboard.GetText().Trim(); } catch { }
            _invite.Focus();
        });
        paste.Margin = new Thickness(10, 0, 0, 0);
        paste.Padding = new Thickness(18, 9, 18, 9);
        paste.Tag = "online-paste";
        Grid.SetColumn(paste, 1);
        inviteRow.Children.Add(paste);
        join.Children.Add(inviteRow);

        var manual = new StackPanel { Visibility = Visibility.Collapsed, Tag = "online-manual", Margin = new Thickness(0, 6, 0, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var a = new StackPanel();
        a.Children.Add(Label("Host adresi (örn. 192.168.1.5:5000)"));
        a.Children.Add(_address);
        var c = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        c.Children.Add(Label("Oda kodu"));
        c.Children.Add(_code);
        Grid.SetColumn(c, 1);
        row.Children.Add(a);
        row.Children.Add(c);
        manual.Children.Add(row);
        var manualToggle = new TextBlock
        {
            Text = "Davet kodu yok mu? Adres ve oda kodunu elle gir ▾", FontFamily = Theme.Serif, FontSize = 16 * Theme.FontScale, Foreground = Theme.B("#a8905a"),
            Margin = new Thickness(0, 16, 0, 0), Cursor = Cursors.Hand
        };
        manualToggle.MouseLeftButtonUp += (_, _) =>
        {
            manual.Visibility = manual.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            manualToggle.Text = manual.Visibility == Visibility.Visible ? "Elle girişi gizle ▴" : "Davet kodu yok mu? Adres ve oda kodunu elle gir ▾";
        };
        join.Children.Add(manualToggle);
        join.Children.Add(manual);
        _join = Ui.Button("Katıl", () => JoinClicked(), primary: true);
        _join.FontSize = 24;
        _join.Padding = new Thickness(0, 12, 0, 12);
        _join.Margin = new Thickness(0, 26, 0, 0);
        _join.Tag = "online-join";
        join.Children.Add(_join);
        join.Children.Add(Ornate.Rule(380, 22));
        join.Children.Add(Label("Nasıl katılırım?"));
        join.Children.Add(Step("1", "Arkadaşın oda kurar; bekleme odasında davet kodu çıkar."));
        join.Children.Add(Step("2", "Kodu buraya yapıştırıp Katıl'a bas."));
        join.Children.Add(Step("3", "İkiniz de hazır deyince maç başlar."));

        // ---- Sağ: oda kur ----
        var host = new StackPanel();
        host.Children.Add(Head("Oda kur", "Oyun senin bilgisayarında sunucu olur; arkadaşların sana bağlanır."));
        host.Children.Add(Label("Oyun türü"));
        var modes = new UniformGrid { Rows = 1 };
        modes.Children.Add(ModeCard(Mode.Duel, "Düello", "Klasik, bire bir", "2"));
        modes.Children.Add(ModeCard(Mode.Three, "Üçlü", "Herkes herkese", "3"));
        modes.Children.Add(ModeCard(Mode.Tournament, "Turnuva", "Tek eleme, Crown ödüllü", "3–8"));
        modes.Children.Add(ModeCard(Mode.Han, "Han Ligi", "Kart topla, son ayakta kal", "2–8"));
        host.Children.Add(modes);

        host.Children.Add(_modeDesc);
        BuildTournamentOptions();
        host.Children.Add(_tourPanel);
        _nextGen.Margin = new Thickness(0, 4, 0, 0);
        host.Children.Add(_nextGen);

        host.Children.Add(Label("Bağlantı"));
        var connRow = new Grid();
        connRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        connRow.Children.Add(_connSummary);
        var connOptions = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0), Tag = "online-connection" };
        connOptions.Children.Add(_tunnel);
        connOptions.Children.Add(_lan);
        connOptions.Children.Add(_upnp);
        var change = Ui.Button("Değiştir ▾", () => { });
        change.Padding = new Thickness(16, 6, 16, 6);
        change.FontSize = 15;
        change.Click += (_, _) =>
        {
            var open = connOptions.Visibility != Visibility.Visible;
            connOptions.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            change.Content = open ? "Gizle ▴" : "Değiştir ▾";
        };
        Grid.SetColumn(change, 1);
        connRow.Children.Add(change);
        host.Children.Add(connRow);
        host.Children.Add(connOptions);

        _create = Ui.Button("Odayı aç", () => CreateClicked(), primary: true);
        _create.FontSize = 24;
        _create.Padding = new Thickness(0, 12, 0, 12);
        _create.Margin = new Thickness(0, 26, 0, 0);
        _create.Tag = "online-create";
        host.Children.Add(_create);

        var joinPanel = Ornate.Panel(join, new Thickness(36, 30, 36, 36), 20, 54);
        var hostPanel = Ornate.Panel(host, new Thickness(36, 30, 36, 36), 20, 54);

        var panels = new Grid();
        panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star), MinWidth = 360 });
        panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star), MinWidth = 560 });
        SetColumn(hostPanel, 1);
        joinPanel.Margin = new Thickness(0, 0, 14, 0);
        hostPanel.Margin = new Thickness(14, 0, 0, 0);
        panels.Children.Add(joinPanel);
        panels.Children.Add(hostPanel);

        _errorBox.Child = _error;

        var content = new StackPanel { MaxWidth = 1480, Margin = new Thickness(44, 14, 44, 20), VerticalAlignment = VerticalAlignment.Top };
        content.Children.Add(identity);
        if (resumeBox != null) content.Children.Add(resumeBox);
        content.Children.Add(panels);
        content.Children.Add(_errorBox);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch };

        var backdrop = new HubBackdrop(Ornate.Animate);
        backdrop.Show("map_skellige");
        var screen = HubKit.Screen(scroll, backdrop, new (string, Action?)[] { ("Çevrimiçi", null) }, () => { if (!_busy) _back(); }, null, hints: new[] { "Enter|Katıl (kod kutusunda)", "Esc|Geri" });

        // ---- Bağlanırken perde ----
        var card = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.Children.Add(Spinner());
        _status.Margin = new Thickness(0, 22, 0, 0);
        card.Children.Add(_status);
        _busyLayer.Children.Add(new Border { Child = card, Padding = new Thickness(70, 46, 70, 46), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Theme.B("#f01a130c"), BorderBrush = Theme.B("#8a7236"), BorderThickness = new Thickness(1.5) });
        screen.Children.Add(_busyLayer);

        Children.Add(screen);
        Ornate.Reveal(new UIElement[] { identity, joinPanel, hostPanel }, 28, 40, 120, 560);
        Focusable = true;
        UpdateMode();
        SyncConnection();
    }

    private static UIElement Spinner()
    {
        var spin = new RotateTransform(45, 20, 20);
        var outer = new System.Windows.Shapes.Rectangle { Width = 40, Height = 40, Stroke = Theme.B("#e0bf6a"), StrokeThickness = 3, RenderTransform = spin };
        var inner = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, Fill = Theme.B("#e0bf6a"), RenderTransform = new RotateTransform(45, 7, 7), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(new Canvas { Children = { outer } });
        Canvas.SetLeft(outer, 10);
        Canvas.SetTop(outer, 10);
        g.Children.Add(inner);
        if (Ornate.Animate) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(45, 405, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever });
        return g;
    }

    /// <summary>Oyun türü kartı: seçiliyken altın çerçeve ve parlak zemin.</summary>
    private Border ModeCard(Mode mode, string title, string desc, string players)
    {
        var sp = new StackPanel { IsHitTestVisible = false };
        var top = new Grid();
        top.Children.Add(Ui.Text(title, 24, Theme.B("#f4e6bf"), true));
        var chip = Ui.Text(players + " kişi", 13, Theme.B("#e8c970"), true);
        chip.HorizontalAlignment = HorizontalAlignment.Right;
        chip.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(chip);
        sp.Children.Add(top);
        var d = Ui.Text(desc, 15, Theme.TextDim, false, TextAlignment.Left, true);
        d.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(d);
        var b = new Border
        {
            Child = sp, Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Background = Theme.B("#661a130c"),
            BorderBrush = Theme.B("#55c9a24a"), BorderThickness = new Thickness(1.5), Tag = "online-mode-" + mode.ToString().ToLowerInvariant(), MinHeight = 104
        };
        b.MouseEnter += (_, _) => { if (_mode != mode) b.Background = Theme.B("#88271d12"); };
        b.MouseLeave += (_, _) => { if (_mode != mode) b.Background = Theme.B("#661a130c"); };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("cursor_movement"); _mode = mode; UpdateMode(); };
        _modeCards[mode] = b;
        return b;
    }

    private void BuildTournamentOptions()
    {
        _tourPanel.Children.Add(Label("Kişi sayısı"));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        _size4 = ChoiceButton("4 kişi", true);
        _size8 = ChoiceButton("8 kişi", false);
        foreach (var b in new[] { _size4, _size8 }) { b.Click += (_, _) => { _size4.IsChecked = ReferenceEquals(b, _size4); _size8.IsChecked = ReferenceEquals(b, _size8); }; sizeRow.Children.Add(b); }
        _tourPanel.Children.Add(sizeRow);
        _tourPanel.Children.Add(Label("Katılım ücreti"));
        var feeRow = new WrapPanel();
        foreach (var f in new[] { 0, 50, 100, 200 })
        {
            var fee = f;
            var b = ChoiceButton(fee == 0 ? "Ücretsiz" : $"{fee} Crown", fee == 0);
            b.Tag = fee;
            b.Click += (_, _) => { foreach (var o in _feeButtons) o.IsChecked = ReferenceEquals(o, b); };
            _feeButtons.Add(b);
            feeRow.Children.Add(b);
        }
        _tourPanel.Children.Add(feeRow);
        var note = Ui.Text($"Bakiyen {Settings.Crowns} Crown. Ücretli turnuvada havuz = ücret × oyuncu; şampiyon %55–70, finalist %25–30 alır. Crown yetmese de katılabilirsin.", 14, Theme.TextDim, false, TextAlignment.Left, true);
        note.Margin = new Thickness(0, 2, 0, 8);
        _tourPanel.Children.Add(note);
    }

    private void UpdateMode()
    {
        foreach (var (m, b) in _modeCards)
        {
            var on = m == _mode;
            b.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0xe8, 0xc9, 0x70)) : Theme.B("#55c9a24a");
            b.Background = on ? Theme.B("#cc3a2a14") : Theme.B("#661a130c");
            b.Effect = on ? new DropShadowEffect { Color = Color.FromRgb(0xe8, 0xc9, 0x70), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.35 } : null;
        }
        _modeDesc.Text = _mode switch
        {
            Mode.Duel => "İki oyuncu, klasik kurallar. Rakibin odaya katılınca ikiniz de hazır deyince maç başlar.",
            Mode.Three => "Üç oyuncu, herkes herkese. İlk iki raundu kazanan alır; sırayı zar belirler.",
            Mode.Tournament => "Tek eleme turnuvası: kişi sayısını ve katılım ücretini seç. Ödül havuzu Crown ile dağıtılır.",
            _ => "Pazardan kart topla, üç eşini birleştirip güçlendir, savaşta işaretlerle araya gir; canı biten elenir, son ayakta kalan kazanır. Boş koltukları güçlü botlar doldurur."
        };
        _tourPanel.Visibility = _mode == Mode.Tournament ? Visibility.Visible : Visibility.Collapsed;
        _nextGen.Visibility = _mode == Mode.Han ? Visibility.Collapsed : Visibility.Visible;
        _create.Content = _mode == Mode.Tournament ? "Turnuvayı kur" : "Odayı aç";
        _create.Tag = _mode == Mode.Tournament ? "online-tournament" : "online-create";
    }

    /// <summary>Bağlantı seçenekleri tek satırda özetlenir; hiçbiri açık değilse uyarılır.</summary>
    private void SyncConnection()
    {
        var parts = new List<string>();
        if (_tunnel.IsChecked == true) parts.Add("İnternet");
        if (_lan.IsChecked == true) parts.Add("yerel ağ");
        if (_upnp.IsChecked == true) parts.Add("UPnP");
        if (parts.Count == 0)
        {
            _connSummary.Text = "Hiçbiri seçili değil: kimse bağlanamaz.";
            _connSummary.Foreground = Theme.Bad;
        }
        else
        {
            var s = string.Join(" + ", parts);
            _connSummary.Text = char.ToUpper(s[0], new System.Globalization.CultureInfo("tr-TR")) + s[1..];
            _connSummary.Foreground = Theme.Text;
        }
    }

    private static ToggleButton ChoiceButton(string text, bool on) => new()
    {
        Style = (Style)Application.Current.FindResource("GToggle"), Content = Ui.Text(text, 17, Theme.Text, true, TextAlignment.Center), IsChecked = on,
        Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(18, 9, 18, 9), MinWidth = 96
    };

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(t, 16, Theme.B("#c9b27a"), true);
        l.Margin = new Thickness(0, 16, 0, 6);
        return l;
    }

    private static UIElement Head(string title, string sub)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var t = Ui.Text(title, 36, Theme.B("#f0d890"), true);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.9 };
        sp.Children.Add(t);
        var s = Ui.Text(sub, 16, Theme.TextDim, false, TextAlignment.Left, true);
        s.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(s);
        var rule = Ornate.Rule(420, 12);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(rule);
        return sp;
    }

    private static UIElement Step(string n, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Ui.Text(n, 20, Theme.B("#e0bf6a"), true));
        var t = Ui.Text(text, 16, Theme.TextDim, false, TextAlignment.Left, true);
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        return row;
    }

    private UIElement GuestNote(GuestResume save)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        box.Children.Add(Ui.Text($"Yarım kalan maçın var: {save.HostNick} ile, oda {save.RoomCode}.", 18, Theme.B("#e8c970"), true, TextAlignment.Left, true));
        box.Children.Add(Ui.Text("Oda kuran maçı yeniden açınca yeni davet kodunu aşağıya yapıştır; kaldığın yerden dönersin.", 15, Theme.TextDim, false, TextAlignment.Left, true));
        var del = Ui.Button("Bu kaydı sil", () => { ResumeStore.ClearGuest(); box.Visibility = Visibility.Collapsed; });
        del.FontSize = 14;
        del.HorizontalAlignment = HorizontalAlignment.Left;
        del.Margin = new Thickness(0, 8, 0, 0);
        box.Children.Add(del);
        return box;
    }

    // ---------- Eylemler ----------

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _create.IsEnabled = !busy;
        _join.IsEnabled = !busy;
        _busyLayer.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = message ?? "";
        if (busy) _errorBox.Visibility = Visibility.Collapsed;
    }

    private void Error(string message)
    {
        SetBusy(false);
        _error.Text = message;
        _errorBox.Visibility = Visibility.Visible;
    }

    private bool NickOk(out string nick)
    {
        nick = _nick.Text.Trim();
        if (nick.Length is < 2 or > 16) { Error("Takma ad 2-16 karakter olmalı."); return false; }
        return true;
    }

    /// <summary>Tünel istenmişse cloudflared yolunu bulur; yoksa yalnız kullanıcı izin verirse indirir. (yol, hata): hata doluysa işlem durur.</summary>
    private async Task<(string? Path, string? Error)> PrepareTunnelAsync()
    {
        if (_tunnel.IsChecked != true) return (null, null);
        var found = GwentHost.Cloudflared.FindInstalled();
        if (found != null) return (found, null);
        var size = await GwentHost.Cloudflared.GetDownloadSizeAsync();
        var sizeText = size is { } bytes ? $"yaklaşık {Math.Max(1, bytes / 1048576)} MB" : "boyutu bilinmiyor";
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"İnternetten bağlanabilmek için Cloudflare'in ücretsiz tünel programı (cloudflared, {sizeText}) indirilecek.\n\n" +
            $"Kaynak: {GwentHost.Cloudflared.Source}\nKonum: {System.IO.Path.GetDirectoryName(GwentHost.Cloudflared.InstallPath)}\n" +
            "İndirilen dosyanın Cloudflare imzası doğrulanır.\n\nİndirmeye izin veriyor musun?\n(Hayır dersen yalnız yerel ağdan bağlanılabilir.)",
            "Gwent — tünel programı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return (null, null);
        SetBusy(true, "cloudflared indiriliyor… %0");
        var progress = new Progress<double>(p => _status.Text = $"cloudflared indiriliyor… %{(int)(p * 100)}");
        var (path, derr) = await GwentHost.Cloudflared.DownloadAsync(progress);
        return path == null ? (null, "Tünel programı indirilemedi: " + derr) : (path, null);
    }

    /// <summary>Seçili oyun türüne göre oda ya da turnuva kurar.</summary>
    private async void CreateClicked()
    {
        if (_busy) return;
        if (_mode == Mode.Tournament) { TournamentClicked(); return; }
        if (!NickOk(out var nick)) return;
        SetBusy(true, "Sunucu başlatılıyor…");
        var (cloudflared, terr) = await PrepareTunnelAsync();
        if (terr != null) { Error(terr); return; }

        var opts = new HostOptions(_nextGen.IsChecked == true && _mode != Mode.Han, _lan.IsChecked == true, _upnp.IsChecked == true, cloudflared, _mode == Mode.Three ? 3 : 2);
        var statusProgress = new Progress<string>(m => _status.Text = m);
        var (c, err) = _mode == Mode.Han
            ? await OnlineController.HostHanAsync(nick, opts with { Players = 2 }, 8, statusProgress)
            : await OnlineController.HostAsync(nick, opts, statusProgress);
        if (c == null) { Error(err ?? "Oda kurulamadı."); return; }
        Settings.Nick = nick;
        Settings.Save();
        _connected(c);
    }

    /// <summary>Yarım kalan turnuvayı sürdürür: aynı bracket ve maçlar yeniden kurulur, yeni davet kodu çıkar.</summary>
    private async void TournamentResumeClicked(GwentNet.TournamentSnapshot snap)
    {
        if (_busy) return;
        SetBusy(true, "Yarım kalan turnuva yeniden kuruluyor…");
        var (cloudflared, terr) = await PrepareTunnelAsync();
        if (terr != null) { Error(terr); return; }
        var opts = new HostOptions(snap.NextGen, _lan.IsChecked == true, _upnp.IsChecked == true, cloudflared);
        var (c, err) = await OnlineController.ResumeTournamentAsync(snap, opts, new Progress<string>(m => _status.Text = m));
        if (c == null) { Error((err ?? "Turnuva sürdürülemedi.") + " Kaydı silip yeni turnuva kurabilirsin."); return; }
        _connected(c);
    }

    private UIElement TournamentResumeStrip(GwentNet.TournamentSnapshot snap)
    {
        var box = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var done = snap.Matches.Count(m => m.Winner != null && m.A != null && m.B != null);
        box.Children.Add(Ui.Text($"Yarım kalan turnuva: {snap.Players.Count} oyuncu, {done} maç bitti", 17, Theme.B("#e8c970"), true, TextAlignment.Left, true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var go = Ui.Button("Turnuvaya devam et", () => TournamentResumeClicked(snap), primary: true);
        go.Tag = "online-tournament-resume";
        go.FontSize = 16;
        var del = Ui.Button("Kaydı sil", () => { ResumeStore.ClearHostTournament(); box.Visibility = Visibility.Collapsed; });
        del.FontSize = 14;
        del.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(go);
        row.Children.Add(del);
        box.Children.Add(row);
        return box;
    }

    /// <summary>Yarım kalan maçı sürdürür: aynı oda ve koltuklar yeniden kurulur, yeni davet kodu çıkar.</summary>
    private async void ResumeClicked(GwentNet.RoomSnapshot snap)
    {
        if (_busy) return;
        SetBusy(true, "Yarım kalan maç yeniden kuruluyor…");
        var (cloudflared, terr) = await PrepareTunnelAsync();
        if (terr != null) { Error(terr); return; }
        var opts = new HostOptions(snap.NextGen, _lan.IsChecked == true, _upnp.IsChecked == true, cloudflared);
        var (c, err) = await OnlineController.ResumeHostAsync(snap, opts, new Progress<string>(m => _status.Text = m));
        if (c == null) { Error((err ?? "Maç sürdürülemedi.") + " Kaydı silip yeni oda kurabilirsin."); return; }
        _connected(c);
    }

    private async void TournamentClicked()
    {
        if (!NickOk(out var nick)) return;
        var size = _size8.IsChecked == true ? 8 : 4;
        var fee = (int)(_feeButtons.FirstOrDefault(b => b.IsChecked == true)?.Tag ?? 0);
        SetBusy(true, "Sunucu başlatılıyor…");
        var (cloudflared, terr) = await PrepareTunnelAsync();
        if (terr != null) { Error(terr); return; }
        var opts = new HostOptions(_nextGen.IsChecked == true, _lan.IsChecked == true, _upnp.IsChecked == true, cloudflared);
        var (c, err) = await OnlineController.HostTournamentAsync(nick, opts, size, fee, new Progress<string>(m => _status.Text = m));
        if (c == null) { Error(err ?? "Turnuva kurulamadı."); return; }
        Settings.Nick = nick;
        Settings.Save();
        _connected(c);
    }

    /// <summary>Yarım kalan maç şeridi: rakipler, hamle sayısı, "Devam et" ve "Sil".</summary>
    private UIElement ResumeStrip(GwentNet.RoomSnapshot snap)
    {
        var box = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        box.Children.Add(Ui.Text($"Yarım kalan maç: {snap.Setup.Nick0} – {snap.Setup.Nick1}{(snap.Setup.Nick2 != null ? " – " + snap.Setup.Nick2 : "")} ({snap.Actions.Count} hamle)", 17, Theme.B("#e8c970"), true, TextAlignment.Left, true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var go = Ui.Button("Maça devam et", () => ResumeClicked(snap), primary: true);
        go.Tag = "online-resume";
        go.FontSize = 16;
        var del = Ui.Button("Kaydı sil", () => { ResumeStore.ClearHost(); box.Visibility = Visibility.Collapsed; });
        del.FontSize = 14;
        del.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(go);
        row.Children.Add(del);
        box.Children.Add(row);
        return box;
    }

    private async void JoinClicked()
    {
        if (_busy || !NickOk(out var nick)) return;
        (OnlineController? Controller, string? Error) result;
        var invite = _invite.Text.Trim();
        if (invite.Length > 0)
        {
            SetBusy(true, "Bağlanılıyor…");
            result = await OnlineController.JoinInviteAsync(invite, nick, new Progress<string>(m => _status.Text = m));
        }
        else
        {
            var address = _address.Text.Trim();
            var code = _code.Text.Trim();
            if (address.Length == 0 && code.Length == 0) { Error("Davet kodunu yapıştır (ya da adres ve oda kodunu elle gir)."); return; }
            if (address.Length == 0) { Error("Host adresini gir."); return; }
            if (code.Length != 5) { Error("Oda kodu 5 karakterdir."); return; }
            SetBusy(true, "Bağlanılıyor…");
            result = await OnlineController.JoinAsync(address, code, nick);
            if (result.Controller != null) Settings.LastAddress = address;
        }
        if (result.Controller == null) { Error(result.Error ?? "Katılınamadı."); return; }
        Settings.Nick = nick;
        Settings.Save();
        _connected(result.Controller);
    }
}
