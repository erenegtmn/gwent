using System.Windows.Controls;
using GwentCore;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Üç kişilik online maç (KURALLAR §24): üç gerçek arayüz aynı sunucuda rastgele tıklamayla oynar (zar, casus hedefi, hedef seçici,
    /// Medic'le casus, Scoia'tael seçimi dahil). Sunucu ve üç kopya olay olay aynı bitmeli. Arada teslim/terk senaryosu.
    /// </summary>
    public static int RunOnlineThree(int games, bool net = false)
    {
        var failures = 0;
        var stats = new Dictionary<string, int>();
        GwentHost.LocalServer? server = net ? GwentHost.LocalServer.StartAsync().GetAwaiter().GetResult() : null;
        for (var g = 1; g <= games; g++)
        {
            try { PlayOnlineThree(g, stats, resignAt: g % 4 == 0 ? 60 : -1, server); }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] üç kişilik oyun {g}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        if (server != null) Task.Run(() => server.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        Console.WriteLine($"Üç kişilik online{(net ? " (SignalR, gerçek ağ)" : "")}: {games} oyun, {failures} hata. " + string.Join(", ", stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        return failures;
    }

    private static void PlayOnlineThree(int g, Dictionary<string, int> stats, int resignAt, GwentHost.LocalServer? server)
    {
        void Count(string k) => stats[k] = stats.GetValueOrDefault(k) + 1;
        var rng = new Random(g * 1231);
        var net = server != null;
        var svc = server?.Rooms ?? new GwentServer.RoomService(null, () => g * 6007);
        RoomLinkBase Link() => net ? NetLink.ConnectAsync(server!.LocalUrl).GetAwaiter().GetResult() : new RoomLink(svc);
        var links = new[] { Link(), Link(), Link() };
        void PumpAll() { foreach (var l in links) l.Pump(); }
        void WaitFor(Func<bool> cond, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!cond())
            {
                PumpAll();
                if (cond()) return;
                if (DateTime.UtcNow > until) throw new TimeoutException("Zaman aşımı: " + what);
                Thread.Sleep(2);
            }
        }
        var code = links[0].CreateRoom("Geralt", nextGen: g % 2 == 0, players: 3).Value!;
        if (!links[1].JoinRoom(code, "Yennefer").Ok || !links[2].JoinRoom(code, "Ciri").Ok) throw new InvalidOperationException("Konuklar katılamadı.");
        // Scoia'tael sık gelsin (başlayanı seçme penceresi denensin); casuslu desteler de
        for (var i = 0; i < 3; i++)
        {
            var f = i == 1 && g % 3 == 0 ? "scoiatael" : Factions[rng.Next(5)];
            var leaders = CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen" || g % 2 == 0).ToList();
            var r = links[i].SetLoadout(new GwentNet.LoadoutDto(f, leaders[rng.Next(leaders.Count)].Id));
            if (!r.Ok) throw new InvalidOperationException($"Deste reddedildi ({i}): {r.Error}");
        }
        WaitFor(() => links.All(l => l.State?.Players.Count == 3), "üç oyuncu");
        foreach (var l in links) if (!l.SetReady(true).Ok) throw new InvalidOperationException("Hazır olunamadı.");
        WaitFor(() => links.All(l => l.Match != null), "maç başlangıcı");
        for (var i = 0; i < 3; i++)
            if (links[i].Match!.MySeat != i || !links[i].Match!.ThreePlayer) throw new InvalidOperationException("Koltuk numaraları / üç kişilik kurulum yanlış.");

        var views = links.Select(l => new GwintView(l.Match!, () => { }, _ => { }, animate: false, effects: true)).ToArray();
        var room = svc.Find(code)!;
        var stuck = 0;
        var lastLog = -1;
        var resigned = false;
        for (var step = 0; step < 30000; step++)
        {
            PumpAll();
            if (links.All(l => l.Match!.IsOver)) break;
            if (resignAt >= 0 && step == resignAt && !resigned) { resigned = true; links[2].Resign(); Count("teslim"); continue; }
            for (var i = 0; i < 3; i++)
            {
                Layout(views[i]);
                UiStep3(views[i], links[i].Match!, rng, Count);
                links[i].Pump();
            }
            if (net) Thread.Sleep(1);
            var log = room.ActionLog.Count;
            if (log == lastLog) stuck++; else { stuck = 0; lastLog = log; }
            if (stuck > (net ? 3000 : 500)) throw new InvalidOperationException($"Üç kişilik arayüz takıldı: faz {room.Game!.Phase}, bekleyen {room.Game.Pending?.Kind}, sıra {room.Game.Current}, eylem {log}");
        }
        WaitFor(() => links.All(l => l.Match!.IsOver), "oyun sonu");
        foreach (var v in views) { v.Refresh(); Layout(v); }   // oyun sonu paneli de çizilsin

        if (resigned)
        {
            var w = links[0].Match!.WinnerSeat;
            if (links.Any(l => l.Match!.WinnerSeat != w) || w == 2) throw new InvalidOperationException("Teslimde kazanan tutarsız (teslim olan kazanamaz).");
            Count("teslim-sonucu");
        }
        else
        {
            var srv = room.Game!;
            foreach (var m in links.Select(l => l.Match!))
            {
                if (m.Game.Events.Count != srv.Events.Count) throw new InvalidOperationException($"Olay sayısı ayrıştı: sunucu {srv.Events.Count}, istemci {m.Game.Events.Count}");
                for (var i = 0; i < srv.Events.Count; i++)
                    if (!Equals(m.Game.Events[i], srv.Events[i])) throw new InvalidOperationException($"Olay {i} ayrıştı.");
                if (m.WinnerSeat != srv.Outcome?.Winner) throw new InvalidOperationException("Kazanan ayrıştı.");
            }
            Count("biten-oyun");
            Count("raund-" + srv.Round);
        }
        if (net) CloseLinks(links);
    }

    /// <summary>İki kişilik adımın üç kişilik eki: hedef rakip penceresi ve Medic'in rakip düğmeleri de çözülür.</summary>
    private static void UiStep3(GwintView view, GameSession session, Random rng, Action<string> count)
    {
        var foes = FindAll<Button>(view, b => (string?)b.Tag == "foe-choice").ToList();
        if (foes.Count > 0)
        {
            Click(foes[rng.Next(foes.Count)]);
            count("hedef-rakip");
            return;
        }
        var foeToggles = FindAll<System.Windows.Controls.Primitives.ToggleButton>(view, t => (string?)t.Tag == "choice-foe").ToList();
        if (foeToggles.Count > 0 && foeToggles.All(t => t.IsChecked != true))
        {
            Raise(foeToggles[rng.Next(foeToggles.Count)], System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
            Layout(view);
            count("medic-hedef");
        }
        var choose = FindAll<Button>(view, b => (string?)b.Tag == "choose-first").ToList();
        if (choose.Count > 0)
        {
            Click(choose[rng.Next(choose.Count)]);
            count("baslayan-sec");
            return;
        }
        UiStep(view, session, rng, count);
    }
}
