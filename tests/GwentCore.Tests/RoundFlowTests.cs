using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

public class RoundFlowTests
{
    /// <summary>İki oyuncu da kart oynayıp raundu sonuçlandırır: 0 → puan, 1 → puan.</summary>
    private static void FinishRound(Game g, string unit0, string unit1)
    {
        T.Filler(g, 0); T.Filler(g, 1);
        var a = g.TestHand(0, unit0);
        var b = g.TestHand(1, unit1);
        T.Play(g, 0, a);
        T.Play(g, 1, b);
        T.Pass(g, 0);
        T.Pass(g, 1);
    }

    [Fact]
    public void Turns_alternate_and_opponent_keeps_playing_after_a_pass()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        Assert.Equal(0, g.Current);
        T.Play(g, 0, g.Players[0].Hand[0]);
        Assert.Equal(1, g.Current);
        T.Pass(g, 1);
        T.Play(g, 0, g.Players[0].Hand[0]);
        Assert.Equal(0, g.Current); // rakip pas: sıra bende kalır
        T.Fails(g, new PlayCard(1, g.Players[1].Hand[0].Uid)); // pas geçmiş oyuncu oynayamaz
    }

    [Fact]
    public void Higher_total_wins_the_round_loser_loses_a_gem()
    {
        var g = T.New();
        FinishRound(g, "ballista", "nekker"); // 6 vs 2
        Assert.Equal(2, g.Players[0].Gems);
        Assert.Equal(1, g.Players[1].Gems);
        Assert.Equal(2, g.Round);
        Assert.Equal(new RoundResult(1, 6, 2, 0), g.History[0]);
    }

    [Fact]
    public void Round_winner_starts_next_round()
    {
        var g = T.New(first: 0);
        FinishRound(g, "nekker", "ballista"); // 1 kazanır
        Assert.Equal(1, g.Current);
        Assert.Equal(1, g.FirstPlayer);
    }

    [Fact]
    public void Tie_costs_both_a_gem_and_same_player_starts_again()
    {
        var g = T.New("northern_realms", "monsters", first: 0);
        FinishRound(g, "ballista", "fire_elemental"); // 6 vs 6
        Assert.Equal(1, g.Players[0].Gems);
        Assert.Equal(1, g.Players[1].Gems);
        Assert.Null(g.History[0].Winner);
        Assert.Equal(0, g.Current);
    }

    [Fact]
    public void Nilfgaard_wins_ties_but_two_nilfgaards_still_draw()
    {
        var g = T.New("nilfgaard", "monsters");
        FinishRound(g, "rotten_mangonel", "nekker");  // 3 vs 2... eşitlik için ayrı kurulum
        var g2 = T.New("nilfgaard", "monsters");
        FinishRound(g2, "siege_engineer", "fire_elemental"); // 6 vs 6
        Assert.Equal(0, g2.History[0].Winner);
        Assert.Equal(1, g2.Players[1].Gems);
        Assert.Equal(2, g2.Players[0].Gems);

        var g3 = T.New("nilfgaard", "nilfgaard");
        FinishRound(g3, "siege_engineer", "siege_engineer");
        Assert.Null(g3.History[0].Winner);
        Assert.Equal(1, g3.Players[0].Gems);
        Assert.Equal(1, g3.Players[1].Gems);
    }

    [Fact]
    public void Board_and_weather_are_cleared_to_graveyards_at_round_end()
    {
        var g = T.New("northern_realms", "northern_realms");
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestWeather("biting_frost");
        FinishRound(g, "ballista", "redanian_foot_soldier");
        Assert.All(g.Players, p => Assert.All(p.Rows, r => Assert.Empty(r.Cards)));
        Assert.Empty(g.WeatherCards);
        Assert.Contains(g.Players[0].Grave, c => c.Def.Id == "ballista");
        Assert.Contains(g.Players[1].Grave, c => c.Def.Id == "redanian_foot_soldier");
    }

    [Fact]
    public void Game_ends_when_a_player_loses_both_gems()
    {
        var g = T.New();
        FinishRound(g, "ballista", "nekker");
        // Raund 2: 1 başlar (kazanan değil, kaybeden 1'dir? Hayır: kazanan 0 başlar)
        Assert.Equal(0, g.Current);
        FinishRound2(g, 0, 1);
        Assert.Equal(Phase.GameOver, g.Phase);
        Assert.Equal(0, g.Outcome!.Winner);
        T.Fails(g, new Pass(0), "Oyun bitti");
    }

    private static void FinishRound2(Game g, int winner, int loser)
    {
        // Mevcut oyuncudan başlayarak: kazanan 6, kaybeden 2 oynar.
        var w = g.TestHand(winner, "ballista");
        var l = g.TestHand(loser, "nekker");
        T.Filler(g, winner, 3); T.Filler(g, loser, 3);
        if (g.Current == winner) { T.Play(g, winner, w); T.Play(g, loser, l); }
        else { T.Play(g, loser, l); T.Play(g, winner, w); }
        T.Pass(g, g.Current);
        T.Pass(g, g.Current);
    }

    [Fact]
    public void Double_loss_in_final_round_is_a_draw()
    {
        var g = T.New("northern_realms", "scoiatael", first: 0);
        FinishRound(g, "ballista", "dol_blathanna_archer");      // 6-4: 0 kazanır
        Assert.Equal(0, g.Current);
        var w0 = g.TestHand(0, "yarpen_zigrin");                  // 2
        var w1 = g.TestHand(1, "mahakaman_defender");             // 5
        T.Filler(g, 0, 3); T.Filler(g, 1, 3);
        T.Play(g, 0, w0); T.Play(g, 1, w1); T.Pass(g, 0); T.Pass(g, 1); // 1 kazanır
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.Equal(3, g.Round);
        Assert.Equal(1, g.Current);
        var c1 = g.TestHand(1, "barclay_els");                    // çevik 6
        var c0 = g.TestHand(0, "ballista");                       // 6
        T.Play(g, 1, c1, Close); T.Play(g, 0, c0);
        T.Pass(g, g.Current); T.Pass(g, g.Current);
        Assert.Equal(Phase.GameOver, g.Phase);
        Assert.Null(g.Outcome!.Winner);
    }

    [Fact]
    public void Player_with_empty_hand_and_no_leader_passes_automatically()
    {
        var g = T.New(l0: "king_bran", f0: "skellige"); // Bran pasif: lider kullanılamaz
        var only = g.TestHand(0, "blueboy_lugos");
        T.Filler(g, 1);
        T.Play(g, 0, only);
        T.Play(g, 1, g.Players[1].Hand[0]);
        // 0'ın eli boş → kendiliğinden pas
        Assert.True(g.Players[0].Passed);
        Assert.Equal(1, g.Current);
    }

    [Fact]
    public void Northern_realms_draws_a_card_after_winning_a_round()
    {
        var g = T.New("northern_realms", "monsters");
        g.TestDeck(0, "keira_metz");
        FinishRound(g, "ballista", "nekker");
        Assert.Contains(g.Players[0].Hand, c => c.Def.Id == "keira_metz");

        var g2 = T.New("northern_realms", "monsters");
        g2.TestDeck(0, "keira_metz");
        FinishRound(g2, "nekker", "ballista"); // NR kaybetti
        Assert.DoesNotContain(g2.Players[0].Hand, c => c.Def.Id == "keira_metz");
    }

    [Fact]
    public void Monsters_keep_one_random_unit_for_the_next_round_then_lose_it_normally()
    {
        var g = T.New("monsters", "northern_realms", seed: 3);
        T.Filler(g, 0); T.Filler(g, 1);
        var a = g.TestHand(0, "fiend");
        var b = g.TestHand(0, "frightener");
        var enemy = g.TestHand(1, "nekker");
        T.Play(g, 0, a); T.Play(g, 1, enemy); T.Play(g, 0, b);
        T.Pass(g, 1); T.Pass(g, 0);
        var kept = g.Players[0].Rows.SelectMany(r => r.Cards).ToList();
        Assert.Single(kept);
        Assert.True(kept[0] == a || kept[0] == b);
        Assert.Empty(g.Players[1].Rows.SelectMany(r => r.Cards));
        Assert.True(g.PlayerTotal(0) >= 5); // kalan birim yeni raundun başlangıç puanı
    }

    /// <summary>1. raundu 1 kazanır, 2. raundu 0 kazanır; mezarlığa yalnız kahramanlar eklenir.</summary>
    private static Game SkelligeRoundThree(params string[] graveCards)
    {
        var g = T.New("skellige", "northern_realms", first: 0);
        foreach (var id in graveCards) g.TestGrave(0, id);
        T.Filler(g, 0); T.Filler(g, 1);
        var a = g.TestHand(0, "ermion");                 // hero 8
        var b = g.TestHand(1, "esterad_thyssen");        // hero 10
        T.Play(g, 0, a); T.Play(g, 1, b); T.Pass(g, 0); T.Pass(g, 1);
        Assert.Equal(1, g.Current);                       // kazanan 1 başlar
        T.Pass(g, 1);                                     // 1 hemen pas
        var c = g.TestHand(0, "hjalmar");                // hero 10
        T.Play(g, 0, c);
        T.Pass(g, 0);
        return g;
    }

    [Fact]
    public void Skellige_revives_two_random_units_at_start_of_round_three()
    {
        var g = SkelligeRoundThree("blueboy_lugos", "blueboy_lugos", "madman_lugos", "madman_lugos");
        Assert.Equal(3, g.Round);
        Assert.Null(g.Pending);
        Assert.Equal(2, g.Players[0].Rows.SelectMany(r => r.Cards).Count());
        Assert.Equal(2, g.Players[0].Grave.Count(c => c.Def.IsUnit && !c.Def.Hero));
    }

    [Fact]
    public void Skellige_agile_revive_asks_for_a_row()
    {
        var g = SkelligeRoundThree("olaf");
        Assert.Equal(3, g.Round);
        Assert.NotNull(g.Pending);
        Assert.Equal(ChoiceKind.AgileRow, g.Pending!.Kind);
        T.Fails(g, new PlayCard(g.Current, g.Players[g.Current].Hand[0].Uid)); // seçim bitmeden oynanamaz
        T.Fails(g, new Choose(0, g.Pending.Candidates, Siege));
        T.Ok(g, new Choose(0, g.Pending.Candidates, Close));
        Assert.Null(g.Pending);
        Assert.Single(g.Players[0].Rows.SelectMany(r => r.Cards));
    }

    [Fact]
    public void Skellige_effect_does_not_trigger_in_other_rounds()
    {
        var g = T.New("skellige", "northern_realms");
        g.TestGrave(0, "blueboy_lugos");
        T.Filler(g, 0); T.Filler(g, 1);
        T.Pass(g, 0); T.Pass(g, 1);
        Assert.Equal(2, g.Round);
        Assert.Empty(g.Players[0].Rows.SelectMany(r => r.Cards));
    }

    [Fact]
    public void Avenger_token_summoned_at_round_end_stays_on_the_board()
    {
        // Yeniden yapım davranışı: ⚠️ orijinalde doğrulanacak (KURALLAR.md §13)
        var g = T.New("northern_realms", "monsters");
        g.TestBoard(0, "cow", Ranged);
        T.Filler(g, 0); T.Filler(g, 1);
        T.Pass(g, 0); T.Pass(g, 1);
        Assert.Contains(g.Players[0].Row(Close).Cards, c => c.Def.Id == "bovine_defense_force");
        Assert.DoesNotContain(g.Players[0].Grave, c => c.Def.Id == "bovine_defense_force");
    }
}
