using System.Reflection;
using System.Text.Json;
using GwentCore;

namespace GwentAI.Career;

/// <summary>
/// Orijinal oyunun NPC Gwent desteleri (def_gwint_decks.xml → data/career_decks.json, <c>tools/build_career.py</c>) ve tüccar fiyatları.
/// Desteler orijinaldeki gibidir: oyuncu kurallarına uymayabilir (22'den az birim, fazla kopya, başka fraksiyondan kart);
/// bu yüzden maçta doğrulanmadan kullanılırlar (<see cref="ReplayData.OppDeckUnchecked"/>).
/// </summary>
public static class NpcDecks
{
    private sealed record RawDeck(string? leader, List<string> cards, string? special);
    private sealed record RawFile(Dictionary<string, Dictionary<string, RawDeck>> decks, Dictionary<string, int> prices);

    private static readonly Lazy<RawFile> Data = new(Load);

    private static RawFile Load()
    {
        var asm = typeof(NpcDecks).Assembly;
        using var s = asm.GetManifestResourceStream("career_decks.json") ?? throw new InvalidOperationException("career_decks.json gömülü kaynak olarak bulunamadı.");
        return JsonSerializer.Deserialize<RawFile>(s) ?? throw new InvalidOperationException("career_decks.json okunamadı.");
    }

    public static IReadOnlyCollection<string> Collections => Data.Value.decks.Keys;

    /// <summary>Koleksiyonun <paramref name="level"/> (1 kolay, 2 normal, 3 zor) destesi; o düzey boşsa en yakın dolu düzey.</summary>
    public static Deck Get(string collection, int level, bool nextGen = false, CardCatalog? catalog = null)
    {
        catalog ??= CardCatalog.Default;
        if (!Data.Value.decks.TryGetValue(collection, out var levels)) throw new ArgumentException($"Bilinmeyen NPC destesi: {collection}");
        var order = new[] { level, 2, 3, 1 }.Select(l => l.ToString()).Distinct();
        foreach (var key in order)
        {
            if (!levels.TryGetValue(key, out var raw) || raw.leader == null || raw.cards.Count == 0) continue;
            var leader = catalog.Get(raw.leader);
            var cards = raw.cards.Where(id => catalog.TryGet(id, out var d) && (nextGen || d.Edition != "nextgen") && !d.Token && !d.IsLeader).ToList();
            return new Deck(leader.Faction, leader.Id, cards);
        }
        throw new ArgumentException($"{collection} destesi boş.");
    }

    /// <summary>Oyunun bu rakibe bağladığı özel kart (onu yenince kazanılan benzersiz kart); yoksa null.</summary>
    public static string? Reward(string collection) =>
        Data.Value.decks.TryGetValue(collection, out var levels) ? levels.Values.Select(d => d.special).FirstOrDefault(s => s != null) : null;

    /// <summary>Tüccar fiyatı (oyunun kendi fiyat listesi); listede yoksa nadirliğe göre.</summary>
    public static int Price(CardDef d)
    {
        if (Data.Value.prices.TryGetValue(d.Id, out var p)) return p;
        if (d.IsLeader) return 50;
        if (d.Hero) return 50;
        return d.Abilities.Count > 0 ? 20 : 10;
    }
}
