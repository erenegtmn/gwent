using System.IO;
using System.Text.Json;

namespace GwentClient;

/// <summary>Günün laneti sonucu: o günün en iyi sonucu (galibiyet ve raund puan farkı).</summary>
public sealed record DailyResult(bool Won, int Margin, DateTime At);

/// <summary>Günün laneti sonuçları (%AppData%\Gwent\daily.json; tarih → en iyi sonuç). Sınamalar yazmaz.</summary>
public static class DailyStore
{
    public static string? PathOverride { get; set; }
    private static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "daily.json");

    public static Dictionary<string, DailyResult> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<string, DailyResult>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static DailyResult? Today(DateOnly date) => Load().GetValueOrDefault(date.ToString("yyyy-MM-dd"));

    /// <summary>Sonucu kaydeder (o günün daha iyisi varsa korunur). Daha iyiyse true.</summary>
    public static bool Record(DateOnly date, DailyResult r)
    {
        var all = Load();
        var key = date.ToString("yyyy-MM-dd");
        var old = all.GetValueOrDefault(key);
        var better = old == null || (r.Won && !old.Won) || (r.Won == old.Won && r.Margin > old.Margin);
        if (!better) return false;
        all[key] = r;
        if (Settings.NoSave && PathOverride == null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch { }
        return true;
    }
}
