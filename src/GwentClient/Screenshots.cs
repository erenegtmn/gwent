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
        Save(new MenuView(_ => { }, (_, _) => { }, animate: false, openSettings: () => { }, openOnline: () => { }, openStats: () => { }, openGuide: () => { }, openCareer: () => { }), "menu.png");
        UpdateCheck.SetResult(new ReleaseInfo("v0.7", new Version(0, 7, 0), UpdateCheck.ReleasesPage));
        Save(new MenuView(_ => { }, (_, _) => { }, animate: false, openSettings: () => { }, openOnline: () => { }, openStats: () => { }, openGuide: () => { }, openCareer: () => { }), "menu_update.png");
        UpdateCheck.SetResult(null);
        Ui.RenderToPng(new MenuView(_ => { }, (_, _) => { }, animate: false, openSettings: () => { }, openOnline: () => { }, openStats: () => { }, openGuide: () => { }, openCareer: () => { }), 1440, 860, System.IO.Path.Combine(dir, "menu_small.png"));
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
        for (var gi = 0; gi < 7; gi++) Save(new GuideView(() => { }, gi), $"guide_{gi + 1}.png");
        {
            // Kariyer: başlangıç, rakip kartı, ilerlemiş kayıt (Velen, Novigrad turnuvası), ödül, tüccar, kariyer destesi, tüm bölgeler
            var nav = new CareerNav(_ => { }, () => { }, _ => { }, () => { }, () => { });
            var cs = GwentAI.Career.CareerLogic.NewSave();
            Save(new CareerView(cs, nav), "career_start.png");
            Save(new CareerView(cs, nav, null, "tutorialDeck"), "career_detail.png");
            var adv = GwentAI.Career.CareerLogic.NewSave();
            var rng = new Random(3);
            foreach (var o in GwentAI.Career.CareerWorld.In("white_orchard")) GwentAI.Career.CareerLogic.ApplyDuel(adv, o, true, rng);
            foreach (var o in GwentAI.Career.CareerWorld.In("velen").Take(4)) GwentAI.Career.CareerLogic.ApplyDuel(adv, o, true, rng);
            adv.LastRegion = "velen";
            Save(new CareerView(adv, nav, null, "Baron"), "career_velen.png");
            adv.LastRegion = "novigrad";
            Save(new CareerView(adv, nav, null, "passiflora"), "career_novigrad.png");
            {
                // Turnuva: giriş uyarısı (onay adımı) ve turlar arası ekran
                var tv = new CareerView(adv, nav, null, "passiflora");
                typeof(CareerView).GetField("_confirmEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(tv, "passiflora");
                typeof(CareerView).GetMethod("Draw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(tv, null);
                Save(tv, "career_tournament_confirm.png");
                var run = GwentAI.Career.CareerLogic.NewSave();
                run.Crowns = 300;
                GwentAI.Career.CareerLogic.EnterTournament(run, GwentAI.Career.CareerWorld.Opponent("passiflora"), new Random(2));
                run.Tournament!.Round = 1;
                Save(new CareerTournamentView(run, () => { }, () => { }, "1. turu kazandın! Sıradaki: 2. tur."), "career_tournament_round.png");
            }
            var rew = GwentAI.Career.CareerLogic.ApplyDuel(adv, GwentAI.Career.CareerWorld.Opponent("Thaler"), true, rng);
            Save(new CareerView(adv, nav, rew), "career_reward.png");
            Save(new CareerShopView(adv, GwentAI.Career.CareerWorld.Region("velen"), () => { }), "career_shop.png");
            Save(new DeckBuilderView("northern_realms", false, () => { }, adv), "career_deck.png");
            var all = GwentAI.Career.CareerLogic.NewSave();
            foreach (var o in GwentAI.Career.CareerWorld.Opponents.Where(o => !o.IsTournament)) GwentAI.Career.CareerLogic.ApplyDuel(all, o, true, rng);
            foreach (var r in new[] { "skellige", "hearts_of_stone", "toussaint" })
            {
                all.LastRegion = r;
                Save(new CareerView(all, nav), $"career_{r}.png");
            }
        }
        // Online: giriş ekranı, bekleme odası (iki oyuncu), oyun içi onay ve rakip koptu çubuğu
        Save(new OnlineMenuView(() => { }, _ => { }), "online_menu.png");
        {
            // Yarım kalan maç şeridi: geçici bir kayıtla çizilir
            var rdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-shot-resume-" + Guid.NewGuid().ToString("N"));
            ResumeStore.DirOverride = rdir;
            ResumeStore.Enabled = true;
            var setup = new GwentNet.MatchSetup(5, false, new GwentNet.LoadoutDto("northern_realms", "foltest_the_siegemaster"), new GwentNet.LoadoutDto("monsters", "eredin_commander_of_the_red_riders"), "Geralt", "Yennefer", 0);
            ResumeStore.OnHostRoomChanged("ABCDE", new GwentNet.RoomSnapshot("ABCDE", false, setup, Enumerable.Range(0, 23).Select(_ => new GwentAI.ReplayAction()).ToList(), "t0", "t1", DateTime.UtcNow));
            ResumeStore.SaveGuest(new GuestResume("ABCDE", "t1", "Yennefer", "Geralt", DateTime.UtcNow));
            for (var i = 0; i < 50 && ResumeStore.LoadHost() == null; i++) System.Threading.Thread.Sleep(20);
            Save(new OnlineMenuView(() => { }, _ => { }), "online_resume.png");
            ResumeStore.ClearHost();
            ResumeStore.ClearGuest();
            ResumeStore.Enabled = false;
            ResumeStore.DirOverride = null;
            try { System.IO.Directory.Delete(rdir, true); } catch { }
        }
        {
            // Turnuva: bekleme odası, 8 kişilik bracket ve biten 4 kişilik turnuva
            var lobbyPlayers = UiSelfTest.OpenTournament(5, 8, 100, ready: false);
            var lobbyHost = lobbyPlayers[0].Link;
            UiSelfTest.Await(() => lobbyPlayers[1].Link.TournamentReadyAsync(true));
            UiSelfTest.Await(() => lobbyPlayers[3].Link.TournamentReadyAsync(true));
            UiSelfTest.PumpUntil(() => lobbyHost.TState!.Players.Count(p => p.Ready) == 2, "hazır", lobbyPlayers.Select(c => c.Link).ToArray());
            var tl = new TournamentLobbyView(lobbyPlayers[0], () => Task.CompletedTask);
            UiSelfTest.Layout(tl);
            Save(tl, "tourney_lobby.png");
            UiSelfTest.CloseAll(lobbyPlayers);

            var eight = UiSelfTest.OpenTournament(8, 8, 100, ready: true);
            UiSelfTest.Await(() => eight[0].Link.StartTournamentAsync());
            UiSelfTest.PumpUntil(() => eight[0].Link.TState!.Phase == GwentNet.TournamentPhase.Running, "başladı", eight.Select(c => c.Link).ToArray());
            // bir eşleşmede iki taraf da hazır olsun (kart "oynanıyor" görünsün)
            var m0 = eight[0].Link.TState!.Matches.First(m => m.Round == 0 && m.Index == 0);
            UiSelfTest.Await(() => eight[m0.A!.Value].Link.TournamentReadyAsync(true));
            var m1 = eight[0].Link.TState!.Matches.First(m => m.Round == 0 && m.Index == 1);
            UiSelfTest.Await(() => eight[m1.A!.Value].Link.TournamentReadyAsync(true));
            UiSelfTest.PumpUntil(() => eight[0].Link.TState!.Matches.Any(m => m.ReadyA), "hazır", eight.Select(c => c.Link).ToArray());
            var viewer = eight[m1.A!.Value];
            var tb = new BracketView(viewer, () => Task.CompletedTask, () => { });
            UiSelfTest.Layout(tb);
            Save(tb, "tourney_bracket.png");
            UiSelfTest.CloseAll(eight);

            // Kurallar paneli (host): tek fraksiyon
            var rp = UiSelfTest.OpenTournament(4, 4, 0, ready: false);
            UiSelfTest.Await(() => rp[0].Link.SetTournamentRulesAsync(new GwentNet.TournamentRules(GwentNet.TournamentRuleKind.Faction, "skellige", null, false)));
            UiSelfTest.PumpUntil(() => rp[0].Link.TState!.Rules!.Kind == GwentNet.TournamentRuleKind.Faction, "kural", rp.Select(c => c.Link).ToArray());
            var rv = new TournamentLobbyView(rp[0], () => Task.CompletedTask);
            UiSelfTest.Layout(rv);
            Save(rv, "tourney_rules.png");
            var rv2 = new TournamentLobbyView(rp[1], () => Task.CompletedTask);
            UiSelfTest.Layout(rv2);
            Save(rv2, "tourney_rules_player.png");
            UiSelfTest.CloseAll(rp);

            // Canlı özet ve izleme: iki oyuncu maçta, üçüncüsü izler
            foreach (var hands in new[] { true, false })
            {
                var lp = UiSelfTest.OpenTournament(4, 4, 0, ready: false);
                var llinks = lp.Select(c => c.Link).ToArray();
                UiSelfTest.Await(() => lp[0].Link.SetTournamentRulesAsync(new GwentNet.TournamentRules(SpectatorHands: hands)));
                foreach (var c in lp) UiSelfTest.Await(() => c.Link.TournamentReadyAsync(true));
                UiSelfTest.PumpUntil(() => lp.All(c => c.Link.TState!.Players.All(p => p.Ready)), "hazır", llinks);
                UiSelfTest.Await(() => lp[0].Link.StartTournamentAsync());
                UiSelfTest.PumpUntil(() => lp.All(c => c.Link.TState!.Phase == GwentNet.TournamentPhase.Running), "başladı", llinks);
                var lm = lp[0].Link.TState!.Matches.First(x => x.Round == 0 && x.Index == 0);
                var pa = lp[lm.A!.Value]; var pb = lp[lm.B!.Value];
                var lm2 = lp[0].Link.TState!.Matches.First(x => x.Round == 0 && x.Index == 1);
                UiSelfTest.Await(() => pa.Link.TournamentReadyAsync(true));
                UiSelfTest.Await(() => pb.Link.TournamentReadyAsync(true));
                UiSelfTest.PumpUntil(() => pa.Link.Match != null && pb.Link.Match != null, "maç", llinks);
                UiSelfTest.StepDuel(pa, pb, 14);
                var watcher = lp.First(c => c != pa && c != pb);
                var liveView = new BracketView(watcher, () => Task.CompletedTask, () => { });
                UiSelfTest.Layout(liveView);
                if (hands) Save(liveView, "tourney_live.png");
                UiSelfTest.Await(() => watcher.Link.WatchMatchAsync(0, 0));
                UiSelfTest.PumpUntil(() => watcher.Link.Spectate != null, "izleme", llinks);
                System.Threading.Thread.Sleep(200);
                watcher.Link.Pump();
                var sp = watcher.Link.Spectate!;
                sp.FastForward();
                var sview = new GwintView(sp, () => { }, _ => { }, animate: false);
                UiSelfTest.Layout(sview);
                Save(sview, hands ? "spectate.png" : "spectate_hidden.png");
                UiSelfTest.CloseAll(lp);
            }

            var four = UiSelfTest.OpenTournament(4, 4, 100, ready: true);
            UiSelfTest.Await(() => four[0].Link.StartTournamentAsync());
            UiSelfTest.PlayTournament(four);
            var tf = new BracketView(four[0], () => Task.CompletedTask, () => { });
            UiSelfTest.Layout(tf);
            Save(tf, "tourney_final.png");
            UiSelfTest.CloseAll(four);
        }
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
