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
    // terrain [oyun] [plan|nocmd]: plan = planlı komutan, plansıza karşı; nocmd = komutansız (yalnız arazinin etkisi)
    var noCmd = args.Length > 2 && args[2] == "nocmd";
    if (Environment.GetEnvironmentVariable("GWENT_NR_BONUS") is { } nrb) Game.CommanderDiscipline = int.Parse(nrb);
    if (Environment.GetEnvironmentVariable("GWENT_CMD_FACTIONS") is { } cf) Game.DisciplinedFactions = cf.Split(',').ToHashSet();
    var maps = new List<TerrainMap?> { null, TerrainMap.Symmetric("plain", "Düzlük", Terrain.Plain, Terrain.Plain, Terrain.Plain) };
    maps.AddRange(Battlefields.All);
    Console.WriteLine($"{"harita",-22} " + string.Join(" ", fs.Select(f => f[..6].PadLeft(7))) + "  adım  taşıma  düşen  | komutanlı AI");
    foreach (var map in maps)
    {
        var opts = new GameOptions { Terrain = map, Commanders = map != null && !noCmd };
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
                if (map == null || noCmd) continue;
                // Komutanı kullanan AI, kullanmayana karşı (koltuk değişerek)
                var seat = seed % 2;
                var withCmd = new HeuristicAgent();
                var without = new HeuristicAgent(args.Length > 2 && args[2] == "plan" ? new AiTuning { CommanderPlanGain = 0 } : new AiTuning { UseCommander = false }, 2);
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

if (args.Length > 0 && args[0] == "runes")
{
    // dotnet run --project src/GwentArena -c Release -- runes [oyun/eşleşme]
    // Kaer Morhen: 3 rünlü deste, rünsüze karşı (aynı AI, rastgele harita, koltuk değişerek); rün başına etki ayrıca.
    var per = args.Length > 1 ? int.Parse(args[1]) : 6;
    string[] fs = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
    var variants = new List<(string Name, Func<Deck, IReadOnlyList<CardRune>> Runes)> { ("otomatik (Svarog/Perun/Veles)", d => Forge.AutoRunes(d, 3)) };
    foreach (var r in RuneIds.All) variants.Add(($"3× {r}", d => Forge.AutoRunes(d, 3).Select(x => x with { Rune = r }).ToList()));
    foreach (var (name, runes) in variants)
    {
        var w = 0; var tot = 0;
        foreach (var a in fs) foreach (var b in fs)
            for (var seed = 1; seed <= per; seed++)
            {
                var seat = seed % 2;
                var opts = new GameOptions { Terrain = Battlefields.ForSeed(seed * 31 + a.Length), Commanders = true };
                var mine = DeckValidator.DefaultDeck(a); mine = mine with { Runes = runes(mine) };
                var theirs = DeckValidator.DefaultDeck(b);
                var d0 = seat == 0 ? mine : theirs; var d1 = seat == 0 ? theirs : mine;
                var r = GameRunner.Play(new HeuristicAgent(), new HeuristicAgent(seed: 2), d0, d1, seed * 977 + 13, opts);
                tot++; if (r.Winner == seat) w++;
            }
        Console.WriteLine($"{name,-32} {100.0 * w / tot,5:F1}%  ({w}/{tot})");
    }
    return;
}

if (args.Length > 0 && args[0] == "han-bots")
{
    // dotnet run --project src/GwentArena -c Release -- han-bots [maç] [--builds] [--ab A B] [--check] [--only Frost,Horn] [--margin 0.03]
    //   botların özel kart / satır kullanımı, düzen başına sıra; --builds: oyundaki gibi koltuk başına düzen (ExpertBot.BuildFor);
    //   --ab: çift koltuklar A, tek koltuklar B düzeni (ör. balanced nospecial: özel kart kararı işe yarıyor mu)
    var nm = args.Length > 1 && int.TryParse(args[1], out var nmv) ? nmv : 100;
    var byId = HanLigi.Meta.Builds.ToDictionary(b => b.Id);
    HanLigi.ExpertBot.Build? abA = null, abB = null;
    var abi = Array.IndexOf(args, "--ab");
    if (abi >= 0) { abA = args[abi + 1] == "none" ? null : byId[args[abi + 1]]; abB = args[abi + 2] == "none" ? null : byId[args[abi + 2]]; }
    var builds = args.Contains("--builds");
    var adaptAb = args.Contains("--adapt-ab");
    if (Array.IndexOf(args, "--pivot") is var pvi and >= 0)   // --pivot round,min,oran
    {
        var pv = args[pvi + 1].Split(',');
        HanLigi.ExpertBot.K.PivotRound = int.Parse(pv[0]);
        HanLigi.ExpertBot.K.PivotMin = double.Parse(pv[1], System.Globalization.CultureInfo.InvariantCulture);
        HanLigi.ExpertBot.K.PivotRatio = double.Parse(pv[2], System.Globalization.CultureInfo.InvariantCulture);
    }   // çift koltuklar duruma göre yön değiştirir, tekler başladığı düzende kalır
    var check = args.Contains("--check");   // özel kart kararını gerçek rakibe karşı sına, idman sahasının gücünü yaz (yavaş)
    if (Array.IndexOf(args, "--only") is var oi and >= 0) HanLigi.ExpertBot.K.Allowed = args[oi + 1].Split(',').Select(Enum.Parse<HanLigi.Special>).ToHashSet();
    if (Array.IndexOf(args, "--margin") is var mgi and >= 0) HanLigi.ExpertBot.K.SpecialMargin = double.Parse(args[mgi + 1], System.Globalization.CultureInfo.InvariantCulture);
    if (Array.IndexOf(args, "--gain") is var gi and >= 0) HanLigi.ExpertBot.K.SpecialGain = double.Parse(args[gi + 1], System.Globalization.CultureInfo.InvariantCulture);
    var lk = new object();
    var sp = new Dictionary<HanLigi.Special, int>();
    var rowsPow = new double[3]; long seats = 0, amb = 0, heldSpecial = 0, heldTotal = 0;
    var places = new Dictionary<string, List<int>>();
    var str = new Dictionary<int, (double, double, int)>();
    var chk = new Dictionary<HanLigi.Special, (double, double, int)>();
    var per = new Dictionary<string, double[]>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Parallel.For(0, nm, g =>
    {
        var m = new HanLigi.Match(g + 1, Enumerable.Range(0, 8).Select(i => ($"B{i}", true)).ToList());
        var rng = new Random(g * 31 + 7);
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(3)]);
        HanLigi.ExpertBot.Build? BuildOf(int pid) => abi >= 0 ? (pid % 2 == 0 ? abA : abB) : builds ? HanLigi.ExpertBot.BuildFor(m.Seed, pid) : null;
        for (var guard = 0; guard < 80 && m.Phase != HanLigi.Phase.Finished; guard++)
        {
            foreach (var p in m.Alive.ToList()) HanLigi.ExpertBot.Prep(m, p.Id, rng, BuildOf(p.Id), adapt: !adaptAb || p.Id % 2 == 0);
            if (m.Phase == HanLigi.Phase.Prep) m.ForceLock();
            if (check && m.Round is 4 or 7 or 10 or 14)
                lock (lk)
                    foreach (var p in m.Alive)
                    {
                        var (mi, spr) = HanLigi.BotSimDebug.Strength(m, p.Id);
                        var key = m.Round;
                        if (!str.TryGetValue(key, out var v)) v = (0, 0, 0);
                        str[key] = (v.Item1 + mi, v.Item2 + spr, v.Item3 + 1);
                    }
            if (m.Round >= 6)
                lock (lk)
                    foreach (var p in m.Alive)
                    {
                        seats++;
                        foreach (var c in p.Cards.Where(c => c.Def.Special != HanLigi.Special.None))
                        {
                            heldTotal++;
                            if (c.Fielded) { sp[c.Def.Special] = sp.GetValueOrDefault(c.Def.Special) + 1; heldSpecial++; }
                        }
                        foreach (var c in p.Cards.Where(c => c.Fielded && c.Def.IsUnit && c.Row != null)) rowsPow[(int)c.Row!.Value] += c.Power;
                        var bk = adaptAb ? (p.Id % 2 == 0 ? "uyarlanır" : "sabit düzen") : BuildOf(p.Id)?.Id ?? "balanced(null)";
                        if (!per.TryGetValue(bk, out var pv)) per[bk] = pv = new double[10];
                        pv[0]++;
                        foreach (var c in p.Cards.Where(c => c.Fielded && c.Def.IsUnit && c.Row != null)) pv[1 + (int)c.Row!.Value] += c.Power;
                        pv[8] += p.Cards.Count(c => !c.Def.IsUnit);
                        pv[9] += p.Cards.Count(c => c.Def.Special == HanLigi.Special.Scorch);
                        foreach (var c in p.Cards.Where(c => c.Zone == HanLigi.Zone.Special)) pv[c.Def.Special is HanLigi.Special.Frost or HanLigi.Special.Fog or HanLigi.Special.Rain ? 4 : c.Def.Special == HanLigi.Special.Horn ? 5 : c.Def.Special == HanLigi.Special.Scorch ? 6 : 7]++;
                        amb += p.Cards.Count(c => c.Zone == HanLigi.Zone.Ambush);
                    }
            foreach (var b in m.Battles.Where(b => check && !b.Pve))
            {
                var pa = b.Sides[0].PlayerId;
                var pb = b.Ghost ? m.Players[pa].OppId : b.Sides[1].PlayerId;
                foreach (var (me, foe) in new[] { (pa, pb), (pb, pa) })
                {
                    if (me < 0 || foe < 0 || !m.Players[me].Alive) continue;
                    var ch = HanLigi.BotSimDebug.Check(m, me, foe);
                    lock (lk) foreach (var (spc, ds, dr) in ch)
                    {
                        var v = chk.GetValueOrDefault(spc);
                        chk[spc] = (v.Item1 + ds, v.Item2 + dr, v.Item3 + 1);
                    }
                }
            }
            foreach (var b in m.Battles)
            {
                b.Start();
                while (!b.Done) { for (var s = 0; s < 2; s++) b.Submit(s, HanLigi.ExpertBot.Window(b, s, rng)); b.Resolve(); }
                m.Settle(b);
            }
            m.EndRound();
        }
        lock (lk)
            foreach (var p in m.Players)
            {
                var k = adaptAb ? (p.Id % 2 == 0 ? "uyarlanır" : "sabit düzen") : BuildOf(p.Id)?.Id ?? "balanced(null)";
                if (!places.TryGetValue(k, out var l)) places[k] = l = new();
                l.Add(p.Place);
            }
    });
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine($"{nm} maç, {sw.Elapsed.TotalSeconds:F0} sn; koltuk-round {seats}; satır gücü Yakın/Menzilli/Kuşatma = {rowsPow[0]/seats:F1}/{rowsPow[1]/seats:F1}/{rowsPow[2]/seats:F1}; pusu {amb/(double)seats:F2}/koltuk");
    Console.WriteLine("sahada özel kart / koltuk-round: " + string.Join(", ", sp.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value/(double)seats:F2}")) + $" (eldeki {heldTotal/(double)seats:F2}, sahada {heldSpecial/(double)seats:F2})");
    foreach (var (spc, v) in chk) Console.WriteLine($"  {spc}: idmana göre {v.Item1 / v.Item3 * 100:+0.0;-0.0} puan, gerçek rakibe göre {v.Item2 / v.Item3 * 100:+0.0;-0.0} puan kazanma şansı (n={v.Item3})");
    foreach (var (r, v) in str.OrderBy(k => k.Key)) Console.WriteLine($"  round {r}: saha toplamı {v.Item1 / v.Item3:F1}, idman sahası {v.Item2 / v.Item3:F1}");
    foreach (var (k, l) in places.OrderBy(k => k.Value.Average()))
    {
        var avg = l.Average();
        var ci = 1.96 * Math.Sqrt(l.Sum(x => (x - avg) * (x - avg)) / Math.Max(1, l.Count - 1)) / Math.Sqrt(l.Count);
        var pv = per.GetValueOrDefault(k) ?? new double[10];
        var n0 = Math.Max(1, pv[0]);
        Console.WriteLine($"  {k,-16} ort. sıra {avg:F2} ± {ci:F2}  (n={l.Count})  Y/M/K {pv[1]/n0:F0}/{pv[2]/n0:F0}/{pv[3]/n0:F0}  hava {pv[4]/n0:F2} boynuz {pv[5]/n0:F2} kavurma {pv[6]/n0:F2} kalkan {pv[7]/n0:F2} (elde {pv[8]/n0:F2}, Kavurma elde {pv[9]/n0:F2})");
    }
    return;
}
if (args.Length > 0 && args[0] == "han-meta")
{
    // dotnet run --project src/GwentArena -c Release -- han-meta [maç] [tohum] [çıktı.md]
    HanLigi.Tuning.AmbushTakesSlot = args.Contains("--ambush-slot");   // karşılaştırma: eski kural
    // ayar denemesi: --xp 5, --interest 3
    if (Array.IndexOf(args, "--xp") is var xi and >= 0) HanLigi.Tuning.XpCost = int.Parse(args[xi + 1]);
    if (Array.IndexOf(args, "--interest") is var ii and >= 0) HanLigi.Tuning.InterestCap = int.Parse(args[ii + 1]);
    if (Array.IndexOf(args, "--damage") is var di and >= 0) HanLigi.Tuning.DamageMode = int.Parse(args[di + 1]);
    if (Array.IndexOf(args, "--streak") is var si and >= 0) HanLigi.Tuning.StreakMode = int.Parse(args[si + 1]);
    if (Array.IndexOf(args, "--ramp") is var ri and >= 0) HanLigi.Tuning.RampMode = int.Parse(args[ri + 1]);
    // Odak denemesi: --focus 2, --focus-cap 4, --no-quen-refund; --signs: koltuklara işaret stratejisi dağıt
    if (Array.IndexOf(args, "--focus") is var fi and >= 0) HanLigi.Tuning.StartFocus = int.Parse(args[fi + 1]);
    if (Array.IndexOf(args, "--focus-cap") is var fc and >= 0) HanLigi.Tuning.FocusCap = int.Parse(args[fc + 1]);
    if (args.Contains("--no-quen-refund")) HanLigi.Tuning.QuenRefund = false;
    if (args.Contains("--auto-land-last")) HanLigi.Tuning.AutoLandLast = true;   // karşılaştırma: eski kural (son pencerede pusu kendiliğinden iner)
    if (args.Contains("--yrden-aard")) HanLigi.Tuning.YrdenBlocksAard = true;
    if (Array.IndexOf(args, "--yrden-mode") is var ym and >= 0) HanLigi.Tuning.YrdenMode = int.Parse(args[ym + 1]);
    if (Array.IndexOf(args, "--yrden-cost") is var yc and >= 0) HanLigi.Tuning.YrdenCost = int.Parse(args[yc + 1]);
    if (Array.IndexOf(args, "--ambush-base") is var ab and >= 0) HanLigi.Tuning.AmbushBase = int.Parse(args[ab + 1]);
    if (args.Contains("--no-yrden-potion")) HanLigi.Tuning.YrdenPotion = false;
    if (Array.IndexOf(args, "--yrden-amb") is var ya and >= 0) HanLigi.Tuning.YrdenAmbushPct = double.Parse(args[ya + 1], System.Globalization.CultureInfo.InvariantCulture);
    if (Array.IndexOf(args, "--yrden-trap") is var yt and >= 0) HanLigi.Tuning.YrdenTrapPct = double.Parse(args[yt + 1], System.Globalization.CultureInfo.InvariantCulture);
    double[] Nums(string v) => v.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    if (Array.IndexOf(args, "--sailor-first") is var sf and >= 0) Nums(args[sf + 1]).CopyTo(HanLigi.Tuning.SailorFirst, 0);   // Denizci ilk pencere yüzdeleri: 0.2,0.3
    if (Array.IndexOf(args, "--sailor-flat") is var sfl and >= 0) Nums(args[sfl + 1]).Select(x => (int)x).ToArray().CopyTo(HanLigi.Tuning.SailorFlat, 0);
    if (Array.IndexOf(args, "--window") is var wi and >= 0) HanLigi.ExpertBot.K.WindowMode = int.Parse(args[wi + 1]);   // botun pencere kararı: 0 klasik, 1 SmartWindow
    HanLigi.Meta.Adapt = args.Contains("--adapt");   // botlar duruma göre yön değiştirsin (düzen tablosu "başlangıç düzeni" olur)
    HanLigi.Meta.SignPolicies = args.Contains("--signs");
    var mg = args.Length > 1 ? int.Parse(args[1]) : 1000;
    var ms = args.Length > 2 ? int.Parse(args[2]) : 1;
    var rep = HanLigi.Meta.Run(mg, ms, d => Console.Error.Write($"\r{d}/{mg}"));
    Console.Error.WriteLine($"\r{mg} maç, {rep.Seconds:F0} sn");
    var md = HanLigi.Meta.Markdown(rep, $"Meta ölçümü ({DateTime.Now:yyyy-MM-dd})") + Environment.NewLine + HanLigi.Meta.Signs.Markdown();
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(md);
    if (args.Length > 3 && !args[3].StartsWith("--")) File.WriteAllText(args[3], md);   // 3. konum: çıktı dosyası (bayraklar değil)
    // --cards dosya.tsv: kart başına son sahadaki bitiş sırası (kart dengesi)
    if (Array.IndexOf(args, "--cards") is var ci2 and >= 0)
        File.WriteAllLines(args[ci2 + 1], new[] { "id\tname\ttier\tpower\ttags\tability\tspecial\thero\tn\tavg\tci\ttop4\twin" }.Concat(
            HanLigi.CardSet.All.Select(d => (d, s: rep.Cards.GetValueOrDefault(d.Id))).Select(x =>
                $"{x.d.Id}\t{x.d.Name}\t{x.d.Tier}\t{x.d.Power}\t{string.Join(',', x.d.Tags)}\t{x.d.Ability}\t{x.d.Special}\t{x.d.Hero}\t{x.s?.N ?? 0}\t" +
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{x.s?.Avg ?? double.NaN:F3}\t{x.s?.Ci ?? double.NaN:F3}\t{x.s?.Top4Rate ?? 0:F3}\t{x.s?.WinRate ?? 0:F3}"))));
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
