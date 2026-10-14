using System.Security.Cryptography;
using System.Text;
using GwentAI;
using GwentCore;

namespace GwentNet;

/// <summary>
/// Online sözleşmeleri: iki istemci ve sunucu bunları paylaşır. Taşıma katmanından bağımsızdır (SignalR yalnız bunları taşır).
/// Koltuk: 0 = oda kuran (host), 1 = konuk; motordaki oyuncu numarasıyla aynıdır.
/// </summary>
public static class ProtocolInfo
{
    /// <summary>Mesaj biçimi değişince artırılır.</summary>
    public const int Number = 1;

    /// <summary>
    /// Sürüm kimliği = protokol numarası + kart verisinin özeti. Kart/kural farkı olan istemci odaya girmesin
    /// (iki taraf aynı kuralla oynamalı).
    /// </summary>
    public static string Version { get; } = Compute();

    private static string Compute()
    {
        var sb = new StringBuilder();
        foreach (var c in CardCatalog.Default.All.OrderBy(c => c.Id, StringComparer.Ordinal))
            sb.Append(c.Id).Append('|').Append(c.Faction).Append('|').Append(c.Strength).Append('|').Append(c.Copies).Append('|')
              .Append(string.Join(",", c.Abilities)).Append('|').Append(c.Hero).Append('|').Append(c.Row).Append(';');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"p{Number}-{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
    }
}

/// <summary>Oyuncunun seçtiği fraksiyon, lider ve deste. CardIds boşsa fraksiyonun "önerilen" destesi kullanılır.</summary>
public sealed record LoadoutDto(string Faction, string LeaderId, List<string>? CardIds = null, string? Avatar = null);

public sealed record PlayerInfo(string Nick, int Seat, bool Connected, bool Ready, LoadoutDto? Loadout);

public enum RoomPhase { Lobby, Playing, Finished }

/// <summary>Odanın herkese görünen durumu. YourSeat/YourToken her oyuncuya kendine özel doldurulur (token yeniden bağlanma anahtarıdır).</summary>
public sealed record RoomState(string Code, RoomPhase Phase, List<PlayerInfo> Players, int YourSeat, string YourToken, bool NextGen, int ActionCount);

/// <summary>Maçın kurulumu: iki açık deste + tohum. Aynı paket iki istemcide aynı <c>Game</c>'i kurar.</summary>
public sealed record MatchSetup(int Seed, bool NextGen, LoadoutDto Seat0, LoadoutDto Seat1, string Nick0, string Nick1, int YourSeat)
{
    /// <summary>Koltuk sırası (0, 1) korunur: koltuk takası karıştırma sırasını değiştirir ve kopyaları ayrıştırır.</summary>
    public ReplayData ToReplay() => new()
    {
        MyFaction = Seat0.Faction, MyLeaderId = Seat0.LeaderId, MyDeckCards = Seat0.CardIds,
        OppFaction = Seat1.Faction, OppLeaderId = Seat1.LeaderId, OppDeckCards = Seat1.CardIds,
        NextGen = NextGen, Seed = Seed
    };
}

/// <summary>Sunucunun doğruladığı ve sıraladığı eylem. Seq 0'dan başlar, boşluk olamaz.</summary>
public sealed record ActionBroadcast(int Seq, int Player, ReplayAction Action);

/// <summary>
/// Devam eden bir maçın diske yazılabilen özeti (host tarafında): kurulum + o ana dek doğrulanmış eylemler + koltuk anahtarları.
/// Uygulama kapanıp açılınca oda bundan yeniden kurulur; iki oyuncu da aynı anahtarlarla geri döner.
/// </summary>
public sealed record RoomSnapshot(string Code, bool NextGen, MatchSetup Setup, List<ReplayAction> Actions, string Token0, string Token1, DateTime SavedAtUtc);

public sealed record MatchResult(int? WinnerSeat, string Reason);

// ---------- Turnuva ----------

public enum TournamentPhase { Lobby, Running, Finished }

/// <summary>Turnuva oyuncusu. Loadout turnuva başlayana dek yalnız kendine görünür; başlayınca herkese açılır. Placement: 1 şampiyon, 2 finalist, 3 yarı final...; 0 = henüz belli değil.</summary>
public sealed record TPlayerInfo(string Nick, bool Connected, bool Ready, bool Eliminated, LoadoutDto? Loadout, int Placement, string? Avatar = null);

/// <summary>Bracket'teki bir eşleşme. A/B oyuncu numaraları (null = henüz belli değil ya da bye); Status: waiting | ready | playing | done.</summary>
public sealed record TMatchInfo(int Round, int Index, int? A, int? B, int? Winner, string Status, bool ReadyA, bool ReadyB);

/// <summary>Turnuvanın herkese görünen durumu. YourIndex/YourToken her oyuncuya özel doldurulur (token yeniden bağlanma anahtarıdır).</summary>
public sealed record TournamentState(string Code, TournamentPhase Phase, int MaxPlayers, bool NextGen, int EntryFee,
    List<TPlayerInfo> Players, List<TMatchInfo> Matches, int YourIndex, string YourToken, int? YourMatchRound, int? YourMatchIndex); // Reason: normal | resigned | abandoned

/// <summary>Sunucudan istemciye giden olaylar. SignalR'da istemci yöntemlerine karşılık gelir.</summary>
public interface IGwentClient
{
    void OnRoomState(RoomState state);
    void OnMatchStarted(MatchSetup setup);
    void OnActionApplied(ActionBroadcast action);
    void OnActionRejected(string reason, int expectedSeq);
    void OnMatchEnded(MatchResult result);
    /// <summary>code: opponent_disconnected | opponent_returned | rematch_requested | room_closed</summary>
    void OnNotice(string code, string text);
    void OnTournamentState(TournamentState state);
}

/// <summary>İstemci isteklerinin sonucu.</summary>
public readonly record struct Reply(bool Ok, string? Error = null, string? Value = null)
{
    public static Reply Success(string? value = null) => new(true, null, value);
    public static Reply Fail(string error) => new(false, error);
}

/// <summary>SignalR yöntem adları (sunucu → istemci). İki taraf aynı sabitleri kullanır.</summary>
public static class HubMethods
{
    public const string Path = "/gwent";
    public const string OnRoomState = nameof(OnRoomState);
    public const string OnMatchStarted = nameof(OnMatchStarted);
    public const string OnActionApplied = nameof(OnActionApplied);
    public const string OnActionRejected = nameof(OnActionRejected);
    public const string OnMatchEnded = nameof(OnMatchEnded);
    public const string OnNotice = nameof(OnNotice);
    public const string OnTournamentState = nameof(OnTournamentState);
}
