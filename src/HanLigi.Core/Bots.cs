namespace HanLigi;

/// <summary>
/// Basit yapay zekâ: hazırlık (alışveriş, seviye, saha dizimi) ve savaş pencereleri. Rakibin gizli bilgisini kullanmaz:
/// yalnız kendi sahası, açılmış satırlar, rakibin Odak'ı ve simya kartı sayısı.
/// </summary>
public static class HanBot
{
    public enum Style { Balanced, Leveler, Economy, Reroll }

    public static Style StyleOf(Match m, int pid) => (Style)(Math.Abs(m.Seed + pid * 7) % 4);

    // ---------- hazırlık ----------

    public static void Prep(Match m, int pid, Random rng)
    {
        var p = m.Players[pid];
        if (!p.Alive || p.Ready || m.Phase != Phase.Prep) return;
        var style = StyleOf(m, pid);
        if (p.RewardOptions.Length > 0)
        {
            var best = Enumerable.Range(0, p.RewardOptions.Length).OrderByDescending(i => Want(p, p.RewardOptions[i], style)).First();
            if (!m.PickReward(pid, best)) { MakeRoom(m, p); m.PickReward(pid, best); }
        }
        if (p.PotionOptions.Length > 0) m.ChoosePotion(pid, rng.Next(p.PotionOptions.Length));

        var rerolls = 0;
        while (true)
        {
            BuyLoop(m, p, style);
            while (p.Cards.Count >= p.Slots && p.Level < Tuning.BotCap && p.Level < TargetLevel(m, style) && p.Gold - Tuning.XpCost >= Reserve(m, style))
                if (!m.BuyXp(pid)) break;
            BuyLoop(m, p, style);
            var can = p.Gold - m.RerollCost(pid) >= Reserve(m, style) && rerolls < 12;
            var go = style switch
            {
                Style.Reroll => can && m.Round >= 5 && p.Gold > 3,
                Style.Balanced => can && p.Level >= TargetLevel(m, style) && p.Gold > 20,
                _ => can && p.Level >= Tuning.BotCap && p.Gold > 15
            };
            if (!go || !m.Reroll(pid)) break;
            rerolls++;
        }
        Arrange(m, p, rng);
        m.SetReady(pid);
    }

    private static int TargetLevel(Match m, Style s) => s switch
    {
        Style.Leveler => 8,
        Style.Reroll => m.Round < 12 ? 4 : 7,
        Style.Economy => m.Round < 8 ? 3 : 8,
        _ => Math.Min(8, 2 + m.Round / 3)
    };

    private static int Reserve(Match m, Style s) => s switch
    {
        Style.Economy => m.Round < 14 ? 20 : 5,
        Style.Leveler => 0,
        Style.Reroll => m.Round < 6 ? 20 : 2,
        _ => m.Round >= 4 ? 10 : 0
    };

    /// <summary>Bir kartın bot için değeri (güç eşdeğeri).</summary>
    private static double Worth(PlayerState p, CardDef d, int star)
    {
        if (!d.IsUnit) return d.Special switch { Special.Horn => 4 + p.Level, Special.Scorch => 3 + p.Level * 0.5, Special.Shield => 2, Special.Mardroeme => p.Cards.Any(c => c.Def.Ability == Ability.Berserk) ? 3 + p.Level * 0.5 : -3, _ => 1.5 + p.Level * 0.3 };
        double v = Tuning.Power(d, star);
        v += d.Ability switch { Ability.Bond => 2, Ability.Growth => 3, Ability.Bard => 2.5, Ability.Treasure => 2.5, Ability.Spy => 2, Ability.Medic => 2, Ability.Morale => 2, Ability.Ambush => 1, Ability.Plunder => 2, Ability.RowScorch => 2, Ability.Challenge => 1.5, _ => 0 };
        if (d.IsHero) v += 8;
        return v;
    }

    private static double Want(PlayerState p, CardDef d, Style style)
    {
        var board = p.Cards.OrderByDescending(c => Worth(p, c.Def, c.Star)).Take(p.Slots).ToList();
        var weakest = board.Count >= p.Slots ? board.Min(c => Worth(p, c.Def, c.Star)) : 0;
        var score = Worth(p, d, 1) - weakest;
        if (!d.IsUnit) return p.Cards.Any(c => c.Def == d) ? -1 : score;
        var same = p.Cards.Count(c => c.Def == d && c.Star == 1);
        if (same >= 1 && !d.IsHero)
        {
            score += 3 * same * (1 + d.Tier / 2.0);
            if (style == Style.Reroll && d.Tier <= 2) score += 8;
        }
        score += 4 * p.Cards.Count(c => c.Def == d && c.Star == 2 && d.Tier <= 2);
        if (d.IsHero) score += 50;
        return score;
    }

    private static void BuyLoop(Match m, PlayerState p, Style style)
    {
        for (var bought = true; bought;)
        {
            bought = false;
            foreach (var i in Enumerable.Range(0, p.Shop.Length).Where(i => p.Shop[i] != null).OrderByDescending(i => Want(p, p.Shop[i]!, style)).ToList())
            {
                var d = p.Shop[i]!;
                var sc = Want(p, d, style);
                var need = p.Cards.Count < p.Slots && d.Price <= 2;
                if (sc <= 0 || p.Gold < d.Price) continue;
                if (!need && !d.IsHero && p.Gold - d.Price < Math.Min(Reserve(m, style), p.Gold)) continue;
                if (!m.CanBuy(p.Id, i) && (sc < 4 || !MakeRoom(m, p))) continue;
                if (m.Buy(p.Id, i)) { bought = true; break; }
            }
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

    private static Zone ZoneOf(Row r) => r switch { Row.Close => Zone.Close, Row.Ranged => Zone.Ranged, _ => Zone.Siege };

    /// <summary>Sahayı dizer: en değerli kartlar; iki satıra odak ya da toplam güç; pusu ve çevik kartlar.</summary>
    private static void Arrange(Match m, PlayerState p, Random rng)
    {
        var focus = rng.Next(3) > 0;
        var ranked = p.Cards.OrderByDescending(c => Worth(p, c.Def, c.Star)).ToList();
        List<OwnedCard> chosen;
        Row[]? targets = null;
        if (focus)
        {
            var sums = Enum.GetValues<Row>().ToDictionary(r => r, r => ranked.Where(c => c.Def.IsUnit && !c.Def.Agile && c.Def.Row == r).Sum(c => c.Power));
            targets = sums.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToArray();
            var pref = ranked.Where(c => !c.Def.IsUnit || c.Def.Agile || c.Def.IsHero || targets.Contains(c.Def.Row)).ToList();
            chosen = pref.Concat(ranked.Except(pref)).Take(p.Slots).ToList();
        }
        else chosen = ranked.Take(p.Slots).ToList();
        // aynı özel karttan birden fazlası gereksiz
        chosen = chosen.Where(c => c.Def.IsUnit || chosen.First(x => x.Def == c.Def) == c).ToList();

        foreach (var c in p.Fielded.Where(c => !chosen.Contains(c)).ToList())
        {
            if (m.Move(p.Id, c.Uid, Zone.Bench)) continue;
            MakeRoom(m, p);
            m.Move(p.Id, c.Uid, Zone.Bench);
        }
        foreach (var c in chosen)
        {
            if (!p.Cards.Contains(c)) continue;
            var zones = Match.ZonesFor(c.Def).ToList();
            Zone to;
            if (zones.Contains(Zone.Ambush) && rng.Next(10) < 6 && (c.Zone == Zone.Ambush || p.Cards.Count(x => x.Zone == Zone.Ambush) < p.AmbushCap)) to = Zone.Ambush;
            else
            {
                var rows = zones.Where(z => z != Zone.Ambush).ToList();
                if (targets != null && rows.Count > 1)
                {
                    var t = rows.Where(z => targets.Any(r => ZoneOf(r) == z)).ToList();
                    if (t.Count > 0) rows = t;
                }
                to = rows.OrderBy(z => p.Cards.Where(x => x != c && x.Zone == z).Sum(x => x.Power)).First();
            }
            if (c.Zone != to && !m.WouldOverflow(p.Id, c.Uid, to)) m.Move(p.Id, c.Uid, to);
        }
    }

    // ---------- savaş ----------

    /// <summary>Pencere kararı: açık bilgiye dayalı, gürültülü bir sezgisel.</summary>
    public static WindowChoice Window(Battle b, int side, Random rng) => Window(b, side, rng, 1.0);

    /// <param name="noiseScale">Karar gürültüsü (1 = klasik bot; güçlü bot daha keskin oynar).</param>
    public static WindowChoice Window(Battle b, int side, Random rng, double noiseScale)
    {
        // öğretici canavar (R1-2) hiçbir şeye müdahale etmez
        if (b.Pve && b.NoDamage && b.Sides[side].PlayerId < 0) return WindowChoice.None;
        var me = b.Sides[side];
        var op = b.Sides[1 - side];
        var row = b.CurrentRow;
        double own = (double)b.PlainRow(side, row);
        var seen = Enumerable.Range(0, b.Window).Select(i => (double)op.RowValues[i]).ToList();
        var est = seen.Count > 0 ? seen.Average() : Enum.GetValues<Row>().Sum(r => (double)b.PlainRow(side, r)) / 3.0;
        est = Math.Max(1, est * (1 + (rng.NextDouble() - 0.5) * 0.5 * noiseScale));
        var margin = (own - est) / est;
        var last = b.Window == 2;
        var decisive = (last && me.Points == op.Points) || (b.Window == 1 && (me.Points == 1 || op.Points == 1));
        var imp = decisive ? 1.5 : last && Math.Abs(me.Points - op.Points) == 2 ? 0.5 : 1.0;
        var w = Math.Exp(-Math.Pow(margin / 0.4, 2)) * imp;
        var pq = op.Focus >= 1 ? 0.35 : 0;
        var pi = op.Focus >= 2 ? 0.35 : 0;
        var rem = 3 - b.Window;
        var c = 0.05 * est * Math.Max(0, 1 - me.Focus / (rem * 1.2));
        var noise = 0.05 * est * noiseScale;
        double Noise() { var u1 = 1 - rng.NextDouble(); var u2 = rng.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) * noise; }

        Sign? sign = null;
        var bestGain = 0.0;
        void Consider(Sign s, double gain)
        {
            if (!b.Valid(side, new WindowChoice(new[] { s }))) return;
            gain += Noise();
            if (gain > bestGain) { bestGain = gain; sign = s; }
        }
        Consider(Sign.Aard, w * 0.10 * est - c);
        Consider(Sign.Quen, w * pi * 0.22 * own);
        Consider(Sign.Yrden, w * (b.AmbushSeen[side] is > 0 ? 0.4 : 0.15) * 0.2 * own - c);
        Consider(Sign.Igni, w * 0.22 * est * (1 - pq) - 2 * c);

        Potion? potion = null;
        if (me.Potions.Count > 0 && (w > 0.5 || last) && rng.NextDouble() < 0.7 + 0.3 * (1 - noiseScale))
        {
            var opts = me.Potions.Where(x => x != Potion.Swallow || b.Weather.Contains(row)).Where(x => x != Potion.BlackBlood || op.Focus >= 2).ToList();
            if (opts.Count > 0) potion = opts[rng.Next(opts.Count)];
        }
        var ambush = new List<int>();
        if (me.Ambush.Count > 0 && last) ambush.AddRange(me.Ambush.Select(u => u.Uid));   // son pencerede pusu kendiliğinden inmez: hepsi bilerek indirilir
        else if (me.Ambush.Count > 0 && margin < 0.1 && imp >= 1) ambush.Add(me.Ambush.OrderByDescending(u => u.Power).First().Uid);
        var choice = WindowChoice.With(sign is { } sg ? new[] { sg } : Array.Empty<Sign>(), potion, ambush);
        return b.Valid(side, choice) ? choice : WindowChoice.None;
    }

    /// <summary>Bir savaşı baştan sona botlarla oynatır.</summary>
    public static void PlayOut(Battle b, Random rng)
    {
        b.Start();
        while (!b.Done)
        {
            for (var i = 0; i < 2; i++) if (!b.Submitted(i)) b.Submit(i, Window(b, i, rng));
            b.Resolve();
        }
    }

    /// <summary>Tümü bot olan bir maçı sonuna kadar oynatır (simülasyon ve sınama için).</summary>
    public static void RunToEnd(Match m, Random rng, int maxRounds = 80)
    {
        foreach (var p in m.Players) if (p.School == null) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(p.SchoolOptions.Length)]);
        while (m.Phase != Phase.Finished && m.Round <= maxRounds)
        {
            foreach (var p in m.Alive.ToList()) Prep(m, p.Id, rng);
            if (m.Phase == Phase.Prep) m.ForceLock();
            foreach (var b in m.Battles) PlayOut(b, rng);
            m.EndRound();
        }
    }
}
