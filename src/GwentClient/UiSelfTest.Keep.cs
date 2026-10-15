using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GwentAI;
using GwentClient.KaerMorhen;
using GwentCore;
using KaerMorhen.Core;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Kaer Morhen kalesi (gerçek kayda dokunmaz): binalar arayüzden onarılır, sözleşme hazırlığında deste/rün/parça seçilir,
    /// sözleşme maçı arayüzden sonuna kadar oynanır ve sonuç kaleye işlenir.
    /// </summary>
    public static int RunKeepCheck()
    {
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] kale: " + what); } }
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-keep-" + Guid.NewGuid().ToString("N"));
        KeepStore.PathOverride = tmp + "-keep.json";
        ForgeStore.PathOverride = tmp + "-forge.json";
        try
        {
            var s = Keep.NewSave(42);
            s.Crowns = 1000;
            (Contract C, Deck D, List<CardRune> R, List<TerrainPiece> P)? started = null;
            var back = false;
            var view = new KeepView(s, new KeepNav((c, d, r, p) => started = (c, d, r, p), () => back = true));
            Layout(view);
            Button Tagged(string tag) => FindAll<Button>(view, b => (b.Tag as string) == tag).First();

            // Binalar: demirci ve atölye onarılır; planlanmış binada düğme yok
            Click(Tagged("keep-upgrade:" + Keep.Forge)); Layout(view);
            Click(Tagged("keep-upgrade:" + Keep.Workshop)); Layout(view);
            Check(s.Level(Keep.Forge) == 1 && s.Level(Keep.Workshop) == 1, "binalar onarılmadı");
            Check(s.Crowns == 1000 - 80 - 100, $"bedel düşmedi ({s.Crowns})");
            Check(!FindAll<Button>(view, b => (b.Tag as string) == "keep-upgrade:" + Keep.Alchemy).Any(), "planlanmış binada düğme var");
            Check(!Tagged("keep-upgrade:" + Keep.Forge).IsEnabled, "mutajensiz yükseltme açık");
            Check(KeepStore.Load()?.Level(Keep.Forge) == 1, "kale kaydedilmedi");

            // Sözleşme hazırlığı: Skellige, demircide bir rün, kule menzil satırına
            var contract = s.Board[0];
            Click(Tagged("contract:" + contract.Id)); Layout(view);
            Raise(FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-faction:skellige").First(), ButtonBase.ClickEvent); Layout(view);
            Click(Tagged("prep-forge")); Layout(view);
            var tiles = FindAll<Grid>(view, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
            Raise(tiles[0], System.Windows.UIElement.MouseLeftButtonUpEvent); Layout(view);
            tiles = FindAll<Grid>(view, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
            Raise(tiles[1], System.Windows.UIElement.MouseLeftButtonUpEvent); Layout(view);   // yuva dolu: takılmamalı
            Click(Tagged("forge-done")); Layout(view);
            for (var i = 0; i < 2; i++) { Raise(FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-piece:tower").First(), ButtonBase.ClickEvent); Layout(view); }
            Check(!FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-piece:barricade").Any(), "atölye 1. seviyede barikat var");
            Click(Tagged("prep-go"));
            Check(started is { } st && st.D.Faction == "skellige" && st.R.Count == 1 && st.R[0].Rune == RuneIds.Svarog
                  && st.P.Count == 1 && st.P[0] == new TerrainPiece(0, RowKind.Ranged, Terrain.Hill), "hazırlık maça doğru geçmedi");

            // Sözleşme maçı arayüzden oynanır, sonuç kaleye işlenir
            if (started is { } go)
            {
                ContractResult? result = null;
                var session = KeepMatch.Create(s, go.C, go.D, go.R, go.P, r => result = r, seed: 5);
                Check(session.Game.TerrainAt(0, RowKind.Ranged) == Terrain.Hill, "kule araziye işlenmedi");
                Check(session.Game.Players[0].Deck.Concat(session.Game.Players[0].Hand).Count(c => c.Rune != null) == 1, "rün maça geçmedi");
                var gv = new GwintView(session, () => { }, _ => { }, animate: false, effects: true);
                var rng = new Random(3);
                for (var step = 0; step < 4000 && !session.IsOver; step++) { Layout(gv); UiStep(gv, session, rng, _ => { }); }
                Layout(gv);
                Check(session.IsOver, "sözleşme maçı bitmedi");
                Check(result != null && s.Wins + s.Losses == 1, "sonuç kaleye işlenmedi");
                Check(KeepStore.Load() is { } k && k.Wins + k.Losses == 1, "sonuç kaydedilmedi");
            }

            // Menüye dönüş
            view = new KeepView(s, new KeepNav((_, _, _, _) => { }, () => back = true));
            Layout(view);
            Click(Tagged("keep-back"));
            Check(back, "menüye dönülmedi");
            Console.WriteLine($"Kale: {s.Wins} başarı, {s.Losses} başarısız, {s.Crowns} Crown.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] kale: " + ex);
        }
        finally
        {
            KeepStore.PathOverride = null;
            ForgeStore.PathOverride = null;
            foreach (var f in new[] { tmp + "-keep.json", tmp + "-forge.json" }) try { System.IO.File.Delete(f); } catch { }
        }
        Console.WriteLine($"Kale: {failures} hata.");
        return failures;
    }
}
