using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GwentClient.HanLigi;
using HanLigi;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Han Ligi kare hızı ölçümü: gerçek bir pencerede hazırlık (süre akarken) ve savaş (efektler oynarken) sırasında saniyedeki
    /// kare sayısını ve UI iş parçacığındaki en uzun kareyi yazar. Hedef: ikisinde de ~60 kare/sn.
    /// </summary>
    public static int RunHanPerf()
    {
        var rng = new Random(9);
        var s = new HanSession(424242, "Ölçüm") { Timed = false };
        s.ChooseSchool(s.Player.SchoolOptions[0]);
        for (var i = 0; i < 40 && s.M.Round < 7 && s.Player.Alive && !s.Over; i++) s.AutoRound(rng);
        if (s.Player.RewardOptions.Length > 0) s.Act(HanActKind.Reward, 0);
        if (s.Player.PotionOptions.Length > 0) s.Act(HanActKind.Potion, 0);
        s.Timed = true;
        var view = new HanView(s, () => { }, () => { }, animate: true);
        var win = new Window { Width = 1600, Height = 900, Content = view, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Han Ligi ölçüm" };
        win.Show();
        Console.WriteLine($"Görüntü katmanı (RenderCapability.Tier): {RenderCapability.Tier >> 16}");

        void Wait(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }
        (double Fps, double Worst) Measure(int ms)
        {
            var frames = 0;
            var sw = Stopwatch.StartNew();
            var last = sw.Elapsed.TotalMilliseconds;
            var worst = 0.0;
            void OnFrame(object? o, EventArgs e) { frames++; var now = sw.Elapsed.TotalMilliseconds; worst = Math.Max(worst, now - last); last = now; }
            CompositionTarget.Rendering += OnFrame;
            Wait(ms);
            CompositionTarget.Rendering -= OnFrame;
            return (frames * 1000.0 / sw.Elapsed.TotalMilliseconds, worst);
        }
        void Click(string tag)
        {
            var b = FindAll<Button>(view, x => (x.Tag as string) == tag && x.IsEnabled).FirstOrDefault();
            if (b != null) Raise(b, ButtonBase.ClickEvent);
        }

        Wait(1500);
        var prep = Measure(3000);
        Console.WriteLine($"Hazırlık (süre akıyor): {prep.Fps:F0} kare/sn, en uzun kare {prep.Worst:F0} ms");
        // bir kart seç: bırakma alanları açılır
        var card = FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true).FirstOrDefault();
        if (card != null) { Raise(card, UIElement.MouseLeftButtonUpEvent); }
        var sel = Measure(1500);
        Console.WriteLine($"Hazırlık (kart seçili): {sel.Fps:F0} kare/sn, en uzun kare {sel.Worst:F0} ms");
        Click("han-ready");
        Wait(2600);
        Click("han-act-go");
        Wait(2500);
        Click("han-sign:Igni");
        Click("han-go");
        FxEngine.StatFrames = 0; FxEngine.StatMs = 0; FxEngine.StatMaxMs = 0;
        var battle = Measure(3000);
        Console.WriteLine($"Savaş (işaret efektleri): {battle.Fps:F0} kare/sn, en uzun kare {battle.Worst:F0} ms; motor kare başı {FxEngine.StatMs / Math.Max(1, FxEngine.StatFrames):F1} ms (en çok {FxEngine.StatMaxMs:F1})");
        Wait(1500);
        Click("han-sign:Aard");
        Click("han-go");
        FxEngine.StatFrames = 0; FxEngine.StatMs = 0; FxEngine.StatMaxMs = 0;
        var battle2 = Measure(3000);
        Console.WriteLine($"Savaş (Aard): {battle2.Fps:F0} kare/sn, en uzun kare {battle2.Worst:F0} ms; motor kare başı {FxEngine.StatMs / Math.Max(1, FxEngine.StatFrames):F1} ms (en çok {FxEngine.StatMaxMs:F1})");
        Console.WriteLine($"Ekranı baştan kurma (UI iş parçacığı, en uzun): {HanView.RenderMaxMs:F0} ms");
        win.Close();
        return 0;
    }
}
