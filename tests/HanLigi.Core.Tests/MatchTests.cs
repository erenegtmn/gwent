using HanLigi;
using Xunit.Abstractions;

namespace HanLigi.Tests;

public class MatchTests(ITestOutputHelper output)
{
    private static Match New(int seed, int players = 8, int humans = 0)
        => new(seed, Enumerable.Range(0, players).Select(i => ($"O{i}", i >= humans)).ToList());

    private static void PickSchools(Match m) { foreach (var p in m.Players) Assert.True(m.ChooseSchool(p.Id, p.SchoolOptions[0])); }

    /// <summary>Marketin ilk yuvasına istenen kartı koyar (havuz sayımı korunur).</summary>
    private static void Put(Match m, PlayerState p, CardDef d)
    {
        if (p.Shop[0] is { } old) m.Pool[old.Id]++;
        p.Shop[0] = d;
        m.Pool[d.Id]--;
    }

    private static void AssertConserved(Match m, Dictionary<string, int> start)
    {
        foreach (var kv in start) Assert.Equal(kv.Value, m.TotalCopies(kv.Key));
    }

    private static string Fingerprint(Match m) => string.Join("|", m.Players.Select(p =>
        $"{p.Hp},{p.Gold},{p.Level},{p.Xp},{p.Place},{string.Join(",", p.Cards.Select(c => c.Def.Id + c.Star + c.Zone))},{string.Join(",", p.Shop.Select(d => d?.Id))}")) + $"|{m.Round}|{m.Phase}";

    [Fact]
    public void Shop_odds_tables_sum_to_100()
    {
        foreach (var row in Tuning.ShopOdds) Assert.Equal(100, row.Sum());
        Assert.Equal(new[] { 7, 7, 8, 7, 6 }, Enumerable.Range(1, 5).Select(t => CardSet.All.Count(d => d.IsUnit && !d.IsHero && d.Tier == t)).ToArray());
        Assert.Equal(8, CardSet.All.Count(d => d.IsHero));
    }

    [Fact]
    public void Round_start_gives_income_interest_and_xp()
    {
        var m = New(1);
        PickSchools(m);
        Assert.Equal(1, m.Round);
        Assert.True(m.IsPve);
        var p = m.Players[0];
        Assert.Equal(Tuning.StartGold + Tuning.Income, p.Gold);   // round 1'de faiz yok
        Assert.Equal(2, p.Level);                                  // 2 XP = L2
        Assert.Equal(4, p.Slots);
    }

    [Fact]
    public void Streak_gold_rewards_loss_streaks_more_than_win_streaks()
    {
        Assert.Equal(new[] { 4, 4, 3, 2, 1, 0, 0, 0, 1, 1, 2 }, new[] { -6, -5, -4, -3, -2, -1, 0, 2, 3, 4, 5 }.Select(Tuning.StreakGold).ToArray());
        // gerçek maçta: seriler round sonunda güncellenir, sonraki round başında altın olarak gelir
        var m = New(11);
        PickSchools(m);
        var rng = new Random(5);
        var sawLoss = 0;
        while (m.Phase != Phase.Finished && m.Round < 60)
        {
            var pve = m.IsPve;
            var before = m.Players.ToDictionary(p => p.Id, p => p.Streak);
            HanBot.RunToEnd(m, rng, m.Round);   // tam olarak bir round
            if (m.Phase == Phase.Finished) break;
            foreach (var p in m.Players.Where(p => p.Alive))
            {
                if (pve) Assert.Equal(before[p.Id], p.Streak);   // PvE seriyi değiştirmez
                else Assert.True(Math.Abs(p.Streak - before[p.Id]) == 1 || Math.Abs(p.Streak) <= 1, $"seri sıçradı: {before[p.Id]} -> {p.Streak}");
                if (p.Streak <= -2) sawLoss++;
            }
        }
        Assert.True(sawLoss > 0, "hiç yenilgi serisi oluşmadı");
    }

    [Fact]
    public void Buying_three_copies_evolves_and_selling_refunds()
    {
        var m = New(3);
        PickSchools(m);
        var p = m.Players[0];
        p.Gold = 100;
        var nekker = CardSet.Get("nekker");
        for (var i = 0; i < 3; i++) { Put(m, p, nekker); Assert.True(m.Buy(0, 0)); }
        var c = Assert.Single(p.Cards);
        Assert.Equal(2, c.Star);
        Assert.Equal(3, c.Power);                                   // 2 × 1,5
        Assert.Equal(100 - 3, p.Gold);
        Assert.True(m.Sell(0, c.Uid));
        Assert.Equal(100 - 3 + 2, p.Gold);                          // 3 × 1 − 1
    }

    [Fact]
    public void Rejected_actions_do_not_change_state()
    {
        var m = New(5);
        PickSchools(m);
        var p = m.Players[0];
        p.Gold = 0;
        var before = Fingerprint(m);
        Assert.False(m.Buy(0, 0));
        Assert.False(m.Reroll(0));
        Assert.False(m.BuyXp(0));
        Assert.False(m.Sell(0, 999));
        Assert.False(m.Move(0, 999, Zone.Close));
        Assert.False(m.ChoosePotion(0, 0));
        Assert.False(m.PickReward(0, 0));
        Assert.False(m.SwapOrder(0, 0, 0));
        Assert.Equal(before, Fingerprint(m));
    }

    [Fact]
    public void Field_slots_follow_level_and_rows_are_fixed()
    {
        var m = New(7);
        PickSchools(m);
        var p = m.Players[0];
        p.Gold = 100;
        void Give(string id) { Put(m, p, CardSet.Get(id)); Assert.True(m.Buy(0, 0)); }
        Give("fiend"); Give("dethmold"); Give("trebuchet"); Give("griffin"); Give("stefan_skellen");
        var fiend = p.Cards.First(c => c.Def.Id == "fiend");
        Assert.False(m.Move(0, fiend.Uid, Zone.Ranged));            // satır sabit
        Assert.False(m.Move(0, fiend.Uid, Zone.Ambush));            // Pusu yeteneği yok
        // hazırlıkta masa sınırı esner: beşi de konur, sayı sınırı aşar; kilitte fazlası depoya iner (§15.4)
        var placed = 0;
        foreach (var c in p.Cards.ToList()) if (m.Move(0, c.Uid, Match.ZonesFor(c.Def).First())) placed++;
        Assert.Equal(5, placed);
        Assert.Equal(4, p.Slots);                                    // L2: 4 slot
        Assert.True(p.Overfull);
        m.ForceLock();
        Assert.Equal(p.Slots, p.Used);
        Assert.Single(p.Bench);
    }

    [Fact]
    public void Same_seed_gives_the_same_match()
    {
        string Run(int seed)
        {
            var m = New(seed);
            HanBot.RunToEnd(m, new Random(seed));
            return Fingerprint(m) + string.Join(";", m.History.Select(h => $"{h.Round}:{h.PlayerA}-{h.PlayerB}:{h.PointsA}-{h.PointsB}:{h.Damage}"));
        }
        Assert.Equal(Run(11), Run(11));
        Assert.NotEqual(Run(11), Run(12));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void Random_bot_matches_finish_and_conserve_cards(int players)
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("HAN_FUZZ_SEEDS"), out var n) ? n : 12;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var m = New(seed * 31 + players, players);
            var start = CardSet.All.ToDictionary(d => d.Id, d => m.TotalCopies(d.Id));
            var rng = new Random(seed);
            foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(3)]);
            while (m.Phase != Phase.Finished)
            {
                Assert.True(m.Round < 80, "maç bitmedi");
                foreach (var p in m.Alive.ToList()) HanBot.Prep(m, p.Id, rng);
                Assert.Equal(Phase.Battle, m.Phase);
                AssertConserved(m, start);
                foreach (var p in m.Alive)
                {
                    Assert.True(p.Gold >= 0);
                    Assert.True(p.Fielded.Count() <= p.Slots);
                    Assert.True(p.Bench.Count() <= Tuning.BenchSize);
                    Assert.True(p.Cards.Count(c => c.Zone == Zone.Ambush) <= p.AmbushCap);
                    Assert.NotNull(m.BattleOf(p.Id));
                }
                foreach (var b in m.Battles)
                {
                    HanBot.PlayOut(b, rng);
                    Assert.All(b.Sides, s => Assert.True(s.Focus >= 0));
                    Assert.Equal(3, b.Outcomes.Count);
                }
                Assert.True(m.EndRound());
                AssertConserved(m, start);
            }
            Assert.Single(m.Players, p => p.Place == 1);
            Assert.All(m.Players, p => Assert.InRange(p.Place, 1, players));
        }
    }

    [Fact]
    public void Eight_bot_matches_land_near_the_measured_targets()
    {
        var games = 60;
        var rounds = new List<int>();
        var heroes = 0;
        var l8 = 0;
        var place = new Dictionary<HanBot.Style, List<int>>();
        for (var g = 0; g < games; g++)
        {
            var m = New(1000 + g);
            HanBot.RunToEnd(m, new Random(g));
            rounds.Add(m.Round);
            foreach (var p in m.Players)
            {
                if (!place.TryGetValue(HanBot.StyleOf(m, p.Id), out var l)) place[HanBot.StyleOf(m, p.Id)] = l = new List<int>();
                l.Add(p.Place);
                if (p.Level >= Tuning.BotCap) l8++;
            }
            heroes += CardSet.All.Where(d => d.IsHero).Sum(d => 1 - Math.Min(1, m.Pool.GetValueOrDefault(d.Id)));
        }
        output.WriteLine($"ortalama round {rounds.Average():F1} (en az {rounds.Min()}, en çok {rounds.Max()}), L8 oranı %{100.0 * l8 / (games * 8):F0}");
        foreach (var kv in place.OrderBy(k => k.Key)) output.WriteLine($"{kv.Key}: ortalama sıra {kv.Value.Average():F2}");
        Assert.InRange(rounds.Average(), 16, 32);
    }
}
