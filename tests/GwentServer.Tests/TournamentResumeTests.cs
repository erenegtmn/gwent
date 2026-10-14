using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Turnuvanın host uygulaması kapanıp açılınca kayıttan devamı.</summary>
public class TournamentResumeTests
{
    private static LoadoutDto Pick(int i)
    {
        var f = Rooms.Factions[i % Rooms.Factions.Length];
        return new LoadoutDto(f, CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id);
    }

    private static void Drive(RoomService svc, List<FakeClient> clients, int maxSteps = 30000)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            FakeClient.Pump(clients.ToArray());
            if (clients.All(c => c.TState?.Phase == TournamentPhase.Finished)) return;
            foreach (var c in clients)
            {
                var ts = c.TState;
                if (ts is { Phase: TournamentPhase.Running, YourMatchRound: { } r, YourMatchIndex: { } ix })
                {
                    var m = ts.Matches.First(x => x.Round == r && x.Index == ix);
                    if (m.Status == "ready" && !(m.A == ts.YourIndex ? m.ReadyA : m.ReadyB)) svc.TournamentSetReady(c.Session, true);
                }
                if (c.Result is { WinnerSeat: null } && ts?.Phase == TournamentPhase.Running) svc.RequestRematch(c.Session);
                c.TryPlay();
            }
        }
        throw new InvalidOperationException("Turnuva beklenen sürede bitmedi.");
    }

    /// <summary>4 kişilik turnuva; ilk yarı finalde maç oynanıyor ve birkaç hamle yapılmış. Döner: servis, istemciler, kod ve son kayıt.</summary>
    private static (RoomService Svc, List<FakeClient> Clients, string Code, TournamentSnapshot Snap) Midway(int seed)
    {
        var next = seed;
        var svc = new RoomService(null, () => next++);
        TournamentSnapshot? last = null;
        svc.TournamentPersisted += (_, snap) => { if (snap != null) last = snap; };
        var clients = Enumerable.Range(0, 4).Select(i => new FakeClient(svc, "Oyuncu" + i, Difficulty.Hard, seed + i)).ToList();
        var code = svc.CreateTournament(clients[0].Session, clients[0].Nick, ProtocolInfo.Version, false, 4, 0).Value!;
        for (var i = 1; i < 4; i++) svc.JoinTournament(clients[i].Session, code, clients[i].Nick, ProtocolInfo.Version);
        for (var i = 0; i < 4; i++) { svc.SetTournamentLoadout(clients[i].Session, Pick(i)); svc.TournamentSetReady(clients[i].Session, true); }
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var a = clients[m.A!.Value];
        var b = clients[m.B!.Value];
        svc.TournamentSetReady(a.Session, true);
        svc.TournamentSetReady(b.Session, true);
        for (var i = 0; i < 10; i++) { FakeClient.Pump(clients.ToArray()); a.TryPlay(); b.TryPlay(); }
        FakeClient.Pump(clients.ToArray());
        Assert.NotNull(last);
        return (svc, clients, code, last!);
    }

    [Fact]
    public void A_running_tournament_resumes_from_its_snapshot_and_plays_to_a_champion()
    {
        var (_, _, code, snap) = Midway(seed: 400);
        Assert.Contains(snap.Matches, m => m.Room != null && m.Room.Actions.Count > 0);                // oynanan maç kayıtta

        // "Host uygulaması kapandı": yeni sunucu, yeni istemciler, yalnız kayıt elde
        var svc2 = new RoomService(null, () => 5000);
        Assert.True(svc2.RestoreTournament(snap).Ok);
        Assert.NotNull(svc2.FindTournament(code));
        var back = snap.Players.Select(p => new FakeClient(svc2, p.Nick, Difficulty.Hard, 77)).ToList();
        for (var i = 0; i < 4; i++) Assert.True(svc2.RejoinTournament(back[i].Session, code, snap.Players[i].Token, -1).Ok);
        FakeClient.Pump(back.ToArray());
        Assert.All(back, c => Assert.Equal(TournamentPhase.Running, c.TState!.Phase));
        Assert.All(back, c => Assert.True(c.TState!.Players.All(p => p.Connected)));

        // oynanmakta olan maçın iki oyuncusu geçmişi aldı ve kopyaları sunucuyla aynı
        var playing = snap.Matches.First(m => m.Room != null);
        var duel = new[] { back[playing.A!.Value], back[playing.B!.Value] };
        Assert.All(duel, c => Assert.NotNull(c.Local));
        Assert.Equal(playing.Room!.Actions.Count, duel[0].NextSeq);
        Drive(svc2, back);
        Assert.Single(back[0].TState!.Players, p => p.Placement == 1);
        Assert.Single(back[0].TState!.Players, p => p.Placement == 2);
    }

    [Fact]
    public void Restore_refuses_corrupted_snapshots_and_duplicate_codes()
    {
        var (svc, _, _, snap) = Midway(seed: 410);
        Assert.False(svc.RestoreTournament(snap).Ok);                                                      // zaten açık

        var svc2 = new RoomService();
        var bad = snap with
        {
            Matches = snap.Matches.Select(m => m.Room == null ? m : m with { Room = m.Room with { Actions = m.Room.Actions.Concat(new[] { m.Room.Actions[0] }).ToList() } }).ToList()
        };
        Assert.False(svc2.RestoreTournament(bad).Ok);                                                      // oyunda geçersiz eylem
        Assert.Equal(0, svc2.TournamentCount);
        Assert.Equal(0, svc2.RoomCount);
        Assert.False(svc2.RestoreTournament(snap with { Players = snap.Players.Take(2).ToList() }).Ok);   // eksik kayıt
        Assert.True(svc2.RestoreTournament(snap).Ok);
    }

    [Fact]
    public void Eliminated_players_and_finished_matches_survive_the_restart()
    {
        var next = 420;
        var svc = new RoomService(null, () => next++);
        TournamentSnapshot? last = null;
        svc.TournamentPersisted += (_, s) => { if (s != null) last = s; };
        var clients = Enumerable.Range(0, 4).Select(i => new FakeClient(svc, "Oyuncu" + i, Difficulty.Hard, 420 + i)).ToList();
        var code = svc.CreateTournament(clients[0].Session, clients[0].Nick, ProtocolInfo.Version, false, 4, 0).Value!;
        for (var i = 1; i < 4; i++) svc.JoinTournament(clients[i].Session, code, clients[i].Nick, ProtocolInfo.Version);
        for (var i = 0; i < 4; i++) { svc.SetTournamentLoadout(clients[i].Session, Pick(i)); svc.TournamentSetReady(clients[i].Session, true); }
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        svc.Leave(clients[m.A!.Value].Session);                                                            // biri ayrılır: rakibi finale çıkar
        FakeClient.Pump(clients.ToArray());
        Assert.NotNull(last);

        var svc2 = new RoomService(null, () => 1);
        Assert.True(svc2.RestoreTournament(last!).Ok);
        var other = clients.First(c => c != clients[m.A.Value] && c != clients[m.B!.Value] && c.Nick != clients[0].Nick || c == clients[m.B.Value]);
        var back = new FakeClient(svc2, other.Nick);
        var idx = last!.Players.FindIndex(p => p.Nick == other.Nick);
        Assert.True(svc2.RejoinTournament(back.Session, code, last.Players[idx].Token, -1).Ok);
        back.Pump();
        Assert.True(back.TState!.Players[m.A.Value].Eliminated);
        Assert.Equal(m.B, back.TState.Matches.First(x => x.Round == 0 && x.Index == 0).Winner);
        Assert.Equal(m.B, back.TState.Matches.First(x => x.Round == 1).A);                                // finalde yerini korudu
    }
}
