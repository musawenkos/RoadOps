using RoadOps.Application.Analytics;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

/// <summary>Read-only aggregate queries over survey data. All grouping and counting happens in the database.</summary>
public interface IConditionAnalyticsRepository
{
    /// <summary>Workspaces matching the search with section/record counts, newest survey year first.</summary>
    Task<IReadOnlyList<WorkspaceOverview>> FindWorkspacesAsync(WorkspaceSearch search, CancellationToken cancellationToken = default);

    /// <summary>All sections of a workspace ordered by chainage, with record counts.</summary>
    Task<IReadOnlyList<SectionOverview>> GetSectionOverviewsAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<ConditionAggregate> GetConditionAggregateAsync(ConditionScope scope, int topDistresses, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KmBandAggregate>> GetKmBandAggregatesAsync(ConditionScope scope, double bandKm, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KmBandDistressAggregate>> GetKmBandDistressAggregatesAsync(ConditionScope scope, double bandKm, CancellationToken cancellationToken = default);

    /// <summary>Records grouped by recommended action ("No action required" excluded).</summary>
    Task<IReadOnlyList<RepairActionAggregate>> GetRepairActionAggregatesAsync(ConditionScope scope, CancellationToken cancellationToken = default);

    /// <summary>The worst records (highest degree, then extent) with the given recommended action.</summary>
    Task<IReadOnlyList<PavedRoadRecord>> GetRecordsByActionAsync(ConditionScope scope, string recommendedAction, int limit, CancellationToken cancellationToken = default);

    /// <summary>Records of a workspace whose km range covers <paramref name="km"/> ± <paramref name="toleranceKm"/>, closest first.</summary>
    Task<IReadOnlyList<PavedRoadRecord>> GetRecordsAtChainageAsync(string workspaceId, double km, double toleranceKm, int limit, CancellationToken cancellationToken = default);

    /// <summary>Records within <see cref="GeoQuery.RadiusM"/> of a GPS point, nearest first.</summary>
    Task<IReadOnlyList<NearbyRecord>> FindNearestRecordsAsync(GeoQuery query, CancellationToken cancellationToken = default);

    /// <summary>The section of a workspace containing <paramref name="km"/>. At a shared boundary the section starting there wins.</summary>
    Task<RoadSection?> FindSectionAtChainageAsync(string workspaceId, double km, CancellationToken cancellationToken = default);
}
