using GwentAI;
using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

public class CloneTests
{
    [Fact]
    public void Clone_is_independent_and_behaves_identically()
    {
        var g = T.New("northern_realms", "nilfgaard");
        var u = g.TestHand(0, "ballista");
        var spy = g.TestHand(1, "vattier_de_rideaux");
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestDeck(1, "fiend"); g.TestDeck(1, "fiend");

        var clone = g.Clone();
        T.Play(clone, 0, clone.Players[0].Hand.First(c => c.Uid == u.Uid));

        // orijinal etkilenmedi
        Assert.Equal(0, g.PlayerTotal(0));
        Assert.Equal(6, clone.PlayerTotal(0));
        Assert.Equal(0, g.Current);

        // aynı eylem iki kopyada aynı sonucu verir
        T.Play(g, 0, u);
        Assert.Equal(clone.PlayerTotal(0), g.PlayerTotal(0));
        T.Play(g, 1, spy);
        T.Play(clone, 1, clone.Players[1].Hand.First(c => c.Uid == spy.Uid));
        Assert.Equal(g.Players[1].Hand.Count, clone.Players[1].Hand.Count);
        Assert.Equal(g.PlayerTotal(0), clone.PlayerTotal(0));
    }

    [Fact]
    public void Clone_continues_the_same_random_sequence()
    {
        var d = DeckValidator.DefaultDeck("monsters");
        var g = new Game(d, d, 99);
        var c = g.Clone();
        T.Ok(g, new Mulligan(0, g.Players[0].Hand.Take(2).Select(x => x.Uid).ToList()));
        T.Ok(c, new Mulligan(0, c.Players[0].Hand.Take(2).Select(x => x.Uid).ToList()));
        Assert.Equal(g.Players[0].Hand.Select(x => x.Uid), c.Players[0].Hand.Select(x => x.Uid));
    }

    [Fact]
    public void Clone_is_refused_while_a_choice_is_pending()
    {
        var g = T.New();
        g.TestGrave(0, "ballista");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);
        T.Play(g, 0, medic);
        Assert.NotNull(g.Pending);
        Assert.Throws<InvalidOperationException>(() => g.Clone());
    }

    [Fact]
    public void PlayersToAct_reports_who_must_move()
    {
        var d = DeckValidator.DefaultDeck("monsters");
        var g = new Game(d, d, 1);
        Assert.Equal(new[] { 0, 1 }, g.PlayersToAct());
        T.Ok(g, new Mulligan(0, Array.Empty<int>()));
        Assert.Equal(new[] { 1 }, g.PlayersToAct());
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(new[] { g.Current }, g.PlayersToAct());
    }
}

public class AiTests
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private static Game NewGame(int me = 0, string f0 = "northern_realms", string f1 = "monsters") => T.New(f0, f1, first: me);

    /// <summary>Rakip (1) ilk başlar ve hemen pas geçer; sıra bende.</summary>
    private static Game OppPassedGame(string f0 = "northern_realms", string f1 = "monsters")
    {
        var g = T.New(f0, f1, first: 1);
        T.Filler(g, 1);
        return g;
    }

    [Fact]
    public void Agent_passes_when_ahead_and_the_opponent_has_passed()
    {
        var g = OppPassedGame();
        g.TestBoard(0, "ballista", Siege);
        T.Filler(g, 0);
        T.Pass(g, 1);
        Assert.IsType<Pass>(new HeuristicAgent().Decide(g, 0));
    }

    [Fact]
    public void Agent_plays_the_cheapest_card_that_is_enough_when_opponent_passed()
    {
        var g = OppPassedGame();
        g.TestBoard(1, "fiend", Close);                       // rakip 6'da
        g.TestHand(0, "redanian_foot_soldier");                // 1
        g.TestHand(0, "keira_metz");                           // 5
        var tight = g.TestHand(0, "dethmold");                 // 6 → eşitler, yetmez
        var enough = g.TestHand(0, "sheldon_skaggs");          // 4 (yetmez)
        var bigger = g.TestHand(0, "philippa_eilhart");        // 10 hero → yeter
        T.Pass(g, 1);
        // Önde olmak için 7+ gerekir: yalnız Philippa tek hamlede yeter.
        var play = Assert.IsType<PlayCard>(new HeuristicAgent().Decide(g, 0));
        Assert.Equal(bigger.Uid, play.Card);
        _ = (tight, enough);
    }

    [Fact]
    public void Agent_prefers_the_smaller_of_two_sufficient_cards()
    {
        var g = OppPassedGame();
        g.TestBoard(1, "fiend", Close);                        // 6
        g.TestHand(0, "redanian_foot_soldier");
        var small = g.TestHand(0, "siege_tower");              // 6
        var enoughBut = g.TestHand(0, "blue_stripes_commando"); // 4
        g.TestBoard(0, "keira_metz", Ranged);                  // 5 (lead -1, need 2)
        g.TestHand(0, "esterad_thyssen");                      // 10 hero
        T.Pass(g, 1);
        var play = Assert.IsType<PlayCard>(new HeuristicAgent().Decide(g, 0));
        // 2+ puan yeter: en ucuz yeterli kart Blue Stripes (4) — Esterad (10) değil.
        Assert.Equal(enoughBut.Uid, play.Card);
        _ = small;
    }

    [Fact]
    public void Agent_concedes_a_hopeless_round_one_to_save_cards()
    {
        var g = NewGame();
        for (var i = 0; i < 4; i++) g.TestBoard(1, "frightener", Close); // rakip 20
        for (var i = 0; i < 3; i++) g.TestHand(0, "redanian_foot_soldier"); // potansiyel 3
        T.Filler(g, 1);
        g.TestBoard(0, "redanian_foot_soldier", Close);
        var a = new HeuristicAgent().Decide(g, 0);
        Assert.IsType<Pass>(a);
    }

    [Fact]
    public void Agent_never_concedes_when_losing_the_round_loses_the_game()
    {
        var g = NewGame();
        g.Players[0].GetType().GetProperty("Gems")!.SetValue(g.Players[0], 1);
        for (var i = 0; i < 4; i++) g.TestBoard(1, "frightener", Close);
        g.TestHand(0, "redanian_foot_soldier");
        g.TestHand(0, "redanian_foot_soldier");
        T.Filler(g, 1);
        var a = new HeuristicAgent().Decide(g, 0);
        Assert.IsType<PlayCard>(a);
    }

    [Fact]
    public void Agent_plays_a_spy_early_when_it_does_not_cost_the_lead()
    {
        var g = NewGame(f0: "nilfgaard", f1: "monsters");
        g.TestBoard(0, "cahir_mawr_dyffryn_aep_ceallach", Close); // 6, önde
        var spy = g.TestHand(0, "vattier_de_rideaux");             // 4
        g.TestDeck(0, "fiend"); g.TestDeck(0, "fiend");
        T.Filler(g, 0, 1);
        T.Filler(g, 1);
        var a = new HeuristicAgent().Decide(g, 0);
        var play = Assert.IsType<PlayCard>(a);
        Assert.Equal(spy.Uid, play.Card);
    }

    [Fact]
    public void Agent_does_not_play_a_spy_when_it_would_lose_the_lead_it_needs()
    {
        var g = OppPassedGame("nilfgaard", "monsters");
        g.TestBoard(1, "fiend", Close);                          // 6
        g.TestBoard(0, "cynthia", Ranged);                       // 4 → fark -2, +3 gerekir
        var spy = g.TestHand(0, "vattier_de_rideaux");           // 4 ama rakibe yazılır
        var unit = g.TestHand(0, "cahir_mawr_dyffryn_aep_ceallach"); // 6
        g.TestHand(0, "vreemde"); g.TestHand(0, "vreemde");
        g.TestDeck(0, "fiend"); g.TestDeck(0, "fiend");
        T.Pass(g, 1);
        var play = Assert.IsType<PlayCard>(new HeuristicAgent().Decide(g, 0));
        Assert.Equal(unit.Uid, play.Card);
        Assert.NotEqual(spy.Uid, play.Card);
    }

    [Fact]
    public void Agent_picks_the_strongest_card_with_medic_and_uses_sensible_rows()
    {
        var g = NewGame();
        var weak = g.TestGrave(0, "redanian_foot_soldier");
        var strong = g.TestGrave(0, "ballista");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0); T.Filler(g, 1);
        T.Play(g, 0, medic);
        var a = new HeuristicAgent().Decide(g, 0);
        var ch = Assert.IsType<Choose>(a);
        Assert.Equal(strong.Uid, ch.Cards[0]);
        T.Ok(g, a);
    }

    [Fact]
    public void Agent_avoids_a_weathered_row_for_agile_units()
    {
        var g = NewGame(f0: "northern_realms", f1: "monsters");
        g.TestWeather("biting_frost");
        g.TestGrave(0, "olaf");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0); T.Filler(g, 1);
        T.Play(g, 0, medic);
        var ch = Assert.IsType<Choose>(new HeuristicAgent().Decide(g, 0));
        Assert.Equal(Ranged, ch.Row);
    }

    [Fact]
    public void Agent_swaps_its_worst_cards_in_the_mulligan_but_keeps_valuable_ones()
    {
        var g = new Game(DeckValidator.DefaultDeck("northern_realms"), DeckValidator.DefaultDeck("monsters"), 11);
        var action = Assert.IsType<Mulligan>(new HeuristicAgent().Decide(g, 0));
        Assert.True(action.Cards.Count <= 2);
        foreach (var uid in action.Cards)
        {
            var c = g.Players[0].Hand.Single(x => x.Uid == uid);
            Assert.True(Valuation.CardValue(c.Def) < 3.5);
        }
        T.Ok(g, action);
    }

    [Fact]
    public void Agent_decisions_are_deterministic_for_the_same_state()
    {
        var g = NewGame();
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestHand(0, "ballista"); g.TestHand(0, "keira_metz");
        var agent = new HeuristicAgent();
        Assert.Equal(agent.Decide(g, 0), agent.Decide(g, 0));
    }

    [Fact]
    public void Agent_scoiatael_lets_the_opponent_start()
    {
        var g = new Game(DeckValidator.DefaultDeck("scoiatael"), DeckValidator.DefaultDeck("monsters"), 3);
        T.Ok(g, new Mulligan(0, Array.Empty<int>()));
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(Phase.ChooseFirst, g.Phase);
        T.Ok(g, new HeuristicAgent().Decide(g, 0));
        Assert.Equal(1, g.Current);
    }

    [Theory]
    [InlineData("northern_realms")]
    [InlineData("nilfgaard")]
    [InlineData("scoiatael")]
    [InlineData("monsters")]
    [InlineData("skellige")]
    public void Agent_beats_a_random_player_most_of_the_time(string faction)
    {
        var wins = 0; var games = 0;
        foreach (var opp in Factions)
        {
            for (var seed = 1; seed <= 8; seed++)
            {
                var d0 = DeckValidator.DefaultDeck(faction);
                var d1 = DeckValidator.DefaultDeck(opp);
                var seat = seed % 2; // yerler dönüşümlü
                var agents = new IAgent[2];
                agents[seat] = new HeuristicAgent();
                agents[1 - seat] = new RandomAgent(seed);
                var decks = seat == 0 ? (d0, d1) : (d1, d0);
                var res = GameRunner.Play(agents[0], agents[1], decks.Item1, decks.Item2, seed * 31 + 7);
                games++;
                if (res.Winner == seat) wins++;
            }
        }
        Assert.True(wins >= games * 0.7, $"{faction}: {wins}/{games} galibiyet");
    }

    [Fact]
    public void Two_agents_finish_games_without_illegal_moves_for_every_faction_pair()
    {
        foreach (var a in Factions)
            foreach (var b in Factions)
                for (var seed = 1; seed <= 4; seed++)
                {
                    var res = GameRunner.Play(new HeuristicAgent(), new HeuristicAgent(),
                        DeckValidator.DefaultDeck(a), DeckValidator.DefaultDeck(b), seed, new GameOptions { NextGen = seed % 2 == 0 });
                    Assert.True(res.Rounds is >= 2 and <= 3, $"{a} vs {b} seed {seed}: {res.Rounds} raund");
                }
    }

    /// <summary>
    /// Bekçi: yapay zekâ rakibin elini/destesini yalnız SAYI olarak kullanabilir. Kart kimliğine bakarsa
    /// "hile yapan" bir rakip olur ve oyun keyifsizleşir.
    /// </summary>
    [Fact]
    public void Agent_never_inspects_the_opponents_hidden_cards()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Gwent.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "src", "GwentAI", "HeuristicAgent.cs"));

        // opp.Hand / opp.Deck ve Players[1 - me].Hand / .Deck yalnız .Count ile kullanılabilir
        var bad = System.Text.RegularExpressions.Regex.Matches(src,
            @"(opp|Players\[1 - me\])\.(Hand|Deck)(?!\.Count)");
        Assert.True(bad.Count == 0, "Rakibin gizli kartlarına sayı dışında bakılıyor: " + string.Join(", ", bad.Select(m => m.Value)));
    }
}
