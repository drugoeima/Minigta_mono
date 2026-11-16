using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>
/// Gun shop overlay (Ammu-Nation / Black Market). Opened with E at the shop door; the city is frozen while it is open.
/// Up/Down or the mouse wheel choose a weapon, Enter or a click buys it (or an ammo pack if you already own it).
/// </summary>
public static class Shop
{
    public static bool Active;

    static int kind;                 // 0 = Ammu-Nation, 1 = Black Market
    static string title = "";
    static Color accent;
    static int sel, scroll;
    static string msg = "";
    static float msgT;
    static readonly List<Weapon> stock = new();

    public static void Open(Building b)
    {
        Active = true;
        kind = Math.Max(0, b.ShopKind);
        title = b.Name;
        accent = b.Neon;
        stock.Clear();
        foreach (var w in Arsenal.All) if (w.Sold(kind)) stock.Add(w);
        if (kind == 0) stock.Add(Arsenal.ArmorItem);          // body armour: AMMU-NATION only, last row
        sel = 0; scroll = 0; msg = ""; msgT = 0f;
    }

    // ------------------------------------------------------------------ layout (shared by Update and Draw)
    struct Lay { public int Px, Py, W, H, ListX, ListY, ListW, RowH, Rows, DetX, DetW; }

    static Lay Layout()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var l = new Lay();
        l.W = Math.Min(1120, sw - 40);
        l.H = Math.Min(sh - 30, 700);
        l.Px = (sw - l.W) / 2;
        l.Py = (sh - l.H) / 2;
        l.RowH = 44;
        l.ListX = l.Px + 16;
        l.ListY = l.Py + 96;
        l.DetW = Math.Min(380, l.W / 3);
        l.ListW = l.W - 32 - l.DetW - 16;
        l.DetX = l.ListX + l.ListW + 16;
        l.Rows = Math.Max(3, (l.H - 96 - 56) / l.RowH);
        return l;
    }

    static void Say(string s) { msg = s; msgT = 3f; }

    // ------------------------------------------------------------------ input
    public static void Update(float dt, Player pl)
    {
        if (msgT > 0f) msgT -= dt;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            Active = false;
            return;
        }

        var L = Layout();
        int n = stock.Count;
        int sel0 = sel;

        if (Raylib.IsKeyPressed(KeyboardKey.Down)) sel = Math.Min(sel + 1, n - 1);
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) sel = Math.Max(sel - 1, 0);
        if (Raylib.IsKeyPressed(KeyboardKey.PageDown)) sel = Math.Min(sel + L.Rows, n - 1);
        if (Raylib.IsKeyPressed(KeyboardKey.PageUp)) sel = Math.Max(sel - L.Rows, 0);

        Vector2 mouse = Raylib.GetMousePosition();
        var listRect = new Rectangle(L.ListX, L.ListY, L.ListW, L.Rows * L.RowH);
        bool inList = Raylib.CheckCollisionPointRec(mouse, listRect);

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f && inList) scroll = Math.Clamp(scroll - (int)MathF.Sign(wheel) * 2, 0, Math.Max(0, n - L.Rows));

        // keep the selection inside the visible window
        if (sel < scroll) scroll = sel;
        if (sel >= scroll + L.Rows) scroll = sel - L.Rows + 1;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, n - L.Rows));

        bool buy = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Space);
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

        if (inList && Raylib.GetMouseDelta().LengthSquared() > 0f)
        {
            int idx = scroll + (int)((mouse.Y - L.ListY) / L.RowH);
            if (idx >= 0 && idx < n) sel = idx;
        }
        if (click && inList)
        {
            int idx = scroll + (int)((mouse.Y - L.ListY) / L.RowH);
            if (idx >= 0 && idx < n) { sel = idx; buy = true; }
        }

        if (sel != sel0) Sfx.UiTick();
        if (buy && sel >= 0 && sel < n)
        {
            int m0 = pl.Money;
            Buy(stock[sel], pl);
            if (pl.Money < m0) Sfx.Cash(0.7f); else Sfx.Error();
        }
    }

    static void Buy(Weapon def, Player pl)
    {
        if (Arsenal.IsArmor(def))
        {
            if (pl.Armor >= Player.MaxArmor - 0.5f) { Say("Your armor is already full"); return; }
            int cost = pl.ArmorCost;
            if (pl.Money < cost) { Say($"Not enough money - armor costs ${cost}"); return; }
            pl.Money -= cost;
            pl.Armor = Player.MaxArmor;
            Say("Body armor on - 100 points");
            return;
        }
        Weapon own = pl.Get(def.Id);
        if (own == null)
        {
            if (pl.Money < def.Price) { Say($"Not enough money - {def.Name} costs ${def.Price}"); return; }
            pl.Money -= def.Price;
            pl.Give(def);
            Say(def.Melee ? $"Bought: {def.Name}" : $"Bought: {def.Name} (+{def.StartReserve} spare rounds)");
            return;
        }
        if (def.Melee) { Say($"You already own the {def.Name}"); return; }
        if (own.Reserve >= def.MaxReserve) { Say($"{def.Name}: ammo is full"); return; }
        if (pl.Money < def.PackPrice) { Say($"Not enough money - ammo costs ${def.PackPrice}"); return; }
        pl.Money -= def.PackPrice;
        int add = Math.Min(def.PackSize, def.MaxReserve - own.Reserve);
        own.Reserve += add;
        Say($"+{add} rounds for the {def.Name}");
    }

    // ------------------------------------------------------------------ drawing
    static void Bar(int x, int y, int w, string label, float frac, Color c)
    {
        Raylib.DrawText(label, x, y, 16, U.C(170, 170, 190));
        int bx = x + 92, bw = w - 92;
        Raylib.DrawRectangle(bx, y + 3, bw, 11, U.C(12, 12, 20));
        Raylib.DrawRectangle(bx, y + 3, (int)(bw * Math.Clamp(frac, 0f, 1f)), 11, c);
        Raylib.DrawRectangleLines(bx, y + 3, bw, 11, U.C(90, 90, 110));
    }

    public static void Draw(Player pl)
    {
        var L = Layout();
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        int n = stock.Count;

        Raylib.DrawRectangle(0, 0, sw, sh, U.C(0, 0, 0, 175));
        Raylib.DrawRectangle(L.Px, L.Py, L.W, L.H, U.C(16, 16, 28, 248));
        Raylib.DrawRectangleLines(L.Px, L.Py, L.W, L.H, accent);
        Raylib.DrawRectangle(L.Px, L.Py, L.W, 6, accent);

        Raylib.DrawText(title, L.Px + 24, L.Py + 20, 40, accent);
        Raylib.DrawText(kind == 0 ? "Licensed dealer - weapons, ammo and body armor" : "No questions asked - the heavy stuff is here",
            L.Px + 24, L.Py + 66, 18, U.C(170, 170, 190));
        string money = $"$ {pl.Money}";
        int mw = Raylib.MeasureText(money, 36);
        Raylib.DrawText(money, L.Px + L.W - mw - 24, L.Py + 24, 36, U.C(90, 230, 120));

        Vector2 mouse = Raylib.GetMousePosition();

        // ---- list
        for (int r = 0; r < L.Rows; r++)
        {
            int i = scroll + r;
            if (i >= n) break;
            Weapon d = stock[i];
            Weapon own = pl.Get(d.Id);
            int y = L.ListY + r * L.RowH;
            var row = new Rectangle(L.ListX, y, L.ListW, L.RowH - 4);
            bool hot = i == sel;

            Raylib.DrawRectangleRec(row, hot ? U.C(46, 50, 86) : U.C(28, 30, 48));
            if (hot) Raylib.DrawRectangleLinesEx(row, 2f, accent);

            bool armorRow = Arsenal.IsArmor(d);
            Raylib.DrawText(d.Name, (int)row.X + 12, y + 4, 22, armorRow ? U.C(150, 200, 255) : own != null ? U.C(150, 235, 170) : U.C(255, 255, 255));
            Raylib.DrawText(armorRow ? "Equipment" : Arsenal.SlotNames[(int)d.Slot], (int)row.X + 12, y + 26, 13, U.C(150, 150, 172));

            string right; Color rc;
            if (armorRow)
            {
                bool full = pl.Armor >= Player.MaxArmor - 0.5f;
                right = full ? "ARMOR FULL" : $"${pl.ArmorCost}";
                rc = full ? U.C(150, 150, 172) : pl.Money >= pl.ArmorCost ? U.C(90, 230, 120) : U.C(235, 90, 90);
            }
            else if (own == null)
            {
                right = $"${d.Price}";
                rc = pl.Money >= d.Price ? U.C(90, 230, 120) : U.C(235, 90, 90);
            }
            else if (d.Melee)
            {
                right = "OWNED"; rc = U.C(150, 235, 170);
            }
            else
            {
                right = own.Reserve >= d.MaxReserve ? "AMMO FULL" : $"+{d.PackSize} ammo ${d.PackPrice}";
                rc = own.Reserve >= d.MaxReserve ? U.C(150, 150, 172) : pl.Money >= d.PackPrice ? U.C(255, 220, 90) : U.C(235, 90, 90);
            }
            int rw = Raylib.MeasureText(right, 20);
            Raylib.DrawText(right, (int)(row.X + row.Width) - rw - 12, y + 6, 20, rc);
            if (armorRow)
            {
                string have = $"{(int)pl.Armor}/{(int)Player.MaxArmor}";
                int hw = Raylib.MeasureText(have, 14);
                Raylib.DrawText(have, (int)(row.X + row.Width) - hw - 12, y + 27, 14, U.C(190, 190, 210));
            }
            else if (own != null && !d.Melee)
            {
                string have = $"{own.Ammo}/{own.Reserve}";
                int hw = Raylib.MeasureText(have, 14);
                Raylib.DrawText(have, (int)(row.X + row.Width) - hw - 12, y + 27, 14, U.C(190, 190, 210));
            }
        }

        // scroll bar
        if (n > L.Rows)
        {
            int trackH = L.Rows * L.RowH;
            int barH = Math.Max(24, trackH * L.Rows / n);
            int barY = L.ListY + (trackH - barH) * scroll / Math.Max(1, n - L.Rows);
            Raylib.DrawRectangle(L.ListX + L.ListW + 4, L.ListY, 6, trackH, U.C(12, 12, 20));
            Raylib.DrawRectangle(L.ListX + L.ListW + 4, barY, 6, barH, U.C(120, 124, 170));
        }

        // ---- details of the selected weapon
        Weapon s = stock[Math.Clamp(sel, 0, n - 1)];
        Weapon mine = pl.Get(s.Id);
        int dx = L.DetX, dy = L.ListY;
        Raylib.DrawRectangle(dx, dy, L.DetW, L.Rows * L.RowH - 4, U.C(24, 26, 42));
        Raylib.DrawRectangleLines(dx, dy, L.DetW, L.Rows * L.RowH - 4, U.C(70, 74, 110));
        bool sArmor = Arsenal.IsArmor(s);
        Raylib.DrawText(s.Name, dx + 14, dy + 12, 26, U.C(255, 255, 255));
        Raylib.DrawText(sArmor ? "Equipment" : Arsenal.SlotNames[(int)s.Slot], dx + 14, dy + 42, 16, accent);
        WrapText(s.Desc, dx + 14, dy + 68, L.DetW - 28, 16, U.C(200, 200, 215));

        int by = dy + 136, bw = L.DetW - 28;
        if (sArmor)
        {
            Bar(dx + 14, by, bw, "Your armor", pl.Armor / Player.MaxArmor, U.C(110, 170, 255)); by += 26;
            Bar(dx + 14, by, bw, "Your health", pl.Hp / Player.MaxHp, U.C(90, 220, 110)); by += 34;
            Raylib.DrawText("Absorbs damage before your health", dx + 14, by, 16, U.C(200, 200, 215)); by += 22;
            Raylib.DrawText($"Full vest ${Arsenal.ArmorPrice}, a damaged one pro rata", dx + 14, by, 14, U.C(200, 200, 215)); by += 22;
            Raylib.DrawText("Lost when you die", dx + 14, by, 16, U.C(255, 150, 70));
        }
        else {
        float perShot = s.Damage * s.Pellets + (s.Blast > 0f ? s.Blast * 8f : 0f);
        Bar(dx + 14, by, bw, "Damage", perShot / 320f, U.C(235, 90, 80)); by += 26;
        Bar(dx + 14, by, bw, "Fire rate", s.Rate / 30f, U.C(255, 200, 60)); by += 26;
        Bar(dx + 14, by, bw, "Range", (s.Melee ? s.Range * 10f : s.Range) / 450f, U.C(90, 180, 255)); by += 26;
        if (!s.Melee)
        {
            Bar(dx + 14, by, bw, "Accuracy", 1f - Math.Clamp(s.Spread * 12f, 0f, 1f), U.C(120, 230, 150)); by += 26;
            Raylib.DrawText($"Magazine {s.Mag}    Reload {s.ReloadTime:0.0}s", dx + 14, by + 4, 16, U.C(200, 200, 215)); by += 26;
            Raylib.DrawText($"Comes with {s.StartReserve} spare rounds", dx + 14, by + 4, 16, U.C(200, 200, 215)); by += 26;
            Raylib.DrawText($"Ammo pack: {s.PackSize} rounds for ${s.PackPrice}", dx + 14, by + 4, 16, U.C(200, 200, 215)); by += 26;
        }
        else
        {
            Raylib.DrawText("No ammunition needed", dx + 14, by + 4, 16, U.C(200, 200, 215)); by += 26;
        }
        if (s.Blast > 0f) Raylib.DrawText($"Blast radius {s.Blast:0} m", dx + 14, by + 4, 16, U.C(255, 150, 70));
        }

        string act = sArmor ? (pl.Armor >= Player.MaxArmor - 0.5f ? "Armor is full" : $"ENTER - buy armor for ${pl.ArmorCost}")
            : mine == null ? $"ENTER - buy for ${s.Price}"
            : s.Melee ? "You own this weapon"
            : mine.Reserve >= s.MaxReserve ? "Ammo is full"
            : $"ENTER - buy {s.PackSize} rounds for ${s.PackPrice}";
        int ay = dy + L.Rows * L.RowH - 4 - 44;
        Raylib.DrawRectangle(dx + 10, ay, L.DetW - 20, 34, U.C(40, 100, 60));
        int aw = Raylib.MeasureText(act, 18);
        Raylib.DrawText(act, dx + L.DetW / 2 - aw / 2, ay + 8, 18, U.C(255, 255, 255));

        // ---- footer
        if (msgT > 0f)
        {
            int tw = Raylib.MeasureText(msg, 22);
            Raylib.DrawText(msg, L.Px + L.W / 2 - tw / 2, L.Py + L.H - 70, 22, U.C(255, 235, 120));
        }
        Raylib.DrawText("Up/Down / mouse - choose    Enter / click - buy    E or ESC - leave", L.Px + 24, L.Py + L.H - 32, 18, U.C(170, 170, 190));
    }

    static void WrapText(string text, int x, int y, int maxW, int size, Color c)
    {
        string line = "";
        foreach (string word in text.Split(' '))
        {
            string t = line.Length == 0 ? word : line + " " + word;
            if (Raylib.MeasureText(t, size) > maxW && line.Length > 0)
            {
                Raylib.DrawText(line, x, y, size, c);
                y += size + 4;
                line = word;
            }
            else line = t;
        }
        if (line.Length > 0) Raylib.DrawText(line, x, y, size, c);
    }
}
