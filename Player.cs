using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public class Player
{
    public Vector3 Pos;
    public float Yaw, Phase, VelY, Speed;
    public bool Moving;
    public const int StartMoney = 150;       // a new game starts with $150 and no weapons
    public int Money = StartMoney;
    public Car Car;

    // ---- health and armour
    public const float MaxHp = 100f, MaxArmor = 100f;
    public const float RegenCap = 50f;         // you slowly recover up to this much on your own; the rest costs money at a hospital
    public float Hp = MaxHp, Armor;
    public float SinceHurt = 99f;              // seconds since the last damage (regeneration waits for it)
    public bool Dead => Hp <= 0f;

    /// <summary>Armour soaks the damage first, whatever is left hurts you. Returns the damage that got through to the health.</summary>
    public float Hurt(float dmg)
    {
        if (dmg <= 0f || Dead) return 0f;
        float soak = MathF.Min(Armor, dmg);
        Armor -= soak;
        float rest = dmg - soak;
        Hp = MathF.Max(0f, Hp - rest);
        SinceHurt = 0f;
        return rest;
    }

    /// <summary>Slow natural recovery: starts a few seconds after the last hit and stops at RegenCap.</summary>
    public void Regen(float dt)
    {
        SinceHurt += dt;
        if (Dead || SinceHurt < 8f || Hp >= RegenCap) return;
        Hp = MathF.Min(RegenCap, Hp + 1.2f * dt);
    }

    public int ArmorCost => (int)MathF.Ceiling((MaxArmor - Armor) * Arsenal.ArmorPrice / MaxArmor);
    public const int HealPricePerHp = 2;
    public int HealCost => (int)MathF.Ceiling(MaxHp - Hp) * HealPricePerHp;

    public readonly List<Weapon> Weapons = new();   // the weapons you own, sorted like the catalogue
    public int Cur;
    public bool Armed;
    public float Cool, Reload;

    public Weapon W => Weapons.Count > 0 ? Weapons[Math.Clamp(Cur, 0, Weapons.Count - 1)] : null;

    public Player(Vector3 start)
    {
        Pos = start;
    }

    public Weapon Get(string id) => Weapons.Find(w => w.Id == id);

    /// <summary>Adds a weapon you do not own yet and takes it into your hands.</summary>
    public Weapon Give(Weapon def)
    {
        var w = def.Fresh();
        Weapons.Add(w);
        Weapons.Sort((a, b) => a.Order.CompareTo(b.Order));
        Select(Weapons.IndexOf(w));
        return w;
    }

    /// <summary>Adds the weapon from a save file (keeps the saved ammo).</summary>
    public void Restore(Weapon def, int ammo, int reserve)
    {
        var w = def.Fresh();
        w.Ammo = Math.Clamp(ammo, 0, w.Mag);
        w.Reserve = Math.Clamp(reserve, 0, Math.Max(w.MaxReserve, w.StartReserve));
        Weapons.Add(w);
        Weapons.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public void Select(int idx)
    {
        if (Weapons.Count == 0) { Cur = 0; Armed = false; return; }
        Cur = Math.Clamp(idx, 0, Weapons.Count - 1);
        Armed = true;
        Reload = 0f;
    }

    /// <summary>Number keys: jump to the first weapon in the slot, or to the next one if you already hold one from it.</summary>
    public bool SelectSlot(int slot)
    {
        int n = Weapons.Count;
        if (n == 0) return false;
        int first = -1, next = -1;
        for (int i = 0; i < n; i++)
        {
            if ((int)Weapons[i].Slot != slot) continue;
            if (first < 0) first = i;
            if (i > Cur && next < 0 && Armed && (int)W.Slot == slot) next = i;
        }
        if (first < 0) return false;
        Select(next >= 0 ? next : first);
        return true;
    }

    /// <summary>Mouse wheel: previous / next weapon.</summary>
    public void Cycle(int dir)
    {
        if (Weapons.Count == 0) return;
        Select((Cur + dir + Weapons.Count) % Weapons.Count);
    }

    static readonly HumanLook Look = new HumanLook
    {
        Shirt = U.C(168, 36, 44),
        Pants = U.C(36, 46, 78),
        Skin = U.C(226, 176, 140),
        Hair = U.C(30, 22, 16),
        Shoes = U.C(20, 20, 22),
        LongSleeve = true,
        Glasses = true,
        IsPlayer = true
    };

    public void Draw()
    {
        if (Dead)
        {
            // lying on the ground (no model is needed for it)
            Vector3 p = Pos; p.Y += World.GroundY(p.X, p.Z);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.13f, 0.1f), new Vector3(0.50f, 0.22f, 0.70f), Yaw, Look.Shirt);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.10f, -0.65f), new Vector3(0.36f, 0.20f, 0.75f), Yaw, Look.Pants);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.14f, 0.60f), new Vector3(0.24f, 0.24f, 0.26f), Yaw, Look.Skin);
            Gfx.Box(U.Local(p, Yaw, 0f, 0.20f, 0.58f), new Vector3(0.25f, 0.10f, 0.27f), Yaw, Look.Hair);
            Gfx.Shadow(Pos, Yaw, 0.9f, 1.6f);
            return;
        }
        Human.Draw(Pos, Yaw, Phase, Moving ? 1f : 0f, Look, Armed && W != null ? (int)W.Look : -1, 0f, Speed);
    }
}
