namespace GwentCore;

/// <summary>
/// Gwent kural motoru. Arayüzden, ağdan ve zamandan bağımsızdır: aynı desteler, aynı tohum (seed)
/// ve aynı eylem dizisi her zaman aynı sonucu verir. Durumu yalnız <see cref="Apply"/> değiştirir.
/// </summary>
public sealed partial class Game
{
    public const int StartingHand = 10;
    public const int MaxMulligans = 2;

    public GameOptions Options { get; }
    public CardCatalog Catalog { get; }
    public IReadOnlyList<PlayerState> Players => _players;
    public List<CardInstance> WeatherCards { get; } = new();
    public Phase Phase { get; private set; }
    /// <summary>Sırası gelen oyuncu. Bekleyen seçim varsa seçimi yapacak olan <see cref="Pending"/>.Player'dır.</summary>
    public int Current { get; private set; }
    /// <summary>İçinde bulunulan raundu ilk başlatan oyuncu.</summary>
    public int FirstPlayer { get; private set; }

    /// <summary>Yazı-tura sonucu: oyun kurulurken belirlenir (arayüz maç başında gösterebilsin diye); Scoia'tael seçiminde kullanılmaz.</summary>
    public int CoinFirst { get; private set; }
    public int Round { get; private set; } = 1;
    public PendingChoice? Pending { get; private set; }
    public GameOutcome? Outcome { get; private set; }
    public List<RoundResult> History { get; } = new();
    public IReadOnlyList<GameEvent> Events => _events;
    public int Seed { get; }

    private readonly PlayerState[] _players = new PlayerState[2];
    private readonly GameRandom _rng;
    private readonly List<GameEvent> _events = new();
    private readonly Queue<Action> _work = new();
    /// <summary>Sıradaki işlerden önce yapılacaklar (harabedeki Medic'in ikinci canlandırması: tur bitmeden çalışmalı).</summary>
    private readonly Queue<Action> _front = new();
    private int _uid;
    private int? _lastRoundWinner;
    private bool _clearing;
    private readonly List<CardInstance> _clearedAvengers = new();

    public Game(Deck deck0, Deck deck1, int seed, GameOptions? options = null, bool validate = true, CardCatalog? catalog = null)
    {
        Options = options ?? new GameOptions();
        Catalog = catalog ?? CardCatalog.Default;
        Seed = seed;
        _rng = new GameRandom(seed);

        if (validate)
        {
            foreach (var (d, i) in new[] { (deck0, 0), (deck1, 1) })
            {
                var errs = DeckValidator.Validate(d, Options, Catalog);
                if (errs.Count > 0) throw new ArgumentException($"Oyuncu {i} destesi geçersiz: {string.Join(" ", errs)}");
            }
        }

        var decks = new[] { deck0, deck1 };
        // Önce iki oyuncu da kurulur: lider yetenekleri (White Flame, Daisy) rakibe bakar.
        for (var p = 0; p < 2; p++)
            _players[p] = new PlayerState(p, decks[p].Faction, Catalog.Get(decks[p].LeaderId))
            {
                Commander = Options.Commanders ? RowKind.Close : null
            };
        for (var p = 0; p < 2; p++)
        {
            var ps = _players[p];
            foreach (var id in decks[p].CardIds) ps.Deck.Add(NewCard(id, p));
            _rng.Shuffle(ps.Deck);
            for (var i = 0; i < Options.StartingHand && ps.Deck.Count > 0; i++) DrawOne(ps);
            // Daisy of the Valley: oyun başında fazladan 1 kart (rakipte White Flame varsa iptal).
            if (LeaderPassive(p, "francesca_findabair_daisy_of_the_valley") && ps.Deck.Count > 0) DrawOne(ps);
        }

        CoinFirst = _rng.Next(2);
        Phase = Phase.Mulligan;
        Emit("game_start", -1, 0, $"seed={seed}");
    }

    // ---------- Kopyalama ----------

    private Game(Game s)
    {
        Options = s.Options;
        Catalog = s.Catalog;
        Seed = s.Seed;
        _rng = s._rng.Clone();
        Phase = s.Phase;
        Current = s.Current;
        FirstPlayer = s.FirstPlayer;
        CoinFirst = s.CoinFirst;
        Round = s.Round;
        Outcome = s.Outcome;
        _uid = s._uid;
        _lastRoundWinner = s._lastRoundWinner;
        History.AddRange(s.History);

        var map = new Dictionary<CardInstance, CardInstance>();
        CardInstance M(CardInstance c)
        {
            if (!map.TryGetValue(c, out var n)) map[c] = n = c.CloneInstance();
            return n;
        }

        for (var i = 0; i < 2; i++)
        {
            var o = s._players[i];
            var n = new PlayerState(i, o.Faction, o.Leader)
            {
                LeaderUsed = o.LeaderUsed,
                Gems = o.Gems,
                Passed = o.Passed,
                MulliganDone = o.MulliganDone,
                Commander = o.Commander,
                CommanderMoved = o.CommanderMoved
            };
            n.Deck.AddRange(o.Deck.Select(M));
            n.Hand.AddRange(o.Hand.Select(M));
            n.Grave.AddRange(o.Grave.Select(M));
            for (var r = 0; r < 3; r++)
            {
                n.Rows[r].Cards.AddRange(o.Rows[r].Cards.Select(M));
                n.Rows[r].Special = o.Rows[r].Special == null ? null : M(o.Rows[r].Special!);
            }
            _players[i] = n;
        }
        WeatherCards.AddRange(s.WeatherCards.Select(M));
    }

    /// <summary>
    /// Oyunun bağımsız derin kopyası (yapay zekâ "bu hamle ne olur?" diye denemek için kullanır).
    /// Yalnız tur arasında kopyalanabilir: bekleyen seçim varken olmaz, çünkü seçimin devamı kopyalanamaz.
    /// Olay günlüğü kopyaya taşınmaz.
    /// </summary>
    public Game Clone()
    {
        if (Pending != null) throw new InvalidOperationException("Bekleyen bir seçim varken oyun kopyalanamaz.");
        if (_work.Count > 0 || _front.Count > 0) throw new InvalidOperationException("İşlem sürerken oyun kopyalanamaz.");
        return new Game(this);
    }

    /// <summary>Şu an eylem bekleyen oyuncular (kart değiştirmede ikisi birden olabilir).</summary>
    public IReadOnlyList<int> PlayersToAct()
    {
        switch (Phase)
        {
            case Phase.GameOver: return Array.Empty<int>();
            case Phase.Mulligan: return _players.Where(p => !p.MulliganDone).Select(p => p.Index).ToList();
            case Phase.ChooseFirst: return new[] { Current };
            default: return new[] { Pending?.Player ?? Current };
        }
    }

    // ---------- Yardımcılar ----------

    internal CardInstance NewCard(string id, int holder) => new(++_uid, Catalog.Get(id), holder, Options.NextGen);

    private void DrawOne(PlayerState p)
    {
        if (p.Deck.Count == 0) return;
        var c = p.Deck[0];
        p.Deck.RemoveAt(0);
        p.Hand.Add(c);
    }

    private void Draw(int player, int count)
    {
        var p = _players[player];
        var n = 0;
        for (var i = 0; i < count && p.Deck.Count > 0; i++) { DrawOne(p); n++; }
        if (n > 0) Emit("draw", player, 0, n.ToString());
    }

    private void Emit(string kind, int player, int uid, string detail = "", int? visible = null) =>
        _events.Add(new GameEvent(_events.Count, kind, player, uid, detail, visible));

    private PlayerState Opp(int p) => _players[1 - p];

    // ---------- Eylem girişi ----------

    public ActionResult Apply(GameAction action)
    {
        if (Phase == Phase.GameOver) return ActionResult.Fail("Oyun bitti.");
        if (action.Player is < 0 or > 1) return ActionResult.Fail("Geçersiz oyuncu.");

        switch (action)
        {
            case Mulligan m: return DoMulligan(m);
            case ChooseFirst cf: return DoChooseFirst(cf);
            case PlayCard pc: return DoPlayCard(pc);
            case Pass pa: return DoPass(pa);
            case UseLeader ul: return DoUseLeader(ul);
            case Choose ch: return DoChoose(ch);
            case MoveCommander mc: return DoMoveCommander(mc);
            default: return ActionResult.Fail("Bilinmeyen eylem.");
        }
    }

    private void Pump()
    {
        while (Pending == null && (_front.Count > 0 || _work.Count > 0) && Phase != Phase.GameOver)
            (_front.Count > 0 ? _front.Dequeue() : _work.Dequeue())();
        if (Phase == Phase.GameOver) { _work.Clear(); _front.Clear(); }
    }

    // ---------- Oyun başı ----------

    private ActionResult DoMulligan(Mulligan m)
    {
        if (Phase != Phase.Mulligan) return ActionResult.Fail("Kart değiştirme aşaması bitti.");
        var p = _players[m.Player];
        if (p.MulliganDone) return ActionResult.Fail("Kartlarını zaten değiştirdin.");
        var uids = m.Cards.Distinct().ToList();
        if (uids.Count != m.Cards.Count) return ActionResult.Fail("Aynı kart iki kez seçilemez.");
        if (uids.Count > MaxMulligans) return ActionResult.Fail($"En fazla {MaxMulligans} kart değiştirebilirsin.");
        var cards = new List<CardInstance>();
        foreach (var uid in uids)
        {
            var c = p.Hand.FirstOrDefault(x => x.Uid == uid);
            if (c == null) return ActionResult.Fail("Seçilen kart elinde değil.");
            cards.Add(c);
        }

        foreach (var c in cards) { p.Hand.Remove(c); p.Deck.Add(c); }
        if (cards.Count > 0) _rng.Shuffle(p.Deck);
        for (var i = 0; i < cards.Count; i++) DrawOne(p);
        p.MulliganDone = true;
        Emit("mulligan", m.Player, 0, cards.Count.ToString());

        if (_players.All(x => x.MulliganDone)) AfterMulligan();
        return ActionResult.Success;
    }

    private void AfterMulligan()
    {
        var scoia = Enumerable.Range(0, 2).Where(i => _players[i].Faction == "scoiatael").ToList();
        if (scoia.Count == 1)
        {
            Phase = Phase.ChooseFirst;
            Current = scoia[0];
            Emit("choose_first", Current, 0);
            return;
        }
        var first = CoinFirst;
        Emit("coin_toss", first, 0);
        BeginRound(first);
        Pump();
    }

    private ActionResult DoChooseFirst(ChooseFirst cf)
    {
        if (Phase != Phase.ChooseFirst) return ActionResult.Fail("Şu an başlangıç oyuncusu seçilemez.");
        if (cf.Player != Current) return ActionResult.Fail("Bu seçimi sen yapamazsın.");
        if (cf.First is < 0 or > 1) return ActionResult.Fail("Geçersiz oyuncu.");
        Emit("first_chosen", cf.Player, 0, cf.First.ToString());
        BeginRound(cf.First);
        Pump();
        return ActionResult.Success;
    }

    // ---------- Pas ----------

    private ActionResult DoPass(Pass pa)
    {
        var err = CheckTurn(pa.Player);
        if (err != null) return ActionResult.Fail(err);
        _players[pa.Player].Passed = true;
        Emit("pass", pa.Player, 0);
        _work.Enqueue(EndTurn);
        Pump();
        return ActionResult.Success;
    }

    private string? CheckTurn(int player)
    {
        if (Phase != Phase.Playing) return "Şu an oynanamaz.";
        if (Pending != null) return "Önce bekleyen seçimi tamamlamalısın.";
        if (player != Current) return "Sıra sende değil.";
        if (_players[player].Passed) return "Bu raund pas geçtin.";
        return null;
    }

    // ---------- Seçim ----------

    private ActionResult DoChoose(Choose ch)
    {
        var pend = Pending;
        if (pend == null) return ActionResult.Fail("Bekleyen bir seçim yok.");
        if (ch.Player != pend.Player) return ActionResult.Fail("Bu seçimi sen yapamazsın.");
        var uids = ch.Cards.Distinct().ToList();
        if (uids.Count != ch.Cards.Count) return ActionResult.Fail("Aynı kart iki kez seçilemez.");
        if (uids.Count < pend.Min || uids.Count > pend.Max)
            return ActionResult.Fail(pend.Min == pend.Max ? $"{pend.Min} kart seçmelisin." : $"{pend.Min}-{pend.Max} kart seçmelisin.");
        if (uids.Any(u => !pend.Candidates.Contains(u))) return ActionResult.Fail("Seçilen kart bu seçim için geçerli değil.");

        if (pend.Kind == ChoiceKind.AgileRow)
        {
            if (ch.Row is not (RowKind.Close or RowKind.Ranged)) return ActionResult.Fail("Yakın veya Menzilli sıra seçmelisin.");
        }
        else if (pend.Kind == ChoiceKind.Medic)
        {
            var picked = FindInZones(uids[0]);
            if (picked != null && picked.Def.IsAgile && ch.Row is not (RowKind.Close or RowKind.Ranged))
                return ActionResult.Fail("Çevik birim için Yakın veya Menzilli sıra seçmelisin.");
        }

        Pending = null;
        pend.Resolve(new ChoiceResponse(uids, ch.Row));
        Pump();
        return ActionResult.Success;
    }

    private CardInstance? FindInZones(int uid)
    {
        foreach (var p in _players)
        {
            foreach (var zone in new[] { p.Hand, p.Deck, p.Grave })
            {
                var c = zone.FirstOrDefault(x => x.Uid == uid);
                if (c != null) return c;
            }
        }
        return null;
    }

    private void SetPending(PendingChoice pc)
    {
        Pending = pc;
        Emit("choice", pc.Player, 0, pc.Kind.ToString());
    }
}
