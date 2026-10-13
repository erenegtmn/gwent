using System.Windows.Media;

namespace GwentClient;

public static class Theme
{
    private static FontFamily? _serif;

    /// <summary>
    /// Arayüz yazı tipi: oyunun kendi "PF Din Text Cond Pro" yazısı (<c>assets/ui/fonts</c>, tools/w3_font_extract.py ile üretilir);
    /// yoksa Windows'taki serif.
    /// </summary>
    public static FontFamily Serif => _serif ??= Load();

    /// <summary>Oyun yazısı dar olduğundan aynı boyutta küçük görünür; metinler bu çarpanla büyütülür.</summary>
    public static double FontScale => GameFont ? 1.12 : 1.0;

    public static bool GameFont { get; private set; }

    /// <summary>Fraksiyonun menü panoramasının adı (<c>assets/ui/pano_*.png</c>).</summary>
    public static string PanoFor(string faction) => faction switch
    {
        "nilfgaard" => "pano_wyzima_castle",
        "scoiatael" => "pano_no_mans_land",
        "monsters" => "pano_island_of_mist",
        "skellige" => "pano_skellige",
        _ => "pano_novigrad"
    };

    private static FontFamily Load()
    {
        var dir = Assets.Dir;
        if (dir != null)
        {
            var fonts = System.IO.Path.Combine(dir, "ui", "fonts");
            if (System.IO.File.Exists(System.IO.Path.Combine(fonts, "din_regular.ttf")))
            {
                GameFont = true;
                return new FontFamily(new Uri(fonts + System.IO.Path.DirectorySeparatorChar), "./#PF Din Text Cond Pro");
            }
        }
        return new FontFamily("Palatino Linotype, Book Antiqua, Georgia, Times New Roman");
    }

    public static SolidColorBrush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public static readonly Brush Bg = B("#14100c");
    public static readonly Brush Panel = B("#211a13");
    public static readonly Brush PanelEdge = B("#3a2e20");
    public static readonly Brush Gold = B("#c9a24b");
    public static readonly Brush GoldDim = B("#8a7236");
    public static readonly Brush Text = B("#e8dcc4");
    public static readonly Brush TextDim = B("#a39880");
    public static readonly Brush RowMine = B("#2a2116");
    public static readonly Brush RowOpp = B("#241a1a");
    public static readonly Brush Good = B("#a3b878");
    public static readonly Brush Bad = B("#b8604f");
    public static readonly Brush Select = B("#ffe9a3");

    public static Brush FactionColor(string faction) => faction switch
    {
        "northern_realms" => B("#3f6a9a"),
        "nilfgaard" => B("#2d2d33"),
        "scoiatael" => B("#3f7a45"),
        "monsters" => B("#8a3535"),
        "skellige" => B("#2f7a80"),
        _ => B("#7a7468"),
    };

    public static Brush FactionAccent(string faction) => faction switch
    {
        "nilfgaard" => B("#d6b24a"),
        _ => B("#e8dcc4"),
    };
}
