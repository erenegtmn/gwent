using System.IO;
using System.Text.Json;
using GwentAI.Career;

namespace GwentClient;

/// <summary>
/// Kariyer kaydı: %AppData%\Gwent\career.json (yalnız bu bilgisayarda). Bozuk ya da eski kayıt yok sayılır.
/// Sınamalar ve ekran görüntüleri (<see cref="Settings.NoSave"/>) kullanıcının kaydına dokunmaz; <see cref="PathOverride"/> ile geçici dosya verilebilir.
/// </summary>
public static class CareerStore
{
    public static string? PathOverride { get; set; }
    private static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "career.json");

    public static CareerSave? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var s = JsonSerializer.Deserialize<CareerSave>(File.ReadAllText(FilePath));
            return s is { Version: 1 } && s.Cards.Count > 0 ? s : null;
        }
        catch { return null; }
    }

    public static void Save(CareerSave s)
    {
        if (Settings.NoSave && PathOverride == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* kayıt başarısızsa oyun sürer */ }
    }

    public static void Delete()
    {
        if (Settings.NoSave && PathOverride == null) return;
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
