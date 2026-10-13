using GwentCore;
using Xunit;

namespace GwentCore.Tests;

/// <summary>
/// Rastgele ama geçerli eylemlerle çok sayıda oyun oynar. Kural testlerinin akla getirmediği
/// etkileşimleri (Medic zinciri, Scorch + Avenger, Skellige + çevik birim...) denetler.
/// </summary>
public class FuzzTests
{
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
            foreach (var c in p.Deck) Add(c);
            foreach (var c in p.Hand) Add(c);
            foreach (var c in p.Grave) Add(c);
            foreach (var r in p.Rows)
            {
                foreach (var c in r.Cards) Add(c);
                if (r.Special != null) Add(r.Special);
            }
        }
        foreach (var c in g.WeatherCards) Add(c);
        return n;
    }

    private static (Game Game, int Steps) PlayRandomGame(string f0, string f1, int seed, bool nextGen)
    {
        var opts = new GameOptions { NextGen = nextGen };
        var rng = new Random(seed * 7919 + 13);
        var leaders0 = CardCatalog.Default.Leaders(f0).Where(l => l.Edition != "nextgen" || nextGen).ToList();
        var leaders1 = CardCatalog.Default.Leaders(f1).Where(l => l.Edition != "nextgen" || nextGen).ToList();
        var d0 = DeckValidator.DefaultDeck(f0, opts) with { LeaderId = leaders0[rng.Next(leaders0.Count)].Id };
        var d1 = DeckValidator.DefaultDeck(f1, opts) with { LeaderId = leaders1[rng.Next(leaders1.Count)].Id };
        var g = new Game(d0, d1, seed, opts);
        var initial = CountCards(g);
        var transforms = 0;
        var steps = 0;

        while (g.Phase != Phase.GameOver)
        {
            Assert.True(++steps < 4000, $"Oyun bitmedi (seed {seed}, {f0} vs {f1})");
            var legal = g.LegalActions().ToList();
            Assert.NotEmpty(legal);

            // Pas'a ağırlık verme: oyunlar uzasın ve yetenekler çalışsın.
            var nonPass = legal.Where(a => a is not Pass).ToList();
            var pick = nonPass.Count > 0 && rng.NextDouble() > 0.06 ? nonPass[rng.Next(nonPass.Count)] : legal[rng.Next(legal.Count)];

            var r = g.Apply(pick);
            Assert.True(r.Ok, $"Geçerli sayılan eylem reddedildi: {pick} → {r.Error} (seed {seed})");

            transforms = g.Events.Count(e => e.Kind == "transform");
            // Çevik birimin sırası beklenirken kart hiçbir bölgede değildir (seçim kartı havada tutar).
            var inLimbo = g.Pending?.Kind == ChoiceKind.AgileRow ? 1 : 0;
            Assert.Equal(initial - transforms, CountCards(g) + inLimbo);
            Assert.All(g.Players, p => Assert.InRange(p.Gems, 0, 2));
        }

        Assert.NotNull(g.Outcome);
        Assert.Empty(g.LegalActions());
        return (g, steps);
    }

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var a in Factions)
            foreach (var b in Factions)
                yield return new object[] { a, b };
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Random_games_finish_and_never_lose_or_duplicate_cards(string f0, string f1)
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("GWENT_FUZZ_SEEDS"), out var n) ? n : 12;
        for (var seed = 1; seed <= seeds; seed++)
            PlayRandomGame(f0, f1, seed, nextGen: seed % 2 == 0);
    }

    [Fact]
    public void Same_seed_and_same_actions_give_identical_events()
    {
        static List<string> Run()
        {
            var (g, _) = PlayRandomGame("skellige", "nilfgaard", 42, nextGen: false);
            return g.Events.Select(e => $"{e.Kind}|{e.Player}|{e.CardUid}|{e.Detail}").ToList();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Different_seeds_give_different_games()
    {
        var a = PlayRandomGame("northern_realms", "monsters", 1, false).Game;
        var b = PlayRandomGame("northern_realms", "monsters", 2, false).Game;
        Assert.NotEqual(a.Events.Count, b.Events.Count);
    }

    [Fact]
    public void Rejected_actions_never_change_the_state()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var snapshot = g.Events.Count;
        var hand = g.Players[0].Hand.Count;
        Assert.False(g.Apply(new PlayCard(1, g.Players[1].Hand[0].Uid)).Ok);   // sıra yok
        Assert.False(g.Apply(new PlayCard(0, 99999)).Ok);                       // kart yok
        Assert.False(g.Apply(new Choose(0, new[] { 1 })).Ok);                   // seçim yok
        Assert.False(g.Apply(new Mulligan(0, Array.Empty<int>())).Ok);          // aşama geçti
        Assert.False(g.Apply(new ChooseFirst(0, 0)).Ok);
        Assert.Equal(snapshot, g.Events.Count);
        Assert.Equal(hand, g.Players[0].Hand.Count);
        Assert.Equal(0, g.Current);
    }
}
