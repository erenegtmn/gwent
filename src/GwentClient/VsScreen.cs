using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

public sealed record VsSideInfo(string Name, string Faction, string LeaderId, bool Mine);

/// <summary>
/// Maç başlamadan önce iki oyuncunun karşı karşıya geldiği ekran (yerel ve online ortak). Tıklayınca ya da ~3 sn sonra
/// <c>done</c> çağrılır. <c>animate:false</c> durağan çizer ve <c>done</c> çağırmaz (ekran görüntüsü için).
/// </summary>
public sealed class VsScreen : Grid
{
    public VsScreen(VsSideInfo left, VsSideInfo right, Action done, bool animate)
    {
        Background = Theme.B("#ff0a0705");
        var l = Side(left);
        var r = Side(right);
        var cols = new Grid { VerticalAlignment = VerticalAlignment.Center };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(l, 0);
        SetColumn(r, 2);
        cols.Children.Add(l);
        cols.Children.Add(r);

        var vs = Ui.Text("VS", 120, Theme.B("#e8c970"), true, TextAlignment.Center);
        vs.FontFamily = Theme.Serif;
        vs.Margin = new Thickness(30, 0, 30, 0);
        vs.VerticalAlignment = VerticalAlignment.Center;
        vs.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 170, 60), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.9 };
        vs.RenderTransformOrigin = new Point(0.5, 0.5);
        var vsScale = new ScaleTransform(1, 1);
        vs.RenderTransform = vsScale;
        SetColumn(vs, 1);
        cols.Children.Add(vs);
        Children.Add(cols);

        if (!animate) return;

        Sfx.Play("gwint_start");
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        MenuView.Slide(l, -320, 0, 700, 150);
        MenuView.Slide(r, 320, 0, 700, 150);
        vs.Opacity = 0;
        vs.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950)))
            }
        });
        var pop = new DoubleAnimation(3, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(700), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        vsScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        vsScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        Fx.After(1100, () => Sfx.Play("hero"));

        var finished = false;
        void Finish() { if (finished) return; finished = true; done(); }
        MouseLeftButtonUp += (_, _) => Finish();
        Cursor = System.Windows.Input.Cursors.Hand;
        Fx.After(3200, Finish);
    }

    private static FrameworkElement Side(VsSideInfo info)
    {
        var leader = CardCatalog.Default.Get(info.LeaderId);
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 420 };

        var avatar = new Grid { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Center };
        var inner = Assets.Ui(info.Mine ? "avatar_geralt" : "avatar_silhouette");
        if (inner != null) avatar.Children.Add(new System.Windows.Controls.Image { Source = inner, Width = 108, Height = 108, Stretch = Stretch.UniformToFill, Clip = new EllipseGeometry(new Point(54, 54), 54, 54) });
        else avatar.Children.Add(new Ellipse { Width = 108, Height = 108, Fill = Theme.FactionColor(info.Faction) });
        var ring = Assets.Ui("ring");
        if (ring != null) avatar.Children.Add(new System.Windows.Controls.Image { Source = ring, Width = 150, Height = 150 });
        avatar.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.8 };
        sp.Children.Add(avatar);

        var name = Ui.Text(info.Name, 38, Theme.B("#f0d890"), true, TextAlignment.Center);
        name.FontFamily = Theme.Serif;
        name.Margin = new Thickness(0, 10, 0, 0);
        name.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1.5, Opacity = 0.95 };
        sp.Children.Add(name);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };
        var shield = Assets.Ui("shield_" + info.Faction);
        if (shield != null) row.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0) });
        var fac = Ui.Text(Texts.Faction(info.Faction), 22, Theme.B("#c9c0aa"), false, TextAlignment.Left);
        fac.FontFamily = Theme.Serif;
        fac.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(fac);
        sp.Children.Add(row);

        var card = new GwintCard(leader, 230, null, null, height: 230 * GwintCard.Aspect) { IsHitTestVisible = false };
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 5, Opacity = 0.9 };
        card.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(card);
        var ln = Ui.Text(leader.Name, 18, Theme.B("#e8dcc4"), false, TextAlignment.Center);
        ln.FontFamily = Theme.Serif;
        ln.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(ln);
        return sp;
    }
}
