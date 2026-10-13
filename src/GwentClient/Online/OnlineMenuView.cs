using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>Online giriş ekranı: takma ad levhası, iki kapı — "Oda kur" ve "Odaya katıl".</summary>
public sealed class OnlineMenuView : Grid
{
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
    private readonly TextBlock _status = new() { FontFamily = Theme.Serif, FontSize = 17 * Theme.FontScale, TextWrapping = TextWrapping.Wrap, MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Button _create;
    private readonly Button _join;
    private bool _busy;

    public OnlineMenuView(Action back, Action<OnlineController> connected)
    {
        _back = back;
        _connected = connected;

        foreach (var tb in new[] { _nick, _address, _code, _invite })
        {
            tb.Style = (Style)Application.Current.FindResource("GText");
            tb.FontFamily = Theme.Serif;
            tb.FontSize = 20;
        }
        _code.CharacterCasing = CharacterCasing.Upper;
        _nick.Text = Settings.Nick;
        _address.Text = Settings.LastAddress;
        _nick.Width = 300;
        _nick.TextAlignment = TextAlignment.Center;
        _code.Width = 150;

        _nextGen = Ornate.Option("Yeni nesil içerik", "Roach, Geralt/Ciri Toplanma", false);
        _tunnel = Ornate.Option("İnternetten bağlanılsın", "Tünel — önerilir; arkadaşın hiçbir şey kurmaz", true);
        _lan = Ornate.Option("Yerel ağdakiler bağlanabilsin", "Aynı modemdeki arkadaşlar için", true);
        _upnp = Ornate.Option("Modemde port aç (UPnP)", "Gelişmiş; her modemde çalışmaz", false);

        // ---- Sol kapı: oda kur ----
        var host = new StackPanel { Width = 600 };
        host.Children.Add(Door("ODA KUR", "Oyun senin bilgisayarında sunucu olur; arkadaşın sana bağlanır."));
        host.Children.Add(_tunnel);
        host.Children.Add(_lan);
        host.Children.Add(_upnp);
        host.Children.Add(_nextGen);
        _create = Ui.Button("Oda kur", () => CreateClicked(), primary: true);
        _create.FontSize = 24;
        _create.Padding = new Thickness(0, 12, 0, 12);
        _create.Margin = new Thickness(0, 22, 0, 0);
        _create.Tag = "online-create";
        host.Children.Add(_create);

        // ---- Sağ kapı: odaya katıl ----
        var join = new StackPanel { Width = 600 };
        join.Children.Add(Door("ODAYA KATIL", "Host'un sana verdiği davet kodunu yapıştır."));
        join.Children.Add(Label("DAVET KODU"));
        join.Children.Add(_invite);

        var manual = new StackPanel { Visibility = Visibility.Collapsed, Tag = "online-manual", Margin = new Thickness(0, 6, 0, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var a = new StackPanel();
        a.Children.Add(Label("HOST ADRESİ (örn. 192.168.1.5:5000)"));
        a.Children.Add(_address);
        var c = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        c.Children.Add(Label("ODA KODU"));
        c.Children.Add(_code);
        Grid.SetColumn(c, 1);
        row.Children.Add(a);
        row.Children.Add(c);
        manual.Children.Add(row);
        var manualToggle = new TextBlock
        {
            Text = "Davet kodu yoksa: adres ve oda kodunu elle gir ▾", FontFamily = Theme.Serif, FontSize = 14 * Theme.FontScale, Foreground = Theme.B("#a8905a"),
            Margin = new Thickness(0, 12, 0, 0), Cursor = System.Windows.Input.Cursors.Hand
        };
        manualToggle.MouseLeftButtonUp += (_, _) =>
        {
            manual.Visibility = manual.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            manualToggle.Text = manual.Visibility == Visibility.Visible ? "Elle girişi gizle ▴" : "Davet kodu yoksa: adres ve oda kodunu elle gir ▾";
        };
        join.Children.Add(manualToggle);
        join.Children.Add(manual);

        _join = Ui.Button("Katıl", () => JoinClicked(), primary: true);
        _join.FontSize = 24;
        _join.Padding = new Thickness(0, 12, 0, 12);
        _join.Margin = new Thickness(0, 22, 0, 0);
        _join.Tag = "online-join";
        join.Children.Add(_join);

        var hostPanel = Ornate.Panel(host, new Thickness(40, 34, 40, 38), 20, 58);
        var joinPanel = Ornate.Panel(join, new Thickness(40, 34, 40, 38), 20, 58);

        // ---- Üst: başlık + takma ad levhası; alt: durum + geri ----
        var plate = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        plate.Children.Add(new TextBlock
        {
            Text = Ornate.Spaced("TAKMA ADIN"), FontFamily = Theme.Serif, FontSize = 13 * Theme.FontScale, Foreground = Theme.B("#a8905a"), FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5)
        });
        plate.Children.Add(_nick);

        var back2 = Ui.Button("Geri", () => { if (!_busy) _back(); });
        back2.FontSize = 18;
        back2.Padding = new Thickness(34, 9, 34, 9);
        back2.HorizontalAlignment = HorizontalAlignment.Center;

        var doors = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
        doors.Children.Add(hostPanel);
        doors.Children.Add(new System.Windows.Shapes.Rectangle { Width = 40 });
        doors.Children.Add(joinPanel);

        var all = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Ornate.Heading("Online", "Arkadaşınla internetten karşılaş", 52, HorizontalAlignment.Center);
        all.Children.Add(heading);
        all.Children.Add(Ornate.Rule(560, 16));
        all.Children.Add(plate);
        all.Children.Add(doors);
        _status.Margin = new Thickness(0, 22, 0, 10);
        all.Children.Add(_status);
        all.Children.Add(back2);

        Children.Add(Ornate.Stage(all, "pano_novigrad"));
        Ornate.Reveal(new UIElement[] { heading, plate, hostPanel, joinPanel, back2 }, 28, 40, 120, 560);
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !_busy) _back(); };
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(Ornate.Spaced(t), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, 14, 0, 5);
        return l;
    }

    private static UIElement Door(string title, string sub)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var t = Ui.Text(Ornate.Spaced(title), 30, Theme.B("#f0d890"), true);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.9 };
        sp.Children.Add(t);
        var s = Ui.Text(sub, 15, Theme.TextDim, false, TextAlignment.Left, true);
        s.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(s);
        var rule = Ornate.Rule(520, 12);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(rule);
        return sp;
    }

    // ---------- Eylemler ----------

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _create.IsEnabled = !busy;
        _join.IsEnabled = !busy;
        _status.Foreground = Theme.B("#e8c970");
        _status.Text = message ?? "";
    }

    private void Error(string message)
    {
        SetBusy(false);
        _status.Foreground = Theme.Bad;
        _status.Text = message;
    }

    private bool NickOk(out string nick)
    {
        nick = _nick.Text.Trim();
        if (nick.Length is < 2 or > 16) { Error("Takma ad 2-16 karakter olmalı."); return false; }
        return true;
    }

    private async void CreateClicked()
    {
        if (_busy || !NickOk(out var nick)) return;
        SetBusy(true, "Sunucu başlatılıyor…");

        // Tünel: cloudflared yoksa yalnız kullanıcı izin verirse indirilir
        string? cloudflared = null;
        if (_tunnel.IsChecked == true)
        {
            cloudflared = GwentHost.Cloudflared.FindInstalled();
            if (cloudflared == null)
            {
                var size = await GwentHost.Cloudflared.GetDownloadSizeAsync();
                var sizeText = size is { } bytes ? $"yaklaşık {Math.Max(1, bytes / 1048576)} MB" : "boyutu bilinmiyor";
                var answer = MessageBox.Show(Window.GetWindow(this),
                    $"İnternetten bağlanabilmek için Cloudflare'in ücretsiz tünel programı (cloudflared, {sizeText}) indirilecek.\n\n" +
                    $"Kaynak: {GwentHost.Cloudflared.Source}\nKonum: {System.IO.Path.GetDirectoryName(GwentHost.Cloudflared.InstallPath)}\n" +
                    "İndirilen dosyanın Cloudflare imzası doğrulanır.\n\nİndirmeye izin veriyor musun?\n(Hayır dersen yalnız yerel ağdan bağlanılabilir.)",
                    "Gwent — tünel programı", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    SetBusy(true, "cloudflared indiriliyor… %0");
                    var progress = new Progress<double>(p => _status.Text = $"cloudflared indiriliyor… %{(int)(p * 100)}");
                    var (path, derr) = await GwentHost.Cloudflared.DownloadAsync(progress);
                    if (path == null) { Error("Tünel programı indirilemedi: " + derr); return; }
                    cloudflared = path;
                }
            }
        }

        var opts = new HostOptions(_nextGen.IsChecked == true, _lan.IsChecked == true, _upnp.IsChecked == true, cloudflared);
        var statusProgress = new Progress<string>(m => _status.Text = m);
        var (c, err) = await OnlineController.HostAsync(nick, opts, statusProgress);
        if (c == null) { Error(err ?? "Oda kurulamadı."); return; }
        Settings.Nick = nick;
        Settings.Save();
        _connected(c);
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
