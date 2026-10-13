#!/bin/sh
# Arkadaşlara dağıtım paketi: tek dosya, self-contained (.NET kurmaya gerek yok). Kullanım: tools/package.sh [çıktı klasörü]
set -e
OUT="${1:-dist/Gwent}"
rm -rf "$OUT"
dotnet publish src/GwentClient -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$OUT"
mkdir -p "$OUT/assets"
for d in cards music sfx ui; do cp -r "assets/$d" "$OUT/assets/$d"; done   # extracted/ ham ara çıktıdır, istemci okumaz
# Oda kuran kişi internetten bağlanabilsin diye tünel programı (Cloudflare cloudflared, Apache-2.0) da pakete konur; kurulu değilse oyun indirme izni ister
for CF in "/c/Program Files (x86)/cloudflared/cloudflared.exe" "/c/Program Files/cloudflared/cloudflared.exe"; do
  if [ -f "$CF" ]; then cp "$CF" "$OUT/cloudflared.exe"; break; fi
done
cat > "$OUT/OKU.txt" <<'TXT'
Gwent - arkadaslar arasi
1) Klasoru bir yere cikar, Gwent.exe'yi calistir (kurulum gerekmez; assets klasoru exe'nin yaninda kalmali).
2) Online: Online > Oda kur (internet tuneli icin cloudflared.exe pakette gelir, bir sey indirilmez) ya da arkadasinin davet kodunu yapistir (katilan kisi hicbir sey kurmaz).
TXT
echo "Paket: $OUT"
