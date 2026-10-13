#!/bin/sh
# Arkadaşlara dağıtım paketi: tek dosya, self-contained (.NET kurmaya gerek yok). Kullanım: tools/package.sh [çıktı klasörü]
set -e
OUT="${1:-dist/Gwent}"
rm -rf "$OUT"
dotnet publish src/GwentClient -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$OUT"
mkdir -p "$OUT/assets"
for d in cards music sfx ui; do cp -r "assets/$d" "$OUT/assets/$d"; done   # extracted/ ham ara çıktıdır, istemci okumaz
cat > "$OUT/OKU.txt" <<'TXT'
Gwent - arkadaslar arasi
1) Klasoru bir yere cikar, Gwent.exe'yi calistir (kurulum gerekmez; assets klasoru exe'nin yaninda kalmali).
2) Online: Online > Oda kur (internetten icin cloudflared kurulu olmali; yoksa oyun izin isteyip indirir) ya da arkadasinin davet kodunu yapistir.
TXT
echo "Paket: $OUT"
