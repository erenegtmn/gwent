namespace GwentCore;

// Kaer Morhen (TASARIM.md §4.5): müttefikler. Destenin dışında, maça elde fazladan kart olarak başlarlar.
// Destede müttefik yoksa klasik Gwent aynen işler.

/// <summary>Müttefik kuralları.</summary>
public static class AllyRules
{
    /// <summary>Bir maça en fazla bu kadar müttefik katılır.</summary>
    public const int MaxPerMatch = 3;

    /// <summary>Müttefik olabilir mi: tarafsız birim kartı (lider ya da jeton değil).</summary>
    public static bool CanJoin(CardDef d) => d.Faction == "neutral" && d.IsUnit && !d.Token && !d.IsLeader;

    public static List<string> Validate(Deck deck, CardCatalog catalog)
    {
        var errs = new List<string>();
        if (deck.Allies is not { Count: > 0 } allies) return errs;
        if (allies.Count > MaxPerMatch) errs.Add($"Bir maça en fazla {MaxPerMatch} müttefik katılabilir.");
        if (allies.Distinct().Count() != allies.Count) errs.Add("Aynı müttefik iki kez katılamaz.");
        foreach (var id in allies)
            if (!catalog.TryGet(id, out var d) || !CanJoin(d)) errs.Add($"{id} müttefik olamaz (yalnız tarafsız birimler).");
        return errs;
    }
}

public sealed partial class Game
{
    /// <summary>Müttefikler başlangıç elinin üstüne eklenir (kart değiştirmede değiştirilebilirler).</summary>
    private void AddAllies(PlayerState ps, Deck deck)
    {
        if (deck.Allies == null) return;
        foreach (var id in deck.Allies) ps.Hand.Add(NewCard(id, ps.Index));
    }
}
