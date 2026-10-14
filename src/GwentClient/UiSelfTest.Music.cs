namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Müzik denetimi (gerçek parçalarla, ses kısık): başlangıç, ileri/geri, parça seçme, karışık/sıralı sıra, başlıklar ve değişim olayı. <c>--selftest-music</c>
    /// </summary>
    public static int RunMusicCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] " + what); } }
        var n = Music.Tracks.Count;
        if (n == 0) { Console.WriteLine("Müzik dosyası yok: sınama atlandı."); return 0; }
        Settings.MusicVolume = 0;
        Music.Enabled = true;
        var events = 0;
        Music.TrackChanged += () => events++;
        Music.Start();
        Check(Music.Current >= 0 && !string.IsNullOrEmpty(Music.CurrentTitle), "başlangıçta çalan parça yok");
        Check(events >= 1, "başlangıçta değişim olayı gelmedi");
        for (var i = 0; i < n; i++) Check(Music.TitleOf(i).Length > 3 && !Music.TitleOf(i).Contains('_'), $"başlık okunur değil: {Music.Tracks[i]} → {Music.TitleOf(i)}");

        Music.SetShuffle(false);
        Music.PlayTrack(2 % n);
        Check(Music.Current == 2 % n, "PlayTrack seçilen parçayı çalmadı");
        var before = Music.Current;
        Music.Next();
        Check(Music.Current == (before + 1) % n, $"sıralı modda Next sıradakine geçmedi ({before} → {Music.Current})");
        Music.Previous();
        Check(Music.Current == before, "Previous geri dönmedi");

        // sıralı modda bir tur: her parça bir kez
        var seen = new HashSet<int> { Music.Current };
        for (var i = 1; i < n; i++) { Music.Next(); seen.Add(Music.Current); }
        Check(seen.Count == n, "sıralı turda bütün parçalar çalınmadı");

        // karışık modda bir tur: yine her parça bir kez, aynı parça üst üste gelmez
        Music.SetShuffle(true);
        var run = new List<int> { Music.Current };
        for (var i = 1; i < n * 2; i++) { Music.Next(); run.Add(Music.Current); }
        Check(run.Zip(run.Skip(1)).All(p => p.First != p.Second) || n == 1, "karışık modda aynı parça üst üste çalındı");
        Check(run.Take(n).Distinct().Count() == n, "karışık turda parçalar tekrarlandı ya da eksik kaldı");
        Music.Stop();
        Console.WriteLine($"Müzik: {n} parça, {events} değişim olayı, {failures} hata.");
        return failures;
    }
}
