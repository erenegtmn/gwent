using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

public class RoomTests
{
    // ---------- Oda kurma / katılma ----------

    [Fact]
    public void Create_and_join_flow_gives_both_players_the_room_state()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var guest = new FakeClient(svc, "Yennefer");

        var created = host.Create();
        Assert.True(created.Ok);
        Assert.Equal(5, created.Value!.Length);
        host.Pump();
        Assert.Equal(RoomPhase.Lobby, host.State!.Phase);
        Assert.Equal(0, host.State.YourSeat);
        Assert.Single(host.State.Players);

        Assert.True(guest.Join(created.Value.ToLowerInvariant()).Ok); // küçük harf de olur
        FakeClient.Pump(host, guest);
        Assert.Equal(1, guest.State!.YourSeat);
        Assert.Equal(2, host.State!.Players.Count);
        Assert.Equal(2, guest.State.Players.Count);
        Assert.NotEqual(host.Token, guest.Token);
    }

    [Fact]
    public void Join_is_refused_for_unknown_code_full_room_duplicate_nick_and_version_mismatch()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var code = host.Create().Value!;

        Assert.False(new FakeClient(svc, "Ciri").Join("ZZZZZ").Ok);
        Assert.False(new FakeClient(svc, "geralt").Join(code).Ok);           // aynı takma ad (büyük/küçük harf fark etmez)
        Assert.False(new FakeClient(svc, "x").Join(code).Ok);                // çok kısa
        Assert.False(svc.JoinRoom(svc.Connect(new FakeClient(svc, "Dandelion")), code, "Dandelion", "p0-eski").Ok);
        Assert.False(svc.CreateRoom(svc.Connect(new FakeClient(svc, "Triss")), "Triss", "p0-eski").Ok);

        var guest = new FakeClient(svc, "Yennefer");
        Assert.True(guest.Join(code).Ok);
        Assert.False(new FakeClient(svc, "Ciri").Join(code).Ok);              // oda dolu
        Assert.False(guest.Create().Ok);                                      // zaten bir odada
    }

    [Fact]
    public void Loadout_is_validated_and_ready_needs_a_loadout()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var guest = new FakeClient(svc, "Yennefer");
        var code = host.Create().Value!;
        guest.Join(code);

        Assert.False(host.Ready().Ok);                                        // deste seçmeden hazır olunmaz
        Assert.False(host.Choose("narnia").Ok);
        Assert.False(host.Choose("northern_realms", "emhyr_var_emreis_his_imperial_majesty").Ok); // başka fraksiyonun lideri
        Assert.False(host.Choose("northern_realms", cards: new List<string> { "geralt_of_rivia" }).Ok); // çok kısa deste
        Assert.False(host.Choose("northern_realms", cards: new List<string> { "yok_boyle_kart" }).Ok);
        Assert.True(host.Choose("northern_realms").Ok);
        Assert.True(host.Ready().Ok);

        // yeni deste seçmek hazır durumunu sıfırlar
        Assert.True(host.Choose("nilfgaard").Ok);
        FakeClient.Pump(host, guest);
        Assert.False(host.State!.Players.First(p => p.Seat == 0).Ready);
    }

    [Fact]
    public void Match_starts_when_both_are_ready_with_identical_setup_and_own_seat()
    {
        var (_, host, guest, _) = Rooms.StartedMatch(seed: 777);
        Assert.Equal(RoomPhase.Playing, host.State!.Phase);
        Assert.Equal(777, host.Setup!.Seed);
        Assert.Equal(host.Setup.Seed, guest.Setup!.Seed);
        Assert.Equal(0, host.Setup.YourSeat);
        Assert.Equal(1, guest.Setup.YourSeat);
        Assert.Equal("northern_realms", host.Setup.Seat0.Faction);
        Assert.Equal("monsters", guest.Setup.Seat1.Faction);
        // iki taraf da açık deste alır (CardIds dolu)
        Assert.NotNull(host.Setup.Seat1.CardIds);
        Assert.NotNull(guest.Setup.Seat0.CardIds);
        Assert.False(host.Choose("skellige").Ok); // maç sürerken deste değişmez
    }

    // ---------- Tam maç: sunucu ve iki kopya birebir aynı ----------

    [Theory]
    [InlineData("northern_realms", "monsters", 1)]
    [InlineData("nilfgaard", "scoiatael", 2)]
    [InlineData("skellige", "northern_realms", 3)]
    [InlineData("monsters", "skellige", 4)]
    [InlineData("scoiatael", "nilfgaard", 5)]
    public void Full_match_server_and_both_clients_end_identically(string f0, string f1, int seed)
    {
        var (svc, host, guest, code) = Rooms.StartedMatch(f0, f1, seed * 101);
        FakeClient.RunToEnd(8000, host, guest);

        var room = svc.Find(code)!;
        Assert.Equal(RoomPhase.Finished, room.Phase);
        Assert.Equal(room.Game!.Outcome!.Winner, host.Result!.WinnerSeat);
        Assert.Equal(host.Result, guest.Result);
        Assert.Equal("normal", host.Result!.Reason);
        Assert.Equal(room.Game.Events.Count, host.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, guest.Local!.Events.Count);
        for (var i = 0; i < room.Game.Events.Count; i++)
        {
            Assert.Equal(room.Game.Events[i], host.Local.Events[i]);
            Assert.Equal(room.Game.Events[i], guest.Local.Events[i]);
        }
        Assert.Equal(room.ActionLog.Count, host.NextSeq);
        Assert.Equal(room.ActionLog.Count, guest.NextSeq);
    }

    [Fact]
    public void Many_random_matches_with_mixed_difficulties_stay_in_sync()
    {
        for (var i = 0; i < 40; i++)
        {
            var d0 = (Difficulty)(i % 3);
            var d1 = (Difficulty)((i / 3) % 3);
            var (svc, host, guest, code) = Rooms.StartedMatch(Rooms.Factions[i % 5], Rooms.Factions[(i / 5 + 1) % 5], 9000 + i, null, d0, d1);
            FakeClient.RunToEnd(8000, host, guest);
            var room = svc.Find(code)!;
            Assert.Equal(room.Game!.Events.Count, host.Local!.Events.Count);
            Assert.Equal(room.Game.Events.Count, guest.Local!.Events.Count);
            Assert.Equal(room.Game.Outcome!.Winner, host.Result!.WinnerSeat);
        }
    }

    // ---------- Eylem doğrulama ----------

    [Fact]
    public void Out_of_turn_impersonating_and_garbage_actions_are_rejected_without_changing_state()
    {
        var (svc, host, guest, code) = Rooms.StartedMatch(seed: 55);
        var room = svc.Find(code)!;
        FakeClient.Pump(host, guest);
        // kart değiştirmeyi bitir (iki taraf da boş değişiklik)
        Assert.True(svc.SubmitAction(host.Session, ReplayData.Encode(new Mulligan(0, new List<int>()))).Ok);
        Assert.True(svc.SubmitAction(guest.Session, ReplayData.Encode(new Mulligan(1, new List<int>()))).Ok);
        FakeClient.Pump(host, guest);
        var turn = room.Game!.PlayersToAct()[0];
        var notTurn = 1 - turn;
        var notTurnClient = notTurn == 0 ? host : guest;
        var turnClient = turn == 0 ? host : guest;

        var before = room.Game.Events.Count;
        var logBefore = room.ActionLog.Count;

        // sırası olmayan oynayamaz
        Assert.False(svc.SubmitAction(notTurnClient.Session, ReplayData.Encode(new Pass(notTurn))).Ok);
        // başkası adına eylem
        Assert.False(svc.SubmitAction(notTurnClient.Session, ReplayData.Encode(new Pass(turn))).Ok);
        // anlaşılmayan eylem
        Assert.False(svc.SubmitAction(turnClient.Session, new ReplayAction { Type = "hile", Player = turn }).Ok);
        // elde olmayan kart
        Assert.False(svc.SubmitAction(turnClient.Session, ReplayData.Encode(new PlayCard(turn, 99999))).Ok);
        // bozuk satır adı
        Assert.False(svc.SubmitAction(turnClient.Session, new ReplayAction { Type = "play", Player = turn, Card = 1, Row = "Gokyuzu" }).Ok);

        Assert.Equal(before, room.Game.Events.Count);
        Assert.Equal(logBefore, room.ActionLog.Count);
        FakeClient.Pump(host, guest);
        Assert.True(notTurnClient.Rejections.Count >= 2);
    }

    [Fact]
    public void Actions_outside_a_match_are_refused()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        Assert.False(svc.SubmitAction(host.Session, ReplayData.Encode(new Pass(0))).Ok);   // odada değil
        host.Create();
        Assert.False(svc.SubmitAction(host.Session, ReplayData.Encode(new Pass(0))).Ok);   // bekleme odası
    }

    // ---------- Teslim, kopma, yeniden bağlanma, rövanş ----------

    [Fact]
    public void Resigning_ends_the_match_and_the_other_player_wins()
    {
        var (svc, host, guest, _) = Rooms.StartedMatch(seed: 8);
        Assert.True(svc.Resign(guest.Session).Ok);
        FakeClient.Pump(host, guest);
        Assert.Equal(0, host.Result!.WinnerSeat);
        Assert.Equal("resigned", host.Result.Reason);
        Assert.Equal(host.Result, guest.Result);
        Assert.False(svc.Resign(host.Session).Ok); // zaten bitti
    }

    [Fact]
    public void Disconnected_player_can_rejoin_and_receives_the_missed_actions()
    {
        var (svc, host, guest, code) = Rooms.StartedMatch(seed: 31);
        // birkaç eylem oynansın
        for (var i = 0; i < 12; i++) { FakeClient.Pump(host, guest); host.TryPlay(); guest.TryPlay(); }
        FakeClient.Pump(host, guest);
        var playedBefore = svc.Find(code)!.ActionLog.Count;
        Assert.True(playedBefore > 4);

        guest.Disconnect();
        FakeClient.Pump(host);
        Assert.Contains("opponent_disconnected", host.Notices);
        Assert.False(host.State!.Players.First(p => p.Seat == 1).Connected);

        // konuk yokken host oynamaya devam eder; kaçırılan eylemler olur
        for (var i = 0; i < 30; i++) { host.Pump(); host.TryPlay(); }
        var playedDuring = svc.Find(code)!.ActionLog.Count;

        Assert.True(guest.Reconnect(code).Ok);
        FakeClient.Pump(host, guest);
        Assert.Contains("opponent_returned", host.Notices);
        Assert.Equal(playedDuring, guest.NextSeq); // eksikler gönderildi, boşluk yok

        FakeClient.RunToEnd(8000, host, guest);
        var room = svc.Find(code)!;
        Assert.Equal(room.Game!.Events.Count, guest.Local!.Events.Count);
        Assert.Equal(room.Game.Events.Count, host.Local!.Events.Count);
    }

    [Fact]
    public void Rejoin_with_a_wrong_token_or_wrong_room_fails()
    {
        var (svc, host, guest, code) = Rooms.StartedMatch(seed: 32);
        guest.Disconnect();
        var thief = new FakeClient(svc, "Hirsiz");
        var s = svc.Connect(thief);
        Assert.False(svc.Rejoin(s, code, "YANLIS", -1).Ok);
        Assert.False(svc.Rejoin(s, "ZZZZZ", guest.Token, -1).Ok);
        Assert.False(svc.Rejoin(s, code, "", -1).Ok);
        Assert.True(svc.Rejoin(s, code, guest.Token, -1).Ok);                      // doğru anahtar
    }

    [Fact]
    public void Claiming_a_win_needs_the_grace_period_to_pass()
    {
        var clock = new ManualClock();
        var (svc, host, guest, code) = Rooms.StartedMatch(seed: 40, clock: clock);
        Assert.False(svc.ClaimWin(host.Session).Ok);                                // rakip bağlı
        guest.Disconnect();
        Assert.False(svc.ClaimWin(host.Session).Ok);                                // süre dolmadı
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(svc.ClaimWin(host.Session).Ok);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(svc.ClaimWin(host.Session).Ok);
        FakeClient.Pump(host);
        Assert.Equal(0, host.Result!.WinnerSeat);
        Assert.Equal("abandoned", host.Result.Reason);
    }

    [Fact]
    public void Rematch_starts_a_fresh_match_when_both_ask()
    {
        var n = 100;
        var svc = new RoomService(null, () => n++);
        var host = new FakeClient(svc, "Geralt");
        var guest = new FakeClient(svc, "Yennefer");
        var code = host.Create().Value!;
        guest.Join(code);
        host.Choose("nilfgaard");
        guest.Choose("skellige");
        host.Ready();
        guest.Ready();
        FakeClient.Pump(host, guest);
        FakeClient.RunToEnd(8000, host, guest);
        var firstSeed = host.Setup!.Seed;

        Assert.True(host.Result != null);
        Assert.True(svc.RequestRematch(host.Session).Ok);
        FakeClient.Pump(guest);
        Assert.Contains("rematch_requested", guest.Notices);
        Assert.True(svc.RequestRematch(guest.Session).Ok);
        FakeClient.Pump(host, guest);

        Assert.NotEqual(firstSeed, host.Setup!.Seed);
        Assert.Null(host.Result);
        Assert.Equal(RoomPhase.Playing, svc.Find(code)!.Phase);
        FakeClient.RunToEnd(8000, host, guest);
        Assert.Equal(svc.Find(code)!.Game!.Events.Count, host.Local!.Events.Count);
    }

    // ---------- Ayrılma ve temizlik ----------

    [Fact]
    public void Host_leaving_the_lobby_closes_the_room_and_a_guest_leaving_frees_the_seat()
    {
        var svc = new RoomService();
        var host = new FakeClient(svc, "Geralt");
        var guest = new FakeClient(svc, "Yennefer");
        var code = host.Create().Value!;
        guest.Join(code);

        svc.Leave(guest.Session);
        FakeClient.Pump(host);
        Assert.Single(host.State!.Players);
        Assert.True(new FakeClient(svc, "Ciri").Join(code).Ok);                     // koltuk boşaldı

        svc.Leave(host.Session);
        Assert.Null(svc.Find(code));                                                // host çıkınca oda kapanır
        Assert.Equal(0, svc.RoomCount);
    }

    [Fact]
    public void Leaving_during_a_match_counts_as_resigning()
    {
        var (svc, host, guest, code) = Rooms.StartedMatch(seed: 12);
        svc.Leave(guest.Session);
        FakeClient.Pump(host);
        Assert.Equal(0, host.Result!.WinnerSeat);
        Assert.Equal("resigned", host.Result.Reason);
        Assert.NotNull(svc.Find(code)); // host hâlâ içeride; sonucu görsün
    }

    [Fact]
    public void Sweep_removes_idle_and_abandoned_rooms_but_keeps_active_ones()
    {
        var clock = new ManualClock();
        var svc = new RoomService(clock);
        var a = new FakeClient(svc, "A1");
        var b = new FakeClient(svc, "B1");
        a.Create();
        b.Create();
        Assert.Equal(0, svc.Sweep(TimeSpan.FromHours(1)));
        clock.Advance(TimeSpan.FromHours(2));
        b.Choose("monsters");                                                       // B hareket etti
        Assert.Equal(1, svc.Sweep(TimeSpan.FromHours(1)));                          // yalnız A'nın odası düşer
        Assert.Equal(1, svc.RoomCount);
    }

    [Fact]
    public void Version_id_is_stable_and_has_the_protocol_number()
    {
        Assert.StartsWith($"p{ProtocolInfo.Number}-", ProtocolInfo.Version);
        Assert.Equal(ProtocolInfo.Version, ProtocolInfo.Version);
    }
}
