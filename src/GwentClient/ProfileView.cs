using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>
/// Profil: takma ad, portre seçimi ve bu bilgisayardaki maç istatistikleri. Hepsi yalnız yerelde (%AppData%\Gwent) tutulur;
/// hiçbir yere gönderilmez. Online'da rakibe yalnız takma ad ve portre kimliği gider.
/// </summary>
public sealed class ProfileView : Grid
{
    private readonly Action _back;
    private readonly TextBox _nick = new() { MaxLength = 16, Tag = "profile-nick" };
    private readonly Border _big = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _portraitName = Ui.Text("", 16, Theme.TextDim, false, TextAlignment.Center);
    private readonly List<(string Id, Border Cell)> _cells = new();
    private readonly TextBlock _error = Ui.Text("", 15, Theme.Bad, false, TextAlignment.Center);
    private readonly bool _persist;

    public ProfileView(Action onBack, IReadOnlyList<MatchRecord>? records = null, bool persist = true)
    {
        _back = onBack;
        _persist = persist;
        var all = records ?? Stats.Load();

        // ---- Sol: kimlik ----
        var left = new StackPanel { Width = 560 };
        left.Children.Add(Ornate.Heading("Profil", "Yalnızca bu bilgisayarda saklanır", 44));
        var rule = Ornate.Rule(480, 14);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(rule);

        var id = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _big.Child = Portraits.Medallion(Settings.Portrait, 150, glow: true);
        id.Children.Add(_big);
        var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
        nameBox.Children.Add(Ornate.Caption("Takma adın", 0));
        _nick.Style = (Style)Application.Current.FindResource("GText");
        _nick.FontFamily = Theme.Serif;
        _nick.FontSize = 22;
        _nick.Width = 300;
        _nick.Text = Settings.Nick;
        nameBox.Children.Add(_nick);
        _portraitName.HorizontalAlignment = HorizontalAlignment.Left;
        _portraitName.TextAlignment = TextAlignment.Left;
        _portraitName.Margin = new Thickness(2, 10, 0, 0);
        nameBox.Children.Add(_portraitName);
        id.Children.Add(nameBox);
        left.Children.Add(id);

        left.Children.Add(Ornate.Caption("Portre seç", 22));
        var wrap = new WrapPanel { Width = 540 };
        foreach (var pid in Portraits.All())
        {
            var key = pid;
            var cell = new Border
            {
                Child = Portraits.Medallion(pid, 76), Margin = new Thickness(2), Padding = new Thickness(2), Cursor = Cursors.Hand,
                Background = Brushes.Transparent, Tag = "profile-portrait:" + pid, ToolTip = Portraits.Title(pid)
            };
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(key); Sfx.Play("cursor_movement"); };
            _cells.Add((pid, cell));
            wrap.Children.Add(cell);
        }
        left.Children.Add(wrap);
        _error.Margin = new Thickness(0, 12, 0, 0);
        left.Children.Add(_error);
        SyncSelection();

        // ---- Sağ: istatistikler ----
        var right = new StackPanel { Width = 640, Margin = new Thickness(54, 0, 0, 0) };
        right.Children.Add(Ornate.Heading("İstatistikler", null, 30));
        var rule2 = Ornate.Rule(500, 12);
        rule2.HorizontalAlignment = HorizontalAlignment.Left;
        right.Children.Add(rule2);
        AddStats(right, all);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 28, 0, 0) };
        var back = Ui.Button("Kaydet ve dön", () => Leave(), primary: true);
        back.FontSize = 20;
        back.Padding = new Thickness(40, 12, 40, 12);
        back.Tag = "profile-back";
        bar.Children.Add(back);
        if (all.Count > 0)
        {
            var clear = Ui.Button("İstatistikleri sil", () =>
            {
                if (MessageBox.Show("Tüm maç kayıtları silinsin mi?", "Gwent", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                Stats.Clear();
                Leave(force: true);
            });
            clear.FontSize = 16;
            clear.Margin = new Thickness(20, 0, 0, 0);
            bar.Children.Add(clear);
        }
        right.Children.Add(bar);

        var cols = new StackPanel { Orientation = Orientation.Horizontal };
        cols.Children.Add(left);
        cols.Children.Add(new Border { Width = 1.5, Background = Ornate.GoldThin, Margin = new Thickness(0, 6, 0, 6) });
        right.Margin = new Thickness(46, 0, 0, 0);
        cols.Children.Add(right);

        var panel = Ornate.Panel(cols, new Thickness(54, 42, 54, 46));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Ornate.Stage(panel));
        Ornate.Reveal(new UIElement[] { left, right }, 26, 40, 140, 560);

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Leave(); };
        Focusable = true;
        Loaded += (_, _) => Focus();
    }

    private void Choose(string id)
    {
        Settings.Portrait = id;
        SyncSelection();
    }

    private void SyncSelection()
    {
        _big.Child = Portraits.Medallion(Settings.Portrait, 150, glow: true);
        _portraitName.Text = Portraits.Title(Settings.Portrait);
        foreach (var (id, cell) in _cells)
        {
            var sel = id == Settings.Portrait;
            cell.Opacity = sel ? 1 : 0.62;
            cell.Effect = sel ? new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.95 } : null;
        }
    }

    private void Leave(bool force = false)
    {
        var nick = _nick.Text.Trim();
        if (!force && nick.Length > 0 && nick.Length is < 2 or > 16) { _error.Text = "Takma ad 2-16 karakter olmalı."; return; }
        if (nick.Length >= 2) Settings.Nick = nick;
        if (_persist) Settings.Save();
        _back();
    }

    private static void AddStats(StackPanel root, IReadOnlyList<MatchRecord> all)
    {
        if (all.Count == 0)
        {
            root.Children.Add(Ui.Text("Henüz biten maç yok. Bir maç oyna, sonuçlar burada görünür.", 20, Theme.TextDim, wrap: true));
            return;
        }
        var s = Stats.Summarize(all);
        root.Children.Add(Ui.Text($"{s.Games} maç — {s.Wins} galibiyet, {s.Draws} beraberlik, {s.Losses} yenilgi (%{s.WinPercent})", 22, Theme.Text, true));
        root.Children.Add(Ui.Text($"Güncel seri: {s.Streak}   En iyi seri: {s.BestStreak}", 17, Theme.TextDim));

        void Section(string heading, IEnumerable<IGrouping<string, MatchRecord>> groups, Func<IGrouping<string, MatchRecord>, string> label)
        {
            root.Children.Add(Ornate.Caption(heading, 18));
            foreach (var g in groups)
            {
                var gs = Stats.Summarize(g);
                root.Children.Add(Ui.Text($"{label(g)}:  {gs.Wins}–{gs.Draws}–{gs.Losses}  (%{gs.WinPercent})", 16, Theme.Text));
            }
        }
        Section("Rakibe göre", all.GroupBy(r => r.Opponent).OrderByDescending(g => g.Count()).Take(6), g => g.Key);
        Section("Fraksiyonuna göre", all.GroupBy(r => r.MyFaction).OrderByDescending(g => g.Count()), g => Texts.Faction(g.Key));

        root.Children.Add(Ornate.Caption("Son maçlar", 18));
        foreach (var r in all.AsEnumerable().Reverse().Take(8))
        {
            var res = r.Result > 0 ? "Kazandın" : r.Result < 0 ? "Kaybettin" : "Berabere";
            root.Children.Add(Ui.Text($"{r.Date:dd.MM.yyyy HH:mm}   {res}   {r.Opponent}   ({Texts.FactionShort(r.MyFaction)} – {Texts.FactionShort(r.OppFaction)}){(r.Online ? "   online" : "")}", 15,
                r.Result > 0 ? Theme.B("#d8bd78") : r.Result < 0 ? Theme.B("#a8503f") : Theme.TextDim));
        }
    }
}
