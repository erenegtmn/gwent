using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentNet;

namespace GwentClient;

/// <summary>
/// Turnuva bracket ekranı: tek eleme ağacı (portreler, adlar, galipler), benim sıradaki maçım ve "Maça hazırım" düğmesi.
/// Maç bitince oyun ekranından buraya dönülür; turnuva bitince şampiyon, sıralamam ve ödülüm (taç) gösterilir.
/// </summary>
public sealed class BracketView : Grid
{
    private const double CardW = 360, CardH = 118, ColGap = 96, Unit = 150;
    private static readonly TimeSpan ClaimAfter = TimeSpan.FromMinutes(2);

    private readonly OnlineController _c;
    private readonly Func<Task> _leave;
    private readonly Action _toMenu;
    private readonly Border _host = new();
    private bool _leaving, _settled;
    private int _prize;
    private DateTime? _oppGoneSince;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private string _msg = "";

    public BracketView(OnlineController controller, Func<Task> leave, Action toMenu)
    {
        _c = controller;
        _leave = leave;
        _toMenu = toMenu;
        Children.Add(Ornate.Stage(_host, "pano_novigrad"));
        _c.Link.TournamentChanged += OnState;
        Unloaded += (_, _) => { _c.Link.TournamentChanged -= OnState; _timer?.Stop(); };
        Rebuild();
    }

    private TournamentState? St => _c.Link.TState;
    private void OnState() => Rebuild();

    private async void LeaveClicked()
    {
        if (_leaving) return;
        _leaving = true;
        await _leave();
    }

    private async void ReadyClicked(bool ready)
    {
        var r = await _c.Link.TournamentReadyAsync(ready);
        _msg = r.Ok ? "" : r.Error ?? "";
        Rebuild();
    }

    private async void WalkoverClicked()
    {
        var r = await _c.Link.TournamentWalkoverAsync();
        _msg = r.Ok ? "" : r.Error ?? "";
        Rebuild();
    }

    private TMatchInfo? MyMatch(TournamentState st) =>
        st.YourMatchRound is { } r && st.YourMatchIndex is { } i ? st.Matches.FirstOrDefault(m => m.Round == r && m.Index == i) : null;

    private static string RoundName(int round, int rounds) => (rounds - round) switch { 1 => "FİNAL", 2 => "YARI FİNAL", 3 => "ÇEYREK FİNAL", _ => $"TUR {round + 1}" };

    private void Rebuild()
    {
        var st = St;
        var root = new Grid { Margin = new Thickness(60, 40, 60, 40) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (st == null) { _host.Child = root; return; }

        // --- Üst ---
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ornate.Heading("Turnuva", $"Kod {st.Code} · {st.Players.Count} oyuncu" + (st.EntryFee > 0 ? $" · ödül havuzu {st.EntryFee * st.Players.Count} taç" : ""), 44));
        var crowns = Ui.Text($"◆ {Settings.Crowns} taç", 22, Theme.B("#f0d890"), true);
        crowns.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(crowns, 1);
        head.Children.Add(crowns);
        root.Children.Add(head);

        // --- Orta: bracket ---
        var rounds = st.Matches.Max(m => m.Round) + 1;
        var leaves = st.Matches.Count(m => m.Round == 0);
        var canvas = new Canvas { Width = rounds * CardW + (rounds - 1) * ColGap, Height = leaves * Unit + 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mine = MyMatch(st);
        foreach (var m in st.Matches)
        {
            var cy = 28 + Unit * (m.Index + 0.5) * (1 << m.Round);
            var x = m.Round * (CardW + ColGap);
            if (m.Index == 0)
            {
                var title = Ui.Text(Ornate.Spaced(RoundName(m.Round, rounds)), 13, Theme.B("#a8905a"), true, TextAlignment.Center);
                title.Width = CardW;
                Canvas.SetLeft(title, x);
                Canvas.SetTop(title, -6);
                canvas.Children.Add(title);
            }
            var card = MatchCard(st, m, mine != null && mine.Round == m.Round && mine.Index == m.Index);
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, cy - CardH / 2);
            canvas.Children.Add(card);
            if (m.Round < rounds - 1)
            {
                var ny = 28 + Unit * (m.Index / 2 + 0.5) * (1 << (m.Round + 1));
                var x0 = x + CardW;
                var x1 = x + CardW + ColGap;
                var pathGeo = new PathGeometry(new[] { new PathFigure(new Point(x0, cy), new PathSegment[]
                {
                    new LineSegment(new Point(x0 + ColGap / 2, cy), true),
                    new LineSegment(new Point(x0 + ColGap / 2, ny), true),
                    new LineSegment(new Point(x1, ny), true)
                }, false) });
                canvas.Children.Add(new System.Windows.Shapes.Path { Data = pathGeo, Stroke = m.Winner != null ? Theme.B("#c9a24a") : Theme.B("#5a4a2a"), StrokeThickness = m.Winner != null ? 2.2 : 1.5, IsHitTestVisible = false });
            }
        }
        Grid.SetRow(canvas, 1);
        root.Children.Add(canvas);

        // --- Alt: durum + düğmeler ---
        var foot = BuildFooter(st, mine);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        _host.Child = root;
    }

    private UIElement MatchCard(TournamentState st, TMatchInfo m, bool isMine)
    {
        var g = new Grid { Width = CardW, Height = CardH };
        var edge = isMine ? Ornate.GoldEdge : m.Status == "playing" ? Theme.B("#c98a3a") : Theme.B("#4a3b28");
        g.Children.Add(new ChamferFrame { Fill = Theme.B("#1c140c"), Edge = edge, Inner = isMine ? Ornate.GoldThin : null, Cut = 12, Inset = 4 });
        if (isMine) g.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.6 };
        var rows = new StackPanel { Margin = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        rows.Children.Add(PlayerRow(st, m, m.A, m.ReadyA));
        rows.Children.Add(new Border { Height = 1, Background = Theme.B("#3a2e1a"), Margin = new Thickness(0, 3, 0, 3) });
        rows.Children.Add(PlayerRow(st, m, m.B, m.ReadyB));
        g.Children.Add(rows);
        if (m.Status == "playing")
        {
            var live = Ui.Text("● OYNANIYOR", 11, Theme.B("#e8a050"), true);
            live.HorizontalAlignment = HorizontalAlignment.Right;
            live.VerticalAlignment = VerticalAlignment.Top;
            live.Margin = new Thickness(0, 5, 14, 0);
            g.Children.Add(live);
        }
        return g;
    }

    private UIElement PlayerRow(TournamentState st, TMatchInfo m, int? idx, bool ready)
    {
        var row = new Grid { Height = 46 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (idx is not { } i || i >= st.Players.Count)
        {
            // henüz belli değil / bye
            var dash = Ui.Text(m.Winner != null && m.A != null && m.B == null ? "bye" : "—", 18, Theme.B("#6a604c"));
            dash.FontStyle = FontStyles.Italic;
            dash.VerticalAlignment = VerticalAlignment.Center;
            dash.Margin = new Thickness(54, 0, 0, 0);
            Grid.SetColumnSpan(dash, 3);
            row.Children.Add(dash);
            return row;
        }
        var p = st.Players[i];
        var won = m.Winner == i;
        var lost = m.Winner != null && !won;
        var medal = Portraits.Medallion(p.Avatar ?? p.Loadout?.Avatar ?? p.Loadout?.LeaderId, 40);
        medal.Margin = new Thickness(0, 0, 10, 0);
        medal.VerticalAlignment = VerticalAlignment.Center;
        medal.Opacity = lost ? 0.45 : 1;
        row.Children.Add(medal);
        var nm = Ui.Text(p.Nick + (i == st.YourIndex ? "  (sen)" : ""), 20, won ? Theme.B("#f4dc98") : lost ? Theme.B("#6f6552") : Theme.B("#e0d4b8"), won || i == st.YourIndex);
        nm.VerticalAlignment = VerticalAlignment.Center;
        nm.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(nm, 1);
        row.Children.Add(nm);
        var mark = Ui.Text(won ? "✓" : ready && m.Winner == null ? "●" : !p.Connected && m.Winner == null ? "⚠" : "", 20, won ? Theme.B("#f0d890") : ready ? Theme.B("#7fc18a") : Theme.Bad, true);
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.ToolTip = ready ? "maça hazır" : !p.Connected ? "bağlantısı koptu" : null;
        Grid.SetColumn(mark, 2);
        row.Children.Add(mark);
        return row;
    }

    private UIElement BuildFooter(TournamentState st, TMatchInfo? mine)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var me = st.YourIndex >= 0 && st.YourIndex < st.Players.Count ? st.Players[st.YourIndex] : null;
        string text;
        Brush color = Theme.B("#e8dcc4");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };

        if (st.Phase == TournamentPhase.Finished)
        {
            if (!_settled) { _settled = true; _prize = TournamentEconomy.Settle(st); }
            var champ = st.Players.FirstOrDefault(p => p.Placement == 1);
            var place = me?.Placement ?? 0;
            text = place == 1 ? "Şampiyon sensin!" : champ != null ? $"Şampiyon: {champ.Nick}" : "Turnuva bitti.";
            if (place > 0 && place != 1) text += $"   ·   Sıralaman: {place}.";
            if (_prize > 0) text += $"   ·   Ödül: +{_prize} taç";
            color = place == 1 ? Theme.B("#f4dc98") : Theme.B("#e8dcc4");
            var back = Ui.Button("Menüye dön", () => LeaveClicked(), primary: true);
            back.FontSize = 22;
            back.Padding = new Thickness(40, 12, 40, 12);
            back.Tag = "bracket-menu";
            buttons.Children.Add(back);
        }
        else if (me == null || me.Eliminated)
        {
            text = me is { Placement: > 0 } ? $"Elendin ({me.Placement}. sıra). Turnuvayı izleyebilirsin." : "Turnuvayı izliyorsun.";
            var leave = Ui.Button("Turnuvadan ayrıl", () => LeaveClicked());
            leave.FontSize = 18;
            buttons.Children.Add(leave);
        }
        else if (mine == null || mine.A == null || mine.B == null)
        {
            text = "Rakibin henüz belli değil; diğer maçlar bekleniyor…";
            buttons.Children.Add(LeaveButton());
        }
        else
        {
            var oppIdx = mine.A == st.YourIndex ? mine.B.Value : mine.A.Value;
            var opp = st.Players[oppIdx];
            var myReady = mine.A == st.YourIndex ? mine.ReadyA : mine.ReadyB;
            if (mine.Status == "playing")
            {
                text = $"Maçın sürüyor: {opp.Nick}";
            }
            else if (!myReady)
            {
                text = $"Sıradaki maçın: {opp.Nick}. Hazır olunca düğmeye bas.";
                var go = Ui.Button("Maça hazırım", () => ReadyClicked(true), primary: true);
                go.FontSize = 26;
                go.Padding = new Thickness(60, 12, 60, 12);
                go.Tag = "bracket-ready";
                buttons.Children.Add(go);
            }
            else
            {
                text = opp.Connected ? $"{opp.Nick} bekleniyor…" : $"{opp.Nick} bağlantısı koptu; dönmesi bekleniyor…";
                var wait = Ui.Button("Hazır değilim", () => ReadyClicked(false));
                wait.FontSize = 18;
                buttons.Children.Add(wait);
            }
            if (!opp.Connected && mine.Status != "playing")
            {
                _oppGoneSince ??= DateTime.UtcNow;
                var left = ClaimAfter - (DateTime.UtcNow - _oppGoneSince.Value);
                var claim = Ui.Button(left > TimeSpan.Zero ? $"Hükmen galibiyet ({(int)left.TotalMinutes}:{left.Seconds:00})" : "Hükmen galibiyeti al", () => WalkoverClicked(), enabled: left <= TimeSpan.Zero);
                claim.FontSize = 16;
                claim.Margin = new Thickness(14, 0, 0, 0);
                claim.Tag = "bracket-walkover";
                buttons.Children.Add(claim);
                _timer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick -= TimerTick;
                _timer.Tick += TimerTick;
                _timer.Start();
            }
            else { _oppGoneSince = null; _timer?.Stop(); }
            if (mine.Status != "playing") buttons.Children.Add(LeaveButton());
        }

        var t = Ui.Text(text, 24, color, true, TextAlignment.Center, true);
        t.MaxWidth = 1200;
        sp.Children.Add(t);
        if (_msg != "") sp.Children.Add(Ui.Text(_msg, 16, Theme.Bad, false, TextAlignment.Center));
        sp.Children.Add(buttons);
        return sp;
    }

    private void TimerTick(object? sender, EventArgs e) => Rebuild();

    private Button LeaveButton()
    {
        var leave = Ui.Button("Turnuvadan ayrıl", () => LeaveClicked());
        leave.FontSize = 16;
        leave.Margin = new Thickness(14, 0, 0, 0);
        return leave;
    }
}
