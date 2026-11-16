using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public class Game
{
    class Fx
    {
        public Vector3 A, B;
        public float Life;
        public bool Tracer;
        public Color Col;
        public float Size = 0.18f;      // radius of the spark / explosion sphere
    }

    static readonly Color Sky = U.C(14, 12, 32);

    World world;
    Player pl;
    readonly List<Car> cars = new();
    readonly List<Npc> npcs = new();
    readonly List<Fx> fx = new();
    readonly List<CopCar> copCars = new();
    readonly List<CopNpc> copNpcs = new();

    // ---- traffic-rules / wanted system
    float wrongSideT;          // seconds spent driving against traffic in the last check window
    float speedingT;           // seconds spent above the city speed limit
    float violationCheckT;     // accumulator for the periodic violation check
    float bustedT;             // > 0 while the BUSTED overlay is showing
    float copSpawnCool;        // cooldown before the next cop car is spawned
    bool wasWanted;            // previous frame's wanted state (for the first-cop grace period)

    // F8 cycles through model categories so you can rotate cop_npc, cop_car, car, player, npc in turn.
    static readonly int[] f8Cats = { Gfx.CatCopNpc, Gfx.CatCopCar, Gfx.CatCar, Gfx.CatPlayer, Gfx.CatNpc };
    int f8Idx = -1;

    Camera3D cam;
    float camYaw, camPitch = 0.28f;
    string hint = "", toast = "";
    float toastT;
    bool quit;
    bool started;               // a game was started or loaded this session (the main menu can resume it)
    float menuAng;
    bool firstPerson;
    bool paused;
    int skipMouse;
    bool scoped;                // sniper zoom (right mouse button)
    float fovCur = 72f;
    float autosaveT, saveFlashT;

    // ---- health / death
    bool dying;                 // the player is dead: WASTED screen, then he wakes up in a hospital
    float deadT;                // seconds of the WASTED screen left
    float hurtT;                // red screen flash after a hit (0..1)
    float carHitCool;           // a car cannot hurt you again for a moment after it hit you
    Action<float> punchCb;      // pedestrians that fight back call this when they land a punch

    // ================================================================= main loop
    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1280, 720, "Mini GTA - Las Vegas");
        Raylib.SetExitKey(KeyboardKey.Null);   // ESC is used in the casino, quit with F10
        ToggleFs();                            // start in fullscreen (F11 toggles)
        Gfx.Init();
        Sfx.Init();
        Missions.Say = Toast;
        Toast(Gfx.ScanModels());

        world = new World();
        pl = new Player(world.StartPos);
        for (int i = 0; i < 70; i++) { var c = new Car(); c.Respawn(pl.Pos); cars.Add(c); }   // was 32, scaled up for the 4x larger map
        for (int i = 0; i < 180; i++) npcs.Add(new Npc());                                    // was 90
        Wanted.Clear();
        punchCb = d => { Sfx.HitFlesh(pl.Pos); HurtPlayer(d); };

        Missions.OnSave = () => SaveGame(true);
        Missions.OnNewGame = NewGame;
        Missions.OnMainMenu = OpenMainMenu;
        Input.Load();
        MainMenu.NewGame = StartNewGame;
        MainMenu.Continue = ContinueGame;
        MainMenu.Quit = () => quit = true;
        toastT = 0f;
        Raylib.EnableCursor();
        MainMenu.Open(false);
        MenuCamera(0f);

        while (!Raylib.WindowShouldClose() && !quit)
        {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
            Update(dt);
            Draw();
        }
        if (started) SaveGame(false);    // closing the window / F10 saves the running game (never a game that was not started)
        Sfx.Shutdown();
        Raylib.CloseWindow();
    }

    // ================================================================= main menu
    void OpenMainMenu()
    {
        if (started) SaveGame(false);
        paused = false; bigMap = false;
        Raylib.EnableCursor();
        MainMenu.Open(started);
    }

    void StartNewGame()
    {
        started = true;
        MainMenu.Active = false;
        NewGame();
    }

    void ContinueGame()
    {
        if (started)
        {
            MainMenu.Active = false;
            Raylib.DisableCursor(); skipMouse = 3;
            return;
        }
        if (LoadGame())
        {
            started = true;
            MainMenu.Active = false;
            Raylib.DisableCursor(); skipMouse = 3;
            UpdateCamera(0f);
            Toast($"Progress loaded - ${pl.Money}");
        }
        else MainMenu.Msg = "The save file could not be read";
    }

    /// <summary>Slow flight over the city (or a still shot of the hall when the menu was opened inside a casino).</summary>
    void MenuCamera(float dt)
    {
        menuAng += dt * 0.07f;
        Vector3 pos, tgt;
        if (Interior.Active)
        {
            pos = Interior.C + new Vector3(8f * MathF.Sin(menuAng), 5.5f, -11f);
            tgt = Interior.C + new Vector3(0f, 2f, 4f);
        }
        else
        {
            Vector3 c = new(3 * World.Step + World.Side, 0f, 3 * World.Step + World.Side);
            pos = c + new Vector3(MathF.Cos(menuAng) * 95f, 38f + 6f * MathF.Sin(menuAng * 1.7f), MathF.Sin(menuAng) * 95f);
            tgt = c + new Vector3(0f, 6f, 0f);
        }
        fovCur = 62f;
        cam = new Camera3D { Position = pos, Target = tgt, Up = Vector3.UnitY, FovY = fovCur, Projection = CameraProjection.Perspective };
    }

    void ToggleFs()
    {
        if (Raylib.IsWindowFullscreen())
        {
            Raylib.ToggleFullscreen();
            Raylib.SetWindowSize(1280, 720);
        }
        else
        {
            int m = Raylib.GetCurrentMonitor();
            Raylib.SetWindowSize(Raylib.GetMonitorWidth(m), Raylib.GetMonitorHeight(m));
            Raylib.ToggleFullscreen();
        }
    }

    // ================================================================= update
    void Update(float dt)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F11)) ToggleFs();
        if (Raylib.IsKeyPressed(KeyboardKey.F10)) { quit = true; return; }
        if (saveFlashT > 0f) saveFlashT -= dt;
        if (Raylib.IsKeyPressed(KeyboardKey.F9)) Toast(Sfx.ToggleMute() ? "Sound off" : "Sound on");
        Sfx.Frame(dt, MainMenu.Active, Interior.Active, paused || bigMap || Shop.Active || Story.Talking || Jobs.MenuOpen, dying, pl.Car, NearestCop(), cam.Position, pl.Hp / Player.MaxHp);
        if (MainMenu.Active)
        {
            MainMenu.Update(dt);
            MenuCamera(dt);
            return;
        }
        if (Input.Pressed(Act.Save) && !Shop.Active) SaveGame(true);

        // ESC = pause menu with missions (inside a casino / gun shop ESC just leaves it)
        if (!Casino.Active && !Shop.Active && !Jobs.MenuOpen && !bigMap && Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            paused = !paused;
            if (paused) { Raylib.EnableCursor(); SaveGame(false); }
            else { Raylib.DisableCursor(); skipMouse = 3; }
        }
        if (paused)
        {
            if (toastT > 0f) toastT -= dt;
            return;
        }

        bool mapJustOpened = false;
        if (!bigMap && Input.Pressed(Act.Map) && !Story.Talking && !Jobs.MenuOpen && !Shop.Active && !Casino.Active && !Interior.Active && !dying)
        {
            bigMap = true;
            mapJustOpened = true;   // the same M press must not also close the map below
            Raylib.EnableCursor();
            Raylib.ShowCursor();
            Raylib.SetMousePosition(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
        }
        if (Input.Pressed(Act.View)) { firstPerson = !firstPerson; Toast(firstPerson ? "First person" : "Third person"); }
        if (Raylib.IsKeyPressed(KeyboardKey.F6)) Toast(Gfx.ScanModels());
        if (Raylib.IsKeyPressed(KeyboardKey.F8))
        {
            // F8 cycles through loaded model categories. Each press rotates the next one by 90 deg.
            // Order: cop_npc, cop_car, car, player, npc. Press repeatedly until the right one is fixed.
            for (int i = 0; i < f8Cats.Length; i++)
            {
                f8Idx++;
                if (f8Idx >= f8Cats.Length) f8Idx = 0;
                if (Gfx.Count(f8Cats[f8Idx]) > 0) break;
            }
            Toast(Gfx.RotateFix(f8Cats[f8Idx]));
        }

        if (Story.Talking)
        {
            // the city is frozen while somebody is talking
            Story.UpdateTalk(dt);
            if (!Story.Talking) skipMouse = 3;
            if (toastT > 0f) toastT -= dt;
            if (Story.Talking && Story.Shot(out Vector3 sp, out Vector3 st))
            {
                if (Interior.Active) sp = Interior.ClampCam(sp);
                fovCur = 60f;
                cam = new Camera3D { Position = sp, Target = st, Up = Vector3.UnitY, FovY = fovCur, Projection = CameraProjection.Perspective };
            }
            else UpdateCamera(dt);
            return;
        }

        if (Jobs.MenuOpen)
        {
            Jobs.UpdateMenu(world, npcs, cars, pl);
            if (!Jobs.MenuOpen) skipMouse = 3;
            if (toastT > 0f) toastT -= dt;
            if (Story.Talking) return;
            return;
        }

        if (bigMap)
        {
            // the city is frozen while the map is open (click on it to set a waypoint)
            if (!mapJustOpened && (Raylib.IsKeyPressed(KeyboardKey.Escape) || Input.Pressed(Act.Map))) CloseMap();
            if (bigMap && Raylib.IsCursorHidden()) { Raylib.EnableCursor(); Raylib.ShowCursor(); }
            if (toastT > 0f) toastT -= dt;
            return;
        }

        if (Shop.Active)
        {
            // the city is frozen while you shop
            Shop.Update(dt, pl);
            if (!Shop.Active) { Raylib.DisableCursor(); skipMouse = 3; SaveGame(false); }
            if (toastT > 0f) toastT -= dt;
            return;
        }

        if (Casino.Active)
        {
            Casino.Update(dt, pl);
            if (Interior.Active) { if (toastT > 0f) toastT -= dt; }
            else AdvanceWorld(dt);
            return;
        }

        Vector2 md = Raylib.GetMouseDelta();
        if (skipMouse > 0) { skipMouse--; md = Vector2.Zero; }
        float sens = scoped ? 0.3f : 1f;          // slower aiming while zoomed in
        camYaw -= md.X * 0.0022f * sens;
        camPitch = Math.Clamp(camPitch + md.Y * 0.0022f * sens, -0.5f, 1.2f);

        if (dying) UpdateDeath(dt);
        else if (pl.Car == null) OnFoot(dt); else { scoped = false; Driving(dt); }
        if (Interior.Active) { if (toastT > 0f) toastT -= dt; }   // the city is frozen while you are in the casino
        else AdvanceWorld(dt);

        autosaveT += dt;
        if (autosaveT >= 30f) { autosaveT = 0f; SaveGame(false); }
        if (dying) hint = ""; else Interact();
        if (!dying && !Interior.Active) Story.Update(dt, pl, world, npcs);
        if (!dying && !Interior.Active) Jobs.Update(pl, world);
        if (wp != null && !Interior.Active)
        {
            Vector3 me = pl.Car != null ? pl.Car.Pos : pl.Pos;
            if (U.Dist2D(me, wp.Value) < (pl.Car != null ? 12f : 5f)) { wp = null; Toast("Waypoint reached"); }
        }
        UpdateCamera(dt);
    }

    void AdvanceWorld(float dt)
    {
        Vector3 foot = pl.Car == null ? pl.Pos : new Vector3(1e6f, 0f, 1e6f);

        if (!dying) pl.Regen(dt);
        if (hurtT > 0f) hurtT = MathF.Max(0f, hurtT - dt * 1.4f);
        if (carHitCool > 0f) carHitCool -= dt;

        foreach (var c in cars)
        {
            // The player's car is driven by input; cop cars still being chased by their CopCar wrapper
            // are driven by CopCar.Update below. Both are skipped here so they do not move twice.
            // An ex-cop car that the player has abandoned (Police=true, Ai=false, Player=false) falls through
            // to the abandoned-car branch so it rolls to a stop and gets recycled into traffic.
            if (c.Player) continue;
            if (c.Police && c.Ai) continue;
            if (c.Ai)
            {
                c.UpdateAi(dt, cars, npcs, foot);
                if (c.Stuck > 6f) c.Respawn(pl.Pos);
            }
            else
            {
                // abandoned car: rolls to a stop; recycled into traffic when far away
                c.Speed *= MathF.Max(0f, 1f - 3f * dt);
                c.Pos += c.Fwd * c.Speed * dt;
                if (!c.Owned && U.Dist2D(c.Pos, pl.Pos) > 260f) c.Respawn(pl.Pos);   // was 170, raised for the larger map
            }
        }

        // a moving car that reaches the player on foot hurts him
        if (pl.Car == null && !dying && carHitCool <= 0f && pl.Pos.Y < 0.9f)
        {
            foreach (var c in cars)
            {
                if (c.Player) continue;
                float sp = MathF.Abs(c.Speed);
                if (sp < 4f || U.Dist2D(c.Pos, pl.Pos) > c.Type.Radius * 0.8f + 0.4f) continue;
                carHitCool = 0.8f;
                c.Speed *= 0.6f;
                Toast("Hit by a car!");
                Sfx.Crash(0.6f);
                HurtPlayer(MathF.Min(70f, sp * 2.5f));
                break;
            }
        }

        foreach (var n in npcs) n.Update(dt, world, pl.Pos, pl.Car == null && !dying, punchCb);

        // ---- cop cars chase the player
        bool copNearby = false;
        for (int i = copCars.Count - 1; i >= 0; i--)
        {
            var cc = copCars[i];
            cc.Update(dt, pl.Car, foot, world, cars, npcs, (p, yaw) =>
            {
                copNpcs.Add(new CopNpc(p, yaw));
                Toast("Officer getting out!");
            });
            if (!cc.Active)
            {
                cars.Remove(cc.Car);
                copCars.RemoveAt(i);
            }
            else if (U.Dist2D(cc.Car.Pos, pl.Pos) < 70f) copNearby = true;
        }

        // ---- cop npcs (officers on foot) chase the player
        bool bustedThisFrame = false;
        for (int i = copNpcs.Count - 1; i >= 0; i--)
        {
            var cn = copNpcs[i];
            cn.Update(dt, pl.Pos, pl.Car != null, world, () => { if (dying) return; bustedThisFrame = true; OnBusted(); }, CopShoot, Wanted.Stars >= 3);
            if (bustedThisFrame) break;        // OnBusted cleared the lists - stop iterating
            if (!cn.Active) copNpcs.RemoveAt(i);
            else if (U.Dist2D(cn.Pos, pl.Pos) < 40f) copNearby = true;
        }

        // ---- traffic-rules check (player driving)
        if (pl.Car != null) DrivingRules(dt);

        // ---- wanted tick: spawn cop cars when the player is wanted, decay heat when they are not seen
        Wanted.Tick(dt, copNearby);
        Sfx.WantedLevel(Wanted.Stars);
        // When wanted level first goes above zero, give the player a 8-second grace period before the first cop arrives.
        if (Wanted.Stars > 0 && !wasWanted)
            copSpawnCool = MathF.Max(copSpawnCool, 8f);
        wasWanted = Wanted.Stars > 0;
        copSpawnCool -= dt;
        // Cap active cop cars at 3 (even at 5 stars) so the player always has a chance to escape.
        int maxCops = Math.Min(Wanted.Stars, 3);
        // Fast getaway: if the player is driving flat out (above ~120 km/h = 33 m/s) the radio
        // cannot keep up - no new cops spawn. The existing chasers (max 68 km/h) fall behind,
        // the distance opens past 70 m, heat decays and the stars drop. Speed is a real escape.
        float escapeSpd = pl.Car != null ? MathF.Abs(pl.Car.Speed) : 0f;
        if (Wanted.Stars > 0 && copSpawnCool <= 0f && copCars.Count < maxCops && escapeSpd < 33f)
        {
            SpawnCopCar();
            copSpawnCool = 7f;       // longer gap between reinforcements
        }
        // clear cops the moment the wanted level drops to zero
        if (Wanted.Stars == 0 && (copCars.Count > 0 || copNpcs.Count > 0))
        {
            foreach (var cc in copCars) cars.Remove(cc.Car);
            copCars.Clear();
            copNpcs.Clear();
        }

        for (int i = fx.Count - 1; i >= 0; i--)
        {
            fx[i].Life -= dt;
            if (fx[i].Life <= 0f) fx.RemoveAt(i);
        }
        if (toastT > 0f) toastT -= dt;
        if (bustedT > 0f) bustedT -= dt;
    }

    // ------------------------------------------------------------------ traffic rules
    // While driving we check whether the player is on the wrong side of the road or
    // exceeding the city speed limit. These are the two ПДД rules that raise heat.
    void DrivingRules(float dt)
    {
        var c = pl.Car;
        if (c == null) return;

        // Wrong-side check: figure out which road segment the car is on and which direction is "with traffic".
        // Roads lie on the lines x = i*Step (running in Z) and z = j*Step (running in X).
        bool wrong = IsOnWrongSide(c.Pos, c.Yaw, c.Speed);
        if (wrong) wrongSideT += dt; else wrongSideT = MathF.Max(0f, wrongSideT - dt * 0.5f);

        // Speeding check: above the city speed limit (50 km/h = ~14 m/s) for a sustained time.
        bool fast = MathF.Abs(c.Speed) > Car.CitySpeedLimit + 2f;
        if (fast) speedingT += dt; else speedingT = MathF.Max(0f, speedingT - dt * 0.5f);
        c.WrongSide = wrong;

        // Sample every 1.5s so heat grows gently - minor traffic offences should NOT spawn cops on their own.
        violationCheckT += dt;
        if (violationCheckT >= 1.5f)
        {
            violationCheckT = 0f;
            // Small amounts: 0.4/sec. It takes ~10 seconds of sustained speeding to reach 1 star.
            if (wrongSideT > 0.8f) { Wanted.Add(0.4f); Toast("Wrong side of the road!"); }
            if (speedingT > 1.5f)  { Wanted.Add(0.4f); Toast("Speeding!"); }
            wrongSideT = 0f;
            speedingT = 0f;
        }
    }

    /// <summary>Is the given car on the wrong side of the road for its direction of travel?
    /// Right-hand traffic: +X travel (west) should be on the +Z (north) side; +Z travel (north) on the -X (east) side.</summary>
    static bool IsOnWrongSide(Vector3 pos, float yaw, float speed)
    {
        if (MathF.Abs(speed) < 1f) return false;       // standing still is never a violation
        Vector3 fwd = new(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        Vector3 dir = speed >= 0 ? fwd : -fwd;          // reverse gear: opposite direction
        float ax = MathF.Abs(dir.X), az = MathF.Abs(dir.Z);

        // Pick the dominant axis - the road this car is travelling along.
        if (ax > az)
        {
            // travelling along X: road centreline is z = j*Step.
            // Right side for +X travel (west) is +Z (north). Wrong side: sideZ < -0.5 (south).
            // Right side for -X travel (east) is -Z (south). Wrong side: sideZ > 0.5 (north).
            float j = MathF.Round(pos.Z / World.Step);
            float centreZ = j * World.Step;
            float sideZ = pos.Z - centreZ;
            return dir.X > 0 ? sideZ < -0.5f : sideZ > 0.5f;
        }
        else
        {
            // travelling along Z: road centreline is x = i*Step.
            // Right side for +Z travel (north) is -X (east). Wrong side: sideX > 0.5 (west).
            // Right side for -Z travel (south) is +X (west). Wrong side: sideX < -0.5 (east).
            float i = MathF.Round(pos.X / World.Step);
            float centreX = i * World.Step;
            float sideX = pos.X - centreX;
            return dir.Z > 0 ? sideX > 0.5f : sideX < -0.5f;
        }
    }

    // ------------------------------------------------------------------ spawning cops
    void SpawnCopCar()
    {
        // Spawn BEHIND the player (opposite their direction of travel, plus/minus 50 degrees).
        // The old version picked a random intersection in a circle around the player, which
        // often dropped a fresh cop car right on the road AHEAD - at 165 km/h the player kept
        // driving into brand-new cops and could never open a gap. Now reinforcements always
        // appear behind, so driving fast genuinely shakes the chase.
        Vector3 target = pl.Car != null ? pl.Car.Pos : pl.Pos;
        bool onFoot = pl.Car == null;

        // Direction of travel (handles reversing: motion = Fwd * sign(speed)).
        Vector3 motion = Vector3.Zero;
        if (!onFoot && MathF.Abs(pl.Car.Speed) > 3f) motion = pl.Car.Fwd * MathF.Sign(pl.Car.Speed);

        // Distance: far enough behind that a fresh spawn does not freeze the heat decay
        // (decay needs no cop within 70 m).
        float dist = motion == Vector3.Zero ? U.Rf(60f, 100f) : U.Rf(95f, 140f);

        Vector3 offset;
        if (motion != Vector3.Zero)
        {
            // Base direction: opposite of travel, rotated by a random +/- 50 degrees.
            Vector3 back = -motion;
            float a = U.Rf(-0.87f, 0.87f);
            float ca = MathF.Cos(a), sa = MathF.Sin(a);
            offset = new Vector3(back.X * ca + back.Z * sa, 0f, -back.X * sa + back.Z * ca);
            offset = Vector3.Normalize(offset);
        }
        else
        {
            // Player on foot or parked: cops can arrive from any direction, but never too close.
            float ang = U.Rf(0f, MathF.Tau);
            offset = new Vector3(MathF.Cos(ang), 0f, MathF.Sin(ang));
        }

        Vector3 spawn = target + offset * dist;
        for (int tries = 0; tries < 20; tries++)
        {
            // Snap to the nearest road intersection that is not inside a building.
            int i = Math.Clamp((int)MathF.Round(spawn.X / World.Step), 0, World.N);
            int j = Math.Clamp((int)MathF.Round(spawn.Z / World.Step), 0, World.N);
            Vector3 cand = new Vector3(i * World.Step, 0f, j * World.Step);
            if (!world.Inside(cand + new Vector3(0f, 1f, 0f))) { spawn = cand; break; }
            // Intersection blocked: push the candidate further out and try again.
            spawn = target + offset * (dist + 15f * (tries + 1));
        }
        float spawnYaw = MathF.Atan2((target - spawn).X, (target - spawn).Z);
        var cc = new CopCar(spawn, spawnYaw);
        copCars.Add(cc);
        cars.Add(cc.Car);
    }

    // ------------------------------------------------------------------ busted
    float NearestCop()
    {
        float best = 9999f;
        foreach (var cc in copCars)
            if (cc.Active) best = MathF.Min(best, U.Dist2D(cc.Car.Pos, pl.Pos));
        return best;
    }

    void OnBusted()
    {
        if (bustedT > 0f || dying) return;       // already busted this beat (or dead)
        Sfx.Busted();
        bustedT = 3f;
        int loss = pl.Money / 2;
        pl.Money -= loss;
        Wanted.Clear();
        // clear the cops
        foreach (var cc in copCars) cars.Remove(cc.Car);
        copCars.Clear();
        copNpcs.Clear();
        // throw the player out of the car, like a real arrest
        if (pl.Car != null)
        {
            pl.Car.Player = false;
            pl.Car.Ai = false;
            pl.Car.Speed = 0f;
            pl.Pos = pl.Car.Pos + pl.Car.Side * 2.8f;
            world.Resolve(ref pl.Pos, 0.5f);
            pl.Yaw = pl.Car.Yaw;
            pl.Car = null;
        }
        Toast($"BUSTED! Lost ${loss}");
    }

    /// <summary>An officer fires at the player: walls block the shot, distance / movement / being in a car make it miss more often.</summary>
    void CopShoot(CopNpc cn)
    {
        if (dying) return;
        Vector3 from = cn.Pos + new Vector3(0f, 1.4f, 0f);
        Vector3 to = pl.Pos + new Vector3(0f, pl.Car != null ? 1.0f : 1.1f, 0f);
        float d = U.Dist2D(cn.Pos, pl.Pos);

        float chance = Math.Clamp(0.85f - d * 0.035f, 0.25f, 0.75f);
        if (pl.Car != null) chance *= 0.65f;                      // the car is a moving target and some cover
        else if (pl.Speed > 6f) chance *= 0.6f;                   // sprinting
        else if (pl.Moving) chance *= 0.8f;
        bool hit = !LineBlocked(from, to) && U.R.NextDouble() < chance;

        Vector3 end = hit ? to : to + new Vector3(U.Rf(-1.4f, 1.4f), U.Rf(-0.6f, 0.8f), U.Rf(-1.4f, 1.4f));
        fx.Add(new Fx { A = from, B = end, Life = 0.06f, Tracer = true, Col = U.C(255, 240, 150) });
        Sfx.CopShot(from);
        if (hit)
        {
            float k = 1f + 0.1f * MathF.Max(0, Wanted.Stars - 2);      // more stars = better trained officers
            HurtPlayer(U.Rf(7f, 11f) * k);
        }

        // gunfire scares pedestrians
        foreach (var n in npcs)
            if (!n.Dead && U.Dist2D(n.Pos, pl.Pos) < 30f) n.Scare(pl.Pos);
    }

    bool LineBlocked(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.01f) return false;
        d /= len;
        foreach (var bl in world.Buildings)
            if (U.RayAabb(a, d, new Vector3(bl.MinX, 0f, bl.MinZ), new Vector3(bl.MaxX, bl.H, bl.MaxZ), out float t) && t < len) return true;
        return false;
    }

    // ================================================================= health and death
    void HurtPlayer(float dmg)
    {
        if (dying || dmg <= 0f || Interior.Active) return;
        pl.Hurt(dmg);
        if (!pl.Dead) Sfx.Hurt(dmg);
        hurtT = MathF.Min(1f, hurtT + 0.3f + dmg * 0.012f);
        if (pl.Dead) Die();
    }

    void Die()
    {
        if (dying) return;
        dying = true;
        Sfx.Die();
        deadT = 4f;
        pl.Hp = 0f;
        hurtT = 1f;
        scoped = false; pl.Moving = false; pl.Speed = 0f; pl.Reload = 0f;
        if (pl.Car != null)
        {
            // you drop out of the car; it is left standing like after an arrest
            var c = pl.Car;
            c.Player = false; c.Ai = false; c.Speed = 0f;
            pl.Pos = c.Pos + c.Side * 2.8f;
            pl.Pos.Y = 0f;
            world.Resolve(ref pl.Pos, 0.5f);
            pl.Yaw = c.Yaw;
            pl.Car = null;
        }
    }

    void UpdateDeath(float dt)
    {
        pl.Moving = false; pl.Speed = 0f;
        pl.VelY -= 20f * dt;
        pl.Pos.Y = MathF.Max(0f, pl.Pos.Y + pl.VelY * dt);
        if (pl.Pos.Y <= 0f) pl.VelY = 0f;
        deadT -= dt;
        if (deadT <= 0f) Respawn();
    }

    /// <summary>You wake up at the nearest hospital: full health, no armour, a bill, and the police lose track of you.</summary>
    void Respawn()
    {
        Building h = null;
        float best = float.MaxValue;
        foreach (var b in world.Hospitals)
        {
            float d = U.Dist2D(b.Door, pl.Pos);
            if (d < best) { best = d; h = b; }
        }
        int bill = Math.Min(pl.Money, Math.Max(100, pl.Money / 4));
        pl.Money -= bill;
        pl.Hp = Player.MaxHp; pl.Armor = 0f; pl.SinceHurt = 99f;
        pl.Car = null; pl.VelY = 0f; pl.Cool = 0f; pl.Reload = 0f;
        pl.Pos = h != null ? new Vector3(h.Door.X, 0f, h.Door.Z) : world.StartPos;
        pl.Yaw = MathF.PI; camYaw = MathF.PI; camPitch = 0.28f;
        dying = false; deadT = 0f; hurtT = 0f;

        Wanted.Clear(); wasWanted = false;
        foreach (var cc in copCars) cars.Remove(cc.Car);
        copCars.Clear(); copNpcs.Clear();
        foreach (var n in npcs) n.Calm();
        Toast($"You woke up in the hospital. Bill: -${bill}, armor lost");
    }

    /// <summary>E at a hospital door: pays 2 $ per missing hit point (as much as you can afford).</summary>
    void Treat()
    {
        int missing = (int)MathF.Ceiling(Player.MaxHp - pl.Hp);
        if (missing <= 0) { Toast("You are in perfect health"); return; }
        int afford = pl.Money / Player.HealPricePerHp;
        if (afford <= 0) { Toast("You have no money for treatment"); return; }
        int heal = Math.Min(missing, afford);
        int cost = heal * Player.HealPricePerHp;
        pl.Money -= cost;
        pl.Hp = MathF.Min(Player.MaxHp, pl.Hp + heal);
        Sfx.Heal();
        Toast(heal == missing ? $"Patched up: -${cost}" : $"Not enough money - partial treatment: +{heal} HP for ${cost}");
    }

    void OnFoot(float dt)
    {
        Vector3 f = new(MathF.Sin(camYaw), 0f, MathF.Cos(camYaw));
        Vector3 r = new(-MathF.Cos(camYaw), 0f, MathF.Sin(camYaw));
        Vector3 mv = Vector3.Zero;
        if (Input.Down(Act.Forward)) mv += f;
        if (Input.Down(Act.Back)) mv -= f;
        if (Input.Down(Act.Right)) mv += r;
        if (Input.Down(Act.Left)) mv -= r;

        pl.Moving = mv.LengthSquared() > 0.01f;
        pl.Speed = 0f;
        if (pl.Moving)
        {
            mv = Vector3.Normalize(mv);
            float sp = Input.Down(Act.Sprint) ? 8.5f : 4.6f;
            pl.Pos += mv * sp * dt;
            pl.Speed = sp;
            pl.Phase += sp * dt * (sp > 6f ? 1.7f : 2.4f);   // stride length matches the baked run / sprint cycles
            float want = MathF.Atan2(mv.X, mv.Z);
            pl.Yaw += U.WrapPi(want - pl.Yaw) * MathF.Min(1f, dt * 12f);
        }
        if (Interior.Active) Interior.Resolve(ref pl.Pos, 0.5f);
        else world.Resolve(ref pl.Pos, 0.5f);
        Story.PushOut(ref pl.Pos, 0.5f);
        Sfx.Footsteps(dt, pl.Moving, pl.Speed > 6f, pl.Pos.Y <= 0.02f, Interior.Active);

        // ---- jump (Space) + gravity
        if (Input.Pressed(Act.Jump) && pl.Pos.Y <= 0.001f) pl.VelY = 7f;
        pl.VelY -= 20f * dt;
        pl.Pos.Y += pl.VelY * dt;
        if (pl.Pos.Y <= 0f) { pl.Pos.Y = 0f; pl.VelY = 0f; }

        // ---- weapons
        scoped = false;
        if (Interior.Active) return;   // no guns in the casino
        if (pl.Weapons.Count == 0)
        {
            pl.Armed = false;
            if (Input.Pressed(Act.Holster))
                Toast("You have no weapons - buy some at an AMMU-NATION (red signs, red squares on the map)");
            Fists(dt);
            return;
        }

        int prev = pl.Cur;
        bool wasArmed = pl.Armed;
        if (Input.Pressed(Act.Holster))
        {
            pl.Armed = !pl.Armed;
            pl.Reload = 0f;
            Toast(pl.Armed ? "Weapon out" : "Weapon holstered");
            Sfx.Draw();
        }
        for (int k = 0; k < Arsenal.SlotNames.Length; k++)
            if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.One + k)) && !pl.SelectSlot(k))
                Toast($"You have no weapon in slot {k + 1} ({Arsenal.SlotNames[k]})");
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel > 0f) pl.Cycle(-1);
        else if (wheel < 0f) pl.Cycle(1);
        if (pl.Cur != prev) pl.Reload = 0f;
        if (pl.Armed && (pl.Cur != prev || !wasArmed) && !Input.Pressed(Act.Holster)) { Toast(pl.W.Name); Sfx.Draw(); }

        if (!pl.Armed) { Fists(dt); return; }
        Weapon w = pl.W;
        scoped = w.Slot == WSlot.Sniper && Raylib.IsMouseButtonDown(MouseButton.Right);
        pl.Cool -= dt;

        if (pl.Reload > 0f)
        {
            pl.Reload -= dt;
            if (pl.Reload <= 0f)
            {
                int take = Math.Min(w.Mag - w.Ammo, w.Reserve);
                w.Ammo += take;
                w.Reserve -= take;
                Sfx.ReloadDone(w);
            }
        }

        if (Input.Pressed(Act.Reload) && pl.Reload <= 0f && w.Ammo < w.Mag && w.Reserve > 0)
        {
            pl.Reload = w.ReloadTime;
            Sfx.Reload(w);
        }

        bool fire = w.Auto ? Raylib.IsMouseButtonDown(MouseButton.Left) : Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (fire)
        {
            pl.Yaw += U.WrapPi(camYaw - pl.Yaw) * MathF.Min(1f, dt * 20f);
            if (pl.Cool <= 0f && pl.Reload <= 0f)
            {
                if (w.Melee) Swing(w);
                else if (w.Ammo > 0) Shoot();
                else if (w.Reserve > 0) { pl.Reload = w.ReloadTime; Sfx.Reload(w); }
                else if (Raylib.IsMouseButtonPressed(MouseButton.Left)) { Toast($"{w.Name}: out of ammo - buy more at a gun shop"); Sfx.Empty(); }
            }
        }
    }

    void Driving(float dt)
    {
        var c = pl.Car;
        float th = (Input.Down(Act.Forward) ? 1f : 0f) - (Input.Down(Act.Back) ? 1f : 0f);
        float st = (Input.Down(Act.Left) ? 1f : 0f) - (Input.Down(Act.Right) ? 1f : 0f);
        float prevSpd = MathF.Abs(c.Speed);
        c.UpdateDriven(dt, world, th, st, Input.Down(Act.Jump));
        Missions.Event(MKind.Drive, MathF.Abs(c.Speed) * dt);

        // a crash: a sudden drop of speed (more than normal braking can do) hurts the driver
        float drop = prevSpd - MathF.Abs(c.Speed);
        float thr = 150f * dt;
        if (drop > thr) { Sfx.Crash(Math.Clamp((drop - thr) / 15f, 0.2f, 1f)); HurtPlayer((drop - thr) * 3f); if (dying) return; }

        // run over pedestrians
        foreach (var n in npcs)
        {
            if (n.Dead || U.Dist2D(n.Pos, c.Pos) > 2.5f) continue;
            if (MathF.Abs(c.Speed) > 5f)
            {
                Sfx.HitFlesh(n.Pos, 1.2f); Sfx.Scream(n.Pos);
                if (n.Hit(MathF.Abs(c.Speed) * 7f, c.Pos))
                {
                    Reward(U.R.Next(20, 200));
                    Missions.Event(MKind.Kill);
                    Missions.Event(MKind.RunOver);
                    // ПДД: hitting a pedestrian is a serious violation - 1 star immediately.
                    Wanted.Add(4f);
                    Toast("Hit and run! Cops are coming");
                }
            }
            else n.Scare(c.Pos);
        }

        // bump other cars
        foreach (var o in cars)
        {
            if (o == c) continue;
            Vector3 d = o.Pos - c.Pos; d.Y = 0f;
            float dist = d.Length();
            float minD = c.Type.Radius + o.Type.Radius;
            if (dist < minD && dist > 0.01f)
            {
                d /= dist;
                if (o.Owned)
                {
                    // your parked cars do not move: you bounce off them
                    c.Pos -= d * (minD - dist);
                    Sfx.Bump(c.Pos, MathF.Abs(c.Speed) / 20f);
                    c.Speed *= 0.6f;
                }
                else
                {
                    o.Pos += d * (minD - dist);
                    Sfx.Bump(c.Pos, MathF.Abs(c.Speed) / 20f);
                    world.Resolve(ref o.Pos, o.Type.Radius + 0.1f);
                    o.Scared = 3f;
                    // Bumping a cop car barely slows you (it is chasing, not blocking). Civilian cars slow you more.
                    c.Speed *= o.Police ? 0.97f : 0.9f;
                    // ПДД / hit-and-run: ramming another moving car raises the wanted level.
                    if (o.Ai && MathF.Abs(c.Speed) > 6f)
                    {
                        Wanted.Add(o.Police ? 4f : 0.5f);   // hitting a cop car = 1 star; civilian car = minor
                        if (o.Police) Toast("You hit a police car!");
                    }
                }
            }
        }
        pl.Pos = c.Pos;
    }

    // ================================================================= shooting
    void Shoot()
    {
        Weapon w = pl.W;
        w.Ammo--;
        pl.Cool = 1f / w.Rate;

        Vector3 origin = cam.Position;
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 ml = Human.MuzzleLocal(pl.Cur);
        Vector3 muzzle = U.Local(pl.Pos, pl.Yaw, ml.X, ml.Y, ml.Z);
        fx.Add(new Fx { A = muzzle, B = muzzle, Life = 0.05f, Tracer = false, Col = U.C(255, 210, 110) });
        Sfx.Shot(w);

        for (int i = 0; i < w.Pellets; i++)
        {
            Vector3 d = fwd + new Vector3(U.Rf(-1f, 1f), U.Rf(-1f, 1f), U.Rf(-1f, 1f)) * w.Spread;
            Trace(origin, Vector3.Normalize(d), muzzle, w);
        }

        // gunshots scare everybody nearby (explosive weapons and the minigun are louder)
        float loud = w.Blast > 0f || w.Slot == WSlot.Heavy ? 60f : 30f;
        foreach (var n in npcs)
            if (!n.Dead && U.Dist2D(n.Pos, pl.Pos) < loud) n.Scare(pl.Pos);
    }

    // ---- melee: hits the nearest pedestrian / officer in front of the player
    /// <summary>Bare-handed fighting: hold the left mouse button to punch (no weapon in your hands).</summary>
    void Fists(float dt)
    {
        pl.Cool -= dt;
        if (Raylib.IsMouseButtonDown(MouseButton.Left) && pl.Cool <= 0f && pl.Reload <= 0f)
            Swing(Arsenal.Fists);
    }

    void Swing(Weapon w)
    {
        pl.Cool = 1f / w.Rate;
        Sfx.Swing(w);
        pl.Yaw = camYaw;
        Vector3 fwd = new(MathF.Sin(camYaw), 0f, MathF.Cos(camYaw));

        Npc bestNpc = null; CopNpc bestCop = null;
        float best = float.MaxValue;
        foreach (var n in npcs)
        {
            if (n.Dead) continue;
            if (MeleeReach(n.Pos, fwd, w.Range, out float d) && d < best) { best = d; bestNpc = n; bestCop = null; }
        }
        foreach (var cn in copNpcs)
        {
            if (cn.Dead) continue;
            if (MeleeReach(cn.Pos, fwd, w.Range, out float d) && d < best) { best = d; bestCop = cn; bestNpc = null; }
        }

        Vector3 a = pl.Pos + new Vector3(0f, 1.8f, 0f) + fwd * 0.3f;
        Vector3 b = pl.Pos + new Vector3(0f, 0.7f, 0f) + fwd * (w.Range * 0.9f);
        fx.Add(new Fx { A = a, B = b, Life = 0.09f, Tracer = true, Col = U.C(235, 235, 240) });

        foreach (var n in npcs)
            if (!n.Dead && U.Dist2D(n.Pos, pl.Pos) < 12f) n.Scare(pl.Pos);

        if (bestNpc != null)
        {
            fx.Add(new Fx { A = bestNpc.Pos, B = bestNpc.Pos + new Vector3(0f, 1.2f, 0f), Life = 0.15f, Col = U.C(220, 20, 20) });
            Sfx.HitMelee(bestNpc.Pos, w);
            DamageNpc(bestNpc, w.Damage, false, w);
        }
        else if (bestCop != null)
        {
            Sfx.HitMelee(bestCop.Pos, w);
            fx.Add(new Fx { A = bestCop.Pos, B = bestCop.Pos + new Vector3(0f, 1.2f, 0f), Life = 0.15f, Col = U.C(220, 20, 20) });
            DamageCop(bestCop, w.Damage, false);
        }
    }

    /// <summary>Is the target within reach and roughly in front of the player (about +-60 degrees)?</summary>
    bool MeleeReach(Vector3 target, Vector3 fwd, float range, out float dist)
    {
        Vector3 d = target - pl.Pos; d.Y = 0f;
        dist = d.Length();
        if (dist > range + 0.5f) return false;
        if (dist < 0.05f) return true;
        return Vector3.Dot(d / dist, fwd) > 0.5f;
    }

    void DamageNpc(Npc n, float dmg, bool head, Weapon w)
    {
        Sfx.Scream(n.Pos);
        if (n.Hit(dmg * (head ? 3f : 1f), pl.Pos))
        {
            Reward(U.R.Next(20, 200));
            Missions.Event(MKind.Kill);
            if (head) Missions.Event(MKind.Headshot);
            if (w.Slot == WSlot.Shotgun) Missions.Event(MKind.ShotgunKill);
            // Killing an NPC = 1 star immediately.
            Wanted.Add(4f);
        }
        else Wanted.Add(1f);   // shooting at someone (non-fatal) raises heat
    }

    void DamageCop(CopNpc cn, float dmg, bool head)
    {
        // Shooting at an officer: 1.5 stars (6 heat). Killing one: +2 stars (8 heat total).
        Wanted.Add(head ? 6f : 5f);
        if (cn.Hit(dmg * (head ? 3f : 1f)))
        {
            Toast("Cop down! Wanted level up");
            Missions.Event(MKind.Kill);
            Wanted.Add(2f);
        }
    }

    void Explode(Vector3 p, Weapon w)
    {
        Sfx.Explosion(p);
        float R = w.Blast;
        fx.Add(new Fx { A = p, B = p, Life = 0.55f, Size = R * 0.5f, Col = U.C(255, 120, 30) });
        fx.Add(new Fx { A = p, B = p, Life = 0.30f, Size = R * 0.3f, Col = U.C(255, 235, 150) });
        Wanted.Add(2f);

        // the player is not immune to his own explosions
        float pd = Vector3.Distance(new Vector3(pl.Pos.X, 1f, pl.Pos.Z), p);
        if (pd < R) HurtPlayer(MathF.Min(w.Damage, 140f) * (1f - pd / R * 0.8f));

        foreach (var n in npcs)
        {
            if (n.Dead) continue;
            float d = Vector3.Distance(new Vector3(n.Pos.X, 1f, n.Pos.Z), p);
            if (d < R) DamageNpc(n, w.Damage * (1f - d / R * 0.7f), false, w);
            else if (d < R * 5f) n.Scare(p);
        }
        foreach (var cn in copNpcs)
        {
            if (cn.Dead) continue;
            float d = Vector3.Distance(new Vector3(cn.Pos.X, 1f, cn.Pos.Z), p);
            if (d < R) DamageCop(cn, w.Damage * (1f - d / R * 0.7f), false);
        }
        foreach (var c in cars)
        {
            if (c.Player) continue;
            if (Vector3.Distance(new Vector3(c.Pos.X, 1f, c.Pos.Z), p) < R * 1.2f)
            {
                c.Scared = 5f;
                if (c.Police) { Wanted.Add(4f); }
            }
        }
    }

    void Trace(Vector3 o, Vector3 d, Vector3 muzzle, Weapon w)
    {
        float best = w.Range;
        int type = 0;               // 0 nothing, 1 wall/ground, 2 npc, 3 car, 4 cop npc
        Npc hitNpc = null; Car hitCar = null; CopNpc hitCop = null; bool head = false;

        foreach (var b in world.Buildings)
        {
            if (U.RayAabb(o, d, new Vector3(b.MinX, 0f, b.MinZ), new Vector3(b.MaxX, b.H, b.MaxZ), out float t) && t < best)
            { best = t; type = 1; hitNpc = null; hitCar = null; hitCop = null; }
        }

        foreach (var n in npcs)
        {
            if (n.Dead) continue;
            if (U.RaySphere(o, d, new Vector3(n.Pos.X, 1.75f, n.Pos.Z), 0.3f, out float th) && th < best)
            { best = th; type = 2; hitNpc = n; hitCar = null; hitCop = null; head = true; }
            else if (U.RaySphere(o, d, new Vector3(n.Pos.X, 1.0f, n.Pos.Z), 0.65f, out float tb) && tb < best)
            { best = tb; type = 2; hitNpc = n; hitCar = null; hitCop = null; head = false; }
        }

        // cops on foot can be shot like ordinary NPCs - but shooting at the police is a much bigger crime.
        foreach (var cn in copNpcs)
        {
            if (cn.Dead) continue;
            if (U.RaySphere(o, d, new Vector3(cn.Pos.X, 1.75f, cn.Pos.Z), 0.3f, out float th) && th < best)
            { best = th; type = 4; hitNpc = null; hitCar = null; hitCop = cn; head = true; }
            else if (U.RaySphere(o, d, new Vector3(cn.Pos.X, 1.0f, cn.Pos.Z), 0.65f, out float tb) && tb < best)
            { best = tb; type = 4; hitNpc = null; hitCar = null; hitCop = cn; head = false; }
        }

        foreach (var c in cars)
        {
            if (c.Player) continue;
            if (U.RaySphere(o, d, new Vector3(c.Pos.X, 1.0f, c.Pos.Z), 2.3f, out float tc) && tc < best)
            { best = tc; type = 3; hitCar = c; hitNpc = null; hitCop = null; }
        }

        if (d.Y < -0.0001f)
        {
            float tg = -o.Y / d.Y;
            if (tg > 0f && tg < best) { best = tg; type = 1; hitNpc = null; hitCar = null; hitCop = null; }
        }

        Vector3 end = o + d * best;
        Color spark = U.C(255, 220, 120);

        if (w.Blast > 0f)
        {
            Explode(end, w);
        }
        else if (type == 2 && hitNpc != null)
        {
            spark = U.C(220, 20, 20);
            DamageNpc(hitNpc, w.Damage, head, w);
        }
        else if (type == 3 && hitCar != null)
        {
            hitCar.Scared = 4f;
            spark = U.C(255, 255, 255);
            // shooting at a cop car = 1 star; civilian car = minor
            if (hitCar.Police) { Wanted.Add(4f); Toast("Shooting at the police!"); }
            else Wanted.Add(0.5f);
        }
        else if (type == 4 && hitCop != null)
        {
            spark = U.C(220, 20, 20);
            DamageCop(hitCop, w.Damage, head);
        }

        fx.Add(new Fx { A = muzzle, B = end, Life = 0.06f, Tracer = true, Col = U.C(255, 240, 150) });
        if (type != 0) fx.Add(new Fx { A = end, B = end, Life = 0.15f, Tracer = false, Col = spark });
        if (w.Blast <= 0f) Sfx.Impact(type, end);
    }

    // ================================================================= interaction
    void Interact()
    {
        hint = "";
        bool e = Input.Pressed(Act.Use);

        if (Interior.Active)
        {
            if (Jobs.NearSal(pl.Pos))
            {
                hint = Jobs.Active ? "E - talk to Sal (your job / cancel)" : "E - talk to Sal (jobs)";
                if (e) Jobs.OpenMenu();
                return;
            }
            int kind = Interior.Near(pl.Pos, out string ih, out int game);
            if (kind == 0) return;
            hint = ih;
            if (!e) return;
            if (kind == 1) { Sfx.Door(); Interior.Leave(pl); camYaw = MathF.PI; camPitch = 0.28f; }
            else Casino.Open(Interior.Name, game);
            return;
        }

        if (pl.Car != null)
        {
            // Sal's casino: hand over the car he asked for
            if (Jobs.CanDeliver(pl, world, out string dh))
            {
                hint = dh;
                if (e && Jobs.CarOk(pl) && MathF.Abs(pl.Car.Speed) <= 3f) DeliverCar();
                return;
            }

            // Scrap Yard: sell a stolen car
            if (pl.Car.Stolen && World.InYard(pl.Car.Pos))
            {
                if (MathF.Abs(pl.Car.Speed) > 3f) hint = "Stop the car to sell it";
                else
                {
                    hint = $"E - sell stolen car (${pl.Car.Value})";
                    if (e) SellCar();
                }
                return;
            }

            // your own garage: park the car in a free bay
            Property lot = null;
            foreach (var pr in world.Properties)
                if (pr.Owned && pr.InLot(pl.Car.Pos)) { lot = pr; break; }
            string full = "";
            if (lot != null)
            {
                int slot = lot.FreeSlot(cars, pl.Car, pl.Car.Pos);
                if (slot >= 0)
                {
                    if (MathF.Abs(pl.Car.Speed) > 3f) hint = "Stop the car to park it";
                    else
                    {
                        hint = $"E - park in your garage ({lot.Name})";
                        if (e) ParkCar(lot, slot);
                    }
                    return;
                }
                full = "Garage is full - ";
            }

            hint = full + "E - exit car";
            if (e)
            {
                if (MathF.Abs(pl.Car.Speed) < 14f) ExitCar();
                else Toast("Too fast!");
            }
            return;
        }

        Building cas = null;
        foreach (var b in world.Casinos)
            if (U.Dist2D(pl.Pos, b.Door) < 5f) { cas = b; break; }

        Building shop = null;
        foreach (var b in world.Shops)
            if (U.Dist2D(pl.Pos, b.Door) < 4.5f) { shop = b; break; }

        Building hosp = null;
        foreach (var b in world.Hospitals)
            if (U.Dist2D(pl.Pos, b.Door) < 4.5f) { hosp = b; break; }

        Property home = null;
        foreach (var pr in world.Properties)
            if (U.Dist2D(pl.Pos, pr.Door) < 4.5f) { home = pr; break; }

        Car near = null;
        float best = 4.5f;
        foreach (var c in cars)
        {
            float d = U.Dist2D(pl.Pos, c.Pos);
            if (d < best) { best = d; near = c; }
        }

        if (cas != null)
        {
            hint = $"E - enter {cas.Name} casino";
            if (e) { Sfx.Door(); Interior.Enter(cas, pl); Jobs.OnEnter(cas, world); Missions.VisitCasino(cas.Name); Story.VisitCasino(cas.Name); camYaw = 0f; camPitch = 0.28f; }
        }
        else if (shop != null)
        {
            hint = $"E - enter {shop.Name} (buy weapons)";
            if (e) OpenShop(shop);
        }
        else if (hosp != null)
        {
            int missing = (int)MathF.Ceiling(Player.MaxHp - pl.Hp);
            if (missing <= 0) hint = "HOSPITAL - you are in perfect health";
            else
            {
                hint = $"E - get treated (${missing * Player.HealPricePerHp} for {missing} HP)";
                if (e) Treat();
            }
        }
        else if (home != null)
        {
            hint = home.Owned
                ? $"E - {home.Name}: garage {home.Used(cars)}/{home.SlotPos.Length}"
                : $"E - buy {home.Name} (${home.Price})";
            if (e) UseHome(home);
        }
        else if (near != null)
        {
            if (near.Ai && MathF.Abs(near.Speed) > 4.5f)
                hint = "Car is moving - step in front of it to stop it";
            else
            {
                hint = near.Ai ? "E - steal car" : "E - get in car";
                if (e) EnterCar(near);
            }
        }
    }

    void OpenShop(Building b)
    {
        pl.Moving = false; pl.Speed = 0f;
        Shop.Open(b);
        Raylib.EnableCursor();
    }

    void UseHome(Property p)
    {
        if (p.Owned)
        {
            Toast($"{p.Name}: {p.Used(cars)} of {p.SlotPos.Length} bays in use. Drive a car into the lot and press E to park it");
            return;
        }
        if (pl.Money < p.Price) { Toast($"Not enough money - {p.Name} costs ${p.Price}"); return; }
        pl.Money -= p.Price;
        p.Owned = true;
        Toast($"You bought the {p.Name}! Park your cars in its lot ({p.SlotPos.Length} bays)");
    }

    void ParkCar(Property p, int slot)
    {
        var c = pl.Car;
        bool fresh = !c.Owned;
        c.Player = false; c.Ai = false;
        c.Speed = 0f; c.Brake = false;
        c.Pos = p.SlotPos[slot];
        c.Yaw = p.SlotYaw[slot];
        c.Owned = true;

        // you get out on the aisle side (cars are parked nose to the wall)
        pl.Car = null;
        pl.Pos = c.Pos - c.Fwd * 3.8f;
        pl.Pos.Y = 0f;
        pl.Yaw = c.Yaw + MathF.PI;
        world.Resolve(ref pl.Pos, 0.5f);

        if (fresh)
        {
            var nc = new Car();            // keeps the amount of traffic the same
            nc.Respawn(pl.Pos);
            cars.Add(nc);
            Missions.Event(MKind.Park);
        }
        Toast($"{c.Type.Name} parked - it is yours now ({p.Used(cars)}/{p.SlotPos.Length})");
    }

    void EnterCar(Car c)
    {
        if (c.Ai)
        {
            // the driver (the farthest pedestrian, so nobody visibly vanishes) pops out and runs away
            Npc driver = null;
            float far = -1f;
            foreach (var n in npcs)
            {
                if (n.Dead) continue;
                float d = U.Dist2D(n.Pos, pl.Pos);
                if (d > far) { far = d; driver = n; }
            }
            driver?.Eject(c.Pos + c.Side * 2.6f, pl.Pos);
            c.Stolen = true;
            c.Value = c.Type.RollPrice();
            // Stealing a cop car = 2 stars immediately. Stealing a civilian car = barely any heat (0.5, not even 1 star).
            if (c.Police)
            {
                Wanted.Add(8f);
                Toast("You stole a police car! Every cop is looking for you");
            }
            else
            {
                Wanted.Add(0.5f);
                Toast($"{c.Type.Name} stolen! Sell it at the Scrap Yard (orange beacon, east of the city)");
            }
            Missions.Event(MKind.Steal);
        }
        // If the player just got into a cop car, drop the CopCar wrapper so its chase AI does not fight
        // the player's driving. The car keeps Police=true (so it still draws the siren / light bar).
        copCars.RemoveAll(cc => cc.Car == c);
        c.Ai = false;
        c.Player = true;
        c.Scared = 0f;
        pl.Car = c;
        camYaw = c.Yaw;
        camPitch = 0.28f;
        Sfx.Door(); Sfx.Ignition();
    }

    void ExitCar()
    {
        Sfx.Door();
        var c = pl.Car;
        c.Player = false;
        c.Ai = false;
        pl.Pos = c.Pos + c.Side * 2.8f;
        world.Resolve(ref pl.Pos, 0.5f);
        pl.Yaw = c.Yaw;
        pl.Car = null;
    }

    void DeliverCar()
    {
        var c = pl.Car;
        c.Player = false;
        c.Ai = false;
        pl.Pos = c.Pos + c.Side * 2.8f;
        world.Resolve(ref pl.Pos, 0.5f);
        pl.Yaw = c.Yaw;
        pl.Car = null;
        c.Respawn(pl.Pos);
        Jobs.Delivered(pl);
    }

    void SellCar()
    {
        var c = pl.Car;
        int v = c.Value;
        c.Player = false;
        c.Ai = false;
        pl.Pos = c.Pos + c.Side * 2.8f;
        world.Resolve(ref pl.Pos, 0.5f);
        pl.Yaw = c.Yaw;
        pl.Car = null;
        if (c.Owned) cars.Remove(c);     // a replacement was added to traffic when you parked it
        else c.Respawn(pl.Pos);          // the yard takes the car; it comes back later as normal traffic
        pl.Money += v;
        Sfx.Door(); Sfx.Cash(1f);
        Toast($"Car sold! +${v}");
        Missions.Event(MKind.Sell);
    }

    void Reward(int amount)
    {
        pl.Money += amount;
        Sfx.Cash(0.5f);
        Toast($"+${amount}");
    }

    void Toast(string s) { toast = s; toastT = 2.5f; }

    // ================================================================= save / load / new game
    SaveData Capture()
    {
        bool inside = Interior.Active;                    // inside a casino we save the spot in front of its door
        Vector3 pos = inside ? Interior.ReturnPos : pl.Pos;
        var d = new SaveData
        {
            Money = pl.Money,
            Hp = pl.Hp, Armor = pl.Armor,
            StoryStep = Story.Index,
            X = pos.X, Z = pos.Z,
            Yaw = inside ? MathF.PI : pl.Yaw,
            Armed = inside ? Interior.WasArmed : pl.Armed,
            Current = pl.W?.Id ?? ""
        };
        foreach (var w in pl.Weapons) d.Weapons.Add(new SavedWeapon { Id = w.Id, Ammo = w.Ammo, Reserve = w.Reserve });
        foreach (var m in Missions.All)
            d.Missions.Add(new SavedMission { Kind = m.Kind.ToString(), State = m.State, Progress = m.Progress, Seen = new List<string>(m.Seen) });
        foreach (var pr in world.Properties) if (pr.Owned) d.Properties.Add(pr.Name);
        foreach (var c in cars)
        {
            bool driven = c == pl.Car;
            if (c.Police || (!c.Owned && !driven)) continue;
            d.Cars.Add(new SavedCar
            {
                X = c.Pos.X, Z = c.Pos.Z, Yaw = c.Yaw, Kind = (int)c.Type.Kind, Model = c.Model,
                R = c.Col.R, G = c.Col.G, B = c.Col.B,
                Owned = c.Owned, Stolen = c.Stolen, Value = c.Value, Driven = driven
            });
        }
        if (pl.Car != null && pl.Car.Police) { d.X = pl.Car.Pos.X; d.Z = pl.Car.Pos.Z; }
        return d;
    }

    void SaveGame(bool announce)
    {
        if (pl == null || world == null) return;
        if (dying) Respawn();             // pausing / quitting while dead: you still wake up in the hospital (and pay the bill)
        if (SaveFile.Write(Capture()))
        {
            saveFlashT = 2f;
            if (announce) Toast("Game saved");
        }
        else Toast("SAVE FAILED - could not write " + SaveFile.PathName);
    }

    bool LoadGame()
    {
        var d = SaveFile.Read();
        if (d == null) return false;
        try { ApplySave(d); return true; }
        catch (Exception e)
        {
            Console.Error.WriteLine("Could not apply the save: " + e.Message);
            ClearProgress();
            return false;
        }
    }

    /// <summary>Back to a blank state: $150, no weapons, no property, no missions, owned cars go back into traffic.</summary>
    void ClearProgress()
    {
        Casino.Active = false; Shop.Active = false; Interior.Active = false;
        paused = false;
        foreach (var cc in copCars) cars.Remove(cc.Car);
        copCars.Clear(); copNpcs.Clear();
        foreach (var c in cars)
            if (c.Owned || c.Player || c.Stolen || c.Police) c.Respawn(world.StartPos);
        Wanted.Clear();
        wasWanted = false; bustedT = 0f; copSpawnCool = 0f;
        dying = false; deadT = 0f; hurtT = 0f; carHitCool = 0f;
        foreach (var n in npcs) n.Calm();
        Story.Reset();
        Jobs.Reset();
        wp = null;
        foreach (var pr in world.Properties) pr.Owned = false;
        Missions.ResetAll();
        pl = new Player(world.StartPos);
        camYaw = 0f; camPitch = 0.28f; scoped = false; autosaveT = 0f;
    }

    void ApplySave(SaveData d)
    {
        ClearProgress();
        pl.Money = Math.Max(0, d.Money);
        pl.Hp = d.Hp > 0f ? MathF.Min(d.Hp, Player.MaxHp) : Player.MaxHp;      // old saves have no health: start healthy
        pl.Armor = Math.Clamp(d.Armor, 0f, Player.MaxArmor);
        Story.Load(d.StoryStep);

        foreach (var sw in d.Weapons)
        {
            var def = Arsenal.Find(sw.Id);
            if (def != null && pl.Get(sw.Id) == null) pl.Restore(def, sw.Ammo, sw.Reserve);
        }
        int ci = pl.Weapons.FindIndex(w => w.Id == d.Current);
        pl.Cur = Math.Max(0, ci);
        pl.Armed = d.Armed && pl.Weapons.Count > 0;

        foreach (var sm in d.Missions)
        {
            var m = Missions.All.Find(x => x.Kind.ToString() == sm.Kind);
            if (m == null) continue;
            m.State = Math.Clamp(sm.State, 0, 2);
            m.Progress = Math.Clamp(sm.Progress, 0f, m.Target);
            m.Seen.Clear();
            foreach (var n in sm.Seen) m.Seen.Add(n);
        }

        foreach (var pr in world.Properties) pr.Owned = d.Properties.Contains(pr.Name);

        foreach (var sc in d.Cars)
        {
            var c = new Car();
            c.Type = CarType.Of((CarKind)Math.Clamp(sc.Kind, 0, CarType.All.Length - 1));
            c.Col = U.C(sc.R, sc.G, sc.B);
            c.Model = sc.Model >= 0 && sc.Model < Gfx.Count(Gfx.CatCar) ? sc.Model : -1;
            c.Cruise = U.Rf(c.Type.CruiseMin, c.Type.CruiseMax);
            c.Pos = new Vector3(sc.X, 0f, sc.Z);
            c.Yaw = sc.Yaw;
            c.Speed = 0f;
            c.Ai = false; c.Player = false;
            c.Owned = sc.Owned; c.Stolen = sc.Stolen; c.Value = sc.Value;
            cars.Add(c);
            if (sc.Driven) { c.Player = true; pl.Car = c; }
        }

        pl.Pos = new Vector3(d.X, 0f, d.Z);
        pl.Yaw = d.Yaw;
        if (pl.Car == null) world.Resolve(ref pl.Pos, 0.5f);
        camYaw = pl.Car != null ? pl.Car.Yaw : d.Yaw;
        camPitch = 0.28f;
    }

    void NewGame()
    {
        ClearProgress();
        paused = false;
        Raylib.DisableCursor(); skipMouse = 3;
        UpdateCamera(0f);
        SaveGame(false);
        Toast($"New game - you have ${pl.Money} and no weapons. Find an AMMU-NATION (red signs)");
    }

    // ================================================================= camera
    void UpdateCamera(float dt)
    {
        Vector3 target;
        float dist;

        if (pl.Car == null)
        {
            Vector3 r = new(-MathF.Cos(camYaw), 0f, MathF.Sin(camYaw));
            target = pl.Pos + new Vector3(0f, 1.55f, 0f) + r * 0.9f;   // over-the-shoulder
            dist = 5.2f;
        }
        else
        {
            var c = pl.Car;
            camYaw += U.WrapPi(c.Yaw - camYaw) * MathF.Min(1f, dt * (MathF.Abs(c.Speed) > 1f ? 2.5f : 0.3f));
            camPitch += ((firstPerson ? 0.02f : 0.3f) - camPitch) * MathF.Min(1f, dt * 2f);
            target = c.Pos + new Vector3(0f, 1.6f, 0f);
            dist = 10f;
        }

        if (firstPerson && !dying)
        {
            Vector3 eye = pl.Car == null ? pl.Pos + new Vector3(0f, 1.68f, 0f) : U.Local(pl.Car.Pos, pl.Car.Yaw, -0.35f, 1.25f, 0.05f);
            float fcp = MathF.Cos(camPitch);
            Vector3 fw = new(MathF.Sin(camYaw) * fcp, -MathF.Sin(camPitch), MathF.Cos(camYaw) * fcp);
            eye += fw * 0.12f;
            fovCur += ((scoped ? 20f : 78f) - fovCur) * MathF.Min(1f, dt * 10f);
            cam = new Camera3D { Position = eye, Target = eye + fw, Up = Vector3.UnitY, FovY = fovCur, Projection = CameraProjection.Perspective };
            return;
        }

        float cp = MathF.Cos(camPitch);
        Vector3 f = new(MathF.Sin(camYaw) * cp, -MathF.Sin(camPitch), MathF.Cos(camYaw) * cp);
        Vector3 pos = target - f * dist;
        for (float d = dist; d > 1.2f; d -= 0.6f)
        {
            pos = target - f * d;
            if (!world.Inside(pos)) break;
        }
        if (pos.Y < 0.5f) pos.Y = 0.5f;
        if (Interior.Active) pos = Interior.ClampCam(pos);

        fovCur += ((scoped ? 20f : 72f) - fovCur) * MathF.Min(1f, dt * 10f);
        cam = new Camera3D { Position = pos, Target = pos + f, Up = Vector3.UnitY, FovY = fovCur, Projection = CameraProjection.Perspective };
    }

    // ================================================================= drawing
    void Draw()
    {
        float time = (float)Raylib.GetTime();
        Gfx.BeginScene(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        Raylib.ClearBackground(Sky);

        Raylib.BeginMode3D(cam);
        Gfx.SetView(cam.Position, time);
        if (Interior.Active) Interior.SetLights(cam.Position, time);
        else Gfx.SetNeon(world.Casinos, cam.Position, time);
        if (pl.Car != null)
        {
            Vector3 dir = Vector3.Normalize(pl.Car.Fwd + new Vector3(0f, -0.12f, 0f));
            Gfx.SetCarLight(true, pl.Car.Front, dir);
        }
        else Gfx.SetCarLight(false, Vector3.Zero, Vector3.UnitZ);

        Gfx.DrawSky(cam.Position);
        if (Interior.Active) Interior.Draw(cam.Position, time);
        else
        {
            world.Draw(cam.Position, time);
            Story.Draw3D(world, time);
            Jobs.Draw3D(world, cars, pl, time);
            if (wp != null)
            {
                Vector3 q = wp.Value;
                Color wc = U.C(255, 90, 220);
                Gfx.Flat(new Vector3(q.X, 25f, q.Z), new Vector3(0.35f, 50f, 0.35f), 0f, wc);
                float wpu = 1f + 0.12f * MathF.Sin(time * 3f);
                float wr = 1.6f * wpu;
                Gfx.Flat(new Vector3(q.X, 0.3f, q.Z - wr), new Vector3(2f * wr, 0.08f, 0.14f), 0f, wc);
                Gfx.Flat(new Vector3(q.X, 0.3f, q.Z + wr), new Vector3(2f * wr, 0.08f, 0.14f), 0f, wc);
                Gfx.Flat(new Vector3(q.X - wr, 0.3f, q.Z), new Vector3(0.14f, 0.08f, 2f * wr), 0f, wc);
                Gfx.Flat(new Vector3(q.X + wr, 0.3f, q.Z), new Vector3(0.14f, 0.08f, 2f * wr), 0f, wc);
            }
            foreach (var c in cars)
                if (U.Dist2D(c.Pos, cam.Position) < 260f) c.Draw(cam.Position);
            foreach (var n in npcs)
                if (U.Dist2D(n.Pos, cam.Position) < 150f) n.Draw(cam.Position);
            // cops: cars use the Car.Draw path above (they are in the cars list). Officers on foot draw here.
            foreach (var cn in copNpcs)
                if (U.Dist2D(cn.Pos, cam.Position) < 150f) cn.Draw(cam.Position);
        }
        Story.DrawCast(cam.Position);
        if (pl.Car == null && (!firstPerson || dying || Story.Talking)) pl.Draw();

        foreach (var f in fx)
        {
            if (f.Tracer) Raylib.DrawLine3D(f.A, f.B, f.Col);
            else Raylib.DrawSphere(f.B, f.Size, f.Col);
        }
        if (!Interior.Active) world.DrawLate(cam.Position, time);
        Raylib.EndMode3D();

        Gfx.EndScene(time);   // post-processing, opens the real frame

        if (MainMenu.Active)
        {
            MainMenu.Draw();
            Raylib.EndDrawing();
            return;
        }

        if (Interior.Active) Interior.DrawLabels(cam);
        else { DrawCasinoLabels(); DrawShopLabels(); DrawHospitalLabels(); DrawYardLabel(); DrawHomeLabels(); }
        DrawHud();
        if (Casino.Active) Casino.Draw(pl);
        if (paused) Missions.DrawMenu(pl);
        if (Shop.Active) Shop.Draw(pl);
        if (Jobs.MenuOpen) Jobs.DrawMenu(pl);
        if (!paused) Story.DrawTalk();

        Raylib.EndDrawing();
    }

    void DrawCasinoLabels()
    {
        Vector3 camFwd = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var b in world.Casinos)
        {
            Vector3 c = b.Center;
            Vector3 p = new(c.X, b.H + 4f, c.Z);
            Vector3 to = p - cam.Position;
            if (to.Length() > 170f || Vector3.Dot(to, camFwd) <= 0f) continue;
            Vector2 s = Raylib.GetWorldToScreen(p, cam);
            int w = Raylib.MeasureText(b.Name, 22);
            Raylib.DrawText(b.Name, (int)s.X - w / 2 + 2, (int)s.Y + 2, 22, U.C(0, 0, 0, 180));
            Raylib.DrawText(b.Name, (int)s.X - w / 2, (int)s.Y, 22, b.Neon);
        }
    }

    void DrawShopLabels()
    {
        Vector3 camFwd = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var b in world.Shops)
        {
            Vector3 p = new(b.Door.X, 11f, b.Door.Z);
            Vector3 to = p - cam.Position;
            if (to.Length() > 170f || Vector3.Dot(to, camFwd) <= 0f) continue;
            Vector2 s = Raylib.GetWorldToScreen(p, cam);
            string t2 = b.ShopKind == 0 ? "weapons & ammo" : "heavy weapons";
            int w = Raylib.MeasureText(b.Name, 24), w2 = Raylib.MeasureText(t2, 16);
            Raylib.DrawText(b.Name, (int)s.X - w / 2 + 2, (int)s.Y + 2, 24, U.C(0, 0, 0, 180));
            Raylib.DrawText(b.Name, (int)s.X - w / 2, (int)s.Y, 24, b.Neon);
            Raylib.DrawText(t2, (int)s.X - w2 / 2, (int)s.Y + 26, 16, U.C(235, 235, 245));
        }
    }

    void DrawHospitalLabels()
    {
        Vector3 camFwd = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var b in world.Hospitals)
        {
            Vector3 p = new(b.Door.X, 17f, b.Door.Z);
            Vector3 to = p - cam.Position;
            if (to.Length() > 170f || Vector3.Dot(to, camFwd) <= 0f) continue;
            Vector2 s = Raylib.GetWorldToScreen(p, cam);
            string t2 = $"treatment ${Player.HealPricePerHp} per HP";
            int w = Raylib.MeasureText(b.Name, 24), w2 = Raylib.MeasureText(t2, 16);
            Raylib.DrawText(b.Name, (int)s.X - w / 2 + 2, (int)s.Y + 2, 24, U.C(0, 0, 0, 180));
            Raylib.DrawText(b.Name, (int)s.X - w / 2, (int)s.Y, 24, b.Neon);
            Raylib.DrawText(t2, (int)s.X - w2 / 2, (int)s.Y + 26, 16, U.C(235, 235, 245));
        }
    }

    void DrawHomeLabels()
    {
        Vector3 camFwd = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var p in world.Properties)
        {
            Vector3 pt = new(p.Door.X, 8f, p.Door.Z);
            Vector3 to = pt - cam.Position;
            if (to.Length() > 170f || Vector3.Dot(to, camFwd) <= 0f) continue;
            Vector2 s = Raylib.GetWorldToScreen(pt, cam);
            string t1 = p.Owned ? p.Name : p.Name + " - FOR SALE";
            string t2 = p.Owned ? $"your garage {p.Used(cars)}/{p.SlotPos.Length}" : $"${p.Price}   ({p.SlotPos.Length} parking bays)";
            Color c1 = p.Owned ? U.C(90, 230, 130) : U.C(255, 90, 110);
            int w1 = Raylib.MeasureText(t1, 24), w2 = Raylib.MeasureText(t2, 18);
            Raylib.DrawText(t1, (int)s.X - w1 / 2 + 2, (int)s.Y + 2, 24, U.C(0, 0, 0, 180));
            Raylib.DrawText(t1, (int)s.X - w1 / 2, (int)s.Y, 24, c1);
            Raylib.DrawText(t2, (int)s.X - w2 / 2, (int)s.Y + 26, 18, U.C(235, 235, 245));
        }
    }

    void DrawYardLabel()
    {
        Vector3 camFwd = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 p = new(World.YardPos.X - 22f, 10.5f, World.YardPos.Z);
        Vector3 to = p - cam.Position;
        if (to.Length() > 200f || Vector3.Dot(to, camFwd) <= 0f) return;
        Vector2 s = Raylib.GetWorldToScreen(p, cam);
        string t1 = "SCRAP YARD", t2 = "sell stolen cars here";
        int w1 = Raylib.MeasureText(t1, 24), w2 = Raylib.MeasureText(t2, 16);
        Raylib.DrawText(t1, (int)s.X - w1 / 2 + 2, (int)s.Y + 2, 24, U.C(0, 0, 0, 180));
        Raylib.DrawText(t1, (int)s.X - w1 / 2, (int)s.Y, 24, U.C(255, 150, 30));
        Raylib.DrawText(t2, (int)s.X - w2 / 2, (int)s.Y + 26, 16, U.C(255, 220, 160));
    }

    void DrawHud()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();

        DrawHurtFlash(sw, sh);
        Raylib.DrawText($"$ {pl.Money}", 20, 20, 34, U.C(90, 230, 120));
        Raylib.DrawFPS(20, 62);
        if (saveFlashT > 0f)
            Raylib.DrawText("Game saved", 110, 66, 18, U.C(120, 230, 150, (int)(255 * MathF.Min(1f, saveFlashT))));
        Missions.DrawTracker(20, 92);

        // Wanted stars: 0..5 across the top-centre of the screen, just under the toast.
        DrawWantedHud(sw, sh);
        DrawVitals(sh);
        Story.DrawHud(sh, pl);
        Jobs.DrawHud(sh);

        if (pl.Car == null)
        {
            Weapon w = pl.W;
            string ws = pl.Weapons.Count == 0 ? "Fists"
                : !pl.Armed ? "Fists"
                : w.Melee ? w.Name
                : $"{w.Name}   {w.Ammo} / {w.Reserve}" + (pl.Reload > 0f ? "   RELOADING" : "");
            int tw = Raylib.MeasureText(ws, 28);
            Raylib.DrawText(ws, sw - tw - 20, sh - 50, 28, U.C(255, 255, 255));

            // owned weapons (slot number + name) under the minimap
            int ly = 16 + 230 + 30;
            foreach (var ow in pl.Weapons)
            {
                bool cur = pl.Armed && ow == w;
                string lt = $"{(int)ow.Slot + 1}  {ow.Name}";
                int lw = Raylib.MeasureText(lt, 18);
                Raylib.DrawText(lt, sw - lw - 17, ly + 1, 18, U.C(0, 0, 0, 170));
                Raylib.DrawText(lt, sw - lw - 16, ly, 18, cur ? U.C(255, 220, 90) : U.C(190, 190, 205));
                ly += 21;
            }

            // crosshair / sniper scope
            int cx = sw / 2, cy = sh / 2;
            if (scoped)
            {
                float rad = MathF.Min(sw, sh) * 0.46f;
                Raylib.DrawRing(new Vector2(cx, cy), rad, MathF.Sqrt((float)sw * sw + (float)sh * sh) / 2f + 4f, 0f, 360f, 96, U.C(0, 0, 0, 255));
                Raylib.DrawCircleLines(cx, cy, rad, U.C(0, 0, 0, 255));
                Raylib.DrawLine(cx - (int)rad, cy, cx + (int)rad, cy, U.C(0, 0, 0, 220));
                Raylib.DrawLine(cx, cy - (int)rad, cx, cy + (int)rad, U.C(0, 0, 0, 220));
                Raylib.DrawCircle(cx, cy, 2f, U.C(255, 40, 40));
            }
            else
            {
                Raylib.DrawLine(cx - 10, cy, cx - 4, cy, U.C(255, 255, 255));
                Raylib.DrawLine(cx + 4, cy, cx + 10, cy, U.C(255, 255, 255));
                Raylib.DrawLine(cx, cy - 10, cx, cy - 4, U.C(255, 255, 255));
                Raylib.DrawLine(cx, cy + 4, cx, cy + 10, U.C(255, 255, 255));
            }
        }
        else
        {
            int kph = (int)(MathF.Abs(pl.Car.Speed) * 3.6f);
            // speed turns red when the player is over the city limit (~50 km/h)
            Color sc = MathF.Abs(pl.Car.Speed) > Car.CitySpeedLimit + 2f ? U.C(255, 90, 90) : U.C(255, 255, 255);
            string sp = $"{pl.Car.Type.Name}   {kph} km/h";
            int tw = Raylib.MeasureText(sp, 36);
            Raylib.DrawText(sp, sw - tw - 20, sh - 56, 36, sc);
            // wrong-side warning lamp
            if (pl.Car.WrongSide)
            {
                string ws = "WRONG SIDE";
                int ww = Raylib.MeasureText(ws, 22);
                Raylib.DrawText(ws, sw - ww - 20, sh - 96, 22, U.C(255, 120, 60));
            }
        }

        if (toastT > 0f)
        {
            int tw = Raylib.MeasureText(toast, 34);
            Raylib.DrawText(toast, sw / 2 - tw / 2, 140, 34, U.C(255, 255, 255));
        }

        if (!Interior.Active) Minimap();

        if (bustedT > 0f) DrawBusted(sw, sh);
        if (dying) DrawWasted(sw, sh);
    }

    /// <summary>Health (and armour, when you have some) in the bottom-left corner.</summary>
    void DrawVitals(int sh)
    {
        int x = 20, w = 250, y = sh - 66;
        float t = (float)Raylib.GetTime();

        if (pl.Armor > 0.5f)
        {
            int ay = y - 22;
            Raylib.DrawRectangle(x - 2, ay - 2, w + 4, 18, U.C(0, 0, 0, 170));
            Raylib.DrawRectangle(x, ay, w, 14, U.C(20, 26, 44));
            Raylib.DrawRectangle(x, ay, (int)(w * Math.Clamp(pl.Armor / Player.MaxArmor, 0f, 1f)), 14, U.C(100, 160, 245));
            Raylib.DrawText($"ARMOR {(int)MathF.Ceiling(pl.Armor)}", x + 8, ay, 13, U.C(255, 255, 255));
        }

        float f = Math.Clamp(pl.Hp / Player.MaxHp, 0f, 1f);
        Color hc = f > 0.6f ? U.C(70, 200, 100) : f > 0.3f ? U.C(235, 190, 60) : U.C(230, 60, 60);
        if (f <= 0.3f && !dying && ((int)(t * 4f) & 1) == 0) hc = U.C(255, 110, 110);      // blinks when low
        Raylib.DrawRectangle(x - 2, y - 2, w + 4, 22, U.C(0, 0, 0, 170));
        Raylib.DrawRectangle(x, y, w, 18, U.C(40, 20, 22));
        Raylib.DrawRectangle(x, y, (int)(w * f), 18, hc);
        Raylib.DrawText($"HP {(int)MathF.Ceiling(pl.Hp)}", x + 8, y + 1, 16, U.C(255, 255, 255));
    }

    /// <summary>Red edges of the screen: a flash after every hit, and a pulse when health is low.</summary>
    void DrawHurtFlash(int sw, int sh)
    {
        float low = (!dying && pl.Hp < 30f) ? (0.25f + 0.2f * MathF.Sin((float)Raylib.GetTime() * 5f)) * (1f - pl.Hp / 30f) : 0f;
        float a = MathF.Max(hurtT * 0.75f, low);
        if (a <= 0.01f) return;
        int al = (int)(255 * Math.Clamp(a, 0f, 1f));
        int e = Math.Min(sw, sh) / 3;
        Color on = U.C(200, 0, 0, al), off = U.C(200, 0, 0, 0);
        Raylib.DrawRectangleGradientV(0, 0, sw, e, on, off);
        Raylib.DrawRectangleGradientV(0, sh - e, sw, e, off, on);
        Raylib.DrawRectangleGradientH(0, 0, e, sh, on, off);
        Raylib.DrawRectangleGradientH(sw - e, 0, e, sh, off, on);
    }

    void DrawWasted(int sw, int sh)
    {
        float t = Math.Clamp((4f - deadT) / 1.2f, 0f, 1f);       // fades in over the first second or so
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(30, 0, 0, (int)(190 * t)));
        int a = (int)(255 * t);
        int tw = Raylib.MeasureText("WASTED", 120);
        Raylib.DrawText("WASTED", sw / 2 - tw / 2 + 4, sh / 2 - 60 + 4, 120, U.C(0, 0, 0, (int)(220 * t)));
        Raylib.DrawText("WASTED", sw / 2 - tw / 2, sh / 2 - 60, 120, U.C(215, 25, 25, a));
        string s2 = "You will wake up in the hospital";
        int tw2 = Raylib.MeasureText(s2, 26);
        Raylib.DrawText(s2, sw / 2 - tw2 / 2, sh / 2 + 70, 26, U.C(220, 220, 220, a));
    }

    /// <summary>Filled 5-point star centred on (cx, cy), outer radius r. Each triangle is drawn in both windings,
    /// so it does not depend on raylib's counter-clockwise rule.</summary>
    static void DrawStar(float cx, float cy, float r, Color fill, Color outline)
    {
        Vector2[] pt = new Vector2[10];
        for (int k = 0; k < 10; k++)
        {
            float ang = -MathF.PI / 2f + k * MathF.PI / 5f;
            float rr = (k & 1) == 0 ? r : r * 0.42f;
            pt[k] = new Vector2(cx + MathF.Cos(ang) * rr, cy + MathF.Sin(ang) * rr);
        }
        Vector2 c0 = new(cx, cy);
        for (int k = 0; k < 10; k++)
        {
            Vector2 a = pt[k], b = pt[(k + 1) % 10];
            Raylib.DrawTriangle(c0, a, b, fill);
            Raylib.DrawTriangle(c0, b, a, fill);
        }
        for (int k = 0; k < 10; k++) Raylib.DrawLineEx(pt[k], pt[(k + 1) % 10], 2f, outline);
    }

    void DrawWantedHud(int sw, int sh)
    {
        int stars = Wanted.Stars;
        if (stars <= 0) return;
        int sz = 28, gap = 6;
        int totalW = 5 * sz + 4 * gap;
        int x0 = sw / 2 - totalW / 2;
        int y0 = 60;
        float flash = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 8f);
        for (int i = 0; i < 5; i++)               // always 5 slots: earned stars flash, the rest stay dark
        {
            int cx = x0 + i * (sz + gap) + sz / 2;
            int cy = y0 + sz / 2;
            Color c = i < stars ? U.C(255, (int)(60 + 80 * flash), 60) : U.C(50, 50, 56, 190);
            DrawStar(cx, cy, sz / 2f + 2f, c, i < stars ? U.C(20, 20, 20) : U.C(110, 110, 120));
        }
        int tw = Raylib.MeasureText("WANTED", 18);
        Raylib.DrawText("WANTED", sw / 2 - tw / 2, y0 - 22, 18, U.C(255, 80, 80));
    }

    void DrawBusted(int sw, int sh)
    {
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 160));
        int tw = Raylib.MeasureText("BUSTED", 120);
        Raylib.DrawText("BUSTED", sw / 2 - tw / 2 + 4, sh / 2 - 60 + 4, 120, U.C(0, 0, 0, 220));
        Raylib.DrawText("BUSTED", sw / 2 - tw / 2, sh / 2 - 60, 120, U.C(220, 30, 30));
        int tw2 = Raylib.MeasureText("You lost half your money", 26);
        Raylib.DrawText("You lost half your money", sw / 2 - tw2 / 2, sh / 2 + 70, 26, U.C(220, 220, 220));
    }

    bool bigMap;

    /// <summary>Radar-style minimap that follows the player and turns with the camera (what is up on the map is straight ahead on the screen).
    /// M opens the full-city map.</summary>
    void Minimap()
    {
        if (bigMap) { BigMap(); return; }

        int sw = Raylib.GetScreenWidth();
        int sz = 230;
        int x0 = sw - sz - 16, y0 = 16;
        float range = 140f;                              // metres from the centre to the edge of the minimap
        float sc = sz / 2f / range;
        Vector3 c = pl.Car != null ? pl.Car.Pos : pl.Pos;
        Vector2 fwd = new(MathF.Sin(camYaw), MathF.Cos(camYaw));
        Vector2 right = new(-MathF.Cos(camYaw), MathF.Sin(camYaw));
        Vector2 ctr = new(x0 + sz / 2f, y0 + sz / 2f);

        Vector2 P(float x, float z)
        {
            float dx = x - c.X, dz = z - c.Z;
            float u = dx * right.X + dz * right.Y, v = dx * fwd.X + dz * fwd.Y;
            return new Vector2(ctr.X + u * sc, ctr.Y - v * sc);
        }

        Raylib.DrawRectangle(x0 - 3, y0 - 3, sz + 6, sz + 6, U.C(0, 0, 0, 200));
        Raylib.BeginScissorMode(x0, y0, sz, sz);
        Raylib.DrawRectangle(x0, y0, sz, sz, U.C(30, 38, 32));
        DrawMapLayers(P, sc, false);

        // the player: an arrow showing where he (or the car) is heading
        float yaw = pl.Car != null ? pl.Car.Yaw : pl.Yaw;
        float au = MathF.Sin(yaw) * right.X + MathF.Cos(yaw) * right.Y;
        float av = MathF.Sin(yaw) * fwd.X + MathF.Cos(yaw) * fwd.Y;
        Vector2 ad = new(au, -av);
        if (ad.LengthSquared() < 1e-4f) ad = new Vector2(0f, -1f);
        ad = Vector2.Normalize(ad);
        Vector2 an = new(-ad.Y, ad.X);
        Vector2 tip = ctr + ad * 11f, bl = ctr - ad * 7f + an * 7f, br = ctr - ad * 7f - an * 7f;
        Raylib.DrawTriangle(tip, bl, br, U.C(255, 255, 255));
        Raylib.DrawTriangle(tip, br, bl, U.C(255, 255, 255));
        Raylib.DrawTriangleLines(tip, bl, br, U.C(0, 0, 0));
        Raylib.EndScissorMode();

        // objectives: when one is off the map, a marker sticks to the edge and points at it
        var objs = new List<(Vector3 pos, Color col)>();
        Vector3? sop = Story.ObjectivePos(world);
        if (sop != null) objs.Add((sop.Value, U.C(255, 225, 60)));
        Vector3? jop = Jobs.ObjectivePos(world, cars, pl);
        if (jop != null) objs.Add((jop.Value, U.C(80, 220, 255)));
        if (wp != null) objs.Add((wp.Value, U.C(255, 90, 220)));
        int dline = 0;
        foreach (var (opos, ocol) in objs)
        {
            Vector2 sp = P(opos.X, opos.Z);
            Vector2 off = sp - ctr;
            float lim = sz / 2f - 9f;
            if (MathF.Abs(off.X) > lim || MathF.Abs(off.Y) > lim)
            {
                float k = lim / MathF.Max(MathF.Abs(off.X), MathF.Abs(off.Y));
                sp = ctr + off * k;
                Vector2 d = Vector2.Normalize(off), n = new(-d.Y, d.X);
                Raylib.DrawTriangle(sp + d * 9f, sp - d * 3f + n * 8f, sp - d * 3f - n * 8f, ocol);
                Raylib.DrawTriangle(sp + d * 9f, sp - d * 3f - n * 8f, sp - d * 3f + n * 8f, ocol);
                Raylib.DrawTriangleLines(sp + d * 9f, sp - d * 3f + n * 8f, sp - d * 3f - n * 8f, U.C(0, 0, 0));
            }
            else DrawObjectiveDot(sp, ocol);
            float dist = U.Dist2D(c, opos);
            string ds = dist >= 1000f ? $"{dist / 1000f:0.0} km" : $"{(int)dist} m";
            Raylib.DrawText(ds, x0 + sz - Raylib.MeasureText(ds, 18) - 6, y0 + sz - 24 - dline * 20, 18, ocol);
            dline++;
        }

        // compass: N is the direction of +Z
        Vector2 np = new(ctr.X + MathF.Sin(camYaw) * (sz / 2f - 12f), ctr.Y - MathF.Cos(camYaw) * (sz / 2f - 12f));
        Raylib.DrawCircle((int)np.X, (int)np.Y, 9f, U.C(0, 0, 0, 200));
        Raylib.DrawText("N", (int)np.X - 5, (int)np.Y - 8, 16, U.C(255, 90, 90));

        Raylib.DrawRectangleLines(x0, y0, sz, sz, U.C(200, 200, 220));
        Raylib.DrawText("M - big map", x0, y0 + sz + 6, 16, U.C(200, 200, 220));
    }

    void DrawObjectiveDot(Vector2 sp, Color col)
    {
        float pr = 6f + 1.5f * MathF.Sin((float)Raylib.GetTime() * 5f);
        Raylib.DrawCircle((int)sp.X, (int)sp.Y, pr + 2f, U.C(0, 0, 0, 230));
        Raylib.DrawCircle((int)sp.X, (int)sp.Y, pr, col);
    }

    /// <summary>Roads, buildings of interest, cars: shared by the minimap and the big map.</summary>
    void DrawMapLayers(Func<float, float, Vector2> P, float sc, bool big)
    {
        // roads as real thick lines (so the grid reads like streets)
        Color road = U.C(74, 76, 90);
        float rw = MathF.Max(2f, World.Road * sc);
        for (int i = 0; i <= World.N; i++)
        {
            Vector2 a = P(i * World.Step, -World.Step), b = P(i * World.Step, World.Max + World.Step);
            Raylib.DrawLineEx(a, b, rw, road);
            Vector2 c2 = P(-World.Step, i * World.Step), d = P(World.Max + World.Step, i * World.Step);
            Raylib.DrawLineEx(c2, d, rw, road);
        }

        int fs = big ? 18 : 12;
        void Mark(Building b, Color col, int s, string label)
        {
            Vector2 p = P(b.Center.X, b.Center.Z);
            Raylib.DrawRectangle((int)p.X - s / 2 - 1, (int)p.Y - s / 2 - 1, s + 2, s + 2, U.C(0, 0, 0, 220));
            Raylib.DrawRectangle((int)p.X - s / 2, (int)p.Y - s / 2, s, s, col);
            if (big && label.Length > 0) Raylib.DrawText(label, (int)p.X + s / 2 + 4, (int)p.Y - fs / 2, fs, col);
        }
        int ms = big ? 12 : 9;
        Building salHome = Jobs.Home(world);
        foreach (var b in world.Casinos)
        {
            bool sal = b == salHome;
            Mark(b, sal ? U.C(90, 220, 140) : U.C(255, 210, 40), sal ? ms + 3 : ms, sal ? b.Name + "  (SAL - JOBS)" : b.Name);
        }
        foreach (var b in world.Shops) Mark(b, b.ShopKind == 0 ? U.C(255, 70, 60) : U.C(190, 90, 255), ms, b.ShopKind == 0 ? "AMMU-NATION" : "BLACK MARKET");
        foreach (var b in world.Hospitals)
        {
            Vector2 p = P(b.Center.X, b.Center.Z);
            int s = ms + 2;
            Raylib.DrawRectangle((int)p.X - s / 2 - 1, (int)p.Y - s / 2 - 1, s + 2, s + 2, U.C(0, 0, 0, 220));
            Raylib.DrawRectangle((int)p.X - s / 2, (int)p.Y - s / 2, s, s, U.C(245, 247, 250));
            Raylib.DrawRectangle((int)p.X - 1, (int)p.Y - s / 2 + 2, 3, s - 4, U.C(40, 200, 110));
            Raylib.DrawRectangle((int)p.X - s / 2 + 2, (int)p.Y - 1, s - 4, 3, U.C(40, 200, 110));
            if (big) Raylib.DrawText("HOSPITAL", (int)p.X + s / 2 + 4, (int)p.Y - fs / 2, fs, U.C(245, 247, 250));
        }
        Vector2 yp = P(World.YardPos.X, World.YardPos.Z);
        Raylib.DrawRectangle((int)yp.X - ms / 2 - 1, (int)yp.Y - ms / 2 - 1, ms + 2, ms + 2, U.C(0, 0, 0, 220));
        Raylib.DrawRectangle((int)yp.X - ms / 2, (int)yp.Y - ms / 2, ms, ms, U.C(255, 140, 30));
        if (big) Raylib.DrawText("SCRAP YARD", (int)yp.X - Raylib.MeasureText("SCRAP YARD", fs) - ms, (int)yp.Y - fs / 2, fs, U.C(255, 140, 30));
        foreach (var h in world.Properties)
        {
            Vector2 hp = P(h.Cx, h.Cz);
            Color hc = h.Owned ? U.C(90, 230, 130) : U.C(255, 90, 110);
            Raylib.DrawRectangle((int)hp.X - ms / 2 - 1, (int)hp.Y - ms / 2 - 1, ms + 2, ms + 2, U.C(0, 0, 0, 220));
            Raylib.DrawRectangle((int)hp.X - ms / 2, (int)hp.Y - ms / 2, ms, ms, hc);
            if (big) Raylib.DrawText("HOME", (int)hp.X + ms / 2 + 4, (int)hp.Y - fs / 2, fs, hc);
        }
        foreach (var c in cars)
        {
            if (!c.Player && !c.Police && !c.Owned && !(!big)) continue;
            Vector2 p = P(c.Pos.X, c.Pos.Z);
            Color mc = c.Player ? U.C(255, 255, 255) : c.Police ? U.C(80, 130, 255) : c.Owned ? U.C(90, 230, 130) : U.C(80, 200, 230);
            Raylib.DrawCircle((int)p.X, (int)p.Y, c.Police ? 3.5f : 2.5f, mc);
        }
        foreach (var cn in copNpcs)
        {
            Vector2 p = P(cn.Pos.X, cn.Pos.Z);
            Raylib.DrawCircle((int)p.X, (int)p.Y, 3f, U.C(80, 130, 255));
        }
        if (big)
        {
            Vector3? sop = Story.ObjectivePos(world);
            if (sop != null) DrawObjectiveDot(P(sop.Value.X, sop.Value.Z), U.C(255, 225, 60));
            Vector3? jop = Jobs.ObjectivePos(world, cars, pl);
            if (jop != null) DrawObjectiveDot(P(jop.Value.X, jop.Value.Z), U.C(80, 220, 255));
            if (wp != null) DrawObjectiveDot(P(wp.Value.X, wp.Value.Z), U.C(255, 90, 220));
        }
    }

    Vector3? wp;                       // waypoint set on the map: pink beam in the world, arrow on the minimap

    void CloseMap()
    {
        bigMap = false;
        Raylib.DisableCursor();
        skipMouse = 3;
    }

    /// <summary>Full-city map (M). North-up. The game is frozen: click a place (or a marker) to set a waypoint, right click removes it.</summary>
    void BigMap()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 170));
        int sz = Math.Min(sw, sh) - 120;
        int x0 = (sw - sz) / 2, y0 = (sh - sz) / 2;
        float span = World.Max + 140f;
        float sc = sz / span;
        float cx = World.Max / 2f + 30f, cz = World.Max / 2f;
        Vector2 ctr = new(x0 + sz / 2f, y0 + sz / 2f);
        Vector2 P(float x, float z) => new(ctr.X + (cx - x) * sc, ctr.Y - (z - cz) * sc);

        Raylib.DrawRectangle(x0, y0, sz, sz, U.C(30, 38, 32));
        Raylib.BeginScissorMode(x0, y0, sz, sz);
        DrawMapLayers(P, sc, true);

        Vector3 c = pl.Car != null ? pl.Car.Pos : pl.Pos;
        float yaw = pl.Car != null ? pl.Car.Yaw : pl.Yaw;
        Vector2 pp = P(c.X, c.Z);
        if (wp != null)
        {
            Vector2 wpp = P(wp.Value.X, wp.Value.Z);
            Raylib.DrawLineEx(pp, wpp, 2f, U.C(255, 90, 220, 150));
        }
        Vector2 ad = new(-MathF.Sin(yaw), -MathF.Cos(yaw));       // screen-right = -X, screen-up = +Z
        ad = Vector2.Normalize(ad);
        Vector2 an = new(-ad.Y, ad.X);
        Vector2 tip = pp + ad * 14f, bl = pp - ad * 9f + an * 9f, br = pp - ad * 9f - an * 9f;
        Raylib.DrawTriangle(tip, bl, br, U.C(255, 50, 50));
        Raylib.DrawTriangle(tip, br, bl, U.C(255, 50, 50));
        Raylib.DrawTriangleLines(tip, bl, br, U.C(255, 255, 255));
        Raylib.DrawText("YOU", (int)pp.X + 12, (int)pp.Y + 8, 18, U.C(255, 120, 120));
        if (wp != null)
        {
            Vector2 q = P(wp.Value.X, wp.Value.Z);
            Raylib.DrawLineEx(q, q - new Vector2(0f, 26f), 3f, U.C(255, 255, 255));
            Raylib.DrawCircle((int)q.X, (int)q.Y - 30, 9f, U.C(255, 255, 255));
            Raylib.DrawCircle((int)q.X, (int)q.Y - 30, 6f, U.C(255, 90, 220));
        }
        Raylib.EndScissorMode();
        Raylib.DrawRectangleLines(x0, y0, sz, sz, U.C(200, 200, 220));
        Raylib.DrawText("N", x0 + sz / 2 - 6, y0 - 26, 22, U.C(255, 90, 90));
        Raylib.DrawText("Click - set waypoint    Right click - remove    G - route to objective    M / Esc - close", x0, y0 + sz + 8, 20, U.C(220, 220, 235));

        // places of interest under the mouse
        var pois = new List<(Vector3 pos, string name)>();
        foreach (var b in world.Casinos) pois.Add((b.Door, b.Name + " casino"));
        foreach (var b in world.Shops) pois.Add((b.Door, b.ShopKind == 0 ? "AMMU-NATION" : "Black Market"));
        foreach (var b in world.Hospitals) pois.Add((b.Door, "Hospital"));
        foreach (var h in world.Properties) pois.Add((h.Door, h.Name + (h.Owned ? " (yours)" : $" - ${h.Price}")));
        pois.Add((World.YardPos, "Scrap Yard"));

        Vector2 mouse = Raylib.GetMousePosition();
        bool inside = mouse.X >= x0 && mouse.X <= x0 + sz && mouse.Y >= y0 && mouse.Y <= y0 + sz;
        int hi = -1;
        float hd = 18f;
        for (int i = 0; i < pois.Count; i++)
        {
            Vector2 pp2 = P(pois[i].pos.X, pois[i].pos.Z);
            float d = Vector2.Distance(pp2, mouse);
            if (d < hd) { hd = d; hi = i; }
        }
        Vector3 mw = new(cx - (mouse.X - ctr.X) / sc, 0f, cz - (mouse.Y - ctr.Y) / sc);
        if (inside)
        {
            Vector3 at = hi >= 0 ? pois[hi].pos : mw;
            string label = hi >= 0 ? pois[hi].name : "Set waypoint here";
            float dist = U.Dist2D(c, at);
            string ds = dist >= 1000f ? $"{dist / 1000f:0.0} km" : $"{(int)dist} m";
            string tt = $"{label}   {ds}";
            int tw = Raylib.MeasureText(tt, 20);
            int tx = (int)Math.Min(mouse.X + 16, sw - tw - 20), ty = (int)Math.Min(mouse.Y + 16, sh - 40);
            Raylib.DrawRectangle(tx - 6, ty - 4, tw + 12, 28, U.C(10, 10, 20, 235));
            Raylib.DrawRectangleLines(tx - 6, ty - 4, tw + 12, 28, hi >= 0 ? U.C(255, 90, 220) : U.C(120, 120, 140));
            Raylib.DrawText(tt, tx, ty, 20, U.C(255, 255, 255));

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                wp = new Vector3(Math.Clamp(at.X, -20f, World.Max + 60f), 0f, Math.Clamp(at.Z, -20f, World.Max + 20f));
                Toast("Waypoint set");
            }
            if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { wp = null; Toast("Waypoint removed"); }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.G))
        {
            Vector3? t = Story.ObjectivePos(world) ?? Jobs.ObjectivePos(world, cars, pl);
            if (t != null) { wp = new Vector3(t.Value.X, 0f, t.Value.Z); Toast("Waypoint: current objective"); }
            else Toast("No objective right now");
        }
        if (toastT > 0f)
        {
            int tw = Raylib.MeasureText(toast, 30);
            Raylib.DrawText(toast, sw / 2 - tw / 2, y0 - 56, 30, U.C(255, 255, 255));
        }

        // our own cursor, so it is visible whatever the window system does with the real one
        Raylib.DrawTriangle(mouse, mouse + new Vector2(0f, 22f), mouse + new Vector2(15f, 15f), U.C(255, 255, 255));
        Raylib.DrawTriangle(mouse, mouse + new Vector2(15f, 15f), mouse + new Vector2(0f, 22f), U.C(255, 255, 255));
        Raylib.DrawTriangleLines(mouse, mouse + new Vector2(0f, 22f), mouse + new Vector2(15f, 15f), U.C(0, 0, 0));

        // legend
        int lx = x0 + sz + 20, ly = y0;
        if (lx + 200 < sw)
        {
            (Color, string)[] leg =
            {
                (U.C(255, 210, 40), "Casino"), (U.C(255, 70, 60), "Ammu-Nation"), (U.C(190, 90, 255), "Black Market"),
                (U.C(245, 247, 250), "Hospital"), (U.C(255, 140, 30), "Scrap yard"), (U.C(255, 90, 110), "Home for sale"),
                (U.C(90, 230, 130), "Your home / car"), (U.C(80, 130, 255), "Police"), (U.C(255, 225, 60), "Story objective"),
                (U.C(90, 220, 140), "Sal - jobs casino"), (U.C(80, 220, 255), "Sal job objective"), (U.C(255, 90, 220), "Your waypoint"),
            };
            foreach (var (col, name) in leg)
            {
                Raylib.DrawRectangle(lx, ly + 3, 14, 14, col);
                Raylib.DrawText(name, lx + 22, ly, 18, U.C(230, 230, 240));
                ly += 26;
            }
        }
    }
}
