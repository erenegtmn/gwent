using System.IO;
using System.Windows.Media;

namespace GwentClient;

/// <summary>
/// Arka plan müziği: <c>assets/music/*.mp3</c> (Gwent masasında çalan han müzikleri) karıştırılıp sırayla çalınır.
/// Dosya yoksa sessiz kalır. Ayar %AppData%\Gwent\settings.json içinde saklanır (sound ile birlikte).
/// </summary>
public static class Music
{
    public static bool Enabled { get; set; } = true;
    private static MediaPlayer? _player;
    private static List<string> _queue = new();
    private static int _index;
    private static bool _started;

    public static void Start()
    {
        if (_started || !Enabled) return;
        try
        {
            var tracks = Assets.Music().ToList();
            if (tracks.Count == 0) return;
            var rng = new Random();
            _queue = tracks.OrderBy(_ => rng.Next()).ToList();
            _index = 0;
            _player = new MediaPlayer { Volume = Settings.MusicVolume / 100.0 };
            _player.MediaEnded += (_, _) => Next();
            _player.MediaFailed += (_, _) => Next();
            _started = true;
            Open();
        }
        catch { _started = false; }
    }

    private static void Open()
    {
        if (_player == null || _queue.Count == 0) return;
        _player.Open(new Uri(_queue[_index % _queue.Count]));
        if (Enabled) _player.Play();
    }

    private static void Next()
    {
        _index++;
        Open();
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
}
