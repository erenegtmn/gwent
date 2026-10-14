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

    // ring.png ölçüleri: 115x126 px; içindeki delik 100 px çapında ve (57,56.5) merkezli (alttaki sivri uç yüzünden görsel yüksek).
    private const double RingW = 115, RingH = 126, HoleCx = 57.5, HoleCy = 57, HoleR = 50;

    /// <summary>
    /// Yuvarlak, halkalı portre. <paramref name="size"/> halka görselinin genişliğidir; yükseklik alttaki sivri uç yüzünden ~%10 fazladır.
    /// Portre, halkanın deliğine tam oturacak biçimde (hafif taşarak) ölçeklenir ve ortalanır; görsel yoksa fraksiyon renginde daire çizilir.
    /// </summary>
    public static FrameworkElement Medallion(string? id, double size, bool glow = false)
    {
        if (id == null || (id != Default && !CardCatalog.Default.TryGet(id, out _))) id = Default;   // yalnız görüntü: herhangi bir kart yüzü olur
        var k = size / RingW;
        var g = new Grid { Width = size, Height = RingH * k, IsHitTestVisible = false };
        var r = (HoleR + 3) * k;   // deliğin kenarının altına 3 px girer: aradan arka plan/parıltı sızmaz
        Brush? fill = null;
        if (id == Default)
        {
            if (Assets.Ui("avatar_geralt") is { } src) fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
        }
        else if (Assets.Card(id) is { } art)
            fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.1, 0.03, 0.8, 0.56), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
        g.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(HoleCx * k - r, HoleCy * k - r, 0, 0), Fill = fill ?? Theme.FactionColor(FactionOf(id))
        });
        if (Assets.Ui("ring") is { } ring)
            g.Children.Add(new Image { Source = ring, Width = size, Height = RingH * k, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        else
            g.Children.Add(new Ellipse
            {
                Width = (HoleR + 4) * 2 * k, Height = (HoleR + 4) * 2 * k, Stroke = Theme.GoldDim, StrokeThickness = Math.Max(2, size * 0.04),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(HoleCx * k - (HoleR + 4) * k, HoleCy * k - (HoleR + 4) * k, 0, 0)
            });
        if (glow) g.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = size * 0.18, ShadowDepth = 0, Opacity = 0.75 };
        return g;
    }
}
