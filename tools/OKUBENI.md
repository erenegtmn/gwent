# Orijinal Witcher 3 varlıklarını çıkarma (kişisel kullanım)

Oyun, kart resimlerini, masayı, simgeleri, müziği ve ses efektlerini **kendi Witcher 3 kurulumundan** okur. Bu dosyalar
CD PROJEKT RED'e aittir: **depoya girmez** (`assets/` tamamen `.gitignore`'da), yayınlanmaz. Her kişi kendi oyun
kopyasından üretir. Dosyalar yoksa istemci yer tutucu görsellerle çalışmaya devam eder.

## Gerekenler
- Witcher 3 (Steam/GOG, **5.x sürümü**, DX12 yapısı: `bin\x64_dx12`)
- Python 3 + `pip install pillow`, .NET SDK 9
- İki açık kaynak araç (elle indirilir, `tools/_downloads/` altına açılır — depoya girmez):
  | Araç | Nereden | Klasör |
  |---|---|---|
  | WolvenKit-7 7.2.0 (`WolvenKit-7.2.0.zip`, ~101 MB) | github.com/WolvenKit/WolvenKit-7/releases | `tools/_downloads/wolvenkit7/` |
  | vgmstream (`vgmstream-win64.zip`, ~4,5 MB) | github.com/vgmstream/vgmstream/releases | `tools/_downloads/vgmstream/` |

  WolvenKit-7'den yalnız kütüphaneleri, `ffmpeg` ve `SoundCache\soundbanksinfo.xml` kullanılır; arayüzü açılmaz.

## Çalıştırma
```bash
python tools/extract_assets.py                       # Steam varsayılan yolu
python tools/extract_assets.py --game "D:\Oyunlar\The Witcher 3"
python tools/extract_assets.py --skip-audio          # yalnız görseller
```
Birkaç dakika sürer ve şunları üretir (hepsi `assets/` altında):

| Klasör | İçerik |
|---|---|
| `assets/ui/` | masa (`board.png`), rozetler, yetenek/sıra simgeleri, fraksiyon kalkanları, deste arkaları, mücevher, defne, mesaj afişleri |
| `assets/cards/<kart_kimliği>.png` | 185 kart resmi (birimler, liderler, özel kartlar, jetonlar) |
| `assets/music/*.mp3` | Gwent masasında çalan 5 han müziği |
| `assets/sfx/*.wav` | 46 Gwent ses efekti (olay adıyla: `horn.wav`, `scorch.wav`, `coin_toss_1.wav` …) |
| `assets/extracted/` | ara dosyalar (ham XML, atlaslar, `.wem`) — silinebilir |

## Neden WolvenKit-7 doğrudan kullanılmıyor?
Oyunun 5.x sürümü (next-gen) dosya biçimlerini değiştirdi: WolvenKit-7'nin paket (`.bundle`), doku önbelleği
(`texture.cache` v7) ve CR2W (v164) okuyucuları bu sürümü tam okuyamıyor. Bu yüzden biçimler `tools/` altındaki
küçük Python okuyucularla çözüldü; WolvenKit-7 yalnız ses önbelleğini ve yardımcı araçları sağlıyor.

| Betik | Ne yapar |
|---|---|
| `w3bundle.py` | `.bundle` paketlerini listeler/çıkarır (kayıt boyu 0x130) |
| `w3swf.py` | `gwint_game.redswf` (CR2W v164 → gömülü GFx) içinden doku atlaslarını ve 340 alt resmi çıkarır |
| `w3texcache.py`, `w3tex_extract.py` | `texture.cache` v7'yi okur, kart resimlerini PNG'ye çevirir (DXT5 / RGBA) |
| `w3cards_xml.py` | oyunun kart veri tabanını (`def_gwint_cards_final.xml`) okur; `data/cards.json` ile karşılaştırır, resim eşlemesini üretir |
| `w3_ui_assets.py` | ham dosyaları adlandırılmış `assets/ui` ve `assets/cards`'a derler |
| `w3bnk.py`, `w3sfx_export.py` | Wwise ses bankalarında olay → WEM eşlemesi çıkarır, efektleri WAV'a çevirir |
| `AssetExtractor/` | (C#) `soundspc.cache`'ten WEM çıkarır |

## Sorun giderme
- "Witcher 3 kurulumu bulunamadı": `--game` ver ya da `W3_GAME` ortam değişkenini ayarla.
- Çıkan resimler/sesler tutmuyorsa oyun sürümü farklıdır (4.x); biçim sabitleri `w3*.py` başlıklarında yazılıdır.
