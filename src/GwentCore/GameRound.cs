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

        for (var p = 0; p < 2; p++)
        {
            var player = p;
            // Northern Realms: bir önceki raundu kazandıysa 1 kart çeker.
            if (Round > 1 && _lastRoundWinner == player && _players[player].Faction == "northern_realms")
                _work.Enqueue(() => Draw(player, 1));
            // Skellige: 3. raundun başında mezarlıktan 2 rastgele birim sahaya iner.
            if (Round == 3 && _players[player].Faction == "skellige")
            {
                _work.Enqueue(() => SkelligeRevive(player));
                _work.Enqueue(() => SkelligeRevive(player));
            }
        }
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
        var s0 = PlayerTotal(0);
        var s1 = PlayerTotal(1);
        int? winner = s0 > s1 ? 0 : s1 > s0 ? 1 : null;
        if (winner == null)
        {
            // Nilfgaard beraberlikte kazanır (ikisi de Nilfgaard ise beraberlik kalır).
            var n0 = _players[0].Faction == "nilfgaard";
            var n1 = _players[1].Faction == "nilfgaard";
            if (n0 && !n1) winner = 0;
            else if (n1 && !n0) winner = 1;
        }

        History.Add(new RoundResult(Round, s0, s1, winner));
        Emit("round_end", winner ?? -1, 0, $"{s0}:{s1}");

        if (winner == null) { _players[0].Gems--; _players[1].Gems--; }
        else _players[1 - winner.Value].Gems--;

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

        ClearBoard();

        var g0 = _players[0].Gems;
        var g1 = _players[1].Gems;
        if (g0 <= 0 || g1 <= 0)
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
