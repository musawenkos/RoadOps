using System.Net;
using System.Net.Http.Json;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PavedRoadRecordsApiTests(RoadOpsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_PersistsEveryFieldIncludingImagePaths()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var body = NewRecord(workspace.Id, section.Id, 10.0, 11.0);

        var create = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<PavedRoadRecordDto>())!.Id;

        var stored = (await _client.GetFromJsonAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/{id}"))!;
        Assert.Equal(body.ChainageFrom, stored.ChainageFrom);
        Assert.Equal(body.ChainageTo, stored.ChainageTo);
        Assert.Equal(body.DistressType, stored.DistressType);
        Assert.Equal(body.Degree, stored.Degree);
        Assert.Equal(body.Extent, stored.Extent);
        Assert.Equal(body.RutDepthMm, stored.RutDepthMm);
        Assert.Equal(body.Latitude, stored.Latitude);
        Assert.Equal(body.Longitude, stored.Longitude);
        Assert.Equal(body.ImagePaths, stored.ImagePaths);
        Assert.Equal(body.SurfaceType, stored.SurfaceType);
        Assert.Equal(body.Notes, stored.Notes);
        Assert.Equal(body.LengthM, stored.LengthM);
        Assert.Equal(body.WidthM, stored.WidthM);
        Assert.Equal(body.DepthMm, stored.DepthMm);
    }

    [Fact]
    public async Task Create_WithoutRecommendedAction_AppliesTheSharedRules()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var body = NewRecord(workspace.Id, section.Id);
        body.DistressType = "potholes";
        body.Degree = 4;
        body.RecommendedAction = "";

        var created = await _client.PostAndReadAsync<PavedRoadRecordDto>(PavedRoadRecordsUrl, body);

        Assert.Equal("Potholes", created.DistressType);
        Assert.Equal("Pothole repair – urgent (within 72h)", created.RecommendedAction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_DegreeOutOfRange_Returns400(int degree)
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var body = NewRecord(workspace.Id, section.Id);
        body.Degree = degree;

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(PavedRoadRecordsUrl, body)).StatusCode);
    }

    [Fact]
    public async Task Create_OutsideItsSection_Returns400()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id, 0, 10);

        var response = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspace.Id, section.Id, 12, 12.1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("outside section", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_ChangesRecord()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var record = await _client.CreateRecordAsync(workspace.Id, section.Id);

        var response = await _client.PutAsJsonAsync($"{PavedRoadRecordsUrl}/{record.Id}", new UpdatePavedRoadRecordDto
        {
            ChainageFrom = 20,
            ChainageTo = 20.1,
            SurfaceType = SurfaceType.Concrete,
            DistressType = "Cracking",
            Degree = 5,
            Extent = 4,
            ImagePaths = []
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await _client.GetFromJsonAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/{record.Id}"))!;
        Assert.Equal(20, stored.ChainageFrom);
        Assert.Equal(SurfaceType.Concrete, stored.SurfaceType);
        Assert.Equal("Cracking", stored.DistressType);
        Assert.Empty(stored.ImagePaths);
    }

    [Fact]
    public async Task Delete_RemovesRecord()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        var record = await _client.CreateRecordAsync(workspace.Id, section.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{PavedRoadRecordsUrl}/{record.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{PavedRoadRecordsUrl}/{record.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetBySectionAndWorkspace_ReturnRecordsOrderedByChainage()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section1 = await _client.CreateSectionAsync(workspace.Id, 0, 5);
        var section2 = await _client.CreateSectionAsync(workspace.Id, 5, 10);
        var r3 = await _client.CreateRecordAsync(workspace.Id, section2.Id, 6.0, 6.1);
        var r1 = await _client.CreateRecordAsync(workspace.Id, section1.Id, 1.0, 1.1);
        var r2 = await _client.CreateRecordAsync(workspace.Id, section1.Id, 2.0, 2.1);

        var bySection = await _client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/section/{section1.Id}");
        Assert.Equal([r1.Id, r2.Id], bySection.Items.Select(r => r.Id));

        var byWorkspace = await _client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/workspace/{workspace.Id}");
        Assert.Equal([r1.Id, r2.Id, r3.Id], byWorkspace.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task FindByChainageRange_ReturnsOnlyRecordsFullyInsideRange()
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);
        // Use a chainage band no other test touches so results are deterministic.
        var inside = await _client.CreateRecordAsync(workspace.Id, section.Id, 9000.2, 9000.4);
        var straddling = await _client.CreateRecordAsync(workspace.Id, section.Id, 9000.9, 9001.2);
        var outside = await _client.CreateRecordAsync(workspace.Id, section.Id, 9002.0, 9002.1);

        var result = await _client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/chainage?chainageFrom=9000&chainageTo=9001&pageSize=500");

        var ids = result.Items.Select(r => r.Id).ToList();
        Assert.Contains(inside.Id, ids);
        Assert.DoesNotContain(straddling.Id, ids);
        Assert.DoesNotContain(outside.Id, ids);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(-1, 1)]
    public async Task Create_InvalidChainage_Returns400(double from, double to)
    {
        var workspace = await _client.CreateWorkspaceAsync();
        var section = await _client.CreateSectionAsync(workspace.Id);

        var response = await _client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspace.Id, section.Id, from, to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindByChainageRange_InvertedRange_Returns400()
    {
        var response = await _client.GetAsync($"{PavedRoadRecordsUrl}/chainage?chainageFrom=10&chainageTo=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"{PavedRoadRecordsUrl}/{Guid.NewGuid()}", new UpdatePavedRoadRecordDto { ChainageFrom = 1, ChainageTo = 2, DistressType = "Rutting", Degree = 2, Extent = 2 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
