using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Online lanetler: oda kuran seçer, iki taraf da aynı bükülmüş kuralla oynar; Ayna online'da deste takasıdır.</summary>
public class CurseRoomTests
{
    private static (RoomService Svc, FakeClient Host, FakeClient Guest, string Code) Room(int seed = 1234)
    {
        var svc = new RoomService(null, () => seed);
        var host = new FakeClient(svc, "Geralt", seed: 3);
        var guest = new FakeClient(svc, "Yennefer", seed: 4);
        var code = host.Create().Value!;
        Assert.True(guest.Join(code).Ok);
        Assert.True(host.Choose("northern_realms").Ok);
        Assert.True(guest.Choose("monsters").Ok);
        FakeClient.Pump(host, guest);
        return (svc, host, guest, code);
    }

    private static void Start(FakeClient host, FakeClient guest)
    {
        Assert.True(host.Ready().Ok);
        Assert.True(guest.Ready().Ok);
        FakeClient.Pump(host, guest);
        Assert.NotNull(host.Setup);
    }

    private static void AssertSameGame(Room room, FakeClient host, FakeClient guest)
    {
        Assert.Equal(room.Game!.Events.Count, host.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, guest.Local!.Events.Count);
        for (var i = 0; i < room.Game.Events.Count; i++)
        {
            Assert.Equal(room.Game.Events[i], host.Local.Events[i]);
            Assert.Equal(room.Game.Events[i], guest.Local.Events[i]);
        }
    }

    [Fact]
    public void Only_the_host_picks_curses_unknown_ones_are_refused_and_a_change_resets_readiness()
    {
        var (svc, host, guest, _) = Room();
        Assert.False(svc.SetCurses(guest.Session, new[] { Curses.ShortCandle }).Ok);
        Assert.False(svc.SetCurses(host.Session, new[] { "bogus" }).Ok);
        Assert.True(guest.Ready().Ok);
        Assert.True(svc.SetCurses(host.Session, new[] { Curses.BloodPrice, Curses.ShortCandle, Curses.ShortCandle }).Ok);
        FakeClient.Pump(host, guest);
        Assert.Equal(new[] { Curses.ShortCandle, Curses.BloodPrice }, guest.State!.Curses);   // sıra Curses.All, tekrar yok
        Assert.False(guest.State.Players[1].Ready);                                              // rakip yeni kuralı görmeden maça girmez
        Assert.True(svc.SetCurses(host.Session, Array.Empty<string>()).Ok);
        FakeClient.Pump(host, guest);
        Assert.Null(guest.State!.Curses);
    }

    [Fact]
    public void Engine_curses_reach_server_and_both_copies_and_the_match_plays_out_identically()
    {
        var (svc, host, guest, code) = Room(seed: 4321);
        Assert.True(svc.SetCurses(host.Session, new[] { Curses.ShortCandle, Curses.EndlessWinter, Curses.NoHeroes, Curses.BloodPrice }).Ok);
        Start(host, guest);
        var room = svc.Find(code)!;
        Assert.Equal(4, host.Setup!.Curses!.Count);
        Assert.Equal(8, room.Game!.Options.StartingHand);
        Assert.True(room.Game.Options.RoundWeather && room.Game.Options.BloodPrice);
        Assert.Equal(8, host.Local!.Players[0].Hand.Count);
        Assert.DoesNotContain(room.Game.Players.SelectMany(p => p.Hand.Concat(p.Deck)), c => c.Def.Hero);   // kahramansız çağ iki tarafta
        FakeClient.RunToEnd(4000, host, guest);
        AssertSameGame(room, host, guest);
        Assert.Contains(room.Game.Events, e => e.Kind == "curse_weather");
    }

    [Fact]
    public void Mirror_online_swaps_the_two_decks_keeps_portraits_and_rematch_swaps_again()
    {
        var (svc, host, guest, code) = Room();
        Assert.True(host.SetLoadout(host.State!.Players[0].Loadout! with { Avatar = "avatar_geralt" }).Ok);
        Assert.True(guest.SetLoadout(guest.State!.Players[1].Loadout! with { Avatar = "face_dijkstra" }).Ok);
        Assert.True(svc.SetCurses(host.Session, new[] { Curses.Mirror }).Ok);
        Start(host, guest);
        var s = host.Setup!;
        Assert.Equal("monsters", s.Seat0.Faction);                 // host rakibin destesiyle
        Assert.Equal("northern_realms", s.Seat1.Faction);          // konuk host'unkiyle
        Assert.Equal("avatar_geralt", s.Seat0.Avatar);             // portre oyuncuda kalır
        Assert.Equal("face_dijkstra", s.Seat1.Avatar);
        Assert.Null(s.EngineCurses());                             // motora yerel Ayna gitmez (yoksa konuk da host destesini alırdı)
        var room = svc.Find(code)!;
        Assert.Equal("monsters", room.Game!.Players[0].Faction);
        Assert.Equal("northern_realms", room.Game.Players[1].Faction);

        FakeClient.RunToEnd(4000, host, guest);
        AssertSameGame(room, host, guest);
        Assert.True(svc.RequestRematch(host.Session).Ok);
        Assert.True(svc.RequestRematch(guest.Session).Ok);
        FakeClient.Pump(host, guest);
        Assert.Equal("monsters", host.Setup!.Seat0.Faction);      // takas oyuncuların kendi seçiminden: iki kez takaslanmaz
    }

    [Fact]
    public void A_cursed_match_resumes_from_the_save_with_its_curses()
    {
        var (svc, host, guest, code) = Room(seed: 99);
        Assert.True(svc.SetCurses(host.Session, new[] { Curses.Mirror, Curses.ShortCandle }).Ok);
        RoomSnapshot? saved = null;
        svc.RoomPersisted += (_, snap) => saved = snap;
        Start(host, guest);
        for (var i = 0; i < 10; i++) { FakeClient.Pump(host, guest); host.TryPlay(); guest.TryPlay(); }
        FakeClient.Pump(host, guest);
        Assert.NotNull(saved);

        var svc2 = new RoomService(null, () => 1);
        Assert.True(svc2.RestoreRoom(saved!).Ok);
        var room = svc2.Find(code)!;
        Assert.Equal(new[] { Curses.ShortCandle, Curses.Mirror }.OrderBy(x => x), room.Curses.OrderBy(x => x));
        Assert.Equal(8, room.Game!.Options.StartingHand);
        Assert.Equal("monsters", room.Game.Players[0].Faction);
        var host2 = new FakeClient(svc2, "Geralt");
        var guest2 = new FakeClient(svc2, "Yennefer");
        Assert.True(svc2.Rejoin(host2.Session, code, saved!.Token0, -1).Ok);
        Assert.True(svc2.Rejoin(guest2.Session, code, saved.Token1, -1).Ok);
        FakeClient.RunToEnd(4000, host2, guest2);
        AssertSameGame(room, host2, guest2);
    }

    [Fact]
    public void Curses_cannot_change_once_the_match_started()
    {
        var (svc, host, guest, _) = Room();
        Start(host, guest);
        Assert.False(svc.SetCurses(host.Session, new[] { Curses.ShortCandle }).Ok);
    }
}
