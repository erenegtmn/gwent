using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GwentClient;

/// <summary>Altın çubuklu ses seviyesi kaydırıcısı (0-100). Sürüklerken <see cref="Changed"/>, bırakınca <see cref="Committed"/> tetiklenir.</summary>
public sealed class VolumeBar : Canvas
{
    private const double TrackW = 320, H = 34;
    private readonly Rectangle _fill = new() { Height = 6, RadiusX = 3, RadiusY = 3, Fill = Theme.B("#c9a24a"), IsHitTestVisible = false };
    private readonly Ellipse _thumb = new() { Width = 22, Height = 22, Fill = Theme.B("#f0d890"), Stroke = Theme.B("#6a5420"), StrokeThickness = 2, IsHitTestVisible = false };
    private int _value;
    public event Action<int>? Changed;
    public event Action<int>? Committed;

    public VolumeBar(int value)
    {
        Width = TrackW + 22;
        Height = H;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Children.Add(new Rectangle { Width = TrackW, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Theme.B("#3a2e1a"), IsHitTestVisible = false });
        SetLeft(Children[0], 11);
        SetTop(Children[0], H / 2 - 3);
        Children.Add(_fill);
        SetLeft(_fill, 11);
        SetTop(_fill, H / 2 - 3);
        Children.Add(_thumb);
        SetTop(_thumb, H / 2 - 11);
        Set(value, false);

        MouseLeftButtonDown += (_, e) => { CaptureMouse(); FromMouse(e); };
        MouseMove += (_, e) => { if (IsMouseCaptured) FromMouse(e); };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); Committed?.Invoke(_value); } };
        MouseWheel += (_, e) => { Set(_value + (e.Delta > 0 ? 5 : -5), true); Committed?.Invoke(_value); };
    }

    private void FromMouse(MouseEventArgs e) => Set((int)Math.Round((e.GetPosition(this).X - 11) / TrackW * 100), true);

    private void Set(int v, bool raise)
    {
        v = Math.Clamp(v, 0, 100);
        _value = v;
        _fill.Width = TrackW * v / 100.0;
        SetLeft(_thumb, 11 + TrackW * v / 100.0 - 11);
        if (raise) Changed?.Invoke(v);
    }
}

/// <summary>Ayarlar: ses ve müzik düzeyi, tam ekran. Menü ve deste kurucuyla aynı görünüm dili.</summary>
public sealed class SettingsView : Grid
{
    private readonly Action _back;

    public SettingsView(Action onBack, bool embedded = false)
    {
        _back = onBack;
        Background = embedded ? Brushes.Transparent : Theme.Bg;   // oyun içinde: oyun alanının üstünde yüzer
        ClipToBounds = true;

        var root = new StackPanel { Width = 760 };
        root.Children.Add(Ornate.Heading("Ayarlar", "Ses, müzik ve görüntü"));
        root.Children.Add(Ornate.Rule(560, 16));
        ((FrameworkElement)root.Children[^1]).HorizontalAlignment = HorizontalAlignment.Left;

        root.Children.Add(VolumeRow("Ses efektleri", () => Sfx.Enabled, v => Sfx.Enabled = v, () => Settings.SfxVolume,
            v => Settings.SfxVolume = v, () => Sfx.Play("lock_in")));
        root.Children.Add(VolumeRow("Müzik", () => Music.Enabled, v => { if (v != Music.Enabled) Music.Toggle(); }, () => Settings.MusicVolume,
            v => { Settings.MusicVolume = v; Music.ApplyVolume(); }, null));
        root.Children.Add(ToggleRow("Tam ekran", "F11 ile de değiştirebilirsin", () => Settings.Fullscreen, v =>
        {
            Settings.Fullscreen = v;
            if (Application.Current.MainWindow is { } w) Settings.ApplyWindow(w);
        }));

        var backBtn = Ui.Button("Geri", () => { Settings.Save(); _back(); }, primary: true);
        backBtn.FontSize = 20;
        backBtn.Padding = new Thickness(44, 12, 44, 12);
        backBtn.HorizontalAlignment = HorizontalAlignment.Left;
        backBtn.Margin = new Thickness(0, 26, 0, 0);
        root.Children.Add(backBtn);

        var panel = Ornate.Panel(root, new Thickness(54, 42, 54, 46));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        if (embedded) Children.Add(panel);
        else Children.Add(Ornate.Stage(panel));
        Ornate.Reveal(root.Children.Cast<UIElement>());
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Settings.Save(); _back(); } };
        Focusable = true;
        Loaded += (_, _) => Focus();
    }

    private static Grid Row(string label, string? sub)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 62 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Ui.Text(label, 26, Theme.Text, true));
        if (sub != null) sp.Children.Add(Ui.Text(sub, 14, Theme.TextDim));
        g.Children.Add(sp);
        return g;
    }

    private static Button OnOff(Func<bool> get, Action<bool> set)
    {
        var b = Ui.Button(get() ? "Açık" : "Kapalı", () => { }, primary: false);
        b.MinWidth = 90;
        b.FontSize = 18;
        b.Click += (_, _) => { set(!get()); b.Content = get() ? "Açık" : "Kapalı"; Settings.Save(); Sfx.Play("lock_in"); };
        b.Margin = new Thickness(0, 0, 18, 0);
        return b;
    }

    private static UIElement VolumeRow(string label, Func<bool> enabled, Action<bool> setEnabled, Func<int> volume, Action<int> setVolume, Action? sample)
    {
        var row = Row(label, null);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(OnOff(enabled, setEnabled));
        var bar = new VolumeBar(volume());
        var pct = Ui.Text($"{volume()}%", 18, Theme.TextDim, true, TextAlignment.Right);
        pct.Width = 56;
        pct.VerticalAlignment = VerticalAlignment.Center;
        bar.Changed += v => { setVolume(v); pct.Text = $"{v}%"; };
        bar.Committed += _ => { Settings.Save(); sample?.Invoke(); };
        right.Children.Add(bar);
        right.Children.Add(pct);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        return row;
    }

    private static UIElement ToggleRow(string label, string sub, Func<bool> get, Action<bool> set)
    {
        var row = Row(label, sub);
        var b = OnOff(get, set);
        b.Margin = new Thickness(0);
        Grid.SetColumn(b, 1);
        row.Children.Add(b);
        return row;
    }
}
