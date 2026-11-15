using HanLigi;
using Xunit.Abstractions;

namespace HanLigi.Tests;

/// <summary>Güçlü botun ölçümü: klasik botlara karşı ortalama bitiş sırası (8 kişide 4,5 = ortalama; küçük daha iyi).</summary>
public class ExpertBotTests(ITestOutputHelper output)
{
    /// <summary>Maçı sonuna kadar oynatır; <paramref name="expert"/> koltukları güçlü botla.</summary>
    private static Match Play(int seed, Func<int, bool> expert, bool prep = true, bool window = true)
    {
        var m = new Match(seed, Enumerable.Range(0, 8).Select(i => ($"B{i}", true)).ToList());
        var rng = new Random(seed * 17 + 3);
        foreach (var p in m.Players) m.ChooseSchool(p.Id, p.SchoolOptions[rng.Next(3)]);
        for (var guard = 0; guard < 80 && m.Phase != Phase.Finished; guard++)
        {
            foreach (var p in m.Alive.ToList())
                if (expert(p.Id) && prep) ExpertBot.Prep(m, p.Id, rng); else HanBot.Prep(m, p.Id, rng);
            if (m.Phase == Phase.Prep) m.ForceLock();
            foreach (var b in m.Battles)
            {
                b.Start();
                while (!b.Done)
                {
                    for (var s = 0; s < 2; s++)
                    {
                        var pid = b.Sides[s].PlayerId;
                        var isExpert = window && pid >= 0 && !(s == 1 && (b.Pve || b.Ghost)) && expert(pid);
                        b.Submit(s, isExpert ? ExpertBot.Window(b, s, rng) : HanBot.Window(b, s, rng));
                    }
                    b.Resolve();
                }
            }
            m.EndRound();
        }
        return m;
    }

    [Fact]
    public void Sweep_parameters()
    {
        if (Environment.GetEnvironmentVariable("HAN_BOT_SWEEP") == null) return;
        var games = 160;
        double Run() { var pl = new List<int>(); for (var i = 0; i < games; i++) { var seat = i % 8; pl.Add(Play(7000 + i, pid => pid == seat).Players[seat].Place); } return pl.Average(); }
        void Try(string name, Action set, Action reset) { set(); output.WriteLine($"{name}: {Run():F2}"); reset(); }
        output.WriteLine($"temel: {Run():F2}");
        Try("gürültü 0.5", () => ExpertBot.K.Noise = 0.5, () => ExpertBot.K.Noise = 1);
        Try("gürültü 0.2", () => ExpertBot.K.Noise = 0.2, () => ExpertBot.K.Noise = 1);
        Try("gürültü 0", () => ExpertBot.K.Noise = 0, () => ExpertBot.K.Noise = 1);
    }

    [Fact]
    public void Expert_bot_beats_the_classic_bots()
    {
        var games = int.TryParse(Environment.GetEnvironmentVariable("HAN_BOT_GAMES"), out var g) ? g : 80;
        var solo = new List<int>();
        var half = new List<int>();
        var halfClassic = new List<int>();
        for (var i = 0; i < games; i++)
        {
            var seat = i % 8;
            var m = Play(5000 + i, pid => pid == seat);
            solo.Add(m.Players[seat].Place);
            var h = Play(9000 + i, pid => pid % 2 == i % 2);
            half.AddRange(h.Players.Where(p => p.Id % 2 == i % 2).Select(p => p.Place));
            halfClassic.AddRange(h.Players.Where(p => p.Id % 2 != i % 2).Select(p => p.Place));
        }
        output.WriteLine($"1 güçlü + 7 klasik: güçlü botun ortalama sırası {solo.Average():F2}, ilk 4 oranı %{100.0 * solo.Count(x => x <= 4) / solo.Count:F0}, birincilik %{100.0 * solo.Count(x => x == 1) / solo.Count:F0}");
        output.WriteLine($"4 güçlü + 4 klasik: güçlü {half.Average():F2}, klasik {halfClassic.Average():F2}");
        Assert.True(solo.Average() < 4.5, $"güçlü bot ortalamanın altında: {solo.Average():F2}");
    }
}
