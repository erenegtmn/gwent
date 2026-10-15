using System.IO;
using System.Text.Json;
using KaerMorhen.Core;

namespace GwentClient.KaerMorhen;

/// <summary>Kalenin kaydı: %AppData%\Gwent\kaermorhen\keep.json. Bozuk dosya oyunu durdurmaz (yeni kale açılır).</summary>
public static class KeepStore
{
    /// <summary>Testler gerçek kayda dokunmasın diye değiştirilebilir.</summary>
    public static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "kaermorhen", "keep.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static KeepSave? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<KeepSave>(File.ReadAllText(FilePath), Json) : null; }
        catch { return null; }
    }

    public static void Save(KeepSave s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch { }
    }
}
