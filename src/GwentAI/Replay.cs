using System.Text.Json;
using GwentCore;

namespace GwentAI;

/// <summary>Tek bir eylemin dosyaya yazılabilir hâli.</summary>
public sealed record ReplayAction
{
    public string Type { get; init; } = "";
    public int Player { get; init; }
    public List<int>? Cards { get; init; }
    public int? Card { get; init; }
    public string? Row { get; init; }
    public int? Target { get; init; }
    public int? First { get; init; }
}

/// <summary>
/// Bir maçın tekrarı: kurulum (fraksiyon, lider, deste, tohum) + eylem listesi. Motor deterministik olduğu için
/// aynı kurulum ve eylemler her zaman aynı oyunu üretir; böylece "şu anda bir hata oldu" bildirimleri birebir yeniden oynatılabilir.
/// </summary>
public sealed class ReplayData
{
    public string MyFaction { get; set; } = "";
    public string MyLeaderId { get; set; } = "";
    public string OppFaction { get; set; } = "";
    public string OppLeaderId { get; set; } = "";
    public bool NextGen { get; set; }
    public int Seed { get; set; }
    public List<string>? MyDeckCards { get; set; }
    /// <summary>Doluysa rakibin destesi de açıktır (online maç); boşsa rakip "önerilen" desteyle oynar (yerel oyun).</summary>
    public List<string>? OppDeckCards { get; set; }
    public int Difficulty { get; set; }
    public List<ReplayAction> Actions { get; set; } = new();
    /// <summary>Oyuncunun gördüğü Türkçe olay günlüğü (hata bildirirken neyin olduğunu gösterir).</summary>
    public List<string> Log { get; set; } = new();

    /// <summary>Oyunu kurar. Rakip desteği her zaman "önerilen" destedir (yerel oyunla aynı kural).</summary>
    public Game CreateGame()
    {
        var opts = new GameOptions { NextGen = NextGen };
        var mine = MyDeckCards != null
            ? new Deck(MyFaction, MyLeaderId, MyDeckCards)
            : DeckValidator.DefaultDeck(MyFaction, opts) with { LeaderId = MyLeaderId };
        var theirs = OppDeckCards != null
            ? new Deck(OppFaction, OppLeaderId, OppDeckCards)
            : DeckValidator.RecommendedDeck(OppFaction, opts, null, OppLeaderId);
        return new Game(mine, theirs, Seed, opts);
    }

    /// <summary>Kayıtlı eylemlerin hepsini oyuna uygular; her eylemden sonra <paramref name="each"/> çağrılır.</summary>
    public Game Run(Action<int, GameAction, ActionResult, Game>? each = null)
    {
        var g = CreateGame();
        for (var i = 0; i < Actions.Count; i++)
        {
            var a = Decode(Actions[i]);
            var r = g.Apply(a);
            each?.Invoke(i, a, r, g);
            if (!r.Ok) break;
        }
        return g;
    }

    // ---------- Eylem <-> kayıt ----------

    public static ReplayAction Encode(GameAction a) => a switch
    {
        Mulligan m => new ReplayAction { Type = "mulligan", Player = m.Player, Cards = m.Cards.ToList() },
        ChooseFirst c => new ReplayAction { Type = "choose_first", Player = c.Player, First = c.First },
        PlayCard p => new ReplayAction { Type = "play", Player = p.Player, Card = p.Card, Row = p.Row?.ToString(), Target = p.Target },
        Pass p => new ReplayAction { Type = "pass", Player = p.Player },
        UseLeader u => new ReplayAction { Type = "leader", Player = u.Player },
        Choose c => new ReplayAction { Type = "choose", Player = c.Player, Cards = c.Cards.ToList(), Row = c.Row?.ToString() },
        _ => throw new ArgumentException("Bilinmeyen eylem: " + a)
    };

    public static GameAction Decode(ReplayAction r)
    {
        RowKind? row = r.Row != null ? Enum.Parse<RowKind>(r.Row) : null;
        return r.Type switch
        {
            "mulligan" => new Mulligan(r.Player, r.Cards ?? new List<int>()),
            "choose_first" => new ChooseFirst(r.Player, r.First ?? 0),
            "play" => new PlayCard(r.Player, r.Card ?? 0, row, r.Target),
            "pass" => new Pass(r.Player),
            "leader" => new UseLeader(r.Player),
            "choose" => new Choose(r.Player, r.Cards ?? new List<int>(), row),
            _ => throw new ArgumentException("Bilinmeyen eylem türü: " + r.Type)
        };
    }

    // ---------- Dosya ----------

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static ReplayData FromJson(string json) => JsonSerializer.Deserialize<ReplayData>(json, Json) ?? throw new InvalidDataException("Boş tekrar dosyası");
}
