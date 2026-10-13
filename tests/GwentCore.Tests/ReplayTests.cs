using GwentAI;
using GwentCore;
using Xunit;

namespace GwentCore.Tests;

public class ReplayTests
{
    [Theory]
    [InlineData("northern_realms", "foltest_the_siegemaster", "monsters", "eredin_commander_of_the_red_riders", 5)]
    [InlineData("skellige", "king_bran", "nilfgaard", "emhyr_var_emreis_his_imperial_majesty", 21)]
    [InlineData("scoiatael", "francesca_findabair_pureblood_elf", "northern_realms", "foltest_king_of_temeria", 77)]
    public void Recorded_match_replays_to_the_identical_game(string f0, string l0, string f1, string l1, int seed)
    {
        var rd = new ReplayData { MyFaction = f0, MyLeaderId = l0, OppFaction = f1, OppLeaderId = l1, Seed = seed };
        var g = rd.CreateGame();
        var agents = new IAgent[] { AgentFactory.Create(Difficulty.Normal, seed), AgentFactory.Create(Difficulty.Hard, seed) };
        for (var i = 0; i < 4000 && g.Phase != Phase.GameOver; i++)
        {
            var p = g.PlayersToAct()[0];
            var a = agents[p].Decide(g, p);
            Assert.True(g.Apply(a).Ok);
            rd.Actions.Add(ReplayData.Encode(a));
        }
        // dosyadan geçirip yeniden oynat
        var again = ReplayData.FromJson(rd.ToJson()).Run();
        Assert.Equal(g.Events.Count, again.Events.Count);
        Assert.Equal(g.Phase, again.Phase);
        Assert.Equal(g.Outcome?.Winner, again.Outcome?.Winner);
        Assert.Equal(g.History.Select(h => (h.Score0, h.Score1)), again.History.Select(h => (h.Score0, h.Score1)));
        for (var i = 0; i < g.Events.Count; i++) Assert.Equal(g.Events[i], again.Events[i]);
    }
}
