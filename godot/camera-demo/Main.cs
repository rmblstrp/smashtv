using System.Collections.Generic;
using Godot;

namespace CameraDemo;

/// <summary>
/// One arena, a playable twin-stick character and a stream of enemies, viewed through
/// switchable fixed cameras. Gameplay runs on the flat floor plane (x, z); the camera is
/// purely presentation, which is the point being demonstrated.
/// </summary>
public partial class Main : Node3D
{
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
    };

    enum Kind { Grunt, Fat, Tank }

    class Actor
    {
        public Kind Kind;
        public Node3D Node, Turret;
        public Vector2 Pos, Vel, Inward;
        public float Radius, Speed, Timer, AgeTimer, FireTimer, Entering, TurretYaw;
        public int Hp, Level;
    }

    class Shot { public Node3D Node; public Vector2 Pos, Vel; public bool Hostile; }
    class Fx { public Node3D Node; public StandardMaterial3D Mat; public float T; }

    const float W = Tuning.ArenaWidth, D = Tuning.ArenaDepth, H = Tuning.WallHeight, T = Tuning.WallThickness;

    Node3D _world, _player, _legs, _torso, _hitRing;
    Camera3D _cam;
    Label _help, _status, _title;
    readonly List<Actor> _enemies = new();
    readonly List<Shot> _shots = new();
    readonly List<Fx> _fx = new();
    readonly List<Node3D> _footprints = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1234 };

    Vector2 _pos;
    float _fireTimer, _spawnTimer, _fatTimer, _tankTimer, _hitCooldown, _time, _pitchOffset, _fovOffset;
    int _preset = 1, _kills, _hits;
    bool _showFeet, _autopilot, _showHelp = true;
    string _cameraDesc = "";

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--autopilot") _autopilot = true;
            else if (arg == "--feet") _showFeet = true;
            else if (arg.StartsWith("--preset=")) _preset = Mathf.Clamp(arg[9..].ToInt() - 1, 0, Presets.Length - 1);
        }
        if (_autopilot) _showHelp = false;

        BuildEnvironment();
        BuildArena();
        BuildHud();
        (_player, _legs, _torso) = Shapes.Player(_world);
        AddFootprint(_player, Tuning.PlayerRadius);
        _hitRing = Shapes.Add(_world, new TorusMesh { InnerRadius = 0.45f, OuterRadius = 0.6f },
            Shapes.Mat(new Color(1, 0.1f, 0.1f), unshaded: true), Vector3.Zero);
        _hitRing.Visible = false;
        _pos = new Vector2(0, 3);

        for (int i = 0; i < 6; i++) SpawnEnemy(Kind.Grunt, i % 4, (i / 4) * 0.8f);
        SpawnEnemy(Kind.Fat, 2, 0);
        SpawnEnemy(Kind.Tank, 3, 0);

        GetViewport().SizeChanged += ApplyCamera;
        ApplyCamera();
    }

    // ---------------------------------------------------------------- camera

    void ApplyCamera()
    {
        var b = Presets[_preset];
        var p = b with { Pitch = Mathf.Clamp(b.Pitch + _pitchOffset, 20, 90), Fov = Mathf.Clamp(b.Fov + _fovOffset, 10, 90) };
        float stretch = p.StretchFloor ? 1f / Mathf.Sin(Mathf.DegToRad(p.Pitch)) : 1f;
        _world.Scale = new Vector3(1, 1, stretch);

        float x = W / 2 + T, z = (D / 2 + T) * stretch;
        var corners = new List<Vector3>();
        foreach (float cx in new[] { -x, x })
            foreach (float cz in new[] { -z, z })
                foreach (float cy in new[] { 0f, H })
                    corners.Add(new Vector3(cx, cy, cz));
        var size = GetViewport().GetVisibleRect().Size;
        _cameraDesc = CameraFit.Apply(_cam, p, size.X / size.Y, corners.ToArray())
                      + (p.StretchFloor ? $", depth stretch ×{stretch:0.000}" : "");
        _title.Text = $"{b.Name}\n{_cameraDesc}";
        _help.Text = _showHelp ? HelpText(b) : "";
    }

    static string HelpText(CameraPreset p) =>
        p.Note + "\n\n" +
        "WASD / left stick: move     Arrows / right stick: shoot\n" +
        "1–6: camera preset     Q / E: pitch −/+5°     Z / X: FOV −/+5° (perspective)\n" +
        "F: show collision footprints     R: reset enemies     H: hide this text";

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case >= Key.Key1 and <= Key.Key6:
                _preset = (int)(k.Keycode - Key.Key1); _pitchOffset = 0; _fovOffset = 0; break;
            case Key.Q: _pitchOffset -= 5; break;
            case Key.E: _pitchOffset += 5; break;
            case Key.Z: _fovOffset -= 5; break;
            case Key.X: _fovOffset += 5; break;
            case Key.F:
                _showFeet = !_showFeet;
                foreach (var f in _footprints) if (IsInstanceValid(f)) f.Visible = _showFeet;
                break;
            case Key.R:
                foreach (var a in _enemies) a.Node.QueueFree();
                _enemies.Clear(); break;
            case Key.H: _showHelp = !_showHelp; break;
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
        var target = new Vector2(6.5f * Mathf.Sin(0.35f * _time), 3.2f * Mathf.Sin(0.7f * _time + 1f));
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

        _pos += move * Tuning.PlayerSpeed * dt;
        _pos = Clamp(_pos, Tuning.PlayerRadius);
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

        Spawn(dt);
        UpdateEnemies(dt);
        UpdateShots(dt);
        UpdateFx(dt);

        _hitCooldown -= dt;
        _hitRing.Visible = _hitCooldown > 0.4f;
        _player.Position = new Vector3(_pos.X, 0, _pos.Y);
        _hitRing.Position = _player.Position + new Vector3(0, 0.05f, 0);
        _status.Text = $"enemies {_enemies.Count}   kills {_kills}   hits taken {_hits}";
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
        var a = new Actor
        {
            Kind = kind,
            Inward = -n,
            Pos = n * (edge + 1.2f + back) + tangent * sideways,
            Entering = (2.2f + back) / Tuning.PlayerSpeed,
        };
        switch (kind)
        {
            case Kind.Grunt:
                a.Node = Shapes.Grunt(_world); a.Radius = Tuning.GruntRadius; a.Hp = 1;
                a.Level = _rng.RandiRange(0, 3); a.Speed = Tuning.GruntSpeed(a.Level);
                a.AgeTimer = _rng.RandfRange(5, 9);
                break;
            case Kind.Fat:
                a.Node = Shapes.Fat(_world); a.Radius = Tuning.FatRadius; a.Hp = Tuning.FatHits;
                a.Speed = Tuning.FatSpeed;
                break;
            case Kind.Tank:
                (a.Node, a.Turret) = Shapes.Tank(_world); a.Radius = Tuning.TankRadius;
                a.Hp = Tuning.TankTurretDamage; a.Speed = Tuning.TankSpeed; a.FireTimer = 2;
                break;
        }
        AddFootprint(a.Node, a.Radius);
        a.Node.Position = new Vector3(a.Pos.X, 0, a.Pos.Y);
        _enemies.Add(a);
    }

    void UpdateEnemies(float dt)
    {
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
                AimTurret(a, dt);
            }

            a.Pos += a.Vel * dt;
            if (a.Entering <= 0)
            {
                var clamped = Clamp(a.Pos, a.Radius);
                if (a.Kind == Kind.Tank && clamped != a.Pos) a.Vel = -a.Vel;
                a.Pos = clamped;
            }
            a.Node.Position = new Vector3(a.Pos.X, 0, a.Pos.Y);
            if (a.Vel.Length() > 0.01f) a.Node.Rotation = new Vector3(0, Yaw(a.Vel), 0);

            if (a.Entering <= 0 && _hitCooldown <= 0 && a.Pos.DistanceTo(_pos) < a.Radius + Tuning.PlayerRadius)
                PlayerHit();
        }
    }

    void AimTurret(Actor a, float dt)
    {
        float want = Yaw(_pos - a.Pos);
        a.TurretYaw = Mathf.RotateToward(a.TurretYaw, want, Mathf.DegToRad(120) * dt);
        a.Turret.GlobalRotation = new Vector3(0, a.TurretYaw, 0);
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
        var node = hostile
            ? Shapes.Projectile(_world, new Color(1f, 0.55f, 0.1f), 0.18f, 0.85f)
            : Shapes.Projectile(_world, new Color(0.75f, 0.9f, 1f), 0.09f, 1.15f);   // gun height, not feet
        _shots.Add(new Shot { Node = node, Pos = pos, Vel = vel, Hostile = hostile });
        node.Position = new Vector3(pos.X, 0, pos.Y);
    }

    void UpdateShots(float dt)
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Pos += s.Vel * dt;
            s.Node.Position = new Vector3(s.Pos.X, 0, s.Pos.Y);
            bool gone = Mathf.Abs(s.Pos.X) > W / 2 || Mathf.Abs(s.Pos.Y) > D / 2;
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

    void Kill(int index)
    {
        var a = _enemies[index];
        var mat = Shapes.Mat(new Color(1f, 0.6f, 0.2f, 0.9f), unshaded: true);
        var fx = new Node3D { Position = a.Node.Position + new Vector3(0, 0.8f, 0) };
        _world.AddChild(fx);
        Shapes.Add(fx, new SphereMesh { Radius = a.Radius, Height = a.Radius * 2 }, mat, Vector3.Zero);
        _fx.Add(new Fx { Node = fx, Mat = mat });
        a.Node.QueueFree();
        _enemies.RemoveAt(index);
        _kills++;
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

    static Vector2 Clamp(Vector2 p, float r) =>
        new(Mathf.Clamp(p.X, -W / 2 + r, W / 2 - r), Mathf.Clamp(p.Y, -D / 2 + r, D / 2 - r));

    /// <summary>Yaw that turns a -Z-facing model toward a floor direction (x, z).</summary>
    static float Yaw(Vector2 d) => Mathf.Atan2(-d.X, -d.Y);

    static Vector2 Snap8(Vector2 v)
    {
        if (v.LengthSquared() < 1e-6f) return Vector2.Zero;
        float step = Mathf.Pi / 4;
        float a = Mathf.Round(Mathf.Atan2(v.Y, v.X) / step) * step;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    void AddFootprint(Node3D owner, float radius)
    {
        var f = Shapes.Footprint(owner, radius);
        f.Visible = _showFeet;
        _footprints.Add(f);
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
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-58, 25, 0),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        });
        _cam = new Camera3D { Current = true };
        AddChild(_cam);
        _world = new Node3D { Name = "World" };
        AddChild(_world);
    }

    void BuildArena()
    {
        var floorMat = new StandardMaterial3D
        {
            AlbedoTexture = Shapes.GridTexture(),
            Uv1Scale = new Vector3(W, D, 1),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        Shapes.Add(_world, new PlaneMesh { Size = new Vector2(W, D) }, floorMat, Vector3.Zero);
        Shapes.Add(_world, new PlaneMesh { Size = new Vector2(W + 12, D + 12) },
            Shapes.Mat(new Color(0.1f, 0.09f, 0.08f)), new Vector3(0, -0.02f, 0));
        // A circle on the floor: it turns into an ellipse wherever the floor is foreshortened.
        var ring = Shapes.Add(_world, new TorusMesh { InnerRadius = 2.6f, OuterRadius = 3.0f },
            Shapes.Mat(new Color(0.6f, 0.12f, 0.1f)), new Vector3(0, 0.01f, 0));
        ring.Scale = new Vector3(1, 0.05f, 1);

        var wall = new Color(0.78f, 0.68f, 0.48f);
        var trim = new Color(0.55f, 0.45f, 0.3f);
        float gap = Tuning.DoorWidth;
        float nsLen = (W + 2 * T - gap) / 2, ewLen = (D - gap) / 2;
        foreach (float s in new[] { -1f, 1f })
        {
            foreach (float side in new[] { -1f, 1f })
            {
                // north/south walls (span the corners), split around a centred door
                var nsPos = new Vector3(side * (gap / 2 + nsLen / 2), H / 2, s * (D / 2 + T / 2));
                Shapes.Box(_world, new Vector3(nsLen, H, T), wall, nsPos);
                Shapes.Box(_world, new Vector3(nsLen, 0.2f, T + 0.1f), trim, nsPos + new Vector3(0, H / 2, 0));
                // east/west walls
                var ewPos = new Vector3(s * (W / 2 + T / 2), H / 2, side * (gap / 2 + ewLen / 2));
                Shapes.Box(_world, new Vector3(T, H, ewLen), wall, ewPos);
                Shapes.Box(_world, new Vector3(T + 0.1f, 0.2f, ewLen), trim, ewPos + new Vector3(0, H / 2, 0));
                // door posts
                Shapes.Box(_world, new Vector3(0.3f, H + 0.4f, T + 0.2f), new Color(0.35f, 0.36f, 0.4f),
                    new Vector3(side * gap / 2, (H + 0.4f) / 2, s * (D / 2 + T / 2)));
                Shapes.Box(_world, new Vector3(T + 0.2f, H + 0.4f, 0.3f), new Color(0.35f, 0.36f, 0.4f),
                    new Vector3(s * (W / 2 + T / 2), (H + 0.4f) / 2, side * gap / 2));
            }
        }
        // Score screens set into the north wall's face, like the arcade's HUD-in-the-set.
        foreach (float side in new[] { -1f, 1f })
        {
            Shapes.Box(_world, new Vector3(4.5f, 1.6f, 0.2f), new Color(0.08f, 0.08f, 0.1f),
                new Vector3(side * 6.5f, 1.8f, -D / 2 + 0.05f));
            Shapes.Box(_world, new Vector3(4.0f, 0.25f, 0.22f), new Color(0.2f, 0.75f, 0.3f),
                new Vector3(side * 6.5f, 1.45f, -D / 2 + 0.06f));
        }
    }

    void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _title = new Label { Position = new Vector2(16, 10) };
        _title.AddThemeFontSizeOverride("font_size", 18);
        _title.AddThemeColorOverride("font_outline_color", Colors.Black);
        _title.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_title);
        _help = new Label { Position = new Vector2(16, 64) };
        _help.AddThemeFontSizeOverride("font_size", 13);
        _help.AddThemeColorOverride("font_outline_color", Colors.Black);
        _help.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_help);
        _status = new Label { AnchorTop = 1, AnchorBottom = 1, OffsetTop = -34, OffsetLeft = 16 };
        _status.AddThemeFontSizeOverride("font_size", 14);
        _status.AddThemeColorOverride("font_outline_color", Colors.Black);
        _status.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_status);
    }
}
