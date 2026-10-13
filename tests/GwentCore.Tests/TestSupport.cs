using GwentCore;
using Xunit;

namespace GwentCore.Tests;

internal static class T
{
    /// <summary>Elleri ve desteleri boş, 1. raunda hazır bir oyun. Testler kartları kendisi dağıtır.</summary>
    public static Game New(string f0 = "northern_realms", string f1 = "monsters", string? l0 = null, string? l1 = null,
        int first = 0, GameOptions? options = null, int seed = 1)
    {
        var d0 = DeckValidator.DefaultDeck(f0, options);
        var d1 = DeckValidator.DefaultDeck(f1, options);
        if (l0 != null) d0 = d0 with { LeaderId = l0 };
        if (l1 != null) d1 = d1 with { LeaderId = l1 };
        var g = new Game(d0, d1, seed, options);
        g.TestEmpty();
        g.TestStart(first);
        return g;
    }

    public static void Ok(Game g, GameAction a)
    {
        var r = g.Apply(a);
        Assert.True(r.Ok, $"{a} reddedildi: {r.Error}");
    }

    public static void Fails(Game g, GameAction a, string? contains = null)
    {
        var before = g.Events.Count;
        var r = g.Apply(a);
        Assert.False(r.Ok, $"{a} kabul edilmemeliydi.");
        Assert.Equal(before, g.Events.Count); // reddedilen eylem durumu değiştirmemeli
        if (contains != null) Assert.Contains(contains, r.Error);
    }

    public static void Play(Game g, int player, CardInstance c, RowKind? row = null, int? target = null) =>
        Ok(g, new PlayCard(player, c.Uid, row, target));

    public static void Pass(Game g, int player) => Ok(g, new Pass(player));

    /// <summary>Elin boşalıp oyuncunun kendiliğinden pas geçmemesi için bol kart verir.</summary>
    public static void Filler(Game g, int player, int count = 6)
    {
        for (var i = 0; i < count; i++) g.TestHand(player, "redanian_foot_soldier");
    }

    public static int Total(Game g, int side) => g.PlayerTotal(side);
}
