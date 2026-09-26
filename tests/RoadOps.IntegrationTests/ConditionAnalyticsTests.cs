using Microsoft.Extensions.DependencyInjection;
using RoadOps.Application.Analytics;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;
using RoadOps.Infrastructure.Data;
using RoadOps.Tests.Shared;

namespace RoadOps.IntegrationTests;

/// <summary>
/// Runs the analytics services and their SQL aggregate queries against PostgreSQL.
///
/// Dataset (a unique corridor, so other tests' data never interferes): a straight 10 km road heading south from
/// (-25.0, 28.0) with one 100 m record per segment, sections S01 km 0–5 and S02 km 5–10, surveyed twice.
///   2024: km 0–5 "Surface cracks" degree 1 × extent 1; km 5–10 "Potholes" degree 4 × extent 4.
///   2026: km 0–5 "Rutting" degree 3 × extent 3, plus two degree-5 potholes at km 2.3 and 2.4; km 5–10 rehabilitated ("None").
/// </summary>
[Collection(ApiCollection.Name)]
public class ConditionAnalyticsTests(RoadOpsApiFactory factory)
{
    private const double DegreesPerKm = 1 / 111.32;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static string? _corridor;

    private async Task<string> CorridorAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            return _corridor ??= await SeedAsync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private async Task<T> WithServiceAsync<T, TService>(Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static (double Lat, double Lon) PositionAtKm(double km) => (-25.0 - km * DegreesPerKm, 28.0);

    private async Task<string> SeedAsync()
    {
        var corridor = ApiTestData.UniqueCorridor();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RoadOpsDbContext>();
        var now = DateTimeOffset.UtcNow;

        foreach (var year in new[] { 2024, 2026 })
        {
            var workspace = new Workspace
            {
                Id = Guid.NewGuid().ToString(), Name = $"{corridor} test road · VCI {year}", AssessmentType = "VCI", Corridor = corridor,
                SurveyYear = year, Status = year == 2026 ? WorkspaceStatus.Active : WorkspaceStatus.Archive, CreatedBy = "tests",
                CreatedAt = now, UpdatedAt = now,
            };
            var sections = new[] { (From: 0.0, To: 5.0, Name: "S01: km 0–5 (Start)"), (From: 5.0, To: 10.0, Name: "S02: km 5–10 (End)") }
                .Select(s => new RoadSection
                {
                    Id = Guid.NewGuid().ToString(), WorkspaceId = workspace.Id, SectionName = $"{corridor} {s.Name}",
                    ChainageFrom = s.From, ChainageTo = s.To, CreatedBy = "tests", CreatedAt = now, UpdatedAt = now,
                })
                .ToArray();
            db.Workspaces.Add(workspace);
            db.RoadSections.AddRange(sections);

            for (var i = 0; i < 100; i++)
            {
                var from = Math.Round(i * 0.1, 3);
                var section = sections[from < 5 ? 0 : 1];
                var (distress, degree, extent) = (year, from < 5) switch
                {
                    (2024, true) => ("Surface cracks", 1, 1),
                    (2024, false) => ("Potholes", 4, 4),
                    (_, true) => ("Rutting", 3, 3),
                    _ => (DistressCatalog.None, 0, 0),
                };
                db.PavedRoadRecords.Add(Record(workspace, section, from, distress, degree, extent, now));

                if (year == 2026 && i is 23 or 24)
                {
                    db.PavedRoadRecords.Add(Record(workspace, section, from, "Potholes", 5, 3, now));
                }
            }
        }

        await db.SaveChangesAsync();
        return corridor;
    }

    private static PavedRoadRecord Record(Workspace workspace, RoadSection section, double from, string distress, int degree, int extent, DateTimeOffset now)
    {
        var (lat, lon) = PositionAtKm(from + 0.05);
        return new PavedRoadRecord
        {
            Id = Guid.NewGuid().ToString(), WorkspaceId = workspace.Id, SectionId = section.Id,
            ChainageFrom = from, ChainageTo = Math.Round(from + 0.1, 3), SurfaceType = SurfaceType.Asphalt,
            DistressType = distress, Degree = degree, Extent = extent, RutDepthMm = 2 + degree * 3,
            RidingQuality = degree >= 3 ? "Poor" : "Good", SkidResistance = "Good", StdRef = "TMH9",
            RecommendedAction = RecommendedActionRules.Recommend(distress, degree, extent),
            Latitude = lat, Longitude = lon, ImagePaths = degree >= 4 ? ["photo.jpg"] : [],
            CreatedBy = "tests", CreatedAt = now, UpdatedAt = now,
        };
    }

    [Fact]
    public async Task FindSurveys_ByCorridor_ReturnsNewestFirstWithCounts()
    {
        var corridor = await CorridorAsync();

        var surveys = await WithServiceAsync((ConditionAnalyticsService s) => s.FindSurveysAsync(new SurveyReference(corridor.ToLowerInvariant())));

        Assert.Equal([2026, 2024], surveys.Select(s => s.SurveyYear));
        Assert.Equal(102, surveys[0].RecordCount);
        Assert.Equal(100, surveys[1].RecordCount);
        Assert.All(surveys, s => Assert.Equal(2, s.SectionCount));
        Assert.Equal(0, surveys[0].FromKm);
        Assert.Equal(10, surveys[0].ToKm);
    }

    [Fact]
    public async Task FindSurveys_NameFilter_TreatsLikeWildcardsLiterally()
    {
        await CorridorAsync();

        var surveys = await WithServiceAsync((ConditionAnalyticsService s) => s.FindSurveysAsync(new SurveyReference(Name: "%")));

        Assert.DoesNotContain(surveys, s => s.Corridor == _corridor);
    }

    [Fact]
    public async Task ListSections_ReturnsKmRangesAndCountsInChainageOrder()
    {
        var corridor = await CorridorAsync();

        var (survey, sections) = await WithServiceAsync((ConditionAnalyticsService s) => s.ListSectionsAsync(new SurveyReference(corridor, 2024)));

        Assert.Equal(2024, survey.SurveyYear);
        Assert.Equal([0.0, 5.0], sections.Select(s => s.ChainageFrom));
        Assert.Equal([50, 50], sections.Select(s => s.RecordCount));
        Assert.Equal([0, 50], sections.Select(s => s.PoorCount));
    }

    [Fact]
    public async Task ConditionSummary_WholeSurvey_AggregatesInTheDatabase()
    {
        var corridor = await CorridorAsync();

        var summary = await WithServiceAsync((ConditionAnalyticsService s) => s.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference(corridor))));

        Assert.Equal(2026, summary.Scope.Survey.SurveyYear);
        Assert.Equal(102, summary.RecordCount);
        Assert.Equal(52, summary.DistressedCount);
        Assert.Equal(50, summary.DegreeCounts[0]);
        Assert.Equal(50, summary.DegreeCounts[3]);
        Assert.Equal(2, summary.DegreeCounts[5]);
        Assert.Equal(2.0, summary.PercentPoor);
        Assert.Equal("Rutting", summary.TopDistresses[0].DistressType);
        Assert.Equal(51.0, summary.RidingQualityPercent["Poor"]);
        Assert.Equal(17, summary.MaxRutDepthMm);
    }

    [Fact]
    public async Task ConditionSummary_SectionAndKmRange_NarrowTheScope()
    {
        var corridor = await CorridorAsync();

        var section = await WithServiceAsync((ConditionAnalyticsService s) =>
            s.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference(corridor, 2024), SectionName: "End")));
        var range = await WithServiceAsync((ConditionAnalyticsService s) =>
            s.GetConditionSummaryAsync(new ScopeRequest(new SurveyReference(corridor), FromKm: 2, ToKm: 3)));

        Assert.Equal(50, section.RecordCount);
        Assert.Equal(100, section.PercentPoor);
        Assert.Equal(12, range.RecordCount);
        Assert.Equal(2, range.FromKm);
        Assert.Equal(3, range.ToKm);
    }

    [Fact]
    public async Task WorstStretches_RankKmBandsAndRecommendAction()
    {
        var corridor = await CorridorAsync();

        var result = await WithServiceAsync((ConditionAnalyticsService s) =>
            s.FindWorstStretchesAsync(new ScopeRequest(new SurveyReference(corridor)), StretchMetric.Severity, bandKm: 1, top: 3));

        var worst = result.Stretches[0];
        Assert.Equal((2.0, 3.0), (worst.FromKm, worst.ToKm));
        Assert.Equal(10, worst.MetricValue);
        Assert.Equal("Rutting", worst.DominantDistress);
        Assert.Equal(RecommendedActionRules.RutFill, worst.RecommendedAction);
        Assert.Equal(5, worst.MaxDegree);
        Assert.EndsWith("S01: km 0–5 (Start)", worst.SectionName);
        Assert.Equal(3, result.Stretches.Count);
        Assert.All(result.Stretches.Skip(1), s => Assert.True(s.ToKm <= 5));
    }

    [Fact]
    public async Task CompareSurveys_DetectsDeteriorationAndRehabilitationPerBand()
    {
        var corridor = await CorridorAsync();

        var result = await WithServiceAsync((ConditionAnalyticsService s) => s.CompareSurveysAsync(new SurveyReference(corridor), bandKm: 5));

        Assert.Equal(2024, result.Baseline.SurveyYear);
        Assert.Equal(2026, result.Current.SurveyYear);
        Assert.Equal([ConditionTrend.Deteriorated, ConditionTrend.Rehabilitated], result.Bands.Select(b => b.Trend));
        Assert.Equal(1, result.Bands[0].BaselineAvgDegree);
        Assert.Equal(3.08, result.Bands[0].CurrentAvgDegree);
        Assert.Equal(4, result.Bands[1].BaselineAvgDegree);
        Assert.Equal(0, result.Bands[1].CurrentAvgDegree);
    }

    [Fact]
    public async Task RepairBacklog_PutsUrgentPotholesFirstWithLocations()
    {
        var corridor = await CorridorAsync();

        var backlog = await WithServiceAsync((ConditionAnalyticsService s) => s.GetRepairBacklogAsync(new ScopeRequest(new SurveyReference(corridor))));

        Assert.Equal(RecommendedActionRules.UrgentPotholeRepair, backlog.Items[0].RecommendedAction);
        Assert.Equal(2, backlog.Items[0].Count);
        Assert.Equal(RecommendedActionRules.RutFill, backlog.Items[1].RecommendedAction);
        Assert.Equal(50, backlog.Items[1].Count);
        Assert.Equal(5, backlog.Items[1].TotalLengthKm);
        Assert.DoesNotContain(backlog.Items, i => i.RecommendedAction == RecommendedActionRules.NoAction);
        Assert.Equal([2.3, 2.4], backlog.UrgentLocations.Select(l => l.ChainageFrom).Order());
    }

    [Fact]
    public async Task Locate_GpsBesideTheRoad_ResolvesSurveySectionAndChainage()
    {
        var corridor = await CorridorAsync();
        var (lat, _) = PositionAtKm(7.25);

        // About 20 m east of the centre line.
        var fix = await WithServiceAsync((SurveyLocationService s) => s.LocateAsync(lat, 28.0002));

        Assert.Equal(corridor, fix.Survey.Corridor);
        Assert.Equal(2026, fix.Survey.SurveyYear);
        Assert.EndsWith("S02: km 5–10 (End)", fix.Section!.SectionName);
        Assert.Equal(7.25, fix.ChainageKm, 2);
        Assert.InRange(fix.DistanceFromRoadM, 15, 30);
    }

    [Fact]
    public async Task Locate_WrongCorridor_IsRejected()
    {
        var corridor = await CorridorAsync();
        var (lat, lon) = PositionAtKm(3);

        // The position is on the test corridor, but the caller named a different (seeded elsewhere) corridor.
        var otherCorridor = ApiTestData.UniqueCorridor();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RoadOpsDbContext>();
            db.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid().ToString(), Name = $"{otherCorridor} elsewhere", AssessmentType = "VCI", Corridor = otherCorridor,
                SurveyYear = 2026, CreatedBy = "tests", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            WithServiceAsync((SurveyLocationService s) => s.LocateAsync(lat, lon, new SurveyReference(otherCorridor))));

        Assert.Contains($"from the {otherCorridor} corridor", ex.Message);
        Assert.NotEqual(corridor, otherCorridor);
    }

    [Fact]
    public async Task Locate_FarFromAnyRoad_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync((SurveyLocationService s) => s.LocateAsync(10.0, 10.0)));
    }

    [Fact]
    public async Task LocationDetails_AtKm_ReturnsObservationsClosestFirst()
    {
        var corridor = await CorridorAsync();

        var details = await WithServiceAsync((SurveyLocationService s) => s.GetLocationDetailsAsync(new SurveyReference(corridor), 2.35, null, null));

        Assert.EndsWith("S01: km 0–5 (Start)", details.Section!.SectionName);
        Assert.Contains(details.Observations, o => o.DistressType == "Potholes" && o.ImagePaths.Length == 1);
        Assert.All(details.Observations, o => Assert.InRange(o.ChainageFrom, 2.2, 2.4));
        Assert.Equal(2.3, details.Observations[0].ChainageFrom);
    }

    [Fact]
    public async Task FindSectionAtChainage_AtASharedBoundary_ReturnsTheSectionStartingThere()
    {
        var corridor = await CorridorAsync();
        var survey = await WithServiceAsync((ConditionAnalyticsService s) => s.FindSurveysAsync(new SurveyReference(corridor, 2026)));

        var section = await WithServiceAsync((IConditionAnalyticsRepository r) => r.FindSectionAtChainageAsync(survey[0].Id, 5.0));
        var beyond = await WithServiceAsync((IConditionAnalyticsRepository r) => r.FindSectionAtChainageAsync(survey[0].Id, 10.5));

        Assert.Equal(5, section!.ChainageFrom);
        Assert.Null(beyond);
    }
}
