using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Online draft (gerçek ağ, iki bekleme odası arayüzü): oda kuran "Draft maçı"nı açar; iki taraf arayüzden fraksiyon, lider ve 25 kart seçer;
    /// rakibin ilerlemesi görünür; ikisi hazır olunca maç draft destelerinin aynısıyla başlar. <c>--selftest-online-draft</c>
    /// </summary>
    public static int RunOnlineDraftCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] online draft: " + what); } }
        OnlineController? hostC = null, guestC = null;
        try
        {
            (hostC, var herr) = Await(() => OnlineController.HostAsync("Geralt", false, lan: false));
            if (hostC == null) throw new InvalidOperationException("Oda kurulamadı: " + herr);
            var host = hostC.Link;
            PumpUntil(() => host.State?.Code is { Length: 5 }, "oda durumu", host);
            var code = host.State!.Code;
            (guestC, var gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{hostC.Server!.Port}", code, "Yennefer"));
            if (guestC == null) throw new InvalidOperationException("Katılınamadı: " + gerr);
            var guest = guestC.Link;
            PumpUntil(() => host.State?.Players.Count == 2 && guest.State?.Players.Count == 2, "iki oyuncu", host, guest);

            var hl = new LobbyView(hostC, () => Task.CompletedTask);
            var gl = new LobbyView(guestC, () => Task.CompletedTask);
            Layout(hl);
            Layout(gl);
            Check(!FindAll<ToggleButton>(gl, t => (t.Tag as string) == "lobby-draft").Any(), "konukta draft seçeneği olmamalı");
            var opt = FindAll<ToggleButton>(hl, t => (t.Tag as string) == "lobby-draft").First();
            opt.IsChecked = true;
            Raise(opt, ButtonBase.ClickEvent);
            PumpUntil(() => host.State!.Draft && guest.State!.Draft, "draft açıldı", host, guest);

            var rng = new Random(17);
            foreach (var (view, link, faction) in new[] { (hl, host, "monsters"), (gl, guest, "nilfgaard") })
            {
                Layout(view);
                Raise(FindAll<FrameworkElement>(view, x => (x.Tag as string) == "odraft-faction:" + faction).First(), UIElement.MouseLeftButtonUpEvent);
                PumpUntil(() => link.State!.YourDraft?.Faction == faction, "fraksiyon", host, guest);
                Layout(view);
                Raise(FindAll<FrameworkElement>(view, x => (x.Tag as string)?.StartsWith("odraft-leader:") == true).First(), UIElement.MouseLeftButtonUpEvent);
                PumpUntil(() => link.State!.YourDraft?.LeaderId != null, "lider", host, guest);
                for (var k = 0; k < GwentAI.Draft.DraftLogic.DeckSize; k++)
                {
                    Layout(view);
                    var offers = FindAll<FrameworkElement>(view, x => (x.Tag as string) == "odraft-card").ToList();
                    var before = link.State!.YourDraft!.Picks.Count;
                    Raise(offers[rng.Next(offers.Count)], UIElement.MouseLeftButtonUpEvent);
                    PumpUntil(() => link.State!.YourDraft!.Picks.Count == before + 1, "kart seçimi", host, guest);
                }
            }
            PumpUntil(() => host.State!.Players.First(p => p.Seat == 1).DraftPicks == GwentAI.Draft.DraftLogic.DeckSize, "rakibin ilerlemesi", host, guest);
            var hostDeck = host.State!.YourDraft!.Picks.ToList();
            var guestDeck = guest.State!.YourDraft!.Picks.ToList();
            foreach (var view in new[] { hl, gl })
            {
                Layout(view);
                Click(FindAll<Button>(view, b => (b.Tag as string) == "lobby-ready").First());
            }
            PumpUntil(() => host.Match != null && guest.Match != null, "maç başladı", host, guest);
            Check(host.Setup!.Seat0.CardIds!.SequenceEqual(hostDeck) && host.Setup.Seat1.CardIds!.SequenceEqual(guestDeck), "maç draft desteleriyle başlamadı");
            Check(host.Setup.Seat0.Faction == "monsters" && host.Setup.Seat1.Faction == "nilfgaard", "fraksiyonlar");
            Console.WriteLine($"Online draft: {failures} hata.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] online draft: " + ex);
        }
        finally
        {
            foreach (var c in new[] { guestC, hostC })
                if (c != null) Task.Run(() => c.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        return failures == 0 ? 0 : 1;
    }
}
