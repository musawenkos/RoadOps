using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using RoadOps.Auth;
using Xunit;

namespace RoadOps.Tests.Shared;

/// <summary>
/// Hosts the real RoadOps REST API in memory against PostgreSQL (see <see cref="PostgresAppFactory{TEntryPoint}"/>),
/// with three API keys: <see cref="ApiKey"/> (admin), <see cref="EditorKey"/> and <see cref="ReaderKey"/> (no role set,
/// so reader by default). Clients from <c>CreateClient()</c> send the admin key; replace or clear the Authorization
/// header to test other roles or anonymous access.
/// </summary>
public sealed class RoadOpsApiFactory : PostgresAppFactory<Program>
{
    public const string ApiKey = "admin-test-key";
    public const string User = "api-tests";
    public const string EditorKey = "editor-test-key";
    public const string ReaderKey = "reader-test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Api:ApiKeys:0:User", User);
        builder.UseSetting("Api:ApiKeys:0:KeyHash", ApiKeyAuthenticationHandler.Hash(ApiKey));
        builder.UseSetting("Api:ApiKeys:0:Role", ApiKeyRoles.Admin);
        builder.UseSetting("Api:ApiKeys:1:User", "api-editor");
        builder.UseSetting("Api:ApiKeys:1:KeyHash", ApiKeyAuthenticationHandler.Hash(EditorKey));
        builder.UseSetting("Api:ApiKeys:1:Role", ApiKeyRoles.Editor);
        builder.UseSetting("Api:ApiKeys:2:User", "api-reader");
        builder.UseSetting("Api:ApiKeys:2:KeyHash", ApiKeyAuthenticationHandler.Hash(ReaderKey));

        // No limits for the functional and stress tests (0 = unlimited); ApiRateLimitTests turns them on.
        builder.UseSetting("RateLimits:RequestsPerMinute", "0");
        builder.UseSetting("RateLimits:WritesPerMinute", "0");
        builder.UseSetting("RateLimits:AnonymousRequestsPerMinute", "0");
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RoadOpsApiFactory>
{
    public const string Name = "RoadOps API";
}
