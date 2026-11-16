using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

public enum CarKind { Sedan, Sport, Taxi, Suv, Pickup, Van, Truck, Bus, Police }

/// <summary>
/// One vehicle class: size, traffic speed, driving feel, price at the Scrap Yard and paint colours.
/// The class of a user model comes from its file name: car_bus_1.glb, car_truck.glb, car_sport_red.glb ...
/// (no keyword = sedan).
/// </summary>
public class CarType
{
    public CarKind Kind;
    public string Name = "";
    public float Len, Wid, Hgt;            // size in world units (Hgt = roof height)
    public float CruiseMin, CruiseMax;     // speed in traffic
    public float Top, Accel, Steer;        // when the player drives: top speed, acceleration, steering factor
    public float Radius;                   // collision circle (cars / buildings)
    public float WheelR;
    public int Weight;                     // how often it shows up in traffic
    public int PriceMin, PriceMax;         // Scrap Yard price
    public Color[] Paint = Std;

    static readonly Color[] Std =
    {
        U.C(200, 30, 40), U.C(30, 90, 200), U.C(235, 200, 30), U.C(235, 235, 235),
        U.C(25, 25, 28), U.C(240, 120, 20), U.C(20, 160, 150), U.C(150, 40, 170)
    };
    static readonly Color[] SportPaint =
    {
        U.C(210, 20, 30), U.C(235, 200, 30), U.C(20, 70, 190), U.C(20, 20, 24), U.C(245, 110, 15), U.C(235, 235, 235)
    };
    static readonly Color[] TaxiPaint = { U.C(244, 196, 20) };
    static readonly Color[] VanPaint =
    {
        U.C(235, 235, 235), U.C(170, 174, 182), U.C(40, 80, 150), U.C(210, 210, 200), U.C(120, 30, 34)
    };
    static readonly Color[] TruckPaint =
    {
        U.C(232, 232, 236), U.C(190, 60, 40), U.C(40, 90, 170), U.C(150, 154, 162), U.C(230, 170, 30)
    };
    static readonly Color[] BusPaint =
    {
        U.C(30, 90, 190), U.C(200, 40, 36), U.C(236, 236, 236), U.C(30, 140, 80), U.C(240, 190, 30)
    };
    // Police livery is fixed: black body with white doors. The light bar is drawn separately in Car.DrawPolice().
    static readonly Color[] PolicePaint = { U.C(28, 30, 36) };

    public static readonly CarType[] All =
    {
        new CarType { Kind = CarKind.Sedan,  Name = "Sedan",      Len = 4.4f,  Wid = 1.9f, Hgt = 1.55f, CruiseMin = 9f,  CruiseMax = 14f, Top = 36f, Accel = 24f, Steer = 1.00f, Radius = 1.9f, WheelR = 0.36f, Weight = 30, PriceMin = 700,  PriceMax = 1400 },
        new CarType { Kind = CarKind.Sport,  Name = "Sports car", Len = 4.3f,  Wid = 1.95f, Hgt = 1.22f, CruiseMin = 12f, CruiseMax = 18f, Top = 46f, Accel = 32f, Steer = 1.10f, Radius = 1.9f, WheelR = 0.34f, Weight = 8,  PriceMin = 2000, PriceMax = 3500, Paint = SportPaint },
        new CarType { Kind = CarKind.Taxi,   Name = "Taxi",       Len = 4.5f,  Wid = 1.9f, Hgt = 1.6f,  CruiseMin = 9f,  CruiseMax = 13f, Top = 34f, Accel = 22f, Steer = 1.00f, Radius = 1.9f, WheelR = 0.36f, Weight = 12, PriceMin = 600,  PriceMax = 1000, Paint = TaxiPaint },
        new CarType { Kind = CarKind.Suv,    Name = "SUV",        Len = 4.7f,  Wid = 2.0f, Hgt = 1.9f,  CruiseMin = 9f,  CruiseMax = 13f, Top = 33f, Accel = 21f, Steer = 0.95f, Radius = 2.1f, WheelR = 0.42f, Weight = 12, PriceMin = 1200, PriceMax = 2000 },
        new CarType { Kind = CarKind.Pickup, Name = "Pickup",     Len = 5.4f,  Wid = 2.0f, Hgt = 1.85f, CruiseMin = 8f,  CruiseMax = 12f, Top = 32f, Accel = 20f, Steer = 0.90f, Radius = 2.3f, WheelR = 0.43f, Weight = 10, PriceMin = 1000, PriceMax = 1800 },
        new CarType { Kind = CarKind.Van,    Name = "Van",        Len = 5.2f,  Wid = 2.0f, Hgt = 2.3f,  CruiseMin = 8f,  CruiseMax = 12f, Top = 30f, Accel = 18f, Steer = 0.85f, Radius = 2.3f, WheelR = 0.40f, Weight = 10, PriceMin = 800,  PriceMax = 1300, Paint = VanPaint },
        new CarType { Kind = CarKind.Truck,  Name = "Truck",      Len = 8.0f,  Wid = 2.5f, Hgt = 3.4f,  CruiseMin = 7f,  CruiseMax = 10f, Top = 26f, Accel = 13f, Steer = 0.65f, Radius = 3.2f, WheelR = 0.52f, Weight = 7,  PriceMin = 1500, PriceMax = 2500, Paint = TruckPaint },
        new CarType { Kind = CarKind.Bus,    Name = "Bus",        Len = 10.5f, Wid = 2.5f, Hgt = 3.3f,  CruiseMin = 6f,  CruiseMax = 9f,  Top = 22f, Accel = 11f, Steer = 0.55f, Radius = 3.8f, WheelR = 0.50f, Weight = 5,  PriceMin = 1200, PriceMax = 2000, Paint = BusPaint },
        // Police cruisers are never spawned as ordinary traffic - they are added explicitly by the Cop system when the player is wanted.
        // Top speed and accel are high so they can keep up with a fleeing player.
        new CarType { Kind = CarKind.Police, Name = "Police",    Len = 4.7f,  Wid = 2.0f, Hgt = 1.6f,  CruiseMin = 14f, CruiseMax = 18f, Top = 42f, Accel = 30f, Steer = 1.10f, Radius = 2.0f, WheelR = 0.38f, Weight = 0,  PriceMin = 0,    PriceMax = 0,    Paint = PolicePaint },
    };

    public static CarType Of(CarKind k) => All[(int)k];

    /// <summary>Random class by traffic weight. 'allowed' (optional) limits it to classes the user has models for.
    /// Police is never picked for ordinary traffic (Weight = 0).</summary>
    public static CarType Random(Func<CarKind, bool> allowed)
    {
        int total = 0;
        foreach (var t in All)
        {
            if (t.Kind == CarKind.Police) continue;          // cops are not civilian traffic
            if (allowed == null || allowed(t.Kind)) total += t.Weight;
        }
        if (total <= 0) return All[0];
        int r = U.R.Next(total);
        foreach (var t in All)
        {
            if (t.Kind == CarKind.Police) continue;
            if (allowed != null && !allowed(t.Kind)) continue;
            r -= t.Weight;
            if (r < 0) return t;
        }
        return All[0];
    }

    public int RollPrice() => U.R.Next(PriceMin / 50, PriceMax / 50 + 1) * 50;

    /// <summary>Class from a model file name (without extension), e.g. "car_bus_2" -> Bus.
    /// "police" or "cop" maps to the Police class, so the user can drop in car_police_1.glb / car_cop.glb.</summary>
    public static CarKind Parse(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("police") || n.Contains("cop")) return CarKind.Police;
        if (n.Contains("taxi")) return CarKind.Taxi;
        if (n.Contains("pickup")) return CarKind.Pickup;
        if (n.Contains("truck")) return CarKind.Truck;
        if (n.Contains("bus")) return CarKind.Bus;
        if (n.Contains("van")) return CarKind.Van;
        if (n.Contains("suv") || n.Contains("jeep")) return CarKind.Suv;
        if (n.Contains("sport") || n.Contains("super")) return CarKind.Sport;
        return CarKind.Sedan;
    }
}
