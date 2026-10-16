using GwentAI;
using GwentNet;
using Microsoft.AspNetCore.SignalR.Client;

namespace GwentClient;

/// <summary>Bağlantı koparsa kısa aralıklarla yeniden dener; yaklaşık iki dakika sonra vazgeçer.</summary>
internal sealed class ReconnectPolicy : IRetryPolicy
{
    private static readonly int[] Seconds = { 0, 1, 2, 3, 5, 5, 10, 10, 15, 15, 15, 20, 20 };

    public TimeSpan? NextRetryDelay(RetryContext ctx) =>
        ctx.PreviousRetryCount < Seconds.Length ? TimeSpan.FromSeconds(Seconds[ctx.PreviousRetryCount]) : null;
}

/// <summary>
/// SignalR üzerinden bir sunucuya (host'un gömülü sunucusu ya da başka biri) bağlanan oda bağlantısı.
/// Bağlantı kopup geri gelirse koltuk anahtarıyla odaya otomatik döner ve kaçırılan eylemleri alır.
/// </summary>
public sealed class NetLink : RoomLinkBase, IAsyncDisposable
{
    private readonly HubConnection _conn;
    private volatile string _code = "";
    private volatile string _token = "";
    private volatile string _tcode = "";
    private volatile string _ttoken = "";

    private NetLink(HubConnection conn)
    {
        _conn = conn;
        _conn.On<RoomState>(HubMethods.OnRoomState, s =>
        {
            _code = s.Code;
            _token = s.YourToken;
            ReceiveRoomState(s);
        });
        _conn.On<TournamentState>(HubMethods.OnTournamentState, s =>
        {
            _tcode = s.Code;
            _ttoken = s.YourToken;
            ReceiveTournamentState(s);
        });
        _conn.On<SpectateSetup>(HubMethods.OnSpectateStarted, ReceiveSpectateStarted);
        _conn.On<ActionBroadcast>(HubMethods.OnSpectateAction, ReceiveSpectateAction);
        _conn.On<MatchResult>(HubMethods.OnSpectateEnded, ReceiveSpectateEnded);
        _conn.On<MatchSetup>(HubMethods.OnMatchStarted, ReceiveMatchStarted);
        _conn.On<ActionBroadcast>(HubMethods.OnActionApplied, ReceiveActionApplied);
        _conn.On<string, int>(HubMethods.OnActionRejected, (reason, _) => ReceiveActionRejected(reason));
        _conn.On<MatchResult>(HubMethods.OnMatchEnded, ReceiveMatchEnded);
        _conn.On<string, string>(HubMethods.OnNotice, ReceiveNotice);
        _conn.Reconnecting += ex =>
        {
            ReceiveNotice("reconnecting", "Bağlantı koptu, yeniden bağlanılıyor…");
            return Task.CompletedTask;
        };
        _conn.Reconnected += async _ =>
        {
            // Yeni bağlantı yeni oturumdur: koltuk anahtarıyla odaya dön, kaçırılan eylemleri iste
            if (_code.Length == 0 && _tcode.Length == 0) { ReceiveNotice("connection_lost", "Bağlantı yenilendi ama oda bilgisi yok."); return; }
            // Turnuvadaysam önce turnuvaya dönerim (sunucu sürmekte olan maç odasına da geri sokar); yoksa doğrudan odaya
            var r = _tcode.Length > 0
                ? await InvokeAsync("RejoinTournament", _tcode, _ttoken, LastReceivedSeq).ConfigureAwait(false)
                : await InvokeAsync("Rejoin", _code, _token, LastReceivedSeq).ConfigureAwait(false);
            ReceiveNotice(r.Ok ? "reconnected" : "room_closed", r.Ok ? "Bağlantı yenilendi." : "Odaya geri dönülemedi: " + r.Error);
        };
        _conn.Closed += ex =>
        {
            ReceiveNotice("connection_lost", ex == null ? "Bağlantı kapandı." : "Bağlantı koptu: " + ex.Message);
            return Task.CompletedTask;
        };
    }

    public HubConnectionState ConnectionState => _conn.State;

    /// <param name="url">Örn. http://127.0.0.1:5000/gwent</param>
    public static async Task<NetLink> ConnectAsync(string url, CancellationToken ct = default)
    {
        // Yeni tünel adları bazı sağlayıcılarda bir dakika kadar çözülemez: tünel adreslerinde DNS-over-HTTPS yedeği kullan
        var tunnel = GwentHost.TunnelDns.IsTunnelHost(new Uri(url).Host);
        var conn = new HubConnectionBuilder()
            .WithUrl(url, o => { if (tunnel) o.HttpMessageHandlerFactory = _ => GwentHost.TunnelDns.CreateHandler(); })
            .WithAutomaticReconnect(new ReconnectPolicy())
            .Build();
        var link = new NetLink(conn);
        await conn.StartAsync(ct).ConfigureAwait(false);
        return link;
    }

    private async Task<Reply> InvokeAsync(string method, params object?[] args)
    {
        try
        {
            return args.Length switch
            {
                0 => await _conn.InvokeAsync<Reply>(method).ConfigureAwait(false),
                1 => await _conn.InvokeAsync<Reply>(method, args[0]).ConfigureAwait(false),
                2 => await _conn.InvokeAsync<Reply>(method, args[0], args[1]).ConfigureAwait(false),
                3 => await _conn.InvokeAsync<Reply>(method, args[0], args[1], args[2]).ConfigureAwait(false),
                _ => await _conn.InvokeAsync<Reply>(method, args[0], args[1], args[2], args[3], args[4]).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return Reply.Fail("Sunucuya ulaşılamadı: " + ex.Message);
        }
    }

    public override Task<Reply> CreateRoomAsync(string nick, bool nextGen = false) => InvokeAsync("CreateRoom", nick, ProtocolInfo.Version, nextGen);
    public override Task<Reply> JoinRoomAsync(string code, string nick) => InvokeAsync("JoinRoom", code, nick, ProtocolInfo.Version);
    /// <summary>Koltuk anahtarıyla odaya geri dön (yarım kalan maç): sunucu kurulumu ve tüm eylem geçmişini yeniden gönderir.</summary>
    public Task<Reply> RejoinAsync(string code, string token) => InvokeAsync("Rejoin", code, token, -1);

    public override Task<Reply> CreateTournamentAsync(string nick, bool nextGen, int maxPlayers, int entryFee) => InvokeAsync("CreateTournament", nick, ProtocolInfo.Version, nextGen, maxPlayers, entryFee);
    public override Task<Reply> JoinTournamentAsync(string code, string nick) => InvokeAsync("JoinTournament", code, nick, ProtocolInfo.Version);
    public override Task<Reply> SetTournamentLoadoutAsync(LoadoutDto loadout) => InvokeAsync("SetTournamentLoadout", loadout);
    public override Task<Reply> TournamentReadyAsync(bool ready) => InvokeAsync("TournamentSetReady", ready);
    public override Task<Reply> StartTournamentAsync() => InvokeAsync("StartTournament");
    /// <summary>Yarım kalan turnuvaya oyuncu anahtarıyla geri dön (uygulama yeniden açıldı): sürmekteki maç varsa sunucu o odaya da sokar.</summary>
    public Task<Reply> RejoinTournamentAsync(string code, string token) => InvokeAsync("RejoinTournament", code, token, -1);

    public override Task<Reply> SetTournamentRulesAsync(TournamentRules rules) => InvokeAsync("SetTournamentRules", rules);
    public override Task<Reply> WatchMatchAsync(int round, int index) => InvokeAsync("WatchMatch", round, index);
    public override Task<Reply> StopWatchingAsync() => InvokeAsync("StopWatching");
    public override Task<Reply> TournamentWalkoverAsync() => InvokeAsync("TournamentClaimWalkover");

    public override Task<Reply> SetLoadoutAsync(LoadoutDto loadout) => InvokeAsync("SetLoadout", loadout);
    public override Task<Reply> SetReadyAsync(bool ready) => InvokeAsync("SetReady", ready);
    public override Task<Reply> SetDraftAsync(bool on) => InvokeAsync("SetDraft", on);
    public override Task<Reply> DraftChooseAsync(string kind, string id, string? avatar) => InvokeAsync("DraftChoose", kind, id, avatar);
    public override Task<Reply> SetCursesAsync(List<string> curses) => InvokeAsync("SetCurses", curses);

    public override async Task LeaveAsync()
    {
        try { await _conn.InvokeAsync("Leave").ConfigureAwait(false); } catch { /* bağlantı zaten kopmuş olabilir */ }
    }

    public override void Submit(ReplayAction action) => _ = SubmitAsync(action);

    private async Task SubmitAsync(ReplayAction action)
    {
        var r = await InvokeAsync("SubmitAction", action).ConfigureAwait(false);
        if (!r.Ok && r.Error != null && r.Error.StartsWith("Sunucuya ulaşılamadı", StringComparison.Ordinal)) ReceiveActionRejected(r.Error);
        // Sunucu reddederse ayrıca OnActionRejected gelir
    }

    public override void Resign() => _ = InvokeAsync("Resign");
    public override void RequestRematch() => _ = InvokeAsync("RequestRematch");
    public override void ClaimWin() => _ = InvokeAsync("ClaimWin");

    public async ValueTask DisposeAsync()
    {
        try { await _conn.DisposeAsync().ConfigureAwait(false); } catch { }
    }
}
