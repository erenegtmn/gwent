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
    public static bool Fullscreen { get; set; } = true;   // varsayılan tam ekran
    public static Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public static string Nick { get; set; } = "";
    public static string Portrait { get; set; } = Portraits.Default;
    public static string LastAddress { get; set; } = "";

    /// <summary>Sanal taç bakiyesi: turnuva katılım ücreti ve ödülleri için. Yalnız bu bilgisayarda tutulur (güven esaslı; hesap yok).</summary>
    public static int Crowns { get; set; } = 500;

    /// <summary>Ücreti ödenmiş turnuvalar (kod → ücret) ve ödülü işlenmiş olanlar; yeniden açılışta çifte işlem olmasın.</summary>
    public static Dictionary<string, int> PaidTournaments { get; } = new();
    public static List<string> SettledTournaments { get; } = new();

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
            // Sürüm alanı olmayan eski ayar dosyaları bir kez tam ekrana geçer; sonrasında seçim kullanıcıya aittir
            if (r.TryGetProperty("fullscreen", out var f) && r.TryGetProperty("settingsVersion", out _)) Fullscreen = f.GetBoolean();
            if (r.TryGetProperty("nick", out var nk)) Nick = nk.GetString() ?? "";
            if (r.TryGetProperty("portrait", out var pt)) Portrait = Portraits.Normalize(pt.GetString());
            if (r.TryGetProperty("lastAddress", out var la)) LastAddress = la.GetString() ?? "";
            if (r.TryGetProperty("crowns", out var cr)) Crowns = Math.Max(0, cr.GetInt32());
            if (r.TryGetProperty("paidTournaments", out var pt2))
                foreach (var kv in pt2.EnumerateObject()) PaidTournaments[kv.Name] = kv.Value.GetInt32();
            if (r.TryGetProperty("settledTournaments", out var st2))
                foreach (var e in st2.EnumerateArray()) SettledTournaments.Add(e.GetString() ?? "");
            if (r.TryGetProperty("difficulty", out var d) && Enum.IsDefined(typeof(Difficulty), d.GetInt32())) Difficulty = (Difficulty)d.GetInt32();
        }
        catch { }
    }

    /// <summary>Sınama ve ekran görüntüsü süreçleri kullanıcının ayar dosyasına yazmaz.</summary>
    public static bool NoSave { get; set; }

    public static void Save()
    {
        if (NoSave) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(new
            {
                sound = Sfx.Enabled, music = Music.Enabled, sfxVolume = SfxVolume, musicVolume = MusicVolume,
                fullscreen = Fullscreen, settingsVersion = 2, difficulty = (int)Difficulty, nick = Nick, portrait = Portrait, lastAddress = LastAddress, crowns = Crowns,
                paidTournaments = PaidTournaments, settledTournaments = SettledTournaments.TakeLast(30).ToList()
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
