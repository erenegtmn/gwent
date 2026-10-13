"""Uygulama simgesini üretir: src/GwentClient/app_source.png (1024x1024) -> app.ico. Kullanım: python tools/make_icon.py

Kaynak resmin köşeleri yuvarlatılmış kutu biçimindedir; dış köşeler şeffaflaştırılır, 16-256 px arası boyutlar yazılır.
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "GwentClient", "app_source.png")
OUT = os.path.join(ROOT, "src", "GwentClient", "app.ico")


def main():
    img = Image.open(SRC).convert("RGBA").resize((1024, 1024), Image.LANCZOS)
    mask = Image.new("L", (1024, 1024), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, 1023, 1023], radius=150, fill=255)
    img.putalpha(mask)
    img.save(OUT, sizes=[(s, s) for s in (256, 128, 64, 48, 32, 24, 16)])
    print("OK:", OUT)


if __name__ == "__main__":
    main()
