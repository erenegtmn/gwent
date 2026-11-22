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
/// <remarks>Pusu: aynı pencerede birden çok birim indirilebilir (<see cref="AmbushUid"/> + <see cref="MoreAmbush"/>).</remarks>
public sealed record WindowChoice(IReadOnlyList<Sign> Signs, Potion? Potion = null, int? AmbushUid = null, IReadOnlyList<int>? MoreAmbush = null)
{
    public static readonly WindowChoice None = new(Array.Empty<Sign>());

    /// <summary>Bu pencerede pusudan indirilecek bütün birimler.</summary>
    public IReadOnlyList<int> AmbushUids => AmbushUid is { } a ? new[] { a }.Concat(MoreAmbush ?? Array.Empty<int>()).ToArray() : MoreAmbush ?? Array.Empty<int>();

    /// <summary>Pusu listesinden seçim kurar (boşsa pusu yok).</summary>
    public static WindowChoice With(IReadOnlyList<Sign> signs, Potion? potion, IReadOnlyList<int> ambush)
        => new(signs, potion, ambush.Count > 0 ? ambush[0] : null, ambush.Count > 1 ? ambush.Skip(1).ToArray() : null);
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
    /// <summary>Hazırlıkta seçilmiş Boynuz / Kalkan satırları (aktivasyonun varsayılanı).</summary>
    public List<Row> HornAims { get; } = new();
    public List<Row> ShieldAims { get; } = new();
    /// <summary>Depodaki kartlar (Çağrı için).</summary>
    public List<(CardDef Def, int Star)> Bench { get; } = new();
    public List<Potion> Potions { get; } = new();
    public Dictionary<Tag, int> Tags { get; } = new();
    public Activation Act { get; set; } = new();
    public int Focus { get; set; }
    public int Rage { get; set; }
    /// <summary>Bu savaşta Yağma ile kazanılan altın (sonuçta oyuncuya eklenir).</summary>
    public int Plunder { get; set; }
    public int Points { get; set; }
    public decimal Total { get; set; }
    public int JaskierUses { get; set; }
    public int TrissUses { get; set; }
    /// <summary>Rakibin Eredin'inin bu tarafa uyguladığı yüzde (basamağa göre, <see cref="Tuning.EredinPct"/>).</summary>
    public double EredinPct { get; set; } = 0.30;
    public bool MageFreeUsed { get; set; }
    public bool YenPending { get; set; }
    public Row? EredinRow { get; set; }
    /// <summary>Önceki satırın sonucu: +1 kazandı, −1 kaybetti, 0 berabere / ilk satır.</summary>
    public int Prev { get; set; }
    public decimal[] RowValues { get; } = new decimal[3];

    /// <summary>Etiket eşik seviyesi: 0 yok, 1 (2 kart), 2 (4 kart), 3 (6 kart).</summary>
    public int Th(Tag t) => Tags.TryGetValue(t, out var n) ? Tuning.TagLevel(t, n) : 0;

    public IEnumerable<BattleUnit> In(Row r) => Units.Where(u => u.Row == r);
    public bool HasHero(Hero h) => Units.Any(u => u.Def.Hero == h);
    /// <summary>Kahramanın güç basamağı: kendi sınıfının eşiği 2'ye varınca 1 (II), 3'e varınca 2 (III); yoksa 0. Sahada (pusu dahil) değilse −1.</summary>
    public int HeroTier(Hero h)
    {
        var u = Units.FirstOrDefault(x => x.Def.Hero == h) ?? Ambush.FirstOrDefault(x => x.Def.Hero == h);
        if (u == null) return -1;
        var th = u.Def.Tags.Length > 0 ? Th(u.Def.Tags[0]) : 0;
        return th >= 3 ? 2 : th >= 2 ? 1 : 0;
    }
    public int Count(Special s) => Specials.Count(x => x == s);
}

/// <summary>Arayüzün canlandıracağı bir olay: işaret, simya, pusu inişi, Öfke, hava, Kavurma. <c>Target</c> birim kimliği ya da satır numarasıdır.</summary>
public sealed record FxEvent(string Kind, int Side, int Target = 0, bool Blocked = false);

public sealed record WindowOutcome(int Index, Row Row, decimal ValueA, decimal ValueB, int Winner, WindowChoice ChoiceA, WindowChoice ChoiceB, IReadOnlyList<string> Log, IReadOnlyList<FxEvent> Fx, IReadOnlyDictionary<int, decimal> CardValues)
{
    /// <summary>Kartların yüzdeler uygulanmadan önceki değeri (arayüz "8 → 4" gösterir).</summary>
    public IReadOnlyDictionary<int, int> BaseValues { get; init; } = new Dictionary<int, int>();
    /// <summary>
    /// Satır yüzdeleri ve kaynakları: (taraf, kaynak adı, yüzde). Satır değeri = yuvarla(kart toplamı × (1 + Σ yüzde)); arayüz
    /// "Σ7 · Aard −%10 = 6" diye gösterir.
    /// </summary>
    public IReadOnlyList<(int Side, string Source, double Pct)> RowMods { get; init; } = Array.Empty<(int, string, double)>();
    /// <summary>[taraf] satırın, satır yüzdeleri (Aard, Gök Gürültüsü, Boynuz...) uygulanmadan önceki toplamı.</summary>
    public decimal[] RowBase { get; init; } = new decimal[2];
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
    /// <summary>Sonucu maça işlendi mi (can, altın, seri, eleme): savaş biter bitmez işlenir (<see cref="Match.Settle"/>).</summary>
    public bool Settled { get; set; }
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
    /// <summary>Savaşın round'u (hasar tabanı için; 0 = bilinmiyor).</summary>
    public int Round { get; init; }
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
            if (th < 2) v = Math.Min(v, th == 1 ? 2 : 1);
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
        if (u.Has(Tag.Soldier) && s.Th(Tag.Soldier) >= 3) v += 2;
        if (u.Has(Tag.Dwarf) && s.Th(Tag.Dwarf) >= 2) v += 1;
        if (u.Has(Tag.Sailor) && s.Th(Tag.Sailor) >= 3) v += 2;
        if (u.Has(Tag.Knight) && s.Th(Tag.Knight) >= 2) v += 2;
        if (mirror && !u.Token && u.Def.Ability == Ability.Mirror)
        {
            var best = s.In(u.Row).Where(o => o != u && (o.Token || o.Def.Ability != Ability.Mirror)).Select(o => Value(s, o, noWeather, false)).DefaultIfEmpty(0).Max();
            v = Math.Max(v, best / 2);
        }
        return v;
    }

    /// <summary>Bir satırın müdahalesiz değeri (arayüz ve botlar için).</summary>
    public decimal PlainRow(int side, Row r)
    {
        var s = Sides[side];
        decimal sum = s.In(r).Sum(u => Value(s, u));
        return Tuning.ApplyPct(sum, 0.5 * s.Act.Horns.Count(h => h == r));
    }

    // ---------- aktivasyon ----------

    /// <summary>Varsayılan aktivasyon (§15.11): Boynuz en güçlü satıra, Kalkan havalı en güçlü satıra, Moral en kalabalık satıra.</summary>
    public Activation DefaultActivation(int side)
    {
        var s = Sides[side];
        var a = new Activation();
        var byPower = Enum.GetValues<Row>().OrderByDescending(r => s.In(r).Sum(u => u.Power)).ToArray();
        for (var i = 0; i < s.Count(Special.Horn); i++) a.Horns.Add(i < s.HornAims.Count ? s.HornAims[i] : byPower[Math.Min(i, 2)]);
        for (var i = 0; i < s.Count(Special.Shield); i++) a.Shields.Add(i < s.ShieldAims.Count ? s.ShieldAims[i] : byPower[Math.Min(i, 2)]);
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
        // 2b. Mardroeme: Berserker'lar dönüşür
        foreach (var s in Sides)
            if (s.Count(Special.Mardroeme) > 0)
                foreach (var u in s.Units.Concat(s.Ambush).Where(u => !u.Token && u.Def.Ability == Ability.Berserk))
                {
                    u.Power *= Tuning.BerserkMult;
                    PreLog.Add($"{s.Name}: Mardroeme ile {u.Def.Name} dönüştü ({u.Power}).");
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
                    if (Burnable(s, u))
                        cands.Add((s, u, Value(s, u)));
            if (cands.Count == 0) break;
            var max = cands.Max(c => c.V);
            foreach (var (s, u, _) in cands.Where(c => c.V == max)) Burn(s, u, $"Kavurma: {s.Name} tarafında {u.Def.Name} ({max}) yok oldu.");
        }
        // 4b. Yakıcı: rakibin aynı satırı en az 10 ise oradaki en güçlü birim(ler) yanar
        for (var si = 0; si < 2; si++)
        {
            var s = Sides[si];
            var o = Sides[1 - si];
            foreach (var rs in s.Units.Where(u => !u.Token && u.Def.Ability == Ability.RowScorch).ToList())
            {
                if (!s.Units.Contains(rs)) continue;
                var total = o.In(rs.Row).Sum(u => Value(o, u));
                if (total < Tuning.RowScorchMin) continue;
                var cands = o.In(rs.Row).Where(u => Burnable(o, u)).ToList();
                if (cands.Count == 0) continue;
                var max = cands.Max(u => Value(o, u));
                foreach (var u in cands.Where(u => Value(o, u) == max).ToList())
                    Burn(o, u, $"Yakıcı ({rs.Def.Name}): {o.Name} tarafında {u.Def.Name} ({max}) yandı.");
            }
        }
        // 6-7. Eredin, Odak
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            if (s.HasHero(Hero.Eredin) || s.Ambush.Any(u => u.Def.Hero == Hero.Eredin))
            {
                var r = Order[_rng.Next(3)];
                var ep = Tuning.EredinPct[Math.Max(0, s.HeroTier(Hero.Eredin))];
                Sides[1 - i].EredinRow = r;
                Sides[1 - i].EredinPct = ep;
                PreLog.Add($"Eredin: {Sides[1 - i].Name} tarafının {Texts.Name(r)} satırı −%{Texts.Pct(ep * 100)}.");
            }
            s.Focus = Math.Min(Tuning.FocusCap, Tuning.StartFocus + s.Units.Count(u => !u.Token && u.Def.Ability == Ability.Bard)
                      + (s.School == HanLigi.School.Wolf ? 1 : 0) + (s.Th(Tag.Mage) >= 2 ? 1 : 0));
        }
    }

    private bool Burnable(BattleSide s, BattleUnit u) => !u.IsHero && !u.Protected && !Shielded(s, u.Row) && !(u.Has(Tag.Dwarf) && s.Th(Tag.Dwarf) >= 2);

    /// <summary>Birimi yakar (Kavurma, Yakıcı); Ölüm Yankısı hayalet bırakır, Şifacı 3★ dirilişi yarı güçle döndürür.</summary>
    private void Burn(BattleSide s, BattleUnit u, string log)
    {
        s.Units.Remove(u);
        PreLog.Add(log);
        PreFx.Add(new FxEvent("scorch", Array.IndexOf(Sides, s), (int)u.Row));
        if (!u.Token && u.Def.Ability == Ability.DeathEcho)
            s.Units.Add(new BattleUnit { Uid = _nextUid++, Def = u.Def, Power = u.Star == 1 ? 2 : u.Star == 2 ? 3 : 5, Row = u.Row, Token = true });
        else if (u.Revive)
            s.Units.Add(new BattleUnit { Uid = _nextUid++, Def = u.Def, Star = u.Star, Power = Math.Max(1, u.Power / 2), Row = u.Row, Token = true });
    }

    // ---------- pencere ----------

    public int AmbushCap(BattleSide s) => Tuning.AmbushBase + (s.Th(Tag.Bandit) >= 1 ? 1 : 0);

    public bool CanDouble(int side) => Sides[side].HasHero(Hero.Jaskier) && Sides[side].JaskierUses < Tuning.JaskierUses[Sides[side].HeroTier(Hero.Jaskier)];

    /// <summary>Seçilen işaretlerin Odak bedeli (bedavalar düşülmüş); durumu değiştirmez.</summary>
    public int Cost(int side, WindowChoice c) => Cost(side, c, false);

    private int Cost(int side, WindowChoice c, bool commit)
    {
        var s = Sides[side];
        var row = CurrentRow;
        var geralt = s.In(row).Any(u => u.Def.Hero == Hero.Geralt);
        var trissLeft = s.HasHero(Hero.Triss) ? Tuning.TrissFree[s.HeroTier(Hero.Triss)] - s.TrissUses : 0;
        var mage = !s.MageFreeUsed && s.Th(Tag.Mage) >= 3;
        var total = 0;
        for (var i = 0; i < c.Signs.Count; i++)
        {
            var sg = c.Signs[i];
            if (i == 0 && geralt) continue;
            if (sg == Sign.Quen && trissLeft > 0) { trissLeft--; if (commit) s.TrissUses++; continue; }
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
        var amb = c.AmbushUids;
        if (amb.Distinct().Count() != amb.Count || amb.Any(uid => s.Ambush.All(u => u.Uid != uid))) return false;
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
        var mods = new List<(int Side, string Source, double Pct)>();   // arayüz için: hangi yüzde nereden geldi
        var card = new Dictionary<BattleUnit, double>();
        var noWeather = new bool[2];
        var quenBlocks = new int[2];
        var potion = new Potion?[2];
        var ambush = new IReadOnlyList<int>[2];
        void Pct(BattleUnit u, double d) => card[u] = card.GetValueOrDefault(u) + d;

        // Quen'in iadesi yalnız gerçekten Odak ödenmişse (Triss, Geralt ya da Büyücü ile bedava Quen geri ödeme getirmez)
        var quenPaid = new int[2];
        for (var i = 0; i < 2; i++)
            if (ch[i].Signs.Contains(Sign.Quen))
                quenPaid[i] = Cost(i, ch[i]) - Cost(i, ch[i] with { Signs = ch[i].Signs.Where(x => x != Sign.Quen).ToArray() });
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            s.Focus -= Cost(i, ch[i], true);
            if (ch[i].Signs.Count > 1) s.JaskierUses++;
            potion[i] = ch[i].Potion;
            if (potion[i] is { } p) s.Potions.Remove(p);
            ambush[i] = ch[i].AmbushUids;
            foreach (var u in s.Units) u.Landed = false;
        }
        // 1. Yrden: rakibin simya kartını ve pusu inişini keser
        var yrden = new[] { ch[0].Signs.Contains(Sign.Yrden), ch[1].Signs.Contains(Sign.Yrden) };
        for (var i = 0; i < 2; i++)
        {
            if (!yrden[1 - i]) continue;
            var s = Sides[i];
            var hit = false;
            if (potion[i] != null && Tuning.YrdenMode != 2 && Tuning.YrdenPotion) { log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} tarafının {Texts.Name(potion[i]!.Value)} kartı etkisiz."); potion[i] = null; hit = true; }
            // 4: yalnız bilerek indirilen pusu zayıflar; 5: bilerek indirilen pusu bu pencerede inemez (son pencerede kendiliğinden inenlere dokunmaz)
            if (Tuning.YrdenMode == 5 && ambush[i].Count > 0 && s.Th(Tag.Bandit) >= 3)
                log.Add($"{s.Name} 4 Haydut: pusu Yrden'den etkilenmedi.");   // 4 haydut (kademe 3) Yrden'e bağışık
            else if (Tuning.YrdenMode == 5 && ambush[i].Count > 0)
            {
                log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} pusudan inemedi" + (Window < 2 ? " (sonra inebilir)." : "."));
                ambush[i] = Array.Empty<int>();
                hit = true;
            }
            if (Tuning.YrdenMode == 4 && ambush[i].Count > 0)
            {
                log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} tarafının pusudan indirdikleri −%{Tuning.YrdenAmbushPct * 100:0}.");
                hit = true;
            }
            if (Tuning.YrdenMode == 3 && (ambush[i].Count > 0 || (Window == 2 && s.Ambush.Count > 0)))
            {
                log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} tarafının pusudan inenleri −%{Tuning.YrdenAmbushPct * 100:0}.");
                hit = true;
            }
            if (Tuning.YrdenMode is 0 or 2 && s.Th(Tag.Bandit) < 3 && (ambush[i].Count > 0 || (Window == 2 && s.Ambush.Count > 0)))
            {
                log.Add($"{Sides[1 - i].Name} Yrden: {s.Name} pusudan inemedi.");
                ambush[i] = Array.Empty<int>();
                hit = true;
            }
            if (!hit) log.Add($"{Sides[1 - i].Name} Yrden attı, boşa gitti.");
            fx.Add(new FxEvent("yrden", 1 - i, 0, hit));
        }
        // 3. pusu inişleri (son pencerede kalanlar kendiliğinden iner)
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            var blocked = yrden[1 - i] && Tuning.YrdenMode is 0 or 2 && s.Th(Tag.Bandit) < 3;
            var sealedLanding = yrden[1 - i] && Tuning.YrdenMode == 3;
            var chosen = ambush[i].ToHashSet();
            var landing = new List<BattleUnit>();
            foreach (var uid in ambush[i]) if (s.Ambush.FirstOrDefault(u => u.Uid == uid) is { } au) landing.Add(au);
            // son pencerede de pusu isteğe bağlı: seçilmeyen iner değil (Yrden seçileni kesince hiç inmez)
            if (Window == 2 && Tuning.AutoLandLast && !blocked) landing.AddRange(s.Ambush.Where(u => !landing.Contains(u)));
            foreach (var u in landing)
            {
                s.Ambush.Remove(u);
                u.Row = row;
                u.Landed = true;
                s.Units.Add(u);
                if (s.Th(Tag.Bandit) >= 2) Pct(u, s.Th(Tag.Bandit) >= 3 ? 0.5 : 0.25);
                if (sealedLanding || (yrden[1 - i] && Tuning.YrdenMode == 4 && chosen.Contains(u.Uid))) Pct(u, -Tuning.YrdenAmbushPct);
                log.Add($"{s.Name} pusudan {u.Def.Name} indirdi.");
                fx.Add(new FxEvent("ambush", i, u.Uid));
            }
            if (landing.Any(u => u.Def.Hero == Hero.Ciri) && Tuning.CiriRow[s.HeroTier(Hero.Ciri)] is > 0 and var cp)
            {
                foreach (var u in s.In(row).Where(u => !u.IsHero)) Pct(u, cp);
                log.Add($"{s.Name}: Cirilla indi, satırındaki birimler +%{Texts.Pct(cp * 100)}.");
            }
        }
        // 4. iksirler
        for (var i = 0; i < 2; i++)
        {
            if (potion[i] == Potion.Thunderbolt) { own[i] += 0.25; mods.Add((i, "Gök Gürültüsü", 0.25)); log.Add($"{Sides[i].Name}: Gök Gürültüsü (+%25)."); fx.Add(new FxEvent("thunderbolt", i)); }
            if (potion[i] == Potion.Swallow) { noWeather[i] = true; log.Add($"{Sides[i].Name}: Kırlangıç (hava etkisi kalktı)."); fx.Add(new FxEvent("swallow", i)); }
        }
        BattleUnit? Strongest(int side, IEnumerable<BattleUnit>? skip = null) =>
            Sides[side].In(row).Where(u => !u.IsHero && !u.Protected && skip?.Contains(u) != true && !(u.Has(Tag.Knight) && Sides[side].Th(Tag.Knight) >= 3))
                .OrderByDescending(u => Value(Sides[side], u, noWeather[side]) * (1 + card.GetValueOrDefault(u))).FirstOrDefault();
        // Şövalye 1. eşik: Igni ve bombalardan yarı etkilenir (3. eşikte hedef bile olmaz, Strongest atlar)
        double KnightF(int side, BattleUnit u) => u.Has(Tag.Knight) && Sides[side].Th(Tag.Knight) >= 1 ? 0.5 : 1;
        double Amp(int side) => Texts.SignAmp(Sides[side].School, Sides[side].Th(Tag.Mage)).Amp;
        string AmpNote(int side, double basePct)
        {
            var (a, why) = Texts.SignAmp(Sides[side].School, Sides[side].Th(Tag.Mage));
            return Texts.Live(basePct, a, why);
        }
        double Bomb(int side, BattleUnit target, int victim)
        {
            var v = Sides[victim];
            var zol = v.Units.FirstOrDefault(u => u.Def.Hero == Hero.Zoltan);
            if (zol != null)
            {
                var zt = v.HeroTier(Hero.Zoltan);
                if (target.Has(Tag.Dwarf) && (zt >= 1 || zol.Row == row)) return 0;
                if (zt >= 2 && zol.Row == row) return 0;
            }
            return (Sides[side].School == HanLigi.School.Viper ? 1.25 : 1) * (target.Has(Tag.Dwarf) && v.Th(Tag.Dwarf) >= 1 ? 0.5 : 1) * KnightF(victim, target);
        }
        static string BombNote(double basePct, double f) =>
            f <= 0 ? "etkisiz (Zoltan)" : Math.Abs(f - 1) < 1e-9 ? $"−%{Texts.Pct(basePct)}" : $"−%{Texts.Pct(basePct * f)} (−%{Texts.Pct(basePct)} × {Texts.Pct(f)})";
        // 2 + 5. işaretler
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            var o = 1 - i;
            foreach (var sg in ch[i].Signs)
            {
                if (sg == Sign.Aard && Tuning.YrdenBlocksAard && yrden[o]) { log.Add($"{s.Name} Aard attı; {Sides[o].Name} Yrden ile tuttu."); fx.Add(new FxEvent("aard", i, 0, true)); continue; }
                if (sg == Sign.Aard) { foe[o] -= 0.10 * Amp(i); mods.Add((o, "Aard", -0.10 * Amp(i))); log.Add($"{s.Name} Aard: {Sides[o].Name} satırı {AmpNote(i, 10)}."); fx.Add(new FxEvent("aard", i, (int)Math.Round(0.10 * Amp(i) * 100))); }
                if (sg == Sign.Quen && !ch[o].Signs.Contains(Sign.Igni)) log.Add($"{s.Name} Quen attı, boşa gitti.");
                if (sg == Sign.Quen) fx.Add(new FxEvent("quen", i, 0, ch[o].Signs.Contains(Sign.Igni) && potion[i] != Potion.BlackBlood));
                if (sg != Sign.Igni) continue;
                int victim;
                if (potion[o] == Potion.BlackBlood) { victim = i; log.Add($"{s.Name} Igni attı; Kara Kan onu kendisine yansıttı."); }
                else if (ch[o].Signs.Contains(Sign.Quen)) { quenBlocks[o]++; log.Add($"{s.Name} Igni attı; {Sides[o].Name} Quen ile engelledi."); fx.Add(new FxEvent("igni", i, 0, true)); continue; }
                else victim = o;
                if (Strongest(victim) is { } t) { Pct(t, -0.5 * Amp(i) * KnightF(victim, t)); if (victim == o) log.Add($"{s.Name} Igni: {t.Def.Name} {AmpNote(i, 50)}."); fx.Add(new FxEvent(victim == o ? "igni" : "igni_reflect", i, t.Uid)); }
                else { log.Add($"{s.Name} Igni attı ama hedef yok."); fx.Add(new FxEvent("igni", i)); }
            }
        }
        // 4b. Meydan Okuma: rakibin bu satırdaki en güçlü birimi meydan okuyandan güçlüyse −%25
        for (var i = 0; i < 2; i++)
            foreach (var c in Sides[i].In(row).Where(u => !u.Token && u.Def.Ability == Ability.Challenge).ToList())
                if (Strongest(1 - i) is { } t && Value(Sides[1 - i], t, noWeather[1 - i]) * (1 + card.GetValueOrDefault(t)) > Value(Sides[i], c, noWeather[i]) * (1 + card.GetValueOrDefault(c)))
                {
                    Pct(t, -Tuning.ChallengePct);
                    log.Add($"{Sides[i].Name}: {c.Def.Name} meydan okudu, {t.Def.Name} −%{Texts.Pct(Tuning.ChallengePct * 100)}.");
                }
        // 5. bombalar, Eredin, Ayı
        for (var i = 0; i < 2; i++)
        {
            var o = 1 - i;
            if (potion[i] == Potion.DancingStar)
            {
                var a = Strongest(o);
                var b = a == null ? null : Strongest(o, new[] { a });
                var hit = new List<string>();
                foreach (var t in new[] { a, b }) if (t != null) { var bm = Bomb(i, t, o); Pct(t, -0.30 * bm); hit.Add($"{t.Def.Name} {BombNote(30, bm)}"); fx.Add(new FxEvent("dancingstar", i, t.Uid)); }
                if (a == null) fx.Add(new FxEvent("dancingstar", i));
                log.Add($"{Sides[i].Name}: Dans Eden Yıldız ({(hit.Count == 0 ? "hedef yok" : string.Join(", ", hit))}).");
            }
            if (potion[i] == Potion.NorthernWind)
            {
                var t = Strongest(o);
                var nb = t == null ? 1 : Bomb(i, t, o);
                if (t != null) Pct(t, -0.70 * nb);
                fx.Add(new FxEvent("northernwind", i, t?.Uid ?? 0));
                log.Add($"{Sides[i].Name}: Kuzey Rüzgârı ({(t == null ? "hedef yok" : $"{t.Def.Name} {BombNote(70, nb)}")}).");
            }
            if (potion[i] == Potion.BlackBlood && !ch[o].Signs.Contains(Sign.Igni)) log.Add($"{Sides[i].Name} Kara Kan içti, boşa gitti.");
            if (potion[i] == Potion.BlackBlood) fx.Add(new FxEvent("blackblood", i, 0, ch[o].Signs.Contains(Sign.Igni)));
            if (Sides[i].EredinRow == row) foreach (var u in Sides[i].In(row).Where(u => !u.IsHero)) Pct(u, -Sides[i].EredinPct);
            if (quenBlocks[i] > 0 && Sides[i].School == HanLigi.School.Bear) { foe[o] -= 0.10; mods.Add((o, "Ayı karşı vuruşu", -0.10)); log.Add($"{Sides[i].Name} (Ayı): karşı vuruş −%10."); }
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
            if (s.YenPending) { var yp = Tuning.YenPct[Math.Max(0, s.HeroTier(Hero.Yennefer))]; own[i] += yp; s.YenPending = false; mods.Add((i, "Yennefer", yp)); }
            if (s.In(row).Any(u => u.Def.Hero == Hero.Geralt) && Tuning.GeraltRow[s.HeroTier(Hero.Geralt)] is > 0 and var gp) { own[i] += gp; mods.Add((i, "Geralt", gp)); }
            if (s.Th(Tag.Dwarf) >= 3 && s.In(row).Any(u => u.Has(Tag.Dwarf))) { own[i] += 0.2; mods.Add((i, "Cüce sinerjisi", 0.2)); }
            if (s.Th(Tag.Knight) >= 3 && s.In(row).Any(u => u.Has(Tag.Knight))) { own[i] += 0.15; mods.Add((i, "Şövalye sinerjisi", 0.15)); }
            if (Window == 0 && s.Th(Tag.Sailor) >= 1)
                foreach (var u in s.In(row).Where(u => u.Has(Tag.Sailor))) Pct(u, s.Th(Tag.Sailor) >= 3 ? 0.5 : 0.3);
            var horns = s.Act.Horns.Count(h => h == row);
            if (horns > 0) { own[i] += 0.5 * horns; mods.Add((i, horns > 1 ? $"Boynuz ×{horns}" : "Boynuz", 0.5 * horns)); }
        }
        // 8. karşılaştırma
        var val = new decimal[2];
        var cardValues = new Dictionary<int, decimal>();
        var baseValues = new Dictionary<int, int>();
        var rowBase = new decimal[2];
        for (var i = 0; i < 2; i++)
        {
            var s = Sides[i];
            decimal normal = 0, exempt = 0;
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
            if (Tuning.QuenRefund) s.Focus = Math.Min(Tuning.FocusCap, s.Focus + Math.Min(quenBlocks[i], quenPaid[i]));
            if (winner == i)
            {
                s.Points++;
                s.Prev = 1;
                var plunderers = s.In(row).Count(u => !u.Token && u.Def.Ability == Ability.Plunder);
                if (plunderers > 0)
                {
                    var sailor = s.Th(Tag.Sailor) >= 2;
                    var got = Math.Min(plunderers * (sailor ? 2 : 1), (sailor ? Tuning.PlunderCapSailor : Tuning.PlunderCap) - s.Plunder);
                    if (got > 0) { s.Plunder += got; log.Add($"{s.Name}: Yağma +{got} altın."); fx.Add(new FxEvent("plunder", i, got)); }
                }
                if (s.In(row).Any(u => u.Def.Hero == Hero.Yennefer)) s.YenPending = true;
            }
            else if (winner == 1 - i)
            {
                s.Prev = -1;
                if (!s.In(row).Any()) continue;
                var ves = s.In(row).Any(u => u.Def.Hero == Hero.Vesemir);
                var vt = ves ? Math.Max(0, s.HeroTier(Hero.Vesemir)) : -1;
                var gain = Math.Min(ves ? Tuning.VesemirGain[vt] : 1, (ves ? Tuning.RageCap + 1 + vt : Tuning.RageCap) - s.Rage);
                if (gain > 0) { s.Rage += gain; s.Focus = Math.Min(Tuning.FocusCap, s.Focus + gain); log.Add($"{s.Name}: Öfke +{gain} Odak."); fx.Add(new FxEvent("rage", i, gain)); }
            }
            else s.Prev = 0;
        }
        var outcome = new WindowOutcome(Window, row, val[0], val[1], winner, ch[0], ch[1], log, fx, cardValues) { BaseValues = baseValues, RowBase = rowBase, RowMods = mods };
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
            var lv = Tuning.DamageMode switch
            {
                1 => Tuning.StageLevel(Round),
                2 => Math.Min(Sides[w].Level, Tuning.StageLevel(Round) + 1),
                _ => Sides[w].Level
            };
            var h = (int)Math.Ceiling(Tuning.DamageMult * (lv + d));
            h = (int)Math.Ceiling(h * Tuning.DamageRamp(Round));
            if (Sides[1 - w].School == HanLigi.School.Bear) h = (int)Math.Ceiling(h * 0.9);
            if (Pve) h = (int)Math.Ceiling(h / 2.0);
            return h;
        }
    }
}
