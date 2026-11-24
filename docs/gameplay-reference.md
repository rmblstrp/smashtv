# Gameplay reference: tuning numbers from the 1990 source

Every number here was read from the assembly in this repo. Citations are `FILE.ASM:line`.
**READ** = read at that line and its use traced; **INFERRED** = deduced, with the basis stated.
Extracted by four parallel reading passes, then spot-checked by hand (see "Verification" at the end).

## 1. Units and timing

| Quantity | Value | Source |
|---|---|---|
| Frame rate | **54.71 Hz** (8 MHz pixel clock / (506 × 289)) — 18.28 ms per frame | MAME `williams/midyunit.cpp` `set_raw(STDRES_PIXEL_CLOCK*2, 506, …, 289, …)`; matches `MAIN.ASM:503-510` (HTOTAL 0xFC, VTOTAL 0x120) |
| Playfield | 400 × 256 px (X 5..395, Y 1..252) | `DISP.EQU` SCRNST/SCRNEND |
| Position / velocity format | 16.16 fixed point; `0x10000` = 1.0 px per frame | `DISP.EQU` OXVEL/OXVAL |
| When velocity is applied | Once per frame, in `DISPLAY` from the half-screen interrupt | `MAIN.ASM:239-257`, `NDSP1.ASM:360+` |
| `SLEEP n` | n dispatcher ticks ≈ n frames (the dispatcher runs once per frame unless overloaded) | `MPROC.ASM` PRCDSP |
| px/frame → px/s | × 54.71 (so 0.5 px/f = 27.4 px/s; 1.0 = 54.7; 2.0 = 109.4) | — |

The code comments say "16 MSEC" for a tick (`MPROC.ASM` TIMER, `SOUNDS.ASM` time words). That is a 60 Hz
assumption; on this board a tick is 18.28 ms, so anything specified in "×16 ms" runs about 14 % longer in seconds.

**Shared helpers that shape the numbers below**

| Helper | Behaviour | Source |
|---|---|---|
| `RANGRAND(a,b)` | Uniform over **a+1 … b** (low bound exclusive) | `UTIL.ASM:910-949` |
| `RANDPER(n)` | True with probability n/1000 | `UTIL.ASM:957-965` |
| `SRAND(n)` | −(n−1) … +n | `UTIL.ASM:922-929` |
| Speed by level `DO_DIFF2` | H = base + 0.125·LEVEL px/f; V = H − 0.0625; diagonals = 0.8 × H, 0.8 × V | `HULKS.ASM:733-753` |

## 2. Player and weapons

### Movement

| Item | Value | px/s | Source |
|---|---|---|---|
| Run, cardinal | `016000H` = 1.375 px/f | 75.2 | `ROBO.ASM:137-138` |
| Run, diagonal | `0FA00H` per axis = 0.977 → 1.381 total (normalized) | 75.6 | `ROBO.ASM:139-140` |
| Shoes, cardinal | `01F000H` = 1.9375 px/f | 106.0 | `ROBO.ASM:142-143` |
| Shoes, diagonal | `018000H` per axis = 1.5 → **2.121 total** (not normalized; the comment says ×0.707 but the value isn't) | 116.0 | `ROBO.ASM:144-145` |
| Acceleration | none — velocity is written directly and zeroed on stick release | — | `ROBO.ASM:2326-2356, 2122-2127` |
| Input sampling | every 3 frames (the leg animation step), so up to ~4 px of overshoot | — | `ROBO.ASM:2165-2218` (INFERRED from loop) |
| Arena bounds (legs) | X 37..354, Y 44..239; tighter with helper/rings (52..340, 57..239) and blades (72..322, 79..223) | — | `WAVES.ASM:3180-3185` |

**Speed relationships worth noticing:** a fresh grunt walks at 36 % of player speed, but grunts age up to 1.75 px/f
and robots to 2.0 — both **faster than the player's 1.375**. Shoes (1.94) restore the edge. Lingering in a room is
punished twice: enemies speed up, and the chaser arrives after a cleared room.

### Lives, death, invulnerability

| Item | Value | Source |
|---|---|---|
| Lives | 3 (operator 1–7); capped at 9 | `AUDIT.ASM:2324`, `ROBO.ASM:1460-1464` |
| Extra lives by score | 500,000, then every 1,500,000 (doubled scoring in 2-player makes these come twice as fast) | `SCORE.ASM:428-475` |
| Extra-life icons per circuit | MAXFREE + 1 = 3 at factory, shared by both players | `ROBO.ASM:339-340`, `ICONS.ASM:160-167` |
| Respawn | at the side doors (P1 x=8, P2 x=384, y=133) with a scripted walk-in; rings for 190 frames | `ROBO.ASM:1842-1868`, `ROBOATT.ASM:1124-1145` |
| Death → control | ~135 frames (spin), ~99 (electrocuted), ~165 (blown to chunks) | `ROBO.ASM:1655-1739` (INFERRED: sum of sleeps) |
| Grace counter quirk | invulnerability counts down once per torso-loop pass; while fire is held a pass is a whole shot cycle, so **holding fire stretches spawn invulnerability up to 8×** | `ROBO.ASM:1634-1638, 2049-2055` (INFERRED) |
| Lost on death | gun, meter, helper, shoes, blades (60 frames later) | `ROBO.ASM:1833-1860, 1342-1356` |

**Hitbox:** only the legs collide (the torso is `M_NOCOLL`). The kill box is the legs sprite shrunk by
+4 px left, +3 px top and −10 px bottom (`COLL2.ASM:247-302`) — about 5 × 6 px standing. Mines only trigger on
the feet band; orbs use a slightly taller box.

### Firing

Twin-stick: an independent 8-way fire direction. Fire rate is the length of the shooting animation — while the
stick is held the animation restarts as soon as it ends (`ROBO.ASM:2001-2055`).

| Weapon | Shots per pull | Cycle | Shots/s | Projectile speed | Ammo (trigger pulls) | Source |
|---|---|---|---|---|---|---|
| Default (`SUBLZR`) | 1 | 8 f | 6.8 | 7.125 px/f per axis: 390 px/s cardinal, **551 px/s diagonal** (not normalized) | ∞ | `SHOTS.ASM:67-68, 921-922` |
| Spray (`SUBSPRY`) | 3 (0°, ±26.6°; ±18.4° on diagonals) | 8 f | 6.8 | centre 7.125, sides 7.97 px/f | 36 | `SHOTS.ASM:930-1006, 1538-1565` |
| Pod bombs (`SUBSPEW`) | 1 | 3 f | 18.2 | random per axis, e.g. right: X 1.5..3.0, Y ±1 px/f; fuse 15–48 frames | 60 | `SHOTS.ASM:1007-1037, 1395-1438` |
| Grenade (`SUBGRND`) | 1 | 8 f | 6.8 | scripted 44-frame lob, ~113 px horizontal / 91 up / 97 down; **lethal only in the last 14 frames** | 24 | `SHOTS.ASM:1196-1376, 1917-2470` |
| Non-stop (`SUBNOSTP`) | 1 | 9 f | 6.1 | 4.0 px/f horizontal, 3.5 vertical (219 / 191 px/s) | 42 | `SHOTS.ASM:69-70, 1143-1194` |

- **Ammo:** `TIMERS .WORD 100,6,10,4,7` per meter box × 6 boxes (`SCORE.ASM:285, 197-244`). The default gun's
  100 is never decremented. A new gun **replaces** the current one and refills the meter; helper, blades, rings
  and shoes stack with any gun (`SHOTS.ASM:745-833`). The gun carries over between waves.
- **No bullet cap:** `MAXBLT .EQU 45` is defined but never checked; only the 240-process / 420-object pools limit
  it (`SHOTS.ASM:77`).
- Bullets leave the arena through open doors and are deleted 10 frames later; otherwise they die on the wall box
  X 32..371, Y 33..252, with a harmless 9-frame "ding" (`SHOTS.ASM:1676-1759, 1868-1899`).
- Pod explosion: 9 frames, up to 15 × 15 px, removed on its first enemy contact; instantly kills fat men.
  Grenade "explosion" is cosmetic — the grenade itself is the hitbox.
- While **any** player's meter is filling (36 frames), shots cost nothing for **both** players (`SCORE.ASM:146-201`).

### Power-ups

| Power-up | Behaviour | Duration | Source |
|---|---|---|---|
| Helper | 1 satellite orbiting clockwise, ~25 px radius, 80 frames per orbit; duplicates every shot (spray and non-stop shots cost double ammo) | until it touches an enemy or you die; persists across waves | `POW2.ASM:56-147`, `SHOTS.ASM:985-1078` |
| Blades | 5 blades, ~42 px radius, 53 frames per orbit; each takes 5 hits then flies off tangentially still killing | until wave exit; one player at a time | `POW1.ASM:53-275` |
| Rings (shield) | invulnerable, kills/pushes what it touches, blinks for the last 100 frames | `RINGTBL[difficulty]`: 400 frames (7.3 s) at factory, 110–600 across settings | `ROBO.ASM:348-455` |
| Shoes | speed boost above | 300 (500 in circuit 3) moving leg-steps ≈ 818 / 1,364 frames | `SHOTS.ASM:574-593` (frames INFERRED) |
| Smart bomb | kills every normal enemy and projectile on screen (fat men give no score); not wall men, mines, bosses or chasers | instant + 30-tick shake | `SHOTS.ASM:594-744` |

### Pickups on the floor

- One icon at a time. It lies 286 frames (5.2 s; 448 in boss waves), then a 160-frame gap
  (`ICONS.ASM:205-256`).
- Type is drawn from a 20-slot table per wave, so weights are counts out of 20 (`ICONS.ASM:150-159`, tables at
  `ICONS.ASM:529-1716`). Example, circuit 1: wave 1 = blades 6, spray 3, rings 3, shoes 2, smart bomb 6;
  wave 10 (boss) = spray 4, pod 3, **grenade 7**, rings 2, non-stop 3, shoes 1. Grenades appear almost only in
  boss waves.
- Position: X 51–340, Y 71–220, never within 25 px vertically / 30 px horizontally of a player
  (`ICONS.ASM:117-137, 257-284`).
- Score pickups: key 15,000; prizes 5,010 (240 frames after wave start, none in boss waves); start-of-wave
  cash 1,250 each (`SHOTS.ASM:81-573`).

## 3. Regular enemies

All enemies kill the player on contact unless noted. Hit counts are at the factory difficulty setting (3).

### Summary

| Enemy (code name) | Hits to kill | Speed | Attack | Points |
|---|---|---|---|---|
| Grunt (`HULK`) | 1 | 0.5 + 0.125·L px/f, L ≤ 10 → 27–96 px/s; 8-way homing | contact | 500 |
| Small robot (`RBTA`) | 1 | 0.5 + 0.125·L, L ≤ 12 → 27–109 px/s; re-homes ~every frame + elliptical wobble | contact | 1,050 |
| Fat man / shrapnel bomber (`FAT`) | 8 (non-stop bullet or pod: 1; blades: immune) | 0.5 → 27 px/s, patrols walls clockwise | contact; self-destructs into a 5-piece fan at ~2.2 px/f (120 px/s) | 2,750 (0 if it self-destructs) |
| Tank turret (`TORSO`) | 17 damage: 9 laser/spray, 4 pod, 1 grenade/non-stop | — | cannonballs 1.6–2.26 px/f (88–124 px/s) **plus the tank's own velocity** | 7,500 |
| Tank body (`TANK`) | invulnerable until the turret dies, then 1 hit | 1.125 px/f (62 px/s), cardinal directions only | contact | 7,500 |
| Laser orb (`ORB`) | random: 30 % die on hit 1, 21 % on hit 2, 49 % on hit 3 (mean 2.19) | spring toward the player, ≈ 0.015 × distance px/f | contact; 4-way laser growing 3 px/f | 2,500 |
| Wall man (`WLMN`, wall turret) | 35 damage: 12 laser/spray, 7 pod, 2 grenade/non-stop | stationary | shots 2.08–2.94 px/f (114–161 px/s) | 5,500 |
| Mine | indestructible | static | contact, single use | 0 |
| Chaser (room-cleared punisher) | immune to bullets (shoved 5–6 px); rings kill | 1.5 px/f L1-norm (82 px/s), re-aims every 21 frames | contact | 0 |
| Gal chain unit | 4 (non-stop: 1) | cap 2.0 px/f (3.0 in circuit 3) | contact | 2,000 |
| Gal saucer | 2 (non-stop: 1) | cap 4.0 px/f | contact | 2,000 |
| Swarm head / ball | 1 / 1 | ≤ 1.75 / ≤ 3.0 px/f per axis | contact | 20,000 / 2,050 |
| Snake | 1 | 0.5 + 0.125·(L+1..L+4) | contact | 1,000 |

**Every point value is doubled while two players are in the game** (`SCORE.ASM:445-456`: `BCDADD` runs twice when STATUS = 3).
Points are packed BCD (`>500` = 500) — `SCORE.ASM:492-529`.

### Damage model

There are no hit points in the usual sense. Each tough enemy keeps a damage counter and adds a per-weapon
value from a `PUNISH` table, indexed by the bullet subtype (laser, spray, pod, grenade, non-stop):

| Table | Laser | Spray | Pod | Grenade | Non-stop | Used by | Source |
|---|---|---|---|---|---|---|---|
| `PUNISH` | 2 | 2 | 5 | 20 | 20 | tank turret (vs `TNKHITS`) | `TANK.ASM:2170` |
| `PUNISHT` | 3 | 3 | 5 | 20 | 20 | tank body after turret dies (vs `HITTNK 3`); wall man (vs `HITWLM 35`) | `TANK.ASM:979`, `WLMAN.ASM:433` |

Fat men just count hits (`FATMAN.ASM:277-297`, `@FATHITS`). Everything else dies in one hit.

Which bullets survive a hit (`COLL2.ASM:1084-1137`): non-stop bullets pass through grunts, robots, orbs,
chasers, snakes and gals (the hit still counts). Every bullet stops on fat men, wall men, the tank turret, and the
tank body once it has no driver. While the turret lives, bullets pass through the body without effect.

### Difficulty tables (operator setting 1–10, factory 3)

`ROBO.ASM:262-347`, `SETDIFF`. Index = setting (entry 0 is unused by a 1–10 setting).

| Table | 0 | 1 | 2 | **3** | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Effect |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `HITTNKS` → TNKHITS | 3 | 6 | 12 | **17** | 20 | 25 | 30 | 35 | 45 | 50 | 50 | tank turret damage to kill |
| `HITFAT` → FATHITS | 1 | 3 | 6 | **8** | 9 | 10 | 12 | 15 | 18 | 20 | 20 | fat-man hits to kill |
| `GRUNTTBL` → GRUNTMOD | −4 | −3 | −2 | **−1** | 0 | 0 | 1 | 3 | 4 | 6 | 6 | ± max grunts and robots on screen |
| `TANKTBL` → TANKDIFF | −3 | −2 | −1 | **−1** | 0 | 0 | 1 | 2 | 3 | 4 | 4 | ± shots per tank burst/sweep |
| `TANKTBL2` → TANKDIFF2 | 12 | 11 | 10 | **9** | 8 | 8 | 7 | 6 | 5 | 4 | 4 | frames between sweep shots (−1) |
| `FREETBL` → MAXFREE | 4 | 4 | 3 | **2** | 2 | 2 | 1 | 1 | 0 | 0 | 0 | extra lives available per circuit |

Built-in mercy: if a player dies in circuit 1 waves 1–2, the grunt speed level in that wave is reset to 0
(`ROBO.ASM:1391-1402`).

### Grunt (`HULKS.ASM`)

| Item | Value | Source |
|---|---|---|
| Base speed | `HSPEED 08000H` = 0.5 px/f H, 0.4375 V; diagonals 0.40 / 0.35 | `HULKS.ASM:111, 733-753` |
| Level cap | `MAXLEVEL 10` → 1.75 H / 1.6875 V px/f | `HULKS.ASM:109` |
| Speed-up while alive | each grunt picks one interval from `AGTBL` (90–500 passes); +1 level each time it expires | `HULKS.ASM:668-676, 498-518` |
| Pass length | 5 / 4 / 3 frames at level < 2 / < 5 / ≥ 5 | `HULKS.ASM:974-1013` |
| Re-aim interval | `RANGRAND(1, LEVTBL[L])` passes, `LEVTBL 11,10,9,8,7,7,6,6,5,5,4…` → 10–55 frames at L0, 6–12 at L10 | `HULKS.ASM:757-770` |
| Turn rate | 45° per 3 frames; new velocity applies immediately | `HULKS.ASM:568-571, 772-793` |
| Gang spawn | 14 % of spawn calls (`RANDPER 140`) send 8–16 at once through one door, levels 0–3 | `HULKS.ASM:124-165, 280-288` |
| Visual tell | level ≥ 4 at spawn uses the purple palette | `HULKS.ASM:312-326` |
| Tank crush | grunts run over by a tank die with no score | `COLL2.ASM:203-220` |

### Small robot (`ROBOTS.ASM`)

Same speed formula as grunts, cap level 12 (2.0 px/f). Gang chance 6 % (always a gang for the "bigger robot"
art), gang size 7–15. **Bug worth knowing:** `GET_STAND` never clears the turn flag, so after the first turn the
robot re-aims on every 1–3-frame pass instead of on its re-aim timer — it homes much harder than the grunt code
suggests (`ROBOTS.ASM:699-704, 1038-1055`). A 72-frame elliptical wobble (~16 × 11 px) is layered on top
(`ROBOTS.ASM:801-866`).

### Fat man (`FATMAN.ASM`)

Walks the arena walls clockwise at 0.5 px/f, turning at 45° per 5 frames; each leg lasts 31–100 passes
(155–500 frames). Each non-lethal hit shoves it 6 px along the bullet direction with a white flash. If a leg runs
out without meeting a wall it turns to face the player, waits 40 frames, shakes for 20, and bursts into 5
shrapnel pieces in a ±63° fan at ~2.2 px/f. Shrapnel can't be shot, dies on the first wall, and kills on contact.
Points only if you kill it first. (`FATMAN.ASM:198-262, 309-362, 488-520, 551-602, 667-725`)

### Tank (`TANK.ASM`)

| Item | Value | Source |
|---|---|---|
| Drive | 1.125 px/f, cardinal directions; legs of 26–50 passes × 5 frames; reverses at 0.25 px/f for 15 passes after a wall or another tank | `TANK.ASM:108-109, 383-452, 541-546` |
| Turret modes | timer 36–80 frames, then 70 % chance to switch; ~83 % of switches are "aim" | `TANK.ASM:1434-1483` |
| Aim | turns 22.5° per 4 frames, fires one ball when aligned | `TANK.ASM:1307-1364` |
| Burst | `RANGRAND(4,7)+TANKDIFF−1` shots (3–5 at factory), 6 frames apart | `TANK.ASM:1241-1253, 1365-1384` |
| Sweep | `RANGRAND(6,20)+TANKDIFF−1` shots (5–18), one per TANKDIFF2+1 = 10 frames, turret oscillating ±22.5° | `TANK.ASM:1227-1239, 1397-1429` |
| Cannonball | 1.6 px/f cardinal, 2.26 diagonal, ~1.77 for the 22.5° directions, **plus tank velocity**; 0 bounces (`TOTBNCS 1` is checked after incrementing); can't be shot | `TANK.ASM:103, 1534-1560, 1805-1868` |
| Spawn | right, top or bottom door only | `TANK.ASM:655-737` |

### Laser orb (`LZRORB.ASM`, by Eugene Jarvis)

A damped spring: every 2 frames `v = (v + a) × 15/16`, with `a = (target ± jitter − pos) × 64` in 16.16 —
terminal speed ≈ 15·d/1024 px/f (≈ 1.46 px/f at 100 px). 0.9 % chance per tick to fire (mean ≈ 222 frames
between shots): it stops, rotates up to 15 frames to the player's major axis, and grows a beam at 3 px/f to the
wall. Orbs repel each other within 16 px; the laser kills other orbs. Bullets knock it back by ¼ of their velocity.
(`LZRORB.ASM:208-344, 540-629, 820-947`)

### Wall man (`WLMAN.ASM`)

Two fixed turrets on the top wall (X 117 and 285). Idles 150–250 frames (78 %) or 2 frames (22 %), then 50/50
fires 2 unaimed shots or aims (22.5° per 3 frames) and fires 2–10 shots 7 frames apart. Shots 2.08 px/f cardinal,
2.94 diagonal. Harmless to touch; immune to rings and blades. (`WLMAN.ASM:45-305`)

### Chaser (`CHASER.ASM`)

The anti-camping enemy: spawns 750 frames (13.7 s) after a room is cleared in circuits 1–2, 210 frames (3.8 s)
in circuit 3, one per player, from a door chosen per circuit and wave (`CID1..3`). Bullets only shove it; rings
kill it. Speed 1.5 px/f (L1), re-aims every 21 frames. (`ENDWAVE.ASM:536-558`, `CHASER.ASM:58-334`)

### Gal family and swarm (`GAL.ASM`)

Chains (1 unit, or a 9-unit train — 4 in circuit 3) steer around the player with a radial pull and a 1/16
perpendicular term, followers chase the unit ahead; saucers start at ±2 px/f and cap at 4.0. The swarm is a head
plus 20 balls on springs; killing the head makes the balls drift 60 frames then explode with no score.
(`GAL.ASM:107-706, 864-951`) Ambiguity: `GALACC` loads the 9-digit literal `>7FFFFFFFF` (`GAL.ASM:625`); radial
pull is 0.0625 or 0.03125 px/f per tick depending on how the assembler truncated it.

### Snake (`SNAKE.ASM`)

Pours out of statue mouths in gangs of 19–23, each delayed 2–50 frames, speed level L+1..L+4. Starts down-left,
down or down-right, goes straight down after 2–8 passes (18 frames each) or on touching a wall; leaves at the
bottom. Not homing. (`SNAKE.ASM:153-479`)

### Spawn caps and the spawn clock

Wave records are `TYPE, COUNT, LEVEL, RATE, ONSCRN, CNTR` (`ROBO.ASM:744-754`). The spawn overlord runs every 3
frames; each type's counter counts down and calls that enemy's spawner at 0, so **RATE is in units of 3 frames**
(`ROBO.ASM:1309-1340`). Enemies freeze for 120 frames at wave start (`ROBO.ASM:1295-1298`). Max-alive caps are per
wave (`HMAX`, `FMAX`, `TMAX`, …, `ROBO.ASM:556-730`), adjusted by GRUNTMOD, and by −5 grunts/robots in circuit 3
and −2 gals/orbs for one player. The `>` comparison lets one extra spawn past the cap (tanks use `>=`).

## 4. Bosses

Four boss fights: Mutoid Man (circuit 1 wave 10), Flat Face → Skull (circuit 2 wave 7, `RBOSS.ASM`, by Jarvis),
two Cobras at once (circuit 3 wave 7), and a harder Mutoid variant as the final boss (circuit 3 wave 21,
`MCON=1`). Placement: `ROBO.ASM:817-823, 861-865, 968-971, 1050-1056`.

Weapon shorthand: **N** normal, **S** spray, **P** pod (in flight / burst), **G** grenade, **NS** non-stop.

### Summary

| | Mutoid (C1) | Mutoid final (C3) | Face → Skull | Cobra ×2 |
|---|---|---|---|---|
| HP model | one shared damage pool, 6 thresholds | same, bigger steps | damage per part (cap 20 each); total > 510 with both eyes > 16, then skull total > 210 | one pool shared by both snakes; can only die on a **head** hit |
| Kill damage, 1P / 2P | 1,600 / 1,850 | 1,775 / 2,025 | > 510 (max 680), then > 210 (max 280) | 2,650 / 2,900 at factory difficulty |
| Normal gun | 1.5 % chance of 1 damage, **can never cross a threshold** | same | 2 | 1 |
| Best weapon | G: 15 | G: 15 | G: 6, NS: 4 | G to the head: 50 |
| Kill points | 5 × 5,000 per stage + 75,000; 2 turrets × 7,500; 350 per hit | same + 480 cash items | 100,000 + 100,000; 250–1,000 per hit (×3 off the shell) | 100–500 per hit only |
| Lethal attacks | fists (only while down), eye laser, belly snake, turret cannonballs, body | same, but fists never kill (bug) | fireballs, 10–19-ball volley, contact | flame, spitballs, the 3 base segments |
| Scales with | player count (+250 / +300 per stage); difficulty only via turrets | same | nothing found | player count (−250 for 1P) and difficulty (`WNDTBL`) |

### Mutoid Man (`MUTOID.ASM`)

**Damage:** one pool `DMG` for all body parts; first threshold `DMGSK` = 150 (`MUTOID.ASM:245`); each stage raises
it by 40 (75 in the final version) plus 250 for one player / 300 for two (`MUTOID.ASM:623-655`). Overkill carries over.

| Weapon | Body | Head | Note | Source |
|---|---|---|---|---|
| N | +1 at 1.5 % and only below the threshold | same | bullet deflected randomly | `MUTOID.ASM:404-416` |
| S, NS, P burst | +2 | 0 | 350 points per hit | `MUTOID.ASM:378-397` |
| P in flight | 0 | 0 | passes through | `COLL2.ASM:1167-1171` |
| G | **+15** | **+15** | 350 points per hit | `MUTOID.ASM:425-444` |

| Stage (cumulative damage 1P / 2P) | Effect |
|---|---|
| 1 (150 / 150) | left arm destroyed (removed ~340 frames later, still dangerous until then) |
| 2 (440 / 490) | right arm |
| 3 (730 / 830) | torso art changes (hitbox unchanged) |
| 4 (1,020 / 1,170) | head gone: no more eye lasers |
| 5 (1,310 / 1,510) | torso gone; head sits on the tank; **lasers become permanent** (116-shot magazine, re-armed) |
| 6 (1,600 / 1,850) | dead; 75,000 points to the last player to hit it |

About 800 spray/non-stop hits or ~107 grenades for one player (INFERRED arithmetic).

**Behaviour:** 790-frame intro (14.4 s), rolls in at 0.875 px/f. Main loop: 8-frame pause, then an action — with
80 % probability a lunge (±1.625 px/f) if a player is lined up within 40 px horizontally, otherwise a weighted
pick out of 42: move down 5, up 6, fast pound + sideways skip (2.0 px/f; 4.5 in the final version) 7 + 7,
**eye laser 8**, slow pound 2 + 2, flex 3, raise fist 2 (`MUTOID.ASM:2029-2113`). After 1,440 frames (26 s) it also
jerks toward a player at 1.5 px/f every 300 frames.

**Eye laser:** 6–25 shots per magazine, all at one player. Each shot is 9 beam segments, 7.5 px/f straight down
with 135 px reach; segments are harmless in flight, and on arrival each has a 75 % chance of becoming a lethal
8-frame fizzle. ~45–51 frames per shot (`MUTOID.ASM:2218-3083`).

**Belly snake:** before stage 4 it needs **both shoulder turrets destroyed**, the boss stopped, a 40 % roll every
30 frames and a player below the belly. 10 segments at 1.5 px/f, head re-aims every 10 frames, 3 hits per
segment, killing one kills everything behind it, self-destructs after ~402 frames (`MUTOID.ASM:3502-3692`).

**Shoulder turrets:** 51 damage each on the `PUNISH` scale (`TOIDHIT`, `TANK.ASM:105`) — 26 laser hits or
3 grenades — 7,500 points each; same turret AI and cannonballs as the tank, limited to firing arcs on their side.

### Flat Face → Skull (`RBOSS.ASM`)

34 face parts (glass, eyes, mouth, cheeks, nose, 24 shell pieces) each absorb up to 20 damage. Per hit:
N/S 2, NS 4, G 6, P burst 1, P in flight doesn't collide; **21 % of hits do nothing**; damage halves in the
skull phase (`RBOSS.ASM:1210-1381`).

- **Face → skull** when both eyes > 16 **and** total > 510: 100,000 points, then ~213 invulnerable frames
  (`RBOSS.ASM:1258-1280`; the comment still says "75K").
- **Skull dies** at total > 210 (14 parts, max 280): another 100,000.
- **Movement:** spring toward the player (`Δ/1024` per tick, ×4 thrust below 2.0 px/f), damping 1/16 (1/8 above
  4.0), confined to X 88–320, Y 27–136. Bullets knock it back.
- **Fire rate rises with damage:** per 2-frame tick, chance `FIRETAB[total/32]` ‰ =
  15, 20, 20, 25, 25, 30, 35, 40, 40, 40, 40, 40, 50, 50, 60, 60 → a mean gap of 133 frames fresh,
  33 frames near death (`RBOSS.ASM:319-338`).
- 32 % of shots are a **volley of 10–19 shootable cannonballs** fired in one frame, each aimed ±80 px around the
  player; the rest are a homing fireball (L1 cap 6 px/f, bounces, ~143 frames, not shootable)
  (`RBOSS.ASM:679-926`).
- Skull: two eye photons at 5.2 % per tick, 30-frame cooldown (`RBOSS.ASM:436-637`).
- Mercy: every player starts the fight with 190 frames of rings; grenade and pod pickups also grant rings
  (`RBOSS.ASM:165-181`, `SHOTS.ASM:768-818`).

### Cobras (`COBRA.ASM`)

Two snakes, each a head plus 24 body segments; the tail is pinned, and each segment sits at the midpoint of its
neighbours, recomputed twice per frame (`COBRA.ASM:574-664`) — a cheap rope.

- **Damage** to the shared pool: body N 1, S 2, P 3, G 5, NS 5; on the head the damage equals the bullet subtype,
  except a grenade, which does **50** (`COBRA.ASM:694-697, 839-850`).
- The kill check only runs on a head hit: `COBRAKIL` from `WNDTBL` = 900, 1,400, 2,400, **2,900**, 3,400, 3,800,
  3,900, 4,400, 4,900… by difficulty, minus 250 for one player (`ROBO.ASM:350-352`, `COBRA.ASM:854-866`).
- **Flame:** chance per tick 25 % if a player is directly below (±24 px, ~130 px down), else 3 %; 27 harmless
  wind-up frames, then 9 lethal frames.
- **Spitball:** 1.5 % per snake per tick, ×4 if a player gets behind the snakes.
- Only the 3 segments nearest the base kill on contact; the head and upper body don't (`COLL2.ASM:631-649`).

## 5. Levels, rooms and spawning

### Structure

Three circuits of rooms on a grid. Each room has up to three exits, and you pick your path. A full game is
**7 + 9 + 12 = 28 rooms** (`WAVES.ASM:702-721`, exits in `ENDWAVE.ASM:1362-1636`).

**Circuit 1.** Grid rows top to bottom, columns left to right; the arrows are the exits.

| | col 1 | col 2 | col 3 | col 4 | col 5 | col 6 |
|---|---|---|---|---|---|---|
| row 0 | | 3 | 4 | 5 (cash) | | |
| row 1 | **1** start | 2 | 8 | 9 | **10 boss** | end |
| row 2 | | 6 | 7 | | | |

Exits: 1→2; 2→3 or 6; 3→4; 4→5 or 8; 5→9; 6→7; 7→8; 8→9; 9→10. Paths: 1-2-3-4-5-9-10, 1-2-3-4-8-9-10, 1-2-6-7-8-9-10.

**Circuits 2 and 3** (same layout for rooms 1–18; room 10 exists only as a placeholder record):

| | col 1 | col 2 | col 3 | col 4 | col 5 |
|---|---|---|---|---|---|
| row 0 | | | 11 | 12 (cash) | |
| row 1 | 8 | 9 | 4 | 5 | |
| row 2 | **1** start | 2 | 3 | 6 | **7 boss** |
| row 3 | 13 | 14 (cash) | 15 | 16 | |
| row 4 | | | 17 | 18 | |

Exits: 1→2, 8 or 13; 2→3, 9 or 14; 3→4 or 15; 4→11; 11→12; 12→5; 5→6; 6→7; 8→9; 9→4; 13→14; 14→15; 15→17; 17→18;
18→16; 16→6. Every path is 9 rooms. Circuit 2 ends after room 7. Circuit 3 continues 7→19→20→21, and room 21 is the
final Mutoid fight. Rooms 19–20 are named "key rooms", but the route is unconditional and nothing counts keys
(`ENDWAVE.ASM:1563-1566`).

### How a room runs

1. The room scrolls in. Start-of-room units (`ONSCRN`) and mines/wall men are placed, caps are set (`SETMAX`).
2. After the room title clears, everything stays frozen for **120 frames** (2.2 s), then the spawn clock starts
   (`ROBO.ASM:1280-1309`).
3. **Spawn clock:** every 3 frames each entry's counter ticks; at zero its spawner runs and the counter reloads
   from RATE. A spawner that fails (cap reached, door closing, a player dying) still reloads
   (`ROBO.ASM:1309-1340`).
4. **The room ends when the primary enemy (★) is exhausted** (its total reaches 0, all spawned) — or the boss
   dies. Fat men, tanks, gals, snakes, mines and wall men never end a room. 120 frames later the spawner is killed,
   so **the totals of non-primary entries are upper bounds**. Then the room waits until no enemy-class object is
   left, smart-bombs leftovers, and opens the exits (`ENDWAVE.ASM:598-781`).
5. To leave, step into an exit box; in a 2-player game **both players must stand in the same box**.

Clear-time pressure, besides grunts aging: the chaser arrives 750 frames (13.7 s) after a clear in circuits 1–2,
210 frames (3.8 s) in circuit 3; and if **neither score changes for 1,200 frames (21.9 s)** the game smart-bombs
the room (`ENDWAVE.ASM:536-597`). There is no room time limit.

### Doors and gangs

Doors: 1 left, 2 right, 3 top, 4 bottom (the bottom has no gate). Single grunts queue two per door; the third
arrival opens the gate for 115 frames. Gangs (14 % of grunt spawn calls, 6 % of robot calls from room 3 on) burst
8–16 through one door, holding it open 290 frames (`HULKS.ASM:115-221`, `ROBODOOR.ASM:66-245`). Tanks use doors
2–4 only. Snakes pour from the statue mouths in circuit 3.

### Every room

Generated from `ROBO.ASM:742-1056` by `tools/wavetables.py` at factory difficulty (re-run it for another setting).
★ = primary enemy, the one whose total ends the room. "Then every" is the spawn-call interval; a grunt or robot call
can be a whole gang. Robot and snake totals are read with `ANDI 03FFH`, so values ≥ 1,024 wrap (shown as raw → used).
Caps: the spawner allows **one more than the listed cap** to be alive (tanks: exactly the cap); swarms and snakes
are uncapped.

#### Circuit 1

| Room | Enemy | Total | Level | On screen at start | First spawn call | Then every | Cap 1P / 2P | Line |
|---|---|---|---|---|---|---|---|---|
| **1** | Grunt ★ | 100 | 0 | 4 | 1.6 s | 18 f (0.33 s) | 11 / 11 | 756 |
|  | Tank | 5 | 0 | 0 | 72.3 s | 270 f (4.94 s) | 1 / 1 | 757 |
| **2** | Grunt ★ | 195 | 1 | 0 | 0.0 s | 3 f (0.05 s) | 10 / 10 | 760 |
|  | Fat man | 6 | 0 | 0 | 2.7 s | 480 f (8.77 s) | 3 / 3 | 761 |
|  | Mine | 3 | — | 3 | placed | — | — | 762 |
|  | Tank | 5 | 0 | 0 | 77.8 s | 420 f (7.68 s) | 1 / 1 | 763 |
| **3** | Grunt ★ | 335 | 2 | 16 | 0.0 s | 24 f (0.44 s) | 24 / 24 | 766 |
|  | Fat man | 15 | 0 | 2 | 1.6 s | 270 f (4.94 s) | 2 / 2 | 767 |
|  | Mine | 5 | — | 5 | placed | — | — | 768 |
|  | Wall man | 1 | — | 1 | placed | — | — | 769 |
|  | Tank | 5 | 0 | 0 | 94.3 s | 270 f (4.94 s) | 1 / 1 | 770 |
| **4** | Grunt ★ | 25 | 3 | 0 | 22.4 s | 3 f (0.05 s) | 31 / 31 | 773 |
|  | Fat man | 30 | 0 | 7 | 2.1 s | 15 f (0.27 s) | 7 / 7 | 774 |
|  | Wall man | 2 | — | 2 | placed | — | — | 775 |
|  | Mine | 5 | — | 5 | placed | — | — | 776 |
|  | Tank | 5 | 0 | 0 | 94.3 s | 270 f (4.94 s) | 1 / 1 | 777 |
| **5** | Grunt ★ | 455 | 6 | 0 | 6.5 s | 12 f (0.22 s) | 19 / 19 | 781 |
|  | Fat man | 25 | 0 | 0 | 21.9 s | 240 f (4.39 s) | 10 / 10 | 782 |
|  | Mine | 5 | — | 5 | placed | — | — | 783 |
|  | Tank | 2 | 0 | 0 | 98.6 s | 420 f (7.68 s) | 1 / 1 | 784 |
| **6** | Grunt ★ | 35 | 3 | 16 | 27.4 s | 3 f (0.05 s) | 19 / 19 | 787 |
|  | Swarm | 1 | 0 | 0 | 0.0 s | 570 f (10.42 s) | none | 788 |
|  | Fat man | 15 | 0 | 3 | 3.8 s | 180 f (3.29 s) | 6 / 6 | 789 |
|  | Mine | 2 | — | 2 | placed | — | — | 790 |
|  | Tank | 5 | 0 | 0 | 66.8 s | 720 f (13.16 s) | 1 / 1 | 791 |
| **7** | Grunt ★ | 395 | 3 | 16 | 0.0 s | 3 f (0.05 s) | 24 / 24 | 794 |
|  | Swarm | 1 | 0 | 0 | 71.2 s | 750 f (13.71 s) | none | 795 |
|  | Mine | 3 | — | 3 | placed | — | — | 796 |
|  | Fat man | 13 | 0 | 2 | 0.0 s | 345 f (6.31 s) | 6 / 6 | 797 |
|  | Tank | 5 | 0 | 0 | 88.8 s | 420 f (7.68 s) | 1 / 1 | 798 |
| **8** | Grunt ★ | 295 | 4 | 0 | 8.7 s | 3 f (0.05 s) | 28 / 28 | 801 |
|  | Swarm | 1 | 0 | 0 | 0.0 s | 1500 f (27.42 s) | none | 803 |
|  | Fat man | 10 | 0 | 3 | 0.0 s | 30 f (0.55 s) | 11 / 11 | 804 |
|  | Wall man | 2 | — | 2 | placed | — | — | 805 |
|  | Mine | 5 | — | 5 | placed | — | — | 806 |
|  | Tank | 13 | 0 | 0 | 38.3 s | 120 f (2.19 s) | 1 / 1 | 807 |
| **9** | Grunt ★ | 325 | 4 | 0 | 0.0 s | 3 f (0.05 s) | 23 / 23 | 810 |
|  | Wall man | 2 | — | 2 | placed | — | — | 812 |
|  | Tank | 17 | 0 | 0 | 0.0 s | 480 f (8.77 s) | 1 / 1 | 813 |
|  | Fat man | 8 | 0 | 3 | 16.4 s | 3 f (0.05 s) | 8 / 8 | 814 |
|  | Mine | 4 | — | 4 | placed | — | — | 815 |
| **10** | **Mutoid (boss)** ★ | — | — | 1 | placed | — | — | 819 |
|  | Mine | 2 | — | 2 | placed | — | — | 820 |
|  | Swarm | 1 | 0 | 0 | 1.0 s | 135 f (2.47 s) | none | 821 |
|  | Fat man | 35 | 0 | 0 | 54.8 s | 420 f (7.68 s) | 5 / 5 | 822 |

#### Circuit 2

| Room | Enemy | Total | Level | On screen at start | First spawn call | Then every | Cap 1P / 2P | Line |
|---|---|---|---|---|---|---|---|---|
| **1** | Orb ★ | 98 | 0 | 1 | 0.0 s | 30 f (0.55 s) | 20 / 20 | 828 |
| **2** | Robot ★ | 380 | 3 | 0 | 0.0 s | 3 f (0.05 s) | 17 / 19 | 832 |
|  | Fat man | 1 | 0 | 1 | 27.4 s | 270 f (4.94 s) | 5 / 5 | 833 |
| **3** | Robot ★ | 270 | 5 | 3 | 46.6 s | 15 f (0.27 s) | 20 / 22 | 836 |
|  | Swarm | 2 | 0 | 0 | 0.0 s | 240 f (4.39 s) | none | 837 |
|  | Mine | 3 | — | 3 | placed | — | — | 838 |
|  | Chain train | 15 | 0 | 0 | 8.2 s | 30 f (0.55 s) | 18 / 20 | 839 |
| **4** | Swarm | 1 | 0 | 0 | 0.0 s | 210 f (3.84 s) | none | 842 |
|  | Robot ★ | 240 | 4 | 0 | 43.8 s | 3 f (0.05 s) | 15 / 17 | 843 |
|  | Mine | 4 | — | 4 | placed | — | — | 844 |
|  | Chain (single) | 60 | 0 | 0 | 0.0 s | 45 f (0.82 s) | 18 / 20 | 845 |
| **5** | Robot ★ | 330 | 7 | 0 | 39.4 s | 3 f (0.05 s) | 15 / 17 | 848 |
|  | Chain (single) | 40 | 0 | 0 | 9.9 s | 24 f (0.44 s) | 10 / 12 | 849 |
|  | Fat man | 10 | 0 | 1 | 27.4 s | 570 f (10.42 s) | 5 / 5 | 850 |
|  | Mine | 4 | — | 4 | placed | — | — | 851 |
|  | Swarm | 4 | 0 | 0 | 0.0 s | 225 f (4.11 s) | none | 852 |
|  | Tank | 6 | 0 | 1 | 65.7 s | 180 f (3.29 s) | 1 / 1 | 853 |
| **6** | Orb ★ | 90 | 0 | 1 | 21.9 s | 24 f (0.44 s) | 13 / 13 | 856 |
|  | Saucer | 35 | 0 | 0 | 0.0 s | 45 f (0.82 s) | 5 / 7 | 857 |
|  | Mine | 4 | — | 4 | placed | — | — | 858 |
|  | Tank | 13 | 0 | 1 | 0.0 s | 150 f (2.74 s) | 1 / 1 | 859 |
| **7** | **Face (boss)** ★ | — | — | 1 | placed | — | — | 863 |
|  | Mine | 5 | — | 5 | placed | — | — | 864 |
| **8** | Robot ★ | 180 | 3 | 0 | 43.8 s | 3 f (0.05 s) | 12 / 14 | 867 |
|  | Chain train | 16 | 0 | 0 | 0.5 s | 30 f (0.55 s) | 11 / 13 | 868 |
|  | Mine | 3 | — | 3 | placed | — | — | 869 |
| **9** | Robot ★ | 290 | 7 | 0 | 7.1 s | 3 f (0.05 s) | 10 / 12 | 873 |
|  | Swarm | 4 | 0 | 0 | 0.0 s | 240 f (4.39 s) | none | 874 |
|  | Mine | 5 | — | 5 | placed | — | — | 876 |
| **11** | Robot ★ | 360 | 5 | 0 | 48.7 s | 3 f (0.05 s) | 12 / 14 | 882 |
|  | Chain train | 16 | 0 | 0 | 0.0 s | 30 f (0.55 s) | 14 / 16 | 883 |
| **12** | Robot ★ | 550 | 4 | 0 | 8.2 s | 3 f (0.05 s) | 20 / 22 | 888 |
|  | Mine | 5 | — | 5 | placed | — | — | 889 |
|  | Fat man | 17 | 0 | 0 | 27.4 s | 210 f (3.84 s) | 5 / 5 | 890 |
| **13** | Saucer | 24 | 0 | 0 | 0.5 s | 12 f (0.22 s) | 2 / 4 | 893 |
|  | Robot ★ | 350 | 4 | 0 | 19.1 s | 15 f (0.27 s) | 15 / 17 | 894 |
|  | Mine | 3 | — | 3 | placed | — | — | 895 |
| **14** | Robot ★ | 380 | 5 | 0 | 8.2 s | 12 f (0.22 s) | 15 / 17 | 898 |
|  | Chain train | 3 | 0 | 0 | 27.4 s | 540 f (9.87 s) | 3 / 5 | 899 |
|  | Fat man | 9 | 0 | 0 | 54.8 s | 30 f (0.55 s) | 5 / 5 | 900 |
|  | Mine | 5 | — | 5 | placed | — | — | 901 |
|  | Tank | 3 | 0 | 0 | 49.3 s | 570 f (10.42 s) | 1 / 1 | 902 |
| **15** | Orb ★ | 90 | 0 | 1 | 0.0 s | 24 f (0.44 s) | 25 / 25 | 905 |
|  | Mine | 5 | — | 5 | placed | — | — | 906 |
|  | Tank | 8 | 0 | 1 | 0.0 s | 570 f (10.42 s) | 1 / 1 | 907 |
| **16** | Robot ★ | 350 | 7 | 0 | 39.4 s | 3 f (0.05 s) | 18 / 20 | 910 |
|  | Chain (single) | 40 | 0 | 0 | 9.9 s | 21 f (0.38 s) | 10 / 12 | 911 |
|  | Fat man | 10 | 0 | 1 | 27.4 s | 570 f (10.42 s) | 5 / 5 | 912 |
|  | Mine | 4 | — | 4 | placed | — | — | 913 |
|  | Swarm | 4 | 0 | 0 | 0.0 s | 225 f (4.11 s) | none | 914 |
|  | Tank | 8 | 0 | 1 | 65.7 s | 180 f (3.29 s) | 1 / 1 | 915 |
| **17** | Robot ★ | 550 | 7 | 0 | 0.0 s | 3 f (0.05 s) | 13 / 15 | 918 |
| **18** | Robot ★ | 690 | 7 | 0 | 65.7 s | 3 f (0.05 s) | 15 / 17 | 921 |
|  | Mine | 2 | — | 2 | placed | — | — | 922 |
|  | Swarm | 9 | 0 | 0 | 0.0 s | 240 f (4.39 s) | none | 923 |
|  | Chain train | 15 | 0 | 0 | 28.5 s | 30 f (0.55 s) | 20 / 22 | 924 |
|  | Saucer | 5 | 0 | 0 | 59.7 s | 30 f (0.55 s) | 20 / 22 | 926 |

#### Circuit 3

| Room | Enemy | Total | Level | On screen at start | First spawn call | Then every | Cap 1P / 2P | Line |
|---|---|---|---|---|---|---|---|---|
| **1** | Grunt ★ | 12 | 2 | 8 | 49.3 s | 3 f (0.05 s) | 9 / 14 | 934 |
|  | Snakes | 15 | 3 | 0 | 21.9 s | 840 f (15.35 s) | none | 935 |
|  | Tank | 24 | 0 | 0 | 0.0 s | 57 f (1.04 s) | 3 / 3 | 936 |
|  | Mine | 5 | — | 5 | placed | — | — | 937 |
| **2** | Grunt ★ | 315 | 2 | 3 | 19.1 s | 24 f (0.44 s) | 9 / 14 | 940 |
|  | Snakes | 335 | 3 | 0 | 0.0 s | 240 f (4.39 s) | none | 941 |
|  | Chain train | 6 | 0 | 0 | 2.7 s | 150 f (2.74 s) | 8 / 10 | 942 |
|  | Mine | 5 | — | 5 | placed | — | — | 943 |
|  | Tank | 3 | 0 | 0 | 82.2 s | 300 f (5.48 s) | 1 / 1 | 944 |
| **3** | Grunt ★ | 700 | 7 | 5 | 0.0 s | 3 f (0.05 s) | 23 / 28 | 947 |
|  | Fat man | 5 | 0 | 2 | 115.1 s | 510 f (9.32 s) | 5 / 5 | 948 |
|  | Snakes | 235 | 0 | 0 | 0.0 s | 870 f (15.90 s) | none | 949 |
|  | Mine | 3 | — | 3 | placed | — | — | 950 |
|  | Tank | 3 | 0 | 0 | 131.5 s | 300 f (5.48 s) | 1 / 1 | 951 |
| **4** | Grunt ★ | 650 | 7 | 5 | 0.0 s | 3 f (0.05 s) | 26 / 31 | 954 |
|  | Mine | 3 | — | 3 | placed | — | — | 955 |
|  | Fat man | 17 | 0 | 4 | 27.4 s | 210 f (3.84 s) | 5 / 5 | 956 |
| **5** | Robot ★ | 400 | 6 | 0 | 0.0 s | 12 f (0.22 s) | 26 / 31 | 959 |
|  | Mine | 8 | — | 8 | placed | — | — | 960 |
| **6** | Grunt ★ | 1040 | 6 | 1 | 43.8 s | 3 f (0.05 s) | 20 / 25 | 963 |
|  | Swarm | 13 | 0 | 0 | 0.0 s | 210 f (3.84 s) | none | 964 |
|  | Mine | 14 | — | 14 | placed | — | — | 965 |
|  | Chain train | 34 | 0 | 0 | 9.8 s | 1200 f (21.93 s) | 5 / 7 | 966 |
| **7** | **Cobras (boss)** ★ | — | — | 1 | placed | — | — | 970 |
| **8** | Grunt ★ | 700 | 5 | 5 | 0.0 s | 6 f (0.11 s) | 17 / 22 | 973 |
|  | Snakes | 835 | 3 | 0 | 1.0 s | 240 f (4.39 s) | none | 974 |
|  | Mine | 6 | — | 6 | placed | — | — | 975 |
| **9** | Grunt ★ | 650 | 6 | 0 | 65.7 s | 3 f (0.05 s) | 18 / 23 | 978 |
|  | Fat man | 17 | 0 | 4 | 27.4 s | 510 f (9.32 s) | 5 / 5 | 979 |
|  | Snakes | 2835 → **787** | 3 | 0 | 2.7 s | 75 f (1.37 s) | none | 980 |
|  | Mine | 5 | — | 5 | placed | — | — | 981 |
| **11** | Grunt ★ | 9 | 2 | 5 | 60.3 s | 3 f (0.05 s) | 22 / 27 | 987 |
|  | Tank | 25 | 0 | 1 | 0.0 s | 45 f (0.82 s) | 4 / 4 | 988 |
|  | Mine | 5 | — | 5 | placed | — | — | 989 |
| **12** | Grunt ★ | 600 | 6 | 0 | 19.1 s | 3 f (0.05 s) | 19 / 24 | 992 |
|  | Mine | 6 | — | 6 | placed | — | — | 993 |
|  | Fat man | 8 | 0 | 0 | 76.7 s | 3 f (0.05 s) | 5 / 5 | 994 |
|  | Chain train | 4 | 0 | 0 | 9.8 s | 150 f (2.74 s) | 13 / 15 | 995 |
|  | Swarm | 5 | 0 | 0 | 38.3 s | 840 f (15.35 s) | none | 996 |
| **13** | Grunt ★ | 400 | 2 | 10 | 0.0 s | 3 f (0.05 s) | 15 / 20 | 999 |
|  | Snakes | 535 | 3 | 0 | 2.7 s | 240 f (4.39 s) | none | 1000 |
|  | Mine | 5 | — | 5 | placed | — | — | 1001 |
|  | Swarm | 1 | 0 | 0 | 38.3 s | 240 f (4.39 s) | none | 1002 |
|  | Fat man | 9 | 0 | 0 | 76.7 s | 3 f (0.05 s) | 5 / 5 | 1003 |
|  | Chain train | 5 | 0 | 0 | 9.8 s | 150 f (2.74 s) | 2 / 4 | 1004 |
| **14** | Grunt ★ | 555 | 5 | 0 | 8.2 s | 3 f (0.05 s) | 19 / 24 | 1007 |
|  | Fat man | 17 | 0 | 0 | 21.9 s | 510 f (9.32 s) | 5 / 5 | 1008 |
|  | Tank | 10 | 0 | 0 | 82.2 s | 300 f (5.48 s) | 1 / 1 | 1009 |
|  | Mine | 6 | — | 6 | placed | — | — | 1010 |
| **15** | Robot ★ | 900 | 5 | 0 | 71.2 s | 3 f (0.05 s) | 17 / 22 | 1013 |
|  | Mine | 4 | — | 4 | placed | — | — | 1014 |
|  | Chain train | 5 | 0 | 0 | 9.8 s | 540 f (9.87 s) | 6 / 8 | 1015 |
|  | Snakes | 2835 → **787** | 4 | 0 | 0.0 s | 99 f (1.81 s) | none | 1016 |
| **16** | Robot ★ | 2440 → **392** | 5 | 0 | 0.0 s | 3 f (0.05 s) | 18 / 23 | 1019 |
|  | Mine | 5 | — | 5 | placed | — | — | 1020 |
|  | Swarm | 13 | 0 | 0 | 49.3 s | 540 f (9.87 s) | none | 1021 |
| **17** | Grunt ★ | 650 | 5 | 1 | 54.8 s | 3 f (0.05 s) | 19 / 24 | 1024 |
|  | Snakes | 2835 → **787** | 4 | 0 | 0.0 s | 75 f (1.37 s) | none | 1025 |
|  | Chain train | 5 | 0 | 0 | 9.8 s | 600 f (10.97 s) | 13 / 15 | 1026 |
|  | Mine | 6 | — | 6 | placed | — | — | 1027 |
| **18** | Grunt ★ | 750 | 5 | 5 | 0.0 s | 9 f (0.16 s) | 23 / 28 | 1030 |
|  | Mine | 3 | — | 3 | placed | — | — | 1031 |
|  | Chain train | 15 | 0 | 0 | 9.8 s | 150 f (2.74 s) | 10 / 12 | 1032 |
|  | Fat man | 27 | 0 | 4 | 27.4 s | 240 f (4.39 s) | 5 / 5 | 1033 |
|  | Swarm | 15 | 0 | 0 | 98.6 s | 540 f (9.87 s) | none | 1034 |
| **19** | Orb ★ | 240 | 0 | 1 | 21.9 s | 24 f (0.44 s) | 23 / 25 | 1037 |
|  | Saucer | 35 | 0 | 0 | 0.0 s | 45 f (0.82 s) | 13 / 15 | 1038 |
|  | Mine | 8 | — | 8 | placed | — | — | 1039 |
|  | Tank | 10 | 0 | 1 | 5.4 s | 540 f (9.87 s) | 1 / 1 | 1040 |
|  | Fat man | 13 | 0 | 0 | 65.7 s | 3 f (0.05 s) | 5 / 5 | 1041 |
| **20** | Grunt ★ | 2540 | 6 | 5 | 4.3 s | 9 f (0.16 s) | 24 / 29 | 1044 |
|  | Mine | 3 | — | 3 | placed | — | — | 1045 |
|  | Chain train | 2 | 0 | 0 | 0.5 s | 30 f (0.55 s) | 14 / 16 | 1046 |
|  | Fat man | 27 | 0 | 4 | 49.3 s | 540 f (9.87 s) | 5 / 5 | 1047 |
|  | Swarm | 2 | 0 | 0 | 115.1 s | 540 f (9.87 s) | none | 1048 |
| **21** | **Mutoid (boss)** ★ | — | — | 1 | placed | — | — | 1052 |
|  | Mine | 2 | — | 2 | placed | — | — | 1053 |
|  | Swarm | 1 | 0 | 0 | 1.0 s | 135 f (2.47 s) | none | 1054 |
|  | Fat man | 35 | 0 | 0 | 54.8 s | 420 f (7.68 s) | 5 / 5 | 1055 |

Patterns worth noticing:
- **Circuit 1 tanks come late** (38–99 s into a room): they punish slow clears rather than set the pace.
- **The 2-player difference is in the caps, not the totals**: circuits 2–3 allow 2–5 more robots, grunts and gals
  alive with two players, and circuit 1 has no player-count adjustment at all.
- Rooms like circuit 1 room 4 (25 grunts, first call at 22.4 s, 7 fat men on screen) are set pieces built around a
  secondary enemy, with the primary used as a timer.
- Several "primary" totals are long trickles (circuit 3 room 1: 12 grunts but first call at 49 s), so the room
  length is set deliberately.

### Scoring

| Item | Points | Source |
|---|---|---|
| Grunt / robot / snake | 500 / 1,050 / 1,000 | `ROBO.EQU` |
| Fat man / orb / wall man | 2,750 / 2,500 / 5,500 | `ROBO.EQU` |
| Tank turret, then tank body | 7,500 + 7,500 | `TANK.ASM:1026, 2179` |
| Gal unit / swarm ball / swarm head | 2,000 / 2,050 / 20,000 | `GAL.ASM:924-946` |
| Prize / start cash / key | 5,010 / 1,250 / 15,000 | `SHOTS.ASM:81-573` |
| Dying | +10 | `ROBO.ASM:1457-1458` |
| 2-player game | every award above added twice | `SCORE.ASM:445-456` |

End-of-circuit bonus ("rackup", the only bonus screen, not doubled in 2-player): each prize collected is worth
2,000–2,500 more per unit (10 units per prize), each cash item 900 per unit (2 units per item) — so a prize pickup
is really 5,010 + 20,000–25,000 (`WAVES.ASM:1784-2060, 2524`).

### Collision rules

The scan runs once per tick in 128-px sectors (`COLL2.ASM:186-219`). Enemy × player and neutral × player are always
checked; enemy × neutral only every 16th tick and only when tanks set `ENON` (so tanks run over grunts);
enemy × enemy only when orbs set `ECON`. The handler is looked up by **victim type × killer type**
(`COLL2.ASM:415-619`), and each handler only affects the victim — two handlers run per collision. The table at
`COLL2.ASM:1059-1254` is where every "who hurts whom" rule above comes from.

## Verification

Each extraction pass was checked by hand against the source before being written up:

- Enemies: grunt speed formula (`HULKS.ASM:109-111, 733-753`), difficulty tables (`ROBO.ASM:262-352`), fat-man
  hit counter (`FATMAN.ASM:277-297`), `PUNISH`/`PUNISHT` (`TANK.ASM:979, 2170`, `WLMAN.ASM:433`), orb death odds
  (`LZRORB.ASM:619-629`), two-player doubling (`SCORE.ASM:445-456`).
- Player: run/shoe velocities (`ROBO.ASM:137-145`), bullet velocities (`SHOTS.ASM:67-72`), ammo `TIMERS`
  (`SCORE.ASM:285`), `MAXBLT` never checked (grep), `RINGTBL` (`ROBO.ASM:348`).
- Bosses: Mutoid thresholds and the normal gun's 1.5 % rule (`MUTOID.ASM:245, 404-416, 623-655`), face phase test
  (`RBOSS.ASM:1258-1280`), cobra kill rule and `WNDTBL` (`COBRA.ASM:854-866`, `ROBO.ASM:350-352`).
- Rooms: record format and values (`ROBO.ASM:742-762, 1042-1046`), the 3-frame spawn clock
  (`ROBO.ASM:1309-1340`), chaser and anti-stall timers (`ENDWAVE.ASM:536-597`). The per-room tables are generated
  from the source, not transcribed.

Known ambiguities, left as-is: the `GALACC` 9-digit literal (`GAL.ASM:625`); `PUNISH` read past its end for
blades and pod bursts on turrets (`TANK.ASM:2102-2106`); the bottom-door state read past `DRINF`
(`HULKS.ASM:1111-1124`); a 16-bit read of the 8-bit `DMGPNT` that may disable Mutoid's post-head action table
(`MUTOID.ASM:2083`). Frame counts that depend on loop structure rather than a single constant are tagged
INFERRED where they appear.
