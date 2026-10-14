using GwentAI;
using GwentAI.Career;
using GwentCore;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Kariyer: oyunun NPC desteleri motorda oynanabilir mi, ödüller, bölge kilitleri, turnuva ve tüccar kuralları.</summary>
public class CareerTests
{
    [Fact]
    public void Every_npc_deck_plays_a_full_game_at_every_level()
    {
        var starter = CareerLogic.NewSave().ActiveDeck()!;
        var seed = 1;
        foreach (var coll in NpcDecks.Collections)
        {
            for (var level = 1; level <= 3; level++)
            {
                Deck npc;
                try { npc = NpcDecks.Get(coll, level); }
                catch (ArgumentException) { continue; }   // oyuncunun boş fraksiyon kalıpları (Nilf, scotial...)
                seed++;
                var replay = new ReplayData
                {
                    MyFaction = starter.Faction, MyLeaderId = starter.LeaderId, MyDeckCards = starter.CardIds.ToList(),
                    OppFaction = npc.Faction, OppLeaderId = npc.LeaderId, OppDeckCards = npc.CardIds.ToList(), OppDeckUnchecked = true, Seed = seed
                };
                var g = replay.CreateGame();
                var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(Difficulty.Normal, seed) };
                for (var step = 0; g.Phase != Phase.GameOver; step++)
                {
                    Assert.True(step < 4000, $"{coll}/{level}: oyun bitmedi");
                    var p = g.PlayersToAct()[0];
                    var r = g.Apply(agents[p].Decide(g, p));
                    Assert.True(r.Ok, $"{coll}/{level}: {r.Error}");
                }
            }
        }
    }

    [Fact]
    public void Every_opponent_has_a_playable_deck_and_sits_inside_its_region_map()
    {
        foreach (var o in CareerWorld.Opponents)
        {
            foreach (var d in o.Decks) Assert.NotEmpty(NpcDecks.Get(d, o.Level).CardIds);
            var r = CareerWorld.Region(o.Region);
            Assert.InRange(o.X, r.Crop[0] + 0.02, r.Crop[0] + r.Crop[2] - 0.02);
            Assert.InRange(o.Y, r.Crop[1] + 0.02, r.Crop[1] + r.Crop[3] - 0.02);
            if (o.IsTournament) Assert.True(o.Decks.Length >= o.Rounds);
        }
        Assert.Equal(CareerWorld.Opponents.Count, CareerWorld.Opponents.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void New_career_starts_with_a_valid_northern_deck()
    {
        var s = CareerLogic.NewSave();
        Assert.Null(CareerLogic.DeckProblem(s));
        Assert.Equal(CareerLogic.StartCrowns, s.Crowns);
        Assert.Equal(1, s.Owned(CareerLogic.StarterLeader));
        Assert.All(s.Cards, kv => Assert.InRange(kv.Value, 1, CardCatalog.Default.Get(kv.Key).Copies));
        var auto = CareerLogic.AutoDeck(s, "northern_realms", CareerLogic.StarterLeader);
        Assert.Empty(DeckValidator.Validate(new Deck("northern_realms", auto.LeaderId, auto.Cards)));
    }

    [Fact]
    public void First_win_pays_the_prize_and_the_unique_card_later_wins_pay_less()
    {
        var s = CareerLogic.NewSave();
        var thaler = CareerWorld.Opponent("Thaler");
        var r1 = CareerLogic.ApplyDuel(s, thaler, true, new Random(1));
        Assert.Equal(thaler.Prize, r1.Crowns);
        Assert.Contains("geralt_of_rivia", r1.Cards);              // oyunda Thaler'i yenen Geralt kartını alır
        Assert.Equal(1, s.Owned("geralt_of_rivia"));
        Assert.Equal(CareerLogic.StartCrowns + thaler.Prize, s.Crowns);

        var r2 = CareerLogic.ApplyDuel(s, thaler, true, new Random(2));
        Assert.True(r2.Crowns < thaler.Prize && r2.Crowns > 0);
        Assert.DoesNotContain("geralt_of_rivia", r2.Cards);
        Assert.Equal(2, s.Record("Thaler").Wins);

        var lost = CareerLogic.ApplyDuel(s, thaler, false, new Random(3));
        Assert.Equal(0, lost.Crowns);
        Assert.Equal(1, s.Record("Thaler").Losses);
        Assert.All(s.Cards, kv => Assert.True(kv.Value <= CardCatalog.Default.Get(kv.Key).Copies));
    }

    [Fact]
    public void Regions_open_in_order_after_enough_distinct_wins()
    {
        var s = CareerLogic.NewSave();
        var regions = CareerWorld.Regions;
        Assert.True(CareerLogic.IsUnlocked(s, regions[0]));
        Assert.False(CareerLogic.IsUnlocked(s, regions[1]));
        var rng = new Random(4);
        for (var i = 1; i < regions.Count; i++)
        {
            Assert.False(CareerLogic.IsUnlocked(s, regions[i]));
            var prev = CareerWorld.In(regions[i - 1].Id).Where(o => !o.IsTournament).ToList();
            CareerLogic.ApplyDuel(s, prev[0], true, rng);
            CareerLogic.ApplyDuel(s, prev[0], true, rng);   // aynı rakibi iki kez yenmek sayılmaz
            for (var k = 1; k < regions[i].NeedWins; k++) CareerLogic.ApplyDuel(s, prev[k], true, rng);
            // Bölgenin turnuvası kazanılmadan sonraki bölge açılmaz
            if (CareerLogic.PendingTournament(s, regions[i - 1]) is { } t)
            {
                Assert.False(CareerLogic.IsUnlocked(s, regions[i]), regions[i].Name + " turnuvasız açıldı");
                s.Record(t.Id).Wins = 1;
            }
            Assert.True(CareerLogic.IsUnlocked(s, regions[i]), regions[i].Name);
        }
    }

    [Fact]
    public void Tournament_locks_the_deck_and_withdrawing_counts_as_elimination()
    {
        var s = CareerLogic.NewSave();
        var t = CareerWorld.Opponent("passiflora");
        s.Crowns = 500;
        var before = s.ActiveDeck()!.CardIds.ToList();
        Assert.Null(CareerLogic.EnterTournament(s, t, new Random(1)));
        s.Decks["northern_realms"] = new CareerDeck(CareerLogic.StarterLeader, new List<string>());   // turnuva içinde deste değişse de
        Assert.Equal(before, s.ActiveDeck()!.CardIds);                                                  // kilitli deste oynanır
        Assert.NotNull(CareerLogic.EnterTournament(s, CareerWorld.Opponent("toussaint_tournament"), new Random(1)));   // ikinci turnuva yok
        var r = CareerLogic.WithdrawTournament(s, t);
        Assert.True(r.TournamentOver && !r.TournamentWon);
        Assert.Null(s.Tournament);
        Assert.Equal(1, s.Record(t.Id).Losses);
        Assert.Equal(500 - t.EntryFee, s.Crowns);
    }

    [Fact]
    public void Tournament_takes_the_fee_advances_round_by_round_and_crowns_the_champion()
    {
        var s = CareerLogic.NewSave();
        var t = CareerWorld.Opponent("passiflora");
        s.Crowns = t.EntryFee - 1;
        Assert.NotNull(CareerLogic.EnterTournament(s, t, new Random(5)));                // ücret yetmiyor
        Assert.Null(s.Tournament);
        s.Crowns = 500;
        Assert.Null(CareerLogic.EnterTournament(s, t, new Random(5)));
        Assert.Equal(500 - t.EntryFee, s.Crowns);
        Assert.Equal(t.Rounds, s.Tournament!.Decks.Count);
        Assert.Equal(NpcDecks.Get(s.Tournament.Decks[0], t.Level).CardIds, CareerLogic.OpponentDeck(s, t).CardIds);

        var draw = CareerLogic.ApplyTournamentMatch(s, t, null, new Random(6));
        Assert.False(draw.TournamentOver);
        Assert.Equal(0, s.Tournament!.Round);
        for (var i = 0; i < t.Rounds - 1; i++) Assert.False(CareerLogic.ApplyTournamentMatch(s, t, true, new Random(6)).TournamentOver);
        var final = CareerLogic.ApplyTournamentMatch(s, t, true, new Random(7));
        Assert.True(final.TournamentWon);
        Assert.Null(s.Tournament);
        Assert.Equal(500 - t.EntryFee + t.Prize, s.Crowns);

        // kaybeden elenir, ücret geri gelmez
        Assert.Null(CareerLogic.EnterTournament(s, t, new Random(8)));
        var lost = CareerLogic.ApplyTournamentMatch(s, t, false, new Random(8));
        Assert.True(lost.TournamentOver && !lost.TournamentWon);
        Assert.Null(s.Tournament);
    }

    [Fact]
    public void Shop_never_sells_heroes_or_unique_rewards_and_buying_costs_the_game_price()
    {
        var s = CareerLogic.NewSave();
        var rewards = CareerWorld.Opponents.SelectMany(o => o.Decks).Select(NpcDecks.Reward).Where(x => x != null).ToHashSet();
        foreach (var r in CareerWorld.Regions)
        {
            var stock = CareerLogic.Stock(s, r).ToList();
            Assert.NotEmpty(stock);
            Assert.DoesNotContain(stock, d => d.Hero || rewards.Contains(d.Id) || d.Token);
        }
        var shop = CareerLogic.Stock(s, CareerWorld.Regions[0]).First(d => d.IsUnit);
        var price = NpcDecks.Price(shop);
        var before = s.Crowns;
        Assert.Null(CareerLogic.Buy(s, shop));
        Assert.Equal(before - price, s.Crowns);
        s.Crowns = 0;
        Assert.NotNull(CareerLogic.Buy(s, CareerLogic.Stock(s, CareerWorld.Regions[0]).First()));
    }
}
