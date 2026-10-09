using Moq;
using RoadOps.Application.Analytics;
using RoadOps.Application.Field;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

public class SessionMemoryServiceTests
{
    private const string Inspector = "t.mokoena";
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly WorkspaceOverview Active2026 = new("ws26", "N1 · VCI 2026", "VCI", "N1", 2026, WorkspaceStatus.Active, 1, 10, 0, 20);
    private static readonly WorkspaceOverview Archive2024 = new("ws24", "N1 · VCI 2024", "VCI", "N1", 2024, WorkspaceStatus.Archive, 1, 10, 0, 20);
    private static readonly RoadSection Section = new() { Id = "s1", WorkspaceId = "ws26", SectionName = "N1 S01", ChainageFrom = 0, ChainageTo = 20 };

    private readonly InMemorySessions _sessions = new();
    private readonly Mock<IPavedRoadRecordRepository> _records = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IRoadSectionRepository> _sections = new();
    private readonly Mock<IConditionAnalyticsRepository> _analytics = new();
    private readonly FakeClock _clock = new(Start);
    private readonly SessionMemoryService _service;

    public SessionMemoryServiceTests()
    {
        _analytics.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1" && s.SurveyYear == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Active2026, Archive2024]);
        _analytics.Setup(r => r.FindSectionAtChainageAsync("ws26", It.IsAny<double>(), It.IsAny<CancellationToken>())).ReturnsAsync(Section);
        _analytics.Setup(r => r.FindNearestRecordsAsync(It.IsAny<GeoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NearbyRecord("ref", "ws24", "s24", "N1", 2024, 7.4, 7.5, -25.0, 28.0, SurfaceType.SurfaceSeal, 12)]);
        _analytics.Setup(r => r.GetConditionAggregateAsync(It.IsAny<ConditionScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionAggregate(10, 6, 4, new Dictionary<int, int>(), [], new Dictionary<string, int>(), null, null, null, null));
        _workspaces.Setup(r => r.GetByIdAsync("ws26", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = "ws26", Name = "N1 · VCI 2026", Corridor = "N1", SurveyYear = 2026, Status = WorkspaceStatus.Active });
        _sections.Setup(r => r.GetByIdAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync(Section);

        var resolver = new SurveyResolver(_analytics.Object);
        _service = new SessionMemoryService(_sessions, _records.Object, _workspaces.Object, _sections.Object, _analytics.Object,
            resolver, new SurveyLocationService(_analytics.Object, resolver), _clock);
    }

    private void LatestObservation(double km, DateTimeOffset createdAt) =>
        _records.Setup(r => r.GetLatestByCreatorAsync(Inspector, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PavedRoadRecord
            {
                Id = "r1", WorkspaceId = "ws26", SectionId = "s1", ChainageFrom = km, ChainageTo = km, DistressType = "Potholes",
                Degree = 4, Extent = 2, CreatedBy = Inspector, CreatedAt = createdAt, UpdatedAt = createdAt,
            });

    [Fact]
    public async Task Get_WithNothingSavedOrLogged_IsEmpty()
    {
        var summary = await _service.GetSummaryAsync(Inspector);

        Assert.True(summary.IsEmpty);
        Assert.Equal(SessionPositionSource.None, summary.PositionSource);
    }

    [Fact]
    public async Task Save_KmAndCorridor_ResolvesSurveyAndSection_AndCountsPreviousSurveyDefectsAhead()
    {
        var summary = await _service.SaveAsync(new SaveSessionRequest(Km: 3.2, Survey: new SurveyReference("N1"), FollowUps: " Check the culvert "), Inspector);

        Assert.Equal(SessionPositionSource.Saved, summary.PositionSource);
        Assert.Equal("ws26", summary.Survey!.Id);
        Assert.Equal("N1 S01", summary.Section!.SectionName);
        Assert.Equal(3.2, summary.Km);
        Assert.Equal("Check the culvert", summary.FollowUps);
        Assert.Equal(new PreviousSurveyAhead(2024, 4, 3.2, 20), summary.PreviousSurveyAhead);
        _analytics.Verify(r => r.GetConditionAggregateAsync(new ConditionScope("ws24", null, 3.2, 20), It.IsAny<int>(), It.IsAny<CancellationToken>()));

        var stored = _sessions.Rows[Inspector];
        Assert.Equal(Start, stored.PositionAt);
        Assert.Equal(Start, stored.UpdatedAt);
    }

    [Fact]
    public async Task Save_KmAlone_UsesTheSavedSurvey()
    {
        await _service.SaveAsync(new SaveSessionRequest(Survey: new SurveyReference("N1")), Inspector);

        var summary = await _service.SaveAsync(new SaveSessionRequest(Km: 5.5), Inspector);

        Assert.Equal("ws26", summary.Survey!.Id);
        Assert.Equal(5.5, summary.Km);
    }

    [Fact]
    public async Task Save_KmAlone_WithNoSurveySaved_AsksForTheSurvey()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(new SaveSessionRequest(Km: 5.5), Inspector));
        Assert.Contains("Name the survey", ex.Message);
    }

    [Fact]
    public async Task Save_Gps_LocatesTheInspector()
    {
        var summary = await _service.SaveAsync(new SaveSessionRequest(Latitude: -25.0, Longitude: 28.0), Inspector);

        Assert.Equal("ws26", summary.Survey!.Id);
        Assert.Equal(7.4, summary.Km!.Value, 1);
    }

    [Theory]
    [InlineData(-25.0, null)]
    [InlineData(null, 28.0)]
    public async Task Save_HalfAGpsPosition_IsRejected(double? latitude, double? longitude)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(new SaveSessionRequest(Latitude: latitude, Longitude: longitude), Inspector));
    }

    [Fact]
    public async Task Save_Nothing_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(new SaveSessionRequest(FollowUps: "  "), Inspector));
        Assert.Contains("Nothing to save", ex.Message);
    }

    [Fact]
    public async Task Save_TooLongFollowUps_AreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(new SaveSessionRequest(FollowUps: new string('x', 1001)), Inspector));
    }

    [Fact]
    public async Task Get_ANewerObservation_MovesThePosition()
    {
        await _service.SaveAsync(new SaveSessionRequest(Km: 3.2, Survey: new SurveyReference("N1")), Inspector);
        LatestObservation(km: 6.1, createdAt: Start.AddMinutes(20));

        var summary = await _service.GetSummaryAsync(Inspector);

        Assert.Equal(SessionPositionSource.LatestObservation, summary.PositionSource);
        Assert.Equal(6.1, summary.Km);
        Assert.Equal("r1", summary.LatestObservation!.Id);
    }

    [Fact]
    public async Task Get_AnOlderObservation_DoesNotOverrideTheSavedPosition()
    {
        LatestObservation(km: 6.1, createdAt: Start.AddMinutes(-20));
        await _service.SaveAsync(new SaveSessionRequest(Km: 3.2, Survey: new SurveyReference("N1")), Inspector);

        var summary = await _service.GetSummaryAsync(Inspector);

        Assert.Equal(SessionPositionSource.Saved, summary.PositionSource);
        Assert.Equal(3.2, summary.Km);
        Assert.NotNull(summary.LatestObservation);
    }

    [Fact]
    public async Task Save_FollowUpsOnly_KeepsThePositionTimeSoANewerObservationStillWins()
    {
        await _service.SaveAsync(new SaveSessionRequest(Km: 3.2, Survey: new SurveyReference("N1")), Inspector);
        LatestObservation(km: 6.1, createdAt: Start.AddMinutes(10));
        _clock.Now = Start.AddMinutes(30);

        var summary = await _service.SaveAsync(new SaveSessionRequest(FollowUps: "Recheck km 4"), Inspector);

        Assert.Equal(SessionPositionSource.LatestObservation, summary.PositionSource);
        Assert.Equal(6.1, summary.Km);
        Assert.Equal(Start, _sessions.Rows[Inspector].PositionAt);
        Assert.Equal(Start.AddMinutes(30), summary.SavedAt);
    }

    [Fact]
    public async Task Save_ClearFollowUps_RemovesThem()
    {
        await _service.SaveAsync(new SaveSessionRequest(FollowUps: "Recheck km 4"), Inspector);

        var summary = await _service.SaveAsync(new SaveSessionRequest(ClearFollowUps: true), Inspector);

        Assert.Null(summary.FollowUps);
    }

    [Fact]
    public async Task Get_NoPreviousSurveyAhead_AtOrPastTheSectionEnd()
    {
        var summary = await _service.SaveAsync(new SaveSessionRequest(Km: 20, Survey: new SurveyReference("N1")), Inspector);

        Assert.Null(summary.PreviousSurveyAhead);
    }

    [Fact]
    public async Task EachInspector_HasTheirOwnSummary()
    {
        await _service.SaveAsync(new SaveSessionRequest(FollowUps: "Mine"), Inspector);

        Assert.True((await _service.GetSummaryAsync("someone.else")).IsEmpty);
    }

    [Fact]
    public async Task MissingCaller_IsAProgrammingError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetSummaryAsync(" "));
    }

    private sealed class InMemorySessions : IInspectorSessionRepository
    {
        public Dictionary<string, InspectorSession> Rows { get; } = [];

        public Task<InspectorSession?> GetAsync(string inspector, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.TryGetValue(inspector, out var row) ? Copy(row) : null);

        public Task SaveAsync(InspectorSession session, CancellationToken cancellationToken = default)
        {
            Rows[session.Inspector] = Copy(session);
            return Task.CompletedTask;
        }

        // Like the database: callers never hold the stored instance.
        private static InspectorSession Copy(InspectorSession s) => new()
        {
            Inspector = s.Inspector, WorkspaceId = s.WorkspaceId, SectionId = s.SectionId, Km = s.Km, PositionAt = s.PositionAt,
            FollowUps = s.FollowUps, UpdatedAt = s.UpdatedAt,
        };
    }
}
