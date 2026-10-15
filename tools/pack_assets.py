"""Dağıtım paketinin varlıkları ve zip'leri (tools/package.sh çağırır).

  python tools/pack_assets.py <paket klasörü> <sürüm>

1) assets/{cards,music,sfx,ui} paket klasörüne kopyalanırken küçültülür (oyunlarda olduğu gibi kayıplı ama gözle fark edilmez):
   - kart resimleri PNG -> JPEG (köşelerdeki saydamlık oyunda yuvarlak kırpılır; saydam yerler koyu renge doldurulur)
   - saydamlık kullanmayan büyük arayüz resimleri (panoramalar, masa) PNG -> JPEG; simgeler PNG kalır
   - ses efektleri WAV -> MP3 (ffmpeg: tools/_downloads/wolvenkit7/ffmpeg), müzik zaten MP3
2) package.json: sürüm + iki parçanın özeti ("core": Gwent.exe ve OKU.txt; "data": assets ve cloudflared.exe)
3) Zip'ler (paket klasörünün yanına): tam paket ve oyun içi güncelleme için iki parça
   - Gwent-win-x64.zip  : her şey (yeni kurulum)
   - Gwent-core.zip     : Gwent.exe, OKU.txt, package.json (çoğu güncellemede yalnız bu değişir)
   - Gwent-data.zip     : assets/ ve cloudflared.exe
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets")
FFMPEG = os.path.join(ROOT, "tools", "_downloads", "wolvenkit7", "ffmpeg", "ffmpeg.exe")
CARD_QUALITY = 90
UI_QUALITY = 88


def to_jpeg(src, dst, quality, fill=(18, 12, 8)):
    im = Image.open(src)
    if im.mode in ("RGBA", "LA", "P"):
        im = im.convert("RGBA")
        bg = Image.new("RGB", im.size, fill)
        bg.paste(im, mask=im.getchannel("A"))
        im = bg
    else:
        im = im.convert("RGB")
    im.save(dst, "JPEG", quality=quality, optimize=True, progressive=True)


def opaque(path):
    im = Image.open(path)
    if im.mode not in ("RGBA", "LA", "P"):
        return True
    return im.convert("RGBA").getchannel("A").getextrema()[0] >= 250


def copy_assets(out):
    dst_root = os.path.join(out, "assets")
    if os.path.isdir(dst_root):
        shutil.rmtree(dst_root)
    stats = {"jpeg": 0, "mp3": 0, "copy": 0}
    for sub in ("cards", "music", "sfx", "ui"):
        for dirpath, _, files in os.walk(os.path.join(SRC, sub)):
            rel_dir = os.path.relpath(dirpath, SRC)
            os.makedirs(os.path.join(dst_root, rel_dir), exist_ok=True)
            for f in files:
                src = os.path.join(dirpath, f)
                stem, ext = os.path.splitext(f)
                dst = os.path.join(dst_root, rel_dir, f)
                ext = ext.lower()
                in_ui_root = rel_dir == "ui"
                if ext == ".png" and (sub == "cards" or (in_ui_root and os.path.getsize(src) > 150_000 and opaque(src))):
                    to_jpeg(src, os.path.join(dst_root, rel_dir, stem + ".jpg"), CARD_QUALITY if sub == "cards" else UI_QUALITY)
                    stats["jpeg"] += 1
                elif ext == ".wav" and sub == "sfx" and os.path.isfile(FFMPEG):
                    subprocess.run([FFMPEG, "-loglevel", "error", "-y", "-i", src, "-codec:a", "libmp3lame", "-q:a", "3",
                                    os.path.join(dst_root, rel_dir, stem + ".mp3")], check=True)
                    stats["mp3"] += 1
                else:
                    shutil.copyfile(src, dst)
                    stats["copy"] += 1
    return stats


def tree_hash(out, paths):
    h = hashlib.sha256()
    for p in paths:
        full = os.path.join(out, p)
        files = [full] if os.path.isfile(full) else sorted(
            os.path.join(d, f) for d, _, fs in os.walk(full) for f in fs) if os.path.isdir(full) else []
        for f in files:
            h.update(os.path.relpath(f, out).replace("\\", "/").encode())
            with open(f, "rb") as fh:
                h.update(hashlib.sha256(fh.read()).digest())
    return h.hexdigest()[:16]


CORE = ["Gwent.exe", "OKU.txt", "package.json"]
DATA = ["assets", "cloudflared.exe"]


def write_zip(path, out, items):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for item in items:
            full = os.path.join(out, item)
            if os.path.isfile(full):
                z.write(full, item)
            elif os.path.isdir(full):
                for d, _, fs in os.walk(full):
                    for f in fs:
                        fp = os.path.join(d, f)
                        z.write(fp, os.path.relpath(fp, out))
    return os.path.getsize(path)


def main():
    out, version = sys.argv[1], sys.argv[2]
    stats = copy_assets(out)
    manifest = {"version": version, "core": tree_hash(out, ["Gwent.exe", "OKU.txt"]), "data": tree_hash(out, DATA)}
    with open(os.path.join(out, "package.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1)
    parent = os.path.dirname(os.path.abspath(out))
    full = write_zip(os.path.join(parent, "Gwent-win-x64.zip"), out, CORE + DATA)
    core = write_zip(os.path.join(parent, "Gwent-core.zip"), out, CORE)
    data = write_zip(os.path.join(parent, "Gwent-data.zip"), out, ["package.json"] + DATA)
    mb = lambda n: f"{n / 1048576:.1f} MB"
    print(f"Varlıklar: {stats['jpeg']} JPEG, {stats['mp3']} MP3, {stats['copy']} kopya")
    print(f"package.json: {manifest}")
    print(f"Zip: tam {mb(full)}, core {mb(core)}, data {mb(data)}")


if __name__ == "__main__":
    main()
