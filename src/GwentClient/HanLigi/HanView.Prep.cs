using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HanLigi;
using HT = HanLigi.Texts;
using HTag = HanLigi.Tag;

namespace GwentClient.HanLigi;

/// <summary>
/// Hazırlık ekranı: masada kendi satırların (rakibin yarısı sis altında), altta tek şerit (ekonomi · depo · tezgâh · Hazır), solda
/// sinerjiler ve pusu. Kart seçilince (ya da sürüklenince) gidebileceği yerler parlar; boş yere tıklamak seçimi bırakır.
/// </summary>
public sealed partial class HanView
{
    private HashSet<int> _seenCards = new();
    private string _shopSig = "";
    private int _lastGold = -1;
    private int _lastLevel = -1;
    private Dictionary<int, int> _lastStars = new();

    // alt şerit bölümleri (x)
    private const double EcoX = 12, EcoW = 262, BenchX = 286, ShopX = 1122, GoX = 1774;
    private const double BenchCardW = 96, ShopCardW = 120;

    private void EnterPrep()
    {
        _gen++;
        _busy = false;
        _fx.Children.Clear();
        _engine.Clear();
        _banner.Children.Clear();
        _screen = Screen.Prep;
        _sel = null;
        StartTimer(30, OnReady);
        Render();
        if (_live && M.Round > 1) RoundBanner($"ROUND {M.Round}", M.IsPve ? (M.Round <= 2 ? "Canavar Sürüsü" : "Vahşi Av saldırıyor") : "Hazırlık");
    }

    /// <summary>Ekranın ortasından geçen ince round afişi.</summary>
    private void RoundBanner(string title, string sub)
    {
        var g = new Grid { Width = 1920, Height = 130, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Margin = new Thickness(0, -120, 0, 0) };
        g.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0, 0, 0), 0), new(Color.FromArgb(0xd0, 0x0c, 0x07, 0x04), 0.3), new(Color.FromArgb(0xd0, 0x0c, 0x07, 0x04), 0.7), new(Color.FromArgb(0, 0, 0, 0), 1) }, 0)
        });
        foreach (var top in new[] { true, false })
            g.Children.Add(new Rectangle
            {
                Height = 1.5, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0xe0, 0xb8, 0x5a), 0.15), new(Color.FromArgb(0xff, 0xe0, 0xb8, 0x5a), 0.5), new(Color.FromArgb(0, 0xe0, 0xb8, 0x5a), 0.85) }, 0)
            });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Txt(title, 56, Theme.B("#ffe9a3"), true, TextAlignment.Center, false));
        sp.Children.Add(new TextBlock { Text = sub, FontFamily = HanUi.Font, FontSize = HanUi.S(18), FontWeight = FontWeights.SemiBold, Foreground = Theme.B("#e0b85a"), TextAlignment = TextAlignment.Center });
        g.Children.Add(sp);
        _banner.Children.Add(g);
        Fx.Banner(g, 800, () => _banner.Children.Remove(g));
        Sfx.Play("han_round");
    }

    private void OnReady()
    {
        if (_screen != Screen.Prep) return;
        StopTimer();
        EndDrag();
        Session.Ready();
        var b = Session.MyBattle!;
        _act = b.DefaultActivation(Session.MySide);
        _screen = Screen.Activation;
        _weatherShown.Clear();
        _hold = 0;
        _sel = null;
        Sfx.Play("lock_in");
        StartTimer(12, OnActivationDone);
        Render();
        if (_live) VsIntro(b);
    }

    private void Do(Func<bool> action, string sfx = "han_place")
    {
        var ok = action();
        Sfx.Play(ok ? sfx : "han_denied");
        if (_sel is { } u && Me.Cards.All(c => c.Uid != u)) _sel = null;
        if (ok && sfx == "han_place") _sel = null;
        Render();
    }

    private static Zone RowZone(Row r) => r switch { Row.Close => Zone.Close, Row.Ranged => Zone.Ranged, _ => Zone.Siege };

    /// <summary>Tek cümlelik yönlendirme: oyuncu şu an ne yapmalı.</summary>
    private (string Text, Brush? Color) PrepHint()
    {
        if (Me.PotionOptions.Length > 0) return ("Bir simya kartı seç.", null);
        if (Me.RewardOptions.Length > 0 && Me.Bench.Count() >= Tuning.BenchSize) return ("Ödül kartın bekliyor: depoda yer aç (bir kartı masaya sür ya da sat).", Theme.B("#ffb070"));
        if (Me.RewardOptions.Length > 0) return ("Ödülünü seç: üç karttan birini al.", null);
        if (_sel is { } uid && Me.Cards.FirstOrDefault(c => c.Uid == uid) is { } c)
            return (c.Fielded ? $"{c.Def.Name}: depoya al, başka yere taşı ya da sat. Boş yere tıkla: vazgeç." : $"{c.Def.Name}: parlayan yere koy ya da sat. Boş yere tıkla: vazgeç.", Theme.B("#ffe9a3"));
        var free = Me.Slots - Me.Fielded.Count();
        if (free > 0 && Me.Bench.Any()) return ($"Masada {free} boş yer var: depodaki kartları satırlarına sürükle.", Theme.B("#ffe9a3"));
        if (free > 0 && Me.Shop.Any(d => d != null && d.Price <= Me.Gold)) return ($"Masada {free} boş yer var: tezgâhtan kart al.", Theme.B("#ffe9a3"));
        if (free > 0) return ($"Masada {free} boş yer: tezgâhı yenile ya da Hazır'a bas.", null);
        if (Me.Gold >= Tuning.XpCost && Me.Level < Tuning.MaxLevel) return ("Masan dolu: seviye atla, tezgâhı yenile ya da Hazır'a bas.", null);
        return ("Hazırsan Hazır'a bas. Satır sırası savaşta açıklanır.", null);
    }

    private void DrawPrep()
    {
        var (hint, hc) = PrepHint();
        TopBar($"ROUND {M.Round} · HAZIRLIK" + (M.IsPve ? " · CANAVAR" : ""), hint, hc);
        PlayerList();
        var preview = M.Preview(HanSession.Me);
        var side = preview.Sides[0];
        var sel = _sel is { } su ? Me.Cards.FirstOrDefault(c => c.Uid == su) : null;
        var newCards = new List<FrameworkElement>();
        var grown = new List<FrameworkElement>();
        HanCardView Mine(OwnedCard c, double w, int? value)
        {
            var el = Own(c, w, value);
            if (!_seenCards.Contains(c.Uid)) newCards.Add(el);
            else if (_lastStars.TryGetValue(c.Uid, out var st) && st < c.Star) grown.Add(el);
            return el;
        }

        // ---- rakibin yarısı: sis ve kimlik ----
        foreach (var r in Enum.GetValues<Row>()) BoardFog(RowRect(false, r), 0.8);
        var oppName = Me.OppKind switch
        {
            OpponentKind.Pve => M.Round <= 2 ? "Canavar Sürüsü" : "Vahşi Av Öncüleri",
            OpponentKind.Ghost => M.Players[Me.OppId].Name + " (gölge)",
            _ => M.Players[Me.OppId].Name
        };
        var oppSub = Me.OppKind == OpponentKind.Pve
            ? (M.Round <= 2 ? "Öğretici round · can kaybı yok, ödül her durumda" : "Kaybedersen yarım hasar · kazanırsan kart ödülü")
            : $"Seviye {M.Players[Me.OppId].Level} · {SchoolName(M.Players[Me.OppId].School)} okulu · {M.Players[Me.OppId].Hp} can";
        var oppTag = new StackPanel { Width = 700 };
        oppTag.Children.Add(new TextBlock { Text = "SIRADAKİ RAKİBİN", FontFamily = HanUi.Font, FontSize = HanUi.S(13), FontWeight = FontWeights.Bold, Foreground = Theme.B("#a8905a"), TextAlignment = TextAlignment.Center });
        oppTag.Children.Add(Txt(oppName, 40, Theme.B("#f0a090"), true, TextAlignment.Center));
        oppTag.Children.Add(new TextBlock { Text = oppSub, FontFamily = HanUi.Font, FontSize = HanUi.S(15), Foreground = Theme.TextDim, TextAlignment = TextAlignment.Center });
        oppTag.Children.Add(new TextBlock
        {
            Text = Me.School == School.Cat ? "Satır sırası: " + string.Join("  →  ", Me.Order.Select(HT.Name)) : "Satırları savaşta tek tek açılacak",
            FontFamily = HanUi.Font, FontSize = HanUi.S(Me.School == School.Cat ? 17 : 14), FontWeight = Me.School == School.Cat ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Me.School == School.Cat ? Theme.B("#ffe9a3") : Theme.B("#88e8dcc4"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        });
        PutU(oppTag, new Point(RowX + RowW / 2, 255), 0, _c);
        if (Me.School == School.Cat && !Me.OrderSwapped)
        {
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (i, j) in new[] { (0, 1), (1, 2), (0, 2) })
            {
                var (a, b) = (i, j);
                var bt = Btn($"{a + 1} ↔ {b + 1}", $"han-swap:{a}-{b}", () => Do(() => M.SwapOrder(HanSession.Me, a, b), "han_click"), width: 92, height: 38);
                bt.Margin = new Thickness(6, 0, 6, 0);
                sw.Children.Add(bt);
            }
            PutU(sw, new Point(RowX + RowW / 2, 420), 0, _c);
        }

        // ---- kendi satırlarım ----
        foreach (var r in Enum.GetValues<Row>())
        {
            var zone = RowZone(r);
            if (sel != null && sel.Zone != zone && Match.ZonesFor(sel.Def).Contains(zone))
            {
                var ok = M.CanMove(HanSession.Me, sel.Uid, zone);
                var uid = sel.Uid;
                BoardZone(RowRect(true, r), "han-move:" + zone, ok ? "Buraya koy" : "Masa dolu", () => Do(() => M.Move(HanSession.Me, uid, zone)), ok, ok ? null : "Seviye atla ya da bir kartı depoya al.");
            }
            var list = Me.Cards.Where(c => c.Zone == zone).Select(c =>
            {
                var unit = side.Units.FirstOrDefault(u => u.Uid == c.Uid);
                return (c.Uid, (FrameworkElement)Mine(c, DesignW, unit == null ? null : preview.Value(side, unit)));
            }).ToList();
            PlaceRow(list, true, r);
            PutU(ScoreBadge(preview.PlainRow(0, r).ToString(), true), new Point(RowX + RowW - 36, RowY(true, r) + RowH / 2));
            RowLabel(true, r, HT.Name(r));
        }
        // özel kartlar: satırların sol sütunu
        var specials = Me.Cards.Where(c => c.Zone == Zone.Special).ToList();
        var colRect = new Rect(ColX, MyTop[0], ColW, MyTop[2] + RowH - MyTop[0]);
        if (sel != null && !sel.Def.IsUnit && sel.Zone != Zone.Special)
        {
            var uid = sel.Uid;
            var ok = M.CanMove(HanSession.Me, uid, Zone.Special);
            BoardZone(colRect, "han-move:" + Zone.Special, ok ? "Özel\nkart" : "Dolu", () => Do(() => M.Move(HanSession.Me, uid, Zone.Special)), ok);
        }
        for (var i = 0; i < specials.Count; i++)
        {
            var row = (Row)Math.Min(2, i);
            var (top, bottom, _, _) = Band(true, row);
            var foot = HanScene.ToScreen(new Point(ColX + ColW / 2 + (i >= 3 ? 30 : 0), RowY(true, row) + RowH - 5));
            PlaceCard(Mine(specials[i], DesignW, null), foot, (bottom - top) * 0.98 / (DesignH * Lean), i);
        }

        // ---- sol: sinerjiler, simya, pusu ----
        Synergies();
        PotionsAndAmbush(sel, Mine);

        // ---- alt şerit ----
        Put(Panel(1896, BarH, null, 0xec), 12, BarY);
        var shopEls = new List<FrameworkElement>();
        Economy(sel);
        Bench(sel, Mine);
        Shop(sel, shopEls);
        var rc = M.RerollCost(HanSession.Me);
        Put(Act("Yenile", rc == 0 ? "bedava" : $"{rc} altın", "han-reroll", () => Do(() => M.Reroll(HanSession.Me), "han_reroll"), HanButtonKind.Secondary, 122, 60, Me.Gold >= rc, "Tezgâha beş yeni kart gelir.", HanArt.Icon("icon_coins", 18, "")), GoX, BarY + 14);
        Put(Act("Hazır", "savaşa geç", "han-ready", OnReady, HanButtonKind.Primary, 122, 96, true, "Masanı kilitle ve savaşa geç."), GoX, BarY + 84);

        // ---- seçim pencereleri ----
        if (Me.RewardOptions.Length > 0 && Me.Bench.Count() < Tuning.BenchSize)
            DrawChoice("Ödülünü seç", "Canavar round'unun ödülü: üç karttan birini al.",
                Me.RewardOptions.Select((d, i) => ((FrameworkElement)CardEl(d, 1, 200, "han-reward:" + i, () => Do(() => M.PickReward(HanSession.Me, i), "han_buy"), named: true, aspect: 1.34), "")).ToList());
        else if (Me.PotionOptions.Length > 0)
            DrawChoice("Simya kartını seç", "Savaşta, bir satır açılmadan önce oynarsın. Tek kullanımlık.",
                Me.PotionOptions.Select((p, i) => ((FrameworkElement)Bare(HanArt.IconToken(HanArt.PotionIcon(p), 150, false, HT.Name(p)[..1]), "han-potion:" + i, () => Do(() => M.ChoosePotion(HanSession.Me, i), "han_drink")), HT.Name(p) + "\n" + HT.Describe(p))).ToList());

        // ---- canlılık ----
        if (_live)
        {
            var k = 0;
            foreach (var el in newCards) DropIn(el, 20 * k++);
            foreach (var el in grown) { Sfx.Play("han_merge"); StarBurst(el); }
            var sig = string.Join(",", Me.Shop.Select(d => d?.Id)) + "|" + M.Round;
            if (sig != _shopSig && (_shopSig.Length == 0 || !SameShopMinusOne(_shopSig, sig)))
                for (var i = 0; i < shopEls.Count; i++) DropIn(shopEls[i], 50 * i, -20);
        }
        _shopSig = string.Join(",", Me.Shop.Select(d => d?.Id)) + "|" + M.Round;
        _seenCards = Me.Cards.Select(c => c.Uid).ToHashSet();
        _lastStars = Me.Cards.ToDictionary(c => c.Uid, c => c.Star);
        _lastGold = Me.Gold;
        _lastLevel = Me.Level;
    }

    /// <summary>Satırın sol sütununda satır adı (masaya basılı gibi soluk).</summary>
    private void RowLabel(bool mine, Row r, string text, Brush? color = null)
    {
        var t = new TextBlock { Text = text.ToUpper(new System.Globalization.CultureInfo("tr-TR")), FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Foreground = color ?? Theme.B("#70e8dcc4"), IsHitTestVisible = false };
        PutU(t, new Point(ColX + ColW / 2, RowY(mine, r) + 16), 0, _rows);
    }

    /// <summary>Sol şeridin altı: simya kartları ve pusu yeri (bırakma alanı).</summary>
    private void PotionsAndAmbush(OwnedCard? sel, Func<OwnedCard, double, int?, HanCardView> mine)
    {
        const double y0 = 646, h = 222;
        Put(Panel(SideW, h, null, 0xc8), SideX, y0);
        var amb = Me.Cards.Where(c => c.Zone == Zone.Ambush).ToList();
        Put(Caption($"PUSU  {amb.Count}/{Me.AmbushCap}"), SideX + 12, y0 + 8);
        if (M.Round >= Tuning.PotionsFromRound || Me.Potions.Count > 0)
        {
            var pots = new StackPanel { Orientation = Orientation.Horizontal };
            pots.Children.Add(Caption($"SİMYA {Me.Potions.Count}/{Me.PotionCap}  "));
            foreach (var p in Me.Potions)
            {
                var ic = HanArt.Icon(HanArt.PotionIcon(p), 24, "⚗");
                ic.IsHitTestVisible = true;
                ic.ToolTip = HT.Name(p) + "\n" + HT.Describe(p);
                ic.Margin = new Thickness(0, -6, 2, 0);
                pots.Children.Add(ic);
            }
            Put(pots, SideX + 12, y0 + 30);
        }
        if (amb.Count == 0)
        {
            var t = new TextBlock { Text = "Pusu kartları burada saklanır ve savaşta istediğin satıra canlı iner.", FontFamily = HanUi.Font, FontSize = HanUi.S(13), Foreground = Theme.B("#88e8dcc4"), TextWrapping = TextWrapping.Wrap, Width = SideW - 30, TextAlignment = TextAlignment.Center };
            Put(t, SideX + 15, y0 + 90);
        }
        if (sel != null && sel.Zone != Zone.Ambush && Match.ZonesFor(sel.Def).Contains(Zone.Ambush))
        {
            var uid = sel.Uid;
            var ok = M.CanMove(HanSession.Me, uid, Zone.Ambush);
            ScreenZone(new Rect(SideX + 6, y0 + 30, SideW - 12, h - 36), "han-move:" + Zone.Ambush, ok ? "Pusuya sakla" : "Pusu dolu", () => Do(() => M.Move(HanSession.Me, uid, Zone.Ambush)), false, ok);
        }
        for (var i = 0; i < amb.Count; i++)
        {
            var el = mine(amb[i], 92, null);
            Put(el, SideX + 14 + i * Math.Min(104, (SideW - 28 - 92) / Math.Max(1, amb.Count - 1.0)), y0 + 64);
        }
    }

    /// <summary>Alt şeridin solu: altın, gelir dökümü, seviye, XP çubuğu, XP / SAT.</summary>
    private void Economy(OwnedCard? sel)
    {
        var gold = new StackPanel { Orientation = Orientation.Horizontal };
        gold.Children.Add(HanArt.Icon("icon_crowns", 36, "◉"));
        var goldText = HanUi.Number(" " + Me.Gold, 42);
        gold.Children.Add(goldText);
        Put(gold, EcoX + 14, BarY + 6);
        var interest = Math.Min(Tuning.InterestCap, Me.Gold / Tuning.InterestPer);
        var streak = Tuning.StreakGold(Me.Streak);
        var next = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12), Foreground = Theme.TextDim };
        next.Inlines.Add(new System.Windows.Documents.Run($"sonraki round  +{Tuning.Income + interest + streak}"));
        next.Inlines.Add(new System.Windows.Documents.Run($"\n{Tuning.Income} gelir · {interest} faiz") { Foreground = Theme.B("#88e8dcc4") });
        if (Me.Streak <= -2 || Me.Streak >= 2)
            next.Inlines.Add(new System.Windows.Documents.Run(Me.Streak < 0 ? $"\n{-Me.Streak} yenilgi serisi +{streak}" : $"\n{Me.Streak} galibiyet serisi +{streak}") { Foreground = Me.Streak < 0 ? Theme.B("#ffb070") : Theme.B("#a3e078"), FontWeight = FontWeights.Bold });
        next.ToolTip = "Faiz: her 10 altına +1 (en çok +2).\nSeri: art arda yenilgi 2 → +1, 3-4 → +2, 5+ → +3; art arda galibiyet 3-4 → +1, 5+ → +2.";
        Put(next, EcoX + 110, BarY + 10);
        next.Width = EcoW - 112;
        next.TextWrapping = TextWrapping.Wrap;
        var lv = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(15), FontWeight = FontWeights.Bold, Foreground = Theme.B("#f4ead2") };
        lv.Inlines.Add(new System.Windows.Documents.Run($"Seviye {Me.Level}"));
        lv.Inlines.Add(new System.Windows.Documents.Run($"   masa {Me.Fielded.Count()}/{Me.Slots}") { Foreground = Me.Fielded.Count() < Me.Slots ? Theme.B("#ffe9a3") : Theme.TextDim, FontWeight = FontWeights.SemiBold });
        Put(lv, EcoX + 16, BarY + 62);
        if (Me.Level < Tuning.MaxLevel)
        {
            var prev = Me.Level >= 2 ? Tuning.XpThresholds[Me.Level - 2] : 0;
            var frac = Math.Clamp((Me.Xp - prev) / (double)(Me.XpNeeded - prev), 0, 1);
            Put(new Border
            {
                Width = 196, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.B("#aa000000"), BorderBrush = Theme.B("#5e451c"), BorderThickness = new Thickness(1),
                Child = new Border { Width = 194 * frac, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(Color.FromRgb(0x8f, 0xd8, 0xff), Color.FromRgb(0x2a, 0x7a, 0xb8), 90) }
            }, EcoX + 16, BarY + 88);
            Put(new TextBlock { Text = $"{Me.Xp}/{Me.XpNeeded}", FontFamily = HanUi.Font, FontSize = HanUi.S(11), Foreground = Theme.TextDim }, EcoX + 218, BarY + 83);
        }
        // XP düğmesi; kart seçiliyken yerinde SAT
        if (sel != null)
        {
            var uid = sel.Uid;
            Put(Act($"Sat  +{Tuning.SellValue(sel.Def, sel.Star)}", sel.Def.Name, "han-sell", () => Do(() => M.Sell(HanSession.Me, uid), "han_sell"), HanButtonKind.Danger, EcoW - 28, 66, true, $"{sel.Def.Name} satılır, kartlar havuza döner."), EcoX + 14, BarY + 110);
        }
        else
        {
            var can = Me.Gold >= Tuning.XpCost && Me.Level < Tuning.MaxLevel;
            Put(Act("XP al", $"{Tuning.XpCost} altın → {Tuning.XpGain} XP", "han-xp", () => Do(() => M.BuyXp(HanSession.Me), "han_xp"), HanButtonKind.Secondary, EcoW - 28, 66, can, "Seviye: masaya daha çok kart sığar ve tezgâha daha iyi kartlar gelir.", HanArt.Icon("icon_exp", 22, "")), EcoX + 14, BarY + 110);
        }
        if (_live && _lastGold >= 0 && _lastGold != Me.Gold) Pop(goldText);
        if (_live && _lastLevel >= 0 && _lastLevel != Me.Level) { Pop(lv); Sfx.Play("han_levelup"); }
    }

    /// <summary>Sayı değişince kısa parlama (ölçek değil, renk): ekran oynamaz.</summary>
    private static void Pop(UIElement el) => el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)));

    /// <summary>Alt şeridin ortası: depo (8 yuva).</summary>
    private void Bench(OwnedCard? sel, Func<OwnedCard, double, int?, HanCardView> mine)
    {
        var bench = Me.Bench.ToList();
        var full = bench.Count >= Tuning.BenchSize;
        Put(Caption($"DEPO  {bench.Count}/{Tuning.BenchSize}"), BenchX + 2, BarY + 8).Foreground = full ? Theme.Bad : Theme.B("#a8905a");
        if (sel != null && sel.Fielded)
        {
            var uid = sel.Uid;
            var ok = M.CanMove(HanSession.Me, uid, Zone.Bench);
            ScreenZone(new Rect(BenchX - 4, BarY + 26, ShopX - BenchX - 8, BenchCardW * 1.2 + 8), "han-move:" + Zone.Bench, ok ? "Depoya al" : "Depo dolu", () => Do(() => M.Move(HanSession.Me, uid, Zone.Bench)), false, ok);
        }
        const double gap = (ShopX - BenchX - 16 - Tuning.BenchSize * BenchCardW) / (Tuning.BenchSize - 1);
        for (var i = 0; i < Tuning.BenchSize; i++)
        {
            var x = BenchX + i * (BenchCardW + gap);
            if (i < bench.Count) Put(mine(bench[i], BenchCardW, null), x, BarY + 30);
            else Put(new Border { Width = BenchCardW, Height = BenchCardW * 1.2, CornerRadius = new CornerRadius(6), BorderBrush = Theme.B("#30e8dcc4"), BorderThickness = new Thickness(1), Background = Theme.B("#26000000"), IsHitTestVisible = false }, x, BarY + 30);
        }
        Put(new Rectangle { Width = 1, Height = BarH - 28, Fill = Theme.B("#405e451c") }, ShopX - 8, BarY + 14);
    }

    /// <summary>Alt şeridin sağı: tezgâh (5 kart, fiyat, birleşme uyarısı). Sürüklenen kart buraya bırakılırsa satılır.</summary>
    private void Shop(OwnedCard? sel, List<FrameworkElement> shopEls)
    {
        Put(Caption("TEZGÂH"), ShopX + 4, BarY + 8);
        const double gap = 8;
        for (var i = 0; i < Me.Shop.Length; i++)
        {
            var idx = i;
            var x = ShopX + 4 + i * (ShopCardW + gap);
            if (Me.Shop[i] is { } d)
            {
                var can = M.CanBuy(HanSession.Me, i);
                var owned = Me.Cards.Count(c => c.Def == d && c.Star == 1);
                var el = CardEl(d, 1, ShopCardW, "han-shop:" + i, () => Do(() => M.Buy(HanSession.Me, idx), "han_buy"), dim: !can, price: d.Price, named: true, aspect: 1.25);
                Put(el, x, BarY + 26);
                shopEls.Add(el);
                if (owned > 0 && d.IsUnit && !d.IsHero)
                    Put(new Border
                    {
                        Background = owned == 2 ? Theme.B("#e04a8a30") : Theme.B("#d0120c07"), BorderBrush = owned == 2 ? Theme.B("#a0f080") : Theme.B("#7a5c24"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(6, 0, 6, 1), IsHitTestVisible = false,
                        Child = new TextBlock { Text = owned == 2 ? "★ BİRLEŞİR" : $"sende {owned}", FontFamily = HanUi.Font, FontSize = HanUi.S(11), FontWeight = FontWeights.Bold, Foreground = owned == 2 ? Theme.B("#d8ffc0") : Theme.TextDim }
                    }, x + ShopCardW / 2 - 34, BarY + 168);
            }
            else Put(new Border { Width = ShopCardW, Height = ShopCardW * 1.25, CornerRadius = new CornerRadius(6), BorderBrush = Theme.B("#22e8dcc4"), BorderThickness = new Thickness(1), Background = Theme.B("#26000000"), IsHitTestVisible = false }, x, BarY + 26);
        }
        // satış: sürüklenen kart tezgâhın üstüne bırakılır (yalnız fare basılıyken; tıklamayla alım engellenmesin)
        if (sel != null && _pressUid == sel.Uid)
        {
            var uid = sel.Uid;
            ScreenZone(new Rect(ShopX, BarY + 22, 5 * ShopCardW + 4 * gap + 8, ShopCardW * 1.25 + 8), "han-sell-drop", $"Sat  +{Tuning.SellValue(sel.Def, sel.Star)} altın", () => Do(() => M.Sell(HanSession.Me, uid), "han_sell"), true);
        }
    }

    /// <summary>Evolve anı: kartın üstünde altın parlama ve "★" yazısı.</summary>
    private void StarBurst(FrameworkElement el)
    {
        Point at;
        try { at = el.TransformToAncestor(_stage).Transform(new Point(el.Width / 2, el.Height / 2)); }
        catch { return; }
        Fx.After(30, () =>
        {
            HanFx.Aura(_engine, new Point(at.X, at.Y + 30), 140, FxEngine.Ramp.Gold, 0.6);
            _engine.Light(at, 220, FxEngine.Ramp.Gold, 1.2f, 0.4f, 0);
            HanArt.FloatText(_fx, "YILDIZ ATLADI!", new Point(at.X, at.Y - 70), Color.FromRgb(0xff, 0xd7, 0x6a), 26, 1300);
        });
    }

    /// <summary>Sol kenar: sinerjiler (TFT gibi): altıgen rozet, ad, sayı ve eşikler; açık olanlar renkli.</summary>
    private void Synergies()
    {
        var counts = Me.TagCounts();
        Put(Caption("SİNERJİLER"), SideX + 4, 66);
        var y = 86.0;
        var any = false;
        foreach (var t in Enum.GetValues<HTag>().OrderByDescending(t => counts.GetValueOrDefault(t)).ThenBy(t => t))
        {
            var n = counts.GetValueOrDefault(t);
            if (n == 0) continue;
            any = true;
            var th = n >= 6 ? 3 : n >= 4 ? 2 : n >= 2 ? 1 : 0;
            var c = HanCardView.TagColor(t);
            var g = new Grid { Width = SideW, Height = 40, Background = Brushes.Transparent };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(20, 6, 6, 20), Background = th > 0 ? new LinearGradientBrush(Color.FromArgb(0xd0, (byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)), Color.FromArgb(0xb0, 0x0c, 0x07, 0x04), 0) : Theme.B("#a00c0704"), BorderBrush = th > 0 ? new SolidColorBrush(c) : Theme.B("#3a2a12"), BorderThickness = new Thickness(1) });
            g.Children.Add(new Path
            {
                Data = Geometry.Parse("M14,0 L42,0 L56,24 L42,48 L14,48 L0,24 Z"), Width = 38, Height = 34, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0),
                Fill = th > 0 ? new LinearGradientBrush(Color.Add(c, Color.FromRgb(0x30, 0x30, 0x30)), Color.Multiply(c, 0.5f), 90) : Theme.B("#2a2016"), Stroke = th > 0 ? Theme.B("#ffe9a3") : Theme.B("#5e451c"), StrokeThickness = 1.2
            });
            g.Children.Add(new TextBlock { Text = n.ToString(), Width = 38, Margin = new Thickness(3, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, FontFamily = HanUi.Font, FontSize = HanUi.S(18), FontWeight = FontWeights.Bold, Foreground = th > 0 ? Theme.B("#1a1209") : Theme.TextDim });
            g.Children.Add(new TextBlock { Text = HT.Name(t), FontFamily = HanUi.Font, FontSize = HanUi.S(14), FontWeight = FontWeights.Bold, Foreground = th > 0 ? Theme.B("#f4ead2") : Theme.TextDim, Margin = new Thickness(48, 3, 0, 0) });
            var pips = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, Margin = new Thickness(48, 21, 0, 0) };
            foreach (var step in new[] { 2, 4, 6 })
            {
                pips.Inlines.Add(new System.Windows.Documents.Run(step.ToString()) { Foreground = n >= step ? new SolidColorBrush(Color.Add(c, Color.FromRgb(0x40, 0x40, 0x40))) : Theme.B("#55e8dcc4") });
                if (step < 6) pips.Inlines.Add(new System.Windows.Documents.Run("  ›  ") { Foreground = Theme.B("#44e8dcc4") });
            }
            g.Children.Add(pips);
            // açık bonusun kısa metni
            if (th > 0)
                g.Children.Add(new TextBlock { Text = HT.Thresholds(t)[th - 1], FontFamily = HanUi.Font, FontSize = HanUi.S(11), Foreground = Theme.B("#d8ccb4"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), MaxWidth = 100, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right });
            g.ToolTip = new ToolTip
            {
                Content = new TextBlock { Text = HT.Name(t) + "\n" + string.Join("\n", HT.Thresholds(t).Select((s, i) => $"{(i + 1) * 2}: {s}")), FontFamily = HanUi.Font, FontSize = HanUi.S(14), Foreground = Theme.Text },
                Background = Theme.B("#f4150f09"), BorderBrush = new SolidColorBrush(c), Padding = new Thickness(10)
            };
            Put(g, SideX, y);
            y += 44;
        }
        if (!any) Put(new TextBlock { Text = "Masaya birim koy: aynı sınıftan 2, 4, 6 kart bonus açar. Sınıflar kartın altında yazar.", FontFamily = HanUi.Font, FontSize = HanUi.S(13), Foreground = Theme.B("#88e8dcc4"), TextWrapping = TextWrapping.Wrap, Width = SideW - 8 }, SideX + 4, y);
    }

    /// <summary>Tezgâh yalnız bir satın almayla mı değişti (yenileme değil)?</summary>
    private static bool SameShopMinusOne(string before, string after)
    {
        var a = before.Split('|')[0].Split(',');
        var b = after.Split('|')[0].Split(',');
        if (a.Length != b.Length || before.Split('|')[1] != after.Split('|')[1]) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (b[i].Length != 0) return false;
            diff++;
        }
        return diff <= 1;
    }

    /// <summary>Ekranın üstünde açılan seçim penceresi (ödül, simya).</summary>
    private void DrawChoice(string title, string sub, List<(FrameworkElement El, string Desc)> options)
    {
        Put(new Border { Width = 1920, Height = 1080, Background = Theme.B("#c8000000") }, 0, 0);
        var sp = new StackPanel { Margin = new Thickness(40, 32, 40, 28) };
        var k = HanUi.Label("Seçim", 12);
        k.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(k);
        var t = HanUi.Display(title, 40);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(t);
        var rule = HanUi.Rule(360);
        rule.HorizontalAlignment = HorizontalAlignment.Center;
        rule.Margin = new Thickness(0, 6, 0, 8);
        sp.Children.Add(rule);
        var s2 = HanUi.Body(sub, 16, HanUi.Br(HanUi.Muted), false, TextAlignment.Center);
        s2.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(s2);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        foreach (var (el, desc) in options)
        {
            var col = new StackPanel { Margin = new Thickness(20, 0, 20, 0), Width = 260 };
            el.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(el);
            if (desc.Length > 0)
            {
                var lines = desc.Split('\n');
                var name = HanUi.Title(lines[0], 20, HanUi.GoldText, TextAlignment.Center);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 14, 0, 4);
                col.Children.Add(name);
                if (lines.Length > 1) col.Children.Add(HanUi.Body(string.Join(" ", lines.Skip(1)), 15, null, true, TextAlignment.Center));
            }
            row.Children.Add(col);
            DropIn(el, 0, -20);
        }
        sp.Children.Add(row);
        Put(Panel(1060, 540, sp, 0xf0, null, 16), 430, 220);
    }

    /// <summary>Tahtadaki bir satırı örten sis (perspektifli; durağan).</summary>
    private void BoardFog(Rect r, double strength)
    {
        Trap(r, new LinearGradientBrush(Color.FromArgb((byte)(0xe8 * strength), 0x0a, 0x0c, 0x12), Color.FromArgb((byte)(0xc0 * strength), 0x16, 0x18, 0x20), 0));
        if (Assets.Ui("wx_fog") is { } fog)
        {
            var g = TrapGeometry(r, 3);
            var img = new Image { Source = fog, Width = g.Bounds.Width, Height = g.Bounds.Height, Stretch = Stretch.Fill, Opacity = 0.5 * strength, IsHitTestVisible = false };
            var local = g.Clone();
            local.Transform = new TranslateTransform(-g.Bounds.X, -g.Bounds.Y);
            img.Clip = local;
            Canvas.SetLeft(img, g.Bounds.X);
            Canvas.SetTop(img, g.Bounds.Y);
            _rows.Children.Add(img);
        }
    }
}
