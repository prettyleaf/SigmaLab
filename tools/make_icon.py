"""Генерирует src/Resources/app.ico: σ на фирменном фоне ТУСУР (#3C388D) с «точечной волной» (#9ABF38)."""
import math, sys
from PIL import Image, ImageDraw, ImageFont

FONT = sys.argv[1]
OUT = sys.argv[2]
PRIMARY = (0x3C, 0x38, 0x8D, 255)
ACCENT = (0x9A, 0xBF, 0x38, 255)

def render(size):
    S = 1024
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = 24
    d.rounded_rectangle([pad, pad, S - pad, S - pad], radius=190, fill=PRIMARY)
    font = ImageFont.truetype(FONT, 700)
    bbox = d.textbbox((0, 0), "σ", font=font)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((S - w) / 2 - bbox[0], S * 0.40 - h / 2 - bbox[1]), "σ", font=font, fill=(255, 255, 255, 255))
    n = 9
    for i in range(n):
        x = S * (0.15 + 0.70 * i / (n - 1))
        y = S * (0.80 + 0.06 * math.sin(i * 0.95))
        r = S * (0.030 + 0.012 * abs(math.cos(i * 0.6)))
        d.ellipse([x - r, y - r, x + r, y + r], fill=ACCENT)
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
imgs = [render(s) for s in sizes]
imgs[-1].save(OUT, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save(OUT.replace(".ico", "_256.png"))
print("ok")
