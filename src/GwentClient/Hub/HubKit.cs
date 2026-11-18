using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>
/// Merkez kapılarının görselleri: <c>assets/ui/hub/&lt;anahtar&gt;.jpg</c> (dikey kapı) ve <c>&lt;anahtar&gt;_wide.jpg</c> (arka plan).
/// Web'den seçilip kırpılmış Witcher/Gwent sahneleridir (ARAYUZ.md); yoksa yedek görsel kullanılır.
/// </summary>
public static class HubArt
{
    public static string Tile(string key, string fallback) => Assets.Ui("hub/" + key) != null ? "hub/" + key : fallback;
    public static string Wide(string key, string fallback) => Assets.Ui("hub/" + key + "_wide") != null ? "hub/" + key + "_wide" : fallback;
}

/// <summary>Arka plan: iki görüntü üst üste; yeni sahne gelince eskisi solar (çapraz geçiş).</summary>
public sealed class HubBackdrop : Grid
{
    private readonly Image[] _img = { new(), new() };
    private int _front;
    private string? _name;
    private readonly bool _animate;

    public HubBackdrop(bool animate)
    {
        _animate = animate;
        foreach (var im in _img) { im.Stretch = Stretch.UniformToFill; im.IsHitTestVisible = false; im.Opacity = 0; Children.Add(im); }
        Background = Theme.Bg;
        IsHitTestVisible = false;
    }

    public void Show(string? name)
    {
        if (name == null || name == _name) return;
        var src = Assets.Ui(name);
        if (src == null) return;
        _name = name;
        var old = _img[_front];
        _front = 1 - _front;
        var im = _img[_front];
        im.Source = src;
        if (_animate && Ornate.Animate)
        {
            var dur = TimeSpan.FromMilliseconds(700);
            im.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.6, dur));
            old.BeginAnimation(OpacityProperty, new DoubleAnimation(old.Opacity, 0, dur));
        }
        else
        {
            im.BeginAnimation(OpacityProperty, null);
            old.BeginAnimation(OpacityProperty, null);
            im.Opacity = 0.6;
            old.Opacity = 0;
        }
    }
}

/// <summary>
/// Mod kapısı: dikey, tam boy sahne görselli kart. Etkin (fare üstünde ya da klavyeyle seçili) olunca yükselir,
/// görseli yakınlaşır, altın çizgisi uzar. Sahne görseli yoksa vurgu renginden bir zemin çizer.
/// </summary>
public sealed class HubTile : Grid
{
    public string Key { get; }
    public Color Accent { get; }
    public string? Pano { get; }
    /// <summary>Etkinken tüm ekranın arka planı olacak geniş görsel (yoksa kapı görseli).</summary>
    public string? Backdrop { get; init; }
    private readonly Action _click;
    private readonly Image _art = new();
    private readonly ScaleTransform _artScale = new(1.0, 1.0);
    private readonly TranslateTransform _lift = new();
    private readonly ScaleTransform _line = new(0, 1);
    private readonly ChamferFrame _frame;
    private readonly TextBlock _tagline;
    private readonly TextBlock _title;
    private readonly Border _clip = new();
    private bool _active;
    public event Action<HubTile>? Hovered;

    public HubTile(string key, string title, string tagline, IEnumerable<string> facts, Color accent, string? pano, Action click, bool animate = true)
    {
        Key = key;
        Accent = accent;
        Pano = pano;
        _click = click;
        Tag = "hub-tile";
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        Focusable = false;
        MinHeight = 440;
        RenderTransform = _lift;
        SnapsToDevicePixels = true;

        // Sahne: tam boy; kenarlar pahlı kırpılır
        var inner = new Grid();
        inner.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb((byte)(accent.R / 4 + 14), (byte)(accent.G / 4 + 12), (byte)(accent.B / 4 + 10)), Color.FromRgb(0x0c, 0x09, 0x06), 90) });
        if (pano != null && Assets.Ui(pano) is { } src)
        {
            _art.Source = src;
            _art.Stretch = Stretch.UniformToFill;
            _art.RenderTransformOrigin = new Point(0.5, 0.5);
            _art.RenderTransform = _artScale;
            inner.Children.Add(_art);
        }
        // Alt kısım okunabilsin diye koyu geçiş; üstte ince vurgu rengi tonu
        inner.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x28, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.34),
                new GradientStop(Color.FromArgb(0x96, 0x0a, 0x07, 0x04), 0.64),
                new GradientStop(Color.FromArgb(0xf2, 0x0a, 0x07, 0x04), 1)
            }, 90)
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30, 0, 30, 30), IsHitTestVisible = false };
        _title = new TextBlock { Text = title, FontFamily = Theme.Serif, FontSize = 44 * Theme.FontScale, Foreground = Theme.B("#f4e6bf"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _title.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = 0.95 };
        text.Children.Add(_title);
        text.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 2, Margin = new Thickness(0, 6, 0, 10), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _line,
            Fill = new LinearGradientBrush(accent, Color.FromArgb(0, accent.R, accent.G, accent.B), 0), HorizontalAlignment = HorizontalAlignment.Stretch
        });
        _tagline = Ui.Text(tagline, 18, Theme.B("#b9ae94"), false, TextAlignment.Left, true);
        text.Children.Add(_tagline);
        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var f in facts)
        {
            chips.Children.Add(new Border
            {
                Background = Theme.B("#66000000"), BorderBrush = Theme.B("#55c9a24a"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 6), Child = Ui.Text(f, 14, Theme.B("#d8cca8"))
            });
        }
        text.Children.Add(chips);
        inner.Children.Add(text);

        _clip.Child = inner;
        _clip.SizeChanged += (_, e) => _clip.Clip = ChamferFrame.Chamfer(0, 0, e.NewSize.Width, e.NewSize.Height, 22);
        Children.Add(_clip);
        _frame = new ChamferFrame { Edge = Ornate.GoldEdge, Inner = Ornate.GoldThin, Cut = 22, Inset = 8, SunSize = 0 };
        Children.Add(_frame);

        MouseEnter += (_, _) => { Hovered?.Invoke(this); };
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Open(); };
        SetActive(false, false);
        _animate = animate;
    }

    private readonly bool _animate;

    public void Open() { Sfx.Play("lock_in"); _click(); }

    public void SetActive(bool on, bool animate = true)
    {
        if (_active == on && animate) return;
        _active = on;
        var smooth = animate && _animate && Ornate.Animate;
        var ms = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        void Anim(DependencyProperty p, Animatable target, double to, double? slow = null)
        {
            if (smooth) target.BeginAnimation(p, new DoubleAnimation(to, slow != null ? TimeSpan.FromMilliseconds(slow.Value) : ms) { EasingFunction = ease });
            else { target.BeginAnimation(p, null); target.SetValue(p, to); }
        }
        Anim(TranslateTransform.YProperty, _lift, on ? -14 : 0);
        Anim(ScaleTransform.ScaleXProperty, _artScale, on ? 1.07 : 1.0, 900);
        Anim(ScaleTransform.ScaleYProperty, _artScale, on ? 1.07 : 1.0, 900);
        Anim(ScaleTransform.ScaleXProperty, _line, on ? 1 : 0, 420);
        if (smooth) BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0.8, ms)); else { BeginAnimation(OpacityProperty, null); Opacity = on ? 1 : 0.8; }
        _frame.Edge = on ? new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, Accent.R + 40), (byte)Math.Min(255, Accent.G + 40), (byte)Math.Min(255, Accent.B + 40))) : Ornate.GoldEdge;
        _frame.Inner = on ? new SolidColorBrush(Accent) : Ornate.GoldThin;
        _tagline.Foreground = on ? Theme.B("#e8dcc4") : Theme.B("#b9ae94");
        Effect = on ? new DropShadowEffect { Color = Accent, BlurRadius = 38, ShadowDepth = 0, Opacity = 0.55 } : new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.8 };
        Panel.SetZIndex(this, on ? 5 : 0);
    }
}

/// <summary>Yan yana mod kapıları; fareyle ya da ← → ile etkin olan değişir, Enter açar.</summary>
public sealed class TileRow : Grid
{
    private readonly List<HubTile> _tiles;
    private int _active = -1;
    public event Action<HubTile>? ActiveChanged;
    public HubTile? Active => _active >= 0 ? _tiles[_active] : null;

    public TileRow(IReadOnlyList<HubTile> tiles, double maxTileWidth = 440, int start = 0)
    {
        _tiles = tiles.ToList();
        HorizontalAlignment = HorizontalAlignment.Center;
        for (var i = 0; i < _tiles.Count; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = maxTileWidth });
            var t = _tiles[i];
            var idx = i;
            t.Margin = new Thickness(i == 0 ? 0 : 14, 14, i == _tiles.Count - 1 ? 0 : 14, 14);
            t.Hovered += _ => Select(idx);
            SetColumn(t, i);
            Children.Add(t);
        }
        Select(Math.Clamp(start, 0, _tiles.Count - 1), false);
    }

    public void Select(int i, bool animate = true)
    {
        if (i == _active || i < 0 || i >= _tiles.Count) return;
        if (_active >= 0) _tiles[_active].SetActive(false, animate);
        _active = i;
        _tiles[i].SetActive(true, animate);
        ActiveChanged?.Invoke(_tiles[i]);
    }

    /// <summary>Klavye: ← → seçer, Enter açar. İşlendiyse true.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Left: Select((_active - 1 + _tiles.Count) % _tiles.Count); Sfx.Play("cursor_movement"); return true;
            case Key.Right: Select((_active + 1) % _tiles.Count); Sfx.Play("cursor_movement"); return true;
            case Key.Enter: case Key.Space: Active?.Open(); return true;
        }
        return false;
    }
}

/// <summary>Sürüm yazısı ve "yeni sürüm" bildirimi (menü ve ana merkezde ortak).</summary>
public static class UpdateBanner
{
    public static void Attach(Grid g)
    {
        var ver = Ui.Text(UpdateCheck.CurrentText + (UpdateCheck.Channel == "kaermorhen" ? " · Kaer Morhen (deneme)" : ""), 14, Theme.B("#7a705e"));
        ver.HorizontalAlignment = HorizontalAlignment.Right;
        ver.VerticalAlignment = VerticalAlignment.Bottom;
        ver.Margin = new Thickness(0, 0, 26, 14);
        ver.IsHitTestVisible = false;
        g.Children.Add(ver);

        var host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 0, 0) };
        g.Children.Add(host);
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
                dlg.Closed += () => g.Children.Remove(dlg);
                g.Children.Add(dlg);
            }, primary: true);
            get.Margin = new Thickness(18, 0, 0, 0);
            get.Padding = new Thickness(22, 6, 22, 6);
            get.Tag = "update-download";
            row.Children.Add(get);
            var later = Ui.Button("Sonra", () => host.Content = null);
            later.Margin = new Thickness(8, 0, 0, 0);
            later.Padding = new Thickness(14, 6, 14, 6);
            row.Children.Add(later);
            var b = new Grid { Tag = "update-banner" };
            b.Children.Add(new ChamferFrame { Fill = Theme.B("#ee1c140a"), Edge = Ornate.GoldEdge, Inner = Ornate.GoldThin, Cut = 10, Inset = 4 });
            row.Margin = new Thickness(22, 10, 14, 10);
            b.Children.Add(row);
            b.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.9 };
            host.Content = b;
        }
        if (UpdateCheck.Pending is not { } pending) return;
        if (pending.IsCompleted) Show(pending.Result);
        else pending.ContinueWith(t => g.Dispatcher.BeginInvoke(() => Show(t.Result)), TaskScheduler.Default);
    }
}

/// <summary>Merkez ekranların ortak çerçevesi: sahne zemini, üstte ekmek kırıntısı + profil, altta tuş ipuçları.</summary>
public static class HubKit
{
    public static Grid Screen(UIElement content, HubBackdrop backdrop, IReadOnlyList<(string Text, Action? Go)>? crumbs, Action? back, Action? openProfile, bool showLogo = false, string[]? hints = null)
    {
        var root = new Grid { Background = Theme.Bg, ClipToBounds = true, Focusable = true };
        root.Children.Add(backdrop);
        root.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.85, RadiusY = 0.95,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xdd, 0, 0, 0), 1) }
            }
        });
        // Üst ve alt karartma: başlık ve ipuçları okunsun
        root.Children.Add(new Border { Height = 170, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = new LinearGradientBrush(Color.FromArgb(0xf0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90) });
        root.Children.Add(new Border { Height = 130, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xf0, 0, 0, 0), 90) });
        root.Children.Add(Ornate.Embers(22));

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Üst şerit
        var header = new Grid { Margin = new Thickness(44, 30, 44, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (showLogo && Assets.Ui("logo") is { } logo)
            left.Children.Add(new Image { Source = logo, Height = 92, Stretch = Stretch.Uniform, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 } });
        else if (showLogo)
            left.Children.Add(Ornate.Heading("Witcher 3  Gwent", null, 34));
        if (back != null) left.Children.Add(BackButton(back));
        if (crumbs != null) left.Children.Add(Crumbs(crumbs));
        header.Children.Add(left);
        if (openProfile != null)
        {
            var chip = ProfileChip(openProfile);
            Grid.SetColumn(chip, 1);
            header.Children.Add(chip);
        }
        layout.Children.Add(header);

        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        // Alt şerit: tuş ipuçları
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(48, 0, 0, 24) };
        foreach (var h in hints ?? new[] { "←→|Seç", "Enter|Aç" }.Concat(back != null ? new[] { "Esc|Geri" } : Array.Empty<string>()).ToArray())
        {
            var parts = h.Split('|');
            foot.Children.Add(KeyHint(parts[0], parts[1]));
        }
        Grid.SetRow(foot, 2);
        layout.Children.Add(foot);
        root.Children.Add(layout);

        if (back != null)
            root.KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Sfx.Play("lock_in"); back(); } };
        root.Loaded += (_, _) => { if (!root.IsKeyboardFocusWithin) root.Focus(); };
        return root;
    }

    private static UIElement BackButton(Action back)
    {
        var tb = new TextBlock { Text = "‹", FontSize = 40 * Theme.FontScale, FontFamily = Theme.Serif, Foreground = Theme.B("#c9a24a"), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, -6, 0, 0) };
        var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(6, 2, 18, 2), Cursor = Cursors.Hand, Child = tb, Tag = "hub-back", ToolTip = "Geri (Esc)" };
        host.MouseEnter += (_, _) => tb.Foreground = Theme.B("#fff2c8");
        host.MouseLeave += (_, _) => tb.Foreground = Theme.B("#c9a24a");
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); back(); };
        return host;
    }

    private static UIElement Crumbs(IReadOnlyList<(string Text, Action? Go)> crumbs)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < crumbs.Count; i++)
        {
            var last = i == crumbs.Count - 1;
            var (text, go) = crumbs[i];
            if (i > 0) row.Children.Add(new TextBlock { Text = "›", FontSize = 26 * Theme.FontScale, FontFamily = Theme.Serif, Foreground = Theme.GoldDim, Margin = new Thickness(12, 0, 12, 2), VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBlock
            {
                Text = text, FontFamily = Theme.Serif, FontSize = (last ? 36 : 24) * Theme.FontScale, VerticalAlignment = VerticalAlignment.Center,
                Foreground = last ? Theme.B("#f4e6bf") : Theme.B("#9a9078"), FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal
            };
            if (last) tb.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.95 };
            if (!last && go != null)
            {
                var host = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = tb, Tag = "hub-crumb" };
                host.MouseEnter += (_, _) => tb.Foreground = Theme.B("#fff2c8");
                host.MouseLeave += (_, _) => tb.Foreground = Theme.B("#9a9078");
                host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); go(); };
                row.Children.Add(host);
            }
            else row.Children.Add(tb);
        }
        return row;
    }

    /// <summary>Sağ üstte portre + takma ad + Crown; tıklayınca profil açılır.</summary>
    public static UIElement ProfileChip(Action open)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Portraits.Medallion(Settings.Portrait, 60, glow: true));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var nick = Ui.Text(string.IsNullOrWhiteSpace(Settings.Nick) ? "Oyuncu" : Settings.Nick, 22, Theme.B("#f0d890"), true);
        nick.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.95 };
        texts.Children.Add(nick);
        texts.Children.Add(Ui.Text($"◆ {Settings.Crowns} Crown", 15, Theme.TextDim));
        row.Children.Add(texts);
        var host = new Border { Child = row, Padding = new Thickness(10, 4, 14, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = "profile-chip", Opacity = 0.92, ToolTip = "Profil" };
        host.MouseEnter += (_, _) => host.Opacity = 1;
        host.MouseLeave += (_, _) => host.Opacity = 0.92;
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); open(); };
        return host;
    }

    private static UIElement KeyHint(string key, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border
        {
            BorderBrush = Theme.B("#8a7236"), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 1, 8, 2),
            Background = Theme.B("#55000000"), Child = Ui.Text(key, 14, Theme.B("#e8c970"), true), VerticalAlignment = VerticalAlignment.Center
        });
        var l = Ui.Text(label, 15, Theme.B("#a39880"));
        l.Margin = new Thickness(9, 0, 0, 0);
        l.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(l);
        return row;
    }

    /// <summary>Küçük metin bağlantısı (ana merkezin alt şeridi).</summary>
    public static UIElement TextLink(string text, Action click)
    {
        var tb = new TextBlock { Text = text, FontFamily = Theme.Serif, FontSize = 20 * Theme.FontScale, Foreground = Theme.B("#a89e86"), IsHitTestVisible = false };
        var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Child = tb, Tag = "menu-item" };
        host.MouseEnter += (_, _) => { tb.Foreground = Theme.B("#fff2c8"); tb.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 }; };
        host.MouseLeave += (_, _) => { tb.Foreground = Theme.B("#a89e86"); tb.Effect = null; };
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); click(); };
        return host;
    }
}
