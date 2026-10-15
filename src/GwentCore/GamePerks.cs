namespace GwentCore;

// Kaer Morhen (TASARIM.md §4.2, §4.4): Kışla avantajları ve Simya Odası iksirleri. Destenin parçası (Deck.Perks);
// yoksa klasik Gwent aynen işler.

/// <summary>Bir oyuncunun maç avantajları: Kışla (el, kart değiştirme, kim başlar) ve iksirler.</summary>
public sealed record Perks(int ExtraHand = 0, int ExtraMulligans = 0, bool ChoosesFirst = false, IReadOnlyList<string>? Potions = null)
{
    public bool Has(string potion) => Potions?.Contains(potion) == true;
}

/// <summary>İksir kimlikleri (W3'teki iksirler).</summary>
public static class PotionIds
{
    /// <summary>Kedi: 1. raund başında rakibin elinden rastgele 3 kartı görürsün.</summary>
    public const string Cat = "cat";
    /// <summary>Kırlangıç: kaybettiğin (ya da berabere) raunddan sonra 1 kart çekersin.</summary>
    public const string Swallow = "swallow";
    /// <summary>Gök Gürültüsü: 1. raundda yakın dövüş birimlerin +1.</summary>
    public const string Thunderbolt = "thunderbolt";
    /// <summary>Puhu: hava birimlerini 1'e değil yarıya indirir.</summary>
    public const string TawnyOwl = "tawny_owl";
    /// <summary>Kara Kan: rakibin oynadığı ilk casus ona kart çektirmez.</summary>
    public const string BlackBlood = "black_blood";

    public static readonly IReadOnlyList<string> All = new[] { Cat, Swallow, Thunderbolt, TawnyOwl, BlackBlood };

    public static List<string> Validate(Deck deck)
    {
        var errs = new List<string>();
        if (deck.Perks is not { } p) return errs;
        if (p.ExtraHand is < 0 or > 1) errs.Add("Başlangıç eline en fazla 1 kart eklenebilir.");
        if (p.ExtraMulligans is < 0 or > 1) errs.Add("En fazla 1 fazladan kart değiştirme hakkı olabilir.");
        if (p.Potions != null)
        {
            if (p.Potions.Distinct().Count() != p.Potions.Count) errs.Add("Aynı iksir iki kez içilemez.");
            foreach (var x in p.Potions.Where(x => !All.Contains(x))) errs.Add($"Bilinmeyen iksir: {x}.");
        }
        return errs;
    }
}

public sealed partial class Game
{
    private readonly Perks?[] _perks = new Perks?[2];
    /// <summary>Kara Kan bir kez işler: hangi oyuncunun iksiri kullanıldı.</summary>
    private readonly bool[] _blackBloodUsed = new bool[2];

    public Perks? PerksOf(int player) => _perks[player];

    private bool HasPotion(int player, string potion) => _perks[player]?.Has(potion) == true;

    /// <summary>Bu oyuncunun kart değiştirme hakkı (klasik 2; Kışla +1).</summary>
    public int MulligansFor(int player) => MaxMulligans + (_perks[player]?.ExtraMulligans ?? 0);

    /// <summary>Başlangıç oyuncusu yazı-turayla değil, bir oyuncunun seçimiyle belirlenecek mi.</summary>
    public bool FirstIsChosen => FirstChoosers().Count == 1;

    /// <summary>Raund başlarken kimin seçeceği: Scoia'tael ya da Kışla (2. seviye). Tam bir oyuncu seçebiliyorsa o seçer.</summary>
    private List<int> FirstChoosers() =>
        Enumerable.Range(0, 2).Where(i => _players[i].Faction == "scoiatael" || _perks[i]?.ChoosesFirst == true).ToList();

    /// <summary>Kedi: 1. raund başında rakibin elinden rastgele 3 kart (yalnız iksiri içen görür).</summary>
    private void CatPeek()
    {
        for (var p = 0; p < 2; p++)
        {
            if (!HasPotion(p, PotionIds.Cat)) continue;
            var hand = _players[1 - p].Hand.ToList();
            _rng.Shuffle(hand);
            var picks = hand.Take(3).ToList();
            if (picks.Count > 0) Emit("peek", p, 0, string.Join(",", picks.Select(c => c.Def.Id)), visible: p);
        }
    }

    /// <summary>Kara Kan: rakibin ilk casusu ona kart çektirmez. true = çekiliş engellendi.</summary>
    private bool BlackBloodBlocks(int spyPlayer)
    {
        var victim = 1 - spyPlayer;
        if (!HasPotion(victim, PotionIds.BlackBlood) || _blackBloodUsed[victim]) return false;
        _blackBloodUsed[victim] = true;
        Emit("potion_black_blood", victim, 0);
        return true;
    }
}
