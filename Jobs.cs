using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public enum JKind { Hit, Car, Rob }

/// <summary>
/// Sal the fixer. He sits in the hall of one casino and hands out endless random jobs:
/// kill somebody, bring him a car of a given model, or rob a courier. Pay arrives the moment the job is done.
/// </summary>
public static class Jobs
{
    public const string CasinoName = "Lucky Star";
    static readonly Vector3 SalPos = new(Interior.C.X, 0f, Interior.C.Z + 6f);
    public static Vector3 SalWorldPos => SalPos;

    // ---------------------------------------------------------------- state
    public static bool Active;
    public static JKind Kind;
    public static int Reward;
    static string title = "", objective = "", victim = "";
    static Npc target;
    static Vector3 lastPos;
    static bool bagDropped;
    static Vector3 bagPos;
    static CarKind wantKind;
    static string wantName = "";
    static Building home;
    static int done;

    public static bool MenuOpen;
    static int sel;

    static readonly string[] Names =
    {
        "Rico", "Big Tony", "Mad Mike", "Lenny", "Dutch Jr.", "Skinny Pete", "Vinnie Two-Shoes", "Gus the Gambler",
        "Little Joe", "Black Jack", "Marty", "Slick Eddie"
    };

    public static void Reset()
    {
        ClearTarget();
        Active = false; bagDropped = false; MenuOpen = false; sel = 0; done = 0;
    }

    static void ClearTarget()
    {
        if (target != null) target.Marked = false;
        target = null;
    }

    static void Say(string s) => Missions.Say?.Invoke(s);

    public static Building Home(World w) => home ??= w.Casinos.Find(b => b.Name == CasinoName) ?? w.Casinos.Find(b => b.Name != "Royal Flush");

    // ---------------------------------------------------------------- Sal in the casino hall
    public static bool IsHere => Interior.Active && Interior.Name == (home?.Name ?? CasinoName);

    public static void OnEnter(Building cas, World w)
    {
        if (cas != Home(w)) return;
        Story.PlaceInside("Sal", SalPos, MathF.PI);
        Say("Sal the fixer sits in the back of this casino - talk to him (E) for jobs");
    }

    public static bool NearSal(Vector3 p) => IsHere && U.Dist2D(p, SalPos) < 3.4f;

    // ---------------------------------------------------------------- menu
    public static void OpenMenu() { MenuOpen = true; sel = 0; }

    /// <summary>Returns true while the menu is open (the game is frozen).</summary>
    public static void UpdateMenu(World w, List<Npc> npcs, List<Car> cars, Player pl)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressed(KeyboardKey.Q))
        { MenuOpen = false; return; }
        int n = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) sel = (sel + 1) % n;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) sel = (sel + n - 1) % n;
        if (!(Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Space))) return;

        if (sel == 1) { MenuOpen = false; return; }
        if (Active)
        {
            if (pl.Money < 150) { Say("Not enough money to cancel ($150)"); return; }
            pl.Money -= 150;
            ClearTarget();
            Active = false;
            MenuOpen = false;
            Story.Play(new[] { new Line("Sal", "You walk away from a job and it costs you $150. Come back when you're serious.") });
            return;
        }
        if (Generate(w, npcs, cars))
        {
            MenuOpen = false;
        }
        else Say("Sal has nothing for you right now - try again in a minute");
    }

    public static void DrawMenu(Player pl)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 150));
        int w = Math.Min(760, sw - 60), h = 330;
        int x = (sw - w) / 2, y = (sh - h) / 2;
        Raylib.DrawRectangle(x, y, w, h, U.C(14, 16, 28, 245));
        Raylib.DrawRectangleLines(x, y, w, h, U.C(90, 220, 140));
        Raylib.DrawText("SAL - JOBS", x + 24, y + 18, 34, U.C(90, 220, 140));
        Raylib.DrawText($"Jobs done: {done}", x + w - 24 - Raylib.MeasureText($"Jobs done: {done}", 18), y + 28, 18, U.C(170, 170, 190));

        string info = Active ? $"Current job: {title} - ${Reward}" : "\"No questions, cash on delivery. Want some work?\"";
        Raylib.DrawText(info, x + 24, y + 72, 22, Active ? U.C(255, 225, 60) : U.C(220, 220, 235));
        if (Active) Raylib.DrawText(objective, x + 24, y + 102, 18, U.C(190, 190, 205));

        string[] opts = { Active ? "Cancel the current job  (-$150)" : "Give me a job", "Leave" };
        for (int i = 0; i < opts.Length; i++)
        {
            var r = new Rectangle(x + 24, y + 150 + i * 62, w - 48, 52);
            bool hot = sel == i;
            Raylib.DrawRectangleRec(r, hot ? U.C(46, 70, 66) : U.C(28, 30, 48));
            if (hot) Raylib.DrawRectangleLinesEx(r, 2f, U.C(90, 220, 140));
            Raylib.DrawText(opts[i], (int)r.X + 18, (int)r.Y + 14, 24, hot ? U.C(255, 255, 255) : U.C(190, 190, 205));
        }
        Raylib.DrawText("Up/Down - choose    E / Enter - confirm    Esc - leave", x + 24, y + h - 32, 18, U.C(150, 150, 172));
    }

    // ---------------------------------------------------------------- making a job
    static bool Generate(World w, List<Npc> npcs, List<Car> cars)
    {
        ClearTarget();
        bagDropped = false;
        JKind k = (JKind)U.R.Next(3);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (k == JKind.Car && MakeCar(w, cars)) return true;
            if (k != JKind.Car && MakePerson(k, npcs)) return true;
            k = (JKind)(((int)k + 1) % 3);
        }
        return false;
    }

    static Npc PickNpc(List<Npc> npcs)
    {
        Vector3 from = Story.PlayerPos;
        var pool = new List<Npc>();
        foreach (var n in npcs)
            if (!n.Dead && !n.Marked)
            {
                float d = U.Dist2D(n.Pos, from);
                if (d > 90f && d < 450f) pool.Add(n);
            }
        if (pool.Count == 0)
            foreach (var n in npcs) if (!n.Dead && !n.Marked) pool.Add(n);
        return pool.Count == 0 ? null : pool[U.R.Next(pool.Count)];
    }

    static bool MakePerson(JKind k, List<Npc> npcs)
    {
        Npc n = PickNpc(npcs);
        if (n == null) return false;
        target = n;
        n.Marked = true;
        victim = Names[U.R.Next(Names.Length)];
        Kind = k;
        lastPos = n.Pos;
        if (k == JKind.Hit)
        {
            n.MakeBoss(90f);
            Reward = U.R.Next(8, 16) * 100;
            title = "Hit: " + victim;
            objective = $"Kill {victim} - red beam. He fights back and the cops will care.";
            Story.Play(new[]
            {
                new Line("Sal", $"There's a man called {victim}. He owes the wrong people and he talks too much."),
                new Line("Sal", $"He's out on the streets right now - I marked him red on your map. Make him quiet. ${Reward}.")
            });
        }
        else
        {
            n.Dress(U.C(40, 150, 90), U.C(150, 130, 96));
            n.Hp = 70f;
            Reward = U.R.Next(10, 23) * 100;
            title = "Rob: " + victim;
            objective = $"Take down {victim} (green beam), then grab his bag from the ground.";
            Story.Play(new[]
            {
                new Line("Sal", $"{victim} walks cash around town for a rival outfit. Green jacket, a bag in his hand."),
                new Line("Sal", $"Put him down, take the bag - it's worth about ${Reward} and I don't want to hear where it came from.")
            });
        }
        Active = true;
        return true;
    }

    static bool MakeCar(World w, List<Car> cars)
    {
        var pool = new List<Car>();
        foreach (var c in cars)
            if (!c.Police && !c.Owned && !c.Player && c.Type != null) pool.Add(c);
        if (pool.Count == 0) return false;
        Car pick = pool[U.R.Next(pool.Count)];
        wantKind = pick.Type.Kind;
        wantName = pick.Type.Name;
        Kind = JKind.Car;
        Reward = (pick.Type.PriceMin + pick.Type.PriceMax) / 2 + 400;
        Reward = Reward / 50 * 50;
        title = "Wheels: " + wantName;
        objective = $"Bring a {wantName} to the {CasinoName} casino and stop outside the door (E).";
        Active = true;
        Story.Play(new[]
        {
            new Line("Sal", $"A buyer wants a {wantName}. Not a different one. A {wantName}."),
            new Line("Sal", $"Steal one, drive it to my casino - the {CasinoName} - and stop at the door. ${Reward}, cash.")
        });
        return true;
    }

    // ---------------------------------------------------------------- progress
    static void Finish(Player pl, string msg)
    {
        pl.Money += Reward;
        done++;
        Say($"{msg}  +${Reward}");
        ClearTarget();
        Active = false; bagDropped = false;
    }

    public static void Update(Player pl, World w)
    {
        if (!Active) return;
        switch (Kind)
        {
            case JKind.Hit:
                if (target == null || target.Dead) Finish(pl, "Job done");
                break;
            case JKind.Rob:
                if (!bagDropped)
                {
                    if (target != null && !target.Dead) lastPos = target.Pos;
                    else
                    {
                        bagDropped = true;
                        bagPos = new Vector3(lastPos.X, 0f, lastPos.Z);
                        ClearTarget();
                        Say("Grab the bag!");
                    }
                }
                else if (pl.Car == null && U.Dist2D(pl.Pos, bagPos) < 2.3f) Finish(pl, "Bag taken");
                break;
        }
    }

    // ---------------------------------------------------------------- delivering a car
    public static bool CanDeliver(Player pl, World w, out string hint)
    {
        hint = "";
        if (!Active || Kind != JKind.Car || pl.Car == null) return false;
        Building h = Home(w);
        if (h == null || U.Dist2D(pl.Car.Pos, h.Door) > 11f) return false;
        Car c = pl.Car;
        if (c.Police || c.Owned) { hint = "Sal does not want this car"; return true; }
        if (c.Type.Kind != wantKind) { hint = $"Sal wants a {wantName}, not a {c.Type.Name}"; return true; }
        if (MathF.Abs(c.Speed) > 3f) { hint = "Stop the car to hand it over"; return true; }
        hint = $"E - hand the {wantName} over to Sal (+${Reward})";
        return true;
    }

    public static bool CarOk(Player pl) =>
        Active && Kind == JKind.Car && pl.Car != null && !pl.Car.Police && !pl.Car.Owned && pl.Car.Type.Kind == wantKind;

    public static void Delivered(Player pl) => Finish(pl, "Car delivered");

    // ---------------------------------------------------------------- map / beams / HUD
    /// <summary>Where the current job wants you to go: the target, the bag, the nearest car of that model or Sal's casino.</summary>
    public static Vector3? ObjectivePos(World w, List<Car> cars, Player pl)
    {
        if (!Active) return null;
        switch (Kind)
        {
            case JKind.Hit: return target != null && !target.Dead ? target.Pos : null;
            case JKind.Rob: return bagDropped ? bagPos : target != null ? target.Pos : null;
            default:
                if (CarOk(pl)) return Home(w)?.Door;
                Car best = null;
                float bd = float.MaxValue;
                foreach (var c in cars)
                {
                    if (c.Police || c.Owned || c.Player || c.Type.Kind != wantKind) continue;
                    float d = U.Dist2D(c.Pos, pl.Pos);
                    if (d < bd) { bd = d; best = c; }
                }
                return best != null ? best.Pos : Home(w)?.Door;
        }
    }

    public static void Draw3D(World w, List<Car> cars, Player pl, float time)
    {
        if (!Active) return;
        if (Kind == JKind.Hit || (Kind == JKind.Rob && !bagDropped))
        {
            if (target == null || target.Dead) return;
            Color c = Kind == JKind.Hit ? U.C(255, 50, 50) : U.C(60, 230, 110);
            Vector3 p = target.Pos;
            Gfx.Flat(new Vector3(p.X, 16f, p.Z), new Vector3(0.3f, 27f, 0.3f), 0f, c);
            Gfx.Flat(new Vector3(p.X, 2.25f + 0.12f * MathF.Sin(time * 4f), p.Z), new Vector3(0.45f, 0.45f, 0.45f), time * 2f, c);
        }
        else if (Kind == JKind.Rob)
        {
            Color c = U.C(60, 230, 110);
            Gfx.Box(new Vector3(bagPos.X, 0.22f, bagPos.Z), new Vector3(0.7f, 0.44f, 0.5f), time, U.C(70, 50, 30));
            Gfx.Flat(new Vector3(bagPos.X, 0.46f, bagPos.Z), new Vector3(0.4f, 0.04f, 0.3f), time, U.C(60, 200, 90));
            Gfx.Flat(new Vector3(bagPos.X, 20f, bagPos.Z), new Vector3(0.3f, 40f, 0.3f), 0f, c);
        }
        else if (CarOk(pl))
        {
            Building h = Home(w);
            if (h == null) return;
            Color c = U.C(80, 220, 255);
            Gfx.Flat(new Vector3(h.Door.X, 23f, h.Door.Z), new Vector3(0.45f, 46f, 0.45f), 0f, c);
        }
        else
        {
            // light up the nearest car of the wanted model so it is easy to find
            Car best = null;
            float bd = 160f;
            foreach (var cr in cars)
            {
                if (cr.Police || cr.Owned || cr.Player || cr.Type.Kind != wantKind) continue;
                float d = U.Dist2D(cr.Pos, pl.Pos);
                if (d < bd) { bd = d; best = cr; }
            }
            if (best != null)
                Gfx.Flat(new Vector3(best.Pos.X, 14f, best.Pos.Z), new Vector3(0.3f, 22f, 0.3f), 0f, U.C(80, 220, 255));
        }
    }

    public static void DrawHud(int sh)
    {
        if (!Active || Story.Talking) return;
        string head = "SAL'S JOB - " + title + $"  (${Reward})";
        int x = 20, y = sh - 104;
        Raylib.DrawText(head, x + 1, y + 1, 16, U.C(0, 0, 0, 180));
        Raylib.DrawText(head, x, y, 16, U.C(90, 220, 140));
        Raylib.DrawText(objective, x + 1, y + 21, 18, U.C(0, 0, 0, 180));
        Raylib.DrawText(objective, x, y + 20, 18, U.C(255, 255, 255));
    }
}
