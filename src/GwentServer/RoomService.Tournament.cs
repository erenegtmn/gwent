using GwentNet;

namespace GwentServer;

/// <summary>
/// Turnuva işlemleri (RoomService'in parçası; aynı kilit ve aynı oda altyapısı). Akış: oda kur → oyuncular katılır, deste seçer, hazır olur →
/// host başlatır → her eşleşme için iki oyuncu "hazırım" der, sunucu bir maç odası kurar → maç bitince galip bracket'te ilerler.
/// Maç odaları normal odalardır (aynı hamle doğrulaması, yeniden bağlanma, teslim); kod: TURNUVAKODU-RE (R=tur, E=eşleşme no).
/// </summary>
public sealed partial class RoomService
{
    private readonly Dictionary<string, Tournament> _tournaments = new(StringComparer.OrdinalIgnoreCase);

    public int TournamentCount { get { lock (_gate) return _tournaments.Count; } }

    public Tournament? FindTournament(string code)
    {
        lock (_gate) return _tournaments.GetValueOrDefault(code.Trim());
    }

    // ---------- Kurma / katılma / bekleme odası ----------

    public Reply CreateTournament(ServerSession s, string nick, string version, bool nextGen, int maxPlayers, int entryFee)
    {
        lock (_gate)
        {
            if (s.Room != null || s.Tournament != null) return Reply.Fail("Zaten bir odadasın.");
            if (version != ProtocolInfo.Version) return Reply.Fail($"Sürüm uyuşmuyor (sende {version}, sunucuda {ProtocolInfo.Version}). Oyunu güncelle.");
            if (CheckNick(nick) is { } err) return Reply.Fail(err);
            if (maxPlayers is not (4 or 8)) return Reply.Fail("Turnuva 4 ya da 8 kişilik olur.");
            if (entryFee is < 0 or > 10000) return Reply.Fail("Katılım ücreti 0-10000 taç arası olmalı.");
            var t = new Tournament(NewCode(), nextGen, maxPlayers, entryFee, _clock.UtcNow);
            _tournaments[t.Code] = t;
            AddPlayer(t, s, nick.Trim());
            BroadcastTournament(t);
            return Reply.Success(t.Code);
        }
    }

    public Reply JoinTournament(ServerSession s, string code, string nick, string version)
    {
        lock (_gate)
        {
            if (s.Room != null || s.Tournament != null) return Reply.Fail("Zaten bir odadasın.");
            if (version != ProtocolInfo.Version) return Reply.Fail($"Sürüm uyuşmuyor (sende {version}, sunucuda {ProtocolInfo.Version}). Oyunu güncelle.");
            if (CheckNick(nick) is { } err) return Reply.Fail(err);
            if (!_tournaments.TryGetValue(code.Trim(), out var t)) return Reply.Fail("Böyle bir turnuva yok.");
            if (t.Phase != TournamentPhase.Lobby) return Reply.Fail("Bu turnuva başlamış.");
            if (t.Players.Count >= t.MaxPlayers) return Reply.Fail("Turnuva dolu.");
            if (t.Players.Any(p => string.Equals(p.Nick, nick.Trim(), StringComparison.OrdinalIgnoreCase))) return Reply.Fail("Turnuvada aynı takma adlı bir oyuncu var.");
            AddPlayer(t, s, nick.Trim());
            Touch(t);
            BroadcastTournament(t);
            return Reply.Success(t.Code);
        }
    }

    private void AddPlayer(Tournament t, ServerSession s, string nick)
    {
        t.Players.Add(new TPlayer { Session = s, Nick = nick, Token = NewToken(), Connected = true });
        s.Tournament = t;
        s.TIndex = t.Players.Count - 1;
    }

    private void Touch(Tournament t) => t.LastActivity = _clock.UtcNow;

    public Reply SetTournamentLoadout(ServerSession s, LoadoutDto loadout)
    {
        lock (_gate)
        {
            if (s.Tournament is not { } t) return Reply.Fail("Bir turnuvada değilsin.");
            if (t.Phase != TournamentPhase.Lobby) return Reply.Fail("Deste yalnız turnuva başlamadan seçilir (turnuva boyunca kilitlidir).");
            if (Room.ValidateLoadout(loadout, t.NextGen) is { } err) return Reply.Fail(err);
            var p = t.Players[s.TIndex];
            p.Loadout = loadout;
            p.Ready = false;
            Touch(t);
            BroadcastTournament(t);
            return Reply.Success();
        }
    }

    /// <summary>Bekleme odasında "hazırım"; turnuva başlayınca sıradaki maça "hazırım" (iki taraf hazırsa sunucu maçı kurar).</summary>
    public Reply TournamentSetReady(ServerSession s, bool ready)
    {
        lock (_gate)
        {
            if (s.Tournament is not { } t) return Reply.Fail("Bir turnuvada değilsin.");
            var p = t.Players[s.TIndex];
            if (t.Phase == TournamentPhase.Lobby)
            {
                if (ready && p.Loadout == null) return Reply.Fail("Önce fraksiyon, lider ve deste seç.");
                p.Ready = ready;
            }
            else if (t.Phase == TournamentPhase.Running)
            {
                if (p.Eliminated || p.Current == null || p.Current.Done) return Reply.Fail("Sıradaki bir maçın yok.");
                if (s.Room is { Phase: RoomPhase.Playing }) return Reply.Fail("Maçın sürüyor.");
                if (s.Room is { Phase: RoomPhase.Finished }) { s.Room = null; s.Seat = -1; }   // önceki maçın sonuç ekranından ayrıl
                p.Ready = ready;
            }
            else return Reply.Fail("Turnuva bitti.");
            Touch(t);
            BroadcastTournament(t);
            if (t.Phase == TournamentPhase.Running) TryStartMatches(t);
            return Reply.Success();
        }
    }

    /// <summary>Host turnuvayı başlatır: en az 3 oyuncu, hepsi hazır olmalı. Bracket karıştırılarak kurulur.</summary>
    public Reply StartTournament(ServerSession s)
    {
        lock (_gate)
        {
            if (s.Tournament is not { } t || s.TIndex != 0) return Reply.Fail("Turnuvayı yalnız kuran başlatabilir.");
            if (t.Phase != TournamentPhase.Lobby) return Reply.Fail("Turnuva zaten başladı.");
            if (t.Players.Count < 3) return Reply.Fail("Turnuva için en az 3 oyuncu gerekir.");
            if (t.Players.Any(p => !p.Ready || p.Loadout == null)) return Reply.Fail("Herkes deste seçip hazır olmalı.");
            var rng = new Random(_seed());
            var order = Enumerable.Range(0, t.Players.Count).OrderBy(_ => rng.Next()).ToList();
            t.Phase = TournamentPhase.Running;
            foreach (var p in t.Players) p.Ready = false;
            t.BuildBracket(order);
            Touch(t);
            ResolveForfeits(t);
            BroadcastTournament(t);
            for (var i = 0; i < t.Players.Count; i++)
                SendTournament(t, i, c => c.OnNotice("tournament_started", "Turnuva başladı!"));
            return Reply.Success();
        }
    }

    /// <summary>Rakibi uzun süredir bağlantısız olan oyuncu eşleşmeyi hükmen kazanır (maç sürüyorsa normal "galibiyeti al" yolu).</summary>
    public Reply TournamentClaimWalkover(ServerSession s)
    {
        lock (_gate)
        {
            if (s.Tournament is not { Phase: TournamentPhase.Running } t) return Reply.Fail("Devam eden bir turnuva yok.");
            var me = t.Players[s.TIndex];
            var m = me.Current;
            if (m == null || m.Done || m.A == null || m.B == null) return Reply.Fail("Bekleyen bir eşleşmen yok.");
            var oppIdx = m.A == s.TIndex ? m.B.Value : m.A.Value;
            var opp = t.Players[oppIdx];
            if (m.Room is { Phase: RoomPhase.Playing }) return ClaimWin(s);
            if (opp.Connected) return Reply.Fail("Rakip bağlı görünüyor.");
            if (opp.DisconnectedAt == null || _clock.UtcNow - opp.DisconnectedAt < DisconnectGrace) return Reply.Fail("Rakibe geri dönmesi için biraz daha süre tanı.");
            t.Advance(m, s.TIndex);
            Touch(t);
            ResolveForfeits(t);
            BroadcastTournament(t);
            return Reply.Success();
        }
    }

    /// <summary>Bağlantısı kopan turnuva oyuncusu anahtarıyla döner; sürmekte olan maçı varsa o odaya da geri girer.</summary>
    public Reply RejoinTournament(ServerSession s, string code, string token, int lastSeq = -1)
    {
        lock (_gate)
        {
            if (s.Room != null || s.Tournament != null) return Reply.Fail("Zaten bir odadasın.");
            if (!_tournaments.TryGetValue(code.Trim(), out var t)) return Reply.Fail("Turnuva artık yok.");
            var idx = token.Length == 0 ? -1 : t.Players.FindIndex(p => p.Token == token);
            if (idx < 0) return Reply.Fail("Oyuncu anahtarı geçersiz.");
            Reattach(t, idx, s);
            var room = t.Players[idx].Current?.Room;
            if (room is { Phase: RoomPhase.Playing }) Rejoin(s, room.Code, token, lastSeq);
            return Reply.Success(t.Code);
        }
    }

    private void Reattach(Tournament t, int idx, ServerSession s)
    {
        var p = t.Players[idx];
        if (p.Session != null && p.Session != s) { p.Session.Tournament = null; p.Session.TIndex = -1; }
        p.Session = s;
        p.Connected = true;
        p.DisconnectedAt = null;
        s.Tournament = t;
        s.TIndex = idx;
        Touch(t);
        BroadcastTournament(t);
    }

    // ---------- Maç kurma ve bitiş ----------

    private void TryStartMatches(Tournament t)
    {
        foreach (var m in t.Pending.ToList())
        {
            if (m.Room != null) continue;   // oynanıyor ya da berabere bitti (rövanşla aynı odada yeniden başlar)
            var a = t.Players[m.A!.Value];
            var b = t.Players[m.B!.Value];
            if (!(a.Ready && b.Ready && a.Connected && b.Connected && a.Session != null && b.Session != null)) continue;
            StartTournamentMatch(t, m, a, b);
        }
    }

    private void StartTournamentMatch(Tournament t, TMatch m, TPlayer a, TPlayer b)
    {
        var room = new Room($"{t.Code}-{m.Round + 1}{m.Index + 1}", t.NextGen, _clock, _seed) { Tournament = t, TMatch = m };
        room.Finished = OnTournamentRoomFinished;
        _rooms[room.Code] = room;
        m.Room = room;
        var players = new[] { a, b };
        for (var seat = 0; seat < 2; seat++)
        {
            var p = players[seat];
            room.Seats[seat] = new Seat { Session = p.Session, Nick = p.Nick, Token = p.Token, Loadout = p.Loadout, Ready = true, Connected = true };
            p.Session!.Room = room;
            p.Session.Seat = seat;
            p.Ready = false;
        }
        Touch(t);
        room.StartMatch();
        BroadcastTournament(t);
    }

    private void OnTournamentRoomFinished(Room room, MatchResult result)
    {
        if (room.Tournament is not { } t || room.TMatch is not { } m) return;
        if (result.WinnerSeat is not { } w) return;   // berabere: oyuncular rövanş ister, galip çıkana dek tekrarlanır
        t.Advance(m, w == 0 ? m.A!.Value : m.B!.Value);
        Touch(t);
        ResolveForfeits(t);
        BroadcastTournament(t);
        if (t.Phase == TournamentPhase.Finished)
            for (var i = 0; i < t.Players.Count; i++)
                SendTournament(t, i, c => c.OnNotice("tournament_finished", $"Turnuva bitti: şampiyon {t.Players.First(p => p.Placement == 1).Nick}."));
    }

    /// <summary>Turnuvadan ayrılmış (hükmen yenik) oyuncunun eşleşmelerinde rakibi otomatik ilerletir; zincirleme çözülür.</summary>
    private void ResolveForfeits(Tournament t)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var m in t.Pending.ToList())
            {
                if (m.Room is { Phase: RoomPhase.Playing }) continue;
                var fa = t.Players[m.A!.Value].Forfeit;
                var fb = t.Players[m.B!.Value].Forfeit;
                if (!fa && !fb) continue;
                t.Advance(m, fa ? m.B!.Value : m.A!.Value);
                changed = true;
            }
        } while (changed && t.Phase == TournamentPhase.Running);
    }

    // ---------- Gönderim ----------

    private void SendTournament(Tournament t, int idx, Action<IGwentClient> send)
    {
        var p = t.Players[idx];
        if (p is { Connected: true, Session: not null }) send(p.Session.Client);
    }

    private void BroadcastTournament(Tournament t)
    {
        for (var i = 0; i < t.Players.Count; i++)
        {
            var idx = i;
            var state = t.Snapshot(idx, t.Phase != TournamentPhase.Lobby);
            SendTournament(t, idx, c => c.OnTournamentState(state));
        }
    }

    // ---------- Ayrılma / kopma (Leave ve Disconnect bunları çağırır; kilit altında) ----------

    private void TournamentDisconnected(ServerSession s)
    {
        if (s.Tournament is not { } t) return;
        var idx = s.TIndex;
        var p = t.Players[idx];
        if (p.Session != s) return;
        if (t.Phase == TournamentPhase.Lobby)
        {
            RemoveFromLobby(t, idx);
            return;
        }
        p.Connected = false;
        p.DisconnectedAt = _clock.UtcNow;
        p.Session = null;
        s.Tournament = null;
        s.TIndex = -1;
        Touch(t);
        BroadcastTournament(t);
    }

    /// <summary>Bilerek ayrılma: bekleme odasında yerini boşaltır (host ayrılırsa turnuva kapanır); turnuva sürerken hükmen yenik sayılır.</summary>
    private void TournamentLeft(ServerSession s)
    {
        if (s.Tournament is not { } t) return;
        var idx = s.TIndex;
        var p = t.Players[idx];
        if (p.Session != s) return;
        if (t.Phase == TournamentPhase.Lobby) { RemoveFromLobby(t, idx); return; }
        if (t.Phase == TournamentPhase.Running && !p.Eliminated) p.Forfeit = true;   // sürmekte olan maç RoomService.Leave içinde teslimle biter
        p.Connected = false;
        p.Session = null;
        s.Tournament = null;
        s.TIndex = -1;
        Touch(t);
        if (t.Phase == TournamentPhase.Running) ResolveForfeits(t);
        BroadcastTournament(t);
    }

    private void RemoveFromLobby(Tournament t, int idx)
    {
        var leaving = t.Players[idx];
        if (leaving.Session != null) { leaving.Session.Tournament = null; leaving.Session.TIndex = -1; }
        if (idx == 0)
        {
            // Kuran ayrılırsa turnuva kapanır (sunucu zaten onun bilgisayarı)
            for (var i = 1; i < t.Players.Count; i++)
            {
                SendTournament(t, i, c => c.OnNotice("tournament_closed", "Turnuva kapandı: kuran ayrıldı."));
                if (t.Players[i].Session is { } ps) { ps.Tournament = null; ps.TIndex = -1; }
            }
            _tournaments.Remove(t.Code);
            return;
        }
        t.Players.RemoveAt(idx);
        for (var i = 0; i < t.Players.Count; i++) if (t.Players[i].Session is { } ps) ps.TIndex = i;
        Touch(t);
        BroadcastTournament(t);
    }

    private int SweepTournaments(TimeSpan idle)
    {
        var dead = _tournaments.Values.Where(t => _clock.UtcNow - t.LastActivity > idle
            || t.Players.All(p => !p.Connected && t.Phase != TournamentPhase.Running)).Select(t => t.Code).ToList();
        foreach (var code in dead)
        {
            var t = _tournaments[code];
            foreach (var m in t.AllMatches) if (m.Room != null) _rooms.Remove(m.Room.Code);
            _tournaments.Remove(code);
        }
        return dead.Count;
    }
}
