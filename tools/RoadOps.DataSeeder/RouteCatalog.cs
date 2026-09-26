using RoadOps.Domain.Enum;

namespace RoadOps.DataSeeder;

public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>A surveyed road corridor. Waypoints are approximate real coordinates along the route.</summary>
public sealed record RouteDefinition(
    string Code,
    string Name,
    double LengthKm,
    SurfaceType PrimarySurface,
    double SealShare,
    double ConcreteShare,
    double BaseCondition,
    IReadOnlyList<(double Km, string Place)> Landmarks,
    IReadOnlyList<GeoPoint> Waypoints);

public static class RouteCatalog
{
    public static readonly IReadOnlyList<RouteDefinition> Routes =
    [
        new("N1", "N1 Pretoria – Polokwane", 250, SurfaceType.Asphalt, 0.15, 0.00, 0.30,
            [(0, "Pretoria"), (45, "Hammanskraal"), (95, "Bela-Bela"), (150, "Modimolle"), (180, "Mokopane"), (250, "Polokwane")],
            [new(-25.7479, 28.2293), new(-25.3951, 28.2799), new(-24.8840, 28.2930), new(-24.7010, 28.4040), new(-24.1837, 29.0100), new(-23.9045, 29.4689)]),

        new("N4", "N4 Pretoria – Emalahleni", 100, SurfaceType.Asphalt, 0.05, 0.00, 0.25,
            [(0, "Pretoria East"), (35, "Bronkhorstspruit"), (70, "Balmoral"), (100, "Emalahleni")],
            [new(-25.7580, 28.3200), new(-25.8090, 28.7400), new(-25.8500, 29.0000), new(-25.8713, 29.2332)]),

        new("N3", "N3 Heidelberg – Harrismith", 220, SurfaceType.Asphalt, 0.05, 0.12, 0.35,
            [(0, "Heidelberg"), (60, "Villiers"), (140, "Warden"), (220, "Harrismith")],
            [new(-26.5041, 28.3587), new(-27.0300, 28.6000), new(-27.8500, 28.9600), new(-28.2744, 29.1294)]),

        new("R21", "R21 Pretoria – OR Tambo", 45, SurfaceType.Asphalt, 0.00, 0.00, 0.20,
            [(0, "Pretoria"), (20, "Irene"), (35, "Kempton Park"), (45, "OR Tambo")],
            [new(-25.7700, 28.2400), new(-25.8800, 28.2200), new(-26.0500, 28.2300), new(-26.1367, 28.2411)]),

        new("N2", "N2 Cape Town – Somerset West", 45, SurfaceType.Asphalt, 0.10, 0.00, 0.40,
            [(0, "Cape Town"), (15, "Airport"), (30, "Macassar"), (45, "Somerset West")],
            [new(-33.9249, 18.4241), new(-33.9700, 18.5900), new(-34.0300, 18.7300), new(-34.0784, 18.8431)]),

        new("R61", "R61 Mthatha – Port St Johns", 95, SurfaceType.SurfaceSeal, 0.70, 0.00, 0.55,
            [(0, "Mthatha"), (40, "Libode"), (95, "Port St Johns")],
            [new(-31.5889, 28.7844), new(-31.5400, 29.0300), new(-31.6200, 29.5400)]),
    ];

    public static readonly string[] Inspectors =
        ["t.mokoena", "l.naidoo", "j.vandermerwe", "s.dlamini", "p.botha", "n.khumalo", "a.pillay", "k.mahlangu"];

    /// <summary>Interpolates a position along the waypoint polyline at a fraction (0..1) of the route.</summary>
    public static GeoPoint PositionAt(RouteDefinition route, double fraction)
    {
        var points = route.Waypoints;
        var scaled = Math.Clamp(fraction, 0, 1) * (points.Count - 1);
        var i = Math.Min((int)scaled, points.Count - 2);
        var t = scaled - i;
        return new GeoPoint(
            points[i].Latitude + (points[i + 1].Latitude - points[i].Latitude) * t,
            points[i].Longitude + (points[i + 1].Longitude - points[i].Longitude) * t);
    }

    public static string NearestPlace(RouteDefinition route, double km) =>
        route.Landmarks.MinBy(l => Math.Abs(l.Km - km)).Place;
}
