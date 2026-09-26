using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Services;

/// <summary>
/// Condition analytics over survey data: summaries, worst stretches, survey comparison and repair backlog.
/// The database does the aggregation; this service validates input, resolves names and applies the shared rules.
/// </summary>
public class ConditionAnalyticsService
{
    public const int TopDistresses = 5;
    public const double MinBandKm = 0.1;
    public const double MaxBandKm = 50;
    public const int MaxStretches = 20;
    public const int UrgentLocationCount = 5;

    private readonly IConditionAnalyticsRepository _analytics;
    private readonly SurveyResolver _resolver;

    public ConditionAnalyticsService(IConditionAnalyticsRepository analytics, SurveyResolver resolver)
    {
        _analytics = analytics;
        _resolver = resolver;
    }

    public Task<IReadOnlyList<WorkspaceOverview>> FindSurveysAsync(SurveyReference reference, WorkspaceStatus? status = null, int limit = SurveyResolver.MaxCandidates, CancellationToken cancellationToken = default) =>
        _resolver.FindSurveysAsync(reference, status, limit, cancellationToken);

    public async Task<(WorkspaceOverview Survey, IReadOnlyList<SectionOverview> Sections)> ListSectionsAsync(SurveyReference reference, CancellationToken cancellationToken = default)
    {
        var survey = await _resolver.ResolveSurveyAsync(reference, cancellationToken);
        return (survey, await _analytics.GetSectionOverviewsAsync(survey.Id, cancellationToken));
    }

    public async Task<ConditionSummary> GetConditionSummaryAsync(ScopeRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await _resolver.ResolveScopeAsync(request, cancellationToken);
        var aggregate = await _analytics.GetConditionAggregateAsync(scope.ToConditionScope(), TopDistresses, cancellationToken);

        var ridingTotal = aggregate.RidingQualityCounts.Values.Sum();
        var ridingPercent = aggregate.RidingQualityCounts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => Percent(kv.Value, ridingTotal));

        return new ConditionSummary(
            scope,
            aggregate.RecordCount,
            aggregate.DistressedCount,
            Percent(aggregate.DistressedCount, aggregate.RecordCount),
            Percent(aggregate.PoorCount, aggregate.RecordCount),
            aggregate.DegreeCounts,
            aggregate.TopDistresses,
            ridingPercent,
            Round(aggregate.AvgRutDepthMm),
            Round(aggregate.MaxRutDepthMm),
            aggregate.FromKm,
            aggregate.ToKm);
    }

    public async Task<WorstStretches> FindWorstStretchesAsync(ScopeRequest request, StretchMetric metric = StretchMetric.Severity, double bandKm = 1, int top = 5, CancellationToken cancellationToken = default)
    {
        RequireBand(bandKm);
        if (top is < 1 or > MaxStretches)
        {
            throw new ArgumentException($"Top must be between 1 and {MaxStretches}.", nameof(top));
        }

        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentException("Unknown metric.", nameof(metric));
        }

        var scope = await _resolver.ResolveScopeAsync(request, cancellationToken);
        var conditionScope = scope.ToConditionScope();
        var bands = await _analytics.GetKmBandAggregatesAsync(conditionScope, bandKm, cancellationToken);

        var worst = bands
            .Select(b => (Band: b, Value: MetricValue(b, metric)))
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .ThenByDescending(x => x.Band.MaxDegree)
            .ThenBy(x => x.Band.BandIndex)
            .Take(top)
            .ToList();

        if (worst.Count == 0)
        {
            return new WorstStretches(scope, metric, bandKm, []);
        }

        var distresses = (await _analytics.GetKmBandDistressAggregatesAsync(conditionScope, bandKm, cancellationToken))
            .ToLookup(d => d.BandIndex);
        var sections = await _analytics.GetSectionOverviewsAsync(scope.Survey.Id, cancellationToken);

        var stretches = worst.Select((x, i) =>
        {
            var from = Math.Round(x.Band.BandIndex * bandKm, 3);
            var to = Math.Round(from + bandKm, 3);
            var dominant = distresses[x.Band.BandIndex]
                .OrderByDescending(d => d.Count)
                .ThenByDescending(d => d.AvgDegree)
                .ThenBy(d => d.DistressType)
                .FirstOrDefault();
            var action = dominant is null
                ? RecommendedActionRules.NoAction
                : RecommendedActionRules.Recommend(dominant.DistressType, (int)Math.Round(dominant.AvgDegree), (int)Math.Round(dominant.AvgExtent));
            var section = sections.LastOrDefault(s => s.ChainageFrom <= from + FieldRules.ChainageTolerance && s.ChainageTo > from);

            return new WorstStretch(
                i + 1,
                from,
                to,
                section?.SectionName,
                Math.Round(x.Value, 2),
                x.Band.RecordCount,
                Math.Round(x.Band.AvgDegree, 2),
                x.Band.MaxDegree,
                Math.Round(x.Band.AvgRutDepthMm, 1),
                Percent(x.Band.PoorRideCount, x.Band.RecordCount),
                dominant?.DistressType,
                action);
        }).ToList();

        return new WorstStretches(scope, metric, bandKm, stretches);
    }

    /// <summary>
    /// Compares two surveys of the same corridor per km band. Without a baseline, the most recent earlier survey of the
    /// corridor is used.
    /// </summary>
    public async Task<SurveyComparison> CompareSurveysAsync(SurveyReference current, SurveyReference? baseline = null, double bandKm = 5, double? fromKm = null, double? toKm = null, CancellationToken cancellationToken = default)
    {
        RequireBand(bandKm);
        if (fromKm is { } f && toKm is { } t)
        {
            FieldRules.RequireChainageRange(f, t, allowEmpty: false, nameof(fromKm), nameof(toKm));
        }

        var currentSurvey = await _resolver.ResolveSurveyAsync(current, cancellationToken);
        var baselineSurvey = baseline is null
            ? await FindPreviousSurveyAsync(currentSurvey, cancellationToken)
            : await _resolver.ResolveSurveyAsync(baseline, cancellationToken);

        if (baselineSurvey.Id == currentSurvey.Id)
        {
            throw new ArgumentException("Pick two different surveys to compare.", nameof(baseline));
        }

        if (baselineSurvey.Corridor != currentSurvey.Corridor)
        {
            throw new ArgumentException(
                $"Only surveys of the same corridor can be compared ({baselineSurvey.Corridor} vs {currentSurvey.Corridor}).", nameof(baseline));
        }

        // Always compare older against newer, whichever order the caller named them in.
        if (baselineSurvey.SurveyYear > currentSurvey.SurveyYear)
        {
            (baselineSurvey, currentSurvey) = (currentSurvey, baselineSurvey);
        }

        var before = (await _analytics.GetKmBandAggregatesAsync(new ConditionScope(baselineSurvey.Id, null, fromKm, toKm), bandKm, cancellationToken))
            .ToDictionary(b => b.BandIndex);
        var after = (await _analytics.GetKmBandAggregatesAsync(new ConditionScope(currentSurvey.Id, null, fromKm, toKm), bandKm, cancellationToken))
            .ToDictionary(b => b.BandIndex);

        var bands = before.Keys.Union(after.Keys).Order().Select(index =>
        {
            before.TryGetValue(index, out var b);
            after.TryGetValue(index, out var a);
            var from = Math.Round(index * bandKm, 3);
            return new BandComparison(
                from,
                Math.Round(from + bandKm, 3),
                Round(b?.AvgDegree),
                Round(a?.AvgDegree),
                b is null ? null : Percent(b.PoorCount, b.RecordCount),
                a is null ? null : Percent(a.PoorCount, a.RecordCount),
                ConditionRules.Classify(b?.AvgDegree, a?.AvgDegree));
        }).ToList();

        var trendCounts = Enum.GetValues<ConditionTrend>().ToDictionary(trend => trend, trend => bands.Count(b => b.Trend == trend));

        return new SurveyComparison(baselineSurvey, currentSurvey, bandKm, trendCounts, bands,
            WeightedAverageDegree(before.Values), WeightedAverageDegree(after.Values));
    }

    public async Task<RepairBacklog> GetRepairBacklogAsync(ScopeRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await _resolver.ResolveScopeAsync(request, cancellationToken);
        var conditionScope = scope.ToConditionScope();

        var items = (await _analytics.GetRepairActionAggregatesAsync(conditionScope, cancellationToken))
            .Select(a => new BacklogItem(a.RecommendedAction, RecommendedActionRules.PriorityOf(a.RecommendedAction), a.Count,
                Math.Round(a.TotalLengthKm, 2), a.MaxDegree, a.FromKm, a.ToKm))
            .OrderBy(i => i.Priority)
            .ThenByDescending(i => i.Count)
            .ThenBy(i => i.RecommendedAction)
            .ToList();

        var urgentAction = items.FirstOrDefault(i => i.Priority == ActionPriority.Urgent)?.RecommendedAction;
        var urgentLocations = urgentAction is null
            ? []
            : (await _analytics.GetRecordsByActionAsync(conditionScope, urgentAction, UrgentLocationCount, cancellationToken))
                .Select(r => new BacklogLocation(r.ChainageFrom, r.ChainageTo, r.DistressType, r.Degree, r.Extent, r.Latitude, r.Longitude))
                .ToList();

        return new RepairBacklog(scope, items, urgentLocations);
    }

    private async Task<WorkspaceOverview> FindPreviousSurveyAsync(WorkspaceOverview current, CancellationToken cancellationToken)
    {
        var surveys = await _resolver.FindSurveysAsync(new SurveyReference(current.Corridor), cancellationToken: cancellationToken);
        return surveys
                   .Where(s => s.SurveyYear < current.SurveyYear)
                   .OrderByDescending(s => s.SurveyYear)
                   .ThenBy(s => s.Status == WorkspaceStatus.Active ? 0 : 1)
                   .FirstOrDefault()
               ?? throw new ArgumentException($"There is no earlier {current.Corridor} survey to compare '{current.Name}' with.", "baseline");
    }

    private static double MetricValue(KmBandAggregate band, StretchMetric metric) => metric switch
    {
        StretchMetric.Rutting => band.AvgRutDepthMm,
        StretchMetric.RidingQuality => Percent(band.PoorRideCount, band.RecordCount),
        _ => band.AvgSeverity,
    };

    private static double? WeightedAverageDegree(IEnumerable<KmBandAggregate> bands)
    {
        var list = bands.ToList();
        var count = list.Sum(b => b.RecordCount);
        return count == 0 ? null : Math.Round(list.Sum(b => b.AvgDegree * b.RecordCount) / count, 2);
    }

    private static void RequireBand(double bandKm) => FieldRules.RequireRange(bandKm, MinBandKm, MaxBandKm, nameof(bandKm));

    private static double Percent(int part, int total) => total == 0 ? 0 : Math.Round(100.0 * part / total, 1);

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 2) : null;
}
