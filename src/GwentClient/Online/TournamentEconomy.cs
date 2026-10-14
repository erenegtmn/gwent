using GwentNet;

namespace GwentClient;

/// <summary>
/// Turnuva tacı: katılım ücreti ve ödül havuzu. Bakiye her oyuncunun kendi bilgisayarında tutulur; sunucu bakiye bilmez (arkadaşlar arası,
/// güven esaslı). Ücret katılırken düşer, turnuva bitince herkes kendi sıralamasına göre ödülünü kendisi işler; bekleme odasında ayrılma/kapanma iade eder.
/// </summary>
public static class TournamentEconomy
{
    public static int Prize(int entryFee, int players, int placement) => TournamentPrizes.Prize(entryFee, players, placement);

    /// <summary>Bakiye bu değerin altına düşmez: açılışta daha azı varsa buna tamamlanır. Taç eğlence içindir, kimse turnuvadan taçsızlık yüzünden kalmasın.</summary>
    public const int Floor = 100;

    /// <summary>
    /// Katılım ücretini düşer; **asla engellemez**: bakiye yetmezse eldeki kadar düşer (bakiye 0'ın altına inmez). Düşen miktar
    /// kaydedilir, iade ve ödül ona göre işlenir. Ücretsiz turnuvada bir şey olmaz.
    /// </summary>
    public static int PayEntry(string code, int fee)
    {
        if (fee <= 0 || Settings.PaidTournaments.TryGetValue(code, out var already)) return Settings.PaidTournaments.GetValueOrDefault(code);
        var paid = Math.Min(fee, Settings.Crowns);
        Settings.Crowns -= paid;
        Settings.PaidTournaments[code] = paid;
        Settings.Save();
        return paid;
    }

    /// <summary>Açılışta bakiye tabanın altındaysa tabana tamamlar.</summary>
    public static void ApplyFloor()
    {
        if (Settings.Crowns >= Floor) return;
        Settings.Crowns = Floor;
        Settings.Save();
    }

    /// <summary>Turnuva başlamadan ayrılma/kapanma: ödenen ücret iade edilir.</summary>
    public static void Refund(string code)
    {
        if (!Settings.PaidTournaments.Remove(code, out var fee)) return;
        Settings.Crowns += fee;
        Settings.Save();
    }

    /// <summary>Turnuva bitince ödülü bir kez işler; kazanılan taç miktarını döner (0 = ödül yok ya da zaten işlenmiş).</summary>
    public static int Settle(TournamentState st)
    {
        if (st.Phase != TournamentPhase.Finished || st.YourIndex < 0) return 0;
        if (Settings.SettledTournaments.Contains(st.Code)) return 0;
        Settings.SettledTournaments.Add(st.Code);
        Settings.PaidTournaments.Remove(st.Code);
        var prize = Prize(st.EntryFee, st.Players.Count, st.Players[st.YourIndex].Placement);
        Settings.Crowns += prize;
        Settings.Save();
        return prize;
    }
}
