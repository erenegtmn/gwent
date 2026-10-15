using System.IO;
using System.Text.Json;
using GwentCore;

namespace GwentClient.KaerMorhen;

/// <summary>
/// Demirci kaydı: deste başına takılı rünler. %AppData%\Gwent\kaermorhen\forge.json — asıl oyunun decks.json'una dokunmaz
/// (ana sürüm desteyi kaydederken rünleri silmesin diye ayrı dosya). Bozuk dosya oyunu durdurmaz.
/// </summary>
public static class ForgeStore
{
    /// <summary>Testler gerçek kayda dokunmasın diye değiştirilebilir.</summary>
    public static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "kaermorhen", "forge.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static Dictionary<string, List<CardRune>> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<CardRune>>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    /// <summary>Destenin rünleri; deste değiştiyse artık geçersiz olanlar ayıklanır.</summary>
    public static List<CardRune> For(string key, Deck deck)
    {
        var list = Load().GetValueOrDefault(key) ?? new List<CardRune>();
        var valid = new List<CardRune>();
        foreach (var r in list)
        {
            var trial = deck with { Runes = valid.Append(r).ToList() };
            if (RuneIds.Validate(trial, CardCatalog.Default).Count == 0) valid.Add(r);
        }
        return valid;
    }

    public static void Save(string key, IReadOnlyList<CardRune> runes)
    {
        var all = Load();
        if (runes.Count == 0) all.Remove(key); else all[key] = runes.ToList();
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
