using Microsoft.AspNetCore.Mvc;
using Moq;
using RoadOps.Api.Controllers;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Domain.Entities;

namespace RoadOps.UnitTests.Controllers;

/// <summary>
/// Verifies how controllers translate service results and validation errors into HTTP responses.
/// Services are concrete classes, so they are used for real with a mocked repository underneath.
/// </summary>
public class ControllerTests
{
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IRoadSectionRepository> _sections = new();
    private readonly Mock<IPavedRoadRecordRepository> _records = new();

    private WorkspacesController CreateWorkspacesController() => new(new WorkspaceService(_workspaces.Object));
    private RoadSectionsController CreateSectionsController() => new(new RoadSectionService(_sections.Object, _workspaces.Object, _records.Object));
    private PavedRoadRecordsController CreateRecordsController() => new(new PavedRoadRecordService(_records.Object, _workspaces.Object, _sections.Object));

    [Fact]
    public async Task Workspaces_Create_Valid_Returns201WithLocationRoute()
    {
        var response = await CreateWorkspacesController().Create(
            new CreateWorkspaceDto { Name = "N1", AssessmentType = "VCI", Corridor = "N1", SurveyYear = 2026, CreatedBy = "u" }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var dto = Assert.IsType<WorkspaceDto>(created.Value);
        Assert.Equal(nameof(WorkspacesController.GetById), created.ActionName);
        Assert.Equal(dto.Id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task Workspaces_Create_Invalid_Returns400WithMessage()
    {
        var response = await CreateWorkspacesController().Create(new CreateWorkspaceDto(), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Contains("Workspace name is required", bad.Value!.ToString());
    }

    [Fact]
    public async Task Workspaces_GetById_Missing_Returns404()
    {
        var response = await CreateWorkspacesController().GetById("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task Workspaces_GetById_Existing_Returns200()
    {
        _workspaces.Setup(r => r.GetByIdAsync("ws", It.IsAny<CancellationToken>())).ReturnsAsync(new Workspace { Id = "ws", Name = "N1" });

        var response = await CreateWorkspacesController().GetById("ws", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal("N1", Assert.IsType<WorkspaceDto>(ok.Value).Name);
    }

    [Fact]
    public async Task Workspaces_Update_Missing_Returns404()
    {
        var response = await CreateWorkspacesController().Update("missing",
            new UpdateWorkspaceDto { Name = "N", AssessmentType = "T", Corridor = "N1", SurveyYear = 2026 }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task Workspaces_Delete_Returns204()
    {
        Assert.IsType<NoContentResult>(await CreateWorkspacesController().Delete("ws", CancellationToken.None));
    }

    [Fact]
    public async Task Workspaces_Delete_BlankId_Returns400()
    {
        Assert.IsType<BadRequestObjectResult>(await CreateWorkspacesController().Delete(" ", CancellationToken.None));
    }

    [Fact]
    public async Task Sections_Create_Invalid_Returns400()
    {
        var response = await CreateSectionsController().Create(new CreateRoadSectionDto { SectionName = "A" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task Sections_GetByWorkspace_ReturnsOk()
    {
        _sections.Setup(r => r.GetByWorkspaceIdAsync("ws", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<RoadSection> { Items = [new RoadSection { Id = "s1", WorkspaceId = "ws" }], Page = 1, PageSize = 50, TotalCount = 1 });

        var response = await CreateSectionsController().GetByWorkspaceId("ws", cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Single(Assert.IsType<PagedResult<RoadSectionDto>>(ok.Value).Items);
    }

    [Fact]
    public async Task Records_GetAll_PageSizeTooLarge_Returns400()
    {
        var response = await CreateRecordsController().GetAll(page: 1, pageSize: PageRequest.MaxPageSize + 1);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task Records_Create_UnknownWorkspace_Returns400()
    {
        var response = await CreateRecordsController().Create(new CreatePavedRoadRecordDto
        {
            WorkspaceId = "missing", SectionId = "sec", CreatedBy = "u", ChainageFrom = 1, ChainageTo = 2, DistressType = "Potholes", Degree = 3, Extent = 2
        }, CancellationToken.None);

        Assert.Contains("does not exist", Assert.IsType<BadRequestObjectResult>(response.Result).Value!.ToString());
    }

    [Fact]
    public async Task Records_FindByChainage_InvertedRange_Returns400()
    {
        var response = await CreateRecordsController().FindByChainageRange(10, 5, null, cancellationToken: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task Records_Update_InvalidChainage_Returns400()
    {
        var response = await CreateRecordsController().Update("r1",
            new UpdatePavedRoadRecordDto { ChainageFrom = -1, ChainageTo = 1 }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task Records_GetById_Missing_Returns404()
    {
        var response = await CreateRecordsController().GetById("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }
}
