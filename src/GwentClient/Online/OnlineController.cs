using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GwentHost;
using GwentNet;

namespace GwentClient;

/// <summary>Oda kurarken seçenekler: yerel ağ, UPnP (modemde port açma), tünel (cloudflared yolu; null = tünel yok).</summary>
public sealed record HostOptions(bool NextGen, bool Lan, bool Upnp, string? CloudflaredPath);

/// <summary>Host'un arkadaşına verebileceği bir davet: <c>Kind</c> lan | upnp | tunnel.</summary>
public sealed record InviteOption(string Kind, string Label, string Url, string Invite);

/// <summary>
/// Bir online oturumun sahibi: host ise gömülü sunucuyu, onu internete açan tüneli/port eşlemesini ve odayı; konuk ise uzak odaya bağlantıyı tutar.
/// Arayüz (menü, bekleme odası, maç) bu nesneyi taşır; kapatınca her şey temizlenir (tünel kapanır, modemdeki port kaldırılır).
/// </summary>
public sealed class OnlineController : IAsyncDisposable
{
    private readonly CloudflareTunnel? _tunnel;
    private readonly UpnpMapping? _mapping;

    private OnlineController(NetLink link, LocalServer? server, string url, CloudflareTunnel? tunnel = null, UpnpMapping? mapping = null)
    {
        Link = link;
        Server = server;
        Url = url;
        _tunnel = tunnel;
        _mapping = mapping;
    }

    public NetLink Link { get; }
    public LocalServer? Server { get; }
    public string Url { get; }
    public bool IsHost => Server != null;
    public string Nick { get; private set; } = "";

    /// <summary>Host'un arkadaşlarına verebileceği davetler (en iyisi başta: tünel, UPnP, yerel ağ).</summary>
    public IReadOnlyList<InviteOption> Invites { get; private set; } = Array.Empty<InviteOption>();

    /// <summary>Denenip başarısız olan bağlantı yöntemlerinin nedenleri (kullanıcıya gösterilir).</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

    /// <summary>Host'un yerel ağda arkadaşlara verebileceği "adres:port" (ağ kartı başına bir tane).</summary>
    public IReadOnlyList<string> LanAddresses => Server == null ? Array.Empty<string>() : LocalIps().Select(ip => $"{ip}:{Server.Port}").ToList();

    // ---------- Kurma ----------

    /// <summary>Eski kısa biçim (sınamalar): yalnız yerel ağ.</summary>
    public static Task<(OnlineController? Controller, string? Error)> HostAsync(string nick, bool nextGen, bool lan = true) =>
        HostAsync(nick, new HostOptions(nextGen, lan, false, null), null);

    /// <summary>
    /// Oda kurar: gömülü sunucuyu başlatır, kendisi ona bağlanır, odayı açar; sonra istenen yollarla (UPnP, tünel) internete açar.
    /// UPnP/tünel başarısız olursa oda yine kurulur (yerel ağ çalışır); nedenler <see cref="Warnings"/> içinde.
    /// </summary>
    public static Task<(OnlineController? Controller, string? Error)> HostAsync(string nick, HostOptions opts, IProgress<string>? progress) =>
        HostCoreAsync(nick, opts, progress, null);

    /// <summary>
    /// Yarım kalan maçı kayıttan sürdürür: oda aynı kod ve koltuk anahtarlarıyla yeniden kurulur, host kendi koltuğuna döner.
    /// Tünel adresi her açılışta değiştiği için konuk, yeni davet kodunu yapıştırır (kod içindeki oda kodu aynı kalır).
    /// </summary>
    public static Task<(OnlineController? Controller, string? Error)> ResumeHostAsync(RoomSnapshot snapshot, HostOptions opts, IProgress<string>? progress) =>
        HostCoreAsync(snapshot.Setup.Nick0, opts with { NextGen = snapshot.NextGen }, progress, snapshot);

    private static async Task<(OnlineController? Controller, string? Error)> HostCoreAsync(string nick, HostOptions opts, IProgress<string>? progress, RoomSnapshot? resume)
    {
        LocalServer? server = null;
        NetLink? link = null;
        CloudflareTunnel? tunnel = null;
        UpnpMapping? mapping = null;
        try
        {
            // Dışarıdan erişim (yerel ağ ya da UPnP) isteniyorsa 0.0.0.0; yalnız tünel varsa 127.0.0.1 yeter (güvenlik duvarı sormaz)
            server = await LocalServer.StartAsync(0, opts.Lan || opts.Upnp).ConfigureAwait(false);
            link = await NetLink.ConnectAsync(server.LocalUrl).ConfigureAwait(false);
            server.Rooms.RoomPersisted += ResumeStore.OnHostRoomChanged;   // devam eden maç diske yazılır (uygulama kapanırsa sürdürülebilsin)
            Reply r;
            if (resume == null) r = await link.CreateRoomAsync(nick, opts.NextGen).ConfigureAwait(false);
            else
            {
                r = server.Rooms.RestoreRoom(resume);
                if (r.Ok) r = await link.RejoinAsync(resume.Code, resume.Token0).ConfigureAwait(false);
            }
            if (!r.Ok) { await CleanupAsync(link, server, null, null).ConfigureAwait(false); return (null, r.Error); }
            var roomCode = r.Value!;

            var invites = new List<InviteOption>();
            var warnings = new List<string>();

            if (opts.CloudflaredPath != null)
            {
                progress?.Report("İnternet tüneli açılıyor…");
                var (t, terr) = await CloudflareTunnel.StartAsync(server.Port, opts.CloudflaredPath, TimeSpan.FromSeconds(40)).ConfigureAwait(false);
                if (t != null)
                {
                    tunnel = t;
                    var url = t.PublicUrl + HubMethods.Path;
                    invites.Add(new InviteOption("tunnel", "İnternet (tünel) — arkadaşın hiçbir şey kurmaz", url, InviteCodes.Encode(url, roomCode)));
                }
                else warnings.Add("Tünel açılamadı: " + terr);
            }

            if (opts.Upnp)
            {
                progress?.Report("Modemde port açılıyor (UPnP)…");
                var (m, uerr) = await Upnp.TryMapAsync(server.Port).ConfigureAwait(false);
                if (m == null) warnings.Add("UPnP: " + uerr);
                else if (m.BehindCgnat)
                {
                    warnings.Add($"UPnP: modemin dış adresi ({m.ExternalIp}) özel/CGNAT aralığında; internetten ulaşılamaz.");
                    await m.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    mapping = m;
                    var url = $"http://{m.ExternalIp}:{m.ExternalPort}{HubMethods.Path}";
                    invites.Add(new InviteOption("upnp", "İnternet (modemde port açıldı)", url, InviteCodes.Encode(url, roomCode)));
                }
            }

            if (opts.Lan || opts.Upnp)
            {
                foreach (var ip in LocalIps())
                {
                    var url = $"http://{ip}:{server.Port}{HubMethods.Path}";
                    invites.Add(new InviteOption("lan", $"Yerel ağ ({ip})", url, InviteCodes.Encode(url, roomCode)));
                }
            }

            return (new OnlineController(link, server, server.LocalUrl, tunnel, mapping) { Nick = nick, Invites = invites, Warnings = warnings }, null);
        }
        catch (Exception ex)
        {
            await CleanupAsync(link, server, tunnel, mapping).ConfigureAwait(false);
            return (null, "Sunucu başlatılamadı: " + ex.Message);
        }
    }

    /// <summary>Davet koduyla odaya katılır (adres ve oda kodu kodun içindedir).</summary>
    public static Task<(OnlineController? Controller, string? Error)> JoinInviteAsync(string inviteText, string nick, IProgress<string>? progress = null)
    {
        if (!InviteCodes.TryDecode(inviteText, out var invite, out var error)) return Task.FromResult<(OnlineController?, string?)>((null, error));
        return JoinAsync(invite.Url, invite.RoomCode, nick, progress);
    }

    /// <summary>Uzaktaki bir odaya katılır. <paramref name="address"/> "adres:port", "http://adres:port" ya da tam hub adresi olabilir.</summary>
    public static async Task<(OnlineController? Controller, string? Error)> JoinAsync(string address, string code, string nick, IProgress<string>? progress = null)
    {
        string url;
        try { url = NormalizeUrl(address); }
        catch (UriFormatException ex) { return (null, "Adres geçersiz: " + ex.Message); }

        // Yeni açılan tünel adresi (DNS) birkaç on saniye boyunca yayılmamış olabilir: tünellerde bir dakika boyunca dene
        var isTunnel = new Uri(url).Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
        var attempts = isTunnel ? 12 : 1;
        string? lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            NetLink? link = null;
            try
            {
                if (isTunnel && attempt > 1) progress?.Report($"Tünel adresi henüz yayılıyor, bekleniyor… (deneme {attempt}/{attempts})");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(isTunnel ? 15 : 10));
                link = await NetLink.ConnectAsync(url, cts.Token).ConfigureAwait(false);
                // Aynı odadan yarım kalan bir maçım varsa yeniden katılmak yerine koltuğuma geri dönerim
                var roomCode = code.Trim().ToUpperInvariant();
                var saved = ResumeStore.LoadGuest();
                var resuming = saved != null && string.Equals(saved.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase);
                var r = resuming
                    ? await link.RejoinAsync(roomCode, saved!.Token).ConfigureAwait(false)
                    : await link.JoinRoomAsync(roomCode, nick).ConfigureAwait(false);
                if (!r.Ok)
                {
                    await CleanupAsync(link, null, null, null).ConfigureAwait(false);
                    if (resuming) ResumeStore.ClearGuest();   // oda artık yok ya da anahtar geçersiz: eski kayıt işe yaramaz
                    return (null, resuming ? "Yarım kalan maça dönülemedi: " + r.Error : r.Error);
                }
                return (new OnlineController(link, null, url) { Nick = nick }, null);
            }
            catch (Exception ex)
            {
                await CleanupAsync(link, null, null, null).ConfigureAwait(false);
                lastError = ex is OperationCanceledException
                    ? (isTunnel ? "Tünele ulaşılamadı. Host'un oyunu açık mı, davet kodu güncel mi?" : "Sunucuya 10 saniyede ulaşılamadı. Adres ve port doğru mu, host oyunu açık mı?")
                    : "Bağlanılamadı: " + ex.Message;
                if (attempt < attempts) await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        return (null, lastError);
    }

    // ---------- Yardımcılar ----------

    /// <summary>Kullanıcının yazdığı adresi hub adresine çevirir: "192.168.1.5:5000" → "http://192.168.1.5:5000/gwent".</summary>
    public static string NormalizeUrl(string address)
    {
        var a = address.Trim();
        if (a.Length == 0) throw new UriFormatException("Adres boş.");
        if (!a.Contains("://", StringComparison.Ordinal)) a = "http://" + a;
        var uri = new Uri(a, UriKind.Absolute);
        var path = uri.AbsolutePath is "" or "/" ? HubMethods.Path : uri.AbsolutePath;
        return new UriBuilder(uri) { Path = path }.Uri.ToString();
    }

    /// <summary>Yerel ağdaki IPv4 adresleri (sanal ağ kartları ve APIPA hariç).</summary>
    public static IEnumerable<string> LocalIps()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                yield return ip;
            }
        }
    }

    private static async Task CleanupAsync(NetLink? link, LocalServer? server, CloudflareTunnel? tunnel, UpnpMapping? mapping)
    {
        if (link != null) await link.DisposeAsync().ConfigureAwait(false);
        if (tunnel != null) await tunnel.DisposeAsync().ConfigureAwait(false);
        if (mapping != null) await mapping.DisposeAsync().ConfigureAwait(false);
        if (server != null) await server.DisposeAsync().ConfigureAwait(false);
    }

    public async Task LeaveAsync()
    {
        try { await Link.LeaveAsync().ConfigureAwait(false); } catch { }
    }

    public ValueTask DisposeAsync() => DisposeAsync(leave: true);

    /// <param name="leave">false: odadan ayrılma bildirilmez, yalnız bağlantı kapanır (uygulama kapanırken: maç yarım kalır ve sürdürülebilir).</param>
    public async ValueTask DisposeAsync(bool leave)
    {
        if (leave) await LeaveAsync().ConfigureAwait(false);
        await CleanupAsync(Link, Server, _tunnel, _mapping).ConfigureAwait(false);
    }
}
