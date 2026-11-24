using System.Collections.Generic;
using Godot;

namespace CameraDemo;

/// <summary>
/// Room-to-room test for preset 9 (painted walls, 3D floor). Four rooms in a 2×2 loop that uses
/// every exit direction once: A east → B, B south → C, C west → D, D north → A.
///
/// Transition, as the arcade does it: when the room is clear the exit opens; stepping into it
/// starts a scripted walk in the exit direction (ROBOATT.ASM:2222-2296) while the screen scrolls
/// at a constant 3 px/frame to the next room (WAVES.ASM:3195). The walk outlasts the scroll and
/// leaves the player near the centre of the new room; enemies start after a 120-frame freeze.
///
/// Gameplay coordinates are world floor coordinates; <see cref="_origin"/> is the current room's
/// centre, and everything that used to assume (0, 0) is offset by it.
/// </summary>
public partial class Main
{
    enum Phase { Fighting, Cleared, Transition }

    record Room(string Name, int Gx, int Gz, int ExitDoor, Color Tint);

    static readonly Room[] Rooms =
    {
        new("A", 0, 0, 3, new Color(1f, 1f, 1f)),
        new("B", 1, 0, 1, new Color(0.8f, 1f, 0.85f)),
        new("C", 1, 1, 2, new Color(1f, 0.82f, 0.78f)),
        new("D", 0, 1, 0, new Color(0.82f, 0.86f, 1f)),
    };

    static readonly string[] DoorName = { "north", "south", "west", "east" };

    Phase _phase = Phase.Fighting;
    int _room, _nextRoom, _roomKills, _quota = Tuning.RoomKillQuota;
    float _spawnDelay, _scriptTime, _stretch = 1f;
    Vector2 _origin, _camFocus, _scriptDir;
    Vector3 _camBase;
    Node3D _exitMarker;
    readonly List<(MeshInstance3D back, MeshInstance3D front)> _roomQuads = new();

    bool RoomsActive => Presets[_preset].Backdrop == BackdropMode.Walls;

    static Vector2 RoomCenter(int i) => new(Rooms[i].Gx * Tuning.RoomStepX, Rooms[i].Gz * Tuning.RoomStepZ);

    static float HalfExtent(Vector2 n) => n.X != 0 ? W / 2 : D / 2;

    /// <summary>How far the painting reaches from a room's centre toward direction n (screen v = -z).</summary>
    static float PaintingReach(Vector2 n) =>
        n.X > 0 ? BackdropPainter.MaxX : n.X < 0 ? -BackdropPainter.MinX : n.Y < 0 ? BackdropPainter.MaxV : -BackdropPainter.MinV;

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

            // Corridor floor across the gap between this painting and the next one. Inside the
            // paintings the doorway is painted, so 3D floor there would cover the art.
            var n = DoorNormal[Rooms[i].ExitDoor];
            var next = RoomCenter((i + 1) % Rooms.Length);
            var a = c + n * PaintingReach(n);
            var b = next - n * PaintingReach(-n);
            var mid = (a + b) / 2;
            float len = a.DistanceTo(b);
            var size = n.X != 0 ? new Vector2(len, Tuning.DoorWidth) : new Vector2(Tuning.DoorWidth, len);
            Shapes.Add(_arena, new PlaneMesh { Size = size }, Shapes.Mat(new Color(0.2f, 0.19f, 0.18f)), new Vector3(mid.X, 0, mid.Y));
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
            foreach (var r in Rooms) _roomQuads.Add((PaintingQuad(backTex, r.Tint), PaintingQuad(frontTex, r.Tint)));
        }
        var c = BackdropPainter.CenterMeters;
        for (int i = 0; i < Rooms.Length; i++)
        {
            var rc = RoomCenter(i);
            var room = new Vector3(rc.X, 0, rc.Y * _stretch);
            var (back, front) = _roomQuads[i];
            back.Transform = new Transform3D(basis, room + Vector3.Right * c.X + up * c.Y + forward * 40f);
            front.Transform = new Transform3D(basis, room + Vector3.Right * c.X + up * c.Y - forward * 40f);
            back.Visible = front.Visible = visible;
        }
    }

    MeshInstance3D PaintingQuad(Texture2D tex, Color tint)
    {
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = BackdropPainter.SizeMeters },
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
                _spawnDelay -= dt;
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
                if (wasScrolling && _camFocus == target) Log($"scroll done, player at {Local(_nextRoom)}");
                if (_scriptTime <= 0 && _camFocus == target) EnterRoom();
                break;
            }
        }
        UpdateCameraPan();
    }

    /// <summary>Clears the room the way the arcade does once the primary enemy runs out: no score for leftovers.</summary>
    void ClearRoom()
    {
        if (!RoomsActive || _phase != Phase.Fighting) return;
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
        float scroll = _camFocus.DistanceTo(RoomCenter(_nextRoom)) / Tuning.ScrollSpeed;
        Log($"exit {Rooms[_room].Name} -> {Rooms[_nextRoom].Name}: walk {_scriptTime:0.00} s, scroll {scroll:0.00} s");
    }

    void EnterRoom()
    {
        _room = _nextRoom;
        _origin = RoomCenter(_room);
        _phase = Phase.Fighting;
        _roomKills = 0;
        _spawnDelay = Tuning.RoomFreeze;
        _spawnTimer = 0; _fatTimer = 2; _tankTimer = 4;
        Log($"enter {Rooms[_room].Name}, player at {Local(_room)}, control back");
    }

    void ResetRooms()
    {
        foreach (var a in _enemies) a.Node.QueueFree();
        _enemies.Clear();
        foreach (var s in _shots) s.Node.QueueFree();
        _shots.Clear();
        _room = 0; _origin = Vector2.Zero; _camFocus = Vector2.Zero;
        _phase = Phase.Fighting; _roomKills = 0; _spawnDelay = 0;
        _pos = new Vector2(0, 3);
        if (IsInstanceValid(_exitMarker)) _exitMarker.Visible = false;
    }

    string RoomStatus() => _phase switch
    {
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
