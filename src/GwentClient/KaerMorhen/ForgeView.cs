using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GwentCore;

namespace GwentClient.KaerMorhen;

/// <summary>
/// Demirci (TASARIM.md §4.3): destedeki kartlara rün takılır. Karta tıklamak sıradaki rüne geçirir (rünsüz → Svarog → … → rünsüz).
/// Bir destede en fazla <see cref="RuneIds.MaxPerDeck"/> rün; her değişiklik hemen kaydedilir.
/// </summary>
public sealed class ForgeView : Grid
{
    private readonly Deck _deck;
    private readonly string _key;
    private readonly Action _close;
    private readonly List<CardRune> _runes;
    private readonly WrapPanel _cards = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = Ui.Text("", 16, Theme.B("#e8c970"), true, TextAlignment.Center);
    private readonly int _max;
    private readonly IReadOnlyList<string> _allowed;

    /// <param name="maxRunes">Takılabilecek rün sayısı (kalede demirci seviyesi).</param>
    /// <param name="allowed">Açık rünler (kalede demirci seviyesi); null = hepsi.</param>
    public ForgeView(Deck deck, string key, Action close, int maxRunes = RuneIds.MaxPerDeck, IReadOnlyList<string>? allowed = null)
    {
        _deck = deck;
        _key = key;
        _close = close;
        _max = maxRunes;
        _allowed = allowed ?? RuneIds.All;
        _runes = ForgeStore.For(key, deck).Where(r => _allowed.Contains(r.Rune)).Take(_max).ToList();
        Background = Theme.B("#e8080503");

        var root = new StackPanel { Width = 1240 };
        root.Children.Add(Ornate.Heading("Demirci", "Kartlarına rün tak. Rünler yalnız Kaer Morhen savaş alanında geçerlidir.", 38));

        // Rünler: simge, ad, kural
        var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        foreach (var r in GwentAI.Forge.All)
        {
            var open = _allowed.Contains(r.Id);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Width = 405, Margin = new Thickness(0, 3, 6, 3), Opacity = open ? 1 : 0.4 };
            sp.Children.Add(RuneBadge(r.Id, 34));
            var txt = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(Ui.Text(r.Name + (open ? "" : "  · kilitli (Demirciyi yükselt)"), 15, Theme.B(r.Color), true));
            txt.Children.Add(Ui.Text(r.Text, 12, Theme.TextDim));
            sp.Children.Add(txt);
            legend.Children.Add(sp);
        }
        root.Children.Add(legend);
        root.Children.Add(Ornate.Caption("Destendeki birimler — tıkla: sıradaki rün"));
        root.Children.Add(new ScrollViewer { Content = _cards, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 6) });
        _status.Margin = new Thickness(0, 6, 0, 10);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var clear = Ui.Button("Rünleri sök", () => { _runes.Clear(); Changed(); });
        clear.Tag = "forge-clear";
        clear.Margin = new Thickness(0, 0, 14, 0);
        clear.Padding = new Thickness(26, 10, 26, 10);
        buttons.Children.Add(clear);
        var done = Ui.Button("Tamam", () => _close(), primary: true);
        done.Tag = "forge-done";
        done.Padding = new Thickness(40, 10, 40, 10);
        buttons.Children.Add(done);
        root.Children.Add(buttons);

        var panel = Ornate.Panel(root, new Thickness(40, 30, 40, 30), 20, 54);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(panel);
        Build();
    }

    /// <summary>Rün nişanı: renkli çerçeveli yuvarlak, içinde rün harfi.</summary>
    public static FrameworkElement RuneBadge(string rune, double size)
    {
        var info = GwentAI.Forge.Get(rune);
        var g = new Grid { Width = size, Height = size, ToolTip = $"{info.Name} rünü: {info.Text}" };
        g.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Theme.B("#e8140e08"), Stroke = Theme.B(info.Color), StrokeThickness = Math.Max(1.5, size * 0.07) });
        var t = Ui.Text(info.Glyph, size * 0.56, Theme.B(info.Color), true, TextAlignment.Center);
        t.FontFamily = new FontFamily("Segoe UI Historic, Segoe UI Symbol, Segoe UI");
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        return g;
    }

    private void Build()
    {
        _cards.Children.Clear();
        var cat = CardCatalog.Default;
        var ids = _deck.CardIds.Distinct().Select(cat.Get).Where(RuneIds.CanTake)
            .OrderByDescending(d => d.Strength).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        foreach (var def in ids)
        {
            var mine = _runes.Where(r => r.CardId == def.Id).ToList();
            var copies = _deck.CardIds.Count(x => x == def.Id);
            for (var k = 0; k < copies; k++)
            {
                var rune = k < mine.Count ? mine[k].Rune : null;
                var cell = new Grid { Width = 118, Height = 158, Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand, Tag = "forge-card:" + def.Id };
                var card = new GwintCard(def, 118, def.Strength, height: 158, highlight: rune != null);
                card.IsHitTestVisible = false;
                cell.Children.Add(card);
                if (rune != null)
                {
                    var b = RuneBadge(rune, 40);
                    b.HorizontalAlignment = HorizontalAlignment.Right;
                    b.VerticalAlignment = VerticalAlignment.Top;
                    b.Margin = new Thickness(0, 4, 4, 0);
                    cell.Children.Add(b);
                }
                var id = def.Id;
                var copy = k;
                cell.MouseLeftButtonUp += (_, _) => Cycle(id, copy);
                _cards.Children.Add(cell);
            }
        }
        var names = _runes.Select(r => $"{cat.Get(r.CardId).Name}: {GwentAI.Forge.Get(r.Rune).Name}");
        _status.Text = _max == 0 ? "Demirci henüz kurulmadı: kalede Demirciyi onarınca rün takabilirsin." : $"Rünler {_runes.Count}/{_max}" + (_runes.Count > 0 ? " — " + string.Join(" · ", names) : "");
    }

    /// <summary>Kartın (o kimliğin <paramref name="copy"/>. kopyası) rününü sıradakine geçirir; sınır doluysa yeni rün takılmaz.</summary>
    public void Cycle(string cardId, int copy = 0)
    {
        var indices = _runes.Select((r, i) => (r, i)).Where(x => x.r.CardId == cardId).Select(x => x.i).ToList();
        if (copy >= indices.Count)
        {
            if (_runes.Count >= _max)
            {
                _status.Text = _max == 0 ? "Demirci henüz kurulmadı." : $"En fazla {_max} rün takabilirsin. Önce bir kartın rününü sök (rünsüze gelene dek tıkla).";
                return;
            }
            _runes.Add(new CardRune(cardId, _allowed[0]));
        }
        else
        {
            var at = indices[copy];
            var next = _allowed.ToList().IndexOf(_runes[at].Rune) + 1;
            if (next >= _allowed.Count) _runes.RemoveAt(at);
            else _runes[at] = _runes[at] with { Rune = _allowed[next] };
        }
        Changed();
    }

    private void Changed()
    {
        ForgeStore.Save(_key, _runes);
        Sfx.Play("dummy");
        Build();
    }
}
