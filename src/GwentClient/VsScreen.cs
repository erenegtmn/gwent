using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

public sealed record VsSideInfo(string Name, string Faction, string LeaderId, bool Mine, string? Avatar = null);

/// <summary>
/// Maç başlamadan önce iki oyuncunun karşı karşıya geldiği sinematik ekran (yerel ve online ortak): ekran çapraz bir kılıç darbesiyle
/// ikiye bölünür, her yarı fraksiyonun rengiyle yanar; liderler iki yandan kayarak gelir, "VS" çarpar (flaş + sarsıntı).
/// Tıklayınca ya da ~3,4 sn sonra <c>done</c> çağrılır. <c>animate:false</c> durağan çizer ve <c>done</c> çağırmaz (ekran görüntüsü için).
/// </summary>
public sealed class VsScreen : Grid
{
    public VsScreen(VsSideInfo left, VsSideInfo right, Action done, bool animate)
    {
        Background = Theme.B("#ff070504");
        ClipToBounds = true;

        // --- Zemin: iki yarı (çapraz kesim) fraksiyon rengiyle ---
        Children.Add(Half(left.Faction, true));
        Children.Add(Half(right.Faction, false));
        Children.Add(Slash());
        Children.Add(Ornate.Embers(34));

        var l = Side(left);
        var r = Side(right);
        var cols = new Grid { VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform() };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(l, 0);
        SetColumn(r, 2);
        cols.Children.Add(l);
        cols.Children.Add(r);

        var vs = Ui.Text("VS", 150, Theme.B("#f0d890"), true, TextAlignment.Center);
        vs.FontFamily = Theme.Serif;
        vs.VerticalAlignment = VerticalAlignment.Center;
        vs.HorizontalAlignment = HorizontalAlignment.Center;
        vs.Foreground = new LinearGradientBrush(Color.FromRgb(0xff, 0xf0, 0xb8), Color.FromRgb(0xc2, 0x8a, 0x2c), 90);
        vs.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 150, 40), BlurRadius = 50, ShadowDepth = 0, Opacity = 0.95 };
        vs.RenderTransformOrigin = new Point(0.5, 0.5);
        var vsScale = new ScaleTransform(1, 1);
        vs.RenderTransform = vsScale;
        SetColumn(vs, 1);
        cols.Children.Add(vs);
        Children.Add(cols);

        // --- Sinema şeritleri ---
        var top = Bar(true);
        var bottom = Bar(false);
        Children.Add(top);
        Children.Add(bottom);
        var flash = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        Children.Add(flash);

        if (!animate) return;

        Sfx.Play("gwint_start");
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        // şeritler dışarıdan kapanır
        top.RenderTransform = new TranslateTransform(0, -120);
        bottom.RenderTransform = new TranslateTransform(0, 120);
        var bar = new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ((TranslateTransform)top.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bar);
        ((TranslateTransform)bottom.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bar);

        MenuView.Slide(l, -420, 0, 760, 200);
        MenuView.Slide(r, 420, 0, 760, 200);
        vs.Opacity = 0;
        vs.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))),
                new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(901)))
            }
        });
        var pop = new DoubleAnimation(4.5, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(900), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        vsScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        vsScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        // çarpma: beyaz flaş + kamera sarsıntısı
        flash.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150))),
                new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1170))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)))
            }
        });
        var shake = new DoubleAnimationUsingKeyFrames();
        var t0 = 1150;
        foreach (var (dt, dx) in new[] { (0, 0.0), (40, -22.0), (90, 18.0), (140, -12.0), (190, 8.0), (250, -4.0), (320, 0.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(dx, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t0 + dt))));
        ((TranslateTransform)cols.RenderTransform).BeginAnimation(TranslateTransform.XProperty, shake);
        Fx.After(1150, () => Sfx.Play("hero"));

        var finished = false;
        void Finish() { if (finished) return; finished = true; done(); }
        MouseLeftButtonUp += (_, _) => Finish();
        Cursor = System.Windows.Input.Cursors.Hand;
        Fx.After(3600, Finish);
    }

    private static Color FactionTone(string faction)
    {
        var c = ((SolidColorBrush)Theme.FactionColor(faction)).Color;
        // çok açık/koyu olanları (Nilfgaard) sıcak bir koyu tonla ayır
        if (faction == "nilfgaard") return Color.FromRgb(0xb0, 0x90, 0x30);
        return c;
    }

    /// <summary>Çapraz kesimli yarım zemin: fraksiyon renginden kararmaya doğru ışıma.</summary>
    private static UIElement Half(string faction, bool leftSide)
    {
        var c = FactionTone(faction);
        var poly = new Polygon
        {
            IsHitTestVisible = false,
            Points = leftSide ? new PointCollection { new Point(0, 0), new Point(56, 0), new Point(44, 100), new Point(0, 100) }
                              : new PointCollection { new Point(56, 0), new Point(100, 0), new Point(100, 100), new Point(44, 100) },
            Fill = new RadialGradientBrush
            {
                Center = new Point(leftSide ? 0.35 : 0.65, 0.5), GradientOrigin = new Point(leftSide ? 0.35 : 0.65, 0.5), RadiusX = 0.9, RadiusY = 0.95,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xa8, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(0x50, (byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)), 0.55),
                    new GradientStop(Color.FromArgb(0xf4, 0x05, 0x04, 0x03), 1)
                }
            }
        };
        var box = new Viewbox { Stretch = Stretch.Fill, IsHitTestVisible = false };
        box.Child = new Canvas { Width = 100, Height = 100, Children = { poly } };
        return box;
    }

    /// <summary>İki yarı arasındaki parlak kılıç izi.</summary>
    private static UIElement Slash()
    {
        var g = new Viewbox { Stretch = Stretch.Fill, IsHitTestVisible = false };
        var canvas = new Canvas { Width = 100, Height = 100 };
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection { new Point(55.35, 0), new Point(56.65, 0), new Point(44.65, 100), new Point(43.35, 100) },
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0xff, 0xe0, 0x90), 0), new GradientStop(Color.FromArgb(0xf0, 0xff, 0xf0, 0xc8), 0.5), new GradientStop(Color.FromArgb(0, 0xff, 0xe0, 0x90), 1)
            }, 90),
            Effect = new BlurEffect { Radius = 5 }
        });
        g.Child = canvas;
        return g;
    }

    private static UIElement Bar(bool top) => new Border
    {
        Height = 96, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, IsHitTestVisible = false,
        Background = new LinearGradientBrush(Color.FromRgb(0, 0, 0), Color.FromArgb(0xe8, 0, 0, 0), top ? 90 : 270),
        BorderBrush = Theme.B("#5a4520"), BorderThickness = top ? new Thickness(0, 0, 0, 1.5) : new Thickness(0, 1.5, 0, 0)
    };

    private static FrameworkElement Side(VsSideInfo info)
    {
        var leader = CardCatalog.Default.Get(info.LeaderId);
        var tone = FactionTone(info.Faction);
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 560 };

        var portrait = Portraits.Medallion(info.Avatar ?? (info.Mine ? Settings.Portrait : info.LeaderId), 112, glow: true);
        portrait.HorizontalAlignment = HorizontalAlignment.Center;
        portrait.Margin = new Thickness(0, 0, 0, 6);
        sp.Children.Add(portrait);

        // Oyuncu adı: büyük, aralıklı harfler; altında fraksiyon kalkanı
        var name = Ui.Text(Ornate.Spaced(info.Name.ToUpper(new System.Globalization.CultureInfo("tr-TR"))), info.Name.Length <= 7 ? 50 : info.Name.Length <= 10 ? 40 : info.Name.Length <= 13 ? 32 : 26, Theme.B("#f4e2a8"), true, TextAlignment.Center);
        name.FontFamily = Theme.Serif;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = 1 };
        name.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(new Border { Height = 64, Child = name });   // farklı uzunluktaki adlar iki yanı hizasız bırakmasın

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 18) };
        var shield = Assets.Ui("shield_" + info.Faction);
        if (shield != null) row.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 46, Height = 46, Margin = new Thickness(0, 0, 12, 0) });
        var fac = Ui.Text(Texts.Faction(info.Faction), 24, Theme.B("#d4c8aa"), false, TextAlignment.Left);
        fac.FontFamily = Theme.Serif;
        fac.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(fac);
        sp.Children.Add(row);

        // Lider kartı: büyük, fraksiyon renginde parlayan; hafif eğik
        var card = new GwintCard(leader, 300, null, null, height: 300 * GwintCard.Aspect) { IsHitTestVisible = false };
        card.Effect = new DropShadowEffect { Color = tone, BlurRadius = 60, ShadowDepth = 0, Opacity = 0.85 };
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new RotateTransform(info.Mine ? -2.5 : 2.5);
        sp.Children.Add(card);

        var ln = Ui.Text(leader.Name, 20, Theme.B("#e8dcc4"), false, TextAlignment.Center);
        ln.FontFamily = Theme.Serif;
        ln.Margin = new Thickness(0, 18, 0, 0);
        ln.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 1 };
        sp.Children.Add(ln);
        return sp;
    }
}
