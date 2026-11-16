using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>
/// Walkable casino hall. It is built far away from the city (at C) and the player is teleported in when entering a casino.
/// Slot machines along the walls, roulette and blackjack tables in the middle, exit door in the south wall.
/// </summary>
public static class Interior
{
    public static bool Active;
    public static string Name = "";

    public static readonly Vector3 C = new(2400f, 0f, 2400f);
    public const float HX = 22f, HZ = 17f, H = 9f;      // half width, half depth, ceiling height

    struct Slot { public Vector3 Pos; public float Yaw; }
    struct Station { public Vector3 Pos; public float R; public int Game; public string Name; }
    struct Solid { public float X0, X1, Z0, Z1; }
    struct Person { public Vector3 Pos; public float Yaw; public HumanLook Look; }

    static readonly List<Slot> slots = new();
    static readonly List<Station> stations = new();
    static readonly List<Solid> solids = new();
    static readonly List<Vector3> roulette = new(), blackjack = new(), pillars = new();
    static readonly List<Person> people = new();
    static readonly List<Building> lights = new();      // 4 fake "buildings": SetNeon turns them into point lights

    static Color wallCol = U.C(55, 28, 85), neon = U.C(255, 200, 40);
    static Vector3 returnPos;
    static bool wasArmed;
    public static Vector3 ReturnPos => returnPos;      // where you stand again after leaving (used when saving inside a casino)
    public static bool WasArmed => wasArmed;

    static Vector3 W(float x, float y, float z) => new(C.X + x, y, C.Z + z);

    static void AddSolid(Vector3 c, float hx, float hz) =>
        solids.Add(new Solid { X0 = c.X - hx, X1 = c.X + hx, Z0 = c.Z - hz, Z1 = c.Z + hz });

    static void AddSlot(Vector3 p, float yaw, float hx, float hz)
    {
        slots.Add(new Slot { Pos = p, Yaw = yaw });
        AddSolid(p, hx, hz);
        stations.Add(new Station { Pos = p, R = 2.4f, Game = 0, Name = "slots" });
    }

    static HumanLook MakeLook(Random r, bool dealer)
    {
        Color[] shirts = { U.C(200, 70, 56), U.C(58, 120, 180), U.C(214, 178, 60), U.C(70, 150, 98), U.C(100, 80, 168), U.C(34, 36, 44) };
        Color[] skins = { U.C(240, 195, 160), U.C(200, 150, 110), U.C(140, 95, 65), U.C(95, 62, 42) };
        Color[] hairs = { U.C(22, 18, 16), U.C(70, 44, 26), U.C(176, 146, 84), U.C(104, 48, 28) };
        return new HumanLook
        {
            Shirt = dealer ? U.C(235, 235, 235) : shirts[r.Next(shirts.Length)],
            Pants = dealer ? U.C(20, 20, 24) : U.C(40, 46, 70),
            Skin = skins[r.Next(skins.Length)],
            Hair = hairs[r.Next(hairs.Length)],
            Shoes = U.C(20, 20, 22),
            LongSleeve = dealer,
            Variant = r.Next(1000)
        };
    }

    static Interior()
    {
        var rnd = new Random(11);

        // slot machines: 8 along the back wall (facing the room), 4 on each side wall
        for (int i = 0; i < 8; i++)
            AddSlot(W(-10.5f + i * 3f, 0f, HZ - 0.8f), MathF.PI, 0.65f, 0.55f);
        for (int i = 0; i < 4; i++)
        {
            float z = -6f + i * 4f;
            AddSlot(W(-HX + 0.8f, 0f, z), MathF.PI / 2f, 0.55f, 0.65f);
            AddSlot(W(HX - 0.8f, 0f, z), -MathF.PI / 2f, 0.55f, 0.65f);
        }

        // tables (the player stands on the south side, the dealer on the north side)
        foreach (float sx in new[] { -8f, 8f })
        {
            Vector3 r = W(sx, 0f, 3f);
            roulette.Add(r);
            AddSolid(r, 2.9f, 1.5f);
            stations.Add(new Station { Pos = r, R = 4.2f, Game = 1, Name = "roulette" });
            people.Add(new Person { Pos = new Vector3(r.X, 0f, r.Z + 2.0f), Yaw = MathF.PI, Look = MakeLook(rnd, true) });

            Vector3 b = W(sx, 0f, -8f);
            blackjack.Add(b);
            AddSolid(b, 2.3f, 1.2f);
            stations.Add(new Station { Pos = b, R = 3.8f, Game = 2, Name = "blackjack" });
            people.Add(new Person { Pos = new Vector3(b.X, 0f, b.Z + 1.9f), Yaw = MathF.PI, Look = MakeLook(rnd, true) });
        }

        foreach (float sx in new[] { -15f, 15f })
            foreach (float sz in new[] { -9f, 9f })
            {
                Vector3 p = W(sx, 0f, sz);
                pillars.Add(p);
                AddSolid(p, 0.7f, 0.7f);
            }

        // a few gamblers at the machines
        foreach (int i in new[] { 1, 4, 6 })
            people.Add(new Person { Pos = W(-10.5f + i * 3f, 0f, HZ - 2.3f), Yaw = 0f, Look = MakeLook(rnd, false) });
        people.Add(new Person { Pos = W(-HX + 2.3f, 0f, -2f), Yaw = -MathF.PI / 2f, Look = MakeLook(rnd, false) });
        people.Add(new Person { Pos = W(HX - 2.3f, 0f, 2f), Yaw = MathF.PI / 2f, Look = MakeLook(rnd, false) });

        // 4 ceiling lights (colour is set when entering)
        float[,] lp = { { -12f, 8f }, { 12f, 8f }, { -12f, -8f }, { 12f, -8f } };
        for (int i = 0; i < 4; i++)
            lights.Add(new Building { MinX = C.X + lp[i, 0], MaxX = C.X + lp[i, 0], MinZ = C.Z + lp[i, 1] + 5f, MaxZ = C.Z + lp[i, 1] + 5f });
    }

    // ------------------------------------------------------------------ enter / leave
    public static void Enter(Building b, Player pl)
    {
        Active = true;
        Name = b.Name;
        wallCol = U.C(b.Col.R, b.Col.G, b.Col.B);
        neon = b.Neon;
        returnPos = b.Door + new Vector3(0f, 0f, -2.5f);

        lights[0].Neon = b.Neon;
        lights[1].Neon = U.C(255, 200, 40);
        lights[2].Neon = U.C(40, 220, 255);
        lights[3].Neon = b.Neon;

        wasArmed = pl.Armed;
        pl.Armed = false;
        pl.Pos = W(0f, 0f, -HZ + 4.5f);
        pl.Yaw = 0f;
        pl.VelY = 0f;
    }

    public static void Leave(Player pl)
    {
        Active = false;
        Story.ClearInterior();
        pl.Pos = returnPos;
        pl.Yaw = MathF.PI;
        pl.Armed = wasArmed;
    }

    public static void SetLights(Vector3 cam, float time) => Gfx.SetNeon(lights, cam, time);

    // ------------------------------------------------------------------ interaction
    /// <summary>0 = nothing, 1 = exit door, 2 = a game (index in 'game': 0 slots, 1 roulette, 2 blackjack).</summary>
    public static int Near(Vector3 p, out string hint, out int game)
    {
        hint = "";
        game = -1;
        if (p.Z < C.Z - HZ + 3.5f && MathF.Abs(p.X - C.X) < 3.5f) { hint = "E - leave the casino"; return 1; }

        float best = float.MaxValue;
        int bi = -1;
        for (int i = 0; i < stations.Count; i++)
        {
            float d = U.Dist2D(p, stations[i].Pos);
            if (d < stations[i].R && d < best) { best = d; bi = i; }
        }
        if (bi < 0) return 0;
        game = stations[bi].Game;
        hint = $"E - play {stations[bi].Name}";
        return 2;
    }

    // ------------------------------------------------------------------ collisions / camera
    public static void Resolve(ref Vector3 p, float r)
    {
        foreach (var s in solids)
        {
            if (p.X < s.X0 - r || p.X > s.X1 + r || p.Z < s.Z0 - r || p.Z > s.Z1 + r) continue;
            float cx = Math.Clamp(p.X, s.X0, s.X1);
            float cz = Math.Clamp(p.Z, s.Z0, s.Z1);
            float dx = p.X - cx, dz = p.Z - cz;
            float d2 = dx * dx + dz * dz;
            if (d2 >= r * r) continue;

            if (d2 > 1e-6f)
            {
                float d = MathF.Sqrt(d2);
                p.X += dx / d * (r - d);
                p.Z += dz / d * (r - d);
            }
            else
            {
                float l = p.X - s.X0, rr = s.X1 - p.X, t = p.Z - s.Z0, bt = s.Z1 - p.Z;
                float m = MathF.Min(MathF.Min(l, rr), MathF.Min(t, bt));
                if (m == l) p.X = s.X0 - r;
                else if (m == rr) p.X = s.X1 + r;
                else if (m == t) p.Z = s.Z0 - r;
                else p.Z = s.Z1 + r;
            }
        }
        p.X = Math.Clamp(p.X, C.X - HX + r + 0.2f, C.X + HX - r - 0.2f);
        p.Z = Math.Clamp(p.Z, C.Z - HZ + r + 0.2f, C.Z + HZ - r - 0.2f);
    }

    /// <summary>Keeps the third-person camera inside the hall.</summary>
    public static Vector3 ClampCam(Vector3 p) => new(
        Math.Clamp(p.X, C.X - HX + 0.5f, C.X + HX - 0.5f),
        Math.Clamp(p.Y, 0.5f, H - 0.4f),
        Math.Clamp(p.Z, C.Z - HZ + 0.5f, C.Z + HZ - 0.5f));

    // ------------------------------------------------------------------ drawing
    public static void Draw(Vector3 cam, float time)
    {
        Color carpet = U.C(78, 18, 44), ceil = U.C(24, 18, 34), gold = U.C(214, 178, 70);
        float ww = 2f * HX + 1.2f;

        // floor, runner, ceiling
        Gfx.Box(W(0f, -0.15f, 0f), new Vector3(2f * HX, 0.3f, 2f * HZ), 0f, carpet);
        Gfx.Flat(W(0f, 0.012f, 0f), new Vector3(5f, 0.02f, 2f * HZ - 2f), 0f, U.C(120, 84, 24));
        Gfx.Box(W(0f, H + 0.15f, 0f), new Vector3(ww, 0.3f, 2f * HZ + 1.2f), 0f, ceil);

        // walls
        Gfx.Box(W(0f, H / 2f, HZ + 0.3f), new Vector3(ww, H, 0.6f), 0f, wallCol);
        Gfx.Box(W(0f, H / 2f, -HZ - 0.3f), new Vector3(ww, H, 0.6f), 0f, wallCol);
        Gfx.Box(W(-HX - 0.3f, H / 2f, 0f), new Vector3(0.6f, H, 2f * HZ), 0f, wallCol);
        Gfx.Box(W(HX + 0.3f, H / 2f, 0f), new Vector3(0.6f, H, 2f * HZ), 0f, wallCol);

        // neon strips (top) and gold skirting (bottom)
        foreach (float y in new[] { H - 0.7f, 0.4f })
        {
            Color c = y > 1f ? neon : U.C(170, 130, 40);
            Gfx.Flat(W(0f, y, HZ - 0.05f), new Vector3(2f * HX, 0.14f, 0.1f), 0f, c);
            Gfx.Flat(W(0f, y, -HZ + 0.05f), new Vector3(2f * HX, 0.14f, 0.1f), 0f, c);
            Gfx.Flat(W(-HX + 0.05f, y, 0f), new Vector3(0.1f, 0.14f, 2f * HZ), 0f, c);
            Gfx.Flat(W(HX - 0.05f, y, 0f), new Vector3(0.1f, 0.14f, 2f * HZ), 0f, c);
        }

        // ceiling light panels
        foreach (float lx in new[] { -14f, 0f, 14f })
            foreach (float lz in new[] { -9f, 0f, 9f })
                Gfx.Flat(W(lx, H - 0.05f, lz), new Vector3(2.4f, 0.08f, 2.4f), 0f, U.C(255, 232, 180));

        // pillars
        foreach (var p in pillars)
        {
            Gfx.Box(new Vector3(p.X, H / 2f, p.Z), new Vector3(1.2f, H, 1.2f), 0f, gold, Gfx.M.Metal);
            Gfx.Flat(new Vector3(p.X, 2f, p.Z), new Vector3(1.3f, 0.2f, 1.3f), 0f, neon);
            Gfx.Flat(new Vector3(p.X, 7f, p.Z), new Vector3(1.3f, 0.2f, 1.3f), 0f, neon);
        }

        // exit door
        Gfx.Flat(W(0f, 2.3f, -HZ + 0.35f), new Vector3(4.2f, 4.6f, 0.12f), 0f, U.C(150, 110, 30));
        Gfx.Flat(W(0f, 2.1f, -HZ + 0.30f), new Vector3(3.4f, 4.2f, 0.14f), 0f, U.C(26, 18, 12));
        Gfx.Flat(W(0f, 5.3f, -HZ + 0.35f), new Vector3(1.8f, 0.5f, 0.1f), 0f, U.C(60, 255, 120));

        // big sign over the slot machines
        Gfx.Flat(W(0f, 6.4f, HZ - 0.45f), new Vector3(16f, 2.2f, 0.2f), 0f, neon);
        Gfx.Flat(W(0f, 6.4f, HZ - 0.58f), new Vector3(15f, 1.6f, 0.2f), 0f, U.C(15, 10, 25));

        for (int i = 0; i < slots.Count; i++) DrawSlot(slots[i].Pos, slots[i].Yaw, time, i);
        foreach (var r in roulette) DrawRoulette(r, time);
        foreach (var b in blackjack) DrawBlackjack(b);

        foreach (var pr in people)
            Human.Draw(pr.Pos, pr.Yaw, 0f, 0f, pr.Look, -1, U.Dist2D(pr.Pos, cam), 0f);
    }

    static void DrawSlot(Vector3 pos, float yaw, float time, int idx)
    {
        Vector3 P(float x, float y, float z) => U.Local(pos, yaw, x, y, z);

        Gfx.Box(P(0f, 1.1f, 0f), new Vector3(1.2f, 2.2f, 1.0f), yaw, U.C(40, 30, 70), Gfx.M.Metal);
        Gfx.Box(P(0f, 0.9f, 0.55f), new Vector3(1.1f, 0.12f, 0.45f), yaw, U.C(150, 150, 160), Gfx.M.Metal);
        Gfx.Box(P(0f, 0.45f, 0.55f), new Vector3(0.7f, 0.2f, 0.25f), yaw, U.C(20, 20, 24), Gfx.M.Rubber);

        int k = ((int)(time * 2.5f) + idx) % 4;
        Color scr = k == 0 ? U.C(255, 70, 70) : k == 1 ? U.C(255, 210, 60) : k == 2 ? U.C(80, 220, 255) : U.C(120, 255, 120);
        Gfx.Flat(P(0f, 1.55f, 0.52f), new Vector3(0.95f, 0.62f, 0.06f), yaw, scr);
        Gfx.Flat(P(0f, 2.3f, 0.2f), new Vector3(1.2f, 0.2f, 0.7f), yaw, neon);
        Gfx.Flat(P(0.35f, 0.97f, 0.6f), new Vector3(0.14f, 0.05f, 0.14f), yaw, U.C(255, 40, 40));
        Gfx.Flat(P(-0.35f, 0.97f, 0.6f), new Vector3(0.14f, 0.05f, 0.14f), yaw, U.C(40, 255, 80));
    }

    static void DrawRoulette(Vector3 p, float time)
    {
        Color wood = U.C(92, 52, 28), felt = U.C(22, 124, 62);
        Gfx.Box(new Vector3(p.X, 0.5f, p.Z), new Vector3(5.6f, 1.0f, 2.8f), 0f, wood);
        Gfx.Box(new Vector3(p.X, 1.03f, p.Z), new Vector3(5.4f, 0.06f, 2.6f), 0f, felt);

        // betting layout
        Gfx.Flat(new Vector3(p.X + 0.9f, 1.07f, p.Z), new Vector3(3.2f, 0.01f, 1.8f), 0f, U.C(170, 140, 50));
        Gfx.Flat(new Vector3(p.X + 0.4f, 1.08f, p.Z), new Vector3(1.4f, 0.01f, 1.5f), 0f, U.C(176, 26, 36));
        Gfx.Flat(new Vector3(p.X + 1.5f, 1.08f, p.Z), new Vector3(0.8f, 0.01f, 1.5f), 0f, U.C(24, 24, 28));

        // spinning wheel
        Vector3 wc = new(p.X - 1.9f, 1.12f, p.Z);
        Gfx.CylQ(wc, 0.95f, 0.22f, Quaternion.Identity, wood, Gfx.M.Metal);
        Gfx.CylQ(wc + new Vector3(0f, 0.04f, 0f), 0.78f, 0.22f, Quaternion.Identity, U.C(70, 46, 30));
        for (int k = 0; k < 12; k++)
        {
            float a = time * 0.9f + k * MathF.Tau / 12f;
            Gfx.Box(new Vector3(wc.X + MathF.Cos(a) * 0.58f, 1.27f, wc.Z + MathF.Sin(a) * 0.58f),
                    new Vector3(0.2f, 0.06f, 0.2f), -a, k % 2 == 0 ? U.C(200, 28, 38) : U.C(18, 18, 20));
        }
        Gfx.CylQ(wc + new Vector3(0f, 0.12f, 0f), 0.16f, 0.3f, Quaternion.Identity, U.C(230, 190, 70), Gfx.M.Metal);

        // chips + neon edge
        Gfx.CylQ(new Vector3(p.X + 0.2f, 1.12f, p.Z + 0.5f), 0.12f, 0.12f, Quaternion.Identity, U.C(200, 30, 40));
        Gfx.CylQ(new Vector3(p.X + 1.1f, 1.15f, p.Z - 0.4f), 0.12f, 0.18f, Quaternion.Identity, U.C(40, 90, 200));
        Gfx.CylQ(new Vector3(p.X + 1.9f, 1.12f, p.Z + 0.3f), 0.12f, 0.12f, Quaternion.Identity, U.C(230, 190, 70));
        Gfx.Flat(new Vector3(p.X, 1.04f, p.Z - 1.42f), new Vector3(5.6f, 0.06f, 0.06f), 0f, neon);
    }

    static void DrawBlackjack(Vector3 p)
    {
        Color wood = U.C(92, 52, 28), felt = U.C(24, 96, 70);
        Gfx.Box(new Vector3(p.X, 0.5f, p.Z), new Vector3(4.4f, 1.0f, 2.2f), 0f, wood);
        Gfx.Box(new Vector3(p.X, 1.03f, p.Z), new Vector3(4.2f, 0.06f, 2.0f), 0f, felt);

        // bet spots (gold ring)
        for (int i = 0; i < 5; i++)
        {
            float x = p.X - 1.6f + i * 0.8f;
            Gfx.Flat(new Vector3(x, 1.07f, p.Z - 0.35f), new Vector3(0.5f, 0.01f, 0.5f), 0f, U.C(210, 180, 70));
            Gfx.Flat(new Vector3(x, 1.08f, p.Z - 0.35f), new Vector3(0.4f, 0.01f, 0.4f), 0f, felt);
        }

        // cards + chips
        Gfx.Flat(new Vector3(p.X - 0.2f, 1.075f, p.Z + 0.55f), new Vector3(0.32f, 0.012f, 0.46f), 0.1f, U.C(240, 240, 240));
        Gfx.Flat(new Vector3(p.X + 0.2f, 1.075f, p.Z + 0.55f), new Vector3(0.32f, 0.012f, 0.46f), -0.1f, U.C(240, 240, 240));
        Gfx.CylQ(new Vector3(p.X - 0.8f, 1.12f, p.Z - 0.35f), 0.12f, 0.12f, Quaternion.Identity, U.C(200, 30, 40));
        Gfx.CylQ(new Vector3(p.X + 0.8f, 1.14f, p.Z - 0.35f), 0.12f, 0.16f, Quaternion.Identity, U.C(30, 30, 34));
        Gfx.Flat(new Vector3(p.X, 1.04f, p.Z - 1.12f), new Vector3(4.4f, 0.06f, 0.06f), 0f, neon);
    }

    // ------------------------------------------------------------------ 2D labels
    public static void DrawLabels(Camera3D cam)
    {
        Label(cam, W(0f, 7.6f, HZ - 1f), Name.ToUpper() + " CASINO", 34, neon);
        Label(cam, W(0f, 6.2f, -HZ + 1f), "EXIT", 24, U.C(90, 255, 130));
        if (Jobs.IsHere) Label(cam, new Vector3(C.X, 2.7f, C.Z + 6f), "SAL - JOBS", 26, U.C(90, 220, 140));
        foreach (var r in roulette) Label(cam, new Vector3(r.X, 3.3f, r.Z), "ROULETTE", 24, U.C(255, 210, 70));
        foreach (var b in blackjack) Label(cam, new Vector3(b.X, 3.3f, b.Z), "BLACKJACK", 24, U.C(255, 210, 70));
    }

    static void Label(Camera3D cam, Vector3 p, string t, int size, Color c)
    {
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 to = p - cam.Position;
        if (to.Length() > 45f || Vector3.Dot(to, fwd) <= 0f) return;
        Vector2 s = Raylib.GetWorldToScreen(p, cam);
        int w = Raylib.MeasureText(t, size);
        Raylib.DrawText(t, (int)s.X - w / 2 + 2, (int)s.Y + 2, size, U.C(0, 0, 0, 180));
        Raylib.DrawText(t, (int)s.X - w / 2, (int)s.Y, size, c);
    }
}
