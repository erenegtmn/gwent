using HanLigi;

namespace HanLigi.Tests;

/// <summary>Pusu masa sınırı dışında, tezgâh kilidi, özel kart satırı, savaş anında can kaybı.</summary>
public class RulesV2Tests
{
    private static Match Ready(int seed, int players = 8)
    {
        var m = new Match(seed, Enumerable.Range(0, players).Select(i => ($"O{i}", true)).ToList());
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[0]);
        return m;
    }

    private static OwnedCard Give(Match m, PlayerState p, string id)
    {
        var d = CardSet.Get(id);
        m.Pool[d.Id]--;
        var c = new OwnedCard { Uid = 50000 + p.Cards.Count, Def = d };
        p.Cards.Add(c);
        return c;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pairing_rotates_opponents(int seed)
    {
        var m = Ready(seed);
        var rng = new Random(seed);
        var pvp = 0;
        var met = new HashSet<(int, int)>();
        var ghosts = 0;
        while (m.Phase != Phase.Finished && m.Round <= 40)
        {
            foreach (var p in m.Alive.ToList()) HanBot.Prep(m, p.Id, rng);
            if (m.Phase == Phase.Prep) m.ForceLock();
            if (!m.IsPve && m.Alive.Count() == 8 && pvp < 7)
            {
                // 8 kişi hayattayken 7 PvP round'unda hiçbir çift ikinci kez eşleşmez (herkes herkesi bir kez görür)
                foreach (var p in m.Alive.Where(p => p.OppKind == OpponentKind.Player && p.Id < p.OppId))
                    Assert.True(met.Add((p.Id, p.OppId)), $"round {m.Round}: {p.Id}-{p.OppId} tekrar");
                pvp++;
            }
            // tek sayıda kalan, en son elenen oyuncunun hayaletiyle savaşır (yaşayan birinin kopyası değil)
            foreach (var g in m.Battles.Where(b => b.Ghost))
            {
                var last = m.Players.Where(p => !p.Alive).OrderBy(p => p.Place).First();
                Assert.Equal(last.Name + " (hayalet)", g.Sides[1].Name);
                ghosts++;
            }
            foreach (var b in m.Battles) HanBot.PlayOut(b, rng);
            m.EndRound();
        }
        Assert.True(pvp >= 2);
        Assert.True(ghosts > 0);
    }

    [Fact]
    public void Ambush_does_not_take_a_board_slot()
    {
        var m = Ready(3);
        var p = m.Players[0];
        Assert.Equal(4, p.Slots);
        foreach (var _ in Enumerable.Range(0, 3)) Assert.True(m.Move(p.Id, Give(m, p, "ghoul").Uid, Zone.Close));
        Assert.True(m.Move(p.Id, Give(m, p, "havekar_smuggler").Uid, Zone.Close));   // Haydut: pusudakiyle 2 Haydut, pusu yeri 2
        Assert.Equal(4, p.Used);
        var sk = Give(m, p, "elven_skirmisher");
        Assert.True(m.WouldOverflow(p.Id, sk.Uid, Zone.Ranged)); // masa dolu (konabilir ama sınırı aşar)
        Assert.True(m.Move(p.Id, sk.Uid, Zone.Ambush));         // pusu yer yemez
        var sk2 = Give(m, p, "vrihedd_brigade_recruit");
        Assert.True(m.Move(p.Id, sk2.Uid, Zone.Ambush));
        Assert.Equal(4, p.Used);
        Assert.Equal(6, p.Fielded.Count());
        // pusudan satıra çıkmak masada yer ister
        Assert.True(m.WouldOverflow(p.Id, sk.Uid, Zone.Ranged));
        Assert.True(m.Move(p.Id, p.Cards.First(c => c.Zone == Zone.Close).Uid, Zone.Bench));
        Assert.False(m.WouldOverflow(p.Id, sk.Uid, Zone.Ranged));
        Assert.True(m.Move(p.Id, sk.Uid, Zone.Ranged));
        // pusudakiler sınıf sayımına girer, savaşta pusu olarak gelir
        Assert.Contains(p.Cards, c => c.Zone == Zone.Ambush);
    }

    [Fact]
    public void Locked_shop_survives_to_next_round_without_the_bought_cards()
    {
        var m = Ready(9, 2);
        var p = m.Players[0];
        p.Gold = 50;
        Assert.True(m.Buy(p.Id, 1));
        var expect = p.Shop.Select(d => d?.Id).ToArray();
        Assert.Null(expect[1]);
        Assert.True(m.ToggleShopLock(p.Id));
        Assert.True(p.ShopLocked);
        var other = m.Players[1].Shop.Select(d => d?.Id).ToArray();
        foreach (var x in m.Players) m.SetReady(x.Id);
        foreach (var b in m.Battles) { b.Start(); while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); } }
        Assert.True(m.EndRound());
        Assert.Equal(2, m.Round);
        Assert.Equal(expect, p.Shop.Select(d => d?.Id).ToArray());   // aynı kartlar, aynı yerlerde; alınanın yeri boş
        Assert.False(p.ShopLocked);                                     // kilit bir round sürer
        Assert.NotEqual(other, m.Players[1].Shop.Select(d => d?.Id).ToArray());
        // kart korunumu
        foreach (var d in CardSet.All) Assert.True(m.TotalCopies(d.Id) >= 0);
    }

    [Fact]
    public void Shop_can_be_locked_during_the_battle()
    {
        var m = Ready(11, 2);
        var p = m.Players[0];
        var expect = p.Shop.Select(d => d?.Id).ToArray();
        foreach (var x in m.Players) m.SetReady(x.Id);
        Assert.Equal(Phase.Battle, m.Phase);
        Assert.False(m.Reroll(p.Id));                                   // savaşta yalnız kilit: alım/yenileme yok
        Assert.False(m.Buy(p.Id, 0));
        Assert.True(m.ToggleShopLock(p.Id));
        Assert.True(m.ToggleShopLock(p.Id));                            // aç / kapa
        Assert.True(m.ToggleShopLock(p.Id));
        foreach (var b in m.Battles) { b.Start(); while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); } }
        Assert.True(m.ToggleShopLock(p.Id) && m.ToggleShopLock(p.Id)); // savaş bittikten sonra da (round kapanana kadar)
        Assert.True(m.EndRound());
        Assert.Equal(expect, p.Shop.Select(d => d?.Id).ToArray());
        // elenmiş oyuncu ve bitmiş maç kilitleyemez
        p.Alive = false;
        Assert.False(m.ToggleShopLock(p.Id));
    }

    [Fact]
    public void Horn_row_chosen_in_prep_is_the_battle_default()
    {
        var m = Ready(4, 2);
        var p = m.Players[0];
        var g = Give(m, p, "ghoul");
        Assert.True(m.Move(p.Id, g.Uid, Zone.Close));
        var horn = Give(m, p, "horn");
        Assert.False(m.CanMove(p.Id, g.Uid, Zone.Close, Row.Siege));     // birime satır hedefi verilmez
        Assert.True(m.Move(p.Id, horn.Uid, Zone.Special, Row.Siege));
        Assert.Equal(Row.Siege, horn.Aim);
        Assert.Equal(new[] { Row.Siege }, m.Preview(p.Id).Sides[0].Act.Horns);
        Assert.True(m.Move(p.Id, horn.Uid, Zone.Special, Row.Ranged));  // yerinde satır değiştirilebilir
        Assert.False(m.Move(p.Id, horn.Uid, Zone.Special, Row.Ranged));
        foreach (var x in m.Players) m.SetReady(x.Id);
        var b = m.BattleOf(p.Id)!;
        Assert.Equal(new[] { Row.Ranged }, b.DefaultActivation(m.SideOf(b, p.Id)).Horns);
        Assert.True(m.Move(p.Id, horn.Uid, Zone.Special) == false);    // kilitten sonra hareket yok
    }

    [Fact]
    public void Damage_and_elimination_happen_when_each_battle_ends()
    {
        var m = Ready(21, 4);
        // PvE roundlarını geç
        var rng = new Random(1);
        while (m.Phase != Phase.Finished && m.IsPve)
        {
            foreach (var x in m.Alive.ToList()) m.SetReady(x.Id);
            foreach (var b in m.Battles) { b.Start(); while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); } }
            m.EndRound();
        }
        foreach (var x in m.Players) x.Hp = 1;
        // her ikilide bir taraf güçlü: beraberlik olmasın
        foreach (var x in m.Alive.Where(x => x.OppKind == OpponentKind.Player && x.OppId > x.Id).ToList())
            foreach (var z in new[] { Zone.Close, Zone.Close, Zone.Ranged }) { m.Move(x.Id, Give(m, x, "ghoul").Uid, z); }
        foreach (var x in m.Alive.ToList()) m.SetReady(x.Id);
        var battles = m.Battles.Where(b => !b.Pve).ToList();
        Assert.Equal(2, battles.Count);
        // ikinci savaş önce biter: kaybedeni hemen elenir ve 4. olur
        foreach (var b in new[] { battles[1], battles[0] })
        {
            b.Start();
            var hp = b.Sides.Select(s => m.Players[s.PlayerId].Hp).ToArray();
            while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); }
            Assert.True(m.Settle(b));
            Assert.False(m.Settle(b));
            if (b.Winner < 0) continue;
            var loser = m.Players[b.Sides[1 - b.Winner].PlayerId];
            Assert.False(loser.Alive);
            Assert.Equal(b == battles[1] ? 4 : 3, loser.Place);
        }
        Assert.Contains(m.Players, x => !x.Alive);
        Assert.True(m.EndRound());
    }

    [Fact]
    public void Class_breakpoints_fit_the_number_of_cards_in_each_class()
    {
        foreach (var t in Enum.GetValues<Tag>())
        {
            var cards = CardSet.All.Count(d => d.IsUnit && !d.IsHero && d.Has(t));
            Assert.True(Tuning.Breakpoints(t)[2] <= cards, $"{t}: son eşik {Tuning.Breakpoints(t)[2]}, kart {cards}");
        }
        Assert.Equal(new[] { 0, 1, 1, 2, 3 }, new[] { 1, 2, 3, 4, 6 }.Select(n => Tuning.TagLevel(Tag.Bandit, n)).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 2, 3 }, new[] { 1, 2, 3, 4, 5 }.Select(n => Tuning.TagLevel(Tag.Vampire, n)).ToArray());
        Assert.Equal(new[] { 1, 1, 2, 2, 3 }, new[] { 2, 3, 4, 5, 6 }.Select(n => Tuning.TagLevel(Tag.Monster, n)).ToArray());
    }

    [Fact]
    public void Manticore_gets_potions_from_round_two()
    {
        for (var seed = 1; seed < 40; seed++)
        {
            var m = new Match(seed, Enumerable.Range(0, 2).Select(i => ($"O{i}", true)).ToList());
            if (m.Players[0].SchoolOptions is not { } o || !o.Contains(School.Manticore)) continue;
            m.ChooseSchool(0, School.Manticore);
            m.ChooseSchool(1, m.Players[1].SchoolOptions.First(x => x != School.Manticore));
            foreach (var x in m.Players) m.SetReady(x.Id);
            foreach (var b in m.Battles) { b.Start(); while (!b.Done) { b.Submit(0, WindowChoice.None); b.Submit(1, WindowChoice.None); b.Resolve(); } }
            m.EndRound();
            Assert.Equal(2, m.Round);
            Assert.Equal(3, m.Players[0].PotionOptions.Length);
            Assert.Empty(m.Players[1].PotionOptions);
            return;
        }
        Assert.Fail("Mantikor seçeneği olan tohum bulunamadı");
    }

    [Fact]
    public void Damage_is_light_early_and_heavy_late()
    {
        Assert.Equal(new[] { 0.6, 0.6, 1.0, 1.0, 1.3, 1.3 }, new[] { 1, 8, 9, 14, 15, 30 }.Select(Tuning.DamageRamp).ToArray());
        Assert.Equal(1.0, Tuning.DamageRamp(0));
    }

    [Fact]
    public void Row_breakdown_shown_to_players_always_matches_the_row_score()
    {
        var checkedRows = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var m = new Match(seed, Enumerable.Range(0, 8).Select(i => ($"B{i}", true)).ToList());
            var rng = new Random(seed);
            foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(3)]);
            for (var guard = 0; guard < 60 && m.Phase != Phase.Finished; guard++)
            {
                foreach (var p in m.Alive.ToList()) ExpertBot.Prep(m, p.Id, rng);
                if (m.Phase == Phase.Prep) m.ForceLock();
                foreach (var b in m.Battles)
                {
                    b.Start();
                    while (!b.Done)
                    {
                        for (var s = 0; s < 2; s++) b.Submit(s, HanBot.Window(b, s, rng));
                        var o = b.Resolve()!;
                        for (var side = 0; side < 2; side++)
                        {
                            var val = side == 0 ? o.ValueA : o.ValueB;
                            var ciri = b.Sides[side].In(o.Row).Any(u => u.Landed && u.Def.Hero == Hero.Ciri);
                            if (ciri || (b.Pve && b.NoDamage && side == 1)) continue;   // muaf kart ve öğretici tabanı ayrı kural
                            Assert.Equal(val, Tuning.ApplyPct(o.RowBase[side], o.RowMods.Where(x => x.Side == side).Sum(x => x.Pct)));
                            checkedRows++;
                        }
                    }
                    m.Settle(b);
                }
                m.EndRound();
            }
        }
        Assert.True(checkedRows > 1000);
    }
}
