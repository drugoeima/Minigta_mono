using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public enum MKind { Kill, RunOver, Steal, Headshot, Sell, Drive, ShotgunKill, Casinos, Park }

public class Mission
{
    public MKind Kind;
    public string Name = "", Desc = "";
    public int Target, Reward;
    public float Progress;
    public int State;                                   // 0 = available, 1 = active, 2 = done (waiting for the reward)
    public readonly HashSet<string> Seen = new();       // for "visit N different casinos"

    public string ProgressText => Kind == MKind.Drive
        ? $"{Progress / 1000f:0.0} / {Target / 1000} km"
        : $"{(int)Progress} / {Target}";
}

/// <summary>Repeatable missions. Take them in the ESC menu, finish them, claim the reward, take them again.</summary>
public static class Missions
{
    public static Action<string> Say;
    public static Action OnSave, OnNewGame, OnMainMenu;          // buttons in the pause menu
    static float confirmT;                           // > 0: NEW GAME waits for a second click

    public static void ResetAll()
    {
        foreach (var m in All) { m.State = 0; m.Progress = 0f; m.Seen.Clear(); }
    }

    public static readonly List<Mission> All = new()
    {
        new Mission { Kind = MKind.Kill,        Name = "Body Count",       Desc = "Kill 20 NPCs",                            Target = 20,   Reward = 1500 },
        new Mission { Kind = MKind.RunOver,     Name = "Road Rage",        Desc = "Run over 10 NPCs",                        Target = 10,   Reward = 1500 },
        new Mission { Kind = MKind.Steal,       Name = "Grand Theft",      Desc = "Steal 5 cars",                            Target = 5,    Reward = 2000 },
        new Mission { Kind = MKind.Headshot,    Name = "Headhunter",       Desc = "Kill 8 NPCs with headshots",              Target = 8,    Reward = 2500 },
        new Mission { Kind = MKind.Sell,        Name = "Chop Shop",        Desc = "Sell 3 stolen cars at the Scrap Yard",    Target = 3,    Reward = 3000 },
        new Mission { Kind = MKind.Drive,       Name = "Long Haul",        Desc = "Drive 5 km",                              Target = 5000, Reward = 1200 },
        new Mission { Kind = MKind.ShotgunKill, Name = "Boomstick",        Desc = "Kill 10 NPCs with a shotgun",           Target = 10,   Reward = 2000 },
        new Mission { Kind = MKind.Casinos,     Name = "High Roller Tour", Desc = "Visit 5 different casinos",               Target = 5,    Reward = 1000 },
        new Mission { Kind = MKind.Park,        Name = "Valet",            Desc = "Park 3 new cars in your garage",          Target = 3,    Reward = 2500 },
    };

    static int sel;

    // ------------------------------------------------------------------ progress
    static void Check(Mission m)
    {
        if (m.Progress < m.Target) return;
        m.Progress = m.Target;
        m.State = 2;
        Say?.Invoke($"Mission complete: {m.Name}! Claim it in the ESC menu");
    }

    public static void Event(MKind k, float amount = 1f)
    {
        Story.Event(k);
        foreach (var m in All)
        {
            if (m.State != 1 || m.Kind != k) continue;
            m.Progress += amount;
            Check(m);
        }
    }

    public static void VisitCasino(string name)
    {
        foreach (var m in All)
        {
            if (m.State != 1 || m.Kind != MKind.Casinos) continue;
            if (m.Seen.Add(name)) { m.Progress = m.Seen.Count; Check(m); }
        }
    }

    static void Take(Mission m)
    {
        m.State = 1;
        m.Progress = 0f;
        m.Seen.Clear();
        Say?.Invoke($"Mission taken: {m.Desc}");
    }

    static void Claim(Mission m, Player pl)
    {
        pl.Money += m.Reward;
        m.State = 0;           // can be taken again right away
        m.Progress = 0f;
        m.Seen.Clear();
        Say?.Invoke($"Mission reward: +${m.Reward}");
    }

    // ------------------------------------------------------------------ in-game tracker (top left)
    public static void DrawTracker(int x, int y)
    {
        bool any = false;
        foreach (var m in All)
        {
            if (m.State == 0) continue;
            if (!any) { Raylib.DrawText("MISSIONS (ESC)", x, y, 16, U.C(170, 170, 190)); y += 20; any = true; }

            string s = m.State == 2 ? $"{m.Name}: DONE - claim in ESC menu" : $"{m.Name}: {m.ProgressText}";
            Color c = m.State == 2 ? U.C(90, 230, 120) : U.C(255, 220, 90);
            Raylib.DrawText(s, x + 1, y + 1, 20, U.C(0, 0, 0, 180));
            Raylib.DrawText(s, x, y, 20, c);
            y += 24;
        }
    }

    // ------------------------------------------------------------------ ESC menu (input + drawing in one pass)
    public static void DrawMenu(Player pl)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 175));

        int n = All.Count;
        int panelW = Math.Min(980, sw - 60);
        int rowH = Math.Clamp((sh - 300) / n, 46, 66);
        int panelH = 100 + rowH * n + 116;
        int px = (sw - panelW) / 2, py = Math.Max(10, (sh - panelH) / 2);

        Raylib.DrawRectangle(px, py, panelW, panelH, U.C(16, 16, 30, 245));
        Raylib.DrawRectangleLines(px, py, panelW, panelH, U.C(200, 200, 220));
        Raylib.DrawText("PAUSED - MISSIONS", px + 24, py + 18, 36, U.C(255, 255, 255));
        string st = Story.StatusText();
        int stw = Raylib.MeasureText(st, 18);
        Raylib.DrawText(st, px + panelW - stw - 24, py + 28, 18, Story.Done ? U.C(90, 230, 120) : U.C(255, 205, 70));
        Raylib.DrawText("Take a mission, finish it, claim the reward. Missions can be taken again and again.", px + 24, py + 62, 18, U.C(170, 170, 190));

        if (Raylib.IsKeyPressed(KeyboardKey.Down)) sel = (sel + 1) % n;
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) sel = (sel + n - 1) % n;
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter);
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        Vector2 mouse = Raylib.GetMousePosition();

        for (int i = 0; i < n; i++)
        {
            Mission m = All[i];
            int y = py + 96 + i * rowH;
            var row = new Rectangle(px + 16, y, panelW - 32, rowH - 6);
            var btn = new Rectangle(px + panelW - 16 - 150 - 10, y + 6, 150, rowH - 18);

            if (Raylib.CheckCollisionPointRec(mouse, row) && Raylib.GetMouseDelta().LengthSquared() > 0f) sel = i;
            bool hot = sel == i;

            Raylib.DrawRectangleRec(row, hot ? U.C(46, 50, 86) : U.C(28, 30, 48));
            if (hot) Raylib.DrawRectangleLinesEx(row, 2f, U.C(120, 130, 220));

            Raylib.DrawText(m.Name, (int)row.X + 14, y + 6, 24, U.C(255, 255, 255));
            Raylib.DrawText(m.Desc, (int)row.X + 14, y + 6 + 27, 16, U.C(170, 170, 190));

            // progress bar
            int bx = px + (int)(panelW * 0.43f), bw = (int)(panelW * 0.19f), by = y + rowH / 2 - 11;
            Raylib.DrawRectangle(bx, by, bw, 14, U.C(12, 12, 20));
            float frac = m.State == 0 ? 0f : m.Progress / m.Target;
            Raylib.DrawRectangle(bx, by, (int)(bw * Math.Clamp(frac, 0f, 1f)), 14, m.State == 2 ? U.C(90, 230, 120) : U.C(255, 200, 60));
            Raylib.DrawRectangleLines(bx, by, bw, 14, U.C(120, 120, 140));
            if (m.State != 0) Raylib.DrawText(m.ProgressText, bx, by + 17, 14, U.C(200, 200, 215));

            Raylib.DrawText($"${m.Reward}", px + (int)(panelW * 0.67f), y + rowH / 2 - 14, 24, U.C(90, 230, 120));

            string label; Color bg;
            if (m.State == 0) { label = "TAKE"; bg = U.C(50, 90, 200); }
            else if (m.State == 1) { label = "ACTIVE"; bg = U.C(60, 60, 72); }
            else { label = "CLAIM"; bg = U.C(40, 160, 70); }

            bool over = Raylib.CheckCollisionPointRec(mouse, btn);
            Raylib.DrawRectangleRec(btn, over && m.State != 1 ? U.C(Math.Min(bg.R + 30, 255), Math.Min(bg.G + 30, 255), Math.Min(bg.B + 30, 255)) : bg);
            int tw = Raylib.MeasureText(label, 22);
            Raylib.DrawText(label, (int)(btn.X + btn.Width / 2) - tw / 2, (int)(btn.Y + btn.Height / 2) - 11, 22, m.State == 1 ? U.C(255, 220, 90) : U.C(255, 255, 255));

            if ((click && over) || (enter && hot))
            {
                if (m.State == 0) Take(m);
                else if (m.State == 2) Claim(m, pl);
            }
        }

        // ---- save / new game buttons
        if (confirmT > 0f) confirmT -= Raylib.GetFrameTime();
        int btnY = py + 100 + rowH * n + 8;
        var bSave = new Rectangle(px + 16, btnY, 260, 42);
        var bNew = new Rectangle(px + 16 + 276, btnY, 300, 42);
        var bMenu = new Rectangle(px + 16 + 276 + 316, btnY, 250, 42);
        bool oMenu = Raylib.CheckCollisionPointRec(mouse, bMenu);
        Raylib.DrawRectangleRec(bMenu, oMenu ? U.C(70, 80, 160) : U.C(48, 56, 120));
        string tMenu = "MAIN MENU";
        Raylib.DrawText(tMenu, (int)(bMenu.X + bMenu.Width / 2) - Raylib.MeasureText(tMenu, 22) / 2, (int)bMenu.Y + 10, 22, U.C(255, 255, 255));
        if (click && oMenu) { OnMainMenu?.Invoke(); return; }
        bool oSave = Raylib.CheckCollisionPointRec(mouse, bSave), oNew = Raylib.CheckCollisionPointRec(mouse, bNew);
        Raylib.DrawRectangleRec(bSave, oSave ? U.C(60, 150, 90) : U.C(40, 120, 70));
        string tSave = "SAVE GAME  (F5)";
        Raylib.DrawText(tSave, (int)(bSave.X + bSave.Width / 2) - Raylib.MeasureText(tSave, 22) / 2, (int)bSave.Y + 10, 22, U.C(255, 255, 255));
        bool armed = confirmT > 0f;
        Raylib.DrawRectangleRec(bNew, armed ? U.C(210, 50, 50) : oNew ? U.C(120, 60, 60) : U.C(90, 44, 44));
        string tNew = armed ? "CLICK AGAIN - ERASE PROGRESS" : "NEW GAME";
        Raylib.DrawText(tNew, (int)(bNew.X + bNew.Width / 2) - Raylib.MeasureText(tNew, 22) / 2, (int)bNew.Y + 10, 22, U.C(255, 255, 255));
        Raylib.DrawText("Progress is saved automatically (every 30 s, when you pause and when you quit). New game = $150, no weapons.",
            px + 24, btnY + 52, 16, U.C(150, 150, 172));
        if (click && oSave) OnSave?.Invoke();
        if (click && oNew)
        {
            if (armed) { confirmT = 0f; OnNewGame?.Invoke(); }
            else confirmT = 3f;
        }

        Raylib.DrawText("ESC - resume    Up/Down + Enter or mouse - choose    F10 - quit game", px + 24, py + panelH - 36, 18, U.C(170, 170, 190));
    }
}
