using GwentAI;
using GwentAI.Draft;
using GwentCore;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Draft: rastgele seçimlerle her zaman geçerli deste çıkar, teklifler tekrarlanabilir, koşu kuralları.</summary>
public class DraftTests
{
    private static DraftRun Drafted(string faction, int seed, Func<List<string>, Random, string>? choose = null)
    {
        var rng = new Random(seed);
        var run = DraftLogic.Start(faction, seed);
        DraftLogic.PickLeader(run, run.LeaderOffer[rng.Next(run.LeaderOffer.Count)]);
        while (run.Phase == DraftPhase.Picking)
        {
            Assert.InRange(run.Offer.Count, 1, 3);
            Assert.Equal(run.Offer.Count, run.Offer.Distinct().Count());
            DraftLogic.PickCard(run, choose?.Invoke(run.Offer, rng) ?? run.Offer[rng.Next(run.Offer.Count)]);
        }
        return run;
    }

    [Fact]
    public void Any_choices_always_end_in_a_valid_deck()
    {
        var seed = 1;
        foreach (var f in DraftLogic.Factions)
            for (var i = 0; i < 40; i++)
            {
                // rastgele, hep özel kart seçen ve hep kahraman seçen oyuncular
                var greedy = (i % 3) switch
                {
                    1 => (Func<List<string>, Random, string>)((o, _) => o.FirstOrDefault(id => !CardCatalog.Default.Get(id).IsUnit) ?? o[0]),
                    2 => (o, _) => o.OrderByDescending(id => CardCatalog.Default.Get(id).Hero).First(),
                    _ => null
                };
                var run = Drafted(f, seed++, greedy);
                Assert.Equal(DraftLogic.DeckSize, run.Picks.Count);
                Assert.Empty(DeckValidator.Validate(run.Deck()));
                Assert.Equal(DraftPhase.Playing, run.Phase);
                Assert.NotNull(run.Next);
            }
    }

    [Fact]
    public void Offers_are_reproducible_from_the_saved_run()
    {
        var run = DraftLogic.Start("nilfgaard", 42);
        DraftLogic.PickLeader(run, run.LeaderOffer[0]);
        for (var i = 0; i < 10; i++) DraftLogic.PickCard(run, run.Offer[i % run.Offer.Count]);
        Assert.Equal(run.Offer, DraftLogic.MakeOffer(run));   // kayıttan devamda aynı teklif
    }

    [Fact]
    public void Run_ends_at_seven_wins_or_three_losses_and_pays_crowns()
    {
        var run = Drafted("monsters", 7);
        Assert.Equal(0, DraftLogic.ApplyResult(run, null).Crowns);
        Assert.Equal(0, run.Wins + run.Losses);
        DraftLogic.ApplyResult(run, true);
        DraftLogic.ApplyResult(run, false);
        DraftLogic.ApplyResult(run, false);
        var (_, crowns) = DraftLogic.ApplyResult(run, false);
        Assert.Equal(DraftPhase.Over, run.Phase);
        Assert.Equal(DraftLogic.CrownsPerWin, crowns);
        Assert.Throws<InvalidOperationException>(() => DraftLogic.ApplyResult(run, true));

        var perfect = Drafted("skellige", 8);
        var faced = new HashSet<string>();
        for (var i = 0; i < DraftLogic.MaxWins; i++)
        {
            Assert.True(faced.Add(perfect.Next!.Deck));                 // aynı rakiple iki kez karşılaşılmaz
            var r = DraftLogic.ApplyResult(perfect, true);
            if (i == DraftLogic.MaxWins - 1) Assert.Equal(DraftLogic.MaxWins * DraftLogic.CrownsPerWin + DraftLogic.PerfectBonus, r.Crowns);
        }
        Assert.Equal(DraftPhase.Over, perfect.Phase);
    }

    [Fact]
    public void A_drafted_deck_plays_against_its_npc_opponents()
    {
        var run = Drafted("scoiatael", 21);
        for (var m = 0; m < 3; m++)
        {
            var npc = GwentAI.Career.NpcDecks.Get(run.Next!.Deck, run.Next.Level);
            var replay = new ReplayData
            {
                MyFaction = run.Faction, MyLeaderId = run.LeaderId!, MyDeckCards = run.Picks.ToList(),
                OppFaction = npc.Faction, OppLeaderId = npc.LeaderId, OppDeckCards = npc.CardIds.ToList(), OppDeckUnchecked = true, Seed = 300 + m
            };
            var g = replay.CreateGame();
            var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(run.Next.Ai, m) };
            for (var step = 0; g.Phase != Phase.GameOver; step++)
            {
                Assert.True(step < 4000);
                var p = g.PlayersToAct()[0];
                Assert.True(g.Apply(agents[p].Decide(g, p)).Ok);
            }
            DraftLogic.ApplyResult(run, g.Outcome!.Winner == 0 ? true : g.Outcome.Winner == 1 ? false : null);
            if (run.Phase == DraftPhase.Over) break;
        }
    }
}
