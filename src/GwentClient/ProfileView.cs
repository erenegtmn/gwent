using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>
/// Profil: takma ad, portre seçimi ve bu bilgisayardaki maç istatistikleri. Hepsi yalnız yerelde (%AppData%\Gwent) tutulur;
/// hiçbir yere gönderilmez. Online'da rakibe yalnız takma ad ve portre kimliği gider.
/// Düzen (ARAYUZ.md): merkez çerçevesi; solda kimlik kartı + portre galerisi, sağda özet rakamlar, çubuklu tablolar, son maçlar.
/// </summary>
public sealed class ProfileView : Grid
{
    private readonly Action _back;
    private readonly TextBox _nick = new() { MaxLength = 16, Tag = "profile-nick" };
    private readonly Border _big = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _portraitName = Ui.Text("", 16, Theme.TextDim, false, TextAlignment.Center);
    private readonly List<(string Id, Border Cell)> _cells = new();
    private readonly TextBlock _error = Ui.Text("", 15, Theme.Bad, false, TextAlignment.Left);
    private readonly bool _persist;

    private static readonly Brush WinBrush = Theme.B("#d8bd78"), LossBrush = Theme.B("#b8604f"), DrawBrush = Theme.B("#8a8070");

    public ProfileView(Action onBack, IReadOnlyList<MatchRecord>? records = null, bool persist = true)
    {
        _back = onBack;
        _persist = persist;
        var all = records ?? Stats.Load();

        // ---- Sol: kimlik kartı + portre galerisi ----
        var left = new StackPanel();
        var id = new StackPanel { Orientation = Orientation.Horizontal };
        _big.Child = Portraits.Medallion(Settings.Portrait, 170, glow: true);
        id.Children.Add(_big);
        var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
        nameBox.Children.Add(Ornate.Caption("Takma adın", 0));
        _nick.Style = (Style)Application.Current.FindResource("GText");
        _nick.FontFamily = Theme.Serif;
        _nick.FontSize = 24;
        _nick.Width = 320;
        _nick.Text = Settings.Nick;
        nameBox.Children.Add(_nick);
        _portraitName.HorizontalAlignment = HorizontalAlignment.Left;
        _portraitName.TextAlignment = TextAlignment.Left;
        _portraitName.Margin = new Thickness(2, 10, 0, 0);
        nameBox.Children.Add(_portraitName);
        var crownsLine = Ui.Text($"◆ {Settings.Crowns} Crown", 22, Theme.B("#f0d890"), true);
        crownsLine.Margin = new Thickness(2, 6, 0, 0);
        nameBox.Children.Add(crownsLine);
        id.Children.Add(nameBox);
        left.Children.Add(id);
        var privacy = Ui.Text("Takma ad ve portre yalnız bu bilgisayarda saklanır; online maçta rakibin yalnız bunları görür.", 15, Theme.TextDim, false, TextAlignment.Left, true);
        privacy.Margin = new Thickness(0, 16, 0, 0);
        left.Children.Add(privacy);
        left.Children.Add(Ornate.Caption("Portre seç", 24));
        var wrap = new WrapPanel();
        foreach (var pid in Portraits.All())
        {
            var key = pid;
            var cell = new Border
            {
                Child = Portraits.Medallion(pid, 78), Margin = new Thickness(2), Padding = new Thickness(2), Cursor = Cursors.Hand,
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
        var right = new StackPanel();
        right.Children.Add(Ui.Text("İstatistikler", 32, Theme.B("#f0d890"), true));
        AddStats(right, all);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
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

        var lp = Ornate.Panel(left, new Thickness(48, 38, 48, 40), 20, 54);
        var rp = Ornate.Panel(right, new Thickness(48, 34, 48, 40), 20, 54);
        var cols = new Grid { MaxWidth = 1640, Margin = new Thickness(44, 16, 44, 30), VerticalAlignment = VerticalAlignment.Top };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        lp.Margin = new Thickness(0, 0, 14, 0);
        rp.Margin = new Thickness(14, 0, 0, 0);
        lp.VerticalAlignment = VerticalAlignment.Top;
        rp.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(rp, 1);
        cols.Children.Add(lp);
        cols.Children.Add(rp);

        var backdrop = new HubBackdrop(Ornate.Animate);
        backdrop.Show(HubArt.Wide("gwent", "pano_kaer_morhen"));
        var screen = HubKit.Screen(new ScrollViewer { Content = cols, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, backdrop,
            new (string, Action?)[] { ("Profil", null) }, () => Leave(), null, hints: new[] { "Esc|Kaydet ve dön" });
        Children.Add(screen);
        Ornate.Reveal(new UIElement[] { lp, rp }, 26, 40, 140, 560);
    }

    private void Choose(string id)
    {
        Settings.Portrait = id;
        SyncSelection();
    }

    private void SyncSelection()
    {
        _big.Child = Portraits.Medallion(Settings.Portrait, 170, glow: true);
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

    /// <summary>Özet rakamlar (büyük), rakibe/fraksiyona göre galibiyet-beraberlik-yenilgi çubukları ve son maçlar.</summary>
    private static void AddStats(StackPanel root, IReadOnlyList<MatchRecord> all)
    {
        if (all.Count == 0)
        {
            var empty = Ui.Text("Henüz biten maç yok. Bir maç oyna; sonuçların, serilerin ve rakiplerine karşı durumun burada görünür.", 20, Theme.TextDim, wrap: true);
            empty.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(empty);
            return;
        }
        var s = Stats.Summarize(all);

        var tiles = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 16, 0, 6) };
        void Tile(string value, string label, bool accent)
        {
            var sp = new StackPanel();
            var v = Ui.Text(value, accent ? 52 : 40, accent ? Theme.B("#f0d890") : Theme.Text, true);
            v.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.9 };
            sp.Children.Add(v);
            sp.Children.Add(Ui.Text(label, 15, Theme.TextDim, false, TextAlignment.Left, true));
            tiles.Children.Add(new Border { Child = sp, BorderBrush = Theme.B("#55c9a24a"), BorderThickness = new Thickness(1.5, 0, 0, 0), Padding = new Thickness(16, 0, 10, 0) });
        }
        Tile($"%{s.WinPercent}", "kazanma oranı", true);
        Tile($"{s.Games}", $"maç: {s.Wins} galibiyet, {s.Draws} beraberlik, {s.Losses} yenilgi", false);
        Tile($"{s.Streak}", "güncel seri", false);
        Tile($"{s.BestStreak}", "en iyi seri", false);
        root.Children.Add(tiles);

        StackPanel Table(string heading, IEnumerable<IGrouping<string, MatchRecord>> groups, Func<string, string> label)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ornate.Caption(heading, 18));
            foreach (var g in groups)
            {
                var gs = Stats.Summarize(g);
                var row = new Grid { Margin = new Thickness(0, 3, 0, 6) };
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Ui.Text(label(g.Key), 17, Theme.Text);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(name);
                var score = Ui.Text($"{gs.Wins}–{gs.Draws}–{gs.Losses}", 17, Theme.TextDim, true);
                SetColumn(score, 1);
                row.Children.Add(score);
                var bars = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
                var parts = new[] { (gs.Wins, WinBrush), (gs.Draws, DrawBrush), (gs.Losses, LossBrush) };
                for (var i = 0; i < parts.Length; i++)
                {
                    bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(parts[i].Item1, 0.0001), GridUnitType.Star) });
                    var r = new Border { Background = parts[i].Item2, Margin = new Thickness(0, 0, parts[i].Item1 > 0 ? 1 : 0, 0) };
                    SetColumn(r, i);
                    bars.Children.Add(r);
                }
                SetRow(bars, 1);
                SetColumnSpan(bars, 2);
                row.Children.Add(bars);
                sp.Children.Add(row);
            }
            return sp;
        }
        var tables = new Grid();
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var byOpp = Table("Rakibe göre", all.GroupBy(r => r.Opponent).OrderByDescending(g => g.Count()).Take(6), k => k);
        var byFaction = Table("Fraksiyonuna göre", all.GroupBy(r => r.MyFaction).OrderByDescending(g => g.Count()), Texts.Faction);
        SetColumn(byFaction, 2);
        tables.Children.Add(byOpp);
        tables.Children.Add(byFaction);
        root.Children.Add(tables);

        root.Children.Add(Ornate.Caption("Son maçlar", 22));
        foreach (var r in all.AsEnumerable().Reverse().Take(8))
        {
            var (res, brush) = r.Result > 0 ? ("Galibiyet", WinBrush) : r.Result < 0 ? ("Yenilgi", LossBrush) : ("Berabere", DrawBrush);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border
            {
                BorderBrush = brush, BorderThickness = new Thickness(1.2), Padding = new Thickness(8, 1, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(res, 14, brush, true)
            });
            var mid = Ui.Text($"{r.Opponent}    {Texts.FactionShort(r.MyFaction)} – {Texts.FactionShort(r.OppFaction)}{(r.Online ? "    online" : "")}", 16, Theme.Text);
            mid.VerticalAlignment = VerticalAlignment.Center;
            mid.TextTrimming = TextTrimming.CharacterEllipsis;
            SetColumn(mid, 1);
            row.Children.Add(mid);
            var date = Ui.Text($"{r.Date:dd.MM.yyyy HH:mm}", 14, Theme.TextDim);
            date.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(date, 2);
            row.Children.Add(date);
            root.Children.Add(row);
        }
    }
}
