using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

public class LeaderTests
{
    [Fact]
    public void Every_leader_in_the_data_is_known_to_the_engine()
    {
        var cat = CardCatalog.Default;
        foreach (var leader in cat.All.Where(c => c.IsLeader))
        {
            var g = T.New(leader.Faction, "monsters", l0: leader.Id);
            // Pasif ya da etkin: motor tanımasa LeaderBlockedReason "tanımlı değil" derdi
            Assert.NotEqual("Bu liderin yeteneği tanımlı değil.", g.LeaderBlockedReason(0));
        }
    }

    [Fact]
    public void Foltest_king_plays_fog_from_the_deck()
    {
        var g = T.New(l0: "foltest_king_of_temeria");
        var fog = g.TestDeck(0, "impenetrable_fog");
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        Assert.Contains(fog, g.WeatherCards);
        Assert.True(g.WeatherActive(Ranged));
        T.Fails(g, new UseLeader(0), "Sıra"); // sıra rakipte
    }

    [Fact]
    public void Leader_is_blocked_without_a_target_and_can_be_used_once()
    {
        var g = T.New(l0: "foltest_king_of_temeria");
        T.Filler(g, 0);
        Assert.NotNull(g.LeaderBlockedReason(0)); // destede Fog yok
        T.Fails(g, new UseLeader(0), "Fog");
        Assert.False(g.Players[0].LeaderUsed);

        g.TestDeck(0, "impenetrable_fog");
        g.TestDeck(0, "impenetrable_fog");
        T.Ok(g, new UseLeader(0));
        T.Pass(g, 1);
        T.Fails(g, new UseLeader(0), "zaten");
    }

    [Fact]
    public void Siegemaster_horns_siege_row_unless_a_horn_is_there()
    {
        var g = T.New(l0: "foltest_the_siegemaster");
        var s = g.TestBoard(0, "ballista", Siege);
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        Assert.Equal(12, g.CardScore(s));

        var g2 = T.New(l0: "foltest_the_siegemaster");
        g2.TestSpecial(0, "commanders_horn", Siege);
        T.Filler(g2, 0);
        T.Fails(g2, new UseLeader(0), "Boynuz");
    }

    [Fact]
    public void Lider_horn_marker_is_removed_at_round_end_without_entering_graveyard()
    {
        var g = T.New(l0: "foltest_the_siegemaster");
        T.Filler(g, 0); T.Filler(g, 1);
        T.Ok(g, new UseLeader(0));
        T.Pass(g, 1); T.Pass(g, 0);
        Assert.Null(g.Players[0].Row(Siege).Special);
        Assert.DoesNotContain(g.Players[0].Grave, c => c.Def.Id == "commanders_horn");
    }

    [Fact]
    public void Steel_forged_scorches_enemy_siege_only_with_ten_or_more()
    {
        var g = T.New(l0: "foltest_the_steel_forged");
        g.TestBoard(1, "fire_elemental", Siege); // 6
        T.Filler(g, 0);
        T.Fails(g, new UseLeader(0), "10");

        var g2 = T.New(l0: "foltest_the_steel_forged");
        var big = g2.TestBoard(1, "fire_elemental", Siege);
        g2.TestBoard(1, "earth_elemental", Siege);
        var small = g2.TestBoard(1, "ice_giant", Siege); // 5
        T.Filler(g2, 0);
        T.Ok(g2, new UseLeader(0));
        Assert.DoesNotContain(big, g2.Players[1].Row(Siege).Cards);
        Assert.Contains(small, g2.Players[1].Row(Siege).Cards);
    }

    [Fact]
    public void White_flame_cancels_the_opponents_leader()
    {
        var g = T.New("northern_realms", "nilfgaard", l0: "foltest_the_siegemaster", l1: "emhyr_var_emreis_the_white_flame");
        T.Filler(g, 0);
        Assert.True(g.LeaderCancelled(0));
        Assert.False(g.LeaderAvailable(0));
        T.Fails(g, new UseLeader(0), "iptal");
    }

    [Fact]
    public void Daisy_draws_an_extra_card_at_game_start()
    {
        var d = DeckValidator.DefaultDeck("scoiatael");
        var plain = new Game(d, d, 7);
        var daisy = new Game(d with { LeaderId = "francesca_findabair_daisy_of_the_valley" }, d, 7);
        Assert.Equal(Game.StartingHand, plain.Players[0].Hand.Count);
        Assert.Equal(Game.StartingHand + 1, daisy.Players[0].Hand.Count);
    }

    [Fact]
    public void Daisy_is_cancelled_by_white_flame_at_game_start()
    {
        var d = DeckValidator.DefaultDeck("scoiatael");
        var n = DeckValidator.DefaultDeck("nilfgaard") with { LeaderId = "emhyr_var_emreis_the_white_flame" };
        var g = new Game(d with { LeaderId = "francesca_findabair_daisy_of_the_valley" }, n, 7);
        Assert.Equal(Game.StartingHand, g.Players[0].Hand.Count);
    }

    [Fact]
    public void Crach_shuffles_every_graveyard_back_into_the_decks()
    {
        var g = T.New("skellige", "monsters", l0: "crach_an_craite");
        g.TestGrave(0, "blueboy_lugos"); g.TestGrave(1, "fiend");
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        Assert.Empty(g.Players[0].Grave);
        Assert.Empty(g.Players[1].Grave);
        Assert.Single(g.Players[0].Deck);
        Assert.Single(g.Players[1].Deck);
    }

    [Fact]
    public void Destroyer_discards_two_then_draws_a_chosen_card()
    {
        var g = T.New("monsters", "northern_realms", l0: "eredin_destroyer_of_worlds");
        var h1 = g.TestHand(0, "fiend"); var h2 = g.TestHand(0, "nekker"); var h3 = g.TestHand(0, "wyvern");
        var d1 = g.TestDeck(0, "draug"); var d2 = g.TestDeck(0, "imlerith");

        T.Ok(g, new UseLeader(0));
        Assert.Equal(ChoiceKind.DestroyerDiscard, g.Pending!.Kind);
        T.Fails(g, new Choose(0, new[] { h1.Uid }), "2 kart");
        T.Ok(g, new Choose(0, new[] { h1.Uid, h2.Uid }));
        Assert.Equal(ChoiceKind.DestroyerDraw, g.Pending!.Kind);
        T.Ok(g, new Choose(0, new[] { d2.Uid }));

        Assert.Contains(d2, g.Players[0].Hand);
        Assert.Contains(h1, g.Players[0].Grave);
        Assert.Contains(h3, g.Players[0].Hand);
        Assert.Contains(d1, g.Players[0].Deck);
        Assert.Equal(1, g.Current);
    }

    [Fact]
    public void Bringer_of_death_and_relentless_return_cards_from_graveyards()
    {
        var g = T.New("monsters", "northern_realms", l0: "eredin_bringer_of_death");
        var mine = g.TestGrave(0, "fiend");
        g.TestGrave(0, "draug"); // kahraman
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        Assert.Equal(new[] { mine.Uid }, g.Pending!.Candidates);
        T.Ok(g, new Choose(0, new[] { mine.Uid }));
        Assert.Contains(mine, g.Players[0].Hand);

        var g2 = T.New("nilfgaard", "northern_realms", l0: "emhyr_var_emreis_the_relentless");
        var theirs = g2.TestGrave(1, "ballista");
        T.Filler(g2, 0);
        T.Ok(g2, new UseLeader(0));
        T.Ok(g2, new Choose(0, new[] { theirs.Uid }));
        Assert.Contains(theirs, g2.Players[0].Hand);
        Assert.Equal(0, theirs.Holder);
    }

    [Fact]
    public void Eredin_king_plays_a_chosen_weather_card()
    {
        var g = T.New("monsters", "northern_realms", l0: "eredin_king_of_the_wild_hunt");
        var frost = g.TestDeck(0, "biting_frost");
        g.TestDeck(0, "impenetrable_fog");
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        T.Ok(g, new Choose(0, new[] { frost.Uid }));
        Assert.Contains(frost, g.WeatherCards);
    }

    [Fact]
    public void Emperor_reveals_up_to_three_cards_only_to_its_owner()
    {
        var g = T.New("nilfgaard", "monsters", l0: "emhyr_var_emreis_emperor_of_nilfgaard");
        for (var i = 0; i < 5; i++) g.TestHand(1, "fiend");
        T.Filler(g, 0);
        T.Ok(g, new UseLeader(0));
        var peek = g.Events.Single(e => e.Kind == "peek");
        Assert.Equal(0, peek.Visible);
        Assert.Equal(3, peek.Detail.Split(',').Length);
    }

    [Fact]
    public void Hope_moves_agile_units_to_the_best_rows()
    {
        var g = T.New("scoiatael", "monsters", l0: "francesca_findabair_hope_of_the_aen_seidhe");
        g.TestBoard(0, "olaf", Close);                    // morale: ranged'e gidince +1 verir
        var agile = g.TestBoard(0, "yaevinn", Close);    // 6
        g.TestBoard(0, "toruviel", Ranged);               // 2
        T.Filler(g, 0);
        var before = g.PlayerTotal(0);
        T.Ok(g, new UseLeader(0));
        Assert.True(g.PlayerTotal(0) >= before);
    }

    [Fact]
    public void Invader_makes_medic_pick_randomly()
    {
        var g = T.New("northern_realms", "nilfgaard", l1: "emhyr_var_emreis_invader_of_the_north");
        g.TestGrave(0, "ballista");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);
        T.Play(g, 0, medic);
        Assert.Null(g.Pending); // seçim sorulmadı
        Assert.Contains(g.Players[0].Row(Siege).Cards, c => c.Def.Id == "ballista");
    }

    [Fact]
    public void Passive_leaders_cannot_be_activated()
    {
        var g = T.New("skellige", "monsters", l0: "king_bran");
        T.Filler(g, 0);
        T.Fails(g, new UseLeader(0), "otomatik");
    }
}
