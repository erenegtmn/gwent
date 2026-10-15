using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GwentCore;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Kaer Morhen denemesi: arazili + komutanlı maçlar arayüzden sonuna kadar oynanır (rozetler, figürler, etiket görünür;
    /// komutan arayüzden taşınabilir); menüde savaş alanı seçimi maça geçer.
    /// </summary>
    public static int RunTerrainCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] savaş alanı: " + what); } }
        try
        {
            var rng = new Random(17);
            var stats = new Dictionary<string, int>();
            foreach (var (map, i) in GwentAI.Battlefields.All.Select((m, i) => (m, i)))
            {
                var f = Factions[i % Factions.Length];
                var o = Factions[(i + 2) % Factions.Length];
                var cfg = new MatchConfig(f, CardCatalog.Default.Leaders(f).First().Id, o, CardCatalog.Default.Leaders(o).First().Id, false, 60 + i,
                    Battlefield: map.Id, Commanders: true);

                // Maç ortasında: arazi rozetleri, iki komutan ve harita etiketi görünür
                var mid = PlayOne(cfg, rng, stats, s => s.Game.Phase == Phase.Playing && s.Game.Pending == null && s.Game.Current == s.MySeat && s.Game.Round == 1);
                Layout(mid);
                var terrains = Enumerable.Range(0, 2).Sum(side => Enum.GetValues<RowKind>().Count(rk => mid.Session.Game.TerrainAt(side, rk) != Terrain.Plain));
                Check(FindAll<Border>(mid, b => (b.Tag as string) == "terrain").Count() == terrains, $"{map.Id}: arazi rozeti sayısı");
                Check(FindAll<Grid>(mid, g => (g.Tag as string) == "commander").Count() == 2, $"{map.Id}: komutan figürleri");
                Check(FindAll<TextBlock>(mid, t => (t.Tag as string) == "battlefield-label").Any(), $"{map.Id}: harita etiketi yok");
                var moves = FindAll<Grid>(mid, g => (g.Tag as string) == "commander-move").ToList();
                Check(moves.Count == 2, $"{map.Id}: komutan taşıma hedefleri ({moves.Count})");
                if (moves.Count > 0)
                {
                    Raise(moves[0], System.Windows.UIElement.MouseLeftButtonUpEvent);
                    Check(mid.Session.Game.Players[mid.Session.MySeat].CommanderMoved, $"{map.Id}: tıklayınca komutan taşınmadı");
                }

                // Sonuna kadar
                PlayOne(cfg, rng, stats);
            }
            Check(stats.GetValueOrDefault("komutan-tasi") > 0, "rastgele oyunda komutan hiç taşınmadı");
            Check(stats.GetValueOrDefault("biten-oyun") == GwentAI.Battlefields.All.Count, "arazili oyunlar bitmedi");

            // Menü: demircide rün takılır, savaş alanı seçimi ve rünler maça geçer (gerçek demirci kaydına dokunmaz)
            var forgeFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-forge-" + Guid.NewGuid().ToString("N") + ".json");
            KaerMorhen.ForgeStore.PathOverride = forgeFile;
            try
            {
                MatchConfig? started = null;
                var menu = new MenuView(c => started = c, (_, _) => { }, animate: false);
                Layout(menu);
                typeof(MenuView).GetMethod("ToggleSetup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(menu, null);
                Layout(menu);
                Click(FindAll<Button>(menu, b => (b.Tag as string) == "forge-open").First());
                Layout(menu);
                var tiles = FindAll<Grid>(menu, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
                Check(tiles.Count > 5, "demircide kart yok");
                Raise(tiles[0], System.Windows.UIElement.MouseLeftButtonUpEvent);               // Svarog
                Layout(menu);
                tiles = FindAll<Grid>(menu, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
                Raise(tiles[1], System.Windows.UIElement.MouseLeftButtonUpEvent);
                Layout(menu);
                tiles = FindAll<Grid>(menu, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
                Raise(tiles[1], System.Windows.UIElement.MouseLeftButtonUpEvent);               // ikinci kart: Perun
                Layout(menu);
                Click(FindAll<Button>(menu, b => (b.Tag as string) == "forge-done").First());
                Layout(menu);
                Check(!FindAll<KaerMorhen.ForgeView>(menu, _ => true).Any(), "demirci kapanmadı");
                Raise(FindAll<ToggleButton>(menu, b => (b.Tag as string) == "battlefield:pontar_bridge").First(), ButtonBase.ClickEvent);
                Click(FindAll<Button>(menu, b => b.Content is string t && t == "Oyuna başla").First());
                Check(started is { Battlefield: "pontar_bridge", Commanders: true }, "menüden seçilen savaş alanı maça geçmedi");
                Check(started?.Runes is { Count: 2 } r && r[0].Rune == RuneIds.Svarog && r[1].Rune == RuneIds.Perun, "demirci rünleri maça geçmedi");
                if (started != null)
                {
                    var view = PlayOne(started, rng, stats, s => s.Game.Phase == Phase.Playing);
                    var g = view.Session.Game;
                    Check(g.Players[0].Deck.Concat(g.Players[0].Hand).Count(c => c.Rune != null) == 2, "oyuncunun destesinde rün yok");
                    Check(g.Players[1].Deck.Concat(g.Players[1].Hand).Count(c => c.Rune != null) == GwentAI.Forge.AiRuneCount(started.Difficulty), "yapay zekâ rünleri");
                    PlayOne(started, rng, stats);
                }
            }
            finally
            {
                KaerMorhen.ForgeStore.PathOverride = null;
                try { System.IO.File.Delete(forgeFile); } catch { }
            }
            Console.WriteLine($"Savaş alanı: {GwentAI.Battlefields.All.Count} harita, komutan {stats.GetValueOrDefault("komutan-tasi")} kez taşındı.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] savaş alanı: " + ex);
        }
        Console.WriteLine($"Savaş alanı: {failures} hata.");
        return failures;
    }
}
