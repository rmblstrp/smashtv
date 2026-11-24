# Arena camera demo (Godot 4.7, C#)

One Smash-TV-proportioned arena with placeholder 3D characters, a playable twin-stick player and a
stream of enemies, viewed through six switchable fixed cameras. Gameplay runs on the flat floor
plane; the camera is presentation only — which is the point being compared.

Open `project.godot` in Godot 4.7 (.NET build) and press Play, or:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path godot/camera-demo
```

## Controls

| Input | Action |
|---|---|
| WASD / left stick | move |
| Arrow keys / right stick | shoot (twin-stick) |
| 1–6 | camera preset |
| Q / E | pitch −/+ 5° |
| Z / X | FOV −/+ 5° (perspective presets) |
| F | show collision footprints (the circle gameplay actually uses) |
| R | clear enemies |
| H | hide help |

## Presets

| # | Camera | What to look at |
|---|---|---|
| 1 | Orthographic, straight down | exact positions; characters are heads and shoulders |
| 2 | Orthographic, 60° | the arcade-like three-quarter view; north wall face visible |
| 3 | Orthographic, 45° | taller silhouettes, more overlap near the back wall |
| 4 | Orthographic 60°, world stretched 1/sin(60°) in depth | floor tiles and the floor circle stay square/round, as in the arcade's art |
| 5 | Perspective 60°, 30° vertical FOV | long lens: near-orthographic with a slight depth cue |
| 6 | Perspective 50°, 50° vertical FOV | wide lens: near side looms, far side shrinks |

Every preset re-fits the camera so the whole arena, walls included, fills the screen — a fixed
single-screen view like the arcade. Shots travel at gun height while collision uses the floor
footprint (press F): the same split the original makes (only the legs collide, `ROBO.ASM:2434`).

Speeds and counts come from `docs/gameplay-reference.md`, converted at 1 m = 17 px and 54.71 Hz
(`Tuning.cs`).

## Unattended renders

`-- --autopilot --preset=N` makes the player fly a fixed figure-eight and shoot the nearest enemy
with a fixed random seed, so clips of different presets show the same fight:

```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path godot/camera-demo --write-movie /abs/path/p2.avi --fixed-fps 60 --quit-after 480 -- --autopilot --preset=2
```

macOS stops redrawing a covered window, so for renders add a gitignored `override.cfg` with
`[display]` `window/size/always_on_top=true`. Never combine `--write-movie` with `--headless`.

## Comparison

`docs/compare.png` (one frame) and `docs/compare.mp4` (8 s) show the same autopilot fight through presets 1–6, left to right, top to bottom.
