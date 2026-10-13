"""Witcher 3 (5.x) texture.cache okuyucu (sürüm 7). Yalnız kişisel kullanım.

Dosya sonundan geriye: altbilgi(32) | giriş tablosu | dizge tablosu | mip tablosu(u32'ler)
Altbilgi: crc u64, kullanılanSayfa u32, girişSayısı u32, dizgeBoyu u32, mipSayısı u32, 'HCXT', sürüm u32
Giriş (52 bayt): özet u32, dizgeKonumu i32, sayfaKonumu u32, sıkışıkBoyut u32, açıkBoyut u32, hizalama u32,
                 genişlik u16, yükseklik u16, mipSayısı u16, dilimSayısı u16, mipKonumİndeksi i32, mipAdedi i32,
                 zaman i64, tür1 u8, tür2 u8, küp u8, ?
Veri: sayfaKonumu*4096 adresinde: u32 sıkışıkBoyut, u32 açıkBoyut, u8 mipİdx, sıkıştırılmış veri (+ mip blokları)
"""
import os
import struct
import sys

CACHE = os.path.join(os.environ.get("W3_GAME", r"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3"),
                     "content", "content0", "texture.cache")


class Tex:
    __slots__ = ("name", "page", "csize", "usize", "w", "h", "mips", "slices", "mip_idx", "mip_n", "t1", "t2", "cube")


def load(path=CACHE, entry_size=52):
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        f.seek(size - 32)
        crc, used, n, strsz, miptab, magic, ver = struct.unpack("<QIIIIII", f.read(32))
        if magic.to_bytes(4, "little") != b"HCXT":
            raise ValueError("HCXT değil")
        total = 32 + n * entry_size + strsz + miptab * 4
        f.seek(size - total)
        mip_offsets = struct.unpack(f"<{miptab}I", f.read(miptab * 4))
        strings = f.read(strsz)
        entries = f.read(n * entry_size)
    names = strings.split(b"\0")
    out = []
    for i in range(n):
        e = entries[i * entry_size:(i + 1) * entry_size]
        t = Tex()
        (h, stroff, page, cs, us, align, w, ht, mips, slices, mip_idx, mip_n, ts, t1, t2, cube, _u) = struct.unpack("<IiIIIIHHHHiiqBBBB", e)
        t.name = names[i].decode("latin-1") if i < len(names) else ""
        t.page, t.csize, t.usize, t.w, t.h, t.mips, t.slices = page, cs, us, w, ht, mips, slices
        t.mip_idx, t.mip_n, t.t1, t.t2, t.cube = mip_idx, mip_n, t1, t2, cube
        out.append(t)
    return ver, out, mip_offsets


if __name__ == "__main__":
    ver, tex, _ = load()
    print("sürüm", ver, "girdi", len(tex))
    pat = sys.argv[1].lower() if len(sys.argv) > 1 else ""
    n = 0
    for t in tex:
        if pat in t.name.lower():
            print(f"{t.w:5d}x{t.h:<5d} fmt{t.t1:3d} mip{t.mips:2d} {t.name}")
            n += 1
            if n >= (int(sys.argv[2]) if len(sys.argv) > 2 else 50):
                break
