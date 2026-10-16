using GwentAI;
using GwentCore;
using GwentNet;

namespace GwentClient;

/// <summary>MyDeck boşsa fraksiyonun tüm kartları kullanılır. Rakip her zaman "önerilen" deste ile oynar (dengeli olsun).</summary>
public sealed record MatchConfig(string MyFaction, string MyLeaderId, string OppFaction, string OppLeaderId, bool NextGen, int Seed, Deck? MyDeck = null, Difficulty Difficulty = Difficulty.Normal, Deck? OppDeck = null, IReadOnlyList<string>? Curses = null, DateOnly? Daily = null);

/// <summary>Online maçta sunucuyla konuşan taraf: eylemi gönderir. Sunucudan gelenler <see cref="GameSession.EnqueueRemote"/> ile oturuma verilir.</summary>
public interface IMatchLink
{
    void Submit(ReplayAction action);
    void Resign();
    void RequestRematch();
    /// <summary>Rakibin bağlantısı yeterince uzun süredir kopuksa galibiyeti al.</summary>
    void ClaimWin();
}

/// <summary>
/// Bir maç: oyun, rakip ve Türkçe olay günlüğü. Rakip ya yapay zekâdır (yerel, koltuk 0) ya da uzaktaki oyuncudur (online, koltuk 0 ya da 1).
/// Online'da eylem sunucuya gider ve **yankısı** gelince uygulanır (tek yol): kendi eylemin de rakibinkiyle aynı sırada, sunucunun
/// numaralandırdığı akıştan işlenir. Eşzamanlı çalışır; gecikme ve animasyon arayüz katmanındadır.
/// </summary>
public sealed class GameSession
{
    public const int Me = 0;
    public const int Opp = 1;

    public Game Game { get; }
    public MatchConfig Config { get; }

    /// <summary>Bu oturumdaki oyuncunun koltuğu (yerelde hep 0; online'da host 0, konuk 1).</summary>
    public int MySeat { get; private set; }

    /// <summary>İzleyici oturumu: hamle yapamaz; bakış koltuğu değiştirilebilir (<see cref="FlipView"/>). ShowHands: eller görünür (turnuva kuralı).</summary>
    public bool IsSpectator { get; private set; }
    public bool ShowHands { get; private set; } = true;

    /// <summary>İzleyici bakışını öbür oyuncuya çevirir (o oyuncunun eli altta görünür).</summary>
    public void FlipView() { if (IsSpectator) MySeat = 1 - MySeat; }
    public int OppSeat => 1 - MySeat;

    /// <summary>Ekranda gösterilen ad ve portreler: online'da kurulumdan, yerelde profilden (rakip yapay zekâ lider yüzüyle).</summary>
    public string MyName => OnlineSetup != null ? (MySeat == 0 ? OnlineSetup.Nick0 : OnlineSetup.Nick1) : (string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick);
    public string OppName => OnlineSetup != null ? (MySeat == 0 ? OnlineSetup.Nick1 : OnlineSetup.Nick0) : OppNameOverride ?? "Rakip";
    public string MyAvatar => OnlineSetup != null ? (MySeat == 0 ? OnlineSetup.Seat0 : OnlineSetup.Seat1).Avatar ?? Portraits.Default : Settings.Portrait;
    public string OppAvatar => (OnlineSetup != null ? (MySeat == 0 ? OnlineSetup.Seat1 : OnlineSetup.Seat0).Avatar : null) ?? OppAvatarOverride ?? Config.OppLeaderId;

    /// <summary>Kariyer maçı: rakibin adı ve portresi (ör. "Kanlı Baron", "face_baron").</summary>
    public string? OppNameOverride { get; init; }
    public string? OppAvatarOverride { get; init; }

    /// <summary>Kariyer maçı bitince bir kez çağrılır (true kazandın, false kaybettin, null berabere); oyun sonu ekranında gösterilecek özeti döner.</summary>
    public Func<bool?, string>? Settle { get; init; }

    /// <summary>Oyun sonu ekranındaki çıkış düğmesinin yazısı (ör. "Haritaya dön").</summary>
    public string? ExitLabel { get; init; }

    /// <summary>İstatistikteki kip adı ("Collect 'Em All", "Draft"); rakip adının önüne yazılır.</summary>
    public string? ModeLabel { get; init; }
    public bool IsRemote => _link != null;

    /// <summary>Turnuva maçı: oyun sonunda "Bracket'e dön" gösterilir (rövanş yalnız berabere kalınca).</summary>
    public bool TournamentMode { get; set; }
    public IMatchLink? Link => _link;
    public MatchSetup? OnlineSetup { get; }
    private readonly IMatchLink? _link;
    private readonly Queue<ActionBroadcast> _remote = new();
    private readonly object _remoteLock = new();
    private bool _awaitingEcho;
    public ReplayData Replay { get; }

    /// <summary>Doluysa her eylemden sonra maç tekrarı bu dosyaya yazılır (yalnız gerçek oyunda; sınamalar kullanıcının dosyasını ezmesin).</summary>
    public static string? ReplayPath { get; set; }
    public List<string> Log { get; } = new();
    private readonly IAgent _ai;
    private int _eventsRead;
    private int _taken;
    private readonly HashSet<int> _announced = new();

    /// <summary>Online maç: kurulum sunucudan gelir; iki taraf da aynı koltuk sırasıyla aynı <see cref="Game"/>'i kurar.</summary>
    /// <summary>İzleme: kurulum + sonradan gelen eylemlerle oyunun bir kopyasını kurar; hamle göndermez.</summary>
    public static GameSession ForSpectator(SpectateSetup s) =>
        new(s.Setup with { YourSeat = 0 }, new NullLink()) { IsSpectator = true, ShowHands = s.ShowHands, TournamentMode = true };

    private sealed class NullLink : IMatchLink
    {
        public void Submit(ReplayAction action) { }
        public void Resign() { }
        public void RequestRematch() { }
        public void ClaimWin() { }
    }

    public GameSession(MatchSetup setup, IMatchLink link)
    {
        _link = link;
        _ai = new RandomAgent(); // kullanılmaz
        OnlineSetup = setup;
        MySeat = setup.YourSeat;
        Config = new MatchConfig(MySeat == 0 ? setup.Seat0.Faction : setup.Seat1.Faction, MySeat == 0 ? setup.Seat0.LeaderId : setup.Seat1.LeaderId,
            MySeat == 0 ? setup.Seat1.Faction : setup.Seat0.Faction, MySeat == 0 ? setup.Seat1.LeaderId : setup.Seat0.LeaderId, setup.NextGen, setup.Seed,
            Curses: setup.Curses);
        Replay = setup.ToReplay();
        Game = Replay.CreateGame();
    }

    public GameSession(MatchConfig cfg, IAgent? ai = null)
    {
        Config = cfg;
        _ai = ai ?? AgentFactory.Create(cfg.Difficulty, cfg.Seed);
        // Kurulum bir tekrar kaydı olarak tutulur: hata bildirimlerinde maç birebir yeniden oynatılabilir
        Replay = new ReplayData
        {
            MyFaction = cfg.MyFaction, MyLeaderId = cfg.MyDeck?.LeaderId ?? cfg.MyLeaderId, OppFaction = cfg.OppFaction, OppLeaderId = cfg.OppLeaderId,
            NextGen = cfg.NextGen, Seed = cfg.Seed, MyDeckCards = cfg.MyDeck?.CardIds.ToList(), Difficulty = (int)cfg.Difficulty,
            OppDeckCards = cfg.OppDeck?.CardIds.ToList(), OppDeckUnchecked = cfg.OppDeck != null, Curses = cfg.Curses?.ToList()
        };
        Game = Replay.CreateGame();
    }

    /// <summary>Online'da oyun kural dışı biterse (teslim, rakip terk etti) sunucunun bildirdiği sonuç.</summary>
    public MatchResult? ExternalResult { get; set; }

    public bool IsOver => Game.Phase == Phase.GameOver || ExternalResult != null;

    /// <summary>Kazanan koltuk (null = berabere ya da bitmedi).</summary>
    public int? WinnerSeat => ExternalResult != null ? ExternalResult.WinnerSeat : Game.Outcome?.Winner;

    /// <summary>Sıradaki eylemi yapacak kişi yapay zekâ mı? (yalnız yerel)</summary>
    public bool AiToAct
    {
        get
        {
            if (IsRemote) return false;
            var who = Game.PlayersToAct();
            return who.Count > 0 && who.All(p => p == OppSeat);
        }
    }

    /// <summary>Kart değiştirmede ikisi de bekleniyorsa yapay zekâ kendi payını hemen yapar. (yalnız yerel)</summary>
    public void AiAutoMulligan()
    {
        if (IsRemote) return;
        if (Game.Phase == Phase.Mulligan && Game.PlayersToAct().Contains(OppSeat))
            Apply(_ai.Decide(Game, OppSeat));
    }

    /// <summary>Rakip tarafı işlenmeyi bekliyor mu? Yerelde yapay zekânın sırası; online'da kuyrukta sunucudan gelen eylem var.</summary>
    public bool OpponentBusy => IsRemote ? RemoteQueued > 0 : AiToAct;

    /// <summary>Online'da kendi eylemimin sunucudan yankısı bekleniyor (bu sürede yeni eylem gönderilmez).</summary>
    public bool AwaitingEcho => _awaitingEcho;

    public ActionResult Apply(GameAction a)
    {
        if (IsSpectator) return ActionResult.Fail("İzleyici hamle yapamaz.");
        if (IsRemote)
        {
            if (_awaitingEcho) return ActionResult.Fail("Önceki hamlenin onayı bekleniyor.");
            var pre = Precheck(a);
            if (!pre.Ok) return pre;
            _awaitingEcho = true;
            _link!.Submit(ReplayData.Encode(a));
            return ActionResult.Success;
        }
        var r = Game.Apply(a);
        CollectLog();
        if (r.Ok) RecordReplay(a);
        return r;
    }

    /// <summary>Online'da eylemi göndermeden önce yerelde, oyunun kopyası üzerinde dener (anında hata mesajı için).</summary>
    private ActionResult Precheck(GameAction a)
    {
        if (Game.Pending != null) return ActionResult.Success; // bekleyen seçimde kopya alınamaz; sunucu doğrular
        try { return Game.Clone().Apply(a); }
        catch (InvalidOperationException) { return ActionResult.Success; }
    }

    // ---------- Online: sunucudan gelen eylemler ----------

    /// <summary>Sunucunun sıraladığı eylem kuyruğa alınır (herhangi bir iş parçacığından çağrılabilir).</summary>
    public void EnqueueRemote(ActionBroadcast b)
    {
        lock (_remoteLock) _remote.Enqueue(b);
        RemoteArrived?.Invoke();
    }

    public event Action? RemoteArrived;

    public int RemoteQueued { get { lock (_remoteLock) return _remote.Count; } }

    /// <summary>Kuyruktaki sıradaki eylem kime ait? (arayüz rakip hamlelerini yavaşlatıp kendi yankımı beklemeden işler)</summary>
    public int? PeekRemotePlayer() { lock (_remoteLock) return _remote.Count > 0 ? _remote.Peek().Player : null; }

    /// <summary>Sıradaki sunucu eylemini oyuna uygular. Uygulanamazsa kopyalar ayrışmış demektir: hata fırlatır.</summary>
    public (GameAction Action, int Player) ApplyNextRemote()
    {
        ActionBroadcast b;
        lock (_remoteLock) b = _remote.Dequeue();
        var a = ReplayData.Decode(b.Action);
        var r = Game.Apply(a);
        if (!r.Ok) throw new InvalidOperationException($"Sunucunun eylemi uygulanamadı (kopyalar ayrıştı): seq {b.Seq} {a} → {r.Error}");
        CollectLog();
        RecordReplay(a);
        if (b.Player == MySeat) _awaitingEcho = false;
        return (a, b.Player);
    }

    /// <summary>
    /// Kuyruktaki tüm sunucu eylemlerini animasyonsuz, hemen uygular (yarım kalan maç yeniden açılırken geçmişi göstermeden durumu kurar).
    /// </summary>
    public void FastForward()
    {
        while (RemoteQueued > 0) ApplyNextRemote();
        _taken = Game.Events.Count;
    }

    /// <summary>Sunucu eylemi reddetti: tekrar denemeye izin ver.</summary>
    public void NotifyRejected(string reason)
    {
        _awaitingEcho = false;
        Log.Add("⚠ " + reason);
    }

    private void RecordReplay(GameAction a)
    {
        Replay.Actions.Add(ReplayData.Encode(a));
        if (ReplayPath == null) return;
        try
        {
            Replay.Log = Log.ToList();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReplayPath)!);
            System.IO.File.WriteAllText(ReplayPath, Replay.ToJson());
        }
        catch { /* kayıt başarısızsa oyun etkilenmez */ }
    }

    /// <summary>Yapay zekânın bir eylemini uygular.</summary>
    public GameAction AiStep()
    {
        var a = _ai.Decide(Game, OppSeat);
        var r = Apply(a);
        if (!r.Ok) throw new InvalidOperationException($"Yapay zekâ geçersiz eylem verdi: {a} → {r.Error}");
        return a;
    }

    /// <summary>Test ve ekran görüntüsü için: verilen ajanı oyuncu yerine oynatır.</summary>
    public void AutoPlayFor(int player, IAgent agent, int maxSteps)
    {
        for (var i = 0; i < maxSteps && !IsOver; i++)
        {
            var who = Game.PlayersToAct();
            if (who.Count == 0 || !who.Contains(player)) break;
            Apply(agent.Decide(Game, player));
        }
    }

    /// <summary>Son çağrıdan beri olan olaylar (ses ve animasyon için). Günlükten bağımsız ilerler.</summary>
    public IReadOnlyList<GameEvent> TakeEvents()
    {
        var list = Game.Events.Skip(_taken).ToList();
        _taken = Game.Events.Count;
        return list;
    }

    // ---------- Günlük ----------

    public string NameOf(int uid)
    {
        foreach (var p in Game.Players)
        {
            foreach (var c in p.Hand.Concat(p.Deck).Concat(p.Grave).Concat(p.Rows.SelectMany(r => r.Cards))
                         .Concat(p.Rows.Where(r => r.Special != null).Select(r => r.Special!)))
                if (c.Uid == uid) return c.Def.Name;
        }
        foreach (var c in Game.WeatherCards) if (c.Uid == uid) return c.Def.Name;
        return $"#{uid}";
    }

    private string CardName(string defId) => Game.Catalog.Get(defId).Name;

    private void CollectLog()
    {
        while (_eventsRead < Game.Events.Count)
        {
            var e = Game.Events[_eventsRead++];
            var line = Translate(e);
            if (line != null) Log.Add(line);
        }
    }

    private string? Translate(GameEvent e)
    {
        string W(int p) => Texts.Who(p, MySeat);
        // Raund puanları oyuncu numarasına göredir (0, 1); günlükte "Sen / Rakip" sırasına çevrilir
        string Mine(string[] parts) => parts[MySeat];
        string Theirs(string[] parts) => parts[1 - MySeat];
        switch (e.Kind)
        {
            case "coin_toss": return $"Yazı-tura: {W(e.Player)} başlıyor.";
            case "first_chosen": return $"Scoia'tael: {W(e.Player)} {(int.Parse(e.Detail) == e.Player ? "kendisinin başlamasını" : "rakibin başlamasını")} seçti.";
            case "round_start": return $"— Raund {e.Detail} —";
            case "play":
                _announced.Add(e.CardUid);
                return $"{W(e.Player)}: {CardName(e.Detail)} oynadı.";
            case "place":
                return _announced.Remove(e.CardUid) ? null : $"{W(e.Player)}: {NameOf(e.CardUid)} sahaya indi.";
            case "draw": return $"{W(e.Player)} {e.Detail} kart çekti.";
            case "pass": return e.Detail == "auto" ? $"{W(e.Player)} elinde kart kalmadığı için pas geçti." : $"{W(e.Player)} pas geçti.";
            case "leader": return $"{W(e.Player)} lider yeteneğini kullandı: {CardName(e.Detail)}.";
            case "kill": return $"{CardName(e.Detail)} yok oldu.";
            case "spawn": return $"{CardName(e.Detail)} sahaya geldi.";
            case "transform": return $"{CardName(e.Detail)} oldu (dönüşüm).";
            case "decoy": return $"{W(e.Player)} Yem ile {CardName(e.Detail)} kartını geri aldı.";
            case "muster": return $"Toplanma: {e.Detail} grubu sahaya indi.";
            case "weather": return e.Detail == Ids.ClearWeather ? "Hava açıldı." : $"{CardName(e.Detail)} etkili.";
            case "peek": return e.Visible == MySeat ? $"Rakibin elinden: {string.Join(", ", e.Detail.Split(',').Select(CardName))}." : null;
            case "monsters_keep": return $"Canavarlar: {NameOf(e.CardUid)} bir sonraki raunda kalıyor.";
            case "skellige_revive": return $"Skellige: {CardName(e.Detail)} mezarlıktan döndü.";
            case "curse_weather": return $"Bitmeyen Kış: raund {CardName(e.Detail)} ile açıldı.";
            case "curse_discard": return $"Kan Bedeli: {W(e.Player)} elinden {CardName(e.Detail)} kartını kaybetti.";
            case "round_end":
            {
                var parts = e.Detail.Split(':');
                var res = e.Player < 0 ? "Berabere." : $"Kazanan: {W(e.Player)}.";
                return $"Raund bitti — Sen {Mine(parts)}, Rakip {Theirs(parts)}. {res}";
            }
            case "game_end": return e.Player < 0 ? "Oyun berabere bitti." : $"Oyun bitti — {(e.Player == MySeat ? "kazandın!" : "rakip kazandı.")}";
            default: return null;
        }
    }
}
