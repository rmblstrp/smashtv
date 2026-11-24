using Godot;

namespace CameraDemo;

/// <summary>Placeholder 3D models built from primitives. Every model faces -Z (Godot forward).</summary>
public static class Shapes
{
    public static StandardMaterial3D Mat(Color c, bool unshaded = false) => new()
    {
        AlbedoColor = c,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        Transparency = c.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public static MeshInstance3D Add(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (rotDeg is { } r) mi.RotationDegrees = r;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Box(Node3D p, Vector3 size, Color c, Vector3 pos) =>
        Add(p, new BoxMesh { Size = size }, Mat(c), pos);

    /// <summary>Flat disc on the floor marking the collision circle (the "feet" footprint).</summary>
    public static MeshInstance3D Footprint(Node3D p, float radius) =>
        Add(p, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f },
            Mat(new Color(1f, 0.9f, 0.1f, 0.55f), unshaded: true), new Vector3(0, 0.02f, 0));

    /// <summary>Legs and torso are separate nodes so the torso can aim while the legs run.</summary>
    public static (Node3D root, Node3D legs, Node3D torso) Player(Node3D parent)
    {
        var root = new Node3D(); parent.AddChild(root);
        var legs = new Node3D(); root.AddChild(legs);
        var torso = new Node3D { Position = new Vector3(0, 0.85f, 0) }; root.AddChild(torso);
        var pants = Mat(new Color(0.15f, 0.2f, 0.45f));
        Add(legs, new CapsuleMesh { Radius = 0.11f, Height = 0.85f }, pants, new Vector3(-0.14f, 0.43f, 0));
        Add(legs, new CapsuleMesh { Radius = 0.11f, Height = 0.85f }, pants, new Vector3(0.14f, 0.43f, 0));
        Box(torso, new Vector3(0.55f, 0.6f, 0.32f), new Color(0.95f, 0.45f, 0.15f), new Vector3(0, 0.3f, 0));
        Add(torso, new SphereMesh { Radius = 0.2f, Height = 0.4f }, Mat(new Color(0.2f, 0.45f, 0.95f)), new Vector3(0, 0.8f, 0));
        Box(torso, new Vector3(0.16f, 0.12f, 0.08f), new Color(0.1f, 0.1f, 0.1f), new Vector3(0, 0.82f, -0.18f)); // visor
        Box(torso, new Vector3(0.12f, 0.12f, 0.75f), new Color(0.75f, 0.75f, 0.8f), new Vector3(0.22f, 0.32f, -0.4f)); // gun
        return (root, legs, torso);
    }

    public static Node3D Grunt(Node3D parent)
    {
        var root = new Node3D(); parent.AddChild(root);
        var skin = Mat(new Color(0.55f, 0.12f, 0.15f));
        Add(root, new CapsuleMesh { Radius = 0.3f, Height = 1.45f }, skin, new Vector3(0, 0.72f, 0));
        Add(root, new SphereMesh { Radius = 0.19f, Height = 0.38f }, Mat(new Color(0.85f, 0.65f, 0.5f)), new Vector3(0, 1.55f, 0));
        Add(root, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 0.9f },
            Mat(new Color(0.45f, 0.3f, 0.15f)), new Vector3(0.32f, 1.0f, -0.25f), new Vector3(-60, 0, 0)); // club
        return root;
    }

    public static Node3D Fat(Node3D parent)
    {
        var root = new Node3D(); parent.AddChild(root);
        var green = Mat(new Color(0.25f, 0.55f, 0.2f));
        Add(root, new SphereMesh { Radius = 0.8f, Height = 1.5f }, green, new Vector3(0, 1.2f, 0));
        Add(root, new SphereMesh { Radius = 0.22f, Height = 0.44f }, Mat(new Color(0.85f, 0.65f, 0.5f)), new Vector3(0, 2.1f, -0.1f));
        Add(root, new CapsuleMesh { Radius = 0.15f, Height = 0.6f }, green, new Vector3(-0.35f, 0.3f, 0));
        Add(root, new CapsuleMesh { Radius = 0.15f, Height = 0.6f }, green, new Vector3(0.35f, 0.3f, 0));
        Box(root, new Vector3(0.5f, 0.3f, 0.2f), new Color(0.9f, 0.75f, 0.1f), new Vector3(0, 1.25f, -0.72f)); // bomb belt
        return root;
    }

    public static (Node3D root, Node3D turret) Tank(Node3D parent)
    {
        var root = new Node3D(); parent.AddChild(root);
        var grey = new Color(0.45f, 0.47f, 0.5f);
        Box(root, new Vector3(1.4f, 0.6f, 2.0f), grey, new Vector3(0, 0.3f, 0));
        Box(root, new Vector3(0.3f, 0.5f, 2.1f), new Color(0.15f, 0.15f, 0.15f), new Vector3(-0.78f, 0.25f, 0));
        Box(root, new Vector3(0.3f, 0.5f, 2.1f), new Color(0.15f, 0.15f, 0.15f), new Vector3(0.78f, 0.25f, 0));
        var turret = new Node3D { Position = new Vector3(0, 0.6f, 0) }; root.AddChild(turret);
        Add(turret, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.5f, Height = 0.4f }, Mat(grey.Lightened(0.2f)), new Vector3(0, 0.2f, 0));
        Add(turret, new SphereMesh { Radius = 0.2f, Height = 0.4f }, Mat(new Color(0.85f, 0.65f, 0.5f)), new Vector3(0, 0.6f, 0.1f)); // driver
        Box(turret, new Vector3(0.14f, 0.14f, 0.9f), new Color(0.2f, 0.2f, 0.2f), new Vector3(0, 0.25f, -0.7f));
        return (root, turret);
    }

    public static Node3D Projectile(Node3D parent, Color c, float radius, float height)
    {
        var n = new Node3D(); parent.AddChild(n);
        Add(n, new SphereMesh { Radius = radius, Height = radius * 2 }, Mat(c, unshaded: true), new Vector3(0, height, 0));
        return n;
    }

    public static ImageTexture GridTexture()
    {
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        img.Fill(new Color(0.55f, 0.47f, 0.27f));
        var line = new Color(0.36f, 0.3f, 0.16f);
        for (int i = 0; i < s; i++)
            for (int w = 0; w < 3; w++)
            {
                img.SetPixel(i, w, line);
                img.SetPixel(w, i, line);
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
