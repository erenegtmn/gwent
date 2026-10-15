using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Ana menü. Solda logo ve yazı menüsü (Oyna / Deste Kurucu / Ses / Müzik / Çıkış), sağda fraksiyon kalkanı şeridi ve
/// seçili fraksiyonun lider kartı. "Oyna" maç kurulumunu (deste, lider, rakip) açar.
/// </summary>
public sealed class MenuView : Grid
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private const string DeckAll = "@all";
    private const string DeckRecommended = "@rec";

    // Fraksiyona göre oyunun kendi menü panoraması
    private static readonly Dictionary<string, string> Panos = new()
    {
        ["northern_realms"] = "pano_novigrad", ["nilfgaard"] = "pano_wyzima_castle", ["scoiatael"] = "pano_no_mans_land",
        ["monsters"] = "pano_island_of_mist", ["skellige"] = "pano_skellige"
    };

    private string _myFaction;
    private string _myLeader = "";
    private string? _oppFaction; // null = rastgele
    private string _deckChoice = DeckAll;
    private readonly ToggleButton _nextGen = Ornate.Option("Yeni nesil içerik", "Roach, Geralt/Ciri Toplanma", false);
    private readonly Action<MatchConfig> _start;
    private readonly Action<string, bool> _openBuilder;
    private readonly StackPanel _leaderPanel = new();
    private readonly TextBlock _leaderLabel = Ui.Text("Lider", 13, Theme.TextDim, true);
    private readonly WrapPanel _deckPanel = new();
    private readonly List<ToggleButton> _oppButtons = new();
    private readonly List<Border> _strip = new();

    private readonly System.Windows.Controls.Image[] _pano = { new(), new() };
    private int _panoFront;
    private string? _panoName;
    private readonly bool _animate;
    private readonly Action? _openSettings;
    private readonly Action? _openStats;
    private readonly Action? _openGuide;
    private readonly Action? _openCareer;
    private readonly Action? _openKeep;
    private readonly Action? _openDraft;
    private readonly Action? _openOnline;
    private readonly Grid _panoLayer = new();
    private bool _starting;

    private readonly Border _leaderHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Border _setup = null!;
    private bool _setupOpen;

    public MenuView(Action<MatchConfig> start, Action<string, bool> openBuilder, string? initialFaction = null, bool nextGen = false, bool animate = true, Action? openSettings = null, Action? openOnline = null, Action? openStats = null, Action? openGuide = null, Action? openCareer = null, Action? openDraft = null, Action? openKeep = null)
    {
        _openDraft = openDraft;
        _openGuide = openGuide;
        _openCareer = openCareer;
        _openKeep = openKeep;
        _openSettings = openSettings;
        _openStats = openStats;
        _openOnline = openOnline;
        _animate = animate;
        _start = start;
        _openBuilder = openBuilder;
        _myFaction = initialFaction ?? "northern_realms";
        _nextGen.IsChecked = nextGen;
        Background = Theme.Bg;
        ClipToBounds = true;

        // 1) Arka plan: oyunun menü panoraması (yavaş yakınlaşır, fraksiyon değişince çapraz geçiş yapar)
        foreach (var im in _pano)
        {
            im.Stretch = Stretch.UniformToFill;
            im.IsHitTestVisible = false;
            im.Opacity = 0;
            im.RenderTransformOrigin = new Point(0.5, 0.5);
            _panoLayer.Children.Add(im);
        }
        // Paralaks sırasında kenarlar görünmesin diye katman hafif büyük tutulur
        _panoLayer.RenderTransformOrigin = new Point(0.5, 0.5);
        _panoLayer.RenderTransform = new ScaleTransform(1.0, 1.0);
        Children.Add(_panoLayer);
        if (Assets.Available) ShowPano(Panos[_myFaction], false);
        else Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x2a, 0x1c, 0x10), Color.FromRgb(0x0c, 0x08, 0x05), 90), IsHitTestVisible = false });

        // 2) Panorama koyu olduğundan sıcak bir ışık lekesi ve kenar karartması (vinyet)
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.3, 0.45), GradientOrigin = new Point(0.3, 0.45), RadiusX = 0.75, RadiusY = 0.9,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0xff, 0xb0, 0x50), 0), new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0xc8, 0, 0, 0), 1) }
            }
        });

        // 3) Havada süzülen kor parçacıkları
        var embers = new Canvas { IsHitTestVisible = false };
        Children.Add(embers);
        if (_animate) Loaded += (_, _) => StartEmbers(embers);

        // 4) İçerik: solda logo + yazı menüsü, ortada sahne (lider kartı / maç kurulumu), sağda fraksiyon şeridi
        var layout = new Grid { Margin = new Thickness(40, 30, 30, 30) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = BuildLeft();
        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        var stage = new Grid { Width = 800, Margin = new Thickness(0, 0, 10, 0) };
        stage.Children.Add(_leaderHost);
        _setup = BuildSetup();
        _setup.Visibility = Visibility.Collapsed;
        stage.Children.Add(_setup);
        Grid.SetColumn(stage, 1);
        layout.Children.Add(stage);

        var strip = BuildStrip();
        Grid.SetColumn(strip, 2);
        layout.Children.Add(strip);
        Children.Add(layout);
        if (_openStats != null) Children.Add(ProfileChip());
        AddVersionInfo();

        BuildDecks();
        BuildLeaders();
        SyncStrip();
        SyncChecks(_oppButtons, "random");
        ShowLeaderCard(false);

        // 5) Açılış: siyahtan çıkış; öğeler hafifçe belirir. Fareyle derinlik (paralaks): arka plan ters, içerik düz yönde kayar.
        if (_animate)
        {
            var black = new Border { Background = Brushes.Black, IsHitTestVisible = false, Opacity = 0 };
            Children.Add(black);
            black.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1600)) { BeginTime = TimeSpan.FromMilliseconds(100) });
            Slide(left, -40, 0, 900, 500);
            Slide(stage, 0, 30, 900, 800);
            Slide(strip, 40, 0, 900, 1000);
        }
    }

    /// <summary>Sağ altta sürüm; GitHub'da daha yenisi varsa üstte "Yeni sürüm" bildirimi (İndir: release sayfası tarayıcıda açılır).</summary>
    private void AddVersionInfo()
    {
        var ver = Ui.Text(UpdateCheck.CurrentText + (UpdateCheck.Channel == "kaermorhen" ? " · Kaer Morhen (deneme)" : ""), 14, Theme.B("#7a705e"));
        ver.HorizontalAlignment = HorizontalAlignment.Right;
        ver.VerticalAlignment = VerticalAlignment.Bottom;
        ver.Margin = new Thickness(0, 0, 26, 14);
        ver.IsHitTestVisible = false;
        Children.Add(ver);

        var host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 0, 0) };
        Children.Add(host);
        void Show(ReleaseInfo? r)
        {
            if (r == null) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var text = Ui.Text($"◆  Yeni sürüm çıktı: {r.Tag}  (sende {UpdateCheck.CurrentText})", 19, Theme.B("#f0d890"), true);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            // Paketlenmiş kopyada oyun kendini günceller; geliştirme çıktısında ya da paket yoksa indirme sayfası açılır
            var self = Updater.CanSelfUpdate && r.ZipUrl != null;
            var get = Ui.Button(self ? "Güncelle" : "İndir", () =>
            {
                if (!self) { UpdateCheck.OpenPage(r.Url); return; }
                var dlg = new UpdateDialog(r);
                dlg.Closed += () => Children.Remove(dlg);
                Children.Add(dlg);
            }, primary: true);
            get.Margin = new Thickness(18, 0, 0, 0);
            get.Padding = new Thickness(22, 6, 22, 6);
            get.Tag = "update-download";
            row.Children.Add(get);
            var later = Ui.Button("Sonra", () => host.Content = null);
            later.Margin = new Thickness(8, 0, 0, 0);
            later.Padding = new Thickness(14, 6, 14, 6);
            row.Children.Add(later);
            var g = new Grid { Tag = "update-banner" };
            g.Children.Add(new ChamferFrame { Fill = Theme.B("#ee1c140a"), Edge = Ornate.GoldEdge, Inner = Ornate.GoldThin, Cut = 10, Inset = 4 });
            row.Margin = new Thickness(22, 10, 14, 10);
            g.Children.Add(row);
            g.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.9 };
            host.Content = g;
        }
        if (UpdateCheck.Pending is not { } pending) return;
        if (pending.IsCompleted) Show(pending.Result);
        else pending.ContinueWith(t => Dispatcher.BeginInvoke(() => Show(t.Result)), TaskScheduler.Default);
    }

    /// <summary>Sol üst köşede portre + takma ad: tıklayınca profil açılır.</summary>
    private UIElement ProfileChip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Portraits.Medallion(Settings.Portrait, 78, glow: true));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var nick = Ui.Text(string.IsNullOrWhiteSpace(Settings.Nick) ? "Oyuncu" : Settings.Nick, 24, Theme.B("#f0d890"), true);
        nick.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.95 };
        texts.Children.Add(nick);
        texts.Children.Add(Ui.Text($"Profil · ◆ {Settings.Crowns} Crown", 14, Theme.TextDim));
        row.Children.Add(texts);
        var host = new Border
        {
            Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(36, 28, 0, 0),
            Padding = new Thickness(4, 2, 18, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = "profile-chip", Opacity = 0.9
        };
        host.MouseEnter += (_, _) => host.Opacity = 1;
        host.MouseLeave += (_, _) => host.Opacity = 0.9;
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); _openStats!(); };
        return host;
    }

    // ---------- Sol: logo ve yazı menüsü ----------

    private FrameworkElement BuildLeft()
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var logo = Assets.Ui("logo");
        if (logo != null)
            left.Children.Add(new System.Windows.Controls.Image { Source = logo, MaxWidth = 520, Stretch = Stretch.Uniform, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 } });
        else
        {
            var t = Ui.Text("WITCHER 3", 48, Theme.Gold, true, TextAlignment.Center);
            t.FontFamily = Theme.Serif;
            left.Children.Add(t);
        }
        var gw = Ui.Text("G W E N T", 38, Theme.B("#e8c970"), true, TextAlignment.Center);
        gw.FontFamily = Theme.Serif;
        gw.Margin = new Thickness(0, 4, 0, 0);
        gw.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.95 };
        left.Children.Add(gw);

        // Altın ayraç
        left.Children.Add(new Rectangle
        {
            Height = 1.5, Width = 380, Margin = new Thickness(0, 22, 0, 18), IsHitTestVisible = false,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 0), new GradientStop(Color.FromRgb(0xc9, 0xa2, 0x4a), 0.5), new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 1)
            }, 0)
        });

        var list = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 360 };
        // Ana öğeler: oyun kipleri ve sık kullanılanlar. Profil (sol üstteki portre de açar) ve kılavuz altta küçük bağlantılardır.
        if (_openKeep != null) list.Children.Add(MenuItem("Kaer Morhen", () => _openKeep()));
        list.Children.Add(MenuItem("Oyna", ToggleSetup));
        if (_openCareer != null) list.Children.Add(MenuItem("Collect 'Em All", () => _openCareer()));
        if (_openDraft != null) list.Children.Add(MenuItem("Draft", () => _openDraft()));
        if (_openOnline != null) list.Children.Add(MenuItem("Online", () => _openOnline()));
        list.Children.Add(MenuItem("Deste Kurucu", () => _openBuilder(_myFaction, NextGen)));
        list.Children.Add(MenuItem("Ayarlar", () => _openSettings?.Invoke()));
        list.Children.Add(MenuItem("Çıkış", () => Application.Current.Shutdown()));
        left.Children.Add(list);

        var links = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        void Link(string text, Action click)
        {
            if (links.Children.Count > 0) links.Children.Add(new TextBlock { Text = "◆", FontSize = 9, Foreground = Theme.GoldDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) });
            links.Children.Add(SmallLink(text, click));
        }
        if (_openGuide != null) Link("Nasıl Oynanır", () => _openGuide());
        if (_openStats != null) Link("Profil", () => _openStats());
        if (links.Children.Count > 0) left.Children.Add(links);
        return left;
    }

    /// <summary>Menünün altındaki küçük yazı bağlantısı (ikincil öğeler).</summary>
    private static UIElement SmallLink(string text, Action click)
    {
        var tb = new TextBlock { Text = text, FontFamily = Theme.Serif, FontSize = 19, Foreground = Theme.B("#9a9078"), IsHitTestVisible = false };
        var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 3, 2, 3), Cursor = Cursors.Hand, Child = tb, Tag = "menu-item" };
        host.MouseEnter += (_, _) => { tb.Foreground = Theme.B("#fff2c8"); tb.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 }; };
        host.MouseLeave += (_, _) => { tb.Foreground = Theme.B("#9a9078"); tb.Effect = null; };
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); click(); };
        return host;
    }

    /// <summary>Orijinal Witcher menüsü gibi sade yazı öğesi: üzerine gelince parlar, ◆ imleci ve altın çizgi belirir.</summary>
    private static UIElement MenuItem(string text, Action? click, Func<string, string>? toggle = null)
    {
        var tb = new TextBlock { Text = text, FontFamily = Theme.Serif, FontSize = 28, Foreground = Theme.B("#bdb39a"), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var chev = new TextBlock { Text = "◆", FontSize = 13, Foreground = Theme.Gold, Opacity = 0, VerticalAlignment = VerticalAlignment.Center, Width = 28, IsHitTestVisible = false };
        var shift = new TranslateTransform();
        var row = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = shift, IsHitTestVisible = false };
        row.Children.Add(chev);
        row.Children.Add(tb);
        var scale = new ScaleTransform(0, 1);
        var line = new Rectangle
        {
            Height = 1.5, Margin = new Thickness(28, 1, 0, 0), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = scale, IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromRgb(0xe8, 0xc9, 0x70), Color.FromArgb(0, 0xe8, 0xc9, 0x70), 0)
        };
        var stack = new StackPanel { IsHitTestVisible = false };
        stack.Children.Add(row);
        stack.Children.Add(line);
        var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(6, 4, 20, 4), Cursor = Cursors.Hand, Child = stack, Tag = "menu-item" };

        var ms = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        host.MouseEnter += (_, _) =>
        {
            tb.Foreground = Theme.B("#fff2c8");
            tb.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.85 };
            chev.BeginAnimation(OpacityProperty, new DoubleAnimation(1, ms));
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(8, ms) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        };
        host.MouseLeave += (_, _) =>
        {
            tb.Foreground = Theme.B("#bdb39a");
            tb.Effect = null;
            chev.BeginAnimation(OpacityProperty, new DoubleAnimation(0, ms));
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, ms));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, ms));
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Sfx.Play("lock_in");
            click?.Invoke();
            if (toggle != null) tb.Text = toggle(tb.Text);
        };
        return host;
    }

    // ---------- Sağ: fraksiyon şeridi ve lider kartı ----------

    private FrameworkElement BuildStrip()
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 120 };
        foreach (var f in Factions)
        {
            var key = f;
            var shield = Assets.Ui("shield_" + key);
            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            FrameworkElement icon;
            if (shield != null) icon = new System.Windows.Controls.Image { Source = shield, Width = 82, Height = 82 };
            else icon = new Ellipse { Width = 70, Height = 70, Fill = Theme.FactionColor(key) };
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(1, 1);
            icon.RenderTransform = sc;
            inner.Children.Add(icon);
            var name = Ui.Text(Texts.FactionShort(key), 14, Theme.B("#c9c0aa"), true, TextAlignment.Center);
            name.FontFamily = Theme.Serif;
            name.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(name);

            var b = new Border
            {
                Tag = key, Child = inner, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(6, 8, 6, 8), Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent
            };
            b.MouseEnter += (_, _) =>
            {
                var d = new DoubleAnimation(1.18, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, d);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, d);
                name.Foreground = Theme.B("#fff2c8");
            };
            b.MouseLeave += (_, _) =>
            {
                var d = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, d);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, d);
                name.Foreground = key == _myFaction ? Theme.B("#e8c970") : Theme.B("#c9c0aa");
            };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("cursor_movement"); SelectFaction(key); };
            _strip.Add(b);
            col.Children.Add(b);
        }
        return col;
    }

    private void SyncStrip()
    {
        foreach (var b in _strip)
        {
            var sel = (string)b.Tag == _myFaction;
            b.BorderBrush = sel ? Theme.B("#c9a24a") : Brushes.Transparent;
            b.Background = sel ? Theme.B("#55c9a24a") : Brushes.Transparent;
            if (((StackPanel)b.Child).Children[1] is TextBlock t) t.Foreground = sel ? Theme.B("#e8c970") : Theme.B("#c9c0aa");
        }
    }

    private void SelectFaction(string f)
    {
        if (f == _myFaction) return;
        _myFaction = f;
        _deckChoice = DeckAll;
        BuildDecks();
        UpdateForgeSummary();
        BuildLeaders();
        SyncStrip();
        if (Assets.Available) ShowPano(Panos[f]);
        ShowLeaderCard(true);
    }

    /// <summary>Seçili fraksiyonun (ve seçili liderin) büyük kartı; yavaşça "nefes alır".</summary>
    private void ShowLeaderCard(bool animated)
    {
        var def = CardCatalog.Default.Leaders(_myFaction).FirstOrDefault(l => l.Id == _myLeader)
                  ?? CardCatalog.Default.Leaders(_myFaction).First(l => l.Edition != "nextgen");
        const double w = 340;
        var card = new GwintCard(def, w, null, null, height: w * GwintCard.Aspect) { IsHitTestVisible = false };
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 36, ShadowDepth = 6, Opacity = 0.9 };
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(1, 1);
        card.RenderTransform = sc;

        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(card);
        var name = Ui.Text(def.Name, 24, Theme.B("#f0d890"), true, TextAlignment.Center);
        name.FontFamily = Theme.Serif;
        name.Margin = new Thickness(0, 16, 0, 0);
        name.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1.5, Opacity = 0.95 };
        sp.Children.Add(name);
        var fac = Ui.Text(Texts.Faction(def.Faction), 16, Theme.B("#c9c0aa"), false, TextAlignment.Center);
        fac.FontFamily = Theme.Serif;
        sp.Children.Add(fac);
        _leaderHost.Child = sp;

        if (!_animate) return;
        var breathe = new DoubleAnimation(1.0, 1.035, TimeSpan.FromSeconds(3.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        if (animated) Slide(sp, 0, 24, 600, 0);
    }

    // ---------- Maç kurulumu ----------

    private Border BuildSetup()
    {
        var root = new StackPanel { Width = 690 };
        root.Children.Add(Ornate.Heading("Maçı hazırla", null, 34));
        var rule = Ornate.Rule(560, 12);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(rule);

        var deckHead = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        deckHead.Children.Add(Ornate.Caption("Deste", 4));
        var edit = Ui.Button("Desteleri düzenle…", () => _openBuilder(_myFaction, NextGen));
        edit.HorizontalAlignment = HorizontalAlignment.Right;
        edit.Padding = new Thickness(14, 4, 14, 4);
        edit.FontSize = 12;
        deckHead.Children.Add(edit);
        root.Children.Add(deckHead);
        root.Children.Add(_deckPanel);

        _leaderLabel.Text = Ornate.Spaced("LİDER");
        _leaderLabel.Margin = new Thickness(0, 12, 0, 6);
        root.Children.Add(_leaderLabel);
        root.Children.Add(_leaderPanel);

        root.Children.Add(Ornate.Caption("Rakip fraksiyon"));
        root.Children.Add(OppRow());

        root.Children.Add(Ornate.Caption("Rakip zorluğu"));
        root.Children.Add(DifficultyRow());
        root.Children.Add(Ornate.Caption("Gaunter O'Dimm'in lanetleri (isteğe bağlı)"));
        root.Children.Add(CurseRow());
        root.Children.Add(DailyRow());
        root.Children.Add(Ornate.Caption("Kaer Morhen: savaş alanı (deneme)"));
        root.Children.Add(BattlefieldRow());
        root.Children.Add(ForgeRow());
        _nextGen.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(_nextGen);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        var back = Ui.Button("Geri", ToggleSetup);
        back.FontSize = 18;
        back.Padding = new Thickness(30, 12, 30, 12);
        back.Margin = new Thickness(0, 0, 14, 0);
        buttons.Children.Add(back);
        var go = Ui.Button("Oyuna başla", Start, primary: true);
        go.FontSize = 18;
        go.Padding = new Thickness(40, 12, 40, 12);
        buttons.Children.Add(go);
        // Düğmeler kaydırılan alanın dışında: panel uzasa da "Oyuna başla" hep görünür
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
        Grid.SetRow(buttons, 1);
        body.Children.Add(buttons);

        var framed = Ornate.Panel(body, new Thickness(40, 30, 32, 30), 20, 54);
        framed.VerticalAlignment = VerticalAlignment.Center;
        framed.MaxHeight = 980;
        return new Border { Child = framed, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Maça giden destenin demirci rünleri ("tüm kartlar" seçiliyse destenin kendisi oyun kurulurken oluşur; aynı kartlarla doğrulanır).</summary>
    private List<CardRune>? ForgeRunes(Deck? myDeck, string leader)
    {
        var (deck, key) = ForgeDeck();
        var runes = KaerMorhen.ForgeStore.For(key, myDeck ?? deck with { LeaderId = leader });
        return runes.Count > 0 ? runes : null;
    }

    private void ToggleSetup()
    {
        _setupOpen = !_setupOpen;
        _setup.Visibility = _setupOpen ? Visibility.Visible : Visibility.Collapsed;
        _leaderHost.Visibility = _setupOpen ? Visibility.Collapsed : Visibility.Visible;
        if (_setupOpen && _animate) Slide(_setup, 30, 0, 450, 0);
        if (!_setupOpen) ShowLeaderCard(false);
    }

    private UIElement OppRow()
    {
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var f in new[] { "random" }.Concat(Factions))
        {
            var key = f;
            var label = Ui.Text(key == "random" ? "Rastgele" : Texts.FactionShort(key), 14, Theme.Text, true, TextAlignment.Center);
            label.FontFamily = Theme.Serif;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var shield = key == "random" ? null : Assets.Ui("shield_" + key);
            if (shield != null)
                content.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 4) });
            content.Children.Add(label);
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"),
                Tag = key,
                Margin = new Thickness(3),
                Content = content
            };
            if (key != "random" && shield == null) tb.Background = Theme.FactionColor(key);
            tb.Click += (_, _) => { _oppFaction = key == "random" ? null : key; SyncChecks(_oppButtons, key); };
            _oppButtons.Add(tb);
            grid.Children.Add(tb);
        }
        return grid;
    }

    private readonly List<ToggleButton> _diffButtons = new();
    private readonly List<ToggleButton> _fieldButtons = new();
    private readonly TextBlock _forgeSummary = Ui.Text("", 13, Theme.TextDim, false, TextAlignment.Left, true);

    /// <summary>Seçili destenin kendisi ve demirci kaydı anahtarı (fraksiyon + deste seçimi).</summary>
    private (Deck Deck, string Key) ForgeDeck()
    {
        var opts = new GameOptions { NextGen = NextGen };
        Deck deck = DeckValidator.DefaultDeck(_myFaction, opts) with { LeaderId = _myLeader };
        if (_deckChoice == DeckRecommended) deck = DeckValidator.RecommendedDeck(_myFaction, opts, null, _myLeader);
        else if (_deckChoice.StartsWith("deck:") && DeckStore.For(_myFaction).FirstOrDefault(d => "deck:" + d.Name == _deckChoice) is { } saved) deck = saved.ToDeck();
        return (deck, _myFaction + "|" + _deckChoice);
    }

    /// <summary>Demirci: rün özeti ve düğmesi (savaş alanı seçiliyken maça geçer).</summary>
    private UIElement ForgeRow()
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _forgeSummary.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(_forgeSummary);
        var open = Ui.Button("Demirci…", OpenForge);
        open.Padding = new Thickness(16, 7, 16, 7);
        open.FontSize = 15;
        open.Tag = "forge-open";
        Grid.SetColumn(open, 1);
        g.Children.Add(open);
        UpdateForgeSummary();
        return g;
    }

    private void UpdateForgeSummary()
    {
        var (deck, key) = ForgeDeck();
        var runes = KaerMorhen.ForgeStore.For(key, deck);
        _forgeSummary.Text = runes.Count == 0
            ? "Bu destede rün yok. Demircide kartlarına rün takabilirsin (savaş alanı seçiliyken geçerli)."
            : $"Rünler {runes.Count}/{RuneIds.MaxPerDeck}: " + string.Join(" · ", runes.Select(r => $"{CardCatalog.Default.Get(r.CardId).Name} ({GwentAI.Forge.Get(r.Rune).Name})"));
    }

    private void OpenForge()
    {
        var (deck, key) = ForgeDeck();
        KaerMorhen.ForgeView? view = null;
        view = new KaerMorhen.ForgeView(deck, key, () => { Children.Remove(view); UpdateForgeSummary(); });
        Children.Add(view);
    }
    /// <summary>Savaş alanı seçimi: null = kapalı (klasik), "random" ya da harita kimliği. Açıkken komutanlar da açıktır.</summary>
    private string? _battlefield;

    /// <summary>Kaer Morhen denemesi: arazili savaş alanı + komutan figürü (TASARIM.md §5).</summary>
    private UIElement BattlefieldRow()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var items = new List<(string? Id, string Name, string Text)> { (null, "Kapalı", "Klasik Gwent tahtası."), ("random", "Rastgele", "Her maç başka bir harita.") };
        items.AddRange(GwentAI.Battlefields.All.Select(m => ((string?)m.Id, m.Name, Summary(m))));
        foreach (var (id, name, text) in items)
        {
            var key = id;
            var sp = new StackPanel { Width = 196 };
            sp.Children.Add(Ui.Text(name, 15, Theme.B("#d8c890"), true));
            sp.Children.Add(Ui.Text(text, 11, Theme.TextDim, false, TextAlignment.Left, true));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = sp, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 7),
                HorizontalContentAlignment = HorizontalAlignment.Left, Tag = "battlefield:" + (id ?? "off"), IsChecked = _battlefield == id
            };
            tb.Click += (_, _) =>
            {
                _battlefield = key;
                foreach (var b in _fieldButtons) b.IsChecked = (string)b.Tag == "battlefield:" + (key ?? "off");
            };
            _fieldButtons.Add(tb);
            wrap.Children.Add(tb);
        }
        return wrap;

        static string Summary(TerrainMap m)
        {
            var first = string.Join(", ", Enum.GetValues<RowKind>().Select(rk => m.At(0, rk, 1)).Where(t => t != Terrain.Plain).Distinct().Select(GwentAI.Battlefields.Name));
            return (first.Length == 0 ? "Düzlük" : first) + (m.Rounds.Count > 1 ? "; raundlar arasında değişir." : ".");
        }
    }
    private readonly HashSet<string> _curses = new();

    /// <summary>Gaunter O'Dimm'in lanetleri: maçın kuralını büken isteğe bağlı seçenekler (birden çok seçilebilir).</summary>
    private UIElement CurseRow()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var c in GwentAI.Curses.All)
        {
            var curse = c;
            var sp = new StackPanel { Width = 196 };
            sp.Children.Add(Ui.Text(c.Name, 15, Theme.B("#e0c8f0"), true));
            sp.Children.Add(Ui.Text(c.Text, 11, Theme.TextDim, false, TextAlignment.Left, true));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = sp, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 7),
                HorizontalContentAlignment = HorizontalAlignment.Left, Tag = "curse:" + c.Id, IsChecked = _curses.Contains(c.Id)
            };
            tb.Click += (_, _) => { if (tb.IsChecked == true) _curses.Add(curse.Id); else _curses.Remove(curse.Id); };
            wrap.Children.Add(tb);
        }
        return wrap;
    }

    /// <summary>Günün laneti: bugünün lanetleri ve rakibi; herkes aynı gün aynı maçı oynar. Senin destenle, Zor rakibe karşı.</summary>
    private UIElement DailyRow()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var d = GwentAI.Curses.Daily(today);
        var names = string.Join(" + ", d.Curses.Select(id => GwentAI.Curses.Get(id).Name));
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Ui.Text($"Günün Laneti: {names}", 16, Theme.B("#e0c8f0"), true));
        var best = DailyStore.Today(today);
        info.Children.Add(Ui.Text($"Rakip: Gaunter O'Dimm ({Texts.FactionShort(d.OppFaction)}, Zor) · " +
            (best == null ? "bugün henüz oynamadın" : best.Won ? $"bugün yendin (fark +{best.Margin})" : $"bugün kaybettin (fark {best.Margin})"), 12, Theme.TextDim, false, TextAlignment.Left, true));
        g.Children.Add(info);
        var play = Ui.Button("Günün Laneti", StartDaily);
        play.Padding = new Thickness(16, 7, 16, 7);
        play.FontSize = 15;
        play.Tag = "daily-curse";
        Grid.SetColumn(play, 1);
        g.Children.Add(play);
        return g;
    }

    private UIElement DifficultyRow()
    {
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (d, label, sub) in new[]
                 {
                     (GwentAI.Difficulty.Easy, "Kolay", "acemi destesi"),
                     (GwentAI.Difficulty.Normal, "Normal", "az kahraman"),
                     (GwentAI.Difficulty.Hard, "Zor", "tam deste, usta")
                 })
        {
            var diff = d;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Ui.Text(label, 18, Theme.Text, true, TextAlignment.Center));
            sp.Children.Add(Ui.Text(sub, 12, Theme.TextDim, false, TextAlignment.Center));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Tag = diff, Margin = new Thickness(3), Content = sp,
                IsChecked = Settings.Difficulty == diff, Padding = new Thickness(8, 8, 8, 8)
            };
            tb.Click += (_, _) =>
            {
                Settings.Difficulty = diff;
                Settings.Save();
                foreach (var b in _diffButtons) b.IsChecked = (GwentAI.Difficulty)b.Tag == diff;
            };
            _diffButtons.Add(tb);
            grid.Children.Add(tb);
        }
        return grid;
    }

    private static void SyncChecks(List<ToggleButton> list, string key)
    {
        foreach (var b in list) b.IsChecked = (string)b.Tag == key;
    }

    private bool NextGen => _nextGen.IsChecked == true;

    // ---------- Animasyon yardımcıları ----------

    internal static void Slide(FrameworkElement el, double dx, double dy, int ms, int delayMs)
    {
        var move = new TranslateTransform(dx, dy);
        el.RenderTransform = move;
        var d = TimeSpan.FromMilliseconds(ms);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, d) { BeginTime = begin, EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, d) { BeginTime = begin, EasingFunction = ease });
        el.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(begin)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(begin + d))
            }
        });
    }

    /// <summary>Panoramayı gösterir; eskisi solarken yenisi belirir, ikisi de yavaşça yakınlaşır (Ken Burns).</summary>
    private void ShowPano(string name, bool fade = true)
    {
        if (_panoName == name) return;
        var src = Assets.Ui(name);
        if (src == null) return;
        _panoName = name;
        var old = _pano[_panoFront];
        _panoFront = 1 - _panoFront;
        var im = _pano[_panoFront];
        im.Source = src;
        var smooth = fade && _animate;
        if (smooth)
        {
            var dur = TimeSpan.FromMilliseconds(1400);
            im.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
            old.BeginAnimation(OpacityProperty, new DoubleAnimation(old.Opacity, 0, dur));
        }
        else
        {
            im.BeginAnimation(OpacityProperty, null);
            old.BeginAnimation(OpacityProperty, null);
            im.Opacity = 1;
            old.Opacity = 0;
        }
        if (!_animate) return;
        var sc = new ScaleTransform(1.0, 1.0);
        im.RenderTransform = sc;
        var zoom = new DoubleAnimation(1.0, 1.1, TimeSpan.FromSeconds(40)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
    }

    private static void StartEmbers(Canvas c)
    {
        var rng = new Random();
        void Spawn(bool initial)
        {
            var w = Math.Max(400, c.ActualWidth);
            var h = Math.Max(300, c.ActualHeight);
            var size = 1.5 + rng.NextDouble() * 3;
            // Bulanıklık efekti yerine yumuşak kenarlı gradyan: 38 parçacıkta her karede efekt çalıştırmaz
            var core = Color.FromRgb(255, (byte)(150 + rng.Next(80)), (byte)(40 + rng.Next(60)));
            var e = new Ellipse
            {
                Width = size * 2.6, Height = size * 2.6, IsHitTestVisible = false, Opacity = 0,
                Fill = new RadialGradientBrush { GradientStops = { new GradientStop(core, 0), new GradientStop(core, 0.3), new GradientStop(Color.FromArgb(0, core.R, core.G, core.B), 1) } }
            };
            var tt = new TranslateTransform();
            e.RenderTransform = tt;
            Canvas.SetLeft(e, rng.NextDouble() * w);
            Canvas.SetTop(e, h * (0.55 + rng.NextDouble() * 0.5));
            c.Children.Add(e);
            var dur = TimeSpan.FromSeconds(7 + rng.NextDouble() * 9);
            var begin = TimeSpan.FromSeconds(initial ? rng.NextDouble() * 6 : 0);
            var drift = (rng.NextDouble() - 0.3) * 160;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -h * (0.35 + rng.NextDouble() * 0.45), dur) { BeginTime = begin });
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, drift, dur) { BeginTime = begin });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = dur };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.45 + rng.NextDouble() * 0.5, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            fade.Completed += (_, _) => { c.Children.Remove(e); if (c.IsLoaded) Spawn(false); };
            e.BeginAnimation(OpacityProperty, fade);
        }
        for (var i = 0; i < 38; i++) Spawn(true);
    }

    // ---------- Deste seçimi ----------

    private void BuildDecks()
    {
        _deckPanel.Children.Clear();
        var buttons = new List<ToggleButton>();

        void AddChoice(string key, string text, string? sub, bool enabled = true)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(text, 13, enabled ? Theme.Text : Theme.TextDim, true));
            if (sub != null) sp.Children.Add(Ui.Text(sub, 10.5, Theme.TextDim));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"),
                Content = sp,
                Tag = key,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 5, 12, 5),
                IsEnabled = enabled,
                MinWidth = 150
            };
            tb.Click += (_, _) =>
            {
                _deckChoice = key;
                foreach (var b in buttons) b.IsChecked = (string)b.Tag == _deckChoice;
                UpdateLeaderVisibility();
                UpdateForgeSummary();
            };
            buttons.Add(tb);
            _deckPanel.Children.Add(tb);
        }

        AddChoice(DeckAll, "Tüm kartlar", "fraksiyonun bütün kartları");
        AddChoice(DeckRecommended, "Önerilen deste", "kahramanlar + en güçlü birimler");
        foreach (var d in DeckStore.For(_myFaction))
        {
            var errors = DeckValidator.Validate(d.ToDeck(), new GameOptions { NextGen = NextGen });
            AddChoice("deck:" + d.Name, d.Name, errors.Count == 0 ? $"{d.CardIds.Count} kart" : "geçersiz — düzenle", errors.Count == 0);
        }
        foreach (var b in buttons) b.IsChecked = (string)b.Tag == _deckChoice;
    }

    private void UpdateLeaderVisibility()
    {
        var saved = _deckChoice.StartsWith("deck:");
        _leaderPanel.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        _leaderLabel.Visibility = _leaderPanel.Visibility;
    }

    private void BuildLeaders()
    {
        _leaderPanel.Children.Clear();
        var leaders = CardCatalog.Default.Leaders(_myFaction).Where(l => l.Edition != "nextgen").ToList();
        _myLeader = leaders[0].Id;
        var buttons = new List<ToggleButton>();
        var wrap = new WrapPanel();
        var desc = Ui.Text(Texts.LeaderText(leaders[0]), 15, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
        desc.FontStyle = FontStyles.Italic;
        desc.Margin = new Thickness(2, 2, 0, 0);
        foreach (var l in leaders)
        {
            var leader = l;
            var sp = new StackPanel { Width = 104 };
            sp.Children.Add(new GwintCard(leader, 104, null, null, height: 104 * GwintCard.Aspect) { IsHitTestVisible = false });
            var nm = Ui.Text(leader.Name.Contains(':') ? leader.Name[(leader.Name.IndexOf(':') + 1)..].Trim() : leader.Name, 12, Theme.Text, true, TextAlignment.Center, true);
            nm.Margin = new Thickness(0, 5, 0, 0);
            sp.Children.Add(nm);
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"),
                Content = sp,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(6, 6, 6, 6),
                Tag = leader.Id,
                ToolTip = leader.Name
            };
            tb.Click += (_, _) =>
            {
                _myLeader = leader.Id;
                desc.Text = Texts.LeaderText(leader);
                foreach (var b in buttons) b.IsChecked = (string)b.Tag == _myLeader;
            };
            buttons.Add(tb);
            wrap.Children.Add(tb);
        }
        _leaderPanel.Children.Add(wrap);
        _leaderPanel.Children.Add(desc);
        buttons[0].IsChecked = true;
        UpdateLeaderVisibility();
    }

    private void Start() => Start(null);

    private void StartDaily() => Start(GwentAI.Curses.Daily(DateOnly.FromDateTime(DateTime.Now)));

    private void Start(GwentAI.DailyCurse? daily)
    {
        var rng = new Random();
        var opp = daily?.OppFaction ?? _oppFaction ?? Factions[rng.Next(Factions.Length)];
        var oppLeaders = CardCatalog.Default.Leaders(opp).Where(l => l.Edition != "nextgen").ToList();
        var oppLeader = daily?.OppLeaderId ?? oppLeaders[rng.Next(oppLeaders.Count)].Id;
        var opts = new GameOptions { NextGen = NextGen };
        var curses = daily != null ? daily.Curses.ToList() : _curses.ToList();

        Deck? myDeck = null;
        var leader = _myLeader;
        if (_deckChoice == DeckRecommended)
            myDeck = DeckValidator.RecommendedDeck(_myFaction, opts, null, _myLeader);
        else if (_deckChoice.StartsWith("deck:"))
        {
            var saved = DeckStore.For(_myFaction).FirstOrDefault(d => "deck:" + d.Name == _deckChoice);
            if (saved != null) { myDeck = saved.ToDeck(); leader = saved.LeaderId; }
        }

        if (curses.Contains(GwentAI.Curses.Mirror)) { opp = _myFaction; oppLeader = myDeck?.LeaderId ?? leader; }   // Ayna: rakip senin destenle
        var seed = daily?.Seed ?? rng.Next(1, int.MaxValue);
        var field = daily != null ? null : _battlefield == "random" ? GwentAI.Battlefields.ForSeed(seed).Id : _battlefield;
        var cfg = new MatchConfig(_myFaction, leader, opp, oppLeader, NextGen, seed, myDeck,
            daily != null ? GwentAI.Difficulty.Hard : Settings.Difficulty, null, curses.Count > 0 ? curses : null, daily?.Date, field, field != null,
            field != null ? ForgeRunes(myDeck, leader) : null);
        if (_animate) ShowVs(cfg);
        else _start(cfg);
    }

    // ---------- "VS" giriş ekranı ----------

    private void ShowVs(MatchConfig cfg)
    {
        if (_starting) return;
        _starting = true;
        Children.Add(new VsScreen(new VsSideInfo(string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick, cfg.MyFaction, cfg.MyLeaderId, true, Settings.Portrait), new VsSideInfo(cfg.Daily != null ? "Gaunter O'Dimm" : "Rakip", cfg.OppFaction, cfg.OppLeaderId, false, cfg.Daily != null ? "gaunter_odimm" : cfg.OppLeaderId),
            () => { if (_animate) _start(cfg); }, _animate));
    }
}
