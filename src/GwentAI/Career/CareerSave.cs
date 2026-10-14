using GwentCore;

namespace GwentAI.Career;

/// <summary>Kariyerde kurulan deste (fraksiyon başına bir tane).</summary>
public sealed record CareerDeck(string LeaderId, List<string> Cards);

/// <summary>Bir rakibe karşı sonuçlar.</summary>
public sealed class CareerRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
}

/// <summary>Süren turnuva: hangi turdasın ve turların desteleri (kayıtlıdır, uygulama kapanınca kaldığı yerden sürer).</summary>
public sealed class CareerTournamentRun
{
    public string Opponent { get; set; } = "";
    public int Round { get; set; }
    public List<string> Decks { get; set; } = new();
    /// <summary>Girişte kilitlenen oyuncu destesi: turnuva boyunca bununla oynanır.</summary>
    public string Faction { get; set; } = "";
    public CareerDeck? Deck { get; set; }
}

/// <summary>
/// Kariyer kaydı: kese, koleksiyon (kart kimliği → adet; liderler dahil), desteler, rakiplere karşı sonuçlar ve süren turnuva.
/// Yalnız bu bilgisayarda tutulur (%AppData%\Gwent\career.json).
/// </summary>
public sealed class CareerSave
{
    public int Version { get; set; } = 1;
    public int Crowns { get; set; }
    public Dictionary<string, int> Cards { get; set; } = new();
    public Dictionary<string, CareerDeck> Decks { get; set; } = new();
    public string ActiveFaction { get; set; } = "northern_realms";
    public Dictionary<string, CareerRecord> Records { get; set; } = new();
    public CareerTournamentRun? Tournament { get; set; }
    public string LastRegion { get; set; } = "white_orchard";
    public DateTime Started { get; set; } = DateTime.UtcNow;

    public int Owned(string id) => Cards.GetValueOrDefault(id);

    public void Add(string id, int n = 1) => Cards[id] = Owned(id) + n;

    public CareerRecord Record(string opp) => Records.TryGetValue(opp, out var r) ? r : Records[opp] = new CareerRecord();

    public bool Beaten(string opp) => Records.TryGetValue(opp, out var r) && r.Wins > 0;

    /// <summary>Oynanacak deste: süren turnuvada girişte kilitlenen deste, değilse etkin deste; yoksa null.</summary>
    public Deck? ActiveDeck() =>
        Tournament is { Deck: { } td } t ? new Deck(t.Faction, td.LeaderId, td.Cards)
        : Decks.TryGetValue(ActiveFaction, out var d) ? new Deck(ActiveFaction, d.LeaderId, d.Cards) : null;
}
