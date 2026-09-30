using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiRateLimitTests(RoadOpsApiFactory factory)
{
    [Fact]
    public async Task Writes_HaveTheirOwnStricterLimit_AndAnonymousCallersAreLimitedByIp()
    {
        // A second host on the same database with low limits; the shared factory runs without limits.
        await using var limited = factory.WithWebHostBuilder(b => b
            .UseSetting("RateLimits:RequestsPerMinute", "3")
            .UseSetting("RateLimits:WritesPerMinute", "1")
            .UseSetting("RateLimits:AnonymousRequestsPerMinute", "1"));
        var client = limited.CreateClient();
        var anonymous = limited.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;

        var firstWrite = await client.PostAsJsonAsync(WorkspacesUrl, NewWorkspace());
        var secondWrite = await client.DeleteAsync($"{WorkspacesUrl}/missing");
        var reads = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) reads.Add((await client.GetAsync(WorkspacesUrl)).StatusCode);
        var anonymousStatuses = new[] { (await anonymous.GetAsync(WorkspacesUrl)).StatusCode, (await anonymous.GetAsync(WorkspacesUrl)).StatusCode };

        Assert.Equal(HttpStatusCode.Created, firstWrite.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondWrite.StatusCode);
        Assert.True(secondWrite.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", secondWrite.Content.Headers.ContentType!.MediaType);
        // Writes do not use up the read budget: 3 reads pass, the 4th is limited.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], reads);
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], anonymousStatuses);
    }
}
