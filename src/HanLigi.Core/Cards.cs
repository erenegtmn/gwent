namespace HanLigi;

public enum Row { Close, Ranged, Siege }

public enum Tag { Monster, Soldier, Mage, Bandit, Noble, Dwarf }

/// <summary>Birim yetenekleri (YENI_MOD.md §6.3).</summary>
public enum Ability { None, Bond, Growth, Morale, Spy, Bard, Treasure, Ambush, Revenge, Zeal, Summon, Medic, DeathEcho, Mirror }

/// <summary>Özel kartlar: gücü yok, slot tüketir, etikete sayılmaz.</summary>
public enum Special { None, Frost, Fog, Rain, Scorch, Horn, Shield }

public enum Hero { None, Geralt, Ciri, Yennefer, Triss, Jaskier, Vesemir, Zoltan, Eredin }

/// <summary>Witcher işaretleri (v0: Axii yok, §7.13).</summary>
public enum Sign { Aard, Igni, Quen, Yrden }

/// <summary>Simya kartları (v0'ın beş kartı).</summary>
public enum Potion { Thunderbolt, Swallow, BlackBlood, DancingStar, NorthernWind }

public enum School { Wolf, Cat, Griffin, Bear, Viper, Manticore }

/// <summary>Kart tanımı. <see cref="Tier"/> 1-5; kahramanlar 6.</summary>
public sealed record CardDef(string Id, string Name, int Tier, int Power, Row Row, bool Agile, Tag[] Tags, Ability Ability, Special Special, Hero Hero, string Art)
{
    public bool IsUnit => Special == Special.None;
    public bool IsHero => Hero != Hero.None;
    public int Price => IsHero ? Tuning.HeroPrice : Tier;
    public bool Has(Tag t) => Array.IndexOf(Tags, t) >= 0;
}

/// <summary>v0 kart seti: tier başına 7/7/8/7/6 birim + özel kartlar + 8 kahraman. Resimler klasik Gwent kartlarından ödünç alınır.</summary>
public static class CardSet
{
    private static readonly List<CardDef> _all = new();
    private static readonly Dictionary<string, CardDef> _byId = new();

    public static IReadOnlyList<CardDef> All => _all;
    public static CardDef Get(string id) => _byId[id];

    private static void U(string id, string name, int tier, int power, Row row, Tag tag, Ability ab = Ability.None, bool agile = false, Tag? tag2 = null)
        => Add(new CardDef(id, name, tier, power, row, agile, tag2 is { } t2 ? new[] { tag, t2 } : new[] { tag }, ab, Special.None, Hero.None, id));

    private static void S(string id, string name, int tier, Special sp, string art)
        => Add(new CardDef(id, name, tier, 0, Row.Close, false, Array.Empty<Tag>(), Ability.None, sp, Hero.None, art));

    private static void H(string id, string name, int power, Row row, Hero hero, string art, Ability ab = Ability.None)
        => Add(new CardDef(id, name, 6, power, row, false, Array.Empty<Tag>(), ab, Special.None, hero, art));

    private static void Add(CardDef d) { _all.Add(d); _byId[d.Id] = d; }

    static CardSet()
    {
        // ---- Tier 1 (güç 1-3) ----
        U("nekker", "Nekker", 1, 2, Row.Close, Tag.Monster, Ability.Summon);
        U("ghoul", "Gulyabani", 1, 1, Row.Close, Tag.Monster, Ability.Growth);
        U("poor_fucking_infantry", "Zavallı Piyade", 1, 2, Row.Close, Tag.Soldier, Ability.Bond);
        U("kaedweni_siege_expert", "Kaedwen Kuşatma Uzmanı", 1, 1, Row.Siege, Tag.Soldier, Ability.Morale);
        U("elven_skirmisher", "Elf Çatışmacı", 1, 2, Row.Ranged, Tag.Bandit, Ability.Ambush);
        U("dwarven_skirmisher", "Cüce Çatışmacı", 1, 3, Row.Close, Tag.Dwarf);
        U("sweers", "Tahsildar Sweers", 1, 1, Row.Ranged, Tag.Noble, Ability.Treasure);
        // ---- Tier 2 (güç 3-4) ----
        U("arachas", "Arachas", 2, 3, Row.Close, Tag.Monster, Ability.Summon);
        U("blue_stripes_commando", "Mavi Çizgili Komando", 2, 4, Row.Close, Tag.Soldier, Ability.Bond);
        U("thaler", "Thaler", 2, 1, Row.Siege, Tag.Noble, Ability.Spy);
        U("vrihedd_brigade_recruit", "Vrihedd Acemisi", 2, 4, Row.Ranged, Tag.Bandit, Ability.Ambush, agile: true);
        U("mahakaman_defender", "Mahakam Savunucusu", 2, 4, Row.Close, Tag.Dwarf, Ability.Revenge);
        U("sabrina_glevissig", "Sabrina Glevissig", 2, 3, Row.Ranged, Tag.Mage, Ability.Bard);
        U("dun_banner_medic", "Dun Sancağı Şifacısı", 2, 3, Row.Siege, Tag.Soldier, Ability.Medic);
        // ---- Tier 3 (güç 5-6) ----
        U("griffin", "Griffin", 3, 5, Row.Close, Tag.Monster, Ability.Zeal);
        U("crinfrid_reavers_dragon_hunter", "Crinfrid Ejder Avcısı", 3, 5, Row.Ranged, Tag.Soldier, Ability.Bond);
        U("dethmold", "Dethmold", 3, 6, Row.Ranged, Tag.Mage);
        U("vrihedd_brigade_veteran", "Vrihedd Kıdemlisi", 3, 5, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
        U("barclay_els", "Barclay Els", 3, 6, Row.Close, Tag.Dwarf, agile: true);
        U("vattier_de_rideaux", "Vattier de Rideaux", 3, 3, Row.Close, Tag.Noble, Ability.Spy);
        U("trebuchet", "Mancınık", 3, 6, Row.Siege, Tag.Soldier);
        U("grave_hag", "Mezar Cadısı", 3, 5, Row.Ranged, Tag.Monster, Ability.DeathEcho);
        // ---- Tier 4 (güç 7-9) ----
        U("fiend", "İblis", 4, 8, Row.Close, Tag.Monster);
        U("catapult", "Katapult", 4, 8, Row.Siege, Tag.Soldier, Ability.Bond);
        U("sheldon_skaggs", "Sheldon Skaggs", 4, 7, Row.Ranged, Tag.Dwarf, Ability.Revenge);
        U("sile_de_tansarville", "Síle de Tansarville", 4, 7, Row.Ranged, Tag.Mage, Ability.Zeal);
        U("assire_var_anahid", "Assire var Anahid", 4, 7, Row.Ranged, Tag.Mage, Ability.Morale, tag2: Tag.Noble);
        U("dol_blathanna_scout", "Dol Blathanna İzcisi", 4, 7, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
        U("stefan_skellen", "Stefan Skellen", 4, 9, Row.Close, Tag.Noble);
        // ---- Tier 5 (güç 10-12) ----
        U("earth_elemental", "Toprak Elementali", 5, 10, Row.Siege, Tag.Monster, Ability.DeathEcho);
        U("heavy_zerrikanian_fire_scorpion", "Ağır Ateş Akrebi", 5, 11, Row.Siege, Tag.Soldier);
        U("draug", "Draug", 5, 12, Row.Close, Tag.Monster);
        U("yarpen_zigrin", "Yarpen Zigrin", 5, 10, Row.Close, Tag.Dwarf, Ability.Bond);
        U("tibor_eggebracht", "Tibor Eggebracht", 5, 11, Row.Ranged, Tag.Noble);
        U("fringilla_vigo", "Fringilla Vigo", 5, 10, Row.Ranged, Tag.Mage, Ability.Mirror);
        // ---- Özel kartlar ----
        S("frost", "Dondurucu Ayaz", 3, Special.Frost, "biting_frost");
        S("fog", "Geçit Vermez Sis", 3, Special.Fog, "impenetrable_fog");
        S("rain", "Sağanak Yağmur", 3, Special.Rain, "torrential_rain");
        S("horn", "Komutan Boynuzu", 3, Special.Horn, "commanders_horn");
        S("shield", "Kalkan Alayı", 4, Special.Shield, "decoy");
        S("scorch", "Kavurma", 4, Special.Scorch, "scorch");
        // ---- Kahramanlar (18-22; evolve olmaz) ----
        H("geralt", "Rivyalı Geralt", 22, Row.Close, Hero.Geralt, "geralt_of_rivia");
        H("ciri", "Cirilla", 20, Row.Close, Hero.Ciri, "cirilla_fiona_elen_riannon", Ability.Ambush);
        H("yennefer", "Vengerbergli Yennefer", 19, Row.Ranged, Hero.Yennefer, "yennefer_of_vengerberg");
        H("triss", "Triss Merigold", 18, Row.Ranged, Hero.Triss, "triss_merigold");
        H("jaskier", "Jaskier", 18, Row.Close, Hero.Jaskier, "dandelion");
        H("vesemir", "Vesemir", 20, Row.Close, Hero.Vesemir, "vesemir");
        H("zoltan", "Zoltan Chivay", 19, Row.Close, Hero.Zoltan, "zoltan_chivay");
        H("eredin", "Eredin", 21, Row.Close, Hero.Eredin, "imlerith");
    }
}

/// <summary>Ayar sayıları (YENI_MOD.md §15; 7. tur model ölçümünden).</summary>
public static class Tuning
{
    public const int StartHp = 100;
    public const int StartGold = 5;
    public const int Income = 4;
    public const int WinGold = 1;
    public const int InterestPer = 10;
    public const int InterestCap = 2;

    /// <summary>
    /// Seri altını (round başı). Yenilgi serisi geri dönüş ve "bilerek kaybedip ekonomi kur" yolunu açar; galibiyet serisi küçük
    /// tutulur (önde olanı kartopu gibi büyütmesin): yenilgi 2 → +1, 3-4 → +2, 5+ → +3; galibiyet 3-4 → +1, 5+ → +2.
    /// </summary>
    public static int StreakGold(int streak) => streak switch
    {
        <= -5 => 3, <= -3 => 2, -2 => 1, >= 5 => 2, >= 3 => 1, _ => 0
    };
    public const int XpCost = 4;
    public const int XpGain = 4;
    public const int XpPerRound = 2;
    public const int RerollCost = 1;
    public const int HeroPrice = 7;
    public const int BenchSize = 8;
    public const int MaxLevel = 8;
    public const int ShopSize = 5;
    public const int StartFocus = 3;
    public const int RageCap = 2;
    public const double DamageMult = 2.25;
    public const double PveEarly = 0.5;
    public const double PveLater = 0.8;
    public const int PotionsFromRound = 4;

    /// <summary>L2…L8 kümülatif XP eşikleri.</summary>
    public static readonly int[] XpThresholds = { 2, 6, 12, 22, 35, 52, 72 };
    /// <summary>Her kartın havuzdaki kopya sayısı (tier 1-5).</summary>
    public static readonly int[] Copies = { 22, 18, 15, 12, 9 };
    /// <summary>Seviyeye göre market tier olasılıkları (%).</summary>
    public static readonly int[][] ShopOdds =
    {
        new[] { 100, 0, 0, 0, 0 }, new[] { 85, 15, 0, 0, 0 }, new[] { 65, 30, 5, 0, 0 }, new[] { 45, 38, 15, 2, 0 },
        new[] { 30, 38, 25, 7, 0 }, new[] { 20, 30, 32, 15, 3 }, new[] { 15, 22, 33, 23, 7 }, new[] { 10, 15, 30, 30, 15 }
    };
    /// <summary>Seviyeye göre yuva başına kahraman olasılığı.</summary>
    public static readonly double[] HeroOdds = { 0, 0, 0, 0, 0.0025, 0.005, 0.01, 0.015 };
    public static readonly double[] SpyRate = { 0.20, 0.35, 0.50 };

    public static int Slots(int level) => Math.Min(9, level + 2);

    /// <summary>Yıldıza göre güç: 2★ ×1,5; 3★ T1 ×6, T2 ×4,5, T3+ ×3,5.</summary>
    public static int Power(CardDef d, int star)
    {
        if (d.IsHero || star <= 1) return d.Power;
        var k = star == 2 ? 1.5 : d.Tier == 1 ? 6 : d.Tier == 2 ? 4.5 : 3.5;
        return Round(d.Power * k);
    }

    /// <summary>En yakın tamsayı, yarım yukarı (§15.1 R2).</summary>
    public static int Round(double v) => (int)Math.Floor(v + 0.5);

    public static int SignCost(Sign s) => s == Sign.Igni ? 2 : 1;

    public static int SellValue(CardDef d, int star) => d.IsHero ? HeroPrice : star == 1 ? d.Price : (star == 2 ? 3 : 9) * d.Price - 1;
}

public static class Texts
{
    public static string Name(Row r) => r switch { Row.Close => "Yakın", Row.Ranged => "Menzilli", _ => "Kuşatma" };
    public static string Name(Tag t) => t switch { Tag.Monster => "Canavar", Tag.Soldier => "Asker", Tag.Mage => "Büyücü", Tag.Bandit => "Haydut", Tag.Noble => "Soylu", _ => "Cüce" };
    public static string Name(Sign s) => s.ToString();
    public static string Name(School s) => s switch { School.Wolf => "Kurt", School.Cat => "Kedi", School.Griffin => "Griffin", School.Bear => "Ayı", School.Viper => "Engerek", _ => "Mantikor" };
    public static string Name(Potion p) => p switch
    {
        Potion.Thunderbolt => "Gök Gürültüsü", Potion.Swallow => "Kırlangıç", Potion.BlackBlood => "Kara Kan",
        Potion.DancingStar => "Dans Eden Yıldız", _ => "Kuzey Rüzgârı"
    };

    public static string Describe(School s) => s switch
    {
        School.Wolf => "Her savaşın başında +1 Odak.",
        School.Cat => "Satır sırasını hazırlıkta görürsün ve iki satırın yerini değiştirebilirsin. Casus bilgisi +1 kart.",
        School.Griffin => "Aard ve Igni etkisi %50 daha güçlü.",
        School.Bear => "Quen bir işareti engellerse rakibin o satırı −%10. Kaybettiğin savaşta %10 daha az hasar alırsın.",
        School.Viper => "Üç simya kartı taşıyabilirsin; bombaların %25 daha güçlü.",
        _ => "Round başı simya seçimini 2 yerine 3 karttan yaparsın."
    };

    public static string Describe(Potion p) => p switch
    {
        Potion.Thunderbolt => "İksir: bu satırına +%25.",
        Potion.Swallow => "İksir: bu satırındaki hava etkisini kaldırır.",
        Potion.BlackBlood => "İksir: bu pencerede sana atılan Igni rakibin kendi satırına yansır.",
        Potion.DancingStar => "Bomba: rakibin bu satırdaki en güçlü iki kartı −%30.",
        _ => "Bomba: rakibin bu satırdaki en güçlü kartı −%70."
    };

    public static string Describe(Sign s) => s switch
    {
        Sign.Aard => "1 Odak · Rakibin bu satırı −%10. Engellenemez, kahramanlara da işler.",
        Sign.Igni => "2 Odak · Rakibin bu satırdaki en güçlü kartı −%50. Quen engeller.",
        Sign.Quen => "1 Odak · Sana atılan Igni'yi engeller; engellerse Odak geri gelir.",
        _ => "1 Odak · Rakibin bu penceredeki simya kartını ve pusu inişini keser."
    };

    public static string Describe(Ability a) => a switch
    {
        Ability.Bond => "Sıkı Bağ: aynı satırda ortak etiketli her diğer birim için +1 (en çok +3).",
        Ability.Growth => "Büyüme: aynı satırdaki her diğer birim için +1 (en çok +4).",
        Ability.Morale => "Moral: seçtiğin satırdaki diğer birimlere yıldızı kadar güç verir.",
        Ability.Spy => "Casus: kilit anında rakibin sahasından kart gösterir (%20 / %35 / %50).",
        Ability.Bard => "Ozan: savaşın başında +1 Odak.",
        Ability.Treasure => "Hazine: sahadaysa her round +1 altın (en çok 2 Hazine).",
        Ability.Ambush => "Pusu: pusu yerine konabilir; savaşta istediğin satıra canlı iner.",
        Ability.Revenge => "İntikam: önceki satırı kaybettiysen +%50.",
        Ability.Zeal => "Coşku: önceki satırı kazandıysan +%30.",
        Ability.Summon => "Çağrı: 2★ depodan bir eşini, 3★ iki eşini savaşa çeker.",
        Ability.Medic => "Şifacı: en güçlü birimini hava, Kavurma ve Igni'den korur (2★ iki birim; 3★ ayrıca diriliş).",
        Ability.DeathEcho => "Ölüm Yankısı: yok olunca satırına bir hayalet bırakır (2 / 3 / 5).",
        Ability.Mirror => "Yansıma: satırındaki en güçlü birimin yarısı kadar güce çıkar.",
        _ => ""
    };

    public static string Describe(Special s) => s switch
    {
        Special.Frost => "Hava: iki tarafın Yakın satırındaki birimler 1 olur.",
        Special.Fog => "Hava: iki tarafın Menzilli satırındaki birimler 1 olur.",
        Special.Rain => "Hava: iki tarafın Kuşatma satırındaki birimler 1 olur.",
        Special.Scorch => "Kavurma: iki sahadaki en güçlü birim(ler) savaş başında yok olur.",
        Special.Horn => "Boynuz: seçtiğin satıra +%50.",
        Special.Shield => "Kalkan: seçtiğin satırını hava ve Kavurma'dan korur.",
        _ => ""
    };

    public static string Describe(Hero h) => h switch
    {
        Hero.Geralt => "Bulunduğu satırın penceresinde ilk işaretin bedava.",
        Hero.Ciri => "Pusu. İndiği pencerede rakibin işaretlerinden etkilenmez.",
        Hero.Yennefer => "Satırını kazanırsan sonraki satırına +%20.",
        Hero.Triss => "Her savaşta ilk Quen bedava.",
        Hero.Jaskier => "Savaş başına bir kez, bir pencerede iki işaret atabilirsin.",
        Hero.Vesemir => "Satırını kaybedersen Öfke +2 (+1 yerine).",
        Hero.Zoltan => "Satırındaki Cüceler bombalardan etkilenmez.",
        Hero.Eredin => "Savaş başında rakibin rastgele bir satırındaki birimler −%30.",
        _ => ""
    };

    /// <summary>Etiket eşik bonuslarının (2/4/6) açıklaması.</summary>
    public static string[] Thresholds(Tag t) => t switch
    {
        Tag.Monster => new[] { "Canavarlar +1", "Canavarlar +2", "Canavarlar +3" },
        Tag.Soldier => new[] { "Askerler havada 2", "Askerler havada 3", "Askerler havadan etkilenmez" },
        Tag.Mage => new[] { "İşaretler +%25", "+1 Odak", "Savaş başına bir işaret bedava" },
        Tag.Bandit => new[] { "Pusu yeri 3 kart", "Pusudan inen +%25", "Pusu Yrden'den etkilenmez" },
        Tag.Noble => new[] { "Round başı +1 altın", "İlk reroll bedava", "Round başı +2 altın" },
        _ => new[] { "Cüceler bombalardan yarı etkilenir", "Cüceler Kavurma'dan korunur", "Cücelerin satırına +%20" }
    };
}
