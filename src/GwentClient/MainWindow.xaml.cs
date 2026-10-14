using System.Windows;

namespace GwentClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Settings.ApplyWindow(this);
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
        ShowMenu();
    }

    private string? _lastFaction;
    private bool _lastNextGen;

    private void ShowMenu() => Host.Content = new MenuView(StartGame, ShowBuilder, _lastFaction, _lastNextGen, openSettings: ShowSettings, openStats: ShowStats, openOnline: ShowOnlineMenu);

    private void ShowStats() => Host.Content = new ProfileView(ShowMenu);

    private void ShowSettings() => Host.Content = new SettingsView(ShowMenu);

    private void ShowBuilder(string faction, bool nextGen)
    {
        _lastFaction = faction;
        _lastNextGen = nextGen;
        Host.Content = new DeckBuilderView(faction, nextGen, ShowMenu);
    }

    private void StartGame(MatchConfig cfg)
    {
        _lastFaction = cfg.MyFaction;
        _lastNextGen = cfg.NextGen;
        var view = new GwintView(new GameSession(cfg), ShowMenu, StartGame);
        Music.Start();
        Host.Content = view;
        view.Focus();
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
        c.Link.Pump();   // bağlanırken kuyrukta kalan iletiler (ör. yarım kalan maçın kurulumu) hemen işlensin
        if (c.Link.Match == null) Host.Content = new LobbyView(c, LeaveOnlineAsync);
    }

    private async Task LeaveOnlineAsync(string? message = null)
    {
        var c = _online;
        _online = null;
        ResumeStore.ClearGuest();   // bilerek ayrılma: dönülecek maç kalmaz
        if (c != null)
        {
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
        string Nick(int seat) => seat == 0 ? setup.Nick0 : setup.Nick1;
        var mine = me == 0 ? setup.Seat0 : setup.Seat1;
        var theirs = me == 0 ? setup.Seat1 : setup.Seat0;
        _pendingNotices.Clear();
        if (_online is { IsHost: false } && _online.Link is { State: { } st })
            ResumeStore.SaveGuest(new GuestResume(st.Code, st.YourToken, _online.Nick, Nick(1 - me), DateTime.UtcNow));
        Music.Start();
        Host.Content = new VsScreen(
            new VsSideInfo(Nick(me), mine.Faction, mine.LeaderId, true, mine.Avatar),
            new VsSideInfo(Nick(1 - me), theirs.Faction, theirs.LeaderId, false, theirs.Avatar ?? theirs.LeaderId),
            () => ShowOnlineGame(session), animate: true);
    }

    private void OnOnlineMatchEnded(GwentNet.MatchResult r) => ResumeStore.ClearGuest();

    private void ShowOnlineGame(GameSession session)
    {
        // VS sırasında yeni bir maç başladıysa (rövanş) eski oturumu gösterme
        if (_online?.Link.Match != session) return;
        session.FastForward();   // yarım kalan maçta geçmiş animasyonsuz uygulanır
        var view = new GwintView(session, () => _ = LeaveOnlineAsync(), _ => { });
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
            case LobbyView when code is "room_closed" or "connection_lost":
                _ = LeaveOnlineAsync(code == "room_closed" ? "Oda kapandı: oda kuran oyuncu ayrıldı." : text);
                break;
            case VsScreen:
                _pendingNotices.Add((code, text));
                break;
        }
    }
}
