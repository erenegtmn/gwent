namespace HanLigi;

/// <summary>
/// Online süreler (sn). İstemci sayacı bunlarla çalışır ve süre dolunca kendi varsayılanını yollar; sunucu <see cref="Grace"/>
/// kadar sonra hâlâ bekleyen varsa onun yerine varsayılanı uygular. Hazırlık sayacı istemcide savaş sonu gösterimi bittikten
/// sonra başladığından sunucu ona <see cref="ResultHold"/> da ekler.
/// </summary>
public static class HanTimes
{
    public const int Prep = 35;
    public const int Activation = 15;
    public const int Window = 15;
    public const int Grace = 5;
    /// <summary>İstemcinin savaş sonucu gösterimi (sinematik) için round sonunu beklettiği en uzun süre.</summary>
    public const int ResultHold = 4;
    /// <summary>İstemcinin bir satırın çözümünü adım adım gösterdiği en uzun süre (pencere sayacı ondan sonra başlar).</summary>
    public const int WindowShow = 10;
}

/// <summary>Kilit adımlı (online) Han Ligi eylemi türü.</summary>
public enum HanActKind
{
    School, Buy, Sell, Move, Reroll, Xp, Reward, Potion, Swap, Ready,
    /// <summary>Savaş öncesi hedefler: A = savaş no, B = taraf, L = [boynuz sayısı, kalkan sayısı, boynuz satırları..., kalkan satırları..., (moral uid, satır)...].</summary>
    Activate,
    /// <summary>Pencere seçimi: A = savaş no, B = taraf, C = simya (−1 yok), D = pusu uid (−1 yok), L = işaretler.</summary>
    Window,
    /// <summary>Yalnız sunucu: hazırlık süresi doldu, herkes kilitlenir.</summary>
    Lock,
    /// <summary>Yalnız sunucu: bütün savaşlar bitti, round kapanır.</summary>
    EndRound,
    /// <summary>Yalnız sunucu: oyuncu ayrıldı, koltuğunu bundan sonra yapay zekâ oynar.</summary>
    Abandon,
    /// <summary>Tezgâh kilidi aç/kapa (sonraki round tezgâh aynen kalır).</summary>
    LockShop,
    /// <summary>Oyuncu kendi savaşının sonunu ekranda izledi (sinematik bitti). Sunucu herkes izleyince round'u kapatır.</summary>
    RoundSeen
}

/// <summary>
/// Ağda taşınan eylem (düz alanlar: JSON'da sorunsuz). <c>Player</c> eylemi yapan koltuk; sunucu eylemlerinde −1 olabilir.
/// </summary>
public sealed record HanAction(HanActKind Kind, int Player, int A = 0, int B = 0, int C = -1, int D = -1, List<int>? L = null)
{
    public static HanAction Activation(int player, int battle, int side, Activation act)
    {
        var l = new List<int> { act.Horns.Count, act.Shields.Count };
        l.AddRange(act.Horns.Select(r => (int)r));
        l.AddRange(act.Shields.Select(r => (int)r));
        foreach (var kv in act.Morale.OrderBy(k => k.Key)) { l.Add(kv.Key); l.Add((int)kv.Value); }
        return new HanAction(HanActKind.Activate, player, battle, side, L: l);
    }

    public static HanAction Choice(int player, int battle, int side, WindowChoice c)
        => new(HanActKind.Window, player, battle, side, c.Potion is { } p ? (int)p : -1, c.AmbushUid ?? -1,
            c.Signs.Select(s => (int)s).Concat(c.MoreAmbush is { Count: > 0 } more ? new[] { -1 }.Concat(more) : Array.Empty<int>()).ToList());

    public Activation? ToActivation()
    {
        if (L is not { Count: >= 2 } l) return null;
        int h = l[0], s = l[1];
        if (h < 0 || s < 0 || l.Count < 2 + h + s || (l.Count - 2 - h - s) % 2 != 0) return null;
        var a = new Activation();
        for (var i = 0; i < h; i++) a.Horns.Add((Row)l[2 + i]);
        for (var i = 0; i < s; i++) a.Shields.Add((Row)l[2 + h + i]);
        for (var i = 2 + h + s; i < l.Count; i += 2) a.Morale[l[i]] = (Row)l[i + 1];
        return Enum.GetValues<Row>().Length == 3 && a.Horns.Concat(a.Shields).Concat(a.Morale.Values).All(r => (int)r is >= 0 and <= 2) ? a : null;
    }

    public WindowChoice? ToChoice()
    {
        var all = (L ?? new List<int>()).ToList();
        var cut = all.IndexOf(-1);
        var signs = cut < 0 ? all : all.Take(cut).ToList();
        var more = cut < 0 ? new List<int>() : all.Skip(cut + 1).ToList();
        if (signs.Any(s => s is < 0 or > 3) || C < -1 || C >= Enum.GetValues<Potion>().Length) return null;
        if (D < 0 && more.Count > 0) return null;
        return new WindowChoice(signs.Select(s => (Sign)s).ToArray(), C >= 0 ? (Potion)C : null, D >= 0 ? D : null, more.Count > 0 ? more : null);
    }
}

/// <summary>
/// Kilit adımlı Han Ligi: sunucu ve her istemci aynı tohumla bir <see cref="HanGame"/> kurar ve <b>aynı eylemleri aynı sırayla</b>
/// uygular; sonuç her kopyada birebir aynıdır. Botlar (boş koltuklar ve ayrılan oyuncular) ağdan gelmez: kararlarını bu sınıf,
/// tohumdan türetilen sabit rastgelelikle, eylem akışının belirli noktalarında verir (round başı hazırlık, savaş kurulumu,
/// pencere seçimleri). Reddedilen eylem durumu değiştirmez.
/// </summary>
public sealed class HanGame
{
    public Match M { get; }
    public int Seed { get; }
    /// <summary>Yapay zekânın oynadığı koltuklar.</summary>
    public HashSet<int> Bots { get; } = new();
    /// <summary>Hedefleri (aktivasyon) seçilmiş taraflar: (savaş no, taraf).</summary>
    private readonly HashSet<(int, int)> _activated = new();
    public int Applied { get; private set; }

    public HanGame(int seed, IReadOnlyList<(string Name, bool Bot)> players)
    {
        Seed = seed;
        M = new Match(seed, players);
        for (var i = 0; i < players.Count; i++) if (players[i].Bot) Bots.Add(i);
        foreach (var p in M.Players.Where(p => Bots.Contains(p.Id))) M.ChooseSchool(p.Id, p.SchoolOptions[Rng(1, p.Id).Next(p.SchoolOptions.Length)]);
        AfterRoundBegins();
    }

    /// <summary>Tohumdan ve bir anahtardan türetilen sabit rastgelelik (süreçten bağımsız: HashCode kullanılmaz).</summary>
    private Random Rng(params int[] key)
    {
        unchecked
        {
            var h = (uint)Seed * 2654435761u ^ 0x9E3779B9u;
            foreach (var k in key) { h ^= (uint)k + 0x7F4A7C15u + (h << 6) + (h >> 2); h *= 0x85EBCA6Bu; h ^= h >> 13; }
            return new Random((int)(h & 0x7FFFFFFF));
        }
    }

    public bool IsBotSide(Battle b, int side) => (side == 1 && (b.Pve || b.Ghost)) || Bots.Contains(b.Sides[side].PlayerId);

    /// <summary>Savaşın bir tarafını yöneten insan koltuğu (bot ya da PvE ise −1).</summary>
    public int Owner(Battle b, int side) => IsBotSide(b, side) ? -1 : b.Sides[side].PlayerId;

    public int BattleIndexOf(int pid) => M.BattleOf(pid) is { } b ? M.Battles.IndexOf(b) : -1;

    public bool Activated(int battle, int side) => _activated.Contains((battle, side));

    /// <summary>İnsanlar ne bekliyor? Sunucu süre aşımında bunlar için varsayılan eylem yollar.</summary>
    public IEnumerable<HanAction> PendingDefaults()
    {
        if (M.Phase == Phase.School)
        {
            foreach (var p in M.Players.Where(p => !Bots.Contains(p.Id) && p.School == null)) yield return new HanAction(HanActKind.School, p.Id, (int)p.SchoolOptions[0]);
            yield break;
        }
        if (M.Phase == Phase.Prep)
        {
            if (M.Alive.Any(p => !Bots.Contains(p.Id) && !p.Ready)) yield return new HanAction(HanActKind.Lock, -1);
            yield break;
        }
        if (M.Phase != Phase.Battle) yield break;
        for (var i = 0; i < M.Battles.Count; i++)
        {
            var b = M.Battles[i];
            for (var s = 0; s < 2; s++)
            {
                var owner = Owner(b, s);
                if (owner < 0) continue;
                if (!b.Started && !_activated.Contains((i, s))) yield return HanAction.Activation(owner, i, s, b.DefaultActivation(s));
                else if (b.Started && !b.Done && !b.Submitted(s)) yield return HanAction.Choice(owner, i, s, WindowChoice.None);
            }
        }
    }

    /// <summary>Savaşın aşama anahtarı (sunucu süre ölçümü için): aşama değişince sayaç baştan başlar.</summary>
    public string StageOf(int battle) => battle < 0 || battle >= M.Battles.Count ? "" : M.Battles[battle] is var b && b.Done ? "done" : b.Started ? "w" + b.Window : "act";

    /// <summary>Round kapanabilir mi (bütün savaşlar bitti)? Sunucu o zaman <see cref="HanActKind.EndRound"/> yollar.</summary>
    public bool RoundCanEnd => M.Phase == Phase.Battle && M.Battles.All(b => b.Done);

    /// <summary>Bu round'da kendi savaşının sonunu izlediğini bildiren insanlar.</summary>
    private readonly HashSet<int> _seen = new();

    /// <summary>
    /// Savaşı olan bütün insanlar savaşlarının sonunu izledi mi. Online'da round, motor bitirse bile ancak o zaman kapanır:
    /// son masadakiler satır çözümünü ve sonucu izlerken ötekiler sonraki round'un tezgâhına geçmesin, herkes birlikte geçsin.
    /// </summary>
    public bool Seen(int pid) => _seen.Contains(pid);

    public bool AllSeen => M.Phase == Phase.Battle && M.Battles.SelectMany((b, i) => new[] { Owner(b, 0), Owner(b, 1) }).Where(o => o >= 0).All(_seen.Contains);

    /// <summary>Eylemi uygular. Geçersizse false ve durum değişmez.</summary>
    public bool Apply(HanAction a)
    {
        var ok = ApplyCore(a);
        if (!ok) return false;
        Applied++;
        // biten savaşlar hemen işlenir: can o anda düşer, eleme sırası savaşın bitiş anına göre belirlenir
        if (M.Phase == Phase.Battle) foreach (var b in M.Battles.Where(b => b.Done && !b.Settled).ToList()) M.Settle(b);
        return true;
    }

    private bool Human(int pid) => pid >= 0 && pid < M.Players.Count && !Bots.Contains(pid) && M.Players[pid].Alive;

    private bool ApplyCore(HanAction a)
    {
        var pid = a.Player;
        switch (a.Kind)
        {
            case HanActKind.School:
                if (!Human(pid) || M.Phase != Phase.School) return false;
                if (!Enum.IsDefined(typeof(School), a.A) || !M.ChooseSchool(pid, (School)a.A)) return false;
                AfterRoundBegins();
                return true;
            case HanActKind.Buy: return Human(pid) && M.Buy(pid, a.A);
            case HanActKind.Sell: return Human(pid) && M.Sell(pid, a.A);
            case HanActKind.Move: return Human(pid) && Enum.IsDefined(typeof(Zone), a.B) && a.C is >= -1 and <= 2 && M.Move(pid, a.A, (Zone)a.B, a.C >= 0 ? (Row)a.C : null);
            case HanActKind.LockShop: return Human(pid) && M.ToggleShopLock(pid);
            case HanActKind.RoundSeen:
                // elenmiş oyuncu da kendi savaşının sonunu izler; yalnız savaşı bitmiş olmalı
                if (pid < 0 || pid >= M.Players.Count || Bots.Contains(pid) || M.Phase != Phase.Battle || M.BattleOf(pid) is not { Done: true }) return false;
                return _seen.Add(pid);
            case HanActKind.Reroll: return Human(pid) && M.Reroll(pid);
            case HanActKind.Xp: return Human(pid) && M.BuyXp(pid);
            case HanActKind.Reward: return Human(pid) && M.PickReward(pid, a.A);
            case HanActKind.Potion: return Human(pid) && M.ChoosePotion(pid, a.A);
            case HanActKind.Swap: return Human(pid) && M.SwapOrder(pid, a.A, a.B);
            case HanActKind.Ready:
                if (!Human(pid) || !M.SetReady(pid)) return false;
                if (M.Phase == Phase.Battle) AfterLock();
                return true;
            case HanActKind.Lock:
                if (M.Phase != Phase.Prep) return false;
                M.ForceLock();
                AfterLock();
                return true;
            case HanActKind.Activate:
            {
                if (Battle(a) is not { } b || b.Started || Owner(b, a.B) != pid || pid < 0 || _activated.Contains((a.A, a.B))) return false;
                var act = a.ToActivation();
                if (act == null || !b.SetActivation(a.B, act)) b.SetActivation(a.B, b.DefaultActivation(a.B));
                _activated.Add((a.A, a.B));
                TryStart(a.A, b);
                return true;
            }
            case HanActKind.Window:
            {
                if (Battle(a) is not { } b || !b.Started || b.Done || Owner(b, a.B) != pid || pid < 0 || b.Submitted(a.B)) return false;
                if (a.ToChoice() is not { } c || !b.Submit(a.B, c)) return false;
                BotWindows(a.A, b);
                return true;
            }
            case HanActKind.EndRound:
                if (!RoundCanEnd || !M.EndRound()) return false;
                _activated.Clear();
                _seen.Clear();
                AfterRoundBegins();
                return true;
            case HanActKind.Abandon:
                if (pid < 0 || pid >= M.Players.Count || Bots.Contains(pid)) return false;
                Bots.Add(pid);
                TakeOver(pid);
                return true;
        }
        return false;
    }

    private Battle? Battle(HanAction a) => M.Phase == Phase.Battle && a.A >= 0 && a.A < M.Battles.Count && a.B is 0 or 1 ? M.Battles[a.A] : null;

    /// <summary>Round başladı (okullar seçildi ya da önceki round kapandı): botlar hazırlanır.</summary>
    private void AfterRoundBegins()
    {
        if (M.Phase != Phase.Prep) return;
        foreach (var p in M.Alive.Where(p => Bots.Contains(p.Id)).ToList())
        {
            ExpertBot.Prep(M, p.Id, Rng(2, M.Round, p.Id));
            if (M.Phase != Phase.Prep) { AfterLock(); return; }   // son hazır olan bot kilitledi
        }
    }

    /// <summary>Saha kilitlendi: bot tarafları hedeflerini seçer; iki tarafı da bot olan savaşlar hemen oynanır.</summary>
    private void AfterLock()
    {
        for (var i = 0; i < M.Battles.Count; i++)
        {
            var b = M.Battles[i];
            if (IsBotSide(b, 0) && IsBotSide(b, 1))
            {
                b.SetActivation(0, b.DefaultActivation(0));
                b.SetActivation(1, b.DefaultActivation(1));
                _activated.Add((i, 0));
                _activated.Add((i, 1));
                { var r3 = Rng(3, M.Round, i); b.Start(); while (!b.Done) { for (var s3 = 0; s3 < 2; s3++) b.Submit(s3, ExpertBot.Window(b, s3, r3)); b.Resolve(); } }
                continue;
            }
            for (var s = 0; s < 2; s++)
                if (IsBotSide(b, s)) { b.SetActivation(s, b.DefaultActivation(s)); _activated.Add((i, s)); }
            TryStart(i, b);
        }
    }

    private void TryStart(int index, Battle b)
    {
        if (b.Started || !_activated.Contains((index, 0)) || !_activated.Contains((index, 1))) return;
        b.Start();
        BotWindows(index, b);
    }

    /// <summary>Bot tarafları pencere seçimini yapar; iki taraf da seçtiyse pencere çözülür (gerekirse sonrakine geçer).</summary>
    private void BotWindows(int index, Battle b)
    {
        while (b.Started && !b.Done)
        {
            for (var s = 0; s < 2; s++)
                if (IsBotSide(b, s) && !b.Submitted(s)) b.Submit(s, ExpertBot.Window(b, s, Rng(4, M.Round, index, b.Window, s)));
            if (!b.Submitted(0) || !b.Submitted(1)) return;
            b.Resolve();
        }
    }

    /// <summary>Ayrılan oyuncunun koltuğunu yapay zekâ devralır: o an beklenen ne varsa yapar.</summary>
    private void TakeOver(int pid)
    {
        if (M.Phase == Phase.Prep && M.Players[pid].Alive && !M.Players[pid].Ready)
        {
            ExpertBot.Prep(M, pid, Rng(5, M.Round, pid));
            if (M.Phase == Phase.Battle) AfterLock();
            return;
        }
        if (M.Phase != Phase.Battle) return;
        for (var i = 0; i < M.Battles.Count; i++)
        {
            var b = M.Battles[i];
            for (var s = 0; s < 2; s++)
            {
                if (b.Sides[s].PlayerId != pid || !IsBotSide(b, s)) continue;
                if (!b.Started && !_activated.Contains((i, s))) { b.SetActivation(s, b.DefaultActivation(s)); _activated.Add((i, s)); TryStart(i, b); }
                else BotWindows(i, b);
            }
        }
    }

    /// <summary>
    /// Yalnız yerel oturum (sınama, ekran görüntüsü): insan koltuğunun hazırlığını botla yapar. Kilit adımlı akışın parçası değildir,
    /// online'da kullanılmaz.
    /// </summary>
    public void LocalAutoPrep(int pid, Random rng)
    {
        if (M.Phase != Phase.Prep) return;
        HanBot.Prep(M, pid, rng);
        if (M.Phase == Phase.Battle) AfterLock();
    }

    /// <summary>Kopyaların karşılaştırılması için durum özeti (testler ve hata ayıklama).</summary>
    public string Fingerprint() => string.Join("|", M.Players.Select(p =>
        $"{p.Hp},{p.Gold},{p.Level},{p.Xp},{p.Place},{p.Streak},{string.Join(",", p.Cards.Select(c => c.Def.Id + c.Star + c.Zone + c.Aim))},{p.ShopLocked}"))
        + $"|{M.Round}|{M.Phase}|" + string.Join(",", M.Battles.Select(b => $"{b.Window}:{b.Sides[0].Points}-{b.Sides[1].Points}:{b.Sides[0].Focus},{b.Sides[1].Focus}:{string.Join("/", b.Sides[0].RowValues)}:{string.Join("/", b.Sides[1].RowValues)}"))
        + "|shop:" + string.Join(";", M.Players.Select(p => string.Join(",", p.Shop.Select(d => d?.Id)) + "#" + string.Join(",", p.Potions) + "#" + p.Ready))
        + "|pool:" + string.Join(",", M.Pool.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Value));
}
