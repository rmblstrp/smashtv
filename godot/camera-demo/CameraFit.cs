using Godot;

namespace CameraDemo;

/// <summary>
/// Wall geometry. Leans are degrees from vertical, tops tipped AWAY from the room, pivoting on the
/// floor boundary so collision is unchanged. Upright = (0, 0, full height).
/// </summary>
public record Walls(float NorthLean, float SideLean, float SouthHeight, float Thickness, float SouthThickness);

/// <summary>None: 3D walls. Walls: painted walls, 3D floor shows through. Room: walls and floor painted.</summary>
public enum BackdropMode { None, Walls, Room }

public record CameraPreset(string Name, bool Ortho, float Pitch, float Fov, bool StretchFloor, string Note,
                           Walls Walls = null, float ActorTilt = 0, BackdropMode Backdrop = BackdropMode.None);

/// <summary>
/// Places a fixed camera so the whole arena (walls included) fills the screen, like the
/// arcade's single-screen view. Pitch is degrees below horizontal (90 = straight down).
/// </summary>
public static class CameraFit
{
    public static string Apply(Camera3D cam, CameraPreset p, float aspect, Vector3[] corners)
    {
        float pr = Mathf.DegToRad(p.Pitch);
        var forward = new Vector3(0, -Mathf.Sin(pr), -Mathf.Cos(pr));
        var up = new Vector3(0, Mathf.Cos(pr), -Mathf.Sin(pr));
        var right = Vector3.Right;

        float minU = float.MaxValue, maxU = float.MinValue, minR = float.MaxValue, maxR = float.MinValue;
        foreach (var c in corners)
        {
            float u = c.Dot(up), r = c.Dot(right);
            minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
            minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
        }
        var center = up * (minU + maxU) / 2 + right * (minR + maxR) / 2;
        const float margin = 1.03f;

        cam.Rotation = new Vector3(-pr, 0, 0);
        cam.KeepAspect = Camera3D.KeepAspectEnum.Height;
        cam.Near = 0.1f;
        cam.Far = 500f;

        if (p.Ortho)
        {
            cam.Projection = Camera3D.ProjectionType.Orthogonal;
            cam.Size = Mathf.Max(maxU - minU, (maxR - minR) / aspect) * margin;
            cam.Position = center - forward * 80f;
            return $"orthographic, pitch {p.Pitch:0}°, view height {cam.Size:0.0} m";
        }

        cam.Projection = Camera3D.ProjectionType.Perspective;
        cam.Fov = p.Fov;
        float t = Mathf.Tan(Mathf.DegToRad(p.Fov) / 2) / margin;
        float lo = 1f, hi = 1000f;
        for (int i = 0; i < 48; i++)
        {
            float mid = (lo + hi) / 2;
            if (Fits(center - forward * mid, forward, up, right, corners, t, aspect)) hi = mid; else lo = mid;
        }
        cam.Position = center - forward * hi;
        return $"perspective, pitch {p.Pitch:0}°, vertical FOV {p.Fov:0}°, distance {hi:0.0} m";
    }

    static bool Fits(Vector3 pos, Vector3 f, Vector3 u, Vector3 r, Vector3[] corners, float t, float aspect)
    {
        foreach (var c in corners)
        {
            var v = c - pos;
            float z = v.Dot(f);
            if (z <= 0.2f) return false;
            if (Mathf.Abs(v.Dot(u)) / z > t || Mathf.Abs(v.Dot(r)) / z > t * aspect) return false;
        }
        return true;
    }
}
