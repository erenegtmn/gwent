using GwentCore;

namespace GwentAI;

public sealed record GameResult(int? Winner, int Rounds, int Steps, Game Game);

/// <summary>İki ajanı karşılaştırıp bir oyunu sonuna kadar oynatır (test, arena ve yerel oyun için).</summary>
public static class GameRunner
{
    public static GameResult Play(IAgent agent0, IAgent agent1, Deck deck0, Deck deck1, int seed,
        GameOptions? options = null, int maxSteps = 5000)
    {
        var g = new Game(deck0, deck1, seed, options);
        var agents = new[] { agent0, agent1 };
        var steps = 0;

        while (g.Phase != Phase.GameOver)
        {
            if (++steps > maxSteps) throw new InvalidOperationException($"Oyun {maxSteps} adımda bitmedi (seed {seed}).");
            var who = g.PlayersToAct();
            if (who.Count == 0) throw new InvalidOperationException("Kimse eylem beklemiyor ama oyun bitmedi.");
            var p = who[0];
            var action = agents[p].Decide(g, p);
            var res = g.Apply(action);
            if (!res.Ok) throw new InvalidOperationException($"Ajan {p} geçersiz eylem verdi: {action} → {res.Error} (seed {seed})");
        }
        return new GameResult(g.Outcome!.Winner, g.History.Count, steps, g);
    }
}
