using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GwentClient;

/// <summary>Online giriş ekranı: takma ad, oda kur ya da bir odaya (adres + kod) katıl.</summary>
public sealed class OnlineMenuView : Grid
{
    private readonly Action _back;
    private readonly Action<OnlineController> _connected;
    private readonly TextBox _nick = new() { MaxLength = 16, Tag = "online-nick" };
    private readonly TextBox _address = new() { Tag = "online-address" };
    private readonly TextBox _code = new() { MaxLength = 5, Tag = "online-code" };
    private readonly CheckBox _nextGen = new() { Content = "Yeni nesil içerik (Roach, Geralt/Ciri Toplanma)", Foreground = Theme.Text, Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _lan = new() { Content = "Yerel ağdaki arkadaşlarım bağlanabilsin", IsChecked = true, Foreground = Theme.Text, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _tunnel = new() { Content = "İnternetten bağlanabilsin (tünel — önerilir; arkadaşın hiçbir şey kurmaz)", IsChecked = true, Foreground = Theme.Text, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _upnp = new() { Content = "Modemde otomatik port aç (UPnP — gelişmiş; her modemde çalışmaz)", IsChecked = false, Foreground = Theme.Text, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _invite = new() { Tag = "online-invite", AcceptsReturn = false };
    private readonly TextBlock _status = new() { FontFamily = Theme.Serif, FontSize = 17 * Theme.FontScale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MinHeight = 40 };
    private readonly Button _create;
    private readonly Button _join;
    private bool _busy;

    public OnlineMenuView(Action back, Action<OnlineController> connected)
    {
        _back = back;
        _connected = connected;
        Background = Theme.Bg;
        ClipToBounds = true;

        var pano = Assets.Ui("pano_novigrad");
        if (pano != null) Children.Add(new System.Windows.Controls.Image { Source = pano, Stretch = Stretch.UniformToFill, IsHitTestVisible = false });
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.9,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xc0, 0, 0, 0), 1) }
            }
        });

        foreach (var tb in new[] { _nick, _address, _code })
        {
            tb.Style = (Style)Application.Current.FindResource("GText");
            tb.FontFamily = Theme.Serif;
            tb.FontSize = 20;
        }
        _code.CharacterCasing = CharacterCasing.Upper;
        _nick.Text = Settings.Nick;
        _address.Text = Settings.LastAddress;
        _nick.Width = 300;
        _code.Width = 150;

        var root = new StackPanel { Width = 820 };
        var title = Ui.Text("Online", 44, Theme.B("#e8c970"), true);
        title.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.95 };
        root.Children.Add(title);
        root.Children.Add(Line(560));

        root.Children.Add(Label("Takma adın"));
        _nick.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(_nick);

        // --- Oda kur ---
        root.Children.Add(Section("Oda kur", "Oyun senin bilgisayarında sunucu olur; arkadaşın sana bağlanır ve sana bir davet kodu verirsin."));
        root.Children.Add(_nextGen);
        root.Children.Add(_tunnel);
        root.Children.Add(_lan);
        root.Children.Add(_upnp);
        _create = Ui.Button("Oda kur", () => CreateClicked(), primary: true);
        _create.FontSize = 20;
        _create.Padding = new Thickness(36, 10, 36, 10);
        _create.HorizontalAlignment = HorizontalAlignment.Left;
        _create.Margin = new Thickness(0, 14, 0, 0);
        _create.Tag = "online-create";
        root.Children.Add(_create);

        // --- Odaya katıl ---
        root.Children.Add(Section("Odaya katıl", "Host'un verdiği davet kodunu yapıştır."));
        root.Children.Add(Label("Davet kodu (GWENT-…)"));
        _invite.Style = (Style)Application.Current.FindResource("GText");
        _invite.FontFamily = Theme.Serif;
        _invite.FontSize = 20;
        root.Children.Add(_invite);

        var manual = new StackPanel();
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
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
        var expander = new Expander { Header = "Davet kodu yoksa: adres ve oda kodunu elle gir", Foreground = Theme.TextDim, Margin = new Thickness(0, 10, 0, 0), Content = manual, Tag = "online-manual" };
        root.Children.Add(expander);

        _join = Ui.Button("Katıl", () => JoinClicked(), primary: true);
        _join.FontSize = 20;
        _join.Padding = new Thickness(36, 10, 36, 10);
        _join.HorizontalAlignment = HorizontalAlignment.Left;
        _join.Margin = new Thickness(0, 14, 0, 0);
        _join.Tag = "online-join";
        root.Children.Add(_join);

        root.Children.Add(_status);

        var back2 = Ui.Button("Geri", () => { if (!_busy) _back(); });
        back2.FontSize = 18;
        back2.Padding = new Thickness(30, 8, 30, 8);
        back2.HorizontalAlignment = HorizontalAlignment.Left;
        back2.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(back2);

        Children.Add(new Border
        {
            Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxHeight = 980,
            Background = Theme.B("#e6140e08"), BorderBrush = Theme.B("#8a6e2c"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(44, 30, 34, 30),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.85 }
        });
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !_busy) _back(); };
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(t, 14, Theme.TextDim, true);
        l.Margin = new Thickness(0, 12, 0, 3);
        return l;
    }

    private static UIElement Section(string title, string sub)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        sp.Children.Add(new Rectangle
        {
            Height = 1, Margin = new Thickness(0, 0, 0, 14), IsHitTestVisible = false,
            Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0x90, 0xc9, 0xa2, 0x4a), 0), new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 1) }, 0)
        });
        sp.Children.Add(Ui.Text(title, 28, Theme.B("#f0d890"), true));
        sp.Children.Add(Ui.Text(sub, 15, Theme.TextDim));
        return sp;
    }

    private static Rectangle Line(double w) => new()
    {
        Height = 1.5, Width = w, Margin = new Thickness(0, 6, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
        Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(0xc9, 0xa2, 0x4a), 0), new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 1) }, 0)
    };

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
