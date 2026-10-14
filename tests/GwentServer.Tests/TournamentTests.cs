using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

public class TournamentTests
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private static LoadoutDto Loadout(int i)
    {
        var f = Factions[i % Factions.Length];
        return new LoadoutDto(f, CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id);
    }

    /// <summary>n oyunculu turnuva: host kurar, diğerleri katılır, herkes deste seçip hazır olur.</summary>
    private static (RoomService Svc, List<FakeClient> Clients, string Code) Lobby(int n, int seed = 1, int max = 8, ManualClock? clock = null)
    {
        var next = seed;
        var svc = new RoomService(clock, () => next++);   // her maç farklı tohum: berabere biten eşleşmenin rövanşı aynı oyunu tekrarlamasın
        var clients = Enumerable.Range(0, n).Select(i => new FakeClient(svc, "Oyuncu" + i, Difficulty.Hard, seed + i)).ToList();
        var created = svc.CreateTournament(clients[0].Session, clients[0].Nick, ProtocolInfo.Version, false, max, 0);
        Assert.True(created.Ok, created.Error);
        var code = created.Value!;
        for (var i = 1; i < n; i++) Assert.True(svc.JoinTournament(clients[i].Session, code, clients[i].Nick, ProtocolInfo.Version).Ok);
        for (var i = 0; i < n; i++)
        {
            Assert.True(svc.SetTournamentLoadout(clients[i].Session, Loadout(i)).Ok);
            Assert.True(svc.TournamentSetReady(clients[i].Session, true).Ok);
        }
        FakeClient.Pump(clients.ToArray());
        return (svc, clients, code);
    }

    /// <summary>Turnuva bitene dek: hazır olmayanları hazırlar, sırası gelenleri oynatır, berabere kalanlara rövanş ister.</summary>
    private static void RunTournament(RoomService svc, List<FakeClient> clients, int maxSteps = 20000)
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
                    var mineReady = m.A == ts.YourIndex ? m.ReadyA : m.ReadyB;
                    if (m.Status == "ready" && !mineReady) svc.TournamentSetReady(c.Session, true);
                }
                if (c.Result is { WinnerSeat: null } && ts?.Phase == TournamentPhase.Running) svc.RequestRematch(c.Session);
                c.TryPlay();
            }
        }
        throw new InvalidOperationException("Turnuva beklenen sürede bitmedi.");
    }

    private static void AssertConsistentEnd(List<FakeClient> clients, int expectedMatches)
    {
        var st = clients[0].TState!;
        Assert.Equal(TournamentPhase.Finished, st.Phase);
        Assert.Single(st.Players, p => p.Placement == 1);                                   // tek şampiyon
        Assert.Single(st.Players, p => p.Placement == 2);                                   // tek finalist
        Assert.Equal(expectedMatches, st.Matches.Count(m => m.Winner != null && m.A != null && m.B != null));
        Assert.All(st.Matches, m => Assert.Equal("done", m.Status));
        Assert.True(st.Players.All(p => p.Placement > 0));
        foreach (var c in clients) Assert.Equal(st.Matches.Select(m => m.Winner), c.TState!.Matches.Select(m => m.Winner));   // herkes aynı bracket'i görür
    }

    [Fact]
    public void A_four_player_tournament_plays_to_a_single_champion()
    {
        var (svc, clients, _) = Lobby(4, seed: 11, max: 4);
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        FakeClient.Pump(clients.ToArray());
        Assert.All(clients, c => Assert.Equal(TournamentPhase.Running, c.TState!.Phase));
        Assert.Equal(3, clients[0].TState!.Matches.Count);                                  // 2 yarı final + final
        RunTournament(svc, clients);
        AssertConsistentEnd(clients, expectedMatches: 3);
    }

    [Fact]
    public void Three_players_get_a_bye_and_the_bye_player_waits_in_the_final()
    {
        var (svc, clients, _) = Lobby(3, seed: 21, max: 4);
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        FakeClient.Pump(clients.ToArray());
        var st = clients[0].TState!;
        Assert.Single(st.Matches, m => m.Round == 0 && m.B == null && m.Winner != null);     // bye otomatik ilerlemiş
        RunTournament(svc, clients);
        AssertConsistentEnd(clients, expectedMatches: 2);                                   // bye sayılmaz: 1 yarı final + final
    }

    [Fact]
    public void An_eight_player_tournament_completes_with_seven_matches()
    {
        var (svc, clients, _) = Lobby(8, seed: 31, max: 8);
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        RunTournament(svc, clients);
        AssertConsistentEnd(clients, expectedMatches: 7);
        Assert.Equal(2, clients[0].TState!.Players.Count(p => p.Placement == 3));           // iki yarı finalist
        Assert.Equal(4, clients[0].TState!.Players.Count(p => p.Placement == 5));           // dört çeyrek finalist
    }

    [Fact]
    public void Lobby_rules_are_enforced()
    {
        var svc = new RoomService(null, () => 5);
        var a = new FakeClient(svc, "Geralt");
        var b = new FakeClient(svc, "Yennefer");
        var c = new FakeClient(svc, "Triss");
        Assert.False(svc.CreateTournament(a.Session, "Geralt", ProtocolInfo.Version, false, 6, 0).Ok);       // yalnız 4 ya da 8
        Assert.False(svc.CreateTournament(a.Session, "Geralt", ProtocolInfo.Version, false, 4, -5).Ok);      // ücret
        Assert.False(svc.CreateTournament(a.Session, "Geralt", "eski-surum", false, 4, 0).Ok);
        var code = svc.CreateTournament(a.Session, "Geralt", ProtocolInfo.Version, false, 4, 100).Value!;
        Assert.False(svc.JoinTournament(b.Session, code, "geralt", ProtocolInfo.Version).Ok);                // aynı takma ad
        Assert.False(svc.JoinTournament(b.Session, "ZZZZZ", "Yennefer", ProtocolInfo.Version).Ok);
        Assert.True(svc.JoinTournament(b.Session, code, "Yennefer", ProtocolInfo.Version).Ok);
        Assert.False(svc.StartTournament(a.Session).Ok);                                                     // 2 oyuncu: yetmez
        Assert.True(svc.JoinTournament(c.Session, code, "Triss", ProtocolInfo.Version).Ok);
        Assert.False(svc.StartTournament(a.Session).Ok);                                                     // hazır değiller
        Assert.False(svc.TournamentSetReady(a.Session, true).Ok);                                            // deste seçmeden hazır olunmaz
        Assert.False(svc.SetTournamentLoadout(a.Session, new LoadoutDto("yok", "yok")).Ok);
        foreach (var (x, i) in new[] { (a, 0), (b, 1), (c, 2) })
        {
            Assert.True(svc.SetTournamentLoadout(x.Session, Loadout(i)).Ok);
            Assert.True(svc.TournamentSetReady(x.Session, true).Ok);
        }
        Assert.False(svc.StartTournament(b.Session).Ok);                                                     // yalnız kuran başlatır
        Assert.True(svc.StartTournament(a.Session).Ok);
        Assert.False(svc.SetTournamentLoadout(a.Session, Loadout(3)).Ok);                                    // deste kilitli
        var d = new FakeClient(svc, "Ciri");
        Assert.False(svc.JoinTournament(d.Session, code, "Ciri", ProtocolInfo.Version).Ok);                  // başladıktan sonra katılınmaz
        FakeClient.Pump(a, b, c);
        Assert.Equal(100, a.TState!.EntryFee);
    }

    [Fact]
    public void Loadouts_stay_hidden_from_others_until_the_tournament_starts()
    {
        var (svc, clients, _) = Lobby(4, seed: 41, max: 4);
        var view = clients[1].TState!;
        Assert.NotNull(view.Players[1].Loadout);          // kendi destem
        Assert.Null(view.Players[2].Loadout);             // başkasınınki gizli
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        Assert.All(clients[1].TState!.Players, p => Assert.NotNull(p.Loadout));
    }

    [Fact]
    public void Leaving_the_lobby_frees_the_slot_and_the_host_leaving_closes_the_tournament()
    {
        var (svc, clients, code) = Lobby(3, seed: 51, max: 4);
        svc.Leave(clients[1].Session);
        FakeClient.Pump(clients.ToArray());
        Assert.Equal(2, clients[0].TState!.Players.Count);
        Assert.Equal(1, clients[2].TState!.YourIndex);                                  // indeksler kayar
        svc.Leave(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        Assert.Contains("tournament_closed", clients[2].Notices);
        Assert.Null(svc.FindTournament(code));
    }

    [Fact]
    public void A_player_who_leaves_mid_tournament_forfeits_and_the_opponent_advances()
    {
        var (svc, clients, _) = Lobby(4, seed: 61, max: 4);
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var leaver = clients[m.A!.Value];
        var stayer = clients[m.B!.Value];
        svc.Leave(leaver.Session);                                                      // hiç oynamadan ayrılır
        FakeClient.Pump(clients.ToArray());
        var st = stayer.TState!;
        Assert.Equal(m.B, st.Matches.First(x => x.Round == 0 && x.Index == 0).Winner);
        Assert.True(st.Players[m.A.Value].Eliminated);
        var final = st.Matches.First(x => x.Round == 1);
        Assert.Equal(m.B, final.A);                                                     // finalde yerini aldı
    }

    [Fact]
    public void Leaving_during_a_running_match_resigns_it_and_the_bracket_moves_on()
    {
        var (svc, clients, _) = Lobby(4, seed: 71, max: 4);
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var a = clients[m.A!.Value];
        var b = clients[m.B!.Value];
        Assert.True(svc.TournamentSetReady(a.Session, true).Ok);
        Assert.True(svc.TournamentSetReady(b.Session, true).Ok);
        FakeClient.Pump(clients.ToArray());
        Assert.Equal("playing", a.TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Status);
        svc.Leave(a.Session);                                                           // maç sürerken ayrılır → teslim
        FakeClient.Pump(clients.ToArray());
        Assert.Equal(m.B, b.TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Winner);
        Assert.Equal("resigned", b.Result!.Reason);
    }

    [Fact]
    public void A_walkover_needs_the_opponent_to_be_gone_for_the_grace_period()
    {
        var clock = new ManualClock();
        var (svc, clients, _) = Lobby(4, seed: 81, max: 4, clock: clock);
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var a = clients[m.A!.Value];
        var b = clients[m.B!.Value];
        Assert.False(svc.TournamentClaimWalkover(a.Session).Ok);                        // rakip bağlı
        b.Disconnect();
        Assert.False(svc.TournamentClaimWalkover(a.Session).Ok);                        // süre dolmadı
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True(svc.TournamentClaimWalkover(a.Session).Ok);
        FakeClient.Pump(clients.ToArray());
        Assert.Equal(m.A, a.TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Winner);
    }

    [Fact]
    public void A_disconnected_player_returns_with_the_token_and_sees_the_bracket()
    {
        var (svc, clients, code) = Lobby(4, seed: 91, max: 4);
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var token = clients[2].TState!.YourToken;
        clients[2].Disconnect();
        FakeClient.Pump(clients.ToArray());
        Assert.False(clients[0].TState!.Players[2].Connected);
        var back = new FakeClient(svc, "Oyuncu2");
        Assert.False(svc.RejoinTournament(back.Session, code, "YANLIS").Ok);
        Assert.True(svc.RejoinTournament(back.Session, code, token).Ok);
        FakeClient.Pump(clients.ToArray());
        back.Pump();
        Assert.Equal(2, back.TState!.YourIndex);
        Assert.True(clients[0].TState!.Players[2].Connected);
    }

    [Fact]
    public void A_whole_bracket_is_decided_even_when_matches_end_in_draws()
    {
        // Berabere biten eşleşme galip sayılmaz: oda Finished kalır, iki taraf rövanş isteyince yeniden başlar; sürücü beraberlikte rövanş ister
        var (svc, clients, _) = Lobby(4, seed: 101, max: 4);
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        RunTournament(svc, clients);
        AssertConsistentEnd(clients, 3);
    }

    [Theory]
    [InlineData(100, 4, 1, 280)]
    [InlineData(100, 4, 2, 120)]
    [InlineData(100, 4, 3, 0)]
    [InlineData(50, 8, 1, 220)]
    [InlineData(50, 8, 2, 100)]
    [InlineData(50, 8, 3, 40)]
    [InlineData(50, 8, 5, 0)]
    [InlineData(0, 8, 1, 0)]
    public void Prize_pool_is_shared_by_placement(int fee, int players, int placement, int expected) =>
        Assert.Equal(expected, TournamentPrizes.Prize(fee, players, placement));

    [Fact]
    public void Paid_out_prizes_never_exceed_the_pool()
    {
        foreach (var players in new[] { 3, 4, 5, 6, 7, 8 })
        {
            var pool = 70 * players;
            var paid = TournamentPrizes.Prize(70, players, 1) + TournamentPrizes.Prize(70, players, 2) + 2 * TournamentPrizes.Prize(70, players, 3);
            Assert.True(paid <= pool, $"{players} oyuncu: {paid} > {pool}");
        }
    }
}
