using System.IO;
using System.Windows.Media;

namespace GwentClient;

/// <summary>
/// Arka plan müziği: <c>assets/music/*.mp3</c> (Gwent masasında çalan han müzikleri) karışık ya da sıralı çalınır.
/// Parça atlama, parça seçme, karışık/sıralı çalma ve "şimdi çalıyor" bilgisi (<see cref="TrackChanged"/>) vardır.
/// Dosya yoksa sessiz kalır. Açık/kapalı ve ses düzeyi %AppData%\Gwent\settings.json içinde saklanır.
/// </summary>
public static class Music
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Karışık çalma (varsayılan açık). Kapalıysa parçalar dosya sırasıyla çalınır.</summary>
    public static bool Shuffle { get; set; } = true;

    /// <summary>Çalan parça değişince (parça bitti, atlandı ya da seçildi) tetiklenir. Arayüz iş parçacığında.</summary>
    public static event Action? TrackChanged;

    private static MediaPlayer? _player;
    private static List<string> _tracks = new();
    private static List<int> _order = new();
    private static int _pos;
    private static int _failures;
    private static bool _started;

    /// <summary>Çalınabilir tüm parçalar (ayarlar ekranındaki liste).</summary>
    public static IReadOnlyList<string> Tracks
    {
        get
        {
            if (_tracks.Count == 0) _tracks = Assets.Music().ToList();
            return _tracks;
        }
    }

    /// <summary>Çalan parçanın <see cref="Tracks"/> içindeki sırası; hiçbiri çalmıyorsa -1.</summary>
    public static int Current => _started && _order.Count > 0 ? _order[_pos % _order.Count] : -1;

    public static string? CurrentTitle => Current is var i and >= 0 ? TitleOf(i) : null;

    public static string TitleOf(int index) => index >= 0 && index < Tracks.Count ? TitleOfFile(Tracks[index]) : "";

    /// <summary>Dosya adından okunur ad: novigrad_tavern_01 → "Novigrad Hanı I", toussaint_tavern_02 → "Toussaint Hanı (Kan ve Şarap) II".</summary>
    internal static string TitleOfFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var region = name.StartsWith("novigrad") ? "Novigrad Hanı"
            : name.StartsWith("skellige") ? "Skellige Hanı"
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
            _player = new MediaPlayer { Volume = Settings.MusicVolume / 100.0 };
            _player.MediaEnded += (_, _) => Advance(1);
            _player.MediaFailed += (_, _) => OnFailed();
            _started = true;
            Open();
        }
        catch { _started = false; }
    }

    /// <summary>Çalma sırasını kurar: karışık ya da dosya sırası. <paramref name="first"/> doluysa o parça başa alınır.</summary>
    private static void BuildOrder(int? first)
    {
        var idx = Enumerable.Range(0, Tracks.Count).ToList();
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
        if (++_failures >= Tracks.Count) { _started = false; return; }
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

    /// <summary>Ayarlar listesinden seçilen parçayı çalar (müzik kapalıysa açar).</summary>
    public static void PlayTrack(int index)
    {
        if (index < 0 || index >= Tracks.Count) return;
        if (!Enabled) { Enabled = true; Sfx.SaveSettings(); }
        if (!_started) { Start(); if (!_started) return; }
        BuildOrderKeeping(index);
        Open();
    }

    private static void BuildOrderKeeping(int first)
    {
        if (!Shuffle)
        {
            // sıralı mod: dosya sırası korunur, seçilen parçadan devam edilir
            _order = Enumerable.Range(0, Tracks.Count).ToList();
            _pos = first;
            return;
        }
        var old = _order.ToList();
        var rest = old.Where(i => i != first).ToList();
        if (Shuffle && rest.Count == 0) rest = Enumerable.Range(0, Tracks.Count).Where(i => i != first).ToList();
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
