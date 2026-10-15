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

/// <summary>İksir: Simya Odası'nda açılır, maç öncesi içilir. Toplam toksisite simya seviyesinin sınırını aşamaz.</summary>
public sealed record Potion(string Id, string Name, int Toxicity, int Level, string Text);

/// <summary>Müttefik: Müttefik Salonu'nda ikna edilir, maça elde fazladan kart olarak katılır (W3'teki Kaer Morhen savaşı gibi).</summary>
public sealed record Ally(string CardId, string Name, string Text, Cost Cost);

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
    /// <summary>İkna edilen müttefikler ve maça katılanlar (sıra önemli: yuva azsa baştakiler katılır).</summary>
    public List<string> Allies { get; set; } = new();
    public List<string> ActiveAllies { get; set; } = new();
    /// <summary>Maç öncesi içilecek iksirler (toksisite sınırına göre).</summary>
    public List<string> Potions { get; set; } = new();
    /// <summary>Son kuşatmadan beri başarılan sözleşmeler; <see cref="Keep.SiegeEvery"/> olunca Wild Hunt gelir.</summary>
    public int SiegeProgress { get; set; }
    /// <summary>Süren kuşatmanın dalgası (0 = kuşatma yok).</summary>
    public int SiegeWave { get; set; }
    public int SiegesWon { get; set; }
    public int SiegesLost { get; set; }

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
        new Building(Alchemy, "Simya Odası", "Maç öncesi iksirler; toplam toksisite sınırı aşılamaz.", Mutagen.Green,
            new[] { new Cost(100), new Cost(220, 2, Mutagen.Green), new Cost(400, 4, Mutagen.Green) },
            new[] { "Toksisite 3: Kedi, Kırlangıç.", "Toksisite 5: + Gök Gürültüsü, Puhu.", "Toksisite 7: + Kara Kan." }),
        new Building(Barracks, "Kışla", "Askerlerin talimi: maç kurallarında küçük üstünlükler.", Mutagen.Red,
            new[] { new Cost(120), new Cost(260, 2, Mutagen.Red), new Cost(450, 4, Mutagen.Red) },
            new[] { "3 kart değiştirebilirsin (2 yerine).", "+ Raundu kimin başlatacağını sen seçersin.", "+ Maça 11 kartla başlarsın." }),
        new Building(AlliesHall, "Müttefik Salonu", "Dostlarını Kaer Morhen'e çağır. Maça elinde fazladan kart olarak katılırlar.", Mutagen.Blue,
            new[] { new Cost(120, 1, Mutagen.Blue), new Cost(260, 3, Mutagen.Blue), new Cost(450, 5, Mutagen.Blue) },
            new[] { "1 müttefik maça katılır.", "2 müttefik maça katılır.", "3 müttefik maça katılır." }),
    };

    public static Building Get(string id) => Buildings.First(b => b.Id == id);

    public static readonly IReadOnlyList<EngineerPiece> Pieces = new[]
    {
        new EngineerPiece("tower", "Gözcü kulesi", Terrain.Hill, false, "Kendi satırın Tepe olur (+1)."),
        new EngineerPiece("barricade", "Barikat", Terrain.Bridge, true, "Rakibin satırı Köprü olur (en fazla 4 birim)."),
        new EngineerPiece("moat", "Hendek", Terrain.Swamp, true, "Rakibin satırı Bataklık olur (−1)."),
    };

    public static readonly IReadOnlyList<Potion> AllPotions = new[]
    {
        new Potion(PotionIds.Cat, "Kedi", 2, 1, "1. raund başında rakibin elinden rastgele 3 kartı görürsün."),
        new Potion(PotionIds.Swallow, "Kırlangıç", 2, 1, "Kaybettiğin ya da berabere kaldığın raunddan sonra 1 kart çekersin."),
        new Potion(PotionIds.Thunderbolt, "Gök Gürültüsü", 3, 2, "1. raundda yakın dövüş birimlerin +1."),
        new Potion(PotionIds.TawnyOwl, "Puhu", 2, 2, "Hava birimlerini 1'e değil, yarı güce indirir."),
        new Potion(PotionIds.BlackBlood, "Kara Kan", 3, 3, "Rakibin oynadığı ilk casus ona kart çektirmez."),
    };

    public static Potion GetPotion(string id) => AllPotions.First(p => p.Id == id);

    /// <summary>Simya seviyesine göre toksisite sınırı (0 = simya yok).</summary>
    public static int ToxicityLimit(KeepSave s) => s.Level(Alchemy) switch { 0 => 0, 1 => 3, 2 => 5, _ => 7 };

    public static IReadOnlyList<Potion> UnlockedPotions(KeepSave s) => AllPotions.Where(p => p.Level <= s.Level(Alchemy)).ToList();

    /// <summary>Seçili iksirlerden geçerli olanlar (açılmış, sınırı aşmayan; sıra korunur).</summary>
    public static List<string> MatchPotions(KeepSave s)
    {
        var limit = ToxicityLimit(s);
        var list = new List<string>();
        var tox = 0;
        foreach (var p in s.Potions.Distinct().Select(GetPotion).Where(p => p.Level <= s.Level(Alchemy)))
        {
            if (tox + p.Toxicity > limit) continue;
            tox += p.Toxicity;
            list.Add(p.Id);
        }
        return list;
    }

    public static int Toxicity(IEnumerable<string> potions) => potions.Sum(id => GetPotion(id).Toxicity);

    /// <summary>İksiri seçer/bırakır; sınır aşılacaksa ya da açılmamışsa false.</summary>
    public static bool TogglePotion(KeepSave s, string id)
    {
        if (s.Potions.Remove(id)) return true;
        var p = GetPotion(id);
        if (p.Level > s.Level(Alchemy) || Toxicity(MatchPotions(s)) + p.Toxicity > ToxicityLimit(s)) return false;
        s.Potions.Add(id);
        return true;
    }

    /// <summary>Kışla ve simyanın maça etkisi.</summary>
    public static Perks? MatchPerks(KeepSave s)
    {
        var b = s.Level(Barracks);
        var potions = MatchPotions(s);
        if (b == 0 && potions.Count == 0) return null;
        return new Perks(b >= 3 ? 1 : 0, b >= 1 ? 1 : 0, b >= 2, potions.Count > 0 ? potions : null);
    }

    public static readonly IReadOnlyList<Ally> AllAllies = new[]
    {
        new Ally("dandelion", "Dandelion", "Ozan. Satırına Komutan Boynuzu çalar.", new Cost(60)),
        new Ally("zoltan_chivay", "Zoltan Chivay", "Cüce dost; baltası ve sözü sağlamdır.", new Cost(90, 1, Mutagen.Green)),
        new Ally("emiel_regis_rohellec_terzieff", "Regis", "Berber-cerrah; geceleri pek uyumaz.", new Cost(120, 2, Mutagen.Red)),
        new Ally("vesemir", "Vesemir", "Kaer Morhen'in en yaşlı cadısı.", new Cost(150, 2, Mutagen.Green)),
        new Ally("triss_merigold", "Triss Merigold", "Ateş büyüsünde ustadır (kahraman).", new Cost(220, 3, Mutagen.Blue)),
        new Ally("yennefer_of_vengerberg", "Yennefer", "Vengerberg'li büyücü; ölüyü geri çağırır (kahraman, Medic).", new Cost(260, 3, Mutagen.Red)),
    };

    public static Ally GetAlly(string cardId) => AllAllies.First(a => a.CardId == cardId);

    /// <summary>Maça katılabilecek müttefik sayısı (Müttefik Salonu seviyesi).</summary>
    public static int AllySlots(KeepSave s) => Math.Min(AllyRules.MaxPerMatch, s.Level(AlliesHall));

    public static string? RecruitBlocked(KeepSave s, string cardId)
    {
        if (s.Level(AlliesHall) == 0) return "Önce Müttefik Salonu'nu onar.";
        if (s.Allies.Contains(cardId)) return "Zaten kalede.";
        var c = GetAlly(cardId).Cost;
        if (s.Crowns < c.Crowns) return $"{c.Crowns - s.Crowns} Crown eksik.";
        if (c.Mutagens > 0 && s.Mutagens(c.Color) < c.Mutagens) return $"{c.Mutagens - s.Mutagens(c.Color)} {MutagenName(c.Color)} mutajen eksik.";
        return null;
    }

    /// <summary>Müttefiki ikna eder; yuva boşsa hemen maça katılanlara eklenir.</summary>
    public static bool Recruit(KeepSave s, string cardId)
    {
        if (RecruitBlocked(s, cardId) != null) return false;
        var c = GetAlly(cardId).Cost;
        s.Crowns -= c.Crowns;
        if (c.Mutagens > 0) s.AddMutagens(c.Color, -c.Mutagens);
        s.Allies.Add(cardId);
        if (s.ActiveAllies.Count < AllySlots(s)) s.ActiveAllies.Add(cardId);
        return true;
    }

    /// <summary>Müttefikin maça katılıp katılmayacağını değiştirir; yuvalar doluysa katılamaz.</summary>
    public static bool ToggleActive(KeepSave s, string cardId)
    {
        if (!s.Allies.Contains(cardId)) return false;
        if (s.ActiveAllies.Remove(cardId)) return true;
        if (s.ActiveAllies.Count >= AllySlots(s)) return false;
        s.ActiveAllies.Add(cardId);
        return true;
    }

    /// <summary>Bu maça katılacak müttefikler.</summary>
    public static List<string> MatchAllies(KeepSave s) => s.ActiveAllies.Where(s.Allies.Contains).Take(AllySlots(s)).ToList();

    // ---------- Wild Hunt kuşatması (TASARIM.md §6.2) ----------

    /// <summary>Bu kadar sözleşme başarılınca Wild Hunt kaleye saldırır.</summary>
    public const int SiegeEvery = 5;
    public const int SiegeWaves = 3;

    public static bool SiegeReady(KeepSave s) => s.SiegeWave == 0 && s.SiegeProgress >= SiegeEvery;

    public static void StartSiege(KeepSave s)
    {
        if (SiegeReady(s)) s.SiegeWave = 1;
    }

    /// <summary>Sürmekte olan kuşatmanın dalgası, sözleşme olarak (kimliği eksi dalga numarası). Kuşatma yoksa null.</summary>
    public static Contract? SiegeContract(KeepSave s)
    {
        if (s.SiegeWave == 0) return null;
        var w = s.SiegeWave;
        var (leader, diff, title, text) = w switch
        {
            1 => ("eredin_commander_of_the_red_riders", Difficulty.Normal, "Kızıl Süvariler", "Wild Hunt'ın öncüleri surlara dayandı."),
            2 => ("eredin_destroyer_of_worlds", Difficulty.Hard, "Imlerith'in Saldırısı", "Kapıya koçbaşı vuruyorlar. Hendekler tutmalı."),
            _ => ("eredin_king_of_the_wild_hunt", Difficulty.Hard, "Wild Hunt'ın Kralı", "Eredin bizzat geldi; ardından Beyaz Ayaz yürüyor.")
        };
        var color = (Mutagen)((w - 1) % 3);
        return new Contract(-w, $"Kuşatma {w}/{SiegeWaves}: {title}", text, "monsters", leader, diff, "kaer_morhen_ruins",
            w == SiegeWaves ? Curses.EndlessWinter : null, 80 + 40 * w, color, 1 + w, unchecked(s.Seed * 31 + s.SiegesWon * 7 + w));
    }

    private static ContractResult CompleteSiege(KeepSave s, Contract c, bool won)
    {
        if (!won)
        {
            s.SiegesLost++;
            s.SiegeWave = 0;
            s.SiegeProgress = 0;
            s.Crowns += LossCrowns;
            return new ContractResult(false, LossCrowns, c.RewardColor, 0,
                $"{c.Title}: surlar tutmadı. Wild Hunt yağmalayıp çekildi; kale ayakta. +{LossCrowns} Crown. Bir sonraki kuşatmaya hazırlan.");
        }
        s.Crowns += c.RewardCrowns;
        s.AddMutagens(c.RewardColor, c.RewardMutagens);
        if (s.SiegeWave < SiegeWaves)
        {
            s.SiegeWave++;
            return new ContractResult(true, c.RewardCrowns, c.RewardColor, c.RewardMutagens,
                $"{c.Title} püskürtüldü! +{c.RewardCrowns} Crown, +{c.RewardMutagens} {MutagenName(c.RewardColor)} mutajen. Sıradaki dalga geliyor.");
        }
        s.SiegesWon++;
        s.SiegeWave = 0;
        s.SiegeProgress = 0;
        const int bonus = 200;
        s.Crowns += bonus;
        foreach (var m in new[] { Mutagen.Red, Mutagen.Green, Mutagen.Blue }) s.AddMutagens(m, 2);
        return new ContractResult(true, c.RewardCrowns + bonus, c.RewardColor, c.RewardMutagens,
            $"Kuşatma püskürtüldü! Kaer Morhen ayakta. +{c.RewardCrowns + bonus} Crown, her renkten +2 mutajen (ve +{c.RewardMutagens} {MutagenName(c.RewardColor)}).");
    }

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
        if (contractId < 0 && SiegeContract(s) is { } siege && siege.Id == contractId) return CompleteSiege(s, siege, won);
        var c = s.Board.FirstOrDefault(x => x.Id == contractId) ?? throw new InvalidOperationException("Sözleşme panoda yok.");
        if (won)
        {
            s.Wins++;
            s.SiegeProgress++;
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
