using System.Net;
using System.Text;
using GwentHost;

namespace GwentHost.Tests;

/// <summary>Sahte modem (UPnP IGD): SOAP isteklerine yanıt verir; hata kodları ayarlanabilir.</summary>
internal sealed class FakeIgd : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    public int Port { get; }
    public string ExternalIp { get; set; } = "203.0.113.50";
    public List<string> Calls { get; } = new();
    public HashSet<int> TakenExternalPorts { get; } = new();
    public bool OnlyPermanentLeases { get; set; }
    public int? MappedExternal { get; private set; }

    public FakeIgd()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = Random.Shared.Next(20000, 40000);
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Port = port;
                break;
            }
            catch (HttpListenerException) { }
        }
        _ = Task.Run(Loop);
    }

    public Uri ControlUrl => new($"http://127.0.0.1:{Port}/ctl/IPConn");
    public string ServiceType => "urn:schemas-upnp-org:service:WANIPConnection:1";

    public string DeviceXml => $@"<?xml version=""1.0""?><root xmlns=""urn:schemas-upnp-org:device-1-0""><device><deviceList><device><deviceList><device><serviceList>
<service><serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType><controlURL>/ctl/CmnIfCfg</controlURL></service>
<service><serviceType>{ServiceType}</serviceType><controlURL>/ctl/IPConn</controlURL></service>
</serviceList></device></deviceList></device></deviceList></device></root>";

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { return; }
            var action = ctx.Request.Headers["SOAPAction"]?.Trim('"').Split('#').LastOrDefault() ?? "";
            string body;
            using (var rd = new StreamReader(ctx.Request.InputStream)) body = await rd.ReadToEndAsync().ConfigureAwait(false);
            lock (Calls) Calls.Add(action);
            var (status, text) = Handle(action, body);
            var bytes = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/xml";
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
    }

    private (int, string) Handle(string action, string body)
    {
        static string Fault(int code) => $"<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError><errorCode>{code}</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>";
        switch (action)
        {
            case "GetExternalIPAddress":
                return (200, $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddressResponse xmlns:u=\"{ServiceType}\"><NewExternalIPAddress>{ExternalIp}</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>");
            case "AddPortMapping":
            {
                var ext = int.Parse(Tag(body, "NewExternalPort"));
                var lease = int.Parse(Tag(body, "NewLeaseDuration"));
                if (OnlyPermanentLeases && lease != 0) return (500, Fault(725));
                if (TakenExternalPorts.Contains(ext)) return (500, Fault(718));
                MappedExternal = ext;
                return (200, "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:AddPortMappingResponse/></s:Body></s:Envelope>");
            }
            case "DeletePortMapping":
                MappedExternal = null;
                return (200, "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:DeletePortMappingResponse/></s:Body></s:Envelope>");
            default:
                return (500, Fault(401));
        }
    }

    private static string Tag(string xml, string name)
    {
        var s = xml.IndexOf("<" + name + ">", StringComparison.Ordinal) + name.Length + 2;
        return xml[s..xml.IndexOf("</" + name + ">", StringComparison.Ordinal)];
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        return ValueTask.CompletedTask;
    }
}

public class UpnpTests
{
    [Fact]
    public void Ssdp_response_location_is_parsed_and_junk_is_ignored()
    {
        var ok = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nLOCATION: http://192.168.0.1:5000/rootDesc.xml\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
        Assert.Equal("http://192.168.0.1:5000/rootDesc.xml", Upnp.ParseSsdpLocation(ok)!.ToString());
        Assert.Null(Upnp.ParseSsdpLocation("HTTP/1.1 200 OK\r\nST: x\r\n\r\n"));
        Assert.Null(Upnp.ParseSsdpLocation("LOCATION: ftp://x/y"));
        Assert.Null(Upnp.ParseSsdpLocation("LOCATION: not a url"));
    }

    [Fact]
    public async Task Device_description_gives_the_wan_ip_connection_control_url()
    {
        await using var igd = new FakeIgd();
        var info = Upnp.ParseControlInfo(igd.DeviceXml, new Uri($"http://127.0.0.1:{igd.Port}/desc.xml"));
        Assert.NotNull(info);
        Assert.Equal(igd.ControlUrl, info!.Value.Control);
        Assert.Equal(igd.ServiceType, info.Value.ServiceType);
        Assert.Null(Upnp.ParseControlInfo("<root><device/></root>", new Uri("http://1.2.3.4/")));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]     // CGNAT
    [InlineData("100.127.255.255", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("169.254.3.4", true)]
    [InlineData("203.0.113.9", false)]
    [InlineData("8.8.8.8", false)]
    public void Private_and_cgnat_addresses_are_recognised(string ip, bool expected) =>
        Assert.Equal(expected, Upnp.IsPrivateOrCgnat(IPAddress.Parse(ip)));

    [Fact]
    public async Task Adding_and_deleting_a_mapping_talks_soap_correctly()
    {
        await using var igd = new FakeIgd();
        using var http = new HttpClient();
        var err = await Upnp.AddMappingAsync(http, igd.ControlUrl, igd.ServiceType, 5000, 5000, "192.168.0.10", "Gwent <test>", 7200);
        Assert.Null(err);
        Assert.Equal(5000, igd.MappedExternal);
        await Upnp.DeleteMappingAsync(http, igd.ControlUrl, igd.ServiceType, 5000);
        Assert.Null(igd.MappedExternal);
    }

    [Fact]
    public async Task Routers_that_only_allow_permanent_leases_are_retried_with_zero()
    {
        await using var igd = new FakeIgd { OnlyPermanentLeases = true };
        using var http = new HttpClient();
        var err = await Upnp.AddMappingAsync(http, igd.ControlUrl, igd.ServiceType, 6000, 6000, "192.168.0.10", "Gwent", 7200);
        Assert.Null(err);
        Assert.Equal(6000, igd.MappedExternal);
        Assert.Equal(2, igd.Calls.Count(c => c == "AddPortMapping")); // önce kira süreli, sonra süresiz
    }

    [Fact]
    public async Task A_taken_external_port_is_reported_as_conflict()
    {
        await using var igd = new FakeIgd();
        igd.TakenExternalPorts.Add(7000);
        using var http = new HttpClient();
        Assert.Equal("718", await Upnp.AddMappingAsync(http, igd.ControlUrl, igd.ServiceType, 7000, 7000, "192.168.0.10", "Gwent", 7200));
        Assert.Null(await Upnp.AddMappingAsync(http, igd.ControlUrl, igd.ServiceType, 7001, 7000, "192.168.0.10", "Gwent", 7200));
    }
}

public class TunnelTests
{
    [Theory]
    [InlineData("2026-01-01T10:00:00Z INF |  https://quiet-river-fox-abc.trycloudflare.com                          |", "https://quiet-river-fox-abc.trycloudflare.com")]
    [InlineData("Your quick Tunnel has been created! Visit it at (it may take some time to be reachable): HTTPS://Rapid-Owl.TryCloudflare.com", "https://rapid-owl.trycloudflare.com")]
    public void Tunnel_url_is_found_in_cloudflared_output(string line, string expected) =>
        Assert.Equal(expected, CloudflareTunnel.FindUrl(line));

    [Theory]
    [InlineData("failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\": dial tcp")]
    [InlineData("INF Requesting new quick Tunnel on trycloudflare.com...")]
    [InlineData("")]
    public void Other_output_lines_are_not_mistaken_for_the_tunnel_url(string line) =>
        Assert.Null(CloudflareTunnel.FindUrl(line));

    private static string FakeExe(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "fake-cloudflared-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task A_started_tunnel_reports_its_url_and_stopping_it_ends_the_process()
    {
        var exe = FakeExe("@echo off\r\necho INF Requesting new quick Tunnel\r\necho 2026 INF ^|  https://quiet-river-fox.trycloudflare.com  ^|\r\nping -n 60 127.0.0.1 >nul\r\n");
        try
        {
            var (tunnel, err) = await CloudflareTunnel.StartAsync(5000, exe, TimeSpan.FromSeconds(15));
            Assert.True(tunnel != null, err);
            Assert.Equal("https://quiet-river-fox.trycloudflare.com", tunnel!.PublicUrl);
            await tunnel.DisposeAsync();
        }
        finally { File.Delete(exe); }
    }

    [Fact]
    public async Task A_tunnel_that_never_prints_a_url_times_out_with_the_last_output_lines()
    {
        var exe = FakeExe("@echo off\r\necho hata ayrintisi burada\r\nping -n 60 127.0.0.1 >nul\r\n");
        try
        {
            var (tunnel, err) = await CloudflareTunnel.StartAsync(5000, exe, TimeSpan.FromSeconds(2));
            Assert.Null(tunnel);
            Assert.Contains("zamanında", err);
            Assert.Contains("hata ayrintisi", err);
        }
        finally { File.Delete(exe); }
    }

    [Fact]
    public async Task A_tunnel_process_that_exits_early_is_reported()
    {
        var exe = FakeExe("@echo off\r\necho kapaniyorum\r\n");
        try
        {
            var (tunnel, err) = await CloudflareTunnel.StartAsync(5000, exe, TimeSpan.FromSeconds(10));
            Assert.Null(tunnel);
            Assert.Contains("kapandı", err);
        }
        finally { File.Delete(exe); }
    }

    [Fact]
    public async Task Missing_executable_is_a_clear_error()
    {
        var (tunnel, err) = await CloudflareTunnel.StartAsync(5000, @"C:\yok\cloudflared.exe", TimeSpan.FromSeconds(1));
        Assert.Null(tunnel);
        Assert.Contains("bulunamadı", err);
    }

    [Fact]
    public void An_unsigned_or_non_exe_download_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), "sahte-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(path, "bu bir program degil");
        try { Assert.NotNull(Cloudflared.VerifySignature(path)); }
        finally { File.Delete(path); }
        // gerçek imzalı bir Microsoft programı Cloudflare imzalı olmadığı için reddedilir
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (File.Exists(notepad)) Assert.NotNull(Cloudflared.VerifySignature(notepad));
    }
}

public class TunnelDnsTests
{
    [Fact]
    public void Doh_json_answers_are_parsed_and_failures_give_an_empty_list()
    {
        var ok = "{\"Status\":0,\"Answer\":[{\"name\":\"x.trycloudflare.com\",\"type\":5,\"data\":\"alias.example.\"},{\"name\":\"x\",\"type\":1,\"TTL\":300,\"data\":\"104.16.230.132\"},{\"type\":1,\"data\":\"104.16.231.132\"}]}";
        var ips = TunnelDns.ParseDohAnswer(ok);
        Assert.Equal(new[] { "104.16.230.132", "104.16.231.132" }, ips.Select(i => i.ToString()).ToArray());
        Assert.Empty(TunnelDns.ParseDohAnswer("{\"Status\":3}"));                 // NXDOMAIN
        Assert.Empty(TunnelDns.ParseDohAnswer("{\"Status\":0}"));                 // cevap yok
        Assert.Empty(TunnelDns.ParseDohAnswer("kirik json"));
        Assert.Empty(TunnelDns.ParseDohAnswer("{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"IP degil\"}]}"));
    }

    [Theory]
    [InlineData("quiet-river.trycloudflare.com", true)]
    [InlineData("QUIET-RIVER.TRYCLOUDFLARE.COM", true)]
    [InlineData("trycloudflare.com", false)]
    [InlineData("example.com", false)]
    [InlineData("evil-trycloudflare.com", false)]
    public void Only_tunnel_hosts_use_the_doh_fallback(string host, bool expected) => Assert.Equal(expected, TunnelDns.IsTunnelHost(host));

    [Fact]
    public void A_known_edge_address_list_exists_as_the_last_resort() => Assert.True(TunnelDns.KnownEdge.Length >= 2);

    [Fact]
    public async Task Non_tunnel_hosts_never_fall_back_to_doh()
    {
        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() => TunnelDns.ResolveAsync("bu-ad-kesinlikle-yok-12345.invalid", CancellationToken.None));
    }
}
