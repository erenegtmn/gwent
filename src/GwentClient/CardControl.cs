using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

public enum CardSize { Board, Hand, Preview }

/// <summary>
/// Yer tutucu kart görünümü: fraksiyon rengi, güç rozeti, yetenek etiketleri ve isim.
/// Orijinal kart resimleri gelince yalnız bu sınıf değişecek.
/// </summary>
public sealed class CardControl : Border
{
    public CardInstance? Card { get; }
    public CardDef Def { get; }

    public static (double W, double H) Dimensions(CardSize s) => s switch
    {
        CardSize.Board => (68, 96),
        CardSize.Hand => (98, 138),
        _ => (210, 296),
    };

    public CardControl(CardDef def, CardSize size, int? currentStrength = null, CardInstance? card = null,
        bool selected = false, bool dimmed = false, bool highlight = false)
    {
        Def = def;
        Card = card;
        var (w, h) = Dimensions(size);
        Width = w;
        Height = h;
        var k = w / 98.0; // yazı ölçeği
        CornerRadius = new CornerRadius(6 * Math.Max(0.7, k));
        BorderThickness = new Thickness(selected || highlight ? 3 : def.Hero ? 2.5 : 1.2);
        BorderBrush = selected ? Theme.Select : highlight ? Theme.Gold : def.Hero ? Theme.Gold : Theme.B("#5a4a32");
        Margin = new Thickness(selected ? 3 : 3, selected ? 0 : 3, 3, selected ? 6 : 3);
        Cursor = System.Windows.Input.Cursors.Hand;
        Opacity = dimmed ? 0.45 : 1;
        SnapsToDevicePixels = true;
        ToolTip = BuildToolTip(def, currentStrength);

        var faction = Theme.FactionColor(def.Faction);
        var accent = Theme.FactionAccent(def.Faction);
        Background = new LinearGradientBrush(((SolidColorBrush)faction).Color, Color.Multiply(((SolidColorBrush)faction).Color, 0.55f), 90);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Gövde: yetenek etiketleri / özel kart başlığı
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 14 * k, 2, 0) };
        foreach (var label in BodyLabels(def))
        {
            body.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = accent,
                FontSize = Math.Max(8.5, 10.5 * k),
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.92
            });
        }
        Grid.SetRow(body, 0);
        grid.Children.Add(body);

        // Alt bant: isim
        var nameBand = new Border { Background = Theme.B("#aa000000"), Padding = new Thickness(3, 2, 3, 3) };
        nameBand.Child = new TextBlock
        {
            Text = def.Name,
            Foreground = Theme.Text,
            FontSize = Math.Max(8, 9.5 * k),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 3 * 12 * k,
            FontWeight = def.Hero ? FontWeights.Bold : FontWeights.Normal
        };
        Grid.SetRow(nameBand, 1);
        grid.Children.Add(nameBand);

        // Güç rozeti
        if (def.IsUnit)
        {
            var shown = currentStrength ?? def.Strength;
            var fg = shown > def.Strength ? Theme.Good : shown < def.Strength ? Theme.Bad : def.Hero ? Theme.B("#18130c") : Theme.B("#18130c");
            var badge = new Grid { Width = 30 * k, Height = 30 * k, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3) };
            badge.Children.Add(new Ellipse
            {
                Fill = def.Hero ? Theme.Gold : Theme.B("#e8dcc4"),
                Stroke = def.Hero ? Theme.B("#6a5420") : Theme.B("#5a4a32"),
                StrokeThickness = 1.5
            });
            // Ham güç farklıysa rengi vurgulamak için sayının arka planını koyulaştırmak yerine yazı rengini kullanırız.
            badge.Children.Add(new TextBlock
            {
                Text = shown.ToString(),
                FontWeight = FontWeights.Bold,
                FontSize = 16 * k,
                Foreground = shown == def.Strength ? Theme.B("#18130c") : (shown > def.Strength ? Theme.B("#1d6b1d") : Theme.B("#a32a1a")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            _ = fg;
            grid.Children.Add(badge);

            // Sıra göstergesi
            var rowText = def.Row switch { "close" => "Y", "ranged" => "M", "siege" => "K", "agile" => "Y/M", _ => "" };
            var rowBadge = new TextBlock
            {
                Text = rowText,
                Foreground = Theme.TextDim,
                FontSize = 9 * Math.Max(0.85, k),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 5, 0),
                FontWeight = FontWeights.Bold
            };
            grid.Children.Add(rowBadge);
        }
        else if (def.IsLeader)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "LİDER",
                Foreground = Theme.Gold,
                FontSize = 9 * Math.Max(0.85, k),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        Child = grid;
    }

    private static IEnumerable<string> BodyLabels(CardDef d)
    {
        if (d.IsLeader)
        {
            yield return Texts.Faction(d.Faction);
            yield break;
        }
        if (d.IsWeather || d.IsSpecial)
        {
            yield return d.Id switch
            {
                Ids.Decoy => "YEM",
                Ids.Horn => "BOYNUZ",
                Ids.Scorch => "YAK",
                Ids.Mardroeme => "MARDROEME",
                Ids.ClearWeather => "AÇIK HAVA",
                Ids.BitingFrost => "DON",
                Ids.ImpenetrableFog => "SİS",
                Ids.TorrentialRain => "YAĞMUR",
                Ids.SkelligeStorm => "FIRTINA",
                _ => d.Name
            };
            yield break;
        }
        if (d.Hero) yield return "★ Kahraman";
        foreach (var a in d.Abilities) yield return Texts.AbilityLabel(a);
    }

    public static ToolTip BuildToolTip(CardDef d, int? current = null)
    {
        var sp = new StackPanel { MaxWidth = 300 };
        sp.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.Bold, FontSize = 14 });
        sp.Children.Add(new TextBlock { Text = Texts.Faction(d.Faction) + (d.Hero ? " • Kahraman" : ""), Foreground = Brushes.Gray, FontSize = 11 });
        if (d.IsUnit)
        {
            var s = $"Güç {d.Strength}" + (current != null && current != d.Strength ? $" (şu an {current})" : "") + $" • {Texts.RowOf(d)}";
            sp.Children.Add(new TextBlock { Text = s, Margin = new Thickness(0, 4, 0, 0) });
        }
        foreach (var a in d.Abilities)
            sp.Children.Add(new TextBlock { Text = Texts.AbilityText(a), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (d.IsLeader)
            sp.Children.Add(new TextBlock { Text = Texts.LeaderText(d), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (d.Hero)
            sp.Children.Add(new TextBlock { Text = "Kahraman: hava, Yak ve Yem gibi özel etkilerden etkilenmez.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.DarkGoldenrod });
        return new ToolTip { Content = sp, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
    }
}
