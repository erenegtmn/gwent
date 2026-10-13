using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

[assembly: InternalsVisibleTo("GwentHost.Tests")]

namespace GwentHost;

/// <summary>Modemde açılmış bir port eşlemesi. <see cref="DisposeAsync"/> eşlemeyi kaldırır.</summary>
public sealed class UpnpMapping : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _control;
    private readonly string _serviceType;
    private readonly string _description;
    private readonly int _internalPort;
    private readonly Timer _renew;
    private bool _removed;

    internal UpnpMapping(HttpClient http, Uri control, string serviceType, string externalIp, int externalPort, string internalIp, int internalPort, string description)
    {
        _http = http;
        _control = control;
        _serviceType = serviceType;
        ExternalIp = externalIp;
        ExternalPort = externalPort;
        InternalIp = internalIp;
        _internalPort = internalPort;
        _description = description;
        // Kira süresi sınırlıdır (çöken uygulama modemi açık bırakmasın); açık kaldığı sürece yenilenir
        _renew = new Timer(_ => _ = Upnp.AddMappingAsync(_http, _control, _serviceType, ExternalPort, _internalPort, InternalIp, _description, Upnp.LeaseSeconds), null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public string ExternalIp { get; }
    public int ExternalPort { get; }
    public string InternalIp { get; }

    /// <summary>Modemin dış adresi özel/CGNAT aralığındaysa internetten ulaşılamaz (operatör adres paylaştırıyor).</summary>
    public bool BehindCgnat => IPAddress.TryParse(ExternalIp, out var ip) && Upnp.IsPrivateOrCgnat(ip);

    public async ValueTask DisposeAsync()
    {
        if (_removed) return;
        _removed = true;
        await _renew.DisposeAsync().ConfigureAwait(false);
        try { await Upnp.DeleteMappingAsync(_http, _control, _serviceType, ExternalPort).ConfigureAwait(false); }
        catch { /* modem yanıt vermezse kira süresi kendiliğinden biter */ }
        _http.Dispose();
    }
}

/// <summary>
/// Küçük bir UPnP istemcisi (SSDP bulma + SOAP): modemden bir TCP portunu bu bilgisayara yönlendirmesini ister.
/// Ek paket gerektirmez. Her modem desteklemez ve operatör adres paylaştırıyorsa (CGNAT) işe yaramaz; çağıran tarafa bunu söyler.
/// </summary>
public static class Upnp
{
    internal const int LeaseSeconds = 7200;
    private const string SearchTarget = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";

    /// <summary>Modem arar ve <paramref name="internalPort"/> için dış port açar.</summary>
    public static async Task<(UpnpMapping? Mapping, string? Error)> TryMapAsync(int internalPort, string description = "Gwent", CancellationToken ct = default)
    {
        try
        {
            using var discoverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            discoverCts.CancelAfter(TimeSpan.FromSeconds(6));
            var locations = await DiscoverAsync(TimeSpan.FromSeconds(2.5), discoverCts.Token).ConfigureAwait(false);
            if (locations.Count == 0) return (null, "Modem bulunamadı ya da UPnP kapalı.");

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var loc in locations)
            {
                try
                {
                    var xml = await http.GetStringAsync(loc, discoverCts.Token).ConfigureAwait(false);
                    var info = ParseControlInfo(xml, loc);
                    if (info == null) continue;
                    var (control, type) = info.Value;
                    var ext = await GetExternalIpAsync(http, control, type).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(ext)) continue;
                    var local = LocalIpFor(new Uri(loc.GetLeftPart(UriPartial.Authority)));
                    if (local == null) continue;

                    // aynı portu dene; çakışırsa rastgele yüksek portlar
                    var rng = new Random();
                    for (var attempt = 0; attempt < 6; attempt++)
                    {
                        var port = attempt == 0 ? internalPort : rng.Next(49152, 65535);
                        var error = await AddMappingAsync(http, control, type, port, internalPort, local, description, LeaseSeconds).ConfigureAwait(false);
                        if (error == null) return (new UpnpMapping(http, control, type, ext, port, local, internalPort, description), null);
                        if (error != "718") return (null, "Modem port açmayı reddetti (hata " + error + ").");
                    }
                    return (null, "Modemde boş port bulunamadı.");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException) { /* sıradaki aygıt */ }
            }
            http.Dispose();
            return (null, "Modem UPnP isteklerine yanıt vermedi.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            return (null, "Modem aranırken zaman aşımı.");
        }
    }

    /// <summary>
    /// Yoklama: modemi bulur ve dış adresini sorar; **hiçbir port açmaz** (teşhis için). Modem yoksa ya da yanıt vermezse bulunamadı döner.
    /// </summary>
    public static async Task<(bool Found, string? ExternalIp, bool BehindCgnat, string? Detail)> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var locations = await DiscoverAsync(TimeSpan.FromSeconds(2.5), cts.Token).ConfigureAwait(false);
            if (locations.Count == 0) return (false, null, false, "SSDP yanıtı yok (UPnP kapalı olabilir).");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var loc in locations)
            {
                try
                {
                    var info = ParseControlInfo(await http.GetStringAsync(loc, cts.Token).ConfigureAwait(false), loc);
                    if (info == null) continue;
                    var ip = await GetExternalIpAsync(http, info.Value.Control, info.Value.ServiceType).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(ip)) continue;
                    return (true, ip, IPAddress.TryParse(ip, out var a) && IsPrivateOrCgnat(a), loc.ToString());
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException) { }
            }
            return (false, null, false, "Modem bulundu ama dış adres sorgusuna yanıt vermedi.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException) { return (false, null, false, "Zaman aşımı."); }
    }

    // ---------- SSDP ----------

    private static async Task<List<Uri>> DiscoverAsync(TimeSpan listenFor, CancellationToken ct)
    {
        var found = new List<Uri>();
        var request = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + SearchTarget + "\r\n\r\n");
        var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

        var locals = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => a.Address).ToList();

        var tasks = locals.Select(async ip =>
        {
            using var udp = new UdpClient(new IPEndPoint(ip, 0));
            await udp.SendAsync(request, request.Length, target).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(listenFor);
            try
            {
                while (true)
                {
                    var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    var loc = ParseSsdpLocation(Encoding.ASCII.GetString(r.Buffer));
                    if (loc != null) lock (found) if (!found.Contains(loc)) found.Add(loc);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return found;
    }

    internal static Uri? ParseSsdpLocation(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(t[9..].Trim(), UriKind.Absolute, out var u) && u.Scheme == "http")
                return u;
        }
        return null;
    }

    // ---------- Aygıt tanımı ----------

    /// <summary>Aygıt XML'inden WAN bağlantı hizmetinin kontrol adresini ve hizmet türünü çıkarır.</summary>
    internal static (Uri Control, string ServiceType)? ParseControlInfo(string xml, Uri location)
    {
        var doc = XDocument.Parse(xml);
        var baseText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value;
        var baseUri = Uri.TryCreate(baseText, UriKind.Absolute, out var b) ? b : new Uri(location.GetLeftPart(UriPartial.Authority));
        foreach (var svc in doc.Descendants().Where(e => e.Name.LocalName == "service"))
        {
            var type = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value?.Trim();
            var control = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value?.Trim();
            if (type == null || control == null) continue;
            if (!type.Contains("WANIPConnection", StringComparison.OrdinalIgnoreCase) && !type.Contains("WANPPPConnection", StringComparison.OrdinalIgnoreCase)) continue;
            return (new Uri(baseUri, control), type);
        }
        return null;
    }

    // ---------- SOAP ----------

    private static async Task<(string Body, bool Ok)> SoapAsync(HttpClient http, Uri control, string serviceType, string action, string args)
    {
        var body = $"<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                   $"<s:Body><u:{action} xmlns:u=\"{serviceType}\">{args}</u:{action}></s:Body></s:Envelope>";
        using var req = new HttpRequestMessage(HttpMethod.Post, control) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#{action}\"");
        using var res = await http.SendAsync(req).ConfigureAwait(false);
        return (await res.Content.ReadAsStringAsync().ConfigureAwait(false), res.IsSuccessStatusCode);
    }

    internal static string? ExtractTag(string xml, string tag)
    {
        try { return XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == tag)?.Value; }
        catch (System.Xml.XmlException) { return null; }
    }

    private static async Task<string?> GetExternalIpAsync(HttpClient http, Uri control, string type)
    {
        var (body, ok) = await SoapAsync(http, control, type, "GetExternalIPAddress", "").ConfigureAwait(false);
        return ok ? ExtractTag(body, "NewExternalIPAddress") : null;
    }

    /// <summary>Boş dönerse başarılı; aksi halde UPnP hata kodu (örn. "718" = bu dış port kullanımda).</summary>
    internal static async Task<string?> AddMappingAsync(HttpClient http, Uri control, string type, int externalPort, int internalPort, string internalIp, string description, int lease)
    {
        var args = $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{externalPort}</NewExternalPort><NewProtocol>TCP</NewProtocol>" +
                   $"<NewInternalPort>{internalPort}</NewInternalPort><NewInternalClient>{internalIp}</NewInternalClient><NewEnabled>1</NewEnabled>" +
                   $"<NewPortMappingDescription>{System.Security.SecurityElement.Escape(description)}</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>";
        var (body, ok) = await SoapAsync(http, control, type, "AddPortMapping", args).ConfigureAwait(false);
        if (ok) return null;
        var code = ExtractTag(body, "errorCode") ?? "?";
        // 725: yalnız süresiz kira desteklenir -> 0 ile yeniden dene
        if (code == "725" && lease != 0) return await AddMappingAsync(http, control, type, externalPort, internalPort, internalIp, description, 0).ConfigureAwait(false);
        return code;
    }

    internal static async Task DeleteMappingAsync(HttpClient http, Uri control, string type, int externalPort) =>
        await SoapAsync(http, control, type, "DeletePortMapping",
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{externalPort}</NewExternalPort><NewProtocol>TCP</NewProtocol>").ConfigureAwait(false);

    // ---------- Yardımcılar ----------

    /// <summary>Modeme giden yolda kullanılan yerel IPv4 adresi.</summary>
    private static string? LocalIpFor(Uri gateway)
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(gateway.Host, gateway.Port);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (SocketException) { return null; }
    }

    /// <summary>10/8, 172.16/12, 192.168/16, 100.64/10 (CGNAT) ve link-local: internetten ulaşılamayan adresler.</summary>
    internal static bool IsPrivateOrCgnat(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127)
               || (b[0] == 169 && b[1] == 254) || b[0] == 127 || b[0] == 0;
    }
}
