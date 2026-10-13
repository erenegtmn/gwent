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
            var d0 = DeckValidator.RecommendedDeck(f0);
            var d1 = DeckValidator.RecommendedDeck(f1);
            var seat = i % 2; // koltuk adaleti: a bazen 0, bazen 1
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
