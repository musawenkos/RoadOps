using Moq;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
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
            .ReturnsAsync(new RoadSection { Id = "sec", WorkspaceId = "ws" });
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
            Degree = 4
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
        Assert.Null(await _service.UpdateAsync("missing", new UpdatePavedRoadRecordDto { ChainageFrom = 1, ChainageTo = 2 }));
    }
}
