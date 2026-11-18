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
    }

    /// <summary>
    /// Düzen (build) eğilimi: meta ölçümü için botun belli bir stratejiye bağlanması (<see cref="Meta"/>). Varsayılan (null) dengeli
    /// oyundur. Sınıf / yetenek odağı alımda ağırlık verir; seviye kayması, faiz birikimi, hiper-yenileme (düşük seviyede kalıp 1-2
    /// tier üçlü arama) ekonomiyi değiştirir.
    /// </summary>
    public sealed record Build(string Id, string Name, Tag? Tag = null, Ability[]? Abilities = null, int LevelShift = 0, int? ReserveHigh = null,
        bool Hyper = false, bool Specials = true, double SpecialBonus = 0);

    [ThreadStatic] private static Build? _b;

    // ---------- hazırlık ----------

    public static void Prep(Match m, int pid, Random rng, Build? build)
    {
        var old = _b;
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

        Arrange(m, p);
        BuyLoop(m, p);
        // seviye eğrisi
        while (p.Level < Tuning.MaxLevel && p.Level < TargetLevel(m, p) && p.Gold - Tuning.XpCost >= Reserve(m, p))
            if (!m.BuyXp(pid)) break;
        Arrange(m, p);
        BuyLoop(m, p);
        // yenileme: can azaldıysa ya da hedef seviyedeyken fazla altın varsa yükseltme ara
        var rolls = 0;
        while (rolls < 25 && WantRoll(m, p) && p.Gold - m.RerollCost(pid) >= RollFloor(m, p) && m.Reroll(pid))
        {
            rolls++;
            BuyLoop(m, p);
        }
        Arrange(m, p);
        m.SetReady(pid);
    }

    private static int TargetLevel(Match m, PlayerState p)
    {
        var r = m.Round;
        var t = Math.Clamp((r <= 2 ? 3 : r <= 4 ? 4 : r <= 7 ? 5 : r <= 10 ? 6 : r <= 14 ? 7 : 8) + K.LevelShift + (_b?.LevelShift ?? 0), 1, Tuning.MaxLevel);
        if (_b is { Hyper: true }) t = Math.Min(t, p.Hp < 35 ? 7 : 5);
        else if (p.Hp < 40 && t < Tuning.MaxLevel) t++;
        return t;
    }

    /// <summary>Faiz için tutulacak altın: can yüksekken 20 (azami faiz), azaldıkça erir.</summary>
    private static int Reserve(Match m, PlayerState p) =>
        m.Round < 3 ? 0 : p.Hp > 65 ? _b?.ReserveHigh ?? K.ReserveHigh : p.Hp > 40 ? (_b?.ReserveHigh is { } rh ? rh / 2 : K.ReserveMid) : 0;

    private static bool WantRoll(Match m, PlayerState p) =>
        (_b is { Hyper: true } && p.Level >= TargetLevel(m, p) && m.Round >= 5 && p.Gold >= 6) || (p.Hp < K.RollHp && m.Round >= 6) || (p.Level >= TargetLevel(m, p) && p.Gold >= K.RollAt) || (p.Level == Tuning.MaxLevel && p.Gold >= 12);

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
        v += d.Ability switch { Ability.Bond => 2.5, Ability.Growth => 3, Ability.Bard => 3, Ability.Treasure => 2, Ability.Spy => 1.5, Ability.Medic => 2.5, Ability.Morale => 2.5, Ability.Ambush => 1.5, Ability.Revenge => 1.5, Ability.Zeal => 1.5, _ => 0 };
        if (d.IsHero) v += 10;
        return v;
    }

    /// <summary>Sahadaki ve depodaki sınıf sayıları (eşiğe yaklaşmak değerlidir).</summary>
    private static Dictionary<Tag, int> TagsOwned(PlayerState p)
    {
        var d = new Dictionary<Tag, int>();
        foreach (var c in p.Cards.Where(c => c.Def.IsUnit && !c.Def.IsHero).GroupBy(c => c.Def.Id).Select(g => g.First()))
            foreach (var t in c.Def.Tags) d[t] = d.GetValueOrDefault(t) + 1;
        return d;
    }

    private static double Want(Match m, PlayerState p, CardDef d)
    {
        var fielded = p.Fielded.Where(c => c.Def.IsUnit).Select(c => Worth(p, c.Def, c.Star)).OrderBy(v => v).ToList();
        var full = p.Used >= p.Slots;
        var baseline = full && fielded.Count > 0 ? fielded[0] : 0;
        var score = Worth(p, d, 1) - baseline * 0.8;
        if (!d.IsUnit) return p.Cards.Any(c => c.Def == d) ? -5 : _b is { Specials: false } ? -10 : score - 1 + (_b?.SpecialBonus ?? 0);
        if (d.IsHero) return 60;
        if (_b != null)
        {
            var on = (_b.Tag is { } bt && d.Has(bt)) || (_b.Abilities?.Contains(d.Ability) ?? false);
            if (on) score += 4 + d.Tier * 0.5;
            else if ((_b.Tag != null || _b.Abilities != null) && m.Round >= 6) score -= 1.5;
            if (_b.Hyper && d.Tier <= 2 && p.Cards.Any(c => c.Def == d)) score += 3;
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
        var weather = s.Specials.Count(x => x is Special.Frost or Special.Fog or Special.Rain) * (1.5 + p.Level * 0.4);
        var weatherHurt = 0.0;
        foreach (var sp in s.Specials)
        {
            Row? wr = sp switch { Special.Frost => Row.Close, Special.Fog => Row.Ranged, Special.Rain => Row.Siege, _ => null };
            if (wr is { } r) weatherHurt += s.In(r).Where(u => !u.IsHero && !u.Protected).Sum(u => Math.Max(0, b.Value(s, u) - 1)) * 0.8;
        }
        return rows[0] * K.Top + rows[1] * K.Second + rows[2] * K.Third + ambush + weather - weatherHurt;
    }

    /// <summary>Yerel arama: her depo kartını boş yere, her (saha, depo) çiftini takas, çevik kartları öteki satıra, pusuyu dener; iyileşeni tutar.</summary>
    private static void Arrange(Match m, PlayerState p)
    {
        var pid = p.Id;
        var best = Score(m, p);
        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            // depodan sahaya (boş slot)
            foreach (var c in p.Bench.ToList())
                foreach (var z in Match.ZonesFor(c.Def))
                {
                    if (!m.CanMove(pid, c.Uid, z) || m.WouldOverflow(pid, c.Uid, z)) continue;
                    m.Move(pid, c.Uid, z);
                    var s = Score(m, p);
                    if (s > best + 1e-6) { best = s; improved = true; break; }
                    m.Move(pid, c.Uid, Zone.Bench);
                }
            // takas: sahadaki kart depoya, depodaki onun yerine
            foreach (var f in p.Fielded.ToList())
            {
                if (!p.Cards.Contains(f) || !f.Fielded) continue;
                var fromZone = f.Zone;
                foreach (var c in p.Bench.ToList())
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
            foreach (var f in p.Fielded.ToList())
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

    // ---------- savaş ----------

    /// <summary>Pencere kararı (açık bilgiyle). Rastgelelik yalnız eşitlikleri bozar.</summary>
    public static WindowChoice Window(Battle b, int side, Random rng)
    {
        if (b.Pve && b.NoDamage && b.Sides[side].PlayerId < 0) return WindowChoice.None;
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

        int? ambush = null;
        if (me.Ambush.Count > 0 && (decisive || w == 2 || (margin < 0.15 && margin > -0.6)))
            ambush = me.Ambush.OrderByDescending(u => u.Power).First().Uid;

        var choice = new WindowChoice(sign is { } sg ? new[] { sg } : Array.Empty<Sign>(), potion, ambush);
        if (b.Valid(side, choice)) return choice;
        choice = new WindowChoice(choice.Signs, null, ambush);
        if (b.Valid(side, choice)) return choice;
        choice = new WindowChoice(Array.Empty<Sign>(), potion, ambush);
        return b.Valid(side, choice) ? choice : WindowChoice.None;
    }
}
