using System.Collections.Concurrent;
using System.Threading.Channels;
using GwentAI;
using GwentNet;
using GwentServer;
using Microsoft.AspNetCore.SignalR;

namespace GwentHost;

/// <summary>
/// Sunucudan istemciye iletileri **sırayla** gönderir. Tek bir bağlantıya gidecek iletiler bir kuyruğa yazılır ve tek bir
/// görev tarafından sırayla gönderilir; böylece eylemler ağda da sıra numarasıyla aynı sırada varır. Sunucu kilidi altında
/// çağrıldığı için asla beklemez (yalnız kuyruğa yazar).
/// </summary>
public sealed class HubClientProxy : IGwentClient
{
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IClientProxy _client;

    public HubClientProxy(IClientProxy client)
    {
        _client = client;
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        await foreach (var send in _queue.Reader.ReadAllAsync())
        {
            try { await send().ConfigureAwait(false); }
            catch { /* bağlantı kopmuş olabilir; yeniden bağlanınca durum yeniden gönderilir */ }
        }
    }

    private void Send(string method, params object?[] args) => _queue.Writer.TryWrite(() => _client.SendCoreAsync(method, args));

    public void Close() => _queue.Writer.TryComplete();

    public void OnRoomState(RoomState state) => Send(HubMethods.OnRoomState, state);
    public void OnMatchStarted(MatchSetup setup) => Send(HubMethods.OnMatchStarted, setup);
    public void OnActionApplied(ActionBroadcast action) => Send(HubMethods.OnActionApplied, action);
    public void OnActionRejected(string reason, int expectedSeq) => Send(HubMethods.OnActionRejected, reason, expectedSeq);
    public void OnMatchEnded(MatchResult result) => Send(HubMethods.OnMatchEnded, result);
    public void OnNotice(string code, string text) => Send(HubMethods.OnNotice, code, text);
    public void OnTournamentState(TournamentState state) => Send(HubMethods.OnTournamentState, state);
    public void OnSpectateStarted(SpectateSetup setup) => Send(HubMethods.OnSpectateStarted, setup);
    public void OnSpectateAction(ActionBroadcast action) => Send(HubMethods.OnSpectateAction, action);
    public void OnSpectateEnded(MatchResult result) => Send(HubMethods.OnSpectateEnded, result);
}

/// <summary>Bağlantı kimliği → sunucu oturumu.</summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, (ServerSession Session, HubClientProxy Proxy)> _map = new();
    public void Add(string id, ServerSession s, HubClientProxy p) => _map[id] = (s, p);

    public bool TryGet(string id, out ServerSession s)
    {
        var ok = _map.TryGetValue(id, out var v);
        s = ok ? v.Session : null!;
        return ok;
    }

    public bool Remove(string id, out (ServerSession Session, HubClientProxy Proxy) v) => _map.TryRemove(id, out v);
}

/// <summary>
/// SignalR bağdaştırıcısı: istemci isteklerini <see cref="RoomService"/> sınıfına iletir. İş mantığı burada değil RoomService içindedir;
/// hub yalnız bağlantı ile oturum eşlemesini yapar.
/// </summary>
public sealed class GwentHub : Hub
{
    private readonly RoomService _rooms;
    private readonly SessionRegistry _registry;
    private readonly IHubContext<GwentHub> _context;

    public GwentHub(RoomService rooms, SessionRegistry registry, IHubContext<GwentHub> context)
    {
        _rooms = rooms;
        _registry = registry;
        _context = context;
    }

    private ServerSession Session => _registry.TryGet(Context.ConnectionId, out var s) ? s : throw new HubException("Oturum bulunamadı.");

    public override Task OnConnectedAsync()
    {
        var proxy = new HubClientProxy(_context.Clients.Client(Context.ConnectionId));
        _registry.Add(Context.ConnectionId, _rooms.Connect(proxy), proxy);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (_registry.Remove(Context.ConnectionId, out var v))
        {
            _rooms.Disconnect(v.Session);
            v.Proxy.Close();
        }
        return base.OnDisconnectedAsync(exception);
    }

    public Reply CreateRoom(string nick, string version, bool nextGen) => _rooms.CreateRoom(Session, nick, version, nextGen);
    public Reply JoinRoom(string code, string nick, string version) => _rooms.JoinRoom(Session, code, nick, version);
    public Reply Rejoin(string code, string token, int lastSeq) => _rooms.Rejoin(Session, code, token, lastSeq);
    public Reply SetLoadout(LoadoutDto loadout) => _rooms.SetLoadout(Session, loadout);
    public Reply SetReady(bool ready) => _rooms.SetReady(Session, ready);
    public Reply SetDraft(bool on) => _rooms.SetDraft(Session, on);
    public Reply DraftChoose(string kind, string id, string? avatar) => _rooms.DraftChoose(Session, kind, id, avatar);
    public Reply SetCurses(List<string>? curses) => _rooms.SetCurses(Session, curses);
    public Reply SubmitAction(ReplayAction action) => _rooms.SubmitAction(Session, action);
    public Reply Resign() => _rooms.Resign(Session);
    public Reply ClaimWin() => _rooms.ClaimWin(Session);
    public Reply RequestRematch() => _rooms.RequestRematch(Session);
    public void Leave() => _rooms.Leave(Session);

    // Turnuva
    public Reply CreateTournament(string nick, string version, bool nextGen, int maxPlayers, int entryFee) => _rooms.CreateTournament(Session, nick, version, nextGen, maxPlayers, entryFee);
    public Reply JoinTournament(string code, string nick, string version) => _rooms.JoinTournament(Session, code, nick, version);
    public Reply RejoinTournament(string code, string token, int lastSeq) => _rooms.RejoinTournament(Session, code, token, lastSeq);
    public Reply SetTournamentLoadout(LoadoutDto loadout) => _rooms.SetTournamentLoadout(Session, loadout);
    public Reply TournamentSetReady(bool ready) => _rooms.TournamentSetReady(Session, ready);
    public Reply SetTournamentRules(TournamentRules rules) => _rooms.SetTournamentRules(Session, rules);
    public Reply WatchMatch(int round, int index) => _rooms.WatchMatch(Session, round, index);
    public Reply StopWatching() => _rooms.StopWatching(Session);
    public Reply StartTournament() => _rooms.StartTournament(Session);
    public Reply TournamentClaimWalkover() => _rooms.TournamentClaimWalkover(Session);
}
