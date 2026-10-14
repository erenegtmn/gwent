using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GwentAI;
using GwentAI.Career;
using GwentCore;

namespace GwentClient;

/// <summary>
/// Collect 'Em All turnuvasında turlar arası ekran. Turnuva sürerken haritaya, desteye ve tüccara dönülmez: yalnız sıradaki tura geçilir
/// ya da turnuvadan çekilinir (çekilen elenmiş sayılır, katılım ücreti geri gelmez). Uygulama kapanıp açılınca da buradan sürer.
/// </summary>
public sealed class CareerTournamentView : Grid
{
    private readonly CareerSave _s;
    private readonly CareerOpponent _o;
    private readonly Action _next;
    private readonly Action _withdraw;
    private readonly string? _last;
    private readonly ContentControl _body = new();
    private bool _confirmQuit;

    public CareerTournamentView(CareerSave save, Action next, Action withdraw, string? lastResult = null)
    {
        _s = save;
        _o = CareerWorld.Opponent(save.Tournament!.Opponent);
        _next = next;
        _withdraw = withdraw;
        _last = lastResult;
        var region = CareerWorld.Region(_o.Region);
        var content = new Grid { Width = 1920, Height = 1080 };
        content.Children.Add(_body);
        Children.Add(Ornate.Stage(new Viewbox { Stretch = Stretch.Uniform, Child = content }, region.Pano, 0.7));
        Focusable = true;
        Loaded += (_, _) => Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _confirmQuit) { _confirmQuit = false; Draw(); } };
        Draw();
    }

    private void Draw()
    {
        var t = _s.Tournament!;
        var sp = new StackPanel { Width = 1160 };
        sp.Children.Add(Ornate.Heading(_o.Name.ToUpperInvariant(), _o.Title, 50, HorizontalAlignment.Center));   // İngilizce ad: "HIGH", "HİGH" değil
        sp.Children.Add(Ornate.Rule(640, 14));
        if (_last != null)
        {
            var last = Ui.Text(_last, 24, Theme.B("#f0d890"), true, TextAlignment.Center, true);
            last.Tag = "tour-last";
            last.Margin = new Thickness(0, 0, 0, 14);
            sp.Children.Add(last);
        }

        // Turlar: geçilenler işaretli, sıradaki parlak, sonrakiler gizli
        var rounds = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 10) };
        for (var i = 0; i < t.Decks.Count; i++)
        {
            var deck = NpcDecks.Get(t.Decks[i], _o.Level);
            var done = i < t.Round;
            var next = i == t.Round;
            var col = new StackPanel { Width = 210, Margin = new Thickness(10, 0, 10, 0) };
            var face = Portraits.Medallion(done || next ? deck.LeaderId : Portraits.FacePrefix + "none", next ? 150 : 118, glow: next);
            face.HorizontalAlignment = HorizontalAlignment.Center;
            face.Opacity = done ? 0.55 : next ? 1 : 0.75;
            col.Children.Add(face);
            var label = i == t.Decks.Count - 1 ? "Final" : $"{i + 1}. tur";
            col.Children.Add(Ui.Text(label, 20, next ? Theme.B("#f0d890") : Theme.TextDim, true, TextAlignment.Center));
            col.Children.Add(Ui.Text(done ? "✓ kazanıldı" : next ? Texts.Faction(deck.Faction) : "?", 16, done ? Theme.Good : Theme.Text, false, TextAlignment.Center, true));
            rounds.Children.Add(col);
        }
        sp.Children.Add(rounds);

        // Sıradaki rakip ve kilitli deste
        var nextDeck = NpcDecks.Get(t.Decks[t.Round], _o.Level);
        var info = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        info.Children.Add(Ui.Text($"Sıradaki: {CardCatalog.Default.Get(nextDeck.LeaderId).Name} · {Texts.Faction(nextDeck.Faction)} · {(_o.Ai switch { Difficulty.Easy => "Kolay", Difficulty.Hard => "Zor", _ => "Normal" })}",
            20, Theme.Text, false, TextAlignment.Center));
        var mine = _s.ActiveDeck();
        if (mine != null)
            info.Children.Add(Ui.Text($"Desten (turnuva boyunca kilitli): {Texts.Faction(mine.Faction)} · {CardCatalog.Default.Get(mine.LeaderId).Name} · {mine.CardIds.Count} kart",
                17, Theme.TextDim, false, TextAlignment.Center));
        info.Children.Add(Ui.Text($"Şampiyona {(_s.Beaten(_o.Id) ? _o.Prize / 2 : _o.Prize)} Crown ve benzersiz bir kart.", 17, Theme.B("#d8bd78"), false, TextAlignment.Center));
        sp.Children.Add(info);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        if (!_confirmQuit)
        {
            var go = Ui.Button(t.Round == t.Decks.Count - 1 ? "Finale çık" : $"{t.Round + 1}. tura geç", _next, primary: true);
            go.FontSize = 26;
            go.Padding = new Thickness(54, 14, 54, 14);
            go.Tag = "tour-next";
            bar.Children.Add(go);
            var quit = Ui.Button("Turnuvadan çekil", () => { _confirmQuit = true; Draw(); });
            quit.FontSize = 20;
            quit.Padding = new Thickness(30, 12, 30, 12);
            quit.Margin = new Thickness(20, 0, 0, 0);
            quit.Tag = "tour-quit";
            bar.Children.Add(quit);
        }
        else
        {
            var q = Ui.Text("Çekilirsen elenmiş sayılırsın; katılım ücreti geri gelmez.", 20, Theme.B("#f0c0b0"), true, TextAlignment.Center);
            q.VerticalAlignment = VerticalAlignment.Center;
            q.Margin = new Thickness(0, 0, 20, 0);
            bar.Children.Add(q);
            var yes = Ui.Button("Evet, çekil", _withdraw);
            yes.FontSize = 20;
            yes.Padding = new Thickness(26, 10, 26, 10);
            yes.Tag = "tour-quit-confirm";
            bar.Children.Add(yes);
            var no = Ui.Button("Vazgeç", () => { _confirmQuit = false; Draw(); }, primary: true);
            no.FontSize = 20;
            no.Padding = new Thickness(26, 10, 26, 10);
            no.Margin = new Thickness(12, 0, 0, 0);
            bar.Children.Add(no);
        }
        sp.Children.Add(bar);

        var panel = Ornate.Panel(sp, new Thickness(60, 44, 60, 48));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        _body.Content = panel;
    }
}
