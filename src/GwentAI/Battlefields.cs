using GwentCore;
using static GwentCore.Terrain;

namespace GwentAI;

/// <summary>Maç öncesi bir satıra konan arazi parçası (Mühendis Atölyesi: kule, barikat, hendek). Haritanın her raundunda o satırı ezer.</summary>
public sealed record TerrainPiece(int Side, RowKind Row, Terrain Terrain);

/// <summary>
/// Kaer Morhen savaş alanları (TASARIM.md §5): Witcher 3'ün yerlerinden esinlenen hazır arazi haritaları.
/// Her satır: oyuncu 0 (yakın, menzil, kuşatma) + oyuncu 1 (aynı sıra). Raund listesi kısaysa son raund sürer.
/// </summary>
public static class Battlefields
{
    private static IReadOnlyList<Terrain> Both(Terrain close, Terrain ranged, Terrain siege) => new[] { close, ranged, siege, close, ranged, siege };

    public static readonly IReadOnlyList<TerrainMap> All = new[]
    {
        new TerrainMap("white_orchard", "White Orchard", new[]
        {
            Both(Plain, Hill, Plain),
        }),
        new TerrainMap("pontar_bridge", "Pontar Bridge", new[]
        {
            Both(Bridge, Plain, Hill),
            Both(Bridge, Plain, Hill),
            Both(Swamp, Plain, Hill),      // 3. raund: nehir taşar, köprü yıkılır
        }),
        new TerrainMap("crookback_bog", "Crookback Bog", new[]
        {
            Both(Swamp, Forest, Plain),
            Both(Swamp, Forest, Swamp),    // 2. raund: bataklık yayılır
        }),
        new TerrainMap("novigrad_gates", "Novigrad Gates", new[]
        {
            Both(Plain, Plain, Hill),
            Both(Bridge, Plain, Hill),     // 2. raund: kapılar kapanır
        }),
        new TerrainMap("kaer_morhen_ruins", "Kaer Morhen Ruins", new[]
        {
            Both(Ruins, Plain, Hill),
        }),
        new TerrainMap("skellige_coast", "Ard Skellig Coast", new[]
        {
            Both(Plain, Hill, Swamp),
            Both(Plain, Forest, Swamp),
        }),
        new TerrainMap("toussaint_vineyards", "Toussaint Vineyards", new[]
        {
            Both(Forest, Plain, Plain),
            Both(Forest, Hill, Plain),
            Both(Ruins, Hill, Plain),
        }),
    };

    public static TerrainMap? Get(string? id) => id == null ? null : All.FirstOrDefault(m => m.Id == id);

    /// <summary>Tohumdan (deterministik) bir harita.</summary>
    public static TerrainMap ForSeed(int seed) => All[(int)((uint)seed % (uint)All.Count)];

    /// <summary>Kısa Türkçe arazi adı ve kuralı (arayüz ve günlük için).</summary>
    public static string Name(Terrain t) => t switch
    {
        Hill => "Tepe",
        Bridge => "Köprü",
        Forest => "Orman",
        Swamp => "Bataklık",
        Ruins => "Harabe",
        _ => "Düzlük"
    };

    public static string Rule(Terrain t) => t switch
    {
        Hill => "Birimler +1 (satırda hava varken bonus yok).",
        Bridge => $"En fazla {Game.BridgeCapacity} birim. Doluyken elden birim oynanamaz; başka yoldan gelen düşer.",
        Forest => "Bu satırdaki birimler yakılamaz.",
        Swamp => "Birimler −1 (en az 1).",
        Ruins => "Buraya konan Medic iki kez canlandırır.",
        _ => "Kural yok."
    };

    /// <summary>Klasik seçeneklere savaş alanı, komutanlar ve (varsa) arazi parçalarını ekler.</summary>
    public static GameOptions With(GameOptions options, string? battlefield, bool commanders, IReadOnlyList<TerrainPiece>? pieces = null) =>
        options with { Terrain = WithPieces(Get(battlefield), pieces), Commanders = commanders };

    /// <summary>Parçalar haritanın her raundunda kendi satırını ezer (harita yoksa düzlük üstüne kurulur).</summary>
    public static TerrainMap? WithPieces(TerrainMap? map, IReadOnlyList<TerrainPiece>? pieces)
    {
        if (pieces is not { Count: > 0 }) return map;
        map ??= TerrainMap.Symmetric("plain", "Düzlük", Plain, Plain, Plain);
        var rounds = map.Rounds.Select(r =>
        {
            var arr = r.ToArray();
            foreach (var p in pieces) arr[p.Side * 3 + (int)p.Row] = p.Terrain;
            return (IReadOnlyList<Terrain>)arr;
        }).ToList();
        return map with { Rounds = rounds };
    }
}
