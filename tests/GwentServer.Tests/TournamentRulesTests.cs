using GwentAI;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Turnuva çeşitleri (kuralsız / tek fraksiyon / sabit deste) ve maçların canlı özeti.</summary>
public class TournamentRulesTests
{
    private static LoadoutDto Free(int i)
    {
        var f = Rooms.Factions[i % Rooms.Factions.Length];
        return new LoadoutDto(f, CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id);
    }

    private static (RoomService Svc, List<FakeClient> Clients, string Code) Open(int n, int seed = 5)
    {
        var next = seed;
        var svc = new RoomService(null, () => next++);
        var clients = Enumerable.Range(0, n).Select(i => new FakeClient(svc, "Oyuncu" + i, Difficulty.Hard, seed + i)).ToList();
        var code = svc.CreateTournament(clients[0].Session, clients[0].Nick, ProtocolInfo.Version, false, 4, 0).Value!;
        for (var i = 1; i < n; i++) Assert.True(svc.JoinTournament(clients[i].Session, code, clients[i].Nick, ProtocolInfo.Version).Ok);
        return (svc, clients, code);
    }

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
                    if (m.Status == "ready" && !(m.A == ts.YourIndex ? m.ReadyA : m.ReadyB)) svc.TournamentSetReady(c.Session, true);
                }
                if (c.Result is { WinnerSeat: null } && ts?.Phase == TournamentPhase.Running) svc.RequestRematch(c.Session);
                c.TryPlay();
            }
        }
        throw new InvalidOperationException("Turnuva beklenen sürede bitmedi.");
    }

    [Fact]
    public void Only_the_host_picks_the_rules_and_only_in_the_lobby()
    {
        var (svc, clients, _) = Open(3);
        var rules = new TournamentRules(TournamentRuleKind.Faction, "skellige");
        Assert.False(svc.SetTournamentRules(clients[1].Session, rules).Ok);                          // yalnız kuran
        Assert.True(svc.SetTournamentRules(clients[0].Session, rules).Ok);
        Assert.False(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.Faction, "yok")).Ok);
        Assert.False(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.FixedDeck)).Ok);   // deste şart
        Assert.False(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.FixedDeck, null, new LoadoutDto("skellige", "yok"))).Ok);
        FakeClient.Pump(clients.ToArray());
        Assert.Equal(TournamentRuleKind.Faction, clients[2].TState!.Rules!.Kind);                    // herkes görür
        for (var i = 0; i < 3; i++) { svc.SetTournamentLoadout(clients[i].Session, new LoadoutDto("skellige", CardCatalog.Default.Leaders("skellige").First(l => l.Edition != "nextgen").Id)); svc.TournamentSetReady(clients[i].Session, true); }
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        Assert.False(svc.SetTournamentRules(clients[0].Session, new TournamentRules()).Ok);          // başladıktan sonra değişmez
    }

    [Fact]
    public void A_faction_tournament_refuses_other_factions_and_clears_mismatched_picks()
    {
        var (svc, clients, _) = Open(4);
        for (var i = 0; i < 4; i++) { Assert.True(svc.SetTournamentLoadout(clients[i].Session, Free(i)).Ok); svc.TournamentSetReady(clients[i].Session, true); }
        Assert.True(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.Faction, "nilfgaard")).Ok);
        FakeClient.Pump(clients.ToArray());
        var st = clients[0].TState!;
        Assert.All(st.Players, p => Assert.False(p.Ready));                                          // kural değişince hazırlıklar sıfırlanır
        Assert.Equal("nilfgaard", st.Players[1].Loadout?.Faction ?? clients[1].TState!.Players[1].Loadout?.Faction);   // zaten nilfgaard olan kalır
        Assert.Null(clients[0].TState!.Players[0].Loadout);                                          // northern_realms seçimi silindi
        Assert.False(svc.SetTournamentLoadout(clients[0].Session, Free(0)).Ok);                      // yanlış fraksiyon reddedilir
        Assert.True(svc.SetTournamentLoadout(clients[0].Session, Free(1)).Ok);                       // nilfgaard olur
        Assert.False(svc.TournamentSetReady(clients[2].Session, true).Ok);                           // seçimi silinen hazır olamaz
    }

    [Fact]
    public void A_faction_tournament_plays_to_the_end_with_only_that_faction()
    {
        var (svc, clients, _) = Open(4, seed: 71);
        Assert.True(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.Faction, "monsters")).Ok);
        for (var i = 0; i < 4; i++)
        {
            var leader = CardCatalog.Default.Leaders("monsters").First(l => l.Edition != "nextgen" && l.Id.GetHashCode() != 0).Id;
            Assert.True(svc.SetTournamentLoadout(clients[i].Session, new LoadoutDto("monsters", leader)).Ok);
            Assert.True(svc.TournamentSetReady(clients[i].Session, true).Ok);
        }
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        RunTournament(svc, clients);
        Assert.All(clients.Where(c => c.Setup != null), c => Assert.Equal("monsters", c.Seat == 0 ? c.Setup!.Seat0.Faction : c.Setup!.Seat1.Faction));
        Assert.Single(clients[0].TState!.Players, p => p.Placement == 1);
    }

    [Fact]
    public void A_fixed_deck_tournament_puts_everyone_on_the_same_deck_and_ignores_picks()
    {
        var (svc, clients, _) = Open(4, seed: 81);
        var deck = new LoadoutDto("northern_realms", CardCatalog.Default.Leaders("northern_realms").First(l => l.Edition != "nextgen").Id);
        Assert.True(svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.FixedDeck, null, deck, SpectatorHands: false)).Ok);
        FakeClient.Pump(clients.ToArray());
        var rules = clients[3].TState!.Rules!;
        Assert.Equal(TournamentRuleKind.FixedDeck, rules.Kind);
        Assert.False(rules.SpectatorHands);
        Assert.NotNull(rules.Deck!.CardIds);                                                          // önerilen deste açık kart listesine çevrilir
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("northern_realms", clients[i].TState!.Players[i].Loadout!.Faction);          // kendi seçimi otomatik sabit deste
            Assert.True(svc.SetTournamentLoadout(clients[i].Session, Free(i + 1)).Ok);                // başka şey seçmeye çalışır: yok sayılır
            Assert.Equal("northern_realms", FakeClient.Peek(clients[i]).Players[i].Loadout!.Faction);
        }
        for (var i = 0; i < 4; i++) Assert.True(svc.TournamentSetReady(clients[i].Session, true).Ok);
        Assert.True(svc.StartTournament(clients[0].Session).Ok);
        RunTournament(svc, clients);
        // her maçta iki taraf da aynı kart listesiyle oynadı
        foreach (var c in clients.Where(c => c.Setup != null))
        {
            Assert.Equal(c.Setup!.Seat0.CardIds, c.Setup.Seat1.CardIds);
            Assert.Equal(rules.Deck.CardIds, c.Setup.Seat0.CardIds);
        }
    }

    [Fact]
    public void A_late_joiner_in_a_fixed_deck_tournament_gets_the_deck_immediately()
    {
        var (svc, clients, code) = Open(2, seed: 91);
        var deck = new LoadoutDto("scoiatael", CardCatalog.Default.Leaders("scoiatael").First(l => l.Edition != "nextgen").Id);
        svc.SetTournamentRules(clients[0].Session, new TournamentRules(TournamentRuleKind.FixedDeck, null, deck));
        var late = new FakeClient(svc, "Gec");
        Assert.True(svc.JoinTournament(late.Session, code, "Gec", ProtocolInfo.Version).Ok);
        late.Pump();
        Assert.Equal("scoiatael", late.TState!.Players[2].Loadout!.Faction);
        Assert.True(svc.TournamentSetReady(late.Session, true).Ok);                                   // seçim yapmadan hazır olabilir
    }

    [Fact]
    public void Matches_report_a_public_live_summary_without_hands()
    {
        var (svc, clients, _) = Open(4, seed: 101);
        for (var i = 0; i < 4; i++) { svc.SetTournamentLoadout(clients[i].Session, Free(i)); svc.TournamentSetReady(clients[i].Session, true); }
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        Assert.Null(m.Live);                                                                          // başlamadan özet yok
        var a = clients[m.A!.Value]; var b = clients[m.B!.Value];
        svc.TournamentSetReady(a.Session, true); svc.TournamentSetReady(b.Session, true);
        FakeClient.Pump(clients.ToArray());
        var live0 = clients[2].TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Live;        // maçta olmayan biri bile görür
        Assert.NotNull(live0);
        Assert.Equal(1, live0!.Round);
        for (var i = 0; i < 12; i++) { FakeClient.Pump(clients.ToArray()); a.TryPlay(); b.TryPlay(); }
        FakeClient.Pump(clients.ToArray());
        var live1 = clients[2].TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Live!;
        Assert.True(live1.TotalA + live1.TotalB > 0 || live1.Round > 1 || live1.RoundScores.Count > 0);   // maç ilerledi, özet güncellendi
        // maç bitince raund skorları kalır
        FakeClient.RunToEnd(600, a, b);
        FakeClient.Pump(clients.ToArray());
        var done = clients[2].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        Assert.Equal("done", done.Status);
        Assert.NotEmpty(done.Live!.RoundScores);
        Assert.Equal(2, Math.Max(done.Live.WinsA, done.Live.WinsB));
    }

    [Fact]
    public void A_drawn_match_stays_open_for_a_rematch_and_does_not_advance_the_bracket()
    {
        var (svc, clients, code) = Open(4, seed: 131);
        for (var i = 0; i < 4; i++) { svc.SetTournamentLoadout(clients[i].Session, Free(i)); svc.TournamentSetReady(clients[i].Session, true); }
        svc.StartTournament(clients[0].Session);
        FakeClient.Pump(clients.ToArray());
        var m = clients[0].TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
        var a = clients[m.A!.Value]; var b = clients[m.B!.Value];
        svc.TournamentSetReady(a.Session, true);
        svc.TournamentSetReady(b.Session, true);
        FakeClient.Pump(clients.ToArray());
        var room = svc.Find(code + "-11")!;
        Assert.Equal(RoomPhase.Playing, room.Phase);

        room.Finish(null, "normal");                                                  // berabere bitirilir
        FakeClient.Pump(clients.ToArray());
        var st = clients[2].TState!;
        Assert.Equal("draw", st.Matches.First(x => x.Round == 0 && x.Index == 0).Status);
        Assert.Null(st.Matches.First(x => x.Round == 0 && x.Index == 0).Winner);       // ilerleme yok
        Assert.False(svc.TournamentSetReady(a.Session, true).Ok);                      // "hazırım" yerine rövanş istenir
        Assert.True(svc.RequestRematch(a.Session).Ok);
        Assert.True(svc.RequestRematch(b.Session).Ok);                                 // ikisi de isteyince aynı odada yeniden başlar
        FakeClient.Pump(clients.ToArray());
        Assert.Equal(RoomPhase.Playing, room.Phase);
        Assert.Equal("playing", clients[2].TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Status);
    }

    [Fact]
    public void Resending_the_same_pick_keeps_the_ready_state_but_a_real_change_clears_it()
    {
        var (svc, clients, _) = Open(3, seed: 141);
        var pick = Free(1);
        Assert.True(svc.SetTournamentLoadout(clients[1].Session, pick).Ok);
        Assert.True(svc.TournamentSetReady(clients[1].Session, true).Ok);
        Assert.True(svc.SetTournamentLoadout(clients[1].Session, pick with { }).Ok);               // aynısı yeniden
        FakeClient.Pump(clients.ToArray());
        Assert.True(clients[1].TState!.Players[1].Ready);
        Assert.True(svc.SetTournamentLoadout(clients[1].Session, Free(2)).Ok);                     // gerçek değişiklik
        FakeClient.Pump(clients.ToArray());
        Assert.False(clients[1].TState!.Players[1].Ready);
    }
}
