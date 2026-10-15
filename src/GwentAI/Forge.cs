using GwentCore;

namespace GwentAI;

/// <summary>Rünün adı (W3'teki gibi), simgesi ve kısa kuralı.</summary>
public sealed record RuneInfo(string Id, string Name, string Glyph, string Color, string Text);

/// <summary>Kaer Morhen demircisi (TASARIM.md §4.3): rün bilgileri ve yapay zekâ destelerinin rünleri.</summary>
public static class Forge
{
    public static readonly IReadOnlyList<RuneInfo> All = new[]
    {
        new RuneInfo(RuneIds.Svarog, "Svarog", "ᛋ", "#e8a040", "+1 güç."),
        new RuneInfo(RuneIds.Perun, "Perun", "ᛈ", "#7ab8f0", "Hava etkisinden muaf."),
        new RuneInfo(RuneIds.Veles, "Veles", "ᚹ", "#9ad070", "Yakılırsa (bir kez) mezarlık yerine ele döner."),
        new RuneInfo(RuneIds.Dazhbog, "Dazhbog", "ᛞ", "#f0d060", "Moral: satırdaki diğer birimlere +1."),
        new RuneInfo(RuneIds.Stribog, "Stribog", "ᛏ", "#c0a0f0", "Komutanın satırındaysa +2."),
        new RuneInfo(RuneIds.Morana, "Morana", "ᛗ", "#e07080", "Raund sonunda (bir kez) sahada kalır."),
    };

    public static RuneInfo Get(string id) => All.First(r => r.Id == id);

    /// <summary>Zorluğa göre yapay zekânın rün sayısı (oyuncu en fazla <see cref="RuneIds.MaxPerDeck"/>).</summary>
    public static int AiRuneCount(Difficulty d) => d switch { Difficulty.Easy => 0, Difficulty.Normal => 2, _ => RuneIds.MaxPerDeck };

    /// <summary>
    /// Yapay zekâ destesine deterministik rün: en güçlü (kahraman/casus olmayan) birimlere sırayla Svarog, Perun, Veles.
    /// </summary>
    public static IReadOnlyList<CardRune> AutoRunes(Deck deck, int count)
    {
        var order = new[] { RuneIds.Svarog, RuneIds.Perun, RuneIds.Veles };
        var cat = CardCatalog.Default;
        var picks = deck.CardIds.Select(id => cat.Get(id)).Where(d => RuneIds.CanTake(d) && !d.Has("spy"))
            .OrderByDescending(d => d.Strength).ThenBy(d => d.Id, StringComparer.Ordinal).Take(Math.Min(count, order.Length)).ToList();
        return picks.Select((d, i) => new CardRune(d.Id, order[i])).ToList();
    }
}
