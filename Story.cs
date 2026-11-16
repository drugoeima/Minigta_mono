using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>One line of a conversation (Who = speaker) or one title card (Card = true, Who = the small heading above the text).</summary>
public class Line
{
    public string Who, Text;
    public bool Card;
    public Line(string who, string text, bool card = false) { Who = who; Text = text; Card = card; }
}

/// <summary>A character who stands in the scene while a conversation is played (Vince at the marker, Kessler and Danny in the casino).</summary>
public class Actor
{
    public string Name;
    public Vector3 Pos;
    public float Yaw;
    public HumanLook Look;
    public bool Inside;                 // true = stands in the casino hall, false = in the city
}

public enum SKind { Talk, GoTo, BuyWeapon, SellCar, KillTarget, EnterCasino, PayDebt, Count, OwnHome }

/// <summary>One step of the story: what to do, what is said before it starts and after it is done.</summary>
public class Step
{
    public SKind Kind;
    public string Chapter = "", Objective = "";
    public Line[] Before, After;
    public int Reward, Cost;
    public string Casino = "";          // Royal Flush: the casino used by the "enter" / "pay" / "kill Dutch" steps
    public Vector3 At;                  // GoTo: where to stand; KillTarget without a casino: where the target stands
    public MKind Ev;                    // Count: which event is counted (kills, stolen cars, ...)
    public int Need;                    // Count: how many
    public string[] Stage;              // casino steps: who waits in the hall during the conversation
    public float TargetHp = 150f;       // KillTarget: how tough he is
    public bool VinceLook;              // KillTarget: the target is dressed like Vince
}

/// <summary>
/// A short linear story told in five chapters (about a dozen conversations and title cards).
/// The city freezes while somebody is talking; E / Space / Enter / click shows the next line.
/// Progress is a single number (the index of the current step) kept in the save file.
/// </summary>
public static class Story
{
    const string RF = "Royal Flush";

    // ---------------------------------------------------------------- the script
    public static readonly Step[] Steps =
    {
        // ---- chapter 1
        new Step { Kind = SKind.Talk, Chapter = "Chapter 1 - Danny's Debt",
            Before = new[]
            {
                new Line("LAS VEGAS", "The desert does not care who you were. It only cares what you owe.", true),
                new Line("CHAPTER 1 - DANNY'S DEBT", "Three days ago your brother Danny called you from a pay phone. He said one word - \"run\". Then the line went dead.", true),
            } },

        new Step { Kind = SKind.GoTo, Chapter = "Chapter 1 - Danny's Debt", Objective = "Meet Vince at the yellow marker",
            At = new Vector3(4 * World.Step + World.Side, 0f, 3 * World.Step + World.Side),
            Before = new[]
            {
                new Line("Vince (phone)", "You Danny's brother? I know where he got into trouble. Corner east of where you landed. Yellow marker. Come alone."),
            },
            After = new[]
            {
                new Line("Vince", "Danny borrowed $2,500 from Mr. Kessler - he owns the Royal Flush casino. He lost it all in one night and ran."),
                new Line("You", "Where is he now?"),
                new Line("Vince", "Kessler's people have him, or they want you to think so. I can get you to Kessler, but not with empty hands."),
                new Line("Vince", "Get a gun first. AMMU-NATION - the red signs. A man with a gun gets listened to."),
                new Line("You", "I have $150."),
                new Line("Vince", "Then buy a pistol and stop talking about money."),
            } },

        new Step { Kind = SKind.BuyWeapon, Chapter = "Chapter 1 - Danny's Debt", Objective = "Buy a weapon at an AMMU-NATION (red squares on the map)",
            After = new[]
            {
                new Line("Vince (phone)", "Now you look like somebody Kessler might take seriously. Next lesson: cash. Kessler only respects cash."),
            } },

        // ---- chapter 2
        new Step { Kind = SKind.SellCar, Chapter = "Chapter 2 - Cash Flow", Objective = "Steal a car and sell it at the Scrap Yard (orange beacon, east)",
            Reward = 600,
            Before = new[]
            {
                new Line("CHAPTER 2 - CASH FLOW", "Nobody in this town gives a stranger a loan. They give him a lesson.", true),
                new Line("Vince (phone)", "Steal a car - any car. Take it to the Scrap Yard east of the city and sell it. The cops will notice, so be quick."),
            },
            After = new[]
            {
                new Line("Vince (phone)", "Clean work. That's $600 out of my own cut - consider it an investment in you."),
            } },

        // ---- chapter 3
        new Step { Kind = SKind.KillTarget, Chapter = "Chapter 3 - A Message For Dutch", Objective = "Kill Dutch - the red beam outside Royal Flush", Casino = RF,
            Reward = 800,
            Before = new[]
            {
                new Line("CHAPTER 3 - A MESSAGE FOR DUTCH", "Every boss has a dog. Before you can talk to the boss, you have to deal with the dog.", true),
                new Line("Vince (phone)", "Dutch is Kessler's collector. He's standing on the sidewalk outside the Royal Flush right now, shaking people down."),
                new Line("Vince (phone)", "Take him out and Kessler will want to meet you. Dutch is tough and he fights back. Expect the police, too."),
            },
            After = new[]
            {
                new Line("Vince (phone)", "Dutch is down. The whole Strip is talking about it. Here's $800 - you earned it."),
                new Line("Vince (phone)", "Kessler is asking who did it. Walk into his casino before he sends someone else for you."),
            } },

        // ---- chapter 4
        new Step { Kind = SKind.EnterCasino, Chapter = "Chapter 4 - The Boss", Objective = "Go inside the Royal Flush casino (yellow marker)", Casino = RF, Stage = new[] { "Kessler" },
            Before = new[]
            {
                new Line("CHAPTER 4 - THE BOSS", "The Royal Flush never sleeps. Neither does the man who owns it.", true),
            },
            After = new[]
            {
                new Line("Kessler", "So you're the brother. You killed my man on my own doorstep. That takes nerve - or stupidity."),
                new Line("You", "Where's Danny?"),
                new Line("Kessler", "Safe. For now. Danny owed me $2,500. Dutch's funeral will cost me more, but I'm a reasonable man."),
                new Line("Kessler", "Bring me the $2,500 in cash and we call it even. Danny walks out of here. Don't make me wait."),
            } },

        // ---- chapter 5
        new Step { Kind = SKind.PayDebt, Chapter = "Chapter 5 - Paid In Full", Objective = "Bring the cash to Kessler - enter the Royal Flush", Casino = RF, Cost = 2500, Stage = new[] { "Kessler", "Danny" },
            Reward = 500,
            Before = new[]
            {
                new Line("CHAPTER 5 - PAID IN FULL", "Two and a half thousand dollars. Earn it, steal it, win it at the tables - Vegas doesn't ask how.", true),
                new Line("Vince (phone)", "Missions, cars, the casinos - get the money any way you like. Just don't walk in there short."),
            },
            After = new[]
            {
                new Line("Kessler", "Pleasure doing business. Your brother is in the back room. Try not to let him near my tables again."),
                new Line("Danny", "I thought I could win it back, I swear. I'm sorry... I'm so sorry. You came for me."),
                new Line("You", "Next time, call before you disappear."),
                new Line("Danny", "Here - it's all I have left. A chip. Don't spend it all in one place."),
                new Line("Danny", "Wait. Look at it - it says NEON PALACE. That's not a Royal Flush chip."),
                new Line("Danny", "I didn't lose that money by accident. Somebody dealt me into that game on purpose."),
                new Line("You", "Who?"),
                new Line("Danny", "I don't know yet. But Kessler wasn't the top of this ladder."),
            } },

        // ---- chapter 6
        new Step { Kind = SKind.Count, Ev = MKind.Kill, Need = 6, Chapter = "Chapter 6 - Loose Ends", Objective = "Defend yourself - kill 6 people",
            Reward = 900,
            Before = new[]
            {
                new Line("CHAPTER 6 - LOOSE ENDS", "A debt that gets paid so easily was never the real debt.", true),
                new Line("Vince (phone)", "Danny is out and you are breathing - and now somebody wants that chip back. Men in grey suits are asking about you on the Strip."),
                new Line("Vince (phone)", "Don't wait for them. Whoever comes at you, put them down. Six bodies will send a message people can read."),
            },
            After = new[]
            {
                new Line("Vince (phone)", "Six. That's a message in any language. Here's $900 for the ammunition."),
            } },

        // ---- chapter 7
        new Step { Kind = SKind.OwnHome, Chapter = "Chapter 7 - Safe House", Objective = "Buy a home on the west edge of the city (red squares on the map)",
            Before = new[]
            {
                new Line("CHAPTER 7 - SAFE HOUSE", "Every man needs one door that locks from the inside.", true),
                new Line("Vince (phone)", "Danny can't sleep in a hotel with his name on the register. Buy a place - the Studio Flat on the west edge is the cheapest, $5,000."),
                new Line("Vince (phone)", "It has a garage, too. Park a car in it and nobody takes it from you."),
            },
            After = new[]
            {
                new Line("Vince (phone)", "Now Danny has a bed and you have a back door. Time to find out who dealt that hand: the Neon Palace. Walk in like you own it."),
            } },

        // ---- chapter 8
        new Step { Kind = SKind.EnterCasino, Chapter = "Chapter 8 - The Neon Palace", Objective = "Go inside the Neon Palace casino (yellow marker)", Casino = "Neon Palace", Stage = new[] { "Vane" },
            Before = new[]
            {
                new Line("CHAPTER 8 - THE NEON PALACE", "The brightest sign in town hangs over the darkest room.", true),
            },
            After = new[]
            {
                new Line("Vane", "The brother who pays his debts and kills collectors. Danny's chip found its way home, I see."),
                new Line("You", "You set him up."),
                new Line("Vane", "I set up a great many men. Kessler was my dog. You buried him, and I am tired of buying dogs."),
                new Line("Vane", "I'd rather hire a wolf. A man called Brick skims my tables at the Golden Nugget. Remove him, and we will talk about Danny's future."),
                new Line("You", "And if I say no?"),
                new Line("Vane", "Then Danny remembers why he ran."),
            } },

        new Step { Kind = SKind.KillTarget, Chapter = "Chapter 8 - The Neon Palace", Objective = "Kill Brick - the red beam outside the Golden Nugget", Casino = "Golden Nugget", TargetHp = 220f,
            Reward = 1500,
            Before = new[]
            {
                new Line("Vince (phone)", "Vane has his hooks in deep. Brick is on the sidewalk outside the Golden Nugget. Big man, bad temper, lots of friends with badges."),
            },
            After = new[]
            {
                new Line("Vince (phone)", "Brick is down. $1,500 - and Vane wants to see you again. Bring $5,000. He doesn't open doors for the poor."),
            } },

        // ---- chapter 9
        new Step { Kind = SKind.PayDebt, Chapter = "Chapter 9 - The Price", Objective = "Bring the cash to Vane - enter the Neon Palace", Casino = "Neon Palace", Cost = 5000, Stage = new[] { "Vane" },
            Reward = 1000,
            Before = new[]
            {
                new Line("CHAPTER 9 - THE PRICE", "Five thousand dollars. In Vegas, trust is just another thing with a price tag.", true),
            },
            After = new[]
            {
                new Line("Vane", "Five thousand, on time. A man who pays on time is a man I can use. Danny is yours - he was never mine to keep."),
                new Line("Vane", "Now, a gift. You wondered how I found a nobody like your brother? Someone gave me his name. Someone who needed a favour."),
                new Line("You", "Who?"),
                new Line("Vane", "I'm a businessman, I don't gossip. But I'd call whoever has been on the phone with you since you landed."),
                new Line("You", "Vince."),
            } },

        // ---- chapter 10
        new Step { Kind = SKind.GoTo, Chapter = "Chapter 10 - The Traitor", Objective = "Meet Vince at the Scrap Yard (yellow marker, far east)",
            At = World.YardPos + new Vector3(-10f, 0f, 0f),
            Before = new[]
            {
                new Line("CHAPTER 10 - THE TRAITOR", "The knife never comes from the man in front of you.", true),
                new Line("Vince (phone)", "I heard you saw Vane. Come to the yard - alone. I've got money, a car, and a way out of town for you and Danny."),
            },
            After = new[]
            {
                new Line("Vince", "You came. Good. Then you know. Danny's debt was real - it just wasn't to Kessler. It was mine."),
                new Line("You", "You gave Vane his name."),
                new Line("Vince", "I owed the wrong people. Your brother was a sucker with a pulse. You were supposed to find a body, not become a problem."),
                new Line("You", "You gave me a pistol. A car job. Six hundred dollars."),
                new Line("Vince", "Guilt money. Don't make this ugly - I have friends."),
                new Line("You", "Not anymore."),
            } },

        new Step { Kind = SKind.KillTarget, Chapter = "Chapter 10 - The Traitor", Objective = "Kill Vince - the red beam", VinceLook = true, TargetHp = 180f,
            At = World.YardPos + new Vector3(-10f, 0f, 0f),
            Reward = 2000,
            After = new[]
            {
                new Line("Danny (phone)", "I heard sirens from the flat. Tell me it's over."),
                new Line("You", "It's over. I'm coming home."),
                new Line("Danny (phone)", "Good. I'll make the coffee. And thank you - for everything."),
                new Line("THE END", "Danny walked out of the Royal Flush that night. Vince lay in the dust of the Scrap Yard. Every debt was paid - all of them.", true),
                new Line("THE END", "Vegas is still open. The tables, the cars, the cops and Sal's jobs are all yours.", true),
            } },
    };

    // ---------------------------------------------------------------- state
    public static int Index;                // the current step (Steps.Length = story finished)
    public static bool Done => Index >= Steps.Length;
    static int count;                       // progress of a Count step
    static bool begun;                      // the "before" lines of the current step were already shown
    static Player P;
    static Npc target;

    // ---------------------------------------------------------------- the cast (people who are really standing in the scene)
    public static readonly List<Actor> Cast = new();
    static Actor lastActor;

    static HumanLook LookOf(string name) => name switch
    {
        "Vince" => new HumanLook { Shirt = U.C(205, 150, 40), Pants = U.C(40, 44, 60), Skin = U.C(214, 170, 130), Hair = U.C(36, 26, 20), Shoes = U.C(20, 20, 22), LongSleeve = true, Variant = 3 },
        "Kessler" => new HumanLook { Shirt = U.C(28, 28, 36), Pants = U.C(20, 20, 26), Skin = U.C(232, 190, 160), Hair = U.C(190, 190, 196), Shoes = U.C(14, 14, 16), LongSleeve = true, Glasses = true, Variant = 7 },
        "Sal" => new HumanLook { Shirt = U.C(60, 150, 98), Pants = U.C(30, 30, 36), Skin = U.C(205, 155, 115), Hair = U.C(18, 16, 16), Shoes = U.C(20, 20, 22), LongSleeve = true, Glasses = true, Variant = 9 },
        "Vane" => new HumanLook { Shirt = U.C(236, 234, 224), Pants = U.C(226, 224, 214), Skin = U.C(226, 182, 152), Hair = U.C(232, 232, 236), Shoes = U.C(120, 90, 60), LongSleeve = true, Glasses = true, Variant = 13 },
        "Danny" => new HumanLook { Shirt = U.C(86, 140, 200), Pants = U.C(60, 66, 90), Skin = U.C(225, 180, 145), Hair = U.C(80, 52, 30), Shoes = U.C(30, 30, 34), Variant = 11 },
        _ => new HumanLook { Shirt = U.C(120, 120, 130), Pants = U.C(40, 40, 50), Skin = U.C(220, 180, 150), Hair = U.C(40, 30, 24), Shoes = U.C(20, 20, 22), Variant = 5 },
    };

    static Actor Spawn(string name, Vector3 pos, bool inside)
    {
        Cast.RemoveAll(a => a.Name == name);
        var act = new Actor { Name = name, Pos = new Vector3(pos.X, 0f, pos.Z), Look = LookOf(name), Inside = inside };
        Cast.Add(act);
        return act;
    }

    static bool Here(Actor a) => a.Inside == Interior.Active;
    static Vector3 Subject() => P == null ? Vector3.Zero : P.Car != null ? P.Car.Pos : P.Pos;

    /// <summary>Puts a person into the casino hall (Sal the fixer).</summary>
    public static void PlaceInside(string name, Vector3 pos, float yaw)
    {
        var a = Spawn(name, pos, true);
        a.Yaw = yaw;
    }

    public static Vector3 PlayerPos => Subject();

    public static void ClearInterior() => Cast.RemoveAll(a => a.Inside);

    static void FaceSubject()
    {
        if (P == null) return;
        Vector3 sp = Subject();
        Actor near = null;
        float bd = float.MaxValue;
        foreach (var a in Cast)
        {
            if (!Here(a)) continue;
            a.Yaw = MathF.Atan2(sp.X - a.Pos.X, sp.Z - a.Pos.Z);
            float d = U.Dist2D(a.Pos, sp);
            if (d < bd) { bd = d; near = a; }
        }
        if (near != null && P.Car == null) P.Yaw = MathF.Atan2(near.Pos.X - P.Pos.X, near.Pos.Z - P.Pos.Z);
    }

    /// <summary>Casino hall: the people of the scene wait a few steps in front of the door.</summary>
    static void StageCasino(string[] names)
    {
        ClearInterior();
        if (names == null) return;
        Vector3 c = Interior.C;
        for (int i = 0; i < names.Length; i++)
            Spawn(names[i], new Vector3(c.X + (i - (names.Length - 1) / 2f) * 2.3f, 0f, c.Z - 8f), true);
        lastActor = null;
    }

    /// <summary>Keeps the player from walking through the people of the scene.</summary>
    public static void PushOut(ref Vector3 p, float r)
    {
        foreach (var a in Cast)
        {
            if (!Here(a)) continue;
            float dx = p.X - a.Pos.X, dz = p.Z - a.Pos.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz), m = r + 0.45f;
            if (d >= m) continue;
            if (d < 0.001f) { p.X += m; continue; }
            p.X += dx / d * (m - d);
            p.Z += dz / d * (m - d);
        }
    }

    public static void DrawCast(Vector3 camPos)
    {
        foreach (var a in Cast)
            if (Here(a)) Human.Draw(a.Pos, a.Yaw, 0f, 0f, a.Look, -1, U.Dist2D(a.Pos, camPos), 0f);
    }

    /// <summary>Film-style camera for a conversation: over the shoulder of the listener, looking at whoever is speaking.</summary>
    public static bool Shot(out Vector3 pos, out Vector3 target)
    {
        pos = target = Vector3.Zero;
        if (talk == null || P == null || Cast.Count == 0) return false;
        Line l = talk[ti];
        if (l.Card) return false;

        Actor a = null;
        bool you = l.Who.StartsWith("You");
        if (!you && !l.Who.Contains("phone"))
        {
            a = Cast.Find(x => Here(x) && l.Who.StartsWith(x.Name));
            if (a != null) lastActor = a;
        }
        else if (you)
        {
            a = lastActor != null && Here(lastActor) ? lastActor : Cast.Find(Here);
        }
        if (a == null) return false;

        Vector3 sp = Subject();
        Vector3 from = you ? a.Pos : sp, to = you ? sp : a.Pos;
        Vector3 dir = to - from; dir.Y = 0f;
        float dl = dir.Length();
        dir = dl > 0.01f ? dir / dl : Vector3.UnitZ;
        Vector3 side = new(dir.Z, 0f, -dir.X);
        pos = from - dir * 2.4f + side * 0.9f + new Vector3(0f, 1.75f, 0f);
        target = to + new Vector3(0f, 1.5f, 0f) + side * 0.15f;
        return true;
    }

    static Line[] talk;
    static int ti;
    static float typed, guard;
    public static bool Talking => talk != null;

    static void Say(string s) => Missions.Say?.Invoke(s);

    public static void Reset()
    {
        if (target != null) target.Marked = false;
        target = null;
        Cast.Clear(); lastActor = null;
        Index = 0; begun = false; count = 0; talk = null; ti = 0; typed = 0f;
    }

    public static void Load(int step)
    {
        Reset();
        Index = Math.Clamp(step, 0, Steps.Length);
    }

    // ---------------------------------------------------------------- progress
    /// <summary>Called every frame while the city is running on foot / in a car (not inside a casino).</summary>
    public static void Update(float dt, Player pl, World w, List<Npc> npcs)
    {
        P = pl;
        if (Done || Talking) return;
        Step s = Steps[Index];

        if (!begun)
        {
            begun = true;
            if (s.Kind == SKind.KillTarget)
            {
                if (s.VinceLook) Cast.RemoveAll(a => a.Name == "Vince");     // the actor turns into the target
                SpawnTarget(s, pl, w, npcs);
            }
            if (s.Kind == SKind.GoTo) Spawn("Vince", s.At, false);
            Play(s.Before);
            return;
        }

        // people of the city scene: look at the player, go home when he is far away
        for (int i = Cast.Count - 1; i >= 0; i--)
        {
            var a = Cast[i];
            if (a.Inside) continue;
            if (U.Dist2D(a.Pos, pl.Pos) > 60f) { Cast.RemoveAt(i); continue; }
            a.Yaw = MathF.Atan2(pl.Pos.X - a.Pos.X, pl.Pos.Z - a.Pos.Z);
        }

        switch (s.Kind)
        {
            case SKind.Talk: Complete(); break;
            case SKind.GoTo: if (U.Dist2D(pl.Pos, s.At) < 4.5f) Complete(); break;
            case SKind.BuyWeapon: if (pl.Weapons.Count > 0) Complete(); break;
            case SKind.KillTarget: if (target == null || target.Dead) Complete(); break;
            case SKind.OwnHome:
                foreach (var pr in w.Properties) if (pr.Owned) { Complete(); break; }
                break;
        }
    }

    static void Complete()
    {
        Step s = Steps[Index];
        if (s.Reward > 0 && P != null) { P.Money += s.Reward; Say($"+${s.Reward}"); }
        if (target != null) { target.Marked = false; target = null; }
        Index++;
        begun = false;
        count = 0;
        Play(s.After);
    }

    /// <summary>Hooked into Missions.Event: selling a stolen car finishes the "sell a car" step.</summary>
    public static void Event(MKind k)
    {
        if (Done || Talking || !begun) return;
        Step st = Steps[Index];
        if (st.Kind == SKind.SellCar && k == MKind.Sell) Complete();
        else if (st.Kind == SKind.Count && k == st.Ev && ++count >= st.Need) Complete();
    }

    /// <summary>Called when the player walks into a casino.</summary>
    public static void VisitCasino(string name)
    {
        if (Done || Talking || !begun || P == null) return;
        Step s = Steps[Index];
        if (name != s.Casino) return;
        if (s.Kind == SKind.EnterCasino) { StageCasino(s.Stage); Complete(); }
        else if (s.Kind == SKind.PayDebt)
        {
            if (P.Money >= s.Cost) { StageCasino(s.Stage); P.Money -= s.Cost; Say($"Paid ${s.Cost}"); Complete(); }
            else Say($"Doorman: come back with ${s.Cost}. You only have ${P.Money}");
        }
    }

    /// <summary>Dutch: the pedestrian nearest to the Royal Flush door is moved to the sidewalk outside it, made tougher and marked.</summary>
    static void SpawnTarget(Step s, Player pl, World w, List<Npc> npcs)
    {
        Building rf = s.Casino.Length > 0 ? w.Casinos.Find(b => b.Name == s.Casino) : null;
        Vector3 spot = rf != null ? rf.Door + new Vector3(9f, 0f, 0f) : s.At != Vector3.Zero ? s.At : pl.Pos;
        Npc best = null;
        float bd = float.MaxValue;
        foreach (var n in npcs)
        {
            if (n.Dead || n.Marked) continue;
            float d = U.Dist2D(n.Pos, spot);
            if (d < bd) { bd = d; best = n; }
        }
        target = best;
        if (best == null) return;
        best.PlaceAt(spot);
        best.MakeBoss(s.TargetHp);
        if (s.VinceLook) best.Dress(U.C(205, 150, 40), U.C(40, 44, 60));
        best.Marked = true;
    }

    // ---------------------------------------------------------------- where to go (marker / minimap)
    public static Vector3? ObjectivePos(World w)
    {
        if (Done || !begun || P == null) return null;
        Step s = Steps[Index];
        switch (s.Kind)
        {
            case SKind.GoTo: return s.At;
            case SKind.SellCar: return World.YardPos;
            case SKind.KillTarget: return target != null && !target.Dead ? target.Pos : null;
            case SKind.EnterCasino:
            case SKind.PayDebt:
            {
                Building rf = w.Casinos.Find(b => b.Name == s.Casino);
                return rf?.Door;
            }
            case SKind.OwnHome:
            {
                Property bp = null;
                float pd = float.MaxValue;
                foreach (var pr in w.Properties)
                {
                    if (pr.Owned) continue;
                    float d = U.Dist2D(pr.Door, P.Pos);
                    if (d < pd) { pd = d; bp = pr; }
                }
                return bp?.Door;
            }
            case SKind.BuyWeapon:
            {
                Building best = null;
                float bd = float.MaxValue;
                foreach (var b in w.Shops)
                {
                    if (b.ShopKind != 0) continue;
                    float d = U.Dist2D(b.Door, P.Pos);
                    if (d < bd) { bd = d; best = b; }
                }
                return best?.Door;
            }
        }
        return null;
    }

    /// <summary>A beam over the objective (shops and the yard already have their own beacons).</summary>
    public static void Draw3D(World w, float time)
    {
        Vector3? op = ObjectivePos(w);
        if (op == null) return;
        Step s = Steps[Index];
        if (s.Kind == SKind.BuyWeapon || s.Kind == SKind.SellCar || s.Kind == SKind.OwnHome) return;
        Vector3 p = op.Value;
        bool kill = s.Kind == SKind.KillTarget;
        Color c = kill ? U.C(255, 50, 50) : U.C(255, 225, 60);
        float h = kill ? 30f : 46f;
        // the beam starts above head height so that the person standing at the marker stays visible
        float y0 = 2.6f;
        Gfx.Flat(new Vector3(p.X, (y0 + h) / 2f, p.Z), new Vector3(kill ? 0.3f : 0.45f, h - y0, kill ? 0.3f : 0.45f), 0f, c);
        if (kill)
        {
            // little floating marker over his head
            float bob = 0.12f * MathF.Sin(time * 4f);
            Gfx.Flat(new Vector3(p.X, 2.25f + bob, p.Z), new Vector3(0.45f, 0.45f, 0.45f), time * 2f, c);
        }
        else
        {
            // a ring on the ground (not a filled disc), so feet are not covered either
            float pu = 1f + 0.12f * MathF.Sin(time * 3f);
            float r = 3.8f * pu / 2f;
            Gfx.Flat(new Vector3(p.X, 0.27f, p.Z - r), new Vector3(2f * r, 0.08f, 0.14f), 0f, c);
            Gfx.Flat(new Vector3(p.X, 0.27f, p.Z + r), new Vector3(2f * r, 0.08f, 0.14f), 0f, c);
            Gfx.Flat(new Vector3(p.X - r, 0.27f, p.Z), new Vector3(0.14f, 0.08f, 2f * r), 0f, c);
            Gfx.Flat(new Vector3(p.X + r, 0.27f, p.Z), new Vector3(0.14f, 0.08f, 2f * r), 0f, c);
        }
    }

    // ---------------------------------------------------------------- talking
    public static void Play(Line[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        talk = lines; ti = 0; typed = 0f; guard = 0.3f;
        FaceSubject();
    }

    public static void UpdateTalk(float dt)
    {
        if (talk == null) return;
        Line l = talk[ti];
        typed += dt * (l.Card ? 30f : 60f);
        if (guard > 0f) { guard -= dt; return; }          // a key held down a moment ago must not skip the line

        bool adv = Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Space)
                   || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (!adv) return;
        if (typed < l.Text.Length) { typed = l.Text.Length; return; }
        ti++;
        typed = 0f;
        guard = 0.12f;
        if (ti >= talk.Length) talk = null;
    }

    static Color SpeakerColor(string who)
    {
        if (who.StartsWith("Sal")) return U.C(90, 220, 140);
        if (who.StartsWith("Vane")) return U.C(190, 120, 255);
        if (who.StartsWith("Vince")) return U.C(255, 205, 70);
        if (who.StartsWith("Kessler")) return U.C(235, 80, 90);
        if (who.StartsWith("Danny")) return U.C(90, 210, 255);
        if (who.StartsWith("You")) return U.C(255, 255, 255);
        return U.C(200, 200, 215);
    }

    static List<string> Wrap(string text, int size, int maxW)
    {
        var lines = new List<string>();
        string cur = "";
        foreach (string word in text.Split(' '))
        {
            string t = cur.Length == 0 ? word : cur + " " + word;
            if (Raylib.MeasureText(t, size) > maxW && cur.Length > 0) { lines.Add(cur); cur = word; }
            else cur = t;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Little blocky portrait of the speaker (also used for phone calls, where nobody is standing in the scene).</summary>
    static void DrawPortrait(string who, int x, int y, int s, bool talking, Color tint)
    {
        Color skin, hair, shirt;
        bool glasses = false, suit = false, grey = false;
        if (who.StartsWith("Sal")) { skin = U.C(205, 155, 115); hair = U.C(18, 16, 16); shirt = U.C(60, 150, 98); glasses = true; }
        else if (who.StartsWith("Vane")) { skin = U.C(226, 182, 152); hair = U.C(232, 232, 236); shirt = U.C(236, 234, 224); glasses = true; suit = true; grey = true; }
        else if (who.StartsWith("Vince")) { skin = U.C(214, 170, 130); hair = U.C(36, 26, 20); shirt = U.C(205, 150, 40); }
        else if (who.StartsWith("Kessler")) { skin = U.C(232, 190, 160); hair = U.C(190, 190, 196); shirt = U.C(28, 28, 36); glasses = true; suit = true; grey = true; }
        else if (who.StartsWith("Danny")) { skin = U.C(225, 180, 145); hair = U.C(80, 52, 30); shirt = U.C(86, 140, 200); }
        else if (who.StartsWith("You")) { skin = U.C(205, 165, 130); hair = U.C(60, 40, 26); shirt = U.C(235, 235, 240); }
        else { skin = U.C(220, 180, 150); hair = U.C(40, 30, 24); shirt = U.C(120, 120, 130); }

        float k = s / 100f;
        int X(float v) => x + (int)(v * k);
        int S(float v) => Math.Max(1, (int)(v * k));
        Raylib.DrawRectangle(x, y, s, s, U.C(22, 22, 36));
        Raylib.DrawRectangle(x, y, s, S(100), U.C(tint.R / 5, tint.G / 5, tint.B / 5, 255));
        Raylib.DrawRectangle(X(8), y + S(78), S(84), S(22), shirt);                          // shoulders
        if (suit) Raylib.DrawRectangle(X(46), y + S(76), S(8), S(22), U.C(190, 30, 40));              // red tie
        Raylib.DrawRectangle(X(40), y + S(64), S(20), S(16), skin);                                     // neck
        Raylib.DrawRectangle(X(26), y + S(22), S(48), S(48), skin);                                     // head
        Raylib.DrawRectangle(X(24), y + S(16), S(52), S(15), hair);                                     // hair
        Raylib.DrawRectangle(X(24), y + S(28), S(6), S(grey ? 12 : 8), hair);
        Raylib.DrawRectangle(X(70), y + S(28), S(6), S(grey ? 12 : 8), hair);
        Raylib.DrawRectangle(X(36), y + S(42), S(8), S(6), U.C(20, 20, 24));                            // eyes
        Raylib.DrawRectangle(X(56), y + S(42), S(8), S(6), U.C(20, 20, 24));
        if (glasses)
        {
            Raylib.DrawRectangleLines(X(33), y + S(39), S(14), S(11), U.C(230, 230, 235));
            Raylib.DrawRectangleLines(X(53), y + S(39), S(14), S(11), U.C(230, 230, 235));
        }
        int mh = talking && ((int)(Raylib.GetTime() * 9) & 1) == 0 ? S(8) : S(3);
        Raylib.DrawRectangle(X(41), y + S(56), S(18), mh, U.C(120, 40, 40));                            // mouth
        if (who.Contains("phone"))
        {
            Raylib.DrawRectangle(X(74), y + S(46), S(14), S(26), U.C(30, 30, 34));                      // phone at his ear
            Raylib.DrawRectangle(X(76), y + S(49), S(10), S(16), U.C(90, 200, 255));
        }
        Raylib.DrawRectangleLines(x, y, s, s, tint);
    }

    public static void DrawTalk()
    {
        if (talk == null) return;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Line l = talk[ti];
        int shown = Math.Min(l.Text.Length, (int)typed);
        bool complete = shown >= l.Text.Length;
        bool blink = ((int)(Raylib.GetTime() * 2.5) & 1) == 0;

        if (l.Card)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 238));
            int maxW = Math.Min(1000, sw - 120);
            var lines = Wrap(l.Text, 34, maxW);
            int total = lines.Count * 42;
            int y0 = sh / 2 - total / 2;
            int hw = Raylib.MeasureText(l.Who, 22);
            Raylib.DrawText(l.Who, sw / 2 - hw / 2, y0 - 50, 22, U.C(255, 205, 70));
            int left = shown;
            for (int i = 0; i < lines.Count && left > 0; i++)
            {
                string part = lines[i].Substring(0, Math.Min(lines[i].Length, left));
                int fw = Raylib.MeasureText(lines[i], 34);        // centred by the full line so the text does not jump while it types
                Raylib.DrawText(part, sw / 2 - fw / 2, y0 + i * 42, 34, U.C(240, 240, 250));
                left -= lines[i].Length + 1;
            }
            if (complete && blink)
            {
                string hint = "E / click - continue";
                Raylib.DrawText(hint, sw / 2 - Raylib.MeasureText(hint, 18) / 2, sh - 60, 18, U.C(150, 150, 170));
            }
            return;
        }

        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 90));
        int w = Math.Min(980, sw - 60), h = 176;
        int x = (sw - w) / 2, y = sh - h - 56;
        Raylib.DrawRectangle(x, y, w, h, U.C(12, 12, 24, 240));
        Color sc = SpeakerColor(l.Who);
        Raylib.DrawRectangleLines(x, y, w, h, sc);
        Raylib.DrawRectangle(x, y, 8, h, sc);
        int ps = 136, tx = x + 28 + ps + 14;
        DrawPortrait(l.Who, x + 24, y + (h - ps) / 2, ps, !complete, sc);
        Raylib.DrawText(l.Who, tx, y + 16, 28, sc);

        var tl = Wrap(l.Text, 26, w - 60 - ps - 14);
        int rem = shown;
        for (int i = 0; i < tl.Count && rem > 0; i++)
        {
            string part = tl[i].Substring(0, Math.Min(tl[i].Length, rem));
            Raylib.DrawText(part, tx, y + 60 + i * 32, 26, U.C(238, 238, 248));
            rem -= tl[i].Length + 1;
        }
        if (complete && blink)
        {
            string hint = ti + 1 < talk.Length ? "E / click >>" : "E / click";
            Raylib.DrawText(hint, x + w - Raylib.MeasureText(hint, 18) - 18, y + h - 28, 18, U.C(150, 150, 170));
        }
    }

    // ---------------------------------------------------------------- HUD
    public static void DrawHud(int sh, Player pl)
    {
        if (Done || Talking || !begun) return;
        Step s = Steps[Index];
        if (s.Objective.Length == 0) return;
        string head = "STORY - " + s.Chapter;
        string obj = s.Objective + (s.Kind == SKind.PayDebt ? $"   (you have ${pl.Money} / ${s.Cost})" : s.Kind == SKind.Count ? $"   ({count} / {s.Need})" : "");
        int x = 20, y = sh - 152;
        Raylib.DrawText(head, x + 1, y + 1, 16, U.C(0, 0, 0, 180));
        Raylib.DrawText(head, x, y, 16, U.C(255, 205, 70));
        Raylib.DrawText(obj, x + 1, y + 23, 22, U.C(0, 0, 0, 180));
        Raylib.DrawText(obj, x, y + 22, 22, U.C(255, 255, 255));
    }

    /// <summary>One line for the pause menu.</summary>
    public static string StatusText()
    {
        if (Done) return "STORY COMPLETE";
        Step s = Steps[Index];
        return s.Objective.Length > 0 ? $"STORY: {s.Chapter} - {s.Objective}" : $"STORY: {s.Chapter}";
    }
}
