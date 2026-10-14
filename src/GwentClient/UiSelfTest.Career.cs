using System.Windows;
using System.Windows.Controls;
using GwentAI.Career;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Kariyer arayüzü: haritada rakip seç → masaya otur → maçı arayüzden sonuna kadar oyna → sonuç kayda işlenir (ödül/kayıt) →
    /// ödül penceresi kapanır; tüccardan kart al; kariyer destesinde koleksiyon sınırı; turnuvaya giriş ücreti. Gerçek kayıt dosyasına dokunmaz.
    /// <c>--selftest-career [maç]</c>
    /// </summary>
    public static int RunCareerCheck(int games)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-career-" + Guid.NewGuid().ToString("N") + ".json");
        CareerStore.PathOverride = tmp;
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] kariyer: " + what); } }
        T Tagged<T>(DependencyObject root, string tag) where T : DependencyObject =>
            FindAll<T>(root, x => x is FrameworkElement fe && (fe.Tag as string) == tag).FirstOrDefault() ?? throw new InvalidOperationException("Bulunamadı: " + tag);
        try
        {
            var s = CareerLogic.NewSave();
            CareerStore.Save(s);
            var rng = new Random(11);
            var wins = 0;
            var stats = new Dictionary<string, int>();
            for (var gi = 0; gi < games; gi++)
            {
                // 1) Harita: açık bir bölgeden rakip seç, ayrıntı kartından "Masaya otur"
                CareerOpponent? picked = null;
                var nav = new CareerNav(o => picked = o, () => { }, _ => { }, () => { }, () => { });
                var region = CareerWorld.Regions.Last(r => CareerLogic.IsUnlocked(s, r));
                s.LastRegion = region.Id;
                var duels = CareerWorld.In(region.Id).Where(o => !o.IsTournament).ToList();
                var target = duels.FirstOrDefault(o => !s.Beaten(o.Id)) ?? duels[rng.Next(duels.Count)];
                var map = new CareerView(s, nav);
                Layout(map);
                Raise(Tagged<FrameworkElement>(map, "career-pin:" + target.Id), UIElement.MouseLeftButtonUpEvent);
                Layout(map);
                Click(Tagged<Button>(map, "career-play"));
                Check(picked == target, $"masaya otur {target.Id} yerine {picked?.Id}");
                if (picked == null) continue;

                // 2) Maç: arayüz rastgele tıklamayla oynar; yapay zekâ rakibin orijinal destesiyle
                CareerReward? reward = null;
                var before = (s.Crowns, Wins: s.Record(target.Id).Wins, Losses: s.Record(target.Id).Losses);
                var m = CareerMatch.Create(s, target, r => reward = r, seed: 100 + gi);
                Check(m.Session.OppName == target.Name, "rakip adı");
                var view = new GwintView(m.Session, () => { }, _ => { }, animate: false, effects: false);
                for (var step = 0; step < 4000 && !m.Session.IsOver; step++)
                {
                    Layout(view);
                    UiStep(view, m.Session, rng, k => stats[k] = stats.GetValueOrDefault(k) + 1);
                }
                Layout(view);
                Check(m.Session.IsOver, "maç bitmedi");
                Check(reward != null, "sonuç kariyere işlenmedi");
                var won = m.Session.WinnerSeat == 0;
                if (won)
                {
                    wins++;
                    Check(s.Crowns > before.Crowns && s.Record(target.Id).Wins == before.Wins + 1, "galibiyet ödülü");
                    Check(FindAll<TextBlock>(view, t => (t.Tag as string) == "career-reward").Any(), "oyun sonunda ödül yazısı yok");
                }
                else if (m.Session.WinnerSeat == 1) Check(s.Record(target.Id).Losses == before.Losses + 1, "yenilgi kaydı");
                Check(CareerStore.Load()?.Crowns == s.Crowns, "kayıt diske yazılmadı");

                // 3) Ödül penceresi açılır ve kapanır
                if (reward is { Cards.Count: > 0 })
                {
                    var after = new CareerView(s, nav, reward, target.Id);
                    Layout(after);
                    Click(Tagged<Button>(after, "career-reward-close"));
                    Check(!FindAll<FrameworkElement>(after, x => (x.Tag as string) == "career-reward-overlay").Any(), "ödül penceresi kapanmadı");
                }
            }

            // 4) Tüccar: alınabilen ilk kart alınır
            s.Crowns = Math.Max(s.Crowns, 200);
            var shop = new CareerShopView(s, CareerWorld.Regions[0], () => { });
            Layout(shop);
            var first = CareerLogic.Stock(s, CareerWorld.Regions[0]).First(d => NpcDecks.Price(d) <= s.Crowns);
            var owned = s.Owned(first.Id);
            var crowns = s.Crowns;
            Raise(Tagged<FrameworkElement>(shop, "shop-card:" + first.Id), UIElement.MouseLeftButtonUpEvent);
            Check(s.Owned(first.Id) == owned + 1 && s.Crowns == crowns - NpcDecks.Price(first), "tüccardan alım");

            // 5) Kariyer destesi: koleksiyonda olmayan kart eklenemez, kaydet etkin desteyi yazar
            var deckView = new DeckBuilderView("northern_realms", false, () => { }, s);
            Layout(deckView);
            foreach (var card in FindAll<GwintCard>(deckView, c => (c.Tag as string) == "pool").ToList())
                for (var k = 0; k < 4; k++) Raise(card, UIElement.MouseLeftButtonUpEvent);
            Layout(deckView);
            Click(Tagged<Button>(deckView, "career-deck-save"));
            var saved = s.Decks["northern_realms"];
            Check(saved.Cards.GroupBy(x => x).All(g => g.Count() <= s.Owned(g.Key)), "deste koleksiyondan fazla kart içeriyor");
            Check(CareerLogic.DeckProblem(s) == null, "kaydedilen deste oynanamıyor: " + CareerLogic.DeckProblem(s));

            // 6) Turnuva: Novigrad açıksa ücretle girilir
            var all = CareerLogic.NewSave();
            foreach (var o in CareerWorld.Opponents.Where(o => !o.IsTournament)) CareerLogic.ApplyDuel(all, o, true, rng);
            all.Crowns = 300;
            all.LastRegion = "novigrad";
            CareerOpponent? entered = null;
            var tv = new CareerView(all, new CareerNav(o => entered = o, () => { }, _ => { }, () => { }, () => { }), null, "passiflora");
            Layout(tv);
            Click(Tagged<Button>(tv, "career-play"));
            Check(entered == null && all.Tournament == null, "turnuvaya uyarı onaylanmadan girildi");
            Layout(tv);
            Click(Tagged<Button>(tv, "career-play-confirm"));
            Check(entered?.Id == "passiflora" && all.Tournament != null && all.Crowns == 300 - CareerWorld.Opponent("passiflora").EntryFee, "turnuvaya giriş");

            // 7) Turlar arası ekran: sonraki tur ve iki adımlı çekilme
            var nexts = 0;
            var withdrew = false;
            var round = new CareerTournamentView(all, () => nexts++, () => withdrew = true, "1. turu kazandın!");
            Layout(round);
            Click(Tagged<Button>(round, "tour-next"));
            Click(Tagged<Button>(round, "tour-quit"));
            Layout(round);
            Check(!withdrew, "çekilme onaysız gerçekleşti");
            Click(Tagged<Button>(round, "tour-quit-confirm"));
            Check(nexts == 1 && withdrew, "turlar arası ekran düğmeleri");

            Console.WriteLine($"Kariyer: {games} maç ({wins} galibiyet), koleksiyon {CareerLogic.Progress(s).Have} kart, {failures} hata.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] kariyer: " + ex);
        }
        finally
        {
            CareerStore.PathOverride = null;
            try { System.IO.File.Delete(tmp); } catch { }
        }
        return failures == 0 ? 0 : 1;
    }
}
