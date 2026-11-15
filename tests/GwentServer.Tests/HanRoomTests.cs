using GwentAI;
using GwentNet;
using GwentServer;
using HanLigi;

namespace GwentServer.Tests;

/// <summary>Han Ligi odası için bellek içi sahte istemci: kendi <see cref="HanGame"/> kopyasını tutar, sunucunun eylemlerini sırayla uygular.</summary>
public sealed class HanFake : IGwentClient, IHanClient
{
    private readonly RoomService _svc;
    private readonly Queue<Action> _inbox = new();
    private readonly Random _rng;

    public HanFake(RoomService svc, string nick, int seed = 1)
    {
        _svc = svc;
        Nick = nick;
        _rng = new Random(seed);
        Session = svc.Connect(this);
    }

    public string Nick { get; }
    public ServerSession Session { get; private set; }
    public HanRoomState? State { get; private set; }
    public HanSetup? Setup { get; private set; }
    public HanGame? Game { get; private set; }
    public int NextSeq { get; private set; }
    public List<string> Notices { get; } = new();
    public int Seat => State?.YourSeat ?? -1;
    public string Token => State?.YourToken ?? "";

    public void OnHanState(HanRoomState s) => _inbox.Enqueue(() => State = s);
    public void OnHanStarted(HanSetup s) => _inbox.Enqueue(() =>
    {
        Setup = s;
        Game = new HanGame(s.Seed, s.Names.Select((n, i) => (n, s.Bots[i])).ToList());
        NextSeq = 0;
    });
    public void OnHanAction(HanBroadcast b) => _inbox.Enqueue(() =>
    {
        Assert.Equal(NextSeq, b.Seq);                 // boşluksuz, sıralı
        Assert.True(Game!.Apply(b.Action), $"{Nick}: sunucunun kabul ettiği eylemi kopya reddetti: {b.Action}");
        NextSeq++;
    });

    public void OnRoomState(RoomState state) { }
    public void OnMatchStarted(MatchSetup setup) { }
    public void OnActionApplied(ActionBroadcast action) { }
    public void OnActionRejected(string reason, int expectedSeq) { }
    public void OnMatchEnded(MatchResult result) { }
    public void OnNotice(string code, string text) => _inbox.Enqueue(() => Notices.Add(code));
    public void OnTournamentState(TournamentState state) { }
    public void OnSpectateStarted(SpectateSetup setup) { }
    public void OnSpectateAction(ActionBroadcast action) { }
    public void OnSpectateEnded(MatchResult result) { }

    public void Pump() { while (_inbox.Count > 0) _inbox.Dequeue()(); }

    public void Reconnect()
    {
        _svc.Disconnect(Session);
        Session = _svc.Connect(this);
    }

    /// <summary>Kendi koltuğu için yapılacak bir şey varsa bir eylem gönderir (bazen kural dışı da dener). Bir şey yaptıysa true.</summary>
    public bool Act()
    {
        Pump();
        if (Game is not { } g || Seat < 0) return false;
        var m = g.M;
        var me = Seat;
        if (g.Bots.Contains(me) || !m.Players[me].Alive) return false;
        var p = m.Players[me];
        HanAction? a = null;
        if (m.Phase == Phase.School && p.School == null) a = new HanAction(HanActKind.School, me, (int)p.SchoolOptions[_rng.Next(3)]);
        else if (m.Phase == Phase.Prep && !p.Ready)
        {
            if (p.RewardOptions.Length > 0) a = new HanAction(HanActKind.Reward, me, 0);
            else if (p.PotionOptions.Length > 0) a = new HanAction(HanActKind.Potion, me, 0);
            else if (p.Bench.FirstOrDefault(c => m.CanMove(me, c.Uid, Match.ZonesFor(c.Def).First())) is { } bc) a = new HanAction(HanActKind.Move, me, bc.Uid, (int)Match.ZonesFor(bc.Def).First());
            else a = _rng.Next(4) switch
            {
                0 when Enumerable.Range(0, Tuning.ShopSize).FirstOrDefault(i => m.CanBuy(me, i), -1) is var s and >= 0 => new HanAction(HanActKind.Buy, me, s),
                1 => new HanAction(HanActKind.Buy, me, 99),   // kural dışı
                _ => new HanAction(HanActKind.Ready, me)
            };
        }
        else if (m.Phase == Phase.Battle && g.BattleIndexOf(me) is var bi and >= 0)
        {
            var b = m.Battles[bi];
            var side = m.SideOf(b, me);
            if (!b.Started && !g.Activated(bi, side)) a = HanAction.Activation(me, bi, side, b.DefaultActivation(side));
            else if (b.Started && !b.Done && !b.Submitted(side)) a = HanAction.Choice(me, bi, side, _rng.Next(3) == 0 ? WindowChoice.None : HanBot.Window(b, side, _rng));
        }
        if (a == null) return false;
        var r = _svc.HanSubmit(Session, a);
        if (!r.Ok && a.Kind == HanActKind.Window) _svc.HanSubmit(Session, HanAction.Choice(me, a.A, a.B, WindowChoice.None));
        else if (!r.Ok && a.Kind is HanActKind.Reward or HanActKind.Potion) _svc.HanSubmit(Session, new HanAction(HanActKind.Ready, me));   // depo dolu vb.: hazır de
        Pump();
        return true;
    }
}

public class HanRoomTests
{
    private static (RoomService Svc, ManualClock Clock) Server(int seed = 11)
    {
        var clock = new ManualClock();
        var n = seed;
        return (new RoomService(clock, () => n++ * 7919 % 100003 + 1), clock);
    }

    private static void PlayOut(RoomService svc, ManualClock clock, List<HanFake> players, int guard = 30000)
    {
        var room = svc.FindHan(players[0].State!.Code)!;
        for (var i = 0; i < guard && room.Phase == RoomPhase.Playing; i++)
        {
            var any = false;
            foreach (var p in players) any |= p.Act();
            if (!any) { clock.Advance(TimeSpan.FromSeconds(121)); svc.TickHan(); }
            foreach (var p in players) p.Pump();
            var fp = room.Game!.Fingerprint();
            foreach (var p in players.Where(p => p.Game != null)) Assert.Equal(fp, p.Game!.Fingerprint());
        }
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(3, 6)]
    [InlineData(1, 8)]
    [InlineData(2, 2)]
    public void Room_fills_with_bots_and_all_copies_finish_identically(int humans, int size)
    {
        var (svc, clock) = Server(humans * 10 + size);
        var ps = Enumerable.Range(0, humans).Select(i => new HanFake(svc, "Oyuncu" + i, i + 1)).ToList();
        Assert.True(svc.CreateHanRoom(ps[0].Session, ps[0].Nick, ProtocolInfo.Version, size).Ok);
        ps[0].Pump();
        var code = ps[0].State!.Code;
        foreach (var p in ps.Skip(1))
        {
            var r = svc.JoinRoom(p.Session, code, p.Nick, ProtocolInfo.Version);
            Assert.True(r.Ok, r.Error);
            Assert.Equal("han:" + code, r.Value);
        }
        foreach (var p in ps) { p.Pump(); Assert.True(svc.HanSetReady(p.Session, true).Ok); }
        foreach (var p in ps) p.Pump();
        Assert.All(ps, p => Assert.NotNull(p.Game));
        Assert.Equal(size, ps[0].Setup!.Names.Count);
        Assert.Equal(size - humans, ps[0].Setup!.Bots.Count(b => b));
        PlayOut(svc, clock, ps);
        var room = svc.FindHan(code)!;
        var g = room.Game!;
        var diag = $"round {g.M.Round} phase {g.M.Phase} actions {room.Actions.Count} pending [{string.Join("; ", g.PendingDefaults())}] canEnd {g.RoundCanEnd} "
            + string.Join(" | ", g.M.Battles.Select((b, i) => $"#{i} {b.Sides[0].PlayerId}v{b.Sides[1].PlayerId} pve{b.Pve} ghost{b.Ghost} started{b.Started} done{b.Done} w{b.Window} sub{b.Submitted(0)}/{b.Submitted(1)} act{g.Activated(i, 0)}/{g.Activated(i, 1)}"))
            + " last: " + string.Join("; ", room.Actions.TakeLast(6));
        Assert.True(room.Phase == RoomPhase.Finished, diag);
        Assert.Single(room.Game!.M.Players, p => p.Place == 1);
    }

    [Fact]
    public void Only_own_seat_and_no_server_actions()
    {
        var (svc, _) = Server();
        var a = new HanFake(svc, "Ali");
        var b = new HanFake(svc, "Banu");
        svc.CreateHanRoom(a.Session, "Ali", ProtocolInfo.Version, 4);
        a.Pump();
        svc.JoinRoom(b.Session, a.State!.Code, "Banu", ProtocolInfo.Version);
        svc.HanSetReady(a.Session, true);
        svc.HanSetReady(b.Session, true);
        a.Pump(); b.Pump();
        var bs = b.Seat;
        Assert.False(svc.HanSubmit(a.Session, new HanAction(HanActKind.School, bs, 0)).Ok);
        Assert.False(svc.HanSubmit(a.Session, new HanAction(HanActKind.Lock, -1)).Ok);
        Assert.False(svc.HanSubmit(a.Session, new HanAction(HanActKind.EndRound, -1)).Ok);
        Assert.False(svc.HanSubmit(a.Session, new HanAction(HanActKind.Abandon, bs)).Ok);
    }

    [Fact]
    public void Disconnected_player_rejoins_with_missing_actions_then_abandons_to_bot()
    {
        var (svc, clock) = Server(5);
        var a = new HanFake(svc, "Ali", 1);
        var b = new HanFake(svc, "Banu", 2);
        svc.CreateHanRoom(a.Session, "Ali", ProtocolInfo.Version, 8);
        a.Pump();
        var code = a.State!.Code;
        svc.JoinRoom(b.Session, code, "Banu", ProtocolInfo.Version);
        svc.HanSetReady(a.Session, true);
        svc.HanSetReady(b.Session, true);
        a.Pump(); b.Pump();
        var token = b.Token;
        // birkaç round oyna, sonra B kopsun
        var room = svc.FindHan(code)!;
        for (var i = 0; i < 400 && room.Game!.M.Round < 4; i++) { a.Act(); b.Act(); if (i % 50 == 49) { clock.Advance(TimeSpan.FromSeconds(121)); svc.TickHan(); } }
        var seen = b.NextSeq;
        b.Reconnect();
        for (var i = 0; i < 100 && room.Game!.M.Round < 6; i++) { a.Act(); clock.Advance(TimeSpan.FromSeconds(30)); svc.TickHan(); a.Pump(); }
        // geri dön: kaçırdığı eylemler gelir, kopyası sunucuyla aynı olur
        Assert.True(svc.Rejoin(b.Session, code, token, seen - 1).Ok);
        b.Pump();
        Assert.Equal(room.Game!.Fingerprint(), b.Game!.Fingerprint());
        // uzun süre kopuk kalırsa koltuğu bot devralır, maç biter
        b.Reconnect();
        clock.Advance(TimeSpan.FromMinutes(3));
        svc.TickHan();
        Assert.Contains(b.Seat, room.Game!.Bots);
        PlayOut(svc, clock, new List<HanFake> { a });
        Assert.Equal(RoomPhase.Finished, room.Phase);
    }

    [Fact]
    public void Host_sets_size_and_leaving_guest_frees_seat_in_lobby()
    {
        var (svc, _) = Server();
        var a = new HanFake(svc, "Ali");
        var b = new HanFake(svc, "Banu");
        svc.CreateHanRoom(a.Session, "Ali", ProtocolInfo.Version, 8);
        a.Pump();
        svc.JoinRoom(b.Session, a.State!.Code, "Banu", ProtocolInfo.Version);
        Assert.False(svc.HanSetSize(b.Session, 4).Ok);   // yalnız host
        Assert.True(svc.HanSetSize(a.Session, 4).Ok);
        Assert.False(svc.HanSetSize(a.Session, 1).Ok);
        svc.Leave(b.Session);
        a.Pump();
        Assert.Single(a.State!.Seats);
        Assert.Equal(4, a.State.Size);
        svc.HanSetReady(a.Session, true);                // yalnız host kaldı: 3 botla başlar
        a.Pump();
        Assert.Equal(4, a.Setup!.Names.Count);
    }
}
