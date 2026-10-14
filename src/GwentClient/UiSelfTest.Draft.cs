using System.Windows;
using System.Windows.Controls;
using GwentAI.Draft;
using GwentCore;

namespace GwentClient;

public static partial class UiSelfTest
{
    /// <summary>
    /// Draft arayüzü: fraksiyon → lider → 25 kart seçimi (arayüzden) → masaya otur → maç arayüzden oynanır → sonuç koşuya işlenir;
    /// koşu bitince ödül Collect 'Em All kesesine yatar; "Koşuyu bitir" iki adımlıdır. Gerçek kayıtlara dokunmaz. <c>--selftest-draft</c>
    /// </summary>
    public static int RunDraftCheck()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gwent-selftest-draft-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        DraftStore.PathOverride = System.IO.Path.Combine(dir, "draft.json");
        CareerStore.PathOverride = System.IO.Path.Combine(dir, "career.json");
        var failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("[HATA] draft: " + what); } }
        FrameworkElement Tagged(DependencyObject root, Func<string, bool> tag) =>
            FindAll<FrameworkElement>(root, x => x.Tag is string t && tag(t)).FirstOrDefault() ?? throw new InvalidOperationException("Bulunamadı");
        try
        {
            var save = new DraftSave();
            var played = 0;
            var view = new DraftView(save, () => played++, () => { });
            Layout(view);
            Raise(Tagged(view, t => t == "draft-faction:monsters"), UIElement.MouseLeftButtonUpEvent);
            Check(save.Run is { Phase: DraftPhase.Leader, Faction: "monsters" }, "fraksiyon seçilmedi");
            Layout(view);
            Raise(Tagged(view, t => t.StartsWith("draft-leader:")), UIElement.MouseLeftButtonUpEvent);
            Check(save.Run!.LeaderId != null, "lider seçilmedi");
            var rng = new Random(5);
            for (var i = 0; i < 40 && save.Run.Phase == DraftPhase.Picking; i++)
            {
                Layout(view);
                var offers = FindAll<FrameworkElement>(view, x => (x.Tag as string) == "draft-card").ToList();
                Raise(offers[rng.Next(offers.Count)], UIElement.MouseLeftButtonUpEvent);
            }
            var run = save.Run;
            Check(run.Phase == DraftPhase.Playing && run.Picks.Count == DraftLogic.DeckSize, "25 seçim tamamlanmadı");
            Check(DeckValidator.Validate(run.Deck()).Count == 0, "draft destesi geçersiz");
            Check(DraftStore.Load().Run?.Picks.Count == DraftLogic.DeckSize, "seçimler kaydedilmedi");
            Layout(view);
            Click(FindAll<Button>(view, b => (b.Tag as string) == "draft-play").First());
            Check(played == 1, "masaya otur çalışmadı");

            // Maç: arayüz rastgele tıklamayla oynar, sonuç koşuya işlenir
            var m = DraftMatch.Create(save, run, seed: 77);
            var game = new GwintView(m.Session, () => { }, _ => { }, animate: false, effects: false);
            for (var step = 0; step < 4000 && !m.Session.IsOver; step++)
            {
                Layout(game);
                UiStep(game, m.Session, rng, _ => { });
            }
            Layout(game);
            Check(m.Session.IsOver && (run.Wins + run.Losses == 1 || m.Session.WinnerSeat == null), "maç sonucu koşuya işlenmedi");

            // Koşunun geri kalanı: hep kazan → ödül kariyer kesesine
            var crownsBefore = CareerStore.Load()?.Crowns ?? 0;
            var careerExisted = CareerStore.Load() != null;
            while (run.Phase == DraftPhase.Playing)
            {
                var r = DraftLogic.ApplyResult(run, true);
                if (run.Phase == DraftPhase.Over) DraftRewards.Pay(save, run, r.Crowns);
            }
            var paid = CareerStore.Load()!.Crowns - (careerExisted ? crownsBefore : GwentAI.Career.CareerLogic.StartCrowns);
            Check(paid == DraftLogic.Reward(run), $"ödül kariyer kesesine yatmadı ({paid})");
            Check(save.Best == run.Wins && save.Runs == 1, "en iyi koşu kaydı");

            // Yeni koşu ve iki adımlı "Koşuyu bitir"
            var over = new DraftView(save, () => { }, () => { });
            Layout(over);
            Click(FindAll<Button>(over, b => (b.Tag as string) == "draft-new").First());
            Check(save.Run == null, "yeni koşu başlamadı");
            save.Run = DraftLogic.Start("nilfgaard", 3);
            DraftLogic.PickLeader(save.Run, save.Run.LeaderOffer[0]);
            while (save.Run.Phase == DraftPhase.Picking) DraftLogic.PickCard(save.Run, save.Run.Offer[0]);
            DraftLogic.ApplyResult(save.Run, true);
            var hub = new DraftView(save, () => { }, () => { }, "Kazandın!");
            Layout(hub);
            Click(FindAll<Button>(hub, b => (b.Tag as string) == "draft-quit").First());
            Check(save.Run.Phase == DraftPhase.Playing, "koşu onaysız bitti");
            Layout(hub);
            Click(FindAll<Button>(hub, b => (b.Tag as string) == "draft-quit-confirm").First());
            Check(save.Run.Phase == DraftPhase.Over && save.Runs == 2, "koşuyu bitir çalışmadı");

            Console.WriteLine($"Draft: koşu {run.Wins}-{run.Losses}, {failures} hata.");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("[HATA] draft: " + ex);
        }
        finally
        {
            DraftStore.PathOverride = null;
            CareerStore.PathOverride = null;
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
        return failures == 0 ? 0 : 1;
    }
}
