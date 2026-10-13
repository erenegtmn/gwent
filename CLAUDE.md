# CLAUDE.md — Gwent (Witcher 3)

Arkadaşlar arası oynanacak, **yayınlanmayacak** bir Witcher 3 Gwent klonu. Orijinal oyuna **sadık** kalınır.

## Belgeler
- `KURALLAR.md` — kurallar, kararlar, mimari, yol haritası, doğrulanacaklar (⚠️). Önce bunu oku.
- `KARTLAR.md` — kart tabloları. **Otomatik üretilir**, elle düzenleme (`tools/build_kartlar_md.py`).
- `data/cards.json` — kart verisinin tek kaynağı; `tools/build_cards.py` ile `tools/cards_table.txt`'ten üretilir.

## Yapı
- `src/GwentCore` — saf, deterministik kural motoru (C#, net9.0). Arayüz/ağ/zaman bilmez.
- `tests/GwentCore.Tests` — xUnit. Kural testleri + `FuzzTests` (rastgele oyunlar, kart korunumu, determinizm).
- `src/GwentAI` — `IAgent`, `HeuristicAgent` (tempo mantığı, `Game.Clone()` ile dener), `RandomAgent`, `GameRunner`.
- `src/GwentArena` — konsol: yapay zekâ arenası (`random|ai|ab`), ayar taraması (`sweep`), oyun izleme (`trace`).
- `src/GwentClient` — WPF istemci. Oyun ekranı `GwintView` (1920×1080 tuval, orijinal Gwent masası), kart `GwintCard`, menü `MenuView`, deste kurucu `DeckBuilderView`. Durum her değişiklikte baştan çizilir. `GameView`/`CardControl` eski yer tutucu arayüzdür (CardControl yalnız ipucu kutusu için kullanılır).
- İstemcide ayrıca: `Assets` (orijinal görseller, yoksa yer tutucu), `Sfx` (olay adlı ses efektleri), `Music`, `Fx` (animasyon), `DeckStore`.
- `tools/` — orijinal varlık çıkarma hattı (`extract_assets.py`, `w3*.py`, `AssetExtractor/`); bkz. `tools/OKUBENI.md`.
- `src/GwentNet` (online sözleşmeler), `src/GwentServer` (oda/doğrulama/yayın, taşıma bağımsız), `src/GwentHost` (SignalR hub + gömülü Kestrel sunucusu) + `tests/GwentServer.Tests` (sahte istemciler), `tests/GwentHost.Tests` (gerçek ağ). İstemcide `Online/` (`RoomLinkBase`, `RoomLink`, `NetLink`). Plan ve durum: `KURALLAR.md` §20.
- İstemcide `Stats`/`StatsView` (yerel istatistik). Sıradaki: portre seçimi, uygulama yeniden açılınca maça dönme.

## Komutlar
```bash
dotnet test tests/GwentCore.Tests
dotnet test tests/GwentServer.Tests   # online sunucu çekirdeği (sahte istemciler)
dotnet test tests/GwentHost.Tests     # gerçek SignalR/Kestrel ağ testleri
dotnet run --project src/GwentClient -c Release -- --selftest-online 20 --net   # iki gerçek arayüz, gerçek ağ
GWENT_FUZZ_SEEDS=400 dotnet test tests/GwentCore.Tests --filter "FullyQualifiedName~Random_games"   # geniş tarama
dotnet run --project src/GwentArena -c Release -- trace nilfgaard monsters 5   # bir oyunu hamle hamle izle
dotnet run --project src/GwentArena -c Release -- sweep 30                      # AI ayar taraması
dotnet run --project src/GwentClient -c Release -- --selftest-lobby 3 | --selftest-tunnel | --net-info   # bekleme odası / gerçek tünel / ağ teşhisi
sh tools/package.sh                                                             # arkadaşlara self-contained paket (dist/Gwent)
dotnet run --project src/GwentClient                                           # oyunu aç
dotnet run --project src/GwentClient -- --shot <klasör>                        # ekranları PNG'ye çiz (görsel doğrulama)
dotnet run --project src/GwentClient -c Release -- --selftest 150               # arayüz rastgele tıklama sınaması
python tools/extract_assets.py                                                  # orijinal görsel/ses çıkar (bir kez)
python tools/w3cards_xml.py compare                                              # kartlarımızı oyun veri tabanıyla karşılaştır
python tools/build_cards.py tools/cards_table.txt data/cards.json && python tools/build_kartlar_md.py
```

## Kurallar
- **Durumu yalnız `Game.Apply(GameAction)` değiştirir.** Reddedilen eylem durumu değiştirmez (testle korunur).
- **Güç hesabı tek yerde:** `Game.CardScore` (hava → casus çarpanı → Tight Bond → Morale → Boynuz). Arayüz hesap yapmaz.
- Kart verisi değişince `cards.json` + `KARTLAR.md`'yi betikle yeniden üret; elle düzenleme.
- Yeni yetenek eklerken: `OnPlaced`/`CardScore`/`ClearBoard` yollarını ve fuzz testini birlikte güncelle.
- Kullanıcıya dönük metinler **Türkçe**; kod içi isimler İngilizce.
- **Yapay zekâ rakibin elini/destesini göremez**, yalnız sayısını bilir (bekçi testi var). AI'yı değiştirirsen ayarı tahminle değil `sweep` ile ölç.
- `Game.Clone()` yalnız bekleyen seçim yokken çalışır; seçimlerin devamı (closure) kopyalanamaz.
- **Arayüz değişince** `--shot` ile PNG'ye bak ve `--selftest` çalıştır; derleme tıklama akışlarını görmez. Etkileşimli öğelere `Tag` (hand, row-hl, choice, mulligan...) koyulur, sınama onları bulur.
- Orijinal oyunun görsel/sesi `assets/` altında durur ve **depoya girmez** (`.gitignore`). Her kişi kendi oyun kopyasından `tools/extract_assets.py` ile üretir.
- Arayüz hem varlıklı hem **varlıksız** çalışmalı (`Assets.Available`); ikisini de `--shot`/`--selftest` ile dene.
- Yerleşim koordinatları (`GwintView`) orijinal ekran görüntüsünden alınmıştır; değiştirirken `--shot` ile karşılaştır.
- Oyunla doğrulanmamış davranışlar `KURALLAR.md` §13 ve §15'te ⚠️ ile işaretli; doğrulanınca oradan sil.
