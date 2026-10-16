using GwentAI;
using GwentNet;
using GwentServer;

namespace GwentClient;

/// <summary>
/// İstemcinin oda bağlantısı (taşıma bağımsız taban): sunucudan gelen iletileri kuyruğa alır ve <see cref="Pump"/> ile
/// (arayüz iş parçacığında) oturuma (<see cref="GameSession"/>) ve arayüze iletir. İstekler iki biçimdedir:
/// <c>...Async</c> (arayüz için) ve bloklayan sarmalayıcılar (sınama/konsol için; arayüz iş parçacığında kullanma).
/// </summary>
public abstract class RoomLinkBase : IMatchLink
{
    private readonly Queue<Action> _inbox = new();
    private readonly object _lock = new();

    public RoomState? State { get; private set; }
    public MatchSetup? Setup { get; private set; }
    public GameSession? Match { get; private set; }
    public TournamentState? TState { get; private set; }
    public GameSession? Spectate { get; private set; }
    public MatchResult? Result { get; private set; }
    public string? RoomCode => State?.Code;
    public int Seat => State?.YourSeat ?? -1;

    /// <summary>Doluysa iletiler bunun üzerinden arayüz iş parçacığına aktarılır (<c>Dispatcher.BeginInvoke</c>); boşsa <see cref="Pump"/> elle çağrılır.</summary>
    public Action<Action>? Post { get; set; }

    public event Action? StateChanged;
    public event Action<GameSession>? MatchStarted;
    public event Action<MatchResult>? MatchEnded;
    public event Action<string, string>? Notice;
    public event Action? TournamentChanged;
    public event Action<GameSession>? SpectateStarted;
    public event Action<MatchResult>? SpectateEnded;

    // ---------- Sunucudan gelenler (herhangi bir iş parçacığında olabilir: yalnız kuyruğa al) ----------

    protected void Enqueue(Action a)
    {
        lock (_lock) _inbox.Enqueue(a);
        Post?.Invoke(Pump);
    }

    protected void ReceiveRoomState(RoomState s) => Enqueue(() => { State = s; StateChanged?.Invoke(); });

    protected void ReceiveMatchStarted(MatchSetup s)
    {
        Volatile.Write(ref _lastSeq, -1); // yeni maç: sıra numaraları 0'dan başlar
        Enqueue(() => StartMatch(s));
    }

    private void StartMatch(MatchSetup s)
    {
        Setup = s;
        Result = null;
        Match = new GameSession(s, this);
        MatchStarted?.Invoke(Match);
    }

    /// <summary>Sunucudan alınan son eylemin sıra numarası (ağ iş parçacığında güncellenir; yeniden bağlanmada eksikleri istemek için).</summary>
    public int LastReceivedSeq => Volatile.Read(ref _lastSeq);
    private int _lastSeq = -1;

    protected void ReceiveActionApplied(ActionBroadcast a)
    {
        Volatile.Write(ref _lastSeq, a.Seq);
        Enqueue(() => Match?.EnqueueRemote(a));
    }

    protected void ReceiveActionRejected(string reason) => Enqueue(() => Match?.NotifyRejected(reason));

    protected void ReceiveMatchEnded(MatchResult r) => Enqueue(() =>
    {
        Result = r;
        // Normal bitişte oyun kendiliğinden sona erer (kalan eylemler işlenince); kural dışı bitişte sonuç hemen gösterilir
        if (r.Reason != "normal" && Match != null) Match.ExternalResult = r;
        MatchEnded?.Invoke(r);
    });

    protected void ReceiveTournamentState(TournamentState s) => Enqueue(() => { TState = s; TournamentChanged?.Invoke(); });

    protected void ReceiveSpectateStarted(SpectateSetup s) => Enqueue(() => { Spectate = GameSession.ForSpectator(s); SpectateStarted?.Invoke(Spectate); });
    protected void ReceiveSpectateAction(ActionBroadcast a) => Enqueue(() => Spectate?.EnqueueRemote(a));
    protected void ReceiveSpectateEnded(MatchResult r) => Enqueue(() =>
    {
        if (Spectate != null && r.Reason != "normal") Spectate.ExternalResult = r;   // kural dışı bitiş (teslim/terk) hemen gösterilir
        SpectateEnded?.Invoke(r);
    });

    protected void ReceiveNotice(string code, string text) => Enqueue(() => Notice?.Invoke(code, text));

    /// <summary>Kuyruktaki iletileri işler. Arayüz iş parçacığında çağrılmalı.</summary>
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

    // ---------- İstekler ----------

    public abstract Task<Reply> CreateRoomAsync(string nick, bool nextGen = false);
    public abstract Task<Reply> JoinRoomAsync(string code, string nick);
    public abstract Task<Reply> SetLoadoutAsync(LoadoutDto loadout);
    public abstract Task<Reply> SetReadyAsync(bool ready);
    public abstract Task LeaveAsync();

    // Draft (oda kuran açar; seçimleri sunucu yürütür)
    public abstract Task<Reply> SetDraftAsync(bool on);
    public abstract Task<Reply> DraftChooseAsync(string kind, string id, string? avatar);
    // Lanetler (oda kuran seçer)
    public abstract Task<Reply> SetCursesAsync(List<string> curses);

    // Turnuva
    public abstract Task<Reply> CreateTournamentAsync(string nick, bool nextGen, int maxPlayers, int entryFee);
    public abstract Task<Reply> JoinTournamentAsync(string code, string nick);
    public abstract Task<Reply> SetTournamentLoadoutAsync(LoadoutDto loadout);
    public abstract Task<Reply> TournamentReadyAsync(bool ready);
    public abstract Task<Reply> StartTournamentAsync();
    public abstract Task<Reply> SetTournamentRulesAsync(TournamentRules rules);
    public abstract Task<Reply> WatchMatchAsync(int round, int index);
    public abstract Task<Reply> StopWatchingAsync();
    public abstract Task<Reply> TournamentWalkoverAsync();
    public abstract void Submit(ReplayAction action);
    public abstract void Resign();
    public abstract void RequestRematch();
    public abstract void ClaimWin();

    // Bloklayan sarmalayıcılar (sınama / konsol)
    public Reply CreateRoom(string nick, bool nextGen = false) => CreateRoomAsync(nick, nextGen).GetAwaiter().GetResult();
    public Reply JoinRoom(string code, string nick) => JoinRoomAsync(code, nick).GetAwaiter().GetResult();
    public Reply SetLoadout(LoadoutDto l) => SetLoadoutAsync(l).GetAwaiter().GetResult();
    public Reply SetReady(bool ready) => SetReadyAsync(ready).GetAwaiter().GetResult();
    public void Leave() => LeaveAsync().GetAwaiter().GetResult();
}

/// <summary>Aynı süreçteki sunucuya doğrudan bağlanan bağlantı (ağ yok); sınamalar ve tek bilgisayarda hızlı deneme için.</summary>
public sealed class RoomLink : RoomLinkBase, IGwentClient
{
    private readonly RoomService _svc;

    public RoomLink(RoomService svc)
    {
        _svc = svc;
        Session = svc.Connect(this);
    }

    public ServerSession Session { get; }

    // IGwentClient: sunucu kilidi altında çağrılır, yalnız kuyruğa alır
    public void OnRoomState(RoomState state) => ReceiveRoomState(state);
    public void OnMatchStarted(MatchSetup setup) => ReceiveMatchStarted(setup);
    public void OnActionApplied(ActionBroadcast action) => ReceiveActionApplied(action);
    public void OnActionRejected(string reason, int expectedSeq) => ReceiveActionRejected(reason);
    public void OnMatchEnded(MatchResult result) => ReceiveMatchEnded(result);
    public void OnNotice(string code, string text) => ReceiveNotice(code, text);
    public void OnTournamentState(TournamentState state) => ReceiveTournamentState(state);
    public void OnSpectateStarted(SpectateSetup setup) => ReceiveSpectateStarted(setup);
    public void OnSpectateAction(ActionBroadcast action) => ReceiveSpectateAction(action);
    public void OnSpectateEnded(MatchResult result) => ReceiveSpectateEnded(result);

    public override Task<Reply> CreateRoomAsync(string nick, bool nextGen = false) => Task.FromResult(_svc.CreateRoom(Session, nick, ProtocolInfo.Version, nextGen));
    public override Task<Reply> JoinRoomAsync(string code, string nick) => Task.FromResult(_svc.JoinRoom(Session, code, nick, ProtocolInfo.Version));
    public override Task<Reply> SetLoadoutAsync(LoadoutDto loadout) => Task.FromResult(_svc.SetLoadout(Session, loadout));
    public override Task<Reply> SetReadyAsync(bool ready) => Task.FromResult(_svc.SetReady(Session, ready));
    public override Task LeaveAsync() { _svc.Leave(Session); return Task.CompletedTask; }
    public override Task<Reply> SetDraftAsync(bool on) => Task.FromResult(_svc.SetDraft(Session, on));
    public override Task<Reply> DraftChooseAsync(string kind, string id, string? avatar) => Task.FromResult(_svc.DraftChoose(Session, kind, id, avatar));
    public override Task<Reply> SetCursesAsync(List<string> curses) => Task.FromResult(_svc.SetCurses(Session, curses));
    public override Task<Reply> CreateTournamentAsync(string nick, bool nextGen, int maxPlayers, int entryFee) => Task.FromResult(_svc.CreateTournament(Session, nick, ProtocolInfo.Version, nextGen, maxPlayers, entryFee));
    public override Task<Reply> JoinTournamentAsync(string code, string nick) => Task.FromResult(_svc.JoinTournament(Session, code, nick, ProtocolInfo.Version));
    public override Task<Reply> SetTournamentLoadoutAsync(LoadoutDto loadout) => Task.FromResult(_svc.SetTournamentLoadout(Session, loadout));
    public override Task<Reply> TournamentReadyAsync(bool ready) => Task.FromResult(_svc.TournamentSetReady(Session, ready));
    public override Task<Reply> StartTournamentAsync() => Task.FromResult(_svc.StartTournament(Session));
    public override Task<Reply> SetTournamentRulesAsync(TournamentRules rules) => Task.FromResult(_svc.SetTournamentRules(Session, rules));
    public override Task<Reply> WatchMatchAsync(int round, int index) => Task.FromResult(_svc.WatchMatch(Session, round, index));
    public override Task<Reply> StopWatchingAsync() => Task.FromResult(_svc.StopWatching(Session));
    public override Task<Reply> TournamentWalkoverAsync() => Task.FromResult(_svc.TournamentClaimWalkover(Session));
    public override void Submit(ReplayAction action) => _svc.SubmitAction(Session, action);
    public override void Resign() => _svc.Resign(Session);
    public override void RequestRematch() => _svc.RequestRematch(Session);
    public override void ClaimWin() => _svc.ClaimWin(Session);
}
