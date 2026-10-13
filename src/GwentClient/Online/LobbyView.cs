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
    private string _faction = "northern_realms";
    private string _leaderId = "";
    private string? _deckName; // null = önerilen deste
    private string _error = "";
    private bool _leaving;

    public LobbyView(OnlineController controller, Func<Task> leave)
    {
        _c = controller;
        _leave = leave;
        Background = Theme.Bg;
        ClipToBounds = true;

        var pano = Assets.Ui("pano_kaer_morhen");
        if (pano != null) Children.Add(new System.Windows.Controls.Image { Source = pano, Stretch = Stretch.UniformToFill, IsHitTestVisible = false });
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.9,
                GradientStops = { new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xc0, 0, 0, 0), 1) }
            }
        });
        Children.Add(_host);

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
        var r = await _c.Link.SetLoadoutAsync(new LoadoutDto(_faction, leader, cards));
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
        var grid = new Grid { Margin = new Thickness(70, 36, 70, 36) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- Üst: başlık + oda kodu ---
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        var t = Ui.Text("Bekleme odası", 44, Theme.B("#e8c970"), true);
        t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.95 };
        title.Children.Add(t);
        title.Children.Add(Ui.Text(NextGen ? "Yeni nesil içerik açık" : "Standart kartlar", 16, Theme.TextDim));
        head.Children.Add(title);

        var code = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        code.Children.Add(Ui.Text("Oda kodu", 14, Theme.TextDim, true, TextAlignment.Right));
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var codeText = Ui.Text(st?.Code ?? "…", 56, Theme.B("#f0d890"), true, TextAlignment.Right);
        codeText.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 170, 60), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.6 };
        codeText.Tag = "lobby-code";
        codeRow.Children.Add(codeText);
        var copy = Ui.Button("Kopyala", () =>
        {
            try { Clipboard.SetText(st?.Code ?? ""); } catch { /* pano başka uygulamada kilitli olabilir */ }
        });
        copy.VerticalAlignment = VerticalAlignment.Center;
        copy.Margin = new Thickness(16, 0, 0, 0);
        copy.FontSize = 14;
        codeRow.Children.Add(copy);
        code.Children.Add(codeRow);
        if (_c.IsHost)
        {
            AddInvites(code);
        }
        Grid.SetColumn(code, 1);
        head.Children.Add(code);
        grid.Children.Add(head);

        // --- Orta: ben / rakip ---
        var body = new Grid { Margin = new Thickness(0, 22, 0, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        var mine = Panel(MyPanel());
        Grid.SetColumn(mine, 0);
        body.Children.Add(mine);
        var theirs = Panel(OppPanel());
        Grid.SetColumn(theirs, 2);
        body.Children.Add(theirs);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // --- Alt: hazır / ayrıl ---
        var foot = new StackPanel { Orientation = Orientation.Horizontal };
        var ready = Me?.Ready == true;
        var readyBtn = Ui.Button(ready ? "Hazır değilim" : "Hazırım", () => ToggleReady(!ready), primary: !ready, enabled: Me?.Loadout != null);
        readyBtn.FontSize = 24;
        readyBtn.Padding = new Thickness(50, 12, 50, 12);
        readyBtn.Tag = "lobby-ready";
        foot.Children.Add(readyBtn);
        var leaveBtn = Ui.Button("Odadan ayrıl", LeaveClicked);
        leaveBtn.FontSize = 20;
        leaveBtn.Padding = new Thickness(30, 12, 30, 12);
        leaveBtn.Margin = new Thickness(16, 0, 0, 0);
        foot.Children.Add(leaveBtn);
        var msg = Ui.Text(_error != "" ? _error : FootMessage(), 18, _error != "" ? Theme.Bad : Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
        msg.VerticalAlignment = VerticalAlignment.Center;
        msg.Margin = new Thickness(24, 0, 0, 0);
        msg.MaxWidth = 700;
        foot.Children.Add(msg);
        Grid.SetRow(foot, 2);
        grid.Children.Add(foot);

        _host.Child = grid;
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
            var title = Ui.Text("Davet kodu — " + best.Label, 14, Theme.B("#c9c0aa"), true, TextAlignment.Right);
            title.Margin = new Thickness(0, 8, 0, 3);
            code.Children.Add(title);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var box = new TextBox { Text = best.Invite, IsReadOnly = true, Width = 560, TextWrapping = TextWrapping.Wrap, Tag = "lobby-invite", FontFamily = Theme.Serif, FontSize = 17 };
            box.Style = (Style)Application.Current.FindResource("GText");
            row.Children.Add(box);
            CopyButton(best.Invite, row);
            code.Children.Add(row);
            code.Children.Add(Ui.Text("Arkadaşın bunu “Online → Odaya katıl”da davet kodu alanına yapıştırır.", 13, Theme.TextDim, false, TextAlignment.Right));
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

    private static Border Panel(UIElement child) => new()
    {
        Child = child, Background = Theme.B("#e6140e08"), BorderBrush = Theme.B("#8a6e2c"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6),
        Padding = new Thickness(26, 20, 20, 20), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.8 }
    };

    private UIElement MyPanel()
    {
        var sp = new StackPanel();
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(Ui.Text("Sen", 20, Theme.TextDim, true));
        var nick = Ui.Text("  " + (Me?.Nick ?? ""), 30, Theme.B("#f0d890"), true);
        nameRow.Children.Add(nick);
        sp.Children.Add(nameRow);

        // fraksiyon
        sp.Children.Add(Label("Fraksiyon"));
        var fr = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var f in Factions)
        {
            var key = f;
            var sel = f == _faction;
            var inner = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
            var shield = Assets.Ui("shield_" + f);
            if (shield != null) inner.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 40, Height = 40, Margin = new Thickness(0, 0, 8, 0) });
            var tn = Ui.Text(Texts.FactionShort(f), 17, sel ? Theme.B("#f0d890") : Theme.B("#c9c0aa"), true);
            tn.VerticalAlignment = VerticalAlignment.Center;
            inner.Children.Add(tn);
            var b = new Border
            {
                Child = inner, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 6, 14, 6), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5), BorderBrush = sel ? Theme.B("#c9a24a") : Brushes.Transparent,
                Background = sel ? Theme.B("#55c9a24a") : Theme.B("#33000000"), Tag = "lobby-faction:" + f
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
            fr.Children.Add(b);
        }
        var frScroll = new WrapPanel();
        foreach (UIElement e in fr.Children.Cast<UIElement>().ToList()) { fr.Children.Remove(e); frScroll.Children.Add(e); }
        sp.Children.Add(frScroll);

        // deste
        sp.Children.Add(Label("Deste"));
        var decks = new WrapPanel();
        void AddDeck(string? name, string title, string sub, bool enabled = true)
        {
            var c = new StackPanel();
            c.Children.Add(Ui.Text(title, 15, enabled ? Theme.Text : Theme.TextDim, true));
            c.Children.Add(Ui.Text(sub, 12, Theme.TextDim));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = _deckName == name, IsEnabled = enabled,
                Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), MinWidth = 170, Tag = "lobby-deck:" + (name ?? "@rec")
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
        sp.Children.Add(decks);

        // lider (kayıtlı deste seçiliyse destenin lideri kullanılır)
        if (_deckName == null)
        {
            sp.Children.Add(Label("Lider"));
            var leaders = new StackPanel();
            foreach (var l in CardCatalog.Default.Leaders(_faction).Where(l => l.Edition != "nextgen" || NextGen))
            {
                var leader = l;
                var c = new StackPanel();
                c.Children.Add(Ui.Text(leader.Name, 15, Theme.Text, true));
                c.Children.Add(Ui.Text(Texts.LeaderText(leader), 13, Theme.TextDim, false, TextAlignment.Left, true));
                var tb = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = leader.Id == _leaderId,
                    Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(10, 5, 10, 5), Tag = "lobby-leader:" + leader.Id
                };
                tb.Click += (_, _) => { _leaderId = leader.Id; SendLoadout(); };
                leaders.Children.Add(tb);
            }
            sp.Children.Add(leaders);
        }
        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(t, 14, Theme.TextDim, true);
        l.Margin = new Thickness(0, 16, 0, 5);
        return l;
    }

    private UIElement OppPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Rakip", 20, Theme.TextDim, true));
        if (Opp == null)
        {
            var w = Ui.Text("Rakip bekleniyor…", 28, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
            w.Margin = new Thickness(0, 16, 0, 0);
            w.FontStyle = FontStyles.Italic;
            sp.Children.Add(w);
            sp.Children.Add(Ui.Text("Oda kodunu paylaş; katılınca burada görünür.", 16, Theme.TextDim, false, TextAlignment.Left, true));
            return sp;
        }
        var nick = Ui.Text(Opp.Nick, 34, Theme.B("#f0d890"), true);
        nick.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(nick);
        sp.Children.Add(Ui.Text(Opp.Connected ? "bağlı" : "bağlantısı koptu", 16, Opp.Connected ? Theme.Good : Theme.Bad));

        if (Opp.Loadout is { } lo)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var shield = Assets.Ui("shield_" + lo.Faction);
            if (shield != null) row.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 56, Height = 56, Margin = new Thickness(0, 0, 12, 0) });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(Texts.Faction(lo.Faction), 22, Theme.Text, true));
            info.Children.Add(Ui.Text(CardCatalog.Default.Get(lo.LeaderId).Name, 16, Theme.TextDim));
            row.Children.Add(info);
            sp.Children.Add(row);
            var leaderCard = new GwintCard(CardCatalog.Default.Get(lo.LeaderId), 180, null, null, height: 180 * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
            leaderCard.Margin = new Thickness(0, 18, 0, 0);
            leaderCard.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.9 };
            sp.Children.Add(leaderCard);
        }
        else sp.Children.Add(Ui.Text("Henüz seçim yapmadı.", 18, Theme.TextDim, false, TextAlignment.Left, true));

        var rd = Ui.Text(Opp.Ready ? "✓ Hazır" : "Hazır değil", 24, Opp.Ready ? Theme.B("#d8bd78") : Theme.TextDim, true);
        rd.Margin = new Thickness(0, 18, 0, 0);
        sp.Children.Add(rd);
        return sp;
    }
}
