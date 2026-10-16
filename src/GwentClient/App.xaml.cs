using System.Windows;

namespace GwentClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Güncelleme sınaması (gizli): gerçek süreçle indir → uygula → yeniden başlat; yeniden açılan kopya sürümünü dosyaya yazıp kapanır
        if (Array.IndexOf(e.Args, "--updated-probe") is var pi and >= 0 && pi + 1 < e.Args.Length)
        {
            System.IO.File.WriteAllText(e.Args[pi + 1], "OK " + UpdateCheck.CurrentText);
            Shutdown();
            return;
        }
        if (Array.IndexOf(e.Args, "--apply-update") is var ai and >= 0 && ai + 2 < e.Args.Length)
        {
            var probe = e.Args[ai + 2];
            try
            {
                // <adres>: release dosyalarının bulunduğu taban adres (tam paket, parçalar ve package.json)
                var baseUrl = e.Args[ai + 1].TrimEnd('/');
                var names = new[] { UpdateCheck.ZipName, Updater.CoreZip, Updater.DataZip, Updater.ManifestName };
                var rel = new ReleaseInfo("test", new Version(99, 0, 0), baseUrl, baseUrl + "/" + UpdateCheck.ZipName)
                {
                    Files = names.ToDictionary(n => n, n => (baseUrl + "/" + n, 0L))
                };
                var exe = Updater.DownloadAndApplyAsync(rel, new Progress<UpdateProgress>(_ => { }), CancellationToken.None).GetAwaiter().GetResult();
                System.Diagnostics.Process.Start(exe, $"--updated-probe \"{probe}\"");
            }
            catch (Exception ex) { System.IO.File.WriteAllText(probe, "FAIL " + ex.Message); }
            Shutdown();
            return;
        }
        // Beklenmeyen hata: %APPDATA%\Gwent\crash.log dosyasına yazılır; oyun kapanmaz (arayüz hatası tek tıklamayı yutar).
        DispatcherUnhandledException += (_, ex) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.Exception}" + Environment.NewLine + Environment.NewLine);
                ex.Handled = true;
            }
            catch { }
        };
        Sfx.LoadSettings();
        Settings.NoSave = e.Args.Any(a => a.StartsWith("--shot") || a.StartsWith("--selftest") || a == "--perf");
        Ornate.Animate = !e.Args.Any(a => a.StartsWith("--shot") || a.StartsWith("--selftest") || a == "--perf");   // sınamalar animasyonsuz
        if (e.Args.Contains("--noassets")) Assets.Override = ""; // varlıksız (yer tutucu) modu denemek için
        // --shot <klasör>: pencere açmadan ekranları PNG'ye çizer (görsel doğrulama için)
        var t = Array.IndexOf(e.Args, "--selftest");
        if (t >= 0)
        {
            var games = t + 1 < e.Args.Length && int.TryParse(e.Args[t + 1], out var n) ? n : 20;
            Sfx.Enabled = false;
            var code = UiSelfTest.Run(games);
            Shutdown(code);
            return;
        }
        if (e.Args.Contains("--selftest-lobby"))
        {
            Sfx.Enabled = false;
            var li = Array.IndexOf(e.Args, "--selftest-lobby");
            var rounds = li + 1 < e.Args.Length && int.TryParse(e.Args[li + 1], out var rn) ? rn : 3;
            Shutdown(UiSelfTest.RunOnlineLobby(rounds) == 0 ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--selftest-update"))
        {
            Shutdown(UiSelfTest.RunUpdateSelfTest());
            return;
        }
        if (e.Args.Contains("--selftest-online-curses"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunOnlineCursesCheck());
            return;
        }
        if (e.Args.Contains("--selftest-online-draft"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunOnlineDraftCheck());
            return;
        }
        if (e.Args.Contains("--selftest-draft"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunDraftCheck());
            return;
        }
        if (e.Args.Contains("--selftest-career"))
        {
            Sfx.Enabled = false;
            var ci = Array.IndexOf(e.Args, "--selftest-career");
            var cg = ci + 1 < e.Args.Length && int.TryParse(e.Args[ci + 1], out var cn) ? cn : 4;
            Shutdown(UiSelfTest.RunCareerCheck(cg));
            return;
        }
        if (e.Args.Contains("--selftest-music"))
        {
            Shutdown(UiSelfTest.RunMusicCheck());
            return;
        }
        if (e.Args.Contains("--selftest-tournament-resume"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunTournamentResumeCheck());
            return;
        }
        if (e.Args.Contains("--selftest-tournament-rules"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunTournamentRulesCheck());
            return;
        }
        if (e.Args.Contains("--selftest-spectate"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunSpectateCheck());
            return;
        }
        if (e.Args.Contains("--selftest-tournament-ui"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunTournamentUiCheck());
            return;
        }
        if (e.Args.Contains("--selftest-tournament"))
        {
            Sfx.Enabled = false;
            var ti = Array.IndexOf(e.Args, "--selftest-tournament");
            var tr = ti + 1 < e.Args.Length && int.TryParse(e.Args[ti + 1], out var trn) ? trn : 2;
            Shutdown(UiSelfTest.RunTournamentCheck(tr) == 0 ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--selftest-resume"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunResumeCheck());
            return;
        }
        if (e.Args.Contains("--selftest-tunnel-probe"))
        {
            Shutdown(UiSelfTest.RunTunnelProbe());
            return;
        }
        if (e.Args.Contains("--selftest-tunnel"))
        {
            Sfx.Enabled = false;
            Shutdown(UiSelfTest.RunTunnelCheck());
            return;
        }
        if (e.Args.Contains("--net-info"))
        {
            // Teşhis: yerel adresler, UPnP ve cloudflared durumu (hiçbir ayarı değiştirmez)
            Console.WriteLine("Yerel ağ adresleri: " + string.Join(", ", OnlineController.LocalIps()));
            var probe = System.Threading.Tasks.Task.Run(() => GwentHost.Upnp.ProbeAsync()).GetAwaiter().GetResult();
            Console.WriteLine(probe.Found
                ? $"UPnP: modem bulundu, dış adres {probe.ExternalIp}{(probe.BehindCgnat ? " (özel/CGNAT adres: internetten ulaşılamaz!)" : " (genel adres: internetten ulaşılabilir)")}"
                : "UPnP: bulunamadı — " + probe.Detail);
            Console.WriteLine("cloudflared: " + (GwentHost.Cloudflared.FindInstalled() ?? "kurulu değil"));
            Shutdown(0);
            return;
        }
        var to = Array.IndexOf(e.Args, "--selftest-online");
        if (to >= 0)
        {
            var games = to + 1 < e.Args.Length && int.TryParse(e.Args[to + 1], out var n) ? n : 10;
            Sfx.Enabled = false;
            var viaNet = e.Args.Contains("--net");
            Shutdown(UiSelfTest.RunOnline(games, viaNet) == 0 ? 0 : 1);
            return;
        }
        var i = Array.IndexOf(e.Args, "--shot");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            Sfx.Enabled = false;
            Screenshots.Run(e.Args[i + 1]);
            Shutdown();
            return;
        }
        if (e.Args.Contains("--perf")) { PerfProbe.Run(); return; }
        // Her maç %AppData%\Gwent\last_match.json içine kaydedilir (hata bildirirken bu dosyayı göster)
        GameSession.ReplayPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "last_match.json");
        TournamentEconomy.ApplyFloor();
        Stats.Enabled = true;
        ResumeStore.Enabled = true;
        Updater.CleanupOld();  // önceki güncellemeden kalan .old dosyaları
        UpdateCheck.Start();   // GitHub'da yeni sürüm var mı (arka planda; ana menü bildirir)
        new MainWindow().Show();
        Music.Start();   // müzik gerçek uygulama açılır açılmaz başlar (ekran görüntüsü/sınama süreçleri müzik çalmaz)
    }
}
