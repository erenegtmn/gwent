using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>İstatistikler: toplam sonuç, seri, fraksiyon ve rakip bazında dökümler, son maçlar. Yalnız bu bilgisayardaki kayıtlardan.</summary>
public sealed class StatsView : Grid
{
    public StatsView(Action onBack, IReadOnlyList<MatchRecord>? records = null)
    {
        Background = Theme.Bg;
        ClipToBounds = true;
        var all = records ?? Stats.Load();

        var pano = Assets.Ui("pano_kaer_morhen");
        if (pano != null) Children.Add(new Image { Source = pano, Stretch = Stretch.UniformToFill, IsHitTestVisible = false });
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.9,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xc0, 0, 0, 0), 1) }
            }
        });

        var root = new StackPanel { Width = 820 };
        var title = Ui.Text("İstatistikler", 44, Theme.B("#e8c970"), true);
        title.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.95 };
        root.Children.Add(title);
        root.Children.Add(new Border { Height = 1.5, Width = 560, Margin = new Thickness(0, 8, 0, 18), HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.B("#8a6e2c") });

        if (all.Count == 0)
            root.Children.Add(Ui.Text("Henüz biten maç yok. Bir maç oyna, sonuçlar burada görünür.", 20, Theme.TextDim, wrap: true));
        else
        {
            var s = Stats.Summarize(all);
            root.Children.Add(Ui.Text($"{s.Games} maç — {s.Wins} galibiyet, {s.Draws} beraberlik, {s.Losses} yenilgi (%{s.WinPercent})", 24, Theme.Text, true));
            root.Children.Add(Ui.Text($"Güncel seri: {s.Streak}   En iyi seri: {s.BestStreak}", 18, Theme.TextDim));

            Section(root, "Rakibe göre", all.GroupBy(r => r.Opponent).OrderByDescending(g => g.Count()).Take(6),
                g => g.Key);
            Section(root, "Fraksiyonuna göre", all.GroupBy(r => r.MyFaction).OrderByDescending(g => g.Count()),
                g => Texts.Faction(g.Key));

            root.Children.Add(Ui.Text("Son maçlar", 22, Theme.B("#e8c970"), true));
            ((TextBlock)root.Children[^1]).Margin = new Thickness(0, 16, 0, 4);
            foreach (var r in all.AsEnumerable().Reverse().Take(8))
            {
                var res = r.Result > 0 ? "Kazandın" : r.Result < 0 ? "Kaybettin" : "Berabere";
                var t = Ui.Text($"{r.Date:dd.MM.yyyy HH:mm}   {res}   {r.Opponent}   ({Texts.FactionShort(r.MyFaction)} – {Texts.FactionShort(r.OppFaction)}){(r.Online ? "   online" : "")}", 16,
                    r.Result > 0 ? Theme.B("#d8bd78") : r.Result < 0 ? Theme.B("#a8503f") : Theme.TextDim);
                root.Children.Add(t);
            }
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        var back = Ui.Button("Geri", onBack, primary: true);
        back.FontSize = 20;
        back.Padding = new Thickness(44, 12, 44, 12);
        bar.Children.Add(back);
        if (all.Count > 0)
        {
            var clear = Ui.Button("Kayıtları sil", () =>
            {
                if (MessageBox.Show("Tüm istatistik kayıtları silinsin mi?", "Gwent", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                Stats.Clear();
                onBack();
            });
            clear.FontSize = 16;
            clear.Margin = new Thickness(20, 0, 0, 0);
            bar.Children.Add(clear);
        }
        root.Children.Add(bar);

        Children.Add(new Border
        {
            Child = root, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = Theme.B("#e6140e08"), BorderBrush = Theme.B("#8a6e2c"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(44, 34, 44, 38),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.85 }
        });
        KeyDown += (_, e) => { if (e.Key == Key.Escape) onBack(); };
        Focusable = true;
        Loaded += (_, _) => Focus();
    }

    private static void Section(StackPanel root, string heading, IEnumerable<IGrouping<string, MatchRecord>> groups, Func<IGrouping<string, MatchRecord>, string> label)
    {
        var h = Ui.Text(heading, 22, Theme.B("#e8c970"), true);
        h.Margin = new Thickness(0, 16, 0, 4);
        root.Children.Add(h);
        foreach (var g in groups)
        {
            var s = Stats.Summarize(g);
            root.Children.Add(Ui.Text($"{label(g)}:  {s.Wins}–{s.Draws}–{s.Losses}  (%{s.WinPercent})", 16, Theme.Text));
        }
    }
}
