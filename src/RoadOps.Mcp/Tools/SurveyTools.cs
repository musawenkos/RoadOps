using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RoadOps.Application.Analytics;
using RoadOps.Application.Rules;
using RoadOps.Application.Services;
using RoadOps.Domain.Enum;
using static RoadOps.Mcp.Tools.Speech;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Read-only survey tools. Surveys are named by corridor/year (or name), sections by name, places by km or GPS; the
/// agent never needs an id. Results are short plain-text summaries meant to be spoken aloud.
/// </summary>
[McpServerToolType]
public sealed class SurveyTools(ConditionAnalyticsService analytics, SurveyLocationService location)
{
    private const string CorridorDescription = "Road corridor code, e.g. \"N1\", \"N4\", \"R21\".";
    private const string YearDescription = "Survey year, e.g. 2026. Omit for the latest survey of the corridor.";
    private const string SurveyNameDescription = "Part of the survey name, only needed when corridor and year are ambiguous.";
    private const string SectionDescription = "Optional section, by (part of) its name, e.g. \"S03\" or a town name.";

    [McpServerTool(Name = "find_surveys", Title = "Find surveys", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Finds condition surveys by corridor, year, name and/or status, newest first, with section and observation counts. Use it to see which surveys exist before comparing or summarising.")]
    public Task<string> FindSurveys(
        [Description(CorridorDescription)] string? corridor = null,
        [Description("Survey year, e.g. 2026.")] int? year = null,
        [Description("Part of the survey name, e.g. \"Polokwane\".")] string? name = null,
        [Description("Filter by status: active, suspended or archive.")] string? status = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var surveys = await analytics.FindSurveysAsync(new SurveyReference(corridor, year, name), ParseStatus(status), cancellationToken: cancellationToken);
            if (surveys.Count == 0)
            {
                return "No surveys match.";
            }

            var text = new StringBuilder($"{surveys.Count} survey(s):");
            foreach (var s in surveys)
            {
                text.Append($"\n- {SurveyLabel(s)}: {s.SectionCount} sections, {s.RecordCount} observations");
                if (s.FromKm is { } from && s.ToKm is { } to) text.Append($", {KmRange(from, to)}");
                text.Append('.');
            }

            return text.ToString();
        });

    [McpServerTool(Name = "list_sections", Title = "List survey sections", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the sections of a survey in chainage order with their km ranges, observation counts, average degree and share of poor observations.")]
    public Task<string> ListSections(
        [Description(CorridorDescription)] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        [Description(SurveyNameDescription)] string? survey = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var (resolved, sections) = await analytics.ListSectionsAsync(new SurveyReference(corridor, year, survey), cancellationToken);
            var text = new StringBuilder($"{resolved.Name} has {sections.Count} sections:");
            foreach (var s in sections)
            {
                text.Append($"\n- {s.SectionName}: {KmRange(s.ChainageFrom, s.ChainageTo)}, {s.RecordCount} observations");
                if (s.AvgDegree is { } avg && s.RecordCount > 0)
                {
                    text.Append($", average degree {Num(avg, "0.0")}, {Pct(100.0 * s.PoorCount / s.RecordCount)} poor");
                }

                text.Append('.');
            }

            return text.ToString();
        });

    [McpServerTool(Name = "get_condition_summary", Title = "Condition summary", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Summarises road condition for a survey, optionally one section and/or a km range: how many observations, share distressed and poor (degree 4-5), degree distribution, top distresses, rut depth and riding quality mix.")]
    public Task<string> GetConditionSummary(
        [Description(CorridorDescription)] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        [Description(SurveyNameDescription)] string? survey = null,
        [Description(SectionDescription)] string? section = null,
        [Description("Start of a km range (inclusive).")] double? fromKm = null,
        [Description("End of a km range (exclusive).")] double? toKm = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var s = await analytics.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference(corridor, year, survey), section, fromKm, toKm), cancellationToken);
            if (s.RecordCount == 0)
            {
                return $"There are no observations for {ScopeLabel(s.Scope)}.";
            }

            var text = new StringBuilder($"{ScopeLabel(s.Scope)}: {s.RecordCount} observations");
            if (s.FromKm is { } from && s.ToKm is { } to) text.Append($" covering {KmRange(from, to)}");
            text.Append($". {Pct(s.PercentDistressed)} show distress and {Pct(s.PercentPoor)} are poor (degree 4 or 5).");
            text.Append(" Degrees: ").Append(string.Join(", ", s.DegreeCounts.Where(d => d.Value > 0).Select(d => $"{d.Key}: {d.Value}"))).Append('.');
            if (s.TopDistresses.Count > 0)
            {
                text.Append(" Top distresses: ")
                    .Append(string.Join("; ", s.TopDistresses.Select(d => $"{d.DistressType} {d.Count} (average degree {Num(d.AvgDegree, "0.0")})")))
                    .Append('.');
            }

            if (s.AvgRutDepthMm is { } avgRut) text.Append($" Average rut depth {Num(avgRut)} mm, deepest {Num(s.MaxRutDepthMm ?? 0)} mm.");
            if (s.RidingQualityPercent.Count > 0)
            {
                text.Append(" Riding quality: ").Append(string.Join(", ", s.RidingQualityPercent.Select(r => $"{r.Key} {Pct(r.Value)}"))).Append('.');
            }

            return text.ToString();
        });

    [McpServerTool(Name = "find_worst_stretches", Title = "Worst stretches", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Ranks the worst km stretches of a survey by severity (degree × extent), rutting (rut depth) or riding_quality (share of poor ride), with each stretch's dominant distress and the recommended action.")]
    public Task<string> FindWorstStretches(
        [Description(CorridorDescription)] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        [Description(SurveyNameDescription)] string? survey = null,
        [Description(SectionDescription)] string? section = null,
        [Description("Start of a km range to search.")] double? fromKm = null,
        [Description("End of a km range to search.")] double? toKm = null,
        [Description("severity (default), rutting or riding_quality.")] string metric = "severity",
        [Description("Stretch length in km (0.1-50, default 1).")] double bandKm = 1,
        [Description("How many stretches to return (1-20, default 5).")] int top = 5,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var parsed = ParseMetric(metric);
            var result = await analytics.FindWorstStretchesAsync(
                new ScopeRequest(new SurveyReference(corridor, year, survey), section, fromKm, toKm), parsed, bandKm, top, cancellationToken);
            if (result.Stretches.Count == 0)
            {
                return $"No distressed stretches found for {ScopeLabel(result.Scope)}.";
            }

            var unit = parsed switch
            {
                StretchMetric.Rutting => " mm average rut",
                StretchMetric.RidingQuality => "% poor ride",
                _ => " average severity",
            };
            var text = new StringBuilder($"Worst {result.Stretches.Count} stretches of {ScopeLabel(result.Scope)} by {metric.Replace('_', ' ')}:");
            foreach (var s in result.Stretches)
            {
                text.Append($"\n{s.Rank}. {KmRange(s.FromKm, s.ToKm)}");
                if (s.SectionName is not null) text.Append($" ({s.SectionName})");
                text.Append($": {Num(s.MetricValue, "0.#")}{unit}, max degree {s.MaxDegree}");
                if (s.DominantDistress is not null) text.Append($", mostly {s.DominantDistress}");
                text.Append($". Recommended: {s.RecommendedAction}.");
            }

            return text.ToString();
        });

    [McpServerTool(Name = "compare_surveys", Title = "Compare surveys", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Compares two surveys of the same corridor per km band and classifies each band as deteriorated, stable or rehabilitated (average degree change of at least 0.5). By default compares the latest survey with the one before it.")]
    public Task<string> CompareSurveys(
        [Description(CorridorDescription)] string? corridor = null,
        [Description("Year of the newer survey. Omit for the latest.")] int? year = null,
        [Description("Year of the older survey to compare against. Omit for the survey before the newer one.")] int? baselineYear = null,
        [Description("Band length in km (0.1-50, default 5).")] double bandKm = 5,
        [Description("Start of a km range to compare.")] double? fromKm = null,
        [Description("End of a km range to compare.")] double? toKm = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var baseline = baselineYear is null ? null : new SurveyReference(corridor, baselineYear);
            var c = await analytics.CompareSurveysAsync(new SurveyReference(corridor, year), baseline, bandKm, fromKm, toKm, cancellationToken);

            var text = new StringBuilder($"{c.Current.Corridor} {c.Baseline.SurveyYear} vs {c.Current.SurveyYear}, {Num(c.BandKm, "0.##")} km bands: ");
            text.Append($"{c.TrendCounts[ConditionTrend.Deteriorated]} deteriorated, {c.TrendCounts[ConditionTrend.Stable]} stable, " +
                        $"{c.TrendCounts[ConditionTrend.Rehabilitated]} rehabilitated");
            if (c.TrendCounts[ConditionTrend.NotComparable] > 0) text.Append($", {c.TrendCounts[ConditionTrend.NotComparable]} surveyed only once");
            text.Append('.');
            if (c.BaselineAvgDegree is { } before && c.CurrentAvgDegree is { } after)
            {
                text.Append($" Average degree went from {Num(before, "0.00")} to {Num(after, "0.00")}.");
            }

            AppendBands(text, "Most deteriorated", c.Bands.Where(b => b.Trend == ConditionTrend.Deteriorated).OrderByDescending(b => b.Change));
            AppendBands(text, "Rehabilitated", c.Bands.Where(b => b.Trend == ConditionTrend.Rehabilitated).OrderBy(b => b.Change));
            return text.ToString();
        });

    [McpServerTool(Name = "get_location_details", Title = "Observations at a location", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the observations at a km on a survey, or at a GPS position: distress, degree, extent, measurements, recommended action, photos and GPS.")]
    public Task<string> GetLocationDetails(
        [Description(CorridorDescription + " Required with km; optional with GPS.")] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        [Description("Chainage in km.")] double? km = null,
        [Description("GPS latitude in decimal degrees.")] double? latitude = null,
        [Description("GPS longitude in decimal degrees.")] double? longitude = null,
        [Description("Maximum observations to return (1-20, default 5).")] int limit = 5,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var reference = corridor is null && year is null ? null : new SurveyReference(corridor, year);
            var d = await location.GetLocationDetailsAsync(reference, km, latitude, longitude, limit, cancellationToken);

            var text = new StringBuilder($"{d.Survey.Name} at {Km(d.ChainageKm)}");
            if (d.Section is { } section) text.Append($", section {section.SectionName}");
            if (d.Fix is { } fix) text.Append($" ({Num(fix.DistanceFromRoadM, "0")} m from the nearest surveyed point)");
            text.Append(d.Observations.Count == 0 ? ": no observations here." : $": {d.Observations.Count} observation(s).");
            foreach (var o in d.Observations)
            {
                text.Append("\n- ").Append(Observation(o));
            }

            return text.ToString();
        });

    [McpServerTool(Name = "get_repair_backlog", Title = "Repair backlog", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Groups a survey's recommended repair actions and counts them, most urgent first (e.g. potholes to repair within 72 hours), with the locations of the urgent ones.")]
    public Task<string> GetRepairBacklog(
        [Description(CorridorDescription)] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        [Description(SurveyNameDescription)] string? survey = null,
        [Description(SectionDescription)] string? section = null,
        [Description("Start of a km range.")] double? fromKm = null,
        [Description("End of a km range.")] double? toKm = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var b = await analytics.GetRepairBacklogAsync(new ScopeRequest(new SurveyReference(corridor, year, survey), section, fromKm, toKm), cancellationToken);
            if (b.Items.Count == 0)
            {
                return $"No repairs are recommended for {ScopeLabel(b.Scope)}.";
            }

            var text = new StringBuilder($"Repair backlog for {ScopeLabel(b.Scope)}, most urgent first:");
            foreach (var i in b.Items.Take(10))
            {
                text.Append($"\n- [{i.Priority.ToString().ToLowerInvariant()}] {i.RecommendedAction}: {i.Count} location(s), {KmRange(i.FromKm, i.ToKm)}.");
            }

            if (b.Items.Count > 10) text.Append($"\n…and {b.Items.Count - 10} more action types.");
            if (b.UrgentLocations.Count > 0)
            {
                text.Append("\nUrgent locations: ")
                    .Append(string.Join("; ", b.UrgentLocations.Select(l => $"{Km(l.ChainageFrom)} {l.DistressType} degree {l.Degree}")))
                    .Append('.');
            }

            return text.ToString();
        });

    [McpServerTool(Name = "locate_position", Title = "Where am I?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Works out which survey, section and km a GPS position is at. Uses the latest active survey of the nearest corridor unless a corridor is given.")]
    public Task<string> LocatePosition(
        [Description("GPS latitude in decimal degrees.")] double latitude,
        [Description("GPS longitude in decimal degrees.")] double longitude,
        [Description(CorridorDescription + " Optional.")] string? corridor = null,
        [Description(YearDescription)] int? year = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var reference = corridor is null && year is null ? null : new SurveyReference(corridor, year);
            var fix = await location.LocateAsync(latitude, longitude, reference, cancellationToken);
            var section = fix.Section is { } s ? $"section {s.SectionName}" : "outside every section of this survey";
            return $"You are at {Km(fix.ChainageKm)} on {fix.Survey.Corridor}, {section}, survey {SurveyLabel(fix.Survey)}. " +
                   $"Nearest surveyed point is {Num(fix.DistanceFromRoadM, "0")} m away; surface {fix.SurfaceType}.";
        });

    [McpServerTool(Name = "list_distress_types", Title = "Distress types", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the TMH9 distress types the server accepts, for flexible (asphalt, seal) and rigid (concrete) roads. Map what the inspector says (e.g. \"a crack\") to one of these names.")]
    public string ListDistressTypes() =>
        $"Flexible (asphalt and surface seal): {string.Join(", ", DistressCatalog.Flexible)}.\n" +
        $"Rigid (concrete): {string.Join(", ", DistressCatalog.Rigid)}.\n" +
        "Degree (severity) and extent (how much of the segment is affected) are each rated 1 to 5.";

    private static void AppendBands(StringBuilder text, string label, IEnumerable<BandComparison> bands)
    {
        var top = bands.Take(3).ToList();
        if (top.Count == 0)
        {
            return;
        }

        text.Append($" {label}: ")
            .Append(string.Join("; ", top.Select(b =>
                $"{KmRange(b.FromKm, b.ToKm)} degree {Num(b.BaselineAvgDegree ?? 0, "0.0")} to {Num(b.CurrentAvgDegree ?? 0, "0.0")}")))
            .Append('.');
    }

    private static WorkspaceStatus? ParseStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "active" => WorkspaceStatus.Active,
        "suspended" => WorkspaceStatus.Suspended,
        "archive" or "archived" => WorkspaceStatus.Archive,
        _ => throw new ArgumentException("Status must be active, suspended or archive."),
    };

    private static StretchMetric ParseMetric(string metric) => metric.Trim().ToLowerInvariant() switch
    {
        "severity" => StretchMetric.Severity,
        "rutting" => StretchMetric.Rutting,
        "riding_quality" or "riding quality" or "ride" => StretchMetric.RidingQuality,
        _ => throw new ArgumentException("Metric must be severity, rutting or riding_quality."),
    };
}
