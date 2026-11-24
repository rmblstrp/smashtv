using System.Collections.Generic;
using Godot;

namespace CameraDemo;

/// <summary>
/// One arena, a playable twin-stick character and a stream of enemies, viewed through
/// switchable fixed cameras. Gameplay runs on the flat floor plane (x, z); the camera, the wall
/// geometry and the character tilt are presentation only, which is the point being demonstrated.
/// </summary>
public partial class Main : Node3D
{
    const float W = Tuning.ArenaWidth, D = Tuning.ArenaDepth, H = Tuning.WallHeight;

    static readonly Walls Upright = new(0, 0, H, Tuning.WallThickness, Tuning.WallThickness);

    // Presets 7 and 8: lean angles fitted to the arcade screenshot. Back-wall face ≈ 22% of the
    // floor's on-screen depth, side-wall faces ≈ 6.7% of its width, front lip ≈ 0.55 m.
    static readonly CameraPreset[] Presets =
    {
        new("1  Top-down", true, 90, 0, false,
            "Straight down. Positions are exact; characters read as heads and shoulders."),
        new("2  Oblique 60°", true, 60, 0, false,
            "Steep three-quarter view. Back wall face visible; floor depth shown at 87%."),
        new("3  Oblique 45°", true, 45, 0, false,
            "Shallow three-quarter view. Tall silhouettes; floor depth at 71%, more overlap."),
        new("4  Smash TV projection", true, 60, 0, true,
            "Ortho 60° with the world stretched 1/sin(pitch) in depth, so floor tiles stay square like the arcade art."),
        new("5  Perspective 60°, FOV 30°", false, 60, 30, false,
            "Long lens. Almost orthographic, with a slight depth cue."),
        new("6  Perspective 50°, FOV 50°", false, 50, 50, false,
            "Wide lens. Strong depth: near side looms, far side shrinks."),
        new("7  Hybrid: 60° + leaned walls", true, 60, 0, true,
            "Preset 4 plus faked walls: back wall leaned 27°, side walls 27°, front wall a 0.5 m curb. Characters upright, real light.",
            new Walls(27, 27, 0.5f, 0.4f, 0.35f)),
        new("8  Literal: straight down + leaned walls + tipped characters", true, 90, 0, false,
            "Plan-view floor, back wall leaned 63°, side walls 27°, characters tipped back 30° to show their fronts. Blob shadows.",
            new Walls(63, 27, 0.5f, 0.4f, 0.55f), ActorTilt: 30),
        new("9  Painted walls + 3D floor", true, 60, 0, true,
            "Walls are a 2D painting on a camera-facing quad; the floor stays 3D, so it takes shadows, decals and light.\n" +
            "A painted front-lip overlay draws over characters. Q/E: the floor stays aligned (the depth stretch keeps it\n" +
            "square at any pitch), but the characters' angle changes while the painted walls can't, so the two stop matching.",
            Backdrop: BackdropMode.Walls),
        new("0  Whole room painted + shadow catcher", true, 60, 0, true,
            "Walls and floor are one painting. An invisible shadow-catcher plane lays the 3D shadows onto it.\n" +
            "Floor decals and dynamic light would each need their own trick.",
            Backdrop: BackdropMode.Room),
    };

    enum Kind { Grunt, Fat, Tank }

    class Actor
    {
        public Kind Kind;
        public Node3D Node, Tilt, Model, Turret, Blob;
        public Vector2 Pos, Vel, Inward;
        public float Radius, Speed, Timer, AgeTimer, FireTimer, Entering, TurretYaw;
        public int Hp, Level;
    }

    class Shot { public Node3D Node, Tilt; public Vector2 Pos, Vel; public bool Hostile; }
    class Fx { public Node3D Node, Tilt; public StandardMaterial3D Mat; public float T; }

    Node3D _world, _arena, _player, _playerTilt, _playerBlob, _legs, _torso, _hitRing;
    Camera3D _cam;
    DirectionalLight3D _sun;
    Label _help, _status, _title;
    Walls _walls;
    BackdropMode _builtMode = BackdropMode.None;
    MeshInstance3D _bdWalls, _bdRoom, _bdFront;
    readonly List<Actor> _enemies = new();
    readonly List<Shot> _shots = new();
    readonly List<Fx> _fx = new();
    readonly List<Node3D> _footprints = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1234 };

    Vector2 _pos;
    float _fireTimer, _spawnTimer, _fatTimer, _tankTimer, _hitCooldown, _time, _pitchOffset, _fovOffset, _tilt;
    int _preset = 1, _kills, _hits;
    bool _showFeet, _autopilot, _showHelp = true, _blobs;
    string _cameraDesc = "", _saveBackdropsTo;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--autopilot") _autopilot = true;
            else if (arg == "--feet") _showFeet = true;
            else if (arg.StartsWith("--preset=")) _preset = Mathf.Clamp(arg[9..].ToInt() - 1, 0, Presets.Length - 1);
            else if (arg.StartsWith("--pitch-offset=")) _pitchOffset = arg[15..].ToFloat();
            else if (arg.StartsWith("--save-backdrops=")) _saveBackdropsTo = arg[17..];
            else if (arg.StartsWith("--quota=")) _quota = arg[8..].ToInt();
        }
        if (_autopilot) _showHelp = false;

        BuildEnvironment();
        BuildHud();
        _bdWalls = BackdropQuad(floor: false, frontOnly: false);
        _bdRoom = BackdropQuad(floor: true, frontOnly: false);
        _bdFront = BackdropQuad(floor: false, frontOnly: true);
        (_player, _playerTilt, _playerBlob) = MakeHolder(Tuning.PlayerRadius);
        (_, _legs, _torso) = Shapes.Player(_playerTilt);
        _hitRing = Shapes.Add(_player, new TorusMesh { InnerRadius = 0.45f, OuterRadius = 0.6f },
            Shapes.Mat(new Color(1, 0.1f, 0.1f), unshaded: true), new Vector3(0, 0.05f, 0));
        _hitRing.Visible = false;
        _pos = new Vector2(0, 3);

        for (int i = 0; i < 6; i++) SpawnEnemy(Kind.Grunt, i % 4, (i / 4) * 0.8f);
        SpawnEnemy(Kind.Fat, 2, 0);
        SpawnEnemy(Kind.Tank, 3, 0);

        GetViewport().SizeChanged += ApplyCamera;
        ApplyCamera();
        if (_saveBackdropsTo != null) SaveBackdrops(_saveBackdropsTo);
    }

    /// <summary>Writes the three paintings as PNGs (templates for an artist), then quits.</summary>
    async void SaveBackdrops(string dir)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (var (quad, name) in new[] { (_bdWalls, "walls"), (_bdRoom, "room"), (_bdFront, "front") })
        {
            var tex = ((StandardMaterial3D)quad.MaterialOverride).AlbedoTexture;
            var err = tex.GetImage().SavePng($"{dir}/backdrop-{name}.png");
            GD.Print($"saved backdrop-{name}.png: {err}");
        }
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- camera

    void ApplyCamera()
    {
        var b = Presets[_preset];
        var p = b with { Pitch = Mathf.Clamp(b.Pitch + _pitchOffset, 20, 90), Fov = Mathf.Clamp(b.Fov + _fovOffset, 10, 90) };
        if (!RoomsActive && (_room != 0 || _phase != Phase.Fighting)) ResetRooms();   // rooms exist in preset 9 only
        var walls = p.Walls ?? Upright;
        if (walls != _walls || p.Backdrop != _builtMode) BuildArena(walls, p.Backdrop);

        float stretch = p.StretchFloor ? 1f / Mathf.Sin(Mathf.DegToRad(p.Pitch)) : 1f;
        _world.Scale = new Vector3(1, 1, stretch);

        _tilt = p.ActorTilt;
        _blobs = p.ActorTilt > 0;
        _sun.ShadowEnabled = !_blobs;   // tipped models cast nonsense shadows; use blobs instead
        SetTilt(_playerTilt, _playerBlob);
        foreach (var a in _enemies) SetTilt(a.Tilt, a.Blob);
        foreach (var s in _shots) SetTilt(s.Tilt, null);
        foreach (var f in _fx) SetTilt(f.Tilt, null);

        var corners = new List<Vector3>();
        float pr = Mathf.DegToRad(p.Pitch);
        var forward = new Vector3(0, -Mathf.Sin(pr), -Mathf.Cos(pr));
        var up = new Vector3(0, Mathf.Cos(pr), -Mathf.Sin(pr));
        if (p.Backdrop == BackdropMode.None)
        {
            var (x, zn, zs) = ArenaExtent(walls);
            foreach (float cx in new[] { -x, x })
                foreach (float cz in new[] { zn * stretch, zs * stretch })
                    foreach (float cy in new[] { 0f, H })
                        corners.Add(new Vector3(cx, cy, cz));
        }
        else
        {
            // Frame the painting, which lives in screen metres on the camera plane.
            foreach (float bx in new[] { BackdropPainter.MinX, BackdropPainter.MaxX })
                foreach (float bv in new[] { BackdropPainter.MinV, BackdropPainter.MaxV })
                    corners.Add(Vector3.Right * bx + up * bv);
        }
        // The painting is screen-aligned at whatever pitch is current, as a painting would be:
        // the floor and characters re-angle under Q/E, the picture can't, so they slide apart.
        var c = BackdropPainter.CenterMeters;
        var basis = new Basis(Vector3.Right, up, -forward);
        _bdRoom.Transform = new Transform3D(basis, Vector3.Right * c.X + up * c.Y + forward * 40f);
        _bdFront.Transform = new Transform3D(basis, Vector3.Right * c.X + up * c.Y - forward * 40f);
        _bdWalls.Visible = false;   // texture source for the per-room paintings of preset 9
        _bdRoom.Visible = p.Backdrop == BackdropMode.Room;
        _bdFront.Visible = p.Backdrop == BackdropMode.Room;
        _stretch = stretch;
        PlaceRoomQuads(basis, up, forward, RoomsActive);
        var size = GetViewport().GetVisibleRect().Size;
        _cameraDesc = CameraFit.Apply(_cam, p, size.X / size.Y, corners.ToArray())
                      + (p.StretchFloor ? $", depth stretch ×{stretch:0.000}" : "")
                      + (p.ActorTilt > 0 ? $", characters tipped {p.ActorTilt:0}°" : "");
        _camBase = _cam.Position;
        UpdateScreenMask(size);
        UpdateCameraPan();
        _title.Text = $"{b.Name}\n{_cameraDesc}";
        _help.Text = _showHelp ? HelpText(b) : "";
    }

    void SetTilt(Node3D tilt, Node3D blob)
    {
        if (IsInstanceValid(tilt)) tilt.RotationDegrees = new Vector3(-_tilt, 0, 0);
        if (blob != null && IsInstanceValid(blob)) blob.Visible = _blobs;
    }

    /// <summary>Outer extent of the room geometry: half-width, north z, south z.</summary>
    static (float x, float zn, float zs) ArenaExtent(Walls w)
    {
        float side = H * Mathf.Sin(Mathf.DegToRad(w.SideLean));
        float north = H * Mathf.Sin(Mathf.DegToRad(w.NorthLean));
        return (W / 2 + w.Thickness + side, -(D / 2 + w.Thickness + north), D / 2 + w.SouthThickness);
    }

    static string HelpText(CameraPreset p) =>
        p.Note + "\n\n" +
        "WASD / left stick: move     Arrows / right stick: shoot\n" +
        "1–9, 0: camera preset     Q / E: pitch −/+5°     Z / X: FOV −/+5° (perspective)\n" +
        "F: show collision footprints     R: reset enemies     H: hide this text" +
        (p.Backdrop == BackdropMode.Walls ? "\nRooms: clear the room (or press K), then walk into the open exit" : "");

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case >= Key.Key1 and <= Key.Key9:
                _preset = (int)(k.Keycode - Key.Key1); _pitchOffset = 0; _fovOffset = 0; break;
            case Key.Key0:
                _preset = 9; _pitchOffset = 0; _fovOffset = 0; break;
            case Key.Q: _pitchOffset -= 5; break;
            case Key.E: _pitchOffset += 5; break;
            case Key.Z: _fovOffset -= 5; break;
            case Key.X: _fovOffset += 5; break;
            case Key.F:
                _showFeet = !_showFeet;
                foreach (var f in _footprints) if (IsInstanceValid(f)) f.Visible = _showFeet;
                _footprints.RemoveAll(f => !IsInstanceValid(f));
                break;
            case Key.R:
                foreach (var a in _enemies) a.Node.QueueFree();
                _enemies.Clear(); break;
            case Key.H: _showHelp = !_showHelp; break;
            case Key.K: ClearRoom(); break;
            default: return;
        }
        ApplyCamera();
    }

    (Vector2 move, Vector2 aim) ReadInput()
    {
        if (_autopilot) return Autopilot();
        static float K(Key a, Key b) => (Input.IsKeyPressed(b) ? 1 : 0) - (Input.IsKeyPressed(a) ? 1 : 0);
        var move = new Vector2(K(Key.A, Key.D), K(Key.W, Key.S));
        var aim = new Vector2(K(Key.Left, Key.Right), K(Key.Up, Key.Down));
        if (Input.GetConnectedJoypads().Count > 0)
        {
            var ls = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
            var rs = new Vector2(Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY));
            if (ls.Length() > 0.25f) move = ls;
            if (rs.Length() > 0.4f) aim = rs;
        }
        return (move.LimitLength(1), aim);
    }

    /// <summary>Scripted player for unattended renders: a slow figure-eight, shooting the nearest enemy.</summary>
    (Vector2, Vector2) Autopilot()
    {
        if (RoomsActive && _phase == Phase.Cleared) return (AutopilotToExit(), Vector2.Zero);
        var target = _origin + new Vector2(6.5f * Mathf.Sin(0.35f * _time), 3.2f * Mathf.Sin(0.7f * _time + 1f));
        var move = ((target - _pos) * 0.8f).LimitLength(1);
        Actor best = null;
        foreach (var a in _enemies)
            if (a.Entering <= 0 && (best == null || a.Pos.DistanceTo(_pos) < best.Pos.DistanceTo(_pos))) best = a;
        return (move, best == null ? Vector2.Zero : Snap8(best.Pos - _pos));
    }

    // ---------------------------------------------------------------- simulation

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        var (move, aim) = ReadInput();
        bool scripted = RoomsActive && _phase == Phase.Transition;
        if (scripted) { move = _scriptDir; aim = Vector2.Zero; }   // walk-out script: no control, no fire

        _pos += move * Tuning.PlayerSpeed * dt;
        if (!scripted) _pos = Clamp(_pos, Tuning.PlayerRadius);
        if (move.Length() > 0.1f) _legs.Rotation = new Vector3(0, Yaw(move), 0);
        if (aim.Length() > 0.3f) _torso.Rotation = new Vector3(0, Yaw(aim), 0);
        else if (move.Length() > 0.1f) _torso.Rotation = _legs.Rotation;

        _fireTimer -= dt;
        if (aim.Length() > 0.3f && _fireTimer <= 0)
        {
            _fireTimer = Tuning.FireInterval;
            var dir = aim.Normalized();
            AddShot(_pos + dir * 0.6f, dir * Tuning.BulletSpeed, hostile: false);
        }

        if (RoomsActive) UpdateRooms(dt);
        if (!RoomsActive || (_phase == Phase.Fighting && !EnemiesFrozen)) Spawn(dt);
        UpdateEnemies(dt);
        UpdateShots(dt);
        UpdateFx(dt);

        _hitCooldown -= dt;
        _hitRing.Visible = _hitCooldown > 0.4f;
        _player.Position = new Vector3(_pos.X, 0, _pos.Y);
        _status.Text = (RoomsActive ? RoomStatus() + "      " : "") + $"enemies {_enemies.Count}   kills {_kills}   hits taken {_hits}";
    }

    /// <summary>
    /// Every dynamic object is holder (floor position, footprint, blob shadow) → tilt (presentation
    /// lean for preset 8) → model (yaw). Gameplay only ever touches the holder's floor position.
    /// </summary>
    (Node3D holder, Node3D tilt, Node3D blob) MakeHolder(float radius)
    {
        var holder = new Node3D();
        _world.AddChild(holder);
        var tilt = new Node3D { RotationDegrees = new Vector3(-_tilt, 0, 0) };
        holder.AddChild(tilt);
        Node3D blob = null;
        if (radius > 0)
        {
            var fp = Shapes.Footprint(holder, radius);
            fp.Visible = _showFeet;
            _footprints.Add(fp);
            blob = Shapes.Add(holder, new CylinderMesh { TopRadius = radius * 1.1f, BottomRadius = radius * 1.1f, Height = 0.01f },
                Shapes.Mat(new Color(0, 0, 0, 0.4f), unshaded: true), new Vector3(0, 0.01f, 0));
            blob.Visible = _blobs;
        }
        return (holder, tilt, blob);
    }

    void Spawn(float dt)
    {
        _spawnTimer -= dt; _fatTimer -= dt; _tankTimer -= dt;
        int grunts = 0, fats = 0, tanks = 0;
        foreach (var a in _enemies)
        {
            if (a.Kind == Kind.Grunt) grunts++;
            else if (a.Kind == Kind.Fat) fats++;
            else tanks++;
        }
        if (_spawnTimer <= 0)
        {
            _spawnTimer = Tuning.GruntSpawnInterval;
            if (grunts < Tuning.MaxGrunts)
            {
                int door = _rng.RandiRange(0, 3);
                int n = _rng.Randf() < Tuning.GangChance ? _rng.RandiRange(6, 10) : 1;
                for (int i = 0; i < n; i++) SpawnEnemy(Kind.Grunt, door, (i % 3 - 1) * 0.8f, i / 3 * 0.9f);
            }
        }
        if (fats < 2 && _fatTimer <= 0) { SpawnEnemy(Kind.Fat, _rng.RandiRange(0, 3), 0); _fatTimer = 4; }
        if (tanks < 1 && _tankTimer <= 0) { SpawnEnemy(Kind.Tank, new[] { 0, 1, 3 }[_rng.RandiRange(0, 2)], 0); _tankTimer = 6; }
    }

    // Doors: 0 north (-Z), 1 south (+Z), 2 west (-X), 3 east (+X). Outward normals.
    static readonly Vector2[] DoorNormal = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

    void SpawnEnemy(Kind kind, int door, float sideways, float back = 0)
    {
        var n = DoorNormal[door];
        var edge = n.X != 0 ? W / 2 : D / 2;
        var tangent = new Vector2(-n.Y, n.X);
        var a = NewActor(kind);
        a.Inward = -n;
        a.Pos = _origin + n * (edge + 1.2f + back) + tangent * sideways;
        a.Entering = (2.2f + back) / Tuning.PlayerSpeed;
        a.Node.Position = new Vector3(a.Pos.X, 0, a.Pos.Y);
    }

    /// <summary>An enemy standing at a floor position, already inside the room (start-of-room placement).</summary>
    void PlaceEnemy(Kind kind, Vector2 pos, Vector2 facing)
    {
        var a = NewActor(kind);
        a.Pos = pos;
        a.Node.Position = new Vector3(pos.X, 0, pos.Y);
        a.Model.Rotation = new Vector3(0, Yaw(facing), 0);
        a.TurretYaw = Yaw(facing);
    }

    Actor NewActor(Kind kind)
    {
        var a = new Actor { Kind = kind };
        a.Radius = kind switch { Kind.Grunt => Tuning.GruntRadius, Kind.Fat => Tuning.FatRadius, _ => Tuning.TankRadius };
        (a.Node, a.Tilt, a.Blob) = MakeHolder(a.Radius);
        switch (kind)
        {
            case Kind.Grunt:
                a.Model = Shapes.Grunt(a.Tilt); a.Hp = 1;
                a.Level = _rng.RandiRange(0, 3); a.Speed = Tuning.GruntSpeed(a.Level);
                a.AgeTimer = _rng.RandfRange(5, 9);
                break;
            case Kind.Fat:
                a.Model = Shapes.Fat(a.Tilt); a.Hp = Tuning.FatHits; a.Speed = Tuning.FatSpeed;
                break;
            case Kind.Tank:
                (a.Model, a.Turret) = Shapes.Tank(a.Tilt);
                a.Hp = Tuning.TankTurretDamage; a.Speed = Tuning.TankSpeed; a.FireTimer = 2;
                break;
        }
        _enemies.Add(a);
        return a;
    }

    void UpdateEnemies(float dt)
    {
        if (EnemiesFrozen) return;   // start-of-room freeze: no movement, no contact
        foreach (var a in _enemies)
        {
            if (a.Entering > 0)
            {
                a.Entering -= dt;
                a.Vel = a.Inward * Mathf.Max(a.Speed, Tuning.PlayerSpeed * 0.6f);
            }
            else if (a.Kind == Kind.Grunt)
            {
                a.AgeTimer -= dt;
                if (a.AgeTimer <= 0 && a.Level < Tuning.GruntMaxLevel)
                {
                    a.Level++; a.Speed = Tuning.GruntSpeed(a.Level); a.AgeTimer = _rng.RandfRange(5, 9);
                }
                if ((a.Timer -= dt) <= 0)
                {
                    a.Timer = _rng.RandfRange(0.2f, 1.0f);
                    a.Vel = Snap8(_pos - a.Pos) * a.Speed;
                }
            }
            else if (a.Kind == Kind.Fat)
            {
                if ((a.Timer -= dt) <= 0) { a.Timer = 1f; a.Vel = Snap8(_pos - a.Pos) * a.Speed; }
            }
            else
            {
                if ((a.Timer -= dt) <= 0)
                {
                    a.Timer = _rng.RandfRange(2.5f, 4.5f);
                    a.Vel = DoorNormal[_rng.RandiRange(0, 3)] * a.Speed;
                }
            }

            a.Pos += a.Vel * dt;
            if (a.Entering <= 0)
            {
                var clamped = Clamp(a.Pos, a.Radius);
                if (a.Kind == Kind.Tank && clamped != a.Pos) a.Vel = -a.Vel;
                a.Pos = clamped;
            }
            a.Node.Position = new Vector3(a.Pos.X, 0, a.Pos.Y);
            if (a.Vel.Length() > 0.01f) a.Model.Rotation = new Vector3(0, Yaw(a.Vel), 0);
            if (a.Kind == Kind.Tank && a.Entering <= 0) AimTurret(a, dt);

            if (a.Entering <= 0 && _hitCooldown <= 0 && a.Pos.DistanceTo(_pos) < a.Radius + Tuning.PlayerRadius)
                PlayerHit();
        }
    }

    void AimTurret(Actor a, float dt)
    {
        float want = Yaw(_pos - a.Pos);
        a.TurretYaw = Mathf.RotateToward(a.TurretYaw, want, Mathf.DegToRad(120) * dt);
        a.Turret.Rotation = new Vector3(0, a.TurretYaw - a.Model.Rotation.Y, 0);   // turret is a child of the hull
        a.FireTimer -= dt;
        if (a.FireTimer <= 0 && Mathf.Abs(Mathf.AngleDifference(a.TurretYaw, want)) < Mathf.DegToRad(10))
        {
            a.FireTimer = _rng.RandfRange(1.2f, 2.2f);
            var dir = new Vector2(-Mathf.Sin(a.TurretYaw), -Mathf.Cos(a.TurretYaw));
            AddShot(a.Pos + dir * 1.1f, dir * Tuning.CannonballSpeed + a.Vel, hostile: true);
        }
    }

    void AddShot(Vector2 pos, Vector2 vel, bool hostile)
    {
        var (holder, tilt, _) = MakeHolder(0);
        if (hostile) Shapes.Projectile(tilt, new Color(1f, 0.55f, 0.1f), 0.18f, 0.85f);
        else Shapes.Projectile(tilt, new Color(0.75f, 0.9f, 1f), 0.09f, 1.15f);   // gun height, not feet
        _shots.Add(new Shot { Node = holder, Tilt = tilt, Pos = pos, Vel = vel, Hostile = hostile });
        holder.Position = new Vector3(pos.X, 0, pos.Y);
    }

    void UpdateShots(float dt)
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Pos += s.Vel * dt;
            s.Node.Position = new Vector3(s.Pos.X, 0, s.Pos.Y);
            bool gone = Mathf.Abs(s.Pos.X - _origin.X) > W / 2 || Mathf.Abs(s.Pos.Y - _origin.Y) > D / 2;
            if (!gone && s.Hostile && _hitCooldown <= 0 && s.Pos.DistanceTo(_pos) < Tuning.PlayerRadius + 0.18f)
            {
                PlayerHit(); gone = true;
            }
            if (!gone && !s.Hostile)
            {
                for (int j = _enemies.Count - 1; j >= 0; j--)
                {
                    var a = _enemies[j];
                    if (s.Pos.DistanceTo(a.Pos) > a.Radius + 0.1f) continue;
                    gone = true;
                    a.Hp -= a.Kind == Kind.Tank ? 2 : 1;                 // laser does 2 on the PUNISH scale
                    if (a.Kind == Kind.Fat) a.Pos += s.Vel.Normalized() * (6f / Tuning.PxPerMeter);
                    if (a.Hp <= 0) Kill(j);
                    break;
                }
            }
            if (gone) { s.Node.QueueFree(); _shots.RemoveAt(i); }
        }
    }

    void Kill(int index, bool scored = true)
    {
        var a = _enemies[index];
        var (holder, tilt, _) = MakeHolder(0);
        holder.Position = a.Node.Position;
        var mat = Shapes.Mat(new Color(1f, 0.6f, 0.2f, 0.9f), unshaded: true);
        Shapes.Add(tilt, new SphereMesh { Radius = a.Radius, Height = a.Radius * 2 }, mat, new Vector3(0, 0.8f, 0));
        _fx.Add(new Fx { Node = holder, Tilt = tilt, Mat = mat });
        a.Node.QueueFree();
        _enemies.RemoveAt(index);
        if (scored) { _kills++; _roomKills++; }
    }

    void UpdateFx(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var f = _fx[i];
            f.T += dt;
            f.Node.Scale = Vector3.One * (1 + f.T * 8);
            f.Mat.AlbedoColor = f.Mat.AlbedoColor with { A = Mathf.Max(0, 0.9f - f.T * 4) };
            if (f.T > 0.25f) { f.Node.QueueFree(); _fx.RemoveAt(i); }
        }
    }

    void PlayerHit() { _hits++; _hitCooldown = 1f; }

    // ---------------------------------------------------------------- helpers

    Vector2 Clamp(Vector2 p, float r) =>
        new(Mathf.Clamp(p.X, _origin.X - W / 2 + r, _origin.X + W / 2 - r),
            Mathf.Clamp(p.Y, _origin.Y - D / 2 + r, _origin.Y + D / 2 - r));

    /// <summary>Yaw that turns a -Z-facing model toward a floor direction (x, z).</summary>
    static float Yaw(Vector2 d) => Mathf.Atan2(-d.X, -d.Y);

    static Vector2 Snap8(Vector2 v)
    {
        if (v.LengthSquared() < 1e-6f) return Vector2.Zero;
        float step = Mathf.Pi / 4;
        float a = Mathf.Round(Mathf.Atan2(v.Y, v.X) / step) * step;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    // ---------------------------------------------------------------- scene construction

    void BuildEnvironment()
    {
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = new Color(0.06f, 0.05f, 0.05f),
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.75f, 0.72f, 0.68f),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        _sun = new DirectionalLight3D { RotationDegrees = new Vector3(-58, 25, 0), LightEnergy = 1.1f, ShadowEnabled = true };
        AddChild(_sun);
        _cam = new Camera3D { Current = true };
        AddChild(_cam);
        _world = new Node3D { Name = "World" };
        AddChild(_world);
    }

    /// <summary>
    /// Floor plus four walls. Each wall is one pivot on its floor boundary line, leaned by
    /// rotating the pivot, so lean never moves the collision edge. The north and side walls are
    /// extended to meet the leaned neighbours; where the slabs cross, the corner seams come out
    /// diagonal, like the arcade art.
    /// </summary>
    void BuildArena(Walls w, BackdropMode mode)
    {
        _arena?.QueueFree();
        _arena = new Node3D { Name = "Arena" };
        _world.AddChild(_arena);
        _walls = w;
        _builtMode = mode;

        if (mode == BackdropMode.Room)
        {
            // Painted floor: an invisible plane that only shows the shadows falling on it.
            // Not render_mode shadow_to_opacity: that clamps alpha by ambient × albedo
            // (scene_forward_clustered.glsl), so a black shadow comes out fully transparent.
            // Instead the light pass writes alpha = how much of the sun is blocked.
            var catcher = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = """
                        shader_type spatial;
                        render_mode blend_mix, depth_draw_never, specular_disabled, ambient_light_disabled;
                        uniform float strength = 0.55;
                        void fragment() { ALBEDO = vec3(0.0); ALPHA = 0.0; }
                        void light() { if (LIGHT_IS_DIRECTIONAL) { ALPHA = (1.0 - ATTENUATION) * strength; } }
                        """,
                },
            };
            Shapes.Add(_arena, new PlaneMesh { Size = new Vector2(W, D) }, catcher, new Vector3(0, 0.005f, 0));
            return;
        }

        var floorMat = new StandardMaterial3D
        {
            AlbedoTexture = Shapes.GridTexture(),
            Uv1Scale = new Vector3(W, D, 1),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (mode == BackdropMode.Walls) { BuildRoomFloors(floorMat); return; }   // walls come from the paintings
        Shapes.Add(_arena, new PlaneMesh { Size = new Vector2(W, D) }, floorMat, Vector3.Zero);
        // A circle on the floor: it turns into an ellipse wherever the floor is foreshortened.
        var ring = Shapes.Add(_arena, new TorusMesh { InnerRadius = 2.6f, OuterRadius = 3.0f },
            Shapes.Mat(new Color(0.6f, 0.12f, 0.1f)), new Vector3(0, 0.01f, 0));
        ring.Scale = new Vector3(1, 0.05f, 1);

        Shapes.Add(_arena, new PlaneMesh { Size = new Vector2(W + 16, D + 16) },
            Shapes.Mat(new Color(0.1f, 0.09f, 0.08f)), new Vector3(0, -0.02f, 0));

        var wall = new Color(0.78f, 0.68f, 0.48f);
        var trim = new Color(0.55f, 0.45f, 0.3f);
        var post = new Color(0.35f, 0.36f, 0.4f);
        float t = w.Thickness, gap = Tuning.DoorWidth;
        var (xOut, zNorth, zSouth) = ArenaExtent(w);

        // North wall: pivot on z = -D/2, top tipped toward -Z.
        var north = new Node3D { Position = new Vector3(0, 0, -D / 2), RotationDegrees = new Vector3(-w.NorthLean, 0, 0) };
        _arena.AddChild(north);
        float nLen = xOut - gap / 2;
        foreach (float s in new[] { -1f, 1f })
        {
            float cx = s * (gap / 2 + nLen / 2);
            Shapes.Box(north, new Vector3(nLen, H, t), wall, new Vector3(cx, H / 2, -t / 2));
            Shapes.Box(north, new Vector3(nLen, 0.2f, t + 0.1f), trim, new Vector3(cx, H, -t / 2));
            Shapes.Box(north, new Vector3(0.3f, H + 0.3f, t + 0.2f), post, new Vector3(s * gap / 2, (H + 0.3f) / 2, -t / 2));
            // Score screens set into the wall face, like the arcade's HUD-in-the-set.
            Shapes.Box(north, new Vector3(4.5f, 1.6f, 0.1f), new Color(0.08f, 0.08f, 0.1f), new Vector3(s * 6.5f, 1.8f, 0.05f));
            Shapes.Box(north, new Vector3(4.0f, 0.25f, 0.12f), new Color(0.2f, 0.75f, 0.3f), new Vector3(s * 6.5f, 1.45f, 0.06f));
        }

        // Side walls: pivot on x = ±W/2, top tipped outward; z-span reaches the leaned north wall.
        float zN = zNorth, zS = D / 2 + w.SouthThickness;
        foreach (float s in new[] { -1f, 1f })
        {
            var side = new Node3D { Position = new Vector3(s * W / 2, 0, 0), RotationDegrees = new Vector3(0, 0, -s * w.SideLean) };
            _arena.AddChild(side);
            foreach (var (z0, z1) in new[] { (zN, -gap / 2), (gap / 2, zS) })
            {
                float len = z1 - z0, cz = (z0 + z1) / 2;
                Shapes.Box(side, new Vector3(t, H, len), wall, new Vector3(s * t / 2, H / 2, cz));
                Shapes.Box(side, new Vector3(t + 0.1f, 0.2f, len), trim, new Vector3(s * t / 2, H, cz));
            }
            foreach (float e in new[] { -1f, 1f })
                Shapes.Box(side, new Vector3(t + 0.2f, H + 0.3f, 0.3f), post, new Vector3(s * t / 2, (H + 0.3f) / 2, e * gap / 2));
        }

        // South wall: upright; a low curb in presets 7-8 so the near edge reads as a lip.
        float hs = w.SouthHeight, ts = w.SouthThickness, sLen = xOut - gap / 2;
        foreach (float s in new[] { -1f, 1f })
        {
            float cx = s * (gap / 2 + sLen / 2);
            Shapes.Box(_arena, new Vector3(sLen, hs, ts), wall, new Vector3(cx, hs / 2, D / 2 + ts / 2));
            Shapes.Box(_arena, new Vector3(0.3f, hs + 0.2f, ts + 0.2f), post, new Vector3(s * gap / 2, (hs + 0.2f) / 2, D / 2 + ts / 2));
        }
    }

    /// <summary>
    /// A painting rendered once into a transparent SubViewport and shown unshaded on a quad that
    /// faces the camera. Lives outside the depth-stretched world so it is never distorted.
    /// </summary>
    MeshInstance3D BackdropQuad(bool floor, bool frontOnly)
    {
        var vp = new SubViewport
        {
            Size = BackdropPainter.PixelSize,
            TransparentBg = true,
            Disable3D = true,
            Msaa2D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        vp.AddChild(new BackdropPainter { PaintFloor = floor, FrontOnly = frontOnly });
        AddChild(vp);
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = vp.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = BackdropPainter.SizeMeters },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(quad);
        return quad;
    }

    void BuildHud()
    {
        // Screen mask for the room loop: the arcade's screen is exactly one room, and neighbouring
        // art only appears when the scroll brings it into that window. Black bars hide whatever a
        // wider window shows past the room painting.
        var mask = new CanvasLayer { Layer = 1 };
        AddChild(mask);
        for (int i = 0; i < 4; i++)
        {
            _maskBars[i] = new ColorRect { Color = Colors.Black, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            mask.AddChild(_maskBars[i]);
        }
        var layer = new CanvasLayer { Layer = 2 };
        AddChild(layer);
        _title = Hud(layer, new Vector2(16, 10), 18);
        _help = Hud(layer, new Vector2(16, 64), 13);
        _status = Hud(layer, Vector2.Zero, 14);
        _status.AnchorTop = 1; _status.AnchorBottom = 1; _status.OffsetTop = -34; _status.OffsetLeft = 16;
    }

    readonly ColorRect[] _maskBars = new ColorRect[4];

    /// <summary>Left/right/top/bottom bars outside the room painting (preset 9 only; camera is orthographic there).</summary>
    void UpdateScreenMask(Vector2 screen)
    {
        bool on = RoomsActive && _cam.Projection == Camera3D.ProjectionType.Orthogonal;
        foreach (var bar in _maskBars) bar.Visible = on;
        if (!on) return;
        float viewH = _cam.Size, viewW = viewH * screen.X / screen.Y;
        var paint = BackdropPainter.SizeMeters;
        float bx = Mathf.Max(0, (viewW - paint.X) / 2 / viewW * screen.X);
        float by = Mathf.Max(0, (viewH - paint.Y) / 2 / viewH * screen.Y);
        _maskBars[0].Position = Vector2.Zero; _maskBars[0].Size = new Vector2(bx, screen.Y);
        _maskBars[1].Position = new Vector2(screen.X - bx, 0); _maskBars[1].Size = new Vector2(bx, screen.Y);
        _maskBars[2].Position = Vector2.Zero; _maskBars[2].Size = new Vector2(screen.X, by);
        _maskBars[3].Position = new Vector2(0, screen.Y - by); _maskBars[3].Size = new Vector2(screen.X, by);
    }

    static Label Hud(CanvasLayer layer, Vector2 pos, int size)
    {
        var l = new Label { Position = pos };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(l);
        return l;
    }
}
