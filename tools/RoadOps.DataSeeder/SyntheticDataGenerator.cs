using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.DataSeeder;

public sealed record SurveyCampaign(int Year, DateTimeOffset StartDate, WorkspaceStatus Status);

public sealed class GeneratedDataset
{
    public List<Workspace> Workspaces { get; } = [];
    public List<RoadSection> Sections { get; } = [];
    public List<PavedRoadRecord> Records { get; } = [];
}

/// <summary>
/// Generates plausible TMH9-style visual condition assessment data.
///
/// Each route gets a latent "condition" profile along its chainage (0 = excellent, 1 = failed):
/// a mean-reverting random walk plus localised hotspots. Distress type, degree, extent, rut depth,
/// riding quality, skid resistance and recommended action are all derived from that profile, so the
/// data is internally consistent (bad stretches have potholes, deep ruts and poor ride quality).
/// Two survey campaigns are produced per route; the later one shows deterioration, except where a
/// stretch was rehabilitated in between.
/// </summary>
public sealed class SyntheticDataGenerator(int seed, double segmentKm)
{
    public static readonly SurveyCampaign[] Campaigns =
    [
        new(2024, new DateTimeOffset(2024, 4, 8, 0, 0, 0, TimeSpan.Zero), WorkspaceStatus.Archive),
        new(2026, new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero), WorkspaceStatus.Active),
    ];

    private const double KmSurveyedPerDay = 25;

    public GeneratedDataset Generate()
    {
        var dataset = new GeneratedDataset();

        for (var r = 0; r < RouteCatalog.Routes.Count; r++)
        {
            var route = RouteCatalog.Routes[r];
            var random = new Random(seed + r * 7919);
            var segments = (int)Math.Round(route.LengthKm / segmentKm);
            var baseline = BuildConditionProfile(route, segments, random);
            var surfaces = BuildSurfaceProfile(route, segments, random);
            var current = Deteriorate(baseline, random);

            foreach (var campaign in Campaigns)
            {
                var profile = campaign.Year == Campaigns[0].Year ? baseline : current;
                var status = campaign.Status;
                var coverage = 1.0;

                // One current survey was halted part-way (e.g. access/safety issue) to give realistic partial data.
                if (route.Code == "R61" && campaign.Year == Campaigns[^1].Year)
                {
                    status = WorkspaceStatus.Suspended;
                    coverage = 0.6;
                }

                GenerateCampaign(dataset, route, campaign, status, profile, surfaces, (int)(segments * coverage), random);
            }
        }

        return dataset;
    }

    private void GenerateCampaign(GeneratedDataset dataset, RouteDefinition route, SurveyCampaign campaign, WorkspaceStatus status,
        double[] condition, SurfaceType[] surfaces, int segmentCount, Random random)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{route.Name} · VCI {campaign.Year}",
            AssessmentType = "Visual Condition Assessment (TMH9)",
            Status = status,
            CreatedBy = RouteCatalog.Inspectors[random.Next(RouteCatalog.Inspectors.Length)],
            CreatedAt = campaign.StartDate.AddDays(-7).AddHours(9),
        };
        workspace.UpdatedAt = status == WorkspaceStatus.Active ? workspace.CreatedAt : campaign.StartDate.AddDays(route.LengthKm / KmSurveyedPerDay + 21);
        dataset.Workspaces.Add(workspace);

        RoadSection? section = null;
        var sectionEndKm = 0.0;
        var sectionNumber = 0;

        for (var i = 0; i < segmentCount; i++)
        {
            var fromKm = Math.Round(i * segmentKm, 3);
            var toKm = Math.Round(Math.Min((i + 1) * segmentKm, route.LengthKm), 3);
            var surveyedAt = SurveyTime(campaign.StartDate, fromKm, random);
            var inspector = RouteCatalog.Inspectors[(int)(fromKm / 20 + campaign.Year) % RouteCatalog.Inspectors.Length];

            if (section is null || fromKm >= sectionEndKm)
            {
                sectionNumber++;
                sectionEndKm = Math.Min(fromKm + random.Next(10, 26), route.LengthKm);
                section = new RoadSection
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = workspace.Id,
                    SectionName = $"{route.Code} S{sectionNumber:00}: km {fromKm:F1}–{sectionEndKm:F1} ({RouteCatalog.NearestPlace(route, fromKm)})",
                    CreatedBy = inspector,
                    CreatedAt = surveyedAt.AddMinutes(-15),
                    UpdatedAt = surveyedAt.AddMinutes(-15),
                };
                dataset.Sections.Add(section);
            }

            var position = RouteCatalog.PositionAt(route, (fromKm + toKm) / 2 / route.LengthKm);
            var cond = condition[i];

            dataset.Records.Add(BuildRecord(workspace, section, route, campaign, surfaces[i], cond, fromKm, toKm, position, surveyedAt, inspector, random, primary: true));

            // Distressed segments often carry a second, independent distress observation.
            if (random.NextDouble() < cond * 0.6)
            {
                dataset.Records.Add(BuildRecord(workspace, section, route, campaign, surfaces[i], cond, fromKm, toKm, position, surveyedAt.AddSeconds(40), inspector, random, primary: false));
            }
        }
    }

    private static PavedRoadRecord BuildRecord(Workspace workspace, RoadSection section, RouteDefinition route, SurveyCampaign campaign,
        SurfaceType surface, double cond, double fromKm, double toKm, GeoPoint position, DateTimeOffset surveyedAt, string inspector,
        Random random, bool primary)
    {
        var noDistress = primary && random.NextDouble() < Math.Pow(1 - cond, 2.5) * 0.85;
        var distress = noDistress ? "None" : PickDistress(surface, cond, random);
        var degree = noDistress ? 0 : Scale(0.6 + cond * 4.2 + Gaussian(random) * 0.6);
        var extent = noDistress ? 0 : Scale(0.8 + cond * 3.5 + Gaussian(random) * 0.8);

        var rutDepth = surface == SurfaceType.Concrete
            ? 1 + random.NextDouble() * 2
            : 2 + cond * 20 + Gaussian(random) * 1.8 + (distress == "Rutting" ? 6 + degree * 2 : 0);

        var iri = 1.2 + cond * 6.5 + Gaussian(random) * 0.45 + (surface == SurfaceType.Concrete ? 0.4 : 0);

        var record = new PavedRoadRecord
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = workspace.Id,
            SectionId = section.Id,
            ChainageFrom = fromKm,
            ChainageTo = toKm,
            SurfaceType = surface,
            DistressType = distress,
            Degree = degree,
            Extent = extent,
            RutDepthMm = Math.Round(Math.Clamp(rutDepth, 0.5, 60), 1),
            RidingQuality = RidingQualityBand(iri),
            SkidResistance = SkidResistance(distress, cond, random),
            StdRef = surface == SurfaceType.Concrete ? "TMH9 Part C (Rigid)" : "TMH9 Part B (Flexible)",
            RecommendedAction = RecommendAction(distress, degree, extent),
            Latitude = Math.Round(position.Latitude + Gaussian(random) * 0.00004, 6),
            Longitude = Math.Round(position.Longitude + Gaussian(random) * 0.00004, 6),
            ImagePaths = degree >= 3
                ? Enumerable.Range(1, degree >= 4 ? 2 : 1)
                    .Select(n => $"surveys/{campaign.Year}/{route.Code}/km{fromKm:000.0}_{Slug(distress)}_{n}.jpg").ToArray()
                : [],
            CreatedBy = inspector,
            CreatedAt = surveyedAt,
            UpdatedAt = surveyedAt,
        };

        // ~8% of records were corrected during office QA a few days after the survey.
        if (random.NextDouble() < 0.08)
        {
            record.UpdatedAt = surveyedAt.AddDays(random.Next(2, 10)).AddHours(random.Next(1, 6));
        }

        return record;
    }

    private static double[] BuildConditionProfile(RouteDefinition route, int segments, Random random)
    {
        var profile = new double[segments];
        var level = route.BaseCondition;
        for (var i = 0; i < segments; i++)
        {
            // Mean-reverting random walk keeps neighbouring segments similar.
            level += (route.BaseCondition - level) * 0.03 + Gaussian(random) * 0.02;
            profile[i] = level;
        }

        // Localised hotspots: failed drainage, heavy-vehicle climbing lanes, poor subgrade...
        var hotspots = (int)(route.LengthKm / 50) + random.Next(1, 3);
        for (var h = 0; h < hotspots; h++)
        {
            var centre = random.Next(segments);
            var width = random.Next(5, 25);
            var height = 0.25 + random.NextDouble() * 0.35;
            for (var i = Math.Max(0, centre - width * 3); i < Math.Min(segments, centre + width * 3); i++)
            {
                profile[i] += height * Math.Exp(-Math.Pow((i - centre) / (double)width, 2));
            }
        }

        return profile.Select(c => Math.Clamp(c, 0.02, 0.98)).ToArray();
    }

    private static double[] Deteriorate(double[] baseline, Random random)
    {
        var current = baseline.Select(c => Math.Clamp(c + 0.04 + c * 0.12 + random.NextDouble() * 0.04, 0.02, 0.98)).ToArray();

        // One stretch was rehabilitated between surveys, so it is now in excellent condition.
        var worst = Array.IndexOf(baseline, baseline.Max());
        var length = Math.Min(random.Next(40, 120), current.Length);
        var start = Math.Clamp(worst - length / 2, 0, current.Length - length);
        for (var i = start; i < start + length; i++)
        {
            current[i] = 0.05 + random.NextDouble() * 0.05;
        }

        return current;
    }

    private static SurfaceType[] BuildSurfaceProfile(RouteDefinition route, int segments, Random random)
    {
        var surfaces = new SurfaceType[segments];
        var i = 0;
        while (i < segments)
        {
            var roll = random.NextDouble();
            var type = roll < route.ConcreteShare ? SurfaceType.Concrete
                : roll < route.ConcreteShare + route.SealShare ? SurfaceType.SurfaceSeal
                : route.PrimarySurface;
            var run = random.Next(20, 150); // surface type changes in runs of 2-15 km
            for (var j = i; j < Math.Min(segments, i + run); j++)
            {
                surfaces[j] = type;
            }
            i += run;
        }
        return surfaces;
    }

    private static readonly string[][] FlexibleDistress =
    [
        ["Surface cracks", "Aggregate loss", "Binder condition", "Longitudinal cracking", "Bleeding"],
        ["Block cracking", "Transverse cracking", "Patching", "Rutting", "Edge breaking", "Undulation"],
        ["Crocodile cracking", "Potholes", "Pumping", "Surface failure", "Rutting"],
    ];

    private static readonly string[][] ConcreteDistress =
    [
        ["Joint seal damage", "Shrinkage cracks"],
        ["Transverse cracking", "Joint spalling", "Faulting"],
        ["Corner breaks", "Punchouts", "Pumping"],
    ];

    private static string PickDistress(SurfaceType surface, double cond, Random random)
    {
        var tiers = surface == SurfaceType.Concrete ? ConcreteDistress : FlexibleDistress;
        var tier = Math.Clamp((int)((cond + Gaussian(random) * 0.15) * 3), 0, 2);
        var options = tiers[tier];
        return options[random.Next(options.Length)];
    }

    private static string RecommendAction(string distress, int degree, int extent)
    {
        if (distress == "None")
        {
            return "No action required";
        }

        var score = degree * extent;
        if (score <= 4)
        {
            return "Routine maintenance – monitor";
        }

        var severe = score > 12;
        return distress switch
        {
            "Potholes" => degree >= 3 ? "Pothole repair – urgent (within 72h)" : "Pothole repair",
            "Crocodile cracking" or "Surface failure" or "Pumping" when severe => "Rehabilitation – base repair and resurfacing",
            "Crocodile cracking" or "Surface failure" or "Pumping" or "Patching" => "Patching",
            "Surface cracks" or "Aggregate loss" or "Binder condition" => severe ? "Reseal" : "Fog spray / rejuvenator",
            "Bleeding" => severe ? "Reseal" : "Apply grit",
            "Rutting" or "Undulation" => severe ? "Mill and replace" : "Rut fill / levelling",
            "Edge breaking" => "Edge repair and shoulder maintenance",
            "Block cracking" or "Transverse cracking" or "Longitudinal cracking" or "Shrinkage cracks" => severe ? "Asphalt overlay" : "Crack sealing",
            "Joint seal damage" => "Joint resealing",
            "Joint spalling" => "Partial-depth repair",
            "Corner breaks" or "Punchouts" => "Full-depth slab repair",
            "Faulting" => "Diamond grinding and load transfer restoration",
            _ => "Routine maintenance – monitor",
        };
    }

    private static string RidingQualityBand(double iri) => iri switch
    {
        < 2.0 => "Very good",
        < 3.0 => "Good",
        < 4.5 => "Fair",
        < 6.0 => "Poor",
        _ => "Very poor",
    };

    private static string SkidResistance(string distress, double cond, Random random)
    {
        if (distress == "Bleeding")
        {
            return "Poor";
        }

        var score = cond + (distress == "Aggregate loss" ? 0.2 : 0) + Gaussian(random) * 0.15;
        return score < 0.4 ? "Good" : score < 0.7 ? "Fair" : "Poor";
    }

    /// <summary>Survey crews move ~25 km/day on weekdays, 07:30–16:30 local (UTC+2).</summary>
    private static DateTimeOffset SurveyTime(DateTimeOffset start, double km, Random random)
    {
        var workDay = (int)(km / KmSurveyedPerDay);
        var date = start;
        for (var d = 0; d < workDay; d++)
        {
            do
            {
                date = date.AddDays(1);
            } while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }

        var dayFraction = km % KmSurveyedPerDay / KmSurveyedPerDay;
        var localMinutes = 7.5 * 60 + dayFraction * 9 * 60 + random.Next(0, 3);
        return date.AddMinutes(localMinutes).AddHours(-2); // store as UTC
    }

    private static int Scale(double value) => Math.Clamp((int)Math.Round(value), 1, 5);

    private static string Slug(string text) => text.ToLowerInvariant().Replace(' ', '-');

    private static double Gaussian(Random random)
    {
        // Box–Muller transform.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
