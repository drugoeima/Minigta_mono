using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>
/// A home you can buy: a building at the west end of a road plus a parking lot in front of it.
/// Cars you park in the lot become yours (they stay there, they are never recycled into traffic).
/// </summary>
public class Property
{
    public string Name = "";
    public int Price;
    public bool Owned;

    public float Cx, Cz, HalfX, HalfZ;      // parking lot: centre and half size
    public Vector3 Door;                    // where you stand to buy / use the home
    public Vector3[] SlotPos;
    public float[] SlotYaw;
    public Building Bld;

    const float Bay = 3.4f;                 // width of one parking bay
    static readonly Color Concrete = U.C(88, 88, 96);

    public bool InLot(Vector3 p) => MathF.Abs(p.X - Cx) < HalfX && MathF.Abs(p.Z - Cz) < HalfZ;

    /// <param name="cz">z of the road this home sits at the end of (the lot touches the road end at x = -Road/2)</param>
    public static Property Make(string name, int price, int slots, float cz, float height, Color wall, float seed)
    {
        var p = new Property { Name = name, Price = price, Cz = cz };

        int bays = (slots + 1) / 2;
        p.HalfX = MathF.Max(12f, bays * Bay * 0.5f + 3f);
        p.HalfZ = 14f;
        p.Cx = -World.Road / 2f - p.HalfX;

        p.SlotPos = new Vector3[slots];
        p.SlotYaw = new float[slots];
        float x0 = p.Cx - (bays - 1) * Bay * 0.5f;
        for (int i = 0; i < slots; i++)
        {
            int row = i % 2, k = i / 2;
            float x = x0 + k * Bay;
            p.SlotPos[i] = new Vector3(x, 0f, cz + (row == 0 ? -8f : 8f));
            p.SlotYaw[i] = row == 0 ? MathF.PI : 0f;       // nose towards the wall of the lot
        }

        float west = p.Cx - p.HalfX;
        p.Bld = new Building
        {
            MinX = west - 24f, MaxX = west,
            MinZ = cz - 15f, MaxZ = cz + 15f,
            H = height, Col = wall, Seed = seed, Tiered = height > 38f, Name = name
        };
        p.Door = new Vector3(west + 2.8f, 0f, cz);
        return p;
    }

    /// <summary>Index of the free bay nearest to 'from', or -1 if the lot is full.</summary>
    public int FreeSlot(List<Car> cars, Car self, Vector3 from)
    {
        int best = -1;
        float bd = float.MaxValue;
        for (int i = 0; i < SlotPos.Length; i++)
        {
            bool taken = false;
            foreach (var c in cars)
                if (c != self && U.Dist2D(c.Pos, SlotPos[i]) < 2.8f) { taken = true; break; }
            if (taken) continue;
            float d = U.Dist2D(from, SlotPos[i]);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>How many bays hold one of your cars.</summary>
    public int Used(List<Car> cars)
    {
        int n = 0;
        for (int i = 0; i < SlotPos.Length; i++)
            foreach (var c in cars)
                if (c.Owned && U.Dist2D(c.Pos, SlotPos[i]) < 2.8f) { n++; break; }
        return n;
    }

    public void Draw(Vector3 cam, float time)
    {
        float dx = Cx - cam.X, dz = Cz - cam.Z;
        if (dx * dx + dz * dz > 280f * 280f) return;

        Color st = Owned ? U.C(70, 230, 120) : U.C(255, 70, 90);
        Color line = U.C(225, 225, 215);

        // beacon (visible from far away) + marker on the ground at the door
        Gfx.Flat(new Vector3(Door.X, 25f, Door.Z), new Vector3(0.5f, 50f, 0.5f), 0f, st);
        float pu = 1f + 0.12f * MathF.Sin(time * 3f);
        Gfx.Flat(new Vector3(Door.X, 0.12f, Door.Z), new Vector3(3.4f * pu, 0.06f, 3.4f * pu), 0f, st);

        // lot
        Gfx.Box(new Vector3(Cx, -0.2f, Cz), new Vector3(2f * HalfX, 0.5f, 2f * HalfZ), 0f, Concrete, Gfx.M.Walk);

        // bays: pad + side lines
        Color pad = Owned ? U.C(36, 92, 60) : U.C(96, 52, 56);
        for (int i = 0; i < SlotPos.Length; i++)
        {
            Vector3 s = SlotPos[i];
            Gfx.Flat(new Vector3(s.X, 0.1f, s.Z), new Vector3(Bay - 0.5f, 0.03f, 5.0f), 0f, pad);
            Gfx.Flat(new Vector3(s.X - Bay * 0.5f, 0.12f, s.Z), new Vector3(0.14f, 0.03f, 5.4f), 0f, line);
            Gfx.Flat(new Vector3(s.X + Bay * 0.5f, 0.12f, s.Z), new Vector3(0.14f, 0.03f, 5.4f), 0f, line);
        }

        // lot border + corner lamps
        Gfx.Flat(new Vector3(Cx, 0.12f, Cz - HalfZ + 0.2f), new Vector3(2f * HalfX, 0.03f, 0.3f), 0f, st);
        Gfx.Flat(new Vector3(Cx, 0.12f, Cz + HalfZ - 0.2f), new Vector3(2f * HalfX, 0.03f, 0.3f), 0f, st);
        Color pole = U.C(40, 42, 48), head = U.C(255, 214, 150);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                float px = Cx + sx * (HalfX - 1.5f), pz = Cz + sz * (HalfZ - 1.5f);
                Gfx.Box(new Vector3(px, 3.5f, pz), new Vector3(0.2f, 7f, 0.2f), 0f, pole, Gfx.M.Metal);
                Gfx.Flat(new Vector3(px, 7.1f, pz), new Vector3(0.8f, 0.2f, 0.8f), 0f, head);
            }

        // lit entrance of the building
        float west = Bld.MaxX;
        Gfx.Flat(new Vector3(west + 0.05f, 1.6f, Cz), new Vector3(0.12f, 3.2f, 3.4f), 0f, U.C(255, 226, 170));
        Gfx.Flat(new Vector3(west + 0.05f, 3.5f, Cz), new Vector3(0.14f, 0.4f, 6f), 0f, st);

        // "for sale" sign next to the door
        if (!Owned)
        {
            Gfx.Box(new Vector3(Door.X, 1.0f, Door.Z + 3.2f), new Vector3(0.15f, 2f, 0.15f), 0f, pole, Gfx.M.Metal);
            Gfx.Flat(new Vector3(Door.X, 2.3f, Door.Z + 3.2f), new Vector3(0.2f, 1.0f, 1.8f), 0f, U.C(255, 60, 70));
        }
    }
}
