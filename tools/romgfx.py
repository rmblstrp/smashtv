#!/usr/bin/env python3
"""Extract every sprite image from a Smash T.V. (Williams Y-unit) MAME ROM set.

Usage:  python3 tools/romgfx.py <smashtv.zip> <out_dir> [IMG/ROBOY.LOD]

No third-party dependencies. Writes one PNG per image plus index.html (a
browsable gallery) and images.json (the decoded header table).

Naming: with a LOADIMG list (.LOD), images get their original names. The
.LOD gives the order images were packed into graphics ROM; each .IMG file's
directory (u16 count, u16 palettes, u32 dir offset; 42-byte records: name[16],
s16 anim_x, s16 anim_y, u16 w, u16 h, ...) gives size and anchor per name.
ROM images, sorted by graphics address, are aligned to that sequence by
(w, h, anim_x, anim_y).

How the data is laid out (sources: MAME src/mame/williams/midyunit*.cpp and
this repo's SYS.INC / PALL.ASM):

* Program ROM: u105 = even bytes, u89 = odd bytes, 256 KB, mapped at TMS34010
  bit address 0xFFE00000. Every pointer in the ROM is a *bit* address.
* Image header (SYS.INC:114-121), 16 bytes little-endian:
      u16 width, u16 height, s16 anim_x, s16 anim_y, u32 SAG, u32 CMAP
  SAG  = bit address of the pixels in graphics ROM, base 0x02000000
         (the "***> 2000000" line in IMG/ROBOY.LOD).
  CMAP = bit address of the palette in program ROM.
* Palette (PALL.ASM GETFPAL): u16 count, then count xRGB-555 words.
* Graphics ROM, 6 bits per pixel: three 384 KB banks (u111-113, u95-97,
  u106-108). Each contributes 2 bits of every pixel; byte i/4 of each bank,
  bits 2*(i%4). Pixel index = (SAG - 0x02000000) / 8.
* The blitter reads rows (width + 3) & ~3 pixels apart. Pixel 0 = transparent.
"""
import html, json, re, struct, sys, zipfile, zlib
from pathlib import Path

ROM_BASE = 0xFFE00000
GFX_BASE = 0x02000000
BANKS = (("u111", "u112", "u113"), ("u95", "u96", "u97"), ("u106", "u107", "u108"))


def load_roms(zip_path):
    roms = {}
    with zipfile.ZipFile(zip_path) as z:
        for name in z.namelist():
            m = re.search(r"u(\d+)", Path(name).name.lower())
            if m:
                roms["u" + m.group(1)] = z.read(name)
    missing = [u for u in ("u105", "u89", *sum(BANKS, ())) if u not in roms]
    if missing:
        sys.exit(f"missing ROMs in {zip_path}: {missing}")
    return roms


def build_program(roms):
    even, odd = roms["u105"], roms["u89"]
    prog = bytearray(len(even) * 2)
    prog[0::2], prog[1::2] = even, odd
    return bytes(prog)


def build_gfx(roms):
    banks = [b"".join(roms[u] for u in bank) for bank in BANKS]
    n = len(banks[0]) * 4
    out = bytearray(n)
    for shift in range(4):
        sh = 2 * shift
        for b, data in enumerate(banks):
            vals = bytes(((x >> sh) & 3) << (2 * b) for x in data)
            view = out[shift::4]
            out[shift::4] = bytes(a | v for a, v in zip(view, vals))
    return bytes(out)


def rom_byte(addr):
    return (addr - ROM_BASE) >> 3 if addr >= ROM_BASE else None


def read_palette(prog, cmap):
    off = rom_byte(cmap)
    if off is None or cmap & 0xF or off + 2 > len(prog):
        return None
    count = struct.unpack_from("<H", prog, off)[0]
    if not 1 <= count <= 256 or off + 2 + 2 * count > len(prog):
        return None
    words = struct.unpack_from(f"<{count}H", prog, off + 2)
    def c5(v): return (v << 3) | (v >> 2)
    return [(c5((w >> 10) & 31), c5((w >> 5) & 31), c5(w & 31)) for w in words]


def find_headers(prog, gfx_pixels):
    found = {}
    for off in range(0, len(prog) - 16, 2):
        w, h, ax, ay, sag, cmap = struct.unpack_from("<HHhhII", prog, off)
        if not (1 <= w <= 512 and 1 <= h <= 256):
            continue
        if sag < GFX_BASE or (sag - GFX_BASE) % 8:
            continue
        start = (sag - GFX_BASE) >> 3
        stride = (w + 3) & ~3
        if start + stride * (h - 1) + w > gfx_pixels:
            continue
        pal = read_palette(prog, cmap)
        if pal is None:
            continue
        key = (sag, w, h, cmap)
        if key in found:
            found[key]["refs"].append(ROM_BASE + off * 8)
            continue
        found[key] = dict(header=ROM_BASE + off * 8, refs=[ROM_BASE + off * 8], width=w,
                          height=h, anim_x=ax, anim_y=ay, sag=sag, cmap=cmap,
                          colors=len(pal), _start=start, _stride=stride, _pal=pal)
    return sorted(found.values(), key=lambda r: (r["sag"], r["header"]))


def read_img_directory(path):
    data = path.read_bytes()
    count, _pals, off = struct.unpack_from("<HHI", data, 0)
    dims = {}
    for i in range(count):
        rec = data[off + 42 * i: off + 42 * (i + 1)]
        name = rec[:16].split(b"\0")[0].decode("latin-1").upper()
        ax, ay, w, h = struct.unpack_from("<hhHH", rec, 16)
        dims.setdefault(name, (w, h, ax, ay))
    return dims


def load_lod_sequence(lod_path):
    """Ordered [(name, img_file, (w, h, anim_x, anim_y) or None)] as packed into ROM."""
    lod_path = Path(lod_path)
    on_disk = {p.name.upper(): p for p in lod_path.parent.iterdir()}
    seq, cur, cur_file, cache = [], None, None, {}
    for line in lod_path.read_text(encoding="latin-1").splitlines():
        line = line.strip()
        if re.match(r"^[A-Za-z]:\\", line) and line.upper().endswith(".IMG"):
            cur_file = line.replace("\\", "/").rsplit("/", 1)[-1].upper()
            if cur_file not in cache and cur_file in on_disk:
                cache[cur_file] = read_img_directory(on_disk[cur_file])
            cur = cache.get(cur_file)
        elif line.startswith("--->") and cur_file:
            for n in line[4:].split(","):
                n = n.strip().split("+")[0].upper()
                if n:
                    seq.append((n, cur_file, (cur or {}).get(n)))
    return seq


def assign_names(recs, seq, window=80):
    j, in_order, skipped = 0, 0, 0
    for r in recs:
        dims = (r["width"], r["height"], r["anim_x"], r["anim_y"])
        for k in range(j, min(j + window, len(seq))):
            if seq[k][2] == dims:
                r["name"], r["source"] = seq[k][0], seq[k][1]
                in_order += k == j
                skipped += k - j
                j = k + 1
                break
    named = sum(1 for r in recs if "name" in r)
    return named, in_order, skipped


def png(path, w, h, rgba_rows):
    raw = b"".join(b"\x00" + row for row in rgba_rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def render(rec, gfx):
    pal, w = rec["_pal"], rec["width"]
    rows, bad = [], 0
    for y in range(rec["height"]):
        base = rec["_start"] + y * rec["_stride"]
        row = bytearray()
        for p in gfx[base:base + w]:
            if p == 0:
                row += b"\0\0\0\0"
            elif p < len(pal):
                row += bytes(pal[p]) + b"\xff"
            else:
                bad += 1
                row += b"\xff\x00\xff\xff"   # index past palette end: magenta
        rows.append(bytes(row))
    return rows, bad


GALLERY = """<!doctype html><meta charset=utf-8><title>ROM sprites</title>
<style>
:root{--bg:#f4f4f2;--fg:#1d1d1b;--card:#fff;--line:#d8d8d4;--muted:#6b6b66}
@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#e8e8e4;--card:#22221f;--line:#3a3a36;--muted:#9a9a93}}
body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:13px/1.4 ui-monospace,Menlo,monospace}
header{position:sticky;top:0;background:var(--bg);padding:8px 0;display:flex;gap:12px;align-items:center;flex-wrap:wrap}
input{font:inherit;padding:4px 8px;border:1px solid var(--line);background:var(--card);color:var(--fg)}
main{display:flex;flex-wrap:wrap;gap:8px}
figure{margin:0;padding:6px;background:var(--card);border:1px solid var(--line);display:flex;flex-direction:column;gap:4px}
figure[hidden]{display:none}
figure img{image-rendering:pixelated;background:repeating-conic-gradient(#8884 0 25%,#0000 0 50%) 0 0/8px 8px}
figcaption{color:var(--muted);font-size:11px;max-width:260px;word-break:break-all}
</style>
<header><b>%COUNT% images</b><label>zoom <input id=z type=range min=1 max=4 value=2></label>
<input id=q placeholder="filter: name, size, address"></header><main>%ITEMS%</main>
<script>
const imgs=[...document.querySelectorAll('figure')];
function zoom(){const k=+z.value;imgs.forEach(f=>{const i=f.querySelector('img');i.width=i.dataset.w*k;i.height=i.dataset.h*k;});}
z.oninput=zoom;zoom();
q.oninput=()=>{const t=q.value.toLowerCase();imgs.forEach(f=>f.hidden=t&&!f.dataset.k.includes(t));};
</script>"""


def main():
    if len(sys.argv) not in (3, 4):
        sys.exit(__doc__)
    roms = load_roms(sys.argv[1])
    out = Path(sys.argv[2])
    (out / "png").mkdir(parents=True, exist_ok=True)
    prog, gfx = build_program(roms), build_gfx(roms)
    recs = find_headers(prog, len(gfx))
    if len(sys.argv) == 4:
        seq = load_lod_sequence(sys.argv[3])
        named, in_order, skipped = assign_names(recs, seq)
        print(f"names: {named}/{len(recs)} matched; {in_order} were the next name in sequence, "
              f"{skipped} list entries skipped; {len(seq)} names in list")
    items, table = [], []
    for i, r in enumerate(recs):
        name = f"{i:04d}_{r.get('name', 'unnamed')}_{r['width']}x{r['height']}"
        rows, bad = render(r, gfx)
        png(out / "png" / f"{name}.png", r["width"], r["height"], rows)
        r["file"], r["pixels_past_palette"] = f"png/{name}.png", bad
        clean = {k: (f"0x{v:08X}" if k in ("header", "sag", "cmap") else v)
                 for k, v in r.items() if not k.startswith("_")}
        clean["refs"] = [f"0x{a:08X}" for a in r["refs"]]
        table.append(clean)
        title = f"<b>{html.escape(r['name'])}</b> {html.escape(r['source'])}<br>" if "name" in r else ""
        cap = (title + f"#{i} {r['width']}x{r['height']} anim({r['anim_x']},{r['anim_y']})<br>"
               f"hdr {r['header']:08X} sag {r['sag']:08X}<br>pal {r['cmap']:08X} ({r['colors']}c)"
               + (f" <b>{bad} px past pal</b>" if bad else ""))
        keys = f'{name} {r.get("source", "")} {r["header"]:08x} {r["sag"]:08x} {r["cmap"]:08x}'.lower()
        items.append(f'<figure data-k="{html.escape(keys)}">'
                     f'<img loading=lazy src="{clean["file"]}" data-w={r["width"]} data-h={r["height"]}>'
                     f"<figcaption>{cap}</figcaption></figure>")
    (out / "images.json").write_text(json.dumps(table, indent=1))
    (out / "index.html").write_text(GALLERY.replace("%COUNT%", str(len(recs))).replace("%ITEMS%", "\n".join(items)))
    bad_imgs = sum(1 for r in recs if r["pixels_past_palette"])
    print(f"{len(recs)} images -> {out}/index.html ({bad_imgs} with pixels past their palette)")


if __name__ == "__main__":
    main()
