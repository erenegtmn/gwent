using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GwentAI;
using GwentAI.Draft;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Draft ekranı: fraksiyon seç → 3 liderden birini → 25 kez 3 karttan birini seç → rakiplerle oyna (7 galibiyet ya da 3 yenilgi).
/// Seçimler anında kaydedilir; uygulama kapanınca koşu kaldığı yerden sürer. Maç <paramref name="play"/> ile başlatılır.
/// </summary>
public sealed class DraftView : Grid
{
    private readonly DraftSave _save;
    private readonly Action _play;
    private readonly Action _back;
    private readonly string? _last;
    private readonly Grid _content = new() { Width = 1920, Height = 1080 };
    private bool _confirmQuit;

    public DraftView(DraftSave save, Action play, Action back, string? lastResult = null)
    {
        _save = save;
        _play = play;
        _back = back;
        _last = lastResult;
        Children.Add(Ornate.Stage(new Viewbox { Stretch = Stretch.Uniform, Child = _content }, "pano_novigrad", 0.8));
        Focusable = true;
        Loaded += (_, _) => Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) _back(); };
        Draw();
    }

    private DraftRun? Run => _save.Run;

    private void Persist() => DraftStore.Save(_save);

    private void Draw()
    {
        _content.Children.Clear();
        var head = new Grid { Margin = new Thickness(60, 36, 60, 0), VerticalAlignment = VerticalAlignment.Top };
        head.Children.Add(Ornate.Heading("Draft", "Desteni masada kur: her seçimde üç karttan biri", 46));
        var back = Ui.Button("‹ Ana menü", _back);
        back.FontSize = 19;
        back.Padding = new Thickness(22, 8, 22, 8);
        back.HorizontalAlignment = HorizontalAlignment.Right;
        back.VerticalAlignment = VerticalAlignment.Top;
        back.Tag = "draft-back";
        head.Children.Add(back);
        _content.Children.Add(head);

        UIElement body = Run?.Phase switch
        {
            null => StartScreen(),
            DraftPhase.Leader => LeaderScreen(Run),
            DraftPhase.Picking => PickScreen(Run),
            DraftPhase.Playing => HubScreen(Run),
            _ => OverScreen(Run)
        };
        var holder = new ContentControl { Content = body, Margin = new Thickness(60, 150, 60, 50) };
        _content.Children.Add(holder);
    }

    // ---------- Başlangıç: fraksiyon ----------

    private UIElement StartScreen()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(Ui.Text("Hazır deste yok. Bir fraksiyon seç; önce 3 liderden birini, sonra 25 kez önüne gelen 3 karttan birini alırsın.", 22, Theme.Text, false, TextAlignment.Center, true));
        sp.Children.Add(Ui.Text($"Kurduğun desteyle rakiplere çık: {DraftLogic.MaxWins} galibiyet ya da {DraftLogic.MaxLosses} yenilgide koşu biter. Galibiyet başına {DraftLogic.CrownsPerWin} Crown Collect 'Em All kesene yatar.",
            18, Theme.TextDim, false, TextAlignment.Center, true));
        if (_save.Runs > 0)
        {
            var best = Ui.Text($"En iyi koşu: {_save.Best} galibiyet  ·  {_save.Runs} koşu", 18, Theme.B("#f0d890"), true, TextAlignment.Center);
            best.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(best);
        }
        sp.Children.Add(Ornate.Caption("Fraksiyon seç", 34));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var f in DraftLogic.Factions)
        {
            var faction = f;
            var tile = new StackPanel { Width = 230, Margin = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "draft-faction:" + f };
            if (Assets.Ui("shield_" + f) is { } src) tile.Children.Add(new Image { Source = src, Width = 150, Height = 150, IsHitTestVisible = false });
            else tile.Children.Add(new Border { Width = 150, Height = 150, Background = Theme.FactionColor(f), IsHitTestVisible = false });
            tile.Children.Add(Ui.Text(Texts.Faction(f), 22, Theme.B("#f0d890"), true, TextAlignment.Center, true));
            tile.Children.Add(Ui.Text(FactionBlurb(f), 15, Theme.TextDim, false, TextAlignment.Center, true));
            Fx.HoverLift(tile, -8);
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Sfx.Play("lock_in");
                _save.Run = DraftLogic.Start(faction, Random.Shared.Next(1, int.MaxValue));
                Persist();
                Draw();
            };
            row.Children.Add(tile);
        }
        sp.Children.Add(row);
        var panel = Ornate.Panel(sp, new Thickness(60, 40, 60, 46));
        panel.VerticalAlignment = VerticalAlignment.Top;
        return panel;
    }

    private static string FactionBlurb(string f) => f switch
    {
        "northern_realms" => "Raund kazanınca kart çeker",
        "nilfgaard" => "Beraberlikte kazanır",
        "scoiatael" => "Kimin başlayacağını seçer",
        "monsters" => "Raund sonunda bir birim kalır",
        _ => "3. raundda mezarlıktan 2 kart"
    };

    // ---------- Lider ----------

    private UIElement LeaderScreen(DraftRun run)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(Ornate.Heading("Liderini seç", Texts.Faction(run.Faction), 34, HorizontalAlignment.Center));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        foreach (var id in run.LeaderOffer)
        {
            var d = CardCatalog.Default.Get(id);
            row.Children.Add(Choice(d, 250, Texts.LeaderText(d), "draft-leader:" + id, () => { DraftLogic.PickLeader(run, id); Persist(); Draw(); }));
        }
        sp.Children.Add(row);
        return sp;
    }

    // ---------- Kart seçimi ----------

    private UIElement PickScreen(DraftRun run)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(Ornate.Heading($"Seçim {run.Picks.Count + 1} / {DraftLogic.DeckSize}", "Desteye eklemek istediğin kartı seç", 36, HorizontalAlignment.Center));
        center.Children.Add(Progress(run.Picks.Count, DraftLogic.DeckSize, 760));
        var s = DraftLogic.Summary(run);
        var needUnits = Math.Max(0, DeckValidator.MinUnits - s.Units);
        var left = DraftLogic.DeckSize - run.Picks.Count;
        if (needUnits >= left)
        {
            var note = Ui.Text("Desten geçerli olsun diye kalan seçimlerde yalnız birim kartları geliyor.", 17, Theme.B("#f0b070"), true, TextAlignment.Center);
            note.Margin = new Thickness(0, 6, 0, 0);
            center.Children.Add(note);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        foreach (var id in run.Offer)
        {
            var d = CardCatalog.Default.Get(id);
            var have = run.Picks.Count(x => x == id);
            var text = d.IsUnit
                ? Texts.RowOf(d) + (d.Hero ? " · Kahraman" : "") + (d.Abilities.Count > 0 ? "\n" + string.Join("\n", d.Abilities.Select(a => Texts.AbilityText(a))) : "")
                : string.Join("\n", d.Abilities.Select(a => Texts.AbilityText(a)));
            if (have > 0) text += $"\nDestende {have} tane var.";
            row.Children.Add(Choice(d, 260, text, "draft-card", () => { DraftLogic.PickCard(run, id); Persist(); Draw(); }));
        }
        center.Children.Add(row);
        g.Children.Add(center);

        var deck = DeckPanel(run);
        SetColumn(deck, 1);
        g.Children.Add(deck);
        return g;
    }

    private FrameworkElement Choice(CardDef d, double w, string text, string tag, Action pick)
    {
        var col = new StackPanel { Width = w + 40, Margin = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = tag };
        var card = new GwintCard(d, w, d.IsUnit ? d.Strength : null, height: w * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.9 };
        col.Children.Add(card);
        var n = Ui.Text(d.Name, 22, d.Hero ? Theme.B("#f0d890") : Theme.Text, true, TextAlignment.Center, true);
        n.Margin = new Thickness(0, 12, 0, 4);
        col.Children.Add(n);
        col.Children.Add(Ui.Text(text, 15, Theme.B("#cfc4aa"), false, TextAlignment.Center, true));
        Fx.HoverLift(col, -12);
        col.MouseEnter += (_, _) => card.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 100), BlurRadius = 34, ShadowDepth = 0, Opacity = 0.9 };
        col.MouseLeave += (_, _) => card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.9 };
        col.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("draw_card"); pick(); };
        return col;
    }

    private static FrameworkElement Progress(int n, int total, double w)
    {
        var g = new Grid { Width = w, Height = 10, Margin = new Thickness(0, 12, 0, 0) };
        g.Children.Add(new Border { Background = Theme.B("#33ffffff"), CornerRadius = new CornerRadius(5) });
        g.Children.Add(new Border { Background = Ornate.GoldEdge, CornerRadius = new CornerRadius(5), Width = w * n / total, HorizontalAlignment = HorizontalAlignment.Left });
        return g;
    }

    /// <summary>Sağda deste özeti: lider, sayılar ve seçilen kartların listesi.</summary>
    private FrameworkElement DeckPanel(DraftRun run)
    {
        var sp = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (run.LeaderId != null)
        {
            var l = CardCatalog.Default.Get(run.LeaderId);
            top.Children.Add(new GwintCard(l, 74, null, height: 74 * GwintCard.Aspect) { IsHitTestVisible = false });
            var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 300 };
            names.Children.Add(Ui.Text(l.Name, 17, Theme.B("#f0d890"), true, TextAlignment.Left, true));
            names.Children.Add(Ui.Text(Texts.Faction(run.Faction), 14, Theme.TextDim));
            top.Children.Add(names);
        }
        sp.Children.Add(top);
        var s = DraftLogic.Summary(run);
        var stats = Ui.Text($"Birim {s.Units}/{DeckValidator.MinUnits}+   ·   Özel {s.Specials}/{DeckValidator.MaxSpecials}   ·   Kahraman {s.Heroes}", 16, Theme.Text, true);
        stats.Margin = new Thickness(0, 12, 0, 2);
        sp.Children.Add(stats);
        sp.Children.Add(Ui.Text($"Yakın {s.Close}  ·  Menzilli {s.Ranged}  ·  Kuşatma {s.Siege}", 14, Theme.TextDim));
        sp.Children.Add(Ornate.Rule(380, 8));
        var list = new StackPanel();
        foreach (var grp in run.Picks.GroupBy(x => x).Select(gr => (Def: CardCatalog.Default.Get(gr.Key), N: gr.Count()))
                     .OrderBy(x => x.Def.IsUnit ? 0 : 1).ThenByDescending(x => x.Def.Strength).ThenBy(x => x.Def.Name))
        {
            var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(Ui.Text(grp.Def.IsUnit ? grp.Def.Strength.ToString() : "★", 16, grp.Def.Hero ? Theme.B("#f0d890") : Theme.Gold, true));
            var name = Ui.Text(grp.Def.Name, 15, grp.Def.Hero ? Theme.B("#f0d890") : Theme.Text);
            SetColumn(name, 1);
            line.Children.Add(name);
            var cnt = Ui.Text("×" + grp.N, 15, Theme.Gold, true);
            SetColumn(cnt, 2);
            line.Children.Add(cnt);
            list.Children.Add(line);
        }
        if (run.Picks.Count == 0) list.Children.Add(Ui.Text("Henüz kart seçmedin.", 15, Theme.TextDim));
        sp.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 });
        var panel = Ornate.Panel(sp, new Thickness(26, 24, 20, 22), 16, 44);
        panel.VerticalAlignment = VerticalAlignment.Top;
        return panel;
    }

    // ---------- Maçlar arası ----------

    private UIElement HubScreen(DraftRun run)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 900 };
        if (_last != null)
        {
            var last = Ui.Text(_last, 26, Theme.B("#f0d890"), true, TextAlignment.Center, true);
            last.Tag = "draft-last";
            sp.Children.Add(last);
        }
        sp.Children.Add(Record(run));
        var next = run.Next!;
        var npc = GwentAI.Career.NpcDecks.Get(next.Deck, next.Level);
        var opp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        var face = Portraits.Medallion(next.Face ?? npc.LeaderId, 150, glow: true);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        opp.Children.Add(face);
        opp.Children.Add(Ui.Text($"Sıradaki rakip: {next.Name}", 28, Theme.B("#f0d890"), true, TextAlignment.Center));
        opp.Children.Add(Ui.Text($"{Texts.Faction(npc.Faction)} · {CardCatalog.Default.Get(npc.LeaderId).Name} · {(next.Ai switch { Difficulty.Easy => "Kolay", Difficulty.Hard => "Zor", _ => "Normal" })}",
            18, Theme.Text, false, TextAlignment.Center));
        sp.Children.Add(opp);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        if (!_confirmQuit)
        {
            var play = Ui.Button("Masaya otur", _play, primary: true);
            play.FontSize = 26;
            play.Padding = new Thickness(54, 14, 54, 14);
            play.Tag = "draft-play";
            bar.Children.Add(play);
            var quit = Ui.Button("Koşuyu bitir", () => { _confirmQuit = true; Draw(); });
            quit.FontSize = 19;
            quit.Padding = new Thickness(26, 11, 26, 11);
            quit.Margin = new Thickness(18, 0, 0, 0);
            quit.Tag = "draft-quit";
            bar.Children.Add(quit);
        }
        else
        {
            var q = Ui.Text($"Koşu şimdi biter: {run.Wins} galibiyetin ödülü (+{DraftLogic.Reward(run)} Crown) verilir.", 19, Theme.B("#f0c0b0"), true, TextAlignment.Center);
            q.VerticalAlignment = VerticalAlignment.Center;
            q.Margin = new Thickness(0, 0, 16, 0);
            bar.Children.Add(q);
            var yes = Ui.Button("Evet, bitir", () => EndRun(run));
            yes.Tag = "draft-quit-confirm";
            bar.Children.Add(yes);
            var no = Ui.Button("Vazgeç", () => { _confirmQuit = false; Draw(); }, primary: true);
            no.Margin = new Thickness(10, 0, 0, 0);
            bar.Children.Add(no);
        }
        sp.Children.Add(bar);
        g.Children.Add(Ornate.Panel(sp, new Thickness(50, 36, 50, 40)).Also(p => { p.HorizontalAlignment = HorizontalAlignment.Center; p.VerticalAlignment = VerticalAlignment.Top; }));

        var deck = DeckPanel(run);
        SetColumn(deck, 1);
        g.Children.Add(deck);
        return g;
    }

    /// <summary>Galibiyetler (7 altın defne) ve kalan haklar (3 mücevher).</summary>
    private static UIElement Record(DraftRun run)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var wins = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < DraftLogic.MaxWins; i++)
        {
            var on = i < run.Wins;
            var cell = new Border
            {
                Width = 54, Height = 54, Margin = new Thickness(5), CornerRadius = new CornerRadius(27),
                Background = on ? Ornate.GoldEdge : Theme.B("#33ffffff"), BorderBrush = Theme.B("#6a5228"), BorderThickness = new Thickness(1.5),
                Child = Ui.Text((i + 1).ToString(), 20, on ? Theme.B("#2a1c0e") : Theme.TextDim, true, TextAlignment.Center).Also(t => t.VerticalAlignment = VerticalAlignment.Center)
            };
            if (on) cell.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 210, 110), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 };
            wins.Children.Add(cell);
        }
        sp.Children.Add(wins);
        sp.Children.Add(Ui.Text($"{run.Wins} galibiyet", 18, Theme.Text, true, TextAlignment.Center));
        var lives = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < DraftLogic.MaxLosses; i++)
        {
            var alive = i >= run.Losses;
            FrameworkElement gem = Assets.Ui("gem") is { } src
                ? new Image { Source = src, Width = 40, Height = 40, Margin = new Thickness(6, 0, 6, 0) }
                : new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.B("#b03030"), Margin = new Thickness(6, 0, 6, 0) };
            gem.Opacity = alive ? 1 : 0.2;
            lives.Children.Add(gem);
        }
        sp.Children.Add(lives);
        sp.Children.Add(Ui.Text($"Kalan hak: {DraftLogic.MaxLosses - run.Losses}", 16, Theme.TextDim, false, TextAlignment.Center));
        return sp;
    }

    // ---------- Koşu sonu ----------

    private void EndRun(DraftRun run)
    {
        // Erken bitirme: kalan haklar yenilgi sayılır, ödül o ana kadarki galibiyetlerle verilir
        var crowns = DraftLogic.Reward(run);
        while (run.Phase == DraftPhase.Playing) run.Losses++;
        run.Next = null;
        DraftRewards.Pay(_save, run, crowns);
        _confirmQuit = false;
        Draw();
    }

    private UIElement OverScreen(DraftRun run)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 900 };
        sp.Children.Add(Ornate.Heading(run.Wins >= DraftLogic.MaxWins ? "Kusursuz koşu" : "Koşu bitti", null, 40, HorizontalAlignment.Center));
        sp.Children.Add(Record(run));
        var r = Ui.Text($"+{DraftLogic.Reward(run)} Crown Collect 'Em All kesene yattı.", 22, Theme.B("#f0d890"), true, TextAlignment.Center);
        r.Margin = new Thickness(0, 18, 0, 0);
        sp.Children.Add(r);
        sp.Children.Add(Ui.Text($"En iyi koşu: {_save.Best} galibiyet  ·  {_save.Runs} koşu", 17, Theme.TextDim, false, TextAlignment.Center));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
        var again = Ui.Button("Yeni koşu", () => { _save.Run = null; Persist(); Draw(); }, primary: true);
        again.FontSize = 24;
        again.Padding = new Thickness(46, 12, 46, 12);
        again.Tag = "draft-new";
        bar.Children.Add(again);
        var menu = Ui.Button("Ana menü", _back);
        menu.FontSize = 20;
        menu.Margin = new Thickness(16, 0, 0, 0);
        bar.Children.Add(menu);
        sp.Children.Add(bar);
        var panel = Ornate.Panel(sp, new Thickness(50, 36, 50, 40));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Top;
        return panel;
    }
}

/// <summary>Koşu bitince ödül: Crown Collect 'Em All kesesine (kariyer yoksa oluşturulur), en iyi sonuç ve koşu sayısı.</summary>
public static class DraftRewards
{
    public static void Pay(DraftSave save, DraftRun run, int crowns)
    {
        save.Runs++;
        save.Best = Math.Max(save.Best, run.Wins);
        if (crowns > 0)
        {
            var career = CareerStore.Load() ?? GwentAI.Career.CareerLogic.NewSave();
            career.Crowns += crowns;
            CareerStore.Save(career);
        }
        DraftStore.Save(save);
    }
}

internal static class ElementExtensions
{
    public static T Also<T>(this T el, Action<T> act) { act(el); return el; }
}
