using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Savaş arası ekranlar: round sonucu ve maç sonu. Masa arkada görünür kalır; üstünde tek, saydam, sade bir panel durur
/// (oyunun odağı masada kalsın). Sağdaki oyuncu listesi yerinde kalır.
/// </summary>
public sealed partial class HanView
{
    /// <summary>Sahne genişliği: sağdaki oyuncu listesine kadar.</summary>
    private const double FieldW = ListX - 8;
    private const double FieldMid = FieldW / 2;
    private const double SheetW = 700;

    // ---------- round sonucu ----------

    private void DrawResult()
    {
        var round = Session.LastRound;
        var recs = M.History.Where(h => h.Round == round).ToList();
        var mine = recs.FirstOrDefault(h => h.PlayerA == Session.Me || (!h.Pve && !h.Ghost && h.PlayerB == Session.Me));
        var side = mine == null ? 0 : mine.PlayerA == Session.Me ? 0 : 1;
        var result = mine == null || mine.Winner < 0 ? 0 : mine.Winner == side ? 1 : -1;

        if (_resultBattle is { Done: true })
        {
            // masa arkada kalır: biten savaşın son hali (satırlar, kartlar, puanlar, sol paneller); üstünde hafif bir karartma
            DrawArena(behindResult: true);
            Put(new Rectangle { Width = FieldW, Height = 1080, Fill = Theme.B("#5a000000") }, 0, 0);   // masaya tıklama geçmesin
            TopButtons();
        }
        else
        {
            Put(new Rectangle { Width = FieldW, Height = 1080, Fill = Theme.B("#66000000"), IsHitTestVisible = false }, 0, 0);
            PlayerList();
            TopButtons();
        }

        var sp = new StackPanel { Margin = new Thickness(34, 26, 34, 26) };
        sp.Children.Add(HanUi.Label($"Round {round} sonucu", 12.5));
        if (mine != null)
        {
            var (mp, op) = side == 0 ? (mine.PointsA, mine.PointsB) : (mine.PointsB, mine.PointsA);
            var oppId = side == 0 ? mine.PlayerB : mine.PlayerA;
            var oppName = side == 0 ? mine.NameB : mine.NameA;
            var lost = result < 0 ? mine.Damage : 0;

            // Hüküm + skor, sağda rakip
            var head = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var verdict = HanUi.Display(result > 0 ? "Zafer" : result < 0 ? "Yenilgi" : "Berabere", 38, TextAlignment.Left);
            if (result < 0) verdict.Foreground = new LinearGradientBrush(Color.FromRgb(0xff, 0xc0, 0xb0), HanUi.Bad, 90);
            else if (result == 0) verdict.Foreground = new LinearGradientBrush(HanUi.Parchment, HanUi.Muted, 90);
            verdict.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(verdict);
            var sc = HanUi.Number($"{mp} – {op}", 34, HanUi.Br(HanUi.Parchment));
            sc.VerticalAlignment = VerticalAlignment.Center;
            sc.Margin = new Thickness(18, 0, 0, 0);
            left.Children.Add(sc);
            head.Children.Add(left);
            var vs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var against = HanUi.Small("rakip", 13);
            against.VerticalAlignment = VerticalAlignment.Center;
            against.Margin = new Thickness(0, 0, 10, 0);
            vs.Children.Add(against);
            var on = HanUi.Title(oppName, 18, null, TextAlignment.Right);
            on.MaxWidth = 170;
            on.VerticalAlignment = VerticalAlignment.Center;
            on.Margin = new Thickness(0, 0, 10, 0);
            vs.Children.Add(on);
            vs.Children.Add(Portraits.Medallion(mine.Pve ? "face_none" : Face(oppId) ?? "face_none", 44, glow: result < 0));
            head.Children.Add(vs);
            sp.Children.Add(head);
            if (mp == op && mine.Winner >= 0) sp.Children.Add(HanUi.Small("Satırlar eşitti: üç satırın toplam gücü belirledi.", 13));

            // Can: ince çubuk, bu round kaybedilen parça kızıl
            var hp = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            hp.ColumnDefinitions.Add(new ColumnDefinition());
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hp.Children.Add(HpBar(Me.Hp + lost, Me.Hp, SheetW - 68 - 190));
            var hpText = HanUi.T(lost > 0 ? $"−{lost} can · kalan {Me.Hp}" : $"Can {Me.Hp}", 15, lost > 0 ? HanUi.Br(Color.FromRgb(0xff, 0x9a, 0x84)) : HanUi.Br(HanUi.Parchment), FontWeights.SemiBold, TextAlignment.Right);
            hpText.Width = 180;
            hpText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hpText, 1);
            hp.Children.Add(hpText);
            sp.Children.Add(hp);

            var gains = new List<string>();
            if (result > 0) gains.Add($"+{Tuning.WinGold} altın zafer ödülü");
            if (Me.Streak <= -2 || Me.Streak >= 3) gains.Add($"+{Tuning.StreakGold(Me.Streak)} altın {(Me.Streak < 0 ? $"{-Me.Streak} yenilgi serisi" : $"{Me.Streak} galibiyet serisi")}");
            if (gains.Count > 0)
            {
                var g = HanUi.Small(string.Join("   ·   ", gains), 14, HanUi.Br(HanUi.Gold, 0xe0));
                g.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(g);
            }
        }

        var rule = HanUi.Rule(SheetW - 68);
        rule.Margin = new Thickness(0, 16, 0, 8);
        sp.Children.Add(rule);
        foreach (var h in recs.OrderBy(r => r == mine ? 0 : 1)) sp.Children.Add(TableRow(h));

        var dead = M.Players.Where(p => !p.Alive && p.EliminatedRound == round).ToList();
        if (dead.Count > 0)
        {
            var d = HanUi.Small("Masadan kalkanlar: " + string.Join(", ", dead.Select(p => $"{p.Name} ({p.Place}.)")), 14, HanUi.Br(HanUi.Bad));
            d.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(d);
        }

        var foot = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        var hint = KeyHint("Enter", "devam");
        hint.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(hint);
        var next = Btn($"Round {M.Round}", "han-next", EnterPrep, true, width: 230, height: 50, sub: "hazırlığa geç");
        next.HorizontalAlignment = HorizontalAlignment.Right;
        foot.Children.Add(next);
        sp.Children.Add(foot);

        PutSheet(sp, FieldMid, 0xd0);
    }

    /// <summary>Paneli içeriğinin boyuna göre çerçeveler ve sahnede ortalar (saydam: masa arkadan görünür).</summary>
    private void PutSheet(StackPanel content, double centerX, byte alpha, double width = SheetW)
    {
        content.Width = width - content.Margin.Left - content.Margin.Right;
        content.Measure(new Size(width, double.PositiveInfinity));
        var h = content.DesiredSize.Height;   // kenar boşlukları dahil
        var frame = HanUi.Frame(width, h, content, alpha, null, 12);
        Put(frame, centerX - width / 2, Math.Max(70, (1080 - h) / 2 - 20));
        if (_live) DropIn(frame, 0, 14);
    }

    /// <summary>Masa satırı: iki ad, ortada skor, sağda hasar. Kazanan altın, kaybeden soluk; senin satırın vurgulu.</summary>
    private FrameworkElement TableRow(BattleRecord h)
    {
        var me = h.PlayerA == Session.Me || h.PlayerB == Session.Me && !h.Ghost && !h.Pve;
        var g = new Grid { Height = 34 };
        if (me) g.Children.Add(new Rectangle { Fill = HanUi.Br(HanUi.Gold, 0x1c), RadiusX = 3, RadiusY = 3, Margin = new Thickness(-8, 1, -8, 1) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        Brush Fg(int s) => h.Winner == s ? HanUi.GoldText : HanUi.Br(HanUi.Parchment, h.Winner == 1 - s ? (byte)0x88 : (byte)0xd8);
        var a = HanUi.Title(h.NameA, 16, Fg(0), TextAlignment.Right);
        var sc = HanUi.Number($"{h.PointsA} – {h.PointsB}", 16, HanUi.Br(HanUi.Parchment));
        sc.HorizontalAlignment = HorizontalAlignment.Center;
        var b = HanUi.Title(h.NameB, 16, Fg(1));
        var res = HanUi.Small(h.Winner < 0 ? "berabere" : h.Damage > 0 ? $"−{h.Damage} can" : "", 14, h.Winner < 0 ? null : HanUi.Br(Color.FromRgb(0xe0, 0x84, 0x70)));
        res.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var el in new FrameworkElement[] { a, sc, b, res }) el.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sc, 1); Grid.SetColumn(b, 2); Grid.SetColumn(res, 3);
        foreach (var el in new UIElement[] { a, sc, b, res }) g.Children.Add(el);
        return g;
    }

    /// <summary>İnce can çubuğu: kalan can renkli, bu round kaybedilen parça kızıl.</summary>
    private static FrameworkElement HpBar(int before, int after, double w)
    {
        before = Math.Max(before, after);
        const double h = 12;
        var bar = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(new Rectangle { Fill = HanUi.Br(HanUi.Ink, 0xd0), Stroke = HanUi.Br(HanUi.GoldDeep, 0xa0), StrokeThickness = 1, RadiusX = 2, RadiusY = 2 });
        var inner = w - 4;
        var keep = inner * Math.Clamp(after / (double)Tuning.StartHp, 0, 1);
        var lostW = inner * Math.Clamp((before - after) / (double)Tuning.StartHp, 0, 1);
        var c = after <= 30 ? HanUi.Bad : after <= 60 ? HanUi.Ember : HanUi.Good;
        bar.Children.Add(new Rectangle { Width = keep, Height = h - 4, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), Fill = HanUi.Br(c, 0xe0) });
        if (lostW > 0) bar.Children.Add(new Rectangle { Width = lostW, Height = h - 4, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2 + keep, 0, 0, 0), Fill = HanUi.Br(Color.FromRgb(0x9a, 0x26, 0x1a), 0xe8) });
        return bar;
    }

    /// <summary>Tuş ipucu: çerçeveli tuş adı + açıklama.</summary>
    private static FrameworkElement KeyHint(string key, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        row.Children.Add(new Border
        {
            BorderBrush = HanUi.Br(HanUi.GoldDeep), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 1, 8, 2),
            Background = HanUi.Br(HanUi.Ink, 0x90), Child = HanUi.T(key, 13, HanUi.Br(HanUi.Gold), FontWeights.Bold), VerticalAlignment = VerticalAlignment.Center
        });
        var l = HanUi.Small(label, 14);
        l.Margin = new Thickness(8, 0, 0, 0);
        l.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(l);
        return row;
    }

    // ---------- maç sonu ----------

    private void DrawFinal()
    {
        Put(new Rectangle { Width = 1920, Height = 1080, Fill = Theme.B("#80000000"), IsHitTestVisible = false }, 0, 0);
        TopButtons();
        var place = Me.Place;
        var result = place == 1 ? 1 : place <= 4 ? 0 : -1;
        var over = Session.Over || !Me.Alive;

        var sp = new StackPanel { Margin = new Thickness(36, 28, 36, 28) };
        sp.Children.Add(HanUi.Label(Session.Over ? $"Han Ligi bitti · {M.Round} round" : "Han Ligi sürüyor", 12.5));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var pl = HanUi.Number(place == 1 ? "Şampiyon" : $"{place}. sıra", 46,
            result > 0 ? HanUi.GoldText : result == 0 ? HanUi.Br(HanUi.Parchment) : new LinearGradientBrush(Color.FromRgb(0xff, 0xb4, 0xa4), HanUi.Bad, 90));
        head.Children.Add(pl);
        var verdict = HanUi.Title(!over ? "Maç sürüyor" : place == 1 ? "Hanın en iyisi sensin" : place <= 4 ? "İlk dörde girdin" : "Bu sefer olmadı", 20, HanUi.Br(HanUi.Muted));
        verdict.VerticalAlignment = VerticalAlignment.Bottom;
        verdict.Margin = new Thickness(18, 0, 0, 10);
        head.Children.Add(verdict);
        sp.Children.Add(head);
        sp.Children.Add(HanUi.Small($"{Me.Wins} galibiyet  ·  {Me.Losses} yenilgi  ·  seviye {Me.Level}  ·  {SchoolName(Me.School)} okulu", 14));
        if (!Session.Over)
        {
            var live = HanUi.Small("Elendin. Maç öteki masalarda sürüyor; sıralama canlı güncellenir.", 14, HanUi.Br(HanUi.Ember));
            live.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(live);
        }
        var rule = HanUi.Rule(SheetW + 60 - 72);
        rule.Margin = new Thickness(0, 14, 0, 6);
        sp.Children.Add(rule);

        foreach (var p in M.Players.OrderBy(p => p.Place == 0 ? 0 : p.Place).ThenByDescending(p => p.Hp))
        {
            var me = p.Id == Session.Me;
            var row = new Grid { Height = 42 };
            if (me) row.Children.Add(new Rectangle { Fill = HanUi.Br(HanUi.Gold, 0x1c), RadiusX = 3, RadiusY = 3, Margin = new Thickness(-8, 2, -8, 2) });
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var num = HanUi.Number(p.Place == 0 ? "•" : p.Place.ToString(), 20, p.Place == 1 ? HanUi.GoldText : HanUi.Br(HanUi.Parchment));
            num.Width = 34;
            num.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(num);
            line.Children.Add(Portraits.Medallion(Face(p.Id), 32, glow: p.Place == 1));
            var n = HanUi.Title(p.Name, 16, me ? HanUi.GoldText : null);
            n.Width = 200;
            n.Margin = new Thickness(10, 0, 0, 0);
            n.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(n);
            var det = HanUi.Small($"Seviye {p.Level} · {SchoolName(p.School)} · {p.Wins} galibiyet" + (p.Place == 0 ? $" · {p.Hp} can, oynuyor" : p.Place == 1 ? "" : $" · round {p.EliminatedRound}'de elendi"), 13);
            det.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(det);
            row.Children.Add(line);
            sp.Children.Add(row);
        }

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (!Session.Online)
        {
            var again = Btn("Yeniden oyna", "han-again", () => { StopTimer(); _gen++; _again(); }, true, width: 244, height: 50);
            again.Margin = new Thickness(0, 0, 12, 0);
            btns.Children.Add(again);
        }
        btns.Children.Add(Btn("Menüye dön", "han-menu", () => { StopTimer(); _gen++; _back(); }, width: 200, height: 50));
        sp.Children.Add(btns);
        PutSheet(sp, 960, 0xc4, SheetW + 60);
    }
}
