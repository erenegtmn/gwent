using System.Security.Cryptography;
using GwentAI;
using GwentNet;

namespace GwentServer;

/// <summary>
/// Odaları yöneten, taşıma katmanından bağımsız sunucu çekirdeği. SignalR hub'ı (ya da testlerdeki sahte istemciler)
/// yalnız bu sınıfın yöntemlerini çağırır. Tek kilit kullanılır (arkadaşlar arası ölçek için yeterli ve yarış durumundan arınık);
/// bu yüzden <see cref="IGwentClient"/> uygulamaları çağrı içinde geri çağırma yapmamalı (iletiyi kuyruğa atmalı).
/// </summary>
public sealed partial class RoomService
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 0/O, 1/I/L gibi karışanlar yok
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly IClock _clock;
    private readonly Func<int> _seed;

    /// <summary>Rakip bağlantısı koparsa galibiyet talep edilebilmesi için beklenecek süre.</summary>
    public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromMinutes(2);

    public RoomService(IClock? clock = null, Func<int>? seed = null)
    {
        _clock = clock ?? new SystemClock();
        _seed = seed ?? (() => RandomNumberGenerator.GetInt32(1, int.MaxValue));
    }

    /// <summary>
    /// Bir odanın maçı değişince (eylem, bitiş) tetiklenir: <c>snapshot</c> devam eden maçın özeti, maç bittiyse/yoksa null.
    /// Sunucu kilidi altında çağrılır; işleyici hızlı olmalı (yalnız seri hâle getirip dosyaya yazmayı kuyruğa atmalı).
    /// </summary>
    public event Action<string, RoomSnapshot?>? RoomPersisted;

    private void Hook(Room room) => room.Changed = r => RoomPersisted?.Invoke(r.Code, r.ExportSnapshot());

    /// <summary>Kayıttan bir odayı geri kurar (uygulama yeniden açıldığında host için). Kod doluysa ya da kayıt bozuksa hata.</summary>
    public Reply RestoreRoom(RoomSnapshot snap)
    {
        lock (_gate)
        {
            if (_rooms.ContainsKey(snap.Code)) return Reply.Fail("Bu oda zaten açık.");
            if (snap.Setup.Seat0 == null || snap.Setup.Seat1 == null) return Reply.Fail("Kayıt eksik.");
            var room = Room.Restore(snap, _clock, _seed);
            if (room == null) return Reply.Fail("Kayıtlı maç yeniden oynatılamadı (bozuk ya da bitmiş).");
            Hook(room);
            _rooms[room.Code] = room;
            room.Touch();
            return Reply.Success(room.Code);
        }
    }

    public int RoomCount { get { lock (_gate) return _rooms.Count; } }

    /// <summary>Testler ve yönetim için odayı döndürür (yoksa null).</summary>
    public Room? Find(string code)
    {
        lock (_gate) return _rooms.GetValueOrDefault(code.Trim());
    }

    public ServerSession Connect(IGwentClient client) => new(Guid.NewGuid().ToString("N"), client);

    // ---------- Oda kurma / katılma ----------

    private static string? CheckNick(string nick)
    {
        nick = nick.Trim();
        if (nick.Length is < 2 or > 16) return "Takma ad 2-16 karakter olmalı.";
        if (nick.Any(char.IsControl)) return "Takma adda geçersiz karakter var.";
        return null;
    }

    private string NewCode()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var chars = new char[5];
            for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            var code = new string(chars);
            if (!_rooms.ContainsKey(code) && !_tournaments.ContainsKey(code)) return code;
        }
        throw new InvalidOperationException("Oda kodu üretilemedi.");
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public Reply CreateRoom(ServerSession s, string nick, string version, bool nextGen = false)
    {
        lock (_gate)
        {
            if (s.Room != null) return Reply.Fail("Zaten bir odadasın.");
            if (version != ProtocolInfo.Version) return Reply.Fail($"Sürüm uyuşmuyor (sende {version}, sunucuda {ProtocolInfo.Version}). Oyunu güncelle.");
            if (CheckNick(nick) is { } err) return Reply.Fail(err);
            var room = new Room(NewCode(), nextGen, _clock, _seed);
            Hook(room);
            _rooms[room.Code] = room;
            Attach(room, s, 0, nick.Trim());
            room.BroadcastState();
            return Reply.Success(room.Code);
        }
    }

    public Reply JoinRoom(ServerSession s, string code, string nick, string version)
    {
        lock (_gate)
        {
            if (s.Room != null) return Reply.Fail("Zaten bir odadasın.");
            if (version != ProtocolInfo.Version) return Reply.Fail($"Sürüm uyuşmuyor (sende {version}, sunucuda {ProtocolInfo.Version}). Oyunu güncelle.");
            if (CheckNick(nick) is { } err) return Reply.Fail(err);
            if (!_rooms.TryGetValue(code.Trim(), out var room)) return Reply.Fail("Böyle bir oda yok.");
            if (room.Phase != RoomPhase.Lobby) return Reply.Fail("Bu odada maç başlamış.");
            if (room.Seats[1] != null) return Reply.Fail("Oda dolu.");
            if (string.Equals(room.Seats[0]!.Nick, nick.Trim(), StringComparison.OrdinalIgnoreCase)) return Reply.Fail("Odadaki oyuncuyla aynı takma ad.");
            Attach(room, s, 1, nick.Trim());
            room.Touch();
            room.BroadcastState();
            return Reply.Success(room.Code);
        }
    }

    private static void Attach(Room room, ServerSession s, int seat, string nick)
    {
        room.Seats[seat] = new Seat { Session = s, Nick = nick, Token = NewToken(), Connected = true };
        s.Room = room;
        s.Seat = seat;
    }

    /// <summary>Bağlantısı kopan oyuncu, koltuk anahtarıyla odasına döner; eksik eylemler kendisine gönderilir.</summary>
    public Reply Rejoin(ServerSession s, string code, string token, int lastSeq)
    {
        lock (_gate)
        {
            if (s.Room != null) return Reply.Fail("Zaten bir odadasın.");
            if (!_rooms.TryGetValue(code.Trim(), out var room)) return Reply.Fail("Oda artık yok.");
            var seat = Enumerable.Range(0, 2).FirstOrDefault(i => room.Seats[i]?.Token == token && token.Length > 0, -1);
            if (seat < 0) return Reply.Fail("Koltuk anahtarı geçersiz.");
            var st = room.Seats[seat]!;
            if (st is { Connected: true, Session: not null }) st.Session.Room = null; // eski bağlantının yerine geçer
            st.Session = s;
            st.Connected = true;
            st.DisconnectedAt = null;
            s.Room = room;
            s.Seat = seat;
            room.Touch();
            room.Send(1 - seat, c => c.OnNotice("opponent_returned", $"{st.Nick} geri döndü."));
            room.Resync(seat, lastSeq);
            room.BroadcastState();
            if (room.Tournament is { } t && t.Players.FindIndex(p => p.Token == token) is var ti and >= 0) Reattach(t, ti, s);
            return Reply.Success(room.Code);
        }
    }

    // ---------- Bekleme odası ----------

    public Reply SetLoadout(ServerSession s, LoadoutDto loadout)
    {
        lock (_gate)
        {
            if (s.Room is not { } room) return Reply.Fail("Bir odada değilsin.");
            if (room.Phase == RoomPhase.Playing) return Reply.Fail("Maç sürerken deste değiştirilemez.");
            if (room.ValidateLoadout(loadout) is { } err) return Reply.Fail(err);
            var seat = room.Seats[s.Seat]!;
            var unchanged = Room.SameLoadout(seat.Loadout, loadout);
            seat.Loadout = loadout;
            if (!unchanged) seat.Ready = false;
            room.Touch();
            room.BroadcastState();
            return Reply.Success();
        }
    }

    public Reply SetReady(ServerSession s, bool ready)
    {
        lock (_gate)
        {
            if (s.Room is not { } room) return Reply.Fail("Bir odada değilsin.");
            if (room.Phase != RoomPhase.Lobby) return Reply.Fail("Hazır olma yalnız bekleme odasında.");
            var seat = room.Seats[s.Seat]!;
            if (ready && seat.Loadout == null) return Reply.Fail("Önce fraksiyon, lider ve deste seç.");
            seat.Ready = ready;
            room.Touch();
            if (room.SeatCount == 2 && room.AllReady()) room.StartMatch();
            else room.BroadcastState();
            return Reply.Success();
        }
    }

    // ---------- Maç ----------

    public Reply SubmitAction(ServerSession s, ReplayAction action)
    {
        lock (_gate)
        {
            if (s.Room is not { } room) return Reply.Fail("Bir odada değilsin.");
            return room.Submit(s.Seat, action);
        }
    }

    public Reply Resign(ServerSession s)
    {
        lock (_gate)
        {
            if (s.Room is not { Phase: RoomPhase.Playing } room) return Reply.Fail("Teslim olunacak bir maç yok.");
            room.Finish(1 - s.Seat, "resigned");
            return Reply.Success();
        }
    }

    /// <summary>Rakip bağlantısı yeterince uzun süredir kopuksa galibiyeti al.</summary>
    public Reply ClaimWin(ServerSession s)
    {
        lock (_gate)
        {
            if (s.Room is not { Phase: RoomPhase.Playing } room) return Reply.Fail("Devam eden bir maç yok.");
            var other = room.Other(s.Seat);
            if (other is not { Connected: false, DisconnectedAt: not null }) return Reply.Fail("Rakip bağlı görünüyor.");
            if (_clock.UtcNow - other.DisconnectedAt < DisconnectGrace) return Reply.Fail("Rakibe geri dönmesi için biraz daha süre tanı.");
            room.Finish(s.Seat, "abandoned");
            return Reply.Success();
        }
    }

    public Reply RequestRematch(ServerSession s)
    {
        lock (_gate)
        {
            if (s.Room is not { Phase: RoomPhase.Finished } room) return Reply.Fail("Rövanş yalnız maç bittikten sonra istenir.");
            if (room.Seats[1 - s.Seat] is not { Connected: true } other) return Reply.Fail("Rakip odada değil.");
            room.Seats[s.Seat]!.Rematch = true;
            if (other.Rematch) { room.StartMatch(); return Reply.Success(); }
            room.Send(1 - s.Seat, c => c.OnNotice("rematch_requested", $"{room.Seats[s.Seat]!.Nick} rövanş istiyor."));
            return Reply.Success();
        }
    }

    // ---------- Ayrılma ----------

    /// <summary>İstemci odadan bilerek ayrılır (maç sürüyorsa teslim sayılır).</summary>
    public void Leave(ServerSession s)
    {
        lock (_gate)
        {
            StopWatchingInternal(s);
            if (s.Room is { } room)
            {
                if (room.Phase == RoomPhase.Playing) room.Finish(1 - s.Seat, "resigned");
                Remove(room, s);
            }
            TournamentLeft(s);
        }
    }

    /// <summary>Bağlantı koptu (istemci kapandı/ağ gitti). Maç sürüyorsa koltuk bekletilir, yoksa oda temizlenir.</summary>
    public void Disconnect(ServerSession s)
    {
        lock (_gate)
        {
            StopWatchingInternal(s);
            TournamentDisconnected(s);
            if (s.Room is not { } room) return;
            var seat = room.Seats[s.Seat];
            if (seat == null || seat.Session != s) return;
            if (room.Phase == RoomPhase.Playing)
            {
                seat.Connected = false;
                seat.DisconnectedAt = _clock.UtcNow;
                seat.Session = null;
                s.Room = null;
                room.Touch();
                room.Send(1 - s.Seat, c => c.OnNotice("opponent_disconnected", $"{seat.Nick} bağlantısı koptu."));
                room.BroadcastState();
                return;
            }
            Remove(room, s);
        }
    }

    private void Remove(Room room, ServerSession s)
    {
        var seatIndex = s.Seat;
        var seat = room.Seats[seatIndex];
        s.Room = null;
        s.Seat = -1;
        if (seat == null) return;
        if (room.Tournament != null) { seat.Connected = false; seat.Session = null; return; }   // turnuva maç odası: yalnız koltuk boşalır, oda bracket için kalır
        var other = room.Other(seatIndex);
        // Host (koltuk 0) ayrılırsa oda kapanır: sunucu zaten host'un kendisidir
        if (seatIndex == 0 || other == null)
        {
            if (other?.Session != null)
            {
                room.Send(1 - seatIndex, c => c.OnNotice("room_closed", "Oda kapandı."));
                other.Session.Room = null;
                other.Session.Seat = -1;
            }
            _rooms.Remove(room.Code);
            return;
        }
        room.Seats[seatIndex] = null;
        room.Touch();
        room.BroadcastState();
    }

    /// <summary>Uzun süre kıpırdamayan ya da boşalmış odaları siler. Dönen sayı silinen oda sayısıdır.</summary>
    public int Sweep(TimeSpan idle)
    {
        lock (_gate)
        {
            var dead = _rooms.Values.Where(r =>
                _clock.UtcNow - r.LastActivity > idle
                || (r.Phase != RoomPhase.Playing && r.Seats.All(x => x == null || !x.Connected))).Select(r => r.Code).ToList();
            foreach (var code in dead) _rooms.Remove(code);
            return dead.Count + SweepTournaments(idle);
        }
    }
}
