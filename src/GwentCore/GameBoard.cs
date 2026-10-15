namespace GwentCore;

public sealed partial class Game
{
    // ---------- Güç hesabı (tek merkez) ----------

    public bool WeatherActive(RowKind row) =>
        WeatherCards.Any(c =>
            (c.Has("frost") && row == RowKind.Close) ||
            (c.Has("fog") && row == RowKind.Ranged) ||
            (c.Has("rain") && row == RowKind.Siege) ||
            (c.Has("storm") && row is RowKind.Ranged or RowKind.Siege));

    private bool DoubleSpy =>
        Enumerable.Range(0, 2).Any(p => LeaderPassive(p, "eredin_breacc_glas_the_treacherous"));

    /// <summary>
    /// Bir kartın nihai gücü. Sıra: taban (+Svarog) → hava (Perun muaf) (1'e ya da King Bran ile yarıya; komutanın satırı korunur) → arazi → casus çarpanı
    /// → Tight Bond (çarpar) → Morale (+1) → Boynuz (×2). Kahraman kartlar hiçbirinden etkilenmez.
    /// </summary>
    public int CardScore(CardInstance card)
    {
        if (!card.Def.IsUnit) return 0; // Decoy gibi sahada duran özel kartlar puan vermez
        var total = card.Def.Strength;
        if (card.Def.Hero) return total;
        if (card.Rune == RuneIds.Svarog) total += 1;

        var loc = Locate(card) ?? throw new InvalidOperationException($"{card} sahada değil.");
        var (side, rowKind, _) = loc;
        var row = _players[side].Row(rowKind);

        var weatherHit = WeatherActive(rowKind) && !CommanderShields(side, rowKind) && card.Rune != RuneIds.Perun;
        if (weatherHit)
        {
            var min = LeaderPassive(side, "king_bran") ? total / 2 : 1;
            total = Math.Min(min, total);
        }
        total = TerrainScore(total, side, rowKind, weatherHit);
        if (card.Rune == RuneIds.Stribog && Options.Commanders && _players[side].Commander == rowKind) total += 2;

        if (DoubleSpy && card.Has("spy")) total *= 2;

        if (card.Has("bond"))
        {
            var bond = row.Cards.Count(c => c.Has("bond") && c.Def.Id == card.Def.Id);
            if (bond > 1) total *= bond;
        }

        var morale = row.Cards.Count(c => c.Has("morale"));
        total += Math.Max(0, morale - (card.Has("morale") ? 1 : 0));

        var horn = row.Cards.Count(c => c.Has("horn")) + (row.Special?.Has("horn") == true ? 1 : 0);
        if (horn - (card.Has("horn") ? 1 : 0) > 0) total *= 2;
        return total;
    }

    public int RowTotal(int side, RowKind row) => _players[side].Row(row).Cards.Sum(CardScore);

    public int PlayerTotal(int side) => Enum.GetValues<RowKind>().Sum(r => RowTotal(side, r));

    // ---------- Saha işlemleri ----------

    internal (int Side, RowKind Row, int Index)? Locate(CardInstance card)
    {
        for (var s = 0; s < 2; s++)
            for (var r = 0; r < 3; r++)
            {
                var i = _players[s].Rows[r].Cards.IndexOf(card);
                if (i >= 0) return (s, (RowKind)r, i);
            }
        return null;
    }

    private IEnumerable<CardInstance> BoardCards()
    {
        foreach (var p in _players)
            foreach (var row in p.Rows)
                foreach (var c in row.Cards) yield return c;
    }

    private static bool IsTargetableUnit(CardInstance c) => c.Def.IsUnit && !c.Def.Hero;

    internal void PlaceOnBoard(CardInstance card, int side, RowKind row, int? index = null)
    {
        var list = _players[side].Row(row).Cards;
        if (index is { } i && i <= list.Count) list.Insert(i, card); else list.Add(card);
        card.Holder = side;
    }

    /// <summary>Kartı sahadan kaldırır (mezarlığa/ele göndermez). Avenger burada tetiklenir.</summary>
    private void RemoveFromBoard(CardInstance card, bool triggerAvenger = true)
    {
        var loc = Locate(card);
        if (loc == null) return;
        _players[loc.Value.Side].Rows[(int)loc.Value.Row].Cards.Remove(card);
        if (triggerAvenger && card.Has("avenger"))
        {
            if (_clearing) _clearedAvengers.Add(card); else SpawnAvenger(card);
        }
    }

    private void SpawnAvenger(CardInstance from)
    {
        if (from.Def.Summons == null) return;
        var token = NewCard(from.Def.Summons, from.Holder);
        if (!TryPlaceUnit(token, from.Holder, RowKind.Close)) return;
        Emit("spawn", from.Holder, token.Uid, token.Def.Id);
    }

    private void SendToGrave(CardInstance card)
    {
        if (card.Def.Token || card.Ephemeral) return; // jetonlar ve etki işaretçileri mezarlığa girmez
        _players[card.Holder].Grave.Add(card);
    }

    /// <summary>Yakma. Olay ayrıntısı "kimlik:güç" (günlük yakıldığı andaki gücü gösterir).</summary>
    private void Kill(CardInstance card)
    {
        var power = Locate(card) != null ? CardScore(card) : card.Def.Strength;
        RemoveFromBoard(card);
        Emit("kill", card.Holder, card.Uid, $"{card.Def.Id}:{power}");
        if (!VelesSaves(card)) SendToGrave(card);
    }

    // ---------- Scorch ----------

    /// <summary>Özel kart / Clan Dimun Pirate: iki sahadaki en güçlü (kahraman olmayan) birim(ler)i yok eder.</summary>
    private void ScorchAll(CardInstance? exclude = null)
    {
        var cands = BoardCards().Where(c => IsScorchable(c) && c != exclude).Select(c => (c, s: CardScore(c))).ToList();
        if (cands.Count == 0) return;
        var max = cands.Max(x => x.s);
        foreach (var (c, _) in cands.Where(x => x.s == max).ToList()) Kill(c);
    }

    /// <summary>Satır kısıtlı Scorch: rakibin o sıradaki toplamı 10 ve üzeriyse sıranın en güçlüsü yok olur.</summary>
    private bool RowScorch(int targetSide, RowKind row)
    {
        if (RowTotal(targetSide, row) < 10) return false;
        var cands = _players[targetSide].Row(row).Cards.Where(IsScorchable).Select(c => (c, s: CardScore(c))).ToList();
        if (cands.Count == 0) return false;
        var max = cands.Max(x => x.s);
        foreach (var (c, _) in cands.Where(x => x.s == max).ToList()) Kill(c);
        return true;
    }

    // ---------- Hava ----------

    private void ClearWeather()
    {
        foreach (var w in WeatherCards.ToList()) SendToGrave(w);
        WeatherCards.Clear();
    }

    private void PlayWeather(CardInstance card, int holder)
    {
        card.Holder = holder;
        if (card.Has("clear"))
        {
            ClearWeather();
            SendToGrave(card);
        }
        else
        {
            WeatherCards.Add(card);
        }
        Emit("weather", holder, card.Uid, card.Def.Id);
    }

    // ---------- Berserker / Mardroeme ----------

    private static bool MardroemePresent(BoardRow row) =>
        row.Special?.Has("mardroeme") == true || row.Cards.Any(c => c.Has("mardroeme"));

    private void TransformBerserker(CardInstance berserker)
    {
        var loc = Locate(berserker);
        if (loc == null || berserker.Def.TransformsInto == null) return;
        var (side, rowKind, idx) = loc.Value;
        RemoveFromBoard(berserker, triggerAvenger: false);
        var bear = NewCard(berserker.Def.TransformsInto, side);
        PlaceOnBoard(bear, side, rowKind, idx);
        Emit("transform", side, bear.Uid, bear.Def.Id);
    }

    private void TransformBerserkersIn(int side, RowKind rowKind)
    {
        foreach (var b in _players[side].Row(rowKind).Cards.Where(c => c.Has("berserker")).ToList())
            TransformBerserker(b);
    }
}
