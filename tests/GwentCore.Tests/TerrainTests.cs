using GwentAI;
using GwentCore;
using Xunit;
using static GwentCore.Terrain;

namespace GwentCore.Tests;

/// <summary>Kaer Morhen: arazi kuralları, komutan figürü, harita programı ve arazili oyunların sonuna kadar oynanması.</summary>
public class TerrainTests
{
    private static GameOptions Opts(Terrain close = Plain, Terrain ranged = Plain, Terrain siege = Plain, bool commanders = false) =>
        new() { Terrain = TerrainMap.Symmetric("test", "Test", close, ranged, siege), Commanders = commanders };

    private static Game New(GameOptions o, string f1 = "monsters") => T.New(options: o, f1: f1);

    [Fact]
    public void Hill_gives_plus_one_unless_weather()
    {
        var g = New(Opts(close: Hill));
        var u = g.TestBoard(0, "siegfried_of_denesle", RowKind.Close);
        var hero = g.TestBoard(0, "john_natalis", RowKind.Close);
        Assert.Equal(6, g.CardScore(u));
        Assert.Equal(10, g.CardScore(hero)); // kahraman araziden etkilenmez
        g.TestWeather(Ids.BitingFrost);
        Assert.Equal(1, g.CardScore(u));
    }

    [Fact]
    public void Swamp_gives_minus_one_but_never_below_one()
    {
        var g = New(Opts(close: Swamp));
        Assert.Equal(4, g.CardScore(g.TestBoard(0, "siegfried_of_denesle", RowKind.Close)));
        Assert.Equal(1, g.CardScore(g.TestBoard(0, "redanian_foot_soldier", RowKind.Close)));
    }

    [Fact]
    public void Hill_bonus_is_doubled_by_horn_but_added_before_it()
    {
        var g = New(Opts(siege: Hill));
        var a = g.TestBoard(0, "ballista", RowKind.Siege);
        g.TestSpecial(0, Ids.Horn, RowKind.Siege);
        Assert.Equal(14, g.CardScore(a)); // (6 + 1) × 2
    }

    [Fact]
    public void Forest_units_cannot_be_scorched()
    {
        var g = New(Opts(close: Forest));
        T.Filler(g, 0); T.Filler(g, 1);
        var strong = g.TestBoard(1, "vampire_katakan", RowKind.Close);   // 5, ormanda
        var other = g.TestBoard(1, "arachas_behemoth", RowKind.Siege);  // kuşatmada, ormanda değil
        var scorch = g.TestHand(0, Ids.Scorch);
        T.Play(g, 0, scorch);
        Assert.Contains(strong, g.Players[1].Row(RowKind.Close).Cards);
        Assert.DoesNotContain(other, g.Players[1].Row(RowKind.Siege).Cards);
    }

    [Fact]
    public void Forest_blocks_row_scorch()
    {
        var g = New(Opts(close: Forest));
        T.Filler(g, 0); T.Filler(g, 1);
        g.TestBoard(1, "vampire_katakan", RowKind.Close);
        g.TestBoard(1, "vampire_katakan", RowKind.Close);
        var v = g.TestHand(0, "villentretenmerth");
        T.Play(g, 0, v);
        Assert.Equal(2, g.Players[1].Row(RowKind.Close).Cards.Count);
        Assert.DoesNotContain(g.Events, e => e.Kind == "kill");
    }

    [Fact]
    public void Bridge_holds_four_units_and_rejects_the_fifth_from_hand()
    {
        var g = New(Opts(close: Bridge));
        for (var i = 0; i < 4; i++) g.TestBoard(0, "redanian_foot_soldier", RowKind.Close);
        var fifth = g.TestHand(0, "siegfried_of_denesle");
        T.Filler(g, 0);
        T.Fails(g, new PlayCard(0, fifth.Uid), "Köprü");
        Assert.DoesNotContain(g.LegalActions(), a => a is PlayCard pc && pc.Card == fifth.Uid);
        // Diğer satırlar serbest
        var siege = g.TestHand(0, "ballista");
        Assert.Contains(g.LegalActions(), a => a is PlayCard pc && pc.Card == siege.Uid);
    }

    [Fact]
    public void Bridge_overflow_from_muster_falls_to_the_grave()
    {
        var g = T.New("monsters", "northern_realms", options: Opts(close: Bridge));
        T.Filler(g, 1);
        g.TestHand(0, "redanian_foot_soldier");
        for (var i = 0; i < 2; i++) g.TestBoard(0, "redanian_foot_soldier", RowKind.Close);
        var v = g.TestHand(0, "vampire_katakan");
        g.TestDeck(0, "vampire_bruxa");
        g.TestDeck(0, "vampire_ekimmara");
        g.TestDeck(0, "vampire_fleder");
        T.Play(g, 0, v);
        Assert.Equal(Game.BridgeCapacity, g.Players[0].Row(RowKind.Close).Cards.Count);
        Assert.Equal(2, g.Events.Count(e => e.Kind == "bridge_fall"));
        Assert.Equal(2, g.Players[0].Grave.Count);
    }

    [Fact]
    public void Ruins_medic_revives_twice_before_the_turn_ends()
    {
        var g = New(Opts(siege: Ruins));
        T.Filler(g, 1);
        var a = g.TestGrave(0, "ballista");
        var b = g.TestGrave(0, "siege_tower");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);
        T.Play(g, 0, medic);
        Assert.Equal(ChoiceKind.Medic, g.Pending!.Kind);
        T.Ok(g, new Choose(0, new[] { a.Uid }));
        Assert.Equal(ChoiceKind.Medic, g.Pending!.Kind); // ikinci canlandırma
        Assert.Equal(0, g.Pending.Player);
        T.Ok(g, new Choose(0, new[] { b.Uid }));
        Assert.Null(g.Pending);
        Assert.Equal(1, g.Current); // tur ancak şimdi geçti
        Assert.Equal(3, g.Players[0].Row(RowKind.Siege).Cards.Count);
    }

    [Fact]
    public void Medic_outside_ruins_revives_once()
    {
        var g = New(Opts(close: Ruins));
        T.Filler(g, 1);
        var a = g.TestGrave(0, "ballista");
        g.TestGrave(0, "siege_tower");
        var medic = g.TestHand(0, "dun_banner_medic");
        T.Filler(g, 0);
        T.Play(g, 0, medic);
        T.Ok(g, new Choose(0, new[] { a.Uid }));
        Assert.Null(g.Pending);
        Assert.Equal(1, g.Current);
    }

    [Fact]
    public void Commander_shields_its_row_from_weather()
    {
        var g = New(Opts(commanders: true));
        var close = g.TestBoard(0, "siegfried_of_denesle", RowKind.Close);
        var siege = g.TestBoard(0, "ballista", RowKind.Siege);
        var oppClose = g.TestBoard(1, "vampire_katakan", RowKind.Close);
        g.TestWeather(Ids.BitingFrost);
        g.TestWeather(Ids.TorrentialRain);
        Assert.Equal(RowKind.Close, g.Players[0].Commander);
        Assert.Equal(6, g.CardScore(close));    // komutan yakın satırda: havadan korunur, Kuzey'in Disiplini +1
        Assert.Equal(1, g.CardScore(siege));
        Assert.Equal(5, g.CardScore(oppClose)); // rakibin komutanı da yakın satırda
    }

    [Fact]
    public void Northern_realms_and_nilfgaard_commanders_give_discipline()
    {
        var g = T.New("northern_realms", "monsters", options: Opts(commanders: true));
        Assert.True(g.HasDiscipline(0));
        Assert.False(g.HasDiscipline(1));
        Assert.Equal(6, g.CardScore(g.TestBoard(0, "siegfried_of_denesle", RowKind.Close)));   // 5 + 1
        Assert.Equal(6, g.CardScore(g.TestBoard(0, "ballista", RowKind.Siege)));               // komutan yok
        Assert.Equal(10, g.CardScore(g.TestBoard(0, "john_natalis", RowKind.Close)));           // kahraman etkilenmez
        Assert.Equal(5, g.CardScore(g.TestBoard(1, "vampire_katakan", RowKind.Close)));         // Canavarlarda yok
        Assert.True(T.New("nilfgaard", "monsters", options: Opts(commanders: true)).HasDiscipline(0));
        Assert.False(T.New("northern_realms", "monsters").HasDiscipline(0));                     // komutansız klasik
    }

    [Fact]
    public void Planning_ai_moves_the_commander_to_its_main_row()
    {
        var g = T.New("northern_realms", "monsters", options: Opts(commanders: true));
        T.Filler(g, 1);
        for (var i = 0; i < 3; i++) g.TestBoard(0, "catapult", RowKind.Siege);
        g.TestHand(0, "ballista");
        g.TestBoard(1, "vampire_katakan", RowKind.Close);   // rakip önde değil, ben öndeyim
        var a = new HeuristicAgent().Decide(g, 0);
        Assert.Equal(new MoveCommander(0, RowKind.Siege), a);
        var off = new HeuristicAgent(new AiTuning { CommanderPlanGain = 0 }).Decide(g, 0);
        Assert.IsNotType<MoveCommander>(off);
    }

    [Fact]
    public void Moving_the_commander_takes_the_turn_once_per_round()
    {
        var g = New(Opts(commanders: true));
        T.Filler(g, 0); T.Filler(g, 1);
        T.Fails(g, new MoveCommander(0, RowKind.Close), "zaten");
        T.Ok(g, new MoveCommander(0, RowKind.Siege));
        Assert.Equal(RowKind.Siege, g.Players[0].Commander);
        Assert.Equal(1, g.Current);
        T.Pass(g, 1);
        T.Fails(g, new MoveCommander(0, RowKind.Ranged), "bu raund");
        Assert.DoesNotContain(g.LegalActions(), a => a is MoveCommander);
        T.Pass(g, 0);
        // yeni raund: yeniden taşınabilir, figür yerinde kalır
        Assert.Equal(2, g.Round);
        Assert.Equal(RowKind.Siege, g.Players[0].Commander);
        Assert.Contains(g.LegalActions(), a => a is MoveCommander);
    }

    [Fact]
    public void Without_commanders_there_is_no_commander()
    {
        var g = New(Opts());
        T.Filler(g, 0);
        Assert.Null(g.Players[0].Commander);
        T.Fails(g, new MoveCommander(0, RowKind.Siege), "komutan");
        Assert.DoesNotContain(g.LegalActions(), a => a is MoveCommander);
    }

    [Fact]
    public void Terrain_follows_the_round_program()
    {
        var map = Battlefields.Get("pontar_bridge")!;
        var g = New(new GameOptions { Terrain = map });
        Assert.Equal(Bridge, g.TerrainAt(0, RowKind.Close));
        Assert.Equal(Bridge, map.At(1, RowKind.Close, 2));
        Assert.Equal(Swamp, map.At(1, RowKind.Close, 3));
        Assert.Equal(Swamp, map.At(0, RowKind.Close, 9)); // listede olmayan raund: son raund
        Assert.Contains(g.Events, e => e.Kind == "terrain");
    }

    [Fact]
    public void Clone_keeps_commander_state()
    {
        var g = New(Opts(commanders: true));
        T.Filler(g, 0); T.Filler(g, 1);
        T.Ok(g, new MoveCommander(0, RowKind.Ranged));
        var c = g.Clone();
        Assert.Equal(RowKind.Ranged, c.Players[0].Commander);
        Assert.True(c.Players[0].CommanderMoved);
        Assert.Same(g.Options.Terrain, c.Options.Terrain);
    }

    [Fact]
    public void Commander_moves_survive_a_replay()
    {
        var replay = new ReplayData
        {
            MyFaction = "northern_realms", MyLeaderId = CardCatalog.Default.Leaders("northern_realms").First().Id,
            OppFaction = "monsters", OppLeaderId = CardCatalog.Default.Leaders("monsters").First().Id,
            Seed = 5, Battlefield = "crookback_bog", Commanders = true
        };
        var g = replay.CreateGame();
        Assert.Equal("crookback_bog", g.Options.Terrain!.Id);
        var agents = new IAgent[] { new HeuristicAgent(), new HeuristicAgent(seed: 2) };
        while (g.Phase != Phase.GameOver)
        {
            var p = g.PlayersToAct()[0];
            var a = agents[p].Decide(g, p);
            replay.Actions.Add(ReplayData.Encode(a));
            Assert.True(g.Apply(a).Ok);
        }
        var again = ReplayData.FromJson(replay.ToJson()).Run();
        Assert.Equal(g.Outcome, again.Outcome);
        Assert.Equal(g.History, again.History);
    }

    public static IEnumerable<object[]> Maps() => Battlefields.All.Select(m => new object[] { m.Id });

    /// <summary>Rastgele eylemlerle arazili + komutanlı oyunlar: bitmeli, kart kaybolmamalı/çoğalmamalı.</summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void Random_terrain_games_finish_and_keep_cards(string mapId)
    {
        string[] f = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("GWENT_FUZZ_SEEDS"), out var n) ? n : 12;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var opts = new GameOptions { Terrain = Battlefields.Get(mapId), Commanders = seed % 3 != 0 };
            var f0 = f[seed % 5];
            var f1 = f[(seed * 3 + 1) % 5];
            var g = new Game(DeckValidator.DefaultDeck(f0, opts), DeckValidator.DefaultDeck(f1, opts), seed, opts);
            var rng = new Random(seed);
            var initial = Count(g);
            for (var step = 0; g.Phase != Phase.GameOver; step++)
            {
                Assert.True(step < 4000, $"oyun bitmedi ({mapId}, {seed})");
                var legal = g.LegalActions().ToList();
                var nonPass = legal.Where(a => a is not Pass).ToList();
                var pick = nonPass.Count > 0 && rng.NextDouble() > 0.06 ? nonPass[rng.Next(nonPass.Count)] : legal[rng.Next(legal.Count)];
                var r = g.Apply(pick);
                Assert.True(r.Ok, $"{pick} reddedildi: {r.Error} ({mapId}, {seed})");
                var transforms = g.Events.Count(e => e.Kind == "transform");
                var limbo = g.Pending?.Kind == ChoiceKind.AgileRow ? 1 : 0;
                Assert.Equal(initial - transforms, Count(g) + limbo);
                foreach (var p in g.Players)
                    foreach (var rk in Enum.GetValues<RowKind>())
                        if (g.TerrainAt(p.Index, rk) == Bridge)
                            Assert.True(p.Row(rk).Cards.Count(c => c.Def.IsUnit) <= Game.BridgeCapacity + Hold(g, p, rk));
            }
        }
    }

    /// <summary>Köprü sınırı yalnız yeni yerleştirmeye uygulanır: önceki raunddan kalan (Monsters) ya da dönüşen birimler sayılmaz.</summary>
    private static int Hold(Game g, PlayerState p, RowKind rk) => p.Row(rk).Cards.Count(c => c.NoRemove) + g.Events.Count(e => e.Kind == "transform");

    private static int Count(Game g)
    {
        var n = 0;
        foreach (var p in g.Players)
        {
            n += p.Deck.Count + p.Hand.Count + p.Grave.Count;
            foreach (var r in p.Rows) n += r.Cards.Count(c => !c.Def.Token && !c.Ephemeral) + (r.Special is { Ephemeral: false } ? 1 : 0);
        }
        return n + g.WeatherCards.Count(c => !c.Ephemeral);
    }

    [Fact]
    public void Ai_plays_terrain_games_to_the_end()
    {
        foreach (var map in Battlefields.All)
        {
            var opts = new GameOptions { Terrain = map, Commanders = true };
            var g = new Game(DeckValidator.RecommendedDeck("northern_realms"), DeckValidator.RecommendedDeck("monsters"), 3, opts);
            var agents = new IAgent[] { new HeuristicAgent(), AgentFactory.Create(Difficulty.Normal, 3) };
            for (var step = 0; g.Phase != Phase.GameOver; step++)
            {
                Assert.True(step < 4000);
                var p = g.PlayersToAct()[0];
                Assert.True(g.Apply(agents[p].Decide(g, p)).Ok);
            }
        }
    }
}
