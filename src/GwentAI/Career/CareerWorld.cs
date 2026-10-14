namespace GwentAI.Career;

/// <summary>Haritada bir bölge. Harita resmi tüm bölgeyi kapsar; <c>Crop</c> (0-1 aralığında x, y, genişlik, yükseklik) gösterilen kısmı seçer.</summary>
public sealed record CareerRegion(string Id, string Name, string Subtitle, string Map, double[] Crop, string Pano, int NeedWins, string[] ShopFactions);

/// <summary>
/// Kariyer rakibi. <c>Decks</c>: oyunun NPC deste koleksiyonları (tek düello için bir tane; turnuvada tur başına biri seçilir).
/// <c>Face</c>: portre kimliği ("face_baron" oyunun günlük portresi, kart kimliği ya da null = destenin lideri).
/// <c>X</c>, <c>Y</c>: bölge haritasının tamamına göre konum (0-1).
/// </summary>
public sealed record CareerOpponent(
    string Id, string Region, string Name, string Title, string? Face, string[] Decks, int Level, Difficulty Ai, int Prize, double X, double Y,
    int EntryFee = 0, int Rounds = 0)
{
    public bool IsTournament => Rounds > 0;
}

/// <summary>
/// Kariyerin dünyası: orijinal oyunun Gwent oyuncuları, bölgelere dağıtılmış hâlde. Desteler oyunun kendi NPC desteleridir;
/// adlar ve kısa tanıtımlar Türkçedir. Bölgeler sırayla açılır: bir önceki bölgede <c>NeedWins</c> farklı rakibi yenmek gerekir.
/// </summary>
public static class CareerWorld
{
    public static readonly IReadOnlyList<CareerRegion> Regions = new[]
    {
        new CareerRegion("white_orchard", "White Orchard", "Her şeyin başladığı köy", "map_prolog_village", new[] { 0.06, 0.12, 0.86, 0.56 }, "pano_prolog_village", 0,
            new[] { "northern_realms" }),
        new CareerRegion("velen", "Velen", "Savaşın kemirdiği bataklıklar", "map_novigrad", new[] { 0.10, 0.47, 0.74, 0.48 }, "pano_no_mans_land", 2,
            new[] { "northern_realms", "nilfgaard" }),
        new CareerRegion("novigrad", "Novigrad", "Özgür şehrin hanları ve kumarhaneleri", "map_novigrad", new[] { 0.17, 0.16, 0.56, 0.36 }, "pano_novigrad", 3,
            new[] { "northern_realms", "nilfgaard", "scoiatael" }),
        new CareerRegion("skellige", "Skellige", "Jarlların ve druidlerin adaları", "map_skellige", new[] { 0.12, 0.16, 0.78, 0.50 }, "pano_skellige", 4,
            new[] { "skellige", "monsters" }),
        new CareerRegion("hearts_of_stone", "Hearts of Stone", "Oxenfurt yolunda tuhaf yabancılar", "map_novigrad", new[] { 0.34, 0.30, 0.56, 0.36 }, "pano_spiral", 3,
            new[] { "scoiatael", "monsters", "nilfgaard" }),
        new CareerRegion("toussaint", "Toussaint", "Şarap, şövalyeler ve büyük turnuva", "map_bob", new[] { 0.10, 0.30, 0.68, 0.44 }, "pano_toussaint", 3,
            new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters", "skellige" }),
    };

    private static CareerOpponent Duel(string region, string deck, string name, string title, string? face, int level, Difficulty ai, int prize, double x, double y) =>
        new(deck, region, name, title, face, new[] { deck }, level, ai, prize, x, y);

    public static readonly IReadOnlyList<CareerOpponent> Opponents = new[]
    {
        // White Orchard
        Duel("white_orchard", "tutorialDeck", "White Orchard Hancısı", "Köy hanının sahibi. Masaya ilk oturduğun kişi.", "face_none", 1, Difficulty.Easy, 15, 0.52, 0.50),
        Duel("white_orchard", "NilfPrologue", "Nilfgaardlı Subay", "Garnizonda canı sıkılan bir subay; kaybetmeyi sevmez.", null, 1, Difficulty.Easy, 25, 0.34, 0.32),

        // Velen
        Duel("velen", "CrossroadsInnkeeper", "Crossroads Hancısı", "Velen'in yorgun yolcularına bira ve Gwent sunar.", "face_none", 1, Difficulty.Normal, 30, 0.47, 0.62),
        Duel("velen", "BoatBuilder", "Tekne Ustası", "Bataklık kıyısında tekne yapar, masada acımaz.", "face_none", 2, Difficulty.Normal, 35, 0.28, 0.80),
        Duel("velen", "ScoiaTrader", "Gezgin Tüccar", "Elflerle iş tutan, her şeyi satan bir tüccar.", "face_none", 2, Difficulty.Normal, 35, 0.67, 0.73),
        Duel("velen", "Roche", "Vernon Roche", "Temeria'nın Mavi Çizgiler komutanı.", "face_roche", 2, Difficulty.Normal, 45, 0.63, 0.56),
        Duel("velen", "Baron", "Kanlı Baron", "Velen'in kendini lord ilan etmiş hâkimi; Gwent'e tutkun.", "face_baron", 2, Difficulty.Normal, 50, 0.34, 0.65),

        // Novigrad
        Duel("novigrad", "Zoltan", "Zoltan Chivay", "Geralt'ın eski cüce dostu. Sert oynar ama hile yapmaz.", "face_zoltan", 2, Difficulty.Normal, 45, 0.48, 0.30),
        Duel("novigrad", "Lambert", "Lambert", "Kaer Morhen'in huysuz cadı avcısı.", "face_lambert", 2, Difficulty.Normal, 45, 0.28, 0.37),
        Duel("novigrad", "Thaler", "Thaler", "Temeria'nın ağzı bozuk casus şefi.", "thaler", 2, Difficulty.Normal, 45, 0.56, 0.39),
        Duel("novigrad", "VimmeVivaldi", "Vimme Vivaldi", "Vivaldi Bankası'nın cüce bankeri.", "face_vivaldi", 3, Difficulty.Normal, 55, 0.35, 0.40),
        Duel("novigrad", "MarkizaSerenity", "Markiz Serenity", "Zarif bir Novigrad soylusu; masada acımasız.", "face_none", 3, Difficulty.Normal, 55, 0.44, 0.43),
        Duel("novigrad", "Sjusta", "Sjusta", "Liman meyhanelerinin tanınmış oyuncusu.", "face_none", 3, Difficulty.Normal, 50, 0.38, 0.48),
        Duel("novigrad", "Stjepan", "Stjepan", "Büyük şehrin usta Gwentçilerinden.", "face_none", 3, Difficulty.Normal, 50, 0.50, 0.47),
        Duel("novigrad", "Dijkstra", "Sigi Reuven", "Novigrad yeraltının efendisi. Gerçek adını herkes bilir.", "face_dijkstra", 3, Difficulty.Hard, 60, 0.40, 0.33),
        Duel("novigrad", "CardProdigy", "Kart Dâhisi", "Kimsenin yenemediği genç yetenek.", "face_none", 3, Difficulty.Hard, 60, 0.62, 0.28),
        new CareerOpponent("passiflora", "novigrad", "High Stakes", "Novigrad'ın en büyük Gwent turnuvası. Üç maç, tek eleme.", "face_none",
            new[] { "NKTournament", "NilfTournament", "ScoiaTournament", "NMLTournament" }, 3, Difficulty.Hard, 400, 0.31, 0.25, EntryFee: 100, Rounds: 3),

        // Skellige
        Duel("skellige", "Crach", "Crach an Craite", "Kaer Trolde'nin jarlı. Kartta da savaştaki gibi.", "face_crach", 3, Difficulty.Hard, 65, 0.50, 0.40),
        Duel("skellige", "Mousesack", "Ermion", "Adaların bilge druidi.", "face_mousesack", 3, Difficulty.Hard, 65, 0.63, 0.47),
        Duel("skellige", "LugosTheMad", "Deli Lugos", "Gürültücü bir klan reisi. Adı boşuna verilmemiş.", "face_lugosmad", 3, Difficulty.Hard, 60, 0.31, 0.56),
        Duel("skellige", "Gremista", "Gremist", "Huysuz bir simyacı; iksirleri gibi kartları da sert.", "face_none", 3, Difficulty.Normal, 55, 0.55, 0.58),
        Duel("skellige", "Hermit", "Münzevi", "Adaların ıssız bir köşesinde yaşayan yaşlı adam.", "face_none", 3, Difficulty.Normal, 55, 0.72, 0.36),
        Duel("skellige", "Olivier", "Olivier", "Liman hanının sahibi.", "face_none", 3, Difficulty.Normal, 50, 0.41, 0.47),

        // Hearts of Stone
        Duel("hearts_of_stone", "Shani", "Shani", "Oxenfurt'lu genç hekim.", "face_shani", 3, Difficulty.Normal, 55, 0.62, 0.52),
        Duel("hearts_of_stone", "Olgierd", "Olgierd von Everec", "Taş kalpli haydut reisi.", "face_olgierd", 3, Difficulty.Hard, 65, 0.49, 0.42),
        Duel("hearts_of_stone", "Gambler", "Kumarbaz", "Her şeyi bir el karta yatıran gizemli bir yabancı.", "face_none", 3, Difficulty.Hard, 60, 0.73, 0.40),
        Duel("hearts_of_stone", "Halflings", "Buçukluk Kardeşler", "Küçük boylu, büyük hırslı iki kardeş.", "face_none", 3, Difficulty.Normal, 50, 0.43, 0.55),
        Duel("hearts_of_stone", "CircusGwentAddict", "Panayır Tutkunu", "Gezici panayırın Gwent delisi.", "face_none", 3, Difficulty.Normal, 50, 0.81, 0.56),

        // Toussaint
        Duel("toussaint", "NKHard", "Toussaint Şövalyesi", "Erdemleri kadar kartları da parlak.", null, 3, Difficulty.Hard, 60, 0.30, 0.40),
        Duel("toussaint", "NilfHard", "İmparatorluk Elçisi", "Beauclair sarayında Nilfgaard'ın sesi.", null, 3, Difficulty.Hard, 60, 0.56, 0.57),
        Duel("toussaint", "ScoiaHard", "Orman Elfi", "Gaunter tepelerinden inen bir elf.", null, 3, Difficulty.Hard, 60, 0.25, 0.53),
        Duel("toussaint", "NMLHard", "Canavar Avcısı", "Canavar kartlarını kendi avladığını söyler.", null, 3, Difficulty.Hard, 60, 0.62, 0.42),
        Duel("toussaint", "SkelHard", "Skellige'li Korsan", "Dükalığa uzak denizlerden gelmiş.", null, 3, Difficulty.Hard, 60, 0.70, 0.64),
        new CareerOpponent("toussaint_tournament", "toussaint", "Toussaint Gwent Turnuvası", "Beauclair'de büyük final: dört maç, tek eleme.", "face_none",
            new[] { "ScoiaTournament2", "NMLTournament2", "NKTournament2", "NilfTournament2", "SkelTournament2" }, 3, Difficulty.Hard, 800, 0.45, 0.65, EntryFee: 200, Rounds: 4),
    };

    public static CareerRegion Region(string id) => Regions.First(r => r.Id == id);
    public static CareerOpponent Opponent(string id) => Opponents.First(o => o.Id == id);
    public static IEnumerable<CareerOpponent> In(string region) => Opponents.Where(o => o.Region == region);
}
