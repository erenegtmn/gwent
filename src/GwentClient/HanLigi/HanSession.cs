using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Tek oyunculu Han Ligi oturumu: 0 numaralı koltuk insan, diğerleri bot. Arayüz yalnız bu sınıfın yöntemlerini çağırır;
/// botların hazırlığı, diğer savaşlar ve rakibin pencere seçimleri burada yürür. Kurallar <see cref="Match"/> içindedir.
/// </summary>
public sealed class HanSession
{
    public const int Me = 0;
    private static readonly string[] BotNames = { "Zoltan", "Lambert", "Eskel", "Keira", "Roche", "Iorveth", "Letho", "Cerys", "Dijkstra" };

    public Match M { get; }
    public Random Rng { get; }
    public Battle? MyBattle { get; private set; }
    public int MySide { get; private set; }
    /// <summary>Süre sınırı (30 / 10 / 5 sn); kapalıysa alıştırma: süre işlemez.</summary>
    public bool Timed { get; set; } = true;
    public PlayerState Player => M.Players[Me];
    public bool Over => M.Phase == Phase.Finished;
    /// <summary>Az önce biten round (sonuç ekranı için).</summary>
    public int LastRound { get; private set; }

    public HanSession(int seed, string name, int players = 8)
    {
        Rng = new Random(seed ^ 0x2545f491);
        var list = new List<(string, bool)> { (name, false) };
        list.AddRange(BotNames.OrderBy(_ => Rng.Next()).Take(players - 1).Select(n => (n, true)));
        M = new Match(seed, list);
    }

    /// <summary>Botun portresi: oyunun günlüğündeki yüz (assets/ui/faces) ya da yoksa aynı adlı Gwent kartı.</summary>
    public string? FaceOf(int pid)
    {
        if (pid < 0 || pid >= M.Players.Count) return null;
        var n = M.Players[pid].Name.ToLowerInvariant();
        return n switch { "iorveth" => "iorveth", _ => "face_" + n };
    }

    public void ChooseSchool(School s)
    {
        foreach (var p in M.Players.Where(p => p.IsBot && p.School == null)) M.ChooseSchool(p.Id, p.SchoolOptions[Rng.Next(p.SchoolOptions.Length)]);
        M.ChooseSchool(Me, s);
        BotsPrep();
    }

    /// <summary>Round başında botlar hemen hazırlanır; maç insanın "Hazır" demesini bekler.</summary>
    private void BotsPrep()
    {
        if (M.Phase != Phase.Prep) return;
        foreach (var p in M.Alive.Where(p => p.IsBot).ToList()) HanBot.Prep(M, p.Id, Rng);
    }

    /// <summary>Hazırlığı bitirir: saha kilitlenir, diğer savaşlar oynanır, insanın savaşı aktivasyonu bekler.</summary>
    public void Ready()
    {
        if (M.Phase == Phase.Prep && (!M.SetReady(Me) || M.Phase != Phase.Battle)) M.ForceLock();
        if (M.Phase != Phase.Battle || MyBattle != null) return;
        MyBattle = M.BattleOf(Me);
        MySide = MyBattle == null ? 0 : M.SideOf(MyBattle, Me);
        foreach (var b in M.Battles.Where(b => b != MyBattle)) HanBot.PlayOut(b, Rng);
    }

    public void StartBattle(Activation mine)
    {
        var b = MyBattle!;
        if (b.Started) return;
        if (!b.SetActivation(MySide, mine)) b.SetActivation(MySide, b.DefaultActivation(MySide));
        b.SetActivation(1 - MySide, b.DefaultActivation(1 - MySide));
        b.Start();
    }

    /// <summary>İnsanın pencere seçimi: rakip bot kendi seçimini bundan bağımsız yapar, sonra pencere çözülür.</summary>
    public WindowOutcome? Submit(WindowChoice c)
    {
        var b = MyBattle!;
        if (b.Done) return null;
        var theirs = HanBot.Window(b, 1 - MySide, Rng);
        if (!b.Submit(MySide, c)) b.Submit(MySide, WindowChoice.None);
        b.Submit(1 - MySide, theirs);
        return b.Resolve();
    }

    /// <summary>İnsan koltuğunu da bot oynatarak bir round'u baştan sona geçer (sınama ve ekran görüntüleri için).</summary>
    public void AutoRound(Random rng)
    {
        if (M.Phase != Phase.Prep || !Player.Alive) return;
        HanBot.Prep(M, Me, rng);
        Ready();
        var b = MyBattle!;
        StartBattle(b.DefaultActivation(MySide));
        while (!b.Done) Submit(HanBot.Window(b, MySide, rng));
        FinishRound();
    }

    /// <summary>Round'u kapatır. İnsan elendiyse kalan maç botlarla hızlıca tamamlanır.</summary>
    public void FinishRound()
    {
        LastRound = M.Round;
        M.EndRound();
        MyBattle = null;
        if (M.Phase == Phase.Finished) return;
        if (!Player.Alive) { HanBot.RunToEnd(M, Rng); return; }
        BotsPrep();
    }
}
