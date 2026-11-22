namespace HanLigi;

/// <summary>
/// Meta ölçümü: her koltuğa bir düzen (build) bağlanmış güçlü botlarla 8 kişilik maçlar oynatılır; düzenlerin, okulların ve
/// sınıf eşiklerinin ortalama bitiş sırası çıkarılır. Kart/ayar değişince yeniden çalıştırılır (aylık güncellemeler):
/// <c>dotnet run --project src/GwentArena -c Release -- han-meta 2000</c>.
/// </summary>
public static class Meta
{
    public static readonly ExpertBot.Build[] Builds =
    {
        new("balanced", "Dengeli (yönsüz)"),
        new("monster", "Canavar sürüsü", Tag: Tag.Monster),
        new("soldier", "Asker hattı", Tag: Tag.Soldier),
        new("mage", "Büyücüler", Tag: Tag.Mage),
        new("bandit", "Haydut pususu", Tag: Tag.Bandit),
        new("noble", "Soylular", Tag: Tag.Noble),
        new("dwarf", "Cüceler", Tag: Tag.Dwarf),
        // Faz 1 (2026-11-23): Denizci Yağma ve Mardroeme ile dönüşen Berserker'larla, Şövalye Meydan Okuma ile
        new("sailor", "Denizci yağması", Tag: Tag.Sailor, Prefer: new[] { Special.Mardroeme }),
        new("knight", "Şövalyeler", Tag: Tag.Knight),
        new("spy", "Casus + pusu", Abilities: new[] { Ability.Spy, Ability.Ambush }),
        new("bond", "Sıkı bağ", Abilities: new[] { Ability.Bond }),
        new("fast", "Hızlı seviye", LevelShift: 1, ReserveHigh: 0),
        new("econ", "Faiz birikimi", LevelShift: -1, ReserveHigh: 20),
        new("hyper", "Hiper-yenileme (1-2 tier üçlü)", Hyper: true, ReserveHigh: 0),
        new("horn", "Hava + Boynuz", SpecialBonus: 4),
        // hava satırını ucuz kalabalıkla ve Askerle (eşikte havaya dayanıklı) taşır; Kalkan kendi satırını korur
        new("weather", "Hava + Asker", Tag: Tag.Soldier, SpecialBonus: 2, Prefer: new[] { Special.Frost, Special.Fog, Special.Rain, Special.Shield }, SpecialMargin: -0.02),
        // Cüce 2. eşik Kavurma'dan korur: rakibin en güçlü kartını yakar
        new("scorch", "Kavurma + Cüce", Tag: Tag.Dwarf, SpecialBonus: 2, Prefer: new[] { Special.Scorch, Special.Shield }, SpecialMargin: -0.02),
        // Yakın'a yığılan rakipleri Ayaz'la kırar, gücünü başka satırda toplar
        new("ranged", "Menzilli hat + Ayaz", Row: Row.Ranged, Prefer: new[] { Special.Frost }, SpecialMargin: 0),
        new("siege", "Kuşatma hattı + Ayaz", Row: Row.Siege, Prefer: new[] { Special.Frost }, SpecialMargin: 0),
        new("nospecial", "Özel kartsız", Specials: false),
    };

    public sealed class Stat
    {
        public string Name = "";
        public int N;
        public double PlaceSum;
        public int Top4, Wins;
        public double Avg => N == 0 ? double.NaN : PlaceSum / N;
        public double Top4Rate => N == 0 ? 0 : Top4 / (double)N;
        public double WinRate => N == 0 ? 0 : Wins / (double)N;
        public double Sd;
        /// <summary>Ortalamanın %95 güven aralığının yarısı.</summary>
        public double Ci => N < 2 ? double.NaN : 1.96 * Sd / Math.Sqrt(N);
        internal readonly List<int> Places = new();
        internal void Add(int place)
        {
            N++;
            PlaceSum += place;
            if (place <= 4) Top4++;
            if (place == 1) Wins++;
            Places.Add(place);
        }
        internal void Finish()
        {
            if (N < 2) return;
            var mean = Avg;
            Sd = Math.Sqrt(Places.Sum(p => (p - mean) * (p - mean)) / (N - 1));
        }
    }

    /// <summary>
    /// Eğlencenin dolaylı göstergeleri: <b>Comeback</b> = 10. round'da canı en düşük yarıdayken maçı kazananların oranı;
    /// <b>Close</b> = PvP savaşlarının 2–1 bitenleri; <b>WinnerTags</b> = kazananın ana sınıfı (en yüksek eşik) dağılımı ve
    /// bunun normalleştirilmiş entropisi (1 = her sınıf eşit sıklıkta kazanıyor); <b>HeroWins</b> = kazananın sahasında kahraman.
    /// </summary>
    public sealed record Fun(double Comeback, double Close, Dictionary<string, double> WinnerTags, double Diversity, double HeroWins);

    public sealed record Report(int Games, double Seconds, List<Stat> Builds, List<Stat> Schools, List<Stat> Traits, List<Stat> Heroes, double AvgRounds, Fun Fun);

    private sealed record Game((int[] Build, School[] School, int[] Place, HashSet<(Tag, int)>[] Traits, HashSet<string>[] Heroes, int Rounds) Core,
        bool? Comeback, int Pvp, int TwoOne, string WinnerTag, bool WinnerHero);

    /// <summary><paramref name="games"/> maç oynatır (paralel, belirlenimci: aynı tohum aynı sonuç).</summary>
    public static Report Run(int games, int seed = 1, Action<int>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var games2 = new Game[games];
        var done = 0;
        Parallel.For(0, games, g =>
        {
            games2[g] = Play(seed * 100003 + g);
            var d = Interlocked.Increment(ref done);
            if (progress != null && d % 50 == 0) progress(d);
        });
        Stat New(string n) => new() { Name = n };
        var builds = Builds.Select(b => New(b.Name)).ToList();
        var schools = Enum.GetValues<School>().Select(s => New(Texts.Name(s))).ToList();
        var traits = new Dictionary<(Tag, int), Stat>();
        var heroes = new Dictionary<string, Stat>();
        var results = games2.Select(x => x.Core).ToList();
        foreach (var r in results)
            for (var i = 0; i < 8; i++)
            {
                builds[r.Build[i]].Add(r.Place[i]);
                schools[(int)r.School[i]].Add(r.Place[i]);
                foreach (var t in r.Traits[i])
                {
                    if (!traits.TryGetValue(t, out var st)) traits[t] = st = New($"{Texts.Name(t.Item1)} {Tuning.Breakpoints(t.Item1)[t.Item2 - 1]}+");
                    st.Add(r.Place[i]);
                }
                foreach (var h in r.Heroes[i])
                {
                    if (!heroes.TryGetValue(h, out var st)) heroes[h] = st = New(CardSet.Get(h).Name);
                    st.Add(r.Place[i]);
                }
            }
        foreach (var s in builds.Concat(schools).Concat(traits.Values).Concat(heroes.Values)) s.Finish();
        return new Report(games, sw.Elapsed.TotalSeconds, builds.OrderBy(s => s.Avg).ToList(), schools.OrderBy(s => s.Avg).ToList(),
            traits.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2).Select(k => k.Value).ToList(), heroes.Values.OrderBy(s => s.Avg).ToList(),
            results.Average(r => r.Rounds), FunOf(games2));
    }

    private static Fun FunOf(Game[] gs)
    {
        var cb = gs.Where(g => g.Comeback != null).ToList();
        var tags = gs.GroupBy(g => g.WinnerTag).ToDictionary(g => g.Key, g => g.Count() / (double)gs.Length);
        var real = tags.Where(k => k.Key != "—").Select(k => k.Value).ToList();
        var sum = real.Sum();
        var h = real.Where(v => v > 0).Sum(v => -(v / sum) * Math.Log(v / sum));
        return new Fun(cb.Count == 0 ? 0 : cb.Count(g => g.Comeback == true) / (double)cb.Count,
            gs.Sum(g => g.Pvp) == 0 ? 0 : gs.Sum(g => g.TwoOne) / (double)gs.Sum(g => g.Pvp),
            tags, h / Math.Log(Enum.GetValues<Tag>().Length), gs.Count(g => g.WinnerHero) / (double)gs.Length);
    }

    private static Game Play(int seed)
    {
        HashSet<int>? lowAt10 = null;
        var rng = new Random(seed);
        var m = new Match(seed, Enumerable.Range(0, 8).Select(i => ($"B{i}", true)).ToList());
        // alt havuz: yalnız sınıfı bu maçta açık olan (ya da sınıfsız) düzenler oturur
        var pick = Enumerable.Range(0, Builds.Length).Where(i => Builds[i].Tag is not { } t || m.ActiveTags.Contains(t)).OrderBy(_ => rng.Next()).Take(8).ToArray();
        var pol = SignPolicies ? Enumerable.Range(0, 8).Select(_ => rng.Next(Policies.Length)).ToArray() : null;
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(p.SchoolOptions.Length)]);
        var traits = Enumerable.Range(0, 8).Select(_ => new HashSet<(Tag, int)>()).ToArray();
        var heroes = Enumerable.Range(0, 8).Select(_ => new HashSet<string>()).ToArray();
        for (var guard = 0; guard < 80 && m.Phase != Phase.Finished; guard++)
        {
            foreach (var p in m.Alive.ToList()) ExpertBot.Prep(m, p.Id, rng, Builds[pick[p.Id]]);
            if (m.Phase == Phase.Prep) m.ForceLock();
            if (m.Round == 10 && lowAt10 == null)
            {
                var al = m.Alive.OrderBy(p => p.Hp).ToList();
                lowAt10 = al.Take(al.Count / 2).Select(p => p.Id).ToHashSet();
            }
            // round ortası sonrasında ulaşılan sınıf eşikleri ve sahadaki kahramanlar (düzenin gerçekte ne kurduğu)
            if (m.Round >= 8)
                foreach (var p in m.Alive)
                {
                    foreach (var t in Enum.GetValues<Tag>()) if (p.Th(t) >= 1) traits[p.Id].Add((t, p.Th(t)));
                    foreach (var c in p.Fielded.Where(c => c.Def.IsHero)) heroes[p.Id].Add(c.Def.Id);
                }
            foreach (var b in m.Battles)
            {
                b.Start();
                while (!b.Done)
                {
                    var focus = new[] { b.Sides[0].Focus, b.Sides[1].Focus };
                    for (var s = 0; s < 2; s++) b.Submit(s, Choose(b, s, rng, pol));
                    b.Resolve();
                    Signs.Window(b, focus, pol);
                }
                Signs.Battle(b, pol);
                m.Settle(b);
            }
            m.EndRound();
        }
        // eşikler: yalnız en yüksek ulaşılanı say (Canavar 4+ ise 2+ ayrıca sayılmasın)
        foreach (var t in traits)
            foreach (var g in t.GroupBy(x => x.Item1).ToList())
                foreach (var lower in g.Where(x => x.Item2 < g.Max(y => y.Item2)).ToList()) t.Remove(lower);
        if (pol != null) Signs.Places(m, pol);
        var core = (pick, m.Players.Select(p => p.School!.Value).ToArray(), m.Players.Select(p => p.Place).ToArray(), traits, heroes, m.Round);
        var win = m.Players.First(p => p.Place == 1);
        var top = Enum.GetValues<Tag>().Select(t => (T: t, L: win.Th(t), N: win.TagCounts().GetValueOrDefault(t))).Where(x => x.L > 0).OrderByDescending(x => x.L).ThenByDescending(x => x.N).FirstOrDefault();
        var pvp = m.History.Where(h => !h.Pve && !h.Ghost && h.Winner >= 0).ToList();
        return new Game(core, lowAt10 == null ? null : lowAt10.Contains(win.Id), pvp.Count, pvp.Count(h => h.PointsA + h.PointsB == 3),
            top.L > 0 ? Texts.Name(top.T) : "—", win.Fielded.Any(c => c.Def.IsHero));
    }

    // ---------- işaret / Odak ölçümü ----------

    /// <summary>Açıksa her koltuğa rastgele bir işaret stratejisi verilir (Policies); simya ve pusu kararları aynı kalır.</summary>
    public static bool SignPolicies;
    public static readonly string[] Policies = { "Uyarlanır (bot)", "Hep Quen", "Hep Aard", "Igni öncelikli", "İşaretsiz", "Hep Yrden" };
    public static readonly SignStats Signs = new();

    private static WindowChoice Choose(Battle b, int side, Random rng, int[]? pol)
    {
        var c = ExpertBot.Window(b, side, rng);
        var pid = b.Sides[side].PlayerId;
        if (pol == null || pid < 0 || (side == 1 && (b.Ghost || b.Pve)) || pol[pid] == 0) return c;
        Sign[] want = pol[pid] switch { 1 => new[] { Sign.Quen }, 2 => new[] { Sign.Aard }, 3 => new[] { Sign.Igni, Sign.Aard }, 5 => new[] { Sign.Yrden }, _ => Array.Empty<Sign>() };
        foreach (var sg in want)
        {
            var t = c with { Signs = new[] { sg } };
            if (b.Valid(side, t)) return t;
        }
        var none = c with { Signs = Array.Empty<Sign>() };
        return b.Valid(side, none) ? none : WindowChoice.None;
    }

    /// <summary>Odak ve işaret istatistikleri (oyuncu tarafları; gölge/PvE canavarı sayılmaz).</summary>
    public sealed class SignStats
    {
        private readonly object _l = new();
        public long Windows, Broke, NoIgni, Skipped, Battles, Leftover, Window3, Broke3;
        public readonly Dictionary<string, long> Picks = new();
        public readonly Dictionary<(string, string), (long Win, long N)> Duel = new();
        public readonly Dictionary<int, List<int>> PolicyPlaces = new();

        public void Reset()
        {
            lock (_l) { Windows = Broke = NoIgni = Skipped = Battles = Leftover = Window3 = Broke3 = 0; Picks.Clear(); Duel.Clear(); PolicyPlaces.Clear(); }
        }

        private static bool Player(Battle b, int side) => b.Sides[side].PlayerId >= 0 && !(side == 1 && (b.Ghost || b.Pve));
        private static bool Tracked(Battle b, int side, int[]? pol) => Player(b, side) && (pol == null || pol[b.Sides[side].PlayerId] == 0);
        private static string Name(WindowChoice c) => c.Signs.Count == 0 ? "—" : c.Signs[0].ToString();

        public void Window(Battle b, int[] focusBefore, int[]? pol)
        {
            var o = b.Outcomes[^1];
            lock (_l)
                for (var i = 0; i < 2; i++)
                {
                    var mine = i == 0 ? o.ChoiceA : o.ChoiceB;
                    var foe = i == 0 ? o.ChoiceB : o.ChoiceA;
                    if (Player(b, i) && Player(b, 1 - i))
                    {
                        var k = (Name(mine), Name(foe));
                        var (w, n) = Duel.GetValueOrDefault(k);
                        Duel[k] = (w + (o.Winner == i ? 1 : 0), n + 1);
                    }
                    if (!Tracked(b, i, pol)) continue;
                    Windows++;
                    if (focusBefore[i] < 1) Broke++;
                    if (focusBefore[i] < 2) NoIgni++;
                    if (o.Index == 2) { Window3++; if (focusBefore[i] < 1) Broke3++; }
                    if (mine.Signs.Count == 0 && focusBefore[i] >= 1) Skipped++;
                    var nm = Name(mine);
                    Picks[nm] = Picks.GetValueOrDefault(nm) + 1;
                }
        }

        public void Battle(Battle b, int[]? pol)
        {
            lock (_l)
                for (var i = 0; i < 2; i++)
                    if (Tracked(b, i, pol)) { Battles++; Leftover += b.Sides[i].Focus; }
        }

        public void Places(Match m, int[] pol)
        {
            lock (_l)
                foreach (var p in m.Players)
                {
                    if (!PolicyPlaces.TryGetValue(pol[p.Id], out var l)) PolicyPlaces[pol[p.Id]] = l = new();
                    l.Add(p.Place);
                }
        }

        public string Markdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("### Odak ve işaretler");
            sb.AppendLine();
            double Pc(long a, long b) => b == 0 ? 0 : 100.0 * a / b;
            sb.AppendLine($"- Odak hiç yetmeyen pencere: %{Pc(Broke, Windows):F1} (3. satırda %{Pc(Broke3, Window3):F1}); Igni'ye yetmeyen: %{Pc(NoIgni, Windows):F1}.");
            sb.AppendLine($"- Odak varken işaret atılmayan pencere: %{Pc(Skipped, Windows):F1}. Savaş sonunda artan Odak: ortalama {(Battles == 0 ? 0 : Leftover / (double)Battles):F2}.");
            sb.AppendLine("- Seçilen işaret: " + string.Join(", ", Picks.OrderByDescending(k => k.Value).Select(k => $"{k.Key} %{Pc(k.Value, Windows):F0}")) + ".");
            if (PolicyPlaces.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Strateji | Ort. sıra | İlk 4 | Örnek |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var (k, l) in PolicyPlaces.OrderBy(k => k.Value.Average()))
                {
                    var avg = l.Average();
                    var ci = 1.96 * Math.Sqrt(l.Sum(x => (x - avg) * (x - avg)) / Math.Max(1, l.Count - 1)) / Math.Sqrt(l.Count);
                    sb.AppendLine($"| {Policies[k]} | {avg:F2} ± {ci:F2} | %{100.0 * l.Count(x => x <= 4) / l.Count:F0} | {l.Count} |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("Satır kazanma oranı (satır sahibinin işareti ↓ · rakibin işareti →; PvP):");
            sb.AppendLine();
            var names = new[] { "—", "Aard", "Igni", "Quen", "Yrden" };
            sb.AppendLine("| | " + string.Join(" | ", names) + " |");
            sb.AppendLine("|---|" + string.Concat(names.Select(_ => "---|")));
            foreach (var a in names)
                sb.AppendLine($"| {a} | " + string.Join(" | ", names.Select(bn => Duel.TryGetValue((a, bn), out var v) && v.N >= 30 ? $"%{100.0 * v.Win / v.N:F0}" : "·")) + " |");
            return sb.ToString();
        }
    }

    /// <summary>Kademe: ortalama sıranın 4,5'ten farkına göre (S en güçlü).</summary>
    public static string Tier(double avg) => avg < 3.9 ? "S" : avg < 4.25 ? "A" : avg < 4.75 ? "B" : avg < 5.1 ? "C" : "D";

    public static string Markdown(Report r, string title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine($"{r.Games} maç × 8 güçlü bot, her koltuğa rastgele bir düzen ({Builds.Length} düzenden 8'i), okul rastgele. Ortalama {r.AvgRounds:F1} round. ");
        sb.AppendLine("Ortalama sıra 4,5 = nötr; küçük daha iyi. ± = %95 güven aralığı. Kademe: S < 3,9 · A < 4,25 · B < 4,75 · C < 5,1 · D.");
        sb.AppendLine();
        void Table(string head, IEnumerable<Stat> rows, bool tier)
        {
            sb.AppendLine($"### {head}");
            sb.AppendLine();
            sb.AppendLine(tier ? "| Kademe | Düzen | Ort. sıra | İlk 4 | 1. | Örnek |" : "| | Ort. sıra | İlk 4 | 1. | Örnek |");
            sb.AppendLine(tier ? "|---|---|---|---|---|---|" : "|---|---|---|---|---|");
            foreach (var s in rows)
                sb.AppendLine((tier ? $"| {Tier(s.Avg)} " : "") + $"| {s.Name} | {s.Avg:F2} ± {s.Ci:F2} | %{s.Top4Rate * 100:F0} | %{s.WinRate * 100:F1} | {s.N} |");
            sb.AppendLine();
        }
        sb.AppendLine("### Eğlence göstergeleri");
        sb.AppendLine();
        sb.AppendLine($"- **Arkadan gelme:** 10. round'da canı en düşük yarıdayken kazananlar: %{r.Fun.Comeback * 100:F0} (yarı yarıya şans %50; düşük olması öndekinin kartopu demek).");
        sb.AppendLine($"- **Başa baş savaşlar:** PvP savaşlarının %{r.Fun.Close * 100:F0}'i 2–1 bitiyor.");
        sb.AppendLine($"- **Kazanan tahtaların çeşitliliği:** {r.Fun.Diversity:F2} (1 = altı sınıf eşit sıklıkta kazanıyor). Kazananın ana sınıfı: " +
            string.Join(", ", r.Fun.WinnerTags.OrderByDescending(k => k.Value).Select(k => $"{k.Key} %{k.Value * 100:F0}")) + ".");
        sb.AppendLine($"- **Kahramanlı zafer:** kazananların %{r.Fun.HeroWins * 100:F0}'inin sahasında kahraman var.");
        sb.AppendLine();
        Table("Düzenler", r.Builds, true);
        Table("Okullar", r.Schools, false);
        Table("Sınıf eşikleri (8. round sonrası ulaşılan en yüksek)", r.Traits.Where(s => s.N >= 30), false);
        Table("Kahramanlar (8. round sonrası sahada)", r.Heroes.Where(s => s.N >= 30), false);
        return sb.ToString();
    }
}
