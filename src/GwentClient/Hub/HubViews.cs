using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GwentClient;

/// <summary>Merkez ekranların ortak renkleri (yalnız kimlik için: hangi dünyada olduğunu gösterir).</summary>
public static class HubColors
{
    public static readonly Color Gwent = Color.FromRgb(0xc9, 0xa2, 0x4a);
    public static readonly Color Online = Color.FromRgb(0x4f, 0xa3, 0xa8);
    public static readonly Color Keep = Color.FromRgb(0x8f, 0xb4, 0xd0);
    public static readonly Color Han = Color.FromRgb(0xe0, 0x87, 0x3a);
}

/// <summary>Ana menü: dört kapı (Gwent, Çevrimiçi, Kaer Morhen, Deste kurucu) ve alt şeritte ikincil öğeler. Han Ligi botlara karşı Gwent merkezinde, online Çevrimiçi'de.</summary>
public sealed class HubView : Grid
{
    private static int _last;   // son seçilen kapı bir sonraki açılışta hatırlanır
    private readonly TileRow _row;

    public HubView(Action? openPlay, Action? openOnline, Action? openKeep, Action? openBuilder, Action? openGuide, Action? openStats, Action? openSettings, bool animate = true)
    {
        var backdrop = new HubBackdrop(animate);
        var tiles = new List<HubTile>();
        void Add(string key, string title, string tagline, string[] facts, Color accent, string fallback, Action? go)
            => tiles.Add(new HubTile(key, title, tagline, facts, accent, HubArt.Tile(key, fallback), () => go?.Invoke(), animate) { Backdrop = HubArt.Wide(key, fallback) });
        Add("gwent", "Gwent", "Klasik maç, kariyer, draft ve Han Ligi: hepsi yapay zekâya karşı.", new[] { "Yapay zekâya karşı", "Tek kişilik" }, HubColors.Gwent, "map_novigrad", openPlay);
        Add("online", "Çevrimiçi", "Arkadaşınla oda kur, turnuva düzenle ya da davet koduyla katıl.", new[] { "2–8 oyuncu", "Davet koduyla" }, HubColors.Online, "map_skellige", openOnline);
        Add("keep", "Kaer Morhen", "Kaleni geliştir, sözleşme al ve sefere çık.", new[] { "Sefer modu", "Tek kişilik" }, HubColors.Keep, "km/scene_main", openKeep);
        Add("deck", "Deste kurucu", "Hızlı Gwent ve online maçlar için fraksiyon destelerini kendi zevkine göre düzenle.", new[] { "Koleksiyon" }, HubColors.Gwent, "board", openBuilder);

        _row = new TileRow(tiles, 430, Math.Clamp(_last, 0, tiles.Count - 1));
        _row.ActiveChanged += t => { _last = tiles.IndexOf(t); backdrop.Show(t.Backdrop ?? t.Pano); };
        backdrop.Show(_row.Active?.Backdrop ?? _row.Active?.Pano);

        var links = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };
        void Link(string text, Action? go)
        {
            if (go == null) return;
            if (links.Children.Count > 0) links.Children.Add(new TextBlock { Text = "◆", FontSize = 9, Foreground = Theme.GoldDim, VerticalAlignment = VerticalAlignment.Center });
            links.Children.Add(HubKit.TextLink(text, go));
        }
        Link("Nasıl oynanır", openGuide);
        Link("Profil", openStats);
        Link("Ayarlar", openSettings);
        Link("Çıkış", () => Application.Current.Shutdown());

        var body = new Grid { Margin = new Thickness(44, 10, 44, 0) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _row.VerticalAlignment = VerticalAlignment.Center;
        _row.MaxHeight = 760;
        body.Children.Add(_row);
        SetRow(links, 1);
        body.Children.Add(links);

        var screen = HubKit.Screen(body, backdrop, null, null, openStats, showLogo: true);
        screen.KeyDown += (_, e) => { if (_row.HandleKey(e.Key)) e.Handled = true; };
        UpdateBanner.Attach(screen);
        Children.Add(screen);
        Ornate.Reveal(new UIElement[] { _row }, 36, 150, 0, 700);
    }

    internal TileRow Row => _row;
}

/// <summary>"Gwent" alt merkezi: yapay zekâya karşı tüm kipler tek yerde (Hızlı Gwent, Collect 'Em All, Draft, Han Ligi).</summary>
public sealed class PlayHubView : Grid
{
    private readonly TileRow _row;

    public PlayHubView(Action back, Action openQuick, Action openCareer, Action openDraft, Action openHan, Action? openStats, bool animate = true)
    {
        var backdrop = new HubBackdrop(animate);
        var tiles = new List<HubTile>
        {
            new("quick", "Hızlı Gwent", "Deste, lider ve rakibi seç; hemen oyna.", new[] { "Yapay zekâya karşı", "Günün laneti" }, HubColors.Gwent, HubArt.Tile("quick", "map_novigrad"), openQuick, animate) { Backdrop = HubArt.Wide("quick", "map_novigrad") },
            new("career", "Collect 'Em All", "Oyunun NPC'lerine meydan oku, bölgeleri gez, Crown kazan.", new[] { "Kariyer", "Ödüllü" }, HubColors.Gwent, HubArt.Tile("career", "map_bob"), openCareer, animate) { Backdrop = HubArt.Wide("career", "map_bob") },
            new("draft", "Draft", "25 kez 3 karttan birini seç, 7 galibiyete ulaşmaya çalış.", new[] { "Seçerek deste kur", "Koşu" }, HubColors.Gwent, HubArt.Tile("draft", "map_prolog_village"), openDraft, animate) { Backdrop = HubArt.Wide("draft", "map_prolog_village") },
            new("han", "Han Ligi", "Pazardan kart topla, üç eşini birleştirip güçlendir, savaşta işaretlerle araya gir. Son ayakta kalan kazanır.", new[] { "7 bota karşı", "Arkadaşlarla: Çevrimiçi" }, HubColors.Han, HubArt.Tile("han", "han/table"), openHan, animate) { Backdrop = HubArt.Wide("han", "han/table") }
        };
        _row = new TileRow(tiles, 430);
        _row.ActiveChanged += t => backdrop.Show(t.Backdrop ?? t.Pano);
        backdrop.Show(_row.Active?.Backdrop ?? _row.Active?.Pano);
        _row.VerticalAlignment = VerticalAlignment.Center;
        _row.MaxHeight = 760;
        var body = new Grid { Margin = new Thickness(44, 10, 44, 20) };
        body.Children.Add(_row);

        var screen = HubKit.Screen(body, backdrop, new (string, Action?)[] { ("Gwent", null) }, back, openStats);
        screen.KeyDown += (_, e) => { if (_row.HandleKey(e.Key)) e.Handled = true; };
        Children.Add(screen);
        Ornate.Reveal(new UIElement[] { _row }, 36, 100, 0, 600);
    }
}
