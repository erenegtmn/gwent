using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Turnuvada canlı izleme: kurulum + geçmiş + canlı eylemler, izleme kuralları, izleyici sayısı, el görünürlüğü ayarı.</summary>
public class TournamentSpectateTests
{
    private static LoadoutDto Pick(int i)
    {
        var f = Rooms.Factions[i % Rooms.Factions.Length];
        return new LoadoutDto(f, CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id);
    }

    /// <summary>4 kişilik turnuva başlamış; ilk yarı finaldeki iki oyuncu hazır ve maç oynanıyor. Döner: (servis, tüm istemciler, a, b, izleyebilecek biri).</summary>
    private static (RoomService Svc, List<FakeClient> All, FakeClient A, FakeClient B, FakeClient Other, TMatchInfo Match) Playing(bool hands = true, int seed = 3)
    {
        var next = seed;
        var svc = new RoomService(null, () => next++);
        var all = Enumerable.Range(0, 4).Select(i => new FakeClient(svc, "Oyuncu" + i, Difficulty.Hard, seed + i)).ToList();
        var code = svc.CreateTournament(all[0].Session, all[0].Nick, ProtocolInfo.Version, false, 4, 0).Value!;
        for (var i = 1; i < 4; i++) svc.JoinTournament(all[i].Session, code, all[i].Nick, ProtocolInfo.Version);
        Assert.True(svc.SetTournamentRules(all[0].Session, new TournamentRules(SpectatorHands: hands)).Ok);
        for (var i = 0; i < 4; i++) { svc.SetTournamentLoadout(all[i].Session, Pick(i)); svc.TournamentSetReady(all[i].Session, true); }
        Assert.True(svc.StartTournament(all[0].Session).Ok);
        FakeClient.Pump(all.ToArray());
        var m = all[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var a = all[m.A!.Value];
        var b = all[m.B!.Value];
        var other = all.First(c => c != a && c != b);
        svc.TournamentSetReady(a.Session, true);
        svc.TournamentSetReady(b.Session, true);
        FakeClient.Pump(all.ToArray());
        return (svc, all, a, b, other, m);
    }

    [Fact]
    public void A_spectator_gets_the_setup_the_history_and_then_live_actions()
    {
        var (svc, all, a, b, other, m) = Playing();
        for (var i = 0; i < 8; i++) { FakeClient.Pump(all.ToArray()); a.TryPlay(); b.TryPlay(); }
        FakeClient.Pump(all.ToArray());
        Assert.True(svc.WatchMatch(other.Session, m.Round, m.Index).Ok);
        other.Pump();
        Assert.NotNull(other.Spectating);
        Assert.Equal(all[m.A!.Value].Nick, other.Spectating!.Setup.Nick0);
        Assert.True(other.SpecSeq > 0);                                                   // geçmiş eylemler geldi
        var seqAtJoin = other.SpecSeq;
        for (var i = 0; i < 8; i++) { FakeClient.Pump(all.ToArray()); a.TryPlay(); b.TryPlay(); }
        FakeClient.Pump(all.ToArray());
        Assert.True(other.SpecSeq > seqAtJoin);                                           // canlı eylemler akıyor
        Assert.Equal(a.Local!.Events.Count, other.SpecGame!.Events.Count);                // izleyici kopyası oyuncularınkiyle aynı
    }

    [Fact]
    public void Spectators_see_the_end_of_the_match()
    {
        var (svc, all, a, b, other, m) = Playing();
        Assert.True(svc.WatchMatch(other.Session, m.Round, m.Index).Ok);
        FakeClient.RunToEnd(600, a, b);
        FakeClient.Pump(all.ToArray());
        Assert.NotNull(other.SpecResult);
        Assert.Equal(a.Result!.WinnerSeat, other.SpecResult!.WinnerSeat);
    }

    [Fact]
    public void The_hands_setting_reaches_the_spectator()
    {
        var shown = Playing(hands: true, seed: 11);
        Assert.True(shown.Svc.WatchMatch(shown.Other.Session, shown.Match.Round, shown.Match.Index).Ok);
        shown.Other.Pump();
        Assert.True(shown.Other.Spectating!.ShowHands);

        var hidden = Playing(hands: false, seed: 12);
        Assert.True(hidden.Svc.WatchMatch(hidden.Other.Session, hidden.Match.Round, hidden.Match.Index).Ok);
        hidden.Other.Pump();
        Assert.False(hidden.Other.Spectating!.ShowHands);
    }

    [Fact]
    public void Watching_rules_are_enforced()
    {
        var (svc, all, a, b, other, m) = Playing();
        Assert.False(svc.WatchMatch(a.Session, m.Round, m.Index).Ok);                     // kendi maçı sürerken izleyemez
        Assert.False(svc.WatchMatch(other.Session, 0, 5).Ok);                              // böyle eşleşme yok
        Assert.False(svc.WatchMatch(other.Session, 1, 0).Ok);                              // final henüz oynanmıyor
        var outsider = new FakeClient(svc, "Dis");
        Assert.False(svc.WatchMatch(outsider.Session, m.Round, m.Index).Ok);               // turnuvada olmayan izleyemez
        Assert.True(svc.WatchMatch(other.Session, m.Round, m.Index).Ok);
        Assert.True(svc.StopWatching(other.Session).Ok);
        Assert.True(svc.StopWatching(other.Session).Ok);                                   // tekrarı zararsız
    }

    [Fact]
    public void The_spectator_count_is_public_and_drops_when_someone_leaves_or_disconnects()
    {
        var (svc, all, a, b, other, m) = Playing();
        var third = all.First(c => c != a && c != b && c != other);
        Assert.True(svc.WatchMatch(other.Session, m.Round, m.Index).Ok);
        Assert.True(svc.WatchMatch(third.Session, m.Round, m.Index).Ok);
        FakeClient.Pump(all.ToArray());
        Assert.Equal(2, a.TState!.Matches.First(x => x.Round == m.Round && x.Index == m.Index).Live!.Spectators);
        third.Disconnect();
        other.Disconnect();
        FakeClient.Pump(a, b);
        Assert.Equal(0, a.TState!.Matches.First(x => x.Round == m.Round && x.Index == m.Index).Live!.Spectators);
    }
}
