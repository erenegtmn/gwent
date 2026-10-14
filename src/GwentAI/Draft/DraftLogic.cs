using GwentAI.Career;
using GwentCore;

namespace GwentAI.Draft;

public enum DraftPhase { Leader, Picking, Playing, Over }

/// <summary>Draft rakibi: oyunun bir NPC destesi, ad ve portre.</summary>
public sealed record DraftOpponent(string Deck, string Name, string? Face, int Level, Difficulty Ai);

/// <summary>
/// Bir draft koşusu: fraksiyon, lider, seçilen kartlar, o anki teklif (3 kart), galibiyet/yenilgi ve sıradaki rakip.
/// Teklifler koşunun tohumundan ve seçim sırasından üretilir: aynı koşu her açılışta aynı teklifi gösterir. Kaydedilir (draft.json).
/// </summary>
public sealed class DraftRun
{
    public int Version { get; set; } = 1;
    public int Seed { get; set; }
    public string Faction { get; set; } = "";
    public string? LeaderId { get; set; }
    public List<string> LeaderOffer { get; set; } = new();
    public List<string> Picks { get; set; } = new();
    public List<string> Offer { get; set; } = new();
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DraftOpponent? Next { get; set; }
    public List<string> Faced { get; set; } = new();
    public DateTime Started { get; set; } = DateTime.UtcNow;

    public DraftPhase Phase =>
        LeaderId == null ? DraftPhase.Leader
        : Picks.Count < DraftLogic.DeckSize ? DraftPhase.Picking
        : Wins >= DraftLogic.MaxWins || Losses >= DraftLogic.MaxLosses ? DraftPhase.Over
        : DraftPhase.Playing;

    public Deck Deck() => new(Faction, LeaderId ?? "", Picks);
}

/// <summary>
/// Draft kuralları (arayüzden bağımsız, sınanabilir). Hazır deste yok: lider 3 seçenekten, deste 25 kez 3 karttan birini seçerek kurulur.
/// Teklifler her zaman geçerli bir deste çıkacak biçimde verilir (22 birim tamamlanacak kadar seçim kaldıysa yalnız birim; en fazla 10 özel kart;
/// kopya sınırı). Kahraman seyrek gelir. Sonra rakiplerle oynanır: 7 galibiyet ya da 3 yenilgide koşu biter; rakipler galibiyetle zorlaşır.
/// </summary>
public static class DraftLogic
{
    public const int DeckSize = 25;
    public const int MaxWins = 7;
    public const int MaxLosses = 3;
    public const int CrownsPerWin = 25;
    public const int PerfectBonus = 75;

    public static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    public static DraftRun Start(string faction, int seed)
    {
        var run = new DraftRun { Faction = faction, Seed = seed };
        var rng = new Random(seed);
        run.LeaderOffer = CardCatalog.Default.Leaders(faction).Where(l => l.Edition != "nextgen").OrderBy(_ => rng.Next()).Take(3).Select(l => l.Id).ToList();
        return run;
    }

    public static void PickLeader(DraftRun run, string leaderId)
    {
        if (run.LeaderId != null || !run.LeaderOffer.Contains(leaderId)) throw new InvalidOperationException("Bu lider seçilemez.");
        run.LeaderId = leaderId;
        run.Offer = MakeOffer(run);
    }

    public static void PickCard(DraftRun run, string cardId)
    {
        if (run.Phase != DraftPhase.Picking || !run.Offer.Contains(cardId)) throw new InvalidOperationException("Bu kart seçilemez.");
        run.Picks.Add(cardId);
        run.Offer = run.Picks.Count < DeckSize ? MakeOffer(run) : new List<string>();
        if (run.Phase == DraftPhase.Playing) run.Next = NextOpponent(run);
    }

    /// <summary>Sıradaki teklif: tohum + seçim sırası → hep aynı (kayıttan devamda değişmez).</summary>
    public static List<string> MakeOffer(DraftRun run)
    {
        var rng = new Random(unchecked(run.Seed * 7919 + run.Picks.Count * 104729 + 17));
        var picked = run.Picks.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var units = run.Picks.Count(id => CardCatalog.Default.Get(id).IsUnit);
        var specials = run.Picks.Count - units;
        var left = DeckSize - run.Picks.Count;
        var unitsOnly = DeckValidator.MinUnits - units >= left || specials >= DeckValidator.MaxSpecials;

        var pool = DeckValidator.Pool(run.Faction)
            .Where(d => picked.GetValueOrDefault(d.Id) < d.Copies && (d.IsUnit || !unitsOnly))
            .ToList();
        double Weight(CardDef d) => d.IsUnit ? (d.Hero ? 0.6 : d.Faction == "neutral" ? 2.5 : 5) : d.IsWeather ? 1.2 : 1.6;

        var offer = new List<string>();
        var specialInOffer = false;
        for (var k = 0; k < 3 && pool.Count > 0; k++)
        {
            var cands = pool.Where(d => !offer.Contains(d.Id) && (d.IsUnit || !specialInOffer)).ToList();   // teklifte en çok bir özel kart
            if (cands.Count == 0) break;
            var total = cands.Sum(Weight);
            var r = rng.NextDouble() * total;
            var chosen = cands[^1];
            foreach (var d in cands) { r -= Weight(d); if (r <= 0) { chosen = d; break; } }
            offer.Add(chosen.Id);
            if (!chosen.IsUnit) specialInOffer = true;
        }
        return offer;
    }

    /// <summary>Galibiyete göre zorlaşan rakip: oyunun NPC destelerinden, daha önce karşılaşılmamış biri.</summary>
    public static DraftOpponent NextOpponent(DraftRun run)
    {
        var rng = new Random(unchecked(run.Seed * 31 + (run.Wins + run.Losses) * 977 + 3));
        var level = run.Wins < 2 ? 1 : run.Wins < 5 ? 2 : 3;
        var ai = run.Wins < 1 ? Difficulty.Easy : run.Wins < 4 ? Difficulty.Normal : Difficulty.Hard;
        var named = CareerWorld.Opponents.Where(o => !o.IsTournament && !run.Faced.Contains(o.Decks[0])).ToList();
        if (named.Count == 0) named = CareerWorld.Opponents.Where(o => !o.IsTournament).ToList();
        var o = named[rng.Next(named.Count)];
        return new DraftOpponent(o.Decks[0], o.Name, o.Face, level, ai);
    }

    /// <summary>Maç sonucu (true kazandın, false kaybettin, null berabere: aynı rakiple yeniden). Koşu bittiyse kazanılan Crown'u döner.</summary>
    public static (string Summary, int Crowns) ApplyResult(DraftRun run, bool? won)
    {
        if (run.Phase != DraftPhase.Playing) throw new InvalidOperationException("Oynanacak maç yok.");
        if (won == null) return ("Berabere: aynı rakiple yeniden oynanacak.", 0);
        if (run.Next != null) run.Faced.Add(run.Next.Deck);
        if (won == true) run.Wins++; else run.Losses++;
        if (run.Phase == DraftPhase.Over)
        {
            run.Next = null;
            var crowns = Reward(run);
            return (run.Wins >= MaxWins ? $"Kusursuz koşu: {MaxWins} galibiyet! +{crowns} Crown" : $"Koşu bitti: {run.Wins} galibiyet, {run.Losses} yenilgi. +{crowns} Crown", crowns);
        }
        run.Next = NextOpponent(run);
        return (won == true ? $"Kazandın! {run.Wins} galibiyet" : $"Kaybettin. {MaxLosses - run.Losses} hakkın kaldı", 0);
    }

    public static int Reward(DraftRun run) => run.Wins * CrownsPerWin + (run.Wins >= MaxWins ? PerfectBonus : 0);

    /// <summary>Destenin özeti: birim, özel, kahraman sayısı ve sıra dağılımı.</summary>
    public static (int Units, int Specials, int Heroes, int Close, int Ranged, int Siege) Summary(DraftRun run)
    {
        var defs = run.Picks.Select(CardCatalog.Default.Get).ToList();
        return (defs.Count(d => d.IsUnit), defs.Count(d => !d.IsUnit), defs.Count(d => d.Hero),
            defs.Count(d => d.Row == "close"), defs.Count(d => d.Row == "ranged" || d.Row == "agile"), defs.Count(d => d.Row == "siege"));
    }
}
