using Godot;

namespace CameraDemo;

/// <summary>
/// Paints the room as a flat 2D picture, the way an artist would draw it for a fixed camera.
/// Coordinates are "screen metres": x right, v up, origin at the floor centre. With the 60°
/// orthographic camera and depth stretch, the 3D floor maps onto exactly the rectangle
/// x ∈ [-W/2, W/2], v ∈ [-D/2, D/2], so 3D characters stand on the painted floor without any
/// fitting. Proportions follow the arcade frame: back wall ≈ 22% of floor depth, side-wall faces
/// ≈ 6.7% of floor width, a thin front lip. The art itself is original.
///
/// Rendered once into a transparent SubViewport and shown on a camera-facing quad.
/// <see cref="PaintFloor"/> off leaves the floor transparent so a real 3D floor shows through;
/// <see cref="FrontOnly"/> paints just the front lip, for an overlay that draws over characters.
/// </summary>
public partial class BackdropPainter : Node2D
{
    public bool PaintFloor, FrontOnly;

    public const float Ppm = 64f;   // pixels per metre in the painting
    const float FW = Tuning.ArenaWidth / 2, FD = Tuning.ArenaDepth / 2, Door = Tuning.DoorWidth / 2;
    public const float Side = 1.35f, North = 2.7f, Cap = 0.45f, Lip = 0.55f, Frame = 0.45f;

    public static float MinX => -(FW + Side + Frame);
    public static float MaxX => FW + Side + Frame;
    public static float MinV => -(FD + Lip + 0.35f);
    public static float MaxV => FD + North + Cap;
    public static Vector2 SizeMeters => new(MaxX - MinX, MaxV - MinV);
    public static Vector2 CenterMeters => new((MinX + MaxX) / 2, (MinV + MaxV) / 2);
    public static Vector2I PixelSize => new(Mathf.CeilToInt(SizeMeters.X * Ppm), Mathf.CeilToInt(SizeMeters.Y * Ppm));

    static readonly Color FrameCol = new(0.11f, 0.12f, 0.15f);
    static readonly Color Hazard = new(0.95f, 0.72f, 0.1f);
    static readonly Color Neon = new(0.25f, 0.9f, 1f);

    static Vector2 P(float x, float v) => new((x - MinX) * Ppm, (MaxV - v) * Ppm);

    static Vector2[] Map((float x, float v)[] pts)
    {
        var a = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) a[i] = P(pts[i].x, pts[i].v);
        return a;
    }

    void Poly(Color c, params (float, float)[] pts) => DrawColoredPolygon(Map(pts), c);
    void Grad(Color[] c, params (float, float)[] pts) => DrawPolygon(Map(pts), c);
    void Line(float x0, float v0, float x1, float v1, Color c, float w) => DrawLine(P(x0, v0), P(x1, v1), c, w * Ppm, true);

    public override void _Draw()
    {
        if (!FrontOnly)
        {
            OuterFrame();
            NorthWall();
            SideWall(-1);
            SideWall(1);
            if (PaintFloor) Floor();
        }
        FrontLip();
    }

    void OuterFrame()
    {
        Poly(FrameCol, (MinX, FD + North), (MaxX, FD + North), (MaxX, MaxV), (MinX, MaxV));
        Poly(FrameCol, (MinX, MinV), (-FW - Side, MinV), (-FW - Side, MaxV), (MinX, MaxV));
        Poly(FrameCol, (FW + Side, MinV), (MaxX, MinV), (MaxX, MaxV), (FW + Side, MaxV));
        // top cap with a neon strip
        Poly(new Color(0.5f, 0.53f, 0.6f), (-FW - Side, FD + North), (FW + Side, FD + North), (FW + Side + 0.2f, FD + North + 0.25f), (-FW - Side - 0.2f, FD + North + 0.25f));
        Line(-FW - Side, FD + North + 0.32f, FW + Side, FD + North + 0.32f, Neon, 0.06f);
    }

    /// <summary>Back wall drawn as a trapezoid wider at the top: it reads as leaned back.</summary>
    void NorthWall()
    {
        var top = new Color(0.46f, 0.49f, 0.57f);
        var bottom = new Color(0.27f, 0.29f, 0.35f);
        Grad(new[] { bottom, bottom, top, top }, (-FW, FD), (FW, FD), (FW + Side, FD + North), (-FW - Side, FD + North));

        var seam = new Color(0.18f, 0.19f, 0.23f);
        for (float v = FD + 0.9f; v < FD + North; v += 0.9f)
        {
            float hw = FW + Side * (v - FD) / North;
            Line(-hw, v, hw, v, seam, 0.04f);
        }
        foreach (float x in new[] { -8.5f, -5.5f, -2.5f, 2.5f, 5.5f, 8.5f })
            Line(x, FD, x * (FW + Side) / FW, FD + North, seam, 0.04f);   // seams radiate like a leaned plane

        foreach (float s in new[] { -1f, 1f })
        {
            // a soft light pool on the wall behind each screen
            for (int i = 6; i >= 1; i--)
                DrawCircle(P(s * 6.5f, FD + 1.4f), i * 0.45f * Ppm, new Color(0.6f, 0.85f, 1f, 0.035f));
            Poly(new Color(0.05f, 0.06f, 0.08f), (s * 4.25f, FD + 0.55f), (s * 8.75f, FD + 0.55f), (s * 8.75f, FD + 2.05f), (s * 4.25f, FD + 2.05f));
            Line(s * 4.25f, FD + 2.05f, s * 8.75f, FD + 2.05f, Neon, 0.05f);
            Line(s * 4.25f, FD + 0.55f, s * 8.75f, FD + 0.55f, Neon, 0.05f);
            Poly(new Color(0.2f, 0.75f, 0.3f), (s * 4.6f, FD + 0.8f), (s * 8.4f, FD + 0.8f), (s * 8.4f, FD + 1.0f), (s * 4.6f, FD + 1.0f));
        }

        // doorway: dark opening, hazard posts and lintel
        float h = North * 0.82f;
        Grad(new[] { new Color(0.08f, 0.08f, 0.09f), new Color(0.08f, 0.08f, 0.09f), new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f) },
            (-Door, FD), (Door, FD), (Door * 1.08f, FD + h), (-Door * 1.08f, FD + h));
        foreach (float s in new[] { -1f, 1f })
        {
            Poly(Hazard, (s * Door, FD), (s * (Door + 0.3f), FD), (s * (Door * 1.08f + 0.32f), FD + h), (s * Door * 1.08f, FD + h));
            for (float v = FD + 0.25f; v < FD + h; v += 0.5f)
                Line(s * Door, v, s * (Door + 0.32f), v + 0.18f, new Color(0.1f, 0.1f, 0.1f), 0.09f);
        }
        Poly(Hazard, (-Door * 1.08f - 0.32f, FD + h), (Door * 1.08f + 0.32f, FD + h), (Door * 1.08f + 0.34f, FD + h + 0.22f), (-Door * 1.08f - 0.34f, FD + h + 0.22f));
    }

    /// <summary>Side walls: strips whose corners meet the back wall and the lip on diagonals.</summary>
    void SideWall(float s)
    {
        var inner = new Color(0.33f, 0.35f, 0.41f);
        var outer = new Color(0.41f, 0.44f, 0.51f);
        Grad(new[] { inner, inner, outer, outer },
            (s * FW, -FD), (s * FW, FD), (s * (FW + Side), FD + North), (s * (FW + Side), -FD - Lip));
        var seam = new Color(0.2f, 0.21f, 0.25f);
        for (float v = -FD + 1.5f; v < FD; v += 1.5f)
            if (Mathf.Abs(v) > Door + 0.2f) Line(s * FW, v, s * (FW + Side), v, seam, 0.04f);
        Line(s * FW, -FD, s * FW, FD, new Color(0.55f, 0.58f, 0.65f), 0.05f);   // lit bottom edge

        var dark = new Color(0.07f, 0.07f, 0.08f);
        var black = new Color(0.01f, 0.01f, 0.01f);
        Grad(new[] { dark, dark, black, black }, (s * FW, -Door), (s * FW, Door), (s * (FW + Side), Door * 1.05f), (s * (FW + Side), -Door * 1.05f));
        foreach (float e in new[] { -1f, 1f })
            Line(s * FW, e * Door, s * (FW + Side), e * Door * 1.05f, Hazard, 0.25f);
    }

    void Floor()
    {
        // Colours matched by sampling the lit 3D floor of preset 9 (≈ #9A844C on screen).
        Poly(new Color(0.64f, 0.55f, 0.315f), (-FW, -FD), (FW, -FD), (FW, FD), (-FW, FD));
        var line = new Color(0.44f, 0.36f, 0.19f);
        for (float x = -FW; x <= FW + 0.01f; x += 1f) Line(x, -FD, x, FD, line, 0.05f);
        for (float v = -FD; v <= FD + 0.01f; v += 1f) Line(-FW, v, FW, v, line, 0.05f);
        DrawArc(P(0, 0), 2.8f * Ppm, 0, Mathf.Tau, 160, new Color(0.68f, 0.14f, 0.11f), 0.4f * Ppm, true);
        // painted contact darkening along the walls (ambient occlusion an artist would add)
        var clear = new Color(0, 0, 0, 0);
        var shade = new Color(0, 0, 0, 0.35f);
        Grad(new[] { clear, clear, shade, shade }, (-FW, FD - 0.9f), (FW, FD - 0.9f), (FW, FD), (-FW, FD));
        foreach (float s in new[] { -1f, 1f })
            Grad(new[] { shade, clear, clear, shade }, (s * FW, -FD), (s * (FW - 0.6f), -FD), (s * (FW - 0.6f), FD), (s * FW, FD));
    }

    /// <summary>The front wall seen from above: a thin lip. In the front layer it covers characters
    /// that walk past it, except in the doorway gap.</summary>
    void FrontLip()
    {
        var top = new Color(0.58f, 0.61f, 0.68f);
        var face = new Color(0.36f, 0.38f, 0.44f);
        foreach (float s in new[] { -1f, 1f })
        {
            Grad(new[] { top, top, face, face },
                (s * Door, -FD), (s * FW, -FD), (s * (FW + Side), -FD - Lip), (s * Door * 1.05f, -FD - Lip));
            Poly(FrameCol, (s * Door * 1.05f, -FD - Lip), (s * (FW + Side), -FD - Lip), (s * (FW + Side), MinV), (s * Door * 1.05f, MinV));
            Poly(FrameCol, (s * (FW + Side), MinV), (s * MaxX, MinV), (s * MaxX, -FD - Lip), (s * (FW + Side), -FD - Lip));
            Line(s * Door, -FD, s * FW, -FD, new Color(0.8f, 0.83f, 0.9f), 0.05f);
            Poly(Hazard, (s * Door, -FD), (s * (Door + 0.3f), -FD), (s * (Door * 1.05f + 0.3f), -FD - Lip), (s * Door * 1.05f, -FD - Lip));
        }
        if (!FrontOnly)
            Poly(new Color(0.03f, 0.03f, 0.04f), (-Door, -FD), (Door, -FD), (Door * 1.05f, MinV), (-Door * 1.05f, MinV));
    }
}
