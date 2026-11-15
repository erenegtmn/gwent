using HanLigi;
using Xunit.Abstractions;

namespace HanLigi.Tests;

/// <summary>
/// Savaş matematiği denetimi: her yüzdesel etki gerçek motordan geçirilir (küçük değerler dahil) ve etkinin gerçekten bir
/// şey değiştirip değiştirmediği tabloya dökülür. Kural: uygulanan her yüzde etkisi değeri <b>en az 1 puan</b> oynatır.
/// </summary>
public class MathAudit(ITestOutputHelper output)
{
    private static int _uid = 5000;

    private static BattleUnit Unit(BattleSide s, string id, int power, Row row)
    {
        var u = new BattleUnit { Uid = _uid++, Def = CardSet.Get(id), Power = power, Row = row };
        s.Units.Add(u);
        return u;
    }

    /// <summary>B tarafının Yakın satırında tek kart (güç p); A bir seçim yapar. B'nin satır değerini döner.</summary>
    private static (int Row, int Card) Hit(int p, WindowChoice a, Potion? potionA = null, WindowChoice? bChoice = null, Potion? potionB = null)
    {
        var sa = new BattleSide { Name = "A", Level = 5 };
        var sb = new BattleSide { Name = "B", Level = 5 };
        Unit(sa, "dwarven_skirmisher", 1, Row.Ranged);
        var t = Unit(sb, "dwarven_skirmisher", p, Row.Close);
        if (potionA is { } pa) sa.Potions.Add(pa);
        if (potionB is { } pb) sb.Potions.Add(pb);
        var bt = new Battle(sa, sb, new[] { Row.Close, Row.Ranged, Row.Siege }, 1);
        bt.Start();
        Assert.True(bt.Submit(0, a), "A seçimi geçersiz");
        Assert.True(bt.Submit(1, bChoice ?? WindowChoice.None));
        var o = bt.Resolve()!;
        return (o.ValueB, o.CardValues[t.Uid]);
    }

    /// <summary>A tarafının Yakın satırında tek kart (güç p) ve A'nın kendi iksiri: A'nın satır değeri.</summary>
    private static int Self(int p, Potion potion)
    {
        var sa = new BattleSide { Name = "A", Level = 5 };
        var sb = new BattleSide { Name = "B", Level = 5 };
        Unit(sa, "dwarven_skirmisher", p, Row.Close);
        sa.Potions.Add(potion);
        var bt = new Battle(sa, sb, new[] { Row.Close, Row.Ranged, Row.Siege }, 1);
        bt.Start();
        Assert.True(bt.Submit(0, new WindowChoice(Array.Empty<Sign>(), potion)));
        Assert.True(bt.Submit(1, WindowChoice.None));
        return bt.Resolve()!.ValueA;
    }

    [Fact]
    public void Every_percentage_effect_changes_the_value_by_at_least_one()
    {
        var bad = new List<string>();
        var lines = new List<string> { "güç | Aard(satır) | Igni(kart) | DansYıldız | KuzeyRüzgârı | GökGürültüsü" };
        for (var p = 1; p <= 12; p++)
        {
            var aard = Hit(p, new WindowChoice(new[] { Sign.Aard })).Row;
            var igni = Hit(p, new WindowChoice(new[] { Sign.Igni })).Card;
            var star = Hit(p, new WindowChoice(Array.Empty<Sign>(), Potion.DancingStar), Potion.DancingStar).Card;
            var wind = Hit(p, new WindowChoice(Array.Empty<Sign>(), Potion.NorthernWind), Potion.NorthernWind).Card;
            var thunder = Self(p, Potion.Thunderbolt);
            lines.Add($"{p,3} | {p}→{aard} | {p}→{igni} | {p}→{star} | {p}→{wind} | {p}→{thunder}");
            if (aard >= p) bad.Add($"Aard güç {p}: değişmedi ({aard})");
            if (igni >= p) bad.Add($"Igni güç {p}: değişmedi ({igni})");
            if (star >= p) bad.Add($"Dans Eden Yıldız güç {p}: değişmedi ({star})");
            if (wind >= p) bad.Add($"Kuzey Rüzgârı güç {p}: değişmedi ({wind})");
            if (thunder <= p) bad.Add($"Gök Gürültüsü güç {p}: değişmedi ({thunder})");
            // etki gerçek yüzdeye yakın olmalı (en çok 1 puan sapma)
            Assert.InRange(aard, p * 0.9 - 1, p * 0.9 + 0.5);
            Assert.InRange(igni, p * 0.5 - 1, p * 0.5 + 0.5);
            Assert.InRange(thunder, p * 1.25 - 0.5, p * 1.25 + 1);
        }
        foreach (var l in lines) output.WriteLine(l);
        foreach (var l in bad) output.WriteLine("ETKİSİZ: " + l);
        Assert.Empty(bad);
    }

    // ---------- Odak ekonomisi ----------

    private static Battle Duel(Action<BattleSide>? a = null, Action<BattleSide>? b = null, School? sa = null, School? sb = null)
    {
        var A = new BattleSide { Name = "A", Level = 5, School = sa };
        var B = new BattleSide { Name = "B", Level = 5, School = sb };
        Unit(A, "dwarven_skirmisher", 8, Row.Close);
        Unit(B, "dwarven_skirmisher", 8, Row.Close);
        a?.Invoke(A);
        b?.Invoke(B);
        var bt = new Battle(A, B, new[] { Row.Close, Row.Ranged, Row.Siege }, 1);
        bt.Start();
        return bt;
    }

    private static WindowOutcome Round(Battle bt, WindowChoice a, WindowChoice b)
    {
        Assert.True(bt.Submit(0, a), "A geçersiz");
        Assert.True(bt.Submit(1, b), "B geçersiz");
        return bt.Resolve()!;
    }

    private static WindowChoice S(params Sign[] s) => new(s);

    [Fact]
    public void Focus_start_bonuses()
    {
        Assert.Equal(3, Duel().Sides[0].Focus);
        Assert.Equal(4, Duel(sa: School.Wolf).Sides[0].Focus);                                      // Kurt +1
        Assert.Equal(4, Duel(a: A => Unit(A, "sabrina_glevissig", 3, Row.Ranged)).Sides[0].Focus);   // Ozan +1
        var mages = Duel(a: A => A.Tags[Tag.Mage] = 4);
        Assert.Equal(4, mages.Sides[0].Focus);                                                      // 4 Büyücü +1
    }

    [Fact]
    public void Focus_is_spent_exactly_and_overspending_is_rejected()
    {
        var bt = Duel();
        Assert.Equal(2, bt.Cost(0, S(Sign.Igni)));
        Assert.Equal(1, bt.Cost(0, S(Sign.Aard)));
        var o = Round(bt, S(Sign.Igni), S(Sign.Aard));
        Assert.Equal(0, o.Winner);            // A: 8 → Aard 7; B: 8 → Igni 4
        Assert.Equal(1, bt.Sides[0].Focus);   // 3 − 2
        Assert.Equal(3, bt.Sides[1].Focus);   // 3 − 1 + 1 Öfke (satırı kaybetti)
        Assert.False(bt.Valid(0, S(Sign.Igni)));   // 1 Odakla Igni (2) atılamaz
        Assert.False(bt.Submit(0, S(Sign.Igni)));
        Assert.True(bt.Valid(0, S(Sign.Quen)));
    }

    [Fact]
    public void Quen_block_refunds_one_focus_and_igni_is_still_paid()
    {
        var bt = Duel();
        var o = Round(bt, S(Sign.Igni), S(Sign.Quen));
        Assert.Equal(1, bt.Sides[0].Focus);   // Igni ödendi, boşa gitti
        Assert.Equal(3, bt.Sides[1].Focus);   // Quen 1 ödendi, engelleyince 1 geri
        Assert.Equal(8, o.ValueB);            // engellenen Igni değeri düşürmedi
    }

    [Fact]
    public void Rage_gives_focus_on_a_lost_row_and_is_capped()
    {
        var bt = Duel(a: A => { Unit(A, "dwarven_skirmisher", 1, Row.Ranged); Unit(A, "dwarven_skirmisher", 1, Row.Siege); A.Units[0].Power = 1; },
                      b: B => { Unit(B, "dwarven_skirmisher", 9, Row.Ranged); Unit(B, "dwarven_skirmisher", 9, Row.Siege); });
        var f = bt.Sides[0].Focus;
        Round(bt, WindowChoice.None, WindowChoice.None);   // A kaybeder: +1
        Assert.Equal(f + 1, bt.Sides[0].Focus);
        Round(bt, WindowChoice.None, WindowChoice.None);   // +1 (toplam 2)
        Assert.Equal(f + 2, bt.Sides[0].Focus);
        Round(bt, WindowChoice.None, WindowChoice.None);   // tavan: Öfke en çok 2
        Assert.Equal(f + 2, bt.Sides[0].Focus);
    }

    [Fact]
    public void Free_signs_geralt_triss_and_mage_six()
    {
        var g = Duel(a: A => Unit(A, "geralt", 22, Row.Close));
        Assert.Equal(0, g.Cost(0, S(Sign.Igni)));   // Geralt'ın satırında ilk işaret bedava
        Round(g, S(Sign.Igni), WindowChoice.None);
        Assert.Equal(3, g.Sides[0].Focus);

        var t = Duel(a: A => Unit(A, "triss", 18, Row.Ranged));
        Assert.Equal(0, t.Cost(0, S(Sign.Quen)));
        Round(t, S(Sign.Quen), WindowChoice.None);
        Assert.Equal(1, t.Cost(0, S(Sign.Quen)));   // yalnız ilk Quen
        Assert.Equal(3, t.Sides[0].Focus);

        var m = Duel(a: A => A.Tags[Tag.Mage] = 6);
        Assert.Equal(0, m.Cost(0, S(Sign.Igni)));
        Round(m, S(Sign.Igni), WindowChoice.None);
        Assert.Equal(2, m.Cost(0, S(Sign.Igni)));   // savaş başına bir kez
    }

    // ---------- yüzdelerin güçlendirilmesi ve üst üste binmesi ----------

    [Fact]
    public void School_and_mage_amplifiers_scale_signs()
    {
        int IgniOn8(School? sc, int mages) => Round(Duel(a: A => { if (mages > 0) A.Tags[Tag.Mage] = mages; }, sa: sc), S(Sign.Igni), WindowChoice.None).CardValues.Values.Min();
        Assert.Equal(4, IgniOn8(null, 0));            // −%50
        Assert.Equal(2, IgniOn8(School.Griffin, 0));  // −%75
        Assert.Equal(3, IgniOn8(null, 2));            // −%62,5 → 3 (buçuk etkinin lehine)
        Assert.Equal(1, IgniOn8(School.Griffin, 2));  // −%87,5
        var aard = Round(Duel(sa: School.Griffin), S(Sign.Aard), WindowChoice.None);
        Assert.Equal(7, aard.ValueB);                 // 8 × 0,85 = 6,8 → 7 (en yakın tam sayı)
        Assert.Equal(15, aard.Fx.First(f => f.Kind == "aard").Target);
    }

    [Fact]
    public void Stacked_reductions_never_go_negative()
    {
        var bt = Duel(a: A => A.Potions.Add(Potion.NorthernWind));
        var o = Round(bt, new WindowChoice(new[] { Sign.Igni }, Potion.NorthernWind), WindowChoice.None);
        Assert.Equal(0, o.ValueB);                    // −%50 −%70 → 0, eksiye inmez
        Assert.All(o.CardValues.Values, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Row_bonuses_add_up_before_rounding()
    {
        // Boynuz +%50 ve Gök Gürültüsü +%25 aynı satırda: +%75 tek seferde
        var bt = Duel(a: A => { A.Specials.Add(Special.Horn); A.Potions.Add(Potion.Thunderbolt); });
        var o = Round(bt, new WindowChoice(Array.Empty<Sign>(), Potion.Thunderbolt), WindowChoice.None);
        Assert.Equal(14, o.ValueA);                   // 8 × 1,75
        Assert.Equal(8, o.RowBase[0]);
    }

    [Fact]
    public void Yrden_cancels_the_potion_and_both_still_pay()
    {
        var bt = Duel(b: B => B.Potions.Add(Potion.Thunderbolt));
        var o = Round(bt, S(Sign.Yrden), new WindowChoice(Array.Empty<Sign>(), Potion.Thunderbolt));
        Assert.Equal(8, o.ValueB);                    // iksir etkisiz
        Assert.Empty(bt.Sides[1].Potions);            // kart yine de harcandı
        Assert.Equal(2, bt.Sides[0].Focus);
    }

    [Fact]
    public void Weather_and_swallow()
    {
        var bt = Duel(a: A => { A.Specials.Add(Special.Frost); A.Potions.Add(Potion.Swallow); });
        var o = Round(bt, new WindowChoice(Array.Empty<Sign>(), Potion.Swallow), WindowChoice.None);
        Assert.Equal(8, o.ValueA);                    // Kırlangıç: hava kalktı
        Assert.Equal(1, o.ValueB);                    // rakipte Ayaz: 1
    }

    [Fact]
    public void Apply_pct_table()
    {
        for (var v = 0; v <= 40; v++)
            foreach (var p in new[] { -1.2, -1.0, -0.875, -0.7, -0.5, -0.3, -0.15, -0.1, 0.1, 0.2, 0.25, 0.3, 0.5, 0.75 })
            {
                var r = Tuning.ApplyPct(v, p);
                Assert.True(r >= 0);
                if (v == 0) { Assert.Equal(0, r); continue; }
                if (p < 0) Assert.True(r < v, $"{v} {p} → {r}"); else Assert.True(r > v, $"{v} {p} → {r}");
                Assert.True(Math.Abs(r - Math.Max(0, v * (1 + p))) <= 1.0 + 1e-9, $"{v} {p} → {r}");
            }
        Assert.Equal(1, Tuning.ApplyPct(3, -0.5));       // 1,5 → 1 (düşüşte buçuk aşağı)
        Assert.Equal(3, Tuning.ApplyPct(2, 0.25));       // 2,5 → 3 (artışta buçuk yukarı)
        Assert.Equal(4, Tuning.ApplyPct(5, -0.1));       // 4,5 → 4 (eskiden 5'te kalıyordu)
    }
}
