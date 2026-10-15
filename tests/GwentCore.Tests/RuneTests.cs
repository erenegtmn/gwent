using GwentAI;
using GwentCore;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Kaer Morhen rünleri: güç etkileri, Veles/Morana, deste doğrulama, yapay zekâ rünleri, kayıt ve rastgele oyunlar.</summary>
public class RuneTests
{
    private static CardInstance Runed(Game g, int side, string id, RowKind row, string rune)
    {
        var c = g.TestBoard(side, id, row);
        c.SetRune(rune);
        return c;
    }

    [Fact]
    public void Svarog_adds_one_before_weather_and_multipliers()
    {
        var g = T.New();
        var c = Runed(g, 0, "ballista", RowKind.Siege, RuneIds.Svarog);
        Assert.Equal(7, g.CardScore(c));
        g.TestSpecial(0, Ids.Horn, RowKind.Siege);
        Assert.Equal(14, g.CardScore(c));
        g.TestWeather(Ids.TorrentialRain);
        Assert.Equal(2, g.CardScore(c)); // hava 1'e indirir, boynuz ikiye katlar
    }

    [Fact]
    public void Perun_ignores_weather()
    {
        var g = T.New();
        var p = Runed(g, 0, "ballista", RowKind.Siege, RuneIds.Perun);
        var plain = g.TestBoard(0, "siege_tower", RowKind.Siege);
        g.TestWeather(Ids.TorrentialRain);
        Assert.Equal(6, g.CardScore(p));
        Assert.Equal(1, g.CardScore(plain));
    }

    [Fact]
    public void Stribog_gives_two_in_the_commanders_row()
    {
        var g = T.New(options: new GameOptions { Commanders = true });
        var close = Runed(g, 0, "siegfried_of_denesle", RowKind.Close, RuneIds.Stribog);
        var siege = Runed(g, 0, "ballista", RowKind.Siege, RuneIds.Stribog);
        Assert.Equal(7, g.CardScore(close));  // komutan yakın satırda başlar
        Assert.Equal(6, g.CardScore(siege));
        Assert.Equal(5, T.New().Let(g2 => g2.CardScore(Runed(g2, 0, "siegfried_of_denesle", RowKind.Close, RuneIds.Stribog)))); // komutansız: etkisiz
    }

    [Fact]
    public void Dazhbog_gives_morale()
    {
        var g = T.New();
        var a = g.TestBoard(0, "ballista", RowKind.Siege);
        var d = Runed(g, 0, "siege_tower", RowKind.Siege, RuneIds.Dazhbog);
        Assert.Equal(7, g.CardScore(a));
        Assert.Equal(6, g.CardScore(d));
    }

    [Fact]
    public void Veles_returns_to_hand_once_when_scorched()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var v = Runed(g, 1, "vampire_katakan", RowKind.Close, RuneIds.Veles);
        var hand = g.Players[1].Hand.Count;
        T.Play(g, 0, g.TestHand(0, Ids.Scorch));
        Assert.Contains(v, g.Players[1].Hand);
        Assert.Equal(hand + 1, g.Players[1].Hand.Count);
        Assert.Null(v.Rune);
        Assert.Contains(g.Events, e => e.Kind == "rune_return" && e.CardUid == v.Uid);
        Assert.Contains(g.Events, e => e.Kind == "kill" && e.Detail == "vampire_katakan:5");
    }

    [Fact]
    public void Kill_event_carries_the_power_at_the_moment_of_burning()
    {
        var g = T.New(options: new GameOptions { Terrain = TerrainMap.Symmetric("t", "T", Terrain.Plain, Terrain.Plain, Terrain.Hill) });
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestBoard(1, "catapult", RowKind.Siege);
        T.Play(g, 0, g.TestHand(0, Ids.Scorch));
        Assert.Contains(g.Events, e => e.Kind == "kill" && e.Detail == "catapult:9");
    }

    [Fact]
    public void Morana_stays_one_round()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var m = Runed(g, 0, "ballista", RowKind.Siege, RuneIds.Morana);
        var other = g.TestBoard(0, "siege_tower", RowKind.Siege);
        T.Pass(g, 0); T.Pass(g, 1);
        Assert.Equal(2, g.Round);
        Assert.Contains(m, g.Players[0].Row(RowKind.Siege).Cards);
        Assert.DoesNotContain(other, g.Players[0].Row(RowKind.Siege).Cards);
        Assert.Null(m.Rune);
        T.Pass(g, g.Current); T.Pass(g, g.Current);
        Assert.DoesNotContain(m, g.Players[0].Row(RowKind.Siege).Cards);
    }

    [Fact]
    public void Deck_runes_are_applied_to_one_copy_each()
    {
        var deck = DeckValidator.RecommendedDeck("northern_realms");
        var id = deck.CardIds.First(x => RuneIds.CanTake(CardCatalog.Default.Get(x)));
        var runed = deck with { Runes = new[] { new CardRune(id, RuneIds.Svarog) } };
        Assert.Empty(DeckValidator.Validate(runed));
        var g = new Game(runed, DeckValidator.RecommendedDeck("monsters"), 3);
        var all = g.Players[0].Deck.Concat(g.Players[0].Hand).ToList();
        Assert.Single(all, c => c.Rune == RuneIds.Svarog && c.Def.Id == id);
        Assert.All(g.Players[1].Deck.Concat(g.Players[1].Hand), c => Assert.Null(c.Rune));
    }

    [Fact]
    public void Invalid_runes_are_rejected()
    {
        var deck = DeckValidator.RecommendedDeck("northern_realms");
        var units = deck.CardIds.Where(x => RuneIds.CanTake(CardCatalog.Default.Get(x))).Distinct().Take(4).ToList();
        var hero = deck.CardIds.First(x => CardCatalog.Default.Get(x).Hero);
        Assert.NotEmpty(DeckValidator.Validate(deck with { Runes = units.Select(u => new CardRune(u, RuneIds.Svarog)).ToList() }));
        Assert.NotEmpty(DeckValidator.Validate(deck with { Runes = new[] { new CardRune(hero, RuneIds.Svarog) } }));
        Assert.NotEmpty(DeckValidator.Validate(deck with { Runes = new[] { new CardRune("vampire_katakan", RuneIds.Svarog) } }));
        Assert.NotEmpty(DeckValidator.Validate(deck with { Runes = new[] { new CardRune(units[0], "yok") } }));
    }

    [Fact]
    public void Ai_runes_are_deterministic_and_valid()
    {
        foreach (var f in new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" })
        {
            var deck = DeckValidator.RecommendedDeck(f);
            var r = Forge.AutoRunes(deck, 3);
            Assert.Equal(3, r.Count);
            Assert.Equal(r, Forge.AutoRunes(deck, 3));
            Assert.Empty(DeckValidator.Validate(deck with { Runes = r }));
        }
    }

    [Fact]
    public void Runed_games_replay_identically()
    {
        var mine = DeckValidator.RecommendedDeck("skellige");
        var replay = new ReplayData
        {
            MyFaction = "skellige", MyLeaderId = mine.LeaderId, MyDeckCards = mine.CardIds.ToList(), MyRunes = Forge.AutoRunes(mine, 3).ToList(),
            OppFaction = "nilfgaard", OppLeaderId = CardCatalog.Default.Leaders("nilfgaard").First().Id, OppAutoRunes = 3,
            Seed = 11, Battlefield = "toussaint_vineyards", Commanders = true, Difficulty = (int)Difficulty.Hard
        };
        var g = replay.CreateGame();
        var agents = new IAgent[] { new HeuristicAgent(), new HeuristicAgent(seed: 4) };
        while (g.Phase != Phase.GameOver)
        {
            var p = g.PlayersToAct()[0];
            var a = agents[p].Decide(g, p);
            replay.Actions.Add(ReplayData.Encode(a));
            Assert.True(g.Apply(a).Ok);
        }
        var again = ReplayData.FromJson(replay.ToJson()).Run();
        Assert.Equal(g.History, again.History);
        Assert.Equal(g.Events.Count, again.Events.Count);
    }

    [Fact]
    public void Random_runed_games_finish_and_keep_cards()
    {
        string[] f = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("GWENT_FUZZ_SEEDS"), out var n) ? n * 5 : 60;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var opts = new GameOptions { Terrain = Battlefields.ForSeed(seed), Commanders = true };
            var d0 = DeckValidator.DefaultDeck(f[seed % 5], opts);
            var d1 = DeckValidator.DefaultDeck(f[(seed + 3) % 5], opts);
            d0 = d0 with { Runes = Forge.AutoRunes(d0, 3).Select((r, i) => r with { Rune = RuneIds.All[(seed + i) % RuneIds.All.Count] }).ToList() };
            d1 = d1 with { Runes = Forge.AutoRunes(d1, 3).Select((r, i) => r with { Rune = RuneIds.All[(seed + i + 3) % RuneIds.All.Count] }).ToList() };
            var g = new Game(d0, d1, seed, opts);
            var rng = new Random(seed);
            var total = Count(g);
            for (var step = 0; g.Phase != Phase.GameOver; step++)
            {
                Assert.True(step < 4000, $"oyun bitmedi ({seed})");
                var legal = g.LegalActions().ToList();
                var nonPass = legal.Where(a => a is not Pass).ToList();
                var pick = nonPass.Count > 0 && rng.NextDouble() > 0.06 ? nonPass[rng.Next(nonPass.Count)] : legal[rng.Next(legal.Count)];
                var r = g.Apply(pick);
                Assert.True(r.Ok, $"{pick}: {r.Error} ({seed})");
                var limbo = g.Pending?.Kind == ChoiceKind.AgileRow ? 1 : 0;
                var cnt = Count(g) + limbo;
                Assert.True(total - g.Events.Count(e => e.Kind == "transform") == cnt,
                    $"seed {seed}: {total} - tr vs {cnt}; {pick}; " + string.Join(" | ", g.Events.TakeLast(8).Select(e => $"{e.Kind} {e.Player} {e.CardUid} {e.Detail}")));
            }
        }

        static int Count(Game g)
        {
            var uids = new HashSet<int>();
            foreach (var p in g.Players)
            {
                foreach (var c in p.Deck.Concat(p.Hand).Concat(p.Grave).Where(c => !c.Def.Token)) Assert.True(uids.Add(c.Uid), $"{c} iki yerde");   // Yem ile ele dönen ayı jetondur
                foreach (var row in p.Rows)
                {
                    foreach (var c in row.Cards.Where(c => !c.Def.Token)) Assert.True(uids.Add(c.Uid), $"{c} iki yerde");
                    if (row.Special is { Ephemeral: false } s) Assert.True(uids.Add(s.Uid));
                }
            }
            foreach (var w in g.WeatherCards.Where(w => !w.Ephemeral)) Assert.True(uids.Add(w.Uid));
            return uids.Count;
        }
    }
}

internal static class LetExt
{
    public static TR Let<T0, TR>(this T0 v, Func<T0, TR> f) => f(v);
}
