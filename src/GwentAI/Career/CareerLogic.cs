using GwentCore;

namespace GwentAI.Career;

/// <summary>Bir maçın kariyere etkisi: kazanılan taç ve kartlar, ekranda gösterilecek kısa özet.</summary>
public sealed record CareerReward(int Crowns, IReadOnlyList<string> Cards, string Summary, bool TournamentOver = false, bool TournamentWon = false);

/// <summary>
/// Kariyer kuralları (arayüzden bağımsız, sınanabilir): başlangıç, bölge kilitleri, ödüller, tüccar, deste ve turnuva.
/// Ödül: bir rakibi ilk yenişte oyunun ona bağladığı benzersiz kart (varsa) + destesinden rastgele bir kart + taçlar;
/// sonraki galibiyetlerde daha az taç ve bazen bir kart. Koleksiyonda bir karttan en fazla kartın kopya sınırı kadar bulunur.
/// </summary>
public static class CareerLogic
{
    public const int StartCrowns = 100;
    public const string StarterLeader = "foltest_king_of_temeria";

    /// <summary>Yeni kariyer: Kuzey Krallıkları başlangıç destesi (oyunun kendi "North" destesi) ve 100 taç.</summary>
    public static CareerSave NewSave()
    {
        var s = new CareerSave { Crowns = StartCrowns };
        var starter = NpcDecks.Get("North", 1);
        var cards = starter.CardIds.ToList();
        cards.Add("blue_stripes_commando");   // orijinal başlangıç destesi 21 birimdir; 22'ye tamamlanır (üçüncü Mavi Çizgi: bağ üçlüsü)
        foreach (var id in cards) s.Add(id);
        s.Add(StarterLeader);
        ClampToCopies(s);
        s.Decks["northern_realms"] = new CareerDeck(StarterLeader, Expand(s, cards));
        s.ActiveFaction = "northern_realms";
        return s;
    }

    private static void ClampToCopies(CareerSave s)
    {
        foreach (var id in s.Cards.Keys.ToList())
            if (CardCatalog.Default.TryGet(id, out var d)) s.Cards[id] = Math.Min(s.Cards[id], d.Copies);
    }

    /// <summary>Deste listesini koleksiyondaki adetlere göre kırpar.</summary>
    private static List<string> Expand(CareerSave s, IEnumerable<string> ids)
    {
        var used = new Dictionary<string, int>();
        var list = new List<string>();
        foreach (var id in ids)
        {
            if (used.GetValueOrDefault(id) >= s.Owned(id)) continue;
            used[id] = used.GetValueOrDefault(id) + 1;
            list.Add(id);
        }
        return list;
    }

    // ---------- Bölgeler ----------

    public static int BeatenIn(CareerSave s, string region) => CareerWorld.In(region).Count(o => !o.IsTournament && s.Beaten(o.Id));

    public static bool IsUnlocked(CareerSave s, CareerRegion r)
    {
        var i = CareerWorld.Regions.ToList().IndexOf(r);
        if (i <= 0) return true;
        var prev = CareerWorld.Regions[i - 1];
        return IsUnlocked(s, prev) && BeatenIn(s, prev.Id) >= r.NeedWins;
    }

    // ---------- Koleksiyon ----------

    /// <summary>Toplanabilir kartlar (liderler dahil; jeton ve yeni nesil hariç).</summary>
    public static IEnumerable<CardDef> Collectible() =>
        CardCatalog.Default.All.Where(d => !d.Token && d.Edition != "nextgen" && (d.IsUnit || d.IsLeader || d.IsSpecial || d.IsWeather));

    public static (int Have, int Total) Progress(CareerSave s)
    {
        var all = Collectible().ToList();
        return (all.Count(d => s.Owned(d.Id) > 0), all.Count);
    }

    private static bool CanReceive(CareerSave s, string id) => CardCatalog.Default.TryGet(id, out var d) && !d.Token && s.Owned(id) < d.Copies;

    /// <summary>Destedeki kartlardan, koleksiyonda sınırı dolmamış rastgele biri (liderler hariç).</summary>
    private static string? RandomFrom(CareerSave s, IEnumerable<string> ids, Random rng, ICollection<string>? not = null)
    {
        var pool = ids.Distinct().Where(id => CanReceive(s, id) && (not == null || !not.Contains(id))).ToList();
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }

    // ---------- Maç ----------

    /// <summary>Rakibin bu maçtaki destesi (turnuvada turun destesi).</summary>
    public static Deck OpponentDeck(CareerSave s, CareerOpponent o)
    {
        var deck = o.IsTournament && s.Tournament is { } t && t.Opponent == o.Id && t.Round < t.Decks.Count ? t.Decks[t.Round] : o.Decks[0];
        return NpcDecks.Get(deck, o.Level);
    }

    /// <summary>Oynamak için geçerli deste var mı? Varsa null, yoksa nedeni.</summary>
    public static string? DeckProblem(CareerSave s)
    {
        var d = s.ActiveDeck();
        if (d == null) return "Henüz bir deste kurmadın.";
        var errs = DeckValidator.Validate(d);
        if (errs.Count > 0) return errs[0];
        foreach (var g in d.CardIds.GroupBy(x => x))
            if (g.Count() > s.Owned(g.Key)) return $"{CardCatalog.Default.Get(g.Key).Name}: koleksiyonunda yeterince yok.";
        if (s.Owned(d.LeaderId) == 0) return "Bu lider koleksiyonunda yok.";
        return null;
    }

    /// <summary>Düello sonucu. <paramref name="won"/>: true kazandın, false kaybettin, null berabere.</summary>
    public static CareerReward ApplyDuel(CareerSave s, CareerOpponent o, bool? won, Random rng)
    {
        if (o.IsTournament) throw new ArgumentException("Turnuva için ApplyTournamentMatch kullanılır.");
        var rec = s.Record(o.Id);
        if (won == false) { rec.Losses++; return new CareerReward(0, Array.Empty<string>(), $"{o.Name} bu sefer kazandı. Desteni gözden geçirip yeniden dene."); }
        if (won == null) return new CareerReward(0, Array.Empty<string>(), "Berabere: ödül yok, rövanş istenebilir.");

        var first = rec.Wins == 0;
        rec.Wins++;
        var cards = new List<string>();
        int crowns;
        var deckIds = NpcDecks.Get(o.Decks[0], o.Level).CardIds;
        if (first)
        {
            crowns = o.Prize;
            var special = NpcDecks.Reward(o.Decks[0]);
            if (special != null && CanReceive(s, special)) cards.Add(special);
            if (RandomFrom(s, deckIds, rng, cards) is { } extra) cards.Add(extra);
        }
        else
        {
            crowns = Math.Max(5, o.Prize / 3);
            if (rng.NextDouble() < 0.3 && RandomFrom(s, deckIds, rng) is { } extra) cards.Add(extra);
        }
        s.Crowns += crowns;
        foreach (var c in cards) s.Add(c);
        var names = cards.Select(c => CardCatalog.Default.Get(c).Name).ToList();
        var summary = $"+{crowns} taç" + (names.Count > 0 ? "  ·  Yeni kart: " + string.Join(", ", names) : "");
        return new CareerReward(crowns, cards, summary);
    }

    // ---------- Turnuva ----------

    /// <summary>Turnuvaya giriş: ücret alınır, turların desteleri karıştırılarak seçilir. Para yetmezse nedeni döner.</summary>
    public static string? EnterTournament(CareerSave s, CareerOpponent o, Random rng)
    {
        if (!o.IsTournament) throw new ArgumentException("Turnuva değil.");
        if (s.Tournament is { } t && t.Opponent == o.Id) return null;   // zaten sürüyor
        if (s.Crowns < o.EntryFee) return $"Katılım ücreti {o.EntryFee} taç; kesende {s.Crowns} var.";
        s.Crowns -= o.EntryFee;
        s.Tournament = new CareerTournamentRun { Opponent = o.Id, Round = 0, Decks = o.Decks.OrderBy(_ => rng.Next()).Take(o.Rounds).ToList() };
        return null;
    }

    /// <summary>Turnuva maçı sonucu: kazanınca sonraki tura geçilir; son turu kazanınca büyük ödül. Kaybedince turnuva biter (berabere: tur tekrar oynanır).</summary>
    public static CareerReward ApplyTournamentMatch(CareerSave s, CareerOpponent o, bool? won, Random rng)
    {
        var t = s.Tournament ?? throw new InvalidOperationException("Süren turnuva yok.");
        var rec = s.Record(o.Id);
        if (won == null) return new CareerReward(0, Array.Empty<string>(), $"Berabere: {t.Round + 1}. tur yeniden oynanacak.");
        if (won == false)
        {
            rec.Losses++;
            s.Tournament = null;
            return new CareerReward(0, Array.Empty<string>(), $"Turnuvadan {t.Round + 1}. turda elendin. Katılım ücretiyle yeniden deneyebilirsin.", TournamentOver: true);
        }
        t.Round++;
        if (t.Round < t.Decks.Count)
            return new CareerReward(0, Array.Empty<string>(), $"{t.Round}. turu kazandın! Sıradaki: {t.Round + 1}. tur ({(t.Round + 1 == t.Decks.Count ? "final" : "devam")}).");

        // Şampiyon
        var first = rec.Wins == 0;
        rec.Wins++;
        s.Tournament = null;
        var crowns = first ? o.Prize : o.Prize / 2;
        var cards = new List<string>();
        var specials = o.Decks.Select(NpcDecks.Reward).Where(x => x != null).Select(x => x!).ToList();
        if (RandomFrom(s, specials, rng) is { } sp) cards.Add(sp);
        var heroes = o.Decks.SelectMany(d => NpcDecks.Get(d, o.Level).CardIds).Where(id => CardCatalog.Default.Get(id).Hero);
        if (first && RandomFrom(s, heroes, rng, cards) is { } hero) cards.Add(hero);
        s.Crowns += crowns;
        foreach (var c in cards) s.Add(c);
        var names = cards.Select(c => CardCatalog.Default.Get(c).Name).ToList();
        return new CareerReward(crowns, cards, $"Şampiyon sensin! +{crowns} taç" + (names.Count > 0 ? "  ·  Yeni kart: " + string.Join(", ", names) : ""),
            TournamentOver: true, TournamentWon: true);
    }

    public static void AbandonTournament(CareerSave s) => s.Tournament = null;

    // ---------- Tüccar ----------

    /// <summary>
    /// Bölge tüccarının satışları: bölgenin fraksiyonlarının birimleri, liderleri ve tarafsız özel kartlar.
    /// Kahramanlar ve rakiplerin benzersiz ödül kartları satılmaz (yalnız masada kazanılır). Sınırı dolanlar listede yoktur.
    /// </summary>
    public static IEnumerable<CardDef> Stock(CareerSave s, CareerRegion r)
    {
        var rewards = CareerWorld.Opponents.SelectMany(o => o.Decks).Select(NpcDecks.Reward).Where(x => x != null).ToHashSet();
        return Collectible()
            .Where(d => !d.Hero && !rewards.Contains(d.Id))
            .Where(d => r.ShopFactions.Contains(d.Faction) || (d.Faction == "neutral" && !d.IsUnit))
            .Where(d => s.Owned(d.Id) < d.Copies)
            .OrderBy(d => d.IsLeader ? 0 : d.IsUnit ? 1 : 2).ThenBy(d => d.Faction).ThenByDescending(d => d.Strength).ThenBy(d => d.Name);
    }

    public static string? Buy(CareerSave s, CardDef d)
    {
        if (s.Owned(d.Id) >= d.Copies) return "Bu karttan yeterince var.";
        var price = NpcDecks.Price(d);
        if (s.Crowns < price) return $"Yetersiz taç: {price} gerekiyor.";
        s.Crowns -= price;
        s.Add(d.Id);
        return null;
    }

    // ---------- Deste ----------

    /// <summary>Koleksiyondan otomatik deste: sahip olunan en güçlü birimler (en az 22) ve birkaç özel kart.</summary>
    public static CareerDeck AutoDeck(CareerSave s, string faction, string leaderId)
    {
        var owned = Collectible().Where(d => s.Owned(d.Id) > 0 && (d.Faction == faction || d.Faction == "neutral") && !d.IsLeader).ToList();
        var cards = new List<string>();
        foreach (var d in owned.Where(d => d.IsUnit).OrderByDescending(d => d.Hero).ThenByDescending(d => d.Strength + (d.Has("bond") ? 2 : 0) + (d.Has("spy") ? 3 : 0) + (d.Has("medic") ? 3 : 0)))
            cards.AddRange(Enumerable.Repeat(d.Id, s.Owned(d.Id)));
        var specials = owned.Where(d => !d.IsUnit).OrderByDescending(d => d.Has("horn") || d.Has("scorch") || d.Has("decoy") ? 1 : 0)
            .SelectMany(d => Enumerable.Repeat(d.Id, s.Owned(d.Id))).Take(6);
        cards.AddRange(specials);
        return new CareerDeck(leaderId, cards);
    }
}
