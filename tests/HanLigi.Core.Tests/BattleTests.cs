using HanLigi;

namespace HanLigi.Tests;

public class BattleTests
{
    private static int _uid = 1;

    private static BattleSide Side(string name, int level = 5, School? school = null) => new() { Name = name, Level = level, School = school };

    private static BattleUnit Add(BattleSide s, string id, Row? row = null, int star = 1, int? power = null)
    {
        var d = CardSet.Get(id);
        var u = new BattleUnit { Uid = _uid++, Def = d, Star = star, Power = power ?? Tuning.Power(d, star), Row = row ?? d.Row };
        s.Units.Add(u);
        return u;
    }

    private static Battle Make(BattleSide a, BattleSide b, params Row[] order)
        => new(a, b, order.Length == 3 ? order : new[] { Row.Close, Row.Ranged, Row.Siege }, 1);

    private static WindowChoice C(params Sign[] s) => new(s);

    private static WindowOutcome Play(Battle b, WindowChoice a, WindowChoice c)
    {
        Assert.True(b.Submit(0, a));
        Assert.True(b.Submit(1, c));
        return b.Resolve()!;
    }

    [Fact]
    public void Row_with_more_power_wins_and_majority_of_rows_wins_battle()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");                 // Yakın 8
        Add(a, "dethmold");              // Menzilli 6
        Add(b, "griffin");               // Yakın 5
        Add(b, "tibor_eggebracht");      // Menzilli 11
        Add(b, "trebuchet");             // Kuşatma 6
        var bt = Make(a, b);
        bt.Start();
        Assert.Equal(0, Play(bt, WindowChoice.None, WindowChoice.None).Winner);
        Assert.Equal(1, Play(bt, WindowChoice.None, WindowChoice.None).Winner);
        Assert.Equal(1, Play(bt, WindowChoice.None, WindowChoice.None).Winner);
        Assert.True(bt.Done);
        Assert.Equal(1, bt.Winner);
        // L5, 2-1: ⌈2,25 × (5 + 1)⌉ = 14
        Assert.Equal(14, bt.Damage);
    }

    [Fact]
    public void Damage_matches_spec_examples()
    {
        int Dmg(int level, int rowsWon)
        {
            var (a, b) = (Side("A", level), Side("B", level));
            Add(a, "fiend"); Add(a, "dethmold"); Add(a, "trebuchet");
            if (rowsWon == 2) Add(b, "heavy_zerrikanian_fire_scorpion");
            var bt = Make(a, b);
            bt.Start();
            for (var i = 0; i < 3; i++) Play(bt, WindowChoice.None, WindowChoice.None);
            return bt.Damage;
        }
        Assert.Equal(9, Dmg(3, 2));
        Assert.Equal(14, Dmg(3, 3));
        Assert.Equal(21, Dmg(8, 2));
        Assert.Equal(25, Dmg(8, 3));
    }

    [Fact]
    public void Quen_blocks_igni_and_refunds_focus()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        Add(b, "fiend");
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, C(Sign.Igni), C(Sign.Quen));
        Assert.Equal(8, o.ValueB);                         // Igni engellendi
        Assert.Equal(Tuning.StartFocus - 2, a.Focus);
        Assert.Equal(Tuning.StartFocus, b.Focus);          // 1 harcadı, 1 geri aldı
    }

    [Fact]
    public void Igni_halves_strongest_non_hero_and_skips_heroes()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "griffin");
        Add(b, "geralt");
        Add(b, "fiend");
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, C(Sign.Igni), WindowChoice.None);
        Assert.Equal(22 + 4, o.ValueB);                    // Geralt dokunulmaz, İblis 8 → 4
    }

    [Fact]
    public void Aard_cannot_be_blocked_and_affects_heroes()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "griffin");
        Add(b, "vesemir");                                 // 20
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, C(Sign.Aard), C(Sign.Quen));
        Assert.Equal(18, o.ValueB);
    }

    [Fact]
    public void Percentages_on_the_same_row_add_up()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "griffin");
        Add(b, "fiend"); Add(b, "stefan_skellen");         // 17
        b.Specials.Add(Special.Horn);
        var bt = Make(a, b);
        bt.Start();                                        // Boynuz varsayılanla en güçlü satıra (Yakın)
        var o = Play(bt, C(Sign.Aard), WindowChoice.None);
        Assert.Equal(Tuning.Round(17 * 1.4), o.ValueB);    // +%50 −%10 = +%40
    }

    [Fact]
    public void Weather_sets_both_sides_to_one_but_not_heroes()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend"); Add(a, "griffin");
        a.Specials.Add(Special.Frost);
        Add(b, "draug"); Add(b, "geralt");
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, WindowChoice.None, WindowChoice.None);
        Assert.Equal(2, o.ValueA);
        Assert.Equal(1 + 22, o.ValueB);
    }

    [Fact]
    public void Swallow_removes_weather_only_on_own_side()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        a.Specials.Add(Special.Frost);
        a.Potions.Add(Potion.Swallow);
        Add(b, "draug");
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, new WindowChoice(Array.Empty<Sign>(), Potion.Swallow), WindowChoice.None);
        Assert.Equal(8, o.ValueA);
        Assert.Equal(1, o.ValueB);
        Assert.Empty(a.Potions);
    }

    [Fact]
    public void Scorch_kills_all_tied_strongest_non_heroes()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend"); Add(a, "griffin");
        a.Specials.Add(Special.Scorch);
        Add(b, "catapult", power: 8); Add(b, "geralt");
        var bt = Make(a, b);
        bt.Start();
        Assert.DoesNotContain(a.Units, u => u.Def.Id == "fiend");
        Assert.DoesNotContain(b.Units, u => u.Def.Id == "catapult");
        Assert.Contains(b.Units, u => u.Def.Id == "geralt");
        Assert.Contains(a.Units, u => u.Def.Id == "griffin");
    }

    [Fact]
    public void Yrden_does_not_stop_ambush_since_meta4()
    {
        var (a, b) = (Side("A"), Side("B"));
        var amb = new BattleUnit { Uid = 5000, Def = CardSet.Get("dol_blathanna_scout"), Power = 7, Row = Row.Close };
        a.Ambush.Add(amb);
        Add(b, "sweers", Row.Siege);
        var bt = Make(a, b);
        bt.Start();
        Play(bt, WindowChoice.None, WindowChoice.None);
        Play(bt, WindowChoice.None, WindowChoice.None);
        var o = Play(bt, new WindowChoice(Array.Empty<Sign>(), null, 5000), C(Sign.Yrden));
        Assert.Equal(0, o.ValueA);   // Yrden son pencerede de mutlak keser: bilerek indirilen pusu inmez
        Assert.Equal(-1, o.Winner);
        Assert.Single(a.Ambush);
    }

    [Fact]
    public void Yrden_delays_a_chosen_ambush_until_a_later_row()
    {
        var (a, b) = (Side("A"), Side("B"));
        var amb = new BattleUnit { Uid = 5000, Def = CardSet.Get("dol_blathanna_scout"), Power = 7, Row = Row.Close };
        a.Ambush.Add(amb);
        Add(b, "sweers", Row.Siege);
        var bt = Make(a, b);
        bt.Start();
        var o1 = Play(bt, new WindowChoice(Array.Empty<Sign>(), null, 5000), C(Sign.Yrden));
        Assert.Equal(0, o1.ValueA);            // inemedi
        Assert.Single(a.Ambush);               // pusuda kaldı
        var o2 = Play(bt, new WindowChoice(Array.Empty<Sign>(), null, 5000), WindowChoice.None);
        Assert.Equal(7, o2.ValueA);            // sonraki satırda indi
        Assert.Empty(a.Ambush);
    }

    [Fact]
    public void Yrden_blocks_aard_and_both_pay()
    {
        var (a, b) = (Side("A"), Side("B"));
        a.Units.Add(new BattleUnit { Uid = 1, Def = CardSet.Get("ghoul"), Power = 10, Row = Row.Close });
        b.Units.Add(new BattleUnit { Uid = 2, Def = CardSet.Get("ghoul"), Power = 10, Row = Row.Close });
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, C(Sign.Aard), C(Sign.Yrden));
        Assert.Equal(10, o.ValueB);   // Aard tutuldu
        Assert.Equal(-1, o.Winner);
        Assert.Equal(Tuning.StartFocus - 1, a.Focus);
        Assert.Equal(Tuning.StartFocus - 1, b.Focus);
        Assert.DoesNotContain(o.RowMods, m => m.Source == "Aard");
    }

    [Fact]
    public void Ambush_lands_in_the_current_row_and_auto_lands_in_last_window()
    {
        var (a, b) = (Side("A"), Side("B"));
        a.Ambush.Add(new BattleUnit { Uid = 5001, Def = CardSet.Get("dol_blathanna_scout"), Power = 7, Row = Row.Close });
        Add(b, "trebuchet");
        var bt = Make(a, b);
        bt.Start();
        Play(bt, WindowChoice.None, WindowChoice.None);
        Play(bt, WindowChoice.None, WindowChoice.None);
        var o = Play(bt, new WindowChoice(Array.Empty<Sign>(), null, 5001), WindowChoice.None);   // son pencerede de bilerek indirilir
        Assert.Equal(Row.Siege, o.Row);
        Assert.Equal(7, o.ValueA);
        Assert.Equal(0, o.Winner);
    }

    [Fact]
    public void Ambush_does_not_land_on_its_own_in_last_window()
    {
        var (a, b) = (Side("A"), Side("B"));
        a.Ambush.Add(new BattleUnit { Uid = 5001, Def = CardSet.Get("dol_blathanna_scout"), Power = 7, Row = Row.Close });
        Add(b, "trebuchet");
        var bt = Make(a, b);
        bt.Start();
        Play(bt, WindowChoice.None, WindowChoice.None);
        Play(bt, WindowChoice.None, WindowChoice.None);
        var o = Play(bt, WindowChoice.None, WindowChoice.None);
        Assert.Equal(0, o.ValueA);
        Assert.Single(a.Ambush);
    }

    [Fact]
    public void Rage_only_when_the_lost_row_had_a_unit()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "griffin");                                  // Yakın 5; Menzilli boş
        Add(b, "fiend"); Add(b, "dethmold");
        var bt = Make(a, b);
        bt.Start();
        Play(bt, WindowChoice.None, WindowChoice.None);     // Yakın kaybedildi, birim vardı
        Assert.Equal(Tuning.StartFocus + 1, a.Focus);
        Play(bt, WindowChoice.None, WindowChoice.None);     // Menzilli boştu: Öfke yok
        Assert.Equal(Tuning.StartFocus + 1, a.Focus);
    }

    [Fact]
    public void Bond_and_growth_follow_spec_caps()
    {
        var s = Side("A");
        var bond = Add(s, "blue_stripes_commando");         // Asker, Bond, 4
        for (var i = 0; i < 5; i++) Add(s, "poor_fucking_infantry");   // Asker
        var growth = Add(s, "ghoul");                        // Büyüme, 1
        var bt = Make(s, Side("B"));
        Assert.Equal(4 + 3, bt.Value(s, bond));
        Assert.Equal(1 + 4, bt.Value(s, growth));
    }

    [Fact]
    public void Mirror_copies_half_of_the_strongest_in_row()
    {
        var s = Side("A");
        var m = Add(s, "fringilla_vigo", power: 2);
        Add(s, "tibor_eggebracht");                          // 11, Menzilli
        var bt = Make(s, Side("B"));
        Assert.Equal(5, bt.Value(s, m));
    }

    [Fact]
    public void Black_blood_reflects_igni_onto_the_caster()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        Add(b, "griffin");
        b.Potions.Add(Potion.BlackBlood);
        var bt = Make(a, b);
        bt.Start();
        var o = Play(bt, C(Sign.Igni), new WindowChoice(Array.Empty<Sign>(), Potion.BlackBlood));
        Assert.Equal(4, o.ValueA);
        Assert.Equal(5, o.ValueB);
    }

    [Fact]
    public void Invalid_choices_are_rejected_without_changing_state()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        Add(b, "griffin");
        var bt = Make(a, b);
        bt.Start();
        Assert.False(bt.Submit(0, C(Sign.Igni, Sign.Aard)));                                   // Jaskier yok
        Assert.False(bt.Submit(0, new WindowChoice(Array.Empty<Sign>(), Potion.Thunderbolt)));  // kart yok
        Assert.False(bt.Submit(0, new WindowChoice(Array.Empty<Sign>(), null, 42)));            // pusu yok
        a.Focus = 1;
        Assert.False(bt.Submit(0, C(Sign.Igni)));                                              // Odak yetmez
        Assert.Equal(1, a.Focus);
        Assert.False(bt.Submitted(0));
    }

    [Fact]
    public void Equal_points_are_decided_by_total_and_full_tie_deals_no_damage()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        Add(b, "fiend");
        var bt = Make(a, b);
        bt.Start();
        for (var i = 0; i < 3; i++) Play(bt, WindowChoice.None, WindowChoice.None);
        Assert.Equal(-1, bt.Winner);
        Assert.Equal(0, bt.Damage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quen_refund_returns_only_the_focus_actually_paid(bool triss)
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");                       // Yakın: A satırı alır (Öfke karışmasın)
        if (triss) Add(a, "triss", Row.Siege); // ilk Quen bedava
        Add(b, "griffin");
        var bt = Make(a, b);
        bt.Start();
        var before = bt.Sides[0].Focus;
        var o = Play(bt, C(Sign.Quen), C(Sign.Igni));
        Assert.Equal(0, o.Winner);
        Assert.Contains(o.Fx, f => f.Kind == "igni" && f.Blocked);
        // ödenmiş Quen: 1 harcanır, 1 geri gelir; bedava Quen: hiçbir şey harcanmaz, geri ödeme de yok
        Assert.Equal(before, bt.Sides[0].Focus);
    }
}
