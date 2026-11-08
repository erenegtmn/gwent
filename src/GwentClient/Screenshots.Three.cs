using GwentAI;
using GwentCore;
using GwentNet;

namespace GwentClient;

/// <summary>Üç kişilik online masa ekranları (KURALLAR §24): bekleme odası, zar, dolu masa, hedef seçimi, oyun sonu.</summary>
public static class ThreeShots
{
    /// <summary>Aynı süreçteki sunucuda üç oyunculu, maçı başlamış oda (koltuk 0 host).</summary>
    public static RoomLink[] StartedRoom(GwentServer.RoomService svc, string[]? factions = null)
    {
        factions ??= new[] { "northern_realms", "nilfgaard", "monsters" };
        var links = new[] { new RoomLink(svc), new RoomLink(svc), new RoomLink(svc) };
        var code = links[0].CreateRoom("Geralt", players: 3).Value!;
        links[1].JoinRoom(code, "Yennefer");
        links[2].JoinRoom(code, "Ciri");
        for (var i = 0; i < 3; i++)
        {
            var f = factions[i];
            var leader = CardCatalog.Default.Leaders(f).First(l => l.Edition != "nextgen").Id;
            links[i].SetLoadout(new LoadoutDto(f, leader, Avatar: i == 0 ? "avatar_geralt" : null));
        }
        foreach (var l in links) l.SetReady(true);
        foreach (var l in links) l.Pump();
        return links;
    }

    /// <summary>Üç oturumu motor üzerinden (arayüzsüz) ilerletir: sırası gelen kendi koltuğu için pas geçmeyen rastgele bir eylem yollar.</summary>
    public static void Advance(RoomLink[] links, Random rng, int steps, Func<Game, bool>? stop = null)
    {
        for (var i = 0; i < steps; i++)
        {
            foreach (var l in links) { l.Pump(); while (l.Match!.RemoteQueued > 0) l.Match.ApplyNextRemote(); }
            var g = links[0].Match!.Game;
            if (g.Phase == Phase.GameOver || (stop?.Invoke(g) ?? false)) return;
            foreach (var l in links)
            {
                var s = l.Match!;
                if (s.AwaitingEcho || !s.Game.PlayersToAct().Contains(s.MySeat)) continue;
                var legal = s.Game.LegalActions().Where(a => a.Player == s.MySeat).ToList();
                var nonPass = legal.Where(a => a is not Pass && a is not UseLeader).ToList();
                var pick = nonPass.Count > 0 && (s.Game.Players[s.MySeat].Hand.Count > 4 || rng.NextDouble() < 0.7) ? nonPass[rng.Next(nonPass.Count)] : legal[rng.Next(legal.Count)];
                s.Apply(pick);
            }
        }
        foreach (var l in links) { l.Pump(); while (l.Match!.RemoteQueued > 0) l.Match.ApplyNextRemote(); }
    }

    public static void Run(Action<System.Windows.FrameworkElement, string> save)
    {
        // Bekleme odası: gerçek gömülü sunucu, host + iki konuk (biri hazır)
        try
        {
            var (hc, _) = UiSelfTest.Await(() => OnlineController.HostAsync("Geralt", new HostOptions(false, false, false, null, Players: 3), null));
            var lh = hc!.Link;
            UiSelfTest.PumpUntil(() => lh.State?.Code is { Length: 5 }, "oda", lh);
            var (g1, _) = UiSelfTest.Await(() => OnlineController.JoinAsync($"127.0.0.1:{hc.Server!.Port}", lh.State!.Code, "Yennefer"));
            UiSelfTest.PumpUntil(() => lh.State?.Players.Count == 2, "ikinci oyuncu", lh, g1!.Link);
            var empty = new LobbyView(hc, () => Task.CompletedTask);
            UiSelfTest.Layout(empty);
            save(empty, "three_lobby_waiting.png");
            var (g2, _) = UiSelfTest.Await(() => OnlineController.JoinAsync($"127.0.0.1:{hc.Server!.Port}", lh.State!.Code, "Ciri"));
            if (g2 == null) throw new InvalidOperationException("üçüncü oyuncu katılamadı");
            foreach (var r in new[]
            {
                UiSelfTest.Await(() => g1.Link.SetLoadoutAsync(new LoadoutDto("nilfgaard", "emhyr_var_emreis_his_imperial_majesty"))),
                UiSelfTest.Await(() => g1.Link.SetReadyAsync(true)),
                UiSelfTest.Await(() => g2.Link.SetLoadoutAsync(new LoadoutDto("monsters", "eredin_bringer_of_death"))),
            })
                if (!r.Ok) throw new InvalidOperationException("bekleme odası isteği reddedildi: " + r.Error);
            try { UiSelfTest.PumpUntil(() => lh.State?.Players.Count == 3 && lh.State.Players.Any(p => p.Seat == 2 && p.Loadout != null) && lh.State.Players.Any(p => p.Ready), "üç oyuncu", lh, g1.Link, g2.Link); }
            catch (TimeoutException)
            {
                throw new TimeoutException("durum: " + string.Join(" | ", lh.State!.Players.Select(p => $"{p.Seat}:{p.Nick} ld={p.Loadout?.Faction} r={p.Ready} c={p.Connected}")));
            }
            var full = new LobbyView(hc, () => Task.CompletedTask);
            UiSelfTest.Layout(full);
            save(full, "three_lobby.png");
            UiSelfTest.Await(async () => { await g2.DisposeAsync(); await g1.DisposeAsync(); await hc.DisposeAsync(); return 0; });
        }
        catch (Exception ex) { Console.WriteLine("[HATA] üç kişilik bekleme odası ekranı: " + ex); }

        // Zar (animasyonun ortası ve sonu)
        {
            var svc = new GwentServer.RoomService(null, () => 77);
            var links = StartedRoom(svc);
            var dv = new GwintView(links[0].Match!, () => { }, _ => { }, animate: true);
            UiSelfTest.Layout(dv);
            Pump(dv, 600);
            save(dv, "three_dice_roll.png");
            Pump(dv, 1300 + (links[0].Match!.Game.DiceRolls.Count - 1) * 1500);
            save(dv, "three_dice_result.png");
        }

        // Dolu masa: koltuk 0 ve koltuk 2 gözünden (ben altta, rakipler üstte ikiye bölünmüş)
        {
            var svc = new GwentServer.RoomService(null, () => 4243);
            var links = StartedRoom(svc, new[] { "skellige", "scoiatael", "monsters" });
            Advance(links, new Random(3), 40, g => g.Round == 1 && g.Players.All(p => p.Rows.Sum(r => r.Cards.Count) >= 4)
                && g.Phase == Phase.Playing && g.Pending == null && g.Current == 0);
            var v0 = new GwintView(links[0].Match!, () => { }, _ => { }, animate: false);
            UiSelfTest.Layout(v0);
            save(v0, "three_table.png");
            var v2 = new GwintView(links[2].Match!, () => { }, _ => { }, animate: false);
            UiSelfTest.Layout(v2);
            save(v2, "three_table_seat2.png");

            // Casus seçili: iki rakibin sırası da vurgulu
            var g = links[0].Match!.Game;
            var spy = g.Players[0].Hand.FirstOrDefault(c => c.Def.Has("spy"));
            if (spy == null)
            {
                // elde casus yoksa ekran için bir tane eklenir (yalnız görüntü; oyun bu oturumla sürmez)
                spy = typeof(Game).GetMethod("TestHand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .Invoke(g, new object[] { 0, "vattier_de_rideaux" }) as CardInstance;
            }
            if (spy != null && g.Current == 0)
            {
                v0.Refresh();
                UiSelfTest.Layout(v0);
                var hand = UiSelfTest.FindAll<GwintCard>(v0, c => (string?)c.Tag == "hand" && c.Card?.Uid == spy.Uid).FirstOrDefault();
                if (hand != null)
                {
                    UiSelfTest.Raise(hand, System.Windows.UIElement.MouseLeftButtonUpEvent);
                    UiSelfTest.Layout(v0);
                    save(v0, "three_spy_target.png");
                }
            }

            // Raund sonu ve oyun sonu
            Advance(links, new Random(4), 4000);
            var end = new GwintView(links[1].Match!, () => { }, _ => { }, animate: false);
            UiSelfTest.Layout(end);
            save(end, "three_game_over.png");
        }
    }

    private static void Pump(System.Windows.FrameworkElement v, int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Threading.Thread.Sleep(16);
        }
        UiSelfTest.Layout(v);
    }
}
