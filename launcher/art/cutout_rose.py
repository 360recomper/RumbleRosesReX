"""Cut a generated rose (flat white background) out to a transparent PNG (LAUNCHER-ROSE).

python -I cutout_rose.py in.png out.png
Background = white pixels connected to the border (flood fill), then a soft 2 px edge; tight crop to a square.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

src, dst = sys.argv[1], sys.argv[2]
im = Image.open(src).convert('RGB')
w, h = im.size
probe = im.copy()
MARK = (0, 255, 0)
for x, y in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)]:
    if probe.getpixel((x, y)) != MARK:
        ImageDraw.floodfill(probe, (x, y), MARK, thresh=60)
bg = np.all(np.asarray(probe) == MARK, axis=2)
alpha = Image.fromarray(np.where(bg, 0, 255).astype(np.uint8))
alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
a = np.asarray(alpha).astype(np.float32) / 255
rgb = np.asarray(im).astype(np.float32)
# unmix the white fringe from semi-transparent edge pixels
safe = np.maximum(a, 1e-3)[..., None]
rgb = np.clip((rgb - 255 * (1 - a[..., None])) / safe, 0, 255)
out = Image.fromarray(np.dstack([rgb, a * 255]).astype(np.uint8), 'RGBA')
box = out.getbbox()
cx, cy = (box[0] + box[2]) // 2, (box[1] + box[3]) // 2
half = max(box[2] - box[0], box[3] - box[1]) // 2 + 4
sq = Image.new('RGBA', (half * 2, half * 2))
sq.paste(out, (half - cx, half - cy))
sq.save(dst)
print(dst, sq.size)
