using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GwentClient;

public static class Ui
{
    public static TextBlock Text(string text, double size = 13, Brush? fg = null, bool bold = false, TextAlignment align = TextAlignment.Left, bool wrap = false)
        => new()
        {
            Text = text,
            FontFamily = Theme.Serif,
            FontSize = size * Theme.FontScale,
            Foreground = fg ?? Theme.Text,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = align,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };

    public static Button Button(string text, Action onClick, bool primary = false, bool enabled = true)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource(primary ? "GBtnPrimary" : "GBtn"),
            IsEnabled = enabled,
            FontFamily = Theme.Serif
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Border Card(UIElement child, Brush? bg = null, double pad = 10, double radius = 8) => new()
    {
        Background = bg ?? Theme.Panel,
        BorderBrush = Theme.PanelEdge,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = new Thickness(pad),
        Child = child
    };

    /// <summary>Bir öğeyi pencere açmadan PNG'ye çizer (görsel doğrulama).</summary>
    public static void RenderToPng(FrameworkElement element, double width, double height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }
}
