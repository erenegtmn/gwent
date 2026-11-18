using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Merkez gezintisi (ARAYUZ.md): ana menüdeki dört kapı gerçek tıklamayla açılır, klavye ← → Enter çalışır, Gwent merkezi ve
    /// Hızlı Gwent geri yolları döner, çevrimiçi ekranda oyun türü seçimi düğme ve seçenekleri doğru değiştirir. <c>--selftest-hub</c>
    /// </summary>
    public static int RunHubCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] merkez: " + what); } }
        FrameworkElement Tagged(DependencyObject root, string tag) =>
            FindAll<FrameworkElement>(root, x => (x.Tag as string) == tag).FirstOrDefault() ?? throw new InvalidOperationException("Bulunamadı: " + tag);
        // IsVisible pencere gerektirir; sınamada ağaç pencereye bağlı değil: atalarda Collapsed var mı diye bakılır
        bool Shown(FrameworkElement el) { for (DependencyObject? c = el; c != null; c = System.Windows.Media.VisualTreeHelper.GetParent(c)) if (c is UIElement u && u.Visibility != Visibility.Visible) return false; return true; }
        bool Visible(DependencyObject root, string tag) => FindAll<FrameworkElement>(root, x => (x.Tag as string) == tag && Shown(x)).Any();
        try
        {
            // Ana menü: her kapı kendi eylemini çağırır (soldan sağa)
            var hits = new List<string>();
            var hub = new HubView(() => hits.Add("play"), () => hits.Add("online"), () => hits.Add("keep"), () => hits.Add("deck"), null, null, null, animate: false);
            Layout(hub);
            var tiles = FindAll<HubTile>(hub, _ => true).OrderBy(t => t.TranslatePoint(new Point(0, 0), hub).X).ToList();
            Check(tiles.Count == 4, $"4 kapı beklendi, {tiles.Count} bulundu");
            foreach (var t in tiles) ClickAt(hub, t);
            Check(hits.SequenceEqual(new[] { "play", "online", "keep", "deck" }), "kapılar sırayla açılmadı: " + string.Join(",", hits));

            // Klavye: sağ ok seçimi kaydırır, Enter seçili kapıyı açar
            hits.Clear();
            var hub2 = new HubView(() => hits.Add("play"), () => hits.Add("online"), () => hits.Add("keep"), () => hits.Add("deck"), null, null, null, animate: false);
            Layout(hub2);
            var start = hub2.Row.Active!.Key;
            hub2.Row.HandleKey(Key.Right);
            hub2.Row.HandleKey(Key.Enter);
            Check(hits.Count == 1 && hits[0] != start, "klavye ok + Enter seçili kapıyı açmadı: " + string.Join(",", hits));

            // Gwent merkezi: dört kapı ve geri
            var played = new List<string>();
            var backed = false;
            var play = new PlayHubView(() => backed = true, () => played.Add("quick"), () => played.Add("career"), () => played.Add("draft"), () => played.Add("han"), null, animate: false);
            Layout(play);
            var ptiles = FindAll<HubTile>(play, _ => true).OrderBy(t => t.TranslatePoint(new Point(0, 0), play).X).ToList();
            Check(ptiles.Count == 4, "Gwent merkezinde 4 kapı beklendi");
            foreach (var t in ptiles) ClickAt(play, t);
            Check(played.SequenceEqual(new[] { "quick", "career", "draft", "han" }), "Gwent merkezi kapıları açılmadı: " + string.Join(",", played));
            ClickAt(play, Tagged(play, "hub-back"));
            Check(backed, "Gwent merkezinde geri çalışmadı");

            // Hızlı Gwent merkezden açılınca kurulum açık, "Geri" merkeze döner
            var exited = false;
            var quick = new MenuView(_ => { }, (_, _) => { }, animate: false, exit: () => exited = true);
            Layout(quick);
            var go = FindAll<Button>(quick, b => b.Content is string s && s == "Oyuna başla").FirstOrDefault();
            Check(go != null && Shown(go), "Hızlı Gwent kurulumu açık gelmedi");
            Click(FindAll<Button>(quick, b => b.Content is string s && s == "Geri").First());
            Check(exited, "Hızlı Gwent 'Geri' merkeze dönmedi");

            // Çevrimiçi: oyun türü seçimi
            var online = new OnlineMenuView(() => { }, _ => { });
            Layout(online);
            Check((string?)((Button)Tagged(online, "online-create")).Content == "Odayı aç", "Düello'da düğme 'Odayı aç' olmalı");
            Check(!Visible(online, "online-tournament-options"), "turnuva ayarı düello'da görünmemeli");
            ClickAt(online, Tagged(online, "online-mode-tournament"));
            Layout(online);
            Check(Visible(online, "online-tournament-options"), "turnuva ayarı açılmadı");
            Check(FindAll<Button>(online, b => (b.Tag as string) == "online-tournament").Any(), "turnuva kurma düğmesi etiketi değişmedi");
            ClickAt(online, Tagged(online, "online-mode-han"));
            Layout(online);
            Check(!Visible(online, "online-tournament-options"), "turnuva ayarı Han Ligi'nde kapanmadı");
            Check(FindAll<Button>(online, b => (b.Tag as string) == "online-create").Any(), "oda kurma düğmesi etiketi geri gelmedi");
            // Geçersiz takma adda ağa gidilmeden hata şeridi çıkar, perde kalkık kalır
            ((TextBox)Tagged(online, "online-nick")).Text = "x";
            Click((Button)Tagged(online, "online-create"));
            Layout(online);
            Check(Visible(online, "online-error"), "geçersiz takma adda hata şeridi çıkmadı");
            Check(!Visible(online, "online-busy"), "hata sonrası perde kalkmalıydı");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] merkez: " + ex.Message);
        }
        Console.WriteLine($"Merkez gezintisi: {failures} hata.");
        return failures;
    }
}
