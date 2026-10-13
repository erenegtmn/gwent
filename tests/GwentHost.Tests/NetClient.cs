using GwentAI;
using GwentCore;
using GwentNet;
using Microsoft.AspNetCore.SignalR.Client;

namespace GwentHost.Tests;

/// <summary>Gerçek SignalR bağlantısı kuran sahte istemci: kendi <see cref="Game"/> kopyasını tutar ve sırası gelince ajanla oynar.</summary>
public sealed class NetClient : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly Queue<Action> _inbox = new();
    private readonly IAgent _agent;
    private bool _awaitingOwn;

    private NetClient(HubConnection conn, string nick, IAgent agent)
    {
        Conn = conn;
        Nick = nick;
        _agent = agent;
        conn.On<RoomState>(HubMethods.OnRoomState, s => Enqueue(() => State = s));
        conn.On<MatchSetup>(HubMethods.OnMatchStarted, s => Enqueue(() =>
        {
            Setup = s;
            Local = s.ToReplay().CreateGame();
            NextSeq = 0;
            Result = null;
            _awaitingOwn = false;
        }));
        conn.On<ActionBroadcast>(HubMethods.OnActionApplied, a => Enqueue(() =>
        {
            Assert.Equal(NextSeq, a.Seq);
            Assert.True(Local!.Apply(ReplayData.Decode(a.Action)).Ok, $"kopya eylemi reddetti: seq {a.Seq}");
            NextSeq++;
            if (a.Player == Seat) _awaitingOwn = false;
        }));
        conn.On<string, int>(HubMethods.OnActionRejected, (r, _) => Enqueue(() => { Rejections.Add(r); _awaitingOwn = false; }));
        conn.On<MatchResult>(HubMethods.OnMatchEnded, r => Enqueue(() => Result = r));
        conn.On<string, string>(HubMethods.OnNotice, (code, _) => Enqueue(() => Notices.Add(code)));
    }

    public HubConnection Conn { get; }
    public string Nick { get; }
    public RoomState? State { get; private set; }
    public MatchSetup? Setup { get; private set; }
    public Game? Local { get; set; }
    public int NextSeq { get; set; }
    public MatchResult? Result { get; private set; }
    public List<string> Notices { get; } = new();
    public List<string> Rejections { get; } = new();
    public int Seat => State?.YourSeat ?? -1;
    public string Token => State?.YourToken ?? "";

    public static async Task<NetClient> ConnectAsync(string url, string nick, Difficulty d = Difficulty.Hard, int seed = 1)
    {
        var conn = new HubConnectionBuilder().WithUrl(url).Build();
        var c = new NetClient(conn, nick, AgentFactory.Create(d, seed));
        await conn.StartAsync();
        return c;
    }

    private void Enqueue(Action a) { lock (_lock) _inbox.Enqueue(a); }

    public void Pump()
    {
        while (true)
        {
            Action a;
            lock (_lock)
            {
                if (_inbox.Count == 0) return;
                a = _inbox.Dequeue();
            }
            a();
        }
    }

    public async Task<bool> TryPlayAsync()
    {
        if (Local == null || Result != null || Local.Phase == Phase.GameOver || _awaitingOwn) return false;
        if (!Local.PlayersToAct().Contains(Seat)) return false;
        var a = _agent.Decide(Local, Seat);
        _awaitingOwn = true;
        var r = await Conn.InvokeAsync<Reply>("SubmitAction", ReplayData.Encode(a));
        Assert.True(r.Ok, r.Error);
        return true;
    }

    public Task<Reply> CreateAsync(bool nextGen = false) => Conn.InvokeAsync<Reply>("CreateRoom", Nick, ProtocolInfo.Version, nextGen);
    public Task<Reply> JoinAsync(string code) => Conn.InvokeAsync<Reply>("JoinRoom", code, Nick, ProtocolInfo.Version);
    public Task<Reply> ReadyAsync(bool ready = true) => Conn.InvokeAsync<Reply>("SetReady", ready);

    public Task<Reply> ChooseAsync(string faction)
    {
        var leader = CardCatalog.Default.Leaders(faction).First(l => l.Edition != "nextgen").Id;
        return Conn.InvokeAsync<Reply>("SetLoadout", new LoadoutDto(faction, leader));
    }

    public async Task WaitAsync(Func<bool> cond, string what, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            Pump();
            if (cond()) return;
            await Task.Delay(5);
        }
        Pump();
        Assert.True(cond(), $"{Nick}: zaman aşımı — {what}");
    }

    public static async Task RunToEndAsync(int timeoutMs, params NetClient[] clients)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            foreach (var c in clients) c.Pump();
            if (clients.All(c => c.Result != null)) return;
            var any = false;
            foreach (var c in clients) any |= await c.TryPlayAsync();
            if (!any) await Task.Delay(2);
        }
        throw new TimeoutException("Maç beklenen sürede bitmedi.");
    }

    public async ValueTask DisposeAsync() => await Conn.DisposeAsync();
}
