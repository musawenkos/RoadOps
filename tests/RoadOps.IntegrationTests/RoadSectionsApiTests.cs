using System.Net;
using System.Net.Http.Json;
using RoadOps.Application.DTOs;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RoadSectionsApiTests(RoadOpsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Crud_Lifecycle_Works()
    {
        var workspace = await _client.CreateWorkspaceAsync();

        var create = await _client.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id, "km 0.0 - 10.0"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var section = (await create.Content.ReadFromJsonAsync<RoadSectionDto>())!;
        Assert.Equal(workspace.Id, section.WorkspaceId);

        var fetched = await _client.GetFromJsonAsync<RoadSectionDto>($"{RoadSectionsUrl}/{section.Id}");
        Assert.Equal("km 0.0 - 10.0", fetched!.SectionName);

        var update = await _client.PutAsJsonAsync($"{RoadSectionsUrl}/{section.Id}", new UpdateRoadSectionDto { SectionName = "km 0.0 - 12.5" });
        Assert.Equal("km 0.0 - 12.5", (await update.Content.ReadFromJsonAsync<RoadSectionDto>())!.SectionName);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{RoadSectionsUrl}/{section.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{RoadSectionsUrl}/{section.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetByWorkspace_ReturnsOnlyThatWorkspacesSections()
    {
        var workspaceA = await _client.CreateWorkspaceAsync();
        var workspaceB = await _client.CreateWorkspaceAsync();
        var a1 = await _client.CreateSectionAsync(workspaceA.Id);
        var a2 = await _client.CreateSectionAsync(workspaceA.Id);
        await _client.CreateSectionAsync(workspaceB.Id);

        var sections = await _client.GetPageAsync<RoadSectionDto>($"{RoadSectionsUrl}/workspace/{workspaceA.Id}");

        Assert.Equal([a1.Id, a2.Id], sections.Items.Select(s => s.Id));
        Assert.Equal(2, sections.TotalCount);
    }

    [Fact]
    public async Task Create_MissingWorkspaceId_Returns400()
    {
        var response = await _client.PostAsJsonAsync(RoadSectionsUrl, new CreateRoadSectionDto { SectionName = "x", CreatedBy = "t" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_BlankName_Returns400()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);

        var response = await _client.PutAsJsonAsync($"{RoadSectionsUrl}/{section.Id}", new UpdateRoadSectionDto { SectionName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
