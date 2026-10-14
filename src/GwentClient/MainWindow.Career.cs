using GwentAI.Career;

namespace GwentClient;

/// <summary>
/// Kariyer (Collect 'Em All) akışı: harita → (deste / tüccar) → VS ekranı → maç → ödül → harita.
/// Turnuva sürerken haritaya dönülmez: turlar arası ekran (<see cref="CareerTournamentView"/>) → maç → turlar arası ekran ... → bitince harita.
/// </summary>
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
        if (s.Tournament != null) { ShowCareerTournament(reward?.Summary); return; }   // turnuva sürüyor: harita yok
        Host.Content = new CareerView(s, new CareerNav(
            StartCareerMatch,
            () => Host.Content = new DeckBuilderView(s.ActiveFaction, false, () => ShowCareer(select), s),
            r => Host.Content = new CareerShopView(s, r, () => ShowCareer(select)),
            () => { CareerStore.Save(s); ShowMenu(); },
            () => { CareerStore.Delete(); _career = null; ShowCareer(null); }), reward, select);
    }

    /// <summary>Turlar arası ekran: sıradaki tur ya da turnuvadan çekilme.</summary>
    private void ShowCareerTournament(string? lastResult)
    {
        var s = _career!;
        var o = CareerWorld.Opponent(s.Tournament!.Opponent);
        Host.Content = new CareerTournamentView(s, () => StartCareerMatch(o), () =>
        {
            _careerReward = CareerLogic.WithdrawTournament(s, o);
            CareerStore.Save(s);
            ShowCareer(o.Id);
        }, lastResult);
    }

    private void StartCareerMatch(CareerOpponent o)
    {
        var s = _career!;
        if (CareerLogic.DeckProblem(s) != null) { ShowCareer(o.Id); return; }
        var m = CareerMatch.Create(s, o, r => _careerReward = r);
        CareerStore.Save(s);

        // Maç bitince (ya da yarıda bırakılınca): bırakmak yenilgi sayılır; turnuva sürüyorsa turlar arası ekrana, değilse haritaya
        void Leave()
        {
            if (!m.Session.IsOver) m.Abandon();
            ShowCareer(o.Id);
        }

        void Begin()
        {
            var view = new GwintView(m.Session, Leave, _ => StartCareerMatch(o));
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
            ExitLabel = o.IsTournament ? "Devam" : "Haritaya dön",
            TournamentMode = o.IsTournament,
            Settle = won =>
            {
                var r = o.IsTournament ? CareerLogic.ApplyTournamentMatch(s, o, won, Random.Shared) : CareerLogic.ApplyDuel(s, o, won, Random.Shared);
                CareerStore.Save(s);
                settled(r);
                return r.Summary;
            }
        };
        return new CareerMatch(session) { Abandon = () => session.Settle!(false) };
    }

    /// <summary>Maç bitmeden çıkıldı: yenilgi olarak işlenir (turnuvada elenme).</summary>
    public Action Abandon { get; init; } = () => { };
}
