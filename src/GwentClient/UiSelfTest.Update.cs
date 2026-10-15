using System.IO;
using System.IO.Compression;
using System.Net;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Oyun içi güncelleme (gerçek oyun klasörüne dokunmaz): GitHub yanıtından paket adresi okunur; yerel HTTP sunucusundan sahte paket indirilir,
    /// geçici "oyun klasörüne" uygulanır (çalışan exe gibi açık tutulan dosya yeniden adlandırılarak değişir, aynı dosyalara dokunulmaz);
    /// taşınamayan bir dosyada her şey geri alınır. <c>--selftest-update</c>
    /// </summary>
    public static int RunUpdateSelfTest()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] güncelleme: " + what); } }
        var root = Path.Combine(Path.GetTempPath(), "gwent-selftest-update-" + Guid.NewGuid().ToString("N"));
        HttpListener? http = null;
        try
        {
            // 1) GitHub yanıtı: paket adresi ve boyutu
            var json = "{\"tag_name\":\"v9.0\",\"html_url\":\"page\",\"assets\":[{\"name\":\"other.zip\",\"browser_download_url\":\"x\",\"size\":1},"
                       + "{\"name\":\"" + UpdateCheck.ZipName + "\",\"browser_download_url\":\"http://h/z.zip\",\"size\":1234}]}";
            var info = UpdateCheck.Newer(json, new Version(0, 7, 0));
            Check(info is { ZipUrl: "http://h/z.zip", ZipSize: 1234, Url: "page" }, "paket adresi okunmadı");

            // 2) Sahte oyun klasörü ve yeni paket
            var app = Path.Combine(root, "app");
            var pkg = Path.Combine(root, "pkg", "Gwent");
            Directory.CreateDirectory(Path.Combine(app, "assets", "ui"));
            Directory.CreateDirectory(Path.Combine(pkg, "assets", "ui"));
            File.WriteAllText(Path.Combine(app, "Gwent.exe"), "eski exe");
            File.WriteAllText(Path.Combine(app, "assets", "ui", "same.png"), "aynı");
            File.WriteAllText(Path.Combine(app, "assets", "ui", "changed.png"), "eski resim");
            File.WriteAllText(Path.Combine(app, "keep.txt"), "kullanıcı dosyası");
            File.WriteAllText(Path.Combine(app, "assets", "ui", "pano.png"), "eski png");   // pakette pano.jpg: eski png kenara çekilmeli
            File.WriteAllText(Path.Combine(pkg, "Gwent.exe"), "yeni exe");
            File.WriteAllText(Path.Combine(pkg, "assets", "ui", "same.png"), "aynı");
            File.WriteAllText(Path.Combine(pkg, "assets", "ui", "changed.png"), "yeni resim");
            File.WriteAllText(Path.Combine(pkg, "assets", "ui", "added.png"), "eklenen");
            File.WriteAllText(Path.Combine(pkg, "assets", "ui", "pano.jpg"), "yeni jpg");
            var zip = Path.Combine(root, UpdateCheck.ZipName);
            ZipFile.CreateFromDirectory(Path.Combine(root, "pkg"), zip);   // tek üst klasörlü zip (Gwent/...)

            // Yerel HTTP sunucusu paketi verir
            var port = new Random().Next(42000, 48000);
            http = new HttpListener();
            http.Prefixes.Add($"http://localhost:{port}/");
            http.Start();
            var bytes = File.ReadAllBytes(zip);
            _ = Task.Run(async () =>
            {
                var ctx = await http.GetContextAsync();
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            });

            // Çalışan exe gibi: açık ama yeniden adlandırılabilir
            using (var running = new FileStream(Path.Combine(app, "Gwent.exe"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var phases = new List<string>();
                var rel = new ReleaseInfo("v9.0", new Version(9, 0, 0), "page", $"http://localhost:{port}/z.zip", bytes.Length);
                var exe = Await(() => Updater.DownloadAndApplyAsync(rel, new SyncProgress(p => phases.Add(p.Phase)), CancellationToken.None, app));
                Check(exe == Path.Combine(app, "Gwent.exe"), "yeniden başlatılacak exe yolu");
                Check(phases.Contains("download") && phases.Contains("apply"), "ilerleme bildirilmedi");
            }
            Check(File.ReadAllText(Path.Combine(app, "Gwent.exe")) == "yeni exe", "exe yenilenmedi");
            Check(File.ReadAllText(Path.Combine(app, "Gwent.exe.old")) == "eski exe", "eski exe .old olarak kenara alınmadı");
            Check(File.ReadAllText(Path.Combine(app, "assets", "ui", "changed.png")) == "yeni resim", "değişen dosya yenilenmedi");
            Check(File.Exists(Path.Combine(app, "assets", "ui", "added.png")), "yeni dosya eklenmedi");
            Check(!File.Exists(Path.Combine(app, "assets", "ui", "same.png.old")), "aynı dosyaya dokunuldu");
            Check(File.Exists(Path.Combine(app, "keep.txt")), "kullanıcı dosyası silindi");
            Check(!File.Exists(Path.Combine(app, "assets", "ui", "pano.png")) && File.Exists(Path.Combine(app, "assets", "ui", "pano.png.old")), "yerini jpg'e bırakan png kenara çekilmedi");

            // Parça seçimi: yalnız değişen parça; eski kurulumda (package.json yok) tam paket
            var all = new[] { UpdateCheck.ZipName, Updater.CoreZip, Updater.DataZip, Updater.ManifestName };
            var local = "{\"version\":\"0.7.1\",\"core\":\"a\",\"data\":\"d\"}";
            Check(Updater.ChooseParts(local, "{\"core\":\"b\",\"data\":\"d\"}", all).SequenceEqual(new[] { Updater.CoreZip }), "yalnız uygulama parçası seçilmedi");
            Check(Updater.ChooseParts(local, "{\"core\":\"b\",\"data\":\"e\"}", all).SequenceEqual(new[] { Updater.CoreZip, Updater.DataZip }), "veri değişince iki parça seçilmedi");
            Check(Updater.ChooseParts(null, "{\"core\":\"b\",\"data\":\"e\"}", all).SequenceEqual(new[] { UpdateCheck.ZipName }), "eski kurulumda tam paket seçilmedi");
            Check(Updater.ChooseParts(local, null, all).SequenceEqual(new[] { UpdateCheck.ZipName }), "uzak bildirim yoksa tam paket seçilmedi");

            // 3) Geri alma: taşınamayan (tamamen kilitli) bir dosya varsa hiçbir şey değişmez
            var app2 = Path.Combine(root, "app2");
            Directory.CreateDirectory(Path.Combine(app2, "assets", "ui"));
            File.WriteAllText(Path.Combine(app2, "Gwent.exe"), "eski exe");
            File.WriteAllText(Path.Combine(app2, "assets", "ui", "changed.png"), "eski resim");
            using (new FileStream(Path.Combine(app2, "assets", "ui", "changed.png"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var threw = false;
                try { Updater.Apply(pkg, app2); } catch (IOException) { threw = true; }
                Check(threw, "kilitli dosyada hata bekleniyordu");
            }
            Check(File.ReadAllText(Path.Combine(app2, "Gwent.exe")) == "eski exe", "geri alma: exe eski hâline dönmedi");
            Check(!File.Exists(Path.Combine(app2, "Gwent.exe.old")), "geri alma: .old kaldı");
            Check(!File.Exists(Path.Combine(app2, "assets", "ui", "added.png")), "geri alma: eklenen dosya kaldı");
            Check(Updater.FindPackageRoot(Path.Combine(root, "pkg")) == pkg, "paket kökü bulunamadı");

            Console.WriteLine($"Güncelleme: {failures} hata.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] güncelleme: " + ex);
        }
        finally
        {
            try { http?.Stop(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>İlerlemeyi hemen (iş parçacığı değiştirmeden) bildirir: sınamada arayüz iş parçacığı pompalanmaz.</summary>
    private sealed class SyncProgress(Action<UpdateProgress> act) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => act(value);
    }
}
