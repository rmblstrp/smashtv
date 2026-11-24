#!/usr/bin/env python3
"""Record every named sound in SOUNDS.ASM through MAME's emulated sound board.

Usage:  python3 tools/romsnd.py <dir containing smashtv.zip> <out_dir> [SOUNDS.ASM] [--only LABEL,LABEL]

Needs `mame` and `ffmpeg` on PATH. Writes one WAV per sound plus index.html
(a listening page) and sounds.json.

How it works:
* SOUNDS.ASM entries look like   LEGOSND .WORD >FCFE-10H,>30,>8058,0 ;LET'S GO
  = priority word (1111 I E NN PPPPPPPP, NN = channel), then a time word
  (hold, x 16 ms) and sound words (8SSS / 4SSS), ending in 0. Music sits on
  channel 2 in this build, so music is detected from the label/comment. Only the low byte of
  a sound word reaches the board (SNDSND strips it, SOUNDS.ASM:293-294).
* The game sends a code by writing 0xFD00|code then 0xFF00|code to the
  sound port 0x01E00000 (SOUNDS.ASM:289-308; MUSICBIT == SOUNDBIT here, so
  the channel does not change what is sent). MAME's cvsd_sound_w turns the
  bit-9 change into the strobe the sound CPU interrupts on.
* A MAME Lua script taps that port: the game's power-on board reset passes,
  later game writes become the idle value 0xFFFF. The script sets full volume
  (VOLUMEF), then plays each entry on a timeline, strobe-low and strobe-high in
  consecutive frames, and logs start/stop times. One long WAV is recorded and
  split with ffmpeg.
* MAME 0.289 lists four PLD dumps (a_5346_3044_*.u8/.u52/.u53/.u65) that many
  ROM zips lack. The driver never reads them, but MAME refuses to start when
  files are missing (a wrong checksum is only a warning). So the run adds a
  second ROM folder with zero-filled placeholders of the right size, created in
  a temp dir; the ROM zip itself is never touched.
"""
import html, json, re, shutil, subprocess, sys, tempfile
from pathlib import Path

BOOT_SECONDS = 8.0       # let the sound board finish its power-on init
GAP_SECONDS = 0.75       # silence between sounds
MUSIC_SECONDS = 40.0     # music loops forever; record this much
SFX_MIN, SFX_MAX = 3.0, 14.0

ENTRY = re.compile(r"^([A-Z0-9_]+):?\s+\.WORD\s+([^;]+?)\s*(?:;(.*))?$", re.I)


def num(tok):
    tok = tok.strip()
    if tok.startswith(">"):
        return int(tok[1:], 16)
    if tok[-1:] in "hH":
        return int(tok[:-1], 16)
    return int(tok, 10)


def evaluate(expr):
    """TI-assembler style: evaluated left to right (intent of '>F0FF|>800-10H')."""
    parts = re.split(r"([|&+\-])", expr.replace(" ", ""))
    val = num(parts[0])
    for op, tok in zip(parts[1::2], parts[2::2]):
        v = num(tok)
        val = {"|": val | v, "&": val & v, "+": val + v, "-": val - v}[op]
    return val & 0xFFFF


def parse_sounds(asm_path):
    sounds = []
    for lineno, line in enumerate(Path(asm_path).read_text(encoding="latin-1").splitlines(), 1):
        m = ENTRY.match(line.strip())
        if not m or line.lstrip().startswith(("*", ";")):
            continue
        try:
            words = [evaluate(w) for w in m.group(2).split(",")]
        except ValueError:
            continue
        if not words or words[0] < 0xF000:
            continue
        # TIME sets the hold for the codes after it: an 8SSS code is sent, then
        # the sequence waits TIME x 16 ms; a 4SSS code is sent with no wait.
        prio, steps, t, hold = words[0], [], 0, 0
        for w in words[1:]:
            if w == 0:
                break
            if w >= 0xF000:            # priority change inside a sequence
                continue
            if w & 0x8000:
                steps.append({"code": w & 0xFF, "at": t})
                t += hold
            elif w & 0x4000:
                steps.append({"code": w & 0xFF, "at": t})
            else:
                hold = w * 16
        if not steps:
            continue
        sounds.append(dict(label=m.group(1), comment=(m.group(3) or "").strip(), line=lineno,
                           channel=(prio >> 8) & 3, priority=prio & 0xFF,
                           words=[f"{w:04X}" for w in words], steps=steps, hold_ms=t))
    return sounds


LUA = r"""
local plan = %PLAN%
local log = io.open(%LOG%, "w")
local space = manager.machine.devices[":maincpu"].spaces["program"]
local PORT = 0x01E00000
local BOOT = %BOOT%
local ours = false
-- Let the game's power-on board reset through; after that, idle every game write
-- (the attract loop re-resets the board every ~19 s, which would cut sounds off).
tap = space:install_write_tap(PORT, PORT + 0x1F, "mute_game_sound", function(offset, data, mask)
  if ours or manager.machine.time:as_double() < BOOT - 2 then return data end
  return 0xFFFF
end)
local function put(v)
  ours = true
  space:write_u16(PORT, v)
  ours = false
end
-- One command = strobe low (0xFD00|code) in one frame, strobe high (0xFF00|code) in
-- the next. Both in the same instant would collapse: MAME's williams_cvsd write()
-- re-arms a single zero-delay timer, so only the last value would reach the PIA.
local events = {}
local VOLUME_F = {0x20, 0x60}   -- VOLUMEF, full volume (SOUNDS.ASM FADE_UP_TAB)
for k, c in ipairs(VOLUME_F) do table.insert(events, {t = BOOT - 1 + k * 0.05, code = c}) end
for _, s in ipairs(plan) do
  for n, st in ipairs(s.steps) do
    table.insert(events, {t = s.start + st.at / 1000.0, code = st.code, start = (n == 1) and s.label or nil})
  end
  table.insert(events, {t = s.stop, code = 0, stop = s.label})
end
table.sort(events, function(a, b) return a.t < b.t end)
local i, low = 1, nil
sub = emu.add_machine_frame_notifier(function()
  local now = manager.machine.time:as_double()
  if low then put(0xFF00 | low) low = nil return end
  if i <= #events and events[i].t <= now then
    local e = events[i]
    put(0xFD00 | e.code)
    low = e.code
    if e.start then log:write(string.format("START %s %.4f\n", e.start, now)) end
    if e.stop then log:write(string.format("STOP %s %.4f\n", e.stop, now)) end
    log:flush()
    i = i + 1
  elseif i > #events then
    log:close()
    manager.machine:exit()
  end
end)
"""


def lua_table(plan):
    rows = []
    for s in plan:
        steps = ",".join(f"{{code={st['code']},at={st['at']}}}" for st in s["steps"])
        rows.append(f'{{label="{s["label"]}",start={s["start"]:.3f},stop={s["stop"]:.3f},steps={{{steps}}}}}')
    return "{\n" + ",\n".join(rows) + "\n}"


PLD_STUBS = ("a_5346_3044_1.u8", "a_5346_3044_5.u52", "a_5346_3044_6.u53", "a_5346_3044_7.u65")


def main():
    args, only = sys.argv[1:], None
    if "--only" in args:
        i = args.index("--only")
        only = set(args[i + 1].upper().split(","))
        del args[i:i + 2]
    if len(args) not in (2, 3):
        sys.exit(__doc__)
    rompath, out = Path(args[0]).resolve(), Path(args[1]).resolve()
    asm = Path(args[2] if len(args) == 3 else Path(__file__).resolve().parent.parent / "SOUNDS.ASM")
    for tool in ("mame", "ffmpeg"):
        if not shutil.which(tool):
            sys.exit(f"{tool} not found on PATH")
    sounds = parse_sounds(asm)
    # The same code sequence under several labels plays the same sound: record it once.
    seen, plan, t = {}, [], BOOT_SECONDS
    if only:
        sounds = [s for s in sounds if s["label"].upper() in only]
    for s in sounds:
        if re.match(r"VOLUME|.*OFF$", s["label"]):   # board control commands, not sounds
            s["same_as"] = "(control command, not recorded)"
            continue
        key = tuple((st["code"], st["at"]) for st in s["steps"])
        if key in seen:
            s["same_as"] = seen[key]
            continue
        seen[key] = s["label"]
        music = bool(re.search(r"TUNE|MUSIC|MSC|MUSC", s["label"] + " " + s["comment"], re.I))
        s["music"] = music
        length = MUSIC_SECONDS if music else min(max(s["hold_ms"] / 1000 + 2.0, SFX_MIN), SFX_MAX)
        s["start"], s["stop"] = t, t + length
        plan.append(s)
        t = s["stop"] + GAP_SECONDS
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        log, wav = tmp / "marks.txt", tmp / "all.wav"
        script = (LUA.replace("%PLAN%", lua_table(plan)).replace("%LOG%", json.dumps(str(log)))
                  .replace("%BOOT%", f"{BOOT_SECONDS:.2f}"))
        (tmp / "rec.lua").write_text(script)
        (tmp / "stub" / "smashtv").mkdir(parents=True)
        for name in PLD_STUBS:
            (tmp / "stub" / "smashtv" / name).write_bytes(bytes(0x32F))
        cmd = ["mame", "smashtv", "-rompath", f"{rompath};{tmp / 'stub'}",
               "-autoboot_script", str(tmp / "rec.lua"),
               "-wavwrite", str(wav), "-video", "none", "-sound", "none", "-nothrottle",
               "-skip_gameinfo", "-nvram_directory", str(tmp), "-cfg_directory", str(tmp),
               "-seconds_to_run", str(int(t + 5))]
        print(f"recording {len(plan)} sounds ({t:.0f} s of emulated time)...")
        res = subprocess.run(cmd, capture_output=True, text=True)
        if not wav.exists() or not log.exists():
            sys.exit(f"MAME run failed ({res.returncode}):\n{res.stdout[-2000:]}\n{res.stderr[-2000:]}")
        marks = {}
        for line in log.read_text().splitlines():
            kind, label, t = line.split()
            marks[(kind, label)] = float(t)
        for i, s in enumerate(plan):
            name = f"{i:03d}_{s['label']}.wav"
            start = marks.get(("START", s["label"]), s["start"])
            dur = marks.get(("STOP", s["label"]), s["stop"]) - start
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{start:.3f}", "-t", f"{dur:.3f}",
                            "-i", str(wav), "-af",
                            "highpass=f=15,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse",  # board idles at a DC offset
                            str(out / name)], check=True)
            s["file"] = name
            probe = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                    str(out / name)], capture_output=True, text=True)
            s["seconds"] = round(float(probe.stdout.strip() or 0), 2)
            if s["seconds"] < 0.1:   # e.g. 0x5B FREEMAN, 0x9E HEXP3 on the sl2 sound ROMs
                s["silent"] = True
    for s in sounds:
        s.pop("start", None), s.pop("stop", None)
    (out / "sounds.json").write_text(json.dumps(sounds, indent=1))
    def group(s):
        code = s["steps"][0]["code"]
        if "(control" in s.get("same_as", ""):
            return "Board control commands (not sounds)"
        if s.get("music"):
            return "Music"
        if 0x30 <= code < 0x60:
            return "Speech and voices (CVSD)"
        return "Sound effects"
    order = ["Music", "Speech and voices (CVSD)", "Sound effects", "Board control commands (not sounds)"]
    sections = []
    for g in order:
        rows = []
        for s in (x for x in sounds if group(x) == g):
            if s.get("silent"):
                audio = "<i>no output for this code on this sound ROM</i>"
            elif "file" in s:
                audio = f'<audio controls preload=none src="{s["file"]}"></audio><span>{s["seconds"]} s</span>'
            elif g.startswith("Board"):
                audio = ""
            else:
                audio = f'same sound as <b>{html.escape(s.get("same_as", ""))}</b>'
            codes = " ".join(f'{st["code"]:02X}' for st in s["steps"])
            rows.append(f'<tr><td><b>{s["label"]}</b><div class=play>{audio}</div></td>'
                        f'<td>{html.escape(s["comment"])}</td><td>{codes}</td><td>{s["hold_ms"]}</td>'
                        f'<td class=src>SOUNDS.ASM:{s["line"]}</td></tr>')
        sections.append(f"<h2>{g} <small>({len(rows)})</small></h2><table><tr><th>label</th><th>comment</th>"
                        f"<th>code</th><th>hold ms</th><th>source</th></tr>{''.join(rows)}</table>")
    (out / "index.html").write_text(PAGE.replace("%COUNT%", str(len(plan))).replace("%ROWS%", "\n".join(sections)))
    print(f"{len(plan)} recordings, {len(sounds)} table entries -> {out}/index.html")


PAGE = """<!doctype html><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>ROM sounds</title>
<style>
:root{--bg:#f4f4f2;--fg:#1d1d1b;--line:#d8d8d4;--muted:#6b6b66}
@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#e8e8e4;--line:#3a3a36;--muted:#9a9a93}}
body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:13px/1.4 ui-monospace,Menlo,monospace}
h2{margin:24px 0 8px;font-size:15px}h2 small{color:var(--muted);font-weight:normal}
table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}
th{color:var(--muted);font-weight:normal}.src{color:var(--muted);white-space:nowrap}
.play{display:flex;align-items:center;gap:8px;margin-top:4px;color:var(--muted)}audio{height:28px;width:220px;max-width:60vw}
</style>
<p><b>%COUNT% recordings</b> from MAME's emulated sound board, each triggered by the code its SOUNDS.ASM entry sends.
<br>Hold = the game's no-interrupt window for that sound (time words &times; 16 ms), not the sound's length.
Music windows are cut at 40 s; most tracks loop.</p>
%ROWS%"""


if __name__ == "__main__":
    main()
