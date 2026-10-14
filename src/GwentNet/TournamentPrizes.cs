namespace GwentNet;

/// <summary>Turnuva ödül havuzu paylaşımı (Crown). Saf hesap: sunucu bilmez, her oyuncu kendi sıralamasına göre kendi ödülünü hesaplar.</summary>
public static class TournamentPrizes
{
    /// <summary>
    /// Havuz = katılım ücreti × oyuncu sayısı. 4 kişiye kadar: şampiyon %70, finalist %30.
    /// 5-8 kişi: şampiyon %55, finalist %25, iki yarı finalist %10'ar. Kalan kesirler yuvarlama nedeniyle dağıtılmaz.
    /// </summary>
    public static int Prize(int entryFee, int players, int placement)
    {
        var pool = entryFee * players;
        if (pool <= 0) return 0;
        var pct = players <= 4
            ? placement switch { 1 => 70, 2 => 30, _ => 0 }
            : placement switch { 1 => 55, 2 => 25, 3 => 10, _ => 0 };
        return pool * pct / 100;
    }
}
