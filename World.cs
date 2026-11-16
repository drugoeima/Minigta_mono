using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public class Building
{
    public float MinX, MaxX, MinZ, MaxZ, H;
    public Color Col;
    public bool IsCasino;
    public string Name = "";
    public Color Neon;
    public Vector3 Door;
    public float Seed;          // per-building random seed (window pattern, roof details)
    public bool Tiered;         // upper part is set back
    public int ShopKind = -1;   // -1 = not a shop, 0 = Ammu-Nation, 1 = Black Market
    public bool Hospital;       // you wake up here after dying, and can pay to be patched up
    public Vector3 Center => new((MinX + MaxX) / 2f, 0f, (MinZ + MaxZ) / 2f);
}

public class Palm
{
    public Vector3 Pos;
    public float H, Phase, LeanX, LeanZ;
}

public class Lamp
{
    public Vector3 Pos;
    public float ArmX, ArmZ;    // unit direction of the arm (towards the road)
}

/// <summary>
/// City = grid of N x N blocks separated by roads.
/// Road centre lines are at x = i*Step and z = j*Step (i, j = 0..N). Intersections are the nodes of the grid.
/// </summary>
public class World
{
    public const int N = 12;          // 12x12 block grid (was 6x6) - the city is 4x larger
    public const float Block = 64f, Road = 18f, Step = Block + Road, Side = 11f;
    public const float Max = N * Step;
    public const float LampOffset = 9.4f;      // must match LO in the shader

    public readonly List<Building> Buildings = new();
    public readonly List<Building> Casinos = new();
    public readonly List<Building> Shops = new();           // gun shops (Ammu-Nation / Black Market)
    public readonly List<Building> Hospitals = new();       // where you wake up after dying
    public readonly List<Palm> Palms = new();
    public readonly List<Property> Properties = new();      // homes for sale (west edge of the city)
    readonly List<Lamp> lamps = new();

    // far mountains: angle, distance from the centre, radius, height, yaw
    readonly float[,] mountains = new float[18, 5];

    // Scrap Yard: stolen cars are sold here (east of the city, at the end of the z = 3*Step road)
    public static readonly Vector3 YardPos = new(Max + 32f, 0f, Max / 2f);
    public const float YardHalf = 10f;
    public static bool InYard(Vector3 p) => MathF.Abs(p.X - YardPos.X) < YardHalf && MathF.Abs(p.Z - YardPos.Z) < YardHalf;

    // Gun shops: (block i, block j, kind). All of them stand in ordinary (non-casino) blocks.
    static readonly (int i, int j, int kind)[] ShopSpots =
    {
        (3, 2, 0),      // right next to the start
        (6, 4, 0),
        (9, 6, 0),
        (2, 9, 0),
        (10, 9, 1),     // Black Market: heavy weapons
        (1, 5, 1),
    };

    // Hospitals: (block i, block j). Ordinary (non-casino) blocks without a gun shop; they take the south-east corner building.
    static readonly (int i, int j)[] HospitalSpots =
    {
        (2, 3),
        (9, 1),
        (5, 7),
        (9, 10),
        (1, 10),
    };

    public Vector3 StartPos => new(3 * Step + Side, 0f, 3 * Step + Side);

    static readonly string[] Names =
    {
        "Golden Nugget", "Lucky Star", "Royal Flush", "Bellagio Lite", "Caesars Mini",
        "Neon Palace", "Jackpot Tower", "Mirage Lite", "High Roller", "Sin City Club"
    };
    static readonly Color[] NeonPal =
    {
        U.C(255, 40, 200), U.C(40, 220, 255), U.C(255, 200, 40), U.C(120, 255, 80), U.C(255, 120, 30)
    };
    static readonly Color[] CasinoPal =
    {
        U.C(55, 28, 85), U.C(22, 48, 92), U.C(88, 30, 44), U.C(26, 72, 64), U.C(70, 50, 20)
    };
    static readonly Color[] BldPal =
    {
        U.C(70, 72, 84), U.C(92, 84, 76), U.C(60, 70, 90), U.C(100, 96, 90), U.C(78, 60, 66), U.C(55, 65, 70)
    };

    static readonly Color Sand = U.C(84, 66, 50);
    static readonly Color Asphalt = U.C(58, 58, 64);
    static readonly Color Sidewalk = U.C(92, 90, 98);
    static readonly Color Gold = U.C(255, 210, 40);
    static readonly Color Concrete = U.C(88, 88, 96);

    /// <summary>Height of the ground at (x, z): sidewalks are slightly raised above the road.</summary>
    public static float GroundY(float x, float z)
    {
        if (x <= 0f || x >= Max || z <= 0f || z >= Max) return 0f;
        float fx = x / Step, fz = z / Step;
        float dx = MathF.Abs(fx - MathF.Floor(fx) - 0.5f) * Step;
        float dz = MathF.Abs(fz - MathF.Floor(fz) - 0.5f) * Step;
        return (dx < Block / 2f && dz < Block / 2f) ? 0.18f : 0f;
    }

    static float Hs(float seed, int k)
    {
        float v = MathF.Sin(seed * 127.1f + k * 311.7f) * 43758.5453f;
        return v - MathF.Floor(v);
    }

    public World()
    {
        var rng = new Random(7);          // layout (kept as in the original game)
        var drng = new Random(77);        // visual details only
        int casinoIdx = 0;

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                float cx = (i + 0.5f) * Step, cz = (j + 0.5f) * Step;
                bool casino = (2 * i + 3 * j) % 5 == 0;

                if (casino)
                {
                    float half = 22f;
                    var b = new Building
                    {
                        MinX = cx - half, MaxX = cx + half, MinZ = cz - half, MaxZ = cz + half,
                        H = 28f + (float)rng.NextDouble() * 30f,
                        Col = CasinoPal[casinoIdx % CasinoPal.Length],
                        IsCasino = true,
                        Name = Names[casinoIdx % Names.Length],
                        Neon = NeonPal[casinoIdx % NeonPal.Length],
                        Door = new Vector3(cx, 0f, cz - half - 3f),
                        Seed = (float)drng.NextDouble()
                    };
                    casinoIdx++;
                    Buildings.Add(b);
                    Casinos.Add(b);
                }
                else
                {
                    foreach (float ox in new[] { -15f, 15f })
                    {
                        foreach (float oz in new[] { -15f, 15f })
                        {
                            float h = rng.NextDouble() < 0.2 ? 45f + (float)rng.NextDouble() * 30f : 10f + (float)rng.NextDouble() * 28f;
                            var nb = new Building
                            {
                                MinX = cx + ox - 13f, MaxX = cx + ox + 13f,
                                MinZ = cz + oz - 13f, MaxZ = cz + oz + 13f,
                                H = h,
                                Col = BldPal[rng.Next(BldPal.Length)],
                                Seed = (float)drng.NextDouble(),
                                Tiered = h > 38f
                            };
                            Buildings.Add(nb);
                            // the random numbers above are always drawn, so the city layout is the same as before
                            foreach (var sp in ShopSpots)
                                if (sp.i == i && sp.j == j && ox < 0f && oz < 0f) MakeShop(nb, sp.kind);
                            foreach (var hs in HospitalSpots)
                                if (hs.i == i && hs.j == j && ox > 0f && oz < 0f) MakeHospital(nb);
                        }
                    }
                }

                // palm trees along the block edges
                foreach (float t in new[] { -21f, 0f, 21f })
                {
                    for (int e = 0; e < 4; e++)
                    {
                        if (drng.NextDouble() < 0.4) continue;
                        float px = e < 2 ? cx + t : cx + (e == 2 ? -30f : 30f);
                        float pz = e < 2 ? cz + (e == 0 ? -30f : 30f) : cz + t;
                        if (casino && e == 0 && MathF.Abs(t) < 4f) continue;     // keep the casino entrance free
                        Palms.Add(new Palm
                        {
                            Pos = new Vector3(px, 0.18f, pz),
                            H = 6.5f + (float)drng.NextDouble() * 3f,
                            Phase = (float)drng.NextDouble() * 6.28f,
                            LeanX = ((float)drng.NextDouble() - 0.5f) * 1.6f,
                            LeanZ = ((float)drng.NextDouble() - 0.5f) * 1.6f
                        });
                    }
                }
            }
        }

        // street lamps: sidewalk corners + the middle of every block edge (must match the shader)
        float lo = LampOffset;
        for (int i = 0; i <= N; i++)
        {
            for (int j = 0; j <= N; j++)
            {
                for (int a = -1; a <= 1; a += 2)
                {
                    for (int b = -1; b <= 1; b += 2)
                        lamps.Add(new Lamp { Pos = new Vector3(i * Step + a * lo, 0f, j * Step + b * lo), ArmX = -a, ArmZ = 0f });
                    if (j < N) lamps.Add(new Lamp { Pos = new Vector3(i * Step + a * lo, 0f, (j + 0.5f) * Step), ArmX = -a, ArmZ = 0f });
                    if (i < N) lamps.Add(new Lamp { Pos = new Vector3((i + 0.5f) * Step, 0f, j * Step + a * lo), ArmX = 0f, ArmZ = -a });
                }
            }
        }

        foreach (var b in Buildings) if (b.ShopKind >= 0) Shops.Add(b);
        foreach (var b in Buildings) if (b.Hospital) Hospitals.Add(b);

        // homes you can buy: each sits at the west end of a road and has a parking lot for your cars
        Properties.Add(Property.Make("Studio Flat",        5000,  2, 1 * Step,  9f, U.C(150, 118, 92), 0.21f));
        Properties.Add(Property.Make("Desert Villa",       15000, 4, 3 * Step, 12f, U.C(176, 150, 112), 0.47f));
        Properties.Add(Property.Make("Skyline Penthouse",  40000, 8, 5 * Step, 46f, U.C(58, 70, 96), 0.83f));
        foreach (var pr in Properties) Buildings.Add(pr.Bld);

        // far mountains around the desert
        var mr = new Random(5);
        for (int k = 0; k < mountains.GetLength(0); k++)
        {
            mountains[k, 0] = (k + (float)mr.NextDouble() * 0.6f) * MathF.Tau / mountains.GetLength(0);
            mountains[k, 1] = 1250f + (float)mr.NextDouble() * 350f;
            mountains[k, 2] = 380f + (float)mr.NextDouble() * 320f;
            mountains[k, 3] = 110f + (float)mr.NextDouble() * 170f;
            mountains[k, 4] = (float)mr.NextDouble() * 6.28f;
        }
    }

    static void MakeHospital(Building b)
    {
        b.Hospital = true;
        b.H = 15f;
        b.Tiered = false;
        b.Name = "HOSPITAL";
        b.Col = U.C(206, 210, 218);
        b.Neon = U.C(60, 230, 140);
        b.Door = new Vector3((b.MinX + b.MaxX) / 2f, 0f, b.MinZ - 2.5f);       // on the sidewalk in front, like the shops
    }

    static void MakeShop(Building b, int kind)
    {
        b.ShopKind = kind;
        b.H = 9f;
        b.Tiered = false;
        b.Name = kind == 0 ? "AMMU-NATION" : "BLACK MARKET";
        b.Col = kind == 0 ? U.C(88, 40, 38) : U.C(34, 30, 44);
        b.Neon = kind == 0 ? U.C(255, 70, 60) : U.C(190, 90, 255);
        b.Door = new Vector3((b.MinX + b.MaxX) / 2f, 0f, b.MinZ - 2.5f);       // on the sidewalk in front of the shop
    }

    // ---------------------------------------------------------------- collisions

    /// <summary>Pushes a circle (x,z,r) out of all buildings. Returns true if it touched something.</summary>
    public bool Resolve(ref Vector3 p, float r)
    {
        bool hit = false;
        foreach (var b in Buildings)
        {
            if (p.X < b.MinX - r || p.X > b.MaxX + r || p.Z < b.MinZ - r || p.Z > b.MaxZ + r) continue;

            float cx = Math.Clamp(p.X, b.MinX, b.MaxX);
            float cz = Math.Clamp(p.Z, b.MinZ, b.MaxZ);
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
                float l = p.X - b.MinX, rr = b.MaxX - p.X, t = p.Z - b.MinZ, bt = b.MaxZ - p.Z;
                float m = MathF.Min(MathF.Min(l, rr), MathF.Min(t, bt));
                if (m == l) p.X = b.MinX - r;
                else if (m == rr) p.X = b.MaxX + r;
                else if (m == t) p.Z = b.MinZ - r;
                else p.Z = b.MaxZ + r;
            }
            hit = true;
        }
        p.X = Math.Clamp(p.X, -100f, Max + 100f);
        p.Z = Math.Clamp(p.Z, -100f, Max + 100f);
        return hit;
    }

    /// <summary>Is a 3D point inside a building (used to keep the camera out of walls).</summary>
    public bool Inside(Vector3 p)
    {
        foreach (var b in Buildings)
            if (p.X > b.MinX - 0.3f && p.X < b.MaxX + 0.3f && p.Z > b.MinZ - 0.3f && p.Z < b.MaxZ + 0.3f && p.Y < b.H)
                return true;
        return false;
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(Vector3 cam, float time)
    {
        // far mountains (drawn first, they are behind everything)
        for (int k = 0; k < mountains.GetLength(0); k++)
        {
            float a = mountains[k, 0], d = mountains[k, 1];
            Vector3 bp = new(Max / 2f + MathF.Cos(a) * d, -2f, Max / 2f + MathF.Sin(a) * d);
            Gfx.Mountain(bp, mountains[k, 2], mountains[k, 3], mountains[k, 4], U.C(46, 40, 62));
        }

        // desert ground + roads (road markings and crosswalks are procedural, in the shader)
        Gfx.Box(new Vector3(Max / 2f, -1.1f, Max / 2f), new Vector3(Max + 1200f, 1f, Max + 1200f), 0f, Sand, Gfx.M.Sand);
        for (int i = 0; i <= N; i++)
        {
            float l = i * Step;
            Gfx.Box(new Vector3(l, -0.25f, Max / 2f), new Vector3(Road, 0.5f, Max + Road), 0f, Asphalt, Gfx.M.Asphalt);
            Gfx.Box(new Vector3(Max / 2f, -0.25f, l), new Vector3(Max + Road, 0.5f, Road), 0f, Asphalt, Gfx.M.Asphalt);
        }

        // sidewalk slabs under every block
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                Gfx.Box(new Vector3((i + 0.5f) * Step, 0.09f, (j + 0.5f) * Step), new Vector3(Block, 0.18f, Block), 0f, Sidewalk, Gfx.M.Walk);

        DrawLamps(cam);

        // buildings
        foreach (var b in Buildings)
        {
            Vector3 c = b.Center;
            float dx = c.X - cam.X, dz = c.Z - cam.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > 330f * 330f) continue;
            float dist = MathF.Sqrt(d2);

            int mat = b.IsCasino ? Gfx.M.CasinoWall : Gfx.M.Building;
            float w = b.MaxX - b.MinX, d = b.MaxZ - b.MinZ;

            if (b.Tiered)
            {
                float h1 = b.H * 0.66f, h2 = b.H - h1;
                float tw = w * 0.62f, td = d * 0.62f;
                Gfx.Box(new Vector3(c.X, h1 / 2f, c.Z), new Vector3(w, h1, d), 0f, b.Col, mat, b.Seed);
                Gfx.Box(new Vector3(c.X, h1 + h2 / 2f, c.Z), new Vector3(tw, h2, td), 0f, b.Col, mat, b.Seed + 0.31f);
                DrawRoof(b, c.X, c.Z, w, d, h1, dist, time, false);      // roof of the lower tier
                DrawRoof(b, c.X, c.Z, tw, td, b.H, dist, time, true);
            }
            else
            {
                Gfx.Box(new Vector3(c.X, b.H / 2f, c.Z), new Vector3(w, b.H, d), 0f, b.Col, mat, b.Seed);
                if (!b.IsCasino) DrawRoof(b, c.X, c.Z, w, d, b.H, dist, time, b.H > 44f);
            }

            if (b.IsCasino) DrawCasinoDecor(b, time, dist);
            if (b.ShopKind >= 0) DrawShopDecor(b, time, dist);
            if (b.Hospital) DrawHospitalDecor(b, time, dist);
        }

        // palm trees
        foreach (var p in Palms)
        {
            float dx = p.Pos.X - cam.X, dz = p.Pos.Z - cam.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > 150f * 150f) continue;
            DrawPalm(p, MathF.Sqrt(d2), time);
        }

        DrawYard(cam, time);
        foreach (var pr in Properties) pr.Draw(cam, time);
    }

    /// <summary>Semi-transparent effects (casino search-light beams). Draw after everything else.</summary>
    public void DrawLate(Vector3 cam, float time)
    {
        Gfx.BeginTranslucent();
        for (int k = 0; k < Casinos.Count; k++)
        {
            Building b = Casinos[k];
            Vector3 c = b.Center;
            float dx = c.X - cam.X, dz = c.Z - cam.Z;
            if (dx * dx + dz * dz > 330f * 330f) continue;

            Vector3 top = new(c.X, b.H + 1.2f, c.Z);
            Color glow = U.C(b.Neon.R, b.Neon.G, b.Neon.B, 20);
            for (int s = 0; s < 2; s++)
            {
                float yaw = time * 0.5f + k * 1.3f + s * MathF.PI;
                Quaternion q = Quaternion.CreateFromYawPitchRoll(yaw, -1.1f, 0f);
                Gfx.FlatQ(top + Vector3.Transform(new Vector3(0f, 0f, 90f), q), new Vector3(3.5f, 3.5f, 180f), q, glow);
            }
        }
        Gfx.EndTranslucent();
    }

    // ---------------------------------------------------------------- details

    void DrawLamps(Vector3 cam)
    {
        Color pole = U.C(40, 42, 48), head = U.C(255, 214, 150);
        foreach (var l in lamps)
        {
            float dx = l.Pos.X - cam.X, dz = l.Pos.Z - cam.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > 160f * 160f) continue;

            Vector3 p = l.Pos;
            Gfx.Box(new Vector3(p.X, 0.3f, p.Z), new Vector3(0.45f, 0.5f, 0.45f), 0f, pole, Gfx.M.Metal);
            Gfx.Box(new Vector3(p.X, 3.7f, p.Z), new Vector3(0.2f, 7.4f, 0.2f), 0f, pole, Gfx.M.Metal);
            if (d2 > 90f * 90f) { Gfx.Flat(new Vector3(p.X, 7.4f, p.Z), new Vector3(0.7f, 0.2f, 0.7f), 0f, head); continue; }
            Gfx.Box(new Vector3(p.X + l.ArmX * 0.6f, 7.3f, p.Z + l.ArmZ * 0.6f),
                    new Vector3(MathF.Abs(l.ArmX) > 0.5f ? 1.3f : 0.14f, 0.14f, MathF.Abs(l.ArmZ) > 0.5f ? 1.3f : 0.14f), 0f, pole, Gfx.M.Metal);
            Gfx.Flat(new Vector3(p.X + l.ArmX * 1.15f, 7.2f, p.Z + l.ArmZ * 1.15f), new Vector3(0.7f, 0.12f, 0.7f), 0f, head);
        }
    }

    static void DrawRoof(Building b, float cx, float cz, float w, float d, float top, float dist, float time, bool mast)
    {
        if (dist > 170f) return;

        // parapet
        Gfx.Box(new Vector3(cx, top + 0.45f, cz - d / 2f + 0.2f), new Vector3(w, 0.9f, 0.4f), 0f, Concrete);
        Gfx.Box(new Vector3(cx, top + 0.45f, cz + d / 2f - 0.2f), new Vector3(w, 0.9f, 0.4f), 0f, Concrete);
        Gfx.Box(new Vector3(cx - w / 2f + 0.2f, top + 0.45f, cz), new Vector3(0.4f, 0.9f, d), 0f, Concrete);
        Gfx.Box(new Vector3(cx + w / 2f - 0.2f, top + 0.45f, cz), new Vector3(0.4f, 0.9f, d), 0f, Concrete);

        // air-conditioning units
        for (int k = 0; k < 3; k++)
        {
            float ux = cx + (Hs(b.Seed, k * 2) - 0.5f) * (w - 6f);
            float uz = cz + (Hs(b.Seed, k * 2 + 1) - 0.5f) * (d - 6f);
            Gfx.Box(new Vector3(ux, top + 0.8f, uz), new Vector3(2.4f, 1.6f, 1.8f), 0f, U.C(118, 122, 128));
            Gfx.Box(new Vector3(ux, top + 1.63f, uz), new Vector3(1.3f, 0.06f, 1.3f), 0f, U.C(30, 30, 34), Gfx.M.Metal);
        }

        if (mast)
        {
            Gfx.Box(new Vector3(cx, top + 5f, cz), new Vector3(0.25f, 10f, 0.25f), 0f, U.C(50, 50, 56), Gfx.M.Metal);
            if (MathF.Sin(time * 3f + b.Seed * 10f) > 0.1f)
                Gfx.Flat(new Vector3(cx, top + 10.2f, cz), new Vector3(0.5f, 0.5f, 0.5f), 0f, U.C(255, 30, 30));
        }
    }

    static void DrawCasinoDecor(Building b, float time, float dist)
    {
        Vector3 c = b.Center;
        float w = b.MaxX - b.MinX, d = b.MaxZ - b.MinZ;

        // neon roof band + corner strips
        Gfx.Flat(new Vector3(c.X, b.H + 0.6f, c.Z), new Vector3(w + 1.2f, 1.2f, d + 1.2f), 0f, b.Neon);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Gfx.Flat(new Vector3(c.X + sx * (w / 2f), b.H / 2f, c.Z + sz * (d / 2f)), new Vector3(0.8f, b.H, 0.8f), 0f, b.Neon);

        // big sign over the entrance
        float sw = w * 0.6f;
        Gfx.Flat(new Vector3(c.X, 9f, b.MinZ - 0.4f), new Vector3(sw, 5f, 0.6f), 0f, b.Neon);
        Gfx.Flat(new Vector3(c.X, 9f, b.MinZ - 0.8f), new Vector3(sw - 1.5f, 3.5f, 0.3f), 0f, U.C(15, 10, 25));

        if (dist < 170f)
        {
            // chasing marquee bulbs around the sign
            int n = (int)(sw / 1.4f);
            int phase = (int)(time * 6f);
            for (int k = 0; k <= n; k++)
            {
                float x = c.X - sw / 2f + k * sw / n;
                Color on = ((k + phase) % 3 != 0) ? U.C(255, 236, 170) : U.C(90, 70, 40);
                Gfx.Flat(new Vector3(x, 11.7f, b.MinZ - 1.0f), new Vector3(0.35f, 0.35f, 0.35f), 0f, on);
                Gfx.Flat(new Vector3(x, 6.3f, b.MinZ - 1.0f), new Vector3(0.35f, 0.35f, 0.35f), 0f, on);
            }
            for (int k = 1; k < 4; k++)
            {
                float y = 6.3f + k * 1.35f;
                Color on = ((k + phase) % 3 != 0) ? U.C(255, 236, 170) : U.C(90, 70, 40);
                Gfx.Flat(new Vector3(c.X - sw / 2f, y, b.MinZ - 1.0f), new Vector3(0.35f, 0.35f, 0.35f), 0f, on);
                Gfx.Flat(new Vector3(c.X + sw / 2f, y, b.MinZ - 1.0f), new Vector3(0.35f, 0.35f, 0.35f), 0f, on);
            }

            // entrance canopy with warm light underneath
            Gfx.Box(new Vector3(c.X, 5.7f, b.MinZ - 4.2f), new Vector3(10f, 0.5f, 8f), 0f, U.C(30, 24, 40), Gfx.M.Generic);
            Gfx.Flat(new Vector3(c.X, 5.4f, b.MinZ - 4.2f), new Vector3(9f, 0.08f, 7f), 0f, U.C(255, 222, 170));
            Gfx.Box(new Vector3(c.X - 4.5f, 2.8f, b.MinZ - 7.6f), new Vector3(0.5f, 5.6f, 0.5f), 0f, U.C(214, 178, 70), Gfx.M.Metal);
            Gfx.Box(new Vector3(c.X + 4.5f, 2.8f, b.MinZ - 7.6f), new Vector3(0.5f, 5.6f, 0.5f), 0f, U.C(214, 178, 70), Gfx.M.Metal);
        }

        // golden entrance marker
        Gfx.Flat(new Vector3(b.Door.X, 0.25f, b.Door.Z), new Vector3(3.2f, 0.1f, 3.2f), 0f, Gold);
        Gfx.Flat(new Vector3(b.Door.X, 3f, b.Door.Z), new Vector3(0.25f, 6f, 0.25f), 0f, Gold);
    }

    static void DrawShopDecor(Building b, float time, float dist)
    {
        Vector3 c = b.Center;
        float w = b.MaxX - b.MinX;
        Color dark = U.C(14, 12, 20);

        // beacon (visible from far away) + marker on the ground at the door
        Gfx.Flat(new Vector3(b.Door.X, 22f, b.Door.Z), new Vector3(0.4f, 44f, 0.4f), 0f, b.Neon);
        float pu = 1f + 0.12f * MathF.Sin(time * 3f);
        Gfx.Flat(new Vector3(b.Door.X, 0.25f, b.Door.Z), new Vector3(3.2f * pu, 0.08f, 3.2f * pu), 0f, b.Neon);

        // sign over the door, neon roof band
        Gfx.Flat(new Vector3(c.X, 6.6f, b.MinZ - 0.35f), new Vector3(w * 0.8f, 2.2f, 0.5f), 0f, b.Neon);
        Gfx.Flat(new Vector3(c.X, 6.6f, b.MinZ - 0.7f), new Vector3(w * 0.8f - 1f, 1.5f, 0.3f), 0f, dark);
        Gfx.Flat(new Vector3(c.X, b.H + 0.35f, c.Z), new Vector3(w + 0.8f, 0.7f, (b.MaxZ - b.MinZ) + 0.8f), 0f, b.Neon);

        if (dist > 170f) return;

        // lit shop window + door, barred
        Gfx.Flat(new Vector3(c.X - 7.5f, 2.2f, b.MinZ - 0.15f), new Vector3(7f, 2.4f, 0.2f), 0f, U.C(255, 226, 170));
        Gfx.Flat(new Vector3(c.X + 7.5f, 2.2f, b.MinZ - 0.15f), new Vector3(7f, 2.4f, 0.2f), 0f, U.C(255, 226, 170));
        Gfx.Flat(new Vector3(c.X, 1.7f, b.MinZ - 0.15f), new Vector3(2.6f, 3.4f, 0.22f), 0f, dark);
        for (int k = -1; k <= 1; k += 2)
            for (int g = 0; g < 4; g++)
                Gfx.Box(new Vector3(c.X + k * 7.5f - 2.6f + g * 1.75f, 2.2f, b.MinZ - 0.3f), new Vector3(0.08f, 2.4f, 0.08f), 0f, U.C(30, 30, 34), Gfx.M.Metal);

        // awning
        Gfx.Box(new Vector3(c.X, 4.3f, b.MinZ - 1.5f), new Vector3(w * 0.8f, 0.25f, 3f), 0f, b.Col, Gfx.M.Generic);
    }

    static void DrawHospitalDecor(Building b, float time, float dist)
    {
        Vector3 c = b.Center;
        float w = b.MaxX - b.MinX;
        Color white = U.C(244, 246, 250), green = b.Neon, dark = U.C(14, 20, 24);

        // beacon (visible from far away) + pulsing pad on the ground at the door
        Gfx.Flat(new Vector3(b.Door.X, 24f, b.Door.Z), new Vector3(0.4f, 48f, 0.4f), 0f, green);
        float pu = 1f + 0.12f * MathF.Sin(time * 3f);
        Gfx.Flat(new Vector3(b.Door.X, 0.25f, b.Door.Z), new Vector3(3.2f * pu, 0.08f, 3.2f * pu), 0f, green);

        // green roof band + a big cross on the front wall
        Gfx.Flat(new Vector3(c.X, b.H + 0.35f, c.Z), new Vector3(w + 0.8f, 0.7f, (b.MaxZ - b.MinZ) + 0.8f), 0f, green);
        float zf = b.MinZ - 0.3f;
        Gfx.Flat(new Vector3(c.X, 10.5f, zf), new Vector3(7.5f, 7.5f, 0.3f), 0f, white);
        Gfx.Flat(new Vector3(c.X, 10.5f, zf - 0.2f), new Vector3(1.8f, 6f, 0.3f), 0f, green);
        Gfx.Flat(new Vector3(c.X, 10.5f, zf - 0.2f), new Vector3(6f, 1.8f, 0.3f), 0f, green);

        if (dist > 170f) return;

        // lit door and windows, awning
        Gfx.Flat(new Vector3(c.X - 7.5f, 2.2f, b.MinZ - 0.15f), new Vector3(7f, 2.4f, 0.2f), 0f, U.C(205, 240, 255));
        Gfx.Flat(new Vector3(c.X + 7.5f, 2.2f, b.MinZ - 0.15f), new Vector3(7f, 2.4f, 0.2f), 0f, U.C(205, 240, 255));
        Gfx.Flat(new Vector3(c.X, 1.7f, b.MinZ - 0.15f), new Vector3(2.8f, 3.4f, 0.22f), 0f, dark);
        Gfx.Box(new Vector3(c.X, 4.3f, b.MinZ - 1.5f), new Vector3(w * 0.8f, 0.25f, 3f), 0f, white, Gfx.M.Generic);
    }

    static void DrawYard(Vector3 cam, float time)
    {
        Vector3 y = YardPos;
        Color orange = U.C(255, 150, 30);

        // light beam, visible from far away
        Gfx.Flat(new Vector3(y.X, 30f, y.Z), new Vector3(0.6f, 60f, 0.6f), 0f, orange);

        float dx = y.X - cam.X, dz = y.Z - cam.Z;
        if (dx * dx + dz * dz > 260f * 260f) return;

        Color fence = U.C(112, 114, 124), post = U.C(230, 180, 40);
        float gx = y.X - 22f;      // gate (faces the city)

        // concrete lot
        Gfx.Box(new Vector3(y.X - 2f, -0.2f, y.Z), new Vector3(50f, 0.5f, 26f), 0f, Concrete, Gfx.M.Walk);

        // fence: back, two sides, front with a gate gap
        Gfx.Box(new Vector3(y.X + 23f, 1.3f, y.Z), new Vector3(0.3f, 2.6f, 26f), 0f, fence, Gfx.M.Metal);
        Gfx.Box(new Vector3(y.X + 0.5f, 1.3f, y.Z - 13f), new Vector3(45f, 2.6f, 0.3f), 0f, fence, Gfx.M.Metal);
        Gfx.Box(new Vector3(y.X + 0.5f, 1.3f, y.Z + 13f), new Vector3(45f, 2.6f, 0.3f), 0f, fence, Gfx.M.Metal);
        Gfx.Box(new Vector3(gx, 1.3f, y.Z - 10f), new Vector3(0.3f, 2.6f, 6f), 0f, fence, Gfx.M.Metal);
        Gfx.Box(new Vector3(gx, 1.3f, y.Z + 10f), new Vector3(0.3f, 2.6f, 6f), 0f, fence, Gfx.M.Metal);

        // gate posts + neon sign
        Gfx.Box(new Vector3(gx, 3f, y.Z - 7f), new Vector3(0.8f, 6f, 0.8f), 0f, post, Gfx.M.Metal);
        Gfx.Box(new Vector3(gx, 3f, y.Z + 7f), new Vector3(0.8f, 6f, 0.8f), 0f, post, Gfx.M.Metal);
        Gfx.Box(new Vector3(gx + 0.3f, 7.2f, y.Z), new Vector3(0.3f, 2.6f, 16.6f), 0f, U.C(20, 16, 24));
        Gfx.Flat(new Vector3(gx - 0.05f, 7.2f, y.Z), new Vector3(0.4f, 1.8f, 16f), 0f, orange);

        // sell zone marker
        float pu = 1f + 0.15f * MathF.Sin(time * 3f);
        Gfx.Flat(new Vector3(y.X, 0.1f, y.Z - YardHalf), new Vector3(2f * YardHalf + 0.6f, 0.04f, 0.6f), 0f, orange);
        Gfx.Flat(new Vector3(y.X, 0.1f, y.Z + YardHalf), new Vector3(2f * YardHalf + 0.6f, 0.04f, 0.6f), 0f, orange);
        Gfx.Flat(new Vector3(y.X - YardHalf, 0.1f, y.Z), new Vector3(0.6f, 0.04f, 2f * YardHalf + 0.6f), 0f, orange);
        Gfx.Flat(new Vector3(y.X + YardHalf, 0.1f, y.Z), new Vector3(0.6f, 0.04f, 2f * YardHalf + 0.6f), 0f, orange);
        Gfx.Flat(new Vector3(y.X, 0.1f, y.Z), new Vector3(8f * pu, 0.04f, 8f * pu), 0f, U.C(255, 120, 20));

        // scrap cars + crane
        Gfx.Box(new Vector3(y.X + 15f, 0.8f, y.Z - 8f), new Vector3(4.4f, 1.4f, 2.0f), 0.4f, U.C(150, 60, 50));
        Gfx.Box(new Vector3(y.X + 15.3f, 2.2f, y.Z - 7.8f), new Vector3(4.2f, 1.3f, 1.9f), 0.55f, U.C(60, 80, 130));
        Gfx.Box(new Vector3(y.X + 16f, 0.8f, y.Z + 8f), new Vector3(4.4f, 1.4f, 2.0f), -0.3f, U.C(200, 170, 50));
        Gfx.Box(new Vector3(y.X + 19f, 5f, y.Z + 1f), new Vector3(0.8f, 10f, 0.8f), 0f, post, Gfx.M.Metal);
        Gfx.Box(new Vector3(y.X + 13f, 10.3f, y.Z + 1f), new Vector3(13f, 0.6f, 0.6f), 0f, post, Gfx.M.Metal);
        Gfx.Box(new Vector3(y.X + 8f, 8.1f, y.Z + 1f), new Vector3(0.1f, 4f, 0.1f), 0f, U.C(30, 30, 34), Gfx.M.Metal);
        Gfx.Box(new Vector3(y.X + 8f, 5.9f, y.Z + 1f), new Vector3(2f, 0.5f, 2f), 0f, U.C(30, 30, 34), Gfx.M.Metal);
    }

    static void DrawPalm(Palm p, float dist, float time)
    {
        Color trunk = U.C(122, 98, 70), leaf = U.C(34, 96, 46);
        int segs = dist < 70f ? 5 : 2;
        float segH = p.H / segs;

        for (int i = 0; i < segs; i++)
        {
            float t = (i + 0.5f) / segs;
            float wd = 0.46f - 0.18f * t;
            Vector3 c = new(p.Pos.X + p.LeanX * t * t, p.Pos.Y + segH * (i + 0.5f), p.Pos.Z + p.LeanZ * t * t);
            Gfx.Box(c, new Vector3(wd, segH + 0.05f, wd), p.Phase, trunk);
        }

        Vector3 crown = new(p.Pos.X + p.LeanX, p.Pos.Y + p.H, p.Pos.Z + p.LeanZ);
        int fronds = dist < 70f ? 9 : 5;
        for (int k = 0; k < fronds; k++)
        {
            float yaw = p.Phase + k * MathF.Tau / fronds;
            float droop = 0.35f + 0.35f * Hs(p.Phase, k) + MathF.Sin(time * 1.4f + p.Phase + k) * 0.05f;
            Quaternion q = Quaternion.CreateFromYawPitchRoll(yaw, droop, 0f);
            float len = 3.6f;
            Gfx.BoxQ(crown + Vector3.Transform(new Vector3(0f, 0f, len / 2f), q), new Vector3(0.75f, 0.07f, len), q, leaf, Gfx.M.Foliage);
        }
        if (dist < 70f)
            Gfx.Box(crown + new Vector3(0f, -0.35f, 0f), new Vector3(0.6f, 0.5f, 0.6f), p.Phase, U.C(92, 64, 40));
    }
}
