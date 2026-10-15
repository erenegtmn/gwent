using GwentAI;
using GwentCore;
using KaerMorhen.Core;
using Xunit;

namespace GwentCore.Tests;

/// <summary>Kaer Morhen kalesi: binalar, bedeller, demirci/atölye sınırları, sözleşme panosu, ödüller ve kaledeki maçlar.</summary>
public class KeepTests
{
    [Fact]
    public void New_keep_starts_with_war_room_and_two_contracts()
    {
        var s = Keep.NewSave(1);
        Assert.Equal(Keep.StartCrowns, s.Crowns);
        Assert.Equal(1, s.Level(Keep.WarRoom));
        Assert.Equal(0, s.Level(Keep.Forge));
        Assert.Equal(2, s.Board.Count);
        Assert.All(s.Board, c => Assert.NotEqual(Difficulty.Hard, c.Difficulty));
        Assert.Equal(s.Board.Count, s.Board.Select(c => c.Title).Distinct().Count());
    }

    [Fact]
    public void Same_seed_gives_same_board()
    {
        Assert.Equal(Keep.NewSave(7).Board, Keep.NewSave(7).Board);
        Assert.NotEqual(Keep.NewSave(7).Board, Keep.NewSave(8).Board);
    }

    [Fact]
    public void Upgrade_costs_crowns_and_mutagens()
    {
        var s = Keep.NewSave(1);
        Assert.True(Keep.Upgrade(s, Keep.Forge));                // 80
        Assert.Equal(20, s.Crowns);
        Assert.Equal(1, s.Level(Keep.Forge));
        Assert.Contains("Crown eksik", Keep.UpgradeBlocked(s, Keep.Forge));
        s.Crowns = 1000;
        Assert.Contains("mutajen eksik", Keep.UpgradeBlocked(s, Keep.Forge));
        s.Red = 2;
        Assert.True(Keep.Upgrade(s, Keep.Forge));
        Assert.Equal(0, s.Red);
        Assert.Equal(800, s.Crowns);
        Assert.False(Keep.Upgrade(s, Keep.Alchemy));            // planlanmış bina
    }

    [Fact]
    public void Forge_level_limits_runes()
    {
        var s = Keep.NewSave(1);
        var runes = RuneIds.All.Select((r, i) => new CardRune("c" + i, r)).ToList();
        Assert.Empty(Keep.LimitRunes(s, runes));
        s.Buildings[Keep.Forge] = 1;
        Assert.Equal(new[] { RuneIds.Svarog, RuneIds.Perun }, Keep.AllowedRunes(s));
        Assert.Single(Keep.LimitRunes(s, runes));
        s.Buildings[Keep.Forge] = 3;
        Assert.Equal(RuneIds.All, Keep.AllowedRunes(s));
        Assert.Equal(3, Keep.LimitRunes(s, runes).Count);
    }

    [Fact]
    public void Workshop_pieces_change_the_battlefield()
    {
        var s = Keep.NewSave(1);
        Assert.Empty(Keep.AvailablePieces(s));
        s.Buildings[Keep.Workshop] = 3;
        var pieces = Keep.ToTerrain(s, new[] { ("tower", RowKind.Siege), ("barricade", RowKind.Close), ("moat", RowKind.Ranged) });
        var map = Battlefields.WithPieces(Battlefields.Get("pontar_bridge"), pieces)!;
        for (var r = 1; r <= 3; r++)
        {
            Assert.Equal(Terrain.Hill, map.At(0, RowKind.Siege, r));
            Assert.Equal(Terrain.Bridge, map.At(1, RowKind.Close, r));
            Assert.Equal(Terrain.Swamp, map.At(1, RowKind.Ranged, r));
        }
        Assert.Equal(Terrain.Swamp, map.At(0, RowKind.Close, 3));   // haritanın kendi programı sürer
        s.Buildings[Keep.Workshop] = 1;
        Assert.Single(Keep.ToTerrain(s, new[] { ("tower", RowKind.Siege), ("barricade", RowKind.Close) }));
    }

    [Fact]
    public void Winning_pays_and_replaces_the_contract_losing_keeps_it()
    {
        var s = Keep.NewSave(3);
        var c = s.Board[0];
        var lost = Keep.Complete(s, c.Id, false);
        Assert.False(lost.Won);
        Assert.Equal(Keep.StartCrowns + Keep.LossCrowns, s.Crowns);
        Assert.Contains(c, s.Board);
        var won = Keep.Complete(s, c.Id, true);
        Assert.True(won.Won);
        Assert.Equal(Keep.StartCrowns + Keep.LossCrowns + c.RewardCrowns, s.Crowns);
        Assert.Equal(c.RewardMutagens, s.Mutagens(c.RewardColor));
        Assert.DoesNotContain(c, s.Board);
        Assert.Equal(Keep.BoardSize(s), s.Board.Count);
        Assert.Equal((1, 1), (s.Wins, s.Losses));
    }

    [Fact]
    public void War_room_grows_the_board_and_unlocks_hard_and_cursed_contracts()
    {
        var s = Keep.NewSave(5);
        s.Crowns = 10000; s.Blue = 100;
        Assert.True(Keep.Upgrade(s, Keep.WarRoom));
        Assert.Equal(3, s.Board.Count);
        Assert.True(Keep.Upgrade(s, Keep.WarRoom));
        Assert.Equal(4, s.Board.Count);
        // Yeterince sözleşme üret: zor ve lanetli olanlar gelmeli
        var seen = new List<Contract>();
        for (var i = 0; i < 60; i++)
        {
            var c = s.Board[0];
            seen.Add(c);
            Keep.Complete(s, c.Id, true);
        }
        Assert.Contains(seen, c => c.Difficulty == Difficulty.Hard);
        Assert.Contains(seen, c => c.Curse != null);
        Assert.All(seen.Where(c => c.Curse != null), c => Assert.NotEqual(Curses.Mirror, c.Curse));
    }

    [Fact]
    public void A_contract_match_with_runes_and_pieces_plays_to_the_end()
    {
        var s = Keep.NewSave(11);
        s.Buildings[Keep.Forge] = 3;
        s.Buildings[Keep.Workshop] = 3;
        var c = s.Board[0];
        var deck = DeckValidator.RecommendedDeck("northern_realms");
        var replay = new ReplayData
        {
            MyFaction = deck.Faction, MyLeaderId = deck.LeaderId, MyDeckCards = deck.CardIds.ToList(),
            MyRunes = Keep.LimitRunes(s, Forge.AutoRunes(deck, 3)),
            OppFaction = c.Faction, OppLeaderId = c.LeaderId, Difficulty = (int)c.Difficulty, OppAutoRunes = Forge.AiRuneCount(c.Difficulty),
            Seed = c.Seed, Battlefield = c.Battlefield, Commanders = true, Curses = c.Curse == null ? null : new List<string> { c.Curse },
            Pieces = Keep.ToTerrain(s, new[] { ("tower", RowKind.Siege), ("moat", RowKind.Close) })
        };
        var g = replay.CreateGame();
        Assert.Equal(Terrain.Hill, g.TerrainAt(0, RowKind.Siege));
        Assert.Equal(Terrain.Swamp, g.TerrainAt(1, RowKind.Close));
        var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(c.Difficulty, 1) };
        for (var step = 0; g.Phase != Phase.GameOver; step++)
        {
            Assert.True(step < 4000);
            var p = g.PlayersToAct()[0];
            Assert.True(g.Apply(agents[p].Decide(g, p)).Ok);
        }
        Keep.Complete(s, c.Id, g.Outcome!.Winner == 0);
    }
}
