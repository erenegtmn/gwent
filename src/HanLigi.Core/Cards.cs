namespace HanLigi;

public enum Row { Close, Ranged, Siege }

public enum Tag { Monster, Soldier, Mage, Bandit, Noble, Dwarf, Sailor, Knight, Vampire }

/// <summary>Birim yetenekleri (YENI_MOD.md §6.3).</summary>
public enum Ability { None, Bond, Growth, Morale, Spy, Bard, Treasure, Ambush, Revenge, Zeal, Summon, Medic, DeathEcho, Mirror, Plunder, Berserk, RowScorch, Challenge, Drain, Trio }

/// <summary>Özel kartlar: gücü yok, slot tüketir, etikete sayılmaz.</summary>
public enum Special { None, Frost, Fog, Rain, Scorch, Horn, Shield, Mardroeme, Storm }

public enum Hero { None, Geralt, Ciri, Yennefer, Triss, Jaskier, Vesemir, Zoltan, Eredin, Letho, Cerys, Iorveth, Philippa, Regis, Menno }

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

    private static void H(string id, string name, int power, Row row, Hero hero, string art, Tag tag, Ability ab = Ability.None, Tag? tag2 = null)
        => Add(new CardDef(id, name, 6, power, row, false, tag2 is { } t2 ? new[] { tag, t2 } : new[] { tag }, ab, Special.None, hero, art));

    private static void Add(CardDef d) { _all.Add(d); _byId[d.Id] = d; }

    static CardSet()
    {
        // ---- Tier 1 (güç 1-3) ----
        U("nekker", "Nekker", 1, 2, Row.Close, Tag.Monster, Ability.Summon);
        U("ghoul", "Gulyabani", 1, 1, Row.Close, Tag.Monster, Ability.Growth);
        U("poor_fucking_infantry", "Zavallı Piyade", 1, 2, Row.Close, Tag.Soldier, Ability.Bond);
        U("kaedweni_siege_expert", "Kaedwen Kuşatma Uzmanı", 4, 1, Row.Siege, Tag.Soldier, Ability.Morale);
        U("elven_skirmisher", "Elf Çatışmacı", 1, 2, Row.Ranged, Tag.Bandit, Ability.Ambush);
        U("dwarven_skirmisher", "Cüce Çatışmacı", 1, 3, Row.Close, Tag.Dwarf);
        U("sweers", "Tahsildar Sweers", 1, 1, Row.Ranged, Tag.Noble, Ability.Treasure);
        // ---- Tier 2 (güç 3-4) ----
        U("arachas", "Arachas", 2, 3, Row.Close, Tag.Monster, Ability.Summon);
        U("blue_stripes_commando", "Mavi Çizgili Komando", 2, 4, Row.Close, Tag.Soldier, Ability.Bond);
        U("thaler", "Thaler", 2, 1, Row.Siege, Tag.Noble, Ability.Spy);
        U("vrihedd_brigade_recruit", "Vrihedd Acemisi", 2, 3, Row.Ranged, Tag.Bandit, Ability.Ambush, agile: true);
        U("mahakaman_defender", "Mahakam Savunucusu", 2, 4, Row.Close, Tag.Dwarf, Ability.Revenge);
        U("sabrina_glevissig", "Sabrina Glevissig", 2, 3, Row.Ranged, Tag.Mage, Ability.Bard);
        U("dun_banner_medic", "Dun Sancağı Şifacısı", 2, 3, Row.Siege, Tag.Soldier, Ability.Medic);
        // ---- Tier 3 (güç 5-6) ----
        U("griffin", "Griffin", 3, 5, Row.Close, Tag.Monster, Ability.Zeal);
        U("crinfrid_reavers_dragon_hunter", "Crinfrid Ejder Avcısı", 3, 5, Row.Ranged, Tag.Soldier, Ability.Bond);
        U("dethmold", "Dethmold", 3, 6, Row.Ranged, Tag.Mage);
        U("vrihedd_brigade_veteran", "Vrihedd Kıdemlisi", 3, 4, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
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
        // ---- Denizci (Skellige klanları; Faz 1, 2026-11-23) ----
        U("clan_heymaey_skald", "Heymaey Ozanı", 1, 2, Row.Close, Tag.Sailor, Ability.Bard);
        U("young_berserker", "Genç Berserker", 2, 2, Row.Ranged, Tag.Sailor, Ability.Berserk);
        U("clan_drummond_shield_maiden", "Drummond Kalkan Kızı", 2, 4, Row.Close, Tag.Sailor, Ability.Bond);
        U("light_longship", "Hafif Uzun Gemi", 2, 3, Row.Ranged, Tag.Sailor, Ability.Summon);
        U("berserker", "Berserker", 3, 4, Row.Close, Tag.Sailor, Ability.Berserk);
        U("clan_brokvar_archer", "Brokvar Okçusu", 3, 6, Row.Ranged, Tag.Sailor, Ability.Plunder);
        U("clan_dimun_pirate", "Dimun Korsanı", 3, 4, Row.Ranged, Tag.Sailor, Ability.RowScorch);
        U("clan_an_craite_warrior", "an Craite Savaşçısı", 4, 7, Row.Close, Tag.Sailor, Ability.Bond);
        U("war_longship", "Savaş Gemisi", 4, 8, Row.Siege, Tag.Sailor, Ability.Plunder);
        U("olaf", "Olaf", 5, 11, Row.Close, Tag.Sailor, Ability.Morale, agile: true);
        // ---- Şövalye (Nilfgaard ve Kuzey; Faz 1) ----
        U("nausicaa_cavalry_rider", "Nausicaa Süvarisi", 1, 2, Row.Close, Tag.Knight, Ability.Bond);
        U("vreemde", "Vreemde", 1, 2, Row.Close, Tag.Knight, Ability.Bond);
        U("impera_brigade_guard", "İmpera Muhafızı", 2, 3, Row.Close, Tag.Knight, Ability.Bond);
        U("morteisen", "Morteisen", 2, 4, Row.Close, Tag.Knight, Ability.Challenge);
        U("rainfarn", "Rainfarn", 3, 5, Row.Close, Tag.Knight, Ability.Challenge);
        U("ves", "Ves", 3, 5, Row.Close, Tag.Knight, Ability.Zeal);
        U("siegfried_of_denesle", "Denesle'li Siegfried", 4, 8, Row.Close, Tag.Knight, Ability.Challenge);
        U("cahir_mawr_dyffryn_aep_ceallach", "Cahir", 4, 8, Row.Close, Tag.Knight, Ability.Revenge);
        U("renuald_aep_matsen", "Renuald aep Matsen", 5, 11, Row.Ranged, Tag.Knight, Ability.Morale);
        // ---- Faz 2 (2026-11-23) ----
        // Vampir
        U("vampire_fleder", "Fleder", 1, 2, Row.Close, Tag.Vampire, Ability.Drain);
        U("vampire_ekimmara", "Ekimmara", 2, 4, Row.Close, Tag.Vampire, Ability.Drain);
        U("vampire_garkain", "Garkain", 2, 4, Row.Close, Tag.Vampire, Ability.Bond);
        U("vampire_bruxa", "Bruxa", 3, 5, Row.Close, Tag.Vampire, Ability.Drain);
        U("vampire_katakan", "Katakan", 4, 8, Row.Close, Tag.Vampire, Ability.Drain);
        U("plague_maiden", "Veba Kızı", 5, 10, Row.Close, Tag.Vampire, Ability.Drain);   // Vampir'in T5'i (2026-11-23)
        // görsel orijinal W3 Gwent'inde yok: Gwent (Crimson Curse) kartından kırpıldı, assets/cards/vampire_dettlaff.png (2026-11-23)
        U("vampire_dettlaff", "Dettlaff", 5, 12, Row.Close, Tag.Vampire, Ability.Drain);
        // Haydut (Scoia'tael)
        U("toruviel", "Toruviel", 1, 2, Row.Ranged, Tag.Bandit, Ability.Ambush);
        U("ciaran_aep_easnillien", "Ciaran", 2, 3, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
        U("havekar_smuggler", "Havekar Kaçakçısı", 2, 3, Row.Close, Tag.Bandit, Ability.Treasure);
        U("yaevinn", "Yaevinn", 3, 5, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
        U("dol_blathanna_archer", "Dol Blathanna Okçusu", 3, 5, Row.Ranged, Tag.Bandit, Ability.Zeal);
        U("filavandrel_aen_fidhail", "Filavandrel", 4, 7, Row.Close, Tag.Bandit, Ability.Ambush, agile: true);
        U("milva", "Milva", 5, 11, Row.Ranged, Tag.Bandit, Ability.Morale);
        // Büyücü
        U("albrich", "Albrich", 1, 2, Row.Ranged, Tag.Mage, Ability.Bard);
        U("cynthia", "Cynthia", 2, 3, Row.Ranged, Tag.Mage, Ability.Spy);
        U("keira_metz", "Keira Metz", 2, 5, Row.Ranged, Tag.Mage, Ability.Zeal);
        U("ida_emean_aep_sivney", "Ida Emean", 3, 6, Row.Ranged, Tag.Mage, Ability.Bard, tag2: Tag.Bandit);
        // Canavar: Kocakarılar
        U("crone_weavess", "Dokumacı Kocakarı", 4, 7, Row.Close, Tag.Monster, Ability.Trio);
        U("crone_brewess", "Ocakçı Kocakarı", 4, 7, Row.Close, Tag.Monster, Ability.Trio);
        U("crone_whispess", "Fısıltıcı Kocakarı", 4, 7, Row.Close, Tag.Monster, Ability.Trio);
        // Asker, Soylu, Cüce
        U("redanian_foot_soldier", "Redanya Piyadesi", 1, 2, Row.Close, Tag.Soldier, Ability.Bond);
        U("ballista", "Balista", 3, 5, Row.Siege, Tag.Soldier, Ability.RowScorch);
        U("prince_stennis", "Prens Stennis", 3, 4, Row.Close, Tag.Noble, Ability.Spy);
        U("shilard_fitz_oesterlen", "Shilard", 5, 10, Row.Close, Tag.Noble, Ability.Spy);
        U("dennis_cranmer", "Dennis Cranmer", 4, 7, Row.Close, Tag.Dwarf, Ability.Morale);
        // ---- Özel kartlar ----
        S("frost", "Dondurucu Ayaz", 3, Special.Frost, "biting_frost");
        S("fog", "Geçit Vermez Sis", 3, Special.Fog, "impenetrable_fog");
        S("rain", "Sağanak Yağmur", 3, Special.Rain, "torrential_rain");
        S("horn", "Komutan Boynuzu", 3, Special.Horn, "commanders_horn");
        S("shield", "Kalkan Alayı", 4, Special.Shield, "decoy");
        S("scorch", "Kavurma", 4, Special.Scorch, "scorch");
        S("mardroeme", "Mardroeme", 3, Special.Mardroeme, "mardroeme");
        S("storm", "Skellige Fırtınası", 5, Special.Storm, "skellige_storm");   // iki satır kapatır: kahraman kademesinde (2026-11-24)
        // ---- Kahramanlar (18-22; evolve olmaz) ----
        H("geralt", "Rivyalı Geralt", 22, Row.Close, Hero.Geralt, "geralt_of_rivia", Tag.Soldier);
        H("ciri", "Cirilla", 20, Row.Close, Hero.Ciri, "cirilla_fiona_elen_riannon", Tag.Bandit, Ability.Ambush);
        H("yennefer", "Vengerbergli Yennefer", 19, Row.Ranged, Hero.Yennefer, "yennefer_of_vengerberg", Tag.Mage);
        H("triss", "Triss Merigold", 18, Row.Ranged, Hero.Triss, "triss_merigold", Tag.Mage);
        H("jaskier", "Jaskier", 18, Row.Close, Hero.Jaskier, "dandelion", Tag.Noble);
        H("vesemir", "Vesemir", 20, Row.Close, Hero.Vesemir, "vesemir", Tag.Soldier);
        H("zoltan", "Zoltan Chivay", 19, Row.Close, Hero.Zoltan, "zoltan_chivay", Tag.Dwarf);
        H("eredin", "Eredin", 21, Row.Close, Hero.Eredin, "imlerith", Tag.Monster);
        // Faz 3 (2026-11-23)
        H("letho", "Gulet'li Letho", 21, Row.Close, Hero.Letho, "letho_of_gulet", Tag.Soldier);
        H("cerys", "Cerys", 19, Row.Close, Hero.Cerys, "cerys", Tag.Sailor);
        H("iorveth", "Iorveth", 20, Row.Ranged, Hero.Iorveth, "iorveth", Tag.Bandit);
        H("philippa", "Philippa Eilhart", 19, Row.Ranged, Hero.Philippa, "philippa_eilhart", Tag.Mage);
        H("regis", "Regis", 19, Row.Close, Hero.Regis, "emiel_regis_rohellec_terzieff", Tag.Vampire);
        H("menno", "Menno Coehoorn", 20, Row.Close, Hero.Menno, "menno_coehoorn", Tag.Knight, Ability.Medic);
    }
}

/// <summary>Ayar sayıları (YENI_MOD.md §15; 7. tur model ölçümünden).</summary>
public static class Tuning
{
    /// <summary>Yalnız ölçüm için (meta karşılaştırması): pusu eskisi gibi masa yeri yesin mi. Oyunda her zaman false.</summary>
    public static bool AmbushTakesSlot;

    public const int StartHp = 100;
    public const int StartGold = 5;
    public const int Income = 4;
    public const int WinGold = 1;
    public const int InterestPer = 10;
    /// <summary>Faiz üst sınırı (meta ölçümünde değiştirilebilir; oyunda sabit).</summary>
    public static int InterestCap = 2;

    /// <summary>
    /// Seri altını (round başı). Yenilgi serisi geri dönüş ve "bilerek kaybedip ekonomi kur" yolunu açar; galibiyet serisi küçük
    /// tutulur (önde olanı kartopu gibi büyütmesin): yenilgi 2 → +1, 3 → +2, 4 → +3, 5+ → +4; galibiyet 3-4 → +1, 5+ → +2 (Meta 3).
    /// </summary>
    public static int StreakGold(int streak) => StreakMode switch
    {
        1 => streak switch { <= -5 => 4, -4 => 3, -3 => 2, -2 => 1, >= 5 => 2, >= 3 => 1, _ => 0 },
        2 => streak switch { <= -5 => 4, -4 => 3, -3 => 2, -2 => 1, _ => 0 },
        _ => streak switch { <= -5 => 3, <= -3 => 2, -2 => 1, >= 5 => 2, >= 3 => 1, _ => 0 }
    };

    /// <summary>Seri altını tablosu (meta denemesi): 0 = Meta 2, 1 = yenilgi serisi güçlü (Meta 3, oyunda bu), 2 = ayrıca galibiyet serisi altını yok.</summary>
    public static int StreakMode = 1;

    /// <summary>
    /// Hasarın taban seviyesi (meta denemesi): 0 = kazananın seviyesi, 1 = round'un seviyesi (<see cref="StageLevel"/>),
    /// 2 = kazananın seviyesi ama round seviyesini en çok 1 aşabilir.
    /// </summary>
    public static int DamageMode;

    /// <summary>
    /// Hasar çarpanının round'a göre eğrisi: 0 = düz (Meta 2), 3 = erken hafif / geç ağır (Meta 3, oyunda bu: 1-8. round ×0,6,
    /// 9-14 ×1, 15+ ×1,3; erken kayıp maçı kilitlemesin, arkadan gelmek mümkün olsun), 4 = daha yumuşak eğri.
    /// </summary>
    public static int RampMode = 3;

    public static double DamageRamp(int round) => round <= 0 ? 1.0 : RampMode switch   // round bilinmiyorsa (doğrudan kurulan savaş) düz
    {
        3 => round <= 8 ? 0.6 : round <= 14 ? 1.0 : 1.3,
        4 => round <= 8 ? 0.75 : round <= 14 ? 1.0 : 1.15,
        _ => 1.0
    };

    /// <summary>Round'a göre beklenen seviye (botların seviye eğrisi): hasar tabanı için.</summary>
    public static int StageLevel(int round) => round <= 2 ? 3 : round <= 4 ? 4 : round <= 7 ? 5 : round <= 10 ? 6 : round <= 14 ? 7 : 8;
    /// <summary>XP fiyatı (meta ölçümünde değiştirilebilir; oyunda sabit).</summary>
    public static int XpCost = 5;
    public const int XpGain = 4;
    public const int XpPerRound = 2;
    public const int RerollCost = 1;
    public const int HeroPrice = 7;
    public const int BenchSize = 8;
    public const int MaxLevel = 10;
    /// <summary>Botların seviye tavanı: L9-L10 yalnız insanlara açık ek kademe (botlar 8'de durur, meta ölçümleri değişmesin).</summary>
    public const int BotCap = 8;
    public const int ShopSize = 5;
    public static int StartFocus = 2;   // 2026-11-18 Meta 4: 3 iken Odak neredeyse hiç yetmezlik yapmıyordu (3. satırda %4)
    /// <summary>Bir savaşta birikebilecek en çok Odak (başlangıç bonusları, Öfke ve Quen iadesi bunu aşamaz).</summary>
    public static int FocusCap = 99;
    /// <summary>Igni'yi engelleyen Quen'in Odağı geri gelir mi.</summary>
    public static bool QuenRefund = true;
    /// <summary>Yrden rakibin Aard'ını da keser (işaret üçgeni: Quen Igni'yi, Yrden Aard'ı ve simyayı tutar).</summary>
    public static bool YrdenBlocksAard = true;
    /// <summary>Yrden pusuya ne yapar: 0 keser (son pencerede hiç inmez), 1 dokunmaz, 2 yalnız pusu (simya yok), 3 inenler −%,
    /// 4 bilerek indirilenler −%, <b>5 bilerek indirilen bu pencerede inemez</b> (sonra inebilir; son pencerede kendiliğinden inenlere
    /// dokunmaz). Meta 4: son pencerede bütün pusuyu kesen Yrden açık ara en güçlüydü (hep Yrden ort. sıra 2,4); 3-4 de baskındı.</summary>
    public static int YrdenMode = 5;
    public static int YrdenCost = 2;
    /// <summary>Yrden tuzağı: Yrden ne pusu inişini ne Aard'ı tuttuysa rakibin satırına bu yüzde (Aard gibi okul/Büyücü ile büyür; 0 = eski kural, boşa gider).</summary>
    public static double YrdenTrapPct = 0.10;
    /// <summary>YrdenMode 3: simya + Aard kesilir, pusudan inen kartlar bu kadar zayıflar (iptal yerine).</summary>
    public static double YrdenAmbushPct = 0.5;
    /// <summary>Temel pusu yeri (Haydut 1. eşik +1). Denge 3: 2 → 1 (Haydut kazananların %32si idi).</summary>
    public static int AmbushBase = 1;
    /// <summary>Son pencerede seçilmeyen pusu kendiliğinden iner mi. Oyunda false: pusu hep isteğe bağlı, Yrden seçileni mutlak keser.</summary>
    public static bool AutoLandLast = false;
    /// <summary>Yrden simyayı da keser mi (Meta 4: hayır; simya + pusu + Aard birlikte Yrden'i her işe yarar yapıyordu).</summary>
    public static bool YrdenPotion = false;
    public const int RageCap = 2;
    public const double DamageMult = 2.25;
    public const double PveLater = 0.8;
    public const int PotionsFromRound = 4;

    /// <summary>
    /// Sınıf eşikleri (TFT gibi sınıfa göre): kart sayısı az olan sınıfların son eşiği ulaşılabilir olsun. Haydutta 4, Büyücü ve
    /// Cücede 5, ötekilerde 6+ farklı kart var.
    /// </summary>
    private static readonly int[] Bp246 = { 2, 4, 6 }, Bp235 = { 2, 3, 5 }, Bp234 = { 2, 3, 4 };
    // Faz 2: Haydut (11 kart) ve Büyücü (9) 2/4/6'ya geçti; az kartlı Cüce ve Vampir 2/3/5
    public static int[] Breakpoints(Tag t) => t switch { Tag.Dwarf or Tag.Vampire or Tag.Mage => Bp235, _ => Bp246 };
    /// <summary>Sınıf sayısından eşik basamağı (0-3).</summary>
    public static int TagLevel(Tag t, int n) { var b = Breakpoints(t); return n >= b[2] ? 3 : n >= b[1] ? 2 : n >= b[0] ? 1 : 0; }
    /// <summary>Kedi okulu: kazandığı savaş başına ek altın.</summary>
    public const int CatWinGold = 1;
    /// <summary>Mantikor okulu simya seçimine bu round'dan başlar (ötekiler <see cref="PotionsFromRound"/>).</summary>
    public const int ManticorePotionsFrom = 2;

    /// <summary>L2…L10 kümülatif XP eşikleri (L9-L10 yalnız market olasılığını artırır, slot vermez).</summary>
    public static readonly int[] XpThresholds = { 2, 6, 12, 22, 35, 52, 72, 96, 124 };
    /// <summary>Her kartın havuzdaki kopya sayısı (tier 1-5).</summary>
    public static readonly int[] Copies = { 22, 18, 15, 12, 9 };
    /// <summary>Seviyeye göre market tier olasılıkları (%).</summary>
    public static readonly int[][] ShopOdds =
    {
        new[] { 100, 0, 0, 0, 0 }, new[] { 85, 15, 0, 0, 0 }, new[] { 65, 30, 5, 0, 0 }, new[] { 45, 38, 15, 2, 0 },
        new[] { 30, 38, 25, 7, 0 }, new[] { 20, 30, 32, 15, 3 }, new[] { 15, 22, 33, 23, 7 }, new[] { 10, 15, 30, 30, 15 },
        new[] { 8, 12, 26, 34, 20 }, new[] { 5, 10, 22, 38, 25 }
    };
    /// <summary>Seviyeye göre yuva başına kahraman olasılığı.</summary>
    /// <summary>Her maçta rastgele açık sınıf sayısı (alt havuz; kapalı sınıfın kartları ve kahramanları markete gelmez). Sınıf sayısını aşarsa hepsi açık.</summary>
    public static int ActiveClasses = 6;
    /// <summary>Yağma: kazanılan satırdaki Yağma kartı başına 1 altın, savaş başına üst sınır.</summary>
    public const int PlunderCap = 2;
    /// <summary>Yakıcı: rakibin aynı satırının toplamı en az bu kadarsa en güçlü birimi yanar.</summary>
    public const int RowScorchMin = 14;
    /// <summary>Dönüşüm: Mardroeme'nin satırındaki Berserker'ın güç çarpanı (2026-11-24: 3 → 2; Berserker'lar T2/T3'e çıktı).</summary>
    public const int BerserkMult = 2;
    /// <summary>Denizci eşikleri: ilk pencere yüzdesi [1-2. eşik, 3. eşik] ve düz güç [2. eşik, 3. eşik] (2026-11-24: %30/%40, +2/+3'ten indi).</summary>
    public static readonly double[] SailorFirst = { 0.20, 0.30 };
    public static readonly int[] SailorFlat = { 1, 2 };
    /// <summary>Meydan Okuma: kendisinden güçlü rakip birime yüzde.</summary>
    public const double ChallengePct = 0.25;
    /// <summary>Haydut 2. / 3. eşik: pusudan inen birimin yüzdesi (Denge 2: %25 / %50'den indi).</summary>
    public static readonly double[] BanditLand = { 0.15, 0.35 };
    /// <summary>Kan Emme: rakibin en zayıf birimin değerinden alınan pay (Vampir 1. eşikte %75).</summary>
    public const double DrainPct = 0.5, DrainPctVampire = 0.75;
    /// <summary>Üçlü: üç farklı Kocakarı sahadaysa her biri ×1,5.</summary>
    public static readonly string[] Crones = { "crone_weavess", "crone_brewess", "crone_whispess" };
    public static readonly double[] HeroOdds = { 0, 0, 0, 0, 0.004, 0.008, 0.015, 0.022, 0.03, 0.04 };
    // Kahraman güç basamakları [I, II, III]: basamak, kahramanın kendi sınıfının eşiğine göre (II: 2. eşik, III: 3. eşik)
    public static readonly double[] EredinPct = { 0.30, 0.40, 0.50 };
    public static readonly double[] YenPct = { 0.20, 0.30, 0.40 };
    public static readonly int[] VesemirGain = { 2, 3, 4 };
    public static readonly int[] TrissFree = { 1, 2, 99 };
    public static readonly int[] JaskierUses = { 1, 2, 3 };
    public static readonly double[] GeraltRow = { 0, 0.10, 0.20 };
    public static readonly double[] CiriRow = { 0, 0.15, 0.30 };
    public static readonly double[] LethoPct = { 0.30, 0.45, 0.60 };
    public static readonly int[] CerysShips = { 1, 2, 3 };
    public const int CerysShipPower = 4;
    public static readonly double[] IorvethPct = { 0.15, 0.25, 0.40 };
    public static readonly int[] PhilippaSigns = { 1, 2, 3 };
    public static readonly double[] RegisPct = { 0.25, 0.50, 0.75 };
    public static readonly int[] MennoProtect = { 2, 3, 3 };
    public static readonly double[] SpyRate = { 0.20, 0.35, 0.50 };

    public static int Slots(int level) => Math.Min(10, level + 2);

    /// <summary>Yıldıza göre güç: 2★ ×1,5; 3★ T1 ×6, T2 ×4,5, T3+ ×3,5.</summary>
    public static int Power(CardDef d, int star)
    {
        if (d.IsHero || star <= 1) return d.Power;
        var k = star == 2 ? 1.5 : d.Tier == 1 ? 6 : d.Tier == 2 ? 4.5 : 3.5;
        return Round(d.Power * k);
    }

    /// <summary>En yakın tamsayı, yarım yukarı (§15.1 R2).</summary>
    public static int Round(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>
    /// Yüzdesel etkiyi uygular (savaştaki bütün yüzdeler buradan geçer). Güç ondalıklıdır, hassasiyet 0,1 (§15.4):
    /// (1) sonuç en yakın 0,1'e yuvarlanır; tam ortadaysa etkinin lehine (düşüşte aşağı, artışta yukarı) — 2 × −%10 = 1,8;
    /// (2) sonuç 0'ın altına inmez, %−100'den fazla düşüş yoktur. Değer 0 ise etki yoktur.
    /// Eski kural (tam sayı + "en az 1 puan") küçük güçlerde etkiyi katlıyordu: 2'lik karta −%10 → 1 (gerçekte −%50).
    /// <c>decimal</c>: 10 tabanında tam ve her makinede aynı sonuç (online kopyalar sunucuyla birebir aynı kalır).
    /// </summary>
    public static decimal ApplyPct(decimal value, double pct)
    {
        if (value <= 0 || Math.Abs(pct) < 1e-9) return Math.Max(0, value);
        var x = value * (1 + (decimal)Math.Max(-1, pct));
        // en yakın 0,1; tam orta etkinin lehine (MidpointRounding.ToNegativeInfinity en yakın değil, düz aşağı keser: kullanılmaz)
        var r = pct < 0 ? Math.Ceiling(x * 10 - 0.5m) / 10 : Math.Floor(x * 10 + 0.5m) / 10;
        return Math.Max(0, r);
    }

    /// <summary>Gücü yazar: tam sayıysa "7", değilse "6,8" (yüzdelerden doğan ondalık).</summary>
    public static string Fmt(decimal v) => v.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    public static int SignCost(Sign s) => s == Sign.Igni ? 2 : s == Sign.Yrden ? YrdenCost : 1;

    public static int SellValue(CardDef d, int star) => d.IsHero ? HeroPrice : star == 1 ? d.Price : (star == 2 ? 3 : 9) * d.Price - 1;
}

public static class Texts
{
    public static string Name(Row r) => r switch { Row.Close => "Yakın", Row.Ranged => "Menzilli", _ => "Kuşatma" };
    public static string Name(Tag t) => t switch { Tag.Monster => "Canavar", Tag.Soldier => "Asker", Tag.Mage => "Büyücü", Tag.Bandit => "Haydut", Tag.Noble => "Soylu", Tag.Sailor => "Denizci", Tag.Knight => "Şövalye", Tag.Vampire => "Vampir", _ => "Cüce" };
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
        School.Cat => "Satır sırasını hazırlıkta görürsün ve iki satırın yerini değiştirebilirsin. Casus bilgisi +1 kart. Bir oyuncuyu yendiğin savaşta +1 altın.",
        School.Griffin => "Aard ve Igni etkisi %50 daha güçlü.",
        School.Bear => "Quen bir işareti engellerse rakibin o satırı −%10. Kaybettiğin savaşta %10 daha az hasar alırsın.",
        School.Viper => "Üç simya kartı taşıyabilirsin; bombaların %25 daha güçlü.",
        _ => "Simya seçimi 2. round'dan başlar, 2 yerine 3 karttan yaparsın ve üç simya kartı taşıyabilirsin."
    };

    public static string Describe(Potion p) => Describe(p, null);

    public static string Describe(Sign s) => Describe(s, null, 0);

    /// <summary>Yüzdeyi okunur yazar: 87,5 → "87,5", 50 → "50".</summary>
    public static string Pct(double v) => (Math.Round(v, 1)).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>
    /// Okul ve Büyücü eşiğinden gelen etki büyütücü: Aard, Igni (okul + Büyücü) ve bombalar (Engerek) buna göre değişir.
    /// <c>Amp</c> çarpandır (1 = büyütücü yok); <c>Why</c> kaynakları sayar ("Griffin +%50, Büyücü 1. eşik +%25").
    /// </summary>
    public static (double Amp, string Why) SignAmp(School? school, int mageLevel)
    {
        var parts = new List<string>();
        var amp = 1.0;
        if (school == School.Griffin) { amp += 0.5; parts.Add("Griffin +%50"); }
        if (mageLevel >= 1) { var m = mageLevel >= 2 ? 0.5 : 0.25; amp += m; parts.Add($"Büyücü {mageLevel}. eşik +%{m * 100:0}"); }
        return (amp, string.Join(", ", parts));
    }

    /// <summary>Bir yüzdenin güncel hâli: büyütücü yoksa "−%50", varsa "−%87,5 (−%50 × 1,75: Griffin +%50, ...)".</summary>
    public static string Live(double basePct, double amp, string why, string sign = "−")
    {
        var now = basePct * amp;
        return Math.Abs(amp - 1) < 1e-9 ? $"{sign}%{Pct(now)}" : $"{sign}%{Pct(now)} ({sign}%{Pct(basePct)} × {Pct(amp)}: {why})";
    }

    /// <summary>
    /// İşaretin bu oyuncu için GÜNCEL etkisi. Okul ve Büyücü eşiği sayıları değiştirir; değişen her sayı hesabıyla yazılır.
    /// </summary>
    public static string Describe(Sign s, School? school, int mageLevel)
    {
        var (amp, why) = SignAmp(school, mageLevel);
        return s switch
        {
            Sign.Aard => $"1 Odak · Rakibin bu satırı {Live(10, amp, why)}. Kahramanlara da işler; Yrden tutar.",
            Sign.Igni => $"2 Odak · Rakibin bu satırdaki en güçlü kartı {Live(50, amp, why)}. Quen engeller; Kara Kan kendine yansıtır.",
            Sign.Quen => "1 Odak · Sana atılan Igni'yi engeller; engellerse Odak geri gelir.",
            _ => $"2 Odak · Rakibin bu penceredeki Aard'ını keser ve pusudan indirmek istediği kartı indirtmez. İlk iki satırda kart pusuda kalır, sonraki satırda yeniden indirilebilir; son satırda ise hiç inemez. İkisini de tutmazsa tuzak rakibin bu satırını {Live(Tuning.YrdenTrapPct * 100, amp, why)} yavaşlatır.",
        };
    }

    /// <summary>
    /// İksir / bombanın güncel etkisi. <paramref name="foeDwarf"/>: rakipte Cüce eşiği var (Cüce hedeflere bomba yarı etki),
    /// <paramref name="foeZoltan"/>: rakibin satırında Zoltan var (Cüce hedeflere bomba işlemez).
    /// </summary>
    public static string Describe(Potion p, School? school, bool foeDwarf = false, bool foeZoltan = false)
    {
        var f = school == School.Viper ? 1.25 : 1.0;
        var why = "Engerek ×1,25";
        string Dw() => foeZoltan ? " Rakibin Zoltan'ı satırdaysa Cüce kartlara işlemez." : foeDwarf ? " Rakibin Cüce eşiği açık: Cüce kartlara yarı etki." : "";
        return p switch
        {
            Potion.Thunderbolt => "İksir: bu satırına +%25.",
            Potion.Swallow => "İksir: bu satırındaki hava etkisini kaldırır.",
            Potion.BlackBlood => "İksir: bu pencerede sana atılan Igni rakibin kendi satırına yansır.",
            Potion.DancingStar => $"Bomba: rakibin bu satırdaki en güçlü iki kartı {Live(30, f, why)}." + Dw(),
            _ => $"Bomba: rakibin bu satırdaki en güçlü kartı {Live(70, f, why)}." + Dw()
        };
    }

    /// <summary>
    /// Kartın bu an geçerli yüzdeleri (kart bilgi paneli "Şu an" bölümü). <paramref name="prev"/>: önceki satırın sonucu
    /// (+1 kazandın, −1 kaybettin, 0 ilk satır/berabere); <paramref name="eredinRow"/>: rakibin Eredin'i senin hangi satırını vurdu;
    /// <paramref name="foeEredinRow"/>: senin Eredin'in rakibin hangi satırını vurdu; <paramref name="banditLevel"/>: Haydut eşiği.
    /// </summary>
    public static List<string> LiveNotes(CardDef d, int? prev, Row? eredinRow, Row? foeEredinRow, int banditLevel, double eredinPct = 0.30, double foeEredinPct = 0.30)
    {
        var l = new List<string>();
        if (!d.IsUnit) return l;
        if (!d.IsHero && prev != null)
        {
            if (d.Ability == Ability.Revenge)
                l.Add(prev < 0 ? "İntikam AKTİF: önceki satırı kaybettin, bu satırda +%50." : "İntikam pasif: önceki satırı kaybedersen +%50 (şu an +%0).");
            if (d.Ability == Ability.Zeal)
                l.Add(prev > 0 ? "Coşku AKTİF: önceki satırı kazandın, bu satırda +%30." : "Coşku pasif: önceki satırı kazanırsan +%30 (şu an +%0).");
        }
        if (d.Ability == Ability.Ambush && banditLevel >= 2 && !d.IsHero)
            l.Add($"Haydut bonusu: pusudan inince +%{(banditLevel >= 3 ? 50 : 25)}.");
        if (d.Hero == Hero.Eredin && foeEredinRow is { } fr)
            l.Add($"Eredin bu savaş: rakibin {Name(fr)} satırındaki birimler −%{Pct(foeEredinPct * 100)}.");
        if (!d.IsHero && eredinRow is { } er)
            l.Add($"Rakibin Eredin'i bu savaş senin {Name(er)} satırını vurdu: o satırdaki kahraman olmayan birimler −%{Pct(eredinPct * 100)} (bu kart oradaysa).");
        return l;
    }

    /// <summary>Okulun açıklaması ve bu okulla bu an geçerli sayılar (varsa).</summary>
    public static string DescribeLive(School s, int mageLevel)
    {
        var d = Describe(s);
        if (s != School.Griffin && s != School.Viper) return d;
        var (amp, why) = SignAmp(s, mageLevel);
        return s == School.Griffin
            ? d + $"\nŞu an: Aard {Live(10, amp, why)}, Igni {Live(50, amp, why)}."
            : d + $"\nŞu an: Dans Eden Yıldız −%{Pct(30 * 1.25)}, Kuzey Rüzgârı −%{Pct(70 * 1.25)}.";
    }

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
        Ability.Summon => "Çağrı: savaş başında DEPONDAKİ aynı isimli kartlardan 2★ bir, 3★ iki tanesini sahaya çeker. Depoda eş yoksa işe yaramaz.",
        Ability.Medic => "Şifacı: en güçlü birimini hava, Kavurma ve Igni'den korur (2★ iki birim; 3★ ayrıca diriliş).",
        Ability.DeathEcho => "Ölüm Yankısı: yok olunca satırına bir hayalet bırakır (2 / 3 / 5).",
        Ability.Mirror => "Yansıma: satırındaki en güçlü birimin yarısı kadar güce çıkar.",
        Ability.Plunder => "Yağma: satırını kazanırsan +1 altın (savaş başına toplam en çok 2).",
        Ability.Berserk => $"Dönüşüm: Mardroeme'nin konduğu satırdaysa savaş başında gücü ×{Tuning.BerserkMult} olur.",
        Ability.RowScorch => $"Yakıcı: savaş başında rakibin aynı satırının toplamı en az {Tuning.RowScorchMin} ise oradaki en güçlü birim yanar (kahraman, Şifacı ve Kalkan korur).",
        Ability.Challenge => "Meydan Okuma: satırının penceresinde rakibin aynı satırdaki en güçlü birimi bu karttan güçlüyse −%25.",
        Ability.Drain => "Kan Emme: satırının penceresinde rakibin aynı satırdaki en zayıf biriminin gücünün yarısını alır (ondan düşer, kendine ekler).",
        Ability.Trio => "Üçlü: üç farklı Kocakarı (Dokumacı, Ocakçı, Fısıltıcı) sahandaysa her biri ×1,5.",
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
        Special.Storm => "Skellige Fırtınası: iki tarafın Menzilli ve Kuşatma satırlarındaki birimler 1 olur.",
        Special.Mardroeme => $"Mardroeme: seçtiğin satırdaki Berserker'lar savaş başında dönüşür (gücü ×{Tuning.BerserkMult}). O satırda Berserker yoksa etkisiz.",
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
        Hero.Letho => "Savaş başında rakibin en güçlü birimi bütün savaş boyunca −%30.",
        Hero.Cerys => "Savaş başında satırına 4 güçlü bir Uzun Gemi çağırır.",
        Hero.Iorveth => "Pusudan inen birimlerin +%15.",
        Hero.Philippa => "Savaştaki ilk Aard ya da Igni'sinin etkisi iki kat.",
        Hero.Regis => "Satırındaki Vampirlerin Kan Emme oranı +%25.",
        Hero.Menno => "Şifacı: en güçlü iki birimini hava, Kavurma ve Igni'den korur.",
        _ => ""
    };

    /// <summary>Kahramanın II ve III basamaklarının eklediği güç (sınıfının 2. ve 3. eşiğinde açılır).</summary>
    public static string[] HeroTiers(Hero h) => h switch
    {
        Hero.Geralt => new[] { "Bulunduğu satırın penceresinde satırına +%10.", "Bulunduğu satırın penceresinde satırına +%20." },
        Hero.Ciri => new[] { "İndiğinde satırındaki öteki birimler +%15.", "İndiğinde satırındaki öteki birimler +%30." },
        Hero.Yennefer => new[] { "Kazandığında sonraki satırına +%30.", "Kazandığında sonraki satırına +%40." },
        Hero.Triss => new[] { "İlk iki Quen bedava.", "Her Quen bedava." },
        Hero.Jaskier => new[] { "Savaşta iki kez iki işaret atabilirsin.", "Her pencerede iki işaret atabilirsin." },
        Hero.Vesemir => new[] { "Satırını kaybedersen Öfke +3 (üst sınır +1 daha).", "Satırını kaybedersen Öfke +4 (üst sınır +1 daha)." },
        Hero.Zoltan => new[] { "Tüm Cüceler (hangi satırda olursa) bombalardan etkilenmez.", "Satırındaki bütün birimler bombalardan etkilenmez." },
        Hero.Eredin => new[] { "Rakibin satırı −%40.", "Rakibin satırı −%50." },
        Hero.Letho => new[] { "En güçlü birim −%45.", "En güçlü birim −%60." },
        Hero.Cerys => new[] { "İki Uzun Gemi.", "Üç Uzun Gemi." },
        Hero.Iorveth => new[] { "Pusudan inenler +%25.", "Pusudan inenler +%40." },
        Hero.Philippa => new[] { "İlk iki Aard/Igni iki kat.", "İlk üç Aard/Igni iki kat." },
        Hero.Regis => new[] { "Kan Emme +%50.", "Kan Emme +%75." },
        Hero.Menno => new[] { "Üç birimi korur.", "Üç birimi korur, ayrıca bir birime diriliş (yanarsa yarı güçle döner)." },
        _ => new[] { "", "" }
    };

    /// <summary>Etiket eşik bonuslarının açıklaması (eşikler: <see cref="Tuning.Breakpoints"/>).</summary>
    public static string[] Thresholds(Tag t) => t switch
    {
        Tag.Monster => new[] { "Canavarlar +1", "Canavarlar +2", "Canavarlar +3" },
        Tag.Soldier => new[] { "Askerler havada 2", "Askerler havadan etkilenmez", "Askerler +2, havadan etkilenmez" },
        Tag.Mage => new[] { "İşaretler +%25", "+1 Odak, işaretler +%50", "Savaş başına bir işaret bedava" },
        Tag.Bandit => new[] { $"Pusu yeri {Tuning.AmbushBase + 1} kart", $"Pusudan inen +%{Pct(Tuning.BanditLand[0] * 100)}", $"Pusudan inen +%{Pct(Tuning.BanditLand[1] * 100)} · Pusu Yrden'den etkilenmez" },
        Tag.Noble => new[] { "İlk yenileme bedava", "Round başı +1 altın", "Round başı +3 altın" },
        Tag.Sailor => new[] { $"İlk pencerede Denizciler +%{Pct(Tuning.SailorFirst[0] * 100)}", $"Denizciler +{Tuning.SailorFlat[0]}", $"Denizciler +{Tuning.SailorFlat[1]}, ilk pencerede +%{Pct(Tuning.SailorFirst[1] * 100)}" },
        Tag.Knight => new[] { "Şövalyeler Igni ve bombalardan yarı etkilenir", "Şövalyeler +2", "Şövalye satırlarına +%15, Igni ve bombalara bağışık" },
        Tag.Vampire => new[] { "Kan Emme %75", "Yanan Vampir yarı güçle döner", "Kan Emme iki hedeften" },
        _ => new[] { "Cüceler bombalardan yarı etkilenir", "Cüceler +1, Kavurma'dan korunur", "Cücelerin satırına +%20" }
    };
}
