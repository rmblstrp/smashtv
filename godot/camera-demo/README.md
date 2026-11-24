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
| 1–9, 0 | camera preset (0 = preset 10) |
| Q / E | pitch −/+ 5° |
| Z / X | FOV −/+ 5° (perspective presets) |
| F | show collision footprints (the circle gameplay actually uses) |
| R | clear enemies |
| H | hide help |
| K | clear the room (preset 9 room loop) |

## Presets

| # | Camera | What to look at |
|---|---|---|
| 1 | Orthographic, straight down | exact positions; characters are heads and shoulders |
| 2 | Orthographic, 60° | the arcade-like three-quarter view; north wall face visible |
| 3 | Orthographic, 45° | taller silhouettes, more overlap near the back wall |
| 4 | Orthographic 60°, world stretched 1/sin(60°) in depth | floor tiles and the floor circle stay square/round, as in the arcade's art |
| 5 | Perspective 60°, 30° vertical FOV | long lens: near-orthographic with a slight depth cue |
| 6 | Perspective 50°, 50° vertical FOV | wide lens: near side looms, far side shrinks |
| 7 | Hybrid: preset 4 + leaned walls | back wall leaned 27°, side walls 27°, front wall a 0.5 m curb; characters upright under real light |
| 8 | Literal: straight down + leaned walls + tipped characters | plan-view floor, back wall leaned 63°, characters tipped back 30°, blob shadows |
| 9 | Painted walls + 3D floor | walls are a 2D painting on a camera-facing quad, floor stays 3D; a painted front-lip overlay draws over characters |
| 0 | Whole room painted + shadow catcher | walls and floor are one painting; an invisible plane lays the 3D shadows onto it |

Every preset re-fits the camera so the whole arena, walls included, fills the screen — a fixed
single-screen view like the arcade. Shots travel at gun height while collision uses the floor
footprint (press F): the same split the original makes (only the legs collide, `ROBO.ASM:2434`).

Speeds and counts come from `docs/gameplay-reference.md`, converted at 1 m = 17 px and 54.71 Hz
(`Tuning.cs`).

## Unattended renders

Launch options after `--`: `--preset=N`, `--autopilot`, `--feet`, `--pitch-offset=DEG`,
`--save-backdrops=DIR`, `--quota=N` (kills to clear a room in preset 9). `--autopilot --preset=N` makes the player fly a fixed figure-eight and shoot the nearest enemy
with a fixed random seed, so clips of different presets show the same fight:

```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path godot/camera-demo --write-movie /abs/path/p2.avi --fixed-fps 60 --quit-after 480 -- --autopilot --preset=2
```

macOS stops redrawing a covered window, so for renders add a gitignored `override.cfg` with
`[display]` `window/size/always_on_top=true`. Never combine `--write-movie` with `--headless`.

## Matching the arcade's walls (presets 7 and 8)

The arcade art draws each surface under its own rule: floor in plan, back wall face-on, side-wall
faces as slanted strips, front wall as a lip, characters always in three-quarter view. No single
camera does that, so the walls are leaned instead: each wall pivots on its floor boundary line, so
collision never moves. Angles were fitted to an arcade frame: back-wall face ≈ 22% of the floor's
on-screen depth, side-wall faces ≈ 6.7% of its width, front lip ≈ 0.55 m.

- **7 (recommended):** the 60° orthographic camera with depth stretch keeps characters upright under
  real lighting; only the walls are faked.
- **8:** a plan-view camera needs the characters tipped back 30° to show their fronts, which breaks
  real shadows, so it uses blob shadows.

`docs/leaned-walls.png` shows the same frame through 7 (left) and 8 (right). Room geometry lives
in `Main.BuildArena`, the angles in the `Walls` records in `Main.Presets`.

## Painted backdrops (presets 9 and 0)

With a fixed camera the room never needs to be seen from another angle, so it can be a picture.
`BackdropPainter` draws it in code, in "screen metres", into a transparent SubViewport shown on a
quad facing the camera. With the 60° orthographic camera and depth stretch, the 3D floor maps to an
exact rectangle on screen, so characters stand on the painted floor with no fitting.

- **Layers:** the back painting sits far behind everything; a front painting (the near lip only)
  sits in front of everything, so characters walking past the near wall go behind it. Back-wall
  doorways need no special case: characters are always in front of the back wall.
- **Painted walls, 3D floor (9) vs. whole room painted (0):** they look the same
  (`docs/backdrop-presets.png`: 7, 9, 0). The floor matters for what lands on it: in 9 the floor is
  real, so shadows, decals (blood, scorch) and dynamic light just work. In 0 shadows need the
  catcher plane (`docs/shadows-3d-vs-painted.png`); decals and lights from explosions would each
  need their own trick.
- **Shadow catcher:** not `render_mode shadow_to_opacity` — that clamps alpha by ambient × albedo
  (`scene_forward_clustered.glsl`), so black shadows come out invisible. The shader's `light()`
  writes `ALPHA = (1 - ATTENUATION) * strength` instead.
- **The limit:** the depth stretch keeps the floor aligned at any pitch, but the painted walls are
  drawn for 60°. At another pitch the characters' angle changes and the walls' doesn't
  (`docs/painting-at-other-pitch.png`, 60° vs 45°). Fine for a fixed camera; zoom and pan also work
  (orthographic), tilt and rotate don't.
- **Templates:** `docs/backdrops/backdrop-{walls,room,front}.png` are the paintings at 64 px/m, with
  the floor transparent in `walls`. An artist can paint over them at the same size and drop them in.
  Regenerate with `-- --save-backdrops=/abs/dir`.

## Room to room (preset 9)

Preset 9 is a loop of four rooms on a grid, using every exit direction once:
A east → B, B south → C, C west → D, D north → A. Clear a room (a kill quota, or K), then walk into
the open exit (green chevrons). The transition follows the arcade:

- **Scroll:** constant 3 px/frame, no easing (`SCRLXSPD`, `WAVES.ASM:175`) → 9.65 m/s. Rooms sit
  472 × 333 px apart (`CRCT1YX`) → 27.8 × 19.6 m, so 2.9 s sideways, 2.0 s vertically.
- **Walk-out script:** the player keeps walking in the exit direction with no control for 223
  frames sideways, 163 down, 170 up (`ROBOATT.ASM:2222-2296`). The walk outlasts the scroll by about
  a second and leaves the player near the new room's centre.
- **Freeze:** enemies start 120 frames after control returns (`ROBO.ASM:1295`).

Each room's floor and painting are tinted so the change reads. A corridor floor fills only the gap
between paintings; inside a painting the doorway is painted, and 3D floor there would cover the art.
Paintings pan cleanly because the camera is orthographic and they face it, so there is no parallax.

**Checked against arcade footage** (a 60 fps gameplay video; scroll timed from frame-to-frame change
in a wall strip, speed from tracking the wall's shift per frame):

| | Source predicts | Footage | Demo |
|---|---|---|---|
| Vertical scroll | 111 frames, 2.03 s | 111 moving frames, 2.02 s, every frame the same step | 2.03 s |
| Horizontal scroll | 157⅓ frames, 2.88 s | 158 moving frames, 2.90 s; last step ≈ 1 px (`STOPX` clamp) | 2.88 s |
| Easing | none | none — constant 3 px/frame from first frame to last | none |
| Player at scroll end | — | about ¼ into the new room, still walking | 4.9–5.4 m off centre |
| Player when control returns | — | near the floor centre, ~1–1.2 s after the scroll stops | within 0.8 m of centre |

Not yet in the demo, from the same footage:
- **The gap between rooms is painted** (machinery panels sideways, a grey wall band with doors
  vertically), not black.
- **Start-of-room enemies are already standing in the new room during the scroll**, frozen (a grunt
  formation, fat men), instead of everything arriving through doors afterwards.
- **A room-title banner** ("COLLECT POWERUPS!", "MEET MR. SHRAPNEL") colour-cycles over the new room
  while enemies stay frozen.
- The back wall with its built-in HUD scrolls with the room, as the painted screens here do.

`docs/transition-sideways.png` and `docs/transition-down.png` are contact sheets (0.4 s apart) of
A → B and B → C; `docs/rooms.mp4` is 45 s of the full loop on autopilot (`--quota=8`). The log
prints `ROOMS` lines with timings, e.g. entering B at (0.0, −0.3) m from its centre.

## Comparison

`docs/compare.png` (one frame) and `docs/compare.mp4` (8 s) show the same autopilot fight through presets 1–6, left to right, top to bottom.
