namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Müzik denetimi (gerçek parçalarla, ses kısık): havuz seçimi (ana menü / Gwent / Han Ligi / Kaer Morhen / Collect 'Em All), ekran → havuz eşlemesi,
    /// ileri/geri, parça seçme, karışık/sıralı sıra, başlıklar ve değişim olayı. <c>--selftest-music</c>
    /// </summary>
    public static int RunMusicCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] " + what); } }
        if (Music.Tracks.Count == 0) { Console.WriteLine("Müzik dosyası yok: sınama atlandı."); return 0; }
        Settings.MusicVolume = 0;
        Music.Enabled = true;
        var events = 0;
        Music.TrackChanged += () => events++;

        // başlıklar okunur, havuzlar dolu
        for (var i = 0; i < Music.Tracks.Count; i++)
            Check(Music.TitleOf(i).Length > 3 && !Music.TitleOf(i).Contains('_'), $"başlık okunur değil: {Music.Tracks[i]} → {Music.TitleOf(i)}");
        var report = new List<string>();
        foreach (var ctx in Enum.GetValues<MusicContext>())
        {
            var pool = Music.PoolIndices(ctx);
            report.Add($"{ctx}={pool.Count}");
            Check(pool.Count > 0, $"{ctx} havuzu boş");
        }
        // ayrı havuzlar birbirine karışmaz (Gwent dışındakiler kendi klasöründen)
        foreach (var ctx in new[] { MusicContext.Menu, MusicContext.Han, MusicContext.KaerMorhen, MusicContext.Career })
        {
            var pool = Music.PoolIndices(ctx);
            if (pool.Count > 0 && pool.All(i => Music.PoolOf(Music.Tracks[i]) == ctx))
                Check(!Music.PoolIndices(MusicContext.Gwent).Intersect(pool).Any(), $"{ctx} parçaları Gwent havuzuna sızdı");
        }

        // ekran → havuz eşlemesi
        Check(MainWindow.MusicContextFor(null, MusicContext.Han) == null, "boş ekran havuz değiştirmemeli");
        Check(MainWindow.MusicContextFor(new System.Windows.Controls.Grid(), MusicContext.Han) == null, "bilinmeyen ekran havuz değiştirmemeli");

        // başlangıç: ana menü havuzundan
        Music.Start();
        Check(Music.Context == MusicContext.Menu, "başlangıç havuzu ana menü değil");
        Check(Music.Current >= 0 && !string.IsNullOrEmpty(Music.CurrentTitle), "başlangıçta çalan parça yok");
        Check(events >= 1, "başlangıçta değişim olayı gelmedi");
        Check(Music.PoolIndices(MusicContext.Menu).Contains(Music.Current), "ana menüde menü havuzundan dışarıda parça çalıyor");

        // havuz geçişleri: her havuzda çalan parça o havuzdan, aynı havuza geçiş parçayı kesmez
        foreach (var ctx in new[] { MusicContext.Gwent, MusicContext.Han, MusicContext.KaerMorhen, MusicContext.Career, MusicContext.Menu })
        {
            var before = events;
            Music.SetContext(ctx);
            Check(Music.Context == ctx, $"{ctx} havuzuna geçilmedi");
            Check(Music.PoolIndices(ctx).Contains(Music.Current), $"{ctx} havuzunda başka havuzdan parça çalıyor ({Music.CurrentTitle})");
            Check(events > before, $"{ctx} geçişinde yeni parça başlamadı");
            var cur = Music.Current;
            var ev = events;
            Music.SetContext(ctx);
            Check(Music.Current == cur && events == ev, $"{ctx} içinde tekrar geçiş parçayı kesti");
        }

        // Gwent havuzunda gezinme: sıralı ve karışık
        Music.SetContext(MusicContext.Gwent);
        var gw = Music.PoolIndices(MusicContext.Gwent);
        var n = gw.Count;
        Music.SetShuffle(false);
        Music.PlayTrack(gw[2 % n]);
        Check(Music.Current == gw[2 % n], "PlayTrack seçilen parçayı çalmadı");
        var start = Music.Current;
        Music.Next();
        Check(Music.Current == gw[(2 % n + 1) % n], $"sıralı modda Next sıradakine geçmedi ({start} → {Music.Current})");
        Music.Previous();
        Check(Music.Current == start, "Previous geri dönmedi");
        var seen = new HashSet<int> { Music.Current };
        for (var i = 1; i < n; i++) { Music.Next(); seen.Add(Music.Current); }
        Check(seen.Count == n, "sıralı turda Gwent havuzunun bütün parçaları çalınmadı");
        Check(seen.All(gw.Contains), "sıralı tur başka havuza taştı");

        Music.SetShuffle(true);
        var run = new List<int> { Music.Current };
        for (var i = 1; i < n * 2; i++) { Music.Next(); run.Add(Music.Current); }
        Check(run.Zip(run.Skip(1)).All(p => p.First != p.Second) || n == 1, "karışık modda aynı parça üst üste çalındı");
        Check(run.Take(n).Distinct().Count() == n, "karışık turda parçalar tekrarlandı ya da eksik kaldı");
        Check(run.All(gw.Contains), "karışık tur başka havuza taştı");

        // ayarlar listesinden başka havuzun parçası seçilince çalma o havuza geçer
        var han = Music.PoolIndices(MusicContext.Han);
        if (han.Count > 0 && Music.PoolOf(Music.Tracks[han[0]]) == MusicContext.Han)
        {
            Music.PlayTrack(han[0]);
            Check(Music.Context == MusicContext.Han && Music.Current == han[0], "başka havuzdan seçilen parça çalmadı");
        }
        Music.Stop();

        // gerçek pencerede ekran geçişleri havuzu değiştirir (müzik başlatılmaz; yalnız bağlam izlenir)
        var win = new MainWindow();
        void Go(string method, MusicContext expect)
        {
            try { typeof(MainWindow).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(win, null); }
            catch (Exception ex) { Check(false, $"{method} açılamadı: {(ex.InnerException ?? ex).Message}"); return; }
            Check(Music.Context == expect, $"{method} → {Music.Context}, beklenen {expect}");
        }
        Go("ShowMenu", MusicContext.Menu);
        Go("ShowPlayHub", MusicContext.Menu);
        Go("ShowQuickGwent", MusicContext.Menu);
        Go("ShowOnlineMenu", MusicContext.Menu);
        Go("ShowHan", MusicContext.Han);
        Go("ShowMenu", MusicContext.Menu);
        Go("ShowKeep", MusicContext.KaerMorhen);
        Go("ShowMenu", MusicContext.Menu);
        win.Close();
        Console.WriteLine($"Müzik: {Music.Tracks.Count} parça ({string.Join(", ", report)}), {events} değişim olayı, {failures} hata.");
        return failures;
    }
}
