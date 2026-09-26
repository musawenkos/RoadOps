using Moq;
using RoadOps.Application.Analytics;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

public class SurveyLocationServiceTests
{
    private static readonly WorkspaceOverview Active2026 = new("ws26", "N1 · VCI 2026", "VCI", "N1", 2026, WorkspaceStatus.Active, 1, 0, 0, 20);
    private static readonly WorkspaceOverview Archive2024 = new("ws24", "N1 · VCI 2024", "VCI", "N1", 2024, WorkspaceStatus.Archive, 1, 100, 0, 20);

    private readonly Mock<IConditionAnalyticsRepository> _repository = new();
    private readonly SurveyLocationService _service;

    public SurveyLocationServiceTests()
    {
        _repository.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Active2026, Archive2024]);
        _repository.Setup(r => r.FindSectionAtChainageAsync("ws26", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoadSection { Id = "s1", SectionName = "N1 S01", ChainageFrom = 0, ChainageTo = 20 });
        _service = new SurveyLocationService(_repository.Object, new SurveyResolver(_repository.Object));
    }

    // Two 2024 points along a north-south line: km 10.05 at -25.000 and km 10.15 at -25.001 (about 111 m apart).
    private static NearbyRecord Point(double lat, double km, double distance, string workspace = "ws24") =>
        new($"r{km}", workspace, "s24", "N1", 2024, km - 0.05, km + 0.05, lat, 28.0, distance);

    [Fact]
    public async Task Locate_WithoutSurvey_UsesNearestCorridorsActiveSurveyAndInterpolatesChainage()
    {
        _repository.Setup(r => r.FindNearestRecordsAsync(It.Is<GeoQuery>(q => q.Corridor == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(-25.0, 10.05, 30), Point(-25.001, 10.15, 80)]);

        var fix = await _service.LocateAsync(-25.0003, 28.0);

        Assert.Equal("ws26", fix.Survey.Id);
        Assert.Equal("s1", fix.Section!.Id);
        Assert.Equal(10.08, fix.ChainageKm, 2);
        Assert.Equal(2024, fix.ReferenceSurveyYear);
        Assert.Equal(30, fix.DistanceFromRoadM);
    }

    [Fact]
    public async Task Locate_WithSurvey_SearchesOnlyThatCorridor()
    {
        _repository.Setup(r => r.FindNearestRecordsAsync(It.Is<GeoQuery>(q => q.Corridor == "N1" && q.RadiusM == SurveyLocationService.MaxDistanceFromRoadM), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(-25.0, 10.05, 5)]);

        var fix = await _service.LocateAsync(-25.0, 28.0, new SurveyReference("N1", 2026));

        Assert.Equal(10.05, fix.ChainageKm);
    }

    [Fact]
    public async Task Locate_FarFromAnySurveyedRoad_Throws()
    {
        _repository.Setup(r => r.FindNearestRecordsAsync(It.IsAny<GeoQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.LocateAsync(-26, 28));

        Assert.Contains("300 m", ex.Message);
    }

    [Theory]
    [InlineData(-91, 28)]
    [InlineData(-25, 181)]
    [InlineData(double.NaN, 28)]
    public async Task Locate_InvalidCoordinates_Throws(double lat, double lon)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.LocateAsync(lat, lon));
    }

    [Fact]
    public void EstimateChainage_IgnoresPointsAtTheSameChainageAndClampsExtrapolation()
    {
        var sameKm = Point(-25.0004, 10.05, 40);
        var references = new[] { Point(-25.0, 10.05, 10), sameKm, Point(-25.001, 10.15, 100) };

        // Far "before" A: extrapolation is capped at one segment length.
        Assert.Equal(9.95, SurveyLocationService.EstimateChainage(-24.99, 28.0, references), 3);
        Assert.Equal(10.1, SurveyLocationService.EstimateChainage(-25.0005, 28.0, references), 3);
    }

    [Fact]
    public async Task GetLocationDetails_ByKm_RequiresASurvey()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetLocationDetailsAsync(null, 10, null, null));
    }

    [Fact]
    public async Task GetLocationDetails_ByKm_ReturnsObservationsAndSection()
    {
        _repository.Setup(r => r.GetRecordsAtChainageAsync("ws26", 10, SurveyLocationService.ObservationToleranceKm, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PavedRoadRecord { Id = "r1", DistressType = "Potholes", Degree = 4, ImagePaths = ["a.jpg"] }]);

        var details = await _service.GetLocationDetailsAsync(new SurveyReference("N1"), 10, null, null);

        Assert.Equal("ws26", details.Survey.Id);
        Assert.Equal("s1", details.Section!.Id);
        Assert.Null(details.Fix);
        Assert.Equal("r1", Assert.Single(details.Observations).Id);
    }

    [Fact]
    public async Task GetLocationDetails_NeitherKmNorGps_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetLocationDetailsAsync(new SurveyReference("N1"), null, -25, null));
    }
}
