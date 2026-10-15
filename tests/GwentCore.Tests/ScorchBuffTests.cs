using GwentCore;
using Xunit;
using static GwentCore.RowKind;

namespace GwentCore.Tests;

/// <summary>
/// Yak (Scorch) ve sıra yakması (Villentretenmerth) kartın **sahadaki** gücüne bakar: hava, bağ, moral ve boynuz dahil.
/// Taban güce göre seçmesi hata olur (oyuncu bildirimi). Sayılar elle hesaplanmıştır.
/// </summary>
public class ScorchBuffTests
{
    [Fact]
    public void Scorch_burns_the_horned_card_not_the_higher_base_card()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var five = g.TestBoard(0, "siegfried_of_denesle", Close);     // 5
        var horn = g.TestHand(0, "commanders_horn");
        var six = g.TestBoard(1, "fiend", Close);                     // 6
        var six2 = g.TestBoard(1, "earth_elemental", Siege);          // 6 (tabanı Siegfried'den yüksek)
        T.Play(g, 0, horn, Close);                                    // Siegfried 5 → 10
        T.Pass(g, 1); // sıra bana geçsin diye rakip pas
        Assert.Equal(10, g.CardScore(five));
        Assert.Equal(6, g.CardScore(six));
        var scorch = g.TestHand(0, Ids.Scorch);
        T.Play(g, 0, scorch);
        Assert.DoesNotContain(five, g.Players[0].Row(Close).Cards);  // boynuzlu 10'luk yandı
        Assert.Contains(six, g.Players[1].Row(Close).Cards);
        Assert.Contains(six2, g.Players[1].Row(Siege).Cards);
    }

    [Fact]
    public void Scorch_counts_morale_and_ignores_weathered_base_strength()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var catapult = g.TestBoard(0, "catapult", Siege);             // 8 (tek katapult: bağ yok)
        var m1 = g.TestBoard(0, "kaedweni_siege_expert", Siege);      // moral +1
        var m2 = g.TestBoard(0, "kaedweni_siege_expert", Siege);      // moral +1 → katapult 10
        var frozen = g.TestBoard(1, "fiend", Close);                  // 6, donla 1
        var other = g.TestBoard(1, "earth_elemental", Siege);         // 6
        var frost = g.TestHand(0, "biting_frost");
        T.Play(g, 0, frost);
        T.Pass(g, 1);
        Assert.Equal(10, g.CardScore(catapult));
        Assert.Equal(1, g.CardScore(frozen));
        T.Play(g, 0, g.TestHand(0, Ids.Scorch));
        Assert.DoesNotContain(catapult, g.Players[0].Row(Siege).Cards);  // moralli 10'luk yandı
        Assert.Contains(frozen, g.Players[1].Row(Close).Cards);       // tabanı 6 ama donda 1
        Assert.Contains(other, g.Players[1].Row(Siege).Cards);
        Assert.Contains(m1, g.Players[0].Row(Siege).Cards);
        Assert.Contains(m2, g.Players[0].Row(Siege).Cards);
    }

    [Fact]
    public void Villentretenmerth_burns_the_strongest_close_card_counting_the_horn()
    {
        var g = T.New();
        T.Filler(g, 0); T.Filler(g, 1);
        var horned = g.TestBoard(1, "werewolf", Close);               // 5
        var plain = g.TestBoard(1, "fiend", Close);                   // 6
        g.TestBoard(1, "dandelion", Close);                           // boynuz: kurt adam 10, fiend 12, dandelion 2
        Assert.Equal(10, g.CardScore(horned));
        Assert.Equal(12, g.CardScore(plain));
        var villen = g.TestHand(0, "villentretenmerth");
        T.Play(g, 0, villen, Close);
        Assert.DoesNotContain(plain, g.Players[1].Row(Close).Cards);  // boynuzlu 12 (taban 6) yandı
        Assert.Contains(horned, g.Players[1].Row(Close).Cards);
    }

    [Fact]
    public void Villentretenmerth_prefers_buffed_strength_over_higher_base()
    {
        var g = T.New("northern_realms", "northern_realms");
        T.Filler(g, 0); T.Filler(g, 1);
        var horned = g.TestBoard(1, "siegfried_of_denesle", Close);  // 5 → 10 (Dandelion boynuzu)
        var ranged = g.TestBoard(1, "dethmold", Ranged);              // 6: sıra yakması yalnız yakın sıraya bakar
        var low = g.TestBoard(1, "redanian_foot_soldier", Close);     // 1 → 2
        g.TestBoard(1, "dandelion", Close);                           // boynuz
        Assert.Equal(10, g.CardScore(horned));
        T.Play(g, 0, g.TestHand(0, "villentretenmerth"), Close);
        Assert.DoesNotContain(horned, g.Players[1].Row(Close).Cards);
        Assert.Contains(low, g.Players[1].Row(Close).Cards);
        Assert.Contains(ranged, g.Players[1].Row(Ranged).Cards);
    }
}
