using GwentAI;
using GwentCore;
using GwentNet;

namespace GwentServer;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Bir bağlantı (istemci). Oda ve koltuk bilgisi sunucuda tutulur; istemci bunları değiştiremez.</summary>
public sealed class ServerSession
{
    public ServerSession(string id, IGwentClient client) { Id = id; Client = client; }
    public string Id { get; }
    internal IGwentClient Client { get; set; }
    internal Room? Room { get; set; }
    internal int Seat { get; set; } = -1;
    internal Tournament? Tournament { get; set; }
    internal int TIndex { get; set; } = -1;
}

internal sealed class Seat
{
    public ServerSession? Session;
    public string Nick = "";
    public string Token = "";
    public LoadoutDto? Loadout;
    public bool Ready;
    public bool Connected;
    public DateTime? DisconnectedAt;
    public bool Rematch;
}

/// <summary>
/// Bir oda: bekleme (Lobby) → maç (Playing) → bitti (Finished). Tüm yöntemler <see cref="RoomService"/> kilidi altında çağrılır.
/// Sunucu oyunun hakemidir: her eylemi kendi <see cref="Game"/> kopyasında doğrular, sonra sıra numarasıyla iki tarafa yayar.
/// </summary>
public sealed class Room
{
    private readonly IClock _clock;
    private readonly Func<int> _seed;
    internal readonly Seat?[] Seats = new Seat?[2];

    internal Room(string code, bool nextGen, IClock clock, Func<int> seed)
    {
        Code = code;
        NextGen = nextGen;
        _clock = clock;
        _seed = seed;
        LastActivity = clock.UtcNow;
    }

    public string Code { get; }
    public bool NextGen { get; }
    public RoomPhase Phase { get; private set; } = RoomPhase.Lobby;
    public DateTime LastActivity { get; internal set; }

    /// <summary>Sunucunun hakem oyunu (yalnız maç sırasında dolu; testler kopyalarla karşılaştırır).</summary>
    public Game? Game { get; private set; }
    public MatchSetup? Setup { get; private set; }
    public MatchResult? Result { get; private set; }
    public IReadOnlyList<ActionBroadcast> ActionLog => _log;
    private readonly List<ActionBroadcast> _log = new();

    /// <summary>Maç durumu değişince (kurulum, doğrulanan eylem, bitiş) çağrılır; host kalıcı kayıt için kullanır.</summary>
    internal Action<Room>? Changed { get; set; }

    /// <summary>Turnuva maçıysa bağlı olduğu turnuva ve eşleşme (normal odalarda null).</summary>
    internal Tournament? Tournament { get; set; }
    internal TMatch? TMatch { get; set; }

    /// <summary>Maç bitince (sonuçla) çağrılır; turnuva bracket'i ilerletmek için kullanır.</summary>
    internal Action<Room, MatchResult>? Finished { get; set; }

    internal RoomSnapshot? ExportSnapshot() =>
        Phase == RoomPhase.Playing && Setup != null && Seats[0] != null && Seats[1] != null
            ? new RoomSnapshot(Code, NextGen, Setup with { YourSeat = 0 }, _log.Select(m => m.Action).ToList(), Seats[0]!.Token, Seats[1]!.Token, _clock.UtcNow)
            : null;

    /// <summary>
    /// Kayıttan odayı yeniden kurar: eylemleri taze bir oyunda yeniden oynatır (biri geçersizse null). Her iki koltuk bağlantısız beklemeye alınır;
    /// oyuncular eski anahtarlarıyla <c>Rejoin</c> eder. Biten oyun geri yüklenmez.
    /// </summary>
    internal static Room? Restore(RoomSnapshot snap, IClock clock, Func<int> seed)
    {
        try
        {
            var room = new Room(snap.Code, snap.NextGen, clock, seed);
            room.Setup = snap.Setup with { YourSeat = 0 };
            room.Game = room.Setup.ToReplay().CreateGame();
            foreach (var action in snap.Actions)
            {
                var a = ReplayData.Decode(action);
                if (!room.Game.PlayersToAct().Contains(a.Player) || !room.Game.Apply(a).Ok) return null;
                room._log.Add(new ActionBroadcast(room._log.Count, a.Player, action));
            }
            if (room.Game.Phase == GwentCore.Phase.GameOver) return null;
            room.Phase = RoomPhase.Playing;
            var now = clock.UtcNow;
            room.Seats[0] = new Seat { Nick = room.Setup.Nick0, Token = snap.Token0, Loadout = room.Setup.Seat0, Ready = true, Connected = false, DisconnectedAt = now };
            room.Seats[1] = new Seat { Nick = room.Setup.Nick1, Token = snap.Token1, Loadout = room.Setup.Seat1, Ready = true, Connected = false, DisconnectedAt = now };
            return room;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidOperationException) { return null; }
    }

    internal int SeatCount => Seats.Count(s => s != null);
    internal Seat? Other(int seat) => Seats[1 - seat];

    // ---------- Durum ve gönderim ----------

    internal RoomState Snapshot(int seat) => new(
        Code, Phase,
        Enumerable.Range(0, 2).Where(i => Seats[i] != null)
            .Select(i => new PlayerInfo(Seats[i]!.Nick, i, Seats[i]!.Connected, Seats[i]!.Ready, Seats[i]!.Loadout)).ToList(),
        seat, Seats[seat]?.Token ?? "", NextGen, _log.Count);

    internal void Send(int seat, Action<IGwentClient> send)
    {
        var s = Seats[seat];
        if (s is { Connected: true, Session: not null }) send(s.Session.Client);
    }

    internal void BroadcastState()
    {
        for (var i = 0; i < 2; i++)
        {
            var seat = i;
            if (Seats[seat] != null) Send(seat, c => c.OnRoomState(Snapshot(seat)));
        }
    }

    internal void Touch() => LastActivity = _clock.UtcNow;

    // ---------- Deste doğrulama ----------

    internal static readonly string[] Factions = { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" };

    internal string? ValidateLoadout(LoadoutDto l) => ValidateLoadout(l, NextGen);

    internal static string? ValidateLoadout(LoadoutDto l, bool nextGenOn)
    {
        if (!Factions.Contains(l.Faction)) return "Bilinmeyen fraksiyon.";
        // Portre yalnızca kısa bir kimliktir (istemci gösterir); rastgele uzun/garip metin yayınlanmasın
        if (l.Avatar != null && (l.Avatar.Length > 64 || !l.Avatar.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))) return "Geçersiz portre.";
        var opts = new GameOptions { NextGen = nextGenOn };
        var leader = CardCatalog.Default.Leaders(l.Faction).FirstOrDefault(x => x.Id == l.LeaderId && (x.Edition != "nextgen" || nextGenOn));
        if (leader == null) return "Bu lider bu fraksiyonda yok ya da yeni nesil kapalı.";
        if (l.CardIds != null)
        {
            if (l.CardIds.Any(id => !CardCatalog.Default.TryGet(id, out _))) return "Destede bilinmeyen kart var.";
            var errors = DeckValidator.Validate(new Deck(l.Faction, l.LeaderId, l.CardIds), opts);
            if (errors.Count > 0) return "Deste geçersiz: " + string.Join(" ", errors);
        }
        return null;
    }

    /// <summary>Açık desteli hâli: CardIds boşsa fraksiyonun önerilen destesi.</summary>
    private LoadoutDto Resolve(LoadoutDto l)
    {
        var opts = new GameOptions { NextGen = NextGen };
        var ids = l.CardIds ?? DeckValidator.RecommendedDeck(l.Faction, opts, null, l.LeaderId).CardIds.ToList();
        return l with { CardIds = ids };
    }

    // ---------- Maç ----------

    internal bool AllReady() => Seats[0] is { Ready: true, Loadout: not null } && Seats[1] is { Ready: true, Loadout: not null };

    internal void StartMatch()
    {
        var a = Seats[0]!;
        var b = Seats[1]!;
        Setup = new MatchSetup(_seed(), NextGen, Resolve(a.Loadout!), Resolve(b.Loadout!), a.Nick, b.Nick, 0);
        Game = Setup.ToReplay().CreateGame();
        _log.Clear();
        Result = null;
        Phase = RoomPhase.Playing;
        foreach (var s in Seats) if (s != null) s.Rematch = false;
        Touch();
        for (var i = 0; i < 2; i++)
        {
            var seat = i;
            Send(seat, c => c.OnMatchStarted(Setup! with { YourSeat = seat }));
        }
        BroadcastState();
        Changed?.Invoke(this);
    }

    /// <summary>Eylemi doğrular ve uygular; geçerliyse iki tarafa yayar. Hata varsa yalnız göndericiye bildirir.</summary>
    internal Reply Submit(int seat, ReplayAction action)
    {
        if (Phase != RoomPhase.Playing || Game == null) return Reply.Fail("Şu an oynanan bir maç yok.");
        GameAction a;
        try { a = ReplayData.Decode(action); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { return Reject(seat, "Anlaşılmayan eylem."); }
        if (a.Player != seat) return Reject(seat, "Başkası adına eylem gönderemezsin.");
        if (!Game.PlayersToAct().Contains(seat)) return Reject(seat, "Şu an sıra sende değil.");

        var r = Game.Apply(a);
        if (!r.Ok) return Reject(seat, r.Error ?? "Eylem geçersiz.");

        var msg = new ActionBroadcast(_log.Count, seat, action);
        _log.Add(msg);
        Touch();
        for (var i = 0; i < 2; i++) Send(i, c => c.OnActionApplied(msg));
        if (Game.Phase == GwentCore.Phase.GameOver) Finish(Game.Outcome?.Winner, "normal");
        else Changed?.Invoke(this);
        return Reply.Success();
    }

    private Reply Reject(int seat, string reason)
    {
        Send(seat, c => c.OnActionRejected(reason, _log.Count));
        return Reply.Fail(reason);
    }

    internal void Finish(int? winner, string reason)
    {
        if (Phase != RoomPhase.Playing) return;
        Phase = RoomPhase.Finished;
        Result = new MatchResult(winner, reason);
        Touch();
        for (var i = 0; i < 2; i++) Send(i, c => c.OnMatchEnded(Result));
        BroadcastState();
        Changed?.Invoke(this);
        Finished?.Invoke(this, Result);
    }

    /// <summary>Yeniden bağlanan oyuncuya durumu ve kaçırdığı eylemleri gönderir (lastSeq = elindeki son sıra numarası, yoksa -1).</summary>
    internal void Resync(int seat, int lastSeq)
    {
        Send(seat, c => c.OnRoomState(Snapshot(seat)));
        if (Phase == RoomPhase.Lobby || Setup == null) return;
        if (lastSeq < 0) Send(seat, c => c.OnMatchStarted(Setup with { YourSeat = seat }));
        foreach (var m in _log.Where(m => m.Seq > lastSeq)) Send(seat, c => c.OnActionApplied(m));
        if (Phase == RoomPhase.Finished && Result != null) Send(seat, c => c.OnMatchEnded(Result));
    }
}
