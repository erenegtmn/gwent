using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Oyuncu portreleri: Geralt ve oyundaki lider kartlarının yüzleri. Bir portre yalnız kısa bir kimliktir
/// (<c>avatar_geralt</c> ya da lider kartı kimliği); online'da rakibe bu kimlik gider, görsel herkesin kendi kopyasından çizilir.
/// </summary>
public static class Portraits
{
    public const string Default = "avatar_geralt";
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    /// <summary>Seçilebilir tüm portre kimlikleri (Geralt + standart liderler).</summary>
    public static IReadOnlyList<string> All()
    {
        var list = new List<string> { Default };
        foreach (var f in Factions)
            list.AddRange(CardCatalog.Default.Leaders(f).Where(l => l.Edition != "nextgen").Select(l => l.Id));
        return list;
    }

    public static string Normalize(string? id) => id != null && All().Contains(id) ? id : Default;

    public static string Title(string id) => id == Default ? "Rivyalı Geralt" : CardCatalog.Default.TryGet(id, out var d) ? d.Name : id;

    private static string FactionOf(string id)
    {
        foreach (var f in Factions)
            if (CardCatalog.Default.Leaders(f).Any(l => l.Id == id)) return f;
        return "northern_realms";
    }

    /// <summary>Yuvarlak, halkalı portre. Görsel yoksa fraksiyon renginde bir daire çizilir.</summary>
    public static FrameworkElement Medallion(string? id, double size, bool glow = false)
    {
        if (id == null || (id != Default && !CardCatalog.Default.TryGet(id, out _))) id = Default;   // yalnız görüntü: herhangi bir kart yüzü olur
        var g = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        var inner = size * 0.73;
        Brush? fill = null;
        if (id == Default)
        {
            if (Assets.Ui("avatar_geralt") is { } src) fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
        }
        else if (Assets.Card(id) is { } art)
            fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.1, 0.03, 0.8, 0.56), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
        g.Children.Add(new Ellipse { Width = inner, Height = inner, Fill = fill ?? Theme.FactionColor(FactionOf(id)) });
        if (Assets.Ui("ring") is { } ring)
            g.Children.Add(new Image { Source = ring, Width = size, Height = size * 1.09, VerticalAlignment = VerticalAlignment.Center });
        else
            g.Children.Add(new Ellipse { Width = size * 0.9, Height = size * 0.9, Stroke = Theme.GoldDim, StrokeThickness = Math.Max(2, size * 0.04) });
        if (glow) g.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = size * 0.22, ShadowDepth = 0, Opacity = 0.9 };
        return g;
    }
}
