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
    private readonly LoadoutPicker _picker;
    private string _error = "";
    private bool _leaving;

    public LobbyView(OnlineController controller, Func<Task> leave)
    {
        _c = controller;
        _leave = leave;
        Children.Add(Ornate.Stage(_host));

        _picker = new LoadoutPicker(() => NextGen);
        _picker.Changed += SendLoadout;
        _c.Link.StateChanged += OnState;
        Unloaded += (_, _) => _c.Link.StateChanged -= OnState;
        Rebuild();
        SendLoadout(); // hazır olunabilsin diye varsayılan seçimi hemen gönder
    }

    private void OnState() => Rebuild();

    private RoomState? State => _c.Link.State;
    private bool NextGen => State?.NextGen == true;

    // ---------- Gönderim ----------

    private async void SendLoadout()
    {
        var r = await _c.Link.SetLoadoutAsync(_picker.ToLoadout());
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

        var code = LobbyParts.CodePanel(st?.Code, _c, "ODA KODU");
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
    internal static UIElement VersusMark()
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
    internal static UIElement Seal(bool ready, bool stampNow, double size = 72)
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
    internal static UIElement SeatHeader(string label, string nick, UIElement seal, string? avatar)
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

        (_picker.Parent as Panel)?.Children.Remove(_picker);   // önceki çizimin kökünden ayır
        _picker.Build();
        root.Children.Add(_picker);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(Ornate.Spaced(t), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, 14, 0, 8);
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
