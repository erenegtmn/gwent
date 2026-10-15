using GwentAI;
using GwentCore;
using KaerMorhen.Core;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Kaer Morhen: müttefikler (motor + kale) ve Wild Hunt kuşatması.</summary>
public class AllySiegeTests
{
    [Fact]
    public void Allies_start_in_hand_on_top_of_the_starting_hand()
    {
        var deck = DeckValidator.RecommendedDeck("skellige") with { Allies = new[] { "zoltan_chivay", "triss_merigold" } };
        Assert.Empty(DeckValidator.Validate(deck));
        var g = new Game(deck, DeckValidator.RecommendedDeck("monsters"), 4);
        Assert.Equal(Game.StartingHand + 2, g.Players[0].Hand.Count);
        Assert.Contains(g.Players[0].Hand, c => c.Def.Id == "triss_merigold");
        Assert.Equal(Game.StartingHand, g.Players[1].Hand.Count);
    }

    [Fact]
    public void Invalid_allies_are_rejected()
    {
        var deck = DeckValidator.RecommendedDeck("skellige");
        Assert.NotEmpty(DeckValidator.Validate(deck with { Allies = new[] { "vampire_katakan" } }));          // fraksiyon kartı
        Assert.NotEmpty(DeckValidator.Validate(deck with { Allies = new[] { "scorch" } }));                   // birim değil
        Assert.NotEmpty(DeckValidator.Validate(deck with { Allies = new[] { "vesemir", "vesemir" } }));
        Assert.NotEmpty(DeckValidator.Validate(deck with { Allies = new[] { "vesemir", "dandelion", "zoltan_chivay", "triss_merigold" } }));
    }

    [Fact]
    public void Allies_need_the_hall_and_respect_its_slots()
    {
        var s = Keep.NewSave(1);
        s.Crowns = 5000; s.Red = s.Green = s.Blue = 50;
        Assert.False(Keep.Recruit(s, "dandelion"));
        Assert.True(Keep.Upgrade(s, Keep.AlliesHall));
        Assert.True(Keep.Recruit(s, "dandelion"));
        Assert.True(Keep.Recruit(s, "vesemir"));
        Assert.False(Keep.Recruit(s, "vesemir"));
        Assert.Equal(new[] { "dandelion" }, Keep.MatchAllies(s));                 // 1 yuva
        Assert.False(Keep.ToggleActive(s, "vesemir"));                            // yuva dolu
        Assert.True(Keep.ToggleActive(s, "dandelion"));
        Assert.True(Keep.ToggleActive(s, "vesemir"));
        Assert.Equal(new[] { "vesemir" }, Keep.MatchAllies(s));
        Assert.True(Keep.Upgrade(s, Keep.AlliesHall));
        Assert.True(Keep.ToggleActive(s, "dandelion"));
        Assert.Equal(2, Keep.MatchAllies(s).Count);
    }

    private static void WinContracts(KeepSave s, int n)
    {
        for (var i = 0; i < n; i++) Keep.Complete(s, s.Board[0].Id, true);
    }

    [Fact]
    public void Siege_comes_after_five_contracts_and_has_three_waves()
    {
        var s = Keep.NewSave(2);
        WinContracts(s, Keep.SiegeEvery - 1);
        Assert.False(Keep.SiegeReady(s));
        WinContracts(s, 1);
        Assert.True(Keep.SiegeReady(s));
        Assert.Null(Keep.SiegeContract(s));
        Keep.StartSiege(s);
        var crowns = s.Crowns;
        for (var w = 1; w <= Keep.SiegeWaves; w++)
        {
            var c = Keep.SiegeContract(s)!;
            Assert.Equal(-w, c.Id);
            Assert.Equal("monsters", c.Faction);
            Assert.True(Keep.Complete(s, c.Id, true).Won);
        }
        Assert.Equal(1, s.SiegesWon);
        Assert.Equal(1, s.Chapter);
        Assert.Equal("northern_realms", Keep.CurrentAntagonist(s).Faction);   // sıradaki düşman
        Assert.Equal(0, s.SiegeWave);
        Assert.Equal(0, s.SiegeProgress);
        Assert.True(s.Crowns > crowns + 200);
        Assert.False(Keep.SiegeReady(s));
    }

    [Fact]
    public void Losing_a_wave_ends_the_run_and_the_keep_starts_over()
    {
        var s = Keep.NewSave(3);
        s.Buildings[Keep.Forge] = 2;
        WinContracts(s, Keep.SiegeEvery);
        Keep.StartSiege(s);
        Keep.Complete(s, -1, true);
        var r = Keep.Complete(s, -2, false);
        Assert.False(r.Won);
        Assert.Equal(0, s.SiegeWave);
        Assert.Equal(0, s.Chapter);
        Assert.Equal(0, s.Level(Keep.Forge));      // kale düştü: her şey baştan
        Assert.Equal(1, s.Level(Keep.WarRoom));
        Assert.Equal(Keep.StartCrowns, s.Crowns);
        Assert.Equal(2, s.Run);
        var rec = Assert.Single(s.Runs);
        Assert.Equal((1, 0, Keep.SiegeEvery), (rec.Run, rec.Chapter, rec.Contracts));
        Assert.Equal(Keep.BoardSize(s), s.Board.Count);
        Assert.False(Keep.ContractsLocked(s));
        Assert.Throws<InvalidOperationException>(() => Keep.Complete(s, -3, true));
    }

    [Fact]
    public void A_siege_wave_with_allies_plays_to_the_end()
    {
        var s = Keep.NewSave(9);
        WinContracts(s, Keep.SiegeEvery);
        Keep.StartSiege(s);
        s.SiegeWave = 3;
        var c = Keep.SiegeContract(s)!;
        var deck = DeckValidator.RecommendedDeck("northern_realms");
        var replay = new ReplayData
        {
            MyFaction = deck.Faction, MyLeaderId = deck.LeaderId, MyDeckCards = deck.CardIds.ToList(), MyAllies = new List<string> { "yennefer_of_vengerberg", "dandelion" },
            OppFaction = c.Faction, OppLeaderId = c.LeaderId, Difficulty = (int)c.Difficulty, OppAutoRunes = 3,
            Seed = c.Seed, Battlefield = c.Battlefield, Commanders = true, Curses = c.Curses?.ToList(), OppPerks = c.OppPerks
        };
        var g = replay.CreateGame();
        Assert.Equal(Game.StartingHand + 2, g.Players[0].Hand.Count);
        Assert.Equal(Game.StartingHand + (c.OppPerks?.ExtraHand ?? 0), g.Players[1].Hand.Count);
        var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(c.Difficulty, 1) };
        for (var step = 0; g.Phase != Phase.GameOver; step++)
        {
            Assert.True(step < 4000);
            var p = g.PlayersToAct()[0];
            Assert.True(g.Apply(agents[p].Decide(g, p)).Ok);
        }
        Keep.Complete(s, c.Id, g.Outcome!.Winner == 0);
        Assert.Equal(0, s.SiegeWave);
    }

    private static void BeatSiege(KeepSave s)
    {
        for (var i = 0; i < Keep.SiegeEvery; i++) Keep.Complete(s, s.Board[0].Id, true);
        Assert.True(Keep.ContractsLocked(s));
        Keep.StartSiege(s);
        Assert.True(Keep.ContractsLocked(s));
        for (var w = 1; w <= Keep.SiegeWaves; w++) Keep.Complete(s, -w, true);
        Assert.False(Keep.ContractsLocked(s));
    }

    [Fact]
    public void The_chronicle_has_five_foes_then_endless_white_frost()
    {
        var s = Keep.NewSave(5);
        var foes = new List<string>();
        for (var i = 0; i < Keep.Chronicle.Count; i++)
        {
            foes.Add(Keep.CurrentAntagonist(s).Name);
            BeatSiege(s);
        }
        Assert.Equal(Keep.Chronicle.Select(a => a.Name), foes);
        Assert.Equal(5, foes.Distinct().Count());
        Assert.True(Keep.Endless(s));
        // Sonsuz: düşmanlar başa döner ama her dalga Zor, rakip fazladan kart ve iksirle
        BeatSiege(s);
        for (var i = 0; i < Keep.SiegeEvery; i++) Keep.Complete(s, s.Board[0].Id, true);
        Keep.StartSiege(s);
        var c = Keep.SiegeContract(s)!;
        Assert.Equal(Difficulty.Hard, c.Difficulty);
        Assert.Equal(1, c.OppPerks!.ExtraHand);
        Assert.Contains(PotionIds.Thunderbolt, c.OppPerks.Potions!);
    }

    [Fact]
    public void Later_chapters_raise_contract_difficulty()
    {
        var s = Keep.NewSave(8);
        s.Buildings[Keep.WarRoom] = 2;
        Keep.RefillBoard(s);
        int Tier()
        {
            var sum = 0;
            for (var i = 0; i < 40; i++) { var c = s.Board[0]; sum += (int)c.Difficulty; Keep.Complete(s, c.Id, true); s.SiegeProgress = 0; }
            return sum;
        }
        var early = Tier();
        s.Chapter = 2;
        var late = Tier();
        Assert.True(late > early, $"{early} -> {late}");
        s.Chapter = 2;
        Assert.All(Enumerable.Range(0, 20).Select(_ => { var c = s.Board[0]; Keep.Complete(s, c.Id, true); s.SiegeProgress = 0; return c; }),
            c => Assert.Equal(Difficulty.Hard, c.Difficulty));
    }

    [Fact]
    public void Gaunter_odimm_plays_your_own_deck()
    {
        var s = Keep.NewSave(6);
        s.Chapter = 3;
        for (var i = 0; i < Keep.SiegeEvery; i++) Keep.Complete(s, s.Board[0].Id, true);
        Keep.StartSiege(s);
        var c = Keep.SiegeContract(s)!;
        Assert.Equal("mirror", c.Faction);
        Assert.Contains(Curses.Mirror, c.Curses!);
        var deck = DeckValidator.RecommendedDeck("skellige");
        var replay = new ReplayData
        {
            MyFaction = deck.Faction, MyLeaderId = deck.LeaderId, MyDeckCards = deck.CardIds.ToList(),
            OppFaction = deck.Faction, OppLeaderId = deck.LeaderId, Difficulty = (int)c.Difficulty, OppAutoRunes = 3,
            Seed = c.Seed, Battlefield = c.Battlefield, Commanders = true, Curses = c.Curses!.ToList(), OppPerks = c.OppPerks
        };
        var g = replay.CreateGame();
        Assert.Equal(g.Players[0].Leader, g.Players[1].Leader);
        Assert.Equal(g.Players[0].Deck.Concat(g.Players[0].Hand).Select(x => x.Def.Id).OrderBy(x => x),
                     g.Players[1].Deck.Concat(g.Players[1].Hand).Select(x => x.Def.Id).OrderBy(x => x));
    }
}
