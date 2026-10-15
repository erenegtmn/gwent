namespace GwentCore;

// Kaer Morhen (TASARIM.md §5): arazi ve komutan figürü. İkisi de seçenekle açılır; kapalıyken klasik Gwent aynen işler.

/// <summary>Bir satırın arazisi. Her satırın tek bir kuralı vardır.</summary>
public enum Terrain
{
    /// <summary>Kural yok.</summary>
    Plain,
    /// <summary>Birimler +1 (satırda hava varken bu bonus yok).</summary>
    Hill,
    /// <summary>En fazla <see cref="Game.BridgeCapacity"/> birim; doluyken elden birim oynanamaz, başka yoldan gelen düşer (mezarlığa).</summary>
    Bridge,
    /// <summary>Satırdaki birimler yakılamaz (Scorch ve satır yakmaları).</summary>
    Forest,
    /// <summary>Birimler −1 (en az 1).</summary>
    Swamp,
    /// <summary>Bu satıra konan Medic iki kez canlandırır.</summary>
    Ruins
}

/// <summary>
/// Savaş alanı: her raund için altı satırın arazisi (oyuncu 0: yakın, menzil, kuşatma; oyuncu 1: aynı sıra).
/// Listede olmayan raundlar son raundun arazisini kullanır. Raund programı maç başında bellidir (sürpriz değil, plan).
/// </summary>
public sealed record TerrainMap(string Id, string Name, IReadOnlyList<IReadOnlyList<Terrain>> Rounds)
{
    public Terrain At(int side, RowKind row, int round)
    {
        if (Rounds.Count == 0) return Terrain.Plain;
        var r = Rounds[Math.Clamp(round, 1, Rounds.Count) - 1];
        return r[side * 3 + (int)row];
    }

    /// <summary>İki tarafa aynı araziyi veren tek raundluk harita.</summary>
    public static TerrainMap Symmetric(string id, string name, Terrain close, Terrain ranged, Terrain siege) =>
        new(id, name, new[] { (IReadOnlyList<Terrain>)new[] { close, ranged, siege, close, ranged, siege } });
}

/// <summary>Komutan figürünü başka satıra taşır. Bir hamle sayılır; raund başına bir kez.</summary>
public sealed record MoveCommander(int Player, RowKind Row) : GameAction(Player);

public sealed partial class Game
{
    public const int BridgeCapacity = 4;

    /// <summary>Bu raundda bir satırın arazisi (arazi kapalıysa düzlük).</summary>
    public Terrain TerrainAt(int side, RowKind row) => Options.Terrain?.At(side, row, Round) ?? Terrain.Plain;

    /// <summary>Satırın havadan korunup korunmadığı: komutan figürü bulunduğu satırı korur.</summary>
    public bool CommanderShields(int side, RowKind row) => Options.Commanders && _players[side].Commander == row;

    public bool RowFull(int side, RowKind row) =>
        TerrainAt(side, row) == Terrain.Bridge && _players[side].Row(row).Cards.Count(c => c.Def.IsUnit) >= BridgeCapacity;

    /// <summary>Yakılabilir mi: kahraman değil ve ormanda değil.</summary>
    private bool IsScorchable(CardInstance c)
    {
        if (!IsTargetableUnit(c)) return false;
        return Locate(c) is not { } l || TerrainAt(l.Side, l.Row) != Terrain.Forest;
    }

    /// <summary>Arazinin güce etkisi (havadan sonra, çarpanlardan önce). Kahramanlar etkilenmez.</summary>
    private int TerrainScore(int total, int side, RowKind row, bool weatherHit) => TerrainAt(side, row) switch
    {
        Terrain.Hill when !weatherHit => total + 1,
        Terrain.Swamp => Math.Max(1, total - 1),
        _ => total
    };

    /// <summary>
    /// Bir birimi sahaya koyar; köprü doluysa birim düşer (mezarlığa) ve false döner.
    /// Elden oynamada bu durum zaten reddedilir; burası Muster, Medic, Avenger gibi yollar içindir.
    /// </summary>
    private bool TryPlaceUnit(CardInstance card, int side, RowKind row, int? index = null)
    {
        if (card.Def.IsUnit && RowFull(side, row))
        {
            card.Holder = side;
            SendToGrave(card);
            Emit("bridge_fall", side, card.Uid, card.Def.Id);
            return false;
        }
        PlaceOnBoard(card, side, row, index);
        return true;
    }

    private ActionResult DoMoveCommander(MoveCommander m)
    {
        var err = CheckTurn(m.Player);
        if (err != null) return ActionResult.Fail(err);
        if (!Options.Commanders) return ActionResult.Fail("Bu maçta komutan yok.");
        var p = _players[m.Player];
        if (p.CommanderMoved) return ActionResult.Fail("Komutanını bu raund zaten taşıdın.");
        if (p.Commander == m.Row) return ActionResult.Fail("Komutan zaten bu satırda.");
        p.Commander = m.Row;
        p.CommanderMoved = true;
        Emit("commander", m.Player, 0, m.Row.ToString());
        _work.Enqueue(EndTurn);
        Pump();
        return ActionResult.Success;
    }

    private IEnumerable<GameAction> CommanderActions()
    {
        var me = _players[Current];
        if (!Options.Commanders || me.CommanderMoved) yield break;
        foreach (var rk in Enum.GetValues<RowKind>())
            if (rk != me.Commander) yield return new MoveCommander(Current, rk);
    }

    /// <summary>Raund başı: komutan yeniden taşınabilir; arazi değiştiyse arayüz için olay.</summary>
    private void BeginRoundTerrain()
    {
        foreach (var p in _players) p.CommanderMoved = false;
        if (Options.Terrain is { } map)
        {
            var now = string.Join(",", Enumerable.Range(0, 6).Select(i => map.At(i / 3, (RowKind)(i % 3), Round)));
            Emit("terrain", -1, 0, now);
        }
    }
}
