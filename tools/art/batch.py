#!/usr/bin/env python3
"""Cut every plate and icon the game is missing, with the locked style-bible templates (GDD §11).

    python3 tools/art/batch.py                 # generate what is missing, process, log, import
    python3 tools/art/batch.py --only coke_oven aeroplane
    python3 tools/art/batch.py --force         # regenerate everything (a style change)
    python3 tools/art/batch.py --sheet         # only rebuild the review contact sheets
    python3 tools/art/batch.py --jobs 6

Subjects: tools/art/subjects.tsv (kind, id, footprint tiles, subject sentence). Generations land in
scratch/art-batch/<kind>-<id>.png and are never deleted; the processed sprite goes to
assets/art/<kind>s/<id>.png through tools/art/process.py; every sprite gets a row in
docs/AI-ASSETS.md. Codex runs with stdin from /dev/null (it eats a loop's input otherwise).
Plain Python on purpose: only the processing step needs Pillow (through uv).
"""
from __future__ import annotations

import argparse
import concurrent.futures
import csv
import datetime
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRATCH = os.path.join(ROOT, "scratch", "art-batch")
LOG = os.path.join(ROOT, "docs", "AI-ASSETS.md")
UV = os.path.expanduser("~/.local/bin/uv")

BUILDING = (
    "1920s newspaper steel engraving of {subject}. Pure black ink lines only: crosshatch, hatching and stipple, "
    "no grey or coloured fill, white paper areas left empty; the only colour is the flat solid pure green background "
    "#00FF00 filling the entire background. Military oblique projection like a board-game piece: the square footprint "
    "is seen from directly above as a true square, the front wall is seen straight-on below the roof, side walls barely "
    "visible, parallel lines stay parallel, no vanishing points, no perspective, no ground, no cast shadow, no text, "
    "centred, filling the frame."
)
GOOD = (
    "a single {subject} as a bold 1920s newspaper woodcut icon: thick black outlines, a little hatching for shadow, "
    "designed to read clearly at postage-stamp size. Pure black ink only, no grey or coloured fill, white paper left "
    "empty inside the outline; the only colour is the flat solid pure green background #00FF00 filling the entire "
    "background. Seen straight on, centred, large, no text, no cast shadow."
)
ASK = (
    "Use your built-in image generation tool (image_gen) to generate exactly one image, then copy the PNG to {out} "
    "(do not resize). Reply with only the saved path. Prompt: {prompt}"
)


def subjects(path: str) -> list[dict]:
    with open(path, newline="") as f:
        return list(csv.DictReader(f, delimiter="\t"))


def target(row: dict) -> str:
    return os.path.join(ROOT, "assets", "art", row["kind"] + "s", row["id"] + ".png")


def generation(row: dict) -> str:
    return os.path.join(SCRATCH, f"{row['kind']}-{row['id']}.png")


def generate(row: dict) -> tuple[dict, bool, str]:
    out = generation(row)
    if os.path.exists(out):
        return row, True, "kept"
    prompt = (BUILDING if row["kind"] == "building" else GOOD).format(subject=row["subject"])
    ask = ASK.format(out=out, prompt=prompt)
    try:
        r = subprocess.run(["codex", "exec", "--skip-git-repo-check", "-C", SCRATCH, ask], stdin=subprocess.DEVNULL,
                           capture_output=True, text=True, timeout=600)
    except subprocess.TimeoutExpired:
        return row, False, "timeout"
    ok = os.path.exists(out)
    return row, ok, "ok" if ok else (r.stdout[-300:] + r.stderr[-300:]).replace("\n", " ")


def process(row: dict) -> tuple[bool, str]:
    args = [UV, "run", "--with", "pillow", "python", os.path.join(ROOT, "tools", "art", "process.py"), generation(row), target(row)]
    args += ["--tiles", row["tiles"]] if row["kind"] == "building" else ["--kind", "good"]
    os.makedirs(os.path.dirname(target(row)), exist_ok=True)
    r = subprocess.run(args, capture_output=True, text=True)
    return r.returncode == 0, (r.stdout + r.stderr).strip()


def log_rows(rows: list[dict]) -> None:
    today = datetime.date.today().isoformat()
    existing = open(LOG).read()
    lines = []
    for row in rows:
        rel_target = os.path.relpath(target(row), ROOT)
        if rel_target in existing:
            continue
        rel_gen = os.path.relpath(generation(row), ROOT)
        template = "building" if row["kind"] == "building" else "goods"
        lines.append(f"| {today} | `{rel_target}` | `{rel_gen}` | {template} | batch of the approved bible |")
    if not lines:
        return
    marker = "\nRejected generations"
    block = "\n".join(lines) + "\n"
    if marker in existing:
        i = existing.index(marker)
        existing = existing[:i].rstrip("\n") + "\n" + block + existing[i:]
    else:
        existing += block
    open(LOG, "w").write(existing)


def qc(row: dict) -> str | None:
    """Cheap checks on the processed sprite: something survived keying, and a building is not a tower."""
    code = (
        "from PIL import Image; import sys\n"
        "im = Image.open(sys.argv[1]).convert('RGBA'); a = im.getchannel('A')\n"
        "bbox = a.getbbox(); w, h = im.size\n"
        "opaque = sum(1 for v in a.getdata() if v > 128) / (w * h)\n"
        "print(w, h, round(opaque, 3))\n"
    )
    r = subprocess.run([UV, "run", "--with", "pillow", "python", "-c", code, target(row)], capture_output=True, text=True)
    if r.returncode != 0:
        return "unreadable"
    w, h, opaque = r.stdout.split()
    w, h, opaque = int(w), int(h), float(opaque)
    if opaque < 0.05:
        return f"almost nothing survived keying ({opaque:.0%})"
    if row["kind"] == "building" and h > w * 1.9:
        return f"too tall for its footprint ({w}x{h})"
    return None


def sheet(rows: list[dict], name: str, cell: int) -> None:
    files = [target(r) for r in rows if os.path.exists(target(r))]
    if not files:
        return
    out = os.path.join(ROOT, "docs", "review", name)
    code = (
        "from PIL import Image, ImageDraw; import sys, os, math\n"
        "cell = int(sys.argv[1]); out = sys.argv[2]; files = sys.argv[3:]\n"
        "cols = 10; rows = math.ceil(len(files) / cols)\n"
        "sheet = Image.new('RGBA', (cols * cell, rows * (cell + 14)), (239, 231, 210, 255)); d = ImageDraw.Draw(sheet)\n"
        "for i, f in enumerate(files):\n"
        "    im = Image.open(f).convert('RGBA'); im.thumbnail((cell - 8, cell - 8))\n"
        "    x = (i % cols) * cell + (cell - im.width) // 2; y = (i // cols) * (cell + 14) + (cell - im.height) // 2\n"
        "    sheet.alpha_composite(im, (x, y)); d.text(((i % cols) * cell + 4, (i // cols) * (cell + 14) + cell), os.path.basename(f)[:-4], fill=(30, 27, 24, 255))\n"
        "sheet.save(out); print(out, sheet.size)\n"
    )
    subprocess.run([UV, "run", "--with", "pillow", "python", "-c", code, str(cell), out, *files], check=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--sheet", action="store_true")
    ap.add_argument("--jobs", type=int, default=6)
    args = ap.parse_args()
    rows = subjects(os.path.join(ROOT, "tools", "art", "subjects.tsv"))
    if args.only:
        rows = [r for r in rows if r["id"] in args.only]
    os.makedirs(SCRATCH, exist_ok=True)
    if not args.sheet:
        todo = [r for r in rows if args.force or not os.path.exists(target(r))]
        if args.force:
            for r in todo:
                if os.path.exists(generation(r)):
                    os.remove(generation(r))
        print(f"{len(todo)} to cut, {args.jobs} at a time", flush=True)
        done, failed = [], []
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
            for row, ok, note in pool.map(generate, todo):
                if not ok:
                    failed.append((row["id"], note))
                    print(f"FAIL {row['kind']} {row['id']}: {note}", flush=True)
                    continue
                pok, pnote = process(row)
                if not pok:
                    failed.append((row["id"], pnote))
                    print(f"FAIL process {row['id']}: {pnote}", flush=True)
                    continue
                flag = qc(row)
                print(f"{'FLAG' if flag else 'ok  '} {row['kind']} {row['id']}" + (f": {flag}" if flag else ""), flush=True)
                done.append(row)
        # Log every sprite that exists (a run that crashed mid-way still gets its rows).
        log_rows([r for r in rows if os.path.exists(target(r))])
        print(f"cut {len(done)}, failed {len(failed)}", flush=True)
        if done:
            subprocess.run(["bash", os.path.join(ROOT, "tools", "art", "import.sh")], check=False)
    all_b = [r for r in subjects(os.path.join(ROOT, "tools", "art", "subjects.tsv")) if r["kind"] == "building"]
    all_g = [r for r in subjects(os.path.join(ROOT, "tools", "art", "subjects.tsv")) if r["kind"] == "good"]
    sheet(all_b, "plates-buildings.png", 160)
    sheet(all_g, "plates-goods.png", 96)


if __name__ == "__main__":
    main()
