using HanLigi;

namespace HanLigi.Tests;

/// <summary>
/// Bütün savaş mekaniklerinin kartta/ipucunda yazdığı gibi çalıştığının ve birbirleriyle doğru birleştiğinin denetimi:
/// işaretler, okullar, sınıf eşikleri, yetenekler, kahramanlar, simya kartları, özel kartlar, pusu, Odak/Öfke ve hasar.
/// Beklenen sayılar elle hesaplanmıştır (güç ondalıklı: <see cref="Tuning.ApplyPct"/> en yakın 0,1, tam orta etkinin lehine).
/// Satır sırası varsayılan Yakın → Menzilli → Kuşatma.
/// </summary>
public class MechanicsTests
{
    private static int _uid = 20000;

    private static BattleSide Side(string name, int level = 5, School? school = null, params (Tag, int)[] tags)
    {
        var s = new BattleSide { Name = name, Level = level, School = school };
        foreach (var (t, n) in tags) s.Tags[t] = n;
        return s;
    }

    private static BattleUnit Add(BattleSide s, string id, Row? row = null, int star = 1, int? power = null)
    {
        var d = CardSet.Get(id);
        var u = new BattleUnit { Uid = _uid++, Def = d, Star = star, Power = power ?? Tuning.Power(d, star), Row = row ?? d.Row };
        s.Units.Add(u);
        return u;
    }

    private static BattleUnit Hide(BattleSide s, string id, int? power = null)
    {
        var d = CardSet.Get(id);
        var u = new BattleUnit { Uid = _uid++, Def = d, Power = power ?? d.Power, Row = d.Row };
        s.Ambush.Add(u);
        return u;
    }

    private static Battle Start(BattleSide a, BattleSide b, Row[]? order = null, bool pve = false)
    {
        var bt = new Battle(a, b, order ?? new[] { Row.Close, Row.Ranged, Row.Siege }, 1) { Pve = pve };
        bt.Start();
        return bt;
    }

    private static WindowChoice C(params Sign[] s) => new(s);
    private static WindowChoice P(Potion p, params Sign[] s) => new(s, p);
    private static readonly WindowChoice N = WindowChoice.None;

    private static WindowOutcome Play(Battle b, WindowChoice a, WindowChoice c)
    {
        Assert.True(b.Submit(0, a), "A seçimi reddedildi");
        Assert.True(b.Submit(1, c), "B seçimi reddedildi");
        return b.Resolve()!;
    }

    private static decimal Val(WindowOutcome o, BattleUnit u) => o.CardValues[u.Uid];

    // ================= işaretler ve okullar =================

    [Fact]
    public void Igni_costs_two_and_quen_one_focus()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend"); Add(b, "fiend");
        var bt = Start(a, b);
        Play(bt, C(Sign.Igni), C(Sign.Aard));
        // A: 8 × 0,9 = 7; B: Igni ile 8 → 4. A kazanır. A: 3 − 2 = 1; B: 3 − 1 + Öfke 1 = 3
        Assert.Equal(0, a.Focus);
        Assert.Equal(2, b.Focus);
    }

    [Fact]
    public void Griffin_and_mage_two_amplify_aard_and_igni()
    {
        foreach (var (school, mage, igni, aard) in new (School?, int, decimal, decimal)[] { (null, 0, 6, 18), (School.Griffin, 0, 3, 17), (null, 2, 4.5m, 17.5m), (School.Griffin, 2, 1.5m, 16.5m) })
        {
            var (a, b) = (Side("A", school: school, tags: (Tag.Mage, mage)), Side("B"));
            Add(a, "ghoul", power: 1);
            Add(b, "fiend");                         // 8
            var t = Add(b, "draug", power: 12);      // 12 → Igni bunu vurur
            var bt = Start(a, b);
            var o = Play(bt, C(Sign.Igni), N);
            // −%50 / −%75 / −%62,5 / −%87,5 → 12 → 6 / 3 / 4,5 / 1,5
            Assert.Equal(igni, Val(o, t));
            var (a2, b2) = (Side("A", school: school, tags: (Tag.Mage, mage)), Side("B"));
            Add(a2, "ghoul", power: 1);
            Add(b2, "fiend", power: 10); Add(b2, "draug", power: 10);
            var o2 = Play(Start(a2, b2), C(Sign.Aard), N);
            // 20 × (1 − %10 / %15 / %12,5 / %17,5) = 18 / 17 / 17,5 / 16,5
            Assert.Equal(aard, o2.ValueB);
        }
    }

    [Fact]
    public void Aard_hits_heroes_but_igni_and_bombs_skip_them()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "ghoul", power: 1);
        var g = Add(b, "geralt");                    // 22, kahraman
        a.Potions.Add(Potion.NorthernWind);
        var bt = Start(a, b);
        var o = Play(bt, P(Potion.NorthernWind, Sign.Igni), N);
        Assert.Equal(22, Val(o, g));
        Assert.Contains(o.Fx, f => f.Kind == "igni" && f.Target == 0);
        var (a2, b2) = (Side("A"), Side("B"));
        Add(a2, "ghoul", power: 1);
        Add(b2, "geralt");
        var o2 = Play(Start(a2, b2), C(Sign.Aard), N);
        Assert.Equal(19.8m, o2.ValueB);              // 22 × 0,9 = 19,8 (Aard kahramana da işler)
    }

    [Fact]
    public void Quen_blocks_every_igni_and_bear_counter_hits_the_row()
    {
        var (a, b) = (Side("A", school: School.Bear), Side("B"));
        var mine = Add(a, "fiend");
        Add(b, "draug", power: 10);
        var bt = Start(a, b);
        var o = Play(bt, C(Sign.Quen), C(Sign.Igni));
        Assert.Equal(8, Val(o, mine));
        Assert.Equal(9, o.ValueB);                   // Ayı karşı vuruşu −%10
        Assert.Equal(3, a.Focus);                    // 3 − 1 + 1 iade + 1 Öfke (8 < 9)
    }

    [Fact]
    public void Black_blood_with_quen_reflects_instead_of_blocking()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        var hot = Add(b, "draug", power: 10);
        a.Potions.Add(Potion.BlackBlood);
        var bt = Start(a, b);
        var o = Play(bt, P(Potion.BlackBlood, Sign.Quen), C(Sign.Igni));
        Assert.Equal(5, Val(o, hot));                // yansıdı: atanın kendi en güçlü kartı −%50
        Assert.Equal(8, o.ValueA);
    }

    [Fact]
    public void Geralt_makes_only_his_rows_first_sign_free()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "geralt");                            // Yakın
        Add(b, "fiend"); Add(b, "dethmold");
        var bt = Start(a, b);
        Assert.Equal(0, bt.Cost(0, C(Sign.Igni)));
        Play(bt, C(Sign.Igni), N);
        Assert.Equal(2, a.Focus);                    // bedava, A satırı kazandı
        Assert.Equal(2, bt.Cost(0, C(Sign.Igni)));   // Menzilli: Geralt yok
    }

    [Fact]
    public void Mage_six_first_sign_free_once_per_battle()
    {
        var (a, b) = (Side("A", tags: (Tag.Mage, 6)), Side("B"));
        Add(a, "fiend"); Add(a, "dethmold");
        Add(b, "griffin"); Add(b, "sile_de_tansarville");
        var bt = Start(a, b);
        Assert.Equal(3, a.Focus);                    // Büyücü 4 eşiği +1
        Play(bt, C(Sign.Igni), N);
        Assert.Equal(3, a.Focus);
        Assert.Equal(2, bt.Cost(0, C(Sign.Igni)));
    }

    [Fact]
    public void Starting_focus_adds_wolf_bard_and_mage_four()
    {
        var a = Side("A", school: School.Wolf, tags: (Tag.Mage, 4));
        Add(a, "sabrina_glevissig"); Add(a, "sabrina_glevissig");
        var bt = Start(a, Side("B"));
        Assert.Equal(Tuning.StartFocus + 1 + 2 + 1, a.Focus);
    }

    [Fact]
    public void Triss_first_quen_free_then_paid()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "triss", Row.Siege); Add(a, "fiend"); Add(a, "dethmold");
        Add(b, "ghoul", power: 1); Add(b, "sweers");
        var bt = Start(a, b);
        Assert.Equal(0, bt.Cost(0, C(Sign.Quen)));
        Play(bt, C(Sign.Quen), N);
        Assert.Equal(2, a.Focus);
        Assert.Equal(1, bt.Cost(0, C(Sign.Quen)));
    }

    [Fact]
    public void Jaskier_two_signs_once_paid_separately_and_second_igni_retargets()
    {
        var (a, b) = (Side("A", school: School.Wolf), Side("B"));
        Add(a, "jaskier");
        Add(a, "sabrina_glevissig", Row.Siege);      // Ozan +1 Odak
        var f = Add(b, "fiend");                     // 8
        var g = Add(b, "griffin");                   // 5
        var bt = Start(a, b);
        Assert.Equal(4, a.Focus);                    // 2 + Kurt + Ozan
        var o = Play(bt, C(Sign.Igni, Sign.Igni), N);
        Assert.Equal(4, Val(o, f));                  // 8 → 4
        Assert.Equal(2.5m, Val(o, g));               // sonra en güçlü griffin 5 → 2,5
        Assert.Equal(0, a.Focus);
        Assert.False(bt.Valid(0, C(Sign.Aard, Sign.Aard)));   // savaş başına bir kez
    }

    [Fact]
    public void Yrden_does_not_touch_potions_since_meta4()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        a.Potions.Add(Potion.Thunderbolt);
        Add(b, "fiend");
        var bt = Start(a, b);
        var o = Play(bt, P(Potion.Thunderbolt), C(Sign.Yrden));
        Assert.Equal(10, o.ValueA);   // 8 +%25
        Assert.Empty(a.Potions);
    }

    // ================= simya =================

    [Fact]
    public void Thunderbolt_and_horn_add_up_as_row_percent()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");
        a.Specials.Add(Special.Horn);
        a.Act.Horns.Add(Row.Close);
        a.Potions.Add(Potion.Thunderbolt);
        Add(b, "draug");
        var bt = Start(a, b);
        var o = Play(bt, P(Potion.Thunderbolt), N);
        Assert.Equal(14, o.ValueA);                  // 8 × 1,75
    }

    [Fact]
    public void Dancing_star_hits_two_strongest_and_viper_strengthens_bombs()
    {
        foreach (var (viper, f, g) in new[] { (false, 5.6m, 3.5m), (true, 5m, 3.1m) })
        {
            var (a, b) = (Side("A", school: viper ? School.Viper : null), Side("B"));
            Add(a, "ghoul", power: 1);
            a.Potions.Add(Potion.DancingStar);
            var uf = Add(b, "fiend");                // 8
            var ug = Add(b, "griffin");              // 5
            var low = Add(b, "dwarven_skirmisher", Row.Close, power: 1);
            var o = Play(Start(a, b), P(Potion.DancingStar), N);
            // −%30: 8 → 5,6; 5 → 3,5.  Engerek −%37,5: 8 → 5; 5 → 3,125 → 3,1
            Assert.Equal(f, Val(o, uf));
            Assert.Equal(g, Val(o, ug));
            Assert.Equal(1, Val(o, low));
        }
    }

    [Fact]
    public void Northern_wind_dwarf_halving_and_zoltan_immunity()
    {
        decimal Hit(bool dwarfTh, bool zoltan)
        {
            var (a, b) = (Side("A"), Side("B", tags: (Tag.Dwarf, dwarfTh ? 2 : 0)));
            Add(a, "ghoul", power: 1);
            a.Potions.Add(Potion.NorthernWind);
            var t = Add(b, "barclay_els", Row.Close);   // Cüce 6
            if (zoltan) Add(b, "zoltan");
            return Val(Play(Start(a, b), P(Potion.NorthernWind), N), t);
        }
        Assert.Equal(1.8m, Hit(false, false));       // 6 × 0,3 = 1,8
        Assert.Equal(3.9m, Hit(true, false));        // −%35: 6 × 0,65 = 3,9
        Assert.Equal(6, Hit(false, true));           // Zoltan: bombadan etkilenmez
    }

    [Fact]
    public void Soldier_threshold_and_shield_under_weather()
    {
        var (a, b) = (Side("A", tags: (Tag.Soldier, 2)), Side("B"));
        var s = Add(a, "blue_stripes_commando");     // Asker 4
        var m = Add(a, "fiend");                     // 8
        a.Specials.Add(Special.Frost);
        Add(b, "fiend");
        b.Specials.Add(Special.Shield);
        b.Act.Shields.Add(Row.Close);
        var bt = Start(a, b);
        var o = Play(bt, N, N);
        Assert.Equal(2, Val(o, s));                  // Asker 2 eşiği: havada 2 (Bağ yok: tek Asker)
        Assert.Equal(1, Val(o, m));
        Assert.Equal(8, o.ValueB);                   // Kalkan
    }

    // ================= sınıf eşikleri =================

    [Fact]
    public void Monster_threshold_adds_flat_power()
    {
        foreach (var (n, add) in new[] { (2, 1), (4, 2), (6, 3) })
        {
            var a = Side("A", tags: (Tag.Monster, n));
            var u = Add(a, "fiend");
            var bt = new Battle(a, Side("B"), new[] { Row.Close, Row.Ranged, Row.Siege }, 1);
            Assert.Equal(8 + add, bt.Value(a, u));
        }
    }

    [Fact]
    public void Soldier_weather_levels()
    {
        foreach (var (n, v) in new[] { (0, 1), (2, 2), (4, 4), (6, 6) })   // Asker 4: havadan etkilenmez; 6: ayrıca +2
        {
            var a = Side("A", tags: (Tag.Soldier, n));
            var u = Add(a, "blue_stripes_commando");
            a.Specials.Add(Special.Frost);
            var bt = Start(a, Side("B"));
            Assert.Equal(v, bt.Value(a, u));
        }
    }

    [Fact]
    public void Bandit_six_landing_bonus_fifty()
    {
        var (a, b) = (Side("A", tags: (Tag.Bandit, 6)), Side("B"));
        var u = Hide(a, "elven_skirmisher", 2);
        Add(b, "ghoul", power: 1);
        var bt = Start(a, b);
        var o = Play(bt, WindowChoice.With(Array.Empty<Sign>(), null, new[] { u.Uid }), N);
        Assert.Equal(3, Val(o, u));                  // Haydut 6: pusudan inen +%50: 2 → 3
        Assert.Equal(3, bt.AmbushCap(a));
    }

    [Fact]
    public void Dwarf_three_protects_from_scorch_and_dwarf_five_boosts_row()
    {
        var (a, b) = (Side("A", tags: (Tag.Dwarf, 6)), Side("B"));
        var y = Add(a, "barclay_els", Row.Close, power: 9);
        Add(b, "fiend", power: 9);
        b.Specials.Add(Special.Scorch);
        var bt = Start(a, b);
        Assert.Contains(a.Units, x => x == y);
        Assert.DoesNotContain(b.Units, x => x.Def.Id == "fiend");
        var o = Play(bt, N, N);
        Assert.Equal(12, o.ValueA);                  // Cüce 3: +1 → 10; Cüce 5: × 1,2 = 12
    }

    // ================= yetenekler =================

    [Fact]
    public void Morale_adds_star_to_other_units_of_the_chosen_row()
    {
        var a = Side("A");
        var m = Add(a, "kaedweni_siege_expert", star: 2);
        var f = Add(a, "fiend");
        var t = Add(a, "trebuchet");
        a.Act.Morale[m.Uid] = Row.Close;
        var bt = new Battle(a, Side("B"), new[] { Row.Close, Row.Ranged, Row.Siege }, 1);
        Assert.Equal(10, bt.Value(a, f));
        Assert.Equal(6, bt.Value(a, t));
        Assert.Equal(Tuning.Power(m.Def, 2), bt.Value(a, m));
    }

    [Fact]
    public void Revenge_and_zeal_follow_the_previous_row()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "ghoul", Row.Close, power: 1);
        var rev = Add(a, "sheldon_skaggs");          // Menzilli 7, İntikam
        var zeal = Add(a, "griffin", Row.Siege);     // 5, Coşku
        Add(b, "fiend");                             // A Yakın'ı kaybeder
        var bt = Start(a, b);
        var o1 = Play(bt, N, N);
        Assert.Equal(1, o1.Winner);
        var o2 = Play(bt, N, N);
        Assert.Equal(10.5m, Val(o2, rev));           // 7 × 1,5 = 10,5
        var o3 = Play(bt, N, N);
        Assert.Equal(6.5m, Val(o3, zeal));           // önceki kazanıldı: 5 × 1,3 = 6,5
    }

    [Fact]
    public void Revenge_does_nothing_in_the_first_row()
    {
        var (a, b) = (Side("A"), Side("B"));
        var rev = Add(a, "mahakaman_defender");      // Yakın 4, İntikam
        Add(b, "fiend");
        var o = Play(Start(a, b), N, N);
        Assert.Equal(4, Val(o, rev));
    }

    [Fact]
    public void Medic_protects_strongest_from_weather_igni_and_scorch_and_three_star_revives()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "dun_banner_medic", star: 3);
        var big = Add(a, "fiend", power: 9);
        var second = Add(a, "griffin", power: 6);    // 3★: iki koruma (fiend, medic? en güçlüler) + diriliş
        a.Specials.Add(Special.Frost);
        Add(b, "draug", Row.Ranged, power: 7);
        b.Specials.Add(Special.Scorch);
        var bt = Start(a, b);
        Assert.True(big.Protected);
        var o = Play(bt, N, C(Sign.Igni));
        Assert.Equal(9, Val(o, big));                // hava ve Igni işlemedi
    }

    [Fact]
    public void Three_star_medic_revives_its_unprotected_target_at_half_power()
    {
        var a = Side("A");
        var med = Add(a, "dun_banner_medic", star: 3, power: 12);
        var big = Add(a, "fiend", power: 9);
        var rv = Add(a, "griffin", power: 6);
        a.Specials.Add(Special.Scorch);
        Start(a, Side("B"));
        Assert.True(med.Protected && big.Protected && rv.Revive);
        Assert.DoesNotContain(rv, a.Units);
        Assert.Contains(a.Units, u => u.Token && u.Def.Id == "griffin" && u.Power == 3);
    }

    [Fact]
    public void Summon_pulls_copies_from_bench()
    {
        var a = Side("A");
        Add(a, "nekker", star: 3);
        a.Bench.Add((CardSet.Get("nekker"), 1));
        a.Bench.Add((CardSet.Get("nekker"), 1));
        a.Bench.Add((CardSet.Get("ghoul"), 1));
        Start(a, Side("B"));
        Assert.Equal(3, a.Units.Count(u => u.Def.Id == "nekker"));
        Assert.Single(a.Bench);
    }

    [Fact]
    public void Death_echo_leaves_a_ghost_after_scorch()
    {
        foreach (var (star, ghost) in new[] { (1, 2), (2, 3), (3, 5) })
        {
            var a = Side("A");
            Add(a, "grave_hag", star: star);
            a.Specials.Add(Special.Scorch);
            Start(a, Side("B"));
            Assert.Single(a.Units);
            Assert.True(a.Units[0].Token);
            Assert.Equal(ghost, a.Units[0].Power);
        }
    }

    [Fact]
    public void Scorch_ignores_ambush_and_shield_rows()
    {
        var (a, b) = (Side("A"), Side("B"));
        var hid = Hide(a, "dol_blathanna_scout", 20);
        Add(a, "griffin");
        var safe = Add(b, "draug");
        b.Specials.Add(Special.Shield);
        b.Act.Shields.Add(Row.Close);
        a.Specials.Add(Special.Scorch);
        Start(a, b);
        Assert.Contains(hid, a.Ambush);
        Assert.Contains(safe, b.Units);
        Assert.DoesNotContain(a.Units, u => u.Def.Id == "griffin");
    }

    // ================= kahramanlar =================

    [Fact]
    public void Ciri_landing_is_immune_to_enemy_signs_that_window()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend");                             // 8
        var ciri = Hide(a, "ciri");
        Add(b, "draug");
        var bt = Start(a, b);
        var o = Play(bt, WindowChoice.With(Array.Empty<Sign>(), null, new[] { ciri.Uid }), C(Sign.Aard));
        Assert.Equal(7.2m + 20, o.ValueA);           // Aard yalnız fiend'e: 8 × 0,9 = 7,2; Ciri 20 muaf
    }

    [Fact]
    public void Yennefer_win_gives_next_row_twenty_percent()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "yennefer", Row.Close);
        Add(a, "fiend", Row.Ranged, power: 10);
        Add(b, "ghoul", power: 1);
        Add(b, "tibor_eggebracht", power: 1);
        var bt = Start(a, b);
        Play(bt, N, N);
        var o = Play(bt, N, N);
        Assert.Equal(12, o.ValueA);
    }

    [Fact]
    public void Vesemir_rage_two_and_cap_three()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "vesemir", Row.Close, power: 1);
        Add(a, "vesemir", Row.Ranged, power: 1);
        Add(a, "vesemir", Row.Siege, power: 1);
        Add(b, "fiend"); Add(b, "dethmold"); Add(b, "trebuchet");
        var bt = Start(a, b);
        Play(bt, N, N); Assert.Equal(4, a.Focus);
        Play(bt, N, N); Assert.Equal(5, a.Focus);   // tavan 3
        Play(bt, N, N); Assert.Equal(5, a.Focus);
    }

    // ===== kahraman güç basamakları: kahramanın sınıf eşiği 2 → II, 3 → III =====

    [Fact]
    public void Hero_tier_follows_own_class_threshold()
    {
        var s = Side("A", 5, null, (Tag.Mage, 4), (Tag.Soldier, 2), (Tag.Noble, 6));
        Add(s, "triss", Row.Siege); Add(s, "vesemir"); Add(s, "jaskier");
        Assert.Equal(1, s.HeroTier(Hero.Triss));    // Büyücü 4 kart = 2. eşik
        Assert.Equal(0, s.HeroTier(Hero.Vesemir));  // Asker 2 kart = yalnız 1. eşik
        Assert.Equal(2, s.HeroTier(Hero.Jaskier));  // Soylu 6 kart = 3. eşik
        Assert.Equal(-1, s.HeroTier(Hero.Geralt));
    }

    [Fact]
    public void Triss_tier3_makes_every_quen_free_tier2_two()
    {
        foreach (var (mages, free) in new[] { (2, 1), (4, 2), (6, 99) })
        {
            var (a, b) = (Side("A", 5, null, (Tag.Mage, mages)), Side("B"));
            Add(a, "triss", Row.Siege); Add(a, "fiend"); Add(a, "dethmold");
            Add(b, "ghoul", power: 1); Add(b, "sweers");
            var bt = Start(a, b);
            for (var i = 0; i < Math.Min(free, 3); i++)
            {
                Assert.Equal(0, bt.Cost(0, C(Sign.Quen)));
                Play(bt, C(Sign.Quen), N);
            }
            if (free < 3) Assert.Equal(1, bt.Cost(0, C(Sign.Quen)));
        }
    }

    [Fact]
    public void Jaskier_tier2_doubles_twice()
    {
        var (a, b) = (Side("A", 5, School.Wolf, (Tag.Noble, 4)), Side("B"));
        Add(a, "jaskier"); Add(a, "sabrina_glevissig", Row.Siege);
        Add(b, "fiend"); Add(b, "griffin");
        var bt = Start(a, b);
        Play(bt, C(Sign.Aard, Sign.Aard), N);
        Assert.True(bt.CanDouble(0));
        Play(bt, C(Sign.Aard, Sign.Aard), N);
        Assert.False(bt.CanDouble(0));
    }

    [Fact]
    public void Vesemir_tier3_rage_four()
    {
        var (a, b) = (Side("A", 5, null, (Tag.Soldier, 6)), Side("B"));
        Add(a, "vesemir", Row.Close, power: 1);
        Add(b, "fiend"); Add(b, "dethmold"); Add(b, "trebuchet");
        var bt = Start(a, b);
        Play(bt, N, N);
        Assert.Equal(2 + 4, a.Focus);
    }

    [Fact]
    public void Eredin_tier_scales_percentage()
    {
        foreach (var (n, pct) in new[] { (1, 0.30), (4, 0.40), (6, 0.50) })
        {
            var (a, b) = (Side("A", 5, null, (Tag.Monster, n)), Side("B"));
            Add(a, "eredin", Row.Close);
            Add(b, "fiend", Row.Close);
            var bt = Start(a, b);
            Assert.Equal(pct, b.EredinPct, 3);
        }
    }

    [Fact]
    public void Normal_rage_caps_at_two()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "ghoul", Row.Close, power: 1); Add(a, "ghoul", Row.Ranged, power: 1); Add(a, "ghoul", Row.Siege, power: 1);
        Add(b, "fiend"); Add(b, "dethmold"); Add(b, "trebuchet");
        var bt = Start(a, b);
        for (var i = 0; i < 3; i++) Play(bt, N, N);
        Assert.Equal(4, a.Focus);
    }

    // ===== Faz 1: Denizci, Şövalye, Yağma, Dönüşüm/Mardroeme, Yakıcı, Meydan Okuma =====

    [Fact]
    public void Mardroeme_triples_berserkers_only_with_the_special()
    {
        var (a, b) = (Side("A"), Side("B"));
        var be = Add(a, "berserker");
        var yb = Add(a, "young_berserker");
        Add(b, "ghoul", power: 1);
        a.Specials.Add(Special.Mardroeme);
        Start(a, b);
        Assert.Equal(12, be.Power);
        Assert.Equal(6, yb.Power);
        var (c, d) = (Side("C"), Side("D"));
        var be2 = Add(c, "berserker");
        Add(d, "ghoul", power: 1);
        Start(c, d);
        Assert.Equal(4, be2.Power);
    }

    [Fact]
    public void Row_scorch_burns_strongest_when_enemy_row_reaches_ten()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "clan_dimun_pirate");                        // Menzilli
        var big = Add(b, "dethmold", power: 7);
        var small = Add(b, "sabrina_glevissig", power: 3);   // toplam 10
        var g = Add(b, "yennefer");                          // kahraman: yanmaz (ve toplamı da büyütür)
        Start(a, b);
        Assert.DoesNotContain(big, b.Units);
        Assert.Contains(small, b.Units);
        Assert.Contains(g, b.Units);

        var (c, d) = (Side("C"), Side("D"));
        Add(c, "clan_dimun_pirate");
        var only = Add(d, "dethmold", power: 9);              // 9 < 10
        Start(c, d);
        Assert.Contains(only, d.Units);
    }

    [Fact]
    public void Plunder_pays_gold_for_won_rows_capped()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "clan_brokvar_archer", Row.Close, power: 20);
        Add(a, "war_longship", Row.Ranged, power: 20);
        Add(a, "war_longship", Row.Siege, power: 20);
        Add(b, "ghoul", power: 1);
        var bt = Start(a, b);
        for (var i = 0; i < 3; i++) Play(bt, N, N);
        Assert.Equal(Tuning.PlunderCap, a.Plunder);          // üç satır kazanıldı ama üst sınır 2

        var (c, d) = (Side("C", 5, null, (Tag.Sailor, 4)), Side("D"));
        Add(c, "clan_brokvar_archer", Row.Close, power: 20);
        Add(d, "ghoul", power: 1);
        var bt2 = Start(c, d);
        Play(bt2, N, N);
        Assert.Equal(2, c.Plunder);                          // Denizci 2. eşik: satır başına 2
    }

    [Fact]
    public void Challenge_cuts_a_stronger_enemy_in_its_row()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "morteisen");                                 // 4, Yakın
        var f = Add(b, "fiend");                             // 8
        var bt = Start(a, b);
        var o = Play(bt, N, N);
        Assert.Equal(6, Val(o, f));                          // 8 × 0,75

        var (c, d) = (Side("C"), Side("D"));
        Add(c, "siegfried_of_denesle");                      // 8
        var w = Add(d, "griffin");                           // 5: daha zayıf, dokunulmaz
        var o2 = Play(Start(c, d), N, N);
        Assert.Equal(5, Val(o2, w));
    }

    [Fact]
    public void Sailors_gain_in_first_window_only()
    {
        var (a, b) = (Side("A", 5, null, (Tag.Sailor, 2)), Side("B"));
        var s1 = Add(a, "clan_drummond_shield_maiden", Row.Close, power: 10);
        var s2 = Add(a, "clan_an_craite_warrior", Row.Ranged, power: 10);
        Add(b, "ghoul", power: 1);
        var bt = Start(a, b);
        var o1 = Play(bt, N, N);
        Assert.Equal(13, Val(o1, s1));                       // +%30
        var o2 = Play(bt, N, N);
        Assert.Equal(10, Val(o2, s2));
    }

    [Fact]
    public void Knights_halve_igni_and_ignore_it_at_third_threshold()
    {
        var (a, b) = (Side("A"), Side("B", 5, null, (Tag.Knight, 2)));
        var k = Add(b, "rainfarn", power: 10);
        var bt = Start(a, b);
        var o = Play(bt, C(Sign.Igni), N);
        Assert.Equal(7.5m, Val(o, k));                       // −%50 × 0,5

        var (c, d) = (Side("C"), Side("D", 5, null, (Tag.Knight, 6)));
        var k2 = Add(d, "rainfarn", power: 10);
        var other = Add(d, "fiend", power: 6);
        var bt2 = Start(c, d);
        var o2 = Play(bt2, C(Sign.Igni), N);
        Assert.Equal(3, Val(o2, other));                     // Igni şövalyeyi atlar, öteki birime gider
        Assert.True(Val(o2, k2) >= 12);                      // 10 + 2 (2. eşik), dokunulmadı
    }

    // ===== Faz 2: Vampir, Kan Emme, Kocakarılar =====

    [Fact]
    public void Drain_takes_half_of_weakest_enemy()
    {
        var (a, b) = (Side("A"), Side("B"));
        var v = Add(a, "vampire_bruxa");                     // 5
        var weak = Add(b, "dwarven_skirmisher", power: 4);
        var strong = Add(b, "fiend");                        // 8
        var o = Play(Start(a, b), N, N);
        Assert.Equal(2, Val(o, weak));                       // 4 → 2
        Assert.Equal(8, Val(o, strong));
        Assert.Equal(7, Val(o, v));                          // 5 + 2
    }

    [Fact]
    public void Vampire_thresholds_raise_drain_and_take_two_targets()
    {
        var (a, b) = (Side("A", 5, null, (Tag.Vampire, 5)), Side("B"));
        var v = Add(a, "vampire_katakan", power: 10);
        var w1 = Add(b, "dwarven_skirmisher", power: 4);
        var w2 = Add(b, "griffin", power: 6);
        var o = Play(Start(a, b), N, N);
        Assert.Equal(1, Val(o, w1));                         // −%75
        Assert.Equal(1.5m, Val(o, w2));
        Assert.Equal(17.5m, Val(o, v));                      // 10 + 3 + 4,5
    }

    [Fact]
    public void Burned_vampire_returns_at_half_with_second_threshold()
    {
        var (a, b) = (Side("A", 5, null, (Tag.Vampire, 3)), Side("B"));
        Add(a, "vampire_katakan", power: 10);
        b.Specials.Add(Special.Scorch);
        Add(b, "ghoul", power: 1);
        Start(a, b);
        var back = Assert.Single(a.Units);
        Assert.True(back.Token);
        Assert.Equal(5, back.Power);
    }

    [Fact]
    public void Crones_trio_only_when_all_three_present()
    {
        var (a, b) = (Side("A"), Side("B"));
        var w = Add(a, "crone_weavess");
        Add(a, "crone_brewess");
        Add(b, "ghoul", power: 1);
        var o = Play(Start(a, b), N, N);
        Assert.Equal(7, Val(o, w));
        var (c, d) = (Side("C"), Side("D"));
        var w2 = Add(c, "crone_weavess");
        Add(c, "crone_brewess");
        Add(c, "crone_whispess");
        Add(d, "ghoul", power: 1);
        var o2 = Play(Start(c, d), N, N);
        Assert.Equal(11, Val(o2, w2));                       // 7 × 1,5 = 10,5 → 11
    }

    [Fact]
    public void Subpool_keeps_only_active_classes_in_the_shop()
    {
        var m = new Match(7, Enumerable.Range(0, 8).Select(i => ($"P{i}", false)).ToList());
        Assert.Equal(Math.Min(Tuning.ActiveClasses, Enum.GetValues<Tag>().Length), m.ActiveTags.Count);
        foreach (var d in CardSet.All.Where(d => d.IsUnit && !d.IsHero && d.Tags.All(t => !m.ActiveTags.Contains(t))))
            Assert.Equal(0, m.Pool[d.Id]);
        foreach (var d in CardSet.All.Where(d => d.IsHero && m.Pool.GetValueOrDefault(d.Id) > 0))
            Assert.Contains(d.Tags[0], m.ActiveTags);
        Assert.True(m.Pool["mardroeme"] > 0);                // özel kartlar hep var
    }

    [Fact]
    public void Eredin_weakens_one_enemy_row_non_heroes()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "eredin");
        foreach (var r in Enum.GetValues<Row>()) Add(b, "fiend", r, power: 10);
        var g = Add(b, "geralt", Row.Close);
        var bt = Start(a, b);
        Assert.NotNull(b.EredinRow);
        for (var i = 0; i < 3; i++)
        {
            var o = Play(bt, N, N);
            var f = b.In(o.Row).First(u => u.Def.Id == "fiend");
            Assert.Equal(o.Row == b.EredinRow ? 7 : 10, Val(o, f));
            if (o.Row == Row.Close) Assert.Equal(22, Val(o, g));
        }
    }

    // ================= pusu =================

    [Fact]
    public void Several_ambush_units_can_land_in_the_same_window()
    {
        var (a, b) = (Side("A"), Side("B"));
        var x = Hide(a, "elven_skirmisher", 2);
        var y = Hide(a, "dol_blathanna_scout", 7);
        Hide(a, "vrihedd_brigade_recruit", 4);
        Add(b, "fiend");
        var bt = Start(a, b);
        var choice = WindowChoice.With(Array.Empty<Sign>(), null, new[] { x.Uid, y.Uid });
        Assert.True(bt.Valid(0, choice));
        Assert.False(bt.Valid(0, WindowChoice.With(Array.Empty<Sign>(), null, new[] { x.Uid, x.Uid })));
        var o = Play(bt, choice, N);
        Assert.Equal(9, o.ValueA);
        Assert.Single(a.Ambush);
        // ağ biçimi: eylem → seçim → aynı birimler
        var back = HanAction.Choice(0, 0, 0, choice).ToChoice()!;
        Assert.Equal(choice.AmbushUids, back.AmbushUids);
    }

    // ================= hasar =================

    [Fact]
    public void Damage_bear_reduction_and_pve_halving()
    {
        int Dmg(School? loserSchool, bool pve)
        {
            var (a, b) = (Side("A", level: 6), Side("B", school: loserSchool));
            Add(a, "fiend"); Add(a, "dethmold"); Add(a, "trebuchet");
            var bt = Start(a, b, pve: pve);
            for (var i = 0; i < 3; i++) Play(bt, N, N);
            return bt.Damage;
        }
        Assert.Equal(21, Dmg(null, false));          // ⌈2,25 × (6 + 3)⌉ = 21
        Assert.Equal(19, Dmg(School.Bear, false));   // ⌈21 × 0,9⌉ = 19
        Assert.Equal(11, Dmg(null, true));           // ⌈21 / 2⌉
    }

    [Fact]
    public void Focus_never_goes_negative_and_unaffordable_signs_are_rejected()
    {
        var (a, b) = (Side("A"), Side("B"));
        Add(a, "fiend"); Add(b, "fiend");
        var bt = Start(a, b);
        Assert.False(bt.Valid(0, new WindowChoice(new[] { Sign.Igni, Sign.Igni })));
        Play(bt, C(Sign.Igni), N);
        Assert.True(a.Focus >= 0);
        if (a.Focus < 2) Assert.False(bt.Valid(0, C(Sign.Igni)));
    }
}
