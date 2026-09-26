using RoadOps.Domain.Enum;

namespace RoadOps.Application.Analytics;

// Query inputs and SQL-side aggregate rows used by IConditionAnalyticsRepository.
// Every aggregate is computed by the database (GROUP BY); records are never paged into memory to be counted.

/// <summary>Filters for finding survey workspaces. Null fields are not filtered on.</summary>
public sealed record WorkspaceSearch(string? Corridor = null, int? SurveyYear = null, string? NameContains = null, WorkspaceStatus? Status = null, int Limit = 20);

/// <summary>The records a condition query looks at: one workspace, optionally one section and/or a km range [FromKm, ToKm).</summary>
public sealed record ConditionScope(string WorkspaceId, string? SectionId = null, double? FromKm = null, double? ToKm = null);

/// <summary>A GPS search around a point, limited to one workspace or one corridor.</summary>
public sealed record GeoQuery(double Latitude, double Longitude, double RadiusM, string? WorkspaceId = null, string? Corridor = null, int Limit = 20);

public sealed record WorkspaceOverview(
    string Id,
    string Name,
    string AssessmentType,
    string Corridor,
    int SurveyYear,
    WorkspaceStatus Status,
    int SectionCount,
    int RecordCount,
    double? FromKm,
    double? ToKm);

public sealed record SectionOverview(
    string Id,
    string SectionName,
    double ChainageFrom,
    double ChainageTo,
    int RecordCount,
    int DistressedCount,
    int PoorCount,
    double? AvgDegree);

public sealed record DistressCount(string DistressType, int Count, double AvgDegree);

public sealed record ConditionAggregate(
    int RecordCount,
    int DistressedCount,
    int PoorCount,
    IReadOnlyDictionary<int, int> DegreeCounts,
    IReadOnlyList<DistressCount> TopDistresses,
    IReadOnlyDictionary<string, int> RidingQualityCounts,
    double? AvgRutDepthMm,
    double? MaxRutDepthMm,
    double? FromKm,
    double? ToKm);

/// <summary>Aggregates for one km band: records whose ChainageFrom falls in [BandIndex × bandKm, (BandIndex + 1) × bandKm).</summary>
public sealed record KmBandAggregate(
    int BandIndex,
    int RecordCount,
    int DistressedCount,
    int PoorCount,
    int PoorRideCount,
    double AvgDegree,
    int MaxDegree,
    double AvgSeverity,
    double AvgRutDepthMm,
    double MaxRutDepthMm);

/// <summary>Per band and distress type, used to find each band's dominant distress.</summary>
public sealed record KmBandDistressAggregate(int BandIndex, string DistressType, int Count, double AvgDegree, double AvgExtent);

public sealed record RepairActionAggregate(string RecommendedAction, int Count, double TotalLengthKm, int MaxDegree, double FromKm, double ToKm);

/// <summary>An observation near a GPS point, with its survey's corridor and year and the straight-line distance.</summary>
public sealed record NearbyRecord(
    string Id,
    string WorkspaceId,
    string SectionId,
    string Corridor,
    int SurveyYear,
    double ChainageFrom,
    double ChainageTo,
    double Latitude,
    double Longitude,
    double DistanceM);
