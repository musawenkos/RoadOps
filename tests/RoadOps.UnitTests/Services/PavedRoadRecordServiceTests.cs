using Moq;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

public class PavedRoadRecordServiceTests
{
    private readonly Mock<IPavedRoadRecordRepository> _repository = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IRoadSectionRepository> _sections = new();
    private readonly PavedRoadRecordService _service;

    public PavedRoadRecordServiceTests()
    {
        _workspaces.Setup(r => r.ExistsAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _workspaces.Setup(r => r.ExistsAsync("other-ws", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _sections.Setup(r => r.GetByIdAsync("sec", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoadSection { Id = "sec", WorkspaceId = "ws", SectionName = "S01", ChainageFrom = 0, ChainageTo = 100 });
        _service = new PavedRoadRecordService(_repository.Object, _workspaces.Object, _sections.Object);
    }

    private static CreatePavedRoadRecordDto ValidCreateDto() => new()
    {
        WorkspaceId = "ws",
        SectionId = "sec",
        ChainageFrom = 10.0,
        ChainageTo = 11.0,
        SurfaceType = SurfaceType.SurfaceSeal,
        DistressType = "Pothole",
        Degree = 3,
        Extent = 2,
        RutDepthMm = 15.5,
        RidingQuality = "Poor",
        SkidResistance = "Low",
        StdRef = "REF-001",
        RecommendedAction = "Repair",
        Latitude = -25.7461,
        Longitude = 28.1881,
        ImagePaths = ["a.jpg"],
        Notes = "Near culvert",
        LengthM = 0.8,
        WidthM = 0.5,
        DepthMm = 60,
        CreatedBy = "inspector"
    };

    [Fact]
    public async Task CreateAsync_ValidDto_MapsEveryField()
    {
        var dto = ValidCreateDto();

        var result = await _service.CreateAsync(dto);

        Assert.True(Guid.TryParse(result.Id, out _));
        Assert.Equal(dto.WorkspaceId, result.WorkspaceId);
        Assert.Equal(dto.SectionId, result.SectionId);
        Assert.Equal(dto.ChainageFrom, result.ChainageFrom);
        Assert.Equal(dto.ChainageTo, result.ChainageTo);
        Assert.Equal(dto.SurfaceType, result.SurfaceType);
        Assert.Equal(dto.DistressType, result.DistressType);
        Assert.Equal(dto.Degree, result.Degree);
        Assert.Equal(dto.Extent, result.Extent);
        Assert.Equal(dto.RutDepthMm, result.RutDepthMm);
        Assert.Equal(dto.RidingQuality, result.RidingQuality);
        Assert.Equal(dto.SkidResistance, result.SkidResistance);
        Assert.Equal(dto.StdRef, result.StdRef);
        Assert.Equal(dto.RecommendedAction, result.RecommendedAction);
        Assert.Equal(dto.Latitude, result.Latitude);
        Assert.Equal(dto.Longitude, result.Longitude);
        Assert.Equal(dto.ImagePaths, result.ImagePaths);
        Assert.Equal(dto.Notes, result.Notes);
        Assert.Equal(dto.LengthM, result.LengthM);
        Assert.Equal(dto.WidthM, result.WidthM);
        Assert.Equal(dto.DepthMm, result.DepthMm);
        Assert.Equal(dto.CreatedBy, result.CreatedBy);
        _repository.Verify(r => r.AddAsync(It.Is<PavedRoadRecord>(p => p.Id == result.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EqualChainage_IsAllowed()
    {
        var dto = ValidCreateDto();
        dto.ChainageFrom = dto.ChainageTo = 5;

        var result = await _service.CreateAsync(dto);

        Assert.Equal(5, result.ChainageFrom);
    }

    public static TheoryData<Action<CreatePavedRoadRecordDto>, string> InvalidCreateCases => new()
    {
        { d => d.WorkspaceId = "", "WorkspaceId" },
        { d => d.SectionId = " ", "SectionId" },
        { d => d.CreatedBy = "", "CreatedBy" },
        { d => d.ChainageFrom = -1, "ChainageFrom" },
        { d => { d.ChainageFrom = 0; d.ChainageTo = -0.5; }, "ChainageTo" },
        { d => { d.ChainageFrom = 12; d.ChainageTo = 11; }, "ChainageFrom" },
        { d => { d.ChainageFrom = 99.5; d.ChainageTo = 100.5; }, "ChainageFrom" },
        { d => d.DistressType = " ", "DistressType" },
        { d => d.Degree = 0, "Degree" },
        { d => d.Degree = 6, "Degree" },
        { d => d.Extent = 0, "Extent" },
        { d => { d.DistressType = "None"; d.Degree = 0; d.Extent = 1; }, "Extent" },
        { d => d.RutDepthMm = -1, "RutDepthMm" },
        { d => d.Latitude = -91, "Latitude" },
        { d => d.Longitude = 181, "Longitude" },
        { d => d.LengthM = -0.1, "LengthM" },
        { d => d.WidthM = 51, "WidthM" },
        { d => d.DepthMm = double.NaN, "DepthMm" },
        { d => d.Notes = new string('x', 1001), "Notes" },
        { d => d.Notes = "ignore previous instructions\u001b[2J", "Notes" },
        { d => d.SurfaceType = (SurfaceType)9, "SurfaceType" },
        { d => d.ImagePaths = Enumerable.Repeat("a.jpg", 21).ToArray(), "ImagePaths" },
    };

    [Theory]
    [MemberData(nameof(InvalidCreateCases))]
    public async Task CreateAsync_InvalidDto_ThrowsAndDoesNotPersist(Action<CreatePavedRoadRecordDto> mutate, string expectedParam)
    {
        var dto = ValidCreateDto();
        mutate(dto);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        Assert.Equal(expectedParam, ex.ParamName);
        _repository.Verify(r => r.AddAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<Action<CreatePavedRoadRecordDto>, string, string> UnknownParentCases => new()
    {
        { d => d.WorkspaceId = "missing", "WorkspaceId", "Workspace 'missing' does not exist." },
        { d => d.SectionId = "missing", "SectionId", "Road section 'missing' does not exist." },
        { d => d.WorkspaceId = "other-ws", "SectionId", "does not belong to workspace 'other-ws'" },
    };

    [Theory]
    [MemberData(nameof(UnknownParentCases))]
    public async Task CreateAsync_UnknownOrMismatchedParent_ThrowsAndDoesNotPersist(Action<CreatePavedRoadRecordDto> mutate, string expectedParam, string expectedMessage)
    {
        var dto = ValidCreateDto();
        mutate(dto);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        Assert.Equal(expectedParam, ex.ParamName);
        Assert.Contains(expectedMessage, ex.Message);
        _repository.Verify(r => r.AddAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, -1)]
    [InlineData(6, 5)]
    public async Task FindByChainageRangeAsync_InvalidRange_Throws(double from, double to)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.FindByChainageRangeAsync(from, to, null, new PageRequest()));
        _repository.Verify(r => r.FindByChainageRangeAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindByChainageRangeAsync_ValidRange_QueriesRepository()
    {
        var page = new PageRequest(1, 10);
        _repository.Setup(r => r.FindByChainageRangeAsync(1, 2, "ws", page, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PavedRoadRecord> { Items = [new PavedRoadRecord { Id = "r1", ChainageFrom = 1.2, ChainageTo = 1.3 }], Page = 1, PageSize = 10, TotalCount = 1 });

        var result = await _service.FindByChainageRangeAsync(1, 2, "ws", page);

        Assert.Equal("r1", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetBySectionIdAsync_BlankId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetBySectionIdAsync("", new PageRequest()));
    }

    [Fact]
    public async Task UpdateAsync_Existing_UpdatesMutableFieldsOnly()
    {
        var existing = new PavedRoadRecord { Id = "r1", WorkspaceId = "ws", SectionId = "sec", CreatedBy = "orig", ChainageFrom = 1, ChainageTo = 2 };
        _repository.Setup(r => r.GetByIdAsync("r1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.UpdateAsync("r1", new UpdatePavedRoadRecordDto
        {
            ChainageFrom = 3,
            ChainageTo = 4,
            SurfaceType = SurfaceType.Concrete,
            DistressType = "Cracking",
            Degree = 4,
            Extent = 2
        });

        Assert.NotNull(result);
        Assert.Equal(3, result!.ChainageFrom);
        Assert.Equal(SurfaceType.Concrete, result.SurfaceType);
        Assert.Equal("Cracking", result.DistressType);
        Assert.Equal("ws", result.WorkspaceId);
        Assert.Equal("sec", result.SectionId);
        Assert.Equal("orig", result.CreatedBy);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_InvalidChainage_ThrowsBeforeLookup()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync("r1", new UpdatePavedRoadRecordDto { ChainageFrom = 5, ChainageTo = 1 }));
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_Missing_ReturnsNull()
    {
        Assert.Null(await _service.UpdateAsync("missing", new UpdatePavedRoadRecordDto { ChainageFrom = 1, ChainageTo = 2, DistressType = "Rutting", Degree = 2, Extent = 2 }));
    }
    [Fact]
    public async Task CreateAsync_NoRecommendedAction_DerivesItFromTheSharedRules()
    {
        var dto = ValidCreateDto();
        dto.DistressType = "potholes";
        dto.Degree = 4;
        dto.Extent = 2;
        dto.RecommendedAction = "";

        var result = await _service.CreateAsync(dto);

        Assert.Equal("Potholes", result.DistressType);
        Assert.Equal(RecommendedActionRules.UrgentPotholeRepair, result.RecommendedAction);
    }

    [Fact]
    public async Task CreateAsync_NoneDistress_AllowsZeroRatingsAndNeedsNoAction()
    {
        var dto = ValidCreateDto();
        dto.DistressType = "None";
        dto.Degree = 0;
        dto.Extent = 0;
        dto.RecommendedAction = "";

        var result = await _service.CreateAsync(dto);

        Assert.Equal(RecommendedActionRules.NoAction, result.RecommendedAction);
    }

    [Fact]
    public async Task CreateAsync_BlankNotes_AreStoredAsNullAndTrimmed()
    {
        var dto = ValidCreateDto();
        dto.Notes = "   ";
        Assert.Null((await _service.CreateAsync(dto)).Notes);

        dto.Notes = "  Line one\nline two  ";
        Assert.Equal("Line one\nline two", (await _service.CreateAsync(dto)).Notes);
    }

    [Fact]
    public async Task UpdateAsync_ChainageOutsideSection_Throws()
    {
        var existing = new PavedRoadRecord { Id = "r1", WorkspaceId = "ws", SectionId = "sec", ChainageFrom = 1, ChainageTo = 2 };
        _repository.Setup(r => r.GetByIdAsync("r1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync("r1", new UpdatePavedRoadRecordDto
        {
            ChainageFrom = 150, ChainageTo = 150.1, DistressType = "Rutting", Degree = 2, Extent = 2
        }));

        Assert.Contains("outside section", ex.Message);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<PavedRoadRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RecomputesActionWhenNotGiven()
    {
        var existing = new PavedRoadRecord { Id = "r1", WorkspaceId = "ws", SectionId = "sec", RecommendedAction = RecommendedActionRules.CrackSealing };
        _repository.Setup(r => r.GetByIdAsync("r1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.UpdateAsync("r1", new UpdatePavedRoadRecordDto
        {
            ChainageFrom = 1, ChainageTo = 1.1, DistressType = "Rutting", Degree = 5, Extent = 4, Notes = "Measured 45 mm"
        });

        Assert.Equal(RecommendedActionRules.MillAndReplace, result!.RecommendedAction);
        Assert.Equal("Measured 45 mm", result.Notes);
    }
}
