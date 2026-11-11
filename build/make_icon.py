"""Draws src/GroceryPOS.App/app.ico (shopping basket on the brand green). Run: python3 build/make_icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024  # drawn large, downsampled for crisp edges
GREEN, DARK, WHITE = (21, 128, 61, 255), (5, 46, 22, 255), (255, 255, 255, 255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([32, 32, S - 32, S - 32], radius=210, fill=GREEN)

# handle
d.arc([300, 170, 724, 560], start=180, end=360, fill=WHITE, width=64)
# rim
d.rounded_rectangle([170, 400, 854, 500], radius=40, fill=WHITE)
# basket body (trapezoid)
d.polygon([(220, 520), (804, 520), (730, 840), (294, 840)], fill=WHITE)
# slats
for x0, x1 in ((360, 380), (497, 497), (634, 614)):
    d.line([(x0, 575), (x1, 790)], fill=GREEN, width=48)

out = Path(__file__).resolve().parents[1] / "src" / "GroceryPOS.App" / "app.ico"
img.resize((256, 256), Image.LANCZOS).save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print(out)
