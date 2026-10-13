"""Witcher 3 .redswf (CR2W v164) içinden Scaleform arayüz kaynaklarını çıkarır. Yalnız kişisel kullanım.

  python tools/w3swf.py extract <redswf yolu> <çıkış klasörü>

Yazar: <çıkış>/gfx.bin (sıkıştırması açılmış GFx), <çıkış>/atlas/<ad>.png (her doku atlası),
       <çıkış>/sub/<id>.png (GFx DefineSubImage dikdörtgenlerinden kesilen tek tek resimler),
       <çıkış>/symbols.json (karakter kimliği → dışa aktarılan ad)
"""
import io
import json
import os
import re
import struct
import sys
import zlib

from PIL import Image


def read_cr2w(path):
    d = open(path, "rb").read()
    if d[:4] != b"CR2W":
        raise ValueError("CR2W değil")
    u = lambda o: struct.unpack_from("<I", d, o)[0]  # noqa: E731
    tables = [struct.unpack_from("<III", d, 40 + i * 12) for i in range(10)]
    s_off, s_len, _ = tables[0]
    strings = d[s_off:s_off + s_len].split(b"\0")
    c_off, c_cnt, _ = tables[4]
    chunks = []
    for i in range(c_cnt):
        tn, flags, parent, size, doff, tmpl, crc = struct.unpack_from("<HHIIIII", d, c_off + i * 24)
        chunks.append({"type": strings[tn].decode(), "parent": parent, "size": size, "off": doff, "data": d[doff:doff + size]})
    return d, strings, chunks


def parse_props(buf, strings):
    """Özellik bloğu: (ad u16, tür u16, boyut u32 [kendisi dahil], değer) ... 0 ile biter. (bitiş konumu, sözlük) döner."""
    pos = 1  # parçanın ilk baytı 0'dır, özellikler ondan sonra başlar
    props = {}
    while True:
        name = struct.unpack_from("<H", buf, pos)[0]
        if name == 0:
            return pos + 2, props
        typ, size = struct.unpack_from("<HI", buf, pos + 2)
        val = buf[pos + 8:pos + 4 + size]
        props[strings[name].decode()] = (strings[typ].decode(), val)
        pos += 4 + size


def dds_header(w, h, four_cc, linear):
    hdr = b"DDS " + struct.pack("<7I", 124, 0x1007 | 0x80000, h, w, linear, 0, 1)
    hdr += b"\0" * 44
    hdr += struct.pack("<II4s", 32, 4, four_cc) + b"\0" * 20
    hdr += struct.pack("<I", 0x1000) + b"\0" * 16
    return hdr


def extract(path, out):
    d, strings, chunks = read_cr2w(path)
    os.makedirs(os.path.join(out, "atlas"), exist_ok=True)
    os.makedirs(os.path.join(out, "sub"), exist_ok=True)

    swf_chunk = chunks[0]
    blob = swf_chunk["data"]
    m = re.search(rb"CFX[\x00-\x20]", blob)
    if not m:
        raise ValueError("CFX (sıkıştırılmış GFx) bulunamadı")
    ver = blob[m.start() + 3]
    ln = struct.unpack_from("<I", blob, m.start() + 4)[0]
    body = zlib.decompressobj().decompress(blob[m.start() + 8:])
    gfx = b"GFX" + bytes([ver]) + struct.pack("<I", ln) + body
    open(os.path.join(out, "gfx.bin"), "wb").write(gfx)

    # --- Dokular
    atlases = {}  # dosya adı -> PIL görüntüsü
    for ch in chunks:
        if ch["type"] != "CSwfTexture":
            continue
        data = ch["data"]
        end, props = parse_props(data, strings)
        w = struct.unpack("<I", props["width"][1][:4])[0]
        h = struct.unpack("<I", props["height"][1][:4])[0]
        comp_idx = struct.unpack("<H", props["compression"][1][:2])[0]
        comp = strings[comp_idx].decode()
        sv = props["linkageName"][1]  # CR2W dizgisi: ilk bayt uzunluk | 0x80
        name = sv[1:1 + (sv[0] & 0x7f)].decode("latin-1")
        alpha = comp == "TCM_DXTAlpha"
        px = (w + 3) // 4 * ((h + 3) // 4) * (16 if alpha else 8)
        pixels = data[-px:]
        dds = dds_header(w, h, b"DXT5" if alpha else b"DXT1", px) + pixels
        img = Image.open(io.BytesIO(dds)).convert("RGBA")
        atlases[name] = img
        img.save(os.path.join(out, "atlas", re.sub(r"[{}]", "", name).replace(".dds", ".png")))

    # --- GFx etiketleri
    pos = 8
    nbits = gfx[pos] >> 3
    pos += (5 + nbits * 4 + 7) // 8 + 4
    ext = {}      # dış resim kimliği -> dosya adı
    subs = []     # (kimlik, taban kimlik, x0, y0, x1, y1)
    symbols = {}
    while pos < len(gfx):
        hdr = struct.unpack_from("<H", gfx, pos)[0]
        pos += 2
        code, ln = hdr >> 6, hdr & 0x3F
        if ln == 0x3F:
            ln = struct.unpack_from("<I", gfx, pos)[0]
            pos += 4
        body = gfx[pos:pos + ln]
        if code == 1009:  # ExternalImage2
            iid = struct.unpack_from("<H", body, 0)[0]
            names = re.findall(rb"[\x20-\x7e]{4,}", body)
            ext[iid] = names[-1].decode()
        elif code == 1008:  # DefineSubImage
            subs.append(struct.unpack_from("<6H", body, 0))
        elif code in (76, 56):
            cnt = struct.unpack_from("<H", body, 0)[0]
            q = 2
            for _ in range(cnt):
                cid = struct.unpack_from("<H", body, q)[0]
                q += 2
                e = body.index(b"\0", q)
                symbols[cid] = body[q:e].decode("latin-1")
                q = e + 1
        pos += ln
        if code == 0:
            break

    saved = 0
    for iid, base, x0, y0, x1, y1 in subs:
        atlas = atlases.get(ext.get(base, ""))
        if atlas is None:
            continue
        atlas.crop((x0, y0, x1, y1)).save(os.path.join(out, "sub", f"{iid}.png"))
        saved += 1
    json.dump({"symbols": symbols, "external": ext, "subimages": subs}, open(os.path.join(out, "symbols.json"), "w"), indent=1)
    print(f"{os.path.basename(path)}: {len(atlases)} atlas, {saved}/{len(subs)} alt resim, {len(symbols)} sembol")


if __name__ == "__main__":
    if len(sys.argv) >= 4 and sys.argv[1] == "extract":
        extract(sys.argv[2], sys.argv[3])
    else:
        print(__doc__)
