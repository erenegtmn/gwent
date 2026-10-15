using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GwentAI;
using GwentCore;
using KaerMorhen.Core;

namespace GwentClient.KaerMorhen;

/// <summary>Kale ekranından çıkışlar: sözleşmeye başla (deste, rünler, parçalar), menüye dön.</summary>
public sealed record KeepNav(Action<Contract, Deck, List<CardRune>, List<TerrainPiece>> StartContract, Action Back);

/// <summary>
/// Kaer Morhen kalesi (TASARIM.md §4): kaynaklar, binalar (onar/yükselt), sözleşme panosu. Sözleşme seçilince hazırlık penceresi:
/// deste, demirci (rünler) ve mühendis parçaları. Her değişiklik hemen kaydedilir.
/// </summary>
public sealed class KeepView : Grid
{
    private readonly KeepSave _s;
    private readonly KeepNav _nav;
    private readonly Grid _content = new() { Width = 1920, Height = 1080 };
    private readonly Grid _overlay = new() { Width = 1920, Height = 1080, Visibility = Visibility.Collapsed };
    private string? _message;

    public KeepView(KeepSave save, KeepNav nav, string? message = null)
    {
        _s = save;
        _nav = nav;
        _message = message;
        var stack = new Grid();
        stack.Children.Add(_content);
        stack.Children.Add(_overlay);
        Children.Add(Ornate.Stage(new Viewbox { Stretch = Stretch.Uniform, Child = stack }, "pano_kaer_morhen", 0.7));
        Build();
    }

    private static Brush MutColor(Mutagen m) => Theme.B(m switch { Mutagen.Red => "#e05a4a", Mutagen.Green => "#7ac85a", _ => "#5a9ae0" });

    private void Save() => KeepStore.Save(_s);

    // ---------- Ana ekran ----------

    private void Build()
    {
        _content.Children.Clear();
        var canvas = new Canvas();
        _content.Children.Add(canvas);

        var head = Ornate.Heading("Kaer Morhen", "Kaleni onar, sözleşmeleri al, Wild Hunt'a hazırlan.", 46);
        Canvas.SetLeft(head, 70); Canvas.SetTop(head, 50);
        canvas.Children.Add(head);

        // Kaynaklar
        var res = new StackPanel { Orientation = Orientation.Horizontal, Tag = "keep-res" };
        res.Children.Add(Ui.Text($"◆ {_s.Crowns} Crown", 26, Theme.B("#f0d890"), true));
        foreach (var m in new[] { Mutagen.Red, Mutagen.Green, Mutagen.Blue })
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20, Fill = MutColor(m), Margin = new Thickness(34, 6, 8, 0), ToolTip = Keep.MutagenName(m) + " mutajen" };
            res.Children.Add(dot);
            res.Children.Add(Ui.Text(_s.Mutagens(m).ToString(), 26, Theme.Text, true));
        }
        Canvas.SetRight(res, 80); Canvas.SetTop(res, 66);
        canvas.Children.Add(res);
        var chapter = Keep.Endless(_s) ? $"Kronik tamam · Beyaz Ayaz {_s.Chapter - Keep.Chronicle.Count + 1}. tur" : $"Kronik {_s.Chapter + 1}/{Keep.Chronicle.Count}";
        var best = _s.Runs.Count > 0 ? $" · En iyi: {(_s.BestChapter >= Keep.Chronicle.Count ? "Kronik tamam" : $"{_s.BestChapter + 1}. bölüm")}" : "";
        var record = Ui.Text($"{_s.Run}. sefer{best} · {chapter} · Sözleşmeler: {_s.Wins} başarı · {_s.Losses} başarısız", 15, Theme.TextDim);
        record.Tag = "keep-run";
        Canvas.SetRight(record, 82); Canvas.SetTop(record, 108);
        canvas.Children.Add(record);

        if (_message != null)
        {
            var fallen = _message.StartsWith("Kaer Morhen düştü");
            var msg = new Border
            {
                Background = Theme.B(fallen ? "#cc2a0a08" : "#cc140e08"), BorderBrush = fallen ? Theme.Bad : Theme.Gold, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(20, 8, 20, 8), Width = 1780,
                Child = Ui.Text(_message, 19, Theme.B("#f0d890"), true, TextAlignment.Center), Tag = "keep-message"
            };
            Canvas.SetLeft(msg, 70); Canvas.SetTop(msg, 150);
            canvas.Children.Add(msg);
        }

        // Binalar (sol): 2 sütun
        var cap1 = Ornate.Caption("Kale", 0);
        Canvas.SetLeft(cap1, 70); Canvas.SetTop(cap1, 214);
        canvas.Children.Add(cap1);
        var grid = new UniformGrid { Columns = 2, Width = 1010 };
        foreach (var b in Keep.Buildings) grid.Children.Add(BuildingCard(b));
        Canvas.SetLeft(grid, 60); Canvas.SetTop(grid, 244);
        canvas.Children.Add(grid);

        // Sözleşme panosu (sağ)
        var cap2 = Ornate.Caption("Sözleşme panosu", 0);
        Canvas.SetLeft(cap2, 1120); Canvas.SetTop(cap2, 214);
        canvas.Children.Add(cap2);
        var board = new StackPanel { Width = 740 };
        board.Children.Add(SiegeCard());
        foreach (var c in _s.Board) board.Children.Add(ContractCard(c));
        Canvas.SetLeft(board, 1110); Canvas.SetTop(board, 244);
        canvas.Children.Add(board);

        var back = Ui.Button("Menüye dön", () => { Save(); _nav.Back(); });
        back.Tag = "keep-back";
        back.Padding = new Thickness(28, 10, 28, 10);
        back.FontSize = 17;
        Canvas.SetLeft(back, 70); Canvas.SetTop(back, 990);
        canvas.Children.Add(back);
    }

    private UIElement BuildingCard(Building b)
    {
        var lvl = _s.Level(b.Id);
        var sp = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Text(b.Name, 22, b.Planned ? Theme.TextDim : Theme.B("#e8c970"), true));
        if (!b.Planned)
        {
            var pips = string.Concat(Enumerable.Range(0, b.MaxLevel).Select(i => i < lvl ? "●" : "○"));
            var pt = Ui.Text("  " + pips, 18, MutColor(b.Color), true);
            pt.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(pt);
        }
        sp.Children.Add(title);
        sp.Children.Add(Ui.Text(b.Text, 13, Theme.TextDim, false, TextAlignment.Left, true));
        if (b.Planned)
        {
            sp.Children.Add(Ui.Text("Yakında (sonraki aşamalar).", 14, Theme.TextDim, false, TextAlignment.Left, true));
        }
        else
        {
            sp.Children.Add(Ui.Text(lvl == 0 ? "Harabe: henüz onarılmadı." : "Şu an: " + b.Effects[lvl - 1], 14, Theme.Text, false, TextAlignment.Left, true));
            if (Keep.NextCost(_s, b.Id) is { } cost)
            {
                var next = Ui.Text("Sonraki: " + b.Effects[lvl], 13, Theme.B("#c9b88a"), false, TextAlignment.Left, true);
                next.Margin = new Thickness(0, 2, 0, 6);
                sp.Children.Add(next);
                var blocked = Keep.UpgradeBlocked(_s, b.Id);
                var label = (lvl == 0 ? "Onar" : "Yükselt") + $" — {cost.Crowns} Crown" + (cost.Mutagens > 0 ? $" + {cost.Mutagens} {Keep.MutagenName(cost.Color)}" : "");
                var btn = Ui.Button(label, () => { if (Keep.Upgrade(_s, b.Id)) { Save(); Sfx.Play("leader_change"); _message = $"{b.Name} {(lvl == 0 ? "onarıldı" : "yükseltildi")}: {b.Effects[lvl]}"; Build(); } },
                    enabled: blocked == null);
                btn.Tag = "keep-upgrade:" + b.Id;
                btn.HorizontalAlignment = HorizontalAlignment.Left;
                btn.FontSize = 14;
                btn.Padding = new Thickness(14, 6, 14, 6);
                if (blocked != null) { ToolTipService.SetShowOnDisabled(btn, true); btn.ToolTip = blocked; }
                sp.Children.Add(btn);
            }
            else sp.Children.Add(Ui.Text("En üst seviyede.", 13, Theme.B("#c9b88a")));
            if (b.Id == Keep.AlliesHall && lvl > 0)
            {
                var allies = Ui.Button($"Müttefikler… ({Keep.MatchAllies(_s).Count}/{Keep.AllySlots(_s)} maçta)", OpenAllies);
                allies.Tag = "keep-allies";
                allies.HorizontalAlignment = HorizontalAlignment.Left;
                allies.FontSize = 14;
                allies.Padding = new Thickness(14, 6, 14, 6);
                allies.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(allies);
            }
        }
        var panel = Ornate.Panel(sp, new Thickness(22, 16, 22, 16), 12, 0);
        panel.Margin = new Thickness(8);
        panel.Height = 228;
        panel.Opacity = b.Planned ? 0.55 : 1;
        return panel;
    }

    /// <summary>Wild Hunt: yaklaşma göstergesi, kuşatmayı başlat ya da sıradaki dalga.</summary>
    private UIElement SiegeCard()
    {
        if (Keep.SiegeContract(_s) is { } wave) return ContractCard(wave);
        var foe = Keep.CurrentAntagonist(_s);
        if (!Keep.SiegeReady(_s))
        {
            var t = Ui.Text($"Sıradaki tehdit: {foe.Name} — {_s.SiegeProgress}/{Keep.SiegeEvery} sözleşme. Sonra kaleye saldıracaklar; hazırlan."
                + (_s.SiegesWon + _s.SiegesLost > 0 ? $"  (Kuşatmalar: {_s.SiegesWon} püskürtüldü, {_s.SiegesLost} kırılamadı)" : ""), 15, Theme.B("#9ac4f0"), false, TextAlignment.Left, true);
            t.Tag = "siege-progress";
            t.Margin = new Thickness(4, 0, 0, 12);
            return t;
        }
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text($"{foe.Name} kapıda!", 24, Theme.B("#9ac4f0"), true));
        sp.Children.Add(Ui.Text(foe.Intro + $" Üç dalga: {string.Join(", ", foe.Waves.Select(w => w.Title))}. Kalenin parçaları ve müttefiklerin seninle. "
            + "Kuşatma bitmeden sözleşmeye çıkamazsın. Bir dalgayı kaybedersen kale düşer ve sefer baştan başlar.", 14, Theme.Text, false, TextAlignment.Left, true));
        var go = Ui.Button("Kuşatmayı başlat", () => { Keep.StartSiege(_s); Save(); Build(); }, primary: true);
        go.Tag = "siege-start";
        go.HorizontalAlignment = HorizontalAlignment.Left;
        go.Margin = new Thickness(0, 8, 0, 0);
        go.Padding = new Thickness(20, 8, 20, 8);
        sp.Children.Add(go);
        var panel = Ornate.Panel(sp, new Thickness(22, 14, 22, 14), 12, 0, Theme.B("#ee0c1420"));
        panel.Margin = new Thickness(0, 0, 0, 12);
        return panel;
    }

    private UIElement ContractCard(Contract c)
    {
        var siege = c.Id < 0;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shield = c.Faction == "mirror" ? null : Assets.Ui("shield_" + c.Faction);
        if (shield != null) g.Children.Add(new Image { Source = shield, Width = 70, Height = 70, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top });
        var sp = new StackPanel();
        Grid.SetColumn(sp, 1);
        sp.Children.Add(Ui.Text(c.Title, 21, Theme.B(siege ? "#9ac4f0" : "#e8c970"), true));
        sp.Children.Add(Ui.Text(c.Text, 13, Theme.TextDim, false, TextAlignment.Left, true));
        var map = Battlefields.Get(c.Battlefield)?.Name ?? c.Battlefield;
        var diff = c.Difficulty switch { Difficulty.Easy => "Kolay", Difficulty.Normal => "Normal", _ => "Zor" };
        var who = c.Faction == "mirror" ? "Senin desten (Ayna)" : Texts.FactionShort(c.Faction);
        var info = $"{who} · {diff} · ⚑ {map}" + (c.Curses is { Count: > 0 } cs ? " · ☠ " + string.Join(", ", cs.Select(x => Curses.Get(x).Name)) : "")
                   + (c.OppPerks is { } op ? $" · rakip {Game.StartingHand + op.ExtraHand} kartla başlar" + (op.Potions is { Count: > 0 } ps ? " + iksir" : "") : "");
        sp.Children.Add(Ui.Text(info, 14, Theme.Text, false, TextAlignment.Left, true));
        var reward = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        reward.Children.Add(Ui.Text($"Ödül: ◆ {c.RewardCrowns} Crown  ", 15, Theme.B("#f0d890"), true));
        reward.Children.Add(new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = MutColor(c.RewardColor), Margin = new Thickness(4, 3, 6, 0) });
        reward.Children.Add(Ui.Text($"{c.RewardMutagens} {Keep.MutagenName(c.RewardColor)} mutajen", 15, Theme.Text, true));
        var locked = !siege && Keep.ContractsLocked(_s);
        var take = Ui.Button(siege ? "Surlara!" : "Sözleşmeyi al", () => OpenPrep(c), primary: true, enabled: !locked);
        take.Tag = siege ? "siege-go" : "contract:" + c.Id;
        if (locked) { ToolTipService.SetShowOnDisabled(take, true); take.ToolTip = "Düşman kapıda: önce kuşatmayı karşıla."; }
        take.FontSize = 14;
        take.Padding = new Thickness(16, 6, 16, 6);
        take.Margin = new Thickness(24, 0, 0, 0);
        reward.Children.Add(take);
        sp.Children.Add(reward);
        g.Children.Add(sp);
        var panel = Ornate.Panel(g, new Thickness(22, 14, 22, 14), 12, 0, siege ? Theme.B("#ee0c1420") : null);
        panel.Margin = new Thickness(0, 0, 0, 12);
        return panel;
    }

    // ---------- Müttefikler ----------

    private void OpenAllies()
    {
        _overlay.Children.Clear();
        _overlay.Visibility = Visibility.Visible;
        _overlay.Background = Theme.B("#d8080503");
        var root = new StackPanel { Width = 1240 };
        root.Children.Add(Ornate.Heading("Müttefik Salonu", $"Dostlarını çağır. Maça katılanlar elinde fazladan kart olarak başlar ({Keep.AllySlots(_s)} yuva).", 36));
        var wrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var a in Keep.AllAllies)
        {
            var ally = a;
            var def = CardCatalog.Default.Get(a.CardId);
            var cell = new StackPanel { Width = 196, Margin = new Thickness(5) };
            var card = new GwintCard(def, 150, def.Strength, height: 200) { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            cell.Children.Add(card);
            cell.Children.Add(Ui.Text(a.Name, 16, Theme.B("#e8c970"), true, TextAlignment.Center));
            var lore = Ui.Text(a.Text, 12, Theme.TextDim, false, TextAlignment.Center, true);
            lore.Height = 34;
            cell.Children.Add(lore);
            if (_s.Allies.Contains(a.CardId))
            {
                var active = _s.ActiveAllies.Contains(a.CardId);
                var tb = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), IsChecked = active, Tag = "ally-active:" + a.CardId, Margin = new Thickness(0, 4, 0, 0),
                    Content = Ui.Text(active ? "Maça katılır" : "Kalede bekler", 14, Theme.Text, true, TextAlignment.Center)
                };
                tb.Click += (_, _) =>
                {
                    if (!Keep.ToggleActive(_s, ally.CardId)) Sfx.Play("dummy");
                    Save();
                    OpenAllies();
                };
                cell.Children.Add(tb);
            }
            else
            {
                var blocked = Keep.RecruitBlocked(_s, a.CardId);
                var c = a.Cost;
                cell.Children.Add(Ui.Text($"◆ {c.Crowns} Crown" + (c.Mutagens > 0 ? $" + {c.Mutagens} {Keep.MutagenName(c.Color)}" : ""), 13, Theme.B("#f0d890"), true, TextAlignment.Center));
                var btn = Ui.Button("Çağır",
                    () => { if (Keep.Recruit(_s, ally.CardId)) { Save(); Sfx.Play("leader_change"); } OpenAllies(); }, enabled: blocked == null);
                btn.Tag = "ally-recruit:" + a.CardId;
                btn.FontSize = 13;
                btn.Margin = new Thickness(0, 4, 0, 0);
                if (blocked != null) { ToolTipService.SetShowOnDisabled(btn, true); btn.ToolTip = blocked; }
                cell.Children.Add(btn);
            }
            wrap.Children.Add(cell);
        }
        root.Children.Add(wrap);
        var done = Ui.Button("Tamam", () => { _overlay.Visibility = Visibility.Collapsed; _overlay.Children.Clear(); Build(); }, primary: true);
        done.Tag = "allies-done";
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.Padding = new Thickness(40, 10, 40, 10);
        done.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(done);
        var panel = Ornate.Panel(root, new Thickness(40, 28, 40, 28), 20, 54);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        _overlay.Children.Add(panel);
    }

    // ---------- Sözleşme hazırlığı ----------

    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };
    private const string DeckRecommended = "@rec";
    private readonly Dictionary<string, RowKind?> _placed = new();

    private (Deck Deck, string Key) PrepDeck()
    {
        var f = _s.LastFaction ?? "northern_realms";
        var choice = _s.LastDeck ?? DeckRecommended;
        var deck = DeckValidator.RecommendedDeck(f);
        if (choice.StartsWith("deck:") && DeckStore.For(f).FirstOrDefault(d => "deck:" + d.Name == choice) is { } saved
            && DeckValidator.Validate(saved.ToDeck()).Count == 0)
            deck = saved.ToDeck();
        else choice = DeckRecommended;
        return (deck, "keep|" + f + "|" + choice);
    }

    private void OpenPrep(Contract c)
    {
        _overlay.Children.Clear();
        _overlay.Visibility = Visibility.Visible;
        _overlay.Background = Theme.B("#d8080503");
        var root = new StackPanel { Width = 1100 };
        root.Children.Add(Ornate.Heading(c.Title.ToUpperInvariant(), c.Text, 36));   // İngilizce ad: Türkçe büyük harf "İ" olmasın

        // Deste
        root.Children.Add(Ornate.Caption("Desteni seç"));
        var fRow = new UniformGrid { Rows = 1 };
        foreach (var f in Factions)
        {
            var key = f;
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Margin = new Thickness(3), Tag = "prep-faction:" + f,
                Content = Ui.Text(Texts.FactionShort(f), 15, Theme.Text, true, TextAlignment.Center), IsChecked = (_s.LastFaction ?? "northern_realms") == f
            };
            tb.Click += (_, _) => { _s.LastFaction = key; _s.LastDeck = DeckRecommended; Save(); OpenPrep(c); };
            fRow.Children.Add(tb);
        }
        root.Children.Add(fRow);
        var dRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var faction = _s.LastFaction ?? "northern_realms";
        var choices = new List<(string Key, string Name)> { (DeckRecommended, "Önerilen deste") };
        choices.AddRange(DeckStore.For(faction).Where(d => DeckValidator.Validate(d.ToDeck()).Count == 0).Select(d => ("deck:" + d.Name, d.Name)));
        var (deck, forgeKey) = PrepDeck();
        foreach (var (key, name) in choices)
        {
            var k = key;
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Margin = new Thickness(3), Padding = new Thickness(14, 6, 14, 6), Tag = "prep-deck:" + key,
                Content = Ui.Text(name, 14, Theme.Text, true), IsChecked = forgeKey.EndsWith("|" + key)
            };
            tb.Click += (_, _) => { _s.LastDeck = k; Save(); OpenPrep(c); };
            dRow.Children.Add(tb);
        }
        root.Children.Add(dRow);

        // Demirci
        root.Children.Add(Ornate.Caption("Demirci"));
        var runes = Keep.LimitRunes(_s, ForgeStore.For(forgeKey, deck));
        var forgeRow = new Grid();
        forgeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        forgeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var slots = Keep.RuneSlots(_s);
        forgeRow.Children.Add(Ui.Text(slots == 0 ? "Demirci harabe: rün takmak için kalede onar."
            : runes.Count == 0 ? $"Rün yok ({slots} yuva)." : $"Rünler {runes.Count}/{slots}: " + string.Join(" · ", runes.Select(r => $"{CardCatalog.Default.Get(r.CardId).Name} ({Forge.Get(r.Rune).Name})")),
            15, Theme.Text, false, TextAlignment.Left, true));
        var forgeBtn = Ui.Button("Demirci…", () =>
        {
            ForgeView? fv = null;
            fv = new ForgeView(deck, forgeKey, () => { _overlay.Children.Remove(fv); OpenPrep(c); }, slots, Keep.AllowedRunes(_s));
            _overlay.Children.Add(fv);
        }, enabled: slots > 0);
        forgeBtn.Tag = "prep-forge";
        forgeBtn.Padding = new Thickness(16, 6, 16, 6);
        Grid.SetColumn(forgeBtn, 1);
        forgeRow.Children.Add(forgeBtn);
        root.Children.Add(forgeRow);

        // Mühendis parçaları
        root.Children.Add(Ornate.Caption("Mühendis atölyesi — tıkla: satırı değiştir"));
        var pieces = Keep.AvailablePieces(_s);
        if (pieces.Count == 0) root.Children.Add(Ui.Text("Atölye harabe: arazi parçası kurmak için kalede onar.", 15, Theme.TextDim));
        foreach (var p in pieces)
        {
            var piece = p;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var current = _placed.GetValueOrDefault(p.Id);
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Width = 520, Padding = new Thickness(12, 6, 12, 6), Tag = "prep-piece:" + p.Id,
                HorizontalContentAlignment = HorizontalAlignment.Left, IsChecked = current != null,
                Content = Ui.Text($"{p.Name}: " + (current is { } cr ? $"{(p.OnOpponent ? "rakibin" : "senin")} {Texts.Row(cr)} satırın" : "kullanma") + $" — {p.Text}", 14, Theme.Text, false, TextAlignment.Left, true)
            };
            // Tıkla: kullanma → yakın → menzil → kuşatma → kullanma
            tb.Click += (_, _) =>
            {
                _placed[piece.Id] = _placed.GetValueOrDefault(piece.Id) switch
                {
                    null => RowKind.Close, RowKind.Close => RowKind.Ranged, RowKind.Ranged => RowKind.Siege, _ => null
                };
                OpenPrep(c);
            };
            row.Children.Add(tb);
            root.Children.Add(row);
        }

        // Simya: iksirler (tıkla: iç / bırak), toksisite sınırı
        var limit = Keep.ToxicityLimit(_s);
        var chosen = Keep.MatchPotions(_s);
        root.Children.Add(Ornate.Caption(limit == 0 ? "Simya" : $"Simya — toksisite {Keep.Toxicity(chosen)}/{limit}"));
        if (limit == 0) root.Children.Add(Ui.Text("Simya Odası harabe: iksir içmek için kalede onar.", 15, Theme.TextDim));
        else
        {
            var pw = new WrapPanel();
            foreach (var p in Keep.UnlockedPotions(_s))
            {
                var potion = p;
                var on = chosen.Contains(p.Id);
                var tb = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), Width = 336, Margin = new Thickness(3), Padding = new Thickness(10, 5, 10, 6), Tag = "prep-potion:" + p.Id,
                    HorizontalContentAlignment = HorizontalAlignment.Left, IsChecked = on, ToolTip = p.Text
                };
                var tsp = new StackPanel();
                tsp.Children.Add(Ui.Text($"{p.Name}  ·  toksisite {p.Toxicity}", 14, Theme.B("#9ad070"), true));
                tsp.Children.Add(Ui.Text(p.Text, 11, Theme.TextDim, false, TextAlignment.Left, true));
                tb.Content = tsp;
                tb.Click += (_, _) => { if (!Keep.TogglePotion(_s, potion.Id)) Sfx.Play("dummy"); Save(); OpenPrep(c); };
                pw.Children.Add(tb);
            }
            root.Children.Add(pw);
        }

        // Kışla
        var barracks = _s.Level(Keep.Barracks);
        if (barracks > 0) root.Children.Add(Ui.Text("Kışla: " + string.Join(" ", Keep.Get(Keep.Barracks).Effects.Take(barracks)).Replace("+ ", ""), 14, Theme.Text, false, TextAlignment.Left, true));

        var allies = Keep.MatchAllies(_s);
        root.Children.Add(Ornate.Caption("Müttefikler"));
        root.Children.Add(Ui.Text(allies.Count == 0
            ? (Keep.AllySlots(_s) == 0 ? "Müttefik Salonu harabe: dostlarını çağırmak için kalede onar." : "Maça katılan müttefik yok (Müttefik Salonu'ndan seç).")
            : "Elinde başlar: " + string.Join(" · ", allies.Select(a => Keep.GetAlly(a).Name)), 15, Theme.Text, false, TextAlignment.Left, true));

        if (c.Id < 0)
        {
            var warn = Ui.Text("Bu bir kuşatma dalgası: kaybedersen Kaer Morhen düşer ve sefer baştan başlar (binalar, Crown, mutajen, müttefikler gider).", 15, Theme.Bad, true, TextAlignment.Left, true);
            warn.Margin = new Thickness(0, 10, 0, 4);
            warn.Tag = "prep-siege-warning";
            root.Children.Add(warn);
        }

        var map = Battlefields.Get(c.Battlefield);
        root.Children.Add(Ui.Text($"Savaş alanı: {map?.Name}. Komutanlar açık." + (c.Curses is { Count: > 0 } pc ? " Lanet: " + string.Join(" · ", pc.Select(x => $"{Curses.Get(x).Name} — {Curses.Get(x).Text}")) : ""), 14, Theme.TextDim, false, TextAlignment.Left, true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Ui.Button("Vazgeç", () => { _overlay.Visibility = Visibility.Collapsed; _overlay.Children.Clear(); });
        cancel.Tag = "prep-cancel";
        cancel.Padding = new Thickness(28, 10, 28, 10);
        cancel.Margin = new Thickness(0, 0, 14, 0);
        buttons.Children.Add(cancel);
        var go = Ui.Button("Savaşa!", () =>
        {
            Save();
            var placed = _placed.Where(x => x.Value != null).Select(x => (x.Key, x.Value!.Value));
            _nav.StartContract(c, deck, runes, Keep.ToTerrain(_s, placed));
        }, primary: true);
        go.Tag = "prep-go";
        go.Padding = new Thickness(40, 10, 40, 10);
        buttons.Children.Add(go);
        root.Children.Add(buttons);

        var panel = Ornate.Panel(root, new Thickness(40, 28, 40, 28), 20, 54);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        _overlay.Children.Add(panel);
    }
}
