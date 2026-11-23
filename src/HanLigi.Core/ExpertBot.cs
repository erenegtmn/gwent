namespace HanLigi;

/// <summary>
/// Güçlü yapay zekâ (online ve tek oyunculu maçlarda boş koltuklar). Gizli bilgiyi kullanmaz: rakibin sahası, eli ve
/// seçimleri görünmez; yalnız açılmış satırlar, Odak ve simya sayısı, casusun gösterdikleri.
/// <list type="bullet">
/// <item><b>Saha:</b> dizilim motorun kendi satır değeriyle (<see cref="Match.Preview"/>: Bağ, Büyüme, Moral, sınıf, Boynuz dahil)
/// yerel aramayla seçilir. Savaş üç satırın en iyi ikisi olduğundan <b>ikinci güçlü satır</b> en ağır basar.</item>
/// <item><b>Alışveriş:</b> çift/üçlü (yıldız atlama) ve sınıf eşikleri peşinde; kart başına güç/altın; kahraman.</item>
/// <item><b>Ekonomi:</b> faiz eşikleri (10/20) korunur, round'a göre seviye eğrisi; can azalınca birikim harcanır (yenile, XP).</item>
/// <item><b>Savaş:</b> satırın önemi (belirleyici mi), farkın yakınlığı ve kalan Odak'a göre işaret; Quen rakip Igni atabilecekken
/// önde olduğumuz satırda; simya ve pusu yakın/belirleyici satırda.</item>
/// </list>
/// </summary>
public static class ExpertBot
{
    /// <summary>Ayar sayıları (ölçümle seçildi: ExpertBotTests). Testler taramak için değiştirebilir.</summary>
    public static class K
    {
        public static double Top = 0.8, Second = 1.25, Third = 0.35;
        public static int ReserveHigh = 10, ReserveMid = 0, LevelShift;
        public static int RollAt = 30, RollHp = 50;
        public static bool ExpertWindows;
        public static double Noise = 0;
        /// <summary>Özel kart alımında kazanma şansı artışının alım puanına çevrimi.</summary>
        public static double SpecialGain = 60;
        /// <summary>Ölçüm: yalnız bu özel kartlar kullanılır (null = hepsi).</summary>
        public static HashSet<Special>? Allowed;
        /// <summary>Sahadaki özel kart başına kazanma şansı payı (ölçümle seçilir).</summary>
        public static double SpecialMargin = 0.03;
        /// <summary>Yön değiştirme (<see cref="Pivot"/>): en erken round, en az yatırım, eski sınıfa göre oran.</summary>
        public static int PivotRound = 4;
        /// <summary>Pencere kararı: 0 klasik (HanBot, gürültüsüz), 1 <see cref="SmartWindow"/> (2026-11-24).</summary>
        public static int WindowMode = 1;
        public static double PivotMin = 4, PivotRatio = 1.4;
    }

    /// <summary>
    /// Düzen (build) eğilimi: meta ölçümü için botun belli bir stratejiye bağlanması (<see cref="Meta"/>). Varsayılan (null) dengeli
    /// oyundur. Sınıf / yetenek odağı alımda ağırlık verir; seviye kayması, faiz birikimi, hiper-yenileme (düşük seviyede kalıp 1-2
    /// tier üçlü arama) ekonomiyi değiştirir.
    /// </summary>
    /// <param name="Row">Satır eğilimi: bu satırın kartlarına alımda ağırlık, dizilimde o satır güçlü tutulur (herkes Yakın'a yığılmasın).</param>
    /// <param name="Prefer">Özellikle aranan özel kartlar (alımda ek değer).</param>
    public sealed record Build(string Id, string Name, Tag? Tag = null, Ability[]? Abilities = null, int LevelShift = 0, int? ReserveHigh = null,
        bool Hyper = false, bool Specials = true, double SpecialBonus = 0, Row? Row = null, Special[]? Prefer = null, double? SpecialMargin = null);

    /// <summary>
    /// Oyundaki bot koltuğunun düzeni: maçın tohumuyla karıştırılmış düzen listesinden sırayla (aynı maçta botlar farklı stratejiler
    /// dener). Tohum ve koltuktan türetilir; online kopyalarda aynıdır.
    /// </summary>
    public static Build BuildFor(int seed, int pid)
    {
        var rng = new Random(unchecked(seed * 92821 + 17));
        var order = Enumerable.Range(0, Meta.Builds.Length).Where(i => Meta.Builds[i].Id != "nospecial").OrderBy(_ => rng.Next()).ToArray();
        return Meta.Builds[order[pid % order.Length]];
    }

    /// <summary>
    /// Alt havuzda düzenin işe yarayan hâli: sınıfı bu maçta kapalıysa ya da satır düzeninin o satırda yeterli kartı yoksa (ör. Asker
    /// kapalıyken Kuşatma) açık bir sınıfın düzenine geçer (koltuktan belirlenir).
    /// </summary>
    public static Build? Effective(Match m, int pid, Build? build)
    {
        if ((build?.Tag is { } bt && !m.ActiveTags.Contains(bt))
            || (build?.Row is { } br && CardSet.All.Count(d => d.IsUnit && !d.IsHero && !d.Agile && d.Row == br && m.IsActive(d)) < 6))
        {
            var alt = Meta.Builds.Where(b => b.Tag is { } t && m.ActiveTags.Contains(t)).ToArray();
            return alt.Length > 0 ? alt[pid % alt.Length] : null;
        }
        return build;
    }

    /// <summary>
    /// Sınıflara yatırım: sahip olunan her farklı kart (açık sınıflarda) kademesi ve kopya sayısıyla tartılır; kahraman ağır sayar
    /// (kahramanın basamakları kendi sınıfının eşiğine bağlı).
    /// </summary>
    public static Dictionary<Tag, double> Commitment(Match m, PlayerState p)
    {
        var d = new Dictionary<Tag, double>();
        foreach (var g in p.Cards.Where(c => c.Def.IsUnit).GroupBy(c => c.Def.Id))
        {
            var def = g.First().Def;
            var copies = g.Sum(c => c.Star == 1 ? 1 : c.Star == 2 ? 3 : 9);
            var w = def.IsHero ? 3.0 : 1 + 0.25 * def.Tier + 0.4 * Math.Min(copies - 1, 4);
            foreach (var t in def.Tags) if (m.ActiveTags.Contains(t)) d[t] = d.GetValueOrDefault(t) + w;
        }
        return d;
    }

    /// <summary>
    /// Duruma göre yön (2026-11-24): bot başta seçtiği düzene körü körüne bağlı kalmaz. 4. round'dan sonra eline gelen kartlar
    /// (ve kahramanı) başka bir açık sınıfta belirgin biçimde ağır basıyorsa o sınıfa döner; sınıfsız düzenler (ekonomi, seviye,
    /// Boynuz, casus...) kendi tarzını korur ama en güçlü sınıfını da kovalar. Durumdan türetilir (hafızasız): online kopyalarda aynı.
    /// </summary>
    public static Build? Pivot(Match m, PlayerState p, Build? build)
    {
        if (m.Round < K.PivotRound) return build;
        var com = Commitment(m, p);
        if (com.Count == 0) return build;
        var (best, bv) = com.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).First();
        var cur = build?.Tag is { } bt ? com.GetValueOrDefault(bt) : 0;
        if (build?.Tag == best || bv < K.PivotMin || bv < cur * K.PivotRatio + 1) return build;
        var plain = Meta.Builds.FirstOrDefault(b => b.Tag == best && b.Abilities == null && b.Row == null && b.SpecialBonus == 0) ?? Meta.Builds.First(b => b.Tag == best);
        if (build == null || build.Tag != null) return plain;
        var prefer = (build.Prefer ?? Array.Empty<Special>()).Concat(plain.Prefer ?? Array.Empty<Special>()).Distinct().ToArray();
        return build with { Tag = best, Prefer = prefer.Length > 0 ? prefer : null };   // satır eğilimi (Menzilli/Kuşatma hattı) korunur
    }

    [ThreadStatic] private static Build? _b;

    // ---------- hazırlık ----------

    /// <param name="adapt">Duruma göre yön değiştirsin mi (<see cref="Pivot"/>). Oyunda açık; meta ölçümü düzeni sabit tutar.</param>
    public static void Prep(Match m, int pid, Random rng, Build? build, bool adapt = true)
    {
        var old = _b;
        build = Effective(m, pid, build);
        if (adapt) build = Pivot(m, m.Players[pid], build);
        _b = build;
        try { Prep(m, pid, rng); }
        finally { _b = old; }
    }

    public static void Prep(Match m, int pid, Random rng)
    {
        var p = m.Players[pid];
        if (!p.Alive || p.Ready || m.Phase != Phase.Prep) return;
        if (p.RewardOptions.Length > 0)
        {
            var best = Enumerable.Range(0, p.RewardOptions.Length).OrderByDescending(i => Want(m, p, p.RewardOptions[i])).First();
            if (!m.PickReward(pid, best)) { MakeRoom(m, p); m.PickReward(pid, best); }
        }
        if (p.PotionOptions.Length > 0)
            m.ChoosePotion(pid, Enumerable.Range(0, p.PotionOptions.Length).OrderByDescending(i => PotionValue(p, p.PotionOptions[i])).First());

        Arrange(m, p, false);
        BuyLoop(m, p);
        // seviye eğrisi
        while (p.Level < Tuning.BotCap && p.Level < TargetLevel(m, p) && p.Gold - Tuning.XpCost >= Reserve(m, p))
            if (!m.BuyXp(pid)) break;
        Arrange(m, p, false);
        BuyLoop(m, p);
        // yenileme: can azaldıysa ya da hedef seviyedeyken fazla altın varsa yükseltme ara
        var rolls = 0;
        while (rolls < 25 && WantRoll(m, p) && p.Gold - m.RerollCost(pid) >= RollFloor(m, p) && m.Reroll(pid))
        {
            rolls++;
            BuyLoop(m, p);
        }
        Arrange(m, p, true);
        m.SetReady(pid);
    }

    private static int TargetLevel(Match m, PlayerState p)
    {
        var r = m.Round;
        var t = Math.Clamp((r <= 2 ? 3 : r <= 4 ? 4 : r <= 7 ? 5 : r <= 10 ? 6 : r <= 14 ? 7 : 8) + K.LevelShift + (_b?.LevelShift ?? 0), 1, Tuning.BotCap);
        if (_b is { Hyper: true }) t = Math.Min(t, p.Hp < 35 ? 7 : 5);
        else if (p.Hp < 40 && t < Tuning.BotCap) t++;
        return t;
    }

    /// <summary>Faiz için tutulacak altın: can yüksekken 20 (azami faiz), azaldıkça erir.</summary>
    private static int Reserve(Match m, PlayerState p) =>
        m.Round < 3 ? 0 : p.Hp > 65 ? _b?.ReserveHigh ?? K.ReserveHigh : p.Hp > 40 ? (_b?.ReserveHigh is { } rh ? rh / 2 : K.ReserveMid) : 0;

    private static bool WantRoll(Match m, PlayerState p) =>
        (_b is { Hyper: true } && p.Level >= TargetLevel(m, p) && m.Round >= 5 && p.Gold >= 6) || (p.Hp < K.RollHp && m.Round >= 6) || (p.Level >= TargetLevel(m, p) && p.Gold >= K.RollAt) || (p.Level >= Tuning.BotCap && p.Gold >= 12);

    private static int RollFloor(Match m, PlayerState p) => p.Hp < 30 ? 0 : p.Hp < 50 ? Math.Min(10, Reserve(m, p)) : Reserve(m, p);

    private static double PotionValue(PlayerState p, Potion x) => x switch
    {
        Potion.Thunderbolt => 3, Potion.NorthernWind => 3.2, Potion.DancingStar => 2.6 + (p.School == School.Viper ? 1 : 0), Potion.BlackBlood => 2, _ => 1.2
    };

    /// <summary>Kartın kaba değeri (alım kararı için; dizilim motorla ölçülür).</summary>
    private static double Worth(PlayerState p, CardDef d, int star)
    {
        if (!d.IsUnit) return d.Special switch { Special.Horn => 3 + p.Level * 1.2, Special.Shield => 1.5, Special.Scorch => 2, _ => 2 + p.Level * 0.3 };
        double v = Tuning.Power(d, star);
        v += d.Ability switch { Ability.Bond => 2.5, Ability.Growth => 3, Ability.Bard => 3, Ability.Treasure => 2, Ability.Spy => 1.5, Ability.Medic => 2.5, Ability.Morale => 2.5, Ability.Ambush => 1.5, Ability.Revenge => 1.5, Ability.Zeal => 1.5, Ability.Plunder => 2, Ability.RowScorch => 2, Ability.Challenge => 1.5, Ability.Drain => 2,
            Ability.Trio => 1 + 3 * p.Cards.Where(c => c.Def.Ability == Ability.Trio && c.Def != d).Select(c => c.Def.Id).Distinct().Count(),
            Ability.Berserk => p.Cards.Any(c => c.Def.Special == Special.Mardroeme) ? Tuning.Power(d, star) * (Tuning.BerserkMult - 1) : 0.5, _ => 0 };
        if (d.IsHero) v += 10;
        return v;
    }

    /// <summary>Sahadaki ve depodaki sınıf sayıları (eşiğe yaklaşmak değerlidir).</summary>
    private static Dictionary<Tag, int> TagsOwned(PlayerState p)
    {
        var d = new Dictionary<Tag, int>();
        foreach (var c in p.Cards.Where(c => c.Def.IsUnit).GroupBy(c => c.Def.Id).Select(g => g.First()))
            foreach (var t in c.Def.Tags) d[t] = d.GetValueOrDefault(t) + 1;
        return d;
    }

    private static double Want(Match m, PlayerState p, CardDef d)
    {
        var fielded = p.Fielded.Where(c => c.Def.IsUnit).Select(c => Worth(p, c.Def, c.Star)).OrderBy(v => v).ToList();
        var full = p.Used >= p.Slots;
        var baseline = full && fielded.Count > 0 ? fielded[0] : 0;
        var score = Worth(p, d, 1) - baseline * 0.8;
        if (!d.IsUnit) return SpecialWant(m, p, d);
        if (d.IsHero) return 60;
        if (_b != null)
        {
            var on = (_b.Tag is { } bt && d.Has(bt)) || (_b.Abilities?.Contains(d.Ability) ?? false);
            if (on) score += 4 + d.Tier * 0.5;
            else if ((_b.Tag != null || _b.Abilities != null) && m.Round >= 6) score -= 1.5;
            if (_b.Hyper && d.Tier <= 2 && p.Cards.Any(c => c.Def == d)) score += 3;
            if (_b.Row is { } br)
            {
                if (d.Row == br) score += 3 + d.Tier * 0.4;
                else if (m.Round >= 5 && !(d.Agile && br != Row.Siege)) score -= 2;   // öteki satırlar yalnız dolgu
            }
        }
        var same1 = p.Cards.Count(c => c.Def == d && c.Star == 1);
        var same2 = p.Cards.Count(c => c.Def == d && c.Star == 2);
        // çift ve üçlü: yıldız atlama büyük sıçrama
        if (same1 == 2) score += 6 + Tuning.Power(d, 2) * 1.2;
        else if (same1 == 1) score += 2.5 + d.Tier * 0.8;
        if (same2 >= 1 && same1 >= 1) score += 4 + Tuning.Power(d, 3) * 0.6;
        // sınıf eşikleri: bir sonraki eşiğe bir kart kalmışsa
        var tags = TagsOwned(p);
        if (p.Cards.All(c => c.Def != d))
            foreach (var t in d.Tags)
            {
                var n = tags.GetValueOrDefault(t);
                if (Tuning.Breakpoints(t).Contains(n + 1)) score += 3 + n * 0.5;
                else if (n >= 2) score += 1;
            }
        // erken tier-1, geç yüksek tier
        score += (d.Tier - 1) * (m.Round >= 8 ? 1.0 : 0.3);
        if (m.Round >= 12 && d.Tier == 1 && same1 == 0 && same2 == 0) score -= 4;
        return score;
    }

    private static void BuyLoop(Match m, PlayerState p)
    {
        for (var guard = 0; guard < 12; guard++)
        {
            var best = -1;
            var bestScore = 0.0;
            for (var i = 0; i < p.Shop.Length; i++)
            {
                if (p.Shop[i] is not { } d || p.Gold < d.Price) continue;
                var sc = Want(m, p, d);
                var pairUp = p.Cards.Count(c => c.Def == d && c.Star == 1) >= 1;
                var floor = Reserve(m, p);
                // çift tamamlayan, kahraman ya da boş slotu dolduran kart faizi bozmaya değer
                if (!d.IsHero && !pairUp && p.Used >= p.Slots && p.Gold - d.Price < floor) continue;
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            if (best < 0 || bestScore < 1.2) return;
            if (!m.CanBuy(p.Id, best) && !MakeRoom(m, p)) return;
            if (!m.Buy(p.Id, best)) return;
        }
    }

    /// <summary>Depo doluysa çifti olmayan en değersiz depo kartını satar.</summary>
    private static bool MakeRoom(Match m, PlayerState p)
    {
        if (p.Bench.Count() < Tuning.BenchSize) return true;
        var junk = p.Bench.Where(c => p.Cards.Count(x => x.Def == c.Def) < 2).OrderBy(c => Worth(p, c.Def, c.Star)).FirstOrDefault()
                   ?? p.Bench.OrderBy(c => Worth(p, c.Def, c.Star)).First();
        return m.Sell(p.Id, junk.Uid);
    }

    /// <summary>Sahanın puanı: en iyi iki satır (ikinci daha ağır), üçüncü az; pusu esnek güç.</summary>
    private static double Score(Match m, PlayerState p)
    {
        var b = m.Preview(p.Id);
        var s = b.Sides[0];
        var rows = Enum.GetValues<Row>().Select(r => (double)b.PlainRow(0, r)).OrderByDescending(v => v).ToArray();
        var ambush = s.Ambush.Sum(u => b.Value(s, u)) * 0.85;
        var weatherHurt = 0.0;
        foreach (var sp in s.Specials)
        {
            foreach (var r in Match.WeatherRows(sp)) weatherHurt += s.In(r).Where(u => !u.IsHero && !u.Protected).Sum(u => Math.Max(0, b.Value(s, u) - 1)) * 0.8;
        }
        var rowBias = _b?.Row is { } br ? (double)b.PlainRow(0, br) * 0.6 : 0;
        return rows[0] * K.Top + rows[1] * K.Second + rows[2] * K.Third + ambush + rowBias - weatherHurt;
    }

    /// <summary>Yerel arama: her depo kartını boş yere, her (saha, depo) çiftini takas, çevik kartları öteki satıra, pusuyu dener; iyileşeni tutar.</summary>
    private static void Arrange(Match m, PlayerState p, bool specials)
    {
        ArrangeUnits(m, p);
        if (!specials) return;
        SpecialPass(m, p);
        ArrangeUnits(m, p);   // özel kartlar yer açtı/aldı, hava satırı değişti
    }

    private static void ArrangeUnits(Match m, PlayerState p)
    {
        var pid = p.Id;
        var best = Score(m, p);
        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            // depodan sahaya (boş slot)
            foreach (var c in p.Bench.Where(c => c.Def.IsUnit).ToList())
                foreach (var z in Match.ZonesFor(c.Def))
                {
                    if (!m.CanMove(pid, c.Uid, z) || m.WouldOverflow(pid, c.Uid, z)) continue;
                    m.Move(pid, c.Uid, z);
                    var s = Score(m, p);
                    if (s > best + 1e-6) { best = s; improved = true; break; }
                    m.Move(pid, c.Uid, Zone.Bench);
                }
            // takas: sahadaki kart depoya, depodaki onun yerine
            foreach (var f in p.Fielded.Where(c => c.Def.IsUnit).ToList())
            {
                if (!p.Cards.Contains(f) || !f.Fielded) continue;
                var fromZone = f.Zone;
                foreach (var c in p.Bench.Where(c => c.Def.IsUnit).ToList())
                {
                    if (!m.CanMove(pid, f.Uid, Zone.Bench) && p.Bench.Count() >= Tuning.BenchSize) break;
                    m.Move(pid, f.Uid, Zone.Bench);
                    var done = false;
                    foreach (var z in Match.ZonesFor(c.Def))
                    {
                        if (!m.CanMove(pid, c.Uid, z) || m.WouldOverflow(pid, c.Uid, z)) continue;
                        m.Move(pid, c.Uid, z);
                        var s = Score(m, p);
                        if (s > best + 1e-6) { best = s; improved = true; done = true; break; }
                        m.Move(pid, c.Uid, Zone.Bench);
                    }
                    if (done) break;
                    m.Move(pid, f.Uid, fromZone);
                }
            }
            // çevik ve pusu: başka bölgeyi dene
            foreach (var f in p.Fielded.Where(c => c.Def.IsUnit).ToList())
            {
                var zones = Match.ZonesFor(f.Def).Where(z => z != f.Zone).ToList();
                var from = f.Zone;
                foreach (var z in zones)
                {
                    if (!m.CanMove(pid, f.Uid, z) || m.WouldOverflow(pid, f.Uid, z)) continue;
                    m.Move(pid, f.Uid, z);
                    var s = Score(m, p);
                    if (s > best + 1e-6) { best = s; improved = true; break; }
                    m.Move(pid, f.Uid, from);
                }
            }
            if (!improved) break;
        }
    }

    // ---------- özel kartlar (simülasyonla) ----------

    /// <summary>Özel kartın alım değeri: sahaya konunca idman sahalarına karşı kazanma şansı ne kadar artıyor.</summary>
    private static double SpecialWant(Match m, PlayerState p, CardDef d)
    {
        if (_b is { Specials: false } || (K.Allowed != null && !K.Allowed.Contains(d.Special))) return -10;
        if (p.Cards.Any(c => c.Def == d)) return -5;
        var spars = BotSim.Spars(m, p);
        var cur = Val(m, p, spars);
        var tmp = new OwnedCard { Uid = int.MinValue + 7, Def = d };
        p.Cards.Add(tmp);
        double best;
        try { best = Explore(m, p, tmp, spars, cur, false); }
        finally { p.Cards.Remove(tmp); }
        var bonus = (_b?.SpecialBonus ?? 0) + (_b?.Prefer?.Contains(d.Special) == true ? 5 : 0);
        return (best - cur) * K.SpecialGain + bonus - 0.5;
    }

    /// <summary>
    /// Özel kartları idman simülasyonuyla yerleştirir: her özel kart için sahaya koy (Boynuz/Kalkan: her satır; yer yoksa en
    /// zayıf üç birimden biri depoya), kaldır ya da hedefini değiştir; kazanma şansını artıranı tutar.
    /// </summary>
    private static void SpecialPass(Match m, PlayerState p)
    {
        var specials = p.Cards.Where(c => !c.Def.IsUnit).ToList();
        if (specials.Count == 0) return;
        if (_b is { Specials: false })
        {
            foreach (var c in specials.Where(c => c.Fielded)) m.Move(p.Id, c.Uid, Zone.Bench);
            return;
        }
        // aynı özel karttan ikincisi işe yaramaz (hava ve Kavurma birikmez): fazlası depoya, yalnız biri denenir
        foreach (var g in specials.Where(c => c.Fielded).GroupBy(c => c.Def.Id))
            foreach (var extra in g.Skip(1)) m.Move(p.Id, extra.Uid, Zone.Bench);
        var spars = BotSim.Spars(m, p);
        var cur = Val(m, p, spars);
        for (var pass = 0; pass < 2; pass++)
        {
            var before = cur;
            foreach (var c in specials.GroupBy(c => c.Def.Id).Select(g => g.OrderByDescending(x => x.Fielded).First()))
                cur = Explore(m, p, c, spars, cur, true);
            if (cur <= before + 1e-9) break;
        }
    }

    /// <summary>
    /// Kararın puanı: idman kazanma şansı, sahadaki her özel kart için <see cref="K.SpecialMargin"/> eksik (simülasyon işaret,
    /// simya ve pusuyu görmez; ölçümde özel kartın faydasını gerçekte olduğundan büyük buluyordu).
    /// </summary>
    private static double Val(Match m, PlayerState p, List<BotSim.Spar> spars) =>
        BotSim.WinChance(m, p, spars) - (_b?.SpecialMargin ?? K.SpecialMargin) * p.Cards.Count(c => c.Zone == Zone.Special);

    /// <summary>
    /// Bir özel kartın en iyi yerini arar; <paramref name="commit"/> ise en iyisini uygular (iyileşme yoksa durum aynen kalır).
    /// Dönen değer en iyi kazanma şansıdır (en az <paramref name="cur"/>).
    /// </summary>
    private static double Explore(Match m, PlayerState p, OwnedCard sc, List<BotSim.Spar> spars, double cur, bool commit)
    {
        const double Eps = 0.004;
        var pid = p.Id;
        var bestV = cur;
        Action? bestDo = null;
        var aims = Match.Aimable(sc.Def) ? Enum.GetValues<Row>().Cast<Row?>().ToArray() : new Row?[] { null };
        var (z0, a0) = (sc.Zone, sc.Aim);

        void Try(Action apply, Action undo)
        {
            apply();
            var v = Val(m, p, spars);
            undo();
            if (v > bestV + Eps) { bestV = v; bestDo = apply; }
        }

        if (sc.Fielded)
        {
            // kaldır: yerine depodaki en değerli birimlerden biri (yoksa boş kalır)
            if (p.Bench.Count() < Tuning.BenchSize)
            {
                Try(() => m.Move(pid, sc.Uid, Zone.Bench), () => { sc.Zone = z0; sc.Aim = a0; });
                foreach (var u in p.Bench.Where(c => c.Def.IsUnit).OrderByDescending(c => Worth(p, c.Def, c.Star)).Take(3).ToList())
                    foreach (var z in Match.ZonesFor(u.Def).Where(z => z != Zone.Ambush))
                        Try(() => { sc.Zone = Zone.Bench; sc.Aim = null; u.Zone = z; }, () => { u.Zone = Zone.Bench; sc.Zone = z0; sc.Aim = a0; });
            }
            foreach (var a in aims.Where(a => a != null && a != a0))
                Try(() => m.Move(pid, sc.Uid, Zone.Special, a), () => { sc.Zone = z0; sc.Aim = a0; });
        }
        else if (p.Used < p.Slots)
        {
            foreach (var a in aims)
                Try(() => m.Move(pid, sc.Uid, Zone.Special, a), () => { sc.Zone = z0; sc.Aim = a0; });
        }
        else
        {
            // yer yok: en zayıf (kahraman olmayan) birimlerden biri depoya
            var weak = p.Cards.Where(c => c.TakesSlot && c.Def.IsUnit && !c.Def.IsHero).OrderBy(c => Worth(p, c.Def, c.Star)).Take(3).ToList();
            if (weak.Count > 0)
                foreach (var w in weak)
                {
                    var wz = w.Zone;
                    foreach (var a in aims)
                        Try(() => { w.Zone = Zone.Bench; sc.Zone = Zone.Special; sc.Aim = a; },
                            () => { sc.Zone = z0; sc.Aim = a0; w.Zone = wz; });
                }
        }
        if (commit && bestDo != null) bestDo();
        return bestV;
    }

    // ---------- savaş ----------

    /// <summary>
    /// Pencere kararı (2026-11-24). Meta ölçümünde eski karar ("Uyarlanır", ort. sıra 4,53) "hep Aard"tan (4,05) ve "Igni
    /// öncelikli"den (4,09) kötüydü: Odak biriktiriyor, savaş sonunda Odak artıyordu, Aard'ı neredeyse hiç seçmiyordu (%2).
    /// Bu karar her işaretin satıra beklenen etkisini (puan) Odak bedeliyle tartar, Odak'ı kalan pencerelere yayar ama
    /// biriktirmez; rakibin olası cevabını (Quen, Yrden) yalnız Odak'ından tahmin eder (gizli bilgi yok).
    /// </summary>
    public static WindowChoice SmartWindow(Battle b, int side, Random rng)
    {
        if (b.Pve && b.NoDamage && b.Sides[side].PlayerId < 0) return WindowChoice.None;
        var me = b.Sides[side];
        var op = b.Sides[1 - side];
        var row = b.CurrentRow;
        var w = b.Window;
        var own = (double)b.PlainRow(side, row);
        var seen = Enumerable.Range(0, w).Select(i => (double)op.RowValues[i]).ToList();
        var myAvg = Enum.GetValues<Row>().Average(r => (double)b.PlainRow(side, r));
        var est = seen.Count > 0 ? 0.6 * seen.Average() + 0.4 * myAvg : myAvg;
        var spied = b.Revealed[side].Where(x => x.Zone == Texts.Name(row)).Select(x => (double)Tuning.Power(x.Def, x.Star)).ToList();
        est = Math.Max(Math.Max(1, est), spied.Sum());
        var (mine, theirs) = (me.Points, op.Points);
        var last = w == 2;
        var dead = last && Math.Abs(mine - theirs) == 2;
        if (dead) return Fallback(b, side, WindowChoice.None);
        var decisive = (last && mine == theirs) || (w == 1 && (mine == 1 || theirs == 1));
        var margin = (own - est) / Math.Max(4, est);
        var remaining = 3 - w;
        // Odak: belirleyici ve son pencerede hepsi; yoksa bir sonraki pencereye en az 1 bırak (kaybedilen satır +1 de getirir)
        var budget = last || decisive ? me.Focus : Math.Max(1, me.Focus - (remaining > 1 ? 1 : 0));
        // satır umutsuz ya da çok rahatsa işaret boşa: Odak sonraki pencereye
        if (!last && !decisive && Math.Abs(margin) > 0.9) budget = 0;
        var amp = Texts.SignAmp(me.School, me.Th(Tag.Mage)).Amp;
        var strongest = Math.Max(spied.DefaultIfEmpty(0).Max(), est * 0.4);
        var ownStrongest = b.Sides[side].In(row).Select(u => (double)b.Value(me, u)).DefaultIfEmpty(0).Max();
        var pQuen = op.Focus >= 1 ? 0.3 : 0;
        var pIgni = op.Focus >= 2 ? 0.35 : 0;
        var pAard = op.Focus >= 1 ? 0.3 : 0;
        var pYrden = op.Focus >= 2 ? 0.08 : 0;
        var ambushThreat = b.AmbushSeen[side] is > 0 || (last && op.Ambush.Count > 0);
        // satır farkı küçüldükçe etki daha değerli (kazanmayı çevirir)
        var swing = 0.5 + Math.Exp(-Math.Pow(margin / 0.5, 2));
        var vals = new Dictionary<Sign, double>
        {
            [Sign.Aard] = 0.10 * amp * est * (1 - pYrden),
            [Sign.Igni] = 0.5 * amp * strongest * (1 - pQuen),
            [Sign.Quen] = pIgni * 0.5 * ownStrongest * (margin > -0.2 ? 1 : 0.4),
            [Sign.Yrden] = ambushThreat ? 0.45 * est : Tuning.YrdenTrapPct * amp * est + pAard * 0.10 * own,
        };
        Sign? sign = null;
        var best = 0.0;
        foreach (var (sg, v) in vals)
        {
            var c = new WindowChoice(new[] { sg });
            var cost = b.Cost(side, c);
            if (cost > budget || !b.Valid(side, c)) continue;
            // Odak'ın fırsat bedeli: kalan pencere varken pahalı işaret biraz cezalı
            var score = v * swing - cost * (last ? 0 : 0.04 * est) + rng.NextDouble() * 0.01;
            if (score > best) { best = score; sign = sg; }
        }

        Potion? potion = null;
        if (me.Potions.Count > 0 && (decisive || last || Math.Abs(margin) < 0.35))
        {
            if (b.Weather.Contains(row) && me.Potions.Contains(Potion.Swallow)) potion = Potion.Swallow;
            else if (margin > 0 && op.Focus >= 2 && me.Potions.Contains(Potion.BlackBlood)) potion = Potion.BlackBlood;
            else foreach (var x in new[] { Potion.NorthernWind, Potion.Thunderbolt, Potion.DancingStar }) if (me.Potions.Contains(x)) { potion = x; break; }
        }
        var ambush = new List<int>();
        if (me.Ambush.Count > 0 && last) ambush.AddRange(me.Ambush.Select(u => u.Uid));
        else if (me.Ambush.Count > 0 && (decisive || (margin < 0.15 && margin > -0.6)))
            ambush.Add(me.Ambush.OrderByDescending(u => u.Power).First().Uid);
        return Fallback(b, side, WindowChoice.With(sign is { } s2 ? new[] { s2 } : Array.Empty<Sign>(), potion, ambush));
    }

    private static WindowChoice Fallback(Battle b, int side, WindowChoice choice)
    {
        if (b.Valid(side, choice)) return choice;
        var c = WindowChoice.With(choice.Signs, null, choice.AmbushUids);
        if (b.Valid(side, c)) return c;
        c = WindowChoice.With(Array.Empty<Sign>(), choice.Potion, choice.AmbushUids);
        return b.Valid(side, c) ? c : WindowChoice.None;
    }

    /// <summary>Pencere kararı (açık bilgiyle). Rastgelelik yalnız eşitlikleri bozar.</summary>
    public static WindowChoice Window(Battle b, int side, Random rng)
    {
        if (b.Pve && b.NoDamage && b.Sides[side].PlayerId < 0) return WindowChoice.None;
        if (K.WindowMode == 1) return SmartWindow(b, side, rng);
        if (!K.ExpertWindows) return HanBot.Window(b, side, rng, K.Noise);   // ölçüm: klasik pencere kararı daha iyi çıktı
        var me = b.Sides[side];
        var op = b.Sides[1 - side];
        var row = b.CurrentRow;
        var own = (double)b.PlainRow(side, row);
        // rakibin bu satırını tahmin: açılmış satırlarının ortalaması, yoksa kendi ortalamam; casusun gösterdikleri en azından
        var seen = Enumerable.Range(0, b.Window).Select(i => (double)op.RowValues[i]).ToList();
        var myAvg = Enum.GetValues<Row>().Average(r => (double)b.PlainRow(side, r));
        var est = seen.Count > 0 ? 0.6 * seen.Average() + 0.4 * myAvg : myAvg;
        var spied = b.Revealed[side].Where(x => x.Zone == Texts.Name(row)).Sum(x => Tuning.Power(x.Def, x.Star));
        est = Math.Max(Math.Max(1, est), spied);
        var margin = (own - est) / Math.Max(4, est);
        var w = b.Window;
        var mine = me.Points;
        var theirs = op.Points;
        // satırın önemi: kaybedersem/kazanırsam savaş biter mi
        var decisive = (w == 2 && mine == theirs) || (w == 1 && (mine == 1 || theirs == 1)) || (w == 2 && Math.Abs(mine - theirs) <= 1 && mine + theirs < 2);
        var dead = w == 2 && Math.Abs(mine - theirs) == 2;   // sonuç belli: yalnız toplam için
        var importance = dead ? 0.25 : decisive ? 1.6 : 1.0;
        var close = Math.Exp(-Math.Pow(margin / 0.45, 2));   // fark küçükse müdahale değerli
        var remaining = 3 - w;
        // Odak bütçesi: kalan pencerelere yay, belirleyici satırda hepsini kullanabilir
        var budget = decisive ? me.Focus : Math.Max(1, (int)Math.Ceiling(me.Focus / (double)remaining));
        var opIgniThreat = op.Focus >= 2 ? 0.45 : 0;
        var opAny = op.Focus >= 1;

        Sign? sign = null;
        var bestGain = 0.25;
        void Consider(Sign s, double gain)
        {
            if (b.Cost(side, new WindowChoice(new[] { s })) > budget || !b.Valid(side, new WindowChoice(new[] { s }))) return;
            gain *= 1 + rng.NextDouble() * 0.08;
            if (gain > bestGain) { bestGain = gain; sign = s; }
        }
        var strongestTheirs = Math.Max(2, est * 0.45);   // satırdaki en güçlü kartın kaba payı
        // kazanma olasılığındaki artış ≈ yakınlık × etkinin farka oranı
        Consider(Sign.Igni, importance * close * (strongestTheirs * 0.5) * (1 - (op.Focus >= 1 ? 0.35 : 0)) / Math.Max(3, est) * 10 - 1.0);
        Consider(Sign.Aard, importance * close * (est * 0.1 + 1) / Math.Max(3, est) * 10 - 0.6);
        if (margin > -0.1) Consider(Sign.Quen, importance * opIgniThreat * (own * 0.25) / Math.Max(3, own) * 10 * Math.Max(close, 0.5) - 0.4);
        if (op.Potions.Count > 0 || b.AmbushSeen[side] is > 0 || (w == 2 && op.Ambush.Count > 0))
            Consider(Sign.Yrden, importance * close * (op.Potions.Count > 0 ? 2.2 : 1.6) - 0.6);
        else   // tuzak: Aard kadar vurur, rakibin Aard'ını da tutar (2 Odak)
            Consider(Sign.Yrden, importance * close * (est * Tuning.YrdenTrapPct + 1) / Math.Max(3, est) * 10 - 1.1 + (opAny ? 0.3 : 0));

        Potion? potion = null;
        if (me.Potions.Count > 0 && importance >= 1 && close > 0.35)
        {
            Potion? pick = null;
            if (b.Weather.Contains(row) && me.Potions.Contains(Potion.Swallow)) pick = Potion.Swallow;
            else if (margin > 0 && op.Focus >= 2 && me.Potions.Contains(Potion.BlackBlood)) pick = Potion.BlackBlood;
            else foreach (var x in new[] { Potion.NorthernWind, Potion.Thunderbolt, Potion.DancingStar }) if (me.Potions.Contains(x)) { pick = x; break; }
            if (pick is { } pk && (pk != Potion.Swallow || b.Weather.Contains(row))) potion = pk;
        }
        else if (me.Potions.Count > 0 && w == 2 && !dead)
            potion = me.Potions.Where(x => x is Potion.Thunderbolt or Potion.NorthernWind or Potion.DancingStar).Cast<Potion?>().FirstOrDefault();   // son satırda elde kalmasın

        var ambush = new List<int>();
        if (me.Ambush.Count > 0 && w == 2) ambush.AddRange(me.Ambush.Select(u => u.Uid));   // son pencerede pusu kendiliğinden inmez
        else if (me.Ambush.Count > 0 && (decisive || (margin < 0.15 && margin > -0.6)))
            ambush.Add(me.Ambush.OrderByDescending(u => u.Power).First().Uid);

        var choice = WindowChoice.With(sign is { } sg ? new[] { sg } : Array.Empty<Sign>(), potion, ambush);
        if (b.Valid(side, choice)) return choice;
        choice = WindowChoice.With(choice.Signs, null, ambush);
        if (b.Valid(side, choice)) return choice;
        choice = WindowChoice.With(Array.Empty<Sign>(), potion, ambush);
        return b.Valid(side, choice) ? choice : WindowChoice.None;
    }
}
