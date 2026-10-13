"""Witcher 3 (sürüm 5.x, next-gen) .bundle okuyucu. Yalnız kişisel kullanım için.

Düzen (doğrulandı):
  başlık 32 bayt: "POTATO70", u32 paketBoyu, u32 0, u32 tabloBoyu, u32 ?, 8 sıfır
  tablo: tabloBoyu/0x130 kayıt, her biri:
     ad[0x100] (NUL ile biter), özet[16], u64 konum, u32 boyut, u32 sıkıştırılmışBoyut, u32 crc, u32 sıkıştırma, 8 sıfır
  sıkıştırma: 0 yok, 1 zlib, 2 snappy, 3 doboz, 4 lz4, 5 lz4hc
"""
import os
import struct
import sys
import zlib

GAME = os.environ.get("W3_GAME", r"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3")
BUNDLES = os.path.join(GAME, "content", "content0", "bundles")
ENTRY = 0x130


class Entry:
    __slots__ = ("bundle", "name", "offset", "size", "zsize", "comp")

    def __init__(self, bundle, name, offset, size, zsize, comp):
        self.bundle, self.name, self.offset, self.size, self.zsize, self.comp = bundle, name, offset, size, zsize, comp


def read_table(path):
    with open(path, "rb") as f:
        head = f.read(32)
        if head[:8] != b"POTATO70":
            raise ValueError("POTATO70 değil: " + path)
        table_size = struct.unpack_from("<I", head, 16)[0]
        table = f.read(table_size)
    out = []
    for i in range(table_size // ENTRY):
        raw = table[i * ENTRY:(i + 1) * ENTRY]
        name = raw[:0x100].split(b"\0")[0].decode("latin-1")
        offset, size, zsize, crc, comp = struct.unpack_from("<QIIII", raw, 0x110)
        out.append(Entry(os.path.basename(path), name, offset, size, zsize, comp))
    return out


def all_entries(only=None):
    res = []
    for fn in sorted(os.listdir(BUNDLES)):
        if not fn.endswith(".bundle") or (only and fn not in only):
            continue
        try:
            res.extend(read_table(os.path.join(BUNDLES, fn)))
        except Exception as ex:  # noqa: BLE001
            print("okunamadı", fn, ex, file=sys.stderr)
    return res


def extract(e, dest_root):
    path = os.path.join(BUNDLES, e.bundle)
    with open(path, "rb") as f:
        f.seek(e.offset)
        raw = f.read(e.zsize)
    if e.comp == 0:
        data = raw
    elif e.comp == 1:
        data = zlib.decompress(raw)
    else:
        raise NotImplementedError(f"sıkıştırma türü {e.comp}: {e.name}")
    out = os.path.join(dest_root, e.name.replace("\\", os.sep))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "wb") as o:
        o.write(data)
    return out, len(data)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "stat"
    if cmd == "stat":
        ents = all_entries()
        print(len(ents), "kayıt")
        by = {}
        for e in ents:
            by.setdefault(e.bundle, []).append(e)
        for b, l in sorted(by.items(), key=lambda kv: -len(kv[1])):
            comps = {}
            for e in l:
                comps[e.comp] = comps.get(e.comp, 0) + 1
            print(f"{len(l):7d}  {b:42s} sıkıştırma: {comps}")
    elif cmd == "find":
        pat = sys.argv[2].lower()
        n = 0
        for e in all_entries():
            if pat in e.name.lower():
                print(f"{e.bundle:28s} c{e.comp} {e.size:9d}  {e.name}")
                n += 1
                if n >= int(sys.argv[3] if len(sys.argv) > 3 else 200):
                    break
    elif cmd == "extract":
        pat = sys.argv[2].lower()
        dest = sys.argv[3]
        ok = 0
        for e in all_entries():
            if pat in e.name.lower():
                try:
                    extract(e, dest)
                    ok += 1
                except Exception as ex:  # noqa: BLE001
                    print("HATA", e.name, ex)
        print("çıkarılan:", ok)
