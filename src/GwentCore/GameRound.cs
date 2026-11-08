namespace GwentCore;

public sealed partial class Game
{
    /// <summary>Raundu başlatır: Monsters'ın tuttuğu kart serbest kalır, fraksiyon yetenekleri çalışır.</summary>
    private void BeginRound(int first)
    {
        Phase = Phase.Playing;
        FirstPlayer = first;
        Current = first;
        foreach (var c in BoardCards()) c.NoRemove = false;
        Emit("round_start", first, 0, Round.ToString());
        BeginRoundTerrain();
        if (Round == 1) CatPeek();
        if (Options.RoundWeather) CurseWeather(first);

        for (var p = 0; p < _players.Length; p++)
        {
            var player = p;
            // Üç kişi: 2. raunddan itibaren herkes 1 kart çeker (raund sayısı arttığı için; KURALLAR §24).
            if (ThreePlayer && Round > 1)
                _work.Enqueue(() => Draw(player, 1));
            // Northern Realms: bir önceki raundu kazandıysa 1 kart çeker.
            if (Round > 1 && _lastRoundWinner == player && _players[player].Faction == "northern_realms")
                _work.Enqueue(() => Draw(player, 1));
            // Kırlangıç iksiri: kaybettiği ya da berabere kaldığı raunddan sonra 1 kart
            if (Round > 1 && _lastRoundWinner != player && HasPotion(player, PotionIds.Swallow))
                _work.Enqueue(() => Draw(player, 1));
            // Skellige: 3. raundun başında mezarlıktan 2 rastgele birim sahaya iner.
            if (Round == 3 && _players[player].Faction == "skellige")
            {
                _work.Enqueue(() => SkelligeRevive(player));
                _work.Enqueue(() => SkelligeRevive(player));
            }
        }
    }

    /// <summary>"Bitmeyen Kış" laneti: raund rastgele bir hava kartıyla açılır. Kart kimsenin değildir (mezarlığa girmez).</summary>
    private void CurseWeather(int first)
    {
        var id = _rng.Pick(new List<string> { "biting_frost", "impenetrable_fog", "torrential_rain" });
        var card = NewCard(id, first);
        card.Ephemeral = true;
        Emit("curse_weather", -1, card.Uid, id);
        PlayWeather(card, first);
    }

    private void SkelligeRevive(int player)
    {
        var cands = MedicCandidates(player).ToList();
        if (cands.Count == 0) return;
        var card = _rng.Pick(cands);
        Emit("skellige_revive", player, card.Uid, card.Def.Id);
        ReviveFromGrave(card, player, null);
    }

    private void EndRound()
    {
        var scores = Enumerable.Range(0, _players.Length).Select(PlayerTotal).ToArray();
        var max = scores.Max();
        var top = Enumerable.Range(0, _players.Length).Where(p => scores[p] == max).ToList();
        int? winner = top.Count == 1 ? top[0] : null;
        if (winner == null)
        {
            // Nilfgaard beraberlikte kazanır (tepede birden çok Nilfgaard varsa beraberlik kalır).
            var nilf = top.Where(p => _players[p].Faction == "nilfgaard").ToList();
            if (nilf.Count == 1) winner = nilf[0];
        }
        var s0 = scores[0];
        var s1 = scores[1];

        History.Add(new RoundResult(Round, s0, s1, winner, ThreePlayer ? scores : null));
        Emit("round_end", winner ?? -1, 0, string.Join(":", scores));
        if (winner is { } rw) _players[rw].Wins++;

        // "Kan Bedeli" laneti: raundu kazanan elinden rastgele bir kart kaybeder
        if (Options.BloodPrice && winner is { } w && _players[w].Hand.Count > 0)
        {
            var lost = _rng.Pick(_players[w].Hand.ToList());
            _players[w].Hand.Remove(lost);
            SendToGrave(lost);
            Emit("curse_discard", w, lost.Uid, lost.Def.Id);
        }

        if (!ThreePlayer)
        {
            if (winner == null) { _players[0].Gems--; _players[1].Gems--; }
            else _players[1 - winner.Value].Gems--;
        }

        // Monsters: sahadaki birimlerden biri bir sonraki raunda kalır.
        foreach (var p in _players.Where(x => x.Faction == "monsters"))
        {
            var units = p.Rows.SelectMany(r => r.Cards).Where(IsTargetableUnit).ToList();
            if (units.Count > 0)
            {
                var keep = _rng.Pick(units);
                keep.NoRemove = true;
                Emit("monsters_keep", p.Index, keep.Uid, keep.Def.Id);
            }
        }

        MoranaKeeps();
        ClearBoard();

        if (ThreePlayer)
        {
            // İki raund kazanan alır; 6. raund sonunda en çok kazanan (eşitse beraberlik).
            var champ = _players.FirstOrDefault(x => x.Wins >= 2);
            if (champ != null || Round >= MaxRoundsThree)
            {
                int? champion = champ?.Index;
                if (champion == null)
                {
                    var best = _players.Max(x => x.Wins);
                    var leaders = _players.Where(x => x.Wins == best).ToList();
                    champion = leaders.Count == 1 ? leaders[0].Index : null;
                }
                Outcome = new GameOutcome(champion);
                Phase = Phase.GameOver;
                Emit("game_end", champion ?? -1, 0);
                return;
            }
        }
        var g0 = _players[0].Gems;
        var g1 = _players[1].Gems;
        if (!ThreePlayer && (g0 <= 0 || g1 <= 0))
        {
            Outcome = new GameOutcome(g0 > 0 ? 0 : g1 > 0 ? 1 : null);
            Phase = Phase.GameOver;
            Emit("game_end", Outcome.Winner ?? -1, 0);
            return;
        }

        _lastRoundWinner = winner;
        Round++;
        foreach (var p in _players) p.Passed = false;
        BeginRound(winner ?? FirstPlayer);
    }

    /// <summary>Saha ve hava temizlenir; kartlar mezarlığa gider. Avenger'lar sonradan jeton bırakır.</summary>
    private void ClearBoard()
    {
        _clearing = true;
        _clearedAvengers.Clear();
        foreach (var p in _players)
        {
            foreach (var row in p.Rows)
            {
                foreach (var c in row.Cards.Where(x => !x.NoRemove).ToList())
                {
                    RemoveFromBoard(c);
                    SendToGrave(c);
                }
                if (row.Special != null)
                {
                    SendToGrave(row.Special);
                    row.Special = null;
                }
            }
        }
        ClearWeather();
        _clearing = false;
        foreach (var a in _clearedAvengers) SpawnAvenger(a);
        _clearedAvengers.Clear();
    }
}
