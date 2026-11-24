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
    private const double EcoX = 12, EcoW = 262, BenchX = 286, ShopX = 1262, GoX = 1774;
    private const double BenchCardW = 106;
    // havada süzülen tezgâh: bant (bilgi) ve üstünde duran kartlar
    private const double ShopBandY = 318, ShopBandH = 92, ShopCardW = 148, ShopGap = 44;
    /// <summary>Tezgâh açık mı (hazırlık başında kendiliğinden açılır; ✕ ya da Boşluk kapatır).</summary>
    private bool _shopOpen = true;

    private void EnterPrep()
    {
        var fromSchool = _screen == Screen.School;
        _gen++;
        _busy = false;
        _fx.Children.Clear();
        _engine.Clear();
        _banner.Children.Clear();
        _screen = Screen.Prep;
        _battleShop = false;
        EndRoll();
        ResetShopFade();
        _sel = null;
        _waitReady = false;
        _shopOpen = true;
        StartTimer(HanTimes.Prep, OnReady);
        Render();
        if (fromSchool) FlyToTable(); else DropTavern();
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
        if (_screen != Screen.Prep || _waitReady) return;
        StopTimer();
        EndDrag();
        _sel = null;
        Session.Ready();
        if (_screen != Screen.Prep) return;   // kilit olayı ekranı zaten aktivasyona geçirdi (yerelde eşzamanlı)
        if (M.Phase == Phase.Battle) EnterActivation();
        else { _waitReady = true; Sfx.Play("lock_in"); Render(); }   // online: öteki oyuncular hâlâ hazırlanıyor
    }

    /// <summary>Saha kilitlendi: savaşın hedef seçimi (Boynuz/Kalkan/Moral) ve VS girişi.</summary>
    private void EnterActivation()
    {
        _waitReady = false;
        _resultShown = false;
        _scoreShown = (-1, -1);
        StopTimer();
        EndDrag();
        // fazla kartların hazırlık masasındaki yerleri (ekran baştan çizilmeden önce): oradan depoya ya da satışa giderler
        var leaving = Me.Overflowed.Select(o => (o, _ownEls.TryGetValue(o.Uid, out var el) ? StageBounds(el) : null)).ToList();
        if (Session.MyBattle is not { } b) { _screen = Screen.Final; Render(); return; }
        _act = b.DefaultActivation(Session.MySide);
        _screen = Screen.Activation;
        _battleShop = false;
        EndRoll();
        _weatherShown.Clear();
        _hold = 0;
        _sel = null;
        Sfx.Play("lock_in");
        StartTimer(HanTimes.Activation, OnActivationDone);
        Render();
        // masa sınırı aşılmıştı: kilitte rastgele seçilen fazla kartlar masadaki yerlerinden depoya ya da satışa gider; VS girişi
        // onlardan sonra gelir (karartma gidişleri örtmesin)
        if (_live && leaving.Count > 0)
        {
            OverflowLeave(leaving);
            var gen = _gen;
            Fx.After(250 + leaving.Count * 380 + 1000, () => { if (gen == _gen && _screen == Screen.Activation && Session.MyBattle == b) VsIntro(b); });
        }
        else if (_live) VsIntro(b);
    }

    /// <summary>
    /// Fazla kartların gidişi: her kart masadaki yerinde kırmızı çerçeveyle belirir ("Fazla"), sonra sırayla depoya (alt şeride
    /// süzülüp küçülerek) ya da satışa (yükselip sönerek, "+n altın") gider; böylece hangi kartın gittiği görülür. Yeri
    /// bilinmeyen kart alt şeridin üstünden başlar. Özet bildirim de çıkar.
    /// </summary>
    private void OverflowLeave(List<(OverflowCard Card, Rect? From)> leaving)
    {
        var gen = _gen;
        for (var i = 0; i < leaving.Count; i++)
        {
            var (o, from) = leaving[i];
            var r = from ?? new Rect(900 + i * 40, BarY - 190, 100, 140);
            var w = Math.Clamp(r.Width, 70, 140);
            var g = new Grid { Width = w, Height = w * HanCardView.CardAspect, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
            g.Children.Add(new HanCardView(o.Def, o.Star, w) { IsHitTestVisible = false });
            g.Children.Add(new Border { BorderBrush = HanUi.Br(HanUi.Bad), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6), Effect = new DropShadowEffect { Color = HanUi.Bad, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 } });
            var tag = HanUi.Shadow(HanUi.Label(o.Sold ? "Fazla · satıldı" : "Fazla · depoya", 11, HanUi.Br(HanUi.Bad)));
            tag.HorizontalAlignment = HorizontalAlignment.Center;
            tag.VerticalAlignment = VerticalAlignment.Top;
            tag.Margin = new Thickness(-40, -20, -40, 0);
            g.Children.Add(tag);
            var start = new Point(r.X + r.Width / 2 - w / 2, r.Y + r.Height / 2 - g.Height / 2);
            Canvas.SetLeft(g, start.X);
            Canvas.SetTop(g, start.Y);
            var move = new TranslateTransform();
            var scale = new ScaleTransform(1, 1);
            g.RenderTransform = new TransformGroup { Children = { scale, move } };
            _fx.Children.Add(g);
            var delay = 250 + i * 380;
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            // belir, kısa dur, git, sön
            g.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(delay), Duration = TimeSpan.FromMilliseconds(1250),
                KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)), new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)), new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.8)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
            });
            Vector to;
            double endScale;
            if (o.Sold) { to = new Vector(0, -90); endScale = 0.55; }
            else { to = new Point(BenchX + 120 + (i % 6) * 120, BarY + 40) - start; endScale = 0.7; }   // depo: alt şeridin depo bölümüne
            var go = TimeSpan.FromMilliseconds(delay + 420);
            var flight = TimeSpan.FromMilliseconds(760);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, to.X, flight) { BeginTime = go, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, to.Y, flight) { BeginTime = go, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, endScale, flight) { BeginTime = go, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, endScale, flight) { BeginTime = go, EasingFunction = ease });
            var oo = o;
            var center = new Point(start.X + w / 2 + to.X, start.Y + g.Height / 2 + to.Y);
            Fx.After(delay + 980, () =>
            {
                if (gen != _gen) return;
                if (oo.Sold)
                {
                    Sfx.Play("han_sell");
                    HanArt.FloatText(_fx, $"+{oo.Gold} altın", new Point(center.X, center.Y - 30), Color.FromRgb(0xff, 0xd7, 0x6a), 24, 1300);
                }
                else Sfx.Play("han_place");
            });
            Fx.After(delay + 1300, () => _fx.Children.Remove(g));
        }
        var down = leaving.Where(x => !x.Card.Sold).Select(x => x.Card.Def.Name).ToList();
        var sold = leaving.Where(x => x.Card.Sold).ToList();
        if (down.Count > 0) Toast("Fazla kart: " + string.Join(", ", down) + " depoya indi", HanUi.Gold, 400);
        if (sold.Count > 0) Toast("Depo dolu: " + string.Join(", ", sold.Select(x => x.Card.Def.Name)) + $" satıldı (+{sold.Sum(x => x.Card.Gold)} altın)", HanUi.Bad, 900);
    }

    private void Do(Func<bool> action, string sfx = "han_place")
    {
        // oturumun "değişti" bildirimi ekranı ayrıca çizmesin: ekran burada bir kez kurulur (iki kez kurmak tıklamayı yavaşlatıyordu)
        bool ok;
        _acting = true;
        try { ok = action(); }
        finally { _acting = false; }
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
        if (Me.Overfull && _sel == null)
            return ($"Masa sınırı aşıldı ({Me.Used}/{Me.Slots}): süre bitince fazla {Me.Used - Me.Slots} kart rastgele depoya iner, depo doluysa satılır.", HanUi.Br(HanUi.Bad));
        if (_sel is { } uid && Me.Cards.FirstOrDefault(c => c.Uid == uid) is { } c)
            return (c.Fielded ? $"{c.Def.Name}: depoya al, başka yere taşı ya da sat. Boş yere tıkla: vazgeç." : $"{c.Def.Name}: parlayan yere koy ya da sat. Boş yere tıkla: vazgeç.", Theme.B("#ffe9a3"));
        var free = Me.Slots - Me.Used;
        if (free > 0 && Me.Bench.Any()) return ($"Masada {free} boş yer var: depodaki kartları satırlarına sürükle.", Theme.B("#ffe9a3"));
        if (free > 0 && Me.Shop.Any(d => d != null && d.Price <= Me.Gold)) return ($"Masada {free} boş yer var: tezgâhtan kart al.", Theme.B("#ffe9a3"));
        var done = Session.Online ? "süreyi bekle" : "Hazır'a bas";
        if (free > 0) return ($"{free} boş yer: tezgâhı yenile ya da {done}.", null);
        if (Me.Gold >= Tuning.XpCost && Me.Level < Tuning.MaxLevel) return ($"Masan dolu: seviye atla, tezgâhı yenile ya da {done}.", null);
        return (Session.Online ? "Masan hazır: süre dolunca kilitlenir. Satır sırası savaşta açıklanır." : "Hazırsan Hazır'a bas. Satır sırası savaşta açıklanır.", null);
    }

    private void DrawPrep()
    {
        var (hint, hc) = PrepHint();
        TopBar($"ROUND {M.Round} · HAZIRLIK" + (M.IsPve ? " · CANAVAR" : ""), hint, hc);
        PlayerList();
        var preview = M.Preview(Session.Me);
        var side = preview.Sides[0];
        var sel = _sel is { } su ? Me.Cards.FirstOrDefault(c => c.Uid == su) : null;
        var newCards = new List<FrameworkElement>();
        HanCardView Mine(OwnedCard c, double w, decimal? value)
        {
            var el = Own(c, w, value);
            if (!_seenCards.Contains(c.Uid)) newCards.Add(el);
            else if (_lastStars.TryGetValue(c.Uid, out var st) && st < c.Star) _grown.Add(c.Uid);   // parlaması uçuşlar varınca (Motion)
            return el;
        }

        // ---- rakibin yarısı: sis ve kimlik ----
        foreach (var r in Enum.GetValues<Row>()) BoardFog(RowRect(false, r), 0.8);
        var oppName = Me.OppKind switch
        {
            OpponentKind.Pve => M.Round <= 2 ? "Canavar Sürüsü" : "Vahşi Av Öncüleri",
            OpponentKind.Ghost => M.Players[Me.OppId].Name + (M.IsWraith(Me.OppId) ? " (hayalet)" : " (gölge)"),
            _ => M.Players[Me.OppId].Name
        };
        var oppSub = Me.OppKind == OpponentKind.Pve
            ? (M.Round == 1 ? "Tek canavar, gücü 1: masaya bir kart koy yeter · can kaybı yok, ödül her durumda"
                : M.Round == 2 ? "Her satırda bir canavar, gücü 1: farklı satırlara iki kart koy · can kaybı yok, ödül her durumda"
                : "Kaybedersen yarım hasar · kazanırsan kart ödülü")
            : Me.OppKind == OpponentKind.Ghost && M.IsWraith(Me.OppId)
                ? "Elenen oyuncunun hayaleti: elendiği andaki sahası · kaybedersen can kaybedersin, kazanırsan altın"
            : $"Seviye {M.Players[Me.OppId].Level} · {SchoolName(M.Players[Me.OppId].School)} okulu · {M.Players[Me.OppId].Hp} can";
        var oppTag = new StackPanel { Width = 700 };
        oppTag.Children.Add(new TextBlock { Text = "SIRADAKİ RAKİBİN", FontFamily = HanUi.Font, FontSize = HanUi.S(13), FontWeight = FontWeights.Bold, Foreground = Theme.B("#a8905a"), TextAlignment = TextAlignment.Center });
        oppTag.Children.Add(Txt(oppName, 40, Theme.B("#f0a090"), true, TextAlignment.Center));
        oppTag.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = oppSub, FontFamily = HanUi.Font, FontSize = HanUi.S(15), Foreground = Theme.TextDim, TextAlignment = TextAlignment.Center });
        oppTag.Children.Add(new TextBlock
        {
            Text = Me.School == School.Cat ? "Satır sırası: " + string.Join("  →  ", Me.Order.Select(HT.Name)) : "Satırları savaşta tek tek açılacak",
            FontFamily = HanUi.Font, FontSize = HanUi.S(Me.School == School.Cat ? 17 : 14), FontWeight = Me.School == School.Cat ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Me.School == School.Cat ? Theme.B("#ffe9a3") : Theme.B("#88e8dcc4"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        });
        if (_shopOpen)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(HanUi.Label("Sıradaki rakibin   ", 12));
            line.Children.Add(HanUi.Shadow(HanUi.Title(oppName, 20, HanUi.Br(Color.FromRgb(0xf0, 0xa0, 0x90)))));
            var sub = HanUi.Shadow(HanUi.Small("   " + oppSub, 13));
            sub.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(sub);
            ((TextBlock)line.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            PutU(line, new Point(RowX + RowW / 2, 70), 0, _c);
        }
        else PutU(oppTag, new Point(RowX + RowW / 2, 255), 0, _c);
        if (Me.School == School.Cat && !Me.OrderSwapped)
        {
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (i, j) in new[] { (0, 1), (1, 2), (0, 2) })
            {
                var (a, b) = (i, j);
                var bt = Btn($"{a + 1} ↔ {b + 1}", $"han-swap:{a}-{b}", () => Do(() => Session.Act(HanActKind.Swap, a, b), "han_click"), width: 92, height: 38);
                bt.Margin = new Thickness(6, 0, 6, 0);
                sw.Children.Add(bt);
            }
            PutU(sw, new Point(RowX + RowW / 2, _shopOpen ? 150 : 420), 0, _c);
        }

        // ---- kendi satırlarım ----
        foreach (var r in Enum.GetValues<Row>())
        {
            var zone = RowZone(r);
            if (sel != null && sel.Zone != zone && Match.ZonesFor(sel.Def).Contains(zone))
            {
                var ok = M.CanMove(Session.Me, sel.Uid, zone);
                var over = M.WouldOverflow(Session.Me, sel.Uid, zone);
                var uid = sel.Uid;
                BoardZone(RowRect(true, r), "han-move:" + zone, !ok ? "Konamaz" : over ? "Buraya koy\nsınır aşılır" : "Buraya koy", () => Do(() => Session.Act(HanActKind.Move, uid, (int)zone)), ok, over ? OverTip : null);
            }
            var list = Me.Cards.Where(c => c.Zone == zone).Select(c =>
            {
                var unit = side.Units.FirstOrDefault(u => u.Uid == c.Uid);
                return (c.Uid, (FrameworkElement)Mine(c, DesignW, unit == null ? null : preview.Value(side, unit)));
            }).ToList();
            PlaceRow(list, true, r);
            // satır gücü: kart girince/çıkınca yuvarlanarak değişir (sayaç katmanında: önbellekli masa yeniden çizilmez)
            var rowPow = preview.PlainRow(0, r);
            var badge = (Grid)ScoreBadge(Tuning.Fmt(rowPow), true);
            var counting = Count("row:" + r, (TextBlock)badge.Children[2], (double)rowPow, PowerFmt(rowPow), 360);
            PutU(badge, new Point(RowX + RowW - 36, RowY(true, r) + RowH / 2), 0, counting ? _counters : _units);
            RowLabel(true, r, HT.Name(r));
        }
        // özel kartlar: satırların sol sütununda, etkilediği satırın hizasında. Boynuz/Kalkan'ın satırını oyuncu seçer (savaşta
        // varsayılan hedef olur); hava kartı kendi satırında durur (Ayaz yakın, Sis menzilli, Yağmur kuşatma); Kavurma ortada.
        var specials = Me.Cards.Where(c => c.Zone == Zone.Special).ToList();
        Row SpecialRow(OwnedCard c) => c.Aim ?? Match.WeatherRow(c.Def.Special) ?? (c.Def.Special == Special.Mardroeme ? Match.MardroemeRow(Me) : Match.Aimable(c.Def) ?preview.Sides[0].Act.Horns.Concat(preview.Sides[0].Act.Shields).FirstOrDefault() : Row.Ranged);
        if (sel != null && !sel.Def.IsUnit)
        {
            var uid = sel.Uid;
            var aimable = Match.Aimable(sel.Def);
            var rows = aimable ? Enum.GetValues<Row>() : new[] { Match.WeatherRow(sel.Def.Special) ?? Row.Ranged };
            foreach (var r in rows)
            {
                if (sel.Zone == Zone.Special && (!aimable || sel.Aim == r)) continue;
                var rr = r;
                var ok = M.CanMove(Session.Me, uid, Zone.Special, aimable ? rr : null);
                var over = M.WouldOverflow(Session.Me, uid, Zone.Special);
                var label = !ok ? "Konamaz" : (aimable ? (sel.Def.Special switch { Special.Horn => "Boynuz", Special.Mardroeme => "Mardroeme", _ => "Kalkan" }) + "\nburaya" : "Özel\nkart") + (over ? "\nsınır aşılır" : "");
                BoardZone(new Rect(ColX, RowY(true, rr), ColW, RowH), "han-move:" + Zone.Special + (aimable ? ":" + (int)rr : ""), label,
                    () => Do(() => Session.Act(HanActKind.Move, uid, (int)Zone.Special, aimable ? (int)rr : -1)), ok,
                    over ? OverTip : null);
            }
        }
        foreach (var grp in specials.GroupBy(SpecialRow))
        {
            var k = 0;
            foreach (var c in grp)
            {
                var (top, bottom, _, _) = Band(true, grp.Key);
                var foot = HanScene.ToScreen(new Point(ColX + ColW / 2 + k * 26, RowY(true, grp.Key) + RowH - 5));
                PlaceCard(Mine(c, DesignW, null), foot, (bottom - top) * 0.98 / (DesignH * Lean), k++);
            }
        }

        // ---- sol: sinerjiler, simya, pusu ----
        Synergies();
        PotionsAndAmbush(sel, Mine);

        // ---- alt şerit ----
        // fazla kart uyarısı: şeridin üst kenarında, ortada (sol alttaki sayı gözden kaçıyordu)
        Border? overfull = null;
        if (Me.Overfull)
        {
            var extra = Me.Used - Me.Slots;
            var warn = new StackPanel { Orientation = Orientation.Horizontal };
            warn.Children.Add(HanUi.Shadow(HanUi.Title("Fazla kart var", 22, HanUi.Br(Color.FromRgb(0xff, 0x8a, 0x5c)))));
            var sub = HanUi.Shadow(HanUi.T($"   {Me.Used}/{Me.Slots} · süre bitince {extra} kart rastgele depoya iner{(Me.Bench.Count() + extra > Tuning.BenchSize ? ", sığmayan satılır" : "")}", 15, HanUi.Br(Color.FromRgb(0xff, 0xc0, 0x9a))));
            sub.VerticalAlignment = VerticalAlignment.Center;
            warn.Children.Add(sub);
            // hafif saydam koyu şerit: masadaki kartların üstünde de okunur
            overfull = new Border
            {
                Child = warn, Padding = new Thickness(18, 2, 18, 3), CornerRadius = new CornerRadius(14), IsHitTestVisible = false, Tag = "han-overfull",
                Background = Theme.B("#f0140806"), BorderBrush = HanUi.Br(Color.FromRgb(0xff, 0x8a, 0x5c), 0x90), BorderThickness = new Thickness(1)
            };
        }
        Put(Panel(1896, BarH, null, 0xec), 12, BarY);
        // şeridin üst kenarına oturur (masanın son satırındaki kartların ayağına binmesin)
        if (overfull != null)
        {
            overfull.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Put(overfull, (BenchX + ShopX - 8) / 2 - overfull.DesiredSize.Width / 2, BarY - overfull.DesiredSize.Height / 2 - 2);   // deponun üstünde (sağdaki düğmelere değmez)
        }
        var shopEls = new List<FrameworkElement>();
        Economy(sel);
        Bench(sel, Mine);
        var rc = M.RerollCost(Session.Me);
        Put(Act(_shopOpen ? "Tezgâhı kapat" : "Tezgâh", _shopOpen ? "Boşluk" : $"{Me.Shop.Count(d => d != null)} kart bekliyor · Boşluk", "han-shop-toggle", ToggleShop, _shopOpen ? HanButtonKind.Secondary : HanButtonKind.Primary, 236, 76, true, "Tezgâhı aç / kapat (Boşluk).", HanArt.Icon("icon_lager", 30, "")), ShopX, BarY + 14);
        Put(Act("Yenile", rc == 0 ? "bedava · R" : $"{rc} altın · R", "han-reroll", () => { _shopOpen = true; Do(() => Session.Act(HanActKind.Reroll), "han_reroll"); }, HanButtonKind.Secondary, 162, 76, Me.Gold >= rc, "Tezgâha beş yeni kart gelir (R).", HanGlyph.Reroll(22)), ShopX, BarY + 100);
        Put(LockButton(66, 76), ShopX + 170, BarY + 100);
        // online'da hazırlık herkes için ortak süreyle biter: "Hazır" düğmesi yok, yerinde süre bilgisi
        if (!Session.Online) Put(Act("Hazır", "savaşa geç", "han-ready", OnReady, HanButtonKind.Primary, 392, 162, true, "Masanı kilitle ve savaşa geç."), 1510, BarY + 14);
        else Put(OnlinePrepNote(), 1510, BarY + 14);
        // seçim penceresi (ödül, simya) ya da yardım açıkken tezgâh gizlenir: üst üste binmesin
        var modal = Me.PotionOptions.Length > 0 || (Me.RewardOptions.Length > 0 && Me.Bench.Count() < Tuning.BenchSize) || _help;
        if (_shopOpen && !modal) ShopOverlay(sel, shopEls);

        // ---- seçim pencereleri ----
        if (Me.RewardOptions.Length > 0 && Me.Bench.Count() < Tuning.BenchSize)
            DrawChoice("Ödülünü seç", "Canavar round'unun ödülü: üç karttan birini al.",
                Me.RewardOptions.Select((d, i) =>
                {
                    // ad ve yetenek kartın altında tam yazar (kartın üstüne yazılınca "+%30" gibi sonlar kırpılıyordu)
                    HanCardView? el = null;
                    el = CardEl(d, 1, 184, "han-reward:" + i, () => { _flyFrom = el is { } e ? StageBounds(e) : null; _flyDef = d; Do(() => Session.Act(HanActKind.Reward, i), "han_buy"); }, aspect: 1.34);
                    var gist = HanCardView.Gist(d);
                    FrameworkElement shown = el;
                    if (CopyChip(d, 184) is { } chip) shown = new Grid { Children = { el, chip } };   // marketteki gibi birleşme / "sende" uyarısı
                    return (shown, d.Name + "\n" + (gist.Length > 0 ? gist : HanCardView.Keyword(d)));
                }).ToList());
        else if (Me.PotionOptions.Length > 0)
            DrawChoice("Simya kartını seç", "Savaşta, bir satır açılmadan önce oynarsın. Tek kullanımlık.",
                Me.PotionOptions.Select((p, i) => ((FrameworkElement)Bare(HanArt.IconToken(HanArt.PotionIcon(p), 150, false, HT.Name(p)[..1]), "han-potion:" + i, () => Do(() => Session.Act(HanActKind.Potion, i), "han_drink")), HT.Name(p) + "\n" + HT.Describe(p, Me.School))).ToList());

        // ---- canlılık ----
        if (_live)
        {
            var k = 0;
            if (!_arrival) foreach (var el in newCards) DropIn(el, 20 * k++);   // tezgâhtan alınan kart düşmez, uçarak gelir
            var sig = string.Join(",", Me.Shop.Select(d => d?.Id)) + "|" + M.Round;
            if (sig != _shopSig && (_shopSig.Length == 0 || !SameShopMinusOne(_shopSig, sig)))
                for (var i = 0; i < shopEls.Count; i++) DropIn(shopEls[i], 50 * i, -20);
        }
        _shopSig = string.Join(",", Me.Shop.Select(d => d?.Id)) + "|" + M.Round;
        _seenCards = Me.Cards.Select(c => c.Uid).ToHashSet();
        _lastStars = Me.Cards.ToDictionary(c => c.Uid, c => c.Star);
        _lastDefs = Me.Cards.ToDictionary(c => c.Uid, c => c.Def);
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
    private void PotionsAndAmbush(OwnedCard? sel, Func<OwnedCard, double, decimal?, HanCardView> mine)
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
                ic.ToolTip = HT.Name(p) + "\n" + HT.Describe(p, Me.School);
                ic.Margin = new Thickness(0, -6, 2, 0);
                pots.Children.Add(ic);
            }
            Put(pots, SideX + 12, y0 + 30);
        }
        if (amb.Count == 0)
        {
            var t = new TextBlock { Text = "Pusu kartları burada saklanır ve savaşta istediğin satıra canlı iner. Masada yer kaplamazlar.", FontFamily = HanUi.Font, FontSize = HanUi.S(13), Foreground = Theme.B("#88e8dcc4"), TextWrapping = TextWrapping.Wrap, Width = SideW - 30, TextAlignment = TextAlignment.Center };
            Put(t, SideX + 15, y0 + 90);
        }
        if (sel != null && sel.Zone != Zone.Ambush && Match.ZonesFor(sel.Def).Contains(Zone.Ambush))
        {
            var uid = sel.Uid;
            var ok = M.CanMove(Session.Me, uid, Zone.Ambush);
            // sürüklerken bırakma alanı pusudaki kartların üstüne çıkar (kartlar alanı doldurunca bırakmayı yutmasın)
            ScreenZone(new Rect(SideX + 6, y0 + 30, SideW - 12, h - 36), "han-move:" + Zone.Ambush, ok ? "Pusuya sakla" : "Pusu dolu", () => Do(() => Session.Act(HanActKind.Move, uid, (int)Zone.Ambush)), false, ok);
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
        gold.Children.Add(HanArt.Icon("icon_crowns", 34, "◉"));
        var goldText = HanUi.Number(" " + Me.Gold, Me.Gold >= 100 ? 32 : 40);
        gold.Children.Add(goldText);
        gold.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // altın yuvarlanarak değişir (sayarken sayaç katmanında); genişlik son değere göre ölçüldü, yanındaki gelir yazısı kaymaz
        var counting = Count("gold", goldText, Me.Gold, v => " " + Whole(v), 380);
        Canvas.SetLeft(gold, EcoX + 12);
        Canvas.SetTop(gold, BarY + 6);
        (counting ? _counters : _c).Children.Add(gold);
        // sonraki round geliri: tek satır toplam + tek satır döküm (taşmaz, sığmazsa kısalır); ayrıntı ipucunda
        var inc = NextIncome();
        var nx = EcoX + 12 + gold.DesiredSize.Width + 8;
        var next = new StackPanel { Width = Math.Max(60, EcoX + EcoW - nx - 6), Background = Brushes.Transparent, ClipToBounds = true };
        var top = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(13), TextTrimming = TextTrimming.CharacterEllipsis };
        top.Inlines.Add(new System.Windows.Documents.Run($"+{inc.Sum(x => x.Value)}") { FontWeight = FontWeights.Bold, Foreground = HanUi.GoldText });
        top.Inlines.Add(new System.Windows.Documents.Run(" sonraki round") { Foreground = Theme.TextDim });
        next.Children.Add(top);
        var parts = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(11.5), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.B("#99e8dcc4") };
        var first = true;
        foreach (var (name, v, c) in inc.Where(x => x.Value > 0 || x.Name is "gelir" or "faiz"))
        {
            if (!first) parts.Inlines.Add(new System.Windows.Documents.Run(" · "));
            var run = new System.Windows.Documents.Run($"{v} {(name.EndsWith("serisi") ? "seri" : name)}");
            if (c is { } cc) { run.Foreground = new SolidColorBrush(cc); run.FontWeight = FontWeights.Bold; }
            parts.Inlines.Add(run);
            first = false;
        }
        next.Children.Add(parts);
        next.ToolTip = IncomeTip(inc);
        Put(next, nx, BarY + 12);
        var lv = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(15), FontWeight = FontWeights.Bold, Foreground = Theme.B("#f4ead2") };
        lv.Inlines.Add(new System.Windows.Documents.Run($"Seviye {Me.Level}"));
        lv.Inlines.Add(new System.Windows.Documents.Run($"   masa {Me.Used}/{Me.Slots}" + (Me.Overfull ? " !" : ""))
        {
            Foreground = Me.Overfull ? HanUi.Br(HanUi.Bad) : Me.Used < Me.Slots ? Theme.B("#ffe9a3") : Theme.TextDim,
            FontWeight = Me.Overfull ? FontWeights.Bold : FontWeights.SemiBold
        });
        lv.ToolTip = SlotTip + "\nMasa sınırına satırlar ve özel kartlar sayılır; pusudakiler sayılmaz (pusunun kendi sınırı var).\n" + OverTip;
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
            Put(Act($"Sat  +{Tuning.SellValue(sel.Def, sel.Star)}", sel.Def.Name, "han-sell", () => Do(() => Session.Act(HanActKind.Sell, uid), "han_sell"), HanButtonKind.Danger, EcoW - 28, 66, true, $"{sel.Def.Name} satılır, kartlar havuza döner."), EcoX + 14, BarY + 110);
        }
        else
        {
            var can = Me.Gold >= Tuning.XpCost && Me.Level < Tuning.MaxLevel;
            Put(Act("XP al", $"{Tuning.XpCost} altın → {Tuning.XpGain} XP · F", "han-xp", () => Do(() => Session.Act(HanActKind.Xp), "han_xp"), HanButtonKind.Secondary, EcoW - 28, 66, can, "Seviye: masaya daha çok kart sığar ve tezgâha daha iyi kartlar gelir.", HanArt.Icon("icon_exp", 22, "")), EcoX + 14, BarY + 110);
        }
        if (_live && _lastGold >= 0 && _lastGold != Me.Gold) Pop(goldText);
        if (_live && _lastLevel >= 0 && _lastLevel != Me.Level) { Pop(lv); Sfx.Play("han_levelup"); }
    }

    internal static readonly string SlotTip = $"Masa sınırı = seviye + 2, en çok {Tuning.Slots(Tuning.MaxLevel)} ({Tuning.Slots(Tuning.MaxLevel) - 2}. seviyede dolar). Daha yüksek seviyeler yalnız tezgâhtaki kart ihtimallerini artırır.";

    private const string OverTip = "Hazırlıkta sınırdan fazla kart koyabilirsin; süre bitince fazlası rastgele seçilip depoya iner, depo doluysa satılır.";

    /// <summary>Online hazırlık: Hazır düğmesinin yerinde, hazırlığın ortak süreyle bittiğini anlatan not.</summary>
    private FrameworkElement OnlinePrepNote()
    {
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 22, 0) };
        sp.Children.Add(HanUi.Label("Savaş", 12));
        sp.Children.Add(HanUi.Title("Süre dolunca", 26, HanUi.GoldText));
        sp.Children.Add(HanUi.Body("Hazırlık herkes için ortak süreyle biter; masan o anki haliyle kilitlenir.", 14, HanUi.Br(HanUi.Muted)));
        var g = Panel(392, 162, sp, 0xb0);
        g.Tag = "han-prep-timer";
        return g;
    }

    /// <summary>Sonraki round başında gelecek altın, kalem kalem (Match.BeginRound ile aynı sıra ve kurallar).</summary>
    private List<(string Name, int Value, Color? Color)> NextIncome()
    {
        var interest = Math.Min(Tuning.InterestCap, Me.Gold / Tuning.InterestPer);
        var streak = Tuning.StreakGold(Me.Streak);
        var treasure = Math.Min(2, Me.Fielded.Count(c => c.Def.Ability == Ability.Treasure));
        var noble = Me.Th(HTag.Noble) >= 3 ? 3 : Me.Th(HTag.Noble) >= 2 ? 1 : 0;
        return new()
        {
            ("gelir", Tuning.Income, null),
            ("faiz", interest, null),
            (Me.Streak < 0 ? "yenilgi serisi" : "galibiyet serisi", streak, Me.Streak < 0 ? Color.FromRgb(0xff, 0xb0, 0x70) : Color.FromRgb(0xa3, 0xe0, 0x78)),
            ("hazine", treasure, Color.FromRgb(0xff, 0xd7, 0x6a)),
            ("soylu", noble, HanCardView.TagColor(HTag.Noble)),
        };
    }

    /// <summary>Gelir ipucu: kalemler sağa hizalı tablo, altında kuralların kısa özeti; şu anki seri vurgulu.</summary>
    private ToolTip IncomeTip(List<(string Name, int Value, Color? Color)> inc)
    {
        var sp = new StackPanel { Width = 270 };
        sp.Children.Add(HanUi.Label("Sonraki round geliri", 11));
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Row(string a, string b, Brush? fg = null, bool bold = false)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var l = HanUi.T(a, 14, fg ?? HanUi.Br(HanUi.Parchment), bold ? FontWeights.Bold : FontWeights.Normal);
            var r = HanUi.T(b, 14, fg ?? HanUi.Br(HanUi.Parchment), FontWeights.Bold);
            l.Margin = r.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(l, grid.RowDefinitions.Count - 1);
            Grid.SetRow(r, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(r, 1);
            grid.Children.Add(l);
            grid.Children.Add(r);
        }
        foreach (var (name, v, c) in inc.Where(x => x.Value > 0 || x.Name is "gelir" or "faiz"))
            Row(char.ToUpper(name[0], new System.Globalization.CultureInfo("tr-TR")) + name[1..], $"+{v}", c is { } cc ? new SolidColorBrush(cc) : null);
        sp.Children.Add(grid);
        var rule = HanUi.Rule(270);
        rule.Margin = new Thickness(0, 4, 0, 4);
        sp.Children.Add(rule);
        var total = HanUi.T($"Toplam  +{inc.Sum(x => x.Value)}", 15, HanUi.GoldText, FontWeights.Bold);
        total.HorizontalAlignment = HorizontalAlignment.Right;
        sp.Children.Add(total);
        TextBlock Note(string head, string body)
        {
            var t = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12.5), Foreground = HanUi.Br(HanUi.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run(head + "  ") { FontWeight = FontWeights.Bold, Foreground = HanUi.Br(HanUi.Parchment) });
            t.Inlines.Add(new System.Windows.Documents.Run(body));
            return t;
        }
        sp.Children.Add(Note("Faiz", "her 10 altın +1, en çok +2"));
        sp.Children.Add(Note("Yenilgi serisi", "2: +1  ·  3: +2  ·  4: +3  ·  5+: +4"));
        sp.Children.Add(Note("Galibiyet serisi", "3-4: +1  ·  5+: +2"));
        if (Me.Streak <= -2 || Me.Streak >= 2) sp.Children.Add(Note("Şu an", Me.Streak < 0 ? $"{-Me.Streak} yenilgi üst üste" : $"{Me.Streak} galibiyet üst üste"));
        return new ToolTip { Content = sp, Background = Theme.B("#f4150f09"), BorderBrush = HanUi.Br(HanUi.Gold, 0xa0), Padding = new Thickness(14, 10, 14, 12) };
    }

    /// <summary>Sayı değişince kısa parlama (ölçek değil, renk): ekran oynamaz.</summary>
    private static void Pop(UIElement el) => el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)));

    /// <summary>Alt şeridin ortası: depo (8 yuva).</summary>
    private void Bench(OwnedCard? sel, Func<OwnedCard, double, decimal?, HanCardView> mine)
    {
        var bench = Me.Bench.ToList();
        var full = bench.Count >= Tuning.BenchSize;
        Put(Caption($"DEPO  {bench.Count}/{Tuning.BenchSize}"), BenchX + 2, BarY + 8).Foreground = full ? Theme.Bad : Theme.B("#a8905a");
        if (sel != null && sel.Fielded)
        {
            var uid = sel.Uid;
            var ok = M.CanMove(Session.Me, uid, Zone.Bench);
            ScreenZone(new Rect(BenchX - 4, BarY + 26, ShopX - BenchX - 8, BenchCardW * HanCardView.CardAspect + 8), "han-move:" + Zone.Bench, ok ? "Depoya al" : "Depo dolu", () => Do(() => Session.Act(HanActKind.Move, uid, (int)Zone.Bench)), false, ok);
        }
        const double gap = (ShopX - BenchX - 16 - Tuning.BenchSize * BenchCardW) / (Tuning.BenchSize - 1);
        for (var i = 0; i < Tuning.BenchSize; i++)
        {
            var x = BenchX + i * (BenchCardW + gap);
            if (i < bench.Count) Put(mine(bench[i], BenchCardW, null), x, BarY + 30);
            else Put(new Border { Width = BenchCardW, Height = BenchCardW * HanCardView.CardAspect, CornerRadius = new CornerRadius(6), BorderBrush = Theme.B("#30e8dcc4"), BorderThickness = new Thickness(1), Background = Theme.B("#26000000"), IsHitTestVisible = false }, x, BarY + 30);
        }
        Put(new Rectangle { Width = 1, Height = BarH - 28, Fill = Theme.B("#405e451c") }, ShopX - 8, BarY + 14);
    }

    /// <summary>Tezgâh kilidi düğmesi (L): kilitliyken sonraki round tezgâh yenilenmez, kalan kartlar aynen gelir.</summary>
    private HanButton LockButton(double w, double h, string tag = "han-lock")
        => Act("", null, tag, ToggleLock, Me.ShopLocked ? HanButtonKind.Primary : HanButtonKind.Ghost, w, h, true,
            Me.ShopLocked ? "Tezgâh kilitli: sonraki round bu kartlar (alınanlar hariç) aynen gelir. Açmak için tıkla (L)." : "Tezgâhı kilitle (L): sonraki round yenilenmez, kalan kartlar aynen gelir.",
            HanGlyph.Lock(h >= 60 ? 28 : 22, Me.ShopLocked));

    private void ToggleLock()
    {
        // hazırlıkta ve savaşta (tezgâha bakarken); savaş adımı çözülürken yeniden çizim olmaz
        if (_screen is not (Screen.Prep or Screen.Activation or Screen.Battle) || (_screen != Screen.Prep && (_busy || !BattleShopAllowed))) return;
        Do(() => Session.Act(HanActKind.LockShop), "han_click");
    }

    private void ToggleShop()
    {
        _shopOpen = !_shopOpen;
        Sfx.Play(_shopOpen ? "han_open" : "han_click");
        Render();
    }

    /// <summary>
    /// Havada süzülen tezgâh (Auto Chess düzeni): masanın ortasından geçen yarı saydam bir bant, bandın üstünde kaidelerde
    /// duran beş kart (hafifçe süzülür), bantta her kartın adı, yeteneği, sınıfları ve fiyatı. Solda yenile, sağda kapat.
    /// Bir karta ya da altındaki yazıya tıklamak satın alır. Kart sürüklenirken bant "Sat" alanı olur.
    /// </summary>
    private void ShopOverlay(OwnedCard? sel, List<FrameworkElement> shopEls)
    {
        var n = Me.Shop.Length;
        var inner = n * ShopCardW + (n - 1) * ShopGap;
        var bw = inner + 200;
        var bx = 960 - bw / 2 + 40;   // masanın ortası (tahta biraz sağda)
        // bant: koyu, yarı saydam, üstü ve altı ince altın çizgili
        var band = new Grid { Width = bw, Height = ShopBandH };
        band.Children.Add(new Path
        {
            Data = HanUi.ChamferGeometry(bw, ShopBandH, 14), IsHitTestVisible = true,
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0xa8, 0x16, 0x10, 0x0a), 0), new(Color.FromArgb(0x88, 0x0a, 0x07, 0x05), 1) }, 90),
            Stroke = Me.ShopLocked ? HanUi.GoldText : new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x00, 0xe8, 0xc3, 0x6a), 0), new(Color.FromArgb(0xd0, 0xe8, 0xc3, 0x6a), 0.5), new(Color.FromArgb(0x00, 0xe8, 0xc3, 0x6a), 1) }, 0), StrokeThickness = Me.ShopLocked ? 2 : 1.2
        });
        Canvas.SetLeft(band, bx);
        Canvas.SetTop(band, ShopBandY);
        _shop.Children.Add(band);
        // bant başlığı
        var head = HanUi.Shadow(HanUi.Label("Tezgâh", 12));
        Canvas.SetLeft(head, bx + 24);
        Canvas.SetTop(head, ShopBandY - 22);
        _shop.Children.Add(head);
        // sol: yenile, sağ: kapat
        var rc = M.RerollCost(Session.Me);
        // savaşta tezgâha yalnız bakılır ve kilitlenir: yenileme ve alım yok
        var browse = _screen != Screen.Prep;
        if (!browse)
        {
            var rr = Act("", rc == 0 ? "bedava" : $"{rc} altın", "han-reroll-band", () => Do(() => Session.Act(HanActKind.Reroll), "han_reroll"), HanButtonKind.Ghost, 64, 64, Me.Gold >= rc, "Yenile (R): tezgâha beş yeni kart gelir.", HanGlyph.Reroll(26));
            Canvas.SetLeft(rr, bx + 18);
            Canvas.SetTop(rr, ShopBandY + (ShopBandH - 64) / 2);
            _shop.Children.Add(rr);
        }
        var lk = LockButton(40, 40, "han-lock-band");
        Canvas.SetLeft(lk, bx + bw - 58);
        Canvas.SetTop(lk, ShopBandY + (ShopBandH - 40) / 2 - 22);
        _shop.Children.Add(lk);
        if (Me.ShopLocked)
        {
            // başlığın üstünde, ilk kartın solunda (sağda kartların ayağına biniyordu); ayrıntısı kilit düğmesinin ipucunda
            var note = HanUi.Shadow(HanUi.Label("Kilitli", 11, HanUi.GoldText));
            Canvas.SetLeft(note, bx + 24);
            Canvas.SetTop(note, ShopBandY - 40);
            _shop.Children.Add(note);
        }
        var close = Act("✕", null, "han-shop-close", browse ? ToggleBattleShop : ToggleShop, HanButtonKind.Ghost, 40, 40, !browse || !_busy, "Kapat (Boşluk)");
        Canvas.SetLeft(close, bx + bw - 58);
        Canvas.SetTop(close, ShopBandY + (ShopBandH - 40) / 2 + 22);
        _shop.Children.Add(close);

        var k = ShopCardW / DesignW;
        for (var i = 0; i < n; i++)
        {
            var idx = i;
            var cx = bx + 100 + i * (ShopCardW + ShopGap) + ShopCardW / 2;
            // kaide
            var ped = new Ellipse
            {
                Width = ShopCardW * 0.95, Height = 22, IsHitTestVisible = false,
                Fill = new RadialGradientBrush(Color.FromArgb(0xf0, 0x14, 0x10, 0x0c), Color.FromArgb(0xf0, 0x06, 0x05, 0x04)),
                Stroke = new LinearGradientBrush(Color.FromArgb(0xc0, 0xe8, 0xc3, 0x6a), Color.FromArgb(0x30, 0x8a, 0x62, 0x22), 90), StrokeThickness = 1.2
            };
            Canvas.SetLeft(ped, cx - ped.Width / 2);
            Canvas.SetTop(ped, ShopBandY - 13);
            _shop.Children.Add(ped);
            if (Me.Shop[i] is not { } d) continue;
            // alınamayan kart (altın yetmiyor ya da depo dolu) soluk ve tıklanamaz; durum değişince (satış, gelir) yeniden çizimde açılır
            var can = !browse && M.CanBuy(Session.Me, i);
            HanCardView? card = null;
            void Buy()
            {
                // kart tezgâhtaki yerinden depoya uçar (Motion)
                _flyFrom = card != null ? StageBounds(card) : null;
                _flyDef = d;
                Do(() => Session.Act(HanActKind.Buy, idx), "han_buy");
            }
            // savaşta (yalnız bakış) kartlar soluk değil, tıklanmaz
            card = CardEl(d, 1, DesignW, browse ? "han-shop-view:" + i : can ? "han-shop:" + i : "han-shop-off:" + i, can ? Buy : null, dim: !can && !browse, plain: true);
            card.CacheMode = new BitmapCache(Math.Max(1, k)) { SnapsToDevicePixels = true };
            var holder = new Grid { Width = DesignW, Height = DesignH, RenderTransformOrigin = new Point(0.5, 1) };
            var bob = new TranslateTransform();
            holder.RenderTransform = new TransformGroup { Children = { new ScaleTransform(k, k), bob } };
            holder.Children.Add(card);
            Canvas.SetLeft(holder, cx - DesignW / 2);
            Canvas.SetTop(holder, ShopBandY - 6 - DesignH);
            _shop.Children.Add(holder);
            shopEls.Add(card);
            // üstüne gelince kart kaidesinden hızla yükselir (sürekli süzülme yok: yavaş hareket piksele oturup takılıyordu)
            if (_live)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                holder.MouseEnter += (_, _) => bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
                holder.MouseLeave += (_, _) => bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
                // basınca hemen hafifçe çöker: alım bırakınca olur ama kart basıldığı an yanıt verir
                if (can) holder.PreviewMouseLeftButtonDown += (_, _) => bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-5, TimeSpan.FromMilliseconds(45)) { EasingFunction = ease });
            }
            // bantta bilgi: ad ★ · yetenek · sınıflar · fiyat (tıklayınca da alınır)
            var info = new StackPanel { Width = ShopCardW + ShopGap - 8, Background = Brushes.Transparent, Cursor = can ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, Opacity = can || browse ? 1 : 0.45 };
            var name = HanUi.Fit(HanUi.Title(d.Name, 16.5, d.IsHero ? HanUi.GoldText : HanUi.Br(HanUi.Parchment), TextAlignment.Center), info.Width - 4, 12.5);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            info.Children.Add(name);
            var kw = HanCardView.Keyword(d);
            var cls = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12.5), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            if (kw.Length > 0) cls.Inlines.Add(new System.Windows.Documents.Run(HanUi.Upper(kw)) { Foreground = HanUi.Br(Color.FromRgb(0xff, 0xe2, 0xa0)), FontWeight = FontWeights.Bold });
            if (d.IsUnit)
                for (var t = 0; t < d.Tags.Length; t++)
                {
                    cls.Inlines.Add(new System.Windows.Documents.Run(kw.Length > 0 || t > 0 ? "  ·  " : "") { Foreground = HanUi.Br(HanUi.Muted) });
                    cls.Inlines.Add(new System.Windows.Documents.Run(HT.Name(d.Tags[t])) { Foreground = new SolidColorBrush(HanCardView.TagColor(d.Tags[t])), FontWeight = FontWeights.SemiBold });
                }
            info.Children.Add(cls);
            var price = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            price.Children.Add(HanArt.Icon("icon_crowns", 20, "◉"));
            price.Children.Add(HanUi.Number(" " + d.Price, 18, can || browse ? HanUi.GoldText : HanUi.Br(HanUi.Bad)));
            info.Children.Add(price);
            info.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (can) Buy(); };
            Canvas.SetLeft(info, cx - info.Width / 2);
            Canvas.SetTop(info, ShopBandY + 12);
            _shop.Children.Add(info);
            // birleşme uyarısı: kartın sağ üst köşesinde (tezgâh kartı hep 1★, köşe boş). Kartla birlikte süzülür; kartın üstünde
            // dururken üst şeritteki rakip satırının üstüne biniyordu. Zemini koyu ve opak, gölgeli: yarı saydam yeşil zeminde yazı
            // kart resmine karışıp okunmuyordu.
            // sahadaki ve depodaki bütün kopyalar sayılır (yalnız 1★ sayılınca sahadaki 2★ kartın eşi markete gelince uyarı çıkmıyordu)
            if (CopyChip(d, DesignW) is { } chip) holder.Children.Add(chip);
        }
        // satış: sürüklenen kart banda bırakılır
        if (sel != null && _pressUid == sel.Uid)
        {
            var uid = sel.Uid;
            var zone = DropZone(new RectangleGeometry(new Rect(bx, ShopBandY - DesignH * k - 20, bw, ShopBandH + DesignH * k + 20), 10, 10), new Rect(bx, ShopBandY - DesignH * k - 20, bw, ShopBandH + DesignH * k + 20), "han-sell-drop", $"Sat  +{Tuning.SellValue(sel.Def, sel.Star)} altın", () => Do(() => Session.Act(HanActKind.Sell, uid), "han_sell"), true);
            _shop.Children.Add(zone);
        }
    }

    /// <summary>
    /// Tezgâh ve ödül kartlarının köşe uyarısı: "★ Birleşir" (elde bu karttan iki tane 1★ var) ya da "sende ..." (kopya özeti).
    /// Elde kopya yoksa (ya da kart birim değilse / kahramansa) null. <paramref name="w"/> kartın genişliği (tier taşının yeri için).
    /// </summary>
    private Border? CopyChip(CardDef d, double w)
    {
        // sahadaki ve depodaki bütün kopyalar sayılır (yalnız 1★ sayılınca sahadaki 2★ kartın eşi markete gelince uyarı çıkmıyordu)
        // 3★ kopya için hiçbir uyarı yok (daha yükselmez); 2★ için yalnız "sende var"; 1★ kopyalar sayılır
        var copies = Me.Cards.Where(c => c.Def == d && c.Star < 3).ToList();
        if (copies.Count == 0 || !d.IsUnit || d.IsHero) return null;
        var ones = copies.Count(c => c.Star == 1);
        var merge = ones == 2;
        var have = ones > 0 ? "sende " + (ones > 1 ? ones + "×" : "") + "1★" : "sende var";
        var chip = HanUi.Chip(merge ? "★ Birleşir" : have, merge ? HanUi.Good : HanUi.Muted, 11);
        chip.Padding = new Thickness(5, 2, 5, 3);   // güç madalyonunun üstüne binmesin
        chip.Background = merge
            ? new LinearGradientBrush(Color.FromRgb(0x1d, 0x33, 0x14), Color.FromRgb(0x0e, 0x18, 0x0a), 90)
            : HanUi.Br(HanUi.Ink, 0xf0);
        if (merge) chip.BorderBrush = HanUi.Br(HanUi.Good);
        chip.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1.5, Direction = 270, Opacity = 0.9 };
        chip.HorizontalAlignment = HorizontalAlignment.Right;
        chip.VerticalAlignment = VerticalAlignment.Top;
        chip.Margin = new Thickness(0, HanCardView.GemBottom(w) + 3, 4, 0);   // tier taşının altında
        chip.IsHitTestVisible = false;
        return chip;
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
        Put(SchoolBadge(Me.School, Me.Th(HTag.Mage), true), SideX, 64);
        Put(Caption("SİNERJİLER"), SideX + 4, 134);
        var y = 154.0;
        var any = false;
        // alt havuz: yalnız bu maçta açık sınıflar (ya da elinde kartı olan)
        foreach (var t in Enum.GetValues<HTag>().Where(t => M.ActiveTags.Contains(t) || counts.GetValueOrDefault(t) > 0).OrderByDescending(t => counts.GetValueOrDefault(t)).ThenBy(t => t))
        {
            var n = counts.GetValueOrDefault(t);
            any |= n > 0;
            var th = Tuning.TagLevel(t, n);
            var bp = Tuning.Breakpoints(t);
            var c = HanCardView.TagColor(t);
            var g = new Grid { Width = SideW, Height = 40, Background = Brushes.Transparent, Opacity = n == 0 ? 0.42 : 1 };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(20, 6, 6, 20), Background = th > 0 ? new LinearGradientBrush(Color.FromArgb(0xd0, (byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)), Color.FromArgb(0xb0, 0x0c, 0x07, 0x04), 0) : Theme.B("#a00c0704"), BorderBrush = th > 0 ? new SolidColorBrush(c) : Theme.B("#3a2a12"), BorderThickness = new Thickness(1) });
            g.Children.Add(new Path
            {
                Data = Geometry.Parse("M14,0 L42,0 L56,24 L42,48 L14,48 L0,24 Z"), Width = 38, Height = 34, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0),
                Fill = th > 0 ? new LinearGradientBrush(Color.Add(c, Color.FromRgb(0x30, 0x30, 0x30)), Color.Multiply(c, 0.5f), 90) : Theme.B("#2a2016"), Stroke = th > 0 ? Theme.B("#ffe9a3") : Theme.B("#5e451c"), StrokeThickness = 1.2
            });
            g.Children.Add(new TextBlock { Text = n.ToString(), Width = 38, Margin = new Thickness(3, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, FontFamily = HanUi.Font, FontSize = HanUi.S(18), FontWeight = FontWeights.Bold, Foreground = th > 0 ? Theme.B("#1a1209") : Theme.TextDim });
            // üst satır: ad ve sağda eşikler; alt satır: açık bonusun metni (yoksa boş). Bonus metni eskiden sağda 100 piksele
            // sıkışıp üç satıra kırılıyor ve satırdan taşıyordu.
            g.Children.Add(new TextBlock { Text = HT.Name(t), FontFamily = HanUi.Font, FontSize = HanUi.S(14), FontWeight = FontWeights.Bold, Foreground = th > 0 ? Theme.B("#f4ead2") : Theme.TextDim, Margin = new Thickness(48, 2, 0, 0) });
            var pips = new TextBlock { FontFamily = HanUi.Font, FontSize = HanUi.S(12), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 9, 0) };
            foreach (var step in bp)
            {
                pips.Inlines.Add(new System.Windows.Documents.Run(step.ToString()) { Foreground = n >= step ? new SolidColorBrush(Color.Add(c, Color.FromRgb(0x40, 0x40, 0x40))) : Theme.B("#55e8dcc4") });
                if (step < bp[2]) pips.Inlines.Add(new System.Windows.Documents.Run(" › ") { Foreground = Theme.B("#44e8dcc4") });
            }
            g.Children.Add(pips);
            if (th > 0)
                g.Children.Add(HanUi.Fit(new TextBlock { Text = HT.Thresholds(t)[th - 1], FontFamily = HanUi.Font, FontSize = HanUi.S(11.5), Foreground = Theme.B("#d8ccb4"), Margin = new Thickness(48, 21, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }, SideW - 56, 9.5));
            g.ToolTip = new ToolTip
            {
                Content = new TextBlock { Text = HT.Name(t) + "\n" + string.Join("\n", HT.Thresholds(t).Select((s, i) => $"{bp[i]}: {s}")), FontFamily = HanUi.Font, FontSize = HanUi.S(14), Foreground = Theme.Text },
                Background = Theme.B("#f4150f09"), BorderBrush = new SolidColorBrush(c), Padding = new Thickness(10)
            };
            Put(g, SideX, y);
            y += 44;
        }
        if (!any) Put(new TextBlock { Text = "Aynı sınıftan farklı kartlar bonus açar (çoğu sınıfta 2, 4, 6; Cüce ve Vampir 2, 3, 5). Sınıflar kartın altında yazar.", FontFamily = HanUi.Font, FontSize = HanUi.S(13), Foreground = Theme.B("#88e8dcc4"), TextWrapping = TextWrapping.Wrap, Width = SideW - 8 }, SideX + 4, y);
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
        // seçim katmanı sayaçların (satır gücü, altın, can) üstünde olmalı: _c sayaç katmanının altında kalır, bu yüzden _shop'a konur
        // (modal açıkken tezgâh gizli, katman her çizimde temizlenir; önbelleksiz, animasyonu etkilemez)
        T PutTop<T>(T e, double x, double y) where T : UIElement { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); _shop.Children.Add(e); return e; }
        PutTop(new Border { Width = 1920, Height = 1080, Background = Theme.B("#c8000000") }, 0, 0);
        var sp =new StackPanel { Margin = new Thickness(40, 32, 40, 28) };
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
        PutTop(Panel(1060, 540, sp, 0xf0, null, 16), 430, 220);
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
