using Godot;

namespace CameraDemo;

/// <summary>
/// Paints the gap between two room paintings, as the arcade does (machinery panels between rooms
/// side by side, a wall band between rooms stacked vertically). Original art. Coordinates are
/// screen metres with the origin at the painting's centre; the corridor through the middle lines
/// up with the rooms' doors, so the player's scripted walk crosses it.
/// </summary>
public partial class ConnectorPainter : Node2D
{
    public bool Horizontal;          // true: gap between rooms side by side
    public Vector2 SizeMeters;       // (width, height)
    public float CorridorOffset;     // corridor centre: v for horizontal, x for vertical

    const float Ppm = BackdropPainter.Ppm;
    const float Door = Tuning.DoorWidth / 2;
    static readonly Color Hazard = new(0.95f, 0.72f, 0.1f);

    public Vector2I PixelSize => new(Mathf.CeilToInt(SizeMeters.X * Ppm), Mathf.CeilToInt(SizeMeters.Y * Ppm));

    Vector2 P(float x, float v) => new((x + SizeMeters.X / 2) * Ppm, (SizeMeters.Y / 2 - v) * Ppm);
    void Rect(float x0, float v0, float x1, float v1, Color c) => DrawRect(new Rect2(P(x0, v1), P(x1, v0) - P(x0, v1)), c);
    void Line(float x0, float v0, float x1, float v1, Color c, float w) => DrawLine(P(x0, v0), P(x1, v1), c, w * Ppm, true);

    public override void _Draw()
    {
        float hw = SizeMeters.X / 2, hh = SizeMeters.Y / 2;
        if (Horizontal) Machinery(hw, hh); else WallBand(hw, hh);
    }

    void Machinery(float hw, float hh)
    {
        Rect(-hw, -hh, hw, hh, new Color(0.08f, 0.09f, 0.11f));
        // vertical coolant pipes with joint collars
        for (float x = -hw + 0.45f; x < hw - 0.3f; x += 0.75f)
        {
            Rect(x - 0.16f, -hh, x + 0.16f, hh, new Color(0.12f, 0.42f, 0.47f));
            Rect(x - 0.05f, -hh, x + 0.02f, hh, new Color(0.3f, 0.72f, 0.76f));   // highlight
            for (float v = -hh + 0.8f; v < hh; v += 1.6f)
                Rect(x - 0.22f, v - 0.08f, x + 0.22f, v + 0.08f, new Color(0.2f, 0.22f, 0.26f));
        }
        // status lights
        for (float v = -hh + 1.4f; v < hh; v += 3.1f)
            DrawCircle(P(-hw + 0.25f, v), 0.09f * Ppm, new Color(1f, 0.3f, 0.2f));
        // frame strips where the gap meets the room paintings
        Rect(-hw, -hh, -hw + 0.18f, hh, new Color(0.22f, 0.24f, 0.28f));
        Rect(hw - 0.18f, -hh, hw, hh, new Color(0.22f, 0.24f, 0.28f));
        // corridor through the side doors
        float c = CorridorOffset;
        Rect(-hw, c - Door, hw, c + Door, new Color(0.2f, 0.19f, 0.18f));
        for (float x = -hw + 0.5f; x < hw; x += 1f) Line(x, c - Door, x, c + Door, new Color(0.15f, 0.14f, 0.13f), 0.04f);
        Line(-hw, c - Door, hw, c - Door, Hazard, 0.12f);
        Line(-hw, c + Door, hw, c + Door, Hazard, 0.12f);
    }

    void WallBand(float hw, float hh)
    {
        var top = new Color(0.58f, 0.6f, 0.64f);
        var bottom = new Color(0.42f, 0.44f, 0.48f);
        DrawPolygon(new[] { P(-hw, hh), P(hw, hh), P(hw, -hh), P(-hw, -hh) }, new[] { top, top, bottom, bottom });
        var seam = new Color(0.3f, 0.31f, 0.35f);
        for (float x = -hw + 2f; x < hw; x += 2f) Line(x, -hh, x, hh, seam, 0.04f);
        Line(-hw, 0, hw, 0, seam, 0.04f);
        Rect(-hw, hh - 0.2f, hw, hh, new Color(0.25f, 0.26f, 0.3f));
        Rect(-hw, -hh, hw, -hh + 0.2f, new Color(0.25f, 0.26f, 0.3f));
        // light pylons either side of the corridor
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 6f;
            Rect(x - 0.45f, -hh + 0.35f, x + 0.45f, hh - 0.35f, new Color(0.16f, 0.17f, 0.2f));
            for (int i = 4; i >= 1; i--)
                DrawCircle(P(x, 0), i * 0.22f * Ppm, new Color(1f, 0.75f, 0.3f, 0.12f));
            DrawCircle(P(x, 0), 0.16f * Ppm, new Color(1f, 0.85f, 0.45f));
        }
        // corridor through the top/bottom doors
        float c = CorridorOffset;
        Rect(c - Door, -hh, c + Door, hh, new Color(0.2f, 0.19f, 0.18f));
        for (float v = -hh + 0.5f; v < hh; v += 1f) Line(c - Door, v, c + Door, v, new Color(0.15f, 0.14f, 0.13f), 0.04f);
        Line(c - Door, -hh, c - Door, hh, Hazard, 0.12f);
        Line(c + Door, -hh, c + Door, hh, Hazard, 0.12f);
    }
}
