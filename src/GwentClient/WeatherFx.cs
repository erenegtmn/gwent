using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GwentClient;

/// <summary>
/// Hava kartlarının sıra üstündeki canlı görünümü (yalnız görünüm). Oyunun kendi parçalarından kurulur:
/// don kenarlardan büyüyen kristal ve parıldayan kar tanesi, akan sis, yağan yağmur, Skellige'de bulut ve şimşek.
/// </summary>
public static class WeatherFx
{
    private static readonly Random Rng = new();

    /// <param name="type">frost | fog | rain | storm</param>
    public static FrameworkElement Make(string type, double w, double h, bool intro)
    {
        var c = new Canvas { Width = w, Height = h, ClipToBounds = true, IsHitTestVisible = false };
        switch (type)
        {
            case "frost": Frost(c, w, h, intro); break;
            case "fog": Fog(c, w, h); break;
            case "rain": Rain(c, w, h); break;
            default: Storm(c, w, h); break;
        }
        if (intro)
            c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1100)));
        return c;
    }

    // ---------- parçalar ----------

    private static Image Img(string name, double w, double h, double opacity)
    {
        var im = new Image { Source = Assets.Ui(name), Width = w, Height = h, Stretch = Stretch.Fill, Opacity = opacity, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
        return im;
    }

    private static void Tint(Canvas c, double w, double h, string argb)
    {
        c.Children.Add(new Rectangle { Width = w, Height = h, Fill = Theme.B(argb), IsHitTestVisible = false });
    }

    /// <summary>Aynalı döşemeyle kesintisiz kayan şerit: yatayda sola, dikeyde aşağı.</summary>
    private static void Scroller(Canvas c, string name, double w, double h, double opacity, double seconds, bool vertical, bool reverse = false)
    {
        var strip = new Canvas { Width = vertical ? w : w * 4, Height = vertical ? h * 4 : h, IsHitTestVisible = false };
        for (var i = 0; i < 4; i++)
        {
            var im = Img(name, w, h, opacity);
            im.RenderTransformOrigin = new Point(0.5, 0.5);
            if (i % 2 == 1) im.RenderTransform = vertical ? new ScaleTransform(1, -1) : new ScaleTransform(-1, 1);
            Canvas.SetLeft(im, vertical ? 0 : i * w);
            Canvas.SetTop(im, vertical ? i * h : 0);
            strip.Children.Add(im);
        }
        var tt = new TranslateTransform();
        strip.RenderTransform = tt;
        var period = vertical ? h * 2 : w * 2;
        var from = reverse ? -period : 0;
        var to = reverse ? 0 : -period;
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
        // dikeyde yağmur aşağı düşer (artan Y), yatayda akış sola
        if (vertical) anim = new DoubleAnimation(-period, 0, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
        tt.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, anim);
        c.Children.Add(strip);
    }

    private static void Pulse(UIElement el, double max, double seconds, double beginSeconds)
    {
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, max, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    // ---------- türler ----------

    private static void Frost(Canvas c, double w, double h, bool intro)
    {
        if (Assets.Ui("wx_frost") == null) { Tint(c, w, h, "#554a78a8"); return; }
        var layer = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
        layer.Children.Add(Img("wx_frost", w, h, 0.85));
        var alt = Img("wx_frost_alt", w, h, 0.6);
        layer.Children.Add(alt);
        Pulse(alt, 0.6, 2.6, 0);
        // Parıldayan kar taneleri
        for (var i = 0; i < 9; i++)
        {
            var s = 22 + Rng.NextDouble() * 22;
            var f = Img("wx_frost_flake", s, s * 0.86, 1);
            Canvas.SetLeft(f, Rng.NextDouble() * (w - s));
            Canvas.SetTop(f, Rng.NextDouble() * (h - s));
            layer.Children.Add(f);
            Pulse(f, 0.85, 1.2 + Rng.NextDouble() * 2, Rng.NextDouble() * 3);
        }
        c.Children.Add(layer);

        if (intro)
        {
            // Kristal iki kenardan ortaya doğru büyür
            var left = new RectangleGeometry(new Rect(0, 0, 0, h));
            var right = new RectangleGeometry(new Rect(w, 0, 0, h));
            layer.Clip = new GeometryGroup { Children = { left, right } };
            var dur = TimeSpan.FromMilliseconds(1400);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var l = new RectAnimation(new Rect(0, 0, 0, h), new Rect(0, 0, w / 2 + 2, h), dur) { EasingFunction = ease };
            var r = new RectAnimation(new Rect(w, 0, 0, h), new Rect(w / 2 - 2, 0, w / 2 + 2, h), dur) { EasingFunction = ease };
            r.Completed += (_, _) => layer.Clip = null;
            left.BeginAnimation(RectangleGeometry.RectProperty, l);
            right.BeginAnimation(RectangleGeometry.RectProperty, r);
        }
    }

    private static void Fog(Canvas c, double w, double h)
    {
        if (Assets.Ui("wx_fog") == null) { Tint(c, w, h, "#55b0b8b8"); return; }
        // Yumuşak sis tabakası (nefes alır) + üst üste binen, kesintisiz kayan iki sis şeridi
        var haze = new Rectangle
        {
            Width = w, Height = h, IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromArgb(0x00, 0xd6, 0xdc, 0xda), Color.FromArgb(0x90, 0xc8, 0xcf, 0xcc), 90)
        };
        c.Children.Add(haze);
        Pulse(haze, 1, 3.5, 0);
        Drift(c, "wx_fog", w, h, 0.85, 30, false);
        Drift(c, "wx_fog", w, h, 0.55, 46, true);
    }

    /// <summary>Aynı görüntünün üst üste binen kopyaları; adım kadar kayıp başa sararak kesintisiz akar.</summary>
    private static void Drift(Canvas c, string name, double w, double h, double opacity, double seconds, bool reverse)
    {
        var step = w * 0.7;
        var strip = new Canvas { Width = w + step * 3, Height = h, IsHitTestVisible = false };
        for (var i = 0; i < 4; i++)
        {
            var im = Img(name, w, h, opacity);
            Canvas.SetLeft(im, i * step);
            strip.Children.Add(im);
        }
        var tt = new TranslateTransform();
        strip.RenderTransform = tt;
        tt.BeginAnimation(TranslateTransform.XProperty, reverse
            ? new DoubleAnimation(-step, 0, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever }
            : new DoubleAnimation(0, -step, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever });
        c.Children.Add(strip);
    }

    private static void Rain(Canvas c, double w, double h)
    {
        if (Assets.Ui("wx_rain") == null) { Tint(c, w, h, "#55405878"); return; }
        c.Children.Add(Img("wx_rain", w, h, 0.85));
        Scroller(c, "wx_rain_streaks", w, h, 0.45, 0.65, vertical: true);
        Scroller(c, "wx_rain_streaks", w, h, 0.3, 0.9, vertical: true);
    }

    private static void Storm(Canvas c, double w, double h)
    {
        if (Assets.Ui("wx_cloud") == null) { Tint(c, w, h, "#55303860"); return; }
        Tint(c, w, h, "#55182438");
        Scroller(c, "wx_rain_streaks", w, h, 0.45, 0.6, vertical: true);
        // Süzülen bulutlar
        for (var i = 0; i < 4; i++)
        {
            var s = h * (1.4 + Rng.NextDouble() * 0.8);
            var cloud = Img("wx_cloud", s, s, 0.4);
            var tt = new TranslateTransform();
            cloud.RenderTransform = tt;
            Canvas.SetLeft(cloud, -s);
            Canvas.SetTop(cloud, -s * 0.35);
            c.Children.Add(cloud);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, w + s, TimeSpan.FromSeconds(16 + Rng.NextDouble() * 12))
                { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-Rng.NextDouble() * 20) });
        }
        // Şimşek: rastgele aralıklarla çift çakar
        var bolt = Img("wx_lightning", w * 0.6, h * 1.1, 0);
        Canvas.SetLeft(bolt, Rng.NextDouble() * w * 0.4);
        Canvas.SetTop(bolt, -h * 0.05);
        c.Children.Add(bolt);
        var flash = new Rectangle { Width = w, Height = h, Fill = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        c.Children.Add(flash);
        var period = 4.5 + Rng.NextDouble() * 3;
        UIElement[] targets = { bolt, flash };
        double[] peaks = { 0.95, 0.35 };
        for (var k = 0; k < 2; k++)
        {
            var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(period), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(Rng.NextDouble() * 3) };
            double T(double s) => s;
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(T(0)))));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(peaks[k], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period - 0.6))));
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period - 0.5))));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(peaks[k] * 0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period - 0.38))));
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period - 0.1))));
            targets[k].BeginAnimation(UIElement.OpacityProperty, kf);
        }
    }
}
