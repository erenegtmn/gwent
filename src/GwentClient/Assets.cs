using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GwentClient;

/// <summary>
/// Orijinal oyundan çıkarılmış görseller (<c>assets/ui</c>, <c>assets/cards</c>). Depoda bulunmaz; kişi kendi oyun
/// kopyasından <c>tools/extract_assets.py</c> ile üretir. Dosyalar yoksa istemci yer tutucu arayüzle çalışmaya devam eder.
/// </summary>
public static class Assets
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();
    private static string? _dir;
    private static bool _searched;

    /// <summary>Testler için kök klasörü elle verebilir (ör. varlıksız davranışı denemek için boş bir klasör).</summary>
    public static string? Override { get; set; }

    public static string? Dir
    {
        get
        {
            if (Override != null) return Override.Length == 0 ? null : Override;
            if (!_searched)
            {
                _searched = true;
                // exe'nin yanında ya da üst klasörlerinde assets/ui/board.png|jpg ara (geliştirmede depo kökü; dağıtım paketinde jpg)
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null)
                {
                    var ui = Path.Combine(d.FullName, "assets", "ui");
                    if (File.Exists(Path.Combine(ui, "board.png")) || File.Exists(Path.Combine(ui, "board.jpg"))) { _dir = Path.Combine(d.FullName, "assets"); break; }
                    d = d.Parent;
                }
            }
            return _dir;
        }
    }

    public static bool Available => Dir != null;

    private static ImageSource? Load(string relative)
    {
        var dir = Dir;
        if (dir == null) return null;
        var path = Path.Combine(dir, relative);
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
            ImageSource? img = null;
            try
            {
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    img = bmp;
                }
            }
            catch { /* bozuk dosya: yer tutucuya düşülür */ }
            Cache[path] = img;
            return img;
        }
    }

    /// <summary>Arayüz görseli (ui/&lt;ad&gt;.png; yoksa .jpg — büyük haritalar jpg'dir).</summary>
    public static ImageSource? Ui(string name) => Load(Path.Combine("ui", name + ".png")) ?? Load(Path.Combine("ui", name + ".jpg"));
    /// <summary>Kart resmi (cards/&lt;kimlik&gt;.png; dağıtım paketinde boyut için .jpg — köşeleri <see cref="GwintCard"/> yuvarlak kırpar).</summary>
    public static ImageSource? Card(string cardId) => Load(Path.Combine("cards", cardId + ".png")) ?? Load(Path.Combine("cards", cardId + ".jpg"));

    /// <summary>Müzik dosyaları (assets/music/*.mp3).</summary>
    public static IReadOnlyList<string> Music()
    {
        var dir = Dir;
        if (dir == null) return Array.Empty<string>();
        var m = Path.Combine(dir, "music");
        return Directory.Exists(m) ? Directory.GetFiles(m, "*.mp3").OrderBy(f => f).ToList() : Array.Empty<string>();
    }

    /// <summary>Ses efektleri klasörü (assets/sfx).</summary>
    public static string? SfxDir
    {
        get
        {
            var dir = Dir;
            return dir == null ? null : Path.Combine(dir, "sfx");
        }
    }
}
