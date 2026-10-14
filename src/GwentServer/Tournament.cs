using GwentNet;

namespace GwentServer;

/// <summary>Turnuva oyuncusu (sunucu tarafı). Oturum yoksa (bağlantı kopuk) Session null'dır; anahtarla geri döner.</summary>
internal sealed class TPlayer
{
    public ServerSession? Session;
    public string Nick = "";
    public string Token = "";
    public LoadoutDto? Loadout;
    public bool Ready;                 // bekleme odasında: deste seçti ve hazır; turnuvada: sıradaki maça hazır
    public bool Connected;
    public DateTime? DisconnectedAt;
    public bool Eliminated;
    public bool Forfeit;               // turnuvadan ayrıldı: eşleşmeleri hükmen yenik sayılır
    public int Placement;              // 1 şampiyon, 2 finalist, 3 yarı finalist, 5 çeyrek finalist; 0 = belli değil
    public TMatch? Current;            // şu an oynadığı ya da sıradaki eşleşme
}

/// <summary>Bracket'teki bir eşleşme. A koltuk 0, B koltuk 1 olarak maç odasına oturur.</summary>
internal sealed class TMatch
{
    public int Round;
    public int Index;
    public int? A;
    public int? B;
    public int? Winner;
    public Room? Room;                 // maç odası (oynanırken ve bittikten sonra sonuç ekranı için)
    public bool Done => Winner != null;
    public bool Bye => Done && (A == null || B == null);
}

/// <summary>
/// Tek elemeli turnuva: bekleme odası (oyuncular + deste kilitleme) → bracket → şampiyon. Sunucu çekirdeğindeki <see cref="RoomService"/>
/// kilidi altında kullanılır; bu sınıf yalnız bracket durumunu ve kurallarını (bye, ilerleme, sıralama) tutar. Maç odalarını RoomService kurar.
/// 3–8 oyuncu: bracket 4 ya da 8 yerlidir, fazladan yerler "bye" (otomatik tur atlama) olur.
/// </summary>
public sealed class Tournament
{
    internal Tournament(string code, bool nextGen, int maxPlayers, int entryFee, DateTime now)
    {
        Code = code;
        NextGen = nextGen;
        MaxPlayers = maxPlayers;
        EntryFee = entryFee;
        LastActivity = now;
    }

    public string Code { get; }
    public bool NextGen { get; }
    public int MaxPlayers { get; }
    public int EntryFee { get; }
    public TournamentPhase Phase { get; internal set; } = TournamentPhase.Lobby;
    public DateTime LastActivity { get; internal set; }
    internal readonly List<TPlayer> Players = new();
    internal List<List<TMatch>> Rounds { get; private set; } = new();

    internal int RoundCount => Rounds.Count;
    internal IEnumerable<TMatch> AllMatches => Rounds.SelectMany(r => r);

    /// <summary>Bracket yerleşimi: katılımcı sayısına göre 4 ya da 8; bye'lar üst ve alttaki eşleşmelere dağıtılır. Sıra <paramref name="order"/> ile karıştırılır.</summary>
    internal void BuildBracket(IReadOnlyList<int> order)
    {
        var n = order.Count;
        var size = n <= 4 ? 4 : 8;
        var rounds = size == 4 ? 2 : 3;
        Rounds = new List<List<TMatch>>();
        for (var r = 0; r < rounds; r++)
            Rounds.Add(Enumerable.Range(0, size / (2 << r)).Select(i => new TMatch { Round = r, Index = i }).ToList());

        var first = Rounds[0];
        var byes = size - n;
        // bye alacak eşleşme sırası: en üst, en alt, sonra içeriye doğru
        var byeSlots = new List<int>();
        for (int lo = 0, hi = first.Count - 1; lo <= hi; lo++, hi--) { byeSlots.Add(lo); if (hi != lo) byeSlots.Add(hi); }
        var byeSet = byeSlots.Take(byes).ToHashSet();
        var queue = new Queue<int>(order);
        foreach (var m in first)
        {
            m.A = queue.Dequeue();
            if (!byeSet.Contains(m.Index)) m.B = queue.Dequeue();
        }
        foreach (var m in first.Where(m => m.B == null)) Advance(m, m.A!.Value);   // bye: otomatik ilerler
        foreach (var m in first.Where(m => !m.Done)) { Players[m.A!.Value].Current = m; Players[m.B!.Value].Current = m; }
    }

    /// <summary>Eşleşmenin galibini kaydeder, bir sonraki turdaki yerine taşır, kaybedeni eler, final bittiyse turnuvayı bitirir.</summary>
    internal void Advance(TMatch m, int winner)
    {
        if (m.Done) return;
        m.Winner = winner;
        var loser = m.A == winner ? m.B : m.A;
        if (loser is { } l)
        {
            Players[l].Eliminated = true;
            Players[l].Placement = m.Round == RoundCount - 1 ? 2 : (1 << (RoundCount - 1 - m.Round)) + 1;
            Players[l].Ready = false;
        }
        Players[winner].Ready = false;
        if (m.Round == RoundCount - 1)
        {
            Players[winner].Placement = 1;
            Phase = TournamentPhase.Finished;
            return;
        }
        var next = Rounds[m.Round + 1][m.Index / 2];
        if (m.Index % 2 == 0) next.A = winner; else next.B = winner;
        Players[winner].Current = next;
        if (loser is { } lp) Players[lp].Current = null;
    }

    /// <summary>İki oyuncusu da belli ve henüz oynanmamış eşleşmeler.</summary>
    internal IEnumerable<TMatch> Pending => AllMatches.Where(m => !m.Done && m.A != null && m.B != null);

    internal TournamentState Snapshot(int viewer, bool revealLoadouts)
    {
        var players = Players.Select((p, i) => new TPlayerInfo(
            p.Nick, p.Connected, p.Ready, p.Eliminated,
            revealLoadouts || i == viewer ? p.Loadout : null, p.Placement, p.Loadout?.Avatar)).ToList();
        var matches = AllMatches.Select(m =>
        {
            var status = m.Done ? "done" : m.Room is { Phase: RoomPhase.Playing } ? "playing" : m.A != null && m.B != null ? "ready" : "waiting";
            return new TMatchInfo(m.Round, m.Index, m.A, m.B, m.Winner, status,
                m.A is { } a && Players[a].Ready && !m.Done, m.B is { } b && Players[b].Ready && !m.Done);
        }).ToList();
        var mine = viewer >= 0 ? Players[viewer].Current : null;
        return new TournamentState(Code, Phase, MaxPlayers, NextGen, EntryFee, players, matches, viewer,
            viewer >= 0 ? Players[viewer].Token : "", mine?.Round, mine?.Index);
    }
}
