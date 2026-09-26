using RoadOps.Tests.Shared;

namespace RoadOps.StressTests;

/// <summary>
/// What the stress tests hit. By default the API is hosted in-process against a PostgreSQL
/// Testcontainer. Set STRESS_BASE_URL (e.g. http://localhost:5277) to load-test a running API instead.
/// </summary>
public sealed class StressTarget : IAsyncLifetime
{
    private RoadOpsApiFactory? _factory;

    public HttpClient Client { get; private set; } = null!;

    public string Description { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("STRESS_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            Client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
            Description = $"running API at {baseUrl}";
            return;
        }

        _factory = new RoadOpsApiFactory();
        await ((IAsyncLifetime)_factory).InitializeAsync();
        Client = _factory.CreateClient();
        Description = "in-process API + PostgreSQL container";
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null)
        {
            await ((IAsyncLifetime)_factory).DisposeAsync();
        }
    }
}
