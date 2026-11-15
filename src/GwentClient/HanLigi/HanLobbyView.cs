using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GwentNet;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi online bekleme odası: davet kodu, sekiz koltuk (gelen oyuncular portreleriyle; boş koltuklarda "bot"), oda kuranın
/// seçtiği oyuncu sayısı, "Hazırım". Odadaki bütün insanlar hazır olunca maç başlar, eksik koltukları bot doldurur.
/// Etkileşimli öğelerin etiketleri <c>hanlobby-*</c>.
/// </summary>
public sealed class HanLobbyView : Grid
{
    private readonly RoomLinkBase _link;
    private readonly IReadOnlyList<InviteOption> _invites;
    private readonly IReadOnlyList<string> _warnings;
    private readonly Func<Task> _leave;
    private readonly Canvas _root = new() { Width = 1920, Height = 1080 };
    private string _error = "";
    private bool _busy;

    public HanLobbyView(OnlineController controller, Func<Task> leave) : this(controller.Link, controller.Invites, controller.Warnings, leave) { }

    public HanLobbyView(RoomLinkBase link, IReadOnlyList<InviteOption> invites, IReadOnlyList<string> warnings, Func<Task> leave)
    {
        _link = link;
        _invites = invites;
        _warnings = warnings;
        _leave = leave;
        Background = Brushes.Black;
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _root });
        _link.HanStateChanged += Rebuild;
        Unloaded += (_, _) => _link.HanStateChanged -= Rebuild;
        Rebuild();
    }

    private HanRoomState? State => _link.HanState;
    private int MySeat => State?.YourSeat ?? -1;
    private bool IsHost => MySeat == 0;

    private void Put(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _root.Children.Add(e);
    }

    private void Rebuild()
    {
        _busy = false;
        _root.Children.Clear();
        if (HanArt.Src("backdrop") is { } bg) Put(new Image { Source = bg, Width = 1920, Height = 1080, Stretch = Stretch.Fill }, 0, 0);
        Put(new Rectangle { Width = 1920, Height = 1080, Fill = new SolidColorBrush(Color.FromArgb(0xa8, 6, 4, 2)) }, 0, 0);

        var head = new StackPanel { Width = 1920 };
        var k = HanUi.Label("Han Ligi · Online", 13);
        k.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(k);
        var t = HanUi.Display("Bekleme odası", 52);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(t);
        var rule = HanUi.Rule(420);
        rule.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(rule);
        var sub = HanUi.Body("Odadaki herkes hazır olunca maç başlar. Boş koltukları yapay zekâ doldurur.", 17, HanUi.Br(HanUi.Muted), false, TextAlignment.Center);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 8, 0, 0);
        head.Children.Add(sub);
        Put(head, 0, 50);

        if (State is not { } st) { Put(HanUi.Body("Odaya bağlanılıyor…", 20), 860, 500); return; }

        // ---- koltuklar ----
        const double cw = 330, ch = 150, gap = 26;
        var x0 = 960 - (4 * cw + 3 * gap) / 2;
        for (var i = 0; i < HanRoom_MaxSize; i++)
        {
            var x = x0 + (i % 4) * (cw + gap);
            var y = 250 + (i / 4) * (ch + gap);
            Put(SeatCard(st, i, cw, ch), x, y);
        }

        // ---- oyuncu sayısı (yalnız oda kuran değiştirir) ----
        var humans = st.Seats.Count;
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        sizeRow.Children.Add(new TextBlock { Text = HanUi.Tracked(HanUi.Upper("Oyuncu sayısı"), 2), FontFamily = HanUi.Font, FontSize = HanUi.S(13), FontWeight = FontWeights.Bold, Foreground = HanUi.Br(Color.FromRgb(0xb8, 0x9a, 0x5e)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) });
        for (var n = 2; n <= HanRoom_MaxSize; n++)
        {
            var size = n;
            var on = st.Size == n;
            var b = new HanButton(n.ToString(), () => SetSize(size), on ? HanButtonKind.Primary : HanButtonKind.Ghost, 52, 46, null, null, IsHost && n >= Math.Max(2, humans) && !on,
                IsHost ? $"Maç {n} kişilik olsun (eksikleri bot)." : "Oyuncu sayısını odayı kuran seçer.") { Tag = "hanlobby-size:" + n, Margin = new Thickness(0, 0, 8, 0) };
            if (on) b.Opacity = 1;
            sizeRow.Children.Add(b);
        }
        sizeRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Put(sizeRow, 960 - sizeRow.DesiredSize.Width / 2, 630);
        var bots = Math.Max(0, st.Size - humans);
        var info = HanUi.Body($"{humans} oyuncu + {bots} bot = {st.Size} kişilik maç", 16, HanUi.Br(HanUi.Muted), false, TextAlignment.Center);
        info.Width = 1920;
        Put(info, 0, 690);

        // ---- davet kodu (oda kuran) ----
        var inv = new StackPanel { Width = 900 };
        if (_invites.Count > 0)
        {
            var best = _invites[0];
            inv.Children.Add(HanUi.Label("Davet kodu · " + best.Label, 12));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var box = new TextBox { Text = best.Invite, IsReadOnly = true, Width = 720, FontFamily = HanUi.Font, FontSize = HanUi.S(17), Tag = "hanlobby-invite", Style = (Style)Application.Current.FindResource("GText") };
            line.Children.Add(box);
            line.Children.Add(new HanButton("Kopyala", () => { try { Clipboard.SetText(best.Invite); } catch { } }, HanButtonKind.Secondary, 150, 44) { Tag = "hanlobby-copy", Margin = new Thickness(12, 0, 0, 0) });
            inv.Children.Add(line);
        }
        else
        {
            inv.Children.Add(HanUi.Label("Oda kodu", 12));
            inv.Children.Add(HanUi.Title(st.Code, 30, HanUi.GoldText));
        }
        foreach (var w in _warnings) inv.Children.Add(HanUi.Small(w, 12));
        Put(inv, 510, 740);

        // ---- hazır / ayrıl ----
        var me = MySeat >= 0 && MySeat < st.Seats.Count ? st.Seats[MySeat] : null;
        var ready = me?.Ready == true;
        Put(new HanButton(ready ? "Hazır değilim" : "Hazırım", () => SetReady(!ready), ready ? HanButtonKind.Secondary : HanButtonKind.Primary, 320, 70, ready ? "öteki oyuncular bekleniyor" : "herkes hazır olunca başlar") { Tag = "hanlobby-ready" }, 1080, 900);
        Put(new HanButton("Ayrıl", () => _ = _leave(), HanButtonKind.Ghost, 180, 70) { Tag = "hanlobby-leave" }, 860, 900);
        if (_error.Length > 0) { var e = HanUi.Body(_error, 16, HanUi.Br(HanUi.Bad), false, TextAlignment.Center); e.Width = 1920; Put(e, 0, 990); }
    }

    private const int HanRoom_MaxSize = 8;

    /// <summary>Bir koltuk kartı: oyuncu (portre, ad, hazır damgası), boşsa "bot gelecek", sayı dışındaysa sönük.</summary>
    private FrameworkElement SeatCard(HanRoomState st, int i, double w, double h)
    {
        var inSize = i < st.Size;
        var seat = i < st.Seats.Count ? st.Seats[i] : null;
        var me = i == MySeat;
        var g = new Grid { Width = w, Height = h, Opacity = inSize ? 1 : 0.28 };
        g.Children.Add(HanUi.Frame(w, h, null, 0xd8, me ? HanUi.Gold : seat != null ? Color.FromRgb(0xa8, 0x90, 0x5a) : (Color?)null, 12));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        if (seat != null)
        {
            row.Children.Add(Portraits.Medallion(seat.Avatar, 92, me));
            col.Children.Add(HanUi.Title(seat.Nick, 24, me ? HanUi.GoldText : null));
            col.Children.Add(HanUi.Small(i == 0 ? "odayı kurdu" : seat.Connected ? "bağlı" : "bağlantı koptu", 13));
            var chip = HanUi.Chip(seat.Ready ? "Hazır" : "Bekliyor", seat.Ready ? HanUi.Good : HanUi.Muted, 12, seat.Ready);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(chip);
        }
        else
        {
            var e = new Grid { Width = 92, Height = 92 };
            e.Children.Add(new Ellipse { Stroke = HanUi.Br(HanUi.Gold, 0x60), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 } });
            var q = HanUi.T("?", 40, HanUi.Br(HanUi.Muted), FontWeights.Bold);
            q.HorizontalAlignment = HorizontalAlignment.Center;
            q.VerticalAlignment = VerticalAlignment.Center;
            e.Children.Add(q);
            row.Children.Add(e);
            col.Children.Add(HanUi.Title(inSize ? "Boş koltuk" : "Kapalı", 21, HanUi.Br(HanUi.Muted)));
            col.Children.Add(HanUi.Small(inSize ? "Arkadaşın katılabilir ya da maç başlayınca bot oturur" : "Oyuncu sayısının dışında", 12));
            ((TextBlock)col.Children[1]).TextWrapping = TextWrapping.Wrap;
            ((TextBlock)col.Children[1]).Width = 180;
        }
        row.Children.Add(col);
        g.Children.Add(row);
        return g;
    }

    private async void SetSize(int n)
    {
        if (_busy) return;
        _busy = true;
        var r = await _link.HanSetSizeAsync(n);
        Dispatcher.Invoke(() => { _busy = false; _error = r.Ok ? "" : r.Error ?? ""; if (!r.Ok) Rebuild(); });
    }

    private async void SetReady(bool on)
    {
        if (_busy) return;
        _busy = true;
        var r = await _link.HanSetReadyAsync(on);
        Dispatcher.Invoke(() => { _busy = false; _error = r.Ok ? "" : r.Error ?? ""; if (!r.Ok) Rebuild(); });
    }
}
