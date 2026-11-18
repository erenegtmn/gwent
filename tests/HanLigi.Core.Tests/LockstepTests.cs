using HanLigi;

namespace HanLigi.Tests;

/// <summary>
/// Kilit adımlı online Han Ligi (<see cref="HanGame"/>): sunucu kopyası ve istemci kopyaları aynı eylemleri aynı sırayla uygular;
/// her adımda bütün kopyalar birebir aynı kalmalı. Botlar eylem akışında değil, kopyanın içinde (tohumla) oynar.
/// </summary>
public class LockstepTests
{
    /// <summary>Sunucuyu taklit eder: insanların eylemlerini üretir, sunucu kopyasında doğrular, geçerliyse bütün kopyalara uygular.</summary>
    private sealed class Sim
    {
        public readonly HanGame Server;
        public readonly List<HanGame> Clients = new();
        public readonly List<HanAction> Log = new();
        private readonly Random _rng;
        public int Rejected;

        public Sim(int seed, int humans, int size)
        {
            var seats = Enumerable.Range(0, size).Select(i => ($"P{i}", i >= humans)).ToList();
            Server = new HanGame(seed, seats);
            for (var i = 0; i < humans; i++) Clients.Add(new HanGame(seed, seats));
            _rng = new Random(seed * 31 + 7);
        }

        public bool Send(HanAction a)
        {
            var before = Server.Fingerprint();
            if (!Server.Apply(a)) { Rejected++; Assert.Equal(before, Server.Fingerprint()); return false; }
            Log.Add(a);
            foreach (var c in Clients) Assert.True(c.Apply(a), $"istemci kopyası reddetti: {a}");
            var fp = Server.Fingerprint();
            foreach (var c in Clients) Assert.Equal(fp, c.Fingerprint());
            return true;
        }

        /// <summary>Sunucunun kendiliğinden yaptıkları: round kapanışı.</summary>
        private void ServerDuties()
        {
            for (var guard = 0; guard < 200 && Server.RoundCanEnd; guard++) Send(new HanAction(HanActKind.EndRound, -1));
        }

        private IEnumerable<int> Humans => Enumerable.Range(0, Server.M.Players.Count).Where(i => !Server.Bots.Contains(i) && Server.M.Players[i].Alive);

        /// <summary>Bir insanın hazırlıkta yapabileceği rastgele eylemler (bazıları bilerek geçersiz).</summary>
        private void HumanPrep(int pid)
        {
            var p = Server.M.Players[pid];
            if (p.RewardOptions.Length > 0) Send(new HanAction(HanActKind.Reward, pid, _rng.Next(4)));
            if (p.PotionOptions.Length > 0) Send(new HanAction(HanActKind.Potion, pid, _rng.Next(3)));
            for (var k = 0; k < _rng.Next(2, 9); k++)
            {
                switch (_rng.Next(8))
                {
                    case 0: case 1: case 2: Send(new HanAction(HanActKind.Buy, pid, _rng.Next(6))); break;
                    case 3: if (p.Cards.Count > 0) Send(new HanAction(HanActKind.Move, pid, p.Cards[_rng.Next(p.Cards.Count)].Uid, _rng.Next(6))); break;
                    case 4: Send(new HanAction(HanActKind.Xp, pid)); break;
                    case 5: Send(new HanAction(HanActKind.Reroll, pid)); break;
                    case 6: if (p.Cards.Count > 0 && _rng.Next(4) == 0) Send(new HanAction(HanActKind.Sell, pid, p.Cards[_rng.Next(p.Cards.Count)].Uid)); break;
                    case 7 when p.Cards.Count >= 2 && _rng.Next(2) == 0:
                        Send(new HanAction(HanActKind.SwapCards, pid, p.Cards[_rng.Next(p.Cards.Count)].Uid, p.Cards[_rng.Next(p.Cards.Count)].Uid)); break;
                    default: Send(new HanAction(HanActKind.Buy, pid + 1, 0)); break;   // başkası adına: reddedilmeli
                }
            }
            foreach (var c in p.Bench.ToList()) Send(new HanAction(HanActKind.Move, pid, c.Uid, (int)Match.ZonesFor(c.Def).First()));
        }

        public void PlayToEnd(int maxSteps = 20000)
        {
            foreach (var h in Humans.ToList()) Send(new HanAction(HanActKind.School, h, (int)Server.M.Players[h].SchoolOptions[_rng.Next(3)]));
            for (var step = 0; step < maxSteps && Server.M.Phase != Phase.Finished; step++)
            {
                var m = Server.M;
                if (m.Phase == Phase.Prep)
                {
                    var hs = Humans.Where(h => !m.Players[h].Ready).ToList();
                    if (hs.Count == 0) { Send(new HanAction(HanActKind.Lock, -1)); ServerDuties(); continue; }
                    var h0 = hs[_rng.Next(hs.Count)];
                    HumanPrep(h0);
                    // bazen biri hazır demeden süre dolar
                    if (_rng.Next(10) == 0) Send(new HanAction(HanActKind.Lock, -1));
                    else Send(new HanAction(HanActKind.Ready, h0));
                    ServerDuties();
                    continue;
                }
                if (m.Phase == Phase.Battle)
                {
                    var moved = false;
                    for (var i = 0; i < m.Battles.Count && !moved; i++)
                    {
                        var b = m.Battles[i];
                        for (var s = 0; s < 2 && !moved; s++)
                        {
                            var owner = Server.Owner(b, s);
                            if (owner < 0) continue;
                            if (!b.Started && !Server.Activated(i, s)) { moved = Send(HanAction.Activation(owner, i, s, b.DefaultActivation(s))); continue; }
                            if (b.Started && !b.Done && !b.Submitted(s))
                            {
                                var c = _rng.Next(3) == 0 ? WindowChoice.None : HanBot.Window(b, s, _rng);
                                moved = Send(HanAction.Choice(owner, i, s, c)) || Send(HanAction.Choice(owner, i, s, WindowChoice.None));
                            }
                        }
                    }
                    if (!moved)
                    {
                        // beklenen kimse kalmadıysa round kapanmalı; kalmadı ama kapanamıyorsa motor takıldı demektir
                        Assert.True(Server.RoundCanEnd, "savaş evresinde ne insan bekleniyor ne round kapanabiliyor");
                    }
                    ServerDuties();
                    continue;
                }
                Assert.Fail("beklenmeyen evre " + m.Phase);
            }
        }
    }

    [Theory]
    [InlineData(1, 8, 1)]
    [InlineData(2, 8, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(5, 8, 4)]
    [InlineData(8, 8, 5)]
    [InlineData(2, 2, 6)]
    public void Replicas_stay_identical_through_a_whole_match(int humans, int size, int seed)
    {
        var sim = new Sim(1000 + seed, humans, size);
        sim.PlayToEnd();
        Assert.Equal(Phase.Finished, sim.Server.M.Phase);
        Assert.Single(sim.Server.M.Players, p => p.Place == 1);
        Assert.True(sim.Rejected > 0, "geçersiz eylemler de denenmeli");
    }

    [Fact]
    public void Replaying_the_log_rebuilds_the_same_match()
    {
        var sim = new Sim(77, 3, 8);
        sim.PlayToEnd();
        var replay = new HanGame(77, Enumerable.Range(0, 8).Select(i => ($"P{i}", i >= 3)).ToList());
        foreach (var a in sim.Log) Assert.True(replay.Apply(a));
        Assert.Equal(sim.Server.Fingerprint(), replay.Fingerprint());
    }

    [Fact]
    public void Player_cannot_act_for_another_seat_or_for_a_bot_side()
    {
        var g = new HanGame(5, new List<(string, bool)> { ("A", false), ("B", false), ("C", true), ("D", true) });
        Assert.False(g.Apply(new HanAction(HanActKind.School, 2, 0)));          // bot koltuğu
        var fp = g.Fingerprint();
        Assert.False(g.Apply(new HanAction(HanActKind.Buy, 0, 0)));             // okul seçilmeden alışveriş yok
        Assert.Equal(fp, g.Fingerprint());
        foreach (var i in new[] { 0, 1 }) Assert.True(g.Apply(new HanAction(HanActKind.School, i, (int)g.M.Players[i].SchoolOptions[0])));
        Assert.Equal(Phase.Prep, g.M.Phase);
        Assert.True(g.M.Players.Where(p => p.IsBot).All(p => p.Ready));        // botlar round başında hazırlandı
        Assert.False(g.Apply(new HanAction(HanActKind.EndRound, -1)));           // savaş yokken round kapanmaz
    }

    [Fact]
    public void Abandoned_seat_is_played_by_the_bot()
    {
        var g = new HanGame(9, new List<(string, bool)> { ("A", false), ("B", false), ("C", true), ("D", true) });
        foreach (var i in new[] { 0, 1 }) g.Apply(new HanAction(HanActKind.School, i, (int)g.M.Players[i].SchoolOptions[0]));
        Assert.True(g.Apply(new HanAction(HanActKind.Ready, 0)));
        Assert.True(g.Apply(new HanAction(HanActKind.Abandon, 1)));             // B ayrıldı: bot hazırlanır, saha kilitlenir
        Assert.Equal(Phase.Battle, g.M.Phase);
        Assert.False(g.Apply(new HanAction(HanActKind.Buy, 1, 0)));
    }
}
