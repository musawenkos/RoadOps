using RoadOps.Application.DTOs;
using RoadOps.Application.Rules;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Analytics;

/// <summary>Identifies a survey by corridor, year and/or name instead of by id, e.g. (Corridor: "N1", SurveyYear: 2026).</summary>
public sealed record SurveyReference(string? Corridor = null, int? SurveyYear = null, string? Name = null);

/// <summary>What to summarise: a survey, optionally narrowed to one section (by name) and/or a km range.</summary>
public sealed record ScopeRequest(SurveyReference Survey, string? SectionName = null, double? FromKm = null, double? ToKm = null);

public enum StretchMetric
{
    /// <summary>Average degree × extent.</summary>
    Severity,
    /// <summary>Average rut depth.</summary>
    Rutting,
    /// <summary>Share of observations with poor or very poor riding quality.</summary>
    RidingQuality
}

public sealed record SectionRef(string Id, string SectionName, double ChainageFrom, double ChainageTo);

/// <summary>The resolved survey (and optional section / km range) a result describes.</summary>
public sealed record ResolvedScope(WorkspaceOverview Survey, SectionRef? Section, double? FromKm, double? ToKm)
{
    public ConditionScope ToConditionScope() => new(Survey.Id, Section?.Id, FromKm, ToKm);
}

public sealed record ConditionSummary(
    ResolvedScope Scope,
    int RecordCount,
    int DistressedCount,
    double PercentDistressed,
    double PercentPoor,
    IReadOnlyDictionary<int, int> DegreeCounts,
    IReadOnlyList<DistressCount> TopDistresses,
    IReadOnlyDictionary<string, double> RidingQualityPercent,
    double? AvgRutDepthMm,
    double? MaxRutDepthMm,
    double? FromKm,
    double? ToKm);

public sealed record WorstStretch(
    int Rank,
    double FromKm,
    double ToKm,
    string? SectionName,
    double MetricValue,
    int RecordCount,
    double AvgDegree,
    int MaxDegree,
    double AvgRutDepthMm,
    double PercentPoorRide,
    string? DominantDistress,
    string RecommendedAction);

public sealed record WorstStretches(ResolvedScope Scope, StretchMetric Metric, double BandKm, IReadOnlyList<WorstStretch> Stretches);

public sealed record BandComparison(
    double FromKm,
    double ToKm,
    double? BaselineAvgDegree,
    double? CurrentAvgDegree,
    double? BaselinePercentPoor,
    double? CurrentPercentPoor,
    ConditionTrend Trend)
{
    public double? Change => CurrentAvgDegree - BaselineAvgDegree;
}

public sealed record SurveyComparison(
    WorkspaceOverview Baseline,
    WorkspaceOverview Current,
    double BandKm,
    IReadOnlyDictionary<ConditionTrend, int> TrendCounts,
    IReadOnlyList<BandComparison> Bands,
    double? BaselineAvgDegree,
    double? CurrentAvgDegree);

public sealed record BacklogItem(
    string RecommendedAction,
    ActionPriority Priority,
    int Count,
    double TotalLengthKm,
    int MaxDegree,
    double FromKm,
    double ToKm);

public sealed record BacklogLocation(double ChainageFrom, double ChainageTo, string DistressType, int Degree, int Extent, double Latitude, double Longitude);

public sealed record RepairBacklog(ResolvedScope Scope, IReadOnlyList<BacklogItem> Items, IReadOnlyList<BacklogLocation> UrgentLocations);

/// <summary>The survey, section and km a GPS position resolves to.</summary>
public sealed record LocationFix(
    WorkspaceOverview Survey,
    SectionRef? Section,
    double ChainageKm,
    double DistanceFromRoadM,
    int ReferenceSurveyYear,
    SurfaceType SurfaceType);

public sealed record LocationDetails(WorkspaceOverview Survey, SectionRef? Section, double ChainageKm, LocationFix? Fix, IReadOnlyList<PavedRoadRecordDto> Observations);
