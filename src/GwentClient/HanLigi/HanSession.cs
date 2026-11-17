using GwentNet;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi oturumu. Kurallar ve botlar <see cref="HanGame"/> içindedir (kilit adımlı motor); bu sınıf yalnız oyuncunun
/// eylemlerini ona iletir.
/// <para><b>Yerel:</b> insan koltuk 0, ötekiler bot; eylemler hemen uygulanır, sunucunun işlerini (round kapanışı) oturum yapar.</para>
/// <para><b>Online:</b> eylemler sunucuya gider; sunucunun sıraladığı eylemler (<see cref="Receive"/>) gelince uygulanır ve
/// <see cref="Changed"/> tetiklenir. Arayüz kendi savaş animasyonunu bitirmeden sonraki round başlamasın diye akış "round sonu"
/// eyleminde bekletilir; arayüz <see cref="FinishRound"/> deyince devam eder.</para>
/// </summary>
public sealed class HanSession
{
    private static readonly string[] BotNames = { "Zoltan", "Lambert", "Eskel", "Keira", "Roche", "Iorveth", "Letho", "Cerys", "Dijkstra" };

    public HanGame G { get; }
    public Match M => G.M;
    /// <summary>Benim koltuğum.</summary>
    public int Me { get; }
    public bool Online => _link != null;
    private readonly Action<HanAction>? _link;
    private readonly List<string?> _avatars = new();
    /// <summary>Sunucudan gelen ama henüz uygulanmamış eylemler (round sonunda bekletme için).</summary>
    private readonly Queue<HanAction> _inbox = new();
    private bool _releaseEndRound;

    public Battle? MyBattle => G.BattleIndexOf(Me) is var i and >= 0 ? M.Battles[i] : _lastBattle;
    private Battle? _lastBattle;
    public int MyBattleIndex => G.BattleIndexOf(Me);
    public int MySide => MyBattle is { } b ? M.SideOf(b, Me) : 0;
    /// <summary>Süre sınırı (30 / 12 / 10 sn); kapalıysa alıştırma (yalnız yerel). Online'da hep açıktır.</summary>
    public bool Timed { get => _timed || Online; set => _timed = value; }
    private bool _timed = true;
    public PlayerState Player => M.Players[Me];
    public bool Over => M.Phase == Phase.Finished;
    /// <summary>Az önce biten round (sonuç ekranı için).</summary>
    public int LastRound { get; private set; }
    /// <summary>Bir eylem uygulandı (online: sunucudan geldi). Arayüz yeniden çizer ya da bekleyen adımına geçer.</summary>
    public event Action? Changed;

    /// <summary>Yerel oturum: koltuk 0 insan, ötekiler bot.</summary>
    public HanSession(int seed, string name, int players = 8)
    {
        var rng = new Random(seed ^ 0x2545f491);
        var list = new List<(string, bool)> { (name, false) };
        list.AddRange(BotNames.OrderBy(_ => rng.Next()).Take(players - 1).Select(n => (n, true)));
        G = new HanGame(seed, list);
        Me = 0;
    }

    /// <summary>Online oturum: sunucunun kurulumu; eylemler <paramref name="send"/> ile sunucuya gider.</summary>
    public HanSession(HanSetup setup, Action<HanAction> send)
    {
        G = new HanGame(setup.Seed, setup.Names.Select((n, i) => (n, setup.Bots[i])).ToList());
        Me = setup.YourSeat;
        _link = send;
        _avatars.AddRange(setup.Avatars);
    }

    /// <summary>Portre: insanın seçtiği (online) ya da botun adına göre oyunun yüzlerinden biri.</summary>
    public string? FaceOf(int pid)
    {
        if (pid < 0 || pid >= M.Players.Count) return null;
        if (pid < _avatars.Count && _avatars[pid] is { Length: > 0 } a) return a;
        var n = M.Players[pid].Name.ToLowerInvariant();
        return n switch { "iorveth" => "iorveth", _ => "face_" + n };
    }

    public bool IsHuman(int pid) => !G.Bots.Contains(pid);

    // ---------- eylemler ----------

    /// <summary>Eylemi uygular (yerel) ya da sunucuya yollar (online). Yerelde sonucu döner; online'da gönderildiyse true.</summary>
    public bool Act(HanAction a)
    {
        if (_link != null) { _link(a); return true; }
        var ok = G.Apply(a);
        if (ok) Changed?.Invoke();
        return ok;
    }

    public bool Act(HanActKind kind, int a = 0, int b = 0) => Act(new HanAction(kind, Me, a, b));

    public bool Act(HanActKind kind, int a, int b, int c) => Act(new HanAction(kind, Me, a, b, c));

    public void ChooseSchool(School s) => Act(HanActKind.School, (int)s);

    /// <summary>Hazırım: yerelde botlar zaten hazır olduğu için saha hemen kilitlenir; online'da herkes hazır olunca (ya da süre dolunca).</summary>
    public void Ready()
    {
        if (M.Phase == Phase.Prep && !Player.Ready) Act(HanActKind.Ready);
    }

    /// <summary>Savaşta hedeflerim (Boynuz/Kalkan/Moral). Savaş, iki taraf da seçince başlar.</summary>
    public void StartBattle(Activation mine)
    {
        if (MyBattle is not { Started: false } b || MyBattleIndex < 0 || G.Activated(MyBattleIndex, MySide)) return;
        Act(HanAction.Activation(Me, MyBattleIndex, MySide, mine));
    }

    /// <summary>Pencere seçimim. Yerelde pencere hemen çözülür ve sonucu döner; online'da sonuç sunucudan gelince <see cref="Changed"/> ile.</summary>
    public WindowOutcome? Submit(WindowChoice c)
    {
        if (MyBattle is not { Started: true, Done: false } b || b.Submitted(MySide)) return null;
        var before = b.Outcomes.Count;
        // geçersiz seçim (ör. kurala uymayan simya/pusu birleşimi) sunucuda reddedilir ve online'da ekran yanıt beklerken takılırdı:
        // kopyalar aynı olduğu için burada denetlenir, geçersizse "müdahale yok" gider
        if (!b.Valid(MySide, c)) c = WindowChoice.None;
        if (!Act(HanAction.Choice(Me, MyBattleIndex, MySide, c)) && !Online) Act(HanAction.Choice(Me, MyBattleIndex, MySide, WindowChoice.None));
        return b.Outcomes.Count > before ? b.Outcomes[before] : null;
    }

    /// <summary>Round'u kapatır (savaş ekranından "Devam"). Yerel: round sonu uygulanır; elendiysem maç botlarla biter. Online: bekletilen round sonu bırakılır.</summary>
    public void FinishRound()
    {
        LastRound = M.Round;
        _lastBattle = null;
        if (Online)
        {
            // sunucuya "savaşımın sonunu izledim": herkes izleyince round hep birlikte kapanır
            if (M.Phase == Phase.Battle && M.BattleOf(Me) is { Done: true }) Act(HanActKind.RoundSeen);
            _releaseEndRound = true;
            Drain();
            return;
        }
        if (G.RoundCanEnd) G.Apply(new HanAction(HanActKind.EndRound, -1));
        if (!Player.Alive && M.Phase != Phase.Finished)
        {
            if (!G.Bots.Contains(Me)) G.Apply(new HanAction(HanActKind.Abandon, Me));
            for (var guard = 0; guard < 500 && M.Phase != Phase.Finished; guard++)
                if (!G.Apply(new HanAction(HanActKind.EndRound, -1))) break;
        }
        Changed?.Invoke();
    }

    /// <summary>Round sonucu uygulandı mı (online'da arayüz bunu bekler)?</summary>
    public bool RoundClosed => M.Round > LastRound || M.Phase == Phase.Finished;

    /// <summary>Yerel sınama ve ekran görüntüleri: insan koltuğunu da bot oynatarak bir round'u baştan sona geçer.</summary>
    public void AutoRound(Random rng)
    {
        if (Online || M.Phase != Phase.Prep || !Player.Alive) return;
        G.LocalAutoPrep(Me, rng);   // hazır da der: saha kilitlenir
        if (M.Phase == Phase.Prep) Ready();
        if (M.Phase == Phase.Battle && MyBattle is { } b)
        {
            StartBattle(b.DefaultActivation(MySide));
            for (var guard = 0; !b.Done; guard++)
            {
                if (guard > 20) throw new InvalidOperationException($"AutoRound ilerlemiyor: round {M.Round}, savaş başladı {b.Started}, pencere {b.Window}, gönderildi {b.Submitted(MySide)}/{b.Submitted(1 - MySide)}, aktif {G.Activated(MyBattleIndex, MySide)}/{G.Activated(MyBattleIndex, 1 - MySide)}, taraf {MySide}, karşı {b.Sides[1 - MySide].PlayerId} bot {G.IsBotSide(b, 1 - MySide)}");
                Submit(HanBot.Window(b, MySide, rng));
            }
        }
        FinishRound();
    }

    // ---------- online: sunucudan gelenler ----------

    /// <summary>Sunucunun sıraladığı eylem (arayüz iş parçacığında çağrılır).</summary>
    public void Receive(HanAction a)
    {
        _inbox.Enqueue(a);
        Drain();
    }

    private void Drain()
    {
        var any = false;
        while (_inbox.Count > 0)
        {
            var a = _inbox.Peek();
            // Round sonu: savaşımın sonu ekranda gösterilene kadar bekler (savaşım yoksa beklemez). Can savaş biter bitmez düşer;
            // elenmiş olsam da kendi savaşımın sonunu görürüm.
            if (a.Kind == HanActKind.EndRound && !_releaseEndRound && G.BattleIndexOf(Me) >= 0) break;
            _inbox.Dequeue();
            if (a.Kind == HanActKind.EndRound)
            {
                _releaseEndRound = false;
                if (G.BattleIndexOf(Me) >= 0) _lastBattle = MyBattle;
                LastRound = M.Round;
            }
            if (!G.Apply(a)) throw new InvalidOperationException("Sunucunun eylemi bu kopyada uygulanamadı (kopyalar ayrıştı): " + a);
            any = true;
        }
        if (any) Changed?.Invoke();
    }

    /// <summary>Bekletilen eylem var mı (sınama)?</summary>
    public int Pending => _inbox.Count;
}
