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
}
