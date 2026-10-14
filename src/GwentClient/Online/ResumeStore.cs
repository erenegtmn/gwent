using System.IO;
using System.Text.Json;
using GwentNet;

namespace GwentClient;

/// <summary>Konuğun yarım kalan maçtan dönmek için sakladığı bilgi: oda kodu + koltuk anahtarı.</summary>
public sealed record GuestResume(string RoomCode, string Token, string Nick, string HostNick, DateTime SavedAtUtc, bool IsTournament = false);

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
    private static string Dir => DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent");
    private static string HostPath => Path.Combine(Dir, "resume_host.json");
    private static string TournamentPath => Path.Combine(Dir, "resume_tournament.json");
    private static string GuestPath => Path.Combine(Dir, "resume_guest.json");

    private static readonly BackgroundWriter HostWriter = new(() => HostPath);
    private static readonly BackgroundWriter TournamentWriter = new(() => TournamentPath);

    /// <summary>Dosyaya arka planda, atomik (geçici dosya + taşıma) ve "son yazılan kazanır" mantığıyla yazar; silme bekleyen yazımları geçersiz kılar.</summary>
    private sealed class BackgroundWriter
    {
        private readonly Func<string> _path;
        private readonly object _gate = new();
        private readonly object _io = new();
        private string? _pending;
        private bool _writing;
        private int _ver;

        public BackgroundWriter(Func<string> path) => _path = path;

        public void Write(string json)
        {
            int ver;
            lock (_gate) { ver = ++_ver; _pending = json; if (_writing) return; _writing = true; }
            Task.Run(() => Drain(ver));
        }

        private void Drain(int _)
        {
            while (true)
            {
                string? json;
                int ver;
                lock (_gate) { json = _pending; _pending = null; ver = _ver; if (json == null) { _writing = false; return; } }
                try
                {
                    lock (_io)
                    {
                        int now;
                        lock (_gate) now = _ver;
                        if (now != ver) continue;   // bu arada yenisi geldi ya da kayıt silindi
                        var path = _path();
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var tmp = path + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, path, overwrite: true);
                    }
                }
                catch { /* kayıt başarısızsa oyun etkilenmez */ }
            }
        }

        public void Clear()
        {
            lock (_gate) { _ver++; _pending = null; }
            try { lock (_io) { var path = _path(); if (File.Exists(path)) File.Delete(path); } } catch { }
        }
    }

    // ---------- Host ----------

    /// <summary>Sunucu kilidi altında çağrılır: yalnız seri hâle getirip yazmayı arka plana atar. snap null = maç bitti → kaydı sil.</summary>
    public static void OnHostRoomChanged(string code, RoomSnapshot? snap)
    {
        if (!Enabled) return;
        if (snap == null) { ClearHost(); return; }
        HostWriter.Write(JsonSerializer.Serialize(snap));
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

    public static void ClearHost() => HostWriter.Clear();

    // ---------- Host: turnuva ----------

    /// <summary>Turnuva durumu her değiştiğinde (hamle, sonuç): sürüyorsa kaydı yazar; bitince/bekleme odasındayken siler.</summary>
    public static void OnHostTournamentChanged(string code, TournamentSnapshot? snap)
    {
        if (!Enabled) return;
        if (snap == null) { ClearHostTournament(); return; }
        TournamentWriter.Write(JsonSerializer.Serialize(snap));
    }

    public static TournamentSnapshot? LoadHostTournament()
    {
        try
        {
            if (!File.Exists(TournamentPath)) return null;
            var snap = JsonSerializer.Deserialize<TournamentSnapshot>(File.ReadAllText(TournamentPath));
            return snap != null && DateTime.UtcNow - snap.SavedAtUtc < MaxAge && snap.Players is { Count: >= 3 } ? snap : null;
        }
        catch { return null; }
    }

    public static void ClearHostTournament() => TournamentWriter.Clear();

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
