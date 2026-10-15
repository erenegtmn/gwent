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
            Check(FindAll<Button>(view, b => (b.Tag as string) == "keep-upgrade:" + Keep.Alchemy).Any(), "simya odasında düğme yok");
            Check(!Tagged("keep-upgrade:" + Keep.Forge).IsEnabled, "mutajensiz yükseltme açık");
            Check(KeepStore.Load()?.Level(Keep.Forge) == 1, "kale kaydedilmedi");

            // Sözleşme hazırlığı: Skellige, demircide bir rün, kule menzil satırına
            var contract = s.Board[0];
            Click(Tagged("contract:" + contract.Id)); Layout(view);
            Raise(FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-faction:skellige").First(), ButtonBase.ClickEvent); Layout(view);
            Click(Tagged("prep-forge")); Layout(view);
            var tiles = FindAll<Grid>(view, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
            ClickAt(view, tiles[0]); Layout(view);
            tiles = FindAll<Grid>(view, g => (g.Tag as string)?.StartsWith("forge-card:") == true).ToList();
            ClickAt(view, tiles[1]); Layout(view);   // yuva dolu: takılmamalı
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

            // Müttefik Salonu: onar, Dandelion'u çağır (hemen maça katılır)
            s.Crowns += 1000; s.Blue += 10;
            view = new KeepView(s, new KeepNav((c, d, r, p) => started = (c, d, r, p), () => back = true));
            Layout(view);
            Click(Tagged("keep-upgrade:" + Keep.AlliesHall)); Layout(view);
            Click(Tagged("keep-upgrade:" + Keep.Alchemy)); Layout(view);
            Click(Tagged("keep-upgrade:" + Keep.Barracks)); Layout(view);
            Check(s.Level(Keep.Alchemy) == 1 && s.Level(Keep.Barracks) == 1, "simya/kışla onarılmadı");
            Click(Tagged("keep-allies")); Layout(view);
            Click(Tagged("ally-recruit:dandelion")); Layout(view);
            Check(FindAll<ToggleButton>(view, t => (t.Tag as string) == "ally-active:dandelion").Any(t => t.IsChecked == true), "çağrılan müttefik maça katılmıyor");
            Click(Tagged("allies-done")); Layout(view);
            Check(Keep.MatchAllies(s).SequenceEqual(new[] { "dandelion" }), "müttefik kaydı");

            // Wild Hunt kuşatması: hazır olunca başlat, ilk dalgaya çık; müttefik elde başlar
            s.SiegeProgress = Keep.SiegeEvery;
            view = new KeepView(s, new KeepNav((c, d, r, p) => started = (c, d, r, p), () => back = true));
            Layout(view);
            Check(!Tagged("contract:" + s.Board[0].Id).IsEnabled, "düşman kapıdayken sözleşme açık");
            Click(Tagged("siege-start")); Layout(view);
            Check(s.SiegeWave == 1, "kuşatma başlamadı");
            started = null;
            Click(Tagged("siege-go")); Layout(view);
            Raise(FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-potion:" + PotionIds.Cat).First(), ButtonBase.ClickEvent); Layout(view);
            Raise(FindAll<ToggleButton>(view, t => (t.Tag as string) == "prep-potion:" + PotionIds.Swallow).First(), ButtonBase.ClickEvent); Layout(view);   // toksisite aşar: içilmez
            Check(s.Potions.SequenceEqual(new[] { PotionIds.Cat }), "iksir seçimi / toksisite sınırı");
            Click(Tagged("prep-go"));
            Check(started is { C.Id: -1 }, "kuşatmanın ilk dalgası başlamadı");
            if (started is { } wave)
            {
                s.Buildings[Keep.Barracks] = 3;   // 11 kart, 3 değiştirme, kim başlar seçimi arayüzde de denensin
                var ws = KeepMatch.Create(s, wave.C, wave.D, wave.R, wave.P, _ => { }, seed: 9);
                Check(ws.Game.Players[0].Hand.Any(c => c.Def.Id == "dandelion") && ws.Game.Players[0].Hand.Count == Game.StartingHand + 2, "müttefik / kışla eli");
                Check(ws.Game.Options.Terrain?.Id == "kaer_morhen_ruins" && ws.Config.OppFaction == "monsters", "kuşatma kurulumu");
                Check(ws.Game.MulligansFor(0) == 3 && ws.Game.PerksOf(0)?.Has(PotionIds.Cat) == true && ws.Game.FirstIsChosen, "kışla/simya maça geçmedi");
                // Kuşatmadan çıkmak önce onay ister (seferi bitirir)
                var left = false;
                var wv = new GwintView(ws, () => left = true, _ => { }, animate: false, effects: true);
                Layout(wv);
                typeof(GwintView).GetMethod("OnMenuClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(wv, null);
                Layout(wv);
                Check(!left && FindAll<Button>(wv, b => (b.Tag as string) == "confirm-yes").Any(), "kuşatmadan çıkarken onay sorulmadı");
                Click(FindAll<Button>(wv, b => b.Content is string t && t == "Vazgeç").First());
                Layout(wv);
                var wrng = new Random(4);
                for (var step = 0; step < 4000 && !ws.IsOver; step++) { Layout(wv); UiStep(wv, ws, wrng, _ => { }); }
                Layout(wv);
                Check(ws.IsOver, "kuşatma dalgası bitmedi");
                Check(ws.Game.Events.Any(e => e.Kind == "first_chosen" && e.Player == 0), "kim başlar seçimi arayüzden yapılmadı");
                Check(ws.WinnerSeat == 0 ? s.SiegeWave == 2 : s.Run == 2 && s.Level(Keep.Forge) == 0 && s.Runs.Count == 1, "dalga sonucu işlenmedi (kayıpta sefer sıfırlanmalı)");
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
