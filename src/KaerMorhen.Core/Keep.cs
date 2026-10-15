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

/// <summary>
/// Sözleşme panosundaki bir iş (ya da kuşatma dalgası): rakip, savaş alanı, lanetler ve ödül. OppPerks: rakibin avantajları (kuşatma boss'ları).
/// Faction "mirror": Gaunter O'Dimm — rakip oyuncunun destesiyle oynar (Ayna laneti).
/// </summary>
public sealed record Contract(int Id, string Title, string Text, string Faction, string LeaderId, Difficulty Difficulty, string Battlefield,
    IReadOnlyList<string>? Curses, int RewardCrowns, Mutagen RewardColor, int RewardMutagens, int Seed, Perks? OppPerks = null);

/// <summary>Kuşatmanın bir dalgası.</summary>
public sealed record SiegeWave(string LeaderId, Difficulty Difficulty, string Title, string Text, IReadOnlyList<string>? Curses = null, Perks? Perks = null);

/// <summary>Kronik'in bir bölümü: Kaer Morhen'e saldıran düşman ve dalgaları.</summary>
public sealed record Antagonist(string Name, string Faction, string Intro, IReadOnlyList<SiegeWave> Waves);

/// <summary>Biten bir seferin özeti (kuşatma kaybedilince kale düşer, sefer biter).</summary>
public sealed record RunRecord(int Run, int Chapter, int Contracts, int SiegesWon, string FallenTo);

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
    /// <summary>Kronik bölümü (0'dan; <see cref="Keep.Chronicle"/> sayısı ve sonrası: sonsuz Beyaz Ayaz).</summary>
    public int Chapter { get; set; }
    /// <summary>Kaçıncı sefer (kale her düştüğünde yeni sefer başlar) ve biten seferler. Sefer sıfırlanınca bunlar korunur.</summary>
    public int Run { get; set; } = 1;
    public List<RunRecord> Runs { get; set; } = new();
    public int BestChapter => Runs.Select(r => r.Chapter).Append(Chapter).Max();

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

    /// <summary>Düşman kapıda (ya da kuşatma sürüyor): sözleşmeye çıkılamaz, önce kuşatma karşılanır.</summary>
    public static bool ContractsLocked(KeepSave s) => SiegeReady(s) || s.SiegeWave > 0;

    private static Perks Boss(int extraHand = 1, params string[] potions) => new(extraHand, 0, false, potions.Length > 0 ? potions : null);

    /// <summary>Kronik: Kaer Morhen'e sırayla saldıran düşmanlar. Her biri bir öncekinden sert; sonuncusundan sonra sonsuz Beyaz Ayaz.</summary>
    public static readonly IReadOnlyList<Antagonist> Chronicle = new[]
    {
        new Antagonist("Wild Hunt'ın Öncüleri", "monsters", "Kızıl süvariler dağ yolunda görüldü. Kaer Morhen'in surları yıllardır bu kadar sessiz değildi.", new[]
        {
            new SiegeWave("eredin_commander_of_the_red_riders", Difficulty.Normal, "Kızıl Süvariler", "Wild Hunt'ın öncüleri surlara dayandı."),
            new SiegeWave("eredin_bringer_of_death", Difficulty.Normal, "Caranthir'in Büyüsü", "Kapıların önünde buz yükseliyor."),
            new SiegeWave("eredin_destroyer_of_worlds", Difficulty.Hard, "Imlerith'in Saldırısı", "Koçbaşı kapıya vuruyor. Hendekler tutmalı."),
        }),
        new Antagonist("Radovid'in Cadı Avcıları", "northern_realms", "Redanya kralı büyücülere ve cadılara savaş açtı. Sıradaki hedef Kaer Morhen.", new[]
        {
            new SiegeWave("foltest_the_siegemaster", Difficulty.Normal, "Kuşatma Makineleri", "Mancınıklar vadiye kuruluyor."),
            new SiegeWave("foltest_lord_commander_of_the_north", Difficulty.Hard, "Kızıl Bayraklar", "Cadı avcıları surlara tırmanıyor."),
            new SiegeWave("foltest_king_of_temeria", Difficulty.Hard, "Yakma Fermanı", "Büyü yasak: kahramanların sesi kısıldı.", new[] { Curses.NoHeroes }, Boss()),
        }),
        new Antagonist("Nilfgaard İstilası", "nilfgaard", "İmparatorluğun ordusu kuzeye yürüyor. Emhyr, Kaer Morhen'in teslim olmasını istiyor.", new[]
        {
            new SiegeWave("emhyr_var_emreis_his_imperial_majesty", Difficulty.Hard, "İmparatorluk Öncüsü", "Kara zırhlı birlikler vadiyi doldurdu."),
            new SiegeWave("emhyr_var_emreis_the_relentless", Difficulty.Hard, "Impera Tugayı", "Her kazanılan savaşın bir bedeli var.", new[] { Curses.BloodPrice }),
            new SiegeWave("emhyr_var_emreis_invader_of_the_north", Difficulty.Hard, "Kuzeyin İstilacısı", "Emhyr bizzat sahada.", null, Boss(1, PotionIds.Thunderbolt)),
        }),
        new Antagonist("Gaunter O'Dimm'in Pazarlığı", "mirror", "Aynalı Adam kapıyı çaldı. Seninle kendi silahlarınla oynamak istiyor.", new[]
        {
            new SiegeWave("", Difficulty.Hard, "Aynadaki Yüz", "Karşında kendi destenle oynayan biri var.", new[] { Curses.Mirror }),
            new SiegeWave("", Difficulty.Hard, "Kısa Mum", "Mum kısa, oyun uzun değil.", new[] { Curses.Mirror, Curses.ShortCandle }),
            new SiegeWave("", Difficulty.Hard, "Ruhun Bedeli", "O'Dimm her zaferin bedelini tahsil eder.", new[] { Curses.Mirror, Curses.BloodPrice }, Boss()),
        }),
        new Antagonist("Wild Hunt'ın Kralı", "monsters", "Beyaz Ayaz geliyor. Eredin bizzat Kaer Morhen'e yürüyor: son savaş.", new[]
        {
            new SiegeWave("eredin_breacc_glas_the_treacherous", Difficulty.Hard, "Naglfar Demir Attı", "Hayalet gemiden askerler iniyor.", new[] { Curses.EndlessWinter }),
            new SiegeWave("eredin_destroyer_of_worlds", Difficulty.Hard, "Imlerith ve Caranthir", "Wild Hunt'ın generalleri birlikte saldırıyor.", new[] { Curses.EndlessWinter }, Boss()),
            new SiegeWave("eredin_king_of_the_wild_hunt", Difficulty.Hard, "Eredin Bréacc Glas", "Wild Hunt'ın Kralı surların önünde.", new[] { Curses.EndlessWinter }, Boss(1, PotionIds.Thunderbolt, PotionIds.BlackBlood)),
        }),
    };

    /// <summary>Kronik bitti mi (sonsuz Beyaz Ayaz: düşmanlar sırayla, daha sert döner).</summary>
    public static bool Endless(KeepSave s) => s.Chapter >= Chronicle.Count;

    /// <summary>Sıradaki ya da süren kuşatmanın düşmanı.</summary>
    public static Antagonist CurrentAntagonist(KeepSave s) => Chronicle[s.Chapter % Chronicle.Count];

    public static void StartSiege(KeepSave s)
    {
        if (SiegeReady(s)) s.SiegeWave = 1;
    }

    /// <summary>Sürmekte olan kuşatmanın dalgası, sözleşme olarak (kimliği eksi dalga numarası). Kuşatma yoksa null.</summary>
    public static Contract? SiegeContract(KeepSave s)
    {
        if (s.SiegeWave == 0) return null;
        var w = s.SiegeWave;
        var foe = CurrentAntagonist(s);
        var wave = foe.Waves[Math.Min(w, foe.Waves.Count) - 1];
        var perks = wave.Perks;
        var diff = wave.Difficulty;
        if (Endless(s))
        {
            // Sonsuz Beyaz Ayaz: hepsi Zor, her dalgada rakip fazladan kartla ve iksirle gelir
            diff = Difficulty.Hard;
            var pots = (perks?.Potions ?? Array.Empty<string>()).Append(PotionIds.Thunderbolt).Distinct().ToList();
            perks = new Perks(1, 0, false, pots);
        }
        var color = (Mutagen)((w - 1 + s.Chapter) % 3);
        var leader = wave.LeaderId;
        if (foe.Faction != "mirror" && leader.Length == 0) leader = CardCatalog.Default.Leaders(foe.Faction).First().Id;
        var crowns = 80 + 40 * w + 20 * Math.Min(s.Chapter, 10);
        return new Contract(-w, $"{foe.Name} {w}/{SiegeWaves}: {wave.Title}", wave.Text, foe.Faction, leader, diff, "kaer_morhen_ruins",
            wave.Curses, crowns, color, 1 + w, unchecked(s.Seed * 31 + s.Chapter * 101 + s.SiegesLost * 7 + w), perks);
    }

    private static ContractResult CompleteSiege(KeepSave s, Contract c, bool won)
    {
        var foe = CurrentAntagonist(s);
        if (!won)
        {
            // Kale düştü: sefer biter, her şey baştan (roguelike). Sefer geçmişi ve deste tercihi kalır.
            var rec = new RunRecord(s.Run, s.Chapter, s.Wins, s.SiegesWon, foe.Name);
            Fall(s);
            return new ContractResult(false, 0, c.RewardColor, 0,
                $"Kaer Morhen düştü! {foe.Name} surları aştı. {rec.Run}. sefer bitti: Kronik {rec.Chapter + 1}. bölüm, {rec.Contracts} sözleşme, {rec.SiegesWon} kuşatma püskürtüldü. "
                + $"{s.Run}. sefer başlıyor: kale yeniden harabe.");
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
        s.Chapter++;
        var bonus = 200 + 50 * Math.Min(s.Chapter, 10);
        s.Crowns += bonus;
        foreach (var m in new[] { Mutagen.Red, Mutagen.Green, Mutagen.Blue }) s.AddMutagens(m, 2);
        var next = s.Chapter == Chronicle.Count
            ? "Kronik tamamlandı: Kaer Morhen Wild Hunt'a karşı ayakta! Ama Beyaz Ayaz dinmez: düşmanlar daha sert dönecek."
            : $"Sıradaki tehdit: {CurrentAntagonist(s).Name}.";
        return new ContractResult(true, c.RewardCrowns + bonus, c.RewardColor, c.RewardMutagens,
            $"{foe.Name} yenildi! +{c.RewardCrowns + bonus} Crown, her renkten +2 mutajen. {next}");
    }

    /// <summary>Kale düştü: seferi kaydeder ve yeni sefer başlatır (yeni tohum: farklı pano).</summary>
    public static void Fall(KeepSave s)
    {
        s.Runs.Add(new RunRecord(s.Run, s.Chapter, s.Wins, s.SiegesWon, CurrentAntagonist(s).Name));
        var fresh = NewSave(unchecked(s.Seed * 1103515245 + 12345 + s.Run) & int.MaxValue);
        s.Run++;
        s.Seed = fresh.Seed;
        s.Crowns = fresh.Crowns;
        s.Red = s.Green = s.Blue = 0;
        s.Buildings = fresh.Buildings;
        s.Board = fresh.Board;
        s.ContractCounter = fresh.ContractCounter;
        s.Wins = s.Losses = 0;
        s.SiegeProgress = s.SiegeWave = s.SiegesWon = s.SiegesLost = 0;
        s.Chapter = 0;
        s.Allies.Clear();
        s.ActiveAllies.Clear();
        s.Potions.Clear();
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
        // Zorluk: şablonun tabanı, çoğunlukla bir üstü (%70); Kronik ilerledikçe taban yükselir. Zor için Savaş Odası 2. seviye.
        var maxDiff = war >= 2 ? Difficulty.Hard : Difficulty.Normal;
        var floor = Math.Min(2, s.Chapter);
        var tier = Math.Max(floor, (int)t.MinDifficulty + (rng.NextDouble() < 0.7 ? 1 : 0));
        var diff = (Difficulty)Math.Min((int)maxDiff, tier);
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
        return new Contract(n, t.Title, t.Text, t.Faction, leaders[rng.Next(leaders.Count)].Id, diff, t.Battlefield, curse == null ? null : new[] { curse },
            crowns, t.Color, mut, rng.Next(1, int.MaxValue));
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
