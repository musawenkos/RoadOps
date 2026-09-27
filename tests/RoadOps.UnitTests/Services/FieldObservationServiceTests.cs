using Moq;
using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Field;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class FieldObservationServiceTests
{
    private const string Inspector = "t.mokoena";
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly WorkspaceOverview Active2026 = new("ws26", "N1 · VCI 2026", "VCI", "N1", 2026, WorkspaceStatus.Active, 1, 10, 0, 20);
    private static readonly WorkspaceOverview Archive2024 = new("ws24", "N1 · VCI 2024", "VCI", "N1", 2024, WorkspaceStatus.Archive, 1, 10, 0, 20);
    private static readonly RoadSection Section = new() { Id = "s1", WorkspaceId = "ws26", SectionName = "N1 S01", ChainageFrom = 0, ChainageTo = 20 };

    private readonly Mock<IConditionAnalyticsRepository> _analytics = new();
    private readonly Mock<IPavedRoadRecordRepository> _records = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IRoadSectionRepository> _sections = new();
    private readonly Mock<IPhotoRepository> _photos = new();
    private readonly FakeClock _clock = new(Start);
    private readonly List<PavedRoadRecord> _saved = [];
    private readonly FieldObservationService _service;

    public FieldObservationServiceTests()
    {
        _analytics.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1" && s.SurveyYear == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Active2026, Archive2024]);
        _analytics.Setup(r => r.FindWorkspacesAsync(It.Is<WorkspaceSearch>(s => s.Corridor == "N1" && s.SurveyYear == 2024), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Archive2024]);
        _analytics.Setup(r => r.FindNearestRecordsAsync(It.IsAny<GeoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NearbyRecord("ref", "ws24", "s24", "N1", 2024, 12.3, 12.4, -25.0, 28.0, SurfaceType.SurfaceSeal, 12)]);
        _analytics.Setup(r => r.FindSectionAtChainageAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Section);

        _workspaces.Setup(r => r.ExistsAsync("ws26", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _workspaces.Setup(r => r.GetByIdAsync("ws26", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = "ws26", Status = WorkspaceStatus.Active });
        _sections.Setup(r => r.GetByIdAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync(Section);
        _records.Setup(r => r.AddAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()))
            .Callback<PavedRoadRecord, CancellationToken>((r, _) => _saved.Add(r));

        var resolver = new SurveyResolver(_analytics.Object);
        _service = new FieldObservationService(
            resolver,
            new SurveyLocationService(_analytics.Object, resolver),
            _analytics.Object,
            new PavedRoadRecordService(_records.Object, _workspaces.Object, _sections.Object),
            _records.Object,
            _workspaces.Object,
            _photos.Object,
            _sections.Object,
            _clock);
    }

    private static LogObservationRequest Pothole(bool confirm = false, int? degree = 4, int? extent = 2) =>
        new(Latitude: -25.0, Longitude: 28.0, DistressType: "potholes", Degree: degree, Extent: extent, LengthM: 0.8, Notes: " Near culvert ", Confirm: confirm);

    private PavedRoadRecord Existing(string id = "r1", string createdBy = Inspector, DateTimeOffset? createdAt = null)
    {
        var record = new PavedRoadRecord
        {
            Id = id, WorkspaceId = "ws26", SectionId = "s1", ChainageFrom = 12.35, ChainageTo = 12.35, DistressType = "Potholes",
            Degree = 3, Extent = 2, RecommendedAction = RecommendedActionRules.UrgentPotholeRepair, Latitude = -25, Longitude = 28,
            Notes = "First note", CreatedBy = createdBy, CreatedAt = createdAt ?? Start.AddMinutes(-2), UpdatedAt = Start.AddMinutes(-2),
        };
        _records.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        return record;
    }

    [Fact]
    public async Task Log_MissingDegreeAndExtent_AsksForThemWithoutSaving()
    {
        var result = await _service.LogObservationAsync(Pothole(confirm: true, degree: null, extent: null), Inspector);

        Assert.Equal(LogObservationStatus.NeedsInput, result.Status);
        Assert.Equal(["degree (1-5)", "extent (1-5)"], result.Missing);
        Assert.Equal("N1 S01", result.Draft.Section.SectionName);
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task Log_WithoutConfirm_ReturnsReadBackAndSavesNothing()
    {
        var result = await _service.LogObservationAsync(Pothole(), Inspector);

        Assert.Equal(LogObservationStatus.AwaitingConfirmation, result.Status);
        Assert.Equal("Potholes", result.Draft.DistressType);
        Assert.Equal("ws26", result.Draft.Survey.Id); // the active survey, even though the GPS reference came from 2024
        Assert.Equal(12.35, result.Draft.ChainageFrom);
        Assert.Equal(RecommendedActionRules.UrgentPotholeRepair, result.Draft.RecommendedAction);
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task Log_Confirmed_SavesAsTheAuthenticatedCallerWithRulesAndMeasuredLength()
    {
        var result = await _service.LogObservationAsync(Pothole(confirm: true), Inspector);

        Assert.Equal(LogObservationStatus.Saved, result.Status);
        var saved = Assert.Single(_saved);
        Assert.Equal(Inspector, saved.CreatedBy);
        Assert.Equal("ws26", saved.WorkspaceId);
        Assert.Equal("s1", saved.SectionId);
        Assert.Equal(12.35, saved.ChainageFrom);
        Assert.Equal(12.351, saved.ChainageTo); // 0.8 m long
        Assert.Equal(SurfaceType.SurfaceSeal, saved.SurfaceType);
        Assert.Equal(RecommendedActionRules.UrgentPotholeRepair, saved.RecommendedAction);
        Assert.Equal("Near culvert", saved.Notes);
        Assert.Equal((-25.0, 28.0), (saved.Latitude, saved.Longitude));
        Assert.Equal(saved.Id, result.Observation!.Id);
    }

    [Fact]
    public async Task Log_RepeatedConfirmation_DoesNotSaveTwice()
    {
        var first = Existing("r1");
        first.ChainageFrom = 12.35;
        first.Degree = 4;
        _records.Setup(r => r.GetLatestByCreatorAsync(Inspector, Start - FieldObservationService.DuplicateWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);

        var result = await _service.LogObservationAsync(Pothole(confirm: true), Inspector);

        Assert.Equal(LogObservationStatus.AlreadySaved, result.Status);
        Assert.Equal("r1", result.Observation!.Id);
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task Log_ToAnArchivedSurvey_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<RuleViolationException>(() =>
            _service.LogObservationAsync(Pothole(confirm: true) with { Survey = new SurveyReference("N1", 2024) }, Inspector));

        Assert.Contains("active survey", ex.Message);
        Assert.Empty(_saved);
    }

    [Theory]
    [InlineData("crack")]
    [InlineData("None")]
    public async Task Log_DistressOutsideTheCatalogue_IsRejected(string distress)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.LogObservationAsync(Pothole() with { DistressType = distress }, Inspector));

        Assert.Contains("Crocodile cracking", ex.Message);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(6, 2)]
    [InlineData(3, 0)]
    public async Task Log_RatingsOutsideOneToFive_AreRejected(int degree, int extent)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.LogObservationAsync(Pothole(degree: degree, extent: extent), Inspector));
    }

    [Fact]
    public async Task Log_KmWithoutSurvey_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.LogObservationAsync(new LogObservationRequest(Km: 12, DistressType: "Potholes", Degree: 3, Extent: 2), Inspector));
    }

    [Fact]
    public async Task Log_KmOnSurvey_TakesPositionAndSurfaceFromNearbyObservation()
    {
        _analytics.Setup(r => r.GetRecordsAtChainageAsync("ws26", 12, It.IsAny<double>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PavedRoadRecord { Latitude = -25.1, Longitude = 28.1, SurfaceType = SurfaceType.Concrete }]);

        var result = await _service.LogObservationAsync(
            new LogObservationRequest(Km: 12, Survey: new SurveyReference("N1"), DistressType: "Punchouts", Degree: 3, Extent: 2), Inspector);

        Assert.Equal((-25.1, 28.1), (result.Draft.Latitude, result.Draft.Longitude));
        Assert.Equal(SurfaceType.Concrete, result.Draft.SurfaceType);
    }

    [Fact]
    public async Task Void_OwnObservationWithinTenMinutes_SoftVoids()
    {
        var record = Existing(createdAt: Start.AddMinutes(-9));

        var result = await _service.VoidObservationAsync("r1", Inspector);

        Assert.Equal(Start, record.VoidedAt);
        Assert.Equal(Inspector, record.VoidedBy);
        Assert.Equal("r1", result.Observation.Id);
        _records.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
        _records.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Void_AfterTenMinutes_IsRefused()
    {
        Existing(createdAt: Start.AddMinutes(-11));

        var ex = await Assert.ThrowsAsync<RuleViolationException>(() => _service.VoidObservationAsync("r1", Inspector));

        Assert.Contains("10 minutes", ex.Message);
        _records.Verify(r => r.UpdateAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Void_SomeoneElsesObservation_IsRefused()
    {
        Existing(createdBy: "l.naidoo");

        var ex = await Assert.ThrowsAsync<RuleViolationException>(() => _service.VoidObservationAsync("r1", Inspector));

        Assert.Contains("Only the inspector who logged", ex.Message);
    }

    [Fact]
    public async Task Update_AppendsNotesAndRecalculatesTheAction()
    {
        Existing();

        var change = await _service.UpdateObservationAsync("r1", new ObservationPatch(Degree: 1, Extent: 1, DepthMm: 40, Notes: "Water in it"), Inspector);

        Assert.Equal(3, change.Before.Degree);
        Assert.Equal(1, change.After.Degree);
        Assert.Equal("First note\nWater in it", change.After.Notes);
        Assert.Equal(40, change.After.DepthMm);
        Assert.Equal(RecommendedActionRules.RoutineMaintenance, change.After.RecommendedAction);
    }

    [Fact]
    public async Task Update_WithoutId_UsesTheCallersLatestObservation()
    {
        var latest = Existing("latest");
        _records.Setup(r => r.GetLatestByCreatorAsync(Inspector, Start - FieldObservationService.LatestObservationWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        var change = await _service.UpdateObservationAsync(null, new ObservationPatch(LengthM: 1.2), Inspector);

        Assert.Equal("latest", change.After.Id);
        Assert.Equal(1.2, change.After.LengthM);
        Assert.Equal(12.351, change.After.ChainageTo); // extended by the measured length
    }

    [Fact]
    public async Task Update_NothingGiven_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateObservationAsync("r1", new ObservationPatch(Notes: "  "), Inspector));
    }

    [Fact]
    public async Task Update_SomeoneElsesObservation_IsRefused()
    {
        Existing(createdBy: "l.naidoo");

        await Assert.ThrowsAsync<RuleViolationException>(() => _service.UpdateObservationAsync("r1", new ObservationPatch(Degree: 2), Inspector));
    }

    [Fact]
    public async Task AttachPhoto_LinksPhotoAndAddsItsPath()
    {
        var record = Existing();
        var photo = new Photo { Id = "p1", UploadedBy = Inspector };
        _photos.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(photo);

        var result = await _service.AttachPhotoAsync("p1", "r1", Inspector);

        Assert.False(result.WasAlreadyAttached);
        Assert.Equal("r1", photo.RecordId);
        Assert.Equal([FieldObservationService.PhotoPath("p1")], record.ImagePaths);
        _photos.Verify(r => r.UpdateAsync(photo, It.IsAny<CancellationToken>()), Times.Once);
        _records.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachPhoto_Twice_IsANoOp()
    {
        Existing();
        _photos.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(new Photo { Id = "p1", UploadedBy = Inspector, RecordId = "r1" });

        var result = await _service.AttachPhotoAsync("p1", "r1", Inspector);

        Assert.True(result.WasAlreadyAttached);
        _records.Verify(r => r.UpdateAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("l.naidoo", null)]   // someone else's upload
    [InlineData(Inspector, "other")] // already on another observation
    public async Task AttachPhoto_NotAllowed_IsRefused(string uploadedBy, string? attachedTo)
    {
        Existing();
        _photos.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(new Photo { Id = "p1", UploadedBy = uploadedBy, RecordId = attachedTo });

        await Assert.ThrowsAsync<RuleViolationException>(() => _service.AttachPhotoAsync("p1", "r1", Inspector));
    }

    [Fact]
    public async Task AttachPhoto_NoRecentObservation_ExplainsWhatToDo()
    {
        _photos.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(new Photo { Id = "p1", UploadedBy = Inspector });

        var ex = await Assert.ThrowsAsync<RuleViolationException>(() => _service.AttachPhotoAsync("p1", null, Inspector));

        Assert.Contains("say which observation", ex.Message);
    }
}
