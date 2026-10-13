using GwentCore;
using Xunit;
using Xunit.Abstractions;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

/// <summary>Kullanıcı bildirimi: 4'er güçte kartlar varken Yak oynandı; Tight Bond'lu rakip kartı yok olmamış göründü.</summary>
public class ScorchBondTests
{
    private readonly ITestOutputHelper _out;
    public ScorchBondTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Scorch_kills_bonded_pair_and_other_fours_together()
    {
        var g = T.New("northern_realms", "nilfgaard");
        var n1 = g.TestBoard(1, "nausicaa_cavalry_rider", Close);   // 2 -> bond ile 4
        var n2 = g.TestBoard(1, "nausicaa_cavalry_rider", Close);   // 4
        var m1 = g.TestBoard(0, "sheldon_skaggs", Ranged);          // 4
        var m2 = g.TestBoard(0, "blue_stripes_commando", Close);    // 4 (tek)
        _out.WriteLine($"önce: n1={g.CardScore(n1)} n2={g.CardScore(n2)} m1={g.CardScore(m1)} m2={g.CardScore(m2)}");
        var scorch = g.TestHand(0, "scorch");
        T.Filler(g, 0);
        T.Play(g, 0, scorch);
        _out.WriteLine($"sonra: rakip kapalı sıra {g.Players[1].Row(Close).Cards.Count}, benim yakın {g.Players[0].Row(Close).Cards.Count}, menzilli {g.Players[0].Row(Ranged).Cards.Count}");
        Assert.Empty(g.Players[1].Row(Close).Cards);
        Assert.Empty(g.Players[0].Row(Ranged).Cards);
        Assert.Empty(g.Players[0].Row(Close).Cards);
    }

    [Fact]
    public void Single_bond_card_with_partner_in_hand_dies_to_scorch_and_twin_stands_alone()
    {
        var g = T.New("northern_realms", "nilfgaard");
        var n1 = g.TestBoard(1, "impera_brigade_guard", Close);     // 3 tek
        var other = g.TestBoard(0, "sheldon_skaggs", Ranged);       // 4 (en güçlü)
        var scorch = g.TestHand(0, "scorch");
        T.Filler(g, 0);
        T.Play(g, 0, scorch);
        Assert.DoesNotContain(other, g.Players[0].Row(Ranged).Cards);
        Assert.Contains(n1, g.Players[1].Row(Close).Cards);          // 3 < 4: yaşar
    }

    /// <summary>Rastgele oyunlarda: Yak oynanabilen her durumda en yüksek anlık güçteki kahraman olmayan TÜM birimler yok olmalı, başkası değil.</summary>
    [Fact]
    public void Scorch_invariant_holds_in_random_games()
    {
        var checkedCases = 0;
        for (var seed = 1; seed <= 250; seed++)
        {
            var f = new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
            var d0 = DeckValidator.RecommendedDeck(f[seed % 5]);
            var d1 = DeckValidator.RecommendedDeck(f[(seed / 5) % 5]);
            var g = new Game(d0, d1, seed);
            var ai = new GwentAI.HeuristicAgent();
            var rnd = new Random(seed);
            for (var step = 0; step < 400 && g.Phase != Phase.GameOver; step++)
            {
                var who = g.PlayersToAct();
                if (who.Count == 0) break;
                var p = who[0];
                if (g.Phase == Phase.Playing && g.Pending == null)
                {
                    var sc = g.Players[p].Hand.FirstOrDefault(c => c.Def.Id == Ids.Scorch);
                    if (sc != null)
                    {
                        var cl = g.Clone();
                        var cards = cl.Players.SelectMany(pl => pl.Rows.SelectMany(r => r.Cards)).Where(c => c.Def.IsUnit && !c.Def.Hero).ToList();
                        if (cards.Count > 0)
                        {
                            var max = cards.Max(c => cl.CardScore(c));
                            var expectDead = cards.Where(c => cl.CardScore(c) == max).Select(c => c.Uid).ToHashSet();
                            var res = cl.Apply(new PlayCard(p, sc.Uid, null, null));
                            Assert.True(res.Ok, res.Error);
                            var alive = cl.Players.SelectMany(pl => pl.Rows.SelectMany(r => r.Cards)).Select(c => c.Uid).ToHashSet();
                            foreach (var uid in expectDead) Assert.False(alive.Contains(uid), $"seed {seed}: en güçlü kart (uid {uid}, güç {max}) yaşıyor");
                            foreach (var c in cards.Where(c => !expectDead.Contains(c.Uid) && cl.History.Count == g.History.Count))
                                Assert.True(alive.Contains(c.Uid) || cl.Players.Any(pl => pl.Grave.Any(x => x.Uid == c.Uid)) , "kart kayboldu");
                            checkedCases++;
                        }
                    }
                }
                var a = rnd.NextDouble() < 0.15 ? g.LegalActions().Where(x => x.Player == p).OrderBy(_ => rnd.Next()).First() : ai.Decide(g, p);
                if (!g.Apply(a).Ok) break;
            }
        }
        _out.WriteLine($"denetlenen Yak durumu: {checkedCases}");
        Assert.True(checkedCases > 20, "yeterli Yak durumu bulunamadı");
    }
}
