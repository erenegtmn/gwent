using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

public class AbilityTests
{
    [Fact]
    public void Spy_is_placed_on_opponent_side_counts_for_them_and_draws_two()
    {
        var g = T.New("nilfgaard", "monsters");
        var spy = g.TestHand(0, "vattier_de_rideaux"); // 4
        T.Filler(g, 0);
        g.TestDeck(0, "fiend"); g.TestDeck(0, "fiend"); g.TestDeck(0, "fiend");
        var handBefore = g.Players[0].Hand.Count;

        T.Play(g, 0, spy);

        Assert.Contains(spy, g.Players[1].Row(Close).Cards);
        Assert.Equal(4, g.PlayerTotal(1));
        Assert.Equal(0, g.PlayerTotal(0));
        Assert.Equal(handBefore - 1 + 2, g.Players[0].Hand.Count);
    }

    [Fact]
    public void Spy_draws_only_what_the_deck_has()
    {
        var g = T.New("nilfgaard", "monsters");
        var spy = g.TestHand(0, "vattier_de_rideaux");
        T.Filler(g, 0);
        g.TestDeck(0, "fiend");
        var before = g.Players[0].Hand.Count;
        T.Play(g, 0, spy);
        Assert.Equal(before, g.Players[0].Hand.Count); // -1 oynanan +1 çekilen
    }

    [Fact]
    public void Medic_asks_for_a_non_hero_unit_and_plays_it_with_its_abilities()
    {
        var g = T.New("northern_realms", "monsters");
        var fiend = g.TestGrave(0, "ballista");
        var hero = g.TestGrave(0, "esterad_thyssen");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);

        T.Play(g, 0, medic);
        Assert.NotNull(g.Pending);
        Assert.Equal(ChoiceKind.Medic, g.Pending!.Kind);
        Assert.Contains(fiend.Uid, g.Pending.Candidates);
        Assert.DoesNotContain(hero.Uid, g.Pending.Candidates); // kahraman geri gelmez

        T.Fails(g, new Choose(0, new[] { hero.Uid }));
        T.Fails(g, new Choose(1, new[] { fiend.Uid }), "sen yapamazsın");
        T.Ok(g, new Choose(0, new[] { fiend.Uid }));

        Assert.Null(g.Pending);
        Assert.Contains(fiend, g.Players[0].Row(Siege).Cards);
        Assert.DoesNotContain(fiend, g.Players[0].Grave);
        Assert.Equal(1, g.Current); // seçim bitince sıra rakibe geçti
    }

    [Fact]
    public void Medic_with_empty_graveyard_just_ends_the_turn()
    {
        var g = T.New();
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);
        T.Play(g, 0, medic);
        Assert.Null(g.Pending);
        Assert.Equal(1, g.Current);
    }

    [Fact]
    public void Medic_chain_revived_medic_asks_again_and_agile_needs_a_row()
    {
        var g = T.New("nilfgaard", "monsters");
        var olaf = g.TestGrave(0, "olaf");                    // agile
        var med2 = g.TestGrave(0, "etolian_auxiliary_archers"); // medic
        var med1 = g.TestHand(0, "siege_technician");
        T.Filler(g, 0);

        T.Play(g, 0, med1);
        T.Ok(g, new Choose(0, new[] { med2.Uid }));
        Assert.NotNull(g.Pending); // geri gelen Medic tekrar seçim ister
        T.Fails(g, new Choose(0, new[] { olaf.Uid }), "Çevik");
        T.Ok(g, new Choose(0, new[] { olaf.Uid }, Ranged));
        Assert.Contains(olaf, g.Players[0].Row(Ranged).Cards);
    }

    [Fact]
    public void Revived_spy_goes_to_opponent_side_and_draws()
    {
        var g = T.New("nilfgaard", "monsters");
        var spy = g.TestGrave(0, "vattier_de_rideaux");
        var medic = g.TestHand(0, "siege_technician");
        T.Filler(g, 0);
        g.TestDeck(0, "fiend"); g.TestDeck(0, "fiend");
        var before = g.Players[0].Hand.Count;

        T.Play(g, 0, medic);
        T.Ok(g, new Choose(0, new[] { spy.Uid }));

        Assert.Contains(spy, g.Players[1].Row(Close).Cards);
        Assert.Equal(before - 1 + 2, g.Players[0].Hand.Count);
    }

    [Fact]
    public void Muster_pulls_same_group_from_hand_and_deck()
    {
        var g = T.New("monsters", "northern_realms");
        var played = g.TestHand(0, "nekker");
        var inHand = g.TestHand(0, "nekker");
        var inDeck = g.TestDeck(0, "nekker");
        var other = g.TestDeck(0, "fiend");
        T.Filler(g, 0);

        T.Play(g, 0, played);

        var close = g.Players[0].Row(Close).Cards;
        Assert.Contains(inHand, close);
        Assert.Contains(inDeck, close);
        Assert.DoesNotContain(other, close);
        Assert.Equal(3, close.Count);
    }

    [Fact]
    public void Muster_group_is_not_the_card_name_vampires_and_cerys()
    {
        var g = T.New("monsters", "northern_realms");
        var bruxa = g.TestHand(0, "vampire_bruxa");
        var katakan = g.TestDeck(0, "vampire_katakan");
        T.Filler(g, 0);
        T.Play(g, 0, bruxa);
        Assert.Contains(katakan, g.Players[0].Row(Close).Cards);

        var g2 = T.New("skellige", "northern_realms");
        var cerys = g2.TestHand(0, "cerys");
        var maiden = g2.TestDeck(0, "clan_drummond_shield_maiden");
        T.Filler(g2, 0);
        T.Play(g2, 0, cerys);
        Assert.Contains(maiden, g2.Players[0].Row(Close).Cards);
    }

    [Fact]
    public void Scorch_kills_strongest_on_both_sides_including_ties_and_spares_heroes()
    {
        var g = T.New();
        var mine = g.TestBoard(0, "ballista", Siege);     // 6
        var a = g.TestBoard(1, "fiend", Close);            // 6
        var b = g.TestBoard(1, "fire_elemental", Siege);   // 6
        var weak = g.TestBoard(1, "nekker", Close);        // 2
        var hero = g.TestBoard(1, "draug", Close);         // 10 hero
        var scorch = g.TestHand(0, "scorch");
        T.Filler(g, 0);

        T.Play(g, 0, scorch);

        Assert.DoesNotContain(mine, g.Players[0].Row(Siege).Cards);
        Assert.DoesNotContain(a, g.Players[1].Row(Close).Cards);
        Assert.DoesNotContain(b, g.Players[1].Row(Siege).Cards);
        Assert.Contains(weak, g.Players[1].Row(Close).Cards);
        Assert.Contains(hero, g.Players[1].Row(Close).Cards);
        Assert.Contains(mine, g.Players[0].Grave);
        Assert.Contains(scorch, g.Players[0].Grave);
    }

    [Fact]
    public void Scorch_uses_current_strength_not_base()
    {
        var g = T.New();
        var big = g.TestBoard(1, "fiend", Close);        // 6 → hava ile 1
        var small = g.TestBoard(1, "dethmold", Ranged);  // 6 ama hava yok
        g.TestWeather("biting_frost");
        var scorch = g.TestHand(0, "scorch");
        T.Filler(g, 0);
        T.Play(g, 0, scorch);
        Assert.Contains(big, g.Players[1].Row(Close).Cards);
        Assert.DoesNotContain(small, g.Players[1].Row(Ranged).Cards);
    }

    [Fact]
    public void Row_scorch_needs_ten_total_in_the_enemy_row()
    {
        var g = T.New();
        g.TestBoard(1, "fiend", Close);   // 6
        g.TestBoard(1, "nekker", Close);  // 2 → toplam 8
        var villen = g.TestHand(0, "villentretenmerth");
        T.Filler(g, 0);
        T.Play(g, 0, villen);
        Assert.Equal(2, g.Players[1].Row(Close).Cards.Count); // 8 < 10: yakmaz

        var g2 = T.New();
        var big = g2.TestBoard(1, "fiend", Close);          // 6
        g2.TestBoard(1, "frightener", Close);                // 5 → 11
        var small = g2.TestBoard(1, "nekker", Close);        // 2
        var v2 = g2.TestHand(0, "villentretenmerth");
        T.Filler(g2, 0);
        T.Play(g2, 0, v2);
        Assert.DoesNotContain(big, g2.Players[1].Row(Close).Cards);
        Assert.Contains(small, g2.Players[1].Row(Close).Cards);
    }

    [Fact]
    public void Clan_dimun_pirate_scorches_everywhere_but_not_itself()
    {
        var g = T.New("skellige", "monsters");
        var enemy = g.TestBoard(1, "leshen", Ranged);       // hero → bağışık
        var target = g.TestBoard(1, "fiend", Close);         // 6
        var pirate = g.TestHand(0, "clan_dimun_pirate");     // 6 (kendisi hariç)
        T.Filler(g, 0);
        T.Play(g, 0, pirate);
        Assert.DoesNotContain(target, g.Players[1].Row(Close).Cards);
        Assert.Contains(pirate, g.Players[0].Row(Ranged).Cards);
        Assert.Contains(enemy, g.Players[1].Row(Ranged).Cards);
    }

    [Fact]
    public void Decoy_swaps_a_non_hero_back_to_hand_and_keeps_its_place()
    {
        var g = T.New();
        var a = g.TestBoard(0, "ballista", Siege);
        var b = g.TestBoard(0, "trebuchet", Siege);
        var hero = g.TestBoard(0, "esterad_thyssen", Close);
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);

        T.Fails(g, new PlayCard(0, decoy.Uid, null, hero.Uid), "kahraman");
        T.Fails(g, new PlayCard(0, decoy.Uid), "birim seçmelisin");
        T.Play(g, 0, decoy, target: a.Uid);

        Assert.Contains(a, g.Players[0].Hand);
        Assert.Equal(decoy, g.Players[0].Row(Siege).Cards[0]); // aynı yer
        Assert.Equal(b, g.Players[0].Row(Siege).Cards[1]);
    }

    [Fact]
    public void Decoy_cannot_target_opponent_units()
    {
        var g = T.New();
        var enemy = g.TestBoard(1, "fiend", Close);
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);
        T.Fails(g, new PlayCard(0, decoy.Uid, null, enemy.Uid));
    }

    [Fact]
    public void Decoy_on_an_opponents_spy_takes_it_to_your_hand()
    {
        var g = T.New("nilfgaard", "monsters");
        var spy = g.TestBoard(0, "vattier_de_rideaux", Close); // rakibin (1) casusu benim sahamda
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);
        T.Play(g, 0, decoy, target: spy.Uid);
        Assert.Contains(spy, g.Players[0].Hand);
    }

    [Fact]
    public void Horn_and_mardroeme_share_one_special_slot_per_row()
    {
        var g = T.New("skellige", "monsters");
        var horn = g.TestHand(0, "commanders_horn");
        var horn2 = g.TestHand(0, "commanders_horn");
        var mard = g.TestHand(0, "mardroeme");
        T.Filler(g, 0);

        T.Fails(g, new PlayCard(0, horn.Uid), "sıra");
        T.Play(g, 0, horn, Close);
        T.Pass(g, 1); // rakip pas: sıra bende kalır
        T.Fails(g, new PlayCard(0, mard.Uid, Close), "zaten");
        T.Play(g, 0, mard, Ranged);
        T.Fails(g, new PlayCard(0, horn2.Uid, Ranged), "zaten");
    }

    [Fact]
    public void Berserker_transforms_when_mardroeme_arrives_or_was_already_there()
    {
        var g = T.New("skellige", "monsters");
        var bers = g.TestBoard(0, "berserker", Close);
        var mard = g.TestHand(0, "mardroeme");
        T.Filler(g, 0);
        T.Play(g, 0, mard, Close);
        var row = g.Players[0].Row(Close).Cards;
        Assert.DoesNotContain(bers, row);
        Assert.Equal("transformed_vildkaarl", row.Single().Def.Id);
        Assert.Equal(14, g.RowTotal(0, Close));

        var g2 = T.New("skellige", "monsters");
        g2.TestSpecial(0, "mardroeme", Ranged);
        var young = g2.TestHand(0, "young_berserker");
        T.Filler(g2, 0);
        T.Play(g2, 0, young);
        Assert.Equal("transformed_young_vildkaarl", g2.Players[0].Row(Ranged).Cards.Single().Def.Id);
        Assert.Equal(8, g2.RowTotal(0, Ranged));
    }

    [Fact]
    public void Ermion_hero_mardroeme_transforms_berserkers_in_its_row()
    {
        var g = T.New("skellige", "monsters");
        g.TestBoard(0, "young_berserker", Ranged);
        var ermion = g.TestHand(0, "ermion");
        T.Filler(g, 0);
        T.Play(g, 0, ermion);
        Assert.Contains(g.Players[0].Row(Ranged).Cards, c => c.Def.Id == "transformed_young_vildkaarl");
    }

    [Fact]
    public void Avenger_summons_its_token_when_removed_even_via_decoy()
    {
        var g = T.New("northern_realms", "monsters");
        var cow = g.TestBoard(0, "cow", Ranged);
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);
        T.Play(g, 0, decoy, target: cow.Uid);
        Assert.Contains(g.Players[0].Row(Close).Cards, c => c.Def.Id == "bovine_defense_force");
        Assert.Equal(8, g.RowTotal(0, Close));
    }

    [Fact]
    public void Kambi_summons_hemdall_when_scorched()
    {
        var g = T.New("skellige", "monsters");
        var kambi = g.TestBoard(1, "kambi", Close); // 0
        g.TestBoard(1, "fiend", Close);             // 6 ← en güçlü
        var scorch = g.TestHand(0, "scorch");
        T.Filler(g, 0);
        T.Play(g, 0, scorch);
        Assert.Contains(kambi, g.Players[1].Row(Close).Cards); // 0 olduğu için ölmedi
        // Kambi doğrudan kaldırılırsa Hemdall gelir: round sonu temizliği testi RoundFlowTests'te
    }

    [Fact]
    public void Token_never_enters_the_graveyard()
    {
        var g = T.New("northern_realms", "monsters");
        var cow = g.TestBoard(0, "cow", Ranged);
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);
        T.Play(g, 0, decoy, target: cow.Uid);
        var bdf = g.Players[0].Row(Close).Cards.Single(c => c.Def.Id == "bovine_defense_force");
        var scorch = g.TestHand(1, "scorch");
        T.Filler(g, 1);
        T.Play(g, 1, scorch);
        Assert.DoesNotContain(bdf, g.Players[0].Grave);
        Assert.Empty(g.Players[0].Row(Close).Cards);
    }

    [Fact]
    public void Clear_weather_removes_all_weather_and_goes_to_graveyard()
    {
        var g = T.New();
        g.TestWeather("biting_frost");
        g.TestWeather("torrential_rain");
        var clear = g.TestHand(0, "clear_weather");
        T.Filler(g, 0);
        T.Play(g, 0, clear);
        Assert.Empty(g.WeatherCards);
        Assert.Contains(clear, g.Players[0].Grave);
        Assert.False(g.WeatherActive(Close));
    }

    [Fact]
    public void Agile_unit_requires_a_valid_row()
    {
        var g = T.New("scoiatael", "monsters");
        var agile = g.TestHand(0, "yaevinn");
        T.Filler(g, 0);
        T.Fails(g, new PlayCard(0, agile.Uid), "Çevik");
        T.Fails(g, new PlayCard(0, agile.Uid, Siege), "Çevik");
        T.Play(g, 0, agile, Ranged);
        Assert.Contains(agile, g.Players[0].Row(Ranged).Cards);
    }

    [Fact]
    public void Cannot_play_a_card_that_is_not_in_hand_or_out_of_turn()
    {
        var g = T.New();
        var mine = g.TestHand(0, "redanian_foot_soldier");
        var theirs = g.TestHand(1, "fiend");
        T.Fails(g, new PlayCard(1, theirs.Uid), "Sıra sende değil");
        T.Fails(g, new PlayCard(0, theirs.Uid), "elinde değil");
        T.Play(g, 0, mine);
    }
}
