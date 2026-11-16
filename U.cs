using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>Small helpers: random, colors, angles, ray tests.</summary>
public static class U
{
    public static readonly Random R = new();
    public const float Deg = 57.29578f;

    public static float Rf(float a, float b) => a + (float)R.NextDouble() * (b - a);

    public static Color C(int r, int g, int b, int a = 255) => new Color((byte)r, (byte)g, (byte)b, (byte)a);

    public static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static float Dist2D(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Local offset (x = side, y = up, z = forward) -> world position for an object with the given yaw.</summary>
    public static Vector3 Local(Vector3 p, float yaw, float lx, float ly, float lz)
    {
        float s = MathF.Sin(yaw), c = MathF.Cos(yaw);
        return new Vector3(p.X + lx * c + lz * s, p.Y + ly, p.Z - lx * s + lz * c);
    }

    static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1)
    {
        if (MathF.Abs(d) < 1e-8f) return o >= mn && o <= mx;
        float a = (mn - o) / d, b = (mx - o) / d;
        if (a > b) { float s = a; a = b; b = s; }
        t0 = MathF.Max(t0, a);
        t1 = MathF.Min(t1, b);
        return t0 <= t1;
    }

    public static bool RayAabb(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx, out float t)
    {
        float t0 = 0f, t1 = float.MaxValue;
        t = 0f;
        if (!Slab(o.X, d.X, mn.X, mx.X, ref t0, ref t1)) return false;
        if (!Slab(o.Y, d.Y, mn.Y, mx.Y, ref t0, ref t1)) return false;
        if (!Slab(o.Z, d.Z, mn.Z, mx.Z, ref t0, ref t1)) return false;
        t = t0;
        return true;
    }

    public static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, out float t)
    {
        Vector3 m = o - c;
        float b = Vector3.Dot(m, d);
        float cc = Vector3.Dot(m, m) - r * r;
        t = 0f;
        if (cc > 0f && b > 0f) return false;
        float disc = b * b - cc;
        if (disc < 0f) return false;
        t = MathF.Max(0f, -b - MathF.Sqrt(disc));
        return true;
    }
}
