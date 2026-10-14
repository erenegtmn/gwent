using GwentCore;

namespace GwentAI;

/// <summary>Yapay zekânın ayarlanabilir sayıları. Arena ile denenip değiştirilebilir.</summary>
public sealed record AiTuning
{
    /// <summary>Elinde kalan kartlardan beklenen puan oranı bunu aşıyorsa raundu teslim eder (kaybetsem de oyun bitmiyorsa).</summary>
    public double ConcedeFactor { get; init; } = 0.55;
    /// <summary>
    /// Geriden gelirken: toplam potansiyelim (sahadaki + eldeki) rakibin tahmini toplam potansiyelinin bu oranından
    /// azsa kartlarımı yakmadan raundu bırakırım. 0 = kural kapalı.
    /// </summary>
    public double RelativeConcedeFactor { get; init; } = 0;
    /// <summary>Öndeysem ve fark, rakibin tahmini potansiyelinin bu oranını aşıyorsa pas geçerim.</summary>
    public double PassAheadFactor { get; init; } = 0.7;
    /// <summary>Rakibin elindeki her kartın ortalama puan tahmini (kart kimliği bilinmez).</summary>
    public double AvgCardPoints { get; init; } = 5;
    /// <summary>Bilinmeyen (destenden gelecek) bir kartın değer tahmini.</summary>
    public double UnknownCardValue { get; init; } = 5;
    /// <summary>"Yetmiyor" durumunda maliyetin kazanca göre ağırlığı.</summary>
    public double Lambda { get; init; } = 0.5;
    /// <summary>Lider yeteneğinin harcanma maliyeti (son raundda 0: ileride işe yaramaz).</summary>
    public double LeaderCost { get; init; } = 4;
    /// <summary>Kart değiştirmede bu değerin altındaki kartlar atılmaya aday.</summary>
    public double MulliganBelow { get; init; } = 3.5;
    /// <summary>
    /// Kolay/Normal zaafı: öne geçmek için yeterli en ucuz kart yerine bu olasılıkla en güçlüsünü oynar (gereğinden fazla kart gücü harcar).
    /// 0 = her zaman en ekonomik hamle (Zor).
    /// </summary>
    public double Overcommit { get; init; } = 0;
}

/// <summary>
/// Klasik tempo mantığıyla oynayan yapay zekâ: gerektiğinden fazla kart yakmaz, ön ise pas geçer,
/// geriden gelmenin maliyeti kazancı aşıyorsa raundu teslim edip kartlarını sonraya saklar.
/// Hamleleri oyunun KOPYASI üzerinde dener (rakibin elini görmeden).
/// </summary>
public sealed class HeuristicAgent : IAgent
{
    private readonly AiTuning _t;
    private readonly Random _rng;
    public HeuristicAgent(AiTuning? tuning = null, int seed = 1)
    {
        _t = tuning ?? new AiTuning();
        _rng = new Random(seed);
    }

    /// <summary>Kazanç sağlayan adaylardan ekonomik olanı; <see cref="AiTuning.Overcommit"/> olasılığıyla en güçlüsü.</summary>
    private Cand? Pick(IEnumerable<Cand> ok)
    {
        var list = ok.ToList();
        if (list.Count == 0) return null;
        if (_t.Overcommit > 0 && _rng.NextDouble() < _t.Overcommit) return list.OrderByDescending(c => c.Gain).First();
        return list.OrderBy(c => c.Cost).ThenByDescending(c => c.Gain).First();
    }

    public GameAction Decide(Game g, int me)
    {
        if (!g.PlayersToAct().Contains(me)) throw new InvalidOperationException($"Şu an oyuncu {me}'nin eylem sırası değil.");
        switch (g.Phase)
        {
            case Phase.Mulligan: return DecideMulligan(g, me);
            case Phase.ChooseFirst: return new ChooseFirst(me, 1 - me); // ikinci başlamak bilgi avantajı verir
            default: return g.Pending != null ? ChoosePending(g, me) : DecideTurn(g, me);
        }
    }

    // ---------- Kart değiştirme ----------

    private GameAction DecideMulligan(Game g, int me)
    {
        var hand = g.Players[me].Hand;
        var bondCounts = hand.Where(c => c.Has("bond")).GroupBy(c => c.Def.Id).ToDictionary(x => x.Key, x => x.Count());
        double Keep(CardInstance c) => Valuation.CardValue(c.Def) + (bondCounts.GetValueOrDefault(c.Def.Id) >= 2 ? 3 : 0);
        var swap = hand.OrderBy(Keep).Where(c => Keep(c) < _t.MulliganBelow).Take(Game.MaxMulligans).Select(c => c.Uid).ToList();
        return new Mulligan(me, swap);
    }

    // ---------- Normal tur ----------

    private sealed record Cand(GameAction Action, int Gain, double Cost);

    private GameAction DecideTurn(Game g, int me)
    {
        var self = g.Players[me];
        var opp = g.Players[1 - me];
        var lead = Lead(g, me);
        var cands = Candidates(g, me, lead);
        var pass = new Pass(me);

        // Kart avantajı sağlayan hamleler (casus gibi) öndeyken ve lideri bozmuyorsa iyidir. Rakip pas geçmişse yapılmaz:
        // öndeyken pas geçip raundu bitirmek beklenir (arka arkaya casus/Yem oynamak kart dökmek gibi görünür).
        if (g.Round < 3 && !opp.Passed)
        {
            var free = cands.Where(c => c.Cost <= -2 && lead + c.Gain > 0).OrderBy(c => c.Cost).FirstOrDefault();
            if (free != null) return free.Action;
        }

        if (lead > 0)
        {
            if (opp.Passed) return pass;
            var oppPotential = opp.Hand.Count * _t.AvgCardPoints + (opp.LeaderUsed ? 0 : 3);
            if (lead >= _t.PassAheadFactor * oppPotential) return pass;
            // Kıl payı öndeyim: rakip yanıt verene dek en ucuz hamleyle önde kalırım.
            var keep = Pick(cands.Where(c => c.Gain > 0));
            return keep?.Action ?? pass;
        }

        var need = -lead + 1;
        var mustWin = self.Gems == 1; // bu raundu kaybedersem oyun biter
        var potential = Valuation.PotentialPoints(self.Hand) + (g.LeaderAvailable(me) ? 4 : 0);
        if (!mustWin && need > potential * _t.ConcedeFactor) return pass; // kartlarımı sonraki raunda saklarım

        // Yıpratma savaşı: toplam gücüm (saha + el) rakibinkinden azsa bu raundu kazanamam; kartlarımı saklarım.
        if (!mustWin && _t.RelativeConcedeFactor > 0)
        {
            var myTotalPot = g.PlayerTotal(me) + potential;
            var oppTotalPot = g.PlayerTotal(1 - me) + (opp.Passed ? 0 : opp.Hand.Count * _t.AvgCardPoints + (opp.LeaderUsed ? 0 : 3));
            if (myTotalPot < oppTotalPot * _t.RelativeConcedeFactor) return pass;
        }

        var enough = Pick(cands.Where(c => c.Gain >= need));
        if (enough != null) return enough.Action;

        var best = cands.Where(c => c.Gain > 0 || c.Cost < -1).OrderByDescending(c => c.Gain - _t.Lambda * c.Cost).FirstOrDefault();
        return best?.Action ?? pass;
    }

    private List<Cand> Candidates(Game g, int me, int lead)
    {
        var self = g.Players[me];
        var handBefore = self.Hand.Select(c => c.Uid).ToHashSet();
        var deckBefore = self.Deck.Select(c => c.Uid).ToHashSet();
        var valueBefore = Valuation.HandValue(self.Hand);
        var list = new List<Cand>();

        foreach (var a in g.LegalActions())
        {
            if (a is Pass) continue;
            var sim = Simulate(g, a, me);
            if (sim == null) continue;

            var gain = LeadAfter(sim, g, me) - lead;

            // Elin değer değişimi: destenden gelen kartların kimliğini bilmiyoruz → ortalama değer.
            double valueAfter = 0;
            foreach (var c in sim.Players[me].Hand)
                valueAfter += handBefore.Contains(c.Uid) || !deckBefore.Contains(c.Uid) ? Valuation.CardValue(c.Def) : _t.UnknownCardValue;
            var cost = valueBefore - valueAfter;
            if (a is UseLeader) cost = g.Round == 3 ? 0 : _t.LeaderCost;

            list.Add(new Cand(a, gain, cost));
        }
        return list;
    }

    /// <summary>Eylemi kopya üzerinde dener; çıkan seçimleri aynı sezgiyle çözer. Raund bitiminde sahalar sıfırlandığı için sonuç geçmişten okunur.</summary>
    private Game? Simulate(Game g, GameAction a, int me)
    {
        var sim = g.Clone();
        if (!sim.Apply(a).Ok) return null;
        for (var i = 0; i < 8 && sim.Pending != null; i++)
            if (!sim.Apply(ChoosePending(sim, sim.Pending.Player)).Ok) return null;
        return sim;
    }

    private static int Lead(Game g, int me) => g.PlayerTotal(me) - g.PlayerTotal(1 - me);

    private static int LeadAfter(Game sim, Game before, int me)
    {
        if (sim.History.Count > before.History.Count)
        {
            var r = sim.History[^1];
            return me == 0 ? r.Score0 - r.Score1 : r.Score1 - r.Score0;
        }
        return Lead(sim, me);
    }

    // ---------- Bekleyen seçimler (kopyalanamaz, bu yüzden saf sezgi) ----------

    private GameAction ChoosePending(Game g, int me)
    {
        var pend = g.Pending!;
        var actions = g.LegalActions().OfType<Choose>().ToList();
        if (actions.Count == 0) throw new InvalidOperationException("Bekleyen seçim için geçerli eylem yok.");

        CardInstance? Find(int uid) => g.Players.SelectMany(p => p.Hand.Concat(p.Deck).Concat(p.Grave)).FirstOrDefault(c => c.Uid == uid);

        switch (pend.Kind)
        {
            case ChoiceKind.AgileRow:
            {
                var card = pend.LimboCard!;
                return actions.OrderByDescending(a => AgileRowScore(g, me, card, a.Row!.Value)).First();
            }
            case ChoiceKind.Medic:
                return actions
                    .OrderByDescending(a => ReviveValue(Find(a.Cards[0])!) + (a.Row is { } r ? AgileRowScore(g, me, Find(a.Cards[0])!, r) * 0.01 : 0))
                    .First();
            case ChoiceKind.RelentlessPick:
            case ChoiceKind.BringerOfDeath:
                return actions.OrderByDescending(a => ReviveValue(Find(a.Cards[0])!)).First();
            case ChoiceKind.DestroyerDiscard:
                return actions.OrderBy(a => a.Cards.Sum(u => Valuation.CardValue(Find(u)!.Def))).First();
            case ChoiceKind.DestroyerDraw:
                return actions.OrderByDescending(a => Valuation.CardValue(Find(a.Cards[0])!.Def)).First();
            case ChoiceKind.KingWeather:
                return actions.OrderByDescending(a => WeatherBenefit(g, me, Find(a.Cards[0])!.Def)).First();
            default:
                return actions[0];
        }
    }

    /// <summary>Mezarlıktan geri alınacak kartın değeri. Casusun gücü rakibe yazıldığı için puan değil kart avantajı sayılır.</summary>
    private static double ReviveValue(CardInstance c) =>
        c.Def.Has("spy") ? 6 : c.Def.Strength + (c.Def.Has("medic") ? 3 : 0) + (c.Def.Has("morale") ? 2 : 0);

    /// <summary>Çevik birim için bir sıranın çekiciliği: hava, moral, boynuz ve bağ etkilerini kabaca hesaplar.</summary>
    private static double AgileRowScore(Game g, int me, CardInstance card, RowKind row)
    {
        var r = g.Players[me].Row(row);
        double v = card.Def.Strength;
        if (!card.Def.Hero && g.WeatherActive(row)) v = 1;
        v += r.Cards.Count(c => c.Has("morale") && c != card);
        var horn = r.Special?.Has("horn") == true || r.Cards.Any(c => c.Has("horn"));
        if (horn && !card.Def.Hero) v *= 2;
        if (card.Has("morale")) v += r.Cards.Count(c => c.Def.IsUnit && !c.Def.Hero);
        return v;
    }

    /// <summary>Bir hava kartının benim lehime net etkisi: rakibin kaybı − benim kaybım.</summary>
    private static double WeatherBenefit(Game g, int me, CardDef weather)
    {
        double Loss(int side, RowKind row) =>
            g.Players[side].Row(row).Cards.Where(c => c.Def.IsUnit && !c.Def.Hero).Sum(c => Math.Max(0, g.CardScore(c) - 1));

        var rows = new List<RowKind>();
        if (weather.Has("frost")) rows.Add(RowKind.Close);
        if (weather.Has("fog")) rows.Add(RowKind.Ranged);
        if (weather.Has("rain")) rows.Add(RowKind.Siege);
        if (weather.Has("storm")) { rows.Add(RowKind.Ranged); rows.Add(RowKind.Siege); }
        return rows.Sum(r => Loss(1 - me, r) - Loss(me, r));
    }
}
