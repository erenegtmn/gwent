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
        new("spy", "Casus + pusu", Abilities: new[] { Ability.Spy, Ability.Ambush }),
        new("bond", "Sıkı bağ", Abilities: new[] { Ability.Bond }),
        new("fast", "Hızlı seviye", LevelShift: 1, ReserveHigh: 0),
        new("econ", "Faiz birikimi", LevelShift: -1, ReserveHigh: 20),
        new("hyper", "Hiper-yenileme (1-2 tier üçlü)", Hyper: true, ReserveHigh: 0),
        new("weather", "Hava + Boynuz", SpecialBonus: 4),
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

    public sealed record Report(int Games, double Seconds, List<Stat> Builds, List<Stat> Schools, List<Stat> Traits, List<Stat> Heroes, double AvgRounds);

    /// <summary><paramref name="games"/> maç oynatır (paralel, belirlenimci: aynı tohum aynı sonuç).</summary>
    public static Report Run(int games, int seed = 1, Action<int>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = new (int[] Build, School[] School, int[] Place, HashSet<(Tag, int)>[] Traits, HashSet<string>[] Heroes, int Rounds)[games];
        var done = 0;
        Parallel.For(0, games, g =>
        {
            results[g] = Play(seed * 100003 + g);
            var d = Interlocked.Increment(ref done);
            if (progress != null && d % 50 == 0) progress(d);
        });
        Stat New(string n) => new() { Name = n };
        var builds = Builds.Select(b => New(b.Name)).ToList();
        var schools = Enum.GetValues<School>().Select(s => New(Texts.Name(s))).ToList();
        var traits = new Dictionary<(Tag, int), Stat>();
        var heroes = new Dictionary<string, Stat>();
        foreach (var r in results)
            for (var i = 0; i < 8; i++)
            {
                builds[r.Build[i]].Add(r.Place[i]);
                schools[(int)r.School[i]].Add(r.Place[i]);
                foreach (var t in r.Traits[i])
                {
                    if (!traits.TryGetValue(t, out var st)) traits[t] = st = New($"{Texts.Name(t.Item1)} {t.Item2 * 2}+");
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
            results.Average(r => r.Rounds));
    }

    private static (int[] Build, School[] School, int[] Place, HashSet<(Tag, int)>[] Traits, HashSet<string>[] Heroes, int Rounds) Play(int seed)
    {
        var rng = new Random(seed);
        var pick = Enumerable.Range(0, Builds.Length).OrderBy(_ => rng.Next()).Take(8).ToArray();
        var m = new Match(seed, Enumerable.Range(0, 8).Select(i => ($"B{i}", true)).ToList());
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(p.SchoolOptions.Length)]);
        var traits = Enumerable.Range(0, 8).Select(_ => new HashSet<(Tag, int)>()).ToArray();
        var heroes = Enumerable.Range(0, 8).Select(_ => new HashSet<string>()).ToArray();
        for (var guard = 0; guard < 80 && m.Phase != Phase.Finished; guard++)
        {
            foreach (var p in m.Alive.ToList()) ExpertBot.Prep(m, p.Id, rng, Builds[pick[p.Id]]);
            if (m.Phase == Phase.Prep) m.ForceLock();
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
                    for (var s = 0; s < 2; s++) b.Submit(s, ExpertBot.Window(b, s, rng));
                    b.Resolve();
                }
                m.Settle(b);
            }
            m.EndRound();
        }
        // eşikler: yalnız en yüksek ulaşılanı say (Canavar 4+ ise 2+ ayrıca sayılmasın)
        foreach (var t in traits)
            foreach (var g in t.GroupBy(x => x.Item1).ToList())
                foreach (var lower in g.Where(x => x.Item2 < g.Max(y => y.Item2)).ToList()) t.Remove(lower);
        return (pick, m.Players.Select(p => p.School!.Value).ToArray(), m.Players.Select(p => p.Place).ToArray(), traits, heroes, m.Round);
    }

    /// <summary>Kademe: ortalama sıranın 4,5'ten farkına göre (S en güçlü).</summary>
    public static string Tier(double avg) => avg < 3.9 ? "S" : avg < 4.25 ? "A" : avg < 4.75 ? "B" : avg < 5.1 ? "C" : "D";

    public static string Markdown(Report r, string title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine($"{r.Games} maç × 8 güçlü bot, her koltuğa rastgele bir düzen (14 düzenden 8'i), okul rastgele. Ortalama {r.AvgRounds:F1} round. ");
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
        Table("Düzenler", r.Builds, true);
        Table("Okullar", r.Schools, false);
        Table("Sınıf eşikleri (8. round sonrası ulaşılan en yüksek)", r.Traits.Where(s => s.N >= 30), false);
        Table("Kahramanlar (8. round sonrası sahada)", r.Heroes.Where(s => s.N >= 30), false);
        return sb.ToString();
    }
}
