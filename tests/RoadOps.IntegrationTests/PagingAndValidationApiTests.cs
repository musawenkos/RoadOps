using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RoadOps.Application.DTOs;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PagingAndValidationApiTests(RoadOpsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Paging_WalksEveryRecordExactlyOnce_EvenWithDuplicateChainages()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var created = new List<string>();
        for (var i = 0; i < 23; i++)
        {
            // Pairs of records share a chainage, like two distresses on one segment.
            var from = i / 2 * 0.1;
            created.Add((await _client.CreateRecordAsync(workspace.Id, section.Id, from, from + 0.1)).Id);
        }

        var seen = new List<string>();
        for (var page = 1; page <= 5; page++)
        {
            var result = await _client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/section/{section.Id}?page={page}&pageSize=5");
            Assert.Equal(23, result.TotalCount);
            Assert.Equal(5, result.TotalPages);
            Assert.Equal(page < 5 ? 5 : 3, result.Items.Count);
            Assert.Equal(page < 5, result.HasNextPage);
            seen.AddRange(result.Items.Select(r => r.Id));
        }

        Assert.Equal(created.Order(), seen.Order());
        Assert.Equal(seen.Count, seen.Distinct().Count());

        var beyond = await _client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/section/{section.Id}?page=6&pageSize=5");
        Assert.Empty(beyond.Items);
        Assert.Equal(23, beyond.TotalCount);
    }

    [Fact]
    public async Task Paging_UsesDefaultPageSizeWhenNotSpecified()
    {
        var result = await _client.GetPageAsync<WorkspaceDto>(WorkspacesUrl);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
    }

    [Theory]
    [InlineData(WorkspacesUrl + "?page=0")]
    [InlineData(WorkspacesUrl + "?pageSize=0")]
    [InlineData(RoadSectionsUrl + "?pageSize=501")]
    [InlineData(PavedRoadRecordsUrl + "?pageSize=1000")]
    [InlineData(PavedRoadRecordsUrl + "/workspace/any?page=-1")]
    [InlineData(PavedRoadRecordsUrl + "/chainage?chainageFrom=0&chainageTo=1&pageSize=0")]
    public async Task Paging_InvalidParameters_Return400(string url)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task ChainageRange_CanBeLimitedToOneWorkspace()
    {
        var workspaceA = await _client.CreateWorkspaceAsync();
        var workspaceB = await _client.CreateWorkspaceAsync();
        var inA = await _client.CreateRecordAsync(workspaceA.Id, (await _client.CreateSectionAsync(workspaceA.Id)).Id, 1.0, 1.1);
        var inB = await _client.CreateRecordAsync(workspaceB.Id, (await _client.CreateSectionAsync(workspaceB.Id)).Id, 1.0, 1.1);

        var result = await _client.GetPageAsync<PavedRoadRecordDto>(
            $"{PavedRoadRecordsUrl}/chainage?chainageFrom=0&chainageTo=2&workspaceId={workspaceA.Id}&pageSize=500");

        Assert.Contains(result.Items, r => r.Id == inA.Id);
        Assert.DoesNotContain(result.Items, r => r.Id == inB.Id);
        Assert.All(result.Items, r => Assert.Equal(workspaceA.Id, r.WorkspaceId));
    }

    [Fact]
    public async Task CreateSection_UnknownWorkspace_Returns400()
    {
        var response = await _client.PostAsJsonAsync(RoadSectionsUrl, NewSection(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateRecord_UnknownWorkspace_Returns400()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);

        var response = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(Guid.NewGuid().ToString(), section.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Workspace", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateRecord_UnknownSection_Returns400()
    {
        var workspace = await _client.CreateWorkspaceAsync();

        var response = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspace.Id, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Road section", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateRecord_SectionFromAnotherWorkspace_Returns400()
    {
        var workspaceA = await _client.CreateWorkspaceAsync();
        var workspaceB = await _client.CreateWorkspaceAsync();
        var sectionInB = await _client.CreateSectionAsync(workspaceB.Id);

        var response = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspaceA.Id, sectionInB.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not belong to workspace", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Schema_UsesSnakeCaseColumns()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'paved_road_records'", connection);
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.Contains("chainage_from", columns);
        Assert.Contains("workspace_id", columns);
        Assert.Contains("image_paths", columns);
        Assert.DoesNotContain("ChainageFrom", columns);
    }
}
