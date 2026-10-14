using GwentAI;
using GwentCore;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Gaunter O'Dimm lanetleri: motor seçenekleri, deste dönüşümleri, günün laneti ve lanetli oyunların sonuna kadar oynanması.</summary>
public class CurseTests
{
    private static readonly string[] F = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private static Game New(int seed, params string[] curses)
    {
        var replay = new ReplayData
        {
            MyFaction = F[seed % 5], MyLeaderId = CardCatalog.Default.Leaders(F[seed % 5]).First().Id, MyDeckCards = DeckValidator.RecommendedDeck(F[seed % 5]).CardIds.ToList(),
            OppFaction = F[(seed + 2) % 5], OppLeaderId = CardCatalog.Default.Leaders(F[(seed + 2) % 5]).First().Id, Seed = seed, Difficulty = (int)Difficulty.Hard,
            Curses = curses.ToList()
        };
        return replay.CreateGame();
    }

    private static void PlayOut(Game g, int seed)
    {
        var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(Difficulty.Normal, seed) };
        for (var step = 0; g.Phase != Phase.GameOver; step++)
        {
            Assert.True(step < 4000, "oyun bitmedi");
            var p = g.PlayersToAct()[0];
            var r = g.Apply(agents[p].Decide(g, p));
            Assert.True(r.Ok, r.Error);
        }
    }

    [Fact]
    public void Short_candle_starts_with_eight_cards()
    {
        var g = New(1, Curses.ShortCandle);
        Assert.Equal(8, g.Players[0].Hand.Count);
        Assert.Equal(10, New(1).Players[0].Hand.Count);
    }

    [Fact]
    public void Endless_winter_opens_every_round_with_weather_that_belongs_to_nobody()
    {
        var g = New(3, Curses.EndlessWinter);
        PlayOut(g, 3);
        var weather = g.Events.Where(e => e.Kind == "curse_weather").ToList();
        Assert.Equal(g.History.Count, weather.Count);              // her raund bir hava
        Assert.All(g.Players, p => Assert.DoesNotContain(p.Grave, c => weather.Any(w => w.CardUid == c.Uid)));
    }

    [Fact]
    public void Blood_price_takes_a_card_from_the_round_winner()
    {
        var found = false;
        for (var seed = 1; seed < 12 && !found; seed++)
        {
            var g = New(seed, Curses.BloodPrice);
            PlayOut(g, seed);
            var evs = g.Events.ToList();
            foreach (var e in evs.Where(e => e.Kind == "curse_discard"))
            {
                var roundEnd = evs.Take(evs.IndexOf(e)).Last(x => x.Kind == "round_end");
                Assert.Equal(roundEnd.Player, e.Player);                  // kazanan kaybeder
                Assert.Contains(g.Players[e.Player].Grave, c => c.Uid == e.CardUid);
                found = true;
            }
        }
        Assert.True(found, "Kan Bedeli hiç işlemedi");
    }

    [Fact]
    public void No_heroes_and_mirror_transform_decks_validly()
    {
        foreach (var f in F)
        {
            var d = DeckValidator.RecommendedDeck(f);
            var nh = Curses.WithoutHeroes(d);
            Assert.DoesNotContain(nh.CardIds, id => CardCatalog.Default.Get(id).Hero);
            Assert.Empty(DeckValidator.Validate(nh));
        }
        var g = New(4, Curses.Mirror, Curses.NoHeroes);
        Assert.Equal(g.Players[0].Faction, g.Players[1].Faction);
        Assert.Equal(g.Players[0].Leader.Id, g.Players[1].Leader.Id);
        Assert.DoesNotContain(g.Players.SelectMany(p => p.Hand.Concat(p.Deck)), c => c.Def.Hero);
    }

    [Fact]
    public void All_curses_together_play_to_the_end()
    {
        for (var seed = 1; seed <= 15; seed++)
            PlayOut(New(seed, Curses.All.Select(c => c.Id).ToArray()), seed);
    }

    [Fact]
    public void Daily_curse_is_the_same_for_everyone_on_the_same_day()
    {
        var a = Curses.Daily(new DateOnly(2026, 10, 15));
        var b = Curses.Daily(new DateOnly(2026, 10, 15));
        Assert.Equal(a.Curses, b.Curses);
        Assert.Equal(a.OppLeaderId, b.OppLeaderId);
        Assert.Equal(2, a.Curses.Count);
        Assert.NotEqual(a.Seed, Curses.Daily(new DateOnly(2026, 10, 16)).Seed);
    }
}
