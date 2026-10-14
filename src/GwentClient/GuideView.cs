using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GwentCore;

namespace GwentClient;

/// <summary>
/// "Nasıl oynanır" dergisi: oyunun kendi kartları, simgeleri ve yazısıyla çizilmiş yedi sayfalık kısa bir kılavuz.
/// Sağ/sol ok ya da düğmelerle sayfa çevrilir; her sayfada küçük bir örnek vardır. Kural metni <c>KURALLAR.md</c> ile uyumludur.
/// </summary>
public sealed class GuideView : Grid
{
    private sealed record Page(string Title, Func<UIElement> Build);

    private static readonly Brush Gold = Theme.B("#f0d890");
    private static readonly Brush Ink = Theme.B("#e8dcc4");
    private static readonly Brush Soft = Theme.B("#cfc4aa");

    private readonly Action _back;
    private readonly List<Page> _pages;
    private readonly ContentControl _body = new() { Height = 580, VerticalContentAlignment = VerticalAlignment.Top };
    private readonly ContentControl _titleHost = new();
    private readonly TextBlock _kicker = Ornate.Caption("", 0);
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _prev;
    private readonly Button _next;
    private int _page = -1;

    public int PageCount => _pages.Count;

    public GuideView(Action onBack, int startPage = 0)
    {
        _back = onBack;
        _pages = new List<Page>
        {
            new("Amaç", PageGoal),
            new("Kartı tanı", PageCards),
            new("Sıra sende", PageTurn),
            new("Güç hesabı", PagePower),
            new("Yetenekler", PageAbilities),
            new("Özel kartlar", PageSpecials),
            new("Fraksiyonlar ve lider", PageFactions),
        };

        var head = new StackPanel();
        head.Children.Add(_kicker);
        head.Children.Add(_titleHost);
        var rule = Ornate.Rule(520, 10);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(rule);

        _prev = Ui.Button("‹  Önceki", () => Go(_page - 1));
        _prev.Tag = "guide-prev";
        _next = Ui.Button("Sonraki  ›", () => Go(_page + 1), primary: true);
        _next.Tag = "guide-next";
        var menu = Ui.Button("Menüye dön", _back);
        menu.Tag = "guide-back";
        foreach (var b in new[] { _prev, _next, menu }) { b.FontSize = 19; b.Padding = new Thickness(26, 9, 26, 9); }
        menu.Margin = new Thickness(0, 0, 14, 0);

        var foot = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(menu);
        left.Children.Add(_prev);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_next);
        SetColumn(_dots, 1);
        SetColumn(right, 2);
        foot.Children.Add(left);
        foot.Children.Add(_dots);
        foot.Children.Add(right);

        var col = new StackPanel { Width = 1392 };
        col.Children.Add(head);
        col.Children.Add(_body);
        col.Children.Add(foot);

        var panel = Ornate.Panel(col, new Thickness(54, 34, 54, 36));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(24, 40, 24, 40), Child = panel };
        Children.Add(Ornate.Stage(fit));

        for (var i = 0; i < _pages.Count; i++)
        {
            var at = i;
            var dot = new TextBlock { FontSize = 20, Foreground = Theme.Gold, Margin = new Thickness(6, 0, 6, 0), Cursor = Cursors.Hand, Tag = "guide-dot", Background = Brushes.Transparent, ToolTip = _pages[i].Title };
            dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; Go(at); };
            _dots.Children.Add(dot);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _back(); e.Handled = true; }
            else if (e.Key is Key.Right or Key.PageDown or Key.Space) { Go(_page + 1); e.Handled = true; }
            else if (e.Key is Key.Left or Key.PageUp) { Go(_page - 1); e.Handled = true; }
        };
        Focusable = true;
        Loaded += (_, _) => Focus();
        Go(startPage, quiet: true);
    }

    private void Go(int page, bool quiet = false)
    {
        page = Math.Clamp(page, 0, _pages.Count - 1);
        if (page == _page) return;
        _page = page;
        var p = _pages[page];
        _kicker.Text = Ornate.Spaced($"NASIL OYNANIR  ·  SAYFA {page + 1} / {_pages.Count}");
        _titleHost.Content = Ornate.Heading(p.Title, null, 42);
        var content = p.Build();
        _body.Content = content;
        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var d = (TextBlock)_dots.Children[i];
            d.Text = i == page ? "◆" : "◇";
            d.Opacity = i == page ? 1 : 0.6;
        }
        _prev.IsEnabled = page > 0;
        _next.IsEnabled = page < _pages.Count - 1;
        if (!quiet) Sfx.Play("lock_in");
        Ornate.Reveal(new[] { content }, 16, 0, 0, 380);
    }

    // ---------- Sayfalar ----------

    private static UIElement PageGoal()
    {
        var text = new StackPanel();
        text.Children.Add(Lead("Gwent iki kişilik bir kart düellosudur. Amacın basit: **en iyi 3 raundun 2'sini** kazanmak."));
        text.Children.Add(Bullet("Her oyuncunun **2 mücevheri** vardır."));
        text.Children.Add(Bullet("Raund sonunda sahadaki **toplam gücü** yüksek olan raundu alır; kaybeden bir mücevher yitirir."));
        text.Children.Add(Bullet("Mücevheri tükenen oyunu kaybeder. İkisi birden tükenirse oyun berabere biter."));
        text.Children.Add(Bullet("Raund **berabere** biterse ikisi de bir mücevher yitirir (Nilfgaard hariç)."));
        text.Children.Add(Tip("Kısaca", "Üç raund, iki mücevher. Her raundu kazanmak zorunda değilsin: önemli olan **ikisini** almak."));

        var fig = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        fig.Children.Add(Pic("msg_round_victory", 150, 0, 0, 8));
        fig.Children.Add(PlayerLine("SEN", 2, 2, 31));
        fig.Children.Add(PlayerLine("RAKİP", 1, 2, 27));
        return Spread(text, Fig(fig, "Raundu 31'e 27 alan sen oldun; rakibin bir mücevheri söndü."));
    }

    private static UIElement PageCards()
    {
        var text = new StackPanel();
        text.Children.Add(Lead("Her kartta üç şeye bakarsın: **gücü**, **sırası** ve **yeteneği**."));
        text.Children.Add(RowInfo("row_close", "Yakın dövüş", "piyadeler ve şövalyeler"));
        text.Children.Add(RowInfo("row_ranged", "Menzilli", "okçular ve büyücüler"));
        text.Children.Add(RowInfo("row_siege", "Kuşatma", "mancınıklar ve balistalar"));
        text.Children.Add(Bullet("Her sıra ayrı toplanır; raund gücün **üç sıranın toplamıdır**."));
        text.Children.Add(Bullet("**Çevik** kartlar Yakın ya da Menzilli sıraya oynanabilir."));
        text.Children.Add(Tip("Kahraman kartlar", "Altın güneş rozetli kartlar **kahramandır**: hava, bağ, boynuz, Yak ve Yem onlara işlemez."));

        var cards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        cards.Children.Add(Labeled(CardImg("blue_stripes_commando", 150), "Yakın · Bağ"));
        cards.Children.Add(Labeled(CardImg("keira_metz", 150), "Menzilli"));
        cards.Children.Add(Labeled(CardImg("catapult", 150), "Kuşatma · Bağ"));
        cards.Children.Add(Labeled(CardImg("geralt_of_rivia", 150), "Kahraman"));
        return Spread(text, Fig(cards, "Sol üst: güç  ·  sol alt: yetenek  ·  sağ alt: sıra. Kartın üzerine gelince ayrıntısı çıkar."));
    }

    private static UIElement PageTurn()
    {
        var text = new StackPanel();
        text.Children.Add(Lead("Maç başında **10 kart** çekersin; istersen **2 tanesini** değiştirebilirsin."));
        text.Children.Add(Bullet("Sıran gelince **bir kart oynarsın** ya da **pas geçersin**."));
        text.Children.Add(Bullet("**Pas geçen**, o raundun geri kalanında oynayamaz."));
        text.Children.Add(Bullet("**Her tur kart çekmezsin.** 10 kartla 3 raund oynanır; fazladan kart yalnızca Casus gibi yeteneklerden gelir."));
        text.Children.Add(Bullet("Rakip pas geçtiyse öndeysen hemen pas geçip raundu bitir; fazla kart harcama."));
        text.Children.Add(Tip("İpucu", "**Kart tasarrufu** oyunun asıl stratejisidir. Önde olduğun raundda fazladan kart yakma."));

        var steps = new StackPanel { Width = 640 };
        steps.Children.Add(Step("msg_your_turn", "Sıra sende", "Elinden bir kart seç ve bir sıraya oyna."));
        steps.Children.Add(Step("laurel", "Rakip oynar", "Hamleler sırayla gider; rakibin sahasını izle."));
        steps.Children.Add(Step("msg_round_pass", "Pas geç", "Kartlarını harcamak istemiyorsan pas geçersin. O raund artık oynayamazsın."));
        steps.Children.Add(Step("msg_round_victory", "Raund biter", "İkiniz de pas geçince toplam güçler karşılaştırılır, saha temizlenir."));
        return Spread(text, Fig(steps, "Bir raundun akışı."));
    }

    private static UIElement PagePower()
    {
        var text = new StackPanel();
        text.Children.Add(Lead("Bir kartın gücü şu **sırayla** hesaplanır:"));
        text.Children.Add(Numbered("1", "Taban güç", "kartın üzerindeki sayı."));
        text.Children.Add(Numbered("2", "Hava", "etkilenen sırada güç 1'e düşer."));
        text.Children.Add(Numbered("3", "Sıkı Bağ", "aynı isimli kartlar sayıları kadar çarpılır."));
        text.Children.Add(Numbered("4", "Moral", "sıradaki diğer birimlere +1."));
        text.Children.Add(Numbered("5", "Boynuz", "sıranın tamamı ×2."));
        text.Children.Add(Tip("Kahramanlar", "Altın kartlar bu hesabın **hiçbir** adımından etkilenmez; gücü kartın üstündeki sayıdır."));

        var rows = new StackPanel { Width = 700 };
        rows.Children.Add(Strip("Normal", 12, 3, 36));
        rows.Children.Add(Strip("Don (Biting Frost) gelince", 3, 3, 9));
        rows.Children.Add(Strip("Boynuz eklenince", 24, 3, 72));
        return Spread(text, Fig(rows, "Aynı üç Blue Stripes Commando (taban 4), üç farklı durumda: bağ 4'ü 12 yapar; don önce 1'e düşürür, bağ onu 3 yapar; boynuz 12'yi 24 yapar."));
    }

    private static UIElement PageAbilities()
    {
        var left = new StackPanel { Width = 660 };
        left.Children.Add(Ability("ab_spy", "Casus", "Rakibin sahasına konur, puanı ona yazılır; sen **2 kart çekersin**."));
        left.Children.Add(Ability("ab_medic", "Medic", "Mezarlığından bir birim seçip **hemen oynarsın**."));
        left.Children.Add(Ability("ab_muster", "Toplanma", "Elindeki ve destendeki aynı gruptaki **tüm kartlar** iner."));
        left.Children.Add(Ability("ab_bond", "Sıkı Bağ", "Aynı isimli kartlarla yan yana olunca gücü **çarpılır**."));
        left.Children.Add(Ability("ab_morale", "Moral", "Sıradaki diğer birimlere **+1** verir."));
        var right = new StackPanel { Width = 660 };
        right.Children.Add(Ability("ab_agile", "Çevik", "**Yakın ya da Menzilli** sıraya oynanabilir."));
        right.Children.Add(Ability("ab_scorch", "Yak", "Oynanınca belirtilen sıradaki **en güçlü** birimleri yok eder."));
        right.Children.Add(Ability("ab_horn", "Boynuz", "Birimin kendi sırasını **×2** yapar (ör. Dandelion)."));
        right.Children.Add(Ability("ab_berserker", "Berserker", "Sırada **Mardroeme** varsa güçlü bir canavara dönüşür."));
        right.Children.Add(Ability("corner_sun", "Kahraman", "Hiçbir etkiden ve Medic'ten **etkilenmez**."));

        var cols = new StackPanel { Orientation = Orientation.Horizontal };
        cols.Children.Add(left);
        cols.Children.Add(new Border { Width = 1.5, Background = Ornate.GoldThin, Margin = new Thickness(20, 4, 36, 4) });
        cols.Children.Add(right);
        var page = new StackPanel();
        page.Children.Add(cols);
        var tip = Tip("Örnek kombinasyon", "Bir **Casus** oyna (2 kart çek), sonra **Yem** ile casusu geri alıp yeniden oyna: her seferinde 2 yeni kart. Medic'i de aynı şekilde tekrar kullanabilirsin.");
        tip.Margin = new Thickness(0, 14, 0, 0);
        page.Children.Add(tip);
        return page;
    }

    private static UIElement PageSpecials()
    {
        var catalog = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        catalog.Children.Add(Catalog("decoy", "Yem", "Sahandaki kahraman olmayan bir birimi eline geri alır; yerine geçer."));
        catalog.Children.Add(Catalog("commanders_horn", "Boynuz", "Seçtiğin sıranın gücünü ×2 yapar. Sıra başına bir tane."));
        catalog.Children.Add(Catalog("scorch", "Yak", "İki sahadaki en güçlü (kahraman olmayan) birimleri yok eder."));
        catalog.Children.Add(Catalog("biting_frost", "Don", "İki sahada da Yakın sırayı 1'e düşürür."));
        catalog.Children.Add(Catalog("impenetrable_fog", "Sis", "İki sahada da Menzilli sırayı 1'e düşürür."));
        catalog.Children.Add(Catalog("torrential_rain", "Yağmur", "İki sahada da Kuşatma sırasını 1'e düşürür."));
        catalog.Children.Add(Catalog("clear_weather", "Açık hava", "Tüm hava etkilerini kaldırır."));

        var page = new StackPanel();
        page.Children.Add(Lead("Birimlerin yanında desteye **en fazla 10 özel kart** girer. Bunlar sahaya birim koymaz, oyunu değiştirir."));
        page.Children.Add(Fig(catalog, "Üç hava kartı (Don, Sis, Yağmur) ve onları kaldıran Açık hava. Kartların üzerine gelince ayrıntısı çıkar."));
        var row = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var a = Tip("Hava iki tarafı da vurur", "Havayı kendi güçlü sırana zarar vermeyecek zamanda oyna; rakibin sırası zayıfken Açık hava'yı sakla.");
        a.Margin = new Thickness(0, 0, 14, 0);
        var b = Tip("Yak'ı ne zaman?", "Rakibin en güçlü birimi **senin** en güçlünden büyükken. Eşitlikte hepsi yok olur, kendi kartların da dahil.");
        b.Margin = new Thickness(14, 0, 0, 0);
        SetColumn(b, 1);
        row.Children.Add(a);
        row.Children.Add(b);
        page.Children.Add(row);
        return page;
    }

    private static UIElement PageFactions()
    {
        var shields = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        shields.Children.Add(Faction("northern_realms", "Kuzey Krallıkları", "Raundu kazanınca desteden 1 kart çeker."));
        shields.Children.Add(Faction("nilfgaard", "Nilfgaard", "Beraberlikte raundu kazanır."));
        shields.Children.Add(Faction("scoiatael", "Scoia'tael", "Oyuna kimin başlayacağını seçer."));
        shields.Children.Add(Faction("monsters", "Canavarlar", "Her raund sonunda sahadan rastgele 1 birim kalır."));
        shields.Children.Add(Faction("skellige", "Skellige", "3. raundun başında mezarlıktan 2 kart sahaya iner."));

        var page = new StackPanel();
        page.Children.Add(Lead("Seçtiğin **fraksiyon** bir de kalıcı yetenek verir. Tarafsız kartlar her destede kullanılır."));
        page.Children.Add(shields);

        var row = new Grid { Margin = new Thickness(0, 26, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var leader = Tip("Lider", "Her destenin bir lideri vardır. Lider yeteneği **oyun başına bir kez** kullanılır ve kart oynamak yerine **bir tur** harcar.");
        leader.VerticalAlignment = VerticalAlignment.Top;
        leader.Margin = new Thickness(0, 0, 26, 0);
        var tips = new StackPanel();
        tips.Children.Add(Ornate.Caption("Üç küçük öğüt", 0));
        tips.Children.Add(Bullet("Bir raundu **bilerek bırak**, kartlarını sakla; kalan iki raundu al."));
        tips.Children.Add(Bullet("Casus rakibe puan yazar ama **2 kart** getirir: kart farkı çoğu zaman puandan değerlidir."));
        tips.Children.Add(Bullet("Rakip pas geçtiyse **öne geçmeye yetecek kadar** kart oyna; fazlasını sonraki raunda sakla."));
        SetColumn(tips, 1);
        row.Children.Add(leader);
        row.Children.Add(tips);
        page.Children.Add(row);
        return page;
    }

    // ---------- Parçalar ----------

    private static TextBlock Rich(string s, double size, Brush? fg = null)
    {
        var tb = new TextBlock
        {
            FontFamily = Theme.Serif, FontSize = size * Theme.FontScale, Foreground = fg ?? Soft, TextWrapping = TextWrapping.Wrap,
            LineHeight = size * Theme.FontScale * 1.3
        };
        var parts = s.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            var run = new Run(parts[i]);
            if (i % 2 == 1) { run.Foreground = Gold; run.FontWeight = FontWeights.Bold; }
            tb.Inlines.Add(run);
        }
        return tb;
    }

    private static UIElement Lead(string s)
    {
        var t = Rich(s, 24, Ink);
        t.Margin = new Thickness(0, 4, 0, 18);
        return t;
    }

    private static UIElement Bullet(string s)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = "◆", FontSize = 12, Foreground = Theme.Gold, Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top });
        var t = Rich(s, 19);
        SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    private static UIElement Numbered(string n, string title, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var num = Ui.Text(n, 34, Theme.Gold, true);
        num.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(num);
        var t = Rich($"**{title}** — {text}", 19);
        t.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    private static UIElement RowInfo(string glyph, string name, string desc)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = GwintCard.Glyph(glyph, 42, "badge_hero");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(icon);
        var t = Rich($"**{name}** — {desc}", 19);
        t.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    private static FrameworkElement Tip(string title, string text)
    {
        var inner = new StackPanel();
        inner.Children.Add(Ornate.Caption(title, 0));
        inner.Children.Add(Rich(text, 18, Ink));
        return new Border
        {
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(18, 12, 16, 14), Child = inner,
            Background = Theme.B("#28c9a24b"), BorderBrush = Theme.Gold, BorderThickness = new Thickness(3, 0, 0, 0)
        };
    }

    private static UIElement Spread(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new ContentControl { Content = left, Margin = new Thickness(0, 0, 44, 0), VerticalAlignment = VerticalAlignment.Top };
        var r = new ContentControl { Content = right, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        SetColumn(r, 1);
        g.Children.Add(l);
        g.Children.Add(r);
        return g;
    }

    /// <summary>Koyu çerçeveli resim alanı + altında italik açıklama.</summary>
    private static UIElement Fig(UIElement content, string caption)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new Border
        {
            Background = Theme.B("#cc0b0805"), BorderBrush = Ornate.GoldThin, BorderThickness = new Thickness(1.4), Padding = new Thickness(22, 18, 22, 18),
            HorizontalAlignment = HorizontalAlignment.Center, Child = content
        });
        var cap = Ui.Text(caption, 15, Theme.TextDim, false, TextAlignment.Center, true);
        cap.FontStyle = FontStyles.Italic;
        cap.Margin = new Thickness(10, 12, 10, 0);
        cap.MaxWidth = 700;
        sp.Children.Add(cap);
        return sp;
    }

    private static FrameworkElement CardImg(string id, double w, int? strength = null) =>
        new GwintCard(CardCatalog.Default.Get(id), w, strength) { Cursor = Cursors.Arrow, Margin = new Thickness(8, 0, 8, 0) };

    private static UIElement Labeled(FrameworkElement card, string label)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(card);
        var t = Ui.Text(label, 16, Gold, true, TextAlignment.Center);
        t.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(t);
        return sp;
    }

    private static FrameworkElement Pic(string name, double height, double l, double t, double b)
    {
        var src = Assets.Ui(name);
        if (src == null) return new Border { Height = 4 };
        var im = new Image { Source = src, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(l, t, 0, b) };
        RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
        return im;
    }

    private static UIElement Score(int n, double size = 62)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (Assets.Ui("badge_big") is { } src) g.Children.Add(new Image { Source = src, Stretch = Stretch.Uniform });
        else g.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Theme.B("#e8dcc4"), Stroke = Theme.B("#c97a20"), StrokeThickness = 3 });
        g.Children.Add(new TextBlock
        {
            Text = n.ToString(), FontFamily = Theme.Serif, FontWeight = FontWeights.Bold, FontSize = size * (n >= 10 ? 0.46 : 0.58),
            Foreground = Theme.B("#2a1c0e"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        return g;
    }

    private static UIElement PlayerLine(string name, int lit, int total, int score)
    {
        var g = new Grid { Width = 560, Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var n = Ui.Text(name, 28, Gold, true);
        n.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(n);
        var gems = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < total; i++)
        {
            var gem = Pic("gem", 52, 0, 0, 0);
            gem.Opacity = i < lit ? 1 : 0.2;
            gem.Margin = new Thickness(0, 0, 10, 0);
            gems.Children.Add(gem);
        }
        SetColumn(gems, 1);
        g.Children.Add(gems);
        var s = Score(score, 72);
        SetColumn(s, 2);
        g.Children.Add(s);
        return g;
    }

    private static UIElement Step(string pic, string title, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var p = Pic(pic, 84, 0, 0, 0);
        p.HorizontalAlignment = HorizontalAlignment.Center;
        p.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(p);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Ui.Text(title, 22, Gold, true));
        sp.Children.Add(Rich(text, 17, Soft));
        SetColumn(sp, 1);
        g.Children.Add(sp);
        return g;
    }

    /// <summary>Bir sıranın örneği: solda sıra simgesi, ortada üç Blue Stripes Commando, sağda sıra toplamı.</summary>
    private static UIElement Strip(string label, int each, int count, int total)
    {
        var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var icon = GwintCard.Glyph("row_close", 40, "badge_hero");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        l.Children.Add(icon);
        var t = Rich($"**{label}**", 18, Gold);
        t.Margin = new Thickness(0, 6, 0, 0);
        l.Children.Add(t);
        g.Children.Add(l);
        var cards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < count; i++) cards.Children.Add(CardImg("blue_stripes_commando", 92, each));
        SetColumn(cards, 1);
        g.Children.Add(cards);
        var s = Score(total, 70);
        SetColumn(s, 2);
        g.Children.Add(s);
        return new Border { Background = Theme.B("#661d160e"), BorderBrush = Ornate.GoldThin, BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 14, 8), Child = g, Margin = new Thickness(0, 4, 0, 4) };
    }

    private static UIElement Ability(string glyph, string title, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement icon;
        if (glyph == "corner_sun")
        {
            var src = Assets.Ui("corner_sun");
            icon = src != null ? new Image { Source = src, Width = 62, Height = 62, Stretch = Stretch.Uniform } : new TextBlock { Text = "★", FontSize = 40, Foreground = Theme.Gold };
        }
        else icon = GwintCard.Glyph(glyph, 62, "badge");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Ui.Text(title, 23, Gold, true));
        sp.Children.Add(Rich(text, 17, Soft));
        SetColumn(sp, 1);
        g.Children.Add(sp);
        return g;
    }

    private static UIElement Catalog(string id, string name, string desc)
    {
        var sp = new StackPanel { Width = 184, Margin = new Thickness(4, 0, 4, 0) };
        var card = CardImg(id, 108);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(card);
        var n = Ui.Text(name, 20, Gold, true, TextAlignment.Center);
        n.Margin = new Thickness(0, 10, 0, 4);
        sp.Children.Add(n);
        var d = Ui.Text(desc, 15, Theme.TextDim, false, TextAlignment.Center, true);
        sp.Children.Add(d);
        return sp;
    }

    private static UIElement Faction(string id, string name, string text)
    {
        var sp = new StackPanel { Width = 258, Margin = new Thickness(6, 8, 6, 0) };
        var shield = Pic("shield_" + id, 128, 0, 0, 0);
        shield.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(shield);
        var n = Ui.Text(name, 22, Gold, true, TextAlignment.Center);
        n.Margin = new Thickness(0, 8, 0, 4);
        sp.Children.Add(n);
        sp.Children.Add(Ui.Text(text, 16, Soft, false, TextAlignment.Center, true));
        return sp;
    }
}
