using GwentAI;
using GwentCore;
using GwentNet;
using Microsoft.AspNetCore.SignalR.Client;

namespace GwentHost.Tests;

public class SignalRTests : IAsyncLifetime
{
    private LocalServer _server = null!;

    public async Task InitializeAsync() => _server = await LocalServer.StartAsync();
    public async Task DisposeAsync() => await _server.DisposeAsync();

    private async Task<(NetClient Host, NetClient Guest, string Code)> StartedMatchAsync(string f0 = "northern_realms", string f1 = "monsters", int seed = 1)
    {
        var host = await NetClient.ConnectAsync(_server.LocalUrl, "Geralt", Difficulty.Hard, seed);
        var guest = await NetClient.ConnectAsync(_server.LocalUrl, "Yennefer", Difficulty.Hard, seed + 1);
        var created = await host.CreateAsync();
        Assert.True(created.Ok, created.Error);
        var code = created.Value!;
        Assert.True((await guest.JoinAsync(code)).Ok);
        Assert.True((await host.ChooseAsync(f0)).Ok);
        Assert.True((await guest.ChooseAsync(f1)).Ok);
        Assert.True((await host.ReadyAsync()).Ok);
        Assert.True((await guest.ReadyAsync()).Ok);
        await host.WaitAsync(() => host.Local != null, "maç başlangıcı");
        await guest.WaitAsync(() => guest.Local != null, "maç başlangıcı");
        return (host, guest, code);
    }

    [Fact]
    public async Task Server_answers_over_http_and_assigns_a_free_port()
    {
        Assert.True(_server.Port > 0);
        using var http = new HttpClient();
        var text = await http.GetStringAsync($"http://127.0.0.1:{_server.Port}/");
        Assert.Contains(ProtocolInfo.Version, text);
    }

    [Fact]
    public async Task Create_and_join_over_the_wire_gives_both_players_the_room_state()
    {
        await using var host = await NetClient.ConnectAsync(_server.LocalUrl, "Geralt");
        await using var guest = await NetClient.ConnectAsync(_server.LocalUrl, "Yennefer");
        var created = await host.CreateAsync();
        Assert.True(created.Ok);
        Assert.Equal(5, created.Value!.Length);
        await host.WaitAsync(() => host.State is { Players.Count: 1 }, "oda durumu");

        Assert.True((await guest.JoinAsync(created.Value)).Ok);
        await host.WaitAsync(() => host.State is { Players.Count: 2 }, "konuk göründü");
        await guest.WaitAsync(() => guest.State is { YourSeat: 1, Players.Count: 2 }, "konuk koltuğu");
        Assert.Equal(0, host.State!.YourSeat);
        Assert.Equal(1, _server.Rooms.RoomCount);
    }

    [Fact]
    public async Task Version_mismatch_and_bad_requests_come_back_as_failed_replies()
    {
        await using var c = await NetClient.ConnectAsync(_server.LocalUrl, "Geralt");
        var wrong = await c.Conn.InvokeAsync<Reply>("CreateRoom", "Geralt", "p0-eski", false);
        Assert.False(wrong.Ok);
        Assert.Contains("Sürüm", wrong.Error);
        Assert.False((await c.JoinAsync("ZZZZZ")).Ok);
        Assert.False((await c.ReadyAsync()).Ok); // odada değil
    }

    [Theory]
    [InlineData("northern_realms", "monsters", 1)]
    [InlineData("nilfgaard", "skellige", 2)]
    [InlineData("scoiatael", "northern_realms", 3)]
    public async Task Full_match_over_signalr_ends_identically_on_server_and_both_clients(string f0, string f1, int seed)
    {
        var (host, guest, code) = await StartedMatchAsync(f0, f1, seed);
        await using var _h = host;
        await using var _g = guest;
        await NetClient.RunToEndAsync(60000, host, guest);

        var room = _server.Rooms.Find(code)!;
        Assert.Equal(RoomPhase.Finished, room.Phase);
        Assert.Equal(room.Game!.Events.Count, host.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, guest.Local!.Events.Count);
        for (var i = 0; i < room.Game.Events.Count; i++)
        {
            Assert.Equal(room.Game.Events[i], host.Local.Events[i]);
            Assert.Equal(room.Game.Events[i], guest.Local.Events[i]);
        }
        Assert.Equal(room.Game.Outcome!.Winner, host.Result!.WinnerSeat);
    }

    [Fact]
    public async Task Cursed_match_with_mirror_swap_plays_out_identically_over_the_wire()
    {
        await using var host = await NetClient.ConnectAsync(_server.LocalUrl, "Geralt", Difficulty.Hard, 5);
        await using var guest = await NetClient.ConnectAsync(_server.LocalUrl, "Yennefer", Difficulty.Hard, 6);
        var code = (await host.CreateAsync()).Value!;
        Assert.True((await guest.JoinAsync(code)).Ok);
        Assert.True((await host.ChooseAsync("nilfgaard")).Ok);
        Assert.True((await guest.ChooseAsync("skellige")).Ok);
        var curses = new List<string> { Curses.Mirror, Curses.ShortCandle, Curses.EndlessWinter, Curses.BloodPrice };
        Assert.True((await host.Conn.InvokeAsync<Reply>("SetCurses", curses)).Ok);
        Assert.False((await guest.Conn.InvokeAsync<Reply>("SetCurses", new List<string>())).Ok);   // konuk seçemez
        await guest.WaitAsync(() => guest.State?.Curses?.Count == 4, "konuk lanetleri görür");
        Assert.True((await host.ReadyAsync()).Ok);
        Assert.True((await guest.ReadyAsync()).Ok);
        await host.WaitAsync(() => host.Local != null, "maç başlangıcı");
        await guest.WaitAsync(() => guest.Local != null, "maç başlangıcı");
        Assert.Equal("skellige", guest.Setup!.Seat0.Faction);       // Ayna: host konuğun destesiyle
        Assert.Equal(4, guest.Setup.Curses!.Count);
        Assert.Equal(8, guest.Local!.Options.StartingHand);

        await NetClient.RunToEndAsync(60000, host, guest);
        var room = _server.Rooms.Find(code)!;
        Assert.Equal(room.Game!.Events.Count, host.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, guest.Local.Events.Count);
        for (var i = 0; i < room.Game.Events.Count; i++) Assert.Equal(room.Game.Events[i], guest.Local.Events[i]);
    }

    [Fact]
    public async Task Illegal_action_over_the_wire_is_rejected_and_state_unchanged()
    {
        var (host, guest, code) = await StartedMatchAsync(seed: 9);
        await using var _h = host;
        await using var _g = guest;
        var room = _server.Rooms.Find(code)!;
        var before = room.ActionLog.Count;
        var reply = await guest.Conn.InvokeAsync<Reply>("SubmitAction", ReplayData.Encode(new Pass(0))); // başkası adına
        Assert.False(reply.Ok);
        var garbage = await guest.Conn.InvokeAsync<Reply>("SubmitAction", new ReplayAction { Type = "hile", Player = 1 });
        Assert.False(garbage.Ok);
        await guest.WaitAsync(() => guest.Rejections.Count >= 2, "ret bildirimleri");
        Assert.Equal(before, room.ActionLog.Count);
    }

    [Fact]
    public async Task Dropped_connection_notifies_the_opponent_and_a_new_connection_can_rejoin_with_the_token()
    {
        var (host, guest, code) = await StartedMatchAsync(seed: 21);
        await using var _h = host;
        for (var i = 0; i < 12; i++) { host.Pump(); guest.Pump(); await host.TryPlayAsync(); await guest.TryPlayAsync(); await Task.Delay(5); }
        host.Pump(); guest.Pump();
        var token = guest.Token;
        var lastSeq = guest.NextSeq - 1;
        var gameCopy = guest.Local;
        var nextSeq = guest.NextSeq;
        await guest.DisposeAsync(); // bağlantı koptu

        await host.WaitAsync(() => host.Notices.Contains("opponent_disconnected"), "kopma bildirimi");

        // host oynamaya devam eder (konuk yok)
        for (var i = 0; i < 40; i++) { host.Pump(); await host.TryPlayAsync(); await Task.Delay(3); }

        // yeni bağlantı, aynı koltuk anahtarıyla; eski kopya ve sıra numarası korunur
        var back = await NetClient.ConnectAsync(_server.LocalUrl, "Yennefer", Difficulty.Hard, 22);
        await using var _b = back;
        back.Local = gameCopy;
        back.NextSeq = nextSeq;
        var r = await back.Conn.InvokeAsync<Reply>("Rejoin", code, token, lastSeq);
        Assert.True(r.Ok, r.Error);
        await host.WaitAsync(() => host.Notices.Contains("opponent_returned"), "dönüş bildirimi");
        await back.WaitAsync(() => back.State != null, "oda durumu");

        await NetClient.RunToEndAsync(60000, host, back);
        var room = _server.Rooms.Find(code)!;
        Assert.Equal(room.Game!.Events.Count, back.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, host.Local!.Events.Count);
    }

    [Fact]
    public async Task Resign_over_the_wire_ends_the_match_for_both()
    {
        var (host, guest, _) = await StartedMatchAsync(seed: 30);
        await using var _h = host;
        await using var _g = guest;
        Assert.True((await guest.Conn.InvokeAsync<Reply>("Resign")).Ok);
        await host.WaitAsync(() => host.Result != null, "sonuç");
        await guest.WaitAsync(() => guest.Result != null, "sonuç");
        Assert.Equal(0, host.Result!.WinnerSeat);
        Assert.Equal("resigned", host.Result.Reason);
    }

    [Fact]
    public async Task A_match_survives_the_host_application_restarting()
    {
        var (host, guest, code) = await StartedMatchAsync(seed: 91);
        RoomSnapshot? saved = null;
        _server.Rooms.RoomPersisted += (_, snap) => { if (snap != null) saved = snap; };
        for (var i = 0; i < 9; i++)
        {
            host.Pump(); guest.Pump();
            await host.TryPlayAsync(); await guest.TryPlayAsync();
            await Task.Delay(3);
        }
        host.Pump(); guest.Pump();
        Assert.NotNull(saved);
        var snap0 = saved!;

        // Host uygulaması kapanır: sunucu ve iki bağlantı gider; bellekte hiçbir şey kalmaz
        await host.DisposeAsync();
        await guest.DisposeAsync();
        await _server.DisposeAsync();

        // Uygulama yeniden açılır: yeni sunucu (yeni port), kayıttan oda, iki taraf eski anahtarlarla döner
        _server = await LocalServer.StartAsync();
        Assert.True(_server.Rooms.RestoreRoom(snap0).Ok);
        await using var host2 = await NetClient.ConnectAsync(_server.LocalUrl, "Geralt", Difficulty.Hard, 91);
        await using var guest2 = await NetClient.ConnectAsync(_server.LocalUrl, "Yennefer", Difficulty.Hard, 92);
        Assert.True((await host2.RejoinAsync(code, snap0.Token0)).Ok);
        Assert.True((await guest2.RejoinAsync(code, snap0.Token1)).Ok);
        await host2.WaitAsync(() => host2.Local != null && host2.NextSeq == snap0.Actions.Count, "geçmiş yeniden geldi");
        await guest2.WaitAsync(() => guest2.Local != null && guest2.NextSeq == snap0.Actions.Count, "geçmiş yeniden geldi");

        await NetClient.RunToEndAsync(30000, host2, guest2);
        Assert.Equal(host2.Result!.WinnerSeat, guest2.Result!.WinnerSeat);
        var room = _server.Rooms.Find(code)!;
        Assert.Equal(room.Game!.Events.Count, host2.Local!.Events.Count);
    }

    [Fact]
    public async Task A_three_player_tournament_runs_over_the_real_network()
    {
        var factions = new[] { "northern_realms", "nilfgaard", "monsters" };
        var players = new List<NetClient>();
        for (var i = 0; i < 3; i++) players.Add(await NetClient.ConnectAsync(_server.LocalUrl, "Oyuncu" + i, Difficulty.Hard, 200 + i));
        var created = await players[0].CreateTournamentAsync(4, 50);
        Assert.True(created.Ok, created.Error);
        for (var i = 1; i < 3; i++) Assert.True((await players[i].JoinTournamentAsync(created.Value!)).Ok);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await players[i].TournamentLoadoutAsync(factions[i])).Ok);
            Assert.True((await players[i].TournamentReadyAsync()).Ok);
        }
        await players[0].WaitAsync(() => players[0].TState is { Players.Count: 3 }, "oyuncular göründü");
        Assert.True((await players[0].StartTournamentAsync()).Ok);

        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until && !players.All(p => p.TState?.Phase == TournamentPhase.Finished))
        {
            foreach (var p in players)
            {
                p.Pump();
                var ts = p.TState;
                if (ts is { Phase: TournamentPhase.Running, YourMatchRound: { } r, YourMatchIndex: { } ix })
                {
                    var m = ts.Matches.First(x => x.Round == r && x.Index == ix);
                    var mine = m.A == ts.YourIndex ? m.ReadyA : m.ReadyB;
                    if (m.Status == "ready" && !mine) await p.TournamentReadyAsync();
                }
                if (p.Result is { WinnerSeat: null } && ts?.Phase == TournamentPhase.Running) await p.Conn.InvokeAsync<Reply>("RequestRematch");
                await p.TryPlayAsync();
            }
            await Task.Delay(3);
        }
        foreach (var p in players) p.Pump();
        Assert.All(players, p => Assert.Equal(TournamentPhase.Finished, p.TState!.Phase));
        Assert.Single(players[0].TState!.Players, x => x.Placement == 1);
        Assert.Equal(50, players[0].TState!.EntryFee);
        foreach (var p in players) await p.DisposeAsync();
    }
}
