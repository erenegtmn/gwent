using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GwentCore;

/// <summary>Savaş alanındaki üç sıra. Her oyuncunun kendi tarafında bir tane vardır.</summary>
public enum RowKind { Close = 0, Ranged = 1, Siege = 2 }

public static class Ids
{
    public const string Decoy = "decoy";
    public const string Horn = "commanders_horn";
    public const string Scorch = "scorch";
    public const string Mardroeme = "mardroeme";
    public const string ClearWeather = "clear_weather";
    public const string BitingFrost = "biting_frost";
    public const string ImpenetrableFog = "impenetrable_fog";
    public const string TorrentialRain = "torrential_rain";
    public const string SkelligeStorm = "skellige_storm";
    public const string Roach = "roach";
}

/// <summary>Bir kartın değişmeyen tanımı (data/cards.json).</summary>
public sealed class CardDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "";
    public string Type { get; set; } = "";
    public string Edition { get; set; } = "base";
    public string? Row { get; set; }
    public int Strength { get; set; }
    public bool Hero { get; set; }
    public List<string> Abilities { get; set; } = new();
    public List<string>? NextgenAbilities { get; set; }
    public string? MusterGroup { get; set; }
    public bool Token { get; set; }
    public int Copies { get; set; } = 1;
    public string? Effect { get; set; }
    public string? Summons { get; set; }
    public string? TransformsInto { get; set; }

    [JsonIgnore] public bool IsUnit => Type == "unit";
    [JsonIgnore] public bool IsLeader => Type == "leader";
    [JsonIgnore] public bool IsWeather => Type == "weather";
    [JsonIgnore] public bool IsSpecial => Type == "special";
    [JsonIgnore] public bool IsAgile => Row == "agile";

    /// <summary>Sabit sıralı kartın sırası (Agile için null).</summary>
    [JsonIgnore]
    public RowKind? FixedRow => Row switch
    {
        "close" => RowKind.Close,
        "ranged" => RowKind.Ranged,
        "siege" => RowKind.Siege,
        _ => null
    };

    public bool Has(string ability) => Abilities.Contains(ability);
    public override string ToString() => Name;
}

public sealed class CardCatalog
{
    private readonly Dictionary<string, CardDef> _byId;
    public IReadOnlyList<CardDef> All { get; }

    private CardCatalog(List<CardDef> all)
    {
        All = all;
        _byId = all.ToDictionary(c => c.Id);
    }

    private static CardCatalog? _default;
    public static CardCatalog Default => _default ??= Load();

    public CardDef Get(string id) =>
        _byId.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Bilinmeyen kart: {id}");

    public bool TryGet(string id, out CardDef def) => _byId.TryGetValue(id, out def!);

    public IEnumerable<CardDef> Leaders(string faction) => All.Where(c => c.IsLeader && c.Faction == faction);

    /// <summary>Kart verisinin parmak izi: istemci ve sunucu aynı veriyle oynadığını bununla doğrular.</summary>
    public string Hash { get; private set; } = "";

    public static CardCatalog Load()
    {
        var asm = typeof(CardCatalog).Assembly;
        using var s = asm.GetManifestResourceStream("cards.json")
                      ?? throw new InvalidOperationException("cards.json gömülü kaynak olarak bulunamadı.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var bytes = ms.ToArray();
        var list = JsonSerializer.Deserialize<List<CardDef>>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("cards.json okunamadı.");
        var cat = new CardCatalog(list);
        cat.Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        return cat;
    }
}
