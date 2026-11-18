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

    public static void Run(Action<FrameworkElement, string> save)
    {
        var rng = new Random(5);
        var s = new HanSession(20261113, "Eren") { Timed = false };
        var v = new HanView(s, () => { }, () => { }, animate: false);
        save(v, "han_1_okul.png");
        s.ChooseSchool(s.Player.SchoolOptions[0]);
        save(new HanView(s, () => { }, () => { }, animate: false), "han_2a_tezgah.png");
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
        var v = new HanView(s, () => { }, () => { }, animate: true);
        UiSelfTest.Layout(v);
        Pump(300);
        Click(v, "han-ready");
        Pump(300);
        Click(v, "han-act-go");
        Pump(2600, v.Engine);
        UiSelfTest.Layout(v);
        foreach (var tag in new[] { "han-sign:Igni", "han-sign:Aard" })
            if (UiSelfTest.FindAll<Button>(v, x => (x.Tag as string) == tag && x.IsEnabled).FirstOrDefault() is { } b) { UiSelfTest.Raise(b, ButtonBase.ClickEvent); UiSelfTest.Layout(v); break; }
        if (UiSelfTest.FindAll<Button>(v, x => (x.Tag as string)?.StartsWith("han-use:") == true).FirstOrDefault() is { } pot) { UiSelfTest.Raise(pot, ButtonBase.ClickEvent); UiSelfTest.Layout(v); }
        save(v, "han_fx_0_secim.png");
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
