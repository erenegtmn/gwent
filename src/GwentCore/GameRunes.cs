namespace GwentCore;

// Kaer Morhen (TASARIM.md §4.3): demircide kartlara takılan rünler. Rün destenin parçasıdır (Deck.Runes);
// destede rün yoksa klasik Gwent aynen işler.

/// <summary>Destedeki bir karta takılan rün: o kimlikteki kartlardan rünsüz ilkine uygulanır.</summary>
public sealed record CardRune(string CardId, string Rune);

/// <summary>Rün kimlikleri ve kuralları.</summary>
public static class RuneIds
{
    /// <summary>+1 güç.</summary>
    public const string Svarog = "svarog";
    /// <summary>Hava etkisinden muaf.</summary>
    public const string Perun = "perun";
    /// <summary>Yakılırsa (bir kez) mezarlık yerine ele döner.</summary>
    public const string Veles = "veles";
    /// <summary>Moral: satırdaki diğer birimlere +1.</summary>
    public const string Dazhbog = "dazhbog";
    /// <summary>Komutanın satırındaysa +2.</summary>
    public const string Stribog = "stribog";
    /// <summary>Raund sonunda (bir kez) sahada kalır.</summary>
    public const string Morana = "morana";

    public static readonly IReadOnlyList<string> All = new[] { Svarog, Perun, Veles, Dazhbog, Stribog, Morana };

    /// <summary>Bir destede en fazla bu kadar rün (TASARIM.md §7: eşit rün bütçesi).</summary>
    public const int MaxPerDeck = 3;

    /// <summary>Rün takılabilir mi: kahraman olmayan birim (casus da olur).</summary>
    public static bool CanTake(CardDef d) => d.IsUnit && !d.Hero;

    /// <summary>Destedeki rünlerin hatalarını döner (boş = geçerli).</summary>
    public static List<string> Validate(Deck deck, CardCatalog catalog)
    {
        var errs = new List<string>();
        if (deck.Runes is not { Count: > 0 } runes) return errs;
        if (runes.Count > MaxPerDeck) errs.Add($"Bir destede en fazla {MaxPerDeck} rün olabilir.");
        foreach (var g in runes.GroupBy(r => r.CardId))
        {
            var copies = deck.CardIds.Count(id => id == g.Key);
            if (copies == 0) { errs.Add($"Rün takılan kart destede yok: {g.Key}."); continue; }
            if (g.Count() > copies) errs.Add($"{g.Key} kartının kopyasından fazla rün var.");
            if (!catalog.TryGet(g.Key, out var def) || !CanTake(def!)) errs.Add($"{g.Key} kartına rün takılamaz (yalnız kahraman olmayan birimler).");
        }
        foreach (var r in runes.Where(r => !All.Contains(r.Rune))) errs.Add($"Bilinmeyen rün: {r.Rune}.");
        return errs;
    }
}

public sealed partial class Game
{
    /// <summary>Destenin rünlerini kartlara uygular (deste karıştırılmadan önce, destedeki sıraya göre).</summary>
    private static void ApplyRunes(PlayerState ps, Deck deck)
    {
        if (deck.Runes == null) return;
        foreach (var r in deck.Runes)
        {
            var target = ps.Deck.FirstOrDefault(c => c.Def.Id == r.CardId && c.Rune == null && RuneIds.CanTake(c.Def));
            target?.SetRune(r.Rune);
        }
    }

    /// <summary>Yakılan kart: Veles rünü varsa (bir kez) ele döner; yoksa mezarlığa.</summary>
    private bool VelesSaves(CardInstance card)
    {
        if (card.Rune != RuneIds.Veles) return false;
        card.SetRune(null);
        _players[card.Holder].Hand.Add(card);
        Emit("rune_return", card.Holder, card.Uid, card.Def.Id);
        return true;
    }

    /// <summary>Raund sonu: Morana rünlü birimler (bir kez) sahada kalır.</summary>
    private void MoranaKeeps()
    {
        foreach (var c in BoardCards().Where(c => c.Rune == RuneIds.Morana && !c.NoRemove).ToList())
        {
            c.NoRemove = true;
            c.SetRune(null);
            Emit("rune_keep", c.Holder, c.Uid, c.Def.Id);
        }
    }
}
