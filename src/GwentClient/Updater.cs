using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace GwentClient;

/// <summary>Güncelleme ilerlemesi: aşama ve (indirmede) bayt sayıları.</summary>
public sealed record UpdateProgress(string Phase, long Done, long Total);

/// <summary>
/// Oyun içi güncelleme: release'teki paket zip'ini indirir, geçici klasöre açar, oyun klasöründeki dosyaları yenileriyle değiştirir ve oyunu
/// yeniden başlatır. Çalışan exe'nin üzerine yazılamaz ama yeniden adlandırılabilir: <c>Gwent.exe</c> → <c>Gwent.exe.old</c>, yenisi yerine
/// kopyalanır; kilitli başka dosya varsa o da aynı yolla değiştirilir. Bir şey ters giderse adı değişen dosyalar geri konur (oyun bozulmaz).
/// <c>.old</c> dosyaları bir sonraki açılışta silinir. Kayıtlar (%AppData%\Gwent) bu klasörde olmadığı için etkilenmez.
/// </summary>
public static class Updater
{
    private const string OldSuffix = ".old";

    /// <summary>Oyun klasörü (exe'nin yanı). Güncelleme yalnız paketlenmiş kopyada (yanında assets klasörü olan) yapılır; geliştirme çıktısında değil.</summary>
    public static string AppDir => AppContext.BaseDirectory;

    public static bool CanSelfUpdate =>
        Directory.Exists(Path.Combine(AppDir, "assets")) && Environment.ProcessPath is { } p && Path.GetFileName(p).Equals("Gwent.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Önceki güncellemeden kalan .old dosyalarını ve geçici klasörleri siler (açılışta; kilitliyse sonraya kalır).</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(AppDir, "*" + OldSuffix, SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), "gwent-update-*"))
                try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }

    public const string CoreZip = "Gwent-core.zip";
    public const string DataZip = "Gwent-data.zip";
    public const string ManifestName = "package.json";

    /// <summary>
    /// Hangi dosyalar indirilecek? Yerel ve uzak package.json'daki parça özetleri karşılaştırılır: yalnız değişen parça (çoğu zaman yalnız
    /// uygulama). Biri değişince package.json da güncellensin diye uygulama parçası hep gelir. Yerel package.json yoksa (eski kurulum) ya da
    /// release'te parçalar yoksa tam paket.
    /// </summary>
    public static List<string> ChooseParts(string? localManifest, string? remoteManifest, ICollection<string> available)
    {
        var full = new List<string> { UpdateCheck.ZipName };
        if (localManifest == null || remoteManifest == null || !available.Contains(CoreZip) || !available.Contains(DataZip)) return full;
        try
        {
            using var l = System.Text.Json.JsonDocument.Parse(localManifest);
            using var r = System.Text.Json.JsonDocument.Parse(remoteManifest);
            string? Part(System.Text.Json.JsonDocument d, string k) => d.RootElement.TryGetProperty(k, out var v) ? v.GetString() : null;
            var parts = new List<string> { CoreZip };
            if (Part(l, "data") != Part(r, "data") || Part(r, "data") == null) parts.Add(DataZip);
            return parts;
        }
        catch { return full; }
    }

    /// <summary>İndir → aç → değiştir. Başarılıysa yeniden başlatılacak exe'nin yolu, değilse hata fırlatır (dosyalar eski hâline döner).</summary>
    public static async Task<string> DownloadAndApplyAsync(ReleaseInfo r, IProgress<UpdateProgress> progress, CancellationToken ct, string? targetDir = null)
    {
        targetDir ??= AppDir;
        var files = r.Files.Count > 0 ? r.Files : r.ZipUrl != null ? new Dictionary<string, (string, long)> { [UpdateCheck.ZipName] = (r.ZipUrl, r.ZipSize) } : null;
        if (files == null || files.Count == 0) throw new InvalidOperationException("Bu sürümde indirilecek paket bulunamadı.");
        var work = Path.Combine(Path.GetTempPath(), "gwent-update-" + r.Tag + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(work);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Gwent-updater");

        // Hangi parçalar? (uzak package.json küçük bir dosyadır)
        string? remoteManifest = null;
        if (files.TryGetValue(ManifestName, out var mf))
            try { remoteManifest = await http.GetStringAsync(mf.Url, ct).ConfigureAwait(false); } catch (OperationCanceledException) { throw; } catch { }
        var localPath = Path.Combine(targetDir, ManifestName);
        var localManifest = File.Exists(localPath) ? File.ReadAllText(localPath) : null;
        var parts = ChooseParts(localManifest, remoteManifest, files.Keys.ToList()).Where(files.ContainsKey).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("Bu sürümde indirilecek paket bulunamadı.");

        var total = parts.Sum(p => files[p].Size);
        long done = 0;
        var zips = new List<string>();
        foreach (var part in parts)
        {
            var zip = Path.Combine(work, part);
            using var resp = await http.GetAsync(files[part].Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            if (total <= 0) total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(zip))
            {
                var buf = new byte[1 << 16];
                var last = DateTime.MinValue;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 100) { last = DateTime.UtcNow; progress.Report(new UpdateProgress("download", done, total)); }
                }
            }
            zips.Add(zip);
        }
        progress.Report(new UpdateProgress("download", done, Math.Max(total, done)));

        ct.ThrowIfCancellationRequested();
        progress.Report(new UpdateProgress("extract", 0, 0));
        var unpacked = Path.Combine(work, "new");
        await Task.Run(() => { foreach (var z in zips) ZipFile.ExtractToDirectory(z, unpacked, overwriteFiles: true); }, ct).ConfigureAwait(false);
        var root = FindPackageRoot(unpacked) ?? throw new InvalidOperationException("İndirilen pakette Gwent.exe bulunamadı.");

        ct.ThrowIfCancellationRequested();
        progress.Report(new UpdateProgress("apply", 0, 0));
        await Task.Run(() => Apply(root, targetDir)).ConfigureAwait(false);
        return Path.Combine(targetDir, "Gwent.exe");
    }

    /// <summary>Paketin kökü: Gwent.exe'nin bulunduğu klasör (zip doğrudan dosyaları ya da tek bir üst klasörü içerebilir).</summary>
    internal static string? FindPackageRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, "Gwent.exe"))) return dir;
        var subs = Directory.GetDirectories(dir);
        return subs.Length == 1 && File.Exists(Path.Combine(subs[0], "Gwent.exe")) ? subs[0] : null;
    }

    /// <summary>
    /// <paramref name="source"/> içindeki tüm dosyaları <paramref name="target"/>'a kopyalar. Üzerine yazılamayan (çalışan/kilitli) dosya önce
    /// <c>.old</c> adına çekilir, sonra yenisi kopyalanır. Hata olursa yapılan her şey geri alınır ve hata yeniden fırlatılır.
    /// </summary>
    internal static void Apply(string source, string target)
    {
        var renamed = new List<(string Original, string Old)>();
        var written = new List<(string Path, string? Backup)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (!File.Exists(dest)) { File.Copy(file, dest); written.Add((dest, null)); continue; }
                if (SameFile(file, dest)) continue;
                var old = dest + OldSuffix;
                if (File.Exists(old)) File.Delete(old);
                File.Move(dest, old);                 // çalışan exe de yeniden adlandırılabilir
                renamed.Add((dest, old));
                File.Copy(file, dest);
                written.Add((dest, old));
            }
            // Paketteki .jpg/.mp3, eski kurulumdaki aynı adlı .png/.wav'ın yerini alır: eskiler kenara çekilir (yer kaplamasın, öncelik almasın)
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var oldExt = ext == ".jpg" ? ".png" : ext == ".mp3" ? ".wav" : null;
                if (oldExt == null) continue;
                var stale = Path.ChangeExtension(Path.Combine(target, Path.GetRelativePath(source, file)), oldExt);
                if (!File.Exists(stale) || File.Exists(Path.ChangeExtension(file, oldExt))) continue;
                var old = stale + OldSuffix;
                if (File.Exists(old)) File.Delete(old);
                File.Move(stale, old);
                renamed.Add((stale, old));
            }
        }
        catch
        {
            // Geri al: yeni yazılanları sil, eskileri yerine koy
            foreach (var (path, _) in written.AsEnumerable().Reverse())
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            foreach (var (original, old) in renamed.AsEnumerable().Reverse())
                try { if (!File.Exists(original) && File.Exists(old)) File.Move(old, original); } catch { }
            throw;
        }
    }

    /// <summary>İki dosya bayt bayt aynı mı? Aynıysa dokunulmaz (açık yazı tipleri, resimler kilitli olsa da sorun çıkmaz).</summary>
    private static bool SameFile(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        try
        {
            using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var ba = new byte[1 << 16];
            var bb = new byte[1 << 16];
            while (true)
            {
                var na = sa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
                var nb = sb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
                if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                if (na == 0) return true;
            }
        }
        catch { return false; }
    }

    /// <summary>Güncellenmiş oyunu başlatır ve bu süreci kapatır.</summary>
    public static void Restart(string exe)
    {
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        System.Windows.Application.Current.Shutdown();
    }
}
