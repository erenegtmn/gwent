using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Üç kişilik online masa (KURALLAR §24): ben altta orijinal düzende; üst yarı ikiye bölünür — sol üstte sıradaki rakip,
/// sağ üstte ondan sonraki. Rakiplerin kartları küçük çizilir; adları, puanları, liderleri ve yığınları kendi renkleriyle.
/// </summary>
public sealed partial class GwintView
{
    // Üst yarının iki yarısı: sol ve sağ rakip
    private const double HalfW = 470, TopCardW = 72, TopCardH = 97, TopSlotW = 56, TopCardsOff = 106;
    private static readonly double[] HalfX = { 570, 1046 };
    private static readonly string[] OppTint = { "#5a9ad8", "#b07ad8" };   // sol: mavi, sağ: mor

    /// <summary>Rakip koltuğunun ekrandaki yarısı (0 sol, 1 sağ). Üç kişide değilse 0.</summary>
    private int HalfOf(int seat) => G.ThreePlayer ? Math.Max(0, _s.OppSeats.ToList().IndexOf(seat)) : 0;

    private Point GravePosOf(int seat) =>
        seat == Me ? GravePos[0] : G.ThreePlayer ? new Point(1545 + HalfOf(seat) * 190, 40) : GravePos[1];

    /// <summary>Rakibin kartı masaya "elinden" gelirken çıktığı nokta (kendi yarısının üstü).</summary>
    private Point OppEntry(int seat) =>
        G.ThreePlayer && seat != Me ? new Point(HalfX[HalfOf(seat)] + 200, -150) : new Point(CardsX + 200, -150);

    private Point LeaderCenter(int seat) =>
        seat == Me ? new Point(133 + 56, 828 + 73)
        : G.ThreePlayer ? new Point(18 + 42, PanelY(HalfOf(seat)) + 40 + 56) : new Point(133 + 56, 75 + 73);

    private static double PanelY(int half) => 12 + half * 204;

    // ---------- Hedef rakip seçimi ----------

    private (string Title, List<int> Seats, Action<int> Then)? _foeAsk;
    private int? _choiceFoe;

    private void AskFoe(string title, List<int> seats, Action<int> then)
    {
        if (seats.Count == 1) { then(seats[0]); return; }
        _foeAsk = (title, seats, then);
        Refresh();
    }

    private UIElement FoePanel()
    {
        var ask = _foeAsk!.Value;
        var sp = new StackPanel { MinWidth = 640 };
        sp.Children.Add(Title(ask.Title));
        var sub = Txt("Hedef rakibi seç.", 20, Theme.TextDim, false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 6, 0, 22);
        sp.Children.Add(sub);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var seat in ask.Seats)
        {
            var s = seat;
            var tint = Theme.B(OppTint[HalfOf(seat)]);
            var content = new StackPanel();
            content.Children.Add(Ui.Text(_s.SeatName(seat), 22, Theme.Text, true, TextAlignment.Center));
            content.Children.Add(Ui.Text($"{Texts.FactionShort(G.Players[seat].Faction)} · {G.PlayerTotal(seat)} puan · {(HalfOf(seat) == 0 ? "sol üst" : "sağ üst")}", 15, Theme.TextDim, false, TextAlignment.Center));
            var b = Ui.Button("", () =>
            {
                var then = _foeAsk?.Then;
                _foeAsk = null;
                then?.Invoke(s);
            }, primary: true);
            b.Content = content;
            b.MinWidth = 260;
            b.Margin = new Thickness(10, 0, 10, 0);
            b.BorderBrush = tint;
            b.Tag = "foe-choice";
            row.Children.Add(b);
        }
        sp.Children.Add(row);
        var cancel = Ui.Button("Vazgeç", () => { _foeAsk = null; Refresh(); });
        cancel.Margin = new Thickness(0, 22, 0, 0);
        cancel.HorizontalAlignment = HorizontalAlignment.Center;
        cancel.Tag = "foe-cancel";
        sp.Children.Add(cancel);
        return sp;
    }

    // ---------- Üst yarı: iki rakibin sıraları ----------

    private void BuildTopThree()
    {
        // Orijinal tahtanın üstteki uzun sıraları örtülür; yerine iki yarım sıra takımı çizilir
        Put(_board, new Rectangle { Width = 966, Height = 404, Fill = Theme.B("#d8170f08"), RadiusX = 6, RadiusY = 6, IsHitTestVisible = false }, 560, 8);
        Put(_board, new Rectangle { Width = 2, Height = 398, Fill = Theme.B("#90c9a24b"), IsHitTestVisible = false }, 1040, 10);
        var opps = _s.OppSeats;
        for (var half = 0; half < 2; half++)
        {
            var seat = opps[half];
            for (var i = 0; i < 3; i++) BuildTopRow(seat, half, i);
            BuildOppPill(seat, half);
        }
    }

    private void BuildTopRow(int seat, int half, int index)
    {
        var rk = index switch { 0 => RowKind.Siege, 1 => RowKind.Ranged, _ => RowKind.Close };
        var row = G.Players[seat].Row(rk);
        var x0 = HalfX[half];
        var top = RowTop[index];
        var cardsX = x0 + TopCardsOff;
        var cardsW = HalfW - TopCardsOff - 6;

        // Oluk: yuva + kart alanı
        Put(_board, new Rectangle { Width = TopSlotW, Height = RowH - 6, Fill = Theme.B("#55000000"), Stroke = Theme.B("#40c9a24b"), StrokeThickness = 1, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false }, x0 + 44, top + 3);
        Put(_board, new Rectangle { Width = cardsW + 4, Height = RowH - 6, Fill = Theme.B("#38000000"), Stroke = Theme.B("#30c9a24b"), StrokeThickness = 1, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false }, cardsX - 2, top + 3);
        if (row.Cards.Count == 0)
            Put(_board, Txt(Texts.Row(rk), 15, Theme.B("#5a4a34"), false, TextAlignment.Center, cardsW, italic: true), cardsX, top + RowH / 2 - 10);

        // Hava: üstteki küçük sıralarda yalnız renkli örtü
        if (G.WeatherActive(rk) && !G.CommanderShields(seat, rk))
        {
            var storm = rk != RowKind.Close && G.WeatherCards.Any(w => w.Def.Has("storm"));
            var tint = storm ? "#3a6070a0" : rk switch { RowKind.Close => "#3ab8dcff", RowKind.Ranged => "#3ad0d0d0", _ => "#3a5070ff" };
            Put(_board, new Rectangle { Width = cardsW + 4, Height = RowH - 6, Fill = Theme.B(tint), RadiusX = 4, RadiusY = 4, IsHitTestVisible = false }, cardsX - 2, top + 3);
        }

        // Sıra puanı (rakibin rengiyle)
        var total = G.RowTotal(seat, rk);
        var circle = new Grid { Width = 40, Height = 40, IsHitTestVisible = false };
        var cimg = Pic("badge_big", 40, 40);
        if (cimg.Source != null) circle.Children.Add(cimg);
        else circle.Children.Add(new Ellipse { Fill = Theme.B("#e8dcc4"), Stroke = Theme.B("#8a6a30"), StrokeThickness = 2 });
        circle.Children.Add(new Ellipse { Fill = Theme.B(OppTint[half]), Opacity = 0.45, Margin = new Thickness(3) });
        var num = Txt(total.ToString(), total > 99 ? 15 : 19, Theme.B("#2a1c0e"), true, TextAlignment.Center);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        circle.Children.Add(num);
        PulseIfChanged(circle, $"r{seat}:{(int)rk}", total);
        Put(_board, circle, x0 + 1, top + (RowH - 40) / 2);

        // Boynuz / Mardroeme
        if (row.Special != null)
            Put(_board, MakeCard(row.Special, 52, false, height: 70), x0 + 46, top + (RowH - 70) / 2);

        // Kartlar (gerekirse üst üste)
        var n = row.Cards.Count;
        var step = n <= 1 ? 0 : Math.Min(TopCardW + 2, (cardsW - TopCardW) / (double)(n - 1));
        var y = top + (RowH - TopCardH) / 2;
        for (var i = 0; i < n; i++)
        {
            var c = row.Cards[i];
            var gc = MakeCard(c, TopCardW, true, height: TopCardH);
            var pos = new Point(cardsX + i * step, y);
            _curBoardInfo[c.Uid] = new Snap(c.Def, pos, seat, TopCardW, TopCardH);
            var arriving = _effects && !_firstDraw && !_prevBoard.Contains(c.Uid);
            Put(_board, CardShadow(TopCardW, TopCardH), pos.X + 2, pos.Y + 4);
            if (arriving) AnimateArrival(gc, c, seat, pos);
            else if (_effects && !_firstDraw && _prevBoardInfo.TryGetValue(c.Uid, out var was) && (Math.Abs(was.Pos.X - pos.X) > 0.5 || Math.Abs(was.Pos.Y - pos.Y) > 0.5))
                SlideFrom(gc, was.Pos.X - pos.X, was.Pos.Y - pos.Y, _evs.Any(e => e.Kind == "kill") ? 1050 : 0);
            Put(_board, gc, pos.X, pos.Y);
        }

        // Casus hedefi: iki rakibin sırası da vurgulanır, tıklanan taraf hedeftir
        if (RowHighlighted(seat, rk))
        {
            var hl = new Border
            {
                Width = HalfW - 38, Height = RowH, BorderBrush = Theme.Gold, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4),
                Background = Theme.B("#2affd36a"), Cursor = Cursors.Hand, Tag = "row-hl",
                Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 110), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 }
            };
            var s2 = seat; var r2 = rk;
            hl.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnRowClicked(s2, r2); };
            Panel.SetZIndex(hl, 5);
            Put(_board, hl, x0 + 40, top);
        }
    }

    /// <summary>Yarının altında, iki taraf arasındaki çizgide: rakibin adı, toplamı ve sıra/pas durumu.</summary>
    private void BuildOppPill(int seat, int half)
    {
        var ps = G.Players[seat];
        var turn = G.Phase == Phase.Playing && G.Pending == null && G.Current == seat && !ps.Passed;
        var text = $"{_s.SeatName(seat)}  ·  {G.PlayerTotal(seat)}" + (ps.Passed ? "  ·  pas" : turn ? "  ·  oynuyor" : "");
        var pill = new Border
        {
            Background = Theme.B("#e6120c07"), BorderBrush = turn ? Theme.Gold : Theme.B(OppTint[half]), BorderThickness = new Thickness(turn ? 2 : 1.4),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 1, 14, 2), IsHitTestVisible = false,
            Child = Txt(text, 16, ps.Passed ? Theme.TextDim : Theme.B("#f0e2c0"), true, TextAlignment.Center)
        };
        var holder = new Grid { Width = HalfW, IsHitTestVisible = false };
        holder.Children.Add(pill);
        pill.HorizontalAlignment = HorizontalAlignment.Center;
        Panel.SetZIndex(holder, 6);
        Put(_board, holder, HalfX[half], 405);
    }

    // ---------- Sol sütun: rakip panelleri; sağ sütun: rakip yığınları ----------

    private void BuildOppPanels3()
    {
        var opps = _s.OppSeats;
        var allTotals = Enumerable.Range(0, G.PlayerCount).Select(G.PlayerTotal).ToList();
        for (var half = 0; half < 2; half++)
        {
            var seat = opps[half];
            var ps = G.Players[seat];
            var y = PanelY(half);
            // Tahtanın alttaki lider yuvası süsü görünmesin: önce koyu zemin, üstüne ad bandı
            Put(_board, new Rectangle { Width = 450, Height = 196, Fill = Theme.B("#f01a1009"), IsHitTestVisible = false }, 0, y);
            var band = Pic("name_band", 450, 196, 0.9, Stretch.Fill);
            if (band.Source != null) Put(_board, band, 0, y);
            else Put(_board, new Rectangle { Width = 450, Height = 196, Fill = Theme.B("#aa000000"), IsHitTestVisible = false }, 0, y);
            Put(_board, new Rectangle { Width = 5, Height = 180, Fill = Theme.B(OppTint[half]), IsHitTestVisible = false }, 4, y + 8);

            // Lider (tıklayınca büyür)
            var leader = ps.Leader;
            var passive = IsPassiveLeader(leader);
            var used = ps.LeaderUsed || G.LeaderCancelled(seat);
            var lc = new GwintCard(leader, 84, null, null, dimmed: used && !passive, height: 113);
            lc.MouseEnter += (_, _) => SetPreview(leader, null);
            var owner = seat;
            lc.MouseLeftButtonUp += (_, e) => { e.Handled = true; _zoom = (owner, leader); Refresh(); };
            lc.Tag = "opp-leader";
            _leaderEl[seat] = lc;
            Put(_board, lc, 18, y + 40);
            var state = passive ? (G.LeaderCancelled(seat) ? "iptal" : "otomatik") : ps.LeaderUsed ? "kullanıldı" : G.LeaderCancelled(seat) ? "iptal" : "";
            if (state != "") Put(_board, Outlined(state, 13, Theme.B("#d0c5ae"), 84), 18, y + 156);

            // Portre, ad, fraksiyon, yarı
            var turn = G.Phase == Phase.Playing && G.Pending == null && G.Current == seat && !ps.Passed;
            Put(_board, Portraits.Medallion(_s.SeatAvatar(seat), 72, glow: turn), 112, y + 14);
            var shown = _s.SeatName(seat);
            Put(_board, Txt(shown, shown.Length > 11 ? 17 : 21, Theme.B("#e8c970"), true, TextAlignment.Left, 150), 194, y + 12);
            Put(_board, Txt(Texts.FactionShort(ps.Faction) + (half == 0 ? "  ·  sol üst ◤" : "  ·  sağ üst ◥"), 15, Theme.B(OppTint[half])), 194, y + 42);

            // El + kazanılan raundlar (mücevher dolar)
            var cardsIcon = Pic("cards_icon", 22, 29);
            if (cardsIcon.Source != null) Put(_board, cardsIcon, 194, y + 76);
            Put(_board, Txt(ps.Hand.Count.ToString(), 22, Theme.B("#e8dcc4"), true), 222, y + 76);
            for (var i = 0; i < 2; i++)
            {
                var lit = i < ps.Wins;
                var gem = Pic("gem", 32, 32, lit ? 1 : 0.22);
                if (gem.Source != null) Put(_board, gem, 258 + i * 38, y + 74);
                else Put(_board, new Ellipse { Width = 26, Height = 26, Fill = lit ? Theme.B("#d94a4a") : Theme.B("#3a2e20"), Stroke = Theme.B("#ffb0a0"), StrokeThickness = 1.5 }, 261 + i * 38, y + 77);
            }

            // Toplam: masanın en yükseği defneli
            var total = allTotals[seat];
            var holder = new Grid { Width = 92, Height = 72, IsHitTestVisible = false };
            if (total > 0 && allTotals.Where((_, i) => i != seat).All(t => total > t))
            {
                var laurel = Pic("laurel", 92, 70);
                if (laurel.Source != null) holder.Children.Add(laurel);
            }
            var num = Outlined(total.ToString(), total > 99 ? 26 : 32, Brushes.White, 92);
            num.VerticalAlignment = VerticalAlignment.Center;
            PulseIfChanged(num, "p" + seat, total);
            holder.Children.Add(num);
            Put(_board, holder, 350, y + 18);
            if (ps.Passed) Put(_board, Txt("Pas geçti", 19, Theme.B("#e8c970"), true, TextAlignment.Left, 150, italic: true), 194, y + 120);
            else if (turn) Put(_board, Txt("Sırası onda", 17, Theme.B("#f0d890"), true, TextAlignment.Left, 150, italic: true), 194, y + 120);

            // Yığınlar (sağ üst): mezarlık + deste
            var px = 1545 + half * 190;
            Put(_board, Txt(shown, 14, Theme.B(OppTint[half]), true, TextAlignment.Left, 180), px, 12);
            if (ps.Grave.Count > 0)
            {
                var topCard = ps.Grave[^1];
                var gc = new GwintCard(topCard.Def, 80, null, topCard, height: 107);
                gc.MouseEnter += (_, _) => SetPreview(topCard.Def, null);
                var gp = seat;
                gc.Tag = "grave-pile";
                gc.MouseLeftButtonUp += (_, e) => { e.Handled = true; _graveView = gp; Refresh(); };
                Put(_board, gc, px, 40);
                Put(_board, Outlined(ps.Grave.Count.ToString(), 18, Brushes.White, 30), px + 54, 124);
            }
            if (ps.Deck.Count > 0)
            {
                var deck = Pic("deck_" + ps.Faction, 60, 110);
                if (deck.Source != null) Put(_board, deck, px + 94, 36);
                else Put(_board, new Rectangle { Width = 56, Height = 104, Fill = Theme.FactionColor(ps.Faction), RadiusX = 4, RadiusY = 4, Opacity = 0.8 }, px + 94, 36);
                Put(_board, Outlined(ps.Deck.Count.ToString(), 20, Brushes.White, 60), px + 94, 150);
            }
        }
    }

    // ---------- Başlangıç: kim başlasın / zar ----------

    private UIElement ChooseFirstPanel3()
    {
        var sp = new StackPanel { MinWidth = 700 };
        sp.Children.Add(Title(G.Players[Me].Faction == "scoiatael" ? "Scoia'tael yeteneği" : "Kim başlasın?"));
        var sub = Txt("Zar atıldı; ama ilk turu kimin oynayacağını sen seçersin.", 22, Theme.TextDim, false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 6, 0, 22);
        sp.Children.Add(sub);
        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var seat in new[] { Me }.Concat(_s.OppSeats))
        {
            var s = seat;
            var btn = Ui.Button(seat == Me ? "Ben başlayayım" : $"{_s.SeatName(seat)} başlasın", () => { _s.Apply(new ChooseFirst(Me, s)); AfterHumanAction(); }, primary: seat == Me);
            btn.FontSize = 20;
            btn.Margin = new Thickness(7, 0, 7, 0);
            btn.Tag = "choose-first";
            b.Children.Add(btn);
        }
        sp.Children.Add(b);
        return sp;
    }

    /// <summary>Üç kişilik başlangıç zarı süresi (ms): her atış ~1,5 sn, sonunda sonuç.</summary>
    private int DiceDurationMs => G.DiceRolls.Count * 1500 + 1400;

    /// <summary>Üç zar masaya atılır (sunucunun tohumundan motorun attığı değerler): eşitler yeniden atar, en yüksek başlar.</summary>
    private void PlayDice()
    {
        var layer = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        Panel.SetZIndex(layer, 60);
        _fx.Children.Add(layer);
        _coinLayer = layer;
        var dim = new Rectangle { Width = W, Height = H, Fill = Theme.B("#99000000"), Opacity = 0 };
        layer.Children.Add(dim);
        dim.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        var order = new[] { Me }.Concat(_s.OppSeats).ToList();   // ekranda: ben, sol rakip, sağ rakip
        const double size = 130, gap = 300, cy = 420;
        var dice = new Dictionary<int, (Border Box, Canvas Face, TextBlock Name)>();
        for (var i = 0; i < order.Count; i++)
        {
            var seat = order[i];
            var cx = 1110 + (i - 1) * gap;
            var face = new Canvas { Width = size - 16, Height = size - 16 };
            var box = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(22), Background = Theme.B("#efe4cc"),
                BorderBrush = seat == Me ? Theme.Gold : Theme.B(OppTint[HalfOf(seat)]), BorderThickness = new Thickness(4), Padding = new Thickness(8),
                Child = face, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 6, Opacity = 0.7 }
            };
            Put(layer, box, cx - size / 2, cy - size / 2);
            var name = Outlined(seat == Me ? "Sen" : _s.SeatName(seat), 26, Theme.B("#f0d890"), 280);
            name.TextAlignment = TextAlignment.Center;
            Put(layer, name, cx - 140, cy + size / 2 + 16);
            dice[seat] = (box, face, name);
        }
        var result = Outlined("", 40, Theme.B("#f0d890"), 1000);
        result.TextAlignment = TextAlignment.Center;
        Put(layer, result, 1110 - 500, cy + size / 2 + 80);

        var rnd = new Random(G.Seed);
        for (var phase = 0; phase < G.DiceRolls.Count; phase++)
        {
            var roll = G.DiceRolls[phase];
            var start = phase * 1500;
            var last = phase == G.DiceRolls.Count - 1;
            Fx.After(start, () =>
            {
                if (!IsLoaded && !_effects) return;
                Sfx.Play("coin_toss");
                if (phase > 0) result.Text = "Eşitlik — yeniden atılıyor";
                // Yuvarlanma: yüzler hızla değişir, zar döner
                for (var k = 0; k < 9; k++)
                {
                    var kk = k;
                    Fx.After(kk * 95, () =>
                    {
                        foreach (var (seat, d) in dice)
                        {
                            if (roll[seat] == 0) continue;
                            DrawPips(d.Face, rnd.Next(6) + 1, size - 16);
                            ((RotateTransform)d.Box.RenderTransform).Angle = (kk % 2 == 0 ? 1 : -1) * (18 - kk * 2);
                        }
                    });
                }
                Fx.After(900, () =>
                {
                    foreach (var (seat, d) in dice)
                    {
                        ((RotateTransform)d.Box.RenderTransform).Angle = 0;
                        if (roll[seat] == 0) { d.Box.Opacity = 0.35; continue; }
                        d.Box.Opacity = 1;
                        DrawPips(d.Face, roll[seat], size - 16);
                        Fx.Pulse(d.Box);
                    }
                    if (last)
                    {
                        result.Text = G.FirstIsChosen ? $"En yüksek: {_s.SeatName(G.CoinFirst)} — Scoia'tael başlayanı seçecek"
                            : G.CoinFirst == Me ? "Sen başlıyorsun" : $"{_s.SeatName(G.CoinFirst)} başlıyor";
                        Fx.PlayFrames(_fx, "burst", new Point(1110 + (order.IndexOf(G.CoinFirst) - 1) * gap, cy), 300, 300, 36);
                    }
                });
            });
        }
        Fx.After(DiceDurationMs, () =>
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) => _fx.Children.Remove(layer);
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    private static void DrawPips(Canvas face, int value, double size)
    {
        face.Children.Clear();
        var a = size * 0.22; var m = size / 2; var b = size - a;
        var spots = value switch
        {
            1 => new[] { (m, m) },
            2 => new[] { (a, a), (b, b) },
            3 => new[] { (a, a), (m, m), (b, b) },
            4 => new[] { (a, a), (b, a), (a, b), (b, b) },
            5 => new[] { (a, a), (b, a), (m, m), (a, b), (b, b) },
            _ => new[] { (a, a), (b, a), (a, m), (b, m), (a, b), (b, b) },
        };
        var r = size * 0.09;
        foreach (var (x, y) in spots)
        {
            var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = value == 1 ? Theme.B("#9a2a1a") : Theme.B("#2a1c0e") };
            Canvas.SetLeft(dot, x - r);
            Canvas.SetTop(dot, y - r);
            face.Children.Add(dot);
        }
    }

    // ---------- Oyun sonu ----------

    private UIElement GameOverPanel3()
    {
        var sp = new StackPanel { MinWidth = 1000, HorizontalAlignment = HorizontalAlignment.Center };
        var winner = _s.WinnerSeat;
        var (title, color, glow) = winner == Me
            ? ("Kazandın!", Theme.B("#d8bd78"), Color.FromRgb(150, 110, 40))
            : winner == null
                ? ("Berabere", Theme.B("#b8ad94"), Color.FromRgb(110, 105, 92))
                : ($"{_s.SeatName(winner.Value)} kazandı", Theme.B("#a8503f"), Color.FromRgb(110, 30, 22));
        var t = Txt(title, 92, color, true, TextAlignment.Center);
        t.Effect = new DropShadowEffect { Color = glow, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 };
        sp.Children.Add(t);

        var seats = new[] { Me }.Concat(_s.OppSeats).ToList();
        var wins = string.Join("   ·   ", seats.Select(s => $"{(s == Me ? "Sen" : _s.SeatName(s))} {G.Players[s].Wins}"));
        var sub = Txt("Raund galibiyeti   " + wins, 28, Theme.B("#e8dcc4"), false, TextAlignment.Center);
        sub.Margin = new Thickness(0, 2, 0, 4);
        sp.Children.Add(sub);
        if (_s.ExternalResult is { Reason: not "normal" } ext)
            sp.Children.Add(Txt(ext.Reason == "resigned" ? "Bir oyuncu teslim oldu / ayrıldı." : "Bir oyuncunun bağlantısı koptu.", 20, Theme.TextDim, false, TextAlignment.Center, 900, true));
        sp.Children.Add(Ornate.Rule(620, 14));

        var table = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var w in new[] { 130.0, 170, 170, 170, 280 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        void Cell(string text, int row, int col, Brush fg, double size, bool bold)
        {
            var c = Txt(text, size, fg, bold, TextAlignment.Center);
            c.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            table.Children.Add(c);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell("Raund", 0, 0, Theme.TextDim, 20, true);
        for (var i = 0; i < 3; i++) Cell(seats[i] == Me ? "Sen" : _s.SeatName(seats[i]), 0, i + 1, i == 0 ? Theme.TextDim : Theme.B(OppTint[i - 1]), 20, true);
        Cell("Sonuç", 0, 4, Theme.TextDim, 20, true);
        var rowIx = 1;
        foreach (var r in G.History)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(r.Round.ToString(), rowIx, 0, Theme.Text, 28, false);
            for (var i = 0; i < 3; i++)
            {
                var won = r.Winner == seats[i];
                Cell(r.Score(seats[i]).ToString(), rowIx, i + 1, won ? Theme.B("#d8bd78") : Theme.TextDim, 32, won);
            }
            Cell(r.Winner == null ? "Kimse" : r.Winner == Me ? "Sen kazandın" : $"{_s.SeatName(r.Winner.Value)}", rowIx, 4,
                r.Winner == null ? Theme.TextDim : r.Winner == Me ? Theme.B("#c9b06c") : Theme.B("#9c5446"), 24, true);
            rowIx++;
        }
        sp.Children.Add(table);

        var b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, 0) };
        var again = Ui.Button("Rövanş iste", () =>
        {
            _s.Link?.RequestRematch();
            _hint = "Rövanş isteği gönderildi; ikisinin de kabul etmesi bekleniyor.";
            Refresh();
        }, primary: true);
        var menu = Ui.Button(_s.ExitLabel ?? "Menü", _toMenu);
        again.FontSize = 26; menu.FontSize = 26;
        again.Padding = new Thickness(50, 14, 50, 14); menu.Padding = new Thickness(50, 14, 50, 14);
        menu.Margin = new Thickness(18, 0, 0, 0);
        if (_s.IsRemote) b.Children.Add(again);
        b.Children.Add(menu);
        sp.Children.Add(b);
        if (!string.IsNullOrEmpty(_hint))
        {
            var h = Txt(_hint, 20, Theme.B("#c9b88a"), false, TextAlignment.Center, 900, true);
            h.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(h);
        }
        return sp;
    }
}
