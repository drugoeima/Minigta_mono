using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>Main menu: NEW GAME / CONTINUE / CONTROLS / QUIT. ESC in the game opens the pause menu, which has a button back to this one.</summary>
public static class MainMenu
{
    public static bool Active = true;
    public static bool InGame;                  // a game is running behind the menu: CONTINUE resumes it
    public static Action NewGame, Continue, Quit;
    public static string Msg = "";

    static int page, sel;
    static bool waiting;
    static float confirmT, guard, t;
    static string note = "";
    static Vector2 lastMouse;

    static readonly string[] MainItems = { "NEW GAME", "CONTINUE", "CONTROLS", "QUIT" };

    public static void Open(bool inGame)
    {
        Active = true;
        InGame = inGame;
        page = 0;
        sel = inGame || SaveFile.Exists ? 1 : 0;
        waiting = false;
        confirmT = 0f;
        guard = 0.3f;
        Msg = "";
        note = "";
        lastMouse = Raylib.GetMousePosition();
    }

    static bool CanContinue => InGame || SaveFile.Exists;

    // ---------------------------------------------------------------- layout
    static Rectangle MainBtn(int i)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        int bw = 440, bh = 60, gap = 16;
        int y0 = (int)(sh * 0.40f);
        return new Rectangle(sw / 2 - bw / 2, y0 + i * (bh + gap), bw, bh);
    }

    static int RowH() => Math.Clamp((Raylib.GetScreenHeight() - 270) / 14, 30, 44);

    static Rectangle CtlRow(int i)
    {
        int sw = Raylib.GetScreenWidth();
        int rh = RowH();
        int w = Math.Min(780, sw - 40);
        if (i < 12) return new Rectangle(sw / 2 - w / 2, 120 + i * rh, w, rh - 4);
        int by = 120 + 12 * rh + 14;
        int bw = (w - 20) / 2;
        return new Rectangle(sw / 2 - w / 2 + (i - 12) * (bw + 20), by, bw, 50);
    }

    // ---------------------------------------------------------------- input
    public static void Update(float dt)
    {
        t += dt;
        if (guard > 0f) guard -= dt;
        if (confirmT > 0f) confirmT -= dt;
        int s0 = sel, p0 = page;
        bool press = guard <= 0f && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || Raylib.IsMouseButtonPressed(MouseButton.Left));
        if (page == 0) UpdateMain(); else UpdateControls();
        if (page != p0) Sfx.UiBack(); else if (press) Sfx.UiSelect(); else if (sel != s0) Sfx.UiTick();
    }

    static bool Hover(int count, Func<int, Rectangle> rect, out bool click)
    {
        Vector2 m = Raylib.GetMousePosition();
        bool moved = (m - lastMouse).LengthSquared() > 0.5f;
        lastMouse = m;
        click = guard <= 0f && Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool over = false;
        for (int i = 0; i < count; i++)
        {
            if (!Raylib.CheckCollisionPointRec(m, rect(i))) continue;
            if (moved || click) sel = i;
            if (sel == i) over = true;
        }
        return over;
    }

    static void UpdateMain()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) sel = (sel + 1) % MainItems.Length;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) sel = (sel + MainItems.Length - 1) % MainItems.Length;
        bool over = Hover(MainItems.Length, MainBtn, out bool click);
        bool go = guard <= 0f && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || (click && over));
        if (guard <= 0f && Raylib.IsKeyPressed(KeyboardKey.Escape) && InGame) { Continue?.Invoke(); return; }
        if (!go) return;

        Msg = "";
        switch (sel)
        {
            case 0:
                if ((InGame || SaveFile.Exists) && confirmT <= 0f) { confirmT = 3f; return; }
                confirmT = 0f;
                NewGame?.Invoke();
                break;
            case 1:
                if (!CanContinue) { Msg = "There is no saved game yet - start a new one"; return; }
                Continue?.Invoke();
                break;
            case 2:
                page = 1; sel = 0; waiting = false; note = "";
                break;
            case 3:
                Quit?.Invoke();
                break;
        }
    }

    static void UpdateControls()
    {
        int n = 14;
        if (waiting)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { waiting = false; note = ""; return; }
            int ki = Convert.ToInt32(Raylib.GetKeyPressed());
            if (ki == 0) return;
            var k = (KeyboardKey)ki;
            if (Input.Reserved(k)) { note = $"{Input.Name(k)} is reserved - choose another key"; return; }
            Input.Set((Act)sel, k);
            Input.Save();
            waiting = false;
            note = "";
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) sel = (sel + 1) % n;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) sel = (sel + n - 1) % n;
        bool over = Hover(n, CtlRow, out bool click);
        if (guard <= 0f && Raylib.IsKeyPressed(KeyboardKey.Escape)) { page = 0; sel = 2; return; }
        bool go = guard <= 0f && (Raylib.IsKeyPressed(KeyboardKey.Enter) || (click && over));
        if (!go) return;

        if (sel < 12)
        {
            waiting = true;
            note = "Press the new key (Esc - cancel)";
            while (Convert.ToInt32(Raylib.GetKeyPressed()) != 0) { }      // forget the key that started this
        }
        else if (sel == 12) { Input.ResetAll(); Input.Save(); note = "Default controls restored"; }
        else { page = 0; sel = 2; }
    }

    // ---------------------------------------------------------------- drawing
    public static void Draw()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(6, 4, 18, 150));
        Raylib.DrawRectangle(0, 0, sw, 90, U.C(0, 0, 0, 90));
        Raylib.DrawRectangle(0, sh - 70, sw, 70, U.C(0, 0, 0, 90));

        if (page == 0) DrawMain(sw, sh); else DrawControls(sw, sh);
    }

    static void Title(int sw, int y, int size)
    {
        string a = "MINI GTA";
        int w = Raylib.MeasureText(a, size);
        float pulse = 0.5f + 0.5f * MathF.Sin(t * 2.2f);
        Raylib.DrawText(a, sw / 2 - w / 2 + 4, y + 4, size, U.C(0, 0, 0, 220));
        Raylib.DrawText(a, sw / 2 - w / 2, y, size, U.C(255, (int)(200 + 40 * pulse), 50));
        string b = "L A S   V E G A S";
        int s2 = size / 3;
        int w2 = Raylib.MeasureText(b, s2);
        Raylib.DrawText(b, sw / 2 - w2 / 2, y + size + 6, s2, U.C(255, 90, 200));
    }

    static void DrawMain(int sw, int sh)
    {
        Title(sw, (int)(sh * 0.10f), Math.Clamp(sh / 8, 56, 110));
        for (int i = 0; i < MainItems.Length; i++)
        {
            Rectangle r = MainBtn(i);
            bool hot = sel == i;
            bool disabled = i == 1 && !CanContinue;
            string label = MainItems[i];
            if (i == 0 && confirmT > 0f) label = "CLICK AGAIN - ERASE PROGRESS";
            else if (i == 1 && InGame) label = "RESUME";
            Color bg = i == 0 && confirmT > 0f ? U.C(190, 40, 40) : hot ? U.C(60, 66, 120) : U.C(22, 24, 44);
            Raylib.DrawRectangleRec(r, U.C(bg.R, bg.G, bg.B, 235));
            Raylib.DrawRectangleLinesEx(r, hot ? 3f : 1.5f, hot ? U.C(255, 210, 60) : U.C(110, 114, 150));
            Color tc = disabled ? U.C(100, 100, 115) : hot ? U.C(255, 255, 255) : U.C(200, 200, 220);
            int tw = Raylib.MeasureText(label, 28);
            Raylib.DrawText(label, (int)(r.X + r.Width / 2) - tw / 2, (int)(r.Y + r.Height / 2) - 14, 28, tc);
        }
        if (Msg.Length > 0)
        {
            int mw = Raylib.MeasureText(Msg, 22);
            Raylib.DrawText(Msg, sw / 2 - mw / 2, (int)MainBtn(MainItems.Length).Y + 4, 22, U.C(255, 150, 80));
        }
        string hint = "Up/Down + Enter, or the mouse" + (InGame ? "    ESC - back to the game" : "");
        Raylib.DrawText(hint, sw / 2 - Raylib.MeasureText(hint, 18) / 2, sh - 44, 18, U.C(170, 170, 190));
    }

    static void DrawControls(int sw, int sh)
    {
        string head = "CONTROLS";
        Raylib.DrawText(head, sw / 2 - Raylib.MeasureText(head, 44) / 2, 30, 44, U.C(255, 210, 60));
        string sub = "Click a row (or Enter) and press the new key. If the key is already used, the two actions swap.";
        Raylib.DrawText(sub, sw / 2 - Raylib.MeasureText(sub, 18) / 2, 84, 18, U.C(170, 170, 190));

        for (int i = 0; i < 12; i++)
        {
            Rectangle r = CtlRow(i);
            bool hot = sel == i;
            Raylib.DrawRectangleRec(r, hot ? U.C(46, 50, 96, 240) : U.C(20, 22, 40, 225));
            if (hot) Raylib.DrawRectangleLinesEx(r, 2f, U.C(255, 210, 60));
            int fs = Math.Clamp((int)r.Height - 12, 16, 24);
            Raylib.DrawText(Input.Labels[i], (int)r.X + 14, (int)(r.Y + r.Height / 2) - fs / 2, fs, U.C(225, 225, 240));

            bool wait = waiting && hot;
            string kn = wait ? "press a key..." : Input.Name((Act)i);
            var kr = new Rectangle(r.X + r.Width - 230, r.Y + 3, 216, r.Height - 6);
            Raylib.DrawRectangleRec(kr, wait ? U.C(190, 120, 20) : U.C(12, 12, 24));
            Raylib.DrawRectangleLinesEx(kr, 1.5f, wait ? U.C(255, 230, 120) : U.C(120, 124, 160));
            Raylib.DrawText(kn, (int)(kr.X + kr.Width / 2) - Raylib.MeasureText(kn, fs) / 2, (int)(kr.Y + kr.Height / 2) - fs / 2, fs, U.C(255, 255, 255));
        }
        for (int i = 12; i < 14; i++)
        {
            Rectangle r = CtlRow(i);
            bool hot = sel == i;
            Raylib.DrawRectangleRec(r, hot ? U.C(60, 66, 120, 240) : U.C(22, 24, 44, 235));
            Raylib.DrawRectangleLinesEx(r, hot ? 3f : 1.5f, hot ? U.C(255, 210, 60) : U.C(110, 114, 150));
            string l = i == 12 ? "RESET TO DEFAULTS" : "BACK";
            Raylib.DrawText(l, (int)(r.X + r.Width / 2) - Raylib.MeasureText(l, 24) / 2, (int)(r.Y + r.Height / 2) - 12, 24, U.C(255, 255, 255));
        }
        if (note.Length > 0)
        {
            int ny = (int)CtlRow(12).Y + 62;
            Raylib.DrawText(note, sw / 2 - Raylib.MeasureText(note, 22) / 2, ny, 22, U.C(255, 200, 90));
        }
        string fixedKeys = "Fixed: 1-7 weapon slots, mouse wheel, LMB fire, RMB zoom, ESC menu, F10 quit, F11 fullscreen";
        Raylib.DrawText(fixedKeys, sw / 2 - Raylib.MeasureText(fixedKeys, 16) / 2, sh - 34, 16, U.C(150, 150, 172));
    }
}
