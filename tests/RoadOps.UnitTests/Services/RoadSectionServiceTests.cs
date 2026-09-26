using Moq;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;

namespace RoadOps.UnitTests.Services;

public class RoadSectionServiceTests
{
    private readonly Mock<IRoadSectionRepository> _repository = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IPavedRoadRecordRepository> _records = new();
    private readonly RoadSectionService _service;

    public RoadSectionServiceTests()
    {
        _workspaces.Setup(r => r.ExistsAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new RoadSectionService(_repository.Object, _workspaces.Object, _records.Object);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_PersistsSection()
    {
        var result = await _service.CreateAsync(new CreateRoadSectionDto { SectionName = "km 0-10", WorkspaceId = "ws", ChainageFrom = 0, ChainageTo = 10, CreatedBy = "u" });

        Assert.True(Guid.TryParse(result.Id, out _));
        Assert.Equal("km 0-10", result.SectionName);
        Assert.Equal("ws", result.WorkspaceId);
        _repository.Verify(r => r.AddAsync(It.Is<RoadSection>(s => s.Id == result.Id && s.WorkspaceId == "ws"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_UnknownWorkspace_ThrowsAndDoesNotPersist()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = "A", WorkspaceId = "missing", ChainageFrom = 0, ChainageTo = 10, CreatedBy = "u" }));

        Assert.Equal("WorkspaceId", ex.ParamName);
        Assert.Contains("does not exist", ex.Message);
        _repository.Verify(r => r.AddAsync(It.IsAny<RoadSection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "ws", "u", "SectionName")]
    [InlineData("name", "", "u", "WorkspaceId")]
    [InlineData("name", "ws", " ", "CreatedBy")]
    public async Task CreateAsync_MissingRequiredField_Throws(string name, string workspaceId, string createdBy, string expectedParam)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = name, WorkspaceId = workspaceId, ChainageFrom = 0, ChainageTo = 10, CreatedBy = createdBy }));

        Assert.Equal(expectedParam, ex.ParamName);
        _repository.Verify(r => r.AddAsync(It.IsAny<RoadSection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByWorkspaceIdAsync_ReturnsMappedSections()
    {
        _repository.Setup(r => r.GetByWorkspaceIdAsync("ws", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<RoadSection> { Items = [new RoadSection { Id = "s1", WorkspaceId = "ws", SectionName = "A" }], Page = 1, PageSize = 50, TotalCount = 1 });

        var result = await _service.GetByWorkspaceIdAsync("ws", new PageRequest());

        Assert.Equal("s1", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetByWorkspaceIdAsync_BlankId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetByWorkspaceIdAsync("", new PageRequest()));
    }

    [Fact]
    public async Task UpdateAsync_Existing_RenamesSection()
    {
        var existing = new RoadSection { Id = "s1", SectionName = "Old", WorkspaceId = "ws" };
        _repository.Setup(r => r.GetByIdAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.UpdateAsync("s1", new UpdateRoadSectionDto { SectionName = "New", ChainageFrom = 0, ChainageTo = 10 });

        Assert.Equal("New", result!.SectionName);
        Assert.Equal("ws", result.WorkspaceId);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Missing_ReturnsNull()
    {
        Assert.Null(await _service.UpdateAsync("nope", new UpdateRoadSectionDto { SectionName = "New", ChainageFrom = 0, ChainageTo = 10 }));
    }

    [Fact]
    public async Task UpdateAsync_BlankName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync("s1", new UpdateRoadSectionDto()));
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository()
    {
        await _service.DeleteAsync("s1");
        _repository.Verify(r => r.DeleteAsync("s1", It.IsAny<CancellationToken>()), Times.Once);
    }
    [Theory]
    [InlineData(-1, 10, "ChainageFrom")]
    [InlineData(5, 5, "ChainageFrom")]
    [InlineData(10, 5, "ChainageFrom")]
    [InlineData(0, -1, "ChainageTo")]
    public async Task CreateAsync_InvalidChainage_Throws(double from, double to, string expectedParam)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = "A", WorkspaceId = "ws", ChainageFrom = from, ChainageTo = to, CreatedBy = "u" }));

        Assert.Equal(expectedParam, ex.ParamName);
    }

    [Fact]
    public async Task CreateAsync_OverlappingSection_ThrowsAndDoesNotPersist()
    {
        _repository.Setup(r => r.OverlapsAnotherSectionAsync("ws", 5, 15, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = "A", WorkspaceId = "ws", ChainageFrom = 5, ChainageTo = 15, CreatedBy = "u" }));

        Assert.Contains("overlaps", ex.Message);
        _repository.Verify(r => r.AddAsync(It.IsAny<RoadSection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RangeThatWouldStrandRecords_Throws()
    {
        _repository.Setup(r => r.GetByIdAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoadSection { Id = "s1", WorkspaceId = "ws", SectionName = "A", ChainageFrom = 0, ChainageTo = 20 });
        _records.Setup(r => r.GetChainageExtentBySectionAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync((2.0, 18.0));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync("s1", new UpdateRoadSectionDto { SectionName = "A", ChainageFrom = 0, ChainageTo = 10 }));

        Assert.Contains("must include them", ex.Message);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<RoadSection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ExtendsRange_ExcludingItselfFromTheOverlapCheck()
    {
        var existing = new RoadSection { Id = "s1", WorkspaceId = "ws", SectionName = "A", ChainageFrom = 0, ChainageTo = 20 };
        _repository.Setup(r => r.GetByIdAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _records.Setup(r => r.GetChainageExtentBySectionAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync((2.0, 18.0));

        var result = await _service.UpdateAsync("s1", new UpdateRoadSectionDto { SectionName = "A", ChainageFrom = 0, ChainageTo = 25 });

        Assert.Equal(25, result!.ChainageTo);
        _repository.Verify(r => r.OverlapsAnotherSectionAsync("ws", 0, 25, "s1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
