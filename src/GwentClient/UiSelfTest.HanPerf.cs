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
        var win = new Window { Width = 1600, Height = 900, Content = view, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Han Ligi ölçüm", Topmost = true };
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
        // savaşta tezgâh: alt şeridin zar dönüşü gerçek ekrandan kare kare yakalanır (HAN_ROLL_FRAMES=klasör). İlk karelerde
        // yeni şerit (depo) görünmemeli: önce eski şerit, sonra dönen küp.
        if (Environment.GetEnvironmentVariable("HAN_ROLL_FRAMES") is { Length: > 0 } dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (var pass in new[] { "ac", "kapa", "fare_ac", "fare_kapa" })
            {
                var r = view.BarScreenRect();
                {
                    var whole = ScreenGrab(new Int32Rect(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight));
                    var e0 = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    e0.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(whole));
                    using var f0 = System.IO.File.Create(System.IO.Path.Combine(dir, $"ekran_{pass}.png"));
                    e0.Save(f0);
                    Console.WriteLine($"önce: depo satırı {FindAll<FrameworkElement>(view, x => (x.Tag as string) == "han-battle-bench").Count()}, düğme {string.Join(",", FindAll<Button>(view, x => (x.Tag as string) == "han-battle-shop").Select(x => x.IsEnabled))}, {view.DebugState}");
                }
                var shots = new List<System.Windows.Media.Imaging.BitmapSource>();
                void Grab(object? o, EventArgs e) { if (shots.Count < 30) shots.Add(ScreenGrab(r)); }
                shots.Add(ScreenGrab(r));
                CompositionTarget.Rendering += Grab;
                if (pass.StartsWith("fare") && FindAll<Button>(view, x => (x.Tag as string) == "han-battle-shop").FirstOrDefault() is { } fb)
                {
                    // gerçek fare: imleç düğmeye gider, bas-bırak (Space ile tıklamanın farkı görülsün)
                    var c = fb.PointToScreen(new Point(fb.ActualWidth / 2, fb.ActualHeight / 2));
                    SetCursorPos((int)c.X, (int)c.Y);
                    Wait(250);
                    shots.Clear();
                    shots.Add(ScreenGrab(r));
                    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
                    Wait(60);
                    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
                }
                else Click("han-battle-shop");
                Wait(900);
                CompositionTarget.Rendering -= Grab;
                for (var i = 0; i < shots.Count; i++)
                {
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shots[i]));
                    using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"zar_{pass}_{i:00}.png"));
                    enc.Save(f);
                }
                Console.WriteLine($"Zar dönüşü ({pass}): {shots.Count} kare yakalandı -> {dir}");
                Wait(600);
            }
        }
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

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    /// <summary>Ekranın bir bölgesi (aygıt pikseli), o anda gösterilen haliyle.</summary>
    private static System.Windows.Media.Imaging.BitmapSource ScreenGrab(Int32Rect r)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, r.Width, r.Height);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020);
        SelectObject(mem, old);
        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        return src;
    }
}
