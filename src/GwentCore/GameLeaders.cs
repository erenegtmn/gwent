namespace GwentCore;

public sealed partial class Game
{
    private enum LeaderKind
    {
        Passive,
        PlayFromDeck, // belirli bir hava kartını destenden oynar
        ClearWeather,
        HornRow,
        ScorchRow,
        PeekHand,
        Relentless,
        BringerOfDeath,
        Destroyer,
        KingWeather,
        Crach,
        Hope
    }

    private static readonly Dictionary<string, (LeaderKind Kind, string? Param, RowKind Row)> LeaderTable = new()
    {
        ["foltest_king_of_temeria"] = (LeaderKind.PlayFromDeck, Ids.ImpenetrableFog, RowKind.Close),
        ["foltest_lord_commander_of_the_north"] = (LeaderKind.ClearWeather, null, RowKind.Close),
        ["foltest_the_siegemaster"] = (LeaderKind.HornRow, null, RowKind.Siege),
        ["foltest_the_steel_forged"] = (LeaderKind.ScorchRow, null, RowKind.Siege),
        ["foltest_son_of_medell"] = (LeaderKind.ScorchRow, null, RowKind.Ranged),
        ["emhyr_var_emreis_his_imperial_majesty"] = (LeaderKind.PlayFromDeck, Ids.TorrentialRain, RowKind.Close),
        ["emhyr_var_emreis_emperor_of_nilfgaard"] = (LeaderKind.PeekHand, null, RowKind.Close),
        ["emhyr_var_emreis_the_white_flame"] = (LeaderKind.Passive, null, RowKind.Close),
        ["emhyr_var_emreis_the_relentless"] = (LeaderKind.Relentless, null, RowKind.Close),
        ["emhyr_var_emreis_invader_of_the_north"] = (LeaderKind.Passive, null, RowKind.Close),
        ["eredin_commander_of_the_red_riders"] = (LeaderKind.HornRow, null, RowKind.Close),
        ["eredin_bringer_of_death"] = (LeaderKind.BringerOfDeath, null, RowKind.Close),
        ["eredin_destroyer_of_worlds"] = (LeaderKind.Destroyer, null, RowKind.Close),
        ["eredin_king_of_the_wild_hunt"] = (LeaderKind.KingWeather, null, RowKind.Close),
        ["eredin_breacc_glas_the_treacherous"] = (LeaderKind.Passive, null, RowKind.Close),
        ["francesca_findabair_queen_of_dol_blathanna"] = (LeaderKind.ScorchRow, null, RowKind.Close),
        ["francesca_findabair_the_beautiful"] = (LeaderKind.HornRow, null, RowKind.Ranged),
        ["francesca_findabair_daisy_of_the_valley"] = (LeaderKind.Passive, null, RowKind.Close),
        ["francesca_findabair_pureblood_elf"] = (LeaderKind.PlayFromDeck, Ids.BitingFrost, RowKind.Close),
        ["francesca_findabair_hope_of_the_aen_seidhe"] = (LeaderKind.Hope, null, RowKind.Close),
        ["crach_an_craite"] = (LeaderKind.Crach, null, RowKind.Close),
        ["king_bran"] = (LeaderKind.Passive, null, RowKind.Close),
    };

    /// <summary>Rakibin lideri White Flame ise bu oyuncunun lider yeteneği iptaldir.</summary>
    public bool LeaderCancelled(int player) =>
        Opp(player).Leader.Id == "emhyr_var_emreis_the_white_flame";

    /// <summary>Lider yeteneği kullanılabilir durumda mı (kullanılmamış, iptal edilmemiş, etkin tipte)?</summary>
    public bool LeaderAvailable(int player)
    {
        var p = _players[player];
        if (p.LeaderUsed || LeaderCancelled(player)) return false;
        return LeaderTable.TryGetValue(p.Leader.Id, out var e) && e.Kind != LeaderKind.Passive;
    }

    private bool LeaderPassive(int player, string leaderId) =>
        _players[player].Leader.Id == leaderId && !LeaderCancelled(player);

    /// <summary>Lider şu an gerçekten işe yarar bir hedefe sahip mi? Boşa harcamayı engellemek için.</summary>
    public string? LeaderBlockedReason(int player)
    {
        var p = _players[player];
        if (!LeaderTable.TryGetValue(p.Leader.Id, out var e)) return "Bu liderin yeteneği tanımlı değil.";
        if (e.Kind == LeaderKind.Passive) return "Bu liderin yeteneği otomatiktir, kullanılmaz.";
        if (p.LeaderUsed) return "Lider yeteneğini zaten kullandın.";
        if (LeaderCancelled(player)) return "Rakibin lideri yeteneğini iptal etti.";
        switch (e.Kind)
        {
            case LeaderKind.PlayFromDeck:
                if (!p.Deck.Any(c => c.Def.Id == e.Param)) return $"Destede {Catalog.Get(e.Param!).Name} yok.";
                break;
            case LeaderKind.HornRow:
                if (p.Row(e.Row).Special != null) return "Bu sırada zaten bir Boynuz veya Mardroeme var.";
                break;
            case LeaderKind.ScorchRow:
                if (RowTotal(1 - player, e.Row) < 10) return "Rakibin o sıradaki toplam gücü 10'dan az.";
                break;
            case LeaderKind.PeekHand:
                if (Opp(player).Hand.Count == 0) return "Rakibin elinde kart yok.";
                break;
            case LeaderKind.Relentless:
                if (!MedicCandidates(1 - player).Any()) return "Rakibin mezarlığında alınabilecek birim yok.";
                break;
            case LeaderKind.BringerOfDeath:
                if (!MedicCandidates(player).Any()) return "Mezarlığında geri alınabilecek birim yok.";
                break;
            case LeaderKind.Destroyer:
                if (p.Hand.Count < 2) return "Atmak için elinde en az 2 kart olmalı.";
                if (p.Deck.Count < 1) return "Destede kart kalmadı.";
                break;
            case LeaderKind.KingWeather:
                if (!p.Deck.Any(c => c.Def.IsWeather)) return "Destende hava kartı yok.";
                break;
        }
        return null;
    }

    private ActionResult DoUseLeader(UseLeader a)
    {
        var err = CheckTurn(a.Player);
        if (err != null) return ActionResult.Fail(err);
        var reason = LeaderBlockedReason(a.Player);
        if (reason != null) return ActionResult.Fail(reason);

        var p = _players[a.Player];
        var e = LeaderTable[p.Leader.Id];
        p.LeaderUsed = true;
        Emit("leader", a.Player, 0, p.Leader.Id);
        // Arayüz için: yetenek hangi kartı oynamış gibi davranıyor (hava çağırma, Boynuz, Yak, hava temizleme)
        var shown = e.Kind switch
        {
            LeaderKind.PlayFromDeck => e.Param,
            LeaderKind.ClearWeather => Ids.ClearWeather,
            LeaderKind.HornRow => Ids.Horn,
            LeaderKind.ScorchRow => Ids.Scorch,
            _ => null
        };
        if (shown != null) Emit("leader_card", a.Player, 0, shown);

        switch (e.Kind)
        {
            case LeaderKind.PlayFromDeck:
            {
                var c = p.Deck.First(x => x.Def.Id == e.Param);
                p.Deck.Remove(c);
                PlayWeather(c, a.Player);
                break;
            }
            case LeaderKind.ClearWeather:
                ClearWeather();
                break;
            case LeaderKind.HornRow:
            {
                var marker = NewCard(Ids.Horn, a.Player);
                marker.Holder = a.Player;
                marker.Ephemeral = true;
                p.Row(e.Row).Special = marker;
                break;
            }
            case LeaderKind.ScorchRow:
                RowScorch(1 - a.Player, e.Row);
                break;
            case LeaderKind.PeekHand:
            {
                var hand = Opp(a.Player).Hand.ToList();
                var picks = new List<CardInstance>();
                while (picks.Count < 3 && hand.Count > 0)
                {
                    var c = _rng.Pick(hand);
                    hand.Remove(c);
                    picks.Add(c);
                }
                Emit("peek", a.Player, 0, string.Join(",", picks.Select(c => c.Def.Id)), visible: a.Player);
                break;
            }
            case LeaderKind.Relentless:
                SetPending(new PendingChoice
                {
                    Player = a.Player,
                    Kind = ChoiceKind.RelentlessPick,
                    Candidates = MedicCandidates(1 - a.Player).Select(c => c.Uid).ToList(),
                    Prompt = "Rakibin mezarlığından eline alacağın kartı seç.",
                    Resolve = r =>
                    {
                        var c = FindInZones(r.Cards[0])!;
                        _players[c.Holder].Grave.Remove(c);
                        c.Holder = a.Player;
                        p.Hand.Add(c);
                    }
                });
                break;
            case LeaderKind.BringerOfDeath:
                SetPending(new PendingChoice
                {
                    Player = a.Player,
                    Kind = ChoiceKind.BringerOfDeath,
                    Candidates = MedicCandidates(a.Player).Select(c => c.Uid).ToList(),
                    Prompt = "Mezarlığından eline alacağın kartı seç.",
                    Resolve = r =>
                    {
                        var c = FindInZones(r.Cards[0])!;
                        p.Grave.Remove(c);
                        p.Hand.Add(c);
                    }
                });
                break;
            case LeaderKind.Destroyer:
                SetPending(new PendingChoice
                {
                    Player = a.Player,
                    Kind = ChoiceKind.DestroyerDiscard,
                    Candidates = p.Hand.Select(c => c.Uid).ToList(),
                    Min = 2,
                    Max = 2,
                    Prompt = "Atılacak 2 kartı seç.",
                    Resolve = r =>
                    {
                        foreach (var uid in r.Cards)
                        {
                            var c = p.Hand.First(x => x.Uid == uid);
                            p.Hand.Remove(c);
                            p.Grave.Add(c);
                        }
                        SetPending(new PendingChoice
                        {
                            Player = a.Player,
                            Kind = ChoiceKind.DestroyerDraw,
                            Candidates = p.Deck.Select(c => c.Uid).ToList(),
                            Prompt = "Destenden çekeceğin kartı seç.",
                            Resolve = r2 =>
                            {
                                var c = p.Deck.First(x => x.Uid == r2.Cards[0]);
                                p.Deck.Remove(c);
                                p.Hand.Add(c);
                            }
                        });
                    }
                });
                break;
            case LeaderKind.KingWeather:
                SetPending(new PendingChoice
                {
                    Player = a.Player,
                    Kind = ChoiceKind.KingWeather,
                    Candidates = p.Deck.Where(c => c.Def.IsWeather).Select(c => c.Uid).ToList(),
                    Prompt = "Destenden oynayacağın hava kartını seç.",
                    Resolve = r =>
                    {
                        var c = p.Deck.First(x => x.Uid == r.Cards[0]);
                        p.Deck.Remove(c);
                        PlayWeather(c, a.Player);
                    }
                });
                break;
            case LeaderKind.Crach:
                foreach (var pl in _players)
                {
                    foreach (var c in pl.Grave) pl.Deck.Add(c);
                    pl.Grave.Clear();
                    _rng.Shuffle(pl.Deck);
                }
                break;
            case LeaderKind.Hope:
                DoHope(a.Player);
                break;
        }

        _work.Enqueue(EndTurn);
        Pump();
        return ActionResult.Success;
    }

    /// <summary>Francesca: Hope — çevik birimleri toplam gücü en yükseğe çıkaran sıralara taşır.</summary>
    private void DoHope(int player)
    {
        var p = _players[player];
        var agile = p.Row(RowKind.Close).Cards.Concat(p.Row(RowKind.Ranged).Cards).Where(c => c.Def.IsAgile).ToList();
        if (agile.Count == 0) return;

        var start = agile.Select(c => p.Row(RowKind.Close).Cards.Contains(c) ? 0 : 1).ToArray();
        int Score() => RowTotal(player, RowKind.Close) + RowTotal(player, RowKind.Ranged);

        void Assign(int[] pattern)
        {
            foreach (var c in agile) { p.Row(RowKind.Close).Cards.Remove(c); p.Row(RowKind.Ranged).Cards.Remove(c); }
            for (var i = 0; i < agile.Count; i++) p.Row(pattern[i] == 0 ? RowKind.Close : RowKind.Ranged).Cards.Add(agile[i]);
        }

        var best = (int[])start.Clone();
        var bestScore = Score();
        var bestMoves = 0;
        for (var mask = 0; mask < 1 << agile.Count; mask++)
        {
            var pat = Enumerable.Range(0, agile.Count).Select(i => (mask >> i) & 1).ToArray();
            Assign(pat);
            var s = Score();
            var moves = pat.Where((v, i) => v != start[i]).Count();
            if (s > bestScore || (s == bestScore && moves < bestMoves))
            {
                best = pat; bestScore = s; bestMoves = moves;
            }
        }
        Assign(best);
        Emit("hope", player, 0, bestMoves.ToString());
    }
}
