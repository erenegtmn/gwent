namespace GwentCore;

public sealed partial class Game
{
    /// <summary>
    /// Şu an yapılabilecek tüm geçerli eylemler (yapay zekâ ve testler için). Bekleyen bir seçim ya da
    /// kart değiştirme aşaması varsa ilgili oyuncuların eylemleri; yoksa sırası gelen oyuncunun eylemleri.
    /// </summary>
    public IEnumerable<GameAction> LegalActions()
    {
        if (Phase == Phase.GameOver) yield break;

        if (Phase == Phase.Mulligan)
        {
            foreach (var p in _players.Where(x => !x.MulliganDone))
            {
                yield return new Mulligan(p.Index, Array.Empty<int>());
                foreach (var c in p.Hand) yield return new Mulligan(p.Index, new[] { c.Uid });
                for (var i = 0; i < p.Hand.Count; i++)
                    for (var j = i + 1; j < p.Hand.Count; j++)
                    {
                        yield return new Mulligan(p.Index, new[] { p.Hand[i].Uid, p.Hand[j].Uid });
                        if (MulligansFor(p.Index) >= 3)
                            for (var k = j + 1; k < p.Hand.Count; k++)
                                yield return new Mulligan(p.Index, new[] { p.Hand[i].Uid, p.Hand[j].Uid, p.Hand[k].Uid });
                    }
            }
            yield break;
        }

        if (Phase == Phase.ChooseFirst)
        {
            yield return new ChooseFirst(Current, Current);
            yield return new ChooseFirst(Current, 1 - Current);
            yield break;
        }

        if (Pending != null)
        {
            foreach (var a in PendingActions(Pending)) yield return a;
            yield break;
        }

        var me = _players[Current];
        if (me.Passed) yield break;
        yield return new Pass(Current);
        if (LeaderBlockedReason(Current) == null) yield return new UseLeader(Current);
        foreach (var mc in CommanderActions()) yield return mc;

        foreach (var card in me.Hand)
        {
            var def = card.Def;
            if (def.IsUnit)
            {
                var side = def.Has("spy") ? 1 - Current : Current;
                if (def.IsAgile)
                {
                    if (!RowFull(side, RowKind.Close)) yield return new PlayCard(Current, card.Uid, RowKind.Close);
                    if (!RowFull(side, RowKind.Ranged)) yield return new PlayCard(Current, card.Uid, RowKind.Ranged);
                }
                else if (!RowFull(side, def.FixedRow!.Value)) yield return new PlayCard(Current, card.Uid);
            }
            else if (def.IsWeather || def.Id == Ids.Scorch)
            {
                yield return new PlayCard(Current, card.Uid);
            }
            else if (def.Id is Ids.Horn or Ids.Mardroeme)
            {
                foreach (var rk in Enum.GetValues<RowKind>())
                    if (me.Row(rk).Special == null) yield return new PlayCard(Current, card.Uid, rk);
            }
            else if (def.Id == Ids.Decoy)
            {
                foreach (var t in me.Rows.SelectMany(r => r.Cards).Where(IsTargetableUnit))
                    yield return new PlayCard(Current, card.Uid, null, t.Uid);
            }
        }
    }

    private IEnumerable<GameAction> PendingActions(PendingChoice pc)
    {
        var uids = pc.Candidates;
        if (pc.Kind == ChoiceKind.AgileRow)
        {
            foreach (var r in pc.Rows) yield return new Choose(pc.Player, new[] { uids[0] }, r);
            yield break;
        }

        if (pc.Max == 2)
        {
            for (var i = 0; i < uids.Count; i++)
                for (var j = i + 1; j < uids.Count; j++)
                    yield return new Choose(pc.Player, new[] { uids[i], uids[j] });
            yield break;
        }

        foreach (var uid in uids)
        {
            var c = FindInZones(uid);
            if (pc.Kind == ChoiceKind.Medic && c != null && c.Def.IsAgile)
            {
                yield return new Choose(pc.Player, new[] { uid }, RowKind.Close);
                yield return new Choose(pc.Player, new[] { uid }, RowKind.Ranged);
            }
            else yield return new Choose(pc.Player, new[] { uid });
        }
    }
}
