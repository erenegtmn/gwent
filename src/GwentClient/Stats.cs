using System.IO;
using System.Text.Json;

namespace GwentClient;

/// <summary>Biten bir maçın kaydı. Opponent: "Yapay zekâ (Normal)" ya da online rakibin takma adı.</summary>
public sealed record MatchRecord(DateTime Date, bool Online, string Opponent, string MyFaction, string OppFaction, int Result);   // Result: 1 kazandı, 0 berabere, -1 kaybetti

/// <summary>Yerel istatistik (%AppData%\Gwent\stats.json). Hesap yok; yalnız bu bilgisayarda tutulur. Sınamalar <see cref="Enabled"/> kapalıyken kullanıcının dosyasına yazmaz.</summary>
public static class Stats
{
    private const int MaxRecords = 500;
    public static bool Enabled { get; set; }
    public static string? PathOverride { get; set; }
    private static string Path_ => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "stats.json");

    public static List<MatchRecord> Load()
    {
        try
        {
            if (File.Exists(Path_)) return JsonSerializer.Deserialize<List<MatchRecord>>(File.ReadAllText(Path_)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Record(MatchRecord r)
    {
        if (!Enabled) return;
        try
        {
            var all = Load();
            all.Add(r);
            if (all.Count > MaxRecords) all.RemoveRange(0, all.Count - MaxRecords);
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(all));
        }
        catch { }
    }

    public static void Clear()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); } catch { }
    }

    public sealed record Summary(int Games, int Wins, int Draws, int Losses, int Streak, int BestStreak)
    {
        public int WinPercent => Games == 0 ? 0 : (int)Math.Round(100.0 * Wins / Games);
    }

    /// <summary>Özet: toplam, galibiyet/beraberlik/yenilgi, güncel galibiyet serisi (kayıp/beraberlikle sıfırlanır) ve en iyi seri.</summary>
    public static Summary Summarize(IEnumerable<MatchRecord> records)
    {
        int g = 0, w = 0, d = 0, l = 0, cur = 0, best = 0;
        foreach (var r in records)
        {
            g++;
            if (r.Result > 0) { w++; cur++; best = Math.Max(best, cur); }
            else { cur = 0; if (r.Result == 0) d++; else l++; }
        }
        return new Summary(g, w, d, l, cur, best);
    }
}
