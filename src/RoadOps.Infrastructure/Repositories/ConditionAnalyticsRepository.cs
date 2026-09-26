using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

/// <summary>
/// Aggregate queries for the analytics/MCP layer. Each method translates to GROUP BY / COUNT / AVG SQL; only the
/// aggregated rows (a few per section, band or action) are materialised.
/// </summary>
public class ConditionAnalyticsRepository : IConditionAnalyticsRepository
{
    private const string None = DistressCatalog.None;
    private const int PoorDegree = ConditionRules.PoorDegree;
    private const string PoorRide = ConditionRules.PoorRide;
    private const string VeryPoorRide = ConditionRules.VeryPoorRide;

    private readonly RoadOpsDbContext _context;

    public ConditionAnalyticsRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<WorkspaceOverview>> FindWorkspacesAsync(WorkspaceSearch search, CancellationToken cancellationToken = default)
    {
        var query = _context.Workspaces.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search.Corridor))
        {
            query = query.Where(w => w.Corridor == search.Corridor);
        }

        if (search.SurveyYear is { } year)
        {
            query = query.Where(w => w.SurveyYear == year);
        }

        if (search.Status is { } status)
        {
            query = query.Where(w => w.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search.NameContains))
        {
            var pattern = "%" + EscapeLike(search.NameContains.Trim()) + "%";
            query = query.Where(w => EF.Functions.ILike(w.Name, pattern, "\\"));
        }

        // Correlated sub-queries: one round trip, counted by the database.
        return await query
            .OrderByDescending(w => w.SurveyYear)
            .ThenBy(w => w.Corridor)
            .ThenBy(w => w.Name)
            .Take(search.Limit)
            .Select(w => new WorkspaceOverview(
                w.Id,
                w.Name,
                w.AssessmentType,
                w.Corridor,
                w.SurveyYear,
                w.Status,
                _context.RoadSections.Count(s => s.WorkspaceId == w.Id),
                _context.PavedRoadRecords.Count(r => r.WorkspaceId == w.Id),
                _context.RoadSections.Where(s => s.WorkspaceId == w.Id).Min(s => (double?)s.ChainageFrom),
                _context.RoadSections.Where(s => s.WorkspaceId == w.Id).Max(s => (double?)s.ChainageTo)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SectionOverview>> GetSectionOverviewsAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var sections = await _context.RoadSections
            .AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .OrderBy(s => s.ChainageFrom)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var stats = await _context.PavedRoadRecords
            .Where(r => r.WorkspaceId == workspaceId)
            .GroupBy(r => r.SectionId)
            .Select(g => new
            {
                SectionId = g.Key,
                Count = g.Count(),
                Distressed = g.Sum(r => r.DistressType != None ? 1 : 0),
                Poor = g.Sum(r => r.Degree >= PoorDegree ? 1 : 0),
                AvgDegree = g.Average(r => (double)r.Degree),
            })
            .ToDictionaryAsync(x => x.SectionId, cancellationToken);

        return sections.Select(s => stats.TryGetValue(s.Id, out var st)
                ? new SectionOverview(s.Id, s.SectionName, s.ChainageFrom, s.ChainageTo, st.Count, st.Distressed, st.Poor, st.AvgDegree)
                : new SectionOverview(s.Id, s.SectionName, s.ChainageFrom, s.ChainageTo, 0, 0, 0, null))
            .ToList();
    }

    public async Task<ConditionAggregate> GetConditionAggregateAsync(ConditionScope scope, int topDistresses, CancellationToken cancellationToken = default)
    {
        var records = InScope(scope);

        var totals = await records
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Distressed = g.Sum(r => r.DistressType != None ? 1 : 0),
                Poor = g.Sum(r => r.Degree >= PoorDegree ? 1 : 0),
                AvgRut = g.Average(r => r.RutDepthMm),
                MaxRut = g.Max(r => r.RutDepthMm),
                From = g.Min(r => r.ChainageFrom),
                To = g.Max(r => r.ChainageTo),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null)
        {
            return new ConditionAggregate(0, 0, 0, EmptyDegreeCounts(), [], new Dictionary<string, int>(), null, null, null, null);
        }

        var degreeCounts = EmptyDegreeCounts();
        foreach (var row in await records.GroupBy(r => r.Degree).Select(g => new { Degree = g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
        {
            degreeCounts[row.Degree] = row.Count;
        }

        var distresses = await records
            .Where(r => r.DistressType != None)
            .GroupBy(r => r.DistressType)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(topDistresses)
            .Select(g => new DistressCount(g.Key, g.Count(), g.Average(r => (double)r.Degree)))
            .ToListAsync(cancellationToken);

        var ridingQuality = await records
            .Where(r => r.RidingQuality != "")
            .GroupBy(r => r.RidingQuality)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        return new ConditionAggregate(totals.Count, totals.Distressed, totals.Poor, degreeCounts, distresses, ridingQuality,
            totals.AvgRut, totals.MaxRut, totals.From, totals.To);
    }

    public async Task<IReadOnlyList<KmBandAggregate>> GetKmBandAggregatesAsync(ConditionScope scope, double bandKm, CancellationToken cancellationToken = default)
    {
        return await InScope(scope)
            .GroupBy(r => (int)Math.Floor(r.ChainageFrom / bandKm))
            .OrderBy(g => g.Key)
            .Select(g => new KmBandAggregate(
                g.Key,
                g.Count(),
                g.Sum(r => r.DistressType != None ? 1 : 0),
                g.Sum(r => r.Degree >= PoorDegree ? 1 : 0),
                g.Sum(r => r.RidingQuality == PoorRide || r.RidingQuality == VeryPoorRide ? 1 : 0),
                g.Average(r => (double)r.Degree),
                g.Max(r => r.Degree),
                g.Average(r => (double)(r.Degree * r.Extent)),
                g.Average(r => r.RutDepthMm),
                g.Max(r => r.RutDepthMm)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KmBandDistressAggregate>> GetKmBandDistressAggregatesAsync(ConditionScope scope, double bandKm, CancellationToken cancellationToken = default)
    {
        return await InScope(scope)
            .Where(r => r.DistressType != None)
            .GroupBy(r => new { Band = (int)Math.Floor(r.ChainageFrom / bandKm), r.DistressType })
            .Select(g => new KmBandDistressAggregate(
                g.Key.Band,
                g.Key.DistressType,
                g.Count(),
                g.Average(r => (double)r.Degree),
                g.Average(r => (double)r.Extent)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RepairActionAggregate>> GetRepairActionAggregatesAsync(ConditionScope scope, CancellationToken cancellationToken = default)
    {
        return await InScope(scope)
            .Where(r => r.RecommendedAction != "" && r.RecommendedAction != RecommendedActionRules.NoAction)
            .GroupBy(r => r.RecommendedAction)
            .Select(g => new RepairActionAggregate(
                g.Key,
                g.Count(),
                g.Sum(r => r.ChainageTo - r.ChainageFrom),
                g.Max(r => r.Degree),
                g.Min(r => r.ChainageFrom),
                g.Max(r => r.ChainageTo)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PavedRoadRecord>> GetRecordsByActionAsync(ConditionScope scope, string recommendedAction, int limit, CancellationToken cancellationToken = default)
    {
        return await InScope(scope)
            .Where(r => r.RecommendedAction == recommendedAction)
            .OrderByDescending(r => r.Degree)
            .ThenByDescending(r => r.Extent)
            .ThenBy(r => r.ChainageFrom)
            .ThenBy(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PavedRoadRecord>> GetRecordsAtChainageAsync(string workspaceId, double km, double toleranceKm, int limit, CancellationToken cancellationToken = default)
    {
        return await _context.PavedRoadRecords
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId &&
                        r.ChainageFrom <= km + toleranceKm &&
                        r.ChainageTo >= km - toleranceKm)
            .OrderBy(r => Math.Abs((r.ChainageFrom + r.ChainageTo) / 2 - km))
            .ThenByDescending(r => r.Degree)
            .ThenBy(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NearbyRecord>> FindNearestRecordsAsync(GeoQuery query, CancellationToken cancellationToken = default)
    {
        // A bounding box (served by the latitude/longitude index) narrows the candidates; the database orders them by
        // squared distance on a local flat projection, and exact distances are computed for the few rows returned.
        var latDelta = query.RadiusM / Geo.MetresPerDegreeLatitude;
        var lonScale = Geo.MetresPerDegreeLongitude(query.Latitude) / Geo.MetresPerDegreeLatitude;
        var lonDelta = latDelta / lonScale;
        var lonScaleSquared = lonScale * lonScale;
        var (lat, lon) = (query.Latitude, query.Longitude);

        var records = _context.PavedRoadRecords.AsNoTracking()
            .Where(r => r.Latitude >= lat - latDelta && r.Latitude <= lat + latDelta &&
                        r.Longitude >= lon - lonDelta && r.Longitude <= lon + lonDelta);

        if (!string.IsNullOrWhiteSpace(query.WorkspaceId))
        {
            records = records.Where(r => r.WorkspaceId == query.WorkspaceId);
        }

        var rows = await records
            .Join(_context.Workspaces, r => r.WorkspaceId, w => w.Id, (r, w) => new { r, w.Corridor, w.SurveyYear })
            .Where(x => query.Corridor == null || x.Corridor == query.Corridor)
            .OrderBy(x => (x.r.Latitude - lat) * (x.r.Latitude - lat) + (x.r.Longitude - lon) * (x.r.Longitude - lon) * lonScaleSquared)
            .ThenBy(x => x.r.Id)
            .Take(query.Limit)
            .Select(x => new
            {
                x.r.Id, x.r.WorkspaceId, x.r.SectionId, x.Corridor, x.SurveyYear,
                x.r.ChainageFrom, x.r.ChainageTo, x.r.Latitude, x.r.Longitude,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new NearbyRecord(x.Id, x.WorkspaceId, x.SectionId, x.Corridor, x.SurveyYear, x.ChainageFrom, x.ChainageTo,
                x.Latitude, x.Longitude, Geo.DistanceM(lat, lon, x.Latitude, x.Longitude)))
            .Where(x => x.DistanceM <= query.RadiusM)
            .OrderBy(x => x.DistanceM)
            .ToList();
    }

    public async Task<RoadSection?> FindSectionAtChainageAsync(string workspaceId, double km, CancellationToken cancellationToken = default)
    {
        var tolerance = FieldRules.ChainageTolerance;
        return await _context.RoadSections
            .AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId && s.ChainageFrom <= km + tolerance && s.ChainageTo >= km - tolerance)
            .OrderByDescending(s => s.ChainageFrom)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Records of the scope: one workspace, optionally one section, with ChainageFrom in [FromKm, ToKm).</summary>
    private IQueryable<PavedRoadRecord> InScope(ConditionScope scope)
    {
        var query = _context.PavedRoadRecords.AsNoTracking().Where(r => r.WorkspaceId == scope.WorkspaceId);

        if (!string.IsNullOrWhiteSpace(scope.SectionId))
        {
            query = query.Where(r => r.SectionId == scope.SectionId);
        }

        if (scope.FromKm is { } from)
        {
            query = query.Where(r => r.ChainageFrom >= from);
        }

        if (scope.ToKm is { } to)
        {
            query = query.Where(r => r.ChainageFrom < to);
        }

        return query;
    }

    private static Dictionary<int, int> EmptyDegreeCounts() =>
        Enumerable.Range(FieldRules.MinDegree, FieldRules.MaxDegree - FieldRules.MinDegree + 1).ToDictionary(d => d, _ => 0);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
