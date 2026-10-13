# Gwent (Witcher 3) — Kural ve Sistem Belirtimi

> Kaynak: oyunun bilinen kuralları + bağımsız bir yeniden yapımın kod/veri okuması (çapraz kontrol). `⚠️` işaretli yerler **doğrulanmalı**.
> Kart verisi: [`data/cards.json`](data/cards.json) (okunabilir hâli: [`KARTLAR.md`](KARTLAR.md)).

## 0. Verilen Kararlar (2026-10-12)

| Konu | Karar |
|---|---|
| Kapsam | Temel oyun + Hearts of Stone + Blood and Wine (**Skellige dahil**) |
| Kartlar | **Tümü** (bkz. KARTLAR.md) |
| Platform | **Masaüstü** |
| Rakip | İlk etap **yapay zekâ**; asıl hedef **iki kişinin online maçı** |
| Sadakat | **Orijinal oyuna sadık** |
| İstemci | **WPF** (hepsi Windows) · sunucu **ASP.NET Core + SignalR** · çekirdek **C# sınıf kütüphanesi** |
| Dağıtım | Yalnız arkadaşlar arası, **yayın yok**. Orijinal oyunun görsel/sesi kullanılabilir (kullanıcının kendi kopyasından çıkarılır, depoya konmaz) |
| Online | **Oda koduyla** giriş, eşleştirme/sıralama yok |
| Hesap | Ufak hesap: **takma ad + PIN**, kişi başı **istatistik** (kazanma/kaybetme, fraksiyon vb.) |
| Süre | **Tur süresi yok** |

**Sonuç:** mimari baştan online'a göre kurulur. Kural motoru saf ve deterministik olur, sunucu otoriter olur (§14).

## 1. Genel Çerçeve

- 2 oyuncu, sıra tabanlı, **en iyi 3 raund** (her oyuncunun 2 "mücevheri" var).
- Raundu kazanan, toplam gücü yüksek olandır. Kaybeden 1 mücevher kaybeder.
- **Beraberlikte iki oyuncu da 1 mücevher kaybeder** (Nilfgaard istisnası: §7).
- 2 mücevheri de biten oyuncu oyunu kaybeder. İkisi aynı anda biterse **oyun berabere**.
- Oyun en fazla 3 raund sürer.

## 2. Saha

Her oyuncunun 3 sırası var (rakiple karşılıklı):

| Sıra | Tipik birimler |
|---|---|
| Yakın Dövüş (Close) | Piyade, şövalye |
| Menzilli (Ranged) | Okçu, büyücü |
| Kuşatma (Siege) | Mancınık, balista |

- Her sıranın bir **Boynuz yuvası** vardır (yalnız o sırada 1 Komutan Boynuzu).
- Bir kartın hangi sıraya oynanacağı kartın üzerinde yazar. **Çevik (Agile)** birimler Yakın veya Menzilli seçilebilir.
- Hava kartları ortak bir alanda durur ve **iki oyuncuyu da** etkiler.
- Mezarlık: oynanan/ölen kartlar buraya gider (Medic ve bazı liderler kullanır).

## 3. Deste ve Başlangıç

- **Deste kuralı:** en az **22 birim kartı**, en fazla **10 özel kart**, **1 lider**. Birim sayısının üst sınırı yok.
- Lider desteden çekilmez; ayrı durur (§8).
- Deste bir **fraksiyona** aittir; Tarafsız (Neutral) kartlar her destede kullanılabilir.
- Başlangıç: desteden karıştırılıp **10 kart** çekilir. Oyuncu **2 kartı değiştirebilir** (mulligan: kartı desteye koyup yenisini çeker).
- İlk oynayan (raund 1): yazı-tura. Scoia'tael fraksiyonu bunu kendi seçer (§7).
- Raund 2 ve 3'ü **bir önceki raundu kazanan başlatır**; raund berabere bittiyse aynı oyuncu yine başlar. ⚠️ (bağımsız yeniden yapımdan; oyunda doğrulanacak)
- **Her tur kart çekilmez.** 10 kartla 3 raund oynanır; ek kart yalnız yeteneklerden gelir (Spy, fraksiyon yeteneği vb.). Bu yüzden kart tasarrufu oyunun asıl stratejisidir.
- El limiti yoktur.

## 4. Tur Akışı

Sırası gelen oyuncu **şunlardan birini** yapar:

1. **Bir kart oynar** (birim veya özel), ya da
2. **Pas geçer.**

- Pas geçen oyuncu o raundun geri kalanında **oynayamaz**.
- Rakip pas geçtiyse diğer oyuncu istediği kadar oynayıp raundu kapatır (genelde önde olduğu için pas geçer).
- **Raund sonu:** ikisi de pas geçince toplam güçler karşılaştırılır; tahta temizlenir, tüm kartlar mezarlığa gider (istisnalar: Monsters §7, Skellige §7).
- **Lider yeteneği** tur sayılır, sırada kart oynamak yerine kullanılır (§8).

## 5. Güç Hesabı

Bir sıranın gücü = o sıradaki birimlerin **nihai güçlerinin toplamı**. Toplam raund gücü = üç sıranın toplamı.

Nihai güç şu **sırayla** hesaplanır:

1. **Taban güç** (kartın üzerindeki sayı)
2. **Hava:** sıradaki (kahraman olmayan) tüm birimlerin gücü **1**'e sabitlenir
3. **Tight Bond:** aynı isimli bitişik/aynı sıradaki kart sayısı kadar **çarpılır** (2 kart → ×2, 3 kart → ×3)
4. **Morale Boost:** sıradaki diğer birimlere **+1** (kartın kendisine değil)
5. **Boynuz:** sıranın tüm birimleri **×2**

**Kahraman (altın) kartlar** hiçbir hesap adımından etkilenmez: hava, Tight Bond, Morale, Boynuz, Scorch, Decoy — hiçbiri onlara işlemez. Gücü kart üzerindeki sayıdır. (Kendi yetenekleri çalışır.)

## 6. Yetenekler ve Özel Kartlar

### Birim yetenekleri

| Yetenek | Etki |
|---|---|
| **Spy (Casus)** | Rakibin sahasına oynanır (gücü rakibe yazar). Oynayan oyuncu desteden **2 kart çeker**. |
| **Medic** | Oynandığında kendi mezarlığından **kahraman olmayan** bir birimi seçip hemen oynar (o birimin yeteneği de çalışır). |
| **Muster (Toplanma)** | Oynandığında elinde ve destende aynı muster grubundaki **tüm kartlar** sahaya iner. |
| **Tight Bond** | §5. Aynı isimli kartlarla çarpan olur. |
| **Morale Boost** | §5. Sıradaki diğer birimlere +1. |
| **Scorch (birim)** | Oynandığında yalnızca kartın belirttiği sıra/koşulda en güçlü birim(ler)i yok eder (örn. rakibin yakın dövüşü toplam ≥10 ise o sıranın en güçlüsü). |
| **Agile (Çevik)** | Yakın veya Menzilli sıraya oynanabilir. |
| **Commander's Horn (birim)** | Bazı birimler (örn. Dandelion) kendi sırasına Boynuz etkisi verir. |
| **Berserker** | Sırada Mardroeme varsa güçlü bir forma dönüşür (örn. Berserker → Vildkaarl). |
| **Hero (Kahraman)** | §5. Hiçbir özel karttan etkilenmez, Medic ile geri getirilemez. |

### Özel kartlar (desteden max 10)

| Kart | Etki |
|---|---|
| **Decoy** | Kendi sahandaki **kahraman olmayan** bir birimi elinize geri alır; Decoy onun yerinde durur (Spy/Medic'i tekrar kullanmak için ideal). |
| **Commander's Horn** | Seçilen sıradaki birimleri ×2 yapar (sıra başına 1). |
| **Scorch** | **Her iki sahadaki** en güçlü kahraman olmayan birim(ler)i yok eder (eşitlikte hepsi). |
| **Mardroeme** | Sıradaki Berserker'ları dönüştürür. |
| **Hava (Biting Frost, Impenetrable Fog, Torrential Rain)** | Sırasıyla **Yakın / Menzilli / Kuşatma** sırasını etkiler: iki sahada da o sıradaki birim gücü 1 olur. |
| **Clear Weather** | Tüm hava etkilerini kaldırır. |
| **Skellige Storm** | Menzilli **ve** Kuşatma'yı birlikte etkiler (Skellige / DLC). |

## 7. Fraksiyonlar

| Fraksiyon | Fraksiyon yeteneği |
|---|---|
| **Northern Realms** | Bir raundu kazanınca **desteden 1 kart çeker**. |
| **Nilfgaard** | **Beraberlikte raundu kazanır** (iki Nilfgaard karşılaşırsa beraberlik kalır). |
| **Scoia'tael** | Oyunu **kimin başlatacağını seçer**. |
| **Monsters** | Raund sonunda sahadan **rastgele 1 birim** kalır (temizlenmez). |
| **Skellige** *(Blood & Wine)* | **3. raundun başında** mezarlıktan **2 rastgele kart** sahaya iner. ⚠️ |

## 8. Liderler

- Oyun başına **tek kullanım**; kart oynamak yerine bir tur harcar. Kullanılmış lider tekrar kullanılamaz.
- Yetenek kullanılamaz durumdaysa (geçerli hedef yoksa) kullanılamaz.
- Tüm liderler ve etkileri: [KARTLAR.md](KARTLAR.md) (Northern Realms, Nilfgaard, Monsters, Scoia'tael: 5'er; Skellige: 2).

## 9. Özel Durumlar

Kesinleşen kenar vakalar §11'dedir. Doğrulanacak kalanlar §13'te.

## 10. Kazanma / Bitiş

```
Raund sonu: güç(A) > güç(B)  → B bir mücevher kaybeder
           güç(A) = güç(B)  → ikisi de kaybeder (Nilfgaard varsa o kazanır)
Oyun sonu : bir oyuncunun 0 mücevheri kalır → kaybeder
           ikisinin de 0 mücevheri kalır   → beraberlik
```


## 11. Orijinale Sadakat — Kesinleşen Ayrıntılar

Kart verisi ve yeniden yapım kodu okunarak netleşen kurallar.

- **Güç ve hava sırası** §5'teki gibi. Hava yalnızca **kahraman olmayan** birimleri 1'e çeker.
- **Scorch (özel kart):** iki sahadaki **tüm** kahraman olmayan birimler arasında en güçlüyü yok eder; eşitlikte hepsi gider. Oynanan kartın kendisi sayılmaz.
- **Satır kısıtlı birim Scorch'ları** (Villentretenmerth = Yakın, Toad = Menzilli, Schirrú = Kuşatma): rakibin **o sıradaki toplam gücü ≥ 10 ise** o sıranın en güçlü birim(ler)i yok olur. **Clan Dimun Pirate** ise tüm sahadaki en güçlüyü yok eden genel Scorch'tur.
- **Lider Scorch'ları** (Foltest Steel-Forged / Son of Medell, Francesca Queen) aynı ≥10 kuralını kullanır.
- **Lider "boynuz"ları** (Siegemaster, Commander of the Red Riders, The Beautiful) o sırada Komutan Boynuzu varsa **etkisizdir** (çift boynuz yok). Sıra başına tek boynuz.
- **Spy:** kart rakip sahaya konur, gücü rakibe yazılır; oynayan 2 kart çeker (deste boşsa çekebildiği kadar). **Eredin: The Treacherous** Spy kartlarının gücünü iki oyuncu için de ikiye katlar.
- **Medic:** kendi mezarlığından **kahraman olmayan birim** seçilir ve hemen oynanır; seçilenin yeteneği çalışır. Çevik birimde sıra seçilir. **Emhyr: Invader of the North** varsa seçim rastgele olur (iki oyuncu için de).
- **Muster:** aynı **gruptaki** tüm kartlar elden ve desteden sahaya iner. Grup `musterGroup` alanıdır, kart adı değil (Vampire Bruxa ↔ Vampire Katakan; Cerys ↔ Clan Drummond Shield Maiden; Gaunter O'Dimm ↔ Darkness).
- **Avenger:** kart sahadan **kalktığında** (ölüm/Decoy/Scorch fark etmez) yerine jeton gelir: Cow → Bovine Defense Force (8, Yakın), Kambi → Hemdall (11, kahraman, Yakın).
- **Berserker + Mardroeme:** aynı sırada Mardroeme (Ermion veya Mardroeme kartı) varsa Berserker → Transformed Vildkaarl (14, Morale Boost), Young Berserker → Transformed Young Vildkaarl (8, Tight Bond).
- **Skellige Storm:** Menzilli **ve** Kuşatma sırasını birlikte 1'e çeker; Clear Weather kaldırır.
- **King Bran:** havada kahraman olmayan birimler **yarı güç** kaybeder.
- **Tight Bond:** aynı **isimli** kartlar; çarpan = o sıradaki aynı isimli kart sayısı.
- **Çevik (Agile):** Yakın veya Menzilli; **konduktan sonra taşınamaz** (Francesca: Hope lideri hariç).
- **Nilfgaard beraberlikte kazanır**; iki Nilfgaard karşılaşırsa beraberlik kalır.
- **Pas:** raunddan çıkmaktır; kalan oyuncu istediği kadar oynayıp raundu kendi kapatır.
- **Fraksiyonlar:** Monsters raund sonu sahadaki **birimlerden** rastgele birini bırakır; Northern Realms raundu kazanınca 1 kart çeker; Skellige 3. raund başında mezarlıktaki **birimlerden** 2 rastgele kartı sahaya indirir; Scoia'tael ilk oyuncuyu seçer.
- **Lider:** oyun başına bir kez, bir tur harcar; **The White Flame** rakip liderini iptal eder. **Francesca: Daisy** oyun başında fazladan 1 kart çeker.

## 12. Orijinalden Sapma / Next-Gen Notları

- **Roach** (tarafsız, 3, Yakın) ve **Geralt / Ciri'nin Muster: Roach** yeteneği yalnız **yeni nesil (next-gen) güncellemesinde** vardır. Orijinal sadakat için varsayılan: **kapalı** (`edition: nextgen`; Geralt ve Ciri'de `nextgenAbilities`). İleride ayar olarak açılabilir.
- *Poor Fucking Infantry* oyundaki gerçek addır; olduğu gibi bırakıldı.

## 13. Veri Kaynakları ve Güven Düzeyi

| Veri | Kaynak | Güven |
|---|---|---|
| Kart adları, fraksiyon, sürüm (base / HoS / B&W) | Fextralife, PocketTactics, game-checklists (3 bağımsız liste) | Yüksek |
| Güç, sıra, yetenek, kopya sayısı | Bağımsız bir Witcher 3 yeniden yapımının kart tablosu (asundr/gwent-classic) | Orta, **oyundaki koleksiyonla karşılaştırılmadı** |
| Lider etkileri | Aynı yeniden yapım, Fextralife ile çapraz kontrol | Yüksek (1 çelişki, aşağıda) |

Kopyalanan yalnızca kart **olgularıdır** (isim, güç, sıra). Yeniden yapımın kodu kopyalanmadı; lisansı belirtilmemiş.

### Doğrulanacaklar ⚠️
1. **Foltest: Lord Commander of the North ↔ The Steel-Forged:** oyun verisi etkileri doğruluyor (hava temizleme ve Kuşatma yakma ayrı liderlerde, resim eşlemesi de ona göre yapıldı); iki bağımsız kaynak "Lord Commander = hava temizler" diyor. Oyunun yerelleştirme dosyası (`en.w3strings`) bu sürümde okunamadığı için **isim** eşleşmesi doğrudan doğrulanamadı. Düşük risk.
2. **Clan Brokvar Archer:** yeniden yapımda 2 kopya, bir checklist 3. Oyun veri tabanında tek kayıt var (B&W kartlarının kopya sayısı kayıtta değil, envanter öğesinde). Veride **2**.
3. **Kopya sayıları:** Kuzey/Nilfgaard/Scoia'tael/Canavar kartlarında oyun veri tabanıyla **doğrulandı** (tek fark: Poor Fucking Infantry 4 → **3** olarak düzeltildi). Yalnız Skellige ve Hearts of Stone kartlarının kopya sayısı (oyunda envanter öğesiyle tutuluyor) doğrulanamadı.
4. Medic ile geri getirilen **Spy** (rakip sahaya gider, kart çeker) ve Decoy ile geri alınan Spy/Medic'in yeteneğinin yeniden tetiklenmesi: yeniden yapımda **evet**. Oyunda doğrulanmalı.
5. Raund başlatma kuralı (§3).

## 14. Mimari (kesinleşti)

**İlke:** kural motoru **saf, deterministik ve arayüzsüz**. Sunucu ve istemci aynı kütüphaneyi kullanır.

```
GwentCore (C#)  ──►  GwentServer (ASP.NET Core + SignalR + SQLite)   ──►  istatistikler, odalar
      ▲                          ▲
      │                          │ WebSocket (eylem ↔ görünüm)
      └──────────  GwentClient (WPF)  ◄── yerel YZ oyunu da aynı çekirdeği kullanır
```

- **Sunucu otoriterdir.** Oyuncu yalnızca eylem gönderir (`PlayCard`, `Pass`, `UseLeader`, `Choose`, `Mulligan`, `ChooseFirst`), durumu sunucu hesaplar. İstemciye yalnız görmesi gereken durum gider (rakibin eli ve destesi gitmez; `peek` olayı `Visible` ile yalnız sahibine).
- **Tohumlu rastgelelik:** aynı desteler + tohum + eylem dizisi = aynı oyun. Maç kaydı tekrar oynatılabilir (testle doğrulandı).
- **Bekleyen seçim modeli:** Medic, çevik birimin sırası, Eredin'in seçimleri `PendingChoice` olarak durur; yalnız o oyuncunun `Choose`'u kabul edilir.
- **Kart verisi sürümlü:** `CardCatalog.Hash`; istemci ve sunucu maç başında karşılaştırır.
- **Oda kodu:** sunucu rastgele kısa kod üretir, ikinci oyuncu kodla katılır; desteleri sunucu `DeckValidator` ile doğrular.
- **Bağlantı kopması:** oyun durumu sunucuda durur; yeniden bağlanan oyuncu mevcut görünümü alır. Tur süresi olmadığı için rakip bekler; terk edilen oda için ayrı bir "pes et / oda zaman aşımı" kuralı eklenecek.

## 15. Durum ve Yol Haritası

| # | İş | Durum |
|---|---|---|
| 1 | **Çekirdek motor** (`src/GwentCore`): tüm kartlar, yetenekler, 21 lider, raund/oyun akışı, deste doğrulama | ✅ bitti |
| 2 | **Testler** (`tests/GwentCore.Tests`): 132 test; kural testleri + 5×5 fraksiyon çifti rastgele oyun (kart kaybı/çoğalması, determinizm) + yapay zekâ testleri | ✅ bitti |
| 3 | Kart doğrulama (§13 ⚠️ maddeleri) | ⏳ oyunla karşılaştırılacak |
| 4 | **Yapay zekâ** (`src/GwentAI`) + **arena aracı** (`src/GwentArena`) | ✅ bitti (v1) |
| 5 | **WPF istemci** (`src/GwentClient`): menü, saha, el, seçim pencereleri, yapay zekâya karşı yerel oyun, **yer tutucu kartlarla** | ✅ v1 (animasyon, ses, kart resmi, deste kurucu yok) |
| 6 | **Sunucu**: oda kodu, SignalR, otoriter doğrulama, yeniden bağlanma | ⏳ |
| 7 | **Hesap + istatistik** (SQLite) | ⏳ |
| 8 | **Varlık çıkarma** (kendi oyun kopyandan kart resmi, masa, simgeler, müzik, ses) + **orijinal görünüm** | ✅ (`tools/extract_assets.py`, bkz. §19) |
| 9 | Deste kurucu ✅ · kayıtlı desteler ✅ · animasyon ✅ · yer tutucu ses ✅ · maç tekrarı ⏳ | ✅ (tekrar hariç) |

### Motorda alınan kararlar (kaynakta belirsiz olanlar) ⚠️
Bunlar yeniden yapımın davranışına göre uygulandı, oyunla doğrulanınca değişebilir. Her biri bir testle kayıtlı:
1. **Casus kart ölürse** kartın bulunduğu **saha tarafının** (yani rakibin) mezarlığına gider.
2. **Avenger** (Cow, Kambi) sahadan kalkınca jeton bırakır: ölüm, Decoy ve **raund sonu temizliği** dahil. Raund sonunda bırakılan jeton bir sonraki raunda kalır.
3. **Medic ile geri gelen Spy** rakip sahaya gider ve 2 kart çektirir; **Decoy ile geri alınan** birimin yeteneği yeniden oynayınca tekrar çalışır.
4. **White Flame** rakibin liderini tümüyle iptal eder (pasif olanlar dahil: Daisy, King Bran, Treacherous, Invader).
5. **Lider kullanım kapısı:** hedefi olmayan lider (örn. destede Fog yok, rakip sırası <10 puan) **kullanılamaz**, boşa harcanmaz. Orijinalde boşa harcanıp harcanmadığı doğrulanmadı.
6. **Gerçek kart değil işaretçi:** lider boynuzu mezarlığa girmez; jetonlar (Hemdall, BDF, Vildkaarl) mezarlığa girmez.
7. **Berserker dönüşünce** asıl kart oyundan kalkar (mezarlığa gitmez).
8. **Skellige 3. raund**, Medic, Relentless, Bringer of Death: yalnız kahraman olmayan **birimler** aday.

## 16. Açık Sorular

1. **Görsel/ses çıkarma:** Witcher 3 kart resimlerinin ve seslerinin hangi dosyadan, hangi araçla çıkarılacağı (WolvenKit/oyun arşivi). Bu işi 8. adımda birlikte yaparız; çıkan dosyalar `.gitignore`'da kalır.
2. **Takma ad + PIN güvenliği:** arkadaş çevresi için PIN yeterli; yine de sunucuda hash'li tutulur.
3. **Sunucu nerede çalışacak?** Mevcut Linux sunucunda küçük bir servis olarak mı, yoksa maç sırasında senin bilgisayarında mı?
4. **Yeni nesil modu** (Roach + Geralt/Ciri Muster): motorda hazır, `GameOptions.NextGen` ile açılıyor. Odada seçenek olarak sunalım mı?

## 17. Yapay Zekâ (v1)

`HeuristicAgent` klasik **tempo** mantığıyla oynar. Hamleleri `Game.Clone()` ile oyunun **kopyası** üzerinde dener; rakibin elini ve destesini **görmez**, yalnız kart **sayısını** bilir (bekçi testi `Agent_never_inspects_the_opponents_hidden_cards` koda bakarak denetler).

**Karar sırası (kendi turunda):**
1. Kart avantajı sağlayan hamle (casus gibi) ve önde kalmayı bozmuyorsa → oynar (son raund hariç).
2. **Öndeysem:** rakip pas geçtiyse pas; fark rakibin tahmini potansiyelinin %70'ini aşıyorsa pas; yoksa en ucuz kazançlı hamle.
3. **Gerideysem:** (a) kaybetmek oyunu bitirmiyorsa ve gereken puan elimdeki potansiyelin %55'ini aşıyorsa **raundu bırakır, kartlarını saklar**; (b) tek hamlede öne geçiren **en ucuz** kartı oynar; (c) yetmiyorsa kazanç − 0,5×maliyet en yüksek hamle.
4. Kaybedince oyun biteceği (son mücevher) raundda hiç pes etmez.

**Bekleyen seçimler** kopyalanamadığı için saf sezgiyle çözülür: Medic en değerli kartı, çevik birim hava almamış / moral-boynuz etkili sırayı, Destroyer en değersiz 2 kartı atıp en değerliyi çeker, King hava kartını net etkisine göre seçer.

**Başlangıç:** en değersiz en çok 2 kartı değiştirir (aynı isimli Tight Bond çiftlerini korur). Scoia'tael'de **rakibi başlatır**.

### Ölçümler (`dotnet run --project src/GwentArena -c Release -- ...`)
| Karşılaştırma | Sonuç |
|---|---|
| AI ↔ rastgele oyuncu (5×5 fraksiyon, 60 oyun/eşleşme) | **%92** galibiyet (Monsters %97, Nilfgaard %90) |
| Kart değiştirme kapalı ↔ açık | kapalı %43,6 → **mulligan önemli** |
| 14 ayar varyantı ↔ taban (750 oyun/varyant) | hiçbiri tabanı geçemedi (%44–49): **ayarlar yerel optimumda** |
| "Göreli teslim" kuralı (toplam potansiyel karşılaştırması) | %45 → **eklendi ama kapalı** (`RelativeConcedeFactor = 0`) |

Arena modları: `<N> random|ai|ab` (fraksiyon matrisi), `sweep <N>` (ayar taraması), `trace <f0> <f1> <seed>` (tek oyunu hamle hamle yazar).

**Bilinen sınırlar (v1):** sonraki raundu planlamaz (hangi kartı hangi raunda saklayacağını bilmez), rakibin oynadığı kartlardan el tahmini yapmaz, Medic/Decoy kombinasyonlarını (casusu tekrar kullanma) bilinçli aramaz. Gerekirse sonraki sürüm bu üçüne bakar; arena ile ölçülerek eklenmeli.

## 18. WPF İstemci (v1)

**Çalıştırma:** `dotnet run --project src/GwentClient` (ya da derlenen `Gwent.exe`). Oyuncu = sen (0), rakip = yapay zekâ (1).

**Ekranlar:** Menü (fraksiyon, lider, rakip fraksiyon, yeni nesil seçeneği) → oyun. Oyunda: sol panelde iki oyuncunun bilgisi (puan, mücevher, el/deste/mezarlık, lider durumu), ortada 6 sıra + elin, sağda hava alanı, durum ve Türkçe olay günlüğü. Bir kartın üzerine gelince solda büyük önizleme ve ipucu penceresi çıkar.

**Kullanım:** Elinden bir kart seç → vurgulanan sıraya tıkla ya da “Oyna” düğmesine bas (çevik birim: sıra seç; Boynuz/Mardroeme: sıra seç; Yem: sahadan birimi seç; hava/Yak: Oyna). `Esc` seçimi kaldırır. Kart değiştirme, Medic/Eredin seçimleri, Scoia'tael ilk oyuncu seçimi ve oyun sonu pencereleri otomatik açılır.

**Yer tutucu kartlar:** fraksiyon renkli gövde, güç rozeti (hava/moral nedeniyle değişince yeşil/kırmızı), yetenek etiketleri, isim. Gerçek görseller gelince yalnız `CardControl` değişir.

**Doğrulama araçları (pencere açmadan):**
- `Gwent.exe --shot <klasör>`: menü, kart değiştirme, oyun ortası, seçili kart, Medic penceresi ve oyun sonu ekranlarını PNG'ye çizer.
- `Gwent.exe --selftest [N]`: arayüzü rastgele bir kullanıcı gibi kullanır (kart seç, sıra/Yem/hava tıkla, pas, lider, tüm seçim pencereleri) ve oyunun takılmadan bitmesini denetler. 150 oyun, 0 hata. İlk çalıştırmada gerçek bir çökme (önizleme panelinin eski ızgaradan ayrılmaması) yakalayıp düzelttirdi.

**Bilinen eksikler (v1):** animasyon ve ses yok; deste kurucu yok (fraksiyonun tüm kartlarıyla oynanır); pas için onay sorulmaz; kart resimleri yer tutucu; ayarlar/kayıt yok.

### Deste kurucu (v1)
Menüde "Desteleri düzenle…" ile açılır. Solda fraksiyonun + tarafsız kartların koleksiyonu (tıkla: ekle, sağ tık: çıkar; rozet = kullanılan/kopya), sağda deste listesi, lider seçimi, istatistik (birim ≥22, özel ≤10, kahraman, toplam güç) ve doğrulama mesajları. **Kurallar motordaki `DeckValidator`'dan gelir; arayüzde ikinci kez yazılmaz.** "Önerilen deste" (`DeckValidator.RecommendedDeck`): tüm kahramanlar + en çok 3 casus + en güçlü birimler (~24) + Decoy/Scorch/Horn/Clear; her zaman geçerli ve testle korunuyor. Desteler `%AppData%\Gwent\decks.json` içinde; yazma önce geçici dosyaya yapılır, bozuk dosya oyunu durdurmaz. Geçersiz deste menüde "geçersiz — düzenle" ile kapalı gelir. Kayıtlı deste kendi liderini taşır. **Rakip her zaman "önerilen" desteyle oynar** (tüm kartlar destesi daha zayıf olduğu için adil olsun diye).

### Animasyon ve ses (v1)
- **Animasyon** (`Fx.cs`): sahaya inen kart büyük/saydam başlayıp yerine oturur (rakipte yukarıdan, sende aşağıdan), değişen puanlar kısa darbe yapar, elde üzerine gelen kart kalkar, **banner**'lar (raund başı/sonu, pas, lider yeteneği). Raund sonunda yapay zekâ banner okunsun diye ~2 sn bekler. Hepsi yalnız görünümdür; durumu değiştirmez.
- **Ses** (`Sfx.cs`): 16 yer tutucu efekt çalışma anında sentezlenir (kart koyma, çekme, pas, hava, Yak, Boynuz, casus, Medic, lider, toplanma, raund/oyun kazanma-kaybetme, yazı-tura, tık). Sol panelde "Ses: açık/kapalı" düğmesi; seçim `%AppData%\Gwent\settings.json`'da saklanır.
- **Orijinal sesleri kullanmak için:** dosyaları `assets/sfx/<ad>.wav` ya da `.mp3` olarak exe'nin yanına (veya `%AppData%\Gwent\sfx`) koy; adlar yukarıdaki 16 efektin kodudur: `place draw pass weather scorch horn spy medic leader muster round-win round-lose win lose coin click`. Bulunursa yer tutucunun yerine o çalınır. (`.ogg` desteklenmez; `.wav`/`.mp3`e çevir.) Müzik henüz yok.
- Sesler olaylara bağlıdır (`GameEvent`): arayüz yeni bir ses için motoru değiştirmez.

**Sınama:** `--selftest` artık deste kurucuyu (rastgele ekle/çıkar, önerilen deste, kaydet, yeniden yükle, sil, bozuk dosya, kayıtlı desteyle oyun), animasyon/banner kodunu (efektler açık) ve 16 sesin geçerli WAV olup `MediaPlayer` ile açılabildiğini de denetler. Bu sınama üç gerçek hata yakaladı: önizleme panelinin yeniden eklenme çökmesi, ad yazılınca "Kaydet"in açılmaması, ve iki ses efektinin kırpılması.

## 19. Orijinal Varlıklar ve Görünüm (2026-10-13)

Arayüz artık Witcher 3'ün Gwent masasını birebir izler: oyunun kendi **masa dokusu** (1920×1080 tek parça), **kart resimleri**
(185 adet), **rozetler/simgeler**, **fraksiyon kalkanları**, **mücevher**, **defne**, **deste arkaları**, **mesaj afişleri**,
**Gwent müziği** (5 han parçası) ve **46 ses efekti** oyunun kendi dosyalarından çıkarılmıştır. Yerleşim (satır konumları,
boynuz yuvası, lider/mezarlık/deste yuvaları, oyuncu blokları) kullanıcının verdiği oyun içi ekran görüntüsünden alınmıştır.

- **Çıkarma:** `python tools/extract_assets.py` (bkz. `tools/OKUBENI.md`). Dosyalar `assets/` altında, **depoya girmez**.
- **Varlıksız mod:** `assets/` yoksa istemci kart gövdesini fraksiyon rengiyle, masayı düz kahverengiyle çizer ve sentezlenmiş
  yer tutucu seslerle çalışır (çökmez, `--selftest` bunu da doğrular).
- **Kaynak doğrulaması:** oyunun kendi kart veri tabanı (`def_gwint_cards_final.xml`, 206 kart) bizim `data/cards.json` ile
  karşılaştırıldı (`python tools/w3cards_xml.py compare`): 185 kartın güç, sıra, kahraman ve yetenek bilgisi **tutuyor**.
  Kalan farklar beklenen: GOG/next-gen kart çiftleri (Geralt, Ciri) ve dönüşüm jetonlarının adları.
- **Ses:** efektler olay adıyla yüklenir (`assets/sfx/<ad>.wav`, çoklu varyant `_1,_2…` rastgele seçilir); oyun ekranı
  motor olaylarını bu adlara bağlar (`GwintView.HandleEvents`). Müzik `assets/music/*.mp3` karıştırılıp döngüde çalınır.
- **Sınama:** `--selftest` artık varlıkları (görseller, 185 kart resmi), her efekt adının dosyaya çözülmesini ve tüm
  müzik/efektlerin `MediaPlayer` ile açılabildiğini denetler.

**Çözüm süreci özeti (neden özel okuyucular):** oyunun 5.x sürümü paket/doku/CR2W biçimlerini değiştirdi; WolvenKit-7'nin
okuyucuları kısmen çalışıyor. Paket kayıt boyu 0x130, `texture.cache` sürüm 7, CR2W v164 olarak çözüldü ve `tools/w3*.py` ile
yazıldı. Arayüz atlasları `gwint_game.redswf` içindeki sıkıştırılmış GFx'ten (`CFX` → zlib) ve `CSwfTexture` parçalarından
(DXT5/DXT1) çıkarıldı; kart resimleri `texture.cache` içinde `gameplay\gui_new\icons\gwint\*.png` adıyla durur;
ses efektleri Wwise `gui.bnk`/`gwint_ep2.bnk` bankalarında olay (HIRC) → WEM eşlemesiyle bulundu; müzik `sfx/music/gwent_game`.

### 19.1 Animasyonlar ve pencereler
- Animasyonlar yalnız görünümdür (`Fx`, `GwintView`): bir önceki çizimle karşılaştırılarak üretilir; kartın eldeki/sahadaki/mezarlıktaki eski konumundan yeni konumuna uçar.
- Efekt kareleri oyunun kendi GFx dosyasından (`assets/ui/fx/`): kahraman → yıldırım, Medic → duman, toplanma/jeton → bulut, diğerleri → iniş patlaması, Yak → ateş + sarsıntı. Tur sonu kartlar mezarlığa uçar, Yem kartı ele döner, kaybedilen mücevher kırılır, hava örtüsü yavaşça belirir.
- Lider kartına tıklamak onu oynatmaz: yakınlaştırma penceresi açılır (açıklama, durum, **Kullan / Vazgeç**). Rakip lideri için yalnız bilgi (**Kapat**). Esc kapatır.
- Mezarlık yığınına tıklamak (iki taraf için) o taraftaki tüm kartları kaydırmalı gösterir. ⚠️ Orijinalde rakip mezarlığının görüntülenip görüntülenmediği doğrulanmadı; açık bilgi olduğu için ikisine de izin verildi.

### 19.2 Ana menü
- Oyunun kendi menü panoramaları (`assets/ui/pano_*.png`) arka plandır; fraksiyona göre değişir (Kuzey→Novigrad, Nilfgaard→Wyzima, Scoia'tael→No Man's Land, Canavar→Sisler Adası, Skellige). Logo `assets/ui/logo.png`.
- Solda logo + yazı menüsü (Oyna / Deste Kurucu / Ses / Müzik / Çıkış), sağda fraksiyon kalkanı şeridi, ortada seçili fraksiyonun lider kartı (yavaş "nefes alır"). "Oyna" maç kurulumunu (deste, lider, rakip) açar.
- Açılış animasyonu (siyahtan çıkış, kayarak belirme), kor parçacıkları ve yavaş yakınlaşma yalnız görünümdür; `animate:false` ile (ekran görüntüsü) kapatılır.

### 19.3 Global etkili kart sinematiği
- Boynuz, Yak, Mardroeme, hava kartları (Yem hariç) ve lider yeteneğinin çağırdığı kart (Sis, Boynuz, Yak, hava temizleme) oynanınca kart ekran ortasına kayar, iki oyuncuya da gösterilir, sonra gider; **etki ancak o zaman çizilir/işlenir görünür**. Çekirdek bunun için lider yeteneğinde `leader_card` olayı yayar (Detail = gösterilecek kart kimliği).
- Sinematik sırasında arayüz kilitlidir (`_cinema`), yapay zekâ sırasını bekler. `animate:false` (sınama) kipinde atlanır.

### 19.4 VS ekranı ve paralaks
- "Oyuna başla" hemen maçı açmaz: iki oyuncunun avatar, fraksiyon ve lider kartıyla karşı karşıya geldiği "VS" ekranı ~3 sn gösterilir (tıklayınca geçilir), sonra maç başlar.
- Menüdeki fare paralaksı (ekranın fareyle kayması) kullanıcı isteğiyle **kaldırıldı**; geri eklenmesin.

### 19.5 Canlı hava, maç başı sekansı, oyun yazı tipi
- **Hava (`WeatherFx`):** don iki kenardan ortaya kristal gibi büyür ve kar taneleri parıldar; sis kesintisiz akar; yağmur yağar; Skellige fırtınasında bulut ve şimşek vardır. Örtü `GwintView._wxEl` içinde yeniden çizimler arasında korunur (animasyon sıfırlanmaz); hava kalkınca solarak gider.
- **Maç başı:** önce oyunun madalyonu atılır (kale yüzü = sen, meşale = rakip; sonuç `Game.CoinFirst`, oyun kurulurken belirlenir), sonra 10 kart desteden tek tek dağıtılır, ardından kart değiştirme penceresi gelir. Scoia'tael'de başlangıcı seçen taraf varsa yazı-tura atlanır. Tıklayınca sekans geçilir.
- **Yazı tipi:** arayüz oyunun kendi "PF Din Text Cond Pro" yazısını kullanır (`tools/w3_font_extract.py`, `fonts_en.redswf` içindeki vektör harflerden TTF üretir; Türkçe harfler tam; `pip install fonttools` gerekir). Yoksa Windows serif'ine döner.

### 19.6 Plan (online): karakter seçimi
- Çevrimiçi maçta oda kurulunca iki oyuncu da bir **karakter (portre)** seçer; VS ekranında ve oyun alanındaki avatar yerinde bu portre görünür. Portreler `gameplay/gui_new/icons/characters` (92 ikon, 64 px; küçük) ya da daha büyük dokulardan çıkarılacak. Online sunucu yazılırken eklenecek.

### 19.7 Ayarlar, zorluk, oyun sonu
- **Ayarlar** (`SettingsView`, `Settings`): ses efektleri ve müzik için aç/kapa + 0-100 düzey çubuğu, tam ekran (F11 her yerde). `%AppData%\Gwent\settings.json` içinde saklanır.
- **Yapay zekâ zorluğu** (`GwentAI.Difficulty`, `AgentFactory`): Zor = ayarlı `HeuristicAgent`; Normal = hamlelerin %35'inde rastgele geçerli eylem; Kolay = %80 + kart değiştirmede hata. Ölçüm (`DifficultyTests`, 60 oyun): Zor-Kolay %82, Zor-Normal %59, Normal-Kolay %68. Seçilen zorluk "Maçı hazırla" ekranında ve kalıcıdır.
- **Oyun sonu**: kutusuz tam ekran sahne (kazanınca altın ışık + patlamalar, kaybedince kızıl), büyük başlık, raund skoru ve raund tablosu.

### 19.8 Performans
- `dotnet run --project src/GwentClient -c Release -- --perf`: gerçek pencerede her efekti (yazı-tura, kart inişi, ateş/şimşek, sarsıntı, sinematik, hava, menü, deste kurucu) ayrı ayrı çalıştırıp kare süresini ölçer; sonuç `perf.txt` (exe yanında ve %TEMP%\gwent-perf.txt). Kasma şikâyetinde önce bunu çalıştır. `--perf-mute` sesi kapatır. Pencere ~45 sn açık kalır.
- Kurallar: sürekli hareket eden öğelere `BlurEffect`/`DropShadowEffect` koyma (kart gölgesi ve parıltı yarı saydam katmanlarla yapılır); büyük öğeyi sarsarken `BitmapCache` kullan; kare dizileri ve yazı-tura `CompositionTarget.Rendering` ile (ekran yenilemesiyle senkron) sürülür, `DispatcherTimer` ile değil.
- **Oyun içi ayarlar:** alt çubukta "Ayarlar" düğmesi (Esc de açar) ayarlar penceresini oyunun üstünde açar; maç sürer. Ses/Müzik düğmeleri alt çubuktan kaldırıldı, hepsi bu pencerede.

### 19.9 Maç tekrarı ve hata bildirimi
- Her maç `%AppData%\Gwent\last_match.json` içine **her hamleden sonra** yazılır (kurulum + tohum + tüm eylemler + oyuncunun gördüğü günlük). Motor deterministik olduğu için aynı kayıt aynı oyunu verir (`ReplayData`, `ReplayTests`).
- Hata bildirirken bu dosyayı göster; yeniden oynatma: `dotnet run --project src/GwentArena -c Release -- replay "%AppData%\Gwent\last_match.json"` (her eylem, olay ve toplam yazdırılır).
- Yak denetimi: `ScorchBondTests` rastgele oyunlarda 1000+ Yak durumunda "en güçlü kartların hepsi ölür, başkası ölmez" kuralını doğrular.
- **Ses zamanlaması:** yerleştirme sesleri (birim, Yem, toplanma, jeton, diriltme) kart sahaya indiği an (uçuş süresi 520 ms sonra) çalar; hava/Boynuz/Yak sesleri etkinin işlendiği anda; sıra bildirimi inişten sonra. Bkz. `GwintView.HandleEvents`.

## 20. Online (plan ve durum)

**Hedef:** iki kişi internetten oynar. Ayrı kiralık sunucu yok: **oda kuran kişinin oyunu sunucu olarak da çalışır** (bugün A kurar, yarın B kurar). Zaman sınırı yok. Hesap sistemi yok; yerel profil ve istatistik.

### 20.1 Mimari (karar verildi)
- **Sunucu hakem, istemciler aynı oyunu oynar (kilit adımlı):** sunucu odayı ve oyunu tutar, gelen her eylemi kendi `Game` kopyasında doğrular (`Game.Apply`), geçerliyse sıra numarasıyla iki istemciye yayar. İki istemci de aynı tohumla kendi `Game` kopyasını çalıştırıp eylemleri sırayla uygular. Motor değişmez; tekrar sistemi (`ReplayData`) yeniden bağlanmanın da temelidir.
- Bedeli: rakibin eli/destesi teknik olarak iki istemcinin belleğinde de var. Yalnız arkadaşlar arası olduğu için kabul; arayüz rakibin elini göstermez. **Hile (bellekten okuma) kapsam dışı.**
- **Taşıma katmanından bağımsız çekirdek:** oda mantığı `GwentServer` kütüphanesinde, istemciye mesajı `IGameClient` arayüzüyle yollar. SignalR (`GwentHub`) ince bir bağdaştırıcı olacak; testler bellek içi sahte istemcilerle yapılır.
- **Gömülü sunucu:** istemci "Oda kur" deyince aynı kütüphane Kestrel üzerinde başlar; host kendisi de bir oyuncu gibi bağlanır. İleride aynı kod ayrı sunucu olarak da çalışabilir.
- **Eşleşme:** kısa oda kodu + davet kodu (adres + oda). Sürüm uyuşmazsa bağlanılmaz (kart verisi/kural farkı).
- **Desteler:** her oyuncu kendi destesiyle oynar (sunucu `DeckValidator` ile doğrular); yerel oyundaki "rakip = önerilen deste" kuralı yalnız yapay zekâ içindir.
- **Kopma:** oda bir süre bekler; yeniden bağlanan oyuncu son sıra numarasını yollar, eksik eylemler gönderilir. Rakip belirli bir süre dönmezse kalan oyuncu galibiyeti alabilir. **Host çıkarsa oda kapanır.** Teslim ol (rakip kazanır) ve rövanş var.
- **İstatistik:** merkezi veritabanı yok; her istemci kendi sonuçlarını yerelde tutar (takma ad, G/M, fraksiyon bazında). Maç tekrarı (`ReplayData`) iki tarafta da saklanır.

### 20.2 Bağlantı (host'a internetten ulaşma)
Modemler dışarıdan gelen bağlantıyı almaz (NAT). Yöntemler, varsayılandan yedeğe:
1. **Tünel (Cloudflare Tunnel, `cloudflared`):** host'un oyunu ücretsiz tünel açar; her yerde çalışır, arkadaş bir şey kurmaz. `cloudflared` indirilirken kullanıcıdan **onay** istenir.
2. **UPnP otomatik port:** birçok modemde çalışır; CGNAT'ta çalışmaz.
3. **Doğrudan IP:port** (Tailscale/ZeroTier gibi sanal ağ ya da elle port yönlendirme).

### 20.3 Aşamalar
| # | Aşama | Durum |
|---|---|---|
| 1 | **Sunucu mantığı kütüphanesi** (`GwentNet` sözleşmeler, `GwentServer` oda/doğrulama/yayın) + sahte iki istemciyle testler | **tamam** (21 test) |
| 2 | İstemci hazırlığı: `GameSession`'da rakibi soyutla (yapay zekâ / uzak oyuncu); `GwintView`'da sabit `Me=0` yerine koltuk numarası | **tamam** (`--selftest-online`) |
| 3 | SignalR bağdaştırıcısı + gömülü sunucu (yerelde iki oyun) | **tamam** (9 ağ testi + `--selftest-online --net`) |
| 4 | Online ekranlar: oda kur/katıl, bekleme odası (deste, lider, hazır), maç, kopma/rövanş | **tamam** (`--selftest-lobby`) |
| 5 | Bağlantı katmanı: davet kodu, tünel (cloudflared), UPnP, doğrudan IP | **tamam** (gerçek cloudflared tüneli üzerinden tam maç doğrulandı) |
| 6 | Yerel profil ve istatistik, karakter (portre) seçimi, sürüm kontrolü, kurulum paketi | **kısmen:** istatistik + dağıtım paketi tamam; portre seçimi ve yeniden başlatınca maça dönme açık |

### 20.4 Açık noktalar (⚠️)
- Karakter portreleri: mevcut 92 ikon 64 px (küçük); daha büyük portre dokuları aranacak.
- İki istemci **aynı koltuk sırasıyla** (host=0, konuk=1) `Game` kurmalı: koltuk takası karıştırma sırasını değiştirir ve kopyaları ayrıştırır. Arayüz bunu koltuk numarasıyla çözer (`GameSession.MySeat`; ekranda "ben" hep altta).

### 20.5 Aşama 1 — ne var
- `src/GwentNet`: sözleşmeler (`ProtocolInfo.Version` = protokol no + kart verisi özeti, `LoadoutDto`, `RoomState`, `MatchSetup`, `ActionBroadcast`, `MatchResult`, `IGwentClient`, `Reply`).
- `src/GwentServer`: `RoomService` (tek kilitli, taşıma bağımsız): oda kur/katıl (5 harfli kod, sürüm ve takma ad denetimi), deste doğrulama, hazır → maç başlat (sunucu tohumu seçer, `Game` hakemdir), eylem doğrulama + sıra numarasıyla yayın, teslim, kopma/`Rejoin` (eksik eylemler), `ClaimWin` (bekleme süresi 2 dk), rövanş, `Leave`, `Sweep`.
- Kurallar: eylem gönderen = koltuk sahibi olmalı ve sırası gelmeli; reddedilen eylem durumu **değiştirmez** (testle korunur); eylemler 0'dan boşluksuz numaralanır; host (koltuk 0) çıkarsa oda kapanır; koltuk sırası (host=0, konuk=1) iki istemcide de aynıdır.
- `tests/GwentServer.Tests`: bellek içi sahte istemciler (`FakeClient`) gerçek istemci gibi kendi `Game` kopyasını tutar; tam maçlarda sunucu ve iki kopya **olay olay aynı** bitmeli (karışık zorluklarla 40 maç dahil).
- `ReplayData.OppDeckCards`: online maçta rakip destesi de açık verilir (yerelde boş = önerilen deste).

### 20.6 Aşama 2 — ne var
- `GameSession` iki kipli: yerel (yapay zekâ rakip, koltuk 0) ve online (`new GameSession(MatchSetup, IMatchLink)`; koltuk 0/1). Online'da `Apply` eylemi **önce kendi `Game` kopyasında dener** (anında hata mesajı için), sonra sunucuya yollar; asıl uygulama sunucunun yankısı gelince olur (kendi hamlem de rakibinkiyle aynı sıralı akıştan). Yankı beklerken yeni eylem gönderilmez (`AwaitingEcho`).
- Sunucudan gelen eylemler `EnqueueRemote` ile kuyruğa alınır (her iş parçacığından); arayüz `KickAi` döngüsü yerelde yapay zekâyı, online'da kuyruğu işler (`OpponentBusy`, `ApplyNextRemote`). Kural dışı bitiş (teslim/terk) `ExternalResult` ile gösterilir; kazanan `WinnerSeat`.
- `RoomLink` (`IGwentClient` + `IMatchLink`): sunucu iletilerini kuyruğa alır, `Pump` ile (arayüz iş parçacığında) oturuma ve olaylara iletir. Şimdilik sunucu aynı süreçte; ağ bağdaştırıcısı aynı olayları üretecek.
- `GwintView`: `Me`/`Opp` artık oturumdan gelir; ekran konumları `Side(player)` ile (ben altta). Oyun sonu tablosu, günlük ve banner puanları koltuğa göre döner. Online'da "Yeniden oyna" yerine "Rövanş iste".
- Sınama: `Gwent.exe --selftest-online [n]` iki gerçek arayüzü sunucu üzerinden rastgele tıklamayla oynatır, sunucunun ve iki arayüzün oyununu olay olay karşılaştırır; her 5. oyunda teslim senaryosu. 60 oyun hatasız.

### 20.7 Aşama 3 — ne var
- `src/GwentHost`: `GwentHub` (SignalR; yalnız bağlantı↔oturum eşlemesi, iş mantığı `RoomService`'te), `HubClientProxy` (bağlantı başına **sıralı** gönderim kuyruğu: eylemler ağda da sıra numarasıyla aynı sırada varır; sunucu kilidi altında beklemez), `LocalServer.StartAsync(port, lan)` (Kestrel; `port=0` boş port seçer; `lan=false` yalnız bu bilgisayar, `true` yerel ağ), `RoomSweeper` (6 saat kıpırdamayan odaları siler).
- İstemci: `RoomLinkBase` (taşıma bağımsız: kuyruk, `Pump`, olaylar, `...Async` istekler + bloklayan sarmalayıcılar), `RoomLink` (aynı süreçteki sunucu), `NetLink` (SignalR; `NetLink.ConnectAsync(url)`).
- Testler: `tests/GwentHost.Tests` gerçek Kestrel + SignalR üzerinden (localhost): oda kur/katıl, sürüm uyuşmazlığı, 3 tam maç (sunucu ve iki kopya olay olay aynı), kural dışı eylem reddi, kopma → bildirim → koltuk anahtarıyla yeniden bağlanma, teslim. `Gwent.exe --selftest-online N --net`: iki gerçek arayüz gerçek ağ üzerinden rastgele tıklamayla oynar.
- **Tuzak (öğrenildi):** UI iş parçacığında bir `HubConnection`/`LocalServer`'ı `.GetAwaiter().GetResult()` ile **kapatmak kilitlenir**. Gerçek arayüzde `await` kullan; sınamada `Task.Run(...)` ile havuzda kapat.
- **Dağıtım uyarısı:** istemci artık `Microsoft.AspNetCore.App` çalışma zamanına bağlı (gömülü sunucu). Çerçeveye bağlı (framework-dependent) yayında **her bilgisayarda .NET Desktop + ASP.NET Core çalışma zamanı** gerekir. Arkadaşlara dağıtırken **self-contained** yayın yapılacak (aşama 6; boyut ~150 MB civarı beklenir).

### 20.8 Aşama 4 — ne var
- **Menü → Online** (`OnlineMenuView`): takma ad (kalıcı), **Oda kur** (gömülü sunucu; "yeni nesil" ve "yerel ağdan erişim" seçenekleri) ya da **Odaya katıl** (host adresi `adres:port` + 5 harfli oda kodu; son adres hatırlanır). Hatalar (yanlış kod, ulaşılamayan adres 10 sn, sürüm uyuşmazlığı) ekranda gösterilir.
- **Bekleme odası** (`LobbyView`): oda kodu + Kopyala, host'un yerel ağ adresleri (`ip:port`), iki oyuncu kartı (bağlı/hazır, rakibin fraksiyonu ve lider kartı), benim fraksiyon / deste (önerilen ya da geçerli kayıtlı desteler) / lider seçimim; her değişiklik sunucuya gider ve hazır durumunu sıfırlar. "Hazırım" → ikisi hazırsa sunucu maçı başlatır.
- **Maç:** `MainWindow` VS ekranını (`VsScreen`, yerel oyunla ortak) gösterir, sonra `GwintView`. Oyun alanında: **Teslim ol** (onaylı), **Menüye dön** (maç sürerken ayrılmak teslim sayılır, onaylı), oyun sonunda **Rövanş iste** (rakip isteyince ipucu; ikisi isteyince yeni maç + VS), üstte **bildirim çubuğu**: rakip koptu (2 dk sonra "Galibiyeti al"), yeniden bağlanılıyor, bağlantı koptu (Menüye dön).
- **Yeniden bağlanma:** `NetLink` bağlantı koparsa kısa aralıklarla (~2 dk) dener, geri gelince koltuk anahtarıyla `Rejoin` yapıp son aldığı sıra numarasından sonrasını ister (`RoomLinkBase.LastReceivedSeq`).
- Sınama: `Gwent.exe --selftest-lobby [n]` host ve konuk için gerçek arayüzlerle: adres normalleştirme, olmayan odaya katılma reddi, küçük harfli kodla katılma, fraksiyon/deste/lider tıklama, "Hazırım", tam maç, **rövanş**, sunucu ve iki kopyanın olay olay eşitliği.
- **Sınırlar (⚠️):** katılma şimdilik yalnız yerel ağ (ya da aynı bilgisayar) için adres+kod ile; internetten bağlanma (tünel/UPnP/davet kodu) aşama 5. Windows ilk host'ta güvenlik duvarı izni isteyebilir. Yeniden bağlanma uygulama kapanıp açıldıktan sonra maça dönmeyi kapsamaz (aşama 6).

### 20.9 Aşama 5 — ne var
- **Davet kodu** (`GwentNet.InviteCodes`): `GWENT-XXXXX-XXXXX-…` tek parça; içinde adres (IPv4+port, `*.trycloudflare.com`, https adresi ya da tam adres), 5 harfli oda kodu ve sağlama baytı var. Büyük/küçük harf, tire/boşluk ve karışan harfler (O/0, I/L/1) önemsiz; tek karakter hatası yakalanır (18 test). Tünel kodu ~70 karakter, yerel ağ kodu ~30.
- **Bağlantı yöntemleri** (oda kurarken seçilir; hepsi başarısız olsa da oda kurulur, nedenler bekleme odasında silik yazılır):
  1. **Tünel** (`GwentHost.CloudflareTunnel`): `cloudflared tunnel --url http://127.0.0.1:PORT` ile geçici `https://….trycloudflare.com` adresi (hesapsız, ücretsiz, CGNAT'ta da çalışır; arkadaş bir şey kurmaz). Sunucu yalnız `127.0.0.1`'e bağlanır (güvenlik duvarı sormaz). Katılırken tünel adresi yayılana kadar 6 deneme (3 sn arayla).
  2. **UPnP** (`GwentHost.Upnp`, ek paketsiz SSDP+SOAP): modemden dış port ister (kira 2 saat, 30 dk'da yenilenir, kapanınca silinir; 725 → süresiz kira, 718 → başka port). Modemin dış adresi özel/CGNAT ise internetten ulaşılamayacağı uyarılır. **Varsayılan kapalı** (gelişmiş). `--net-info` bu bilgisayarda yoklar (port açmaz).
  3. **Yerel ağ** (`ip:port` davet kodu) ve elle adres+oda kodu (gelişmiş).
- **cloudflared indirme:** yalnız **kullanıcı izin verirse** (boyut, kaynak `github.com/cloudflare/cloudflared`, konum `%AppData%\Gwent	ools` gösterilir); indirilen dosya `WinVerifyTrust` (özet + zincir) ve imzalayanın "Cloudflare" olması ile doğrulanır, aksi halde silinir.
- Testler: 18 davet kodu, sahte modem (SOAP, 725/718), SSDP/XML ayrıştırma, CGNAT aralıkları, sahte cloudflared (URL bulma, zaman aşımı, erken çıkış, bulunamadı), imza reddi; `--selftest-lobby` davet koduyla katılma + sahte tünelli oda.
- **Gerçek tünel doğrulandı (2026-10-13):** `Gwent.exe --selftest-tunnel` kurulu `cloudflared` ile tünel açar (~6 sn), konuk davet koduyla bağlanır, tam maç oynanır (SignalR/WebSocket Cloudflare üzerinden); sunucu ve iki kopya aynı. `--selftest-tunnel-probe` tünel adresine periyodik GET atıp durum kodunu yazar (teşhis).
- **⚠️ Doğrulanmayan:** modemde gerçek port açma (bu ağdaki modem UPnP'ye yanıt vermiyor).

### 20.10 Tünel dersleri (gerçek denemeden)
- **Genel cloudflared ayarı hızlı tüneli bozar:** kullanıcının `~/.cloudflared/config.yml` dosyası (başka projenin adlandırılmış tüneli, ingress kuralları, son kural `http_status:404`) hızlı tünele de uygulanır; adresimiz hiçbir kurala uymadığı için **Cloudflare 404** döner. Çözüm: tüneli `--config <boş dosya>` ile başlatmak (`CloudflareTunnel.StartAsync`). Bu olmadan kurulu cloudflared'i olan herkeste tünel çalışmaz.
- **Yeni tünel adı DNS'te yayılana kadar çözülemez** (10–60 sn; bu ağda sistem DNS'i 61 sn, 1.1.1.1 UDP 19 sn). `TunnelDns`: sistem DNS → 1.1.1.1 DNS-over-HTTPS → `trycloudflare.com` ana alan adının adresi → bilinen uç nokta; bağlantı IP'ye yapılır, TLS tünel adıyla (sertifika denetimi sağlam). Böylece konuk adın yayılmasını beklemez (~6 sn). Katılma yine 12 deneme (5 sn arayla) yapar.
- `FindInstalled`: oyunun kurulum klasörü, exe yanı, Program Files / (x86) / LocalAppData ve **PATH** içinde `cloudflared.exe` arar (bu makinede `C:\Program Files (x86)\cloudflared`, sürüm 2026.6.1).

### 20.11 Aşama 6 notları (2026-10-13)
- **İstatistik:** `Stats` her biten maçı (yapay zekâ ve online) `%AppData%\Gwent\stats.json` içine yazar (en çok 500 kayıt; yalnız gerçek uygulamada, sınamalarda `Stats.Enabled` kapalı). Menüde "İstatistikler": toplam, galibiyet serisi, rakibe/fraksiyona göre döküm, son maçlar, kayıtları silme. Hesap yok.
- **Dağıtım:** `tools/package.sh` tek dosyalı **self-contained** `Gwent.exe` (~155 MB, .NET/ASP.NET kurmaya gerek yok; `--selftest-online` ile doğrulandı) + `assets/` (cards, music, sfx, ui; ~94 MB) + `OKU.txt` üretir. Görsel/ses pakete dahildir, arkadaşlar çıkarma yapmaz (paket yalnız arkadaşlara; depoya girmez). Protokol sürümü yalnız kart verisine bağlı: farklı sürüm odaya girerken reddedilir.
- **Açık:** portre seçimi (oyundaki karakter simgeleri yalnız 64 px; büyük sanat gerekir), uygulama kapanıp açılınca maça dönme.
