using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

public class ScoringTests
{
    [Fact]
    public void Plain_units_sum_per_row_and_player()
    {
        var g = T.New();
        g.TestBoard(0, "ballista", Siege);               // 6
        g.TestBoard(0, "dethmold", Ranged);              // 6
        g.TestBoard(1, "fiend", Close);                  // 6
        Assert.Equal(6, g.RowTotal(0, Siege));
        Assert.Equal(12, g.PlayerTotal(0));
        Assert.Equal(6, g.PlayerTotal(1));
    }

    [Fact]
    public void Weather_sets_non_heroes_to_one_in_that_row_for_both_players_and_hero_is_immune()
    {
        var g = T.New();
        var mine = g.TestBoard(0, "blue_stripes_commando", Close);   // 4
        var hero = g.TestBoard(0, "esterad_thyssen", Close);          // 10 hero
        var theirs = g.TestBoard(1, "fiend", Close);                   // 6
        var ranged = g.TestBoard(0, "dethmold", Ranged);               // 6, etkilenmez
        g.TestWeather("biting_frost");

        Assert.Equal(1, g.CardScore(mine));
        Assert.Equal(10, g.CardScore(hero));
        Assert.Equal(1, g.CardScore(theirs));
        Assert.Equal(6, g.CardScore(ranged));
    }

    [Fact]
    public void Each_weather_card_hits_its_own_row_and_storm_hits_ranged_and_siege()
    {
        var g = T.New();
        var close = g.TestBoard(0, "fiend", Close);
        var ranged = g.TestBoard(0, "dethmold", Ranged);
        var siege = g.TestBoard(0, "ballista", Siege);

        g.TestWeather("impenetrable_fog");
        Assert.Equal((6, 1, 6), (g.CardScore(close), g.CardScore(ranged), g.CardScore(siege)));
        g.TestWeather("torrential_rain");
        Assert.Equal((6, 1, 1), (g.CardScore(close), g.CardScore(ranged), g.CardScore(siege)));

        var g2 = T.New();
        var c2 = g2.TestBoard(0, "fiend", Close);
        var r2 = g2.TestBoard(0, "dethmold", Ranged);
        var s2 = g2.TestBoard(0, "ballista", Siege);
        g2.TestWeather("skellige_storm");
        Assert.Equal((6, 1, 1), (g2.CardScore(c2), g2.CardScore(r2), g2.CardScore(s2)));
    }

    [Fact]
    public void Tight_bond_multiplies_by_number_of_same_named_cards_in_the_row()
    {
        var g = T.New();
        var a = g.TestBoard(0, "poor_fucking_infantry", Close); // 1
        Assert.Equal(1, g.CardScore(a));
        var b = g.TestBoard(0, "poor_fucking_infantry", Close);
        Assert.Equal(2, g.CardScore(a));
        g.TestBoard(0, "poor_fucking_infantry", Close);
        Assert.Equal(3, g.CardScore(a));
        Assert.Equal(3, g.CardScore(b));
        Assert.Equal(9, g.RowTotal(0, Close));
        // farklı isimli bağlı kart çarpmaz
        g.TestBoard(0, "blue_stripes_commando", Close); // 4, tek başına
        Assert.Equal(4, g.CardScore(g.Players[0].Row(Close).Cards[^1]));
    }

    [Fact]
    public void Morale_boosts_others_in_row_but_not_itself_and_stacks()
    {
        var g = T.New();
        var plain = g.TestBoard(0, "fiend", Close); // 6
        var m1 = g.TestBoard(0, "olaf", Close);      // 12, morale
        Assert.Equal(7, g.CardScore(plain));
        Assert.Equal(12, g.CardScore(m1));
        var m2 = g.TestBoard(0, "isengrim_faoiltiarna", Close); // hero morale
        Assert.Equal(8, g.CardScore(plain));
        Assert.Equal(13, g.CardScore(m1)); // Isengrim Olaf'a +1 verir, Olaf kendine vermez
        Assert.Equal(10, g.CardScore(m2)); // kahraman: etkilenmez
    }

    [Fact]
    public void Horn_doubles_row_excluding_heroes_and_does_not_stack_with_other_horn_sources()
    {
        var g = T.New();
        var u = g.TestBoard(0, "fiend", Close); // 6
        var hero = g.TestBoard(0, "esterad_thyssen", Close); // 10
        g.TestSpecial(0, "commanders_horn", Close);
        Assert.Equal(12, g.CardScore(u));
        Assert.Equal(10, g.CardScore(hero));

        // Dandelion (birim boynuzu) aynı sırada: çift boynuz yok
        var dandelion = g.TestBoard(0, "dandelion", Close); // 2, horn
        Assert.Equal(12, g.CardScore(u));
        Assert.Equal(4, g.CardScore(dandelion)); // Komutan Boynuzu onu da ikiler
    }

    [Fact]
    public void Unit_horn_does_not_double_its_own_card()
    {
        var g = T.New();
        var u = g.TestBoard(0, "fiend", Close);
        var d = g.TestBoard(0, "dandelion", Close);
        Assert.Equal(12, g.CardScore(u));
        Assert.Equal(2, g.CardScore(d));
    }

    [Fact]
    public void Calculation_order_is_weather_then_bond_then_morale_then_horn()
    {
        var g = T.New("nilfgaard");
        var a = g.TestBoard(0, "impera_brigade_guard", Close); // 3 bond
        var b = g.TestBoard(0, "impera_brigade_guard", Close);
        g.TestBoard(0, "isengrim_faoiltiarna", Close);          // morale (kahraman)
        g.TestSpecial(0, "commanders_horn", Close);
        // (3 × 2 bond) + 1 morale = 7, × 2 boynuz = 14
        Assert.Equal(14, g.CardScore(a));
        Assert.Equal(14, g.CardScore(b));

        g.TestWeather("biting_frost");
        // hava: 1 → bond ×2 = 2 → +1 morale = 3 → ×2 boynuz = 6
        Assert.Equal(6, g.CardScore(a));
    }

    [Fact]
    public void King_bran_halves_strength_in_weather_instead_of_setting_to_one()
    {
        var g = T.New(l0: "king_bran", f0: "skellige");
        var u = g.TestBoard(0, "blueboy_lugos", Close); // 6
        var enemy = g.TestBoard(1, "fiend", Close);      // 6
        g.TestWeather("biting_frost");
        Assert.Equal(3, g.CardScore(u));
        Assert.Equal(1, g.CardScore(enemy)); // Bran yalnız kendi sahası için
    }

    [Fact]
    public void Treacherous_doubles_all_spy_cards_for_both_players()
    {
        var g = T.New(f0: "nilfgaard", f1: "monsters", l1: "eredin_breacc_glas_the_treacherous");
        var spy = g.TestBoard(1, "vattier_de_rideaux", Close); // 4 spy, rakip sahada
        Assert.Equal(8, g.CardScore(spy));
        var plain = g.TestBoard(1, "fiend", Close);
        Assert.Equal(6, g.CardScore(plain));
    }

    [Fact]
    public void Decoy_card_on_board_scores_nothing()
    {
        var g = T.New();
        var u = g.TestBoard(0, "fiend", Close);
        var decoy = g.TestHand(0, "decoy");
        T.Filler(g, 0);
        T.Play(g, 0, decoy, target: u.Uid);
        Assert.Equal(0, g.PlayerTotal(0));
    }
}
