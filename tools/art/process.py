#!/usr/bin/env python3
"""
Turn a Codex-generated engraving on chroma green into a game sprite (GDD §11).

    uv run --with pillow python tools/art/process.py <generated.png> <out.png> --tiles 2 [--kind building|good|terrain]

Steps: key out the green (Codex's remove_chroma_key.py), map every remaining pixel onto the
ink↔paper axis (white walls become opaque paper so a building hides what stands behind it),
scale so the drawing's width equals its footprint at 128 px per tile (2× the 64 px design size,
so zooming in stays crisp), and trim. Goods get a fixed 96 px box; terrain tiles a 128 px square.
Every output is logged in docs/AI-ASSETS.md by the caller (see tools/art/bible.sh).
"""
import argparse, os, subprocess, sys, tempfile
from PIL import Image

PAPER = (239, 231, 210)
INK = (30, 27, 24)
PX_PER_TILE = 128  # 2× the 64 px design resolution
KEY_SCRIPT = os.path.expanduser("~/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py")


def key_out(src, dst):
    subprocess.run([sys.executable, KEY_SCRIPT, "--input", src, "--out", dst, "--auto-key", "border",
                    "--soft-matte", "--transparent-threshold", "12", "--opaque-threshold", "220", "--despill"],
                   check=True, stdout=subprocess.DEVNULL)


def to_ink(im):
    """Map RGB onto ink..paper by luminance; keep the keyed alpha."""
    im = im.convert("RGBA")
    lum = im.convert("L")
    a = im.getchannel("A")
    out = Image.new("RGBA", im.size)
    px_l, px_a, px_o = lum.load(), a.load(), out.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            l = px_l[x, y] / 255.0
            # Lines in these engravings sit below ~0.55 luminance; stretch so paper is clean and ink solid.
            t = min(1.0, max(0.0, (l - 0.08) / 0.82))
            r = int(INK[0] + (PAPER[0] - INK[0]) * t)
            g = int(INK[1] + (PAPER[1] - INK[1]) * t)
            b = int(INK[2] + (PAPER[2] - INK[2]) * t)
            px_o[x, y] = (r, g, b, px_a[x, y])
    return out


def ink_on_white(im):
    """Ink on white paper: darkness becomes alpha, colour becomes ink, so paper shows through."""
    lum = im.convert("L")
    w, h = lum.size
    out = Image.new("RGBA", (w, h))
    px_l, px_o = lum.load(), out.load()
    for y in range(h):
        for x in range(w):
            a = 255 - px_l[x, y]
            a = 0 if a < 24 else min(255, int((a - 24) * 1.15))
            px_o[x, y] = (INK[0], INK[1], INK[2], a)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--tiles", type=float, default=2, help="footprint width in tiles (buildings)")
    ap.add_argument("--kind", choices=["building", "good", "terrain"], default="building")
    ap.add_argument("--white", action="store_true", help="source is ink on white paper (no chroma key): ink density becomes alpha")
    args = ap.parse_args()

    with tempfile.TemporaryDirectory() as td:
        keyed = os.path.join(td, "keyed.png")
        if args.white:
            im = ink_on_white(Image.open(args.src))
            bbox = im.getbbox() or (0, 0, im.size[0], im.size[1])
        else:
            key_out(args.src, keyed)
            im = Image.open(keyed).convert("RGBA")
            bbox = im.getbbox()
            if bbox is None:
                sys.exit("nothing left after keying")
            im = im.crop(bbox)
            if args.kind == "terrain":
                # Terrain keeps the whole square; the drawing must fill it edge to edge.
                im = Image.open(keyed).convert("RGBA")
            im = to_ink(im)
        w, h = im.size
        if args.kind == "building":
            scale = args.tiles * PX_PER_TILE / w
        elif args.kind == "good":
            scale = 96 / max(w, h)
        else:
            scale = PX_PER_TILE / max(w, h)
        size = (max(1, round(w * scale)), max(1, round(h * scale)))
        im = im.resize(size, Image.LANCZOS)
        if args.kind == "good":
            box = Image.new("RGBA", (96, 96), (0, 0, 0, 0))
            box.paste(im, ((96 - size[0]) // 2, (96 - size[1]) // 2))
            im = box
        elif args.kind == "terrain":
            im = im.resize((PX_PER_TILE, PX_PER_TILE), Image.LANCZOS)
        im.save(args.dst)
        print(f"{args.dst}: {im.size[0]}x{im.size[1]} from {bbox} (scale {scale:.3f})")


if __name__ == "__main__":
    main()
