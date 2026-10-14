using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Deste kurucu: soldaki koleksiyondan kart ekle (tıkla, sağ tık: çıkar), sağdaki desteden çıkar. Doğrulama kuralları
/// motordaki <see cref="DeckValidator"/> ile aynıdır; kuralı burada ikinci kez yazmayız.
/// Görünüm menü ve oyun alanıyla aynı dilde: fraksiyon panoraması, parşömen paneller, oyunun yazı tipi ve rozetleri.
/// </summary>
public sealed class DeckBuilderView : Grid
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private readonly Action _close;
    private readonly GameOptions _options;
    private string _faction;
    private string _leaderId = "";
    private string _name = "";
    private readonly Dictionary<string, int> _cards = new();
    private string? _loadedName;
    private string _typeFilter = "all";
    private string _message = "";

    private readonly StackPanel _header = new();
    private readonly Border _poolHost = new();
    private readonly Border _deckHost = new();
    private readonly Border _preview = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 38, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.Image _bg = new() { Stretch = Stretch.UniformToFill, Opacity = 1, IsHitTestVisible = false };
    private readonly TextBox _nameBox = new();
    private Button? _saveButton;
    private ScrollViewer? _poolScroll, _listScroll;
    private double _poolOffset, _listOffset;

    /// <summary>Kariyer kipi: yalnız koleksiyondaki kartlar ve liderler; bir karttan en fazla sahip olunan kadar. Kaydedilen deste kariyerin destesi olur.</summary>
    private readonly GwentAI.Career.CareerSave? _career;

    /// <summary>Kartın bu destede kullanılabilecek en fazla adedi: kopya sınırı; kariyerde ayrıca koleksiyondaki adet.</summary>
    private int Limit(CardDef d) => _career == null ? d.Copies : Math.Min(d.Copies, _career.Owned(d.Id));

    private IEnumerable<CardDef> LeaderChoices() =>
        CardCatalog.Default.Leaders(_faction).Where(l => (l.Edition != "nextgen" || _options.NextGen) && (_career == null || _career.Owned(l.Id) > 0));

    public DeckBuilderView(string faction, bool nextGen, Action close, GwentAI.Career.CareerSave? career = null)
    {
        _career = career;
        if (career != null && !OwnsLeader(career, faction)) faction = career.ActiveFaction;
        _faction = faction;
        _options = new GameOptions { NextGen = nextGen };
        _close = close;
        Background = Theme.Bg;
        ClipToBounds = true;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });

        // Arka plan: fraksiyonun panoraması, koyulaştırılmış + vinyet
        SetRowSpan(_bg, 2);
        SetColumnSpan(_bg, 2);
        Children.Add(_bg);
        var vignette = new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.9,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xb0, 0, 0, 0), 1) }
            }
        };
        SetRowSpan(vignette, 2);
        SetColumnSpan(vignette, 2);
        Children.Add(vignette);

        SetColumnSpan(_header, 2);
        Children.Add(_header);
        SetRow(_poolHost, 1);
        Children.Add(_poolHost);
        SetRow(_deckHost, 1);
        SetColumn(_deckHost, 1);
        Children.Add(_deckHost);
        SetRow(_preview, 1);
        Children.Add(_preview);

        _nameBox.Style = (Style)Application.Current.FindResource("GText");
        _nameBox.FontFamily = Theme.Serif;
        _nameBox.FontSize = 18;
        // Ad yazılırken yeniden çizim yapılmaz (imleç ve odak kaybolurdu); yalnız Kaydet düğmesi güncellenir.
        _nameBox.TextChanged += (_, _) =>
        {
            _name = _nameBox.Text;
            if (_saveButton != null) _saveButton.IsEnabled = !string.IsNullOrWhiteSpace(_name);
        };

        NewDeck();
    }

    // ---------- Durum ----------

    private Deck CurrentDeck()
    {
        var ids = _cards.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        return new Deck(_faction, _leaderId, ids);
    }

    private static bool OwnsLeader(GwentAI.Career.CareerSave c, string faction) => CardCatalog.Default.Leaders(faction).Any(l => c.Owned(l.Id) > 0);

    private void NewDeck()
    {
        _cards.Clear();
        _loadedName = null;
        _name = "";
        _nameBox.Text = "";
        _leaderId = LeaderChoices().FirstOrDefault()?.Id ?? CardCatalog.Default.Leaders(_faction).First().Id;
        _message = "";
        if (_career != null && _career.Decks.TryGetValue(_faction, out var saved))
        {
            foreach (var id in saved.Cards) _cards[id] = _cards.GetValueOrDefault(id) + 1;
            if (_career.Owned(saved.LeaderId) > 0) _leaderId = saved.LeaderId;
        }
        Rebuild();
    }

    private void LoadDeck(SavedDeck d)
    {
        _cards.Clear();
        foreach (var id in d.CardIds) _cards[id] = _cards.GetValueOrDefault(id) + 1;
        _leaderId = d.LeaderId;
        _loadedName = d.Name;
        _name = d.Name;
        _nameBox.Text = d.Name;
        _message = "";
        Rebuild();
    }

    private void SwitchFaction(string faction)
    {
        if (faction == _faction) return;
        _faction = faction;
        _poolOffset = _listOffset = 0;
        NewDeck();
    }

    private int Count(string id) => _cards.GetValueOrDefault(id);

    private void Add(CardDef d)
    {
        if (Count(d.Id) >= Limit(d)) return;
        _cards[d.Id] = Count(d.Id) + 1;
        _message = "";
        RebuildLight();
        Sfx.Play("draw_card");
    }

    private void Remove(CardDef d)
    {
        if (Count(d.Id) <= 0) return;
        if (--_cards[d.Id] == 0) _cards.Remove(d.Id);
        _message = "";
        RebuildLight();
        Sfx.Play("discard_card");
    }

    // ---------- Çizim ----------

    private void Rebuild()
    {
        var pano = Assets.Ui(Theme.PanoFor(_faction));
        if (pano != null) _bg.Source = pano;
        BuildHeader();
        RebuildLight();
    }

    /// <summary>Kart ekle/çıkar: üst bölüm değişmez, kaydırma konumları korunur.</summary>
    private void RebuildLight()
    {
        _preview.Visibility = Visibility.Collapsed;
        if (_poolScroll != null) _poolOffset = _poolScroll.VerticalOffset;
        if (_listScroll != null) _listOffset = _listScroll.VerticalOffset;
        BuildPool();
        BuildDeck();
    }

    private static FrameworkElement PanelBox(UIElement child, Thickness pad)
    {
        var p = Ornate.Panel(child, pad, 18, 46);
        return p;
    }

    private static Rectangle GoldLine(double width, double margin = 0) => new()
    {
        Height = 1.5, Width = width, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, margin, 0, margin), IsHitTestVisible = false,
        Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0xc9, 0xa2, 0x4a), 0), new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 1)
        }, 0)
    };

    /// <summary>Menüdeki gibi sade yazı bağlantısı: üzerine gelince parlar.</summary>
    private static UIElement NavLink(string text, Action click)
    {
        var tb = new TextBlock { Text = text, FontFamily = Theme.Serif, FontSize = 26, Foreground = Theme.B("#bdb39a"), IsHitTestVisible = false };
        var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(6, 4, 14, 4), Cursor = Cursors.Hand, Child = tb };
        host.MouseEnter += (_, _) => { tb.Foreground = Theme.B("#fff2c8"); tb.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 }; };
        host.MouseLeave += (_, _) => { tb.Foreground = Theme.B("#bdb39a"); tb.Effect = null; };
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("lock_in"); click(); };
        return host;
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.Margin = new Thickness(34, 18, 34, 10);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(NavLink(_career != null ? "‹ Harita" : "‹ Menü", _close));
        var title = Ornate.Heading(_career != null ? "Collect 'Em All · Deste" : "Deste kurucu", null, 38);
        ((FrameworkElement)title).Margin = new Thickness(26, 0, 0, 0);
        ((FrameworkElement)title).VerticalAlignment = VerticalAlignment.Center;
        SetColumn(title, 1);
        top.Children.Add(title);
        _header.Children.Add(top);
        _header.Children.Add(GoldLine(520, 6));

        // Fraksiyon seçimi (kalkanlar) + kayıtlı desteler
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fr = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var f in Factions)
        {
            if (_career != null && !OwnsLeader(_career, f)) continue;   // kariyerde liderin yoksa o fraksiyonla deste kurulamaz
            var key = f;
            var sel = f == _faction;
            var inner = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
            var shield = Assets.Ui("shield_" + f);
            if (shield != null) inner.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 42, Height = 42, Margin = new Thickness(0, 0, 8, 0) });
            var name = Ui.Text(Texts.FactionShort(f), 17, sel ? Theme.B("#f0d890") : Theme.B("#c9c0aa"), true);
            name.VerticalAlignment = VerticalAlignment.Center;
            inner.Children.Add(name);
            var frame = new ChamferFrame
            {
                Fill = sel ? Theme.B("#3a2c14") : Theme.B("#1a130b"), Edge = sel ? Ornate.GoldEdge : Theme.B("#4a3b28"), Inner = sel ? Theme.B("#88e0bf6a") : null,
                Cut = 9, Inset = 4
            };
            var b = new Grid { Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            b.Children.Add(frame);
            inner.Margin = new Thickness(12, 7, 16, 7);
            b.Children.Add(inner);
            if (sel) b.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5 };
            b.MouseEnter += (_, _) => { if (key != _faction) frame.Edge = Theme.B("#a88a40"); };
            b.MouseLeave += (_, _) => { if (key != _faction) frame.Edge = Theme.B("#4a3b28"); };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("cursor_movement"); SwitchFaction(key); };
            fr.Children.Add(b);
        }
        row.Children.Add(fr);

        if (_career != null)
        {
            var (have, total) = GwentAI.Career.CareerLogic.Progress(_career);
            var info = Ui.Text($"Koleksiyon {have}/{total} kart  ·  Yeni kartları rakipleri yenerek ya da tüccardan alırsın.", 15, Theme.TextDim, false, TextAlignment.Right);
            info.HorizontalAlignment = HorizontalAlignment.Right;
            info.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(info, 1);
            row.Children.Add(info);
            _header.Children.Add(row);
            return;
        }
        var saved = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var lbl = Ui.Text("Desteler", 15, Theme.TextDim, true);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.Margin = new Thickness(0, 0, 10, 0);
        saved.Children.Add(lbl);
        foreach (var d in DeckStore.For(_faction))
        {
            var deck = d;
            var valid = DeckValidator.Validate(deck.ToDeck(), _options).Count == 0;
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"),
                Content = Ui.Text((valid ? "" : "⚠ ") + d.Name, 14, valid ? Theme.Text : Theme.Bad, true),
                IsChecked = d.Name == _loadedName,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 5, 12, 5),
                Tag = "saved-deck"
            };
            tb.Click += (_, _) => LoadDeck(deck);
            saved.Children.Add(tb);
        }
        var fresh = Ui.Button("+ Yeni deste", NewDeck);
        fresh.Padding = new Thickness(12, 5, 12, 5);
        fresh.FontSize = 14;
        saved.Children.Add(fresh);
        SetColumn(saved, 1);
        row.Children.Add(saved);
        _header.Children.Add(row);
    }

    private IEnumerable<CardDef> FilteredPool()
    {
        var pool = DeckValidator.Pool(_faction, _options);
        IEnumerable<CardDef> q = _career == null ? pool : pool.Where(d => Limit(d) > 0);
        q = _typeFilter switch
        {
            "unit" => q.Where(d => d.IsUnit && !d.Hero),
            "hero" => q.Where(d => d.IsUnit && d.Hero),
            "special" => q.Where(d => !d.IsUnit),
            _ => q
        };
        int Order(CardDef d) => d.IsUnit ? (d.FixedRow is { } r ? (int)r : 3) : 4;
        return q.OrderBy(Order).ThenByDescending(d => d.IsUnit ? d.Strength : 0).ThenBy(d => d.Name);
    }

    private static Grid Shadow(double w, double h)
    {
        var g = new Grid { Width = w, Height = h, IsHitTestVisible = false };
        foreach (var (grow, alpha) in new[] { (7.0, 0x18), (4.0, 0x26), (1.5, 0x3a) })
            g.Children.Add(new Rectangle
            {
                Width = w + grow * 2, Height = h + grow * 2, RadiusX = 4 + grow, RadiusY = 4 + grow, IsHitTestVisible = false,
                Fill = new SolidColorBrush(Color.FromArgb((byte)alpha, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        return g;
    }

    private void BuildPool()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Filtre sekmeleri: altı çizgili yazı
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 10) };
        foreach (var (key, label) in new[] { ("all", "Tümü"), ("unit", "Birimler"), ("hero", "Kahramanlar"), ("special", "Özel ve hava") })
        {
            var k = key;
            var active = _typeFilter == key;
            var t = Ui.Text(label, 20, active ? Theme.B("#f0d890") : Theme.B("#a89d85"), true);
            var tab = new Border
            {
                Child = t, Padding = new Thickness(4, 2, 4, 5), Margin = new Thickness(0, 0, 26, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                BorderBrush = active ? Theme.B("#c9a24a") : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2)
            };
            tab.MouseEnter += (_, _) => { if (_typeFilter != k) t.Foreground = Theme.B("#e8dcc4"); };
            tab.MouseLeave += (_, _) => { if (_typeFilter != k) t.Foreground = Theme.B("#a89d85"); };
            tab.MouseLeftButtonUp += (_, e) => { e.Handled = true; _typeFilter = k; _poolOffset = 0; Sfx.Play("cursor_movement"); BuildPool(); };
            filters.Children.Add(tab);
        }
        root.Children.Add(filters);

        const double cw = 118, ch = 170;
        var wrap = new WrapPanel();
        foreach (var d in FilteredPool())
        {
            var def = d;
            var n = Count(def.Id);
            var limit = Limit(def);
            var maxed = n >= limit;
            var cc = new GwintCard(def, cw, def.IsUnit ? def.Strength : null, null, selected: n > 0, height: ch);
            cc.Opacity = maxed ? 0.5 : 1;
            cc.Tag = "pool";
            if (!maxed) Fx.HoverLift(cc, -6);

            var tile = new Grid { Tag = "pool-tile", Margin = new Thickness(7, 6, 7, 10) };
            var sh = Shadow(cw, ch);
            sh.Margin = new Thickness(3, 6, -3, -6);
            tile.Children.Add(sh);
            tile.Children.Add(cc);
            if (limit > 1 || n > 0)
            {
                var badge = new Border
                {
                    Background = Theme.B("#f0120c08"),
                    BorderBrush = n > 0 ? Theme.Gold : Theme.PanelEdge,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(8, 1, 8, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, -9),
                    IsHitTestVisible = false,
                    Child = Ui.Text($"{n}/{limit}", 13, n > 0 ? Theme.Gold : Theme.TextDim, true)
                };
                tile.Children.Add(badge);
            }
            cc.ToolTip = null;
            cc.MouseEnter += (_, _) => ShowPreview(def);
            cc.MouseLeave += (_, _) => _preview.Visibility = Visibility.Collapsed;
            cc.MouseLeftButtonUp += (_, _) => Add(def);
            cc.MouseRightButtonUp += (_, _) => Remove(def);
            wrap.Children.Add(tile);
        }
        var scroll = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        var offset = _poolOffset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(offset);
        _poolScroll = scroll;
        SetRow(scroll, 1);
        root.Children.Add(scroll);
        var box = PanelBox(root, new Thickness(24, 20, 14, 14));
        box.Margin = new Thickness(34, 4, 14, 26);
        _poolHost.Child = box;
    }

    /// <summary>Üzerine gelinen kartın büyük önizlemesi (sağ üst köşede, koleksiyonun üstünde).</summary>
    private void ShowPreview(CardDef d, bool overDeck = true)
    {
        const double w = 250;
        var sp = new StackPanel { Width = w + 60, HorizontalAlignment = HorizontalAlignment.Center };
        var card = new GwintCard(d, w, d.IsUnit ? d.Strength : null, null, height: w * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(card);
        var name = Ui.Text(d.Name, 22, d.Hero ? Theme.B("#f0d890") : Theme.Text, true, TextAlignment.Center, true);
        name.Margin = new Thickness(0, 12, 0, 2);
        sp.Children.Add(name);
        var kind = d.IsUnit ? Texts.RowOf(d) + (d.Hero ? " · Kahraman" : "") : (d.IsWeather ? "Hava kartı" : "Özel kart");
        sp.Children.Add(Ui.Text(kind, 15, Theme.TextDim, false, TextAlignment.Center, true));
        foreach (var a in d.Abilities)
        {
            var t = Ui.Text(Texts.AbilityLabel(a) + ": " + Texts.AbilityText(a), 14, Theme.B("#d8cdb0"), false, TextAlignment.Left, true);
            t.Margin = new Thickness(6, 8, 6, 0);
            sp.Children.Add(t);
        }
        _preview.Child = new Border
        {
            Child = Ornate.Panel(new Grid { Children = { sp }, VerticalAlignment = VerticalAlignment.Center }, new Thickness(18, 20, 18, 22), 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        // Koleksiyondan gelinen kartın önizlemesi deste panelinin üstünde (koleksiyonu kapatmaz); deste satırından gelinense koleksiyonun sağ üstünde.
        SetColumn(_preview, overDeck ? 1 : 0);
        _preview.HorizontalAlignment = overDeck ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _preview.VerticalAlignment = overDeck ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        _preview.Margin = overDeck ? new Thickness(0, 4, 34, 26) : new Thickness(0, 24, 38, 0);
        if (_preview.Child is Border pb) { pb.VerticalAlignment = VerticalAlignment.Center; if (overDeck) pb.VerticalAlignment = VerticalAlignment.Stretch; }
        _preview.Visibility = Visibility.Visible;
    }

    private void BuildDeck()
    {
        var deck = CurrentDeck();
        var errors = DeckValidator.Validate(deck, _options);
        if (_career != null && _career.Owned(_leaderId) == 0) errors.Insert(0, "Bu fraksiyonun hiçbir liderine sahip değilsin.");
        var defs = _cards.Select(kv => (Def: CardCatalog.Default.Get(kv.Key), N: kv.Value)).ToList();
        var units = defs.Where(x => x.Def.IsUnit).Sum(x => x.N);
        var specials = defs.Where(x => !x.Def.IsUnit).Sum(x => x.N);
        var heroes = defs.Where(x => x.Def.IsUnit && x.Def.Hero).Sum(x => x.N);
        var power = defs.Where(x => x.Def.IsUnit).Sum(x => x.Def.Strength * x.N);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Üst: ad, lider ve istatistik
        var top = new StackPanel();
        _nameBox.Margin = new Thickness(0, 0, 0, 12);
        if (_nameBox.Parent is Panel op) op.Children.Remove(_nameBox);
        if (_career == null)
        {
            top.Children.Add(Ui.Text("Deste adı", 14, Theme.TextDim, true));
            ((TextBlock)top.Children[0]).Margin = new Thickness(0, 0, 0, 3);
            top.Children.Add(_nameBox);
        }

        var current = CardCatalog.Default.Get(_leaderId);
        var lrow = new Grid();
        lrow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var big = new GwintCard(current, 112, null, null, height: 112 * GwintCard.Aspect) { IsHitTestVisible = false };
        big.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.9 };
        lrow.Children.Add(big);
        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var lname = Ui.Text(current.Name, 17, Theme.B("#f0d890"), true, TextAlignment.Left, true);
        right.Children.Add(lname);
        right.Children.Add(Ui.Text(Texts.LeaderText(current), 13, Theme.B("#c9c0aa"), false, TextAlignment.Left, true));
        SetColumn(right, 1);
        lrow.Children.Add(right);
        top.Children.Add(lrow);

        top.Children.Add(Ui.Text("Lider seç", 14, Theme.TextDim, true));
        ((TextBlock)top.Children[^1]).Margin = new Thickness(0, 10, 0, 4);
        var leaders = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var l in LeaderChoices())
        {
            var leader = l;
            var cc = new GwintCard(leader, 62, null, null, selected: leader.Id == _leaderId, height: 62 * GwintCard.Aspect);
            cc.Margin = new Thickness(0, 0, 8, 4);
            cc.Tag = "leader";
            if (leader.Id != _leaderId) cc.Opacity = 0.7;
            cc.ToolTip = leader.Name;
            cc.MouseLeftButtonUp += (_, _) => { _leaderId = leader.Id; Sfx.Play("leader_change"); RebuildLight(); };
            leaders.Children.Add(cc);
        }
        top.Children.Add(leaders);

        var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 6, 0, 8) };
        stats.Children.Add(Stat("Birim", $"{units}", units >= DeckValidator.MinUnits ? Theme.Good : Theme.Bad, $"≥ {DeckValidator.MinUnits}"));
        stats.Children.Add(Stat("Özel", $"{specials}", specials <= DeckValidator.MaxSpecials ? Theme.Good : Theme.Bad, $"≤ {DeckValidator.MaxSpecials}"));
        stats.Children.Add(Stat("Kahraman", $"{heroes}", Theme.B("#f0d890"), ""));
        stats.Children.Add(Stat("Güç", $"{power}", Theme.Text, ""));
        top.Children.Add(stats);
        SetRow(top, 0);
        root.Children.Add(top);

        // Orta: deste listesi
        var list = new StackPanel();
        foreach (var x in defs.OrderBy(x => x.Def.IsUnit ? 0 : 1).ThenBy(x => x.Def.FixedRow is { } r ? (int)r : 3).ThenByDescending(x => x.Def.Strength).ThenBy(x => x.Def.Name))
            list.Children.Add(DeckRow(x.Def, x.N));
        if (defs.Count == 0)
        {
            var empty = Ui.Text("Henüz kart yok. Soldan bir karta tıkla (sağ tık: çıkar) ya da “Önerilen deste”yi dene.", 15, Theme.TextDim, false, TextAlignment.Left, true);
            empty.Margin = new Thickness(8);
            list.Children.Add(empty);
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var off = _listOffset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(off);
        _listScroll = scroll;
        var listBox = new Border
        {
            Child = scroll, Background = Theme.B("#aa0a0705"), BorderBrush = Theme.B("#5a4a24"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4)
        };
        SetRow(listBox, 1);
        root.Children.Add(listBox);

        // Alt: doğrulama + düğmeler
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (errors.Count == 0) bottom.Children.Add(Ui.Text("✓ Deste geçerli.", 15, Theme.Good, true));
        else foreach (var e in errors) bottom.Children.Add(Ui.Text("• " + e, 14, Theme.Bad, false, TextAlignment.Left, true));
        if (!string.IsNullOrEmpty(_message)) bottom.Children.Add(Ui.Text(_message, 14, Theme.Gold, true, TextAlignment.Left, true));

        var btns = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (_career != null)
        {
            var use = Ui.Button("Kaydet ve kullan", SaveCareer, primary: true);
            use.Margin = new Thickness(0, 0, 6, 0);
            use.Tag = "career-deck-save";
            btns.Children.Add(use);
            var auto = Ui.Button("Otomatik doldur", () =>
            {
                var d = GwentAI.Career.CareerLogic.AutoDeck(_career, _faction, _leaderId);
                _cards.Clear();
                foreach (var id in d.Cards) _cards[id] = _cards.GetValueOrDefault(id) + 1;
                _message = "Koleksiyonundaki en güçlü kartlarla dolduruldu.";
                RebuildLight();
            });
            auto.Margin = new Thickness(0, 0, 6, 0);
            btns.Children.Add(auto);
            var empty = Ui.Button("Temizle", () => { _cards.Clear(); _message = ""; RebuildLight(); });
            btns.Children.Add(empty);
            bottom.Children.Add(btns);
            SetRow(bottom, 2);
            root.Children.Add(bottom);
            var cbox = PanelBox(root, new Thickness(28, 26, 26, 24));
            cbox.Margin = new Thickness(0, 4, 34, 26);
            _deckHost.Child = cbox;
            return;
        }
        var save = Ui.Button("Kaydet", Save, primary: true, enabled: !string.IsNullOrWhiteSpace(_name));
        save.Margin = new Thickness(0, 0, 6, 0);
        _saveButton = save;
        btns.Children.Add(save);
        var rec = Ui.Button("Önerilen deste", Recommended);
        rec.Margin = new Thickness(0, 0, 6, 0);
        btns.Children.Add(rec);
        var clear = Ui.Button("Temizle", () => { _cards.Clear(); _message = ""; RebuildLight(); });
        clear.Margin = new Thickness(0, 0, 6, 0);
        btns.Children.Add(clear);
        if (_loadedName != null)
        {
            var del = Ui.Button("Sil", DeleteLoaded);
            del.Margin = new Thickness(0, 0, 6, 0);
            btns.Children.Add(del);
        }
        bottom.Children.Add(btns);
        SetRow(bottom, 2);
        root.Children.Add(bottom);

        var box = PanelBox(root, new Thickness(28, 26, 26, 24));
        box.Margin = new Thickness(0, 4, 34, 26);
        _deckHost.Child = box;
    }

    private static UIElement Stat(string label, string value, Brush color, string hint)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        sp.Children.Add(Ui.Text(value, 26, color, true, TextAlignment.Center));
        sp.Children.Add(Ui.Text(label, 13, Theme.TextDim, false, TextAlignment.Center));
        if (hint != "") sp.Children.Add(Ui.Text(hint, 11, Theme.TextDim, false, TextAlignment.Center));
        return new Border
        {
            Child = sp, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(2, 4, 2, 4), CornerRadius = new CornerRadius(4),
            Background = Theme.B("#55000000"), BorderBrush = Theme.B("#4a3c1c"), BorderThickness = new Thickness(1)
        };
    }

    private UIElement DeckRow(CardDef d, int n)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Tag = "deck-row" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.MouseEnter += (_, _) => { g.Background = Theme.B("#33c9a24a"); ShowPreview(d, overDeck: false); };
        g.MouseLeave += (_, _) => { g.Background = Brushes.Transparent; _preview.Visibility = Visibility.Collapsed; };

        // Güç rozeti: oyunun kendi rozeti (kahramanda altın)
        var badge = new Grid { Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var bimg = Assets.Ui(d.Hero ? "badge_hero" : "badge");
        if (bimg != null) badge.Children.Add(new System.Windows.Controls.Image { Source = bimg, Stretch = Stretch.Uniform });
        else badge.Children.Add(new Ellipse { Fill = d.Hero ? Theme.Gold : Theme.B("#e8dcc4") });
        var num = Ui.Text(d.IsUnit ? d.Strength.ToString() : "★", 17, d.Hero ? Brushes.White : Theme.B("#2a1c0e"), true, TextAlignment.Center);
        num.VerticalAlignment = VerticalAlignment.Center;
        num.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(num);
        g.Children.Add(badge);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 6, 2) };
        name.Children.Add(Ui.Text(d.Name, 16, d.Hero ? Theme.B("#f0d890") : Theme.Text, d.Hero));
        var extra = d.IsUnit
            ? string.Join(" · ", new[] { Texts.RowOf(d) }.Concat(d.Abilities.Select(Texts.AbilityLabel)))
            : string.Join(" · ", d.Abilities.Select(Texts.AbilityLabel));
        name.Children.Add(Ui.Text(extra, 12, Theme.TextDim));
        SetColumn(name, 1);
        g.Children.Add(name);

        var count = Ui.Text($"×{n}", 18, Theme.Gold, true);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(4, 0, 8, 0);
        SetColumn(count, 2);
        g.Children.Add(count);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var minus = Ui.Button("−", () => Remove(d));
        minus.Padding = new Thickness(9, 1, 9, 1);
        minus.Tag = "dec-minus";
        var plus = Ui.Button("+", () => Add(d), enabled: n < Limit(d));
        plus.Padding = new Thickness(8, 1, 8, 1);
        plus.Margin = new Thickness(3, 0, 0, 0);
        btns.Children.Add(minus);
        btns.Children.Add(plus);
        SetColumn(btns, 3);
        g.Children.Add(btns);
        return g;
    }

    // ---------- Eylemler ----------

    private void Recommended()
    {
        var rec = DeckValidator.RecommendedDeck(_faction, _options, null, _leaderId);
        _cards.Clear();
        foreach (var id in rec.CardIds) _cards[id] = _cards.GetValueOrDefault(id) + 1;
        _message = "Önerilen deste dolduruldu. İstediğin gibi değiştir.";
        RebuildLight();
    }

    /// <summary>Kariyer: desteyi bu fraksiyonun destesi yapar ve etkin deste olarak seçer (geçersizse de kaydedilir; oynamadan önce uyarılır).</summary>
    private void SaveCareer()
    {
        var deck = CurrentDeck();
        _career!.Decks[_faction] = new GwentAI.Career.CareerDeck(deck.LeaderId, deck.CardIds.ToList());
        _career.ActiveFaction = _faction;
        CareerStore.Save(_career);
        var errors = DeckValidator.Validate(deck, _options);
        _message = errors.Count == 0 ? $"Kaydedildi. Masada bu desteyle ({Texts.Faction(_faction)}) oynayacaksın." : "Kaydedildi ama geçersiz: oynamak için eksikleri tamamla.";
        RebuildLight();
    }

    private void Save()
    {
        var name = _name.Trim();
        if (name.Length == 0) { _message = "Önce desteye bir ad ver."; RebuildLight(); return; }
        var deck = CurrentDeck();
        // Yeniden adlandırılan deste eskisinin üstüne yazmasın: ad değiştiyse eskisi kalır (farklı deste).
        DeckStore.Save(new SavedDeck(name, deck.Faction, deck.LeaderId, deck.CardIds.ToList()));
        _loadedName = name;
        var errors = DeckValidator.Validate(deck, _options);
        _message = errors.Count == 0
            ? $"“{name}” kaydedildi."
            : $"“{name}” kaydedildi ama geçersiz: oynamak için eksikleri tamamla.";
        Rebuild();
    }

    private void DeleteLoaded()
    {
        if (_loadedName == null) return;
        DeckStore.Delete(_loadedName, _faction);
        var gone = _loadedName;
        NewDeck();
        _message = $"“{gone}” silindi.";
        RebuildLight();
    }
}
