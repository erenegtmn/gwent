using GwentAI;
using GwentClient.KaerMorhen;
using GwentCore;
using KaerMorhen.Core;

namespace GwentClient;

/// <summary>Kaer Morhen akışı: kale → sözleşme hazırlığı → VS ekranı → maç (arazi, komutan, rünler, parçalar) → ödül → kale.</summary>
public partial class MainWindow
{
    private KeepSave? _keep;
    private string? _keepMessage;

    private void ShowKeep()
    {
        if (_keep == null)
        {
            _keep = KeepStore.Load();
            if (_keep == null)
            {
                _keep = Keep.NewSave(Random.Shared.Next(1, int.MaxValue));
                KeepStore.Save(_keep);
                _keepMessage = "Kaer Morhen harabe hâlinde. Sözleşmelerle Crown ve mutajen kazan, binaları onar.";
            }
        }
        var msg = _keepMessage;
        _keepMessage = null;
        Host.Content = new KeepView(_keep, new KeepNav(StartContract, ShowMenu), msg);
    }

    private void StartContract(Contract c, Deck deck, List<CardRune> runes, List<TerrainPiece> pieces)
    {
        var s = _keep!;
        var session = KeepMatch.Create(s, c, deck, runes, pieces, r => _keepMessage = r.Summary);

        void Leave()
        {
            if (!session.IsOver) session.Settle!(false);   // yarıda bırakmak başarısızlık sayılır
            ShowKeep();
        }

        // Rövanş: sözleşme panoda duruyorsa (kaybedildiyse) aynı hazırlıkla yeniden; yoksa kaleye
        void Again(MatchConfig _)
        {
            if (s.Board.Any(x => x.Id == c.Id)) StartContract(c, deck, runes, pieces);
            else ShowKeep();
        }

        void Begin()
        {
            var view = new GwintView(session, Leave, Again);
            Music.Start();
            Host.Content = view;
            view.Focus();
        }

        var me = string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick;
        var cfg = session.Config;
        Host.Content = new VsScreen(new VsSideInfo(me, cfg.MyFaction, cfg.MyLeaderId, true, Settings.Portrait),
            new VsSideInfo(session.OppName, cfg.OppFaction, cfg.OppLeaderId, false, session.OppAvatar), Begin, animate: true);
    }
}

/// <summary>Bir sözleşme maçının kurulumu ve sonucun kaleye işlenmesi.</summary>
public static class KeepMatch
{
    public static GameSession Create(KeepSave s, Contract c, Deck deck, List<CardRune> runes, List<TerrainPiece> pieces, Action<ContractResult> settled, int? seed = null)
    {
        var cfg = new MatchConfig(deck.Faction, deck.LeaderId, c.Faction, c.LeaderId, false, seed ?? Random.Shared.Next(1, int.MaxValue), deck, c.Difficulty,
            null, c.Curse == null ? null : new[] { c.Curse }, null, c.Battlefield, true, Keep.LimitRunes(s, runes), pieces, Keep.MatchAllies(s));
        return new GameSession(cfg)
        {
            OppNameOverride = c.Title,
            OppAvatarOverride = c.LeaderId,
            ModeLabel = c.Id < 0 ? "Wild Hunt kuşatması" : "Sözleşme",
            ExitLabel = "Kaleye dön",
            Settle = won =>
            {
                var r = Keep.Complete(s, c.Id, won == true);
                KeepStore.Save(s);
                settled(r);
                return r.Summary;
            }
        };
    }
}
