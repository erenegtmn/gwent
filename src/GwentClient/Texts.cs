using GwentCore;

namespace GwentClient;

/// <summary>Kullanıcıya dönük Türkçe metinler.</summary>
public static class Texts
{
    public static string Faction(string f) => f switch
    {
        "northern_realms" => "Kuzey Krallıkları",
        "nilfgaard" => "Nilfgaard İmparatorluğu",
        "scoiatael" => "Scoia'tael",
        "monsters" => "Canavarlar",
        "skellige" => "Skellige",
        _ => "Tarafsız"
    };

    public static string FactionShort(string f) => f switch
    {
        "northern_realms" => "Kuzey",
        "nilfgaard" => "Nilfgaard",
        "scoiatael" => "Scoia'tael",
        "monsters" => "Canavar",
        "skellige" => "Skellige",
        _ => "Tarafsız"
    };

    public static string Row(RowKind r) => r switch { RowKind.Close => "Yakın dövüş", RowKind.Ranged => "Menzilli", _ => "Kuşatma" };

    public static string AbilityLabel(string a) => a switch
    {
        "spy" => "Casus",
        "medic" => "Medic",
        "muster" => "Toplanma",
        "bond" => "Bağ",
        "morale" => "Moral",
        "horn" => "Boynuz",
        "scorch" => "Yak",
        "scorch_c" => "Yak·Yakın",
        "scorch_r" => "Yak·Menzil",
        "scorch_s" => "Yak·Kuşatma",
        "avenger" => "İntikam",
        "berserker" => "Berserker",
        "mardroeme" => "Mardroeme",
        "frost" => "Don",
        "fog" => "Sis",
        "rain" => "Yağmur",
        "storm" => "Fırtına",
        "clear" => "Açık hava",
        "decoy" => "Yem",
        _ => a
    };

    public static string AbilityText(string a) => a switch
    {
        "spy" => "Casus: Rakibin sahasına konur (puanı rakibe yazılır). 2 kart çekersin.",
        "medic" => "Medic: Mezarlığından kahraman olmayan bir birimi seçip hemen oynarsın.",
        "muster" => "Toplanma: Elindeki ve destendeki aynı gruptan tüm kartlar sahaya iner.",
        "bond" => "Sıkı Bağ: Aynı isimli kartlarla yan yana olunca gücü kart sayısıyla çarpılır.",
        "morale" => "Moral: Aynı sıradaki diğer birimlere +1 verir.",
        "horn" => "Boynuz: Aynı sıradaki diğer birimlerin gücünü ikiye katlar.",
        "scorch" => "Yak: Sahadaki en güçlü (kahraman olmayan) birim(ler)i yok eder.",
        "scorch_c" => "Yak: Rakibin Yakın dövüş sırası toplam 10 ve üzerindeyse en güçlüsünü yok eder.",
        "scorch_r" => "Yak: Rakibin Menzilli sırası toplam 10 ve üzerindeyse en güçlüsünü yok eder.",
        "scorch_s" => "Yak: Rakibin Kuşatma sırası toplam 10 ve üzerindeyse en güçlüsünü yok eder.",
        "avenger" => "İntikam: Sahadan kalkınca yerine güçlü bir birim gelir.",
        "berserker" => "Berserker: Aynı sırada Mardroeme varsa ayıya dönüşür.",
        "mardroeme" => "Mardroeme: Aynı sıradaki Berserker'ları dönüştürür.",
        "frost" => "Don: Yakın dövüş sırasındaki tüm birimlerin gücünü iki oyuncu için de 1 yapar.",
        "fog" => "Sis: Menzilli sıradaki tüm birimlerin gücünü iki oyuncu için de 1 yapar.",
        "rain" => "Yağmur: Kuşatma sırasındaki tüm birimlerin gücünü iki oyuncu için de 1 yapar.",
        "storm" => "Fırtına: Menzilli ve Kuşatma sıralarını birlikte 1'e düşürür.",
        "clear" => "Açık hava: Tüm hava etkilerini kaldırır.",
        "decoy" => "Yem: Sahandaki kahraman olmayan bir birimle yer değiştirip onu eline alırsın.",
        _ => a
    };

    public static string RowOf(CardDef d) => d.Row switch
    {
        "close" => "Yakın dövüş",
        "ranged" => "Menzilli",
        "siege" => "Kuşatma",
        "agile" => "Yakın veya Menzilli",
        _ => ""
    };

    /// <summary>Liderlerin yeteneği (Türkçe).</summary>
    public static string LeaderText(CardDef d) => d.Id switch
    {
        "foltest_king_of_temeria" => "Destenden bir Sis kartı seçip hemen oynar.",
        "foltest_lord_commander_of_the_north" => "Tüm hava etkilerini kaldırır.",
        "foltest_the_siegemaster" => "Kuşatma sıran ikiye katlanır (orada Boynuz yoksa).",
        "foltest_the_steel_forged" => "Rakibin Kuşatma toplamı 10+ ise en güçlü Kuşatma birimini yok eder.",
        "foltest_son_of_medell" => "Rakibin Menzilli toplamı 10+ ise en güçlü Menzilli birimini yok eder.",
        "emhyr_var_emreis_his_imperial_majesty" => "Destenden bir Yağmur kartı seçip hemen oynar.",
        "emhyr_var_emreis_emperor_of_nilfgaard" => "Rakibin elinden rastgele 3 kartı görürsün.",
        "emhyr_var_emreis_the_white_flame" => "Rakibin lider yeteneğini iptal eder.",
        "emhyr_var_emreis_the_relentless" => "Rakibin mezarlığından bir kartı eline alırsın.",
        "emhyr_var_emreis_invader_of_the_north" => "Geri getirme yetenekleri (Medic) rastgele birim getirir (iki oyuncu için).",
        "eredin_commander_of_the_red_riders" => "Yakın dövüş sıran ikiye katlanır (orada Boynuz yoksa).",
        "eredin_bringer_of_death" => "Mezarlığından bir kartı eline alırsın.",
        "eredin_destroyer_of_worlds" => "Elinden 2 kart atıp destenden istediğin 1 kartı çekersin.",
        "eredin_king_of_the_wild_hunt" => "Destenden istediğin bir hava kartını hemen oynar.",
        "eredin_breacc_glas_the_treacherous" => "Casus kartların gücü iki oyuncu için de ikiye katlanır.",
        "francesca_findabair_queen_of_dol_blathanna" => "Rakibin Yakın toplamı 10+ ise en güçlü Yakın birimini yok eder.",
        "francesca_findabair_the_beautiful" => "Menzilli sıran ikiye katlanır (orada Boynuz yoksa).",
        "francesca_findabair_daisy_of_the_valley" => "Oyun başında fazladan 1 kart çekersin.",
        "francesca_findabair_pureblood_elf" => "Destenden bir Don kartı seçip hemen oynar.",
        "francesca_findabair_hope_of_the_aen_seidhe" => "Çevik birimleri en çok puan verecek sıraya taşır.",
        "crach_an_craite" => "İki oyuncunun mezarlığındaki tüm kartları destelerine karıştırır.",
        "king_bran" => "Birimler kötü havada gücünün yalnız yarısını kaybeder.",
        _ => d.Effect ?? ""
    };

    /// <summary>Oyuncu etiketi: 0 = sen, 1 = rakip.</summary>
    public static string Who(int player, int me = 0) => player == me ? "Sen" : "Rakip";
}
