using Moq;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.UnitTests.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IWorkspaceRepository> _repository = new();
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _service = new WorkspaceService(_repository.Object);
    }

    private static CreateWorkspaceDto ValidCreateDto() => new()
    {
        Name = "N1 Pretoria",
        AssessmentType = "Visual Condition Assessment",
        Corridor = "n1 ",
        SurveyYear = 2026,
        CreatedBy = "inspector"
    };

    [Fact]
    public async Task CreateAsync_ValidDto_PersistsActiveWorkspaceWithGeneratedId()
    {
        Workspace? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Workspace>(), It.IsAny<CancellationToken>()))
            .Callback<Workspace, CancellationToken>((w, _) => saved = w)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(ValidCreateDto());

        Assert.NotNull(saved);
        Assert.True(Guid.TryParse(result.Id, out _));
        Assert.Equal(saved!.Id, result.Id);
        Assert.Equal("N1 Pretoria", result.Name);
        Assert.Equal("Visual Condition Assessment", result.AssessmentType);
        Assert.Equal(WorkspaceStatus.Active, result.Status);
        Assert.Equal("N1", result.Corridor);
        Assert.Equal(2026, result.SurveyYear);
        Assert.Equal("inspector", result.CreatedBy);
        Assert.True((result.UpdatedAt - result.CreatedAt).Duration() < TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("", "type", "user", "Name")]
    [InlineData("  ", "type", "user", "Name")]
    [InlineData("name", "", "user", "AssessmentType")]
    [InlineData("name", "type", "", "CreatedBy")]
    public async Task CreateAsync_MissingRequiredField_ThrowsAndDoesNotPersist(string name, string type, string createdBy, string expectedParam)
    {
        var dto = new CreateWorkspaceDto { Name = name, AssessmentType = type, Corridor = "N1", SurveyYear = 2026, CreatedBy = createdBy };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        Assert.Equal(expectedParam, ex.ParamName);
        _repository.Verify(r => r.AddAsync(It.IsAny<Workspace>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByIdAsync_BlankId_Throws(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetByIdAsync(id));
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ReturnsNull()
    {
        _repository.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Workspace?)null);

        Assert.Null(await _service.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task GetPageAsync_MapsItemsAndKeepsPagingMetadata()
    {
        var page = new PageRequest(2, 2);
        _repository.Setup(r => r.GetPageAsync(page, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<Workspace>
        {
            Items = [new Workspace { Id = "3", Name = "C" }, new Workspace { Id = "4", Name = "D" }],
            Page = 2,
            PageSize = 2,
            TotalCount = 5
        });

        var result = await _service.GetPageAsync(page);

        Assert.Equal(["C", "D"], result.Items.Select(w => w.Name));
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 501)]
    public async Task GetPageAsync_InvalidPage_ThrowsWithoutQuerying(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetPageAsync(new PageRequest(page, pageSize)));
        _repository.Verify(r => r.GetPageAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_Existing_UpdatesFieldsAndTimestamp()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var existing = new Workspace { Id = "ws", Name = "Old", AssessmentType = "Old", CreatedAt = created, UpdatedAt = created };
        _repository.Setup(r => r.GetByIdAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.UpdateAsync("ws", new UpdateWorkspaceDto { Name = "New", AssessmentType = "Rut", Corridor = "N4", SurveyYear = 2024, Status = WorkspaceStatus.Archive });

        Assert.NotNull(result);
        Assert.Equal("New", result!.Name);
        Assert.Equal("Rut", result.AssessmentType);
        Assert.Equal(WorkspaceStatus.Archive, result.Status);
        Assert.Equal("N4", result.Corridor);
        Assert.Equal(2024, result.SurveyYear);
        Assert.Equal(created, result.CreatedAt);
        Assert.True(result.UpdatedAt > created);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Missing_ReturnsNullWithoutSaving()
    {
        _repository.Setup(r => r.GetByIdAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync((Workspace?)null);

        var result = await _service.UpdateAsync("ws", new UpdateWorkspaceDto { Name = "N", AssessmentType = "T", Corridor = "N1", SurveyYear = 2026 });

        Assert.Null(result);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Workspace>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_BlankName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync("ws", new UpdateWorkspaceDto { Name = "", AssessmentType = "T" }));
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository()
    {
        Assert.True(await _service.DeleteAsync("ws"));
        _repository.Verify(r => r.DeleteAsync("ws", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_BlankId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteAsync(""));
    }
    [Theory]
    [InlineData("", 2026, "Corridor")]
    [InlineData("N 1", 2026, "Corridor")]
    [InlineData("N1", 0, "SurveyYear")]
    [InlineData("N1", 1989, "SurveyYear")]
    [InlineData("N1", 2101, "SurveyYear")]
    public async Task CreateAsync_InvalidCorridorOrYear_Throws(string corridor, int year, string expectedParam)
    {
        var dto = ValidCreateDto();
        dto.Corridor = corridor;
        dto.SurveyYear = year;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        Assert.Equal(expectedParam, ex.ParamName);
    }

    [Fact]
    public async Task UpdateAsync_UnknownStatus_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync("ws",
            new UpdateWorkspaceDto { Name = "N", AssessmentType = "T", Corridor = "N1", SurveyYear = 2026, Status = (WorkspaceStatus)42 }));
    }
}
