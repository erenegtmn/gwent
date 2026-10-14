using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentAI;
using GwentAI.Career;
using GwentCore;

namespace GwentClient;

/// <summary>Kariyer ekranından çıkan yollar (MainWindow bağlar).</summary>
public sealed record CareerNav(Action<CareerOpponent> Play, Action OpenDeck, Action<CareerRegion> OpenShop, Action Back, Action Reset);

/// <summary>
/// Kariyer haritası: solda bölgeler (sırayla açılır), ortada oyunun kendi dünya haritası ve üzerinde Gwent oyuncularının portreleri.
/// Bir portreye tıklayınca solda (bölge listesinin yerine) rakibin kartı açılır: kim olduğu, destesi, ödülü ve "Masaya otur".
/// Son maçın ödülü (yeni kartlar) ekrana gelince ortada gösterilir.
/// </summary>
public sealed class CareerView : Grid
{
    private const double MapW = 1300, MapH = 850;

    private readonly CareerSave _s;
    private readonly CareerNav _nav;
    private CareerRegion _region;
    private string? _selected;
    private readonly Grid _content = new() { Width = 1920, Height = 1080 };
    private readonly ContentControl _left = new();
    private readonly Grid _map = new() { Width = MapW, Height = MapH, ClipToBounds = true };
    private readonly ContentControl _top = new();

    public CareerView(CareerSave save, CareerNav nav, CareerReward? lastReward = null, string? select = null)
    {
        _s = save;
        _nav = nav;
        _region = CareerWorld.Regions.FirstOrDefault(r => r.Id == save.LastRegion && CareerLogic.IsUnlocked(save, r)) ?? CareerWorld.Regions[0];
        _selected = select;

        // Yerleşim 1920x1080 tasarlanır, pencereye orantılı ölçeklenir
        var top = new Grid { Margin = new Thickness(44, 34, 44, 0), VerticalAlignment = VerticalAlignment.Top, Height = 110 };
        top.Children.Add(_top);
        _content.Children.Add(top);

        _left.Width = 430;
        _left.HorizontalAlignment = HorizontalAlignment.Left;
        _left.Margin = new Thickness(44, 160, 0, 0);
        _left.VerticalAlignment = VerticalAlignment.Top;
        _left.MaxHeight = 880;
        _content.Children.Add(_left);

        var mapFrame = new Grid();
        mapFrame.Children.Add(_map);
        var panel = Ornate.Panel(mapFrame, new Thickness(14), 18, 56);
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.Margin = new Thickness(0, 156, 44, 0);
        _content.Children.Add(panel);

        Children.Add(Ornate.Stage(new Viewbox { Stretch = Stretch.Uniform, Child = _content }, _region.Pano, 0.75));
        if (lastReward != null && (lastReward.Cards.Count > 0 || lastReward.TournamentOver)) Children.Add(RewardOverlay(lastReward));

        Focusable = true;
        Loaded += (_, _) => Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_selected != null) { _selected = null; Draw(); } else _nav.Back();
        };
        Draw();
    }

    private void Draw()
    {
        _s.LastRegion = _region.Id;
        _top.Content = TopBar();
        _left.Content = _selected != null ? Detail(CareerWorld.Opponent(_selected)) : RegionList();
        DrawMap();
    }

    // ---------- Üst şerit ----------

    private UIElement TopBar()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var head = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(Ornate.Heading("Collect 'Em All", "Masadan masaya: White Orchard'dan Toussaint'e Gwent yolculuğu", 46));
        g.Children.Add(head);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var (have, total) = CareerLogic.Progress(_s);
        right.Children.Add(Stat($"◆ {_s.Crowns}", "Crown"));
        right.Children.Add(Stat($"{have}/{total}", "koleksiyon"));
        var deck = _s.ActiveDeck();
        right.Children.Add(Stat(deck == null ? "—" : Texts.FactionShort(deck.Faction), deck == null ? "deste yok" : $"{deck.CardIds.Count} kartlık deste"));
        var me = Portraits.Medallion(Settings.Portrait, 92, glow: true);
        ((FrameworkElement)me).Margin = new Thickness(18, 0, 0, 0);
        right.Children.Add(me);
        SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    private static UIElement Stat(string value, string label)
    {
        var sp = new StackPanel { Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var v = Ui.Text(value, 30, Theme.B("#f0d890"), true, TextAlignment.Right);
        v.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1.5, Opacity = 0.9 };
        sp.Children.Add(v);
        sp.Children.Add(Ui.Text(label, 15, Theme.TextDim, false, TextAlignment.Right));
        return sp;
    }

    // ---------- Sol: bölgeler ve düğmeler ----------

    private UIElement RegionList()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ornate.Caption("Bölgeler", 0));
        for (var i = 0; i < CareerWorld.Regions.Count; i++)
        {
            var r = CareerWorld.Regions[i];
            var open = CareerLogic.IsUnlocked(_s, r);
            var sel = r.Id == _region.Id;
            var duels = CareerWorld.In(r.Id).Where(o => !o.IsTournament).ToList();
            var beaten = duels.Count(o => _s.Beaten(o.Id));

            var inner = new StackPanel { IsHitTestVisible = false };
            var name = Ui.Text(r.Name, 24, sel ? Theme.B("#f0d890") : open ? Theme.Text : Theme.B("#7a705e"), true);
            inner.Children.Add(name);
            string sub;
            if (open) sub = $"{beaten}/{duels.Count} rakip yenildi" + (CareerWorld.In(r.Id).Any(o => o.IsTournament && _s.Beaten(o.Id)) ? "  ·  turnuva şampiyonu" : "");
            else
            {
                var prev = CareerWorld.Regions[i - 1];
                var need = Math.Max(0, r.NeedWins - CareerLogic.BeatenIn(_s, prev.Id));
                var tour = CareerLogic.PendingTournament(_s, prev);
                sub = $"Kilitli · {prev.Name}: " + (need > 0 ? $"{need} rakip daha yen" : "") + (need > 0 && tour != null ? ", " : "") + (tour != null ? tour.Name + " turnuvasını kazan" : "");
            }
            inner.Children.Add(Ui.Text(sub, 14, open ? Theme.TextDim : Theme.B("#8a6a4a")));

            var frame = new ChamferFrame
            {
                Fill = sel ? Theme.B("#e63a2c14") : Theme.B("#cc140f09"), Edge = sel ? Ornate.GoldEdge : Theme.B("#5a4a30"),
                Inner = sel ? Theme.B("#88e0bf6a") : null, Cut = 10, Inset = 4
            };
            var host = new Grid { Margin = new Thickness(0, 0, 0, 10), Cursor = open ? Cursors.Hand : Cursors.Arrow, Background = Brushes.Transparent, Tag = "career-region:" + r.Id };
            host.Children.Add(frame);
            inner.Margin = new Thickness(20, 10, 16, 12);
            host.Children.Add(inner);
            if (!open) host.Opacity = 0.7;
            var region = r;
            if (open)
            {
                host.MouseEnter += (_, _) => { if (!sel) frame.Edge = Theme.B("#a88a40"); };
                host.MouseLeave += (_, _) => { if (!sel) frame.Edge = Theme.B("#5a4a30"); };
                host.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (region.Id == _region.Id) return; Sfx.Play("cursor_movement"); _region = region; _selected = null; Draw(); };
            }
            sp.Children.Add(host);
        }

        var bar = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        Button Big(string text, Action click, string tag, bool primary = false)
        {
            var b = Ui.Button(text, click, primary);
            b.FontSize = 21;
            b.Padding = new Thickness(18, 10, 18, 10);
            b.Margin = new Thickness(0, 0, 0, 10);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Tag = tag;
            return b;
        }
        bar.Children.Add(Big("Destem", _nav.OpenDeck, "career-deck"));
        bar.Children.Add(Big($"Tüccar · {_region.Name}", () => _nav.OpenShop(_region), "career-shop"));
        bar.Children.Add(Big("Ana menü", _nav.Back, "career-back"));
        var reset = Ui.Button("Baştan başla", () =>
        {
            if (MessageBox.Show("Collect 'Em All baştan başlasın mı? Koleksiyon, Crown'lar ve yenilen rakipler silinir.", "Gwent", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _nav.Reset();
        });
        reset.FontSize = 14;
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 6, 0, 0);
        bar.Children.Add(reset);
        sp.Children.Add(bar);
        return sp;
    }

    // ---------- Harita ----------

    private void DrawMap()
    {
        _map.Children.Clear();
        var c = _region.Crop;
        if (Assets.Ui(_region.Map) is { } src)
        {
            var img = new Rectangle
            {
                Width = MapW, Height = MapH, IsHitTestVisible = false,
                Fill = new ImageBrush(src) { Viewbox = new Rect(c[0], c[1], c[2], c[3]), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill }
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _map.Children.Add(img);
        }
        else _map.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x3a, 0x30, 0x22), Color.FromRgb(0x1a, 0x14, 0x0e), 90) });
        // Sıcak ton ve kenar karartması: harita eski bir parşömen gibi
        _map.Children.Add(new Border { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x30, 0x40, 0x28, 0x08)) });
        _map.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75,
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0xb0, 0, 0, 0), 1) }
            }
        });

        // Bölge adı (sol alt köşe)
        var title = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30, 0, 0, 24), IsHitTestVisible = false };
        title.Children.Add(Ornate.Heading(_region.Name.ToUpperInvariant(), null, 48));
        var sub = Ui.Text(_region.Subtitle, 18, Theme.B("#e0d4b8"));
        sub.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 1 };
        title.Children.Add(sub);
        _map.Children.Add(title);

        var pins = new Canvas { Width = MapW, Height = MapH };
        foreach (var o in CareerWorld.In(_region.Id).OrderBy(o => o.Y))
        {
            var pin = Pin(o);
            pin.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = (o.X - c[0]) / c[2] * MapW;
            var y = (o.Y - c[1]) / c[3] * MapH;
            Canvas.SetLeft(pin, x - pin.DesiredSize.Width / 2);
            Canvas.SetTop(pin, y - 50);
            pins.Children.Add(pin);
        }
        _map.Children.Add(pins);

        if (CareerWorld.In(_region.Id).All(o => !_s.Beaten(o.Id)) && _region.Id == CareerWorld.Regions[0].Id && _selected == null)
        {
            var hint = Ui.Text("Bir portreye tıkla ve masaya otur.", 22, Theme.B("#f0d890"), true);
            hint.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1.5, Opacity = 1 };
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.Margin = new Thickness(0, 26, 0, 0);
            hint.IsHitTestVisible = false;
            _map.Children.Add(hint);
        }
    }

    private string FaceOf(CareerOpponent o) => o.Face ?? NpcDecks.Get(o.Decks[0], o.Level).LeaderId;

    private FrameworkElement Pin(CareerOpponent o)
    {
        var beaten = _s.Beaten(o.Id);
        var sel = o.Id == _selected;
        var running = o.IsTournament && _s.Tournament?.Opponent == o.Id;
        var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "career-pin:" + o.Id };
        FrameworkElement face;
        if (o.IsTournament)
        {
            var g = new Grid { Width = 104, Height = 100, HorizontalAlignment = HorizontalAlignment.Center };
            g.Children.Add(new Ellipse { Width = 92, Height = 92, Fill = new RadialGradientBrush(Color.FromArgb(0xe0, 0x2a, 0x1e, 0x0e), Color.FromArgb(0xf0, 0x0e, 0x0a, 0x06)), Stroke = Ornate.GoldEdge, StrokeThickness = 3 });
            if (Assets.Ui("laurel") is { } l) g.Children.Add(new Image { Source = l, Width = 78, Stretch = Stretch.Uniform });
            else g.Children.Add(Ui.Text("♛", 44, Theme.Gold, true, TextAlignment.Center));
            face = g;
        }
        else face = Portraits.Medallion(FaceOf(o), 92);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.IsHitTestVisible = false;
        if (sel || running) face.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 210, 110), BlurRadius = 26, ShadowDepth = 0, Opacity = 1 };
        else face.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.9 };
        sp.Children.Add(face);

        var plate = new Border
        {
            Background = sel ? Theme.B("#f02c200e") : Theme.B("#e0120c07"), BorderBrush = beaten ? Theme.Gold : Theme.B("#6a5228"), BorderThickness = new Thickness(1.3),
            Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 0), IsHitTestVisible = false,
            Child = Ui.Text((beaten ? "✓ " : "") + o.Name + (running ? $"  ·  {_s.Tournament!.Round + 1}. tur" : ""), 16, beaten ? Theme.B("#f0d890") : Theme.Text, true)
        };
        sp.Children.Add(plate);

        var scale = new ScaleTransform(1, 1);
        sp.RenderTransformOrigin = new Point(0.5, 0.5);
        sp.RenderTransform = scale;
        sp.MouseEnter += (_, _) => { scale.ScaleX = scale.ScaleY = 1.08; };
        sp.MouseLeave += (_, _) => { scale.ScaleX = scale.ScaleY = 1; };
        sp.MouseLeftButtonUp += (_, e) => { e.Handled = true; Sfx.Play("cursor_movement"); _selected = o.Id; Draw(); };
        return sp;
    }

    // ---------- Sağ: rakip kartı ----------

    private UIElement Detail(CareerOpponent o)
    {
        var sp = new StackPanel();
        var deck = CareerLogic.OpponentDeck(_s, o);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement face = o.IsTournament ? Pic("laurel", 100) : Portraits.Medallion(FaceOf(o), 110, glow: true);
        head.Children.Add(face);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Width = 220 };
        names.Children.Add(Ui.Text(o.Name, 28, Theme.B("#f0d890"), true, TextAlignment.Left, true));
        var title = Ui.Text(o.Title, 16, Theme.B("#cfc4aa"), false, TextAlignment.Left, true);
        title.FontStyle = FontStyles.Italic;
        names.Children.Add(title);
        head.Children.Add(names);
        sp.Children.Add(head);
        sp.Children.Add(Ornate.Rule(360, 10));

        // Deste
        var drow = new StackPanel { Orientation = Orientation.Horizontal };
        if (Assets.Ui("shield_" + deck.Faction) is { } shield) drow.Children.Add(new Image { Source = shield, Width = 46, Height = 46, Margin = new Thickness(0, 0, 12, 0) });
        var dtext = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dtext.Children.Add(Ui.Text((o.IsTournament ? $"{(_s.Tournament?.Opponent == o.Id ? _s.Tournament.Round + 1 : 1)}. turdaki rakip: " : "") + Texts.Faction(deck.Faction), 18, Theme.Text, true));
        dtext.Children.Add(Ui.Text($"Lider: {CardCatalog.Default.Get(deck.LeaderId).Name}  ·  {(o.Ai switch { Difficulty.Easy => "Kolay", Difficulty.Hard => "Zor", _ => "Normal" })}", 15, Theme.TextDim));
        drow.Children.Add(dtext);
        sp.Children.Add(drow);

        var rec = _s.Records.TryGetValue(o.Id, out var r) ? r : null;
        if (rec != null && rec.Wins + rec.Losses > 0)
        {
            var rt = Ui.Text($"Karşılaşmalar: {rec.Wins} galibiyet, {rec.Losses} yenilgi", 15, Theme.TextDim);
            rt.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(rt);
        }

        // Ödül
        sp.Children.Add(Ornate.Caption(o.IsTournament ? "Turnuva" : "Ödül", 16));
        if (o.IsTournament)
        {
            sp.Children.Add(Ui.Text($"Katılım {o.EntryFee} Crown  ·  {o.Rounds} maç, kaybeden elenir.", 17, Theme.Text, false, TextAlignment.Left, true));
            sp.Children.Add(Ui.Text($"Şampiyona {(rec?.Wins > 0 ? o.Prize / 2 : o.Prize)} Crown ve rakiplerin benzersiz kartlarından biri.", 17, Theme.B("#f0d890"), true, TextAlignment.Left, true));
        }
        else
        {
            var first = rec == null || rec.Wins == 0;
            var reward = NpcDecks.Reward(o.Decks[0]);
            var rw = new StackPanel { Orientation = Orientation.Horizontal };
            if (first && reward != null && CardCatalog.Default.TryGet(reward, out var rd))
            {
                var card = new GwintCard(rd, 92, rd.IsUnit ? rd.Strength : null) { Cursor = Cursors.Arrow, Margin = new Thickness(0, 0, 14, 0) };
                rw.Children.Add(card);
            }
            var rt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = first && reward != null ? 250 : 360 };
            if (first)
            {
                rt.Children.Add(Ui.Text($"+{o.Prize} Crown", 22, Theme.B("#f0d890"), true));
                if (reward != null && CardCatalog.Default.TryGet(reward, out var rd2))
                    rt.Children.Add(Ui.Text((_s.Owned(reward) > 0 ? "Benzersiz kart (zaten sende): " : "Benzersiz kart: ") + rd2.Name, 16, Theme.Text, false, TextAlignment.Left, true));
                rt.Children.Add(Ui.Text("ve destesinden rastgele bir kart.", 15, Theme.TextDim, false, TextAlignment.Left, true));
            }
            else
            {
                rt.Children.Add(Ui.Text($"Yeniden yenersen +{Math.Max(5, o.Prize / 3)} Crown, bazen bir kart.", 17, Theme.Text, false, TextAlignment.Left, true));
            }
            rw.Children.Add(rt);
            sp.Children.Add(rw);
        }

        // Oyna
        var problem = CareerLogic.DeckProblem(_s);
        var running = o.IsTournament && _s.Tournament?.Opponent == o.Id;
        var otherRun = o.IsTournament && _s.Tournament != null && !running;
        if (problem != null)
        {
            var p = Ui.Text("Desten oynamaya hazır değil: " + problem, 15, Theme.Bad, true, TextAlignment.Left, true);
            p.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(p);
        }
        if (otherRun)
        {
            var p = Ui.Text("Önce sürmekte olan turnuvayı bitir.", 15, Theme.Bad, true, TextAlignment.Left, true);
            p.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(p);
        }
        var confirming = o.IsTournament && !running && _confirmEntry == o.Id;
        if (o.IsTournament && !running)
        {
            // Girişten önce uyarı: deste turnuva boyunca kilitlenir, turnuva bitene dek başka maç yapılmaz
            var myDeck = _s.ActiveDeck();
            var warn = new Border
            {
                Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(14, 10, 12, 12), Background = Theme.B("#30b8604f"),
                BorderBrush = Theme.B("#b8604f"), BorderThickness = new Thickness(3, 0, 0, 0),
                Child = Ui.Text("Girdikten sonra desten kilitlenir: turnuva boyunca değiştirilemez" + (myDeck != null ? $" ({Texts.FactionShort(myDeck.Faction)}, {myDeck.CardIds.Count} kart)" : "")
                    + ". Turnuva bitene dek haritaya dönülmez ve başka maç yapılmaz; maçtan çıkmak elenmek sayılır.", 15, Theme.B("#f0d0c0"), false, TextAlignment.Left, true)
            };
            sp.Children.Add(warn);
        }
        var label = !o.IsTournament ? "Masaya otur" : running ? $"{_s.Tournament!.Round + 1}. turu oyna"
            : confirming ? $"Evet, katıl ({o.EntryFee} Crown)" : $"Turnuvaya katıl ({o.EntryFee} Crown)";
        var play = Ui.Button(label, () => Play(o), primary: true, enabled: problem == null && !otherRun);
        play.FontSize = 24;
        play.Padding = new Thickness(30, 12, 30, 12);
        play.Margin = new Thickness(0, 20, 0, 0);
        play.HorizontalAlignment = HorizontalAlignment.Stretch;
        play.Tag = confirming ? "career-play-confirm" : "career-play";
        sp.Children.Add(play);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        if (problem != null)
        {
            var fix = Ui.Button("Desteni düzenle", _nav.OpenDeck);
            fix.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(fix);
        }
        if (running)
        {
            var quit = Ui.Button("Turnuvadan çekil", () =>
            {
                if (MessageBox.Show("Turnuvadan çekilirsen katılım ücreti geri gelmez. Emin misin?", "Gwent", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                CareerLogic.AbandonTournament(_s);
                CareerStore.Save(_s);
                Draw();
            });
            quit.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(quit);
        }
        var close = Ui.Button(confirming ? "Vazgeç" : "Kapat", () => { _confirmEntry = null; _selected = null; Draw(); });
        close.Tag = "career-detail-close";
        row.Children.Add(close);
        sp.Children.Add(row);
        if (_error != null)
        {
            var e = Ui.Text(_error, 15, Theme.Bad, true, TextAlignment.Left, true);
            e.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(e);
            _error = null;
        }

        var panel = Ornate.Panel(new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(24, 24, 20, 22), 16, 44, Theme.B("#f4140e08"));
        panel.Tag = "career-detail";
        Ornate.Reveal(new UIElement[] { panel }, 0, 0, 0, 260);
        return panel;
    }

    private string? _error;
    private string? _confirmEntry;

    private void Play(CareerOpponent o)
    {
        if (o.IsTournament && _s.Tournament == null && _confirmEntry != o.Id)
        {
            _confirmEntry = o.Id;   // ilk tıklama: uyarıyı onaylat
            Draw();
            return;
        }
        _confirmEntry = null;
        if (o.IsTournament)
        {
            var err = CareerLogic.EnterTournament(_s, o, Random.Shared);
            if (err != null) { _error = err; Draw(); return; }
            CareerStore.Save(_s);
        }
        _nav.Play(o);
    }

    private static FrameworkElement Pic(string name, double w)
    {
        if (Assets.Ui(name) is { } src) return new Image { Source = src, Width = w, Stretch = Stretch.Uniform };
        return new Border { Width = w, Height = w };
    }

    // ---------- Ödül gösterimi ----------

    private UIElement RewardOverlay(CareerReward r)
    {
        var veil = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xc8, 0, 0, 0)), Tag = "career-reward-overlay" };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(Ornate.Heading(r.TournamentWon ? "Şampiyon" : r.Cards.Count > 0 ? "Yeni kartlar" : "Turnuva bitti", null, 44, HorizontalAlignment.Center));
        sp.Children.Add(Ornate.Rule(520, 12));
        if (r.Cards.Count > 0)
        {
            var cards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            var items = new List<UIElement>();
            foreach (var id in r.Cards)
            {
                var d = CardCatalog.Default.Get(id);
                var col = new StackPanel { Margin = new Thickness(18, 0, 18, 0) };
                var card = new GwintCard(d, 200, d.IsUnit ? d.Strength : null, height: 200 * GwintCard.Aspect) { Cursor = Cursors.Arrow };
                card.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 100), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.7 };
                col.Children.Add(card);
                var n = Ui.Text(d.Name, 20, d.Hero ? Theme.B("#f0d890") : Theme.Text, true, TextAlignment.Center, true);
                n.Width = 220;
                n.Margin = new Thickness(0, 12, 0, 0);
                col.Children.Add(n);
                cards.Children.Add(col);
                items.Add(col);
            }
            sp.Children.Add(cards);
            Ornate.Reveal(items, 40, 200, 260, 600);
        }
        var sum = Ui.Text(r.Summary, 22, Theme.B("#f0d890"), true, TextAlignment.Center, true);
        sum.MaxWidth = 900;
        sum.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(sum);
        var ok = Ui.Button("Devam", () => Children.Remove(veil), primary: true);
        ok.FontSize = 22;
        ok.Padding = new Thickness(46, 10, 46, 10);
        ok.Margin = new Thickness(0, 24, 0, 0);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Tag = "career-reward-close";
        sp.Children.Add(ok);
        var panel = Ornate.Panel(sp, new Thickness(60, 40, 60, 40));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        veil.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = panel, Margin = new Thickness(30) });
        if (r.Cards.Count > 0) Sfx.Play("lock_in");
        return veil;
    }
}
