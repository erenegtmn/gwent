namespace GwentCore;

// Yalnız testler içindir (InternalsVisibleTo). Üretim kodu bu yöntemleri çağırmaz.
public sealed partial class Game
{
    internal void TestEmpty()
    {
        foreach (var p in _players) { p.Hand.Clear(); p.Deck.Clear(); p.Grave.Clear(); }
    }

    internal void TestStart(int first)
    {
        foreach (var p in _players) p.MulliganDone = true;
        BeginRound(first);
        Pump();
    }

    internal CardInstance TestHand(int player, string id)
    {
        var c = NewCard(id, player);
        _players[player].Hand.Add(c);
        return c;
    }

    internal CardInstance TestDeck(int player, string id)
    {
        var c = NewCard(id, player);
        _players[player].Deck.Add(c);
        return c;
    }

    internal CardInstance TestGrave(int player, string id)
    {
        var c = NewCard(id, player);
        _players[player].Grave.Add(c);
        return c;
    }

    internal CardInstance TestBoard(int side, string id, RowKind row)
    {
        var c = NewCard(id, side);
        PlaceOnBoard(c, side, row);
        return c;
    }

    internal CardInstance TestSpecial(int side, string id, RowKind row)
    {
        var c = NewCard(id, side);
        _players[side].Row(row).Special = c;
        return c;
    }

    internal void TestWeather(string id, int holder = 0)
    {
        var c = NewCard(id, holder);
        WeatherCards.Add(c);
    }
}
