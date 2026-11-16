using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

// =====================================================================================
//  CAR
//  Every car has a CarType (sedan, sports car, taxi, SUV, pickup, van, truck, bus).
//  With user models (models/car*.glb) the type comes from the model's file name,
//  without them each type has its own built-in body.
// =====================================================================================
public class Car
{
    const float Lane = 4.2f;
    // City speed limit (~50 km/h). The player can exceed it; doing so raises the wanted level.
    public const float CitySpeedLimit = 14f;   // m/s, ~50 km/h

    public Vector3 Pos;
    public float Yaw, Speed;
    public Color Col;
    public CarType Type;
    public int Model = -1;      // index of the user model (models/car*), -1 = built-in body
    public bool Ai = true;      // driven by traffic AI
    public bool Player;         // currently driven by the player
    public float Cruise, Scared, Stuck;
    public bool Brake;          // brake lights
    public bool Stolen;         // carjacked by the player (can be sold at the Scrap Yard)
    public bool Owned;          // parked in your garage once: stays yours, never recycled into traffic
    public int Value;           // price at the Scrap Yard
    public bool Police;         // police cruiser (drawn with light bar + siren, never civilian traffic)
    public float SirenT;        // siren phase (flashing red/blue)
    public bool WrongSide;      // set by the traffic-rules check when the player drives against traffic

    // visual state (suspension lean, wheel spin, steering angle)
    float lastSpeed, lastYaw, pitch, roll, wheel, steerVis;

    static readonly float[] AxVan = { 1.6f, -1.55f };
    static readonly float[] AxPickup = { 1.75f, -1.55f };
    static readonly float[] AxTruck = { 2.75f, -1.6f, -2.95f };
    static readonly float[] AxBus = { 3.5f, -3.3f };
    readonly float[] axSedan = new float[2];

    /// <summary>Position of the headlights (used for the light cone when the player drives).</summary>
    public Vector3 Front => U.Local(Pos, Yaw, 0f, 0.85f, Type.Len * 0.48f);

    // AI route: we drive towards grid node (Tx, Tz) in direction (Dx, Dz)
    int Tx, Tz, Dx, Dz;

    public Vector3 Fwd => new(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));
    public Vector3 Side => new(MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));

    public Car() { Roll(); }

    /// <summary>Picks the class, the user model (if any), the paint and the cruising speed.
    /// When the user has uploaded any car* models, one is picked uniformly at random and its kind
    /// (parsed from the file name) decides the CarType. This way EVERY uploaded car model appears in traffic,
    /// regardless of whether its name matches a known keyword.</summary>
    /// <param name="keep">keep the current model if it is still valid (used when models are loaded later with F6)</param>
    void Roll(bool keep = false)
    {
        int nm = Gfx.Count(Gfx.CatCar);
        if (nm > 0)
        {
            if (keep && Model >= 0 && Model < nm)
                return;                  // current model is still valid, keep Type / Col / Cruise as-is
            Model = U.R.Next(nm);
            Type = CarType.Of((CarKind)Gfx.CarModelKind(Model));
        }
        else
        {
            Type = CarType.Random(null);
            Model = -1;
        }
        Col = Type.Paint[U.R.Next(Type.Paint.Length)];
        Cruise = U.Rf(Type.CruiseMin, Type.CruiseMax);
    }

    /// <summary>Set this car up as a police cruiser. Used by the Cop system when the player is wanted.</summary>
    public void MakePolice(Vector3 pos, float yaw)
    {
        Type = CarType.Of(CarKind.Police);
        Col = Type.Paint[0];
        Cruise = U.Rf(Type.CruiseMin, Type.CruiseMax);
        Pos = pos;
        Yaw = yaw;
        Speed = 0f;
        Stuck = 0f; Scared = 0f;
        Ai = true; Player = false;
        Stolen = false; Owned = false;
        Police = true;
        Model = Gfx.Count(Gfx.CatCopCar) > 0 ? U.R.Next(Gfx.Count(Gfx.CatCopCar)) : -1;
        // If there is no cop_car model, fall back to a civilian police-named model if present.
        if (Model < 0 && Gfx.HasCarKind((int)CarKind.Police)) Model = Gfx.RandomCarModel((int)CarKind.Police);
    }

    // Right-hand traffic: the car drives on the RIGHT side of the road centreline.
    // For direction (Dx, Dz), the right side (in world coords) is (-Dz, +Dx).
    // Example: driving +X (Dx=1, Dz=0) -> offset (0, 0, +1) = +Z = north.
    // A west-bound car on the north side of the road = right-hand traffic. Correct.
    Vector3 Waypoint => new Vector3(Tx * World.Step, 0f, Tz * World.Step) + new Vector3(-Dz, 0f, Dx) * Lane;

    /// <summary>Put the car on a random road segment, at least 90 units away from 'avoid', as traffic.</summary>
    public void Respawn(Vector3 avoid)
    {
        Roll();
        for (int tries = 0; tries < 25; tries++)
        {
            bool alongX = U.R.Next(2) == 0;
            int line = U.R.Next(World.N + 1);
            int a = U.R.Next(World.N);
            int sign = U.R.Next(2) == 0 ? 1 : -1;
            float t = U.Rf(0.15f, 0.85f);
            float along = (a + t) * World.Step;

            if (alongX) { Dx = sign; Dz = 0; Tx = sign > 0 ? a + 1 : a; Tz = line; }
            else        { Dx = 0; Dz = sign; Tx = line; Tz = sign > 0 ? a + 1 : a; }

            Yaw = MathF.Atan2(Dx, Dz);
            Vector3 p = alongX ? new Vector3(along, 0f, line * World.Step) : new Vector3(line * World.Step, 0f, along);
            p -= Side * Lane;          // right-hand traffic: place on the LEFT of the car (= RIGHT of the road)
            Pos = p;
            if (U.Dist2D(p, avoid) > 90f) break;
        }
        Speed = Cruise;
        Stuck = 0f; Scared = 0f;
        Ai = true; Player = false;
        Stolen = false; Owned = false;
        // Respawn always returns the car to civilian traffic - clear any police role it had.
        Police = false; SirenT = 0f; WrongSide = false;
    }

    void PickNext()
    {
        var opts = new List<(int dx, int dz)>();
        void Try(int dx, int dz)
        {
            if (dx == -Dx && dz == -Dz) return; // no U-turns
            int nx = Tx + dx, nz = Tz + dz;
            if (nx < 0 || nx > World.N || nz < 0 || nz > World.N) return;
            opts.Add((dx, dz));
        }
        Try(Dx, Dz); Try(Dx, Dz);               // straight is twice as likely
        Try(Dz, -Dx); Try(-Dz, Dx);             // right / left
        if (opts.Count == 0) { Dx = -Dx; Dz = -Dz; Tx += Dx; Tz += Dz; return; }
        var o = opts[U.R.Next(opts.Count)];
        Dx = o.dx; Dz = o.dz; Tx += Dx; Tz += Dz;
    }

    // ------------------------------------------------------------ traffic AI
    public void UpdateAi(float dt, List<Car> cars, List<Npc> npcs, Vector3 foot)
    {
        Vector3 to = Waypoint - Pos; to.Y = 0f;
        bool atIntersection = to.Length() < 8f;
        if (atIntersection) { PickNext(); to = Waypoint - Pos; to.Y = 0f; }

        float want = MathF.Atan2(to.X, to.Z);
        float diff = U.WrapPi(want - Yaw);
        float turn = 2.6f * (0.6f + 0.4f * Type.Steer) * dt;      // buses and trucks turn slower
        Yaw += Math.Clamp(diff, -turn, turn);

        float target = Cruise * (Scared > 0f ? 1.6f : 1f) * (1f - 0.45f * MathF.Min(MathF.Abs(diff), 1f));
        if (Blocked(cars, npcs, foot)) target = 0f;
        // ПДД: yield at intersections. If another car is approaching the same intersection from a perpendicular
        // direction and is closer to it, slow down to ~3 m/s (creep forward, do not blow through).
        if (atIntersection && YieldTo(cars)) target = MathF.Min(target, 3f);
        Brake = target < Speed - 0.5f;

        Speed += Math.Clamp(target - Speed, -30f * dt, Type.Accel * 0.42f * dt);
        Pos += Fwd * Speed * dt;

        // Siren animation for police cruisers (chasing or parked alike).
        if (Police) SirenT += dt;

        Stuck = Speed < 0.5f ? Stuck + dt : 0f;
        Scared = MathF.Max(0f, Scared - dt);
    }

    /// <summary>ПДД rule: at an intersection, yield to a car that is already in it or arriving from the right.
    /// Returns true if this car should slow down.</summary>
    bool YieldTo(List<Car> cars)
    {
        Vector3 node = new(Tx * World.Step, 0f, Tz * World.Step);
        float myD = U.Dist2D(Pos, node);
        foreach (var c in cars)
        {
            if (c == this || !c.Ai) continue;
            // is the other car heading for the same node?
            Vector3 theirNode = new(c.Tx * World.Step, 0f, c.Tz * World.Step);
            if (U.Dist2D(theirNode, node) > 1f) continue;
            float theirD = U.Dist2D(c.Pos, node);
            // if they are closer, or already inside the intersection box, yield.
            if (theirD < myD - 1f) return true;
            if (theirD < 6f && (MathF.Abs(c.Dx) > 0 != MathF.Abs(Dx) > 0)) return true;   // perpendicular and overlapping
        }
        return false;
    }

    /// <summary>Something in front of the car (other car, pedestrian, the player on foot)?</summary>
    bool Blocked(List<Car> cars, List<Npc> npcs, Vector3 foot)
    {
        Vector3 f = Fwd, s = Side;
        float look = Type.Len * 0.5f + 3.8f + Speed * 0.9f;
        float hw = Type.Wid * 0.5f;

        bool Ahead(Vector3 p, float halfW)
        {
            Vector3 rel = p - Pos; rel.Y = 0f;
            float along = Vector3.Dot(rel, f);
            if (along < 1.5f || along > look) return false;
            return MathF.Abs(Vector3.Dot(rel, s)) < halfW;
        }

        foreach (var c in cars) if (c != this && Ahead(c.Pos, 1.6f + hw)) return true;
        foreach (var n in npcs) if (!n.Dead && Ahead(n.Pos, 0.75f + hw)) return true;
        return Ahead(foot, 0.85f + hw);
    }

    // ------------------------------------------------------------ player driving (arcade physics)
    public void UpdateDriven(float dt, World w, float throttle, float steer, bool handbrake)
    {
        Brake = (throttle < 0f && Speed > 0.5f) || handbrake;
        if (throttle > 0f) Speed += (Speed < 0f ? 45f : Type.Accel) * dt;
        else if (throttle < 0f) Speed -= (Speed > 0.5f ? 48f : 14f) * dt;
        else Speed -= Speed * 0.6f * dt;

        if (handbrake) Speed -= MathF.Sign(Speed) * MathF.Min(MathF.Abs(Speed), 55f * dt);
        Speed = Math.Clamp(Speed, -10f, Type.Top);

        float grip = Math.Clamp(MathF.Abs(Speed) / 5f, 0f, 1f) / (1f + MathF.Abs(Speed) * 0.045f);
        Yaw += steer * 2.4f * Type.Steer * grip * MathF.Sign(Speed) * dt;

        Vector3 np = Pos + Fwd * Speed * dt;
        if (w.Resolve(ref np, Type.Radius + 0.1f)) Speed *= 0.6f; // scrape along walls
        Pos = np;
    }

    // ------------------------------------------------------------ drawing
    public void Draw(Vector3 camPos)
    {
        float dist = U.Dist2D(Pos, camPos);
        float dt = MathF.Max(Raylib.GetFrameTime(), 0.001f);

        // ---- suspension lean, wheel spin and steering angle (visual only)
        float accel = Math.Clamp((Speed - lastSpeed) / dt, -35f, 35f);
        float yr = Math.Clamp(U.WrapPi(Yaw - lastYaw) / dt, -2.5f, 2.5f);
        lastSpeed = Speed;
        lastYaw = Yaw;
        float k = MathF.Min(1f, dt * 6f);
        pitch += (-accel * 0.0014f - pitch) * k;
        roll += (yr * Math.Clamp(Speed, -30f, 30f) * 0.0022f - roll) * k;
        wheel += Speed * dt / Type.WheelR;
        float sp = Speed >= 0f ? MathF.Max(Speed, 4f) : MathF.Min(Speed, -4f);
        float steerT = Math.Clamp(yr * 2.8f / sp, -0.5f, 0.5f);
        steerVis += (steerT - steerVis) * MathF.Min(1f, dt * 8f);

        Quaternion qb = Quaternion.CreateFromYawPitchRoll(Yaw, pitch, roll);
        Quaternion qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw);

        Gfx.Shadow(Pos, Yaw, Type.Wid + 0.2f, Type.Len + 0.3f);

        // ---- user model path: police cruisers prefer cop_car* models, everyone else uses car* models.
        int copNm = Gfx.Count(Gfx.CatCopCar);
        int nm = Gfx.Count(Gfx.CatCar);
        if (Police && copNm > 0)
        {
            int idx = Model >= 0 && Model < copNm ? Model : 0;
            Gfx.DrawCustom(Gfx.CatCopCar, idx, Pos, qb, Color.White);
            DrawSiren(qb, dist, true);
            return;
        }
        if (!Police && nm > 0)
        {
            if (Model < 0 || Model >= nm) Roll(true);      // models were loaded after this car was created
            Gfx.DrawCustom(Gfx.CatCar, Model, Pos, qb, Color.White);
            return;
        }
        // Police model could also be supplied as a civilian car_police.glb - fall back to it.
        if (Police && nm > 0 && Gfx.HasCarKind((int)CarKind.Police))
        {
            int idx = Model >= 0 && Model < nm ? Model : Gfx.RandomCarModel((int)CarKind.Police);
            if (idx >= 0) { Gfx.DrawCustom(Gfx.CatCar, idx, Pos, qb, Color.White); DrawSiren(qb, dist); return; }
        }

        bool mid = dist < 90f, nearL = dist < 45f;
        switch (Type.Kind)
        {
            case CarKind.Van: DrawVan(qb, qy, mid, nearL); break;
            case CarKind.Pickup: DrawPickup(qb, qy, mid, nearL); break;
            case CarKind.Truck: DrawTruck(qb, qy, mid, nearL); break;
            case CarKind.Bus: DrawBus(qb, qy, mid, nearL); break;
            case CarKind.Police: DrawPolice(qb, qy, mid, nearL); DrawSiren(qb, dist); break;
            default: DrawSedan(qb, qy, mid, nearL); break;
        }
    }

    /// <summary>Two-tone police livery: black body with white front doors, plus a light bar on the roof.</summary>
    void DrawPolice(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float L = Type.Len, W = Type.Wid;
        Color black = U.C(28, 30, 36), white = U.C(232, 232, 236);
        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40);

        Bq(qb, 0f, 0.62f, 0f, W, 0.50f, L, black, Gfx.M.Paint);               // lower body (black)
        Bq(qb, 0f, 0.95f, 1.35f, W, 0.16f, 1.60f, black, Gfx.M.Paint);        // hood
        Bq(qb, 0f, 0.97f, -1.62f, W, 0.16f, 1.10f, black, Gfx.M.Paint);       // trunk lid
        Bq(qb, 0f, 1.22f, -0.20f, 1.62f, 0.50f, 1.30f, glass, Gfx.M.Glass);   // cabin glass
        Bq(qb, 0f, 1.53f, -0.22f, 1.66f, 0.08f, 1.25f, black, Gfx.M.Paint);   // roof

        // white front doors (the classic "POLICE" panel). Sits on both sides of the cabin.
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Bq(qb, s * (W / 2f + 0.005f), 0.86f, -0.55f, 0.04f, 0.62f, 1.30f, white, Gfx.M.Paint);
            if (nearL)
            {
                // "POLICE" bar across the door (just a thin stripe, text is not rendered on the box body)
                Bq(qb, s * (W / 2f + 0.012f), 1.00f, -0.20f, 0.03f, 0.18f, 0.95f, U.C(20, 30, 90), Gfx.M.Generic);
            }
        }

        if (mid)
        {
            // windshield / rear window (raked)
            float wy = 0.46f, wz = 0.50f;
            Quaternion qw = qb * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Atan2(wy, wz));
            Gfx.BoxQ(Pq(qb, 0f, 1.26f, 0.70f), new Vector3(1.60f, 0.04f, MathF.Sqrt(wy * wy + wz * wz)), qw, glass, Gfx.M.Glass);
            float ry = 0.46f, rz = 0.355f;
            Quaternion qr = qb * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.Atan2(ry, rz));
            Gfx.BoxQ(Pq(qb, 0f, 1.26f, -1.02f), new Vector3(1.60f, 0.04f, MathF.Sqrt(ry * ry + rz * rz)), qr, glass, Gfx.M.Glass);

            Bq(qb, 0f, 0.46f, 2.20f, W, 0.22f, 0.16f, trim, Gfx.M.Rubber);    // front bumper (push bar)
            Bq(qb, 0f, 0.46f, -2.20f, W, 0.22f, 0.16f, trim, Gfx.M.Rubber);
            // simple push bar (chrome verticals) on the front bumper
            Bq(qb, -0.7f, 0.55f, 2.27f, 0.08f, 0.45f, 0.06f, U.C(180, 182, 188), Gfx.M.Metal);
            Bq(qb,  0.7f, 0.55f, 2.27f, 0.08f, 0.45f, 0.06f, U.C(180, 182, 188), Gfx.M.Metal);
        }

        // headlights / tail lights
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Fq(qb, s * 0.66f, 0.80f, 2.215f, 0.50f, 0.17f, 0.06f, U.C(255, 248, 205));
            // police tail lights: red + blue split
            bool leftRed = ((int)(SirenT * 4f) & 1) == 0;
            Color tailL = leftRed ? U.C(255, 30, 30) : U.C(40, 80, 255);
            Color tailR = leftRed ? U.C(40, 80, 255) : U.C(255, 30, 30);
            Fq(qb, s * 0.70f, 0.90f, -2.215f, 0.52f, 0.16f, 0.06f, s < 0 ? tailL : tailR);
        }

        if (nearL)
        {
            Bq(qb, 0f, 0.55f, 2.285f, 0.50f, 0.12f, 0.02f, U.C(225, 225, 215), Gfx.M.Generic);   // number plates
            Bq(qb, 0f, 0.55f, -2.285f, 0.50f, 0.12f, 0.02f, U.C(225, 225, 215), Gfx.M.Generic);
        }

        axSedan[0] = 1.42f;
        axSedan[1] = -1.42f;
        DrawWheels(qy, mid, nearL, axSedan);
    }

    /// <summary>Roof-mounted light bar with alternating red/blue flashes and a ground halo.</summary>
    void DrawSiren(Quaternion qb, float dist, bool onModel = false)
    {
        bool red = ((int)(SirenT * 6f) & 1) == 0;
        Color cA = red ? U.C(255, 30, 30) : U.C(40, 90, 255);
        Color cB = red ? U.C(40, 90, 255) : U.C(255, 30, 30);

        // light bar housing + two flashing halves
        // On the cop_car model the bar sits on the roof: roof top ~1.41 m, roof centre ~0.6 m behind the car's
        // centre and ~0.95 m wide. (The procedural car keeps the old numbers; those floated above the model's roof.)
        float by = onModel ? 1.44f : 1.62f, bz = onModel ? -0.60f : -0.20f;
        float bw = onModel ? 0.95f : 1.30f, bd = onModel ? 0.30f : 0.45f;
        float lx = bw * 0.245f, lw = bw * 0.42f;
        Bq(qb, 0f, by, bz, bw, 0.10f, bd, U.C(20, 20, 22), Gfx.M.Metal);
        Fq(qb, -lx, by + 0.06f, bz, lw, 0.05f, bd, cA);
        Fq(qb,  lx, by + 0.06f, bz, lw, 0.05f, bd, cB);

        // soft halo on the ground beneath the cruiser (visible from far away)
        if (dist < 60f)
        {
            float pulse = 0.6f + 0.4f * MathF.Sin(SirenT * 12f);
            Color halo = red ? U.C(255, 60, 60, 70) : U.C(60, 110, 255, 70);
            Vector3 haloPos = new(Pos.X, 0.05f, Pos.Z);
            Gfx.Flat(haloPos, new Vector3(3.4f * pulse, 0.06f, 5.5f * pulse), Yaw, halo);
        }
    }

    // ---- small helpers for the boxy bodies (position is in the car's own space)
    Vector3 Pq(Quaternion q, float x, float y, float z) => Pos + Vector3.Transform(new Vector3(x, y, z), q);

    void Bq(Quaternion q, float x, float y, float z, float w, float h, float l, Color c, int m) =>
        Gfx.BoxQ(Pq(q, x, y, z), new Vector3(w, h, l), q, c, m);

    void Fq(Quaternion q, float x, float y, float z, float w, float h, float l, Color c) =>
        Gfx.FlatQ(Pq(q, x, y, z), new Vector3(w, h, l), q, c);

    void Lights(Quaternion q, float x, float zf, float zr, float yf, float yr, float w, float h)
    {
        Color head = U.C(255, 248, 205);
        Color tail = Brake ? U.C(255, 30, 30) : U.C(165, 12, 16);
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Fq(q, s * x, yf, zf, w, h, 0.06f, head);
            Fq(q, s * x, yr, zr, w, h, 0.06f, tail);
        }
    }

    /// <summary>Wheels follow the ground, not the lean of the body. Axle 0 is the steering axle.</summary>
    void DrawWheels(Quaternion qy, bool mid, bool nearL, float[] axles)
    {
        float wr = Type.WheelR;
        float half = Type.Wid * 0.5f - 0.02f;
        for (int a = 0; a < axles.Length; a++)
        {
            for (int si = 0; si < 2; si++)
            {
                float sx = si == 0 ? -1f : 1f;
                Vector3 wc = Pos + Vector3.Transform(new Vector3(sx * half, wr, axles[a]), qy);
                Quaternion qs = a == 0 ? qy * Quaternion.CreateFromAxisAngle(Vector3.UnitY, steerVis) : qy;
                Quaternion qa = qs * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);   // cylinder axis -> car X

                Gfx.CylQ(wc, wr, 0.26f, qa, U.C(18, 18, 20), Gfx.M.Rubber);
                if (!mid) continue;
                Gfx.CylQ(wc, wr * 0.68f, 0.272f, qa, U.C(176, 182, 192), Gfx.M.Metal);
                if (!nearL) continue;
                Vector3 sc = wc + Vector3.Transform(new Vector3(sx * 0.14f, 0f, 0f), qs);
                for (int s = 0; s < 3; s++)
                {
                    Quaternion qk = qs * Quaternion.CreateFromAxisAngle(Vector3.UnitX, wheel + s * MathF.PI / 3f);
                    Gfx.BoxQ(sc, new Vector3(0.02f, 0.07f, wr * 1.3f), qk, U.C(28, 28, 32), Gfx.M.Metal);
                }
            }
        }
    }

    // ---- sedan, sports car, taxi, SUV: one body, stretched to the size of the class
    void DrawSedan(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float sx = Type.Wid / 1.9f, sy = Type.Hgt / 1.55f, sz = Type.Len / 4.4f;
        Vector3 P(float x, float y, float z) => Pos + Vector3.Transform(new Vector3(x * sx, y * sy, z * sz), qb);
        void B(float x, float y, float z, float w, float h, float l, Color c, int m) =>
            Gfx.BoxQ(P(x, y, z), new Vector3(w * sx, h * sy, l * sz), qb, c, m);
        void F(float x, float y, float z, float w, float h, float l, Color c) =>
            Gfx.FlatQ(P(x, y, z), new Vector3(w * sx, h * sy, l * sz), qb, c);

        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40);

        // ---- body
        B(0f, 0.62f, 0f, 1.90f, 0.50f, 4.40f, Col, Gfx.M.Paint);          // lower body
        B(0f, 0.95f, 1.35f, 1.84f, 0.16f, 1.60f, Col, Gfx.M.Paint);       // hood
        B(0f, 0.97f, -1.62f, 1.84f, 0.16f, 1.10f, Col, Gfx.M.Paint);      // trunk lid
        B(0f, 1.22f, -0.20f, 1.62f, 0.50f, 1.30f, glass, Gfx.M.Glass);    // cabin glass
        B(0f, 1.53f, -0.22f, 1.66f, 0.08f, 1.25f, Col, Gfx.M.Paint);      // roof

        if (mid)
        {
            float wy = 0.46f * sy, wz = 0.50f * sz;
            Quaternion qw = qb * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Atan2(wy, wz));
            Gfx.BoxQ(P(0f, 1.26f, 0.70f), new Vector3(1.60f * sx, 0.04f, MathF.Sqrt(wy * wy + wz * wz)), qw, glass, Gfx.M.Glass);   // windshield
            float ry = 0.46f * sy, rz = 0.355f * sz;
            Quaternion qr = qb * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.Atan2(ry, rz));
            Gfx.BoxQ(P(0f, 1.26f, -1.02f), new Vector3(1.60f * sx, 0.04f, MathF.Sqrt(ry * ry + rz * rz)), qr, glass, Gfx.M.Glass);   // rear window

            B(0f, 0.46f, 2.20f, 1.90f, 0.22f, 0.16f, trim, Gfx.M.Rubber);    // bumpers
            B(0f, 0.46f, -2.20f, 1.90f, 0.22f, 0.16f, trim, Gfx.M.Rubber);
            B(0f, 0.72f, 2.205f, 0.90f, 0.16f, 0.05f, U.C(10, 10, 12), Gfx.M.Rubber);   // grille

            if (Type.Kind == CarKind.Sport)
            {
                B(0f, 1.18f, -2.00f, 1.70f, 0.05f, 0.34f, trim, Gfx.M.Rubber);          // spoiler
                B(-0.55f, 1.07f, -2.00f, 0.06f, 0.20f, 0.10f, trim, Gfx.M.Rubber);
                B(0.55f, 1.07f, -2.00f, 0.06f, 0.20f, 0.10f, trim, Gfx.M.Rubber);
            }
            if (Type.Kind == CarKind.Taxi)
                F(0f, 1.68f, -0.20f, 0.50f, 0.18f, 0.30f, U.C(255, 244, 190));          // roof sign
            if (Type.Kind == CarKind.Suv)
            {
                B(-0.62f, 1.60f, -0.22f, 0.05f, 0.05f, 1.1f, trim, Gfx.M.Metal);        // roof rails
                B(0.62f, 1.60f, -0.22f, 0.05f, 0.05f, 1.1f, trim, Gfx.M.Metal);
            }
        }

        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;

            if (mid)
            {
                B(s * 0.82f, 1.22f, 0.38f, 0.07f, 0.50f, 0.10f, Col, Gfx.M.Paint);      // pillars
                B(s * 0.82f, 1.22f, -0.20f, 0.06f, 0.50f, 0.12f, Col, Gfx.M.Paint);
                B(s * 0.82f, 1.22f, -0.80f, 0.07f, 0.50f, 0.12f, Col, Gfx.M.Paint);
            }
            if (nearL)
            {
                B(s * 1.00f, 1.12f, 0.38f, 0.12f, 0.09f, 0.16f, Col, Gfx.M.Paint);      // mirrors
                B(s * 0.955f, 0.85f, -0.05f, 0.02f, 0.04f, 0.18f, U.C(190, 196, 206), Gfx.M.Metal);   // door handle
                B(s * 0.96f, 0.40f, 0f, 0.06f, 0.10f, 2.0f, trim, Gfx.M.Rubber);        // side skirt
                B(s * 0.97f, 0.72f, 1.42f, 0.16f, 0.20f, 0.95f, Col, Gfx.M.Paint);      // fender flares
                B(s * 0.97f, 0.72f, -1.42f, 0.16f, 0.20f, 0.95f, Col, Gfx.M.Paint);
                if (Type.Kind == CarKind.Taxi)
                    B(s * 0.965f, 0.66f, 0f, 0.02f, 0.07f, 3.0f, U.C(20, 20, 22), Gfx.M.Generic);   // checker stripe
            }

            // lights (emissive)
            F(s * 0.66f, 0.80f, 2.215f, 0.50f, 0.17f, 0.06f, U.C(255, 248, 205));
            F(s * 0.70f, 0.90f, -2.215f, 0.52f, 0.16f, 0.06f, Brake ? U.C(255, 30, 30) : U.C(165, 12, 16));
        }

        if (nearL)
        {
            B(0f, 0.55f, 2.285f, 0.50f, 0.12f, 0.02f, U.C(225, 225, 215), Gfx.M.Generic);   // number plates
            B(0f, 0.55f, -2.285f, 0.50f, 0.12f, 0.02f, U.C(225, 225, 215), Gfx.M.Generic);
        }

        axSedan[0] = 1.42f * sz;
        axSedan[1] = -1.42f * sz;
        DrawWheels(qy, mid, nearL, axSedan);
    }

    // ---- van: tall box with a short nose
    void DrawVan(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float L = Type.Len, W = Type.Wid, H = Type.Hgt;
        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40);

        Bq(qb, 0f, (0.3f + H) / 2f, -0.35f, W, H - 0.3f, L - 0.7f, Col, Gfx.M.Paint);        // box
        Bq(qb, 0f, 0.75f, L / 2f - 0.35f, W, 0.9f, 0.7f, Col, Gfx.M.Paint);                  // nose
        Bq(qb, 0f, H * 0.72f, L / 2f - 0.68f, W - 0.15f, H * 0.36f, 0.06f, glass, Gfx.M.Glass);   // windshield
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Bq(qb, s * (W / 2f + 0.005f), H * 0.72f, L / 2f - 1.45f, 0.04f, H * 0.34f, 1.3f, glass, Gfx.M.Glass);   // cab windows
            if (nearL) Bq(qb, s * (W / 2f + 0.12f), H * 0.58f, L / 2f - 0.9f, 0.10f, 0.18f, 0.12f, Col, Gfx.M.Paint);   // mirrors
        }
        if (mid)
        {
            Bq(qb, 0f, 0.50f, L / 2f + 0.05f, W, 0.24f, 0.16f, trim, Gfx.M.Rubber);          // bumpers
            Bq(qb, 0f, 0.50f, -L / 2f - 0.05f, W, 0.24f, 0.16f, trim, Gfx.M.Rubber);
        }
        Lights(qb, W / 2f - 0.35f, L / 2f + 0.01f, -L / 2f - 0.02f, 0.95f, 1.05f, 0.45f, 0.20f);
        DrawWheels(qy, mid, nearL, AxVan);
    }

    // ---- pickup: low body, glass cab, open bed
    void DrawPickup(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float L = Type.Len, W = Type.Wid;
        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40);

        Bq(qb, 0f, 0.78f, 0f, W, 0.70f, L, Col, Gfx.M.Paint);                               // lower body
        Bq(qb, 0f, 1.50f, 0.95f, W - 0.10f, 0.62f, 1.50f, glass, Gfx.M.Glass);              // cab glass
        Bq(qb, 0f, 1.85f, 0.95f, W - 0.05f, 0.08f, 1.58f, Col, Gfx.M.Paint);                // roof
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Bq(qb, s * (W / 2f - 0.06f), 1.34f, -1.35f, 0.12f, 0.42f, 2.5f, Col, Gfx.M.Paint);   // bed walls
            if (mid)
            {
                Bq(qb, s * 0.88f, 1.50f, 1.66f, 0.07f, 0.62f, 0.09f, Col, Gfx.M.Paint);     // cab pillars
                Bq(qb, s * 0.88f, 1.50f, 0.24f, 0.07f, 0.62f, 0.09f, Col, Gfx.M.Paint);
            }
            if (nearL) Bq(qb, s * (W / 2f + 0.1f), 1.32f, 1.55f, 0.10f, 0.09f, 0.16f, Col, Gfx.M.Paint);   // mirrors
        }
        Bq(qb, 0f, 1.34f, -L / 2f + 0.08f, W - 0.10f, 0.42f, 0.10f, Col, Gfx.M.Paint);      // tailgate
        Bq(qb, 0f, 1.34f, -0.15f, W - 0.10f, 0.42f, 0.10f, Col, Gfx.M.Paint);               // front wall of the bed
        if (mid)
        {
            Bq(qb, 0f, 0.55f, L / 2f + 0.05f, W, 0.22f, 0.16f, trim, Gfx.M.Rubber);         // bumpers
            Bq(qb, 0f, 0.55f, -L / 2f - 0.05f, W, 0.22f, 0.16f, trim, Gfx.M.Rubber);
            Bq(qb, 0f, 0.85f, L / 2f + 0.01f, 0.9f, 0.18f, 0.05f, U.C(10, 10, 12), Gfx.M.Rubber);   // grille
        }
        Lights(qb, W / 2f - 0.34f, L / 2f + 0.02f, -L / 2f - 0.02f, 0.90f, 0.95f, 0.46f, 0.17f);
        DrawWheels(qy, mid, nearL, AxPickup);
    }

    // ---- truck: cab-over with a long cargo box
    void DrawTruck(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float L = Type.Len, W = Type.Wid, H = Type.Hgt;
        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40), dark = U.C(40, 42, 48);
        float cl = L - 2.7f;                                                                 // length of the cargo box

        Bq(qb, 0f, 0.65f, -0.2f, 1.3f, 0.3f, L - 0.4f, dark, Gfx.M.Metal);                  // chassis
        Bq(qb, 0f, (0.85f + H) / 2f, -L / 2f + cl / 2f, W, H - 0.85f, cl, Col, Gfx.M.Paint);   // cargo box
        Bq(qb, 0f, 1.35f, L / 2f - 1.2f, W, 2.1f, 2.4f, Col, Gfx.M.Paint);                  // cab
        Bq(qb, 0f, 1.75f, L / 2f + 0.01f, W - 0.30f, 0.85f, 0.06f, glass, Gfx.M.Glass);     // windshield
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Bq(qb, s * (W / 2f + 0.005f), 1.75f, L / 2f - 1.0f, 0.04f, 0.80f, 1.2f, glass, Gfx.M.Glass);   // cab windows
            if (nearL) Bq(qb, s * (W / 2f + 0.14f), 1.8f, L / 2f - 0.3f, 0.10f, 0.35f, 0.12f, dark, Gfx.M.Metal);   // mirrors
        }
        if (mid)
        {
            Bq(qb, 0f, 0.55f, L / 2f + 0.05f, W, 0.30f, 0.20f, trim, Gfx.M.Rubber);         // bumpers
            Bq(qb, 0f, 0.60f, -L / 2f - 0.05f, W - 0.20f, 0.25f, 0.20f, trim, Gfx.M.Rubber);
        }
        Lights(qb, W / 2f - 0.40f, L / 2f + 0.01f, -L / 2f - 0.02f, 0.95f, 1.0f, 0.50f, 0.22f);
        DrawWheels(qy, mid, nearL, AxTruck);
    }

    // ---- bus: long box with a window strip and a lit route sign
    void DrawBus(Quaternion qb, Quaternion qy, bool mid, bool nearL)
    {
        float L = Type.Len, W = Type.Wid, H = Type.Hgt;
        Color trim = U.C(16, 16, 18), glass = U.C(22, 28, 40);

        Bq(qb, 0f, (0.45f + H) / 2f, 0f, W, H - 0.45f, L, Col, Gfx.M.Paint);                // body
        Bq(qb, 0f, 2.3f, L / 2f + 0.005f, W - 0.30f, 1.3f, 0.04f, glass, Gfx.M.Glass);      // windshield
        Bq(qb, 0f, 2.3f, -L / 2f - 0.005f, W - 0.50f, 0.9f, 0.04f, glass, Gfx.M.Glass);     // rear window
        Fq(qb, 0f, 3.05f, L / 2f + 0.02f, 1.6f, 0.28f, 0.04f, U.C(255, 170, 40));           // route sign
        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            Bq(qb, s * (W / 2f + 0.005f), 2.15f, 0f, 0.04f, 1.0f, L - 1.8f, glass, Gfx.M.Glass);   // window strip
            if (mid) Bq(qb, s * (W / 2f + 0.004f), 0.85f, 0f, 0.03f, 0.22f, L - 0.3f, U.C(235, 235, 235), Gfx.M.Generic);   // stripe
            if (nearL) Bq(qb, s * (W / 2f + 0.14f), 2.2f, L / 2f - 0.3f, 0.10f, 0.4f, 0.12f, trim, Gfx.M.Metal);          // mirrors
        }
        if (mid)
        {
            Bq(qb, 0f, 0.55f, L / 2f + 0.05f, W, 0.25f, 0.18f, trim, Gfx.M.Rubber);         // bumpers
            Bq(qb, 0f, 0.55f, -L / 2f - 0.05f, W, 0.25f, 0.18f, trim, Gfx.M.Rubber);
        }
        Lights(qb, W / 2f - 0.40f, L / 2f + 0.01f, -L / 2f - 0.02f, 0.85f, 0.9f, 0.50f, 0.20f);
        DrawWheels(qy, mid, nearL, AxBus);
    }
}

// =====================================================================================
//  NPC (pedestrian)
//  Walks on the sidewalk graph: nodes = the 4 corners of every intersection.
//  From a corner it can cross the road (2 ways) or walk along the block edge (2 ways).
// =====================================================================================
public class Npc
{
    static readonly Color[] Shirts =
    {
        U.C(200, 70, 56), U.C(58, 120, 180), U.C(214, 178, 60), U.C(70, 150, 98),
        U.C(200, 100, 160), U.C(226, 226, 222), U.C(100, 80, 168), U.C(220, 124, 44),
        U.C(34, 36, 44), U.C(130, 134, 142)
    };
    static readonly Color[] Pants =
    {
        U.C(46, 62, 104), U.C(28, 28, 32), U.C(150, 130, 96), U.C(78, 80, 88), U.C(60, 54, 48), U.C(36, 48, 84)
    };
    static readonly Color[] Hairs =
    {
        U.C(22, 18, 16), U.C(70, 44, 26), U.C(176, 146, 84), U.C(140, 140, 144), U.C(104, 48, 28), U.C(40, 30, 24)
    };
    static readonly Color[] Shoes = { U.C(22, 22, 24), U.C(210, 210, 206), U.C(84, 54, 34), U.C(40, 44, 60) };
    static readonly Color[] Skins = { U.C(240, 195, 160), U.C(200, 150, 110), U.C(140, 95, 65), U.C(95, 62, 42) };

    public Vector3 Pos;
    public float Yaw, Hp = 40f;
    public bool Dead;

    int I, J, SX, SZ, pI, pJ, pSX, pSZ;   // target node and previous node
    Vector3 target, threat;
    float off, panic, deadT, phase, blockedT, curSp;
    bool wasPanic;
    bool brave, wasAggro;               // a brave pedestrian hits back when you attack him up close
    public float Aggro;                 // > 0: chasing and punching the player
    float atkCool;
    HumanLook look;

    public Npc() { Respawn(new Vector3(-1e5f, 0f, -1e5f)); }

    Vector3 NodePos(int i, int j, int sx, int sz) =>
        new(i * World.Step + sx * (World.Side + off), 0f, j * World.Step + sz * (World.Side + off));

    public void Respawn(Vector3 avoid)
    {
        Dead = false; Hp = 40f; panic = 0f; blockedT = 0f; wasPanic = false;
        Aggro = 0f; wasAggro = false; atkCool = 0f;
        brave = U.R.NextDouble() < 0.35;
        off = U.Rf(-1.4f, 1.4f);
        for (int k = 0; k < 30; k++)
        {
            I = U.R.Next(World.N + 1); J = U.R.Next(World.N + 1);
            SX = U.R.Next(2) == 0 ? -1 : 1; SZ = U.R.Next(2) == 0 ? -1 : 1;
            Pos = NodePos(I, J, SX, SZ);
            if (U.Dist2D(Pos, avoid) > 50f) break;
        }
        pI = I; pJ = J; pSX = SX; pSZ = SZ;
        PickNext();
        Pos = Vector3.Lerp(Pos, target, U.Rf(0f, 1f));

        look = new HumanLook
        {
            Shirt = Shirts[U.R.Next(Shirts.Length)],
            Pants = Pants[U.R.Next(Pants.Length)],
            Skin = Skins[U.R.Next(Skins.Length)],
            Hair = Hairs[U.R.Next(Hairs.Length)],
            Shoes = Shoes[U.R.Next(Shoes.Length)],
            LongSleeve = U.R.Next(3) == 0,
            Glasses = U.R.Next(8) == 0,
            Variant = U.R.Next(1000)
        };
    }

    void PickNext()
    {
        var o = new List<(int i, int j, int sx, int sz)>(6);
        o.Add((I, J, -SX, SZ));      // cross the road in x
        o.Add((I, J, SX, -SZ));      // cross the road in z
        if (I + SX >= 0 && I + SX <= World.N) { o.Add((I + SX, J, -SX, SZ)); o.Add((I + SX, J, -SX, SZ)); }
        if (J + SZ >= 0 && J + SZ <= World.N) { o.Add((I, J + SZ, SX, -SZ)); o.Add((I, J + SZ, SX, -SZ)); }

        var filtered = o.Where(n => !(n.i == pI && n.j == pJ && n.sx == pSX && n.sz == pSZ)).ToList();
        if (filtered.Count > 0) o = filtered;

        var pick = o[U.R.Next(o.Count)];
        pI = I; pJ = J; pSX = SX; pSZ = SZ;
        I = pick.i; J = pick.j; SX = pick.sx; SZ = pick.sz;
        target = NodePos(I, J, SX, SZ);
    }

    void Rejoin()
    {
        int i = Math.Clamp((int)MathF.Round(Pos.X / World.Step), 0, World.N);
        int j = Math.Clamp((int)MathF.Round(Pos.Z / World.Step), 0, World.N);
        I = i; J = j;
        SX = Pos.X - i * World.Step >= 0f ? 1 : -1;
        SZ = Pos.Z - j * World.Step >= 0f ? 1 : -1;
        pI = I; pJ = J; pSX = SX; pSZ = SZ;
        target = NodePos(I, J, SX, SZ);
    }

    public void Scare(Vector3 from, float seconds = 6f)
    {
        if (Dead) return;
        panic = MathF.Max(panic, seconds);
        threat = from;
    }

    /// <summary>Returns true if this hit killed the pedestrian.</summary>
    public bool Hit(float dmg, Vector3 from)
    {
        if (Dead) return false;
        Hp -= dmg;
        if (Hp <= 0f) { Dead = true; deadT = 12f; Aggro = 0f; return true; }
        if (brave && U.Dist2D(Pos, from) < 25f) { Aggro = 15f; panic = 0f; }     // stands his ground and fights
        else Scare(from, 8f);
        return false;
    }

    public bool Marked;                 // the story target (Dutch): drawn with a beam, tougher than a normal pedestrian

    /// <summary>Puts the pedestrian at a spot and lets him pick up the sidewalk route from there.</summary>
    public void PlaceAt(Vector3 p)
    {
        Pos = new Vector3(p.X, 0f, p.Z);
        Aggro = 0f; panic = 0f; wasPanic = false; wasAggro = false;
        Rejoin();
    }

    /// <summary>A tough character: lots of health, always hits back, dresses in black.</summary>
    public void MakeBoss(float hp)
    {
        Hp = hp;
        brave = true;
        look.Shirt = U.C(24, 24, 28);
        look.Pants = U.C(18, 18, 22);
    }

    /// <summary>Gives the pedestrian a recognisable outfit (job targets).</summary>
    public void Dress(Color shirt, Color pants) { look.Shirt = shirt; look.Pants = pants; }

    /// <summary>Calms a fighting pedestrian (the player died / respawned).</summary>
    public void Calm() { Aggro = 0f; }

    /// <summary>Used when the player carjacks a car: the driver pops out next to it and runs away.</summary>
    public void Eject(Vector3 pos, Vector3 from)
    {
        Pos = pos; Pos.Y = 0f;
        Dead = false; Hp = 40f; blockedT = 0f; Aggro = 0f;
        Scare(from, 10f);
    }

    public void Update(float dt, World w, Vector3 playerPos, bool playerOnFoot = true, Action<float> onPunch = null)
    {
        if (Dead)
        {
            deadT -= dt;
            if (deadT <= 0f) Respawn(playerPos);
            return;
        }

        // ---- fighting back: run at the player and punch him
        if (Aggro > 0f)
        {
            Aggro -= dt;
            Vector3 tp = playerPos - Pos; tp.Y = 0f;
            float dd = tp.Length();
            if (!playerOnFoot || dd > 45f) Aggro = 0f;
            else
            {
                wasAggro = true;
                Vector3 ad = dd > 0.001f ? tp / dd : Vector3.UnitX;
                float asp = dd > 1.3f ? 4.4f : 0f;
                curSp = asp;
                Pos += ad * asp * dt;
                phase += asp * dt * 2.6f;
                Yaw = MathF.Atan2(ad.X, ad.Z);
                w.Resolve(ref Pos, 0.45f);
                atkCool -= dt;
                if (dd < 1.7f && atkCool <= 0f) { atkCool = 0.9f; onPunch?.Invoke(U.Rf(6f, 10f)); }
                return;
            }
        }
        if (wasAggro) { wasAggro = false; Rejoin(); }

        panic = MathF.Max(0f, panic - dt);
        bool fleeing = panic > 0f;
        if (!fleeing && wasPanic) Rejoin();
        wasPanic = fleeing;

        Vector3 dir;
        float sp;
        if (fleeing)
        {
            dir = Pos - threat; dir.Y = 0f;
            dir = dir.LengthSquared() < 0.01f ? Vector3.UnitX : Vector3.Normalize(dir);
            sp = 5.5f;
        }
        else
        {
            Vector3 to = target - Pos; to.Y = 0f;
            float d = to.Length();
            if (d < 0.8f) { PickNext(); to = target - Pos; to.Y = 0f; d = to.Length(); }
            dir = d > 0.001f ? to / d : Vector3.Zero;
            sp = 1.7f;
        }

        curSp = sp;
        Pos += dir * sp * dt;
        phase += sp * dt * (fleeing ? 2.4f : 3.3f);
        if (dir.LengthSquared() > 0.0001f) Yaw = MathF.Atan2(dir.X, dir.Z);

        bool hit = w.Resolve(ref Pos, 0.45f);
        blockedT = hit ? blockedT + dt : 0f;
        if (blockedT > 2f) Respawn(playerPos);
    }

    public void Draw(Vector3 camPos)
    {
        Vector3 p = Pos;
        p.Y += World.GroundY(p.X, p.Z);
        if (Dead)
        {
            if (Gfx.HasClip(Gfx.CatNpc, look.Variant, "dead"))
            {
                Gfx.Shadow(Pos, Yaw, 0.9f, 1.6f);
                Gfx.DrawAnim(Gfx.CatNpc, look.Variant, "dead", 0f, p, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw), Color.White);
                return;
            }
            Gfx.Box(U.Local(p, Yaw, 0f, 0.13f, 0.1f), new Vector3(0.50f, 0.22f, 0.70f), Yaw, look.Shirt);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.10f, -0.65f), new Vector3(0.36f, 0.20f, 0.75f), Yaw, look.Pants);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.14f, 0.60f), new Vector3(0.24f, 0.24f, 0.26f), Yaw, look.Skin);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.20f, 0.58f), new Vector3(0.25f, 0.10f, 0.27f), Yaw, look.Hair);
            Gfx.Shadow(Pos, Yaw, 0.9f, 1.6f);
            return;
        }

        Human.Draw(Pos, Yaw, phase, panic > 0f ? 1.0f : 0.7f, look, -1, U.Dist2D(Pos, camPos), curSp);
    }
}
