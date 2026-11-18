using HanLigi;

namespace HanLigi.Tests;

/// <summary>Hazırlıkta esneyen masa sınırı (fazlası kilitte depoya ya da yanar) ve sürükle-bırak takası (§15.4).</summary>
public class OverflowSwapTests
{
    private static Match Ready(int seed, int players = 2)
    {
        var m = new Match(seed, Enumerable.Range(0, players).Select(i => ($"O{i}", true)).ToList());
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[0]);
        return m;
    }

    private static OwnedCard Give(Match m, PlayerState p, string id)
    {
        var d = CardSet.Get(id);
        m.Pool[d.Id]--;
        var c = new OwnedCard { Uid = 50000 + p.Id * 1000 + p.Cards.Count, Def = d };
        p.Cards.Add(c);
        return c;
    }

    private static readonly string[] Units = { "ghoul", "griffin", "fiend", "dethmold", "trebuchet", "elven_skirmisher", "stefan_skellen", "ghoul", "griffin", "fiend", "dethmold", "trebuchet" };

    private static Dictionary<string, int> Copies(Match m) => CardSet.All.ToDictionary(d => d.Id, d => m.TotalCopies(d.Id));

    /// <summary>Oyuncu 0'ın sahasına sınırdan <paramref name="extra"/> fazla, deposuna <paramref name="bench"/> kart koyar.</summary>
    private static PlayerState Overfill(Match m, int extra, int bench)
    {
        var p = m.Players[0];
        for (var i = 0; i < p.Slots + extra; i++)
        {
            var c = Give(m, p, Units[i % Units.Length]);
            Assert.True(m.Move(p.Id, c.Uid, Match.ZonesFor(c.Def).First(z => z != Zone.Ambush)));
        }
        for (var i = 0; i < bench; i++) Give(m, p, "ghoul");
        Assert.Equal(p.Slots + extra, p.Used);
        Assert.Equal(extra > 0, p.Overfull);
        return p;
    }

    [Fact]
    public void Prep_allows_more_cards_than_the_board_limit()
    {
        var m = Ready(3);
        var p = Overfill(m, 2, 0);
        Assert.True(p.Used > p.Slots);
        Assert.True(m.WouldOverflow(p.Id, Give(m, p, "griffin").Uid, Zone.Close));
    }

    [Fact]
    public void Overflow_goes_to_the_bench_when_there_is_room()
    {
        var m = Ready(5);
        var start = Copies(m);
        var p = Overfill(m, 2, 3);
        var cards = p.Cards.Count;
        m.ForceLock();
        Assert.Equal(p.Slots, p.Used);
        Assert.Equal(5, p.Bench.Count());
        Assert.Equal(cards, p.Cards.Count);
        Assert.Equal(2, p.Overflowed.Count);
        Assert.All(p.Overflowed, o => Assert.False(o.Sold));
        Assert.Equal(start, Copies(m));
    }

    [Fact]
    public void Overflow_sells_cards_when_the_bench_is_full()
    {
        var m = Ready(6);
        var start = Copies(m);
        var p = Overfill(m, 2, Tuning.BenchSize);
        var cards = p.Cards.Count;
        var gold = p.Gold;
        m.ForceLock();
        Assert.Equal(p.Slots, p.Used);
        Assert.Equal(Tuning.BenchSize, p.Bench.Count());
        Assert.Equal(cards - 2, p.Cards.Count);
        Assert.Equal(2, p.Overflowed.Count(o => o.Sold));
        Assert.All(p.Overflowed, o => Assert.Equal(Tuning.SellValue(o.Def, o.Star), o.Gold));
        Assert.Equal(gold + p.Overflowed.Sum(o => o.Gold), p.Gold);   // satış değeri altın olarak gelir
        Assert.All(p.Overflowed, o => Assert.DoesNotContain(p.Cards, c => c.Uid == o.Uid));
        Assert.Equal(start, Copies(m));   // satılan kartın kopyaları havuza döner
    }

    [Fact]
    public void Overflow_fills_the_bench_then_sells_the_rest()
    {
        var m = Ready(8);
        var p = Overfill(m, 3, Tuning.BenchSize - 1);
        m.ForceLock();
        Assert.Equal(p.Slots, p.Used);
        Assert.Equal(Tuning.BenchSize, p.Bench.Count());
        Assert.Equal(new[] { false, true, true }, p.Overflowed.Select(o => o.Sold).ToArray());
        // depoya inen kart sahadakilerden rastgele seçilir: depoda, sahada değil
        Assert.Equal(Zone.Bench, p.Cards.First(c => c.Uid == p.Overflowed[0].Uid).Zone);
    }

    [Fact]
    public void Overflow_choice_is_seeded_and_varies_with_the_seed()
    {
        string Run(int seed)
        {
            var m = Ready(seed);
            var p = Overfill(m, 3, 0);
            m.ForceLock();
            return string.Join(",", p.Overflowed.Select(o => o.Uid));
        }
        Assert.Equal(Run(11), Run(11));
        Assert.Equal(Run(12), Run(12));
        // farklı tohumlar en az bir kez farklı kart seçer (rastgele, hep aynı kart değil)
        Assert.True(Enumerable.Range(20, 10).Select(Run).Distinct().Count() > 1);
    }

    [Fact]
    public void Sold_overflow_choice_is_random_too()
    {
        // depo doluyken satılan kartlar da tohuma göre değişir (hep aynı kart değil)
        string Run(int seed)
        {
            var m = Ready(seed);
            var p = Overfill(m, 2, Tuning.BenchSize);
            m.ForceLock();
            return string.Join(",", p.Overflowed.Select(o => o.Def.Id + o.Uid));
        }
        Assert.True(Enumerable.Range(40, 12).Select(Run).Distinct().Count() > 1);
    }

    [Fact]
    public void Shop_stays_the_same_from_prep_into_battle_and_rolls_at_next_prep()
    {
        var m = Ready(17, 2);
        var p = m.Players[0];
        var prep = p.Shop.Select(d => d?.Id).ToArray();
        foreach (var x in m.Players) m.SetReady(x.Id);
        Assert.Equal(Phase.Battle, m.Phase);
        Assert.Equal(prep, p.Shop.Select(d => d?.Id).ToArray());   // savaşta aynı tezgâh
        foreach (var b in m.Battles) { b.Start(); while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); } }
        foreach (var b in m.Battles) m.Settle(b);
        Assert.Equal(prep, p.Shop.Select(d => d?.Id).ToArray());   // savaş bitince de
        Assert.True(m.EndRound());
        Assert.Equal(Phase.Prep, m.Phase);
        Assert.NotEqual(prep, p.Shop.Select(d => d?.Id).ToArray()); // yenileme yalnız sonraki hazırlığın başında
    }

    [Fact]
    public void Ready_lock_trims_overflow_too()
    {
        var m = Ready(13);
        var p = Overfill(m, 1, 0);
        foreach (var x in m.Players) m.SetReady(x.Id);
        Assert.Equal(Phase.Battle, m.Phase);
        Assert.Equal(p.Slots, p.Used);
        // savaşa yalnız sınır kadar kart girer
        var b = m.BattleOf(p.Id)!;
        var s = b.Sides[m.SideOf(b, p.Id)];
        Assert.Equal(p.Slots, s.Units.Count + s.Specials.Count);
    }

    [Fact]
    public void Swap_exchanges_bench_and_field_cards()
    {
        var m = Ready(4);
        var p = m.Players[0];
        var onBoard = Give(m, p, "ghoul");
        Assert.True(m.Move(p.Id, onBoard.Uid, Zone.Close));
        var inBench = Give(m, p, "griffin");
        Assert.True(m.SwapCards(p.Id, inBench.Uid, onBoard.Uid));
        Assert.Equal(Zone.Bench, onBoard.Zone);
        Assert.Equal(Zone.Close, inBench.Zone);
        Assert.True(m.SwapCards(p.Id, onBoard.Uid, inBench.Uid));      // geri: sıra fark etmez
        Assert.Equal(Zone.Close, onBoard.Zone);
        Assert.Equal(Zone.Bench, inBench.Zone);
    }

    [Fact]
    public void Swap_requires_each_card_to_fit_the_others_place()
    {
        var m = Ready(4);
        var p = m.Players[0];
        var ghoul = Give(m, p, "ghoul");
        Assert.True(m.Move(p.Id, ghoul.Uid, Zone.Close));
        var treb = Give(m, p, "trebuchet");
        var before = string.Join(",", p.Cards.Select(c => c.Uid + ":" + c.Zone));
        Assert.False(m.SwapCards(p.Id, treb.Uid, ghoul.Uid));          // Mancınık Yakın'a gidemez
        Assert.Equal(before, string.Join(",", p.Cards.Select(c => c.Uid + ":" + c.Zone)));

        var griffin = Give(m, p, "griffin");
        Assert.True(m.SwapCards(p.Id, griffin.Uid, ghoul.Uid));       // ikisi de Yakın
        Assert.Equal(Zone.Close, griffin.Zone);
        Assert.Equal(Zone.Bench, ghoul.Zone);

        Assert.True(m.Move(p.Id, treb.Uid, Zone.Siege));
        Assert.False(m.SwapCards(p.Id, treb.Uid, griffin.Uid));        // saha ↔ saha: satırlar uymuyor
        Assert.False(m.SwapCards(p.Id, treb.Uid, treb.Uid));
        Assert.False(m.SwapCards(p.Id, treb.Uid, 999));

        // özel kart birimin yerine geçemez, depodaki birimle yer değiştirebilir
        var frost = Give(m, p, "frost");
        Assert.True(m.Move(p.Id, frost.Uid, Zone.Special));
        Assert.False(m.SwapCards(p.Id, frost.Uid, griffin.Uid));
        Assert.True(m.SwapCards(p.Id, frost.Uid, ghoul.Uid) == false);   // Gulyabani özel yere gidemez
        var horn = Give(m, p, "horn");
        Assert.True(m.SwapCards(p.Id, horn.Uid, frost.Uid));
        Assert.Equal(Zone.Special, horn.Zone);
        Assert.Equal(Zone.Bench, frost.Zone);
    }

    [Fact]
    public void Swap_in_the_same_zone_reorders_and_horn_inherits_the_row()
    {
        var m = Ready(9);
        var p = m.Players[0];
        var a = Give(m, p, "ghoul");
        var b = Give(m, p, "griffin");
        Assert.True(m.SwapCards(p.Id, a.Uid, b.Uid));                   // depo ↔ depo: sıra değişir
        Assert.Equal(new[] { b.Uid, a.Uid }, p.Cards.Select(c => c.Uid).ToArray());
        Assert.All(p.Cards, c => Assert.Equal(Zone.Bench, c.Zone));

        var shield = Give(m, p, "shield");
        Assert.True(m.Move(p.Id, shield.Uid, Zone.Special, Row.Siege));
        var horn = Give(m, p, "horn");
        Assert.True(m.SwapCards(p.Id, horn.Uid, shield.Uid));
        Assert.Equal(Row.Siege, horn.Aim);                              // yerine geçtiği kartın satırı
        Assert.Null(shield.Aim);
    }

    [Fact]
    public void Swap_is_rejected_after_lock_and_for_bots_over_the_wire()
    {
        var g = new HanGame(77, new List<(string, bool)> { ("A", false), ("B", true) });
        g.Apply(new HanAction(HanActKind.School, 0, (int)g.M.Players[0].SchoolOptions[0]));
        var p = g.M.Players[0];
        var x = Give(g.M, p, "ghoul");
        var y = Give(g.M, p, "griffin");
        Assert.False(g.Apply(new HanAction(HanActKind.SwapCards, 1, x.Uid, y.Uid)));   // başkasının adına
        Assert.True(g.Apply(new HanAction(HanActKind.SwapCards, 0, x.Uid, y.Uid)));
        Assert.True(g.Apply(new HanAction(HanActKind.Lock, -1)));
        var fp = g.Fingerprint();
        Assert.False(g.Apply(new HanAction(HanActKind.SwapCards, 0, x.Uid, y.Uid)));
        Assert.Equal(fp, g.Fingerprint());
    }
}
