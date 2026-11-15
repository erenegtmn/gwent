namespace HanLigi;

public enum Zone { Bench, Close, Ranged, Siege, Ambush, Special }

public enum Phase { School, Prep, Battle, Finished }

/// <summary>Oyuncunun sahip olduğu bir kart (depoda ya da sahada).</summary>
public sealed class OwnedCard
{
    public int Uid { get; init; }
    public required CardDef Def { get; init; }
    public int Star { get; set; } = 1;
    public Zone Zone { get; set; } = Zone.Bench;
    public int Power => Tuning.Power(Def, Star);
    public bool Fielded => Zone != Zone.Bench;
    public Row? Row => Zone switch { Zone.Close => HanLigi.Row.Close, Zone.Ranged => HanLigi.Row.Ranged, Zone.Siege => HanLigi.Row.Siege, _ => null };
}

public enum OpponentKind { Player, Ghost, Pve }

public sealed class PlayerState
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public bool IsBot { get; init; }
    public School? School { get; set; }
    public School[] SchoolOptions { get; set; } = Array.Empty<School>();
    public int Hp { get; set; } = Tuning.StartHp;
    public int Gold { get; set; } = Tuning.StartGold;
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public List<OwnedCard> Cards { get; } = new();
    public CardDef?[] Shop { get; } = new CardDef?[Tuning.ShopSize];
    public List<Potion> Potions { get; } = new();
    public Potion[] PotionOptions { get; set; } = Array.Empty<Potion>();
    public CardDef[] RewardOptions { get; set; } = Array.Empty<CardDef>();
    public bool Alive { get; set; } = true;
    public bool Ready { get; set; }
    /// <summary>Bitiş sırası (1 = kazanan); oyundayken 0.</summary>
    public int Place { get; set; }
    public int EliminatedRound { get; set; }
    public bool FreeRerollUsed { get; set; }
    public bool OrderSwapped { get; set; }
    public OpponentKind OppKind { get; set; }
    /// <summary>Rakip oyuncunun (ya da gölgesi kopyalanan oyuncunun) kimliği; PvE'de −1.</summary>
    public int OppId { get; set; } = -1;
    /// <summary>Bu round'un satır sırası (eşleşmeye özel).</summary>
    public Row[] Order { get; set; } = { Row.Close, Row.Ranged, Row.Siege };
    public int Wins { get; set; }
    public int Losses { get; set; }
    /// <summary>Seri: art arda PvP galibiyeti (+) ya da yenilgisi (−). PvE ve gölge savaşın karşı tarafı saymaz; beraberlik sıfırlar.</summary>
    public int Streak { get; set; }

    public int Slots => Tuning.Slots(Level);
    public int PotionCap => School == HanLigi.School.Viper ? 3 : 2;
    public IEnumerable<OwnedCard> Fielded => Cards.Where(c => c.Fielded);
    public IEnumerable<OwnedCard> Bench => Cards.Where(c => !c.Fielded);
    public int XpNeeded => Level >= Tuning.MaxLevel ? 0 : Tuning.XpThresholds[Level - 1];

    /// <summary>Etiket sayıları: sahadaki (satırlar + pusu) farklı isimli birimler (§15.8).</summary>
    public Dictionary<Tag, int> TagCounts()
    {
        var d = new Dictionary<Tag, int>();
        foreach (var def in Fielded.Where(c => c.Def.IsUnit).Select(c => c.Def).Distinct())
            foreach (var t in def.Tags) d[t] = d.GetValueOrDefault(t) + 1;
        return d;
    }

    public int Th(Tag t) => TagCounts().TryGetValue(t, out var n) ? n >= 6 ? 3 : n >= 4 ? 2 : n >= 2 ? 1 : 0 : 0;
    public int AmbushCap => Th(Tag.Bandit) >= 1 ? 3 : 2;
}

/// <summary>Bir round'da oynanan savaşın kaydı (sonuç ekranı için).</summary>
public sealed record BattleRecord(int Round, int PlayerA, string NameA, string NameB, int PlayerB, int PointsA, int PointsB, int Winner, int Damage, bool Pve, bool Ghost);

/// <summary>
/// Han Ligi maçı (YENI_MOD.md §15): 2-8 oyuncu, ortak havuz, market, evolve, round akışı. Durumu yalnız genel yöntemler
/// değiştirir; reddedilen eylem <c>false</c> döner ve durumu değiştirmez. Tüm rastgelelik tohumdan gelir.
/// </summary>
public sealed class Match
{
    public List<PlayerState> Players { get; } = new();
    public Dictionary<string, int> Pool { get; } = new();
    public int Round { get; private set; }
    public Phase Phase { get; private set; } = Phase.School;
    public List<Battle> Battles { get; } = new();
    public List<BattleRecord> History { get; } = new();
    public int Seed { get; }
    private readonly Random _shop, _pair, _misc;
    private int _nextUid = 1;

    public bool IsPve => Round is 1 or 2 || Round % 5 == 0;
    public IEnumerable<PlayerState> Alive => Players.Where(p => p.Alive);

    public Match(int seed, IReadOnlyList<(string Name, bool Bot)> players)
    {
        if (players.Count is < 2 or > 8) throw new ArgumentException("2-8 oyuncu");
        Seed = seed;
        _shop = new Random(seed);
        _pair = new Random(seed ^ 0x5f3759df);
        _misc = new Random(seed ^ 0x1234567);
        for (var i = 0; i < players.Count; i++) Players.Add(new PlayerState { Id = i, Name = players[i].Name, IsBot = players[i].Bot });
        var n = players.Count;
        foreach (var d in CardSet.All.Where(d => !d.IsHero))
            Pool[d.Id] = (int)Math.Ceiling(Tuning.Copies[d.Tier - 1] * n / 8.0);
        // Kahraman sayısı = oyuncu sayısı (küçük lobide yoğunluk artmasın)
        foreach (var h in CardSet.All.Where(d => d.IsHero).OrderBy(_ => _misc.Next()).Take(n)) Pool[h.Id] = 1;
        var schools = Enum.GetValues<School>();
        foreach (var p in Players) p.SchoolOptions = schools.OrderBy(_ => _misc.Next()).Take(3).ToArray();
    }

    // ---------- okul ----------

    public bool ChooseSchool(int pid, School s)
    {
        var p = Players[pid];
        if (Phase != Phase.School || p.School != null || !p.SchoolOptions.Contains(s)) return false;
        p.School = s;
        if (Players.All(x => x.School != null)) BeginRound();
        return true;
    }

    // ---------- round başı ----------

    private void BeginRound()
    {
        Round++;
        Phase = Phase.Prep;
        Battles.Clear();
        foreach (var p in Alive)
        {
            p.Ready = false;
            p.FreeRerollUsed = false;
            p.OrderSwapped = false;
            if (Round >= 2) p.Gold += Math.Min(Tuning.InterestCap, p.Gold / Tuning.InterestPer);
            p.Gold += Tuning.Income;
            if (Round >= 2) p.Gold += Tuning.StreakGold(p.Streak);
            p.Gold += Math.Min(2, p.Fielded.Count(c => c.Def.Ability == Ability.Treasure));
            var noble = p.Th(Tag.Noble);
            p.Gold += noble >= 3 ? 2 : noble >= 1 ? 1 : 0;
            p.Xp += Tuning.XpPerRound;
            LevelUp(p);
            ReturnShop(p);
            RollShop(p);
            p.PotionOptions = Array.Empty<Potion>();
            if (Round >= Tuning.PotionsFromRound && p.Potions.Count < p.PotionCap)
                p.PotionOptions = Enum.GetValues<Potion>().OrderBy(_ => _misc.Next()).Take(p.School == School.Manticore ? 3 : 2).ToArray();
        }
        Pair();
    }

    private void Pair()
    {
        var alive = Alive.ToList();
        foreach (var p in alive)
        {
            p.OppKind = OpponentKind.Pve;
            p.OppId = -1;
            p.Order = Enum.GetValues<Row>().OrderBy(_ => _pair.Next()).ToArray();
        }
        if (IsPve) return;
        var order = alive.OrderBy(_ => _pair.Next()).ToList();
        for (var i = 0; i + 1 < order.Count; i += 2)
        {
            var (a, b) = (order[i], order[i + 1]);
            a.OppKind = b.OppKind = OpponentKind.Player;
            a.OppId = b.Id;
            b.OppId = a.Id;
            b.Order = a.Order;
        }
        if (order.Count % 2 == 1)
        {
            var lone = order[^1];
            var others = alive.Where(x => x != lone).ToList();
            lone.OppKind = OpponentKind.Ghost;
            lone.OppId = others[_pair.Next(others.Count)].Id;
        }
    }

    private void LevelUp(PlayerState p)
    {
        while (p.Level < Tuning.MaxLevel && p.Xp >= Tuning.XpThresholds[p.Level - 1]) p.Level++;
    }

    // ---------- market ----------

    private void ReturnShop(PlayerState p)
    {
        for (var i = 0; i < p.Shop.Length; i++)
        {
            if (p.Shop[i] is { } d) Pool[d.Id]++;
            p.Shop[i] = null;
        }
    }

    private void RollShop(PlayerState p)
    {
        for (var i = 0; i < p.Shop.Length; i++)
        {
            CardDef? got = null;
            var heroes = CardSet.All.Where(d => d.IsHero && Pool.GetValueOrDefault(d.Id) > 0).ToList();
            if (heroes.Count > 0 && _shop.NextDouble() < Tuning.HeroOdds[p.Level - 1]) got = heroes[_shop.Next(heroes.Count)];
            else
            {
                var roll = _shop.Next(100);
                var odds = Tuning.ShopOdds[p.Level - 1];
                var tier = 1;
                for (var acc = 0; tier <= 5; tier++) { acc += odds[tier - 1]; if (roll < acc) break; }
                tier = Math.Min(tier, 5);
                for (; tier >= 1 && got == null; tier--)
                {
                    var cands = CardSet.All.Where(d => !d.IsHero && d.Tier == tier && Pool[d.Id] > 0).ToList();
                    var total = cands.Sum(d => Pool[d.Id]);
                    if (total == 0) continue;
                    var pick = _shop.Next(total);
                    foreach (var d in cands) { pick -= Pool[d.Id]; if (pick < 0) { got = d; break; } }
                }
            }
            if (got != null) Pool[got.Id]--;
            p.Shop[i] = got;
        }
    }

    private bool InPrep(PlayerState p) => Phase == Phase.Prep && p.Alive && !p.Ready;

    public int RerollCost(int pid) => Players[pid].Th(Tag.Noble) >= 2 && !Players[pid].FreeRerollUsed ? 0 : Tuning.RerollCost;

    public bool Reroll(int pid)
    {
        var p = Players[pid];
        var cost = RerollCost(pid);
        if (!InPrep(p) || p.Gold < cost) return false;
        if (cost == 0) p.FreeRerollUsed = true;
        p.Gold -= cost;
        ReturnShop(p);
        RollShop(p);
        return true;
    }

    public bool BuyXp(int pid)
    {
        var p = Players[pid];
        if (!InPrep(p) || p.Level >= Tuning.MaxLevel || p.Gold < Tuning.XpCost) return false;
        p.Gold -= Tuning.XpCost;
        p.Xp += Tuning.XpGain;
        LevelUp(p);
        return true;
    }

    /// <summary>Bu kartı almak anında bir birleşme tamamlar mı (depo doluyken satın almanın koşulu).</summary>
    private static bool WouldMerge(PlayerState p, CardDef d) => d.IsUnit && !d.IsHero && p.Cards.Count(c => c.Def == d && c.Star == 1) >= 2;

    public bool CanBuy(int pid, int slot)
    {
        var p = Players[pid];
        if (!InPrep(p) || slot < 0 || slot >= p.Shop.Length || p.Shop[slot] is not { } d) return false;
        if (p.Gold < d.Price) return false;
        return p.Bench.Count() < Tuning.BenchSize || WouldMerge(p, d);
    }

    public bool Buy(int pid, int slot)
    {
        if (!CanBuy(pid, slot)) return false;
        var p = Players[pid];
        var d = p.Shop[slot]!;
        p.Gold -= d.Price;
        p.Shop[slot] = null;
        p.Cards.Add(new OwnedCard { Uid = _nextUid++, Def = d });
        Merge(p);
        return true;
    }

    /// <summary>Evolve: aynı isim ve yıldızdan 3 birim birleşir; biri sahadaysa sonuç o yerde kalır.</summary>
    private void Merge(PlayerState p)
    {
        for (var again = true; again;)
        {
            again = false;
            foreach (var g in p.Cards.Where(c => c.Def.IsUnit && !c.Def.IsHero && c.Star < 3).GroupBy(c => (c.Def, c.Star)).ToList())
            {
                var three = g.OrderByDescending(c => c.Fielded).Take(3).ToList();
                if (three.Count < 3) continue;
                var keep = three[0];
                keep.Star++;
                p.Cards.Remove(three[1]);
                p.Cards.Remove(three[2]);
                again = true;
                break;
            }
        }
    }

    public bool Sell(int pid, int uid)
    {
        var p = Players[pid];
        if (!InPrep(p) || p.Cards.FirstOrDefault(c => c.Uid == uid) is not { } c) return false;
        p.Gold += Tuning.SellValue(c.Def, c.Star);
        Pool[c.Def.Id] += c.Def.IsHero ? 1 : c.Star == 1 ? 1 : c.Star == 2 ? 3 : 9;
        p.Cards.Remove(c);
        return true;
    }

    /// <summary>Kartın gidebileceği saha yerleri.</summary>
    public static IEnumerable<Zone> ZonesFor(CardDef d)
    {
        if (!d.IsUnit) { yield return Zone.Special; yield break; }
        if (d.Agile) { yield return Zone.Close; yield return Zone.Ranged; }
        else yield return d.Row switch { Row.Close => Zone.Close, Row.Ranged => Zone.Ranged, _ => Zone.Siege };
        if (d.Ability == Ability.Ambush) yield return Zone.Ambush;
    }

    public bool CanMove(int pid, int uid, Zone to)
    {
        var p = Players[pid];
        if (!InPrep(p) || p.Cards.FirstOrDefault(c => c.Uid == uid) is not { } c || c.Zone == to) return false;
        if (to == Zone.Bench) return p.Bench.Count() < Tuning.BenchSize;
        if (!ZonesFor(c.Def).Contains(to)) return false;
        if (!c.Fielded && p.Fielded.Count() >= p.Slots) return false;
        if (to == Zone.Ambush && p.Cards.Count(x => x.Zone == Zone.Ambush) >= p.AmbushCap) return false;
        return true;
    }

    public bool Move(int pid, int uid, Zone to)
    {
        if (!CanMove(pid, uid, to)) return false;
        Players[pid].Cards.First(c => c.Uid == uid).Zone = to;
        return true;
    }

    public bool ChoosePotion(int pid, int index)
    {
        var p = Players[pid];
        if (!InPrep(p) || index < 0 || index >= p.PotionOptions.Length || p.Potions.Count >= p.PotionCap) return false;
        p.Potions.Add(p.PotionOptions[index]);
        p.PotionOptions = Array.Empty<Potion>();
        return true;
    }

    public bool PickReward(int pid, int index)
    {
        var p = Players[pid];
        if (!InPrep(p) || index < 0 || index >= p.RewardOptions.Length) return false;
        var d = p.RewardOptions[index];
        if (p.Bench.Count() >= Tuning.BenchSize && !WouldMerge(p, d)) return false;
        for (var i = 0; i < p.RewardOptions.Length; i++) if (i != index) Pool[p.RewardOptions[i].Id]++;
        p.RewardOptions = Array.Empty<CardDef>();
        p.Cards.Add(new OwnedCard { Uid = _nextUid++, Def = d });
        Merge(p);
        return true;
    }

    /// <summary>Kedi okulu: satır sırasında iki satırın yerini değiştirir (round başına bir kez; rakip de Kedi ise yapılamaz).</summary>
    public bool SwapOrder(int pid, int i, int j)
    {
        var p = Players[pid];
        if (!InPrep(p) || p.School != School.Cat || p.OrderSwapped || i == j || i is < 0 or > 2 || j is < 0 or > 2) return false;
        if (p.OppKind == OpponentKind.Player && Players[p.OppId].School == School.Cat) return false;
        var o = (Row[])p.Order.Clone();
        (o[i], o[j]) = (o[j], o[i]);
        p.Order = o;
        if (p.OppKind == OpponentKind.Player) Players[p.OppId].Order = o;
        p.OrderSwapped = true;
        return true;
    }

    /// <summary>Hazırlığı bitirir; herkes hazırsa savaşlar kurulur.</summary>
    public bool SetReady(int pid)
    {
        var p = Players[pid];
        if (!InPrep(p)) return false;
        p.Ready = true;
        if (Alive.All(x => x.Ready)) Lock();
        return true;
    }

    /// <summary>Süre doldu: hazır olmayanlar da kilitlenir.</summary>
    public void ForceLock()
    {
        if (Phase != Phase.Prep) return;
        foreach (var p in Alive) p.Ready = true;
        Lock();
    }

    // ---------- kilit ve savaşlar ----------

    private BattleSide Snapshot(PlayerState p, string? nameOverride = null)
    {
        var s = new BattleSide { PlayerId = p.Id, Name = nameOverride ?? p.Name, Level = p.Level, School = p.School };
        // Pusu sınırı aşıldıysa (eşik kaybı) fazlası kendi satırına döner
        var amb = p.Cards.Where(c => c.Zone == Zone.Ambush).ToList();
        for (var i = p.AmbushCap; i < amb.Count; i++) amb[i].Zone = ZonesFor(amb[i].Def).First();
        foreach (var c in p.Cards)
        {
            if (!c.Fielded) { s.Bench.Add((c.Def, c.Star)); continue; }
            if (c.Zone == Zone.Special) { s.Specials.Add(c.Def.Special); continue; }
            var u = new BattleUnit { Uid = c.Uid, Def = c.Def, Star = c.Star, Power = c.Power, Row = c.Row ?? Row.Close };
            if (c.Zone == Zone.Ambush) s.Ambush.Add(u); else s.Units.Add(u);
        }
        foreach (var kv in p.TagCounts()) s.Tags[kv.Key] = kv.Value;
        s.Potions.AddRange(p.Potions);
        return s;
    }

    /// <summary>Arayüz için: oyuncunun şu anki sahasının savaş görünümü (kart değerleri ve satır toplamları; rakipsiz).</summary>
    public Battle Preview(int pid)
    {
        var b = new Battle(Snapshot(Players[pid]), new BattleSide { Name = "" }, Players[pid].Order, 0);
        b.Sides[0].Act = b.DefaultActivation(0);
        return b;
    }

    /// <summary>PvE rakibi: oyuncunun seviyesinde rastgele bir tahta; gücü %50 (R1-2) ya da %80.</summary>
    private BattleSide PveSide(PlayerState p)
    {
        var s = new BattleSide { PlayerId = -1, Name = Round <= 2 ? "Canavar Sürüsü" : "Vahşi Av Öncüleri", Level = p.Level };
        var scale = Round <= 2 ? Tuning.PveEarly : Tuning.PveLater;
        var odds = Tuning.ShopOdds[p.Level - 1];
        for (var i = 0; i < p.Slots; i++)
        {
            var roll = _misc.Next(100);
            var tier = 1;
            for (var acc = 0; tier <= 5; tier++) { acc += odds[tier - 1]; if (roll < acc) break; }
            tier = Math.Min(tier, 5);
            var cands = CardSet.All.Where(d => d.IsUnit && !d.IsHero && d.Tier == tier && d.Has(Tag.Monster)).ToList();
            if (cands.Count == 0) cands = CardSet.All.Where(d => d.IsUnit && !d.IsHero && d.Tier == tier).ToList();
            var d = cands[_misc.Next(cands.Count)];
            s.Units.Add(new BattleUnit { Uid = 900000 + i, Def = d, Power = Math.Max(1, Tuning.Round(d.Power * scale)), Row = d.Row });
        }
        // PvE'de etiket eşikleri sayılmaz (güç zaten ölçeklendi)
        if (Round == 2) s.Specials.Add(Special.Frost);   // öğretici: hava
        return s;
    }

    private void Lock()
    {
        foreach (var p in Alive.ToList())
        {
            // seçilmemiş ödül: yer varsa ilk seçenek, yoksa havuza döner
            if (p.RewardOptions.Length == 0) continue;
            p.Ready = false;
            if (!PickReward(p.Id, 0)) { foreach (var r in p.RewardOptions) Pool[r.Id]++; p.RewardOptions = Array.Empty<CardDef>(); }
            p.Ready = true;
        }
        Phase = Phase.Battle;
        Battles.Clear();
        var done = new HashSet<int>();
        foreach (var p in Alive.ToList())
        {
            if (done.Contains(p.Id)) continue;
            Battle b;
            if (p.OppKind == OpponentKind.Player)
            {
                var o = Players[p.OppId];
                done.Add(o.Id);
                b = new Battle(Snapshot(p), Snapshot(o), p.Order, _pair.Next());
            }
            else if (p.OppKind == OpponentKind.Ghost)
                b = new Battle(Snapshot(p), Snapshot(Players[p.OppId], Players[p.OppId].Name + " (gölge)"), p.Order, _pair.Next()) { Ghost = true };
            else
                b = new Battle(Snapshot(p), PveSide(p), p.Order, _pair.Next()) { Pve = true, NoDamage = Round <= 2 };
            done.Add(p.Id);
            Spy(b, 0);
            Spy(b, 1);
            Battles.Add(b);
        }
    }

    /// <summary>Casus (§15.9): kilit anında rakibin sahasından oranlı sayıda rastgele kart gösterir.</summary>
    private void Spy(Battle b, int side)
    {
        var me = b.Sides[side];
        var op = b.Sides[1 - side];
        var all = new List<(CardDef, int, string)>();
        foreach (var u in op.Units) all.Add((u.Def, u.Star, Texts.Name(u.Row)));
        foreach (var sp in op.Specials) all.Add((CardSet.All.First(d => d.Special == sp), 1, "Özel"));
        var spies = me.Units.Concat(me.Ambush).Where(u => u.Def.Ability == Ability.Spy).ToList();
        var n = spies.Sum(u => (int)Math.Ceiling(Tuning.SpyRate[u.Star - 1] * all.Count));
        if (spies.Count > 0 && me.School == School.Cat) n++;
        if (me.School == School.Cat && spies.Count == 0) n = Math.Min(1, all.Count);
        n = Math.Min(n, all.Count);
        b.Revealed[side].AddRange(all.OrderBy(_ => _misc.Next()).Take(n));
        if (spies.Any(u => u.Star >= 3)) b.AmbushSeen[side] = op.Ambush.Count;
    }

    public Battle? BattleOf(int pid) => Battles.FirstOrDefault(b => b.Sides[0].PlayerId == pid || (!b.Pve && !b.Ghost && b.Sides[1].PlayerId == pid));

    public int SideOf(Battle b, int pid) => b.Sides[0].PlayerId == pid ? 0 : 1;

    /// <summary>Tüm savaşlar bitince round'u kapatır: hasar, altın, ödüller, elemeler; sonra yeni round ya da bitiş.</summary>
    public bool EndRound()
    {
        if (Phase != Phase.Battle || Battles.Any(b => !b.Done)) return false;
        foreach (var b in Battles)
        {
            var (sa, sb) = (b.Sides[0], b.Sides[1]);
            var ghost = b.Ghost;
            var w = b.Winner;
            var dmg = b.Damage;
            History.Add(new BattleRecord(Round, sa.PlayerId, sa.Name, sb.Name, b.Pve ? -1 : sb.PlayerId, sa.Points, sb.Points, w, dmg, b.Pve, ghost));
            for (var i = 0; i < 2; i++)
            {
                var side = b.Sides[i];
                if (side.PlayerId < 0 || (i == 1 && ghost)) continue;
                var p = Players[side.PlayerId];
                // kullanılan simya kartları harcanır
                p.Potions.Clear();
                p.Potions.AddRange(side.Potions);
                if (w == i) { p.Gold += Tuning.WinGold; p.Wins++; }
                else if (w == 1 - i) { p.Hp -= dmg; p.Losses++; }
                if (!b.Pve) p.Streak = w == i ? Math.Max(0, p.Streak) + 1 : w == 1 - i ? Math.Min(0, p.Streak) - 1 : 0;
                // PvE ödülü: R1-2'de her durumda, sonra yalnız kazanana
                if (b.Pve && (Round <= 2 || w == i)) p.RewardOptions = RewardCards(p);
            }
        }
        var dead = Alive.Where(p => p.Hp <= 0).OrderBy(p => p.Hp).ToList();
        var place = Alive.Count();
        foreach (var grp in dead.GroupBy(p => p.Hp).OrderBy(g => g.Key))
        {
            var cnt = grp.Count();
            foreach (var p in grp)
            {
                p.Alive = false;
                p.Place = place - cnt + 1;
                p.EliminatedRound = Round;
                foreach (var c in p.Cards) Pool[c.Def.Id] += c.Def.IsHero ? 1 : c.Star == 1 ? 1 : c.Star == 2 ? 3 : 9;
                p.Cards.Clear();
                ReturnShop(p);
                foreach (var r in p.RewardOptions) Pool[r.Id]++;
                p.RewardOptions = Array.Empty<CardDef>();
            }
            place -= cnt;
        }
        var alive = Alive.ToList();
        if (alive.Count <= 1)
        {
            if (alive.Count == 1) alive[0].Place = 1;
            else foreach (var p in dead.Where(p => p.Hp == dead.Max(x => x.Hp))) p.Place = 1;   // hepsi aynı anda elendi: canı en yüksek olan
            Phase = Phase.Finished;
            Battles.Clear();
            return true;
        }
        BeginRound();
        return true;
    }

    /// <summary>PvE ödülü: bir üst seviyenin market olasılıklarıyla 3 kart (kahraman yok).</summary>
    private CardDef[] RewardCards(PlayerState p)
    {
        var odds = Tuning.ShopOdds[Math.Min(Tuning.MaxLevel, p.Level + 1) - 1];
        var res = new List<CardDef>();
        for (var i = 0; i < 3; i++)
        {
            var roll = _misc.Next(100);
            var tier = 1;
            for (var acc = 0; tier <= 5; tier++) { acc += odds[tier - 1]; if (roll < acc) break; }
            tier = Math.Min(tier, 5);
            CardDef? got = null;
            for (; tier >= 1 && got == null; tier--)
            {
                var cands = CardSet.All.Where(d => !d.IsHero && d.Tier == tier && Pool[d.Id] > 0 && !res.Contains(d)).ToList();
                if (cands.Count > 0) got = cands[_misc.Next(cands.Count)];
            }
            if (got == null) continue;
            Pool[got.Id]--;
            res.Add(got);
        }
        return res.ToArray();
    }

    /// <summary>Kart korunumu denetimi için: bir kartın oyundaki toplam kopya sayısı.</summary>
    public int TotalCopies(string id)
    {
        var n = Pool.GetValueOrDefault(id);
        foreach (var p in Players)
        {
            n += p.Shop.Count(d => d?.Id == id) + p.RewardOptions.Count(d => d.Id == id);
            foreach (var c in p.Cards.Where(c => c.Def.Id == id)) n += c.Def.IsHero ? 1 : c.Star == 1 ? 1 : c.Star == 2 ? 3 : 9;
        }
        return n;
    }
}
