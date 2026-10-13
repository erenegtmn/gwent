"""Witcher 3 kurulumundan Gwent görsel ve seslerini çıkarıp assets/ klasörünü üretir (kişisel kullanım).

  python tools/extract_assets.py [--game "C:\\...\\The Witcher 3"] [--skip-audio]

Ön koşullar (elle indirilir, depoda yoktur — bkz. tools/OKUBENI.md):
  tools/_downloads/wolvenkit7/   WolvenKit-7 7.2.0 (github.com/WolvenKit/WolvenKit-7/releases)
  tools/_downloads/vgmstream/    vgmstream-win64 (github.com/vgmstream/vgmstream/releases)
  Python 3 + Pillow, .NET SDK 9

Adımlar: paketlerden XML + GFx arayüz dosyaları → atlaslar ve alt resimler → kart resimleri (texture.cache)
         → kart eşlemesi → assets/ui + assets/cards → müzik (soundspc.cache) → ses efektleri (ses bankaları).
"""
import argparse
import os
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, "tools")
PY = sys.executable
RAW = os.path.join(ROOT, "assets", "extracted", "raw")
EX = os.path.join(ROOT, "assets", "extracted")


def run(cmd, **kw):
    print("\n$", " ".join(f'"{c}"' if " " in c else c for c in cmd))
    r = subprocess.run(cmd, **kw)
    if r.returncode != 0:
        sys.exit(f"adım başarısız (çıkış kodu {r.returncode})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", default=os.environ.get("W3_GAME"))
    ap.add_argument("--skip-audio", action="store_true")
    a = ap.parse_args()
    if a.game:
        os.environ["W3_GAME"] = a.game
    game = os.environ.get("W3_GAME", r"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3")
    if not os.path.isdir(os.path.join(game, "content", "content0")):
        sys.exit(f"Witcher 3 kurulumu bulunamadı: {game}\n--game ile yolu ver.")
    for need in ("wolvenkit7", "vgmstream"):
        if not os.path.isdir(os.path.join(TOOLS, "_downloads", need)):
            sys.exit(f"tools/_downloads/{need} yok. tools/OKUBENI.md'deki indirme adımlarına bak.")
    env = dict(os.environ)
    env["PYTHONIOENCODING"] = "utf-8"

    # 1) oyun veri tabanı (kartlar) ve arayüz dosyası
    run([PY, os.path.join(TOOLS, "w3bundle.py"), "extract", "gameplay\\items\\def_gwint", RAW], env=env)
    run([PY, os.path.join(TOOLS, "w3bundle.py"), "extract", "swf\\gwint\\", RAW], env=env)
    # 2) arayüz atlasları ve alt resimler
    run([PY, os.path.join(TOOLS, "w3swf.py"), "extract",
         os.path.join(RAW, "gameplay", "gui_new", "swf", "gwint", "gwint_game.redswf"), os.path.join(EX, "gwint_game")], env=env)
    # 3) kart resimleri
    run([PY, os.path.join(TOOLS, "w3tex_extract.py"), "save", "gui_new/icons/gwint/", os.path.join(EX, "tex")], env=env)
    # arayüz yazı tipi: GFx yazı kütüphanesi -> TTF (pip install fonttools gerekir)
    run([PY, os.path.join(TOOLS, "w3bundle.py"), "extract", "witcher3\fonts_en.redswf", os.path.join(EX, "fonts")], env=env)
    run([PY, os.path.join(TOOLS, "w3_font_extract.py")], env=env)
    # ana menü: oyunun kendi arka plan panoramaları ve logosu
    run([PY, os.path.join(TOOLS, "w3tex_extract.py"), "save", "gui_new/icons/menubackground/", os.path.join(EX, "tex")], env=env)
    run([PY, os.path.join(TOOLS, "w3tex_extract.py"), "save", "gui_new/icons/logos/witcherlogo_en", os.path.join(EX, "tex")], env=env)
    # 4) kart eşlemesi ve derleme
    run([PY, os.path.join(TOOLS, "w3cards_xml.py"), "compare"], env=env)
    run([PY, os.path.join(TOOLS, "w3_ui_assets.py")], env=env)

    if a.skip_audio:
        print("\nSes atlandı (--skip-audio).")
        return
    # 5) ses: AssetExtractor ile müzik + bankalar, sonra dönüştürme
    ae = os.path.join(TOOLS, "AssetExtractor")
    run(["dotnet", "build", "-c", "Release", "-nologo", "-v", "q"], cwd=ae)
    out = os.path.join(ae, "bin", "Release", "net9.0-windows")
    sc = os.path.join(TOOLS, "_downloads", "wolvenkit7", "SoundCache")
    if not os.path.isdir(os.path.join(out, "SoundCache")):
        shutil.copytree(sc, os.path.join(out, "SoundCache"))
    exe = [os.path.join(out, "AssetExtractor.exe")]
    audio = os.path.join(EX, "audio")
    run(exe + ["extract", "sound", "music\\gwent_game", audio])
    for bank in ("gui.bnk", "gui_ep2.bnk", "gwint_ep2.bnk"):
        run(exe + ["extract", "sound", bank, os.path.join(audio, "banks")])
    vgm = os.path.join(TOOLS, "_downloads", "vgmstream", "vgmstream-cli.exe")
    ff = os.path.join(TOOLS, "_downloads", "wolvenkit7", "ffmpeg", "ffmpeg.exe")
    music = os.path.join(ROOT, "assets", "music")
    os.makedirs(music, exist_ok=True)
    wems = os.path.join(audio, "sfx", "music", "gwent_game")
    for f in sorted(os.listdir(wems)):
        stem = os.path.splitext(f)[0]
        name = stem.rsplit("_arranged_", 1)[0].replace("tw3_novigrad_06_", "novigrad_").replace("skl_11_", "skellige_")
        wav = os.path.join(audio, name + ".wav")
        run([vgm, "-o", wav, os.path.join(wems, f)], stdout=subprocess.DEVNULL)
        run([ff, "-y", "-loglevel", "error", "-i", wav, "-codec:a", "libmp3lame", "-q:a", "3", os.path.join(music, name + ".mp3")])
        os.remove(wav)
    run([PY, os.path.join(TOOLS, "w3sfx_export.py")], env=env)
    print("\nTamam: assets/ui, assets/cards, assets/music, assets/sfx hazır.")


if __name__ == "__main__":
    main()
