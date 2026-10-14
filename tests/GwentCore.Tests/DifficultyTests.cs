using GwentAI;
using GwentCore;
using Xunit;
using Xunit.Abstractions;

namespace GwentCore.Tests;

/// <summary>Zorluk sırası: Zor, Normal'i; Normal, Kolay'ı yenmeli (sabit tohumlarla, yani kararlı).</summary>
public class DifficultyTests
{
    private readonly ITestOutputHelper _out;
    public DifficultyTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private double WinRate(Difficulty a, Difficulty b, int games)
    {
        var wins = 0.0;
        var decided = 0;
        for (var i = 0; i < games; i++)
        {
            var f0 = Factions[i % 5];
            var f1 = Factions[(i / 5 + 2) % 5];
            var seat = i % 2; // koltuk adaleti: a bazen 0, bazen 1
            // Zorluk = ajan + deste (hızlı maçta yapay zekâ zorluğuna göre deste alır)
            var da = AiDecks.For(seat == 0 ? f0 : f1, null, CardCatalog.Default.Leaders(seat == 0 ? f0 : f1).First().Id, a);
            var db = AiDecks.For(seat == 0 ? f1 : f0, null, CardCatalog.Default.Leaders(seat == 0 ? f1 : f0).First().Id, b);
            var d0 = seat == 0 ? da : db;
            var d1 = seat == 0 ? db : da;
            var seed = 9000 + i;
            var ag = AgentFactory.Create(a, seed);
            var bg = AgentFactory.Create(b, seed + 1);
            var r = seat == 0
                ? GameRunner.Play(ag, bg, d0, d1, seed)
                : GameRunner.Play(bg, ag, d0, d1, seed);
            if (r.Winner == null) continue;
            decided++;
            if (r.Winner == seat) wins++;
        }
        return decided == 0 ? 0 : wins / decided;
    }

    [Fact]
    public void No_difficulty_dumps_cards_when_ahead_after_the_opponent_passed()
    {
        // Kolaylık saçma hamleyle verilmez: rakip pas geçmişken öndeyse her zorlukta pas geçer
        foreach (var d in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            for (var i = 0; i < 25; i++)
            {
                var f0 = Factions[i % 5];
                var f1 = Factions[(i + 2) % 5];
                var g = new Game(AiDecks.For(f0, null, CardCatalog.Default.Leaders(f0).First().Id, d),
                    AiDecks.For(f1, null, CardCatalog.Default.Leaders(f1).First().Id, Difficulty.Hard), 500 + i);
                var agents = new[] { AgentFactory.Create(d, 500 + i), AgentFactory.Create(Difficulty.Hard, 900 + i) };
                for (var step = 0; g.Phase != Phase.GameOver && step < 3000; step++)
                {
                    var p = g.PlayersToAct()[0];
                    var a = agents[p].Decide(g, p);
                    if (p == 0 && g.Phase == Phase.Playing && g.Pending == null && g.Players[1].Passed && g.PlayerTotal(0) > g.PlayerTotal(1))
                        Assert.IsType<Pass>(a);
                    Assert.True(g.Apply(a).Ok);
                }
            }
    }

    [Fact]
    public void Hard_beats_Easy_clearly()
    {
        var rate = WinRate(Difficulty.Hard, Difficulty.Easy, 60);
        _out.WriteLine($"Hard vs Easy: {rate:P0}");
        Assert.True(rate >= 0.7, $"Hard vs Easy {rate:P0}");
    }

    [Fact]
    public void Hard_beats_Normal()
    {
        var rate = WinRate(Difficulty.Hard, Difficulty.Normal, 60);
        _out.WriteLine($"Hard vs Normal: {rate:P0}");
        Assert.True(rate >= 0.54, $"Hard vs Normal {rate:P0}");
    }

    [Fact]
    public void Normal_beats_Easy()
    {
        var rate = WinRate(Difficulty.Normal, Difficulty.Easy, 60);
        _out.WriteLine($"Normal vs Easy: {rate:P0}");
        Assert.True(rate >= 0.6, $"Normal vs Easy {rate:P0}");
    }
}
