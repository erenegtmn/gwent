using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GwentClient.HanLigi;
using HanLigi;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Han Ligi arayüzü: maçlar baştan sona yalnız etiketli öğelere (han-*) rastgele tıklanarak oynanır. Her adımda kuralların
    /// değişmezleri (slot, depo, altın) denetlenir; maç bitmeli, bitiş ekranından menüye dönülebilmeli.
    /// </summary>
    /// <summary>Öğe ekran katmanında mı (3D masanın içinde değil)?</summary>
    private static bool IsOnScreen(FrameworkElement root, FrameworkElement el)
    {
        for (DependencyObject? cur = el; cur != null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
            if (cur is System.Windows.Media.Media3D.Viewport2DVisual3D || cur is System.Windows.Media.Media3D.Visual3D) return false;
        try { el.TranslatePoint(new Point(0, 0), root); return true; } catch { return false; }
    }

    public static int RunHanCheck(int games)
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] han: " + what); } }
        var totalRounds = 0;
        var deselects = 0;
        var swaps = 0;
        var battleShops = 0;
        var overfull = 0;
        for (var g = 0; g < games; g++)
        {
            try
            {
                var rng = new Random(100 + g);
                var s = new HanSession(500 + g, "Sınama") { Timed = false };
                var back = false;
                var again = false;
                var view = new HanView(s, () => back = true, () => again = true, animate: false);
                Layout(view);
                // yardım açılır ve kapanır
                Raise(FindAll<Button>(view, b => (b.Tag as string) == "han-help").First(), ButtonBase.ClickEvent); Layout(view);
                Check(FindAll<Button>(view, b => (b.Tag as string) == "han-help-close").Any(), "yardım açılmadı");
                Raise(FindAll<Button>(view, b => (b.Tag as string) == "han-help-close").First(), ButtonBase.ClickEvent); Layout(view);
                Check(FindAll<Button>(view, b => (b.Tag as string)?.StartsWith("han-school:") == true).Count() == 3, "üç okul seçeneği yok");

                var steps = 0;
                for (; steps < 20000 && !s.Over; steps++)
                {
                    Layout(view);
                    string T(FrameworkElement f) => f.Tag as string ?? "";
                    if (steps == 60)
                    {
                        // maç sürerken çıkış önce sorulur; "Masada kal" maçı sürdürür
                        Raise(FindAll<Button>(view, b => T(b) == "han-exit").First(), ButtonBase.ClickEvent); Layout(view);
                        Check(!back && FindAll<Button>(view, b => T(b) == "han-exit-confirm").Any(), "maç sürerken çıkış sorulmadı");
                        if (FindAll<Button>(view, b => T(b) == "han-exit-cancel").FirstOrDefault() is { } stay) { Raise(stay, ButtonBase.ClickEvent); Layout(view); }
                        Check(!FindAll<Button>(view, b => T(b) == "han-exit-confirm").Any(), "çıkış penceresi kapanmadı");
                    }
                    var buttons = FindAll<Button>(view, b => b.IsEnabled && T(b).StartsWith("han-") && T(b) is not ("han-exit" or "han-help" or "han-menu" or "han-again")).ToList();
                    var cards = FindAll<HanCardView>(view, c => T(c).StartsWith("han-")).ToList();
                    // seçim penceresi açıkken yalnız onun öğeleri tıklanabilir
                    var modal = buttons.Where(b => T(b).StartsWith("han-potion:")).Cast<FrameworkElement>().Concat(cards.Where(c => T(c).StartsWith("han-reward:"))).ToList();
                    FrameworkElement pick;
                    if (modal.Count > 0)
                    {
                        pick = modal[rng.Next(modal.Count)];
                    }
                    else
                    {
                        var ready = buttons.FirstOrDefault(b => T(b) == "han-ready");
                        if (ready != null && rng.Next(100) < 12) pick = ready;
                        else
                        {
                            var all = buttons.Where(b => T(b) != "han-ready").Cast<FrameworkElement>().Concat(cards).ToList();
                            if (all.Count == 0)
                            {
                                Check(ready != null, "tıklanacak öğe yok");
                                if (ready == null) break;
                                pick = ready;
                            }
                            else pick = all[rng.Next(all.Count)];
                        }
                    }
                    // savaşta tezgâh: alt şerit depo satırına döner, tezgâh yalnız bakılır, kilit çalışır, kapanınca şerit geri gelir
                    if (FindAll<Button>(view, b => T(b) == "han-battle-shop" && b.IsEnabled).FirstOrDefault() is { } bs && rng.Next(6) == 0)
                    {
                        // rastgele tıklamalar tezgâhı zaten açmış olabilir: önce kapat
                        if (FindAll<FrameworkElement>(view, f => T(f) == "han-battle-bench").Any())
                        {
                            Raise(bs, ButtonBase.ClickEvent); Layout(view);
                            Check(!FindAll<FrameworkElement>(view, f => T(f) == "han-battle-bench").Any(), "savaşta tezgâh kapanmadı");
                            if (FindAll<Button>(view, b => T(b) == "han-battle-shop" && b.IsEnabled).FirstOrDefault() is not { } reopen) continue;
                            bs = reopen;
                        }
                        Raise(bs, ButtonBase.ClickEvent); Layout(view);
                        Check(FindAll<FrameworkElement>(view, f => T(f) == "han-battle-bench").Any(), "savaşta tezgâh açılınca depo satırı yok");
                        Check(!FindAll<FrameworkElement>(view, f => T(f).StartsWith("han-shop:") || T(f) == "han-reroll-band").Any(), "savaşta tezgâhtan alınabiliyor ya da yenilenebiliyor");
                        Check(s.Player.Shop.All(d => d == null) || FindAll<FrameworkElement>(view, f => T(f).StartsWith("han-shop-view:")).Any(), "savaşta tezgâh kartları görünmüyor");
                        var locked = s.Player.ShopLocked;
                        if (FindAll<Button>(view, b => T(b) == "han-lock" && b.IsEnabled).FirstOrDefault() is { } lk) { Raise(lk, ButtonBase.ClickEvent); Layout(view); }
                        Check(s.Player.ShopLocked != locked, $"savaşta tezgâh kilitlenemedi ({view.DebugState}, faz {s.M.Phase}, kilit düğmesi {FindAll<Button>(view, b => T(b) == "han-lock").Count()}/{FindAll<Button>(view, b => T(b) == "han-lock" && b.IsEnabled).Count()}, depo {FindAll<FrameworkElement>(view, f => T(f) == "han-battle-bench").Count()})");
                        if (FindAll<Button>(view, b => T(b) == "han-battle-shop" && b.IsEnabled).FirstOrDefault() is { } bs2) { Raise(bs2, ButtonBase.ClickEvent); Layout(view); }
                        Check(!FindAll<FrameworkElement>(view, f => T(f) == "han-battle-bench").Any(), "tezgâh kapanınca depo satırı hâlâ duruyor");
                        battleShops++;
                        continue;
                    }
                    // depodaki birimleri masaya sür (masa dolu olsa da): hazırlıkta sınır esner, kilitte fazlası ayıklanır
                    if (s.M.Phase == Phase.Prep && modal.Count == 0 && s.Player.Bench.Any(c => c.Def.IsUnit) && rng.Next(10) == 0)
                    {
                        foreach (var extra in s.Player.Bench.Where(c => c.Def.IsUnit).ToList())
                        {
                            var zone = Match.ZonesFor(extra.Def).First(z => z != Zone.Ambush);
                            if (FindAll<HanCardView>(view, c => T(c) == "han-card:" + extra.Uid).FirstOrDefault() is not { } ce) continue;
                            view.Deselect(); Layout(view);
                            Raise(ce, UIElement.MouseLeftButtonUpEvent); Layout(view);
                            if (FindAll<Button>(view, b => T(b) == "han-move:" + zone).FirstOrDefault() is { IsEnabled: true } mv) { Raise(mv, ButtonBase.ClickEvent); Layout(view); }
                            Check(extra.Zone == zone, $"{extra.Def.Id} masaya konamadı (masa {s.Player.Used}/{s.Player.Slots})");
                        }
                        if (s.Player.Overfull) overfull++;
                        continue;
                    }
                    // sürükle-bırak takası: kartımı gerçek bırakma yolundan başka bir kartımın üstüne bırak
                    if (s.M.Phase == Phase.Prep && s.Player.Cards.Count >= 2 && rng.Next(8) == 0)
                    {
                        var cs = s.Player.Cards;
                        var (a, b2) = (cs[rng.Next(cs.Count)], cs[rng.Next(cs.Count)]);
                        var before = string.Join(",", cs.Select(c => c.Uid + ":" + c.Zone));
                        var can = a != b2 && s.M.CanSwapCards(s.Me, a.Uid, b2.Uid);
                        var (za, zb) = (a.Zone, b2.Zone);
                        if (view.TestDragOnto(a.Uid, b2.Uid))
                        {
                            Layout(view);
                            var after = string.Join(",", s.Player.Cards.Select(c => c.Uid + ":" + c.Zone));
                            if (can)
                            {
                                Check(a.Zone == zb && b2.Zone == za && before != after, $"takas olmadı: {a.Def.Id} {za} ↔ {b2.Def.Id} {zb}");
                                swaps++;
                            }
                            else Check(before == after, "geçersiz takas durumu değiştirdi");
                        }
                        continue;
                    }
                    // Her şey ekran katmanında: kartlarda gerçek isabet denetimi yapılır (görünmeyen öğeye tıklama doğrudan gönderilir).
                    if (pick is Button btn) Raise(btn, ButtonBase.ClickEvent);
                    else if (IsOnScreen(view, pick)) ClickAt(view, pick);
                    else Raise(pick, UIElement.MouseLeftButtonUpEvent);
                    // boş yere tıklamak seçimi bırakır (masaya tıklama: olay kimse tarafından tüketilmez)
                    if (view.Selected != null && rng.Next(6) == 0)
                    {
                        Layout(view);
                        // gerçek tıklama gibi: kabarcıklanan MouseUp, yol üstündeki her öğede MouseLeftButtonUp doğurur
                        var scene = FindAll<HanScene>(view, _ => true).First();
                        scene.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent, Source = scene });
                        Layout(view);
                        Check(view.Selected == null, "boş yere tıklama seçimi bırakmadı");
                        deselects++;
                    }
                    var p = s.Player;
                    Check(p.Gold >= 0, "altın eksiye düştü");
                    // hazırlıkta masa sınırı esner (kırmızı uyarı); kilitten sonra sınır içinde olmalı
                    if (s.M.Phase != Phase.Prep) Check(p.Used <= p.Slots && p.Cards.Count(c => c.Zone == Zone.Ambush) <= p.AmbushCap, "kilitten sonra saha slot sınırı aşık");
                    Check(p.Bench.Count() <= Tuning.BenchSize, "depo sınırı aşıldı");
                    if (s.MyBattle is { } b) Check(b.Sides[s.MySide].Focus >= 0, "Odak eksiye düştü");
                }
                Layout(view);
                Check(s.Over, $"maç bitmedi ({steps} adım, round {s.M.Round})");
                Check(s.M.Players.Count(p => p.Place == 1) == 1, "tek kazanan yok");
                totalRounds += s.M.Round;
                var menu = FindAll<Button>(view, b => (b.Tag as string) == "han-menu").FirstOrDefault();
                Check(menu != null, "bitiş ekranı açılmadı");
                if (menu != null) Raise(menu, ButtonBase.ClickEvent);
                Check(back, "menüye dönülmedi");
                var ag = FindAll<Button>(view, b => (b.Tag as string) == "han-again").FirstOrDefault();
                if (ag != null) Raise(ag, ButtonBase.ClickEvent);
                Check(again, "yeniden oyna çalışmadı");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("[HATA] han: " + ex);
            }
        }
        Check(deselects > 0, "boş yere tıklama hiç denenmedi");
        Check(swaps > 0, "sürükle-bırak takası hiç olmadı");
        Check(overfull > 0, "masa sınırı hiç aşılmadı");
        Check(battleShops > 0, "savaşta tezgâh hiç açılmadı");
        Console.WriteLine($"Han Ligi: {games} maç, {totalRounds} round, {deselects} boş tıklama, {swaps} takas, {overfull} kez sınır aşıldı, {battleShops} kez savaşta tezgâh, {failures} hata.");
        return failures;
    }
}
