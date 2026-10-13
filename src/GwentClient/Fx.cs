using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace GwentClient;

/// <summary>Küçük, tek seferlik animasyonlar. Hepsi yalnız görünümdür: oyun durumuna dokunmaz.</summary>
public static class Fx
{
    private static readonly IEasingFunction Out = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Kart sahaya iner: büyük ve saydam başlar, yerine oturur. <paramref name="fromY"/> kartın geldiği yön.</summary>
    public static void CardIn(FrameworkElement el, double fromY)
    {
        var scale = new ScaleTransform(1.4, 1.4);
        var move = new TranslateTransform(0, fromY);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { scale, move } };

        var d = TimeSpan.FromMilliseconds(320);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.4, 1, d) { EasingFunction = Out });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.4, 1, d) { EasingFunction = Out });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, d) { EasingFunction = Out });
        var target = el.Opacity;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>Değişen bir sayıyı kısa süre büyütüp eski haline getirir.</summary>
    public static void Pulse(FrameworkElement el)
    {
        var scale = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        var a = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Out };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Üzerine gelince kartı hafifçe yukarı kaldırır.</summary>
    public static void HoverLift(FrameworkElement el, double dy = -16)
    {
        var move = new TranslateTransform();
        el.RenderTransform = move;
        el.MouseEnter += (_, _) => move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, TimeSpan.FromMilliseconds(110)) { EasingFunction = Out });
        el.MouseLeave += (_, _) => move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = Out });
    }

    /// <summary>Banner: hızla belirir, bir süre durur, silinir. Bitince <paramref name="done"/> çağrılır.</summary>
    public static void Banner(UIElement el, int holdMs, Action done)
    {
        var total = 220 + holdMs + 420;
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(total), FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220 + holdMs))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total))));
        anim.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private static readonly IEasingFunction Cubic = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly Dictionary<string, List<ImageSource>> Frames = new();

    private static readonly IEasingFunction Drop = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2.2 };

    /// <summary>
    /// Kartı <paramref name="from"/> konumundan yerine "düşürür": başta yavaş, sonra hızlanarak iner (ağırlık hissi),
    /// yere çarpınca hafifçe ezilir ve geri sıçrar.
    /// </summary>
    public static void FlyIn(FrameworkElement el, Point from, Point to, int ms = 520, int delayMs = 0)
    {
        var finalOpacity = el.Opacity;
        var scale = new ScaleTransform(1.25, 1.25);
        var move = new TranslateTransform(from.X - to.X, from.Y - to.Y);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { scale, move } };
        Panel.SetZIndex(el, 30);
        var dur = TimeSpan.FromMilliseconds(ms);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - to.X, 0, dur) { EasingFunction = Cubic, BeginTime = begin });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y - to.Y, 0, dur) { EasingFunction = Drop, BeginTime = begin });

        // Boyut: iner, çarpınca ezilir (0.93), toparlar
        var sk = new DoubleAnimationUsingKeyFrames { BeginTime = begin, FillBehavior = FillBehavior.HoldEnd };
        sk.KeyFrames.Add(new LinearDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sk.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(dur), Drop));
        sk.KeyFrames.Add(new EasingDoubleKeyFrame(0.93, KeyTime.FromTimeSpan(dur + TimeSpan.FromMilliseconds(70)), Out));
        sk.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(dur + TimeSpan.FromMilliseconds(190)), Out));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sk);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sk);

        el.Opacity = 0;
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.Zero, FillBehavior = FillBehavior.HoldEnd };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(begin)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(finalOpacity, KeyTime.FromTimeSpan(begin + TimeSpan.FromMilliseconds(Math.Min(140, ms)))));
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Bir hayalet öğeyi hedefe uçurup solar ve işi bitince <paramref name="done"/> çağrılır.</summary>
    public static void FlyAway(FrameworkElement el, Point to, int ms, int delayMs, Action done)
    {
        var tt = new TranslateTransform();
        var sc = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { sc, tt } };
        var from = new Point(Canvas.GetLeft(el), Canvas.GetTop(el));
        var dur = TimeSpan.FromMilliseconds(ms);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, to.X - from.X, dur) { EasingFunction = Cubic, BeginTime = begin });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, to.Y - from.Y, dur) { EasingFunction = Cubic, BeginTime = begin });
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.8, dur) { BeginTime = begin });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.8, dur) { BeginTime = begin });
        var fade = new DoubleAnimation(1, 0, dur) { BeginTime = begin };
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Yerinde solarak kaybolur (yanma, dönüşüm).</summary>
    public static void FadeOut(FrameworkElement el, int ms, int delayMs, Action done)
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Tahtayı kısa süre sarsar (Yak gibi şiddetli etkiler).</summary>
    public static void Shake(FrameworkElement el, double amp = 7, int ms = 380)
    {
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        // Büyük ve efektli bir öğeyi her karede yeniden çizmek pahalı: sarsıntı boyunca GPU dokusu olarak önbelleğe alınır
        el.CacheMode = new BitmapCache(1.0);
        var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms), FillBehavior = FillBehavior.Stop };
        var steps = new[] { 0.0, -amp, amp, -amp * 0.7, amp * 0.6, -amp * 0.3, 0 };
        for (var i = 0; i < steps.Length; i++)
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromPercent((double)i / (steps.Length - 1))));
        kf.Completed += (_, _) => el.CacheMode = null;
        tt.BeginAnimation(TranslateTransform.XProperty, kf);
    }

    /// <summary>Mücevher kırılırken: büyür, döner ve söner.</summary>
    public static void Shatter(FrameworkElement el, Action done)
    {
        var sc = new ScaleTransform(1, 1);
        var rot = new RotateTransform(0);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { sc, rot } };
        var dur = TimeSpan.FromMilliseconds(650);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.9, dur) { EasingFunction = Out });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.9, dur) { EasingFunction = Out });
        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 25, dur));
        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public static void After(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    private static List<ImageSource> Seq(string name)
    {
        lock (Frames)
        {
            if (Frames.TryGetValue(name, out var list)) return list;
            list = new List<ImageSource>();
            for (var i = 0; ; i++)
            {
                var f = Assets.Ui($"fx/{name}_{i:00}");
                if (f == null) break;
                list.Add(f);
            }
            return Frames[name] = list;
        }
    }

    /// <summary>
    /// Oyunun efekt kare dizisini (yıldırım, duman, ateş, patlama...) merkez noktasında oynatır; bitince kendini kaldırır.
    /// Kare dosyaları yoksa sessizce hiçbir şey yapmaz.
    /// </summary>
    public static void PlayFrames(Canvas layer, string sequence, Point center, double w, double h, int fps = 30, int delayMs = 0, double opacity = 1)
    {
        var frames = Seq(sequence);
        if (frames.Count == 0) return;
        var img = new Image { Source = frames[0], Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(img, center.X - w / 2);
        Canvas.SetTop(img, center.Y - h / 2);
        Panel.SetZIndex(img, 40);
        layer.Children.Add(img);
        System.Diagnostics.Stopwatch? sw = null;
        EventHandler? onFrame = null;
        onFrame = (_, _) =>
        {
            var index = (int)(sw!.ElapsedMilliseconds * fps / 1000.0);
            if (index >= frames.Count) { CompositionTarget.Rendering -= onFrame; layer.Children.Remove(img); return; }
            if (!ReferenceEquals(img.Source, frames[index])) img.Source = frames[index];
        };
        Action start = () => { img.Opacity = opacity; sw = System.Diagnostics.Stopwatch.StartNew(); CompositionTarget.Rendering += onFrame; };
        if (delayMs <= 0) start(); else After(delayMs, start);
    }
}
