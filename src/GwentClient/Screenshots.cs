using GwentAI;
using GwentCore;

namespace GwentClient;

/// <summary>Ekranları pencere açmadan PNG'ye çizer: <c>Gwent.exe --shot klasör</c>.</summary>
public static class Screenshots
{
    private const double W = 1920, H = 1080;

    public static void Run(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        void Save(System.Windows.FrameworkElement e, string name) => Ui.RenderToPng(e, W, H, System.IO.Path.Combine(dir, name));

        // Deste kurucu ve menüdeki kayıtlı deste (gerçek kullanıcı verisine dokunmadan)
        DeckStore.PathOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-shot-decks.json");
        System.IO.File.Delete(DeckStore.PathOverride);
        var rec = DeckValidator.RecommendedDeck("northern_realms");
        DeckStore.Save(new SavedDeck("Hızlı Saldırı", rec.Faction, rec.LeaderId, rec.CardIds.ToList()));
        DeckStore.Save(new SavedDeck("Yarım deste", "northern_realms", rec.LeaderId, rec.CardIds.Take(8).ToList()));
        Save(new MenuView(_ => { }, (_, _) => { }, animate: false, openSettings: () => { }, openOnline: () => { }, openStats: () => { }), "menu.png");
        var setupMenu = new MenuView(_ => { }, (_, _) => { }, animate: false);
        typeof(MenuView).GetMethod("ToggleSetup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(setupMenu, null);
        Save(setupMenu, "menu_setup.png");
        var vsMenu = new MenuView(_ => { }, (_, _) => { }, animate: false);
        typeof(MenuView).GetMethod("ShowVs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vsMenu, new object[] { new MatchConfig("northern_realms", "foltest_the_siegemaster", "monsters", "eredin_commander_of_the_red_riders", false, 1) });
        Save(vsMenu, "vs.png");
        Save(new SettingsView(() => { }), "settings.png");
        var builder = new DeckBuilderView("northern_realms", false, () => { });
        UiSelfTest.Layout(builder);
        var chip = UiSelfTest.FindAll<System.Windows.Controls.Primitives.ToggleButton>(builder, t => (string?)t.Tag == "saved-deck").First();
        UiSelfTest.Raise(chip, System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
        Save(builder, "builder.png");
        typeof(DeckBuilderView).GetMethod("ShowPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(builder, new object[] { CardCatalog.Default.Get("impera_brigade_guard"), true });
        UiSelfTest.Layout(builder);
        Save(builder, "builder_preview.png");
        DeckStore.PathOverride = null;

        // Gerçek ana pencere kuruluyor mu? (gösterilmeden; XAML ve açılış akışı doğrulanır)
        var win = new MainWindow();
        Save((System.Windows.FrameworkElement)win.Content, "window.png");

        var cfg = new MatchConfig("northern_realms", "foltest_the_siegemaster", "monsters", "eredin_commander_of_the_red_riders", false, 4242);

        // Kart değiştirme penceresi
        Save(new GwintView(new GameSession(cfg), () => { }, _ => { }, animate: false), "mulligan.png");

        // Oyun ortası: iki taraf da sezgisel ajanla bir süre oynar, sıra bana geçince dururuz.
        var s = new GameSession(cfg);
        s.AiAutoMulligan();
        s.Apply(new HeuristicAgent().Decide(s.Game, GameSession.Me));
        var ai = new HeuristicAgent();
        for (var i = 0; i < 15 && !s.IsOver; i++)
        {
            var who = s.Game.PlayersToAct()[0];
            s.Apply(ai.Decide(s.Game, who));
        }
        while (!s.IsOver && s.Game.PlayersToAct()[0] != GameSession.Me) s.AiStep();
        Save(new GwintView(s, () => { }, _ => { }, animate: false), "game.png");

        // Bir kart seçili
        var v = new GwintView(s, () => { }, _ => { }, animate: false);
        Save(v, "game2.png");

        // Lider yakınlaştırma ve mezarlık görüntüleyici
        var zv = new GwintView(s, () => { }, _ => { }, animate: false);
        UiSelfTest.Layout(zv);
        UiSelfTest.Raise(UiSelfTest.FindAll<GwintCard>(zv, c => (string?)c.Tag == "leader-button").First(), System.Windows.UIElement.MouseLeftButtonUpEvent);
        Save(zv, "leader_zoom.png");
        var gv = UiSelfTest.PlayOne(cfg, new Random(7), new Dictionary<string, int>(), g => g.Game.Phase == Phase.Playing && g.Game.Pending == null && g.Game.Current == GameSession.Me && g.Game.Players[0].Grave.Count >= 2);
        UiSelfTest.Layout(gv);
        var pile = UiSelfTest.FindAll<GwintCard>(gv, c => (string?)c.Tag == "grave-pile").FirstOrDefault();
        if (pile != null) { UiSelfTest.Raise(pile, System.Windows.UIElement.MouseLeftButtonUpEvent); Save(gv, "grave_view.png"); }

        // Global etkili kart sinematiği (animasyon bir süre oynatılıp yakalanır)
        var cv = new GwintView(s, () => { }, _ => { }, animate: true);
        UiSelfTest.Layout(cv);
        typeof(GwintView).GetMethod("StartCinema", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(cv, new object[] { 0, s.Game.Catalog.Get(GwentCore.Ids.ImpenetrableFog), "Sen oynadın", new List<GwentCore.GameEvent>() });
        var until = DateTime.UtcNow.AddMilliseconds(1100);
        while (DateTime.UtcNow < until)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Threading.Thread.Sleep(16);
        }
        UiSelfTest.Layout(cv);
        Save(cv, "cinema.png");

        // Maç başı sekansı: yazı-tura (havada) ve kart dağıtımı (ortasında)
        var ov = new GwintView(new GameSession(cfg), () => { }, _ => { }, animate: true);
        UiSelfTest.Layout(ov);
        void Pump(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                System.Threading.Thread.Sleep(16);
            }
            UiSelfTest.Layout(ov);
        }
        Pump(950);
        Save(ov, "open_coin.png");
        Pump(2650);
        Save(ov, "open_result.png");
        Pump(900);
        Save(ov, "open_deal.png");

        // Online: konuk (koltuk 1) görünümü; ben yine altta, rakip üstte olmalı
        {
            var svc = new GwentServer.RoomService(null, () => 4242);
            var ol0 = new RoomLink(svc);
            var gl = new RoomLink(svc);
            var code = ol0.CreateRoom("Geralt").Value!;
            gl.JoinRoom(code, "Yennefer");
            ol0.SetLoadout(new GwentNet.LoadoutDto("northern_realms", "foltest_the_siegemaster"));
            gl.SetLoadout(new GwentNet.LoadoutDto("nilfgaard", "emhyr_var_emreis_his_imperial_majesty"));
            ol0.SetReady(true);
            gl.SetReady(true);
            ol0.Pump();
            gl.Pump();
            var ohv = new GwintView(ol0.Match!, () => { }, _ => { }, animate: false);
            var ogv = new GwintView(gl.Match!, () => { }, _ => { }, animate: false);
            var rr = new Random(5);
            for (var i = 0; i < 22; i++)
            {
                ol0.Pump(); gl.Pump();
                UiSelfTest.Layout(ohv); UiSelfTest.Layout(ogv);
                UiSelfTest.UiStep(ohv, ol0.Match!, rr, _ => { });
                ol0.Pump();
                UiSelfTest.UiStep(ogv, gl.Match!, rr, _ => { });
            }
            ol0.Pump(); gl.Pump();
            UiSelfTest.Layout(ogv);
            Save(ogv, "online_guest.png");
        }

        {
            var d = DateTime.Now;
            var statRecs = new List<MatchRecord>
            {
                new(d.AddDays(-3), false, "Yapay zekâ (Normal)", "nilfgaard", "monsters", 1),
                new(d.AddDays(-2), true, "Yennefer", "northern_realms", "scoiatael", -1),
                new(d.AddDays(-1), true, "Yennefer", "northern_realms", "skellige", 1),
                new(d, false, "Yapay zekâ (Zor)", "monsters", "nilfgaard", 0),
            };
            Save(new ProfileView(() => { }, statRecs, persist: false), "stats.png");
            Save(new ProfileView(() => { }, new List<MatchRecord>(), persist: false), "stats_empty.png");
        }
        // Online: giriş ekranı, bekleme odası (iki oyuncu), oyun içi onay ve rakip koptu çubuğu
        Save(new OnlineMenuView(() => { }, _ => { }), "online_menu.png");
        {
            var (hc, _) = UiSelfTest.Await(() => OnlineController.HostAsync("Geralt", false, lan: false));
            var lh = hc!.Link;
            UiSelfTest.PumpUntil(() => lh.State?.Code is { Length: 5 }, "oda", lh);
            var (gc, _) = UiSelfTest.Await(() => OnlineController.JoinAsync($"127.0.0.1:{hc.Server!.Port}", lh.State!.Code, "Yennefer"));
            var lg = gc!.Link;
            UiSelfTest.PumpUntil(() => lh.State?.Players.Count == 2, "iki oyuncu", lh, lg);
            var hl = new LobbyView(hc, () => Task.CompletedTask);
            var gl = new LobbyView(gc, () => Task.CompletedTask);
            UiSelfTest.Layout(gl);
            UiSelfTest.Raise(UiSelfTest.FindAll<System.Windows.Controls.Border>(gl, b => (string?)b.Tag == "lobby-faction:nilfgaard").First(), System.Windows.UIElement.MouseLeftButtonUpEvent);
            UiSelfTest.PumpUntil(() => lh.State!.Players.Any(p => p.Loadout?.Faction == "nilfgaard"), "konuk seçimi", lh, lg);
            UiSelfTest.Layout(hl);
            Save(hl, "lobby.png");
            UiSelfTest.Await(async () => { await gc.DisposeAsync(); await hc.DisposeAsync(); return 0; });
        }
        {
            // Davet kodlu bekleme odası (sahte tünel + gerçek UPnP yoklaması: bu ağda modem yanıt vermez, uyarı çıkar)
            var fake = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-shot-cf-" + Guid.NewGuid().ToString("N") + ".cmd");
            System.IO.File.WriteAllText(fake, "@echo off\r\necho 2026 INF ^|  https://quiet-river-fox-abc.trycloudflare.com  ^|\r\nping -n 60 127.0.0.1 >nul\r\n");
            var (ic, _) = UiSelfTest.Await(() => OnlineController.HostAsync("Geralt", new HostOptions(false, true, true, fake), null));
            UiSelfTest.PumpUntil(() => ic!.Link.State?.Code is { Length: 5 }, "oda", ic!.Link);
            var il = new LobbyView(ic!, () => Task.CompletedTask);
            UiSelfTest.Layout(il);
            Save(il, "lobby_invite.png");
            UiSelfTest.Await(async () => { await ic.DisposeAsync(); return 0; });
            try { System.IO.File.Delete(fake); } catch { }
        }
        {
            var svc2 = new GwentServer.RoomService(null, () => 99);
            var l0 = new RoomLink(svc2);
            var l1 = new RoomLink(svc2);
            var cd = l0.CreateRoom("Geralt").Value!;
            l1.JoinRoom(cd, "Yennefer");
            l0.SetLoadout(new GwentNet.LoadoutDto("northern_realms", "foltest_the_siegemaster"));
            l1.SetLoadout(new GwentNet.LoadoutDto("monsters", "eredin_commander_of_the_red_riders"));
            l0.SetReady(true); l1.SetReady(true);
            l0.Pump(); l1.Pump();
            var oview = new GwintView(l0.Match!, () => { }, _ => { }, animate: false);
            UiSelfTest.Layout(oview);
            oview.OnlineNotice("opponent_disconnected", "Yennefer bağlantısı koptu.");
            UiSelfTest.Layout(oview);
            Save(oview, "online_gone.png");
            typeof(GwintView).GetField("_confirm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(oview, "resign");
            oview.Refresh();
            UiSelfTest.Layout(oview);
            Save(oview, "online_confirm.png");
        }

        // Oyun içi ayarlar penceresi
        var igs = new GwintView(s, () => { }, _ => { }, animate: false);
        typeof(GwintView).GetMethod("OpenSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(igs, null);
        Save(igs, "ingame_settings.png");

        // Hava örtüleri (animasyon bir süre oynatılıp yakalanır)
        var wxCanvas = new System.Windows.Controls.Canvas { Width = 1920, Height = 1080, Background = Ui_Brush("#3a2616") };
        var wxTypes = new[] { "frost", "fog", "rain", "storm" };
        for (var i = 0; i < wxTypes.Length; i++)
        {
            var el = WeatherFx.Make(wxTypes[i], 797, 124, true);
            System.Windows.Controls.Canvas.SetLeft(el, 300);
            System.Windows.Controls.Canvas.SetTop(el, 120 + i * 180);
            wxCanvas.Children.Add(el);
        }
        UiSelfTest.Layout(wxCanvas);
        var wxUntil = DateTime.UtcNow.AddMilliseconds(1800);
        while (DateTime.UtcNow < wxUntil)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Threading.Thread.Sleep(16);
        }
        Save(wxCanvas, "weather.png");

        // Banner (animasyonu atlayıp tam görünür hâlde)
        var bv = new GwintView(s, () => { }, _ => { }, animate: false, effects: false);
        var show = typeof(GwintView).GetMethod("ShowBanner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        show.Invoke(bv, new object[] { "Raundu kazandın!", "Sen 35  –  Rakip 30\nRaund 2: sen başlıyorsun", Theme.Good, 2000, "msg_round_victory" });
        var host = (System.Windows.Controls.Grid)typeof(GwintView).GetField("_bannerHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(bv)!;
        foreach (System.Windows.UIElement c in host.Children) { c.BeginAnimation(System.Windows.UIElement.OpacityProperty, null); c.Opacity = 1; }
        Save(bv, "banner.png");

        // Uzun isimli kart önizlemesi (isim iki satıra iner; açıklama altında kalmalı)
        var pv = new GwintView(s, () => { }, _ => { }, animate: false);
        typeof(GwintView).GetMethod("SetPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(pv, new object?[] { CardCatalog.Default.Get("emhyr_var_emreis_his_imperial_majesty"), null });
        Save(pv, "preview_long.png");

        // Bir kart seçili (sıra vurgusu + Oyna düğmesi)
        var sel = new GwintView(s, () => { }, _ => { }, animate: false);
        UiSelfTest.Layout(sel);
        var hand = UiSelfTest.FindAll<GwintCard>(sel, c => (string?)c.Tag == "hand").ToList();
        var pick = hand.FirstOrDefault(c => c.Def.IsAgile) ?? hand.FirstOrDefault(c => c.Def.IsUnit && c.Def.Hero) ?? hand.First();
        UiSelfTest.Raise(pick, System.Windows.UIElement.MouseLeftButtonUpEvent);
        Save(sel, "selected.png");

        // Medic seçim penceresi: rastgele oyunlarda ilk Medic seçimini yakala
        foreach (var seed in Enumerable.Range(1, 400))
        {
            var cfgM = new MatchConfig("nilfgaard", "emhyr_var_emreis_the_white_flame", "northern_realms", "foltest_the_siegemaster", false, seed);
            var hit = false;
            try
            {
                var view = UiSelfTest.PlayOne(cfgM, new Random(seed), new Dictionary<string, int>(),
                    g => hit = g.Game.Pending is { Player: 0, Kind: ChoiceKind.Medic });
                if (hit) { Save(view, "medic.png"); break; }
            }
            catch { }
        }

        // Oyun sonu
        var s2 = new GameSession(cfg with { Seed = 7 });
        s2.AiAutoMulligan();
        var h = new HeuristicAgent();
        while (!s2.IsOver) s2.Apply(h.Decide(s2.Game, s2.Game.PlayersToAct()[0]));
        Save(new GwintView(s2, () => { }, _ => { }, animate: false), "gameover.png");
    }

    private static System.Windows.Media.Brush Ui_Brush(string hex) => Theme.B(hex);
}
