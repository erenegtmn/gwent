using System.IO;
using System.Windows.Media;

namespace GwentClient;

/// <summary>Müzik havuzu: ekran türüne göre seçilir (<see cref="Music.SetContext"/>).</summary>
public enum MusicContext
{
    /// <summary>Ana menü ve menü ekranları: Witcher 3 ana müziği (<c>assets/music/menu</c>).</summary>
    Menu,
    /// <summary>Gwent kipleri (Hızlı Gwent, 1v1v1, turnuva, Draft, çevrimiçi): han müzikleri (<c>assets/music/*.mp3</c>).</summary>
    Gwent,
    /// <summary>Han Ligi (<c>assets/music/han</c>).</summary>
    Han,
    /// <summary>Kaer Morhen (<c>assets/music/kaermorhen</c>).</summary>
    KaerMorhen,
    /// <summary>Collect 'Em All kariyeri (<c>assets/music/career</c>).</summary>
    Career
}

/// <summary>
/// Arka plan müziği, ekrana göre havuz seçer (<see cref="MusicContext"/>): ana menüde Witcher 3 ana müziği, Gwent kiplerinde han müzikleri,
/// Han Ligi / Kaer Morhen / Collect 'Em All için kendi klasörleri. Havuz karışık ya da sıralı çalınır; parça atlama, parça seçme ve
/// "şimdi çalıyor" bilgisi (<see cref="TrackChanged"/>) vardır. Dosya yoksa sessiz kalır (havuz boşsa Gwent havuzuna düşer).
/// Açık/kapalı ve ses düzeyi %AppData%\Gwent\settings.json içinde saklanır.
/// </summary>
public static class Music
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Karışık çalma (varsayılan açık). Kapalıysa parçalar dosya sırasıyla çalınır.</summary>
    public static bool Shuffle { get; set; } = true;

    /// <summary>Çalan parça değişince (parça bitti, atlandı ya da seçildi) tetiklenir. Arayüz iş parçacığında.</summary>
    public static event Action? TrackChanged;

    /// <summary>Şu an seçili havuz. <see cref="SetContext"/> ile ekran geçişlerinde değişir.</summary>
    public static MusicContext Context { get; private set; } = MusicContext.Menu;

    private static MediaPlayer? _player;
    private static List<string> _tracks = new();
    private static List<int> _order = new();
    private static int _pos;
    private static int _failures;
    private static bool _started;

    /// <summary>Çalınabilir tüm parçalar, bütün havuzlar (ayarlar ekranındaki liste).</summary>
    public static IReadOnlyList<string> Tracks
    {
        get
        {
            if (_tracks.Count == 0) _tracks = Assets.Music().ToList();
            return _tracks;
        }
    }

    /// <summary>Parçanın havuzu: klasör adından (<c>menu</c>, <c>han</c>, <c>kaermorhen</c>, <c>career</c>); kökteki parçalar Gwent havuzudur.</summary>
    internal static MusicContext PoolOf(string path) => (Path.GetFileName(Path.GetDirectoryName(path)) ?? "").ToLowerInvariant() switch
    {
        "menu" => MusicContext.Menu,
        "han" => MusicContext.Han,
        "kaermorhen" => MusicContext.KaerMorhen,
        "career" => MusicContext.Career,
        _ => MusicContext.Gwent
    };

    /// <summary>Havuzdaki parçaların <see cref="Tracks"/> sıraları. Havuz boşsa (dosyalar çıkarılmamışsa) Gwent havuzuna düşer.</summary>
    public static List<int> PoolIndices(MusicContext ctx)
    {
        var all = Tracks;
        var idx = Enumerable.Range(0, all.Count).Where(i => PoolOf(all[i]) == ctx).ToList();
        return idx.Count > 0 || ctx == MusicContext.Gwent ? idx : PoolIndices(MusicContext.Gwent);
    }

    /// <summary>Parçanın havuz adı (ayarlar listesinde başlığın yanında gösterilir).</summary>
    public static string GroupOf(int index) => index < 0 || index >= Tracks.Count ? "" : PoolOf(Tracks[index]) switch
    {
        MusicContext.Menu => "Ana menü",
        MusicContext.Han => "Han Ligi",
        MusicContext.KaerMorhen => "Kaer Morhen",
        MusicContext.Career => "Collect 'Em All",
        _ => "Gwent"
    };

    /// <summary>Ekran değişince çağrılır: havuz değişirse o havuzdan yeni bir parça başlar, aynıysa çalan parça kesilmez.</summary>
    public static void SetContext(MusicContext ctx)
    {
        if (ctx == Context) return;
        Context = ctx;
        if (!_started) return;
        BuildOrder(null);
        Open();
    }

    /// <summary>Çalan parçanın <see cref="Tracks"/> içindeki sırası; hiçbiri çalmıyorsa -1.</summary>
    public static int Current => _started && _order.Count > 0 ? _order[_pos % _order.Count] : -1;

    public static string? CurrentTitle => Current is var i and >= 0 ? TitleOf(i) : null;

    public static string TitleOf(int index) => index >= 0 && index < Tracks.Count ? TitleOfFile(Tracks[index]) : "";

    /// <summary>Moda özel parçaların okunur adları (dosya adı → başlık).</summary>
    private static readonly Dictionary<string, string> Titles = new()
    {
        ["main_menu"] = "Witcher 3 Ana Müziği",
        ["han_fistfight"] = "Yumruk Dövüşü (Kan ve Şarap)",
        ["han_tavern_brawl"] = "Han Kavgası (Kan ve Şarap)",
        ["han_skellige_games"] = "Skellige Han Oyunları",
        ["han_tournament_day"] = "Turnuva Çayırı, Gündüz",
        ["han_tournament_night"] = "Turnuva Çayırı, Gece",
        ["han_tavern_robbery"] = "Handa Soygun",
        ["km_training"] = "Kaer Morhen: Eğitim",
        ["km_geralt_and_yen"] = "Geralt ve Yennefer",
        ["km_little_ciri"] = "Küçük Ciri",
        ["km_trial_of_the_grasses"] = "Otların Sınavı",
        ["km_percival"] = "Percival'le Keşif",
        ["km_ciri_wakes"] = "Ciri Uyanıyor",
        ["km_teleport"] = "Kaer Morhen'e Dönüş",
        ["km_exploration"] = "Kaer Morhen Keşfi",
        ["road_white_orchard"] = "White Orchard Yolu",
        ["road_prologue_02"] = "Prolog Yolu II",
        ["road_prologue_03"] = "Prolog Yolu III",
        ["road_velen_day"] = "Velen, Gündüz",
        ["road_velen_night"] = "Velen, Gece",
        ["road_velen_04"] = "Velen Yolu I",
        ["road_velen_08"] = "Velen Yolu II",
        ["road_velen_07"] = "Velen Yolu III",
        ["road_assassins_p1"] = "Kralkatilleri I",
        ["road_assassins_p2"] = "Kralkatilleri II",
        ["road_witches_village"] = "Cadılar Köyü",
        ["road_novigrad"] = "Novigrad Yolu",
        ["road_skellige_caves"] = "Skellige Mağaraları",
        ["road_isle_of_mists"] = "Sis Adası",
        ["road_toussaint_day"] = "Toussaint, Gündüz",
        ["road_toussaint_night"] = "Toussaint, Gece",
        ["road_toussaint_25"] = "Toussaint Yolu",
    };

    /// <summary>Dosya adından okunur ad: novigrad_tavern_01 → "Novigrad Hanı I", toussaint_tavern_02 → "Toussaint Hanı (Kan ve Şarap) II".</summary>
    internal static string TitleOfFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (Titles.TryGetValue(name, out var fixedTitle)) return fixedTitle;
        var region = name.StartsWith("novigrad") ? "Novigrad Hanı"
            : name.StartsWith("skellige") ? "Skellige Hanı"
            : name.StartsWith("toussaint_mandragora") ? "Mandragora Hanı (Kan ve Şarap)"
            : name.StartsWith("toussaint_drunken") ? "Sarhoş Usta (Kan ve Şarap)"
            : name.StartsWith("toussaint") ? "Toussaint Hanı (Kan ve Şarap)"
            : name;
        var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var n) && n is >= 1 and <= 10 ? $"{region} {Roman[n - 1]}" : region;
    }

    private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    public static void Start()
    {
        if (_started || !Enabled) return;
        try
        {
            if (Tracks.Count == 0) return;
            BuildOrder(null);
            if (_order.Count == 0) return;
            _player = new MediaPlayer { Volume = Settings.MusicVolume / 100.0 };
            _player.MediaEnded += (_, _) => Advance(1);
            _player.MediaFailed += (_, _) => OnFailed();
            _started = true;
            Open();
        }
        catch { _started = false; }
    }

    /// <summary>Çalma sırasını kurar: seçili havuz, karışık ya da dosya sırası. <paramref name="first"/> doluysa o parça başa alınır.</summary>
    private static void BuildOrder(int? first)
    {
        var idx = PoolIndices(Context);
        if (Shuffle)
        {
            var rng = new Random();
            idx = idx.OrderBy(_ => rng.Next()).ToList();
        }
        if (first is { } f && idx.Remove(f)) idx.Insert(0, f);
        _order = idx;
        _pos = first == null ? 0 : _order.IndexOf(first.Value);
        if (_pos < 0) _pos = 0;
    }

    private static void Open()
    {
        if (_player == null || _order.Count == 0) return;
        _player.Open(new Uri(Tracks[_order[_pos % _order.Count]]));
        if (Enabled) _player.Play();
        TrackChanged?.Invoke();
    }

    private static void OnFailed()
    {
        // Bozuk dosya: sıradakine geç; hepsi bozuksa sonsuz döngüye girmeden dur
        if (++_failures >= Math.Max(1, _order.Count)) { _started = false; return; }
        Advance(1);
    }

    /// <summary>Sıradaki (+1) ya da önceki (-1) parçaya geçer; sıra sarınca karışıkta yeniden karıştırılır.</summary>
    private static void Advance(int delta)
    {
        if (!_started || _order.Count == 0) return;
        _failures = 0;
        var next = _pos + delta;
        if (next >= _order.Count)
        {
            var cur = _order[_pos];
            if (Shuffle) BuildOrder(null);                       // yeni tur: yeniden karıştır
            if (Shuffle && _order[0] == cur && _order.Count > 1) (_order[0], _order[1]) = (_order[1], _order[0]);   // aynı parça üst üste çalmasın
            next = 0;
        }
        else if (next < 0) next = _order.Count - 1;
        _pos = next;
        Open();
    }

    public static void Next() => Advance(1);
    public static void Previous() => Advance(-1);

    /// <summary>Ayarlar listesinden seçilen parçayı çalar (müzik kapalıysa açar). Parça başka havuzdansa çalma sırası o havuza geçer.</summary>
    public static void PlayTrack(int index)
    {
        if (index < 0 || index >= Tracks.Count) return;
        if (!Enabled) { Enabled = true; Sfx.SaveSettings(); }
        if (!_started) { Start(); if (!_started) return; }
        var pool = PoolOf(Tracks[index]);
        if (pool != Context) { Context = pool; BuildOrder(index); }
        else BuildOrderKeeping(index);
        Open();
    }

    private static void BuildOrderKeeping(int first)
    {
        var pool = PoolIndices(Context);
        if (!Shuffle)
        {
            // sıralı mod: dosya sırası korunur, seçilen parçadan devam edilir
            _order = pool;
            _pos = Math.Max(0, _order.IndexOf(first));
            return;
        }
        var rest = _order.Where(i => i != first && pool.Contains(i)).ToList();
        if (rest.Count == 0) rest = pool.Where(i => i != first).ToList();
        _order = new List<int> { first }.Concat(rest).ToList();
        _pos = 0;
    }

    /// <summary>Karışık/sıralı değişince çalan parça kesilmeden sıra yeniden kurulur.</summary>
    public static void SetShuffle(bool shuffle)
    {
        Shuffle = shuffle;
        if (_started) BuildOrder(Current >= 0 ? Current : null);
    }

    public static void ApplyVolume()
    {
        if (_player != null) _player.Volume = Settings.MusicVolume / 100.0;
    }

    public static void Toggle()
    {
        Enabled = !Enabled;
        if (!Enabled) _player?.Pause();
        else if (_started) _player?.Play();
        else Start();
        Sfx.SaveSettings();
    }

    public static void Stop()
    {
        _player?.Stop();
        _started = false;
    }

    /// <summary>Çalan dosyayı tamamen bırakır (güncelleme dosyaları değiştirirken kilit kalmasın). Sonra <see cref="Start"/> ile yeniden başlar.</summary>
    public static void Release()
    {
        try { _player?.Stop(); _player?.Close(); } catch { }
        _player = null;
        _started = false;
    }
}
