"""Turns numbered PNG frames into an animated WebP (full color, unlike GIF).

python tools/frames2webp.py <frames-folder> <out.webp> <ms-per-frame>
"""
import sys
from pathlib import Path
from PIL import Image, ImageChops

folder, out, ms = Path(sys.argv[1]), sys.argv[2], int(sys.argv[3])
paths = sorted(folder.glob("*.png"))
frames = [Image.open(p).convert("RGB") for p in paths]
# Hold identical frames longer instead of storing them twice: smaller file, same motion.
kept, durations = [], []
for f in frames:
    if kept and ImageChops.difference(f, kept[-1]).getbbox() is None:
        durations[-1] += ms
    else:
        kept.append(f)
        durations.append(ms)
kept[0].save(out, save_all=True, append_images=kept[1:], duration=durations, loop=0, quality=90, method=6)
print(f"{out}: {len(kept)} frames from {len(frames)}, {Path(out).stat().st_size // 1024} KB")
