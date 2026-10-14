using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GwentAI.Career;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Bölge tüccarı: o bölgenin fraksiyonlarından kartlar ve liderler, oyunun kendi fiyatlarıyla. Kahramanlar ve rakiplerin
/// benzersiz kartları satılmaz; onlar masada kazanılır. Kartın üzerine gelince büyük önizleme, tıklayınca satın alma.
/// </summary>
public sealed class CareerShopView : Grid
{
    private readonly CareerSave _s;
    private readonly CareerRegion _r;
    private readonly Action _back;
    private readonly Grid _content = new() { Width = 1920, Height = 1080 };
    private readonly ContentControl _body = new();
    private readonly ContentControl _preview = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 170, 60, 0) };
    private string _filter = "all";
    private string _message = "";
    private double _offset;
    private ScrollViewer? _scroll;

    public CareerShopView(CareerSave save, CareerRegion region, Action back)
    {
        _s = save;
        _r = region;
        _back = back;
        _content.Children.Add(_body);
        _content.Children.Add(_preview);
        Children.Add(Ornate.Stage(new Viewbox { Stretch = Stretch.Uniform, Child = _content }, region.Pano, 0.8));
        Focusable = true;
        Loaded += (_, _) => Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) _back(); };
        Draw();
    }

    private void Draw()
    {
        if (_scroll != null) _offset = _scroll.VerticalOffset;
        var root = new Grid { Margin = new Thickness(60, 40, 440, 40) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ornate.Heading("TÜCCAR · " + _r.Name.ToUpperInvariant(), "Kahramanlar satılmaz: onları masada kazanırsın. Kartın üstüne tıkla, satın al.", 42));
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var crowns = Ui.Text($"◆ {_s.Crowns} Crown", 30, Theme.B("#f0d890"), true);
        crowns.VerticalAlignment = VerticalAlignment.Center;
        crowns.Tag = "shop-crowns";
        right.Children.Add(crowns);
        var back = Ui.Button("‹ Haritaya dön", _back);
        back.FontSize = 20;
        back.Padding = new Thickness(22, 8, 22, 8);
        back.Margin = new Thickness(26, 0, 0, 0);
        back.Tag = "shop-back";
        right.Children.Add(back);
        SetColumn(right, 1);
        top.Children.Add(right);
        root.Children.Add(top);

        // Fraksiyon süzgeci
        var stock = CareerLogic.Stock(_s, _r).ToList();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 12) };
        var keys = new[] { "all" }.Concat(stock.Select(d => d.IsLeader ? "leaders" : d.Faction).Distinct().OrderBy(k => k == "leaders" ? 0 : k == "neutral" ? 2 : 1)).ToList();
        foreach (var k in keys)
        {
            var key = k;
            var active = _filter == k;
            var label = k switch { "all" => "Tümü", "leaders" => "Liderler", _ => Texts.FactionShort(k) };
            var t = Ui.Text(label, 20, active ? Theme.B("#f0d890") : Theme.B("#a89d85"), true);
            var tab = new Border
            {
                Child = t, Padding = new Thickness(4, 2, 4, 5), Margin = new Thickness(0, 0, 26, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                BorderBrush = active ? Theme.B("#c9a24a") : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2), Tag = "shop-tab"
            };
            tab.MouseLeftButtonUp += (_, e) => { e.Handled = true; _filter = key; _offset = 0; _scroll = null; Sfx.Play("cursor_movement"); Draw(); };
            tabs.Children.Add(tab);
        }
        if (_message != "")
        {
            var m = Ui.Text(_message, 17, Theme.Gold, true);
            m.VerticalAlignment = VerticalAlignment.Center;
            m.Margin = new Thickness(20, 0, 0, 0);
            tabs.Children.Add(m);
        }
        SetRow(tabs, 1);
        root.Children.Add(tabs);

        var wrap = new WrapPanel();
        var shown = stock.Where(d => _filter == "all" || (_filter == "leaders" ? d.IsLeader : !d.IsLeader && d.Faction == _filter));
        foreach (var d in shown)
        {
            var def = d;
            var price = NpcDecks.Price(d);
            var afford = _s.Crowns >= price;
            var tile = new StackPanel { Margin = new Thickness(8, 6, 8, 14), Cursor = afford ? Cursors.Hand : Cursors.Arrow, Background = Brushes.Transparent, Tag = "shop-card:" + d.Id };
            var card = new GwintCard(d, 128, d.IsUnit ? d.Strength : null, height: 128 * 1.42) { Opacity = afford ? 1 : 0.55, IsHitTestVisible = false };
            tile.Children.Add(card);
            var plate = new Border
            {
                Background = Theme.B("#e0120c07"), BorderBrush = afford ? Theme.Gold : Theme.B("#5a4a30"), BorderThickness = new Thickness(1.2), Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(6, 2, 6, 3), HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false,
                Child = Ui.Text($"◆ {price}   ·   {_s.Owned(d.Id)}/{d.Copies}", 15, afford ? Theme.B("#f0d890") : Theme.TextDim, true)
            };
            tile.Children.Add(plate);
            if (afford) Fx.HoverLift(tile, -6);
            tile.MouseEnter += (_, _) => ShowPreview(def, price);
            tile.MouseLeave += (_, _) => _preview.Content = null;
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Buy(def); };
            wrap.Children.Add(tile);
        }
        if (!shown.Any()) wrap.Children.Add(Ui.Text("Bu tüccarda alınacak kart kalmadı.", 20, Theme.TextDim));
        var scroll = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var off = _offset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(off);
        _scroll = scroll;
        var box = Ornate.Panel(scroll, new Thickness(22, 20, 12, 16), 18, 46);
        SetRow(box, 2);
        root.Children.Add(box);
        _body.Content = root;
    }

    private void ShowPreview(CardDef d, int price)
    {
        var sp = new StackPanel();
        var card = new GwintCard(d, 300, d.IsUnit ? d.Strength : null, height: 300 * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.9 };
        sp.Children.Add(card);
        var n = Ui.Text(d.Name, 24, d.Hero ? Theme.B("#f0d890") : Theme.Text, true, TextAlignment.Center, true);
        n.Margin = new Thickness(0, 12, 0, 2);
        sp.Children.Add(n);
        var kind = d.IsLeader ? "Lider · " + Texts.FactionShort(d.Faction) : d.IsUnit ? Texts.RowOf(d) + " · " + Texts.FactionShort(d.Faction) : d.IsWeather ? "Hava kartı" : "Özel kart";
        sp.Children.Add(Ui.Text(kind, 16, Theme.TextDim, false, TextAlignment.Center));
        var text = d.IsLeader ? Texts.LeaderText(d) : string.Join("\n", d.Abilities.Select(a => Texts.AbilityLabel(a) + ": " + Texts.AbilityText(a)));
        if (text != "")
        {
            var t = Ui.Text(text, 15, Theme.B("#d8cdb0"), false, TextAlignment.Left, true);
            t.Margin = new Thickness(4, 10, 4, 0);
            sp.Children.Add(t);
        }
        var p = Ui.Text($"Fiyat: {price} Crown", 20, Theme.B("#f0d890"), true, TextAlignment.Center);
        p.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(p);
        _preview.Content = Ornate.Panel(sp, new Thickness(22, 22, 22, 22), 16, 40);
    }

    private void Buy(CardDef d)
    {
        var err = CareerLogic.Buy(_s, d);
        if (err != null) { _message = err; Sfx.Play("cursor_movement"); Draw(); return; }
        CareerStore.Save(_s);
        Sfx.Play("lock_in");
        _message = $"{d.Name} satın alındı. Desteye eklemek için: Destem.";
        _preview.Content = null;
        Draw();
    }
}
