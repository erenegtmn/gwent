using System.IO;
using System.Text.Json;
using System.Windows;
using GwentAI;

namespace GwentClient;

/// <summary>Kalıcı kullanıcı ayarları (%AppData%\Gwent\settings.json). Bozuk ya da eksik dosya oyunu durdurmaz.</summary>
public static class Settings
{
    public static int SfxVolume { get; set; } = 60;     // 0-100
    public static int MusicVolume { get; set; } = 35;   // 0-100
    public static bool Fullscreen { get; set; }
    public static Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public static string Nick { get; set; } = "";
    public static string LastAddress { get; set; } = "";

    private static string Path_ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "settings.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path_)) return;
            var r = JsonDocument.Parse(File.ReadAllText(Path_)).RootElement;
            if (r.TryGetProperty("sound", out var s)) Sfx.Enabled = s.GetBoolean();
            if (r.TryGetProperty("music", out var m)) Music.Enabled = m.GetBoolean();
            if (r.TryGetProperty("sfxVolume", out var sv)) SfxVolume = Math.Clamp(sv.GetInt32(), 0, 100);
            if (r.TryGetProperty("musicVolume", out var mv)) MusicVolume = Math.Clamp(mv.GetInt32(), 0, 100);
            if (r.TryGetProperty("fullscreen", out var f)) Fullscreen = f.GetBoolean();
            if (r.TryGetProperty("nick", out var nk)) Nick = nk.GetString() ?? "";
            if (r.TryGetProperty("lastAddress", out var la)) LastAddress = la.GetString() ?? "";
            if (r.TryGetProperty("difficulty", out var d) && Enum.IsDefined(typeof(Difficulty), d.GetInt32())) Difficulty = (Difficulty)d.GetInt32();
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(new
            {
                sound = Sfx.Enabled, music = Music.Enabled, sfxVolume = SfxVolume, musicVolume = MusicVolume,
                fullscreen = Fullscreen, difficulty = (int)Difficulty, nick = Nick, lastAddress = LastAddress
            }));
        }
        catch { }
    }

    /// <summary>Tam ekran ayarını pencereye uygular (kenarlıksız ve ekranı kaplar; yoksa normal pencere).</summary>
    public static void ApplyWindow(Window w)
    {
        if (Fullscreen)
        {
            w.WindowState = WindowState.Normal;
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.WindowState = WindowState.Maximized;
        }
        else
        {
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = ResizeMode.CanResize;
            w.WindowState = WindowState.Normal;
        }
    }
}
