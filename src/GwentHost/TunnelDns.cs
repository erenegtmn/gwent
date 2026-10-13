using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GwentHost;

/// <summary>
/// Yeni açılan <c>*.trycloudflare.com</c> adları bazı internet sağlayıcılarının DNS'inde bir dakika kadar görünmez (olumsuz önbellek).
/// Sistem DNS'i bu adı çözemezse Cloudflare'in 1.1.1.1 DNS-over-HTTPS sunucusundan çözer ve doğrudan o IP'ye bağlanır;
/// TLS yine tünel adıyla yapıldığından sertifika denetimi bozulmaz.
/// </summary>
public static class TunnelDns
{
    private static readonly HttpClient Doh = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = TimeSpan.FromSeconds(6) };

    public static bool IsTunnelHost(string host) => host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>DNS-over-HTTPS (JSON) yanıtından IPv4 adreslerini çıkarır. Ad yoksa ya da yanıt bozuksa boş liste.</summary>
    internal static List<IPAddress> ParseDohAnswer(string json)
    {
        var result = new List<IPAddress>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Status", out var status) || status.GetInt32() != 0) return result;
            if (!doc.RootElement.TryGetProperty("Answer", out var answers)) return result;
            foreach (var a in answers.EnumerateArray())
                if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1 && a.TryGetProperty("data", out var d) && IPAddress.TryParse(d.GetString(), out var ip))
                    result.Add(ip);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
        return result;
    }

    /// <summary>Adı çözer: önce sistem DNS'i; olmazsa (yalnız tünel adları için) 1.1.1.1 DNS-over-HTTPS.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            var system = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            if (system.Length > 0) return system;
        }
        catch (SocketException) when (IsTunnelHost(host)) { /* aşağıda 1.1.1.1 ile dene */ }
        if (!IsTunnelHost(host)) throw new SocketException((int)SocketError.HostNotFound);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://1.1.1.1/dns-query?name={Uri.EscapeDataString(host)}&type=A");
            req.Headers.TryAddWithoutValidation("Accept", "application/dns-json");
            using var res = await Doh.SendAsync(req, ct).ConfigureAwait(false);
            var ips = ParseDohAnswer(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (ips.Count > 0) return ips.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        // Son çare: tüm tünel adları aynı Cloudflare uç noktasına çözülür. Yeni adın DNS kaydı henüz yayılmamışsa bile uç nokta tüneli SNI/Host
        // adından bulur; bu yüzden ana alan adının adresine bağlanıp TLS'i tünel adıyla yapmak yeter (sertifika denetimi bozulmaz).
        foreach (var apex in new[] { "trycloudflare.com", "api.trycloudflare.com" })
        {
            try
            {
                var ips = await Dns.GetHostAddressesAsync(apex, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                if (ips.Length > 0) return ips;
            }
            catch (SocketException) { }
        }
        return KnownEdge;
    }

    /// <summary>Ana alan adı da çözülemezse kullanılacak bilinen Cloudflare tünel uç noktaları.</summary>
    internal static readonly IPAddress[] KnownEdge = { IPAddress.Parse("104.16.230.132"), IPAddress.Parse("104.16.231.132") };

    /// <summary>Sistem DNS'i yetmezse DoH'a düşen bağlantı kurucu; hem HTTP hem WebSocket isteklerinde kullanılır.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host;
            var ips = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await ResolveAsync(host, ct).ConfigureAwait(false);
            Exception? last = null;
            foreach (var ip in ips)
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException)
                {
                    socket.Dispose();
                    last = ex;
                }
            }
            throw last ?? new SocketException((int)SocketError.HostNotFound);
        }
    };
}
