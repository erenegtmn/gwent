"""Oyunun arayüz yazı tipini (PF Din Text Cond Pro) GFx .redswf içindeki vektör harflerden TTF'ye çevirir.
Yalnız kişisel kullanım; üretilen dosyalar depoya girmez (assets/ui/fonts/).

  python tools/w3_font_extract.py            # assets/extracted/fonts/.../fonts_en.redswf -> assets/ui/fonts/*.ttf
Gerekenler: pip install fonttools
"""
import os
import re
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import w3swf  # noqa: E402
from fontTools.fontBuilder import FontBuilder  # noqa: E402
from fontTools.pens.ttGlyphPen import TTGlyphPen  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "extracted", "fonts", "gameplay", "gui_new", "swf", "witcher3", "fonts_en.redswf")
OUT = os.path.join(ROOT, "assets", "ui", "fonts")
SCALE = 10  # SWF em = 20480 birim, TTF em = 2048


class Bits:
    def __init__(self, data, pos):
        self.d, self.p, self.bit = data, pos, 0

    def u(self, n):
        v = 0
        for _ in range(n):
            byte = self.d[self.p + (self.bit >> 3)]
            v = (v << 1) | ((byte >> (7 - (self.bit & 7))) & 1)
            self.bit += 1
        return v

    def s(self, n):
        if n == 0:
            return 0
        v = self.u(n)
        return v - (1 << n) if v & (1 << (n - 1)) else v


def parse_shape(data, pos):
    """Tek bir glif şeklini (SWF SHAPE) ayrıştırıp kontur listesi döner: [[('M',x,y),('L',x,y),('Q',cx,cy,x,y)...]]"""
    b = Bits(data, pos)
    nfill, nline = b.u(4), b.u(4)
    x = y = 0
    contours, cur = [], None
    while True:
        if b.u(1) == 0:
            flags = b.u(5)
            if flags == 0:
                break
            if flags & 1:  # moveTo
                n = b.u(5)
                x, y = b.s(n), b.s(n)
                if cur:
                    contours.append(cur)
                cur = [("M", x, y)]
            if flags & 2:
                b.u(nfill)
            if flags & 4:
                b.u(nfill)
            if flags & 8:
                b.u(nline)
            if flags & 16:
                raise ValueError("yeni stiller beklenmiyordu")
        else:
            if cur is None:
                cur = [("M", x, y)]
            if b.u(1):  # düz çizgi
                n = b.u(4) + 2
                if b.u(1):
                    dx, dy = b.s(n), b.s(n)
                elif b.u(1):
                    dx, dy = 0, b.s(n)
                else:
                    dx, dy = b.s(n), 0
                x, y = x + dx, y + dy
                cur.append(("L", x, y))
            else:  # eğri
                n = b.u(4) + 2
                cx, cy = x + b.s(n), y + b.s(n)
                x, y = cx + b.s(n), cy + b.s(n)
                cur.append(("Q", cx, cy, x, y))
    if cur:
        contours.append(cur)
    return contours


def pen_glyph(contours):
    pen = TTGlyphPen(None)
    for c in contours:
        for seg in c:
            if seg[0] == "M":
                pen.moveTo((seg[1] // SCALE, -seg[2] // SCALE))
            elif seg[0] == "L":
                pen.lineTo((seg[1] // SCALE, -seg[2] // SCALE))
            else:
                pen.qCurveTo((seg[1] // SCALE, -seg[2] // SCALE), (seg[3] // SCALE, -seg[4] // SCALE))
        pen.closePath()
    return pen.glyph()


def read_fonts():
    d, strings, chunks = w3swf.read_cr2w(SRC)
    blob = chunks[0]["data"]
    m = re.search(rb"CFX[\x00-\x20]", blob)
    gfx = zlib.decompressobj().decompress(blob[m.start() + 8:])
    nbits = gfx[0] >> 3
    pos = (5 + nbits * 4 + 7) // 8 + 4
    fonts = []
    while pos < len(gfx):
        hdr = struct.unpack_from("<H", gfx, pos)[0]
        pos += 2
        code, ln = hdr >> 6, hdr & 0x3F
        if ln == 0x3F:
            ln = struct.unpack_from("<I", gfx, pos)[0]
            pos += 4
        if code == 75:
            fonts.append(parse_font3(gfx[pos:pos + ln]))
        pos += ln
    return fonts


def parse_font3(b):
    flags = b[2]
    nl = b[4]
    name = b[5:5 + nl].decode("latin-1").strip()
    q = 5 + nl
    n = struct.unpack_from("<H", b, q)[0]
    q += 2
    wide = bool(flags & 0x08)
    fmt = "<I" if wide else "<H"
    size = 4 if wide else 2
    offs = [struct.unpack_from(fmt, b, q + i * size)[0] for i in range(n)]
    table_start = q
    code_off = struct.unpack_from(fmt, b, q + n * size)[0]
    code_pos = table_start + code_off
    codes = list(struct.unpack_from(f"<{n}H", b, code_pos))
    glyphs = []
    for i in range(n):
        glyphs.append(parse_shape(b, table_start + offs[i]))
    r = code_pos + n * 2
    ascent, descent, leading = struct.unpack_from("<hhh", b, r)
    adv = list(struct.unpack_from(f"<{n}h", b, r + 6))
    return {"name": name, "bold": bool(flags & 1), "italic": bool(flags & 2), "codes": codes, "glyphs": glyphs,
            "ascent": ascent // SCALE, "descent": descent // SCALE, "adv": [a // SCALE for a in adv]}


def build(font, path):
    order = [".notdef"]
    cmap, glyf, metrics = {}, {".notdef": TTGlyphPen(None).glyph()}, {".notdef": (1000, 0)}
    for i, c in enumerate(font["codes"]):
        if c == 0 or c in cmap:
            continue
        gn = f"uni{c:04X}"
        order.append(gn)
        cmap[c] = gn
        glyf[gn] = pen_glyph(font["glyphs"][i])
        metrics[gn] = (max(0, font["adv"][i]), 0)
    # boşluk karakteri
    fb = FontBuilder(2048, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(glyf)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=font["ascent"], descent=-abs(font["descent"]))
    style = ("Bold Italic" if font["bold"] and font["italic"] else "Bold" if font["bold"] else "Italic" if font["italic"] else "Regular")
    fb.setupNameTable({"familyName": font["name"], "styleName": style})
    # Ağırlık/stil bilgisi: WPF Bold ve Italic yüzleri bu alanlardan eşler (yoksa sistem yazı tipine düşer)
    fs = (0x20 if font["bold"] else 0) | (0x01 if font["italic"] else 0) | (0x40 if not font["bold"] and not font["italic"] else 0)
    fb.setupOS2(sTypoAscender=font["ascent"], sTypoDescender=-abs(font["descent"]), usWinAscent=font["ascent"], usWinDescent=abs(font["descent"]),
                fsType=0, usWeightClass=700 if font["bold"] else 400, fsSelection=fs)
    fb.setupPost(italicAngle=-10 if font["italic"] else 0)
    fb.font["head"].macStyle = (1 if font["bold"] else 0) | (2 if font["italic"] else 0)
    fb.save(path)
    return len(cmap)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in read_fonts():
        style = "bold" if f["bold"] else "italic" if f["italic"] else "regular"
        path = os.path.join(OUT, f"din_{style}.ttf")
        n = build(f, path)
        print(f"{f['name']} {style}: {n} karakter -> {path}")
        want = [0xE7, 0x11F, 0x131, 0x15F, 0xF6, 0xFC, 0x130, 0x15E, 0x11E, 0xC7, 0xD6, 0xDC]
        miss = [hex(c) for c in want if c not in f["codes"]]
        print("  Türkçe eksikleri:", miss or "yok")


if __name__ == "__main__":
    main()
