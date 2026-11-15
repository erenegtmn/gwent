using HanLigi;

namespace HanLigi.Tests;

/// <summary>
/// Öğretici canavar round'ları (kesin kural): R1'de tek kart, güç 1; R2'de her satırda bir güç-1 kart. Tek kart R2'de her
/// durumda kaybeder; farklı satırlarda iki kart (güç ≥ 2) kazanır. Canavar bu iki round'da müdahale etmez.
/// </summary>
public class PveTests
{
    private static Match New(int seed) => new(seed, Enumerable.Range(0, 2).Select(i => ($"O{i}", true)).ToList());

    /// <summary>Oyuncunun bütün kartlarını satar, sonra istenen kartları alıp satırlarına koyar.</summary>
    private static void Field(Match m, int pid, params string[] ids)
    {
        var p = m.Players[pid];
        foreach (var c in p.Cards.ToList()) Assert.True(m.Sell(pid, c.Uid));
        p.Gold = 100;
        foreach (var id in ids)
        {
            var d = CardSet.Get(id);
            if (p.Shop[0] is { } old) m.Pool[old.Id]++;
            p.Shop[0] = d;
            m.Pool[d.Id]--;
            Assert.True(m.Buy(pid, 0));
            var c = p.Cards.Last();
            var zone = d.Row switch { Row.Close => Zone.Close, Row.Ranged => Zone.Ranged, _ => Zone.Siege };
            Assert.True(m.Move(pid, c.Uid, zone), $"{id} sahaya konamadı");
        }
    }

    private static Battle Fight(Match m, int pid, int seed)
    {
        m.ForceLock();
        var b = m.BattleOf(pid)!;
        Assert.True(b.Pve);
        HanBot.PlayOut(b, new Random(seed));
        return b;
    }

    [Fact]
    public void Round_one_monster_is_a_single_one_power_card()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var m = New(seed);
            foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[0]);
            Assert.Equal(1, m.Round);
            Field(m, 0, "dwarven_skirmisher");
            var b = Fight(m, 0, seed);
            var mon = b.Sides[1];
            var u = Assert.Single(mon.Units);
            Assert.Equal(1, u.Power);
            Assert.Empty(mon.Specials);
            Assert.Equal(0, b.Winner);   // tek kart (güç 3) yeter
        }
    }

    [Fact]
    public void Round_two_monster_loses_to_two_cards_and_beats_any_single_card()
    {
        // tek kartlar: her satırdan en güçlü tier-1/2 seçenekleri
        var singles = new[] { "dwarven_skirmisher", "blue_stripes_commando", "mahakaman_defender", "vrihedd_brigade_recruit", "elven_skirmisher", "kaedweni_siege_expert" };
        for (var seed = 0; seed < 20; seed++)
        {
            foreach (var single in singles)
            {
                var m = ToRoundTwo(seed);
                Field(m, 0, single);
                var b = Fight(m, 0, seed);
                Assert.Equal(3, b.Sides[1].Units.Count);
                Assert.All(b.Sides[1].Units, u => Assert.Equal(1, u.Power));
                Assert.Equal(3, b.Sides[1].Units.Select(u => u.Row).Distinct().Count());
                Assert.Empty(b.Sides[1].Specials);
                Assert.Equal(1, b.Winner);   // tek kart her durumda kaybeder
            }
            // farklı satırlarda iki kart kazanır
            var m2 = ToRoundTwo(seed);
            Field(m2, 0, "dwarven_skirmisher", "elven_skirmisher");
            Assert.Equal(0, Fight(m2, 0, seed).Winner);
        }
    }

    private static Match ToRoundTwo(int seed)
    {
        var m = New(seed);
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[0]);
        HanBot.RunToEnd(m, new Random(seed), 1);
        Assert.Equal(2, m.Round);
        Assert.True(m.IsPve);
        foreach (var p in m.Players) if (p.RewardOptions.Length > 0) m.PickReward(p.Id, 0);
        return m;
    }
}
