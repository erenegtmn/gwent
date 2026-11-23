using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using GwentClient.HanLigi;
using HanLigi;

namespace GwentClient;

/// <summary>Han Ligi ekranlarının görüntüleri: okul, hazırlık (erken ve geç), aktivasyon, savaş, round sonucu, bitiş.</summary>
public static class HanShots
{
    private static void Click(HanView v, string tag)
    {
        UiSelfTest.Layout(v);
        var b = UiSelfTest.FindAll<Button>(v, x => (x.Tag as string) == tag).First();
        UiSelfTest.Raise(b, ButtonBase.ClickEvent);
        UiSelfTest.Layout(v);
    }

    /// <summary>Han odası: aynı sahne üç kamerayla (oyundaki masa görüşü, ara, geniş açılış çekimi).</summary>
    public static void Tavern(Action<FrameworkElement, string> save)
    {
        var board = HanScene.BoardTexture();
        FrameworkElement Img(System.Windows.Media.Imaging.BitmapSource b) => new System.Windows.Controls.Image { Source = b, Width = 1920, Height = 1080 };
        save(Img(HanTavern.Render(HanTavern.GameCamera(-28, 1.3, 18.65, 55, -0.595), 1920, 1080, board)), "han_sahne_1_oyun.png");
        save(Img(HanTavern.Render(HanTavern.Camera(new(-6, -30, 12), new(1, 6, -2), 56), 1920, 1080, board, 4)), "han_sahne_2_orta.png");
        save(Img(HanTavern.Render(HanTavern.Camera(new(-12, -42, 9), new(3, 16, -2), 58), 1920, 1080, board, 5)), "han_sahne_3_genis.png");
    }

    /// <summary>Yetenek simgeleri galerisi: her simge büyük ve kartlardaki boyutlarda (masadaki kartta ~15 px).</summary>
    private static FrameworkElement IconGallery()
    {
        var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x13, 0x0c)) };
        var wrap = new WrapPanel { Margin = new Thickness(40, 40, 40, 40) };
        foreach (var (name, g) in HanIcons.All)
        {
            var tile = new StackPanel { Width = 220, Margin = new Thickness(0, 0, 14, 26) };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(HanIcons.Medal(g, 110));
            var smalls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 4) };
            foreach (var s in new[] { 36.0, 22, 15 })
            {
                var m = HanIcons.Medal(g, s);
                m.Margin = new Thickness(0, 0, 8, 0);
                m.VerticalAlignment = VerticalAlignment.Bottom;
                smalls.Children.Add(m);
            }
            line.Children.Add(smalls);
            tile.Children.Add(line);
            tile.Children.Add(new TextBlock { Text = name, FontFamily = HanUi.Font, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xf0, 0xdc, 0xaa)), Margin = new Thickness(2, 6, 0, 0) });
            wrap.Children.Add(tile);
        }
        root.Children.Add(wrap);
        return root;
    }

    public static void Run(Action<FrameworkElement, string> save)
    {
        var rng = new Random(5);
        var s = new HanSession(20261113, "Eren") { Timed = false };
        var v = new HanView(s, () => { }, () => { }, animate: false);
        save(v, "han_1_okul.png");
        save(IconGallery(), "han_ikonlar.png");
        s.ChooseSchool(s.Player.SchoolOptions[0]);
        save(new HanView(s, () => { }, () => { }, animate: false), "han_2a_tezgah.png");
        // online hazırlık: Hazır düğmesi yok, yerinde ortak süre notu (eylemler sunucuya gitmez, kopya doğrudan ilerletilir)
        var os = new HanSession(new GwentNet.HanSetup(20261113, new() { "Eren", "Lambert", "Eskel", "Keira" }, new() { false, true, true, true }, new() { null, null, null, null }, 0), _ => { });
        os.G.Apply(new HanAction(HanActKind.School, 0, (int)os.Player.SchoolOptions[0]));
        save(new HanView(os, () => { }, () => { }, animate: false), "han_2c_online_hazirlik.png");
        // tezgâhta birleşme uyarısı: elde iki kopya ("★ Birleşir") ve bir kopya ("sende 1") olan kartlar; yazı kart resminde okunmalı
        var ms = new HanSession(20261113, "Eren") { Timed = false };
        ms.ChooseSchool(ms.Player.SchoolOptions[0]);
        var shop = ms.Player.Shop;
        if (shop.Where(d => d is { IsUnit: true, IsHero: false }).Select(d => d!).Distinct().Take(2).ToList() is [var two, var one])
        {
            ms.Player.Gold = 50;
            shop[0] = two; shop[1] = two; shop[2] = one;
            ms.Act(HanActKind.Buy, 0);
            ms.Act(HanActKind.Buy, 1);
            ms.Act(HanActKind.Buy, 2);
            shop[0] = two; shop[1] = one; shop[2] = two;
            save(new HanView(ms, () => { }, () => { }, animate: false), "han_2d_birlesir.png");
        }
        // erken hazırlık: marketten alınabilenleri al, sahaya sür
        for (var i = 0; i < Tuning.ShopSize; i++) s.Act(HanActKind.Buy, i);
        foreach (var c in s.Player.Cards.ToList()) s.Act(HanActKind.Move, c.Uid, (int)Match.ZonesFor(c.Def).First());
        v = new HanView(s, () => { }, () => { }, animate: false);
        save(v, "han_2_hazirlik_erken.png");
        // kart incelemesi (fare kartın üstünde): yalnız bilgi penceresi
        if (UiSelfTest.FindAll<HanCardView>(v, c => (c.Tag as string)?.StartsWith("han-card:") == true).FirstOrDefault() is { } hc)
        {
            hc.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            UiSelfTest.Layout(v);
            save(v, "han_4b_inceleme.png");
        }
        // kahraman kartı ve bilgi paneli (güneş köşesi): görüntü için depoya geçici olarak Yennefer konur
        var hero = new OwnedCard { Uid = 990001, Def = CardSet.Get("yennefer") };
        s.Player.Cards.Add(hero);
        v = new HanView(s, () => { }, () => { }, animate: false);
        UiSelfTest.Layout(v);
        if (UiSelfTest.FindAll<HanCardView>(v, c => (c.Tag as string) == "han-card:990001").FirstOrDefault() is { } yc)
        {
            yc.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            UiSelfTest.Layout(v);
            save(v, "han_4c_kahraman.png");
        }
        s.Player.Cards.Remove(hero);
        // ekonomi şeridi uç durumda: üç basamaklı altın, uzun yenilgi serisi, hazine ve soylu geliri (taşma denetimi)
        var (gold0, streak0) = (s.Player.Gold, s.Player.Streak);
        s.Player.Gold = 137;
        s.Player.Streak = -6;
        save(new HanView(s, () => { }, () => { }, animate: false), "han_4d_ekonomi.png");
        (s.Player.Gold, s.Player.Streak) = (gold0, streak0);
        Click(v, "han-help");
        save(v, "han_3_yardim.png");
        // orta oyun: insan koltuğunu bot oynatır
        for (var i = 0; i < 40 && s.M.Round < 11 && s.Player.Alive && !s.Over; i++) s.AutoRound(rng);
        if (!s.Over && s.Player.Alive)
        {
            v = new HanView(s, () => { }, () => { }, animate: false);
            save(v, "han_4_hazirlik_gec.png");
            if (s.Player.RewardOptions.Length > 0) s.Act(HanActKind.Reward, 0);
            if (s.Player.PotionOptions.Length > 0) s.Act(HanActKind.Potion, 0);
            // masa sınırı aşık (hazırlıkta esner): sayı kırmızı, üstte uyarı; görüntüden sonra geçici kartlar kalkar
            var extras = new[] { "ghoul", "griffin" }.Select((id, i) => new OwnedCard { Uid = 990020 + i, Def = CardSet.Get(id), Zone = Zone.Close }).ToList();
            while (s.Player.Used + extras.Count <= s.Player.Slots) extras.Add(new OwnedCard { Uid = 990020 + extras.Count, Def = CardSet.Get("ghoul"), Zone = Zone.Close });
            s.Player.Cards.AddRange(extras);
            save(new HanView(s, () => { }, () => { }, animate: false), "han_4e_sinir_asik.png");
            foreach (var x in extras) s.Player.Cards.Remove(x);
            if (s.Player.RewardOptions.Length > 0) s.Act(HanActKind.Reward, 0);
            if (s.Player.PotionOptions.Length > 0) s.Act(HanActKind.Potion, 0);
            // özel kartlar savaşta görünsün: Boynuz (Yakın) ve Ayaz
            s.Player.Cards.Add(new OwnedCard { Uid = 990010, Def = CardSet.Get("horn"), Zone = Zone.Special, Aim = Row.Close });
            s.Player.Cards.Add(new OwnedCard { Uid = 990011, Def = CardSet.All.First(d => d.Special == Special.Frost), Zone = Zone.Special });
            // rakibin Boynuz'u 2. satırda: han_5/6'da gizli, satır açılınca (han_7/8) görünür
            if (s.Player.OppKind != OpponentKind.Pve && s.Player.OppId >= 0)
                s.M.Players[s.Player.OppId].Cards.Add(new OwnedCard { Uid = 990012, Def = CardSet.Get("horn"), Zone = Zone.Special, Aim = s.Player.Order[1] });
            v = new HanView(s, () => { }, () => { }, animate: false);
            Click(v, "han-ready");
            save(v, "han_5_aktivasyon.png");
            Click(v, "han-act-go");
            save(v, "han_6_savas.png");
            Click(v, "han-go");
            save(v, "han_7_savas_pencere2.png");
            Click(v, "han-go");
            Click(v, "han-go");
            save(v, "han_8_savas_bitti.png");
            Click(v, "han-continue");
            save(v, "han_9_round_sonucu.png");
        }
        for (var i = 0; i < 80 && !s.Over && s.Player.Alive; i++) s.AutoRound(rng);
        v = new HanView(s, () => { }, () => { }, animate: false);
        save(v, "han_10_bitis.png");
        FxEngine.Manual = true;
        try { Gallery(save); Live(save); }
        finally { FxEngine.Manual = false; }
    }

    private static void Pump(int ms, FxEngine? engine = null)
    {
        // zamanlayıcılar gerçek zamanla, parçacıklar elle aynı süre kadar ilerler
        for (var left = ms; left > 0; left -= 33)
        {
            var step = Math.Min(33, left);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(step) };
            t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
            t.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            engine?.Advance(step / 1000.0);
        }
    }

    /// <summary>Efekt galerisi: her efekt boş bir masa sahnesinde, birkaç anından kare.</summary>
    private static void Gallery(Action<FrameworkElement, string> save)
    {
        void Shot(string name, Action<Canvas, FxEngine> play, params int[] at)
        {
            var root = new Grid { Width = 1920, Height = 1080, Background = Brushes.Black };
            var scene = new HanScene(false);
            root.Children.Add(scene);
            var layer = new Canvas { Width = 1920, Height = 1080 };
            var engine = new FxEngine();
            root.Children.Add(layer);
            root.Children.Add(engine);
            UiSelfTest.Layout(root);
            play(layer, engine);
            var last = 0;
            foreach (var ms in at)
            {
                Pump(ms - last, engine);
                last = ms;
                save(root, $"han_fxg_{name}_{ms}.png");
            }
        }
        var from = new Point(640, 700);
        var target = new Point(1060, 330);
        Shot("igni", (l, e) => { HanFx.DrawGlyph(l, e, global::HanLigi.Sign.Igni, from, 170); Fx.After(700, () => HanFx.Igni(e, from, target, 100, true)); }, 300, 900, 1200, 1500);
        Shot("aard", (l, e) => HanFx.Aard(e, from, new Point(1010, 330), 900), 150, 420, 700);
        Shot("quen", (l, e) => HanFx.Quen(l, e, new Point(1010, 640), 1000, true), 300, 650, 900);
        Shot("yrden", (l, e) => HanFx.Yrden(l, e, new Point(1010, 330), 900, true), 300, 700);
        Shot("frost", (l, e) => HanFx.Throw(e, from, target, FxEngine.Ramp.Ice, 500, () => HanFx.Frost(e, target)), 300, 650);
        Shot("fireball", (l, e) => HanFx.Fireball(e, new Point(1000, 500), new Point(1690, 300), 700, () => { }), 400, 820);
    }

    /// <summary>Canlı kip (animasyonlar açık): bir pencerenin atış, isabet ve sonuç anlarından görüntü.</summary>
    private static void Live(Action<FrameworkElement, string> save)
    {
        var rng = new Random(9);
        var s = new HanSession(424242, "Eren") { Timed = false };
        s.ChooseSchool(s.Player.SchoolOptions[0]);
        for (var i = 0; i < 40 && s.M.Round < 9 && s.Player.Alive && !s.Over; i++) s.AutoRound(rng);
        if (s.Over || !s.Player.Alive) return;
        if (s.Player.RewardOptions.Length > 0) s.Act(HanActKind.Reward, 0);
        if (s.Player.PotionOptions.Length > 0) s.Act(HanActKind.Potion, 0);
        // masa sınırını aş: depodaki birimler masaya (kilitte fazlası depoya iner ya da satılır; gidişleri görünür)
        foreach (var c in s.Player.Bench.Where(c => c.Def.IsUnit).Take(3).ToList()) s.Act(HanActKind.Move, c.Uid, (int)Match.ZonesFor(c.Def).First(z => z != Zone.Ambush));
        var v = new HanView(s, () => { }, () => { }, animate: true);
        UiSelfTest.Layout(v);
        Pump(300);
        save(v, "han_fx_t0_fazla_uyari.png");
        Click(v, "han-ready");
        // (görüntü kaydı yaklaşık bir saniye sürer: gidişin ortası tek kareyle alınır)
        Pump(820, v.Engine);
        save(v, "han_fx_t1_fazla_gidis.png");
        Pump(1500, v.Engine);
        Click(v, "han-act-go");
        Pump(2600, v.Engine);
        UiSelfTest.Layout(v);
        foreach (var tag in new[] { "han-sign:Igni", "han-sign:Aard" })
            if (UiSelfTest.FindAll<Button>(v, x => (x.Tag as string) == tag && x.IsEnabled).FirstOrDefault() is { } b) { UiSelfTest.Raise(b, ButtonBase.ClickEvent); UiSelfTest.Layout(v); break; }
        if (UiSelfTest.FindAll<Button>(v, x => (x.Tag as string)?.StartsWith("han-use:") == true).FirstOrDefault() is { } pot) { UiSelfTest.Raise(pot, ButtonBase.ClickEvent); UiSelfTest.Layout(v); }
        save(v, "han_fx_0_secim.png");
        // savaşta tezgâh: alt şerit zar gibi dönerek depo satırına geçer (dönüşün ortası ve sonu), sonra geri döner
        UiSelfTest.Raise(UiSelfTest.FindAll<Button>(v, x => (x.Tag as string) == "han-battle-shop").First(), ButtonBase.ClickEvent);
        save(v, "han_fx_s0_zar_baslangic.png");   // dönüş başlamadan (küp 0°, üstünde eski şeridin görüntüsü): yeni şerit hiçbir yerde görünmemeli
        Pump(90, v.Engine);
        save(v, "han_fx_s1_zar.png");
        Pump(110, v.Engine);
        save(v, "han_fx_s2_zar.png");
        Pump(800, v.Engine);
        save(v, "han_fx_s3_tezgah.png");
        Click(v, "han-battle-shop");
        Pump(240, v.Engine);
        save(v, "han_fx_s4_zar_geri.png");
        Pump(800, v.Engine);
        Click(v, "han-go");
        Pump(700, v.Engine);
        save(v, "han_fx_1_secimler.png");
        Pump(2400, v.Engine);
        save(v, "han_fx_2_isaret.png");
        Pump(650, v.Engine);
        save(v, "han_fx_3_isabet.png");
        Pump(900, v.Engine);
        save(v, "han_fx_4_isabet2.png");
        Pump(2600, v.Engine);
        save(v, "han_fx_5_sonraki.png");
        Pump(4000, v.Engine);
        save(v, "han_fx_6_sonuc.png");
        Pump(1500, v.Engine);
    }
}
