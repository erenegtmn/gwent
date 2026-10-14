using System.IO;
using System.Text.Json;
using GwentNet;

namespace GwentClient;

/// <summary>Konuğun yarım kalan maçtan dönmek için sakladığı bilgi: oda kodu + koltuk anahtarı.</summary>
public sealed record GuestResume(string RoomCode, string Token, string Nick, string HostNick, DateTime SavedAtUtc);

/// <summary>
/// Uygulama kapanıp açılınca online maçı sürdürebilmek için yerel kayıtlar (%AppData%\Gwent). Yalnız bu bilgisayarda kalır.
/// Host: devam eden maçın özeti (kurulum + eylemler + koltuk anahtarları). Konuk: oda kodu + anahtar.
/// Kayıtlar 24 saatten eskiyse yok sayılır; maç bitince/bilerek ayrılınca silinir. Sınamalar <see cref="Enabled"/> kapalıyken yazmaz.
/// </summary>
public static class ResumeStore
{
    public static bool Enabled { get; set; }
    public static string? DirOverride { get; set; }
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly object Gate = new();
    private static readonly object IoGate = new();
    private static string? _pending;
    private static bool _writing;
    private static int _ver;

    private static string Dir => DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent");
    private static string HostPath => Path.Combine(Dir, "resume_host.json");
    private static string GuestPath => Path.Combine(Dir, "resume_guest.json");

    // ---------- Host ----------

    /// <summary>Sunucu kilidi altında çağrılır: yalnız seri hâle getirip yazmayı arka plana atar. snap null = maç bitti → kaydı sil.</summary>
    public static void OnHostRoomChanged(string code, RoomSnapshot? snap)
    {
        if (!Enabled) return;
        if (snap == null) { ClearHost(); return; }
        string json;
        int ver;
        lock (Gate) { json = JsonSerializer.Serialize(snap); ver = ++_ver; _pending = json; if (_writing) return; _writing = true; }
        Task.Run(() => Drain(ver));
    }

    private static void Drain(int _)
    {
        while (true)
        {
            string? json;
            int ver;
            lock (Gate) { json = _pending; _pending = null; ver = _ver; if (json == null) { _writing = false; return; } }
            try
            {
                lock (IoGate)
                {
                    int now;
                    lock (Gate) now = _ver;
                    if (now != ver) continue;   // bu arada yenisi geldi ya da kayıt silindi
                    Directory.CreateDirectory(Dir);
                    var tmp = HostPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, HostPath, overwrite: true);
                }
            }
            catch { /* kayıt başarısızsa oyun etkilenmez */ }
        }
    }

    public static RoomSnapshot? LoadHost()
    {
        try
        {
            if (!File.Exists(HostPath)) return null;
            var snap = JsonSerializer.Deserialize<RoomSnapshot>(File.ReadAllText(HostPath));
            return snap != null && DateTime.UtcNow - snap.SavedAtUtc < MaxAge && snap.Setup != null ? snap : null;
        }
        catch { return null; }
    }

    public static void ClearHost()
    {
        lock (Gate) { _ver++; _pending = null; }
        try { lock (IoGate) { if (File.Exists(HostPath)) File.Delete(HostPath); } } catch { }
    }

    // ---------- Konuk ----------

    public static void SaveGuest(GuestResume g)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(GuestPath, JsonSerializer.Serialize(g));
        }
        catch { }
    }

    public static GuestResume? LoadGuest()
    {
        try
        {
            if (!File.Exists(GuestPath)) return null;
            var g = JsonSerializer.Deserialize<GuestResume>(File.ReadAllText(GuestPath));
            return g != null && g.Token.Length > 0 && DateTime.UtcNow - g.SavedAtUtc < MaxAge ? g : null;
        }
        catch { return null; }
    }

    public static void ClearGuest()
    {
        try { if (File.Exists(GuestPath)) File.Delete(GuestPath); } catch { }
    }
}
