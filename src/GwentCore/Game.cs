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

    private readonly PlayerState[] _players;

    /// <summary>Masadaki oyuncu sayısı: 2 (klasik) ya da 3 (yalnız online, KURALLAR §24).</summary>
    public int PlayerCount => _players.Length;
    public bool ThreePlayer => _players.Length == 3;
    /// <summary>Üç kişide raund sınırı: bu raund da biter ve kimse 2 galibiyete ulaşmazsa en çok kazanan alır.</summary>
    public const int MaxRoundsThree = 6;

    /// <summary>Üç kişide başlangıç zarları: her atış oyuncu başına 1–6 (0 = o atışa katılmadı; eşitler yeniden atar). İki kişide boş.</summary>
    public IReadOnlyList<int[]> DiceRolls => _dice;
    private readonly List<int[]> _dice = new();
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
        : this(new[] { deck0, deck1 }, seed, options, validate, catalog) { }

    /// <summary>2 ya da 3 desteyle kurar. Üç kişide Kaer Morhen eklentileri ve lanetler kapalıdır (KURALLAR §24).</summary>
    public Game(IReadOnlyList<Deck> decks, int seed, GameOptions? options = null, bool validate = true, CardCatalog? catalog = null)
    {
        Options = options ?? new GameOptions();
        Catalog = catalog ?? CardCatalog.Default;
        Seed = seed;
        _rng = new GameRandom(seed);
        var n = decks.Count;
        if (n is not (2 or 3)) throw new ArgumentException("Oyun 2 ya da 3 oyuncuyla kurulur.");
        _players = new PlayerState[n];
        _perks = new Perks?[n];
        _blackBloodUsed = new bool[n];
        if (n == 3)
        {
            if (Options.Terrain != null || Options.Commanders || Options.RoundWeather || Options.BloodPrice || Options.StartingHand != StartingHand)
                throw new ArgumentException("Üç kişilik maçta lanetler ve Kaer Morhen eklentileri kapalıdır.");
            if (decks.Any(d => d.Runes is { Count: > 0 } || d.Allies is { Count: > 0 } || d.Perks != null))
                throw new ArgumentException("Üç kişilik maçta rün, müttefik ve iksir kullanılamaz.");
        }

        if (validate)
        {
            for (var i = 0; i < n; i++)
            {
                var errs = DeckValidator.Validate(decks[i], Options, Catalog);
                if (errs.Count > 0) throw new ArgumentException($"Oyuncu {i} destesi geçersiz: {string.Join(" ", errs)}");
            }
        }

        // Önce bütün oyuncular kurulur: lider yetenekleri (White Flame, Daisy) rakibe bakar.
        for (var p = 0; p < n; p++)
            _players[p] = new PlayerState(p, decks[p].Faction, Catalog.Get(decks[p].LeaderId))
            {
                Commander = Options.Commanders ? RowKind.Close : null
            };
        for (var p = 0; p < n; p++)
        {
            var ps = _players[p];
            foreach (var id in decks[p].CardIds) ps.Deck.Add(NewCard(id, p));
            ApplyRunes(ps, decks[p]);
            _rng.Shuffle(ps.Deck);
            _perks[p] = decks[p].Perks;
            for (var i = 0; i < Options.StartingHand + (_perks[p]?.ExtraHand ?? 0) && ps.Deck.Count > 0; i++) DrawOne(ps);
            // Daisy of the Valley: oyun başında fazladan 1 kart (rakipte White Flame varsa iptal).
            if (LeaderPassive(p, "francesca_findabair_daisy_of_the_valley") && ps.Deck.Count > 0) DrawOne(ps);
            AddAllies(ps, decks[p]);
        }

        CoinFirst = n == 2 ? _rng.Next(2) : RollDice();
        Phase = Phase.Mulligan;
        Emit("game_start", -1, 0, $"seed={seed}");
    }

    /// <summary>Üç kişilik başlangıç zarı: herkes atar, en yüksekte eşit kalanlar yeniden atar. Kazananın koltuğunu döndürür.</summary>
    private int RollDice()
    {
        var rolling = Enumerable.Range(0, _players.Length).ToList();
        while (true)
        {
            var roll = new int[_players.Length];
            foreach (var p in rolling) roll[p] = _rng.Next(6) + 1;
            _dice.Add(roll);
            var max = rolling.Max(p => roll[p]);
            rolling = rolling.Where(p => roll[p] == max).ToList();
            if (rolling.Count == 1) return rolling[0];
        }
    }

    // ---------- Kopyalama ----------

    private Game(Game s)
    {
        _players = new PlayerState[s._players.Length];
        _perks = new Perks?[s._players.Length];
        _blackBloodUsed = new bool[s._players.Length];
        _dice.AddRange(s._dice);
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
        Array.Copy(s._perks, _perks, _perks.Length);
        Array.Copy(s._blackBloodUsed, _blackBloodUsed, _blackBloodUsed.Length);
        History.AddRange(s.History);

        var map = new Dictionary<CardInstance, CardInstance>();
        CardInstance M(CardInstance c)
        {
            if (!map.TryGetValue(c, out var n)) map[c] = n = c.CloneInstance();
            return n;
        }

        for (var i = 0; i < _players.Length; i++)
        {
            var o = s._players[i];
            var n = new PlayerState(i, o.Faction, o.Leader)
            {
                LeaderUsed = o.LeaderUsed,
                Gems = o.Gems,
                Wins = o.Wins,
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

    /// <summary>p dışındaki oyuncular, sıradakinden başlayarak (koltuk sırası).</summary>
    public IEnumerable<int> Opponents(int p)
    {
        for (var k = 1; k < _players.Length; k++) yield return (p + k) % _players.Length;
    }

    /// <summary>Rakibe dönük etkinin hedefi: geçerli bir rakip verildiyse o, yoksa sıradaki rakip (iki kişide tek rakip).</summary>
    private int Foe(int actor, int? side) =>
        side is { } s && s >= 0 && s < _players.Length && s != actor ? s : (actor + 1) % _players.Length;

    private bool ValidFoe(int actor, int? side) => side is not { } s || (s >= 0 && s < _players.Length && s != actor);

    // ---------- Eylem girişi ----------

    public ActionResult Apply(GameAction action)
    {
        if (Phase == Phase.GameOver) return ActionResult.Fail("Oyun bitti.");
        if (action.Player < 0 || action.Player >= _players.Length) return ActionResult.Fail("Geçersiz oyuncu.");

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
        if (uids.Count > MulligansFor(m.Player)) return ActionResult.Fail($"En fazla {MulligansFor(m.Player)} kart değiştirebilirsin.");
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
        var scoia = FirstChoosers();   // Scoia'tael ya da Kışla
        // Üç kişide zar her zaman gösterilir; tek Scoia'tael varsa başlayanı yine o seçer.
        if (ThreePlayer) Emit("dice", CoinFirst, 0, string.Join("|", _dice.Select(r => string.Join(",", r))));
        if (scoia.Count == 1)
        {
            Phase = Phase.ChooseFirst;
            Current = scoia[0];
            Emit("choose_first", Current, 0);
            return;
        }
        var first = CoinFirst;
        if (!ThreePlayer) Emit("coin_toss", first, 0);
        BeginRound(first);
        Pump();
    }

    private ActionResult DoChooseFirst(ChooseFirst cf)
    {
        if (Phase != Phase.ChooseFirst) return ActionResult.Fail("Şu an başlangıç oyuncusu seçilemez.");
        if (cf.Player != Current) return ActionResult.Fail("Bu seçimi sen yapamazsın.");
        if (cf.First < 0 || cf.First >= _players.Length) return ActionResult.Fail("Geçersiz oyuncu.");
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

        if (!ValidFoe(ch.Player, ch.Side)) return ActionResult.Fail("Geçersiz hedef rakip.");

        Pending = null;
        pend.Resolve(new ChoiceResponse(uids, ch.Row, ch.Side));
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
