using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

public sealed class ManualClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan t) => UtcNow += t;
}

/// <summary>
/// Bellek içi sahte istemci: gerçek istemci gibi kendi <see cref="Game"/> kopyasını tutar, sunucudan gelen eylemleri sırayla uygular
/// ve sırası gelince bir ajanla eylem gönderir. Gelen iletiler kuyruğa alınır (sunucu kilidi altında geri çağırma yapılmaz).
/// </summary>
public sealed class FakeClient : IGwentClient
{
    private readonly RoomService _svc;
    private readonly IAgent _agent;
    private readonly Queue<Action> _inbox = new();
    private bool _awaitingOwn;

    public FakeClient(RoomService svc, string nick, Difficulty difficulty = Difficulty.Hard, int seed = 1)
    {
        _svc = svc;
        Nick = nick;
        _agent = AgentFactory.Create(difficulty, seed);
        Session = svc.Connect(this);
    }

    public string Nick { get; }
    public ServerSession Session { get; private set; }
    public RoomState? State { get; private set; }
    public MatchSetup? Setup { get; private set; }
    public Game? Local { get; set; }
    public int NextSeq { get; set; }
    public MatchResult? Result { get; private set; }
    public List<string> Notices { get; } = new();
    public List<string> Rejections { get; } = new();
    public int Seat => State?.YourSeat ?? -1;
    public string Token => State?.YourToken ?? "";

    // ---------- IGwentClient: yalnız kuyruğa at ----------
    public void OnRoomState(RoomState s) => _inbox.Enqueue(() => State = s);
    public void OnMatchStarted(MatchSetup s) => _inbox.Enqueue(() =>
    {
        Setup = s;
        Local = s.ToReplay().CreateGame();
        NextSeq = 0;
        Result = null;
        _awaitingOwn = false;
    });
    public void OnActionApplied(ActionBroadcast a) => _inbox.Enqueue(() =>
    {
        Assert.Equal(NextSeq, a.Seq); // sıra numarasında boşluk/tekrar olmamalı
        Assert.True(Local!.Apply(ReplayData.Decode(a.Action)).Ok, $"kopya eylemi reddetti: seq {a.Seq}");
        NextSeq++;
        if (a.Player == Seat) _awaitingOwn = false;
    });
    public void OnActionRejected(string reason, int expectedSeq) => _inbox.Enqueue(() => { Rejections.Add(reason); _awaitingOwn = false; });
    public void OnMatchEnded(MatchResult r) => _inbox.Enqueue(() => Result = r);
    public void OnNotice(string code, string text) => _inbox.Enqueue(() => Notices.Add(code));

    // ---------- Sürücü ----------

    public void Pump()
    {
        while (_inbox.Count > 0) _inbox.Dequeue()();
    }

    /// <summary>Sıra bendeyse ajanla bir eylem gönderir. Gönderdiyse true.</summary>
    public bool TryPlay()
    {
        if (Local == null || Result != null || Local.Phase == Phase.GameOver || _awaitingOwn) return false;
        if (!Local.PlayersToAct().Contains(Seat)) return false;
        var a = _agent.Decide(Local, Seat);
        _awaitingOwn = true;
        var r = _svc.SubmitAction(Session, ReplayData.Encode(a));
        Assert.True(r.Ok, r.Error);
        return true;
    }

    public Reply Create(bool nextGen = false) => _svc.CreateRoom(Session, Nick, ProtocolInfo.Version, nextGen);
    public Reply Join(string code) => _svc.JoinRoom(Session, code, Nick, ProtocolInfo.Version);

    public Reply Choose(string faction, string? leader = null, List<string>? cards = null)
    {
        leader ??= CardCatalog.Default.Leaders(faction).FirstOrDefault(l => l.Edition != "nextgen")?.Id ?? "yok";
        return _svc.SetLoadout(Session, new LoadoutDto(faction, leader, cards));
    }

    public Reply Ready(bool ready = true) => _svc.SetReady(Session, ready);

    /// <summary>Bağlantıyı yeni bir oturumla yeniden kurar (eski durum — oyun kopyası, sıra no — korunur).</summary>
    public Reply Reconnect(string code)
    {
        Session = _svc.Connect(this);
        return _svc.Rejoin(Session, code, Token, NextSeq - 1);
    }

    public void Disconnect() => _svc.Disconnect(Session);

    public static void Pump(params FakeClient[] clients)
    {
        foreach (var c in clients) c.Pump();
    }

    /// <summary>Maç bitene kadar iki istemciyi sırayla çalıştırır.</summary>
    public static void RunToEnd(int maxRounds, params FakeClient[] clients)
    {
        for (var i = 0; i < maxRounds; i++)
        {
            Pump(clients);
            if (clients.All(c => c.Result != null)) return;
            var any = false;
            foreach (var c in clients) any |= c.TryPlay();
            if (!any)
            {
                Pump(clients);
                if (clients.All(c => c.Result != null)) return;
            }
        }
        throw new InvalidOperationException("Maç beklenen sürede bitmedi.");
    }
}

public static class Rooms
{
    public static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    /// <summary>İki istemcili hazır oda: host + konuk, ikisi de deste seçip hazır olmuş (maç başlamış).</summary>
    public static (RoomService Svc, FakeClient Host, FakeClient Guest, string Code) StartedMatch(
        string f0 = "northern_realms", string f1 = "monsters", int seed = 1234, ManualClock? clock = null, Difficulty d0 = Difficulty.Hard, Difficulty d1 = Difficulty.Hard)
    {
        var svc = new RoomService(clock, () => seed);
        var host = new FakeClient(svc, "Geralt", d0, seed);
        var guest = new FakeClient(svc, "Yennefer", d1, seed + 1);
        var code = host.Create().Value!;
        Assert.True(guest.Join(code).Ok);
        Assert.True(host.Choose(f0).Ok);
        Assert.True(guest.Choose(f1).Ok);
        Assert.True(host.Ready().Ok);
        Assert.True(guest.Ready().Ok);
        FakeClient.Pump(host, guest);
        return (svc, host, guest, code);
    }
}
