"""Wwise ses bankası (.bnk) okuyucu: olay -> WEM eşlemesi çıkarır. Kişisel kullanım.

  python tools/w3bnk.py <bank.bnk> [olay_adı_alt_dizesi]
Mantık: HIRC içindeki Event(4) -> Action(3) -> hedef(Sound(2) ya da Container(5/6/9)) -> Sound kaynak WEM kimliği.
Konteynerlerde çocuk kimlikleri, nesne gövdesinde "sayı + kimlikler" desenini arayarak bulunur (sürüme bağımlı
parametre bloğunu çözmeden).
"""
import re
import struct
import sys
import xml.etree.ElementTree as ET


def fnv(name):
    h = 2166136261
    for c in name.lower().encode():
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= c
    return h


def read_bank(path):
    d = open(path, "rb").read()
    pos = 0
    secs = {}
    while pos + 8 <= len(d):
        tag = d[pos:pos + 4]
        size = struct.unpack_from("<I", d, pos + 4)[0]
        secs[tag] = (pos + 8, size)
        pos += 8 + size
    bk, bs = secs[b"BKHD"]
    version, bank_id = struct.unpack_from("<II", d, bk)
    media = {}
    if b"DIDX" in secs and b"DATA" in secs:
        io_, isz = secs[b"DIDX"]
        do, _ = secs[b"DATA"]
        for i in range(isz // 12):
            mid, off, sz = struct.unpack_from("<III", d, io_ + i * 12)
            media[mid] = d[do + off:do + off + sz]
    objs = {}
    if b"HIRC" in secs:
        ho, hs = secs[b"HIRC"]
        n = struct.unpack_from("<I", d, ho)[0]
        p = ho + 4
        for _ in range(n):
            t = d[p]
            sz = struct.unpack_from("<I", d, p + 1)[0]
            oid = struct.unpack_from("<I", d, p + 5)[0]
            objs[oid] = (t, d[p + 9:p + 5 + sz])
            p += 5 + sz
    return version, bank_id, media, objs


def children(obj_id, objs, depth=0):
    """Bir nesnenin (Sound ise kendisi) altındaki tüm Sound(2) kimliklerini döner."""
    t, body = objs[obj_id]
    if t == 2:
        return [obj_id]
    ids = set(objs)
    out = []
    # gövdede "sayı + kimlikler" desenini ara
    for p in range(0, len(body) - 8, 1):
        cnt = struct.unpack_from("<I", body, p)[0]
        if 1 <= cnt <= 64 and p + 4 + cnt * 4 <= len(body):
            cand = struct.unpack_from(f"<{cnt}I", body, p + 4)
            if all(c in ids and c != obj_id for c in cand):
                for c in cand:
                    out.extend(children(c, objs, depth + 1) if depth < 6 else [])
                return out
    return out


def sound_source(body):
    """Sound SFX gövdesi: plugin u32, akış türü u32, kaynak kimliği u32, bellek boyutu u32 ..."""
    plugin = struct.unpack_from("<I", body, 0)[0]
    stream = body[4]
    src, size = struct.unpack_from("<II", body, 5)
    return plugin, stream, src, size


def event_sources(event_id, objs):
    t, body = objs[event_id]
    n = body[0]  # Wwise 2018 (sürüm 150): adet tek baytlık değişken uzunluklu sayı
    out = []
    for i in range(n):
        aid = struct.unpack_from("<I", body, 1 + i * 4)[0]
        if aid not in objs:
            continue
        at, ab = objs[aid]
        atype = struct.unpack_from("<H", ab, 0)[0]
        target = struct.unpack_from("<I", ab, 2)[0]
        if (atype & 0xFF) == 0x03 and target in objs:  # Play
            out.extend(children(target, objs))
    return out


if __name__ == "__main__":
    bank = sys.argv[1]
    version, bank_id, media, objs = read_bank(bank)
    import collections
    if len(sys.argv) > 2:
        info = ET.parse(sys.argv[2]).getroot()
        names = {int(e.get("Id")): e.get("Name") for e in info.iter("Event")}
        pat = sys.argv[3].lower() if len(sys.argv) > 3 else ""
        for eid, name in sorted(names.items(), key=lambda kv: kv[1]):
            if eid in objs and pat in name.lower():
                srcs = event_sources(eid, objs)
                info_s = []
                for sid in srcs:
                    plugin, stream, src, size = sound_source(objs[sid][1])
                    info_s.append(f"{src}({len(media.get(src, b''))}B{'' if src in media else ',gömülü değil'})")
                print(f"{name:45s} {len(srcs)} ses: {' '.join(info_s)}")
        sys.exit(0)
    print("sürüm", version, "banka", bank_id, "gömülü wem", len(media), "nesne türleri", dict(collections.Counter(t for t, _ in objs.values())))
