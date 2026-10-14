using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace GwentClient;

/// <summary>Yayımlanmış bir sürüm: etiket (ör. "v0.7"), sürüm ve indirme sayfası.</summary>
public sealed record ReleaseInfo(string Tag, Version Version, string Url);

/// <summary>
/// Sürüm denetimi: uygulama açılınca GitHub'daki son release'e bakar (arka planda, en fazla birkaç saniye). Daha yeni bir sürüm varsa
/// ana menüde "Yeni sürüm" bildirimi çıkar ve indirme sayfası tarayıcıda açılır. İnternet yoksa ya da depo erişilemezse sessizce geçer.
/// Sürüm numarası GwentClient.csproj'daki &lt;Version&gt;'dır; release etiketi "v" + bu sayı olmalı (ör. v0.6).
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "erenegtmn/gwent";
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
        return Normalize(v) > Normalize(current) ? new ReleaseInfo(tag, Normalize(v), url ?? ReleasesPage) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
