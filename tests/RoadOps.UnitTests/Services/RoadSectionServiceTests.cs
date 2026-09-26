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
    private readonly RoadSectionService _service;

    public RoadSectionServiceTests()
    {
        _workspaces.Setup(r => r.ExistsAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new RoadSectionService(_repository.Object, _workspaces.Object);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_PersistsSection()
    {
        var result = await _service.CreateAsync(new CreateRoadSectionDto { SectionName = "km 0-10", WorkspaceId = "ws", CreatedBy = "u" });

        Assert.True(Guid.TryParse(result.Id, out _));
        Assert.Equal("km 0-10", result.SectionName);
        Assert.Equal("ws", result.WorkspaceId);
        _repository.Verify(r => r.AddAsync(It.Is<RoadSection>(s => s.Id == result.Id && s.WorkspaceId == "ws"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_UnknownWorkspace_ThrowsAndDoesNotPersist()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = "A", WorkspaceId = "missing", CreatedBy = "u" }));

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
            _service.CreateAsync(new CreateRoadSectionDto { SectionName = name, WorkspaceId = workspaceId, CreatedBy = createdBy }));

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

        var result = await _service.UpdateAsync("s1", new UpdateRoadSectionDto { SectionName = "New" });

        Assert.Equal("New", result!.SectionName);
        Assert.Equal("ws", result.WorkspaceId);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Missing_ReturnsNull()
    {
        Assert.Null(await _service.UpdateAsync("nope", new UpdateRoadSectionDto { SectionName = "New" }));
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
}
