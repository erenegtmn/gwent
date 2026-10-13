using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace GwentHost;

/// <summary>
/// Cloudflare "quick tunnel" (hesapsız, ücretsiz): <c>cloudflared</c> yerel bir portu internete açık geçici bir https adresine bağlar.
/// Arkadaş hiçbir şey kurmaz; her yerde (CGNAT dahil) çalışır. <c>cloudflared</c> ayrı bir programdır, ilk kullanımda indirilir.
/// </summary>
public sealed class CloudflareTunnel : IAsyncDisposable
{
    private static readonly Regex UrlPattern = new(@"https://([a-z0-9][a-z0-9-]*)\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly Process _process;

    private CloudflareTunnel(Process p, string publicUrl)
    {
        _process = p;
        PublicUrl = publicUrl;
    }

    /// <summary>Örn. https://quiet-river-fox.trycloudflare.com (hub yolu eklenmemiş).</summary>
    public string PublicUrl { get; }

    internal static string? FindUrl(string line)
    {
        var m = UrlPattern.Match(line);
        // "api.trycloudflare.com" tünel adresi değildir (cloudflared bunu hata mesajlarında anabilir)
        return m.Success && !m.Groups[1].Value.Equals("api", StringComparison.OrdinalIgnoreCase) ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>cloudflared'i başlatır ve tünel adresini bekler.</summary>
    public static async Task<(CloudflareTunnel? Tunnel, string? Error)> StartAsync(int localPort, string exePath, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!File.Exists(exePath)) return (null, "cloudflared bulunamadı.");
        // Kullanıcının ~/.cloudflared/config.yml dosyası (başka projenin adlandırılmış tüneli: ingress kuralları, son kural 404) hızlı tünele de
        // uygulanır ve bizim adresimiz hiçbir kurala uymadığı için 404 döner. Boş bir yapılandırma dosyasıyla genel ayarlar yok sayılır.
        var emptyConfig = Path.Combine(Path.GetTempPath(), "gwent-cloudflared-empty.yml");
        try { File.WriteAllText(emptyConfig, "# Gwent hızlı tüneli: kullanıcının genel cloudflared ayarlarını yok say" + Environment.NewLine); }
        catch (IOException) { /* dosya başka süreçte açıksa var olan içerik de boştur */ }
        var psi = new ProcessStartInfo(exePath, $"tunnel --config \"{emptyConfig}\" --url http://127.0.0.1:{localPort} --no-autoupdate")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p == null) return (null, "cloudflared başlatılamadı.");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastLines = new Queue<string>();
            void OnLine(string? line)
            {
                if (line == null) return;
                lock (lastLines) { lastLines.Enqueue(line); while (lastLines.Count > 6) lastLines.Dequeue(); }
                var url = FindUrl(line);
                if (url != null) tcs.TrySetResult(url);
            }
            p.OutputDataReceived += (_, e) => OnLine(e.Data);
            p.ErrorDataReceived += (_, e) => OnLine(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => tcs.TrySetException(new InvalidOperationException("cloudflared kapandı."));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                var url = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                return (new CloudflareTunnel(p, url), null);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
            {
                string tail;
                lock (lastLines) tail = string.Join(" | ", lastLines);
                KillQuietly(p);
                return (null, ex is OperationCanceledException ? "Tünel adresi zamanında alınamadı. " + tail : "cloudflared beklenmedik şekilde kapandı. " + tail);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (p != null) KillQuietly(p);
            return (null, "cloudflared çalıştırılamadı: " + ex.Message);
        }
    }

    private static void KillQuietly(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        try { p.Dispose(); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        KillQuietly(_process);
        return ValueTask.CompletedTask;
    }
}

/// <summary>cloudflared'in bulunması, indirilmesi ve doğrulanması. İndirme yalnız kullanıcı izin verirse yapılır.</summary>
public static class Cloudflared
{
    public const string DownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
    public const string Source = "github.com/cloudflare/cloudflared";

    public static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "tools", "cloudflared.exe");

    /// <summary>
    /// Kurulu cloudflared yolu; yoksa null. Sırayla: oyunun kendi kurulum klasörü, exe'nin yanı, Program Files / LocalAppData'daki
    /// bilinen konumlar, ardından PATH (başka bir projeden ya da winget/choco ile kurulmuş olabilir).
    /// </summary>
    public static string? FindInstalled()
    {
        var candidates = new List<string>
        {
            InstallPath,
            Path.Combine(AppContext.BaseDirectory, "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cloudflared", "cloudflared.exe")
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim().Trim('"'), "cloudflared.exe")); }
            catch (ArgumentException) { /* PATH içinde geçersiz giriş */ }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>İndirilecek dosyanın boyutu (bayt); öğrenilemezse null. Kullanıcıya izin sorarken gösterilir.</summary>
    public static async Task<long?> GetDownloadSizeAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var req = new HttpRequestMessage(HttpMethod.Head, DownloadUrl);
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode ? res.Content.Headers.ContentLength : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>cloudflared'i indirir, imzasını (Cloudflare) doğrular ve kurulum klasörüne koyar.</summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var target = InstallPath;
        var tmp = target + ".indiriliyor";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var res = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return (null, $"İndirme başarısız (HTTP {(int)res.StatusCode}).");
            var total = res.Content.Headers.ContentLength;
            await using (var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }
            var err = VerifySignature(tmp);
            if (err != null) { File.Delete(tmp); return (null, err); }
            File.Move(tmp, target, overwrite: true);
            return (target, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch { }
            return (null, "cloudflared indirilemedi: " + ex.Message);
        }
    }

    /// <summary>
    /// İndirilen dosyanın imzasını Windows ile (WinVerifyTrust: dosya özeti + sertifika zinciri) doğrular ve imzalayanın Cloudflare olduğunu
    /// denetler; bozuk, değiştirilmiş ya da başkasının imzaladığı dosya çalıştırılmaz. Sorun yoksa null döner.
    /// </summary>
    public static string? VerifySignature(string path)
    {
        try
        {
            using (var bytes = File.OpenRead(path))
                if (bytes.ReadByte() != 'M' || bytes.ReadByte() != 'Z') return "İndirilen dosya bir Windows programı değil.";
            if (!OperatingSystem.IsWindows()) return "İmza doğrulaması yalnız Windows'ta yapılır.";
            if (!WinTrust.IsTrusted(path)) return "İndirilen dosyanın imzası geçersiz ya da güvenilmiyor.";
#pragma warning disable SYSLIB0057 // yalnız imzalayan sertifikanın adını okumak için; bütünlüğü yukarıdaki WinVerifyTrust denetler
            var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase) ? null : "İndirilen dosya Cloudflare tarafından imzalanmamış: " + cert.Subject;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException or IOException)
        {
            return "İndirilen dosyanın imzası doğrulanamadı.";
        }
    }
}

/// <summary>Windows Authenticode doğrulaması (WinVerifyTrust).</summary>
internal static class WinTrust
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public int cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public int dwUIChoice;          // 2 = WTD_UI_NONE
        public int fdwRevocationChecks; // 0 = WTD_REVOKE_NONE (çevrimdışı da çalışsın)
        public int dwUnionChoice;       // 1 = WTD_CHOICE_FILE
        public IntPtr pFile;
        public int dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public int dwProvFlags;
        public int dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [System.Runtime.InteropServices.DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    public static bool IsTrusted(string path)
    {
        var file = new WinTrustFileInfo { cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        var filePtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<WinTrustData>(), dwUIChoice = 2, fdwRevocationChecks = 0,
                dwUnionChoice = 1, pFile = filePtr, dwStateAction = 0, dwProvFlags = 0x00000010 /* WTD_CACHE_ONLY_URL_RETRIEVAL */
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(filePtr); }
    }
}
