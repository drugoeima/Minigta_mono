using Raylib_cs;

namespace MiniGTA;

/// <summary>Casino overlay with two games: slots and roulette. Uses the player's money.</summary>
public static class Casino
{
    public static bool Active;

    static string name = "";
    static int tab;                  // 0 = slots, 1 = roulette
    static int bi = 2;               // selected bet index
    static readonly int[] Bets = { 10, 50, 100, 500, 1000 };

    static readonly string[] Syms = { "7", "BAR", "CHERRY", "BELL", "GEM", "LEMON" };
    static readonly Color[] SymCol =
    {
        U.C(255, 60, 60), U.C(230, 230, 230), U.C(255, 80, 140), U.C(255, 210, 40), U.C(80, 220, 255), U.C(240, 240, 80)
    };
    static readonly int[] reel = new int[3];
    static readonly int[] fin = new int[3];

    static int rnum = -1, rfin, choice;   // choice: 0 red, 1 black, 2 green
    static bool spinning;
    static float el;
    static int curBet;
    static string msg = "";

    // blackjack
    static int reelsStopped;
    static float tickT;
    static readonly List<int> bjP = new(), bjD = new();
    static int bjState;              // 0 = no hand in progress, 1 = player's turn
    static int bjBet;
    static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
    static readonly string[] Suits = { "S", "H", "D", "C" };

    static readonly HashSet<int> Reds = new() { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

    public static void Open(string casinoName, int game = -1)
    {
        Active = true;
        if (game >= 0) tab = game;
        bjState = 0;
        name = casinoName;
        msg = "";
        spinning = false;
        rnum = -1;
    }

    public static void Update(float dt, Player p)
    {
        bool inHand = tab == 2 && bjState == 1;
        if (!spinning && !inHand && (Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Escape)))
        {
            Active = false;
            return;
        }

        if (spinning)
        {
            el += dt;
            tickT -= dt;
            if (tab == 0)
            {
                int stopsNow = (el >= 0.8f ? 1 : 0) + (el >= 1.2f ? 1 : 0) + (el >= 1.6f ? 1 : 0);
                if (stopsNow > reelsStopped) { reelsStopped = stopsNow; Sfx.ReelStop(); }
                if (tickT <= 0f && stopsNow < 3) { tickT = 0.07f; Sfx.SlotTick(); }
                for (int k = 0; k < 3; k++)
                    reel[k] = el >= 0.8f + 0.4f * k ? fin[k] : U.R.Next(Syms.Length);
                if (el >= 1.7f) Finish(p);
            }
            else
            {
                rnum = el >= 1.6f ? rfin : U.R.Next(37);
                if (tickT <= 0f && el < 1.6f) { tickT = 0.05f + el * 0.07f; Sfx.RouletteTick(1.2f - el * 0.3f); }
                if (el >= 1.6f) Finish(p);
            }
            return;
        }

        if (!inHand && Raylib.IsKeyPressed(KeyboardKey.Tab)) { tab = (tab + 1) % 3; msg = ""; }
        if (!inHand && Raylib.IsKeyPressed(KeyboardKey.Up)) bi = Math.Min(bi + 1, Bets.Length - 1);
        if (!inHand && Raylib.IsKeyPressed(KeyboardKey.Down)) bi = Math.Max(bi - 1, 0);

        if (tab == 1)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.R)) choice = 0;
            if (Raylib.IsKeyPressed(KeyboardKey.B)) choice = 1;
            if (Raylib.IsKeyPressed(KeyboardKey.G)) choice = 2;
        }

        if (tab == 2)
        {
            if (inHand)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.H)) BjHit(p);
                else if (Raylib.IsKeyPressed(KeyboardKey.S)) BjStand(p);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Space)) BjDeal(p);
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Space)) Start(p);
    }

    // ------------------------------------------------------------------ blackjack
    static int Card() => U.R.Next(52);

    static int BjVal(List<int> h)
    {
        int t = 0, aces = 0;
        foreach (int c in h)
        {
            int r = c % 13;
            if (r == 0) { aces++; t += 11; }
            else t += r >= 9 ? 10 : r + 1;
        }
        while (t > 21 && aces > 0) { t -= 10; aces--; }
        return t;
    }

    static void BjDeal(Player p)
    {
        int bet = Bets[bi];
        if (p.Money < bet) { msg = "Not enough money!"; return; }
        p.Money -= bet;
        bjBet = bet;
        bjP.Clear(); bjD.Clear();
        bjP.Add(Card()); bjD.Add(Card()); bjP.Add(Card()); bjD.Add(Card());
        Sfx.Card();
        bjState = 1;
        msg = "";
        if (BjVal(bjP) == 21) BjSettle(p);      // natural blackjack
    }

    static void BjHit(Player p)
    {
        bjP.Add(Card());
        Sfx.Card();
        int v = BjVal(bjP);
        if (v > 21) BjSettle(p);
        else if (v == 21) BjStand(p);
    }

    static void BjStand(Player p)
    {
        while (BjVal(bjD) < 17) bjD.Add(Card());
        BjSettle(p);
    }

    static void BjSettle(Player p)
    {
        bjState = 0;
        int pv = BjVal(bjP), dv = BjVal(bjD);
        bool pNat = bjP.Count == 2 && pv == 21, dNat = bjD.Count == 2 && dv == 21;
        float mult;
        if (pv > 21) mult = 0f;
        else if (pNat && !dNat) mult = 2.5f;
        else if (dv > 21 || pv > dv) mult = 2f;
        else if (pv == dv) mult = 1f;
        else mult = 0f;

        int win = (int)(bjBet * mult);
        p.Money += win;
        if (mult >= 2f) Sfx.Win(mult > 2f); else if (mult == 1f) Sfx.Cash(0.4f); else Sfx.Lose();
        msg = mult == 0f ? "You lose" : mult == 1f ? "Push - bet returned" : mult == 2.5f ? $"BLACKJACK +${win - bjBet}!" : $"WIN +${win - bjBet}!";
    }

    static void DrawHand(List<int> h, int y, string who, bool hideSecond)
    {
        int sw = Raylib.GetScreenWidth();
        int cw = 78, ch = 108, gap = 12;
        string label = h.Count == 0 ? who : (hideSecond ? who : $"{who}: {BjVal(h)}");
        Center(label, y - 30, 22, U.C(230, 230, 240));
        if (h.Count == 0) return;

        int x0 = sw / 2 - (h.Count * (cw + gap) - gap) / 2;
        for (int i = 0; i < h.Count; i++)
        {
            int x = x0 + i * (cw + gap);
            if (hideSecond && i == 1)
            {
                Raylib.DrawRectangle(x, y, cw, ch, U.C(40, 60, 140));
                Raylib.DrawRectangleLines(x, y, cw, ch, U.C(255, 200, 40));
                int qw = Raylib.MeasureText("?", 48);
                Raylib.DrawText("?", x + cw / 2 - qw / 2, y + ch / 2 - 24, 48, U.C(255, 255, 255));
                continue;
            }
            int c = h[i];
            Color col = (c / 13 == 1 || c / 13 == 2) ? U.C(200, 30, 40) : U.C(20, 20, 24);
            Raylib.DrawRectangle(x, y, cw, ch, U.C(240, 240, 240));
            Raylib.DrawRectangleLines(x, y, cw, ch, U.C(255, 200, 40));
            Raylib.DrawText(Ranks[c % 13], x + 8, y + 8, 30, col);
            Raylib.DrawText(Suits[c / 13], x + 8, y + 44, 28, col);
        }
    }

    static void DrawBlackjack(int sh)
    {
        DrawHand(bjD, sh / 2 - 150, "DEALER", bjState == 1);
        DrawHand(bjP, sh / 2 + 10, "YOU", false);
    }

    static void Start(Player p)
    {
        int bet = Bets[bi];
        if (p.Money < bet) { msg = "Not enough money!"; return; }

        p.Money -= bet;
        curBet = bet;
        reelsStopped = 0; tickT = 0f;
        spinning = true;
        el = 0f;
        msg = "";

        if (tab == 0) for (int k = 0; k < 3; k++) fin[k] = U.R.Next(Syms.Length);
        else rfin = U.R.Next(37);
    }

    static void Finish(Player p)
    {
        spinning = false;
        float mult = 0f;

        if (tab == 0)
        {
            if (fin[0] == fin[1] && fin[1] == fin[2]) mult = fin[0] == 0 ? 100f : 12f;
            else if (fin[0] == fin[1] || fin[1] == fin[2] || fin[0] == fin[2]) mult = 0.5f;
        }
        else
        {
            int color = rnum == 0 ? 2 : (Reds.Contains(rnum) ? 0 : 1);
            if (color == choice) mult = color == 2 ? 36f : 2f;
        }

        int win = (int)(curBet * mult);
        p.Money += win;
        if (win > curBet) Sfx.Win(mult >= 12f); else if (win > 0) Sfx.Cash(0.5f); else Sfx.Lose();
        msg = win > curBet ? $"WIN +${win - curBet}!" : (win > 0 ? $"Half back: ${win}" : "You lose");
    }

    public static void Draw(Player p)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, U.C(8, 4, 20, 225));

        Center(name.ToUpper() + " CASINO", 40, 44, U.C(255, 200, 40));
        string[] tn = { "SLOTS", "ROULETTE", "BLACKJACK" };
        string head = "";
        for (int i = 0; i < tn.Length; i++) head += (i == tab ? $"< {tn[i]} >" : tn[i].ToLower()) + "   ";
        Center(head.TrimEnd(), 100, 24, U.C(230, 230, 240));
        Center($"Money: ${p.Money}      Bet: ${Bets[bi]}", 140, 30, U.C(90, 230, 120));

        if (tab == 0)
        {
            int bw = 170, gap = 20;
            int x0 = sw / 2 - (3 * bw + 2 * gap) / 2, y = sh / 2 - 110;
            for (int k = 0; k < 3; k++)
            {
                int x = x0 + k * (bw + gap);
                Raylib.DrawRectangle(x, y, bw, bw, U.C(20, 16, 40));
                Raylib.DrawRectangleLines(x, y, bw, bw, U.C(255, 200, 40));
                string s = Syms[reel[k]];
                int fs = s.Length > 3 ? 36 : 64;
                int tw = Raylib.MeasureText(s, fs);
                Raylib.DrawText(s, x + bw / 2 - tw / 2, y + bw / 2 - fs / 2, fs, SymCol[reel[k]]);
            }
            Center("Three 7 = x100 | three of a kind = x12 | pair = x0.5", sh / 2 + 85, 20, U.C(160, 160, 180));
        }
        else if (tab == 1)
        {
            int bs = 200;
            int x = sw / 2 - bs / 2, y = sh / 2 - 130;
            Color nc = rnum < 0 ? U.C(40, 40, 60) : (rnum == 0 ? U.C(30, 150, 60) : (Reds.Contains(rnum) ? U.C(200, 30, 40) : U.C(30, 30, 34)));
            Raylib.DrawRectangle(x, y, bs, bs, nc);
            Raylib.DrawRectangleLines(x, y, bs, bs, U.C(255, 200, 40));
            string s = rnum < 0 ? "?" : rnum.ToString();
            int tw = Raylib.MeasureText(s, 90);
            Raylib.DrawText(s, x + bs / 2 - tw / 2, y + bs / 2 - 45, 90, U.C(255, 255, 255));

            string[] names = { "RED (R)  x2", "BLACK (B)  x2", "GREEN (G)  x36" };
            Color[] cols = { U.C(200, 30, 40), U.C(60, 60, 70), U.C(30, 150, 60) };
            int bw = 190, gap = 16;
            int bx0 = sw / 2 - (3 * bw + 2 * gap) / 2, by = sh / 2 + 90;
            for (int k = 0; k < 3; k++)
            {
                int bx = bx0 + k * (bw + gap);
                Raylib.DrawRectangle(bx, by, bw, 44, cols[k]);
                if (choice == k) Raylib.DrawRectangleLines(bx - 3, by - 3, bw + 6, 50, U.C(255, 200, 40));
                int w2 = Raylib.MeasureText(names[k], 22);
                Raylib.DrawText(names[k], bx + bw / 2 - w2 / 2, by + 11, 22, U.C(255, 255, 255));
            }
        }

        if (tab == 2) DrawBlackjack(sh);
        Center(msg, tab == 2 ? sh / 2 + 140 : sh / 2 + 150, 40, U.C(255, 210, 40));
        string help = tab == 2
            ? (bjState == 1 ? "H hit | S stand" : "SPACE deal | UP/DOWN bet | TAB switch game | E / ESC leave")
            : "SPACE spin | UP/DOWN bet | TAB switch game | E / ESC leave";
        Center(help, sh - 60, 22, U.C(160, 160, 180));
    }

    static void Center(string t, int y, int size, Color c)
    {
        int w = Raylib.MeasureText(t, size);
        Raylib.DrawText(t, Raylib.GetScreenWidth() / 2 - w / 2, y, size, c);
    }
}
