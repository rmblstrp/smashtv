using System.Collections.Generic;
using Godot;

namespace CameraDemo;

/// <summary>
/// Room-to-room test for preset 9 (painted walls, 3D floor). Four rooms in a 2×2 loop that uses
/// every exit direction once: A east → B, B south → C, C west → D, D north → A.
///
/// Transition, as the arcade does it (checked against footage, see README): when the room is clear
/// the exit opens; stepping into it starts a scripted walk in the exit direction
/// (ROBOATT.ASM:2222-2296) while the screen scrolls at a constant 3 px/frame to the next room
/// (WAVES.ASM:3195). The next room's start-of-room enemies are already standing there, frozen, and
/// its title banner colour-cycles over it. The banner clears 1.5 s after the scroll stops; enemies
/// stay frozen 120 frames more (ROBO.ASM:1280-1306), then the room starts.
///
/// Gameplay coordinates are world floor coordinates; <see cref="_origin"/> is the current room's
/// centre, and everything that used to assume (0, 0) is offset by it.
/// </summary>
public partial class Main
{
    enum Phase { Fighting, Cleared, Transition }

    enum Formation { None, Ring, Heavies, Armor }

    record Room(string Name, int Gx, int Gz, int ExitDoor, Color Tint, string Title, Formation Start);

    static readonly Room[] Rooms =
    {
        new("A", 0, 0, 3, new Color(1f, 1f, 1f), "BACK TO THE START!", Formation.Ring),
        new("B", 1, 0, 1, new Color(0.8f, 1f, 0.85f), "CIRCLE OF FRIENDS!", Formation.Ring),
        new("C", 1, 1, 2, new Color(1f, 0.82f, 0.78f), "HEAVY HITTERS!", Formation.Heavies),
        new("D", 0, 1, 0, new Color(0.82f, 0.86f, 1f), "ARMOR ARENA!", Formation.Armor),
    };

    static readonly string[] DoorName = { "north", "south", "west", "east" };

    static readonly Color[] BannerColors =
    {
        new(1f, 0.55f, 0.1f), new(1f, 0.2f, 0.75f), new(0.35f, 0.45f, 1f), new(1f, 1f, 1f),
        new(0.3f, 1f, 0.3f), new(1f, 0.95f, 0.2f), new(1f, 0.25f, 0.2f), new(0.7f, 0.3f, 1f), new(0.3f, 0.95f, 1f),
    };

    Phase _phase = Phase.Fighting;
    int _room, _nextRoom, _roomKills, _quota = Tuning.RoomKillQuota;
    float _spawnDelay, _scriptTime, _stretch = 1f, _bannerUntil = -1f;
    Vector2 _origin, _camFocus, _scriptDir;
    Vector3 _camBase;
    Node3D _exitMarker;
    Label3D _banner;
    readonly List<(MeshInstance3D back, MeshInstance3D front)> _roomQuads = new();
    readonly List<MeshInstance3D> _gapQuads = new();

    bool RoomsActive => Presets[_preset].Backdrop == BackdropMode.Walls;

    /// <summary>Enemies hold still during the scroll, while the banner is up, and for the freeze after it.</summary>
    bool EnemiesFrozen => RoomsActive && (_phase == Phase.Transition || _bannerUntil > 0 || _spawnDelay > 0);

    static Vector2 RoomCenter(int i) => new(Rooms[i].Gx * Tuning.RoomStepX, Rooms[i].Gz * Tuning.RoomStepZ);

    static float HalfExtent(Vector2 n) => n.X != 0 ? W / 2 : D / 2;

    /// <summary>How far the painting reaches from a room's centre toward direction n (screen v = -z).</summary>
    static float PaintingReach(Vector2 n) =>
        n.X > 0 ? BackdropPainter.MaxX : n.X < 0 ? -BackdropPainter.MinX : n.Y < 0 ? BackdropPainter.MaxV : -BackdropPainter.MinV;

    /// <summary>Centre (screen metres, relative to room i's floor centre) and size of the gap painting toward the next room.</summary>
    static (Vector2 center, Vector2 size, bool horizontal) GapRect(int i)
    {
        var n = DoorNormal[Rooms[i].ExitDoor];
        bool horizontal = n.X != 0;
        float step = horizontal ? Tuning.RoomStepX : Tuning.RoomStepZ;
        float near = PaintingReach(n), far = step - PaintingReach(-n);
        float mid = (near + far) / 2, len = far - near;
        var bc = BackdropPainter.CenterMeters;
        var bs = BackdropPainter.SizeMeters;
        return horizontal
            ? (new Vector2(n.X * mid, bc.Y), new Vector2(len, bs.Y), true)
            : (new Vector2(bc.X, -n.Y * mid), new Vector2(bs.X, len), false);   // screen v is -z
    }

    // ---------------------------------------------------------------- scene

    void BuildRoomFloors(StandardMaterial3D floorMat)
    {
        for (int i = 0; i < Rooms.Length; i++)
        {
            var c = RoomCenter(i);
            var mat = (StandardMaterial3D)floorMat.Duplicate();
            mat.AlbedoColor = Rooms[i].Tint;
            Shapes.Add(_arena, new PlaneMesh { Size = new Vector2(W, D) }, mat, new Vector3(c.X, 0, c.Y));
            var ring = Shapes.Add(_arena, new TorusMesh { InnerRadius = 2.6f, OuterRadius = 3.0f },
                Shapes.Mat(new Color(0.6f, 0.12f, 0.1f)), new Vector3(c.X, 0.01f, c.Y));
            ring.Scale = new Vector3(1, 0.05f, 1);
        }

        _exitMarker = new Node3D { Visible = false };
        _arena.AddChild(_exitMarker);
        var glow = Shapes.Mat(new Color(0.4f, 1f, 0.5f), unshaded: true);
        for (int k = 0; k < 3; k++)   // three chevrons pointing along -Z, turned toward the exit later
            foreach (float s in new[] { -1f, 1f })
                Shapes.Add(_exitMarker, new BoxMesh { Size = new Vector3(0.14f, 0.04f, 0.9f) }, glow,
                    new Vector3(s * 0.28f, 0.03f, -k * 0.8f + 0.3f), new Vector3(0, s * 35f, 0));
    }

    void PlaceRoomQuads(Basis basis, Vector3 up, Vector3 forward, bool visible)
    {
        if (_roomQuads.Count == 0)
        {
            var backTex = ((StandardMaterial3D)_bdWalls.MaterialOverride).AlbedoTexture;
            var frontTex = ((StandardMaterial3D)_bdFront.MaterialOverride).AlbedoTexture;
            foreach (var r in Rooms) _roomQuads.Add((PaintingQuad(backTex, r.Tint, BackdropPainter.SizeMeters),
                                                     PaintingQuad(frontTex, r.Tint, BackdropPainter.SizeMeters)));
            // One painting per gap shape, shared by both gaps of that shape.
            Texture2D hTex = null, vTex = null;
            for (int i = 0; i < Rooms.Length; i++)
            {
                var (_, size, horizontal) = GapRect(i);
                var tex = horizontal ? (hTex ??= GapTexture(true, size)) : (vTex ??= GapTexture(false, size));
                _gapQuads.Add(PaintingQuad(tex, Colors.White, size));
            }
        }
        var c = BackdropPainter.CenterMeters;
        for (int i = 0; i < Rooms.Length; i++)
        {
            var rc = RoomCenter(i);
            var room = new Vector3(rc.X, 0, rc.Y * _stretch);
            var (back, front) = _roomQuads[i];
            back.Transform = new Transform3D(basis, room + Vector3.Right * c.X + up * c.Y + forward * 40f);
            front.Transform = new Transform3D(basis, room + Vector3.Right * c.X + up * c.Y - forward * 40f);
            var (gc, _, _) = GapRect(i);
            _gapQuads[i].Transform = new Transform3D(basis, room + Vector3.Right * gc.X + up * gc.Y + forward * 40f);
            back.Visible = front.Visible = _gapQuads[i].Visible = visible;
        }
    }

    Texture2D GapTexture(bool horizontal, Vector2 size)
    {
        var painter = new ConnectorPainter
        {
            Horizontal = horizontal,
            SizeMeters = size,
            // The corridor lines up with the doors, which are centred on the floor, not the painting.
            CorridorOffset = horizontal ? -BackdropPainter.CenterMeters.Y : -BackdropPainter.CenterMeters.X,
        };
        var vp = new SubViewport
        {
            Size = painter.PixelSize,
            TransparentBg = true,
            Disable3D = true,
            Msaa2D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        vp.AddChild(painter);
        AddChild(vp);
        return vp.GetTexture();
    }

    MeshInstance3D PaintingQuad(Texture2D tex, Color tint, Vector2 size)
    {
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = tex,
                AlbedoColor = tint,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(quad);
        return quad;
    }

    void UpdateCameraPan() => _cam.Position = _camBase + new Vector3(_camFocus.X, 0, _camFocus.Y * _stretch);

    // ---------------------------------------------------------------- flow

    void UpdateRooms(float dt)
    {
        switch (_phase)
        {
            case Phase.Fighting:
                if (_bannerUntil < 0) _spawnDelay -= dt;
                if (_roomKills >= _quota) ClearRoom();
                break;

            case Phase.Cleared:
            {
                var n = DoorNormal[Rooms[_room].ExitDoor];
                var t = new Vector2(-n.Y, n.X);
                var local = _pos - _origin;
                _exitMarker.Scale = Vector3.One * (1f + 0.12f * Mathf.Sin(_time * 8f));
                if (local.Dot(n) >= HalfExtent(n) - Tuning.PlayerRadius - 0.05f &&
                    Mathf.Abs(local.Dot(t)) <= Tuning.DoorWidth / 2 - Tuning.PlayerRadius)
                    StartTransition(n);
                break;
            }

            case Phase.Transition:
            {
                _scriptTime -= dt;
                var target = RoomCenter(_nextRoom);
                bool wasScrolling = _camFocus != target;
                _camFocus = _camFocus.MoveToward(target, Tuning.ScrollSpeed * dt);
                if (wasScrolling && _camFocus == target)
                {
                    _bannerUntil = _time + Tuning.BannerAfterScroll;
                    Log($"scroll done, player at {Local(_nextRoom)}");
                }
                if (_scriptTime <= 0 && _camFocus == target) EnterRoom();
                break;
            }
        }
        UpdateBanner();
        UpdateCameraPan();
    }

    void UpdateBanner()
    {
        if (_banner == null || !_banner.Visible) return;
        _banner.Modulate = BannerColors[(int)(_time / Tuning.BannerColorStep) % BannerColors.Length];
        if (_bannerUntil > 0 && _time >= _bannerUntil)
        {
            _banner.Visible = false;
            _bannerUntil = -1;
            _spawnDelay = Tuning.RoomFreeze;
            Log($"banner cleared, enemies frozen {Tuning.RoomFreeze:0.00} s more");
        }
    }

    /// <summary>Clears the room the way the arcade does once the primary enemy runs out: no score for leftovers.</summary>
    void ClearRoom()
    {
        if (!RoomsActive || _phase != Phase.Fighting || EnemiesFrozen) return;
        for (int i = _enemies.Count - 1; i >= 0; i--) Kill(i, scored: false);
        foreach (var s in _shots) s.Node.QueueFree();
        _shots.Clear();
        _phase = Phase.Cleared;
        var n = DoorNormal[Rooms[_room].ExitDoor];
        var p = _origin + n * (HalfExtent(n) - 1.8f);
        _exitMarker.Position = new Vector3(p.X, 0, p.Y);
        _exitMarker.Rotation = new Vector3(0, Yaw(n), 0);
        _exitMarker.Visible = true;
        Log($"room {Rooms[_room].Name} clear, exit {DoorName[Rooms[_room].ExitDoor]} open");
    }

    void StartTransition(Vector2 n)
    {
        _phase = Phase.Transition;
        _scriptDir = n;
        _nextRoom = (_room + 1) % Rooms.Length;
        _scriptTime = n.X != 0 ? Tuning.WalkOutSide : n.Y > 0 ? Tuning.WalkOutDown : Tuning.WalkOutUp;
        _exitMarker.Visible = false;
        PlaceFormation(_nextRoom, entryNormal: -n);
        ShowBanner(_nextRoom);
        float scroll = _camFocus.DistanceTo(RoomCenter(_nextRoom)) / Tuning.ScrollSpeed;
        Log($"exit {Rooms[_room].Name} -> {Rooms[_nextRoom].Name}: walk {_scriptTime:0.00} s, scroll {scroll:0.00} s, " +
            $"{_enemies.Count} enemies placed");
    }

    void EnterRoom()
    {
        _room = _nextRoom;
        _origin = RoomCenter(_room);
        _phase = Phase.Fighting;
        _roomKills = 0;
        _spawnTimer = 0; _fatTimer = 2; _tankTimer = 4;
        Log($"enter {Rooms[_room].Name}, player at {Local(_room)}, control back");
    }

    /// <summary>
    /// Start-of-room enemies, standing frozen in the next room while it scrolls in. Each formation
    /// leaves the path from the entry door to the centre clear (entryNormal points from the room's
    /// centre toward the door the player comes through).
    /// </summary>
    void PlaceFormation(int room, Vector2 entryNormal)
    {
        var c = RoomCenter(room);
        var face = entryNormal;   // look toward the door the player arrives through
        switch (Rooms[room].Start)
        {
            case Formation.Ring:   // 12 grunts round the floor emblem, gaps at all four door lines
                for (int k = 0; k < 12; k++)
                {
                    float a = Mathf.DegToRad(15 + 30 * k);
                    PlaceEnemy(Kind.Grunt, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.6f, face);
                }
                break;
            case Formation.Heavies:   // two fat men flanking, a line of grunts behind the centre
                PlaceEnemy(Kind.Fat, c + new Vector2(-4.5f, -1f), face);
                PlaceEnemy(Kind.Fat, c + new Vector2(4.5f, -1f), face);
                foreach (float x in new[] { -3f, -1.8f, -0.6f, 0.6f, 1.8f, 3f })
                    PlaceEnemy(Kind.Grunt, c + new Vector2(x, 3f), face);
                break;
            case Formation.Armor:   // a tank north of the centre, a V of grunts south of it
                PlaceEnemy(Kind.Tank, c + new Vector2(0, -2.5f), face);
                foreach (var (x, z) in new[] { (1f, 2.2f), (2.2f, 3.2f), (3.4f, 4.2f) })
                {
                    PlaceEnemy(Kind.Grunt, c + new Vector2(-x, z), face);
                    PlaceEnemy(Kind.Grunt, c + new Vector2(x, z), face);
                }
                break;
        }
    }

    void ShowBanner(int room)
    {
        if (_banner == null)
        {
            _banner = new Label3D
            {
                FontSize = 64,
                PixelSize = 0.0085f,
                OutlineSize = 14,
                OutlineModulate = Colors.Black,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Shaded = false,
            };
            AddChild(_banner);   // outside the depth-stretched world
        }
        // Placed in the new room, near the top of its floor, so it scrolls in with the room.
        var c = RoomCenter(room);
        _banner.Position = new Vector3(c.X, 1f, (c.Y - 4f) * _stretch);
        _banner.Text = Rooms[room].Title;
        _banner.Visible = true;
        _bannerUntil = float.MaxValue;   // up until the scroll ends; then BannerAfterScroll more
    }

    void ResetRooms()
    {
        foreach (var a in _enemies) a.Node.QueueFree();
        _enemies.Clear();
        foreach (var s in _shots) s.Node.QueueFree();
        _shots.Clear();
        _room = 0; _origin = Vector2.Zero; _camFocus = Vector2.Zero;
        _phase = Phase.Fighting; _roomKills = 0; _spawnDelay = 0; _bannerUntil = -1;
        _pos = new Vector2(0, 3);
        if (IsInstanceValid(_exitMarker)) _exitMarker.Visible = false;
        if (_banner != null) _banner.Visible = false;
    }

    string RoomStatus() => _phase switch
    {
        Phase.Fighting when EnemiesFrozen => $"room {Rooms[_room].Name}   get ready",
        Phase.Fighting => $"room {Rooms[_room].Name}   kills {_roomKills}/{_quota}   (K: clear room)",
        Phase.Cleared => $"room {Rooms[_room].Name} CLEAR — exit {DoorName[Rooms[_room].ExitDoor]}",
        _ => $"room {Rooms[_room].Name} → {Rooms[_nextRoom].Name}",
    };

    /// <summary>Autopilot once the room is clear: head for a point just past the exit door.</summary>
    Vector2 AutopilotToExit()
    {
        var n = DoorNormal[Rooms[_room].ExitDoor];
        var target = _origin + n * (HalfExtent(n) + 1f);
        return (target - _pos).Normalized();
    }

    string Local(int room)
    {
        var l = _pos - RoomCenter(room);
        return $"({l.X:0.0}, {l.Y:0.0}) m from room {Rooms[room].Name} centre";
    }

    void Log(string msg) => GD.Print($"ROOMS t={_time:0.00} {msg}");
}
