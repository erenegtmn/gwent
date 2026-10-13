"""Gwent ses efektlerini bankalardan çıkarıp assets/sfx/<olay>[_n].wav olarak yazar (kişisel kullanım).

  python tools/w3sfx_export.py
Gereken: assets/extracted/audio/banks/{gui,gui_ep2,gwint_ep2}.bnk (AssetExtractor ile çıkarılır),
         tools/_downloads/vgmstream/vgmstream-cli.exe, WolvenKit-7'nin SoundCache/soundbanksinfo.xml
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(__file__))
import w3bnk  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BANKS = os.path.join(ROOT, "assets", "extracted", "audio", "banks")
OUT = os.path.join(ROOT, "assets", "sfx")
VGM = os.path.join(ROOT, "tools", "_downloads", "vgmstream", "vgmstream-cli.exe")
INFO = os.path.join(ROOT, "tools", "_downloads", "wolvenkit7", "SoundCache", "soundbanksinfo.xml")
SILENT = 2319  # birçok yetenek olayının paylaştığı kısa boş ses


def main():
    os.makedirs(OUT, exist_ok=True)
    tmp = os.path.join(ROOT, "assets", "extracted", "audio", "wem")
    os.makedirs(tmp, exist_ok=True)
    names = {int(e.get("Id")): e.get("Name") for e in ET.parse(INFO).getroot().iter("Event")}
    done = {}
    for bank in ("gui", "gui_ep2", "gwint_ep2"):
        path = os.path.join(BANKS, bank + ".bnk")
        if not os.path.exists(path):
            print("yok:", path)
            continue
        _, _, media, objs = w3bnk.read_bank(path)
        for eid, name in sorted(names.items(), key=lambda kv: kv[1]):
            if eid not in objs or "gwint" not in name:
                continue
            srcs = []
            for sid in w3bnk.event_sources(eid, objs):
                src = w3bnk.sound_source(objs[sid][1])[2]
                if src in media and len(media[src]) != SILENT:
                    srcs.append(src)
            for i, src in enumerate(srcs):
                short = name.replace("gui_gwint_", "")
                base = short if len(srcs) == 1 else f"{short}_{i + 1}"
                wem = os.path.join(tmp, f"{src}.wem")
                open(wem, "wb").write(media[src])
                wav = os.path.join(OUT, base + ".wav")
                r = subprocess.run([VGM, "-o", wav, wem], capture_output=True, text=True)
                if r.returncode != 0 or not os.path.exists(wav):
                    print("dönüştürülemedi:", name, src, r.stderr[:100])
                    continue
                done[base] = os.path.getsize(wav)
    for k, v in sorted(done.items()):
        print(f"{k:34s} {v / 1024:7.1f} KB")
    print(len(done), "efekt yazıldı ->", OUT)


if __name__ == "__main__":
    main()
