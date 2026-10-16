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

    private bool _wasDraft;
    private string? _rulesSeen;
    private string _notice = "";

    /// <summary>Kuralların imzası (draft + lanetler): değişince oyuncuya söylenir, çünkü sunucu iki tarafın hazırlığını sıfırlar.</summary>
    private string RulesKey => (Draft ? "D|" : "-|") + string.Join(",", State?.Curses ?? new List<string>());
    private bool Mirror => State?.Curses?.Contains(GwentAI.Curses.Mirror) == true;

    private void OnState()
    {
        _busy = false;   // sunucu yeni durumu gönderdi: seçim işlendi
        if (State?.Phase == RoomPhase.Lobby)
        {
            if (_rulesSeen != null && _rulesSeen != RulesKey)
                _notice = State.YourSeat == 0 ? "Kurallar değişti: ikinizin de hazırlığı sıfırlandı."
                    : "Oda kuran maç kurallarını değiştirdi. Yukarıdan bak, uygunsa yeniden Hazırım'a bas.";
            _rulesSeen = RulesKey;
        }
        if (_wasDraft && !Draft) { _wasDraft = false; SendLoadout(); }   // draft kapandı: deste seçicinin seçimi geri gönderilir
        _wasDraft = Draft;
        Rebuild();
    }

    private RoomState? State => _c.Link.State;
    private bool NextGen => State?.NextGen == true;
    private bool Draft => State?.Draft == true;
    private bool _busy;

    // ---------- Gönderim ----------

    private async void SendLoadout()
    {
        if (Draft) return;   // draft odasında deste draft ile kurulur
        var r = await _c.Link.SetLoadoutAsync(_picker.ToLoadout());
        _error = r.Ok ? "" : r.Error ?? "Seçim gönderilemedi.";
        Rebuild();
    }

    private async void ToggleReady(bool ready)
    {
        _notice = "";
        var r = await _c.Link.SetReadyAsync(ready);
        if (!r.Ok) { _error = r.Error ?? ""; Rebuild(); }
    }

    /// <summary>Oda kuran: draft maçını aç/kapat (açılınca iki oyuncu da desteyi draft ile kurar).</summary>
    private async void SetDraft(bool on)
    {
        var r = await _c.Link.SetDraftAsync(on);
        _error = r.Ok ? "" : r.Error ?? "";
        if (r.Ok && !on) SendLoadout();   // draft kapandı: deste seçicinin seçimi geri gönderilir
        Rebuild();
    }

    /// <summary>Oda kuran: bir laneti aç/kapat (sunucu doğrular; değişince iki tarafın hazırlığı sıfırlanır).</summary>
    private async void ToggleCurse(string id, bool on)
    {
        var set = (State?.Curses ?? new List<string>()).ToHashSet();
        if (on) set.Add(id); else set.Remove(id);
        var r = await _c.Link.SetCursesAsync(set.ToList());
        _error = r.Ok ? "" : r.Error ?? "";
        Rebuild();
    }

    private const string DraftText = "İkiniz de desteyi 25 kez 3 karttan birini seçerek kurarsınız.";

    /// <summary>
    /// Maç kuralları tek şeritte: Draft + Gaunter O'Dimm'in lanetleri. İki tarafta da aynı yerleşim (konuk neyin açık olduğunu aynı
    /// düğmelerden okur), yalnız oda kuran tıklayabilir. Açıklamalar düğmenin ipucunda; altta seçilenlerin tek satırlık özeti.
    /// </summary>
    private UIElement RulesBar(RoomState st)
    {
        var host = st.YourSeat == 0;
        var active = st.Curses ?? new List<string>();
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Ui.Text(Ornate.Spaced("MAÇ KURALLARI"), 12, Theme.B("#a8905a"), true));
        head.Children.Add(Ui.Text(host ? "   seçmek için tıkla · ayrıntı için üzerine gel" : "   oda kuran seçer · ayrıntı için üzerine gel", 12, Theme.TextDim));
        sp.Children.Add(head);

        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(RuleChip("Draft maçı", DraftText, Draft, host, "lobby-draft", on => SetDraft(on)));
        row.Children.Add(new Rectangle { Width = 1, Height = 26, Fill = Theme.B("#5a4a2a"), Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        foreach (var c in GwentAI.Curses.All)
        {
            var id = c.Id;
            row.Children.Add(RuleChip(c.Name, GwentAI.Curses.TextFor(id, true), active.Contains(id), host, "lobby-curse:" + id, on => ToggleCurse(id, on)));
        }
        sp.Children.Add(row);

        var summary = Ui.Text(RulesSummary(active, host), 15, active.Count > 0 || Draft ? Theme.B("#d8b8f0") : Theme.TextDim, false, TextAlignment.Left, true);
        summary.Tag = "lobby-curses";
        summary.MaxWidth = 1150;
        summary.HorizontalAlignment = HorizontalAlignment.Left;
        summary.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(summary);
        return sp;
    }

    /// <summary>Kural düğmesi: oda kuranda tıklanır; konukta aynı görünür ama kilitlidir (ipucu yine açılır).</summary>
    private static ToggleButton RuleChip(string title, string tip, bool on, bool editable, string tag, Action<bool> changed)
    {
        var tb = new ToggleButton
        {
            Style = (Style)Application.Current.FindResource("GToggle"), Margin = new Thickness(0, 3, 6, 3), Padding = new Thickness(12, 5, 12, 6),
            Content = Ui.Text(title, 15, on ? Theme.B("#f0e0ff") : Theme.B("#c8b8d8"), on),
            IsChecked = on, ToolTip = title + ": " + tip,
            Tag = editable ? tag : tag.Replace("lobby-", "lobby-view-"),
        };
        if (editable) tb.Click += (_, _) => changed(tb.IsChecked == true);
        else
        {
            tb.IsEnabled = false;
            tb.Cursor = Cursors.Arrow;
            ToolTipService.SetShowOnDisabled(tb, true);
        }
        return tb;
    }

    /// <summary>Seçili kuralların tek satırlık özeti (ne değişeceğini düz cümleyle söyler).</summary>
    private string RulesSummary(List<string> active, bool host)
    {
        var parts = new List<string>();
        if (Draft) parts.Add(host ? "Draft: desteleri masada kuracaksınız" : "Draft: desteni aşağıda draft ile kur");
        foreach (var id in active) {
            var text = GwentAI.Curses.TextFor(id, true).TrimEnd('.');
            parts.Add(GwentAI.Curses.Get(id).Name + ": " + char.ToLower(text[0], new System.Globalization.CultureInfo("tr-TR")) + text[1..]);
        }
        if (parts.Count == 0) return host ? "Orijinal kurallar. İstersen draft ya da bir veya birkaç lanet seç." : "Orijinal kurallar.";
        return (active.Count > 0 ? "☠ " : "") + string.Join(" · ", parts) + ".";
    }

    /// <summary>Draft seçimi sunucuya gider (sunucu doğrular); yanıt gelene dek ikinci tıklama yok sayılır.</summary>
    private async void Choose(string kind, string id)
    {
        if (_busy) return;
        _busy = true;
        Sfx.Play(kind == "card" ? "draw_card" : "lock_in");
        var r = await _c.Link.DraftChooseAsync(kind, id, Settings.Portrait);
        _busy = false;
        _error = r.Ok ? "" : r.Error ?? "";
        Rebuild();
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
        var titleCol = new StackPanel();
        titleCol.Children.Add(Ornate.Heading("Bekleme odası", Draft ? "Draft maçı: desteler masada kurulur" : NextGen ? "Yeni nesil içerik açık" : "Standart kartlar", 46));
        if (st != null && st.Phase == RoomPhase.Lobby) titleCol.Children.Add(RulesBar(st));
        head.Children.Add(titleCol);

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
        var msg = Ui.Text(_error != "" ? _error : _notice != "" ? _notice : FootMessage(), 18,
            _error != "" ? Theme.Bad : _notice != "" ? Theme.B("#f0d890") : Theme.B("#c9c0aa"), _notice != "" && _error == "", TextAlignment.Left, true);
        msg.Tag = "lobby-foot";
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
        if (Opp == null) return _c.Invites.Count > 0 ? "Rakip bekleniyor… Sağ üstteki davet kodunu kopyalayıp arkadaşına gönder." : "Rakip bekleniyor…";
        if (!Opp.Connected) return "Rakibin bağlantısı koptu.";
        if (Me?.Ready == true && Opp.Ready != true) return "Rakibin hazır olması bekleniyor…";
        if (Me?.Ready != true && Opp.Ready) return "Rakip hazır. Seçimini yapıp hazır ol.";
        if (Draft && Me?.Loadout == null) return "Draft: desteni kur; bitince Hazırım'a bas.";
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

        if (Mirror) root.Children.Add(MirrorHint("Ayna: burada kurduğun desteyle RAKİBİN oynayacak; sen onun destesiyle.", 560));
        (_picker.Parent as Panel)?.Children.Remove(_picker);   // önceki çizimin kökünden ayır
        if (Draft) root.Children.Add(DraftPanel());
        else
        {
            _picker.Build();
            root.Children.Add(_picker);
        }
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
    }

    // ---------- Draft ----------

    private static FrameworkElement Progress(int n, int total, double w)
    {
        var g = new Grid { Width = w, Height = 10, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(new Border { Background = Theme.B("#33ffffff"), CornerRadius = new CornerRadius(5) });
        g.Children.Add(new Border { Background = Ornate.GoldEdge, CornerRadius = new CornerRadius(5), Width = w * Math.Min(n, total) / total, HorizontalAlignment = HorizontalAlignment.Left });
        return g;
    }

    /// <summary>Benim draft adımım: fraksiyon → lider → 25 kart (her seferinde 3 karttan biri) → deste hazır.</summary>
    private UIElement DraftPanel()
    {
        var d = State?.YourDraft;
        var sp = new StackPanel();
        if (d?.Faction == null)
        {
            sp.Children.Add(Label("Fraksiyon seç"));
            var row = new WrapPanel();
            foreach (var f in Factions)
            {
                var faction = f;
                var tile = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 12, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "odraft-faction:" + f };
                if (Assets.Ui("shield_" + f) is { } src) tile.Children.Add(new System.Windows.Controls.Image { Source = src, Width = 96, Height = 96, IsHitTestVisible = false });
                else tile.Children.Add(new Border { Width = 96, Height = 96, Background = Theme.FactionColor(f), IsHitTestVisible = false });
                tile.Children.Add(Ui.Text(Texts.Faction(f), 16, Theme.B("#f0d890"), true, TextAlignment.Center, true));
                Fx.HoverLift(tile, -6);
                tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose("faction", faction); };
                row.Children.Add(tile);
            }
            sp.Children.Add(row);
            return sp;
        }
        if (d.LeaderId == null)
        {
            sp.Children.Add(Label("Liderini seç · " + Texts.Faction(d.Faction)));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var id in d.LeaderOffer)
            {
                var def = CardCatalog.Default.Get(id);
                row.Children.Add(Choice(def, 170, Texts.LeaderText(def), "odraft-leader:" + id, () => Choose("leader", id)));
            }
            sp.Children.Add(row);
            return sp;
        }
        var picks = d.Picks.Select(CardCatalog.Default.Get).ToList();
        var units = picks.Count(x => x.IsUnit);
        var summary = $"{Texts.Faction(d.Faction)} · {CardCatalog.Default.Get(d.LeaderId).Name}   ·   Birim {units} · Özel {picks.Count - units} · Kahraman {picks.Count(x => x.Hero)}";
        if (d.Picks.Count < GwentAI.Draft.DraftLogic.DeckSize)
        {
            sp.Children.Add(Label($"Seçim {d.Picks.Count + 1} / {GwentAI.Draft.DraftLogic.DeckSize}"));
            sp.Children.Add(Ui.Text(summary, 15, Theme.TextDim));
            sp.Children.Add(Progress(d.Picks.Count, GwentAI.Draft.DraftLogic.DeckSize, 560));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var id in d.Offer)
            {
                var def = CardCatalog.Default.Get(id);
                var text = def.IsUnit ? Texts.RowOf(def) + (def.Hero ? " · Kahraman" : "") + (def.Abilities.Count > 0 ? "\n" + string.Join("\n", def.Abilities.Select(Texts.AbilityText)) : "")
                    : string.Join("\n", def.Abilities.Select(Texts.AbilityText));
                row.Children.Add(Choice(def, 180, text, "odraft-card", () => Choose("card", id)));
            }
            sp.Children.Add(row);
            return sp;
        }
        // Draft bitti: destenin özeti
        sp.Children.Add(Label("Desten hazır"));
        sp.Children.Add(Ui.Text(summary, 16, Theme.Text, true));
        var list = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var g in d.Picks.GroupBy(x => x).Select(x => (Def: CardCatalog.Default.Get(x.Key), N: x.Count())).OrderBy(x => x.Def.IsUnit ? 0 : 1).ThenByDescending(x => x.Def.Strength))
        {
            var t = Ui.Text($"{(g.Def.IsUnit ? g.Def.Strength.ToString() : "★")}  {g.Def.Name}{(g.N > 1 ? " ×" + g.N : "")}", 14, g.Def.Hero ? Theme.B("#f0d890") : Theme.B("#d8cdb0"));
            t.Width = 260;
            t.Margin = new Thickness(0, 1, 0, 1);
            list.Children.Add(t);
        }
        sp.Children.Add(list);
        var hint = Ui.Text("Hazırım'a bas: ikiniz de hazır olunca maç draft destelerinizle başlar.", 16, Theme.B("#f0d890"), true, TextAlignment.Left, true);
        hint.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(hint);
        return sp;
    }

    private static FrameworkElement Choice(CardDef d, double w, string text, string tag, Action pick)
    {
        var col = new StackPanel { Width = w + 30, Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = tag };
        var card = new GwintCard(d, w, d.IsUnit ? d.Strength : null, height: w * GwintCard.Aspect) { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.9 };
        col.Children.Add(card);
        var n = Ui.Text(d.Name, 17, d.Hero ? Theme.B("#f0d890") : Theme.Text, true, TextAlignment.Center, true);
        n.Margin = new Thickness(0, 8, 0, 2);
        col.Children.Add(n);
        col.Children.Add(Ui.Text(text, 13, Theme.B("#cfc4aa"), false, TextAlignment.Center, true));
        Fx.HoverLift(col, -10);
        col.MouseEnter += (_, _) => card.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 100), BlurRadius = 28, ShadowDepth = 0, Opacity = 0.9 };
        col.MouseLeave += (_, _) => card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.9 };
        col.MouseLeftButtonUp += (_, e) => { e.Handled = true; pick(); };
        return col;
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(Ornate.Spaced(t), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, 14, 0, 8);
        return l;
    }

    /// <summary>Ayna açıkken desteler takas edilir; iki panelde de kimin hangi desteyle oynayacağı açıkça yazılır.</summary>
    private static UIElement MirrorHint(string text, double width)
    {
        var t = Ui.Text("⇄ " + text, 15, Theme.B("#e0c8f0"), true, TextAlignment.Left, true);
        t.MaxWidth = width;
        t.Tag = "lobby-mirror-hint";
        return new Border
        {
            Child = t, Margin = new Thickness(0, 8, 0, 4), Padding = new Thickness(12, 6, 12, 7), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.B("#2a1d33"), BorderBrush = Theme.B("#6a4a80"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3)
        };
    }

    private UIElement OppPanel()
    {
        var oppReady = Opp?.Ready == true;
        var stamp = oppReady && !_oppReadyShown;
        _oppReadyShown = oppReady;
        var sp = new StackPanel();
        sp.Children.Add(SeatHeader("RAKİP", Opp?.Nick ?? "—", Seal(oppReady, stamp), Opp?.Loadout?.Avatar ?? Opp?.Avatar ?? Opp?.Loadout?.LeaderId));
        sp.Children.Add(Ornate.Rule(430, 10));
        if (Opp == null)
        {
            var w = Ui.Text("Rakip bekleniyor…", 28, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
            w.Margin = new Thickness(0, 24, 0, 0);
            w.FontStyle = FontStyles.Italic;
            sp.Children.Add(w);
            sp.Children.Add(Ui.Text("Davet kodunu paylaş; arkadaşın katılınca burada belirir.", 16, Theme.TextDim, false, TextAlignment.Left, true));
            return sp;
        }
        sp.Children.Add(Ui.Text(Opp.Connected ? "● bağlı" : "● bağlantısı koptu", 15, Opp.Connected ? Theme.Good : Theme.Bad));
        if (Mirror) sp.Children.Add(MirrorHint("Ayna: aşağıdaki desteyle SEN oynayacaksın.", 430));

        if (Draft && Opp.Loadout == null)
        {
            // Rakibin draft ilerlemesi (teklifleri görünmez)
            var picks = Math.Max(0, Opp.DraftPicks);
            if (Opp.DraftFaction is { } df && Assets.Ui("shield_" + df) is { } sh)
                sp.Children.Add(new System.Windows.Controls.Image { Source = sh, Width = 70, Height = 70, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            sp.Children.Add(Ui.Text(Opp.DraftPicks < 0 ? "Fraksiyon seçiyor…" : $"Draft: {picks} / {GwentAI.Draft.DraftLogic.DeckSize} kart", 22, Theme.Text, true));
            sp.Children.Add(Progress(picks, GwentAI.Draft.DraftLogic.DeckSize, 420));
            return sp;
        }
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
