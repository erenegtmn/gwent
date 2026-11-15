namespace HanLigi;

/// <summary>Savaştaki bir birim (kart, Çağrı ile gelen kopya ya da Ölüm Yankısı hayaleti).</summary>
public sealed class BattleUnit
{
    public int Uid { get; init; }
    public required CardDef Def { get; init; }
    public int Star { get; init; } = 1;
    public int Power { get; set; }
    public Row Row { get; set; }
    public bool Protected { get; set; }
    public bool Revive { get; set; }
    public bool Token { get; init; }
    public bool Summoned { get; init; }
    /// <summary>Bu pencerede pusudan indi (Ciri muafiyeti, Haydut 4 bonusu).</summary>
    public bool Landed { get; set; }
    public bool IsHero => Def.IsHero;
    public bool Has(Tag t) => !Token && Def.Has(t);
}

/// <summary>Aktivasyon seçimleri: Boynuz ve Kalkan kartlarının, Moral birimlerinin hedef satırı.</summary>
public sealed class Activation
{
    public List<Row> Horns { get; } = new();
    public List<Row> Shields { get; } = new();
    public Dictionary<int, Row> Morale { get; } = new();
}

/// <summary>Bir pencere seçimi: en çok 1 işaret (Jaskier ile bir kez 2), en çok 1 simya, en çok 1 pusu birimi.</summary>
public sealed record WindowChoice(IReadOnlyList<Sign> Signs, Potion? Potion = null, int? AmbushUid = null)
{
    public static readonly WindowChoice None = new(Array.Empty<Sign>());
}

public sealed class BattleSide
{
    public int PlayerId { get; init; } = -1;
    public required string Name { get; init; }
    public int Level { get; init; }
    public School? School { get; init; }
    public List<BattleUnit> Units { get; } = new();
    public List<BattleUnit> Ambush { get; } = new();
    public List<Special> Specials { get; } = new();
    /// <summary>Depodaki kartlar (Çağrı için).</summary>
    public List<(CardDef Def, int Star)> Bench { get; } = new();
    public List<Potion> Potions { get; } = new();
    public Dictionary<Tag, int> Tags { get; } = new();
    public Activation Act { get; set; } = new();
    public int Focus { get; set; }
    public int Rage { get; set; }
    public int Points { get; set; }
    public int Total { get; set; }
    public bool JaskierUsed { get; set; }
    public bool TrissUsed { get; set; }
    public bool MageFreeUsed { get; set; }
    public bool YenPending { get; set; }
    public Row? EredinRow { get; set; }
    /// <summary>Önceki satırın sonucu: +1 kazandı, −1 kaybetti, 0 berabere / ilk satır.</summary>
    public int Prev { get; set; }
    public int[] RowValues { get; } = new int[3];

    /// <summary>Etiket eşik seviyesi: 0 yok, 1 (2 kart), 2 (4 kart), 3 (6 kart).</summary>
    public int Th(Tag t) => Tags.TryGetValue(t, out var n) ? n >= 6 ? 3 : n >= 4 ? 2 : n >= 2 ? 1 : 0 : 0;

    public IEnumerable<BattleUnit> In(Row r) => Units.Where(u => u.Row == r);
    public bool HasHero(Hero h) => Units.Any(u => u.Def.Hero == h);
    public int Count(Special s) => Specials.Count(x => x == s);
}

/// <summary>Arayüzün canlandıracağı bir olay: işaret, simya, pusu inişi, Öfke, hava, Kavurma. <c>Target</c> birim kimliği ya da satır numarasıdır.</summary>
public sealed record FxEvent(string Kind, int Side, int Target = 0, bool Blocked = false);

public sealed record WindowOutcome(int Index, Row Row, int ValueA, int ValueB, int Winner, WindowChoice ChoiceA, WindowChoice ChoiceB, IReadOnlyList<string> Log, IReadOnlyList<FxEvent> Fx, IReadOnlyDictionary<int, int> CardValues)
{
    /// <summary>Kartların yüzdeler uygulanmadan önceki değeri (arayüz "8 → 4" gösterir).</summary>
    public IReadOnlyDictionary<int, int> BaseValues { get; init; } = new Dictionary<int, int>();
    /// <summary>[taraf] satırın, satır yüzdeleri (Aard, Gök Gürültüsü, Boynuz...) uygulanmadan önceki toplamı.</summary>
    public int[] RowBase { get; init; } = new int[2];
}

/// <summary>
/// İki sahanın satır çarpışması (YENI_MOD.md §15.5-15.7). Akış: <see cref="Start"/> (savaş öncesi çözüm) → her satır için
/// iki tarafın <see cref="Submit"/> ile gizli seçimi → <see cref="Resolve"/>. Durum yalnız bu yöntemlerle değişir.
/// </summary>
public sealed class Battle
{
    public BattleSide[] Sides { get; }
    public Row[] Order { get; }
    public HashSet<Row> Weather { get; } = new();
    public int Window { get; private set; }
    public bool Started { get; private set; }
    public bool Done => Window >= 3;
    public List<WindowOutcome> Outcomes { get; } = new();
    public List<string> PreLog { get; } = new();
    /// <summary>Savaş öncesi olaylar (hava: Target = satır; Kavurma: Target = satır, Side = birimin tarafı).</summary>
    public List<FxEvent> PreFx { get; } = new();
    /// <summary>Casusların gösterdiği kartlar: [taraf] = rakibin kartları.</summary>
    public List<(CardDef Def, int Star, string Zone)>[] Revealed { get; } = { new(), new() };
    /// <summary>3★ casus: rakibin pusudaki kart sayısı (bilinmiyorsa null).</summary>
    public int?[] AmbushSeen { get; } = new int?[2];
    public bool Pve { get; init; }
    public bool NoDamage { get; init; }
    /// <summary>İkinci taraf başka bir oyuncunun sahasının kopyası (gölge rakip).</summary>
    public bool Ghost { get; init; }
    private readonly WindowChoice?[] _pending = new WindowChoice?[2];
    private readonly Random _rng;
    private int _nextUid = 100000;

    public Battle(BattleSide a, BattleSide b, Row[] order, int seed)
    {
        Sides = new[] { a, b };
        Order = order;
        _rng = new Random(seed);
    }

    public Row CurrentRow => Order[Math.Min(Window, 2)];
    public bool Submitted(int side) => _pending[side] != null;

    // ---------- kart değeri ----------

    private bool Shielded(BattleSide s, Row r) => s.Act.Shields.Contains(r);

    /// <summary>Kart değeri (§15.4): hava → Bond → Büyüme → Moral → etiket → Yansıma.</summary>
    public int Value(BattleSide s, BattleUnit u, bool noWeather = false) => Value(s, u, noWeather, true);

    private int Value(BattleSide s, BattleUnit u, bool noWeather, bool mirror)
    {
        var v = u.Power;
        if (Weather.Contains(u.Row) && !noWeather && !u.IsHero && !u.Protected && !Shielded(s, u.Row))
        {
            var th = u.Has(Tag.Soldier) ? s.Th(Tag.Soldier) : 0;
            if (th < 3) v = Math.Min(v, th == 2 ? 3 : th == 1 ? 2 : 1);
        }
        if (!u.Token)
        {
            if (u.Def.Ability == Ability.Bond)
                v += Math.Min(3, s.In(u.Row).Count(o => o != u && !o.Token && o.Def.Tags.Any(u.Def.Has)));
            if (u.Def.Ability == Ability.Growth)
                v += Math.Min(4, s.In(u.Row).Count(o => o != u));
        }
        foreach (var m in s.Units)
            if (m != u && m.Def.Ability == Ability.Morale && !m.Token && s.Act.Morale.TryGetValue(m.Uid, out var mr) && mr == u.Row)
                v += m.Star;
        if (u.Has(Tag.Monster)) v += s.Th(Tag.Monster);
        if (mirror && !u.Token && u.Def.Ability == Ability.Mirror)
        {
            var best = s.In(u.Row).Where(o => o != u && (o.Token || o.Def.Ability != Ability.Mirror)).Select(o => Value(s, o, noWeather, false)).DefaultIfEmpty(0).Max();
            v = Math.Max(v, best / 2);
        }
        return v;
    }

    /// <summary>Bir satırın müdahalesiz değeri (arayüz ve botlar için).</summary>
    public int PlainRow(int side, Row r)
    {
        var s = Sides[side];
        var sum = s.In(r).Sum(u => Value(s, u));
        return Tuning.ApplyPct(sum, 0.5 * s.Act.Horns.Count(h => h == r));
    }

    // ---------- aktivasyon ----------

    /// <summary>Varsayılan aktivasyon (§15.11): Boynuz en güçlü satıra, Kalkan havalı en güçlü satıra, Moral en kalabalık satıra.</summary>
    public Activation DefaultActivation(int side)
    {
        var s = Sides[side];
        var a = new Activation();
        var byPower = Enum.GetValues<Row>().OrderByDescending(r => s.In(r).Sum(u => u.Power)).ToArray();
        for (var i = 0; i < s.Count(Special.Horn); i++) a.Horns.Add(byPower[Math.Min(i, 2)]);
        for (var i = 0; i < s.Count(Special.Shield); i++) a.Shields.Add(byPower[Math.Min(i, 2)]);
        foreach (var m in s.Units.Where(u => u.Def.Ability == Ability.Morale))
            a.Morale[m.Uid] = Enum.GetValues<Row>().OrderByDescending(r => s.In(r).Count(u => u != m)).First();
        return a;
    }

    public bool SetActivation(int side, Activation a)
    {
        if (Started) return false;
        var s = Sides[side];
        if (a.Horns.Count != s.Count(Special.Horn) || a.Shields.Count != s.Count(Special.Shield)) return false;
        if (a.Morale.Keys.Any(k => !s.Units.Any(u => u.Uid == k && u.Def.Ability == Ability.Morale))) return false;
        s.Act = a;
        return true;
    }

    // ---------- savaş öncesi ----------

    /// <summary>Savaş öncesi çözüm (§15.5): Çağrı → korumalar → hava → Kavurma → yankı/diriliş → Eredin → Odak.</summary>
    public void Start()
    {
        if (Started) return;
        Started = true;
        foreach (var s in Sides)
        {
            // eksik aktivasyon varsayılanla tamamlanır
            var def = DefaultActivation(Array.IndexOf(Sides, s));
            if (s.Act.Horns.Count != s.Count(Special.Horn)) { s.Act.Horns.Clear(); s.Act.Horns.AddRange(def.Horns); }
            if (s.Act.Shields.Count != s.Count(Special.Shield)) { s.Act.Shields.Clear(); s.Act.Shields.AddRange(def.Shields); }
            foreach (var kv in def.Morale) s.Act.Morale.TryAdd(kv.Key, kv.Value);
            // 1. Çağrı
            foreach (var c in s.Units.Where(u => u.Def.Ability == Ability.Summon && u.Star >= 2).ToList())
            {
                var want = c.Star - 1;
                for (var i = 0; i < s.Bench.Count && want > 0; i++)
                {
                    if (s.Bench[i].Def != c.Def) continue;
                    var b = s.Bench[i];
                    s.Units.Add(new BattleUnit { Uid = _nextUid++, Def = b.Def, Star = b.Star, Power = Tuning.Power(b.Def, b.Star), Row = c.Row, Summoned = true });
                    s.Bench.RemoveAt(i--);
                    want--;
                    PreLog.Add($"{s.Name}: {c.Def.Name} depodan bir eşini çağırdı.");
                }
            }
            // 2. Şifacı korumaları (v0'da hedef otomatik: en güçlü kahraman olmayan birimler)
            foreach (var m in s.Units.Where(u => u.Def.Ability == Ability.Medic).ToList())
            {
                var n = m.Star >= 2 ? 2 : 1;
                foreach (var t in s.Units.Where(u => !u.IsHero && !u.Protected).OrderByDescending(u => u.Power).Take(n)) t.Protected = true;
                if (m.Star >= 3 && s.Units.Where(u => !u.IsHero && !u.Protected).OrderByDescending(u => u.Power).FirstOrDefault() is { } rv) rv.Revive = true;
            }
        }
        // 3. hava
        foreach (var s in Sides)
            foreach (var sp in s.Specials)
            {
                Row? r = sp switch { Special.Frost => Row.Close, Special.Fog => Row.Ranged, Special.Rain => Row.Siege, _ => null };
                if (r is { } row && Weather.Add(row)) { PreLog.Add($"{s.Name} hava açtı: {Texts.Name(row)} satırı (iki taraf)."); PreFx.Add(new FxEvent("weather", Array.IndexOf(Sides, s), (int)row)); }
            }
        // 4. Kavurma
        var scorches = Sides.Sum(s => s.Count(Special.Scorch));
        for (var i = 0; i < scorches; i++)
        {
            var cands = new List<(BattleSide S, BattleUnit U, int V)>();
            foreach (var s in Sides)
                foreach (var u in s.Units)
                    if (!u.IsHero && !u.Protected && !Shielded(s, u.Row) && !(u.Has(Tag.Dwarf) && s.Th(Tag.Dwarf) >= 2))
                        cands.Add((s, u, Value(s, u)));
            if (cands.Count == 0) break;
            var max = cands.Max(c => c.V);
            foreach (var (s, u, _) in cands.Where(c => c.V == max))
            {
                s.Units.Remove(u);
                PreLog.Add($"Kavurma: {s.Name} tarafında {u.Def.Name} ({max}) yok oldu.");
                PreFx.Add(new FxEvent("scorch", Array.IndexOf(Sides, s), (int)u.Row));
                // 5. yankı / diriliş
                if (!u.Token && u.Def.Ability == Ability.DeathEcho)
                    s.Units.Add(new BattleUnit { Uid = _nextUid++, Def = u.Def, Power = u.Star == 1 ? 2 : u.Star == 2 ? 3 : 5, Row = u.Row, Token = true });
                else if (u.Revive)
                    s.Units.Add(new BattleUnit { Uid = _nextUid++, Def = u.Def, Star = u.Star, Power = Math.Max(1, u.Power / 2), Row = u.Row, Token = true });
            }
        }
        // 6-7. Eredin, Odak
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            if (s.HasHero(Hero.Eredin) || s.Ambush.Any(u => u.Def.Hero == Hero.Eredin))
            {
                var r = Order[_rng.Next(3)];
                Sides[1 - i].EredinRow = r;
                PreLog.Add($"Eredin: {Sides[1 - i].Name} tarafının {Texts.Name(r)} satırı −%30.");
            }
            s.Focus = Tuning.StartFocus + s.Units.Count(u => !u.Token && u.Def.Ability == Ability.Bard)
                      + (s.School == HanLigi.School.Wolf ? 1 : 0) + (s.Th(Tag.Mage) >= 2 ? 1 : 0);
        }
    }

    // ---------- pencere ----------

    public int AmbushCap(BattleSide s) => s.Th(Tag.Bandit) >= 1 ? 3 : 2;

    public bool CanDouble(int side) => !Sides[side].JaskierUsed && Sides[side].HasHero(Hero.Jaskier);

    /// <summary>Seçilen işaretlerin Odak bedeli (bedavalar düşülmüş); durumu değiştirmez.</summary>
    public int Cost(int side, WindowChoice c) => Cost(side, c, false);

    private int Cost(int side, WindowChoice c, bool commit)
    {
        var s = Sides[side];
        var row = CurrentRow;
        var geralt = s.In(row).Any(u => u.Def.Hero == Hero.Geralt);
        var triss = !s.TrissUsed && s.HasHero(Hero.Triss);
        var mage = !s.MageFreeUsed && s.Th(Tag.Mage) >= 3;
        var total = 0;
        for (var i = 0; i < c.Signs.Count; i++)
        {
            var sg = c.Signs[i];
            if (i == 0 && geralt) continue;
            if (sg == Sign.Quen && triss) { triss = false; if (commit) s.TrissUsed = true; continue; }
            if (mage) { mage = false; if (commit) s.MageFreeUsed = true; continue; }
            total += Tuning.SignCost(sg);
        }
        return total;
    }

    public bool Valid(int side, WindowChoice c)
    {
        if (!Started || Done) return false;
        var s = Sides[side];
        if (c.Signs.Count > (CanDouble(side) ? 2 : 1)) return false;
        if (c.Potion is { } p && !s.Potions.Contains(p)) return false;
        if (c.AmbushUid is { } uid && s.Ambush.All(u => u.Uid != uid)) return false;
        return Cost(side, c) <= s.Focus;
    }

    public bool Submit(int side, WindowChoice c)
    {
        if (_pending[side] != null || !Valid(side, c)) return false;
        _pending[side] = c;
        return true;
    }

    /// <summary>İki taraf da seçtiyse pencereyi çözer (§15.6) ve sonucu döner.</summary>
    public WindowOutcome? Resolve()
    {
        if (!Started || Done || _pending[0] == null || _pending[1] == null) return null;
        var ch = new[] { _pending[0]!, _pending[1]! };
        _pending[0] = _pending[1] = null;
        var row = CurrentRow;
        var log = new List<string>();
        var fx = new List<FxEvent>();
        var own = new double[2];   // kendi satır yüzdesi
        var foe = new double[2];   // rakibin işaretlerinden gelen satır yüzdesi (Ciri muaf)
        var card = new Dictionary<BattleUnit, double>();
        var noWeather = new bool[2];
        var quenBlocks = new int[2];
        var potion = new Potion?[2];
        var ambush = new int?[2];
        void Pct(BattleUnit u, double d) => card[u] = card.GetValueOrDefault(u) + d;

        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            s.Focus -= Cost(i, ch[i], true);
            if (ch[i].Signs.Count > 1) s.JaskierUsed = true;
            potion[i] = ch[i].Potion;
            if (potion[i] is { } p) s.Potions.Remove(p);
            ambush[i] = ch[i].AmbushUid;
            foreach (var u in s.Units) u.Landed = false;
        }
        // 1. Yrden: rakibin simya kartını ve pusu inişini keser
        var yrden = new[] { ch[0].Signs.Contains(Sign.Yrden), ch[1].Signs.Contains(Sign.Yrden) };
        for (var i = 0; i < 2; i++)
        {
            if (!yrden[1 - i]) continue;
            var s = Sides[i];
            var hit = false;
            if (potion[i] != null) { log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} tarafının {Texts.Name(potion[i]!.Value)} kartı etkisiz."); potion[i] = null; hit = true; }
            if (s.Th(Tag.Bandit) < 3 && (ambush[i] != null || (Window == 2 && s.Ambush.Count > 0)))
            {
                log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} pusudan inemedi.");
                ambush[i] = null;
                hit = true;
            }
            if (!hit) log.Add($"{Sides[1 - i].Name} Yrden attı, boşa gitti.");
            fx.Add(new FxEvent("yrden", 1 - i, 0, hit));
        }
        // 3. pusu inişleri (son pencerede kalanlar kendiliğinden iner)
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            var blocked = yrden[1 - i] && s.Th(Tag.Bandit) < 3;
            var landing = new List<BattleUnit>();
            if (ambush[i] is { } uid && s.Ambush.FirstOrDefault(u => u.Uid == uid) is { } au) landing.Add(au);
            if (Window == 2 && !blocked) landing.AddRange(s.Ambush.Where(u => !landing.Contains(u)));
            foreach (var u in landing)
            {
                s.Ambush.Remove(u);
                u.Row = row;
                u.Landed = true;
                s.Units.Add(u);
                if (s.Th(Tag.Bandit) >= 2) Pct(u, 0.25);
                log.Add($"{s.Name} pusudan {u.Def.Name} indirdi.");
                fx.Add(new FxEvent("ambush", i, u.Uid));
            }
        }
        // 4. iksirler
        for (var i = 0; i < 2; i++)
        {
            if (potion[i] == Potion.Thunderbolt) { own[i] += 0.25; log.Add($"{Sides[i].Name}: Gök Gürültüsü (+%25)."); fx.Add(new FxEvent("thunderbolt", i)); }
            if (potion[i] == Potion.Swallow) { noWeather[i] = true; log.Add($"{Sides[i].Name}: Kırlangıç (hava etkisi kalktı)."); fx.Add(new FxEvent("swallow", i)); }
        }
        BattleUnit? Strongest(int side, IEnumerable<BattleUnit>? skip = null) =>
            Sides[side].In(row).Where(u => !u.IsHero && !u.Protected && skip?.Contains(u) != true)
                .OrderByDescending(u => Value(Sides[side], u, noWeather[side]) * (1 + card.GetValueOrDefault(u))).FirstOrDefault();
        double Amp(int side) => 1 + (Sides[side].School == HanLigi.School.Griffin ? 0.5 : 0) + (Sides[side].Th(Tag.Mage) >= 1 ? 0.25 : 0);
        double Bomb(int side, BattleUnit target, int victim)
        {
            var v = Sides[victim];
            if (target.Has(Tag.Dwarf) && v.In(row).Any(u => u.Def.Hero == Hero.Zoltan)) return 0;
            return (Sides[side].School == HanLigi.School.Viper ? 1.25 : 1) * (target.Has(Tag.Dwarf) && v.Th(Tag.Dwarf) >= 1 ? 0.5 : 1);
        }
        // 2 + 5. işaretler
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            var o = 1 - i;
            foreach (var sg in ch[i].Signs)
            {
                if (sg == Sign.Aard) { foe[o] -= 0.10 * Amp(i); log.Add($"{s.Name} Aard: {Sides[o].Name} satırı −%{0.10 * Amp(i) * 100:0}."); fx.Add(new FxEvent("aard", i, (int)Math.Round(0.10 * Amp(i) * 100))); }
                if (sg == Sign.Quen && !ch[o].Signs.Contains(Sign.Igni)) log.Add($"{s.Name} Quen attı, boşa gitti.");
                if (sg == Sign.Quen) fx.Add(new FxEvent("quen", i, 0, ch[o].Signs.Contains(Sign.Igni) && potion[i] != Potion.BlackBlood));
                if (sg != Sign.Igni) continue;
                int victim;
                if (potion[o] == Potion.BlackBlood) { victim = i; log.Add($"{s.Name} Igni attı; Kara Kan onu kendisine yansıttı."); }
                else if (ch[o].Signs.Contains(Sign.Quen)) { quenBlocks[o]++; log.Add($"{s.Name} Igni attı; {Sides[o].Name} Quen ile engelledi."); fx.Add(new FxEvent("igni", i, 0, true)); continue; }
                else victim = o;
                if (Strongest(victim) is { } t) { Pct(t, -0.5 * Amp(i)); if (victim == o) log.Add($"{s.Name} Igni: {t.Def.Name} −%{0.5 * Amp(i) * 100:0}."); fx.Add(new FxEvent(victim == o ? "igni" : "igni_reflect", i, t.Uid)); }
                else { log.Add($"{s.Name} Igni attı ama hedef yok."); fx.Add(new FxEvent("igni", i)); }
            }
        }
        // 5. bombalar, Eredin, Ayı
        for (var i = 0; i < 2; i++)
        {
            var o = 1 - i;
            if (potion[i] == Potion.DancingStar)
            {
                var a = Strongest(o);
                var b = a == null ? null : Strongest(o, new[] { a });
                foreach (var t in new[] { a, b }) if (t != null) { Pct(t, -0.30 * Bomb(i, t, o)); fx.Add(new FxEvent("dancingstar", i, t.Uid)); }
                if (a == null) fx.Add(new FxEvent("dancingstar", i));
                log.Add($"{Sides[i].Name}: Dans Eden Yıldız ({(a == null ? "hedef yok" : b == null ? a.Def.Name : a.Def.Name + ", " + b.Def.Name)}).");
            }
            if (potion[i] == Potion.NorthernWind)
            {
                var t = Strongest(o);
                if (t != null) Pct(t, -0.70 * Bomb(i, t, o));
                fx.Add(new FxEvent("northernwind", i, t?.Uid ?? 0));
                log.Add($"{Sides[i].Name}: Kuzey Rüzgârı ({t?.Def.Name ?? "hedef yok"}).");
            }
            if (potion[i] == Potion.BlackBlood && !ch[o].Signs.Contains(Sign.Igni)) log.Add($"{Sides[i].Name} Kara Kan içti, boşa gitti.");
            if (potion[i] == Potion.BlackBlood) fx.Add(new FxEvent("blackblood", i, 0, ch[o].Signs.Contains(Sign.Igni)));
            if (Sides[i].EredinRow == row) foreach (var u in Sides[i].In(row).Where(u => !u.IsHero)) Pct(u, -0.30);
            if (quenBlocks[i] > 0 && Sides[i].School == HanLigi.School.Bear) { foe[o] -= 0.10; log.Add($"{Sides[i].Name} (Ayı): karşı vuruş −%10."); }
        }
        // 7. İntikam / Coşku, Yennefer, Cüce 6, Boynuz
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            foreach (var u in s.In(row).Where(u => !u.Token))
            {
                if (u.Def.Ability == Ability.Revenge && s.Prev < 0) Pct(u, 0.5);
                if (u.Def.Ability == Ability.Zeal && s.Prev > 0) Pct(u, 0.3);
            }
            if (s.YenPending) { own[i] += 0.2; s.YenPending = false; }
            if (s.Th(Tag.Dwarf) >= 3 && s.In(row).Any(u => u.Has(Tag.Dwarf))) own[i] += 0.2;
            own[i] += 0.5 * s.Act.Horns.Count(h => h == row);
        }
        // 8. karşılaştırma
        var val = new int[2];
        var cardValues = new Dictionary<int, int>();
        var baseValues = new Dictionary<int, int>();
        var rowBase = new int[2];
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            int normal = 0, exempt = 0;
            foreach (var u in s.In(row))
            {
                var bv = Value(s, u, noWeather[i]);
                baseValues[u.Uid] = bv;
                var v = Tuning.ApplyPct(bv, card.GetValueOrDefault(u));
                if (Tutorial(i)) v = Math.Max(1, v);   // öğretici canavar sıfırlanamaz
                cardValues[u.Uid] = v;
                if (u.Landed && u.Def.Hero == Hero.Ciri) exempt += v; else normal += v;
            }
            rowBase[i] = normal + exempt;
            val[i] = Tuning.ApplyPct(normal, own[i] + foe[i]) + Tuning.ApplyPct(exempt, own[i]);
            if (Tutorial(i) && s.In(row).Any()) val[i] = Math.Max(1, val[i]);
            s.RowValues[Window] = val[i];
            s.Total += val[i];
        }
        var winner = val[0] > val[1] ? 0 : val[1] > val[0] ? 1 : -1;
        // 9. Öfke ve iadeler
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            s.Focus += quenBlocks[i];
            if (winner == i)
            {
                s.Points++;
                s.Prev = 1;
                if (s.In(row).Any(u => u.Def.Hero == Hero.Yennefer)) s.YenPending = true;
            }
            else if (winner == 1 - i)
            {
                s.Prev = -1;
                if (!s.In(row).Any()) continue;
                var ves = s.In(row).Any(u => u.Def.Hero == Hero.Vesemir);
                var gain = Math.Min(ves ? 2 : 1, (ves ? Tuning.RageCap + 1 : Tuning.RageCap) - s.Rage);
                if (gain > 0) { s.Rage += gain; s.Focus += gain; log.Add($"{s.Name}: Öfke +{gain} Odak."); fx.Add(new FxEvent("rage", i, gain)); }
            }
            else s.Prev = 0;
        }
        var outcome = new WindowOutcome(Window, row, val[0], val[1], winner, ch[0], ch[1], log, fx, cardValues) { BaseValues = baseValues, RowBase = rowBase };
        Outcomes.Add(outcome);
        Window++;
        return outcome;
    }

    // ---------- sonuç ----------

    /// <summary>
    /// R1-2 öğretici canavarı mı (PvE, hasarsız, canavar tarafı)? Kartları hiçbir etkiyle 1'in altına inmez: R2'de tek kart,
    /// işaret ya da bombayla bile her durumda kaybeder (YENI_MOD.md §15).
    /// </summary>
    public bool Tutorial(int side) => Pve && NoDamage && Sides[side].PlayerId < 0;

    /// <summary>Kazanan taraf (0/1) ya da berabere (−1). Puanlar eşitse üç satırın toplamı belirler.</summary>
    public int Winner
    {
        get
        {
            var (a, b) = (Sides[0], Sides[1]);
            if (a.Points != b.Points) return a.Points > b.Points ? 0 : 1;
            if (a.Total != b.Total) return a.Total > b.Total ? 0 : 1;
            return -1;
        }
    }

    /// <summary>Kaybedenin alacağı hasar (§15.7).</summary>
    public int Damage
    {
        get
        {
            var w = Winner;
            if (w < 0 || NoDamage) return 0;
            var d = Math.Max(1, Math.Abs(Sides[0].Points - Sides[1].Points));
            var h = (int)Math.Ceiling(Tuning.DamageMult * (Sides[w].Level + d));
            if (Sides[1 - w].School == HanLigi.School.Bear) h = (int)Math.Ceiling(h * 0.9);
            if (Pve) h = (int)Math.Ceiling(h / 2.0);
            return h;
        }
    }
}
