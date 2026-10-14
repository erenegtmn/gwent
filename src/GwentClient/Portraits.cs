using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Oyuncu portreleri: oyunun karakterleri. Çoğu oyunun günlüğündeki karakter portresidir (<c>face_&lt;ad&gt;</c>, assets/ui/faces);
/// günlükte yüzü olmayanlar (Thaler) kendi Gwent kartından alınır. Bir portre yalnız kısa bir kimliktir; online'da rakibe bu kimlik gider,
/// görsel herkesin kendi kopyasından çizilir. Lider kartı kimlikleri de çizilebilir (yapay zekâ rakibinin yüzü).
/// </summary>
public static class Portraits
{
    public const string Default = "avatar_geralt";
    public const string FacePrefix = "face_";
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    /// <summary>Seçilebilir portreler: kimlik ve oyundaki ad.</summary>
    private static readonly (string Id, string Title)[] Choices =
    {
        (Default, "Geralt of Rivia"),
        ("face_yenn", "Yennefer of Vengerberg"), ("face_triss", "Triss Merigold"), ("face_ciri", "Ciri"), ("face_dandelion", "Dandelion"),
        ("face_zoltan", "Zoltan Chivay"), ("thaler", "Thaler"), ("face_vesemir", "Vesemir"), ("face_lambert", "Lambert"), ("face_eskel", "Eskel"),
        ("face_keira", "Keira Metz"), ("face_philipa", "Philippa Eilhart"), ("face_margarita", "Margarita Laux-Antille"), ("face_priscilla", "Priscilla"),
        ("face_roche", "Vernon Roche"), ("face_ves", "Ves"), ("face_dijkstra", "Sigismund Dijkstra"), ("face_radovid", "Radovid V"),
        ("face_baron", "Bloody Baron"), ("face_emhyr", "Emhyr var Emreis"), ("face_letho", "Letho of Gulet"),
        ("face_crach", "Crach an Craite"), ("face_cerys", "Cerys an Craite"), ("face_hjalmar", "Hjalmar an Craite"), ("face_mousesack", "Ermion"),
        ("face_eredin", "Eredin"), ("face_imlerith", "Imlerith"), ("face_caranthir", "Caranthir"),
        ("face_olgierd", "Olgierd von Everec"), ("face_shani", "Shani"), ("face_regis", "Regis"), ("face_henrietta", "Anna Henrietta"), ("face_syanna", "Syanna"),
    };

    /// <summary>Seçilebilir tüm portre kimlikleri.</summary>
    public static IReadOnlyList<string> All() => Choices.Select(c => c.Id).ToList();

    public static string Normalize(string? id) => id != null && Choices.Any(c => c.Id == id) ? id : Default;

    public static string Title(string id) =>
        Choices.FirstOrDefault(c => c.Id == id).Title ?? (CardCatalog.Default.TryGet(id, out var d) ? d.Name : id);

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
        // "face_<ad>": oyunun günlüğündeki karakter portresi (profil ve kariyer rakipleri); "face_none" ya da bulunamazsa siluet
        string? face = id != null && id.StartsWith(FacePrefix) ? id[FacePrefix.Length..] : null;
        if (face == null && (id == null || (id != Default && !CardCatalog.Default.TryGet(id, out _)))) id = Default;   // yalnız görüntü: herhangi bir kart yüzü olur
        var k = size / RingW;
        var g = new Grid { Width = size, Height = RingH * k, IsHitTestVisible = false };
        var r = (HoleR + 3) * k;   // deliğin kenarının altına 3 px girer: aradan arka plan/parıltı sızmaz
        Brush? fill = null;
        if (face != null)
        {
            if ((face != "none" ? Assets.Ui("faces/" + face) : null) is { } src)
                fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
            else if (Assets.Ui("avatar_silhouette") is { } sil)   // adı bilinmeyen karakter: oyunun silueti, kısık tonda
            {
                var dg = new DrawingGroup();
                dg.Children.Add(new GeometryDrawing(Theme.B("#1c140c"), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                dg.Children.Add(new ImageDrawing(sil, new Rect(0, 0, 1, 1)) { });
                dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x90, 0x14, 0x0e, 0x08)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                fill = new DrawingBrush(dg) { Stretch = Stretch.UniformToFill };
            }
            else fill = Theme.B("#3a2e20");
        }
        else if (id == Default)
        {
            if (Assets.Ui("avatar_geralt") is { } src) fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
        }
        else if (Assets.Card(id) is { } art)
            fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.1, 0.03, 0.8, 0.56), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
        g.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(HoleCx * k - r, HoleCy * k - r, 0, 0), Fill = fill ?? Theme.FactionColor(FactionOf(id!))
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
