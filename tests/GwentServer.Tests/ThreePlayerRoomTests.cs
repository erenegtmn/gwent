using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Üç kişilik oda: üç koltuk, aynı kurulum, sunucu ve üç kopya olay olay aynı (KURALLAR §24).</summary>
public class ThreePlayerRoomTests
{
    private static (RoomService Svc, FakeClient[] C, string Code) Started(int seed = 99, ManualClock? clock = null,
        string f0 = "northern_realms", string f1 = "monsters", string f2 = "skellige")
    {
        var svc = new RoomService(clock, () => seed);
        var c = new[]
        {
            new FakeClient(svc, "Geralt", new RandomAgent(seed)),
            new FakeClient(svc, "Yennefer", new RandomAgent(seed + 1)),
            new FakeClient(svc, "Ciri", new RandomAgent(seed + 2)),
        };
        var code = c[0].Create(players: 3).Value!;
        Assert.True(c[1].Join(code).Ok);
        Assert.True(c[2].Join(code).Ok);
        Assert.True(c[0].Choose(f0).Ok);
        Assert.True(c[1].Choose(f1).Ok);
        Assert.True(c[2].Choose(f2).Ok);
        foreach (var x in c) Assert.True(x.Ready().Ok);
        FakeClient.Pump(c);
        return (svc, c, code);
    }

    [Fact]
    public void Three_seats_fill_in_order_and_fourth_is_refused()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var code = host.Create(players: 3).Value!;
        var b = new FakeClient(svc, "Yennefer");
        var c = new FakeClient(svc, "Ciri");
        Assert.True(b.Join(code).Ok);
        Assert.False(new FakeClient(svc, "yennefer").Join(code).Ok);   // aynı takma ad
        Assert.True(c.Join(code).Ok);
        Assert.False(new FakeClient(svc, "Triss").Join(code).Ok);
        FakeClient.Pump(host, b, c);
        Assert.Equal(3, host.State!.Capacity);
        Assert.Equal(new[] { 0, 1, 2 }, new[] { host.Seat, b.Seat, c.Seat });
        Assert.Equal(3, c.State!.Players.Count);
        Assert.False(svc.CreateRoom(svc.Connect(new FakeClient(svc, "Zoltan")), "Zoltan", ProtocolInfo.Version, false, 4).Ok);
    }

    [Fact]
    public void Match_waits_for_all_three_and_curses_draft_are_refused()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var b = new FakeClient(svc, "Yennefer");
        var code = host.Create(players: 3).Value!;
        Assert.True(b.Join(code).Ok);
        Assert.False(svc.SetCurses(host.Session, new[] { Curses.All[0].Id }).Ok);
        Assert.False(svc.SetDraft(host.Session, true).Ok);
        Assert.True(host.Choose("northern_realms").Ok);
        Assert.True(b.Choose("monsters").Ok);
        Assert.True(host.Ready().Ok);
        Assert.True(b.Ready().Ok);
        Assert.Equal(RoomPhase.Lobby, svc.Find(code)!.Phase);   // üçüncü yokken başlamaz

        var c = new FakeClient(svc, "Ciri");
        Assert.True(c.Join(code).Ok);
        Assert.True(c.Choose("skellige").Ok);
        Assert.True(c.Ready().Ok);
        FakeClient.Pump(host, b, c);
        Assert.Equal(RoomPhase.Playing, svc.Find(code)!.Phase);
        Assert.Equal(3, host.Setup!.PlayerCount);
        Assert.Equal(new[] { 0, 1, 2 }, new[] { host.Setup.YourSeat, b.Setup!.YourSeat, c.Setup!.YourSeat });
        Assert.Equal("Ciri", host.Setup.Nick2);
        Assert.Equal(host.Setup.Seed, c.Setup.Seed);
        Assert.Equal(3, host.Local!.PlayerCount);
        Assert.Equal(host.Local.DiceRolls.Select(r => string.Join(",", r)), c.Local!.DiceRolls.Select(r => string.Join(",", r)));
    }

    [Fact]
    public void Full_three_player_matches_keep_server_and_all_copies_identical()
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            var f = Rooms.Factions;
            var (svc, c, code) = Started(seed, null, f[seed % 5], f[(seed + 1) % 5], f[(seed + 3) % 5]);
            FakeClient.RunToEnd(20000, c);
            var room = svc.Find(code)!;
            var server = room.Game!;
            Assert.Equal(Phase.GameOver, server.Phase);
            foreach (var x in c)
            {
                Assert.Equal(server.Outcome!.Winner, x.Result!.WinnerSeat);
                Assert.Equal(server.Events.Select(e => (e.Kind, e.Player, e.CardUid, e.Detail)), x.Local!.Events.Select(e => (e.Kind, e.Player, e.CardUid, e.Detail)));
            }
        }
    }

    [Fact]
    public void Resign_in_three_player_gives_the_match_to_the_leader_among_the_rest_or_a_draw()
    {
        var (svc, c, code) = Started();
        Assert.True(svc.Resign(c[1].Session).Ok);
        FakeClient.Pump(c);
        Assert.All(c, x => Assert.Equal("resigned", x.Result!.Reason));
        Assert.Null(c[0].Result!.WinnerSeat);   // kimse raund kazanmadı: berabere
    }

    [Fact]
    public void Disconnect_notifies_both_others_and_claim_win_works_after_grace()
    {
        var clock = new ManualClock();
        var (svc, c, code) = Started(clock: clock);
        c[2].Disconnect();
        FakeClient.Pump(c);
        Assert.Contains("opponent_disconnected", c[0].Notices);
        Assert.Contains("opponent_disconnected", c[1].Notices);
        Assert.False(svc.ClaimWin(c[0].Session).Ok);
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True(svc.ClaimWin(c[0].Session).Ok);
        FakeClient.Pump(c);
        Assert.Equal("abandoned", c[0].Result!.Reason);
    }

    [Fact]
    public void Reconnect_resends_missing_actions_to_the_third_seat()
    {
        var (svc, c, code) = Started(seed: 5);
        for (var i = 0; i < 30; i++) { FakeClient.Pump(c); foreach (var x in c) x.TryPlay(); }
        FakeClient.Pump(c);
        c[2].Disconnect();
        for (var i = 0; i < 20; i++) { FakeClient.Pump(c[0], c[1]); c[0].TryPlay(); c[1].TryPlay(); }
        Assert.True(c[2].Reconnect(code).Ok);
        FakeClient.RunToEnd(20000, c);
        var server = svc.Find(code)!.Game!;
        Assert.Equal(server.Events.Count, c[2].Local!.Events.Count);
    }

    [Fact]
    public void Rematch_needs_all_three()
    {
        var (svc, c, code) = Started(seed: 8);
        FakeClient.RunToEnd(20000, c);
        Assert.True(svc.RequestRematch(c[0].Session).Ok);
        Assert.True(svc.RequestRematch(c[1].Session).Ok);
        Assert.Equal(RoomPhase.Finished, svc.Find(code)!.Phase);
        Assert.True(svc.RequestRematch(c[2].Session).Ok);
        Assert.Equal(RoomPhase.Playing, svc.Find(code)!.Phase);
    }

    [Fact]
    public void Snapshot_restores_a_three_player_match()
    {
        var (svc, c, code) = Started(seed: 11);
        for (var i = 0; i < 25; i++) { FakeClient.Pump(c); foreach (var x in c) x.TryPlay(); }
        FakeClient.Pump(c);
        var snap = svc.Find(code)!.ExportSnapshot();
        Assert.NotNull(snap);
        Assert.NotNull(snap!.Token2);
        var svc2 = new RoomService();
        Assert.True(svc2.RestoreRoom(snap).Ok);
        var room = svc2.Find(code)!;
        Assert.Equal(3, room.Capacity);
        Assert.Equal(svc.Find(code)!.Game!.Events.Count, room.Game!.Events.Count);
    }
}
