using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GwentClient.HanLigi;
using HanLigi;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Online Han Ligi: iki gerçek arayüz (host + konuk) aynı odada; bekleme odasında oyuncu sayısı ve "Hazırım" tıklanır, boş
    /// koltukları botlar doldurur; maç iki arayüzde de yalnız etiketli öğelere rastgele tıklanarak oynanır. Her adımda sunucu ve
    /// iki istemcinin kopyası birebir aynı olmalı; maç bitmeli. <paramref name="net"/>: gerçek SignalR (yerel Kestrel).
    /// </summary>
    public static int RunOnlineHan(int games, bool net)
    {
        var failures = 0;
        var rounds = 0;
        GwentHost.LocalServer? server = net ? GwentHost.LocalServer.StartAsync().GetAwaiter().GetResult() : null;
        for (var g = 1; g <= games; g++)
        {
            try { rounds += PlayOnlineHan(g, server); }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[HATA] online Han {g}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        if (server != null) Task.Run(() => server.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        Console.WriteLine($"Online Han Ligi{(net ? " (SignalR, gerçek ağ)" : "")}: {games} maç, {rounds} round, {failures} hata.");
        return failures;
    }

    private static int PlayOnlineHan(int g, GwentHost.LocalServer? server)
    {
        var net = server != null;
        var svc = server?.Rooms ?? new GwentServer.RoomService(null, () => g * 7919 + 13);
        RoomLinkBase Link() => net ? NetLink.ConnectAsync(server!.LocalUrl).GetAwaiter().GetResult() : new RoomLink(svc);
        var host = Link();
        var guest = Link();
        var size = g % 3 == 0 ? 4 : 8;
        var r = host.CreateHanRoomAsync("Geralt", 8, "avatar_geralt").GetAwaiter().GetResult();
        if (!r.Ok) throw new Exception("oda kurulamadı: " + r.Error);
        PumpUntil(() => host.HanState != null, "Han odası", host);
        var code = host.HanState!.Code;
        var j = guest.JoinRoomAvatarAsync(code, "Yennefer", null).GetAwaiter().GetResult();
        if (!j.Ok || j.Value != "han:" + code) throw new Exception("katılınamadı: " + j.Error + " " + j.Value);
        PumpUntil(() => host.HanState!.Seats.Count == 2 && guest.HanState?.Seats.Count == 2, "iki oyuncu", host, guest);

        // ---- bekleme odası: gerçek arayüzler ----
        var hl = new HanLobbyView(host, Array.Empty<InviteOption>(), Array.Empty<string>(), () => Task.CompletedTask);
        var gl = new HanLobbyView(guest, Array.Empty<InviteOption>(), Array.Empty<string>(), () => Task.CompletedTask);
        Layout(hl); Layout(gl);
        Check(FindAll<Button>(gl, b => (b.Tag as string) == "hanlobby-size:" + size).All(b => !b.IsEnabled), "konuk oyuncu sayısını değiştirebiliyor");
        if (size != 8)
        {
            Raise(FindAll<Button>(hl, b => (b.Tag as string) == "hanlobby-size:" + size).First(), ButtonBase.ClickEvent);
            PumpUntil(() => guest.HanState!.Size == size, "oyuncu sayısı", host, guest);
            Layout(hl); Layout(gl);
        }
        foreach (var lv in new[] { hl, gl })
        {
            Raise(FindAll<Button>(lv, b => (b.Tag as string) == "hanlobby-ready").First(), ButtonBase.ClickEvent);
            PumpUntil(() => true, "hazır", host, guest);
        }
        PumpUntil(() => host.Han != null && guest.Han != null, "maç başlangıcı", host, guest);
        var hs = host.Han!;
        var gs = guest.Han!;
        Check(hs.M.Players.Count == size, $"koltuk sayısı {hs.M.Players.Count} ≠ {size}");
        Check(hs.M.Players.Count(p => p.IsBot) == size - 2, "boş koltuklar bot değil");
        var room = svc.FindHan(code)!;

        // ---- maç: iki arayüz rastgele tıklanır ----
        var hv = new HanView(hs, () => { }, () => { }, animate: false);
        var gv = new HanView(gs, () => { }, () => { }, animate: false);
        var rng = new Random(g * 101);
        var lastProgress = DateTime.UtcNow;
        var lastCount = -1;
        for (var step = 0; step < 60000 && room.Phase == GwentNet.RoomPhase.Playing; step++)
        {
            host.Pump(); guest.Pump();
            foreach (var v in new[] { hv, gv })
            {
                Layout(v);
                // online hazırlıkta "Hazır" düğmesi yok: hazırlık ortak süreyle biter
                Check(!FindAll<Button>(v, b => (b.Tag as string) == "han-ready").Any(), "online hazırlıkta Hazır düğmesi var");
                HanClick(v, rng);
                host.Pump(); guest.Pump();
            }
            if (room.Actions.Count != lastCount) { lastCount = room.Actions.Count; lastProgress = DateTime.UtcNow; }
            else if ((DateTime.UtcNow - lastProgress).TotalSeconds > 20)
                throw new Exception($"takıldı: round {room.Game!.M.Round} {room.Game.M.Phase}, bekleyen [{string.Join("; ", room.Game.PendingDefaults())}], host bekliyor {hs.Pending}, konuk {gs.Pending}; host ekranı {hv.DebugState}; konuk ekranı {gv.DebugState}");
            if (net) System.Threading.Thread.Sleep(1);
        }
        // ekranlar bekletilen son round sonunu bıraksın ("Devam" tıklanınca)
        for (var k = 0; k < 2000 && !(hs.Over && gs.Over); k++)
        {
            host.Pump(); guest.Pump();
            foreach (var v in new[] { hv, gv }) { Layout(v); HanClick(v, rng); }
        }
        Check(hs.Over && gs.Over, $"istemciler maç sonuna gelmedi (host bekliyor {hs.Pending}, konuk {gs.Pending})");
        Check(room.Phase == GwentNet.RoomPhase.Finished, "maç bitmedi");
        Check(hs.G.Fingerprint() == room.Game!.Fingerprint(), "host kopyası sunucudan ayrıştı");
        Check(gs.G.Fingerprint() == room.Game!.Fingerprint(), "konuk kopyası sunucudan ayrıştı");
        Check(room.Game.M.Players.Count(p => p.Place == 1) == 1, "tek kazanan yok");
        Task.Run(() => host.LeaveAsync()).GetAwaiter().GetResult();
        Task.Run(() => guest.LeaveAsync()).GetAwaiter().GetResult();
        if (host is NetLink hn) Task.Run(() => hn.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        if (guest is NetLink gn) Task.Run(() => gn.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        return room.Game.M.Round;

        void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
    }

    /// <summary>Han ekranında etiketli bir öğeye rastgele tıklar (sınamanın ortak adımı).</summary>
    private static void HanClick(HanView view, Random rng)
    {
        string T(FrameworkElement f) => f.Tag as string ?? "";
        var buttons = FindAll<Button>(view, b => b.IsEnabled && T(b).StartsWith("han-") && T(b) is not ("han-exit" or "han-help" or "han-menu" or "han-again" or "han-timed")).ToList();
        var cards = FindAll<HanCardView>(view, c => T(c).StartsWith("han-")).ToList();
        var modal = buttons.Where(b => T(b).StartsWith("han-potion:")).Cast<FrameworkElement>().Concat(cards.Where(c => T(c).StartsWith("han-reward:"))).ToList();
        FrameworkElement? pick;
        Button? ready = null;
        if (modal.Count > 0) pick = modal[rng.Next(modal.Count)];
        else
        {
            ready = buttons.FirstOrDefault(b => T(b) is "han-ready" or "han-act-go" or "han-go" or "han-continue" or "han-next");
            var all = buttons.Where(b => b != ready).Cast<FrameworkElement>().Concat(cards).ToList();
            pick = ready != null && (rng.Next(100) < 25 || all.Count == 0) ? ready : all.Count > 0 ? all[rng.Next(all.Count)] : null;
        }
        // online hazırlıkta Hazır düğmesi yok: arada bir hazırlık süresi dolmuş sayılır (canlı olmayan kipte sayaç çalışmaz)
        if (ready == null && rng.Next(100) < 20 && view.PrepTimeUp()) return;
        if (pick == null) return;
        if (pick is Button btn) Raise(btn, ButtonBase.ClickEvent);
        else Raise(pick, UIElement.MouseLeftButtonUpEvent);
    }
}
