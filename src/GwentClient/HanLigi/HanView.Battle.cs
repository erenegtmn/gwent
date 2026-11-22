using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;
using HT = HanLigi.Texts;
using HTu = HanLigi.Tuning;

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

    private Battle B => (_screen == Screen.Result ? _resultBattle : null) ?? Session.MyBattle!;

    /// <summary>Biten savaş: round kapanınca oturumdan silinir; sonuç paneli onu arkada (masada) göstermek için tutulur.</summary>
    private Battle? _resultBattle;
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
        var left = Side(true, me.Name, Face(Session.Me), $"Seviye {me.Level} · {SchoolName(me.School)}");
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

    /// <param name="behindResult">Round sonucu panelinin arkasında: alt şerit (Devam) çizilmez, masa yalnız gösterilir.</param>
    private void DrawArena(bool behindResult = false)
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
        else if (b.Done && !_busy)
        {
            hint = Session.Online ? "Sonraki round, bütün masalar bitince herkes için birlikte başlar." : "Savaş bitti: masaya bakabilir, sonra devam edebilirsin.";
            hc = null;
        }
        else if (_busy) hint = $"{HT.Name(b.Order[Math.Max(0, current)])} satırı çarpışıyor...";
        else
        {
            hint = $"{b.Window + 1}. çarpışma: {HT.Name(b.CurrentRow)}. İşaret ya da simya seç, sonra Satırı aç.";
            hc = Theme.B("#ffe9a3");
        }
        if (behindResult) hint = "Round bitti.";
        if (activation) TopBar("SAVAŞ · HAZIRLAN", hint, hc);
        else ScoreBar(myPts, opPts, hint, hc);
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

        // açılmış satırlar (rakibin Boynuz/Kalkan'ı ancak satırı açılınca görünür)
        bool Open(Row row) { var i = Array.IndexOf(b.Order, row); return i < shownWindows || (i == resolving && _hold == 2); }
        var specials = new[] { VisibleSpecials(b, 1 - Mine, false, Open), VisibleSpecials(b, Mine, true, Open) };   // [0] rakip, [1] ben

        // ---- satırlar ----
        for (var i = 0; i < 3; i++)
        {
            var r = b.Order[i];
            var revealed = i < shownWindows || (i == resolving && _hold == 2);
            var scored = i < shownWindows;
            var o = i < b.Outcomes.Count ? b.Outcomes[i] : null;
            var (mv, ov) = o == null ? (0m, 0m) : Mine == 0 ? (o.ValueA, o.ValueB) : (o.ValueB, o.ValueA);
            decimal Shown(BattleSide s, BattleUnit u) => (scored || (o != null && _stepShown.Contains(u.Uid))) && o!.CardValues.TryGetValue(u.Uid, out var fv) ? fv : b.Value(s, u);

            if (i == current)
                foreach (var mine in new[] { false, true })
                    Trap(RowRect(mine, r), Theme.B("#1effe08a"), Theme.Select, 3, 3, new DropShadowEffect { Color = Color.FromRgb(0xff, 0xd8, 0x70), BlurRadius = 18, ShadowDepth = 0 });
            if (scored && o!.Winner >= 0)
                Trap(RowRect(o.Winner == Mine, r), new SolidColorBrush(o.Winner == Mine ? Color.FromArgb(0x34, 0x8f, 0xd0, 0x62) : Color.FromArgb(0x34, 0xe0, 0x5a, 0x48)));

            // benim satırım
            var myUnits = me.In(r).Where(u => !(i == resolving && _hold == 1 && u.Landed)).ToList();
            var myCards = myUnits.Select(u => (u.Uid, (FrameworkElement)CardEl(u.Def, u.Star, DesignW, null, null, Shown(me, u), footer: u.Token ? "hayalet" : u.Summoned ? "çağrıldı" : null))).ToList();
            PlaceRow(myCards, true, r, _pos);
            PutU(ScoreBadge(scored ? HTu.Fmt(mv) : b.Started ? HTu.Fmt(b.PlainRow(Mine, r)) : me.In(r).Sum(u => u.Power).ToString(), true, scored && o!.Winner == Mine), new Point(RowX + RowW - 36, RowY(true, r) + RowH / 2));

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

            // satır yüzdeleri: kart toplamı ile satır puanı farklıysa nedeni rozetin altında kalır (Σ7 · Aard −%10)
            if (scored)
            {
                RowModChip(o!, Mine, true, r);
                RowModChip(o!, 1 - Mine, false, r);
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
                PutU(num, new Point(ColX + ColW * (specials[mine ? 1 : 0].Any(x => x.Row == r) ? 0.24 : 0.5), RowY(mine, r) + RowH / 2 + 8), 0, _rows);   // özel kart varsa sütunun solunda
                RowLabel(mine, r, HT.Name(r), i == current ? Theme.B("#c0ffe9a3") : null);
            }

            // Boynuz / Kalkan / Moral hedefleri: satırın sağ ucunda küçük işaretler
            var act = b.Started ? me.Act : _act;
            var marks = act.Horns.Where(h => h == r).Select(_ => "Boynuz +%50").Concat(act.Shields.Where(h => h == r).Select(_ => "Kalkan")).Concat(act.Morale.Where(kv => kv.Value == r).Select(_ => "Moral")).ToList();
            if (marks.Count > 0 && !(scored && o!.RowMods.Any(m => m.Side == Mine)))   // çözülmüş satırda döküm çipi zaten gösterir
            {
                var chip = new Border
                {
                    Background = Theme.B("#e01a1209"), BorderBrush = Theme.B("#c9a24b"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2),
                    Child = new TextBlock { Text = string.Join(" · ", marks), FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = Theme.B("#ffe2a0") }
                };
                PutU(chip, new Point(RowX + RowW - 36, RowY(true, r) + 12));
            }
        }
        // iki tarafın özel kartları: satırların sol sütununda, etkilediği satırın hizasında
        SpecialCards(specials[1], true);
        SpecialCards(specials[0], false);

        // ---- sol: iki tarafın durumu ----
        SideCard(false, op, b.Pve ? (M.Round <= 2 ? "arachas" : "face_imlerith") : Face(op.PlayerId), PointsShown(1 - Mine, shownWindows), b.Started, 66);
        if (!behindResult) TablesPanel(b, shownWindows, activation);   // round kapanınca masalar sonuç panelinde
        SideCard(true, me, Face(Session.Me), PointsShown(Mine, shownWindows), b.Started, 742);

        // ---- alt şerit ----
        if (behindResult) return;
        Put(Panel(1896, BarH, null, 0xec), 12, BarY);
        if (_battleShop) BattleBenchTray();   // savaşta tezgâh açık: şerit depo satırıdır (HanView.BattleShop.cs)
        else if (activation) ActivationTray(b, me);
        else if (b.Done && !_busy) EndTray(b, me, op);
        else if (!_busy) SignTray(b, me);
        else ChoicesTray(b);
        BattleShopButton();
        if (_battleShop) ShopOverlay(null, new());
    }

    /// <summary>
    /// Çözülmüş satırın puan rozetinin altına kalıcı döküm: kart toplamı ve satır yüzdeleri (kaynağıyla). Kart rozetleri yüzdeden
    /// önceki değeri, satır rozeti sonrakini gösterir; aradaki fark böylece görünür. Üstüne gelince tam hesap.
    /// </summary>
    private void RowModChip(WindowOutcome o, int side, bool mine, Row r)
    {
        var mods = o.RowMods.Where(m => m.Side == side).ToList();
        if (mods.Count == 0) return;
        var sum = o.RowBase[side];
        var val = side == 0 ? o.ValueA : o.ValueB;
        static string Pct(double p) => (p < 0 ? "−" : "+") + "%" + Math.Round(Math.Abs(p) * 100);
        var total = mods.Sum(m => m.Pct);
        var color = total < 0 ? HanUi.Bad : total > 0 ? HanUi.Good : HanUi.Gold;
        var tb = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = HanUi.Br(HanUi.Parchment) };
        tb.Inlines.Add(new System.Windows.Documents.Run($"Σ{HTu.Fmt(sum)}"));
        foreach (var m in mods)
            tb.Inlines.Add(new System.Windows.Documents.Run($"  {m.Source} {Pct(m.Pct)}") { Foreground = HanUi.Br(m.Pct < 0 ? HanUi.Bad : HanUi.Good) });
        tb.Inlines.Add(new System.Windows.Documents.Run($"  = {HTu.Fmt(val)}"));
        var chip = new Border
        {
            Background = Theme.B("#e8120c07"), BorderBrush = HanUi.Br(color, 0xc0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 1, 7, 2), Child = tb,
            ToolTip = $"Kartların toplamı: {HTu.Fmt(sum)}\n" + string.Join("\n", mods.Select(m => $"{m.Source}: {Pct(m.Pct)}"))
                      + $"\nSatır: {HTu.Fmt(sum)} × (1 {(total < 0 ? "−" : "+")} %{HT.Pct(Math.Abs(total) * 100)}) = {HTu.Fmt(val)} (en yakın 0,1)"
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var at = HanScene.ToScreen(new Point(RowX + RowW - 36, RowY(mine, r) + RowH / 2));
        Canvas.SetLeft(chip, Math.Min(at.X + 30, ListX - 10) - chip.DesiredSize.Width);
        Canvas.SetTop(chip, at.Y + 26);
        System.Windows.Controls.Panel.SetZIndex(chip, 100001);
        _units.Children.Add(chip);
    }

    /// <summary>
    /// Bir tarafın masada görünen özel kartları (Boynuz, Kalkan, hava, Kavurma), etkilediği satırla. Hava ve Kavurma savaş
    /// başında uygulandığı için baştan görünür. Rakibin Boynuz/Kalkan'ı ise <b>satırı açılınca</b> görünür (önceden görünse
    /// diğer satırlara yüklenip avantajını boşa çıkarmak kolaylaşırdı); casus onu açtıysa önceden "casus görüyor" diye görünür.
    /// Kendi kartların hep görünür; satırı seçilmemişse ortada "satır seçiyor" diye bekler.
    /// </summary>
    private List<(Row Row, CardDef Def, string? Footer)> VisibleSpecials(Battle b, int side, bool mine, Func<Row, bool> open)
    {
        var s = b.Sides[side];
        var act = mine ? (b.Started ? s.Act : _act) : s.Act;
        var spied = mine ? new List<CardDef>() : b.Revealed[Mine].Where(x => x.Zone == "Özel").Select(x => x.Def).ToList();
        var horn = 0;
        var shield = 0;
        var placed = new List<(Row Row, CardDef Def, string? Footer)>();
        foreach (var sp in s.Specials)
        {
            var def = CardSet.All.First(d => d.Special == sp);
            Row? r = Match.WeatherRow(sp);
            string? footer = sp == Special.Scorch ? "savaş başında" : null;
            if (sp is Special.Horn or Special.Shield)
            {
                var list = sp == Special.Horn ? act.Horns : act.Shields;
                var n = sp == Special.Horn ? horn++ : shield++;
                r = (mine || b.Started) && n < list.Count ? list[n] : null;
                if (!mine)
                {
                    var spy = spied.Remove(def);
                    if (r is { } rr && open(rr)) { }
                    else if (spy) footer = "casus görüyor";
                    else continue;   // rakibin Boynuz/Kalkan'ı satırı açılana dek gizli
                }
                if (r == null) footer ??= "satır seçiyor";
            }
            placed.Add((r ?? Row.Ranged, def, footer));
        }
        return placed;
    }

    private void SpecialCards(List<(Row Row, CardDef Def, string? Footer)> placed, bool mine)
    {
        foreach (var grp in placed.GroupBy(x => x.Row))
        {
            var k = 0;
            foreach (var (row, def, footer) in grp)
            {
                var (top, bottom, _, _) = Band(mine, row);
                var el = CardEl(def, 1, DesignW, null, null, footer: footer);
                if (footer is "satır seçiyor" or "casus görüyor") el.Opacity = 0.8;
                PlaceCard(el, HanScene.ToScreen(new Point(ColX + ColW * 0.68 + k * 22, RowY(mine, row) + RowH - 5)), (bottom - top) * 0.86 / (DesignH * Lean), k);   // sütunun sağı: sıra numarası solda görünsün
                k++;
            }
        }
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
        g.Children.Add(new TextBlock { Text = $"Seviye {s.Level}", FontFamily = HanUi.Font, FontSize = HanUi.S(13), FontWeight = FontWeights.SemiBold, Foreground = Theme.B("#e8d49a"), Margin = new Thickness(76, 33, 0, 0) });
        // okul: oyuncu çerçevesinin köşesinde simge (üstüne gelince açıklama)
        g.Children.Add(SchoolPip(s.School, s.Th(global::HanLigi.Tag.Mage), 34, mine, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 8, 8, 0)));
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
        fo.Children.Add(new TextBlock { Text = $"  {focus} Odak  ·  {s.Potions.Count} simya", FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.SemiBold, Foreground = Theme.B("#cfefff"), VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(fo);
        Put(g, SideX, y);
    }

    /// <summary>
    /// Sol orta: bu round'un bütün masaları canlı (kim kiminle, kaçıncı satırda, skor; biten masanın kazananı). Kendi masam,
    /// sonucu ekranda gösterilene kadar yalnız gösterilen skoru verir. Altta casus bilgisi ve savaş öncesi olaylar.
    /// </summary>
    private void TablesPanel(Battle mineB, int shownWindows, bool activation)
    {
        var sp = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        var head = new Grid();
        head.Children.Add(Caption("MASALAR"));
        var done = M.Battles.Count(x => x.Done && (x != mineB || _resultShown));
        var cnt = HanUi.Small($"{done}/{M.Battles.Count} bitti", 11);
        cnt.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(cnt);
        sp.Children.Add(head);
        foreach (var b in M.Battles.OrderByDescending(x => x == mineB))
        {
            var mine = b == mineB;
            var side = mine ? Mine : 0;
            var (left, right) = (b.Sides[side], b.Sides[1 - side]);
            var rightName = b.Pve ? (M.Round <= 2 ? "Canavar Sürüsü" : "Vahşi Av") : right.Name;
            var finished = b.Done && (!mine || _resultShown);
            var (lp, rp) = mine ? (PointsShown(Mine, shownWindows), PointsShown(1 - Mine, shownWindows)) : (left.Points, right.Points);
            var cell = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            cell.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(5), Background = mine ? Theme.B("#402a1e0a") : Theme.B("#30000000"),
                BorderBrush = mine ? HanUi.Br(HanUi.Gold, 0x90) : HanUi.Br(HanUi.GoldDeep, 0x50), BorderThickness = new Thickness(1)
            });
            var col = new StackPanel { Margin = new Thickness(8, 4, 8, 5) };
            var names = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(13), TextTrimming = TextTrimming.CharacterEllipsis };
            Brush NameBrush(int s) => finished && b.Winner == s ? HanUi.GoldText : finished && b.Winner >= 0 ? HanUi.Br(HanUi.Muted) : HanUi.Br(HanUi.Parchment);
            names.Inlines.Add(new System.Windows.Documents.Run(mine ? "Sen" : left.Name) { Foreground = NameBrush(side), FontWeight = FontWeights.Bold });
            names.Inlines.Add(new System.Windows.Documents.Run("  ⚔  ") { Foreground = HanUi.Br(HanUi.GoldDeep) });
            names.Inlines.Add(new System.Windows.Documents.Run(rightName) { Foreground = NameBrush(1 - side), FontWeight = FontWeights.Bold });
            col.Children.Add(names);
            var status = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12), Margin = new Thickness(0, 1, 0, 0) };
            status.Inlines.Add(new System.Windows.Documents.Run($"{lp} – {rp}") { FontWeight = FontWeights.Bold, Foreground = HanUi.Br(HanUi.Parchment) });
            var tail = finished
                ? (b.Winner < 0 ? "   berabere" : $"   {(b.Winner == side ? (mine ? "kazandın" : left.Name + " kazandı") : (mine ? "kaybettin" : rightName + " kazandı"))}" + (b.Damage > 0 && b.Winner >= 0 ? $" · −{b.Damage} can" : ""))
                : b.Started ? $"   {Math.Min(3, (mine ? shownWindows : b.Window) + 1)}. satır" : "   hazırlanıyor";
            status.Inlines.Add(new System.Windows.Documents.Run(tail) { Foreground = finished ? (b.Winner == side && mine ? HanUi.Br(HanUi.Good) : mine && b.Winner >= 0 ? HanUi.Br(HanUi.Bad) : HanUi.Br(HanUi.Muted)) : HanUi.Br(HanUi.Muted) });
            col.Children.Add(status);
            cell.Children.Add(col);
            sp.Children.Add(cell);
        }
        // casus ve savaş öncesi olaylar (kısa)
        var notes = new List<(string, Brush)>();
        if (b0() is { } amb) notes.Add(($"Casus: rakibin pusuda {amb} kartı var.", Theme.B("#ffe9a3")));
        else if (activation && mineB.Revealed[Mine].Count == 0) notes.Add(("Casusun yok: rakibin satırları sis altında.", Theme.TextDim));
        foreach (var l in mineB.PreLog.TakeLast(4)) notes.Add(("• " + l, Theme.TextDim));
        if (notes.Count > 0)
        {
            var rule = HanUi.Rule(SideW - 30);
            rule.Margin = new Thickness(0, 12, 0, 4);
            sp.Children.Add(rule);
            foreach (var (t, c) in notes) sp.Children.Add(new TextBlock { Text = t, FontFamily = HanUi.Font, FontSize = HanUi.S(12), Foreground = c, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }
        var border = Panel(SideW, 540, sp, 0xb8);
        border.ClipToBounds = true;
        Put(border, SideX, 196);

        int? b0() => mineB.AmbushSeen[Mine];
    }

    /// <summary>Savaşta üst şerit: solda skor (sayı değişince yavaşça belirir), yanında tek cümlelik yönlendirme.</summary>
    private void ScoreBar(int mine, int theirs, string hint, Brush? hc)
    {
        var g = new Grid { Width = TopW, Height = 50 };
        g.Children.Add(Panel(TopW, 50, null, 0xdc, null, 12));
        var score = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 0, 2) };
        TextBlock Lab(string t, Brush c) { var l = HanUi.Label(t, 11, c); l.VerticalAlignment = VerticalAlignment.Center; return l; }
        score.Children.Add(Lab("Sen", HanUi.GoldText));
        var a = HanUi.Number(" " + mine, 30, mine > theirs ? HanUi.GoldText : HanUi.Br(HanUi.Parchment));
        var dash = HanUi.Number(" – ", 26, HanUi.Br(HanUi.GoldDeep));
        var b = HanUi.Number(theirs + " ", 30, theirs > mine ? HanUi.Br(Color.FromRgb(0xff, 0x9a, 0x88)) : HanUi.Br(HanUi.Parchment));
        foreach (var t in new[] { a, dash, b }) { t.VerticalAlignment = VerticalAlignment.Center; score.Children.Add(t); }
        score.Children.Add(Lab("Rakip", HanUi.Br(Color.FromRgb(0xf0, 0xa0, 0x90))));
        g.Children.Add(score);
        score.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var h = HanUi.T(hint, hint.Length > 64 ? 15 : 17, hc ?? HanUi.Br(HanUi.Parchment), FontWeights.SemiBold, TextAlignment.Center);
        h.VerticalAlignment = VerticalAlignment.Center;
        h.TextTrimming = TextTrimming.CharacterEllipsis;
        h.Margin = new Thickness(16 + score.DesiredSize.Width + 18, 0, 64, 2);
        g.Children.Add(h);
        g.Children.Add(new Rectangle { Width = 1, Height = 28, Fill = HanUi.Br(HanUi.GoldDeep, 0x80), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16 + score.DesiredSize.Width + 8, 0, 0, 0) });
        Put(g, TopX, 6);
        TopButtons();
        if (_live && _scoreShown.Mine >= 0)
        {
            if (mine != _scoreShown.Mine) FadeIn(a);
            if (theirs != _scoreShown.Theirs) FadeIn(b);
        }
        _scoreShown = (mine, theirs);
    }

    /// <summary>Sayı yerine yavaşça belirir (hafif yukarıdan iner).</summary>
    private static void FadeIn(FrameworkElement el)
    {
        var tt = new TranslateTransform(0, -12);
        el.RenderTransform = tt;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = ease });
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
                face.Children.Add(new TextBlock { Text = $"{HT.Name(r)} {HTu.Fmt(b.PlainRow(Mine, r))}", FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = on ? Theme.Select : Theme.Text, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
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
            if (me.Units.FirstOrDefault(x => x.Uid == uid) is not { } u) { _act.Morale.Remove(uid); continue; }   // önceki savaştan kalmış hedef
            RowChoice($"Moral +{u.Star}", u.Def, "han-morale:" + uid, _act.Morale[uid], r => _act.Morale[uid] = r);
        }
        if (!any) sp.Children.Add(new TextBlock { Text = "Hedef seçecek kartın yok. Satırlar numara sırasıyla açılacak: üç çarpışmadan ikisini alan savaşı kazanır.", FontFamily = HanUi.Font, FontSize = HanUi.S(18), Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Width = 1200 });
        Put(new Grid { Width = 1700, Height = BarH, Children = { sp } }, 40, BarY);
        Put(Act("Savaş", "başlasın", "han-act-go", OnActivationDone, HanButtonKind.Primary, 122, 164), GoX, BarY + 14);
    }

    private void OnActivationDone()
    {
        if (_screen != Screen.Activation || _waitAct) return;
        StopTimer();
        Session.StartBattle(_act);
        if (B.Started) BeginBattle();
        else { _waitAct = true; Render(); }   // online: rakip hedeflerini seçiyor
    }

    private void BeginBattle()
    {
        _waitAct = false;
        _signs.Clear();
        _potion = null;
        _ambush.Clear();
        _screen = Screen.Battle;
        _hold = 0;
        var b = B;
        if (!_live) { Render(); if (b.Done) AfterBattle(); return; }
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
        After(t + 150, gen, () =>
        {
            _busy = false;
            if (b.Done) { Render(); BattleEnd(b, gen); return; }   // savaş ben dönmeden bitti (süre aşımı)
            StartTimer(HanTimes.Window, OnWindowGo);
            Render();
        });
    }

    // ---------- işaret çubuğu ----------

    private WindowChoice CurrentChoice() => WindowChoice.With(_signs.ToArray(), _potion, _ambush);

    private void SignTray(Battle b, BattleSide me)
    {
        var maxSigns = b.CanDouble(Mine) ? 2 : 1;
        var foe = b.Sides[1 - Mine];
        var x = 40.0;
        Put(Caption("İŞARETLER"), x, BarY + 10);
        foreach (var s in Enum.GetValues<Sign>())
        {
            var on = _signs.Contains(s);
            var trial = on ? _signs.Where(v => v != s).ToList() : _signs.Count < maxSigns ? _signs.Append(s).ToList() : new List<Sign> { s };
            var ok = on || b.Valid(Mine, WindowChoice.With(trial.ToArray(), _potion, _ambush));
            var cost = b.Cost(Mine, new WindowChoice(new[] { s }));
            var med = HanArt.SignMedallion(s, 104, on, ok, cost);
            Put(Bare(med, "han-sign:" + s, () => { _signs.Clear(); _signs.AddRange(trial); Sfx.Play("han_click"); Render(); }, ok, $"{s}\n{HT.Describe(s, me.School, me.Th(global::HanLigi.Tag.Mage))}"), x, BarY + 32);
            x += 122;
        }
        x += 24;
        Put(new Rectangle { Width = 1, Height = BarH - 30, Fill = Theme.B("#605e451c") }, x - 14, BarY + 15);
        Put(Caption("SİMYA"), x, BarY + 10);
        foreach (var p in me.Potions.Distinct())
        {
            var pp = p;
            var tok = HanArt.IconToken(HanArt.PotionIcon(p), 96, _potion == p, HT.Name(p)[..1]);
            Put(Bare(tok, "han-use:" + p, () => { _potion = _potion == pp ? null : pp; Sfx.Play("han_click"); Render(); }, true, HT.Name(p) + "\n" + HT.Describe(p, me.School, foe.Th(global::HanLigi.Tag.Dwarf) >= 1, foe.In(b.CurrentRow).Any(u => u.Def.Hero == Hero.Zoltan))), x, BarY + 36);
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
            Put(CardEl(u.Def, u.Star, 112, "han-ambush:" + u.Uid, () => { if (!_ambush.Remove(uu.Uid)) _ambush.Add(uu.Uid); Sfx.Play("han_click"); Render(); }, u.Power, _ambush.Contains(u.Uid)), x, BarY + 30);
            x += 120;
        }
        if (me.Ambush.Count == 0) Put(new TextBlock { Text = "pusuda kart yok", FontFamily = HanUi.Font, FontSize = HanUi.S(14), Foreground = Theme.TextDim }, x, BarY + 84);
        var picked = _signs.Count + (_potion != null ? 1 : 0) + _ambush.Count;
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
            if (c.AmbushUids.Count > 0) sp.Children.Add(Txt(c.AmbushUids.Count > 1 ? $"PUSU ×{c.AmbushUids.Count}" : "PUSU", 26, Theme.B("#ffe9a3"), true));
            if (sp.Children.Count == 0) sp.Children.Add(new TextBlock { Text = "müdahale yok", FontFamily = HanUi.Font, FontSize = HanUi.S(18), Foreground = Theme.TextDim });
            sp.VerticalAlignment = VerticalAlignment.Center;
            Put(new Grid { Height = 140, Children = { sp } }, x, BarY + 36);
        }
        Side(mineC, true, 300);
        Put(Txt("VS", 42, Theme.B("#7a5c24"), true, TextAlignment.Center), 900, BarY + 66).Width = 120;
        Side(theirsC, false, 1140);
    }

    /// <summary>Savaş bitti: alt şeritte üç çarpışmanın dökümü (satır, iki değer, kazanan) ve kısa sonuç. Yerelde "Devam".</summary>
    private void EndTray(Battle b, BattleSide me, BattleSide op)
    {
        Put(Caption("ÇARPIŞMALAR"), 40, BarY + 10);
        var x = 40.0;
        foreach (var o in b.Outcomes)
        {
            var (mv, ov) = Mine == 0 ? (o.ValueA, o.ValueB) : (o.ValueB, o.ValueA);
            var won = o.Winner == Mine;
            var accent = o.Winner < 0 ? HanUi.Muted : won ? HanUi.Good : HanUi.Bad;
            var cell = new Grid { Width = 300, Height = 132 };
            cell.Children.Add(HanUi.Frame(300, 132, null, 0xb0, accent, 10));
            var sp = new StackPanel { Margin = new Thickness(18, 12, 18, 10) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            if (Assets.Ui(RowIcon(o.Row)) is { } icon) top.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0) });
            var rn = HanUi.Label($"{o.Index + 1}. {HT.Name(o.Row)}", 12);
            rn.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(rn);
            sp.Children.Add(top);
            var sc = HanUi.Number($"{HTu.Fmt(mv)} – {HTu.Fmt(ov)}", 40, won ? HanUi.GoldText : HanUi.Br(HanUi.Parchment));
            sc.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(sc);
            sp.Children.Add(HanUi.Small(o.Winner < 0 ? "berabere" : won ? "senin" : "rakibin", 13, HanUi.Br(accent)));
            cell.Children.Add(sp);
            Put(cell, x, BarY + 34);
            x += 316;
        }
        var w = b.Winner;
        var res = new StackPanel { Width = 520 };
        var verdict = HanUi.Display(w < 0 ? "Berabere" : w == Mine ? "Zafer" : "Yenilgi", 40, TextAlignment.Left);
        if (w >= 0 && w != Mine) verdict.Foreground = new LinearGradientBrush(Color.FromRgb(0xff, 0xb4, 0xa4), HanUi.Bad, 90);
        res.Children.Add(verdict);
        var line = w < 0 ? "Kimse can kaybetmedi." : w == Mine ? (b.Damage > 0 && !b.Pve ? $"{op.Name} {b.Damage} can kaybetti · +{Tuning.WinGold} altın" : $"+{Tuning.WinGold} altın") : b.Damage > 0 ? $"{b.Damage} can kaybettin · kalan {HpShown(Me)}" : "Bu round can kaybı yok.";
        if (me.Points == op.Points && w >= 0) line += " · eşit çarpışma: toplam güç belirledi";
        res.Children.Add(HanUi.Body(line, 17));
        Put(res, x + 16, BarY + 44);
        if (!Session.Online)
            Put(Act("Devam", "round sonucu", "han-continue", OnBattleContinue, HanButtonKind.Primary, 122, 164), GoX, BarY + 14);
    }

    // ---------- pencere çözümü ----------

    /// <summary>Çözülen penceredeki adımların ilerleyişi: etkisi kartına inmiş kartlar son değerini gösterir.</summary>
    private readonly HashSet<int> _stepShown = new();

    /// <summary>Bir çözüm adımı: kim (SEN/RAKİP), ne (işaret/simya adı), ayrıntı (hedef, sonuç, kalan Odak), süre, oynatma.</summary>
    /// <param name="Passive">Kalıcı satır bonusu (Yennefer, Boynuz, Cüce): ayrı adım değil, satır sonucuyla aynı anda oynar (süre eklemez).</param>
    private sealed record Step(bool Mine, string What, string Detail, Color Color, int Ms, Action Play, bool Passive = false);

    /// <summary>Bir satırın adım adım çözümüne ayrılan en uzun süre (müdahaleler çoksa adımlar hızlanır; HanTimes.WindowShow ile uyumlu).</summary>
    private const int StepBudgetMs = 6000;

    /// <summary>
    /// Satırı aç: seçimler kilitlenir ve pencere <b>adım adım</b> çözülür (oyuncunun izleyebileceği hızda):
    /// seçimler gösterilir → rakibin satırı açılır → her müdahale sırayla duyurulur (kim, ne, hedef, kalan Odak), işaret havada
    /// çizilir, etkisi yalnız etkilediği kartlara iner ve o kartların sayısı değişir → satır toplamları → sonuç.
    /// </summary>
    private void OnWindowGo()
    {
        if (_screen != Screen.Battle || _busy) return;
        StopTimer();
        var b = B;
        if (b.Done && _waitWindow == null)
        {
            // savaş ben seçmeden bitti (süre doldu, sunucu varsayılanı uyguladı): sonucu göster, round sonunu bildir
            Render();
            if (_live) BattleEnd(b, ++_gen); else AfterBattle();
            return;
        }
        if (_waitWindow != null) return;
        var idx = b.Outcomes.Count;
        var o = Session.Submit(CurrentChoice());
        _signs.Clear();
        _potion = null;
        _ambush.Clear();
        if (o == null && b.Outcomes.Count > idx) o = b.Outcomes[idx];
        if (o == null) { _waitWindow = idx; Render(); return; }   // online: rakip seçiyor
        ResolveWindow(o);
    }

    /// <summary>Pencere çözüldü: adım adım gösterim, sonra sıradaki pencere ya da savaş sonu.</summary>
    private void ResolveWindow(WindowOutcome o)
    {
        _waitWindow = null;
        var b = B;
        if (!_live) { _hold = 0; Render(); if (b.Done) AfterBattle(); return; }

        _busy = true;
        var gen = ++_gen;
        var row = o.Row;
        _stepShown.Clear();
        _hold = 1;
        Render();
        var (mc, tc) = Mine == 0 ? (o.ChoiceA, o.ChoiceB) : (o.ChoiceB, o.ChoiceA);
        Caption(new Step(true, "Seçimler açıldı", $"Sen: {ChoiceText(mc)}   ·   Rakip: {ChoiceText(tc)}", HanUi.Gold, 1100, () => { }));
        Sfx.Play("han_open");
        var t = 1100;
        After(t, gen, () =>
        {
            _hold = 2;
            Sfx.Play("draw_card");
            Render();
            Caption(new Step(false, $"{HT.Name(row)} satırı açıldı", "Rakibin kartları masada", HanUi.Parchment, 1000, () => { }));
        });
        t += 1100;
        // müdahaleler sırayla; çoksa hızlanır (bir satır ~10 sn'yi geçmesin: online'da süreler buna göre)
        var all = Steps(o, b);
        var active = all.Where(s => !s.Passive).ToList();
        var passive = all.Where(s => s.Passive).ToList();
        var total = active.Sum(s => s.Ms);
        var speed = total > StepBudgetMs ? StepBudgetMs / (double)total : 1.0;
        foreach (var st in active)
        {
            var step = st with { Ms = (int)(st.Ms * speed) };
            After(t, gen, () => { Caption(step); step.Play(); });
            t += step.Ms;
        }
        After(t, gen, () =>
        {
            _hold = 0;
            Render();
            foreach (var p in passive) p.Play();   // Yennefer / Boynuz / Cüce: sonuçla aynı anda, satırın üstünde
            var won = o.Winner == Mine;
            var (mv, ov) = Mine == 0 ? (o.ValueA, o.ValueB) : (o.ValueB, o.ValueA);
            Sfx.Play(o.Winner < 0 ? "battle_draw" : won ? "battle_won" : "battle_lost");
            var c = o.Winner < 0 ? Color.FromRgb(0xe8, 0xdc, 0xc4) : won ? Color.FromRgb(0xa3, 0xe0, 0x78) : Color.FromRgb(0xff, 0x6a, 0x55);
            if (o.Winner >= 0)
            {
                var (rc, rw) = RowS(won, row);
                HanFx.Fountain(_engine, rc, rw * 0.9, won ? FxEngine.Ramp.Gold : FxEngine.Ramp.Blood);
            }
            var sv = $"{HTu.Fmt(mv)} – {HTu.Fmt(ov)}";
            ScreenBanner(o.Winner < 0 ? $"BERABERE  {sv}" : won ? $"SATIR SENİN  {sv}" : $"SATIR RAKİBİN  {sv}", c);
            foreach (var f in o.Fx.Where(f => f.Kind == "rage"))
            {
                var at = new Point(SideX + SideW / 2, f.Side == Mine ? 800 : 130);
                HanArt.FloatText(_fx, $"ÖFKE  +{f.Target} ODAK", at, Color.FromRgb(0x8f, 0xd8, 0xff), 24, 1800, 400);
                Fx.After(400, () => HanFx.Aura(_engine, new Point(at.X, at.Y + 20), 180, FxEngine.Ramp.Aard, 0.9));
            }
        });
        t += 1800;
        After(t, gen, () =>
        {
            _busy = false;
            _stepShown.Clear();
            if (b.Done) { Render(); BattleEnd(b, gen); }
            else { StartTimer(HanTimes.Window, OnWindowGo); Render(); }
        });
    }

    private static string ChoiceText(WindowChoice c)
    {
        var parts = c.Signs.Select(s => s.ToString()).ToList();
        if (c.Potion is { } p) parts.Add(HT.Name(p));
        if (c.AmbushUids.Count > 0) parts.Add(c.AmbushUids.Count > 1 ? $"Pusu ×{c.AmbushUids.Count}" : "Pusu");
        return parts.Count == 0 ? "müdahale yok" : string.Join(" + ", parts);
    }

    /// <summary>Üstte, yönlendirme şeridinin altında beliren adım duyurusu: kim · ne · ayrıntı.</summary>
    private void Caption(Step st)
    {
        var g = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = false };
        var sp = new StackPanel { Margin = new Thickness(26, 10, 26, 12) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (st.What != "Seçimler açıldı" && !st.What.EndsWith("açıldı"))
        {
            var who = HanUi.Chip(st.Mine ? "Sen" : "Rakip", st.Mine ? HanUi.Gold : HanUi.Bad, 12, true);
            who.VerticalAlignment = VerticalAlignment.Center;
            who.Margin = new Thickness(0, 0, 12, 0);
            head.Children.Add(who);
        }
        var what = HanUi.T(HanUi.Tracked(HanUi.Upper(st.What), 1), 26, new SolidColorBrush(st.Color), FontWeights.Bold);
        what.Effect = new DropShadowEffect { Color = st.Color, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 };
        head.Children.Add(what);
        sp.Children.Add(head);
        if (st.Detail.Length > 0)
        {
            var d = HanUi.Body(st.Detail, 17, null, true, TextAlignment.Center);
            d.HorizontalAlignment = HorizontalAlignment.Center;
            d.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(d);
        }
        sp.Measure(new Size(1100, double.PositiveInfinity));
        var w = Math.Max(420, Math.Min(1100, sp.DesiredSize.Width + 20));
        var frame = HanUi.Frame(w, sp.DesiredSize.Height + 4, sp, 0xec, st.Color, 12);
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        frame.VerticalAlignment = VerticalAlignment.Top;
        frame.Margin = new Thickness(0, 66, 0, 0);
        g.Children.Add(frame);
        _banner.Children.Add(g);
        Fx.Banner(g, Math.Max(400, st.Ms - 640), () => _banner.Children.Remove(g));
    }

    /// <summary>Bir kartın ekrandaki merkezi, genişliği, yüksekliği ve adı (masada değilse null).</summary>
    private (Point C, double W, double H, string Name)? CardAt(int uid)
    {
        if (!_pos.TryGetValue(uid, out var p)) return null;
        var w = _cardW.GetValueOrDefault(uid, 100);
        var name = B.Sides.SelectMany(s => s.Units).FirstOrDefault(u => u.Uid == uid)?.Def.Name ?? "";
        return (p, w, w * HanCardView.CardAspect * Lean, name);
    }

    /// <summary>Etkisi inen kartlar son değerini gösterir (ekran yeniden çizilir), sonra titrer.</summary>
    private void Land(IEnumerable<int> uids, double amp = 7)
    {
        var list = uids.ToList();
        _stepShown.UnionWith(list);
        Render();
        foreach (var u in list) Hit(u, amp);
    }

    private int FocusLeft(WindowOutcome o, int side) => B.Sides[side].Focus - o.Fx.Where(f => f.Kind == "rage" && f.Side == side).Sum(f => f.Target);

    /// <summary>Penceredeki olayları oyuncunun okuyabileceği adımlara çevirir (önce pusu, sonra senin, sonra rakibin müdahalelerin).</summary>
    private List<Step> Steps(WindowOutcome o, Battle b)
    {
        var steps = new List<Step>();
        var row = o.Row;
        const int Sign = 2300, Short = 1500;
        decimal Val(int uid) => o.CardValues.GetValueOrDefault(uid);
        decimal BaseOf(int uid) => o.BaseValues.TryGetValue(uid, out var bv) ? bv : Val(uid);
        string Delta(int uid) => $"{HTu.Fmt(BaseOf(uid))} → {HTu.Fmt(Val(uid))}";
        decimal RowBaseOf(int side) => o.RowBase[side];
        string NameOf(int uid) => b.Sides.SelectMany(x => x.Units).FirstOrDefault(u => u.Uid == uid)?.Def.Name ?? "Bir kart";
        string Odak(int side) => $"kalan Odak: {FocusLeft(o, side)}";

        foreach (var side in new[] { Mine, 1 - Mine })
        {
            var mine = side == Mine;
            var at = Caster(mine);
            void Glyph(Sign s, string sound) { HanFx.DrawGlyph(_fx, _engine, s, at, 130, 420); Sfx.Play(sound); }

            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind == "yrden"))
                steps.Add(f.Blocked
                    ? new Step(mine, "Yrden", (mine ? "Rakibin pusu inişi mühürlendi: kart pusuda kaldı" : "Pusu inişin mühürlendi: kart pusuda kaldı") + " · " + Odak(side), HanArt.SignColor(global::HanLigi.Sign.Yrden), Sign, () =>
                    {
                        Glyph(global::HanLigi.Sign.Yrden, "han_yrden");
                        Fx.After(500, () => { HanFx.Yrden(_fx, _engine, SealAt(!mine), 300, true); Sfx.Play("han_yrden_shock"); });
                        Fx.After(1300, () => Sfx.Play("han_yrden_hit"));
                    })
                    : new Step(mine, "Yrden", "Pusudan inen olmadı · " + Odak(side), HanArt.SignColor(global::HanLigi.Sign.Yrden), Short, () =>
                    {
                        Glyph(global::HanLigi.Sign.Yrden, "han_yrden");
                        Fx.After(500, () => HanFx.Yrden(_fx, _engine, SealAt(!mine), 200, false));
                    }));

        }

        foreach (var f in o.Fx.Where(f => f.Kind == "ambush"))
        {
            var mine = f.Side == Mine;
            var uid = f.Target;
            steps.Add(new Step(mine, "Pusu", $"{NameOf(uid)} pusudan {HT.Name(row)} satırına indi", Color.FromRgb(0xff, 0xe9, 0xa3), Short, () =>
            {
                if (CardAt(uid) is { } c)
                {
                    for (var i = 0; i < 12; i++) _engine.Puff(new Point(c.C.X + _engine.R(-(float)c.W / 2, (float)c.W / 2), c.C.Y + c.H * 0.4), _engine.R(-170, -10), _engine.R(60, 200), _engine.R(0.6f, 1.1f), 26, 90, 0.18f, -20, 60);
                    _engine.Light(c.C, (float)c.W * 2.2f, FxEngine.Ramp.Gold, 0.9f, 0.4f, 0);
                    Hit(uid, 5);
                }
                Sfx.Play("han_place");
            }));
        }

        foreach (var side in new[] { Mine, 1 - Mine })
        {
            var mine = side == Mine;
            var at = Caster(mine);
            var foe = 1 - side;
            void Glyph(Sign s, string sound) { HanFx.DrawGlyph(_fx, _engine, s, at, 130, 420); Sfx.Play(sound); }

            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind == "aard" && f.Blocked))
                steps.Add(new Step(mine, "Aard tutuldu", $"{(mine ? "Rakibin Yrden'i Aard'ını tuttu" : "Yrden'in rakibin Aard'ını tuttu")} · {Odak(side)}", HanArt.SignColor(global::HanLigi.Sign.Yrden), Short, () =>
                {
                    Glyph(global::HanLigi.Sign.Aard, "han_glyph");
                    Fx.After(450, () => HanFx.Yrden(_fx, _engine, SealAt(mine), 200, true));
                }));
            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind == "aard" && !f.Blocked))
            {
                var targets = b.Sides[foe].In(row).Select(u => u.Uid).ToList();
                var pct = f.Target > 0 ? f.Target : 10;
                var rb = RowBaseOf(foe);
                var after = Tuning.ApplyPct(rb, -pct / 100.0);
                steps.Add(new Step(mine, "Aard", $"{(mine ? "Rakibin" : "Senin")} satırı −%{pct}: {HTu.Fmt(rb)} → {HTu.Fmt(after)} · {Odak(side)}", HanArt.SignColor(global::HanLigi.Sign.Aard), Sign, () =>
                {
                    Glyph(global::HanLigi.Sign.Aard, "han_glyph");
                    Fx.After(450, () =>
                    {
                        Sfx.Play("han_aard");
                        var k = 0;
                        foreach (var uid in targets) if (CardAt(uid) is { } c) HanFx.Gust(_engine, at, c.C, c.W, c.H, 70 * k++);
                        Fx.After(300 + 70 * targets.Count, () =>
                        {
                            Land(targets, 6);
                            var (rc, rw) = RowS(!mine, row);
                            HanArt.FloatText(_fx, $"−%{pct}   {HTu.Fmt(rb)} → {HTu.Fmt(after)}", new Point(rc.X + rw * 0.36, rc.Y - 50), HanArt.SignColor(global::HanLigi.Sign.Aard), 30, 1800);
                        });
                    });
                }));
            }

            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind is "igni" or "igni_reflect"))
            {
                if (f.Kind == "igni" && f.Blocked)
                {
                    var shielded = b.Sides[foe].In(row).Where(u => !u.IsHero).OrderByDescending(u => Val(u.Uid)).Select(u => u.Uid).FirstOrDefault();
                    steps.Add(new Step(mine, "Igni → Quen", (mine ? "Rakip Quen ile engelledi" : "Quen'in alevi durdurdu") + $" · {Odak(side)}", HanArt.SignColor(global::HanLigi.Sign.Quen), Sign, () =>
                    {
                        Glyph(global::HanLigi.Sign.Igni, "han_igni_cast");
                        Fx.After(450, () =>
                        {
                            if (shielded != 0 && CardAt(shielded) is { } c)
                            {
                                HanFx.Quen(_fx, _engine, new Point(c.C.X, c.C.Y + c.H * 0.5), c.W * 1.7, true);
                                HanFx.IgniStrike(_engine, at, c.C, c.W, c.H, false);
                                Fx.After(700, () => { Sfx.Play("han_quen_hit"); HanArt.FloatText(_fx, "ENGELLENDİ", new Point(c.C.X, c.C.Y - c.H * 0.7), HanArt.SignColor(global::HanLigi.Sign.Quen), 30, 1400); });
                            }
                            Sfx.Play("han_igni");
                        });
                    }));
                    continue;
                }
                var uid = f.Target;
                var reflect = f.Kind == "igni_reflect";
                steps.Add(new Step(mine, reflect ? "Igni → Kara Kan" : "Igni", uid == 0 ? "Hedef yoktu, boşa gitti · " + Odak(side)
                    : (reflect ? $"Kara Kan yansıttı: {(mine ? "kendi" : "rakibin kendi")} {NameOf(uid)} yandı · " : $"{NameOf(uid)} yandı · ") + $"güç {Delta(uid)} · {Odak(side)}",
                    HanArt.SignColor(global::HanLigi.Sign.Igni), Sign, () =>
                    {
                        Glyph(global::HanLigi.Sign.Igni, "han_igni_cast");
                        Fx.After(450, () =>
                        {
                            Sfx.Play("han_igni");
                            if (CardAt(uid) is not { } c) return;
                            HanFx.IgniStrike(_engine, at, c.C, c.W, c.H, true);
                            Fx.After(330, () => { Land(new[] { uid }, 8); HanArt.FloatText(_fx, Delta(uid), new Point(c.C.X, c.C.Y - c.H * 0.7), Color.FromRgb(0xff, 0xa0, 0x40), 34, 1700); });
                        });
                    }));
            }

            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind == "quen" && !f.Blocked))
                steps.Add(new Step(mine, "Quen", "Boşa gitti: Igni gelmedi · " + Odak(side), HanArt.SignColor(global::HanLigi.Sign.Quen), Short, () =>
                {
                    Glyph(global::HanLigi.Sign.Quen, "han_quen");
                    Fx.After(450, () => HanFx.Quen(_fx, _engine, new Point(at.X, at.Y + 60), 160, false));
                }));

            var own = b.Sides[side].In(row).Select(u => u.Uid).ToList();
            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind is "thunderbolt" or "swallow"))
            {
                var thunder = f.Kind == "thunderbolt";
                var ramp = thunder ? FxEngine.Ramp.Toxic : FxEngine.Ramp.Ice;
                steps.Add(new Step(mine, thunder ? "Gök Gürültüsü" : "Kırlangıç", thunder ? $"Satırın +%25: {HTu.Fmt(RowBaseOf(side))} → {HTu.Fmt(Tuning.ApplyPct(RowBaseOf(side), 0.25))}" : "Satırdaki hava etkisi kalktı: " + string.Join(", ", own.Select(u => $"{NameOf(u)} {HTu.Fmt(Val(u))}")), thunder ? Color.FromRgb(0x9f, 0xf0, 0x9a) : Color.FromRgb(0x9a, 0xd8, 0xff), Sign - 300, () =>
                {
                    Sfx.Play("han_drink");
                    Fx.After(400, () =>
                    {
                        Sfx.Play("han_potion");
                        foreach (var uid in own) if (CardAt(uid) is { } c) HanFx.CardAura(_engine, c.C, c.W, c.H, ramp, 1.2);
                        Fx.After(500, () =>
                        {
                            Land(own, 3);
                            foreach (var uid in own) if (CardAt(uid) is { } c) HanArt.FloatText(_fx, thunder ? "+%25" : HTu.Fmt(Val(uid)), new Point(c.C.X, c.C.Y - c.H * 0.6), thunder ? Color.FromRgb(0x9f, 0xf0, 0x9a) : Color.FromRgb(0x9a, 0xd8, 0xff), 24, 1300);
                        });
                    });
                }));
            }

            foreach (var f in o.Fx.Where(f => f.Side == side && f.Kind == "blackblood" && !f.Blocked))
                steps.Add(new Step(mine, "Kara Kan", "Boşa gitti: Igni gelmedi", Color.FromRgb(0xff, 0x60, 0x70), Short, () => Sfx.Play("han_drink")));

            foreach (var kind in new[] { "dancingstar", "northernwind" })
            {
                var hits = o.Fx.Where(f => f.Side == side && f.Kind == kind).Select(f => f.Target).ToList();
                if (hits.Count == 0) continue;
                var ice = kind == "northernwind";
                var targets = hits.Where(u => u != 0).ToList();
                var names = string.Join(", ", targets.Select(NameOf));
                steps.Add(new Step(mine, ice ? "Kuzey Rüzgârı" : "Dans Eden Yıldız", targets.Count == 0 ? "Hedef yoktu" : string.Join(" · ", targets.Select(u => $"{NameOf(u)} {Delta(u)}")), ice ? Color.FromRgb(0xb0, 0xe0, 0xff) : Color.FromRgb(0xff, 0xa0, 0x40), Sign, () =>
                {
                    Sfx.Play("han_whoosh");
                    foreach (var uid in targets)
                        if (CardAt(uid) is { } c)
                            HanFx.Throw(_engine, new Point(at.X + 200, at.Y), c.C, ice ? FxEngine.Ramp.Ice : FxEngine.Ramp.Ember, 620, () =>
                            {
                                if (ice) HanFx.Frost(_engine, c.C);
                                else { HanFx.Explosion(_engine, c.C, FxEngine.Ramp.Fire, 0.55); HanFx.CardBurn(_engine, c.C, c.W, c.H, 1.2); }
                                Sfx.Play(ice ? "han_bomb_frost" : "han_bomb_fire");
                                Land(new[] { uid }, 8);
                                HanArt.FloatText(_fx, Delta(uid), new Point(c.C.X, c.C.Y - c.H * 0.7), ice ? Color.FromRgb(0xb0, 0xe0, 0xff) : Color.FromRgb(0xff, 0xa0, 0x40), 32, 1400);
                            });
                }));
            }
        }

        // kalıcı satır bonusları (Yennefer, Boynuz, Cüce sinerjisi): işaret ve simyalarla aynı toplama girer, ayrıca duyurulur
        foreach (var side in new[] { Mine, 1 - Mine })
        {
            var mine = side == Mine;
            var k = 0;
            foreach (var (_, src, pct) in o.RowMods.Where(m => m.Side == side && m.Source is "Yennefer" or "Cüce sinerjisi" || (m.Side == side && m.Source.StartsWith("Boynuz"))))
            {
                var why = src switch
                {
                    "Yennefer" => "Yennefer önceki satırı kazandı",
                    "Cüce sinerjisi" => "satırda Cüce var (Cüce sinerjisi)",
                    _ => "Boynuz bu satıra çalıyor"
                };
                var units = b.Sides[side].In(row).Select(u => u.Uid).ToList();
                var color = src == "Yennefer" ? Color.FromRgb(0xc8, 0x9a, 0xff) : src.StartsWith("Boynuz") ? Color.FromRgb(0xff, 0xd7, 0x6a) : Color.FromRgb(0xe8, 0xb0, 0x70);
                var kk = k;   // kapanış için kopya (döngü sayacı değişir)
                steps.Add(new Step(mine, src, $"{why}: {(mine ? "satırın" : "rakibin satırı")} +%{Math.Round(pct * 100)} · işaret ve simya yüzdeleriyle toplanır", color, Short, () =>
                {
                    Sfx.Play(src.StartsWith("Boynuz") ? "horn" : "han_potion");
                    foreach (var uid in units) if (CardAt(uid) is { } c) HanFx.CardAura(_engine, c.C, c.W, c.H, src == "Yennefer" ? FxEngine.Ramp.Aard : FxEngine.Ramp.Gold, 1.0);
                    var (rc, rw) = RowS(mine, row);
                    HanArt.FloatText(_fx, $"{src}  +%{Math.Round(pct * 100)}", new Point(rc.X - rw * 0.3, rc.Y - 46 - 34 * kk), color, 26, 1900);
                }, Passive: true));
                k++;
            }
        }
        return steps;
    }

    /// <summary>Ekranın ortasında kısa yazı.</summary>
    private void ScreenBanner(string text, Color c)
    {
        var t = Txt(text, 54, new SolidColorBrush(c), true, TextAlignment.Center, false);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 0, Opacity = 1 };
        var holder = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = false };
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, -60, 0, 0);
        holder.Children.Add(t);
        _banner.Children.Add(holder);
        Fx.Banner(holder, 1300, () => _banner.Children.Remove(holder));
    }

    /// <summary>Yrden mührünün indiği yer: mühürlenen tarafın panelinin altı (simyası ve pusu orada).</summary>
    private static Point SealAt(bool mine) => mine ? new Point(SideX + SideW / 2, 852) : new Point(SideX + SideW / 2, 176);

    /// <summary>İşaretin çıktığı yer: kendi tarafında masanın sol önü, rakibinde sol arkası.</summary>
    private static Point Caster(bool mine) => mine ? new Point(300, 790) : new Point(330, 240);

    /// <summary>Bir kartı titretir (vurulma tepkisi); masa ve ekran yerinde kalır.</summary>
    private void Hit(int uid, double amp = 7)
    {
        if (_unitEls.TryGetValue(uid, out var el)) Fx.Shake(el, amp, 420);
    }

    /// <summary>
    /// Savaş sonu sinematiği: ekranın ortasında amblem, skor ve can sonucu ağır ağır belirir, bir süre durur, sonra hafifçe
    /// büyüyüp yukarı süzülerek kaybolur. Bu arada hasar mermisi kaybedenin can sayısına uçar (can o anda düşer). Ardından
    /// online'da round kendiliğinden ilerler (herkes bitince hep birlikte), yerelde "Devam" beklenir.
    /// </summary>
    private void BattleEnd(Battle b, int gen)
    {
        var w = b.Winner;
        var won = w == Mine;
        var (me, op) = (b.Sides[Mine], b.Sides[1 - Mine]);
        Sfx.Play(w < 0 ? "battle_draw" : won ? "han_victory" : "han_defeat");
        var holder = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = false, Opacity = 0 };
        holder.Children.Add(new Rectangle { Fill = new RadialGradientBrush(Color.FromArgb(0xb8, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) { RadiusX = 0.55, RadiusY = 0.5 } });
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -60, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
        var el = HanArt.ResultEmblem(w < 0 ? 0 : won ? 1 : -1, w < 0 ? "Berabere" : won ? "Zafer" : "Yenilgi", 440);
        el.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(el);
        var sc = HanUi.Number($"{me.Points}  –  {op.Points}", 64, won ? HanUi.GoldText : HanUi.Br(HanUi.Parchment));
        sc.HorizontalAlignment = HorizontalAlignment.Center;
        sc.Margin = new Thickness(0, -6, 0, 0);
        sc.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0 };
        sp.Children.Add(sc);
        var sub = w < 0 ? "Kimse can kaybetmedi"
            : won ? (b.Damage > 0 && !b.Pve ? $"{op.Name}  −{b.Damage} can" : $"+{Tuning.WinGold} altın")
            : b.Damage > 0 ? $"−{b.Damage} can" : "Can kaybı yok";
        var st = HanUi.T(HanUi.Tracked(HanUi.Upper(sub), 1), 22, w >= 0 && !won && b.Damage > 0 ? HanUi.Br(Color.FromRgb(0xff, 0x8a, 0x78)) : HanUi.Br(HanUi.Parchment), FontWeights.Bold);
        st.HorizontalAlignment = HorizontalAlignment.Center;
        st.Margin = new Thickness(0, 6, 0, 0);
        st.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 0 };
        sp.Children.Add(st);
        holder.Children.Add(sp);
        _banner.Children.Add(holder);

        const int total = 3400;
        var life = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(total) };
        life.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        life.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.14), new CubicEase { EasingMode = EasingMode.EaseOut }));
        life.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.72)));
        life.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseIn }));
        life.Completed += (_, _) => _banner.Children.Remove(holder);
        holder.BeginAnimation(OpacityProperty, life);
        var scale = new ScaleTransform(0.86, 0.86);
        var lift = new TranslateTransform();
        sp.RenderTransform = new TransformGroup { Children = { scale, lift } };
        var grow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(total) };
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.18), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }));
        grow.KeyFrames.Add(new LinearDoubleKeyFrame(1.01, KeyTime.FromPercent(0.72)));
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.07, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseIn }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var rise = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(total) };
        rise.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.72)));
        rise.KeyFrames.Add(new EasingDoubleKeyFrame(-36, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseIn }));
        lift.BeginAnimation(TranslateTransform.YProperty, rise);

        // hasar: kaybedenin can sayısına uçar, can o anda düşer
        var loser = w < 0 ? -1 : b.Sides[1 - w].PlayerId;
        var hits = w >= 0 && b.Damage > 0 && loser >= 0 && !(b.Ghost && 1 - w == 1) && _hpAt.ContainsKey(loser);
        if (hits)
        {
            var from = RowS(won, Row.Ranged).Center;
            After(1500, gen, () => Projectile(from, _hpAt[loser], b.Damage, loser == Session.Me, () => { _resultShown = true; Render(); }));
        }
        else After(1500, gen, () => { _resultShown = true; Render(); });
        After(total - 200, gen, AfterBattle);
    }

    /// <summary>Kendi savaşımın sonu gösterildi: online'da round sonu bırakılır (herkes bitince birlikte ilerlenir).</summary>
    private void AfterBattle()
    {
        if (!_resultShown) { _resultShown = true; Render(); }
        if (!Session.Online) return;   // yerel: "Devam"
        _gen++;
        Session.FinishRound();
        if (Session.RoundClosed) ShowResult();
        else { _waitRound = true; Render(); }
    }

    private void Projectile(Point from, Point to, int dmg, bool me, Action? landed = null)
    {
        Sfx.Play("han_whoosh");
        HanFx.Fireball(_engine, from, to, 700, () =>
        {
            Sfx.Play(me ? "han_damage" : "han_explosion");
            landed?.Invoke();
        });
    }

    private void OnBattleContinue()
    {
        StopTimer();
        _gen++;
        _fx.Children.Clear();
        _engine.Clear();
        _banner.Children.Clear();
        _resultBattle = Session.MyBattle;
        Session.FinishRound();
        if (Session.RoundClosed) ShowResult();
        else { _waitRound = true; Render(); }   // online: öteki masalar hâlâ oynuyor
    }

    private void ShowResult()
    {
        _waitRound = false;
        _resultShown = true;
        if (Session.Over || !Me.Alive) { _screen = Screen.Final; Render(); return; }
        // online: düğme yok; bütün masalar bitince herkes aynı anda hazırlığa geçer (round afişi geçişi gösterir)
        if (Session.Online) { EnterPrep(); return; }
        _screen = Screen.Result;
        Render();
    }
}
