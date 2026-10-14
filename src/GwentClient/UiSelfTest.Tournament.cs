using GwentAI;
using GwentCore;
using GwentNet;

namespace GwentClient;

public static partial class UiSelfTest
{
    private static readonly string[] TFactions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    /// <summary>
    /// Gerçek sunucuda turnuva açar: host + (players-1) konuk bağlanır, herkes deste seçer. ready=true ise hepsi hazır olur.
    /// Dönen ilk öğe host'tur; hepsi aynı gömülü sunucuya bağlıdır.
    /// </summary>
    internal static List<OnlineController> OpenTournament(int players, int size, int fee, bool ready)
    {
        var (hc, err) = Await(() => OnlineController.HostTournamentAsync("Oyuncu0", new HostOptions(false, false, false, null), size, fee, null));
        if (hc == null) throw new InvalidOperationException("Turnuva kurulamadı: " + err);
        var all = new List<OnlineController> { hc };
        PumpUntil(() => hc.Link.TState?.Code is { Length: 5 }, "turnuva durumu", hc.Link);
        var code = hc.Link.TState!.Code;
        for (var i = 1; i < players; i++)
        {
            var nick = "Oyuncu" + i;
            var (gc, gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{hc.Server!.Port}", code, nick));
            if (gc == null) throw new InvalidOperationException("Turnuvaya katılınamadı: " + gerr);
            if (!gc.IsTournament) throw new InvalidOperationException("Kod turnuva olarak çözülmedi.");
            all.Add(gc);
        }
        PumpUntil(() => all.All(c => c.Link.TState is { } s && s.Players.Count == players), "oyuncular göründü", all.Select(c => c.Link).ToArray());
        for (var i = 0; i < all.Count; i++)
        {
            var f = TFactions[i % TFactions.Length];
            var leader = CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id;
            var link = all[i].Link;
            var lo = new LoadoutDto(f, leader, null, i % 2 == 0 ? Portraits.Default : leader);
            var r = Await(() => link.SetTournamentLoadoutAsync(lo));
            if (!r.Ok) throw new InvalidOperationException("Deste seçilemedi: " + r.Error);
            if (ready && !Await(() => link.TournamentReadyAsync(true)).Ok) throw new InvalidOperationException("Hazır olunamadı.");
        }
        PumpUntil(() => all.All(c => c.Link.TState!.Players[c.Link.TState.YourIndex].Loadout != null && c.Link.TState.Players.All(p => !ready || p.Ready)), "seçimler ve hazır durumları", all.Select(c => c.Link).ToArray());
        return all;
    }

    internal static void CloseAll(IEnumerable<OnlineController> controllers)
    {
        var list = controllers.Reverse().ToList();
        Await(async () => { foreach (var c in list) await c.DisposeAsync(); return 0; });
    }

    /// <summary>
    /// Turnuva uçtan uca (gerçek ağ): 4 ve 3 (bye) kişilik turnuva baştan sona oynanır; her oyuncunun oturumu kendi oyununu ajanla oynar,
    /// beraberlikte rövanş istenir. Şampiyon, sıralamalar ve iki kopyanın sunucuyla aynı bitmesi doğrulanır. <c>--selftest-tournament</c>
    /// </summary>
    public static int RunTournamentCheck(int rounds)
    {
        var failures = 0;
        for (var i = 0; i < rounds; i++)
        {
            foreach (var players in new[] { 4, 3 })
            {
                List<OnlineController>? all = null;
                try
                {
                    all = OpenTournament(players, 4, 0, ready: true);
                    var host = all[0].Link;
                    if (!Await(() => host.StartTournamentAsync()).Ok) throw new InvalidOperationException("Turnuva başlatılamadı.");
                    PlayTournament(all);
                    var st = host.TState!;
                    if (st.Phase != TournamentPhase.Finished) throw new InvalidOperationException("Turnuva bitmedi.");
                    if (st.Players.Count(p => p.Placement == 1) != 1 || st.Players.Count(p => p.Placement == 2) != 1) throw new InvalidOperationException("Şampiyon/finalist sayısı yanlış.");
                    foreach (var c in all)
                        if (!c.Link.TState!.Matches.Select(m => m.Winner).SequenceEqual(st.Matches.Select(m => m.Winner))) throw new InvalidOperationException("Bracket'ler ayrıştı.");
                    Console.WriteLine($"  turnuva ({players} kişi) tamam: şampiyon {st.Players.First(p => p.Placement == 1).Nick}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[HATA] turnuva {i + 1} ({players} kişi): {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
                finally { if (all != null) CloseAll(all); }
            }
        }
        Console.WriteLine($"Turnuva akışı: {rounds} tur, {failures} hata.");
        return failures;
    }

    /// <summary>Turnuva bitene dek: hazır olunur, her oturum kendi oyununu oynar (arayüz yok; oturum seviyesinde), beraberlikte rövanş istenir.</summary>
    internal static void PlayTournament(List<OnlineController> all, int timeoutSeconds = 120, Action<int>? readyHook = null, Action<MatchSetup>? onSetup = null)
    {
        var links = all.Select(c => c.Link).ToArray();
        var agents = all.Select((_, i) => AgentFactory.Create(Difficulty.Hard, 300 + i)).ToList();
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var rematchAsked = new HashSet<GameSession>();
        var seenSetups = new HashSet<GameSession>();
        while (!all.All(c => c.Link.TState?.Phase == TournamentPhase.Finished))
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Turnuva beklenen sürede bitmedi.");
            for (var i = 0; i < all.Count; i++)
            {
                var link = all[i].Link;
                link.Pump();
                var ts = link.TState;
                if (ts is { Phase: TournamentPhase.Running, YourMatchRound: { } r, YourMatchIndex: { } ix })
                {
                    var m = ts.Matches.First(x => x.Round == r && x.Index == ix);
                    var mineReady = m.A == ts.YourIndex ? m.ReadyA : m.ReadyB;
                    if (m.Status == "ready" && !mineReady)
                    {
                        if (readyHook != null) readyHook(i);   // arayüz düğmesine tıkla
                        else Await(() => link.TournamentReadyAsync(true));
                    }
                }
                var s = link.Match;
                if (s == null) continue;
                if (onSetup != null && s.OnlineSetup != null && seenSetups.Add(s)) onSetup(s.OnlineSetup);
                if (s.RemoteQueued > 0) { s.ApplyNextRemote(); continue; }
                if (s.IsOver)
                {
                    if (s.WinnerSeat == null && ts?.Phase == TournamentPhase.Running && rematchAsked.Add(s)) link.RequestRematch();
                    continue;
                }
                if (s.Game.PlayersToAct().Contains(s.MySeat) && !s.AwaitingEcho) s.Apply(agents[i].Decide(s.Game, s.MySeat));
            }
            Thread.Sleep(2);
        }
        foreach (var l in links) l.Pump();
    }

    /// <summary>
    /// Turnuva arayüzü: bekleme odası görünümlerinde "Hazırım" ve "Turnuvayı başlat" tıklanır, bracket görünümlerinde "Maça hazırım" tıklanır;
    /// maçlar oturum seviyesinde oynanır. <c>--selftest-tournament-ui</c>
    /// </summary>
    public static int RunTournamentUiCheck()
    {
        List<OnlineController>? all = null;
        try
        {
            all = OpenTournament(4, 4, 0, ready: false);
            var links = all.Select(c => c.Link).ToArray();
            var lobbies = all.Select(c => new TournamentLobbyView(c, () => Task.CompletedTask)).ToList();
            var clickInfo = new List<string>();
            foreach (var v in lobbies)
            {
                Layout(v);
                var ready = FindAll<System.Windows.Controls.Button>(v, b => (string?)b.Tag == "tlobby-ready").First();
                clickInfo.Add($"{ready.Content}/{(ready.IsEnabled ? "açık" : "PASİF")}");
                Click(ready);
            }
            try { PumpUntil(() => all.All(c => c.Link.TState!.Players.All(p => p.Ready)), "herkes hazır", links); }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Herkes hazır olmadı. Düğmeler: " + string.Join(", ", clickInfo) + " | durum: " + string.Join(", ", all.Select(c => string.Join("", c.Link.TState!.Players.Select(p => p.Ready ? "H" : "-")))));
            }
            Layout(lobbies[0]);
            var start = FindAll<System.Windows.Controls.Button>(lobbies[0], b => (string?)b.Tag == "tlobby-start").First();
            if (!start.IsEnabled) throw new InvalidOperationException("Herkes hazırken 'Turnuvayı başlat' pasif.");
            Click(start);
            PumpUntil(() => all.All(c => c.Link.TState!.Phase == TournamentPhase.Running), "turnuva başladı", links);
            var brackets = all.Select(c => new BracketView(c, () => Task.CompletedTask, () => { })).ToList();
            PlayTournament(all, 120, i =>
            {
                var v = brackets[i];
                Layout(v);
                var b = FindAll<System.Windows.Controls.Button>(v, x => (string?)x.Tag == "bracket-ready").FirstOrDefault();
                if (b is { IsEnabled: true }) Click(b);
            });
            Layout(brackets[0]);
            if (!FindAll<System.Windows.Controls.Button>(brackets[0], b => (string?)b.Tag == "bracket-menu").Any()) throw new InvalidOperationException("Biten turnuvada 'Menüye dön' düğmesi yok.");
            Console.WriteLine("Turnuva arayüz akışı tamam: bekleme odası, başlatma ve bracket tıklamaları çalıştı.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HATA] " + ex);
            return 1;
        }
        finally { if (all != null) CloseAll(all); }
    }
}
