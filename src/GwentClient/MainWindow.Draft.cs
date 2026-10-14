using GwentAI.Draft;

namespace GwentClient;

/// <summary>Draft akışı: draft ekranı (seçimler) → VS → maç → draft ekranı ... → koşu sonu.</summary>
public partial class MainWindow
{
    private void ShowDraft() => ShowDraft(null);

    private void ShowDraft(string? last)
    {
        var save = DraftStore.Load();
        Host.Content = new DraftView(save, () => StartDraftMatch(save), () =>
        {
            _career = null;   // draft ödülü Collect 'Em All kesesine diskten yazıldı: bellekteki kopya yeniden okunsun
            ShowMenu();
        }, last);
    }

    private void StartDraftMatch(DraftSave save)
    {
        var run = save.Run!;
        var m = DraftMatch.Create(save, run, () => _career = null);

        void Leave()
        {
            if (!m.Session.IsOver) m.Session.Settle!(false);   // yarıda bırakmak yenilgi sayılır
            ShowDraft(m.LastSummary);
        }

        void Begin()
        {
            var view = new GwintView(m.Session, Leave, _ => StartDraftMatch(save));
            Music.Start();
            Host.Content = view;
            view.Focus();
        }

        var me = string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick;
        var cfg = m.Session.Config;
        Host.Content = new VsScreen(new VsSideInfo(me, cfg.MyFaction, cfg.MyLeaderId, true, Settings.Portrait),
            new VsSideInfo(m.Session.OppName, cfg.OppFaction, cfg.OppLeaderId, false, m.Session.OppAvatar), Begin, animate: true);
    }
}

/// <summary>Bir draft maçı: draft destesi, rakibin orijinal NPC destesi; bitince sonuç koşuya işlenir, koşu bittiyse ödül ödenir.</summary>
public sealed class DraftMatch
{
    public GameSession Session { get; }
    public string? LastSummary { get; private set; }

    private DraftMatch(GameSession s) => Session = s;

    public static DraftMatch Create(DraftSave save, DraftRun run, Action? paid = null, int? seed = null)
    {
        var next = run.Next ?? throw new InvalidOperationException("Sıradaki rakip yok.");
        var npc = GwentAI.Career.NpcDecks.Get(next.Deck, next.Level);
        var cfg = new MatchConfig(run.Faction, run.LeaderId!, npc.Faction, npc.LeaderId, false, seed ?? Random.Shared.Next(1, int.MaxValue), run.Deck(), next.Ai, npc);
        DraftMatch? m = null;
        var session = new GameSession(cfg)
        {
            OppNameOverride = next.Name,
            OppAvatarOverride = next.Face ?? npc.LeaderId,
            ExitLabel = "Devam",
            ModeLabel = "Draft",
            TournamentMode = true,   // "Yeniden oyna" yalnız berabere kalınca (aynı rakiple)
            Settle = won =>
            {
                var (summary, crowns) = DraftLogic.ApplyResult(run, won);
                if (run.Phase == DraftPhase.Over) { DraftRewards.Pay(save, run, crowns); paid?.Invoke(); }
                else DraftStore.Save(save);
                m!.LastSummary = summary;
                return summary + (crowns > 0 ? "  (Collect 'Em All kesene)" : "");
            }
        };
        m = new DraftMatch(session);
        return m;
    }
}
