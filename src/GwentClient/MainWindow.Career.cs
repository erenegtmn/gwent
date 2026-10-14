using GwentAI.Career;

namespace GwentClient;

/// <summary>Kariyer akışı: harita → (deste / tüccar) → VS ekranı → maç → ödül → harita.</summary>
public partial class MainWindow
{
    private CareerSave? _career;
    private CareerReward? _careerReward;

    private void ShowCareer() => ShowCareer(null);

    private void ShowCareer(string? select)
    {
        if (_career == null)
        {
            _career = CareerStore.Load();
            if (_career == null)
            {
                _career = CareerLogic.NewSave();
                CareerStore.Save(_career);
            }
        }
        var s = _career;
        var reward = _careerReward;
        _careerReward = null;
        Host.Content = new CareerView(s, new CareerNav(
            StartCareerMatch,
            () => Host.Content = new DeckBuilderView(s.ActiveFaction, false, () => ShowCareer(select), s),
            r => Host.Content = new CareerShopView(s, r, () => ShowCareer(select)),
            () => { CareerStore.Save(s); ShowMenu(); },
            () => { CareerStore.Delete(); _career = null; ShowCareer(null); }), reward, select);
    }

    private void StartCareerMatch(CareerOpponent o)
    {
        var s = _career!;
        if (CareerLogic.DeckProblem(s) != null) { ShowCareer(o.Id); return; }
        var m = CareerMatch.Create(s, o, r => _careerReward = r);
        CareerStore.Save(s);

        void Begin()
        {
            var view = new GwintView(m.Session, () => ShowCareer(o.Id), _ => StartCareerMatch(o));
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

/// <summary>Bir kariyer maçının kurulumu: oyuncunun kariyer destesi, rakibin orijinal NPC destesi, ad/portre ve maç sonunda kariyere işleme.</summary>
public sealed record CareerMatch(GameSession Session)
{
    public static CareerMatch Create(CareerSave s, CareerOpponent o, Action<CareerReward> settled, int? seed = null)
    {
        var deck = s.ActiveDeck() ?? throw new InvalidOperationException("Kariyer destesi yok.");
        var npc = CareerLogic.OpponentDeck(s, o);
        var round = o.IsTournament && s.Tournament != null ? s.Tournament.Round : 0;
        var cfg = new MatchConfig(deck.Faction, deck.LeaderId, npc.Faction, npc.LeaderId, false, seed ?? Random.Shared.Next(1, int.MaxValue), deck, o.Ai, npc);
        var session = new GameSession(cfg)
        {
            OppNameOverride = o.IsTournament ? $"{o.Name} · {round + 1}. tur" : o.Name,
            OppAvatarOverride = o.IsTournament ? npc.LeaderId : o.Face ?? npc.LeaderId,
            ExitLabel = "Haritaya dön",
            TournamentMode = o.IsTournament,
            Settle = won =>
            {
                var r = o.IsTournament ? CareerLogic.ApplyTournamentMatch(s, o, won, Random.Shared) : CareerLogic.ApplyDuel(s, o, won, Random.Shared);
                CareerStore.Save(s);
                settled(r);
                return r.Summary;
            }
        };
        return new CareerMatch(session);
    }
}
