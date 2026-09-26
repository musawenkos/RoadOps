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

        var create = await _client.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id, "km 0.0 - 10.0", 0, 10));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var section = (await create.Content.ReadFromJsonAsync<RoadSectionDto>())!;
        Assert.Equal(workspace.Id, section.WorkspaceId);

        var fetched = await _client.GetFromJsonAsync<RoadSectionDto>($"{RoadSectionsUrl}/{section.Id}");
        Assert.Equal("km 0.0 - 10.0", fetched!.SectionName);
        Assert.Equal(0, fetched.ChainageFrom);
        Assert.Equal(10, fetched.ChainageTo);

        var update = await _client.PutAsJsonAsync($"{RoadSectionsUrl}/{section.Id}", new UpdateRoadSectionDto { SectionName = "km 0.0 - 12.5", ChainageFrom = 0, ChainageTo = 12.5 });
        var updated = (await update.Content.ReadFromJsonAsync<RoadSectionDto>())!;
        Assert.Equal("km 0.0 - 12.5", updated.SectionName);
        Assert.Equal(12.5, updated.ChainageTo);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{RoadSectionsUrl}/{section.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{RoadSectionsUrl}/{section.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetByWorkspace_ReturnsOnlyThatWorkspacesSections()
    {
        var workspaceA = await _client.CreateWorkspaceAsync();
        var workspaceB = await _client.CreateWorkspaceAsync();
        var a1 = await _client.CreateSectionAsync(workspaceA.Id, 0, 10);
        var a2 = await _client.CreateSectionAsync(workspaceA.Id, 10, 20);
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

    [Fact]
    public async Task Create_OverlappingSection_Returns400_ButTouchingSectionIsAllowed()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        await _client.CreateSectionAsync(workspace.Id, 0, 10);

        var overlapping = await _client.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id, from: 9.5, to: 20));
        var touching = await _client.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id, from: 10, to: 20));

        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        Assert.Contains("overlaps", await overlapping.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, touching.StatusCode);
    }

    [Fact]
    public async Task Update_RangeExcludingExistingRecords_Returns400()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id, 0, 20);
        await _client.CreateRecordAsync(workspace.Id, section.Id, 15, 15.1);

        var response = await _client.PutAsJsonAsync($"{RoadSectionsUrl}/{section.Id}",
            new UpdateRoadSectionDto { SectionName = "shrunk", ChainageFrom = 0, ChainageTo = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
