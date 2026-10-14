using GwentNet;

namespace GwentClient;

/// <summary>
/// Turnuva tacı: katılım ücreti ve ödül havuzu. Bakiye her oyuncunun kendi bilgisayarında tutulur; sunucu bakiye bilmez (arkadaşlar arası,
/// güven esaslı). Ücret katılırken düşer, turnuva bitince herkes kendi sıralamasına göre ödülünü kendisi işler; bekleme odasında ayrılma/kapanma iade eder.
/// </summary>
public static class TournamentEconomy
{
    public static int Prize(int entryFee, int players, int placement) => TournamentPrizes.Prize(entryFee, players, placement);

    /// <summary>Katılım ücretini düşer. Yetersizse false (bakiye değişmez). Ücret 0 ise her zaman true.</summary>
    public static bool TryPayEntry(string code, int fee)
    {
        if (fee <= 0) return true;
        if (Settings.PaidTournaments.ContainsKey(code)) return true;
        if (Settings.Crowns < fee) return false;
        Settings.Crowns -= fee;
        Settings.PaidTournaments[code] = fee;
        Settings.Save();
        return true;
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
