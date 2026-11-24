#!/usr/bin/env python3
"""Print every room's spawn table as markdown, decoded from ROBO.ASM.

Usage:  python3 tools/wavetables.py [ROBO.ASM] [difficulty 1-10, default 3]

Record format (ROBO.ASM:744-754):  TYPE, #, LEVEL, RATE, ONSCRN, CNTR ; `.WORD 0` ends a room.
Timing (overlord loop, ROBO.ASM:1309-1340): one pass every 3 frames; an entry's
CNTR counts down once per pass and the spawner runs when it hits 0, then CNTR
reloads from RATE. So the first call is 3*(CNTR-1) frames after release and the
next ones every 3*RATE frames. Release is 120 frames after the room title clears.
Caps follow SETMAX (ROBO.ASM:556-702) at the given difficulty; spawners allow
one more than the cap (`count <= cap`), tanks exactly the cap.
"""
import re, sys
from pathlib import Path

FPS = 54.71
NAMES = {"HULK": "Grunt", "RBTA": "Robot", "ORB": "Orb", "TANK": "Tank", "FAT": "Fat man",
         "WLMN": "Wall man", "MINE": "Mine", "MUTD": "**Mutoid (boss)**", "FACE": "**Face (boss)**",
         "COBRA": "**Cobras (boss)**", "GALS1": "Chain (single)", "GALS1A": "Chain train",
         "GALS2": "Saucer", "GALS3": "Swarm", "SNK": "Snakes"}
PLACED = {"MINE", "WLMN", "MUTD", "FACE", "COBRA"}
PRIMARY = ("MUTD", "FACE", "COBRA", "HULK", "RBTA", "ORB")   # types whose count ends a room
MASKED = {"RBTA", "SNK"}                                    # count read with ANDI 03FFH
DIFF_GRUNTMOD = [-4, -3, -2, -1, 0, 0, 1, 3, 4, 6, 6]       # GRUNTTBL, ROBO.ASM:341


def ev(expr):
    expr = re.sub(r"\b([0-9][0-9A-F]*)H\b", r"0x\1", expr.strip())   # 07FFFH
    return eval(re.sub(r">([0-9A-F]+)", r"0x\1", expr), {})              # >7FFF


def parse(asm):
    text = Path(asm).read_text(encoding="latin-1")
    tables = {}
    for name in ("HMAX", "TMAX", "FMAX", "OMAX", "RMAX", "RMAX2", "GMAX", "GMAX2", "HMAX2"):
        m = re.search(rf"^{name}\s+\.BYTE\s+(.*?)(?=^\S)", text, re.M | re.S)
        vals = []
        for n, line in enumerate(m.group(1).splitlines()):
            if n and ".BYTE" not in line:
                continue
            line = line.split(";")[0].replace(".BYTE", "")
            vals += [ev(v) for v in line.split(",") if v.strip()]
        tables[name] = vals
    circuits = []
    for label in ("WAVE_INFO", "W2_INFO", "W3_INFO"):
        start = re.search(rf"^{label}:", text, re.M).end()
        rooms, room, lineno = [], [], text[:start].count("\n") + 1
        for i, line in enumerate(text[start:].splitlines(), lineno):
            code = line.split(";")[0].strip()
            if not code.startswith(".WORD"):
                if room == [] and rooms and code and not code.startswith((".", "*")) and ":" in line:
                    break
                continue
            vals = [v.strip() for v in code[5:].split(",")]
            if vals == ["0"]:
                rooms.append(room)
                room = []
                if label == "WAVE_INFO" and len(rooms) == 10 or len(rooms) == (18 if label == "W2_INFO" else 21):
                    break
                continue
            t, n, lvl, rate, ons, cntr = vals
            room.append(dict(type=t, n=ev(n), level=ev(lvl), rate=ev(rate), onscrn=ev(ons), cntr=ev(cntr), line=i))
        circuits.append(rooms)
    return tables, circuits


def caps(tables, circuit, wave, players, gmod):
    one = players == 1
    if circuit == 0:
        return {"HULK": tables["HMAX"][wave] + gmod, "TANK": tables["TMAX"][wave], "FAT": tables["FMAX"][wave]}
    if circuit == 1:
        return {"RBTA": tables["RMAX"][wave] - 2 * one + gmod, "ORB": tables["OMAX"][wave],
                "GAL": tables["GMAX"][wave] - 2 * one, "FAT": 5, "TANK": 1}
    return {"RBTA": tables["RMAX2"][wave] - 5 * one + gmod, "ORB": tables["OMAX"][wave] - 2 * one,
            "GAL": tables["GMAX2"][wave] - 2 * one, "HULK": tables["HMAX2"][wave] - 5 * one + gmod,
            "FAT": 5, "TANK": 3 if wave == 1 else 4 if wave == 11 else 1}


def main():
    asm = sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "ROBO.ASM"
    diff = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    tables, circuits = parse(asm)
    gmod = DIFF_GRUNTMOD[diff]
    for c, rooms in enumerate(circuits):
        print(f"\n#### Circuit {c + 1}\n")
        print("| Room | Enemy | Total | Level | On screen at start | First spawn call | Then every | Cap 1P / 2P | Line |")
        print("|---|---|---|---|---|---|---|---|---|")
        for w, room in enumerate(rooms, 1):
            if c > 0 and w == 10:
                continue  # placeholder record, no exit leads to it
            prim = next(t for t in PRIMARY if any(e["type"] == t for e in room))
            c1, c2 = caps(tables, c, w, 1, gmod), caps(tables, c, w, 2, gmod)
            for k, e in enumerate(room):
                t = e["type"]
                name = NAMES.get(t, t) + (" ★" if t == prim else "")
                total = e["n"] if t not in MASKED or e["n"] < 1024 else f"{e['n']} → **{e['n'] & 0x3FF}**"
                key = "GAL" if t in ("GALS1", "GALS1A", "GALS2") else t
                if t in PLACED:
                    timing = ("placed", "—")
                    total = "—" if t in ("MUTD", "FACE", "COBRA") else e["n"]
                else:
                    f0 = 3 * (e["cntr"] - 1)
                    timing = (f"{f0 / FPS:.1f} s", f"{3 * e['rate']} f ({3 * e['rate'] / FPS:.2f} s)")
                cap = "—" if t in PLACED else ("none" if key not in c1 else f"{c1[key]} / {c2[key]}")
                lvl = "—" if t in PLACED else e["level"]
                room_cell = f"**{w}**" if k == 0 else ""
                print(f"| {room_cell} | {name} | {total} | {lvl} | {e['onscrn'] if t not in PLACED else e['onscrn']} "
                      f"| {timing[0]} | {timing[1]} | {cap} | {e['line']} |")


if __name__ == "__main__":
    main()
