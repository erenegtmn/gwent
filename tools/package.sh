#!/bin/sh
# Arkadaşlara dağıtım paketi: tek dosya, self-contained (.NET kurmaya gerek yok). Kullanım: tools/package.sh [çıktı klasörü]
# Çıktı: paket klasörü + yanında Gwent-win-x64.zip (tam), Gwent-core.zip ve Gwent-data.zip (oyun içi güncelleme parçaları).
set -e
OUT="${1:-dist/Gwent}"
rm -rf "$OUT"
# Tek dosya içindeki .NET kütüphaneleri sıkıştırılır (156 MB -> ~71 MB; açılışa ~0,15 sn ekler)
dotnet publish src/GwentClient -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$OUT"
# Oda kuran kişi internetten bağlanabilsin diye tünel programı (Cloudflare cloudflared, Apache-2.0) da pakete konur; kurulu değilse oyun indirme izni ister
for CF in "/c/Program Files (x86)/cloudflared/cloudflared.exe" "/c/Program Files/cloudflared/cloudflared.exe"; do
  if [ -f "$CF" ]; then cp "$CF" "$OUT/cloudflared.exe"; break; fi
done
cat > "$OUT/OKU.txt" <<'TXT'
Gwent - arkadaslar arasi
1) Klasoru bir yere cikar, Gwent.exe'yi calistir (kurulum gerekmez; assets klasoru exe'nin yaninda kalmali).
2) Online: Online > Oda kur (internet tuneli icin cloudflared.exe pakette gelir, bir sey indirilmez) ya da arkadasinin davet kodunu yapistir (katilan kisi hicbir sey kurmaz).
3) Guncelleme: yeni surum cikinca ana menude "Guncelle" dugmesi cikar; oyun kendini gunceller.
TXT
# Varlıklar küçültülerek kopyalanır (kartlar/panoramalar JPEG, sesler MP3); package.json ve zip'ler üretilir
VERSION=$(grep -o '<Version>[^<]*' src/GwentClient/GwentClient.csproj | cut -d'>' -f2)
PYTHONIOENCODING=utf-8 python tools/pack_assets.py "$OUT" "$VERSION"
echo "Paket: $OUT"
