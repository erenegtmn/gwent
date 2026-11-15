using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;
using HT = HanLigi.Texts;

namespace GwentClient.HanLigi;

/// <summary>
/// Savaş: VS girişi → aktivasyon (Boynuz/Kalkan/Moral hedefi) → üç pencere. Her pencerede: işaret çubuğundan seçim → iki tarafın
/// işaretleri havada çizilir → rakibin satırı açılır, kartlar iner → etkiler hedefe iner → satırın sahibi. Sonunda hasar
/// mermisi kaybedenin can çubuğuna uçar. Ekran sarsılmaz; yalnız vurulan kart titrer.
/// </summary>
public sealed partial class HanView
{
    private readonly HashSet<Row> _weatherShown = new();
    /// <summary>Masadaki kart merkezleri (ekran).</summary>
    private readonly Dictionary<int, Point> _pos = new();
    /// <summary>Çözülen pencere: 1 = işaretler atılıyor (satır sisli), 2 = kartlar açıldı (sayılar gizli), 0 = hepsi görünür.</summary>
    private int _hold;

    private Battle B => Session.MyBattle!;
    private int Mine => Session.MySide;

    private static string RowIcon(Row r) => r switch { Row.Close => "row_close", Row.Ranged => "row_ranged", _ => "row_siege" };

    // ---------- VS girişi ----------

    private void VsIntro(Battle b)
    {
        var (me, op) = (b.Sides[Mine], b.Sides[1 - Mine]);
        var g = new Grid { Width = 1920, Height = 1080 };
        g.Children.Add(new Rectangle { Fill = Theme.B("#c8000000") });
        Canvas Side(bool mine, string name, string? face, string sub)
        {
            var c = new Canvas { Width = 960, Height = 1080 };
            var port = Portraits.Medallion(face, 260, true);
            Canvas.SetLeft(port, mine ? 470 : 230);
            Canvas.SetTop(port, 290);
            c.Children.Add(port);
            var n = Txt(name, 44, mine ? Theme.B("#ffe9a3") : Theme.B("#f0a090"), true, TextAlignment.Center);
            n.Width = 520;
            Canvas.SetLeft(n, mine ? 340 : 100);
            Canvas.SetTop(n, 570);
            c.Children.Add(n);
            var s = new TextBlock { Text = sub, FontFamily = HanUi.Font, FontSize = HanUi.S(18), Foreground = Theme.TextDim, TextAlignment = TextAlignment.Center, Width = 520 };
            Canvas.SetLeft(s, mine ? 340 : 100);
            Canvas.SetTop(s, 630);
            c.Children.Add(s);
            return c;
        }
        var left = Side(true, me.Name, Face(HanSession.Me), $"Seviye {me.Level} · {SchoolName(me.School)}");
        var oppFace = b.Pve ? (M.Round <= 2 ? "arachas" : "face_imlerith") : Face(op.PlayerId);
        var right = Side(false, op.Name, oppFace, b.Pve ? "canavar round'u" : $"Seviye {op.Level} · {SchoolName(op.School)}");
        right.HorizontalAlignment = HorizontalAlignment.Right;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(left);
        g.Children.Add(right);
        var vs = Txt("VS", 120, Theme.B("#e0b85a"), true, TextAlignment.Center);
        vs.HorizontalAlignment = HorizontalAlignment.Center;
        vs.VerticalAlignment = VerticalAlignment.Center;
        vs.Margin = new Thickness(0, -110, 0, 0);
        g.Children.Add(vs);
        var order = new TextBlock { Text = "Satır sırası:  " + string.Join("   →   ", b.Order.Select(HT.Name)), FontFamily = HanUi.Font, FontSize = HanUi.S(26), FontWeight = FontWeights.Bold, Foreground = Theme.B("#f4ead2"), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 230) };
        g.Children.Add(order);
        _banner.Children.Add(g);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var lt = new TranslateTransform(-600, 0);
        var rt = new TranslateTransform(600, 0);
        left.RenderTransform = lt;
        right.RenderTransform = rt;
        lt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-600, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        rt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(600, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        vs.Opacity = 0;
        vs.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(380) });
        Sfx.Play("han_whoosh");
        Fx.After(400, () => Sfx.Play("han_medallion"));
        var hold = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(2100) };
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.82)));
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        hold.Completed += (_, _) => _banner.Children.Remove(g);
        g.BeginAnimation(OpacityProperty, hold);
    }

    // ---------- arena ----------

    private void DrawArena()
    {
        var b = B;
        var (me, op) = (b.Sides[Mine], b.Sides[1 - Mine]);
        var activation = _screen == Screen.Activation;
        _pos.Clear();
        var shownWindows = b.Window - (_hold > 0 ? 1 : 0);
        var resolving = _hold > 0 ? b.Window - 1 : -1;
        var current = activation || (b.Done && !_busy) ? -1 : _busy ? Math.Max(0, b.Window - 1) : b.Window;
        var (myPts, opPts) = (PointsShown(Mine, shownWindows), PointsShown(1 - Mine, shownWindows));

        // ---- üst şerit ----
        string hint;
        Brush? hc = null;
        if (activation)
        {
            var targets = _act.Horns.Count + _act.Shields.Count + _act.Morale.Count;
            hint = targets > 0 ? "Boynuz / Kalkan / Moral için hedef satırı seç, sonra Savaşa başla." : "Masan kilitlendi. Satırlar sırayla açılacak: Savaşa başla.";
        }
        else if (b.Done && !_busy) { hint = b.Winner < 0 ? "Berabere: kimse can kaybetmez." : b.Winner == Mine ? "Savaşı kazandın!" : "Savaşı kaybettin."; hc = b.Winner == Mine ? Theme.Good : b.Winner < 0 ? null : Theme.Bad; }
        else if (_busy) hint = $"{HT.Name(b.Order[Math.Max(0, current)])} satırı çarpışıyor...";
        else
        {
            hint = $"{b.Window + 1}. çarpışma: {HT.Name(b.CurrentRow)}. İşaret ya da simya seç, sonra Satırı aç.";
            hc = Theme.B("#ffe9a3");
        }
        TopBar(activation ? "SAVAŞ · HAZIRLAN" : $"SAVAŞ · {myPts} - {opPts}", hint, hc);
        PlayerList();

        // ---- hava (iki tarafta) ----
        foreach (var r in b.Weather)
        {
            var type = r switch { Row.Close => "frost", Row.Ranged => "fog", _ => "rain" };
            var intro = _live && _weatherShown.Add(r);
            foreach (var mine in new[] { false, true })
            {
                Trap(RowRect(mine, r), Theme.B("#405fb7e8"));
                if (!Assets.Available) continue;
                var g = TrapGeometry(RowRect(mine, r), 3);
                var wx = WeatherFx.Make(type, g.Bounds.Width, g.Bounds.Height, intro);
                var local = g.Clone();
                local.Transform = new TranslateTransform(-g.Bounds.X, -g.Bounds.Y);
                wx.Clip = local;
                Canvas.SetLeft(wx, g.Bounds.X);
                Canvas.SetTop(wx, g.Bounds.Y);
                (_live ? _anim : _rows).Children.Add(wx);
            }
        }

        // ---- satırlar ----
        for (var i = 0; i < 3; i++)
        {
            var r = b.Order[i];
            var revealed = i < shownWindows || (i == resolving && _hold == 2);
            var scored = i < shownWindows;
            var o = i < b.Outcomes.Count ? b.Outcomes[i] : null;
            var (mv, ov) = o == null ? (0, 0) : Mine == 0 ? (o.ValueA, o.ValueB) : (o.ValueB, o.ValueA);
            int Shown(BattleSide s, BattleUnit u) => scored && o!.CardValues.TryGetValue(u.Uid, out var fv) ? fv : b.Value(s, u);

            if (i == current)
                foreach (var mine in new[] { false, true })
                    Trap(RowRect(mine, r), Theme.B("#1effe08a"), Theme.Select, 3, 3, new DropShadowEffect { Color = Color.FromRgb(0xff, 0xd8, 0x70), BlurRadius = 18, ShadowDepth = 0 });
            if (scored && o!.Winner >= 0)
                Trap(RowRect(o.Winner == Mine, r), new SolidColorBrush(o.Winner == Mine ? Color.FromArgb(0x34, 0x8f, 0xd0, 0x62) : Color.FromArgb(0x34, 0xe0, 0x5a, 0x48)));

            // benim satırım
            var myUnits = me.In(r).Where(u => !(i == resolving && _hold == 1 && u.Landed)).ToList();
            var myCards = myUnits.Select(u => (u.Uid, (FrameworkElement)CardEl(u.Def, u.Star, DesignW, null, null, Shown(me, u), footer: u.Token ? "hayalet" : u.Summoned ? "çağrıldı" : null))).ToList();
            PlaceRow(myCards, true, r, _pos);
            PutU(ScoreBadge(scored ? mv.ToString() : b.Started ? b.PlainRow(Mine, r).ToString() : me.In(r).Sum(u => u.Power).ToString(), true, scored && o!.Winner == Mine), new Point(RowX + RowW - 36, RowY(true, r) + RowH / 2));

            // rakibin satırı
            if (revealed)
            {
                var opCards = op.In(r).Select(u => (u.Uid, (FrameworkElement)CardEl(u.Def, u.Star, DesignW, null, null, Shown(op, u), footer: u.Token ? "hayalet" : u.Summoned ? "çağrıldı" : null))).ToList();
                PlaceRow(opCards, false, r, _pos);
                if (i == resolving && _hold == 2)
                {
                    var k = 0;
                    foreach (var (_, el) in opCards) DropIn(el, 70 * k++, -36);
                    foreach (var (uid, el) in myCards) if (me.In(r).First(u => u.Uid == uid).Landed) DropIn(el, 0, -60);
                }
                PutU(ScoreBadge(scored ? ov.ToString() : "…", false, scored && o!.Winner == 1 - Mine), new Point(RowX + RowW - 36, RowY(false, r) + RowH / 2));
            }
            else
            {
                BoardFog(RowRect(false, r), 1);
                var seen = b.Revealed[Mine].Where(x => x.Zone == HT.Name(r)).Select((x, k) => (k, (FrameworkElement)CardEl(x.Def, x.Star, DesignW, null, null, footer: "casus görüyor"))).ToList();
                foreach (var (_, el) in seen) el.Opacity = 0.8;
                PlaceRow(seen, false, r);
                if (seen.Count == 0) PutU(Txt("?", 56, Theme.B("#50e8dcc4"), true), RowCenter(false, r), 0, _rows);
                PutU(ScoreBadge("?", false), new Point(RowX + RowW - 36, RowY(false, r) + RowH / 2));
            }

            // sıra numarası: satırın sol sütununda
            foreach (var mine in new[] { false, true })
            {
                var num = new TextBlock
                {
                    Text = (i + 1).ToString(), FontFamily = HanUi.Font, FontSize = HanUi.S(mine ? 58 : 46), FontWeight = FontWeights.Bold, IsHitTestVisible = false,
                    Foreground = i == current ? Theme.B("#ffe9a3") : scored ? Theme.B("#30e8dcc4") : Theme.B("#70e0b85a"),
                    Effect = i == current ? new DropShadowEffect { Color = Color.FromRgb(0xff, 0xd8, 0x70), BlurRadius = 18, ShadowDepth = 0 } : null
                };
                PutU(num, new Point(ColX + ColW / 2, RowY(mine, r) + RowH / 2 + 8), 0, _rows);
                RowLabel(mine, r, HT.Name(r), i == current ? Theme.B("#c0ffe9a3") : null);
            }

            // Boynuz / Kalkan / Moral hedefleri: satırın sağ ucunda küçük işaretler
            var act = b.Started ? me.Act : _act;
            var marks = act.Horns.Where(h => h == r).Select(_ => "Boynuz +%50").Concat(act.Shields.Where(h => h == r).Select(_ => "Kalkan")).Concat(act.Morale.Where(kv => kv.Value == r).Select(_ => "Moral")).ToList();
            if (marks.Count > 0)
            {
                var chip = new Border
                {
                    Background = Theme.B("#e01a1209"), BorderBrush = Theme.B("#c9a24b"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2),
                    Child = new TextBlock { Text = string.Join(" · ", marks), FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffe2a0") }
                };
                PutU(chip, new Point(RowX + RowW - 36, RowY(true, r) + 12));
            }
        }
        // rakibin özel kartlarından casusun gördükleri
        var seenSpecials = b.Revealed[Mine].Where(x => x.Zone == "Özel").ToList();
        for (var k = 0; k < seenSpecials.Count; k++)
        {
            var (top, bottom, _, _) = Band(false, Row.Close);
            var el = CardEl(seenSpecials[k].Def, 1, DesignW, null, null, footer: "casus görüyor");
            el.Opacity = 0.85;
            PlaceCard(el, HanScene.ToScreen(new Point(ColX + ColW / 2 + k * 20, OppTop[0] + RowH - 5)), (bottom - top) * 0.98 / (DesignH * Lean), k);
        }

        // ---- sol: iki tarafın durumu ----
        SideCard(false, op, b.Pve ? (M.Round <= 2 ? "arachas" : "face_imlerith") : Face(op.PlayerId), PointsShown(1 - Mine, shownWindows), b.Started, 66);
        BattleLog(b, shownWindows, activation);
        SideCard(true, me, Face(HanSession.Me), PointsShown(Mine, shownWindows), b.Started, 742);

        // ---- alt şerit ----
        Put(Panel(1896, BarH, null, 0xec), 12, BarY);
        if (activation) ActivationTray(b, me);
        else if (b.Done && !_busy) EndTray(b, me, op);
        else if (!_busy) SignTray(b, me);
        else ChoicesTray(b);
    }

    private int PointsShown(int side, int shownWindows) => B.Outcomes.Take(shownWindows).Count(o => o.Winner == side);

    /// <summary>Sol kenardaki taraf kartı: portre, ad, satır puanı mücevherleri, Odak, simya.</summary>
    private void SideCard(bool mine, BattleSide s, string? face, int points, bool started, double y)
    {
        var g = new Grid { Width = SideW, Height = 124 };
        g.Children.Add(Panel(SideW, 124, null, 0xd8));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = mine ? Theme.B("#c9a24b") : Theme.B("#a05a48") });
        var port = Portraits.Medallion(face, 60, mine);
        port.HorizontalAlignment = HorizontalAlignment.Left;
        port.VerticalAlignment = VerticalAlignment.Top;
        port.Margin = new Thickness(8, 8, 0, 0);
        g.Children.Add(port);
        var name = new TextBlock { Text = s.Name, FontFamily = HanUi.Font, FontSize = HanUi.S(19), FontWeight = FontWeights.Bold, Foreground = mine ? Theme.B("#ffe9a3") : Theme.B("#f0a090"), Margin = new Thickness(76, 8, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        g.Children.Add(name);
        g.Children.Add(new TextBlock { Text = $"Seviye {s.Level} · {SchoolName(s.School)}", FontFamily = HanUi.Font, FontSize = HanUi.S(12), Foreground = Theme.TextDim, Margin = new Thickness(76, 34, 0, 0) });
        // satır puanı (iki satır kazanan savaşı alır)
        var gems = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(76, 52, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < 2; i++)
        {
            var on = i < points;
            if (Assets.Ui("gem") is { } gem) gems.Children.Add(new Image { Source = gem, Width = 24, Height = 24, Opacity = on ? 1 : 0.22, Margin = new Thickness(0, 0, 4, 0) });
            else gems.Children.Add(new Ellipse { Width = 18, Height = 18, Margin = new Thickness(0, 3, 6, 3), Fill = on ? Theme.B("#d0402e") : Theme.B("#33ffffff") });
        }
        g.Children.Add(gems);
        var focus = started ? s.Focus : Tuning.StartFocus;
        var fo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 84, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        fo.Children.Add(HanArt.Icon("icon_focus", 24, "◈"));
        for (var i = 0; i < Math.Max(focus, 3); i++)
            fo.Children.Add(new Ellipse
            {
                Width = 14, Height = 14, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Stroke = Theme.B("#cfefff"), StrokeThickness = 1,
                Fill = i < focus ? new RadialGradientBrush(Color.FromRgb(0xc8, 0xf0, 0xff), Color.FromRgb(0x2a, 0x84, 0xc8)) : Theme.B("#33000000")
            });
        fo.Children.Add(new TextBlock { Text = $"  {focus} Odak  ·  {s.Potions.Count} simya" + (mine && s.Ambush.Count > 0 ? $"  ·  pusu {s.Ambush.Count}" : ""), FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.SemiBold, Foreground = Theme.B("#cfefff"), VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(fo);
        Put(g, SideX, y);
    }

    /// <summary>Sol orta: son olaylar.</summary>
    private void BattleLog(Battle b, int shownWindows, bool activation)
    {
        var sp = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        sp.Children.Add(Caption("SON OLAYLAR"));
        var lines = new List<(string, Brush)>();
        foreach (var l in b.PreLog) lines.Add(("• " + l, Theme.TextDim));
        foreach (var o in b.Outcomes.Take(shownWindows))
        {
            var (mv, ov) = Mine == 0 ? (o.ValueA, o.ValueB) : (o.ValueB, o.ValueA);
            lines.Add(($"{o.Index + 1}. {HT.Name(o.Row)}  {mv} - {ov}", o.Winner == Mine ? Theme.Good : o.Winner < 0 ? Theme.Text : Theme.Bad));
            foreach (var l in o.Log) lines.Add(("• " + l, Theme.Text));
        }
        if (activation && b.Revealed[Mine].Count == 0) lines.Add(("Casusun yok: rakibin satırları sis altında.", Theme.TextDim));
        if (b.AmbushSeen[Mine] is { } amb) lines.Add(($"Casus: rakibin pusuda {amb} kartı var.", Theme.B("#ffe9a3")));
        foreach (var (t, c) in lines.TakeLast(12)) sp.Children.Add(new TextBlock { Text = t, FontFamily = HanUi.Font, FontSize = HanUi.S(12), Foreground = c, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var border = Panel(SideW, 540, sp, 0xb8);
        border.ClipToBounds = true;
        Put(border, SideX, 196);
    }

    // ---------- aktivasyon ----------

    private void ActivationTray(Battle b, BattleSide me)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var any = false;
        void RowChoice(string label, CardDef art, string tagPrefix, Row current, Action<Row> set)
        {
            any = true;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 34, 0), Orientation = Orientation.Horizontal };
            cell.Children.Add(CardEl(art, 1, 96, null, null));
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = label, FontFamily = HanUi.Font, FontSize = HanUi.S(15), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffe9a3") });
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var r in b.Order)
            {
                var rr = r;
                var on = current == r;
                var face = new Grid { Width = 84, Height = 84, Background = Brushes.Transparent };
                face.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = on ? Theme.B("#e05a4313") : Theme.B("#c0120c07"), BorderBrush = on ? Theme.Select : Theme.B("#7a5c24"), BorderThickness = new Thickness(on ? 2.5 : 1) });
                if (Assets.Ui(RowIcon(r)) is { } icon) face.Children.Add(new Image { Source = icon, Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) });
                face.Children.Add(new TextBlock { Text = $"{HT.Name(r)} {b.PlainRow(Mine, r)}", FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = on ? Theme.Select : Theme.Text, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
                var bt = Bare(face, $"{tagPrefix}:{r}", () => { set(rr); Sfx.Play("han_click"); Render(); });
                bt.Margin = new Thickness(0, 0, 6, 0);
                line.Children.Add(bt);
            }
            right.Children.Add(line);
            cell.Children.Add(right);
            sp.Children.Add(cell);
        }
        for (var i = 0; i < _act.Horns.Count; i++) { var k = i; RowChoice("Boynuz: +%50", CardSet.Get("horn"), "han-horn:" + i, _act.Horns[i], r => _act.Horns[k] = r); }
        for (var i = 0; i < _act.Shields.Count; i++) { var k = i; RowChoice("Kalkan: havaya ve Kavurma'ya karşı", CardSet.Get("shield"), "han-shield:" + i, _act.Shields[i], r => _act.Shields[k] = r); }
        foreach (var uid in _act.Morale.Keys.ToList())
        {
            var u = me.Units.First(x => x.Uid == uid);
            RowChoice($"Moral +{u.Star}", u.Def, "han-morale:" + uid, _act.Morale[uid], r => _act.Morale[uid] = r);
        }
        if (!any) sp.Children.Add(new TextBlock { Text = "Hedef seçecek kartın yok. Satırlar numara sırasıyla açılacak: üç çarpışmadan ikisini alan savaşı kazanır.", FontFamily = HanUi.Font, FontSize = HanUi.S(18), Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Width = 1200 });
        Put(new Grid { Width = 1700, Height = BarH, Children = { sp } }, 40, BarY);
        Put(Act("Savaş", "başlasın", "han-act-go", OnActivationDone, HanButtonKind.Primary, 122, 164), GoX, BarY + 14);
    }

    private void OnActivationDone()
    {
        if (_screen != Screen.Activation) return;
        StopTimer();
        Session.StartBattle(_act);
        _signs.Clear();
        _potion = null;
        _ambush = null;
        _screen = Screen.Battle;
        _hold = 0;
        var b = B;
        if (!_live) { Render(); return; }
        _busy = true;
        var gen = ++_gen;
        _banner.Children.Clear();
        Sfx.Play("horn");
        Render();
        var t = 300;
        foreach (var f in b.PreFx)
        {
            var row = (Row)f.Target;
            if (f.Kind == "weather") { After(t, gen, () => Sfx.Play("frost")); t += 600; continue; }
            var mine = f.Side == Mine;
            After(t, gen, () =>
            {
                Sfx.Play("scorch");
                var (c, w) = RowS(mine, row);
                // gökten inen ateş sütunu, sonra satır boyunca yanma
                for (var i = 0; i < 90; i++) _engine.Spark(new Point(c.X + _engine.R(-40, 40), c.Y - 420), _engine.R(84, 96), _engine.R(900, 1400), 0.32f, _engine.R(6, 12), 3, FxEngine.Ramp.Fire, 0.9f, 0, 0, true);
                Fx.After(300, () => { HanFx.Explosion(_engine, c, FxEngine.Ramp.Fire, 1.4); HanFx.Burn(_engine, c, w * 0.5, 1.4, 1); Sfx.Play("han_explosion"); });
                HanArt.FloatText(_fx, "KAVURMA!", new Point(c.X, c.Y - 110), Color.FromRgb(0xff, 0x8a, 0x2e), 48, 1400, 300);
            });
            t += 750;
        }
        After(t + 150, gen, () => { _busy = false; StartTimer(6, OnWindowGo); Render(); });
    }

    // ---------- işaret çubuğu ----------

    private WindowChoice CurrentChoice() => new(_signs.ToArray(), _potion, _ambush);

    private void SignTray(Battle b, BattleSide me)
    {
        var maxSigns = b.CanDouble(Mine) ? 2 : 1;
        var x = 40.0;
        Put(Caption("İŞARETLER"), x, BarY + 10);
        foreach (var s in Enum.GetValues<Sign>())
        {
            var on = _signs.Contains(s);
            var trial = on ? _signs.Where(v => v != s).ToList() : _signs.Count < maxSigns ? _signs.Append(s).ToList() : new List<Sign> { s };
            var ok = on || b.Valid(Mine, new WindowChoice(trial.ToArray(), _potion, _ambush));
            var cost = b.Cost(Mine, new WindowChoice(new[] { s }));
            var med = HanArt.SignMedallion(s, 104, on, ok, cost);
            Put(Bare(med, "han-sign:" + s, () => { _signs.Clear(); _signs.AddRange(trial); Sfx.Play("han_click"); Render(); }, ok, $"{s}\n{HT.Describe(s)}"), x, BarY + 32);
            x += 122;
        }
        x += 24;
        Put(new Rectangle { Width = 1, Height = BarH - 30, Fill = Theme.B("#605e451c") }, x - 14, BarY + 15);
        Put(Caption("SİMYA"), x, BarY + 10);
        foreach (var p in me.Potions.Distinct())
        {
            var pp = p;
            var tok = HanArt.IconToken(HanArt.PotionIcon(p), 96, _potion == p, HT.Name(p)[..1]);
            Put(Bare(tok, "han-use:" + p, () => { _potion = _potion == pp ? null : pp; Sfx.Play("han_click"); Render(); }, true, HT.Name(p) + "\n" + HT.Describe(p)), x, BarY + 36);
            var n = me.Potions.Count(v => v == p);
            if (n > 1) Put(Txt("×" + n, 18, Theme.B("#ffd76a"), true), x + 76, BarY + 120);
            Put(new TextBlock { Text = HT.Name(p), FontFamily = HanUi.Font, FontSize = HanUi.S(12), Foreground = Theme.TextDim, Width = 96, TextAlignment = TextAlignment.Center }, x, BarY + 140);
            x += 110;
        }
        if (me.Potions.Count == 0) { Put(new TextBlock { Text = "simya kartın yok", FontFamily = HanUi.Font, FontSize = HanUi.S(14), Foreground = Theme.TextDim }, x, BarY + 84); x += 130; }
        x += 24;
        Put(new Rectangle { Width = 1, Height = BarH - 30, Fill = Theme.B("#605e451c") }, x - 14, BarY + 15);
        Put(Caption("PUSU"), x, BarY + 10);
        foreach (var u in me.Ambush)
        {
            var uu = u;
            Put(CardEl(u.Def, u.Star, 112, "han-ambush:" + u.Uid, () => { _ambush = _ambush == uu.Uid ? null : uu.Uid; Sfx.Play("han_click"); Render(); }, u.Power, _ambush == u.Uid), x, BarY + 30);
            x += 120;
        }
        if (me.Ambush.Count == 0) Put(new TextBlock { Text = "pusuda kart yok", FontFamily = HanUi.Font, FontSize = HanUi.S(14), Foreground = Theme.TextDim }, x, BarY + 84);
        var picked = _signs.Count + (_potion != null ? 1 : 0) + (_ambush != null ? 1 : 0);
        Put(Act("Çarpış", picked == 0 ? "satırı aç" : $"{b.Cost(Mine, CurrentChoice())} Odak harca", "han-go", OnWindowGo, HanButtonKind.Primary, 122, 164, true, picked == 0 ? "Hiçbir şey seçmezsen satır müdahalesiz açılır." : null), GoX, BarY + 14);
    }

    /// <summary>Çarpışma sürerken: iki tarafın bu penceredeki seçimleri yan yana (kim ne attı).</summary>
    private void ChoicesTray(Battle b)
    {
        var o = b.Outcomes.LastOrDefault();
        if (o == null || _hold == 0 && b.Outcomes.Count == 0) return;
        var (mineC, theirsC) = Mine == 0 ? (o.ChoiceA, o.ChoiceB) : (o.ChoiceB, o.ChoiceA);
        void Side(WindowChoice c, bool mine, double x)
        {
            Put(Caption(mine ? "SEN" : "RAKİP"), x, BarY + 12).Foreground = mine ? Theme.B("#ffe9a3") : Theme.B("#f0a090");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var s in c.Signs) { var m = HanArt.SignMedallion(s, 96, true, true, Tuning.SignCost(s)); m.Margin = new Thickness(0, 0, 12, 0); sp.Children.Add(m); }
            if (c.Potion is { } p) { var t = HanArt.IconToken(HanArt.PotionIcon(p), 96, true, "⚗"); t.Margin = new Thickness(0, 0, 12, 0); sp.Children.Add(t); }
            if (c.AmbushUid != null) sp.Children.Add(Txt("PUSU", 26, Theme.B("#ffe9a3"), true));
            if (sp.Children.Count == 0) sp.Children.Add(new TextBlock { Text = "müdahale yok", FontFamily = HanUi.Font, FontSize = HanUi.S(18), Foreground = Theme.TextDim });
            sp.VerticalAlignment = VerticalAlignment.Center;
            Put(new Grid { Height = 140, Children = { sp } }, x, BarY + 36);
        }
        Side(mineC, true, 300);
        Put(Txt("VS", 42, Theme.B("#7a5c24"), true, TextAlignment.Center), 900, BarY + 66).Width = 120;
        Side(theirsC, false, 1140);
    }

    private void EndTray(Battle b, BattleSide me, BattleSide op)
    {
        var w = b.Winner;
        var text = w < 0 ? "Berabere: kimse can kaybetmez."
            : w == Mine ? $"Zafer {me.Points}-{op.Points}!" + (b.Damage > 0 ? $"  {op.Name} {b.Damage} can kaybetti." : "")
            : $"Yenilgi {me.Points}-{op.Points}." + (b.Damage > 0 ? $"  {b.Damage} can kaybettin." : "  Bu round can kaybı yok.");
        Put(Txt(text, 32, w == Mine ? Theme.Good : w < 0 ? Theme.Text : Theme.Bad, true), 300, BarY + 70);
        Put(Act("Devam", "round sonucu", "han-continue", OnBattleContinue, HanButtonKind.Primary, 122, 164), GoX, BarY + 14);
    }

    // ---------- pencere çözümü ----------

    private void OnWindowGo()
    {
        if (_screen != Screen.Battle || _busy) return;
        StopTimer();
        var b = B;
        if (b.Done) return;
        var o = Session.Submit(CurrentChoice());
        _signs.Clear();
        _potion = null;
        _ambush = null;
        if (o == null) return;
        if (!_live) { _hold = 0; Render(); return; }

        _busy = true;
        var gen = ++_gen;
        var row = o.Row;
        _hold = 1;
        Render();
        var castMs = Cast(o, row);
        After(castMs, gen, () => { _hold = 2; Sfx.Play("draw_card"); Render(); Impact(o, row); });
        After(castMs + 1350, gen, () =>
        {
            _hold = 0;
            Render();
            var won = o.Winner == Mine;
            Sfx.Play(o.Winner < 0 ? "battle_draw" : won ? "battle_won" : "battle_lost");
            var c = o.Winner < 0 ? Color.FromRgb(0xe8, 0xdc, 0xc4) : won ? Color.FromRgb(0xa3, 0xe0, 0x78) : Color.FromRgb(0xff, 0x6a, 0x55);
            if (o.Winner >= 0)
            {
                var (rc, rw) = RowS(won, row);
                HanFx.Fountain(_engine, rc, rw * 0.9, won ? FxEngine.Ramp.Gold : FxEngine.Ramp.Blood);
            }
            ScreenBanner(o.Winner < 0 ? "BERABERE" : won ? "SATIR SENİN" : "SATIR RAKİBİN", c);
            foreach (var f in o.Fx.Where(f => f.Kind == "rage"))
            {
                var at = new Point(SideX + SideW / 2, f.Side == Mine ? 800 : 120);
                HanArt.FloatText(_fx, $"ÖFKE  +{f.Target} ODAK", at, Color.FromRgb(0x8f, 0xd8, 0xff), 24, 1400, 300);
                Fx.After(300, () => HanFx.Aura(_engine, new Point(at.X, at.Y + 20), 180, FxEngine.Ramp.Aard, 0.7));
            }
        });
        After(castMs + 2500, gen, () =>
        {
            _busy = false;
            if (b.Done) { Render(); BattleEnd(b, gen); }
            else { StartTimer(6, OnWindowGo); Render(); }
        });
    }

    /// <summary>Ekranın ortasında kısa yazı.</summary>
    private void ScreenBanner(string text, Color c)
    {
        var t = Txt(text, 60, new SolidColorBrush(c), true, TextAlignment.Center, false);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 1 };
        var holder = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = false };
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, -60, 0, 0);
        holder.Children.Add(t);
        _banner.Children.Add(holder);
        Fx.Banner(holder, 650, () => _banner.Children.Remove(holder));
    }

    /// <summary>İşaretin çıktığı yer: kendi tarafında masanın sol önü, rakibinde sol arkası.</summary>
    private static Point Caster(bool mine) => mine ? new Point(560, 800) : new Point(620, 110);

    /// <summary>
    /// Atış: iki tarafın işaretleri havada çizilir (ışıklı kalem, kıvılcımlar), simya kartları elde parlar. Süresini (ms) döner.
    /// </summary>
    private int Cast(WindowOutcome o, Row row)
    {
        var any = false;
        var seen = new HashSet<string>();
        foreach (var f in o.Fx)
        {
            if (!seen.Add(f.Kind + f.Side)) continue;
            var mine = f.Side == Mine;
            var at = Caster(mine);
            Sign? sign = f.Kind switch { "aard" => Sign.Aard, "igni" or "igni_reflect" => Sign.Igni, "quen" => Sign.Quen, "yrden" => Sign.Yrden, _ => null };
            if (sign is { } s)
            {
                any = true;
                var delay = mine ? 0 : 140;
                Fx.After(1 + delay, () => HanFx.DrawGlyph(_fx, _engine, s, at, 150));
                HanArt.FloatText(_fx, (mine ? "" : "Rakip: ") + s.ToString().ToUpperInvariant(), new Point(at.X, at.Y + (mine ? -110 : 105)), HanArt.SignColor(s), 28, 1100, delay + 200);
                Fx.After(1 + delay, () => Sfx.Play(s switch { Sign.Igni => "han_igni_cast", Sign.Quen => "han_quen", Sign.Yrden => "han_yrden", _ => "han_glyph" }));
            }
            else if (f.Kind is "thunderbolt" or "swallow" or "blackblood" or "dancingstar" or "northernwind")
            {
                any = true;
                var icon = f.Kind switch { "thunderbolt" => "potion_thunderbolt", "swallow" => "potion_swallow", "blackblood" => "potion_blackblood", "dancingstar" => "bomb_dancingstar", _ => "bomb_northernwind" };
                var pat = new Point(at.X + 260, at.Y + (mine ? -10 : 10));
                var holder = new Grid { Width = 120, Height = 120 };
                holder.Children.Add(HanArt.Icon(icon, 120, "⚗"));
                HanArt.Burst(_fx, holder, pat, 0.5, 1.05, 900, 80);
                var ramp = f.Kind switch { "thunderbolt" => FxEngine.Ramp.Toxic, "swallow" => FxEngine.Ramp.Ice, "blackblood" => FxEngine.Ramp.Blood, "northernwind" => FxEngine.Ramp.Ice, _ => FxEngine.Ramp.Fire };
                Fx.After(80, () => { _engine.Light(pat, 160, ramp, 1.0f, 0.4f, 0); for (var i = 0; i < 40; i++) _engine.Spark(pat, _engine.R(0, 360), _engine.R(60, 260), _engine.R(0.3f, 0.7f), 3, 2, ramp, 1.2f, 200, 1.5f, true); });
                HanArt.FloatText(_fx, (mine ? "" : "Rakip: ") + HT.Name(f.Kind switch { "thunderbolt" => Potion.Thunderbolt, "swallow" => Potion.Swallow, "blackblood" => Potion.BlackBlood, "dancingstar" => Potion.DancingStar, _ => Potion.NorthernWind }), new Point(pat.X, pat.Y + 85), Color.FromRgb(0xff, 0xe0, 0x9a), 22, 1100, 150);
                Sfx.Play(f.Kind is "dancingstar" or "northernwind" ? "han_whoosh" : "han_drink");
            }
        }
        return any ? 760 : 300;
    }

    /// <summary>Bir kartı titretir (vurulma tepkisi); masa ve ekran yerinde kalır.</summary>
    private void Hit(int uid, double amp = 7)
    {
        if (_unitEls.TryGetValue(uid, out var el)) Fx.Shake(el, amp, 420);
    }

    /// <summary>Etkilerin hedefe inişi: alev seli, şok dalgası, kalkan, mühür, bomba; vurulan kart titrer.</summary>
    private void Impact(WindowOutcome o, Row row)
    {
        foreach (var f in o.Fx)
        {
            var mine = f.Side == Mine;
            var origin = Caster(mine);
            var (foeC, foeW) = RowS(!mine, row);
            var (ownC, ownW) = RowS(mine, row);
            Point Target(bool onFoe) => f.Target != 0 && _pos.TryGetValue(f.Target, out var p) ? p : (onFoe ? foeC : ownC);
            var cardW = foeW / 9;
            switch (f.Kind)
            {
                case "aard":
                    HanFx.Aard(_engine, origin, foeC, foeW);
                    Sfx.Play("han_aard");
                    Fx.After(280, () =>
                    {
                        foreach (var kv in _pos) if (Math.Abs(kv.Value.Y - foeC.Y) < 70) Hit(kv.Key, 8);
                        HanArt.FloatText(_fx, "−%10", new Point(foeC.X + foeW * 0.38, foeC.Y - 40), HanArt.SignColor(Sign.Aard), 44, 1200);
                    });
                    break;
                case "igni" when f.Blocked:
                    HanFx.Igni(_engine, origin, new Point(foeC.X, foeC.Y - 40), cardW, false);
                    Sfx.Play("han_igni");
                    Fx.After(380, () => Sfx.Play("han_quen_hit"));
                    Fx.After(420, () => HanArt.FloatText(_fx, "ENGELLENDİ", new Point(foeC.X, foeC.Y - 90), HanArt.SignColor(Sign.Quen), 46, 1300));
                    break;
                case "igni":
                case "igni_reflect":
                {
                    var p = Target(f.Kind == "igni");
                    var landed = f.Target != 0;
                    HanFx.Igni(_engine, origin, p, cardW, landed);
                    Sfx.Play("han_igni");
                    Fx.After(400, () =>
                    {
                        if (landed) { Hit(f.Target, 9); HanArt.FloatText(_fx, f.Kind == "igni" ? "−%50" : "YANSIDI  −%50", new Point(p.X, p.Y - 90), Color.FromRgb(0xff, 0xa0, 0x40), 46, 1300); }
                        else HanArt.FloatText(_fx, "hedef yok", p, Color.FromRgb(0xa3, 0x98, 0x80), 28, 900);
                    });
                    break;
                }
                case "quen":
                    HanFx.Quen(_fx, _engine, ownC, ownW, f.Blocked);
                    if (f.Blocked) Fx.After(620, () => Sfx.Play("han_quen_hit"));
                    break;
                case "yrden":
                    HanFx.Yrden(_fx, _engine, foeC, foeW, f.Blocked);
                    Fx.After(150, () => Sfx.Play("han_yrden_shock"));
                    if (f.Blocked) Fx.After(900, () => Sfx.Play("han_yrden_hit"));
                    Fx.After(350, () => HanArt.FloatText(_fx, f.Blocked ? "MÜHÜRLENDİ" : "boşa gitti", new Point(foeC.X, foeC.Y - 80), HanArt.SignColor(Sign.Yrden), f.Blocked ? 46 : 28, 1300));
                    break;
                case "thunderbolt":
                    HanFx.Aura(_engine, ownC, ownW * 0.85, FxEngine.Ramp.Toxic);
                    Sfx.Play("han_potion");
                    HanArt.FloatText(_fx, "+%25", new Point(ownC.X + ownW * 0.38, ownC.Y - 40), Color.FromRgb(0x9f, 0xf0, 0x9a), 44, 1200, 200);
                    break;
                case "swallow":
                    HanFx.Aura(_engine, ownC, ownW * 0.85, FxEngine.Ramp.Ice);
                    Sfx.Play("han_potion");
                    HanArt.FloatText(_fx, "HAVA KALKTI", new Point(ownC.X, ownC.Y - 80), Color.FromRgb(0x9a, 0xd8, 0xff), 38, 1200, 200);
                    break;
                case "blackblood":
                    if (f.Blocked)
                    {
                        HanFx.Aura(_engine, ownC, ownW * 0.6, FxEngine.Ramp.Blood, 0.8);
                        Sfx.Play("han_potion");
                        HanArt.FloatText(_fx, "KARA KAN: YANSIDI", new Point(ownC.X, ownC.Y - 80), Color.FromRgb(0xff, 0x60, 0x70), 36, 1300, 300);
                    }
                    break;
                case "dancingstar":
                case "northernwind":
                {
                    var p = Target(true);
                    var ice = f.Kind == "northernwind";
                    var landed = f.Target != 0;
                    HanFx.Throw(_engine, new Point(origin.X + 260, origin.Y), p, ice ? FxEngine.Ramp.Ice : FxEngine.Ramp.Ember, 520, () =>
                    {
                        if (ice) HanFx.Frost(_engine, p); else HanFx.Explosion(_engine, p, FxEngine.Ramp.Fire, 1.2);
                        if (!ice && landed) HanFx.Burn(_engine, p, cardW, 1.0, 0.7);
                        Sfx.Play(ice ? "han_bomb_frost" : "han_bomb_fire");
                        if (landed) { Hit(f.Target, 9); HanArt.FloatText(_fx, ice ? "−%70" : "−%30", new Point(p.X, p.Y - 90), ice ? Color.FromRgb(0xb0, 0xe0, 0xff) : Color.FromRgb(0xff, 0xa0, 0x40), 46, 1300); }
                    });
                    break;
                }
                case "ambush":
                    if (_pos.TryGetValue(f.Target, out var ap))
                    {
                        for (var i = 0; i < 40; i++) _engine.Puff(new Point(ap.X + _engine.R(-50, 50), ap.Y + 50), _engine.R(-170, -10), _engine.R(60, 240), _engine.R(0.6f, 1.1f), 30, 110, 0.18f, -20, 80);
                        _engine.Light(ap, 240, FxEngine.Ramp.Gold, 1.0f, 0.35f);
                        HanArt.FloatText(_fx, "PUSU!", new Point(ap.X, ap.Y - 100), Color.FromRgb(0xff, 0xe9, 0xa3), 46, 1200);
                    }
                    Sfx.Play("han_place");
                    break;
            }
        }
    }

    /// <summary>Savaş sonu: zafer/yenilgi afişi, sonra hasar mermisi kaybedenin can çubuğuna uçar.</summary>
    private void BattleEnd(Battle b, int gen)
    {
        var w = b.Winner;
        Sfx.Play(w < 0 ? "battle_draw" : w == Mine ? "han_victory" : "han_defeat");
        var el = HanArt.ResultEmblem(w < 0 ? 0 : w == Mine ? 1 : -1, w < 0 ? "Berabere" : w == Mine ? "Zafer!" : "Yenilgi", 520);
        var holder = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = false, Opacity = 0 };
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.VerticalAlignment = VerticalAlignment.Center;
        el.Margin = new Thickness(0, -80, 0, 0);
        holder.Children.Add(new Rectangle { Fill = new RadialGradientBrush(Color.FromArgb(0xa0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) { RadiusX = 0.5, RadiusY = 0.45 } });
        holder.Children.Add(el);
        _banner.Children.Add(holder);
        Fx.Banner(holder, 1100, () => _banner.Children.Remove(holder));
        if (w < 0 || b.Damage <= 0) return;
        var loser = b.Sides[1 - w].PlayerId;
        if (loser < 0 || !_hpAt.TryGetValue(loser, out var hp)) return;
        var from = RowS(w == Mine, Row.Ranged).Center;
        After(1300, gen, () => Projectile(from, hp, b.Damage, loser == HanSession.Me));
    }

    private void Projectile(Point from, Point to, int dmg, bool me)
    {
        Sfx.Play("han_whoosh");
        HanFx.Fireball(_engine, from, to, 700, () =>
        {
            HanArt.FloatText(_fx, "−" + dmg, new Point(to.X - 50, to.Y), Color.FromRgb(0xff, 0x5a, 0x48), 40, 1400);
            Sfx.Play(me ? "han_damage" : "han_explosion");
        });
    }

    private void OnBattleContinue()
    {
        StopTimer();
        _gen++;
        _fx.Children.Clear();
        _engine.Clear();
        _banner.Children.Clear();
        Session.FinishRound();
        _screen = Session.Over || !Me.Alive ? Screen.Final : Screen.Result;
        Render();
    }

    // ---------- round sonucu ----------

    private void DrawResult()
    {
        var round = Session.LastRound;
        PlayerList();
        Put(new Rectangle { Width = ListX - 8, Height = 1080, Fill = Theme.B("#a8000000"), IsHitTestVisible = false }, 0, 0);
        TopButtons();
        var recs = M.History.Where(h => h.Round == round).ToList();
        var mine = recs.FirstOrDefault(h => h.PlayerA == HanSession.Me || (!h.Pve && !h.Ghost && h.PlayerB == HanSession.Me));
        var sp = new StackPanel { Margin = new Thickness(46, 32, 46, 28) };
        sp.Children.Add(HanUi.Label($"Round {round} sonucu", 13));
        if (mine != null)
        {
            var side = mine.PlayerA == HanSession.Me ? 0 : 1;
            var (mp, opp) = side == 0 ? (mine.PointsA, mine.PointsB) : (mine.PointsB, mine.PointsA);
            var lost = mine.Winner == 1 - side ? mine.Damage : 0;
            var won = mine.Winner == side;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var h1 = HanUi.Display(mine.Winner < 0 ? "Berabere" : won ? "Zafer" : "Yenilgi", 54, TextAlignment.Left);
            if (!won && mine.Winner >= 0) h1.Foreground = new LinearGradientBrush(Color.FromRgb(0xff, 0xb4, 0xa4), HanUi.Bad, 90);
            head.Children.Add(h1);
            var sc = HanUi.Number($"  {mp} – {opp}", 46, HanUi.Br(HanUi.Parchment));
            sc.VerticalAlignment = VerticalAlignment.Bottom;
            head.Children.Add(sc);
            sp.Children.Add(head);
            if (mp == opp && mine.Winner >= 0) sp.Children.Add(HanUi.Small("Çarpışmalar eşitti: üç satırın toplam gücü belirledi.", 14));
            sp.Children.Add(HanUi.Body(lost > 0 ? $"{lost} can kaybettin. Kalan can: {Me.Hp}" : won ? $"+{Tuning.WinGold} altın kazandın. Can: {Me.Hp}" : $"Can kaybı yok. Can: {Me.Hp}", 18));
            if (Me.Streak <= -2 || Me.Streak >= 3)
            {
                var chip = HanUi.Chip(Me.Streak < 0 ? $"{-Me.Streak} yenilgi serisi · sonraki round +{Tuning.StreakGold(Me.Streak)} altın" : $"{Me.Streak} galibiyet serisi · sonraki round +{Tuning.StreakGold(Me.Streak)} altın", Me.Streak < 0 ? HanUi.Ember : HanUi.Good, 12, true);
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                chip.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(chip);
            }
        }
        var rule = HanUi.Rule(900);
        rule.Margin = new Thickness(0, 18, 0, 10);
        sp.Children.Add(rule);
        sp.Children.Add(HanUi.Label("Diğer masalar", 12));
        foreach (var h in recs)
        {
            var me = h.PlayerA == HanSession.Me || h.PlayerB == HanSession.Me && !h.Ghost;
            var line = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            var a = HanUi.Title(h.NameA, 17, me ? HanUi.GoldText : null, TextAlignment.Right);
            var sc = HanUi.Number($"{h.PointsA} – {h.PointsB}", 17, HanUi.Br(HanUi.Parchment));
            sc.HorizontalAlignment = HorizontalAlignment.Center;
            var b = HanUi.Title(h.NameB, 17, me ? HanUi.GoldText : null);
            var res = HanUi.Small(h.Winner < 0 ? "berabere" : $"{(h.Winner == 0 ? h.NameA : h.NameB)} kazandı" + (h.Damage > 0 ? $" · {h.Damage} hasar" : ""), 14);
            Grid.SetColumn(sc, 1); Grid.SetColumn(b, 2); Grid.SetColumn(res, 3);
            res.Margin = new Thickness(16, 0, 0, 0);
            foreach (var el in new UIElement[] { a, sc, b, res }) line.Children.Add(el);
            sp.Children.Add(line);
        }
        var dead = M.Players.Where(p => !p.Alive && p.EliminatedRound == round).ToList();
        if (dead.Count > 0)
        {
            var d = HanUi.Title("Elenenler: " + string.Join(", ", dead.Select(p => $"{p.Name} ({p.Place}.)")), 17, HanUi.Br(HanUi.Bad));
            d.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(d);
        }
        var next = Btn($"Round {M.Round}", "han-next", EnterPrep, true, width: 280, height: 56, sub: "hazırlığa geç");
        next.HorizontalAlignment = HorizontalAlignment.Left;
        next.Margin = new Thickness(0, 26, 0, 0);
        sp.Children.Add(next);
        Put(Panel(1040, 620, sp, 0xf0, null, 16), 330, 220);
    }
}
