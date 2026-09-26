using Moq;
using RoadOps.Application.Analytics;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

public class ConditionAnalyticsServiceTests
{
    private static readonly WorkspaceOverview N1Of2024 = Survey("ws24", "N1 · VCI 2024", "N1", 2024, WorkspaceStatus.Archive);
    private static readonly WorkspaceOverview N1Of2026 = Survey("ws26", "N1 · VCI 2026", "N1", 2026, WorkspaceStatus.Active);

    private readonly Mock<IConditionAnalyticsRepository> _repository = new();
    private readonly ConditionAnalyticsService _service;

    public ConditionAnalyticsServiceTests()
    {
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1" && s.SurveyYear == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync([N1Of2026, N1Of2024]);
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1" && s.SurveyYear == 2024), It.IsAny<CancellationToken>()))
            .ReturnsAsync([N1Of2024]);
        _repository.Setup(r => r.GetSectionOverviewsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SectionOverview("s1", "N1 S01: km 0.0–10.0", 0, 10, 100, 60, 10, 1.5),
                new SectionOverview("s2", "N1 S02: km 10.0–20.0 (Hammanskraal)", 10, 20, 100, 70, 20, 2.1),
            ]);
        _service = new ConditionAnalyticsService(_repository.Object, new SurveyResolver(_repository.Object));
    }

    private static WorkspaceOverview Survey(string id, string name, string corridor, int year, WorkspaceStatus status) =>
        new(id, name, "VCI", corridor, year, status, 2, 200, 0, 20);

    private static KmBandAggregate Band(int index, double avgDegree, double severity = 4, double rut = 8, int records = 10, int poorRide = 0, int poor = 0) =>
        new(index, records, records, poor, poorRide, avgDegree, (int)Math.Ceiling(avgDegree), severity, rut, rut + 5);

    [Fact]
    public async Task ResolveSurvey_CorridorOnly_PicksTheLatestYear()
    {
        var summary = await ResolveAsync(new SurveyReference("n1"));

        Assert.Equal("ws26", summary.Id);
    }

    [Fact]
    public async Task ResolveSurvey_NoMatch_ExplainsWhatWasSearched()
    {
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N9"), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ResolveAsync(new SurveyReference("N9", 2030)));

        Assert.Equal("No survey matches corridor N9, year 2030.", ex.Message.Split(" (")[0]);
    }

    [Fact]
    public async Task ResolveSurvey_TwoActiveSurveysInOneYear_IsAmbiguous()
    {
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N4"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Survey("a", "N4 east", "N4", 2026, WorkspaceStatus.Active), Survey("b", "N4 west", "N4", 2026, WorkspaceStatus.Active)]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ResolveAsync(new SurveyReference("N4")));

        Assert.Contains("N4 east; N4 west", ex.Message);
    }

    [Fact]
    public async Task ResolveSurvey_NothingGiven_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAsync(new SurveyReference()));
    }

    private Task<WorkspaceOverview> ResolveAsync(SurveyReference reference) =>
        new SurveyResolver(_repository.Object).ResolveSurveyAsync(reference);

    [Fact]
    public async Task GetConditionSummary_SectionByPartialName_ScopesTheQueryAndComputesPercentages()
    {
        ConditionScope? scope = null;
        _repository.Setup(r => r.GetConditionAggregateAsync(It.IsAny<ConditionScope>(), ConditionAnalyticsService.TopDistresses, It.IsAny<CancellationToken>()))
            .Callback<ConditionScope, int, CancellationToken>((s, _, _) => scope = s)
            .ReturnsAsync(new ConditionAggregate(200, 150, 50,
                new Dictionary<int, int> { [0] = 50, [1] = 0, [2] = 50, [3] = 50, [4] = 30, [5] = 20 },
                [new DistressCount("Potholes", 40, 3.5)],
                new Dictionary<string, int> { ["Good"] = 150, ["Poor"] = 50 },
                9.456, 30, 10, 20));

        var summary = await _service.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference("N1"), "hammanskraal"));

        Assert.Equal(new ConditionScope("ws26", "s2"), scope);
        Assert.Equal("s2", summary.Scope.Section!.Id);
        Assert.Equal(75, summary.PercentDistressed);
        Assert.Equal(25, summary.PercentPoor);
        Assert.Equal(75, summary.RidingQualityPercent["Good"]);
        Assert.Equal(9.46, summary.AvgRutDepthMm);
    }

    [Fact]
    public async Task GetConditionSummary_AmbiguousSectionName_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference("N1"), "N1 S0")));

        Assert.Contains("matches 2 sections", ex.Message);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    public async Task GetConditionSummary_InvalidKmRange_Throws(double from, double to)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference("N1"), FromKm: from, ToKm: to)));
    }

    [Fact]
    public async Task FindWorstStretches_RanksBandsAndAppliesTheRulesToTheDominantDistress()
    {
        _repository.Setup(r => r.GetKmBandAggregatesAsync(It.IsAny<ConditionScope>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Band(3, 2, severity: 5), Band(12, 4, severity: 16), Band(15, 3, severity: 9), Band(18, 0, severity: 0)]);
        _repository.Setup(r => r.GetKmBandDistressAggregatesAsync(It.IsAny<ConditionScope>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new KmBandDistressAggregate(12, "Potholes", 6, 4.2, 3.1),
                new KmBandDistressAggregate(12, "Rutting", 2, 3, 3),
                new KmBandDistressAggregate(15, "Rutting", 5, 3, 3),
            ]);

        var result = await _service.FindWorstStretchesAsync(new ScopeRequest(new SurveyReference("N1")), top: 3);

        Assert.Equal([12.0, 15.0, 3.0], result.Stretches.Select(s => s.FromKm));
        var worst = result.Stretches[0];
        Assert.Equal(1, worst.Rank);
        Assert.Equal(13, worst.ToKm);
        Assert.Equal("Potholes", worst.DominantDistress);
        Assert.Equal(RecommendedActionRules.UrgentPotholeRepair, worst.RecommendedAction);
        Assert.Equal("N1 S02: km 10.0–20.0 (Hammanskraal)", worst.SectionName);
        Assert.Equal(RecommendedActionRules.RutFill, result.Stretches[1].RecommendedAction);
        Assert.Equal(RecommendedActionRules.NoAction, result.Stretches[2].RecommendedAction);
    }

    [Fact]
    public async Task FindWorstStretches_ByRidingQuality_UsesPercentPoorRide()
    {
        _repository.Setup(r => r.GetKmBandAggregatesAsync(It.IsAny<ConditionScope>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Band(0, 1, records: 10, poorRide: 2), Band(1, 1, records: 10, poorRide: 8)]);
        _repository.Setup(r => r.GetKmBandDistressAggregatesAsync(It.IsAny<ConditionScope>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.FindWorstStretchesAsync(new ScopeRequest(new SurveyReference("N1")), StretchMetric.RidingQuality, bandKm: 2, top: 1);

        var stretch = Assert.Single(result.Stretches);
        Assert.Equal(2, stretch.FromKm);
        Assert.Equal(80, stretch.MetricValue);
    }

    [Theory]
    [InlineData(0.05, 5)]
    [InlineData(51, 5)]
    [InlineData(1, 0)]
    [InlineData(1, 21)]
    public async Task FindWorstStretches_OutOfRangeArguments_Throw(double bandKm, int top)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FindWorstStretchesAsync(new ScopeRequest(new SurveyReference("N1")), bandKm: bandKm, top: top));
    }

    [Fact]
    public async Task CompareSurveys_WithoutBaseline_UsesThePreviousSurveyAndClassifiesEachBand()
    {
        _repository.Setup(r => r.GetKmBandAggregatesAsync(It.Is<ConditionScope>(s => s.WorkspaceId == "ws24"), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Band(0, 1.0), Band(1, 3.5), Band(2, 2.0), Band(3, 2.5)]);
        _repository.Setup(r => r.GetKmBandAggregatesAsync(It.Is<ConditionScope>(s => s.WorkspaceId == "ws26"), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Band(0, 2.0), Band(1, 0.5), Band(2, 2.2), Band(4, 1.0)]);

        var result = await _service.CompareSurveysAsync(new SurveyReference("N1"));

        Assert.Equal("ws24", result.Baseline.Id);
        Assert.Equal("ws26", result.Current.Id);
        Assert.Equal(
            [ConditionTrend.Deteriorated, ConditionTrend.Rehabilitated, ConditionTrend.Stable, ConditionTrend.NotComparable, ConditionTrend.NotComparable],
            result.Bands.Select(b => b.Trend));
        Assert.Equal(1, result.TrendCounts[ConditionTrend.Deteriorated]);
        Assert.Equal(2, result.TrendCounts[ConditionTrend.NotComparable]);
        Assert.Equal(1.0, result.Bands[0].Change);
        Assert.Equal(5, result.Bands[1].FromKm);
        Assert.Equal(2.25, result.BaselineAvgDegree);
    }

    [Fact]
    public async Task CompareSurveys_NamedInReverseOrder_StillComparesOlderToNewer()
    {
        _repository.Setup(r => r.GetKmBandAggregatesAsync(It.IsAny<ConditionScope>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await _service.CompareSurveysAsync(new SurveyReference("N1", 2024), new SurveyReference("N1"));

        Assert.Equal(2024, result.Baseline.SurveyYear);
        Assert.Equal(2026, result.Current.SurveyYear);
    }

    [Fact]
    public async Task CompareSurveys_DifferentCorridors_Throws()
    {
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N4"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Survey("n4", "N4 · VCI 2024", "N4", 2024, WorkspaceStatus.Archive)]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CompareSurveysAsync(new SurveyReference("N1"), new SurveyReference("N4")));

        Assert.Contains("same corridor", ex.Message);
    }

    [Fact]
    public async Task CompareSurveys_NoEarlierSurvey_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CompareSurveysAsync(new SurveyReference("N1", 2024)));

        Assert.Contains("no earlier N1 survey", ex.Message);
    }

    [Fact]
    public async Task GetRepairBacklog_OrdersUrgentFirstAndListsUrgentLocations()
    {
        _repository.Setup(r => r.GetRepairActionAggregatesAsync(It.IsAny<ConditionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RepairActionAggregate(RecommendedActionRules.CrackSealing, 120, 12, 3, 0, 20),
                new RepairActionAggregate(RecommendedActionRules.Patching, 30, 3, 4, 2, 18),
                new RepairActionAggregate(RecommendedActionRules.UrgentPotholeRepair, 7, 0.7, 5, 11, 16),
                new RepairActionAggregate(RecommendedActionRules.Rehabilitation, 9, 0.9, 5, 12, 13),
            ]);
        _repository.Setup(r => r.GetRecordsByActionAsync(It.IsAny<ConditionScope>(), RecommendedActionRules.UrgentPotholeRepair,
                ConditionAnalyticsService.UrgentLocationCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PavedRoadRecord { ChainageFrom = 12.3, ChainageTo = 12.4, DistressType = "Potholes", Degree = 5, Extent = 3 }]);

        var backlog = await _service.GetRepairBacklogAsync(new ScopeRequest(new SurveyReference("N1")));

        Assert.Equal(
            [RecommendedActionRules.UrgentPotholeRepair, RecommendedActionRules.Rehabilitation, RecommendedActionRules.Patching, RecommendedActionRules.CrackSealing],
            backlog.Items.Select(i => i.RecommendedAction));
        Assert.Equal(ActionPriority.Urgent, backlog.Items[0].Priority);
        Assert.Equal(12.3, Assert.Single(backlog.UrgentLocations).ChainageFrom);
    }

    [Fact]
    public async Task GetRepairBacklog_NoUrgentWork_DoesNotQueryLocations()
    {
        _repository.Setup(r => r.GetRepairActionAggregatesAsync(It.IsAny<ConditionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RepairActionAggregate(RecommendedActionRules.CrackSealing, 3, 0.3, 3, 0, 1)]);

        var backlog = await _service.GetRepairBacklogAsync(new ScopeRequest(new SurveyReference("N1")));

        Assert.Empty(backlog.UrgentLocations);
        _repository.Verify(r => r.GetRecordsByActionAsync(It.IsAny<ConditionScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
