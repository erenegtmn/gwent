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
    /// Han açılışı (HAN_TAVERN=1 ya da kare klasörü): okul seçiminde canlı han görüntüsü ve okul seçilince kameranın masaya inişi.
    /// Kare hızı yazılır; değer bir klasörse iniş kare kare oraya kaydedilir (kayıt yavaştır, o koşuda kare hızına bakılmaz).
    /// </summary>
    private static int RunTavernPerf(string arg)
    {
        var dir = arg is "1" or "" ? null : arg;
        if (dir != null) System.IO.Directory.CreateDirectory(dir);
        var s = new HanSession(424242, "Ölçüm") { Timed = false };
        var view = new HanView(s, () => { }, () => { }, animate: true);
        var win = new Window { Width = 1600, Height = 900, Content = view, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Han Ligi açılış", Topmost = true };
        win.Show();
        void Wait(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }
        var origin = view.PointToScreen(new Point(0, 0));
        var dpi = PresentationSource.FromVisual(view)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var area = new Int32Rect((int)origin.X, (int)origin.Y, (int)(view.ActualWidth * dpi), (int)(view.ActualHeight * dpi));
        (double Fps, double Worst, int Saved) Run(int ms, string? tag, int every)
        {
            var frames = 0;
            var sw = Stopwatch.StartNew();
            var last = 0.0;
            var worst = 0.0;
            var shots = new List<(double Ms, System.Windows.Media.Imaging.BitmapSource Img)>();
            void OnFrame(object? o, EventArgs e)
            {
                frames++;
                var now = sw.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, now - last);
                last = now;
                if (tag != null && frames % every == 0 && shots.Count < 120) shots.Add((now, ScreenGrab(area)));
            }
            CompositionTarget.Rendering += OnFrame;
            Wait(ms);
            CompositionTarget.Rendering -= OnFrame;
            for (var i = 0; i < shots.Count; i++)
            {
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shots[i].Img));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir!, $"{tag}_{i:000}_{shots[i].Ms:00000}.png"));
                enc.Save(f);
            }
            return (frames * 1000.0 / sw.Elapsed.TotalMilliseconds, worst, shots.Count);
        }
        Wait(1500);
        var school = Run(dir != null ? 1200 : 3000, dir != null ? "okul" : null, 20);
        Console.WriteLine($"Okul seçimi (canlı han): {school.Fps:F0} kare/sn, en uzun kare {school.Worst:F0} ms");
        if (FindAll<Button>(view, b => (b.Tag as string)?.StartsWith("han-school:") == true && b.IsEnabled).FirstOrDefault() is { } pick)
            Raise(pick, ButtonBase.ClickEvent);
        var fly = Run(3300, dir != null ? "inis" : null, 2);
        Console.WriteLine($"Masaya iniş: {fly.Fps:F0} kare/sn, en uzun kare {fly.Worst:F0} ms{(dir != null ? $", {fly.Saved} kare -> {dir}" : "")}");
        var prep = Run(2000, dir != null ? "hazirlik" : null, 30);
        Console.WriteLine($"Ardından hazırlık: {prep.Fps:F0} kare/sn, en uzun kare {prep.Worst:F0} ms");
        win.Close();
        return 0;
    }

    /// <summary>
    /// Han Ligi kare hızı ölçümü: gerçek bir pencerede hazırlık (süre akarken) ve savaş (efektler oynarken) sırasında saniyedeki
    /// kare sayısını ve UI iş parçacığındaki en uzun kareyi yazar. Hedef: ikisinde de ~60 kare/sn.
    /// </summary>
    public static int RunHanPerf()
    {
        if (Environment.GetEnvironmentVariable("HAN_TAVERN") is { } tav) return RunTavernPerf(tav);
        var rng = new Random(9);
        var s = new HanSession(424242, "Ölçüm") { Timed = false };
        s.ChooseSchool(s.Player.SchoolOptions[0]);
        for (var i = 0; i < 40 && s.M.Round < 7 && s.Player.Alive && !s.Over; i++) s.AutoRound(rng);
        if (s.Player.RewardOptions.Length > 0) s.Act(HanActKind.Reward, 0);
        if (s.Player.PotionOptions.Length > 0) s.Act(HanActKind.Potion, 0);
        // HAN_PERF_PLUNDER=1: masadaki ilk üç birim 3★ Yağma kartlarıyla değişir (Menzilli ve Kuşatma); kazanılan satırda Yağma
        // altını ekranda belirir (HAN_CINE_FRAMES ile kare kare görülür)
        if (Environment.GetEnvironmentVariable("HAN_PERF_PLUNDER") == "1")
        {
            var ids = new[] { "clan_brokvar_archer", "war_longship", "clan_brokvar_archer" };
            var field = s.Player.Cards.Where(c => c.Def.IsUnit && c.Zone is Zone.Close or Zone.Ranged or Zone.Siege).Take(ids.Length).ToList();
            for (var i = 0; i < field.Count; i++)
            {
                var def = CardSet.Get(ids[i]);
                s.Player.Cards.Remove(field[i]);
                s.Player.Cards.Add(new OwnedCard { Uid = 990100 + i, Def = def, Star = 3, Zone = def.Row == Row.Siege ? Zone.Siege : Zone.Ranged });
            }
            Console.WriteLine($"Yağma: {field.Count} kart değişti");
        }
        s.Timed = true;
        var view = new HanView(s, () => { }, () => { }, animate: true);
        var win = new Window { Width = 1600, Height = 900, Content = view, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Han Ligi ölçüm", Topmost = true };
        win.Show();
        // imleç pencerenin dışında: ölçüm, farenin o an hangi kartın üstünde durduğuna bağlı olmasın
        if (Environment.GetEnvironmentVariable("HAN_PERF_NOMOUSE") != "1") SetCursorPos(0, 0);
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
        if (card != null) Raise(card, UIElement.MouseLeftButtonUpEvent);   // seçimi bırak
        Wait(300);
        Console.WriteLine($"Ekranı baştan kurma, hazırlığa kadar (en uzun): {HanView.RenderMaxMs:F0} ms");
        HanView.RenderMaxMs = 0;
        // HAN_PERF_CLICK=n: tıklama gecikmesi (alım, seçim, satış n kez): çizim (UI iş parçacığı), ilk kare, değişikliğin göründüğü
        // kare (ikinci kare: önbellekli katmanlar o arada yeniden dokuya çizilir). Yalnız bu ölçülür, sonra çıkılır.
        if (int.TryParse(Environment.GetEnvironmentVariable("HAN_PERF_CLICK"), out var clicks) && clicks > 0)
        {
            var lat = new Dictionary<string, List<(double Ui, double Act, double F1, double F2)>>();
            void Lat(string name, Action act)
            {
                var sw = Stopwatch.StartNew();
                var stamps = new List<double>();
                void OnFrame(object? o, EventArgs e) => stamps.Add(sw.Elapsed.TotalMilliseconds);
                CompositionTarget.Rendering += OnFrame;
                HanView.RenderLastMs = 0;
                act();
                var ui = HanView.RenderLastMs;
                var actMs = sw.Elapsed.TotalMilliseconds;
                Wait(250);
                CompositionTarget.Rendering -= OnFrame;
                if (!lat.TryGetValue(name, out var l)) lat[name] = l = new();
                l.Add((ui, actMs, stamps.ElementAtOrDefault(0), stamps.ElementAtOrDefault(1)));
                Wait(500);   // uçuş bitsin
            }
            HanCardView? Vis(Func<string, bool> pred) => FindAll<HanCardView>(view, c => c.Tag is string t && pred(t) && c.IsVisible).FirstOrDefault();
            var sfxMs = new List<double>();
            for (var i = 0; i < 8; i++)
            {
                var t0 = Stopwatch.StartNew();
                Sfx.Play(i % 2 == 0 ? "han_buy" : "han_sell");
                sfxMs.Add(t0.Elapsed.TotalMilliseconds);
                Wait(200);
            }
            Console.WriteLine($"Ses çalma çağrısı (UI iş parçacığı): {string.Join(", ", sfxMs.Select(x => x.ToString("F1")))} ms");
            for (var i = 0; i < clicks; i++)
            {
                s.Player.Gold = 50;
                if (Vis(t => t.StartsWith("han-shop:")) == null)
                {
                    if (FindAll<Button>(view, x => (x.Tag as string) == "han-shop-toggle").FirstOrDefault() is { } tg) Raise(tg, ButtonBase.ClickEvent);
                    Wait(400);
                }
                if (Vis(t => t.StartsWith("han-shop:")) == null) { Click("han-reroll"); Wait(400); }
                if (Vis(t => t.StartsWith("han-shop:")) is { } buy) Lat("alım", () => Raise(buy, UIElement.MouseLeftButtonUpEvent));
                if (Vis(t => t.StartsWith("han-card:")) is { } sc)
                {
                    Lat("seçim", () => Raise(sc, UIElement.MouseLeftButtonUpEvent));
                    if (FindAll<Button>(view, x => (x.Tag as string) == "han-sell").FirstOrDefault() is { } sell) Lat("satış", () => Raise(sell, ButtonBase.ClickEvent));
                }
            }
            foreach (var (name, l) in lat)
            {
                double Med(Func<(double Ui, double Act, double F1, double F2), double> f) { var v = l.Select(f).OrderBy(x => x).ToList(); return v[v.Count / 2]; }
                Console.WriteLine($"Tıklama ({name}, {l.Count} kez): çizim {Med(x => x.Ui):F0} ms (en çok {l.Max(x => x.Ui):F0}), tıklama işi {Med(x => x.Act):F0} ms, ilk kare {Med(x => x.F1):F0} ms, görünür {Med(x => x.F2):F0} ms (en çok {l.Max(x => x.F2):F0})");
            }
            win.Close();
            return 0;
        }
        // kart uçuşları (alım, masaya taşıma, satış): kare hızı ve isteğe bağlı kare kare görüntü (HAN_FLY_FRAMES=klasör).
        // Bakılacak: ilk karede kart hedefinde belirmemeli (önce kaynakta kopya), varışta tek karelik boşluk ya da çift kart olmamalı.
        // HAN_PERF_NOFLY=1: bu bölüm atlanır (savaş ölçümü eski sürümle aynı masada karşılaştırılsın).
        if (Environment.GetEnvironmentVariable("HAN_PERF_NOFLY") != "1")
        {
            var flyDir = Environment.GetEnvironmentVariable("HAN_FLY_FRAMES");
            if (flyDir is { Length: > 0 }) System.IO.Directory.CreateDirectory(flyDir);
            var origin = view.PointToScreen(new Point(0, 0));
            var dpi = PresentationSource.FromVisual(view)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var area = new Int32Rect((int)origin.X, (int)origin.Y, (int)(view.ActualWidth * dpi), (int)(view.ActualHeight * dpi));
            void Capture(string name, Action act)
            {
                var shots = new List<System.Windows.Media.Imaging.BitmapSource>();
                var frames = 0;
                var sw = Stopwatch.StartNew();
                var last = 0.0;
                var worst = 0.0;
                void OnFrame(object? o, EventArgs e)
                {
                    frames++;
                    var now = sw.Elapsed.TotalMilliseconds;
                    worst = Math.Max(worst, now - last);
                    last = now;
                    if (flyDir is { Length: > 0 } && shots.Count < 72) shots.Add(ScreenGrab(area));
                }
                if (flyDir is { Length: > 0 }) shots.Add(ScreenGrab(area));
                CompositionTarget.Rendering += OnFrame;
                sw.Restart();
                act();
                Wait(900);
                CompositionTarget.Rendering -= OnFrame;
                Console.WriteLine($"Uçuş ({name}): {frames * 1000.0 / sw.Elapsed.TotalMilliseconds:F0} kare/sn, en uzun kare {worst:F0} ms{(flyDir is { Length: > 0 } ? $", {shots.Count} kare -> {flyDir}" : "")}");
                for (var i = 0; i < shots.Count; i++)
                {
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shots[i]));
                    using var f = System.IO.File.Create(System.IO.Path.Combine(flyDir!, $"ucus_{name}_{i:00}.png"));
                    enc.Save(f);
                }
                Wait(300);
            }
            // önce (hazırlık süresi dolmadan; kare kaydı yavaştır) gerçek fareyle: masadaki bir kartın üstüne gelme (kart kalkar) ve depodaki bir kartı satıra sürükleme (kopya yaylı
            // izler, yana yatar; bırakınca yerine süzülür)
            // HAN_PERF_NOMOUSE=1: gerçek fare kullanılmaz (üstüne gelme ve sürükleme atlanır; masada başka bir pencere açıkken)
            var noMouse = Environment.GetEnvironmentVariable("HAN_PERF_NOMOUSE") == "1";
            Point Center(FrameworkElement el) => el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            var boardCard = noMouse ? null : FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true && c.TransformToAncestor(view).Transform(new Point(0, 0)).Y < view.ActualHeight * 0.75).FirstOrDefault();
            if (boardCard != null)
            {
                var c = Center(boardCard);
                Capture("ustune", () => SetCursorPos((int)c.X, (int)c.Y));
                SetCursorPos(0, 0);
                Wait(300);
            }
            var dragCard = noMouse ? null : FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true && c.TransformToAncestor(view).Transform(new Point(0, 0)).Y > view.ActualHeight * 0.8).FirstOrDefault();
            if (dragCard != null)
            {
                var a = Center(dragCard);
                SetCursorPos((int)a.X, (int)a.Y);
                Wait(200);
                Capture("surukle", () =>
                {
                    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
                    Wait(60);
                    // hedef: masanın ortası (orta satır), 8 adımda; sonra bırak
                    var b = view.PointToScreen(new Point(view.ActualWidth * 0.52, view.ActualHeight * 0.55));
                    for (var i = 1; i <= 8; i++)
                    {
                        SetCursorPos((int)(a.X + (b.X - a.X) * i / 8), (int)(a.Y + (b.Y - a.Y) * i / 8));
                        Wait(18);
                    }
                    Wait(60);
                    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
                });
                SetCursorPos(0, 0);
            }
            else if (!noMouse) Console.WriteLine("Sürükleme denenmedi: depoda kart yok (" + string.Join(", ", FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true).Select(c => $"{c.Tag}@{c.TransformToAncestor(view).Transform(new Point(0, 0)).Y:F0}")) + $" / {view.ActualHeight:F0})");
            HanCardView? Tagged(Func<string, bool> pred) => FindAll<HanCardView>(view, c => c.Tag is string t && pred(t) && c.IsVisible).FirstOrDefault();
            // alım: tezgâhı aç, alınabilir ilk kart
            if (FindAll<Button>(view, x => (x.Tag as string) == "han-shop-toggle").FirstOrDefault() is { } tg && Tagged(t => t.StartsWith("han-shop:")) == null) { Raise(tg, ButtonBase.ClickEvent); Wait(400); }
            if (Tagged(t => t.StartsWith("han-shop:")) is { } buy) Capture("alim", () => Raise(buy, UIElement.MouseLeftButtonUpEvent));
            // taşıma: depodaki bir kartı seç, parlayan satıra koy
            var benchCard = FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true && c.TransformToAncestor(view).Transform(new Point(0, 0)).Y > view.ActualHeight * 0.8).FirstOrDefault();
            if (benchCard != null)
            {
                Raise(benchCard, UIElement.MouseLeftButtonUpEvent);
                Wait(300);
                var zone = FindAll<Button>(view, x => (x.Tag as string)?.StartsWith("han-move:") == true && x.IsEnabled && (x.Tag as string) != "han-move:Bench" && (x.Tag as string) != "han-move:Ambush").FirstOrDefault();
                if (zone != null) Capture("tasima", () => Raise(zone, ButtonBase.ClickEvent));
            }
            // satış: bir kart seç, Sat
            if (FindAll<HanCardView>(view, c => (c.Tag as string)?.StartsWith("han-card:") == true).FirstOrDefault() is { } sc)
            {
                Raise(sc, UIElement.MouseLeftButtonUpEvent);
                Wait(300);
                if (FindAll<Button>(view, x => (x.Tag as string) == "han-sell").FirstOrDefault() is { } sell) Capture("satis", () => Raise(sell, ButtonBase.ClickEvent));
            }
            Console.WriteLine($"Ekranı baştan kurma, hazırlıkta (en uzun): {HanView.RenderMaxMs:F0} ms");
            HanView.RenderMaxMs = 0;
        }
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
        // HAN_CINE_FRAMES=klasör: savaşın bütün pencereleri (seçimler, açılış, müdahaleler, puanlama, çarpışma, sonuç) ve hasar
        // anı gerçek ekrandan kare kare kaydedilir (her 3. kare; arka planda PNG'ye yazılır), sonra çıkılır.
        if (Environment.GetEnvironmentVariable("HAN_CINE_FRAMES") is { Length: > 0 } cdir)
        {
            System.IO.Directory.CreateDirectory(cdir);
            var o2 = view.PointToScreen(new Point(0, 0));
            var dpi2 = PresentationSource.FromVisual(view)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var area2 = new Int32Rect((int)o2.X, (int)o2.Y, (int)(view.ActualWidth * dpi2), (int)(view.ActualHeight * dpi2));
            var saving = new List<System.Threading.Tasks.Task>();
            var clock = Stopwatch.StartNew();
            var frame = 0;
            var saved = 0;
            // HAN_CINE_FROM=ms: bundan önceki kareler kaydedilmez (ör. yalnız savaş sonu ve hasar)
            var from = int.TryParse(Environment.GetEnvironmentVariable("HAN_CINE_FROM"), out var cf) ? cf : 0;
            void Grab(object? o, EventArgs e)
            {
                if (frame++ % 3 != 0 || saved >= 900 || clock.Elapsed.TotalMilliseconds < from) return;
                var shot = ScreenGrab(area2);
                shot.Freeze();
                var name = System.IO.Path.Combine(cdir, $"cine_{saved++:000}_{clock.Elapsed.TotalMilliseconds:00000}.png");
                saving.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
                    using var f = System.IO.File.Create(name);
                    enc.Save(f);
                }));
            }
            Click("han-act-go");
            Wait(2500);
            CompositionTarget.Rendering += Grab;
            for (var w = 0; w < 3 && s.MyBattle is { Done: false }; w++)
            {
                Console.WriteLine($"pencere {w + 1}: {clock.Elapsed.TotalMilliseconds:F0} ms");
                Click(w == 0 ? "han-sign:Igni" : "han-sign:Aard");
                Click("han-go");
                Wait(10600);
            }
            Wait(4200);
            CompositionTarget.Rendering -= Grab;
            System.Threading.Tasks.Task.WaitAll(saving.ToArray());
            Console.WriteLine($"Savaş sinematiği: {saved} kare -> {cdir}");
            win.Close();
            return 0;
        }
        Click("han-act-go");
        Wait(2500);
        Click("han-sign:Igni");
        Click("han-go");
        FxEngine.StatFrames = 0; FxEngine.StatMs = 0; FxEngine.StatMaxMs = 0;
        var battle = Measure(3000);
        Console.WriteLine($"Savaş (işaret efektleri): {battle.Fps:F0} kare/sn, en uzun kare {battle.Worst:F0} ms; motor kare başı {FxEngine.StatMs / Math.Max(1, FxEngine.StatFrames):F1} ms (en çok {FxEngine.StatMaxMs:F1})");
        // pencerenin geri kalanı: puanlama (sayı jetonları, yüzdeler, çarpışma) ve sonuç
        FxEngine.StatFrames = 0; FxEngine.StatMs = 0; FxEngine.StatMaxMs = 0;
        var scoring = Measure(5500);
        Console.WriteLine($"Savaş (puanlama ve sonuç): {scoring.Fps:F0} kare/sn, en uzun kare {scoring.Worst:F0} ms; motor kare başı {FxEngine.StatMs / Math.Max(1, FxEngine.StatFrames):F1} ms (en çok {FxEngine.StatMaxMs:F1})");
        for (var i = 0; view.Busy && i < 100; i++) Wait(100);   // pencere bitsin: sonraki seçim ancak o zaman yapılır
        Wait(300);
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
