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
/// Fraksiyon (kalkan rozetleri) + deste + lider (kart şeridi) seçici. Bekleme odası ve turnuva bekleme odası ortak kullanır.
/// Seçim değişince <see cref="Changed"/> tetiklenir; görünüm <see cref="Build"/> ile (durum değiştikçe) yeniden kurulur. Etkileşim etiketleri
/// (<c>lobby-faction:</c>, <c>lobby-deck:</c>, <c>lobby-leader:</c>) sınama tarafından bulunur.
/// </summary>
public sealed class LoadoutPicker : StackPanel
{
    private static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    private readonly Func<bool> _nextGen;
    public string Faction { get; private set; } = "northern_realms";
    public string LeaderId { get; private set; } = "";
    public string? DeckName { get; private set; }   // null = önerilen deste

    public event Action? Changed;

    /// <summary>Doluysa yalnız bu fraksiyon seçilebilir (turnuva kuralı); diğer rozetler soluk ve tıklanamaz.</summary>
    public string? LockedFaction { get; set; }

    public LoadoutPicker(Func<bool> nextGen)
    {
        _nextGen = nextGen;
        LeaderId = DefaultLeader(Faction);
    }

    private bool NextGen => _nextGen();

    private string DefaultLeader(string faction) =>
        CardCatalog.Default.Leaders(faction).First(l => l.Edition != "nextgen" || NextGen).Id;

    /// <summary>Seçimin sunucuya gidecek hâli (kayıtlı deste seçiliyse kartları ve lideri oradan).</summary>
    public LoadoutDto ToLoadout()
    {
        List<string>? cards = null;
        var leader = LeaderId;
        if (DeckName != null)
        {
            var saved = DeckStore.For(Faction).FirstOrDefault(d => d.Name == DeckName);
            if (saved != null) { cards = saved.CardIds.ToList(); leader = saved.LeaderId; }
        }
        return new LoadoutDto(Faction, leader, cards, Settings.Portrait);
    }

    private static TextBlock Label(string t)
    {
        var l = Ui.Text(Ornate.Spaced(t), 13, Theme.B("#a8905a"), true);
        l.Margin = new Thickness(0, 14, 0, 8);
        return l;
    }

    public void Build()
    {
        Children.Clear();
        if (LockedFaction != null && Faction != LockedFaction)
        {
            Faction = LockedFaction;
            LeaderId = DefaultLeader(Faction);
            DeckName = null;
        }

        // Fraksiyon: kalkan rozetleri (seçili olan altın çerçeveli ve parlar)
        Children.Add(Label("FRAKSİYON"));
        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var f in Factions)
        {
            var key = f;
            var sel = f == Faction;
            var blocked = LockedFaction != null && f != LockedFaction;
            var cell = new Grid { Width = 112, Margin = new Thickness(0, 0, 10, 0), Opacity = blocked ? 0.25 : 1 };
            cell.Children.Add(new ChamferFrame
            {
                Fill = sel ? Theme.B("#3a2c14") : Theme.B("#1a130b"), Edge = sel ? Ornate.GoldEdge : Theme.B("#4a3b28"), Inner = sel ? Theme.B("#88e0bf6a") : null, Cut = 10, Inset = 4
            });
            var inner = new StackPanel { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
            var shield = Assets.Ui("shield_" + f);
            if (shield != null) inner.Children.Add(new System.Windows.Controls.Image { Source = shield, Width = 66, Height = 66, Opacity = sel ? 1 : 0.7 });
            else inner.Children.Add(new Ellipse { Width = 60, Height = 60, Fill = Theme.FactionColor(f), Opacity = sel ? 1 : 0.7 });
            var tn = Ui.Text(Texts.FactionShort(f), 14, sel ? Theme.B("#f0d890") : Theme.B("#9c927a"), true, TextAlignment.Center);
            tn.Margin = new Thickness(0, 5, 0, 0);
            inner.Children.Add(tn);
            cell.Children.Add(inner);
            if (sel) cell.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 200, 90), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 };
            var b = new Border { Child = cell, Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "lobby-faction:" + f, ToolTip = Texts.Faction(f) };
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (key == Faction || blocked) return;
                Sfx.Play("cursor_movement");
                Faction = key;
                LeaderId = DefaultLeader(key);
                DeckName = null;
                Changed?.Invoke();
            };
            badges.Children.Add(b);
        }
        Children.Add(badges);

        // Deste
        Children.Add(Label("DESTE"));
        var decks = new WrapPanel();
        void AddDeck(string? name, string title, string sub, bool enabled = true)
        {
            var c = new StackPanel();
            c.Children.Add(Ui.Text(title, 15, enabled ? Theme.Text : Theme.TextDim, true));
            c.Children.Add(Ui.Text(sub, 12, Theme.TextDim));
            var tb = new ToggleButton
            {
                Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = DeckName == name, IsEnabled = enabled,
                Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(16, 8, 16, 8), MinWidth = 180, Tag = "lobby-deck:" + (name ?? "@rec")
            };
            tb.Click += (_, _) => { DeckName = name; Changed?.Invoke(); };
            decks.Children.Add(tb);
        }
        AddDeck(null, "Önerilen deste", "kahramanlar + en güçlü birimler");
        foreach (var d in DeckStore.For(Faction))
        {
            var errs = DeckValidator.Validate(d.ToDeck(), new GameOptions { NextGen = NextGen });
            AddDeck(d.Name, d.Name, errs.Count == 0 ? $"{d.CardIds.Count} kart" : "geçersiz", errs.Count == 0);
        }
        Children.Add(decks);

        // Lider (kayıtlı deste seçiliyse destenin lideri kullanılır): kart şeridi
        if (DeckName == null)
        {
            Children.Add(Label("LİDER"));
            var strip = new WrapPanel();
            CardDef? chosen = null;
            foreach (var l in CardCatalog.Default.Leaders(Faction).Where(l => l.Edition != "nextgen" || NextGen))
            {
                var leader = l;
                var sel = leader.Id == LeaderId;
                if (sel) chosen = leader;
                var c = new StackPanel { Width = 118 };
                c.Children.Add(new GwintCard(leader, 118, null, null, height: 118 * GwintCard.Aspect) { IsHitTestVisible = false, Opacity = sel ? 1 : 0.7 });
                var nm = Ui.Text(leader.Name.Contains(':') ? leader.Name[(leader.Name.IndexOf(':') + 1)..].Trim() : leader.Name, 12, sel ? Theme.B("#f0d890") : Theme.TextDim, true, TextAlignment.Center, true);
                nm.Margin = new Thickness(0, 5, 0, 0);
                c.Children.Add(nm);
                var tb = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("GToggle"), Content = c, IsChecked = sel,
                    Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(6, 6, 6, 6), Tag = "lobby-leader:" + leader.Id, ToolTip = leader.Name
                };
                tb.Click += (_, _) => { LeaderId = leader.Id; Changed?.Invoke(); };
                strip.Children.Add(tb);
            }
            Children.Add(strip);
            if (chosen != null)
            {
                var d = Ui.Text(Texts.LeaderText(chosen), 15, Theme.B("#c9c0aa"), false, TextAlignment.Left, true);
                d.Margin = new Thickness(2, 4, 0, 0);
                d.FontStyle = FontStyles.Italic;
                Children.Add(d);
            }
        }
    }
}
