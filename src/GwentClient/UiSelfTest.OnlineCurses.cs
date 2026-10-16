using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Online lanetler (gerçek ağ, iki bekleme odası arayüzü): oda kuran arayüzden lanet seçer, konukta seçim düğmesi yoktur ama lanetleri görür;
    /// ikisi hazır olunca maç lanetlerle başlar, Ayna desteleri takas eder ve oyun masasında lanet şeridi çıkar. <c>--selftest-online-curses</c>
    /// </summary>
    public static int RunOnlineCursesCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] online lanet: " + what); } }
        OnlineController? hostC = null, guestC = null;
        try
        {
            (hostC, var herr) = Await(() => OnlineController.HostAsync("Geralt", false, lan: false));
            if (hostC == null) throw new InvalidOperationException("Oda kurulamadı: " + herr);
            var host = hostC.Link;
            PumpUntil(() => host.State?.Code is { Length: 5 }, "oda durumu", host);
            (guestC, var gerr) = Await(() => OnlineController.JoinAsync($"127.0.0.1:{hostC.Server!.Port}", host.State!.Code, "Yennefer"));
            if (guestC == null) throw new InvalidOperationException("Katılınamadı: " + gerr);
            var guest = guestC.Link;
            PumpUntil(() => host.State?.Players.Count == 2 && guest.State?.Players.Count == 2, "iki oyuncu", host, guest);

            var hl = new LobbyView(hostC, () => Task.CompletedTask);
            var gl = new LobbyView(guestC, () => Task.CompletedTask);
            Layout(gl);
            Raise(FindAll<Border>(gl, b => (string?)b.Tag == "lobby-faction:skellige").First(), UIElement.MouseLeftButtonUpEvent);
            PumpUntil(() => host.State!.Players.Any(p => p.Seat == 1 && p.Loadout?.Faction == "skellige"), "konuk seçimi", host, guest);
            Layout(gl);
            Check(!FindAll<ToggleButton>(gl, t => (t.Tag as string)?.StartsWith("lobby-curse:") == true).Any(), "konukta lanet düğmesi olmamalı");

            foreach (var id in new[] { GwentAI.Curses.Mirror, GwentAI.Curses.ShortCandle, GwentAI.Curses.EndlessWinter })
            {
                Layout(hl);
                var tb = FindAll<ToggleButton>(hl, t => (t.Tag as string) == "lobby-curse:" + id).First();
                tb.IsChecked = true;
                Raise(tb, ButtonBase.ClickEvent);
                PumpUntil(() => guest.State!.Curses?.Contains(id) == true, "lanet " + id, host, guest);
            }
            // Biri açılıp kapatılır: sunucuya giden liste güncel durumdan kurulur
            Layout(hl);
            var off = FindAll<ToggleButton>(hl, t => (t.Tag as string) == "lobby-curse:" + GwentAI.Curses.EndlessWinter).First();
            off.IsChecked = false;
            Raise(off, ButtonBase.ClickEvent);
            PumpUntil(() => guest.State!.Curses?.Count == 2, "lanet kapandı", host, guest);
            Layout(gl);
            Check(FindAll<TextBlock>(gl, t => (t.Tag as string) == "lobby-curses").Any(t => t.Text.Contains("Ayna") && t.Text.Contains("Kısa Mum")), "konuk lanetleri görmüyor");
            Check(FindAll<ToggleButton>(gl, t => (t.Tag as string) == "lobby-view-curse:" + GwentAI.Curses.Mirror).Any(t => t.IsChecked == true && !t.IsEnabled), "konukta kilitli lanet düğmesi işaretli değil");
            Check(FindAll<TextBlock>(gl, t => (t.Tag as string) == "lobby-foot").Any(t => t.Text.Contains("değiştirdi")), "konuğa kural değişikliği söylenmedi");
            Check(FindAll<TextBlock>(gl, t => (t.Tag as string) == "lobby-mirror-hint").Count() == 2, "Ayna ipuçları iki panelde yok");

            foreach (var view in new[] { hl, gl })
            {
                Layout(view);
                Click(FindAll<Button>(view, b => (b.Tag as string) == "lobby-ready").First());
            }
            PumpUntil(() => host.Match != null && guest.Match != null, "maç başladı", host, guest);
            Check(host.Setup!.Seat0.Faction == "skellige" && host.Setup.Seat1.Faction != "skellige", "Ayna desteleri takas etmedi");
            Check(guest.Match!.Game.Options.StartingHand == 8 && host.Match!.Game.Options.StartingHand == 8, "Kısa Mum uygulanmadı");
            Check(guest.Match.Config.MyFaction == host.Setup.Seat1.Faction, "konuğun oturumu takaslı desteyi görmüyor");

            var view2 = new GwintView(guest.Match, () => { }, _ => { });
            Layout(view2);
            var label = FindAll<TextBlock>(view2, t => (t.Tag as string) == "curse-label").FirstOrDefault();
            Check(label != null && label.Text.Contains("Ayna") && (label.ToolTip as string)?.Contains("takas") == true, "oyun masasında lanet şeridi yok ya da Ayna açıklaması yanlış");
            Console.WriteLine($"Online lanet: {failures} hata.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] online lanet: " + ex);
        }
        finally
        {
            foreach (var c in new[] { guestC, hostC })
                if (c != null) Task.Run(() => c.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        return failures == 0 ? 0 : 1;
    }
}
