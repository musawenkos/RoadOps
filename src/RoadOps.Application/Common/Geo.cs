namespace RoadOps.Application.Common;

/// <summary>Small-distance geodesy helpers (WGS84 latitude/longitude in degrees).</summary>
public static class Geo
{
    private const double EarthRadiusM = 6_371_008.8;

    /// <summary>Metres per degree of latitude (close enough everywhere for local searches).</summary>
    public const double MetresPerDegreeLatitude = 111_320;

    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Metres per degree of longitude at a latitude, floored so boxes near the poles stay finite.</summary>
    public static double MetresPerDegreeLongitude(double latitude) =>
        MetresPerDegreeLatitude * Math.Max(Math.Cos(ToRadians(latitude)), 0.01);

    /// <summary>
    /// Where P falls along the segment A→B, as a fraction (0 at A, 1 at B), using a local flat projection.
    /// Values outside 0..1 mean P lies before A or beyond B.
    /// </summary>
    public static double ProjectOntoSegment(double pLat, double pLon, double aLat, double aLon, double bLat, double bLon)
    {
        var mPerLon = MetresPerDegreeLongitude(aLat);
        var (abx, aby) = ((bLon - aLon) * mPerLon, (bLat - aLat) * MetresPerDegreeLatitude);
        var (apx, apy) = ((pLon - aLon) * mPerLon, (pLat - aLat) * MetresPerDegreeLatitude);
        var lengthSquared = abx * abx + aby * aby;
        return lengthSquared == 0 ? 0 : (apx * abx + apy * aby) / lengthSquared;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
