using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;
using GwentNet;

namespace GwentClient;

/// <summary>
/// Online bekleme odası: oda kodu, iki oyuncu, benim fraksiyon/lider/deste seçimim ve "hazırım". İkisi de hazır olunca sunucu maçı
/// başlatır; ekran geçişini <c>MainWindow</c> yapar.
/// </summary>
public sealed class LobbyView : Grid
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private readonly OnlineController _c;
    private readonly Func<Task> _leave;
    private readonly Border _host = new();
    private bool _revealed;
    private bool _meReadyShown, _oppReadyShown;
    private string _faction = "northern_realms";
    private string _leaderId = "";
    private string? _deckName; // null = önerilen deste
    private string _error = "";
    private bool _leaving;

    public LobbyView(OnlineController controller, Func<Task> leave)
    {
        _c = controller;
        _leave = leave;
        Children.Add(Ornate.Stage(_host));

        _leaderId = DefaultLeader(_faction);
        _c.Link.StateChanged += OnState;
        Unloaded += (_, _) => _c.Link.StateChanged -= OnState;
        Rebuild();
        SendLoadout(); // hazır olunabilsin diye varsayılan seçimi hemen gönder
    }

    private void OnState() => Rebuild();

    private RoomState? State => _c.Link.State;
    private bool NextGen => State?.NextGen == true;

    private string DefaultLeader(string faction) =>
        CardCatalog.Default.Leaders(faction).First(l => l.Edition != "nextgen" || NextGen).Id;

    // ---------- Gönderim ----------

    private async void SendLoadout()
    {
        List<string>? cards = null;
        var leader = _leaderId;
        if (_deckName != null)
        {
            var saved = DeckStore.For(_faction).FirstOrDefault(d => d.Name == _deckName);
            if (saved != null) { cards = saved.CardIds.ToList(); leader = saved.LeaderId; }
        }
        var r = await _c.Link.SetLoadoutAsync(new LoadoutDto(_faction, leader, cards, Settings.Portrait));
        _error = r.Ok ? "" : r.Error ?? "Seçim gönderilemedi.";
        Rebuild();
    }

    private async void ToggleReady(bool ready)
    {
        var r = await _c.Link.SetReadyAsync(ready);
        if (!r.Ok) { _error = r.Error ?? ""; Rebuild(); }
    }

    private async void LeaveClicked()
    {
        if (_leaving) return;
        _leaving = true;
        await _leave();
    }

    // ---------- Çizim ----------

    private PlayerInfo? Me => State?.Players.FirstOrDefault(p => p.Seat == State.YourSeat);
    private PlayerInfo? Opp => State?.Players.FirstOrDefault(p => p.Seat != State.YourSeat);

    private void Rebuild()
    {
        var st = State;
        var grid = new Grid { Margin = new Thickness(70, 46, 70, 46) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- Üst: başlık + oda kodu (taş levhalar) ---
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ornate.Heading("Bekleme odası", NextGen ? "Yeni nesil içerik açık" : "Standart kartlar", 46));

        var code = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Tag = "lobby-code" };
        var codeLabel = Ui.Text("ODA KODU", 13, Theme.TextDim, true);
        codeLabel.VerticalAlignment = VerticalAlignment.Center;
        codeLabel.Margin = new Thickness(0, 0, 16, 0);
        codeRow.Children.Add(codeLabel);
        foreach (var ch in st?.Code ?? "…")
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
            try { Clipboard.SetText(st?.Code ?? ""); } catch { /* pano başka uygulamada kilitli olabilir */ }
        });
        copy.VerticalAlignment = VerticalAlignment.Center;
        copy.Margin = new Thickness(16, 0, 0, 0);
        copy.FontSize = 14;
        codeRow.Children.Add(copy);
        code.Children.Add(codeRow);
        if (_c.IsHost) AddInvites(code);
        Grid.SetColumn(code, 1);
        head.Children.Add(code);
        grid.Children.Add(head);

        // --- Orta: ben | karşı karşıya | rakip ---
        var body = new Grid { Margin = new Thickness(0, 26, 0, 22) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) });
        var mine = Ornate.Panel(MyPanel(), new Thickness(40, 30, 34, 30), 20, 58);
        body.Children.Add(mine);
        var vs = VersusMark();
        Grid.SetColumn(vs, 1);
        body.Children.Add(vs);
        var theirs = Ornate.Panel(OppPanel(), new Thickness(36, 30, 36, 30), 20, 58);
        Grid.SetColumn(theirs, 2);
        body.Children.Add(theirs);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // --- Alt: hazır / ayrıl ---
        var foot = new StackPanel { Orientation = Orientation.Horizontal };
        var ready = Me?.Ready == true;
        var readyBtn = Ui.Button(ready ? "Hazır değilim" : "Hazırım", () => ToggleReady(!ready), primary: !ready, enabled: Me?.Loadout != null);
        readyBtn.FontSize = 26;
        readyBtn.Padding = new Thickness(64, 12, 64, 12);
        readyBtn.Tag = "lobby-ready";
        foot.Children.Add(readyBtn);
        var leaveBtn = Ui.Button("Odadan ayrıl", LeaveClicked);
        leaveBtn.FontSize = 20;
        leaveBtn.Padding = new Thickness(30, 12, 30, 12);
        leaveBtn.Margin = new Thickness(16, 0, 0, 0);
        foot.Children.Add(leaveBtn);
        var msg = Ui.Text(_error != "" ? _error : FootMessage(), 18, _error != "" ? Theme.Bad : Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
        msg.VerticalAlignment = VerticalAlignment.Center;
        msg.Margin = new Thickness(26, 0, 0, 0);
        msg.MaxWidth = 760;
        foot.Children.Add(msg);
        Grid.SetRow(foot, 2);
        grid.Children.Add(foot);

        _host.Child = grid;
        if (!_revealed)
        {
            _revealed = true;
            Ornate.Reveal(new UIElement[] { head, mine, vs, theirs, foot }, 30, 60, 130, 620);
        }
    }

    /// <summary>İki koltuk arasındaki dikey altın çizgi ve "VS" mührü.</summary>
    private static UIElement VersusMark()
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        g.Children.Add(new Rectangle
        {
            Width = 1.5, HorizontalAlignment = HorizontalAlignment.Center,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 0), new GradientStop(Color.FromRgb(0xc9, 0xa2, 0x4a), 0.5), new GradientStop(Color.FromArgb(0, 0xc9, 0xa2, 0x4a), 1)
            }, 90)
        });
        var seal = new Grid { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Center };
        seal.Children.Add(new ChamferFrame { Fill = Theme.B("#160f08"), Edge = Ornate.GoldEdge, Inner = Ornate.GoldThin, Cut = 18, Inset = 4 });
        var t = Ui.Text("VS", 26, Theme.B("#f0d890"), true, TextAlignment.Center);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 160, 50), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 };
        seal.Children.Add(t);
        g.Children.Add(seal);
        return g;
    }

    /// <summary>Hazır mührü: hazırsa mum kırmızısı, "damgalanarak" belirir; değilse boş halka.</summary>
    private static UIElement Seal(bool ready, bool stampNow, double size = 72)
    {
        var g = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        g.Children.Add(new Ellipse
        {
            Fill = ready ? new RadialGradientBrush(Color.FromRgb(0xd0, 0x4a, 0x38), Color.FromRgb(0x6a, 0x14, 0x0e)) { GradientOrigin = new Point(0.35, 0.3) } : Theme.B("#14100a"),
            Stroke = ready ? Theme.B("#f0a898") : Theme.B("#5a4a2a"), StrokeThickness = ready ? 2 : 1.5, StrokeDashArray = ready ? null : new DoubleCollection { 3, 3 }
        });
        var t = Ui.Text(ready ? "✓" : "…", ready ? 34 : 26, ready ? Theme.B("#ffe2d8") : Theme.TextDim, true, TextAlignment.Center);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        if (ready)
        {
            g.Effect = new DropShadowEffect { Color = Color.FromRgb(210, 70, 50), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 };
            if (stampNow && Ornate.Animate)
            {
                var sc = new ScaleTransform(1, 1);
                g.RenderTransform = sc;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(2.0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new System.Windows.Media.Animation.BounceEase { Bounces = 1, Bounciness = 2.5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                Sfx.Play("lock_in");
            }
        }
        return g;
    }

    /// <summary>Host için davet kodları: en iyisi büyük ve kopyalanabilir; diğer yöntemler altında. Başarısız yöntemlerin nedeni silik yazılır.</summary>
    private void AddInvites(StackPanel code)
    {
        void CopyButton(string text, StackPanel row)
        {
            var b = Ui.Button("Kopyala", () => { try { Clipboard.SetText(text); } catch { /* pano kilitli olabilir */ } });
            b.FontSize = 13;
            b.Margin = new Thickness(10, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(b);
        }

        var invites = _c.Invites;
        if (invites.Count == 0)
        {
            code.Children.Add(Ui.Text("Bağlantı yöntemi seçilmedi: arkadaşın bu bilgisayara bağlanamaz.", 14, Theme.Bad, false, TextAlignment.Right));
        }
        else
        {
            var best = invites[0];
            var title = Ui.Text("Davet kodu (arkadaşın Online → katıl alanına yapıştırır) — " + best.Label, 14, Theme.B("#c9c0aa"), true, TextAlignment.Right);
            title.Margin = new Thickness(0, 10, 0, 3);
            code.Children.Add(title);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var box = new TextBox { Text = best.Invite, IsReadOnly = true, Width = 640, TextWrapping = TextWrapping.Wrap, Tag = "lobby-invite", FontFamily = Theme.Serif, FontSize = 14 };
            box.Style = (Style)Application.Current.FindResource("GText");
            row.Children.Add(box);
            CopyButton(best.Invite, row);
            code.Children.Add(row);
            foreach (var other in invites.Skip(1))
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
                r.Children.Add(Ui.Text(other.Label, 13, Theme.TextDim));
                CopyButton(other.Invite, r);
                code.Children.Add(r);
            }
        }
        foreach (var w in _c.Warnings)
        {
            var t = Ui.Text(w, 12, Theme.B("#a8837a"), false, TextAlignment.Right, true);
            t.MaxWidth = 640;
            t.Margin = new Thickness(0, 4, 0, 0);
            code.Children.Add(t);
        }
    }

    private string FootMessage()
    {
        if (Opp == null) return "Rakip bekleniyor… Oda kodunu arkadaşına ver.";
        if (!Opp.Connected) return "Rakibin bağlantısı koptu.";
        if (Me?.Ready == true && Opp.Ready != true) return "Rakibin hazır olması bekleniyor…";
        if (Me?.Ready != true && Opp.Ready) return "Rakip hazır. Seçimini yapıp hazır ol.";
        return "Seçimini yap ve hazır ol. İkiniz de hazır olunca maç başlar.";
    }

    private static string BannerFor(string faction) => Assets.Ui("banner_" + faction) != null ? "banner_" + faction : "banner_neutral";

    /// <summary>Koltuk başlığı: küçük etiket + büyük takma ad + sağda mühür.</summary>
    private static UIElement SeatHeader(string label, string nick, UIElement seal, string? avatar)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var medal = Portraits.Medallion(avatar, 80, glow: true);
        medal.Margin = new Thickness(0, 0, 14, 0);
        g.Children.Add(medal);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sp, 1);
        sp.Children.Add(Ui.Text(Ornate.Spaced(label), 13, Theme.B("#a8905a"), true));
        var n = Ui.Text(nick, 36, Theme.B("#f0d890"), true);
        n.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.9 };
        sp.Children.Add(n);
        g.Children.Add(sp);
        Grid.SetColumn(seal, 2);
        g.Children.Add(seal);
        return g;
    }

    private UIElement MyPanel()
    {
        var meReady = Me?.Ready == true;
        var stamp = meReady && !_meReadyShown;
        _meReadyShown = meReady;
        var root = new StackPanel();
        root.Children.Add(SeatHeader("SEN", Me?.Nick ?? "", Seal(meReady, stamp), Settings.Portrait));
        var rule = Ornate.Rule(560, 10);
        rule.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(rule);

        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Fraksiyon: tavandan sarkan flamalar (seçili olan aşağı iner ve parlar)
        var rack = new Grid { Margin = new Thickness(0, 0, 38, 0), VerticalAlignment = VerticalAlignment.Top };
        rack.Children.Add(new Rectangle { Height = 5, VerticalAlignment = VerticalAlignment.Top, Fill = Ornate.GoldEdge, Margin = new Thickness(-8, 0, -8, 0), RadiusX = 2, RadiusY = 2 });
        var pennants = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        foreach (var f in Factions)
        {
            var key = f;
            var sel = f == _faction;
            var inner = new StackPanel { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
            var img = Assets.Ui(BannerFor(f));
            if (img != null) inner.Children.Add(new System.Windows.Controls.Image { Source = img, Width = 56, Height = 330, Stretch = Stretch.Fill, Opacity = sel ? 1 : 0.55 });
            else inner.Children.Add(new Rectangle { Width = 56, Height = 330, Fill = Theme.FactionColor(f), Opacity = sel ? 1 : 0.55 });
            var tn = Ui.Text(Texts.FactionShort(f), 14, sel ? Theme.B("#f0d890") : Theme.B("#8f8670"), true, TextAlignment.Center);
            tn.Margin = new Thickness(0, 6, 0, 0);
            inner.Children.Add(tn);
            if (sel) inner.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.7 };
            var b = new Border
            {
                Child = inner, Margin = new Thickness(5, sel ? 14 : 0, 5, 0), Padding = new Thickness(4, 0, 4, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Tag = "lobby-faction:" + f, ToolTip = Texts.Faction(f)
            };
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (key == _faction) return;
                Sfx.Play("cursor_movement");
                _faction = key;
                _leaderId = DefaultLeader(key);
                _deckName = null;
                SendLoadout();
            };
            pennants.Children.Add(b);
        }
        rack.Children.Add(pennants);
        cols.Children.Add(rack);

        var right = new StackPanel();
        Grid.SetColumn(right, 1);
        cols.Children.Add(right);

        // Deste
        right.Children.Add(Label("DESTE"));
        var decks = new WrapPanel();
        void AddDeck(string? name, string title, string sub, bool enabled = true)
        {
            var c = new StackPanel();
            c.Children.Add(Ui.Text(title, 15, enabled ? Theme.Text : Theme.TextDim, true));
            c.Children.Add(Ui.Text(sub, 12, Theme.TextDim));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = _deckName == name, IsEnabled = enabled,
                Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(16, 8, 16, 8), MinWidth = 180, Tag = "lobby-deck:" + (name ?? "@rec")
            };
            tb.Click += (_, _) => { _deckName = name; SendLoadout(); };
            decks.Children.Add(tb);
        }
        AddDeck(null, "Önerilen deste", "kahramanlar + en güçlü birimler");
        foreach (var d in DeckStore.For(_faction))
        {
            var errs = DeckValidator.Validate(d.ToDeck(), new GameOptions { NextGen = NextGen });
            AddDeck(d.Name, d.Name, errs.Count == 0 ? $"{d.CardIds.Count} kart" : "geçersiz", errs.Count == 0);
        }
        right.Children.Add(decks);

        // Lider (kayıtlı deste seçiliyse destenin lideri kullanılır): kart şeridi
        if (_deckName == null)
        {
            right.Children.Add(Label("LİDER"));
            var strip = new WrapPanel();
            CardDef? chosen = null;
            foreach (var l in CardCatalog.Default.Leaders(_faction).Where(l => l.Edition != "nextgen" || NextGen))
            {
                var leader = l;
                var sel = leader.Id == _leaderId;
                if (sel) chosen = leader;
                var c = new StackPanel { Width = 100 };
                c.Children.Add(new GwintCard(leader, 100, null, null, height: 100 * GwintCard.Aspect) { IsHitTestVisible = false, Opacity = sel ? 1 : 0.7 });
                var nm = Ui.Text(leader.Name.Contains(':') ? leader.Name[(leader.Name.IndexOf(':') + 1)..].Trim() : leader.Name, 12, sel ? Theme.B("#f0d890") : Theme.TextDim, true, TextAlignment.Center, true);
                nm.Margin = new Thickness(0, 5, 0, 0);
                c.Children.Add(nm);
                var tb = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = sel,
                    Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(6, 6, 6, 6), Tag = "lobby-leader:" + leader.Id, ToolTip = leader.Name
                };
                tb.Click += (_, _) => { _leaderId = leader.Id; SendLoadout(); };
                strip.Children.Add(tb);
            }
            right.Children.Add(strip);
            if (chosen != null)
            {
                var d = Ui.Text(Texts.LeaderText(chosen), 15, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
                d.Margin = new Thickness(2, 4, 0, 0);
                d.FontStyle = FontStyles.Italic;
                right.Children.Add(d);
            }
        }
        root.Children.Add(cols);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(Ornate.Spaced(t), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, 4, 0, 8);
        return l;
    }

    private UIElement OppPanel()
    {
        var oppReady = Opp?.Ready == true;
        var stamp = oppReady && !_oppReadyShown;
        _oppReadyShown = oppReady;
        var sp = new StackPanel();
        sp.Children.Add(SeatHeader("RAKİP", Opp?.Nick ?? "—", Seal(oppReady, stamp), Opp?.Loadout?.Avatar ?? Opp?.Loadout?.LeaderId));
        sp.Children.Add(Ornate.Rule(430, 10));
        if (Opp == null)
        {
            var w = Ui.Text("Rakip bekleniyor…", 28, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
            w.Margin = new Thickness(0, 24, 0, 0);
            w.FontStyle = FontStyles.Italic;
            sp.Children.Add(w);
            sp.Children.Add(Ui.Text("Oda kodunu paylaş; katılınca burada belirir.", 16, Theme.TextDim, false, TextAlignment.Left, true));
            return sp;
        }
        sp.Children.Add(Ui.Text(Opp.Connected ? "● bağlı" : "● bağlantısı koptu", 15, Opp.Connected ? Theme.Good : Theme.Bad));

        if (Opp.Loadout is { } lo)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var shield = Assets.Ui("shield_" + lo.Faction);
            if (shield != null) row.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 52, Height = 52, Margin = new Thickness(0, 0, 12, 0) });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(Texts.Faction(lo.Faction), 22, Theme.Text, true));
            info.Children.Add(Ui.Text(CardCatalog.Default.Get(lo.LeaderId).Name, 15, Theme.TextDim));
            row.Children.Add(info);
            sp.Children.Add(row);
            var leaderCard = new GwintCard(CardCatalog.Default.Get(lo.LeaderId), 250, null, null, height: 250 * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
            leaderCard.Margin = new Thickness(0, 18, 0, 0);
            leaderCard.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 5, Opacity = 0.9 };
            sp.Children.Add(leaderCard);
        }
        else sp.Children.Add(Ui.Text("Henüz seçim yapmadı.", 18, Theme.TextDim, false, TextAlignment.Left, true));
        return sp;
    }
}
