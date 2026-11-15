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
                    Check(p.Used <= p.Slots && p.Cards.Count(c => c.Zone == Zone.Ambush) <= p.AmbushCap, "saha slot sınırı aşıldı");
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
        Console.WriteLine($"Han Ligi: {games} maç, {totalRounds} round, {deselects} boş tıklama, {failures} hata.");
        return failures;
    }
}
