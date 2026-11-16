using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>Colours / options of one person.</summary>
public class HumanLook
{
    public Color Shirt, Pants, Skin, Hair, Shoes;
    public bool LongSleeve, Glasses, IsPlayer;
    public int Variant;
}

/// <summary>
/// Articulated low-poly person built from boxes: legs swing around the hip, arms around the shoulder,
/// shoes, belt, neck, head, hair, eyes and (optionally) a weapon held in an aiming pose.
/// Used for both pedestrians and the player.
/// </summary>
public static class Human
{
    static readonly Color Gunmetal = U.C(34, 36, 40);
    static readonly Color Wood = U.C(104, 66, 38);

    /// <summary>Muzzle position of the weapon in the player's local space (x side, y up, z forward).</summary>
    public static Vector3 MuzzleLocal(int gun)
    {
        float z = (WLook)gun switch
        {
            WLook.Pistol => 0.95f, WLook.Smg => 1.20f, WLook.Shotgun => 1.52f, WLook.Rifle => 1.55f, WLook.Sniper => 2.0f,
            WLook.Launcher => 1.5f, WLook.Bat => 0.9f, WLook.Blade => 0.9f, WLook.Minigun => 1.45f, WLook.Magnum => 1.02f,
            _ => 1.2f
        };
        return new Vector3(0.30f, 1.52f, z) + Gfx.GunShift;
    }

    /// <param name="swing">0 = standing, 1 = full walking swing</param>
    /// <param name="gun">-1 = no weapon, otherwise a WLook value (0 pistol, 1 SMG, 2 shotgun, 3 rifle ...)</param>
    /// <param name="camDist">distance to the camera, used for level of detail</param>
    public static void Draw(Vector3 pos, float yaw, float phase, float swing, HumanLook look, int gun, float camDist, float speed = -1f)
    {
        Vector3 p = pos;
        p.Y += World.GroundY(p.X, p.Z);

        // user-supplied model (models/player*, models/npc*) replaces the procedural body
        if (Gfx.Count(look.IsPlayer ? Gfx.CatPlayer : Gfx.CatNpc) > 0 || (look.IsPlayer && Gfx.Count(Gfx.CatNpc) > 0))
        {
            int cat = look.IsPlayer && Gfx.Count(Gfx.CatPlayer) > 0 ? Gfx.CatPlayer : Gfx.CatNpc;
            Gfx.Shadow(new Vector3(pos.X, 0f, pos.Z), yaw, 0.7f, 0.5f, pos.Y > 0.3f ? 0.5f : 1f);
            // baked animation clips: pick one from the real speed (m/s), play it with the stride phase
            bool airborne = pos.Y > 0.15f;
            if (speed < 0f) speed = swing > 0.05f ? 4f : 0f;          // callers that do not pass a speed
            bool moving = speed > 0.3f;
            string clip;
            if (gun >= 0) clip = airborne ? "airaim" : moving ? "aimrun" : "aim";
            else clip = airborne ? "air" : !moving ? "idle" : speed < 3.2f ? "walk" : speed < 6.8f ? "run" : "sprint";
            Quaternion qc = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            Gfx.DrawAnim(cat, look.Variant, clip, phase / MathF.Tau, p, qc, Color.White, camDist);

            // the weapon sits in the hands of the baked 'aim' pose
            if (gun >= 0)
            {
                Vector3 gsh = Gfx.GunShift;
                Vector3 Wc(float x, float y, float z) => p + Vector3.Transform(new Vector3(x + gsh.X, y + gsh.Y, z + gsh.Z), qc);
                DrawGun(Wc, qc, gun);
            }
            return;
        }

        bool near = camDist < 45f;
        Quaternion qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        float bob = MathF.Abs(MathF.Sin(phase)) * 0.035f * swing;

        Vector3 W(float x, float y, float z) => p + Vector3.Transform(new Vector3(x, y, z), qy);
        Quaternion Rx(float a) => qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, a);

        Gfx.Shadow(new Vector3(pos.X, 0f, pos.Z), yaw, 0.7f, 0.5f, pos.Y > 0.3f ? 0.5f : 1f);

        // ---- legs + shoes
        for (int s = -1; s <= 1; s += 2)
        {
            float la = MathF.Sin(phase + (s > 0 ? MathF.PI : 0f)) * 0.55f * swing;
            Quaternion ql = Rx(la);
            Vector3 hip = W(s * 0.11f, 0.90f, 0f);
            Gfx.BoxQ(hip + Vector3.Transform(new Vector3(0f, -0.45f, 0f), ql), new Vector3(0.19f, 0.90f, 0.21f), ql, look.Pants);
            if (near)
                Gfx.BoxQ(hip + Vector3.Transform(new Vector3(0f, -0.86f, 0.06f), ql), new Vector3(0.20f, 0.10f, 0.34f), ql, look.Shoes);
        }

        // ---- torso
        Gfx.BoxQ(W(0f, 1.20f + bob, 0f), new Vector3(0.46f, 0.60f, 0.26f), qy, look.Shirt);
        if (near) Gfx.BoxQ(W(0f, 0.93f + bob * 0.5f, 0f), new Vector3(0.43f, 0.16f, 0.25f), qy, look.Pants);

        // ---- head
        if (near) Gfx.BoxQ(W(0f, 1.55f + bob, 0f), new Vector3(0.11f, 0.10f, 0.11f), qy, look.Skin);
        Gfx.BoxQ(W(0f, 1.69f + bob, 0f), new Vector3(0.20f, 0.24f, 0.22f), qy, look.Skin);
        Gfx.BoxQ(W(0f, 1.83f + bob, -0.01f), new Vector3(0.215f, 0.08f, 0.235f), qy, look.Hair);
        if (near)
        {
            Gfx.BoxQ(W(0f, 1.72f + bob, -0.095f), new Vector3(0.21f, 0.20f, 0.05f), qy, look.Hair);
            if (look.Glasses)
                Gfx.BoxQ(W(0f, 1.71f + bob, 0.112f), new Vector3(0.22f, 0.05f, 0.02f), qy, U.C(8, 8, 10));
            else
            {
                Color eye = U.C(18, 18, 22);
                Gfx.BoxQ(W(-0.05f, 1.71f + bob, 0.112f), new Vector3(0.035f, 0.03f, 0.015f), qy, eye);
                Gfx.BoxQ(W(0.05f, 1.71f + bob, 0.112f), new Vector3(0.035f, 0.03f, 0.015f), qy, eye);
            }
        }

        // ---- arms (and the weapon)
        Color fore = look.LongSleeve ? look.Shirt : look.Skin;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 sh = W(s * 0.30f, 1.47f + bob, 0f);
            Quaternion qa;
            bool aim = false;

            if (gun >= 0 && s > 0)
            {
                qa = Rx(-MathF.PI * 0.5f);                                         // right arm straight forward
                aim = true;
            }
            else if (gun >= 0 && TwoHanded(gun) && s < 0)
            {
                qa = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)      // support hand
                        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI * 0.5f + 0.05f);
            }
            else
            {
                float la = MathF.Sin(phase + (s > 0 ? MathF.PI : 0f)) * 0.55f * swing;
                qa = Rx(-la * 0.9f);
            }

            if (near)
            {
                Gfx.BoxQ(sh + Vector3.Transform(new Vector3(0f, -0.17f, 0f), qa), new Vector3(0.13f, 0.34f, 0.14f), qa, look.Shirt);
                Gfx.BoxQ(sh + Vector3.Transform(new Vector3(0f, -0.49f, 0f), qa), new Vector3(0.115f, 0.32f, 0.125f), qa, fore);
                Gfx.BoxQ(sh + Vector3.Transform(new Vector3(0f, -0.67f, 0f), qa), new Vector3(0.10f, 0.09f, 0.10f), qa, look.Skin);
            }
            else
            {
                Gfx.BoxQ(sh + Vector3.Transform(new Vector3(0f, -0.32f, 0f), qa), new Vector3(0.12f, 0.64f, 0.13f), qa, fore);
            }

            if (aim) DrawGun(W, qy, gun);
        }
    }

    /// <summary>Weapons held with both hands (the left arm comes forward to support them).</summary>
    static bool TwoHanded(int gun) => (WLook)gun is WLook.Smg or WLook.Shotgun or WLook.Rifle or WLook.Sniper or WLook.Launcher or WLook.Minigun;

    static readonly Color Olive = U.C(66, 76, 48);
    static readonly Color Silver = U.C(158, 160, 166);
    static readonly Color BatWood = U.C(196, 150, 92);

    static void DrawGun(Func<float, float, float, Vector3> W, Quaternion qy, int gun)
    {
        const float x = 0.30f, y = 1.50f;
        switch ((WLook)gun)
        {
            case WLook.Pistol:
            {
                Quaternion qg = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.28f);
                Gfx.BoxQ(W(x, y + 0.02f, 0.80f), new Vector3(0.045f, 0.07f, 0.28f), qy, Gunmetal, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.06f, 0.69f), new Vector3(0.05f, 0.14f, 0.06f), qg, U.C(20, 20, 22), Gfx.M.Rubber);
                break;
            }
            case WLook.Magnum:
            {
                Quaternion qg = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.28f);
                Gfx.BoxQ(W(x, y + 0.025f, 0.83f), new Vector3(0.055f, 0.10f, 0.34f), qy, Silver, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.07f, 0.70f), new Vector3(0.055f, 0.16f, 0.07f), qg, U.C(30, 24, 20), Gfx.M.Rubber);
                break;
            }
            case WLook.Smg:
            {
                Quaternion qm = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.1f);
                Gfx.BoxQ(W(x, y + 0.01f, 0.82f), new Vector3(0.06f, 0.11f, 0.42f), qy, Gunmetal, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.02f, 1.12f), new Vector3(0.03f, 0.03f, 0.18f), qy, U.C(18, 18, 20), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.13f, 0.82f), new Vector3(0.04f, 0.20f, 0.07f), qm, U.C(24, 24, 26), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.02f, 0.55f), new Vector3(0.05f, 0.08f, 0.20f), qy, U.C(24, 24, 26), Gfx.M.Rubber);
                break;
            }
            case WLook.Shotgun:
                Gfx.BoxQ(W(x, y + 0.02f, 1.08f), new Vector3(0.05f, 0.06f, 0.90f), qy, U.C(52, 54, 58), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.03f, 0.98f), new Vector3(0.07f, 0.07f, 0.24f), qy, Wood);
                Gfx.BoxQ(W(x, y - 0.03f, 0.55f), new Vector3(0.06f, 0.12f, 0.30f), qy, Wood);
                break;
            case WLook.Rifle:
            {
                Quaternion qm = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.25f);
                Gfx.BoxQ(W(x, y + 0.02f, 0.90f), new Vector3(0.06f, 0.10f, 0.55f), qy, Gunmetal, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.03f, 1.38f), new Vector3(0.03f, 0.03f, 0.42f), qy, U.C(18, 18, 20), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.01f, 1.15f), new Vector3(0.07f, 0.07f, 0.28f), qy, Wood);
                Gfx.BoxQ(W(x, y - 0.02f, 0.52f), new Vector3(0.05f, 0.11f, 0.32f), qy, Wood);
                Gfx.BoxQ(W(x, y - 0.15f, 0.88f), new Vector3(0.04f, 0.20f, 0.08f), qm, U.C(24, 24, 26), Gfx.M.Metal);
                break;
            }
            case WLook.Sniper:
                Gfx.BoxQ(W(x, y + 0.01f, 1.00f), new Vector3(0.05f, 0.09f, 0.90f), qy, Gunmetal, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.02f, 1.62f), new Vector3(0.03f, 0.03f, 0.70f), qy, U.C(18, 18, 20), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.10f, 0.95f), new Vector3(0.05f, 0.05f, 0.36f), qy, U.C(12, 12, 14), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.02f, 0.50f), new Vector3(0.05f, 0.12f, 0.36f), qy, Wood);
                break;
            case WLook.Launcher:
                Gfx.BoxQ(W(x, y + 0.10f, 0.80f), new Vector3(0.13f, 0.13f, 1.10f), qy, Olive, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.10f, 1.40f), new Vector3(0.19f, 0.19f, 0.20f), qy, U.C(40, 46, 32), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y + 0.10f, 0.22f), new Vector3(0.17f, 0.17f, 0.14f), qy, U.C(40, 46, 32), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.03f, 0.75f), new Vector3(0.05f, 0.14f, 0.06f), qy, U.C(20, 20, 22), Gfx.M.Rubber);
                break;
            case WLook.Minigun:
                Gfx.BoxQ(W(x, y - 0.01f, 1.05f), new Vector3(0.13f, 0.13f, 0.70f), qy, Gunmetal, Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.01f, 1.42f), new Vector3(0.17f, 0.17f, 0.06f), qy, U.C(70, 72, 78), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.01f, 0.60f), new Vector3(0.15f, 0.17f, 0.34f), qy, U.C(48, 50, 56), Gfx.M.Metal);
                Gfx.BoxQ(W(x, y - 0.20f, 0.56f), new Vector3(0.20f, 0.22f, 0.22f), qy, Olive, Gfx.M.Metal);
                break;
            case WLook.Bat:
            {
                Quaternion qb = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.30f);
                Gfx.BoxQ(W(x, y + 0.32f, 0.62f), new Vector3(0.075f, 0.90f, 0.075f), qb, BatWood);
                Gfx.BoxQ(W(x, y + 0.66f, 0.52f), new Vector3(0.10f, 0.40f, 0.10f), qb, BatWood);
                break;
            }
            case WLook.Blade:
            {
                Quaternion qb = qy * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.15f);
                Gfx.BoxQ(W(x, y + 0.01f, 0.70f), new Vector3(0.045f, 0.045f, 0.13f), qy, U.C(30, 24, 20), Gfx.M.Rubber);
                Gfx.BoxQ(W(x, y + 0.02f, 0.97f), new Vector3(0.018f, 0.06f, 0.42f), qb, Silver, Gfx.M.Metal);
                break;
            }
        }
    }
}
