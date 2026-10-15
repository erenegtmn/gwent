using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace GwentClient;

/// <summary>Yayımlanmış bir sürüm: etiket (ör. "v0.7"), sürüm, indirme sayfası ve (varsa) paket zip'inin doğrudan adresi/boyutu.</summary>
public sealed record ReleaseInfo(string Tag, Version Version, string Url, string? ZipUrl = null, long ZipSize = 0)
{
    /// <summary>Release'e eklenen tüm dosyalar: ad → (indirme adresi, boyut). Parçalı güncelleme bunları kullanır.</summary>
    public IReadOnlyDictionary<string, (string Url, long Size)> Files { get; init; } = new Dictionary<string, (string, long)>();
}

/// <summary>
/// Sürüm denetimi: uygulama açılınca GitHub'daki son release'e bakar (arka planda, en fazla birkaç saniye). Daha yeni bir sürüm varsa
/// ana menüde "Yeni sürüm" bildirimi çıkar ve indirme sayfası tarayıcıda açılır. İnternet yoksa ya da depo erişilemezse sessizce geçer.
/// Sürüm numarası GwentClient.csproj'daki &lt;Version&gt;'dır; release etiketi "v" + bu sayı olmalı (ör. v0.6).
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "erenegtmn/gwent";
    /// <summary>Release'e eklenen paketin adı: oyun içi güncelleme bu dosyayı indirir (tools/package.sh çıktısı, zip'lenmiş).</summary>
    public const string ZipName = "Gwent-win-x64.zip";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    /// <summary>Bu kopyanın sürümü (ör. 0.6.0).</summary>
    public static Version Current { get; } = Normalize(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));

    public static string CurrentText => $"v{Current.Major}.{Current.Minor}" + (Current.Build > 0 ? $".{Current.Build}" : "");

    /// <summary>Denetim sonucu: daha yeni sürüm varsa bilgisi, yoksa null. Başlatılmadıysa null görev.</summary>
    public static Task<ReleaseInfo?>? Pending { get; private set; }

    /// <summary>Ekran görüntüsü ve sınama için sonucu elle verir.</summary>
    internal static void SetResult(ReleaseInfo? r) => Pending = Task.FromResult(r);

    /// <summary>Uygulama açılınca bir kez başlatılır (sınama ve ekran görüntüsü süreçlerinde çağrılmaz).</summary>
    public static void Start() => Pending ??= Task.Run(CheckAsync);

    private static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Gwent-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").ConfigureAwait(false);
            return Newer(json, Current);
        }
        catch { return null; }   // çevrimdışı, depo özel ya da istek sınırı: bildirim yok
    }

    /// <summary>GitHub "latest release" yanıtından, <paramref name="current"/>'ten yeni bir sürüm varsa onu döner.</summary>
    public static ReleaseInfo? Newer(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        var files = new Dictionary<string, (string, long)>();
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
                if (a.TryGetProperty("name", out var n) && n.GetString() is { } name && a.TryGetProperty("browser_download_url", out var d) && d.GetString() is { } du)
                    files[name] = (du, a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0);
        var full = files.TryGetValue(ZipName, out var f) ? f : default;
        return Normalize(v) > Normalize(current)
            ? new ReleaseInfo(tag, Normalize(v), url ?? ReleasesPage, full.Item1, full.Item2) { Files = files }
            : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
