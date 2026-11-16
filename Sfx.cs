using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>
/// Sound for the whole game. There are no audio files: every effect, engine loop, siren and music track is
/// synthesised once at start-up (a fraction of a second) and played through raylib's audio device.
///   Sfx.Init() / Sfx.Shutdown()      window lifetime
///   Sfx.Frame(...)                   once per frame: music, ambience, engine, siren, tyre screech, heartbeat
///   Sfx.Shot / Reload / Explosion .. one-shot events, called from the game code
/// F9 mutes / unmutes everything. If no audio device exists the whole class silently does nothing.
/// </summary>
public static unsafe class Sfx
{
    const int Rate = 44100;
    static bool ok, muted;
    static readonly Random R = new(7);          // fixed seed: the generated sounds are identical on every run
    static readonly Random Rnd = new();         // run-time variation (pitch jitter, which footstep...)
    static Vector3 listener;
    static float now;

    // ================================================================ playback primitives
    /// <summary>Optional real recordings: put sounds\NAME.wav / .ogg / .mp3 next to the game and they replace the synthesised sound.</summary>
    static string FindFile(string name)
    {
        string[] dirs = { Path.Combine(Directory.GetCurrentDirectory(), "sounds"), Path.Combine(AppContext.BaseDirectory, "sounds") };
        string[] ext = { ".wav", ".ogg", ".mp3", ".flac" };
        foreach (var d in dirs)
            foreach (var e in ext)
            {
                string p = Path.Combine(d, name + e);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>One sound with a few aliases so that rapid fire / overlapping events do not cut each other off.</summary>
    sealed class Voice
    {
        readonly Sound[] s;
        int next;

        public Voice(Sound first, int copies)
        {
            s = new Sound[copies];
            s[0] = first;
            for (int i = 1; i < copies; i++) s[i] = Raylib.LoadSoundAlias(first);
        }

        public Voice(short[] pcm, int copies = 4) : this(MakeSound(pcm), copies) { }

        static Sound MakeSound(short[] pcm)
        {
            fixed (short* p = pcm)
            {
                var w = new Wave { SampleRate = Rate, SampleSize = 16, Channels = 1, Data = p };
                object boxed = w;
                SetFrames(boxed, (uint)pcm.Length);       // the field is called differently in different Raylib-cs versions
                w = (Wave)boxed;
                return Raylib.LoadSoundFromWave(w);       // raylib copies the samples
            }
        }

        public void Play(float vol = 1f, float pitch = 1f)
        {
            if (vol < 0.004f) return;
            var c = s[next];
            next = (next + 1) % s.Length;
            Raylib.SetSoundVolume(c, MathF.Min(vol, 1.5f));
            Raylib.SetSoundPitch(c, pitch);
            Raylib.PlaySound(c);
        }
    }

    static void SetFrames(object boxedWave, uint frames)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        string[] known = { "samplerate", "samplesize", "channels" };
        var names = new List<string>();
        foreach (var f in typeof(Wave).GetFields(F))
        {
            names.Add(f.Name + ":" + f.FieldType.Name);
            if (f.FieldType != typeof(uint) && f.FieldType != typeof(int)) continue;
            if (Array.IndexOf(known, f.Name.TrimStart('_').ToLowerInvariant()) >= 0) continue;
            f.SetValue(boxedWave, Convert.ChangeType(frames, f.FieldType));
            return;
        }
        foreach (var pr in typeof(Wave).GetProperties(F))
        {
            names.Add(pr.Name + ":" + pr.PropertyType.Name);
            if (!pr.CanWrite || (pr.PropertyType != typeof(uint) && pr.PropertyType != typeof(int))) continue;
            if (Array.IndexOf(known, pr.Name.ToLowerInvariant()) >= 0) continue;
            pr.SetValue(boxedWave, Convert.ChangeType(frames, pr.PropertyType));
            return;
        }
        throw new InvalidOperationException("Wave has no frame count member. Members: " + string.Join(", ", names));
    }

    /// <summary>A looping sound (volume and pitch can change every frame): a synthesised stream or a music file from sounds\\.</summary>
    sealed class Loop
    {
        const int Chunk = 4096;
        AudioStream st;
        Music mus;
        readonly bool file;
        readonly short[] data, buf = new short[Chunk];
        int pos;
        public float Target, Cur, Pitch = 1f;

        public Loop(string name, short[] d)
        {
            string path = FindFile(name);
            if (path != null)
            {
                mus = Raylib.LoadMusicStream(path);       // raylib loops music streams by default
                Raylib.SetMusicVolume(mus, 0f);
                Raylib.PlayMusicStream(mus);
                file = true;
                return;
            }
            data = d;
            st = Raylib.LoadAudioStream(Rate, 16, 1);
            Raylib.SetAudioStreamVolume(st, 0f);
            Feed(); Feed();
            Raylib.PlayAudioStream(st);
        }

        void Feed()
        {
            for (int i = 0; i < Chunk; i++)
            {
                buf[i] = data[pos];
                if (++pos >= data.Length) pos = 0;
            }
            fixed (short* p = buf) Raylib.UpdateAudioStream(st, p, Chunk);
        }

        public void Update(float dt)
        {
            Cur += (Target - Cur) * MathF.Min(1f, dt * 5f);
            if (Cur < 0.002f && Target <= 0f) Cur = 0f;
            if (file)
            {
                Raylib.UpdateMusicStream(mus);
                Raylib.SetMusicVolume(mus, Cur);
                Raylib.SetMusicPitch(mus, Pitch);
                return;
            }
            for (int k = 0; k < 3 && Raylib.IsAudioStreamProcessed(st); k++) Feed();
            Raylib.SetAudioStreamVolume(st, Cur);
            Raylib.SetAudioStreamPitch(st, Pitch);
        }
    }

    static readonly List<string> names = new();

    static Voice V(string name, short[] pcm, int copies = 4)
    {
        names.Add(name);
        string f = FindFile(name);
        return f != null ? new Voice(Raylib.LoadSound(f), copies) : new Voice(pcm, copies);
    }

    // ---- one-shots
    static Voice gunPistol, gunDeagle, gunSmg, gunShotgun, gunRifle, gunSniper, gunLauncher, gunMinigun, gunCop;
    static Voice explosion, hitFlesh, hitWall, hitMetal, whooshBat, whooshBlade, whooshFist, slash, punch;
    static Voice reload, reloadDone, empty, draw;
    static Voice[] step;
    static Voice land, door, ignition, crash, bump;
    static Voice hurt, dieV, screamM, screamF, heartbeat, busted;
    static Voice cash, heal, alert, relief;
    static Voice uiTick, uiSelect, uiBack, uiError;
    static Voice slotTick, reelStop, win, jackpot, lose, card, rouletteTick;

    // ---- loops
    static Loop engine, siren, screech, ambience, musicMenu, musicCasino, road, wind;

    // ================================================================ lifetime
    public static void Init()
    {
        try { InitInner(); }
        catch (Exception e)
        {
            // never let a sound problem kill the game: it just runs silently
            ok = false;
            Console.WriteLine("SFX disabled: " + e.Message);
        }
    }

    static void InitInner()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return;
        Raylib.SetAudioStreamBufferSizeDefault(4096);

        // guns
        gunPistol   = V("gun_pistol", Gun(0.30f, 28f, 0.55f, 140f, 22f, 0.8f, 0.5f, 0.08f), 6);
        gunDeagle   = V("gun_deagle", Gun(0.55f, 16f, 0.40f, 180f, 14f, 1.1f, 0.6f, 0.20f), 4);
        gunSmg      = V("gun_smg", Gun(0.17f, 45f, 0.60f, 110f, 30f, 0.6f, 0.5f, 0.04f), 8);
        gunShotgun  = V("gun_shotgun", Gun(0.65f, 9f, 0.30f, 120f, 10f, 1.2f, 0.6f, 0.25f), 4);
        gunRifle    = V("gun_rifle", Gun(0.42f, 20f, 0.45f, 130f, 16f, 0.9f, 0.55f, 0.14f), 6);
        gunSniper   = V("gun_sniper", Gun(1.20f, 7f, 0.35f, 150f, 9f, 1.2f, 0.7f, 0.50f), 3);
        gunLauncher = V("gun_launcher", Gun(0.90f, 6f, 0.18f, 90f, 7f, 1.3f, 0.2f, 0.40f), 3);
        gunMinigun  = V("gun_minigun", Gun(0.10f, 55f, 0.60f, 100f, 40f, 0.55f, 0.5f, 0.02f), 10);
        gunCop      = V("gun_cop", Gun(0.45f, 22f, 0.35f, 120f, 16f, 0.8f, 0.4f, 0.28f), 6);   // distant, with an echo

        explosion = V("explosion", GExplosion(2.8f), 3);
        hitFlesh = V("hit_flesh", GHitFlesh(), 5);
        hitWall = V("hit_wall", Ping(0.30f, 2400f, 3350f), 5);
        hitMetal = V("hit_metal", MetalHit(), 4);
        whooshBat = V("whoosh_bat", Whoosh(0.30f), 3);
        whooshBlade = V("whoosh_blade", Whoosh(0.20f), 3);
        whooshFist = V("whoosh_fist", Whoosh(0.16f), 3);
        slash = V("slash", GSlash(), 3);
        punch = V("punch", GHitFlesh(), 3);
        reload = V("reload", ReloadSeq(), 3);
        reloadDone = V("reload_done", Click(0.10f, 700f, 1f), 3);
        empty = V("empty", Click(0.07f, 1800f, 0.8f), 3);
        draw = V("draw", DrawSnd(), 3);

        step = new[] { V("step1", Step(0.14f, 0.10f), 3), V("step2", Step(0.12f, 0.16f), 3), V("step3", Step(0.16f, 0.12f), 3) };
        land = V("land", Step(0.07f, 0.08f, 1.5f), 2);
        door = V("door", GDoor(), 3);
        ignition = V("ignition", GIgnition(), 2);
        crash = V("crash", GCrash(0.95f), 3);
        bump = V("bump", GBump(), 3);

        hurt = V("hurt", Vowel(0.28f, 150f, 105f, 550f, 1000f, 0.25f, 0.01f, 5f, 0f), 3);
        dieV = V("die", Death(), 1);
        screamM = V("scream_m", Vowel(0.75f, 190f, 230f, 750f, 1200f, 0.18f, 0.02f, 1.6f, 1f), 3);
        screamF = V("scream_f", Vowel(0.75f, 340f, 410f, 850f, 1450f, 0.15f, 0.02f, 1.6f, 1f), 3);
        heartbeat = V("heartbeat", GHeartbeat(), 2);
        busted = V("busted", GBusted(), 1);

        cash = V("cash", GCash(), 4);
        heal = V("heal", Chimes(new[] { 659f, 784f, 988f, 1319f }, 0.07f, 0.8f), 2);
        alert = V("alert", GAlert(), 2);
        relief = V("relief", Chimes(new[] { 1047f, 784f }, 0.18f, 0.9f), 2);

        uiTick = V("ui_tick", Tone(0.05f, 1250f, 1250f, 0.5f, 90f), 4);
        uiSelect = V("ui_select", Chimes(new[] { 700f, 1050f }, 0.06f, 0.35f), 3);
        uiBack = V("ui_back", Chimes(new[] { 700f, 480f }, 0.06f, 0.30f), 3);
        uiError = V("ui_error", GBuzz(), 3);

        slotTick = V("slot_tick", Click(0.04f, 1900f, 0.7f), 4);
        reelStop = V("reel_stop", GReelStop(), 3);
        win = V("win", Chimes(new[] { 784f, 988f, 1175f, 1568f }, 0.09f, 1.1f), 2);
        jackpot = V("jackpot", Chimes(new[] { 523f, 659f, 784f, 1047f, 1319f, 1568f, 2093f, 1568f, 2093f, 2637f }, 0.085f, 1.8f), 1);
        lose = V("lose", GLose(), 2);
        card = V("card", GCard(), 4);
        rouletteTick = V("roulette_tick", Click(0.03f, 1100f, 0.6f), 4);

        // loops
        engine = new Loop("engine", EngineLoop());
        siren = new Loop("siren", SirenLoop());
        screech = new Loop("screech", ScreechLoop());
        ambience = new Loop("ambience", AmbienceLoop());
        musicMenu = new Loop("music_menu", Song(0));
        musicCasino = new Loop("music_casino", Song(1));
        road = new Loop("road", NoiseLoop(3f, 0.15f, 0.03f, 6f));
        wind = new Loop("wind", NoiseLoop(3f, 0.45f, 0.12f, 5f));

        ok = true;
    }

    public static void Shutdown()
    {
        if (ok) Raylib.CloseAudioDevice();
        ok = false;
    }

    /// <summary>F9: mute / unmute. Returns true when the sound is now off.</summary>
    public static bool ToggleMute()
    {
        muted = !muted;
        if (ok) Raylib.SetMasterVolume(muted ? 0f : 1f);
        return muted;
    }

    // ================================================================ per-frame
    static float engPitch = 0.6f, hbT, stepT, airT;
    static bool wasGround = true;
    static readonly float[] gear = { 0f, 0.16f, 0.34f, 0.54f, 0.76f, 1.01f };

    /// <param name="menu">main menu open</param><param name="interior">inside a casino</param>
    /// <param name="frozen">pause / map / shop / dialogue</param><param name="car">the car the player drives (or null)</param>
    /// <param name="copDist">distance to the nearest chasing cop car (9999 = none)</param><param name="hpFrac">health 0..1</param>
    public static void Frame(float dt, bool menu, bool interior, bool frozen, bool dead, Car car, float copDist, Vector3 camPos, float hpFrac)
    {
        if (!ok) return;
        now += dt;
        listener = camPos;
        bool game = !menu;
        bool drive = game && !frozen && !interior && !dead && car != null;

        musicMenu.Target = menu ? 0.55f : 0f;
        musicCasino.Target = game && interior ? (frozen ? 0.2f : 0.45f) : 0f;
        ambience.Target = game && !interior ? (frozen ? 0.08f : (car != null ? 0.12f : 0.22f)) : 0f;

        // ---- engine: a simple automatic gearbox so that the pitch rises and falls like a real car
        float sp = 0f, thr = 0f;
        if (drive)
        {
            sp = MathF.Abs(car.Speed);
            thr = Input.Down(Act.Forward) || Input.Down(Act.Back) ? 1f : 0f;
            float frac = Math.Clamp(sp / MathF.Max(10f, car.Type.Top), 0f, 1f);
            int g = 0;
            while (g < 4 && frac >= gear[g + 1]) g++;
            float r = Math.Clamp((frac - gear[g]) / (gear[g + 1] - gear[g]), 0f, 1f);
            float target = 0.62f + r * 0.95f + thr * 0.10f;
            engPitch += (target - engPitch) * MathF.Min(1f, dt * 9f);
            engine.Pitch = Math.Clamp(engPitch, 0.5f, 2.2f);
            engine.Target = 0.30f + 0.22f * thr + 0.10f * (sp / MathF.Max(10f, car.Type.Top));
        }
        else engine.Target = 0f;

        // ---- police siren, louder the closer the cruiser is
        siren.Target = game && !interior && !dead && copDist < 150f ? Math.Clamp(1f - copDist / 150f, 0f, 1f) * 0.32f : 0f;

        // ---- tyres: handbrake or hard cornering at speed
        bool slide = drive && sp > 9f && Input.Down(Act.Jump);
        bool corner = drive && sp > 20f && (Input.Down(Act.Left) || Input.Down(Act.Right));
        screech.Target = slide ? 0.38f : corner ? 0.16f : 0f;
        screech.Pitch = 0.85f + MathF.Min(sp, 40f) / 80f;

        // ---- heartbeat when badly hurt
        if (game && !frozen && !interior && !dead && hpFrac < 0.3f)
        {
            hbT -= dt;
            if (hbT <= 0f)
            {
                hbT = 0.45f + 0.6f * (hpFrac / 0.3f);
                heartbeat.Play(0.55f * (1f - hpFrac / 0.3f * 0.5f));
            }
        }
        else hbT = 0f;

        road.Target = drive ? Math.Clamp(sp / 30f, 0f, 1f) * 0.22f : 0f;
        road.Pitch = 0.7f + MathF.Min(sp, 40f) / 50f;
        wind.Target = drive ? Math.Clamp((sp - 6f) / 30f, 0f, 1f) * 0.28f : 0f;
        wind.Pitch = 0.8f + MathF.Min(sp, 45f) / 90f;
        road.Update(dt); wind.Update(dt);
        engine.Update(dt); siren.Update(dt); screech.Update(dt);
        ambience.Update(dt); musicMenu.Update(dt); musicCasino.Update(dt);
    }

    static void At(Voice v, Vector3 p, float vol = 1f, float pitch = 1f)
    {
        if (!ok) return;
        float d = Vector3.Distance(listener, p);
        if (d > 220f) return;
        v.Play(vol / (1f + d * d / 225f), pitch);
    }

    static float Jit(float a = 0.06f) => 1f + (float)(Rnd.NextDouble() * 2.0 - 1.0) * a;

    // ================================================================ events: shooting and fighting
    public static void Shot(Weapon w)
    {
        if (!ok) return;
        Voice v = w.Look switch
        {
            WLook.Pistol => gunPistol,
            WLook.Magnum => gunDeagle,
            WLook.Smg => gunSmg,
            WLook.Shotgun => gunShotgun,
            WLook.Rifle => gunRifle,
            WLook.Sniper => gunSniper,
            WLook.Launcher => gunLauncher,
            WLook.Minigun => gunMinigun,
            _ => gunPistol,
        };
        v.Play(0.85f, Jit(0.04f));
    }

    public static void CopShot(Vector3 p) => At(gunCop, p, 0.9f, Jit(0.05f));

    public static void Reload(Weapon w) { if (ok) reload.Play(0.6f, w.ReloadTime > 1.6f ? 0.85f : 1f); }
    public static void ReloadDone(Weapon w) { if (ok) reloadDone.Play(0.6f); }
    public static void Empty() { if (ok) empty.Play(0.7f); }
    public static void Draw() { if (ok) draw.Play(0.5f, Jit(0.05f)); }

    public static void Swing(Weapon w)
    {
        if (!ok) return;
        if (w.Look == WLook.Blade) whooshBlade.Play(0.6f, 1.25f * Jit());
        else if (w.Look == WLook.Bat) whooshBat.Play(0.7f, 0.85f * Jit());
        else whooshFist.Play(0.45f, Jit(0.1f));
    }

    public static void HitMelee(Vector3 p, Weapon w)
    {
        if (!ok) return;
        if (w.Look == WLook.Blade) At(slash, p, 0.9f, Jit());
        else if (w.Look == WLook.Bat) At(punch, p, 1.1f, 0.7f * Jit());
        else At(punch, p, 0.9f, Jit(0.1f));
    }

    static readonly float[] lastImpact = new float[6];

    /// <summary>Bullet impact: kind 1 = wall / ground, 2 = pedestrian, 3 = car, 4 = officer.</summary>
    public static void Impact(int kind, Vector3 p)
    {
        if (!ok || kind <= 0 || kind >= lastImpact.Length) return;
        if (now - lastImpact[kind] < 0.05f) return;
        lastImpact[kind] = now;
        switch (kind)
        {
            case 1: At(hitWall, p, 0.55f, Jit(0.15f)); break;
            case 3: At(hitMetal, p, 0.7f, Jit(0.12f)); break;
            default: At(hitFlesh, p, 0.7f, Jit(0.12f)); break;
        }
    }

    public static void HitFlesh(Vector3 p, float vol = 1f) { if (ok) At(hitFlesh, p, vol, Jit(0.1f)); }
    public static void Explosion(Vector3 p) { if (ok) At(explosion, p, 1.4f, Jit(0.08f)); }

    static float lastScream, lastHurt;
    public static void Scream(Vector3 p)
    {
        if (!ok || now - lastScream < 0.35f) return;
        lastScream = now;
        if (Rnd.Next(2) == 0) At(screamM, p, 0.9f, Jit(0.12f)); else At(screamF, p, 0.9f, Jit(0.12f));
    }

    // ================================================================ events: the player
    public static void Hurt(float dmg)
    {
        if (!ok || now - lastHurt < 0.2f) return;
        lastHurt = now;
        hurt.Play(Math.Clamp(0.4f + dmg * 0.02f, 0.4f, 1f), Jit(0.1f));
    }

    public static void Die() { if (ok) dieV.Play(1f); }
    public static void Busted() { if (ok) busted.Play(0.9f); }

    public static void Footsteps(float dt, bool moving, bool sprint, bool ground, bool interior)
    {
        if (!ok) return;
        if (!ground) { airT += dt; wasGround = false; return; }
        if (!wasGround)
        {
            wasGround = true;
            if (airT > 0.25f) land.Play(interior ? 0.3f : 0.55f, Jit(0.1f));
            airT = 0f;
        }
        airT = 0f;
        if (!moving) { stepT = 0f; return; }
        stepT -= dt;
        if (stepT > 0f) return;
        stepT = sprint ? 0.28f : 0.46f;
        step[Rnd.Next(step.Length)].Play(interior ? 0.22f : (sprint ? 0.5f : 0.38f), Jit(0.12f));
    }

    // ================================================================ events: cars
    public static void Door() { if (ok) door.Play(0.8f, Jit(0.05f)); }
    public static void Ignition() { if (ok) ignition.Play(0.7f); }

    static float lastBump;
    public static void Crash(float strength)
    {
        if (!ok || now - lastBump < 0.25f) return;
        lastBump = now;
        crash.Play(Math.Clamp(0.35f + strength * 0.8f, 0.3f, 1.2f), Jit(0.1f));
    }

    public static void Bump(Vector3 p, float strength)
    {
        if (!ok || now - lastBump < 0.25f) return;
        lastBump = now;
        At(bump, p, Math.Clamp(0.3f + strength * 0.7f, 0.3f, 1f), Jit(0.12f));
    }

    // ================================================================ events: money, police, UI
    public static void Cash(float vol = 0.8f) { if (ok) cash.Play(vol, Jit(0.03f)); }
    public static void Heal() { if (ok) heal.Play(0.6f); }

    static int lastStars;
    public static void WantedLevel(int stars)
    {
        if (!ok) { lastStars = stars; return; }
        if (stars > lastStars) alert.Play(0.7f, 1f + 0.05f * stars);
        else if (stars == 0 && lastStars > 0) relief.Play(0.5f);
        lastStars = stars;
    }

    public static void UiTick() { if (ok) uiTick.Play(0.5f, Jit(0.02f)); }
    public static void UiSelect() { if (ok) uiSelect.Play(0.6f); }
    public static void UiBack() { if (ok) uiBack.Play(0.5f); }
    public static void Error() { if (ok) uiError.Play(0.5f); }

    // ---- casino
    public static void SlotTick() { if (ok) slotTick.Play(0.35f, Jit(0.12f)); }
    public static void RouletteTick(float pitch) { if (ok) rouletteTick.Play(0.5f, pitch); }
    public static void ReelStop() { if (ok) reelStop.Play(0.6f, Jit(0.05f)); }
    public static void Win(bool big) { if (!ok) return; if (big) jackpot.Play(0.8f); else win.Play(0.7f); cash.Play(0.5f); }
    public static void Lose() { if (ok) lose.Play(0.6f); }
    public static void Card() { if (ok) card.Play(0.55f, Jit(0.1f)); }

    // ================================================================ synthesis helpers
    static float W() => (float)(R.NextDouble() * 2.0 - 1.0);
    static float[] Buf(float sec) => new float[Math.Max(1, (int)(sec * Rate))];

    /// <summary>Peak-normalise, short fade-out (no click at the end), convert to 16 bit.</summary>
    static short[] Fin(float[] b, float peak = 0.9f)
    {
        float m = 1e-6f;
        foreach (float v in b) m = MathF.Max(m, MathF.Abs(v));
        float k = peak / m;
        int fade = Math.Min(b.Length / 4, (int)(0.006f * Rate));
        var o = new short[b.Length];
        for (int i = 0; i < b.Length; i++)
        {
            float v = b[i] * k;
            int left = b.Length - 1 - i;
            if (left < fade) v *= left / (float)fade;
            o[i] = (short)(Math.Clamp(v, -1f, 1f) * 32000f);
        }
        return o;
    }

    /// <summary>Small Schroeder reverb (4 combs + 2 all-passes) mixed into the buffer: gives shots and blasts their "outdoor air".</summary>
    static void Reverb(float[] b, float mix, float size)
    {
        int[] cd = { 1687, 1931, 2213, 2459 };
        float[] cg = { 0.80f, 0.78f, 0.76f, 0.74f };
        var wet = new float[b.Length];
        for (int k = 0; k < cd.Length; k++)
        {
            int d = Math.Max(16, (int)(cd[k] * size));
            var c = new float[d];
            int p = 0;
            float lpz = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float y = c[p];
                lpz = y * 0.6f + lpz * 0.4f;           // high frequencies die faster in the tail
                c[p] = b[i] + lpz * cg[k];
                if (++p >= d) p = 0;
                wet[i] += y;
            }
        }
        int[] ad = { 225, 556 };
        foreach (int dd in ad)
        {
            var a = new float[dd];
            int p = 0;
            for (int i = 0; i < wet.Length; i++)
            {
                float bo = a[p], x = wet[i];
                wet[i] = -x + bo;
                a[p] = x + bo * 0.5f;
                if (++p >= dd) p = 0;
            }
        }
        for (int i = 0; i < b.Length; i++) b[i] += wet[i] * mix * 0.25f;
    }

    static short[] Gun(float dur, float nk, float lp, float th, float tk, float ta, float crack, float tail)
    {
        var b = Buf(dur + 0.7f);
        float y = 0, y2 = 0;
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += lp * (w - y);                         // dark muzzle blast
            y2 += 0.5f * (w - y2);
            float cr = (w - y2) * MathF.Exp(-t * 1100f) * crack * 4f;                  // supersonic crack, about 1 ms
            float body = y * MathF.Exp(-t * nk) * 2.2f + (y2 - y) * MathF.Exp(-t * nk * 1.7f) * 1.2f;
            ph += MathF.Tau * (38f + th * MathF.Exp(-t * 35f)) / Rate;
            float thump = MathF.Sin((float)ph) * MathF.Exp(-t * tk) * ta;
            b[i] = cr + body + thump;
        }
        AddClick(b, 0.04f, 1100f, 0.10f + ta * 0.05f);   // the action cycling
        Reverb(b, 0.35f + tail * 1.4f, 1f);
        return Fin(b);
    }

    static short[] GExplosion(float dur)
    {
        var b = Buf(dur + 1.2f);
        float y = 0, deb = 0;
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            float a = 0.015f + 0.25f * MathF.Exp(-t * 2.5f);
            y += a * (w - y);
            ph += MathF.Tau * (28f + 60f * MathF.Exp(-t * 3f)) / Rate;
            if (R.NextDouble() < 0.0012 * MathF.Exp(-t * 1.1f)) deb = 0.7f;          // flying debris
            deb *= 0.994f;
            b[i] = y * MathF.Exp(-t * 1.4f) * 4f + MathF.Sin((float)ph) * MathF.Exp(-t * 1.8f) * 1.3f
                 + (w - y) * MathF.Exp(-t * 18f) * 0.8f + (w - y) * deb * 0.35f;
        }
        Reverb(b, 0.9f, 1.8f);
        return Fin(b);
    }

    static short[] GHitFlesh()
    {
        var b = Buf(0.14f);
        float y = 0;
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            y += 0.25f * (W() - y);
            ph += MathF.Tau * (70f + 80f * MathF.Exp(-t * 40f)) / Rate;
            b[i] = MathF.Sin((float)ph) * MathF.Exp(-t * 28f) + y * MathF.Exp(-t * 40f) * 0.8f;
        }
        return Fin(b);
    }

    static short[] Ping(float dur, float f1, float f2)
    {
        var b = Buf(dur);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.3f * (w - y);
            b[i] = (w - y) * MathF.Exp(-t * 120f) * 0.6f
                 + MathF.Sin(MathF.Tau * f1 * t) * MathF.Exp(-t * 22f) * 0.25f
                 + MathF.Sin(MathF.Tau * f2 * t) * MathF.Exp(-t * 30f) * 0.15f;
        }
        return Fin(b);
    }

    static short[] MetalHit()
    {
        var b = Buf(0.35f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.3f * (w - y);
            b[i] = (w - y) * MathF.Exp(-t * 90f) * 0.5f
                 + MathF.Sin(MathF.Tau * 620f * t) * MathF.Exp(-t * 14f) * 0.4f
                 + MathF.Sin(MathF.Tau * 1330f * t) * MathF.Exp(-t * 18f) * 0.3f
                 + MathF.Sin(MathF.Tau * 2150f * t) * MathF.Exp(-t * 25f) * 0.2f;
        }
        return Fin(b);
    }

    static short[] Whoosh(float dur)
    {
        var b = Buf(dur);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, u = MathF.Sin(MathF.PI * t / dur);
            y += (0.04f + 0.35f * u) * (W() - y);
            b[i] = y * u * u * 3f;
        }
        return Fin(b);
    }

    static short[] GSlash()
    {
        var b = Buf(0.16f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.15f * (w - y);
            b[i] = (w - y) * MathF.Exp(-t * 35f) * 0.9f + MathF.Sin(MathF.Tau * 110f * t) * MathF.Exp(-t * 40f) * 0.5f;
        }
        return Fin(b);
    }

    static void AddClick(float[] b, float t0, float f, float amp)
    {
        int s = (int)(t0 * Rate), n = (int)(0.08f * Rate);
        for (int j = 0; j < n && s + j < b.Length; j++)
        {
            float t = j / (float)Rate;
            b[s + j] += (W() * MathF.Exp(-t * 500f) * 0.6f
                       + MathF.Sin(MathF.Tau * f * t) * MathF.Exp(-t * 180f) * 0.6f
                       + MathF.Sin(MathF.Tau * f * 0.35f * t) * MathF.Exp(-t * 90f) * 0.5f) * amp;
        }
    }

    static short[] Click(float dur, float f, float amp)
    {
        var b = Buf(dur);
        AddClick(b, 0f, f, amp);
        return Fin(b);
    }

    static short[] ReloadSeq()
    {
        var b = Buf(0.85f);
        AddClick(b, 0.02f, 900f, 0.8f);           // magazine out
        AddClick(b, 0.40f, 600f, 1.0f);           // magazine in
        AddClick(b, 0.64f, 1400f, 0.9f);          // slide
        AddClick(b, 0.69f, 1100f, 0.6f);
        return Fin(b);
    }

    static short[] DrawSnd()
    {
        var b = Buf(0.22f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            y += 0.3f * (W() - y);
            b[i] = y * MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.12f)) * 0.5f;
        }
        AddClick(b, 0.10f, 700f, 0.7f);
        return Fin(b);
    }

    static void Thud(float[] b, float t0, float lp, float amp, float f)
    {
        int s0 = (int)(t0 * Rate), n = (int)(0.12f * Rate);
        float y = 0;
        for (int j = 0; j < n && s0 + j < b.Length; j++)
        {
            float t = j / (float)Rate;
            y += lp * (W() - y);
            b[s0 + j] += (y * MathF.Exp(-t * 40f) * 3f + MathF.Sin(MathF.Tau * f * t) * MathF.Exp(-t * 45f) * 0.6f) * amp;
        }
    }

    static short[] Step(float dur, float lp, float amp = 1f)
    {
        var b = Buf(dur + 0.1f);
        Thud(b, 0f, lp, amp, 55f + lp * 150f);                 // heel
        Thud(b, 0.075f, lp * 1.6f, amp * 0.55f, 90f + lp * 120f);   // toe
        float y = 0;
        for (int i = 0; i < b.Length; i++)                      // gravel / fabric scuff
        {
            float t = i / (float)Rate;
            y += 0.4f * (W() - y);
            b[i] += (W() - y) * MathF.Exp(-t * 30f) * 0.25f * amp;
        }
        return Fin(b);
    }

    static short[] GDoor()
    {
        var b = Buf(0.35f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            y += 0.1f * (W() - y);
            b[i] = MathF.Sin(MathF.Tau * (95f - 40f * t) * t) * MathF.Exp(-t * 22f) + y * MathF.Exp(-t * 30f) * 2f;
        }
        AddClick(b, 0.02f, 1500f, 0.5f);
        return Fin(b);
    }

    static short[] GIgnition()
    {
        var b = Buf(1.2f);
        float y = 0;
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.12f * (w - y);
            float v;
            if (t < 0.55f)
            {
                float pulse = MathF.Pow(MathF.Max(0f, MathF.Sin(MathF.Tau * 11f * t)), 2f);
                ph += MathF.Tau * (70f + 12f * pulse) / Rate;
                v = (y * 1.6f + MathF.Sin((float)ph) * 0.5f) * pulse;
            }
            else
            {
                float u = t - 0.55f;
                ph += MathF.Tau * (48f + 30f * MathF.Exp(-u * 6f)) / Rate;
                float saw = ((float)(ph / MathF.Tau) % 1f) * 2f - 1f;
                v = y * MathF.Exp(-u * 10f) * 1.5f + saw * MathF.Exp(-u * 5f) * 0.6f;
            }
            b[i] = v;
        }
        return Fin(b);
    }

    static short[] GCrash(float dur)
    {
        var b = Buf(dur);
        float y = 0, g = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.25f * (w - y);
            if (R.NextDouble() < 0.0015 * (1f - t / dur)) g = 0.8f;
            g *= 0.995f;
            b[i] = y * MathF.Exp(-t * 7f) * 3f
                 + (MathF.Sin(MathF.Tau * 310f * t) + 0.7f * MathF.Sin(MathF.Tau * 760f * t) + 0.4f * MathF.Sin(MathF.Tau * 1210f * t)) * MathF.Exp(-t * 9f) * 0.35f
                 + (w - y) * g * 0.5f;
        }
        return Fin(b);
    }

    static short[] GBump()
    {
        var b = Buf(0.25f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            y += 0.12f * (W() - y);
            b[i] = MathF.Sin(MathF.Tau * (80f + 40f * MathF.Exp(-t * 30f)) * t) * MathF.Exp(-t * 20f)
                 + y * MathF.Exp(-t * 25f) * 2f
                 + MathF.Sin(MathF.Tau * 540f * t) * MathF.Exp(-t * 28f) * 0.25f;
        }
        return Fin(b);
    }

    /// <summary>Voice-like sound: harmonic series shaped by two formant peaks (an "ah" / "uh").</summary>
    static short[] Vowel(float dur, float f0a, float f0b, float f1, float f2, float breath, float attack, float decayK, float vib)
    {
        var b = Buf(dur);
        double ph = 0;
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, u = t / dur;
            float f0 = f0a + (f0b - f0a) * u + vib * 6f * MathF.Sin(MathF.Tau * 6f * t);
            ph += MathF.Tau * f0 / Rate;
            float v = 0f;
            for (int h = 1; h <= 24; h++)
            {
                float fh = f0 * h;
                if (fh > 5000f) break;
                float d1 = (fh - f1) / 180f, d2 = (fh - f2) / 260f;
                float g = MathF.Exp(-d1 * d1) + 0.6f * MathF.Exp(-d2 * d2) + 0.04f;
                v += g * MathF.Sin((float)(h * ph)) / (1f + h * 0.35f);
            }
            y += 0.2f * (W() - y);
            float env = MathF.Min(1f, t / attack) * MathF.Exp(-t * decayK) * MathF.Min(1f, (dur - t) / 0.08f);
            b[i] = (v + y * breath * 2f) * env;
        }
        return Fin(b);
    }

    static short[] Death()
    {
        var voice = new float[(int)(1.8f * Rate)];
        var s = Vowel(1.8f, 140f, 55f, 600f, 900f, 0.3f, 0.03f, 1.3f, 0.6f);
        double ph = 0;
        for (int i = 0; i < voice.Length; i++)
        {
            float t = i / (float)Rate;
            ph += MathF.Tau * (60f - 30f * MathF.Min(1f, t / 1.8f)) / Rate;
            voice[i] = s[i] / 32000f * 0.8f + MathF.Sin((float)ph) * MathF.Exp(-t * 1.5f) * 0.6f;
        }
        return Fin(voice);
    }

    static short[] GHeartbeat()
    {
        var b = Buf(0.6f);
        float y = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, t2 = t - 0.22f;
            y += 0.06f * (W() - y);
            float lub = MathF.Sin(MathF.Tau * 58f * t) * MathF.Exp(-t * 22f) + y * MathF.Exp(-t * 30f) * 2f;
            float dub = t2 > 0 ? (MathF.Sin(MathF.Tau * 52f * t2) * MathF.Exp(-t2 * 26f) + y * MathF.Exp(-t2 * 34f)) * 0.7f : 0f;
            b[i] = lub + dub;
        }
        return Fin(b);
    }

    static short[] GBusted()
    {
        var b = Buf(1.5f);
        AddClick(b, 0.00f, 2200f, 0.9f); AddClick(b, 0.04f, 2500f, 0.8f); AddClick(b, 0.08f, 2800f, 0.8f);
        AddClick(b, 0.30f, 2300f, 1.0f); AddClick(b, 0.34f, 2600f, 0.9f);
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            if (t < 0.45f) continue;
            float u = t - 0.45f;
            ph += MathF.Tau * (440f * MathF.Exp(-u * 1.6f) + 100f) / Rate;
            b[i] += (MathF.Sin((float)ph) + 0.4f * MathF.Sin((float)(2 * ph))) * MathF.Exp(-u * 2.2f) * 0.5f;
        }
        return Fin(b);
    }

    static float Bell(float t, float f) =>
        t < 0f ? 0f : (MathF.Sin(MathF.Tau * f * t) + 0.4f * MathF.Sin(MathF.Tau * f * 2.76f * t) * MathF.Exp(-t * 6f)) * MathF.Exp(-t * 7f);

    static short[] GCash()
    {
        var b = Buf(0.7f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            b[i] = Bell(t, 1568f) + Bell(t - 0.09f, 2093f) + W() * MathF.Exp(-t * 400f) * 0.3f;
        }
        return Fin(b);
    }

    static short[] Chimes(float[] notes, float gap, float dur)
    {
        var b = Buf(dur + notes.Length * gap);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, v = 0f;
            for (int k = 0; k < notes.Length; k++) v += Bell(t - k * gap, notes[k]);
            b[i] = v;
        }
        return Fin(b);
    }

    static short[] GAlert()
    {
        var b = Buf(0.5f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, v = 0f;
            if (t < 0.10f) v = MathF.Sin(MathF.Tau * 880f * t);
            else if (t >= 0.15f && t < 0.25f) v = MathF.Sin(MathF.Tau * 1175f * t);
            else if (t >= 0.30f && t < 0.40f) v = MathF.Sin(MathF.Tau * 880f * t);
            float seg = t % 0.15f;
            b[i] = v * MathF.Min(1f, seg / 0.005f) * MathF.Min(1f, MathF.Max(0f, 0.10f - seg) / 0.01f);
        }
        return Fin(b);
    }

    static short[] Tone(float dur, float f0, float f1, float amp, float k)
    {
        var b = Buf(dur);
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            ph += MathF.Tau * (f0 + (f1 - f0) * t / dur) / Rate;
            b[i] = MathF.Sin((float)ph) * MathF.Exp(-t * k) * amp;
        }
        return Fin(b);
    }

    static short[] GBuzz()
    {
        var b = Buf(0.25f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float sq = MathF.Sin(MathF.Tau * 110f * t) > 0f ? 1f : -1f;
            b[i] = (sq * 0.5f + MathF.Sin(MathF.Tau * 220f * t) * 0.3f) * MathF.Min(1f, (0.25f - t) / 0.05f);
        }
        return Fin(b);
    }

    static short[] GReelStop()
    {
        var b = Buf(0.14f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            b[i] = MathF.Sin(MathF.Tau * 220f * t) * MathF.Exp(-t * 40f);
        }
        AddClick(b, 0f, 1300f, 0.7f);
        return Fin(b);
    }

    static short[] GLose()
    {
        var b = Buf(0.7f);
        double ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float f = t < 0.3f ? 247f : 220f - 60f * MathF.Min(1f, (t - 0.3f) / 0.4f);
            ph += MathF.Tau * f / Rate;
            float saw = ((float)(ph / MathF.Tau) % 1f) * 2f - 1f;
            b[i] = (saw * 0.35f + MathF.Sin((float)ph) * 0.5f) * MathF.Min(1f, (0.7f - t) / 0.25f);
        }
        return Fin(b);
    }

    static short[] GCard()
    {
        var b = Buf(0.13f);
        float y = 0, y2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y += 0.5f * (w - y);
            y2 += 0.1f * (w - y2);
            float swish = (y - y2) * MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.07f)) * (t < 0.07f ? 1f : 0f);
            float slap = (w - y) * MathF.Exp(-(t - 0.07f) * 70f) * (t >= 0.07f ? 1f : 0f) * 0.8f;
            b[i] = swish * 1.5f + slap;
        }
        return Fin(b);
    }

    // ================================================================ loops
    static short[] EngineLoop()
    {
        // 32 firing pulses of exactly 735 samples (60 Hz) -> a seamless loop. Every pulse is a short burst of
        // pressure; the exhaust is modelled by two resonant filters, and the pulses differ slightly (V8 burble).
        const int per = 735, pulses = 32;
        int n = per * pulses;
        float[] amp = { 1f, 0.82f, 0.95f, 0.78f, 1f, 0.88f, 0.92f, 0.80f };
        int[] jit = { 0, 9, -6, 14, -10, 4, -13, 7 };
        var x = new float[n];
        for (int p = 0; p < pulses; p++)
        {
            int start = p * per + jit[p % 8];
            for (int j = 0; j < 600; j++)
            {
                float t = j / (float)Rate;
                float v = MathF.Sin(MathF.Tau * (90f + 50f * MathF.Exp(-t * 60f)) * t) * MathF.Exp(-t * 42f)
                        + W() * MathF.Exp(-t * 130f) * 0.35f;
                x[((start + j) % n + n) % n] += v * amp[p % 8];
            }
        }
        var o = new float[n];
        float l1 = 0, b1 = 0, l2 = 0, b2 = 0;
        float f1 = 2f * MathF.Sin(MathF.PI * 170f / Rate), f2 = 2f * MathF.Sin(MathF.PI * 520f / Rate);
        for (int pass = 0; pass < 2; pass++)                       // second pass starts with settled filter state
            for (int i = 0; i < n; i++)
            {
                float xi = x[i];
                float h1 = xi - l1 - 0.35f * b1; b1 += f1 * h1; l1 += f1 * b1;
                float h2 = xi - l2 - 0.45f * b2; b2 += f2 * h2; l2 += f2 * b2;
                o[i] = l1 * 1.3f + l2 * 0.7f + xi * 0.12f;
            }
        return Fin(o, 0.8f);
    }

    /// <summary>Seamless band-limited noise (road roar, wind): two one-pole filters, tail cross-faded into the head.</summary>
    static short[] NoiseLoop(float sec, float a1, float a2, float gain)
    {
        int n = (int)(sec * Rate), f = Rate / 4;
        var raw = new float[n + f];
        float y1 = 0, y2 = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            float w = W();
            y1 += a1 * (w - y1);
            y2 += a2 * (w - y2);
            raw[i] = (y1 - y2) * gain;
        }
        var b = new float[n];
        for (int i = 0; i < n; i++)
            b[i] = i < f ? raw[i] * (i / (float)f) + raw[n + i] * (1f - i / (float)f) : raw[i];
        return Fin(b, 0.7f);
    }

    static short[] SirenLoop()
    {
        int n = 4 * Rate;
        var ph = new double[n];
        double p = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = 700f + 500f * (0.5f - 0.5f * MathF.Cos(MathF.Tau * t / 1.0f));
            p += MathF.Tau * f / Rate;
            ph[i] = p;
        }
        double k = Math.Round(p / (2 * Math.PI)) * 2 * Math.PI / p;    // whole number of cycles -> seamless loop
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            float x = (float)(ph[i] * k);
            b[i] = MathF.Sin(x) + 0.35f * MathF.Sin(2f * x) + 0.2f * MathF.Sin(3f * x);
        }
        return Fin(b, 0.7f);
    }

    static short[] ScreechLoop()
    {
        var b = new float[Rate];
        float y1 = 0, y2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y1 += 0.35f * (w - y1);
            y2 += 0.08f * (w - y2);
            b[i] = (y1 - y2) * (1f + 0.4f * MathF.Sin(MathF.Tau * 37f * t)) + MathF.Sin(MathF.Tau * 1450f * t) * 0.12f;
        }
        return Fin(b, 0.7f);
    }

    static short[] AmbienceLoop()
    {
        int n = 6 * Rate, f = Rate / 2;
        var raw = new float[n + f];
        float y1 = 0, y2 = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate, w = W();
            y1 += 0.004f * (w - y1);
            y2 += 0.03f * (w - y2);
            float swell = 1f + 0.5f * MathF.Sin(MathF.Tau * 2f * t / 6f);
            raw[i] = y1 * 18f + (y2 - y1) * 3f * swell + MathF.Sin(MathF.Tau * 55f * t) * 0.05f;
        }
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = i < f ? i / (float)f : 1f;                // crossfade the tail into the head
            b[i] = i < f ? raw[i] * a + raw[n + i] * (1f - a) : raw[i];
        }
        return Fin(b, 0.6f);
    }

    // ================================================================ music
    static float Hz(int m) => 440f * MathF.Pow(2f, (m - 69) / 12f);

    static void Note(float[] b, float t0, float len, float f, float amp, int tim)
    {
        int s = (int)(t0 * Rate), n = (int)(len * Rate);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, v, env, atk = MathF.Min(1f, t * 300f);
            switch (tim)
            {
                case 0:     // saw pluck
                    v = 0f;
                    for (int k = 1; k <= 6; k++) v += MathF.Sin(MathF.Tau * f * k * t) / k;
                    v *= 0.55f; env = atk * MathF.Exp(-t * 6f); break;
                case 1:     // synth bass
                    v = MathF.Sin(MathF.Tau * f * t) + 0.45f * MathF.Sin(MathF.Tau * f * 2f * t) + 0.25f * MathF.Sin(MathF.Tau * f * 3f * t);
                    env = atk * MathF.Exp(-t * 4f); break;
                case 2:     // pad
                    v = (MathF.Sin(MathF.Tau * f * t) + MathF.Sin(MathF.Tau * f * 1.004f * t) + MathF.Sin(MathF.Tau * f * 0.996f * t)) / 3f;
                    env = MathF.Min(1f, t * 2f) * MathF.Max(0f, MathF.Min(1f, (len - t) * 2f)); break;
                case 3:     // electric piano
                    v = MathF.Sin(MathF.Tau * f * t) + 0.45f * MathF.Sin(MathF.Tau * f * 2f * t) * MathF.Exp(-t * 7f) + 0.12f * MathF.Sin(MathF.Tau * f * 4f * t) * MathF.Exp(-t * 12f);
                    env = atk * MathF.Exp(-t * 3.2f); break;
                default:    // upright bass
                    v = MathF.Sin(MathF.Tau * f * t) + 0.5f * MathF.Sin(MathF.Tau * f * 2f * t) * MathF.Exp(-t * 6f) + 0.15f * MathF.Sin(MathF.Tau * f * 3f * t);
                    env = atk * MathF.Exp(-t * 4.5f); break;
            }
            b[(s + i) % b.Length] += v * env * amp;       // the tail wraps around: the loop stays seamless
        }
    }

    static void Kick(float[] b, float t0, float amp)
    {
        int s = (int)(t0 * Rate), n = (int)(0.3f * Rate);
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            ph += MathF.Tau * (42f + 110f * MathF.Exp(-t * 28f)) / Rate;
            b[(s + i) % b.Length] += MathF.Sin((float)ph) * MathF.Exp(-t * 14f) * amp;
        }
    }

    static void Snare(float[] b, float t0, float amp)
    {
        int s = (int)(t0 * Rate), n = (int)(0.2f * Rate);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            b[(s + i) % b.Length] += (W() * MathF.Exp(-t * 20f) * 0.6f + MathF.Sin(MathF.Tau * 190f * t) * MathF.Exp(-t * 25f) * 0.4f) * amp;
        }
    }

    static void Hat(float[] b, float t0, float amp)
    {
        int s = (int)(t0 * Rate), n = (int)(0.06f * Rate);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, w = W();
            b[(s + i) % b.Length] += (w - prev) * MathF.Exp(-t * 90f) * amp * 0.5f;
            prev = w;
        }
    }

    /// <summary>style 0 = synthwave (main menu), style 1 = casino lounge. Both are 8 bars and loop seamlessly.</summary>
    static short[] Song(int style)
    {
        float bpm = style == 0 ? 104f : 108f, beat = 60f / bpm, bar = beat * 4f;
        const int bars = 8;
        var b = new float[(int)(bar * bars * Rate)];

        if (style == 0)
        {
            int[][] ch = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };
            int[] prog = { 0, 1, 2, 3, 0, 1, 2, 3 };
            int[] pat = { 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 4, 3, 2, 1, 2, 1 };
            for (int bi = 0; bi < bars; bi++)
            {
                int[] c = ch[prog[bi]];
                float t0 = bi * bar;
                foreach (int m in c) Note(b, t0, bar, Hz(m), 0.10f, 2);
                int[] arp = { c[0] + 12, c[1] + 12, c[2] + 12, c[0] + 24, c[1] + 24, c[2] + 24 };
                for (int s = 0; s < 16; s++) Note(b, t0 + s * beat / 4f, 0.22f, Hz(arp[pat[s]]), 0.16f, 0);
                for (int j = 0; j < 8; j++) Note(b, t0 + j * beat / 2f, beat * 0.45f, Hz(c[0] - 12), j % 2 == 0 ? 0.30f : 0.20f, 1);
                for (int q = 0; q < 4; q++) Kick(b, t0 + q * beat, 0.55f);
                Snare(b, t0 + beat, 0.35f); Snare(b, t0 + 3f * beat, 0.35f);
                for (int j = 0; j < 4; j++) Hat(b, t0 + (j + 0.5f) * beat, 0.25f);
            }
        }
        else
        {
            int[][] ch = { new[] { 50, 53, 57, 60 }, new[] { 55, 59, 62, 65 }, new[] { 48, 52, 55, 59 }, new[] { 57, 61, 64, 67 } };
            int[] prog = { 0, 1, 2, 3, 0, 1, 2, 2 };
            for (int bi = 0; bi < bars; bi++)
            {
                int[] c = ch[prog[bi]];
                float t0 = bi * bar;
                foreach (int m in c) Note(b, t0, 1.2f, Hz(m), 0.13f, 3);
                foreach (int m in c) Note(b, t0 + 2.5f * beat, 0.9f, Hz(m), 0.10f, 3);
                int root = c[0] % 12 + 36;
                int[] walk = { 0, 7, 12, 7 };
                for (int q = 0; q < 4; q++) Note(b, t0 + q * beat, beat * 0.9f, Hz(root + walk[q]), 0.35f, 4);
                Kick(b, t0, 0.25f);
                Snare(b, t0 + beat, 0.12f); Snare(b, t0 + 3f * beat, 0.12f);
                for (int j = 0; j < 8; j++) Hat(b, t0 + j * beat / 2f + (j % 2 == 1 ? beat * 0.17f : 0f), 0.20f);
            }
        }
        return Fin(b, 0.85f);
    }
}
