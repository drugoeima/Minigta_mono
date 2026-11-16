using System.Text.Json;

namespace MiniGTA;

// ---- what goes into the save file (plain fields, serialised with System.Text.Json)
public class SavedWeapon { public string Id = ""; public int Ammo, Reserve; }
public class SavedMission { public string Kind = ""; public int State; public float Progress; public List<string> Seen = new(); }
public class SavedCar
{
    public float X, Z, Yaw;
    public int Kind, Model = -1;
    public byte R, G, B;
    public bool Owned, Stolen, Driven;
    public int Value;
}

public class SaveData
{
    public int Version = 1;
    public string SavedAt = "";
    public int Money;
    public int StoryStep;                   // index of the current story step (0 = the story starts from the beginning)
    public float Hp, Armor;                 // Hp <= 0 means "no value in the file" (old saves) -> full health
    public float X, Z, Yaw;
    public bool Armed;
    public string Current = "";
    public List<SavedWeapon> Weapons = new();
    public List<SavedMission> Missions = new();
    public List<string> Properties = new();
    public List<SavedCar> Cars = new();
}

/// <summary>Reads / writes the save file. It lives in the user's local application-data folder, so rebuilding the game never deletes it.</summary>
public static class SaveFile
{
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true, WriteIndented = true };

    public static string Dir
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;      // no profile folder: save next to the game
            return Path.Combine(root, "MiniGTA");
        }
    }
    public static string PathName => Path.Combine(Dir, "save.json");

    public static bool Exists => File.Exists(PathName);

    public static bool Write(SaveData d)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            d.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string tmp = PathName + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, Opt));
            File.Move(tmp, PathName, true);          // replace atomically: a crash never leaves a half-written save
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Save failed: " + e.Message);
            return false;
        }
    }

    public static SaveData Read()
    {
        try
        {
            if (!File.Exists(PathName)) return null;
            var d = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(PathName), Opt);
            return d != null && d.Version == 1 ? d : null;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Load failed: " + e.Message);
            try { File.Move(PathName, PathName + ".bad", true); } catch { /* keep the broken file out of the way */ }
            return null;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(PathName)) File.Delete(PathName); } catch { }
    }
}
