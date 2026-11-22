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

    private static void H(string id, string name, int power, Row row, Hero hero, string art, Tag tag, Ability ab = Ability.None, Tag? tag2 = null)
        => Add(new CardDef(id, name, 6, power, row, false, tag2 is { } t2 ? new[] { tag, t2 } : new[] { tag }, ab, Special.None, hero, art));

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
        H("geralt", "Rivyalı Geralt", 22, Row.Close, Hero.Geralt, "geralt_of_rivia", Tag.Soldier);
        H("ciri", "Cirilla", 20, Row.Close, Hero.Ciri, "cirilla_fiona_elen_riannon", Tag.Bandit, Ability.Ambush, Tag.Mage);
        H("yennefer", "Vengerbergli Yennefer", 19, Row.Ranged, Hero.Yennefer, "yennefer_of_vengerberg", Tag.Mage);
        H("triss", "Triss Merigold", 18, Row.Ranged, Hero.Triss, "triss_merigold", Tag.Mage);
        H("jaskier", "Jaskier", 18, Row.Close, Hero.Jaskier, "dandelion", Tag.Noble);
        H("vesemir", "Vesemir", 20, Row.Close, Hero.Vesemir, "vesemir", Tag.Soldier);
        H("zoltan", "Zoltan Chivay", 19, Row.Close, Hero.Zoltan, "zoltan_chivay", Tag.Dwarf);
        H("eredin", "Eredin", 21, Row.Close, Hero.Eredin, "imlerith", Tag.Monster);
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
    /// <summary>YrdenMode 3: simya + Aard kesilir, pusudan inen kartlar bu kadar zayıflar (iptal yerine).</summary>
    public static double YrdenAmbushPct = 0.5;
    /// <summary>Temel pusu yeri (Haydut 2 eşiği +1).</summary>
    public static int AmbushBase = 2;
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
    public static int[] Breakpoints(Tag t) => t switch { Tag.Bandit => Bp234, Tag.Mage or Tag.Dwarf => Bp235, _ => Bp246 };
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
    public static readonly double[] HeroOdds = { 0, 0, 0, 0, 0.0025, 0.005, 0.01, 0.015, 0.02, 0.025 };
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
            _ => "2 Odak · Rakibin bu penceredeki Aard'ını keser; pusudan inmek isteyen kartı bu satırda indirmez (son satır dışında sonra inebilir; son satırda hiç inmez). Pusu kendiliğinden inmez, seçilmeli.",
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
    public static List<string> LiveNotes(CardDef d, int? prev, Row? eredinRow, Row? foeEredinRow, int banditLevel)
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
            l.Add($"Eredin bu savaş: rakibin {Name(fr)} satırındaki birimler −%30.");
        if (!d.IsHero && eredinRow is { } er)
            l.Add($"Rakibin Eredin'i bu savaş senin {Name(er)} satırını vurdu: o satırdaki kahraman olmayan birimler −%30 (bu kart oradaysa).");
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

    /// <summary>Etiket eşik bonuslarının açıklaması (eşikler: <see cref="Tuning.Breakpoints"/>).</summary>
    public static string[] Thresholds(Tag t) => t switch
    {
        Tag.Monster => new[] { "Canavarlar +1", "Canavarlar +2", "Canavarlar +3" },
        Tag.Soldier => new[] { "Askerler havada 2", "Askerler havadan etkilenmez", "Askerler +2, havadan etkilenmez" },
        Tag.Mage => new[] { "İşaretler +%25", "+1 Odak, işaretler +%50", "Savaş başına bir işaret bedava" },
        Tag.Bandit => new[] { "Pusu yeri 3 kart", "Pusudan inen +%25", "Pusudan inen +%50 · Pusu Yrden'den etkilenmez" },
        Tag.Noble => new[] { "İlk yenileme bedava", "Round başı +1 altın", "Round başı +3 altın" },
        _ => new[] { "Cüceler bombalardan yarı etkilenir", "Cüceler +1, Kavurma'dan korunur", "Cücelerin satırına +%20" }
    };
}
