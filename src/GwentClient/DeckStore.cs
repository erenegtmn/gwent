using System.IO;
using System.Text.Json;
using GwentCore;

namespace GwentClient;

public sealed record SavedDeck(string Name, string Faction, string LeaderId, List<string> CardIds)
{
    public Deck ToDeck() => new(Faction, LeaderId, CardIds);
}

/// <summary>Kayıtlı desteler: %AppData%\Gwent\decks.json. Bozuk ya da eksik dosya oyunu durdurmaz.</summary>
public static class DeckStore
{
    /// <summary>Testler gerçek kullanıcı verisine dokunmasın diye değiştirilebilir.</summary>
    public static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "decks.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<SavedDeck> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<SavedDeck>();
            return JsonSerializer.Deserialize<List<SavedDeck>>(File.ReadAllText(FilePath)) ?? new List<SavedDeck>();
        }
        catch
        {
            return new List<SavedDeck>();
        }
    }

    public static List<SavedDeck> For(string faction) => Load().Where(d => d.Faction == faction).OrderBy(d => d.Name).ToList();

    public static void Save(SavedDeck deck)
    {
        var all = Load();
        all.RemoveAll(d => d.Faction == deck.Faction && string.Equals(d.Name, deck.Name, StringComparison.OrdinalIgnoreCase));
        all.Add(deck);
        Write(all);
    }

    public static void Delete(string name, string faction)
    {
        var all = Load();
        all.RemoveAll(d => d.Faction == faction && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        Write(all);
    }

    private static void Write(List<SavedDeck> all)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        // Önce geçici dosyaya yaz: yarım yazılmış dosya mevcut desteleri bozmasın.
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
