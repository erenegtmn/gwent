using GwentNet;
using GwentServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GwentHost;

/// <summary>
/// Oda kuran oyuncunun bilgisayarında çalışan gömülü sunucu (Kestrel + SignalR). Aynı <see cref="RoomService"/> çekirdeğini kullanır;
/// host'un kendisi de bu sunucuya diğer oyuncular gibi bağlanır.
/// </summary>
public sealed class LocalServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LocalServer(WebApplication app, int port, RoomService rooms)
    {
        _app = app;
        Port = port;
        Rooms = rooms;
    }

    public int Port { get; }
    public RoomService Rooms { get; }

    /// <summary>Bu bilgisayardan bağlanma adresi (host'un kendi istemcisi için).</summary>
    public string LocalUrl => $"http://127.0.0.1:{Port}{HubMethods.Path}";

    /// <param name="port">0 = boş bir port seç.</param>
    /// <param name="lan">true: yerel ağdan da erişilebilir (0.0.0.0); false: yalnız bu bilgisayar (127.0.0.1).</param>
    public static async Task<LocalServer> StartAsync(int port = 0, bool lan = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{(lan ? "0.0.0.0" : "127.0.0.1")}:{port}");
        var rooms = new RoomService();
        builder.Services.AddSingleton(rooms);
        builder.Services.AddSingleton<SessionRegistry>();
        builder.Services.AddHostedService<RoomSweeper>();
        builder.Services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 64 * 1024;   // eylem mesajları küçüktür
            o.KeepAliveInterval = TimeSpan.FromSeconds(10);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(40);
        });
        var app = builder.Build();
        app.MapHub<GwentHub>(HubMethods.Path);
        app.MapGet("/", () => $"Gwent sunucusu ({ProtocolInfo.Version})");
        await app.StartAsync().ConfigureAwait(false);
        var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new LocalServer(app, new Uri(bound.Replace("0.0.0.0", "127.0.0.1")).Port, rooms);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Uzun süre kıpırdamayan odaları temizler.</summary>
internal sealed class RoomSweeper : BackgroundService
{
    private readonly RoomService _rooms;
    public RoomSweeper(RoomService rooms) => _rooms = rooms;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            _rooms.Sweep(TimeSpan.FromHours(6));
        }
    }
}
