using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RoadOps.Application.DTOs;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiAuthenticationTests(RoadOpsApiFactory factory)
{
    private HttpClient ClientWith(AuthenticationHeaderValue? authorization)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authorization;
        return client;
    }

    [Theory]
    [InlineData("GET", WorkspacesUrl)]
    [InlineData("POST", WorkspacesUrl)]
    [InlineData("GET", RoadSectionsUrl)]
    [InlineData("GET", PavedRoadRecordsUrl)]
    [InlineData("DELETE", WorkspacesUrl + "/any-id")]
    public async Task EveryEndpoint_RequiresAKey(string method, string url)
    {
        var anonymous = await ClientWith(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        var wrongKey = await ClientWith(new AuthenticationHeaderValue("Bearer", "not-a-key")).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
    }

    [Fact]
    public async Task XApiKeyHeader_IsAccepted()
    {
        var client = ClientWith(null);
        client.DefaultRequestHeaders.Add("X-Api-Key", RoadOpsApiFactory.ApiKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(WorkspacesUrl)).StatusCode);
    }

    [Fact]
    public async Task CreatedBy_ComesFromTheKey_NotTheRequestBody()
    {
        var client = factory.CreateClient();
        var workspace = NewWorkspace();
        workspace.CreatedBy = "someone-else";

        var created = (await (await client.PostAsJsonAsync(WorkspacesUrl, workspace)).Content.ReadFromJsonAsync<WorkspaceDto>())!;
        var section = NewSection(created.Id);
        section.CreatedBy = "someone-else";
        var createdSection = (await (await client.PostAsJsonAsync(RoadSectionsUrl, section)).Content.ReadFromJsonAsync<RoadSectionDto>())!;
        var record = NewRecord(created.Id, createdSection.Id);
        record.CreatedBy = "someone-else";
        var createdRecord = (await (await client.PostAsJsonAsync(PavedRoadRecordsUrl, record)).Content.ReadFromJsonAsync<PavedRoadRecordDto>())!;

        Assert.Equal(RoadOpsApiFactory.User, created.CreatedBy);
        Assert.Equal(RoadOpsApiFactory.User, createdSection.CreatedBy);
        Assert.Equal(RoadOpsApiFactory.User, createdRecord.CreatedBy);
    }
}
