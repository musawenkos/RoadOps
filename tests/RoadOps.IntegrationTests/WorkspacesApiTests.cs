using System.Net;
using System.Net.Http.Json;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WorkspacesApiTests(RoadOpsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Crud_Lifecycle_Works()
    {
        // Create
        var create = await _client.PostAsJsonAsync(WorkspacesUrl, NewWorkspace("N4 Maputo Corridor"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Assert.Equal("N4 Maputo Corridor", created.Name);
        Assert.Equal(WorkspaceStatus.Active, created.Status);
        Assert.NotNull(create.Headers.Location);

        // Read
        var fetched = await _client.GetFromJsonAsync<WorkspaceDto>(create.Headers.Location);
        Assert.Equal(created.Id, fetched!.Id);

        var all = await _client.GetPageAsync<WorkspaceDto>($"{WorkspacesUrl}?pageSize=500");
        Assert.Contains(all.Items, w => w.Id == created.Id);

        // Update
        var update = await _client.PutAsJsonAsync($"{WorkspacesUrl}/{created.Id}",
            new UpdateWorkspaceDto { Name = "N4 Renamed", AssessmentType = "Rut Survey", Status = WorkspaceStatus.Suspended });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Assert.Equal("N4 Renamed", updated.Name);
        Assert.Equal(WorkspaceStatus.Suspended, updated.Status);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        // Delete
        var delete = await _client.DeleteAsync($"{WorkspacesUrl}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{WorkspacesUrl}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(WorkspacesUrl, new CreateWorkspaceDto { AssessmentType = "VCI", CreatedBy = "t" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Workspace name is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{WorkspacesUrl}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"{WorkspacesUrl}/{Guid.NewGuid()}",
            new UpdateWorkspaceDto { Name = "x", AssessmentType = "y" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Workspace_CascadesToSectionsAndRecords()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var record = await _client.CreateRecordAsync(workspace.Id, section.Id);

        (await _client.DeleteAsync($"{WorkspacesUrl}/{workspace.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{RoadSectionsUrl}/{section.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{PavedRoadRecordsUrl}/{record.Id}")).StatusCode);
    }
}
