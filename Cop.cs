using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

// =====================================================================================
//  WANTED LEVEL
//  A simple 0..5 star system. Heat is a continuous counter that decays slowly when the
//  player is not committing new violations. Stars decide how many cop cars / officers
//  spawn and how aggressive they are.
// =====================================================================================
public static class Wanted
{
    public static int Stars;          // 0..5
    public static float Heat;         // continuous accumulator
    const float PerStar = 4f;         // heat needed for one full star
    const float Decay = 1.5f;         // heat lost per second when out of sight of cops
    public static float Cool;         // time since the player was last seen by a cop

    public static void Add(float amount)
    {
        Heat = Math.Min(Heat + amount, PerStar * 5f);
        UpdateStars();
        Cool = 0f;
    }

    public static void Clear()
    {
        Heat = 0f; Stars = 0; Cool = 0f;
    }

    // Integer truncation: 0..3.99 heat = 0 stars, 4..7.99 = 1 star, etc.
    // (The previous Ceiling() made any heat > 0 give 1 star, which spawned a cop the moment you stole a car.)
    static void UpdateStars() => Stars = Math.Clamp((int)(Heat / PerStar), 0, 5);

    /// <summary>Call every frame. If no cop is nearby, heat bleeds off.</summary>
    public static void Tick(float dt, bool copNearby)
    {
        Cool += dt;
        if (!copNearby && Cool > 3f)
            Heat = MathF.Max(0f, Heat - Decay * dt);
        UpdateStars();
    }
}

// =====================================================================================
//  COP CAR
//  Wraps a Car (which has Police=true so it draws with the siren / light bar).
//  Drives towards the player and rams their car. If the player is on foot and the cop
//  car is close, the officer steps out (a CopNpc is spawned).
// =====================================================================================
public class CopCar
{
    public Car Car;
    public float Life = 14f;        // despawn timer - cops give up quickly so the player can escape
    public bool Active = true;
    public bool StoppedToDrop;      // dropped an officer and is now holding position briefly

    public CopCar(Vector3 pos, float yaw)
    {
        Car = new Car();
        Car.MakePolice(pos, yaw);
        // give the chase AI a sensible cruising speed
        Car.Cruise = U.Rf(Car.Type.CruiseMin, Car.Type.CruiseMax);
    }

    public void Update(float dt, Car playerCar, Vector3 foot, World w, List<Car> cars, List<Npc> npcs, Action<Vector3, float> onDropOfficer)
    {
        Life -= dt;
        if (Life <= 0f) { Active = false; return; }

        Vector3 target = playerCar != null ? playerCar.Pos : foot;
        float distToP = U.Dist2D(Car.Pos, target);

        // If we are close to a player on foot, stop and drop an officer.
        if (playerCar == null && distToP < 12f && !StoppedToDrop)
        {
            StoppedToDrop = true;
            Car.Speed = 0f;
            onDropOfficer?.Invoke(Car.Pos + Car.Side * 1.6f, Car.Yaw);
            Life = MathF.Min(Life, 8f);          // drive off soon after dropping the officer
        }

        if (StoppedToDrop)
        {
            Car.Speed *= MathF.Max(0f, 1f - 3f * dt);
            Car.SirenT += dt;
            return;
        }

        // Direct chase: steer towards the player. We bypass the grid route when the player is in sight.
        Vector3 to = target - Car.Pos; to.Y = 0f;
        float want = MathF.Atan2(to.X, to.Z);
        float diff = U.WrapPi(want - Car.Yaw);

        // Hard turn towards the player, ignoring the road grid - cops blow through intersections during a chase.
        float turn = 3.0f * dt;
        Car.Yaw += Math.Clamp(diff, -turn, turn);

        // Throttle: chase at ~45% of top speed - the player can easily outrun a cop in any car.
        float targetSpeed = Car.Type.Top * 0.45f * (1f - 0.5f * MathF.Min(MathF.Abs(diff), 1f));
        // Maintain a gap: slow down proportional to distance when close, stop at ~6 units so the cop does not ram.
        if (distToP < 16f) targetSpeed = MathF.Min(targetSpeed, distToP * 0.3f);
        if (distToP < 6f)  targetSpeed = 0f;

        // Obstacle avoidance: still brake for walls and other cars in front.
        if (BlockedAhead(cars, npcs, foot)) targetSpeed = MathF.Min(targetSpeed, 2f);

        Car.Brake = targetSpeed < Car.Speed - 0.5f;
        Car.Speed += Math.Clamp(targetSpeed - Car.Speed, -28f * dt, Car.Type.Accel * 0.35f * dt);

        Vector3 np = Car.Pos + Car.Fwd * Car.Speed * dt;
        if (w.Resolve(ref np, Car.Type.Radius + 0.1f)) Car.Speed *= 0.7f;
        Car.Pos = np;

        Car.SirenT += dt;
        Car.Stuck = Car.Speed < 0.5f ? Car.Stuck + dt : 0f;
        if (Car.Stuck > 6f) Active = false;        // hopelessly stuck: give up
    }

    bool BlockedAhead(List<Car> cars, List<Npc> npcs, Vector3 foot)
    {
        Vector3 f = Car.Fwd, s = Car.Side;
        float look = Car.Type.Len * 0.5f + 4.5f + Car.Speed * 0.6f;
        float hw = Car.Type.Wid * 0.5f;
        bool Ahead(Vector3 p, float halfW)
        {
            Vector3 rel = p - Car.Pos; rel.Y = 0f;
            float along = Vector3.Dot(rel, f);
            if (along < 1.5f || along > look) return false;
            return MathF.Abs(Vector3.Dot(rel, s)) < halfW;
        }
        foreach (var c in cars)
            if (c != Car && !c.Police && !c.Player && Ahead(c.Pos, 1.6f + hw)) return true;
        foreach (var n in npcs) if (!n.Dead && Ahead(n.Pos, 0.75f + hw)) return true;
        return false;
    }
}

// =====================================================================================
//  COP NPC (officer on foot)
//  Spawned when a CopCar reaches a player on foot, or directly at higher wanted levels.
//  Runs at the player, fires a pistol, and busts them on contact.
// =====================================================================================
public class CopNpc
{
    public Vector3 Pos;
    public float Yaw, Phase, Speed;
    public float Hp = 25f;           // weak - a couple of pistol shots drop them
    public bool Dead;
    public float deadT;
    public float ShootCool;
    public float Life = 35f;        // despawn timer (shorter so cops do not pile up)
    public bool Active = true;
    public int Variant = 0;         // which cop_npc model variant to use

    static readonly Color UniformShirt = U.C(28, 38, 80);      // navy blue
    static readonly Color UniformPants = U.C(20, 24, 36);      // dark navy
    static readonly Color Cap = U.C(22, 30, 70);

    static readonly Color[] Skins = { U.C(240, 195, 160), U.C(200, 150, 110), U.C(140, 95, 65), U.C(95, 62, 42) };
    static readonly Color[] Hairs = { U.C(22, 18, 16), U.C(70, 44, 26), U.C(176, 146, 84), U.C(40, 30, 24) };

    public readonly HumanLook Look;

    public CopNpc(Vector3 pos, float yaw)
    {
        Pos = pos;
        Yaw = yaw;
        ShootCool = U.Rf(0.5f, 1.2f);
        Variant = U.R.Next(Math.Max(1, Gfx.Count(Gfx.CatCopNpc)));
        Look = new HumanLook
        {
            Shirt = UniformShirt,
            Pants = UniformPants,
            Skin = Skins[U.R.Next(Skins.Length)],
            Hair = Hairs[U.R.Next(Hairs.Length)],
            Shoes = U.C(20, 20, 22),
            LongSleeve = true,
            Glasses = false,
            Variant = Variant
        };
    }

    public bool Hit(float dmg)
    {
        if (Dead) return false;
        Hp -= dmg;
        if (Hp <= 0f) { Dead = true; deadT = 12f; return true; }
        return false;
    }

    /// <param name="lethal">3+ stars: nobody tries to arrest you any more, officers shoot at any range.</param>
    public void Update(float dt, Vector3 playerPos, bool playerInCar, World w, Action onBust, Action<CopNpc> onShoot, bool lethal)
    {
        if (Dead)
        {
            deadT -= dt;
            if (deadT <= 0f) Active = false;
            return;
        }
        Life -= dt;
        if (Life <= 0f) { Active = false; return; }

        Vector3 to = playerPos - Pos; to.Y = 0f;
        float d = to.Length();
        Vector3 dir = d > 0.001f ? to / d : Vector3.UnitX;

        // officers jog at 3.6 m/s - slower than the player's sprint (4.6) so you can outrun them on foot.
        float sp = d > 1.2f ? 3.6f : 0f;
        Speed = sp;
        Pos += dir * sp * dt;
        Phase += sp * dt * 2.6f;
        Yaw = MathF.Atan2(dir.X, dir.Z);

        bool hit = w.Resolve(ref Pos, 0.45f);
        if (hit) { /* scrape along walls - keep moving */ }

        // Contact bust: must be very close (1.2 m) and the player on foot -> arrest (only while the force is not lethal).
        if (!playerInCar && d < 1.2f && !lethal)
        {
            onBust?.Invoke();
            return;
        }

        // Up to 2 stars they fire only at medium range (they want to catch you); from 3 stars they shoot to kill, faster and from point blank.
        ShootCool -= dt;
        if (d < 22f && ShootCool <= 0f && (lethal || playerInCar || d > 5f))
        {
            ShootCool = lethal ? 0.9f : 1.5f;
            onShoot?.Invoke(this);
        }
    }

    public void Draw(Vector3 camPos)
    {
        float dist = U.Dist2D(Pos, camPos);
        Vector3 p = Pos;
        p.Y += World.GroundY(p.X, p.Z);

        if (Dead)
        {
            if (Gfx.HasClip(Gfx.CatCopNpc, Variant, "dead"))
            {
                Gfx.Shadow(Pos, Yaw, 0.9f, 1.6f);
                Gfx.DrawAnim(Gfx.CatCopNpc, Variant, "dead", 0f, p, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw), Color.White);
                return;
            }
            // procedural dead pose
            Gfx.Box(U.Local(p, Yaw, 0f, 0.13f, 0.1f), new Vector3(0.50f, 0.22f, 0.70f), Yaw, Look.Shirt);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.10f, -0.65f), new Vector3(0.36f, 0.20f, 0.75f), Yaw, Look.Pants);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.14f, 0.60f), new Vector3(0.24f, 0.24f, 0.26f), Yaw, Look.Skin);
            Gfx.Shadow(Pos, Yaw, 0.9f, 1.6f);
            return;
        }

        // Use a cop_npc model if one was loaded. Otherwise fall back to the procedural body.
        if (Gfx.Count(Gfx.CatCopNpc) > 0)
        {
            // pick a clip based on speed; the gun is held in the 'aim' pose baked into the model.
            bool moving = Speed > 0.3f;
            // new cop_npc.glb: clips idle/run/aim/aimrun/dead with the pistol baked into the right hand
            bool bakedGun = Gfx.HasClip(Gfx.CatCopNpc, Variant, "aimrun");
            string clip = moving ? (bakedGun ? "aimrun" : "run") : "aim";
            Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw);
            Gfx.Shadow(Pos, Yaw, 0.7f, 0.5f);
            Gfx.DrawAnim(Gfx.CatCopNpc, Variant, clip, Phase / MathF.Tau, p, q, Color.White, dist);
            // procedural pistol only for models that do not have one baked into the hand
            // (the old code always drew it at a fixed spot, which looked like a gun floating above the head)
            if (!bakedGun) DrawPistol(p, Yaw);
            return;
        }

        // Procedural officer: blue uniform, dark cap, badge. The right hand holds a pistol.
        // We pass gun=0 (pistol) to Human.Draw so it raises the arm into the aim pose.
        Human.Draw(Pos, Yaw, Phase, Speed > 0.3f ? 1f : 0f, Look, 0, dist, Speed);
        DrawCap(Pos, Yaw);
    }

    static void DrawCap(Vector3 pos, float yaw)
    {
        Vector3 cap = U.Local(pos, yaw, 0f, 1.86f, -0.02f);
        Gfx.Box(cap, new Vector3(0.245f, 0.09f, 0.26f), yaw, Cap);
        // small brim
        Vector3 brim = U.Local(pos, yaw, 0f, 1.83f, 0.13f);
        Gfx.Box(brim, new Vector3(0.24f, 0.04f, 0.14f), yaw, Cap);
    }

    static void DrawPistol(Vector3 pos, float yaw)
    {
        Vector3 grip = U.Local(pos, yaw, 0.30f, 1.50f, 0.80f);
        Gfx.Box(grip, new Vector3(0.05f, 0.18f, 0.07f), yaw, U.C(20, 20, 22), Gfx.M.Metal);
        Vector3 slide = U.Local(pos, yaw, 0.30f, 1.54f, 0.95f);
        Gfx.Box(slide, new Vector3(0.05f, 0.07f, 0.22f), yaw, U.C(34, 36, 40), Gfx.M.Metal);
    }
}
