using GwentNet;
using HanLigi;

namespace GwentServer;

/// <summary>Han Ligi koltuğu (insan). Bot koltukları maç başlarken eklenir ve oturumu olmaz.</summary>
internal sealed class HanSeat
{
    public ServerSession? Session;
    public string Nick = "";
    public string Token = "";
    public string? Avatar;
    public bool Connected;
    public bool Ready;
    public bool Bot;
    public DateTime? DisconnectedAt;
}

/// <summary>
/// Han Ligi odası (online, kilit adımlı): sunucu kendi <see cref="HanGame"/> kopyasında her eylemi doğrular ve sıra numarasıyla
/// herkese yayar. Bekleme odasındaki insanlar oturur; maç başlarken boş koltuklar <see cref="Size"/>'a kadar botla dolar.
/// Botlar ağdan gelmez, her kopyanın içinde tohumla oynar. Sunucu ayrıca round kapanışını ve süre aşımlarını yürütür.
/// </summary>
public sealed class HanRoom
{
    public const int MaxSize = 8;
    private static readonly string[] BotNames = { "Zoltan", "Lambert", "Eskel", "Keira", "Roche", "Iorveth", "Letho", "Cerys", "Dijkstra", "Vesemir", "Triss", "Yennefer" };

    internal HanRoom(string code, IClock clock, Func<int> seed) { Code = code; _clock = clock; _seed = seed; Touch(); }

    private readonly IClock _clock;
    private readonly Func<int> _seed;
    public string Code { get; }
    public RoomPhase Phase { get; private set; } = RoomPhase.Lobby;
    /// <summary>Maçtaki toplam koltuk (insanlar + botlar), 2-8.</summary>
    public int Size { get; internal set; } = MaxSize;
    internal List<HanSeat> Seats { get; } = new();
    public HanGame? Game { get; private set; }
    public HanSetup? Setup { get; private set; }
    private readonly List<HanAction> _actions = new();
    public IReadOnlyList<HanAction> Actions => _actions;
    public DateTime LastActivity { get; private set; }
    /// <summary>Son ilerleme (uygulanan eylem) anı: süre aşımları buna göre.</summary>
    public DateTime LastProgress { get; private set; }

    internal void Touch() => LastActivity = _clock.UtcNow;

    // aşama sayaçları: hazırlığın ve her savaşın o anki aşamasının başladığı an
    private (int Round, HanLigi.Phase Phase) _phaseKey;
    private DateTime _phaseAt;
    private readonly Dictionary<int, (string Stage, DateTime At)> _stages = new();

    private void Stamp()
    {
        if (Game == null) return;
        var now = _clock.UtcNow;
        var key = (Game.M.Round, Game.M.Phase);
        if (key != _phaseKey) { _phaseKey = key; _phaseAt = now; _stages.Clear(); }
        for (var i = 0; i < Game.M.Battles.Count; i++)
        {
            var st = Game.StageOf(i);
            if (!_stages.TryGetValue(i, out var cur) || cur.Stage != st) _stages[i] = (st, now);
        }
    }

    internal int Humans => Seats.Count(s => !s.Bot);

    internal HanRoomState StateFor(int seat) => new(Code, Phase, Size,
        Seats.Select((s, i) => new HanSeatInfo(s.Nick, i, s.Bot || s.Connected, s.Ready, s.Bot, s.Avatar)).ToList(),
        seat, seat >= 0 && seat < Seats.Count ? Seats[seat].Token : "", _actions.Count);

    internal void Send(int seat, Action<IHanClient> send)
    {
        if (seat < 0 || seat >= Seats.Count || Seats[seat] is not { Bot: false, Connected: true, Session: { } s }) return;
        if (s.Client is IHanClient hc) send(hc);
    }

    internal void BroadcastState()
    {
        for (var i = 0; i < Seats.Count; i++) { var st = StateFor(i); Send(i, c => c.OnHanState(st)); }
    }

    /// <summary>Maçı başlatır: boş koltuklar botla dolar, tohum seçilir, herkese kurulum gider.</summary>
    internal void Start()
    {
        var used = new HashSet<string>(Seats.Select(s => s.Nick), StringComparer.OrdinalIgnoreCase);
        var names = BotNames.Where(n => !used.Contains(n)).OrderBy(_ => _seed()).ToList();
        for (var i = 0; Seats.Count < Size; i++)
            Seats.Add(new HanSeat { Nick = names[i % names.Count], Bot = true, Connected = true, Ready = true });
        var seed = _seed();
        Game = new HanGame(seed, Seats.Select(s => (s.Nick, s.Bot)).ToList());
        _actions.Clear();
        Phase = RoomPhase.Playing;
        Setup = new HanSetup(seed, Seats.Select(s => s.Nick).ToList(), Seats.Select(s => s.Bot).ToList(), Seats.Select(s => s.Avatar).ToList(), -1);
        for (var i = 0; i < Seats.Count; i++) { var su = Setup with { YourSeat = i }; Send(i, c => c.OnHanStarted(su)); }
        LastProgress = _clock.UtcNow;
        Stamp();
        Touch();
        BroadcastState();
    }

    /// <summary>Eylemi sunucu kopyasında uygular; geçerliyse sıra numarasıyla herkese yayar ve sunucunun görevlerini yürütür.</summary>
    internal bool Apply(HanAction a)
    {
        if (Game == null || Phase != RoomPhase.Playing || !Game.Apply(a)) return false;
        var b = new HanBroadcast(_actions.Count, a);
        _actions.Add(a);
        for (var i = 0; i < Seats.Count; i++) Send(i, c => c.OnHanAction(b));
        LastProgress = _clock.UtcNow;
        Touch();
        Duties();
        Stamp();
        return true;
    }

    /// <summary>Sunucu görevleri: bütün savaşlar bitince round'u kapatır (yalnız botların kaldığı roundlar da hemen geçer); maç bitince oda biter.</summary>
    private void Duties()
    {
        if (Game == null) return;
        for (var guard = 0; guard < 500 && Game.RoundCanEnd; guard++) Apply(new HanAction(HanActKind.EndRound, -1));
        if (Game.M.Phase == HanLigi.Phase.Finished && Phase == RoomPhase.Playing) { Phase = RoomPhase.Finished; BroadcastState(); }
    }

    /// <summary>
    /// Süre aşımları: (1) bağlantısı <paramref name="abandonAfter"/>'dan uzun kopuk oyuncunun koltuğunu bot devralır;
    /// (2) <paramref name="stall"/> boyunca hiç ilerleme yoksa beklenen insanlar için varsayılan eylemler (kilit, hedef, "müdahale yok").
    /// İstemciler kendi sayaçlarıyla zaten süre dolunca gönderir; bu yalnız takılmaya karşı güvencedir.
    /// </summary>
    internal void Tick(TimeSpan stall, TimeSpan abandonAfter)
    {
        if (Game == null || Phase != RoomPhase.Playing) return;
        for (var i = 0; i < Seats.Count; i++)
        {
            var s = Seats[i];
            if (s is { Bot: false, Connected: false, DisconnectedAt: { } at } && _clock.UtcNow - at > abandonAfter && !Game.Bots.Contains(i) && Game.M.Players[i].Alive)
                Apply(new HanAction(HanActKind.Abandon, i));
        }
        // aşama süreleri: hazırlık herkes için ortak; savaşta her ikili kendi aşamasında (öteki ikilileri beklemez)
        var now = _clock.UtcNow;
        var pending = Game.PendingDefaults().ToList();
        if (Game.M.Phase == HanLigi.Phase.Prep && now - _phaseAt > TimeSpan.FromSeconds(HanTimes.Prep + HanTimes.Grace + (Game.M.Round > 1 ? HanTimes.ResultHold : 0)))
            foreach (var a in pending.Where(a => a.Kind == HanActKind.Lock).Take(1)) Apply(a);
        else if (Game.M.Phase == HanLigi.Phase.Battle)
            foreach (var a in pending.Where(a => a.Kind is HanActKind.Activate or HanActKind.Window))
            {
                if (!_stages.TryGetValue(a.A, out var st)) continue;
                var limit = st.Stage == "act" ? HanTimes.Activation + HanTimes.ResultHold : HanTimes.Window + HanTimes.WindowShow;
                if (now - st.At > TimeSpan.FromSeconds(limit + HanTimes.Grace)) Apply(a);
            }
        if (_clock.UtcNow - LastProgress < stall) return;
        foreach (var a in Game.PendingDefaults().ToList()) Apply(a);
    }

    /// <summary>Yeniden bağlanan oyuncuya kurulum + kaçırdığı eylemler.</summary>
    internal void Resync(int seat, int lastSeq)
    {
        if (Setup == null) return;
        var su = Setup with { YourSeat = seat };
        if (lastSeq < 0) Send(seat, c => c.OnHanStarted(su));
        for (var i = Math.Max(0, lastSeq + 1); i < _actions.Count; i++) { var b = new HanBroadcast(i, _actions[i]); Send(seat, c => c.OnHanAction(b)); }
    }
}

public sealed partial class RoomService
{
    private readonly Dictionary<string, HanRoom> _han = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Takılmaya karşı: bu süre hiç ilerleme olmazsa beklenenler için varsayılan eylem.</summary>
    public TimeSpan HanStall { get; init; } = TimeSpan.FromSeconds(120);

    public HanRoom? FindHan(string code) { lock (_gate) return _han.GetValueOrDefault(code.Trim()); }

    private static string? CheckAvatar(string? avatar) =>
        avatar != null && (avatar.Length > 64 || !avatar.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) ? "Geçersiz portre." : null;

    /// <summary>Han Ligi odası kurar. <paramref name="size"/>: maçtaki toplam koltuk (eksikleri bot doldurur).</summary>
    public Reply CreateHanRoom(ServerSession s, string nick, string version, int size = HanRoom.MaxSize, string? avatar = null)
    {
        lock (_gate)
        {
            if (s.Room != null || s.Han != null) return Reply.Fail("Zaten bir odadasın.");
            if (version != ProtocolInfo.Version) return Reply.Fail($"Sürüm uyuşmuyor (sende {version}, sunucuda {ProtocolInfo.Version}). Oyunu güncelle.");
            if (CheckNick(nick) is { } err) return Reply.Fail(err);
            if (CheckAvatar(avatar) is { } aerr) return Reply.Fail(aerr);
            if (size is < 2 or > HanRoom.MaxSize) return Reply.Fail("Han Ligi 2-8 kişiliktir.");
            var room = new HanRoom(NewCode(), _clock, _seed) { Size = size };
            _han[room.Code] = room;
            AttachHan(room, s, nick.Trim(), avatar);
            room.BroadcastState();
            return Reply.Success(room.Code);
        }
    }

    private static void AttachHan(HanRoom room, ServerSession s, string nick, string? avatar)
    {
        room.Seats.Add(new HanSeat { Session = s, Nick = nick, Token = NewToken(), Connected = true, Avatar = avatar });
        s.Han = room;
        s.Seat = room.Seats.Count - 1;
    }

    private Reply JoinHan(ServerSession s, HanRoom room, string nick, string? avatar)
    {
        if (room.Phase != RoomPhase.Lobby) return Reply.Fail("Bu odada maç başlamış.");
        if (room.Humans >= room.Size) return Reply.Fail("Oda dolu.");
        if (room.Seats.Any(x => string.Equals(x.Nick, nick.Trim(), StringComparison.OrdinalIgnoreCase))) return Reply.Fail("Odadaki oyuncuyla aynı takma ad.");
        if (CheckAvatar(avatar) is { } aerr) return Reply.Fail(aerr);
        AttachHan(room, s, nick.Trim(), avatar);
        foreach (var st in room.Seats) st.Ready = false;   // yeni biri geldi: herkes yeniden onaylasın
        room.Touch();
        room.BroadcastState();
        return Reply.Success("han:" + room.Code);
    }

    /// <summary>Oda kuran, bekleme odasında maçtaki toplam koltuk sayısını değiştirir (en az içerideki insan sayısı).</summary>
    public Reply HanSetSize(ServerSession s, int size)
    {
        lock (_gate)
        {
            if (s.Han is not { } room) return Reply.Fail("Bir Han odasında değilsin.");
            if (s.Seat != 0) return Reply.Fail("Oyuncu sayısını yalnız odayı kuran seçer.");
            if (room.Phase != RoomPhase.Lobby) return Reply.Fail("Maç başladı.");
            if (size < Math.Max(2, room.Humans) || size > HanRoom.MaxSize) return Reply.Fail($"Oyuncu sayısı {Math.Max(2, room.Humans)}-{HanRoom.MaxSize} olmalı.");
            room.Size = size;
            foreach (var st in room.Seats) st.Ready = false;
            room.Touch();
            room.BroadcastState();
            return Reply.Success();
        }
    }

    /// <summary>Bekleme odasında hazır. Odadaki bütün insanlar hazır olunca maç başlar (eksikleri bot doldurur).</summary>
    public Reply HanSetReady(ServerSession s, bool ready)
    {
        lock (_gate)
        {
            if (s.Han is not { } room) return Reply.Fail("Bir Han odasında değilsin.");
            if (room.Phase != RoomPhase.Lobby) return Reply.Fail("Hazır olma yalnız bekleme odasında.");
            room.Seats[s.Seat].Ready = ready;
            room.Touch();
            if (room.Seats.All(x => x.Bot || (x.Connected && x.Ready))) room.Start();
            else room.BroadcastState();
            return Reply.Success();
        }
    }

    /// <summary>Oyuncunun eylemi: yalnız kendi koltuğu adına; sunucuya özgü eylemler (kilit, round sonu, devralma) reddedilir.</summary>
    public Reply HanSubmit(ServerSession s, HanAction action)
    {
        lock (_gate)
        {
            if (s.Han is not { Phase: RoomPhase.Playing } room) return Reply.Fail("Devam eden bir Han maçı yok.");
            if (action.Kind is HanActKind.Lock or HanActKind.EndRound or HanActKind.Abandon) return Reply.Fail("Bu eylemi yalnız sunucu yapar.");
            if (action.Player != s.Seat) return Reply.Fail("Başka bir koltuk adına oynanamaz.");
            return room.Apply(action) ? Reply.Success() : Reply.Fail("Kural dışı eylem.");
        }
    }

    private Reply RejoinHan(ServerSession s, HanRoom room, string token, int lastSeq)
    {
        var seat = room.Seats.FindIndex(x => !x.Bot && x.Token == token && token.Length > 0);
        if (seat < 0) return Reply.Fail("Koltuk anahtarı geçersiz.");
        var st = room.Seats[seat];
        if (st is { Connected: true, Session: not null }) st.Session.Han = null;
        st.Session = s;
        st.Connected = true;
        st.DisconnectedAt = null;
        s.Han = room;
        s.Seat = seat;
        room.Touch();
        room.BroadcastState();
        room.Resync(seat, lastSeq);
        return Reply.Success("han:" + room.Code);
    }

    /// <summary>Han odasından bilerek ayrılma: bekleme odasında koltuk boşalır, maçta koltuğu bot devralır. Host ayrılırsa oda kapanır.</summary>
    private void LeaveHan(ServerSession s)
    {
        if (s.Han is not { } room) return;
        var seat = s.Seat;
        s.Han = null;
        s.Seat = -1;
        if (seat < 0 || seat >= room.Seats.Count) return;
        var st = room.Seats[seat];
        if (seat == 0) { CloseHan(room); return; }
        if (room.Phase == RoomPhase.Lobby)
        {
            room.Seats.RemoveAt(seat);
            for (var i = seat; i < room.Seats.Count; i++) if (room.Seats[i].Session is { } other) other.Seat = i;
            room.Touch();
            room.BroadcastState();
            return;
        }
        st.Connected = false;
        st.Session = null;
        st.DisconnectedAt = _clock.UtcNow;
        if (room.Phase == RoomPhase.Playing && room.Game is { } g && !g.Bots.Contains(seat)) room.Apply(new HanAction(HanActKind.Abandon, seat));
        room.BroadcastState();
    }

    private void DisconnectHan(ServerSession s)
    {
        if (s.Han is not { } room) return;
        var seat = s.Seat;
        if (seat < 0 || seat >= room.Seats.Count || room.Seats[seat].Session != s) return;
        if (room.Phase != RoomPhase.Playing) { LeaveHan(s); return; }
        var st = room.Seats[seat];
        st.Connected = false;
        st.Session = null;
        st.DisconnectedAt = _clock.UtcNow;
        s.Han = null;
        room.Touch();
        room.BroadcastState();
    }

    private void CloseHan(HanRoom room)
    {
        foreach (var st in room.Seats)
        {
            if (st.Session is not { } other) continue;
            if (other.Client is IGwentClient c) c.OnNotice("room_closed", "Oda kapandı.");
            other.Han = null;
            other.Seat = -1;
        }
        _han.Remove(room.Code);
    }

    /// <summary>Süre aşımları ve bot devralmaları (host her saniye çağırır; sınamalar sahte saatle).</summary>
    public void TickHan()
    {
        lock (_gate)
            foreach (var room in _han.Values.ToList()) room.Tick(HanStall, DisconnectGrace);
    }

    private int SweepHan(TimeSpan idle)
    {
        var dead = _han.Values.Where(r => _clock.UtcNow - r.LastActivity > idle || r.Seats.All(x => x.Bot || !x.Connected)).Select(r => r.Code).ToList();
        foreach (var c in dead) _han.Remove(c);
        return dead.Count;
    }
}
