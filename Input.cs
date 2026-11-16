using System.Text.Json;
using Raylib_cs;

namespace MiniGTA;

/// <summary>Everything the player can re-bind in the CONTROLS screen of the main menu.</summary>
public enum Act { Forward, Back, Left, Right, Sprint, Jump, Use, Holster, Reload, View, Map, Save }

public static class Input
{
    public static readonly string[] Labels =
    {
        "Move forward / accelerate", "Move back / brake", "Move left / steer left", "Move right / steer right", "Sprint",
        "Jump / handbrake", "Use / enter / exit car", "Holster / draw weapon", "Reload", "Change view (1st / 3rd person)",
        "Open the map (waypoint)", "Quick save"
    };

    static readonly KeyboardKey[] Defaults =
    {
        KeyboardKey.W, KeyboardKey.S, KeyboardKey.A, KeyboardKey.D, KeyboardKey.LeftShift, KeyboardKey.Space,
        KeyboardKey.E, KeyboardKey.H, KeyboardKey.R, KeyboardKey.V, KeyboardKey.M, KeyboardKey.F5
    };

    static readonly KeyboardKey[] keys = (KeyboardKey[])Defaults.Clone();

    public static KeyboardKey Key(Act a) => keys[(int)a];
    public static bool Down(Act a) => Raylib.IsKeyDown(keys[(int)a]);
    public static bool Pressed(Act a) => Raylib.IsKeyPressed(keys[(int)a]);

    /// <summary>Keys that cannot be bound: they have a fixed job (menu, weapon slots 1-7, quit, fullscreen).</summary>
    public static bool Reserved(KeyboardKey k)
    {
        int v = (int)k;
        if (v == 0) return true;
        if (k == KeyboardKey.Escape || k == KeyboardKey.F10 || k == KeyboardKey.F11) return true;
        return v >= (int)KeyboardKey.One && v <= (int)KeyboardKey.Seven;
    }

    /// <summary>Binds a key. If another action already uses it the two actions swap keys.</summary>
    public static void Set(Act a, KeyboardKey k)
    {
        KeyboardKey old = keys[(int)a];
        for (int i = 0; i < keys.Length; i++)
            if (i != (int)a && keys[i] == k) keys[i] = old;
        keys[(int)a] = k;
    }

    public static void ResetAll() => Array.Copy(Defaults, keys, keys.Length);

    public static string Name(KeyboardKey k)
    {
        string s = k.ToString();
        string[] digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
        int di = Array.IndexOf(digits, s);
        if (di >= 0) return di.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    public static string Name(Act a) => Name(Key(a));

    /// <summary>The key line at the bottom of the screen, built from the current bindings.</summary>
    public static string HelpLine() =>
        $"{Name(Act.Forward)}/{Name(Act.Left)}/{Name(Act.Back)}/{Name(Act.Right)} drive/move | {Name(Act.Sprint)} sprint | {Name(Act.Jump)} jump/handbrake | LMB fire | RMB zoom (sniper) | " +
        $"1-7 weapon slot / wheel | {Name(Act.Reload)} reload | {Name(Act.Use)} use/enter | {Name(Act.Holster)} holster | {Name(Act.View)} view | {Name(Act.Map)} map + waypoint | " +
        $"{Name(Act.Save)} save | ESC menu | F11 fullscreen | F10 quit";

    // ---------------------------------------------------------------- file
    static string PathName => Path.Combine(SaveFile.Dir, "controls.json");

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SaveFile.Dir);
            var d = new Dictionary<string, int>();
            for (int i = 0; i < keys.Length; i++) d[((Act)i).ToString()] = (int)keys[i];
            File.WriteAllText(PathName, JsonSerializer.Serialize(d));
        }
        catch (Exception e) { Console.Error.WriteLine("Could not save controls: " + e.Message); }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathName)) return;
            var d = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(PathName));
            if (d == null) return;
            for (int i = 0; i < keys.Length; i++)
                if (d.TryGetValue(((Act)i).ToString(), out int v) && v > 0 && !Reserved((KeyboardKey)v)) keys[i] = (KeyboardKey)v;
        }
        catch (Exception e) { Console.Error.WriteLine("Could not read controls: " + e.Message); }
    }
}
