using GwentAI;
using GwentCore;
using KaerMorhen.Core;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Kaer Morhen: Kışla avantajları ve Simya iksirleri (motor + kale).</summary>
public class PerkTests
{
    private static Game New(Perks perks, string f0 = "northern_realms", string f1 = "monsters", int seed = 3) =>
        new(DeckValidator.RecommendedDeck(f0) with { Perks = perks }, DeckValidator.RecommendedDeck(f1), seed);

    /// <summary>Boş elli test oyunu; oyuncu 0'ın avantajları verilir.</summary>
    private static Game Bare(Perks perks)
    {
        var d0 = DeckValidator.DefaultDeck("northern_realms") with { Perks = perks };
        var g = new Game(d0, DeckValidator.DefaultDeck("monsters"), 1);
        g.TestEmpty();
        g.TestStart(0);
        return g;
    }

    [Fact]
    public void Barracks_gives_an_extra_card_and_mulligan()
    {
        var g = New(new Perks(ExtraHand: 1, ExtraMulligans: 1));
        Assert.Equal(Game.StartingHand + 1, g.Players[0].Hand.Count);
        Assert.Equal(3, g.MulligansFor(0));
        Assert.Equal(2, g.MulligansFor(1));
        Assert.False(g.Apply(new Mulligan(1, g.Players[1].Hand.Take(3).Select(c => c.Uid).ToList())).Ok);
        Assert.Contains(g.LegalActions(), a => a is Mulligan { Player: 0, Cards.Count: 3 });
        Assert.DoesNotContain(g.LegalActions(), a => a is Mulligan { Player: 1, Cards.Count: 3 });
        Assert.True(g.Apply(new Mulligan(0, g.Players[0].Hand.Take(3).Select(c => c.Uid).ToList())).Ok);
    }

    [Fact]
    public void Barracks_lets_you_choose_who_starts_unless_both_can()
    {
        var g = New(new Perks(ChoosesFirst: true));
        T.Ok(g, new Mulligan(0, Array.Empty<int>()));
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(Phase.ChooseFirst, g.Phase);
        Assert.Equal(0, g.Current);
        var both = New(new Perks(ChoosesFirst: true), f1: "scoiatael");
        T.Ok(both, new Mulligan(0, Array.Empty<int>()));
        T.Ok(both, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(Phase.Playing, both.Phase);   // ikisi de seçebiliyorsa yazı-tura
    }

    [Fact]
    public void Cat_peeks_three_opponent_cards_at_the_start()
    {
        var g = New(new Perks(Potions: new[] { PotionIds.Cat }));
        T.Ok(g, new Mulligan(0, Array.Empty<int>()));
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        var peek = Assert.Single(g.Events, e => e.Kind == "peek");
        Assert.Equal(0, peek.Visible);
        Assert.Equal(3, peek.Detail.Split(',').Length);
        Assert.All(peek.Detail.Split(','), id => Assert.Contains(g.Players[1].Hand, c => c.Def.Id == id));
    }

    [Fact]
    public void Swallow_draws_after_a_lost_round()
    {
        var g = Bare(new Perks(Potions: new[] { PotionIds.Swallow }));
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestDeck(0, "ballista");
        g.TestBoard(1, "vampire_katakan", RowKind.Close);
        var before = g.Players[0].Hand.Count;
        T.Pass(g, 0); T.Pass(g, 1);
        Assert.Equal(2, g.Round);
        Assert.Equal(before + 1, g.Players[0].Hand.Count);
    }

    [Fact]
    public void Thunderbolt_buffs_close_combat_in_round_one_only()
    {
        var g = Bare(new Perks(Potions: new[] { PotionIds.Thunderbolt }));
        T.Filler(g, 0); T.Filler(g, 1);
        var c = g.TestBoard(0, "siegfried_of_denesle", RowKind.Close);
        var r = g.TestBoard(0, "keira_metz", RowKind.Ranged);
        var o = g.TestBoard(1, "vampire_katakan", RowKind.Close);
        Assert.Equal(6, g.CardScore(c));
        Assert.Equal(5, g.CardScore(r));
        Assert.Equal(5, g.CardScore(o));
        T.Pass(g, 0); T.Pass(g, 1);
        var c2 = g.TestBoard(0, "siegfried_of_denesle", RowKind.Close);
        Assert.Equal(5, g.CardScore(c2));
    }

    [Fact]
    public void Tawny_owl_halves_weather_instead()
    {
        var g = Bare(new Perks(Potions: new[] { PotionIds.TawnyOwl }));
        var mine = g.TestBoard(0, "ballista", RowKind.Siege);
        var theirs = g.TestBoard(1, "arachas_behemoth", RowKind.Siege);
        g.TestWeather(Ids.TorrentialRain);
        Assert.Equal(3, g.CardScore(mine));
        Assert.Equal(1, g.CardScore(theirs));
    }

    [Fact]
    public void Black_blood_blocks_the_first_enemy_spy_only()
    {
        var g = Bare(new Perks(Potions: new[] { PotionIds.BlackBlood }));
        T.Filler(g, 0); T.Filler(g, 1);
        for (var i = 0; i < 6; i++) g.TestDeck(1, "vampire_fleder");
        T.Play(g, 0, g.Players[0].Hand[0]);
        var hand = g.Players[1].Hand.Count;
        var spy = g.TestHand(1, "avallach");
        T.Play(g, 1, spy);
        Assert.Equal(hand, g.Players[1].Hand.Count);           // casus oynandı, kart çekilmedi
        Assert.Contains(g.Events, e => e.Kind == "potion_black_blood");
        T.Play(g, 0, g.Players[0].Hand[0]);
        T.Play(g, 1, g.TestHand(1, "avallach"));
        Assert.Equal(hand + 2, g.Players[1].Hand.Count);       // ikinci casus çeker
    }

    [Fact]
    public void Alchemy_limits_toxicity_and_unlocks_by_level()
    {
        var s = Keep.NewSave(1);
        Assert.False(Keep.TogglePotion(s, PotionIds.Cat));          // simya yok
        s.Buildings[Keep.Alchemy] = 1;
        Assert.True(Keep.TogglePotion(s, PotionIds.Cat));           // 2
        Assert.False(Keep.TogglePotion(s, PotionIds.Swallow));      // 2+2 > 3
        Assert.False(Keep.TogglePotion(s, PotionIds.Thunderbolt));  // kilitli
        s.Buildings[Keep.Alchemy] = 3;
        Assert.True(Keep.TogglePotion(s, PotionIds.Swallow));
        Assert.True(Keep.TogglePotion(s, PotionIds.BlackBlood));    // 7
        Assert.False(Keep.TogglePotion(s, PotionIds.TawnyOwl));
        Assert.Equal(7, Keep.Toxicity(Keep.MatchPotions(s)));
        s.Buildings[Keep.Alchemy] = 1;                              // seviye düşerse fazlası geçmez
        Assert.Equal(new[] { PotionIds.Cat }, Keep.MatchPotions(s));
    }

    [Fact]
    public void Keep_perks_follow_the_buildings()
    {
        var s = Keep.NewSave(1);
        Assert.Null(Keep.MatchPerks(s));
        s.Buildings[Keep.Barracks] = 3;
        Assert.Equal(new Perks(1, 1, true), Keep.MatchPerks(s));
        var deck = DeckValidator.RecommendedDeck("skellige") with { Perks = Keep.MatchPerks(s) };
        Assert.Empty(DeckValidator.Validate(deck));
    }

    [Fact]
    public void Perk_games_play_to_the_end_and_replay()
    {
        var mine = DeckValidator.RecommendedDeck("scoiatael");
        var replay = new ReplayData
        {
            MyFaction = mine.Faction, MyLeaderId = mine.LeaderId, MyDeckCards = mine.CardIds.ToList(),
            MyPerks = new Perks(1, 1, true, PotionIds.All.ToList()),
            OppFaction = "nilfgaard", OppLeaderId = CardCatalog.Default.Leaders("nilfgaard").First().Id, Seed = 21, Difficulty = (int)Difficulty.Hard
        };
        var g = replay.CreateGame();
        var agents = new IAgent[] { new HeuristicAgent(), new HeuristicAgent(seed: 5) };
        while (g.Phase != Phase.GameOver)
        {
            var p = g.PlayersToAct()[0];
            var a = agents[p].Decide(g, p);
            replay.Actions.Add(ReplayData.Encode(a));
            Assert.True(g.Apply(a).Ok);
        }
        var again = ReplayData.FromJson(replay.ToJson()).Run();
        Assert.Equal(g.History, again.History);
    }
}
