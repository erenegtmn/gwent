using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Arayüzü rastgele bir "kullanıcı" gibi kullanır: görsel ağaçta kart ve düğmeleri bulup tıklama olaylarını
/// tetikler, oyunun takılmadan bitmesini ve hiçbir tıklamanın istisna fırlatmamasını denetler.
/// Kullanım: <c>Gwent.exe --selftest [oyunSayisi]</c>. Başarısızlıkta çıkış kodu 1.
/// </summary>
public static partial class UiSelfTest
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    public static int Run(int games)
    {
        var failures = RunDeckBuilder() + RunGuide() + RunUpdateCheck() + RunCurseCheck();
        var sfxProblems = Sfx.Diagnose();
        foreach (var p in sfxProblems) Console.WriteLine("[HATA] ses: " + p);
        Console.WriteLine($"Ses: {Sfx.AllNames.Length} yer tutucu denetlendi, {sfxProblems.Count} sorun.");
        failures += sfxProblems.Count;
        Console.WriteLine($"Varlık: arayüz görselleri {(Assets.Available ? "var" : "YOK (yer tutucu mod)")}, müzik {Assets.Music().Count} parça.");
        if (Assets.Available)
        {
            foreach (var n in new[] { "board", "badge", "gem", "laurel", "shield_nilfgaard", "deck_northern_realms", "msg_round_victory" })
                if (Assets.Ui(n) == null) { failures++; Console.WriteLine("[HATA] arayüz görseli eksik: " + n); }
            var missingArt = CardCatalog.Default.All.Where(c => Assets.Card(c.Id) == null).Select(c => c.Id).ToList();
            if (missingArt.Count > 0) { failures++; Console.WriteLine("[HATA] kart resmi eksik: " + string.Join(", ", missingArt)); }
        }
        var stats = new Dictionary<string, int>();
        for (var g = 1; g <= games; g++)
        {
            var rng = new Random(g * 31);
            var f0 = Factions[rng.Next(5)];
            var f1 = Factions[rng.Next(5)];
            var l0 = CardCatalog.Default.Leaders(f0).Where(l => l.Edition != "nextgen").ToList();
            var l1 = CardCatalog.Default.Leaders(f1).Where(l => l.Edition != "nextgen").ToList();
            var cfg = new MatchConfig(f0, l0[rng.Next(l0.Count)].Id, f1, l1[rng.Next(l1.Count)].Id, g % 2 == 0, g * 101);
            try
            {
                PlayOne(cfg, rng, stats);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] oyun {g} ({f0} vs {f1}, seed {cfg.Seed}): {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        Console.WriteLine($"{games} oyun, {failures} hata. Tetiklenen etkileşimler: " + string.Join(", ", stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        return failures == 0 ? 0 : 1;
    }

    internal static GwintView PlayOne(MatchConfig cfg, Random rng, Dictionary<string, int> stats, Func<GameSession, bool>? stopWhen = null)
    {
        void Count(string k) => stats[k] = stats.GetValueOrDefault(k) + 1;

        var session = new GameSession(cfg);
        // Animasyon/banner/ses kodu da çalışsın (ses kapalı); yapay zekâ beklemesi yok.
        var view = new GwintView(session, () => { }, _ => { }, animate: false, effects: true);
        var game = session.Game;
        var stuck = 0;
        var lastEvents = -1;

        for (var step = 0; step < 4000 && !session.IsOver; step++)
        {
            Layout(view);
            if (stopWhen != null && stopWhen(session)) return view;
            if (game.Events.Count == lastEvents) stuck++; else { stuck = 0; lastEvents = game.Events.Count; }
            if (stuck > 80) throw new InvalidOperationException($"Arayüz takıldı: faz {game.Phase}, bekleyen {game.Pending?.Kind}, sıra {game.Current}");

            if (UiStep(view, session, rng, Count)) stuck += 5;
        }

        if (!session.IsOver) throw new InvalidOperationException("Oyun 4000 adımda bitmedi.");
        Count("biten-oyun");
        return view;
    }

    // ---------- Online: iki arayüz, bir sunucu ----------

    /// <summary>
    /// İki gerçek arayüz (host koltuk 0, konuk koltuk 1) sunucu üzerinden birbirine karşı rastgele tıklamayla oynar.
    /// Maç sonunda sunucunun ve iki arayüzün oyunu olay olay aynı olmalı; bir de teslim senaryosu denenir.
    /// </summary>
    /// <summary>Bağlantıları kapatır. UI iş parçacığını bloklayarak kapatmak kilitlenir: iş parçacığı havuzunda yapılır.</summary>
    private static void CloseLinks(params RoomLinkBase[] links)
    {
        foreach (var l in links)
            if (l is NetLink n) Task.Run(() => n.DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }


    public static int RunOnline(int games, bool net = false)
    {
        var failures = 0;
        var stats = new Dictionary<string, int>();
        GwentHost.LocalServer? server = net ? GwentHost.LocalServer.StartAsync().GetAwaiter().GetResult() : null;
        for (var g = 1; g <= games; g++)
        {
            try { PlayOnlinePair(g, stats, resignAt: g % 5 == 0 ? 40 : -1, server); }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] online oyun {g}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        if (server != null) Task.Run(() => server.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        Console.WriteLine($"Online{(net ? " (SignalR, gerçek ağ)" : "")}: {games} oyun, {failures} hata. " + string.Join(", ", stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        return failures;
    }

    private static void PlayOnlinePair(int g, Dictionary<string, int> stats, int resignAt, GwentHost.LocalServer? server)
    {
        void Count(string k) => stats[k] = stats.GetValueOrDefault(k) + 1;
        var rng = new Random(g * 977);
        var net = server != null;
        var svc = server?.Rooms ?? new GwentServer.RoomService(null, () => g * 7919);
        RoomLinkBase host = net ? NetLink.ConnectAsync(server!.LocalUrl).GetAwaiter().GetResult() : new RoomLink(svc);
        RoomLinkBase guest = net ? NetLink.ConnectAsync(server!.LocalUrl).GetAwaiter().GetResult() : new RoomLink(svc);
        // Ağda iletiler biraz gecikmeyle gelir: bekleme döngüsü
        void WaitFor(Func<bool> cond, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!cond())
            {
                host.Pump(); guest.Pump();
                if (cond()) return;
                if (DateTime.UtcNow > until) throw new TimeoutException("Zaman aşımı: " + what);
                Thread.Sleep(2);
            }
        }
        var code = host.CreateRoom("Geralt", nextGen: g % 2 == 0).Value!;
        if (!guest.JoinRoom(code, "Yennefer").Ok) throw new InvalidOperationException("Konuk katılamadı.");
        foreach (var (link, who) in new[] { (host, 0), (guest, 1) })
        {
            var f = Factions[rng.Next(5)];
            var leaders = CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen" || g % 2 == 0).ToList();
            var r = link.SetLoadout(new GwentNet.LoadoutDto(f, leaders[rng.Next(leaders.Count)].Id));
            if (!r.Ok) throw new InvalidOperationException($"Deste reddedildi ({who}): {r.Error}");
        }
        if (!host.SetReady(true).Ok || !guest.SetReady(true).Ok) throw new InvalidOperationException("Hazır olunamadı.");
        WaitFor(() => host.Match != null && guest.Match != null, "maç başlangıcı");
        if (host.Match.MySeat != 0 || guest.Match.MySeat != 1) throw new InvalidOperationException("Koltuk numaraları yanlış.");

        var hv = new GwintView(host.Match, () => { }, _ => { }, animate: false, effects: true);
        var gv = new GwintView(guest.Match, () => { }, _ => { }, animate: false, effects: true);
        var room = svc.Find(code)!;
        var stuck = 0;
        var stuckLimit = net ? 3000 : 400;
        var lastLog = -1;
        var resigned = false;

        for (var step = 0; step < 20000; step++)
        {
            host.Pump();
            guest.Pump();
            if (host.Match.IsOver && guest.Match.IsOver) break;
            if (resignAt >= 0 && step == resignAt && !resigned) { resigned = true; guest.Resign(); Count("teslim"); continue; }
            Layout(hv);
            Layout(gv);
            UiStep(hv, host.Match, rng, Count);
            host.Pump();
            UiStep(gv, guest.Match, rng, Count);
            if (net) Thread.Sleep(1);

            var log = room.ActionLog.Count;
            if (log == lastLog) stuck++; else { stuck = 0; lastLog = log; }
            if (stuck > stuckLimit) throw new InvalidOperationException($"Online arayüz takıldı: faz {room.Game!.Phase}, bekleyen {room.Game.Pending?.Kind}, sıra {room.Game.Current}, eylem {log}");
        }
        WaitFor(() => host.Match.IsOver && guest.Match.IsOver, "oyun sonu");

        if (resigned)
        {
            if (host.Match.WinnerSeat != 0 || guest.Match.WinnerSeat != 0) throw new InvalidOperationException("Teslimde kazanan host olmalıydı.");
            Count("teslim-sonucu");
            if (net) { CloseLinks(host, guest); }
            return;
        }
        var srv = room.Game!;
        foreach (var m in new[] { host.Match, guest.Match })
        {
            if (m.Game.Events.Count != srv.Events.Count) throw new InvalidOperationException($"Olay sayısı ayrıştı: sunucu {srv.Events.Count}, istemci {m.Game.Events.Count}");
            for (var i = 0; i < srv.Events.Count; i++)
                if (!Equals(m.Game.Events[i], srv.Events[i])) throw new InvalidOperationException($"Olay {i} ayrıştı.");
            if (m.WinnerSeat != srv.Outcome?.Winner) throw new InvalidOperationException("Kazanan ayrıştı.");
        }
        Count(net ? "net-biten-oyun" : "online-biten-oyun");
        if (net) { CloseLinks(host, guest); }
    }

    // ---------- Online: oda kur / katıl / bekleme odası / maç / rövanş (uçtan uca, gerçek ağ) ----------

    /// <summary>
    /// Gerçek arayüz akışı: host gömülü sunucu açar, konuk adresle katılır; iki bekleme odası görünümünde fraksiyon, deste ve lider
    /// tıklanır, "Hazırım" basılır, maç oynanır, "Rövanş iste" ile ikinci maç oynanır; sunucu ve iki kopya aynı bitmeli.
    /// </summary>
    public static int RunOnlineLobby(int rounds)
    {
        var failures = 0;
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-lobby-" + Guid.NewGuid().ToString("N") + ".json");
        DeckStore.PathOverride = tmp;
        try
        {
            for (var i = 1; i <= rounds; i++)
            {
                try { PlayLobbyFlow(i); }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[HATA] online bekleme odası akışı {i}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
        finally { DeckStore.PathOverride = null; try { System.IO.File.Delete(tmp); } catch { } }
        Console.WriteLine($"Online bekleme odası akışı: {rounds} tur, {failures} hata.");
        return failures;
    }

    /// <summary>
    /// Gerçek cloudflared tüneli: host tünel açar, konuk **davet koduyla** (https tünel adresi) bağlanır, tam bir maç oynanır.
    /// İnternet gerektirir (Cloudflare uç noktasına çıkış). <c>--selftest-tunnel</c>
    /// </summary>
    public static int RunTunnelCheck()
    {
        var path = GwentHost.Cloudflared.FindInstalled();
        if (path == null) { Console.WriteLine("[HATA] cloudflared kurulu değil."); return 1; }
        Console.WriteLine("cloudflared: " + path);
        OnlineController? hostC = null, guestC = null;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (hc, err) = Await(() => OnlineController.HostAsync("Geralt", new HostOptions(false, false, false, path), null));
            if (hc == null) { Console.WriteLine("[HATA] oda kurulamadı: " + err); return 1; }
            hostC = hc;
            var best = hc.Invites.FirstOrDefault();
            if (best == null || best.Kind != "tunnel") { Console.WriteLine("[HATA] tünel daveti yok: " + string.Join(" | ", hc.Warnings)); return 1; }
            Console.WriteLine($"Tünel açıldı ({sw.ElapsedMilliseconds} ms): {best.Url}");
            Console.WriteLine($"Davet kodu: {best.Invite} ({best.Invite.Length} karakter)");

            var host = hc.Link;
            PumpUntil(() => host.State?.Code is { Length: 5 }, "oda", host);
            sw.Restart();
            (guestC, var gerr) = Await(() => OnlineController.JoinInviteAsync(best.Invite, "Yennefer", new Progress<string>(m => Console.WriteLine("  " + m))));
            if (guestC == null) { Console.WriteLine("[HATA] tünelden katılınamadı: " + gerr); return 1; }
            Console.WriteLine($"Konuk tünelden bağlandı ({sw.ElapsedMilliseconds} ms).");
            var guest = guestC.Link;

            host.SetLoadout(new GwentNet.LoadoutDto("northern_realms", "foltest_the_siegemaster"));
            guest.SetLoadout(new GwentNet.LoadoutDto("monsters", "eredin_commander_of_the_red_riders"));
            host.SetReady(true);
            guest.SetReady(true);
            PumpUntil(() => host.Match != null && guest.Match != null, "maç başlangıcı", host, guest);

            var hs = host.Match!;
            var gs = guest.Match!;
            var hv = new GwintView(hs, () => { }, _ => { }, animate: false);
            var gv = new GwintView(gs, () => { }, _ => { }, animate: false);
            var room = hc.Server!.Rooms.Find(host.State!.Code)!;
            var rng = new Random(2024);
            var lastLog = -1;
            var lastProgress = DateTime.UtcNow;
            sw.Restart();
            while (!(hs.IsOver && gs.IsOver))
            {
                host.Pump(); guest.Pump();
                Layout(hv); Layout(gv);
                if (!hs.IsOver) UiStep(hv, hs, rng, _ => { });
                host.Pump();
                if (!gs.IsOver) UiStep(gv, gs, rng, _ => { });
                Thread.Sleep(5);
                var log = room.ActionLog.Count;
                if (log != lastLog) { lastLog = log; lastProgress = DateTime.UtcNow; }
                else if (DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(40)) { Console.WriteLine($"[HATA] tünelde maç takıldı (eylem {log})."); return 1; }
            }
            PumpUntil(() => hs.IsOver && gs.IsOver, "oyun sonu", host, guest);
            var srv = room.Game!;
            foreach (var m in new[] { hs, gs })
            {
                if (m.Game.Events.Count != srv.Events.Count) { Console.WriteLine("[HATA] olay sayısı ayrıştı."); return 1; }
                for (var k = 0; k < srv.Events.Count; k++)
                    if (!Equals(m.Game.Events[k], srv.Events[k])) { Console.WriteLine($"[HATA] olay {k} ayrıştı."); return 1; }
            }
            Console.WriteLine($"Tünel üzerinden tam maç tamam: {room.ActionLog.Count} eylem, {sw.Elapsed.TotalSeconds:0.0} sn, sunucu ve iki kopya aynı.");
            return 0;
        }
        finally
        {
            Await(async () => { if (guestC != null) await guestC.DisposeAsync(); if (hostC != null) await hostC.DisposeAsync(); return 0; });
        }
    }

    /// <summary>
    /// Yarım kalan maç (uygulama kapanıp açılması): host ve konuk gerçek arayüzle birkaç hamle oynar, ikisi de bağlantıyı "kapatır" (teslim değil),
    /// host kayıttan odayı yeniden kurar, konuk yeni adresle aynı koltuğa döner, geçmiş animasyonsuz uygulanır ve maç sonuna dek oynanır. <c>--selftest-resume</c>
    /// </summary>
    public static int RunResumeCheck()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-resume-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        ResumeStore.DirOverride = dir;
        ResumeStore.Enabled = true;
        OnlineController? hc = null, gc = null;
        try
        {
            var opts = new HostOptions(false, false, false, null);
            (hc, var err) = Await(() => OnlineController.HostAsync("Geralt", opts, null));
            if (hc == null) { Console.WriteLine("[HATA] oda kurulamadı: " + err); return 1; }
            var host = hc.Link;
            PumpUntil(() => host.State?.Code is { Length: 5 }, "oda", host);
            var code = host.State!.Code;
            var port1 = hc.Server!.Port;
            (gc, var gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{port1}", code, "Yennefer"));
            if (gc == null) { Console.WriteLine("[HATA] katılınamadı: " + gerr); return 1; }
            var guest = gc.Link;
            host.SetLoadout(new GwentNet.LoadoutDto("northern_realms", "foltest_the_siegemaster"));
            guest.SetLoadout(new GwentNet.LoadoutDto("monsters", "eredin_commander_of_the_red_riders"));
            host.SetReady(true);
            guest.SetReady(true);
            PumpUntil(() => host.Match != null && guest.Match != null, "maç başlangıcı", host, guest);

            // 1) Birkaç hamle oyna
            var hs = host.Match!;
            var gs = guest.Match!;
            var hv = new GwintView(hs, () => { }, _ => { }, animate: false);
            var gv = new GwintView(gs, () => { }, _ => { }, animate: false);
            var room = hc.Server!.Rooms.Find(code)!;
            var rng = new Random(7);
            var until = DateTime.UtcNow.AddSeconds(30);
            while (room.ActionLog.Count < 16 && !(hs.IsOver || gs.IsOver))
            {
                host.Pump(); guest.Pump();
                Layout(hv); Layout(gv);
                UiStep(hv, hs, rng, _ => { });
                host.Pump();
                UiStep(gv, gs, rng, _ => { });
                Thread.Sleep(5);
                if (DateTime.UtcNow > until) { Console.WriteLine("[HATA] ilk hamleler oynanamadı."); return 1; }
            }
            host.Pump(); guest.Pump();
            if (hs.IsOver || gs.IsOver) { Console.WriteLine("[HATA] maç yarıda kalmadan bitti (deneme geçersiz)."); return 1; }

            // 2) Kayıt diske yazıldı mı?
            GwentNet.RoomSnapshot? snap = null;
            for (var i = 0; i < 100 && (snap = ResumeStore.LoadHost()) == null; i++) Thread.Sleep(20);
            if (snap == null || snap.Actions.Count < 10) { Console.WriteLine("[HATA] host kaydı yazılmadı."); return 1; }
            var saved = snap.Actions.Count;
            ResumeStore.SaveGuest(new GuestResume(code, guest.State!.YourToken, "Yennefer", "Geralt", DateTime.UtcNow));

            // 3) "Uygulama kapanır": teslim bildirilmez, yalnız bağlantılar ve sunucu gider
            var g0 = gc; var h0 = hc;
            Await(async () => { await g0.DisposeAsync(false); await h0.DisposeAsync(false); return 0; });
            hc = null; gc = null;
            if (ResumeStore.LoadHost() == null) { Console.WriteLine("[HATA] kapanış kaydı sildi (teslim sayıldı)."); return 1; }

            // 4) Yeniden açılış: host kayıttan döner, konuk yeni adresle aynı koltuğa döner
            var snapCopy = snap;
            (hc, err) = Await(() => OnlineController.ResumeHostAsync(snapCopy, opts, null));
            if (hc == null) { Console.WriteLine("[HATA] host maçı sürdüremedi: " + err); return 1; }
            host = hc.Link;
            PumpUntil(() => host.Match != null, "host maç kurulumu", host);
            var port2 = hc.Server!.Port;
            (gc, gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{port2}", code, "Yennefer"));
            if (gc == null) { Console.WriteLine("[HATA] konuk dönemedi: " + gerr); return 1; }
            guest = gc.Link;
            PumpUntil(() => guest.Match != null && guest.Match.RemoteQueued == saved && host.Match.RemoteQueued == saved, "geçmişin yeniden gelmesi", host, guest);
            hs = host.Match!; gs = guest.Match!;
            hs.FastForward(); gs.FastForward();
            room = hc.Server!.Rooms.Find(code)!;
            foreach (var m in new[] { hs, gs })
                if (m.Game.Events.Count != room.Game!.Events.Count) { Console.WriteLine("[HATA] geçmiş uygulanınca olay sayısı farklı."); return 1; }

            // 5) Kaldığı yerden bitir
            hv = new GwintView(hs, () => { }, _ => { }, animate: false);
            gv = new GwintView(gs, () => { }, _ => { }, animate: false);
            var lastLog = -1;
            var lastProgress = DateTime.UtcNow;
            while (!(hs.IsOver && gs.IsOver))
            {
                host.Pump(); guest.Pump();
                Layout(hv); Layout(gv);
                if (!hs.IsOver) UiStep(hv, hs, rng, _ => { });
                host.Pump();
                if (!gs.IsOver) UiStep(gv, gs, rng, _ => { });
                Thread.Sleep(5);
                var log = room.ActionLog.Count;
                if (log != lastLog) { lastLog = log; lastProgress = DateTime.UtcNow; }
                else if (DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(30)) { Console.WriteLine($"[HATA] maç takıldı (eylem {log})."); return 1; }
            }
            PumpUntil(() => hs.IsOver && gs.IsOver, "oyun sonu", host, guest);
            var srv = room.Game!;
            foreach (var m in new[] { hs, gs })
            {
                if (m.Game.Events.Count != srv.Events.Count) { Console.WriteLine("[HATA] olay sayısı ayrıştı."); return 1; }
                for (var k = 0; k < srv.Events.Count; k++)
                    if (!Equals(m.Game.Events[k], srv.Events[k])) { Console.WriteLine($"[HATA] olay {k} ayrıştı."); return 1; }
            }
            for (var i = 0; i < 100 && ResumeStore.LoadHost() != null; i++) Thread.Sleep(20);
            if (ResumeStore.LoadHost() != null) { Console.WriteLine("[HATA] maç bitince host kaydı silinmedi."); return 1; }
            Console.WriteLine($"Yarım kalan maç sürdürüldü: kapanıştan sonra {saved} hamle geçmişi yeniden kuruldu, toplam {room.ActionLog.Count} eylemle bitti; sunucu ve iki kopya aynı.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[HATA] " + ex); return 1; }
        finally
        {
            var gf = gc; var hf = hc;
            Await(async () => { if (gf != null) await gf.DisposeAsync(); if (hf != null) await hf.DisposeAsync(); return 0; });
            ResumeStore.Enabled = false;
            ResumeStore.DirOverride = null;
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>Teşhis: tünel açar ve adresine her 5 sn'de GET atıp durum kodunu / gövdeyi yazar (--selftest-tunnel-probe).</summary>
    public static int RunTunnelProbe()
    {
        var path = GwentHost.Cloudflared.FindInstalled();
        if (path == null) { Console.WriteLine("cloudflared yok"); return 1; }
        var (hc, err) = Await(() => OnlineController.HostAsync("Geralt", new HostOptions(false, false, false, path), null));
        if (hc == null) { Console.WriteLine("HATA: " + err); return 1; }
        try
        {
            var best = hc.Invites[0];
            Console.WriteLine("Tünel: " + best.Url + "  yerel port: " + hc.Server!.Port);
            using var http = new System.Net.Http.HttpClient(GwentHost.TunnelDns.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) };
            var t0 = DateTime.UtcNow;
            for (var i = 0; i < 16; i++)
            {
                foreach (var suffix in new[] { "/" })
                {
                    try
                    {
                        using var req = new System.Net.Http.HttpRequestMessage(suffix == "/" ? System.Net.Http.HttpMethod.Get : System.Net.Http.HttpMethod.Post, new Uri(new Uri(best.Url), suffix));
                        var res = Await(() => http.SendAsync(req));
                        var body = Await(() => res.Content.ReadAsStringAsync());
                        Console.WriteLine($"[{(DateTime.UtcNow - t0).TotalSeconds,5:0}s] {suffix,-34} -> {(int)res.StatusCode} {body.Replace((char)10, (char)32).Substring(0, Math.Min(90, body.Length))} | " + string.Join("; ", res.Headers.Concat(res.Content.Headers).Select(h => h.Key + "=" + string.Join(",", h.Value)).Take(12)));
                    }
                    catch (Exception ex) { Console.WriteLine($"[{(DateTime.UtcNow - t0).TotalSeconds,5:0}s] {suffix,-34} -> HATA {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}"); }
                }
                Thread.Sleep(5000);
            }
            return 0;
        }
        finally { Await(async () => { await hc.DisposeAsync(); return 0; }); }
    }

    internal static void PumpUntil(Func<bool> cond, string what, params RoomLinkBase[] links)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            foreach (var l in links) l.Pump();
            if (cond()) return;
            if (DateTime.UtcNow > until) throw new TimeoutException("Zaman aşımı: " + what);
            Thread.Sleep(2);
        }
    }

    internal static T Await<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();

    /// <summary>Sahte cloudflared ile tünel akışı: davet listesi, kod çözme, kapatınca süreç ölür.</summary>
    private static void CheckTunnelInvite()
    {
        var fake = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-fake-cf-" + Guid.NewGuid().ToString("N") + ".cmd");
        System.IO.File.WriteAllText(fake, "@echo off\r\necho 2026 INF ^|  https://quiet-river-fox.trycloudflare.com  ^|\r\nping -n 60 127.0.0.1 >nul\r\n");
        try
        {
            var (c, err) = Await(() => OnlineController.HostAsync("Tuneli", new HostOptions(false, false, false, fake), null));
            if (c == null) throw new InvalidOperationException("Tünelli oda kurulamadı: " + err);
            try
            {
                PumpUntil(() => c.Link.State?.Code is { Length: 5 }, "oda", c.Link);
                var best = c.Invites.FirstOrDefault() ?? throw new InvalidOperationException("Davet yok.");
                if (best.Kind != "tunnel" || best.Url != "https://quiet-river-fox.trycloudflare.com/gwent") throw new InvalidOperationException("Tünel daveti yanlış: " + best.Url);
                if (!GwentNet.InviteCodes.TryDecode(best.Invite, out var inv, out _) || inv.Url != best.Url || inv.RoomCode != c.Link.State!.Code) throw new InvalidOperationException("Tünel davet kodu çözülemedi.");
                if (c.Invites.Count != 1) throw new InvalidOperationException("Yalnız tünel seçildi, tek davet olmalı.");
            }
            finally { Await(async () => { await c.DisposeAsync(); return 0; }); }
        }
        finally { try { System.IO.File.Delete(fake); } catch { } }

        // tünel programı yoksa oda yine kurulur, neden uyarı olarak görünür
        var (c2, err2) = Await(() => OnlineController.HostAsync("Tunelsiz", new HostOptions(false, false, false, @"C:\yok\cloudflared.exe"), null));
        if (c2 == null) throw new InvalidOperationException("Tünel başarısızken oda kurulmalıydı: " + err2);
        try { if (c2.Warnings.Count != 1 || c2.Invites.Count != 0) throw new InvalidOperationException("Tünel başarısızlığı uyarı olarak bildirilmeliydi."); }
        finally { Await(async () => { await c2.DisposeAsync(); return 0; }); }
    }

    private static void PlayLobbyFlow(int i)
    {
        var rng = new Random(i * 313);
        var (hostC, herr) = Await(() => OnlineController.HostAsync("Geralt", i % 2 == 0, lan: false));
        if (hostC == null) throw new InvalidOperationException("Oda kurulamadı: " + herr);
        OnlineController? guestC = null;
        try
        {
            var host = hostC.Link;
            PumpUntil(() => host.State?.Code is { Length: 5 }, "oda durumu", host);
            var code = host.State!.Code;
            if (hostC.Url.Contains("0.0.0.0")) throw new InvalidOperationException("Adres 0.0.0.0 olmamalı.");

            // adres normalleştirme: sade adres:port, tam adres ve yanlış girişler
            if (OnlineController.NormalizeUrl($"127.0.0.1:{hostC.Server!.Port}") != hostC.Url) throw new InvalidOperationException("Adres normalleştirme hatalı.");
            var (nobody, bad) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{hostC.Server.Port}", "ZZZZZ", "Yennefer"));
            if (nobody != null || string.IsNullOrEmpty(bad)) throw new InvalidOperationException("Olmayan odaya katılma reddedilmeliydi.");

            // davet koduyla katıl (küçük harf ve tirelerin bozulmasına karşı dayanıklı)
            var inviteText = GwentNet.InviteCodes.Encode(hostC.Url, code).ToLowerInvariant().Replace("-", " ");
            (guestC, var gerr) = Await(() => OnlineController.JoinInviteAsync(inviteText, "Yennefer"));
            var (junk, junkErr) = Await(() => OnlineController.JoinInviteAsync("GWENT-ABCDE-FGHJK", "Ciri"));
            if (junk != null || string.IsNullOrEmpty(junkErr)) throw new InvalidOperationException("Bozuk davet kodu reddedilmeliydi.");
            if (i == 1) CheckTunnelInvite();
            if (guestC == null) throw new InvalidOperationException("Odaya katılınamadı: " + gerr);
            var guest = guestC.Link;
            PumpUntil(() => host.State?.Players.Count == 2 && guest.State?.Players.Count == 2, "iki oyuncu", host, guest);

            // iki bekleme odası görünümü
            var hl = new LobbyView(hostC, () => Task.CompletedTask);
            var gl = new LobbyView(guestC, () => Task.CompletedTask);
            Layout(hl);
            Layout(gl);
            foreach (var (view, link) in new[] { (hl, host), (gl, guest) })
            {
                var f = Factions[rng.Next(5)];
                var factionBtn = FindAll<Border>(view, b => (string?)b.Tag == "lobby-faction:" + f).First();
                Raise(factionBtn, UIElement.MouseLeftButtonUpEvent);
                PumpUntil(() => link.State!.Players.First(p => p.Seat == link.State.YourSeat).Loadout?.Faction == f, "fraksiyon seçimi", host, guest);
                Layout(view);
                var kind = rng.Next(3);
                if (kind == 1)
                {
                    var leaders = FindAll<ToggleButton>(view, t => ((string?)t.Tag)?.StartsWith("lobby-leader:") == true).ToList();
                    Raise(leaders[rng.Next(leaders.Count)], ButtonBase.ClickEvent);
                    PumpUntil(() => true, "lider", host, guest);
                }
                else if (kind == 2)
                {
                    var rec = DeckValidator.RecommendedDeck(f, new GameOptions { NextGen = host.State!.NextGen });
                    DeckStore.Save(new SavedDeck("Sınama", f, rec.LeaderId, rec.CardIds.ToList()));
                    Layout(view);
                    var deckBtn = FindAll<ToggleButton>(view, t => (string?)t.Tag == "lobby-deck:Sınama").FirstOrDefault();
                    if (deckBtn != null) Raise(deckBtn, ButtonBase.ClickEvent);
                }
                PumpUntil(() => true, "seçim", host, guest);
                System.Threading.Thread.Sleep(30);
                host.Pump(); guest.Pump();
                Layout(view);
            }

            // hazırım
            foreach (var view in new[] { hl, gl })
            {
                Layout(view);
                var ready = FindAll<Button>(view, b => (string?)b.Tag == "lobby-ready").First();
                if (!ready.IsEnabled) throw new InvalidOperationException("Hazırım düğmesi devre dışı.");
                Click(ready);
                PumpUntil(() => true, "hazır", host, guest);
            }
            PumpUntil(() => host.Match != null && guest.Match != null, "maç başlangıcı", host, guest);

            for (var game = 1; game <= 2; game++)
            {
                var hs = host.Match!;
                var gs = guest.Match!;
                if (hs.MySeat != 0 || gs.MySeat != 1) throw new InvalidOperationException("Koltuklar yanlış.");
                var hv = new GwintView(hs, () => { }, _ => { }, animate: false, effects: true);
                var gv = new GwintView(gs, () => { }, _ => { }, animate: false, effects: true);
                var room = hostC.Server!.Rooms.Find(code)!;
                var stuck = 0;
                var lastLog = -1;
                for (var step = 0; step < 20000 && !(hs.IsOver && gs.IsOver); step++)
                {
                    host.Pump(); guest.Pump();
                    Layout(hv); Layout(gv);
                    if (!hs.IsOver) UiStep(hv, hs, rng, _ => { });
                    host.Pump();
                    if (!gs.IsOver) UiStep(gv, gs, rng, _ => { });   // oyun bitince rastgele tıklama "Rövanş iste"ye basardı; onu aşağıda bilerek yapıyoruz
                    System.Threading.Thread.Sleep(1);
                    var log = room.ActionLog.Count;
                    if (log == lastLog) stuck++; else { stuck = 0; lastLog = log; }
                    if (stuck > 3000) throw new InvalidOperationException($"Takıldı: faz {room.Game!.Phase}, eylem {log}");
                }
                PumpUntil(() => hs.IsOver && gs.IsOver, "oyun sonu", host, guest);
                var srv = room.Game!;
                foreach (var m in new[] { hs, gs })
                {
                    if (m.Game.Events.Count != srv.Events.Count) throw new InvalidOperationException($"Maç {game}: olay sayısı ayrıştı ({srv.Events.Count} / {m.Game.Events.Count}).");
                    for (var k = 0; k < srv.Events.Count; k++)
                        if (!Equals(m.Game.Events[k], srv.Events[k])) throw new InvalidOperationException($"Maç {game}: olay {k} ayrıştı.");
                }
                if (game == 2) break;

                // rövanş: iki oyun sonu ekranında "Rövanş iste"
                Layout(hv); Layout(gv);
                foreach (var v in new[] { hv, gv })
                {
                    var rb = FindAll<Button>(v, b => b.Content is string t && t == "Rövanş iste").FirstOrDefault()
                             ?? throw new InvalidOperationException("Rövanş düğmesi yok.");
                    Click(rb);
                }
                var oldH = hs; var oldG = gs;
                PumpUntil(() => host.Match != oldH && guest.Match != oldG, "rövanş maçı", host, guest);
            }
        }
        finally
        {
            Await(async () => { if (guestC != null) await guestC.DisposeAsync(); await hostC.DisposeAsync(); return 0; });
        }
    }

    /// <summary>
    /// Bir "kullanıcı adımı": açık pencere varsa çözer, sıra bendeyse rastgele bir tıklama yapar.
    /// Beklemede olduğunu (sıra bende değil) bildirmek için true döner.
    /// </summary>
    internal static bool UiStep(GwintView view, GameSession session, Random rng, Action<string> Count)
    {
        var game = session.Game;
        // 1) Açık pencere varsa onu çöz
        var overlay = FindOverlayButtons(view);
        if (overlay.Visible)
        {
            SolveOverlay(view, rng, Count);
            return false;
        }

        // 2) Normal tur
        if (game.Phase != Phase.Playing || game.Pending != null || game.Current != session.MySeat) return true; // beklemede

        var roll = rng.NextDouble();
        var pass = FindAll<Border>(view, b => (string?)b.Tag == "pass-button").FirstOrDefault();
        var leader = FindAll<GwintCard>(view, b => (string?)b.Tag == "leader-button").FirstOrDefault();
        if (roll < 0.04 && pass != null) { Raise(pass, UIElement.MouseLeftButtonUpEvent); Count("pas"); return false; }
        var piles = FindAll<GwintCard>(view, b => (string?)b.Tag == "grave-pile").ToList();
        if (roll < 0.14 && piles.Count > 0) { Raise(piles[rng.Next(piles.Count)], UIElement.MouseLeftButtonUpEvent); Count("mezarlik-ac"); return false; }
        if (roll < 0.10 && leader != null) { Raise(leader, UIElement.MouseLeftButtonUpEvent); Count("lider"); return false; }

        var hand = FindAll<GwintCard>(view, c => (string?)c.Tag == "hand").ToList();
        if (hand.Count == 0) { if (pass != null) Raise(pass, UIElement.MouseLeftButtonUpEvent); return false; }
        Raise(hand[rng.Next(hand.Count)], UIElement.MouseLeftButtonUpEvent);
        Count("kart-sec");
        Layout(view);

        var playBtn = FindAll<Button>(view, b => b.Content is string t && t.StartsWith("Oyna:")).FirstOrDefault();
        if (playBtn != null) { Click(playBtn); Count("oyna-dugme"); return false; }

        var rows = FindAll<Border>(view, g => (string?)g.Tag == "row-hl").ToList();
        if (rows.Count > 0) { Raise(rows[rng.Next(rows.Count)], UIElement.MouseLeftButtonUpEvent); Count("sira-tikla"); return false; }

        var decoys = FindAll<GwintCard>(view, c => (string?)c.Tag == "decoy-target").ToList();
        if (decoys.Count > 0) { Raise(decoys[rng.Next(decoys.Count)], UIElement.MouseLeftButtonUpEvent); Count("yem-hedef"); return false; }

        var weather = FindAll<Border>(view, b => (string?)b.Tag == "weather-hl").FirstOrDefault();
        if (weather != null) { Raise(weather, UIElement.MouseLeftButtonUpEvent); Count("hava-tikla"); return false; }

        // Oynanamayan seçim: seçimi kaldır (boş yere tıkla)
        Count("hedefsiz-secim");
        return false;
    }

    private static void SolveOverlay(GwintView view, Random rng, Action<string> count)
    {
        var mull = FindAll<GwintCard>(view, c => (string?)c.Tag == "mulligan").ToList();
        var choice = FindAll<GwintCard>(view, c => (string?)c.Tag == "choice").ToList();
        var primary = FindAll<Button>(view, b => b.Style == (Style)Application.Current.FindResource("GBtnPrimary")).LastOrDefault();

        if (mull.Count > 0)
        {
            foreach (var c in mull.OrderBy(_ => rng.Next()).Take(rng.Next(0, 3))) { Raise(c, UIElement.MouseLeftButtonUpEvent); Layout(view); }
            Layout(view);
            primary = FindAll<Button>(view, b => b.Style == (Style)Application.Current.FindResource("GBtnPrimary")).LastOrDefault();
            if (primary != null) Click(primary);
            count("kart-degistirme");
            return;
        }

        if (choice.Count > 0 || FindAll<ToggleButton>(view, _ => true).Any(t => t.Content is TextBlock))
        {
            // Aday kartları rastgele sırayla dene; onay açılana kadar
            var order = choice.OrderBy(_ => rng.Next()).ToList();
            foreach (var c in order)
            {
                Layout(view);
                primary = FindAll<Button>(view, b => b.Style == (Style)Application.Current.FindResource("GBtnPrimary")).LastOrDefault();
                if (primary is { IsEnabled: true }) break;
                // sıra düğmeleri (çevik birim) görünüyorsa önce sırayı seç
                var rowToggle = FindAll<ToggleButton>(view, t => t.IsChecked != true && t.Content is TextBlock tb && (tb.Text == "Yakın dövüş" || tb.Text == "Menzilli")).FirstOrDefault();
                if (rowToggle != null) { Raise(rowToggle, ButtonBase.ClickEvent); Layout(view); count("sira-secimi"); continue; }
                Raise(c, UIElement.MouseLeftButtonUpEvent);
            }
            Layout(view);
            // Çevik aday seçildiyse sıra seçilmeli
            var rt = FindAll<ToggleButton>(view, t => t.IsChecked != true && t.Content is TextBlock tb2 && (tb2.Text == "Yakın dövüş" || tb2.Text == "Menzilli")).FirstOrDefault();
            primary = FindAll<Button>(view, b => b.Style == (Style)Application.Current.FindResource("GBtnPrimary")).LastOrDefault();
            if (primary is { IsEnabled: false } && rt != null) { Raise(rt, ButtonBase.ClickEvent); Layout(view); }
            primary = FindAll<Button>(view, b => b.Style == (Style)Application.Current.FindResource("GBtnPrimary")).LastOrDefault();
            if (primary is { IsEnabled: true }) { Click(primary); count("secim-onay"); return; }
            throw new InvalidOperationException("Seçim penceresi onaylanabilir hale gelmedi.");
        }

        // Lider yakınlaştırma / mezarlık penceresi: Kullan (etkinse, bazen) ya da Vazgeç/Kapat
        var use = FindAll<Button>(view, b => b.Content is string t && t == "Kullan").FirstOrDefault();
        var dismiss = FindAll<Button>(view, b => b.Content is string t && (t == "Vazgeç" || t == "Kapat")).FirstOrDefault();
        if (use != null || dismiss != null)
        {
            if (use is { IsEnabled: true } && rng.NextDouble() < 0.6) { Click(use); count("lider-kullan"); return; }
            if (dismiss != null) { Click(dismiss); count(use != null ? "lider-vazgec" : "mezarlik-kapat"); return; }
        }

        // Scoia'tael ilk oyuncu seçimi vb. (kartsız pencere): ilk birincil düğme
        if (primary != null) { Click(primary); count("diger-pencere"); return; }
        throw new InvalidOperationException("Pencere açık ama tıklanabilir bir şey yok.");
    }

    // ---------- Görsel ağaç yardımcıları ----------

    internal static void Layout(FrameworkElement el)
    {
        el.Measure(new Size(1920, 1080));
        el.Arrange(new Rect(0, 0, 1920, 1080));
        el.UpdateLayout();
    }

    private static (bool Visible, int Count) FindOverlayButtons(GwintView view)
    {
        var overlay = FindAll<Grid>(view, g => g.Visibility == Visibility.Visible && g.Children.Count == 1 && g.Background != null && g.ColumnDefinitions.Count == 0 && Panel.GetZIndex(g) == 100).FirstOrDefault();
        return (overlay != null, overlay?.Children.Count ?? 0);
    }

    internal static IEnumerable<T> FindAll<T>(DependencyObject root, Func<T, bool> pred) where T : DependencyObject
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur is T t && pred(t)) yield return t;
            var n = VisualTreeHelper.GetChildrenCount(cur);
            for (var i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(cur, i));
            if (n == 0 && cur is ContentControl cc && cc.Content is DependencyObject d) stack.Push(d);
        }
    }

    private static Button? FindButton(DependencyObject root, string text) =>
        FindAll<Button>(root, b => b.Content is string s && s == text).FirstOrDefault();

    private static void Click(Button b)
    {
        if (!b.IsEnabled) return;
        Raise(b, ButtonBase.ClickEvent);
    }

    internal static void Raise(UIElement el, RoutedEvent ev)
    {
        if (ev == UIElement.MouseRightButtonUpEvent)
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = ev, Source = el });
        else if (ev == UIElement.MouseLeftButtonUpEvent)
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = ev, Source = el });
        else
            el.RaiseEvent(new RoutedEventArgs(ev, el));
    }

    // ---------- Deste kurucu ----------

    /// <summary>Deste kurucuyu rastgele kullanır; kaydedilen desteleri doğrulayıp onlarla oyun oynar. Hata sayısını döner.</summary>
    public static int RunDeckBuilder()
    {
        var failures = 0;
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-decks-" + Guid.NewGuid().ToString("N") + ".json");
        DeckStore.PathOverride = tmp;
        var rng = new Random(77);
        var saved = 0;
        try
        {
            foreach (var f in Factions)
            {
                foreach (var nextGen in new[] { false, true })
                {
                    try
                    {
                        var opts = new GameOptions { NextGen = nextGen };
                        var view = new GameViewHolder(new DeckBuilderView(f, nextGen, () => { }));
                        var b = view.View;
                        Layout(b);

                        // 1) Rastgele ekle/çıkar
                        for (var i = 0; i < 70; i++)
                        {
                            var pool = FindAll<GwintCard>(b, c => (string?)c.Tag == "pool").ToList();
                            if (pool.Count == 0) throw new InvalidOperationException("Koleksiyon boş.");
                            Raise(pool[rng.Next(pool.Count)], rng.NextDouble() < 0.8 ? UIElement.MouseLeftButtonUpEvent : UIElement.MouseRightButtonUpEvent);
                            Layout(b);
                        }
                        foreach (var minus in FindAll<Button>(b, x => (string?)x.Tag == "dec-minus").Take(5).ToList()) { Click(minus); Layout(b); }

                        // 2) Lider değiştir
                        var leaders = FindAll<GwintCard>(b, c => (string?)c.Tag == "leader").ToList();
                        Raise(leaders[rng.Next(leaders.Count)], UIElement.MouseLeftButtonUpEvent);
                        Layout(b);

                        // 3) Önerilen deste → kaydet
                        Click(FindButton(b, "Önerilen deste")!);
                        Layout(b);
                        var box = FindAll<TextBox>(b, _ => true).First();
                        box.Text = $"Sınama-{f}-{nextGen}";
                        Layout(b);
                        Click(FindAll<Button>(b, x => (string?)x.Tag == "deck-save").First());
                        Layout(b);
                        saved++;

                        var stored = DeckStore.For(f).FirstOrDefault(d => d.Name == box.Text)
                                     ?? throw new InvalidOperationException("Kaydedilen deste depoda yok.");
                        var errors = DeckValidator.Validate(stored.ToDeck(), opts);
                        if (errors.Count > 0) throw new InvalidOperationException("Önerilen deste geçersiz kaydedildi: " + string.Join(" ", errors));

                        // 4) Yeniden aç, kayıtlı desteyi yükle, bir kartı çıkar, tekrar kaydet
                        var view2 = new DeckBuilderView(f, nextGen, () => { });
                        Layout(view2);
                        var chip = FindAll<ToggleButton>(view2, t => (string?)t.Tag == "saved-deck").FirstOrDefault()
                                   ?? throw new InvalidOperationException("Kayıtlı deste çipi görünmüyor.");
                        Raise(chip, ButtonBase.ClickEvent);
                        Layout(view2);
                        Click(FindAll<Button>(view2, x => (string?)x.Tag == "dec-minus").First());
                        Layout(view2);

                        // Kaydedilmemiş değişiklikle geri: önce sorulur; İptal kalır, kaydet ve çık kaydeder
                        var closed = 0;
                        var view3 = new DeckBuilderView(f, nextGen, () => closed++);
                        Layout(view3);
                        Raise(FindAll<ToggleButton>(view3, t => (string?)t.Tag == "saved-deck").First(), ButtonBase.ClickEvent);
                        Layout(view3);
                        if (view3.Dirty) throw new InvalidOperationException("Yüklenen deste değişmiş sayıldı.");
                        Click(FindAll<Button>(view3, x => (string?)x.Tag == "dec-minus").First());
                        Layout(view3);
                        if (!view3.Dirty || !FindAll<TextBlock>(view3, t => (string?)t.Tag == "unsaved-mark").Any()) throw new InvalidOperationException("Değişiklik işareti yok.");
                        Raise(FindAll<FrameworkElement>(view3, x => (string?)x.Tag == "deck-back").First(), UIElement.MouseLeftButtonUpEvent);
                        Layout(view3);
                        if (closed != 0 || !FindAll<FrameworkElement>(view3, x => (string?)x.Tag == "unsaved-dialog").Any()) throw new InvalidOperationException("Kaydedilmemiş değişiklikte geri dönüş sorulmadı.");
                        Click(FindAll<Button>(view3, x => (string?)x.Tag == "unsaved-cancel").First());
                        Layout(view3);
                        if (closed != 0 || FindAll<FrameworkElement>(view3, x => (string?)x.Tag == "unsaved-dialog").Any()) throw new InvalidOperationException("İptal pencereyi kapatmadı.");
                        Raise(FindAll<FrameworkElement>(view3, x => (string?)x.Tag == "deck-back").First(), UIElement.MouseLeftButtonUpEvent);
                        Layout(view3);
                        Click(FindAll<Button>(view3, x => (string?)x.Tag == "unsaved-save").First());
                        if (closed != 1 || view3.Dirty) throw new InvalidOperationException("Kaydet ve çık çalışmadı.");

                        Click(FindAll<Button>(view2, x => (string?)x.Tag == "deck-save").First());

                        // 5) Kaydedilen desteyle gerçek bir oyun
                        var deck = DeckStore.For(f).First().ToDeck();
                        var cfg = new MatchConfig(f, deck.LeaderId, Factions[rng.Next(5)], CardCatalog.Default.Leaders(Factions[0]).First().Id, nextGen, rng.Next(1, 9999), deck);
                        cfg = cfg with { OppLeaderId = CardCatalog.Default.Leaders(cfg.OppFaction).First(l => l.Edition != "nextgen").Id };
                        // Kullanıcı 1 kartı çıkardığı için deste geçersiz olabilir; oyun yalnız geçerli deste ile açılır.
                        if (DeckValidator.Validate(deck, opts).Count == 0) PlayOne(cfg, rng, new Dictionary<string, int>());

                        // 6) Silme
                        Click(FindButton(view2, "Sil")!);
                        if (DeckStore.For(f).Any(d => d.Name == box.Text)) throw new InvalidOperationException("Deste silinmedi.");
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine($"[HATA] deste kurucu {f}/nextgen={nextGen}: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
            // Bozuk dosya oyunu çökertmemeli
            System.IO.File.WriteAllText(tmp, "{ bozuk json");
            if (DeckStore.Load().Count != 0) { failures++; Console.WriteLine("[HATA] bozuk dosya boş liste vermedi"); }
        }
        finally
        {
            DeckStore.PathOverride = null;
            try { System.IO.File.Delete(tmp); } catch { }
        }
        Console.WriteLine($"Deste kurucu: {saved} kayıt denendi, {failures} hata.");
        return failures;
    }

    /// <summary>Lanetler: tüm lanetler açıkken arayüzden maç oynanır (etiket görünür); menüden "Günün Laneti" bugünün lanetleriyle başlar.</summary>
    public static int RunCurseCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] lanet: " + what); } }
        try
        {
            var rng = new Random(9);
            var all = GwentAI.Curses.All.Select(c => c.Id).ToList();
            for (var i = 0; i < 2; i++)
            {
                var f = Factions[i];
                var cfg = new MatchConfig(f, CardCatalog.Default.Leaders(f).First().Id, f, CardCatalog.Default.Leaders(f).First().Id, false, 40 + i, null, GwentAI.Difficulty.Normal, null, all);
                var view = PlayOne(cfg, rng, new Dictionary<string, int>());
                Check(FindAll<TextBlock>(view, t => (t.Tag as string) == "curse-label").Any(), "lanet etiketi yok");
                Check(view.Session.Game.Players[0].Leader.Id == view.Session.Game.Players[1].Leader.Id, "Ayna: rakip aynı liderle oynamadı");
            }
            MatchConfig? started = null;
            var menu = new MenuView(c => started = c, (_, _) => { }, animate: false);
            Layout(menu);
            typeof(MenuView).GetMethod("ToggleSetup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(menu, null);
            Layout(menu);
            Click(FindAll<Button>(menu, b => (b.Tag as string) == "daily-curse").First());
            var today = GwentAI.Curses.Daily(DateOnly.FromDateTime(DateTime.Now));
            Check(started is { Daily: not null } && started.Curses!.SequenceEqual(today.Curses) && started.Seed == today.Seed, "günün laneti başlamadı");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] lanet: " + ex.Message);
        }
        Console.WriteLine($"Lanetler: {failures} hata.");
        return failures;
    }

    /// <summary>Sürüm denetimi: GitHub yanıtından yeni sürüm doğru seçilir; menü bildirimi çıkar.</summary>
    public static int RunUpdateCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] sürüm denetimi: " + what); } }
        var cur = new Version(0, 6, 0);
        Check(UpdateCheck.Newer("{\"tag_name\":\"v0.7\",\"html_url\":\"u\"}", cur)?.Tag == "v0.7", "v0.7 > v0.6 görülmedi");
        Check(UpdateCheck.Newer("{\"tag_name\":\"v0.6\"}", cur) == null, "aynı sürüm yeni sayıldı");
        Check(UpdateCheck.Newer("{\"tag_name\":\"v0.5\"}", cur) == null, "eski sürüm yeni sayıldı");
        Check(UpdateCheck.Newer("{\"tag_name\":\"v0.10\"}", cur) != null, "v0.10 > v0.6 görülmedi");
        Check(UpdateCheck.Newer("{\"tag_name\":\"deneme\"}", cur) == null, "geçersiz etiket");
        Check(UpdateCheck.Current >= new Version(0, 6, 0), "uygulama sürümü okunamadı: " + UpdateCheck.Current);
        UpdateCheck.SetResult(new ReleaseInfo("v9.9", new Version(9, 9, 0), UpdateCheck.ReleasesPage));
        var menu = new MenuView(_ => { }, (_, _) => { }, animate: false);
        Layout(menu);
        Check(FindAll<FrameworkElement>(menu, x => (x.Tag as string) == "update-banner").Any(), "menüde yeni sürüm bildirimi yok");
        UpdateCheck.SetResult(null);
        Console.WriteLine($"Sürüm denetimi ({UpdateCheck.CurrentText}): {failures} hata.");
        return failures;
    }

    /// <summary>"Nasıl oynanır" dergisi: tüm sayfalar ileri/geri düğmeleriyle gezilir, her sayfa çizilir, "Menüye dön" çağrılır.</summary>
    public static int RunGuide()
    {
        var failures = 0;
        var backed = false;
        try
        {
            var view = new GuideView(() => backed = true);
            Layout(view);
            Button Tagged(string tag) => FindAll<Button>(view, b => (b.Tag as string) == tag).First();
            for (var i = 1; i < view.PageCount; i++)
            {
                Click(Tagged("guide-next"));
                Layout(view);
            }
            if (Tagged("guide-next").IsEnabled) throw new InvalidOperationException("Son sayfada 'Sonraki' kapanmadı.");
            for (var i = 1; i < view.PageCount; i++) Click(Tagged("guide-prev"));
            if (Tagged("guide-prev").IsEnabled) throw new InvalidOperationException("İlk sayfada 'Önceki' kapanmadı.");
            var dots = FindAll<TextBlock>(view, t => (t.Tag as string) == "guide-dot").Count();
            if (dots != view.PageCount) throw new InvalidOperationException($"Sayfa noktası {dots}, sayfa {view.PageCount}.");
            Click(Tagged("guide-back"));
            if (!backed) throw new InvalidOperationException("'Menüye dön' çalışmadı.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] kılavuz: " + ex.Message);
        }
        Console.WriteLine($"Kılavuz: {failures} hata.");
        return failures;
    }

    /// <summary>Görsel ağaç yardımcılarının çalışması için çizim yüzeyi.</summary>
    private sealed class GameViewHolder
    {
        public FrameworkElement View { get; }
        public GameViewHolder(FrameworkElement v) => View = v;
    }
}
