using System.Windows;

namespace GwentClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Settings.ApplyWindow(this);
        if (UpdateCheck.Channel == "kaermorhen") Title = "Gwent — Kaer Morhen (deneme)";
        // F11: tam ekran / pencere
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F11) return;
            Settings.Fullscreen = !Settings.Fullscreen;
            Settings.Save();
            Settings.ApplyWindow(this);
            e.Handled = true;
        };
        Closed += (_, _) => { var c = _online; _online = null; if (c != null) _ = Task.Run(() => c.DisposeAsync(leave: false).AsTask()); };   // kapanış "teslim" sayılmaz: yarım kalan maç sürdürülebilir
        // Ekran değişince müzik havuzu da değişir (ana menü / Gwent / Han Ligi / Kaer Morhen / Collect 'Em All)
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.ContentControl.ContentProperty, typeof(System.Windows.Controls.ContentControl))
            .AddValueChanged(Host, (_, _) => { if (MusicContextFor(Host.Content, Music.Context) is { } ctx) Music.SetContext(ctx); });
        ShowMenu();
        Music.TrackChanged += ShowTrackToast;
        Closed += (_, _) => Music.TrackChanged -= ShowTrackToast;
    }

    /// <summary>Parça değişince köşede kısa süre "Şimdi çalıyor" bilgisi (yavaşça belirir ve söner).</summary>
    private void ShowTrackToast()
    {
        var title = Music.CurrentTitle;
        if (title == null || !Music.Enabled) return;
        ToastText.FontFamily = Theme.Serif;
        ToastText.Text = "♪  Şimdi çalıyor: " + title;
        var anim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.Zero));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.95, TimeSpan.FromMilliseconds(500)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.95, TimeSpan.FromMilliseconds(4200)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(5200)));
        Toast.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Ekranın müzik havuzu; null = havuz değişmez (ara ekranlar: VS, deste kurucu, bekleme odaları, sonuç...). Masa, Collect 'Em All ve Kaer Morhen'de o modun havuzunda kalır.</summary>
    internal static MusicContext? MusicContextFor(object? content, MusicContext current) => content switch
    {
        null => null,
        HubView or PlayHubView or MenuView or OnlineMenuView or GuideView or ProfileView or SettingsView => MusicContext.Menu,
        GwintView => current is MusicContext.Career or MusicContext.KaerMorhen ? current : MusicContext.Gwent,
        DraftView or LobbyView or TournamentLobbyView or BracketView => MusicContext.Gwent,
        CareerView or CareerShopView or CareerTournamentView => MusicContext.Career,
        _ when content.GetType().Namespace == "GwentClient.HanLigi" => MusicContext.Han,
        _ when content.GetType().Namespace == "GwentClient.KaerMorhen" => MusicContext.KaerMorhen,
        _ => null
    };

    private string? _lastFaction;
    private bool _lastNextGen;

    /// <summary>Ana menü: dört kapılı merkez (Gwent, Çevrimiçi, Kaer Morhen, Han Ligi).</summary>
    private void ShowMenu() => Host.Content = new HubView(ShowPlayHub, ShowOnlineMenu, ShowKeep, () => ShowBuilder(_lastFaction ?? "northern_realms", _lastNextGen, ShowMenu), ShowGuide, ShowStats, ShowSettings);

    /// <summary>"Gwent" alt merkezi: Hızlı Gwent, Collect 'Em All, Draft, Deste kurucu.</summary>
    private void ShowPlayHub() => Host.Content = new PlayHubView(ShowMenu, ShowQuickGwent, ShowCareer, ShowDraft, ShowHan, ShowStats);

    private void ShowQuickGwent() => Host.Content = new MenuView(StartGame, ShowBuilder, _lastFaction, _lastNextGen, openSettings: ShowSettings, openStats: ShowStats, exit: ShowPlayHub);

    private void ShowGuide() => Host.Content = new GuideView(ShowMenu);

    private void ShowStats() => Host.Content = new ProfileView(ShowMenu);

    private void ShowSettings() => Host.Content = new SettingsView(ShowMenu);

    /// <summary>Deste kurucu Hızlı Gwent kurulumundan açılınca oraya döner; ana menüden açılınca ana menüye.</summary>
    private void ShowBuilder(string faction, bool nextGen) => ShowBuilder(faction, nextGen, ShowQuickGwent);

    private void ShowBuilder(string faction, bool nextGen, Action back)
    {
        _lastFaction = faction;
        _lastNextGen = nextGen;
        Host.Content = new DeckBuilderView(faction, nextGen, back);
    }

    private void StartGame(MatchConfig cfg)
    {
        _lastFaction = cfg.MyFaction;
        _lastNextGen = cfg.NextGen;
        var session = cfg.Daily is { } day
            ? new GameSession(cfg)
            {
                OppNameOverride = "Gaunter O'Dimm", OppAvatarOverride = "gaunter_odimm", ModeLabel = "Günün Laneti",
                Settle = won => DailySummary(day, won)
            }
            : new GameSession(cfg);
        var view = new GwintView(session, ShowMenu, StartGame);
        Music.Start();
        Host.Content = view;
        view.Focus();
    }

    /// <summary>Günün laneti bitti: sonucu (galibiyet + raund puan farkı) bugüne kaydeder, özet döner.</summary>
    private string DailySummary(DateOnly day, bool? won)
    {
        var margin = Host.Content is GwintView gv ? gv.Session.Game.History.Sum(r => r.Score0 - r.Score1) : 0;
        var better = DailyStore.Record(day, new DailyResult(won == true, margin, DateTime.Now));
        var head = won == true ? "Günün lanetini yendin!" : won == null ? "Günün laneti berabere bitti." : "Gaunter O'Dimm bu sefer kazandı.";
        return $"{head} Puan farkı {(margin > 0 ? "+" : "")}{margin}" + (better ? " · bugünün en iyi sonucu" : "") + " — arkadaşlarınla karşılaştır.";
    }

    // ---------- Online ----------

    private OnlineController? _online;
    private readonly List<(string Code, string Text)> _pendingNotices = new();

    private void ShowOnlineMenu() => Host.Content = new OnlineMenuView(ShowMenu, OnConnected);

    /// <summary>Oda kuruldu ya da odaya katıldı: bekleme odasına geç ve sunucudan gelenleri dinle.</summary>
    private void OnConnected(OnlineController c)
    {
        _online = c;
        _pendingNotices.Clear();
        c.Link.Post = a => Dispatcher.BeginInvoke(a);   // ağ iş parçacığından gelenler arayüz iş parçacığında işlenir
        c.Link.MatchStarted += OnOnlineMatchStarted;
        c.Link.Notice += OnOnlineNotice;
        c.Link.MatchEnded += OnOnlineMatchEnded;
        c.Link.TournamentChanged += OnTournamentChanged;
        c.Link.SpectateStarted += OnSpectateStarted;
        c.Link.SpectateEnded += OnSpectateEnded;
        c.Link.Pump();   // bağlanırken kuyrukta kalan iletiler (ör. yarım kalan maçın kurulumu) hemen işlensin
        if (c.IsHan)
        {
            c.Link.HanStarted += OnHanStarted;
            if (c.Link.Han is { } running) OnHanStarted(running);
            else Host.Content = new HanLigi.HanLobbyView(c, LeaveOnlineAsync);
            return;
        }
        if (c.IsTournament && c.Link.TState is { Phase: GwentNet.TournamentPhase.Running }) { if (c.Link.Match == null) ShowBracket(); }   // yarım kalan turnuva: doğrudan bracket (sürmekte olan maç varsa VS ekranı açılmıştır)
        else if (c.IsTournament) Host.Content = new TournamentLobbyView(c, LeaveOnlineAsync);
        else if (c.Link.Match == null) Host.Content = new LobbyView(c, LeaveOnlineAsync);
    }

    private async Task LeaveOnlineAsync(string? message = null)
    {
        var c = _online;
        _online = null;
        ResumeStore.ClearGuest();   // bilerek ayrılma: dönülecek maç kalmaz
        if (c is { IsHost: true, IsTournament: true }) ResumeStore.ClearHostTournament();   // kuran bilerek ayrılırsa turnuva biter
        if (c is { IsTournament: true, Link.TState: { Phase: GwentNet.TournamentPhase.Lobby } lobby }) TournamentEconomy.Refund(lobby.Code);   // başlamadan ayrılana ücret iade
        if (c != null)
        {
            c.Link.HanStarted -= OnHanStarted;
            c.Link.TournamentChanged -= OnTournamentChanged;
            c.Link.SpectateStarted -= OnSpectateStarted;
            c.Link.SpectateEnded -= OnSpectateEnded;
            c.Link.MatchStarted -= OnOnlineMatchStarted;
            c.Link.Notice -= OnOnlineNotice;
            c.Link.MatchEnded -= OnOnlineMatchEnded;
            await c.DisposeAsync();
        }
        ShowMenu();
        if (!string.IsNullOrEmpty(message)) MessageBox.Show(this, message, "Gwent", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Task LeaveOnlineAsync() => LeaveOnlineAsync(null);

    /// <summary>Sunucu maçı başlattı (ilk maç ya da rövanş): önce VS ekranı, sonra oyun.</summary>
    private void OnOnlineMatchStarted(GameSession session)
    {
        var setup = session.OnlineSetup!;
        var me = session.MySeat;
        string Nick(int seat) => setup.NickOf(seat);
        var mine = setup.SeatOf(me);
        var theirs = setup.SeatOf((me + 1) % setup.PlayerCount);
        // Üç kişide VS'nin karşı tarafı iki rakibin adını birlikte taşır (görsel sol üstteki rakibin)
        var theirName = setup.PlayerCount == 3 ? $"{Nick((me + 1) % 3)} & {Nick((me + 2) % 3)}" : Nick(1 - me);
        _pendingNotices.Clear();
        if (_online is { IsHost: false, IsTournament: false } && _online.Link is { State: { } st })
            ResumeStore.SaveGuest(new GuestResume(st.Code, st.YourToken, _online.Nick, Nick(0), DateTime.UtcNow));
        Music.Start();
        Host.Content = new VsScreen(
            new VsSideInfo(Nick(me), mine.Faction, mine.LeaderId, true, mine.Avatar),
            new VsSideInfo(theirName, theirs.Faction, theirs.LeaderId, false, theirs.Avatar ?? theirs.LeaderId),
            () => ShowOnlineGame(session), animate: true);
    }

    private void OnOnlineMatchEnded(GwentNet.MatchResult r) => ResumeStore.ClearGuest();

    /// <summary>Turnuva durumu değişti: bekleme odasında başladıysa bracket'e geç.</summary>
    private void OnTournamentChanged()
    {
        if (Host.Content is TournamentLobbyView && _online?.Link.TState is { Phase: GwentNet.TournamentPhase.Running })
            ShowBracket();
        // Konuk: turnuva sürerken anahtarını sakla (uygulama kapanırsa host'un yeni davet kodu ile geri dönebilsin); bitince sil
        if (_online is { IsHost: false, IsTournament: true } o && o.Link.TState is { } st)
        {
            if (st.Phase == GwentNet.TournamentPhase.Running)
                ResumeStore.SaveGuest(new GuestResume(st.Code, st.YourToken, o.Nick, st.Players.Count > 0 ? st.Players[0].Nick : "", DateTime.UtcNow, IsTournament: true));
            else if (st.Phase == GwentNet.TournamentPhase.Finished) ResumeStore.ClearGuest();
        }
    }

    /// <summary>İzlenen maç bitti: sonucu görmek için kısa bir bekleyişten sonra otomatik bracket'e dönülür (rövanşla yeniden başlarsa dönülmez).</summary>
    private async void OnSpectateEnded(GwentNet.MatchResult r)
    {
        var session = _online?.Link.Spectate;
        await Task.Delay(4500);
        if (session == null || _online?.Link.Spectate != session) return;
        if (Host.Content is GwintView gv && gv.Session == session) StopSpectating();
    }

    /// <summary>Canlı izleme başladı (ya da izlenen maç rövanşla yeniden başladı): geçmiş animasyonsuz uygulanır, sonra masa gösterilir.</summary>
    private async void OnSpectateStarted(GameSession session)
    {
        await Task.Delay(300);   // sunucu geçmiş eylemleri kurulumun hemen ardından yollar; hepsi gelsin
        if (_online?.Link.Spectate != session) return;
        session.FastForward();
        var view = new GwintView(session, StopSpectating, _ => { });
        Host.Content = view;
        view.Focus();
    }

    private void StopSpectating()
    {
        _ = _online?.Link.StopWatchingAsync();
        ShowBracket();
    }

    private void ShowBracket()
    {
        if (_online == null) { ShowMenu(); return; }
        Host.Content = new BracketView(_online, LeaveOnlineAsync, ShowMenu);
    }

    private void ShowOnlineGame(GameSession session)
    {
        // VS sırasında yeni bir maç başladıysa (rövanş) eski oturumu gösterme
        if (_online?.Link.Match != session) return;
        session.FastForward();   // yarım kalan maçta geçmiş animasyonsuz uygulanır
        session.TournamentMode = _online is { IsTournament: true };
        var view = new GwintView(session, session.TournamentMode ? ShowBracket : () => _ = LeaveOnlineAsync(), _ => { });
        if (_online is { IsHost: true, Invites.Count: > 0 } host) view.InviteText = host.Invites[0].Invite;
        Host.Content = view;
        view.Focus();
        // Host yarım kalan maça döndüyse rakip henüz gelmemiş olabilir: bekleme çubuğu ve davet kodu
        if (_online?.Link.State?.Players.FirstOrDefault(p => p.Seat != session.MySeat) is { Connected: false } gone)
            view.OnlineNotice("opponent_disconnected", $"{gone.Nick} henüz dönmedi.");
        foreach (var (code, text) in _pendingNotices) view.OnlineNotice(code, text);
        _pendingNotices.Clear();
    }

    private void OnOnlineNotice(string code, string text)
    {
        switch (Host.Content)
        {
            case GwintView gv:
                gv.OnlineNotice(code, text);
                break;
            case TournamentLobbyView when code is "tournament_closed" or "connection_lost":
                if (_online?.Link.TState is { } tl) TournamentEconomy.Refund(tl.Code);
                _ = LeaveOnlineAsync(code == "tournament_closed" ? "Turnuva kapandı: kuran ayrıldı. Katılım ücretin iade edildi." : text);
                break;
            case LobbyView when code is "room_closed" or "connection_lost":
                _ = LeaveOnlineAsync(code == "room_closed" ? "Oda kapandı: oda kuran oyuncu ayrıldı." : text);
                break;
            case VsScreen:
                _pendingNotices.Add((code, text));
                break;
        }
    }
}
