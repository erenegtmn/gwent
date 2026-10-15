using GwentAI;
using GwentCore;

namespace KaerMorhen.Core;

/// <summary>Mutajen renkleri (W3'teki gibi).</summary>
public enum Mutagen { Red, Green, Blue }

/// <summary>Bir bina seviyesinin bedeli.</summary>
public sealed record Cost(int Crowns, int Mutagens = 0, Mutagen Color = Mutagen.Red);

/// <summary>Bir bina: kimlik, ad, açıklama, renk ve seviye başına etki. <see cref="Planned"/> binalar henüz kurulamaz (sonraki aşamalar).</summary>
public sealed record Building(string Id, string Name, string Text, Mutagen Color, IReadOnlyList<Cost> Levels, IReadOnlyList<string> Effects, bool Planned = false)
{
    public int MaxLevel => Levels.Count;
}

/// <summary>Mühendis atölyesinin arazi parçası. Kule kendi satırına, barikat ve hendek rakibin satırına konur.</summary>
public sealed record EngineerPiece(string Id, string Name, Terrain Terrain, bool OnOpponent, string Text);

/// <summary>Sözleşme panosundaki bir iş: rakip, savaş alanı, (isteğe bağlı) lanet ve ödül.</summary>
public sealed record Contract(int Id, string Title, string Text, string Faction, string LeaderId, Difficulty Difficulty, string Battlefield,
    string? Curse, int RewardCrowns, Mutagen RewardColor, int RewardMutagens, int Seed);

/// <summary>Kalenin kaydı. Yalnız <see cref="Keep"/> değiştirir.</summary>
public sealed class KeepSave
{
    public int Version { get; set; } = 1;
    public int Crowns { get; set; }
    public int Red { get; set; }
    public int Green { get; set; }
    public int Blue { get; set; }
    public Dictionary<string, int> Buildings { get; set; } = new();
    /// <summary>Üretilen sözleşme sayısı: yeni sözleşmenin tohumu buradan gelir (deterministik).</summary>
    public int ContractCounter { get; set; }
    public List<Contract> Board { get; set; } = new();
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Seed { get; set; }
    /// <summary>Son sözleşmede seçilen deste (fraksiyon ve seçim anahtarı: "@rec" ya da "deck:Ad").</summary>
    public string? LastFaction { get; set; }
    public string? LastDeck { get; set; }

    public int Level(string building) => Buildings.GetValueOrDefault(building);

    public int Mutagens(Mutagen c) => c switch { Mutagen.Red => Red, Mutagen.Green => Green, _ => Blue };

    internal void AddMutagens(Mutagen c, int n)
    {
        switch (c)
        {
            case Mutagen.Red: Red += n; break;
            case Mutagen.Green: Green += n; break;
            default: Blue += n; break;
        }
    }
}

/// <summary>Bir sözleşmenin sonucu: kayda işlenen ödül ve oyuncuya gösterilen özet.</summary>
public sealed record ContractResult(bool Won, int Crowns, Mutagen Color, int Mutagens, string Summary);

/// <summary>Kaer Morhen kurallari: binalar, sözleşmeler, ödüller (TASARIM.md §4, §6.1).</summary>
public static class Keep
{
    public const string WarRoom = "war_room";
    public const string Forge = "forge";
    public const string Workshop = "workshop";
    public const string Alchemy = "alchemy";
    public const string Barracks = "barracks";
    public const string AlliesHall = "allies_hall";

    public const int StartCrowns = 100;

    public static readonly IReadOnlyList<Building> Buildings = new[]
    {
        new Building(WarRoom, "Savaş Odası", "Sözleşme panosu ve savaş masası.", Mutagen.Blue,
            new[] { new Cost(0), new Cost(150, 2, Mutagen.Blue), new Cost(350, 4, Mutagen.Blue) },
            new[] { "Panoda 2 sözleşme (Kolay, Normal).", "Panoda 3 sözleşme; Zor sözleşmeler gelir.", "Panoda 4 sözleşme; lanetli sözleşmeler (ödül ×1,5)." }),
        new Building(Forge, "Demirci", "Kartlara rün döver.", Mutagen.Red,
            new[] { new Cost(80), new Cost(200, 2, Mutagen.Red), new Cost(400, 4, Mutagen.Red) },
            new[] { "1 rün: Svarog, Perun.", "2 rün: + Veles, Dazhbog.", "3 rün: + Stribog, Morana." }),
        new Building(Workshop, "Mühendis Atölyesi", "Savaş alanına arazi parçası kurar.", Mutagen.Green,
            new[] { new Cost(100), new Cost(250, 2, Mutagen.Green), new Cost(450, 4, Mutagen.Green) },
            new[] { "Gözcü kulesi (kendi satırın Tepe olur).", "+ Barikat (rakibin satırı Köprü olur); maçta 2 parça.", "+ Hendek (rakibin satırı Bataklık olur); maçta 3 parça." }),
        new Building(Alchemy, "Simya Odası", "Maç öncesi iksirler (toksisite sınırıyla).", Mutagen.Green, Array.Empty<Cost>(), Array.Empty<string>(), Planned: true),
        new Building(Barracks, "Kışla", "Deste kuralları: fazladan kahraman/casus hakkı.", Mutagen.Red, Array.Empty<Cost>(), Array.Empty<string>(), Planned: true),
        new Building(AlliesHall, "Müttefik Salonu", "Zoltan, Lambert, Yennefer, Triss... Kuşatmada yanında savaşırlar.", Mutagen.Blue, Array.Empty<Cost>(), Array.Empty<string>(), Planned: true),
    };

    public static Building Get(string id) => Buildings.First(b => b.Id == id);

    public static readonly IReadOnlyList<EngineerPiece> Pieces = new[]
    {
        new EngineerPiece("tower", "Gözcü kulesi", Terrain.Hill, false, "Kendi satırın Tepe olur (+1)."),
        new EngineerPiece("barricade", "Barikat", Terrain.Bridge, true, "Rakibin satırı Köprü olur (en fazla 4 birim)."),
        new EngineerPiece("moat", "Hendek", Terrain.Swamp, true, "Rakibin satırı Bataklık olur (−1)."),
    };

    // ---------- Kale ----------

    public static KeepSave NewSave(int seed)
    {
        var s = new KeepSave { Crowns = StartCrowns, Seed = seed };
        s.Buildings[WarRoom] = 1;
        RefillBoard(s);
        return s;
    }

    /// <summary>Sıradaki seviyenin bedeli; en üst seviyede ya da planlanmış binada null.</summary>
    public static Cost? NextCost(KeepSave s, string building)
    {
        var b = Get(building);
        var lvl = s.Level(building);
        return b.Planned || lvl >= b.MaxLevel ? null : b.Levels[lvl];
    }

    /// <summary>Yükseltilemiyorsa nedeni (Türkçe), yükseltilebiliyorsa null.</summary>
    public static string? UpgradeBlocked(KeepSave s, string building)
    {
        var b = Get(building);
        if (b.Planned) return "Bu bina henüz kurulamıyor.";
        if (NextCost(s, building) is not { } c) return "En üst seviyede.";
        if (s.Crowns < c.Crowns) return $"{c.Crowns - s.Crowns} Crown eksik.";
        if (c.Mutagens > 0 && s.Mutagens(c.Color) < c.Mutagens) return $"{c.Mutagens - s.Mutagens(c.Color)} {MutagenName(c.Color)} mutajen eksik.";
        return null;
    }

    public static bool Upgrade(KeepSave s, string building)
    {
        if (UpgradeBlocked(s, building) != null) return false;
        var c = NextCost(s, building)!;
        s.Crowns -= c.Crowns;
        if (c.Mutagens > 0) s.AddMutagens(c.Color, -c.Mutagens);
        s.Buildings[building] = s.Level(building) + 1;
        if (building == WarRoom) RefillBoard(s);   // pano büyüdü
        return true;
    }

    public static string MutagenName(Mutagen c) => c switch { Mutagen.Red => "kırmızı", Mutagen.Green => "yeşil", _ => "mavi" };

    // ---------- Demirci ve atölye ----------

    /// <summary>Demirci seviyesine göre takılabilecek rün sayısı.</summary>
    public static int RuneSlots(KeepSave s) => Math.Min(RuneIds.MaxPerDeck, s.Level(Forge));

    /// <summary>Demirci seviyesine göre açık rünler (seviye başına iki).</summary>
    public static IReadOnlyList<string> AllowedRunes(KeepSave s) => RuneIds.All.Take(2 * Math.Min(3, s.Level(Forge))).ToList();

    /// <summary>Atölye seviyesine göre kurulabilen parçalar (maçta her biri bir kez).</summary>
    public static IReadOnlyList<EngineerPiece> AvailablePieces(KeepSave s) => Pieces.Take(Math.Min(Pieces.Count, s.Level(Workshop))).ToList();

    /// <summary>Kale kurallarına uymayan rünleri ayıklar (seviye düşükse fazlası ve açılmamış rünler düşer).</summary>
    public static List<CardRune> LimitRunes(KeepSave s, IEnumerable<CardRune> runes)
    {
        var allowed = AllowedRunes(s);
        return runes.Where(r => allowed.Contains(r.Rune)).Take(RuneSlots(s)).ToList();
    }

    /// <summary>Seçilen parçaları maçın arazisine çevirir (oyuncu 0 = kale sahibi).</summary>
    public static List<TerrainPiece> ToTerrain(KeepSave s, IEnumerable<(string PieceId, RowKind Row)> placed)
    {
        var available = AvailablePieces(s).ToDictionary(p => p.Id);
        var list = new List<TerrainPiece>();
        foreach (var (id, row) in placed.DistinctBy(p => p.PieceId))
            if (available.TryGetValue(id, out var p)) list.Add(new TerrainPiece(p.OnOpponent ? 1 : 0, row, p.Terrain));
        return list;
    }

    // ---------- Sözleşmeler ----------

    private sealed record Template(string Title, string Text, string Faction, string Battlefield, Mutagen Color, Difficulty MinDifficulty);

    private static readonly Template[] Templates =
    {
        new("White Orchard Griffin", "Köylüler sürülerini kaybediyor. Grifon tepelerden iniyor.", "monsters", "white_orchard", Mutagen.Red, Difficulty.Easy),
        new("Devil by the Well", "Terk edilmiş köyün kuyusunda bir öğle cadısı.", "monsters", "white_orchard", Mutagen.Blue, Difficulty.Easy),
        new("Bandits on Pontar Bridge", "Köprüyü tutan haydutlar geçiş parası istiyor.", "northern_realms", "pontar_bridge", Mutagen.Green, Difficulty.Easy),
        new("The Ladies of the Wood", "Crookback Bog'un üç kocakarısı pazarlık istemez.", "monsters", "crookback_bog", Mutagen.Red, Difficulty.Normal),
        new("Nilfgaardian Patrol", "Novigrad kapılarında imparatorluk devriyesi.", "nilfgaard", "novigrad_gates", Mutagen.Blue, Difficulty.Easy),
        new("Raiders of Ard Skellig", "Kıyıya çıkan yağmacılar köyleri yakıyor.", "skellige", "skellige_coast", Mutagen.Green, Difficulty.Normal),
        new("Ambush in the Vineyards", "Toussaint bağlarında Scoia'tael pususu.", "scoiatael", "toussaint_vineyards", Mutagen.Green, Difficulty.Normal),
        new("Wild Hunt Scouts", "Kaer Morhen harabelerinde Wild Hunt'ın öncüleri.", "monsters", "kaer_morhen_ruins", Mutagen.Blue, Difficulty.Normal),
        new("The Beast of Toussaint", "Bağlarda ceset bırakan bir canavar.", "monsters", "toussaint_vineyards", Mutagen.Red, Difficulty.Normal),
        new("Redanian Deserters", "Kaçak askerler ormanda kamp kurmuş.", "northern_realms", "crookback_bog", Mutagen.Green, Difficulty.Easy),
        new("Nilfgaard's Vanguard", "İmparatorluğun öncü birliği Pontar'ı geçiyor.", "nilfgaard", "pontar_bridge", Mutagen.Red, Difficulty.Normal),
        new("Elves of Brokilon's Edge", "Elfler yolu kesti; ancak kart masasında konuşurlar.", "scoiatael", "white_orchard", Mutagen.Blue, Difficulty.Easy),
    };

    /// <summary>Savaş odası seviyesine göre panodaki sözleşme sayısı.</summary>
    public static int BoardSize(KeepSave s) => 1 + Math.Clamp(s.Level(WarRoom), 1, 3);

    /// <summary>Panoyu eksik sözleşmelerle doldurur. Her sözleşme kaydın tohumu + sayaçtan üretilir (aynı kayıt = aynı pano).</summary>
    public static void RefillBoard(KeepSave s)
    {
        while (s.Board.Count < BoardSize(s))
            s.Board.Add(MakeContract(s, ++s.ContractCounter));
    }

    private static Contract MakeContract(KeepSave s, int n)
    {
        var rng = new Random(unchecked(s.Seed * 7919 + n * 104729));
        var war = s.Level(WarRoom);
        // Aynı şablon panoda iki kez olmasın
        var free = Templates.Where(t => s.Board.All(c => c.Title != t.Title)).ToList();
        var t = free[rng.Next(free.Count)];
        var maxDiff = war >= 2 ? Difficulty.Hard : Difficulty.Normal;
        var diff = (Difficulty)Math.Min((int)maxDiff, (int)t.MinDifficulty + rng.Next(2));
        string? curse = null;
        if (war >= 3 && rng.NextDouble() < 0.35)
        {
            var pool = Curses.All.Where(c => c.Id is not Curses.Mirror).ToList();
            curse = pool[rng.Next(pool.Count)].Id;
        }
        var leaders = CardCatalog.Default.Leaders(t.Faction).Where(l => l.Edition != "nextgen").ToList();
        var crowns = diff switch { Difficulty.Easy => 40, Difficulty.Normal => 70, _ => 110 };
        var mut = diff switch { Difficulty.Easy => 1, Difficulty.Normal => 2, _ => 3 };
        if (curse != null) { crowns = crowns * 3 / 2; mut++; }
        return new Contract(n, t.Title, t.Text, t.Faction, leaders[rng.Next(leaders.Count)].Id, diff, t.Battlefield, curse, crowns, t.Color, mut, rng.Next(1, int.MaxValue));
    }

    /// <summary>Kaybedince teselli Crown'ı.</summary>
    public const int LossCrowns = 10;

    /// <summary>
    /// Sözleşme bitti: kazanınca tam ödül ve sözleşme panodan kalkar (yenisi gelir); kaybedince teselli Crown'ı, sözleşme panoda kalır.
    /// Beraberlik kayıp sayılır.
    /// </summary>
    public static ContractResult Complete(KeepSave s, int contractId, bool won)
    {
        var c = s.Board.FirstOrDefault(x => x.Id == contractId) ?? throw new InvalidOperationException("Sözleşme panoda yok.");
        if (won)
        {
            s.Wins++;
            s.Crowns += c.RewardCrowns;
            s.AddMutagens(c.RewardColor, c.RewardMutagens);
            s.Board.Remove(c);
            RefillBoard(s);
            return new ContractResult(true, c.RewardCrowns, c.RewardColor, c.RewardMutagens,
                $"Sözleşme tamam: {c.Title}. +{c.RewardCrowns} Crown, +{c.RewardMutagens} {MutagenName(c.RewardColor)} mutajen.");
        }
        s.Losses++;
        s.Crowns += LossCrowns;
        return new ContractResult(false, LossCrowns, c.RewardColor, 0, $"{c.Title} başarısız. +{LossCrowns} Crown (yol harcırahı). Sözleşme panoda duruyor.");
    }
}
