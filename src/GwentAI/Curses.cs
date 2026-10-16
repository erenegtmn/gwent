using GwentCore;

namespace GwentAI;

/// <summary>Bir lanet: kimlik, ad ve kısa açıklama.</summary>
public sealed record Curse(string Id, string Name, string Text);

/// <summary>Günün laneti: o güne ait lanetler, rakip (fraksiyon/lider) ve maç tohumu. Herkes aynı gün aynı meydan okumayı oynar.</summary>
public sealed record DailyCurse(DateOnly Date, IReadOnlyList<string> Curses, string OppFaction, string OppLeaderId, int Seed);

/// <summary>
/// Gaunter O'Dimm'in lanetleri: kuralı bükülmüş maçlar. Bazıları motor seçeneğidir (<see cref="GameOptions"/>: el, hava, bedel),
/// bazıları deste dönüşümüdür (kahramansız, ayna). İki tarafa da aynı işler. Birden çok lanet birlikte seçilebilir.
/// </summary>
public static class Curses
{
    public const string ShortCandle = "short_candle";
    public const string EndlessWinter = "endless_winter";
    public const string NoHeroes = "no_heroes";
    public const string Mirror = "mirror";
    public const string BloodPrice = "blood_price";

    public static readonly IReadOnlyList<Curse> All = new[]
    {
        new Curse(ShortCandle, "Kısa Mum", "İki taraf da 10 yerine 8 kartla başlar."),
        new Curse(EndlessWinter, "Bitmeyen Kış", "Her raund rastgele bir hava kartıyla açılır. Açık hava kaldırabilir."),
        new Curse(NoHeroes, "Kahramansız Çağ", "İki destede de kahraman yok; yerlerine sıradan birimler girer."),
        new Curse(Mirror, "Ayna", "Rakip senin destenin aynısıyla oynar."),
        new Curse(BloodPrice, "Kan Bedeli", "Raundu kazanan, elinden rastgele bir kart kaybeder."),
    };

    public static Curse Get(string id) => All.First(c => c.Id == id);

    /// <summary>Online'da Ayna deste takasıdır (iki insan oyuncu: ikisi de ötekinin destesiyle oynar); öteki lanetler aynıdır.</summary>
    public const string MirrorOnlineText = "Desteler takas edilir: sen rakibinin destesiyle, o seninkiyle oynar.";

    public static string TextFor(string id, bool online) => online && id == Mirror ? MirrorOnlineText : Get(id).Text;

    /// <summary>Lanetlerin motor seçenekleri.</summary>
    public static GameOptions Options(IReadOnlyCollection<string>? curses, bool nextGen = false) => new()
    {
        NextGen = nextGen,
        StartingHand = curses?.Contains(ShortCandle) == true ? 8 : Game.StartingHand,
        RoundWeather = curses?.Contains(EndlessWinter) == true,
        BloodPrice = curses?.Contains(BloodPrice) == true,
    };

    /// <summary>Deste lanetleri: kahramansız ve ayna. (Benim destem, rakibin destesi) döner.</summary>
    public static (Deck Mine, Deck Opp) ApplyDecks(IReadOnlyCollection<string>? curses, Deck mine, Deck opp, GameOptions? options = null)
    {
        if (curses == null || curses.Count == 0) return (mine, opp);
        if (curses.Contains(Mirror)) opp = mine;
        if (curses.Contains(NoHeroes)) { mine = WithoutHeroes(mine, options); opp = WithoutHeroes(opp, options); }
        return (mine, opp);
    }

    /// <summary>Kahramanları çıkarır; birim sayısı düşmesin diye yerlerine fraksiyonun en güçlü (sınırı dolmamış) sıradan birimlerini koyar.</summary>
    public static Deck WithoutHeroes(Deck d, GameOptions? options = null)
    {
        var cat = CardCatalog.Default;
        var cards = d.CardIds.Where(id => !cat.Get(id).Hero).ToList();
        var removed = d.CardIds.Count - cards.Count;
        var spare = DeckValidator.Pool(d.Faction, options).Where(c => c.IsUnit && !c.Hero && !c.Has("spy") && c.MusterGroup == null)
            .OrderByDescending(c => c.Strength).ToList();
        foreach (var c in spare)
        {
            while (removed > 0 && cards.Count(x => x == c.Id) < c.Copies) { cards.Add(c.Id); removed--; }
            if (removed == 0) break;
        }
        return new Deck(d.Faction, d.LeaderId, cards);
    }

    /// <summary>Günün laneti: tarihten üretilir (aynı gün herkese aynı): 2 lanet, zor bir rakip ve maç tohumu.</summary>
    public static DailyCurse Daily(DateOnly date)
    {
        var seed = date.Year * 10000 + date.Month * 100 + date.Day;
        var rng = new Random(seed * 7 + 13);
        var curses = All.OrderBy(_ => rng.Next()).Take(2).Select(c => c.Id).ToList();
        var factions = new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
        var f = factions[rng.Next(factions.Length)];
        var leaders = CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen").ToList();
        return new DailyCurse(date, curses, f, leaders[rng.Next(leaders.Count)].Id, seed);
    }
}
