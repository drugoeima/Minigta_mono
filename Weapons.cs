namespace MiniGTA;

/// <summary>Weapon slots (keys 1-7). Pressing a slot key again cycles through the weapons you own in that slot.</summary>
public enum WSlot { Melee, Pistol, Smg, Shotgun, Rifle, Sniper, Heavy }

/// <summary>Which model Human.DrawGun draws. The first three keep their old numbers (cops pass 0 = pistol).</summary>
public enum WLook { Pistol, Smg, Shotgun, Rifle, Sniper, Launcher, Bat, Blade, Minigun, Magnum }

/// <summary>One weapon: the static stats come from the Arsenal catalogue, Ammo / Reserve are the player's own state.</summary>
public class Weapon
{
    public string Id = "", Name = "", Desc = "";
    public WSlot Slot;
    public WLook Look;
    public int Order;                         // position in the catalogue (the player's list is sorted by it)

    public int Price;                         // price of the weapon itself (comes with StartReserve rounds)
    public int PackSize, PackPrice;           // an ammo pack: rounds / price
    public int StartReserve, MaxReserve;

    public float Damage, Rate, Spread, Range = 160f, ReloadTime = 1.4f;
    public float Blast;                       // > 0: explodes at the impact point, radius in metres
    public int Pellets = 1, Mag, Ammo, Reserve;
    public bool Auto, Melee;

    public bool Sold(int shopKind) => shopKind == 1 || Slot != WSlot.Heavy;      // heavy weapons: only the Black Market

    public Weapon Clone() => (Weapon)MemberwiseClone();

    /// <summary>A new copy for the player, with the starting ammunition.</summary>
    public Weapon Fresh()
    {
        var w = Clone();
        w.Ammo = w.Mag;
        w.Reserve = w.StartReserve;
        return w;
    }
}

public static class Arsenal
{
    public static readonly string[] SlotNames = { "Melee", "Pistols", "SMG", "Shotguns", "Rifles", "Sniper", "Heavy" };

    // Damage numbers are per bullet / pellet. A pedestrian has 40 hp, a headshot does x3.
    public static readonly List<Weapon> All = Build();

    static List<Weapon> Build()
    {
        var l = new List<Weapon>
        {
            // ---- melee (no ammunition)
            new Weapon { Id = "knife", Name = "Knife", Slot = WSlot.Melee, Look = WLook.Blade, Melee = true, Price = 25,
                Damage = 35, Rate = 2.2f, Range = 2.2f,
                Desc = "Quiet and cheap. Two stabs drop a pedestrian." },
            new Weapon { Id = "bat", Name = "Baseball Bat", Slot = WSlot.Melee, Look = WLook.Bat, Melee = true, Price = 45,
                Damage = 55, Rate = 1.3f, Range = 2.7f,
                Desc = "A classic. Long reach, one good swing is usually enough." },
            new Weapon { Id = "machete", Name = "Machete", Slot = WSlot.Melee, Look = WLook.Blade, Melee = true, Price = 140,
                Damage = 80, Rate = 1.5f, Range = 2.6f,
                Desc = "Heavy blade. Kills on the spot." },

            // ---- pistols
            new Weapon { Id = "pistol", Name = "Pistol", Slot = WSlot.Pistol, Look = WLook.Pistol, Price = 120,
                Damage = 25, Rate = 3.5f, Spread = 0.004f, Mag = 12, StartReserve = 36, MaxReserve = 120, ReloadTime = 1.3f,
                PackSize = 24, PackPrice = 30,
                Desc = "Reliable 9mm sidearm. Cheap rounds, easy to handle." },
            new Weapon { Id = "deagle", Name = "Desert Eagle", Slot = WSlot.Pistol, Look = WLook.Magnum, Price = 650,
                Damage = 60, Rate = 2.0f, Spread = 0.004f, Mag = 7, StartReserve = 21, MaxReserve = 70, ReloadTime = 1.8f,
                PackSize = 14, PackPrice = 60,
                Desc = ".50 hand cannon. A headshot kills anything on two legs." },

            // ---- SMG
            new Weapon { Id = "micro", Name = "Micro SMG", Slot = WSlot.Smg, Look = WLook.Smg, Price = 450,
                Damage = 9, Rate = 16f, Spread = 0.04f, Mag = 20, StartReserve = 60, MaxReserve = 240, ReloadTime = 1.5f, Auto = true,
                PackSize = 40, PackPrice = 50,
                Desc = "Drive-by classic. Huge fire rate, wild spread." },
            new Weapon { Id = "smg", Name = "SMG", Slot = WSlot.Smg, Look = WLook.Smg, Price = 900,
                Damage = 13, Rate = 11f, Spread = 0.025f, Mag = 30, StartReserve = 90, MaxReserve = 270, ReloadTime = 1.8f, Auto = true,
                PackSize = 60, PackPrice = 80,
                Desc = "MP5-style submachine gun. Controllable and fast." },

            // ---- shotguns
            new Weapon { Id = "sawed", Name = "Sawed-Off Shotgun", Slot = WSlot.Shotgun, Look = WLook.Shotgun, Price = 500,
                Damage = 9, Rate = 1.6f, Spread = 0.12f, Range = 45f, Pellets = 12, Mag = 2, StartReserve = 10, MaxReserve = 36, ReloadTime = 2.0f,
                PackSize = 8, PackPrice = 40,
                Desc = "Two barrels, brutal at close range, useless at distance." },
            new Weapon { Id = "pump", Name = "Pump Shotgun", Slot = WSlot.Shotgun, Look = WLook.Shotgun, Price = 700,
                Damage = 11, Rate = 1.1f, Spread = 0.07f, Pellets = 8, Mag = 6, StartReserve = 24, MaxReserve = 60, ReloadTime = 2.2f,
                PackSize = 12, PackPrice = 50,
                Desc = "Standard 12-gauge. Good all-round street weapon." },
            new Weapon { Id = "spas", Name = "Combat Shotgun", Slot = WSlot.Shotgun, Look = WLook.Shotgun, Price = 1400,
                Damage = 10, Rate = 3.0f, Spread = 0.06f, Pellets = 8, Mag = 8, StartReserve = 32, MaxReserve = 80, ReloadTime = 2.6f, Auto = true,
                PackSize = 16, PackPrice = 90,
                Desc = "SPAS-12 style. Hold the trigger for rapid fire." },

            // ---- assault rifles
            new Weapon { Id = "ak47", Name = "AK-47", Slot = WSlot.Rifle, Look = WLook.Rifle, Price = 1800,
                Damage = 22, Rate = 9f, Spread = 0.018f, Range = 200f, Mag = 30, StartReserve = 90, MaxReserve = 300, ReloadTime = 2.2f, Auto = true,
                PackSize = 60, PackPrice = 110,
                Desc = "Hard-hitting assault rifle. Rough but deadly." },
            new Weapon { Id = "m4", Name = "M4 Carbine", Slot = WSlot.Rifle, Look = WLook.Rifle, Price = 2300,
                Damage = 19, Rate = 12f, Spread = 0.012f, Range = 220f, Mag = 30, StartReserve = 90, MaxReserve = 300, ReloadTime = 2.0f, Auto = true,
                PackSize = 60, PackPrice = 130,
                Desc = "Accurate and fast. The all-round killing machine." },

            // ---- sniper
            new Weapon { Id = "sniper", Name = "Sniper Rifle", Slot = WSlot.Sniper, Look = WLook.Sniper, Price = 3500,
                Damage = 150, Rate = 0.8f, Spread = 0.0006f, Range = 450f, Mag = 5, StartReserve = 15, MaxReserve = 40, ReloadTime = 2.8f,
                PackSize = 10, PackPrice = 120,
                Desc = "One shot, one kill. Hold the right mouse button to zoom." },

            // ---- heavy weapons (Black Market only)
            new Weapon { Id = "gl", Name = "Grenade Launcher", Slot = WSlot.Heavy, Look = WLook.Launcher, Price = 4500,
                Damage = 140, Rate = 0.9f, Spread = 0.004f, Range = 180f, Blast = 7f, Mag = 1, StartReserve = 6, MaxReserve = 20, ReloadTime = 1.8f,
                PackSize = 4, PackPrice = 200,
                Desc = "Explosive rounds with a 7 m blast radius." },
            new Weapon { Id = "rpg", Name = "RPG", Slot = WSlot.Heavy, Look = WLook.Launcher, Price = 6500,
                Damage = 300, Rate = 0.5f, Spread = 0.002f, Range = 240f, Blast = 11f, Mag = 1, StartReserve = 3, MaxReserve = 10, ReloadTime = 2.8f,
                PackSize = 2, PackPrice = 250,
                Desc = "Rocket launcher. 11 m blast radius. Expect a lot of attention." },
            new Weapon { Id = "minigun", Name = "Minigun", Slot = WSlot.Heavy, Look = WLook.Minigun, Price = 9500,
                Damage = 8, Rate = 30f, Spread = 0.05f, Range = 180f, Mag = 200, StartReserve = 400, MaxReserve = 1000, ReloadTime = 4.0f, Auto = true,
                PackSize = 200, PackPrice = 300,
                Desc = "Six barrels and 30 rounds a second. Pure chaos." },
        };
        for (int i = 0; i < l.Count; i++)
        {
            l[i].Order = i;
            if (l[i].Melee) { l[i].Mag = 0; l[i].StartReserve = 0; l[i].MaxReserve = 0; }
        }
        return l;
    }

    /// <summary>Bare hands: always available, not sold in shops.</summary>
    public static readonly Weapon Fists = new Weapon { Id = "fists", Name = "Fists", Slot = WSlot.Melee, Look = WLook.Blade, Melee = true,
        Damage = 20, Rate = 2.6f, Range = 1.8f };

    /// <summary>Body armour: sold in the AMMU-NATION list as an extra row (it is not a weapon and never goes into the player's list).</summary>
    public const int ArmorPrice = 350;              // price of a full 100 points; a damaged vest is repaired pro rata
    public static readonly Weapon ArmorItem = new Weapon { Id = "armor", Name = "Body Armor", Slot = WSlot.Melee, Look = WLook.Pistol,
        Desc = "Kevlar vest. Soaks damage before your health takes any. Buying tops it up to 100." };
    public static bool IsArmor(Weapon w) => w == ArmorItem;

    public static Weapon Find(string id) => All.Find(w => w.Id == id);
}
