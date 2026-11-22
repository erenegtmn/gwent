namespace HanLigi;

/// <summary>
/// Botun özel kart (hava, Kavurma, Boynuz, Kalkan) kararları için küçük simülasyon. Rakibin sahası gizli olduğundan bot
/// "tipik rakip" sahaları (idman sahası) kurar: kendi seviyesinin market olasılıklarıyla rastgele kartlar, round ilerledikçe 2★,
/// bazılarında rakibin havası. Kendi sahasını bunlara karşı motorun savaş öncesi çözümüyle (<see cref="Battle.Start"/>:
/// hava, Kavurma, Kalkan, Şifacı, Boynuz) dener ve satır olasılıklarından savaş kazanma şansını çıkarır. Yalnız açık bilgi ve
/// tohum kullanır: aynı durum aynı kararı verir (online kopyalar aynı kalır).
/// </summary>
internal static class BotSim
{
    /// <param name="Scale">Güç çarpanı: idman sahası botun kendi ham gücüne eşitlenir ("rakip aşağı yukarı benim kadar güçlü").</param>
    internal sealed record Spar(List<(CardDef Def, int Star)> Units, Special Weather, double Scale);

    private static readonly CardDef[][] UnitsByTier = Enumerable.Range(1, 5)
        .Select(t => CardSet.All.Where(d => d.IsUnit && !d.IsHero && d.Tier == t).ToArray()).ToArray();

    /// <summary>Bu round için idman sahaları (oyuncuya ve round'a göre tohumlu).</summary>
    /// <summary>İdman sahalarından kaçı botun kendi sahasının aynası (ölçümle seçilir).</summary>
    internal static int Mirrors = 2;

    internal static List<Spar> Spars(Match m, PlayerState p, int count = 4)
    {
        var list = new List<Spar>();
        var odds = Tuning.ShopOdds[Math.Clamp(p.Level, 1, Tuning.MaxLevel) - 1];
        var star2 = Math.Clamp((m.Round - 3) * 0.07, 0, 0.55);
        double mine = p.Fielded.Where(c => c.Def.IsUnit).Sum(c => c.Power);
        for (var k = 0; k < count; k++)
        {
            var rng = new Random(unchecked(m.Seed * 7919 + m.Round * 104729 + p.Id * 31 + k * 7));
            var units = new List<(CardDef, int)>();
            if (k < Mirrors && mine > 0)
            {
                // ayna: rakipler de benzer kurar; kartların bir kısmı aynı tier'dan başka kartla değişir
                foreach (var c in p.Fielded.Where(c => c.Def.IsUnit))
                {
                    var d = c.Def;
                    if (!d.IsHero && rng.NextDouble() < 0.35) { var pool0 = UnitsByTier[d.Tier - 1]; d = pool0[rng.Next(pool0.Length)]; }
                    units.Add((d, c.Star));
                }
                list.Add(new Spar(units, Special.None, 1.0));
                continue;
            }
            for (var i = 0; i < Tuning.Slots(p.Level); i++)
            {
                var roll = rng.Next(100);
                var tier = 1;
                for (var acc = 0; tier <= 5; tier++) { acc += odds[tier - 1]; if (roll < acc) break; }
                var pool = UnitsByTier[Math.Min(tier, 5) - 1];
                // rakip de seçerek alır: iki adaydan güçlüsü
                var a = pool[rng.Next(pool.Length)];
                var b = pool[rng.Next(pool.Length)];
                units.Add((a.Power >= b.Power ? a : b, rng.NextDouble() < star2 ? 2 : 1));
            }
            // rakiplerin bir kısmı hava taşır (Ayaz en sık: Yakın satırı en kalabalık)
            var w = m.Round >= 5 && rng.NextDouble() < 0.3 ? (rng.Next(4) switch { 0 or 1 => Special.Frost, 2 => Special.Fog, _ => Special.Rain }) : Special.None;
            double raw = units.Sum(u => Tuning.Power(u.Item1, u.Item2));
            list.Add(new Spar(units, w, mine > 0 && raw > 0 ? Math.Clamp(mine / raw, 0.5, 2.0) : 1.0));
        }
        return list;
    }

    internal static BattleSide SideOf(Spar s, int level)
    {
        var side = new BattleSide { Name = "idman", Level = level };
        var uid = 800000;
        foreach (var (d, star) in s.Units)
            side.Units.Add(new BattleUnit { Uid = uid++, Def = d, Star = star, Power = Math.Max(1, Tuning.Round(Tuning.Power(d, star) * s.Scale)), Row = d.Row });
        foreach (var d in s.Units.Select(u => u.Def).Distinct())
            foreach (var t in d.Tags) side.Tags[t] = side.Tags.GetValueOrDefault(t) + 1;
        if (s.Weather != Special.None) side.Specials.Add(s.Weather);
        return side;
    }

    /// <summary>Satırı kazanma olasılığı: fark ortalamanın ~%20'si iken ~%84 (işaret ve simya belirsizliği).</summary>
    private static double RowP(double mine, double theirs)
    {
        var scale = Math.Max(1.5, 0.12 * (mine + theirs) / 2);
        return 1 / (1 + Math.Exp(-(mine - theirs) / scale));
    }

    internal static double WinChanceVs(Match m, PlayerState p, Func<BattleSide> foe)
    {
        var b = m.PreviewVs(p.Id, foe());
        b.Start();
        var ps = Enum.GetValues<Row>().Select(r => RowP((double)b.PlainRow(0, r), (double)b.PlainRow(1, r))).ToArray();
        return ps[0] * ps[1] + ps[0] * ps[2] + ps[1] * ps[2] - 2 * ps[0] * ps[1] * ps[2];
    }

    /// <summary>Şu anki sahayla idman sahalarına karşı ortalama savaş kazanma şansı (üç satırın en az ikisi).</summary>
    internal static double WinChance(Match m, PlayerState p, List<Spar> spars)
    {
        var tot = 0.0;
        foreach (var s in spars)
        {
            var b = m.PreviewVs(p.Id, SideOf(s, p.Level));
            b.Start();
            var ps = Enum.GetValues<Row>().Select(r => RowP((double)b.PlainRow(0, r), (double)b.PlainRow(1, r))).ToArray();
            tot += ps[0] * ps[1] + ps[0] * ps[2] + ps[1] * ps[2] - 2 * ps[0] * ps[1] * ps[2];
        }
        return tot / spars.Count;
    }
}

/// <summary>Ölçüm yardımcıları (arena): idman sahasının gücü gerçek sahayla kıyaslanır.</summary>
public static class BotSimDebug
{
    /// <summary>(kendi üç satır toplamı, idman sahalarının ortalama üç satır toplamı) — rakipsiz ön izleme değerleriyle.</summary>
    public static (double Mine, double Spar) Strength(Match m, int pid)
    {
        var p = m.Players[pid];
        var spars = BotSim.Spars(m, p);
        double mine = 0, spar = 0;
        foreach (var s in spars)
        {
            var b = m.PreviewVs(pid, BotSim.SideOf(s, p.Level));
            mine += Enum.GetValues<Row>().Sum(r => (double)b.PlainRow(0, r));
            spar += Enum.GetValues<Row>().Sum(r => (double)b.PlainRow(1, r));
        }
        return (mine / spars.Count, spar / spars.Count);
    }

    /// <summary>
    /// Özel kart kararının sınaması: sahadaki her özel kart için (kart sahada − kart depoda) kazanma şansı farkı, idman sahalarına
    /// ve gerçek rakibe (<paramref name="foe"/>) karşı. Durumu değiştirmez.
    /// </summary>
    public static List<(Special Sp, double Spar, double Real)> Check(Match m, int pid, int foe)
    {
        var p = m.Players[pid];
        var res = new List<(Special, double, double)>();
        var spars = BotSim.Spars(m, p);
        double Real() => BotSim.WinChanceVs(m, p, () => m.Snapshot(m.Players[foe]));
        foreach (var c in p.Cards.Where(c => c.Zone == Zone.Special).ToList())
        {
            var s1 = BotSim.WinChance(m, p, spars);
            var r1 = Real();
            var aim = c.Aim;
            c.Zone = Zone.Bench;
            var s0 = BotSim.WinChance(m, p, spars);
            var r0 = Real();
            c.Zone = Zone.Special;
            c.Aim = aim;
            res.Add((c.Def.Special, s1 - s0, r1 - r0));
        }
        return res;
    }
}
