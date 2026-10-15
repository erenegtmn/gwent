using GwentAI;
using GwentCore;

// Kullanım: dotnet run --project src/GwentArena -c Release -- [oyunSayisi=40] [random|ai]
// AI'yı rastgele oyuncuya ya da kendine karşı oynatıp fraksiyon bazında galibiyet oranı verir.
if (args.Length > 1 && args[0] == "replay")
{
    // dotnet run --project src/GwentArena -c Release -- replay "%AppData%\Gwent\last_match.json" [son-kaç-eylem]
    var rd = ReplayData.FromJson(File.ReadAllText(Environment.ExpandEnvironmentVariables(args[1])));
    var shown = 0;
    Console.WriteLine($"{rd.MyFaction}/{rd.MyLeaderId} vs {rd.OppFaction}/{rd.OppLeaderId}  tohum {rd.Seed}  zorluk {rd.Difficulty}  eylem {rd.Actions.Count}");
    var g = rd.Run((i, a, r, game) =>
    {
        Console.WriteLine($"#{i + 1} {a}  {(r.Ok ? "" : "REDDEDİLDİ: " + r.Error)}");
        foreach (var e in game.Events.Skip(shown)) Console.WriteLine($"     · {e.Kind} oyuncu {e.Player} kart {e.CardUid} {e.Detail}");
        shown = game.Events.Count;
        Console.WriteLine($"     tahta: sen {game.PlayerTotal(0)} / rakip {game.PlayerTotal(1)}");
    });
    Console.WriteLine("Oyun sonu durumu: " + g.Phase);
    return;
}

if (args.Length > 0 && args[0] == "trace")
{
    // dotnet run --project src/GwentArena -c Release -- trace nilfgaard monsters 5
    var f0 = args.Length > 1 ? args[1] : "northern_realms";
    var f1 = args.Length > 2 ? args[2] : "monsters";
    var sd = args.Length > 3 ? int.Parse(args[3]) : 1;
    Trace(f0, f1, sd);
    return;
}

if (args.Length > 0 && args[0] == "terrain")
{
    // dotnet run --project src/GwentArena -c Release -- terrain [oyun/eşleşme]
    // Kaer Morhen: her harita için fraksiyon dengesi, oyun uzunluğu, olay sayıları; komutanı kullanan AI'nın üstünlüğü.
    var per = args.Length > 1 ? int.Parse(args[1]) : 10;
    string[] fs = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
    var maps = new List<TerrainMap?> { null };
    maps.AddRange(Battlefields.All);
    Console.WriteLine($"{"harita",-22} " + string.Join(" ", fs.Select(f => f[..6].PadLeft(7))) + "  adım  taşıma  düşen  | komutanlı AI");
    foreach (var map in maps)
    {
        var opts = new GameOptions { Terrain = map, Commanders = map != null };
        var wins = fs.ToDictionary(f => f, _ => 0);
        var plays = fs.ToDictionary(f => f, _ => 0);
        long steps = 0; var moves = 0; var falls = 0; var played = 0;
        var cmdWins = 0; var cmdGames = 0;
        foreach (var a in fs) foreach (var b in fs)
            for (var seed = 1; seed <= per; seed++)
            {
                var sd = seed * 977 + 13;
                var r = GameRunner.Play(new HeuristicAgent(), new HeuristicAgent(seed: 2), DeckValidator.DefaultDeck(a), DeckValidator.DefaultDeck(b), sd, opts);
                played++; steps += r.Steps;
                plays[a]++; plays[b]++;
                if (r.Winner == 0) wins[a]++; else if (r.Winner == 1) wins[b]++;
                moves += r.Game.Events.Count(e => e.Kind == "commander");
                falls += r.Game.Events.Count(e => e.Kind == "bridge_fall");
                if (map == null) continue;
                // Komutanı kullanan AI, kullanmayana karşı (koltuk değişerek)
                var seat = seed % 2;
                var withCmd = new HeuristicAgent();
                var without = new HeuristicAgent(new AiTuning { UseCommander = false }, 2);
                var ag = seat == 0 ? new IAgent[] { withCmd, without } : new IAgent[] { without, withCmd };
                var r2 = GameRunner.Play(ag[0], ag[1], DeckValidator.DefaultDeck(a), DeckValidator.DefaultDeck(b), sd, opts);
                cmdGames++; if (r2.Winner == seat) cmdWins++;
            }
        var name = map?.Name ?? "(klasik)";
        Console.WriteLine($"{name,-22} " + string.Join(" ", fs.Select(f => $"{100.0 * wins[f] / plays[f],6:F0}%")) +
                          $"  {steps / (double)played,4:F0}  {moves / (double)played,6:F1}  {falls / (double)played,5:F2}" +
                          (map == null ? "" : $"  | {100.0 * cmdWins / cmdGames,5:F1}%"));
    }
    return;
}

if (args.Length > 0 && args[0] == "sweep")
{
    // dotnet run --project src/GwentArena -c Release -- sweep [oyun/eşleşme]
    var per = args.Length > 1 ? int.Parse(args[1]) : 30;
    var baseline = new AiTuning();
    var variants = new (string Name, AiTuning T)[]
    {
        ("rel 0.95", baseline with { RelativeConcedeFactor = 0.95 }),
        ("rel 0.75", baseline with { RelativeConcedeFactor = 0.75 }),
        ("concede 0.35", baseline with { ConcedeFactor = 0.35 }),
        ("concede 0.8", baseline with { ConcedeFactor = 0.8 }),
        ("concede 1.2", baseline with { ConcedeFactor = 1.2 }),
        ("passAhead 0.4", baseline with { PassAheadFactor = 0.4 }),
        ("passAhead 1.0", baseline with { PassAheadFactor = 1.0 }),
        ("lambda 0.1", baseline with { Lambda = 0.1 }),
        ("lambda 1.0", baseline with { Lambda = 1.0 }),
        ("avgPts 4", baseline with { AvgCardPoints = 4 }),
        ("avgPts 6.5", baseline with { AvgCardPoints = 6.5 }),
        ("leaderCost 1", baseline with { LeaderCost = 1 }),
        ("leaderCost 8", baseline with { LeaderCost = 8 }),
        ("mulligan 5", baseline with { MulliganBelow = 5 }),
        ("mulligan 0", baseline with { MulliganBelow = 0 }),
    };
    string[] fs = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
    foreach (var (name, t) in variants)
    {
        var w = 0; var tot = 0;
        foreach (var a in fs) foreach (var b in fs)
            for (var seed = 1; seed <= per; seed++)
            {
                var seat = seed % 2;
                var test = new HeuristicAgent(t); var @base = new HeuristicAgent(baseline);
                var agents = seat == 0 ? new IAgent[] { test, @base } : new IAgent[] { @base, test };
                var d0 = DeckValidator.DefaultDeck(seat == 0 ? a : b);
                var d1 = DeckValidator.DefaultDeck(seat == 0 ? b : a);
                var r = GameRunner.Play(agents[0], agents[1], d0, d1, seed * 977 + 13);
                tot++; if (r.Winner == seat) w++;
            }
        Console.WriteLine($"{name,-16} {100.0 * w / tot,5:F1}%  ({w}/{tot})");
    }
    return;
}

var games = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 40;
var mode = args.Length > 1 ? args[1] : "random";
string[] factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

Console.WriteLine($"{games} oyun / eşleşme, rakip: {mode}");
Console.WriteLine($"{"fraksiyon",-18} " + string.Join(" ", factions.Select(f => f[..Math.Min(6, f.Length)].PadLeft(7))) + "   toplam");

var grandWins = 0; var grandGames = 0;
foreach (var a in factions)
{
    var row = new List<string>();
    var wins = 0; var total = 0;
    foreach (var b in factions)
    {
        var w = 0;
        for (var seed = 1; seed <= games; seed++)
        {
            var seat = seed % 2;
            IAgent self = new HeuristicAgent();
            IAgent other = mode switch
            {
                "ai" => new HeuristicAgent(),
                "ab" => new HeuristicAgent(new AiTuning { RelativeConcedeFactor = 0 }), // eski davranış
                _ => new RandomAgent(seed)
            };
            var agents = seat == 0 ? new[] { self, other } : new[] { other, self };
            var d0 = DeckValidator.DefaultDeck(seat == 0 ? a : b);
            var d1 = DeckValidator.DefaultDeck(seat == 0 ? b : a);
            var res = GameRunner.Play(agents[0], agents[1], d0, d1, seed * 977 + 13);
            if (res.Winner == seat) w++;
        }
        wins += w; total += games;
        row.Add($"{100.0 * w / games,6:F0}%");
    }
    grandWins += wins; grandGames += total;
    Console.WriteLine($"{a,-18} " + string.Join(" ", row) + $"   {100.0 * wins / total:F0}%");
}
Console.WriteLine($"genel: {100.0 * grandWins / grandGames:F1}%");

static void Trace(string f0, string f1, int seed)
{
    var g = new Game(DeckValidator.DefaultDeck(f0), DeckValidator.DefaultDeck(f1), seed);
    var ai = new HeuristicAgent();
    string Name(int uid)
    {
        foreach (var p in g.Players)
            foreach (var c in p.Hand.Concat(p.Deck).Concat(p.Grave).Concat(p.Rows.SelectMany(r => r.Cards)))
                if (c.Uid == uid) return c.Def.Name;
        return "#" + uid;
    }
    var lastRound = 0;
    while (g.Phase != Phase.GameOver)
    {
        var p = g.PlayersToAct()[0];
        var a = ai.Decide(g, p);
        var desc = a switch
        {
            PlayCard pc => $"oynar {Name(pc.Card)}{(pc.Row is { } r ? " → " + r : "")}{(pc.Target is { } t ? " (hedef " + Name(t) + ")" : "")}",
            Choose ch => $"seçer {string.Join(", ", ch.Cards.Select(Name))}{(ch.Row is { } r2 ? " → " + r2 : "")}",
            Mulligan m => $"{m.Cards.Count} kart değiştirir",
            _ => a.GetType().Name
        };
        if (g.Round != lastRound && g.Phase == Phase.Playing) { lastRound = g.Round; Console.WriteLine($"{(char)10}=== RAUND {g.Round} (mücevher {g.Players[0].Gems}-{g.Players[1].Gems}) ==="); }
        var res = g.Apply(a);
        if (!res.Ok) throw new Exception(res.Error);
        if (a is Mulligan) continue;
        Console.WriteLine($"  P{p} {desc,-60} [{g.PlayerTotal(0),3} - {g.PlayerTotal(1),3}]");
    }
    foreach (var r in g.History) Console.WriteLine($"raund {r.Round}: {r.Score0}-{r.Score1} kazanan: {(r.Winner?.ToString() ?? "-")}");
    Console.WriteLine($"OYUN: kazanan {(g.Outcome!.Winner?.ToString() ?? "beraberlik")}");
}
