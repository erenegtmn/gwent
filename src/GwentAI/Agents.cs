using GwentCore;

namespace GwentAI;

/// <summary>
/// Bir oyuncunun yerine karar veren varlık. <see cref="Decide"/> yalnız o oyuncunun
/// eylemde olduğu anlarda çağrılır (<see cref="Game.PlayersToAct"/>).
/// Ajanlar rakibin elini ve destesini GÖRMEMELİ: yalnız kart sayılarına bakabilirler.
/// </summary>
public interface IAgent
{
    GameAction Decide(Game game, int me);
}

/// <summary>Geçerli eylemlerden rastgele seçer. Karşılaştırma tabanı ve test için.</summary>
public sealed class RandomAgent : IAgent
{
    private readonly Random _rng;
    public RandomAgent(int seed = 0) => _rng = new Random(seed);

    public GameAction Decide(Game game, int me)
    {
        var legal = game.LegalActions().Where(a => a.Player == me).ToList();
        return legal[_rng.Next(legal.Count)];
    }
}
