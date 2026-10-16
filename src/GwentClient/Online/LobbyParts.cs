using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GwentClient;

/// <summary>Bekleme odası ile turnuva bekleme odasının ortak üst bölümü: taş harfli kod levhaları ve host için davet kodu.</summary>
internal static class LobbyParts
{
    /// <summary>Sağ üst köşe: etiket + kod (taş levhalar) + Kopyala; host ise altında davet kodu kutusu ve Kopyala.</summary>
    public static StackPanel CodePanel(string? code, OnlineController c, string label)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Tag = "lobby-code" };
        var codeLabel = Ui.Text(label, 13, Theme.TextDim, true);
        codeLabel.VerticalAlignment = VerticalAlignment.Center;
        codeLabel.Margin = new Thickness(0, 0, 16, 0);
        codeRow.Children.Add(codeLabel);
        foreach (var ch in code ?? "…")
        {
            var tile = new Grid { Width = 58, Height = 76, Margin = new Thickness(4, 0, 0, 0) };
            tile.Children.Add(new ChamferFrame { Fill = Theme.B("#18110a"), Edge = Ornate.GoldEdge, Inner = Ornate.GoldThin, Cut = 9, Inset = 4 });
            var tx = Ui.Text(ch.ToString(), 46, Theme.B("#f4dc98"), true, TextAlignment.Center);
            tx.HorizontalAlignment = HorizontalAlignment.Center;
            tx.VerticalAlignment = VerticalAlignment.Center;
            tx.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 160, 50), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 };
            tile.Children.Add(tx);
            codeRow.Children.Add(tile);
        }
        var copy = Ui.Button("Kopyala", () =>
        {
            try { Clipboard.SetText(code ?? ""); } catch { /* pano başka uygulamada kilitli olabilir */ }
        });
        copy.VerticalAlignment = VerticalAlignment.Center;
        copy.Margin = new Thickness(16, 0, 0, 0);
        copy.FontSize = 14;
        codeRow.Children.Add(copy);
        panel.Children.Add(codeRow);
        if (c.IsHost) AddInvite(panel, c);
        return panel;
    }

    /// <summary>Host için davet kodu: en iyi yöntemin kodu tek kutuda, yanında Kopyala. Hiç yöntem yoksa kısa bir uyarı.</summary>
    private static void AddInvite(StackPanel panel, OnlineController c)
    {
        var invites = c.Invites;
        if (invites.Count == 0)
        {
            panel.Children.Add(Ui.Text("Bağlantı yöntemi yok: arkadaşın bağlanamaz.", 14, Theme.Bad, false, TextAlignment.Right));
            return;
        }
        var best = invites[0];
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var label = Ui.Text("DAVET KODU", 13, Theme.TextDim, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(label);
        var box = new TextBox { Text = best.Invite, IsReadOnly = true, Width = 470, TextWrapping = TextWrapping.Wrap, Tag = "lobby-invite", FontFamily = Theme.Serif, FontSize = 15 };
        box.Style = (Style)Application.Current.FindResource("GText");
        row.Children.Add(box);
        var b = Ui.Button("Kopyala", () => { try { Clipboard.SetText(best.Invite); } catch { /* pano kilitli olabilir */ } });
        b.FontSize = 13;
        b.Margin = new Thickness(10, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(b);
        panel.Children.Add(row);
        // Oda kodu tek başına internetten bağlatmaz (merkezi sunucu yok, adres davet kodunun içinde): bunu açıkça söyle
        var hint = Ui.Text("Bunu arkadaşına gönder, Online → Odaya katıl'a yapıştırsın. Oda kodu tek başına yetmez.", 14, Theme.B("#c9b88a"), false, TextAlignment.Right, true);
        hint.MaxWidth = 640;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(hint);
    }
}
