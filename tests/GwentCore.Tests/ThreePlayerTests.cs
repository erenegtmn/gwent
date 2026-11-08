using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

/// <summary>Üç kişilik online maç kuralları (KURALLAR §24).</summary>
public class ThreePlayerTests
{
    private static Game New3(string f0 = "northern_realms", string f1 = "monsters", string f2 = "skellige",
        string? l0 = null, string? l1 = null, string? l2 = null, int first = 0, int seed = 1, bool empty = true)
    {
        var decks = new[] { f0, f1, f2 }.Select(f => DeckValidator.DefaultDeck(f)).ToArray();
        if (l0 != null) decks[0] = decks[0] with { LeaderId = l0 };
        if (l1 != null) decks[1] = decks[1] with { LeaderId = l1 };
        if (l2 != null) decks[2] = decks[2] with { LeaderId = l2 };
        var g = new Game(decks, seed);
        if (!empty) return g;
        g.TestEmpty();
        g.TestStart(first);
        return g;
    }

    private static void FillAll(Game g)
    {
        for (var p = 0; p < 3; p++) T.Filler(g, p);
    }

    [Fact]
    public void Three_decks_deal_three_hands_and_roll_dice()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var g = New3(seed: seed, empty: false);
            Assert.Equal(3, g.PlayerCount);
            Assert.All(g.Players, p => Assert.Equal(10, p.Hand.Count));
            Assert.NotEmpty(g.DiceRolls);
            var last = g.DiceRolls[^1];
            var max = last.Max();
            Assert.Single(last, v => v == max);
            Assert.Equal(Array.IndexOf(last, max), g.CoinFirst);
            Assert.All(g.DiceRolls, r => Assert.All(r, v => Assert.InRange(v, 0, 6)));

            for (var p = 0; p < 3; p++) T.Ok(g, new Mulligan(p, Array.Empty<int>()));
            Assert.Equal(Phase.Playing, g.Phase);
            Assert.Equal(g.CoinFirst, g.Current);
            Assert.Contains(g.Events, e => e.Kind == "dice");
            Assert.DoesNotContain(g.Events, e => e.Kind == "coin_toss");
        }
    }

    [Fact]
    public void Dice_ties_reroll_only_among_the_tied()
    {
        var found = false;
        for (var seed = 1; seed <= 400 && !found; seed++)
        {
            var g = New3(seed: seed, empty: false);
            if (g.DiceRolls.Count < 2) continue;
            found = true;
            var first = g.DiceRolls[0];
            var max = first.Max();
            for (var p = 0; p < 3; p++)
                Assert.Equal(first[p] == max, g.DiceRolls[1][p] > 0);
        }
        Assert.True(found, "Eşit zar hiç çıkmadı");
    }

    [Fact]
    public void Single_scoiatael_chooses_first_player_after_dice()
    {
        var g = New3("scoiatael", "monsters", "skellige", empty: false);
        for (var p = 0; p < 3; p++) T.Ok(g, new Mulligan(p, Array.Empty<int>()));
        Assert.Equal(Phase.ChooseFirst, g.Phase);
        Assert.Contains(g.Events, e => e.Kind == "dice");
        T.Ok(g, new ChooseFirst(0, 2));
        Assert.Equal(2, g.Current);
    }

    [Fact]
    public void Turns_rotate_by_seat_and_skip_passed_players()
    {
        var g = New3(first: 1);
        FillAll(g);
        T.Play(g, 1, g.Players[1].Hand[0]);
        Assert.Equal(2, g.Current);
        T.Play(g, 2, g.Players[2].Hand[0]);
        Assert.Equal(0, g.Current);
        T.Pass(g, 0);
        Assert.Equal(1, g.Current);
        T.Play(g, 1, g.Players[1].Hand[0]);
        Assert.Equal(2, g.Current);   // 0 pas geçti, atlanır
        T.Play(g, 2, g.Players[2].Hand[0]);
        Assert.Equal(1, g.Current);
        T.Pass(g, 1);
        Assert.Equal(2, g.Current);
        T.Play(g, 2, g.Players[2].Hand[0]);
        Assert.Equal(2, g.Current);   // tek kalan oynamaya devam eder
    }

    [Fact]
    public void Spy_goes_to_the_chosen_opponent_and_defaults_to_the_next()
    {
        var g = New3("nilfgaard", "monsters", "skellige");
        FillAll(g);
        for (var i = 0; i < 4; i++) g.TestDeck(0, "redanian_foot_soldier");
        var spy = g.TestHand(0, "vattier_de_rideaux");
        var before = g.Players[0].Hand.Count;
        T.Ok(g, new PlayCard(0, spy.Uid, null, null, 2));
        Assert.Contains(spy, g.Players[2].Row(Close).Cards);
        Assert.Equal(4, g.PlayerTotal(2));
        Assert.Equal(before - 1 + 2, g.Players[0].Hand.Count);

        T.Play(g, 1, g.Players[1].Hand[0]);
        T.Play(g, 2, g.Players[2].Hand[0]);
        var spy2 = g.TestHand(0, "vattier_de_rideaux");
        T.Play(g, 0, spy2);
        Assert.Contains(spy2, g.Players[1].Row(Close).Cards);
    }

    [Fact]
    public void Self_or_out_of_range_target_is_rejected_without_change()
    {
        var g = New3("nilfgaard", "monsters", "skellige");
        FillAll(g);
        var spy = g.TestHand(0, "vattier_de_rideaux");
        T.Fails(g, new PlayCard(0, spy.Uid, null, null, 0), "hedef");
        T.Fails(g, new PlayCard(0, spy.Uid, null, null, 3), "hedef");
        T.Fails(g, new PlayCard(0, spy.Uid, null, null, -1), "hedef");
    }

    [Fact]
    public void Row_scorch_unit_hits_the_chosen_opponent()
    {
        var g = New3("northern_realms", "monsters", "skellige");
        FillAll(g);
        var big1 = g.TestBoard(1, "fiend", Close);     // 6
        g.TestBoard(1, "fiend", Close);
        var big2 = g.TestBoard(2, "fiend", Close);
        g.TestBoard(2, "fiend", Close);
        var villen = g.TestHand(0, "villentretenmerth");
        T.Ok(g, new PlayCard(0, villen.Uid, null, null, 2));
        Assert.Contains(big1, g.Players[1].Row(Close).Cards);
        Assert.Empty(g.Players[2].Row(Close).Cards);
        Assert.DoesNotContain(big2, g.Players[2].Row(Close).Cards);
    }

    [Fact]
    public void Scorch_burns_the_strongest_across_all_three_boards()
    {
        var g = New3();
        FillAll(g);
        var a = g.TestBoard(0, "fiend", Close);
        var b = g.TestBoard(1, "fiend", Close);
        var c = g.TestBoard(2, "fiend", Close);
        var small = g.TestBoard(2, "redanian_foot_soldier", Close);
        var scorch = g.TestHand(0, "scorch");
        T.Play(g, 0, scorch);
        Assert.DoesNotContain(a, g.Players[0].Row(Close).Cards);
        Assert.DoesNotContain(b, g.Players[1].Row(Close).Cards);
        Assert.DoesNotContain(c, g.Players[2].Row(Close).Cards);
        Assert.Contains(small, g.Players[2].Row(Close).Cards);
    }

    [Fact]
    public void Highest_score_wins_the_round_gem_fills_and_two_wins_take_the_match()
    {
        var g = New3();
        FillAll(g);
        for (var p = 0; p < 3; p++) for (var i = 0; i < 3; i++) g.TestDeck(p, "redanian_foot_soldier");
        g.TestBoard(1, "fiend", Close);
        g.TestBoard(2, "redanian_foot_soldier", Close);
        T.Pass(g, 0); T.Pass(g, 1); T.Pass(g, 2);

        Assert.Equal(2, g.Round);
        Assert.Equal(new[] { 0, 1, 0 }, g.Players.Select(p => p.Wins).ToArray());
        Assert.All(g.Players, p => Assert.Equal(2, p.Gems));   // üç kişide can kaybı yok
        var r1 = g.History[0];
        Assert.Equal(1, r1.Winner);
        Assert.Equal(new[] { 0, 6, 1 }, new[] { r1.Score(0), r1.Score(1), r1.Score(2) });
        Assert.Equal(1, g.Current);   // kazanan başlar
        // 2. raund başında herkes 1 kart çeker
        Assert.All(g.Players, p => Assert.Equal(7, p.Hand.Count));

        g.TestBoard(1, "fiend", Close);
        T.Pass(g, 1); T.Pass(g, 2); T.Pass(g, 0);
        Assert.Equal(Phase.GameOver, g.Phase);
        Assert.Equal(1, g.Outcome!.Winner);
    }

    [Fact]
    public void Tie_at_the_top_gives_nobody_the_round_unless_one_nilfgaard()
    {
        var g = New3();
        FillAll(g);
        g.TestBoard(0, "redanian_foot_soldier", Close);
        g.TestBoard(1, "redanian_foot_soldier", Close);
        T.Pass(g, 0); T.Pass(g, 1); T.Pass(g, 2);
        Assert.Null(g.History[0].Winner);
        Assert.All(g.Players, p => Assert.Equal(0, p.Wins));
        Assert.Equal(0, g.Current);   // önceki raundu başlatan başlar

        var n = New3("northern_realms", "nilfgaard", "skellige");
        FillAll(n);
        n.TestBoard(0, "redanian_foot_soldier", Close);
        n.TestBoard(1, "redanian_foot_soldier", Close);
        T.Pass(n, 0); T.Pass(n, 1); T.Pass(n, 2);
        Assert.Equal(1, n.History[0].Winner);
    }

    [Fact]
    public void Nilfgaard_tie_breaker_only_applies_at_the_top()
    {
        var g = New3("northern_realms", "skellige", "nilfgaard");
        FillAll(g);
        g.TestBoard(0, "fiend", Close);
        g.TestBoard(1, "redanian_foot_soldier", Close);
        g.TestBoard(2, "redanian_foot_soldier", Close);
        T.Pass(g, 0); T.Pass(g, 1); T.Pass(g, 2);
        Assert.Equal(0, g.History[0].Winner);
    }

    [Fact]
    public void Match_ends_at_the_round_cap_with_most_wins_or_draw()
    {
        var g = New3();
        for (var round = 1; round <= Game.MaxRoundsThree; round++)
        {
            Assert.Equal(round, g.Round);
            FillAll(g);
            if (round == 2) g.TestBoard(0, "redanian_foot_soldier", Close);   // yalnız bir raund kazanılır (Monsters değil: o kartı tutar)
            var start = g.Current;
            for (var k = 0; k < 3; k++) T.Pass(g, (start + k) % 3);
        }
        Assert.Equal(Phase.GameOver, g.Phase);
        Assert.Equal(0, g.Outcome!.Winner);

        var d = New3();
        for (var round = 1; round <= Game.MaxRoundsThree; round++)
        {
            FillAll(d);
            var start = d.Current;
            for (var k = 0; k < 3; k++) T.Pass(d, (start + k) % 3);
        }
        Assert.Equal(Phase.GameOver, d.Phase);
        Assert.Null(d.Outcome!.Winner);
    }

    [Fact]
    public void White_flame_cancels_both_other_leaders()
    {
        var g = New3("northern_realms", "nilfgaard", "monsters", l1: "emhyr_var_emreis_the_white_flame");
        Assert.True(g.LeaderCancelled(0));
        Assert.True(g.LeaderCancelled(2));
        Assert.False(g.LeaderCancelled(1));
    }

    [Fact]
    public void Row_scorch_leader_picks_a_qualifying_opponent_or_the_given_one()
    {
        var g = New3("northern_realms", "monsters", "skellige", l0: "foltest_the_steel_forged");
        FillAll(g);
        var t1 = g.TestBoard(1, "redanian_foot_soldier", Siege);
        var t2a = g.TestBoard(2, "fiend", Siege);
        g.TestBoard(2, "fiend", Siege);
        Assert.Null(g.LeaderBlockedReason(0));          // 2. rakipte 12 var
        Assert.NotNull(g.LeaderBlockedReason(0, 1));    // 1. rakipte 1
        T.Fails(g, new UseLeader(0, 1));
        T.Ok(g, new UseLeader(0));
        Assert.Contains(t1, g.Players[1].Row(Siege).Cards);
        Assert.DoesNotContain(t2a, g.Players[2].Row(Siege).Cards);
    }

    [Fact]
    public void Medic_reviving_a_spy_can_send_it_to_either_opponent()
    {
        var g = New3("nilfgaard", "monsters", "skellige");
        FillAll(g);
        var spy = g.TestGrave(0, "vattier_de_rideaux");
        var medic = g.TestHand(0, "etolian_auxiliary_archers");
        T.Play(g, 0, medic, medic.Def.IsAgile ? Ranged : null);
        Assert.NotNull(g.Pending);
        var acts = g.LegalActions().OfType<Choose>().Where(c => c.Cards[0] == spy.Uid).ToList();
        Assert.Equal(new int?[] { 1, 2 }, acts.Select(a => a.Side).ToArray());
        T.Ok(g, new Choose(0, new[] { spy.Uid }, null, 2));
        Assert.Contains(spy, g.Players[2].Row(Close).Cards);
    }

    [Fact]
    public void Three_player_rejects_curses_and_keep_extras()
    {
        var decks = new[] { "northern_realms", "monsters", "skellige" }.Select(f => DeckValidator.DefaultDeck(f)).ToArray();
        Assert.Throws<ArgumentException>(() => new Game(decks, 1, new GameOptions { BloodPrice = true }));
        Assert.Throws<ArgumentException>(() => new Game(decks, 1, new GameOptions { StartingHand = 8 }));
        Assert.Throws<ArgumentException>(() => new Game(decks, 1, new GameOptions { Commanders = true }));
    }

    // ---------- Rastgele oyunlar ----------

    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private static int CountCards(Game g)
    {
        var uids = new HashSet<int>();
        var n = 0;
        void Add(CardInstance c)
        {
            if (c.Def.Token || c.Ephemeral) return;
            Assert.True(uids.Add(c.Uid), $"{c} birden fazla yerde");
            n++;
        }
        foreach (var p in g.Players)
        {
            foreach (var c in p.Deck.Concat(p.Hand).Concat(p.Grave)) Add(c);
            foreach (var r in p.Rows)
            {
                foreach (var c in r.Cards) Add(c);
                if (r.Special != null) Add(r.Special);
            }
        }
        foreach (var c in g.WeatherCards) Add(c);
        return n;
    }

    private static (Game Game, List<GameAction> Actions) RandomGame(int seed)
    {
        var rng = new Random(seed * 31 + 7);
        var decks = Enumerable.Range(0, 3).Select(_ =>
        {
            var f = Factions[rng.Next(Factions.Length)];
            var leaders = CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen").ToList();
            return DeckValidator.DefaultDeck(f) with { LeaderId = leaders[rng.Next(leaders.Count)].Id };
        }).ToArray();
        var g = new Game(decks, seed);
        var initial = CountCards(g);
        var actions = new List<GameAction>();
        var steps = 0;
        while (g.Phase != Phase.GameOver)
        {
            Assert.True(++steps < 6000, $"Oyun bitmedi (seed {seed})");
            var legal = g.LegalActions().ToList();
            Assert.NotEmpty(legal);
            var nonPass = legal.Where(a => a is not Pass).ToList();
            var pick = nonPass.Count > 0 && rng.NextDouble() > 0.08 ? nonPass[rng.Next(nonPass.Count)] : legal[rng.Next(legal.Count)];
            var r = g.Apply(pick);
            Assert.True(r.Ok, $"Geçerli sayılan eylem reddedildi: {pick} → {r.Error} (seed {seed})");
            actions.Add(pick);
            var transforms = g.Events.Count(e => e.Kind == "transform");
            var inLimbo = g.Pending?.Kind == ChoiceKind.AgileRow ? 1 : 0;
            Assert.Equal(initial - transforms, CountCards(g) + inLimbo);
            Assert.All(g.Players, p => Assert.InRange(p.Wins, 0, 2));
        }
        Assert.NotNull(g.Outcome);
        Assert.InRange(g.Round, 2, Game.MaxRoundsThree);
        Assert.Empty(g.LegalActions());
        if (g.Outcome!.Winner is { } w) Assert.True(g.Players[w].Wins >= 1);
        return (g, actions);
    }

    [Fact]
    public void Random_three_player_games_conserve_cards_and_are_deterministic()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("GWENT_FUZZ_SEEDS"), out var n) ? n : 120;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var (g, actions) = RandomGame(seed);
            // Aynı tohum + aynı eylemler = olay olay aynı oyun (online kopyaların temeli)
            var rng = new Random(seed * 31 + 7);
            var decks = Enumerable.Range(0, 3).Select(_ =>
            {
                var f = Factions[rng.Next(Factions.Length)];
                var leaders = CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen").ToList();
                return DeckValidator.DefaultDeck(f) with { LeaderId = leaders[rng.Next(leaders.Count)].Id };
            }).ToArray();
            var copy = new Game(decks, seed);
            foreach (var a in actions) Assert.True(copy.Apply(a).Ok);
            Assert.Equal(g.Events.Select(e => (e.Kind, e.Player, e.CardUid, e.Detail)), copy.Events.Select(e => (e.Kind, e.Player, e.CardUid, e.Detail)));
        }
    }

    [Fact]
    public void Clone_of_three_player_game_plays_the_same()
    {
        var g = New3(empty: false);
        for (var p = 0; p < 3; p++) T.Ok(g, new Mulligan(p, Array.Empty<int>()));
        var c = g.Clone();
        Assert.Equal(3, c.PlayerCount);
        var a = g.LegalActions().First(x => x is PlayCard);
        Assert.True(g.Apply(a).Ok);
        Assert.True(c.Apply(a).Ok);
        Assert.Equal(g.Current, c.Current);
        Assert.Equal(Enumerable.Range(0, 3).Select(g.PlayerTotal), Enumerable.Range(0, 3).Select(c.PlayerTotal));
    }
}
