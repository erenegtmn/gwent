namespace GwentCore;

public enum Phase { Mulligan, ChooseFirst, Playing, GameOver }

public enum ChoiceKind
{
    Medic,            // mezarlıktan bir birim seç (çevik ise sıra da)
    AgileRow,         // seçim değil, yerleştirme: çevik birimin sırası
    RelentlessPick,   // rakibin mezarlığından kart al
    BringerOfDeath,   // kendi mezarlığından ele kart al
    DestroyerDiscard, // elden 2 kart at
    DestroyerDraw,    // destenden 1 kart al
    KingWeather       // destenden bir hava kartı oyna
}

public sealed record GameOptions
{
    /// <summary>Yalnız yeni nesil (next-gen) güncellemesindeki içerik: Roach ve Geralt/Ciri'nin Muster'ı.</summary>
    public bool NextGen { get; init; }

    // ---------- Lanetler (Gaunter O'Dimm meydan okumaları; varsayılanlar orijinal kuraldır) ----------

    /// <summary>Başlangıç eli (orijinal 10; "Kısa Mum" 8).</summary>
    public int StartingHand { get; init; } = Game.StartingHand;
    /// <summary>"Bitmeyen Kış": her raund rastgele bir hava kartıyla (Don/Sis/Yağmur) açılır; Açık hava kaldırabilir.</summary>
    public bool RoundWeather { get; init; }
    /// <summary>"Kan Bedeli": raundu kazanan elinden rastgele bir kart kaybeder (mezarlığa).</summary>
    public bool BloodPrice { get; init; }

    // ---------- Kaer Morhen (TASARIM.md §5; varsayılan kapalı) ----------

    /// <summary>Savaş alanı: satırların arazisi (<see cref="GwentCore.Terrain"/>). null = klasik tahta.</summary>
    public TerrainMap? Terrain { get; init; }
    /// <summary>Komutan figürü: bulunduğu satırı havadan korur; raund başına bir kez taşınabilir (bir hamle sayılır).</summary>
    public bool Commanders { get; init; }
}

public sealed class CardInstance
{
    public int Uid { get; }
    public CardDef Def { get; }
    /// <summary>Kartın bulunduğu bölgenin sahibi (el/deste/mezarlık/saha tarafı). Casus kartta saha tarafı rakiptir.</summary>
    public int Holder { get; internal set; }
    /// <summary>Monsters yeteneği: raund sonunda sahadan kaldırılmaz.</summary>
    public bool NoRemove { get; internal set; }
    /// <summary>Gerçek bir kart değil, bir etkinin işaretçisi (lider boynuzu). Mezarlığa girmez.</summary>
    public bool Ephemeral { get; internal set; }
    public IReadOnlyList<string> Abilities { get; }

    private CardInstance(CardInstance src)
    {
        Uid = src.Uid;
        Def = src.Def;
        Holder = src.Holder;
        NoRemove = src.NoRemove;
        Ephemeral = src.Ephemeral;
        Abilities = src.Abilities; // değişmez liste, paylaşılabilir
    }

    internal CardInstance CloneInstance() => new(this);

    internal CardInstance(int uid, CardDef def, int holder, bool nextGen)
    {
        Uid = uid;
        Def = def;
        Holder = holder;
        var ab = new List<string>(def.Abilities);
        if (nextGen && def.NextgenAbilities is { Count: > 0 }) ab.AddRange(def.NextgenAbilities);
        Abilities = ab;
    }

    public bool Has(string ability) => Abilities.Contains(ability);
    public override string ToString() => $"{Def.Name}#{Uid}";
}

public sealed class BoardRow
{
    public List<CardInstance> Cards { get; } = new();
    /// <summary>Komutan Boynuzu veya Mardroeme (aynı yuva). Lider boynuzu da burada jeton olarak durur.</summary>
    public CardInstance? Special { get; internal set; }
}

public sealed class PlayerState
{
    public int Index { get; }
    public string Faction { get; }
    public CardDef Leader { get; }
    public bool LeaderUsed { get; internal set; }
    public List<CardInstance> Deck { get; } = new();
    public List<CardInstance> Hand { get; } = new();
    public List<CardInstance> Grave { get; } = new();
    public BoardRow[] Rows { get; } = { new(), new(), new() };
    public int Gems { get; internal set; } = 2;
    public bool Passed { get; internal set; }
    public bool MulliganDone { get; internal set; }
    /// <summary>Komutan figürünün satırı (<see cref="GameOptions.Commanders"/> kapalıysa null).</summary>
    public RowKind? Commander { get; internal set; }
    /// <summary>Komutan bu raund taşındı mı.</summary>
    public bool CommanderMoved { get; internal set; }

    internal PlayerState(int index, string faction, CardDef leader)
    {
        Index = index;
        Faction = faction;
        Leader = leader;
    }

    public BoardRow Row(RowKind k) => Rows[(int)k];
}

public sealed record RoundResult(int Round, int Score0, int Score1, int? Winner);

public sealed record GameOutcome(int? Winner); // null = beraberlik

/// <summary>Arayüz, kayıt ve ağ için olay günlüğü. Visible != null ise yalnız o oyuncu görür.</summary>
public sealed record GameEvent(int Seq, string Kind, int Player, int CardUid, string Detail, int? Visible = null);

public sealed record ChoiceResponse(IReadOnlyList<int> Cards, RowKind? Row = null);

public sealed class PendingChoice
{
    public int Player { get; init; }
    public ChoiceKind Kind { get; init; }
    public IReadOnlyList<int> Candidates { get; init; } = Array.Empty<int>();
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
    /// <summary>Sıra seçimi gerekiyorsa izin verilen sıralar (AgileRow); Medic'te seçilen çevikse zorunlu.</summary>
    public IReadOnlyList<RowKind> Rows { get; init; } = Array.Empty<RowKind>();
    public string Prompt { get; init; } = "";
    /// <summary>Çevik birimin sırası beklenirken kart hiçbir bölgede değildir; seçim onu burada taşır.</summary>
    public CardInstance? LimboCard { get; init; }
    internal Action<ChoiceResponse> Resolve { get; init; } = _ => { };
}

public readonly record struct ActionResult(bool Ok, string? Error)
{
    public static ActionResult Success => new(true, null);
    public static ActionResult Fail(string error) => new(false, error);
}

public abstract record GameAction(int Player);
public sealed record Mulligan(int Player, IReadOnlyList<int> Cards) : GameAction(Player);
public sealed record ChooseFirst(int Player, int First) : GameAction(Player);
public sealed record PlayCard(int Player, int Card, RowKind? Row = null, int? Target = null) : GameAction(Player);
public sealed record Pass(int Player) : GameAction(Player);
public sealed record UseLeader(int Player) : GameAction(Player);
public sealed record Choose(int Player, IReadOnlyList<int> Cards, RowKind? Row = null) : GameAction(Player);

/// <summary>Seri hâle getirilebilir deste tanımı. Kartlar tekrarlı yazılır (3 kopya = 3 kez).</summary>
public sealed record Deck(string Faction, string LeaderId, IReadOnlyList<string> CardIds);

/// <summary>
/// Tohumlu, kopyalanabilir rastgele sayı üreteci (SplitMix64). Kendi algoritmamız olduğu için
/// .NET sürümünden bağımsız aynı diziyi verir: sunucu ve test tekrarları tutarlı kalır.
/// </summary>
public sealed class GameRandom
{
    private ulong _state;

    public GameRandom(int seed) => _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
    private GameRandom(ulong state) => _state = state;

    public GameRandom Clone() => new(_state);

    private ulong NextU64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        return (int)(NextU64() % (ulong)exclusiveMax);
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];
}
