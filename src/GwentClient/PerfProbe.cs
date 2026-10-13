using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GwentCore;

namespace GwentClient;

/// <summary>
/// <c>--perf</c>: gerçek pencerede her efekti ayrı ayrı çalıştırıp kare süresini ölçer (kasmayı tahminle değil ölçümle bulmak için).
/// Sonuç konsola ve <c>perf.txt</c> dosyasına yazılır. Pencere ~40 saniye açık kalır; ona dokunma.
/// </summary>
public static class PerfProbe
{
    private static readonly List<double> Dts = new();
    private static long _last = -1;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StringBuilder Report = new();

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static MethodInfo M(string name) => typeof(GwintView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static async void Run()
    {
        var win = new MainWindow();
        win.Show();
        await Task.Delay(800);
        CompositionTarget.Rendering += (_, _) =>
        {
            var now = Clock.ElapsedTicks;
            if (_last >= 0) Dts.Add((now - _last) * 1000.0 / Stopwatch.Frequency);
            _last = now;
        };
        Line($"Render katmanı (tier): {RenderCapability.Tier >> 16}  (0 = yazılım, 1-2 = GPU) | pencere {win.ActualWidth:0}x{win.ActualHeight:0}");

        Sfx.Enabled = !Environment.GetCommandLineArgs().Contains("--perf-mute");
        Line($"Ses: {(Sfx.Enabled ? "açık" : "kapalı")}");
        var cfg = new MatchConfig("northern_realms", "foltest_the_siegemaster", "monsters", "eredin_commander_of_the_red_riders", false, 11);

        GwintView Fresh(bool skipOpening)
        {
            var s = new GameSession(cfg);
            if (skipOpening)
            {
                s.AiAutoMulligan();
                s.Apply(new Mulligan(0, new List<int>()));
            }
            var v = new GwintView(s, () => { }, _ => { }, animate: true);
            win.Host.Content = v;
            return v;
        }

        async Task Measure(string name, GwintView v, Action<GwintView>? act, int ms)
        {
            await Task.Delay(500);
            Dts.Clear();                      // _last korunur: act() içinde olan takılma ilk karenin süresine yansır
            act?.Invoke(v);
            await Task.Delay(ms);
            if (Dts.Count < 4) { Line($"{name,-28} ölçüm yok"); return; }
            var first = Dts[0];                // efekt başlatılırken UI iş parçacığının ne kadar takıldığı
            var d = Dts.Skip(1).OrderBy(x => x).ToList();
            var avg = d.Average();
            var p95 = d[(int)(d.Count * 0.95)];
            var slow = d.Count(x => x > 33.4);
            Line($"{name,-28} İLK KARE {first,6:0.0} ms | ort {1000 / avg,4:0} fps | p95 {p95,5:0.0} ms | en kötü {d[^1],6:0.0} ms | >33ms: {slow,3}/{d.Count}");
        }

        Line("");
        var idle = Fresh(true);
        await Measure("boşta (oyun alanı)", idle, null, 2500);

        // Maç başı: yazı-tura + dağıtım (tüm sekans)
        // Soğuk başlangıç: oyun alanı kurulurken ve ilk çizilirken (gerçekte VS ekranından hemen sonra) olan takılma
        GwintView? cold = null;
        await Measure("MAÇ BAŞI soğuk kurulum+6sn", idle, _ => { cold = Fresh(false); }, 6000);
        var opening = Fresh(false);
        await Measure("maç başı sekansı (sıcak)", opening, null, 6000);

        var v1 = Fresh(true);
        await Measure("yalnız yazı-tura", v1, v => M("PlayCoin").Invoke(v, new object[] { 0 }), 2400);

        // Kart oynama: birim kart
        var v2 = Fresh(true);
        await Measure("birim kartı oyna (iniş)", v2, v =>
        {
            var g = v2Session(v);
            var card = g.Game.Players[0].Hand.First(c => c.Def.IsUnit && !c.Def.IsAgile && !c.Def.Hero && !c.Def.Has("spy"));
            g.Apply(new PlayCard(0, card.Uid, null, null));
            v.Refresh();
        }, 2200);

        var v3 = Fresh(true);
        await Measure("efekt: kahraman şimşeği", v3, v => M("Landing").Invoke(v, new object[] { CardCatalog.Default.Get("geralt_of_rivia"), new Point(900, 520), 0 }), 1800);

        var v4 = Fresh(true);
        await Measure("efekt: toz patlaması", v4, v => M("Landing").Invoke(v, new object[] { CardCatalog.Default.Get("blue_stripes_commando"), new Point(900, 520), 0 }), 1800);

        var v5 = Fresh(true);
        await Measure("efekt: ateş (Yak)", v5, v => Fx.PlayFrames(Canvas_Fx(v), "fire", new Point(900, 520), 150, 190, 30), 1800);

        var v6 = Fresh(true);
        await Measure("tahta sarsıntısı", v6, v => Fx.Shake(Board(v), 3.5, 230), 800);

        var v7 = Fresh(true);
        await Measure("kart sinematiği (Boynuz vb.)", v7, v => M("StartCinema").Invoke(v, new object[] { 0, CardCatalog.Default.Get("impenetrable_fog"), "Sen oynadın", new List<GameEvent>() }), 2200);

        foreach (var type in new[] { "frost", "fog", "rain", "storm" })
        {
            var vw = Fresh(true);
            var t = type;
            await Measure($"hava: {t} (4 sıra, giriş+boşta)", vw, v =>
            {
                var fx = Canvas_Fx(v);
                for (var i = 0; i < 4; i++)
                {
                    var el = WeatherFx.Make(t, 797, 124, true);
                    Canvas.SetLeft(el, 707);
                    Canvas.SetTop(el, 24 + i * 134);
                    fx.Children.Add(el);
                }
            }, 3000);
        }

        // Diğer ekranlar (menü parçacıkları, deste kurucu kartları)
        var menu = new MenuView(_ => { }, (_, _) => { }, animate: true);
        win.Host.Content = menu;
        await Measure("ana menü (kor+panorama)", idle, null, 3500);
        var builder = new DeckBuilderView("northern_realms", false, () => { });
        win.Host.Content = builder;
        await Measure("deste kurucu (boşta)", idle, null, 2500);

        Line("");
        Line("Not: kare süresi ~16 ms = 60 fps; >33 ms kareler gözle kasma olarak görünür.");
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "perf.txt"), Report.ToString()); } catch { }
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "gwent-perf.txt"), Report.ToString()); } catch { }
        Application.Current.Shutdown();
    }

    private static GameSession v2Session(GwintView v) => (GameSession)typeof(GwintView).GetField("_s", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(v)!;
    private static Canvas Canvas_Fx(GwintView v) => (Canvas)typeof(GwintView).GetField("_fx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(v)!;
    private static FrameworkElement Board(GwintView v) => (FrameworkElement)typeof(GwintView).GetField("_board", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(v)!;
}
