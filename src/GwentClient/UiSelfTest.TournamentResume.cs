using GwentAI;
using GwentNet;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Turnuva uygulama kapanıp açılınca: 4 kişilik turnuva başlar, ilk yarı finalde birkaç hamle oynanır, hepsi bağlantıyı "kapatır" (teslim değil);
    /// host kayıttan turnuvayı yeniden kurar, oyuncular kendi anahtarlarıyla (yeni adres, aynı kod) geri döner, sürmekte olan maç kaldığı yerden devam eder
    /// ve turnuva şampiyona dek oynanır. <c>--selftest-tournament-resume</c>
    /// </summary>
    public static int RunTournamentResumeCheck()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-tresume-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        ResumeStore.DirOverride = dir;
        ResumeStore.Enabled = true;
        List<OnlineController>? all = null;
        List<OnlineController>? back = null;
        try
        {
            all = OpenTournament(4, 4, 0, ready: true);
            var links = all.Select(c => c.Link).ToArray();
            if (!Await(() => all[0].Link.StartTournamentAsync()).Ok) throw new InvalidOperationException("Turnuva başlatılamadı.");
            PumpUntil(() => all.All(c => c.Link.TState!.Phase == TournamentPhase.Running), "başladı", links);

            var m = all[0].Link.TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
            var a = all[m.A!.Value];
            var b = all[m.B!.Value];
            Await(() => a.Link.TournamentReadyAsync(true));
            Await(() => b.Link.TournamentReadyAsync(true));
            PumpUntil(() => a.Link.Match != null && b.Link.Match != null, "maç başladı", links);
            StepDuel(a, b, 16);
            if (a.Link.Match!.IsOver) throw new InvalidOperationException("Maç yarıda kalmadan bitti (deneme geçersiz).");

            // Kayıt diske yazıldı mı (oynanan maçın eylemleriyle)?
            TournamentSnapshot? snap = null;
            for (var i = 0; i < 150; i++)
            {
                snap = ResumeStore.LoadHostTournament();
                if (snap?.Matches.Any(x => x.Room is { Actions.Count: >= 8 }) == true) break;
                Thread.Sleep(20);
            }
            if (snap == null || !snap.Matches.Any(x => x.Room is { Actions.Count: >= 8 })) throw new InvalidOperationException("Turnuva kaydı yazılmadı.");
            var playing = snap.Matches.First(x => x.Room != null);
            var saved = playing.Room!.Actions.Count;
            var code = snap.Code;
            var tokens = all.Select(c => c.Link.TState!.YourToken).ToList();
            var nicks = snap.Players.Select(p => p.Nick).ToList();

            // "Uygulama kapanır": hiçbir ayrılma bildirilmez
            var toClose = all;
            Await(async () => { foreach (var c in toClose.AsEnumerable().Reverse()) await c.DisposeAsync(false); return 0; });
            all = null;
            if (ResumeStore.LoadHostTournament() == null) throw new InvalidOperationException("Kapanış turnuva kaydını sildi.");

            // Yeniden açılış: host kayıttan döner, herkes yeni adresle aynı koltuğa
            var opts = new HostOptions(false, false, false, null);
            var (hc, err) = Await(() => OnlineController.ResumeTournamentAsync(snap, opts, null));
            if (hc == null) throw new InvalidOperationException("Host turnuvayı sürdüremedi: " + err);
            back = new List<OnlineController> { hc };
            if (!hc.IsTournament) throw new InvalidOperationException("Host turnuva olarak açılmadı.");
            var port = hc.Server!.Port;
            for (var i = 1; i < 4; i++)
            {
                ResumeStore.SaveGuest(new GuestResume(code, tokens[i], nicks[i], nicks[0], DateTime.UtcNow, IsTournament: true));
                var nick = nicks[i];
                var (gc, gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{port}", code, nick));
                if (gc == null) throw new InvalidOperationException("Oyuncu geri dönemedi: " + gerr);
                if (!gc.IsTournament) throw new InvalidOperationException("Dönen oyuncu turnuva olarak bağlanmadı.");
                back.Add(gc);
            }
            var blinks = back.Select(c => c.Link).ToArray();
            PumpUntil(() => back.All(c => c.Link.TState is { Phase: TournamentPhase.Running } s && s.Players.All(p => p.Connected)), "herkes döndü", blinks);

            // Oynanan maçın iki oyuncusu geçmişi aldı; kopyaları sunucuyla aynı
            var duel = new[] { back[playing.A!.Value], back[playing.B!.Value] };
            PumpUntil(() => duel.All(c => c.Link.Match != null && c.Link.Match.RemoteQueued == saved), "maç geçmişi yeniden geldi", blinks);
            foreach (var c in duel) c.Link.Match!.FastForward();
            var room = hc.Server.Rooms.Find(playing.Room.Code)!;
            if (duel[0].Link.Match!.Game.Events.Count != room.Game!.Events.Count) throw new InvalidOperationException("Geçmiş uygulanınca olay sayısı farklı.");

            PlayTournament(back);
            var st = hc.Link.TState!;
            if (st.Phase != TournamentPhase.Finished || st.Players.Count(p => p.Placement == 1) != 1) throw new InvalidOperationException("Turnuva şampiyonla bitmedi.");
            for (var i = 0; i < 100 && ResumeStore.LoadHostTournament() != null; i++) Thread.Sleep(20);
            if (ResumeStore.LoadHostTournament() != null) throw new InvalidOperationException("Turnuva bitince kayıt silinmedi.");
            Console.WriteLine($"Turnuva sürdürüldü: kapanıştan sonra {saved} hamlelik maç kaldığı yerden bitti, şampiyon {st.Players.First(p => p.Placement == 1).Nick}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HATA] " + ex);
            return 1;
        }
        finally
        {
            if (all != null) CloseAll(all);
            if (back != null) CloseAll(back);
            ResumeStore.ClearGuest();
            ResumeStore.Enabled = false;
            ResumeStore.DirOverride = null;
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }
}
