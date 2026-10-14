using GwentAI;
using GwentCore;
using GwentNet;

namespace GwentClient;

public static partial class UiSelfTest
{
    private static System.Windows.Controls.Primitives.ToggleButton FindToggle(System.Windows.FrameworkElement view, string tag) =>
        FindAll<System.Windows.Controls.Primitives.ToggleButton>(view, t => (string?)t.Tag == tag).First();

    /// <summary>
    /// Turnuva çeşitleri arayüzden: host kurallar bölümünde "Tek fraksiyon" ve "Sabit deste"yi tıklar; oyuncular uyar, herkes hazır olur,
    /// turnuva oynanır; oynanan tüm maçlarda kurala uyulduğu doğrulanır. <c>--selftest-tournament-rules</c>
    /// </summary>
    public static int RunTournamentRulesCheck()
    {
        var failures = 0;
        foreach (var kind in new[] { TournamentRuleKind.Faction, TournamentRuleKind.FixedDeck })
        {
            List<OnlineController>? all = null;
            try
            {
                all = OpenTournament(4, 4, 0, ready: false);
                var links = all.Select(c => c.Link).ToArray();
                var lobbies = all.Select(c => new TournamentLobbyView(c, () => Task.CompletedTask)).ToList();
                var host = lobbies[0];
                Layout(host);
                PumpUntil(() => all[0].Link.TState?.Rules != null, "kurallar", links);

                if (kind == TournamentRuleKind.Faction)
                {
                    Raise(FindToggle(host, "trule:Faction"), System.Windows.Controls.Primitives.ToggleButton.ClickEvent);
                    PumpUntil(() => all[0].Link.TState!.Rules!.Kind == TournamentRuleKind.Faction, "tek fraksiyon kuralı", links);
                    Layout(host);
                    Raise(FindToggle(host, "trule-faction:skellige"), System.Windows.Controls.Primitives.ToggleButton.ClickEvent);
                    PumpUntil(() => all[0].Link.TState!.Rules!.Faction == "skellige", "fraksiyon seçimi", links);
                    // Seçimi uymayan oyuncular yeniden seçer (arayüz otomatik gönderir); sunucu başka fraksiyonu reddeder
                    PumpUntil(() => all.All(c => c.Link.TState!.Players[c.Link.TState.YourIndex].Loadout?.Faction == "skellige"), "oyuncular skellige seçti", links);
                    var bad = Await(() => all[1].Link.SetTournamentLoadoutAsync(new LoadoutDto("monsters", CardCatalog.Default.Leaders("monsters").First(l => l.Edition != "nextgen").Id)));
                    if (bad.Ok) throw new InvalidOperationException("Yanlış fraksiyon kabul edildi.");
                }
                else
                {
                    Raise(FindToggle(host, "trule:FixedDeck"), System.Windows.Controls.Primitives.ToggleButton.ClickEvent);
                    PumpUntil(() => all[0].Link.TState!.Rules!.Kind == TournamentRuleKind.FixedDeck, "sabit deste kuralı", links);
                    PumpUntil(() => all.All(c => c.Link.TState!.Players[c.Link.TState.YourIndex].Loadout?.CardIds != null), "herkese sabit deste", links);
                    // izleyici elleri ayarı
                    Layout(host);
                    Raise(FindToggle(host, "trule-hands"), System.Windows.Controls.Primitives.ToggleButton.ClickEvent);
                    PumpUntil(() => !all[0].Link.TState!.Rules!.SpectatorHands, "izleyici elleri kapandı", links);
                }

                foreach (var v in lobbies)
                {
                    Layout(v);
                    Click(FindAll<System.Windows.Controls.Button>(v, b => (string?)b.Tag == "tlobby-ready").First());
                }
                PumpUntil(() => all.All(c => c.Link.TState!.Players.All(p => p.Ready)), "herkes hazır", links);
                Layout(host);
                Click(FindAll<System.Windows.Controls.Button>(host, b => (string?)b.Tag == "tlobby-start").First());
                PumpUntil(() => all.All(c => c.Link.TState!.Phase == TournamentPhase.Running), "başladı", links);
                var setups = new List<MatchSetup>();
                PlayTournament(all, 120, null, s => setups.Add(s));
                if (setups.Count == 0) throw new InvalidOperationException("Hiç maç kurulumu görülmedi.");
                foreach (var s in setups)
                {
                    if (kind == TournamentRuleKind.Faction && (s.Seat0.Faction != "skellige" || s.Seat1.Faction != "skellige")) throw new InvalidOperationException("Tek fraksiyon kuralı çiğnendi.");
                    if (kind == TournamentRuleKind.FixedDeck && !(s.Seat0.CardIds!.SequenceEqual(s.Seat1.CardIds!) && s.Seat0.LeaderId == s.Seat1.LeaderId)) throw new InvalidOperationException("Sabit deste kuralı çiğnendi.");
                }
                Console.WriteLine($"  {kind} turnuvası tamam: {setups.Count} maç kurulumu kurala uygun.");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] {kind}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally { if (all != null) CloseAll(all); }
        }
        Console.WriteLine($"Turnuva çeşitleri: {failures} hata.");
        return failures;
    }

    /// <summary>
    /// Canlı izleme: iki oyuncu maça girer, maçta olmayan bir oyuncu bracket'teki "İzle" düğmesine tıklar; izleyici oturumu sunucuyla aynı
    /// bitmeli. El gizleme ayarı izleyiciye ulaşmalı. <c>--selftest-spectate</c>
    /// </summary>
    public static int RunSpectateCheck()
    {
        var failures = 0;
        foreach (var hands in new[] { true, false })
        {
            List<OnlineController>? all = null;
            try
            {
                all = OpenTournament(4, 4, 0, ready: false);
                var links = all.Select(c => c.Link).ToArray();
                Await(() => all[0].Link.SetTournamentRulesAsync(new TournamentRules(SpectatorHands: hands)));
                foreach (var c in all) Await(() => c.Link.TournamentReadyAsync(true));
                PumpUntil(() => all.All(c => c.Link.TState!.Players.All(p => p.Ready)), "hazır", links);
                if (!Await(() => all[0].Link.StartTournamentAsync()).Ok) throw new InvalidOperationException("Başlatılamadı.");
                PumpUntil(() => all.All(c => c.Link.TState!.Phase == TournamentPhase.Running), "başladı", links);

                var m = all[0].Link.TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
                var a = all[m.A!.Value];
                var b = all[m.B!.Value];
                var watcher = all.First(c => c != a && c != b);
                var brackets = all.Select(c => new BracketView(c, () => Task.CompletedTask, () => { })).ToList();
                Await(() => a.Link.TournamentReadyAsync(true));
                Await(() => b.Link.TournamentReadyAsync(true));
                PumpUntil(() => watcher.Link.TState!.Matches.First(x => x.Round == 0 && x.Index == 0).Status == "playing", "maç oynanıyor", links);

                // izleyici bracket'te "İzle" düğmesini bulup tıklar
                var view = brackets[all.IndexOf(watcher)];
                Layout(view);
                var watch = FindAll<System.Windows.Controls.Button>(view, x => (string?)x.Tag == "bracket-watch:0:0").FirstOrDefault();
                if (watch == null) throw new InvalidOperationException("'İzle' düğmesi yok.");
                Click(watch);
                PumpUntil(() => watcher.Link.Spectate != null, "izleme kurulumu", links);
                var spec = watcher.Link.Spectate!;
                if (spec.ShowHands != hands) throw new InvalidOperationException("El görünürlüğü ayarı izleyiciye ulaşmadı.");
                if (!spec.IsSpectator || !Equals(spec.Apply(new Pass(0)).Ok, false)) throw new InvalidOperationException("İzleyici hamle yapabiliyor.");

                // maç oynanır; izleyici oturumu akışı uygular
                var agents = new[] { AgentFactory.Create(Difficulty.Hard, 11), AgentFactory.Create(Difficulty.Hard, 12) };
                var duel = new[] { a, b };
                var until = DateTime.UtcNow.AddSeconds(60);
                var rematch = new HashSet<GameSession>();
                while (!(a.Link.Match!.IsOver && b.Link.Match!.IsOver))
                {
                    if (DateTime.UtcNow > until) throw new TimeoutException("Maç bitmedi.");
                    for (var i = 0; i < 2; i++)
                    {
                        var link = duel[i].Link;
                        link.Pump();
                        var s = link.Match!;
                        if (s.RemoteQueued > 0) { s.ApplyNextRemote(); continue; }
                        if (s.IsOver) { if (s.WinnerSeat == null && rematch.Add(s)) link.RequestRematch(); continue; }
                        if (s.Game.PlayersToAct().Contains(s.MySeat) && !s.AwaitingEcho) s.Apply(agents[i].Decide(s.Game, s.MySeat));
                    }
                    watcher.Link.Pump();
                    var sp = watcher.Link.Spectate!;
                    while (sp.RemoteQueued > 0) sp.ApplyNextRemote();
                    Thread.Sleep(2);
                    if (a.Link.Match!.IsOver && a.Link.Match.WinnerSeat == null && !rematch.Contains(a.Link.Match)) continue;
                }
                PumpUntil(() => watcher.Link.Spectate!.IsOver || watcher.Link.Spectate.RemoteQueued > 0, "izleyici sonu", links);
                var sess = watcher.Link.Spectate!;
                while (sess.RemoteQueued > 0) sess.ApplyNextRemote();
                var winnerPlayers = a.Link.Match!.WinnerSeat;
                if (!sess.IsOver) throw new InvalidOperationException("İzleyici maçın bittiğini görmedi.");
                if (sess.Game.Events.Count != a.Link.Match.Game.Events.Count) throw new InvalidOperationException("İzleyici kopyası oyuncularınkinden ayrıştı.");
                Await(() => watcher.Link.StopWatchingAsync());
                Console.WriteLine($"  izleme tamam (eller {(hands ? "açık" : "gizli")}): {sess.Game.Events.Count} olay, kopya aynı.");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] izleme (eller {(hands ? "açık" : "gizli")}): {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally { if (all != null) CloseAll(all); }
        }
        Console.WriteLine($"İzleme: {failures} hata.");
        return failures;
    }

    /// <summary>İki oyuncunun oturumunu verilen sayıda tur oynatır (ekran görüntüsü için maçı yarıya getirir).</summary>
    internal static void StepDuel(OnlineController a, OnlineController b, int rounds)
    {
        var agents = new[] { AgentFactory.Create(Difficulty.Hard, 21), AgentFactory.Create(Difficulty.Hard, 22) };
        var duel = new[] { a, b };
        for (var n = 0; n < rounds; n++)
            for (var i = 0; i < 2; i++)
            {
                var link = duel[i].Link;
                link.Pump();
                var s = link.Match;
                if (s == null || s.IsOver) continue;
                if (s.RemoteQueued > 0) { s.ApplyNextRemote(); continue; }
                if (s.Game.PlayersToAct().Contains(s.MySeat) && !s.AwaitingEcho) s.Apply(agents[i].Decide(s.Game, s.MySeat));
                Thread.Sleep(3);
            }
        a.Link.Pump(); b.Link.Pump();
    }
}
