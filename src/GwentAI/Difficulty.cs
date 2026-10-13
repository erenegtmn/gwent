using GwentCore;

namespace GwentAI;

/// <summary>Yapay zekâ zorluğu. Zor = sweep ile ayarlanmış tempo ajanı; Kolay ve Normal ona rastgele hatalar ekler.</summary>
public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }

/// <summary>
/// Asıl ajanın kararlarının bir kısmını geçerli eylemlerden rastgele biriyle değiştirir ("hata yapar").
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
        var mistake = g.Phase switch
        {
            Phase.Mulligan => _mulliganMistakes && _rng.NextDouble() < 0.7,
            Phase.Playing when g.Pending == null => _rng.NextDouble() < _turnMistakes,
            _ => false
        };
        if (!mistake) return _inner.Decide(g, me);
        var legal = g.LegalActions().Where(a => a.Player == me).ToList();
        return legal.Count == 0 ? _inner.Decide(g, me) : legal[_rng.Next(legal.Count)];
    }
}

public static class AgentFactory
{
    public static IAgent Create(Difficulty d, int seed) => d switch
    {
        Difficulty.Hard => new HeuristicAgent(),
        Difficulty.Normal => new NoisyAgent(new HeuristicAgent(), 0.35, false, seed + 101),
        _ => new NoisyAgent(new HeuristicAgent(), 0.8, true, seed + 101)
    };
}
