using GwentCore;

namespace GwentAI;

/// <summary>Yapay zekâ zorluğu. Zor = sweep ile ayarlanmış tempo ajanı; Kolay ve Normal ona rastgele hatalar ekler.</summary>
public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }

/// <summary>
/// Asıl ajanın kararlarının bir kısmını daha zayıf ama **akla yatkın** bir hamleyle değiştirir ("hata yapar"): yanlış kartı ya da sırayı seçer.
/// Hiçbir zaman saçma hamle yapmaz: asıl ajan pas geçmek istiyorsa pas geçer (önündeyken ve rakip pas geçmişken kart dökmez),
/// asıl ajan oynamak istiyorsa pasa çevirmez, lider yeteneğini rastgele harcamaz.
/// Seçim bekleyen durumlarda (Medic hedefi vb.) hata yapmaz; kart değiştirmede yalnız Kolay'da hata yapar.
/// Yalnız kendi geçerli eylemlerine bakar: rakibin elini görmez.
/// </summary>
public sealed class NoisyAgent : IAgent
{
    private readonly IAgent _inner;
    private readonly double _turnMistakes;
    private readonly bool _mulliganMistakes;
    private readonly Random _rng;

    public NoisyAgent(IAgent inner, double turnMistakes, bool mulliganMistakes, int seed)
    {
        _inner = inner;
        _turnMistakes = turnMistakes;
        _mulliganMistakes = mulliganMistakes;
        _rng = new Random(seed);
    }

    public GameAction Decide(Game g, int me)
    {
        var best = _inner.Decide(g, me);
        switch (g.Phase)
        {
            case Phase.Mulligan:
                if (!_mulliganMistakes || _rng.NextDouble() >= 0.7) return best;
                var mull = g.LegalActions().Where(a => a.Player == me).ToList();
                return mull.Count == 0 ? best : mull[_rng.Next(mull.Count)];
            case Phase.Playing when g.Pending == null:
                if (best is Pass or UseLeader || _rng.NextDouble() >= _turnMistakes) return best;
                // Başka bir kart (ya da aynı kart başka sıraya): pas ve lider hata olarak seçilmez
                var plays = g.LegalActions().Where(a => a.Player == me && a is not Pass && a is not UseLeader).ToList();
                return plays.Count == 0 ? best : plays[_rng.Next(plays.Count)];
            default:
                return best;
        }
    }
}

public static class AgentFactory
{
    /// <summary>
    /// Kolay ve Normal, Zor'un aynı mantığına acemi zaafları eklenmiş hâlidir: kaybedilmiş raundu bırakmayıp kart harcamaya devam eder,
    /// rakip pas geçmeden fazla öne geçmeye çalışır, gereğinden güçlü kart oynar, lideri erken kullanır; arada yanlış kart/sıra seçer.
    /// Hiçbir zorlukta saçma pas ya da önündeyken (rakip pas geçmişken) kart dökme yoktur.
    /// </summary>
    public static readonly AiTuning EasyTuning = new() { ConcedeFactor = 10, PassAheadFactor = 1.8, LeaderCost = 0, MulliganBelow = 0, Overcommit = 0.75 };
    public static readonly AiTuning NormalTuning = new() { ConcedeFactor = 1.2, PassAheadFactor = 1.1, LeaderCost = 1, Overcommit = 0.4 };

    public static IAgent Create(Difficulty d, int seed) => d switch
    {
        Difficulty.Hard => new HeuristicAgent(),
        Difficulty.Normal => new NoisyAgent(new HeuristicAgent(NormalTuning, seed + 7), 0.2, false, seed + 101),
        _ => new NoisyAgent(new HeuristicAgent(EasyTuning, seed + 7), 0.5, true, seed + 101)
    };
}

/// <summary>
/// Hızlı maçta yapay zekânın destesi zorluğa göre (orijinal oyun da NPC destelerinin kolay/normal/zor sürümleriyle zorluk verir):
/// Zor = önerilen deste (tüm kahramanlar, 3 casus, Yem/Yak/Boynuz/Açık hava); Normal = en fazla 2 kahraman ve 1 casus;
/// Kolay = kahramansız, casussuz, az özel kart. Hepsi kurallara uygun, geçerli destelerdir.
/// </summary>
public static class AiDecks
{
    public static Deck For(string faction, GameOptions? options, string leaderId, Difficulty d)
    {
        options ??= new GameOptions();
        if (d == Difficulty.Hard) return DeckValidator.RecommendedDeck(faction, options, null, leaderId);
        var (heroes, spies, size) = d == Difficulty.Normal ? (2, 1, 24) : (0, 0, 22);
        var units = DeckValidator.Pool(faction, options).Where(u => u.IsUnit).ToList();
        var chosen = new Dictionary<string, int>();
        int Count() => chosen.Values.Sum();
        void Take(CardDef c) => chosen[c.Id] = c.Copies;

        foreach (var c in units.Where(u => u.Hero).OrderByDescending(u => u.Strength).Take(heroes)) Take(c);
        foreach (var c in units.Where(u => u.Has("spy") && !u.Hero).OrderByDescending(u => u.Strength).Take(spies)) Take(c);
        foreach (var c in units.Where(u => !u.Hero && !u.Has("spy")).OrderByDescending(u => u.Strength + (u.Has("bond") ? u.Copies : 0)))
        {
            if (Count() >= size) break;
            Take(c);
        }
        foreach (var g in units.Where(u => chosen.ContainsKey(u.Id) && u.MusterGroup != null).Select(u => u.MusterGroup!).Distinct().ToList())
            foreach (var c in units.Where(u => u.MusterGroup == g && !u.Hero)) Take(c);
        foreach (var c in units.OrderBy(u => u.Hero).ThenByDescending(u => u.Strength))
        {
            if (Count() >= DeckValidator.MinUnits) break;
            if (!chosen.ContainsKey(c.Id)) Take(c);
        }
        var ids = chosen.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        ids.AddRange(d == Difficulty.Normal ? new[] { Ids.Decoy, Ids.Horn, Ids.ClearWeather } : new[] { Ids.ClearWeather, "biting_frost" });
        return new Deck(faction, leaderId, ids);
    }
}
