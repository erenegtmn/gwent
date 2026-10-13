"""texture.cache içinden doku çıkarıcı (kişisel kullanım).

  python tools/w3tex_extract.py info  <altdize>
  python tools/w3tex_extract.py save  <altdize> <çıkış klasörü>
"""
import collections
import io
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(__file__))
import w3texcache as tc  # noqa: E402
from PIL import Image  # noqa: E402

B = chr(92)  # ters bölü


def key(name):
    return name.lower().replace(B, "/")


def raw_pixels(t):
    """Mip 0 (sayfa başındaki akış) açılmış baytlarını döner."""
    with open(tc.CACHE, "rb") as f:
        f.seek(t.page * 4096)
        zsize, size, idx = struct.unpack("<IIB", f.read(9))
        data = f.read(zsize)
    return zlib.decompress(data), size


def expected(t):
    w, h = t.w, t.h
    blocks = ((w + 3) // 4) * ((h + 3) // 4)
    return {"dxt1": blocks * 8, "dxt5": blocks * 16, "rgba": w * h * 4}


def to_image(t, pix):
    w, h = t.w, t.h
    exp = expected(t)
    if len(pix) == exp["rgba"]:
        # B8G8R8A8 varsayımı (RED motoru TRUECOLOR)
        return Image.frombuffer("RGBA", (w, h), pix, "raw", "BGRA", 0, 1)
    four = None
    if len(pix) == exp["dxt5"]:
        four = b"DXT5"
    elif len(pix) == exp["dxt1"]:
        four = b"DXT1"
    if four is None:
        raise ValueError(f"bilinmeyen boyut {len(pix)} ({w}x{h}) fmt{t.t1}")
    hdr = b"DDS " + struct.pack("<7I", 124, 0x1007 | 0x80000, h, w, len(pix), 0, 1) + b"\0" * 44
    hdr += struct.pack("<II4s", 32, 4, four) + b"\0" * 20 + struct.pack("<I", 0x1000) + b"\0" * 16
    return Image.open(io.BytesIO(hdr + pix)).convert("RGBA")


def main():
    cmd, pat = sys.argv[1], sys.argv[2].lower().replace(B, "/")
    ver, tex, _ = tc.load()
    hits = [t for t in tex if pat in key(t.name)]
    if cmd == "info":
        print(len(hits), "eşleşme; (t1,t2) dağılımı:", collections.Counter((t.t1, t.t2) for t in hits))
        for t in hits[:8]:
            pix, size = raw_pixels(t)
            print(t.name.split(B)[-1], t.w, t.h, "t1", t.t1, "açık", len(pix), "beklenen", expected(t))
    elif cmd == "save":
        out = sys.argv[3]
        ok = bad = 0
        for t in hits:
            try:
                pix, _ = raw_pixels(t)
                img = to_image(t, pix)
                path = os.path.join(out, *t.name.split(B))
                path = os.path.splitext(path)[0] + ".png"
                os.makedirs(os.path.dirname(path), exist_ok=True)
                img.save(path)
                ok += 1
            except Exception as ex:  # noqa: BLE001
                bad += 1
                if bad <= 5:
                    print("HATA", t.name, ex)
        print("kaydedilen", ok, "hata", bad)


if __name__ == "__main__":
    main()
