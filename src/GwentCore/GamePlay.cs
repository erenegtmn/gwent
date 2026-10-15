namespace GwentCore;

public sealed partial class Game
{
    private ActionResult DoPlayCard(PlayCard a)
    {
        var err = CheckTurn(a.Player);
        if (err != null) return ActionResult.Fail(err);
        var p = _players[a.Player];
        var card = p.Hand.FirstOrDefault(c => c.Uid == a.Card);
        if (card == null) return ActionResult.Fail("Bu kart elinde değil.");
        var def = card.Def;

        if (def.IsUnit)
        {
            RowKind row;
            if (def.IsAgile)
            {
                if (a.Row is not (RowKind.Close or RowKind.Ranged)) return ActionResult.Fail("Çevik birim için Yakın veya Menzilli sıra seçmelisin.");
                row = a.Row.Value;
            }
            else row = def.FixedRow!.Value;
            if (RowFull(def.Has("spy") ? 1 - a.Player : a.Player, row))
                return ActionResult.Fail($"Köprü dolu: bu satırda en fazla {BridgeCapacity} birim olabilir.");

            p.Hand.Remove(card);
            Emit("play", a.Player, card.Uid, def.Id);
            PlayUnit(card, a.Player, row);
        }
        else if (def.IsWeather)
        {
            p.Hand.Remove(card);
            Emit("play", a.Player, card.Uid, def.Id);
            PlayWeather(card, a.Player);
        }
        else if (def.IsSpecial)
        {
            switch (def.Id)
            {
                case Ids.Decoy:
                {
                    if (a.Target is not { } tuid) return ActionResult.Fail("Decoy için sahandan bir birim seçmelisin.");
                    var target = p.Rows.SelectMany(r => r.Cards).FirstOrDefault(c => c.Uid == tuid);
                    if (target == null || !IsTargetableUnit(target)) return ActionResult.Fail("Decoy yalnız kendi sahandaki kahraman olmayan bir birime uygulanabilir.");
                    p.Hand.Remove(card);
                    Emit("play", a.Player, card.Uid, def.Id);
                    DoDecoy(card, target, a.Player);
                    break;
                }
                case Ids.Horn:
                case Ids.Mardroeme:
                {
                    if (a.Row is not { } rk) return ActionResult.Fail("Bu kart için bir sıra seçmelisin.");
                    var row = p.Row(rk);
                    if (row.Special != null) return ActionResult.Fail("Bu sırada zaten bir Boynuz veya Mardroeme var.");
                    p.Hand.Remove(card);
                    Emit("play", a.Player, card.Uid, def.Id);
                    card.Holder = a.Player;
                    row.Special = card;
                    if (def.Id == Ids.Mardroeme) TransformBerserkersIn(a.Player, rk);
                    break;
                }
                case Ids.Scorch:
                    p.Hand.Remove(card);
                    Emit("play", a.Player, card.Uid, def.Id);
                    card.Holder = a.Player;
                    ScorchAll();
                    SendToGrave(card);
                    break;
                default:
                    return ActionResult.Fail("Bu kart oynanamaz.");
            }
        }
        else return ActionResult.Fail("Bu kart oynanamaz.");

        _work.Enqueue(EndTurn);
        Pump();
        return ActionResult.Success;
    }

    private void DoDecoy(CardInstance decoy, CardInstance target, int player)
    {
        var (side, row, idx) = Locate(target)!.Value;
        RemoveFromBoard(target); // Avenger (Cow/Kambi) sahadan kalkınca tetiklenir
        PlaceOnBoard(decoy, side, row, idx);
        target.Holder = player;
        _players[player].Hand.Add(target);
        Emit("decoy", player, target.Uid, target.Def.Id);
    }

    /// <summary>
    /// Bir birimi oynar: elden, mezarlıktan (Medic/Skellige) ya da desteden. Casus rakip sahaya konur.
    /// Kart zaten kaynağından çıkarılmış olmalı.
    /// </summary>
    private void PlayUnit(CardInstance card, int actor, RowKind row)
    {
        var side = card.Has("spy") ? 1 - actor : actor;
        if (!TryPlaceUnit(card, side, row)) return;
        Emit("place", actor, card.Uid, $"{side}:{row}");
        OnPlaced(card, actor, allowMuster: true);
    }

    private void OnPlaced(CardInstance card, int actor, bool allowMuster)
    {
        foreach (var ab in card.Abilities.ToList())
        {
            switch (ab)
            {
                case "spy":
                    if (!BlackBloodBlocks(actor)) Draw(actor, 2);
                    break;
                case "medic":
                    StartMedic(actor);
                    // Harabe: Medic iki kez canlandırır (ikincisi ilk seçim bitince, tur bitmeden)
                    if (Locate(card) is { } ml && TerrainAt(ml.Side, ml.Row) == Terrain.Ruins)
                        _front.Enqueue(() => StartMedic(actor));
                    break;
                case "muster":
                    if (allowMuster) DoMuster(card, actor);
                    break;
                case "scorch":
                    ScorchAll(exclude: card);
                    break;
                case "scorch_c": RowScorch(1 - actor, RowKind.Close); break;
                case "scorch_r": RowScorch(1 - actor, RowKind.Ranged); break;
                case "scorch_s": RowScorch(1 - actor, RowKind.Siege); break;
                case "berserker":
                    if (Locate(card) is { } l && MardroemePresent(_players[l.Side].Row(l.Row))) TransformBerserker(card);
                    break;
                case "mardroeme":
                    if (Locate(card) is { } m) TransformBerserkersIn(m.Side, m.Row);
                    break;
            }
        }
    }

    private void DoMuster(CardInstance card, int actor)
    {
        var group = card.Def.MusterGroup;
        if (group == null) return;
        var p = _players[actor];
        var found = p.Hand.Where(c => c.Def.MusterGroup == group && c != card).Select(c => (c, zone: p.Hand))
            .Concat(p.Deck.Where(c => c.Def.MusterGroup == group && c != card).Select(c => (c, zone: p.Deck)))
            .ToList();
        if (found.Count == 0) return;
        Emit("muster", actor, card.Uid, group);
        foreach (var (c, zone) in found)
        {
            zone.Remove(c);
            var row = c.Def.FixedRow ?? RowKind.Close;
            if (!TryPlaceUnit(c, actor, row)) continue;
            Emit("place", actor, c.Uid, $"{actor}:{row}");
            OnPlaced(c, actor, allowMuster: false);
        }
    }

    private IEnumerable<CardInstance> MedicCandidates(int player) =>
        _players[player].Grave.Where(c => IsTargetableUnit(c) && !c.Def.Token);

    private void StartMedic(int actor)
    {
        var cands = MedicCandidates(actor).ToList();
        if (cands.Count == 0) return;

        if (AnyRandomRespawn())
        {
            // Emhyr: Invader of the North — geri getirilen birim rastgele seçilir.
            var pick = _rng.Pick(cands);
            ReviveFromGrave(pick, actor, null);
            return;
        }

        SetPending(new PendingChoice
        {
            Player = actor,
            Kind = ChoiceKind.Medic,
            Candidates = cands.Select(c => c.Uid).ToList(),
            Prompt = "Mezarlığından geri getireceğin birimi seç.",
            Resolve = r =>
            {
                var card = FindInZones(r.Cards[0])!;
                ReviveFromGrave(card, actor, r.Row);
            }
        });
    }

    private bool AnyRandomRespawn() =>
        Enumerable.Range(0, 2).Any(p => LeaderPassive(p, "emhyr_var_emreis_invader_of_the_north"));

    /// <summary>Mezarlıktaki birimi oynar. Çevik ise sıra verilmediyse oyuncudan ayrıca sorulur.</summary>
    private void ReviveFromGrave(CardInstance card, int actor, RowKind? row)
    {
        _players[card.Holder].Grave.Remove(card);
        if (card.Def.IsAgile)
        {
            if (row is RowKind.Close or RowKind.Ranged)
            {
                PlayUnit(card, actor, row.Value);
            }
            else
            {
                // Rastgele geri getirmede sıra oyuncuya sorulur.
                AskAgileRow(card, actor);
            }
        }
        else PlayUnit(card, actor, card.Def.FixedRow!.Value);
    }

    private void AskAgileRow(CardInstance card, int actor)
    {
        // Kart kaynağından çıkarılmış durumda; seçim bitene kadar hiçbir bölgede değildir.
        SetPending(new PendingChoice
        {
            Player = actor,
            Kind = ChoiceKind.AgileRow,
            Candidates = new[] { card.Uid },
            LimboCard = card,
            Rows = new[] { RowKind.Close, RowKind.Ranged },
            Prompt = $"{card.Def.Name} için sıra seç.",
            Resolve = r => PlayUnit(card, actor, r.Row!.Value)
        });
    }

    // ---------- Tur sonu ----------

    private void EndTurn()
    {
        if (Phase != Phase.Playing) return;
        if (_players[0].Passed && _players[1].Passed)
        {
            EndRound();
            return;
        }

        var other = 1 - Current;
        if (!_players[other].Passed) Current = other;

        // Elinde kart kalmayan ve lideri de kullanılamayan oyuncu kendiliğinden pas geçer.
        var cur = _players[Current];
        if (!cur.Passed && cur.Hand.Count == 0 && !LeaderAvailable(Current))
        {
            cur.Passed = true;
            Emit("pass", Current, 0, "auto");
            EndTurn();
        }
    }
}
